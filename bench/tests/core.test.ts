import { describe, expect, test } from 'bun:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import {
  aggregate,
  buildArmPrompt,
  buildJudgePrompt,
  buildRunRecord,
  buildSummary,
  classifyInvocation,
  loadQuestion,
  parseJudgeVerdict,
  parseQuestion,
  parseSetupRecord,
  QuestionFault,
  renderMarkdownSummary,
  resolveRoots,
  skillEntryPoint,
  type BenchSummary,
  type CliResult,
  type ResolvedRoot,
  type RunFailure,
  type RunRecord
} from '../core';

const smokeQuestionPath = path.join(import.meta.dirname, 'fixtures/smoke.md');

const validQuestion = [
  '# A title',
  '',
  '## Prompt',
  '',
  'Which system writes the component?',
  '',
  '## Verified answer',
  '',
  'ElectricityFlowSystem, at src/Game/Simulation/ElectricityFlowSystem.cs:42.',
  '',
  '## Rubric',
  '',
  '- 6: Names ElectricityFlowSystem.',
  '- 4: Says the write happens in the simulation phase.',
  '',
  '## Roots',
  '',
  '- decompile',
  '- ui-bundle',
  ''
].join('\n');

/**
 * Swaps one section's body in the valid fixture, so each fault test differs by one thing only.
 */
function withSection(heading: string, body: string): string {
  return validQuestion.replace(
    new RegExp(`(## ${heading}\\n\\n)[^#]*`, 'u'),
    `$1${body ? `${body}\n\n` : ''}`
  );
}

const roots: readonly ResolvedRoot[] = [
  { kind: 'decompile', label: 'the decompiled game', path: 'D:\\decompile' },
  { kind: 'ui-bundle', label: 'the reformatted UI bundle copy', path: 'D:\\ui\\source.js' }
];

describe('question validation', () => {
  test('parses a well-formed file into its four parts', () => {
    const question = parseQuestion('a-slug', validQuestion);

    expect(question.slug).toBe('a-slug');
    expect(question.title).toBe('A title');
    expect(question.prompt).toBe('Which system writes the component?');
    expect(question.verifiedAnswer).toContain('ElectricityFlowSystem.cs:42');
    expect(question.rubric).toEqual([
      { weight: 6, text: 'Names ElectricityFlowSystem.' },
      { weight: 4, text: 'Says the write happens in the simulation phase.' }
    ]);
    expect(question.roots).toEqual(['decompile', 'ui-bundle']);
  });

  test('loads the smoke question from disk', () => {
    const question = loadQuestion(smokeQuestionPath);

    expect(question.slug).toBe('smoke');
    expect(question.roots).toEqual(['decompile']);
    expect(question.rubric).toHaveLength(2);
  });

  test.each([
    ['missing-section', validQuestion.replace('## Rubric', '## Grading')],
    ['empty-section', withSection('Prompt', '')],
    ['rubric-size', withSection('Rubric', '- 10: The only point.')],
    ['rubric-line', withSection('Rubric', '- Names the system.\n- 10: Says why.')],
    ['rubric-weight-sum', withSection('Rubric', '- 6: One point.\n- 3: Another point.')],
    ['unknown-root', withSection('Roots', '- decompile\n- wiki')],
    ['duplicate-root', withSection('Roots', '- decompile\n- decompile')],
    ['missing-decompile-root', withSection('Roots', '- install')]
  ])('rejects a malformed file with the %s fault', (fault, text) => {
    expect(faultOf(text)).toBe(fault);
  });
});

