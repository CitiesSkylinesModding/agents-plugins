import { afterEach, beforeEach, describe, expect, test } from 'bun:test';
import { access, chmod, mkdir, readFile, rm, utimes, writeFile } from 'node:fs/promises';
import path from 'node:path';
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { readCursor } from '../src/cursor';
import { TEST_CEILING_SECONDS, type ToolContext } from '../src/cycle';
import { ignoreError, status, testMod } from '../src/tools';
import { createFakeGame, type FakeGame, resultText } from './fake-game';

let game: FakeGame;
let context: ToolContext;

beforeEach(async () => {
  game = await createFakeGame();
  context = {
    host: game,
    userDataPath: game.userDataPath,
    localModsPath: path.join(game.userDataPath, 'Mods'),
    progress: () => {},
    slot: { running: undefined },
    signal: new AbortController().signal
  };
});

afterEach(async () => {
  await rm(game.userDataPath, { recursive: true, force: true });
});

/**
 * What the game logs of one scenario: `lines` between its start and its finish.
 */
async function logScenario(
  name: string,
  lines: ReadonlyArray<readonly [message: string, level?: string]> = []
): Promise<void> {
  const isFailure = lines.some(([, level]) => level == 'ERROR');
  const outcome = isFailure ? 'Failure' : 'Success';

  await game.log('TestScenarios', `---| START TEST SCENARIO: ${name}`);
  await game.log('TestScenarios', `---| START TEST: Counts`);

  for (const [message, level] of lines) {
    await game.log('TestScenarios', message, level);
  }

  await game.log(
    'TestScenarios',
    `---| FINISH TEST (${outcome}, exceptions: 0, errors: 0): Counts `
  );
  await game.log('TestScenarios', '[Metrics] Mono used: 471.13 MB');
  await game.log(
    'TestScenarios',
    `---| FINISH TEST SCENARIO (${outcome}, exceptions: 0, errors: 0): ${name} `
  );
}

/**
 * A launch whose game boots, runs what `scenarios` logs, finishes, and closes when asked.
 */
function runs(scenarios: () => Promise<void>): void {
  game.onClose = () => {
    game.after(5, async () => {
      await game.writeCleanShutdown();
      game.exit();
    });
  };

  game.onLaunch = () => {
    game.after(5, () => game.start());
    game.after(10, () => game.log('SceneFlow', 'Booting'));
    game.after(90, () => game.log('Automation', `Starting test scenarios.`));
    game.after(120, async () => {
      await scenarios();
      await game.log('Automation', `Finished test scenarios.`);
    });
  };
}

