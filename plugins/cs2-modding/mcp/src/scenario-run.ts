// Watches a run of a mod's in-game scenarios to its end, and says how it went.

import { readdir, stat } from 'node:fs/promises';
import path from 'node:path';
import {
  BOOT_SECONDS,
  isGameRunning,
  LOAD_SECONDS,
  POLL_MILLISECONDS,
  readLog,
  SCENARIO_RUN_SECONDS,
  secondsSince,
  type ToolContext
} from './cycle';
import { errorMessage, MILLISECONDS_PER_SECOND } from './game-host';
import { isErrorLevel } from './game-log';
import { crashReportsPath, createIgnoreMatcher, moveCursorPastListing, sum } from './hint';
import { readIgnores } from './ignores';
import { cutAt, describeLogs, LOGS_CAP_CHARACTERS } from './log-report';
import { QUIET_SECONDS, scanLogs } from './scanner';
import {
  classifyRun,
  readScenarios,
  type RunLimits,
  type RunVerdict,
  type ScenarioResult
} from './scenario-log';

/**
 * Seconds between two signs of life to a caller:
 * Claude Code aborts a stdio call that stays silent for 30 minutes,
 * and a scenario loading a city logs nothing for as long as the load takes.
 */
const HEARTBEAT_SECONDS = 60;

/**
 * Seconds a scenario may log nothing before the run counts as stuck:
 * a load's whole bound, as a scenario loading a city logs nothing until it ends, and a margin.
 */
export const STALL_SECONDS = LOAD_SECONDS + HEARTBEAT_SECONDS;

/**
 * Why a run with no scenario failed.
 * The tool cannot tell which, as a DLL's content cannot tell a Release build:
 * the run's own outcome is the only answer.
 */
const NO_SCENARIO = `no scenario ran: a Release build, no scenario written, or a category mismatch`;

const SCENARIOS_FILE = 'Logs/TestScenarios.log';

/**
 * Opens the memory figures the game logs after every scenario.
 * Printed by [symbol: Colossal.TestFramework.TestScenario].
 */
const METRICS_PREFIX = '[Metrics]';

const limits: RunLimits = {
  bootSeconds: BOOT_SECONDS,
  stallSeconds: STALL_SECONDS,
  runSeconds: SCENARIO_RUN_SECONDS
};

/**
 * How the watch ended.
 */
export type RunEnd = Exclude<RunVerdict, 'running'>;

/**
 * What a run came to, for the report.
 */
export interface ScenarioRun {
  readonly verdict: RunEnd;
  readonly scenarios: readonly ScenarioResult[];
  readonly seconds: number;
}

/**
 * The errors naming the mod outside its scenarios, each of which fails the run.
 */
export interface ModErrors {
  readonly count: number;

  /**
   * The errors naming no mod, which the moved cursor leaves no hint to tell of.
   */
  readonly otherCount: number;

  /**
   * The entries, one per kind, undefined with none.
   */
  readonly listing: string | undefined;

  /**
   * Why they could not be counted, which fails the run as they would.
   */
  readonly failure?: string;
}

export interface RunReport {
  readonly hasPassed: boolean;
  readonly text: string;
}

/**
 * Compares the sources of a mod with its deployed build, undefined when the build is current.
 * Fails on a folder that is not there, which a call makes before it stops the game.
 */
export async function readStaleBuild(
  { localModsPath }: ToolContext,
  { mod, sources }: { readonly mod: string; readonly sources: string }
): Promise<string | undefined> {
  const names = await readdir(sources, { recursive: true }).catch((error: unknown) => {
    throw new Error(`Cannot read the sources at ${sources}: ${errorMessage(error)}`, {
      cause: error
    });
  });

  const assemblyPath = path.join(localModsPath, mod, `${mod}.dll`);
  const built = await stat(assemblyPath).catch((): undefined => undefined);

  if (!built) {
    return `Warning: no deployed build at ${assemblyPath} to compare the sources with.`;
  }

  for (const name of names.filter(file => isSourceFile(file))) {
    const modified = await stat(path.join(sources, name)).then(
      ({ mtime }) => mtime,
      (): undefined => undefined
    );

    if (modified && modified > built.mtime) {
      return `Warning: ${name} changed after the deployed build, so this run tested older code.`;
    }
  }

  return undefined;
}

