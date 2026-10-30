// The process side of the tools: one port, with a Linux and a Windows implementation.
// Tools take the port, so a test drives them with a fake game.

import { spawn } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { setTimeout as sleep } from 'node:timers/promises';
import { STEAM_APP_ID, userManagerEnvironment, wineEnvironment } from './config';

const STEAM_URL = `steam://rungameid/${STEAM_APP_ID}`;

const GAME_PROCESS = 'Cities2.exe';

/**
 * Milliseconds a command gets: each answers at once on a healthy machine, `wine taskkill` included.
 */
const COMMAND_TIMEOUT_MILLISECONDS = 30_000;

/**
 * Milliseconds a process's output gets to arrive once it exited.
 */
const DRAIN_MILLISECONDS = 200;

const MILLISECONDS_PER_SECOND = 1000;

/**
 * The exit codes of a `pgrep` that answered, 1 saying it matched nothing.
 */
const pgrepExitCodes = [0, 1] as const;

export interface GamePids {
  /**
   * The PIDs of `Cities2.exe`, the only process that says the game itself runs.
   */
  readonly game: readonly number[];

  /**
   * The PIDs of Steam's reaper for the game, always empty on Windows.
   */
  readonly reapers: readonly number[];
}

/**
 * How a game that does not end is ended: `terminate` a process may still answer, `kill` it cannot.
 */
export type ForceStage = 'terminate' | 'kill';

export interface GameHost {
  readonly findPids: () => Promise<GamePids>;

  /**
   * Starts a launch through Steam, returning once Steam took the request.
   */
  readonly launch: () => Promise<void>;

  /**
   * Asks the running game to close, as its window's close button does.
   * Returns why the request never left, undefined when it left, which proves nothing about the
   * game: a stuck game takes it just as well.
   */
  readonly askToEnd: () => Promise<string | undefined>;

  readonly forceToEnd: (stage: ForceStage) => Promise<void>;
  readonly now: () => Date;
  readonly sleep: (milliseconds: number) => Promise<void>;

  /**
   * Resolves `seconds` from now, whatever the call is doing meanwhile.
   */
  readonly alarm: (seconds: number) => Promise<void>;
}

export function createGameHost(userDataPath: string): GameHost {
  return process.platform == 'win32' ? windowsGameHost() : linuxGameHost(userDataPath);
}

/**
 * Whether a file operation failed on a file that is not there.
 */
