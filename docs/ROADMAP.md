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

### Gameface driving skill (`gameface-driving`)

Model-invoked skill teaching agents how to operate the `game_*` tools against a live game:
session triage and crash conduct, element discovery under hashed class names and missing lookup
APIs, the act-then-verify loop, the rebuild-to-live reload cycle (full view reload, sentinel
detection), and safe source-level debugging (attach-before-parse script
visibility, the pause freeze matrix, minified-bundle breakpoint placement). Verified live against
Cities: Skylines II; points at the `gameface` skill for engine domain facts. See
`skills/gameface-driving/`.

## Planned

### Richer console object rendering

`game_console` shows console args via their RemoteObject description, so objects
render as "Object". Use `Runtime.getProperties` / object previews to expand them.

### Network inspection

Gameface implements the `Network` domain (observe + `getResponseBody` + cookies), but `Fetch` is
missing (no request interception). Surface request/response observation as tools.

When these land they become standard plugin components: `commands/`, `agents/`, and `skills/`
directories auto-discovered by the plugin manifest (`skills/` already ships the `gameface` and
`gameface-driving` skills).
