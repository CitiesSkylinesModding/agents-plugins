/**
 * Console capture for the Gameface UI: buffers `console.*` calls, log entries, and uncaught
 * exceptions, expands object arguments into real values at capture time, and renders them in the
 * DevTools idiom.
 *
 * Expansion runs one `Runtime.callFunctionOn` per object argument, invoking the depth-capped,
 * cycle-safe serializer below (injected by source text, the way tools.ts injects its page
 * functions).
 * What it returns is stored as a tree, not a string, so a later read can render the same entry
 * deeper.
 * Nothing keeps an objectId: those die on a view reload and the buffer must never hold dead
 * references.
 */

import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { oneLine } from 'common-tags';
import type { CdpClient, CdpConnectListener, CdpEventListener } from './cdp';
import {
  type EvaluateResult,
  type RemoteObject,
  describeRemoteObject,
  formatException,
  text,
  toErrorResult,
  valToStr
} from './shared';

const DEFAULT_CONSOLE_LIMIT = 50;
const DEFAULT_MAX_ENTRIES = 500;
const MILLIS_DIGITS = 3;

/**
 * Above this, a CDP event timestamp reads as the epoch milliseconds the protocol specifies.
 */
const EPOCH_MS_FLOOR = 1e12;

/**
 * How deep the capture-time serializer walks.
 * The render depth is capped to this: beyond it the tree holds no data, only the marker saying so.
 */
export const CAPTURE_DEPTH_CAP = 4;

export const DEFAULT_RENDER_DEPTH = 2;

/**
 * Per-level cap on properties, elements and entries; the rest render as a `…N more` marker.
 */
const LEVEL_WIDTH = 20;

/**
 * Where a string inside an expanded value gets clipped, ellipsis included in the rendering.
 */
const STRING_CLIP = 200;

/**
 * How long one argument's serialization may take before the entry falls back to its preview.
 * A hung fetch must never hold an entry.
 * A paused debugger is not one: the engine answers `Runtime.callFunctionOn` while execution is
 * suspended.
 */
const DEFAULT_EXPAND_TIMEOUT_MS = 2000;

/**
 * How many captures may await serialization before further ones fall back to their preview.
 */
const DEFAULT_MAX_PENDING = 32;

/**
 * The slice of the CDP client the console pipeline needs.
 * Narrow on purpose: this is the one part of the server exercised without a live application.
 */
export interface ConsoleCdp {
  readonly onConnect: (listener: CdpConnectListener) => void;
  readonly onEvent: (listener: CdpEventListener) => void;
  readonly call: <T = unknown>(method: string, params?: Record<string, unknown>) => Promise<T>;
}

/**
 * The slice of the reload tracker the console pipeline needs.
 */
export interface ConsoleReloads {
  readonly onReload: (listener: (count: number) => void) => void;
}

export interface ConsoleBufferOptions {
  readonly max?: number | undefined;
  readonly maxPending?: number | undefined;
  readonly expandTimeoutMs?: number | undefined;
}

export interface ConsoleReadOptions {
  readonly limit?: number | undefined;
  readonly level?: string | undefined;
  readonly clear?: boolean | undefined;
  readonly depth?: number | undefined;
}

/**
 * An expanded value, as the page-side serializer produces it and the renderer consumes it.
 */
export type ConsoleValue =
  | { readonly kind: 'raw'; readonly text: string }
  | { readonly kind: 'string'; readonly text: string; readonly clipped?: boolean | undefined }
  | { readonly kind: 'node'; readonly text: string }
  | { readonly kind: 'circular' }
  | {
      readonly kind: 'object';
      readonly ctor?: string | undefined;
      readonly entries: ReadonlyArray<readonly [string, ConsoleValue]>;
      readonly more?: Overflow;
    }
  | {
      readonly kind: 'array';
      readonly items: readonly ConsoleValue[];
      readonly more?: Overflow;
    }
  | {
      readonly kind: 'map';
      readonly size: number;
      readonly pairs: ReadonlyArray<readonly [ConsoleValue, ConsoleValue]>;
      readonly more?: Overflow;
    }
  | {
      readonly kind: 'set';
      readonly size: number;
      readonly items: readonly ConsoleValue[];
      readonly more?: Overflow;
    }
  | ({ readonly kind: 'capped' } & Collapsed);

