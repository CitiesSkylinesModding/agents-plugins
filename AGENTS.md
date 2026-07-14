# AGENTS.md

## Project overview

`coherent-gameface-agent-plugin` is an agent plugin for Claude Code and OpenAI Codex CLI: a **generic** toolkit for driving a running **Coherent Gameface** UI (the HTML/CSS/JS UI engine, Cohtml, that many games embed) over a direct Chrome DevTools Protocol (CDP) WebSocket.
It ships an MCP server (evaluate JS, screenshot, inspect and drive the DOM, capture the console, set JS breakpoints) plus skills.
It targets any Gameface application, but is developed and verified against **Cities: Skylines II**'s Gameface UI, which is the reference implementation and the source of the CDP quirks documented below.

The repo wears two hats, with distinct names:

- The plugin (`coherent-gameface` in both `.claude-plugin/plugin.json` and `.codex-plugin/plugin.json`, repo/root package `coherent-gameface-agent-plugin`): launches the committed server bundle (zero-install, offline, version-locked) from `.mcp.json` on Claude Code and from `.codex-plugin/mcp.json` on Codex CLI, and carries the skills (`skills/`).
- The MCP server (`mcp/` workspace) is also a standalone product for ANY MCP client, published on npm as **`@csmodding/gameface-devtools-mcp`** (handshake name `gameface-devtools-mcp`, bin `gameface-devtools-mcp`, launched via `npx -y @csmodding/gameface-devtools-mcp@latest`). Its npm-facing product page is `mcp/README.md`. Publishing is manual (`npm publish` from `mcp/`, run by Morgan).

## Tech stack

Project-specific tech stack. Ex:

