// The steps a tool strings together: stopping the game, launching it, and waiting on its logs.

import { createReadStream } from 'node:fs';
import { access, readFile, rm, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { text } from 'node:stream/consumers';
import { updateCursor } from './cursor';
import { errorMessage, type GameHost, isMissing } from './game-host';
import { type LogEntry, parseLog } from './game-log';
import { sceneFlowPath } from './scanner';

/**
 * Seconds a game asked to close gets before it is forced: above the slowest close of a loaded city
 * measured mid-autosave, and above what the game's own shutdown allows itself.
 */
export const STOP_BOUND_SECONDS = 60;

/**
 * Seconds a process gets to answer a request to end before it is killed: a healthy game goes at
 * once, and a frozen one holds the request pending forever.
 */
const TERMINATE_SECONDS = 5;

/**
 * Seconds a killed game and Steam's reaper for it take to go.
 */
const KILL_SECONDS = 30;

/**
 * Launches tried, and seconds each waits for the game's process.
 */
const LAUNCH_ATTEMPTS = 3;
const LAUNCH_SECONDS = 60;

/**
 * Milliseconds between two checks of the game or of its logs.
 */
const POLL_MILLISECONDS = 1000;

/**
 * Seconds a boot may take, above the slowest seen: the prefabs' load varies by the day.
 */
export const BOOT_SECONDS = 600;

/**
 * Seconds a city or a map may take to load.
 */
export const LOAD_SECONDS = 300;

/**
 * Seconds the loading screen stays up after the game logged the end of a load.
 */
export const LOADING_SCREEN_SECONDS = 20;

/**
 * Seconds a tool's ceiling keeps above the sum of its waits, for the commands and the copies
 * between them: only what the waits did not foresee reaches a ceiling.
 */
const CEILING_MARGIN_SECONDS = 120;

const FORCE_SECONDS = TERMINATE_SECONDS + KILL_SECONDS;

export const STOP_CEILING_SECONDS = STOP_BOUND_SECONDS + FORCE_SECONDS + CEILING_MARGIN_SECONDS;

export const WAIT_LOAD_CEILING_SECONDS =
  LOAD_SECONDS + LOADING_SCREEN_SECONDS + CEILING_MARGIN_SECONDS;

export const RESTART_CEILING_SECONDS =
  STOP_BOUND_SECONDS +
  FORCE_SECONDS +
  LAUNCH_ATTEMPTS * (FORCE_SECONDS + LAUNCH_SECONDS) +
  FORCE_SECONDS +
  BOOT_SECONDS +
  LOAD_SECONDS +
  LOADING_SCREEN_SECONDS +
  CEILING_MARGIN_SECONDS;

/**
 * Seconds a read-only tool may take: reading a log, however large, is its only wait.
 */
export const READ_CEILING_SECONDS = 120;

/**
 * (VOLATILE: this line -- `Game.SceneFlow.GameManager`.)
 */
export const MAIN_MENU_REACHED = 'MainMenu reached';

/**
 * Bytes read off the end of `Player.log` for its shutdown line, past which only the engine's own
 * last output follows.
 */
const LOG_END_BYTES = 1_048_576;

/**
 * The call holding the game, one at a time: two calls would stop and launch it over each other.
 * Mutable, since it outlives a call and is what calls share.
 */
export interface CallSlot {
  running: string | undefined;
}

/**
 * What a tool works with.
 */
export interface ToolContext {
  readonly host: GameHost;
  readonly userDataPath: string;

  /**
   * Tells the caller where a long call is at.
   */
  readonly progress: (message: string) => void;

  readonly slot: CallSlot;

  /**
   * Aborts when the caller cancels the call or goes away.
   * A tool's steps get one that aborts once the call ended, however it ended, to check before what
   * the host does not guard.
   */
  readonly signal: AbortSignal;
}

/**
 * How a `stopGame` went.
 * `closed` is a game that ended within the bound.
 * `isClean` says whether its logs show its own shutdown, and `logFailure` why they could not say.
 * `forced` carries why the close request never left, when it did not.
 */
export type StopOutcome =
  | { readonly kind: 'notRunning' }
  | {
      readonly kind: 'closed';
      readonly seconds: number;
      readonly isClean: boolean;
      readonly logFailure?: string;
    }
  | { readonly kind: 'forced'; readonly seconds: number; readonly closeFailure?: string };

/**
 * Fails with what to set when the user data folder is not there.
 */
export async function requireUserData(userDataPath: string): Promise<void> {
  try {
    await access(userDataPath);
  } catch (error) {
    throw new Error(
      `No game user data folder at "${userDataPath}": run the game once, or set ` +
        `CSII_USERDATAPATH to its user data folder.`,
      { cause: error }
    );
  }
}

/**
 * Stops the game: asks it once to close, waits for its exit, and past the bound forces it.
 */
export async function stopGame(context: ToolContext): Promise<StopOutcome> {
  const { host, userDataPath } = context;
  const start = host.now();
  const { game, reapers } = await host.findPids();

  if (game.length == 0 && reapers.length == 0) {
    return { kind: 'notRunning' };
  }

  await recordStop(context);

  // Only the reaper left: the game is already on its way out.
  const closeFailure = game.length > 0 ? await host.askToEnd() : undefined;

  // The whole bound whatever the request did: the agent may have quit the game from the eval, and
  // forcing it sooner would kill a shutdown in progress.
  const remaining = STOP_BOUND_SECONDS - secondsSince(host, start);

  if (await waitUntil(host, () => isStopped(host), remaining)) {
    return {
      kind: 'closed',
      seconds: secondsSince(host, start),
      ...(await readShutdown(userDataPath))
    };
  }

  await forceGameEnd(host);

  return {
    kind: 'forced',
    seconds: secondsSince(host, start),
    ...(closeFailure == null ? {} : { closeFailure })
  };
}

/**
 * Tells the hint this end is the tool's own, before the game goes.
 * A stop must never fail on it: at worst a hint reports the game going away.
 */
async function recordStop(context: ToolContext): Promise<void> {
  try {
    await updateCursor(context, cursor => ({
      ...cursor,
      lastStop: context.host.now().toISOString()
    }));
  } catch {
    // The stop is what the caller asked for, and a cancelled one asked nothing of the game.
  }
}

/**
 * Refuses a game option holding whitespace, before the stop that `launchGame` follows.
 * `GameManager` splits the line it reads from `runOnce.txt` on spaces alone, with no quoting:
 * any other whitespace stays in a token, which it mostly drops with no word.
 */
export function requireWholeOptions(gameOptions: readonly string[]): void {
  const spaced = gameOptions.find(option => /\s/u.test(option));

  if (spaced != null) {
    throw new Error(
      `${JSON.stringify(spaced)} cannot reach the game: it splits its options on spaces with no ` +
        `quoting and keeps any other whitespace inside the option, so a --profile= path must ` +
        `hold none.`
    );
  }
}

/**
 * Launches the stopped game through Steam, with game options written to `runOnce.txt`.
 * Returns what it wrote once the game's process runs.
 */
export async function launchGame(
  context: ToolContext,
  gameOptions: readonly string[]
): Promise<string> {
  const { host, userDataPath, signal } = context;
  const runOncePath = path.join(userDataPath, 'runOnce.txt');
  const options = ['--noSplash', ...gameOptions].join(' ');

  let isLaunched = false;

  try {
    for (let attempt = 1; attempt <= LAUNCH_ATTEMPTS && !isLaunched; attempt++) {
      // What a failed launch left, a reaper for one, makes Steam ignore the next.
      await forceGameEnd(host);
      signal.throwIfAborted();
      await writeFile(runOncePath, options);
      await host.launch();

      // The game's process itself: a launch starts and ends a first reaper before the game's.
      isLaunched = await waitUntil(host, () => isGameRunning(host), LAUNCH_SECONDS);
    }
  } finally {
    // A call that ended leaves the file to its give-up: a newer call may own it by now.
    if (!isLaunched && !signal.aborted) {
      // The options are for this launch only: the next one, the user's own maybe, must not read
      // them, even when the game will not die.
      await rm(runOncePath, { force: true });
      await forceGameEnd(host);
    }
  }

  if (!isLaunched) {
    throw new Error(
      `The game did not start after ${LAUNCH_ATTEMPTS} launches through Steam: Steam may be ` +
        `closed, signed out, updating the game, or showing a dialog. Check Steam, then retry.`
    );
  }

  return options;
}

/**
 * Waits for the main menu of the game launched at `launchTime`.
 */
export async function waitForMainMenu(context: ToolContext, launchTime: Date): Promise<void> {
  const isReached = await waitForSceneFlow(context, {
    since: launchTime,
    seconds: BOOT_SECONDS,
    isDone: entries => entries.some(entry => entry.message == MAIN_MENU_REACHED)
  });

  if (!isReached) {
    throw new Error(
      `No main menu after ${BOOT_SECONDS} s, see ${sceneFlowPath(context.userDataPath)}.`
    );
  }
}

/**
 * Waits for the last logged city or map load to complete and its loading screen to go.
 * Reads the entries stamped at or after `since`.
 * Fails on a game that reached the main menu after `fallbackSince` in place of the load, which is
 * where a failed load ends.
 * It watches a loading screen like the load: the game may yet leave the city or die during it.
 */
export async function waitForLoad(
  context: ToolContext,
  {
    since,
    fallbackSince,
    seconds
  }: { readonly since: Date; readonly fallbackSince: Date; readonly seconds: number }
): Promise<void> {
  const { host, userDataPath } = context;
  const allowedSeconds = seconds + LOADING_SCREEN_SECONDS;

  const isLoaded = await waitForSceneFlow(context, {
    since,
    seconds: allowedSeconds,
    isDone: entries => {
      const load = readLoad(entries, fallbackSince);

      if (load == 'fellBack') {
        throw new Error(
          `The game did not load it and went to the main menu, see ` +
            `${sceneFlowPath(userDataPath)}.`
        );
      }

      return (
        load instanceof Date &&
        host.now().getTime() >= load.getTime() + LOADING_SCREEN_SECONDS * 1000
      );
    }
  });

  if (!isLoaded) {
    throw new Error(
      `Nothing loaded within ${allowedSeconds} s, see ${sceneFlowPath(userDataPath)}.`
    );
  }
}

/**
 * Runs a wait on a game, adding to its error what to do about the game it leaves:
 * a hung game stays up, for a look with the `unity` eval before it is stopped.
 */
export async function withGameLeftRunning(
  host: GameHost,
  wait: () => Promise<void>
): Promise<void> {
  try {
    await wait();
  } catch (error) {
    const message = errorMessage(error);
    const processes = await describeProcesses(host);

    throw new Error([message, ...processes].join('\n'), { cause: error });
  }
}

export function isGameRunning(host: GameHost): Promise<boolean> {
  return host.findPids().then(({ game }) => game.length > 0);
}

export function secondsSince(host: GameHost, time: Date): number {
  return Math.round((host.now().getTime() - time.getTime()) / 1000);
}

/**
 * The entries of a game log stamped at or after `since`, none when the game has not written it.
 */
export async function readLog(logPath: string, since: Date): Promise<LogEntry[]> {
  const log = await readOrEmpty(logPath, readFile(logPath, 'utf8'));

  return parseLog(log, since);
}

/**
 * Where the last logged load stands: when it completed, `fellBack` when it ended at the main menu
 * after `fallbackSince`, or `pending`.
 * The main menu takes a load of its own, which is what a failed load falls back through.
 * (VOLATILE: the `Loading` lines -- `Game.SceneFlow.GameManager`.)
 */
function readLoad(
  entries: readonly LogEntry[],
  fallbackSince: Date
): Date | 'fellBack' | 'pending' {
  const start = entries.findLastIndex(entry => entry.message.startsWith('Loading mode '));
  const load = entries[start];

  if (!load) {
    return 'pending';
  }

  const after = entries.slice(start);

  if (/^Loading mode (?:Game|Editor) /u.test(load.message)) {
    return after.find(entry => entry.message == 'Loading completed')?.time ?? 'pending';
  }

  const hasFallenBack = after.some(
    entry => entry.message == MAIN_MENU_REACHED && entry.time > fallbackSince
  );

  return hasFallenBack ? 'fellBack' : 'pending';
}

/**
 * Waits for the entries of `SceneFlow.log` stamped at or after `since` to satisfy `isDone`, for at
 * most `seconds`, failing at once when the game is gone.
 * Reads `Cities2.exe` alone: Steam's reaper outlives a crash by many seconds.
 */
function waitForSceneFlow(
  context: ToolContext,
  wait: {
    readonly since: Date;
    readonly seconds: number;
    readonly isDone: (entries: readonly LogEntry[]) => boolean;
  }
): Promise<boolean> {
  const { host, userDataPath } = context;
  const logPath = sceneFlowPath(userDataPath);

  return waitUntil(
    host,
    async () => {
      const isDone = wait.isDone(await readLog(logPath, wait.since));

      if (!isDone && !(await isGameRunning(host))) {
        throw new Error(`The game stopped, see ${logPath}.`);
      }

      return isDone;
    },
    wait.seconds
  );
}

/**
 * Whether the game and, on Linux, Steam's reaper for it are gone.
 * Steam ignores a launch while its reaper for the app lives, which outlives `Cities2.exe`: only
 * the reaper's exit means the game is stopped.
 */
async function isStopped(host: GameHost): Promise<boolean> {
  const { game, reapers } = await host.findPids();

  return game.length == 0 && reapers.length == 0;
}

/**
 * Ends what is left of the game by force, if anything is.
 */
async function forceGameEnd(host: GameHost): Promise<void> {
  if (await isStopped(host)) {
    return;
  }

  await host.forceToEnd('terminate');

  if (await waitUntil(host, () => isStopped(host), TERMINATE_SECONDS)) {
    return;
  }

  await host.forceToEnd('kill');

  if (await waitUntil(host, () => isStopped(host), KILL_SECONDS)) {
    return;
  }

  const { game, reapers } = await host.findPids();

  throw new Error(
    `The game is still running after it was killed (PIDs ${[...game, ...reapers].join(', ')}): ` +
      `kill them by hand, or restart the machine if they will not die.`
  );
}

/**
 * What the ended run's logs say of its shutdown.
 * A failed read is no reason to hide that the game closed.
 */
async function readShutdown(
  userDataPath: string
): Promise<{ readonly isClean: boolean; readonly logFailure?: string }> {
  try {
    return { isClean: await hasCleanShutdown(userDataPath) };
  } catch (error) {
    return { isClean: false, logFailure: errorMessage(error) };
  }
}

/**
 * Whether the ended run wrote the two lines only the game's own shutdown writes; a signal writes
 * neither.
 * Unity rewrites `Player.log` at launch, while `SceneFlow.log` may still be the last run's, so the
 * first keeps a stale line of the second from passing for this run's.
 * (VOLATILE: the two lines -- `Game.SceneFlow.GameManager`.)
 */
async function hasCleanShutdown(userDataPath: string): Promise<boolean> {
  const playerLogPath = path.join(userDataPath, 'Player.log');
  const player = await readOrEmpty(playerLogPath, readEnd(playerLogPath));

  // `successfully` or `due to timeout`: `GameManager` saves the settings before it picks the line.
  if (!/^Game terminated /mu.test(player)) {
    return false;
  }

  const sceneFlow = await readLog(sceneFlowPath(userDataPath), new Date(0));

  return sceneFlow.some(entry => entry.message.startsWith('GameManager destroyed'));
}

/**
 * A log's text, empty only when it is not there.
 * Any other failure names the log: read as empty, it would wait out a whole boot on a healthy game,
 * and a read past a string's size says nothing of which file it was.
 */
async function readOrEmpty(logPath: string, read: Promise<string>): Promise<string> {
  try {
    return await read;
  } catch (error) {
    if (isMissing(error)) {
      return '';
    }

    const reason = errorMessage(error);

    throw new Error(`Cannot read ${logPath}: ${reason}`, { cause: error });
  }
}

/**
 * The end of a log, which an error storm can grow past what a string holds.
 */
async function readEnd(logPath: string): Promise<string> {
  const { size } = await stat(logPath);

  return text(createReadStream(logPath, { start: Math.max(0, size - LOG_END_BYTES) }));
}

/**
 * The lines a failed wait's error gets about the game it leaves.
 * A failed look at the processes must not hide why the wait failed.
 */
async function describeProcesses(host: GameHost): Promise<readonly string[]> {
  try {
    const { game } = await host.findPids();

    if (game.length == 0) {
      return [];
    }

    return [
      `The game is left running (PIDs ${game.join(', ')}): with the unity plugin, its eval can ` +
        `look at it, and \`stop\` stops it.`
    ];
  } catch (error) {
    const reason = errorMessage(error);

    return [`Whether the game still runs could not be checked: ${reason}`];
  }
}

async function waitUntil(
  host: GameHost,
  condition: () => Promise<boolean>,
  seconds: number
): Promise<boolean> {
  const deadline = host.now().getTime() + seconds * 1000;

  while (host.now().getTime() < deadline) {
    if (await condition()) {
      return true;
    }

    await host.sleep(POLL_MILLISECONDS);
  }

  return condition();
}
