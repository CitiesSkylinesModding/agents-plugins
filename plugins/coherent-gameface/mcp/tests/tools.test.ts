import { afterEach, beforeEach, describe, expect, test } from 'bun:test';
import { Window } from 'happy-dom';
import type { CdpClient } from '../src/cdp';
import { fillFn, gameFill, gameType, typeFn } from '../src/tools';

/**
 * The page functions read the DOM off globals, since that is what they get once serialized into
 * the page. The seam is driven by installing a happy-dom window's globals around each test.
 */
const DOM_GLOBALS = [
  'document',
  'window',
  'Event',
  'FocusEvent',
  'KeyboardEvent',
  'HTMLInputElement',
  'HTMLTextAreaElement'
] as const;

/**
 * Every event type the fill and type paths can produce, focus-side ones included: a test asserts
 * on the whole recorded sequence, so an event nobody asked for has to show up in it.
 */
const RECORDED = [
  'focus',
  'focusin',
  'input',
  'change',
  'blur',
  'focusout',
  'keydown',
  'keyup'
] as const;

/**
 * The three branches the dispatch must behave identically across, each with the markup that
 * selects it.
 */
const FIELDS = [
  { branch: 'input', html: `<input id="f">` },
  { branch: 'textarea', html: `<textarea id="f"></textarea>` },
  { branch: 'contenteditable', html: `<div id="f" contenteditable="true"></div>` }
] as const;

/**
 * What the recorders saw during the arm under way: the event sequence, how many of the commits
 * bubbled from the target, and the value the commit handler could read.
 */
const seen: { events: string[]; bubbledCommits: number; valueAtCommit: string | undefined } = {
  events: [],
  bubbledCommits: 0,
  valueAtCommit: undefined
};

const saved = new Map<string, unknown>();

beforeEach(() => {
  const window = new Window();

  for (const name of DOM_GLOBALS) {
    saved.set(name, (globalThis as Record<string, unknown>)[name]);
    // The one hard boundary: happy-dom's constructors are not lib.dom's, and the page functions
    // want them under the standard global names.
    (globalThis as Record<string, unknown>)[name] =
      name == 'window' ? window : (window as unknown as Record<string, unknown>)[name];
  }

  reset();
  record();
});

afterEach(() => {
  for (const [name, value] of saved) {
    (globalThis as Record<string, unknown>)[name] = value;
  }

  saved.clear();
});

describe('fillFn', () => {
  for (const { branch, html } of FIELDS) {
    test(`commits the ${branch} branch with one bubbling focusout, last`, () => {
      mount(html);

      const result = fillFn('#f', 'hello', 0, true);

      expect(result).toMatchObject({ found: true, value: 'hello' });
      expect(seen.events.at(-1)).toBe('focusout');
      expect(seen.events.filter(type => type == 'focusout')).toHaveLength(1);
      expect(seen.bubbledCommits).toBe(1);
      // Ordered after the value set and after input/change: the commit handler reads the new value.
      expect(seen.valueAtCommit).toBe('hello');
      expect(seen.events.indexOf('focusout')).toBeGreaterThan(seen.events.indexOf('change'));
    });

    test(`leaves the ${branch} branch's default sequence untouched`, () => {
      mount(html);

      const withCommit = collect(() => fillFn('#f', 'hello', 0, true));

      mount(html);

      const byDefault = collect(() => fillFn('#f', 'hello', 0, false));

      expect(byDefault).not.toContain('focusout');
      expect(byDefault).not.toContain('blur');
      // The option adds the commit and nothing else, so the two arms differ by that one event.
      expect([...byDefault, 'focusout']).toEqual(withCommit);
    });
  }
});

describe('typeFn', () => {
  for (const { branch, html } of FIELDS) {
    test(`commits the ${branch} branch after the last keystroke`, () => {
      mount(html);

      const result = typeFn('#f', 'hi', 0, true);

      expect(result).toMatchObject({ found: true, typed: 2, value: 'hi' });
      expect(seen.events.at(-1)).toBe('focusout');
      expect(seen.events.filter(type => type == 'focusout')).toHaveLength(1);
      expect(seen.bubbledCommits).toBe(1);
      expect(seen.valueAtCommit).toBe('hi');
      expect(seen.events.indexOf('focusout')).toBeGreaterThan(seen.events.lastIndexOf('keyup'));
    });

    test(`leaves the ${branch} branch's default sequence untouched`, () => {
      mount(html);

      const withCommit = collect(() => typeFn('#f', 'hi', 0, true));

      mount(html);

      const byDefault = collect(() => typeFn('#f', 'hi', 0, false));

      expect(byDefault).not.toContain('focusout');
      expect(byDefault).not.toContain('blur');
      expect([...byDefault, 'focusout']).toEqual(withCommit);
    });
  }
});

