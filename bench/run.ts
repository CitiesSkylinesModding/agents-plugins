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

/**
 * Enough of an ISO timestamp to name a results directory to the second.
 */
const secondsPrecision = 'YYYY-MM-DDTHH-MM-SS'.length;

const msPerMinute = 60_000;

const repoRoot = path.resolve(import.meta.dirname, '..');
const smokeTaskDir = path.join(import.meta.dirname, 'tests/fixtures/daily-blackout-log');
const setupRecordPath = path.join(os.homedir(), '.cs2-modding/setup.md');
const skillsDir = path.join(repoRoot, core.skillsSourceDir);

const machine: core.Machine = { repoRoot, home: os.homedir(), tmpDir: os.tmpdir() };

interface Options {
  readonly tasksDir: string;
  readonly resultsDir: string;
  readonly runs: number;
  readonly concurrency: number;
  readonly model: string;
  readonly judgeModel: string;
  readonly effort: string;
  readonly timeoutMs: number;
  readonly isSmoke: boolean;
  readonly isValidateOnly: boolean;
}

interface Job {
  readonly task: core.Task;
  readonly arm: core.ArmName;
  readonly run: number;
  readonly roots: readonly core.ResolvedRoot[];
}

/**
 * The last arm attempt that got as far as a stream, kept for what the job record reads off it.
 */
interface LastRun {
  readonly run: core.StreamedRun;
  readonly workspace: string;
  readonly files: ReturnType<typeof core.collectedFiles>;
}

await main();

async function main(): Promise<void> {
  const options = readOptions();

  // The fixture is a task like any other, so validation always covers it.
  const tasks = options.isSmoke
    ? [core.loadTask(smokeTaskDir, skillsDir)]
    : [
        ...(fs.existsSync(options.tasksDir) ? core.loadTasks(options.tasksDir, skillsDir) : []),
        ...(options.isValidateOnly ? [core.loadTask(smokeTaskDir, skillsDir)] : [])
      ];

  if (!tasks.length) {
    throw new Error(`No task folders under ${options.tasksDir}.`);
  }

  // Resolving every root up front is what keeps an unprovisioned machine from spending money on
  // half a comparison: an unresolvable root throws here, before the first invocation.
  const record = core.parseSetupRecord(fs.readFileSync(setupRecordPath, 'utf8'));

  const jobs = tasks.flatMap(task => {
    const roots = core.resolveRoots(task.roots, record, process.env);

    // The core resolves a root from what the record says; only the filesystem knows whether the
    // path still exists. Without this, a moved decompile passes --validate-only and fails once
    // every run is already spending.
    for (const root of roots) {
      if (!fs.existsSync(root.path)) {
        throw new Error(`The ${root.kind} root does not exist: ${root.path}`);
      }
    }

    return core.armNames.flatMap(arm =>
      Array.from({ length: options.runs }, (_, index) => ({ task, arm, run: index + 1, roots }))
    );
  });

  // Everything above this line is what a scored invocation does before it can spend anything, so
  // running only that much is the whole of a task set's validation.
  if (options.isValidateOnly) {
    for (const task of tasks) {
      console.log(oneLine`
        ${task.slug}: ${task.kind}, ${task.checklist.length} items, roots
        ${task.roots.join(', ')} resolved, ${task.folders.seed ? 'seeded' : 'no seed'}.
      `);
    }

    console.log(`${tasks.length} task(s) valid.`);

    return;
  }

  const startedAt = new Date().toISOString();

  const outputDir = path.join(
    options.resultsDir,
    startedAt.replaceAll(':', '-').slice(0, secondsPrecision)
  );

  fs.mkdirSync(path.join(outputDir, 'raw'), { recursive: true });

  console.log(oneLine`
    ${jobs.length} runs over ${tasks.length} task(s), ${options.model} at ${options.effort}
    effort against ${options.judgeModel} as judge, ${options.concurrency} at a time.
    Results: ${outputDir}
  `);

  const records = await runPool(jobs, options.concurrency, job => runJob(job, options, outputDir));

  const summary = core.buildSummary({
    startedAt,
    model: options.model,
    effort: options.effort,
    judgeModel: options.judgeModel,
    jobs: records
  });

  const report = core.renderReport(summary);

  fs.writeFileSync(
    path.join(outputDir, 'summary.json'),
    `${JSON.stringify(summary, undefined, 2)}\n`
  );
  fs.writeFileSync(path.join(outputDir, 'summary.md'), report);

  console.log(`\n${report}`);

  if (records.some(job => job.outcome == 'failed')) {
    process.exitCode = 1;
  }
}

/**
 * One job: arm attempts until one writes code or the cap is reached, then the judge.
 */
async function runJob(job: Job, options: Options, outputDir: string): Promise<core.JobRecord> {
  const label = `${job.task.slug} ${job.arm} #${job.run}`;
  const jobDir = path.join(outputDir, 'raw', `${job.task.slug}-${job.arm}-${job.run}`);
  const attempts: core.Attempt[] = [];
  let lastRun: LastRun | undefined;
  let next: core.NextStep = { action: 'retry' };

  fs.mkdirSync(jobDir, { recursive: true });

  while (next.action == 'retry') {
    const attempt = await runArmAttempt(job, options, jobDir, attempts.length + 1);

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
    task: job.task.slug,
    kind: job.task.kind,
    arm: job.arm,
    run: job.run,
    attempts,
    spend: core.jobSpend(attempts),
    ...arm
  };

  const last = attempts.at(-1);

  const record: core.JobRecord =
    next.action == 'judge'
      ? await judge({ job, base, files: judgedFiles(lastRun), options, jobDir })
      : last?.outcome == 'failed'
        ? { ...base, outcome: 'failed', fault: last.fault }
        : { ...base, outcome: 'no-code' };

  fs.writeFileSync(path.join(jobDir, 'record.json'), `${JSON.stringify(record, undefined, 2)}\n`);

  const mark = record.contamination.length ? ', contaminated' : '';

  console.log(`  ${label}: ${record.outcome}${mark}`);

  return record;
}

