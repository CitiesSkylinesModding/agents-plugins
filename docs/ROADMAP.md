# Roadmap

`coherent-gameface` (in the `agents-plugins` marketplace) is a generic toolkit for driving a running
Coherent Gameface UI over CDP.
The MCP server (`gameface-devtools-mcp`, published on npm as `@csmodding/gameface-devtools-mcp`) is
the first facet; skills and richer instrumentation are planned.
The plugin is developed and verified against Cities: Skylines II's Gameface UI, but stays
application-agnostic.

## Planned/Explore

### Non-text element discovery

`game_find` matches on `textContent` only, so icon controls with no text (a back arrow, a close X)
cannot be located by it, forcing a `game_eval` scan by attribute or bounding-box position (surfaced
live locating a settings back arrow). Add an attribute-match mode to `game_find` (find by
`aria-label` / `data-tooltip` / `title` / any attribute); the driving skill already targets
`[data-tooltip="..."]` for clicks, so those anchors exist. `querySelectorAll` + `getAttribute` makes
it trivial.

### Debugger ergonomics

Three sharp edges found while field-testing the debugger against a live game:

- `game_screenshot` hangs for the full call timeout while the UI is paused (`Page.captureScreenshot`
  needs the frozen frame loop); the server knows the pause state and should fail fast with "paused;
  resume first". Only gate frame-dependent commands: `Runtime.evaluate` keeps working while paused
  (verified), so `game_eval`, `game_dom`, and `game_wait` must stay usable.
- The debugger only sees scripts parsed after it attaches (Gameface does not replay
  `scriptParsed`), so a late attach lists nothing; `game_debug_scripts` should say so and suggest
  triggering a UI reload.
- On minified one-line bundles, a line breakpoint resolves to column 0 (module evaluation) and
  never hits during normal interaction. `game_debug_set_breakpoint` should report the resolved
  column and warn on single-line scripts; a `game_debug_search_source` (find string, return
  line:column candidates) would make column targeting practical.
- Open question: whether CDP breakpoints re-resolve across a same-connection view reload (scripts
  re-parse under fresh scriptIds; the server prunes its script map, but the engine-side
  `setBreakpointByUrl` registrations were never verified to re-bind). Probe before relying on
  breakpoints surviving a reload.

### Richer console output

`game_console` shows console args via their RemoteObject description, so objects
render as "Object". Use `Runtime.getProperties` / object previews to expand them.
Entries also carry no timestamps, which makes correlating logs with actions and reloads
guesswork; capture and print one per entry (`Runtime.consoleAPICalled` and `Log.entryAdded` carry
a `timestamp` field in standard CDP; verify Gameface populates it).

### Network inspection

Gameface implements the `Network` domain (observe + `getResponseBody` + cookies), but `Fetch` is
missing (no request interception). Surface request/response observation as tools.

When these land they become standard plugin components: `commands/`, `agents/`, and `skills/`
directories auto-discovered by the plugin manifest (`skills/` already ships the `gameface` and
`gameface-driving` skills).

## Sibling plugin: unity-devtools

`plugins/unity-devtools/` is a second, generic plugin in the making: drive a running Unity Mono
development build from the outside over the Mono Soft Debugger protocol (SDB) — attach, inspect,
query ECS entities, read/write components live, invoke C# — with CS2 as the reference target
(dev Mono build, SDB agent live). The feasibility PoC (`poc/`, a .NET CLI) is done and verified
end-to-end against CS2; see `plugins/unity-devtools/AGENTS.md` for what it proves and the SDB
gotchas. The natural next step is converting the CLI's attach-act-detach commands into a .NET MCP
server, the same shape `coherent-gameface` has for CDP.

Shipping checklist when it graduates from PoC (not registered anywhere yet):

- Dual manifests: `.claude-plugin/plugin.json` + `.codex-plugin/plugin.json` (+ its `mcp.json`),
  shared fields identical.
- Entries in BOTH marketplace files: `.claude-plugin/marketplace.json` and
  `.agents/plugins/marketplace.json`.
- A release-please unit (its own `package.json` anchor + `release-please-config.json` entry;
  decide whether it joins the `linked-versions` group or versions independently).
- Extend `scripts/check-plugin-sync.ts` to cover its manifest pair.
- Decide the vendored `Mono.Debugger.Soft` story for installs (marketplace installs copy the
  plugin subtree; a git submodule does not ship through that path — likely needs the built
  binary committed, mirroring the `coherent-gameface` bundle approach).
