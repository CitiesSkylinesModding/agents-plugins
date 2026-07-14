# gameface-devtools-mcp

An MCP server that drives a running **Coherent Gameface** UI, for any MCP client.

[Coherent Gameface](https://coherent-labs.com/products/coherent-gameface/) (Cohtml) is the
HTML/CSS/JS UI engine many games embed.
This server connects to its **Chrome DevTools Protocol (CDP)** debug endpoint directly and exposes
MCP tools to evaluate JavaScript, take screenshots, inspect and drive the DOM, capture the console,
and set JS breakpoints.

> **Generic, but developed against Cities: Skylines II.** The server makes no assumptions about a
> specific application; it works against any Gameface CDP endpoint. It is developed and verified
> against Cities: Skylines II's Gameface UI, which is the reference target and the source of the
> CDP quirks noted below.

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
- **Node.js 22.4+**. The package is a self-contained bundle; `npx` installs nothing else.

## Install

The server speaks MCP over stdio. Register it in your client under the name `gameface`, launched
as `npx -y @csmodding/gameface-devtools-mcp@latest` (`@latest` keeps it fresh on each launch).

Most clients accept the canonical `mcpServers` JSON shape:

```json
{
  "mcpServers": {
    "gameface": {
      "command": "npx",
      "args": ["-y", "@csmodding/gameface-devtools-mcp@latest"],
      "env": {
        "GAMEFACE_HOST": "localhost",
        "GAMEFACE_PORT": "9444"
      }
    }
  }
}
```

The `env` block is optional; defaults are shown (see Configuration for all variables).

### Claude Code

```sh
claude mcp add gameface -- npx -y @csmodding/gameface-devtools-mcp@latest
```

Add `--scope project` to share it with your team via a committed `.mcp.json`. To pass env vars,
note that another option must sit between `--env` and the server name:

```sh
claude mcp add --env GAMEFACE_PORT=9444 --transport stdio gameface -- npx -y @csmodding/gameface-devtools-mcp@latest
```

Claude Code and Codex CLI users can also install the
[coherent-gameface plugin](https://github.com/CitiesSkylinesModding/coherent-gameface-agent-plugin)
instead, which bundles this server (no npm involved) plus skills that teach the agent the Gameface
workflows.

### Cursor

Add the canonical JSON above to `.cursor/mcp.json` in your project, or `~/.cursor/mcp.json`
globally.

### Codex CLI

```sh
codex mcp add gameface -- npx -y @csmodding/gameface-devtools-mcp@latest
```

Or in `~/.codex/config.toml`:

```toml
[mcp_servers.gameface]
command = "npx"
args = ["-y", "@csmodding/gameface-devtools-mcp@latest"]
```

Codex CLI users can also install the
[coherent-gameface plugin](https://github.com/CitiesSkylinesModding/coherent-gameface-agent-plugin)
instead (see the note under Claude Code above).

### Gemini CLI

```sh
gemini mcp add gameface npx -- -y @csmodding/gameface-devtools-mcp@latest
```

(The `--` goes after `npx`: it separates dash-prefixed server args from Gemini's own flags.)
Or add the canonical JSON above to `.gemini/settings.json` (project) or `~/.gemini/settings.json`.

### VS Code / GitHub Copilot

```sh
code --add-mcp "{\"name\":\"gameface\",\"command\":\"npx\",\"args\":[\"-y\",\"@csmodding/gameface-devtools-mcp@latest\"]}"
```

Or in `.vscode/mcp.json` (note the `servers` key, not `mcpServers`):

```json
{
  "servers": {
    "gameface": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "@csmodding/gameface-devtools-mcp@latest"]
    }
  }
}
```

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

> **Hitting a breakpoint or pausing FREEZES the UI thread until you resume** (`game_debug_step`
> with `resume`). Prefer conditional breakpoints to limit freezes, and while paused inspect with
> `game_debug_evaluate` rather than `game_eval`. Safety net: if the server's connection drops while
> paused, the engine auto-resumes.

## Configuration

All are optional, read by the server from the environment:

| Variable                      | Default     | Purpose                                  |
| ----------------------------- | ----------- | ---------------------------------------- |
| `GAMEFACE_HOST`               | `localhost` | Host of the Gameface CDP endpoint.       |
| `GAMEFACE_PORT`               | `9444`      | Port of the Gameface CDP endpoint.       |
| `GAMEFACE_CONNECT_TIMEOUT_MS` | `5000`      | HTTP discovery / WebSocket open timeout. |
| `GAMEFACE_CALL_TIMEOUT_MS`    | `15000`     | Per-command reply timeout.               |

## Troubleshooting

- **Tools error with "Cannot reach ..."**: the Gameface application is not running or the debug
  port is not reachable. Check `curl http://localhost:9444/json/list`. Use `game_status` for a
  structured diagnosis.
- **The server fails to launch**: check your client's MCP logs for the server's stderr. Common
  causes: `npx` not on `PATH`, or Node older than 22.4 (the bundle needs the global `WebSocket`).

## Development

The server is developed in the
[coherent-gameface-agent-plugin](https://github.com/CitiesSkylinesModding/coherent-gameface-agent-plugin)
repository, where this package lives as the `mcp/` workspace. See the repository README for the
development setup, and its `AGENTS.md` for the verified Gameface CDP behavior matrix.
