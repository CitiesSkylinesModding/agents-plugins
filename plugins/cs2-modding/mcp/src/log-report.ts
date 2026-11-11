// What `logs` and `status` print: a run's kinds within a cap, and the game's state.

import { stat } from 'node:fs/promises';
import path from 'node:path';
import { MAIN_MENU_REACHED } from './cycle';
import type { GamePids } from './game-host';
import { createLogParser, type GameLogEntry } from './game-log';
import { IGNORE_INPUT_CHARACTERS, type IgnorePattern, readIgnores } from './ignores';
import {
  describeSource,
  type Kind,
  type LogScan,
  readEntries,
  readHead,
  sceneFlowPath
} from './scanner';

/**
 * Characters of entries `logs` returns.
 */
export const LOGS_CAP_CHARACTERS = 20_000;

/**
 * The length of `HH:MM:SS`, which opens `Date.toTimeString`.
 */
const CLOCK_CHARACTERS = 8;

/**
 * What `SceneFlow.log` shows of a run: where its last load stands, and the boot's version block.
 */
interface SceneFlowReport {
  readonly state: string;
  readonly version: string | undefined;
  readonly configuration: string | undefined;
}

/**
 * Prints a scan's kinds in order of first occurrence, cutting at the cap.
 * A kind with no stamp comes first: its time is unknown, and an exception Unity logged has none.
 */
export function describeLogs(
  scan: LogScan,
  options: {
    readonly basePath: string;
    readonly shouldShowIgnored: boolean;
    readonly isModOnly: boolean;
    readonly shouldPointBack: boolean;
  }
): string {
  const all = [...scan.kinds.values()].filter(kind => !options.isModOnly || kind.modCount > 0);
  const ignored = all.filter(kind => isHidden(kind));
  const kinds = all.filter(kind => !isHidden(kind)).toSorted(byFirstOccurrence);

  // Mutable: the blocks fill the cap one at a time.
  let room = LOGS_CAP_CHARACTERS;
  let leftOut = 0;

  const blocks: string[] = [];

  for (const kind of kinds) {
    if (room <= 0) {
      leftOut++;
    } else {
      const block = describeKind(kind, room);

      blocks.push(block);
      room -= block.length;
    }
  }

  const count = kinds.reduce((total, kind) => total + kind.count, 0);
  const ignoredCount = ignored.reduce((total, kind) => total + kind.count, 0);

  const footer = [
    `${count} entries, ${kinds.length} kinds.`,
    ...(leftOut > 0
      ? [`${leftOut} kinds left out by the cap: narrow with file, level, mod or errorsOnly.`]
      : []),
    ...(ignoredCount > 0
      ? [`${ignoredCount} ignored entries left out: ignored: true brings them back.`]
      : []),
    ...(count == 0 && ignoredCount == 0 && options.shouldPointBack
      ? [`previous: true reads the run before the last restart or test.`]
      : []),
    `Paths are relative to ${options.basePath}.`
  ].join(' ');

  return [...blocks, footer].join('\n\n');

  function isHidden(kind: Kind): boolean {
    return kind.ignoredBy != undefined && !options.shouldShowIgnored;
  }
}

/**
 * Prints the game's state, from its process and its logs.
 * A stopped game's logs are the last run's, and a running one's until it writes them.
 */
export async function describeStatus(userDataPath: string, pids: GamePids): Promise<string> {
  const { game, reapers } = pids;
  const isRunning = game.length > 0;
  const report = await readSceneFlow(userDataPath);
  const debuggerPort = await readDebuggerPort(userDataPath);
  const patterns = await readIgnores(userDataPath);

  return [
    isRunning
      ? `The game is ${report.state} (PIDs ${game.join(', ')}).`
      : `The game is stopped${reapers.length > 0 ? `, and Steam is still cleaning up` : ''}.` +
        ` The rest comes from the last run's logs.`,
    `Version: ${report.version ?? `not logged`}.`,
    `Build: ${report.configuration ?? `not logged`}.`,
    debuggerPort == undefined
      ? `Player.log shows no managed debugger.`
      : `Managed debugger on port ${debuggerPort}.`,
    ...(patterns.length > 0 ? [describePatterns(patterns)] : [])
  ].join('\n');
}