/**
 * A container shown without its contents, either because the capture cap stopped there or because
 * the rendered depth does.
 */
interface Collapsed {
  readonly of: 'object' | 'array' | 'map' | 'set';
  readonly ctor?: string | undefined;
  readonly size?: number | undefined;
}

/**
 * What a level cap dropped: a count, or `true` when only the fact is known.
 */
type Overflow = number | true | undefined;

interface ConsoleEntry {
  readonly at: number;
  readonly kind: string;
  readonly level: string;
  readonly args: readonly ConsoleValue[];
}

/**
 * A captured event awaiting its turn in the queue.
 * `expand` is decided on arrival, so a burst that saturates the queue degrades to previews instead
 * of piling up round trips.
 */
interface PendingCapture {
  readonly at: number;
  readonly kind: string;
  readonly level: string;
  readonly args: readonly PreviewedRemoteObject[];
  readonly expand: boolean;
}

/**
 * A console argument as the engine sends it: a RemoteObject, plus the shallow preview the engine
 * populates unprompted on `consoleAPICalled` args.
 */
interface PreviewedRemoteObject extends RemoteObject {
  readonly preview?: ObjectPreview;
}

interface ObjectPreview {
  readonly type?: string;
  readonly subtype?: string;
  readonly description?: string;
  readonly overflow?: boolean;
  readonly properties?: readonly PreviewProperty[];
  readonly entries?: readonly PreviewEntry[];
}

interface PreviewEntry {
  readonly key?: ObjectPreview;
  readonly value?: ObjectPreview;
}

interface PreviewProperty {
  readonly name: string;
  readonly type: string;
  readonly subtype?: string;
  readonly value?: string;
  readonly valuePreview?: ObjectPreview;
}

/**
 * Buffers console/log/exception events from the Gameface UI into a ring buffer.
 * Subscribes to CDP events and (re)enables `Runtime` and `Log` on every connection.
 * Also interleaves a synthetic entry per detected view reload, so log lines can be correlated with
 * the context reset that separates them.
 */
export class ConsoleBuffer {
  private readonly entries: ConsoleEntry[] = [];

  // Captures publish in arrival order even though serialization is asynchronous: one drains at a
  // time, so an argument resolving late cannot let a later entry overtake it.
  private readonly queue: PendingCapture[] = [];
  private isDraining = false;

  // Bumped by a clear, so a capture resolving afterwards cannot publish into the emptied buffer.
  private generation = 0;

  private readonly cdp: ConsoleCdp;
  private readonly max: number;
  private readonly maxPending: number;
  private readonly expandTimeoutMs: number;

  public constructor(
    cdp: ConsoleCdp,
    reloads?: ConsoleReloads,
    options: ConsoleBufferOptions = {}
  ) {
    this.cdp = cdp;
    this.max = options.max ?? DEFAULT_MAX_ENTRIES;
    this.maxPending = options.maxPending ?? DEFAULT_MAX_PENDING;
    this.expandTimeoutMs = options.expandTimeoutMs ?? DEFAULT_EXPAND_TIMEOUT_MS;

    cdp.onConnect(async conn => {
      await conn.ensureDomain('Runtime');
      await conn.ensureDomain('Log');
    });

    cdp.onEvent((method, params) => {
      this.handle(method, (params ?? {}) as Record<string, unknown>);
    });

    reloads?.onReload(count => {
      this.enqueueText('reload', 'info', `view reloaded (#${count})`);
    });
  }