describe('prompt assembly', () => {
  const question = parseQuestion('a-slug', validQuestion);
  const control = buildArmPrompt({ arm: 'control', question, roots });
  const treatment = buildArmPrompt({ arm: 'treatment', question, roots });

  test('the control prompt forbids skills and reading beyond the roots', () => {
    expect(control).toContain(question.prompt);
    expect(control).toContain('Use no skills');
    expect(control).toContain('read nothing beyond');
    expect(control).not.toContain(skillEntryPoint);
  });

  test('the treatment prompt names the skill entry point and does not forbid it', () => {
    expect(treatment).toContain(question.prompt);
    expect(treatment).toContain(skillEntryPoint);
    expect(treatment.toLowerCase()).not.toContain('use no skills');
  });

  test('both arms are closed to the same reach, since the Read grant is machine-wide', () => {
    // The prompt is the only thing holding an arm inside its roots, so an arm short of the clause
    // is an arm free to score points off sources the other one was forbidden.
    expect(treatment).toContain('Read nothing beyond that documentation and the provided roots');
    expect(control).toContain('read nothing beyond the provided roots');

    for (const prompt of [control, treatment]) {
      expect(prompt).toContain("none of this machine's other repositories in particular");
    }
  });

  test('both arms carry exactly the question roots', () => {
    for (const prompt of [control, treatment]) {
      for (const root of roots) {
        expect(prompt).toContain(root.path);
      }

      expect(prompt).not.toContain('%CSII_');
      expect(prompt).not.toContain('setup.md');
    }
  });

  test('the judge prompt carries the rubric and no arm label', () => {
    const judge = buildJudgePrompt({ question, candidateAnswer: 'The answer under test.' });

    expect(judge).toContain(question.prompt);
    expect(judge).toContain(question.verifiedAnswer);
    expect(judge).toContain('Names ElectricityFlowSystem.');
    expect(judge).toContain('The answer under test.');

    expect(judge.toLowerCase()).not.toContain('control');
    expect(judge.toLowerCase()).not.toContain('treatment');
    expect(judge).not.toContain(skillEntryPoint);
    for (const root of roots) {
      expect(judge).not.toContain(root.path);
    }
  });
});

describe('result parsing', () => {
  const cliJson = {
    type: 'result',
    subtype: 'success',
    is_error: false,
    duration_ms: 90_000,
    duration_api_ms: 61_000,
    num_turns: 7,
    result: 'The answer.',
    total_cost_usd: 0.42,
    usage: {
      input_tokens: 12,
      cache_creation_input_tokens: 30_000,
      cache_read_input_tokens: 500_000,
      output_tokens: 1200
    }
  };

  function ok(overrides: Record<string, unknown> = {}): CliResult {
    const outcome = classifyInvocation({
      exitCode: 0,
      timedOut: false,
      stdout: JSON.stringify({ ...cliJson, ...overrides }),
      stderr: ''
    });

    expect(outcome.status).toBe('ok');
    assertOk(outcome);

    return outcome.result;
  }

  test('computes the fresh-input headline from input plus cache creation', () => {
    const result = ok();

    expect(result.freshInputTokens).toBe(30_012);
    expect(result.outputTokens).toBe(1200);
    expect(result.cacheReadTokens).toBe(500_000);
    expect(result.costUsd).toBe(0.42);
    expect(result.numTurns).toBe(7);
    expect(result.answer).toBe('The answer.');
  });

  test('falls back to the CLI wall duration when the API duration is missing', () => {
    const withApi = buildRunRecord({ ...runInput, result: ok() });
    const withoutApi = buildRunRecord({
      ...runInput,
      result: ok({ duration_api_ms: undefined })
    });

    expect(withApi.durationMs).toBe(61_000);
    expect(withApi.durationSource).toBe('api');
    expect(withoutApi.durationMs).toBe(90_000);
    expect(withoutApi.durationSource).toBe('cli');
  });

  test('carries the headline into the run record', () => {
    const record = buildRunRecord({ ...runInput, result: ok() });

    expect(record.headlineTokens).toBe(31_212);
    expect(record.score).toBe(8);
    expect(record.wallMs).toBe(120_000);
    expect(record.attempts).toBe(2);
  });

  test.each([
    ['a nonzero exit', { exitCode: 1, timedOut: false, stdout: '', stderr: 'boom' }],
    ['a timeout', { exitCode: null, timedOut: true, stdout: '', stderr: '' }],
    ['unparseable output', { exitCode: 0, timedOut: false, stdout: 'not json', stderr: '' }],
    [
      'an error result',
      {
        exitCode: 0,
        timedOut: false,
        stdout: JSON.stringify({ ...cliJson, is_error: true }),
        stderr: ''
      }
    ],
    [
      'a result missing its usage block',
      {
        exitCode: 0,
        timedOut: false,
        stdout: JSON.stringify({ ...cliJson, usage: undefined }),
        stderr: ''
      }
    ]
  ])('classifies %s as retryable', (_name, invocation) => {
    const outcome = classifyInvocation(invocation);

    expect(outcome.status).toBe('retryable');
  });

  test('parses the judge verdict off its trailing lines', () => {
    const verdict = parseJudgeVerdict(
      'Some reasoning first.\n\nSCORE: 7\nJUSTIFICATION: Missed the namespace.'
    );

    expect(verdict).toEqual({ score: 7, justification: 'Missed the namespace.' });
  });

  test('rejects a judge answer with no score line', () => {
    expect(() => parseJudgeVerdict('It was pretty good.')).toThrow(/score/iu);
  });

  test.each([
    ['emphasised', '**SCORE: 8**\n**JUSTIFICATION: Missed the phase.**', 8],
    ['scale-restating', 'SCORE: 8/10\nJUSTIFICATION: Missed the phase.', 8],
    ['half-point', 'SCORE: 7.5\nJUSTIFICATION: Missed the phase.', 7.5],
    ['reconsidered', 'SCORE: 4\nthen again\nSCORE: 8\nJUSTIFICATION: Missed the phase.', 8]
  ])('reads a %s verdict rather than retrying its formatting', (_name, text, score) => {
    expect(parseJudgeVerdict(text)).toEqual({ score, justification: 'Missed the phase.' });
  });

  test('rejects a score off the /10 scale instead of averaging it', () => {
    expect(() => parseJudgeVerdict('SCORE: 85\nJUSTIFICATION: Graded percent.')).toThrow(/scale/u);
  });
});

