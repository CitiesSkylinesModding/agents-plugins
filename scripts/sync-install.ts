/* oxlint-disable node/no-sync -- sequential copy script, synchronous IO is intentional. */
/* oxlint-disable eslint/no-console -- reporting each mirror is this script's purpose. */

import { execFileSync } from 'node:child_process';
import {
  cpSync,
  existsSync,
  lstatSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  rmSync
} from 'node:fs';
import { homedir } from 'node:os';
import path from 'node:path';

// Mirrors this working tree over the installed copies of the marketplace's plugins (Claude Code and
// Codex CLI), so unreleased edits are dogfooded from other projects.
// A plugin update only refreshes an install when the marketplace advertises a higher version.
// An install holds a plugin's tracked files; the mirror copies them plus the untracked files git
// does not ignore, so a new file is dogfooded too while build output and node_modules stay out.
// Usage: `bun scripts/sync-install.ts [plugin...]` (defaults to every installed plugin).

const MARKETPLACE = 'csmodding';

/**
 * The entry Claude Code keeps in an install for each session using it, which the mirror keeps so
 * that no running session's install reads as unused.
 */
const IN_USE_ENTRY = '.in_use';

interface Target {
  readonly plugin: string;
  readonly harness: string;
  readonly installPath: string;
}

const repoRoot = path.resolve(import.meta.dirname, '..');

const plugins = readdirSync(path.join(repoRoot, 'plugins'), { withFileTypes: true })
  .filter(entry => entry.isDirectory())
  .map(entry => entry.name);

const requested = process.argv.slice(2);
const unknown = requested.filter(name => !plugins.includes(name));

if (unknown.length > 0) {
  console.error(`No plugin named ${unknown.join(', ')}; the plugins are ${plugins.join(', ')}.`);
  process.exitCode = 1;
} else {
  syncAll(requested.length > 0 ? requested : plugins);
}

function syncAll(names: readonly string[]): void {
  const targets = names.flatMap(name => findInstalls(name));

  if (targets.length == 0) {
    console.error(`None of ${names.join(', ')} is installed in either harness.`);
    process.exitCode = 1;

    return;
  }

  // Every target is checked before any is touched: a refusal raised halfway would leave the
  // installs already mirrored holding a tree the others do not.
  for (const target of targets) {
    const refusal = refuse(target.installPath);

    if (refusal) {
      console.error(`Refusing to overwrite ${target.installPath}: ${refusal}`);
      process.exitCode = 1;

      return;
    }
  }

  for (const target of targets) {
    mirror(target);
  }

  console.log('Run /reload-plugins in a live Claude Code session, or start a new one, to load it.');

  if (targets.some(target => target.plugin == 'unity-devtools')) {
    console.log("unity-devtools' server still runs the NuGet release its .mcp.json pins.");
  }
}

function findInstalls(plugin: string): Target[] {
  const targets: Target[] = [];
  const registryPath = path.join(homedir(), '.claude', 'plugins', 'installed_plugins.json');

  // Claude Code records the exact install path, so read it rather than guessing the version.
  if (existsSync(registryPath)) {
    const registry = JSON.parse(readFileSync(registryPath, 'utf8')) as {
      plugins?: Record<string, ReadonlyArray<{ installPath?: string }>>;
    };

    const installPath = registry.plugins?.[`${plugin}@${MARKETPLACE}`]?.[0]?.installPath;

    if (installPath) {
      targets.push({ plugin, harness: 'Claude Code', installPath });
    }
  }

  // Codex records installs without their version, so the highest cached version wins.
  const codexCache = path.join(homedir(), '.codex', 'plugins', 'cache', MARKETPLACE, plugin);

  if (existsSync(codexCache)) {
    const [version] = readdirSync(codexCache).toSorted((a, b) =>
      b.localeCompare(a, undefined, { numeric: true })
    );

    if (version) {
      targets.push({ plugin, harness: 'Codex CLI', installPath: path.join(codexCache, version) });
    }
  }

  return targets;
}

/**
 * Why the mirror must not empty this path, undefined when it may.
 */
function refuse(installPath: string): string | undefined {
  if (!installPath.includes(`${path.sep}plugins${path.sep}cache${path.sep}${MARKETPLACE}`)) {
    return 'not a plugin cache directory.';
  }

  // Both harnesses install by copying; emptying a link would reach through it.
  if (existsSync(installPath) && lstatSync(installPath).isSymbolicLink()) {
    return 'it is a link, not a copied install.';
  }

  if (path.resolve(installPath).startsWith(repoRoot + path.sep)) {
    return 'it is inside this checkout.';
  }

  return undefined;
}

function mirror(target: Target): void {
  const source = path.join(repoRoot, 'plugins', target.plugin);
  const installedVersion = path.basename(target.installPath);
  const treeVersion = readVersion(source);

  if (installedVersion != treeVersion) {
    console.log(
      `${target.harness}: ${target.plugin} is installed at ${installedVersion}, the tree is at ` +
        `${treeVersion}; mirroring into the ${installedVersion} directory.`
    );
  }

  mkdirSync(target.installPath, { recursive: true });

  for (const entry of readdirSync(target.installPath)) {
    if (entry != IN_USE_ENTRY) {
      rmSync(path.join(target.installPath, entry), { recursive: true, force: true });
    }
  }

  const files = execFileSync(
    'git',
    ['ls-files', '--cached', '--others', '--exclude-standard', '-z', '--', '.'],
    { cwd: source, encoding: 'utf8' }
  )
    .split('\0')
    // A tracked file deleted in the tree is listed but gone, as it will be once committed.
    .filter(file => file && existsSync(path.join(source, file)));

  for (const file of files) {
    const destination = path.join(target.installPath, file);

    mkdirSync(path.dirname(destination), { recursive: true });
    // Verbatim, so a symlink such as CLAUDE.md stays the link an install holds.
    cpSync(path.join(source, file), destination, { verbatimSymlinks: true });
  }

  console.log(`${target.harness}: mirrored ${target.plugin} over ${target.installPath}.`);
}

function readVersion(pluginRoot: string): string {
  const manifest = JSON.parse(
    readFileSync(path.join(pluginRoot, '.claude-plugin', 'plugin.json'), 'utf8')
  ) as { version?: string };

  return manifest.version ?? 'unknown';
}
