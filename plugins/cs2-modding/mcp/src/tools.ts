// The tools: each strings the cycle's steps together and says how it went.

import { rm } from 'node:fs/promises';
import path from 'node:path';
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { isAssetGuid, readAssetGuid } from './asset-guid';
import { toolFolderPath, updateCursor } from './cursor';
import {
  BOOT_SECONDS,
  describeProcesses,
  LOAD_SECONDS,
  READ_CEILING_SECONDS,
  RESTART_CEILING_SECONDS,
  STOP_CEILING_SECONDS,
  TEST_CEILING_SECONDS,
  WAIT_LOAD_CEILING_SECONDS,
  isGameRunning,
  launchGame,
  readLog,
  requireWholeOptions,
  secondsSince,
  type StopOutcome,
  stopGame,
  type ToolContext,
  waitForLoad,
  waitForMainMenu,
  withGameLeftRunning
} from './cycle';
import { keepPreviousRun, PREVIOUS_RUN_FOLDER } from './evidence';
import { errorMessage } from './game-host';
import { isErrorLevel, logLevels, type LogEntry } from './game-log';
import { createIgnoreMatcher } from './hint';
import { markDropped, readIgnores, requireValidPattern, updateIgnores } from './ignores';
import { describeLogs, describePatterns, describeStatus } from './log-report';
import { listLogFiles, readRunStart, scanLogs } from './scanner';
import {
  describeRun,
  readModErrors,
  readScenarioRun,
  readStaleBuild,
  watchScenarios
} from './scenario-run';
import { type GuardedContext, readHint, runTool } from './tool-run';

/**
 * The guid of Archipelago, a map of the base game.
 */
const ARCHIPELAGO_GUID = 'bf8036b291428535b986c757fda3e627';

/**
 * The scenario category a mod made from the template uses, `1 << 20`.
 */
export const DEFAULT_CATEGORY = 1_048_576;

export interface RestartRequest {
  readonly save?: string | undefined;
  readonly newCity?: { readonly map?: string | undefined } | undefined;
  readonly gameOptions?: readonly string[] | undefined;
  readonly mod?: string | undefined;
}

export interface TestRequest {
  readonly mod: string;
  readonly category?: number | undefined;
  readonly sources?: string | undefined;
  readonly keepOpen?: boolean | undefined;
  readonly gameOptions?: readonly string[] | undefined;
}

export interface LogsRequest {
  readonly file?: string | undefined;
  readonly level?: string | undefined;
  readonly mod?: string | undefined;
  readonly errorsOnly?: boolean | undefined;
  readonly previous?: boolean | undefined;
  readonly ignored?: boolean | undefined;
}

export interface IgnoreRequest {
  readonly pattern: string;
  readonly remove?: boolean | undefined;
}

export function stop(context: ToolContext): Promise<CallToolResult> {
  const run = { name: 'stop', ceilingSeconds: STOP_CEILING_SECONDS, isExclusive: true };

  return runTool(context, run, async guarded => {
    guarded.progress(`Stopping the game`);

    return describeStop(await stopGame(guarded));
  });
}

/**
 * Stops the game, keeps the ended run's evidence, and launches it.
 * Returns at the main menu, or once the save or the new city loaded.
 */
export function restart(context: ToolContext, request: RestartRequest): Promise<CallToolResult> {
  const run = {
    name: 'restart',
    ceilingSeconds: RESTART_CEILING_SECONDS,
    isExclusive: true,
    // A call given up on never reaches its own cleanup, and the next launch must not read these
    // options.
    onGiveUp: () => rm(path.join(context.userDataPath, 'runOnce.txt'), { force: true })
  };

  return runTool(context, run, async guarded => {
    if (request.save != null && request.newCity) {
      throw new Error(`Pass a save or a new city, not both.`);
    }

    requireWholeOptions(request.gameOptions ?? []);

    // Before the stop: a save that does not exist leaves a running game alone.
    const startGuid = await readStartGuid(guarded.userDataPath, request);

    return restartGame(guarded, request, startGuid);
  });
}

/**
 * Stops the game, keeps the ended run's evidence,
 * launches it with a mod's scenarios as its test filter, and returns how they went.
 */
