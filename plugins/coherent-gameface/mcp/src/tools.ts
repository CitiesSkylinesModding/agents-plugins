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
  type RemoteObject,
  describeRemoteObject,
  errorText,
  formatException,
  text,
  toErrorResult,
  valToStr
} from './shared';

// Page-context functions.
// These run inside the Gameface UI (never in this process); they are serialised with
// .toString() and injected into Runtime.evaluate. Keep them as plain, self-contained browser
// JS with no references to anything outside their body. Type annotations are fine: the build
// erases them before serialization.

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

type FillResult = { found: true; mode: string; value: string } | { found: false };

type TypeResult = { found: true; typed: number; value: string } | { found: false; typed: 0 };

type HoverResult = { found: true; x: number; y: number; fired: string[] } | { found: false };

type RectResult =
  | { found: true; x: number; y: number; width: number; height: number }
  | { found: false };

interface FindMatch {
  tagName: string | null;
  id: string | null;
  classes: string | null;
  rect: { x: number; y: number; width: number; height: number };
  text: string;
  truncated: boolean;
  // Present only when tag=true: a ready-to-use selector targeting this match's data-gf-find handle.
  selector?: string;
}

interface FindResult {
  // Raw textContent matches before deepest pruning; unprunedTotal vs. total shows how many ancestor
  // matches the deepest filter removed.
  unprunedTotal: number;
  // Matches after deepest pruning, before the limit truncates; total vs returned shows the limit
  // truncating.
  total: number;
  returned: number;
  tagged: boolean;
  elements: FindMatch[];
}

// Input to findFn, passed as one object so the page function stays under the params ceiling.
interface FindArgs {
  sel: string;
  needle: string;
  mode: string;
  caseSensitive: boolean;
  deepest: boolean;
  tag: boolean;
  limit: number;
}

const collectDomFn = (sel: string, all: boolean, maxHtml: number): CollectDomResult => {
  const matches = document.querySelectorAll(sel);
  const first = matches.item(0);
  const chosen = all ? Array.from(matches) : first ? [first] : [];

  const describe = (el: Element): DomElementInfo => {
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
  };

  return { count: matches.length, elements: chosen.map(el => describe(el)) };
};

// Scans querySelectorAll matches and filters on trimmed textContent, the only text search Cohtml
// affords (no XPath, TreeWalker, or innerText). Returns lean, actionable info per match and, when
// tag=true, stamps handles so the discovery result feeds straight into the input tools.
const findFn = (args: FindArgs): FindResult | { error: string } => {
  const { sel, needle, mode, caseSensitive, deepest, tag, limit } = args;

  // Cap on the returned text snippet, kept inline because page functions are self-contained.
  const SNIPPET_MAX = 100;

  // Precompile the matcher once. Case insensitivity lowercases both sides for equals/contains and
  // adds the 'i' flag for regex.
  const target = caseSensitive ? needle : needle.toLowerCase();

  let regex: RegExp | undefined;

  if (mode == 'regex') {
    try {
      regex = new RegExp(needle, caseSensitive ? '' : 'i');
    } catch (error) {
      return { error: `Invalid regex: ${error instanceof Error ? error.message : String(error)}` };
    }
  }

  const matches = (raw: string): boolean => {
    if (mode == 'regex') {
      return regex != null && regex.test(raw);
    }

    const hay = caseSensitive ? raw : raw.toLowerCase();

    if (mode == 'equals') {
      return hay == target;
    }

    // 'contains' is the default mode.
    return hay.includes(target);
  };

  const found: Element[] = [];

  for (const el of Array.from(document.querySelectorAll(sel))) {
    if (matches((el.textContent || '').trim())) {
      found.push(el);
    }
  }

  // Deepest keeps only the innermost matches: an element's textContent includes its descendants',
  // so an ancestor matches whenever a descendant does. Drop any match that contains another match.
  const kept = deepest
    ? found.filter(el => !found.some(other => other != el && el.contains(other)))
    : found;

  const chosen = kept.slice(0, limit);

  if (tag) {
    // Clear-then-retag: strip every handle from a previous find first, so its handles die here.
    // Cohtml exposes setAttribute/removeAttribute but not the dataset DOMStringMap, so use those.
    for (const stale of Array.from(document.querySelectorAll('[data-gf-find]'))) {
      stale.removeAttribute('data-gf-find');
    }

    for (const [i, el] of chosen.entries()) {
      el.setAttribute('data-gf-find', String(i + 1));
    }
  }

  const describe = (el: Element, i: number): FindMatch => {
    const rect = el.getBoundingClientRect();
    const classAttr = el.getAttribute('class');
    const raw = (el.textContent || '').trim();
    const truncated = raw.length > SNIPPET_MAX;

    const info: FindMatch = {
      tagName: el.tagName ? el.tagName.toLowerCase() : null,
      id: el.id || null,
      classes: classAttr != null && classAttr.length > 0 ? classAttr : null,
      rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height },
      text: truncated ? raw.slice(0, SNIPPET_MAX) : raw,
      truncated
    };

    if (tag) {
      info.selector = `[data-gf-find="${i + 1}"]`;
    }

    return info;
  };

  return {
    unprunedTotal: found.length,
    total: kept.length,
    returned: chosen.length,
    tagged: tag,
    elements: chosen.map((el, i) => describe(el, i))
  };
};

