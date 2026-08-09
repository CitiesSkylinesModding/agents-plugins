/* oxlint-disable no-console -- the driver reports its progress to whoever launched it. */
/* oxlint-disable node/no-sync -- single-shot script; synchronous IO keeps the shell readable. */

import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { oneLine } from 'common-tags';
import * as core from './core';

// The thin shell around core.ts: workspaces, spawning, retries, files. Every decision it looks like
// it makes belongs to the core, so this file stays untested by design (see the spec's test seams).
// The namespace import is what keeps that visible: every `core.` in here is a decision made
// elsewhere.

const arms: readonly core.ArmName[] = ['control', 'treatment'];

/**
 * The first attempt plus the two retries the run protocol allows for infrastructure failures.
 */
const maxAttempts = 3;

/**
 * Enough of an ISO timestamp to name a results directory to the second.
 */
const secondsPrecision = 'YYYY-MM-DDTHH-MM-SS'.length;

const msPerMinute = 60_000;

const repoRoot = path.resolve(import.meta.dirname, '..');
const smokeQuestionPath = path.join(import.meta.dirname, 'tests/fixtures/smoke.md');
const setupRecordPath = path.join(os.homedir(), '.cs2-modding/setup.md');

interface Options {
  readonly questionsDir: string;
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
  readonly question: core.Question;
  readonly arm: core.ArmName;
  readonly run: number;
  readonly roots: readonly core.ResolvedRoot[];
}

type Attempt<T> = { status: 'ok'; value: T } | { status: 'retryable'; fault: string };

await main();

async function main(): Promise<void> {
  const options = readOptions();

  const questions = options.isSmoke
    ? [core.loadQuestion(smokeQuestionPath)]
    : core.loadQuestions(options.questionsDir);

  if (!questions.length) {
    throw new Error(`No question files under ${options.questionsDir}.`);
  }

  // Resolving every root up front is what keeps an unprovisioned machine from spending money on
  // half a comparison: an unresolvable root throws here, before the first invocation.
  const record = core.parseSetupRecord(fs.readFileSync(setupRecordPath, 'utf8'));

  const jobs = questions.flatMap(question => {
    const roots = core.resolveRoots(question.roots, record, process.env);

    // The core resolves a root from what the record says; only the filesystem knows whether the
    // path still exists. Without this, a moved decompile passes --validate-only and fails once
    // every run is already spending.
    for (const root of roots) {
      if (!fs.existsSync(root.path)) {
        throw new Error(`The ${root.kind} root does not exist: ${root.path}`);
      }
    }

    return arms.flatMap(arm =>
      Array.from({ length: options.runs }, (_, index) => ({ question, arm, run: index + 1, roots }))
    );
  });

  // Everything above this line is what a scored invocation does before it can spend anything, so
  // running only that much is the whole of a question set's validation.
  if (options.isValidateOnly) {
    for (const question of questions) {
      console.log(`${question.slug}: roots ${question.roots.join(', ')} resolved.`);
    }

    console.log(`${questions.length} question file(s) valid.`);

    return;
  }

  const startedAt = new Date().toISOString();

  const outputDir = path.join(
    options.resultsDir,
    startedAt.replaceAll(':', '-').slice(0, secondsPrecision)
  );

  fs.mkdirSync(path.join(outputDir, 'raw'), { recursive: true });

  console.log(oneLine`
    ${jobs.length} runs over ${questions.length} question(s), ${options.model} against
    ${options.judgeModel} as judge, ${options.concurrency} at a time. Results: ${outputDir}
  `);

  const outcomes = await runPool(jobs, options.concurrency, job => runJob(job, options, outputDir));

  const runRecords: core.RunRecord[] = [];
  const failures: core.RunFailure[] = [];

  for (const outcome of outcomes) {
    if ('fault' in outcome) {
      failures.push(outcome);
    } else {
      runRecords.push(outcome);
    }
  }

  const summary = core.buildSummary({
    startedAt,
    model: options.model,
    judgeModel: options.judgeModel,
    concurrency: options.concurrency,
    runs: runRecords,
    failures
  });

  fs.writeFileSync(
    path.join(outputDir, 'summary.json'),
    `${JSON.stringify(summary, undefined, 2)}\n`
  );
  fs.writeFileSync(path.join(outputDir, 'summary.md'), core.renderMarkdownSummary(summary));

  console.log(`\n${core.renderMarkdownSummary(summary)}`);

  if (failures.length) {
    process.exitCode = 1;
  }
}

/**
 * One benchmarked answer and its judge score, or the fault that ended the run's last attempt.
 */