export function testMod(context: ToolContext, request: TestRequest): Promise<CallToolResult> {
  const run = {
    name: 'test',
    ceilingSeconds: TEST_CEILING_SECONDS,
    isExclusive: true,
    onGiveUp: () => rm(path.join(context.userDataPath, 'runOnce.txt'), { force: true })
  };

  return runTool(context, run, async guarded => {
    requireWholeOptions(request.gameOptions ?? []);

    // Before the stop: a folder that does not exist leaves a running game alone.
    const { mod, sources } = request;
    const staleBuild =
      sources == undefined ? undefined : await readStaleBuild(guarded, { mod, sources });

    return runScenarios(guarded, request, staleBuild);
  });
}

/**
 * Returns once the last logged load completed, the loading screen included.
 */
export function waitLoad(context: ToolContext): Promise<CallToolResult> {
  // Not exclusive: it only reads, and an agent may wait on a load beside anything else.
  const run = { name: 'wait_load', ceilingSeconds: WAIT_LOAD_CEILING_SECONDS, isExclusive: false };

  return runTool(context, run, async guarded => {
    const { host, progress } = guarded;

    progress(`Waiting for the load`);

    // A load the stopped game logged last would pass for this one.
    if (!(await isGameRunning(host))) {
      throw new Error(`The game is not running.`);
    }

    await withGameLeftRunning(host, () =>
      waitForLoad(guarded, {
        since: new Date(0),
        fallbackSince: host.now(),
        seconds: LOAD_SECONDS
      })
    );

    return `The game loaded it.`;
  });
}

/**
 * Says what the game is doing, from its process and its logs.
 */
export function status(context: ToolContext): Promise<CallToolResult> {
  const run = { name: 'status', ceilingSeconds: READ_CEILING_SECONDS, isExclusive: false };

  return runTool(context, run, async guarded => {
    const { host, userDataPath, progress } = guarded;

    progress(`Reading the game's state`);

    return describeStatus(userDataPath, await host.findPids());
  });
}

/**
 * Returns a run's entries, one per kind, within a cap: a storm costs what one occurrence does.
 */
export function logs(context: ToolContext, request: LogsRequest): Promise<CallToolResult> {
  const run = { name: 'logs', ceilingSeconds: READ_CEILING_SECONDS, isExclusive: false };

  return runTool(context, run, async guarded => {
    const { host, userDataPath, progress } = guarded;
    const basePath = request.previous
      ? path.join(toolFolderPath(userDataPath), PREVIOUS_RUN_FOLDER)
      : userDataPath;

    progress(`Reading the logs`);

    const runStart = await readRunStart(basePath);

    if (!runStart) {
      return request.previous ? `No previous run was kept.` : `No run has logged yet.`;
    }

    const stored = await readIgnores(userDataPath);
    const matcher = createIgnoreMatcher(stored);
    const level = request.level?.toUpperCase();
    const hasFile = fileFilter(request.file);

    if (level != undefined && !logLevels.includes(level)) {
      throw new Error(`No level ${level}: it is one of ${logLevels.join(', ')}.`);
    }

    if (hasFile) {
      await requireLogFile(basePath, request.file, hasFile);
    }

    const scan = await scanLogs({
      host,
      basePath,
      runStart,
      isRunning: request.previous ? false : await isGameRunning(host),
      offsets: {},
      mod: request.mod,
      signal: guarded.signal,
      hasFile,
      shouldKeepTwins: hasFile?.('Player.log') ?? false,
      accepts: entry =>
        (level == undefined || entry.level == level) &&
        (!request.errorsOnly || isErrorLevel(entry.level)),
      findIgnore: matcher.findIgnore
    });

    await markDropped(guarded, matcher.newlyDropped);

    return describeLogs(scan, {
      basePath,
      shouldShowIgnored: request.ignored ?? false,
      isModOnly: request.mod != undefined,

      // `errorsOnly` is how a hint's `See logs` gets read, and any other filter may match nothing.
      shouldPointBack:
        !request.previous && level == undefined && !hasFile && request.mod == undefined
    });
  });
}

/**
 * Adds or removes a pattern silencing errors for the session, returning the active ones.
 */
export function ignoreError(context: ToolContext, request: IgnoreRequest): Promise<CallToolResult> {
  const run = { name: 'ignore_error', ceilingSeconds: READ_CEILING_SECONDS, isExclusive: false };

  return runTool(context, run, async guarded => {
    const { pattern, remove } = request;

    if (!remove) {
      requireValidPattern(pattern);
    }

    const next = await updateIgnores(guarded, stored => {
      const isKnown = stored.some(({ source }) => source == pattern);

      if (remove && !isKnown) {
        throw new Error(`No ignore pattern ${pattern}.`);
      }

      return remove
        ? stored.filter(({ source }) => source != pattern)
        : [...stored, ...(isKnown ? [] : [{ source: pattern, hits: 0 }])];
    });

    return describePatterns(next);
  });
}