describe('aggregation and rendering', () => {
  const records = [
    runRecord({ arm: 'control', run: 1, score: 4, wallMs: 100 }),
    runRecord({ arm: 'control', run: 2, score: 5, wallMs: 200 }),
    runRecord({ arm: 'control', run: 3, score: 5, wallMs: 300 }),
    runRecord({ arm: 'control', run: 4, score: 10, wallMs: 400 }),
    runRecord({ arm: 'treatment', run: 1, score: 9, wallMs: 100 })
  ];

  test('takes the mean and the median per arm per question', () => {
    const aggregates = aggregate(records);

    expect(aggregates).toHaveLength(2);

    const control = aggregates.find(candidate => candidate.arm == 'control');

    assert.ok(control, 'the fixture runs include a control arm');

    expect(control.question).toBe('a-slug');
    expect(control.runs).toBe(4);
    expect(control.score.mean).toBe(6);
    expect(control.score.median).toBe(5);
    expect(control.wallMs.median).toBe(250);
    expect(control.headlineTokens.mean).toBe(31_212);
  });

  test('renders every recorded field as a per-run row, then the aggregates', () => {
    const markdown = renderMarkdownSummary(summaryOf(records));

    for (const record of records) {
      expect(markdown).toContain(`| ${record.arm} | ${record.run} | ${record.score} |`);
    }

    expect(markdown).toContain('| control | 4 | 6.00 | 5.00 |');
    expect(markdown).toContain('cheap-model');
    expect(markdown).not.toContain('concurrency-distorted');
  });

  test('renders a run that exhausted its retries instead of dropping it', () => {
    const failure = {
      question: 'a-slug',
      arm: 'treatment',
      run: 2,
      fault: 'the run hit its timeout'
    } as const;

    expect(renderMarkdownSummary(summaryOf(records, [failure]))).toContain(
      '| a-slug | treatment | 2 | the run hit its timeout |'
    );
  });

  test('reports an unpriced arm as unpriced rather than as a free one', () => {
    const unpriced = [runRecord({ arm: 'control', run: 1, costless: true })];
    const [entry] = aggregate(unpriced);

    assert.ok(entry, 'one arm went in');

    expect(entry.costUsd).toBeUndefined();
    expect(renderMarkdownSummary(summaryOf(unpriced))).toContain('(none reported)');
  });

  test('names the clocks an arm mean pools, so a mixed one is not read as one scale', () => {
    const mixed = [...records, runRecord({ arm: 'control', run: 5, apiless: true })];
    const control = aggregate(mixed).find(candidate => candidate.arm == 'control');

    assert.ok(control, 'the fixture runs include a control arm');

    expect(control.durationSources).toEqual(['api', 'cli']);
    expect(renderMarkdownSummary(summaryOf(mixed))).toContain('api+cli');
  });

  test('flags wall times as concurrency-distorted when a run reports no API duration', () => {
    const summary = summaryOf([...records, runRecord({ arm: 'treatment', run: 2, apiless: true })]);

    expect(summary.areWallTimesDistorted).toBe(true);
    expect(renderMarkdownSummary(summary)).toContain('concurrency-distorted');
  });
});

