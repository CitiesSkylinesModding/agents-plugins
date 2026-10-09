/* oxlint-disable no-console -- the driver reports its progress to whoever launched it. */
/* oxlint-disable node/no-sync -- single-shot script; synchronous IO keeps the shell readable. */

import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { oneLine } from 'common-tags';
import * as core from './core';

// The thin shell around core.ts: workspaces, spawning, files and the clock. Every decision it
// looks like it makes belongs to the core, so this file stays untested by design. The namespace
// import is what keeps that visible: every `core.` in here is a decision made elsewhere.

/**
 * The first attempt plus two more, shared by infrastructure failures and runs that wrote no code.
 */
const maxAttempts = 3;

const msPerMinute = 60_000;

/**
 * The exit code of a process a signal ended, less the signal's number.
 */
const signalExitBase = 128;

const repoRoot = path.resolve(import.meta.dirname, '..');
const fixturesDir = path.join(import.meta.dirname, 'tests/fixtures');
const resultsFile = path.join(import.meta.dirname, 'results.md');
const setupRecordPath = path.join(os.homedir(), '.cs2-modding/setup.md');
const skillsDir = path.join(repoRoot, core.skillsSourceDir);

const manifestFile = 'run.json';
const recordFile = 'record.json';

// The setup record is read here for the deny rules alone; the roots read it again in main, where
// its absence is the error. A promote needs neither, so a missing record is no error here.
const setupRecord = fs.existsSync(setupRecordPath)
  ? core.parseSetupRecord(fs.readFileSync(setupRecordPath, 'utf8'))
  : undefined;

const machine: core.Machine = {
  repoRoot,
  home: os.homedir(),
  tmpDir: os.tmpdir(),
  modCorpus: setupRecord && core.modCorpusRoot(setupRecord),
  uiBundleCopy: setupRecord && core.uiBundleCopyDir(setupRecord)
};

/**
 * The CLI sessions in flight, killed with the driver so an interrupted run stops spending.
 */
const children = new Set<Bun.Subprocess>();

/**
 * The flags that shape what a run measures. A run directory fixes them when it is created, so a
 * resume refuses them rather than mixing two settings in one run.
 */
const runShapeFlags = ['smoke', 'tasks', 'runs', 'model', 'judge-model', 'effort'] as const;

interface Options {
  readonly mode: 'run' | 'validate' | 'calibrate' | 'promote';
  readonly tasksDir: string;
  readonly resultsDir: string;
  readonly runs: number;
  readonly concurrency: number;
  readonly model: string;
  readonly judgeModel: string;
  readonly effort: string;
  readonly timeoutMs: number;
  readonly taskBudget: number | undefined;
  /**
   * The run directory to resume or promote.
   */
  readonly runDir: string | undefined;
}

/**
 * What a run directory's `run.json` holds: its settings, and where its tasks are loaded from.
 */
interface Manifest extends core.RunSettings {
  readonly tasksDir: string;
  /**
   * Each task folder's digest, by slug.
   */
  readonly taskDigests: Readonly<Record<string, string>>;
}

interface Job extends core.JobKey {
  readonly spec: core.Task;
  readonly roots: readonly core.ResolvedRoot[];
}

/**
 * What every job of an invocation shares.
 */
interface Context {
  readonly settings: core.RunSettings;
  readonly outputDir: string;
  readonly timeoutMs: number;
}

/**
 * The last arm attempt that got as far as a stream, kept for what the job record reads off it.
 */
interface LastRun {
  readonly run: core.StreamedRun;
  readonly workspace: string;
  readonly files: ReturnType<typeof core.collectedFiles>;
}

type JudgeOutcome =
  | {
      readonly status: 'judged';
      readonly verdicts: readonly core.ItemRuling[];
    }
  | { readonly status: 'failed'; readonly fault: string };

for (const signal of ['SIGINT', 'SIGTERM'] as const) {
  process.on(signal, () => {
    for (const child of children) {
      child.kill();
    }

    // Exiting from the write's callback lets stdout drain first, so a piped notice is never cut.
    process.stdout.write(
      '\nInterrupted. Every job with a record is kept; resume with --resume.\n',
      () => process.exit(signalExitBase + os.constants.signals[signal])
    );
  });
}

