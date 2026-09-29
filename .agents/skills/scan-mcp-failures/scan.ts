/* oxlint-disable node/no-sync -- sequential scan script, synchronous IO is intentional. */
/* oxlint-disable no-console -- the console carries the report. */

import assert from 'node:assert/strict';
import {
  existsSync,
  globSync,
  mkdirSync,
  readFileSync,
  renameSync,
  statSync,
  writeFileSync,
} from 'node:fs';
import { homedir } from 'node:os';
import path from 'node:path';
import { parseArgs } from 'node:util';

// Finds failed gameface and unity MCP calls in the Claude Code and Codex transcripts on this
// machine, skipping what earlier scans already counted.
//
// The ledger maps each transcript to a watermark: calls stamped before it were counted. A scan
// reads only transcripts modified since their watermark, reports the failures at or after it, and
// writes the watermarks it would advance to a pending file; --record promotes that file to the
// ledger. Recording is a separate step so an abandoned triage leaves its failures to the next run.

const repoRoot = path.resolve(import.meta.dirname, '../../..');
const stateDir = path.join(repoRoot, '.scratch/mcp-failures');
const ledgerPath = path.join(stateDir, 'ledger.json');
const pendingPath = path.join(stateDir, 'pending.json');
const failuresPath = path.join(stateDir, 'failures.jsonl');

const roots = [
  { dir: path.join(homedir(), '.claude/projects'), parse: parseClaude },
  { dir: path.join(homedir(), '.codex/sessions'), parse: parseCodex },
];

// Matches direct servers (mcp__unity__eval) and plugin-installed ones (mcp__plugin_<plugin>_...).
const toolPattern = new RegExp(
  String.raw`^(?:mcp__)?(?:plugin_(?:unity-devtools|coherent-gameface)_)?` +
    String.raw`(?<server>gameface|unity)__(?<tool>\w+)$`,
  'u'
);

// A call the user refused is counted apart from the failures. Pressing Esc on a running call writes
// the same result, so a hang the user escaped lands here too.
const rejectedPattern = /doesn't want to proceed/u;

// Codex outputs carry no error flag, so they fall back to the prefixes the servers emit.
const codexFailurePattern =
  /^(?:An error occurred invoking|Evaluation threw|Gameface CDP error|Cannot reach|Timed out)/u;

// How many of the agent's following moves a failure carries: its workaround says what it wanted.
const NEXT_MOVES = 4;

// Clip widths, keeping failures.jsonl readable in one pass.
const ERROR_WIDTH = 600;
const SIGNATURE_WIDTH = 90;
const MOVE_WIDTH = 300;
const ANSWER_WIDTH = 160;

const ISO_DATE_LENGTH = 10;

type Event =
  | { kind: 'use'; id: string; name: string; input: unknown; ts: string }
  | { kind: 'text'; text: string };

interface Result {
  readonly isError: boolean | undefined;
  readonly text: string;
}

interface Transcript {
  readonly events: readonly Event[];
  readonly results: ReadonlyMap<string, Result>;
}

interface Failure {
  readonly file: string;
  readonly ts: string;
  readonly server: string;
  readonly tool: string;
  readonly input: unknown;
  readonly error: string;
  readonly signature: string;
  readonly next: readonly string[];
}

interface ScanCounts {
  files: number;
  skipped: number;
  calls: number;
  rejected: number;
}

// The transcript fields read here; both formats carry many more.
interface ClaudeBlock {
  readonly type?: string;
  readonly id?: string;
  readonly name?: string;
  readonly input?: unknown;
  readonly text?: string;
  readonly tool_use_id?: string;
  readonly is_error?: boolean;
  readonly content?: unknown;
}

interface ClaudeRecord {
  readonly type?: string;
  readonly timestamp?: string;
  readonly message?: { readonly content?: unknown };
}

interface CodexRecord {
  readonly type?: string;
  readonly timestamp?: string;
  readonly payload?: {
    readonly type?: string;
    readonly role?: string;
    readonly name?: string;
    readonly namespace?: string;
    readonly call_id?: string;
    readonly arguments?: unknown;
    readonly output?: unknown;
    readonly content?: unknown;
  };
}

