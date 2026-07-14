/* oxlint-disable node/no-sync -- sequential check script, synchronous IO is intentional. */

import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';

// Consistency check for the dual plugin manifests (Claude Code + Codex CLI).
// The two harnesses need separate mcp configs (Codex resolves a relative "cwd" against the plugin
// root but does not interpolate ${VAR}; Claude Code interpolates ${VAR} but ignores "cwd"), so
// shared metadata is duplicated and must be kept in sync by hand.
// Exits nonzero (via a failed assertion) on any drift.

const repoRoot = path.resolve(import.meta.dirname, '..');

const claudeManifest = readJsonObject('.claude-plugin/plugin.json');
const codexManifest = readJsonObject('.codex-plugin/plugin.json');

checkSharedManifestFields();
checkCodexMcpConfig();

function checkSharedManifestFields(): void {
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
      `Manifest field "${field}" differs between .claude-plugin/plugin.json and ` +
        `.codex-plugin/plugin.json; keep the shared fields identical.`
    );
  }
}

function checkCodexMcpConfig(): void {
  const mcpConfig = readJsonObject('.codex-plugin/mcp.json');

  // The key must be camelCase "mcpServers": Codex silently registers a bogus server when the
  // snake_case spelling is used.
  const servers = mcpConfig.mcpServers;
  assert.ok(
    typeof servers == 'object' && servers != null,
    `.codex-plugin/mcp.json must declare a camelCase "mcpServers" object.`
  );

  const { gameface } = servers as Record<string, unknown>;
  assert.ok(
    typeof gameface == 'object' && gameface != null,
    `.codex-plugin/mcp.json must declare the "gameface" server.`
  );

  const { args, cwd } = gameface as Record<string, unknown>;

  // A relative "cwd" is what makes Codex resolve the bundle against the installed plugin root.
  assert.ok(cwd == '.', `The "gameface" server in .codex-plugin/mcp.json must set "cwd": ".".`);

  assert.ok(
    Array.isArray(args),
    `The "gameface" server in .codex-plugin/mcp.json must pass the bundle path in "args".`
  );

  const [bundleRelativePath] = args as unknown[];
  assert.ok(
    typeof bundleRelativePath == 'string',
    `The "gameface" server in .codex-plugin/mcp.json must pass the bundle path as args[0].`
  );

  assert.ok(
    existsSync(path.join(repoRoot, bundleRelativePath)),
    `.codex-plugin/mcp.json points at "${bundleRelativePath}", which does not exist in the repository.`
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
