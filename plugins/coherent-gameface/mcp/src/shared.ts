/**
 * Shared helpers for tool implementations: MCP result builders, error mapping, and the partial
 * CDP result shapes used across facets.
 */

import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { GameUnreachableError } from './cdp';

/**
 * A partial CDP RemoteObject (Runtime/Debugger).
 */
export interface RemoteObject {
  readonly type: string;
  readonly subtype?: string;
  readonly value?: unknown;
  readonly unserializableValue?: string;
  readonly description?: string;
  readonly className?: string;
  readonly objectId?: string;
}

/**
 * A partial CDP Runtime.evaluate / Debugger.evaluateOnCallFrame result.
 */
export interface EvaluateResult {
  readonly result: RemoteObject;
  readonly exceptionDetails?: {
    readonly text?: string;
    readonly exception?: RemoteObject;
    readonly lineNumber?: number;
    readonly columnNumber?: number;
  };
}

export function text(value: string): CallToolResult {
  return { content: [{ type: 'text', text: value }] };
}

export function errorText(value: string): CallToolResult {
  return { content: [{ type: 'text', text: value }], isError: true };
}

/**
 * Maps thrown errors to a clear, actionable MCP error result.
 */
export function toErrorResult(error: unknown): CallToolResult {
  if (error instanceof GameUnreachableError) {
    return errorText(error.message);
  }

  return errorText(`Gameface CDP error: ${error instanceof Error ? error.message : String(error)}`);
}

/**
 * Extracts a human-readable value from a CDP RemoteObject.
 */
export function describeRemoteObject(obj: RemoteObject | undefined): unknown {
  if (!obj) {
    return null;
  }

  // Presence check, not truthiness: `value` legitimately holds undefined, null, 0, or ''.
  if (Object.hasOwn(obj, 'value')) {
    return obj.value;
  }

  // NaN, Infinity, -0, and BigInts cannot cross JSON and arrive as this string instead.
  if (obj.unserializableValue != null) {
    return obj.unserializableValue;
  }

  return obj.description ?? `[${obj.type}${obj.subtype ? ` ${obj.subtype}` : ''}]`;
}

/**
 * Formats CDP exceptionDetails into one message line, preferring the thrown error's own
 * description over CDP's generic text.
 */
export function formatException(details: EvaluateResult['exceptionDetails']): string {
  const exception = details?.exception;
  const desc = exception?.description ?? exception?.value ?? details?.text ?? `unknown error`;

  return typeof desc == 'string' ? desc : JSON.stringify(desc);
}

/**
 * Best-effort stringification of an arbitrary value for tool output.
 */
export function valToStr(value: unknown): string {
  if (typeof value == 'string') {
    return value;
  }

  try {
    return JSON.stringify(value);
  } catch {
    // Circular structures and BigInts make JSON.stringify throw.
    return String(value);
  }
}