await main();

async function main(): Promise<void> {
  const options = readOptions();

  if (options.mode == 'promote') {
    promote(options);

    return;
  }

  const resumed = options.runDir
    ? { outputDir: options.runDir, manifest: readManifest(options.runDir) }
    : undefined;

  const tasksDir = resumed?.manifest.tasksDir ?? options.tasksDir;

  // A resumed run loads exactly the tasks it was created over, and refuses one edited since: its
  // jobs would be run and judged against two versions of the task and pooled as one. The fixtures
  // are tasks like any other, so validation always covers them.
  const tasks = resumed
    ? resumed.manifest.tasks.map(slug => {
        if (taskDigest(tasksDir, slug) != resumed.manifest.taskDigests[slug]) {
          throw new Error(`The ${slug} task folder changed since the run began.`);
        }

        return core.loadTask(path.join(tasksDir, slug), skillsDir);
      })
    : [
        ...(fs.existsSync(tasksDir) ? core.loadTasks(tasksDir, skillsDir) : []),
        ...(options.mode == 'validate' && tasksDir != fixturesDir
          ? core.loadTasks(fixturesDir, skillsDir)
          : [])
      ];

  if (!tasks.length) {
    throw new Error(`No task folders under ${tasksDir}.`);
  }

  // Resolving every root up front is what keeps an unprovisioned machine from spending money on
  // half a comparison: an unresolvable root throws here, before the first invocation.
  const record = core.parseSetupRecord(fs.readFileSync(setupRecordPath, 'utf8'));

  const roots = new Map(
    tasks.map(task => {
      const resolved = core.resolveRoots(task.roots, record, process.env);

      // The core resolves a root from what the record says; only the filesystem knows whether the
      // path still exists. Without this, a moved decompile passes --validate-only and fails once
      // every run is already spending.
      for (const root of resolved) {
        if (!fs.existsSync(root.path)) {
          throw new Error(`The ${root.kind} root does not exist: ${root.path}`);
        }

        // Both arms are denied the copy's whole directory, so one holding a root blinds them.
        const copyDir = machine.uiBundleCopy;

        if (copyDir && core.isInside(root.path, copyDir)) {
          throw new Error(oneLine`
            The setup record's UI bundle copy sits in ${copyDir}, which holds the ${root.kind}
            root, and both arms are denied that directory. Move the copy into a directory of its
            own and update the record.
          `);
        }
      }

      return [task.slug, resolved] as const;
    })
  );

  // Everything above this line is what a scored invocation does before it can spend anything, so
  // running only that much is the whole of a task set's validation.
  if (options.mode == 'validate') {
    for (const task of tasks) {
      console.log(oneLine`
        ${task.slug}: ${task.kind}, ${task.checklist.length} items, roots
        ${task.roots.join(', ')} resolved, ${task.folders.seed ? 'seeded' : 'no seed'}.
      `);
    }

    console.log(`${tasks.length} task(s) valid.`);

    return;
  }

  if (options.mode == 'calibrate') {
    await calibrate(tasks, options);

    return;
  }

  const run = resumed ?? createRun(options, tasks, tasksDir);

  const { tasksDir: _, taskDigests: __, ...settings } = run.manifest;
  const context: Context = { settings, outputDir: run.outputDir, timeoutMs: options.timeoutMs };

  const planned = core.planJobs({
    tasks: settings.tasks,
    runs: settings.runs,
    records: readRecords(run.outputDir),
    budget: options.taskBudget
  });

  const jobs = planned.map((key): Job => {
    const spec = tasks.find(task => task.slug == key.task);
    const taskRoots = roots.get(key.task);

    assert.ok(spec && taskRoots, 'a planned job names a task of the run');

    return { task: key.task, arm: key.arm, run: key.run, spec, roots: taskRoots };
  });

  console.log(oneLine`
    ${jobs.length} job(s) to run, over ${new Set(jobs.map(job => job.task)).size} of the run's
    ${settings.tasks.length} task(s): ${settings.model} at ${settings.effort} effort against
    ${settings.judgeModel} as judge, ${options.concurrency} at a time.
    Results: ${run.outputDir}
  `);

  for (const job of jobs) {
    console.log(`  planned: ${job.task} ${job.arm} #${job.run}`);
  }

  await runPool(jobs, options.concurrency, async job => {
    await runJob(job, context);

    // Rebuilt after every job, so an interrupted run's summary covers all it finished.
    writeSummary(context);
  });

  const { summary, report } = writeSummary(context);

  console.log(`\n${report}`);

  if (summary.jobs.some(job => job.outcome == 'failed')) {
    process.exitCode = 1;
  }
}