  /**
   * Renders the matching entries as output lines, newest last.
   */
  public read(options: ConsoleReadOptions): string[] {
    const { limit = DEFAULT_CONSOLE_LIMIT, level, clear = false } = options;
    const depth = Math.min(Math.max(options.depth ?? DEFAULT_RENDER_DEPTH, 1), CAPTURE_DEPTH_CAP);

    const filtered = level ? this.entries.filter(entry => entry.level == level) : this.entries;

    // Keep the newest entries when the limit truncates.
    const taken = filtered.slice(-limit);

    // Clear before rendering, never after: the caller asked for an empty buffer and must get one
    // even if rendering an entry fails.
    if (clear) {
      this.clear();
    }

    return taken.map(entry => renderLine(entry, depth));
  }

  /**
   * Empties the buffer and voids what is still in flight.
   */
  private clear(): void {
    this.entries.length = 0;
    this.queue.length = 0;
    this.generation++;
  }

  private handle(method: string, params: Record<string, unknown>): void {
    if (method == 'Runtime.consoleAPICalled') {
      this.enqueue({
        at: entryTime(params.timestamp),
        kind: 'console',
        level: (params.type as string) ?? 'log',
        args: (params.args as PreviewedRemoteObject[]) ?? [],
        expand: this.expanding() < this.maxPending
      });
    } else if (method == 'Log.entryAdded') {
      const entry = (params.entry as Record<string, unknown>) ?? {};

      this.enqueueText(
        (entry.source as string) ?? 'log',
        (entry.level as string) ?? 'info',
        (entry.text as string) ?? '',
        entry.timestamp
      );
    } else if (method == 'Runtime.exceptionThrown') {
      this.enqueueText(
        'exception',
        'error',
        formatException(params.exceptionDetails as EvaluateResult['exceptionDetails']),
        params.timestamp
      );
    }
  }

  /**
   * Queues an entry whose text is already known, so it still publishes in arrival order relative
   * to the console entries waiting on serialization.
   */
  private enqueueText(kind: string, level: string, line: string, timestamp?: unknown): void {
    this.enqueue({
      at: entryTime(timestamp),
      kind,
      level,
      args: [{ type: 'string', value: line }],
      expand: false
    });
  }

  private enqueue(capture: PendingCapture): void {
    this.queue.push(capture);

    void this.drain();
  }

  /**
   * The backlog `maxPending` caps: captures still owing a round trip.
   * A burst of plain log lines owes none, and must not push a real object argument onto the
   * preview path.
   */
  private expanding(): number {
    return this.queue.filter(capture => capture.expand).length;
  }

  private async drain(): Promise<void> {
    if (this.isDraining) {
      return;
    }

    this.isDraining = true;

    try {
      for (let next = this.queue.shift(); next; next = this.queue.shift()) {
        const { generation } = this;
        const args = await Promise.all(next.args.map(arg => this.resolveArg(arg, next.expand)));

        // A clear while the round trip was in flight voided this capture: it belongs to a buffer
        // that no longer exists.
        if (generation == this.generation) {
          this.push({ at: next.at, kind: next.kind, level: next.level, args });
        }
      }
    } finally {
      this.isDraining = false;
    }
  }

  private async resolveArg(arg: PreviewedRemoteObject, expand: boolean): Promise<ConsoleValue> {
    // Only arguments the engine handed us a reference for can be expanded; primitives and strings
    // already carry their value.
    if (arg.objectId == null) {
      return describedValue(arg);
    }

    if (expand) {
      const tree = await this.expand(arg.objectId);

      if (tree) {
        return tree;
      }
    }

    // Degraded but never empty: the engine's own preview is still depth-1 better than the
    // RemoteObject description.
    return previewValue(arg.preview) ?? describedValue(arg);
  }

