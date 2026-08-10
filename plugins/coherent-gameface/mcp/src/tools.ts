/**
 * Tool implementations for the Gameface MCP server. Each maps to one or more CDP commands via
 * the shared CdpClient and returns an MCP CallToolResult.
 */

import { setTimeout as sleep } from 'node:timers/promises';
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { oneLine } from 'common-tags';
import type { CdpClient } from './cdp';
import {
  type EvaluateResult,
  describeRemoteObject,
  errorText,
  formatException,
  text,
  toErrorResult
} from './shared';

// Result shapes returned by the page functions below, reused by the server-side callers to
// cast Runtime.evaluate results.
interface DomElementInfo {
  tagName: string | null;
  id: string | null;
  classes: string | null;
  rect: { x: number; y: number; width: number; height: number };
  attributes: Record<string, string>;
  outerHTML: string;
  truncated: boolean;
}

interface CollectDomResult {
  count: number;
  elements: DomElementInfo[];
}

type ClickResult =
  | { found: true; count: number; x: number; y: number; fired: string[] }
  | { found: false; count: number; fired: string[] };

type FillResult =
  | { found: true; count: number; mode: string; value: string }
  | { found: false; count: number };

type TypeResult =
  | { found: true; count: number; typed: number; value: string }
  | { found: false; count: number; typed: 0 };

type HoverResult =
  | { found: true; count: number; x: number; y: number; fired: string[] }
  | { found: false; count: number };

type RectResult =
  | { found: true; count: number; x: number; y: number; width: number; height: number }
  | { found: false; count: number };

type KeyResult =
  | {
      found: true;
      // How the target was resolved, for the human-readable result.
      via: 'selector' | 'activeElement' | 'document';
      // Compact <tag#id.class> descriptor of the resolved element, or 'document' when no element.
      target: string;
      // Selector match count when via=selector, else null.
      matches: number | null;
      presses: number;
      // True when any keydown had preventDefault() called: the observable signal a JS handler
      // consumed the key.
      defaultPrevented: boolean;
    }
  | { found: false; count: number };

// Input to keyFn, passed as one object so the page function stays under the params ceiling.
interface KeyArgs {
  key: string;
  count: number;
  ctrl: boolean;
  shift: boolean;
  alt: boolean;
  meta: boolean;
  // Null selects the focused element (or document); a string targets its index-th match.
  sel: string | null;
  index: number;
}

interface QueryMatch {
  tagName: string | null;
  id: string | null;
  classes: string | null;
  rect: { x: number; y: number; width: number; height: number };
  text: string;
  truncated: boolean;
  // Present only when attributes=true: every attribute of the match, name to value.
  attributes?: Record<string, string>;
  // Present only when tag=true: a ready-to-use selector targeting this match's data-gf-tag handle.
  selector?: string;
}

interface QueryResult {
  // Matches before deepest pruning; unprunedTotal vs. total shows how many ancestor matches the
  // deepest filter removed.
  unprunedTotal: number;
  // Matches after deepest pruning, before the limit truncates; total vs returned shows the limit
  // truncating.
  total: number;
  returned: number;
  tagged: boolean;
  elements: QueryMatch[];
}

// Input to queryFn, passed as one object so the page function stays under the params ceiling.
interface QueryArgs {
  sel: string;
  // Null selects on the selector alone, with no text filter.
  needle: string | null;
  mode: string;
  caseSensitive: boolean;
  deepest: boolean;
  tag: boolean;
  attributes: boolean;
  limit: number;
}

/**
 * Server-side polling interval for game_wait.
 */
const POLL_INTERVAL_MS = 150;

/**
 * Hard ceiling on game_wait budgets, so a huge timeoutMs cannot hang a tool call for minutes.
 */
const MAX_WAIT_MS = 60_000;

const DEFAULT_WAIT_TIMEOUT_MS = 8000;

/**
 * Reload waits default higher: the trigger latency of an application-side file watcher can run tens
 * of seconds (CS2's mtime poll ticks every ~15-20s) before the first context swap.
 */
const DEFAULT_RELOAD_WAIT_TIMEOUT_MS = 30_000;

/**
 * Default quiescence window after a reload is observed; long enough to absorb engines that swap
 * the context more than once per reload trigger (CS2's watcher fires two swaps ~530ms apart).
 */
const DEFAULT_QUIESCENT_MS = 1000;

const DEFAULT_JPEG_QUALITY = 80;
const DEFAULT_QUERY_LIMIT = 20;

/**
 * Reports reachability + page target + engine info + view-reload tracking. Never throws.
 */
export async function gameStatus(
  client: CdpClient,
  reloads: ReloadTracker
): Promise<CallToolResult> {
  const { host, port } = client.config;

  try {
    const target = await client.discover();
    let browser: string | undefined;
    let protocol: string | undefined;

    try {
      // noinspection HttpUrlsUsage
      const res = await fetch(`http://${host}:${port}/json/version`);

      if (res.ok) {
        const versionInfo = (await res.json()) as Record<string, string>;

        browser = versionInfo.Browser;
        protocol = versionInfo['Protocol-Version'];
      }
    } catch {
      /* Version info is best-effort. */
    }

    // Arm reload tracking: opening the WebSocket enables Runtime, which starts the passive
    // execution-context watch. Best-effort: the HTTP-only report must survive a WS failure.
    let tracking = false;

    try {
      await client.connection();
      tracking = true;
    } catch {
      /* HTTP reachable but no WS; report tracking: false. */
    }

    const { lastReloadAt } = reloads;

    // noinspection HttpUrlsUsage
    return text(
      JSON.stringify(
        {
          reachable: true,
          endpoint: `http://${host}:${port}`,
          target: { id: target.id, url: target.url, title: target.title, wsUrl: target.wsUrl },
          browser,
          cdpProtocol: protocol,
          reloads: tracking
            ? {
                tracking: true,
                // Lower bound: reloads that happen while disconnected collapse into one.
                count: reloads.count,
                lastReloadAt: lastReloadAt == null ? null : new Date(lastReloadAt).toISOString(),
                lastReloadAgoMs: lastReloadAt == null ? null : Date.now() - lastReloadAt,
                contextUniqueId: reloads.contextUniqueId ?? null
              }
            : { tracking: false }
        },
        null,
        2
      )
    );
  } catch (error) {
    // noinspection HttpUrlsUsage
    return text(
      JSON.stringify(
        {
          reachable: false,
          endpoint: `http://${host}:${port}`,
          error: error instanceof Error ? error.message : String(error),
          hint: oneLine`
            Launch the Gameface application with its CDP debug port open, then retry.
            Override host/port via GAMEFACE_HOST / GAMEFACE_PORT.
          `
        },
        null,
        2
      )
    );
  }
}

