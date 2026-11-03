import { afterEach, beforeEach, describe, expect, spyOn, test } from 'bun:test';
import assert from 'node:assert/strict';
import {
  appendFile,
  lstat,
  mkdir,
  readdir,
  readFile,
  rm,
  symlink,
  utimes,
  writeFile
} from 'node:fs/promises';
import path from 'node:path';
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import {
  LOAD_SECONDS,
  LOADING_SCREEN_SECONDS,
  RESTART_CEILING_SECONDS,
  STOP_CEILING_SECONDS,
  type ToolContext,
  WAIT_LOAD_CEILING_SECONDS
} from '../src/cycle';
import { keepPreviousRun } from '../src/evidence';
import type { GameHost } from '../src/game-host';
import { runTool } from '../src/tool-run';
import { restart, stop, waitLoad } from '../src/tools';
import { createFakeGame, type FakeGame, resultText } from './fake-game';
import { storedZip } from './stored-zip';

let game: FakeGame;
let context: ToolContext;
let cancellation: AbortController;

beforeEach(async () => {
  game = await createFakeGame();
  cancellation = new AbortController();
  context = {
    host: game,
    userDataPath: game.userDataPath,
    localModsPath: path.join(game.userDataPath, 'Mods'),
    progress: () => {},
    slot: { running: undefined },
    signal: cancellation.signal
  };
});

afterEach(async () => {
  await rm(game.userDataPath, { recursive: true, force: true });
});

/**
 * The line the game logs for a mod that threw while loading: its slots hold the assembly's name
 * in full, and the mod's own display name in neither.
 */
function modError(mod: string): string {
  const assembly = `${mod}, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null`;

  return `Error initializing mod ${assembly} (${assembly})`;
}

/**
 * The evidence copies in the tool's folder, finished or not.
 */
async function copies(): Promise<string[]> {
  const names = await readdir(path.join(game.userDataPath, '.cs2-dev'));

  return names.filter(name => name.startsWith('previous-run'));
}

async function failureOf(call: Promise<unknown>): Promise<string> {
  try {
    await call;
  } catch (error) {
    return String(error);
  }

  return '';
}

/**
 * A game running since boot, whose close takes `seconds` and writes its shutdown lines.
 */
async function runningGame(seconds = 14): Promise<void> {
  await game.start();

  game.onClose = () => {
    game.after(seconds, async () => {
      await game.writeCleanShutdown();
      game.exit();
    });
  };
}

/**
 * A launch that starts the game in 5 s and reaches the main menu `seconds` later.
 */
function bootsToMenu(seconds = 90): void {
  game.onLaunch = () => {
    game.after(5, () => game.start());
    game.after(5 + seconds, () => game.log('SceneFlow', 'MainMenu reached'));
  };
}

/**
 * A launch that starts the game, and loads what it was told to in `seconds`.
 */
function bootsAndLoads(seconds = 150): void {
  game.onLaunch = () => {
    game.after(5, () => game.start());
    game.after(40, () => game.log('SceneFlow', 'Loading mode Game with purpose LoadGame'));
    game.after(5 + seconds, () => game.log('SceneFlow', 'Loading completed'));
  };
}

/**
 * Writes a user asset the way the game does, its guid an entry of the archive.
 */
async function writeAsset(folder: string, name: string, type: string, guid: string): Promise<void> {
  const directory = path.join(game.userDataPath, folder, '76561198000000000');

  await mkdir(directory, { recursive: true });
  await writeFile(
    path.join(directory, `${name}.cok`),
    storedZip({ 'preview.png': 'pixels', [`${name}.${type}.cid`]: guid })
  );
}

