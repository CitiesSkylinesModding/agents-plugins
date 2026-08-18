# AGENTS.md

## Plugin overview

`coherent-gameface` is a **generic** toolkit for driving a running **Coherent Gameface** UI (the HTML/CSS/JS UI engine, Cohtml, that many games embed) over a direct Chrome DevTools Protocol (CDP) WebSocket.
It ships an MCP server (evaluate JS, screenshot, inspect and drive the DOM, capture the console, set JS breakpoints) plus skills.
It targets any Gameface application, but is developed and verified against **Cities: Skylines II**'s UI, the reference implementation and the source of the documented CDP quirks.

The plugin wears two hats, with distinct names:

- The plugin (this directory) launches the committed server bundle — zero-install, offline, version-locked — from `.mcp.json` on Claude Code and `.codex-plugin/mcp.json` on Codex CLI, and carries `skills/`.
- The MCP server (`mcp/`) is also a standalone product for ANY MCP client, published on npm as **`@csmodding/gameface-devtools-mcp`** (handshake name and bin `gameface-devtools-mcp`, run via `npx -y @csmodding/gameface-devtools-mcp@latest`). `mcp/README.md` is its npm product page. Publishing is manual (`mise publish`).

## Directory structure

- `package.json`: private release-please version anchor; NOT a bun workspace package.
- `.claude-plugin/` + `.codex-plugin/` + `.mcp.json`: the two harness manifest sets (see the root AGENTS.md).
- `skills/gameface/`: the domain-knowledge skill — what the engine supports (layout, events, platform APIs), with `references/` and the `scripts/fetch-doc.mjs` docs extractor.
- `skills/gameface-driving/`: the operational skill for driving a live UI with the `game_*` tools.
- `mcp/src/`: the server (TypeScript). `mcp/package.json` is the publishable npm package.
- `mcp/tests/`: the server's unit tests (`mise test:gameface`), hermetic and typed against Bun through their own tsconfig. A facet reaches them either by taking an injected CDP facade, which is what makes it testable without a live application (the console pipeline and the debugger session), or by being pure to begin with (the selector diagnosis).
- `mcp/dist/server.mjs`: the shipped self-contained bundle. COMMITTED on purpose (zero-install); also what npm publishes and the package's `bin`.

## Conventions

- **One tier per fact.** Traps every caller needs go in the tool descriptions (always in context once the tools load, and all a standalone MCP client gets); interpretation and procedure go in the skill.
- **Skills and docs stay generic.** State what holds for any Gameface UI. The engine itself (Cohtml/Coherent APIs, `engine.trigger`) is in-domain; a particular game's use of it is not. Where a CS2 specific genuinely aids understanding, demote it to a labelled example ("verified on CS2: …") instead of letting it frame the section — and prefer none at all in general procedure.
- **One sentence per line** in `skills/**` markdown, never wrapped at 100 chars: fewer tokens in context, line-granular diffs.
- **Report the size that predicts the cost.** A tool returning text takes a character budget with a default (`game_dom`'s `maxHtml`, `game_debug_source`'s `maxChars`) and marks what it clipped; a line or element count lets a caller walk into a megabyte.

## Build and shipping

`bun build --target=node` bundles `@modelcontextprotocol/sdk` + `zod` (build-time devDependencies) into a single `dist/server.mjs` with a `#!/usr/bin/env node` banner, so there is NO runtime install step and the same file serves as the npm `bin`.
Bun is dev tooling only; the shipped runtime is node 22.4+, where global `WebSocket`/`fetch` are stable.
Never enable minification: page-context functions are injected by `.toString()`.

Rebuild with `mise build:gameface` and commit the result.

Harness wiring: Claude Code's `.mcp.json` launches the bundle through `${GAMEFACE_MCP_RUNTIME:-node}` and passes `GAMEFACE_HOST`/`GAMEFACE_PORT`; Codex hardcodes `node` and passes no env, so the server falls back to `mcp/src/config.ts` defaults.

## Gameface CDP behavior

The verified matrix — CDP domain support, in-page DOM API availability, input dispatch, the JS debugger, view-reload detection — lives in `skills/gameface/` and `skills/gameface-driving/`, which are the canonical source and teach it to agents at runtime.
The selector whitelist is the one fact the server also holds, in `mcp/src/selectors.ts`, because the diagnosis has to answer a standalone MCP client that loads no skill.
Re-probing it for a new engine version updates that module and, in each of the two skills, both the whitelist sentence and the `:nth-child()` argument sentence beside it: a test binds the module's two copies to each other, and nothing binds the skills.
Server-side consequences are commented where they are implemented (`cdp.ts` for discovery, `tools.ts` for page-context functions).
Verification context: Cohtml 1.64.0.7, V8 9.4, CDP 1.3.

## Preferred agent behavior

- After changing `mcp/src/`, run `mise check:agents` and `mise build:gameface`. The running server keeps serving the old bundle: ask the user in plain text to hit Reconnect in `/mcp`, then end your turn — they cannot run `/mcp` while an AskUserQuestion prompt is pending.
- Server launch failures (`MCP error -32000: Connection closed`, spawn/process-exit errors — not CDP errors) are diagnosable with `mise dev:mcp-logs`.
- Keep the server generic: no assumptions about a specific game's DOM or APIs beyond the defaults.
