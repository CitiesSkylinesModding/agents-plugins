import { afterEach, beforeEach, describe, expect, spyOn, test } from 'bun:test';
import assert from 'node:assert/strict';
import { cp, mkdir, readdir, readFile, rm, utimes, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import {
  getDefaultEnvironment,
  StdioClientTransport
} from '@modelcontextprotocol/sdk/client/stdio.js';
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { type Offsets, readCursor, updateCursor } from '../src/cursor';
import { READ_CEILING_SECONDS, RESTART_CEILING_SECONDS, type ToolContext } from '../src/cycle';
import { createIgnoreMatcher, settleIgnored } from '../src/hint';
import { IGNORE_INPUT_CHARACTERS, readIgnores, updateIgnores } from '../src/ignores';
import { LOGS_CAP_CHARACTERS } from '../src/log-report';
import { SCAN_CEILING_SECONDS, watchForNews } from '../src/monitor-loop';
import { type LogScan, SCAN_WINDOW_BYTES, type ScanRequest, scanLogs } from '../src/scanner';
import { HINT_CEILING_SECONDS } from '../src/tool-run';
import { ignoreError, logs, restart, status, stop } from '../src/tools';
import { createFakeGame, type FakeGame, resultText } from './fake-game';

const fixturesPath = path.join(import.meta.dirname, 'fixtures');

/**
 * Past the quiet window, so the last entry of a file counts as whole.
 */
const QUIET_MILLISECONDS = 2500;

const afterTheRun = new Date('2026-11-01T12:00:00.000Z');

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
 * A running game whose run started now.
 */
async function runningGame(): Promise<void> {
  await game.start();
  await game.log('SceneFlow', 'GameManager created!');
  await game.sleep(QUIET_MILLISECONDS);
}

/**
 * How a hint opens, whatever it tells first.
 */
const HINT_OPENINGS = [
  `The game went away`,
  `A new crash report`,
  `Errors: `,
  `Dropped ignore pattern`
];

/**
 * The hint a result carries, its last line, undefined with none.
 */
function hintOf(result: CallToolResult): string | undefined {
  const last = resultText(result).split('\n').at(-1) ?? '';

  return HINT_OPENINGS.some(opening => last.startsWith(opening)) ? last : undefined;
}

async function offsetsOf(): Promise<Offsets> {
  const { offsets } = await readCursor(game.userDataPath);

  return offsets;
}

async function statusHint(): Promise<string | undefined> {
  return hintOf(await status(context));
}

async function settledError(message: string, file = 'MyMod'): Promise<void> {
  await game.log(file, message, 'ERROR');
  await game.sleep(QUIET_MILLISECONDS);
}

/**
 * An exception Unity logged, as `Player.log` holds it, thrown by a frame.
 */
function loggedException(frame: string): string {
  return [
    'NullReferenceException: Object reference not set to an instance of an object',
    `  at ${frame} () [0x00000] in D:/Mods/MyMod/Cases.cs:12`,
    'UnityEngine.DebugLogHandler:Internal_LogException(Exception, Object)',
    '',
    '(Filename: D:/Mods/MyMod/Cases.cs Line: 12)',
    '',
    ''
  ].join('\n');
}

/**
 * An exception logged and never thrown, as `Player.log` holds it, from a call site.
 */
function neverThrownException(caller: string): string {
  return [
    'InvalidOperationException: It failed.',
    'UnityEngine.DebugLogHandler:Internal_LogException(Exception, Object)',
    'UnityEngine.DebugLogHandler:LogException(Exception, Object)',
    'Colossal.Logging.CustomLogHandler:LogException(Exception, Object)',
    'UnityEngine.Logger:LogException(Exception, Object)',
    'UnityEngine.Debug:LogException(Exception)',
    `${caller} (at D:/Mods/MyMod/Cases.cs:82)`,
    '',
    ''
  ].join('\n');
}

async function logsText(request: Parameters<typeof logs>[1] = {}): Promise<string> {
  return resultText(await logs(context, request));
}

/**
 * A stopped game holding a fixture run's logs.
 */
async function fixtureRun(name: string): Promise<void> {
  await cp(path.join(fixturesPath, name), game.userDataPath, { recursive: true });
  await dateAfterTheRun(game.userDataPath);
}

/**
 * Dates a folder's files after the fixtures' runs:
 * a copy takes today's date, which a stale check reads against stamps `bun test`'s UTC clock moves.
 */
async function dateAfterTheRun(folder: string): Promise<void> {
  const files = await readdir(folder, { recursive: true, withFileTypes: true });

  for (const file of files.filter(entry => entry.isFile())) {
    await utimes(path.join(file.parentPath, file.name), afterTheRun, afterTheRun);
  }
}

describe(`the hint`, () => {
  test(`says nothing with no news`, async () => {
    await runningGame();

    expect(await statusHint()).toBeUndefined();
  });

  test(`ends a result, on a line of its own`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    const result = await status(context);

    expect(result.content).toHaveLength(1);
    expect(resultText(result)).toMatch(
      /\S\nErrors: 1 new, 1 kind, 1 in Logs\/MyMod\.log\. See logs\.$/u
    );
  });

  test(`reaches a failed call too`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    const result = await ignoreError(context, { pattern: `never added`, remove: true });

    expect(result.isError).toBe(true);
    expect(hintOf(result)).toContain(`Errors: 1 new`);
  });

  test(`announces an error once`, async () => {
    await runningGame();
    await settledError(`It failed.`);
    await statusHint();

    expect(await statusHint()).toBeUndefined();
  });

  test(`announces an error once across parallel results`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    const results = await Promise.all([status(context), logs(context, {})]);

    expect(results.flatMap(result => hintOf(result) ?? [])).toEqual([
      `Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`
    ]);
  });

  test(`counts the mod's errors apart`, async () => {
    await runningGame();
    await updateCursor(context, cursor => ({ ...cursor, mod: 'MyMod' }));
    await game.log('MyMod', `Mine.`, 'ERROR');
    await game.log('OtherMod', 'Failed\n  at MyMod.Patch.Run () [0x00000] in <0>:0', 'ERROR');
    await settledError(`Not mine.`, 'OtherMod');

    expect(await statusHint()).toBe(
      `Errors: 3 new, 3 kinds, 2 naming MyMod, 1 in Logs/OtherMod.log. See logs.`
    );
  });

  test(`names where the others are, most first, two at most`, async () => {
    await runningGame();
    await updateCursor(context, cursor => ({ ...cursor, mod: 'MyMod' }));
    await game.log('First', `One.`, 'ERROR');
    await game.log('First', `Two.`, 'ERROR');
    await game.log('Second', `One.`, 'ERROR');
    await game.log('Second', `Two.`, 'ERROR');
    await game.log('Second', `Three.`, 'ERROR');
    await game.log('Third', `One.`, 'ERROR');
    await settledError(`One.`, 'Fourth');

    expect(await statusHint()).toBe(
      `Errors: 7 new, 7 kinds, 0 naming MyMod, 3 in Logs/Second.log, 2 in Logs/First.log ` +
        `and 2 elsewhere. See logs.`
    );
  });

  test(`counts an error once across Logs/ and Player.log`, async () => {
    await runningGame();
    await game.log('MyMod', `It failed.`, 'ERROR');
    await game.append(game.playerLogPath, '[MyMod] [ERROR]  It failed. System.Exception: why\n\n');
    await game.sleep(QUIET_MILLISECONDS);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`);
  });

  test(`counts an exception from a build with no PDB`, async () => {
    await runningGame();
    await game.append(
      game.playerLogPath,
      [
        'InvalidOperationException: It failed.',
        '  at MyMod.Cases.Run () [0x0006e] in <c050ed2c6390477389422fd7527d116e>:0 ',
        'UnityEngine.DebugLogHandler:Internal_LogException(Exception, Object)',
        'UnityEngine.DebugLogHandler:LogException(Exception, Object)',
        'Colossal.Logging.CustomLogHandler:LogException(Exception, Object)',
        'UnityEngine.Logger:LogException(Exception, Object)',
        'UnityEngine.Debug:CallOverridenDebugHandler(Exception, Object)',
        '',
        ''
      ].join('\n')
    );
    await game.sleep(QUIET_MILLISECONDS);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Player.log. See logs.`);
  });

  test(`tells never-thrown exceptions apart by the call site under Unity's logging`, async () => {
    await runningGame();
    await game.append(
      game.playerLogPath,
      neverThrownException('MyMod.Panel:Open()') + neverThrownException('MyMod.Poller:Update()')
    );
    await game.sleep(QUIET_MILLISECONDS);

    expect(await statusHint()).toBe(`Errors: 2 new, 2 kinds, 2 in Player.log. See logs.`);
  });

  test(`keeps an engine method on top of a thrown exception's stack in its kind`, async () => {
    await runningGame();
    await game.append(
      game.playerLogPath,
      loggedException('UnityEngine.Texture2D.Create ()\n  at MyMod.Cases.Run') +
        loggedException('UnityEngine.Mesh.Create ()\n  at MyMod.Cases.Run')
    );
    await game.sleep(QUIET_MILLISECONDS);

    expect(await statusHint()).toBe(`Errors: 2 new, 2 kinds, 2 in Player.log. See logs.`);
  });

  test(`counts an exception Unity logged, which only Player.log holds, for the mod`, async () => {
    await runningGame();
    await updateCursor(context, cursor => ({ ...cursor, mod: 'MyMod' }));

    await game.append(
      game.playerLogPath,
      `Fallback handler said so\n${loggedException('MyMod.Cases.Run')}`
    );
    await game.sleep(QUIET_MILLISECONDS);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 naming MyMod. See logs.`);
  });

  test(`counts an entry half-written at a scan's end once, and whole`, async () => {
    await runningGame();
    await game.log('MyMod', `It failed.`, 'ERROR');

    expect(await statusHint()).toBeUndefined();

    await game.append(game.logPath('MyMod'), '  at MyMod.Run () [0x00000] in <0>:0\n');
    await game.log('MyMod', `The next line.`);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`);
    expect(await logsText({ errorsOnly: true })).toContain('at MyMod.Run ()');
    expect(await statusHint()).toBeUndefined();
  });

  test(`counts a Player.log line half-written at a scan's end once, and whole`, async () => {
    await runningGame();
    await game.append(game.playerLogPath, '[Other] [ERR');

    expect(await statusHint()).toBeUndefined();

    await game.append(game.playerLogPath, 'OR]  It failed.\n\n');

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Player.log. See logs.`);
  });

  test(`counts the line a stopped game's log ends on, with no line break after it`, async () => {
    await runningGame();
    await game.append(game.playerLogPath, '[Other] [ERROR]  It failed.');
    await game.sleep(QUIET_MILLISECONDS);
    game.exit();

    expect(await statusHint()).toContain(`Errors: 1 new`);
  });

  test(`counts the last entry of a file no longer written, on a one-shot scan`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    expect(await statusHint()).toContain(`1 new`);
  });

  test(`counts the last entry of a file once the game ended, however fresh`, async () => {
    await runningGame();
    await game.log('MyMod', `It failed.`, 'ERROR');
    game.exit();

    expect(await statusHint()).toContain(`Errors: 1 new`);
  });

  test(`holds a lone Player.log entry for its twin, which then counts once`, async () => {
    await runningGame();
    await game.append(game.playerLogPath, '[MyMod] [ERROR]  It failed.\n\n');

    game.after(1, () => game.log('MyMod', `It failed.`, 'ERROR'));

    await game.sleep(0);

    expect(await statusHint()).toBeUndefined();

    await game.sleep(QUIET_MILLISECONDS);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`);
  });

  test(`lets a Player.log entry stand alone past the hold, with no logger file`, async () => {
    await runningGame();
    await game.append(game.playerLogPath, '[NoFile] [ERROR]  It failed.\n\n');
    await game.sleep(QUIET_MILLISECONDS);

    const start = game.seconds();

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Player.log. See logs.`);
    expect(game.seconds() - start).toBeGreaterThanOrEqual(2);
  });

  test(`leaves stale files out`, async () => {
    await writeFile(game.logPath('OldMod'), '[2020-01-01 10:00:00,000] [ERROR]  Last year.\n');
    await utimes(game.logPath('OldMod'), new Date('2020-01-01'), new Date('2020-01-01'));
    await runningGame();

    expect(await statusHint()).toBeUndefined();
  });

  test(`leaves out a stale file whose date says otherwise, by its stamps`, async () => {
    await writeFile(game.logPath('OldMod'), '[2020-01-01 10:00:00,000] [ERROR]  Last year.\n');
    await runningGame();
    await utimes(game.logPath('OldMod'), game.now(), game.now());
    await game.sleep(QUIET_MILLISECONDS);

    expect(await statusHint()).toBeUndefined();

    const offsets = await offsetsOf();

    expect(offsets['Logs/OldMod.log'] ?? 0).toBe(0);
  });

  test(`reads a file the game truncates after a first scan left it`, async () => {
    await writeFile(game.logPath('OldMod'), '[2020-01-01 10:00:00,000] [ERROR]  Last year.\n');
    await runningGame();
    await statusHint();
    await writeFile(game.logPath('OldMod'), '');
    await settledError(`It failed.`, 'OldMod');

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Logs/OldMod.log. See logs.`);
  });

  test(`reads again a file rewritten under its offset, and announces it once`, async () => {
    await runningGame();
    await settledError(`A first error, longer than what comes after it.`);
    await statusHint();
    await writeFile(game.logPath('MyMod'), '');
    await settledError(`Second.`);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`);
    expect(await statusHint()).toBeUndefined();
  });

  test(`takes a last run's file for no twin of a Player.log entry`, async () => {
    await writeFile(game.logPath('MyMod'), '[2020-01-01 10:00:00,000] [INFO]  Last year.\n');
    await runningGame();
    await game.append(game.playerLogPath, '[MyMod] [ERROR]  It failed.\n\n');
    await game.sleep(QUIET_MILLISECONDS);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Player.log. See logs.`);
  });

  test(`starts over on a Player.log rewritten at launch`, async () => {
    await runningGame();
    await game.append(game.playerLogPath, `[NoFile] [ERROR]  ${'It failed. '.repeat(50)}\n\n`);
    await game.sleep(QUIET_MILLISECONDS);
    await statusHint();

    game.exit();
    await updateCursor(context, cursor => ({
      ...cursor,
      lastStop: game.now().toISOString()
    }));

    await game.sleep(1000);
    await rm(game.logPath('SceneFlow'));
    await game.start();
    await game.log('SceneFlow', 'GameManager created!');
    await game.append(game.playerLogPath, '[NoFile] [ERROR]  Again.\n\n');
    await game.sleep(QUIET_MILLISECONDS);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Player.log. See logs.`);
  });

  test(`reports the game going away with no cs2-dev stop, once`, async () => {
    await runningGame();
    await statusHint();
    game.exit();

    expect(await statusHint()).toBe(`The game went away with no cs2-dev stop.`);
    expect(await statusHint()).toBeUndefined();
  });

  test(`reports the game going away after a stop of an earlier run`, async () => {
    await runningGame();
    await updateCursor(context, cursor => ({
      ...cursor,
      lastStop: game.now().toISOString()
    }));

    await game.sleep(1000);
    await rm(game.logPath('SceneFlow'));
    await runningGame();
    await statusHint();
    game.exit();

    expect(await statusHint()).toBe(`The game went away with no cs2-dev stop.`);
  });

  test(`reports the game going away after a stop its caller cancelled`, async () => {
    await runningGame();
    await statusHint();

    const cancel = new AbortController();

    game.stuckOn = 'findPids';

    const stopping = stop({ ...context, signal: cancel.signal });

    await game.reachedStuck;
    cancel.abort();
    await stopping;
    game.releaseOldest();

    // The step left behind writes on real time, whatever the game's clock says.
    await Bun.sleep(100);
    game.exit();

    expect(await statusHint()).toBe(`The game went away with no cs2-dev stop.`);
  });

  test(`says nothing of a game the tool stopped`, async () => {
    await runningGame();

    game.onClose = () => {
      game.exit();
    };

    const result = await stop(context);

    expect(hintOf(result)).toBeUndefined();
    expect(await statusHint()).toBeUndefined();
  });

  test(`reports a new crash report, without calling it a death`, async () => {
    await runningGame();
    await crashReport();

    expect(await statusHint()).toBe(`A new crash report appeared, which may be a hang snapshot.`);
    expect(await statusHint()).toBeUndefined();
  });

  test(`stays silent on a crash report older than the run, on a fresh cursor`, async () => {
    await crashReport();
    await game.sleep(1000);
    await runningGame();

    expect(await statusHint()).toBeUndefined();
  });

  test(`tells a restart of the run it ended, whose errors no later result can count`, async () => {
    await runningGame();
    await settledError(`It failed.`);
    await crashReport();
    game.exit();

    game.onLaunch = () => {
      void game.start();
      game.after(5, () => game.log('SceneFlow', 'MainMenu reached'));
    };

    const result = await restart(context, {});

    expect(resultText(result)).toContain(
      `The game went away with no cs2-dev stop. ` +
        `A new crash report appeared, which may be a hang snapshot. ` +
        `Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`
    );
    expect(resultText(result)).toContain(`previous: true`);
  });

  test(`tells a restart of the errors its own stop brought out, as ended by the tool`, async () => {
    await runningGame();

    game.onClose = () => {
      game.after(1, async () => {
        await game.log('MyMod', `Shutdown failed.`, 'ERROR');
        game.exit();
      });
    };

    game.onLaunch = () => {
      void game.start();
      game.after(5, () => game.log('SceneFlow', 'MainMenu reached'));
    };

    expect(resultText(await restart(context, {}))).toStartWith(
      `The run this restart ends: Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs. ` +
        `logs reads it with previous: true.\n`
    );
  });

  test(`counts the ended run's errors for the mod this restart names`, async () => {
    await runningGame();
    await settledError(`It failed.`);
    game.exit();

    game.onLaunch = () => {
      void game.start();
      game.after(5, () => game.log('SceneFlow', 'MainMenu reached'));
    };

    expect(resultText(await restart(context, { mod: 'MyMod' }))).toContain(`1 naming MyMod`);
  });

  test(`keeps the ended run's line on a restart given up on`, async () => {
    await runningGame();
    await settledError(`It failed.`);
    game.exit();
    game.stuckOn = 'launch';

    const call = restart(context, {});

    await game.reachedStuck;
    await game.sleep(RESTART_CEILING_SECONDS * 1000);

    const result = await call;

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`The run this restart ends: `);
  });

  test(`leaves the ended run's news to the next result when the stop never ends`, async () => {
    await runningGame();
    await settledError(`It failed.`);
    game.stuckOn = 'askToEnd';

    const call = restart(context, {});

    await game.reachedStuck;
    await game.sleep(RESTART_CEILING_SECONDS * 1000);

    expect(resultText(await call)).not.toContain(`previous: true`);

    game.releaseOldest();

    expect(await statusHint()).toContain(`Errors: 1 new`);
  });

  test(`takes a logger no file can be named after for one with no twin`, async () => {
    await runningGame();
    await game.append(game.playerLogPath, `[${'x'.repeat(300)}] [ERROR]  It failed.\n\n`);
    await game.sleep(QUIET_MILLISECONDS);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Player.log. See logs.`);

    const result = await logs(context, {});

    expect(result.isError).toBeFalsy();
  });

  test(`reads no log outside Logs/ for a logger's twin`, async () => {
    await runningGame();
    await game.log('../outside', `Written this run.`);
    await game.append(game.playerLogPath, '[../outside] [ERROR]  It failed.\n\n');
    await game.sleep(QUIET_MILLISECONDS);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Player.log. See logs.`);
  });

  test(`skips an entry stamped before the run and reads on`, async () => {
    await runningGame();
    await writeFile(game.logPath('MyMod'), '[2020-01-01 10:00:00,000] [ERROR]  Stamped earlier.\n');
    await settledError(`It failed.`);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`);

    const offsets = await offsetsOf();

    expect(offsets['Logs/MyMod.log']).toBeGreaterThan(0);
  });

  test(`counts a mod's error from its toolchain-named log`, async () => {
    await runningGame();
    await updateCursor(context, cursor => ({ ...cursor, mod: 'MyMod' }));
    await settledError(`It failed.`, 'MyMod.Mod');

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 naming MyMod. See logs.`);
  });

  test(`reads a backlog one window at a time, so a scan always ends and moves on`, async () => {
    await runningGame();
    await settledError(`Before the window.`);
    await game.log('MyMod', `Filler ${'x'.repeat(4096)}`);

    const log = await readFile(game.logPath('MyMod'), 'utf8');
    const filler = log.slice(log.lastIndexOf('\n[') + 1);
    const count = Math.ceil(SCAN_WINDOW_BYTES / filler.length);

    await game.append(game.logPath('MyMod'), filler.repeat(count));
    await settledError(`After the window.`);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`);

    const { 'Logs/MyMod.log': reached = 0 } = await offsetsOf();
    const bytes = await readFile(game.logPath('MyMod'));

    expect(reached).toBeLessThanOrEqual(SCAN_WINDOW_BYTES);
    expect(String.fromCodePoint(bytes.at(reached - 1) ?? 0)).toBe('\n');

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`);
    expect(await statusHint()).toBeUndefined();
  });

  test(`moves the cursor past a hint of a call that holds the game`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    game.onClose = () => {
      game.exit();
    };

    expect(hintOf(await stop(context))).toContain(`1 new`);
  });
});

describe(`a storm`, () => {
  test(`costs the hint one line, with its kind count`, async () => {
    await fixtureRun('storm');

    const hint = await statusHint();

    expect(hint).toMatch(/^The game went away with no cs2-dev stop\. Errors: \d+ new, 23 kinds,/u);
  });

  test(`merges numbers that vary, hexadecimal ones included`, async () => {
    await runningGame();
    await game.log('MyMod', 'Storm: frame 1209 of Entity(1209:1) at 0x000004B9', 'ERROR');
    await settledError('Storm: frame 1210 of Entity(1210:1) at 0x000004BA');

    expect(await statusHint()).toBe(`Errors: 2 new, 1 kind, 2 in Logs/MyMod.log. See logs.`);
  });

  test(`merges numbers of any width past the first line's cut`, async () => {
    await runningGame();
    await game.log('MyMod', `Request 99 failed ${'x'.repeat(300)}`, 'ERROR');
    await settledError(`Request 100 failed ${'x'.repeat(300)}`);

    expect(await statusHint()).toBe(`Errors: 2 new, 1 kind, 2 in Logs/MyMod.log. See logs.`);
  });

  test(`merges lines that imitate a stamped log entry`, async () => {
    await runningGame();

    for (const second of ['01', '02', '03']) {
      await game.append(game.playerLogPath, `[12:00:${second}] [ERROR]  It failed.\n\n`);
    }

    await game.sleep(QUIET_MILLISECONDS);

    expect(await statusHint()).toBe(`Errors: 3 new, 1 kind, 3 in Player.log. See logs.`);
  });

  test(`tells apart errors that share a first line, by their top frame`, async () => {
    await runningGame();
    await game.append(
      game.playerLogPath,
      loggedException('MyMod.Cases.Run') + loggedException('MyMod.Other.Go')
    );
    await game.sleep(QUIET_MILLISECONDS);

    expect(await statusHint()).toBe(`Errors: 2 new, 2 kinds, 2 in Player.log. See logs.`);
  });

  test(`costs logs one entry per kind within its cap, with a footer`, async () => {
    await fixtureRun('storm');

    const text = await logsText({ errorsOnly: true });

    expect(text.match(/MainLoop->RaycastSystem/gu)).toHaveLength(1);
    expect(text).toContain('[CRITICAL] x30,');
    expect(text).toMatch(/\d+ entries, 23 kinds\./u);
  });

  test(`cuts a long entry with a marker, so it leaves room for the kinds after it`, async () => {
    await runningGame();
    await game.log('MyMod', `Noisy ${'x'.repeat(LOGS_CAP_CHARACTERS)}`, 'WARN');
    await settledError(`The next kind.`);

    const text = await logsText();

    expect(text).toMatch(/\[cut: \d+ more characters\]/u);
    expect(text).toContain(`The next kind.`);
  });

  test(`leaves out the kinds past the cap, and says how to narrow`, async () => {
    await runningGame();

    for (const name of 'abcdefghij') {
      await game.log('MyMod', `Kind ${name} ${'x'.repeat(LOGS_CAP_CHARACTERS)}`, 'ERROR');
    }

    await game.sleep(QUIET_MILLISECONDS);

    const text = await logsText({ errorsOnly: true });

    expect(text).toContain(`left out by the cap: narrow with file, level, mod or errorsOnly.`);
    expect(text.length).toBeLessThan(LOGS_CAP_CHARACTERS + 500);
  });

  test(`counts what a cut left out of an entry, the cap's cut included`, async () => {
    await runningGame();

    const names = ['a', 'b', 'c', 'd', 'e'];
    const filler = 'x'.repeat(LOGS_CAP_CHARACTERS);

    for (const name of names) {
      await game.log('MyMod', `Kind ${name} ${filler}`, 'ERROR');
    }

    await game.sleep(QUIET_MILLISECONDS);

    const text = await logsText({ errorsOnly: true });
    const cuts = text.matchAll(/^(?<kept>Kind .*)\n\[cut: (?<count>\d+) more characters\]$/gmu);
    const lengths = [...cuts].map(
      ({ groups }) => (groups?.kept?.length ?? 0) + Number(groups?.count)
    );

    expect(lengths).toEqual(names.map(name => `Kind ${name} ${filler}`.length));
  });
});

