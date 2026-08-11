import { describe, expect, test } from 'bun:test';
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import {
  DEFAULT_SOURCE_MAX_CHARS,
  type DebuggerCdp,
  DebuggerSession,
  MAX_SEARCH_MATCHES
} from '../src/debugger';
import { gameScreenshot, gameWait } from '../src/tools';

/**
 * A script the fake serves: its `scriptParsed` metadata and the source `getScriptSource` answers
 * with, which is how the engine presents the two.
 */
interface FakeScript {
  readonly scriptId: string;
  readonly url: string;
  readonly source: string;

  /**
   * Where the script sits inside its resource, as an embedded one does; both default to 0, which
   * is what a whole .js file reports.
   */
  readonly startLine?: number;
  readonly startColumn?: number;
}

/**
 * Stand-in for the CDP client at the injected seam: feeds synthetic events and answers commands
 * from what a test registered, with no socket and no application.
 */
class FakeCdp implements DebuggerCdp {
  public readonly calls: Array<{ method: string; params: Record<string, unknown> }> = [];

  private readonly sources = new Map<string, string>();
  private readonly handlers = new Map<string, (params: Record<string, unknown>) => unknown>();
  private readonly listeners: Array<(method: string, params: unknown) => void> = [];

  public onConnect(): void {
    // The session enables its domain here; nothing connects in a hermetic test.
  }

  public onEvent(listener: (method: string, params: unknown) => void): void {
    this.listeners.push(listener);
  }

  public connection(): Promise<unknown> {
    return Promise.resolve();
  }

  public ensureDomain(): Promise<void> {
    return Promise.resolve();
  }

  /**
   * Registers how one CDP command answers. The last registration for a method wins.
   */
  public answer(method: string, handler: (params: Record<string, unknown>) => unknown): void {
    this.handlers.set(method, handler);
  }

  /**
   * Announces a parsed script and makes its source readable, as a real parse does both.
   */
  public parse(script: FakeScript): void {
    const lines = script.source.split('\n');
    const startLine = script.startLine ?? 0;

    this.sources.set(script.scriptId, script.source);

    this.emit('Debugger.scriptParsed', {
      scriptId: script.scriptId,
      url: script.url,
      startLine,
      startColumn: script.startColumn ?? 0,
      // As CDP reports it: the resource line the script's last line falls on.
      endLine: startLine + lines.length - 1,
      length: script.source.length
    });
  }

  // Async so a handler that throws rejects, as the real client does on a CDP error frame.
  public async call<T = unknown>(method: string, params: Record<string, unknown> = {}): Promise<T> {
    this.calls.push({ method, params });

    const handler = this.handlers.get(method);

    // A registered answer wins over the parsed sources, so a test can make one script unreadable.
    if (handler) {
      return (await handler(params)) as T;
    }

    if (method == 'Debugger.getScriptSource') {
      return { scriptSource: this.sources.get(params.scriptId as string) } as T;
    }

    throw new Error(`FakeCdp has no answer for ${method}`);
  }

  public emit(method: string, params: unknown): void {
    for (const listener of this.listeners) {
      listener(method, params);
    }
  }
}

/**
 * A session wired to a fresh fake, so no test states the constructor's shape.
 */
function session(): { cdp: FakeCdp; debug: DebuggerSession } {
  const cdp = new FakeCdp();

  return { cdp, debug: new DebuggerSession(cdp, { onReload: () => {} }) };
}

/**
 * A `Debugger.paused` payload stopped in `scriptId`, at a 0-based line and column as CDP sends
 * them.
 */
function pausedAt(
  scriptId: string,
  lineNumber: number,
  columnNumber: number,
  functionName = 'handleClick'
): Record<string, unknown> {
  return {
    reason: 'other',
    hitBreakpoints: ['1:0:0:app'],
    callFrames: [
      {
        callFrameId: '0',
        functionName,
        location: { scriptId, lineNumber, columnNumber },
        url: '',
        scopeChain: []
      }
    ]
  };
}