describe('root resolution', () => {
  const record = parseSetupRecord(
    [
      '# cs2-modding setup',
      '',
      'Game install: (none)',
      String.raw`Decompile root: D:\decompile`,
      String.raw`UI bundle copy: D:\ui\source.js`
    ].join('\n')
  );

  test('reads the user-chosen roots off the record and the install off the environment', () => {
    const resolved = resolveRoots(['decompile', 'install'], record, {
      CSII_INSTALLATIONPATH: 'D:\\game'
    });

    expect(resolved).toEqual([
      { kind: 'decompile', label: 'the decompiled game', path: 'D:\\decompile' },
      { kind: 'install', label: 'the installed game', path: 'D:\\game' }
    ]);
  });

  test('refuses to run on a root the machine cannot resolve', () => {
    expect(() => resolveRoots(['install'], record, {})).toThrow(/CSII_INSTALLATIONPATH/u);
  });

  test('treats a (none) record value as unprovisioned', () => {
    const unprovisioned = parseSetupRecord('Decompile root: (none)');

    expect(() => resolveRoots(['decompile'], unprovisioned, {})).toThrow(/Decompile root/u);
  });
});

const runInput = {
  question: 'a-slug',
  arm: 'control',
  run: 1,
  attempts: 2,
  wallMs: 120_000,
  verdict: { score: 8, justification: 'Good enough.' }
} as const;

function runRecord(overrides: {
  arm: 'control' | 'treatment';
  run: number;
  score?: number;
  wallMs?: number;
  apiless?: boolean;
  costless?: boolean;
}): RunRecord {
  return buildRunRecord({
    ...runInput,
    arm: overrides.arm,
    run: overrides.run,
    wallMs: overrides.wallMs ?? 100,
    verdict: { score: overrides.score ?? 5, justification: 'Because.' },
    result: {
      answer: 'The answer.',
      outputTokens: 1200,
      freshInputTokens: 30_012,
      cacheReadTokens: 500_000,
      costUsd: overrides.costless ? undefined : 0.42,
      numTurns: 7,
      cliDurationMs: 90_000,
      apiDurationMs: overrides.apiless ? undefined : 61_000
    }
  });
}

function summaryOf(
  records: readonly RunRecord[],
  failures: readonly RunFailure[] = []
): BenchSummary {
  return buildSummary({
    startedAt: '2026-08-09T00:00:00.000Z',
    model: 'cheap-model',
    judgeModel: 'cheap-model',
    concurrency: 2,
    runs: records,
    failures
  });
}

/**
 * Names the fault a malformed question raises, so a test asserts the code and not the wording.
 */
function faultOf(text: string): string {
  try {
    parseQuestion('a-slug', text);
  } catch (error) {
    return error instanceof QuestionFault ? error.fault : `unexpected error: ${String(error)}`;
  }

  return 'no fault raised';
}

function assertOk(
  outcome: ReturnType<typeof classifyInvocation>
): asserts outcome is Extract<ReturnType<typeof classifyInvocation>, { status: 'ok' }> {
  if (outcome.status != 'ok') {
    throw new Error(`Expected an ok outcome, got: ${outcome.fault}`);
  }
}