describe('stop', () => {
  test(`says so on a stopped game, and asks nothing`, async () => {
    const result = await stop(context);

    expect(result.isError).toBeUndefined();
    expect(resultText(result)).toContain(`not running`);
    expect(game.closeRequests).toEqual([]);
  });

  test(`reports a clean close, and forces nothing`, async () => {
    await runningGame();

    const result = await stop(context);

    expect(resultText(result)).toContain(`closed cleanly`);
    expect(game.closeRequests).toHaveLength(1);
    expect(game.forces).toEqual([]);
    expect(game.seconds()).toBeLessThan(16);
  });

  test(`waits for Steam's reaper, which holds a stopped game for Steam`, async () => {
    await runningGame();

    game.onClose = () => {
      game.after(14, async () => {
        await game.writeCleanShutdown();
        game.exit([4200]);
      });
      game.after(20, () => {
        game.exit();
      });
    };

    await stop(context);

    expect(game.seconds()).toBeGreaterThanOrEqual(20);
  });

  test(`does not call clean an end without the game's shutdown lines`, async () => {
    await game.start();

    game.onClose = () => {
      game.after(3, () => {
        game.exit();
      });
    };

    const text = resultText(await stop(context));

    expect(text).not.toContain(`closed cleanly`);
    expect(text).toContain(`no clean shutdown`);
  });

  test(`does not take the last run's lines in SceneFlow.log for this close`, async () => {
    await game.start();
    await game.log('SceneFlow', 'GameManager destroyed (11722.1ms)');

    game.onClose = () => {
      game.after(3, () => {
        game.exit();
      });
    };

    expect(resultText(await stop(context))).toContain(`no clean shutdown`);
  });

  test(`does not call clean an end missing GameManager destroyed`, async () => {
    await game.start();

    game.onClose = () => {
      game.after(3, async () => {
        await appendFile(game.playerLogPath, 'Game terminated successfully\n');
        game.exit();
      });
    };

    expect(resultText(await stop(context))).toContain(`no clean shutdown`);
  });

  test(`waits the whole bound when the close never left, for a game quitting alone`, async () => {
    await runningGame();

    game.closeFailure = `no config_info`;
    game.onClose = () => {
      game.after(50, async () => {
        await game.writeCleanShutdown();
        game.exit();
      });
    };

    const text = resultText(await stop(context));

    expect(game.forces).toEqual([]);
    expect(text).toContain(`closed cleanly`);
    expect(text).not.toContain('Application.Quit');
  });

  test(`forces a game that outlives the bound, not a second before`, async () => {
    await game.start();

    const result = await stop(context);
    const text = resultText(result);

    expect(game.forces).toEqual(['terminate']);
    expect(game.seconds()).toBeGreaterThanOrEqual(60);
    expect(game.seconds()).toBeLessThan(65);
    expect(result.isError).toBeUndefined();
    expect(text).toContain('forced');
    expect(text).not.toContain('Application.Quit');
  });

  test(`names the eval quit when a forced end follows a close that never left`, async () => {
    await game.start();

    game.closeFailure = `wine did not run`;

    const text = resultText(await stop(context));

    expect(text).toContain('forced');
    expect(text).toContain(`wine did not run`);
    expect(text).toContain('UnityEngine.Application.Quit()');
  });

  test(`kills a game that a request to end does not reach`, async () => {
    await game.start();

    game.onForce = stage => {
      if (stage == 'kill') {
        game.exit();
      }
    };

    await stop(context);

    expect(game.forces).toEqual(['terminate', 'kill']);
  });

  test(`fails on a game that will not die, naming what is left`, async () => {
    await game.start();

    game.onForce = () => {};

    const result = await stop(context);

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain('4242');
    expect(resultText(result)).toContain('4200');
  });

  test(`reports where to look when the user data folder is missing`, async () => {
    const result = await stop({ ...context, userDataPath: `${game.userDataPath}/nowhere` });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain('CSII_USERDATAPATH');
  });
});

