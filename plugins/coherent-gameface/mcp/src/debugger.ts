/**
 * JS debugger facet: drives the Gameface UI's V8 Debugger domain (verified present: breakpoints,
 * paused events, evaluateOnCallFrame, stepping).
 *
 * IMPORTANT: hitting a breakpoint or pausing FREEZES the UI thread until you resume.
 * Keep pauses short, prefer conditional breakpoints, and always resume.
 * While paused, read frame locals with game_debug_evaluate (evaluateOnCallFrame); game_eval still
 * answers, but only in global scope.
 */

import { setTimeout as sleep } from 'node:timers/promises';
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { oneLine } from 'common-tags';
import type { CdpCall, CdpConnectListener, CdpConnectionHandle, CdpEventListener } from './cdp';
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

/**
 * Polling interval while waiting for pause/resume state changes.
 */
const POLL_INTERVAL_MS = 50;

/**
 * How long to wait for `Debugger.pause` to actually pause (the UI may be idle).
 */
const PAUSE_WAIT_MS = 3000;

/**
 * How long to wait for the resumed event after `Debugger.resume`.
 */
const RESUME_WAIT_MS = 2000;

/**
 * How long to wait for a step to re-pause before assuming execution continued.
 */
const STEP_WAIT_MS = 2000;

/**
 * Max local/closure variables reported per scope by game_debug_pause_state.
 */
const MAX_SCOPE_VARIABLES = 50;

/**
 * Max matches game_debug_search_source returns; the envelope reports the true total.
 * Exported so the tool description quotes the cap rather than restating it.
 */
export const MAX_SEARCH_MATCHES = 10;

/**
 * Characters of source kept either side of a search hit.
 */
const SEARCH_SNIPPET_MARGIN = 40;

/**
 * Characters of rendered source game_debug_source returns before clipping.
 * A line cap alone bounds nothing on a bundle whose single line is the whole module, and the cost
 * lands on a caller who cannot see it coming from the line count.
 * Matches game_dom's maxHtml default, the same budget for the same reason, and small on purpose:
 * overrunning it costs one call the result tells you how to make, while not having it costs context
 * nobody asked to spend.
 * Exported so the tool description quotes the default rather than restating it.
 */
export const DEFAULT_SOURCE_MAX_CHARS = 4000;

/**
 * The way out of an empty script map, which is the normal state of a late attach.
 * Why it is empty belongs to the tool descriptions; this is the recovery, which only the result
 * knows is needed.
 */
const EMPTY_SCRIPT_MAP_NOTE = oneLine`
  No scripts are parsed on this connection: a view reload re-parses every one of them.
  Take the reload count from game_status, game_eval of location.reload(), then game_wait with
  reload true and that count as sinceReloads, then call this again.
`;

/**
 * What a zero-match search should check before concluding the string is absent.
 */
const NO_MATCH_NOTE = oneLine`
  Nothing matched. The search is case-sensitive and literal (no regex), and it only reaches scripts
  parsed on this connection: game_debug_scripts lists them.
`;

/**
 * What a zero-match search means when the url filter, not the query, is what matched nothing.
 * The query never ran, so the note that blames it would send the caller to fix the wrong input.
 */
const FILTER_MISS_NOTE = oneLine`
  No parsed script's url contains that urlContains, so the query never ran against anything.
  game_debug_scripts lists the urls; the url filter is case-insensitive, so case is not the cause.
`;

/**
 * What an empty listing means when the filter, not the script map, is what emptied it.
 */
const FILTER_MISS_LIST_NOTE = oneLine`
  No parsed script's url contains that filter, though others are parsed: call this without one to
  see every url. Matching is case-insensitive, so case is not the cause.
`;

/**
 * What a zero-match search means when the engine would not hand over any of the sources.
 * The scripts were selected but none could be read, which is a stale script map rather than a miss.
 */
const UNREADABLE_NOTE = oneLine`
  The engine has no source for any of the scripts matched, so the query never ran: their ids are
  stale, which a view reload clears by re-parsing everything under fresh ones.
`;

/**
 * Where a search hit goes next, which is the whole point of reporting its column.
 */
const SEARCH_HINT = oneLine`
  Pass a match's line and column to game_debug_set_breakpoint to break there; both are 1-based, as
  everywhere in these tools, and its urlContains takes the match's url as printed.
`;