/**
 * Evaluates a JS expression in the page and returns its value as JSON.
 */
export async function gameEval(
  client: CdpClient,
  expression: string,
  awaitPromise = false
): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>('Runtime.evaluate', {
      expression,
      returnByValue: true,
      awaitPromise
    });

    if (res.exceptionDetails) {
      return errorText(`Evaluation threw: ${formatException(res.exceptionDetails)}`);
    }

    const value = describeRemoteObject(res.result);

    return text(typeof value == 'string' ? value : JSON.stringify(value, null, 2));
  } catch (error) {
    return toErrorResult(error);
  }
}

/**
 * Options for gameScreenshot.
 */
export interface GameScreenshotOptions {
  readonly format?: 'png' | 'jpeg' | undefined;
  readonly quality?: number | undefined;
  readonly selector?: string | undefined;
  readonly index?: number | undefined;
}

/**
 * Captures a screenshot of the Gameface UI and returns it as an inline image.
 * When a selector is given, the capture is clipped to the `index`-th match's bounding box.
 */
export async function gameScreenshot(
  client: CdpClient,
  options: GameScreenshotOptions = {}
): Promise<CallToolResult> {
  // PNG by default: the capture is the UI layer alone (flat fills and small text, no 3D scene),
  // so lossless costs little payload and spares the glyphs JPEG rings around.
  const { format = 'png', quality, selector, index = 0 } = options;

  try {
    await client.ensureDomain('Page');

    const params: Record<string, unknown> = { format };

    if (format == 'jpeg') {
      params.quality = quality ?? DEFAULT_JPEG_QUALITY;
    }

    let caption: string | undefined;

    if (selector) {
      const rectRes = await client.call<EvaluateResult>('Runtime.evaluate', {
        expression: callPageFn(rectFn, selector, index),
        returnByValue: true
      });

      const rect = rectRes.result.value as RectResult | undefined;

      if (!rect?.found) {
        return errorText(oneLine`
          No element matched ${JSON.stringify(selector)} for game_screenshot at index ${index}
          (matches found: ${rect?.count ?? 0}).
        `);
      }

      if (!(rect.width > 0 && rect.height > 0)) {
        return errorText(
          `Element ${JSON.stringify(selector)} has a zero-size box; nothing to capture.`
        );
      }

      params.clip = { x: rect.x, y: rect.y, width: rect.width, height: rect.height, scale: 1 };

      caption = oneLine`
        Clipped to ${JSON.stringify(selector)} [index ${index}]. Matches: ${rect.count}.
      `;
    }

    const res = await client.call<{ data?: string }>('Page.captureScreenshot', params);

    if (!res?.data) {
      return errorText(`Page.captureScreenshot returned no image data.`);
    }

    const image = {
      type: 'image' as const,
      data: res.data,
      mimeType: format == 'jpeg' ? 'image/jpeg' : 'image/png'
    };

    // A clipped capture prefixes a text block naming the match and index; a full-viewport capture
    // (no selector) has no match concept and stays image-only.
    return { content: caption ? [{ type: 'text', text: caption }, image] : [image] };
  } catch (error) {
    return toErrorResult(error);
  }
}

/**
 * Returns DOM info (tag, classes, attributes, rect, outerHTML) for a selector.
 */
export async function gameDom(
  client: CdpClient,
  selector: string,
  all = false,
  maxHtml = 4000
): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>('Runtime.evaluate', {
      expression: callPageFn(collectDomFn, selector, all, maxHtml),
      returnByValue: true
    });

    if (res.exceptionDetails) {
      return errorText(`DOM query failed: ${formatException(res.exceptionDetails)}`);
    }

    const value = res.result.value as CollectDomResult | undefined;

    if (!value || value.count == 0) {
      return text(JSON.stringify({ selector, count: 0, elements: [] }, null, 2));
    }

    return text(JSON.stringify({ selector, ...value }, null, 2));
  } catch (error) {
    return toErrorResult(error);
  }
}

/**
 * Options for gameQuery.
 */
export interface GameQueryOptions {
  readonly text?: string | undefined;
  readonly match?: 'equals' | 'contains' | 'regex' | undefined;
  readonly caseSensitive?: boolean | undefined;
  readonly selector?: string | undefined;
  readonly deepest?: boolean | undefined;
  readonly tag?: boolean | undefined;
  readonly attributes?: boolean | undefined;
  readonly limit?: number | undefined;
}

/**
 * Locates elements by a CSS selector, by their trimmed textContent (equals / contains / regex), or
 * by both, returning lean, actionable info per match with the total count, so truncation shows.
 * With `tag=true`, stamps matches with `data-gf-tag` handles and returns ready-to-use selectors,
 * solving the discovery-to-action handoff when no unique selector can be written.
 */
export async function gameQuery(
  client: CdpClient,
  options: GameQueryOptions
): Promise<CallToolResult> {
  const mode = options.match ?? 'contains';
  const needle = options.text ?? null;
  const { selector } = options;

  const rejection = queryRejection(options, mode);

  if (rejection != null) {
    return errorText(rejection);
  }

  const args: QueryArgs = {
    sel: selector ?? '*',
    needle,
    mode,
    caseSensitive: options.caseSensitive ?? false,
    // Text bleeds into ancestors, so pruning there drops an artifact. An ancestor matching a
    // selector is a genuine match, and so is one matching empty text -- the icon-only control
    // wrapping a text-free span -- so pruning either would drop what the caller asked for.
    deepest: options.deepest ?? (needle != null && needle.length > 0),
    tag: options.tag ?? false,
    attributes: options.attributes ?? false,
    limit: options.limit ?? DEFAULT_QUERY_LIMIT
  };

  try {
    const res = await client.call<EvaluateResult>('Runtime.evaluate', {
      expression: callPageFn(queryFn, args),
      returnByValue: true
    });

    if (res.exceptionDetails) {
      return errorText(`Query failed: ${formatException(res.exceptionDetails)}`);
    }

    const value = res.result.value as QueryResult | { error: string } | undefined;

    if (!value) {
      return errorText(`game_query returned no result for selector ${JSON.stringify(args.sel)}.`);
    }

    if ('error' in value) {
      return errorText(`game_query: ${value.error}`);
    }

    // The match mode describes an operation that never ran when no text was given, and the key is
    // left undefined rather than null so it drops out of the JSON entirely: an explicit null would
    // read as a filter that ran. deepest is reported either way, since unprunedTotal and total come
    // back equal both when pruning found nothing and when it never ran.
    const envelope = {
      selector: args.sel,
      match: args.needle == null ? undefined : args.mode,
      deepest: args.deepest,
      ...value
    };

    return text(JSON.stringify(envelope, null, 2));
  } catch (error) {
    return toErrorResult(error);
  }
}

