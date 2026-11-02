// Reads the game's two log formats: the `Logs/*.log` files, whose every entry carries a stamp, and
// `Player.log`, which has none and interleaves entries with the engine's own noise.
// Pure functions over the logs' text.

/**
 * One entry of a game log: `[2026-10-23 12:37:22,040] [INFO]  message`, the lines after it that
 * carry no stamp of their own belonging to its message.
 */
export interface LogEntry {
  readonly time: Date;
  readonly level: string;
  readonly message: string;
}

/**
 * One line of a log, its number 1-based and its offset in bytes.
 */
export interface LogLine {
  readonly text: string;
  readonly number: number;
  readonly offset: number;
}

/**
 * An entry of either format, the line it starts on, and its text whole.
 * `logger` is a tagged `Player.log` entry's, `time` a `Logs/*.log` entry's.
 */
export interface GameLogEntry {
  readonly line: number;
  readonly logger: string | undefined;
  readonly level: string;
  readonly time: Date | undefined;
  readonly text: string;
}

/**
 * Turns the lines of a log into its entries, an entry being known only once what ends it was read.
 */
export interface EntryParser {
  /**
   * Takes the next line, returning the entry it ended.
   */
  readonly push: (line: LogLine) => GameLogEntry | undefined;

  /**
   * Where the entry that nothing has ended yet starts.
   */
  readonly openOffset: () => number | undefined;

  /**
   * Ends the log, returning the entry still open if the file's end may end it.
   */
  readonly finish: () => GameLogEntry | undefined;
}

/**
 * The level of an exception Unity logged, which carries no level of its own.
 */
export const EXCEPTION_LEVEL = 'EXCEPTION';

/**
 * Lines a `Player.log` entry keeps at most, a memory bound:
 * engine noise with nothing to end an entry would join it for good.
 * A rethrow chain joins its stacks past it,
 * so an exception that long is cut, and a line past the bound still confirms it.
 */
export const PLAYER_ENTRY_LINES = 2000;

/**
 * (VOLATILE: the level names -- `Colossal.Logging.Level`.)
 */
const taggedLevels = [
  'VERBOSE',
  'TRACE',
  'DEBUG',
  'INFO',
  'WARN',
  'ERROR',
  'CRITICAL',
  'FATAL',
  'EMERGENCY'
];

/**
 * Every level a log entry may carry.
 */
export const logLevels: readonly string[] = [...taggedLevels, EXCEPTION_LEVEL];

const errorLevels: ReadonlySet<string> = new Set([
  'ERROR',
  'CRITICAL',
  'FATAL',
  'EMERGENCY',
  EXCEPTION_LEVEL
]);

/**
 * A stamped line of a `Logs/*.log` file.
 * (VOLATILE: the stamp and level format -- `Colossal.Logging.UnityLogger`.)
 */
const entryPattern =
  /^\[(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2}) (?<hours>\d{2}):(?<minutes>\d{2}):(?<seconds>\d{2}),(?<milliseconds>\d{3})\] \[(?<level>[A-Z]+)\] {1,2}(?<message>.*)$/u;

/**
 * A tagged `Player.log` line: `[HallOfFame] [ERROR] message`.
 * (VOLATILE: the tag format -- `Colossal.Logging.CustomLogHandler`.)
 */
const taggedPattern = new RegExp(
  String.raw`^\[(?<logger>[^\]]+)\] \[(?<level>${taggedLevels.join('|')})\] {1,2}(?<message>.*)$`,
  'u'
);

/**
 * The first line of an exception Unity logged,
 * which only `HANDLER_LINE` or `BURST_SENTENCE` confirms:
 * engine noise and an exception logged as text read the same,
 * and the block may start with no blank line before it.
 */