test('the commit still fires on an engine without the FocusEvent constructor', () => {
  mount(`<input id="f">`);
  delete (globalThis as Record<string, unknown>).FocusEvent;

  const result = fillFn('#f', 'hello', 0, true);

  expect(result).toMatchObject({ found: true, value: 'hello' });
  expect(seen.events.at(-1)).toBe('focusout');
  expect(seen.bubbledCommits).toBe(1);
});

/**
 * The seam above the page functions: what the handlers serialize into the page. The page function
 * takes `commit` as a required argument, so only these tests hold the option's default and the
 * order the arguments reach it in.
 */
describe('the handler seam', () => {
  test('gameFill sends the index and the commit flag it was given', async () => {
    const cdp = new FakeCdp();

    await gameFill(cdp.asClient(), { selector: '#f', value: 'v', index: 2, commit: true });

    expect(cdp.expressions[0]).toEndWith(`("#f", "v", 2, true)`);
  });

  test('gameFill defaults to index 0 and no commit', async () => {
    const cdp = new FakeCdp();

    await gameFill(cdp.asClient(), { selector: '#f', value: 'v' });

    expect(cdp.expressions[0]).toEndWith(`("#f", "v", 0, false)`);
  });

  test('gameType sends the index and the commit flag it was given', async () => {
    const cdp = new FakeCdp();

    await gameType(cdp.asClient(), { selector: '#f', text: 'hi', index: 2, commit: true });

    expect(cdp.expressions[0]).toEndWith(`("#f", "hi", 2, true)`);
  });

  test('gameType defaults to index 0 and no commit', async () => {
    const cdp = new FakeCdp();

    await gameType(cdp.asClient(), { selector: '#f', text: 'hi' });

    expect(cdp.expressions[0]).toEndWith(`("#f", "hi", 0, false)`);
  });
});

/**
 * Stand-in for the CDP client at the handler seam: records the expressions the handlers build and
 * answers each with a successful page result, with no socket and no application.
 */
class FakeCdp {
  public readonly expressions: string[] = [];

  public call<T>(_method: string, params?: Record<string, unknown>): Promise<T> {
    this.expressions.push(String(params?.expression));

    return Promise.resolve({
      result: { value: { found: true, count: 1, typed: 1, mode: 'input', value: 'v' } }
    } as T);
  }

  /**
   * The handlers take the concrete client, whose private fields no fake can satisfy; only `call`
   * is ever reached from here.
   */
  public asClient(): CdpClient {
    return this as unknown as CdpClient;
  }
}

/**
 * Installs the markup for one arm, on the document the recorders are already watching.
 */
function mount(html: string): void {
  document.body.innerHTML = html;

  reset();
}

/**
 * Watches the whole document in the capture phase, so a non-bubbling event is recorded too and
 * only `seen.bubbledCommits` speaks for the commit's bubbling.
 */
function record(): void {
  for (const type of RECORDED) {
    document.addEventListener(
      type,
      event => {
        seen.events.push(event.type);

        if (event.type != 'focusout') {
          return;
        }

        const field = document.querySelector<HTMLElement>('#f');

        if (event.bubbles && event.target == field) {
          seen.bubbledCommits++;
        }

        seen.valueAtCommit = read(field);
      },
      true
    );
  }
}

function reset(): void {
  seen.events = [];
  seen.bubbledCommits = 0;
  seen.valueAtCommit = undefined;
}

/**
 * Runs one arm and hands back the sequence it produced.
 */
function collect(arm: () => unknown): string[] {
  arm();

  return [...seen.events];
}

function read(field: HTMLElement | null): string | undefined {
  if (!field) {
    return undefined;
  }

  return field.isContentEditable
    ? (field.textContent ?? '')
    : (field as HTMLInputElement | HTMLTextAreaElement).value;
}
