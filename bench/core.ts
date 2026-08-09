/* oxlint-disable node/no-sync -- a question file is read once, at startup, before anything runs. */

import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { oneLine } from 'common-tags';

// Every decision the benchmark makes: what a question file may say, what each arm is told, what a
// CLI result means, and how the numbers are summarized.
// run.ts is the shell around it and holds no decision of its own, which is what makes the whole
// tool testable without spawning anything.

export type ArmName = 'control' | 'treatment';

export type SourceRootKind = 'decompile' | 'install' | 'ui-bundle';

export interface RubricPoint {
  readonly weight: number;
  readonly text: string;
}

export interface Question {
  readonly slug: string;
  readonly title: string;
  readonly prompt: string;
  readonly verifiedAnswer: string;
  readonly rubric: readonly RubricPoint[];
  readonly roots: readonly SourceRootKind[];
}

export interface ResolvedRoot {
  readonly kind: SourceRootKind;
  /**
   * What the prompts call the root: the arms never see a kind slug.
   */
  readonly label: string;
  readonly path: string;
}

export interface CliResult {
  readonly answer: string;
  readonly outputTokens: number;
  readonly freshInputTokens: number;
  readonly cacheReadTokens: number;
  readonly costUsd: number | undefined;
  readonly numTurns: number | undefined;
  readonly cliDurationMs: number | undefined;
  readonly apiDurationMs: number | undefined;
}

export type InvocationOutcome =
  | { readonly status: 'ok'; readonly result: CliResult }
  | { readonly status: 'retryable'; readonly fault: string };

export interface JudgeVerdict {
  readonly score: number;
  readonly justification: string;
}

export interface RunRecord {
  readonly question: string;
  readonly arm: ArmName;
  readonly run: number;
  readonly attempts: number;
  readonly score: number;
  readonly justification: string;
  readonly headlineTokens: number;
  readonly freshInputTokens: number;
  readonly outputTokens: number;
  readonly cacheReadTokens: number;
  readonly costUsd: number | undefined;
  readonly numTurns: number | undefined;
  readonly wallMs: number;
  readonly durationMs: number;
  readonly durationSource: 'api' | 'cli' | 'wall';
}

/**
 * A run that exhausted its retries. It scores nothing: the summary reports it and moves on.
 */
export interface RunFailure {
  readonly question: string;
  readonly arm: ArmName;
  readonly run: number;
  readonly fault: string;
}

export interface MetricStats {
  readonly mean: number;
  readonly median: number;
}

export interface ArmAggregate {
  readonly question: string;
  readonly arm: ArmName;
  readonly runs: number;
  readonly score: MetricStats;
  readonly headlineTokens: MetricStats;
  readonly freshInputTokens: MetricStats;
  readonly outputTokens: MetricStats;
  readonly cacheReadTokens: MetricStats;
  /**
   * Undefined where no run in the group reported a cost, so an absent price never reads as a free
   * one.
   */
  readonly costUsd: MetricStats | undefined;
  readonly wallMs: MetricStats;
  readonly durationMs: MetricStats;
  /**
   * Which clocks the group's durations came from, deduplicated.
   * More than one means the mean pools scales that are not comparable, which no single number can
   * disclose on its own.
   */
  readonly durationSources: ReadonlyArray<RunRecord['durationSource']>;
}

export interface BenchSummary {
  readonly startedAt: string;
  readonly model: string;
  readonly judgeModel: string;
  readonly concurrency: number;
  /**
   * True where any run reported no API duration, so its wall time carries the queueing.
   */
  readonly areWallTimesDistorted: boolean;
  readonly runs: readonly RunRecord[];
  readonly failures: readonly RunFailure[];
  readonly aggregates: readonly ArmAggregate[];
}

/**
 * Where the treatment arm reads the skill from, relative to its workspace.
 * The driver's copy step and the treatment prompt both derive from this one constant, so the file
 * an arm is told to open is by construction the file the driver put there.
 */
export const skillEntryPoint = '.claude/skills/cs2-modding/SKILL.md';

/**
 * The shipped skill folder, relative to the repository root, copied in for the treatment arm.
 */
export const skillSourceDir = 'plugins/cs2-modding/skills/cs2-modding';

/**
 * A rubric splits ten points, so a judge scoring each point in turn lands on the /10 scale.
 */
const rubricTotalWeight = 10;

/**
 * Few enough that a judge weighs them all, more than one so a score is not all-or-nothing.
 */
const rubricSize = { min: 2, max: 4 };