/**
 * What a breakpoint on a one-line script most likely did, fired from the script's own metadata.
 */
const SINGLE_LINE_HINT = oneLine`
  That script is one line, as a minified bundle is, so the whole module sits on the line the
  resolved location names and the breakpoint bound to the first breakable position at or after the
  one you asked for: read the column above to see where it landed.
  At the module's own first column that is evaluation code, which runs on load and never again
  during interaction.
  To break inside a function, find its column with game_debug_search_source and pass that column
  here.
`;

type PauseState = 'none' | 'uncaught' | 'all';
type StepAction = 'resume' | 'over' | 'into' | 'out' | 'pause';

/**
 * A debugger pause, as the tools a frozen UI would strand need to see it.
 */
export interface PauseSnapshot {
  readonly reason: string;

  /**
   * The top frame, as `function at url:line:column`.
   */
  readonly location: string;
}

/**
 * The slice of the CDP client the debugger session needs.
 * Narrow on purpose: it is what lets the session run against synthetic events, with no socket and
 * no application.
 */
export interface DebuggerCdp {
  readonly onConnect: (listener: CdpConnectListener) => void;
  readonly onEvent: (listener: CdpEventListener) => void;
  readonly call: CdpCall;

  /**
   * Awaited for its side effect alone: a connection is what enables the Debugger domain.
   */
  readonly connection: () => Promise<unknown>;
}

/**
 * The slice of the reload tracker the debugger session needs.
 */
export interface DebuggerReloads {
  readonly onReload: (listener: (count: number) => void) => void;
}

interface ScriptInfo {
  readonly scriptId: string;
  readonly url: string;

  /**
   * Where the script starts inside its resource, which is 0 for a whole .js file and non-zero for
   * a script embedded in a document.
   * CDP locations are resource-based while getScriptSource returns the body alone, so these offsets
   * are what convert between the two.
   */
  readonly startLine: number;
  readonly startColumn: number;
  readonly endLine: number;
  readonly length?: number | undefined;
}

interface Location {
  readonly scriptId: string;
  readonly lineNumber: number;
  readonly columnNumber?: number;
}

interface CallFrame {
  readonly callFrameId: string;
  readonly functionName: string;
  readonly location: Location;
  readonly url: string;
  readonly scopeChain: Array<{ type: string; name?: string; object: RemoteObject }>;
  readonly this?: RemoteObject;
}

interface PausedInfo {
  readonly reason: string;
  readonly hitBreakpoints?: string[] | undefined;
  readonly callFrames: CallFrame[];
}

interface LogicalBreakpoint {
  readonly id: number;
  readonly urlContains: string;
  readonly urlRegex: string;

  /**
   * 0-based (CDP).
   */
  readonly lineNumber: number;
  readonly columnNumber?: number | undefined;
  readonly condition?: string | undefined;

  // Mutable: re-bound on every reconnect (Gameface assigns a fresh CDP id per connection).
  cdpId?: string | undefined;

  /**
   * Where the engine bound it, flattened to `url:line:column` the moment it resolved.
   * Flattened rather than kept as CDP locations because a view reload re-parses every script under
   * fresh ids, which would leave a stored scriptId naming nothing.
   */
  resolved: string[];
}

/**
 * Escapes a literal string for embedding in a RegExp.
 * `Debugger.setBreakpointByUrl` matches by exact url or urlRegex only, so we implement the
 * friendlier "url contains" contract by escaping the needle into a regex.
 */
function escapeRegex(value: string): string {
  return value.replaceAll(/[.*+?^${}()|[\]\\]/gu, String.raw`\$&`);
}

/**
 * Tracks debugger state (scripts, breakpoints, current pause) across reconnects.
 * Enables Debugger on every connection and re-applies breakpoints.
 */
export class DebuggerSession {
  private readonly scripts = new Map<string, ScriptInfo>();
  private paused: PausedInfo | undefined;
  private pauseState: PauseState = 'none';
  private readonly breakpoints = new Map<number, LogicalBreakpoint>();
  private nextBpId = 1;

  // Increments on every `Debugger.paused` event, letting waiters detect a NEW pause (e.g., a step
  // that re-paused) rather than just "some pause is active".
  private pausedSeq = 0;

