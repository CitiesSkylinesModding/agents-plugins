/**
 * Tool implementations for the Gameface MCP server. Each maps to one or more
 * CDP commands via the shared CdpClient and returns an MCP CallToolResult.
 */
import type { CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import { CdpClient } from "./cdp";
import {
  type EvaluateResult,
  type RemoteObject,
  describeRemoteObject,
  errorText,
  formatException,
  text,
  toErrorResult,
  valToStr,
} from "./shared";

// --- Page-context functions ------------------------------------------------
// These run inside the game UI (never in this process); they are serialised
// with .toString() and injected into Runtime.evaluate. Keep them as plain,
// self-contained browser JS with no references to anything outside their body.

const collectDomFn = (sel: string, all: boolean, maxHtml: number) => {
  const matches = document.querySelectorAll(sel);
  const chosen = all ? Array.from(matches) : matches[0] ? [matches[0]] : [];
  const describe = (el: Element) => {
    const r = el.getBoundingClientRect();
    const attributes: Record<string, string> = {};
    for (const a of Array.from(el.attributes)) attributes[a.name] = a.value;
    let html = el.outerHTML || "";
    const truncated = html.length > maxHtml;
    if (truncated) html = html.slice(0, maxHtml);
    return {
      tagName: el.tagName ? el.tagName.toLowerCase() : null,
      id: el.id || null,
      classes: el.getAttribute("class") || null,
      rect: { x: r.x, y: r.y, width: r.width, height: r.height },
      attributes,
      outerHTML: html,
      truncated,
    };
  };
  return { count: matches.length, elements: chosen.map(describe) };
};

// Gameface ACCEPTS CDP Input.dispatchMouseEvent but does NOT route it into the
// Cohtml/React DOM event system (verified: handlers never fire). So we click by
// dispatching real, bubbling DOM events on the element, which React's delegated
// listeners pick up. Note: HTMLElement.click() does not exist in Cohtml either.
const clickFn = (sel: string, index: number) => {
  const nodes = document.querySelectorAll(sel);
  if (nodes.length === 0) return { found: false, count: 0, fired: [] as string[] };
  const el = nodes[index] as HTMLElement | undefined;
  if (!el) return { found: false, count: nodes.length, fired: [] as string[] };
  const r = el.getBoundingClientRect();
  const cx = r.x + r.width / 2;
  const cy = r.y + r.height / 2;
  const base = { bubbles: true, cancelable: true, view: window, button: 0, clientX: cx, clientY: cy };
  // eslint-disable-next-line @typescript-eslint/no-explicit-any -- serialised page code
  type EventCtor = new (type: string, init?: any) => Event;
  const Pointer: EventCtor = typeof PointerEvent === "function" ? PointerEvent : MouseEvent;
  const fired: string[] = [];
  const fire = (Ctor: EventCtor, type: string, extra?: object) => {
    try {
      el.dispatchEvent(new Ctor(type, Object.assign({}, base, extra)));
      fired.push(type);
    } catch {
      /* event type unsupported in this engine */
    }
  };
  fire(Pointer, "pointerdown", { pointerId: 1, isPrimary: true });
  fire(MouseEvent, "mousedown");
  fire(Pointer, "pointerup", { pointerId: 1, isPrimary: true });
  fire(MouseEvent, "mouseup");
  fire(MouseEvent, "click");
  return { found: true, count: nodes.length, x: cx, y: cy, fired };
};

// Returns true once the selector matches (and, if `visible`, has a non-zero box).
const waitCheckFn = (sel: string, visible: boolean) => {
  const el = document.querySelector(sel);
  if (!el) return false;
  if (!visible) return true;
  const r = el.getBoundingClientRect();
  return r.width > 0 && r.height > 0;
};

// Sets an input/textarea/contenteditable value so React's onChange fires. We use the
// native value setter (verified present in Cohtml) so React's value tracker notices.
// InputEvent is missing in Cohtml, so we dispatch a plain bubbling Event('input').
const fillFn = (sel: string, value: string) => {
  const el: any = document.querySelector(sel);
  if (!el) return { found: false };
  try {
    el.focus();
  } catch {
    /* not focusable */
  }
  const tag = (el.tagName || "").toLowerCase();
  if (el.isContentEditable) {
    el.textContent = value;
    el.dispatchEvent(new Event("input", { bubbles: true }));
    el.dispatchEvent(new Event("change", { bubbles: true }));
    return { found: true, mode: "contenteditable", value: el.textContent };
  }
  const proto: any = tag === "textarea" ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  const setter = (Object.getOwnPropertyDescriptor(proto, "value") || {}).set;
  if (setter) setter.call(el, value);
  else el.value = value;
  el.dispatchEvent(new Event("input", { bubbles: true }));
  el.dispatchEvent(new Event("change", { bubbles: true }));
  return { found: true, mode: tag || "input", value: el.value };
};

// Types text character by character, firing real KeyboardEvents (present in Cohtml)
// plus keeping the value in sync and dispatching input/change for React.
const typeFn = (sel: string, textToType: string) => {
  const el: any = document.querySelector(sel);
  if (!el) return { found: false, typed: 0 };
  try {
    el.focus();
  } catch {
    /* not focusable */
  }
  const tag = (el.tagName || "").toLowerCase();
  const isCE = !!el.isContentEditable;
  const proto: any = tag === "textarea" ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  const setter = (Object.getOwnPropertyDescriptor(proto, "value") || {}).set;
  const cur = () => (isCE ? el.textContent || "" : el.value || "");
  const setVal = (v: string) => {
    if (isCE) el.textContent = v;
    else if (setter) setter.call(el, v);
    else el.value = v;
  };
  let typed = 0;
  for (const ch of String(textToType)) {
    const opts: any = { bubbles: true, cancelable: true, key: ch, view: window };
    try {
      el.dispatchEvent(new KeyboardEvent("keydown", opts));
    } catch {
      /* no KeyboardEvent */
    }
    setVal(cur() + ch);
    el.dispatchEvent(new Event("input", { bubbles: true }));
    try {
      el.dispatchEvent(new KeyboardEvent("keyup", opts));
    } catch {
      /* no KeyboardEvent */
    }
    typed++;
  }
  el.dispatchEvent(new Event("change", { bubbles: true }));
  return { found: true, typed, value: cur() };
};

// Hovers an element by dispatching the over/enter/move sequence. PointerEvent is
// missing in Cohtml, so pointer* are dispatched as MouseEvents (the type string is
// what React keys off). enter/leave do not bubble.
const hoverFn = (sel: string) => {
  const el: any = document.querySelector(sel);
  if (!el) return { found: false };
  const r = el.getBoundingClientRect();
  const cx = r.x + r.width / 2;
  const cy = r.y + r.height / 2;
  const base: any = { bubbles: true, cancelable: true, view: window, clientX: cx, clientY: cy };
  const noBubble: any = Object.assign({}, base, { bubbles: false });
  const fired: string[] = [];
  const fire = (type: string, init: any) => {
    try {
      el.dispatchEvent(new MouseEvent(type, init));
      fired.push(type);
    } catch {
      /* unsupported event type */
    }
  };
  fire("pointerover", base);
  fire("mouseover", base);
  fire("pointerenter", noBubble);
  fire("mouseenter", noBubble);
  fire("mousemove", base);
  return { found: true, x: cx, y: cy, fired };
};

// Returns an element's viewport box, for clipping a screenshot to it.
const rectFn = (sel: string) => {
  const el = document.querySelector(sel);
  if (!el) return { found: false };
  const r = el.getBoundingClientRect();
  return { found: true, x: r.x, y: r.y, width: r.width, height: r.height };
};

function callPageFn(fn: (...args: never[]) => unknown, ...args: unknown[]): string {
  const serialisedArgs = args.map((a) => JSON.stringify(a)).join(", ");
  return `(${fn.toString()})(${serialisedArgs})`;
}

const POLL_INTERVAL_MS = 150;
const MAX_WAIT_MS = 60000;

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

// --- Tools ------------------------------------------------------------------

/** Reports reachability + page target + engine info. Never throws. */
export async function gameStatus(client: CdpClient): Promise<CallToolResult> {
  const { host, port } = client.config;
  try {
    const target = await client.discover();
    let browser: string | undefined;
    let protocol: string | undefined;
    try {
      const res = await fetch(`http://${host}:${port}/json/version`);
      if (res.ok) {
        const v = (await res.json()) as Record<string, string>;
        browser = v.Browser;
        protocol = v["Protocol-Version"];
      }
    } catch {
      /* version is best-effort */
    }
    return text(
      JSON.stringify(
        {
          reachable: true,
          endpoint: `http://${host}:${port}`,
          target: { id: target.id, url: target.url, title: target.title, wsUrl: target.wsUrl },
          browser,
          cdpProtocol: protocol,
        },
        null,
        2,
      ),
    );
  } catch (err) {
    return text(
      JSON.stringify(
        {
          reachable: false,
          endpoint: `http://${host}:${port}`,
          error: err instanceof Error ? err.message : String(err),
          hint:
            "Launch Cities: Skylines II with the Gameface debug port open, then retry. " +
            "Override host/port via CS2_GAMEFACE_HOST / CS2_GAMEFACE_PORT.",
        },
        null,
        2,
      ),
    );
  }
}

/** Evaluates a JS expression in the page and returns its value as JSON. */
export async function gameEval(
  client: CdpClient,
  expression: string,
  awaitPromise = false,
): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>("Runtime.evaluate", {
      expression,
      returnByValue: true,
      awaitPromise,
    });
    if (res.exceptionDetails) {
      return errorText(`Evaluation threw: ${formatException(res.exceptionDetails)}`);
    }
    const value = describeRemoteObject(res.result);
    return text(typeof value === "string" ? value : JSON.stringify(value, null, 2));
  } catch (err) {
    return toErrorResult(err);
  }
}

