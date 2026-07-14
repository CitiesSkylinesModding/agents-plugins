/* oxlint-disable node/no-sync -- simple polling tail script, synchronous IO is intentional. */
/* oxlint-disable eslint/no-console -- printing to stdout is this script's purpose. */

import assert from 'node:assert/strict';
import { closeSync, existsSync, openSync, readdirSync, readSync, statSync } from 'node:fs';
import path from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';
import chalk from 'chalk';

// Tails the newest Claude Code MCP log for this project and pretty-prints its .jsonl entries.
// Claude Code writes one log file per connection under
// %LocalAppData%\claude-cli-nodejs\Cache\<project-slug>\mcp-logs-<server>\<timestamp>.jsonl,
// so the tail follows the directory: when reconnecting creates a newer file, it switches to it.
// Usage: `bun scripts/tail-mcp-logs.ts [server-name]` (defaults to "gameface").

const POLL_INTERVAL_MS = 500;
const NEWLINE_BYTE = 10; // The byte value of "\n".
const SESSION_ID_WIDTH = 8;
const LABEL_WIDTH = 6;

const LEVELS = ['error', 'warn', 'info', 'debug'] as const;

type Colorizer = (text: string) => string;

const LEVEL_COLORS: Readonly<Record<string, Colorizer>> = {
  error: chalk.redBright,
  warn: chalk.yellowBright,
  stderr: chalk.yellowBright,
  info: chalk.cyanBright,
  debug: chalk.dim
};

// Claude Code logs the server's whole stderr stream at the "error" level; entries carrying this
// prefix are relabeled "stderr" so real client-side errors stand out.
const STDERR_PREFIX = 'Server stderr: ';

// Everything before the message column; continuation lines of multi-line messages align to it.
const CONTINUATION_INDENT = 'HH:MM:SS'.length + 1 + LABEL_WIDTH + 1 + SESSION_ID_WIDTH + 1;

const serverName = process.argv[2] ?? 'gameface';
const logDir = resolveLogDir(serverName);

console.log(chalk.dim(`Tailing MCP logs for "${serverName}" in ${logDir}`));

await tailForever(logDir);

async function tailForever(dir: string): Promise<never> {
  let currentFile: string | undefined;
  let offset = 0;

  // noinspection InfiniteLoopJS
  while (true) {
    const newest = findNewestLog(dir);

    if (newest != undefined && newest != currentFile) {
      currentFile = newest;
      offset = 0;

      console.log(chalk.dim(`--- ${path.basename(newest)} ---`));
    }

    if (currentFile != undefined) {
      offset = printNewEntries(currentFile, offset);
    }

    await sleep(POLL_INTERVAL_MS);
  }
}

/**
 * Prints the complete lines appended to the file since the last read.
 * Returns the new byte offset, pointing just past the last complete line consumed.
 */
function printNewEntries(file: string, offset: number): number {
  const { size } = statSync(file);

  // A shrunk file means it was truncated or replaced; start over from the beginning.
  if (size < offset) {
    return printNewEntries(file, 0);
  }

  if (size == offset) {
    return offset;
  }

  const buffer = Buffer.alloc(size - offset);
  const fd = openSync(file, 'r');

  try {
    readSync(fd, buffer, 0, buffer.length, offset);
  } finally {
    closeSync(fd);
  }

  // Only consume up to the last newline; a partial trailing line is re-read on the next poll.
  const lastNewline = buffer.lastIndexOf(NEWLINE_BYTE);
  if (lastNewline == -1) {
    return offset;
  }

  const lines = buffer.subarray(0, lastNewline).toString('utf8').split('\n');

  for (const line of lines) {
    if (line.trim() != '') {
      console.log(formatEntry(line));
    }
  }

  return offset + lastNewline + 1;
}

function formatEntry(line: string): string {
  let parsed: unknown;

  try {
    parsed = JSON.parse(line);
  } catch {
    return chalk.dim(line);
  }

  if (typeof parsed != 'object' || parsed == null) {
    return chalk.dim(line);
  }

  const entry = parsed as Record<string, unknown>;
  const level = LEVELS.find(name => typeof entry[name] == 'string');

  if (level == undefined) {
    return chalk.dim(line);
  }

  const rawMessage = entry[level];
  assert.ok(typeof rawMessage == 'string');

  let label: string = level;
  let message = rawMessage.trimEnd();

  if (message.startsWith(STDERR_PREFIX)) {
    label = 'stderr';
    message = message.slice(STDERR_PREFIX.length).trimEnd();
  }

  const time = typeof entry.timestamp == 'string' ? formatTime(entry.timestamp) : '--:--:--';

  const session =
    typeof entry.sessionId == 'string'
      ? entry.sessionId.slice(0, SESSION_ID_WIDTH)
      : ' '.repeat(SESSION_ID_WIDTH);

  const colorize = LEVEL_COLORS[label] ?? chalk.reset;
  const indented = message.replaceAll('\n', `\n${' '.repeat(CONTINUATION_INDENT)}`);

  return (
    `${chalk.dim(time)} ${colorize(label.padEnd(LABEL_WIDTH))} ` +
    `${chalk.dim(session)} ${indented}`
  );
}

function formatTime(iso: string): string {
  const date = new Date(iso);

  if (Number.isNaN(date.getTime())) {
    return '--:--:--';
  }

  // `en-GB` gives a plain 24-hour HH:MM:SS, rendered in the local timezone.
  return date.toLocaleTimeString('en-GB');
}

function findNewestLog(dir: string): string | undefined {
  let newest: string | undefined;
  let newestMtime = -1;

  for (const name of readdirSync(dir)) {
    if (!name.endsWith('.jsonl')) {
      continue;
    }

    const fullPath = path.join(dir, name);
    const mtime = statSync(fullPath).mtimeMs;

    if (mtime > newestMtime) {
      newest = fullPath;
      newestMtime = mtime;
    }
  }

  return newest;
}

function resolveLogDir(server: string): string {
  // oxlint-disable-next-line node/no-process-env -- locating the appdata root is this script's job.
  const localAppData = process.env.LOCALAPPDATA;
  assert.ok(localAppData != undefined, `LOCALAPPDATA is not set; this script targets Windows.`);

  // Claude Code derives the cache directory name by replacing every non-alphanumeric character
  // of the project path with a dash (ex. C:\Foo\bar becomes C--Foo-bar).
  const projectSlug = process.cwd().replaceAll(/[^a-zA-Z0-9]/gu, '-');

  const dir = path.join(
    localAppData,
    'claude-cli-nodejs',
    'Cache',
    projectSlug,
    `mcp-logs-${server}`
  );

  assert.ok(
    existsSync(dir),
    `No MCP log directory at ${dir}. ` +
      `Start Claude Code in this project with the "${server}" server connected first.`
  );

  return dir;
}
