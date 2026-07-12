# Roadmap

`coherent-gameface-claude-plugin` is a generic toolkit for driving a running Coherent Gameface UI
over CDP. The MCP server (`gameface-devtools-mcp`, published on npm as
`@csmodding/gameface-devtools-mcp`) is the first facet; skills and richer instrumentation are
planned. The plugin is developed and verified against Cities: Skylines II's Gameface UI, but stays
application-agnostic.

## Shipped

### Gameface UI instrumentation (MCP server)

Direct Chrome DevTools Protocol control of a running Gameface UI. Tools: `game_status`,
`game_eval`, `game_screenshot` (with selector clipping), `game_dom`, `game_wait`,
`game_click`, `game_fill`, `game_type`, `game_hover`, `game_console`. See `mcp/README.md`
and `mcp/`. Input tools dispatch DOM events (CDP `Input` is ignored by Gameface).

### JS debugging (MCP server)

Source-level debugging of the UI via the V8 `Debugger` domain: `game_debug_status`,
`game_debug_scripts`, `game_debug_source`, `game_debug_set_breakpoint`,
`game_debug_remove_breakpoint`, `game_debug_pause_state`, `game_debug_evaluate`,
`game_debug_step`. Breakpoints/pauses freeze the UI until resume; closing the
connection auto-resumes.

## Planned

### Skills

Skills packaged with the plugin that document how to drive a Gameface UI with the `game_*` tools:
the CDP quirks (input via DOM events, malformed `webSocketDebuggerUrl`, the Debugger freeze), common
recipes (find a widget, read React state, wait for a screen), and safe debugging workflows.

### Richer console object rendering

`game_console` shows console args via their RemoteObject description, so objects
render as "Object". Use `Runtime.getProperties` / object previews to expand them.

### Network inspection

Gameface implements the `Network` domain (observe + `getResponseBody` + cookies), but `Fetch` is
missing (no request interception). Surface request/response observation as tools.

### Self-contained executable

Port the precompiled ESM build to a self-contained executable (`bun build --compile`), dropping
the bun/node requirement for npm consumers. Deliver the executables through the SAME
`@csmodding/gameface-devtools-mcp` package, following the esbuild/biome/oxlint pattern:
per-platform binary packages wired as `optionalDependencies`, with a tiny JS launcher kept as the
`bin` entry, so `npx -y @csmodding/gameface-devtools-mcp@latest` keeps delivering auto-updates.

When these land they become standard plugin components: `commands/`, `agents/`,
`skills/` directories auto-discovered by the plugin manifest.