/**
 * Captures a screenshot of the game UI and returns it as an inline image. When a
 * selector is given, the capture is clipped to that element's bounding box.
 */
export async function gameScreenshot(
  client: CdpClient,
  format: "png" | "jpeg" = "png",
  quality?: number,
  selector?: string,
): Promise<CallToolResult> {
  try {
    await client.ensureDomain("Page");
    const params: Record<string, unknown> = { format };
    if (format === "jpeg") params.quality = quality ?? 80;
    if (selector) {
      const r = await client.call<EvaluateResult>("Runtime.evaluate", {
        expression: callPageFn(rectFn, selector),
        returnByValue: true,
      });
      const rect = r.result.value as
        | { found: true; x: number; y: number; width: number; height: number }
        | { found: false };
      if (!rect?.found) {
        return errorText(`No element matched ${JSON.stringify(selector)} for game_screenshot.`);
      }
      if (!(rect.width > 0 && rect.height > 0)) {
        return errorText(`Element ${JSON.stringify(selector)} has a zero-size box; nothing to capture.`);
      }
      params.clip = { x: rect.x, y: rect.y, width: rect.width, height: rect.height, scale: 1 };
    }
    const res = await client.call<{ data?: string }>("Page.captureScreenshot", params);
    if (!res?.data) return errorText("Page.captureScreenshot returned no image data.");
    return {
      content: [
        { type: "image", data: res.data, mimeType: format === "jpeg" ? "image/jpeg" : "image/png" },
      ],
    };
  } catch (err) {
    return toErrorResult(err);
  }
}

