# AGENTS.md

## Plugin overview

`coherent-gameface` is a **generic** toolkit for driving a running **Coherent Gameface** UI (the HTML/CSS/JS UI engine, Cohtml, that many games embed) over a direct Chrome DevTools Protocol (CDP) WebSocket.
It ships an MCP server (evaluate JS, screenshot, inspect and drive the DOM, capture the console, set JS breakpoints) plus skills.
It targets any Gameface application, but is developed and verified against **Cities: Skylines II**'s Gameface UI, which is the reference implementation and the source of the CDP quirks documented below.

The plugin wears two hats, with distinct names:

- The plugin (this directory, named `coherent-gameface` in both its `.claude-plugin/plugin.json` and `.codex-plugin/plugin.json`): launches the committed server bundle (zero-install, offline, version-locked) from its `.mcp.json` on Claude Code and from its `.codex-plugin/mcp.json` on Codex CLI, and carries the skills (`skills/`).
- The MCP server (`mcp/` workspace) is also a standalone product for ANY MCP client, published on npm as **`@csmodding/gameface-devtools-mcp`** (handshake name `gameface-devtools-mcp`, bin `gameface-devtools-mcp`, launched via `npx -y @csmodding/gameface-devtools-mcp@latest`). Its npm-facing product page is the workspace's `README.md`. Publishing is manual (`mise publish`).

## Directory structure

- `package.json`: private release-please version anchor; NOT a bun workspace package.
- `.claude-plugin/plugin.json`: Claude Code plugin manifest.
- `.codex-plugin/plugin.json`: Codex CLI plugin manifest; points `mcpServers` at `.codex-plugin/mcp.json`. Shared fields must match the Claude manifest.
- `.mcp.json`: wires the `gameface` MCP server for Claude Code plugin installs. Launches the committed bundle with `${GAMEFACE_MCP_RUNTIME:-node}`; node 22.4+ is the sole supported runtime (the env var is an escape hatch).
- `skills/gameface/`: the Gameface domain-knowledge skill (`SKILL.md`, `references/`, and the `scripts/fetch-doc.mjs` docs extractor). AUTHORING CONVENTION: prose in `skills/**` markdown is written one sentence per line, never wrapped at 100 chars; it costs fewer tokens when loaded into context and keeps diffs line-granular.
- `mcp/src/`: the MCP server (TypeScript). `mcp/package.json` is the publishable npm package (`@csmodding/gameface-devtools-mcp`); `mcp/README.md` is what npm displays.
  - `server.ts`: entry point; registers tools and connects the stdio transport.
  - `cdp.ts`: direct CDP client (HTTP discovery, WebSocket connection, reconnect, events, onConnect).
  - `tools.ts`: UI tool implementations + page-context functions injected via `Runtime.evaluate`, plus the `ConsoleBuffer`.
  - `debugger.ts`: the `DebuggerSession` (V8 Debugger domain) + `game_debug_*` tools.
  - `shared.ts`: result builders (text/errorText/toErrorResult), RemoteObject/EvaluateResult types.
  - `config.ts`: `GAMEFACE_*` env config.
- `mcp/dist/server.mjs`: the shipped, self-contained bundle. COMMITTED on purpose (zero-install). Also the file npm publishes (`files: ["dist"]`) and the package's `bin`.

## How the server is built and shipped

The server uses `@modelcontextprotocol/sdk` + `zod`, bundled with `bun build --target=node` into a single `dist/server.mjs` (inside the mcp workspace) that runs under node 22.4+ (global `WebSocket` / `fetch` are stable from that version). Bun is dev tooling only (build, package manager); the shipped runtime is node. Unit tests target node 24 and the current LTS.
The SDK/zod are bundled in, so there is NO runtime installation step.
Those packages are build-time `devDependencies` only.
The build emits a `#!/usr/bin/env node` banner so the same bundle works as the npm package's `bin` script.
Never enable minification (page-context functions are serialized via `.toString()`).

Rebuild with `mise build:gameface` and commit the result.

## Harness wiring specifics

