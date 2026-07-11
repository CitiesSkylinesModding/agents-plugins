# CLAUDE.md

## Project overview

`cs2-modkit` is a Claude Code plugin: a multi-facet toolkit for Cities: Skylines II mod
development. The first facet is an MCP server that drives the running mod UI (Coherent Gameface)
over a direct Chrome DevTools Protocol (CDP) WebSocket. Future facets (C# debugging, a
decompiled-source retro-engineering subagent) are tracked in `docs/ROADMAP.md`.

## Repository structure

- `.claude-plugin/plugin.json`: plugin manifest.
- `.mcp.json`: wires the `gameface` MCP server. Launches the committed bundle with
  `${CS2_MCP_RUNTIME:-bun}` so it runs under bun (default) or node (`CS2_MCP_RUNTIME=node`).
- `server/src/`: the MCP server (TypeScript).
  - `server.ts`: entry point; registers tools and connects the stdio transport.
  - `cdp.ts`: direct CDP client (HTTP discovery, WebSocket connection, reconnect, events, onConnect).
  - `tools.ts`: UI tool implementations + page-context functions injected via `Runtime.evaluate`,
    plus the `ConsoleBuffer`.
  - `debugger.ts`: the `DebuggerSession` (V8 Debugger domain) + `game_debug_*` tools.
  - `shared.ts`: result builders (text/errorText/toErrorResult), RemoteObject/EvaluateResult types.
  - `config.ts`: `CS2_GAMEFACE_*` env config.
- `server/dist/server.mjs`: the shipped, self-contained bundle. COMMITTED on purpose (zero-install).
- `docs/ROADMAP.md`: planned facets.

## Commands

Use `mise` / `bun`, never `npx`.

- `mise run build`: install deps and rebuild `server/dist/server.mjs`. Run this after ANY change
  under `server/src/` and commit the updated bundle.
- `mise run typecheck` (or `cd server && bun run typecheck`): type-check the server.

## How the server is built and shipped

The server uses `@modelcontextprotocol/sdk` + `zod`, bundled with `bun build --target=node` into a
single `server/dist/server.mjs` that runs under bun or node 22+ (both provide global `WebSocket` /
`fetch`). The SDK/zod are bundled in, so there is NO runtime install step. Those packages are
build-time `devDependencies` only.

## Gameface CDP gotchas (verified, do not relearn the hard way)

- Discover the page target from `GET /json/list`; build the WS URL yourself as
  `ws://host:port/devtools/page/<id>`. The `webSocketDebuggerUrl` field is malformed.
- `Runtime.evaluate` (returnByValue) works immediately. `Page.captureScreenshot` needs `Page.enable`.
- CDP `Input.*` (mouse AND key) is ACCEPTED but does NOT reach the UI. All input tools (`game_click`,
  `game_fill`, `game_type`, `game_hover`) dispatch real bubbling DOM events in-page
  (`el.dispatchEvent(new MouseEvent('click', {bubbles:true,...}))`); React's delegated handlers pick
  them up. `HTMLElement.click()` does not exist in Cohtml. `PointerEvent` and `InputEvent`
  constructors are missing too (dispatch pointer* as `MouseEvent`, use `Event('input')`); but
  `KeyboardEvent`, `MouseEvent`, and the native `HTMLInputElement` value setter DO exist.
- `getBoundingClientRect()` returns 0x0 for a node measured in the same eval tick it was inserted.
- Page-context functions in `tools.ts` are serialized via `.toString()`; keep them self-contained
  plain browser JS (no references outside their body). Do not enable minification in the build.
- The V8 `Debugger` domain works (breakpoints, paused events, evaluateOnCallFrame, stepping). Hitting
  a breakpoint or `Debugger.pause` FREEZES the UI thread until resume. Safety net: closing the CDP
  connection auto-resumes a paused UI (verified). CDP line numbers are 0-based; the `game_debug_*`
  tools expose 1-based lines.

## Boundaries and style

- Make the smallest safe change. Ask before adding a runtime dependency or reworking architecture.
- 100-character line limit, comments included. NEVER use em dashes in code, comments, or docs.
- Prefer editing existing files over creating parallel abstractions.
- After changing `server/src/`, run `mise run typecheck` and `mise run build`.
- Store hard-won facts about the game's internals in memory.
