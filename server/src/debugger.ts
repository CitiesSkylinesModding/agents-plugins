/**
 * JS debugger facet: drives the Gameface UI's V8 Debugger domain (verified present: breakpoints,
 * paused events, evaluateOnCallFrame, stepping).
 *
 * IMPORTANT: hitting a breakpoint or pausing FREEZES the UI thread until you resume.
 * Keep pauses short, prefer conditional breakpoints, and always resume.
 * While paused, inspect with game_debug_evaluate (evaluateOnCallFrame), not game_eval
 * (Runtime.evaluate can block on the paused context).
 */

import { setTimeout as sleep } from 'node:timers/promises';
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { oneLine } from 'common-tags';
import type { CdpClient, CdpConnectionHandle } from './cdp';
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
 * How long to let the engine replay scriptParsed events after (re)connect.
 */
const SCRIPT_REPLAY_WAIT_MS = 350;

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

type PauseState = 'none' | 'uncaught' | 'all';
type StepAction = 'resume' | 'over' | 'into' | 'out' | 'pause';

interface ScriptInfo {
  readonly scriptId: string;
  readonly url: string;
  readonly startLine: number;
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
  locations: Location[];
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

  private readonly client: CdpClient;

  public constructor(client: CdpClient) {
    this.client = client;

    client.onConnect(conn => this.onConnect(conn));
    client.onEvent((method, params) => {
      this.handle(method, params as Record<string, unknown>);
    });
  }

  public async status(setPause?: PauseState): Promise<CallToolResult> {
    try {
      await this.ensureReady();

      if (setPause) {
        this.pauseState = setPause;
        await this.client.call('Debugger.setPauseOnExceptions', { state: setPause });
      }

      const breakpoints = [...this.breakpoints.values()].map(bp => ({
        id: bp.id,
        urlContains: bp.urlContains,
        line: bp.lineNumber + 1,
        condition: bp.condition,
        // Number of concrete script locations the breakpoint bound to (0 = still pending).
        resolved: bp.locations.length
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

      const needle = filter?.toLowerCase();
      let scripts = [...this.scripts.values()];

      if (needle) {
        scripts = scripts.filter(script => script.url.toLowerCase().includes(needle));
      }

      scripts.sort((a, b) => a.url.localeCompare(b.url));

      // Keep tool output bounded; the filter parameter lets callers narrow past the cap.
      const cap = 120;
      const shown = scripts.slice(0, cap).map(script => ({
        scriptId: script.scriptId,
        url: script.url || '(anonymous)',
        lines: script.endLine || undefined
      }));

      return text(
        JSON.stringify(
          {
            total: scripts.length,
            shown: shown.length,
            truncated: scripts.length > cap,
            scripts: shown
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
    lineEnd?: number
  ): Promise<CallToolResult> {
    try {
      await this.ensureReady();

      const res = await this.client.call<{ scriptSource?: string }>('Debugger.getScriptSource', {
        scriptId
      });
      const source = res?.scriptSource;

      if (source == null) {
        return errorText(`No source for scriptId ${scriptId}.`);
      }

      const lines = source.split('\n');

      // Tool lines are 1-based; cap the window so huge bundles stay digestible.
      const from = lineStart && lineStart > 0 ? lineStart : 1;
      const cap = 400;
      const to =
        lineEnd && lineEnd >= from
          ? Math.min(lineEnd, from + cap - 1)
          : Math.min(lines.length, from + cap - 1);

      const padWidth = 5;
      const slice = lines
        .slice(from - 1, to)
        .map((line, index) => `${String(from + index).padStart(padWidth)}  ${line}`)
        .join('\n');
      const note =
        to < lines.length || from > 1
          ? `\n... showing lines ${from}-${to} of ${lines.length}.`
          : '';

      return text(slice + note);
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

      // The tool is 1-based; CDP is 0-based.
      const lineNumber = Math.max(0, line - 1);
      const urlRegex = escapeRegex(urlContains);

      const res = await this.client.call<{ breakpointId: string; locations?: Location[] }>(
        'Debugger.setBreakpointByUrl',
        { urlRegex, lineNumber, columnNumber: column, condition }
      );

      const id = this.nextBpId++;
      const locations = res.locations ?? [];

      this.breakpoints.set(id, {
        id,
        urlContains,
        urlRegex,
        lineNumber,
        columnNumber: column,
        condition,
        cdpId: res.breakpointId,
        locations
      });

      return text(
        JSON.stringify(
          {
            id,
            urlContains,
            line,
            condition: condition ?? null,
            resolvedLocations: locations.map(loc => this.locStr(loc)),
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
                  `
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
          'Not paused (the UI is running). Set a breakpoint or use game_debug_step pause.'
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
          "Not paused. Set a breakpoint and trigger it, or use action 'pause' first."
        );
      }

      if (action == 'resume') {
        await this.client.call('Debugger.resume');

        // Wait for the resumed event so local pause state is accurate before reporting.
        const deadline = Date.now() + RESUME_WAIT_MS;

        while (Date.now() < deadline && this.paused) {
          await sleep(POLL_INTERVAL_MS);
        }

        return text('Resumed (UI unfrozen).');
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
        bp.locations = res.locations ?? [];
      } catch {
        /* Leave the breakpoint pending on this connection. */
      }
    }
  }

  private handle(method: string, params: Record<string, unknown>): void {
    // The engine replays scriptParsed for already-loaded scripts right after `Debugger.enable`,
    // which is how this map fills up on (re)connect.
    if (method == 'Debugger.scriptParsed') {
      this.scripts.set(params.scriptId as string, {
        scriptId: params.scriptId as string,
        url: (params.url as string) || '',
        startLine: (params.startLine as number) ?? 0,
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
    }
  }

  /**
   * Ensures a connection (which enables Debugger) and lets scripts replay.
   */
  private async ensureReady(): Promise<void> {
    await this.client.connection();

    if (this.scripts.size == 0) {
      await sleep(SCRIPT_REPLAY_WAIT_MS);
    }
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
  private async waitForNewPause(beforeSeq: number, timeoutMs: number): Promise<boolean> {
    const deadline = Date.now() + timeoutMs;

    while (Date.now() < deadline) {
      if (this.pausedSeq > beforeSeq) {
        return true;
      }

      await sleep(POLL_INTERVAL_MS);
    }

    return false;
  }

  private locStr(loc: Location): string {
    const url = this.scripts.get(loc.scriptId)?.url ?? '';
    const label = url.length > 0 ? url : loc.scriptId;

    // The +1 converts CDP's 0-based line to the 1-based lines the tools expose.
    return `${label}:${loc.lineNumber + 1}`;
  }

  private topLocation(): string {
    const frame = this.paused?.callFrames[0];

    if (!frame) {
      return '(unknown)';
    }

    return `${frame.functionName || '(anonymous)'} at ${this.locStr(frame.location)}`;
  }
}
