/**
 * JS debugger facet: drives the Gameface UI's V8 Debugger domain (verified
 * present: breakpoints, paused events, evaluateOnCallFrame, stepping).
 *
 * IMPORTANT: hitting a breakpoint or pausing FREEZES the UI thread until you
 * resume. Keep pauses short, prefer conditional breakpoints, and always resume.
 * While paused, inspect with game_debug_evaluate (evaluateOnCallFrame), not
 * game_eval (Runtime.evaluate can block on the paused context).
 */
import type { CdpClient, CdpConnectionHandle } from "./cdp";
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
import type { CallToolResult } from "@modelcontextprotocol/sdk/types.js";

type PauseState = "none" | "uncaught" | "all";
type StepAction = "resume" | "over" | "into" | "out" | "pause";

interface ScriptInfo {
  scriptId: string;
  url: string;
  startLine: number;
  endLine: number;
  length?: number;
}

interface Location {
  scriptId: string;
  lineNumber: number;
  columnNumber?: number;
}

interface CallFrame {
  callFrameId: string;
  functionName: string;
  location: Location;
  url: string;
  scopeChain: Array<{ type: string; name?: string; object: RemoteObject }>;
  this?: RemoteObject;
}

interface PausedInfo {
  reason: string;
  hitBreakpoints?: string[];
  callFrames: CallFrame[];
}