/**
 * Names the argument combinations gameQuery refuses rather than answering as asked.
 * Returns undefined when the arguments are usable.
 */
function queryRejection(
  options: GameQueryOptions,
  mode: NonNullable<GameQueryOptions['match']>
): string | undefined {
  const { text: needle, selector } = options;

  if (needle == null && selector == null) {
    return `game_query needs text, selector, or both; neither was given.`;
  }

  if (selector != null && selector.trim().length == 0) {
    return `game_query: selector is blank; pass a real one.`;
  }

  if (mode == 'equals' && needle != null && needle.trim() != needle) {
    return oneLine`
      game_query: equals compares against trimmed text, so trim yours; where that empties it, pass
      '' with a selector to find elements carrying no text.
    `;
  }

  if (needle == '' && mode != 'equals') {
    return oneLine`
      game_query: empty text matches every element under ${mode}; pass match: 'equals' with a
      selector to find elements carrying no text.
    `;
  }

  // Every void element in the document answers this one, head metadata included.
  if (needle == '' && selector == null) {
    return `game_query: empty text needs a selector to scope it.`;
  }

  return undefined;
}

/**
 * Clicks the element matching `selector` (the `index`-th match) by dispatching a realistic bubbling
 * pointer/mouse/click sequence in the page.
 * We do NOT use CDP Input.dispatchMouseEvent: Gameface accepts it but never delivers it.
 */
export async function gameClick(
  client: CdpClient,
  selector: string,
  index = 0
): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>('Runtime.evaluate', {
      expression: callPageFn(clickFn, selector, index),
      returnByValue: true
    });

    if (res.exceptionDetails) {
      return errorText(`Click failed: ${formatException(res.exceptionDetails)}`);
    }

    const info = res.result.value as ClickResult | undefined;

    if (!info?.found) {
      return errorText(oneLine`
        No element to click for selector ${JSON.stringify(selector)} at index ${index}
        (matches found: ${info?.count ?? 0}).
      `);
    }

    return text(oneLine`
      Clicked ${JSON.stringify(selector)} [index ${index}] at
      (${info.x.toFixed(0)}, ${info.y.toFixed(0)}).
      Dispatched: ${info.fired.join(', ')}. Matches: ${info.count}.
    `);
  } catch (error) {
    return toErrorResult(error);
  }
}

/**
 * Options for gameWait.
 * Provide at least one of `reload` / `selector` / `predicate`; `selector` and `predicate` are
 * mutually exclusive.
 */
export interface GameWaitOptions {
  readonly selector?: string | undefined;
  readonly predicate?: string | undefined;
  readonly reload?: boolean | undefined;
  readonly sinceReloads?: number | undefined;
  readonly quiescentMs?: number | undefined;
  readonly timeoutMs?: number | undefined;
  readonly visible?: boolean | undefined;
}

/**
 * Waits (server-side polling) for a view reload and/or until a selector matches or a JS
 * predicate is truthy.
 * The phases compose in order: reload, then quiescence, then the selector/predicate poll running
 * in the fresh context.
 */
export async function gameWait(
  client: CdpClient,
  reloads: ReloadTracker,
  options: GameWaitOptions
): Promise<CallToolResult> {
  const {
    selector,
    predicate,
    reload = false,
    sinceReloads,
    quiescentMs = DEFAULT_QUIESCENT_MS,
    visible = false
  } = options;

  if (!reload && !selector && !predicate) {
    return errorText(`game_wait needs at least one of 'reload', 'selector', or 'predicate'.`);
  }

  if (selector && predicate) {
    return errorText(`game_wait takes either 'selector' or 'predicate', not both.`);
  }

  const timeoutMs =
    options.timeoutMs ?? (reload ? DEFAULT_RELOAD_WAIT_TIMEOUT_MS : DEFAULT_WAIT_TIMEOUT_MS);
  const budget = Math.min(Math.max(timeoutMs, 0), MAX_WAIT_MS);

  const start = Date.now();
  const deadline = start + budget;

  try {
    if (reload) {
      const failed = await waitForReload();

      if (failed) {
        return failed;
      }

      if (!selector && !predicate) {
        return text(
          `Reload observed after ${Date.now() - start}ms (reload count: ${reloads.count}).`
        );
      }
    }

    return await pollCondition();
  } catch (error) {
    return toErrorResult(error);
  }

  /**
   * Reload phase + quiescence phase. Returns an error result on timeout, undefined on success.
   */
  async function waitForReload(): Promise<CallToolResult | undefined> {
    // Arm tracking before baselining: the counter only moves while a WS connection exists.
    await client.connection();

    // `sinceReloads` lets the caller baseline against a prior game_status, so a reload that
    // fired between that call and this one (location.reload() lands in ~200ms, faster than the
    // next tool call) still satisfies the wait instead of hanging until timeout.
    const baseline = sinceReloads ?? reloads.count;

    while (reloads.count <= baseline) {
      if (Date.now() >= deadline) {
        return errorText(oneLine`
          Timed out after ${budget}ms waiting for a view reload
          (reload count still ${reloads.count}, baseline ${baseline}).
        `);
      }

      await sleep(POLL_INTERVAL_MS);
    }

    // Quiescence: hold until no further context swap for quiescentMs, so an engine that swaps
    // the context several times per reload trigger yields one wait return, not one per swap.
    if (quiescentMs > 0) {
      let lastCount = reloads.count;
      let quietSince = Date.now();

      while (Date.now() - quietSince < quiescentMs) {
        if (Date.now() >= deadline) {
          return errorText(oneLine`
            Timed out after ${budget}ms: reload observed, but context swaps kept arriving within the
            ${quiescentMs}ms quiescence window.
          `);
        }

        await sleep(POLL_INTERVAL_MS);

        if (reloads.count != lastCount) {
          lastCount = reloads.count;
          quietSince = Date.now();
        }
      }
    }

    return undefined;
  }

  /**
   * The selector/predicate poll loop.
   */
  async function pollCondition(): Promise<CallToolResult> {
    const expression = selector
      ? callPageFn(waitCheckFn, selector, visible)
      : `Boolean(${predicate ?? ''})`;

    // Remember the last predicate error so a predicate that consistently throws (e.g., a typo)
    // surfaces in the timeout message instead of failing silently on every poll.
    let lastError: string | undefined;

    while (true) {
      const res = await client.call<EvaluateResult>('Runtime.evaluate', {
        expression,
        returnByValue: true
      });

      if (res.exceptionDetails) {
        lastError = formatException(res.exceptionDetails);
      } else if (res.result.value) {
        const what = selector ? `selector ${JSON.stringify(selector)}` : 'predicate';

        // The reload count lets agents chain baselines without an extra game_status call.
        const reloadNote = reload ? ` Reload count: ${reloads.count}.` : '';

        return text(`Condition met (${what}) after ${Date.now() - start}ms.${reloadNote}`);
      } else {
        // The expression evaluated cleanly but falsy; clear any stale error.
        lastError = undefined;
      }

      if (Date.now() >= deadline) {
        const what = selector ? `selector ${JSON.stringify(selector)}` : 'predicate';
        const errorNote = lastError ? ` Last predicate error: ${lastError}` : '';

        return errorText(`Timed out after ${budget}ms waiting for ${what}.${errorNote}`);
      }

      await sleep(POLL_INTERVAL_MS);
    }
  }
}