describe('logs', () => {
  test(`refuses an empty mod, which would read a broken run as clean`, async () => {
    const client = new Client({ name: 'test', version: '0' });

    await client.connect(
      new StdioClientTransport({
        command: process.execPath,
        args: [path.join(import.meta.dirname, '..', 'src', 'server.ts')],
        env: { ...getDefaultEnvironment(), CSII_USERDATAPATH: game.userDataPath }
      })
    );

    const { tools } = await client.listTools();

    await client.close();

    const minimums = ['restart', 'logs'].map(name => {
      const mod = tools.find(tool => tool.name == name)?.inputSchema.properties?.mod;

      return mod && 'minLength' in mod ? mod.minLength : undefined;
    });

    expect(minimums).toEqual([1, 1]);

    const pattern = tools.find(tool => tool.name == 'ignore_error')?.inputSchema.properties
      ?.pattern;

    expect(pattern).toMatchObject({ minLength: 1 });
  });

  test(`points at the kept run when the current one holds nothing`, async () => {
    const pointer = `previous: true reads the run before the last restart or test.`;

    await runningGame();

    expect(await logsText({ errorsOnly: true })).toContain(pointer);
    expect(await logsText({ mod: 'NoSuchMod' })).not.toContain(pointer);
    expect(await logsText({ level: 'critical' })).not.toContain(pointer);
    expect(await logsText({ file: 'SceneFlow', errorsOnly: true })).not.toContain(pointer);

    await ignoreError(context, { pattern: 'HTTP' });
    await settledError(`HTTP failed.`);

    expect(await logsText({ errorsOnly: true })).not.toContain(pointer);

    await settledError(`It failed.`);

    expect(await logsText({ errorsOnly: true })).not.toContain(pointer);
  });

  /**
   * The first line a kind prints, and the line its citation names.
   */
  async function citedLine(
    text: string,
    pattern: RegExp,
    base: string
  ): Promise<{ readonly printed: string; readonly cited: string }> {
    const groups = pattern.exec(text)?.groups;

    assert.ok(groups?.file != undefined && groups.line != undefined && groups.printed != undefined);

    const log = await readFile(path.join(base, groups.file), 'utf8');
    const lines = log.split(/\r?\n/u);

    return { printed: groups.printed, cited: lines[Number(groups.line) - 1] ?? '' };
  }

  test(`cites the first line of a Logs/ entry`, async () => {
    await fixtureRun('survey');

    const { printed, cited } = await citedLine(
      await logsText({ file: 'ErrorSurvey', errorsOnly: true }),
      /^(?<file>\S+):(?<line>\d+) \[ERROR\]\s.*\n(?<printed>Survey level error)$/mu,
      game.userDataPath
    );

    expect(cited).toContain(printed);
  });

  test(`cites the first line of a multi-line entry`, async () => {
    await fixtureRun('survey');

    const { printed, cited } = await citedLine(
      await logsText({ file: 'ErrorSurvey', errorsOnly: true }),
      /^(?<file>\S+):(?<line>\d+) \[ERROR\]\s.*\n(?<printed>Survey multi first)$/mu,
      game.userDataPath
    );

    expect(cited).toContain(printed);
  });

  test(`cites the first line of a Player.log entry`, async () => {
    await fixtureRun('survey');

    const { printed, cited } = await citedLine(
      await logsText({ file: 'Player.log', level: 'EXCEPTION' }),
      /^(?<file>\S+):(?<line>\d+) \[EXCEPTION\]\n(?<printed>.*a deep stack threw)$/mu,
      game.userDataPath
    );

    expect(cited).toBe(printed);
  });

  test(`lists the Player.log twin of a Logs/ entry, when Player.log is asked`, async () => {
    await runningGame();
    await game.log('MyMod', `It failed.`, 'ERROR');
    await game.append(game.playerLogPath, '[MyMod] [ERROR]  It failed.\n\n');
    await game.sleep(QUIET_MILLISECONDS);

    expect(await logsText({ file: 'Player.log' })).toContain(`[MyMod]`);
  });

  test(`cites an entry of the previous run in its copy, and says so`, async () => {
    await fixtureRun('survey');

    const copyPath = path.join(game.userDataPath, '.cs2-dev', 'previous-run');

    await mkdir(copyPath, { recursive: true });
    await cp(path.join(game.userDataPath, 'Logs'), path.join(copyPath, 'Logs'), {
      recursive: true
    });

    await rm(path.join(game.userDataPath, 'Logs'), { recursive: true });
    await dateAfterTheRun(copyPath);

    const text = await logsText({ previous: true, file: 'ErrorSurvey', errorsOnly: true });

    const { printed, cited } = await citedLine(
      text,
      /^(?<file>\S+):(?<line>\d+) \[ERROR\]\s.*\n(?<printed>Survey level error)$/mu,
      copyPath
    );

    expect(cited).toContain(printed);
    expect(text).toContain(`Paths are relative to ${copyPath}.`);
  });

  test(`says when no previous run was kept`, async () => {
    await runningGame();

    expect(await logsText({ previous: true })).toBe(`No previous run was kept.`);
  });

  test(`reads the run whole again, past what its own hint announced`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    const first = await logs(context, { errorsOnly: true });

    expect(hintOf(first)).toContain(`1 new`);
    expect(resultText(await logs(context, { errorsOnly: true }))).toContain(`It failed.`);
  });

  test(`filters by level, by file and by mod`, async () => {
    await runningGame();
    await game.log('MyMod', `Careful.`, 'WARN');
    await game.log('OtherMod', `Broken.`, 'ERROR');
    await game.sleep(QUIET_MILLISECONDS);

    expect(await logsText({ level: 'warn' })).toContain(`Careful.`);
    expect(await logsText({ level: 'warn' })).not.toContain(`Broken.`);
    expect(await logsText({ file: 'OtherMod' })).toContain(`Broken.`);
    expect(await logsText({ file: 'OtherMod' })).not.toContain(`Careful.`);
    expect(await logsText({ mod: 'MyMod' })).toContain(`Careful.`);
    expect(await logsText({ mod: 'MyMod' })).not.toContain(`Broken.`);
  });

  test(`takes a file named by a Windows path`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    expect(await logsText({ file: String.raw`Logs\MyMod.log` })).toContain(`It failed.`);
    expect(await logsText({ file: String.raw`C:\Game\Logs\MyMod.log` })).toContain(`It failed.`);
  });

  test(`fails on a filter that matches nothing, which would read as a clean log`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    const noFile = await logs(context, { file: 'Nothing' });
    const noLevel = await logs(context, { level: 'LOUD' });

    expect(noFile.isError).toBe(true);
    expect(resultText(noFile)).toContain(`No log Nothing: it is one of `);
    expect(noLevel.isError).toBe(true);
    expect(resultText(noLevel)).toContain(`No level LOUD: it is one of `);
  });

  test(`prints a kind with no stamp first, its time being unknown`, async () => {
    await runningGame();
    await game.log('MyMod', `Stamped.`, 'ERROR');
    await game.append(game.playerLogPath, loggedException('MyMod.Cases.Run'));
    await game.sleep(QUIET_MILLISECONDS);

    const text = await logsText({ errorsOnly: true });

    expect(text.indexOf('NullReferenceException')).toBeGreaterThanOrEqual(0);
    expect(text.indexOf('NullReferenceException')).toBeLessThan(text.indexOf(`Stamped.`));
  });

  test(`names the mod of a Burst job by its native frame`, async () => {
    await fixtureRun('survey');

    const { kinds } = await scanRun({
      mod: 'ErrorSurvey',
      accepts: ({ text }) => text.includes(`a Burst job threw`)
    });

    const [kind] = kinds.values();

    expect(kind?.modCount).toBe(1);
    expect(kind?.entry.text).toContain('(errorsurvey_win_x86_64) Unity.Jobs.IJobExtensions');
    expect(kind?.entry.text).not.toContain('(unityplayer)');
  });
});