const { values: options } = parseArgs({ options: { record: { type: 'boolean' } } });

if (options.record) {
  recordScan();
} else {
  scan();
}

function recordScan(): void {
  if (!existsSync(pendingPath)) {
    console.error('no pending scan to record: run the scan first');
    process.exitCode = 1;

    return;
  }

  renameSync(pendingPath, ledgerPath);
  console.log(`recorded ${ledgerPath}`);
}

function scan(): void {
  const startedAt = new Date().toISOString();
  const ledger = readLedger();

  // Both accumulate across every transcript of the run.
  const pending: Record<string, string> = { ...ledger };
  const failures: Failure[] = [];
  const counts: ScanCounts = { files: 0, skipped: 0, calls: 0, rejected: 0 };

  for (const root of roots) {
    if (!existsSync(root.dir)) {
      continue;
    }

    for (const relative of globSync('**/*.jsonl', { cwd: root.dir })) {
      const file = path.join(root.dir, relative);
      const since = ledger[file] ?? '';

      if (since && statSync(file).mtime.toISOString() < since) {
        counts.skipped++;
        continue;
      }

      const raw = readFileSync(file, 'utf8');

      pending[file] = startedAt;

      if (!/(?:gameface|unity)__/u.test(raw)) {
        continue;
      }

      counts.files++;

      const transcript = root.parse(raw);

      for (const [index, event] of transcript.events.entries()) {
        if (event.kind != 'use' || event.ts < since) {
          continue;
        }

        const { server, tool } = toolPattern.exec(event.name)?.groups ?? {};

        if (!server || !tool) {
          continue;
        }

        const result = transcript.results.get(event.id);

        // In flight, or its session died: dropped for good, since holding the watermark back for it
        // would recount every failure after it on every run.
        if (!result) {
          continue;
        }

        counts.calls++;

        if (rejectedPattern.test(result.text)) {
          counts.rejected++;
          continue;
        }

        if (!isFailure(tool, result)) {
          continue;
        }

        const error = cleanError(result.text);

        failures.push({
          file,
          ts: event.ts,
          server,
          tool,
          input: event.input,
          error,
          signature: `${server} ${tool}: ${signatureOf(error)}`,
          next: nextMoves(transcript, index),
        });
      }
    }
  }

  mkdirSync(stateDir, { recursive: true });
  writeFileSync(pendingPath, JSON.stringify(pending, undefined, 2));
  writeFileSync(failuresPath, failures.map(failure => JSON.stringify(failure)).join('\n'));

  report(failures, counts);
}

function isFailure(tool: string, result: Result): boolean {
  if (result.isError) {
    return true;
  }

  // The game_status tool reports an unreachable endpoint as data rather than as an error.
  if (tool == 'game_status' && /"reachable":\s*false/u.test(result.text)) {
    return true;
  }

  return result.isError == null && codexFailurePattern.test(result.text);
}

// Drops the MCP wrapper prefix and the eval locals dump, whose type names otherwise dominate.
function cleanError(text: string): string {
  return text
    .replace(/^An error occurred invoking '\w+': /u, '')
    .replace(/\s+locals so far[\s\S]*$/u, '')
    .trim()
    .slice(0, ERROR_WIDTH);
}

// A grouping hint rather than a classification: one message can have several root causes.
function signatureOf(error: string): string {
  return error
    .replace(/\n[\s\S]*$/u, '')
    .replaceAll(/'[^']*'|`[^`]*`|"[^"]*"/gu, '_')
    .replaceAll(/\d+/gu, 'N')
    .slice(0, SIGNATURE_WIDTH);
}

function nextMoves(transcript: Transcript, index: number): string[] {
  return transcript.events.slice(index + 1, index + 1 + NEXT_MOVES).map(event => {
    if (event.kind == 'text') {
      return `TEXT ${event.text.slice(0, MOVE_WIDTH)}`;
    }

    const name = event.name.replace(/^mcp__(?:plugin_[a-z-]+_)?/u, '');
    const input = JSON.stringify(event.input).slice(0, MOVE_WIDTH);
    const answer = (transcript.results.get(event.id)?.text ?? '').replaceAll(/\s+/gu, ' ');

    return `USE ${name} ${input} => ${answer.slice(0, ANSWER_WIDTH)}`;
  });
}