interface LogicalBreakpoint {
  id: number;
  urlContains: string;
  urlRegex: string;
  lineNumber: number; // 0-based (CDP)
  columnNumber?: number;
  condition?: string;
  cdpId?: string;
  locations: Location[];
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function escapeRegex(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/**
 * Tracks debugger state (scripts, breakpoints, current pause) across reconnects.
 * Enables Debugger on every connection and re-applies breakpoints.
 */
export class DebuggerSession {
  private readonly scripts = new Map<string, ScriptInfo>();
  private paused: PausedInfo | null = null;
  private pauseState: PauseState = "none";
  private readonly breakpoints = new Map<number, LogicalBreakpoint>();
  private nextBpId = 1;
  private pausedSeq = 0;

  constructor(private readonly client: CdpClient) {
    client.onConnect((conn) => this.onConnect(conn));
    client.onEvent((method, params) => this.handle(method, params as Record<string, unknown>));
  }

  private async onConnect(conn: CdpConnectionHandle): Promise<void> {
    await conn.ensureDomain("Debugger");
    this.scripts.clear();
    this.paused = null;
    await conn.call("Debugger.setPauseOnExceptions", { state: this.pauseState });
    for (const bp of this.breakpoints.values()) {
      try {
        const res = await conn.call<{ breakpointId: string; locations?: Location[] }>(
          "Debugger.setBreakpointByUrl",
          {
            urlRegex: bp.urlRegex,
            lineNumber: bp.lineNumber,
            columnNumber: bp.columnNumber,
            condition: bp.condition,
          },
        );
        bp.cdpId = res.breakpointId;
        bp.locations = res.locations ?? [];
      } catch {
        /* leave the breakpoint pending on this connection */
      }
    }
  }

  private handle(method: string, params: Record<string, unknown>): void {
    if (method === "Debugger.scriptParsed") {
      this.scripts.set(params.scriptId as string, {
        scriptId: params.scriptId as string,
        url: (params.url as string) || "",
        startLine: (params.startLine as number) ?? 0,
        endLine: (params.endLine as number) ?? 0,
        length: params.length as number | undefined,
      });
    } else if (method === "Debugger.paused") {
      this.paused = {
        reason: params.reason as string,
        hitBreakpoints: params.hitBreakpoints as string[] | undefined,
        callFrames: (params.callFrames as CallFrame[]) ?? [],
      };
      this.pausedSeq++;
    } else if (method === "Debugger.resumed") {
      this.paused = null;
    }
  }

  /** Ensures a connection (which enables Debugger) and lets scripts replay. */
  private async ensureReady(): Promise<void> {
    await this.client.connection();
    if (this.scripts.size === 0) await sleep(350);
  }

  private locStr(loc: Location): string {
    const url = this.scripts.get(loc.scriptId)?.url || loc.scriptId;
    return `${url}:${loc.lineNumber + 1}`;
  }

  private topLocation(): string {
    const f = this.paused?.callFrames[0];
    if (!f) return "(unknown)";
    return `${f.functionName || "(anonymous)"} at ${this.locStr(f.location)}`;
  }

  async status(setPause?: PauseState): Promise<CallToolResult> {
    try {
      await this.ensureReady();
      if (setPause) {
        this.pauseState = setPause;
        await this.client.call("Debugger.setPauseOnExceptions", { state: setPause });
      }
      const breakpoints = [...this.breakpoints.values()].map((b) => ({
        id: b.id,
        urlContains: b.urlContains,
        line: b.lineNumber + 1,
        condition: b.condition,
        resolved: b.locations.length,
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
                  frames: this.paused.callFrames.length,
                }
              : false,
            breakpoints,
            scriptCount: this.scripts.size,
          },
          null,
          2,
        ),
      );
    } catch (err) {
      return toErrorResult(err);
    }
  }

  async listScripts(filter?: string): Promise<CallToolResult> {
    try {
      await this.ensureReady();
      const needle = filter?.toLowerCase();
      let scripts = [...this.scripts.values()];
      if (needle) scripts = scripts.filter((s) => s.url.toLowerCase().includes(needle));
      scripts.sort((a, b) => a.url.localeCompare(b.url));
      const cap = 120;
      const shown = scripts.slice(0, cap).map((s) => ({
        scriptId: s.scriptId,
        url: s.url || "(anonymous)",
        lines: s.endLine || undefined,
      }));
      return text(
        JSON.stringify(
          {
            total: scripts.length,
            shown: shown.length,
            truncated: scripts.length > cap,
            scripts: shown,
          },
          null,
          2,
        ),
      );
    } catch (err) {
      return toErrorResult(err);
    }
  }

  async getSource(scriptId: string, lineStart?: number, lineEnd?: number): Promise<CallToolResult> {
    try {
      await this.ensureReady();
      const res = await this.client.call<{ scriptSource?: string }>("Debugger.getScriptSource", {
        scriptId,
      });
      const source = res?.scriptSource;
      if (source == null) return errorText(`No source for scriptId ${scriptId}.`);
      const lines = source.split("\n");
      const from = lineStart && lineStart > 0 ? lineStart : 1;
      const cap = 400;
      const to = lineEnd && lineEnd >= from ? Math.min(lineEnd, from + cap - 1) : Math.min(lines.length, from + cap - 1);
      const slice = lines
        .slice(from - 1, to)
        .map((l, i) => `${String(from + i).padStart(5)}  ${l}`)
        .join("\n");
      const note =
        to < lines.length || from > 1
          ? `\n... showing lines ${from}-${to} of ${lines.length}.`
          : "";
      return text(slice + note);
    } catch (err) {
      return toErrorResult(err);
    }
  }

  async setBreakpoint(
    urlContains: string,
    line: number,
    column?: number,
    condition?: string,
  ): Promise<CallToolResult> {
    try {
      await this.ensureReady();
      const lineNumber = Math.max(0, line - 1); // tool is 1-based; CDP is 0-based
      const urlRegex = escapeRegex(urlContains);
      const res = await this.client.call<{ breakpointId: string; locations?: Location[] }>(
        "Debugger.setBreakpointByUrl",
        { urlRegex, lineNumber, columnNumber: column, condition },
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
        locations,
      });
      return text(
        JSON.stringify(
          {
            id,
            urlContains,
            line,
            condition: condition ?? null,
            resolvedLocations: locations.map((l) => this.locStr(l)),
            pending: locations.length === 0,
            note:
              locations.length === 0
                ? "Pending: no matching script/line loaded yet, or the line has no code. It will bind when the script loads."
                : "Hitting this breakpoint FREEZES the UI until you resume (game_debug_step resume).",
          },
          null,
          2,
        ),
      );
    } catch (err) {
      return toErrorResult(err);
    }
  }

  async removeBreakpoint(target: string): Promise<CallToolResult> {
    try {
      await this.ensureReady();
      if (target === "all") {
        for (const bp of this.breakpoints.values()) {
          if (bp.cdpId) await this.client.call("Debugger.removeBreakpoint", { breakpointId: bp.cdpId }).catch(() => {});
        }
        const n = this.breakpoints.size;
        this.breakpoints.clear();
        return text(`Removed all ${n} breakpoint(s).`);
      }
      const id = Number(target);
      const bp = this.breakpoints.get(id);
      if (!bp) return errorText(`No breakpoint with id ${target}.`);
      if (bp.cdpId) await this.client.call("Debugger.removeBreakpoint", { breakpointId: bp.cdpId }).catch(() => {});
      this.breakpoints.delete(id);
      return text(`Removed breakpoint ${id} (${bp.urlContains}:${bp.lineNumber + 1}).`);
    } catch (err) {
      return toErrorResult(err);
    }
  }

