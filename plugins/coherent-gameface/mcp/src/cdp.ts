/**
 * Direct Chrome DevTools Protocol (CDP) client for the Coherent Gameface UI engine.
 *
 * Gameface speaks CDP 1.3 but does NOT implement the browser-level handshake that Puppeteer and
 * Playwright rely on (`Browser.getVersion` is missing, and `Target.attachedToTarget` is never
 * emitted). So we talk to the page target directly over a raw WebSocket and only send the
 * commands Gameface supports.
 */

import { oneLine } from 'common-tags';
import type { Config } from './config';

/**
 * A discovered CDP page target plus the WebSocket URL we build for it.
 */
export interface PageTarget {
  readonly id: string;
  readonly title: string;
  readonly url: string;

  /**
   * Built by us, NOT taken from /json/list (see discoverPageTarget).
   */
  readonly wsUrl: string;
}

/**
 * Raised when the Gameface debug endpoint cannot be reached at all.
 */
export class GameUnreachableError extends Error {
  public constructor(cfg: Config, cause?: unknown) {
    // noinspection HttpUrlsUsage
    super(oneLine`
      Cannot reach the Gameface debug endpoint at http://${cfg.host}:${cfg.port}.
      Make sure the Gameface application is running with its CDP debug port open.
      Override with GAMEFACE_HOST / GAMEFACE_PORT.
    `);

    this.name = 'GameUnreachableError';

    // Only set when provided: an own `cause: undefined` property would still print as a cause.
    if (cause !== undefined) {
      this.cause = cause;
    }
  }
}

/**
 * Thrown for protocol-level failures once a connection exists.
 */
export class CdpError extends Error {
  public constructor(message: string) {
    super(message);

    this.name = 'CdpError';
  }
}

/**
 * Listener for CDP events (method + params), used by console capture.
 */
export type CdpEventListener = (method: string, params: unknown) => void;

/**
 * Minimal connection surface handed to onConnect listeners (e.g., to enable domains).
 */
export interface CdpConnectionHandle {
  readonly ensureDomain: (domain: string) => Promise<void>;
  readonly call: <T = unknown>(method: string, params?: Record<string, unknown>) => Promise<T>;
  readonly target: PageTarget;
}

/**
 * Called after each new connection is established (e.g., to (re)enable domains).
 */
export type CdpConnectListener = (conn: CdpConnectionHandle) => void | Promise<void>;

/**
 * One entry of the /json/list discovery response (only the fields we read).
 */
interface TargetListEntry {
  readonly id?: string | number;
  readonly type?: string;
  readonly title?: string;
  readonly url?: string;
}

