/**
 * Direct Chrome DevTools Protocol (CDP) client for the Cities: Skylines II
 * Coherent Gameface UI engine.
 *
 * Gameface speaks CDP 1.3 but does NOT implement the browser-level handshake
 * Puppeteer/Playwright rely on (`Browser.getVersion` is missing, and
 * `Target.attachedToTarget` is never emitted). So we talk to the page target
 * directly over a raw WebSocket and only send the commands Gameface supports.
 */
import type { Config } from "./config";

/** A discovered CDP page target plus the WebSocket URL we build for it. */
export interface PageTarget {
  id: string;
  title: string;
  url: string;
  /** Built by us, NOT taken from /json/list (see discoverPageTarget). */
  wsUrl: string;
}

/** Raised when the game / debug endpoint cannot be reached at all. */
export class GameUnreachableError extends Error {
  constructor(cfg: Config, cause?: unknown) {
    super(
      `Cannot reach the Cities: Skylines II Gameface debug endpoint at ` +
        `http://${cfg.host}:${cfg.port}. Make sure the game is running with the ` +
        `Gameface debug port open. Override with CS2_GAMEFACE_HOST / CS2_GAMEFACE_PORT.`,
    );
    this.name = "GameUnreachableError";
    if (cause !== undefined) this.cause = cause;
  }
}

/** Raised for protocol-level failures once a connection exists. */
export class CdpError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "CdpError";
  }
}

/** Listener for CDP events (method + params), used by console capture. */
export type CdpEventListener = (method: string, params: unknown) => void;

/** Minimal connection surface handed to onConnect listeners (e.g. to enable domains). */
export interface CdpConnectionHandle {
  ensureDomain(domain: string): Promise<void>;
  call<T = unknown>(method: string, params?: Record<string, unknown>): Promise<T>;
  readonly target: PageTarget;
}

/** Called after each new connection is established (e.g. to (re)enable domains). */
export type CdpConnectListener = (conn: CdpConnectionHandle) => void | Promise<void>;

async function fetchWithTimeout(url: string, timeoutMs: number): Promise<Response> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetch(url, { signal: controller.signal });
  } finally {
    clearTimeout(timer);
  }
}

/**
 * Resolves the live page target via HTTP discovery. We deliberately ignore the
 * `webSocketDebuggerUrl` from /json/list because Gameface returns it relative to
 * the request path (e.g. ws://host:port/json/list/devtools/page/0, which is
 * wrong) and instead build the canonical ws://host:port/devtools/page/<id>.
 * Resolving at runtime (rather than hardcoding page/0) survives game restarts.
 */
export async function discoverPageTarget(cfg: Config): Promise<PageTarget> {
  const listUrl = `http://${cfg.host}:${cfg.port}/json/list`;
  let res: Response;
  try {
    res = await fetchWithTimeout(listUrl, cfg.connectTimeoutMs);
  } catch (err) {
    throw new GameUnreachableError(cfg, err);
  }
  if (!res.ok) {
    throw new GameUnreachableError(cfg, new Error(`HTTP ${res.status} from ${listUrl}`));
  }
  let list: unknown;
  try {
    list = await res.json();
  } catch (err) {
    throw new CdpError(`Malformed /json/list response from ${listUrl}: ${String(err)}`);
  }
  const targets = Array.isArray(list) ? (list as Array<Record<string, unknown>>) : [];
  const page = targets.find((t) => t?.type === "page" && t?.id != null);
  if (!page) {
    throw new CdpError(
      `No CDP 'page' target found at ${listUrl}. Targets: ${JSON.stringify(list)}`,
    );
  }
  return {
    id: String(page.id),
    title: String(page.title ?? ""),
    url: String(page.url ?? ""),
    wsUrl: `ws://${cfg.host}:${cfg.port}/devtools/page/${page.id}`,
  };
}

interface Pending {
  resolve: (value: unknown) => void;
  reject: (err: unknown) => void;
  timer: ReturnType<typeof setTimeout>;
}

/** A single live WebSocket connection to one page target. */
class CdpConnection {
  private nextId = 1;
  private readonly pending = new Map<number, Pending>();
  private readonly enabledDomains = new Set<string>();
  private closed = false;

  private constructor(
    private readonly ws: WebSocket,
    private readonly cfg: Config,
    readonly target: PageTarget,
    private readonly onEvent: CdpEventListener,
  ) {
    ws.addEventListener("message", (e) => this.handleMessage(e));
    ws.addEventListener("close", () => this.handleClose("connection closed"));
    ws.addEventListener("error", () => this.handleClose("connection error"));
  }

  /** Opens a connection and resolves once the socket is ready. */
  static async open(
    cfg: Config,
    target: PageTarget,
    onEvent: CdpEventListener,
  ): Promise<CdpConnection> {
    const ws = new WebSocket(target.wsUrl);
    await new Promise<void>((resolve, reject) => {
      const timer = setTimeout(() => {
        try {
          ws.close();
        } catch {
          /* ignore */
        }
        reject(new GameUnreachableError(cfg));
      }, cfg.connectTimeoutMs);
      ws.addEventListener(
        "open",
        () => {
          clearTimeout(timer);
          resolve();
        },
        { once: true },
      );
      ws.addEventListener(
        "error",
        (e) => {
          clearTimeout(timer);
          reject(new GameUnreachableError(cfg, e));
        },
        { once: true },
      );
    });
    return new CdpConnection(ws, cfg, target, onEvent);
  }