  private readonly client: DebuggerCdp;

  public constructor(client: DebuggerCdp, reloads: DebuggerReloads) {
    this.client = client;

    client.onConnect(conn => this.onConnect(conn));
    client.onEvent((method, params) => {
      this.handle(method, params as Record<string, unknown>);
    });

    // A view reload re-parses every script under fresh scriptIds; drop the stale ones so
    // game_debug_scripts lists only ids that are still resolvable.
    // Breakpoints need no such care: engine-side setBreakpointByUrl registrations survive a
    // same-connection reload and re-bind to the re-parsed script themselves (verified), so
    // re-applying here would register each breakpoint twice.
    reloads.onReload(() => {
      this.scripts.clear();
    });
  }

  /**
   * The current pause, or undefined while the UI runs.
   * Tools that a frozen frame loop would strand read this instead of owning debugger state.
   */
  public get pause(): PauseSnapshot | undefined {
    if (!this.paused) {
      return undefined;
    }

    return { reason: this.paused.reason, location: this.topLocation() };
  }

  public async status(setPause?: PauseState): Promise<CallToolResult> {
    try {
      await this.ensureReady();

      if (setPause) {
        this.pauseState = setPause;
        await this.client.call('Debugger.setPauseOnExceptions', { state: setPause });
      }

      // Both axes 1-based, as everywhere on the tool surface, and the column is reported because
      // it is what distinguishes a column-targeted breakpoint from a line-targeted one here.
      const breakpoints = [...this.breakpoints.values()].map(bp => ({
        id: bp.id,
        urlContains: bp.urlContains,
        line: bp.lineNumber + 1,
        column: bp.columnNumber == null ? undefined : bp.columnNumber + 1,
        condition: bp.condition,
        // Where the engine bound it, as url:line:column; empty means still pending.
        resolvedLocations: bp.resolved
      }));

      return text(
        JSON.stringify(
          {
            enabled: true,
            pauseOnExceptions: this.pauseState,
            paused: this.paused
              ? {
                  reason: this.paused.reason,
                  hitBreakpoints: this.paused.hitBreakpoints ?? [],
                  topFrame: this.topLocation(),
                  frames: this.paused.callFrames.length
                }
              : false,
            breakpoints,
            scriptCount: this.scripts.size
          },
          null,
          2
        )
      );
    } catch (error) {
      return toErrorResult(error);
    }
  }

  public async listScripts(filter?: string): Promise<CallToolResult> {
    try {
      await this.ensureReady();

      const scripts = this.filteredScripts(filter);

      // Keep tool output bounded; the filter parameter lets callers narrow past the cap.
      const cap = 120;
      const shown = scripts.slice(0, cap).map(script => ({
        scriptId: script.scriptId,
        url: script.url || '(anonymous)',
        // Both bounds are 0-based and inclusive, so the count takes the +1; that is what makes a
        // minified bundle report the 1 line it is.
        lines: script.endLine - script.startLine + 1,
        // Only an embedded script carries one, and its lines are numbered from there: without it a
        // caller reading the count alone would aim at the top of the document instead.
        firstLine: script.startLine == 0 ? undefined : script.startLine + 1
      }));

      return text(
        JSON.stringify(
          {
            total: scripts.length,
            shown: shown.length,
            truncated: scripts.length > cap,
            scripts: shown,
            note: this.listNote(scripts.length)
          },
          null,
          2
        )
      );
    } catch (error) {
      return toErrorResult(error);
    }
  }

  /**
   * Finds a literal string across the parsed sources, reporting each hit as a line:column the
   * breakpoint tool can take.
   * The one practical way to target a column in a minified bundle, whose single line no source
   * listing can render.
   */
  public async searchSource(query: string, urlContains?: string): Promise<CallToolResult> {
    try {
      await this.ensureReady();

      const scripts = this.filteredScripts(urlContains);
      const { matches, total, unreadable } = await this.collectMatches(scripts, query);

      // Four states arrive here as the same empty match list and call for different next moves:
      // reload the view, widen the url filter, reload again for fresh ids, or rethink the query.
      const missNote = this.missNote(scripts.length, unreadable);

      return text(
        JSON.stringify(
          {
            query,
            urlContains,
            scriptsSearched: urlContains == null ? undefined : scripts.length,
            total,
            returned: matches.length,
            truncated: total > matches.length,
            matches,
            note: total == 0 ? missNote : undefined,
            hint: total == 0 ? undefined : SEARCH_HINT
          },
          null,
          2
        )
      );
    } catch (error) {
      return toErrorResult(error);
    }
  }

