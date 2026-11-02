// Decides when the monitor speaks, so a burst costs one line and a storm a few. The words come from
// the hint: this only holds the timing, as a pure function over what each scan saw.

/**
 * Seconds a kind must go unseen before seeing it again is news.
 */
export const NEWS_SECONDS = 10;

/**
 * Seconds a burst gets to finish before its line goes out.
 */
export const SETTLE_SECONDS = 2;

/**
 * The shortest and longest wait between two lines.
 */
export const GAP_SECONDS = 30;
export const MAX_GAP_SECONDS = 600;

export interface GateState {
  /**
   * When each kind was last seen, in seconds.
   */
  readonly lastSeen: ReadonlyMap<string, number>;

  /**
   * When the oldest unannounced news arrived.
   */
  readonly newsSince: number | undefined;

  readonly lastLine: number | undefined;
  readonly gap: number;
}

export interface GateInput {
  readonly now: number;

  /**
   * The kinds the scan saw.
   */
  readonly kinds: readonly string[];

  /**
   * What skips the settle and the gap:
   * the game went away, a crash report appeared, or a pattern was dropped, none of which repeats.
   */
  readonly isUrgent: boolean;
}

export const initialGate: GateState = {
  lastSeen: new Map(),
  newsSince: undefined,
  lastLine: undefined,
  gap: GAP_SECONDS
};

/**
 * Takes one scan, returning the state after it and whether a line goes out now.
 */
export function advanceGate(
  state: GateState,
  { now, kinds, isUrgent }: GateInput
): { readonly state: GateState; readonly shouldSpeak: boolean } {
  const isNews = kinds.some(kind => {
    const seen = state.lastSeen.get(kind);

    return seen == undefined || now - seen >= NEWS_SECONDS;
  });

  const lastSeen = new Map([...state.lastSeen, ...kinds.map(kind => [kind, now] as const)]);
  const newsSince = state.newsSince ?? (isNews ? now : undefined);
  const { lastLine, gap } = state;

  if (isUrgent) {
    return { state: { lastSeen, newsSince: undefined, lastLine: now, gap }, shouldSpeak: true };
  }

  // A full gap with no news ends the backing off.
  const isGapIdle = lastLine != undefined && (newsSince ?? now) >= lastLine + gap;

  const currentGap = isGapIdle ? GAP_SECONDS : gap;
  const isSettled = newsSince != undefined && now - newsSince >= SETTLE_SECONDS;
  const isGapOver = lastLine == undefined || now >= lastLine + currentGap;

  if (!isSettled || !isGapOver) {
    return { state: { lastSeen, newsSince, lastLine, gap: currentGap }, shouldSpeak: false };
  }

  // News that had to wait for the gap means lines keep going out: the next gap is longer.
  const wasHeld = lastLine != undefined && !isGapIdle;

  return {
    state: {
      lastSeen,
      newsSince: undefined,
      lastLine: now,
      gap: wasHeld ? Math.min(currentGap * 2, MAX_GAP_SECONDS) : currentGap
    },
    shouldSpeak: true
  };
}
