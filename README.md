# cs2-modkit

A Claude Code plugin: a toolkit for **Cities: Skylines II** mod development.

Its first facet is an MCP server that lets Claude drive the running mod UI (the game's
**Coherent Gameface** interface) over a **direct Chrome DevTools Protocol (CDP)** connection:
evaluate JavaScript, take screenshots, inspect the DOM, and click elements. More facets
(C# debugging, a decompiled-source retro-engineering subagent) are planned, see
[`docs/ROADMAP.md`](docs/ROADMAP.md).

> Why direct CDP instead of Puppeteer/Playwright (or chrome-devtools-mcp)? Gameface does not
> implement the browser-level handshake those tools rely on (`Browser.getVersion` is missing and
> `Target.attachedToTarget` is never emitted), so they connect but find zero drivable pages. A
> direct CDP client only sends the commands Gameface supports, and works end-to-end.

## Requirements

- **Cities: Skylines II running** with the Gameface CDP debug endpoint reachable (default
  `http://localhost:9444`). Verify with:
  ```sh
  curl http://localhost:9444/json/list
  ```
  You should get back a JSON array containing a `"type": "page"` target.
- A JavaScript runtime to launch the server: **Bun** (default) or **Node 22+** (both ship a global
  `WebSocket`). No `npm install` is needed: the server is shipped as a committed, self-contained
  bundle.

## Install

Add this repository as a plugin (e.g. via your plugin marketplace or a local path). Once enabled,
Claude Code auto-loads the `gameface` MCP server from [`.mcp.json`](.mcp.json). Run `/mcp` to confirm
it connected, then ask Claude to use the `game_*` tools.

## Tools

| Tool | What it does | Under the hood |
|---|---|---|
| `game_status` | Reachability + page target + engine info. Run first when things fail. | `/json/list` + `/json/version` |
| `game_eval` | Evaluate a JS expression in the mod UI, returns the value as JSON. | `Runtime.evaluate` (returnByValue) |
| `game_screenshot` | Screenshot the viewport (or a selector's box) as an inline image. | `Page.captureScreenshot` (+ `clip`) |
| `game_dom` | DOM details (tag, classes, attributes, rect, outerHTML) for a CSS selector. | `Runtime.evaluate` |
| `game_wait` | Wait until a selector matches (optionally visible) or a JS predicate is truthy. | polled `Runtime.evaluate` |
| `game_click` | Click an element by dispatching real bubbling DOM events. | `Runtime.evaluate` (see note) |
| `game_fill` | Set an input/textarea/contenteditable value (fires input/change for React). | `Runtime.evaluate` (see note) |
| `game_type` | Type text key by key (real KeyboardEvents + value sync). | `Runtime.evaluate` (see note) |
| `game_hover` | Hover an element (over/enter/move sequence) to trigger tooltips/hover state. | `Runtime.evaluate` (see note) |
| `game_console` | Recent console.*, log entries, and uncaught exceptions from the mod UI. | `Log` + `Runtime.consoleAPICalled` |

> **Input is done via DOM events, not CDP `Input`.** Gameface accepts `Input.dispatchMouseEvent` /
> `dispatchKeyEvent` but never delivers them to the UI. So `game_click`, `game_fill`, `game_type`,
> and `game_hover` dispatch real DOM events in the page, which React's delegated handlers receive.
> Caveat: these target native `<input>`/`<textarea>`/`contenteditable` and standard React handlers;
> fully custom widgets may need a tailored `game_eval`.

### JS debugger tools

The mod UI's V8 `Debugger` domain is fully supported, so these drive a real source-level debugger.

| Tool | What it does |
|---|---|
| `game_debug_status` | Debugger state: paused?/where, pause-on-exceptions, breakpoints, script count. Also sets pause-on-exceptions. |
| `game_debug_scripts` | List parsed UI scripts (scriptId + url), filterable by url substring. |
| `game_debug_source` | Get a script's source (by scriptId) with line numbers, optionally a line range. |
| `game_debug_set_breakpoint` | Break at url-substring + line (1-based), with an optional JS condition. |
| `game_debug_remove_breakpoint` | Remove a breakpoint by id, or `all`. |
| `game_debug_pause_state` | When paused: the call stack, optionally with each frame's local/closure variables. |
| `game_debug_evaluate` | Evaluate in the paused frame's scope (read locals), or globally when running. |
| `game_debug_step` | `resume` / `over` / `into` / `out` / `pause`. |

> ⚠️ **Hitting a breakpoint or pausing FREEZES the UI thread until you resume** (`game_debug_step`
> with `resume`). Prefer conditional breakpoints to limit freezes, and while paused inspect with
> `game_debug_evaluate` rather than `game_eval`. Safety net: if the server's connection drops while
> paused, the engine auto-resumes.

## Configuration

All optional, read by the server (and surfaced in `.mcp.json`):

| Variable | Default | Purpose |
|---|---|---|
| `CS2_MCP_RUNTIME` | `bun` | Runtime used to launch the server. Set to `node` to use Node. |
| `CS2_GAMEFACE_HOST` | `localhost` | Host of the Gameface CDP endpoint. |
| `CS2_GAMEFACE_PORT` | `9444` | Port of the Gameface CDP endpoint. |
| `CS2_GAMEFACE_CONNECT_TIMEOUT_MS` | `5000` | HTTP discovery / WebSocket open timeout. |
| `CS2_GAMEFACE_CALL_TIMEOUT_MS` | `15000` | Per-command reply timeout. |

## Development

Uses `mise` + `bun` (never `npx`). The server lives in `server/`.

```sh
mise run build       # install deps + rebuild server/dist/server.mjs (commit the result)
mise run typecheck   # type-check the server
```

After changing anything under `server/src/`, rebuild and commit the updated
`server/dist/server.mjs`. See [`CLAUDE.md`](CLAUDE.md) for conventions and the hard-won Gameface
CDP quirks.

## Troubleshooting

- **`/mcp` shows the server failed / tools error with "Cannot reach ..."**: the game is not running
  or the debug port is not reachable. Check `curl http://localhost:9444/json/list`. Use `game_status`
  for a structured diagnosis.
- **Runtime not found**: ensure `bun` (or `node`, with `CS2_MCP_RUNTIME=node`) is on your `PATH`.
- **Read the MCP server logs**: Claude Code records each server's connection attempts and captured
  stderr to per-project JSONL files, the fastest way to see why a launch failed (e.g. a
  `-32000 Connection closed` from a bad command/path before any `game_*` tool runs). They live under
  the Claude CLI cache, in an `mcp-logs-gameface/` folder keyed by the project path (separators
  replaced with `-`); newest `.jsonl` first, and each `Server stderr: ...` line is what the server
  printed:
  - Windows: `%LocalAppData%\claude-cli-nodejs\Cache\<project-path>\mcp-logs-gameface\`
  - macOS / Linux: `~/.cache/claude-cli-nodejs/Cache/<project-path>/mcp-logs-gameface/`