// Gameface ACCEPTS CDP Input.dispatchMouseEvent but does NOT route it into the Cohtml/React
// DOM event system (verified: handlers never fire). So we click by dispatching real, bubbling
// DOM events on the element, which React's delegated listeners pick up. Note:
// HTMLElement.click() does not exist in Cohtml either.
const clickFn = (sel: string, index: number): ClickResult => {
  const nodes = document.querySelectorAll(sel);

  if (nodes.length == 0) {
    return { found: false, count: 0, fired: [] };
  }

  const el = nodes[index] as HTMLElement | undefined;

  if (!el) {
    return { found: false, count: nodes.length, fired: [] };
  }

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

  // PointerEvent does not exist in Cohtml; dispatch pointer* as MouseEvents (React keys off
  // the event type string, not the constructor).
  const Pointer: EventCtor = typeof PointerEvent == 'function' ? PointerEvent : MouseEvent;
  const fired: string[] = [];

  const fire = (Ctor: EventCtor, type: string, extra?: PointerEventInit): void => {
    try {
      el.dispatchEvent(new Ctor(type, { ...base, ...extra }));
      fired.push(type);
    } catch {
      /* Event type unsupported in this engine. */
    }
  };

  fire(Pointer, 'pointerdown', { pointerId: 1, isPrimary: true });
  fire(MouseEvent, 'mousedown');
  fire(Pointer, 'pointerup', { pointerId: 1, isPrimary: true });
  fire(MouseEvent, 'mouseup');
  fire(MouseEvent, 'click');

  return { found: true, count: nodes.length, x: cx, y: cy, fired };
};

// Returns true once the selector matches (and, if `visible`, has a non-zero box).
const waitCheckFn = (sel: string, visible: boolean): boolean => {
  const el = document.querySelector(sel);

  if (!el) {
    return false;
  }

  if (!visible) {
    return true;
  }

  const rect = el.getBoundingClientRect();

  return rect.width > 0 && rect.height > 0;
};

// Sets an input/textarea/contenteditable value so React's onChange fires. We use the native
// value setter (verified present in Cohtml) so React's value tracker notices. InputEvent is
// missing in Cohtml, so we dispatch a plain bubbling Event('input').
const fillFn = (sel: string, value: string): FillResult => {
  const el = document.querySelector<HTMLElement>(sel);

  if (!el) {
    return { found: false };
  }

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

    return { found: true, mode: 'contenteditable', value: el.textContent ?? '' };
  }

  const field = el as HTMLInputElement | HTMLTextAreaElement;
  const proto = tag == 'textarea' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  // oxlint-disable-next-line typescript/unbound-method -- Deliberately unbound: invoked with .call(el) so React's value tracker sees the native setter run.
  const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;

  if (setter) {
    setter.call(el, value);
  } else {
    field.value = value;
  }

  el.dispatchEvent(new Event('input', { bubbles: true }));
  el.dispatchEvent(new Event('change', { bubbles: true }));

  return { found: true, mode: tag || 'input', value: field.value };
};

// Types text character by character, firing real KeyboardEvents (present in Cohtml) plus
// keeping the value in sync and dispatching input/change for React.
const typeFn = (sel: string, textToType: string): TypeResult => {
  const el = document.querySelector<HTMLElement>(sel);

  if (!el) {
    return { found: false, typed: 0 };
  }

  try {
    el.focus();
  } catch {
    /* Not focusable. */
  }

  const tag = (el.tagName || '').toLowerCase();
  const editable = el.isContentEditable;
  const field = el as HTMLInputElement | HTMLTextAreaElement;
  const proto = tag == 'textarea' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  // oxlint-disable-next-line typescript/unbound-method -- Deliberately unbound: invoked with .call(el) so React's value tracker sees the native setter run.
  const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;

  const current = (): string => {
    if (editable) {
      return el.textContent ?? '';
    }

    return field.value || '';
  };

  const setValue = (next: string): void => {
    if (editable) {
      el.textContent = next;
    } else if (setter) {
      setter.call(el, next);
    } else {
      field.value = next;
    }
  };

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

  return { found: true, typed, value: current() };
};