describe('restart', () => {
  test(`boots to the main menu, and names the options it wrote`, async () => {
    bootsToMenu();

    const result = await restart(context, { gameOptions: ['--uiDeveloperMode'] });
    const text = resultText(result);

    expect(result.isError).toBeUndefined();
    expect(game.launches).toEqual(['--noSplash --uiDeveloperMode']);
    expect(text).toContain(`main menu`);
    expect(text).toContain(`--noSplash --uiDeveloperMode`);
    expect(game.seconds()).toBeGreaterThanOrEqual(95);
  });

  test(`does not take the last run's main menu for this one's`, async () => {
    await game.log('SceneFlow', 'MainMenu reached');
    await game.sleep(1000);
    bootsToMenu(200);

    await restart(context, {});

    expect(game.seconds()).toBeGreaterThanOrEqual(205);
  });

  test(`stops a running game itself, and says how it ended`, async () => {
    await runningGame();
    await game.log('SceneFlow', 'GameManager created!');
    bootsToMenu();

    const text = resultText(await restart(context, {}));

    expect(game.closeRequests).toHaveLength(1);
    expect(game.launches).toHaveLength(1);
    expect(text).toContain(`closed cleanly`);
  });

  test(`loads a save by name, its guid read from the save`, async () => {
    await writeAsset('Saves', 'My City', 'SaveGameMetadata', '0123456789abcdef0123456789abcdef');
    bootsAndLoads();

    const text = resultText(await restart(context, { save: 'My City' }));

    expect(game.launches).toEqual(['--noSplash --startGame=0123456789abcdef0123456789abcdef']);
    expect(text).toContain('loaded');
  });

  test(`does not take the last run's load for this one's`, async () => {
    await writeAsset('Saves', 'My City', 'SaveGameMetadata', 'a'.repeat(32));
    await game.log('SceneFlow', 'Loading mode Game with purpose LoadGame');
    await game.log('SceneFlow', 'Loading completed');
    await game.sleep(100_000);
    bootsAndLoads(150);

    await restart(context, { save: 'My City' });

    expect(game.seconds()).toBeGreaterThanOrEqual(275);
  });

  test(`waits out the loading screen after the load`, async () => {
    await writeAsset('Saves', 'My City', 'SaveGameMetadata', 'a'.repeat(32));
    bootsAndLoads(150);

    await restart(context, { save: 'My City' });

    expect(game.seconds()).toBeGreaterThanOrEqual(175);
  });

  test(`starts a new city on Archipelago by default`, async () => {
    bootsAndLoads();

    await restart(context, { newCity: {} });

    expect(game.launches).toEqual(['--noSplash --startGame=bf8036b291428535b986c757fda3e627']);
  });

  test(`starts a new city on a map given by guid`, async () => {
    bootsAndLoads();

    await restart(context, { newCity: { map: 'b'.repeat(32) } });

    expect(game.launches).toEqual([`--noSplash --startGame=${'b'.repeat(32)}`]);
  });

  test(`starts a new city on a user map given by name`, async () => {
    await writeAsset('Maps', 'Fjords', 'MapMetadata', 'c'.repeat(32));
    bootsAndLoads();

    await restart(context, { newCity: { map: 'Fjords' } });

    expect(game.launches).toEqual([`--noSplash --startGame=${'c'.repeat(32)}`]);
  });

  test(`fails on an unknown save without touching a running game`, async () => {
    await runningGame();

    const result = await restart(context, { save: 'Nowhere' });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain('Nowhere');
    expect(game.closeRequests).toEqual([]);
  });

  test(`refuses a save and a new city at once`, async () => {
    await runningGame();

    const result = await restart(context, { save: 'My City', newCity: {} });

    expect(resultText(result)).toContain(`not both`);
    expect(game.closeRequests).toEqual([]);
  });

  describe('a launch that never starts the game', () => {
    test(`retries, then fails naming Steam`, async () => {
      const result = await restart(context, {});

      expect(result.isError).toBe(true);
      expect(game.launches).toHaveLength(3);
      expect(resultText(result)).toContain('Steam');
    });

    test(`deletes runOnce.txt, so the next launch does not inherit its options`, async () => {
      await restart(context, { gameOptions: ['--uiDeveloperMode'] });

      expect(game.launches).toHaveLength(3);
      expect(await readFile(game.runOncePath, 'utf8').catch(() => 'deleted')).toBe('deleted');
    });

    test(`stops the reaper it started, which would make Steam ignore the next launch`, async () => {
      game.onLaunch = () => {
        game.pids = { game: [], reapers: [4200] };
      };

      await restart(context, {});

      expect(game.pids).toEqual({ game: [], reapers: [] });
      expect(game.forces).toEqual(['terminate', 'terminate', 'terminate']);
    });

    test(`clears what a failed launch left before trying again`, async () => {
      game.onLaunch = attempt => {
        if (attempt == 1) {
          game.pids = { game: [], reapers: [4100] };
        } else {
          game.after(5, () => game.start());
          game.after(95, () => game.log('SceneFlow', 'MainMenu reached'));
        }
      };

      const result = await restart(context, {});

      expect(result.isError).toBeUndefined();
      expect(game.launches).toHaveLength(2);
      expect(game.forces).toEqual(['terminate']);
    });
  });

  test(`fails at once on a game dying during boot, whatever its reaper does`, async () => {
    game.onLaunch = () => {
      game.after(5, () => game.start());
      game.after(30, () => {
        game.exit([4200]);
      });
    };

    const result = await restart(context, {});

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`The game stopped`);
    expect(game.seconds()).toBeLessThan(40);
  });

  test(`leaves a game whose boot hangs running, and says so`, async () => {
    game.onLaunch = () => {
      game.after(5, () => game.start());
    };

    const result = await restart(context, {});

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`left running`);
    expect(resultText(result)).toContain('4242');
    expect(game.pids.game).toEqual([4242]);
    expect(game.forces).toEqual([]);
    expect(game.seconds()).toBeGreaterThanOrEqual(600);
    expect(game.seconds()).toBeLessThan(620);
  });

  test(`fails a load that fell back to the main menu, and leaves the game running`, async () => {
    await writeAsset('Saves', 'Broken', 'SaveGameMetadata', 'a'.repeat(32));

    game.onLaunch = () => {
      game.after(5, () => game.start());
      game.after(40, () => game.log('SceneFlow', 'Loading mode Game with purpose LoadGame'));
      game.after(60, () => game.log('SceneFlow', 'Loading mode MainMenu with purpose Cleanup'));
      game.after(61, () => game.log('SceneFlow', 'Loading completed'));
      game.after(62, () => game.log('SceneFlow', 'MainMenu reached'));
    };

    const result = await restart(context, { save: 'Broken' });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`main menu`);
    expect(resultText(result)).toContain(`left running`);
    expect(game.seconds()).toBeLessThan(70);
  });

  test(`fails when the city was left during the loading screen`, async () => {
    await writeAsset('Saves', 'My City', 'SaveGameMetadata', 'a'.repeat(32));
    bootsAndLoads(150);

    const { onLaunch } = game;

    game.onLaunch = attempt => {
      onLaunch(attempt);
      game.after(160, () => game.log('SceneFlow', 'Loading mode MainMenu with purpose Cleanup'));
      game.after(161, () => game.log('SceneFlow', 'Loading completed'));
      game.after(162, () => game.log('SceneFlow', 'MainMenu reached'));
    };

    const result = await restart(context, { save: 'My City' });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`main menu`);
  });

  test(`fails when the game dies during the loading screen`, async () => {
    await writeAsset('Saves', 'My City', 'SaveGameMetadata', 'a'.repeat(32));
    bootsAndLoads(150);

    const { onLaunch } = game;

    game.onLaunch = attempt => {
      onLaunch(attempt);
      game.after(165, () => {
        game.exit();
      });
    };

    const result = await restart(context, { save: 'My City' });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`The game stopped`);
  });

  describe('given a mod', () => {
    const burst = 'Loaded additional Burst code';

    function logsMods(): void {
      const { onLaunch } = game;

      game.onLaunch = attempt => {
        onLaunch(attempt);
        game.after(10, async () => {
          await game.log('Modding', `${burst} C:/users/steamuser/Mods/Other/Other_win_x86_64.dll`);
          await game.log(
            'Modding',
            `${burst} C:/users/steamuser/.cache/Mods/pdx_mods/74285_22/MyMod_win_x86_64.dll`
          );
        });
      };
    }

    test(`names the Burst code logged for its assembly`, async () => {
      bootsToMenu();
      logsMods();

      const text = resultText(await restart(context, { mod: 'MyMod' }));

      expect(text).toContain('pdx_mods/74285_22/MyMod_win_x86_64.dll');
      expect(text).not.toContain('Other_win_x86_64.dll');
    });

    test(`says when the log holds no Burst code for it`, async () => {
      bootsToMenu();
      logsMods();

      const text = resultText(await restart(context, { mod: 'Absent' }));

      expect(text).toContain(`No Burst code`);
    });

    test(`reports its load, and no other mod's, claiming no clean init`, async () => {
      bootsToMenu();
      logsMods();

      const { onLaunch } = game;

      game.onLaunch = attempt => {
        onLaunch(attempt);
        game.after(10, async () => {
          await game.log(
            'Modding',
            `Loaded MyModTools, Version=1.0.0.0, PublicKeyToken=null in 5ms`
          );
          await game.log(
            'Modding',
            `Loaded MyMod, Version=2026.0.0.0, PublicKeyToken=null in 66.7ms`
          );
        });
      };

      const text = resultText(await restart(context, { mod: 'MyMod' }));

      expect(text).toContain(`Mod MyMod: Modding.log logs its load, in 66.7ms, and no error.`);
      expect(text).not.toContain(`initialized`);
      expect(text).not.toContain(`5ms`);
    });

    test(`reads its load under the stack trace the logger may write`, async () => {
      bootsToMenu();

      const stack = `\n  at Game.Modding.ModManager.InitializeMods ()`;

      const { onLaunch } = game;

      game.onLaunch = attempt => {
        onLaunch(attempt);
        game.after(10, async () => {
          await game.log(
            'Modding',
            `${burst} C:/users/steamuser/Mods/MyMod/MyMod_win_x86_64.dll${stack}`
          );
          await game.log(
            'Modding',
            `Loaded MyMod, Version=1.0.0.0, PublicKeyToken=null in 66.7ms${stack}`
          );
        });
      };

      const text = resultText(await restart(context, { mod: 'MyMod' }));

      expect(text).toContain(
        `Mod MyMod: Burst code loaded from C:/users/steamuser/Mods/MyMod/MyMod_win_x86_64.dll\n`
      );
      expect(text).toContain(`Mod MyMod: Modding.log logs its load, in 66.7ms, and no error.`);
    });

    test(`says when the log holds no load of it`, async () => {
      bootsToMenu();
      logsMods();

      const text = resultText(await restart(context, { mod: 'Absent' }));

      expect(text).toContain(`Mod Absent: Modding.log logs no load of it since the launch.`);
    });

    test(`reports its load error, the stack left to the logs`, async () => {
      bootsToMenu();

      const { onLaunch } = game;

      game.onLaunch = attempt => {
        onLaunch(attempt);
        game.after(10, () =>
          game.log('Modding', `${modError('MyMod')}\n  at MyMod.Mod.OnLoad ()`, 'ERROR')
        );
        game.after(11, () => game.log('Modding', modError('Other'), 'ERROR'));
      };

      const result = await restart(context, { mod: 'MyMod' });
      const text = resultText(result);

      expect(result.isError).toBeUndefined();
      expect(text).toContain(modError('MyMod'));
      expect(text).not.toContain('OnLoad');
      expect(text).not.toContain(`mod Other`);
    });

    test(`calls a load that threw a failure, as the game logs its time either way`, async () => {
      bootsToMenu();

      const { onLaunch } = game;

      game.onLaunch = attempt => {
        onLaunch(attempt);
        game.after(10, async () => {
          await game.log('Modding', `Loaded MyMod, Version=1.0.0.0, PublicKeyToken=null in 60.1ms`);
          await game.log('Modding', modError('MyMod'), 'ERROR');
        });
      };

      const text = resultText(await restart(context, { mod: 'MyMod' }));

      expect(text).toContain(`Mod MyMod: initialization failed after 60.1ms:`);
      expect(text).not.toContain(`with no error`);
    });

    test(`does not report the last run's error`, async () => {
      await game.log('Modding', modError('MyMod'), 'ERROR');
      await game.sleep(1000);
      bootsToMenu();

      expect(resultText(await restart(context, { mod: 'MyMod' }))).not.toContain('Error');
    });

    test(`says nothing of a mod it was not given`, async () => {
      bootsToMenu();
      logsMods();

      expect(resultText(await restart(context, {}))).not.toContain('Burst');
    });
  });

  describe('the evidence of the previous run', () => {
    const copy = (...parts: readonly string[]): string =>
      path.join(game.userDataPath, '.cs2-dev', 'previous-run', ...parts);

    const reportsPath = (): string =>
      path.join(game.userDataPath, '.cache', 'backtrace', 'crashpad', 'reports');

    /**
     * A signal that aborts just past the sweep's check, as for a call ending during its copy.
     */
    function endingPastTheSweep(): { signal: AbortSignal; passed: Promise<void> } {
      const ended = new AbortController();
      const { promise: passed, resolve } = Promise.withResolvers<void>();

      spyOn(ended.signal, 'throwIfAborted').mockImplementationOnce(() => {
        ended.abort();
        resolve();
      });

      return { signal: ended.signal, passed };
    }

    /**
     * A previous run's logs and two crash reports, one written during it and one before it.
     */
    async function previousRun(): Promise<void> {
      await game.start();
      await game.log('SceneFlow', 'Boot completed');
      await game.log('MyMod', 'the line to keep', 'ERROR');
      await writeFile(game.playerLogPath, 'the ended run\n');
      await mkdir(reportsPath(), { recursive: true });
      await writeFile(path.join(reportsPath(), 'this-run.dmp'), 'dump');
      await writeFile(path.join(reportsPath(), 'older.dmp'), 'old dump');

      const before = new Date(game.now().getTime() - 3_600_000);

      await utimes(path.join(reportsPath(), 'older.dmp'), before, before);
      await utimes(path.join(reportsPath(), 'this-run.dmp'), game.now(), game.now());
      game.exit();
    }

    test(`is copied before the launch overwrites it`, async () => {
      await previousRun();
      bootsToMenu();

      await restart(context, {});

      expect(await readFile(copy('Logs', 'MyMod.log'), 'utf8')).toContain(`the line to keep`);
      expect(await readFile(copy('Player.log'), 'utf8')).toBe('the ended run\n');
    });

    test(`holds the crash reports the ended run wrote, and only those`, async () => {
      await previousRun();
      bootsToMenu();

      await restart(context, {});

      expect(await readdir(copy('crashpad', 'reports'))).toEqual(['this-run.dmp']);
    });

    test(`comes after the stop, since a dying game still writes`, async () => {
      await game.start();
      await game.log('SceneFlow', 'Boot completed');

      game.onClose = () => {
        game.after(14, async () => {
          await game.log('MyMod', 'a last word', 'ERROR');
          game.exit();
        });
      };

      bootsToMenu();

      await restart(context, {});

      expect(await readFile(copy('Logs', 'MyMod.log'), 'utf8')).toContain(`a last word`);
    });

    test(`survives a crash report that vanishes under it`, async () => {
      await previousRun();
      await symlink('/nowhere', path.join(reportsPath(), 'pruned.dmp'));
      bootsToMenu();

      const result = await restart(context, {});

      expect(result.isError).toBeUndefined();
      expect(await readdir(copy('crashpad', 'reports'))).toEqual(['this-run.dmp']);
    });

    test(`fails on a crash report it cannot copy, keeping the last copy whole`, async () => {
      await previousRun();
      bootsToMenu();
      await restart(context, {});

      game.exit();
      await mkdir(path.join(reportsPath(), 'unreadable.dmp'));
      bootsToMenu();

      const launches = game.launches.length;
      const result = await restart(context, {});

      expect(result.isError).toBe(true);
      expect(game.launches).toHaveLength(launches);
      expect(await readdir(copy('crashpad', 'reports'))).toEqual(['this-run.dmp']);
    });

    test(`fails on a log gone during its copy, keeping the last copy whole`, async () => {
      await previousRun();
      bootsToMenu();
      await restart(context, {});

      game.exit();
      await symlink(path.join(game.userDataPath, 'nowhere'), game.logPath('Gone'));
      bootsToMenu();

      const launches = game.launches.length;
      const result = await restart(context, {});

      expect(result.isError).toBe(true);
      expect(game.launches).toHaveLength(launches);
      expect(await readFile(copy('Logs', 'MyMod.log'), 'utf8')).toContain('the line to keep');
    });

    test(`copies the bytes of a linked log, which the launch would replace`, async () => {
      await previousRun();

      const target = path.join(game.userDataPath, 'Player.target.log');

      await rm(game.playerLogPath);
      await writeFile(target, 'the ended run\n');
      await symlink(target, game.playerLogPath);
      bootsToMenu();
      await restart(context, {});

      const copied = await lstat(copy('Player.log'));

      expect(copied.isSymbolicLink()).toBe(false);
      expect(await readFile(copy('Player.log'), 'utf8')).toBe('the ended run\n');
    });

    test(`copies nothing into the last copy once its call ended`, async () => {
      await previousRun();
      bootsToMenu();
      await restart(context, {});

      await rm(game.logPath('MyMod'));

      const { signal } = endingPastTheSweep();

      expect(await failureOf(keepPreviousRun(game.userDataPath, signal))).not.toBe('');
      expect(await readFile(copy('Logs', 'MyMod.log'), 'utf8')).not.toBe('');
      expect(await copies()).toEqual(['previous-run']);
    });

    test(`clears what a killed server left of a copy`, async () => {
      await previousRun();

      const stale = path.join(game.userDataPath, '.cs2-dev', 'previous-run.next-stale');

      await mkdir(stale, { recursive: true });
      await writeFile(path.join(stale, 'Player.log'), 'a partial copy');
      bootsToMenu();
      await restart(context, {});

      expect(await copies()).toEqual(['previous-run']);
    });

    test(`shares nothing with a copy its call left behind`, async () => {
      await previousRun();

      for (let round = 0; round < 30; round++) {
        const { signal, passed } = endingPastTheSweep();
        const left = failureOf(keepPreviousRun(game.userDataPath, signal));

        await passed;

        expect(
          await failureOf(keepPreviousRun(game.userDataPath, new AbortController().signal))
        ).toBe('');

        await left;
      }

      expect(await readFile(copy('Logs', 'MyMod.log'), 'utf8')).toContain(`the line to keep`);
    });

    test(`leaves a newer call's copy alone once its own call ended`, async () => {
      const live = path.join(game.userDataPath, '.cs2-dev', 'previous-run.next-live');

      const ended = new AbortController();

      await mkdir(live, { recursive: true });

      const left = failureOf(keepPreviousRun(game.userDataPath, ended.signal));

      ended.abort();
      await left;

      expect(await readdir(path.dirname(live))).toEqual(['previous-run.next-live']);
    });

    test(`replaces the last copy`, async () => {
      await previousRun();
      bootsToMenu();
      await restart(context, {});

      await rm(game.logPath('MyMod'));
      game.exit();
      bootsToMenu();
      await restart(context, {});

      expect(await readFile(copy('Logs', 'MyMod.log'), 'utf8').catch(() => 'deleted')).toBe(
        'deleted'
      );
    });

    test(`is not needed for a first launch`, async () => {
      await rm(path.join(game.userDataPath, 'Logs'), { recursive: true });
      bootsToMenu();

      const result = await restart(context, {});

      expect(result.isError).toBeUndefined();
    });
  });

  test(`says where the call is at`, async () => {
    const messages: string[] = [];

    bootsToMenu();

    await restart(
      {
        ...context,
        progress: message => {
          messages.push(message);
        }
      },
      {}
    );

    expect(messages.length).toBeGreaterThan(1);
  });
});

