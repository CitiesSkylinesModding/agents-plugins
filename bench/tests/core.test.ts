/* oxlint-disable node/no-sync -- the task-folder tests build their folders on disk. */

import { afterAll, describe, expect, test } from 'bun:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { oneLine } from 'common-tags';
import {
  armNames,
  buildJudgePrompt,
  buildSummary,
  calibrationVerdict,
  collectedFiles,
  findContamination,
  findSkillUse,
  jobSpend,
  planJobs,
  planNextAttempt,
  loadTask,
  parseJudgeReply,
  parseSetupRecord,
  parseTask,
  planArm,
  readStream,
  renderReport,
  renderResults,
  resolveRoots,
  TaskFault,
  workspacePrefix,
  type ArmName,
  type ArmPlan,
  type ArmSummary,
  type BenchSummary,
  type ItemRuling,
  type JobKey,
  type JobRecord,
  type RawInvocation,
  type TaskKind,
  type Verdict,
  type Attempt,
  type ResolvedRoot,
  type RunSettings,
  type ToolCall
} from '../core';

const fixtureTaskDir = path.join(import.meta.dirname, 'fixtures/daily-blackout-log');

const scratchDirs: string[] = [];

afterAll(() => {
  for (const dir of scratchDirs) {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

const skillsDir = path.resolve(import.meta.dirname, '../../plugins/cs2-modding/skills');

const lifecycleReference =
  'cs2-modding/references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md';

const checklistItem = (name: string): string =>
  [
    `### ${name}`,
    '',
    '- Falls in: `GetUpdateInterval` returns 10000.',
    '- Avoids: `GetUpdateInterval` returns 262144 / 16.',
    `- Reference: ${lifecycleReference}`,
    '- Source: src/Game/Game/UpdateSystem.cs:288',
    ''
  ].join('\n');

const validTask = [
  '# A title',
  '',
  '## Kind',
  '',
  'feature request',
  '',
  '## Prompt',
  '',
  'I want my mod to log something once a day.',
  '',
  '## Checklist',
  '',
  checklistItem('The interval is a power of two'),
  checklistItem('The system runs in a phase that honours its interval'),
  checklistItem('The day boundary is not assumed to fall on offset zero'),
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
  return validTask.replace(
    new RegExp(`(## ${heading}\\n\\n)[\\s\\S]*?(?=\\n## |$)`, 'u'),
    `$1${body}\n`
  );
}

describe('task validation', () => {
  test('parses a well-formed file into its parts', () => {
    const task = parseTask('a-slug', validTask);

    expect(task.slug).toBe('a-slug');
    expect(task.title).toBe('A title');
    expect(task.kind).toBe('feature request');
    expect(task.prompt).toBe('I want my mod to log something once a day.');
    expect(task.roots).toEqual(['decompile', 'ui-bundle']);
    expect(task.checklist).toHaveLength(3);
    expect(task.checklist[0]).toEqual({
      name: 'The interval is a power of two',
      fallsIn: '`GetUpdateInterval` returns 10000.',
      avoids: '`GetUpdateInterval` returns 262144 / 16.',
      reference: lifecycleReference,
      source: 'src/Game/Game/UpdateSystem.cs:288'
    });
  });

  test('reads a bug report as the second kind', () => {
    expect(parseTask('a-slug', withSection('Kind', 'bug report')).kind).toBe('bug report');
  });

  const sevenItems = Array.from({ length: 7 }, (_, index) => checklistItem(`Item ${index}`));

  test.each([
    ['missing-title', validTask.replace('# A title', 'A title')],
    ['missing-section', validTask.replace('## Checklist', '## Rubric')],
    ['empty-section', withSection('Prompt', '')],
    ['unknown-kind', withSection('Kind', 'question')],
    ['checklist-size', withSection('Checklist', checklistItem('Only one'))],
    ['checklist-size', withSection('Checklist', sevenItems.join('\n'))],
    ['item-field', validTask.replace('- Source: src/Game/Game/UpdateSystem.cs:288\n', '')],
    ['item-field', validTask.replace('- Avoids: `GetUpdateInterval` returns', '- Avoid:')],
    [
      'duplicate-item',
      validTask.replace(
        '### The system runs in a phase that honours its interval',
        '### The interval is a power of two'
      )
    ],
    ['unknown-root', withSection('Roots', '- decompile\n- wiki')],
    ['duplicate-root', withSection('Roots', '- decompile\n- decompile')],
    ['missing-decompile-root', withSection('Roots', '- install')]
  ])('rejects a malformed file with the %s fault', (fault, text) => {
    expect(faultOf(() => parseTask('a-slug', text))).toBe(fault);
  });
});

describe('task folders', () => {
  test('loads the fixture task, its samples and no seed', () => {
    const task = loadTask(fixtureTaskDir, skillsDir);

    expect(task.slug).toBe('daily-blackout-log');
    expect(task.folders.falls).toBe(path.join(fixtureTaskDir, 'falls'));
    expect(task.folders.avoids).toBe(path.join(fixtureTaskDir, 'avoids'));
    expect(task.folders.seed).toBeUndefined();
  });

  test('seeds a feature request from its seed folder', () => {
    const dir = scratchTask(validTask, ['falls', 'avoids', 'seed']);

    expect(loadTask(dir, skillsDir).folders.seed).toBe(path.join(dir, 'seed'));
  });

  test('seeds a bug report from its falling sample', () => {
    const dir = scratchTask(withSection('Kind', 'bug report'), ['falls', 'avoids']);

    expect(loadTask(dir, skillsDir).folders.seed).toBe(path.join(dir, 'falls'));
  });

  test.each([
    ['missing-sample', validTask, ['falls']],
    ['empty-sample', validTask, ['falls', 'avoids', '!avoids']],
    ['seed-on-bug-report', withSection('Kind', 'bug report'), ['falls', 'avoids', 'seed']],
    [
      'unknown-reference',
      validTask.replace('mod-lifecycle-and-ordering.md', 'no-such-reference.md'),
      ['falls', 'avoids']
    ]
  ])('rejects a task folder with the %s fault', (fault, text, folders) => {
    const dir = scratchTask(text, folders);

    expect(faultOf(() => loadTask(dir, skillsDir))).toBe(fault);
  });
});

const roots: readonly ResolvedRoot[] = [
  { kind: 'decompile', path: '/data/cs2-decompile' },
  { kind: 'ui-bundle', path: '/data/cs2-decompile/src-ui/source.js' }
];

const machine = {
  repoRoot: '/home/modder/agents-plugins',
  home: '/home/modder',
  tmpDir: '/tmp'
} as const;

function armPlan(arm: ArmName): ArmPlan {
  return planArm({
    arm,
    task: parseTask('a-slug', validTask),
    roots,
    workspace: `/tmp/${workspacePrefix}ws`,
    machine,
    model: 'sonnet',
    effort: 'low'
  });
}

describe('root resolution', () => {
  const record = parseSetupRecord(
    [
      '# cs2-modding setup',
      '',
      'Game install: (none)',
      'Decompile root: /data/cs2-decompile/',
      'UI bundle copy: /data/ui/source.js'
    ].join('\n')
  );

  test('reads the chosen roots off the record and the install off the environment', () => {
    const resolved = resolveRoots(['ui-bundle', 'install'], record, {
      CSII_INSTALLATIONPATH: '/games/cs2'
    });

    expect(resolved).toEqual([
      { kind: 'ui-bundle', path: '/data/ui/source.js' },
      { kind: 'install', path: '/games/cs2' }
    ]);
  });

  test('normalises a root written with a trailing slash', () => {
    expect(resolveRoots(['decompile'], record, {})).toEqual([
      { kind: 'decompile', path: '/data/cs2-decompile' }
    ]);
  });

  test('treats a (none) record value as unprovisioned', () => {
    expect(() => resolveRoots(['install'], record, {})).toThrow(/CSII_INSTALLATIONPATH/u);
  });

  test('falls back to the record when the install variable is set but empty', () => {
    const installed = parseSetupRecord('Game install: /games/cs2');

    expect(resolveRoots(['install'], installed, { CSII_INSTALLATIONPATH: '' })).toEqual([
      { kind: 'install', path: '/games/cs2' }
    ]);
  });
});

describe('arm plans', () => {
  const control = armPlan('control');
  const treatment = armPlan('treatment');

  test('both arms get the byte-identical prompt', () => {
    expect(treatment.prompt).toBe(control.prompt);
  });

  test('the prompt is the request, then where the roots are, then the two closing lines', () => {
    const paragraphs = control.prompt.split('\n\n');

    expect(paragraphs[0]).toBe('I want my mod to log something once a day.');
    expect(paragraphs[1]).toBe(
      oneLine`
        The game's decompiled source is at /data/cs2-decompile and a reformatted copy of its UI
        bundle is at /data/cs2-decompile/src-ui/source.js, if you need to look anything up.
      `
    );
    expect(paragraphs.slice(2)).toEqual([
      [
        'Write the code without asking me questions.',
        oneLine`
          Don't build, test or run anything: your only side effects should be the files that
          answer this request.
        `
      ].join('\n')
    ]);
  });

  test('the prompt carries none of the retired wrapper and names no skill, web or judge', () => {
    for (const retired of [
      'How to answer',
      'Source roots',
      'What to produce',
      'Read it first',
      'Use no skills',
      'follow-up question',
      'skill',
      'documentation',
      'web',
      'judge'
    ]) {
      expect(control.prompt.toLowerCase()).not.toContain(retired.toLowerCase());
    }
  });

  test('only the treatment arm gets the four cs2 skills', () => {
    expect(treatment.skills).toEqual([
      'cs2-mod-project',
      'cs2-modding',
      'cs2-modding-setup',
      'cs2-modding-ui'
    ]);
    expect(control.skills).toEqual([]);
  });

  test('both arms run under the same flags, granted the workspace and the roots', () => {
    expect(treatment.args).toEqual(control.args);
    expect(control.args).toContain('--setting-sources');
    expect(control.args.join(' ')).toContain(
      'Edit(//tmp/cs2-bench-ws/**),Write(//tmp/cs2-bench-ws/**)'
    );
    expect(control.args.slice(control.args.indexOf('--add-dir') + 1)).toEqual(
      roots.map(root => root.path)
    );
  });

  test('both arms deny the repo, the config and the setup record; control also the copies', () => {
    const shared = [
      'Read(//home/modder/agents-plugins/**)',
      'Read(//home/modder/.claude/**)',
      'Read(//home/modder/.cs2-modding/**)',
      'Skill(code-review)'
    ];

    expect(treatment.settings.permissions.deny).toEqual(shared);
    expect(control.settings.permissions.deny).toEqual([
      ...shared,
      'Read(//tmp/cs2-bench-*/.claude/skills/**)'
    ]);
    expect(control.settings.autoMemoryEnabled).toBe(false);
  });
});

/**
 * A streamed run in the CLI's `stream-json` shape: one tool call per entry, each answered in the
 * next event, then the closing result event.
 */
function streamOf(
  calls: ReadonlyArray<{
    name: string;
    input: Record<string, unknown>;
    result?: unknown;
    isError?: boolean;
    isDenied?: boolean;
  }>,
  result: Record<string, unknown> = {}
): string {
  const events: unknown[] = [{ type: 'system', subtype: 'init', tools: ['Read'] }];

  for (const [index, call] of calls.entries()) {
    const id = `toolu_${index}`;

    events.push(
      {
        type: 'assistant',
        message: { content: [{ type: 'tool_use', id, name: call.name, input: call.input }] }
      },
      {
        type: 'user',
        message: {
          content: [
            { type: 'tool_result', tool_use_id: id, is_error: call.isError ?? false, content: 'x' }
          ]
        },
        tool_use_result: call.result ?? 'x'
      }
    );
  }

  events.push({
    type: 'result',
    subtype: 'success',
    is_error: false,
    duration_ms: 90_000,
    duration_api_ms: 61_000,
    num_turns: 7,
    result: 'I wrote the mod.',
    total_cost_usd: 0.42,
    usage: { input_tokens: 12, output_tokens: 1200 },
    permission_denials: calls
      .map((call, index) => ({ call, index }))
      .filter(({ call }) => call.isDenied)
      .map(({ call, index }) => ({ tool_name: call.name, tool_use_id: `toolu_${index}` })),
    ...result
  });

  return events.map(event => JSON.stringify(event)).join('\n');
}

describe('stream reading', () => {
  const invocation = (stdout: string): RawInvocation => ({
    exitCode: 0,
    timedOut: false,
    stdout,
    stderr: ''
  });

  test('records every tool call with its input, error and denial', () => {
    const outcome = readStream(
      invocation(
        streamOf([
          { name: 'Read', input: { file_path: '/data/a.cs' } },
          { name: 'Bash', input: { command: 'cat ~/.claude/x' }, isError: true, isDenied: true },
          { name: 'Write', input: { file_path: '/tmp/ws/Mod.cs', content: '...' } }
        ])
      )
    );

    assert.ok(outcome.status == 'ok');

    expect(outcome.run.toolCalls).toEqual([
      {
        name: 'Read',
        input: { file_path: '/data/a.cs' },
        isError: false,
        isDenied: false,
        resultUrls: [],
        isForked: false
      },
      {
        name: 'Bash',
        input: { command: 'cat ~/.claude/x' },
        isError: true,
        isDenied: true,
        resultUrls: [],
        isForked: false
      },
      {
        name: 'Write',
        input: { file_path: '/tmp/ws/Mod.cs', content: '...' },
        isError: false,
        isDenied: false,
        resultUrls: [],
        isForked: false
      }
    ]);
  });

  test('reads the final message, cost, turns and time off the result event', () => {
    const outcome = readStream(invocation(streamOf([])));

    assert.ok(outcome.status == 'ok');

    expect(outcome.run.finalMessage).toBe('I wrote the mod.');
    expect(outcome.run.spend).toEqual({
      costUsd: 0.42,
      numTurns: 7,
      cliDurationMs: 90_000,
      apiDurationMs: 61_000
    });
  });

  test('exposes the URLs a web search returned and a skill that forked', () => {
    const outcome = readStream(
      invocation(
        streamOf([
          {
            name: 'WebSearch',
            input: { query: 'cs2 modding' },
            result: {
              query: 'cs2 modding',
              results: [
                {
                  content: [
                    { title: 'a', url: 'https://a.test/1' },
                    { title: 'b', url: 'https://b.test/2' }
                  ]
                },
                'A summary.'
              ]
            }
          },
          {
            name: 'Skill',
            input: { skill: 'code-review' },
            result: { success: true, status: 'forked', agentId: 'x' }
          }
        ])
      )
    );

    assert.ok(outcome.status == 'ok');

    expect(outcome.run.toolCalls.map(call => call.resultUrls)).toEqual([
      ['https://a.test/1', 'https://b.test/2'],
      []
    ]);
    expect(outcome.run.toolCalls.map(call => call.isForked)).toEqual([false, true]);
  });

  test.each([
    ['a nonzero exit', { exitCode: 1, timedOut: false, stdout: '', stderr: 'boom' }],
    ['a timeout', { exitCode: null, timedOut: true, stdout: '', stderr: '' }],
    ['a stream with no result event', invocation('{"type":"system","subtype":"init"}')],
    ['an error result', invocation(streamOf([], { is_error: true, subtype: 'error_max_turns' }))],
    ['a result with no message', invocation(streamOf([], { result: undefined }))]
  ])('classifies %s as retryable', (_name, raw) => {
    expect(readStream(raw).status).toBe('retryable');
  });

  test('keeps the spend of an attempt that failed after reporting it', () => {
    const outcome = readStream(
      invocation(streamOf([], { is_error: true, subtype: 'error_max_turns' }))
    );

    assert.ok(outcome.status == 'retryable');

    expect(outcome.spend?.costUsd).toBe(0.42);
  });

  test('keeps the spend of an attempt the CLI exited nonzero on', () => {
    const outcome = readStream({
      exitCode: 1,
      timedOut: false,
      stdout: streamOf([], { is_error: true, subtype: 'error_during_execution' }),
      stderr: 'Error: the session failed'
    });

    assert.ok(outcome.status == 'retryable');

    expect(outcome.fault).toContain('the session failed');
    expect(outcome.spend?.costUsd).toBe(0.42);
  });
});

describe('file collection', () => {
  const before = new Map([
    ['Mod.cs', 'h1'],
    ['BlackoutLogSystem.cs', 'h2'],
    ['.claude/settings.json', 'h3']
  ]);

  const after = new Map([
    ['Mod.cs', 'h1'],
    ['BlackoutLogSystem.cs', 'h2-edited'],
    ['.claude/settings.json', 'h3'],
    ['BlackoutLog/Settings.cs', 'h4']
  ]);

  test('tells the files a run created or changed from the ones it left alone', () => {
    expect(collectedFiles(before, after).changed).toEqual([
      'BlackoutLog/Settings.cs',
      'BlackoutLogSystem.cs'
    ]);
  });

  test('hands the judge the seeded files the run left alone too', () => {
    expect(collectedFiles(before, after).judged).toEqual([
      'BlackoutLog/Settings.cs',
      'BlackoutLogSystem.cs',
      'Mod.cs'
    ]);
  });

  test('leaves out build output and the workspace configuration', () => {
    const built = new Map([
      ...before,
      ['BlackoutLog/bin/Debug/BlackoutLog.dll', 'h5'],
      ['BlackoutLog/obj/project.assets.json', 'h6'],
      ['UI/node_modules/react/index.js', 'h7'],
      ['.claude/settings.local.json', 'h8'],
      ['UI/src/index.tsx', 'h9']
    ]);

    expect(collectedFiles(before, built)).toEqual({
      changed: ['UI/src/index.tsx'],
      judged: ['BlackoutLogSystem.cs', 'Mod.cs', 'UI/src/index.tsx']
    });
  });
});

describe('contamination', () => {
  const workspace = '/tmp/cs2-bench-own';

  function call(
    name: string,
    input: Record<string, unknown>,
    overrides: Partial<ToolCall> = {}
  ): ToolCall {
    return {
      name,
      input,
      isError: false,
      isDenied: false,
      resultUrls: [],
      isForked: false,
      ...overrides
    };
  }

  function findingsOf(calls: readonly ToolCall[]): readonly string[] {
    return findContamination({ toolCalls: calls, workspace, roots, machine });
  }

  const cleanRun = [
    call('Read', { file_path: '/tmp/cs2-bench-own/.claude/skills/cs2-modding/SKILL.md' }),
    call('Read', { file_path: '/data/cs2-decompile/src/Game/Game/UpdateSystem.cs' }),
    call('Read', { file_path: '/data/cs2-decompile/src-ui/source.js' }),
    call('Read', {
      file_path: '/home/modder/.claude/projects/-tmp-cs2-bench-own/abc/tool-results/b1.txt'
    }),
    call('Grep', { pattern: 'GetDay', path: 'BlackoutLog' }),
    call('Glob', { pattern: '**/*.cs' }),
    call('Write', { file_path: '/tmp/cs2-bench-own/Mod.cs', content: '' }),
    call('Bash', { command: 'cd /data/cs2-decompile/src && grep -rn GetDay . | head; ls /tmp' }),
    call('WebFetch', { url: 'https://cs2.paradoxwikis.com/Modding' }),
    call(
      'WebSearch',
      { query: 'cs2 update interval' },
      { resultUrls: ['https://github.com/someone/a-mod'] }
    ),
    call('Skill', { skill: 'cs2-modding' })
  ];

  test('a run that stayed in its workspace and roots is clean', () => {
    expect(findingsOf(cleanRun)).toEqual([]);
  });

  test.each([
    [
      'a read outside the workspace and roots',
      call('Read', { file_path: '/home/modder/notes/cs2.md' })
    ],
    [
      'a search of another folder',
      call('Grep', { pattern: 'IMod', path: '/home/modder/cs2-mods' })
    ],
    ['an absolute glob outside', call('Glob', { pattern: '/home/modder/cs2-mods/**/*.cs' })],
    [
      'a read of another workspace',
      call('Read', { file_path: '/tmp/cs2-bench-other/.claude/skills/cs2-modding/SKILL.md' })
    ],
    ['a shell read of the config directory', call('Bash', { command: 'cat ~/.claude/CLAUDE.md' })],
    [
      'a shell read of the setup record',
      call('Bash', { command: 'head "$HOME/.cs2-modding/setup.md"' })
    ],
    [
      'a shell read of the repository',
      call('Bash', { command: 'python3 -c "open(\'/home/modder/agents-plugins/AGENTS.md\')"' })
    ],
    [
      'a shell read of another workspace',
      call('Bash', { command: 'ls /tmp/cs2-bench-*/.claude/skills' })
    ],
    [
      'a shell fetch of the repository',
      call('Bash', { command: 'curl -s https://github.com/CitiesSkylinesModding/agents-plugins' })
    ],
    [
      'a fetch of the repository',
      call('WebFetch', {
        url: 'https://raw.githubusercontent.com/CitiesSkylinesModding/agents-plugins/main/README.md'
      })
    ],
    [
      'a search returning the repository',
      call(
        'WebSearch',
        { query: 'cs2 skills' },
        { resultUrls: ['https://github.com/citiesskylinesmodding/agents-plugins'] }
      )
    ],
    ['a skill that forked', call('Skill', { skill: 'simplify' }, { isForked: true })]
  ])('%s contaminates the run', (_name, leak) => {
    expect(findingsOf([...cleanRun, leak])).toHaveLength(1);
  });

  test('a call a deny rule refused leaked nothing', () => {
    const refused = [
      call(
        'Read',
        { file_path: '/home/modder/.claude/CLAUDE.md' },
        { isError: true, isDenied: true }
      ),
      call('Bash', { command: 'cat ~/.cs2-modding/setup.md' }, { isError: true, isDenied: true })
    ];

    expect(findingsOf([...cleanRun, ...refused])).toEqual([]);
  });
});

describe('skill use', () => {
  const workspace = '/tmp/cs2-bench-own';
  const skills = `${workspace}/.claude/skills`;

  function call(
    name: string,
    input: Record<string, unknown>,
    overrides: Partial<ToolCall> = {}
  ): ToolCall {
    return {
      name,
      input,
      isError: false,
      isDenied: false,
      resultUrls: [],
      isForked: false,
      ...overrides
    };
  }

  test('counts the cs2 skills a run loaded, never a bundled or a refused one', () => {
    const use = findSkillUse({
      workspace,
      toolCalls: [
        call('Skill', { skill: 'cs2-modding' }),
        call('Skill', { skill: 'dataviz' }),
        call('Skill', { skill: 'cs2-modding-ui' }, { isError: true, isDenied: true }),
        call('Skill', { skill: 'cs2-modding' })
      ]
    });

    expect(use.skillsLoaded).toEqual(['cs2-modding']);
  });

  test('lists the references read through the file tools and the shell', () => {
    const use = findSkillUse({
      workspace,
      toolCalls: [
        call('Read', {
          file_path: `${skills}/cs2-modding/references/technique/patching/patching.md`
        }),
        call(
          'Read',
          { file_path: `${skills}/cs2-modding/references/missing.md` },
          { isError: true }
        ),
        call('Bash', { command: `head -50 ${skills}/cs2-modding-ui/SKILL.md` }),
        call('Bash', {
          command: oneLine`
            cd ${skills}/cs2-modding/references;
            cat mechanics/simulation-time-and-units/cadence.md | head
          `
        }),
        call('Read', { file_path: '/data/cs2-decompile/README.md' })
      ]
    });

    expect(use.referencesRead).toEqual([
      'cs2-modding-ui/SKILL.md',
      'cs2-modding/references/mechanics/simulation-time-and-units/cadence.md',
      'cs2-modding/references/technique/patching/patching.md'
    ]);
  });

  test('resolves a relative reference read against the workspace, where the shell starts', () => {
    const use = findSkillUse({
      workspace,
      toolCalls: [call('Bash', { command: 'cat .claude/skills/cs2-modding/references/x.md' })]
    });

    expect(use.referencesRead).toEqual(['cs2-modding/references/x.md']);
  });

  test('never records a glob as a reference read', () => {
    const use = findSkillUse({
      workspace,
      toolCalls: [
        call('Bash', { command: 'grep -l Update .claude/skills/*/references/*.md' }),
        call('Bash', { command: `cat ${skills}/cs2-modding/references/{ecs,jobs}.md` }),
        call('Glob', { pattern: `${skills}/cs2-modding/references/**/*.md` })
      ]
    });

    expect(use.referencesRead).toEqual([]);
  });

  test('reads a workspace whose own path holds a glob character', () => {
    const bracketed = '/tmp/run[1]/cs2-bench-own';

    const use = findSkillUse({
      workspace: bracketed,
      toolCalls: [
        call('Read', { file_path: `${bracketed}/.claude/skills/cs2-modding/references/x.md` })
      ]
    });

    expect(use.referencesRead).toEqual(['cs2-modding/references/x.md']);
  });
});

describe('attempts', () => {
  const spend = { costUsd: 0.1, numTurns: 5, cliDurationMs: 9000, apiDurationMs: 6000 };
  const code: Attempt = { outcome: 'code', spend, wallMs: 10_000 };
  const noCode: Attempt = { outcome: 'no-code', spend, wallMs: 10_000 };
  const failed: Attempt = {
    outcome: 'failed',
    fault: 'the run hit its timeout',
    spend: undefined,
    wallMs: 20_000
  };

  test.each([
    ['code on the first attempt is judged', [code], { action: 'judge' }],
    ['no code is asked again', [noCode], { action: 'retry' }],
    ['a failure is retried', [failed], { action: 'retry' }],
    ['code after a stall and a failure is judged', [failed, noCode, code], { action: 'judge' }],
    [
      'no code at the cap is a no-code run',
      [noCode, noCode, noCode],
      { action: 'give-up', outcome: 'no-code' }
    ],
    [
      'the cap is shared with failures',
      [failed, failed, noCode],
      { action: 'give-up', outcome: 'no-code' }
    ],
    [
      'a failure at the cap is a failed run',
      [noCode, noCode, failed],
      { action: 'give-up', outcome: 'failed' }
    ]
  ] as const)('%s', (_name, attempts, next) => {
    expect(planNextAttempt(attempts, 3)).toEqual(next);
  });

  test('a job carries the cost, turns and time of every attempt it discarded', () => {
    const total = jobSpend([
      failed,
      { ...noCode, spend: { ...spend, costUsd: 0.25, apiDurationMs: 4000 } },
      code
    ]);

    expect(total.costUsd).toBeCloseTo(0.35);
    expect(total.numTurns).toBe(10);
    expect(total.durationMs).toBe(30_000);
    expect(total.durationSources).toEqual(['api', 'wall']);
  });

  test('a job no attempt priced stays unpriced rather than free', () => {
    expect(jobSpend([failed]).costUsd).toBeUndefined();
  });

  test('times an attempt by the CLI where the API reported no duration', () => {
    const total = jobSpend([{ ...code, spend: { ...spend, apiDurationMs: undefined } }]);

    expect(total.durationMs).toBe(9000);
    expect(total.durationSources).toEqual(['cli']);
  });
});

describe('judging', () => {
  const task = parseTask('a-slug', validTask);

  const judge = buildJudgePrompt({
    task,
    files: [
      {
        path: 'BlackoutLog/Mod.cs',
        content: 'updateSystem.UpdateAt<BlackoutLogSystem>(SystemUpdatePhase.UIUpdate);'
      },
      {
        path: 'BlackoutLog/BlackoutLogSystem.cs',
        content: 'public override int GetUpdateInterval(SystemUpdatePhase phase) => 10000;'
      }
    ],
    finalMessage: 'I wrote the mod in BlackoutLog/.'
  });

  test('the judge prompt carries the request, the checklist, the files and the message', () => {
    expect(judge).toContain(task.prompt);

    for (const [index, item] of task.checklist.entries()) {
      expect(judge).toContain(`ITEM ${index + 1}: ${item.name}`);
      expect(judge).toContain(item.fallsIn);
      expect(judge).toContain(item.avoids);
    }

    expect(judge).toContain('BlackoutLog/BlackoutLogSystem.cs');
    expect(judge).toContain('=> 10000;');
    expect(judge).toContain('I wrote the mod in BlackoutLog/.');
  });

  test('the judge prompt names no arm, no skill and no root', () => {
    for (const telltale of [
      'control',
      'treatment',
      'skill',
      'arm',
      ...roots.map(root => root.path)
    ]) {
      expect(judge.toLowerCase()).not.toMatch(new RegExp(`\\b${telltale.toLowerCase()}\\b`, 'u'));
    }
  });

  test('reads a pass, fail or not-applicable verdict per item, each with its quoted line', () => {
    const reply = [
      'Some reasoning first.',
      '',
      'ITEM 1: FAIL | `public override int GetUpdateInterval(SystemUpdatePhase phase) => 10000;`',
      'ITEM 2: PASS | updateSystem.UpdateAt<BlackoutLogSystem>(SystemUpdatePhase.GameSimulation);',
      'ITEM 3: N/A | (none)'
    ].join('\n');

    expect(parseJudgeReply(reply, task.checklist)).toEqual([
      {
        item: 'The interval is a power of two',
        verdict: 'fail',
        evidence: '`public override int GetUpdateInterval(SystemUpdatePhase phase) => 10000;`'
      },
      {
        item: 'The system runs in a phase that honours its interval',
        verdict: 'pass',
        evidence: 'updateSystem.UpdateAt<BlackoutLogSystem>(SystemUpdatePhase.GameSimulation);'
      },
      {
        item: 'The day boundary is not assumed to fall on offset zero',
        verdict: 'n/a',
        evidence: '(none)'
      }
    ]);
  });

  test.each([
    ['emphasised', '**ITEM 1: PASS | a**\n**ITEM 2: FAIL | b**\n**ITEM 3: NOT APPLICABLE | c**'],
    ['lowercased', 'item 1: pass | a\nitem 2: fail | b\nitem 3: na | c'],
    ['reconsidered', 'ITEM 1: FAIL | z\nITEM 1: PASS | a\nITEM 2: FAIL | b\nITEM 3: N/A | c']
  ])('reads a %s reply rather than retrying its formatting', (_name, reply) => {
    expect(
      parseJudgeReply(reply, task.checklist).map(({ verdict, evidence }) => ({ verdict, evidence }))
    ).toEqual([
      { verdict: 'pass', evidence: 'a' },
      { verdict: 'fail', evidence: 'b' },
      { verdict: 'n/a', evidence: 'c' }
    ]);
  });

  test.each([
    ['a reply missing an item', 'ITEM 1: PASS | a\nITEM 3: PASS | c', /item 2/iu],
    [
      'a reply ruling an item the checklist lacks',
      'ITEM 1: PASS | a\nITEM 2: PASS | b\nITEM 3: PASS | c\nITEM 4: FAIL | d',
      /item 4/iu
    ],
    [
      'a verdict outside the three',
      'ITEM 1: PASS | a\nITEM 2: PARTIAL | b\nITEM 3: PASS | c',
      /item 2/iu
    ],
    ['a reply with no verdict at all', 'The code looks fine to me.', /item 1/iu]
  ])('rejects %s', (_name, reply, fault) => {
    expect(() => parseJudgeReply(reply, task.checklist)).toThrow(fault);
  });
});

describe('calibration', () => {
  const rulings = (...verdicts: readonly Verdict[]): readonly ItemRuling[] =>
    verdicts.map((verdict, index) => ({
      item: ['Interval', 'Phase', 'Day'][index] ?? 'Extra',
      verdict,
      evidence: 'a line'
    }));

  test('passes a task whose items all fail on the falling sample and pass on the other', () => {
    expect(
      calibrationVerdict({
        falls: rulings('fail', 'fail', 'fail'),
        avoids: rulings('pass', 'pass', 'pass')
      })
    ).toEqual({ passes: true, misses: [] });
  });

  test.each([
    [
      'an item the falling sample passes',
      rulings('fail', 'pass', 'fail'),
      rulings('pass', 'pass', 'pass'),
      'Phase: the falling sample was ruled pass'
    ],
    [
      'an item the avoiding sample fails',
      rulings('fail', 'fail', 'fail'),
      rulings('pass', 'pass', 'fail'),
      'Day: the avoiding sample was ruled fail'
    ],
    [
      'a not-applicable item on the falling sample',
      rulings('n/a', 'fail', 'fail'),
      rulings('pass', 'pass', 'pass'),
      'Interval: the falling sample was ruled n/a'
    ],
    [
      'a not-applicable item on the avoiding sample',
      rulings('fail', 'fail', 'fail'),
      rulings('pass', 'n/a', 'pass'),
      'Phase: the avoiding sample was ruled n/a'
    ]
  ] as const)('fails a task with %s', (_name, falls, avoids, miss) => {
    expect(calibrationVerdict({ falls, avoids })).toEqual({ passes: false, misses: [miss] });
  });
});

describe('resume and the task budget', () => {
  const tasks = ['first', 'second', 'third'];

  const keysOf = (planned: readonly JobKey[]): string[] =>
    planned.map(key => `${key.task} ${key.arm} ${key.run}`);

  test('a fresh run plans every job of every task, task by task', () => {
    expect(keysOf(planJobs({ tasks: ['first', 'second'], runs: 2, records: [] }))).toEqual([
      'first control 1',
      'first control 2',
      'first treatment 1',
      'first treatment 2',
      'second control 1',
      'second control 2',
      'second treatment 1',
      'second treatment 2'
    ]);
  });

  test('a resume skips the jobs with a result on disk and reruns a failed one', () => {
    const records = [
      job({ task: 'first', arm: 'control', run: 1 }),
      job({ task: 'first', arm: 'treatment', run: 1, outcome: 'no-code' }),
      job({ task: 'first', arm: 'treatment', run: 2, outcome: 'failed' })
    ];

    expect(keysOf(planJobs({ tasks: ['first'], runs: 2, records }))).toEqual([
      'first control 2',
      'first treatment 2'
    ]);
  });

  test('a budget takes whole tasks, each in both arms for every run', () => {
    expect(keysOf(planJobs({ tasks, runs: 1, records: [], budget: 2 }))).toEqual([
      'first control 1',
      'first treatment 1',
      'second control 1',
      'second treatment 1'
    ]);
  });

  test('a budget spends nothing on a finished task and a whole slot on a started one', () => {
    const records = [
      job({ task: 'first', arm: 'control', run: 1 }),
      job({ task: 'first', arm: 'treatment', run: 1 }),
      job({ task: 'second', arm: 'control', run: 1 })
    ];

    expect(keysOf(planJobs({ tasks, runs: 1, records, budget: 1 }))).toEqual([
      'second treatment 1'
    ]);
  });
});

describe('aggregation and the report', () => {
  const cadence = 'cs2-modding/references/mechanics/simulation-time-and-units/cadence.md';
  const leak = 'Read read /home/modder/notes.md, outside the workspace and roots';
  const brokenLog = { task: 'broken-log', kind: 'bug report' } as const;
  const partialLog = { task: 'partial-log' } as const;

  const settings: RunSettings = {
    startedAt: '2026-10-06T00:00:00.000Z',
    model: 'sonnet',
    effort: 'low',
    judgeModel: 'sonnet',
    runs: 2,
    tasks: ['daily-log', 'broken-log', 'partial-log', 'unstarted-log']
  };

  const jobs = [
    job({ arm: 'control', run: 1, verdicts: ['pass', 'fail', 'n/a'] }),
    job({ arm: 'control', run: 2, verdicts: ['pass', 'pass', 'fail'] }),
    job({ arm: 'treatment', run: 1, skillsLoaded: ['cs2-modding'], referencesRead: [cadence] }),
    job({
      arm: 'treatment',
      run: 2,
      verdicts: ['n/a', 'n/a', 'n/a'],
      skillsLoaded: ['cs2-modding']
    }),
    job({ ...brokenLog, arm: 'control', run: 1, contamination: [leak] }),
    job({ ...brokenLog, arm: 'control', run: 2, outcome: 'no-code' }),
    job({ ...brokenLog, arm: 'treatment', run: 1, verdicts: ['fail', 'pass', 'pass'] }),
    job({ ...brokenLog, arm: 'treatment', run: 2, outcome: 'no-code' }),
    // An interruption left this task with a failed job and one never run: if its two judged runs
    // were scored, the control's would sink its pass rate and the treatment's would lift it.
    job({ ...partialLog, arm: 'control', run: 1, verdicts: ['fail', 'fail', 'fail'] }),
    job({ ...partialLog, arm: 'control', run: 2, outcome: 'failed' }),
    job({ ...partialLog, arm: 'treatment', run: 1, skillsLoaded: ['cs2-modding'] })
  ];

  const summary = buildSummary({ settings, jobs });

  function armOf(arm: ArmName): ArmSummary {
    const found = summary.arms.find(candidate => candidate.arm == arm);

    assert.ok(found, `the summary holds the ${arm} arm`);

    return found;
  }

  test('the pass rate pools applicable items over the clean judged runs only', () => {
    expect(armOf('control').tally).toEqual({ passed: 3, applicable: 5 });
    expect(armOf('control').contaminated).toBe(1);
    expect(armOf('control').noCode).toBe(1);
  });

  test('the per-arm row counts the jobs of finished tasks only', () => {
    // Counting the unfinished task's judged control job would move its pass rate, runs and share.
    expect(renderReport(summary)).toContain('| control | 60% (3/5) | 2 | 0/4 | 1 | 1 | 0.2');
  });

  test('a not-applicable item counts neither way', () => {
    expect(armOf('treatment').tally).toEqual({ passed: 5, applicable: 6 });
    expect(
      summary.tasks.find(entry => entry.task == 'daily-log' && entry.arm == 'treatment')?.tally
    ).toEqual({
      passed: 3,
      applicable: 3
    });
  });

  test('splits the tally by task kind', () => {
    expect(summary.kinds).toEqual([
      { kind: 'feature request', arm: 'control', tally: { passed: 3, applicable: 5 } },
      { kind: 'feature request', arm: 'treatment', tally: { passed: 3, applicable: 3 } },
      { kind: 'bug report', arm: 'treatment', tally: { passed: 2, applicable: 3 } }
    ]);
  });

  test('counts each item per arm over the clean judged runs', () => {
    const day = summary.items.find(entry => entry.task == 'daily-log' && entry.item == 'Day');

    expect(day?.counts).toEqual([
      { arm: 'control', pass: 0, fail: 1, notApplicable: 1 },
      { arm: 'treatment', pass: 1, fail: 0, notApplicable: 1 }
    ]);
  });

  test('reports how many runs loaded a cs2 skill and which references they read', () => {
    expect(armOf('treatment').skillLoaded).toEqual({ runs: 2, of: 4 });
    expect(armOf('control').skillLoaded).toEqual({ runs: 0, of: 4 });
    expect(summary.references).toEqual([{ reference: cadence, runs: 1 }]);
  });

  test('keeps a task an interruption left incomplete out of every score, and lists it', () => {
    expect(summary.unfinished).toEqual([
      { task: 'partial-log', finished: 2, of: 4 },
      { task: 'unstarted-log', finished: 0, of: 4 }
    ]);
    expect(summary.tasks.map(entry => entry.task)).not.toContain('partial-log');
    expect(summary.items.map(entry => entry.task)).not.toContain('partial-log');
  });

  test('names every skills commit the run spanned, and whether any job ran on a dirty tree', () => {
    const clean = { commit: 'aaaa111', isDirty: false };
    const later = { commit: 'bbbb222', isDirty: false };

    const spanned = (skills: ReadonlyArray<JobRecord['skills']>): BenchSummary['skills'] =>
      buildSummary({
        settings: { ...settings, runs: skills.length, tasks: ['daily-log'] },
        jobs: skills.flatMap((version, index) =>
          armNames.map(arm => job({ arm, run: index + 1, skills: version }))
        )
      }).skills;

    expect(spanned([clean, later, clean])).toEqual({
      commits: ['aaaa111', 'bbbb222'],
      isDirty: false
    });
    expect(spanned([clean, { ...later, isDirty: true }])).toEqual({
      commits: ['aaaa111', 'bbbb222'],
      isDirty: true
    });
  });

  test('renders the arms, the splits and every run left out of the scores', () => {
    const report = renderReport(summary);

    expect(report).toContain('| control | 60% (3/5) |');
    expect(report).toContain('| treatment | 83% (5/6) |');
    expect(report).toContain('| bug report | – | 67% (2/3) |');
    expect(report).toContain('| daily-log | Day | 0/1/1 | 1/0/1 |');
    expect(report).toContain(`| broken-log | control | 1 | ${leak} |`);
    expect(report).toContain('## No-code runs');
    expect(report).toContain('| partial-log | control | 2 | the run hit its timeout |');
    expect(report).toContain(`| ${cadence} | 1 |`);
    expect(report).toContain('| treatment | 83% (5/6) | 3 | 2/4 | 0 | 1 | 0.2000 |');
    expect(report).toContain('| partial-log | 2/4 |');
    expect(report).toContain('| unstarted-log | 0/4 |');
    expect(report).not.toContain('concurrency distorts');
  });

  describe('the promoted results file', () => {
    const finished = buildSummary({
      settings: { ...settings, tasks: ['daily-log', 'broken-log'] },
      jobs: jobs.filter(entry => entry.task != 'partial-log')
    });

    test('carries the date, the models, the skills commits and the summary tables', () => {
      const results = renderResults(finished);

      expect(results).toContain('2026-10-06');
      expect(results).toContain('sonnet at low effort');
      expect(results).toContain('aaaa111');
      expect(results).toContain('| control | 60% (3/5) |');
      expect(results).toContain('| daily-log | Day | 0/1/1 | 1/0/1 |');
      expect(results).not.toContain('uncommitted');
    });

    test('marks a run any job of which ran on uncommitted skills', () => {
      const [first, ...rest] = finished.jobs;

      assert.ok(first, 'the finished run holds jobs');

      const dirty = buildSummary({
        settings: finished.settings,
        jobs: [{ ...first, skills: { commit: 'aaaa111', isDirty: true } }, ...rest]
      });

      expect(renderResults(dirty)).toContain('uncommitted');
    });

    test('leaves out the per-run detail and its machine-local findings', () => {
      const results = renderResults(finished);

      expect(results).toContain('## Per arm');
      expect(results).not.toContain('## Runs');
      expect(results).not.toContain(leak);
    });

    test('refuses a run with an unfinished task', () => {
      expect(() => renderResults(summary)).toThrow(/partial-log/u);
    });
  });

  test('reports an unpriced arm as unpriced and a mixed clock as distorted', () => {
    const unpriced: JobRecord = {
      ...job({ arm: 'control', run: 1 }),
      spend: { costUsd: undefined, numTurns: 10, durationMs: 60_000, durationSources: ['cli'] }
    };

    // The treatment arm is priced, so only the control row can say its cost went unreported.
    const priced = job({ arm: 'treatment', run: 1 });

    const report = renderReport(
      buildSummary({
        settings: { ...settings, runs: 1, tasks: ['daily-log'] },
        jobs: [unpriced, priced]
      })
    );

    expect(report).toContain('(none reported)');
    expect(report).toContain('concurrency distorts');
  });
});

/**
 * A job record with every field the aggregation does not read set to a neutral value.
 */
function job(overrides: {
  task?: string;
  kind?: TaskKind;
  arm: ArmName;
  run: number;
  outcome?: JobRecord['outcome'];
  verdicts?: readonly Verdict[];
  contamination?: readonly string[];
  skillsLoaded?: readonly string[];
  referencesRead?: readonly string[];
  costUsd?: number;
  skills?: JobRecord['skills'];
}): JobRecord {
  const outcome = overrides.outcome ?? 'judged';

  const base = {
    task: overrides.task ?? 'daily-log',
    kind: overrides.kind ?? 'feature request',
    arm: overrides.arm,
    run: overrides.run,
    skills: overrides.skills ?? { commit: 'aaaa111', isDirty: false },
    attempts: [],
    spend: {
      costUsd: overrides.costUsd ?? 0.2,
      numTurns: 10,
      durationMs: 60_000,
      durationSources: ['api']
    },
    contamination: overrides.contamination ?? [],
    skillUse: {
      skillsLoaded: overrides.skillsLoaded ?? [],
      referencesRead: overrides.referencesRead ?? []
    },
    files: outcome == 'judged' ? ['Mod.cs'] : [],
    finalMessage: '',
    toolCalls: []
  } as const;

  if (outcome == 'failed') {
    return { ...base, outcome, fault: 'the run hit its timeout' };
  }

  if (outcome == 'no-code') {
    return { ...base, outcome };
  }

  return {
    ...base,
    outcome,
    verdicts: (overrides.verdicts ?? ['pass', 'pass', 'pass']).map((verdict, index) => ({
      item: ['Interval', 'Phase', 'Day'][index] ?? 'Extra',
      verdict,
      evidence: 'a line'
    }))
  };
}

/**
 * Writes a task folder to a temp directory, removed once the file's tests are done.
 * Each named folder gets one file, except a name prefixed with `!`, which is left empty.
 */
function scratchTask(text: string, folders: readonly string[]): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'cs2-bench-test-'));

  scratchDirs.push(dir);

  fs.writeFileSync(path.join(dir, 'task.md'), text);

  for (const folder of folders) {
    const name = folder.replace(/^!/u, '');

    fs.rmSync(path.join(dir, name), { recursive: true, force: true });
    fs.mkdirSync(path.join(dir, name));

    if (!folder.startsWith('!')) {
      fs.writeFileSync(path.join(dir, name, 'Mod.cs'), '// sample');
    }
  }

  return dir;
}

/**
 * Names the fault a malformed task raises, so a test asserts the code and not the wording.
 */
function faultOf(load: () => unknown): string {
  try {
    load();
  } catch (error) {
    return error instanceof TaskFault ? error.fault : `unexpected error: ${String(error)}`;
  }

  return 'no fault raised';
}
