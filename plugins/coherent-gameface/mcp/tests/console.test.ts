import { afterAll, beforeEach, describe, expect, setSystemTime, test } from 'bun:test';
import {
  type ConsoleBufferOptions,
  type ConsoleCdp,
  type ConsoleReadOptions,
  type ConsoleReloads,
  ConsoleBuffer
} from '../src/console';

/**
 * Receive time is frozen for every test, so a line that falls back to it renders predictably.
 */
const RECEIVED_AT = new Date(2026, 7, 10, 9, 5, 1, 7);
const AT = `09:05:01.007`;

beforeEach(() => {
  setSystemTime(RECEIVED_AT);
});

afterAll(() => {
  setSystemTime();
});

/**
 * Stand-in for the CDP client at the ConsoleBuffer's injected seam: feeds synthetic events and
 * answers commands, with no socket and no application.
 */
class FakeCdp implements ConsoleCdp {
  public readonly calls: Array<{ method: string; params: Record<string, unknown> }> = [];

  /**
   * How `Runtime.callFunctionOn` behaves: run the injected serializer against the registered
   * object (the honest path), fail, or never resolve.
   */
  public mode: 'run' | 'fail' | 'hang' = 'run';

  /**
   * How long a given objectId's serialization takes to come back, in ms.
   */
  public readonly delays = new Map<string, number>();

  private readonly objects = new Map<string, unknown>();

  private readonly eventListeners: Array<(method: string, params: unknown) => void> = [];

  public onConnect(): void {
    // The buffer enables its domains here; nothing connects in a hermetic test.
  }

  public onEvent(listener: (method: string, params: unknown) => void): void {
    this.eventListeners.push(listener);
  }

  /**
   * Registers a live in-process object under an objectId and returns the console argument
   * referring to it, preview included.
   */
  public register(
    objectId: string,
    value: unknown,
    preview?: Record<string, unknown>
  ): Record<string, unknown> {
    this.objects.set(objectId, value);

    return { type: 'object', objectId, description: 'Object', ...(preview ? { preview } : {}) };
  }

  public async call<T = unknown>(method: string, params: Record<string, unknown> = {}): Promise<T> {
    this.calls.push({ method, params });

    if (this.mode == 'hang') {
      return new Promise<T>(() => {
        // Never settles: the buffer's own timeout has to be what ends the wait.
      });
    }

    if (this.mode == 'fail' || method != 'Runtime.callFunctionOn') {
      throw new Error(`FakeCdp refuses ${method}`);
    }

    const objectId = params.objectId as string;

    await Bun.sleep(this.delays.get(objectId) ?? 0);

    // The injected serializer is self-contained page JS, so it runs verbatim in-process against
    // the registered object, exactly as the engine would run it against the real one.
    const source = params.functionDeclaration as string;
    const args = (params.arguments as Array<{ value: unknown }>).map(arg => arg.value);

    // oxlint-disable-next-line no-implied-eval, no-new-func, typescript/no-unsafe-call -- Running the injected source is the point: it is what the engine does with it.
    const fn = new Function(`return (${source});`)() as (...rest: unknown[]) => unknown;

    return { result: { type: 'object', value: fn.apply(this.objects.get(objectId), args) } } as T;
  }

  public emit(method: string, params: unknown): void {
    for (const listener of this.eventListeners) {
      listener(method, params);
    }
  }
}

/**
 * A buffer wired to a fresh fake, so no test states the constructor's shape.
 */
function buffered(options: ConsoleBufferOptions & { readonly reloads?: ConsoleReloads } = {}): {
  cdp: FakeCdp;
  buffer: ConsoleBuffer;
} {
  const cdp = new FakeCdp();

  return { cdp, buffer: new ConsoleBuffer(cdp, options.reloads, options) };
}

/**
 * Publishing is asynchronous (the capture queue serializes object arguments), so tests wait for
 * the entries to reach the buffer rather than assuming they are there on the next tick.
 */
