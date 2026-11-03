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
import { loadLocalModsPath, loadUserDataPath } from './config';
import {
  BOOT_SECONDS,
  type CallSlot,
  LOAD_SECONDS,
  LOADING_SCREEN_SECONDS,
  READ_CEILING_SECONDS,
  RESTART_CEILING_SECONDS,
  SCENARIO_RUN_SECONDS,
  STOP_BOUND_SECONDS,
  TEST_CEILING_SECONDS,
  type ToolContext
} from './cycle';
import { createGameHost } from './game-host';
import { logLevels } from './game-log';
import { clearIgnores, IGNORE_INPUT_CHARACTERS } from './ignores';
import { LOGS_CAP_CHARACTERS } from './log-report';
import { STALL_SECONDS } from './scenario-run';
import {
  DEFAULT_CATEGORY,
  ignoreError,
  logs,
  restart,
  status,
  stop,
  testMod,
  waitLoad
} from './tools';

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

async function main(): Promise<void> {
  const userDataPath = loadUserDataPath();
  const localModsPath = loadLocalModsPath(userDataPath);
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
      localModsPath,
      slot,
      signal: extra.signal,
      progress: message => {
        void sendProgress(message);
      }
    };
  }

  // A session is the server's life, and no folder yet is no pattern either.
  await clearIgnores(userDataPath).catch(() => {
    // Patterns left over silence a little too much, which beats a server that cannot start.
  });

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
        mod: z.string().min(1).optional().describe(oneLine`
            Assembly name of a mod: the result reports its load from the logs, and later hints count
            the errors naming it
          `)
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
        Follows a load started from the main menu, which may not be logged yet the moment after
        its trigger; from a city it reports the load already done until the new one is logged.
        Fails on a stopped game, on a game that dies, and on a load that falls back to the main
        menu; a timeout of ${LOAD_SECONDS + LOADING_SCREEN_SECONDS} s leaves the game running.
      `,
      annotations: { readOnlyHint: true }
    },
    extra => waitLoad(contextOf(extra))
  );

  server.registerTool(
    'test',
    {
      title: `Run a mod's in-game scenarios`,
      description: oneLine`
        Stop the game if it runs, keep the ended run's logs and crash reports, launch it with the
        scenario category as its test filter, watch until the game finishes its scenarios, and
        return each scenario's result and lines.
        A scenario passes when the game reports a success, one of its tests finished, and nothing in
        it logged an error: the game alone calls a scenario whose preparation threw a success.
        Any error naming the mod in any log but TestScenarios.log fails the run, an exception Unity
        logged and one ignore_error silences included, listed one per kind.
        A run with no scenario fails, which is how a Release build ends.
        The game stops when the run ends unless keepOpen.
        Its stops are the stop tool's: up to ${STOP_BOUND_SECONDS} s, then forced, and only a close
        past boot saves settings.
        Blocks up to ${TEST_CEILING_SECONDS} s in all, of which ${SCENARIO_RUN_SECONDS} s from the
        launch for the run, ${BOOT_SECONDS} s for the scenarios to start, and ${STALL_SECONDS} s for
        a scenario to log nothing.
      `,
      inputSchema: {
        mod: z
          .string()
          .min(1)
          .describe(`Assembly name of the mod: later hints count the errors naming it too`),
        category: z
          .number()
          .int()
          .positive()
          .optional()
          .describe(
            `The scenarios' TestDescriptor category (default: 1 << 20, ${DEFAULT_CATEGORY})`
          ),
        sources: z.string().min(1).optional().describe(oneLine`
            Absolute path of the mod's project folder: the result warns when a source changed after
            the deployed build
          `),
        keepOpen: z
          .boolean()
          .optional()
          .describe(`Leave the game running, to inspect what a scenario left`),
        gameOptions: z.array(z.string()).optional().describe(oneLine`
            The game's own options, as for restart: an unknown one is ignored silently, and one
            holding whitespace is refused
          `)
      }
    },
    (request, extra) => testMod(contextOf(extra), request)
  );

  server.registerTool(
    'status',
    {
      title: `Read the game's state`,
      description: oneLine`
        Say whether the game is stopped, booting, at the main menu, loading or in a city, with its
        PIDs, its version, the debug-patch signals its logs show, and the active ignore patterns.
        On a stopped game it reads the last run's logs and says so.
        Blocks up to ${READ_CEILING_SECONDS} s.
      `,
      annotations: { readOnlyHint: true }
    },
    extra => status(contextOf(extra))
  );

  server.registerTool(
    'logs',
    {
      title: `Read the game's logs`,
      description: oneLine`
        Return one entry per kind of error or message, a storm costing one entry: the file and line
        of its first occurrence, its count, its first and last stamps, and its text with its stack,
        cut past ${IGNORE_INPUT_CHARACTERS} characters.
        Reads the run from its start, unlike the hint, which says what is new.
        An untagged Player.log line is no entry, Debug.LogError output included, unless it opens an
        exception Unity logged.
        Capped at ${LOGS_CAP_CHARACTERS} characters, with a footer saying what was left out and how
        to narrow it.
        Cited lines hold until a launch rewrites the file, or a restart or test replaces the
        previous run's copy.
        Blocks up to ${READ_CEILING_SECONDS} s.
      `,
      inputSchema: {
        file: z
          .string()
          .optional()
          .describe(`One log, ex. HallOfFame, or Player.log with what Logs/ also holds`),
        level: z
          .string()
          .optional()
          .describe(`One of ${logLevels.join(', ')}`),
        mod: z.string().min(1).optional().describe(`Assembly name of a mod the entries must name`),
        errorsOnly: z
          .boolean()
          .optional()
          .describe(`Only ERROR and above, and exceptions Unity logged`),
        previous: z
          .boolean()
          .optional()
          .describe(`Read the run before the last restart or test, from its kept copy`),
        ignored: z.boolean().optional().describe(`Bring back the entries ignore_error silenced`)
      },
      annotations: { readOnlyHint: true }
    },
    (request, extra) => logs(contextOf(extra), request)
  );

  server.registerTool(
    'ignore_error',
    {
      title: `Ignore an error for the session`,
      description: oneLine`
        Silence the errors matching a pattern in the hint and the notification, for this session.
        A pattern is a case-insensitive regular expression tested on what logs prints for an entry,
        less its line number: its file, tagged logger, bracketed level, first line and stack.
        It lasts until the server restarts, a reconnect or a resume included, and changes no test
        verdict and no restart report.
        A pattern that takes too long is dropped, and the hint says so.
        Returns the active patterns with their approximate hit counts.
      `,
      inputSchema: {
        pattern: z.string().min(1).describe(`Regular expression, ex. HTTP: Error sending request`),
        remove: z.boolean().optional().describe(`Remove this exact pattern instead`)
      }
    },
    (request, extra) => ignoreError(contextOf(extra), request)
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