  async pauseStateReport(expandScopes: boolean): Promise<CallToolResult> {
    try {
      await this.ensureReady();
      if (!this.paused) return text("Not paused (the UI is running). Set a breakpoint or use game_debug_step pause.");
      const frames = [];
      for (let i = 0; i < this.paused.callFrames.length; i++) {
        const f = this.paused.callFrames[i];
        const base: Record<string, unknown> = {
          index: i,
          function: f.functionName || "(anonymous)",
          location: this.locStr(f.location),
          scopes: f.scopeChain.map((s) => s.type),
        };
        if (expandScopes) {
          const vars: Record<string, string[]> = {};
          for (const scope of f.scopeChain) {
            if (scope.type !== "local" && scope.type !== "closure") continue;
            if (!scope.object?.objectId) continue;
            const props = await this.client.call<{ result?: Array<{ name: string; value?: RemoteObject }> }>(
              "Runtime.getProperties",
              { objectId: scope.object.objectId, ownProperties: true, generatePreview: false },
            );
            vars[scope.type] = (props.result ?? [])
              .slice(0, 50)
              .map((p) => `${p.name} = ${valToStr(describeRemoteObject(p.value))}`);
          }
          base.variables = vars;
        }
        frames.push(base);
      }
      return text(
        JSON.stringify(
          { reason: this.paused.reason, hitBreakpoints: this.paused.hitBreakpoints ?? [], frames },
          null,
          2,
        ),
      );
    } catch (err) {
      return toErrorResult(err);
    }
  }

  async evaluate(expression: string, frameIndex = 0): Promise<CallToolResult> {
    try {
      await this.ensureReady();
      if (this.paused) {
        const frame = this.paused.callFrames[frameIndex];
        if (!frame) {
          return errorText(
            `No call frame at index ${frameIndex} (paused stack has ${this.paused.callFrames.length}).`,
          );
        }
        const res = await this.client.call<EvaluateResult>("Debugger.evaluateOnCallFrame", {
          callFrameId: frame.callFrameId,
          expression,
          returnByValue: true,
          silent: true,
        });
        if (res.exceptionDetails) return errorText(`Eval threw: ${formatException(res.exceptionDetails)}`);
        const value = describeRemoteObject(res.result);
        return text(typeof value === "string" ? value : valToStr(value));
      }
      const res = await this.client.call<EvaluateResult>("Runtime.evaluate", {
        expression,
        returnByValue: true,
      });
      if (res.exceptionDetails) return errorText(`Eval threw: ${formatException(res.exceptionDetails)}`);
      const value = describeRemoteObject(res.result);
      return text(typeof value === "string" ? value : valToStr(value));
    } catch (err) {
      return toErrorResult(err);
    }
  }

  private async waitForNewPause(beforeSeq: number, timeoutMs: number): Promise<boolean> {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      if (this.pausedSeq > beforeSeq) return true;
      await sleep(50);
    }
    return false;
  }

  async step(action: StepAction): Promise<CallToolResult> {
    try {
      await this.ensureReady();
      if (action === "pause") {
        if (this.paused) return text(`Already paused at ${this.topLocation()}.`);
        const before = this.pausedSeq;
        await this.client.call("Debugger.pause");
        const ok = await this.waitForNewPause(before, 3000);
        return text(ok ? `Paused at ${this.topLocation()}.` : "Pause requested; nothing executed within 3s (UI idle).");
      }
      if (!this.paused) {
        return errorText("Not paused. Set a breakpoint and trigger it, or use action 'pause' first.");
      }
      if (action === "resume") {
        await this.client.call("Debugger.resume");
        const deadline = Date.now() + 2000;
        while (Date.now() < deadline && this.paused) await sleep(50);
        return text("Resumed (UI unfrozen).");
      }
      const cdpMethod = { over: "stepOver", into: "stepInto", out: "stepOut" }[action];
      const before = this.pausedSeq;
      await this.client.call(`Debugger.${cdpMethod}`);
      const ok = await this.waitForNewPause(before, 2000);
      return text(
        ok
          ? `Stepped (${action}). Now at ${this.topLocation()}.`
          : `Stepped (${action}); execution continued without re-pausing (UI resumed).`,
      );
    } catch (err) {
      return toErrorResult(err);
    }
  }
}
