/**
 * Gameface MCP server entry point.
 *
 * Exposes tools for driving a Coherent Gameface application UI over a direct CDP WebSocket.
 * Runs under Node 22.4+ (global WebSocket and fetch are stable from that version).
 * All diagnostics go to stderr; stdout is reserved for the MCP JSON-RPC stream.
 */

import { readFileSync } from 'node:fs';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { oneLine } from 'common-tags';
import { z } from 'zod';
import { CdpClient } from './cdp';
import { loadConfig } from './config';
import { DebuggerSession } from './debugger';
import {
  ConsoleBuffer,
  ReloadTracker,
  gameClick,
  gameConsole,
  gameDom,
  gameEval,
  gameFill,
  gameFind,
  gameHover,
  gameKey,
  gameScreenshot,
  gameStatus,
  gameType,
  gameWait
} from './tools';

// Read at runtime rather than baked in at build time, so a version bump needs no rebuild.
// Both execution modes sit one level below package.json (dist/server.mjs and src/server.ts).
// A static JSON import is avoided on purpose: `bun build` either inlines it (baking the version)
// or, with --external, strips the `with { type: 'json' }` attribute so Node throws; the dynamic
// form warns on Node < 22.12 (the floor is 22.4). readFileSync is silent on every supported Node.
const { version: VERSION } = JSON.parse(
  // oxlint-disable-next-line node/no-sync -- One-shot startup read, nothing is serving yet.
  readFileSync(new URL('../package.json', import.meta.url), 'utf8')
) as { version: string };

