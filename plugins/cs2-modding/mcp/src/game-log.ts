// Reads the game's `Logs/*.log` files, whose every entry carries a stamp.
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

const entryPattern =
  /^\[(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2}) (?<hours>\d{2}):(?<minutes>\d{2}):(?<seconds>\d{2}),(?<milliseconds>\d{3})\] \[(?<level>[A-Z]+)\] {1,2}(?<message>.*)$/u;

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
        time: new Date(
          Number(groups.year),
          Number(groups.month) - 1,
          Number(groups.day),
          Number(groups.hours),
          Number(groups.minutes),
          Number(groups.seconds),
          Number(groups.milliseconds)
        ),
        level: groups.level ?? '',
        message: groups.message ?? ''
      });
    } else if (line && last) {
      last.message += `\n${line}`;
    }
  }

  return entries.filter(entry => entry.time >= since);
}
