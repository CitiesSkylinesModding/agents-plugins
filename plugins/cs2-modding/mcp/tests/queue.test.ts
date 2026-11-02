import { describe, expect, test } from 'bun:test';
import { setImmediate } from 'node:timers/promises';
import { createQueue } from '../src/queue';

describe('createQueue', () => {
  const { signal } = new AbortController();

  test(`runs one step at a time`, async () => {
    const queue = createQueue();
    const first = Promise.withResolvers<void>();
    const order: string[] = [];

    const steps = [
      queue.run(signal, async () => {
        await first.promise;

        order.push('first');
      }),
      queue.run(signal, () => {
        order.push('second');

        return Promise.resolve();
      })
    ];

    first.resolve();

    await Promise.all(steps);

    expect(order).toEqual(['first', 'second']);
  });

  test(`lets the next step go once a stuck step's signal aborts`, async () => {
    const queue = createQueue();
    const stuck = new AbortController();

    void queue.run(stuck.signal, () => Promise.withResolvers<void>().promise);

    const next = queue.run(signal, () => Promise.resolve('ran'));

    stuck.abort();

    expect(await next).toBe('ran');
  });

  test(`holds a step behind a running one when the step between them aborts`, async () => {
    const queue = createQueue();
    const held = Promise.withResolvers<void>();
    const leaving = new AbortController();
    const order: string[] = [];

    const running = queue.run(signal, async () => {
      await held.promise;

      order.push('running');
    });

    const between = queue.run(leaving.signal, () => Promise.resolve());

    const next = queue.run(signal, () => {
      order.push('next');

      return Promise.resolve();
    });

    leaving.abort();

    await setImmediate();

    held.resolve();

    await Promise.allSettled([running, between, next]);

    expect(order).toEqual(['running', 'next']);
  });

  test(`fails a step whose signal aborted while it waited, leaving it unrun`, async () => {
    const queue = createQueue();
    const held = Promise.withResolvers<void>();
    const leaving = new AbortController();
    const ran: string[] = [];

    void queue.run(signal, () => held.promise);

    const waiting = queue.run(leaving.signal, () => {
      ran.push('waiting');

      return Promise.resolve();
    });

    leaving.abort();
    held.resolve();

    expect(await Promise.allSettled([waiting])).toMatchObject([
      { status: 'rejected', reason: { name: 'AbortError' } }
    ]);
    expect(ran).toEqual([]);
  });
});