/**
 * Fetches with a hard timeout; global fetch has no timeout option of its own.
 */
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
 * Resolves the live page target via HTTP discovery.
 * We deliberately ignore the `webSocketDebuggerUrl` from /json/list because Gameface returns it
 * relative to the request path (e.g., ws://host:port/json/list/devtools/page/0, which is wrong),
 * and instead build the canonical ws://host:port/devtools/page/<id>.
 * Resolving at runtime (rather than hardcoding page/0) survives game restarts.
 */
export async function discoverPageTarget(cfg: Config): Promise<PageTarget> {
  const listUrl = `http://${cfg.host}:${cfg.port}/json/list`;
  let res: Response;

  try {
    res = await fetchWithTimeout(listUrl, cfg.connectTimeoutMs);
  } catch (error) {
    throw new GameUnreachableError(cfg, error);
  }

  if (!res.ok) {
    throw new GameUnreachableError(cfg, new Error(`HTTP ${res.status} from ${listUrl}`));
  }

  let list: unknown;

  try {
    list = await res.json();
  } catch (error) {
    throw new CdpError(`Malformed /json/list response from ${listUrl}: ${String(error)}`);
  }

  const targets = Array.isArray(list) ? (list as Array<TargetListEntry | null>) : [];
  const page = targets.find(entry => entry != null && entry.type == 'page' && entry.id != null);

  if (page?.id == null) {
    throw new CdpError(
      `No CDP 'page' target found at ${listUrl}. Targets: ${JSON.stringify(list)}`
    );
  }

  const id = String(page.id);

  return {
    id,
    title: page.title ?? '',
    url: page.url ?? '',
    wsUrl: `ws://${cfg.host}:${cfg.port}/devtools/page/${id}`
  };
}

/**
 * Bookkeeping for one in-flight CDP command awaiting its response frame.
 */
interface Pending {
  readonly resolve: (value: unknown) => void;
  readonly reject: (error: unknown) => void;
  readonly timer: ReturnType<typeof setTimeout>;
}

/**
 * An incoming CDP frame: either a command response (id) or an event (method).
 */
interface CdpIncomingMessage {
  readonly id?: number;
  readonly result?: unknown;
  readonly error?: { readonly code?: number; readonly message?: string };
  readonly method?: string;
  readonly params?: unknown;
}

/**
 * A single live WebSocket connection to one page target.
 */
class CdpConnection {
  public readonly target: PageTarget;

  // Monotonic command id; CDP correlates a response frame to its request by it.
  private nextId = 1;
  private readonly pending = new Map<number, Pending>();
  private readonly enabledDomains = new Set<string>();
  private closed = false;

  private readonly ws: WebSocket;
  private readonly cfg: Config;
  private readonly onEvent: CdpEventListener;

  private constructor(ws: WebSocket, cfg: Config, target: PageTarget, onEvent: CdpEventListener) {
    this.ws = ws;
    this.cfg = cfg;
    this.target = target;
    this.onEvent = onEvent;

    ws.addEventListener('message', event => this.handleMessage(event));
    ws.addEventListener('close', () => this.handleClose(`connection closed`));
    ws.addEventListener('error', () => this.handleClose(`connection error`));
  }

  /**
   * Opens a connection and resolves once the socket is ready.
   */
  public static async open(
    cfg: Config,
    target: PageTarget,
    onEvent: CdpEventListener
  ): Promise<CdpConnection> {
    const ws = new WebSocket(target.wsUrl);

    // Await open or error, whichever fires first; both listeners are once-only, so whichever
    // loses the race becomes a no-op.
    await new Promise<void>((resolve, reject) => {
      const timer = setTimeout(() => {
        try {
          ws.close();
        } catch {
          /* Ignore. */
        }

        reject(new GameUnreachableError(cfg));
      }, cfg.connectTimeoutMs);

      ws.addEventListener(
        'open',
        () => {
          clearTimeout(timer);
          resolve();
        },
        { once: true }
      );

      ws.addEventListener(
        'error',
        event => {
          clearTimeout(timer);
          reject(new GameUnreachableError(cfg, event));
        },
        { once: true }
      );
    });

    return new CdpConnection(ws, cfg, target, onEvent);
  }

  public get isOpen(): boolean {
    return !this.closed && this.ws.readyState == WebSocket.OPEN;
  }

  /**
   * Sends a CDP command and resolves with its `result` (or rejects on error).
   */
  public call<T = unknown>(method: string, params?: Record<string, unknown>): Promise<T> {
    if (!this.isOpen) {
      return Promise.reject(new CdpError(`CDP connection is not open`));
    }

    const id = this.nextId++;

    return new Promise<T>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new CdpError(`CDP call '${method}' timed out after ${this.cfg.callTimeoutMs}ms`));
      }, this.cfg.callTimeoutMs);

      // The cast erases T from resolve; the response value is typed at the call site.
      this.pending.set(id, { resolve: resolve as (value: unknown) => void, reject, timer });

      try {
        this.ws.send(JSON.stringify({ id, method, params: params ?? {} }));
      } catch (error) {
        clearTimeout(timer);
        this.pending.delete(id);
        reject(new CdpError(`Failed to send CDP call '${method}': ${String(error)}`));
      }
    });
  }

  /**
   * Enables a CDP domain once per connection (e.g., Page, DOM).
   */
  public async ensureDomain(domain: string): Promise<void> {
    if (this.enabledDomains.has(domain)) {
      return;
    }

    await this.call(`${domain}.enable`);
    this.enabledDomains.add(domain);
  }

  public close(): void {
    this.handleClose(`closed by client`);

    try {
      this.ws.close();
    } catch {
      /* Ignore. */
    }
  }

  private handleMessage(event: MessageEvent): void {
    let msg: CdpIncomingMessage;

    try {
      const raw = typeof event.data == 'string' ? event.data : String(event.data);

      msg = JSON.parse(raw) as CdpIncomingMessage;
    } catch {
      // Drop frames that are not valid JSON.
      return;
    }

    // A frame with an id is a command response; a frame with a method is an event.
    if (typeof msg.id == 'number') {
      // A missing entry means the call already timed out; drop the late response.
      const entry = this.pending.get(msg.id);

      if (entry) {
        clearTimeout(entry.timer);
        this.pending.delete(msg.id);

        if (msg.error) {
          entry.reject(
            new CdpError(`CDP error (${msg.error.code ?? '?'}): ${msg.error.message ?? 'unknown'}`)
          );
        } else {
          entry.resolve(msg.result);
        }

        return;
      }
    }

    if (typeof msg.method == 'string') {
      try {
        this.onEvent(msg.method, msg.params);
      } catch {
        /* Listener errors must not break the read loop. */
      }
    }
  }

  private handleClose(reason: string): void {
    if (this.closed) {
      return;
    }

    this.closed = true;

    // Fail every in-flight call so awaiting tools error out instead of hanging until timeout.
    for (const { reject, timer } of this.pending.values()) {
      clearTimeout(timer);
      reject(new CdpError(`CDP ${reason}`));
    }

    this.pending.clear();
  }
}