async function readWhen(
  buffer: ConsoleBuffer,
  count: number,
  options: ConsoleReadOptions = {}
): Promise<string[]> {
  // Counted rather than clock-bounded: tests freeze the clock, which would hang a wall-clock wait.
  for (let attempt = 0; attempt < 2000; attempt++) {
    const lines = buffer.read({ limit: 1000, ...options });

    if (lines.length >= count) {
      return lines;
    }

    await Bun.sleep(1);
  }

  return buffer.read({ limit: 1000, ...options });
}

/**
 * A `Runtime.consoleAPICalled` payload, shaped like the ones the reference engine emits.
 */
function consoleEvent(
  args: ReadonlyArray<Record<string, unknown>>,
  overrides: Record<string, unknown> = {}
): Record<string, unknown> {
  return { type: 'log', timestamp: 60_422_604, args, ...overrides };
}

describe(`timestamps`, () => {
  test(`renders an epoch-plausible CDP timestamp as local wall-clock time`, async () => {
    const { cdp, buffer } = buffered();

    // 2026-08-10T12:32:07.123 local time, as epoch ms.
    const at = new Date(2026, 7, 10, 14, 32, 7, 123).getTime();

    cdp.emit(
      'Runtime.consoleAPICalled',
      consoleEvent([{ type: 'string', value: 'hello' }], { timestamp: at })
    );

    expect(await readWhen(buffer, 1)).toEqual([`14:32:07.123 [log] (console) hello`]);
  });

  test(`falls back to receive time on a boot-relative or absent timestamp`, async () => {
    const { cdp, buffer } = buffered();

    // The reference engine sends ms since boot here, not epoch ms.
    cdp.emit('Runtime.consoleAPICalled', consoleEvent([{ type: 'string', value: 'boot' }]));

    cdp.emit(
      'Runtime.consoleAPICalled',
      consoleEvent([{ type: 'string', value: 'none' }], { timestamp: undefined })
    );

    expect(await readWhen(buffer, 2)).toEqual([
      `${AT} [log] (console) boot`,
      `${AT} [log] (console) none`
    ]);
  });
});

