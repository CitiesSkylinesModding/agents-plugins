/* oxlint-disable node/no-sync -- sequential check script, synchronous IO is intentional. */
/* oxlint-disable no-console -- the console carries the report. */

import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { type Claim, checkClaims, type ClaimState } from './core';

// Names each `[symbol: <type>]` claim of the cs2-modding skills and server source as changed,
// missing, unchecked or clean between two commits of a local decompile.

const { values: options, positionals } = parseArgs({
  allowPositionals: true,
  options: {
    base: { type: 'string' },
    target: { type: 'string', default: 'HEAD' }
  }
});

const [decompileRoot = ''] = positionals;

assert.ok(decompileRoot, `Usage: <decompile root> [--base <commit>] [--target <commit>]`);

const base =
  options.base ?? readFileSync(path.join(import.meta.dirname, 'baseline'), 'utf8').trim();
const { target } = options;

// The decompile's file list runs past execFileSync's default buffer.
// oxlint-disable-next-line no-magic-numbers
const GIT_OUTPUT_LIMIT = 64 * 1024 * 1024;

const repoRoot = path.join(import.meta.dirname, '../..');

// Bun's glob matches nothing for a brace group spanning a `/`, hence one pattern per folder.
const scanned = ['plugins/cs2-modding/skills/**/*.md', 'plugins/cs2-modding/mcp/src/**/*.ts']
  .flatMap(pattern => Array.from(new Bun.Glob(pattern).scanSync(repoRoot)))
  .map(file => ({ path: file, text: readFileSync(path.join(repoRoot, file), 'utf8') }));

const claims = checkClaims({
  scanned,
  decompileFiles: git('ls-tree', '-r', '--name-only', target, '--', 'src'),
  changedFiles: new Set(git('diff', '--name-only', '--no-renames', base, target, '--', 'src')),
  findDeclarations
});

const stateOrder: readonly ClaimState[] = ['changed', 'missing', 'unchecked', 'clean'];
const stateWidth = Math.max(...stateOrder.map(state => state.length));

console.log(`Trap check, decompile ${base}..${target}:`);

for (const claim of claims.toSorted(byState)) {
  const state = claim.state.padEnd(stateWidth);
  const where = `${claim.file}:${claim.line}`;

  console.log(`${state} ${where} ${claim.symbol} ${claim.files.join(' ')}`);
}

console.log(
  stateOrder
    .map(state => `${claims.filter(claim => claim.state == state).length} ${state}`)
    .join(', ')
);

function byState(a: Claim, b: Claim): number {
  return stateOrder.indexOf(a.state) - stateOrder.indexOf(b.state);
}

function git(...args: readonly string[]): string[] {
  return execFileSync('git', ['-C', decompileRoot, ...args], {
    encoding: 'utf8',
    maxBuffer: GIT_OUTPUT_LIMIT
  })
    .split('\n')
    .filter(Boolean);
}

// `git grep` exits 1 on no match, which is an answer here and not a failure.
function findDeclarations(type: string): string[] {
  const keyword = 'class|struct|interface|enum|record|delegate';
  // Escaped so a malformed marker reads as missing rather than failing the grep.
  const name = type.replaceAll(/[\\^$.|?*+()[\]{}]/gu, String.raw`\$&`);
  // `,` keeps out constraints like `where T : class, IComponentData`.
  const pattern = String.raw`\b(${keyword})\b[^;{(=:<,]*\b${name}\b`;
  const result = spawnSync(
    'git',
    ['-C', decompileRoot, 'grep', '-l', '-P', pattern, target, '--', 'src'],
    {
      encoding: 'utf8'
    }
  );

  assert.ok(result.status == 0 || result.status == 1, result.stderr);

  return result.stdout
    .split('\n')
    .filter(Boolean)
    .map(line => line.slice(target.length + 1));
}
