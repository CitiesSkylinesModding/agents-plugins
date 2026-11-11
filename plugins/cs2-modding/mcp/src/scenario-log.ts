// Reads the game's logs of an in-game test run: what each scenario logged and how it ended, and how
// the run itself ended. Pure functions over the logs' entries.

import { isErrorLevel, type LogEntry } from './game-log';

export interface ScenarioResult {
  readonly name: string;

  /**
   * The entries the scenario logged, from its start line to its finish line.
   */
  readonly entries: readonly LogEntry[];

  /**
   * Why the scenario failed, empty when it passed.
   */
  readonly failures: readonly string[];
}

export type RunVerdict = 'running' | 'finished' | 'died' | 'stuck' | 'did not start' | 'timed out';

export interface RunState {
  /**
   * The entries of `Automation.log` since the launch.
   */
  readonly automation: readonly LogEntry[];

  /**
   * The entries of `TestScenarios.log` since the launch.
   */
  readonly scenarios: readonly LogEntry[];

  readonly isGameAlive: boolean;
  readonly launchTime: Date;
  readonly now: Date;
}

export interface RunLimits {
  /**
   * Seconds the game may take to start the scenarios, from the launch.
   */
  readonly bootSeconds: number;

  /**
   * Seconds `TestScenarios.log` may stand still once the scenarios started:
   * a scenario loading a city logs nothing until the load ends.
   */
  readonly stallSeconds: number;

  /**
   * Seconds the whole run may take, from the launch.
   */
  readonly runSeconds: number;
}

/**
 * Printed by [symbol: Colossal.TestFramework.AutomationClientSystem].
 */
const SCENARIOS_STARTING = `Starting test scenarios.`;

/**
 * Printed by [symbol: Colossal.TestFramework.AutomationClientSystem].
 */
const SCENARIOS_FINISHED = `Finished test scenarios.`;

/**
 * Printed by [symbol: Colossal.TestFramework.TestScenarioSystem].
 */
const scenarioStartPattern = /^---\| START TEST SCENARIO: (?<name>.+?)\s*$/u;

/**
 * Printed by [symbol: Colossal.TestFramework.TestScenarioSystem].
 */
const scenarioFinishPattern =
  /^---\| FINISH TEST SCENARIO \((?<outcome>Success|Failure), exceptions: \d+, errors: \d+\): (?<name>.+?)\s*$/u;

/**
 * Printed by [symbol: Colossal.TestFramework.TestScenario].
 */
const testFinishPattern = /^---\| FINISH TEST \((?<outcome>Success|Failure), /u;

/**
 * Splits the entries of `TestScenarios.log` into its scenarios, and judges each one.
 * The game's own verdict is not enough: when the preparation of a scenario throws
 * (a load that failed, or errors logged before it started),
 * the game logs the error, runs no test, and still reports the scenario a success.
 * So a scenario passes only when the game reports it a success, at least one of its tests finished,
 * and nothing in it was logged at an error level.
 */
export function readScenarios(entries: readonly LogEntry[]): ScenarioResult[] {
  const scenarios: ScenarioResult[] = [];

  let current: { name: string; entries: LogEntry[] } | undefined;

  for (const entry of entries) {
    const startName = scenarioStartPattern.exec(entry.message)?.groups?.name;

    if (startName != null) {
      if (current) {
        scenarios.push(judgeScenario(current.name, current.entries));
      }

      current = { name: startName, entries: [entry] };

      continue;
    }

    if (!current) {
      continue;
    }

    current.entries.push(entry);

    const finish = scenarioFinishPattern.exec(entry.message)?.groups;

    if (finish?.name == current.name) {
      scenarios.push(judgeScenario(current.name, current.entries, finish.outcome));

      current = undefined;
    }
  }

  if (current) {
    scenarios.push(judgeScenario(current.name, current.entries));
  }

  return scenarios;
}

/**
 * How the run stands: finished once the game logged the end of its scenarios,
 * died when the game is gone before that, did not start past the boot deadline,
 * stuck when a scenario logs nothing for too long, timed out past the run's deadline.
 */
export function classifyRun(state: RunState, limits: RunLimits): RunVerdict {
  const elapsedSeconds = (state.now.getTime() - state.launchTime.getTime()) / 1000;

  if (findEntry(state.automation, SCENARIOS_FINISHED)) {
    return 'finished';
  }

  if (!state.isGameAlive) {
    return 'died';
  }

  const scenariosStart = findEntry(state.automation, SCENARIOS_STARTING);

  if (!scenariosStart) {
    return elapsedSeconds >= limits.bootSeconds ? 'did not start' : 'running';
  }

  // From the start of the scenarios at the earliest: the game writes TestScenarios.log at boot,
  // then nothing until the first scenario, a frame after each of its own it skips.
  const lastActivity = Math.max(
    scenariosStart.time.getTime(),
    state.scenarios.at(-1)?.time.getTime() ?? 0
  );

  if ((state.now.getTime() - lastActivity) / 1000 >= limits.stallSeconds) {
    return 'stuck';
  }

  return elapsedSeconds >= limits.runSeconds ? 'timed out' : 'running';
}

function findEntry(entries: readonly LogEntry[], message: string): LogEntry | undefined {
  return entries.find(entry => entry.message == message);
}

function judgeScenario(
  name: string,
  entries: readonly LogEntry[],
  outcome?: string
): ScenarioResult {
  const failures: string[] = [];

  if (outcome == null) {
    failures.push(`it did not finish`);
  } else if (outcome != 'Success') {
    failures.push(`the game reported a failure`);
  }

  const testOutcomes = entries.flatMap(entry => {
    const testOutcome = testFinishPattern.exec(entry.message)?.groups?.outcome;

    return testOutcome == null ? [] : [testOutcome];
  });

  if (outcome != null && testOutcomes.length == 0) {
    failures.push(`it ran no test: none finished, or its preparation threw`);
  }

  const failedTests = testOutcomes.filter(testOutcome => testOutcome != 'Success').length;

  if (failedTests > 0) {
    failures.push(`${failedTests} of its ${testOutcomes.length} tests failed`);
  }

  if (entries.some(entry => isErrorLevel(entry.level))) {
    failures.push(`it logged an error`);
  }

  return { name, entries, failures };
}