/**
 * Sets the value of an input/textarea/contenteditable (framework-aware).
 */
export async function gameFill(
  client: CdpClient,
  selector: string,
  value: string,
  index = 0
): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>('Runtime.evaluate', {
      expression: callPageFn(fillFn, selector, value, index),
      returnByValue: true
    });

    if (res.exceptionDetails) {
      return errorText(`Fill failed: ${formatException(res.exceptionDetails)}`);
    }

    const info = res.result.value as FillResult | undefined;

    if (!info?.found) {
      return errorText(oneLine`
        No element matched ${JSON.stringify(selector)} for game_fill at index ${index}
        (matches found: ${info?.count ?? 0}).
      `);
    }

    return text(oneLine`
      Filled ${JSON.stringify(selector)} [index ${index}] (${info.mode}).
      Value is now ${JSON.stringify(info.value)}. Matches: ${info.count}.
    `);
  } catch (error) {
    return toErrorResult(error);
  }
}

/**
 * Types text into an element key by key (real KeyboardEvents + value sync).
 */
export async function gameType(
  client: CdpClient,
  selector: string,
  textToType: string,
  index = 0
): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>('Runtime.evaluate', {
      expression: callPageFn(typeFn, selector, textToType, index),
      returnByValue: true
    });

    if (res.exceptionDetails) {
      return errorText(`Type failed: ${formatException(res.exceptionDetails)}`);
    }

    const info = res.result.value as TypeResult | undefined;

    if (!info?.found) {
      return errorText(oneLine`
        No element matched ${JSON.stringify(selector)} for game_type at index ${index}
        (matches found: ${info?.count ?? 0}).
      `);
    }

    return text(oneLine`
      Typed ${info.typed} char(s) into ${JSON.stringify(selector)} [index ${index}].
      Value is now ${JSON.stringify(info.value)}. Matches: ${info.count}.
    `);
  } catch (error) {
    return toErrorResult(error);
  }
}

/**
 * Hovers an element by dispatching the over/enter/move event sequence.
 */
export async function gameHover(
  client: CdpClient,
  selector: string,
  index = 0
): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>('Runtime.evaluate', {
      expression: callPageFn(hoverFn, selector, index),
      returnByValue: true
    });

    if (res.exceptionDetails) {
      return errorText(`Hover failed: ${formatException(res.exceptionDetails)}`);
    }

    const info = res.result.value as HoverResult | undefined;

    if (!info?.found) {
      return errorText(oneLine`
        No element matched ${JSON.stringify(selector)} for game_hover at index ${index}
        (matches found: ${info?.count ?? 0}).
      `);
    }

    return text(oneLine`
      Hovered ${JSON.stringify(selector)} [index ${index}] at
      (${info.x.toFixed(0)}, ${info.y.toFixed(0)}).
      Dispatched: ${info.fired.join(', ')}. Matches: ${info.count}.
    `);
  } catch (error) {
    return toErrorResult(error);
  }
}

/**
 * Options for gameKey.
 */
export interface GameKeyOptions {
  readonly key: string;
  readonly count?: number | undefined;
  readonly ctrl?: boolean | undefined;
  readonly shift?: boolean | undefined;
  readonly alt?: boolean | undefined;
  readonly meta?: boolean | undefined;
  readonly selector?: string | undefined;
  readonly index?: number | undefined;
}

/**
 * Presses a named key by dispatching real bubbling keydown+keyup events (no keypress, no default
 * action), optionally with modifiers and repeats, on a selector/the focused element/document.
 */
