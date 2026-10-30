import { describe, expect, test } from 'bun:test';
import { parseLog } from '../src/game-log';

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