describe('test', () => {
  test(`returns each scenario's result and lines, and stops the game`, async () => {
    runs(() => logScenario('MyMod: Sample', [['  [PASSED] Buildings: counted 26']]));

    const result = await testMod(context, { mod: 'MyMod' });
    const text = resultText(result);

    expect(result.isError).toBeUndefined();
    expect(text).toStartWith(`PASSED: 1 scenarios`);
    expect(text).toContain(`MyMod: Sample: passed`);
    expect(text).toContain(`[PASSED] Buildings: counted 26`);
    expect(text).not.toContain(`[Metrics]`);
    expect(game.launches).toEqual(['--noSplash --categoryFilter=1048576']);
    expect(game.closeRequests).toHaveLength(1);
  });

  test(`fails on a failed scenario, keeping its lines`, async () => {
    runs(() =>
      logScenario('MyMod: Sample', [
        [`  [FAILED] Buildings: counted 11, the query holds 12`],
        [`TestScenarioException: 1 checks failed, see the lines above.`, 'ERROR']
      ])
    );

    const result = await testMod(context, { mod: 'MyMod' });
    const text = resultText(result);

    expect(result.isError).toBe(true);
    expect(text).toStartWith(`FAILED: 1 of 1 scenarios failed.`);
    expect(text).toContain(`MyMod: Sample: FAILED: the game reported a failure;`);
    expect(text).toContain(`[FAILED] Buildings: counted 11`);
    expect(text).toContain(`[ERROR] TestScenarioException: 1 checks failed`);
    expect(game.closeRequests).toHaveLength(1);
  });

  test(`fails a run that finished with no scenario, naming the three causes`, async () => {
    runs(() => Promise.resolve());

    const result = await testMod(context, { mod: 'MyMod' });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toStartWith(
      `FAILED: no scenario ran: a Release build, no scenario written, or a category mismatch.`
    );
  });

  test(`takes the same failure from a boot that never starts the scenarios`, async () => {
    runs(() => Promise.resolve());

    game.onLaunch = () => {
      game.after(5, () => game.start());
      game.after(10, () => game.log('SceneFlow', 'Booting'));
    };

    const result = await testMod(context, { mod: 'MyMod' });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`no scenario ran: a Release build,`);
    expect(game.seconds()).toBeGreaterThanOrEqual(600);
  });

  test(`reports a run that stalled once the scenarios started`, async () => {
    runs(() => Promise.resolve());

    game.onLaunch = () => {
      game.after(5, () => game.start());
      game.after(90, () => game.log('Automation', `Starting test scenarios.`));
    };

    const text = resultText(await testMod(context, { mod: 'MyMod' }));

    expect(text).toContain(`logged nothing for`);
    expect(text).not.toContain(`no scenario ran`);
  });

  test(`keeps a metrics entry logged at an error level, which fails its scenario`, async () => {
    runs(() => logScenario('MyMod: Sample', [[`[Metrics] Error logging metrics: boom`, 'ERROR']]));

    const text = resultText(await testMod(context, { mod: 'MyMod' }));

    expect(text).toContain(`[ERROR] [Metrics] Error logging metrics: boom`);
    expect(text).not.toContain(`Mono used`);
  });

  test(`reports the mod's load, as restart does`, async () => {
    runs(() => game.log('Modding', `Error initializing mod MyMod, MyMod.dll`, 'ERROR'));

    const text = resultText(await testMod(context, { mod: 'MyMod' }));

    expect(text).toContain(`Mod MyMod: Error initializing mod MyMod, MyMod.dll`);
  });

  test(`names the crash dump folder when the game died`, async () => {
    runs(() => Promise.resolve());

    game.onLaunch = () => {
      game.after(5, () => game.start());
      game.after(90, () => game.log('Automation', `Starting test scenarios.`));
      game.after(100, () => logStartOnly('MyMod: Sample'));
      game.after(110, () => {
        game.exit();
      });
    };

    const result = await testMod(context, { mod: 'MyMod' });
    const text = resultText(result);

    expect(result.isError).toBe(true);
    expect(text).toStartWith(`FAILED: the game died`);
    expect(text).toContain(`MyMod: Sample: FAILED: it did not finish.`);
    expect(text).toContain(path.join('.cache', 'backtrace', 'crashpad', 'reports'));
    expect(text).not.toContain(`no scenario ran`);
  });
});