  public async getSource(
    scriptId: string,
    lineStart?: number,
    lineEnd?: number,
    maxChars?: number
  ): Promise<CallToolResult> {
    try {
      await this.ensureReady();

      const source = await this.scriptSource(scriptId);

      if (source == null) {
        return errorText(`No source for scriptId ${scriptId}.`);
      }

      const lines = source.split('\n');

      // Callers speak resource lines, as the search, the breakpoints and the pause frames all do,
      // while getScriptSource hands back the body alone: shift the window by the script's own
      // offset, which is 0 for a whole .js file.
      const offset = this.scripts.get(scriptId)?.startLine ?? 0;
      const first = offset + 1;
      const last = offset + lines.length;

      // Tool lines are 1-based; cap the window so huge bundles stay digestible.
      const cap = 400;
      const from = lineStart && lineStart > first ? lineStart : first;

      // A line carried over from another script would otherwise render as an empty body under a
      // backwards range, which reads as "no code here" rather than "wrong script".
      if (from > last) {
        return errorText(oneLine`
          Script ${scriptId} spans lines ${first}-${last}, so there is nothing at line ${from}.
        `);
      }

      // Clamped to the script's own end, so a caller's open-ended lineEnd cannot report a window
      // wider than the source it came from.
      const to = Math.min(lineEnd && lineEnd >= from ? lineEnd : last, last, from + cap - 1);

      const padWidth = 5;
      const rendered = lines
        .slice(from - first, to - offset)
        .map((line, index) => `${String(from + index).padStart(padWidth)}  ${line}`)
        .join('\n');

      // The line window bounds nothing on a bundle whose single line is the whole module, so the
      // character budget is what decides what this costs its caller.
      const budget = maxChars ?? DEFAULT_SOURCE_MAX_CHARS;
      const clipped = rendered.length > budget;
      const body = clipped ? rendered.slice(0, budget) : rendered;

      // Counted off the body rather than the line window, which clipping can end long before:
      // a range naming lines the answer does not carry is worse than no range at all.
      const shownTo = from + body.replace(/\n$/u, '').split('\n').length - 1;
      const notes: string[] = [];

      if (shownTo < last || from > first) {
        notes.push(`showing lines ${from}-${shownTo} of ${last}`);
      }

      if (clipped) {
        notes.push(oneLine`
          clipped to ${budget} of ${rendered.length} characters: raise maxChars for more, or reach
          a position inside a minified line with game_debug_search_source
        `);
      }

      const note = notes.length > 0 ? `\n... ${notes.join('. ')}.` : '';

      return text(body + note);
    } catch (error) {
      return toErrorResult(error);
    }
  }

  public async setBreakpoint(
    urlContains: string,
    line: number,
    column?: number,
    condition?: string
  ): Promise<CallToolResult> {
    try {
      await this.ensureReady();

      // The tool is 1-based on both axes; CDP is 0-based on both.
      const lineNumber = Math.max(0, line - 1);
      const columnNumber = column == null ? undefined : Math.max(0, column - 1);
      const urlRegex = escapeRegex(urlContains);

      const res = await this.client.call<{ breakpointId: string; locations?: Location[] }>(
        'Debugger.setBreakpointByUrl',
        { urlRegex, lineNumber, columnNumber, condition }
      );

      const id = this.nextBpId++;
      const locations = res.locations ?? [];
      const resolved = locations.map(loc => this.locStr(loc));

      this.breakpoints.set(id, {
        id,
        urlContains,
        urlRegex,
        lineNumber,
        columnNumber,
        condition,
        cdpId: res.breakpointId,
        resolved
      });

      // A one-line script is a minified bundle, and the trap it sets is worth naming: the whole
      // module is line 1, so a line breakpoint binds to the first breakable location on it.
      const singleLine = locations.some(loc => {
        const script = this.scripts.get(loc.scriptId);

        return script != null && script.endLine == script.startLine;
      });

      return text(
        JSON.stringify(
          {
            id,
            urlContains,
            line,
            // Dropped when unset rather than sent as null, matching how the status listing reports
            // the same breakpoint: one shape per state, whichever tool the caller reads.
            column,
            condition,
            resolvedLocations: resolved,
            pending: locations.length == 0,
            note:
              locations.length == 0
                ? oneLine`
                    Pending: no matching script/line loaded yet, or the line has no code.
                    It will bind when the script loads.
                  `
                : oneLine`
                    Hitting this breakpoint FREEZES the UI until you resume
                    (game_debug_step resume).
                  `,
            hint: singleLine ? SINGLE_LINE_HINT : undefined
          },
          null,
          2
        )
      );
    } catch (error) {
      return toErrorResult(error);
    }
  }

