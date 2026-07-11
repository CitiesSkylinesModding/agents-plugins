/**
 * Shared helpers for tool implementations: MCP result builders, error mapping,
 * and the partial CDP result shapes used across facets.
 */
import type { CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import { GameUnreachableError } from "./cdp";

/** A partial CDP RemoteObject (Runtime/Debugger). */
export interface RemoteObject {
  type: string;
  subtype?: string;
  value?: unknown;
  unserializableValue?: string;
  description?: string;
  className?: string;
  objectId?: string;
}

/** A partial CDP Runtime.evaluate / Debugger.evaluateOnCallFrame result. */
export interface EvaluateResult {
  result: RemoteObject;
  exceptionDetails?: {
    text?: string;
    exception?: RemoteObject;
    lineNumber?: number;
    columnNumber?: number;
  };
}

export function text(value: string): CallToolResult {
  return { content: [{ type: "text", text: value }] };
}

export function errorText(value: string): CallToolResult {
  return { content: [{ type: "text", text: value }], isError: true };
}

/** Maps thrown errors to a clear, actionable MCP error result. */
export function toErrorResult(err: unknown): CallToolResult {
  if (err instanceof GameUnreachableError) return errorText(err.message);
  return errorText(`Gameface CDP error: ${err instanceof Error ? err.message : String(err)}`);
}

export function describeRemoteObject(o: RemoteObject | undefined): unknown {
  if (!o) return null;
  if (Object.prototype.hasOwnProperty.call(o, "value")) return o.value;
  if (o.unserializableValue !== undefined) return o.unserializableValue;
  return o.description ?? `[${o.type}${o.subtype ? ` ${o.subtype}` : ""}]`;
}

export function formatException(details: EvaluateResult["exceptionDetails"]): string {
  const ex = details?.exception;
  const desc = ex?.description ?? ex?.value ?? details?.text ?? "unknown error";
  return typeof desc === "string" ? desc : JSON.stringify(desc);
}

export function valToStr(v: unknown): string {
  if (typeof v === "string") return v;
  try {
    return JSON.stringify(v);
  } catch {
    return String(v);
  }
}
