import { describe, expect, test } from 'bun:test';
import path from 'node:path';
import { type LogEntry, parseLog } from '../src/game-log';
import {
  classifyRun,
  type RunLimits,
  readScenarios,
  type ScenarioResult
} from '../src/scenario-log';

function readFixture(name: string): Promise<string> {
  return Bun.file(path.join(import.meta.dir, 'fixtures', 'scenarios', `${name}.log`)).text();
}

async function scenariosOf(name: string): Promise<ScenarioResult[]> {
  return readScenarios(parseLog(await readFixture(name)));
}

/**
 * The run starts the scenarios at this time.
 */
const start = new Date(2026, 9, 23, 13, 39, 7);

function at(seconds: number): Date {
  return new Date(start.getTime() + seconds * 1000);
}

function logEntry(seconds: number, message: string): LogEntry {
  return { time: at(seconds), level: 'INFO', message };
}

const limits: RunLimits = { bootSeconds: 600, stallSeconds: 240, runSeconds: 1800 };

describe('readScenarios', () => {
  test(`passes a scenario the game reports a success, its tests run and nothing logged as an error`, async () => {
    const [scenario, ...others] = await scenariosOf('passed');

    expect(others).toEqual([]);
    expect(scenario?.name).toBe('Greenfield: Sample');
    expect(scenario?.failures).toEqual([]);
    expect(scenario?.entries.at(-1)?.message).toStartWith('---| FINISH TEST SCENARIO (Success');
  });

  test(`keeps the entries from the start line to the finish line`, async () => {
    const [scenario] = await scenariosOf('passed');

    expect(scenario?.entries[0]?.message).toStartWith('---| START TEST SCENARIO');
    expect(scenario?.entries.at(-1)?.message).toStartWith('---| FINISH TEST SCENARIO');
    expect(scenario?.entries.some(entry => entry.message.includes('[PASSED] Buildings'))).toBe(
      true
    );
  });

  test(`fails a scenario that ran no test, which the game reports a success`, async () => {
    const [scenario] = await scenariosOf('preparation-failed');

    expect(scenario?.entries.at(-1)?.message).toStartWith('---| FINISH TEST SCENARIO (Success');
    expect(scenario?.failures).toEqual([
      `it ran no test: none finished, or its preparation threw`,
      `it logged an error`
    ]);
  });

  test(`fails a scenario with a failed test`, async () => {
    const [scenario] = await scenariosOf('test-failed');

    expect(scenario?.failures).toEqual([
      `the game reported a failure`,
      `1 of its 2 tests failed`,
      `it logged an error`
    ]);
  });

  test(`fails a scenario that did not finish`, async () => {
    const [scenario] = await scenariosOf('unfinished');

    expect(scenario?.failures).toEqual([`it did not finish`]);
  });

  test(`reads no scenario from a log where none started`, async () => {
    const entries = parseLog(await readFixture('passed'));

    expect(readScenarios(entries.slice(0, 1))).toEqual([]);
  });
});

describe('classifyRun', () => {
  const booting = {
    automation: [],
    scenarios: [logEntry(-60, `24 test scenario available`)],
    isGameAlive: true,
    launchTime: at(-60),
    now: at(0)
  };

  const running = {
    ...booting,
    automation: [
      logEntry(-5, `Main menu reached for the first time.`),
      logEntry(0, `Starting test scenarios.`)
    ],
    scenarios: [...booting.scenarios, logEntry(0, '---| START TEST SCENARIO: A scenario')]
  };

  test(`is running while the game boots, within the boot deadline`, () => {
    expect(classifyRun({ ...booting, now: at(539) }, limits)).toBe('running');
  });

  test(`did not start when the scenarios do not start within the boot deadline`, () => {
    expect(classifyRun({ ...booting, now: at(540) }, limits)).toBe('did not start');
  });

  test(`is running when the scenarios just started after a boot longer than the stall time`, () => {
    const state = {
      ...running,
      scenarios: [logEntry(-300, `24 test scenario available`)],
      launchTime: at(-300),
      now: at(1)
    };

    expect(classifyRun(state, limits)).toBe('running');
  });

  test(`is running while the scenarios log`, () => {
    expect(classifyRun({ ...running, now: at(239) }, limits)).toBe('running');
  });

  test(`is running past the boot deadline once the scenarios started`, () => {
    const scenarios = [...running.scenarios, logEntry(899, '---| START TEST: A test')];

    expect(classifyRun({ ...running, scenarios, now: at(900) }, limits)).toBe('running');
  });

  test(`is stuck when the scenarios log nothing for the stall time`, () => {
    expect(classifyRun({ ...running, now: at(240) }, limits)).toBe('stuck');
  });

  test(`times out past the run deadline`, () => {
    const scenarios = [...running.scenarios, logEntry(1739, '---| START TEST: A test')];

    expect(classifyRun({ ...running, scenarios, now: at(1740) }, limits)).toBe('timed out');
  });

  test(`died when the game is gone before the end of the scenarios`, () => {
    expect(classifyRun({ ...running, isGameAlive: false }, limits)).toBe('died');
    expect(classifyRun({ ...booting, isGameAlive: false }, limits)).toBe('died');
  });

  test(`finished once the game logged the end of the scenarios, whatever came after`, () => {
    const finished = {
      ...running,
      automation: [...running.automation, logEntry(60, `Finished test scenarios.`)]
    };

    expect(classifyRun(finished, limits)).toBe('finished');
    expect(classifyRun({ ...finished, isGameAlive: false, now: at(9999) }, limits)).toBe(
      'finished'
    );
  });
});
