// Reads a run's logs from byte offsets and sorts their entries into kinds, so a storm costs only
// its new bytes and one line.

import { createReadStream } from 'node:fs';
import { readdir, stat } from 'node:fs/promises';
import path from 'node:path';
import type { Offsets } from './cursor';
import { errorMessage, type GameHost, isMissing, MILLISECONDS_PER_SECOND } from './game-host';
import {
  createLogParser,
  createPlayerLogParser,
  type EntryParser,
  type GameLogEntry,
  type LogLine,
  parseLog,
  topFrame
} from './game-log';
import { IGNORE_INPUT_CHARACTERS } from './ignores';

/**
 * Seconds a file must go unwritten before its last entry counts as whole.
 * The game writes an entry in one piece, so this only has to outlast a write.
 */
export const QUIET_SECONDS = 2;

/**
 * Seconds a `Player.log` entry waits for its `Logs/` twin, which lands within milliseconds of it.
 */
const TWIN_HOLD_SECONDS = QUIET_SECONDS;

/**
 * Bytes read off the head of a log, for what the game writes once at boot.
 */
const HEAD_BYTES = 65_536;

/**
 * Bytes read off a log for its first stamp, which opens its first line.
 */
const FIRST_STAMP_BYTES = 1024;

/**
 * Characters of a kind's first line that tell it apart.
 */
const KIND_FIRST_LINE_CHARACTERS = 200;

/**
 * Bytes of a file one hint or monitor scan reads, so a backlog takes several rather than none.
 * A storm writes far slower than the scans catch up.
 */
export const SCAN_WINDOW_BYTES = 67_108_864;

const PLAYER_LOG = 'Player.log';

/**
 * A number in a message, in decimal or in hexadecimal,
 * which varies between two occurrences of an error without making it another one.
 */
const numberPattern = /0x[\da-f]+|\b[\da-f]{8,}\b|\d+/giu;

/**
 * Every occurrence of one error.
 */
export interface Kind {
  readonly key: string;

  /**
   * The first occurrence, as the file it was counted in, under the scan's base, and its entry.
   */
  readonly file: string;

  readonly entry: GameLogEntry;

  /**
   * The pattern silencing the kind, undefined with none.
   */
  readonly ignoredBy: string | undefined;

  readonly firstTime: Date | undefined;

  // Mutable, as a storm's occurrences are tallied one at a time.
  count: number;
  modCount: number;
  lastTime: Date | undefined;
}

type ModCheck = (logger: string, text: string) => boolean;

export interface LogScan {
  readonly kinds: ReadonlyMap<string, Kind>;

  /**
   * How far the scan got in each file, past whole entries alone.
   */
  readonly offsets: Offsets;

  /**
   * The files rewritten under the offset the scan was given.
   */
  readonly shrunk: readonly string[];
}

export interface ScanRequest {
  readonly host: Pick<GameHost, 'now' | 'sleep'>;

  /**
   * The folder holding `Logs/` and `Player.log`: the user data, or the evidence copy.
   */
  readonly basePath: string;

  readonly runStart: Date;
  readonly isRunning: boolean;
  readonly offsets: Offsets;
  readonly mod: string | undefined;

  /**
   * Stops the reads, once nobody will read what they find.
   */
  readonly signal: AbortSignal;

  /**
   * Bytes read of each file, its whole rest without it.
   */
  readonly windowBytes?: number | undefined;

  /**
   * Keeps a file out of the scan.
   */
  readonly hasFile?: ((file: string) => boolean) | undefined;

  readonly accepts: (entry: GameLogEntry) => boolean;

  /**
   * The pattern silencing a text, undefined with none.
   */
  readonly findIgnore: (text: string) => string | undefined;

  /**
   * Counts `Player.log` entries that have a `Logs/` twin too, for a read of that file alone.
   */
  readonly shouldKeepTwins?: boolean | undefined;
}

/**
 * Sorts what the logs hold since `offsets` into kinds.
 * An offset only ever passes a whole entry: one that nothing has ended yet stays for the next scan,
 * until the file went quiet or the game stopped.
 */