function resultText(result: CallToolResult): string {
  return result.content
    .filter(part => part.type == 'text')
    .map(part => part.text)
    .join('\n');
}

function resultJson(result: CallToolResult): Record<string, unknown> {
  return JSON.parse(resultText(result)) as Record<string, unknown>;
}

/**
 * A one-line bundle: `endLine` equals `startLine`, which is the metadata the single-line warning
 * reads. The needle sits far enough in for the snippet to be clipped on both sides.
 */
const MINIFIED = {
  scriptId: '7',
  url: 'coui://ui-mods/bundle.js',
  source: `${'a'.repeat(60)};function openPanel(e){return e}${'b'.repeat(60)}`
};

describe(`the screenshot pause gate`, () => {
  test(`refuses to capture while the debugger holds the UI paused`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'let a\nlet b\nlet c\n' });
    cdp.emit('Debugger.paused', pausedAt('3', 1, 12));

    const result = await gameScreenshot(cdp, debug);

    expect(result.isError).toBe(true);

    const message = resultText(result);

    // The location is what tells the caller what resuming would abandon.
    expect(message).toContain(`handleClick at coui://ui-mods/app.js:2:13`);
    expect(message).toContain(`game_debug_step action=resume`);

    // Failing fast is the whole point: the hanging call must never leave.
    expect(cdp.calls.some(call => call.method == 'Page.captureScreenshot')).toBe(false);
  });

  test(`captures as before once the UI has resumed`, async () => {
    const { cdp, debug } = session();

    cdp.answer('Page.captureScreenshot', () => ({ data: 'aW1hZ2U=' }));
    cdp.emit('Debugger.paused', pausedAt('3', 0, 0));
    cdp.emit('Debugger.resumed', {});

    const result = await gameScreenshot(cdp, debug);

    expect(result.isError).toBeUndefined();
    expect(result.content).toEqual([{ type: 'image', data: 'aW1hZ2U=', mimeType: 'image/png' }]);
  });
});

describe(`game_wait against a paused UI`, () => {
  const reloads = { count: 0 };

  test(`names the pause in a timeout, not leaving it to read as unreachable`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'let a\nlet b\n' });
    cdp.answer('Runtime.evaluate', () => ({ result: { type: 'boolean', value: false } }));
    cdp.emit('Debugger.paused', pausedAt('3', 1, 4));

    const result = await gameWait(cdp, reloads, debug, { selector: '.panel', timeoutMs: 10 });

    expect(result.isError).toBe(true);

    const message = resultText(result);

    expect(message).toContain(`Timed out`);
    expect(message).toContain(`paused in the JS debugger (other, at handleClick`);
  });

  test(`says nothing about pausing when the UI is running`, async () => {
    const { cdp, debug } = session();

    cdp.answer('Runtime.evaluate', () => ({ result: { type: 'boolean', value: false } }));

    const result = await gameWait(cdp, reloads, debug, { selector: '.panel', timeoutMs: 10 });

    expect(result.isError).toBe(true);
    expect(resultText(result)).not.toContain(`paused`);
  });

  test(`still resolves instantly while paused when the condition already holds`, async () => {
    const { cdp, debug } = session();

    cdp.answer('Runtime.evaluate', () => ({ result: { type: 'boolean', value: true } }));
    cdp.emit('Debugger.paused', pausedAt('3', 0, 0));

    const result = await gameWait(cdp, reloads, debug, { selector: '.panel', timeoutMs: 10 });

    expect(result.isError).toBeUndefined();
    expect(resultText(result)).toContain(`Condition met`);
  });
});