describe(`the cursor`, () => {
  test(`keeps each file's larger offset across two writes`, async () => {
    const offsets = (first: number, second: number): Offsets => ({ a: first, b: second });

    await updateCursor(context, cursor => ({ ...cursor, offsets: offsets(10, 50) }));
    await updateCursor(context, cursor => ({ ...cursor, offsets: offsets(20, 40) }));

    expect(await offsetsOf()).toEqual(offsets(20, 50));
  });

  test(`lets a new run take every offset back`, async () => {
    await updateCursor(context, cursor => ({
      ...cursor,
      runStart: 'first',
      offsets: { a: 50 }
    }));

    await updateCursor(context, cursor => ({
      ...cursor,
      runStart: 'second',
      offsets: { a: 5 }
    }));

    expect(await offsetsOf()).toEqual({ a: 5 });
  });

  test(`keeps every field of parallel writes`, async () => {
    await Promise.all([
      updateCursor(context, cursor => ({ ...cursor, mod: 'MyMod' })),
      updateCursor(context, cursor => ({ ...cursor, goneRun: 'a run' }))
    ]);

    expect(await readCursor(game.userDataPath)).toMatchObject({ mod: 'MyMod', goneRun: 'a run' });
  });

  test(`takes concurrent writes`, async () => {
    const writes = ['A', 'B', 'C', 'D'].map(mod =>
      updateCursor(context, cursor => ({ ...cursor, mod }))
    );

    await Promise.all(writes);

    const { mod } = await readCursor(game.userDataPath);

    expect(mod).toBeString();
  });

  test(`survives a damaged file`, async () => {
    await mkdir(path.join(game.userDataPath, '.cs2-dev'));
    await writeFile(path.join(game.userDataPath, '.cs2-dev', 'cursor.json'), '{ half');

    expect(await offsetsOf()).toEqual({});
  });

  test(`keeps the mod given to a restart whose stop never ends`, async () => {
    await runningGame();
    game.stuckOn = 'askToEnd';

    const call = restart(context, { mod: 'MyMod' });

    await game.reachedStuck;
    await game.sleep(RESTART_CEILING_SECONDS * 1000);
    await call;

    const { mod } = await readCursor(game.userDataPath);

    expect(mod).toBe('MyMod');
  });

  test(`keeps the mod given to restart`, async () => {
    game.onLaunch = () => {
      void game.start();
      game.after(5, () => game.log('SceneFlow', 'MainMenu reached'));
    };

    await restart(context, { mod: 'MyMod' });

    const { mod } = await readCursor(game.userDataPath);

    expect(mod).toBe('MyMod');
  });
});