/**
 * Costs run to fractions of a cent, so a summary that rounds them to two places loses runs.
 */
const costDecimals = 4;

/**
 * How much of an unparseable judge answer the fault quotes.
 */
const faultExcerptLength = 200;

// Where each root comes from, per docs/SOURCES.md: the user chooses the decompile and the
// reformatted UI bundle copy and only the setup record names them, while the toolchain exports the
// install.
// The install keeps the record as a fallback because a session started before the toolchain did
// inherits none of the CSII_* variables.
const rootSources: Readonly<
  Record<SourceRootKind, { label: string; envVar?: string; recordKey: string }>
> = {
  'decompile': { label: 'the decompiled game', recordKey: 'Decompile root' },
  'install': {
    label: 'the installed game',
    envVar: 'CSII_INSTALLATIONPATH',
    recordKey: 'Game install'
  },
  'ui-bundle': { label: 'the reformatted UI bundle copy', recordKey: 'UI bundle copy' }
};

export type QuestionFaultCode =
  | 'missing-title'
  | 'missing-section'
  | 'empty-section'
  | 'rubric-size'
  | 'rubric-line'
  | 'rubric-weight-sum'
  | 'unknown-root'
  | 'duplicate-root'
  | 'missing-decompile-root';

/**
 * A question file the benchmark refuses, carrying the named fault rather than only a message.
 */
export class QuestionFault extends Error {
  public readonly fault: QuestionFaultCode;

  public constructor(fault: QuestionFaultCode, message: string) {
    super(message);

    this.name = 'QuestionFault';
    this.fault = fault;
  }
}

/**
 * Loads every question in a directory, in filename order.
 */
export function loadQuestions(directory: string): readonly Question[] {
  return readdirSync(directory)
    .filter(entry => entry.endsWith('.md'))
    .toSorted()
    .map(entry => loadQuestion(path.join(directory, entry)));
}

export function loadQuestion(filePath: string): Question {
  return parseQuestion(path.basename(filePath, '.md'), readFileSync(filePath, 'utf8'));
}

export function parseQuestion(slug: string, text: string): Question {
  const lines = text.split(/\r?\n/u);
  const titleLine = lines.find(line => line.startsWith('# '));

  if (!titleLine) {
    throw new QuestionFault('missing-title', `${slug}: the file has no "# " title line.`);
  }

  const sections = splitSections(lines);

  return {
    slug,
    title: titleLine.slice('# '.length).trim(),
    prompt: requireSection(slug, sections, 'Prompt'),
    verifiedAnswer: requireSection(slug, sections, 'Verified answer'),
    rubric: parseRubric(slug, requireSection(slug, sections, 'Rubric')),
    roots: parseRoots(slug, requireSection(slug, sections, 'Roots'))
  };
}

/**
 * Reads the setup record's fixed `Key: value` lines; `(none)` values are kept as written.
 */
export function parseSetupRecord(text: string): ReadonlyMap<string, string> {
  const record = new Map<string, string>();

  for (const line of text.split(/\r?\n/u)) {
    const fields = /^(?<key>[A-Za-z][^:]*):\s*(?<value>.*)$/u.exec(line.trim())?.groups;

    if (fields?.key != undefined && fields.value != undefined) {
      record.set(fields.key, fields.value.trim());
    }
  }

  return record;
}

/**
 * Reads one of the driver's count flags, refusing the values that make a run do nothing quietly.
 * `Number('two')` is NaN, and both NaN and zero size an empty worker pool that leaves the driver
 * holding a result array it never filled; a zero run count writes a summary of nothing and exits
 * clean, which reads exactly like a benchmark that ran.
 */
export function parseCount(flag: string, raw: string): number {
  const value = Number(raw);

  if (!Number.isInteger(value) || value < 1) {
    throw new Error(`--${flag} takes a whole number of 1 or more, not "${raw}".`);
  }

  return value;
}

/**
 * Resolves the roots a question asks for, in the order it asks for them.
 * Throws on the first unresolvable one: an uneven or silently short root set would make the two
 * arms incomparable, and finding that out after the spend is worse than not running.
 */