/**
 * Manages discovery and a single live connection, reconnecting transparently when the Gameface
 * application restarts or the socket drops. Tools talk to this, not CdpConnection.
 */
export class CdpClient {
  private conn: CdpConnection | undefined;
  private connecting: Promise<CdpConnection> | undefined;
  private readonly listeners = new Set<CdpEventListener>();
  private readonly connectListeners = new Set<CdpConnectListener>();

  private readonly cfg: Config;

  public constructor(cfg: Config) {
    this.cfg = cfg;
  }

  public get config(): Config {
    return this.cfg;
  }

  /**
   * Subscribe to CDP events. Returns an unsubscribe function.
   */
  public onEvent(listener: CdpEventListener): () => void {
    this.listeners.add(listener);

    return () => {
      this.listeners.delete(listener);
    };
  }

  /**
   * Run a callback after each new connection (e.g., to (re)enable domains).
   */
  public onConnect(listener: CdpConnectListener): () => void {
    this.connectListeners.add(listener);

    return () => {
      this.connectListeners.delete(listener);
    };
  }

  /**
   * HTTP-only discovery, without opening a WebSocket (used by game_status).
   */
  public discover(): Promise<PageTarget> {
    return discoverPageTarget(this.cfg);
  }

  /**
   * Returns the live connection, (re)connecting if needed.
   */
  public async connection(): Promise<CdpConnection> {
    if (this.conn?.isOpen) {
      return this.conn;
    }

    // Single-flight: concurrent callers await the same in-progress attempt.
    if (this.connecting) {
      return this.connecting;
    }

    this.connecting = (async () => {
      const target = await discoverPageTarget(this.cfg);
      const conn = await CdpConnection.open(this.cfg, target, (method, params) => {
        this.emit(method, params);
      });

      this.conn = conn;

      for (const listener of this.connectListeners) {
        try {
          await listener(conn);
        } catch (error) {
          process.stderr.write(`gameface onConnect listener failed: ${String(error)}\n`);
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

  /**
   * Sends a CDP command, retrying once if the connection dropped.
   */
  public async call<T = unknown>(method: string, params?: Record<string, unknown>): Promise<T> {
    const conn = await this.connection();

    try {
      return await conn.call<T>(method, params);
    } catch (error) {
      // Both 'connection closed/error' (handleClose) and 'not open' (call) are our own CdpError
      // messages, so matching on them is reliable. One retry covers a game restart mid-session.
      if (error instanceof CdpError && /closed|not open/iu.test(error.message)) {
        this.conn?.close();
        this.conn = undefined;

        const fresh = await this.connection();

        return fresh.call<T>(method, params);
      }

      throw error;
    }
  }

  /**
   * Ensures a CDP domain is enabled on the live connection.
   */
  public async ensureDomain(domain: string): Promise<void> {
    const conn = await this.connection();

    await conn.ensureDomain(domain);
  }

  private emit(method: string, params: unknown): void {
    for (const listener of this.listeners) {
      try {
        listener(method, params);
      } catch {
        /* Ignore. */
      }
    }
  }
}