async function runJob(
  job: Job,
  options: Options,
  outputDir: string
): Promise<core.RunRecord | core.RunFailure> {
  const label = `${job.question.slug} ${job.arm} #${job.run}`;
  const rawPrefix = path.join(outputDir, 'raw', `${job.question.slug}-${job.arm}-${job.run}`);
  const armPrompt = core.buildArmPrompt({ arm: job.arm, question: job.question, roots: job.roots });

  fs.writeFileSync(`${rawPrefix}-prompt.txt`, armPrompt);

  // Timed per attempt rather than around the retry loop: a run whose first attempt burned the full
  // timeout is not a twenty-minute run, and the figure feeds both the wall aggregate and the
  // duration column whenever the CLI reports no duration of its own.
  let attemptStartedAt = Date.now();

  const answer = await withRetry(label, () => {
    attemptStartedAt = Date.now();

    return invokeArm(job, armPrompt, options, rawPrefix);
  });

  const wallMs = Date.now() - attemptStartedAt;

  if (!answer.ok) {
    return { question: job.question.slug, arm: job.arm, run: job.run, fault: answer.fault };
  }

  const judgePrompt = core.buildJudgePrompt({
    question: job.question,
    candidateAnswer: answer.value.answer
  });

  fs.writeFileSync(`${rawPrefix}-judge-prompt.txt`, judgePrompt);

  const verdict = await withRetry(`${label} judge`, () =>
    invokeJudge(judgePrompt, options, rawPrefix)
  );

  if (!verdict.ok) {
    return { question: job.question.slug, arm: job.arm, run: job.run, fault: verdict.fault };
  }

  console.log(`  ${label}: ${verdict.value.score}/10`);

  return core.buildRunRecord({
    question: job.question.slug,
    arm: job.arm,
    run: job.run,
    attempts: answer.attempts,
    wallMs,
    result: answer.value,
    verdict: verdict.value
  });
}

/**
 * Spawns one arm in a throwaway workspace outside any repository, shaped by the arm.
 */
async function invokeArm(
  job: Job,
  prompt: string,
  options: Options,
  rawPrefix: string
): Promise<Attempt<core.CliResult>> {
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), 'cs2-bench-'));

  if (job.arm == 'treatment') {
    const target = path.join(workspace, path.dirname(core.skillEntryPoint));

    fs.cpSync(path.join(repoRoot, core.skillSourceDir), target, { recursive: true });
  }

  const invocation = await spawnClaude({
    cwd: workspace,
    prompt,
    timeoutMs: options.timeoutMs,
    args: [
      ...baseArgs(options.model, options),
      '--tools',
      'Read,Glob,Grep',
      '--allowed-tools',
      'Read,Glob,Grep',
      '--add-dir',
      ...job.roots.map(root => root.path)
    ]
  });

  fs.writeFileSync(`${rawPrefix}-cli.json`, invocation.stdout || invocation.stderr);

  const outcome = core.classifyInvocation(invocation);

  // A failed workspace is kept for inspection; a spent one is not worth the disk.
  if (outcome.status == 'ok') {
    fs.rmSync(workspace, { recursive: true, force: true });

    return { status: 'ok', value: outcome.result };
  }

  return { status: 'retryable', fault: `${outcome.fault} (workspace kept at ${workspace})` };
}

/**
 * Grades one answer. The judge gets no tools and no directory grant: it only reads its prompt.
 */
async function invokeJudge(
  prompt: string,
  options: Options,
  rawPrefix: string
): Promise<Attempt<core.JudgeVerdict>> {
  const invocation = await spawnClaude({
    cwd: os.tmpdir(),
    prompt,
    timeoutMs: options.timeoutMs,
    args: [...baseArgs(options.judgeModel, options), '--tools', '']
  });

  fs.writeFileSync(`${rawPrefix}-judge.json`, invocation.stdout || invocation.stderr);

  const outcome = core.classifyInvocation(invocation);

  if (outcome.status != 'ok') {
    return outcome;
  }

  try {
    return { status: 'ok', value: core.parseJudgeVerdict(outcome.result.answer) };
  } catch (error) {
    return { status: 'retryable', fault: String(error) };
  }
}

/**
 * The isolation contract both arms and the judge run under.
 * `--safe-mode` disables every customization on this machine (CLAUDE.md, skills, plugins, hooks,
 * MCP servers) while leaving auth and permissions working, and `--tools` carries the subagent
 * denial: the Task tool is simply not in the set an invocation may use.
 */
function baseArgs(model: string, options: Options): readonly string[] {
  return [
    '-p',
    '--output-format',
    'json',
    '--model',
    model,
    '--effort',
    options.effort,
    '--safe-mode',
    '--disable-slash-commands'
  ];
}

async function spawnClaude(input: {
  readonly cwd: string;
  readonly prompt: string;
  readonly args: readonly string[];
  readonly timeoutMs: number;
}): Promise<{ exitCode: number | null; timedOut: boolean; stdout: string; stderr: string }> {
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

async function withRetry<T>(
  label: string,
  attempt: () => Promise<Attempt<T>>
): Promise<{ ok: true; value: T; attempts: number } | { ok: false; fault: string }> {
  let fault = '';

  for (let attempts = 1; attempts <= maxAttempts; attempts++) {
    const outcome = await attempt();

    if (outcome.status == 'ok') {
      return { ok: true, value: outcome.value, attempts };
    }

    ({ fault } = outcome);

    console.warn(`  ${label}: attempt ${attempts}/${maxAttempts} failed, ${fault}`);
  }

  return { ok: false, fault };
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
      'questions': { type: 'string', default: 'bench/questions' },
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
    questionsDir: path.resolve(repoRoot, values.questions),
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