/** Returns DOM info (tag, classes, attributes, rect, outerHTML) for a selector. */
export async function gameDom(
  client: CdpClient,
  selector: string,
  all = false,
  maxHtml = 4000,
): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>("Runtime.evaluate", {
      expression: callPageFn(collectDomFn, selector, all, maxHtml),
      returnByValue: true,
    });
    if (res.exceptionDetails) {
      return errorText(`DOM query failed: ${formatException(res.exceptionDetails)}`);
    }
    const value = res.result.value as { count: number; elements: unknown[] };
    if (!value || value.count === 0) {
      return text(JSON.stringify({ selector, count: 0, elements: [] }, null, 2));
    }
    return text(JSON.stringify({ selector, ...value }, null, 2));
  } catch (err) {
    return toErrorResult(err);
  }
}

/**
 * Clicks the element matching `selector` (the `index`-th match) by dispatching a
 * realistic bubbling pointer/mouse/click sequence in the page. We do NOT use CDP
 * Input.dispatchMouseEvent: Gameface accepts it but never delivers it to the UI.
 */
export async function gameClick(
  client: CdpClient,
  selector: string,
  index = 0,
): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>("Runtime.evaluate", {
      expression: callPageFn(clickFn, selector, index),
      returnByValue: true,
    });
    if (res.exceptionDetails) {
      return errorText(`Click failed: ${formatException(res.exceptionDetails)}`);
    }
    const info = res.result.value as
      | { found: true; count: number; x: number; y: number; fired: string[] }
      | { found: false; count: number; fired: string[] };
    if (!info?.found) {
      return errorText(
        `No element to click for selector ${JSON.stringify(selector)} at index ${index} ` +
          `(matches found: ${info?.count ?? 0}).`,
      );
    }
    return text(
      `Clicked ${JSON.stringify(selector)} [index ${index}] at ` +
        `(${info.x.toFixed(0)}, ${info.y.toFixed(0)}). Dispatched: ${info.fired.join(", ")}. ` +
        `Matches: ${info.count}.`,
    );
  } catch (err) {
    return toErrorResult(err);
  }
}