// Hovers an element by dispatching the over/enter/move sequence. PointerEvent is missing in
// Cohtml, so `pointer*` are dispatched as MouseEvents (the type string is what React keys off).
// enter/leave do not bubble.
const hoverFn = (sel: string): HoverResult => {
  const el = document.querySelector<HTMLElement>(sel);

  if (!el) {
    return { found: false };
  }

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

  const fire = (type: string, init: MouseEventInit): void => {
    try {
      el.dispatchEvent(new MouseEvent(type, init));
      fired.push(type);
    } catch {
      /* Unsupported event type. */
    }
  };

  fire('pointerover', base);
  fire('mouseover', base);
  fire('pointerenter', noBubble);
  fire('mouseenter', noBubble);
  fire('mousemove', base);

  return { found: true, x: cx, y: cy, fired };
};

// Returns an element's viewport box, for clipping a screenshot to it.
const rectFn = (sel: string): RectResult => {
  const el = document.querySelector(sel);

  if (!el) {
    return { found: false };
  }

  const rect = el.getBoundingClientRect();

  return { found: true, x: rect.x, y: rect.y, width: rect.width, height: rect.height };
};

/**
 * Serializes a page-context function and its JSON-safe args into one self-invoking expression
 * for Runtime.evaluate.
 */
function callPageFn(fn: (...args: never[]) => unknown, ...args: unknown[]): string {
  const serialisedArgs = args.map(arg => JSON.stringify(arg)).join(', ');

  return `(${fn.toString()})(${serialisedArgs})`;
}

// Server-side polling interval for game_wait.
const POLL_INTERVAL_MS = 150;

// Hard ceiling on game_wait budgets, so a huge timeoutMs cannot hang a tool call for minutes.
const MAX_WAIT_MS = 60_000;

const DEFAULT_WAIT_TIMEOUT_MS = 8000;
const DEFAULT_JPEG_QUALITY = 80;
const DEFAULT_CONSOLE_LIMIT = 50;
const DEFAULT_FIND_LIMIT = 20;

/**
 * Reports reachability + page target + engine info. Never throws.
 */