describe('ignore_error', () => {
  const slowPattern = '(a+)+b';

  function ignore(pattern: string, remove = false): Promise<CallToolResult> {
    return ignoreError(context, { pattern, remove });
  }

  test(`adds and removes a pattern, returning the active ones`, async () => {
    await runningGame();

    expect(resultText(await ignore('HTTP'))).toContain('HTTP (0 hits)');
    expect(resultText(await ignore('HTTP', true))).toBe(`No ignore patterns.`);
  });

  test(`fails on an invalid pattern and adds nothing`, async () => {
    await runningGame();

    const result = await ignore('(unclosed');

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`Not a valid regular expression`);
    expect(await readIgnores(game.userDataPath)).toEqual([]);
  });

  test(`fails on removing a pattern it does not hold`, async () => {
    await runningGame();

    const result = await ignore('HTTP', true);

    expect(result.isError).toBe(true);
  });

  test(`applies before its own hint`, async () => {
    await runningGame();
    await settledError(`HTTP failed.`);

    expect(hintOf(await ignore(`http failed`))).toBeUndefined();
  });

  test(`leaves ignored entries out of the hint, and counts them`, async () => {
    await runningGame();
    await ignore('HTTP');
    await game.log('MyMod', `HTTP failed.`, 'ERROR');
    await settledError(`Something else.`);

    expect(await statusHint()).toBe(
      `Errors: 1 new, 1 kind, 1 in Logs/MyMod.log (1 ignored). See logs.`
    );
  });

  test(`reads an entry no further than its input cap`, async () => {
    await runningGame();
    await ignore('needle');
    await settledError(`${'x'.repeat(IGNORE_INPUT_CHARACTERS)} needle`);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`);
  });

  test(`reads an entry as far as logs prints it`, async () => {
    await runningGame();
    await ignore('needle');

    const lead = 'x'.repeat(IGNORE_INPUT_CHARACTERS - 'needle'.length);

    await settledError(`${lead}needle past the cut`);

    expect(await statusHint()).toBeUndefined();
    expect(await logsText({ ignored: true })).toContain(`needle\n[cut: `);
  });

  test(`says nothing when only ignored entries are new, and moves the cursor`, async () => {
    await runningGame();
    await ignore('HTTP');
    await settledError(`HTTP failed.`);

    expect(await statusHint()).toBeUndefined();

    const { offsets } = await readCursor(game.userDataPath);

    expect(offsets['Logs/MyMod.log']).toBeGreaterThan(0);
  });

  test(`ignores a file's kinds by a pattern naming the file`, async () => {
    await runningGame();
    await ignore(String.raw`Logs/MyMod\.log`);
    await settledError(`Anything.`);

    expect(await statusHint()).toBeUndefined();
  });

  test(`matches no line number`, async () => {
    await runningGame();
    await ignore(String.raw`MyMod\.log:1`);
    await settledError(`Anything.`);

    expect(await statusHint()).toContain(`1 new`);
  });

  test(`reads an entry as logs heads it, less the line number`, async () => {
    await runningGame();
    await game.log('MyMod', `First.`, 'ERROR');
    await game.append(game.playerLogPath, '[NoFile] [ERROR]  Second.\n\n');
    await game.sleep(QUIET_MILLISECONDS);

    const text = await logsText({ errorsOnly: true });
    const heading = /^(?<place>\S+):\d+(?<source>(?: \[\w+\])+).*\n(?<body>.*)$/gmu;
    const headings = [...text.matchAll(heading)];

    expect(headings).toHaveLength(2);

    for (const { groups } of headings) {
      const read = `${groups?.place}${groups?.source}\n${groups?.body}`;

      await ignore(`^${read.replaceAll(/[$()*+.?[\\\]^{|}]/gu, String.raw`\$&`)}$`);
    }

    expect(await logsText({ errorsOnly: true })).toContain(`2 ignored entries left out`);
  });

  test(`skips what a hand edit left in its file that is no pattern`, async () => {
    await runningGame();
    await mkdir(path.join(game.userDataPath, '.cs2-dev'));
    await writeFile(
      path.join(game.userDataPath, '.cs2-dev', 'ignores.json'),
      JSON.stringify([null, {}, { source: 1, hits: 0 }, { source: 'HTTP', hits: 0 }])
    );

    await game.log('MyMod', `HTTP failed.`, 'ERROR');
    await settledError(`Something else.`);

    expect(await statusHint()).toBe(
      `Errors: 1 new, 1 kind, 1 in Logs/MyMod.log (1 ignored). See logs.`
    );
    expect(resultText(await ignore('Other'))).toContain('Other (0 hits)');
  });

  test(`decides once for a kind, on its first entry`, async () => {
    await runningGame();
    await ignore(`first only`);
    await game.log('MyMod', `Failed #1\n  first only`, 'ERROR');
    await settledError(`Failed #2\n  not the same text`);

    await statusHint();

    const [pattern] = await readIgnores(game.userDataPath);

    expect(pattern?.hits).toBe(2);
  });

  test(`leaves ignored entries out of logs with a footer count, and brings them back`, async () => {
    await runningGame();
    await ignore('HTTP');
    await settledError(`HTTP failed.`);

    const hidden = await logsText({ errorsOnly: true });

    expect(hidden).not.toContain(`HTTP failed.`);
    expect(hidden).toContain(`1 ignored entries left out: ignored: true brings them back.`);
    expect(await logsText({ errorsOnly: true, ignored: true })).toContain(`HTTP failed.`);
  });

  test(`lists its patterns in status with their hits`, async () => {
    await runningGame();
    await ignore('HTTP');
    await settledError(`HTTP failed.`);
    await statusHint();

    expect(resultText(await status(context))).toContain('HTTP (1 hits)');
  });

  test(`survives a restart`, async () => {
    await runningGame();
    await ignore('HTTP');

    game.onClose = () => {
      game.exit();
    };

    game.onLaunch = () => {
      void game.start();
      game.after(5, () => game.log('SceneFlow', 'MainMenu reached'));
    };

    const result = await restart(context, {});

    expect(result.isError).toBeFalsy();
    expect(await readIgnores(game.userDataPath)).toHaveLength(1);
  });

  test(`drops a too-slow pattern, which matches nothing and goes once named`, async () => {
    await runningGame();
    await ignore(slowPattern);
    await settledError(`${'a'.repeat(40)}!`);

    const hint = await statusHint();

    expect(hint).toContain(`Dropped ignore pattern ${slowPattern}: it took too long.`);
    expect(hint).toContain(`Errors: 1 new`);
    expect(await readIgnores(game.userDataPath)).toEqual([]);
  }, 20_000);

  test(`marks a too-slow pattern that logs met, for the next hint to name`, async () => {
    await runningGame();
    await settledError(`${'a'.repeat(40)}!`);
    await statusHint();
    await ignore(slowPattern);

    // Nothing is new, so its own hint meets no entry: only the mark can name the pattern.
    const result = await logs(context, { errorsOnly: true });

    expect(hintOf(result)).toContain(`Dropped ignore pattern ${slowPattern}`);
    expect(await readIgnores(game.userDataPath)).toEqual([]);
  }, 20_000);

  test(`marks a too-slow pattern a settle met, though it stands down`, async () => {
    await runningGame();
    await ignore(slowPattern);
    await settledError(`${'a'.repeat(40)}!`);

    await settleIgnored({ ...context, host: game });

    expect(await readIgnores(game.userDataPath)).toEqual([
      { source: slowPattern, hits: 0, isDropped: true }
    ]);
  }, 20_000);

  test(`matches nothing with a pattern already marked dropped`, async () => {
    await runningGame();
    await updateIgnores(context, () => [{ source: 'failed', hits: 0, isDropped: true }]);
    await settledError(`It failed.`);

    expect(await statusHint()).toContain(`Errors: 1 new`);
  });

  test(`keeps every pattern of parallel calls`, async () => {
    await Promise.all(['one', 'two', 'three'].map(pattern => ignore(pattern)));

    const stored = await readIgnores(game.userDataPath);

    expect(stored.map(({ source }) => source).toSorted()).toEqual(['one', 'three', 'two']);
  });

  test(`clears at server start`, async () => {
    await updateIgnores(context, () => [{ source: 'HTTP', hits: 0 }]);

    const client = new Client({ name: 'test', version: '0' });

    await client.connect(
      new StdioClientTransport({
        command: process.execPath,
        args: [path.join(import.meta.dirname, '..', 'src', 'server.ts')],
        env: { ...getDefaultEnvironment(), CSII_USERDATAPATH: game.userDataPath }
      })
    );

    await client.close();

    expect(await readIgnores(game.userDataPath)).toEqual([]);
  });

  test(`silences neither the game going away nor a crash report`, async () => {
    await runningGame();
    await ignore('.');
    await crashReport();
    await statusHint();
    await crashReport();
    await settledError(`Anything.`);
    game.exit();

    expect(await statusHint()).toBe(
      `The game went away with no cs2-dev stop. ` +
        `A new crash report appeared, which may be a hang snapshot.`
    );
  });
});

