/* oxlint-disable node/no-sync -- sequential one-shot script, synchronous IO is intentional. */
/* oxlint-disable no-console -- the console is this script's report on what it fetched. */

import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

// Refreshes the vendored Mono.Debugger.Soft sources from Unity's mono fork, applying the local
// patches below on the way in.
//
// The sources are committed rather than submoduled, so this is the only thing that writes them:
// vendor/mono-debugger-soft/VENDOR.md states why, and is itself rewritten here with what was
// fetched. Restoring the committed state needs no task at all, being a plain git checkout.
//
// Usage: bun scripts/update-vendored-sdb.ts [ref]
// With no ref, re-fetches the tip of the branch VENDOR.md records; a ref (branch, tag or commit)
// moves the pin there.

const upstreamUrl = 'https://github.com/Unity-Technologies/mono';
const upstreamPath = 'mcs/class/Mono.Debugger.Soft/Mono.Debugger.Soft';

// Connection.cs is the one file not taken verbatim, so that what the build compiles is what the
// repository holds and no build step has to reproduce it. VENDOR.md's "Local patches" section is
// the record a reader of the vendored tree finds; keep the two in step.
const connectionPatches = [
  {
    // The client dispatches an invoke reply through BeginInvoke, which throws on modern .NET.
    anchor: 'cb.BeginInvoke (r, null, null)',
    replacement: 'System.Threading.Tasks.Task.Run (() => cb (r))'
  },
  {
    // The receiver thread reports a failed receive on stdout, which in the MCP server carries
    // JSON-RPC and nothing else: a debuggee dying abruptly would write a stack trace into the
    // protocol stream and break the session. The diagnostic is worth keeping, so it moves to
    // stderr rather than being suppressed.
    anchor: 'Console.WriteLine (ex);',
    replacement: 'Console.Error.WriteLine (ex);'
  }
] as const;

const repoRoot = path.resolve(import.meta.dirname, '..');
const vendorDir = path.join(repoRoot, 'plugins', 'unity-devtools', 'vendor', 'mono-debugger-soft');
const vendorDoc = path.join(vendorDir, 'VENDOR.md');

await update();

async function update(): Promise<void> {
  const doc = await Bun.file(vendorDoc).text();
  const ref = process.argv[2] ?? tableValue(doc, 'Branch');

  // A cone-mode sparse checkout set before the first checkout is what keeps this survivable on
  // Windows: mono's full tree carries paths past the 260-character limit, and materializing it
  // fails outright there. Nothing outside upstreamPath is ever written to disk.
  const clone = fs.mkdtempSync(path.join(os.tmpdir(), 'unity-mono-'));

  try {
    console.log(`Fetching ${upstreamPath} at ${ref}…`);

    git(['clone', '--filter=blob:none', '--no-checkout', '--depth', '1', upstreamUrl, clone]);
    git(['-C', clone, 'sparse-checkout', 'set', upstreamPath]);
    git(['-C', clone, 'fetch', '--depth', '1', 'origin', ref]);
    git(['-C', clone, 'checkout', 'FETCH_HEAD']);

    const commit = git(['-C', clone, 'rev-parse', 'FETCH_HEAD']).trim();

    // Pinned before anything is written, so an unwritable table joins a moved patch anchor as a
    // failure that leaves the vendored tree exactly as it was.
    const pinned = withPin(doc, ref, commit);
    const before = sourceNames(vendorDir);

    await copySources(path.join(clone, upstreamPath), vendorDir);

    await Bun.write(vendorDoc, pinned);
    report(before, sourceNames(vendorDir), commit);
  } finally {
    // The clone holds a git object store whose files are read-only on Windows, which a plain
    // recursive delete refuses; force covers that. A cleanup that fails anyway must neither fail
    // the run nor mask the error that reached this block, so it degrades to a warning naming the
    // directory left behind.
    try {
      fs.rmSync(clone, { recursive: true, force: true, maxRetries: 3 });
    } catch (error) {
      console.warn(`Could not remove the temporary clone at ${clone}: ${String(error)}`);
    }
  }
}