export async function scanLogs(request: ScanRequest): Promise<LogScan> {
  const { basePath, runStart, offsets, hasFile, signal, windowBytes } = request;
  const kinds = new Map<string, Kind>();
  const reached: Record<string, number> = {};
  const shrunk: string[] = [];
  const twins = createTwinCheck(request);
  const namesMod = createModCheck(request.mod);
  const files = await listLogFiles(basePath);

  for (const file of files.filter(name => hasFile?.(name) ?? true)) {
    const filePath = path.join(basePath, file);
    const fileStat = await stat(filePath).catch(ignoreMissing);

    if (!fileStat) {
      continue;
    }

    const isPlayerLog = file == PLAYER_LOG;

    // The game truncates a log at its first message, so an older file is the last run's.
    if (!isPlayerLog && fileStat.mtime < runStart) {
      continue;
    }

    const given = offsets[file] ?? 0;
    const start = given > fileStat.size ? 0 : given;

    if (start != given) {
      shrunk.push(file);
    }

    // Mutable, the visit below counting.
    const seen = { ofRun: 0, older: 0 };

    const end = Math.min(fileStat.size, start + (windowBytes ?? Infinity));

    const reachedEnd = await readEntries(
      {
        filePath,
        start,
        end,
        parser: isPlayerLog ? createPlayerLogParser() : createLogParser(),
        // A window's end cuts a file anywhere, so only the file's own end may end an entry.
        isSettled:
          end == fileStat.size && (!request.isRunning || isQuiet(request.host, fileStat.mtime)),
        signal
      },
      async entry => {
        // Local stamps run back at the end of daylight saving, so one alone rules out no file.
        if (!isPlayerLog && entry.time && entry.time < runStart) {
          seen.older++;

          return;
        }

        seen.ofRun++;

        if (request.accepts(entry) && !(isPlayerLog && (await twins.isTwin(entry)))) {
          tally(kinds, request, { entry, file, namesMod });
        }
      }
    );

    // A file of older entries alone waits for the game's truncation: an offset could outrun it.
    if (seen.ofRun > 0 || seen.older == 0) {
      reached[file] = reachedEnd;
    }
  }

  return { kinds, offsets: reached, shrunk };
}

/**
 * Names where an entry comes from, as `logs` heads it: `place` is its file, with a line or not.
 * A `Logs/` entry's logger is its file's stem, so only a tagged `Player.log` entry shows one.
 */
export function describeSource(place: string, { logger, level }: GameLogEntry): string {
  return `${place}${logger == undefined ? '' : ` [${logger}]`} [${level}]`;
}

/**
 * The first stamp of a run's `SceneFlow.log`, which starts the run, undefined with no such log.
 */
export function readRunStart(basePath: string): Promise<Date | undefined> {
  return readFirstStamp(sceneFlowPath(basePath));
}

export function sceneFlowPath(basePath: string): string {
  return path.join(basePath, 'Logs', 'SceneFlow.log');
}

export async function listLogFiles(basePath: string): Promise<readonly string[]> {
  const names = await readdir(path.join(basePath, 'Logs')).catch((error: unknown): string[] => {
    if (isMissing(error)) {
      return [];
    }

    throw error;
  });

  return [...names.filter(name => name.endsWith('.log')).map(name => `Logs/${name}`), PLAYER_LOG];
}

/**
 * The head of a log, for what the game writes once at boot.
 */
export async function readHead(logPath: string, bytes = HEAD_BYTES): Promise<string> {
  const lines: string[] = [];

  try {
    for await (const line of readLines({ filePath: logPath, start: 0, end: bytes })) {
      lines.push(line.text);
    }
  } catch (error) {
    if (!isMissing(error)) {
      throw new Error(`Cannot read ${logPath}: ${errorMessage(error)}`, { cause: error });
    }
  }

  return lines.join('\n');
}

/**
 * Runs `visit` over a file's whole entries from a byte offset, returning how far they reach.
 */
export async function readEntries(
  source: {
    readonly filePath: string;
    readonly start: number;
    readonly end: number;
    readonly parser: EntryParser;
    readonly isSettled: boolean;
    readonly signal?: AbortSignal | undefined;
  },
  visit: (entry: GameLogEntry) => Promise<void>
): Promise<number> {
  const { filePath, start, end, parser, isSettled, signal } = source;

  // Mutable: the last offset past which no entry is left unfinished.
  let reached = start;

  const lines = readLines({ filePath, start, end, shouldIncludePartial: isSettled, signal });

  for await (const line of lines) {
    const ended = parser.push(line);

    if (ended) {
      await visit(ended);
    }

    reached = parser.openOffset() ?? line.offset + line.bytes;
  }

  if (isSettled) {
    const last = parser.finish();

    if (last) {
      await visit(last);
    }

    return end;
  }

  return reached;
}

/**
 * The lines of a file between two byte offsets.
 * A line with no newline yet is only given when `shouldIncludePartial`: the game may be writing it.
 *
 * @yields Each line, with how many bytes it takes in the file.
 */
async function* readLines({
  filePath,
  start,
  end,
  shouldIncludePartial = true,
  signal
}: {
  readonly filePath: string;
  readonly start: number;
  readonly end: number;
  readonly shouldIncludePartial?: boolean;
  readonly signal?: AbortSignal | undefined;
}): AsyncGenerator<LogLine & { readonly bytes: number }> {
  if (end <= start) {
    return;
  }

  // Mutable: the bytes of the line being read, a storm's file arriving in chunks.
  let pending: Buffer = Buffer.alloc(0);
  let offset = start;
  let number = 1;

  // `end` is inclusive in a stream, and bounds the read: what lands after it is the next scan's.
  const stream = createReadStream(filePath, { start, end: end - 1, signal });

  for await (const chunk of stream) {
    pending = Buffer.concat([pending, chunk as Buffer]);

    for (;;) {
      const newline = pending.indexOf('\n');

      if (newline == -1) {
        break;
      }

      const bytes = newline + 1;
      const text = pending.toString('utf8', 0, newline).replace(/\r$/u, '');

      yield { text, number, offset, bytes };

      pending = pending.subarray(bytes);
      offset += bytes;
      number++;
    }
  }

  if (pending.length > 0 && shouldIncludePartial) {
    yield {
      text: pending.toString('utf8').replace(/\r$/u, ''),
      number,
      offset,
      bytes: pending.length
    };
  }
}

