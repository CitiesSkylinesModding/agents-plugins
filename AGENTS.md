# AGENTS.md

## Project overview

`agents-plugins` is the CS Modding marketplace (`csmodding`) of agent plugins for Claude Code and OpenAI Codex CLI. Plugin sources live under `plugins/<name>/`; each plugin's own architecture, gotchas, and agent behaviors are documented in its own `AGENTS.md` (with a `CLAUDE.md` relative symlink to it):

- `plugins/coherent-gameface/`: the flagship plugin, a generic toolkit for driving a running Coherent Gameface UI (Cohtml) over a direct CDP WebSocket; MCP server (published on npm as `@csmodding/gameface-devtools-mcp`) + skills.
- `plugins/unity-devtools/`: drive a running Unity Mono development build from the outside over the Mono Soft Debugger protocol (SDB); C# MCP server + SDB class library.

Both plugins are generic toolkits developed and verified against **Cities: Skylines II** as the reference target.

Everything a plugin ships MUST live inside its `plugins/<name>/` directory (marketplace installs copy only that subtree); that is why each plugin's `mcp/` lives inside the plugin. New plugins get sibling directories and entries in both marketplace files.

## Tech stack

Project-specific tech stack. Ex:

- [mise-en-place](https://mise.jdx.dev): A tool to manage dev tools, env vars, and tasks per project.
- Bun workspaces: the root `package.json` carries the lint/format tooling (oxfmt/oxlint configs live at the root) and lefthook; the gameface `mcp/` is the only workspace package. One `bun.lock` at the root.

## Repository structure

- `package.json`: bun workspace root; lint/format tooling and lefthook live here (with `oxfmt.config.ts` / `oxlint.config.ts`).
- `.claude-plugin/marketplace.json`: the Claude Code marketplace file (`csmodding`); each entry's `source` points at a `./plugins/<name>` directory.
- `.agents/plugins/marketplace.json`: the Codex CLI native marketplace file (object-form `source`, e.g. `{"source": "local", "path": "./plugins/coherent-gameface"}`).
- `.mcp.json` (root): LOCAL DEV ONLY; wires the plugins' MCP servers for sessions in this repo by pointing straight at the committed/built artifacts. Installed users get each plugin's own `.mcp.json` instead; keep them in sync when changing server wiring.
- `scripts/check-plugin-sync.ts`: consistency check between each plugin's two manifests, run by `mise check` / `mise check:agents` (task `check:plugin-sync`) and by the lefthook pre-commit.
- `scripts/check-skill-changelog.ts`: freshness check of the gameface skill's baked version timeline against the live Gameface changelog (`mise skills:check-changelog`; network-dependent, not in CI).
- `plugins/coherent-gameface/`: the Gameface plugin.
- `plugins/unity-devtools/`: the Unity plugin.
- `docs/ROADMAP.md`: planned facets.

## Commands

You can run `mise tasks` to see the full list of shortcut commands. Do NOT use npx to run commands, always prefer mise shortcuts, or bun/bunx if there is no dedicated mise shortcut.

- `mise build:gameface`: Rebuild the shipped bundle `plugins/coherent-gameface/mcp/dist/server.mjs` (commit the result). (`build:` is a namespace; unity-devtools has its own `build:unity:*` tasks.)
- `mise check:agents`: Verify (read-only) type checking, linting, and formatting, with output optimized for agents. `check:*` write nothing; `mise fix` (or `fix:oxlint` / `fix:oxfmt`) applies the auto-fixes. C# formatting is `mise fix:cs` (no read-only check exists; see `plugins/unity-devtools/AGENTS.md`).

Tip: you can append arguments to mise shortcuts, mise will pass them through, ex. `mise some:task --some-arg`.

Always run the appropriate check/test commands after performing changes; but do it at the end of the editing session, not in the middle.

## Dual-harness plugin architecture (Claude Code + Codex CLI)

Every plugin targets both harnesses from one repo, with one manifest per harness. The mcp configs CANNOT be merged into one file, each harness has a blocker the other does not (verified 2026-07, details below):

- Claude Code: the plugin's `.claude-plugin/plugin.json` + `.mcp.json` (both under `plugins/<name>/`). Interpolates `${VAR:-default}` (that syntax is Claude Code-specific) but IGNORES `cwd` (anthropics/claude-code#17565), so it relies on `${CLAUDE_PLUGIN_ROOT}` for artifact paths.
- Codex CLI: `.codex-plugin/plugin.json` (schema is a superset of Claude's; component pointers are `./`-relative paths) + `.codex-plugin/mcp.json`. Codex does NOT interpolate `${VAR}` in MCP command/args (openai/codex#19582, open as of 2026-07) and injects almost no env vars into the MCP child; `PLUGIN_ROOT`/`CLAUDE_PLUGIN_ROOT` exist for hooks ONLY. THE working mechanism is a relative `"cwd"` resolved against the installed plugin root (verified in codex-rs `plugin_config.rs`; same pattern as OpenAI's first-party `codex-security` plugin). Watch #19582: if fixed, the two configs could converge.
- Codex gotcha: the server-map key must be camelCase `mcpServers`; snake_case silently registers a bogus server.
- Codex marketplace: native file is `.agents/plugins/marketplace.json` (object-form `source`, e.g. `{"source": "local", "path": "./plugins/coherent-gameface"}`). The legacy fallback that reads `.claude-plugin/marketplace.json` is flaky (#19372), so we ship the native file.
- Env config on Codex: plugin MCP servers get no env block, so a plugin's server must fall back to built-in defaults there. `~/.codex/config.toml` CANNOT override a plugin-provided server (verified 2026-07: Codex fails with `Error loading config.toml: invalid transport`); the only override path is registering a server manually via `codex mcp add`, which replaces the plugin's copy under the same name.
- SYNC RULE: shared fields of each plugin's two plugin.json files (name, version, description, author, homepage, repository, license, keywords) must stay identical. `scripts/check-plugin-sync.ts` enforces this (plus that each `.codex-plugin/mcp.json` points at an existing committed artifact) via `mise check:plugin-sync`, wired into `mise check`, `mise check:agents` (so CI gets it), and the lefthook pre-commit. When editing plugin metadata, edit BOTH manifests.

## Versioning and releases

release-please (`.github/workflows/release-please.yml`, config in `release-please-config.json` + `.release-please-manifest.json`) maintains a rolling release PR on `main` from Conventional Commits; merging it bumps versions, updates changelogs, tags, and creates GitHub Releases. Release units come in per-plugin pairs, and there is NO root unit (root-only changes never release; bare `vX.Y.Z` tags were dropped before the first release):

- **coherent-gameface plugin** (`plugins/coherent-gameface/`): version anchored in that directory's private `package.json` (a release-please anchor, not a workspace package), synced via `extra-files` into the plugin's `.claude-plugin/plugin.json`, `.codex-plugin/plugin.json`, AND the root `package.json` (leading-`/` extra-file path = repo-root-relative). Tags `coherent-gameface-vX.Y.Z`.
- **coherent-gameface mcp** (`plugins/coherent-gameface/mcp/`): version in its `package.json`. Tags `gameface-devtools-mcp-vX.Y.Z`.
- **unity-devtools plugin** (`plugins/unity-devtools/`): same private `package.json` anchor pattern, synced via `extra-files` into its two plugin manifests (NOT the root `package.json`, which follows the flagship plugin). Tags `unity-devtools-vX.Y.Z`.
- **unity-devtools mcp** (`plugins/unity-devtools/mcp/`): private `package.json` anchor (the server is C#), synced via a `generic` extra-file into the csproj's `<Version>` (block markers `x-release-please-start-version`/`x-release-please-end`), which the server reports at the MCP handshake through the assembly version. Tags `unity-devtools-mcp-vX.Y.Z`.

Each pair is grouped by a `linked-versions` plugin (one group per plugin), so the two units of a plugin ALWAYS share the same version and release together; the two plugins version independently of each other. Rationale: release-please attributes each commit file to the DEEPEST matching package path, so without linking, an mcp commit would bump only mcp and never the plugin, even though the `plugin.json` version is Claude Code's update pin that ships the new artifact. Trade-off (accepted): a plugin-only change (e.g. skills) also bumps the mcp version; npm publishing is manual, so unpublished mcp versions are fine.

Rules that matter when committing:

- Commit messages follow Conventional Commits. `feat`/`fix`/`deps` trigger releases; `chore`/`refactor`/`docs` do not. Anything user-facing (skills, server behavior, a plugin's `.mcp.json`) must be committed as `feat` or `fix`.
- Because of `linked-versions`, any releasable commit under a plugin's directory (mcp or not) bumps BOTH of that plugin's units to the same version.
- Pre-1.0: `feat` bumps minor, `fix` bumps patch. 1.0.0 only via a deliberate `Release-As:` footer.
- The gameface server reads its version from the mcp workspace's `package.json` at runtime (no hardcoded version, no rebuild needed on release).
- npm publishing stays MANUAL (`mise publish`, run by the user). No CI publish job; do not add one.
- CI (`.github/workflows/ci.yml`) runs `mise check:agents` (read-only; fails on any unformatted or lint-dirty file) + `mise build:gameface`, then `git diff --exit-code` catches a stale committed bundle; it then restores the vendored SDB sources (sparse clone) and builds the .NET solution to catch compile errors, but does NOT diff the committed unity exe (a dotnet publish is not assumed byte-reproducible). Lefthook pre-commits rebuild and stage each committed artifact whenever staged files touch its sources (gameface mcp `src/`, unity `mcp/`+`sdb/` C# sources).

## Boundaries

Never:

- Create a git branch, stage files, or commit work yourself unless the user expressly told you so.
- Commit secrets, tokens, `.env` files, dumps, or credentials.
- Modify generated files unless the generation command was run.
- Change public API behavior without calling it out.
- Add large dependencies for small utilities.

Ask first before:

- Adding a dependency.
- Changing database schema.
- Changing authentication/authorization logic.
- Reworking architecture.
- Adding background jobs, queues, or external services.
- Performing destructive file or data operations.

## Preferred agent behavior

- Start by inspecting existing patterns.
- Prefer LSP over Grep/Glob/Read for code navigation.
- Make the smallest safe change, but if you think a refactor is overdue, speak up.
- Prefer editing existing files over creating parallel abstractions.
- When uncertain, state the assumption and proceed conservatively.
- Propose updates to `AGENTS.md` or `docs/` when you notice a pattern or introduced changes that deserve to be documented for future sessions. Plugin-specific facts belong in that plugin's `AGENTS.md`, repo-wide facts here; every `AGENTS.md` gets a `CLAUDE.md` relative symlink pointing at it.