  private async expand(objectId: string): Promise<ConsoleValue | undefined> {
    try {
      const response = await withTimeout(
        this.cdp.call<EvaluateResult>('Runtime.callFunctionOn', {
          objectId,
          functionDeclaration: SERIALIZE_SOURCE,
          arguments: [{ value: CAPTURE_DEPTH_CAP }, { value: LEVEL_WIDTH }, { value: STRING_CLIP }],
          returnByValue: true
        }),
        this.expandTimeoutMs
      );

      const value = response.result?.value;

      if (response.exceptionDetails || value == null || typeof value != 'object') {
        return undefined;
      }

      return value as ConsoleValue;
    } catch {
      // Any failure is a fallback, never a lost entry: a dead objectId, a rejected command, a
      // connection drop, or the timeout above.
      return undefined;
    }
  }

  private push(entry: ConsoleEntry): void {
    this.entries.push(entry);

    // Ring-buffer behavior: drop the oldest entries beyond the cap.
    if (this.entries.length > this.max) {
      this.entries.splice(0, this.entries.length - this.max);
    }
  }
}

/**
 * Returns recent console/log/exception lines captured from the Gameface UI.
 */
export async function gameConsole(
  client: CdpClient,
  buffer: ConsoleBuffer,
  options: ConsoleReadOptions
): Promise<CallToolResult> {
  try {
    // Ensure a connection exists so Runtime/Log are enabled and capture is running.
    await client.connection();
  } catch (error) {
    return toErrorResult(error);
  }

  const lines = buffer.read(options);

  if (lines.length == 0) {
    return text(oneLine`
      No console entries captured yet.
      Capture begins once the server connects to the application;
      trigger some UI activity (or a game_eval console.log) and retry.
    `);
  }

  return text(lines.join('\n'));
}

/**
 * Epoch ms for an entry, from the CDP event timestamp when it is one.
 *
 * CDP 1.3 specifies epoch milliseconds, but Cohtml populates the field with milliseconds since
 * engine boot instead.
 * The plausibility gate keeps a spec-compliant engine exact and falls back to receive time
 * elsewhere, which costs nothing observable over a local socket.
 */
function entryTime(timestamp: unknown): number {
  const isEpoch = typeof timestamp == 'number' && timestamp > EPOCH_MS_FLOOR;

  return isEpoch ? timestamp : Date.now();
}

/**
 * Rejects once the deadline passes, so a page-side call that never answers cannot stall the queue.
 */
function withTimeout<T>(promise: Promise<T>, timeoutMs: number): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined;

  const deadline = new Promise<never>((_resolve, reject) => {
    timer = setTimeout(() => reject(new Error(`console expansion timed out`)), timeoutMs);
  });

  return Promise.race([promise, deadline]).finally(() => clearTimeout(timer));
}

// Page-context function.
// This runs inside the Gameface UI (never in this process); it is serialized with .toString() and
// injected into Runtime.callFunctionOn, which binds `this` to the object being expanded.
// Keep it a plain, self-contained browser JS with no reference to anything outside its body.
// Type annotations are fine: the build erases them before serialization.

/**
 * Walks the bound object into a depth-capped, cycle-safe tree.
 * `returnByValue` brings the tree back as structured data, the way the page functions in tools.ts
 * return theirs.
 */