describe(`breakpoint locations`, () => {
  test(`reports where the engine bound it, 1-based on both axes`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'a\nb\nc\nd\n' });
    cdp.answer('Debugger.setBreakpointByUrl', () => ({
      breakpointId: '1:41:7:app',
      locations: [{ scriptId: '3', lineNumber: 41, columnNumber: 7 }]
    }));

    const result = resultJson(await debug.setBreakpoint('app.js', 42));

    expect(result.resolvedLocations).toEqual([`coui://ui-mods/app.js:42:8`]);
    expect(result.pending).toBe(false);
    expect(result.hint).toBeUndefined();
  });

  test(`converts the 1-based column it takes to the 0-based one CDP wants`, async () => {
    const { cdp, debug } = session();

    cdp.answer('Debugger.setBreakpointByUrl', () => ({ breakpointId: '1', locations: [] }));

    await debug.setBreakpoint('app.js', 42, 118);

    const call = cdp.calls.find(entry => entry.method == 'Debugger.setBreakpointByUrl');

    expect(call?.params.lineNumber).toBe(41);
    expect(call?.params.columnNumber).toBe(117);
  });

  test(`warns that a one-line script is a bundle and names the tool that solves it`, async () => {
    const { cdp, debug } = session();

    cdp.parse(MINIFIED);
    cdp.answer('Debugger.setBreakpointByUrl', () => ({
      breakpointId: '1:0:0:bundle',
      locations: [{ scriptId: MINIFIED.scriptId, lineNumber: 0, columnNumber: 0 }]
    }));

    const result = resultJson(await debug.setBreakpoint('bundle.js', 1));

    expect(result.resolvedLocations).toEqual([`coui://ui-mods/bundle.js:1:1`]);
    expect(String(result.hint)).toContain(`game_debug_search_source`);
  });

  test(`leaves the pending path saying what pending means`, async () => {
    const { cdp, debug } = session();

    cdp.parse(MINIFIED);
    cdp.answer('Debugger.setBreakpointByUrl', () => ({ breakpointId: '1', locations: [] }));

    const result = resultJson(await debug.setBreakpoint('not-loaded.js', 12));

    expect(result.pending).toBe(true);
    expect(result.resolvedLocations).toEqual([]);
    expect(String(result.note)).toContain(`Pending`);

    // Nothing resolved, so there is no script to call one-line.
    expect(result.hint).toBeUndefined();
  });

  // The status listing is where you decide which breakpoint to remove, so a column-targeted one
  // has to be distinguishable there from a line-targeted one.
  test(`reports the requested column back through the status listing`, async () => {
    const { cdp, debug } = session();

    cdp.answer('Debugger.setBreakpointByUrl', () => ({ breakpointId: '1', locations: [] }));

    await debug.setBreakpoint('app.js', 42, 118);
    await debug.setBreakpoint('app.js', 7);

    const [withColumn, lineOnly] = resultJson(await debug.status()).breakpoints as Array<
      Record<string, unknown>
    >;

    expect(withColumn?.line).toBe(42);
    expect(withColumn?.column).toBe(118);
    expect(lineOnly?.line).toBe(7);
    expect(lineOnly?.column).toBeUndefined();
  });

  // Reading one breakpoint through two tools must not show two shapes for the same state, or a
  // check written against one of them misreads the other.
  test(`omits an unset column and condition, as the status listing does`, async () => {
    const { cdp, debug } = session();

    cdp.answer('Debugger.setBreakpointByUrl', () => ({ breakpointId: '1', locations: [] }));

    const set = resultJson(await debug.setBreakpoint('app.js', 42));
    const [listed] = resultJson(await debug.status()).breakpoints as Array<Record<string, unknown>>;

    expect(`column` in set).toBe(false);
    expect(`condition` in set).toBe(false);
    expect(`column` in (listed ?? {})).toBe(false);
    expect(`condition` in (listed ?? {})).toBe(false);
  });

  test(`reports a column and condition it was given, through both tools`, async () => {
    const { cdp, debug } = session();

    cdp.answer('Debugger.setBreakpointByUrl', () => ({ breakpointId: '1', locations: [] }));

    const set = resultJson(await debug.setBreakpoint('app.js', 42, 118, 'a > 1'));
    const [listed] = resultJson(await debug.status()).breakpoints as Array<Record<string, unknown>>;

    expect(set.column).toBe(118);
    expect(set.condition).toBe(`a > 1`);
    expect(listed?.column).toBe(118);
    expect(listed?.condition).toBe(`a > 1`);
  });

  // A breakpoint aimed at a script that parses later binds on the event, not on the reply, and an
  // armed breakpoint still listed as pending is what makes a caller set a second one.
  test(`stops calling a breakpoint pending once the engine reports it bound`, async () => {
    const { cdp, debug } = session();

    cdp.answer('Debugger.setBreakpointByUrl', () => ({
      breakpointId: '1:11:0:app',
      locations: []
    }));

    expect(resultJson(await debug.setBreakpoint('app.js', 12)).pending).toBe(true);

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'a\nb\nc\n' });
    cdp.emit('Debugger.breakpointResolved', {
      breakpointId: '1:11:0:app',
      location: { scriptId: '3', lineNumber: 11, columnNumber: 4 }
    });

    const [bp] = resultJson(await debug.status()).breakpoints as Array<Record<string, unknown>>;

    expect(bp?.resolvedLocations).toEqual([`coui://ui-mods/app.js:12:5`]);
  });

  test(`ignores a resolution for a breakpoint it does not own`, async () => {
    const { cdp, debug } = session();

    cdp.answer('Debugger.setBreakpointByUrl', () => ({ breakpointId: '1', locations: [] }));

    await debug.setBreakpoint('app.js', 12);
    cdp.emit('Debugger.breakpointResolved', {
      breakpointId: 'someone-elses',
      location: { scriptId: '3', lineNumber: 11, columnNumber: 4 }
    });

    const [bp] = resultJson(await debug.status()).breakpoints as Array<Record<string, unknown>>;

    expect(bp?.resolvedLocations).toEqual([]);
  });

  // A late attach resolves against scripts this connection never saw parsed, and a bare id where
  // the url goes reads as a filename.
  test(`says so when it cannot name the script a location falls in`, async () => {
    const { cdp, debug } = session();

    cdp.answer('Debugger.setBreakpointByUrl', () => ({
      breakpointId: '1',
      locations: [{ scriptId: '77', lineNumber: 0, columnNumber: 4212 }]
    }));

    const result = resultJson(await debug.setBreakpoint('bundle.js', 1));

    expect(result.resolvedLocations).toEqual([`(unknown script 77):1:4213`]);
  });
});

