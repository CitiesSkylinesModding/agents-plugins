# AGENTS.md

## Project overview

`coherent-gameface-claude-plugin` is a Claude Code plugin: a **generic** toolkit for driving a running **Coherent Gameface** UI (the HTML/CSS/JS UI engine, Cohtml, that many games embed) over a direct Chrome DevTools Protocol (CDP) WebSocket.
It ships an MCP server (evaluate JS, screenshot, inspect and drive the DOM, capture the console, set JS breakpoints) plus skills.
It targets any Gameface application, but is developed and verified against **Cities: Skylines II**'s Gameface UI, which is the reference implementation and the source of the CDP quirks documented below.

The repo wears two hats, with distinct names:

- The Claude Code plugin (`coherent-gameface` in `.claude-plugin/plugin.json`, repo/root package `coherent-gameface-claude-plugin`): launches the committed server bundle from `.mcp.json` (zero-install, offline, version-locked) and will carry the skills.
- The MCP server (`mcp/` workspace) is also a standalone product for ANY MCP client, published on npm as **`@csmodding/gameface-devtools-mcp`** (handshake name `gameface-devtools-mcp`, bin `gameface-devtools-mcp`, launched via `npx -y @csmodding/gameface-devtools-mcp@latest`). Its npm-facing product page is `mcp/README.md`. Publishing is manual (`npm publish` from `mcp/`, run by Morgan).

## Tech stack

Project-specific tech stack. Ex:

- [mise-en-place](https://mise.jdx.dev): A tool to manage dev tools, env vars, and tasks per project.
- Bun workspaces: the root `package.json` carries the lint/format tooling (oxfmt/oxlint configs live at the root) and lefthook; `mcp/` is the only workspace package. One `bun.lock` at the root.

## Repository structure

- `package.json`: bun workspace root; lint/format tooling and lefthook live here (with `oxfmt.config.ts` / `oxlint.config.ts`).
- `.claude-plugin/plugin.json`: plugin manifest.
- `.mcp.json`: wires the `gameface` MCP server. Launches the committed bundle with `${GAMEFACE_MCP_RUNTIME:-node}`; node 22.4+ is the sole supported runtime (the env var is an escape hatch).
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

## Versioning and releases

release-please (`.github/workflows/release-please.yml`, config in `release-please-config.json` + `.release-please-manifest.json`) maintains a rolling release PR on `main` from Conventional Commits; merging it bumps versions, updates changelogs, tags, and creates GitHub Releases. Two release units with independent numbers:

- **plugin** (root): version in root `package.json`, synced into `.claude-plugin/plugin.json` via `extra-files`. Bare `vX.Y.Z` tags.
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
