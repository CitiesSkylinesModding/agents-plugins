// The tools: each strings the cycle's steps together and says how it went.

import { rm } from 'node:fs/promises';
import path from 'node:path';
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { isAssetGuid, readAssetGuid } from './asset-guid';
import {
  BOOT_SECONDS,
  LOAD_SECONDS,
  RESTART_CEILING_SECONDS,
  STOP_CEILING_SECONDS,
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
import { keepPreviousRun } from './evidence';
import { errorMessage } from './game-host';
import type { LogEntry } from './game-log';
import { runTool } from './tool-run';

/**
 * The guid of Archipelago, a map of the base game.
 */
const ARCHIPELAGO_GUID = 'bf8036b291428535b986c757fda3e627';

export interface RestartRequest {
  readonly save?: string | undefined;
  readonly newCity?: { readonly map?: string | undefined } | undefined;
  readonly gameOptions?: readonly string[] | undefined;
  readonly mod?: string | undefined;
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
    const { host, userDataPath, progress, signal } = guarded;

    if (request.save != null && request.newCity) {
      throw new Error(`Pass a save or a new city, not both.`);
    }

    requireWholeOptions(request.gameOptions ?? []);

    // Before the stop: a save that does not exist leaves a running game alone.
    const startGuid = await readStartGuid(userDataPath, request);

    progress(`Stopping the game`);

    const stopOutcome = await stopGame(guarded);

    progress(`Keeping the ended run's evidence`);

    await keepPreviousRun(userDataPath, signal);

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
