/* oxlint-disable node/no-sync -- a task folder is read once, at startup, before anything runs. */

import { existsSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { oneLine } from 'common-tags';

// Every decision the benchmark makes: what a task file may say, what each arm is told and allowed,
// what a run's streamed output records, which files it produced, whether it leaked, what the judge
// ruled, and how the numbers are summarized.
// run.ts is the shell around it and holds no decision of its own, which is what makes the whole
// tool testable without spawning anything.

export type ArmName = 'control' | 'treatment';

export type TaskKind = 'feature request' | 'bug report';

export type SourceRootKind = 'decompile' | 'install' | 'ui-bundle';

export interface ChecklistItem {
  readonly name: string;
  readonly fallsIn: string;
  readonly avoids: string;
  /**
   * The shipped reference that teaches the item, relative to the cs2-modding skills folder.
   */
  readonly reference: string;
  readonly source: string;
}

export interface TaskText {
  readonly slug: string;
  readonly title: string;
  readonly kind: TaskKind;
  readonly prompt: string;
  readonly checklist: readonly ChecklistItem[];
  readonly roots: readonly SourceRootKind[];
}

export interface Task extends TaskText {
  readonly folders: {
    /**
     * Copied into the workspace before the run. A bug report seeds from its falling sample.
     */
    readonly seed: string | undefined;
    readonly falls: string;
    readonly avoids: string;
  };
}

export interface ResolvedRoot {
  readonly kind: SourceRootKind;
  readonly path: string;
}

/**
 * The absolute paths an invocation's deny rules are written against.
 */
export interface Machine {
  readonly repoRoot: string;
  readonly home: string;
  readonly tmpDir: string;
}

export interface WorkspaceSettings {
  readonly autoMemoryEnabled: false;
  readonly permissions: { readonly deny: readonly string[] };
}

/**
 * Everything the driver needs to start one arm: the prompt on stdin, the CLI flags, the
 * workspace's `.claude/settings.json`, and the skill folders copied into `.claude/skills/`.
 */
export interface ArmPlan {
  readonly prompt: string;
  readonly args: readonly string[];
  readonly settings: WorkspaceSettings;
  readonly skills: readonly string[];
}

export interface JudgePlan {
  readonly args: readonly string[];
  readonly settings: WorkspaceSettings;
}

export interface RawInvocation {
  readonly exitCode: number | null;
  readonly timedOut: boolean;
  readonly stdout: string;
  readonly stderr: string;
}

/**
 * One tool call of a run, read off the streamed output.
 */
export interface ToolCall {
  readonly name: string;
  readonly input: Readonly<Record<string, unknown>>;
  readonly isError: boolean;
  /**
   * A permission rule refused the call, so it never ran.
   */
  readonly isDenied: boolean;
  /**
   * The URLs a web search returned, where the stream exposes them.
   */
  readonly resultUrls: readonly string[];
  /**
   * The call ran a skill as a forked subagent.
   */
  readonly isForked: boolean;
}

export interface Spend {
  readonly costUsd: number | undefined;
  readonly numTurns: number | undefined;
  readonly cliDurationMs: number | undefined;
  readonly apiDurationMs: number | undefined;
}

export interface StreamedRun {
  readonly toolCalls: readonly ToolCall[];
  readonly finalMessage: string;
  readonly spend: Spend;
}

export type StreamOutcome =
  | { readonly status: 'ok'; readonly run: StreamedRun }
  | {
      readonly status: 'retryable';
      readonly fault: string;
      /**
       * What the attempt cost, where it got as far as reporting it.
       */
      readonly spend: Spend | undefined;
    };

/**
 * One attempt at a job's arm run: it wrote code, wrote none, or failed on the infrastructure.
 */
export type Attempt = {
  readonly spend: Spend | undefined;
  /**
   * The driver's own clock around the attempt, the duration of last resort.
   */
  readonly wallMs: number;
} & (
  | { readonly outcome: 'code' | 'no-code' }
  | { readonly outcome: 'failed'; readonly fault: string }
);

export type NextStep =
  | { readonly action: 'judge' }
  | { readonly action: 'retry' }
  | { readonly action: 'give-up'; readonly outcome: 'no-code' | 'failed' };

export type DurationSource = 'api' | 'cli' | 'wall';

export interface JobSpend {
  /**
   * Undefined where no attempt reported a cost, so an absent price never reads as a free one.
   */
  readonly costUsd: number | undefined;
  readonly numTurns: number;
  readonly durationMs: number;
  /**
   * Which clocks the duration pools, deduplicated: more than one means it adds scales that are
   * not comparable.
   */
  readonly durationSources: readonly DurationSource[];
}

export interface SkillUse {
  readonly skillsLoaded: readonly string[];
  /**
   * Markdown files read from the installed skills, relative to the skills folder.
   */
  readonly referencesRead: readonly string[];
}

export interface CollectedFile {
  /**
   * Relative to the workspace, so no absolute path in the judge prompt hints at the run.
   */
  readonly path: string;
  readonly content: string;
}

export type Verdict = 'pass' | 'fail' | 'n/a';

export interface ItemVerdict {
  readonly verdict: Verdict;
  /**
   * The line of the candidate's code the ruling turns on, as the judge quoted it.
   */
  readonly evidence: string;
}

/**
 * What a job's arm did and produced, the part of its record that is there whatever the outcome.
 * A job is one run of one task in one arm.
 */
export interface ArmRecord {
  readonly task: string;
  readonly kind: TaskKind;
  readonly arm: ArmName;
  readonly run: number;
  readonly attempts: readonly Attempt[];
  readonly spend: JobSpend;
  readonly contamination: readonly string[];
  readonly skillUse: SkillUse;
  readonly files: readonly string[];
  readonly finalMessage: string;
  readonly toolCalls: readonly ToolCall[];
}

/**
 * One job's whole record.
 * Judged: the arm wrote code and the judge ruled on it. No-code: no attempt up to the cap wrote
 * any. Failed: the infrastructure, the arm's or the judge's, gave out at the cap.
 */
export type JobRecord = ArmRecord &
  (
    | {
        readonly outcome: 'judged';
        readonly verdicts: ReadonlyArray<ItemVerdict & { readonly item: string }>;
      }
    | { readonly outcome: 'no-code' }
    | { readonly outcome: 'failed'; readonly fault: string }
  );

export interface Tally {
  readonly passed: number;
  readonly applicable: number;
}

export interface MetricStats {
  readonly mean: number;
  readonly median: number;
}

export interface ArmSummary {
  readonly arm: ArmName;
  /**
   * Over the scored runs: judged and clean.
   */
  readonly tally: Tally;
  readonly scored: number;
  /**
   * Over the runs that finished, judged or no-code, contaminated or not.
   */
  readonly skillLoaded: { readonly runs: number; readonly of: number };
  readonly contaminated: number;
  readonly noCode: number;
  readonly failed: number;
  /**
   * Over the runs that finished; undefined where none reported a cost.
   */
  readonly costUsd: MetricStats | undefined;
  readonly numTurns: MetricStats | undefined;
  readonly durationMs: MetricStats | undefined;
  readonly durationSources: readonly DurationSource[];
}

export interface BenchSummary {
  readonly startedAt: string;
  readonly model: string;
  readonly effort: string;
  readonly judgeModel: string;
  readonly arms: readonly ArmSummary[];
  readonly kinds: ReadonlyArray<{
    readonly kind: TaskKind;
    readonly arm: ArmName;
    readonly tally: Tally;
  }>;
  readonly tasks: ReadonlyArray<{
    readonly task: string;
    readonly kind: TaskKind;
    readonly arm: ArmName;
    readonly tally: Tally;
  }>;
  /**
   * Each item's verdict counts per arm, over the scored runs.
   */
  readonly items: ReadonlyArray<{
    readonly task: string;
    readonly item: string;
    readonly counts: ReadonlyArray<{
      readonly arm: ArmName;
      readonly pass: number;
      readonly fail: number;
      readonly notApplicable: number;
    }>;
  }>;
  /**
   * Which skill files the treatment runs read, and in how many runs.
   */
  readonly references: ReadonlyArray<{ readonly reference: string; readonly runs: number }>;
  readonly jobs: readonly JobRecord[];
}

/**
 * The file a task folder holds its text in, beside its sample folders.
 */
export const taskFileName = 'task.md';

/**
 * The prefix of every workspace directory the driver creates.
 * The control arm's deny rule for the skill copies in concurrent treatment workspaces matches on
 * it, so the two change together.
 */
export const workspacePrefix = 'cs2-bench-';

/**
 * Where the shipped skills live, relative to the repository root.
 */
export const skillsSourceDir = 'plugins/cs2-modding/skills';

/**
 * The skills the treatment arm has installed, and the only ones a skill load counts for: both
 * arms also list the CLI's bundled skills.
 */
export const cs2Skills = [
  'cs2-mod-project',
  'cs2-modding',
  'cs2-modding-setup',
  'cs2-modding-ui'
] as const;

const taskKinds: readonly TaskKind[] = ['feature request', 'bug report'];

/**
 * Enough items that a run's score is not all-or-nothing, few enough that the judge weighs each.
 */
const checklistSize = { min: 3, max: 6 };

const itemFields = {
  'Falls in': 'fallsIn',
  'Avoids': 'avoids',
  'Reference': 'reference',
  'Source': 'source'
} as const satisfies Record<string, keyof ChecklistItem>;

// Where each root comes from, per docs/SOURCES.md: the user chooses the decompile and the
// reformatted UI bundle copy and only the setup record names them, while the toolchain exports the
// install.
// The install keeps the record as a fallback because a session started before the toolchain did
// inherits none of the CSII_* variables.
const rootSources: Readonly<Record<SourceRootKind, { envVar?: string; recordKey: string }>> = {
  'decompile': { recordKey: 'Decompile root' },
  'install': { envVar: 'CSII_INSTALLATIONPATH', recordKey: 'Game install' },
  'ui-bundle': { recordKey: 'UI bundle copy' }
};

/**
 * How a modder would say where each root is, in the order the prompt names them.
 * The decompile comes first so the UI bundle's "its" has the game to refer to.
 */
const rootClauses: Readonly<Record<SourceRootKind, (rootPath: string) => string>> = {
  'decompile': rootPath => `the game's decompiled source is at ${rootPath}`,
  'install': rootPath => `the game is installed at ${rootPath}`,
  'ui-bundle': rootPath => `a reformatted copy of its UI bundle is at ${rootPath}`
};

/**
 * The two lines every prompt ends on: a headless run must not end on a clarifying question, and
 * the files answering the request must be its only side effects.
 */
const closingLines = [
  `Write the code without asking me questions.`,
  oneLine`
    Don't build, test or run anything: your only side effects should be the files that answer this
    request.
  `
];

/**
 * Every tool an arm has. The agent tool stays out, which is half of how subagents stay denied.
 */
const armTools = [
  'Read',
  'Glob',
  'Grep',
  'Write',
  'Edit',
  'Bash',
  'WebSearch',
  'WebFetch',
  'Skill'
];

/**
 * Folders whose files are never collected: build output, in case an arm built despite being told
 * not to, and the workspace configuration the driver wrote.
 */
const uncollectedFolders = new Set(['bin', 'obj', 'node_modules', '.claude']);

/**
 * What a URL of this repository contains, lowercased: the GitHub page, its raw files and its
 * clones all carry the owner and name.
 */
const repoUrlMarker = 'citiesskylinesmodding/agents-plugins';

/**
 * A path-shaped token in shell text: absolute, or from the home directory by `~` or `$HOME`.
 * The lookbehind keeps the tail of a relative path from reading as an absolute one.
 */
const shellPathPattern = /(?<![\w.~}$-])(?:~|\$\{?HOME\}?)?\/[^\s'"`;|&<>()]*/gu;

/**
 * One verdict line of a judge reply, decoration around it tolerated.
 */
const judgeLinePattern =
  /^[\W_]*ITEM\s+(?<item>\d+)\W*?(?<verdict>PASS|FAIL|N\/A|NA|NOT[ -]APPLICABLE)\b\**\s*(?:\|\s*(?<evidence>.*))?$/gimu;

/**
 * How much of an unreadable judge reply the fault quotes.
 */
const faultExcerptLength = 200;

/**
 * The shortest Markdown code fence; a file holding a run of backticks gets a longer one.
 */
const minimumFence = 3;

export const armNames: readonly ArmName[] = ['control', 'treatment'];

/**
 * Costs run to fractions of a cent, so a summary that rounds them to two places loses runs.
 */
const costDecimals = 4;

export type TaskFaultCode =
  | 'missing-title'
  | 'missing-section'
  | 'empty-section'
  | 'unknown-kind'
  | 'checklist-size'
  | 'item-field'
  | 'duplicate-item'
  | 'unknown-root'
  | 'duplicate-root'
  | 'missing-decompile-root'
  | 'missing-sample'
  | 'empty-sample'
  | 'seed-on-bug-report'
  | 'unknown-reference';

/**
 * A task the benchmark refuses, carrying the named fault rather than only a message.
 */
export class TaskFault extends Error {
  public readonly fault: TaskFaultCode;

  public constructor(fault: TaskFaultCode, message: string) {
    super(message);

    this.name = 'TaskFault';
    this.fault = fault;
  }
}

/**
 * Loads every task folder in a directory, in name order.
 */
export function loadTasks(directory: string, skillsDir: string): readonly Task[] {
  return readdirSync(directory, { withFileTypes: true })
    .filter(entry => entry.isDirectory())
    .map(entry => entry.name)
    .toSorted()
    .map(name => loadTask(path.join(directory, name), skillsDir));
}

/**
 * Loads one task folder: its text, its two calibration samples and its optional seed, with every
 * reference its checklist names resolved against the shipped skills.
 */
export function loadTask(directory: string, skillsDir: string): Task {
  const slug = path.basename(directory);
  const text = parseTask(slug, readFileSync(path.join(directory, taskFileName), 'utf8'));

  for (const item of text.checklist) {
    const reference = item.reference.replace(/#.*$/u, '');

    if (!existsSync(path.join(skillsDir, reference))) {
      throw new TaskFault(
        'unknown-reference',
        `${slug}: "${item.name}" names a reference no shipped skill holds: ${item.reference}.`
      );
    }
  }

  const falls = requireSample(slug, directory, 'falls');
  const avoids = requireSample(slug, directory, 'avoids');
  const seedDir = path.join(directory, 'seed');
  const hasSeedDir = existsSync(seedDir);

  if (text.kind == 'bug report' && hasSeedDir) {
    throw new TaskFault(
      'seed-on-bug-report',
      `${slug}: a bug report seeds from its falling sample, so it takes no seed folder.`
    );
  }

  const seed = text.kind == 'bug report' ? falls : hasSeedDir ? seedDir : undefined;

  return { ...text, folders: { seed, falls, avoids } };
}

export function parseTask(slug: string, text: string): TaskText {
  const lines = text.split(/\r?\n/u);
  const titleLine = lines.find(line => line.startsWith('# '));

  if (!titleLine) {
    throw new TaskFault('missing-title', `${slug}: the file has no "# " title line.`);
  }

  const sections = splitSections(lines, '##');
  const kind = requireSection(slug, sections, 'Kind');

  if (!taskKinds.includes(kind as TaskKind)) {
    throw new TaskFault(
      'unknown-kind',
      `${slug}: "${kind}" is no task kind. Known: ${taskKinds.join(', ')}.`
    );
  }

  return {
    slug,
    title: titleLine.slice('# '.length).trim(),
    kind: kind as TaskKind,
    prompt: requireSection(slug, sections, 'Prompt'),
    checklist: parseChecklist(slug, requireSection(slug, sections, 'Checklist')),
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
 * Resolves the roots a task asks for, in the order it asks for them.
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
    // An exported but empty variable is as unset as a missing one.
    const fromEnv = source.envVar && env[source.envVar] != '' ? env[source.envVar] : undefined;
    const fromRecord = record.get(source.recordKey);
    const resolved = fromEnv ?? (fromRecord && fromRecord != '(none)' ? fromRecord : undefined);

    if (!resolved) {
      const variable = source.envVar ? `${source.envVar} is unset and ` : '';

      throw new Error(oneLine`
        The ${kind} root is unresolvable: ${variable}the setup record's "${source.recordKey}" is
        missing or (none). Run the cs2-modding-setup skill.
      `);
    }

    return { kind, path: path.resolve(resolved) };
  });
}

/**
 * Plans one arm's invocation.
 * The two arms differ in the skills copied into the workspace and in the control's extra deny rule
 * for the treatment workspaces' copies, and in nothing else: the prompt and the flags are shared.
 */
export function planArm(input: {
  readonly arm: ArmName;
  readonly task: TaskText;
  readonly roots: readonly ResolvedRoot[];
  readonly workspace: string;
  readonly machine: Machine;
  readonly model: string;
  readonly effort: string;
}): ArmPlan {
  const workspaceRule = `/${input.workspace}/**`;
  const workspaces = path.join(input.machine.tmpDir, workspacePrefix);
  const siblingSkills = `Read(/${workspaces}*/.claude/skills/**)`;
  const shared = workspaceSettings(input.machine);

  return {
    prompt: buildArmPrompt(input.task, input.roots),
    args: [
      ...baseArgs(input.model, input.effort),
      '--permission-mode',
      'default',
      '--permission-prompts',
      'none',
      '--tools',
      armTools.join(','),
      '--allowed-tools',
      [
        ...armTools.filter(tool => tool != 'Write' && tool != 'Edit'),
        `Edit(${workspaceRule})`,
        `Write(${workspaceRule})`
      ].join(','),
      '--add-dir',
      ...input.roots.map(root => root.path)
    ],
    settings:
      input.arm == 'control'
        ? { ...shared, permissions: { deny: [...shared.permissions.deny, siblingSkills] } }
        : shared,
    skills: input.arm == 'treatment' ? cs2Skills : []
  };
}

/**
 * Plans the judge's invocation: no tools, no directory grant, no slash commands.
 */
export function planJudge(input: {
  readonly machine: Machine;
  readonly model: string;
  readonly effort: string;
}): JudgePlan {
  return {
    args: [...baseArgs(input.model, input.effort), '--disable-slash-commands', '--tools', ''],
    settings: workspaceSettings(input.machine)
  };
}

/**
 * Reads one `stream-json` invocation into its tool calls, final message and spend, or classifies
 * it as a retryable infrastructure failure.
 * The session transcript is never read: its format is internal, while the stream is the CLI's
 * documented output.
 */
export function readStream(input: RawInvocation): StreamOutcome {
  const events = input.stdout
    .split('\n')
    .filter(line => line.trim())
    .flatMap(line => {
      try {
        const event: unknown = JSON.parse(line);

        return isRecord(event) ? [event] : [];
      } catch {
        return [];
      }
    });

  const result = events.findLast(event => event.type == 'result');

  // The CLI exits nonzero on an error result, which still carries what the attempt cost.
  const spend: Spend | undefined = result && {
    costUsd: readNumber(result, 'total_cost_usd'),
    numTurns: readNumber(result, 'num_turns'),
    cliDurationMs: readNumber(result, 'duration_ms'),
    apiDurationMs: readNumber(result, 'duration_api_ms')
  };

  if (input.timedOut) {
    return { status: 'retryable', fault: 'the run hit its timeout', spend };
  }

  if (input.exitCode != 0) {
    const detail = input.stderr.trim().split('\n')[0] ?? '';

    return {
      status: 'retryable',
      fault: `the CLI exited with code ${input.exitCode}: ${detail}`,
      spend
    };
  }

  if (!result || !spend) {
    return { status: 'retryable', fault: 'the stream carried no result event', spend: undefined };
  }

  if (result.is_error == true || result.subtype != 'success') {
    return {
      status: 'retryable',
      fault: `the CLI reported subtype ${String(result.subtype)}`,
      spend
    };
  }

  if (typeof result.result != 'string') {
    return { status: 'retryable', fault: 'the result event carried no message', spend };
  }

  return {
    status: 'ok',
    run: { toolCalls: readToolCalls(events, result), finalMessage: result.result, spend }
  };
}

/**
 * The files of a run, from two snapshots of its workspace mapping each workspace-relative path to
 * a hash of its content, in path order: the ones it created or changed, which say whether it wrote
 * code, and every one the judge rules on, seeded files it left alone included.
 * An arm reaches files through the shell as readily as through `Write` and `Edit`, so only the
 * workspace itself can say what it produced; and a bug it never touched is still in the code.
 */
export function collectedFiles(
  before: ReadonlyMap<string, string>,
  after: ReadonlyMap<string, string>
): { readonly changed: readonly string[]; readonly judged: readonly string[] } {
  const judged = [...after.keys()]
    .filter(file => !file.split('/').some(segment => uncollectedFolders.has(segment)))
    .toSorted();

  return { changed: judged.filter(file => before.get(file) != after.get(file)), judged };
}

/**
 * What follows a job's attempts so far.
 * An attempt that wrote no code is asked again whole, as a modder would re-ask a stalled session,
 * under the same cap as an infrastructure failure, and the last attempt decides what a job that
 * reached the cap is.
 */
export function planNextAttempt(attempts: readonly Attempt[], maxAttempts: number): NextStep {
  const last = attempts.at(-1);

  if (last?.outcome == 'code') {
    return { action: 'judge' };
  }

  if (!last || attempts.length < maxAttempts) {
    return { action: 'retry' };
  }

  return { action: 'give-up', outcome: last.outcome };
}

/**
 * What a job cost over every attempt it made, the discarded ones included, so an arm that stalls
 * more never looks cheaper.
 * Each attempt's duration is the API's where reported: it is the one clock concurrency does not
 * distort.
 */
export function jobSpend(attempts: readonly Attempt[]): JobSpend {
  const costs = attempts.map(attempt => attempt.spend?.costUsd).filter(cost => cost != undefined);

  const durations = attempts.map(attempt => durationOf(attempt));

  return {
    costUsd: costs.length ? sum(costs) : undefined,
    numTurns: sum(attempts.map(attempt => attempt.spend?.numTurns ?? 0)),
    durationMs: sum(durations.map(([duration]) => duration)),
    durationSources: [...new Set(durations.map(([, source]) => source))].toSorted()
  };
}

/**
 * Why a run is contaminated, one finding per offending tool call; a clean run has none.
 * A run is contaminated when a call read outside its workspace and roots, named a denied location
 * in shell text, reached this repository on the web, or ran a skill as a forked subagent.
 * A call a deny rule refused never ran, so it leaked nothing.
 * The shell scan is best-effort: it sees the locations a command names, never the ones it computes.
 */
export function findContamination(input: {
  readonly toolCalls: readonly ToolCall[];
  readonly workspace: string;
  readonly roots: readonly ResolvedRoot[];
  readonly machine: Machine;
}): readonly string[] {
  const { workspace, machine } = input;
  const allowed = [workspace, ...input.roots.map(root => root.path)];

  const denied = deniedLocations(machine);

  const isOtherWorkspace = (target: string): boolean =>
    target.startsWith(path.join(machine.tmpDir, workspacePrefix)) && !isInside(target, workspace);

  const ran = input.toolCalls.filter(call => !call.isDenied);

  const reads = ran.flatMap(call =>
    readPaths(call, workspace)
      .filter(target => !allowed.some(root => isInside(target, root)))
      .filter(target => !isSavedToolOutput(target, machine.home))
      .map(target => `${call.name} read ${target}, outside the workspace and roots`)
  );

  const named = ran.flatMap(call =>
    shellPaths(call, machine.home)
      .filter(
        target => denied.some(location => isInside(target, location)) || isOtherWorkspace(target)
      )
      .filter(target => !isSavedToolOutput(target, machine.home))
      .map(target => `${call.name} named ${target}, a denied location`)
  );

  const reached = ran.flatMap(call =>
    reachedUrls(call)
      .filter(url => url.toLowerCase().includes(repoUrlMarker))
      .map(url => `${call.name} reached ${url}, this repository`)
  );

  const forked = ran
    .filter(call => call.isForked)
    .map(call => `${call.name} ${String(call.input.skill)} ran as a forked subagent`);

  return [...reads, ...named, ...reached, ...forked];
}

/**
 * Which cs2 skills a run loaded and which of their files it read.
 * An arm reads references with `cat` as often as with `Read`, so shell text counts too, a relative
 * path resolved against a leading `cd` or else the workspace the shell starts in; past that the
 * record is best-effort, since the shell keeps the directory an earlier call moved to.
 */
export function findSkillUse(input: {
  readonly toolCalls: readonly ToolCall[];
  readonly workspace: string;
}): SkillUse {
  const skillsDir = path.join(input.workspace, '.claude/skills');
  const ran = input.toolCalls.filter(call => !call.isDenied);

  const skillsLoaded = ran
    .filter(call => call.name == 'Skill' && !call.isError)
    .map(call => String(call.input.skill))
    .filter(skill => (cs2Skills as readonly string[]).includes(skill));

  const referencesRead = [
    ...ran.flatMap(call => readPaths(call, input.workspace)),
    ...ran.flatMap(call => shellFiles(call, input.workspace))
  ]
    .filter(file => file.endsWith('.md') && isInside(file, skillsDir))
    .map(file => path.posix.relative(skillsDir, file))
    // A glob or a brace expansion names a pattern, never the files it matched.
    .filter(file => !/[*?[{]/u.test(file));

  return {
    skillsLoaded: [...new Set(skillsLoaded)],
    referencesRead: [...new Set(referencesRead)].toSorted()
  };
}

/**
 * Builds the judge prompt.
 * It carries the request without the roots, the checklist without the references, the produced
 * files and the final message, and nothing naming the arm, so only the candidate's own text can
 * tell the judge where it came from.
 */
export function buildJudgePrompt(input: {
  readonly task: TaskText;
  readonly files: readonly CollectedFile[];
  readonly finalMessage: string;
}): string {
  return [
    oneLine`
      You are reviewing the code a Cities: Skylines II modder got in answer to the request below,
      against a checklist of traps and good practices. Rule on each item from the code.
    `,
    '',
    '## The request',
    '',
    input.task.prompt,
    '',
    '## The checklist',
    '',
    ...input.task.checklist.flatMap((item, index) => [
      `ITEM ${index + 1}: ${item.name}`,
      `- Falls in it: ${item.fallsIn}`,
      `- Avoids it: ${item.avoids}`,
      ''
    ]),
    '## The code',
    '',
    ...input.files.flatMap(file => {
      const longestRun = Math.max(0, ...(file.content.match(/`+/gu) ?? []).map(run => run.length));
      const fence = '`'.repeat(Math.max(minimumFence, longestRun + 1));

      return [`### ${file.path}`, '', fence, file.content, fence, ''];
    }),
    '## The final message',
    '',
    input.finalMessage.trim() || '(none)',
    '',
    '## How to rule',
    '',
    `Rule on each item:`,
    '',
    `- PASS when the code avoids the trap or follows the practice, as "Avoids it" describes.`,
    `- FAIL when the code falls into it, as "Falls in it" describes.`,
    oneLine`
      - N/A when the code never meets what the item is about, having answered the request by a
      route the item does not concern.
    `,
    '',
    oneLine`
      Rule on the code as written. The final message may tell you where to look, but a claim it
      makes that the code does not carry earns nothing. Never rule on where the code or the
      message says its knowledge came from, on style, or on whether the code would compile.
    `,
    '',
    oneLine`
      End your reply with exactly one line per item, in checklist order, quoting the one line of
      the code your ruling turns on, or (none) for N/A:
    `,
    '',
    'ITEM <n>: PASS|FAIL|N/A | <the quoted line>'
  ].join('\n');
}

/**
 * Reads the judge's verdict lines, one per checklist item. An unreadable reply is a fault, never a
 * verdict.
 * Lines are read leniently and the last one per item wins: a judge that emphasises its verdicts,
 * lowercases them or reconsiders one mid-reply is answering, and rejecting it would spend retries
 * reaching the same formatting.
 * An item missing or one past the checklist is a fault, since either means the judge ruled on a
 * list other than the one it was given.
 */
export function parseJudgeReply(
  text: string,
  checklist: readonly ChecklistItem[]
): ReadonlyArray<ItemVerdict & { readonly item: string }> {
  const itemCount = checklist.length;
  const verdicts = new Map<number, ItemVerdict>();

  for (const match of text.matchAll(judgeLinePattern)) {
    const { item, verdict, evidence } = match.groups ?? {};
    const number = Number(item);

    if (number < 1 || number > itemCount) {
      throw new Error(`The judge ruled on item ${number}, and the checklist holds ${itemCount}.`);
    }

    const quoted = evidence?.replace(/\*+$/u, '').trim();

    verdicts.set(number, {
      verdict: readVerdict(verdict ?? ''),
      evidence: quoted?.length ? quoted : '(none given)'
    });
  }

  return checklist.map((item, index) => {
    const verdict = verdicts.get(index + 1);

    if (!verdict) {
      const excerpt = text.slice(0, faultExcerptLength);

      throw new Error(`The judge reply carried no verdict for item ${index + 1}: ${excerpt}`);
    }

    return { item: item.name, ...verdict };
  });
}

/**
 * Summarizes the jobs on disk.
 * A contaminated run is excluded from every score, a no-code or failed one has none, and a
 * not-applicable item counts neither way; all of them stay listed.
 */
export function buildSummary(input: {
  readonly startedAt: string;
  readonly model: string;
  readonly effort: string;
  readonly judgeModel: string;
  readonly jobs: readonly JobRecord[];
}): BenchSummary {
  const { jobs } = input;
  const scored = jobs.filter(job => job.outcome == 'judged' && !job.contamination.length);
  const finished = jobs.filter(job => job.outcome != 'failed');
  const tasks = [...new Map(jobs.map(job => [job.task, job.kind]))];

  const arms = armNames.map((arm): ArmSummary => {
    const armScored = scored.filter(job => job.arm == arm);
    const armFinished = finished.filter(job => job.arm == arm);
    const costs = armFinished.map(job => job.spend.costUsd).filter(cost => cost != undefined);

    return {
      arm,
      tally: tallyOf(armScored),
      scored: armScored.length,
      skillLoaded: {
        runs: armFinished.filter(job => job.skillUse.skillsLoaded.length).length,
        of: armFinished.length
      },
      contaminated: armFinished.filter(job => job.contamination.length).length,
      noCode: armFinished.filter(job => job.outcome == 'no-code').length,
      failed: jobs.filter(job => job.arm == arm && job.outcome == 'failed').length,
      costUsd: statsOf(costs),
      numTurns: statsOf(armFinished.map(job => job.spend.numTurns)),
      durationMs: statsOf(armFinished.map(job => job.spend.durationMs)),
      durationSources: [
        ...new Set(armFinished.flatMap(job => job.spend.durationSources))
      ].toSorted()
    };
  });

  const references = new Map<string, number>();

  for (const job of finished.filter(candidate => candidate.arm == 'treatment')) {
    for (const reference of job.skillUse.referencesRead) {
      references.set(reference, (references.get(reference) ?? 0) + 1);
    }
  }

  return {
    startedAt: input.startedAt,
    model: input.model,
    effort: input.effort,
    judgeModel: input.judgeModel,
    arms,
    kinds: taskKinds.flatMap(kind =>
      armNames
        .filter(arm => scored.some(job => job.kind == kind && job.arm == arm))
        .map(arm => ({
          kind,
          arm,
          tally: tallyOf(scored.filter(job => job.kind == kind && job.arm == arm))
        }))
    ),
    tasks: tasks.flatMap(([task, kind]) =>
      armNames
        .filter(arm => scored.some(job => job.task == task && job.arm == arm))
        .map(arm => ({
          task,
          kind,
          arm,
          tally: tallyOf(scored.filter(job => job.task == task && job.arm == arm))
        }))
    ),
    items: tasks.flatMap(([task]) => {
      const taskRuns = scored.filter(job => job.task == task);
      const names = [...new Set(taskRuns.flatMap(job => verdictsOf(job).map(entry => entry.item)))];

      return names.map(item => ({
        task,
        item,
        counts: armNames.map(arm => {
          const rulings = taskRuns
            .filter(job => job.arm == arm)
            .flatMap(job => verdictsOf(job).filter(entry => entry.item == item))
            .map(entry => entry.verdict);

          return {
            arm,
            pass: rulings.filter(verdict => verdict == 'pass').length,
            fail: rulings.filter(verdict => verdict == 'fail').length,
            notApplicable: rulings.filter(verdict => verdict == 'n/a').length
          };
        })
      }));
    }),
    references: [...references]
      .map(([reference, runs]) => ({ reference, runs }))
      .toSorted((a, b) => b.runs - a.runs || a.reference.localeCompare(b.reference)),
    jobs
  };
}

export function renderReport(summary: BenchSummary): string {
  const unscored = summary.jobs.filter(job => job.outcome != 'judged' || job.contamination.length);
  const contaminated = summary.jobs.filter(job => job.contamination.length);
  const noCode = summary.jobs.filter(job => job.outcome == 'no-code');
  const failed = summary.jobs.filter(job => job.outcome == 'failed');

  return [
    '# Benchmark summary',
    '',
    `- Started: ${summary.startedAt}`,
    `- Arms: ${summary.model} at ${summary.effort} effort, judge: ${summary.judgeModel}`,
    ...(summary.arms.some(arm => arm.durationSources.some(source => source != 'api'))
      ? [
          oneLine`
            - Some durations are wall or CLI time, which concurrency distorts: an attempt reported
            no API duration.
          `
        ]
      : []),
    ...(unscored.length
      ? [`- ${unscored.length} run(s) left out of the scores, listed below.`]
      : []),
    '',
    '## Per arm',
    '',
    ...table(
      [
        'arm',
        'pass rate',
        'scored runs',
        'loaded a cs2 skill',
        'contaminated',
        'no code',
        'failed',
        'cost mean usd',
        'turns mean',
        'duration mean ms'
      ],
      summary.arms.map(arm => [
        arm.arm,
        rateOf(arm.tally),
        String(arm.scored),
        `${arm.skillLoaded.runs}/${arm.skillLoaded.of}`,
        String(arm.contaminated),
        String(arm.noCode),
        String(arm.failed),
        arm.costUsd?.mean.toFixed(costDecimals) ?? '(none reported)',
        arm.numTurns?.mean.toFixed(1) ?? '',
        arm.durationMs?.mean.toFixed(0) ?? ''
      ])
    ),
    '',
    '## Per task kind',
    '',
    ...table(
      ['kind', ...armNames],
      taskKinds
        .filter(kind => summary.kinds.some(entry => entry.kind == kind))
        .map(kind =>
          armRow(
            [kind],
            summary.kinds.filter(entry => entry.kind == kind),
            entry => rateOf(entry?.tally)
          )
        )
    ),
    '',
    '## Per task',
    '',
    ...table(
      ['task', 'kind', ...armNames],
      [...new Map(summary.tasks.map(entry => [entry.task, entry.kind]))].map(([task, kind]) =>
        armRow(
          [task, kind],
          summary.tasks.filter(entry => entry.task == task),
          entry => rateOf(entry?.tally)
        )
      )
    ),
    '',
    '## Per item',
    '',
    `Pass/fail/not-applicable counts over the scored runs.`,
    '',
    ...table(
      ['task', 'item', ...armNames],
      summary.items.map(entry =>
        armRow([entry.task, entry.item], entry.counts, counts =>
          counts ? `${counts.pass}/${counts.fail}/${counts.notApplicable}` : '–'
        )
      )
    ),
    '',
    '## Runs',
    '',
    ...table(
      [
        'task',
        'arm',
        'run',
        'outcome',
        'score',
        'attempts',
        'cost usd',
        'turns',
        'duration ms',
        'cs2 skills',
        'contaminated'
      ],
      summary.jobs.map(job => [
        job.task,
        job.arm,
        String(job.run),
        job.outcome,
        rateOf(job.outcome == 'judged' ? tallyOf([job]) : undefined),
        String(job.attempts.length),
        job.spend.costUsd?.toFixed(costDecimals) ?? '',
        String(job.spend.numTurns),
        String(job.spend.durationMs),
        job.skillUse.skillsLoaded.join(', '),
        job.contamination.length ? 'yes' : ''
      ])
    ),
    ...section(
      'References read by treatment runs',
      ['reference', 'runs'],
      summary.references.map(entry => [entry.reference, String(entry.runs)])
    ),
    ...section(
      'Contaminated runs',
      ['task', 'arm', 'run', 'findings'],
      contaminated.map(job => [job.task, job.arm, String(job.run), job.contamination.join('; ')])
    ),
    ...section(
      'No-code runs',
      ['task', 'arm', 'run', 'attempts'],
      noCode.map(job => [job.task, job.arm, String(job.run), String(job.attempts.length)])
    ),
    ...section(
      'Failed runs',
      ['task', 'arm', 'run', 'fault'],
      failed.map(job => [job.task, job.arm, String(job.run), job.fault])
    ),
    ''
  ].join('\n');
}

function requireSample(slug: string, directory: string, name: 'falls' | 'avoids'): string {
  const sample = path.join(directory, name);

  if (!existsSync(sample)) {
    throw new TaskFault('missing-sample', `${slug}: no "${name}" calibration sample folder.`);
  }

  if (!readdirSync(sample).length) {
    throw new TaskFault('empty-sample', `${slug}: the "${name}" calibration sample is empty.`);
  }

  return sample;
}

function requireSection(
  slug: string,
  sections: ReadonlyMap<string, string>,
  heading: string
): string {
  const body = sections.get(heading);

  if (body == undefined) {
    throw new TaskFault('missing-section', `${slug}: no "## ${heading}" section.`);
  }

  if (!body) {
    throw new TaskFault('empty-section', `${slug}: the "## ${heading}" section is empty.`);
  }

  return body;
}

/**
 * Splits lines into the bodies under each heading of one level, keyed by heading text.
 */
function splitSections(lines: readonly string[], marker: '##' | '###'): Map<string, string> {
  const sections = new Map<string, string[]>();
  const pattern = new RegExp(`^${marker}\\s+(?<name>.+?)\\s*$`, 'u');
  let current: string[] | undefined;

  for (const line of lines) {
    const heading = pattern.exec(line)?.groups?.name;

    if (heading != undefined) {
      current = [];

      sections.set(heading, current);
    } else if (current) {
      current.push(line);
    }
  }

  return new Map([...sections].map(([heading, body]) => [heading, body.join('\n').trim()]));
}

function parseChecklist(slug: string, body: string): readonly ChecklistItem[] {
  const lines = body.split('\n');
  const names = lines
    .map(line => /^###\s+(?<name>.+?)\s*$/u.exec(line)?.groups?.name)
    .filter(name => name != undefined);
  const duplicate = names.find((name, index) => names.indexOf(name) != index);

  if (duplicate != undefined) {
    throw new TaskFault('duplicate-item', `${slug}: the checklist names "${duplicate}" twice.`);
  }

  const items = [...splitSections(lines, '###')].map(([name, itemBody]) =>
    parseItem(slug, name, itemBody)
  );

  if (items.length < checklistSize.min || items.length > checklistSize.max) {
    throw new TaskFault(
      'checklist-size',
      oneLine`
        ${slug}: the checklist holds ${items.length} items, and ${checklistSize.min} to
        ${checklistSize.max} are allowed.
      `
    );
  }

  return items;
}

function parseItem(slug: string, name: string, body: string): ChecklistItem {
  const fields = new Map<string, string>();

  for (const line of body.split('\n').filter(entry => entry.trim())) {
    const field = /^-\s*(?<label>[^:]+):\s*(?<value>.+)$/u.exec(line.trim())?.groups;

    if (field?.label == undefined || field.value == undefined || !(field.label in itemFields)) {
      throw new TaskFault(
        'item-field',
        `${slug}: "${name}" holds a line that is no item field: "${line.trim()}".`
      );
    }

    fields.set(field.label, field.value.trim());
  }

  const entries = Object.entries(itemFields).map(([label, key]) => {
    const value = fields.get(label);

    if (value == undefined) {
      throw new TaskFault('item-field', `${slug}: "${name}" has no "- ${label}:" line.`);
    }

    return [key, value] as const;
  });

  return { name, ...(Object.fromEntries(entries) as Omit<ChecklistItem, 'name'>) };
}

function parseRoots(slug: string, body: string): readonly SourceRootKind[] {
  const roots: SourceRootKind[] = [];

  for (const line of body.split('\n').filter(entry => entry.trim())) {
    const kind = line.trim().replace(/^-\s*/u, '');

    if (!(kind in rootSources)) {
      throw new TaskFault(
        'unknown-root',
        `${slug}: "${kind}" is no source root. Known: ${Object.keys(rootSources).join(', ')}.`
      );
    }

    if (roots.includes(kind as SourceRootKind)) {
      throw new TaskFault('duplicate-root', `${slug}: the "${kind}" root is listed twice.`);
    }

    roots.push(kind as SourceRootKind);
  }

  if (!roots.includes('decompile')) {
    throw new TaskFault(
      'missing-decompile-root',
      `${slug}: every task brings the decompile root in, whatever else it needs.`
    );
  }

  return roots;
}

/**
 * The request alone, then where its roots are, then the closing lines.
 * Nothing in it names skills, documentation, the web or the judge, so it reads the same to an arm
 * holding the skills as to one without.
 */
function buildArmPrompt(task: TaskText, roots: readonly ResolvedRoot[]): string {
  const clauses = (Object.keys(rootClauses) as SourceRootKind[]).flatMap(kind =>
    roots.filter(root => root.kind == kind).map(root => rootClauses[kind](root.path))
  );

  const listed =
    clauses.length > 1
      ? `${clauses.slice(0, -1).join(', ')} and ${clauses.at(-1)},`
      : (clauses[0] ?? '');

  const where = `${listed} if you need to look anything up.`;

  return [
    task.prompt,
    `${where.charAt(0).toUpperCase()}${where.slice(1)}`,
    closingLines.join('\n')
  ].join('\n\n');
}

/**
 * The isolation contract every invocation runs under; the bench README records why each flag is
 * there and what the probe confirmed of it.
 */
function baseArgs(model: string, effort: string): readonly string[] {
  return [
    '-p',
    '--output-format',
    'stream-json',
    '--verbose',
    '--model',
    model,
    '--effort',
    effort,
    '--setting-sources',
    'project',
    '--strict-mcp-config'
  ];
}

/**
 * The deny rules every workspace carries.
 * A permission rule spells an absolute path with a leading `//`, hence the extra slash.
 */
function workspaceSettings(machine: Machine): WorkspaceSettings {
  return {
    autoMemoryEnabled: false,
    permissions: {
      deny: [
        ...deniedLocations(machine).map(location => `Read(/${location}/**)`),
        'Skill(code-review)'
      ]
    }
  };
}

/**
 * Pairs each `tool_use` block with the user event answering it.
 * A forked skill's own calls arrive as events of their own and are recorded like any other.
 */
function readToolCalls(
  events: ReadonlyArray<Record<string, unknown>>,
  result: Record<string, unknown>
): readonly ToolCall[] {
  const answers = new Map<string, { isError: boolean; detail: unknown }>();

  for (const event of events.filter(candidate => candidate.type == 'user')) {
    for (const block of contentBlocks(event)) {
      if (block.type == 'tool_result' && typeof block.tool_use_id == 'string') {
        answers.set(block.tool_use_id, {
          isError: block.is_error == true,
          detail: event.tool_use_result
        });
      }
    }
  }

  const denied = new Set(
    arrayOf(result.permission_denials)
      .filter(denial => isRecord(denial))
      .map(denial => denial.tool_use_id)
  );

  return events
    .filter(event => event.type == 'assistant')
    .flatMap(event => contentBlocks(event))
    .filter(block => block.type == 'tool_use' && typeof block.name == 'string')
    .map(block => {
      const answer = typeof block.id == 'string' ? answers.get(block.id) : undefined;
      const detail = isRecord(answer?.detail) ? answer.detail : {};

      return {
        name: String(block.name),
        input: isRecord(block.input) ? block.input : {},
        isError: answer?.isError ?? false,
        isDenied: denied.has(block.id),
        resultUrls: searchResultUrls(detail),
        isForked: detail.status == 'forked'
      };
    });
}

/**
 * The URLs a `WebSearch` result lists: `results` holds blocks of `{ title, url }` entries,
 * interleaved with the summary text.
 */
function searchResultUrls(detail: Record<string, unknown>): readonly string[] {
  return arrayOf(detail.results)
    .filter(entry => isRecord(entry))
    .flatMap(entry => arrayOf(entry.content))
    .filter(link => isRecord(link))
    .map(link => link.url)
    .filter(url => typeof url == 'string');
}

function contentBlocks(event: Record<string, unknown>): ReadonlyArray<Record<string, unknown>> {
  const message = isRecord(event.message) ? event.message : {};

  return arrayOf(message.content).filter(block => isRecord(block));
}

/**
 * The paths a file tool read, resolved against the workspace it runs in.
 * A failed read leaked nothing, and a search with no path searched the workspace.
 */
function readPaths(call: ToolCall, workspace: string): readonly string[] {
  if (call.isError) {
    return [];
  }

  return readTargets(call)
    .filter(target => typeof target == 'string')
    .map(target => path.posix.resolve(workspace, target));
}

/**
 * The paths a file-tool call's input names, unchecked.
 */
function readTargets(call: ToolCall): readonly unknown[] {
  const { file_path: filePath, path: searchPath, pattern } = call.input;

  switch (call.name) {
    case 'Read': {
      return [filePath];
    }
    case 'Grep': {
      return [searchPath];
    }
    case 'Glob': {
      // An absolute pattern names its own directory, whatever the search path says.
      return [
        searchPath,
        typeof pattern == 'string' && pattern.startsWith('/') ? pattern : undefined
      ];
    }
    default: {
      return [];
    }
  }
}

/**
 * The absolute paths a shell command names, `~` and `$HOME` expanded.
 */
function shellPaths(call: ToolCall, home: string): readonly string[] {
  const { command } = call.input;

  if (call.name != 'Bash' || typeof command != 'string') {
    return [];
  }

  return [...command.matchAll(shellPathPattern)].map(([token]) =>
    path.posix.normalize(token.replace(/^(?:~|\$\{?HOME\}?)(?=\/)/u, home))
  );
}

/**
 * The files a shell command names, a relative one resolved against the directory a leading `cd`
 * moved to, or else the workspace the shell starts in.
 */
function shellFiles(call: ToolCall, workspace: string): readonly string[] {
  const { command } = call.input;

  if (call.name != 'Bash' || typeof command != 'string') {
    return [];
  }

  const cwd = /^\s*cd\s+(?<dir>\/[^\s;&|]+)/u.exec(command)?.groups?.dir ?? workspace;

  return (command.match(/[^\s'"`;|&<>()]+\.md\b/gu) ?? []).map(token =>
    path.posix.resolve(cwd, token)
  );
}

/**
 * The URLs a call fetched, named in shell text, or got back from a search.
 */
function reachedUrls(call: ToolCall): readonly string[] {
  const { url, command } = call.input;

  const named =
    call.name == 'WebFetch' && typeof url == 'string' && !call.isError
      ? [url]
      : call.name == 'Bash' && typeof command == 'string'
        ? (command.match(/https?:\/\/\S+/gu) ?? [])
        : [];

  return [...named, ...call.resultUrls];
}

/**
 * The CLI saves a large tool output under `~/.claude/projects/<project>/<session>/tool-results/`
 * and has the arm read it back: that read is the arm reading its own output.
 */
function isSavedToolOutput(target: string, home: string): boolean {
  const [project, session, folder, file] = path.posix
    .relative(path.join(home, '.claude/projects'), target)
    .split('/');

  return project != '..' && session != undefined && folder == 'tool-results' && file != undefined;
}

/**
 * What both arms are denied reading:
 * this repository, the CLI's configuration, and the setup record.
 */
function deniedLocations(machine: Machine): readonly string[] {
  return [
    machine.repoRoot,
    path.join(machine.home, '.claude'),
    path.join(machine.home, '.cs2-modding')
  ];
}

function verdictsOf(job: JobRecord): ReadonlyArray<ItemVerdict & { readonly item: string }> {
  return job.outcome == 'judged' ? job.verdicts : [];
}

function durationOf({ spend, wallMs }: Attempt): [number, DurationSource] {
  if (spend?.apiDurationMs != undefined) {
    return [spend.apiDurationMs, 'api'];
  }

  if (spend?.cliDurationMs != undefined) {
    return [spend.cliDurationMs, 'cli'];
  }

  return [wallMs, 'wall'];
}

function isInside(target: string, root: string): boolean {
  return target == root || target.startsWith(`${root}/`);
}

function readVerdict(word: string): Verdict {
  const normalized = word.toUpperCase();

  return normalized == 'PASS' ? 'pass' : normalized == 'FAIL' ? 'fail' : 'n/a';
}

function rateOf(tally: Tally | undefined): string {
  if (!tally?.applicable) {
    return '–';
  }

  const percent = Math.round((tally.passed / tally.applicable) * 100);

  return `${percent}% (${tally.passed}/${tally.applicable})`;
}

/**
 * A table row: the leading cells, then one cell per arm rendered from that arm's entry.
 */
function armRow<T extends { readonly arm: ArmName }>(
  leading: readonly string[],
  entries: readonly T[],
  render: (entry: T | undefined) => string
): string[] {
  return [...leading, ...armNames.map(arm => render(entries.find(entry => entry.arm == arm)))];
}

function tallyOf(jobs: readonly JobRecord[]): Tally {
  const verdicts = jobs.flatMap(job => verdictsOf(job));

  return {
    passed: verdicts.filter(entry => entry.verdict == 'pass').length,
    applicable: verdicts.filter(entry => entry.verdict != 'n/a').length
  };
}

function statsOf(readings: readonly number[]): MetricStats | undefined {
  const values = readings.toSorted((a, b) => a - b);
  const middle = Math.floor(values.length / 2);
  const upper = values[middle];

  if (upper == undefined) {
    return undefined;
  }

  const lower = values[middle - 1] ?? upper;

  return {
    mean: sum(values) / values.length,
    median: values.length % 2 ? upper : (lower + upper) / 2
  };
}

/**
 * A titled table, or nothing where it would have no rows.
 */
function section(
  title: string,
  columns: readonly string[],
  rows: ReadonlyArray<readonly string[]>
): string[] {
  return rows.length ? ['', `## ${title}`, '', ...table(columns, rows)] : [];
}

function table(columns: readonly string[], rows: ReadonlyArray<readonly string[]>): string[] {
  const cell = (text: string): string => text.replaceAll('|', String.raw`\|`).replaceAll('\n', ' ');

  return [
    `| ${columns.join(' | ')} |`,
    `| ${columns.map(() => '---').join(' | ')} |`,
    ...rows.map(row => `| ${row.map(text => cell(text)).join(' | ')} |`)
  ];
}

/**
 * A JSON value as an array, or an empty one where it is anything else.
 */
function arrayOf(value: unknown): readonly unknown[] {
  return Array.isArray(value) ? (value as unknown[]) : [];
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value == 'object' && value != null && !Array.isArray(value);
}

function readNumber(source: Record<string, unknown>, key: string): number | undefined {
  const value = source[key];

  return typeof value == 'number' ? value : undefined;
}

function sum(values: readonly number[]): number {
  return values.reduce((total, value) => total + value, 0);
}