export async function gameKey(client: CdpClient, options: GameKeyOptions): Promise<CallToolResult> {
  const {
    key,
    count = 1,
    ctrl = false,
    shift = false,
    alt = false,
    meta = false,
    selector,
    index = 0
  } = options;

  try {
    const res = await client.call<EvaluateResult>('Runtime.evaluate', {
      expression: callPageFn(keyFn, {
        key,
        count,
        ctrl,
        shift,
        alt,
        meta,
        sel: selector ?? null,
        index
      }),
      returnByValue: true
    });

    if (res.exceptionDetails) {
      return errorText(`Key press failed: ${formatException(res.exceptionDetails)}`);
    }

    const info = res.result.value as KeyResult | undefined;

    if (!info) {
      return errorText(`game_key returned no result.`);
    }

    if (!info.found) {
      return errorText(oneLine`
        No element matched ${JSON.stringify(selector)} for game_key at index ${index}
        (matches found: ${info.count}).
      `);
    }

    // Where the press landed: the selector match, the focused element, or the document.
    const where =
      info.via == 'selector'
        ? `${JSON.stringify(selector)} [index ${index}] ${info.target}`
        : info.via == 'activeElement'
          ? `the focused element ${info.target}`
          : `document`;

    const matchNote = info.matches == null ? '' : ` Matches: ${info.matches}.`;

    return text(oneLine`
      Pressed ${keyLabel(key, { ctrl, shift, alt, meta })} ${info.presses}x on ${where}.${matchNote}
      Default prevented: ${info.defaultPrevented ? 'yes' : 'no'}.
    `);
  } catch (error) {
    return toErrorResult(error);
  }
}

/**
 * Builds a human-readable "Ctrl+Shift+Escape" label for the result line.
 */
function keyLabel(
  key: string,
  mods: { ctrl: boolean; shift: boolean; alt: boolean; meta: boolean }
): string {
  const parts: string[] = [];

  if (mods.ctrl) {
    parts.push('Ctrl');
  }

  if (mods.shift) {
    parts.push('Shift');
  }

  if (mods.alt) {
    parts.push('Alt');
  }

  if (mods.meta) {
    parts.push('Meta');
  }

  parts.push(key == ' ' ? 'Space' : key);

  return parts.join('+');
}

/**
 * Payload of Runtime.executionContextCreated (only the fields we read).
 * Gameface serializes auxData.isDefault as the string "true" (verified); accept the boolean too.
 */
interface ExecutionContextCreatedParams {
  readonly context?: {
    readonly uniqueId?: string;
    readonly auxData?: { readonly isDefault?: boolean | string };
  };
}

/**
 * Called after each detected view reload with the new (monotonic) reload count.
 */
export type ReloadListener = (count: number) => void;

/**
 * Passively counts UI view reloads by watching the default execution context's uniqueId.
 *
 * A view reload (mod hot-reload, `location.reload()`) destroys and recreates the page's default
 * execution context on the surviving WebSocket; Gameface never fires `Page.frameNavigated` or
 * `Runtime.executionContextsCleared`, so `executionContextCreated` is THE reload signal.
 * `Runtime.enable` replays `executionContextCreated` for the already-live context, so the first
 * uniqueId observed is the baseline, not a reload; a reconnect replaying the SAME uniqueId
 * (socket blip) does not count either, while a different one (e.g., a game restart while
 * disconnected) does.
 * The count is monotonic for the server process lifetime and is a lower bound: reloads that happen
 * while disconnected collapse into one.
 */
export class ReloadTracker {
  private reloadCount = 0;

  private lastReloadAtMs: number | undefined;

  private uniqueId: string | undefined;

  private readonly reloadListeners = new Set<ReloadListener>();

  public constructor(client: CdpClient) {
    client.onConnect(async conn => {
      await conn.ensureDomain('Runtime');
    });

    client.onEvent((method, params) => {
      this.handle(method, params);
    });
  }

  public get count(): number {
    return this.reloadCount;
  }

  /**
   * Epoch ms of the last detected reload, or undefined before the first one.
   */
  public get lastReloadAt(): number | undefined {
    return this.lastReloadAtMs;
  }

  /**
   * The current default execution context's uniqueId, or undefined before the first
   * executionContextCreated (none observed yet on this server's connections).
   */
  public get contextUniqueId(): string | undefined {
    return this.uniqueId;
  }

  /**
   * Subscribe to reload detections (e.g., to prune per-context caches).
   */
  public onReload(listener: ReloadListener): void {
    this.reloadListeners.add(listener);
  }

  private handle(method: string, params: unknown): void {
    if (method != 'Runtime.executionContextCreated') {
      return;
    }

    const { context } = params as ExecutionContextCreatedParams;
    const isDefault = context?.auxData?.isDefault;

    // Only the default (page) context defines a view reload; ignore auxiliary contexts.
    if (!(isDefault == true || isDefault == 'true') || context?.uniqueId == null) {
      return;
    }

    if (this.uniqueId == context.uniqueId) {
      return;
    }

    const isBaseline = this.uniqueId == null;

    this.uniqueId = context.uniqueId;

    if (isBaseline) {
      return;
    }

    this.reloadCount++;
    this.lastReloadAtMs = Date.now();

    for (const listener of this.reloadListeners) {
      try {
        listener(this.reloadCount);
      } catch {
        /* Listener errors must not break event handling. */
      }
    }
  }
}

/**
 * Serializes a page-context function and its JSON-safe args into one self-invoking expression
 * for Runtime.evaluate.
 */
function callPageFn(fn: (...args: never[]) => unknown, ...args: unknown[]): string {
  const serialisedArgs = args.map(arg => JSON.stringify(arg)).join(', ');

  return `(${fn.toString()})(${serialisedArgs})`;
}

// Page-context functions.
// These run inside the Gameface UI (never in this process); they are serialized with
// .toString() and injected into Runtime.evaluate. Keep them as plain, self-contained browser
// JS with no references to anything outside their body. Type annotations are fine: the build
// erases them before serialization.

/**
 * Collects DOM info (tag, id, classes, rect, attributes, outerHTML) for a selector's matches.
 */
function collectDomFn(sel: string, all: boolean, maxHtml: number): CollectDomResult {
  const matches = document.querySelectorAll(sel);
  const first = matches.item(0);
  const chosen = all ? Array.from(matches) : first ? [first] : [];

  return { count: matches.length, elements: chosen.map(el => describe(el)) };

  function describe(el: Element): DomElementInfo {
    const rect = el.getBoundingClientRect();
    const attributes: Record<string, string> = {};

    for (const attr of Array.from(el.attributes)) {
      attributes[attr.name] = attr.value;
    }

    const classAttr = el.getAttribute('class');
    let html = el.outerHTML || '';
    const truncated = html.length > maxHtml;

    if (truncated) {
      html = html.slice(0, maxHtml);
    }

    return {
      tagName: el.tagName ? el.tagName.toLowerCase() : null,
      id: el.id || null,
      classes: classAttr != null && classAttr.length > 0 ? classAttr : null,
      rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height },
      attributes,
      outerHTML: html,
      truncated
    };
  }
}