/**
 * Creates a run directory and fixes its settings in its manifest.
 */
function createRun(
  options: Options,
  tasks: readonly core.Task[],
  tasksDir: string
): { readonly outputDir: string; readonly manifest: Manifest } {
  const startedAt = new Date().toISOString();

  const outputDir = path.join(options.resultsDir, stampOf(startedAt));

  const manifest: Manifest = {
    startedAt,
    model: options.model,
    effort: options.effort,
    judgeModel: options.judgeModel,
    runs: options.runs,
    tasks: tasks.map(task => task.slug),
    tasksDir,
    taskDigests: Object.fromEntries(tasks.map(task => [task.slug, taskDigest(tasksDir, task.slug)]))
  };

  fs.mkdirSync(path.join(outputDir, 'raw'), { recursive: true });
  writeJson(path.join(outputDir, manifestFile), manifest);

  return { outputDir, manifest };
}

/**
 * One job, rerun whole: arm attempts until one writes code or the cap is reached, then the judge.
 */
async function runJob(job: Job, context: Context): Promise<void> {
  const label = `${job.task} ${job.arm} #${job.run}`;
  const jobDir = path.join(context.outputDir, 'raw', `${job.task}-${job.arm}-${job.run}`);
  const skills = skillsVersion();
  const attempts: core.Attempt[] = [];
  let lastRun: LastRun | undefined;
  let next: core.NextStep = { action: 'retry' };

  // What an earlier invocation left here is a failed record or an interrupted job's attempts.
  fs.rmSync(jobDir, { recursive: true, force: true });
  fs.mkdirSync(jobDir, { recursive: true });

  while (next.action == 'retry') {
    const attempt = await runArmAttempt(job, context, jobDir, attempts.length + 1);

    attempts.push(attempt.attempt);
    lastRun = attempt.lastRun ?? lastRun;
    next = core.planNextAttempt(attempts, maxAttempts);

    console.log(`  ${label}: attempt ${attempts.length}, ${attempt.attempt.outcome}`);
  }

  const arm = lastRun
    ? {
        contamination: core.findContamination({
          toolCalls: lastRun.run.toolCalls,
          workspace: lastRun.workspace,
          roots: job.roots,
          machine
        }),
        skillUse: core.findSkillUse({
          toolCalls: lastRun.run.toolCalls,
          workspace: lastRun.workspace
        }),
        files: lastRun.files.changed,
        finalMessage: lastRun.run.finalMessage,
        toolCalls: lastRun.run.toolCalls
      }
    : {
        contamination: [],
        skillUse: { skillsLoaded: [], referencesRead: [] },
        files: [],
        finalMessage: '',
        toolCalls: []
      };

  const base: core.ArmRecord = {
    task: job.task,
    kind: job.spec.kind,
    arm: job.arm,
    run: job.run,
    skills,
    attempts,
    spend: core.jobSpend(attempts),
    ...arm
  };

  const last = attempts.at(-1);

  const record: core.JobRecord =
    next.action == 'judge'
      ? await judgeJob({ job, base, files: judgedFiles(lastRun), context, jobDir })
      : last?.outcome == 'failed'
        ? { ...base, outcome: 'failed', fault: last.fault }
        : { ...base, outcome: 'no-code' };

  // Renamed into place, so an interruption never leaves a half-written record to read as a result.
  writeJson(path.join(jobDir, `${recordFile}.tmp`), record);
  fs.renameSync(path.join(jobDir, `${recordFile}.tmp`), path.join(jobDir, recordFile));

  const mark = record.contamination.length ? ', contaminated' : '';

  console.log(`  ${label}: ${record.outcome}${mark}`);
}