/**
 * Watches the logs and the game until the run ends, telling the caller of each scenario started.
 */
export async function watchScenarios(context: ToolContext, launchTime: Date): Promise<RunEnd> {
  const { host, progress } = context;

  // Mutable: where the run has got to.
  let started = 0;
  let step = `Waiting for the scenarios to start`;
  let lastBeat = launchTime;

  progress(step);

  for (;;) {
    // Ahead of the logs: a game that ends past its last line must not read as dead before it.
    const isGameAlive = await isGameRunning(host);
    const automation = await readLog(logPath(context, 'Automation'), launchTime);
    const entries = await readLog(logPath(context, 'TestScenarios'), launchTime);

    const verdict = classifyRun(
      { automation, scenarios: entries, isGameAlive, launchTime, now: host.now() },
      limits
    );

    if (verdict != 'running') {
      return verdict;
    }

    const scenarios = readScenarios(entries);
    const newest = scenarios.at(-1);

    if (scenarios.length > started && newest) {
      started = scenarios.length;
      step = `Running the scenario ${newest.name}`;
      lastBeat = host.now();

      progress(step);
    } else if (secondsSince(host, lastBeat) >= HEARTBEAT_SECONDS) {
      lastBeat = host.now();

      progress(`${step}, ${secondsSince(host, launchTime)} s after the launch`);
    }

    await host.sleep(POLL_MILLISECONDS);
  }
}

/**
 * Reads what the run left, once it ended.
 */
export async function readScenarioRun(
  context: ToolContext,
  { verdict, launchTime }: { readonly verdict: RunEnd; readonly launchTime: Date }
): Promise<ScenarioRun> {
  const entries = await readLog(logPath(context, 'TestScenarios'), launchTime);

  return {
    verdict,
    scenarios: readScenarios(entries),
    seconds: secondsSince(context.host, launchTime)
  };
}

/**
 * Reads the mod's errors since the launch from every log.
 * An ignore pattern marks an entry for its hit count and takes nothing out of what is counted.
 * `TestScenarios.log` is read too, for the cursor, and judged by its scenarios alone.
 * Moves the cursor past a listing, so the result's hint tells only what came after it.
 * A failed read keeps the report, as the scenarios did run.
 */
export async function readModErrors(
  context: ToolContext,
  { mod, launchTime }: { readonly mod: string; readonly launchTime: Date }
): Promise<ModErrors> {
  const { host, userDataPath, signal } = context;

  try {
    const isRunning = await isGameRunning(host);

    // A live game's last entry waits for a quiet file, and the run may have ended on one.
    if (isRunning) {
      await host.sleep(QUIET_SECONDS * MILLISECONDS_PER_SECOND);
    }

    const matcher = createIgnoreMatcher(await readIgnores(userDataPath));

    const scan = await scanLogs({
      host,
      basePath: userDataPath,
      runStart: launchTime,
      isRunning,
      offsets: {},
      mod,
      signal,
      accepts: ({ level }) => isErrorLevel(level),
      findIgnore: matcher.findIgnore
    });

    const kinds = [...scan.kinds.values()].filter(({ file }) => file != SCENARIOS_FILE);
    const named = kinds.filter(({ modCount }) => modCount > 0);

    if (named.length == 0) {
      return { count: 0, otherCount: 0, listing: undefined };
    }

    const listing = describeLogs(
      { ...scan, kinds: new Map(named.map(kind => [kind.key, kind])) },
      {
        basePath: userDataPath,
        shouldShowIgnored: true,
        isModOnly: true,
        shouldPointBack: false
      }
    );

    // A failed move costs one repeated hint, which is no reason to lose what was counted.
    await moveCursorPastListing(context, scan, matcher.newlyDropped).catch(
      (): undefined => undefined
    );

    return {
      count: sum(named, kind => kind.modCount),
      otherCount: sum(kinds, kind => kind.count - kind.modCount),
      listing
    };
  } catch (error) {
    return { count: 0, otherCount: 0, listing: undefined, failure: errorMessage(error) };
  }
}