/**
 * Waits (server-side polling) until a selector matches or a JS predicate is truthy.
 * Provide exactly one of `selector` / `predicate`.
 */
export async function gameWait(
  client: CdpClient,
  selector?: string,
  predicate?: string,
  timeoutMs = 8000,
  visible = false,
): Promise<CallToolResult> {
  if (!selector && !predicate) {
    return errorText("game_wait needs either 'selector' or 'predicate'.");
  }
  const budget = Math.min(Math.max(timeoutMs, 0), MAX_WAIT_MS);
  const deadline = Date.now() + budget;
  const start = Date.now();
  const expression = selector ? callPageFn(waitCheckFn, selector, visible) : `Boolean(${predicate})`;
  let lastError: string | undefined;
  try {
    for (;;) {
      const res = await client.call<EvaluateResult>("Runtime.evaluate", {
        expression,
        returnByValue: true,
      });
      if (res.exceptionDetails) {
        lastError = formatException(res.exceptionDetails);
      } else if (res.result.value) {
        const what = selector ? `selector ${JSON.stringify(selector)}` : "predicate";
        return text(`Condition met (${what}) after ${Date.now() - start}ms.`);
      } else {
        lastError = undefined;
      }
      if (Date.now() >= deadline) {
        const what = selector ? `selector ${JSON.stringify(selector)}` : "predicate";
        return errorText(
          `Timed out after ${budget}ms waiting for ${what}.` +
            (lastError ? ` Last predicate error: ${lastError}` : ""),
        );
      }
      await sleep(POLL_INTERVAL_MS);
    }
  } catch (err) {
    return toErrorResult(err);
  }
}

/** Sets the value of an input/textarea/contenteditable (React-aware). */
export async function gameFill(
  client: CdpClient,
  selector: string,
  value: string,
): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>("Runtime.evaluate", {
      expression: callPageFn(fillFn, selector, value),
      returnByValue: true,
    });
    if (res.exceptionDetails) {
      return errorText(`Fill failed: ${formatException(res.exceptionDetails)}`);
    }
    const info = res.result.value as { found: boolean; mode?: string; value?: string };
    if (!info?.found) {
      return errorText(`No element matched ${JSON.stringify(selector)} for game_fill.`);
    }
    return text(`Filled ${JSON.stringify(selector)} (${info.mode}). Value is now ${JSON.stringify(info.value)}.`);
  } catch (err) {
    return toErrorResult(err);
  }
}

/** Types text into an element key by key (real KeyboardEvents + value sync). */
export async function gameType(
  client: CdpClient,
  selector: string,
  textToType: string,
): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>("Runtime.evaluate", {
      expression: callPageFn(typeFn, selector, textToType),
      returnByValue: true,
    });
    if (res.exceptionDetails) {
      return errorText(`Type failed: ${formatException(res.exceptionDetails)}`);
    }
    const info = res.result.value as { found: boolean; typed?: number; value?: string };
    if (!info?.found) {
      return errorText(`No element matched ${JSON.stringify(selector)} for game_type.`);
    }
    return text(
      `Typed ${info.typed} char(s) into ${JSON.stringify(selector)}. ` +
        `Value is now ${JSON.stringify(info.value)}.`,
    );
  } catch (err) {
    return toErrorResult(err);
  }
}