  get isOpen(): boolean {
    return !this.closed && this.ws.readyState === WebSocket.OPEN;
  }

  /** Sends a CDP command and resolves with its `result` (or rejects on error). */
  call<T = unknown>(method: string, params?: Record<string, unknown>): Promise<T> {
    if (!this.isOpen) {
      return Promise.reject(new CdpError("CDP connection is not open"));
    }
    const id = this.nextId++;
    return new Promise<T>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new CdpError(`CDP call '${method}' timed out after ${this.cfg.callTimeoutMs}ms`));
      }, this.cfg.callTimeoutMs);
      this.pending.set(id, { resolve: resolve as (v: unknown) => void, reject, timer });
      try {
        this.ws.send(JSON.stringify({ id, method, params: params ?? {} }));
      } catch (err) {
        clearTimeout(timer);
        this.pending.delete(id);
        reject(new CdpError(`Failed to send CDP call '${method}': ${String(err)}`));
      }
    });
  }

  /** Enables a CDP domain once per connection (e.g. Page, DOM). */
  async ensureDomain(domain: string): Promise<void> {
    if (this.enabledDomains.has(domain)) return;
    await this.call(`${domain}.enable`);
    this.enabledDomains.add(domain);
  }

  close(): void {
    this.handleClose("closed by client");
    try {
      this.ws.close();
    } catch {
      /* ignore */
    }
  }

  private handleMessage(event: MessageEvent): void {
    let msg: { id?: number; result?: unknown; error?: { code?: number; message?: string }; method?: string; params?: unknown };
    try {
      const raw = typeof event.data === "string" ? event.data : String(event.data);
      msg = JSON.parse(raw);
    } catch {
      return;
    }
    if (typeof msg.id === "number" && this.pending.has(msg.id)) {
      const entry = this.pending.get(msg.id)!;
      clearTimeout(entry.timer);
      this.pending.delete(msg.id);
      if (msg.error) {
        entry.reject(new CdpError(`CDP error (${msg.error.code ?? "?"}): ${msg.error.message ?? "unknown"}`));
      } else {
        entry.resolve(msg.result);
      }
      return;
    }
    if (typeof msg.method === "string") {
      try {
        this.onEvent(msg.method, msg.params);
      } catch {
        /* listener errors must not break the read loop */
      }
    }
  }

  private handleClose(reason: string): void {
    if (this.closed) return;
    this.closed = true;
    for (const { reject, timer } of this.pending.values()) {
      clearTimeout(timer);
      reject(new CdpError(`CDP ${reason}`));
    }
    this.pending.clear();
  }
}

/**
 * Manages discovery + a single live connection, reconnecting transparently when
 * the game restarts or the socket drops. Tools talk to this, not CdpConnection.
 */
export class CdpClient {
  private conn?: CdpConnection;
  private connecting?: Promise<CdpConnection>;
  private readonly listeners = new Set<CdpEventListener>();
  private readonly connectListeners = new Set<CdpConnectListener>();

  constructor(private readonly cfg: Config) {}

  get config(): Config {
    return this.cfg;
  }

  /** Subscribe to CDP events. Returns an unsubscribe function. */
  onEvent(listener: CdpEventListener): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  /** Run a callback after each new connection (e.g. to (re)enable domains). */
  onConnect(listener: CdpConnectListener): () => void {
    this.connectListeners.add(listener);
    return () => this.connectListeners.delete(listener);
  }

  /** HTTP-only discovery, without opening a WebSocket (used by game_status). */
  discover(): Promise<PageTarget> {
    return discoverPageTarget(this.cfg);
  }

  /** Returns the live connection, (re)connecting if needed. */
  async connection(): Promise<CdpConnection> {
    if (this.conn?.isOpen) return this.conn;
    if (this.connecting) return this.connecting;
    this.connecting = (async () => {
      const target = await discoverPageTarget(this.cfg);
      const conn = await CdpConnection.open(this.cfg, target, (m, p) => this.emit(m, p));
      this.conn = conn;
      for (const cb of this.connectListeners) {
        try {
          await cb(conn);
        } catch (err) {
          process.stderr.write(`gameface onConnect listener failed: ${String(err)}\n`);
        }
      }
      return conn;
    })();
    try {
      return await this.connecting;
    } finally {
      this.connecting = undefined;
    }
  }

  /** Sends a CDP command, retrying once if the connection dropped. */
  async call<T = unknown>(method: string, params?: Record<string, unknown>): Promise<T> {
    const conn = await this.connection();
    try {
      return await conn.call<T>(method, params);
    } catch (err) {
      if (err instanceof CdpError && /closed|not open/i.test(err.message)) {
        this.conn?.close();
        this.conn = undefined;
        const fresh = await this.connection();
        return await fresh.call<T>(method, params);
      }
      throw err;
    }
  }

  /** Ensures a CDP domain is enabled on the live connection. */
  async ensureDomain(domain: string): Promise<void> {
    const conn = await this.connection();
    await conn.ensureDomain(domain);
  }

  /** Returns the current target (connecting if needed). */
  async target(): Promise<PageTarget> {
    return (await this.connection()).target;
  }

  private emit(method: string, params: unknown): void {
    for (const listener of this.listeners) {
      try {
        listener(method, params);
      } catch {
        /* ignore */
      }
    }
  }
}