/**
 * One arm attempt in a fresh workspace, seeded and shaped by the arm's plan.
 */
async function runArmAttempt(
  job: Job,
  context: Context,
  jobDir: string,
  number: number
): Promise<{ readonly attempt: core.Attempt; readonly lastRun: LastRun | undefined }> {
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), core.workspacePrefix));

  const plan = core.planArm({
    arm: job.arm,
    task: job.spec,
    roots: job.roots,
    workspace,
    machine,
    model: context.settings.model,
    effort: context.settings.effort
  });

  writeSettings(workspace, plan.settings);

  for (const skill of plan.skills) {
    fs.cpSync(path.join(skillsDir, skill), path.join(workspace, '.claude/skills', skill), {
      recursive: true
    });
  }

  if (job.spec.folders.seed) {
    fs.cpSync(job.spec.folders.seed, workspace, { recursive: true });
  }

  fs.writeFileSync(path.join(jobDir, 'prompt.txt'), plan.prompt);

  const before = snapshot(workspace);
  const startedAt = Date.now();

  const invocation = await spawnClaude({
    cwd: workspace,
    prompt: plan.prompt,
    args: plan.args,
    timeoutMs: context.timeoutMs
  });

  const wallMs = Date.now() - startedAt;

  fs.writeFileSync(path.join(jobDir, `attempt-${number}.jsonl`), invocation.stdout);
  fs.writeFileSync(path.join(jobDir, `attempt-${number}.err`), invocation.stderr);

  const outcome = core.readStream(invocation);

  if (outcome.status == 'retryable') {
    return {
      attempt: {
        outcome: 'failed',
        fault: `${outcome.fault} (workspace kept at ${workspace})`,
        spend: outcome.spend,
        wallMs
      },
      lastRun: undefined
    };
  }

  const files = core.collectedFiles(before, snapshot(workspace));
  const filesDir = path.join(jobDir, 'files');

  fs.rmSync(filesDir, { recursive: true, force: true });

  for (const file of files.judged) {
    fs.mkdirSync(path.dirname(path.join(filesDir, file)), { recursive: true });
    fs.copyFileSync(path.join(workspace, file), path.join(filesDir, file));
  }

  // The files are copied out, so the workspace is spent; only a failed attempt's is kept, for
  // inspection, its path in the fault.
  fs.rmSync(workspace, { recursive: true, force: true });

  return {
    attempt: {
      outcome: files.changed.length ? 'code' : 'no-code',
      spend: outcome.run.spend,
      wallMs
    },
    lastRun: { run: outcome.run, workspace, files }
  };
}

/**
 * Has the judge rule on the files a job collected.
 */
async function judgeJob(input: {
  readonly job: Job;
  readonly base: core.ArmRecord;
  readonly files: readonly string[];
  readonly context: Context;
  readonly jobDir: string;
}): Promise<core.JobRecord> {
  const { job, base, files, context, jobDir } = input;

  const outcome = await judge({
    task: job.spec,
    files: readFiles(path.join(jobDir, 'files'), files),
    finalMessage: base.finalMessage,
    model: context.settings.judgeModel,
    effort: context.settings.effort,
    timeoutMs: context.timeoutMs,
    logDir: jobDir
  });

  return outcome.status == 'judged'
    ? { ...base, outcome: 'judged', verdicts: outcome.verdicts }
    : { ...base, outcome: 'failed', fault: `the judge: ${outcome.fault}` };
}

/**
 * Has the judge rule on a set of files, retrying an unreadable reply under the cap, and keeps its
 * prompt and replies in the log directory.
 */
