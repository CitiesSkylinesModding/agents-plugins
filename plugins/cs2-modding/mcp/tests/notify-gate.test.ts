import { describe, expect, test } from 'bun:test';
import {
  advanceGate,
  GAP_SECONDS,
  type GateState,
  initialGate,
  MAX_GAP_SECONDS,
  NEWS_SECONDS,
  SETTLE_SECONDS
} from '../src/notify-gate';

/**
 * A timeline of scans, one per step: each entry says the second and the kinds seen.
 * Returns the seconds at which a line went out.
 */
function linesOf(scans: ReadonlyArray<readonly [number, ...string[]]>): number[] {
  // Mutable: the gate's state moves along the timeline.
  let state: GateState = initialGate;

  const lines: number[] = [];

  for (const [now, ...kinds] of scans) {
    const { state: after, shouldSpeak } = advanceGate(state, { now, kinds, isUrgent: false });

    state = after;

    if (shouldSpeak) {
      lines.push(now);
    }
  }

  return lines;
}

/**
 * A scan every 2 s from `from` to `to`, seeing the kinds.
 */
function scansOf(from: number, to: number, ...kinds: string[]): Array<[number, ...string[]]> {
  const scans: Array<[number, ...string[]]> = [];

  for (let now = from; now <= to; now += 2) {
    scans.push([now, ...kinds]);
  }

  return scans;
}

describe('advanceGate', () => {
  test(`gives a burst one line, once it settled`, () => {
    expect(linesOf([[0, 'a'], [1, 'a', 'b'], [SETTLE_SECONDS, 'a'], [3], [4]])).toEqual([
      SETTLE_SECONDS
    ]);
  });

  test(`gives a storm of one kind one line, however long it runs`, () => {
    expect(linesOf(scansOf(0, 3600, 'a'))).toEqual([2]);
  });

  test(`holds a second kind back to the end of the gap`, () => {
    expect(linesOf([...scansOf(0, 8, 'a'), ...scansOf(10, 60, 'a', 'b')])).toEqual([
      2,
      2 + GAP_SECONDS
    ]);
  });

  test(`takes a kind seen again after a quiet spell as news`, () => {
    const repeat = 2 + GAP_SECONDS + NEWS_SECONDS;

    expect(linesOf([...scansOf(0, 2, 'a'), ...scansOf(repeat, repeat + 2, 'a')])).toEqual([
      2,
      repeat + 2
    ]);
  });

  test(`takes no repeat within the news window as news`, () => {
    expect(linesOf([[0, 'a'], [2], [NEWS_SECONDS - 1, 'a'], [40]])).toEqual([2]);
  });

  test(`doubles the gap while lines keep going out, up to its ceiling`, () => {
    const scans: Array<[number, ...string[]]> = [];

    // A new kind every 2 s, so every gap ends with news waiting.
    for (let now = 0; now < 7200; now += 2) {
      scans.push([now, `kind ${now}`]);
    }

    const lines = linesOf(scans);
    const gaps = lines.slice(1).map((line, index) => line - (lines[index] ?? 0));

    expect(gaps.slice(0, 4)).toEqual([
      GAP_SECONDS,
      GAP_SECONDS * 2,
      GAP_SECONDS * 4,
      GAP_SECONDS * 8
    ]);

    expect(Math.max(...gaps)).toBe(MAX_GAP_SECONDS);
  });

  test(`goes back to the shortest gap once a full gap passes with no news`, () => {
    // Two lines double the gap to 60 s; a kind arriving past that gap goes out after its settle,
    // and the next waits the shortest gap again.
    const lines = linesOf([
      ...scansOf(0, 2, 'a'),
      ...scansOf(10, 32, 'b'),
      ...scansOf(100, 102, 'c'),
      ...scansOf(110, 140, 'd')
    ]);

    expect(lines).toEqual([2, 32, 102, 102 + GAP_SECONDS]);
  });

  test(`lets the game going away skip the settle and the gap`, () => {
    const first = advanceGate(initialGate, { now: 0, kinds: ['a'], isUrgent: false });
    const second = advanceGate(first.state, { now: 2, kinds: [], isUrgent: false });
    const gone = advanceGate(second.state, { now: 3, kinds: [], isUrgent: true });

    expect(second.shouldSpeak).toBe(true);
    expect(gone.shouldSpeak).toBe(true);
  });
});
