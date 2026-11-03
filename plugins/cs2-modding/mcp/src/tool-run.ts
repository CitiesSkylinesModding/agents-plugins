// What keeps a call from wedging: whatever happens inside a tool, the call ends, and what it
// abandoned stops touching the game.

import assert from 'node:assert/strict';
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { requireUserData, type ToolContext } from './cycle';
import { errorMessage, type GameHost } from './game-host';
import { deliverHint, type HintContext, withinCeiling } from './hint';

/**
 * Seconds a hint gets: it must never cost a call its result.
 */
export const HINT_CEILING_SECONDS = 10;

/**
 * The hint due now, undefined if it fails, never settles, or the caller cancels first.
 */
export async function readHint(context: HintContext): Promise<string | undefined> {
  try {
    return await withinCeiling(context.host, HINT_CEILING_SECONDS, ceiling =>
      deliverHint({ ...context, signal: AbortSignal.any([ceiling, context.signal]) })
    );
  } catch {
    return undefined;
  }
}

/**
 * What a tool runs on.
 */
export interface GuardedContext extends ToolContext {
  /**
   * Keeps a line for the head of the result, however the call ends.
   * For what a later failure must not cost the caller, as nothing else can tell it again.
   */
  readonly carry: (line: string) => void;
}

export interface ToolRun {
  readonly name: string;

  /**
   * Seconds the call may take, whatever is inside it.
   */
  readonly ceilingSeconds: number;

  /**
   * Whether the call holds the game, refusing to run beside another that does.
   */
  readonly isExclusive: boolean;

  /**
   * Cleans up after a call given up on, on the best effort: this too may never settle.
   */
  readonly onGiveUp?: () => Promise<void>;
}

/**
 * Runs a tool, its error the result the caller reads.
 * A call ends at its ceiling or when the caller cancels it,
 * even with a step inside it that never settles.
 * The step left behind fails at its next touch of the game rather than acting on it later, unseen.
 * A call that ended on its own carries the hint, failed or not.
 */
export async function runTool(
  context: ToolContext,
  run: ToolRun,
  tool: (guarded: GuardedContext) => Promise<string>
): Promise<CallToolResult> {
  const { host, signal, slot } = context;

  if (signal.aborted) {
    return errorResult(`Cancelled before it started: nothing was done to the game.`);
  }

  if (run.isExclusive) {
    if (slot.running != null) {
      return errorResult(
        `Another call holds the game: ${slot.running}. Wait for it or cancel it, then call again.`
      );
    }

    slot.running = run.name;
  }

  const ended = new AbortController();

  // Mutable: the tool adds to it as it goes.
  const carried: string[] = [];

  let step = `Starting`;
  let hasGivenUp = false;

  const giveUp = new Promise<never>((_resolve, reject) => {
    function fail(reason: string): void {
      hasGivenUp = true;

      reject(new Error(`${reason} at this step: ${step}. Nothing more is done to the game.`));
    }

    async function failAtCeiling(): Promise<void> {
      await host.alarm(run.ceilingSeconds);

      fail(`Gave up after ${run.ceilingSeconds} s, something never finished,`);
    }

    void failAtCeiling();

    signal.addEventListener(
      'abort',
      () => {
        fail(`Cancelled`);
      },
      { once: true }
    );
  });

  const guarded: GuardedContext = {
    ...context,
    host: guardHost(host, ended.signal),
    signal: ended.signal,
    carry: line => {
      carried.push(line);
    },
    progress: message => {
      // A step left behind must not take the slot back from whoever holds it by now.
      if (ended.signal.aborted) {
        return;
      }

      step = message;

      if (run.isExclusive) {
        slot.running = `${run.name} (${message})`;
      }

      context.progress(message);
    }
  };

  async function runGuarded(): Promise<string> {
    await requireUserData(context.userDataPath);

    return tool(guarded);
  }

  // One block, a line per part: a client may run separate blocks together with nothing between.
  async function endCall(result: CallToolResult): Promise<CallToolResult> {
    const hint = hasGivenUp ? undefined : await readHint(context);

    const lines = [
      ...carried,
      ...result.content.map(block => {
        assert(block.type == 'text');

        return block.text;
      }),
      ...(hint == undefined ? [] : [hint])
    ];

    return { ...result, content: [toTextBlock(lines.join('\n'))] };
  }

  try {
    return await endCall(textResult(await Promise.race([runGuarded(), giveUp])));
  } catch (error) {
    if (hasGivenUp) {
      void cleanUp(run);
    }

    return await endCall(errorResult(errorMessage(error)));
  } finally {
    ended.abort();

    if (run.isExclusive) {
      slot.running = undefined;
    }
  }
}

/**
 * The host, failing every step once `ended` aborts.
 */
function guardHost(host: GameHost, ended: AbortSignal): GameHost {
  function guard<TArguments extends readonly unknown[], TResult>(
    step: (...args: TArguments) => Promise<TResult>
  ): (...args: TArguments) => Promise<TResult> {
    return (...args) => {
      if (ended.aborted) {
        return Promise.reject(new Error(`The call ended.`));
      }

      return step(...args);
    };
  }

  return {
    now: host.now,
    alarm: host.alarm,
    findPids: guard(host.findPids),
    launch: guard(host.launch),
    askToEnd: guard(host.askToEnd),
    forceToEnd: guard(host.forceToEnd),
    sleep: guard(host.sleep)
  };
}

async function cleanUp(run: ToolRun): Promise<void> {
  try {
    await run.onGiveUp?.();
  } catch {
    // The call already failed, and says what it left.
  }
}

function textResult(text: string): CallToolResult {
  return { content: [toTextBlock(text)] };
}

function errorResult(text: string): CallToolResult {
  return { content: [toTextBlock(text)], isError: true };
}

function toTextBlock(text: string): CallToolResult['content'][number] {
  return { type: 'text', text };
}