function serializeConsoleArgFn(
  this: unknown,
  cap: number,
  width: number,
  clip: number
): ConsoleValue {
  const seen = new Set<unknown>();

  return walk(this, 0);

  function walk(value: unknown, depth: number): ConsoleValue {
    if (typeof value == 'string') {
      return value.length > clip
        ? { kind: 'string', text: value.slice(0, clip), clipped: true }
        : { kind: 'string', text: value };
    }

    if (value === null || typeof value != 'object') {
      return { kind: 'raw', text: scalarText(value) };
    }

    const node = value as Record<string, unknown>;

    // A DOM node renders as a descriptor, never as its property bag: walking one yields hundreds
    // of engine properties and nothing a reader wants.
    if (typeof node.nodeType == 'number') {
      return { kind: 'node', text: nodeText(node) };
    }

    if (value instanceof Date) {
      return { kind: 'raw', text: dateText(value) };
    }

    if (value instanceof RegExp || value instanceof Error) {
      return { kind: 'raw', text: String(value) };
    }

    if (seen.has(value)) {
      return { kind: 'circular' };
    }

    const shape = Array.isArray(value)
      ? 'array'
      : value instanceof Map
        ? 'map'
        : value instanceof Set
          ? 'set'
          : 'object';

    if (depth >= cap) {
      return {
        kind: 'capped',
        of: shape,
        // Only what the collapsed rendering shows: a name for a plain object, a size for a
        // keyed collection.
        ctor: shape == 'object' ? ctorName(value) : undefined,
        size: sizeOf(value, shape)
      };
    }

    seen.add(value);

    try {
      return expand(value, shape, depth);
    } finally {
      // Per-path, not global: the same object appearing twice side by side is not a cycle.
      seen.delete(value);
    }
  }

  function expand(value: object, shape: Collapsed['of'], depth: number): ConsoleValue {
    if (shape == 'array') {
      const all = value as unknown[];
      const items = all.slice(0, width).map(item => walk(item, depth + 1));

      return { kind: 'array', items, more: dropped(all.length, items.length) };
    }

    if (shape == 'map') {
      const all = Array.from((value as Map<unknown, unknown>).entries());
      const pairs = all
        .slice(0, width)
        .map(pair => [walk(pair[0], depth + 1), walk(pair[1], depth + 1)] as const);

      return { kind: 'map', size: all.length, pairs, more: dropped(all.length, pairs.length) };
    }

    if (shape == 'set') {
      const all = Array.from((value as Set<unknown>).values());
      const items = all.slice(0, width).map(item => walk(item, depth + 1));

      return { kind: 'set', size: all.length, items, more: dropped(all.length, items.length) };
    }

    const bag = value as Record<string, unknown>;
    const keys = Object.keys(bag);
    const entries = keys.slice(0, width).map(key => [key, readProperty(bag, key, depth)] as const);

    return {
      kind: 'object',
      ctor: ctorName(value),
      entries,
      more: dropped(keys.length, entries.length)
    };
  }

  function dropped(total: number, kept: number): Overflow {
    // Undefined drops the key from what crosses the socket, so an untruncated level stays compact.
    return total > kept ? total - kept : undefined;
  }

  function readProperty(bag: Record<string, unknown>, key: string, depth: number): ConsoleValue {
    try {
      return walk(bag[key], depth + 1);
    } catch {
      // A getter that throws must cost its own property, not the whole entry.
      return { kind: 'raw', text: '[unreadable]' };
    }
  }

  function scalarText(value: unknown): string {
    if (typeof value == 'function') {
      const named = value as { name?: string };

      return named.name ? `[Function: ${named.name}]` : `[Function (anonymous)]`;
    }

    if (typeof value == 'bigint') {
      return `${value}n`;
    }

    if (typeof value == 'symbol') {
      return value.toString();
    }

    return String(value);
  }

  function nodeText(node: Record<string, unknown>): string {
    const name = typeof node.tagName == 'string' ? node.tagName : String(node.nodeName);
    const id = typeof node.id == 'string' && node.id ? `#${node.id}` : '';
    const classes = typeof node.className == 'string' ? node.className.trim() : '';
    const suffix = classes ? `.${classes.split(/\s+/u).join('.')}` : '';

    return `<${name.toLowerCase()}${id}${suffix}>`;
  }

  function dateText(value: Date): string {
    try {
      return value.toISOString();
    } catch {
      // An invalid Date throws on toISOString.
      return String(value);
    }
  }

  function ctorName(value: object): string | undefined {
    const name = value.constructor?.name;

    return name && name != 'Object' ? name : undefined;
  }

  function sizeOf(value: object, shape: Collapsed['of']): number | undefined {
    // Only a keyed collection renders its size when collapsed.
    return shape == 'map' || shape == 'set' ? (value as Set<unknown>).size : undefined;
  }
}