export function resolveRoots(
  kinds: readonly SourceRootKind[],
  record: ReadonlyMap<string, string>,
  env: Readonly<Record<string, string | undefined>>
): readonly ResolvedRoot[] {
  return kinds.map(kind => {
    const source = rootSources[kind];
    const fromEnv = source.envVar ? env[source.envVar] : undefined;
    const fromRecord = record.get(source.recordKey);
    const resolved = fromEnv ?? (fromRecord && fromRecord != '(none)' ? fromRecord : undefined);

    if (!resolved) {
      const variable = source.envVar ? `${source.envVar} is unset and ` : '';

      throw new Error(oneLine`
        The ${kind} root is unresolvable: ${variable}the setup record's "${source.recordKey}" is
        missing or (none). Run the cs2-modding-setup skill.
      `);
    }

    return { kind, label: source.label, path: resolved };
  });
}

/**
 * Builds one arm's prompt.
 * The two differ in what they may read from and nothing else: the control arm is told to use no
 * skills and read only the roots, the treatment arm is handed the skill's entry point and may read
 * that tree too.
 * Both carry the same closure clause, because the `Read` grant the driver passes is machine-wide:
 * the prompt is the only thing keeping either arm inside its roots, so an arm missing the clause
 * is an arm free to score points off sources the other one was forbidden.
 */
export function buildArmPrompt(input: {
  readonly arm: ArmName;
  readonly question: Question;
  readonly roots: readonly ResolvedRoot[];
}): string {
  const howToAnswer =
    input.arm == 'control'
      ? [
          'Answer from the source roots listed below and from nothing else.',
          oneLine`
            Use no skills, and read nothing beyond the provided roots: none of this machine's
            other repositories in particular.
          `
        ]
      : [
          'Answer from the documentation named here and from the source roots listed below.',
          oneLine`
            Read nothing beyond that documentation and the provided roots: none of this machine's
            other repositories in particular.
          `,
          oneLine`
            Documentation entry point: ${skillEntryPoint}, relative to your working directory.
            Read it first; it links the rest of the tree, and every link in it resolves on disk.
          `,
          oneLine`
            The documentation tells you to look for a recorded file naming the local sources.
            There is none here: the roots below are already resolved, so use them wherever it
            says to.
          `
        ];

  return [
    input.question.prompt,
    '',
    '## How to answer',
    ...howToAnswer,
    '',
    oneLine`
      The roots below are absolute paths on this machine. Read them with the Read, Grep and Glob
      tools; no other tool is available to you.
    `,
    '',
    '## Source roots',
    '',
    ...input.roots.map(root => `- ${root.label}: ${root.path}`),
    '',
    '## What to produce',
    '',
    oneLine`
      A direct, self-contained answer to the question above, stating the files it rests on.
      Nobody will ask you a follow-up question, so answer in full the first time.
    `
  ].join('\n');
}

/**
 * Builds the judge prompt.
 * It carries no arm label, no source roots and no skill path, so nothing but the answer's own text
 * can tell the judge which arm produced it.
 */
export function buildJudgePrompt(input: {
  readonly question: Question;
  readonly candidateAnswer: string;
}): string {
  return [
    oneLine`
      You are grading one candidate answer to a hard Cities: Skylines II modding question, against
      an answer the maintainer verified against the game itself.
    `,
    '',
    '## The question',
    '',
    input.question.prompt,
    '',
    '## The verified answer',
    '',
    input.question.verifiedAnswer,
    '',
    '## The rubric',
    '',
    oneLine`
      ${rubricTotalWeight} points, split over the key points below. Award each point's weight in
      full or in part, by how completely the candidate answer carries it.
    `,
    '',
    ...input.question.rubric.map(point => `- ${point.weight} points: ${point.text}`),
    '',
    '## The candidate answer',
    '',
    input.candidateAnswer,
    '',
    '## How to grade',
    '',
    oneLine`
      Score against the rubric alone. Never grade on where an answer says its knowledge came from,
      on which files it cites, on its style, or on how confident it sounds.
    `,
    'A claim the verified answer contradicts loses the points it touches, however well argued.',
    '',
    'End your reply with exactly these two lines:',
    '',
    'SCORE: <a whole number from 0 to 10>',
    'JUSTIFICATION: <one line saying what the score turns on>'
  ].join('\n');
}

/**
 * Reads one `claude -p --output-format json` invocation into a result, or classifies it as a
 * retryable infrastructure failure.
 * A run that produced an answer is never retryable: a bad answer is a measurement.
 */