describe(`object expansion`, () => {
  test(`renders an expanded object in the DevTools idiom at the default depth`, async () => {
    const { cdp, buffer } = buffered();

    const state = { a: 1, b: { c: { d: 4 } }, arr: [1, 2, 3] };

    cdp.emit(
      'Runtime.consoleAPICalled',
      consoleEvent([{ type: 'string', value: 'state' }, cdp.register('1', state)])
    );

    expect(await readWhen(buffer, 1)).toEqual([
      `${AT} [log] (console) state {a: 1, b: {c: {…}}, arr: [1, 2, 3]}`
    ]);
  });

  test(`re-reads the same entry deeper, up to the capture cap`, async () => {
    const { cdp, buffer } = buffered();

    const deep = { l1: { l2: { l3: { l4: { l5: 5 } } } } };

    cdp.emit('Runtime.consoleAPICalled', consoleEvent([cdp.register('1', deep)]));

    await readWhen(buffer, 1);

    expect(buffer.read({ depth: 1 })).toEqual([`${AT} [log] (console) {l1: {…}}`]);
    expect(buffer.read({ depth: 2 })).toEqual([`${AT} [log] (console) {l1: {l2: {…}}}`]);
    // Depth 4 is the capture cap: the innermost marker is what the serializer stored, and asking
    // for more cannot reach past it.
    expect(buffer.read({ depth: 4 })).toEqual([
      `${AT} [log] (console) {l1: {l2: {l3: {l4: {…}}}}}`
    ]);
    expect(buffer.read({ depth: 9 })).toEqual(buffer.read({ depth: 4 }));
  });

  test(`marks every truncation it applies`, async () => {
    const { cdp, buffer } = buffered();

    const wide: Record<string, number> = {};

    for (let index = 0; index < 23; index++) {
      wide[`k${index}`] = index;
    }

    const cyclic: Record<string, unknown> = { name: 'x'.repeat(205) };

    cyclic.self = cyclic;

    cdp.emit(
      'Runtime.consoleAPICalled',
      consoleEvent([cdp.register('1', { wide, cyclic, list: Array.from({ length: 22 }, () => 0) })])
    );

    const [line] = await readWhen(buffer, 1, { depth: 3 });

    const zeroes = Array.from({ length: 20 }, () => '0').join(', ');

    expect(line).toContain(`k19: 19, …3 more`);
    expect(line).toContain(`list: [${zeroes}, …2 more]`);
    expect(line).toContain(`name: '${'x'.repeat(200)}…'`);
    expect(line).toContain(`self: [Circular]`);
  });

  test(`escapes a line break inside a string so the entry stays one line`, async () => {
    const { cdp, buffer } = buffered();

    cdp.emit('Runtime.consoleAPICalled', consoleEvent([cdp.register('1', { s: 'one\ntwo' })]));

    expect(await readWhen(buffer, 1)).toEqual([`${AT} [log] (console) {s: 'one\\ntwo'}`]);
  });

  test(`renders arrays, Maps, Sets and DOM nodes in the browser-console idiom`, async () => {
    const { cdp, buffer } = buffered();

    const value = {
      map: new Map<unknown, unknown>([['a', 1]]),
      set: new Set([1, 2]),
      // Duck-typed the way the serializer detects a node, since there is no DOM in-process.
      node: { nodeType: 1, tagName: 'DIV', id: 'panel', className: 'row active' },
      when: new Date(Date.UTC(2026, 7, 10, 12, 0, 0)),
      fn: function named(): void {},
      re: /ab+c/gu
    };

    cdp.emit('Runtime.consoleAPICalled', consoleEvent([cdp.register('1', value)]));

    const [line] = await readWhen(buffer, 1, { depth: 2 });

    expect(line).toBe(
      `${AT} [log] (console) {map: Map(1) {'a' => 1}, set: Set(2) {1, 2}, ` +
        `node: <div#panel.row.active>, when: 2026-08-10T12:00:00.000Z, ` +
        `fn: [Function: named], re: /ab+c/gu}`
    );
  });
});

/**
 * The shallow preview the engine populates on console arguments unprompted, and what the buffer
 * renders from when it cannot expand the object itself.
 */
const previewOf = {
  type: 'object',
  description: 'Object',
  properties: [
    { name: 'hp', type: 'number', value: '3' },
    { name: 'label', type: 'string', value: 'ok' },
    { name: 'inner', type: 'object', value: 'Object' }
  ]
};

const previewLine = `${AT} [log] (console) {hp: 3, label: 'ok', inner: {…}}`;

