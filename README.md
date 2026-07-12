# coherent-gameface-mcp

A Claude Code plugin: a **generic** toolkit for driving a running **Coherent Gameface** UI.

[Coherent Gameface](https://coherent-labs.com/products/coherent-gameface/) (Cohtml) is the
HTML/CSS/ JS UI engine many games embed.
This plugin ships an MCP server that lets Claude drive any Gameface UI over a **direct Chrome
DevTools Protocol (CDP)** connection: evaluate JavaScript, take screenshots, inspect and drive the
DOM, capture the console, and set JS breakpoints.
Skills are planned on top, see [`docs/ROADMAP.md`](docs/ROADMAP.md).

> [!NOTE]
> **Generic, but developed against Cities: Skylines II.** The server makes no assumptions about a
> specific application; it works against any Gameface CDP endpoint. It is developed and verified
> against Cities: Skylines II's Gameface UI, which is the reference target and the source of the CDP
> quirks noted below.

> [!NOTE]
> **Why direct CDP instead of Puppeteer/Playwright (or chrome-devtools-mcp)?** Gameface does not
> implement the browser-level handshake those tools rely on (`Browser.getVersion` is missing and
> `Target.attachedToTarget` is never emitted), so they connect but find zero drivable pages.
> A direct CDP client only sends the commands Gameface supports, and works end-to-end.

## Requirements

- **A Gameface application running** with its CDP debug endpoint reachable (default
  `http://localhost:9444`). Verify with:
  ```sh
  curl http://localhost:9444/json/list
  ```
  You should get back a JSON array containing a `"type": "page"` target. Set the host/port to match
  your application if it differs (see Configuration).
- A JavaScript runtime to launch the server: **Bun** (default) or **Node 24+**.
  No `npm install` is needed: the server is shipped as a self-contained bundle.

## Install

Add this repository as a plugin (e.g., via your plugin marketplace or a local path). Once enabled,
Claude Code autoloads the `gameface` MCP server from [`.mcp.json`](.mcp.json).
Run `/mcp` to confirm it connected, then ask Claude to use the `game_*` tools.

## Tools

| Tool              | What it does                                                                    | Under the hood                      |
| ----------------- | ------------------------------------------------------------------------------- | ----------------------------------- |
| `game_status`     | Reachability + page target + engine info. Run first when things fail.           | `/json/list` + `/json/version`      |
| `game_eval`       | Evaluate a JS expression in the Gameface UI, returns the value as JSON.         | `Runtime.evaluate` (returnByValue)  |
| `game_screenshot` | Screenshot the viewport (or a selector's box) as an inline image.               | `Page.captureScreenshot` (+ `clip`) |
| `game_dom`        | DOM details (tag, classes, attributes, rect, outerHTML) for a CSS selector.     | `Runtime.evaluate`                  |
| `game_wait`       | Wait until a selector matches (optionally visible) or a JS predicate is truthy. | polled `Runtime.evaluate`           |
| `game_click`      | Click an element by dispatching real bubbling DOM events.                       | `Runtime.evaluate` (see note)       |
| `game_fill`       | Set an input/textarea/contenteditable value.                                    | `Runtime.evaluate` (see note)       |
| `game_type`       | Type text key by key (real KeyboardEvents + value sync).                        | `Runtime.evaluate` (see note)       |
| `game_hover`      | Hover an element (over/enter/move sequence) to trigger tooltips/hover state.    | `Runtime.evaluate` (see note)       |
| `game_console`    | Recent `console.*`, log entries, and uncaught exceptions from the Gameface UI.  | `Log` + `Runtime.consoleAPICalled`  |

> [!NOTE]
> **Input is done via DOM events, not CDP `Input`.** Gameface accepts `Input.dispatchMouseEvent` /
> `dispatchKeyEvent` but never delivers them to the UI. So `game_click`, `game_fill`, `game_type`,
> and `game_hover` dispatch real DOM events in the page.

### JS debugger tools

The Gameface UI's V8 `Debugger` domain is fully supported, so these drive a real source-level
debugger.

| Tool                           | What it does                                                                                                  |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------- |
| `game_debug_status`            | Debugger state: paused?/where, pause-on-exceptions, breakpoints, script count. Also sets pause-on-exceptions. |
| `game_debug_scripts`           | List parsed UI scripts (scriptId + url), filterable by url substring.                                         |
| `game_debug_source`            | Get a script's source (by scriptId) with line numbers, optionally a line range.                               |
| `game_debug_set_breakpoint`    | Break at url-substring + line (1-based), with an optional JS condition.                                       |
| `game_debug_remove_breakpoint` | Remove a breakpoint by id, or `all`.                                                                          |
| `game_debug_pause_state`       | When paused: the call stack, optionally with each frame's local/closure variables.                            |
| `game_debug_evaluate`          | Evaluate in the paused frame's scope (read locals), or globally when running.                                 |
| `game_debug_step`              | `resume` / `over` / `into` / `out` / `pause`.                                                                 |

> [!IMPORTANT]
> **Hitting a breakpoint or pausing FREEZES the UI thread until you resume** (`game_debug_step` with
> `resume`). Prefer conditional breakpoints to limit freezes, and while paused inspect with
> `game_debug_evaluate` rather than `game_eval`. Safety net: if the server's connection drops while
> paused, the engine auto-resumes.

## Configuration

All are optional, read by the server (and surfaced in `.mcp.json`):

| Variable                      | Default     | Purpose                                                       |
| ----------------------------- | ----------- | ------------------------------------------------------------- |
| `GAMEFACE_MCP_RUNTIME`        | `bun`       | Runtime used to launch the server. Set to `node` to use Node. |
| `GAMEFACE_HOST`               | `localhost` | Host of the Gameface CDP endpoint.                            |
| `GAMEFACE_PORT`               | `9444`      | Port of the Gameface CDP endpoint.                            |
| `GAMEFACE_CONNECT_TIMEOUT_MS` | `5000`      | HTTP discovery / WebSocket open timeout.                      |
| `GAMEFACE_CALL_TIMEOUT_MS`    | `15000`     | Per-command reply timeout.                                    |

## Development

Uses `mise` + `bun` (never `npx`). The repository is a bun workspace: the root `package.json`
carries the lint/format tooling (oxlint, oxfmt) and the lefthook git hooks, while the MCP server
lives in the `server/` workspace package.

```sh
bun install   # install all workspace deps (also installs the git hooks)
mise check    # type-check, lint (with safe auto-fixes), and format
mise build    # rebuild server/dist/server.mjs (commit the result)
```

Run `mise tasks` for the full list.

After changing anything under `server/src/`, run `mise check`, rebuild, and commit the updated
`server/dist/server.mjs`.

## Troubleshooting

- **`/mcp` shows the server failed / tools error with "Cannot reach ..."**: the Gameface application
  is not running or the debug port is not reachable. Check `curl http://localhost:9444/json/list`.
  Use `game_status` for a structured diagnosis.
- **Runtime not found**: ensure `bun` (or `node`, with `GAMEFACE_MCP_RUNTIME=node`) is on your
  `PATH`.
- **Read the MCP server logs**: Claude Code records each server's connection attempts and captured
  stderr to per-project JSONL files, the fastest way to see why a launch failed (e.g. a
  `-32000 Connection closed` from a bad command/path before any `game_*` tool runs). They live under
  the Claude CLI cache, in an `mcp-logs-gameface/` folder keyed by the project path (separators
  replaced with `-`); newest `.jsonl` first, and each `Server stderr: ...` line is what the server
  printed:
  - Windows: `%LocalAppData%\claude-cli-nodejs\Cache\<project-path>\mcp-logs-gameface\`
  - macOS / Linux: `~/.cache/claude-cli-nodejs/Cache/<project-path>/mcp-logs-gameface/`