describe('restart options', () => {
  test(`refuses an option holding a space and leaves the running game alone`, async () => {
    await game.start();

    const option = `--profile=/Colossal Order/p.raw`;
    const result = await restart(context, { gameOptions: [option] });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(option);
    expect(game.closeRequests).toEqual([]);
    expect(game.launches).toEqual([]);
  });

  test(`refuses an option holding a newline, which the game drops with no word`, async () => {
    await game.start();

    const option = '--uiDeveloperMode\n';
    const result = await restart(context, { gameOptions: [option] });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(JSON.stringify(option));
    expect(resultText(result)).toContain(`keeps any other whitespace`);
    expect(game.closeRequests).toEqual([]);
  });
});

describe('log reads', () => {
  test(`fail a wait on a log they cannot read, naming why`, async () => {
    await game.start();
    await rm(game.logPath('SceneFlow'), { force: true });
    await mkdir(game.logPath('SceneFlow'));

    const result = await waitLoad(context);

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(game.logPath('SceneFlow'));
    expect(resultText(result)).toContain(`EISDIR`);
    expect(game.seconds()).toBeLessThan(5);
  });

  test(`leave a stop saying the game ended and why its logs told nothing`, async () => {
    await game.start();
    await rm(game.playerLogPath);
    await mkdir(game.playerLogPath);
    game.onClose = () => {
      game.exit();
    };

    const result = await stop(context);

    expect(result.isError).toBeUndefined();
    expect(resultText(result)).toContain(`The game ended in`);
    expect(resultText(result)).toContain(game.playerLogPath);
    expect(resultText(result)).toContain(`EISDIR`);
  });

  test(`take a missing Player.log for no clean shutdown`, async () => {
    await game.start();
    await game.writeCleanShutdown();
    await rm(game.playerLogPath);
    game.onClose = () => {
      game.exit();
    };

    const result = await stop(context);

    expect(result.isError).toBeUndefined();
    expect(resultText(result)).toContain(`no clean shutdown`);
  });

  test(`fail a restart before its launch, naming the log it cannot get past`, async () => {
    await rm(game.logPath('SceneFlow'), { force: true });
    await mkdir(game.logPath('SceneFlow'));

    const result = await restart(context, {});

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(game.logPath('SceneFlow'));
    expect(game.launches).toEqual([]);
  });

  test(`leave a booted restart its result when the mod's log cannot be read`, async () => {
    await mkdir(game.logPath('Modding'));
    bootsToMenu();

    const result = await restart(context, { mod: 'MyMod' });

    expect(result.isError).toBeUndefined();
    expect(resultText(result)).toContain(`The game is at the main menu`);
    expect(resultText(result)).toContain(`Options written`);
    expect(resultText(result)).toContain(`Mod MyMod: Cannot read ${game.logPath('Modding')}`);
  });
});

