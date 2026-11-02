import { describe, expect, test } from 'bun:test';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import {
  createLogParser,
  createPlayerLogParser,
  type EntryParser,
  EXCEPTION_LEVEL,
  type GameLogEntry,
  isErrorLevel,
  parseLog,
  PLAYER_ENTRY_LINES
} from '../src/game-log';

const sceneFlowLog = [
  '[2026-10-23 12:38:10,593] [INFO]  Boot completed',
  '[2026-10-23 12:38:44,248] [INFO]  Loading mode Game with purpose LoadGame',
  '[2026-10-23 12:38:49,863] [WARN]  Unknown prefab ID: CarPrefab:MuscleCar01',
  '[2026-10-23 12:38:50,100] [ERROR]  System.NullReferenceException: Object reference not set',
  '  at Game.Simulation.CitySystem.OnUpdate () [0x00000] in <00000000000000000000000000000000>:0',
  '',
  '[2026-10-23 12:39:02,678] [INFO]  Loading completed'
].join('\n');

describe('parseLog', () => {
  test(`reads the stamp in local time, the level and the message`, () => {
    const [first] = parseLog(sceneFlowLog);

    expect(first).toEqual({
      time: new Date('2026-10-23T12:38:10.593'),
      level: 'INFO',
      message: 'Boot completed'
    });
  });

  test(`joins the unstamped lines to the message before them`, () => {
    const error = parseLog(sceneFlowLog).find(entry => entry.level == 'ERROR');

    expect(error?.message).toBe(
      'System.NullReferenceException: Object reference not set\n' +
        '  at Game.Simulation.CitySystem.OnUpdate () [0x00000] in <00000000000000000000000000000000>:0'
    );
  });

  test('keeps the entries stamped at or after `since`', () => {
    const entries = parseLog(sceneFlowLog, new Date('2026-10-23T12:38:49.863'));

    expect(entries.map(entry => entry.level)).toEqual(['WARN', 'ERROR', 'INFO']);
  });

  test(`reads a log written with Windows line endings`, () => {
    expect(parseLog(sceneFlowLog.replaceAll('\n', '\r\n'))).toEqual(parseLog(sceneFlowLog));
  });
});

describe('error levels', () => {
  function errorEntries(log: string): readonly string[] {
    return parseLog(log)
      .filter(entry => isErrorLevel(entry.level))
      .map(entry => entry.level);
  }

  test(`keeps the entries at an error level`, () => {
    expect(errorEntries(sceneFlowLog)).toEqual(['ERROR']);
  });

  test(`keeps nothing from a clean log`, () => {
    expect(errorEntries('[2026-10-23 12:38:10,593] [INFO]  Boot completed')).toEqual([]);
  });
});

/**
 * Feeds a parser a log's lines, as the scanner does.
 */
function entriesOf(parser: EntryParser, lines: readonly string[]): GameLogEntry[] {
  const pushed = lines.map((text, index) =>
    parser.push({ text, number: index + 1, offset: index })
  );

  return [...pushed, parser.finish()].filter(entry => entry != undefined);
}

describe('createLogParser', () => {
  test(`keeps a message's lines and their place in the file`, () => {
    const [first, second] = entriesOf(createLogParser(), sceneFlowLog.split('\n').slice(3));

    expect(first).toMatchObject({ line: 1, level: 'ERROR' });
    expect(first?.text.split('\n')).toHaveLength(2);
    expect(second).toMatchObject({ line: 4, level: 'INFO', text: 'Loading completed' });
  });
});