/**
 * The serializer's source, injected verbatim into every expansion.
 * Derived once: the text is several kilobytes and identical on every call.
 */
const SERIALIZE_SOURCE = serializeConsoleArgFn.toString();

/**
 * Renders one entry as its output line: wall-clock stamp, level, kind, then the arguments.
 */
function renderLine(entry: ConsoleEntry, depth: number): string {
  const args = entry.args.map(arg => renderArgument(arg, depth));

  return `${formatClock(entry.at)} [${entry.level}] (${entry.kind}) ${args.join(' ')}`;
}

/**
 * One argument's rendering, or a marker in its place.
 * The value tree crossed a socket from the page, so a malformed one costs its own argument rather
 * than the line it sits on, which would otherwise take the message text down with it.
 */
function renderArgument(value: ConsoleValue, depth: number): string {
  try {
    return renderValue(value, depth);
  } catch {
    return `[unrenderable]`;
  }
}

/**
 * Local wall-clock time with milliseconds. The date is omitted: correlation is within a session.
 */
function formatClock(at: number): string {
  const date = new Date(at);
  const hours = String(date.getHours()).padStart(2, '0');
  const minutes = String(date.getMinutes()).padStart(2, '0');
  const seconds = String(date.getSeconds()).padStart(2, '0');
  const millis = String(date.getMilliseconds()).padStart(MILLIS_DIGITS, '0');

  return `${hours}:${minutes}:${seconds}.${millis}`;
}

/**
 * Renders an expanded value, `depth` being how many container levels may still expand.
 * Everything the rendering leaves out is marked: `{…}` / `[…]` for a collapsed container,
 * `…N more` for what a level cap dropped, a trailing ellipsis inside a clipped string.
 */
function renderValue(value: ConsoleValue, depth: number): string {
  switch (value.kind) {
    case 'raw':
    case 'node': {
      return value.text;
    }

    case 'string': {
      // Line breaks are escaped rather than emitted: an entry is one line, and a raw break would
      // put the tail of the string in the read with no stamp in front of it.
      const held = value.text.replaceAll('\r', String.raw`\r`).replaceAll('\n', String.raw`\n`);

      return `'${held}${value.clipped ? '…' : ''}'`;
    }

    case 'circular': {
      return `[Circular]`;
    }

    case 'capped': {
      return collapsed(value);
    }

    case 'object': {
      if (depth <= 0) {
        return collapsed({ of: 'object', ctor: value.ctor });
      }

      const parts = value.entries.map(
        ([key, held]) => `${renderKey(key)}: ${renderValue(held, depth - 1)}`
      );

      return `${value.ctor ? `${value.ctor} ` : ''}{${joinParts(parts, value.more)}}`;
    }

    case 'array': {
      if (depth <= 0) {
        return collapsed({ of: 'array' });
      }

      return `[${joinParts(
        value.items.map(item => renderValue(item, depth - 1)),
        value.more
      )}]`;
    }

    case 'map': {
      if (depth <= 0) {
        return collapsed({ of: 'map', size: value.size });
      }

      const parts = value.pairs.map(
        ([key, held]) => `${renderValue(key, depth - 1)} => ${renderValue(held, depth - 1)}`
      );

      return `Map(${value.size}) {${joinParts(parts, value.more)}}`;
    }

    case 'set': {
      if (depth <= 0) {
        return collapsed({ of: 'set', size: value.size });
      }

      return `Set(${value.size}) {${joinParts(
        value.items.map(item => renderValue(item, depth - 1)),
        value.more
      )}}`;
    }

    default: {
      const unreachable: never = value;

      throw new Error(`Unhandled console value ${JSON.stringify(unreachable)}`);
    }
  }
}

/**
 * The collapsed form of a container: what it is, plus the marker saying its contents are not here.
 */
