/* oxlint-disable node/no-sync -- sequential check script, synchronous IO is intentional. */

import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';

// Consistency check for each plugin's dual manifests (Claude Code + Codex CLI).
// The two harnesses need separate mcp configs (Codex resolves a relative "cwd" against the plugin
// root but does not interpolate ${VAR}; Claude Code interpolates ${VAR} but ignores "cwd"), so
// shared metadata is duplicated and must be kept in sync by hand.
// Exits nonzero (via a failed assertion) on any drift.

const repoRoot = path.resolve(import.meta.dirname, '..');

// Plugin sources live under plugins/<name>/; each carries its own pair of manifests and declares
// one MCP server whose committed artifact must exist. Plugins are discovered from the directory
// tree, so a newly added plugin cannot silently escape the check.
const pluginRoots = readdirSync(path.join(repoRoot, 'plugins'), { withFileTypes: true })
  .filter(entry => entry.isDirectory())
  .map(entry => `plugins/${entry.name}`);

assert.ok(pluginRoots.length > 0, `No plugin directories found under plugins/.`);

for (const pluginRoot of pluginRoots) {
  checkPlugin(pluginRoot);
}

checkRootVersionAnchor();

function checkPlugin(pluginRoot: string): void {
  const claudeManifest = readJsonObject(`${pluginRoot}/.claude-plugin/plugin.json`);
  const codexManifest = readJsonObject(`${pluginRoot}/.codex-plugin/plugin.json`);

  checkSharedManifestFields(pluginRoot, claudeManifest, codexManifest);
  checkVersionAnchor(pluginRoot, claudeManifest);
  checkCodexMcpConfig(pluginRoot);
}

function checkSharedManifestFields(
  pluginRoot: string,
  claudeManifest: Record<string, unknown>,
  codexManifest: Record<string, unknown>
): void {
  const sharedFields = [
    'name',
    'version',
    'description',
    'author',
    'homepage',
    'repository',
    'license',
    'keywords'
  ];

  for (const field of sharedFields) {
    assert.deepEqual(
      codexManifest[field],
      claudeManifest[field],
      `Manifest field "${field}" differs between ${pluginRoot}'s .claude-plugin/plugin.json and ` +
        `.codex-plugin/plugin.json; keep the shared fields identical.`
    );
  }
}

function checkVersionAnchor(pluginRoot: string, claudeManifest: Record<string, unknown>): void {
  // The plugin's package.json is the release-please version anchor; the manifests are synced from
  // it via extra-files, so any drift means a hand edit bypassed the release process. Codex
  // manifest coverage is transitive through checkSharedManifestFields.
  const anchor = readJsonObject(`${pluginRoot}/package.json`);

  assert.equal(
    claudeManifest.version,
    anchor.version,
    `The plugin manifests must carry the same version as ${pluginRoot}/package.json ` +
      `(the release-please anchor).`
  );
}

function checkCodexMcpConfig(pluginRoot: string): void {
  const mcpConfig = readJsonObject(`${pluginRoot}/.codex-plugin/mcp.json`);

  // The key must be camelCase "mcpServers": Codex silently registers a bogus server when the
  // snake_case spelling is used.
  const servers = mcpConfig.mcpServers;
  assert.ok(
    typeof servers == 'object' && servers != null,
    `${pluginRoot}/.codex-plugin/mcp.json must declare a camelCase "mcpServers" object.`
  );

  // Each plugin ships exactly one MCP server; its key is whatever the config declares.
  const serverKeys = Object.keys(servers);
  assert.equal(
    serverKeys.length,
    1,
    `${pluginRoot}/.codex-plugin/mcp.json must declare exactly one server.`
  );

  const [serverKey] = serverKeys;

  assert.ok(serverKey != null, `${pluginRoot}/.codex-plugin/mcp.json declares no server key.`);

  const server = (servers as Record<string, unknown>)[serverKey];
  assert.ok(
    typeof server == 'object' && server != null,
    `${pluginRoot}/.codex-plugin/mcp.json must declare the "${serverKey}" server.`
  );

  const { command, args, cwd } = server as Record<string, unknown>;

  // A relative "cwd" is what makes Codex resolve the artifact against the installed plugin root.
  assert.ok(
    cwd == '.',
    `The "${serverKey}" server in ${pluginRoot}/.codex-plugin/mcp.json must set "cwd": ".".`
  );

  // The committed artifact the server launches: args[0] when a runtime carries it (gameface's
  // node bundle), else the command itself (unity's exe).
  assert.ok(
    typeof command == 'string',
    `The "${serverKey}" server in ${pluginRoot}/.codex-plugin/mcp.json must set "command".`
  );

  let artifactRelativePath = command;

  if (args != null) {
    assert.ok(
      Array.isArray(args),
      `The "${serverKey}" server in ${pluginRoot}/.codex-plugin/mcp.json must pass the artifact ` +
        `path in "args".`
    );

    const [firstArg] = args as unknown[];

    assert.ok(
      typeof firstArg == 'string',
      `The "${serverKey}" server in ${pluginRoot}/.codex-plugin/mcp.json must pass the artifact ` +
        `path as args[0].`
    );

    artifactRelativePath = firstArg;
  }

  // Codex resolves the relative path against the installed plugin root, so mirror that here.
  assert.ok(
    existsSync(path.join(repoRoot, pluginRoot, artifactRelativePath)),
    `${pluginRoot}/.codex-plugin/mcp.json points at "${artifactRelativePath}", which does not ` +
      `exist under ${pluginRoot}. Is the committed artifact missing?`
  );
}

function checkRootVersionAnchor(): void {
  // The root package.json version is synced (via extra-files) from the coherent-gameface anchor,
  // the repo's flagship plugin; the other plugins version independently.
  const anchor = readJsonObject('plugins/coherent-gameface/package.json');
  const rootPackage = readJsonObject('package.json');

  assert.equal(
    rootPackage.version,
    anchor.version,
    `The root package.json must carry the same version as plugins/coherent-gameface/package.json ` +
      `(the release-please anchor).`
  );
}

function readJsonObject(relativePath: string): Record<string, unknown> {
  const parsed: unknown = JSON.parse(readFileSync(path.join(repoRoot, relativePath), 'utf8'));

  assert.ok(
    typeof parsed == 'object' && parsed != null && !Array.isArray(parsed),
    `${relativePath} must contain a JSON object.`
  );

  return parsed as Record<string, unknown>;
}