- On Claude Code, `.mcp.json` launches the bundle with `${GAMEFACE_MCP_RUNTIME:-node}` and passes the `GAMEFACE_HOST`/`GAMEFACE_PORT` env with defaults.
- On Codex, `.codex-plugin/mcp.json` hardcodes `node` (no `GAMEFACE_MCP_RUNTIME` escape hatch there) and passes no env, so the server falls back to `mcp/src/config.ts` defaults. The only user override path on Codex is registering the npm server manually via `codex mcp add`, which replaces the plugin's copy under the same name.

## Gameface CDP gotchas (verified, do not relearn the hard way)

These were verified against Cities: Skylines II's Gameface UI (Cohtml 1.64.0.7, V8 9.4, CDP 1.3) but reflect Gameface/Cohtml behavior in general. The essentials are below; the full verified matrix (CDP domain support, in-page DOM API availability, input-dispatch details, the JS debugger, and view-reload detection) lives in the `gameface` and `gameface-driving` skills under `skills/`, which are the canonical source and teach it to agents at runtime.

- Discover the page target from `GET /json/list`; build the WS URL yourself as `ws://host:port/devtools/page/<id>`. The `webSocketDebuggerUrl` field is malformed.
- `Runtime.evaluate` (returnByValue) works immediately. `Page.captureScreenshot` needs `Page.enable`.
- CDP `Input.*` (mouse AND key) is ACCEPTED but does NOT reach the UI. All input tools (`game_click`, `game_fill`, `game_type`, `game_hover`) dispatch real bubbling DOM events in-page (`el.dispatchEvent(new MouseEvent('click', {bubbles:true,...}))`); the UI framework's delegated handlers pick them up. `HTMLElement.click()` does not exist in Cohtml. `PointerEvent` and `InputEvent` constructors are missing too (dispatch pointer* as `MouseEvent`, use `Event('input')`); but `KeyboardEvent`, `MouseEvent`, and the native `HTMLInputElement` value setter DO exist.
- `getBoundingClientRect()` returns 0x0 for a node measured in the same eval tick it was inserted.
- Page-context functions in `tools.ts` are serialized via `.toString()`; keep them self-contained plain browser JS (no references outside their body). Do not enable minification in the build.
- The V8 `Debugger` domain works (breakpoints, paused events, evaluateOnCallFrame, stepping). Hitting a breakpoint or `Debugger.pause` FREEZES the UI thread until resume. Safety net: closing the CDP connection auto-resumes a paused UI (verified). CDP line numbers are 0-based; the `game_debug_*` tools expose 1-based lines.

## Preferred agent behavior

- After changing `mcp/src/`, run `mise check:agents` and `mise build:gameface`. The running `gameface` MCP server keeps serving the old bundle after a rebuild; ask the user to hit Reconnect in `/mcp` whenever you need to get the new build. Ask in plain text and end your turn: the user cannot run `/mcp` while an AskUserQuestion prompt is pending. Server launch/connection failures (e.g. `MCP error -32000: Connection closed`, an SDK process-exit/spawn error, not a CDP error) are diagnosable from the newest `.jsonl` under `%LocalAppData%\claude-cli-nodejs\Cache\C--Users-Morgan-Documents-Projets-coherent-gameface-mcp\mcp-logs-gameface\`.
- Store hard-won facts about Gameface internals in memory.
- Keep the server generic: no assumptions about a specific game's DOM and APIs beyond the defaults. CS2 is the test target, not a hard dependency.
- Keep skills and docs generic too (`skills/**`, `docs/`, `README`s): the plugin targets any Gameface application, so write the general behavior as the guidance and the rule. CS2 is where facts are verified, not the lesson: state what holds for any Gameface UI, and keep CS2-only specifics (a concrete DOM path or class, an engine detail like Unity/`Colossal.*`, a binding name such as `menu.setActiveScreen`, a decompiled-source finding) out of the general text. When such a specific genuinely aids understanding, demote it to a clearly labeled example ("verified on CS2: ...") rather than letting it become the framing, and prefer none at all in a section meant as general procedure. The gameface engine itself (Cohtml/Coherent APIs, `engine.trigger`) is in-domain and generic; a particular game's use of it is not.