function collapsed(value: Collapsed): string {
  if (value.of == 'array') {
    return `[…]`;
  }

  if (value.of == 'map' || value.of == 'set') {
    const label = value.of == 'map' ? 'Map' : 'Set';

    return `${label}(${value.size ?? '?'}) {…}`;
  }

  return `${value.ctor ? `${value.ctor} ` : ''}{…}`;
}

function joinParts(parts: readonly string[], more: Overflow): string {
  const marked = more == null ? parts : [...parts, more === true ? `…more` : `…${more} more`];

  return marked.join(', ');
}

/**
 * Object keys render bare when they read as identifiers, quoted otherwise.
 */
function renderKey(key: string): string {
  return /^[A-Za-z_$][\w$]*$/u.test(key) ? key : `'${key}'`;
}

/**
 * Converts the engine's shallow preview into the same tree shape, one level deep.
 * This is the fallback path: it never round-trips, so it is what a failed, timed-out or
 * queue-saturated capture renders from.
 *
 * It is the second producer of a `ConsoleValue`, kept in step with `serializeConsoleArgFn`'s rules
 * by hand: that one is injected into the page as source text and can reach nothing in this module,
 * so clipping, constructor labels and container collapse are written twice on purpose.
 * Nothing live exercises this path either, since expansion succeeds against a healthy engine, so a
 * change here is only ever proven by the unit tests.
 */
function previewValue(preview: ObjectPreview | undefined): ConsoleValue | undefined {
  if (!preview) {
    return undefined;
  }

  const scalar = previewScalar(preview);

  if (scalar) {
    return scalar;
  }

  if (preview.subtype == 'array') {
    const items = (preview.properties ?? []).map(property => previewProperty(property));
    const size = previewSize(preview.description) ?? items.length;

    return { kind: 'array', items, more: previewOverflow(preview, items.length, size) };
  }

  if (preview.subtype == 'set') {
    const items = (preview.entries ?? []).map(entry => previewValue(entry.value) ?? unknownValue());
    const size = previewSize(preview.description) ?? items.length;

    return { kind: 'set', size, items, more: previewOverflow(preview, items.length, size) };
  }

  if (preview.subtype == 'map') {
    const pairs = (preview.entries ?? []).map(entry => {
      const key = previewValue(entry.key) ?? unknownValue();

      return [key, previewValue(entry.value) ?? unknownValue()] as const;
    });

    const size = previewSize(preview.description) ?? pairs.length;

    return { kind: 'map', size, pairs, more: previewOverflow(preview, pairs.length, size) };
  }

  if (isStringified(preview)) {
    return { kind: 'raw', text: oneLineText(preview.description ?? preview.subtype ?? 'object') };
  }

  const label = preview.description;
  const entries = (preview.properties ?? []).map(
    property => [property.name, previewProperty(property)] as const
  );

  // A subtyped container states its real length in its description (`Int8Array(100)`), where a
  // plain object's is a constructor name and a trailing `(9)` would fabricate a count.
  const size = preview.subtype ? previewSize(preview.description) : undefined;

  return {
    kind: 'object',
    ctor: label && label != 'Object' ? label : undefined,
    entries,
    more: previewOverflow(preview, entries.length, size)
  };
}

/**
 * A preview with nothing to walk: a primitive carrying its stringified value, or an object the
 * engine handed over as a DOM node.
 */
function previewScalar(preview: ObjectPreview): ConsoleValue | undefined {
  if (preview.type && preview.type != 'object') {
    return preview.type == 'string'
      ? clippedString(preview.description ?? '')
      : { kind: 'raw', text: oneLineText(preview.description ?? preview.type) };
  }

  if (preview.subtype == 'node') {
    return { kind: 'node', text: preview.description ?? 'node' };
  }

  return undefined;
}

/**
 * Whether a preview arrived already stringified, with nothing under it left to render.
 * Only the array, set and map branches above read a preview's contents, so a subtype listing no
 * properties has none this side can show: a null, a date, a regexp, an error, and the collections
 * whose members the engine keeps in `entries` (a weak map, a weak set) all land here, where a
 * container rendering would assert emptiness the value does not have.
 * A typed array, a promise and a generator do list real properties, and are walked instead.
 */