  public async removeBreakpoint(target: string): Promise<CallToolResult> {
    try {
      await this.ensureReady();

      if (target == 'all') {
        for (const bp of this.breakpoints.values()) {
          await this.removeCdpBreakpoint(bp);
        }

        const count = this.breakpoints.size;
        this.breakpoints.clear();

        return text(`Removed all ${count} breakpoint(s).`);
      }

      const id = Number(target);
      const bp = this.breakpoints.get(id);

      if (!bp) {
        return errorText(`No breakpoint with id ${target}.`);
      }

      await this.removeCdpBreakpoint(bp);
      this.breakpoints.delete(id);

      return text(`Removed breakpoint ${id} (${bp.urlContains}:${bp.lineNumber + 1}).`);
    } catch (error) {
      return toErrorResult(error);
    }
  }

  public async pauseStateReport(expandScopes: boolean): Promise<CallToolResult> {
    try {
      await this.ensureReady();

      if (!this.paused) {
        return text(
          `Not paused (the UI is running). Set a breakpoint or use game_debug_step pause.`
        );
      }

      const frames: Array<Record<string, unknown>> = [];

      for (const [index, frame] of this.paused.callFrames.entries()) {
        const base: Record<string, unknown> = {
          index,
          function: frame.functionName || '(anonymous)',
          location: this.locStr(frame.location),
          scopes: frame.scopeChain.map(scope => scope.type)
        };

        if (expandScopes) {
          base.variables = await this.expandFrameScopes(frame);
        }

        frames.push(base);
      }

      return text(
        JSON.stringify(
          { reason: this.paused.reason, hitBreakpoints: this.paused.hitBreakpoints ?? [], frames },
          null,
          2
        )
      );
    } catch (error) {
      return toErrorResult(error);
    }
  }

  public async evaluate(expression: string, frameIndex = 0): Promise<CallToolResult> {
    try {
      await this.ensureReady();

      if (this.paused) {
        const frame = this.paused.callFrames[frameIndex];

        if (!frame) {
          return errorText(oneLine`
            No call frame at index ${frameIndex}
            (paused stack has ${this.paused.callFrames.length}).
          `);
        }

        const res = await this.client.call<EvaluateResult>('Debugger.evaluateOnCallFrame', {
          callFrameId: frame.callFrameId,
          expression,
          returnByValue: true,
          silent: true
        });

        if (res.exceptionDetails) {
          return errorText(`Eval threw: ${formatException(res.exceptionDetails)}`);
        }

        const value = describeRemoteObject(res.result);

        return text(typeof value == 'string' ? value : valToStr(value));
      }

      const res = await this.client.call<EvaluateResult>('Runtime.evaluate', {
        expression,
        returnByValue: true
      });

      if (res.exceptionDetails) {
        return errorText(`Eval threw: ${formatException(res.exceptionDetails)}`);
      }

      const value = describeRemoteObject(res.result);

      return text(typeof value == 'string' ? value : valToStr(value));
    } catch (error) {
      return toErrorResult(error);
    }
  }