/**
 * What the `Logs/` side of the duplicate rule asks:
 * `Player.log` repeats every logger's `Warn` and above,
 * so a tagged entry whose logger wrote its own file this run is that file's to count.
 * With no such file yet, the entry may simply have landed first, so it waits once for the twin.
 */
function createTwinCheck(request: ScanRequest): {
  readonly isTwin: (entry: GameLogEntry) => Promise<boolean>;
} {
  const { basePath, runStart, host, isRunning, shouldKeepTwins } = request;
  const known = new Map<string, boolean>();

  // Mutable: one wait covers every entry of a scan, a storm of them included.
  let hasWaited = !isRunning;

  return { isTwin };

  async function isTwin({ logger }: GameLogEntry): Promise<boolean> {
    if (logger == undefined || shouldKeepTwins) {
      return false;
    }

    const answer = known.get(logger) ?? (await hasOwnFile(logger));

    if (answer || hasWaited) {
      known.set(logger, answer);

      return answer;
    }

    hasWaited = true;

    await host.sleep(TWIN_HOLD_SECONDS * MILLISECONDS_PER_SECOND);

    const settled = await hasOwnFile(logger);

    known.set(logger, settled);

    return settled;
  }

  async function hasOwnFile(logger: string): Promise<boolean> {
    // A logger is text off a log line: it may name no file, or one outside `Logs/`.
    if (/[/\\]/u.test(logger)) {
      return false;
    }

    const logPath = path.join(basePath, 'Logs', `${logger}.log`);
    const time = await readFirstStamp(logPath).catch((): undefined => undefined);

    return time != undefined && time >= runStart;
  }
}

function tally(
  kinds: Map<string, Kind>,
  request: ScanRequest,
  {
    entry,
    file,
    namesMod
  }: {
    readonly entry: GameLogEntry;
    readonly file: string;
    readonly namesMod: ModCheck;
  }
): void {
  const logger = entry.logger ?? path.basename(file, '.log');
  const key = kindKey(logger, entry);
  const known = kinds.get(key);
  const isModNamed = namesMod(logger, entry.text);

  if (known) {
    known.count++;
    known.modCount += isModNamed ? 1 : 0;
    known.lastTime = entry.time ?? known.lastTime;

    return;
  }

  kinds.set(key, {
    key,
    file,
    entry,
    // Once per kind, so a storm costs one match.
    ignoredBy: request.findIgnore(ignoreText(file, entry)),
    count: 1,
    modCount: isModNamed ? 1 : 0,
    firstTime: entry.time,
    lastTime: entry.time
  });
}

/**
 * What a pattern reads of an entry: what `logs` prints less the line number,
 * so a pattern never depends on where an entry landed.
 */
function ignoreText(file: string, entry: GameLogEntry): string {
  return `${describeSource(file, entry)}\n${entry.text.slice(0, IGNORE_INPUT_CHARACTERS)}`;
}

/**
 * What makes two entries one error, less their numbers:
 * the logger, the level, the first line, and the top frame,
 * as unrelated null references share a first line.
 * Masks before cutting, as a number's width would otherwise move the cut.
 */
function kindKey(logger: string, { level, text }: GameLogEntry): string {
  const [first = '', ...rest] = text.split('\n');
  const line = maskNumbers(first).slice(0, KIND_FIRST_LINE_CHARACTERS);

  return [maskNumbers(logger), level, line, maskNumbers(topFrame(rest))].join('\n');
}

function maskNumbers(text: string): string {
  return text.replace(numberPattern, '#');
}

/**
 * Whether an entry names the mod: by its logger, which the toolchain's template names `<mod>.Mod`,
 * the assembly name, a stack frame, or its UI module's path.
 * Built once per scan, as a storm asks for every occurrence.
 */
function createModCheck(mod: string | undefined): ModCheck {
  if (mod == undefined) {
    return () => false;
  }

  const name = mod.replaceAll(/[$()*+.?[\\\]^{|}]/gu, String.raw`\$&`);
  const pattern = new RegExp(String.raw`(?:^|[\s(/])${name}[.,]`, 'mu');

  return (logger, text) => logger == mod || logger.startsWith(`${mod}.`) || pattern.test(text);
}

async function readFirstStamp(logPath: string): Promise<Date | undefined> {
  return parseLog(await readHead(logPath, FIRST_STAMP_BYTES))[0]?.time;
}

function isQuiet(host: Pick<GameHost, 'now'>, modified: Date): boolean {
  return host.now().getTime() - modified.getTime() >= QUIET_SECONDS * MILLISECONDS_PER_SECOND;
}

function ignoreMissing(error: unknown): undefined {
  if (!isMissing(error)) {
    throw error;
  }

  return undefined;
}