async function judge(input: {
  readonly task: core.Task;
  readonly files: readonly core.CollectedFile[];
  readonly finalMessage: string;
  readonly model: string;
  readonly effort: string;
  readonly timeoutMs: number;
  readonly logDir: string;
}): Promise<JudgeOutcome> {
  const { task, logDir } = input;

  const prompt = core.buildJudgePrompt({
    task,
    files: input.files,
    finalMessage: input.finalMessage
  });

  const plan = core.planJudge({ machine, model: input.model, effort: input.effort });
  let fault = '';

  fs.writeFileSync(path.join(logDir, 'judge-prompt.txt'), prompt);

  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    const cwd = fs.mkdtempSync(path.join(os.tmpdir(), core.workspacePrefix));

    writeSettings(cwd, plan.settings);

    const invocation = await spawnClaude({
      cwd,
      prompt,
      args: plan.args,
      timeoutMs: input.timeoutMs
    });

    fs.rmSync(cwd, { recursive: true, force: true });
    fs.writeFileSync(path.join(logDir, `judge-${attempt}.jsonl`), invocation.stdout);
    fs.writeFileSync(path.join(logDir, `judge-${attempt}.err`), invocation.stderr);

    const outcome = core.readStream(invocation);

    if (outcome.status == 'retryable') {
      ({ fault } = outcome);
      continue;
    }

    try {
      return {
        status: 'judged',
        verdicts: core.parseJudgeReply(outcome.run.finalMessage, task.checklist)
      };
    } catch (error) {
      fault = String(error);
    }
  }

  return { status: 'failed', fault };
}

/**
 * Judges each task's two calibration samples and reports which tasks the judge can rule on.
 */
async function calibrate(tasks: readonly core.Task[], options: Options): Promise<void> {
  const logRoot = path.join(options.resultsDir, `calibration-${stampOf(new Date().toISOString())}`);

  console.log(oneLine`
    Calibrating ${tasks.length} task(s) against ${options.judgeModel} at ${options.effort} effort.
    Judge logs: ${logRoot}
  `);

  const judgeSample = (task: core.Task, sample: 'falls' | 'avoids'): Promise<JudgeOutcome> => {
    const logDir = path.join(logRoot, `${task.slug}-${sample}`);
    const sampleDir = task.folders[sample];

    fs.mkdirSync(logDir, { recursive: true });

    return judge({
      task,
      files: readFiles(sampleDir, core.collectedFiles(new Map(), snapshot(sampleDir)).judged),
      finalMessage: '',
      model: options.judgeModel,
      effort: options.effort,
      timeoutMs: options.timeoutMs,
      logDir
    });
  };

  const judged = await runPool(tasks, options.concurrency, async task => ({
    task,
    falls: await judgeSample(task, 'falls'),
    avoids: await judgeSample(task, 'avoids')
  }));

  for (const { task, falls, avoids } of judged) {
    if (falls.status == 'failed' || avoids.status == 'failed') {
      const faults = [falls, avoids].flatMap(entry =>
        entry.status == 'failed' ? entry.fault : []
      );

      console.log(`\n${task.slug}: not calibrated, the judge failed: ${faults.join('; ')}`);
      process.exitCode = 1;
      continue;
    }

    const verdict = core.calibrationVerdict({ falls: falls.verdicts, avoids: avoids.verdicts });

    console.log(`\n${task.slug}: ${verdict.passes ? 'calibrated' : 'FAILS calibration'}`);

    for (const [sample, rulings] of [
      ['falls', falls.verdicts],
      ['avoids', avoids.verdicts]
    ] as const) {
      for (const ruling of rulings) {
        console.log(`  ${sample} ${ruling.verdict}  ${ruling.item} | ${ruling.evidence}`);
      }
    }

    for (const miss of verdict.misses) {
      console.log(`  miss: ${miss}`);
    }

    if (!verdict.passes) {
      process.exitCode = 1;
    }
  }
}

/**
 * Writes a finished run into the tracked results file, replacing the previous promotion.
 */
function promote(options: Options): void {
  assert.ok(options.runDir, 'promotion names a run directory');

  const { tasksDir: _, taskDigests: __, ...settings } = readManifest(options.runDir);
  const summary = core.buildSummary({ settings, jobs: readRecords(options.runDir) });

  fs.writeFileSync(resultsFile, core.renderResults(summary));

  console.log(`Promoted ${options.runDir} into ${path.relative(repoRoot, resultsFile)}.`);
}