/**
 * One arm attempt in a fresh workspace, seeded and shaped by the arm's plan.
 */
async function runArmAttempt(
  job: Job,
  options: Options,
  jobDir: string,
  number: number
): Promise<{ readonly attempt: core.Attempt; readonly lastRun: LastRun | undefined }> {
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), core.workspacePrefix));

  const plan = core.planArm({
    arm: job.arm,
    task: job.task,
    roots: job.roots,
    workspace,
    machine,
    model: options.model,
    effort: options.effort
  });

  writeSettings(workspace, plan.settings);

  for (const skill of plan.skills) {
    fs.cpSync(path.join(skillsDir, skill), path.join(workspace, '.claude/skills', skill), {
      recursive: true
    });
  }

  if (job.task.folders.seed) {
    fs.cpSync(job.task.folders.seed, workspace, { recursive: true });
  }

  fs.writeFileSync(path.join(jobDir, 'prompt.txt'), plan.prompt);

  const before = snapshot(workspace);
  const startedAt = Date.now();

  const invocation = await spawnClaude({
    cwd: workspace,
    prompt: plan.prompt,
    args: plan.args,
    timeoutMs: options.timeoutMs
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
 * Has the judge rule on the collected files, retrying an unreadable reply under the cap.
 */
async function judge(input: {
  readonly job: Job;
  readonly base: core.ArmRecord;
  readonly files: readonly string[];
  readonly options: Options;
  readonly jobDir: string;
}): Promise<core.JobRecord> {
  const { job, base, files, options, jobDir } = input;

  const prompt = core.buildJudgePrompt({
    task: job.task,
    files: files.map(file => ({
      path: file,
      content: fs.readFileSync(path.join(jobDir, 'files', file), 'utf8')
    })),
    finalMessage: base.finalMessage
  });

  const plan = core.planJudge({ machine, model: options.judgeModel, effort: options.effort });
  let fault = '';

  fs.writeFileSync(path.join(jobDir, 'judge-prompt.txt'), prompt);

  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    const cwd = fs.mkdtempSync(path.join(os.tmpdir(), core.workspacePrefix));

    writeSettings(cwd, plan.settings);

    const invocation = await spawnClaude({
      cwd,
      prompt,
      args: plan.args,
      timeoutMs: options.timeoutMs
    });

    fs.rmSync(cwd, { recursive: true, force: true });
    fs.writeFileSync(path.join(jobDir, `judge-${attempt}.jsonl`), invocation.stdout);
    fs.writeFileSync(path.join(jobDir, `judge-${attempt}.err`), invocation.stderr);

    const outcome = core.readStream(invocation);

    if (outcome.status == 'retryable') {
      ({ fault } = outcome);
      continue;
    }

    try {
      const verdicts = core.parseJudgeReply(outcome.run.finalMessage, job.task.checklist);

      return { ...base, outcome: 'judged', verdicts };
    } catch (error) {
      fault = String(error);
    }
  }

  return { ...base, outcome: 'failed', fault: `the judge: ${fault}` };
}

/**
 * The files the judge rules on, off the attempt that wrote code: a judge step always follows one.
 */
function judgedFiles(lastRun: LastRun | undefined): readonly string[] {
  assert.ok(lastRun, 'a judge step follows an attempt that wrote code');

  return lastRun.files.judged;
}

function writeSettings(directory: string, settings: core.WorkspaceSettings): void {
  fs.mkdirSync(path.join(directory, '.claude'), { recursive: true });
  fs.writeFileSync(
    path.join(directory, '.claude/settings.json'),
    `${JSON.stringify(settings, undefined, 2)}\n`
  );
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

  try {
    const [stdout, stderr] = await Promise.all([
      new Response(child.stdout).text(),
      new Response(child.stderr).text()
    ]);

    await child.exited;

    return { exitCode: child.exitCode, timedOut, stdout, stderr };
  } finally {
    clearTimeout(timer);
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
  const { values } = parseArgs({
    options: {
      'smoke': { type: 'boolean', default: false },
      'tasks': { type: 'string', default: 'bench/tasks' },
      'results': { type: 'string', default: 'bench/results' },
      'runs': { type: 'string', default: '4' },
      'concurrency': { type: 'string', default: '2' },
      'model': { type: 'string', default: 'opus' },
      'judge-model': { type: 'string', default: 'opus' },
      'effort': { type: 'string', default: 'medium' },
      'timeout-minutes': { type: 'string', default: '20' },
      'validate-only': { type: 'boolean', default: false }
    }
  });

  return {
    tasksDir: path.resolve(repoRoot, values.tasks),
    resultsDir: path.resolve(repoRoot, values.results),
    runs: core.parseCount('runs', values.runs),
    concurrency: core.parseCount('concurrency', values.concurrency),
    model: values.model,
    judgeModel: values['judge-model'],
    effort: values.effort,
    timeoutMs: core.parseCount('timeout-minutes', values['timeout-minutes']) * msPerMinute,
    isSmoke: values.smoke,
    isValidateOnly: values['validate-only']
  };
}
