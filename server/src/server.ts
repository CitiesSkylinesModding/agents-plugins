/**
 * Gameface MCP server entry point.
 *
 * Exposes tools for driving a Coherent Gameface application UI over a direct CDP WebSocket.
 * Runs unchanged under Bun or Node 24+ (both provide global WebSocket / fetch).
 * All diagnostics go to stderr; stdout is reserved for the MCP JSON-RPC stream.
 */

import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { oneLine } from 'common-tags';
import { z } from 'zod';
import { CdpClient } from './cdp';
import { loadConfig } from './config';
import { DebuggerSession } from './debugger';
import {
  ConsoleBuffer,
  gameClick,
  gameConsole,
  gameDom,
  gameEval,
  gameFill,
  gameHover,
  gameScreenshot,
  gameStatus,
  gameType,
  gameWait
} from './tools';

const VERSION = '0.1.0';

async function main(): Promise<void> {
  const config = loadConfig();
  const client = new CdpClient(config);

  // Constructing these registers their CDP connect/event listeners; they must exist before the
  // first connection, so console capture and breakpoint re-binding are armed from the start.
  const consoleBuffer = new ConsoleBuffer(client);
  const debug = new DebuggerSession(client);

  const server = new McpServer({ name: 'gameface', version: VERSION });

  server.registerTool(
    'game_status',
    {
      title: 'Gameface UI status',
      description: oneLine`
        Check whether the Gameface UI debug endpoint is reachable and report the live page
        target and engine info. Run this first when other game_* tools fail.
      `
    },
    () => gameStatus(client)
  );

  server.registerTool(
    'game_eval',
    {
      title: 'Evaluate JS in the Gameface UI',
      description: oneLine`
        Evaluate a JavaScript expression in the running Gameface UI (CDP Runtime.evaluate,
        returnByValue) and return the resulting value as JSON. Use document.querySelector and
        friends to read the live DOM, inspect React state, or call UI APIs.
      `,
      inputSchema: {
        expression: z.string().describe('JavaScript expression to evaluate in the page context'),
        awaitPromise: z
          .boolean()
          .optional()
          .describe('If the expression returns a Promise, await it before returning')
      }
    },
    ({ expression, awaitPromise }) => gameEval(client, expression, awaitPromise)
  );

  server.registerTool(
    'game_screenshot',
    {
      title: 'Screenshot the Gameface UI',
      description: oneLine`
        Capture a screenshot of the Gameface viewport (CDP Page.captureScreenshot) and return it
        as an inline image. Pass a selector to clip the capture to one element. Use jpeg with a
        lower quality to reduce payload size.
      `,
      inputSchema: {
        format: z.enum(['png', 'jpeg']).optional().describe('Image format (default: png)'),
        quality: z
          .number()
          .min(1)
          .max(100)
          .optional()
          .describe('JPEG quality 1-100 (only used when format is jpeg; default 80)'),
        selector: z
          .string()
          .optional()
          .describe("If set, clip the screenshot to this element's bounding box")
      }
    },
    ({ format, quality, selector }) => gameScreenshot(client, format, quality, selector)
  );

  server.registerTool(
    'game_wait',
    {
      title: 'Wait for a condition in the Gameface UI',
      description: oneLine`
        Wait until a CSS selector matches (optionally visible) or a JS predicate becomes truthy,
        polling the page. Provide exactly one of selector / predicate. Returns when met or times
        out.
      `,
      inputSchema: {
        selector: z.string().optional().describe('CSS selector to wait for'),
        predicate: z
          .string()
          .optional()
          .describe('JS expression evaluated in the page; waits until it is truthy'),
        timeoutMs: z
          .number()
          .int()
          .min(0)
          .optional()
          .describe('Max time to wait in ms (default 8000, capped at 60000)'),
        visible: z
          .boolean()
          .optional()
          .describe('For selector waits, also require a non-zero bounding box (default false)')
      }
    },
    ({ selector, predicate, timeoutMs, visible }) =>
      gameWait(client, { selector, predicate, timeoutMs, visible })
  );

  server.registerTool(
    'game_fill',
    {
      title: 'Set an input value in the Gameface UI',
      description: oneLine`
        Set the value of an input, textarea, or contenteditable element and fire input/change so
        React's onChange runs. Best for setting a field in one shot; use game_type for keystrokes.
      `,
      inputSchema: {
        selector: z.string().describe('CSS selector of the field to fill'),
        value: z.string().describe('Value to set')
      }
    },
    ({ selector, value }) => gameFill(client, selector, value)
  );

  server.registerTool(
    'game_type',
    {
      title: 'Type text into the Gameface UI',
      description: oneLine`
        Type text into an element character by character, firing real KeyboardEvents plus keeping
        the value in sync. Use when handlers react to individual keystrokes; otherwise game_fill.
      `,
      inputSchema: {
        selector: z.string().describe('CSS selector of the field to type into'),
        text: z.string().describe('Text to type')
      }
    },
    ({ selector, text }) => gameType(client, selector, text)
  );

  server.registerTool(
    'game_hover',
    {
      title: 'Hover an element in the Gameface UI',
      description: oneLine`
        Hover an element by dispatching the pointer/mouse over/enter/move sequence in the page, so
        React onMouseEnter / onPointerOver handlers (tooltips, hover states) fire.
      `,
      inputSchema: {
        selector: z.string().describe('CSS selector of the element to hover')
      }
    },
    ({ selector }) => gameHover(client, selector)
  );

  server.registerTool(
    'game_console',
    {
      title: 'Read the Gameface UI console',
      description: oneLine`
        Return recent console.* calls, log entries, and uncaught exceptions captured from the
        Gameface UI. Capture starts when the server first connects to the application.
      `,
      inputSchema: {
        limit: z
          .number()
          .int()
          .min(1)
          .max(1000)
          .optional()
          .describe('Max entries to return (default 50)'),
        level: z.string().optional().describe('Filter by level, e.g. error / warning / log / info'),
        clear: z.boolean().optional().describe('Clear the buffer after reading (default false)')
      }
    },
    ({ limit, level, clear }) => gameConsole(client, consoleBuffer, { limit, level, clear })
  );

  server.registerTool(
    'game_dom',
    {
      title: 'Inspect Gameface UI DOM',
      description: oneLine`
        Return DOM details (tag, id, classes, attributes, bounding rect, outerHTML) for elements
        matching a CSS selector in the live Gameface UI. Set all=true to return every match.
      `,
      inputSchema: {
        selector: z.string().describe('CSS selector to query in the Gameface UI'),
        all: z
          .boolean()
          .optional()
          .describe('Return all matches instead of just the first (default: false)'),
        maxHtml: z
          .number()
          .min(0)
          .optional()
          .describe('Max outerHTML characters per element before truncation (default: 4000)')
      }
    },
    ({ selector, all, maxHtml }) => gameDom(client, selector, all, maxHtml)
  );

  server.registerTool(
    'game_click',
    {
      title: 'Click an element in the Gameface UI',
      description: oneLine`
        Click the element matching a CSS selector by dispatching a real bubbling
        pointer/mouse/click sequence in the page (NOT CDP Input, which Gameface ignores for the
        UI). React onClick handlers fire via event delegation. Use index to pick among matches.
      `,
      inputSchema: {
        selector: z.string().describe('CSS selector of the element to click'),
        index: z
          .number()
          .int()
          .min(0)
          .optional()
          .describe('Which match to click when several exist (default: 0)')
      }
    },
    ({ selector, index }) => gameClick(client, selector, index)
  );

  server.registerTool(
    'game_debug_status',
    {
      title: 'JS debugger status',
      description: oneLine`
        Report debugger state: whether paused (and where), pause-on-exceptions mode, breakpoints,
        and parsed script count. Pass setPauseOnExceptions to change exception pausing. Enables
        the debugger on first use. Hitting a breakpoint FREEZES the UI until resumed.
      `,
      inputSchema: {
        setPauseOnExceptions: z
          .enum(['none', 'uncaught', 'all'])
          .optional()
          .describe('If set, change which exceptions pause execution (default none)')
      }
    },
    ({ setPauseOnExceptions }) => debug.status(setPauseOnExceptions)
  );

  server.registerTool(
    'game_debug_scripts',
    {
      title: 'List parsed UI scripts',
      description: oneLine`
        List JavaScript scripts parsed in the Gameface UI (scriptId + url + line count),
        optionally filtered by a url substring. Use the scriptId with game_debug_source.
      `,
      inputSchema: {
        filter: z.string().optional().describe('Only scripts whose url contains this substring')
      }
    },
    ({ filter }) => debug.listScripts(filter)
  );

  server.registerTool(
    'game_debug_source',
    {
      title: 'Get UI script source',
      description: oneLine`
        Return the source of a script (by scriptId from game_debug_scripts), with line numbers.
        Pass lineStart/lineEnd to get a range (large scripts are capped at 400 lines).
      `,
      inputSchema: {
        scriptId: z.string().describe('Script id from game_debug_scripts'),
        lineStart: z.number().int().min(1).optional().describe('First line (1-based)'),
        lineEnd: z.number().int().min(1).optional().describe('Last line (1-based)')
      }
    },
    ({ scriptId, lineStart, lineEnd }) => debug.getSource(scriptId, lineStart, lineEnd)
  );

  server.registerTool(
    'game_debug_set_breakpoint',
    {
      title: 'Set a breakpoint',
      description: oneLine`
        Set a breakpoint by url substring + line (1-based). Add a condition (JS expression) to
        only pause when it is truthy, which limits how often the UI freezes. Hitting it FREEZES
        the UI until you resume with game_debug_step.
      `,
      inputSchema: {
        urlContains: z.string().describe('Substring of the script url to break in'),
        line: z.number().int().min(1).describe('Line number (1-based)'),
        column: z.number().int().min(0).optional().describe('Column (0-based), optional'),
        condition: z
          .string()
          .optional()
          .describe('Optional JS condition; pause only when it evaluates truthy')
      }
    },
    ({ urlContains, line, column, condition }) =>
      debug.setBreakpoint(urlContains, line, column, condition)
  );

  server.registerTool(
    'game_debug_remove_breakpoint',
    {
      title: 'Remove a breakpoint',
      description: "Remove a breakpoint by its id (from game_debug_status), or pass 'all'.",
      inputSchema: {
        breakpoint: z.string().describe("Breakpoint id, or 'all'")
      }
    },
    ({ breakpoint }) => debug.removeBreakpoint(breakpoint)
  );

  server.registerTool(
    'game_debug_pause_state',
    {
      title: 'Inspect the paused stack',
      description: oneLine`
        When paused, return the call stack (frames with function + location + scope types). Set
        expandScopes to also list local/closure variables of each frame. Returns 'not paused'
        otherwise.
      `,
      inputSchema: {
        expandScopes: z
          .boolean()
          .optional()
          .describe('Also list local/closure variables per frame (default false)')
      }
    },
    ({ expandScopes }) => debug.pauseStateReport(expandScopes ?? false)
  );

  server.registerTool(
    'game_debug_evaluate',
    {
      title: 'Evaluate while debugging',
      description: oneLine`
        Evaluate a JS expression. When paused, it runs in the selected call frame's scope
        (Debugger.evaluateOnCallFrame) so you can read locals; otherwise it runs globally. Prefer
        this over game_eval while paused.
      `,
      inputSchema: {
        expression: z.string().describe('JS expression to evaluate'),
        frameIndex: z
          .number()
          .int()
          .min(0)
          .optional()
          .describe('Call frame index to evaluate in when paused (default 0 = top)')
      }
    },
    ({ expression, frameIndex }) => debug.evaluate(expression, frameIndex)
  );

  server.registerTool(
    'game_debug_step',
    {
      title: 'Step / resume / pause execution',
      description: oneLine`
        Control paused execution: resume (unfreeze the UI), over / into / out (step), or pause
        (break at the next statement). Stepping reports the new location.
      `,
      inputSchema: {
        action: z
          .enum(['resume', 'over', 'into', 'out', 'pause'])
          .describe('resume | over | into | out | pause')
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