/**
 * Rebuilds the run's summary from the records on disk.
 */
function writeSummary(context: Context): {
  readonly summary: core.BenchSummary;
  readonly report: string;
} {
  const summary = core.buildSummary({
    settings: context.settings,
    jobs: readRecords(context.outputDir)
  });

  const report = core.renderReport(summary);

  writeJson(path.join(context.outputDir, 'summary.json'), summary);
  fs.writeFileSync(path.join(context.outputDir, 'summary.md'), report);

  return { summary, report };
}

function readManifest(runDir: string): Manifest {
  const file = path.join(runDir, manifestFile);

  if (!fs.existsSync(file)) {
    throw new Error(`${runDir} holds no ${manifestFile}, so it is no run directory.`);
  }

  // The driver's own output, read back unvalidated.
  return JSON.parse(fs.readFileSync(file, 'utf8')) as Manifest;
}

/**
 * Every job record in a run directory, in job-directory order.
 */
function readRecords(runDir: string): readonly core.JobRecord[] {
  const rawDir = path.join(runDir, 'raw');

  return fs
    .readdirSync(rawDir)
    .toSorted()
    .map(name => path.join(rawDir, name, recordFile))
    .filter(file => fs.existsSync(file))
    .map(file => JSON.parse(fs.readFileSync(file, 'utf8')) as core.JobRecord);
}

/**
 * An ISO timestamp as a directory name, to the second.
 */
function stampOf(isoDate: string): string {
  return isoDate.replaceAll(':', '-').slice(0, 'YYYY-MM-DDTHH-MM-SS'.length);
}

function taskDigest(tasksDir: string, slug: string): string {
  const files = [...snapshot(path.join(tasksDir, slug))].toSorted(([left], [right]) =>
    left.localeCompare(right)
  );

  return crypto.createHash('sha1').update(JSON.stringify(files)).digest('hex');
}

/**
 * The commit that last touched the shipped skills, and whether the working tree changes them.
 */
function skillsVersion(): core.SkillsVersion {
  return {
    commit: git(['log', '-1', '--format=%h', '--', core.skillsSourceDir]),
    isDirty: git(['status', '--porcelain', '--', core.skillsSourceDir]) != ''
  };
}

function git(args: readonly string[]): string {
  const result = Bun.spawnSync(['git', ...args], { cwd: repoRoot });

  if (!result.success) {
    throw new Error(`git ${args.join(' ')} failed: ${result.stderr.toString().trim()}`);
  }

  return result.stdout.toString().trim();
}

/**
 * The files a job collected, off the attempt that wrote code: a judge step always follows one.
 */
function judgedFiles(lastRun: LastRun | undefined): readonly string[] {
  assert.ok(lastRun, 'a judge step follows an attempt that wrote code');

  return lastRun.files.judged;
}

function readFiles(directory: string, files: readonly string[]): readonly core.CollectedFile[] {
  return files.map(file => ({
    path: file,
    content: fs.readFileSync(path.join(directory, file), 'utf8')
  }));
}

function writeSettings(directory: string, settings: core.WorkspaceSettings): void {
  fs.mkdirSync(path.join(directory, '.claude'), { recursive: true });
  writeJson(path.join(directory, '.claude/settings.json'), settings);
}

function writeJson(file: string, value: unknown): void {
  fs.writeFileSync(file, `${JSON.stringify(value, undefined, 2)}\n`);
}

/**
 * Every file under a directory, by path relative to it, mapped to a hash of its content.
 */
function snapshot(directory: string): ReadonlyMap<string, string> {
  return new Map(
    fs
      .readdirSync(directory, { recursive: true, withFileTypes: true })
      .filter(entry => entry.isFile())
      .map(entry => {
        const file = path.join(entry.parentPath, entry.name);
        const hash = crypto.createHash('sha1').update(fs.readFileSync(file)).digest('hex');

        return [path.relative(directory, file), hash];
      })
  );
}