/**
 * Selects querySelectorAll matches, optionally filtering them on trimmed textContent, the only text
 * search Cohtml affords (no XPath, TreeWalker, or innerText).
 * Returns lean, actionable info per match and, when tag=true, stamps handles so the result feeds
 * straight into the input tools.
 */
function queryFn(args: QueryArgs): QueryResult | { error: string } {
  const { sel, needle, mode, caseSensitive, deepest, tag, attributes, limit } = args;

  // Cap on the returned text snippet, kept inline because page functions are self-contained.
  const SNIPPET_MAX = 100;

  // Precompile the matcher once. Case insensitivity lowercases both sides for equals/contains and
  // adds the 'i' flag for regex. Unused where the selector alone selects, which the scan gates on.
  const target = needle == null ? '' : caseSensitive ? needle : needle.toLowerCase();

  let regex: RegExp | undefined;

  if (needle != null && mode == 'regex') {
    try {
      regex = new RegExp(needle, caseSensitive ? '' : 'i');
    } catch (error) {
      return { error: `Invalid regex: ${error instanceof Error ? error.message : String(error)}` };
    }
  }

  const found: Element[] = [];

  for (const el of Array.from(document.querySelectorAll(sel))) {
    // Reading textContent materializes the whole subtree's text, so skip it with no filter to run.
    if (needle == null || matches((el.textContent || '').trim())) {
      found.push(el);
    }
  }

  const kept = deepest ? innermost(found) : found;

  const chosen = kept.slice(0, limit);

  if (tag) {
    // Clear-then-retag: strip every handle from a previous query first, so its handles die here.
    // Cohtml exposes setAttribute/removeAttribute but not the dataset DOMStringMap, so use those.
    for (const stale of Array.from(document.querySelectorAll('[data-gf-tag]'))) {
      stale.removeAttribute('data-gf-tag');
    }

    for (const [i, el] of chosen.entries()) {
      el.setAttribute('data-gf-tag', String(i + 1));
    }
  }

  return {
    unprunedTotal: found.length,
    total: kept.length,
    returned: chosen.length,
    tagged: tag,
    elements: chosen.map((el, i) => describe(el, i))
  };

  // Striking each match's ancestor chain costs depth per match; comparing every pair instead would
  // cost the square of the match count, which a selector alone can make large.
  function innermost(all: Element[]): Element[] {
    const remaining = new Set(all);

    for (const el of all) {
      for (let parent = el.parentElement; parent != null; parent = parent.parentElement) {
        remaining.delete(parent);
      }
    }

    return Array.from(remaining);
  }

  function matches(raw: string): boolean {
    if (mode == 'regex') {
      return regex != null && regex.test(raw);
    }

    const hay = caseSensitive ? raw : raw.toLowerCase();

    if (mode == 'equals') {
      return hay == target;
    }

    // 'contains' is the default mode.
    return hay.includes(target);
  }

  function describe(el: Element, i: number): QueryMatch {
    const rect = el.getBoundingClientRect();
    const classAttr = el.getAttribute('class');
    const raw = (el.textContent || '').trim();
    const truncated = raw.length > SNIPPET_MAX;

    const info: QueryMatch = {
      tagName: el.tagName ? el.tagName.toLowerCase() : null,
      id: el.id || null,
      classes: classAttr != null && classAttr.length > 0 ? classAttr : null,
      rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height },
      text: truncated ? raw.slice(0, SNIPPET_MAX) : raw,
      truncated
    };

    if (attributes) {
      const map: Record<string, string> = {};

      for (const attr of Array.from(el.attributes)) {
        // The tool's own handle is never a durable anchor, so reporting it beside the page's own
        // attributes would invite the next query to anchor on the one that dies first.
        if (attr.name != 'data-gf-tag') {
          map[attr.name] = attr.value;
        }
      }

      info.attributes = map;
    }

    if (tag) {
      info.selector = `[data-gf-tag="${i + 1}"]`;
    }

    return info;
  }
}

/**
 * Gameface ACCEPTS CDP Input.dispatchMouseEvent but does NOT route it into the Cohtml DOM event
 * system (verified: handlers never fire). So we click by dispatching real, bubbling DOM events on
 * the element, which the UI's delegated event listeners pick up.
 * Note: `HTMLElement.click()` does not exist in Cohtml either.
 */
function clickFn(sel: string, index: number): ClickResult {
  const nodes = document.querySelectorAll(sel);

  if (nodes.length == 0) {
    return { found: false, count: 0, fired: [] };
  }

  const node = nodes[index] as HTMLElement | undefined;

  if (!node) {
    return { found: false, count: nodes.length, fired: [] };
  }

  // Alias the narrowed node so the hoisted fire() helper below closes over a non-null element.
  const el = node;

  const rect = el.getBoundingClientRect();
  const cx = rect.x + rect.width / 2;
  const cy = rect.y + rect.height / 2;

  const base: MouseEventInit = {
    bubbles: true,
    cancelable: true,
    // oxlint-disable-next-line unicorn/prefer-global-this -- Browser page context; MouseEventInit.view wants the Window.
    view: window,
    button: 0,
    clientX: cx,
    clientY: cy
  };

  type EventCtor = new (type: string, init?: PointerEventInit) => Event;

  // PointerEvent does not exist in Cohtml; dispatch pointer* as MouseEvents (delegated handlers
  // key off the event type string, not the constructor).
  const Pointer: EventCtor = typeof PointerEvent == 'function' ? PointerEvent : MouseEvent;
  const fired: string[] = [];

  fire(Pointer, 'pointerdown', { pointerId: 1, isPrimary: true });
  fire(MouseEvent, 'mousedown');
  fire(Pointer, 'pointerup', { pointerId: 1, isPrimary: true });
  fire(MouseEvent, 'mouseup');
  fire(MouseEvent, 'click');

  return { found: true, count: nodes.length, x: cx, y: cy, fired };

  function fire(Ctor: EventCtor, type: string, extra?: PointerEventInit): void {
    try {
      el.dispatchEvent(new Ctor(type, { ...base, ...extra }));
      fired.push(type);
    } catch {
      /* Event type unsupported in this engine. */
    }
  }
}