function report(failures: readonly Failure[], counts: ScanCounts): void {
  console.log(
    `transcripts read: ${counts.files} (skipped as already scanned: ${counts.skipped}), ` +
      `calls: ${counts.calls}, rejected: ${counts.rejected}, failures: ${failures.length}`
  );

  const groups = Map.groupBy(failures, failure => failure.signature);
  const sorted = [...groups].toSorted(([, a], [, b]) => b.length - a.length);

  for (const [signature, members] of sorted) {
    const dates = members.map(failure => failure.ts.slice(0, ISO_DATE_LENGTH)).toSorted();

    console.log(`${members.length}\t${dates[0]}..${dates.at(-1)}\t${signature}`);
  }

  console.log(`details: ${failuresPath}`);
}

// Claude Code: assistant tool_use blocks answered by user tool_result blocks, paired by id.
function parseClaude(raw: string): Transcript {
  const events: Event[] = [];
  const results = new Map<string, Result>();

  for (const line of parseLines<ClaudeRecord>(raw)) {
    const content = line.message?.content;

    if (!Array.isArray(content)) {
      continue;
    }

    for (const block of content as readonly ClaudeBlock[]) {
      if (line.type == 'assistant' && block.type == 'tool_use' && block.id && block.name) {
        events.push({
          kind: 'use',
          id: block.id,
          name: block.name,
          input: block.input,
          ts: line.timestamp ?? '',
        });
      } else if (line.type == 'assistant' && block.type == 'text' && block.text?.trim()) {
        events.push({ kind: 'text', text: block.text });
      } else if (line.type == 'user' && block.type == 'tool_result' && block.tool_use_id) {
        results.set(block.tool_use_id, {
          isError: Boolean(block.is_error),
          text: textOf(block.content),
        });
      }
    }
  }

  return { events, results };
}

// Codex: response_item function_call answered by function_call_output, paired by call_id.
// Unverified against a real MCP call: no recorded Codex session has made one yet.
function parseCodex(raw: string): Transcript {
  const events: Event[] = [];
  const results = new Map<string, Result>();

  for (const line of parseLines<CodexRecord>(raw)) {
    const { payload } = line;

    if (line.type != 'response_item' || !payload) {
      continue;
    }

    if (payload.type == 'function_call' && payload.call_id && payload.name) {
      events.push({
        kind: 'use',
        id: payload.call_id,
        name: payload.namespace ? `${payload.namespace}__${payload.name}` : payload.name,
        input: parseArguments(payload.arguments),
        ts: line.timestamp ?? '',
      });
    } else if (payload.type == 'function_call_output' && payload.call_id) {
      const text =
        typeof payload.output == 'string' ? payload.output : JSON.stringify(payload.output);

      results.set(payload.call_id, { isError: undefined, text });
    } else if (payload.type == 'message' && payload.role == 'assistant') {
      events.push({ kind: 'text', text: textOf(payload.content) });
    }
  }

  return { events, results };
}

function* parseLines<T>(raw: string): Generator<T> {
  for (const line of raw.split('\n')) {
    try {
      // JSON boundary: the interfaces above list only optional fields, each checked before use.
      yield JSON.parse(line) as T;
    } catch {
      // A partial last line from a session still writing.
    }
  }
}

function parseArguments(value: unknown): unknown {
  try {
    return typeof value == 'string' ? (JSON.parse(value) as unknown) : value;
  } catch {
    return value;
  }
}

function textOf(content: unknown): string {
  if (typeof content == 'string') {
    return content;
  }

  if (!Array.isArray(content)) {
    return '';
  }

  return (content as readonly ClaudeBlock[])
    .map(part => part.text)
    .filter(text => typeof text == 'string')
    .join('\n');
}

function readLedger(): Record<string, string> {
  if (!existsSync(ledgerPath)) {
    console.log('no ledger: every transcript counts as unscanned, overlapping the recorded Hits');

    return {};
  }

  const ledger: unknown = JSON.parse(readFileSync(ledgerPath, 'utf8'));

  assert.ok(typeof ledger == 'object' && ledger != null);

  return ledger as Record<string, string>;
}