/** Hovers an element by dispatching the over/enter/move event sequence. */
export async function gameHover(client: CdpClient, selector: string): Promise<CallToolResult> {
  try {
    const res = await client.call<EvaluateResult>("Runtime.evaluate", {
      expression: callPageFn(hoverFn, selector),
      returnByValue: true,
    });
    if (res.exceptionDetails) {
      return errorText(`Hover failed: ${formatException(res.exceptionDetails)}`);
    }
    const info = res.result.value as { found: boolean; x?: number; y?: number; fired?: string[] };
    if (!info?.found) {
      return errorText(`No element matched ${JSON.stringify(selector)} for game_hover.`);
    }
    return text(
      `Hovered ${JSON.stringify(selector)} at (${info.x!.toFixed(0)}, ${info.y!.toFixed(0)}). ` +
        `Dispatched: ${info.fired!.join(", ")}.`,
    );
  } catch (err) {
    return toErrorResult(err);
  }
}

/** One captured console/log/exception line. */
export interface ConsoleEntry {
  ts: number;
  kind: string;
  level: string;
  text: string;
}

/**
 * Buffers console/log/exception events from the game UI into a ring buffer.
 * Subscribes to CDP events and (re)enables Runtime + Log on every connection.
 */
export class ConsoleBuffer {
  private readonly entries: ConsoleEntry[] = [];

  constructor(
    client: CdpClient,
    private readonly max = 500,
  ) {
    client.onConnect(async (conn) => {
      await conn.ensureDomain("Runtime");
      await conn.ensureDomain("Log");
    });
    client.onEvent((method, params) => this.handle(method, params as Record<string, unknown>));
  }

  private push(entry: ConsoleEntry): void {
    this.entries.push(entry);
    if (this.entries.length > this.max) {
      this.entries.splice(0, this.entries.length - this.max);
    }
  }

  private handle(method: string, params: Record<string, unknown>): void {
    if (method === "Runtime.consoleAPICalled") {
      const args = ((params.args as RemoteObject[]) ?? []).map((a) => valToStr(describeRemoteObject(a)));
      this.push({
        ts: (params.timestamp as number) ?? 0,
        kind: "console",
        level: (params.type as string) ?? "log",
        text: args.join(" "),
      });
    } else if (method === "Log.entryAdded") {
      const e = (params.entry as Record<string, unknown>) ?? {};
      this.push({
        ts: (e.timestamp as number) ?? 0,
        kind: (e.source as string) ?? "log",
        level: (e.level as string) ?? "info",
        text: (e.text as string) ?? "",
      });
    } else if (method === "Runtime.exceptionThrown") {
      this.push({
        ts: (params.timestamp as number) ?? 0,
        kind: "exception",
        level: "error",
        text: formatException(params.exceptionDetails as EvaluateResult["exceptionDetails"]),
      });
    }
  }

  read(limit: number, level?: string, clear?: boolean): ConsoleEntry[] {
    const filtered = level ? this.entries.filter((e) => e.level === level) : this.entries;
    const out = filtered.slice(-limit);
    if (clear) this.entries.length = 0;
    return out;
  }
}

/** Returns recent console/log/exception lines captured from the game UI. */
export async function gameConsole(
  client: CdpClient,
  buffer: ConsoleBuffer,
  limit = 50,
  level?: string,
  clear = false,
): Promise<CallToolResult> {
  try {
    // Ensure a connection exists so Runtime/Log are enabled and capture is running.
    await client.connection();
  } catch (err) {
    return toErrorResult(err);
  }
  const entries = buffer.read(limit, level, clear);
  if (entries.length === 0) {
    return text(
      "No console entries captured yet. Capture begins once the server connects to the game; " +
        "trigger some UI activity (or a game_eval console.log) and retry.",
    );
  }
  return text(entries.map((e) => `[${e.level}] (${e.kind}) ${e.text}`).join("\n"));
}
