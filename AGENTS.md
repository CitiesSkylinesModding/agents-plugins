# AGENTS.md

## Project overview

`coherent-gameface-mcp` is a Claude Code plugin: a **generic** toolkit for driving a running **Coherent Gameface** UI (the HTML/CSS/JS UI engine, Cohtml, that many games embed) over a direct Chrome DevTools Protocol (CDP) WebSocket.
It ships an MCP server (evaluate JS, screenshot, inspect and drive the DOM, capture the console, set JS breakpoints) plus skills.
It targets any Gameface application, but is developed and verified against **Cities: Skylines II**'s Gameface UI, which is the reference implementation and the source of the CDP quirks documented below.

## Tech stack

Project-specific tech stack. Ex:

- [mise-en-place](https://mise.jdx.dev): A tool to manage dev tools, env vars, and tasks per project.
- Bun workspaces: the root `package.json` carries the lint/format tooling (oxfmt/oxlint configs live at the root) and lefthook; `server/` is the only workspace package. One `bun.lock` at the root.

## Repository structure

- `package.json`: bun workspace root; lint/format tooling and lefthook live here (with `oxfmt.config.ts` / `oxlint.config.ts`).
- `.claude-plugin/plugin.json`: plugin manifest.
- `.mcp.json`: wires the `gameface` MCP server. Launches the committed bundle with `${GAMEFACE_MCP_RUNTIME:-bun}` so it runs under bun (default) or node (`GAMEFACE_MCP_RUNTIME=node`).
- `server/src/`: the MCP server (TypeScript).
  - `server.ts`: entry point; registers tools and connects the stdio transport.
  - `cdp.ts`: direct CDP client (HTTP discovery, WebSocket connection, reconnect, events, onConnect).
  - `tools.ts`: UI tool implementations + page-context functions injected via `Runtime.evaluate`, plus the `ConsoleBuffer`.
  - `debugger.ts`: the `DebuggerSession` (V8 Debugger domain) + `game_debug_*` tools.
  - `shared.ts`: result builders (text/errorText/toErrorResult), RemoteObject/EvaluateResult types.
  - `config.ts`: `GAMEFACE_*` env config.
- `server/dist/server.mjs`: the shipped, self-contained bundle. COMMITTED on purpose (zero-install).
- `docs/ROADMAP.md`: planned facets.

## Commands

You can run `mise tasks` to see the full list of shortcut commands. Do NOT use npx to run commands, always prefer mise shortcuts, or bun/bunx if there is no dedicated mise shortcut.

- `mise build`: Rebuild the shipped bundle `server/dist/server.mjs` (commit the result).
- `mise check:agents`: Run type checking, formatting, and linting, with optimized output.

Tip: you can append arguments to mise shortcuts, mise will pass them through, ex. `mise some:task --some-arg`.

Always run the appropriate check/test commands after performing changes; but do it at the end of the editing session, not in the middle.

## How the server is built and shipped

The server uses `@modelcontextprotocol/sdk` + `zod`, bundled with `bun build --target=node` into a single `server/dist/server.mjs` that runs under bun or node 24+ (both provide global `WebSocket` / `fetch`).
The SDK/zod are bundled in, so there is NO runtime installation step.
Those packages are build-time `devDependencies` only.

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
- After changing `server/src/`, run `mise check:agents` and `mise build`.
- Store hard-won facts about Gameface internals in memory.
- Keep the server generic: no assumptions about a specific game's DOM and APIs beyond the defaults. CS2 is the test target, not a hard dependency.