describe(`the ceilings`, () => {
  test(`keep an abandoned hint's errors for the next result`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    game.stuckOn = 'findPids';

    const call = ignoreError(context, { pattern: 'HTTP' });

    await game.reachedStuck;
    await game.sleep(HINT_CEILING_SECONDS * 1000);

    expect(hintOf(await call)).toBeUndefined();

    game.releaseOldest();

    // The abandoned scan's file reads run on real time, whatever the game's clock says.
    await Bun.sleep(100);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`);
  });

  test(`keep a hint that never settles from holding up the next`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    game.stuckOn = 'findPids';

    const call = ignoreError(context, { pattern: 'HTTP' });

    await game.reachedStuck;
    await game.sleep(HINT_CEILING_SECONDS * 1000);
    await call;

    game.stuckOn = undefined;

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`);
  });

  test(`leave a cancelled call's errors for the next result`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    game.stuckOn = 'findPids';

    const cancel = new AbortController();
    const call = ignoreError({ ...context, signal: cancel.signal }, { pattern: 'HTTP' });

    await game.reachedStuck;
    cancel.abort();
    game.stuckOn = undefined;
    game.releaseOldest();

    await call;

    // The cancelled scan's file reads run on real time, whatever the game's clock says.
    await Bun.sleep(100);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`);
  });

  test(`spare a hint cancelled in the queue its scan`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    const scans = spyOn(game, 'findPids');

    game.stuckOn = 'findPids';

    const first = ignoreError(context, { pattern: 'One' });

    await game.reachedStuck;

    const cancel = new AbortController();
    const queued = ignoreError({ ...context, signal: cancel.signal }, { pattern: 'Two' });

    // Its own write runs on real time, and only then does its hint queue.
    await Bun.sleep(50);
    cancel.abort();
    game.releaseOldest();

    await Promise.all([first, queued]);
    await Bun.sleep(100);

    expect(scans).toHaveBeenCalledTimes(1);
  });

  test(`stop a scan's reads once its signal aborts`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    // Mutable: what the scan fails with.
    let failure: unknown;

    try {
      await scanRun({ signal: AbortSignal.abort() });
    } catch (error) {
      failure = error;
    }

    expect(failure).toMatchObject({ name: 'AbortError' });
  });

  test(`make status return an error when a read never settles`, async () => {
    await runningGame();

    game.stuckOn = 'findPids';

    const call = status(context);

    await game.reachedStuck;
    await game.sleep(READ_CEILING_SECONDS * 1000);

    const result = await call;

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`Gave up after ${READ_CEILING_SECONDS} s`);
  });

  test(`make logs return an error when a read never settles`, async () => {
    await runningGame();

    game.stuckOn = 'findPids';

    const call = logs(context, {});

    await game.reachedStuck;
    await game.sleep(READ_CEILING_SECONDS * 1000);

    const result = await call;

    expect(result.isError).toBe(true);
  });
});