export function describePatterns(patterns: readonly IgnorePattern[]): string {
  if (patterns.length == 0) {
    return `No ignore patterns.`;
  }

  const lines = patterns.map(
    ({ source, hits, isDropped }) => `${source} (${hits} hits${isDropped ? ', dropped' : ''})`
  );

  return `Ignore patterns:\n${lines.join('\n')}`;
}

function describeKind(kind: Kind, room: number): string {
  const { file, entry, count, firstTime, lastTime } = kind;
  const place = describeSource(`${file}:${entry.line}`, entry);
  const times = [
    ...(count > 1 ? [`x${count}`] : []),
    ...(firstTime ? [describeSpan(firstTime, count > 1 ? lastTime : undefined)] : [])
  ].join(', ');

  const heading = `${place}${times ? ` ${times}` : ''}\n`;

  // One noisy entry must not cost the kinds after it their place under the cap.
  const characters = Math.min(IGNORE_INPUT_CHARACTERS, Math.max(0, room - heading.length));

  return `${heading}${cutAt(entry.text, characters)}`;
}

export function cutAt(text: string, characters: number): string {
  return text.length > characters
    ? `${text.slice(0, characters)}\n[cut: ${text.length - characters} more characters]`
    : text;
}

function describeSpan(first: Date, last: Date | undefined): string {
  return last ? `${clock(first)} to ${clock(last)}` : clock(first);
}

function clock(time: Date): string {
  return time.toTimeString().slice(0, CLOCK_CHARACTERS);
}

function byFirstOccurrence(a: Kind, b: Kind): number {
  return (a.firstTime?.getTime() ?? -Infinity) - (b.firstTime?.getTime() ?? -Infinity);
}

/**
 * Reads the run's state and version block off `SceneFlow.log`.
 * The lines it reads are printed by [symbol: Game.SceneFlow.GameManager].
 */
async function readSceneFlow(userDataPath: string): Promise<SceneFlowReport> {
  const filePath = sceneFlowPath(userDataPath);

  // Mutable: the log's lines move these along.
  let mode: string | undefined;
  let isComplete = false;
  let isAtMenu = false;
  let version: string | undefined;
  let configuration: string | undefined;

  function read({ text }: GameLogEntry): void {
    const loading = /^Loading mode (?<mode>\w+)/u.exec(text)?.groups?.mode;

    if (loading != undefined) {
      mode = loading;
      isComplete = false;
      isAtMenu = false;
    } else if (text == 'Loading completed') {
      isComplete = true;
    } else if (text == MAIN_MENU_REACHED) {
      isAtMenu = true;
    }

    version ??= /^Game version: (?<value>.+)$/mu.exec(text)?.groups?.value;
    configuration ??= /^Game configuration: (?<value>.+)$/mu.exec(text)?.groups?.value;
  }

  const size = await stat(filePath).then(
    file => file.size,
    () => 0
  );

  await readEntries(
    { filePath, start: 0, end: size, parser: createLogParser(), isSettled: true },
    entry => {
      read(entry);

      return Promise.resolve();
    }
  );

  return { state: describeState(mode, isComplete, isAtMenu), version, configuration };
}

function describeState(mode: string | undefined, isComplete: boolean, isAtMenu: boolean): string {
  if (mode == undefined) {
    return `booting`;
  }

  if (isAtMenu) {
    return `at the main menu`;
  }

  if (!isComplete) {
    return `loading`;
  }

  return mode == 'Editor' ? `in the editor` : mode == 'Game' ? `in a city` : `loading`;
}

async function readDebuggerPort(userDataPath: string): Promise<string | undefined> {
  const head = await readHead(path.join(userDataPath, 'Player.log'));

  return /^Starting managed debugger on port (?<port>\d+)$/mu.exec(head)?.groups?.port;
}