describe('createPlayerLogParser', () => {
  const handlerLine = 'UnityEngine.DebugLogHandler:Internal_LogException(Exception, Object)';

  const exception = [
    'InvalidOperationException: it threw',
    '  at MyMod.Cases.Run () [0x00000] in D:/Mods/MyMod/Cases.cs:12',
    handlerLine,
    'UnityEngine.Debug:LogException(Exception)',
    '',
    '(Filename: D:/Mods/MyMod/Cases.cs Line: 12)',
    ''
  ];

  test(`reads a tagged line, without the engine's noise around it or its native stack`, () => {
    const entries = entriesOf(createPlayerLogParser(), [
      'Uploaded shader variant to the GPU driver',
      '[HallOfFame] [ERROR]  It failed.',
      '  at HallOfFame.Run () [0x00000] in <0>:0',
      '0x00006ffffb573a7d (unityplayer) ',
      '',
      'Uploaded shader variant to the GPU driver'
    ]);

    expect(entries).toHaveLength(1);
    expect(entries[0]).toMatchObject({
      line: 2,
      logger: 'HallOfFame',
      level: 'ERROR',
      text: 'It failed.\n  at HallOfFame.Run () [0x00000] in <0>:0'
    });
  });

  test(`ends a tagged entry at the next tagged line`, () => {
    const entries = entriesOf(createPlayerLogParser(), [
      '[A] [WARN]  One.',
      '[B] [ERROR]  Two.',
      ''
    ]);

    expect(entries.map(entry => entry.logger)).toEqual(['A', 'B']);
  });

  test(`ends a tagged entry at its line bound, so noise with no blank line stays out`, () => {
    const noise = Array.from(
      { length: PLAYER_ENTRY_LINES + 100 },
      (_, index) => `Uploaded shader ${index}`
    );

    const entries = entriesOf(createPlayerLogParser(), [
      '[SceneFlow] [WARN]  Unknown prefab ID',
      ...noise,
      '[MyMod] [ERROR]  It failed.',
      ''
    ]);

    expect(entries.map(entry => entry.logger)).toEqual(['SceneFlow', 'MyMod']);
    expect(entries[0]?.text.split('\n')).toHaveLength(PLAYER_ENTRY_LINES);
    expect(entries[0]?.text).not.toContain(`Uploaded shader ${PLAYER_ENTRY_LINES}`);
  });

  test(`reads an exception Unity logged`, () => {
    const [entry] = entriesOf(createPlayerLogParser(), exception);

    expect(entry).toMatchObject({ line: 1, logger: undefined, level: EXCEPTION_LEVEL });
    expect(entry?.text.split('\n')).toHaveLength(4);
  });

  test(`reads an exception that starts mid-block`, () => {
    const entries = entriesOf(createPlayerLogParser(), ['Fallback handler said so', ...exception]);

    expect(entries.map(entry => entry.line)).toEqual([2]);
  });

  test(`takes an exception with no file named under it, as a build with no PDB logs it`, () => {
    const entries = entriesOf(createPlayerLogParser(), [
      'NullReferenceException: Object reference not set to an instance of an object',
      '  at MyMod.Cases.Run (System.String name) [0x00881] in <623262be86cd46848b78c54f53ba>:0 ',
      handlerLine,
      'UnityEngine.Debug:CallOverridenDebugHandler(Exception, Object)',
      '',
      '[SceneFlow] [INFO]  Still going',
      ''
    ]);

    expect(entries.map(entry => entry.level)).toEqual([EXCEPTION_LEVEL, 'INFO']);
    expect(entries[0]?.line).toBe(1);
  });

  test(`ends an exception at the blank line past its handler line, leaving noise out`, () => {
    const [entry] = entriesOf(createPlayerLogParser(), [
      'NullReferenceException: Object reference not set to an instance of an object',
      '  at MyMod.Cases.Run (System.String name) [0x00881] in <623262be86cd46848b78c54f53ba>:0 ',
      handlerLine,
      '',
      'Uploaded shader variant to the GPU driver',
      ''
    ]);

    expect(entry?.text).not.toContain('Uploaded shader');
  });

  test(`keeps a stack past the blank line Unity writes under a never-thrown wrapper`, () => {
    const [entry] = entriesOf(createPlayerLogParser(), [
      'InvalidOperationException: The cause',
      '  at MyMod.Cases.Caught () [0x00000] in <623262be86cd46848b78c54f53ba>:0 ',
      'Rethrow as AggregateException: The wrapper',
      '',
      handlerLine,
      'UnityEngine.Debug:LogException(Exception)',
      'MyMod.Cases:Run(String)',
      '',
      'Uploaded shader variant to the GPU driver',
      ''
    ]);

    expect(entry?.text).toContain('MyMod.Cases:Run(String)');
    expect(entry?.text).not.toContain('Uploaded shader');
  });

  test(`takes a never-thrown exception, which has no managed frame`, () => {
    const entries = entriesOf(createPlayerLogParser(), [
      'InvalidOperationException: Never thrown',
      handlerLine,
      'UnityEngine.Debug:LogException(Exception)',
      'MyMod.Cases:Run(String)',
      '',
      '[SceneFlow] [INFO]  Still going',
      ''
    ]);

    expect(entries.map(entry => entry.level)).toEqual([EXCEPTION_LEVEL, 'INFO']);
  });

  test(`takes an exception by a handler line past the line bound, which no kept line holds`, () => {
    const message = Array.from({ length: PLAYER_ENTRY_LINES }, () => 'A long message');

    const entries = entriesOf(createPlayerLogParser(), [
      'InvalidOperationException: The head',
      ...message,
      handlerLine,
      '',
      '[SceneFlow] [INFO]  Still going',
      ''
    ]);

    expect(entries.map(entry => entry.level)).toEqual([EXCEPTION_LEVEL, 'INFO']);
  });

  test(`takes no exception from one logged as text, whose frames Unity did not write`, () => {
    const entries = entriesOf(createPlayerLogParser(), [
      'System.InvalidOperationException: logged by Debug.LogWarning',
      '  at MyMod.Cases.Caught () [0x00002] in <c050ed2c6390477389422fd7527d116e>:0 ',
      'System.InvalidOperationException: logged by Debug.Log',
      '  at MyMod.Cases.Caught () [0x00002] in <c050ed2c6390477389422fd7527d116e>:0 ',
      '0x00006ffffc30c90d (unityplayer) ',
      '',
      '[SceneFlow] [INFO]  Still going',
      ''
    ]);

    expect(entries.map(entry => entry.level)).toEqual(['INFO']);
  });

  test(`takes no exception from an error that reads as one, with its native frames`, () => {
    const entries = entriesOf(createPlayerLogParser(), [
      'System.InvalidOperationException: logged as text',
      '0x00006ffffadf3a7d (unityplayer) ',
      '0x00006ffffadf9e19 (unityplayer) ',
      '',
      '[SceneFlow] [INFO]  Still going',
      ''
    ]);

    expect(entries.map(entry => entry.level)).toEqual(['INFO']);
  });

  test(`takes no exception from a block nothing confirms`, () => {
    const entries = entriesOf(createPlayerLogParser(), [
      'System.InvalidOperationException: printed by something',
      'Then a second line',
      '',
      'Uploaded shader variant to the GPU driver',
      ''
    ]);

    expect(entries).toEqual([]);
  });

  test(`takes a Burst job's exception by its sentence, with its code's frame`, () => {
    const frames = [
      '0x00006fffee9610ae (mymod_win_x86_64) MyMod.Job.Execute(ref MyMod.Job data)',
      '0x00006fffee9610ae (my.mod-core_win_x86_64) MyMod.Job.Execute(ref MyMod.Job data)'
    ];

    const [entry, ...rest] = entriesOf(createPlayerLogParser(), [
      'System.InvalidOperationException: thrown from a Burst job',
      'This Exception was thrown from a job compiled with Burst, which has limited exception',
      '0x00006ffffb573a7d (unityplayer) ',
      '0x00000000769dd0fa (Mono JIT Code) UnityEngine.Debug:Log (object)',
      '0x0000038100099513 ((<unknown>)) ',
      '0x00007FF845D77D9D (UnityPlayer) (function-name not available)',
      '0x00007FF8C43954E0 (KERNEL32) BaseThreadInitThunk',
      '0x0000018C2E3E193A (Mono JIT Code) UnityEngine.Debug:Log (object)',
      ...frames,
      '0x00006fffee961016 (mymod_win_x86_64) ',
      '0x00007FF845D77D9D (MyMod_win_x86_64) (function-name not available)',
      '',
      'Uploaded shader variant to the GPU driver',
      ''
    ]);

    expect(rest).toEqual([]);
    expect(entry).toMatchObject({ level: 'EXCEPTION', logger: undefined, line: 1 });
    expect(entry?.text.split('\n').slice(2)).toEqual(frames);
  });

  test(`takes an exception whose message ends in a blank line, as the game logs it`, () => {
    const entries = entriesOf(createPlayerLogParser(), [
      'Exception: A message with two line breaks',
      '',
      '  at MyMod.Cases.Run (System.String name) [0x0078e] in D:/Mods/MyMod/Cases.cs:82',
      '  at MyMod.Poller.Update () [0x00075] in D:/Mods/MyMod/Mod.cs:58',
      'UnityEngine.DebugLogHandler:Internal_LogException(Exception, Object)',
      'UnityEngine.Debug:CallOverridenDebugHandler(Exception, Object)',
      '',
      '(Filename: D:/Mods/MyMod/Cases.cs Line: 82)',
      ''
    ]);

    expect(entries).toHaveLength(1);
    expect(entries[0]?.text).toContain('at MyMod.Cases.Run');
    expect(entries[0]?.text).not.toContain('\n\n');
  });

  test(`takes an exception past a blank line and free text`, () => {
    const entries = entriesOf(createPlayerLogParser(), [
      'Exception: The first line of a message',
      '',
      'The rest of the message',
      '  at MyMod.Cases.Run (System.String name) [0x00783] in D:/Mods/MyMod/Cases.cs:82',
      handlerLine,
      '',
      ''
    ]);

    expect(entries).toHaveLength(1);
    expect(entries[0]?.text).toContain('The rest of the message');
  });

  test(`reads the exception blocks of live captures, on a patched and a stock game`, async () => {
    const text = await readFile(
      path.join(import.meta.dirname, 'fixtures', 'exceptions', 'Player.log'),
      'utf8'
    );

    const entries = entriesOf(createPlayerLogParser(), text.split(/\r?\n/u));

    expect(entries.map(entry => entry.text.split('\n', 1)[0]?.trimEnd())).toEqual([
      'Exception: Survey throw with a trailing newline',
      'Exception: Survey throw first',
      'Exception: Survey throw with two newlines',
      'InvalidProgramException: Invalid IL code in Survey.Type:Method (): IL_0000: ldarg.0',
      'NullReferenceException: Object reference not set to an instance of an object',
      'InvalidOperationException: Survey never thrown',
      'InvalidOperationException: Survey: a caught exception',
      'InvalidOperationException: Survey: a caught exception'
    ]);
    expect(entries[1]?.text).toContain('Survey throw after the blank line');
    expect(entries[6]?.text).toContain('ErrorSurvey.Cases:Run(String)');
    expect(entries[7]?.text).toContain('Rethrow as InvalidOperationException: Survey outer');
    expect(entries[7]?.text).toContain('ErrorSurvey.Cases:Run(String)');
  });

  test(`lets an unconfirmed exception go at the next one`, () => {
    const entries = entriesOf(createPlayerLogParser(), [
      'Exception-shaped noise from the engine',
      '',
      'Uploaded shader variant to the GPU driver',
      'Exception: The real one',
      '  at MyMod.Cases.Run (System.String name) [0x00783] in D:/Mods/MyMod/Cases.cs:82',
      handlerLine,
      '',
      ''
    ]);

    expect(entries.map(entry => entry.line)).toEqual([4]);
    expect(entries[0]?.text).not.toContain('noise');
  });

  test(`lets an unconfirmed exception go at a tagged line`, () => {
    const entries = entriesOf(createPlayerLogParser(), [
      'Exception-shaped noise from the engine',
      '',
      '[MyMod] [INFO]  Still going',
      ''
    ]);

    expect(entries.map(entry => entry.level)).toEqual(['INFO']);
  });

  test(`cuts an exception past the line bound, which its handler line still confirms`, () => {
    const frames = Array.from({ length: PLAYER_ENTRY_LINES }, (_, index) => `  at Link${index} ()`);

    const entries = entriesOf(createPlayerLogParser(), [
      'System.InvalidOperationException: outer',
      ...frames,
      'Rethrow as MyMod.LoadException: wrapped',
      handlerLine,
      ''
    ]);

    expect(entries.map(entry => entry.level)).toEqual(['EXCEPTION']);
    expect(entries[0]?.text.split('\n')).toHaveLength(PLAYER_ENTRY_LINES);
    expect(entries[0]?.text).not.toContain('Rethrow as');
  });

  test(`lets an unconfirmed exception go at a tagged line past the line bound`, () => {
    const noise = Array.from({ length: PLAYER_ENTRY_LINES }, () => 'Uploaded shader');

    const entries = entriesOf(createPlayerLogParser(), [
      'Exception-shaped noise from the engine',
      ...noise,
      '[MyMod] [INFO]  Still going',
      ''
    ]);

    expect(entries.map(entry => entry.level)).toEqual(['INFO']);
  });

  test(`takes a Burst job's exception a tagged line ends`, () => {
    const entries = entriesOf(createPlayerLogParser(), [
      'System.InvalidOperationException: thrown from a Burst job',
      'This Exception was thrown from a job compiled with Burst, which has limited exception',
      '0x00006ffffb573a7d (unityplayer) ',
      '[MyMod] [INFO]  Still going',
      ''
    ]);

    expect(entries.map(entry => entry.level)).toEqual(['EXCEPTION', 'INFO']);
  });

  test(`takes a Burst job's exception the file ends on, as Burst aborts the game`, () => {
    const entries = entriesOf(createPlayerLogParser(), [
      'System.InvalidOperationException: thrown from a Burst job',
      'This Exception was thrown from a job compiled with Burst, which has limited exception',
      '0x00006ffffb573a7d (unityplayer) '
    ]);

    expect(entries).toHaveLength(1);
  });

  test(`leaves an exception quoted in a tagged entry's stack to that entry`, () => {
    const entries = entriesOf(createPlayerLogParser(), [
      '[UI] [ERROR]  Asset not found',
      'Colossal.IO.AssetDatabase.AssetDatabaseRequestException: no such database',
      ''
    ]);

    expect(entries.map(entry => entry.logger)).toEqual(['UI']);
  });

  test(`holds an exception open until it ends, so an offset never passes it`, () => {
    const parser = createPlayerLogParser();

    parser.push({ text: exception[0] ?? '', number: 1, offset: 40 });

    expect(parser.openOffset()).toBe(40);
  });
});