  public async step(action: StepAction): Promise<CallToolResult> {
    try {
      await this.ensureReady();

      if (action == 'pause') {
        if (this.paused) {
          return text(`Already paused at ${this.topLocation()}.`);
        }

        const before = this.pausedSeq;

        await this.client.call('Debugger.pause');

        const paused = await this.waitForNewPause(before, PAUSE_WAIT_MS);

        return text(
          paused
            ? `Paused at ${this.topLocation()}.`
            : `Pause requested; nothing executed within ${PAUSE_WAIT_MS}ms (UI idle).`
        );
      }

      if (!this.paused) {
        return errorText(
          `Not paused. Set a breakpoint and trigger it, or use action 'pause' first.`
        );
      }

      if (action == 'resume') {
        await this.client.call('Debugger.resume');

        // Wait for the resumed event, and report what it says: a resume that leaves the UI paused
        // (another armed breakpoint on the resumed path) must not read as success, since the
        // caller's next move turns on whether the UI is running.
        await this.waitUntil(() => !this.paused, RESUME_WAIT_MS);

        if (this.paused) {
          return errorText(oneLine`
            Resume sent, but the UI is paused again at ${this.topLocation()}
            (reason: ${this.paused.reason}), so it is still frozen.
            Whatever armed that pause sits on the resumed path, so clear it before resuming again:
            game_debug_remove_breakpoint for a breakpoint, or game_debug_status
            setPauseOnExceptions none when the reason above is an exception.
          `);
        }

        return text(`Resumed (UI unfrozen).`);
      }

      const cdpMethod = { over: 'stepOver', into: 'stepInto', out: 'stepOut' }[action];
      const before = this.pausedSeq;

      await this.client.call(`Debugger.${cdpMethod}`);

      const repaused = await this.waitForNewPause(before, STEP_WAIT_MS);

      return text(
        repaused
          ? `Stepped (${action}). Now at ${this.topLocation()}.`
          : `Stepped (${action}); execution continued without re-pausing (UI resumed).`
      );
    } catch (error) {
      return toErrorResult(error);
    }
  }

  private async onConnect(conn: CdpConnectionHandle): Promise<void> {
    await conn.ensureDomain('Debugger');

    this.scripts.clear();
    this.paused = undefined;

    await conn.call('Debugger.setPauseOnExceptions', { state: this.pauseState });

    // Re-apply logical breakpoints: CDP breakpoint ids do not survive a reconnect.
    for (const bp of this.breakpoints.values()) {
      try {
        const res = await conn.call<{ breakpointId: string; locations?: Location[] }>(
          'Debugger.setBreakpointByUrl',
          {
            urlRegex: bp.urlRegex,
            lineNumber: bp.lineNumber,
            columnNumber: bp.columnNumber,
            condition: bp.condition
          }
        );

        bp.cdpId = res.breakpointId;
        bp.resolved = (res.locations ?? []).map(loc => this.locStr(loc));
      } catch {
        /* Leave the breakpoint pending on this connection. */
      }
    }
  }

  private handle(method: string, params: Record<string, unknown>): void {
    // Only scripts parsed from here on: Gameface does NOT replay scriptParsed for code the UI
    // loaded before `Debugger.enable` (verified), so a late attach starts with an empty map and
    // fills only as further scripts parse, a view reload being the way to force that.
    if (method == 'Debugger.scriptParsed') {
      this.scripts.set(params.scriptId as string, {
        scriptId: params.scriptId as string,
        url: (params.url as string) || '',
        startLine: (params.startLine as number) ?? 0,
        startColumn: (params.startColumn as number) ?? 0,
        endLine: (params.endLine as number) ?? 0,
        length: params.length as number | undefined
      });
    } else if (method == 'Debugger.paused') {
      this.paused = {
        reason: params.reason as string,
        hitBreakpoints: params.hitBreakpoints as string[] | undefined,
        callFrames: (params.callFrames as CallFrame[]) ?? []
      };
      this.pausedSeq++;
    } else if (method == 'Debugger.resumed') {
      this.paused = undefined;
    } else if (method == 'Debugger.breakpointResolved') {
      this.recordResolved(params);
    }
  }

  /**
   * Records where the engine bound a breakpoint that resolved after the call setting it answered.
   * A breakpoint aimed at a script parsed later binds only here, so without this the status listing
   * reports it pending for the rest of the session while it is in fact armed.
   */
  private recordResolved(params: Record<string, unknown>): void {
    const location = params.location as Location | undefined;
    const bp = [...this.breakpoints.values()].find(
      entry => entry.cdpId == (params.breakpointId as string)
    );

    if (location == null || bp == null) {
      return;
    }

    const at = this.locStr(location);

    if (!bp.resolved.includes(at)) {
      bp.resolved.push(at);
    }
  }