export async function gameStatus(client: CdpClient): Promise<CallToolResult> {
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

    // noinspection HttpUrlsUsage
    return text(
      JSON.stringify(
        {
          reachable: true,
          endpoint: `http://${host}:${port}`,
          target: { id: target.id, url: target.url, title: target.title, wsUrl: target.wsUrl },
          browser,
          cdpProtocol: protocol
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
 * Captures a screenshot of the Gameface UI and returns it as an inline image.
 * When a selector is given, the capture is clipped to that element's bounding box.
 */
export async function gameScreenshot(
  client: CdpClient,
  format: 'png' | 'jpeg' = 'png',
  quality?: number,
  selector?: string
): Promise<CallToolResult> {
  try {
    await client.ensureDomain('Page');

    const params: Record<string, unknown> = { format };

    if (format == 'jpeg') {
      params.quality = quality ?? DEFAULT_JPEG_QUALITY;
    }

    if (selector) {
      const rectRes = await client.call<EvaluateResult>('Runtime.evaluate', {
        expression: callPageFn(rectFn, selector),
        returnByValue: true
      });
      const rect = rectRes.result.value as RectResult | undefined;

      if (!rect?.found) {
        return errorText(`No element matched ${JSON.stringify(selector)} for game_screenshot.`);
      }

      if (!(rect.width > 0 && rect.height > 0)) {
        return errorText(
          `Element ${JSON.stringify(selector)} has a zero-size box; nothing to capture.`
        );
      }

      params.clip = { x: rect.x, y: rect.y, width: rect.width, height: rect.height, scale: 1 };
    }

    const res = await client.call<{ data?: string }>('Page.captureScreenshot', params);

    if (!res?.data) {
      return errorText('Page.captureScreenshot returned no image data.');
    }

    return {
      content: [
        { type: 'image', data: res.data, mimeType: format == 'jpeg' ? 'image/jpeg' : 'image/png' }
      ]
    };
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
 * Options for gameFind.
 */
export interface GameFindOptions {
  readonly text: string;
  readonly match?: 'equals' | 'contains' | 'regex' | undefined;
  readonly caseSensitive?: boolean | undefined;
  readonly selector?: string | undefined;
  readonly deepest?: boolean | undefined;
  readonly tag?: boolean | undefined;
  readonly limit?: number | undefined;
}

/**
 * Finds elements by their trimmed textContent (equals / contains / regex) and returns lean,
 * actionable info per match, with the total count, so truncation is visible.
 * With `tag=true`, stamps matches with `data-gf-find` handles and returns ready-to-use selectors,
 * solving the discovery-to-action handoff when no unique selector can be written.
 */
export async function gameFind(
  client: CdpClient,
  options: GameFindOptions
): Promise<CallToolResult> {
  const {
    text: needle,
    match = 'contains',
    caseSensitive = false,
    selector = '*',
    deepest = true,
    tag = false,
    limit = DEFAULT_FIND_LIMIT
  } = options;

  try {
    const res = await client.call<EvaluateResult>('Runtime.evaluate', {
      expression: callPageFn(findFn, {
        sel: selector,
        needle,
        mode: match,
        caseSensitive,
        deepest,
        tag,
        limit
      }),
      returnByValue: true
    });

    if (res.exceptionDetails) {
      return errorText(`Find failed: ${formatException(res.exceptionDetails)}`);
    }

    const value = res.result.value as FindResult | { error: string } | undefined;

    if (!value) {
      return errorText(`game_find returned no result for selector ${JSON.stringify(selector)}.`);
    }

    if ('error' in value) {
      return errorText(`game_find: ${value.error}`);
    }

    return text(JSON.stringify({ selector, match, ...value }, null, 2));
  } catch (error) {
    return toErrorResult(error);
  }
}

/**
 * Clicks the element matching `selector` (the `index`-th match) by dispatching a realistic
 * bubbling pointer/mouse/click sequence in the page.
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
 * Options for gameWait. Provide exactly one of `selector` / `predicate`.
 */
export interface GameWaitOptions {
  readonly selector?: string | undefined;
  readonly predicate?: string | undefined;
  readonly timeoutMs?: number | undefined;
  readonly visible?: boolean | undefined;
}

/**
 * Waits (server-side polling) until a selector matches or a JS predicate is truthy.
 */
export async function gameWait(
  client: CdpClient,
  options: GameWaitOptions
): Promise<CallToolResult> {
  const { selector, predicate, timeoutMs = DEFAULT_WAIT_TIMEOUT_MS, visible = false } = options;

  if (!selector && !predicate) {
    return errorText("game_wait needs either 'selector' or 'predicate'.");
  }

  const budget = Math.min(Math.max(timeoutMs, 0), MAX_WAIT_MS);
  const deadline = Date.now() + budget;
  const start = Date.now();
  const expression = selector
    ? callPageFn(waitCheckFn, selector, visible)
    : `Boolean(${predicate ?? ''})`;

  // Remember the last predicate error so a predicate that consistently throws (e.g., a typo)
  // surfaces in the timeout message instead of failing silently on every poll.
  let lastError: string | undefined;

  try {
    for (;;) {
      const res = await client.call<EvaluateResult>('Runtime.evaluate', {
        expression,
        returnByValue: true
      });

      if (res.exceptionDetails) {
        lastError = formatException(res.exceptionDetails);
      } else if (res.result.value) {
        const what = selector ? `selector ${JSON.stringify(selector)}` : 'predicate';

        return text(`Condition met (${what}) after ${Date.now() - start}ms.`);
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
  } catch (error) {
    return toErrorResult(error);
  }
}

/**
 * Sets the value of an input/textarea/contenteditable (React-aware).
 */
export async function gameFill(
  client: CdpClient,
  selector: string,
  value: string
): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>('Runtime.evaluate', {
      expression: callPageFn(fillFn, selector, value),
      returnByValue: true
    });

    if (res.exceptionDetails) {
      return errorText(`Fill failed: ${formatException(res.exceptionDetails)}`);
    }

    const info = res.result.value as FillResult | undefined;

    if (!info?.found) {
      return errorText(`No element matched ${JSON.stringify(selector)} for game_fill.`);
    }

    return text(oneLine`
      Filled ${JSON.stringify(selector)} (${info.mode}).
      Value is now ${JSON.stringify(info.value)}.
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
  textToType: string
): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>('Runtime.evaluate', {
      expression: callPageFn(typeFn, selector, textToType),
      returnByValue: true
    });

    if (res.exceptionDetails) {
      return errorText(`Type failed: ${formatException(res.exceptionDetails)}`);
    }

    const info = res.result.value as TypeResult | undefined;

    if (!info?.found) {
      return errorText(`No element matched ${JSON.stringify(selector)} for game_type.`);
    }

    return text(oneLine`
      Typed ${info.typed} char(s) into ${JSON.stringify(selector)}.
      Value is now ${JSON.stringify(info.value)}.
    `);
  } catch (error) {
    return toErrorResult(error);
  }
}

/**
 * Hovers an element by dispatching the over/enter/move event sequence.
 */
export async function gameHover(client: CdpClient, selector: string): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>('Runtime.evaluate', {
      expression: callPageFn(hoverFn, selector),
      returnByValue: true
    });

    if (res.exceptionDetails) {
      return errorText(`Hover failed: ${formatException(res.exceptionDetails)}`);
    }

    const info = res.result.value as HoverResult | undefined;

    if (!info?.found) {
      return errorText(`No element matched ${JSON.stringify(selector)} for game_hover.`);
    }

    return text(oneLine`
      Hovered ${JSON.stringify(selector)} at (${info.x.toFixed(0)}, ${info.y.toFixed(0)}).
      Dispatched: ${info.fired.join(', ')}.
    `);
  } catch (error) {
    return toErrorResult(error);
  }
}

/**
 * One captured console/log/exception line.
 */
export interface ConsoleEntry {
  readonly ts: number;
  readonly kind: string;
  readonly level: string;
  readonly text: string;
}

/**
 * Buffers console/log/exception events from the Gameface UI into a ring buffer.
 * Subscribes to CDP events and (re)enables `Runtime` and `Log` on every connection.
 */
export class ConsoleBuffer {
  private readonly entries: ConsoleEntry[] = [];
  private readonly max: number;

  public constructor(client: CdpClient, max = 500) {
    this.max = max;

    client.onConnect(async conn => {
      await conn.ensureDomain('Runtime');
      await conn.ensureDomain('Log');
    });

    client.onEvent((method, params) => {
      this.handle(method, params as Record<string, unknown>);
    });
  }

  public read(limit: number, level?: string, clear?: boolean): ConsoleEntry[] {
    const filtered = level ? this.entries.filter(entry => entry.level == level) : this.entries;

    // Keep the newest entries when the limit truncates.
    const out = filtered.slice(-limit);

    if (clear) {
      this.entries.length = 0;
    }

    return out;
  }

  private push(entry: ConsoleEntry): void {
    this.entries.push(entry);

    // Ring-buffer behavior: drop the oldest entries beyond the cap.
    if (this.entries.length > this.max) {
      this.entries.splice(0, this.entries.length - this.max);
    }
  }

  private handle(method: string, params: Record<string, unknown>): void {
    if (method == 'Runtime.consoleAPICalled') {
      const args = ((params.args as RemoteObject[]) ?? []).map(arg =>
        valToStr(describeRemoteObject(arg))
      );

      this.push({
        ts: (params.timestamp as number) ?? 0,
        kind: 'console',
        level: (params.type as string) ?? 'log',
        text: args.join(' ')
      });
    } else if (method == 'Log.entryAdded') {
      const entry = (params.entry as Record<string, unknown>) ?? {};

      this.push({
        ts: (entry.timestamp as number) ?? 0,
        kind: (entry.source as string) ?? 'log',
        level: (entry.level as string) ?? 'info',
        text: (entry.text as string) ?? ''
      });
    } else if (method == 'Runtime.exceptionThrown') {
      this.push({
        ts: (params.timestamp as number) ?? 0,
        kind: 'exception',
        level: 'error',
        text: formatException(params.exceptionDetails as EvaluateResult['exceptionDetails'])
      });
    }
  }
}

/**
 * Options for gameConsole.
 */
export interface GameConsoleOptions {
  readonly limit?: number | undefined;
  readonly level?: string | undefined;
  readonly clear?: boolean | undefined;
}

/**
 * Returns recent console/log/exception lines captured from the Gameface UI.
 */
export async function gameConsole(
  client: CdpClient,
  buffer: ConsoleBuffer,
  options: GameConsoleOptions
): Promise<CallToolResult> {
  const { limit = DEFAULT_CONSOLE_LIMIT, level, clear = false } = options;

  try {
    // Ensure a connection exists so Runtime/Log are enabled and capture is running.
    await client.connection();
  } catch (error) {
    return toErrorResult(error);
  }

  const entries = buffer.read(limit, level, clear);

  if (entries.length == 0) {
    return text(oneLine`
      No console entries captured yet.
      Capture begins once the server connects to the application;
      trigger some UI activity (or a game_eval console.log) and retry.
    `);
  }

  return text(entries.map(entry => `[${entry.level}] (${entry.kind}) ${entry.text}`).join('\n'));
}