async function spawnClaude(input: {
  readonly cwd: string;
  readonly prompt: string;
  readonly args: readonly string[];
  readonly timeoutMs: number;
}): Promise<core.RawInvocation> {
  const child = Bun.spawn(['claude', ...input.args], {
    cwd: input.cwd,
    stdin: new TextEncoder().encode(input.prompt),
    stdout: 'pipe',
    stderr: 'pipe'
  });

  let timedOut = false;
  const timer = setTimeout(() => {
    timedOut = true;

    child.kill();
  }, input.timeoutMs);

  children.add(child);

  try {
    const [stdout, stderr] = await Promise.all([
      new Response(child.stdout).text(),
      new Response(child.stderr).text()
    ]);

    await child.exited;

    return { exitCode: child.exitCode, timedOut, stdout, stderr };
  } finally {
    clearTimeout(timer);
    children.delete(child);
  }
}

async function runPool<TJob, TResult>(
  jobs: readonly TJob[],
  concurrency: number,
  run: (job: TJob) => Promise<TResult>
): Promise<readonly TResult[]> {
  const results: TResult[] = Array.from({ length: jobs.length });
  let next = 0;

  const workers = Array.from({ length: Math.min(concurrency, jobs.length) }, async () => {
    while (next < jobs.length) {
      const index = next++;
      const job = jobs[index];

      assert.ok(job != undefined, 'the pool only hands out indices it holds');

      results[index] = await run(job);
    }
  });

  // All-or-nothing on purpose: a worker only rejects on a bug in this file, and finishing the pool
  // around one would write a summary that silently misses runs.
  await Promise.all(workers);

  return results;
}

function readOptions(): Options {
  // The run-shape flags carry no parser default, so a resume can tell one given from one omitted.
  const { values } = parseArgs({
    options: {
      'smoke': { type: 'boolean' },
      'tasks': { type: 'string' },
      'results': { type: 'string', default: 'bench/results' },
      'runs': { type: 'string' },
      'concurrency': { type: 'string', default: '2' },
      'model': { type: 'string' },
      'judge-model': { type: 'string' },
      'effort': { type: 'string' },
      'timeout-minutes': { type: 'string', default: '20' },
      'task-budget': { type: 'string' },
      'resume': { type: 'string' },
      'validate-only': { type: 'boolean', default: false },
      'calibrate': { type: 'boolean', default: false },
      'promote': { type: 'string' }
    }
  });

  const modes = (
    [
      ['validate', values['validate-only']],
      ['calibrate', values.calibrate],
      ['promote', values.promote != undefined],
      ['resume', values.resume != undefined]
    ] as const
  ).flatMap(([mode, isGiven]) => (isGiven ? [mode] : []));

  const [mode = 'run'] = modes;

  if (modes.length > 1) {
    throw new Error(`--${modes.join(' and --')} are separate commands; pass one.`);
  }

  if (values.resume != undefined) {
    const given = runShapeFlags.filter(flag => values[flag] != undefined);

    if (given.length) {
      throw new Error(oneLine`
        A resumed run keeps the settings it was created with, so --${given.join(', --')} cannot
        change them.
      `);
    }
  }

  const runDir = values.resume ?? values.promote;

  return {
    mode: mode == 'resume' ? 'run' : mode,
    tasksDir: values.smoke ? fixturesDir : path.resolve(repoRoot, values.tasks ?? 'bench/tasks'),
    resultsDir: path.resolve(repoRoot, values.results),
    runs: core.parseCount('runs', values.runs ?? '4'),
    concurrency: core.parseCount('concurrency', values.concurrency),
    model: values.model ?? 'opus',
    judgeModel: values['judge-model'] ?? 'opus',
    effort: values.effort ?? 'medium',
    timeoutMs: core.parseCount('timeout-minutes', values['timeout-minutes']) * msPerMinute,
    taskBudget:
      values['task-budget'] == undefined
        ? undefined
        : core.parseCount('task-budget', values['task-budget']),
    runDir: runDir == undefined ? undefined : path.resolve(repoRoot, runDir)
  };
}