  private async ensureReady(): Promise<void> {
    await this.client.connection();
  }

  /**
   * The parsed scripts a caller's url substring selects, in a stable url order.
   */
  private filteredScripts(contains?: string): ScriptInfo[] {
    return [...this.scripts.values()]
      .filter(script => matchesUrl(script.url, contains))
      .toSorted((a, b) => a.url.localeCompare(b.url));
  }

  /**
   * Why the listing came back empty, which the total alone does not distinguish: nothing parsed on
   * this connection, or a filter matching none of what is.
   */
  private listNote(selected: number): string | undefined {
    if (this.scripts.size == 0) {
      return EMPTY_SCRIPT_MAP_NOTE;
    }

    // Nothing selected out of a non-empty map is the filter's doing: without one, every parsed
    // script is selected.
    return selected == 0 ? FILTER_MISS_LIST_NOTE : undefined;
  }

  /**
   * Tells a caller which of the four ways a search can come back empty they are looking at, since
   * the fix differs for each and the note is the field they act on.
   */
  private missNote(scriptsSearched: number, unreadable: number): string {
    if (this.scripts.size == 0) {
      return EMPTY_SCRIPT_MAP_NOTE;
    }

    if (scriptsSearched == 0) {
      return FILTER_MISS_NOTE;
    }

    // Every selected script came back unreadable, so the query never ran against a single source
    // and the note that calls the string absent would be answering a search nobody performed.
    return unreadable == scriptsSearched ? UNREADABLE_NOTE : NO_MATCH_NOTE;
  }

  /**
   * Scans the given scripts for a literal query, capping what it reports but not what it counts:
   * a truncated view passed off as the whole answer is worse than a visible cap.
   */
  private async collectMatches(
    scripts: readonly ScriptInfo[],
    query: string
  ): Promise<{ matches: Array<Record<string, unknown>>; total: number; unreadable: number }> {
    const matches: Array<Record<string, unknown>> = [];
    let total = 0;
    let unreadable = 0;

    for (const script of scripts) {
      let source: string | undefined;

      try {
        source = await this.scriptSource(script.scriptId);
      } catch {
        // A script the engine has dropped answers with a CDP error rather than an empty reply, and
        // one stale id must not discard the matches every other script already yielded.
        unreadable++;

        continue;
      }

      if (source == null) {
        unreadable++;

        continue;
      }

      let at = source.indexOf(query);

      while (at >= 0) {
        total++;

        if (matches.length < MAX_SEARCH_MATCHES) {
          matches.push({
            url: script.url || '(anonymous)',
            scriptId: script.scriptId,
            ...resourcePosition(script, positionAt(source, at)),
            snippet: snippetAt(source, at, query.length)
          });
        }

        at = source.indexOf(query, at + query.length);
      }
    }

    return { matches, total, unreadable };
  }

  /**
   * Fetches a script's source, or undefined when the engine has none for that id.
   */
  private async scriptSource(scriptId: string): Promise<string | undefined> {
    const res = await this.client.call<{ scriptSource?: string }>('Debugger.getScriptSource', {
      scriptId
    });

    return res?.scriptSource;
  }

  /**
   * Removes a breakpoint on the CDP side, best-effort (it may already be gone).
   */
  private async removeCdpBreakpoint(bp: LogicalBreakpoint): Promise<void> {
    if (bp.cdpId == null) {
      return;
    }

    try {
      await this.client.call('Debugger.removeBreakpoint', { breakpointId: bp.cdpId });
    } catch {
      /* Best-effort: the breakpoint may already be gone on this connection. */
    }
  }

  /**
   * Lists local/closure variables per scope of one paused call frame.
   */
  private async expandFrameScopes(frame: CallFrame): Promise<Record<string, string[]>> {
    const variables: Record<string, string[]> = {};

    for (const scope of frame.scopeChain) {
      if (scope.type != 'local' && scope.type != 'closure') {
        continue;
      }

      if (!scope.object?.objectId) {
        continue;
      }

      const props = await this.client.call<{
        result?: Array<{ name: string; value?: RemoteObject }>;
      }>('Runtime.getProperties', {
        objectId: scope.object.objectId,
        ownProperties: true,
        generatePreview: false
      });

      variables[scope.type] = (props.result ?? [])
        .slice(0, MAX_SCOPE_VARIABLES)
        .map(prop => `${prop.name} = ${valToStr(describeRemoteObject(prop.value))}`);
    }

    return variables;
  }

