// The patterns an agent silenced for the session, kept in a file since the monitor reads them too.

import { readFile, rm } from 'node:fs/promises';
import path from 'node:path';
import { createContext, Script } from 'node:vm';
import { type FileContext, toolFolderPath, writeJson } from './cursor';
import { isMissing } from './game-host';
import { createQueue } from './queue';

/**
 * Characters of an entry's text a pattern reads and `logs` prints.
 * What tells a kind apart comes first, and few entries are longer.
 */
export const IGNORE_INPUT_CHARACTERS = 4096;

/**
 * Milliseconds a pattern gets on one entry.
 */
const MATCH_MILLISECONDS = 50;

export interface IgnorePattern {
  readonly source: string;

  /**
   * Approximate: both the server and the monitor count, and neither locks the file.
   */
  readonly hits: number;

  /**
   * Set once the pattern took too long, until a hint said so.
   */
  readonly isDropped?: boolean | undefined;
}

const updates = createQueue();

// The pattern and the text are values in the context, so no pattern can reach the script's syntax.
const matchScript = new Script(`new RegExp(source, 'i').test(text)`);
const matchSandbox = createContext({ source: '', text: '' }) as { source: string; text: string };

export async function readIgnores(userDataPath: string): Promise<readonly IgnorePattern[]> {
  try {
    const parsed: unknown = JSON.parse(await readFile(ignoresPath(userDataPath), 'utf8'));

    return Array.isArray(parsed) ? parsed.filter(item => isIgnorePattern(item)) : [];
  } catch (error) {
    // A damaged file silences nothing, which only ever shows the agent more.
    if (isMissing(error) || error instanceof SyntaxError) {
      return [];
    }

    throw error;
  }
}

/**
 * Applies `change` to the patterns as they stand now, returning what it stored.
 * Updates run one at a time: parallel calls would each read before the other wrote, and one lose.
 */
export function updateIgnores(
  { userDataPath, signal }: FileContext,
  change: (stored: readonly IgnorePattern[]) => readonly IgnorePattern[]
): Promise<readonly IgnorePattern[]> {
  return updates.run(signal, async () => {
    const next = change(await readIgnores(userDataPath));

    await writeJson(ignoresPath(userDataPath), next);

    return next;
  });
}

/**
 * Marks the patterns that ran out of time, for the next hint to name.
 */
export async function markDropped(
  context: FileContext,
  dropped: readonly IgnorePattern[]
): Promise<void> {
  if (dropped.length == 0) {
    return;
  }

  const sources = new Set(dropped.map(({ source }) => source));

  await updateIgnores(context, stored =>
    stored.map(({ source, hits, isDropped }) => ({
      source,
      hits,
      isDropped: sources.has(source) ? true : isDropped
    }))
  );
}

/**
 * Forgets every pattern: a session is the server's life.
 */
export function clearIgnores(userDataPath: string): Promise<void> {
  return rm(ignoresPath(userDataPath), { force: true });
}

/**
 * Fails with the engine's reason on a pattern that does not compile.
 */
export function requireValidPattern(source: string): void {
  try {
    // oxlint-disable-next-line no-new, require-unicode-regexp -- it compiles as the match will.
    new RegExp(source, 'i');
  } catch (error) {
    throw new Error(`Not a valid regular expression: ${String(error)}`, { cause: error });
  }
}

/**
 * Whether a pattern matches a text, `tooSlow` when it ran out of time.
 * A synchronous regular expression blocks the event loop,
 * so nothing outside the match could stop one: only a `vm` timeout can.
 */
export function matchPattern(source: string, text: string): boolean | 'tooSlow' {
  matchSandbox.source = source;
  matchSandbox.text = text;

  try {
    return Boolean(matchScript.runInContext(matchSandbox, { timeout: MATCH_MILLISECONDS }));
  } catch (error) {
    // Under Node this comes from the context, so it is no `Error` of this one: its code tells.
    if (
      typeof error == 'object' &&
      error != null &&
      'code' in error &&
      error.code == 'ERR_SCRIPT_EXECUTION_TIMEOUT'
    ) {
      return 'tooSlow';
    }

    // A pattern is checked on the way in, so this is a file edited by hand: it matches nothing.
    return false;
  }
}

function isIgnorePattern(item: unknown): item is IgnorePattern {
  return (
    typeof item == 'object' &&
    item != null &&
    'source' in item &&
    typeof item.source == 'string' &&
    'hits' in item &&
    typeof item.hits == 'number'
  );
}

function ignoresPath(userDataPath: string): string {
  return path.join(toolFolderPath(userDataPath), 'ignores.json');
}