describe('waitLoad', () => {
  test(`fails on a stopped game, whatever the last run logged`, async () => {
    await game.log('SceneFlow', 'Loading mode Game with purpose LoadGame');
    await game.log('SceneFlow', 'Loading completed');

    const result = await waitLoad(context);

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`not running`);
  });

  test(`returns once the load in progress completed, the loading screen included`, async () => {
    await game.start();
    await game.log('SceneFlow', 'Loading mode Game with purpose LoadGame');

    game.after(30, () => game.log('SceneFlow', 'Loading completed'));

    const result = await waitLoad(context);

    expect(result.isError).toBeUndefined();
    expect(game.seconds()).toBeGreaterThanOrEqual(50);
    expect(game.seconds()).toBeLessThan(55);
  });

  test(`waits for the city load that follows a load of the main menu`, async () => {
    await game.start();
    await game.log('SceneFlow', 'Loading mode MainMenu with purpose Cleanup');
    await game.log('SceneFlow', 'Loading completed');
    await game.log('SceneFlow', 'MainMenu reached');

    game.after(10, () => game.log('SceneFlow', 'Loading mode Game with purpose LoadGame'));
    game.after(40, () => game.log('SceneFlow', 'Loading completed'));

    const result = await waitLoad(context);

    expect(result.isError).toBeUndefined();
    expect(game.seconds()).toBeGreaterThanOrEqual(60);
  });

  test(`returns at once past the screen of a load already done`, async () => {
    await game.start();
    await game.log('SceneFlow', 'Loading mode Game with purpose LoadGame');
    await game.log('SceneFlow', 'Loading completed');
    await game.sleep(100_000);

    const before = game.now();

    await waitLoad(context);

    expect(game.seconds(before)).toBeLessThan(2);
  });

  test(`fails a load that fell back to the main menu`, async () => {
    await game.start();
    await game.log('SceneFlow', 'Loading mode Game with purpose LoadGame');

    game.after(10, () => game.log('SceneFlow', 'Loading mode MainMenu with purpose Cleanup'));
    game.after(12, () => game.log('SceneFlow', 'MainMenu reached'));

    const result = await waitLoad(context);

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`main menu`);
    expect(game.seconds()).toBeLessThan(20);
  });

  test(`fails at once on a game dying during the load`, async () => {
    await game.start();
    await game.log('SceneFlow', 'Loading mode Game with purpose LoadGame');

    game.after(10, () => {
      game.exit();
    });

    const result = await waitLoad(context);

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`The game stopped`);
    expect(game.seconds()).toBeLessThan(15);
  });

  test(`leaves a game whose load hangs running, and says so`, async () => {
    await game.start();
    await game.log('SceneFlow', 'Loading mode Game with purpose LoadGame');

    const result = await waitLoad(context);

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`left running`);
    expect(game.pids.game).toEqual([4242]);
  });
});

