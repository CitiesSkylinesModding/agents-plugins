// What the server and the monitor share so an error is announced once, kept beside the evidence
// copy in the tool's own folder.

import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { isMissing } from './game-host';
import { createQueue } from './queue';

/**
 * How far each file was announced, by its path under the user data folder, in bytes.
 */
export type Offsets = Readonly<Record<string, number>>;

/**
 * What an update of one of the tool's files needs.
 */
export interface FileContext {
  readonly userDataPath: string;

  /**
   * Aborts once nobody waits for the update: a waiting one fails, a running one lets the next go.
   */
  readonly signal: AbortSignal;
}

const updates = createQueue();

// Mutable: numbers this process's writes, which may overlap.
let writes = 0;

export interface Cursor {
  /**
   * The mod last given to `restart` or `test`, whose errors a hint counts apart.
   */
  readonly mod?: string | undefined;

  /**
   * The run the offsets belong to, as the first stamp of `SceneFlow.log`.
   */
  readonly runStart?: string | undefined;

  /**
   * When a `cs2-dev` call last asked the game to end.
   */
  readonly lastStop?: string | undefined;

  /**
   * The run whose going away a hint already reported.
   */
  readonly goneRun?: string | undefined;

  /**
   * The newest crash report a hint already reported.
   */
  readonly lastReport?: string | undefined;

  readonly offsets: Offsets;
}

/**
 * The folder of the tool's own files under the game's user data.
 */
export function toolFolderPath(userDataPath: string): string {
  return path.join(userDataPath, '.cs2-dev');
}

export async function readCursor(userDataPath: string): Promise<Cursor> {
  try {
    const cursor = JSON.parse(await readFile(cursorPath(userDataPath), 'utf8')) as Cursor | null;

    return { ...cursor, offsets: cursor?.offsets ?? {} };
  } catch (error) {
    // A damaged cursor costs one repeated announcement, which beats failing every result on it.
    if (isMissing(error) || error instanceof SyntaxError) {
      return { offsets: {} };
    }

    throw error;
  }
}

/**
 * Applies `change` to the cursor as it stands now, both processes writing it.
 * Updates of one process run one at a time: parallel ones would each read before the other wrote.
 * Within a run each file keeps its larger offset,
 * so a slow scan never takes back what a faster one announced,
 * except for the files in `shrunk`, which were rewritten under their offset.
 */
export function updateCursor(
  { userDataPath, signal }: FileContext,
  change: (current: Cursor) => Cursor,
  shrunk: readonly string[] = []
): Promise<void> {
  return updates.run(signal, async () => {
    const current = await readCursor(userDataPath);
    const next = change(current);
    const isSameRun = next.runStart == current.runStart;

    await writeJson(cursorPath(userDataPath), {
      ...next,
      offsets: isSameRun ? keepLarger(current.offsets, next.offsets, shrunk) : next.offsets
    });
  });
}

/**
 * Each file's larger offset, but for the files in `shrunk`, which take `next`'s.
 */
export function keepLarger(current: Offsets, next: Offsets, shrunk: readonly string[]): Offsets {
  const larger = Object.entries(next).map(([file, offset]): [string, number] => [
    file,
    shrunk.includes(file) ? offset : Math.max(offset, current[file] ?? 0)
  ]);

  return { ...current, ...Object.fromEntries(larger) };
}

/**
 * Replaces a file of the tool's folder through a rename, so a reader never sees half of it.
 * Named per write: the monitor and each of the server's parallel calls write.
 */
export async function writeJson(filePath: string, value: unknown): Promise<void> {
  writes++;

  const temporaryPath = `${filePath}.${process.pid}.${writes}.tmp`;

  await mkdir(path.dirname(filePath), { recursive: true });
  await writeFile(temporaryPath, JSON.stringify(value, undefined, 2));
  await rename(temporaryPath, filePath);
}

function cursorPath(userDataPath: string): string {
  return path.join(toolFolderPath(userDataPath), 'cursor.json');
}