describe('test, on errors outside the scenarios', () => {
  const systemError = `Crashed.\n  at MyMod.Systems.Foo.OnUpdate () [0x00012] in <000>:0`;

  const unityException = [
    `NullReferenceException: Object reference not set to an instance of an object`,
    '  at MyMod.Patches.Bar.Prefix () [0x00001] in <000>:0',
    'UnityEngine.DebugLogHandler:Internal_LogException(Exception, Object)',
    '',
    ''
  ].join('\n');

  test(`fails on an error naming the mod, even with every scenario passed`, async () => {
    runs(async () => {
      await game.log('SceneFlow', systemError, 'ERROR');
      await logScenario('MyMod: Sample');
    });

    const result = await testMod(context, { mod: 'MyMod' });
    const text = resultText(result);

    expect(result.isError).toBe(true);
    expect(text).toStartWith(`FAILED: the mod logged 1 errors.`);
    expect(text).toContain(`Logs/SceneFlow.log:`);
    expect(text).toContain(`at MyMod.Systems.Foo.OnUpdate`);
    expect(text).toContain(`MyMod: Sample: passed`);
  });

  test(`fails on an exception of the mod's that Unity logged`, async () => {
    runs(async () => {
      await game.append(game.playerLogPath, unityException);
      await logScenario('MyMod: Sample');
    });

    const result = await testMod(context, { mod: 'MyMod' });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`at MyMod.Patches.Bar.Prefix`);
  });

  test(`counts the same error under an ignore pattern`, async () => {
    await ignoreError(context, { pattern: 'Crashed' });

    runs(async () => {
      await game.log('SceneFlow', systemError, 'ERROR');
      await logScenario('MyMod: Sample');
    });

    const result = await testMod(context, { mod: 'MyMod' });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`Crashed.`);
  });

  test(`counts an ignore pattern's hits on what the result listed`, async () => {
    await ignoreError(context, { pattern: 'Crashed' });

    runs(async () => {
      await game.log('SceneFlow', systemError, 'ERROR');
      await logScenario('MyMod: Sample');
    });

    await testMod(context, { mod: 'MyMod' });

    expect(allText(await status(context))).toContain(`Crashed (1 hits)`);
  });

  test(`keeps the errors it counted when the cursor cannot move`, async () => {
    const toolFolder = path.join(game.userDataPath, '.cs2-dev');

    runs(async () => {
      await game.log('SceneFlow', systemError, 'ERROR');
      await logScenario('MyMod: Sample');
      await chmod(toolFolder, 0o555);
    });

    const text = resultText(await testMod(context, { mod: 'MyMod' }));

    await chmod(toolFolder, 0o755);

    expect(text).toStartWith(`FAILED: the mod logged 1 errors.`);
    expect(text).toContain(`at MyMod.Systems.Foo.OnUpdate`);
  });

  test(`leaves another mod's error to the hint, and passes`, async () => {
    runs(async () => {
      await game.log('Other', `Broken.`, 'ERROR');
      await logScenario('MyMod: Sample');
    });

    const result = await testMod(context, { mod: 'MyMod' });

    expect(result.isError).toBeUndefined();
    expect(allText(result)).toContain(
      `Errors: 1 new, 1 kind, 0 naming MyMod, 1 in Logs/Other.log.`
    );
  });

  test(`judges a scenario's own error once, on its lines`, async () => {
    runs(() =>
      logScenario('MyMod: Sample', [[`Failed.\n  at MyMod.Scenarios.Sample.Run ()`, 'ERROR']])
    );

    const text = resultText(await testMod(context, { mod: 'MyMod' }));

    expect(text).toStartWith(`FAILED: 1 of 1 scenarios failed.`);
  });

  test(`lists a storm one per kind, and announces it once`, async () => {
    runs(async () => {
      for (let frame = 0; frame < 500; frame++) {
        await game.log('SceneFlow', `Tick ${frame}.\n  at MyMod.Systems.Foo.Tick ()`, 'ERROR');
      }

      await game.log('Other', `Broken.`, 'ERROR');
      await logScenario('MyMod: Sample');
    });

    const result = await testMod(context, { mod: 'MyMod' });
    const text = allText(result);

    expect(text).toContain(`the mod logged 500 errors`);
    expect(text).toContain(`x500`);
    expect(text.match(/at MyMod\.Systems\.Foo\.Tick/gu)).toHaveLength(1);
    expect(text).toContain(`1 other errors name no MyMod: logs reads them.`);
    expect(text).not.toContain(`Errors: `);

    await game.log('SceneFlow', `Later.`, 'ERROR');

    expect(allText(await status(context))).toContain(
      `Errors: 1 new, 1 kind, 0 naming MyMod, 1 in Logs/SceneFlow.log.`
    );
  });

  test(`counts an error the game wrote just as the run finished, with the game kept open`, async () => {
    runs(async () => {
      await logScenario('MyMod: Sample');
      await game.log('SceneFlow', systemError, 'ERROR');
    });

    const result = await testMod(context, { mod: 'MyMod', keepOpen: true });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`the mod logged 1 errors`);
  });

  test(`leaves an error logged after its listing to the hint, with the game kept open`, async () => {
    // Mutable: counts the checks since the run's last line, the fourth following the listing.
    let checks = 0;

    runs(async () => {
      await game.log('SceneFlow', systemError, 'ERROR');
      await logScenario('MyMod: Sample');

      game.onFindPids = async () => {
        checks++;

        if (checks == 4) {
          await game.log('Other', `Slipped.`, 'ERROR');
          await game.log('Other', `Later.`);
        }
      };
    });

    const result = await testMod(context, { mod: 'MyMod', keepOpen: true });

    game.onFindPids = () => Promise.resolve();

    expect(allText(result) + allText(await status(context))).toContain(`Errors: 1 new, 1 kind`);
  });

  test(`says so when the errors cannot be counted, and never passes`, async () => {
    runs(() => logScenario('MyMod: Sample'));

    game.onClose = () => {
      game.findPidsFailure = `pgrep did not answer`;
    };

    const result = await testMod(context, { mod: 'MyMod' });
    const text = resultText(result);

    expect(result.isError).toBe(true);
    expect(text).toContain(`the mod's errors could not be counted: pgrep did not answer`);
    expect(text).toContain(`The game could not be stopped: pgrep did not answer`);
    expect(text).toContain(`MyMod: Sample: passed`);
  });
});