async function copySources(from: string, to: string): Promise<void> {
  // Everything is read, normalized and patched before anything is written, so a patch anchor that
  // upstream has moved leaves the existing tree untouched rather than half-updated.
  const sources = await Promise.all(
    sourceNames(from).map(async name => ({
      name,
      content: patchConnection(name, normalizeEol(await Bun.file(path.join(from, name)).text()))
    }))
  );

  for (const name of sourceNames(to)) {
    fs.rmSync(path.join(to, name));
  }

  await Promise.all(sources.map(source => Bun.write(path.join(to, source.name), source.content)));
}

// Upstream is checked out with whatever line endings the platform gives it, while this repository
// mandates LF (.gitattributes). Only CRLF pairs are rewritten, so a lone CR inside a file survives
// as the byte it was. The text round trip is byte-exact for any valid UTF-8, which these are.
function normalizeEol(content: string): string {
  return content.replaceAll('\r\n', '\n');
}

function patchConnection(name: string, content: string): string {
  if (name != 'Connection.cs') {
    return content;
  }

  let patched = content;

  for (const { anchor, replacement } of connectionPatches) {
    if (!patched.includes(anchor)) {
      throw new Error(`Connection.cs no longer carries the patch anchor "${anchor}".`);
    }

    patched = patched.replaceAll(anchor, replacement);
  }

  return patched;
}

function sourceNames(dir: string): readonly string[] {
  return fs
    .readdirSync(dir)
    .filter(name => name.endsWith('.cs'))
    .toSorted();
}

function withPin(doc: string, ref: string, commit: string): string {
  // A commit-shaped ref pins both rows to the same value, which is the honest record of it: the
  // branch that commit sits on is not something the fetch can tell us.
  const branch = /^[0-9a-f]{7,40}$/u.test(ref) ? commit : ref;

  return withRow(withRow(doc, 'Branch', branch), 'Commit', commit);
}

function withRow(doc: string, row: string, value: string): string {
  const pattern = rowPattern(row);

  // The pattern is tested rather than the result compared, since rewriting a row to the value it
  // already holds is a normal outcome (a re-run at the same commit) and leaves the doc identical.
  if (!pattern.test(doc)) {
    throw new Error(`VENDOR.md has no "${row}" row to write the new pin into.`);
  }

  return doc.replace(pattern, `| ${row} | \`${value}\` |`);
}

function tableValue(doc: string, row: string): string {
  const match = rowPattern(row).exec(doc);

  if (match?.groups?.value == null) {
    throw new Error(`VENDOR.md has no "${row}" row to read the current pin from.`);
  }

  return match.groups.value;
}

// One pattern for both directions, so a table shape that cannot be read cannot be written either.
function rowPattern(row: string): RegExp {
  return new RegExp(`^\\| ${row} \\| \`(?<value>.+?)\` \\|$`, 'mu');
}

function report(before: readonly string[], after: readonly string[], commit: string): void {
  const added = after.filter(name => !before.includes(name));
  const removed = before.filter(name => !after.includes(name));

  console.log(`Vendored ${after.length} sources at ${commit}.`);

  if (added.length) {
    console.log(`Added: ${added.join(', ')}`);
  }

  if (removed.length) {
    console.log(`Removed: ${removed.join(', ')}`);
  }

  const diffPath = path.relative(repoRoot, vendorDir);

  console.log(`Applied ${connectionPatches.length} local patches to Connection.cs.`);
  console.log(`Review "git diff ${diffPath}", then build and test before committing.`);
}

// Bun.spawnSync does not throw on a nonzero exit the way node's execFileSync did, so the check is
// explicit; stderr inherits so git's own progress and error text reach the caller unfiltered.
function git(args: readonly string[]): string {
  const result = Bun.spawnSync(['git', ...args], {
    stdin: 'ignore',
    stdout: 'pipe',
    stderr: 'inherit'
  });

  if (!result.success) {
    throw new Error(`"git ${args.join(' ')}" exited with code ${result.exitCode}.`);
  }

  return result.stdout.toString();
}
