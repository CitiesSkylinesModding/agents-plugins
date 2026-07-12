/**
 * Runtime configuration for the Gameface MCP server, sourced from environment
 * variables so the same bundle works against any Gameface application / debug port.
 */
export interface Config {
  /** Host the Gameface CDP endpoint listens on. */
  host: string;
  /** Port the Gameface CDP endpoint listens on (common default: 9444). */
  port: number;
  /** How long to wait for HTTP discovery / WebSocket open before giving up. */
  connectTimeoutMs: number;
  /** How long to wait for a single CDP command reply before giving up. */
  callTimeoutMs: number;
}

function num(value: string | undefined, fallback: number): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

/** Builds the config from GAMEFACE_* env vars, falling back to defaults. */
export function loadConfig(): Config {
  return {
    host: process.env.GAMEFACE_HOST?.trim() || "localhost",
    port: num(process.env.GAMEFACE_PORT, 9444),
    connectTimeoutMs: num(process.env.GAMEFACE_CONNECT_TIMEOUT_MS, 5000),
    callTimeoutMs: num(process.env.GAMEFACE_CALL_TIMEOUT_MS, 15000),
  };
}