describe(`searching the parsed sources`, () => {
  test(`returns each hit as a 1-based line and column, with the source around it`, async () => {
    const { cdp, debug } = session();

    cdp.parse(MINIFIED);

    const result = resultJson(await debug.searchSource('openPanel'));

    expect(result.total).toBe(1);
    expect(result.returned).toBe(1);
    expect(result.truncated).toBe(false);
    expect(result.matches).toEqual([
      {
        url: `coui://ui-mods/bundle.js`,
        scriptId: '7',
        line: 1,
        column: 71,
        // 40 characters either side of the hit itself, so the leading `;function ` and the
        // trailing `(e){return e}` eat into the margin.
        snippet: `…${'a'.repeat(30)};function openPanel(e){return e}${'b'.repeat(27)}…`
      }
    ]);
  });

  test(`counts lines and columns from the line the hit is on`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'let a\nlet b\n  found\n' });

    const { matches } = resultJson(await debug.searchSource('found'));
    const [match] = matches as Array<Record<string, unknown>>;

    expect(match?.line).toBe(3);
    expect(match?.column).toBe(3);
  });

  test(`matches case-sensitively and literally`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'openPanel(a.b)' });

    expect(resultJson(await debug.searchSource('openpanel')).total).toBe(0);
    expect(resultJson(await debug.searchSource('a.b')).total).toBe(1);
    expect(resultJson(await debug.searchSource('a?b')).total).toBe(0);
  });

  test(`caps what it returns while reporting the true total`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'hit '.repeat(12) });

    const result = resultJson(await debug.searchSource('hit'));

    expect(result.total).toBe(12);
    expect(result.returned).toBe(MAX_SEARCH_MATCHES);
    expect(result.truncated).toBe(true);
  });

  test(`searches only the scripts whose url contains the filter`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'openPanel()' });
    cdp.parse({ scriptId: '4', url: 'coui://ui-mods/other.js', source: 'openPanel()' });

    const result = resultJson(await debug.searchSource('openPanel', 'other'));

    expect(result.total).toBe(1);
    expect(result.scriptsSearched).toBe(1);
    expect(cdp.calls.filter(call => call.method == 'Debugger.getScriptSource')).toHaveLength(1);
  });

  // The workflow copies a url fragment out of game_debug_scripts, which matches case-insensitively;
  // a fragment that listed a script has to search it too.
  test(`matches the url filter case-insensitively, unlike the query`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/AppBundle.js', source: 'openPanel()' });

    expect(resultJson(await debug.searchSource('openPanel', 'appbundle')).total).toBe(1);
  });

  test(`sends an empty script map to the reload rather than answering "no matches"`, async () => {
    const { debug } = session();

    const result = resultJson(await debug.searchSource('openPanel'));

    expect(result.total).toBe(0);
    expect(String(result.note)).toContain(`location.reload()`);
  });

  test(`blames the query, not the map, once scripts are parsed`, async () => {
    const { cdp, debug } = session();

    cdp.parse(MINIFIED);

    const result = resultJson(await debug.searchSource('nothingLikeThis'));

    expect(result.total).toBe(0);
    expect(String(result.note)).toContain(`case-sensitive`);
    expect(String(result.note)).not.toContain(`location.reload()`);
  });

  // The query never ran here, so blaming it would send the caller to fix the wrong input.
  test(`blames the url filter when it is what matched nothing`, async () => {
    const { cdp, debug } = session();

    cdp.parse(MINIFIED);

    const result = resultJson(await debug.searchSource('openPanel', 'no-such-bundle'));

    expect(result.total).toBe(0);
    expect(result.scriptsSearched).toBe(0);
    expect(String(result.note)).toContain(`urlContains`);
    expect(String(result.note)).not.toContain(`case-sensitive`);
  });

  // A dropped script answers with a CDP error, and one stale id must not cost the caller the
  // matches every other script yielded.
  test(`keeps the matches it has when one script's source cannot be read`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'openPanel()' });
    cdp.parse({ scriptId: '4', url: 'coui://ui-mods/gone.js', source: 'openPanel()' });
    cdp.answer('Debugger.getScriptSource', params => {
      if (params.scriptId == '4') {
        throw new Error(`CDP error (-32000): No script for id: 4`);
      }

      return { scriptSource: 'openPanel()' };
    });

    const result = resultJson(await debug.searchSource('openPanel'));

    expect(result.total).toBe(1);
    expect(result.matches).toHaveLength(1);
  });

  // A script embedded in a document starts partway into its resource, and every position the
  // breakpoint tool takes is resource-based, so the search has to report it in that space.
  test(`reports an embedded script's hits in resource coordinates`, async () => {
    const { cdp, debug } = session();

    cdp.parse({
      scriptId: '9',
      url: 'coui://ui-mods/index.html',
      source: 'let a\n  openPanel()',
      startLine: 30,
      startColumn: 8
    });

    const { matches } = resultJson(await debug.searchSource('openPanel'));
    const [match] = matches as Array<Record<string, unknown>>;

    // Line 2 of the body is document line 32; the column shifts only on the shared first line.
    expect(match?.line).toBe(32);
    expect(match?.column).toBe(3);
  });

  // The try/catch that keeps one stale script from sinking the search must not then report the
  // string absent when no source was read at all.
  test(`says the ids are stale when no matched script could be read`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'openPanel()' });
    cdp.answer('Debugger.getScriptSource', () => {
      throw new Error(`CDP error (-32000): No script for id: 3`);
    });

    const result = resultJson(await debug.searchSource('openPanel'));

    expect(result.total).toBe(0);
    expect(String(result.note)).toContain(`stale`);
    expect(String(result.note)).not.toContain(`case-sensitive`);
  });

  test(`shifts the column too when the hit is on the line the script shares`, async () => {
    const { cdp, debug } = session();

    cdp.parse({
      scriptId: '9',
      url: 'coui://ui-mods/index.html',
      source: 'openPanel()',
      startLine: 30,
      startColumn: 8
    });

    const { matches } = resultJson(await debug.searchSource('openPanel'));
    const [match] = matches as Array<Record<string, unknown>>;

    expect(match?.line).toBe(31);
    expect(match?.column).toBe(9);
  });
});