/**
 * Says how a run went: what decides first, the game's state next, and each scenario's lines last.
 */
export function describeRun(
  run: ScenarioRun,
  {
    userDataPath,
    mod,
    errors,
    lines
  }: {
    readonly userDataPath: string;
    readonly mod: string;
    readonly errors: ModErrors;
    readonly lines: readonly string[];
  }
): RunReport {
  const { verdict, scenarios, seconds } = run;
  const failedScenarios = scenarios.filter(scenario => scenario.failures.length > 0);

  const failures = [
    ...describeEnd(run),
    ...(failedScenarios.length > 0
      ? [`${failedScenarios.length} of ${scenarios.length} scenarios failed`]
      : []),
    ...(errors.count > 0 ? [`the mod logged ${errors.count} errors`] : []),
    ...(errors.failure == undefined
      ? []
      : [`the mod's errors could not be counted: ${errors.failure}`])
  ];

  const hasPassed = failures.length == 0;

  const summary = hasPassed
    ? `PASSED: ${scenarios.length} scenarios, ${seconds} s after the launch.`
    : `FAILED: ${failures.join('; ')}.`;

  return {
    hasPassed,
    text: [
      [
        summary,
        ...(verdict == 'died' ? [`Crash dumps: ${crashReportsPath(userDataPath)}`] : []),
        ...(errors.otherCount > 0
          ? [`${errors.otherCount} other errors name no ${mod}: logs reads them.`]
          : []),
        ...lines,
        ...scenarios.map(scenario => describeOutcome(scenario))
      ].join('\n'),
      ...(errors.listing == undefined ? [] : [errors.listing]),
      cutAt(scenarios.map(scenario => describeLines(scenario)).join('\n\n'), LOGS_CAP_CHARACTERS)
    ]
      .filter(Boolean)
      .join('\n\n')
  };
}

/**
 * What the way the run ended fails it on, nothing when it ended well.
 * Finished or not started are the only ends a run with none may have:
 * any other names what really happened.
 */
function describeEnd({ verdict, scenarios }: ScenarioRun): readonly string[] {
  const isEmpty = verdict == 'finished' || verdict == 'did not start';

  if (isEmpty && scenarios.length == 0) {
    return [NO_SCENARIO];
  }

  switch (verdict) {
    case 'finished': {
      return [];
    }
    case 'died': {
      return [`the game died`];
    }
    case 'stuck': {
      return [`a scenario logged nothing for ${limits.stallSeconds} s`];
    }
    case 'did not start': {
      return [`the scenarios did not start within ${limits.bootSeconds} s`];
    }
    case 'timed out': {
      return [`the run passed its ${limits.runSeconds} s`];
    }
    default: {
      throw new Error(`Unhandled verdict ${JSON.stringify(verdict satisfies never)}.`);
    }
  }
}

function describeOutcome({ name, failures }: ScenarioResult): string {
  return failures.length == 0 ? `${name}: passed` : `${name}: FAILED: ${failures.join('; ')}.`;
}

function describeLines({ name, entries }: ScenarioResult): string {
  const lines = entries
    .filter(({ level, message }) => !(level == 'INFO' && message.startsWith(METRICS_PREFIX)))
    .map(({ level, message }) => {
      const tag = level == 'INFO' ? '' : `[${level}] `;

      return `  ${tag}${message.replaceAll('\n', '\n    ')}`;
    });

  return [name, ...lines].join('\n');
}

/**
 * Whether a path under a mod's sources goes into its build.
 */
function isSourceFile(name: string): boolean {
  return (
    !/(?:^|[/\\])(?:bin|obj|node_modules)[/\\]/u.test(name) &&
    /\.(?:cs|csproj|props|targets)$|(?:^|[/\\])Locales[/\\][^/\\]+\.json$/u.test(name)
  );
}

function logPath({ userDataPath }: ToolContext, name: string): string {
  return path.join(userDataPath, 'Logs', `${name}.log`);
}