/**
 * Returns true once the selector matches (and, if `visible`, has a non-zero box).
 */
function waitCheckFn(sel: string, visible: boolean): boolean {
  const el = document.querySelector(sel);

  if (!el) {
    return false;
  }

  if (!visible) {
    return true;
  }

  const rect = el.getBoundingClientRect();

  return rect.width > 0 && rect.height > 0;
}

/**
 * Sets an input/textarea/contenteditable value so the framework's change handler fires.
 * We use the native value setter (verified present in Cohtml) so a framework value tracker notices.
 * InputEvent is missing in Cohtml, so we dispatch a plain bubbling `Event('input')`.
 */
function fillFn(sel: string, value: string, index: number): FillResult {
  const nodes = document.querySelectorAll<HTMLElement>(sel);

  if (nodes.length == 0) {
    return { found: false, count: 0 };
  }

  const node = nodes[index];

  if (!node) {
    return { found: false, count: nodes.length };
  }

  // Alias the narrowed node so the branches below operate on a non-null element.
  const el = node;
  const count = nodes.length;

  try {
    el.focus();
  } catch {
    /* Not focusable. */
  }

  const tag = (el.tagName || '').toLowerCase();

  if (el.isContentEditable) {
    el.textContent = value;
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));

    return { found: true, count, mode: 'contenteditable', value: el.textContent ?? '' };
  }

  const field = el as HTMLInputElement | HTMLTextAreaElement;
  const proto = tag == 'textarea' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  // oxlint-disable-next-line typescript/unbound-method -- Deliberately unbound: invoked with .call(el) so the framework's value tracker sees the native setter run.
  const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;

  if (setter) {
    setter.call(el, value);
  } else {
    field.value = value;
  }

  el.dispatchEvent(new Event('input', { bubbles: true }));
  el.dispatchEvent(new Event('change', { bubbles: true }));

  return { found: true, count, mode: tag || 'input', value: field.value };
}

/**
 * Types text character by character, firing real KeyboardEvents (present in Cohtml) plus keeping
 * the value in sync and dispatching input/change for the framework.
 */
function typeFn(sel: string, textToType: string, index: number): TypeResult {
  const nodes = document.querySelectorAll<HTMLElement>(sel);

  if (nodes.length == 0) {
    return { found: false, count: 0, typed: 0 };
  }

  const node = nodes[index];

  if (!node) {
    return { found: false, count: nodes.length, typed: 0 };
  }

  // Alias the narrowed node so the hoisted helpers below close over a non-null element.
  const el = node;
  const count = nodes.length;

  try {
    el.focus();
  } catch {
    /* Not focusable. */
  }

  const tag = (el.tagName || '').toLowerCase();
  const editable = el.isContentEditable;
  const field = el as HTMLInputElement | HTMLTextAreaElement;
  const proto = tag == 'textarea' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  // oxlint-disable-next-line typescript/unbound-method -- Deliberately unbound: invoked with .call(el) so the framework's value tracker sees the native setter run.
  const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;

  let typed = 0;

  for (const ch of textToType) {
    const opts: KeyboardEventInit = {
      bubbles: true,
      cancelable: true,
      key: ch,
      // oxlint-disable-next-line unicorn/prefer-global-this -- Browser page context; KeyboardEventInit.view wants the Window.
      view: window
    };

    try {
      el.dispatchEvent(new KeyboardEvent('keydown', opts));
    } catch {
      /* No KeyboardEvent in this engine. */
    }

    setValue(current() + ch);

    el.dispatchEvent(new Event('input', { bubbles: true }));

    try {
      el.dispatchEvent(new KeyboardEvent('keyup', opts));
    } catch {
      /* No KeyboardEvent in this engine. */
    }

    typed++;
  }

  el.dispatchEvent(new Event('change', { bubbles: true }));

  return { found: true, count, typed, value: current() };

  function current(): string {
    if (editable) {
      return el.textContent ?? '';
    }

    return field.value || '';
  }

  function setValue(next: string): void {
    if (editable) {
      el.textContent = next;
    } else if (setter) {
      setter.call(el, next);
    } else {
      field.value = next;
    }
  }
}

/**
 * Hovers an element by dispatching the over/enter/move sequence.
 * PointerEvent is missing in Cohtml, so `pointer*` are dispatched as MouseEvents (the type string
 * is what delegated handlers key off).
 * `enter`/`leave` do not bubble.
 */
function hoverFn(sel: string, index: number): HoverResult {
  const nodes = document.querySelectorAll<HTMLElement>(sel);

  if (nodes.length == 0) {
    return { found: false, count: 0 };
  }

  const node = nodes[index];

  if (!node) {
    return { found: false, count: nodes.length };
  }

  // Alias the narrowed node so the hoisted fire() helper below closes over a non-null element.
  const el = node;
  const count = nodes.length;

  const rect = el.getBoundingClientRect();
  const cx = rect.x + rect.width / 2;
  const cy = rect.y + rect.height / 2;

  const base: MouseEventInit = {
    bubbles: true,
    cancelable: true,
    // oxlint-disable-next-line unicorn/prefer-global-this -- Browser page context; MouseEventInit.view wants the Window.
    view: window,
    clientX: cx,
    clientY: cy
  };

  const noBubble: MouseEventInit = { ...base, bubbles: false };

  const fired: string[] = [];

  fire('pointerover', base);
  fire('mouseover', base);
  fire('pointerenter', noBubble);
  fire('mouseenter', noBubble);
  fire('mousemove', base);

  return { found: true, count, x: cx, y: cy, fired };

  function fire(type: string, init: MouseEventInit): void {
    try {
      el.dispatchEvent(new MouseEvent(type, init));
      fired.push(type);
    } catch {
      /* Unsupported event type. */
    }
  }
}

/**
 * Presses a named key by dispatching real bubbling keydown+keyup DOM events.
 *
 * Cohtml's KeyboardEvent DERIVES `key` from the init `keyCode` and ignores the init `key`
 * (verified: keyCode 65 yields key "A", 27/13 yield ""), so we force `key` (and code/keyCode/which
 * when known) on every instance via `Object.defineProperty`; standard engines that ignore init
 * keyCode get the same forced values, so the target reads exactly the requested key either way.
 * We fire ONLY keydown and keyup and perform NO default action (no character insertion, no
 * deletion, no focus move, no scrolling): text entry is typeFn's job.
 */
