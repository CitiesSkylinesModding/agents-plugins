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
| `game_screenshot` | Screenshot the viewport as an inline image (png, or jpeg+quality). | `Page.enable` + `Page.captureScreenshot` |
| `game_dom` | DOM details (tag, classes, attributes, rect, outerHTML) for a CSS selector. | `Runtime.evaluate` |
| `game_click` | Click an element by dispatching real bubbling DOM events. | `Runtime.evaluate` (see note) |

> `game_click` does NOT use CDP `Input.dispatchMouseEvent`: Gameface accepts that command but never
> delivers it to the UI. Instead it dispatches a real pointer/mouse/click sequence on the element,
> which React's delegated `onClick` handlers receive.

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