describe('status', () => {
  async function stateLine(): Promise<string> {
    return resultText(await status(context)).split('\n')[0] ?? '';
  }

  test(`reads each state from the process and SceneFlow.log`, async () => {
    await runningGame();

    expect(await stateLine()).toContain(`The game is booting`);

    await game.log('SceneFlow', 'Loading mode MainMenu with purpose Cleanup');

    expect(await stateLine()).toContain(`The game is loading`);

    await game.log('SceneFlow', 'Loading completed');
    await game.log('SceneFlow', 'MainMenu reached');

    expect(await stateLine()).toContain(`The game is at the main menu`);

    await game.log('SceneFlow', 'Loading mode Game with purpose LoadGame');

    expect(await stateLine()).toContain(`The game is loading`);

    await game.log('SceneFlow', 'Loading completed');

    expect(await stateLine()).toContain(`The game is in a city (PIDs 4242)`);

    await game.log('SceneFlow', 'Loading mode Editor with purpose NewMap');
    await game.log('SceneFlow', 'Loading completed');

    expect(await stateLine()).toContain(`The game is in the editor`);
  });

  test(`says a stopped game's report comes from the last run`, async () => {
    await fixtureRun('survey');

    const text = resultText(await status(context));

    expect(text).toContain(`The game is stopped. The rest comes from the last run's logs.`);
    expect(text).toContain(`Version: 1.6.2f1 (767.21d1) [6300.26419] Windows Steamworks.`);
    expect(text).toContain(`Build: Development (Mono).`);
    expect(text).toMatch(/Managed debugger on port \d+\./u);
  });
});