/**
 * The stop, the kept evidence, and the launch, returning what they came to.
 */
async function restartGame(
  guarded: GuardedContext,
  request: RestartRequest,
  startGuid: string | undefined
): Promise<string> {
  const { host, userDataPath, progress } = guarded;
  const stopOutcome = await endRun(guarded, 'restart', request.mod);
  const launchTime = host.now();

  progress(`Launching the game`);

  const options = await launchGame(guarded, [
    ...(startGuid ? [`--startGame=${startGuid}`] : []),
    ...(request.gameOptions ?? [])
  ]);

  progress(startGuid ? `Waiting for the load` : `Waiting for the main menu`);

  await withGameLeftRunning(host, () =>
    startGuid
      ? waitForLoad(guarded, {
          since: launchTime,
          fallbackSince: launchTime,
          seconds: BOOT_SECONDS + LOAD_SECONDS
        })
      : waitForMainMenu(guarded, launchTime)
  );

  const seconds = secondsSince(host, launchTime);
  const reached = startGuid ? `The game loaded it` : `The game is at the main menu`;

  return [
    `${reached}, ${seconds} s after the launch.`,
    ...(stopOutcome.kind == 'notRunning' ? [] : [describeStop(stopOutcome)]),
    // The game ignores an option it does not know, a mistyped one included.
    `Options written to runOnce.txt: ${options}`,
    ...(request.mod ? await describeModLoad(userDataPath, request.mod, launchTime) : [])
  ].join('\n');
}

/**
 * The stop, the kept evidence, the launch with the mod's scenarios as the test filter, the watch,
 * and the game's end unless it is to stay, returning how the run went or failing with it.
 */
async function runScenarios(
  guarded: GuardedContext,
  request: TestRequest,
  staleBuild: string | undefined
): Promise<string> {
  const { host, progress } = guarded;
  const { mod, category = DEFAULT_CATEGORY, keepOpen, gameOptions = [] } = request;

  const stopOutcome = await endRun(guarded, 'test', mod);
  const launchTime = host.now();

  progress(`Launching the game`);

  const options = await launchGame(guarded, [`--categoryFilter=${category}`, ...gameOptions]);

  const verdict = await withGameLeftRunning(host, () => watchScenarios(guarded, launchTime));
  const gameLines = keepOpen ? await describeProcesses(host) : await endScenarioRun(guarded);
  const errors = await readModErrors(guarded, { mod, launchTime });

  const report = describeRun(await readScenarioRun(guarded, { verdict, launchTime }), {
    userDataPath: guarded.userDataPath,
    mod,
    errors,
    lines: [
      ...(staleBuild == undefined ? [] : [staleBuild]),
      ...(stopOutcome.kind == 'notRunning'
        ? []
        : [`Before the launch: ${describeStop(stopOutcome)}`]),
      ...gameLines,
      `Options written to runOnce.txt: ${options}`,
      ...(await describeModLoad(guarded.userDataPath, mod, launchTime))
    ]
  });

  if (!report.hasPassed) {
    throw new Error(report.text);
  }

  return report.text;
}

/**
 * Ends the game a run left. Its failure joins the report: the scenarios still ran.
 */
async function endScenarioRun(guarded: GuardedContext): Promise<readonly string[]> {
  guarded.progress(`Stopping the game`);

  try {
    const outcome = await stopGame(guarded);

    return outcome.kind == 'notRunning' ? [] : [describeStop(outcome)];
  } catch (error) {
    return [`The game could not be stopped: ${errorMessage(error)}`];
  }
}

/**
 * Stops the game and keeps its evidence, ahead of a launch.
 */
async function endRun(
  guarded: GuardedContext,
  caller: 'restart' | 'test',
  mod: string | undefined
): Promise<StopOutcome> {
  const { userDataPath, progress, signal, carry } = guarded;

  if (mod != null) {
    await updateCursor(guarded, cursor => ({ ...cursor, mod }));
  }

  progress(`Stopping the game`);

  const stopOutcome = await stopGame(guarded);

  progress(`Keeping the ended run's evidence`);

  await keepPreviousRun(userDataPath, signal);

  // The launch starts the scan over, so this is the last call to tell of the ended run, and only
  // now does `previous` read it and do its shutdown's errors count.
  progress(`Reading the ended run's errors`);

  const ended = await readHint(guarded);

  if (ended != undefined) {
    carry(`The run this ${caller} ends: ${ended} logs reads it with previous: true.`);
  }

  return stopOutcome;
}