describe('a step that never settles', () => {
  /**
   * Runs a tool until it is stuck, and returns its result once the ceiling passed, having checked
   * it was still waiting a second before.
   */
  async function resultAtCeiling(
    call: Promise<CallToolResult>,
    ceilingSeconds: number
  ): Promise<CallToolResult> {
    let isSettled = false;

    async function watch(): Promise<void> {
      await call;

      isSettled = true;
    }

    void watch();

    await game.reachedStuck;
    await game.sleep((ceilingSeconds - 1) * 1000);
    await Bun.sleep(0);

    expect(isSettled).toBe(false);

    await game.sleep(1000);

    return call;
  }

  test(`ends a stop at its ceiling, naming the step`, async () => {
    await game.start();
    game.stuckOn = 'askToEnd';

    const result = await resultAtCeiling(stop(context), STOP_CEILING_SECONDS);

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`Stopping the game`);
    expect(resultText(result)).toContain(`Nothing more is done to the game.`);
  });

  test(`ends a restart at its ceiling, naming the step`, async () => {
    game.stuckOn = 'launch';

    const result = await resultAtCeiling(restart(context, {}), RESTART_CEILING_SECONDS);

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`Launching the game`);
  });

  test(`deletes runOnce.txt when a restart is given up on during its launch`, async () => {
    game.stuckOn = 'launch';

    await resultAtCeiling(restart(context, {}), RESTART_CEILING_SECONDS);
    await Bun.sleep(5);

    expect(await readFile(game.runOncePath, 'utf8').catch(() => 'deleted')).toBe('deleted');
  });

  test(`frees the game for the next call`, async () => {
    game.stuckOn = 'launch';

    await resultAtCeiling(restart(context, {}), RESTART_CEILING_SECONDS);

    game.stuckOn = undefined;

    expect(resultText(await stop(context))).toContain(`not running`);
  });

  test(`ends a wait_load at its ceiling, naming the step`, async () => {
    game.stuckOn = 'findPids';

    const result = await resultAtCeiling(waitLoad(context), WAIT_LOAD_CEILING_SECONDS);

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`Waiting for the load`);
  });
});