describe(`the monitor`, () => {
  /**
   * Runs the monitor's loop for `scans` scans, returning what it printed.
   */
  async function monitorFor(scans: number): Promise<readonly string[]> {
    const lines: string[] = [];

    // Mutable: the loop asks after each scan.
    let left = scans;

    await watchForNews({
      host: game,
      userDataPath: game.userDataPath,
      print: line => {
        lines.push(line);
      },
      isDone: () => left-- <= 0
    });

    return lines;
  }

  test(`announces an error, and the next result does not again`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    expect(await monitorFor(3)).toEqual([`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`]);
    expect(await statusHint()).toBeUndefined();
  });

  test(`counts what it held back on its next line, or leaves it to a result`, async () => {
    await runningGame();

    for (let index = 0; index < 5; index++) {
      await game.log('MyMod', `A storm.`, 'ERROR');
    }

    await game.sleep(QUIET_MILLISECONDS);
    await monitorFor(3);
    await settledError(`A storm.`);

    expect(await statusHint()).toBe(`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`);
  });

  test(`spends no gap on what a result already announced`, async () => {
    await runningGame();
    await settledError(`Announced.`);
    await statusHint();

    game.after(6, () => game.log('MyMod', `Not announced yet.`, 'ERROR'));

    expect(await monitorFor(10)).toEqual([`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`]);
  });

  test(`reports the game going away at once`, async () => {
    await runningGame();
    await statusHint();
    game.exit();

    expect(await monitorFor(1)).toEqual([`The game went away with no cs2-dev stop.`]);
  });

  test(`reports a crash report at once, the game alive and no error logged`, async () => {
    await runningGame();
    await statusHint();
    await crashReport();

    expect(await monitorFor(1)).toEqual([
      `A new crash report appeared, which may be a hang snapshot.`
    ]);
  });

  test(`says nothing of what a result already announced`, async () => {
    await runningGame();
    await settledError(`It failed.`);
    await statusHint();

    expect(await monitorFor(3)).toEqual([]);
  });

  test(`settles ignored entries alone, moving the cursor and counting the hits`, async () => {
    await runningGame();
    await ignoreError(context, { pattern: 'HTTP' });
    await settledError(`HTTP failed.`);

    expect(await monitorFor(1)).toEqual([]);
    expect(Object.keys(await offsetsOf())).toContain('Logs/MyMod.log');
    expect(await readIgnores(game.userDataPath)).toEqual([{ source: 'HTTP', hits: 1 }]);
  });

  test(`settles ignored entries past the ordinary lines it read`, async () => {
    await runningGame();
    await ignoreError(context, { pattern: 'HTTP' });

    game.after(1, () => game.log('MyMod', `Just a line.`));
    game.after(6, () => game.log('MyMod', `HTTP failed.`, 'ERROR'));

    expect(await monitorFor(8)).toEqual([]);
    expect(await readIgnores(game.userDataPath)).toEqual([{ source: 'HTTP', hits: 1 }]);
  });

  test(`leaves what a line would tell to the gate, settling nothing`, async () => {
    await runningGame();
    await ignoreError(context, { pattern: 'HTTP' });
    await settledError(`HTTP failed.`);
    await settledError(`Held by the gate.`);

    const announced = await offsetsOf();

    await settleIgnored({ ...context, host: game });

    expect(await offsetsOf()).toEqual(announced);
    expect(await readIgnores(game.userDataPath)).toEqual([{ source: 'HTTP', hits: 0 }]);
    expect(await statusHint()).toBe(
      `Errors: 1 new, 1 kind, 1 in Logs/MyMod.log (1 ignored). See logs.`
    );
  });

  test(`holds its gap through a scan of ignored entries alone`, async () => {
    await runningGame();
    await ignoreError(context, { pattern: 'HTTP' });
    await settledError(`Told.`);

    game.after(6, () => game.log('MyMod', `Held by the gap.`, 'ERROR'));
    game.after(12, () => game.log('MyMod', `HTTP failed.`, 'ERROR'));

    expect(await monitorFor(10)).toEqual([`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`]);
  });

  test(`names a dropped pattern at once, moving past what it read`, async () => {
    await runningGame();
    await updateIgnores(context, () => [
      { source: 'HTTP', hits: 0 },
      { source: 'slow', hits: 0, isDropped: true }
    ]);
    await settledError(`HTTP failed.`);

    expect(await monitorFor(1)).toEqual([`Dropped ignore pattern slow: it took too long.`]);
    expect(await readIgnores(game.userDataPath)).toEqual([{ source: 'HTTP', hits: 1 }]);
  });

  test(`spends no settle on ignored entries behind what the gate holds`, async () => {
    await runningGame();
    await ignoreError(context, { pattern: 'HTTP' });
    await settledError(`Told.`);

    game.after(6, () => game.log('MyMod', `Held by the gap.`, 'ERROR'));
    game.after(8, () => game.log('MyMod', `HTTP failed.`, 'ERROR'));
    game.after(12, () => game.log('MyMod', `HTTP failed.`, 'ERROR'));
    game.after(16, () => game.log('MyMod', `HTTP failed.`, 'ERROR'));

    const scans = spyOn(game, 'findPids');

    await monitorFor(10);

    // One a scan and one for the line: a settle would scan again.
    expect(scans).toHaveBeenCalledTimes(11);
  });

  test(`spends no settle on ignored entries behind repeats no line told`, async () => {
    await runningGame();
    await ignoreError(context, { pattern: 'HTTP' });
    await settledError(`Told.`);

    game.after(3, () => game.log('MyMod', `Told.`, 'ERROR'));
    game.after(6, () => game.log('MyMod', `HTTP failed.`, 'ERROR'));
    game.after(10, () => game.log('MyMod', `HTTP failed.`, 'ERROR'));
    game.after(14, () => game.log('MyMod', `HTTP failed.`, 'ERROR'));

    const scans = spyOn(game, 'findPids');

    await monitorFor(10);

    expect(scans).toHaveBeenCalledTimes(11);
    expect(await statusHint()).toBe(
      `Errors: 1 new, 1 kind, 1 in Logs/MyMod.log (3 ignored). See logs.`
    );
  });

  test(`settles ignored entries again once a line told what it held`, async () => {
    await runningGame();
    await ignoreError(context, { pattern: 'HTTP' });
    await settledError(`Told.`);

    game.after(6, async () => {
      await game.log('MyMod', `HTTP failed.`, 'ERROR');
      await game.log('MyMod', `Ends the entry above.`);
    });

    await monitorFor(6);

    expect(await readIgnores(game.userDataPath)).toEqual([{ source: 'HTTP', hits: 1 }]);
  });

  test(`settles a new run's ignored entries past what the last run held`, async () => {
    await runningGame();
    await ignoreError(context, { pattern: 'HTTP' });
    await settledError(`Told.`);

    game.after(3, async () => {
      await game.log('MyMod', `Told.`, 'ERROR');
      await game.log('MyMod', `Ends the entry above.`);
    });

    game.after(6, async () => {
      await rm(game.logPath('SceneFlow'));
      await rm(game.logPath('MyMod'));
      await game.start();
      await game.log('SceneFlow', 'GameManager created!');
      await game.log('MyMod', `HTTP failed.`, 'ERROR');
      await game.log('MyMod', `Filler ${'x'.repeat(200)}`);
    });

    expect(await monitorFor(6)).toEqual([`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`]);
    expect(await readIgnores(game.userDataPath)).toEqual([{ source: 'HTTP', hits: 1 }]);
  });

  test(`reads a new run from its start, past where it read the last`, async () => {
    await runningGame();
    await settledError(`First run.`);

    game.after(6, async () => {
      await rm(game.logPath('SceneFlow'));
      await rm(game.logPath('MyMod'));
      await game.start();
      await game.log('SceneFlow', 'GameManager created!');
      await game.log('MyMod', `Second run.`, 'ERROR');
      await game.log('MyMod', `Filler ${'x'.repeat(200)}`);
    });

    expect(await monitorFor(20)).toEqual([
      `Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`,
      `Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`
    ]);
  });

  test(`abandons a scan that never settles, and the next one runs`, async () => {
    await runningGame();
    await settledError(`It failed.`);

    game.stuckOn = 'findPids';

    const watching = monitorFor(3);

    await game.reachedStuck;
    game.stuckOn = undefined;

    await game.sleep(SCAN_CEILING_SECONDS * 1000);

    expect(await watching).toEqual([`Errors: 1 new, 1 kind, 1 in Logs/MyMod.log. See logs.`]);
  });
});