/**
 * The guid `--startGame` takes for a request, which no main menu trigger races:
 * the game loads a save or starts a new city on a map as soon as it booted.
 */
function readStartGuid(
  userDataPath: string,
  { save, newCity }: RestartRequest
): Promise<string | undefined> {
  if (save != null) {
    return readAssetGuid(userDataPath, 'Saves', save, 'SaveGameMetadata');
  }

  const map = newCity?.map;

  if (map != null && !isAssetGuid(map)) {
    return readAssetGuid(userDataPath, 'Maps', map, 'MapMetadata');
  }

  return Promise.resolve(map ?? (newCity ? ARCHIPELAGO_GUID : undefined));
}

/**
 * What `Logs/Modding.log` shows of a mod's load since the launch.
 * (VOLATILE: the Burst line -- `Colossal.IO.AssetDatabase.ExecutableAsset`.)
 * (VOLATILE: the error line -- `Game.Modding.ModManager`.)
 */
async function describeModLoad(
  userDataPath: string,
  mod: string,
  launchTime: Date
): Promise<readonly string[]> {
  let entries: readonly LogEntry[];

  try {
    entries = await readLog(path.join(userDataPath, 'Logs', 'Modding.log'), launchTime);
  } catch (error) {
    // The game booted: a report that cannot be read must not fail the call.
    return [`Mod ${mod}: ${errorMessage(error)}`];
  }

  const burstPrefix = `Loaded additional Burst code `;
  const burstFile = `${mod}_win_x86_64.dll`;

  const burst = entries.find(
    ({ message }) => message.startsWith(burstPrefix) && path.posix.basename(message) == burstFile
  );

  const errors = entries
    .map(({ message }) => message.split('\n')[0] ?? '')
    // `ModManager` logs the assembly's full name in both slots, so the name goes on after a comma.
    .filter(line => line.startsWith(`Error initializing mod ${mod}, `));

  return [
    burst
      ? `Mod ${mod}: Burst code loaded from ${burst.message.slice(burstPrefix.length)}`
      : `No Burst code logged for ${mod}.`,
    ...errors.map(line => `Mod ${mod}: ${line}`)
  ];
}

/**
 * Fails on a file filter that matches no log, which would otherwise read as a clean run.
 */
async function requireLogFile(
  basePath: string,
  name: string | undefined,
  hasFile: (file: string) => boolean
): Promise<void> {
  const files = await listLogFiles(basePath);

  if (!files.some(file => hasFile(file))) {
    throw new Error(`No log ${name}: it is one of ${files.join(', ')}.`);
  }
}

/**
 * Reads a file named as an agent would: `HallOfFame`, `HallOfFame.log`, or its path.
 */
function fileFilter(name: string | undefined): ((file: string) => boolean) | undefined {
  if (name == undefined) {
    return undefined;
  }

  const wanted = path.win32.basename(name, '.log');

  return file => path.posix.basename(file, '.log') == wanted;
}

function describeStop(outcome: StopOutcome): string {
  switch (outcome.kind) {
    case 'notRunning': {
      return `The game is not running.`;
    }
    case 'closed': {
      if (outcome.logFailure != null) {
        return (
          `The game ended in ${outcome.seconds} s, and its logs could not say how: ` +
          `${outcome.logFailure}\nIts settings may be unsaved.`
        );
      }

      return outcome.isClean
        ? `The game closed cleanly in ${outcome.seconds} s.`
        : `The game ended in ${outcome.seconds} s with no clean shutdown in its logs: it was ` +
            `still booting, it crashed, or something else ended it. Its settings may be unsaved.`;
    }
    case 'forced': {
      const forced =
        `The game did not exit in time and was forced to end after ${outcome.seconds} s: its ` +
        `shutdown may not have finished, and its settings may be unsaved.`;

      return outcome.closeFailure == null
        ? forced
        : `${forced}\nThe close request never left: ${outcome.closeFailure}\nWith the unity ` +
            `plugin installed, quit from its eval first, UnityEngine.Application.Quit(), then ` +
            `detach and stop.`;
    }
    default: {
      throw new Error(`Unhandled outcome ${JSON.stringify(outcome satisfies never)}.`);
    }
  }
}
