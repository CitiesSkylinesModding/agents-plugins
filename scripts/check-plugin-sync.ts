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
  checkMcpVersionPins(pluginRoot);
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

  assert.ok(
    typeof command == 'string',
    `The "${serverKey}" server in ${pluginRoot}/.codex-plugin/mcp.json must set "command".`
  );

  // Dnx-launched servers ship as a NuGet dotnet tool: there is no committed artifact to check
  // (the version pin is checked by checkMcpVersionPins instead).
  if (isDnxLaunch(command, args)) {
    return;
  }

  // The committed artifact the server launches: args[0] when a runtime carries it (gameface's node
  // bundle), else the command itself.
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

function isDnxLaunch(command: unknown, args: unknown): boolean {
  return command == 'dotnet' && Array.isArray(args) && args[0] == 'dnx';
}

// Both harness configs launch the dnx-shipped server with an explicit version pin
// (`dotnet dnx <packageId> --version <pin> --yes`); release-please syncs the pins via
// extra-files, so a drift from the mcp anchor means a hand edit bypassed the release process.
// Publish existence cannot be checked offline; release-day ordering matters instead: after
// merging the release PR (which bumps the pins), publish the nupkg to NuGet BEFORE reconnecting
// or announcing, since installs resolve the pinned version from NuGet and fail until it exists.
function checkMcpVersionPins(pluginRoot: string): void {
  for (const configPath of [`${pluginRoot}/.mcp.json`, `${pluginRoot}/.codex-plugin/mcp.json`]) {
    if (!existsSync(path.join(repoRoot, configPath))) {
      continue;
    }

    const servers = readJsonObject(configPath).mcpServers;

    assert.ok(
      typeof servers == 'object' && servers != null,
      `${configPath} must declare a camelCase "mcpServers" object.`
    );

    for (const [serverKey, server] of Object.entries(servers)) {
      assert.ok(
        typeof server == 'object' && server != null,
        `The "${serverKey}" server in ${configPath} must be an object.`
      );

      const { command, args } = server as Record<string, unknown>;

      if (!isDnxLaunch(command, args)) {
        continue;
      }

      const argList = args as unknown[];

      // The launched package id must be the one the csproj actually packs: a typo'd or drifted
      // id passes every offline check and fails at every user's first connect.
      assert.equal(
        argList[1],
        readDnxPackageId(pluginRoot),
        `The "${serverKey}" server in ${configPath} launches a dnx package ID different from ` +
          `the <PackageId> in ${pluginRoot}/mcp's csproj.`
      );

      const versionFlagIndex = argList.indexOf('--version');

      assert.ok(
        versionFlagIndex > 0 && typeof argList[versionFlagIndex + 1] == 'string',
        `The "${serverKey}" server in ${configPath} must pin the tool version with ` +
          `"--version <version>".`
      );

      // Release-please rewrites the pin through a fixed JSONPath (args[N]); assert the slot we
      // just validated positionally is the same one it rewrites, so an arg reorder cannot
      // silently decouple the two encodings.
      assert.equal(
        versionFlagIndex + 1,
        releasePleasePinIndex(configPath, serverKey),
        `The "--version" pin in ${configPath} sits at args[${versionFlagIndex + 1}], but ` +
          `release-please-config.json rewrites a different args slot.`
      );

      const anchor = readJsonObject(`${pluginRoot}/mcp/package.json`);

      assert.equal(
        argList[versionFlagIndex + 1],
        anchor.version,
        `The "${serverKey}" server in ${configPath} pins a version different from ` +
          `${pluginRoot}/mcp/package.json (the release-please anchor).`
      );
    }
  }
}

// Finds the args index release-please's extra-files entry rewrites for this config's server pin.
function releasePleasePinIndex(configPath: string, serverKey: string): number {
  const { packages } = readJsonObject('release-please-config.json');

  assert.ok(
    typeof packages == 'object' && packages != null,
    `release-please-config.json must declare "packages".`
  );

  for (const [packageDir, pkg] of Object.entries(packages)) {
    if (typeof pkg != 'object' || pkg == null) {
      continue;
    }

    const extraFiles = (pkg as Record<string, unknown>)['extra-files'];

    if (!Array.isArray(extraFiles)) {
      continue;
    }

    for (const entry of extraFiles) {
      if (typeof entry != 'object' || entry == null) {
        continue;
      }

      const { path: entryPath, jsonpath } = entry as Record<string, unknown>;

      if (typeof entryPath != 'string' || typeof jsonpath != 'string') {
        continue;
      }

      // A leading "/" means repo-root-relative; otherwise the path resolves against the
      // release-please package directory.
      const resolved = entryPath.startsWith('/')
        ? entryPath.slice(1)
        : `${packageDir}/${entryPath}`;

      if (resolved != configPath) {
        continue;
      }

      const match = jsonpath.match(/^\$\.mcpServers\.(?<key>[^.]+)\.args\[(?<index>\d+)\]$/u);

      if (match?.groups?.key == serverKey && match.groups.index != null) {
        return Number(match.groups.index);
      }
    }
  }

  return assert.fail(
    `release-please-config.json has no extra-file rewriting the "${serverKey}" dnx version pin ` +
      `in ${configPath}; the pin would go stale on release.`
  );
}

function readDnxPackageId(pluginRoot: string): string {
  const mcpDir = path.join(repoRoot, pluginRoot, 'mcp');

  const csprojNames = readdirSync(mcpDir).filter(name => name.endsWith('.csproj'));

  assert.equal(
    csprojNames.length,
    1,
    `${pluginRoot}/mcp must contain exactly one csproj to read the dnx <PackageId> from.`
  );

  const [csprojName] = csprojNames;

  assert.ok(csprojName != null, `${pluginRoot}/mcp has no csproj.`);

  const packageId = readFileSync(path.join(mcpDir, csprojName), 'utf8').match(
    /<PackageId>(?<id>[^<]+)<\/PackageId>/u
  )?.groups?.id;

  assert.ok(
    packageId != null,
    `${pluginRoot}/mcp/${csprojName} must declare the <PackageId> the dnx launch targets.`
  );

  return packageId;
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