describe(`reading a script's source`, () => {
  // The source listing has to number lines the same way the search and the breakpoints do, or a
  // line carried from one tool to the other lands somewhere else.
  test(`numbers an embedded script's lines from its place in the resource`, async () => {
    const { cdp, debug } = session();

    cdp.parse({
      scriptId: '9',
      url: 'coui://ui-mods/index.html',
      source: 'let a\nopenPanel()',
      startLine: 30
    });

    const whole = resultText(await debug.getSource('9'));

    expect(whole).toContain(`   31  let a`);
    expect(whole).toContain(`   32  openPanel()`);

    // And the window takes the same coordinates it prints.
    expect(resultText(await debug.getSource('9', 32))).toContain(`   32  openPanel()`);
    expect(resultText(await debug.getSource('9', 32))).not.toContain(`let a`);
  });

  test(`holds the reported window to the lines the script actually has`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'let a\nlet b' });

    // From the second line, so the footer renders and its end bound is what the assertion reads.
    const text = resultText(await debug.getSource('3', 2, 999));

    expect(text).toContain(`    2  let b`);
    expect(text).toContain(`showing lines 2-2 of 2.`);
  });

  test(`says so rather than rendering nothing when the line is past the end`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'let a\nlet b' });

    const result = await debug.getSource('3', 50);

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`spans lines 1-2`);
  });

  // A low line count is no promise of a small answer: one minified line can be the whole module,
  // and the caller cannot see that coming from game_debug_scripts.
  test(`clips a long line to the character budget and says it did`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'x'.repeat(50_000) });

    const text = resultText(await debug.getSource('3'));

    expect(text.length).toBeLessThan(DEFAULT_SOURCE_MAX_CHARS + 300);
    expect(text).toContain(`clipped to ${String(DEFAULT_SOURCE_MAX_CHARS)} of`);
    expect(text).toContain(`game_debug_search_source`);
  });

  test(`takes a caller's own budget over the default`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'x'.repeat(50_000) });

    expect(resultText(await debug.getSource('3', undefined, undefined, 100))).toContain(
      `clipped to 100 of`
    );

    // Raised past the source, so nothing is clipped and no note claims otherwise.
    expect(resultText(await debug.getSource('3', undefined, undefined, 99_999))).not.toContain(
      `clipped`
    );
  });

  // Both caps fire together on any real source file, and the range has to name what survived the
  // character budget rather than what the line window asked for.
  test(`reports the line range the clipped body actually carries`, async () => {
    const { cdp, debug } = session();

    cdp.parse({
      scriptId: '3',
      url: 'coui://ui-mods/app.js',
      source: Array.from(
        { length: 1000 },
        (_, at) => `const v${String(at)} = ${'y'.repeat(40)};`
      ).join('\n')
    });

    const text = resultText(await debug.getSource('3'));
    const cut = text.lastIndexOf(`\n... `);
    const body = text.slice(0, cut);
    const note = text.slice(cut);

    expect(note).toContain(`clipped to`);

    const reported = Number(/showing lines 1-(?<end>\d+) of 1000/u.exec(note)?.groups?.end);

    expect(reported).toBe(body.split('\n').length);

    // Clipping bit first, so the 400-line window is not what bounded the answer.
    expect(reported).toBeLessThan(400);
  });

  test(`says nothing about clipping when the whole script fits`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'let a\nlet b' });

    expect(resultText(await debug.getSource('3'))).not.toContain(`clipped`);
  });

  test(`numbers a whole file from 1, the offset being zero`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'let a\nlet b' });

    expect(resultText(await debug.getSource('3'))).toContain(`    1  let a`);
  });
});