describe('a wait that failed', () => {
  test(`names the whole of the wait it gave up on`, async () => {
    await game.start();
    await game.log('SceneFlow', 'Loading mode Game with purpose LoadGame');

    const result = await waitLoad(context);

    expect(resultText(result)).toContain(`within ${LOAD_SECONDS + LOADING_SCREEN_SECONDS} s`);
    expect(game.seconds()).toBe(LOAD_SECONDS + LOADING_SCREEN_SECONDS);
  });

  test(`says so even when the game can no longer be looked at`, async () => {
    await game.start();
    await game.log('SceneFlow', 'Loading mode MainMenu with purpose Cleanup');
    game.after(1, async () => {
      await game.log('SceneFlow', 'MainMenu reached');
      game.findPidsFailure = `pgrep did not answer`;
    });

    const result = await waitLoad(context);

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`went to the main menu`);
    expect(resultText(result)).toContain(
      `Whether the game still runs could not be checked: pgrep did not answer`
    );
  });
});

describe('a call beside another', () => {
  test(`is refused while another holds the game, naming it`, async () => {
    game.stuckOn = 'launch';

    const first = restart(context, {});

    await game.reachedStuck;

    for (const second of [await stop(context), await restart(context, {})]) {
      expect(second.isError).toBe(true);
      expect(resultText(second)).toContain(`restart (Launching the game)`);
    }

    // The stuck launch never got to record itself, and the refused calls never got that far.
    expect(game.launches).toEqual([]);

    cancellation.abort();
    await first;
  });

  test(`runs wait_load, which only reads`, async () => {
    context.slot.running = 'restart (Launching the game)';

    expect(resultText(await waitLoad(context))).toContain(`The game is not running`);
  });
});

