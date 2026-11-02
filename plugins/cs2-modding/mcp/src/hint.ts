// The one line a result or a notification carries: what happened since the last one.

import { readdir, stat } from 'node:fs/promises';
import path from 'node:path';
import { type Cursor, type FileContext, type Offsets, readCursor, updateCursor } from './cursor';
import type { GameHost } from './game-host';
import { isErrorLevel } from './game-log';
import {
  type IgnorePattern,
  markDropped,
  matchPattern,
  readIgnores,
  updateIgnores
} from './ignores';
import { createQueue } from './queue';
import { type LogScan, readRunStart, SCAN_WINDOW_BYTES, scanLogs } from './scanner';

/**
 * What a hint needs: past the `signal`'s abort nobody will read it, so it announces nothing.
 */
export interface HintContext extends FileContext {
  readonly host: GameHost;
}

const hints = createQueue();

/**
 * What one scan of the logs found, with what it read to scan them.
 */
export interface LogNews {
  readonly run: string;
  readonly cursor: Cursor;
  readonly stored: readonly IgnorePattern[];

  /**
   * The patterns that ran out of time during this scan.
   */
  readonly newlyDropped: readonly IgnorePattern[];

  readonly scan: LogScan;

  /**
   * Whether the run's going away is still to be told.
   */
  readonly isGoneUntold: boolean;

  /**
   * The newest crash report still to be told of.
   */
  readonly newReport: Date | undefined;
}

/**
 * The folder Crashpad writes its reports to, under the user data.
 */
export function crashReportsPath(userDataPath: string): string {
  return path.join(userDataPath, '.cache', 'backtrace', 'crashpad', 'reports');
}

/**
 * Runs a step under a ceiling, returning undefined past it, when its signal aborts.
 * A signal is all that can stop a step: one that never settles simply hangs on, unread.
 */
export function withinCeiling<T>(
  host: Pick<GameHost, 'alarm'>,
  seconds: number,
  step: (signal: AbortSignal) => Promise<T>
): Promise<T | undefined> {
  const abandoned = new AbortController();

  async function ceiling(): Promise<undefined> {
    await host.alarm(seconds);

    abandoned.abort();

    return undefined;
  }

  return Promise.race([step(abandoned.signal), ceiling()]);
}

/**
 * Scans from the cursor and returns the line to deliver, undefined with no news.
 * Hints run one at a time: parallel results would each read the cursor before the other moves it,
 * and announce the same errors.
 */
export function deliverHint(context: HintContext): Promise<string | undefined> {
  return hints.run(context.signal, () => announceNews(context, false));
}

/**
 * Moves the cursor past ignored entries and counts their hits, delivering no line.
 * Stands down on anything a line would tell, which is the gate's to time,
 * writing only the patterns its scan dropped.
 */
export async function settleIgnored(context: HintContext): Promise<void> {
  await hints.run(context.signal, () => announceNews(context, true));
}

/**
 * Moves the cursor past the errors a result listed itself and counts their hits,
 * leaving the rest of what a line tells to the next hint.
 * Takes the listing's own scan, so that nothing newer goes past unlisted.
 * That scan covers the run since the launch, so hits a monitor settled during it count twice.
 */
export async function moveCursorPastListing(
  context: HintContext,
  scan: LogScan,
  newlyDropped: readonly IgnorePattern[]
): Promise<void> {
  await hints.run(context.signal, async () => {
    const runStart = await readRunStart(context.userDataPath);

    if (!runStart) {
      return;
    }

    await markDropped(context, newlyDropped);
    await settleIgnores(context, scan, []);

    await updateCursor(
      context,
      current => ({ ...current, runStart: runStart.toISOString(), offsets: scan.offsets }),
      scan.shrunk
    );
  });
}

/**
 * Scans the current run's errors from the offsets `chooseOffsets` picks given what was announced,
 * undefined before a run logged.
 * Marks the patterns that ran out of time, which a scan telling no line would otherwise lose.
 */
export async function readNews(
  context: HintContext,
  chooseOffsets: (announced: Offsets, run: string) => Offsets
): Promise<LogNews | undefined> {
  const { host, userDataPath, signal } = context;
  const runStart = await readRunStart(userDataPath);

  if (!runStart) {
    return undefined;
  }

  const run = runStart.toISOString();
  const cursor = await readCursor(userDataPath);
  const { game } = await host.findPids();
  const isRunning = game.length > 0;
  const stored = await readIgnores(userDataPath);
  const matcher = createIgnoreMatcher(stored);

  const scan = await scanLogs({
    host,
    basePath: userDataPath,
    runStart,
    isRunning,
    offsets: chooseOffsets(cursor.runStart == run ? cursor.offsets : {}, run),
    mod: cursor.mod,
    signal,
    windowBytes: SCAN_WINDOW_BYTES,
    accepts: ({ level }) => isErrorLevel(level),
    findIgnore: matcher.findIgnore
  });

  await markDropped(context, matcher.newlyDropped);

  return {
    run,
    cursor,
    stored,
    newlyDropped: matcher.newlyDropped,
    scan,
    isGoneUntold: !isRunning && isGoneUnannounced(cursor, run, runStart),
    newReport: await readNewReport(userDataPath, runStart, cursor)
  };
}

export interface IgnoreMatcher {
  /**
   * The pattern silencing a text, undefined with none.
   */
  readonly findIgnore: (text: string) => string | undefined;

