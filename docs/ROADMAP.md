# Roadmap

`cs2-modkit` is a multi-facet toolkit for Cities: Skylines II mod development. The
Gameface browser-instrumentation MCP server is the first facet; others are planned.

## Shipped

### Gameface UI instrumentation (MCP server)

Direct Chrome DevTools Protocol control of the running mod UI. Tools: `game_status`,
`game_eval`, `game_screenshot` (with selector clipping), `game_dom`, `game_wait`,
`game_click`, `game_fill`, `game_type`, `game_hover`, `game_console`. See the README
and `server/`. Input tools dispatch DOM events (CDP `Input` is ignored by Gameface).

### JS debugging (MCP server)

Source-level debugging of the UI via the V8 `Debugger` domain: `game_debug_status`,
`game_debug_scripts`, `game_debug_source`, `game_debug_set_breakpoint`,
`game_debug_remove_breakpoint`, `game_debug_pause_state`, `game_debug_evaluate`,
`game_debug_step`. Breakpoints/pauses freeze the UI until resume; closing the
connection auto-resumes.

## Planned

### Richer console object rendering

`game_console` shows console args via their RemoteObject description, so objects
render as "Object". Use `Runtime.getProperties` / object previews to expand them.

### C# debugging

Help attach to / inspect the mod's C# side (the mod runs inside the Unity/CS2
process). Likely a mix of: a skill documenting how to attach a managed debugger,
commands to surface mod logs, and helpers around the reflection/proxy layer.
Design still open.

### Decompiled-source retro-engineering subagent

A subagent that answers "what does the game do here / how is X wired" by reading the
game's pre-decompiled public DLLs and the decompiled C# (`../DecompiledCitiesSkylines2`)
plus the minified game UI source. It should know the repo conventions (ask the user
when the game's internals are ambiguous) and store learnings in memory.

When these land they become standard plugin components: `commands/`, `agents/`,
`skills/` directories auto-discovered by the plugin manifest.