export function isMissing(error: unknown): boolean {
  return error instanceof Error && 'code' in error && error.code == 'ENOENT';
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/**
 * The PIDs in the output of `pgrep -x`.
 */
export function parsePgrep(output: string): number[] {
  return output
    .split('\n')
    .map(Number)
    .filter(pid => isProcessId(pid));
}

/**
 * The PIDs of Steam's reapers for the game in the output of `pgrep -ax reaper`.
 */
export function parseReapers(output: string): number[] {
  return [...output.matchAll(new RegExp(`^(\\d+) .*\\bAppId=${STEAM_APP_ID}\\b`, 'gmu'))]
    .map(([, pid]) => Number(pid))
    .filter(pid => isProcessId(pid));
}

/**
 * The PIDs in the CSV of `tasklist /FO CSV /NH`.
 */
export function parseTasklist(csv: string): number[] {
  return [...csv.matchAll(/^"Cities2\.exe","(?<pid>\d+)"/gimu)]
    .map(match => Number(match.groups?.pid))
    .filter(pid => isProcessId(pid));
}

/**
 * Whether a parsed value can be signalled: 0 and below address process groups, this one's included.
 */
function isProcessId(pid: number): boolean {
  return Number.isSafeInteger(pid) && pid > 0;
}

const clock = {
  now: () => new Date(),
  sleep: (milliseconds: number) => sleep(milliseconds),
  alarm: (seconds: number) => sleep(seconds * MILLISECONDS_PER_SECOND, undefined, { ref: false })
} satisfies Pick<GameHost, 'now' | 'sleep' | 'alarm'>;

function linuxGameHost(userDataPath: string): GameHost {
  return {
    ...clock,
    findPids: findLinuxPids,
    async launch(): Promise<void> {
      // Through the user's service manager and never from this process: a Steam started here
      // inherits the agent's environment, tokens included, and the game's crash dumps upload it to
      // Colossal Order's Backtrace.
      await runChecked('systemd-run', ['--user', '--collect', '--quiet', 'steam', STEAM_URL], {
        env: userManagerEnvironment()
      });
    },
    askToEnd: () => askProtonToClose(userDataPath),
    async forceToEnd(stage): Promise<void> {
      const { game, reapers } = await findLinuxPids();

      for (const pid of [...game, ...reapers]) {
        try {
          process.kill(pid, stage == 'terminate' ? 'SIGTERM' : 'SIGKILL');
        } catch {
          // The process ended since it was listed.
        }
      }
    }
  };
}

async function findLinuxPids(): Promise<GamePids> {
  // By exact process name: a pattern over command lines also matches any shell whose command line
  // holds it, the agent's own tool call included.
  const game = await runChecked('pgrep', ['-x', GAME_PROCESS], { exitCodes: pgrepExitCodes });
  const reapers = await runChecked('pgrep', ['-ax', 'reaper'], { exitCodes: pgrepExitCodes });

  return { game: parsePgrep(game), reapers: parseReapers(reapers) };
}

/**
 * Sends the close through Proton's own `wine taskkill`.
 * It reaches the game's wineserver from outside the Steam Linux Runtime container, and needs no X
 * client, window id, or display.
 * The prefix is the only setting it needs.
 * A wrong one builds a new prefix and says little, so this reads the path from the prefix's own
 * `config_info`, whose second line is Proton's fonts folder.
 */
async function askProtonToClose(userDataPath: string): Promise<string | undefined> {
  const prefixMark = `${path.sep}pfx${path.sep}`;
  // The last one: the folders above a Proton prefix are the user's to name, `pfx` included.
  const prefixAt = userDataPath.lastIndexOf(prefixMark);

  if (prefixAt == -1) {
    return `the user data folder "${userDataPath}" is not in a Proton prefix.`;
  }

  const compatDataPath = userDataPath.slice(0, prefixAt);
  const configInfoPath = path.join(compatDataPath, 'config_info');

  let configInfo = '';

  try {
    configInfo = await readFile(configInfoPath, 'utf8');
  } catch (error) {
    // A missing file stays empty text, which the check below reports as no install.
    if (!isMissing(error)) {
      return `${configInfoPath} could not be read: ${errorMessage(error)}`;
    }
  }

  const fontsSuffix = `/share/fonts/`;
  const fontsPath = configInfo.split('\n')[1] ?? '';

  if (!fontsPath.endsWith(fontsSuffix)) {
    return `no Proton install named in ${configInfoPath}.`;
  }

  const wine = path.join(fontsPath.slice(0, -fontsSuffix.length), 'bin', 'wine');

  try {
    const { output, errorOutput } = await run(wine, ['taskkill', '/IM', GAME_PROCESS], {
      env: wineEnvironment(path.join(compatDataPath, 'pfx'))
    });

    if (output.includes('Close message sent')) {
      return undefined;
    }

    // Wine says why it did not start on stderr, last: a healthy run writes noise there too.
    const answer = output.trim() || errorOutput.trim().split('\n').at(-1);

    return `wine taskkill answered "${answer}".`;
  } catch (error) {
    return `${wine} did not run: ${errorMessage(error)}`;
  }
}

function windowsGameHost(): GameHost {
  return {
    ...clock,
    async findPids(): Promise<GamePids> {
      const filter = `IMAGENAME eq ${GAME_PROCESS}`;
      const output = await runChecked('tasklist', ['/FI', filter, '/FO', 'CSV', '/NH']);

      return { game: parseTasklist(output), reapers: [] };
    },
    async launch(): Promise<void> {
      await runChecked('cmd', ['/c', 'start', STEAM_URL]);
    },
    async askToEnd(): Promise<string | undefined> {
      try {
        // Without /F, taskkill asks the window to close, which the game takes as a quit.
        const { code, output } = await run('taskkill', ['/IM', GAME_PROCESS]);

        return code == 0 ? undefined : `taskkill answered "${output.trim()}".`;
      } catch (error) {
        return `taskkill did not run: ${errorMessage(error)}`;
      }
    },
    async forceToEnd(): Promise<void> {
      await run('taskkill', ['/F', '/IM', GAME_PROCESS]);
    }
  };
}

/**
 * Runs a command that must succeed, returning its standard output.
 * Rejects on an exit code that is none of the `exitCodes`, a signal's included:
 * a command that failed must not read as an empty answer.
 * Its error carries what the command said on its error stream.
 */
async function runChecked(
  file: string,
  args: readonly string[],
  options: { readonly exitCodes?: readonly number[]; readonly env?: NodeJS.ProcessEnv } = {}
): Promise<string> {
  const { output, errorOutput, code } = await run(file, args, options);

  if (code == null || !(options.exitCodes ?? [0]).includes(code)) {
    const reason = errorOutput.trim();

    throw new Error(
      `${file} ${args.join(' ')} exited with ${code ?? 'a signal'}${reason ? `: ${reason}` : '.'}`
    );
  }

  return output;
}

/**
 * Runs a command and returns its two output streams and exit code.
 * Rejects on a command that cannot start or does not answer in time, never reading as an empty
 * answer: a `pgrep` that says nothing means a stopped game.
 * The time limit does not wait for the exit, since a process in uninterruptible sleep survives even
 * SIGKILL.
 */
export function run(
  file: string,
  args: readonly string[],
  options: { readonly env?: NodeJS.ProcessEnv; readonly timeoutMilliseconds?: number } = {}
): Promise<{
  readonly output: string;
  readonly errorOutput: string;
  readonly code: number | null;
}> {
  const timeoutMilliseconds = options.timeoutMilliseconds ?? COMMAND_TIMEOUT_MILLISECONDS;

  return new Promise((resolve, reject) => {
    const child = spawn(file, args, {
      stdio: ['ignore', 'pipe', 'pipe'],
      ...(options.env ? { env: options.env } : {})
    });

    const chunks: string[] = [];
    const errorChunks: string[] = [];

    const limit = setTimeout(() => {
      child.kill('SIGKILL');
      closePipes();
      // A child surviving its kill must not keep the server alive past its client.
      child.unref();
      reject(new Error(`${file} did not answer in ${timeoutMilliseconds} ms.`));
    }, timeoutMilliseconds);

    function closePipes(): void {
      child.stdout.destroy();
      child.stderr.destroy();
    }

    function finish(code: number | null): void {
      clearTimeout(limit);
      closePipes();
      resolve({ output: chunks.join(''), errorOutput: errorChunks.join(''), code });
    }

    child.once('error', error => {
      clearTimeout(limit);
      reject(error);
    });

    // A process that never started has nothing more to say, nor pipes on EMFILE.
    if (child.pid == null) {
      return;
    }

    child.stdout.setEncoding('utf8');
    child.stdout.on('data', (chunk: string) => {
      chunks.push(chunk);
    });

    child.stderr.setEncoding('utf8');
    child.stderr.on('data', (chunk: string) => {
      errorChunks.push(chunk);
    });

    child.once('close', finish);

    // A wineserver that wine starts inherits the pipe, and a pipe still held never closes.
    child.once('exit', code => {
      setTimeout(finish, DRAIN_MILLISECONDS, code);
    });
  });
}