export function classifyInvocation(input: {
  readonly exitCode: number | null;
  readonly timedOut: boolean;
  readonly stdout: string;
  readonly stderr: string;
}): InvocationOutcome {
  if (input.timedOut) {
    return { status: 'retryable', fault: 'the run hit its timeout' };
  }

  if (input.exitCode != 0) {
    const detail = input.stderr.trim().split('\n')[0] ?? '';

    return { status: 'retryable', fault: `the CLI exited with code ${input.exitCode}: ${detail}` };
  }

  let payload: unknown;

  try {
    payload = JSON.parse(input.stdout);
  } catch {
    return { status: 'retryable', fault: 'the CLI output did not parse as JSON' };
  }

  if (!isRecord(payload)) {
    return { status: 'retryable', fault: 'the CLI output was not a JSON object' };
  }

  const { is_error: isError, subtype, result: answer, usage } = payload;

  if (isError == true || subtype != 'success') {
    return { status: 'retryable', fault: `the CLI reported subtype ${String(subtype)}` };
  }

  if (typeof answer != 'string' || !answer.trim() || !isRecord(usage)) {
    return { status: 'retryable', fault: 'the CLI result carried no answer or no usage block' };
  }

  const outputTokens = readNumber(usage, 'output_tokens');

  if (outputTokens == undefined) {
    return { status: 'retryable', fault: 'the usage block carried no output_tokens' };
  }

  return {
    status: 'ok',
    result: {
      answer,
      outputTokens,
      freshInputTokens:
        (readNumber(usage, 'input_tokens') ?? 0) +
        (readNumber(usage, 'cache_creation_input_tokens') ?? 0),
      cacheReadTokens: readNumber(usage, 'cache_read_input_tokens') ?? 0,
      costUsd: readNumber(payload, 'total_cost_usd'),
      numTurns: readNumber(payload, 'num_turns'),
      cliDurationMs: readNumber(payload, 'duration_ms'),
      apiDurationMs: readNumber(payload, 'duration_api_ms')
    }
  };
}

/**
 * Reads the judge's two trailing lines. An unparseable verdict is a fault, never a zero.
 * Both lines are read leniently and from the end: a judge that emphasises its verdict
 * (`**SCORE: 8**`), restates the scale (`SCORE: 8/10`) or awards a half point is answering, not
 * failing, and rejecting it would spend three identical retries to reach the same formatting.
 * The leniency has to cover both lines or it costs what it saves, since a judge that emphasises
 * one emphasises the other and the justification would go quietly missing. Reading the last match
 * is what makes it safe: a deliberation may name a provisional score, and the line the prompt
 * asked for still wins.
 * A score outside the scale is a fault rather than a number: a judge grading out of 100 would
 * otherwise carry its arm's mean off the axis with nothing in the summary marking it.
 */
export function parseJudgeVerdict(text: string): JudgeVerdict {
  const score = lastMatch(text, /^\W*SCORE\W*(?<value>\d+(?:\.\d+)?)/gmu);

  if (score == undefined) {
    throw new Error(
      `The judge answer carried no "SCORE:" line: ${text.slice(0, faultExcerptLength)}`
    );
  }

  if (Number(score) > rubricTotalWeight) {
    throw new Error(
      `The judge scored ${score}, off the /${rubricTotalWeight} scale: ${text.slice(
        0,
        faultExcerptLength
      )}`
    );
  }

  // Trailing emphasis is the one decoration the capture keeps, since the line runs to its end,
  // and stripping it can leave nothing behind on a line that was all decoration.
  const justification = lastMatch(text, /^\W*JUSTIFICATION\W*(?<value>.+)$/gmu)
    ?.replace(/\*+$/u, '')
    .trim();

  return {
    score: Number(score),
    justification: justification?.length ? justification : '(none given)'
  };
}

/**
 * Joins one invocation, its judge verdict and the driver's own wall clock into a run record.
 */
export function buildRunRecord(input: {
  readonly question: string;
  readonly arm: ArmName;
  readonly run: number;
  readonly attempts: number;
  readonly wallMs: number;
  readonly result: CliResult;
  readonly verdict: JudgeVerdict;
}): RunRecord {
  const { result } = input;

  return {
    ...durationOf(result, input.wallMs),
    question: input.question,
    arm: input.arm,
    run: input.run,
    attempts: input.attempts,
    score: input.verdict.score,
    justification: input.verdict.justification,
    headlineTokens: result.freshInputTokens + result.outputTokens,
    freshInputTokens: result.freshInputTokens,
    outputTokens: result.outputTokens,
    cacheReadTokens: result.cacheReadTokens,
    costUsd: result.costUsd,
    numTurns: result.numTurns,
    wallMs: input.wallMs
  };
}

/**
 * Which clock a run's duration comes from.
 * The API duration is the only measurement concurrency does not distort, so a run says where its
 * number came from rather than quietly mixing the two scales.
 */