describe('a call the caller cancels', () => {
  test(`ends at once, naming the step`, async () => {
    game.stuckOn = 'launch';

    const call = restart(context, {});

    await game.reachedStuck;
    cancellation.abort();

    const result = await call;

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`Cancelled at this step: Launching the game`);
  });

  test(`stops launching, and leaves no options for the next launch`, async () => {
    game.onLaunch = () => {
      cancellation.abort();
    };

    const result = await restart(context, {});

    await Bun.sleep(5);

    expect(result.isError).toBe(true);
    expect(game.launches).toHaveLength(1);
    expect(await readFile(game.runOncePath, 'utf8').catch(() => 'deleted')).toBe('deleted');
  });

  test(`does not start on a call already cancelled`, async () => {
    cancellation.abort();

    const result = await restart(context, {});

    expect(result.isError).toBe(true);
    expect(game.launches).toEqual([]);
    expect(context.slot.running).toBeUndefined();
  });

  test(`frees the game for good, even when its step goes on later`, async () => {
    game.stuckOn = 'findPids';

    const call = restart(context, {});

    await game.reachedStuck;
    cancellation.abort();
    await call;

    game.releaseOldest();
    await Bun.sleep(20);

    expect(context.slot.running).toBeUndefined();

    const next = await stop({ ...context, signal: new AbortController().signal });

    expect(resultText(next)).toContain(`not running`);
  });

  test(`writes no options once it ended, even past a step that goes on later`, async () => {
    const call = restart(
      {
        ...context,
        progress: message => {
          if (message == 'Launching the game') {
            game.stuckOn = 'findPids';
          }
        }
      },
      {}
    );

    await game.reachedStuck;
    cancellation.abort();
    await call;

    game.releaseOldest();
    await Bun.sleep(20);

    expect(await readFile(game.runOncePath, 'utf8').catch(() => 'deleted')).toBe('deleted');
  });

  test(`leaves the options of the call that retried alone`, async () => {
    game.stuckOn = 'launch';

    const call = restart(context, {});

    await game.reachedStuck;
    cancellation.abort();
    await call;

    const retry = restart(
      { ...context, signal: new AbortController().signal },
      { gameOptions: ['--retry'] }
    );

    while (game.stuck.length < 2) {
      await Bun.sleep(1);
    }

    game.releaseOldest();
    await Bun.sleep(20);

    expect(await readFile(game.runOncePath, 'utf8')).toContain('--retry');

    game.releaseOldest();
    game.onLaunch = () => {
      game.after(5, () => game.start());
      game.after(10, () => game.log('SceneFlow', 'MainMenu reached'));
    };

    const result = await retry;

    expect(result.isError).toBeUndefined();
  });

  test(`leaves the game's process alone`, async () => {
    await writeAsset('Saves', 'My City', 'SaveGameMetadata', 'a'.repeat(32));

    game.onLaunch = () => {
      game.after(5, () => game.start());
      game.after(30, () => {
        cancellation.abort();
      });
    };

    await restart(context, { save: 'My City' });

    expect(game.pids.game).toEqual([4242]);
    expect(game.forces).toEqual([]);
  });
});

describe('a call that ended', () => {
  const steps: ReadonlyArray<readonly [string, (host: GameHost) => Promise<unknown>]> = [
    ['findPids', host => host.findPids()],
    ['launch', host => host.launch()],
    ['askToEnd', host => host.askToEnd()],
    ['forceToEnd', host => host.forceToEnd('kill')],
    ['sleep', host => host.sleep(1000)]
  ];

  test.each(steps)(`fails at its next touch of the game: %s`, async (_name, touch) => {
    let guarded: GameHost | undefined;

    await runTool(context, { name: 'tool', ceilingSeconds: 1, isExclusive: true }, tool => {
      guarded = tool.host;

      return Promise.resolve('done');
    });

    assert.ok(guarded);

    expect(await failureOf(touch(guarded))).toContain(`The call ended`);
    expect(game.launches).toEqual([]);
    expect(game.closeRequests).toEqual([]);
    expect(game.forces).toEqual([]);
  });

  test(`sends no progress after the call ended`, async () => {
    const messages: string[] = [];

    let progress: (message: string) => void = () => {};

    await runTool(
      {
        ...context,
        progress: message => {
          messages.push(message);
        }
      },
      { name: 'tool', ceilingSeconds: 1, isExclusive: true },
      tool => {
        ({ progress } = tool);

        return Promise.resolve('done');
      }
    );

    progress('later');

    expect(messages).toEqual([]);
    expect(context.slot.running).toBeUndefined();
  });
});

describe('a damaged asset', () => {
  test(`fails on a file that is no archive, without touching a running game`, async () => {
    await runningGame();
    await writeAsset('Saves', 'Broken', 'SaveGameMetadata', 'a'.repeat(32));
    await writeFile(
      path.join(game.userDataPath, 'Saves', '76561198000000000', 'Broken.cok'),
      'not a zip'
    );

    const result = await restart(context, { save: 'Broken' });

    expect(result.isError).toBe(true);
    expect(game.closeRequests).toEqual([]);
  });

  test(`fails on an archive naming a range it does not hold`, async () => {
    await writeAsset('Saves', 'Broken', 'SaveGameMetadata', 'a'.repeat(32));

    const archive = path.join(game.userDataPath, 'Saves', '76561198000000000', 'Broken.cok');
    const bytes = await readFile(archive);

    // The directory's size and offset, in the end of central directory record.
    bytes.writeUInt32LE(4_000_000_000, bytes.length - 10);
    await writeFile(archive, bytes);

    const result = await restart(context, { save: 'Broken' });

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain('damaged');
  });

  test(`fails on a guid entry that holds no guid`, async () => {
    await writeAsset('Saves', 'Broken', 'SaveGameMetadata', '--startGame=nonsense --evil');

    const result = await restart(context, { save: 'Broken' });

    expect(result.isError).toBe(true);
    expect(game.launches).toEqual([]);
  });
});
