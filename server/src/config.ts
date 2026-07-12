/**
 * Runtime configuration for the Gameface MCP server, sourced from environment variables so the
 * same bundle works against any Gameface application / debug port.
 */

const DEFAULT_PORT = 9444;
const DEFAULT_CONNECT_TIMEOUT_MS = 5000;
const DEFAULT_CALL_TIMEOUT_MS = 15_000;

export interface Config {
  /**
   * Host the Gameface CDP endpoint listens on.
   */
  readonly host: string;

  /**
   * Port the Gameface CDP endpoint listens on (common default: 9444).
   */
  readonly port: number;

  /**
   * How long to wait for HTTP discovery / WebSocket opening before giving up.
   */
  readonly connectTimeoutMs: number;

  /**
   * How long to wait for a single CDP command reply before giving up.
   */
  readonly callTimeoutMs: number;
}

/**
 * Builds the config from GAMEFACE_* env vars, falling back to defaults.
 */
export function loadConfig(): Config {
  return {
    host: str(process.env.GAMEFACE_HOST, 'localhost'),
    port: num(process.env.GAMEFACE_PORT, DEFAULT_PORT),
    connectTimeoutMs: num(process.env.GAMEFACE_CONNECT_TIMEOUT_MS, DEFAULT_CONNECT_TIMEOUT_MS),
    callTimeoutMs: num(process.env.GAMEFACE_CALL_TIMEOUT_MS, DEFAULT_CALL_TIMEOUT_MS)
  };
}

/**
 * Reads a string setting. An empty or whitespace-only env value counts as unset and falls back.
 */
function str(value: string | undefined, fallback: string): string {
  const trimmed = value?.trim() ?? '';

  return trimmed.length > 0 ? trimmed : fallback;
}

/**
 * Reads a positive-number setting. Missing, non-numeric, zero, or negative values fall back.
 */
function num(value: string | undefined, fallback: number): number {
  const parsed = Number(value);

  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}
