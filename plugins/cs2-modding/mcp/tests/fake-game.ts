import assert from 'node:assert/strict';
import { appendFile, mkdir, mkdtemp, readFile, rm, utimes, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import type { ForceStage, GameHost, GamePids } from '../src/game-host';

const startTime = new Date('2026-10-30T12:00:00.000');

/**
 * The game host a test drives a tool with.
 * Its clock moves only when a tool sleeps, and what the game does is scripted on that clock, so a
 * ten-minute boot takes no time.
 */
export class FakeGame implements GameHost {
  public readonly closeRequests: Date[] = [];

  public readonly forces: ForceStage[] = [];

  /**
   * What `runOnce.txt` held at each launch, `deleted` for a launch without one.
   */
  public readonly launches: string[] = [];

  /**
   * Why the close request never left, undefined when it leaves.
   */
  public closeFailure: string | undefined;

  public onLaunch: (attempt: number) => void = () => {};
  public onClose: () => void = () => {};

  /**
   * What a forced end does: by default a signal ends everything at once.
   */
  public onForce: (stage: ForceStage) => void = () => {
    this.pids = { game: [], reapers: [] };
  };

  public pids: GamePids = { game: [], reapers: [] };

  /**
   * The step of the host that hangs until `releaseOldest`, as on a machine where a command hangs.
   */
  public stuckOn: 'findPids' | 'launch' | 'askToEnd' | undefined;

  /**
   * Why `findPids` fails, undefined while it answers.
   */
  public findPidsFailure: string | undefined;

  /**
   * Resolves once a tool reached the stuck step.
   */
  public readonly reachedStuck: Promise<void>;

  /**
   * The steps hanging on `stuckOn`, oldest first.
   */
  public readonly stuck: Array<() => void> = [];

  // Mutable, with `scheduled` and `pids`: the state the scripted game moves.
  private time = startTime.getTime();

  private scheduled: Array<{ readonly at: number; readonly action: () => Promise<void> | void }> =
    [];

  public readonly userDataPath: string;

  private markReachedStuck: () => void = () => {};

  public constructor(userDataPath: string) {
    this.userDataPath = userDataPath;
    this.reachedStuck = new Promise(resolve => {
      this.markReachedStuck = resolve;
    });
  }

  public readonly now = (): Date => new Date(this.time);

  public readonly findPids = async (): Promise<GamePids> => {
    if (this.stuckOn == 'findPids') {
      await this.hang();
    }

    if (this.findPidsFailure != null) {
      throw new Error(this.findPidsFailure);
    }

    return this.pids;
  };

  public readonly launch = async (): Promise<void> => {
    if (this.stuckOn == 'launch') {
      await this.hang();
    }

    this.launches.push(await readFile(this.runOncePath, 'utf8').catch(() => 'deleted'));
    this.onLaunch(this.launches.length);
  };

  public readonly askToEnd = async (): Promise<string | undefined> => {
    if (this.stuckOn == 'askToEnd') {
      await this.hang();
    }

    this.closeRequests.push(this.now());
    this.onClose();

    return this.closeFailure;
  };

  public readonly forceToEnd = (stage: ForceStage): Promise<void> => {
    this.forces.push(stage);
    this.onForce(stage);

    return Promise.resolve();
  };

  public readonly sleep = async (milliseconds: number): Promise<void> => {
    const end = this.time + milliseconds;

    for (;;) {
      const [next] = this.scheduled.filter(item => item.at <= end).toSorted((a, b) => a.at - b.at);

      if (!next) {
        break;
      }

      this.scheduled = this.scheduled.filter(item => item != next);
      this.time = next.at;

      await next.action();
    }

    this.time = end;
  };

  public readonly alarm = (seconds: number): Promise<void> =>
    new Promise(resolve => {
      this.after(seconds, resolve);
    });

  /**
   * Schedules what the game does `seconds` from now on its clock.
   */
  public after(seconds: number, action: () => Promise<void> | void): void {
    this.scheduled.push({ at: this.time + seconds * 1000, action });
  }

  public seconds(since: Date = startTime): number {
    return (this.time - since.getTime()) / 1000;
  }

  /**
   * Starts the game's process, which reads and deletes `runOnce.txt` as the game does.
   * Unity rewrites `Player.log` at launch, while `Logs/` keeps the last run's until first written.
   */
  public async start(): Promise<void> {
    this.pids = { game: [4242], reapers: [4200] };

    await rm(this.runOncePath, { force: true });
    await writeFile(this.playerLogPath, 'Mono path[0] = ...\n');
    await mkdir(path.join(this.userDataPath, 'Logs'), { recursive: true });
  }

  /**
   * The game's process goes, Steam's reaper with it unless told otherwise.
   */
  public exit(reapers: readonly number[] = []): void {
    this.pids = { game: [], reapers };
  }

  /**
   * Writes the two lines only the game's own shutdown writes.
   */
  public async writeCleanShutdown(): Promise<void> {
    await this.log('SceneFlow', 'GameManager destroyed (11722.1ms)');
    await appendFile(this.playerLogPath, 'Game terminated successfully\n');
  }

  /**
   * Appends an entry stamped with the game's clock to a log of `Logs/`.
   */
  public async log(file: string, message: string, level = 'INFO'): Promise<void> {
    await this.append(this.logPath(file), `[${stamp(this.now())}] [${level}]  ${message}\n`);
  }

  /**
   * Appends text to a file as the game does now, on its clock:
   * how old a write is decides whether an entry counts as whole.
   */
  public async append(filePath: string, text: string): Promise<void> {
    await appendFile(filePath, text);
    await utimes(filePath, this.now(), this.now());
  }

  /**
   * Lets the oldest hanging step go on, and later calls of it run free.
   */
  public releaseOldest(): void {
    this.stuckOn = undefined;
    this.stuck.shift()?.();
  }

  private hang(): Promise<void> {
    this.markReachedStuck();

    return new Promise(resolve => {
      this.stuck.push(resolve);
    });
  }

  public logPath(file: string): string {
    return path.join(this.userDataPath, 'Logs', `${file}.log`);
  }

  public get playerLogPath(): string {
    return path.join(this.userDataPath, 'Player.log');
  }

  public get runOncePath(): string {
    return path.join(this.userDataPath, 'runOnce.txt');
  }
}

/**
 * A fake game over a new temporary user data folder.
 */
export async function createFakeGame(): Promise<FakeGame> {
  const userDataPath = await mkdtemp(path.join(tmpdir(), 'cs2-dev-'));

  await mkdir(path.join(userDataPath, 'Logs'));

  return new FakeGame(userDataPath);
}

export function resultText(result: CallToolResult): string {
  const [first] = result.content;

  assert.ok(first?.type == 'text');

  return first.text;
}

function stamp(time: Date): string {
  const pad = (value: number, length = 2): string => String(value).padStart(length, '0');

  const day = `${time.getFullYear()}-${pad(time.getMonth() + 1)}-${pad(time.getDate())}`;
  const hour = `${pad(time.getHours())}:${pad(time.getMinutes())}:${pad(time.getSeconds())}`;

  return `${day} ${hour},${pad(time.getMilliseconds(), 3)}`;
}