  /**
   * The patterns that ran out of time during this matcher's life.
   */
  readonly newlyDropped: readonly IgnorePattern[];
}

/**
 * Tests a text against each live pattern; one that runs out of time matches nothing from then on.
 */
export function createIgnoreMatcher(patterns: readonly IgnorePattern[]): IgnoreMatcher {
  const newlyDropped: IgnorePattern[] = [];
  const live = new Set(patterns.filter(({ isDropped }) => !isDropped).map(({ source }) => source));

  function findIgnore(text: string): string | undefined {
    for (const source of live) {
      const matched = matchPattern(source, text);

      if (matched == 'tooSlow') {
        live.delete(source);

        const pattern = patterns.find(item => item.source == source);

        newlyDropped.push({ source, hits: pattern?.hits ?? 0, isDropped: true });
      } else if (matched) {
        return source;
      }
    }

    return undefined;
  }

  return { newlyDropped, findIgnore };
}

/**
 * Whether the run's going away is still to be told: no `cs2-dev` stop since the run started.
 */
function isGoneUnannounced(cursor: Cursor, run: string, runStart: Date): boolean {
  const lastStop = cursor.lastStop == undefined ? undefined : new Date(cursor.lastStop);

  return cursor.goneRun != run && (lastStop == undefined || lastStop < runStart);
}

/**
 * Moves the cursor past what the line covers, and past ignored entries that need no line.
 * With `shouldHoldLine`, news a line would tell stays ahead of the cursor, unannounced.
 */
async function announceNews(
  context: HintContext,
  shouldHoldLine: boolean
): Promise<string | undefined> {
  const news = await readNews(context, announced => announced);

  if (!news) {
    return undefined;
  }

  const { run, cursor, stored, newlyDropped, scan, isGoneUntold, newReport } = news;
  const dropped = [...stored, ...newlyDropped].filter(({ isDropped }) => isDropped);

  const line = [
    ...(isGoneUntold ? [`The game went away with no cs2-dev stop.`] : []),
    ...(newReport ? [`A new crash report appeared, which may be a hang snapshot.`] : []),
    ...describeErrors(scan, cursor.mod),
    ...dropped.map(({ source }) => `Dropped ignore pattern ${source}: it took too long.`)
  ].join(' ');

  if (shouldHoldLine && line) {
    return undefined;
  }

  // First: past the cursor, a failure here would cost the line for good.
  await settleIgnores(context, scan, dropped);

  await updateCursor(
    context,
    current => ({
      ...current,
      runStart: run,
      ...(isGoneUntold ? { goneRun: run } : {}),
      ...(newReport ? { lastReport: newReport.toISOString() } : {}),
      offsets: scan.offsets
    }),
    scan.shrunk
  );

  return line || undefined;
}

/**
 * The line's error part, nothing when all of it was ignored.
 */
function describeErrors(scan: LogScan, mod: string | undefined): readonly string[] {
  const kinds = [...scan.kinds.values()];
  const shown = kinds.filter(kind => kind.ignoredBy == undefined);
  const count = sum(shown, kind => kind.count);

  if (count == 0) {
    return [];
  }

  const ignored = sum(kinds, kind => (kind.ignoredBy == undefined ? 0 : kind.count));
  const naming = mod == undefined ? '' : `, ${sum(shown, kind => kind.modCount)} naming ${mod}`;
  const suffix = ignored > 0 ? ` (${ignored} ignored)` : '';

  const kindCount = `${shown.length} kind${shown.length == 1 ? '' : 's'}`;

  return [`Errors: ${count} new, ${kindCount}${naming}${suffix}. See logs.`];
}

/**
 * Adds the scan's hits and retires the dropped patterns the hint named.
 * Works on the file as it stands: the agent may have changed it while the scan ran.
 */
async function settleIgnores(
  context: HintContext,
  scan: LogScan,
  named: readonly IgnorePattern[]
): Promise<void> {
  const added = new Map<string, number>();

  for (const { ignoredBy, count } of scan.kinds.values()) {
    if (ignoredBy != undefined) {
      added.set(ignoredBy, (added.get(ignoredBy) ?? 0) + count);
    }
  }

  const retired = new Set(named.map(({ source }) => source));

  if (added.size == 0 && retired.size == 0) {
    return;
  }

  await updateIgnores(context, stored =>
    stored
      .filter(({ source }) => !retired.has(source))
      .map(({ source, hits, isDropped }) => ({
        source,
        hits: hits + (added.get(source) ?? 0),
        isDropped
      }))
  );
}

/**
 * The newest crash report this run wrote that no hint told of yet.
 * Crashpad also writes one for a hang, a slow boot included, so it proves no death.
 */
async function readNewReport(
  userDataPath: string,
  runStart: Date,
  { lastReport }: Cursor
): Promise<Date | undefined> {
  const folder = crashReportsPath(userDataPath);
  const names = await readdir(folder).catch((): string[] => []);
  const after = Math.max(runStart.getTime(), lastReport ? Date.parse(lastReport) : 0);

  const times = await Promise.all(
    names.map(name =>
      stat(path.join(folder, name)).then(
        ({ mtime }) => mtime.getTime(),
        // Crashpad prunes its reports on its own.
        () => 0
      )
    )
  );

  const newest = Math.max(0, ...times);

  return newest > after ? new Date(newest) : undefined;
}

export function sum<T>(items: readonly T[], value: (item: T) => number): number {
  return items.reduce((total, item) => total + value(item), 0);
}
