# coherent-gameface-claude-plugin

A Claude Code plugin: a **generic** toolkit for driving a running **Coherent Gameface** UI.

[Coherent Gameface](https://coherent-labs.com/products/coherent-gameface/) (Cohtml) is the
HTML/CSS/JS UI engine many games embed.
This plugin ships **[gameface-devtools-mcp](mcp/README.md)**, an MCP server that lets Claude drive
any Gameface UI over a **direct Chrome DevTools Protocol (CDP)** connection: evaluate JavaScript,
take screenshots, inspect and drive the DOM, capture the console, and set JS breakpoints.
Skills are planned on top, see [`docs/ROADMAP.md`](docs/ROADMAP.md).

> [!NOTE]
> **Generic, but developed against Cities: Skylines II.** The server makes no assumptions about a
> specific application; it works against any Gameface CDP endpoint. It is developed and verified
> against Cities: Skylines II's Gameface UI, which is the reference target.

> [!TIP]
> **Not using Claude Code?** The MCP server is also published on npm as
> [`@csmodding/gameface-devtools-mcp`](https://www.npmjs.com/package/@csmodding/gameface-devtools-mcp)
> and works with any MCP client (Cursor, Codex CLI, Gemini CLI, VS Code, ...).
> See [`mcp/README.md`](mcp/README.md) for the tool reference and per-client install snippets.

## Requirements

- **A Gameface application running** with its CDP debug endpoint reachable (default
  `http://localhost:9444`). Verify with:
  ```sh
  curl http://localhost:9444/json/list
  ```
  You should get back a JSON array containing a `"type": "page"` target. Set the host/port to match
  your application if it differs (see Configuration).
- **Node 22.4+** to launch the server.
  No `npm install` is needed: the plugin launches the server from a committed, self-contained
  bundle, so it works offline and stays version-locked to the plugin.

## Install

Add this repository as a plugin (e.g., via your plugin marketplace or a local path). Once enabled,
Claude Code autoloads the `gameface` MCP server from [`.mcp.json`](.mcp.json).
Run `/mcp` to confirm it connected, then ask Claude to use the `game_*` tools.

The full tool reference (UI tools + JS debugger tools) lives in [`mcp/README.md`](mcp/README.md).

## Configuration

All are optional, surfaced in [`.mcp.json`](.mcp.json):

| Variable                      | Default     | Purpose                                  |
| ----------------------------- | ----------- | ---------------------------------------- |
| `GAMEFACE_MCP_RUNTIME`        | `node`      | Runtime used to launch the server.       |
| `GAMEFACE_HOST`               | `localhost` | Host of the Gameface CDP endpoint.       |
| `GAMEFACE_PORT`               | `9444`      | Port of the Gameface CDP endpoint.       |
| `GAMEFACE_CONNECT_TIMEOUT_MS` | `5000`      | HTTP discovery / WebSocket open timeout. |
| `GAMEFACE_CALL_TIMEOUT_MS`    | `15000`     | Per-command reply timeout.               |

## Development

Uses `mise` + `bun` (never `npx`). The repository is a bun workspace: the root `package.json`
carries the lint/format tooling (oxlint, oxfmt) and the lefthook git hooks, while the MCP server
lives in the `mcp/` workspace package (published on npm as `@csmodding/gameface-devtools-mcp`).

```sh
bun install   # install all workspace deps (also installs the git hooks)
mise check    # type-check, lint (with safe auto-fixes), and format
mise build    # rebuild mcp/dist/server.mjs (commit the result)
```

Run `mise tasks` for the full list.

After changing anything under `mcp/src/`, run `mise check`, rebuild, and commit the updated
`mcp/dist/server.mjs`.

## Troubleshooting

- **`/mcp` shows the server failed / tools error with "Cannot reach ..."**: the Gameface application
  is not running or the debug port is not reachable. Check `curl http://localhost:9444/json/list`.
  Use `game_status` for a structured diagnosis.
- **Runtime not found**: ensure `node` (22.4+) is on your `PATH`.
- **Read the MCP server logs**: Claude Code records each server's connection attempts and captured
  stderr to per-project JSONL files, the fastest way to see why a launch failed (e.g. a
  `-32000 Connection closed` from a bad command/path before any `game_*` tool runs). They live under
  the Claude CLI cache, in an `mcp-logs-gameface/` folder keyed by the project path (separators
  replaced with `-`); newest `.jsonl` first, and each `Server stderr: ...` line is what the server
  printed:
  - Windows: `%LocalAppData%\claude-cli-nodejs\Cache\<project-path>\mcp-logs-gameface\`
  - macOS / Linux: `~/.cache/claude-cli-nodejs/Cache/<project-path>/mcp-logs-gameface/`