describe(`the fixtures`, () => {
  test(`carry no user name, token or machine path`, async () => {
    const files = await readdir(fixturesPath, { recursive: true, withFileTypes: true });
    const logFiles = files.filter(file => file.isFile() && file.name.endsWith('.log'));

    expect(logFiles.length).toBeGreaterThan(0);

    for (const file of logFiles) {
      const text = await readFile(path.join(file.parentPath, file.name), 'utf8');

      expect(text).not.toMatch(/\/home\/|76561198|\b192\.168\.|bearer |\bmorgan\b|\btoverux\b/iu);
    }
  });
});

/**
 * Scans a stopped run's whole logs.
 */
function scanRun(request: Partial<ScanRequest>): Promise<LogScan> {
  return scanLogs({
    host: game,
    basePath: game.userDataPath,
    runStart: new Date(0),
    isRunning: false,
    offsets: {},
    mod: undefined,
    signal: context.signal,
    accepts: () => true,
    findIgnore: createIgnoreMatcher([]).findIgnore,
    ...request
  });
}

/**
 * Crashpad writes a report, stamped on the game's clock.
 */
async function crashReport(): Promise<void> {
  const folder = path.join(game.userDataPath, '.cache', 'backtrace', 'crashpad', 'reports');
  const report = path.join(folder, `${game.seconds()}.dmp`);

  await game.sleep(1000);
  await mkdir(folder, { recursive: true });
  await writeFile(report, '');
  await utimes(report, game.now(), game.now());
}