describe(`listing the parsed scripts`, () => {
  test(`explains the empty map a late attach starts with`, async () => {
    const { debug } = session();

    const result = resultJson(await debug.listScripts());

    expect(result.total).toBe(0);
    expect(String(result.note)).toContain(`location.reload()`);
  });

  test(`drops the explanation once scripts are parsed`, async () => {
    const { cdp, debug } = session();

    cdp.parse(MINIFIED);

    const result = resultJson(await debug.listScripts());

    expect(result.total).toBe(1);
    expect(result.note).toBeUndefined();
  });

  // An empty listing has two causes needing opposite moves, and the total alone tells them apart
  // for neither.
  test(`blames the filter when it is what emptied the listing`, async () => {
    const { cdp, debug } = session();

    cdp.parse(MINIFIED);

    const result = resultJson(await debug.listScripts('no-such-bundle'));

    expect(result.total).toBe(0);
    expect(String(result.note)).toContain(`filter`);
    expect(String(result.note)).not.toContain(`location.reload()`);
  });

  test(`says nothing about the filter when it selected something`, async () => {
    const { cdp, debug } = session();

    cdp.parse(MINIFIED);

    expect(resultJson(await debug.listScripts('bundle')).note).toBeUndefined();
  });

  // A count alone would have a caller aim at the top of the document rather than at the script.
  test(`gives an embedded script the line its numbering starts from`, async () => {
    const { cdp, debug } = session();

    cdp.parse({
      scriptId: '9',
      url: 'coui://ui-mods/index.html',
      source: 'let a\nlet b',
      startLine: 30
    });
    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'let a\nlet b' });

    // Sorted by url, so app.js precedes index.html.
    const [wholeFile, embedded] = resultJson(await debug.listScripts()).scripts as Array<
      Record<string, unknown>
    >;

    expect(embedded?.lines).toBe(2);
    expect(embedded?.firstLine).toBe(31);

    // A whole file starts at 1 like every other tool's default, so the key stays out of its way.
    expect(wholeFile?.firstLine).toBeUndefined();
  });

  // The bundle the column tools exist for is the one whose line count has to be right.
  test(`counts a one-line bundle as one line`, async () => {
    const { cdp, debug } = session();

    cdp.parse(MINIFIED);

    const [script] = resultJson(await debug.listScripts()).scripts as Array<
      Record<string, unknown>
    >;

    expect(script?.lines).toBe(1);
  });
});

describe(`resuming`, () => {
  test(`refuses to call a resume that left the UI paused a success`, async () => {
    const { cdp, debug } = session();

    cdp.parse({ scriptId: '3', url: 'coui://ui-mods/app.js', source: 'a\nb\n' });
    cdp.emit('Debugger.paused', pausedAt('3', 0, 0, 'first'));

    // The engine continues and stops again before the resumed event: another armed breakpoint on
    // the resumed path. Only the second pause is ever observable.
    cdp.answer('Debugger.resume', () => {
      cdp.emit('Debugger.paused', pausedAt('3', 1, 2, 'second'));

      return {};
    });

    const result = await debug.step('resume');

    expect(result.isError).toBe(true);
    expect(resultText(result)).toContain(`paused again at second at coui://ui-mods/app.js:2:3`);
  });

  test(`reports a resume the UI honored`, async () => {
    const { cdp, debug } = session();

    cdp.emit('Debugger.paused', pausedAt('3', 0, 0));

    cdp.answer('Debugger.resume', () => {
      cdp.emit('Debugger.resumed', {});

      return {};
    });

    expect(resultText(await debug.step('resume'))).toContain(`Resumed`);
  });
});