function durationOf(
  result: CliResult,
  wallMs: number
): Pick<RunRecord, 'durationMs' | 'durationSource'> {
  if (result.apiDurationMs != undefined) {
    return { durationMs: result.apiDurationMs, durationSource: 'api' };
  }

  if (result.cliDurationMs != undefined) {
    return { durationMs: result.cliDurationMs, durationSource: 'cli' };
  }

  return { durationMs: wallMs, durationSource: 'wall' };
}

/**
 * Means and medians per arm per question, in question then arm order.
 */
export function aggregate(records: readonly RunRecord[]): readonly ArmAggregate[] {
  const groups = new Map<string, readonly RunRecord[]>();

  for (const record of records) {
    const key = `${record.question} ${record.arm}`;

    groups.set(key, [...(groups.get(key) ?? []), record]);
  }

  return [...groups.values()]
    .map(group => {
      const [first] = group;

      assert.ok(first != undefined, 'a group exists only because a record went into it');

      const costs = group.map(record => record.costUsd).filter(cost => cost != undefined);

      return {
        question: first.question,
        arm: first.arm,
        runs: group.length,
        score: statsOf(group.map(record => record.score)),
        headlineTokens: statsOf(group.map(record => record.headlineTokens)),
        freshInputTokens: statsOf(group.map(record => record.freshInputTokens)),
        outputTokens: statsOf(group.map(record => record.outputTokens)),
        cacheReadTokens: statsOf(group.map(record => record.cacheReadTokens)),
        costUsd: costs.length ? statsOf(costs) : undefined,
        wallMs: statsOf(group.map(record => record.wallMs)),
        durationMs: statsOf(group.map(record => record.durationMs)),
        durationSources: [...new Set(group.map(record => record.durationSource))].toSorted()
      };
    })
    .toSorted((a, b) => a.question.localeCompare(b.question) || a.arm.localeCompare(b.arm));
}

export function buildSummary(input: {
  readonly startedAt: string;
  readonly model: string;
  readonly judgeModel: string;
  readonly concurrency: number;
  readonly runs: readonly RunRecord[];
  readonly failures: readonly RunFailure[];
}): BenchSummary {
  return {
    ...input,
    areWallTimesDistorted: input.runs.some(record => record.durationSource != 'api'),
    aggregates: aggregate(input.runs)
  };
}

export function renderMarkdownSummary(summary: BenchSummary): string {
  const runColumns = [
    'question',
    'arm',
    'run',
    'score',
    'headline',
    'fresh in',
    'out',
    'cache read',
    'cost usd',
    'turns',
    'wall ms',
    'duration ms',
    'duration',
    'attempts',
    'justification'
  ];

  const aggregateColumns = [
    'question',
    'arm',
    'runs',
    'score mean',
    'score median',
    'headline mean',
    'headline median',
    'duration mean ms',
    'duration median ms',
    'duration src',
    'cost mean usd'
  ];

  return [
    '# Benchmark summary',
    '',
    `- Started: ${summary.startedAt}`,
    `- Arm model: ${summary.model}, judge model: ${summary.judgeModel}`,
    `- Concurrency: ${summary.concurrency}`,
    ...(summary.areWallTimesDistorted
      ? ['- Wall times are concurrency-distorted: a run reported no API duration.']
      : []),
    '',
    '## Runs',
    '',
    ...table(
      runColumns,
      summary.runs.map(record => [
        record.question,
        record.arm,
        String(record.run),
        String(record.score),
        String(record.headlineTokens),
        String(record.freshInputTokens),
        String(record.outputTokens),
        String(record.cacheReadTokens),
        record.costUsd?.toFixed(costDecimals) ?? '',
        String(record.numTurns ?? ''),
        String(record.wallMs),
        String(record.durationMs),
        record.durationSource,
        String(record.attempts),
        record.justification
      ])
    ),
    '',
    '## Per arm',
    '',
    ...table(
      aggregateColumns,
      summary.aggregates.map(entry => [
        entry.question,
        entry.arm,
        String(entry.runs),
        entry.score.mean.toFixed(2),
        entry.score.median.toFixed(2),
        entry.headlineTokens.mean.toFixed(0),
        entry.headlineTokens.median.toFixed(0),
        entry.durationMs.mean.toFixed(0),
        entry.durationMs.median.toFixed(0),
        entry.durationSources.join('+'),
        entry.costUsd?.mean.toFixed(costDecimals) ?? '(none reported)'
      ])
    ),
    ...(summary.failures.length
      ? [
          '',
          '## Failed runs',
          '',
          ...table(
            ['question', 'arm', 'run', 'fault'],
            summary.failures.map(failure => [
              failure.question,
              failure.arm,
              String(failure.run),
              failure.fault
            ])
          )
        ]
      : []),
    ''
  ].join('\n');
}

