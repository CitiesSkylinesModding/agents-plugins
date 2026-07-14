# Roadmap

`coherent-gameface-agent-plugin` is a generic toolkit for driving a running Coherent Gameface UI
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

### Gameface domain-knowledge skill (`gameface`)

Model-invoked skill teaching agents what Gameface is and is not: the not-a-browser deltas, the
version-gating protocol (docs describe the latest engine; games embed a frozen Cohtml), a baked
version-to-feature timeline (`references/version-gating.md`, freshness-checked by
`mise skills:check-changelog`), layout/scripting/performance/tooling references, and a
`fetch-doc.mjs` extractor for the docs site (which defeats summarizing fetch tools). Generic
across games, with Cities: Skylines II as the labeled worked example. See `skills/gameface/`.

## Planned

### Driving skill

A skill that documents how to drive a Gameface UI with the `game_*` tools: the CDP quirks
(input via DOM events, malformed `webSocketDebuggerUrl`, the Debugger freeze), common recipes
(find a widget, read React state, wait for a screen), and safe debugging workflows. Points at
the `gameface` skill for engine domain facts instead of duplicating them.

### Richer console object rendering

`game_console` shows console args via their RemoteObject description, so objects
render as "Object". Use `Runtime.getProperties` / object previews to expand them.

### Network inspection

Gameface implements the `Network` domain (observe + `getResponseBody` + cookies), but `Fetch` is
missing (no request interception). Surface request/response observation as tools.

When these land they become standard plugin components: `commands/`, `agents/`, and `skills/`
directories auto-discovered by the plugin manifest (`skills/` already ships the `gameface`
skill).