function isStringified(preview: ObjectPreview): boolean {
  return Boolean(preview.subtype) && !preview.properties?.length;
}

function previewProperty(property: PreviewProperty): ConsoleValue {
  if (property.valuePreview) {
    return previewValue(property.valuePreview) ?? unknownValue();
  }

  if (property.type == 'string') {
    return clippedString(property.value ?? '');
  }

  if (property.type == 'object') {
    return previewObjectProperty(property);
  }

  // An empty value counts as absent, not as an empty rendering: the engine sends one for a
  // function-typed property, where the type at least names what is there.
  const described = property.value?.length ? property.value : property.type;

  return { kind: 'raw', text: oneLineText(described) };
}

/**
 * A previewed object property.
 * A container collapses, since the preview names it without describing its contents; every other
 * subtype (a null, a date, a regexp, an error) arrives already stringified and reads as that text,
 * where a collapsed marker would claim contents it does not have.
 */
function previewObjectProperty(property: PreviewProperty): ConsoleValue {
  const { subtype, value } = property;

  if (subtype == 'array' || subtype == 'map' || subtype == 'set') {
    // The value states the size (`Map(3)`), which is what a collapsed collection renders.
    return { kind: 'capped', of: subtype, size: previewSize(value) };
  }

  if (subtype == 'node') {
    return { kind: 'node', text: value ?? 'node' };
  }

  if (subtype) {
    return { kind: 'raw', text: oneLineText(value ?? subtype) };
  }

  const named = value && value != 'Object';

  return { kind: 'capped', of: 'object', ctor: named ? value : undefined };
}

/**
 * What a preview left out: the difference where a real size is known, and the engine's own
 * overflow flag otherwise.
 * The size is passed in rather than read here, since only a container's description states one:
 * a plain object's is its constructor name, where a trailing `(9)` would fabricate a count.
 */
function previewOverflow(preview: ObjectPreview, shown: number, size = shown): Overflow {
  if (size > shown) {
    return size - shown;
  }

  return preview.overflow ? true : undefined;
}

/**
 * The size a preview states in its own description, as `Set(100)` and `Map(3)` state it.
 * The entry list beside it holds only what the engine chose to preview.
 */
function previewSize(description: string | undefined): number | undefined {
  const size = /\((?<size>\d+)\)$/u.exec(description ?? '')?.groups?.size;

  return size == null ? undefined : Number(size);
}

/**
 * A previewed string, clipped and marked the way the page-side serializer clips its own.
 */
function clippedString(held: string): ConsoleValue {
  return held.length > STRING_CLIP
    ? { kind: 'string', text: held.slice(0, STRING_CLIP), clipped: true }
    : { kind: 'string', text: held };
}

/**
 * A stringified value flattened onto one line and clipped.
 * An entry is one line, and an error's description carries its whole stack, which would otherwise
 * land in the middle of the read with no stamp in front of it.
 */
function oneLineText(held: string): string {
  // Cut at a line break rather than collapsing whitespace: the spaces inside a regexp or a message
  // are part of the value, while what follows the break is a stack.
  // The first non-blank line, since a description built from a template literal opens with one.
  const flat = held
    .split(/\r\n|[\r\n]/u)
    .map(line => line.trim())
    .find(line => line.length);

  return clipText(flat ?? '');
}

/**
 * Clips to the string budget, marking the cut.
 */
function clipText(held: string): string {
  return held.length > STRING_CLIP ? `${held.slice(0, STRING_CLIP)}…` : held;
}

/**
 * The last-resort rendering of an argument: whatever the RemoteObject itself says it is.
 */
function describedValue(arg: RemoteObject): ConsoleValue {
  return { kind: 'raw', text: valToStr(describeRemoteObject(arg)) };
}

function unknownValue(): ConsoleValue {
  return { kind: 'capped', of: 'object' };
}
