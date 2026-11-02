// The monitor's loop: scan what is new, ask the gate, and deliver the hint when it says so.

import { keepLarger, type Offsets } from './cursor';
import { type GameHost, MILLISECONDS_PER_SECOND } from './game-host';
import { deliverHint, readNews, settleIgnored, withinCeiling } from './hint';
import { advanceGate, type GateState, initialGate } from './notify-gate';

/**
 * Milliseconds between two scans.
 */
const SCAN_MILLISECONDS = 2000;

/**
 * Seconds a scan gets, after which it is abandoned so the next one runs.
 */
export const SCAN_CEILING_SECONDS = 30;

export interface MonitorContext {
  readonly host: GameHost;
  readonly userDataPath: string;
  readonly print: (line: string) => void;
  readonly isDone: () => boolean;
}

/**
 * What the monitor remembers: how far it read, which may pass the cursor, the gate,
 * and whether a kind awaits a line.
 */
interface WatchState {
  readonly run: string | undefined;
  readonly offsets: Offsets;
  readonly gate: GateState;

  /**
   * Whether the run logged a shown kind since the monitor's last line, which alone clears this.
   * Unless a result told the kind, a settle would stand down on it,
   * after reading everything since the cursor.
   */
  readonly hasUntoldKinds: boolean;
}

/**
 * Scans until `isDone`.
 * A scan that fails or never settles costs that scan alone.
 */
export async function watchForNews(context: MonitorContext): Promise<void> {
  const { host, isDone } = context;

  // Mutable: what the scans carry from one to the next.
  let state: WatchState = {
    run: undefined,
    offsets: {},
    gate: initialGate,
    hasUntoldKinds: false
  };

  while (!isDone()) {
    state = await scanOnce(context, state);

    await host.sleep(SCAN_MILLISECONDS);
  }
}

/**
 * One scan under its ceiling; past it the scan's result is dropped and its bytes read again.
 */
async function scanOnce(context: MonitorContext, state: WatchState): Promise<WatchState> {
  try {
    const next = await withinCeiling(context.host, SCAN_CEILING_SECONDS, signal =>
      watchStep(context, state, signal)
    );

    return next ?? state;
  } catch {
    return state;
  }
}

async function watchStep(
  { host, userDataPath, print }: MonitorContext,
  state: WatchState,
  signal: AbortSignal
): Promise<WatchState> {
  const context = { host, userDataPath, signal };

  // A result may have announced past what the monitor read.
  const news = await readNews(context, (announced, run) =>
    keepLarger(offsetsOf(state, run), announced, [])
  );

  if (!news) {
    return state;
  }

  const { run, stored, scan, isGoneUntold, newReport } = news;

  const shown = [...scan.kinds.values()].filter(kind => kind.ignoredBy == undefined);

  const { state: gate, shouldSpeak } = advanceGate(state.gate, {
    now: host.now().getTime() / MILLISECONDS_PER_SECOND,
    kinds: shown.map(kind => kind.key),
    isUrgent: isGoneUntold || newReport != undefined || stored.some(item => item.isDropped)
  });

  const wasUntold = state.run == run && state.hasUntoldKinds;
  const hasUntoldKinds = !shouldSpeak && (wasUntold || shown.length > 0);

  if (shouldSpeak) {
    const line = await deliverHint(context);

    if (line != undefined) {
      print(line);
    }
  } else if (scan.kinds.size > 0 && !hasUntoldKinds) {
    await settleIgnored(context);
  }

  return { run, offsets: { ...offsetsOf(state, run), ...scan.offsets }, gate, hasUntoldKinds };
}

/**
 * How far the monitor read the run, nothing when it last read another.
 */
function offsetsOf(state: WatchState, run: string): Offsets {
  return state.run == run ? state.offsets : {};
}
