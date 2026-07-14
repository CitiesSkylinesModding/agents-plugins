# coherent-gameface-agent-plugin

An agent plugin for **Claude Code** and **OpenAI Codex CLI**: a **generic** toolkit for driving a
running **Coherent Gameface** UI.

[Coherent Gameface](https://coherent-labs.com/products/coherent-gameface/) (Cohtml) is the
HTML/CSS/JS UI engine many games embed.
This plugin ships **[gameface-devtools-mcp](mcp/README.md)**, an MCP server that lets your agent
drive any Gameface UI over a **direct Chrome DevTools Protocol (CDP)** connection: evaluate
JavaScript, take screenshots, inspect and drive the DOM, capture the console, and set JS
breakpoints.

It also ships the **`gameface` skill**, which teaches the agent the engine itself: how Gameface
is, and is not, a browser; version-gating features against the changelog; data binding; and how
to probe the running game for what the docs cannot promise.

> [!NOTE]
> **Generic, but developed against Cities: Skylines II.** The server makes no assumptions about a
> specific application; it works against any Gameface CDP endpoint. It is developed and verified
> against Cities: Skylines II's Gameface UI, which is the reference target.

> [!TIP]
> **Not using Claude Code or Codex CLI?** The MCP server is also published on npm as
> [`@csmodding/gameface-devtools-mcp`](https://www.npmjs.com/package/@csmodding/gameface-devtools-mcp)
> and works with any MCP client (Cursor, Gemini CLI, VS Code, …).
> See [`mcp/README.md`](mcp/README.md) for the tool reference and per-client install snippets.

## What it looks like in practice

Short, abridged transcripts. The skill supplies the engine knowledge; the server supplies the
hands.

### "My panel looks right in Chrome but wrong in-game"

> **You:** My settings panel is a two-column grid in Chrome, but in the game everything stacks
> into one column. Why?
>
> **Agent:** Gameface has no CSS Grid in any version, and unsupported CSS is silently ignored:
> your `display: grid` just falls back to the default, and in Gameface every element defaults to
> `display: flex` in column direction, hence the single column. I rewrote the layout with nested
> flex rows, and a `game_screenshot` of `.settings-panel` confirms the two columns now render
> in-game.

### "The UI stops updating after the first refresh"

> **You:** My mod pushes stats to the UI every second, but the panel freezes on the first value.
> No errors anywhere.
>
> **Agent:** `game_console` shows no exceptions, and `game_eval` on the bound nodes shows they
> are stuck on the first payload. This is a known engine gotcha: Cohtml caches a model's shape by
> its type name, and two payloads sharing a type name must expose the same set of PRESENT
> properties. Your second update omits `delta` when nothing changed; an omitted property is a
> different shape than a null one, and the cached binding breaks silently. Send `delta: null`
> instead. Verified in-game across two refresh cycles.

### "Test the new toolbar button end to end"

> **You:** I just built the milestones toolbar button. Can you verify the whole flow in the
> running game?
>
> **Agent:** `game_click` on `[data-tooltip="Milestones"]`: Gameface accepts CDP input commands
> but never delivers them to the UI, so the click is dispatched as real bubbling DOM events,
> which your React handlers receive. `game_wait` until `.milestones-panel` is visible,
> `game_screenshot` to check the render, `game_console` shows no new errors. The flow works;
> screenshot attached.

### "Something throws when the trade panel opens, no idea where"

> **You:** Opening the trade panel logs `TypeError: cannot read 'income' of undefined` and I
> cannot reproduce it outside the game.
>
> **Agent:** `game_console` captures the stack, pointing at `trade-panel.js:412`.
> `game_debug_set_breakpoint` there with the condition `district == null`, then `game_click` on
> the trade button. It pauses; the UI thread is frozen while paused, so I inspect quickly:
> `game_debug_pause_state` shows `district` is undefined exactly when the row is an
> outside connection, then `game_debug_step resume`. Fix: guard the outside-connection case,
> which has no district.

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

Both harnesses install the same plugin from this repository and launch the same committed server
bundle. The full tool reference (UI tools and JS debugger tools) lives in
[`mcp/README.md`](mcp/README.md).

### Claude Code

In Claude Code, add this repository as a marketplace, then install the plugin from it:

```
/plugin marketplace add CitiesSkylinesModding/coherent-gameface-agent-plugin
/plugin install coherent-gameface@csmodding
```

Or from your terminal:

```sh
claude plugin marketplace add CitiesSkylinesModding/coherent-gameface-agent-plugin
claude plugin install coherent-gameface@csmodding
```

Once enabled, Claude Code autoloads the `gameface` MCP server from [`.mcp.json`](.mcp.json).
Run `/mcp` to confirm it connected, then Codex will use this MCP when it needs it.
You can ask it to call `game_status` to check the MCP is working properly.

### Codex CLI

Add this repository as a marketplace, then install the plugin from it:

```sh
codex plugin marketplace add CitiesSkylinesModding/coherent-gameface-agent-plugin
codex plugin add coherent-gameface@csmodding
```

Once enabled, Codex autoloads the `gameface` MCP server from
[`.codex-plugin/mcp.json`](.codex-plugin/mcp.json).
Run `/mcp` to confirm it connected, then Claude will use this MCP when it needs it.
You can ask it to call `game_status` to check the MCP is working properly.

## Configuration

The server reads these environment variables (all optional):

| Variable                      | Default     | Purpose                                  |
| ----------------------------- | ----------- | ---------------------------------------- |
| `GAMEFACE_HOST`               | `localhost` | Host of the Gameface CDP endpoint.       |
| `GAMEFACE_PORT`               | `9444`      | Port of the Gameface CDP endpoint.       |
| `GAMEFACE_CONNECT_TIMEOUT_MS` | `5000`      | HTTP discovery / WebSocket open timeout. |
| `GAMEFACE_CALL_TIMEOUT_MS`    | `15000`     | Per-command reply timeout.               |

**On Claude Code**, [`.mcp.json`](.mcp.json) forwards them from your environment
(`${VAR:-default}`), and an extra `GAMEFACE_MCP_RUNTIME` variable (default `node`) overrides the
runtime used to launch the server.

**On Codex CLI**, the plugin config passes no environment block (Codex does not interpolate
`${VAR}` placeholders, and `~/.codex/config.toml` cannot override a plugin-provided server), so the
server always starts with the defaults above. If you need non-default settings, register the
npm-published server manually with `codex mcp add` and the environment you want; it replaces the
plugin's copy under the same name (see [`mcp/README.md`](mcp/README.md)).

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

- **`/mcp` shows the server failed / tools error with "Cannot reach …"**: the Gameface application
  is not running or the debug port is not reachable. Check `curl http://localhost:9444/json/list`.
  Use `game_status` for a structured diagnosis.
- **Runtime not found**: ensure `node` (22.4+) is on your `PATH`.
- **Read the MCP server logs**: Claude Code records each server's connection attempts and captured
  stderr to per-project `.jsonl` files, the fastest way to see why a launch failed (e.g., a
  `-32000 Connection closed` from a bad command/path before any `game_*` tool runs). They live under
  the Claude CLI cache, in an `mcp-logs-gameface/` folder keyed by the project path (separators
  replaced with `-`); newest `.jsonl` first, and each `Server stderr: ...` line is what the server
  printed:
  - Windows: `%LocalAppData%\claude-cli-nodejs\Cache\<project-path>\mcp-logs-gameface\`
  - macOS / Linux: `~/.cache/claude-cli-nodejs/Cache/<project-path>/mcp-logs-gameface/`