describe(`preview fallback`, () => {
  test(`renders from the preview when serialization fails`, async () => {
    const { cdp, buffer } = buffered();

    cdp.mode = 'fail';
    cdp.emit('Runtime.consoleAPICalled', consoleEvent([cdp.register('1', {}, previewOf)]));

    expect(await readWhen(buffer, 1)).toEqual([previewLine]);
  });

  test(`renders from the preview when serialization never answers`, async () => {
    const { cdp, buffer } = buffered({ expandTimeoutMs: 20 });

    cdp.mode = 'hang';
    cdp.emit('Runtime.consoleAPICalled', consoleEvent([cdp.register('1', {}, previewOf)]));

    expect(await readWhen(buffer, 1)).toEqual([previewLine]);
  });

  test(`renders from the preview once the pending queue saturates`, async () => {
    const { cdp, buffer } = buffered({ maxPending: 1 });

    const value = { hp: 3, label: 'ok', inner: { deep: true } };

    for (const id of ['1', '2', '3', '4']) {
      cdp.delays.set(id, 5);
      cdp.emit('Runtime.consoleAPICalled', consoleEvent([cdp.register(id, value, previewOf)]));
    }

    const lines = await readWhen(buffer, 4);

    // The burst outruns the queue: the last entries render from their preview rather than
    // queueing up more round trips, and none of them is lost or reordered.
    expect(lines.filter(line => line == previewLine).length).toBeGreaterThan(0);
    expect(cdp.calls.length).toBeLessThan(4);
    expect(lines.length).toBe(4);
  });

  test(`renders a previewed non-container as its value, not as a collapsed object`, async () => {
    const { cdp, buffer } = buffered();

    cdp.mode = 'fail';
    cdp.emit(
      'Runtime.consoleAPICalled',
      consoleEvent([
        cdp.register(
          '1',
          {},
          {
            type: 'object',
            description: 'Object',
            properties: [
              { name: 'owner', type: 'object', subtype: 'null', value: 'null' },
              { name: 'when', type: 'object', subtype: 'date', value: 'Mon Aug 10 2026' },
              { name: 're', type: 'object', subtype: 'regexp', value: '/ab+c/g' },
              { name: 'items', type: 'object', subtype: 'array', value: 'Array(3)' }
            ]
          }
        )
      ])
    );

    expect(await readWhen(buffer, 1)).toEqual([
      `${AT} [log] (console) {owner: null, when: Mon Aug 10 2026, re: /ab+c/g, items: […]}`
    ]);
  });

  test(`takes a previewed collection's size from its description and marks the rest`, async () => {
    const { cdp, buffer } = buffered();

    cdp.mode = 'fail';
    cdp.emit(
      'Runtime.consoleAPICalled',
      consoleEvent([
        cdp.register(
          '1',
          {},
          {
            type: 'object',
            subtype: 'set',
            description: 'Set(100)',
            // No overflow flag: the size alone has to be what marks the omission, and a previewed
            // primitive is clipped like any other string.
            entries: [
              { value: { type: 'number', description: '1' } },
              { value: { type: 'string', description: 'y'.repeat(400) } }
            ]
          }
        )
      ])
    );

    expect(await readWhen(buffer, 1)).toEqual([
      `${AT} [log] (console) Set(100) {1, '${'y'.repeat(200)}…', …98 more}`
    ]);
  });

  test(`keeps a previewed collection property's size and flattens a stringified one`, async () => {
    const { cdp, buffer } = buffered();

    cdp.mode = 'fail';
    cdp.emit(
      'Runtime.consoleAPICalled',
      consoleEvent([
        cdp.register(
          '1',
          {},
          {
            type: 'object',
            description: 'Object',
            properties: [
              { name: 'm', type: 'object', subtype: 'map', value: 'Map(3)' },
              { name: 'e', type: 'object', subtype: 'error', value: 'TypeError: bad\n    at f' }
            ]
          }
        )
      ])
    );

    // The error keeps its message and drops its stack: an entry is one line, and the frames would
    // otherwise land in the read with no stamp in front of them.
    expect(await readWhen(buffer, 1)).toEqual([
      `${AT} [log] (console) {m: Map(3) {…}, e: TypeError: bad}`
    ]);
  });

  test(`stringifies a previewed subtype with nothing listed under it`, async () => {
    const { cdp, buffer } = buffered();

    cdp.mode = 'fail';
    cdp.emit(
      'Runtime.consoleAPICalled',
      consoleEvent([
        cdp.register(
          '1',
          {},
          {
            type: 'object',
            description: 'Object',
            properties: [{ name: 'w', type: 'object', subtype: 'weakmap', value: 'WeakMap' }]
          }
        ),
        // A weak collection keeps its members in `entries`, which nothing this side reads, so a
        // container rendering would assert an emptiness the value does not have.
        cdp.register(
          '2',
          {},
          {
            type: 'object',
            subtype: 'weakmap',
            description: 'WeakMap',
            entries: [{ key: { type: 'object', description: 'Object' } }]
          }
        )
      ])
    );

    expect(await readWhen(buffer, 1)).toEqual([`${AT} [log] (console) {w: WeakMap} WeakMap`]);
  });

  test(`keeps a message whose description opens with a blank line`, async () => {
    const { cdp, buffer } = buffered();

    cdp.mode = 'fail';
    cdp.emit(
      'Runtime.consoleAPICalled',
      consoleEvent([
        cdp.register(
          '1',
          {},
          {
            type: 'object',
            description: 'Object',
            properties: [
              { name: 'e', type: 'object', subtype: 'error', value: '\nBoom happened\r    at f' }
            ]
          }
        )
      ])
    );

    expect(await readWhen(buffer, 1)).toEqual([`${AT} [log] (console) {e: Boom happened}`]);
  });

  test(`falls back to the RemoteObject description when there is no preview`, async () => {
    const { cdp, buffer } = buffered();

    cdp.mode = 'fail';
    cdp.emit(
      'Runtime.consoleAPICalled',
      consoleEvent([{ type: 'object', objectId: '1', description: 'Object' }])
    );

    expect(await readWhen(buffer, 1)).toEqual([`${AT} [log] (console) Object`]);
  });
});