- [mise-en-place](https://mise.jdx.dev): A tool to manage dev tools, env vars, and tasks per project.
- Bun workspaces: the root `package.json` carries the lint/format tooling (oxfmt/oxlint configs live at the root) and lefthook; `mcp/` is the only workspace package. One `bun.lock` at the root.

## Repository structure

- `package.json`: bun workspace root; lint/format tooling and lefthook live here (with `oxfmt.config.ts` / `oxlint.config.ts`).
- `.claude-plugin/plugin.json`: Claude Code plugin manifest (marketplace file: `.claude-plugin/marketplace.json`).
- `.codex-plugin/plugin.json`: Codex CLI plugin manifest; points `mcpServers` at `.codex-plugin/mcp.json` (native marketplace file: `.agents/plugins/marketplace.json`). Shared fields must match the Claude manifest, see "Dual-harness plugin architecture".
- `.mcp.json`: wires the `gameface` MCP server for Claude Code. Launches the committed bundle with `${GAMEFACE_MCP_RUNTIME:-node}`; node 22.4+ is the sole supported runtime (the env var is an escape hatch).
- `scripts/check-plugin-sync.ts`: consistency check between the two plugin manifests, run by `mise check` / `mise check:agents` (task `check:plugin-sync`) and by the lefthook pre-commit.
- `scripts/check-skill-changelog.ts`: freshness check of the gameface skill's baked version timeline against the live Gameface changelog (`mise skills:check-changelog`; network-dependent, not in CI).
- `skills/gameface/`: the Gameface domain-knowledge skill (`SKILL.md`, `references/`, and the `scripts/fetch-doc.mjs` docs extractor). AUTHORING CONVENTION: prose in `skills/**` markdown is written one sentence per line, never wrapped at 100 chars; it costs fewer tokens when loaded into context and keeps diffs line-granular.
- `mcp/src/`: the MCP server (TypeScript). `mcp/package.json` is the publishable npm package (`@csmodding/gameface-devtools-mcp`); `mcp/README.md` is what npm displays.
  - `server.ts`: entry point; registers tools and connects the stdio transport.
  - `cdp.ts`: direct CDP client (HTTP discovery, WebSocket connection, reconnect, events, onConnect).
  - `tools.ts`: UI tool implementations + page-context functions injected via `Runtime.evaluate`, plus the `ConsoleBuffer`.
  - `debugger.ts`: the `DebuggerSession` (V8 Debugger domain) + `game_debug_*` tools.
  - `shared.ts`: result builders (text/errorText/toErrorResult), RemoteObject/EvaluateResult types.
  - `config.ts`: `GAMEFACE_*` env config.
- `mcp/dist/server.mjs`: the shipped, self-contained bundle. COMMITTED on purpose (zero-install). Also the file npm publishes (`files: ["dist"]`) and the package's `bin`.
- `docs/ROADMAP.md`: planned facets.

## Commands

You can run `mise tasks` to see the full list of shortcut commands. Do NOT use npx to run commands, always prefer mise shortcuts, or bun/bunx if there is no dedicated mise shortcut.

- `mise build`: Rebuild the shipped bundle `mcp/dist/server.mjs` (commit the result).
- `mise check:agents`: Run type checking, formatting, and linting, with optimized output.

Tip: you can append arguments to mise shortcuts, mise will pass them through, ex. `mise some:task --some-arg`.

Always run the appropriate check/test commands after performing changes; but do it at the end of the editing session, not in the middle.

## How the server is built and shipped

The server uses `@modelcontextprotocol/sdk` + `zod`, bundled with `bun build --target=node` into a single `mcp/dist/server.mjs` that runs under node 22.4+ (global `WebSocket` / `fetch` are stable from that version). Bun is dev tooling only (build, package manager); the shipped runtime is node. Unit tests target node 24 and the current LTS.
The SDK/zod are bundled in, so there is NO runtime installation step.
Those packages are build-time `devDependencies` only.
The build emits a `#!/usr/bin/env node` banner so the same bundle works as the npm package's `bin` script.
Never enable minification (page-context functions are serialized via `.toString()`).

## Dual-harness plugin architecture (Claude Code + Codex CLI)

The plugin targets both harnesses from one repo, with one manifest per harness. The mcp configs CANNOT be merged into one file, each harness has a blocker the other does not (verified 2026-07, details below):

- Claude Code: `.claude-plugin/plugin.json` + root `.mcp.json`. Interpolates `${VAR:-default}` (that syntax is Claude Code-specific) but IGNORES `cwd` (anthropics/claude-code#17565), so it relies on `${CLAUDE_PLUGIN_ROOT}` for the bundle path.
- Codex CLI: `.codex-plugin/plugin.json` (schema is a superset of Claude's; component pointers are `./`-relative paths) + `.codex-plugin/mcp.json`. Codex does NOT interpolate `${VAR}` in MCP command/args (openai/codex#19582, open as of 2026-07) and injects almost no env vars into the MCP child; `PLUGIN_ROOT`/`CLAUDE_PLUGIN_ROOT` exist for hooks ONLY. THE working mechanism is a relative `"cwd"` resolved against the installed plugin root (verified in codex-rs `plugin_config.rs`; same pattern as OpenAI's first-party `codex-security` plugin). Watch #19582: if fixed, the two configs could converge.
- Codex gotcha: the server-map key must be camelCase `mcpServers`; snake_case silently registers a bogus server.
- Codex marketplace: native file is `.agents/plugins/marketplace.json` (object-form `source`, e.g. `{"source": "local", "path": "./"}`). The legacy fallback that reads `.claude-plugin/marketplace.json` is flaky (#19372), so we ship the native file.
- Env config on Codex: no env block, `node` hardcoded (no `GAMEFACE_MCP_RUNTIME` escape hatch there); the server falls back to `mcp/src/config.ts` defaults. `~/.codex/config.toml` CANNOT override a plugin-provided server (verified 2026-07: Codex fails with `Error loading config.toml: invalid transport`); the only override path is registering the npm server manually via `codex mcp add`, which replaces the plugin's copy under the same name.
- SYNC RULE: shared fields of the two plugin.json files (name, version, description, author, homepage, repository, license, keywords) must stay identical. `scripts/check-plugin-sync.ts` enforces this (plus that `.codex-plugin/mcp.json` points at an existing bundle) via `mise check:plugin-sync`, wired into `mise check`, `mise check:agents` (so CI gets it), and the lefthook pre-commit. When editing plugin metadata, edit BOTH manifests.

## Versioning and releases

release-please (`.github/workflows/release-please.yml`, config in `release-please-config.json` + `.release-please-manifest.json`) maintains a rolling release PR on `main` from Conventional Commits; merging it bumps versions, updates changelogs, tags, and creates GitHub Releases. Two release units with independent numbers:

- **plugin** (root): version in root `package.json`, synced into `.claude-plugin/plugin.json` AND `.codex-plugin/plugin.json` via `extra-files`. Bare `vX.Y.Z` tags.
- **mcp** (`mcp/`): version in `mcp/package.json`. Tags `gameface-devtools-mcp-vX.Y.Z`.

Rules that matter when committing:

- Commit messages follow Conventional Commits. `feat`/`fix`/`deps` trigger releases; `chore`/`refactor`/`docs` do not. Anything user-facing (skills, server behavior, `.mcp.json`) must be committed as `feat` or `fix`.
- Any `mcp/**` commit bumps BOTH mcp and the plugin (the `plugin.json` version is Claude Code's update pin, so an mcp fix must bump it to reach plugin users). Plugin-only changes bump only the plugin.
- Pre-1.0: `feat` bumps minor, `fix` bumps patch. 1.0.0 only via a deliberate `Release-As:` footer.
- The server reads its version from `mcp/package.json` at runtime (no hardcoded version, no rebuild needed on release).
- npm publishing stays MANUAL (`mise publish`, run by Morgan). No CI publish job; do not add one.
- CI (`.github/workflows/ci.yml`) runs `mise check:agents` + `mise build` and fails if the tree differs (stale bundle or unformatted code). A lefthook pre-commit rebuilds and stages `mcp/dist/server.mjs` whenever staged files touch `mcp/src/`.

## Gameface CDP gotchas (verified, do not relearn the hard way)

These were verified against Cities: Skylines II's Gameface UI (Cohtml 1.64.0.7, V8 9.4, CDP 1.3) but
reflect Gameface/Cohtml behavior in general.

- Discover the page target from `GET /json/list`; build the WS URL yourself as `ws://host:port/devtools/page/<id>`. The `webSocketDebuggerUrl` field is malformed.
- `Runtime.evaluate` (returnByValue) works immediately. `Page.captureScreenshot` needs `Page.enable`.
- CDP `Input.*` (mouse AND key) is ACCEPTED but does NOT reach the UI. All input tools (`game_click`, `game_fill`, `game_type`, `game_hover`) dispatch real bubbling DOM events in-page (`el.dispatchEvent(new MouseEvent('click', {bubbles:true,...}))`); React's delegated handlers pick them up. `HTMLElement.click()` does not exist in Cohtml. `PointerEvent` and `InputEvent` constructors are missing too (dispatch pointer* as `MouseEvent`, use `Event('input')`); but `KeyboardEvent`, `MouseEvent`, and the native `HTMLInputElement` value setter DO exist.
- `getBoundingClientRect()` returns 0x0 for a node measured in the same eval tick it was inserted.
- Page-context functions in `tools.ts` are serialized via `.toString()`; keep them self-contained plain browser JS (no references outside their body). Do not enable minification in the build.
- The V8 `Debugger` domain works (breakpoints, paused events, evaluateOnCallFrame, stepping). Hitting a breakpoint or `Debugger.pause` FREEZES the UI thread until resume. Safety net: closing the CDP connection auto-resumes a paused UI (verified). CDP line numbers are 0-based; the `game_debug_*` tools expose 1-based lines.

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
- Propose updates to `AGENTS.md` or `docs/` when you notice a pattern or introduced changes that deserve to be documented for future sessions.
- After changing `mcp/src/`, run `mise check:agents` and `mise build`. The running `gameface` MCP server keeps serving the old bundle after a rebuild; ask the user to hit Reconnect in `/mcp` (verified sufficient, no Claude Code restart needed; agents cannot trigger it).
- Store hard-won facts about Gameface internals in memory.
- Keep the server generic: no assumptions about a specific game's DOM and APIs beyond the defaults. CS2 is the test target, not a hard dependency.
