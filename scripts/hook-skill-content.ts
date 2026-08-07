// oxlint-disable unicorn/no-process-exit -- the hook protocol communicates via exit codes.
// oxlint-disable node/no-sync -- a hook is a one-shot process whose whole job is to block the edit.

// A PostToolUse hook running the shipped-prose lint on the edit that could break it, rather than on
// the commit that would have shipped it. `mise check:skill-content` and the pre-commit run the same
// rules over the same tree; what this adds is when. A warning block, a link, a marker or a mod name
// broken here is reported while the author still holds the reason for the edit, which is the
// difference between a fix and a finding three review rounds later.
//
// The lint is spawned rather than imported, so this hook and the check stay one implementation: a
// rule added there is enforced here for free, and the message an author reads is the one they would
// have read from `mise check`.
//
// Exit 2 is the only code that blocks a tool call, so a bug in this hook lets the edit through
// instead of wedging every write under the skills tree.

import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';

const repoRoot = path.resolve(import.meta.dirname, '..');
const lintPath = path.join(repoRoot, 'scripts', 'check-skill-content.ts');

// The trunk plugin's skills tree is the whole of what the lint judges, so an edit anywhere else
// cannot change its verdict and must not pay for a run.
const skillsRoot = path.join(repoRoot, 'plugins', 'cs2-modding', 'skills');

run();

function run(): void {
  const filePath = filePathOf(readPayload());

  if (filePath == null || !isUnderSkillsRoot(filePath)) {
    return;
  }

  const result = spawnSync('bun', [lintPath], { cwd: repoRoot, encoding: 'utf8' });

  if (result.status == 0) {
    return;
  }

  process.stderr.write(
    `check-skill-content rejected this edit.\n\n${failureOf(result.stdout, result.stderr)}\n\n` +
      `The same rules run in \`mise check:skill-content\`, in the pre-commit and in CI, so this ` +
      `blocks the commit either way. \`plugins/cs2-modding/AGENTS.md\` carries the contract.\n`
  );

  process.exit(2);
}

// The assertion message alone where there is one: node prints it above a stack that says only where
// the rule lives, while the rule's own message already names the file and what it wanted. The whole
// output is the fallback, since a lint that failed some other way has nothing else to hand back.
function failureOf(stdout: string | null, stderr: string | null): string {
  const output = `${stdout ?? ''}${stderr ?? ''}`;
  const assertion = /^AssertionError.*$/mu.exec(output)?.[0];

  return (assertion ?? output).trim();
}

// Containment rather than a substring match, so a path that merely mentions the directory does not
// pass and a relative payload path is resolved before it is judged.
function isUnderSkillsRoot(filePath: string): boolean {
  const relative = path.relative(skillsRoot, path.resolve(filePath));

  return relative.length > 0 && !relative.startsWith('..') && !path.isAbsolute(relative);
}

// The hook payload is JSON on stdin. A harness that invokes the hook with no stdin, or with
// something that is not JSON, gets no check rather than a crash: the hook has nothing to say about
// a file it cannot identify.
function readPayload(): unknown {
  try {
    return JSON.parse(readFileSync(0, 'utf8'));
  } catch {
    return null;
  }
}

function filePathOf(payload: unknown): string | null {
  if (payload == null || typeof payload != 'object') {
    return null;
  }

  const toolInput = (payload as Record<string, unknown>).tool_input;

  if (toolInput == null || typeof toolInput != 'object') {
    return null;
  }

  const filePath = (toolInput as Record<string, unknown>).file_path;

  return typeof filePath == 'string' ? filePath : null;
}