const exceptionPattern = /^[\w.+`]*Exception\b/u;

/**
 * A frame of a managed stack.
 */
const managedFramePattern = /^\s+at (?<frame>.+?)(?: \[0x[\da-f]+\])?(?: in .*)?$/u;

/**
 * A frame of the stack Unity takes itself, which is all a never-thrown exception has.
 */
const unityFramePattern = /^(?<frame>[\w.+<>`$|]+:[\w.<>`$|]+\(.*?\))(?: \(at .*\))?$/u;

/**
 * The frames Unity's stack opens on, its own logging path and the game's, the same at every site.
 * (VOLATILE: this path's namespace -- `Colossal.Logging`.)
 */
const loggingFramePattern = /^(?:UnityEngine|Colossal\.Logging)\./u;

/**
 * The line on the stack of every exception Unity logged but a Burst job's, thrown or not,
 * on a stock game and a debug-patched one, with a PDB or without,
 * and on no exception logged as text.
 * A rename silences those exceptions, and no test sees it.
 * (VOLATILE: this method's name -- `UnityEngine.DebugLogHandler`.)
 * (VOLATILE: the route an uncaught exception takes to it -- `Colossal.Logging.CustomLogHandler`.)
 */
const HANDLER_LINE = 'UnityEngine.DebugLogHandler:Internal_LogException(';

/**
 * What Unity writes under a Burst job's exception, on its second line, in place of `HANDLER_LINE`.
 */
const BURST_SENTENCE = `This Exception was thrown from a job compiled with Burst`;

/**
 * A native stack frame, which the game logs under an error on a stock build as on a patched one.
 * It says nothing a mod's author can act on.
 * The pattern leaves out a Burst library's frame with a symbol:
 * that frame is the only stack a Burst job's exception has.
 */
const nativeFramePattern = /^0x[\da-f]{16} \((?![^()]+_win_x86_64\) [^(\s])/iu;

export function isErrorLevel(level: string): boolean {
  return errorLevels.has(level);
}

/**
 * The method on top of a stack, without what varies by build: its IL offset and its path.
 * A stack Unity took itself counts from under its logging frames.
 */
export function topFrame(lines: readonly string[]): string {
  for (const line of lines) {
    const managed = managedFramePattern.exec(line)?.groups?.frame;

    if (managed) {
      return managed;
    }

    const unity = unityFramePattern.exec(line)?.groups?.frame;

    if (unity && !loggingFramePattern.test(unity)) {
      return unity;
    }
  }

  return '';
}

/**
 * Parses a game log into its entries, keeping those stamped at or after `since`.
 * The game truncates a log at its first message, so a file read before that holds the last run's.
 * The stamps are in local time, the game's under Proton too.
 */
export function parseLog(text: string, since = new Date(0)): LogEntry[] {
  // Mutable, since a line with no stamp of its own extends the last entry's message.
  const entries: Array<{ readonly time: Date; readonly level: string; message: string }> = [];

  for (const line of text.split(/\r?\n/u)) {
    const groups = entryPattern.exec(line)?.groups;
    const last = entries.at(-1);

    if (groups) {
      entries.push({
        time: readStamp(groups),
        level: groups.level ?? '',
        message: groups.message ?? ''
      });
    } else if (line && last) {
      last.message += `\n${line}`;
    }
  }

  return entries.filter(entry => entry.time >= since);
}

/**
 * Parses a `Logs/*.log` file: an entry runs to the next stamped line.
 */
export function createLogParser(): EntryParser {
  // Mutable: the entry the lines so far belong to.
  let open: OpenEntry | undefined;

  function push(line: LogLine): GameLogEntry | undefined {
    const groups = entryPattern.exec(line.text)?.groups;

    if (groups) {
      const ended = open;

      open = {
        line,
        logger: undefined,
        level: groups.level ?? '',
        time: readStamp(groups),
        lines: [groups.message ?? '']
      };

      return ended && closeEntry(ended);
    }

    if (line.text && open) {
      open.lines.push(line.text);
    }

    return undefined;
  }

  return {
    push,
    openOffset: () => open?.line.offset,
    finish: () => open && closeEntry(open)
  };
}

/**
 * Parses `Player.log`, whose lines are mostly engine noise that belongs to no entry.
 * A tagged entry runs to the next tagged line or blank line, or to `PLAYER_ENTRY_LINES` lines.
 * An exception opens on an exception-shaped line outside a tagged entry, whose stack may quote one.
 * `HANDLER_LINE` or `BURST_SENTENCE` confirms it, and the next blank line ends it.
 * Until then blank lines keep it open, as a message or a wrapper never thrown may hold some.
 * A tagged line or the next exception-shaped line lets it go.
 * Past `PLAYER_ENTRY_LINES` lines it only stops growing.
 */
export function createPlayerLogParser(): EntryParser {
  // Mutable, the three together being the parser's state.
  let open: OpenEntry | undefined;
  let exception: OpenEntry | undefined;
  let isConfirmed = false;

  function push(line: LogLine): GameLogEntry | undefined {
    const { text } = line;

    if (open) {
      if (!text) {
        return takeOpen();
      }

      const ended = open;

      if (startTagged(line)) {
        return closeEntry(ended);
      }

      if (!nativeFramePattern.test(text)) {
        open.lines.push(text);
      }

      return open.lines.length < PLAYER_ENTRY_LINES ? undefined : takeOpen();
    }

    if (exception) {
      return pushToException(exception, line);
    }

    if (text && !startTagged(line)) {
      startException(line);
    }

    return undefined;
  }

  function pushToException(block: OpenEntry, line: LogLine): GameLogEntry | undefined {
    const { text } = line;

    if (!text) {
      return isConfirmed ? takeException() : undefined;
    }

    if (startTagged(line)) {
      return takeException();
    }

    if (exceptionPattern.test(text)) {
      const ended = takeException();

      startException(line);

      return ended;
    }

    // Ahead of the bound: a line it drops confirms as well as one it keeps.
    isConfirmed ||= confirmsException(block, text);

    if (block.lines.length < PLAYER_ENTRY_LINES && !nativeFramePattern.test(text)) {
      block.lines.push(text);
    }

    return undefined;
  }

  function startTagged(line: LogLine): boolean {
    const groups = taggedPattern.exec(line.text)?.groups;

    if (!groups) {
      return false;
    }

    open = {
      line,
      logger: groups.logger,
      level: groups.level ?? '',
      time: undefined,
      lines: [groups.message ?? '']
    };

    return true;
  }

  function startException(line: LogLine): void {
    if (exceptionPattern.test(line.text)) {
      exception = {
        line,
        logger: undefined,
        level: EXCEPTION_LEVEL,
        time: undefined,
        lines: [line.text]
      };
    }
  }

  function takeOpen(): GameLogEntry | undefined {
    const ended = open;

    open = undefined;

    return ended && closeEntry(ended);
  }

  /**
   * Lets go of the open exception, returning it if confirmed.
   */
  function takeException(): GameLogEntry | undefined {
    const ended = isConfirmed ? exception : undefined;

    exception = undefined;
    isConfirmed = false;

    return ended && closeEntry(ended);
  }

  return {
    push,
    openOffset: () => (open ?? exception)?.line.offset,
    // A tagged entry may stand at the end of the file, and so may a confirmed exception,
    // which a timed capture shows Unity writing at once.
    finish: () => takeOpen() ?? takeException()
  };
}

interface OpenEntry {
  readonly line: LogLine;
  readonly logger: string | undefined;
  readonly level: string;
  readonly time: Date | undefined;

  /**
   * Grows as the lines arrive: a storm's entries are built one at a time.
   */
  readonly lines: string[];
}

/**
 * Whether the next line of an open exception confirms it.
 */
function confirmsException({ lines }: OpenEntry, text: string): boolean {
  return text.startsWith(HANDLER_LINE) || (lines.length == 1 && text.startsWith(BURST_SENTENCE));
}

function closeEntry({ line, logger, level, time, lines }: OpenEntry): GameLogEntry {
  return {
    line: line.number,
    logger,
    level,
    time,
    text: lines.join('\n')
  };
}

function readStamp(groups: Readonly<Record<string, string | undefined>>): Date {
  return new Date(
    Number(groups.year),
    Number(groups.month) - 1,
    Number(groups.day),
    Number(groups.hours),
    Number(groups.minutes),
    Number(groups.seconds),
    Number(groups.milliseconds)
  );
}
