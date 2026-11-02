// One step at a time within a process, for the hints and for each file's read-change-write.

export interface Queue {
  /**
   * Runs `step` once those before it let go, failing instead if `signal` aborted by then.
   * A step that never settles lets go when its signal aborts: nothing else can stop it.
   */
  readonly run: <T>(signal: AbortSignal, step: () => Promise<T>) => Promise<T>;
}

export function createQueue(): Queue {
  // Mutable: what the next step waits for.
  let last: Promise<unknown> = Promise.resolve();

  function enqueue<T>(signal: AbortSignal, step: () => Promise<T>): Promise<T> {
    const previous = last;
    const result = runAfter(previous, signal, step);

    last = letGo(previous, result, signal);

    return result;
  }

  return { run: enqueue };
}

async function runAfter<T>(
  previous: Promise<unknown>,
  signal: AbortSignal,
  step: () => Promise<T>
): Promise<T> {
  await previous;

  signal.throwIfAborted();

  return step();
}

/**
 * Settles once a step let go, never before those ahead of it:
 * a waiter's abort must not start the next step beside one still running.
 */
async function letGo(
  previous: Promise<unknown>,
  result: Promise<unknown>,
  signal: AbortSignal
): Promise<void> {
  await previous;
  await Promise.race([whenSettled(result), whenAborted(signal)]);
}

async function whenSettled(step: Promise<unknown>): Promise<void> {
  try {
    await step;
  } catch {
    // Its caller hears of it: the next one only waits for its turn.
  }
}

function whenAborted(signal: AbortSignal): Promise<void> {
  return new Promise(resolve => {
    if (signal.aborted) {
      resolve();

      return;
    }

    signal.addEventListener(
      'abort',
      () => {
        resolve();
      },
      { once: true }
    );
  });
}