async function main(): Promise<void> {
  const config = loadConfig();
  const client = new CdpClient(config);

  // Constructing these registers their CDP connect/event listeners; they must exist before the
  // first connection, so reload tracking, console capture, and breakpoint re-binding are armed
  // from the start. The tracker comes first: the others subscribe to its reload detections.
  const reloads = new ReloadTracker(client);
  const consoleBuffer = new ConsoleBuffer(client, reloads);
  const debug = new DebuggerSession(client, reloads);

  const server = new McpServer({ name: 'gameface-devtools-mcp', version: VERSION });

  server.registerTool(
    'game_status',
    {
      title: `Gameface UI status`,
      description: oneLine`
        Check whether the Gameface UI debug endpoint is reachable and report the live page target,
        engine info, and view-reload tracking (count, last reload time, context id).
        Calling it arms reload tracking and returns the baseline count for game_wait's sinceReloads.
        Run this first when other game_* tools fail.
      `
    },
    () => gameStatus(client, reloads)
  );

  server.registerTool(
    'game_eval',
    {
      title: `Evaluate JS in the Gameface UI`,
      description: oneLine`
        Evaluate a JavaScript expression in the running Gameface UI (CDP Runtime.evaluate,
        returnByValue) and return the resulting value as JSON.
        Use document.querySelector and friends to read the live DOM, inspect state, or call UI APIs.
      `,
      inputSchema: {
        expression: z.string().describe(`JavaScript expression to evaluate in the page context`),
        awaitPromise: z
          .boolean()
          .optional()
          .describe(`If the expression returns a Promise, await it before returning`)
      }
    },
    ({ expression, awaitPromise }) => gameEval(client, expression, awaitPromise)
  );

  server.registerTool(
    'game_screenshot',
    {
      title: `Screenshot the Gameface UI`,
      description: oneLine`
        Capture a screenshot of the Gameface viewport (CDP Page.captureScreenshot) and return it
        as an inline image.
        Pass a selector to clip the capture to one element; use index to pick among matches.
        Hangs while the JS debugger is paused; resume (game_debug_step) before capturing.
      `,
      inputSchema: {
        format: z.enum(['png', 'jpeg']).optional().describe(`Image format (default: jpeg)`),
        quality: z
          .number()
          .min(1)
          .max(100)
          .optional()
          .describe(`JPEG quality 1-100 (only used when format is jpeg; default 80)`),
        selector: z
          .string()
          .optional()
          .describe(`If set, clip the screenshot to this element's bounding box`),
        index: z
          .number()
          .int()
          .min(0)
          .optional()
          .describe(`Which match to clip to when several exist (default: 0)`)
      }
    },
    ({ format, quality, selector, index }) =>
      gameScreenshot(client, { format, quality, selector, index })
  );

  server.registerTool(
    'game_wait',
    {
      title: `Wait for a condition in the Gameface UI`,
      description: oneLine`
        Wait until a CSS selector matches (optionally visible), a JS predicate becomes truthy,
        and/or a view reload happens. Provide at least one of reload / selector / predicate
        (selector and predicate are mutually exclusive).
        With reload, the phases compose: reload first, then a quiescence window, then the
        selector/predicate poll in the fresh context.
        Returns when met or times out.
      `,
      inputSchema: {
        selector: z.string().optional().describe(`CSS selector to wait for`),
        predicate: z
          .string()
          .optional()
          .describe(`JS expression evaluated in the page; waits until it is truthy`),
        reload: z.boolean().optional().describe(oneLine`
            Wait for a view reload (context reset) before the selector/predicate phase.
            Without sinceReloads, waits for the next reload after the call starts.
          `),
        sinceReloads: z.number().int().min(0).optional().describe(oneLine`
            Baseline reload count (from a prior game_status or game_wait).
            The reload phase is satisfied as soon as the count exceeds it, even if the reload
            already happened; use it to avoid racing a reload you triggered yourself.
          `),
        quiescentMs: z.number().int().min(0).optional().describe(oneLine`
            After a reload is observed, hold until no further context swap for this long (default
            1000, 0 disables); absorbs engines that swap the context several times per reload.
          `),
        timeoutMs: z.number().int().min(0).optional().describe(oneLine`
            Max time to wait in ms (default 8000, or 30000 when reload is set; capped at 60000)
          `),
        visible: z
          .boolean()
          .optional()
          .describe(`For selector waits, also require a non-zero bounding box (default false)`)
      }
    },
    ({ selector, predicate, reload, sinceReloads, quiescentMs, timeoutMs, visible }) =>
      gameWait(client, reloads, {
        selector,
        predicate,
        reload,
        sinceReloads,
        quiescentMs,
        timeoutMs,
        visible
      })
  );

  server.registerTool(
    'game_fill',
    {
      title: `Set an input value in the Gameface UI`,
      description: oneLine`
        Set the value of an input, textarea, or contenteditable element and fire input/change so
        the UI framework reacts as if the user edited it.
        Best for setting a field in one shot; use game_type for keystrokes.
        Use index to pick among matches.
      `,
      inputSchema: {
        selector: z.string().describe(`CSS selector of the field to fill`),
        value: z.string().describe(`Value to set`),
        index: z
          .number()
          .int()
          .min(0)
          .optional()
          .describe(`Which match to fill when several exist (default: 0)`)
      }
    },
    ({ selector, value, index }) => gameFill(client, selector, value, index)
  );

  server.registerTool(
    'game_type',
    {
      title: `Type text into the Gameface UI`,
      description: oneLine`
        Type text into an element character by character, firing real KeyboardEvents plus keeping
        the value in sync.
        Use when handlers react to individual keystrokes; otherwise game_fill.
        Use index to pick among matches.
      `,
      inputSchema: {
        selector: z.string().describe(`CSS selector of the field to type into`),
        text: z.string().describe(`Text to type`),
        index: z
          .number()
          .int()
          .min(0)
          .optional()
          .describe(`Which match to type into when several exist (default: 0)`)
      }
    },
    ({ selector, text, index }) => gameType(client, selector, text, index)
  );

  server.registerTool(
    'game_hover',
    {
      title: `Hover an element in the Gameface UI`,
      description: oneLine`
        Hover an element by dispatching the pointer/mouse over/enter/move sequence in the page, so
        the UI's mouseenter / pointerover JS handlers (tooltips) fire.
        The CSS :hover state is NOT set (only real game-forwarded mouse input sets it); verify a
        hover by its DOM effect, never by styling.
        Use index to pick among matches.
      `,
      inputSchema: {
        selector: z.string().describe(`CSS selector of the element to hover`),
        index: z
          .number()
          .int()
          .min(0)
          .optional()
          .describe(`Which match to hover when several exist (default: 0)`)
      }
    },
    ({ selector, index }) => gameHover(client, selector, index)
  );

  server.registerTool(
    'game_key',
    {
      title: `Press a key in the Gameface UI`,
      description: oneLine`
        Press a named key by dispatching a real bubbling keydown+keyup in the page
        (KeyboardEvent.key, e.g. Escape, Enter, ArrowDown, a, F5), optionally with
        ctrl/shift/alt/meta and a repeat count.
        With a selector it focuses that element and dispatches on it (index picks among matches);
        without one it dispatches on the focused element, else document.
        It fires ONLY keydown and keyup (no keypress) and performs NO default action: no character
        insertion, no Backspace delete, no Tab focus move, no scrolling; use game_type to enter
        text.
        It reaches the UI's JS keydown handlers, but keys the game routes through its own native
        input layer do NOT respond (e.g. an Escape-to-close handled by the engine rather than the
        DOM).
        The result reports whether a handler called preventDefault, the observable signal the key
        was consumed.
      `,
      inputSchema: {
        key: z
          .string()
          .describe(`KeyboardEvent.key name to press, e.g. Escape, Enter, ArrowDown, a, F5`),
        count: z
          .number()
          .int()
          .min(1)
          .max(100)
          .optional()
          .describe(`How many keydown+keyup presses to fire back-to-back (default 1)`),
        ctrl: z
          .boolean()
          .optional()
          .describe(`Hold Ctrl (ctrlKey) during the press (default false)`),
        shift: z
          .boolean()
          .optional()
          .describe(`Hold Shift (shiftKey) during the press (default false)`),
        alt: z.boolean().optional().describe(`Hold Alt (altKey) during the press (default false)`),
        meta: z
          .boolean()
          .optional()
          .describe(`Hold Meta / Win / Cmd (metaKey) during the press (default false)`),
        selector: z
          .string()
          .optional()
          .describe(`If set, focus this element and dispatch on it; else the focused element`),
        index: z
          .number()
          .int()
          .min(0)
          .optional()
          .describe(`Which match to target when the selector has several (default: 0)`)
      }
    },
    ({ key, count, ctrl, shift, alt, meta, selector, index }) =>
      gameKey(client, { key, count, ctrl, shift, alt, meta, selector, index })
  );

  server.registerTool(
    'game_console',
    {
      title: `Read the Gameface UI console`,
      description: oneLine`
        Return recent console.* calls, log entries, and uncaught exceptions captured from the
        Gameface UI.
        Capture starts when the server first connects to the application.
      `,
      inputSchema: {
        limit: z
          .number()
          .int()
          .min(1)
          .max(1000)
          .optional()
          .describe(`Max entries to return (default 50)`),
        level: z.string().optional().describe(`Filter by level, e.g. error / warning / log / info`),
        clear: z.boolean().optional().describe(`Clear the buffer after reading (default false)`)
      }
    },
    ({ limit, level, clear }) => gameConsole(client, consoleBuffer, { limit, level, clear })
  );

  server.registerTool(
    'game_dom',
    {
      title: `Inspect Gameface UI DOM`,
      description: oneLine`
        Return DOM details (tag, id, classes, attributes, bounding rect, outerHTML) for elements
        matching a CSS selector in the live Gameface UI.
        Set all=true to return every match.
      `,
      inputSchema: {
        selector: z.string().describe(`CSS selector to query in the Gameface UI`),
        all: z
          .boolean()
          .optional()
          .describe(`Return all matches instead of just the first (default: false)`),
        maxHtml: z
          .number()
          .min(0)
          .optional()
          .describe(`Max outerHTML characters per element before truncation (default: 4000)`)
      }
    },
    ({ selector, all, maxHtml }) => gameDom(client, selector, all, maxHtml)
  );

  server.registerTool(
    'game_find',
    {
      title: `Find elements by text in the Gameface UI`,
      description: oneLine`
        Locate elements by their text content in the live Gameface UI: scan a CSS selector's matches
        (default: every element) and filter on trimmed textContent by equals/contains/regex
        (case-insensitive by default).
        Returns tag, id, classes, and bounding rect per match, plus match counts before and after
        deepest pruning so pruning and limit truncation are both visible.
        Set tag=true to stamp matches with data-gf-find handles and get back ready-to-use selectors
        for game_click / game_hover / game_screenshot.
        The go-to way to find an element when class names are build-hashed and there is no XPath.
      `,
      inputSchema: {
        text: z.string().describe(`Text to match against each element's trimmed textContent`),
        match: z
          .enum(['equals', 'contains', 'regex'])
          .optional()
          .describe(`How to match the text: equals / contains / regex (default: contains)`),
        caseSensitive: z.boolean().optional().describe(`Match case-sensitively (default: false)`),
        selector: z
          .string()
          .optional()
          .describe(`CSS selector scoping the scan (default: *, every element)`),
        deepest: z
          .boolean()
          .optional()
          .describe(
            `Keep only the innermost match, pruning ancestors that also matched (default: true)`
          ),
        tag: z
          .boolean()
          .optional()
          .describe(
            oneLine`
              Stamp matches with data-gf-find handles and return them as selectors, clearing any
              prior handles first (default: false).
            `
          ),
        limit: z
          .number()
          .int()
          .min(1)
          .max(100)
          .optional()
          .describe(`Max matches to return (default: 20); the total count is always reported`)
      }
    },
    ({ text, match, caseSensitive, selector, deepest, tag, limit }) =>
      gameFind(client, { text, match, caseSensitive, selector, deepest, tag, limit })
  );

  server.registerTool(
    'game_click',
    {
      title: `Click an element in the Gameface UI`,
      description: oneLine`
        Click the element matching a CSS selector by dispatching a real bubbling pointer/mouse/click
        sequence in the page (NOT CDP Input, which Gameface ignores for the UI).
        Use index to pick among matches.
      `,
      inputSchema: {
        selector: z.string().describe(`CSS selector of the element to click`),
        index: z
          .number()
          .int()
          .min(0)
          .optional()
          .describe(`Which match to click when several exist (default: 0)`)
      }
    },
    ({ selector, index }) => gameClick(client, selector, index)
  );

  server.registerTool(
    'game_debug_status',
    {
      title: `JS debugger status`,
      description: oneLine`
        Report debugger state: whether paused (and where), pause-on-exceptions mode, breakpoints,
        and parsed script count.
        Pass setPauseOnExceptions to change exception pausing.
        Enables the debugger on first use.
        Hitting a breakpoint FREEZES the UI until resumed.
      `,
      inputSchema: {
        setPauseOnExceptions: z
          .enum(['none', 'uncaught', 'all'])
          .optional()
          .describe(`If set, change which exceptions pause execution (default none)`)
      }
    },
    ({ setPauseOnExceptions }) => debug.status(setPauseOnExceptions)
  );

  server.registerTool(
    'game_debug_scripts',
    {
      title: `List parsed UI scripts`,
      description: oneLine`
        List JavaScript scripts parsed in the Gameface UI (scriptId + url + line count), optionally
        filtered by a url substring.
        Only scripts parsed after the debugger attached appear (Gameface does not replay
        scriptParsed); an empty list means attach first, then trigger a view reload to repopulate.
        Use the scriptId with game_debug_source.
      `,
      inputSchema: {
        filter: z.string().optional().describe(`Only scripts whose url contains this substring`)
      }
    },
    ({ filter }) => debug.listScripts(filter)
  );

  server.registerTool(
    'game_debug_source',
    {
      title: `Get UI script source`,
      description: oneLine`
        Return the source of a script (by scriptId from game_debug_scripts), with line numbers.
        Pass lineStart/lineEnd to get a range (large scripts are capped at 400 lines).
      `,
      inputSchema: {
        scriptId: z.string().describe(`Script id from game_debug_scripts`),
        lineStart: z.number().int().min(1).optional().describe(`First line (1-based)`),
        lineEnd: z.number().int().min(1).optional().describe(`Last line (1-based)`)
      }
    },
    ({ scriptId, lineStart, lineEnd }) => debug.getSource(scriptId, lineStart, lineEnd)
  );

  server.registerTool(
    'game_debug_set_breakpoint',
    {
      title: `Set a breakpoint`,
      description: oneLine`
        Set a breakpoint by url substring + line (1-based).
        Add a condition (JS expression) to only pause when it is truthy, which limits how often the
        UI freezes.
        Hitting it FREEZES the UI until you resume with game_debug_step.
      `,
      inputSchema: {
        urlContains: z.string().describe(`Substring of the script url to break in`),
        line: z.number().int().min(1).describe(`Line number (1-based)`),
        column: z.number().int().min(0).optional().describe(`Column (0-based), optional`),
        condition: z
          .string()
          .optional()
          .describe(`Optional JS condition; pause only when it evaluates truthy`)
      }
    },
    ({ urlContains, line, column, condition }) =>
      debug.setBreakpoint(urlContains, line, column, condition)
  );

  server.registerTool(
    'game_debug_remove_breakpoint',
    {
      title: `Remove a breakpoint`,
      description: `Remove a breakpoint by its id (from game_debug_status), or pass 'all'.`,
      inputSchema: {
        breakpoint: z.string().describe(`Breakpoint id, or 'all'`)
      }
    },
    ({ breakpoint }) => debug.removeBreakpoint(breakpoint)
  );

  server.registerTool(
    'game_debug_pause_state',
    {
      title: `Inspect the paused stack`,
      description: oneLine`
        When paused, return the call stack (frames with function + location + scope types).
        Set expandScopes to also list local/closure variables of each frame.
        Returns 'not paused' otherwise.
      `,
      inputSchema: {
        expandScopes: z
          .boolean()
          .optional()
          .describe(`Also list local/closure variables per frame (default false)`)
      }
    },
    ({ expandScopes }) => debug.pauseStateReport(expandScopes ?? false)
  );

  server.registerTool(
    'game_debug_evaluate',
    {
      title: `Evaluate while debugging`,
      description: oneLine`
        Evaluate a JS expression.
        When paused, it runs in the selected call frame's scope (Debugger.evaluateOnCallFrame) so
        you can read locals; otherwise it runs globally.
        Prefer this over game_eval while paused.
      `,
      inputSchema: {
        expression: z.string().describe(`JS expression to evaluate`),
        frameIndex: z
          .number()
          .int()
          .min(0)
          .optional()
          .describe(`Call frame index to evaluate in when paused (default 0 = top)`)
      }
    },
    ({ expression, frameIndex }) => debug.evaluate(expression, frameIndex)
  );

  server.registerTool(
    'game_debug_step',
    {
      title: `Step / resume / pause execution`,
      description: oneLine`
        Control paused execution: resume (unfreeze the UI), over/into/out (step), or pause (break at
        the next statement).
        Stepping reports the new location.
      `,
      inputSchema: {
        action: z
          .enum(['resume', 'over', 'into', 'out', 'pause'])
          .describe(`resume | over | into | out | pause`)
      }
    },
    ({ action }) => debug.step(action)
  );

  const transport = new StdioServerTransport();

  await server.connect(transport);

  // noinspection HttpUrlsUsage
  process.stderr.write(
    `gameface MCP server v${VERSION} ready (target http://${config.host}:${config.port})\n`
  );
}

try {
  await main();
} catch (error) {
  const detail = error instanceof Error ? (error.stack ?? error.message) : String(error);

  process.stderr.write(`gameface MCP server failed to start: ${detail}\n`);

  // oxlint-disable-next-line unicorn/no-process-exit -- Fatal startup failure; the connected stdio transport would otherwise keep the process alive.
  process.exit(1);
}