function keyFn(args: KeyArgs): KeyResult {
  const { key, count, ctrl, shift, alt, meta, sel, index } = args;

  // Resolve the target. A selector focuses its index-th match and dispatches on it; without one,
  // dispatch on the focused element, else the body / document. Events bubble either way.
  let target: EventTarget;
  let via: 'selector' | 'activeElement' | 'document';
  let node: Element | null = null;
  let matches: number | null = null;

  if (sel == null) {
    const active = document.activeElement;

    if (active) {
      target = active;
      node = active;
      via = 'activeElement';
    } else {
      target = document.body || document;
      node = document.body;
      via = 'document';
    }
  } else {
    const nodes = document.querySelectorAll<HTMLElement>(sel);

    if (nodes.length == 0) {
      return { found: false, count: 0 };
    }

    const chosen = nodes[index];

    if (!chosen) {
      return { found: false, count: nodes.length };
    }

    try {
      chosen.focus();
    } catch {
      /* Not focusable; still dispatch on it. */
    }

    target = chosen;
    node = chosen;
    matches = nodes.length;
    via = 'selector';
  }

  // Legacy code/keyCode for the known key names; undefined leaves an unknown key with key-only
  // fields (never rejected).
  const map = codeFor(key);

  const init: KeyboardEventInit = {
    bubbles: true,
    cancelable: true,
    // oxlint-disable-next-line unicorn/prefer-global-this -- Browser page context; view wants Window.
    view: window,
    key,
    ctrlKey: ctrl,
    shiftKey: shift,
    altKey: alt,
    metaKey: meta
  };

  if (map) {
    init.code = map.code;
  }

  let defaultPrevented = false;
  let presses = 0;

  for (let i = 0; i < count; i++) {
    if (dispatch('keydown')) {
      defaultPrevented = true;
    }

    dispatch('keyup');
    presses++;
  }

  return {
    found: true,
    via,
    target: node ? describe(node) : 'document',
    matches,
    presses,
    defaultPrevented
  };

  // Dispatches one event of the given type; returns true only when a keydown was preventDefault-ed.
  function dispatch(type: string): boolean {
    try {
      const ev = new KeyboardEvent(type, init);

      // Force the fields regardless of engine derivation (see the function docblock).
      force(ev, 'key', key);

      if (map) {
        force(ev, 'code', map.code);
        force(ev, 'keyCode', map.keyCode);
        force(ev, 'which', map.keyCode);
      }

      const notPrevented = target.dispatchEvent(ev);

      return type == 'keydown' && !notPrevented;
    } catch {
      // No KeyboardEvent constructor in this engine (does not happen on Gameface).
      return false;
    }
  }

  function force(ev: KeyboardEvent, prop: string, value: string | number): void {
    try {
      Object.defineProperty(ev, prop, { get: () => value, configurable: true });
    } catch {
      /* Property not redefinable here; keep the constructed value. */
    }
  }

  // Maps a KeyboardEvent.key name to its `code` and legacy keyCode; returns undefined for names we
  // do not know, so they pass through with key-only fields.
  function codeFor(name: string): { code: string; keyCode: number } | undefined {
    const table: Record<string, { code: string; keyCode: number }> = {
      'Escape': { code: 'Escape', keyCode: 27 },
      'Enter': { code: 'Enter', keyCode: 13 },
      'Tab': { code: 'Tab', keyCode: 9 },
      ' ': { code: 'Space', keyCode: 32 },
      'Backspace': { code: 'Backspace', keyCode: 8 },
      'Delete': { code: 'Delete', keyCode: 46 },
      'Home': { code: 'Home', keyCode: 36 },
      'End': { code: 'End', keyCode: 35 },
      'PageUp': { code: 'PageUp', keyCode: 33 },
      'PageDown': { code: 'PageDown', keyCode: 34 },
      'ArrowUp': { code: 'ArrowUp', keyCode: 38 },
      'ArrowDown': { code: 'ArrowDown', keyCode: 40 },
      'ArrowLeft': { code: 'ArrowLeft', keyCode: 37 },
      'ArrowRight': { code: 'ArrowRight', keyCode: 39 }
    };

    const known = table[name];

    if (known) {
      return known;
    }

    // F1-F12: keyCode counts up from F1. The char-guarded branches below can never see an empty
    // string, so the `?? 0` fallbacks are unreachable and only satisfy codePointAt's return type.
    const F1_KEY_CODE = 112;
    const fn = /^F(?<num>[1-9]|1[0-2])$/u.exec(name);

    if (fn) {
      const num = Number(fn.groups?.num);

      return { code: `F${num}`, keyCode: F1_KEY_CODE + num - 1 };
    }

    // Single letter: code KeyX, keyCode is the uppercase char code (65-90).
    if (/^[a-zA-Z]$/u.test(name)) {
      const upper = name.toUpperCase();

      return { code: `Key${upper}`, keyCode: upper.codePointAt(0) ?? 0 };
    }

    // Single digit: code DigitX, keyCode 48-57.
    if (/^[0-9]$/u.test(name)) {
      return { code: `Digit${name}`, keyCode: name.codePointAt(0) ?? 0 };
    }

    return undefined;
  }

  // Compact <tag#id.firstclass> descriptor of the resolved element for the result line.
  function describe(el: Element): string {
    const tag = el.tagName ? el.tagName.toLowerCase() : 'node';
    const id = el.id ? `#${el.id}` : '';
    const classAttr = el.getAttribute('class');
    const cls = classAttr && classAttr.trim() ? `.${classAttr.trim().split(/\s+/u)[0]}` : '';

    return `<${tag}${id}${cls}>`;
  }
}

/**
 * Returns an element's viewport box, for clipping a screenshot to it.
 */
function rectFn(sel: string, index: number): RectResult {
  const nodes = document.querySelectorAll(sel);

  if (nodes.length == 0) {
    return { found: false, count: 0 };
  }

  const el = nodes[index];

  if (!el) {
    return { found: false, count: nodes.length };
  }

  const rect = el.getBoundingClientRect();

  return {
    found: true,
    count: nodes.length,
    x: rect.x,
    y: rect.y,
    width: rect.width,
    height: rect.height
  };
}