function requireSection(
  slug: string,
  sections: ReadonlyMap<string, string>,
  heading: string
): string {
  const body = sections.get(heading);

  if (body == undefined) {
    throw new QuestionFault('missing-section', `${slug}: no "## ${heading}" section.`);
  }

  if (!body) {
    throw new QuestionFault('empty-section', `${slug}: the "## ${heading}" section is empty.`);
  }

  return body;
}

function splitSections(lines: readonly string[]): Map<string, string> {
  const sections = new Map<string, string[]>();
  let current: string[] | undefined;

  for (const line of lines) {
    const heading = /^##\s+(?<name>.+?)\s*$/u.exec(line)?.groups?.name;

    if (heading != undefined) {
      current = [];

      sections.set(heading, current);
    } else if (current) {
      current.push(line);
    }
  }

  return new Map([...sections].map(([heading, body]) => [heading, body.join('\n').trim()]));
}

function parseRubric(slug: string, body: string): readonly RubricPoint[] {
  const points = body
    .split('\n')
    .filter(line => line.trim())
    .map(line => {
      const point = /^-\s*(?<weight>\d+):\s*(?<text>.+)$/u.exec(line.trim())?.groups;

      if (point?.weight == undefined || point.text == undefined) {
        throw new QuestionFault(
          'rubric-line',
          `${slug}: rubric line "${line.trim()}" is not "- <weight>: <key point>".`
        );
      }

      return { weight: Number(point.weight), text: point.text.trim() };
    });

  if (points.length < rubricSize.min || points.length > rubricSize.max) {
    throw new QuestionFault(
      'rubric-size',
      oneLine`
        ${slug}: the rubric holds ${points.length} key points, and ${rubricSize.min} to
        ${rubricSize.max} are allowed.
      `
    );
  }

  const total = points.reduce((sum, point) => sum + point.weight, 0);

  if (total != rubricTotalWeight) {
    throw new QuestionFault(
      'rubric-weight-sum',
      `${slug}: the rubric weights sum to ${total}, and must sum to ${rubricTotalWeight}.`
    );
  }

  return points;
}

function parseRoots(slug: string, body: string): readonly SourceRootKind[] {
  const roots: SourceRootKind[] = [];

  for (const line of body.split('\n').filter(entry => entry.trim())) {
    const kind = line.trim().replace(/^-\s*/u, '');

    if (!(kind in rootSources)) {
      throw new QuestionFault(
        'unknown-root',
        `${slug}: "${kind}" is no source root. Known: ${Object.keys(rootSources).join(', ')}.`
      );
    }

    if (roots.includes(kind as SourceRootKind)) {
      throw new QuestionFault('duplicate-root', `${slug}: the "${kind}" root is listed twice.`);
    }

    roots.push(kind as SourceRootKind);
  }

  if (!roots.includes('decompile')) {
    throw new QuestionFault(
      'missing-decompile-root',
      `${slug}: every question brings the decompile root in, whatever else it needs.`
    );
  }

  return roots;
}

/**
 * The `value` group of the pattern's last match, the pattern being a global one.
 */
function lastMatch(text: string, pattern: RegExp): string | undefined {
  return [...text.matchAll(pattern)].at(-1)?.groups?.value;
}

function statsOf(readings: readonly number[]): MetricStats {
  const values = readings.toSorted((a, b) => a - b);
  const middle = Math.floor(values.length / 2);
  const upper = values[middle];

  assert.ok(upper != undefined, 'a metric is only taken over runs that happened');

  const lower = values[middle - 1] ?? upper;

  return {
    mean: values.reduce((sum, value) => sum + value, 0) / values.length,
    median: values.length % 2 ? upper : (lower + upper) / 2
  };
}

function table(columns: readonly string[], rows: ReadonlyArray<readonly string[]>): string[] {
  return [
    `| ${columns.join(' | ')} |`,
    `| ${columns.map(() => '---').join(' | ')} |`,
    ...rows.map(row => `| ${row.join(' | ')} |`)
  ];
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value == 'object' && value != null;
}

function readNumber(source: Record<string, unknown>, key: string): number | undefined {
  const value = source[key];

  return typeof value == 'number' ? value : undefined;
}
