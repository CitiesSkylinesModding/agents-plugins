/**
 * Cs2-dev MCP server entry point.
 *
 * Exposes tools over the lifecycle of Cities: Skylines II.
 * Runs under plain Node; all diagnostics go to stderr, stdout is reserved for the MCP JSON-RPC
 * stream.
 */

import { readFileSync } from 'node:fs';
import process from 'node:process';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import type { RequestHandlerExtra } from '@modelcontextprotocol/sdk/shared/protocol.js';
import type { ServerNotification, ServerRequest } from '@modelcontextprotocol/sdk/types.js';
import { oneLine } from 'common-tags';
import { z } from 'zod';
import { loadUserDataPath } from './config';
import {
  BOOT_SECONDS,
  type CallSlot,
  LOAD_SECONDS,
  LOADING_SCREEN_SECONDS,
  RESTART_CEILING_SECONDS,
  STOP_BOUND_SECONDS,
  type ToolContext
} from './cycle';
import { createGameHost } from './game-host';
import { restart, stop, waitLoad } from './tools';

// The plugin's version, the server having no release of its own. Read at runtime, as both execution
// modes sit two levels below the plugin's package.json (dist/server.mjs and src/server.ts).
const { version: VERSION } = JSON.parse(
  // oxlint-disable-next-line node/no-sync -- One-shot startup read, nothing is serving yet.
  readFileSync(new URL('../../package.json', import.meta.url), 'utf8')
) as { version: string };

/**
 * Milliseconds a server whose client went gives its calls to clean up:
 * a harness follows its signal with a SIGKILL.
 */
const SHUTDOWN_GRACE_MILLISECONDS = 200;

function main(): Promise<void> {
  const userDataPath = loadUserDataPath();
  const host = createGameHost(userDataPath);
  const slot: CallSlot = { running: undefined };

  /**
   * A tool's context, sending its progress where the client supplied a token:
   * Claude Code aborts a stdio call that stays silent for 30 minutes.
   */
  function contextOf(extra: RequestHandlerExtra<ServerRequest, ServerNotification>): ToolContext {
    // oxlint-disable-next-line no-underscore-dangle -- the protocol's own field.
    const progressToken = extra._meta?.progressToken;

    let progress = 0;

    async function sendProgress(message: string): Promise<void> {
      if (progressToken == null) {
        return;
      }

      progress++;

      try {
        await extra.sendNotification({
          method: 'notifications/progress',
          params: { progressToken, progress, message }
        });
      } catch {
        // A notification that cannot be delivered changes nothing about the call.
      }
    }

    return {
      host,
      userDataPath,
      slot,
      signal: extra.signal,
      progress: message => {
        void sendProgress(message);
      }
    };
  }

  const server = new McpServer({ name: 'cs2-dev', version: VERSION });

  server.registerTool(
    'restart',
    {
      title: `Restart the game`,
      description: oneLine`
        Stop the game if it runs, keep the ended run's logs and crash reports, launch it through
        Steam, and return at the main menu, or once the save or the new city loaded and its loading
        screen went.
        Its stop is the stop tool's: up to ${STOP_BOUND_SECONDS} s, then forced, only a close past
        boot saves settings, and a game held by a debugger takes the whole wait.
        Blocks up to ${RESTART_CEILING_SECONDS} s in all, ${BOOT_SECONDS} s of it for a boot and
        ${LOAD_SECONDS + LOADING_SCREEN_SECONDS} s for a load.
        Fails at once on a game that dies, and on a load that falls back to the main menu.
        A timeout leaves the game running and says so.
        Unverified on Windows.
      `,
      inputSchema: {
        save: z.string().optional().describe(`Name of a save to load, as in the game's list`),
        newCity: z
          .object({
            map: z
              .string()
              .optional()
              .describe(`Map guid, or a user map's name (default: Archipelago)`)
          })
          .optional()
          .describe(`Start a new city instead of loading a save`),
        gameOptions: z.array(z.string()).optional().describe(oneLine`
            The game's own options, ex. --uiDeveloperMode, not Unity's or Burst's, which are read
            before it.
            The game ignores an unknown one silently: read the options the result names.
            Without save or newCity the call waits for the main menu alone, which an option loading
            a city or the editor never reaches unless the load fails: load through save or newCity.
            An option holding whitespace is refused, as the game takes no quoting.
          `),
        mod: z
          .string()
          .optional()
          .describe(`Assembly name of a mod whose load the result reports from the logs`)
      }
    },
    (request, extra) => restart(contextOf(extra), request)
  );

  server.registerTool(
    'stop',
    {
      title: `Stop the game`,
      description: oneLine`
        Ask the game once to close, wait up to ${STOP_BOUND_SECONDS} s for its exit, then force it.
        The result says whether it closed cleanly or was forced.
        Only a close past boot saves settings.
        A game held by a debugger takes the whole wait, then is forced.
        Unverified on Windows.
      `
    },
    extra => stop(contextOf(extra))
  );

  server.registerTool(
    'wait_load',
    {
      title: `Wait for a load`,
      description: oneLine`
        Return once the last logged city or map load completed and its loading screen went.
        Follows a load started from the UI, which may not be logged yet the moment after its
        trigger.
        Fails on a stopped game, on a game that dies, and on a load that falls back to the main
        menu; a timeout of ${LOAD_SECONDS + LOADING_SCREEN_SECONDS} s leaves the game running.
        Unverified on Windows.
      `
    },
    extra => waitLoad(contextOf(extra))
  );

  const transport = new StdioServerTransport();

  /**
   * Ends the running calls of a client that went away, then the server.
   * A signal ends it even with a step that never settles, which `process.exit` would wait on.
   * A handled signal has lost its exit, so the same signal, unhandled by then, ends the server.
   * See docs/solutions/an-mcp-server-outliving-its-client.md.
   */
  function shutDown(signal?: NodeJS.Signals): void {
    void transport.close();

    const ending = signal ?? 'SIGTERM';

    const end = setTimeout(() => {
      process.removeAllListeners(ending);
      process.kill(process.pid, ending);
    }, SHUTDOWN_GRACE_MILLISECONDS);

    // With no signal to pass on, a server with nothing left running simply exits.
    if (!signal) {
      end.unref();
    }
  }

  // The transport ignores both the end of its input and an error on it, which a client on a socket
  // raises in place of that end, and an error on its output, which nothing would catch, raises when
  // a client closed that end alone.
  for (const stream of [process.stdin, process.stdout]) {
    stream.on('error', () => {
      shutDown();
    });
  }

  process.stdin.once('end', () => {
    shutDown();
  });

  for (const signal of ['SIGINT', 'SIGTERM', 'SIGHUP'] as const) {
    process.once(signal, () => {
      shutDown(signal);
    });
  }

  return server.connect(transport);
}

await main();