describe('test, around the run', () => {
  /**
   * A deployed build, and a source written `seconds` after it.
   */
  async function deployed(seconds: number): Promise<string> {
    const assembly = path.join(context.localModsPath, 'MyMod', 'MyMod.dll');
    const sources = path.join(game.userDataPath, 'sources');
    const source = path.join(sources, 'Systems', 'Foo.cs');
    const built = game.now();

    await mkdir(path.dirname(assembly), { recursive: true });
    await writeFile(assembly, 'build');
    await utimes(assembly, built, built);
    await mkdir(path.dirname(source), { recursive: true });
    await writeFile(source, 'code');
    await utimes(source, built, new Date(built.getTime() + seconds * 1000));

    return sources;
  }

  test(`warns when sources changed after the deployed build`, async () => {
    const sources = await deployed(60);

    runs(() => logScenario('MyMod: Sample'));

    const result = await testMod(context, { mod: 'MyMod', sources });
    const text = resultText(result);

    expect(result.isError).toBeUndefined();
    expect(text).toContain(`Warning: ${path.join('Systems', 'Foo.cs')} changed after the deployed`);
  });

  test(`says nothing of a build newer than its sources`, async () => {
    const sources = await deployed(-60);

    runs(() => logScenario('MyMod: Sample'));

    expect(resultText(await testMod(context, { mod: 'MyMod', sources }))).not.toContain(`Warning`);
  });

  test(`leaves build output out of the sources`, async () => {
    const sources = await deployed(-60);
    const output = path.join(sources, 'obj', 'Debug', 'Generated.cs');

    await mkdir(path.dirname(output), { recursive: true });
    await writeFile(output, 'generated');

    runs(() => logScenario('MyMod: Sample'));

    expect(resultText(await testMod(context, { mod: 'MyMod', sources }))).not.toContain(`Warning`);
  });

  test(`says when there is no deployed build to compare the sources with`, async () => {
    const sources = await deployed(60);

    await rm(context.localModsPath, { recursive: true });

    runs(() => Promise.resolve());

    const text = resultText(await testMod(context, { mod: 'MyMod', sources }));

    expect(text).toContain(`Warning: no deployed build at ${context.localModsPath}`);
  });

  test(`refuses a sources folder that does not exist, leaving a running game alone`, async () => {
    await game.start();

    const sources = path.join(game.userDataPath, 'nowhere');
    const result = await testMod(context, { mod: 'MyMod', sources });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(sources);
    expect(game.closeRequests).toEqual([]);
  });

  test(`refuses an option holding whitespace, leaving a running game alone`, async () => {
    await game.start();

    const result = await testMod(context, { mod: 'MyMod', gameOptions: ['--profile=a b'] });

    expect(result.isError).toBe(true);
    expect(game.closeRequests).toEqual([]);
  });

  test(`stops a running game first, keeping its evidence and telling of its errors`, async () => {
    await game.start();
    await game.log('SceneFlow', 'Booting');
    await game.log('Other', `From the last run.`, 'ERROR');

    runs(() => logScenario('MyMod: Sample'));

    game.onClose = () => {
      game.onClose = () => {};
      game.after(5, () => {
        game.exit();
      });
    };

    const result = await testMod(context, { mod: 'MyMod' });

    expect(resultText(result)).toStartWith(`The run this test ends: Errors: 1 new`);
    expect(
      await readFile(
        path.join(game.userDataPath, '.cs2-dev', 'previous-run', 'Logs', 'Other.log'),
        'utf8'
      )
    ).toContain(`From the last run.`);
  });

  test(`passes the category and the game options to the launch`, async () => {
    runs(() => logScenario('MyMod: Sample'));

    await testMod(context, { mod: 'MyMod', category: 32, gameOptions: ['--uiDeveloperMode'] });

    expect(game.launches).toEqual(['--noSplash --categoryFilter=32 --uiDeveloperMode']);
  });

  test(`keeps the mod for the hints`, async () => {
    runs(() => logScenario('MyMod: Sample'));

    await testMod(context, { mod: 'MyMod' });

    const { mod } = await readCursor(game.userDataPath);

    expect(mod).toBe('MyMod');
  });

  test(`leaves the game running when asked to, and says so`, async () => {
    runs(() => logScenario('MyMod: Sample'));

    const result = await testMod(context, { mod: 'MyMod', keepOpen: true });
    const text = resultText(result);

    expect(result.isError).toBeUndefined();
    expect(text).toContain(`The game is left running (PIDs 4242)`);
    expect(game.closeRequests).toEqual([]);
  });

  test(`leaves the game running when a read fails during the watch, and says so`, async () => {
    const log = path.join(game.userDataPath, 'Logs', 'TestScenarios.log');

    game.onLaunch = () => {
      game.after(5, () => game.start());
      game.after(90, () => game.log('Automation', `Starting test scenarios.`));
      game.after(100, async () => {
        await rm(log, { force: true });
        await mkdir(log);
      });
    };

    const result = await testMod(context, { mod: 'MyMod' });
    const text = resultText(result);

    expect(result.isError).toBe(true);
    expect(text).toContain(`Cannot read ${log}`);
    expect(text).toContain(`The game is left running (PIDs 4242)`);
    expect(game.closeRequests).toEqual([]);
  });

  test(`tells the caller of each scenario, and keeps talking through a silent one`, async () => {
    const messages: string[] = [];

    context = {
      ...context,
      progress: message => {
        messages.push(message);
      }
    };

    game.onClose = () => {
      game.after(5, () => {
        game.exit();
      });
    };

    game.onLaunch = () => {
      game.after(5, () => game.start());
      game.after(90, () => game.log('Automation', `Starting test scenarios.`));
      game.after(100, () => logStartOnly('MyMod: Sample'));
      game.after(250, () =>
        game.log(
          'TestScenarios',
          `---| FINISH TEST SCENARIO (Success, exceptions: 0, errors: 0): MyMod: Sample `
        )
      );
      game.after(260, () => game.log('Automation', `Finished test scenarios.`));
    };

    await testMod(context, { mod: 'MyMod' });

    expect(messages).toContain(`Running the scenario MyMod: Sample`);
    expect(
      messages.filter(message => message.startsWith(`Running the scenario MyMod: Sample, `)).length
    ).toBeGreaterThanOrEqual(2);
  });

  test(`cuts a flood of scenario lines, keeping what decides`, async () => {
    runs(async () => {
      await game.log('SceneFlow', `Crashed.\n  at MyMod.Systems.Foo.Run ()`, 'ERROR');
      await logScenario('MyMod: Sample', [['x'.repeat(30_000)]]);
    });

    const text = resultText(await testMod(context, { mod: 'MyMod' }));

    expect(text).toStartWith(`FAILED: the mod logged 1 errors.`);
    expect(text).toContain(`at MyMod.Systems.Foo.Run`);
    expect(text).toContain(`MyMod: Sample: passed`);
    expect(text).toContain(`[cut: `);
    expect(text.length).toBeLessThan(25_000);
  });

  test(`does not call dead a game that finished and quit between two checks`, async () => {
    game.onLaunch = () => {
      game.after(5, () => game.start());
      game.after(90, () => game.log('Automation', `Starting test scenarios.`));
      game.after(120, () => {
        game.onFindPids = async () => {
          game.onFindPids = () => Promise.resolve();

          await logScenario('MyMod: Sample');
          await game.log('Automation', `Finished test scenarios.`);

          game.exit();
        };
      });
    };

    const result = await testMod(context, { mod: 'MyMod' });

    expect(result.isError).toBeUndefined();
    expect(resultText(result)).toStartWith(`PASSED: 1 scenarios`);
  });

  test(`refuses to run beside another call holding the game`, async () => {
    context.slot.running = `restart (Launching the game)`;

    const result = await testMod(context, { mod: 'MyMod' });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`Another call holds the game: restart`);
    expect(game.launches).toEqual([]);
  });

  test(`ends at its ceiling when a step never settles, naming it`, async () => {
    game.onLaunch = () => {
      game.after(5, () => game.start());
      game.after(30, () => {
        game.stuckOn = 'findPids';
      });
    };

    const call = testMod(context, { mod: 'MyMod' });

    await game.reachedStuck;

    const before = game.seconds();

    await game.sleep((TEST_CEILING_SECONDS - before + 1) * 1000);

    const result = await call;

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(
      `Gave up after ${TEST_CEILING_SECONDS} s, something never finished, at this step: ` +
        `Waiting for the scenarios to start.`
    );
    expect(game.launches).toHaveLength(1);

    game.releaseOldest();
  });

  test(`deletes the options of a launch it gave up on`, async () => {
    game.stuckOn = 'launch';

    const call = testMod(context, { mod: 'MyMod' });

    await game.reachedStuck;

    expect(await exists(game.runOncePath)).toBe(true);

    await game.sleep((TEST_CEILING_SECONDS + 1) * 1000);
    await call;
    await waitFor(async () => !(await exists(game.runOncePath)));

    game.releaseOldest();
  });

  test(`ends at once when the caller cancels, doing nothing more to the game`, async () => {
    const cancellation = new AbortController();

    context = { ...context, signal: cancellation.signal };

    game.onLaunch = () => {
      game.after(5, () => game.start());
      game.after(30, () => {
        game.stuckOn = 'findPids';
      });
    };

    const call = testMod(context, { mod: 'MyMod' });

    await game.reachedStuck;

    cancellation.abort();

    expect(resultText(await call)).toContain(`Cancelled at this step: Waiting for the scenarios`);
    expect(game.closeRequests).toEqual([]);

    game.releaseOldest();
  });
});

/**
 * Waits for what a call left to run in the background.
 */
async function waitFor(condition: () => Promise<boolean>): Promise<void> {
  for (let attempt = 0; attempt < 100 && !(await condition()); attempt++) {
    await new Promise(resolve => {
      setTimeout(resolve, 5);
    });
  }

  expect(await condition()).toBe(true);
}

function exists(filePath: string): Promise<boolean> {
  return access(filePath).then(
    () => true,
    () => false
  );
}

function allText({ content }: CallToolResult): string {
  return content.map(block => (block.type == 'text' ? block.text : '')).join('\n');
}

function logStartOnly(name: string): Promise<void> {
  return game.log('TestScenarios', `---| START TEST SCENARIO: ${name}`);
}
