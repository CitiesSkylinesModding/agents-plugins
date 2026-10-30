// Keeps what a launch would overwrite: a restart costs the agent no evidence of the run it ended.

import { access, copyFile, cp, mkdir, mkdtemp, readdir, rename, rm, stat } from 'node:fs/promises';
import path from 'node:path';
import { readLog, sceneFlowPath } from './cycle';
import { isMissing } from './game-host';

/**
 * Copies the ended run's `Logs/`, `Player.log`, and crash reports into the tool's own folder,
 * replacing the last copy.
 * Run after the stop, since a dying game still writes, and before the launch that overwrites them.
 * Builds the copy aside: a copy that fails, or whose call ended, leaves the last one whole.
 * A swap that fails may lose it, but the call then never launches:
 * the ended run's own files stay for a retry.
 */
export async function keepPreviousRun(userDataPath: string, signal: AbortSignal): Promise<void> {
  const folderPath = path.join(userDataPath, '.cs2-dev');
  const copyPath = path.join(folderPath, 'previous-run');
  const nextPrefix = 'previous-run.next-';

  await mkdir(folderPath, { recursive: true });

  const leftovers = await readdir(folderPath);

  // Past this, a listed folder may be a newer call's.
  signal.throwIfAborted();

  // What a killed server left. Best effort: a call given up on may still be writing into its own.
  await Promise.allSettled(
    leftovers
      .filter(name => name.startsWith(nextPrefix))
      .map(name => rm(path.join(folderPath, name), { recursive: true, force: true }))
  );

  // A folder per call: one given up on may still be copying when the next starts.
  const nextPath = await mkdtemp(path.join(folderPath, nextPrefix));

  try {
    await copyIfPresent(path.join(userDataPath, 'Logs'), path.join(nextPath, 'Logs'));
    await copyIfPresent(path.join(userDataPath, 'Player.log'), path.join(nextPath, 'Player.log'));
    await copyCrashReports(userDataPath, nextPath);

    signal.throwIfAborted();

    await rm(copyPath, { recursive: true, force: true });
    await rename(nextPath, copyPath);
  } finally {
    await rm(nextPath, { recursive: true, force: true });
  }
}

async function copyIfPresent(source: string, destination: string): Promise<void> {
  try {
    await access(source);
  } catch (error) {
    if (isMissing(error)) {
      return;
    }

    throw error;
  }

  // Past the check: a file gone during the copy fails it, rather than publishing a partial one.
  // `dereference` follows links, so a redirected log keeps its bytes.
  await cp(source, destination, { recursive: true, dereference: true });
}

/**
 * Copies the crash reports the ended run wrote.
 * Its logs tell the run's start, since the tool may not have launched it.
 */
async function copyCrashReports(userDataPath: string, nextPath: string): Promise<void> {
  const runStart = await readRunStart(userDataPath);

  if (!runStart) {
    return;
  }

  const reportsPath = path.join(userDataPath, '.cache', 'backtrace', 'crashpad', 'reports');
  const reports = await readdir(reportsPath).catch((): string[] => []);
  const copiedPath = path.join(nextPath, 'crashpad', 'reports');

  await mkdir(copiedPath, { recursive: true });

  for (const report of reports) {
    const source = path.join(reportsPath, report);

    try {
      const { mtime } = await stat(source);

      if (mtime >= runStart) {
        await copyFile(source, path.join(copiedPath, report));
      }
    } catch (error) {
      // Crashpad prunes its reports on its own, so one listed a moment ago may be gone.
      if (!isMissing(error)) {
        throw error;
      }
    }
  }
}

async function readRunStart(userDataPath: string): Promise<Date | undefined> {
  const entries = await readLog(sceneFlowPath(userDataPath), new Date(0));

  return entries[0]?.time;
}