  /**
   * Polls until a pause newer than `beforeSeq` is observed, or the timeout elapses.
   */
  private waitForNewPause(beforeSeq: number, timeoutMs: number): Promise<boolean> {
    return this.waitUntil(() => this.pausedSeq > beforeSeq, timeoutMs);
  }

  /**
   * Polls until `done` holds, or the timeout elapses; reports whether it held.
   * Debugger state moves on CDP events rather than on a reply, so every wait here is a poll.
   */
  private async waitUntil(done: () => boolean, timeoutMs: number): Promise<boolean> {
    const deadline = Date.now() + timeoutMs;

    while (Date.now() < deadline) {
      if (done()) {
        return true;
      }

      await sleep(POLL_INTERVAL_MS);
    }

    return false;
  }

  /**
   * Flattens a CDP location to `url:line:column`.
   * The column is what tells a caller their line breakpoint landed on module-evaluation code
   * rather than in the function they meant, which no line alone can show on a minified bundle.
   */
  private locStr(loc: Location): string {
    const url = this.scripts.get(loc.scriptId)?.url ?? '';

    // A script absent from the map is one this connection never saw parsed, and a bare id in the
    // url's place reads as a filename; say what it is instead.
    const label = url.length > 0 ? url : `(unknown script ${loc.scriptId})`;

    // The +1s convert CDP's 0-based line and column to the 1-based ones the tools expose.
    return `${label}:${loc.lineNumber + 1}:${(loc.columnNumber ?? 0) + 1}`;
  }

  private topLocation(): string {
    const frame = this.paused?.callFrames[0];

    if (!frame) {
      return '(unknown)';
    }

    return `${frame.functionName || '(anonymous)'} at ${this.locStr(frame.location)}`;
  }
}

/**
 * Tests a script url against a caller's substring, matching everything when none is given.
 * Case-insensitive, and shared by every server-side url filter: the workflow copies a fragment
 * from one tool into the next, so a fragment that listed a script has to search it too.
 * The breakpoint tool is the exception it cannot follow, its urlContains going to the engine as a
 * CDP urlRegex, which has no case-insensitive form.
 */
function matchesUrl(url: string, contains?: string): boolean {
  if (contains == null) {
    return true;
  }

  return url.toLowerCase().includes(contains.toLowerCase());
}

/**
 * Rebases a position found in a script's own source onto the resource that carries the script.
 * Breakpoint lines, resolved locations and pause frames are all resource-based, since that is what
 * CDP speaks, while getScriptSource hands back the body alone.
 * The two agree for a whole .js file and differ by the offsets for a script embedded in a document,
 * where an unrebased line would set a breakpoint startLine lines above the code it found.
 * The column shifts only on the first line, the only one the script shares with what precedes it.
 */
function resourcePosition(
  script: ScriptInfo,
  position: { line: number; column: number }
): { line: number; column: number } {
  return {
    line: position.line + script.startLine,
    column: position.line == 1 ? position.column + script.startColumn : position.column
  };
}

/**
 * Locates a source offset as a 1-based line and column.
 */
function positionAt(source: string, index: number): { line: number; column: number } {
  let line = 1;
  let lineStart = 0;

  for (let at = source.indexOf('\n'); at >= 0 && at < index; at = source.indexOf('\n', at + 1)) {
    line++;
    lineStart = at + 1;
  }

  return { line, column: index - lineStart + 1 };
}

/**
 * Quotes a hit with its surrounding source, marking either side that was cut.
 */
function snippetAt(source: string, index: number, length: number): string {
  const from = Math.max(0, index - SEARCH_SNIPPET_MARGIN);
  const to = Math.min(source.length, index + length + SEARCH_SNIPPET_MARGIN);
  const head = from > 0 ? '…' : '';
  const tail = to < source.length ? '…' : '';

  return `${head}${source.slice(from, to)}${tail}`;
}