describe(`ordering`, () => {
  test(`publishes entries in arrival order despite asynchronous serialization`, async () => {
    const { cdp, buffer } = buffered();

    // The first argument takes far longer to come back than the ones logged after it.
    cdp.delays.set('slow', 40);

    cdp.emit('Runtime.consoleAPICalled', consoleEvent([cdp.register('slow', { n: 1 })]));

    cdp.emit('Runtime.consoleAPICalled', consoleEvent([cdp.register('fast', { n: 2 })]));

    cdp.emit('Runtime.exceptionThrown', {
      timestamp: 60_422_605,
      exceptionDetails: { exception: { type: 'object', description: 'Error: boom' } }
    });

    cdp.emit('Log.entryAdded', {
      entry: { source: 'network', level: 'warning', text: 'slow request' }
    });

    expect(await readWhen(buffer, 4)).toEqual([
      `${AT} [log] (console) {n: 1}`,
      `${AT} [log] (console) {n: 2}`,
      `${AT} [error] (exception) Error: boom`,
      `${AT} [warning] (network) slow request`
    ]);
  });

  test(`stamps the synthetic view-reload entry like every other one`, async () => {
    let notify: ((count: number) => void) | undefined;

    const { cdp, buffer } = buffered({
      reloads: {
        onReload: listener => {
          notify = listener;
        }
      }
    });

    cdp.delays.set('1', 20);
    cdp.emit('Runtime.consoleAPICalled', consoleEvent([cdp.register('1', { n: 1 })]));

    notify?.(2);

    expect(await readWhen(buffer, 2)).toEqual([
      `${AT} [log] (console) {n: 1}`,
      `${AT} [info] (reload) view reloaded (#2)`
    ]);
  });
});

describe(`the read contract`, () => {
  test(`keeps limit, level and clear working as before`, async () => {
    const { cdp, buffer } = buffered();

    for (const [level, message] of [
      ['log', 'one'],
      ['error', 'two'],
      ['log', 'three']
    ]) {
      cdp.emit(
        'Runtime.consoleAPICalled',
        consoleEvent([{ type: 'string', value: message }], { type: level })
      );
    }

    await readWhen(buffer, 3);

    expect(buffer.read({ limit: 1 })).toEqual([`${AT} [log] (console) three`]);
    expect(buffer.read({ level: 'error' })).toEqual([`${AT} [error] (console) two`]);
    expect(buffer.read({ limit: 10, clear: true }).length).toBe(3);
    expect(buffer.read({})).toEqual([]);
  });

  test(`clear discards a capture still awaiting serialization`, async () => {
    const { cdp, buffer } = buffered();

    cdp.delays.set('slow', 20);
    cdp.emit('Runtime.consoleAPICalled', consoleEvent([cdp.register('slow', { n: 1 })]));

    expect(buffer.read({ clear: true })).toEqual([]);

    await Bun.sleep(60);

    // The capture was taken before the clear, so it belongs to a buffer that no longer exists.
    expect(buffer.read({})).toEqual([]);
  });
});
