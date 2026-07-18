# Roadmap

Planned facets for the `csmodding` marketplace plugins. Both plugins are generic toolkits,
developed and verified against Cities: Skylines II but application-agnostic.

## coherent-gameface

Drive a running Coherent Gameface UI over CDP; the MCP server (`gameface-devtools-mcp`, published
on npm as `@csmodding/gameface-devtools-mcp`) is the first facet; richer instrumentation is
planned. When these land they become standard plugin components: `commands/`, `agents/`, and
`skills/` directories auto-discovered by the plugin manifest (`skills/` already ships the
`gameface` and `gameface-driving` skills).

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

## unity-devtools

Drive a running Unity Mono development build from the outside over the Mono Soft Debugger protocol
(SDB): discovery, live type reflection, method invokes, ECS entity/component/buffer read-write,
through one persistent lazy-attach session.

### Cross-platform support

Discovery is netstat-based and Windows-only, and the committed exe is win-x64. Port discovery to
Linux/macOS (parse `/proc` or `lsof`) and publish per-RID artifacts.

### Complex invoke arguments / expression evaluation

`invoke` coerces text tokens against the resolved signature, which covers primitives, enums,
strings, Entity, and the EntityManager; struct/object parameters are unreachable. Two explorable
extensions:

- Struct arguments constructed debuggee-side: JSON-shaped input mapped onto a
  default-constructed StructMirror, field by field (the machinery `ecs_set_component` already
  uses, generalized).
- A C# expression evaluator, the way Rider does it: SDB has NO expression-evaluation command, so
  IDEs parse the expression client-side and interpret it as a sequence of mirror primitives
  (field reads, property getters, invokes, indexers), which our `Invoker` already provides.
  A parser (Roslyn parser-only package, or a hand-rolled subset grammar: member chains, calls,
  literals, casts, indexers) plus an AST walker over `Invoker` would cover most of what Rider's
  evaluator does, with no change to the runtime footprint. Lambdas/LINQ need real compilation and
  fall to the next tier.
- An OPT-IN injected helper (exploratory, nothing decided): compile client-side, load into the
  debuggee via an `Assembly.Load(byte[])` invoke. Unlocks lambdas/LINQ, arbitrary struct
  construction, and above all batching (one in-game call instead of thousands of mirror
  round-trips for bulk reads/edits). Constraints that would shape any design: Mono cannot unload
  assemblies, so one persistent helper loaded once per game session (never per-expression
  compilation, which leaks an assembly per eval); compiling user expressions against game types
  needs the game's `Managed/` assemblies on disk as references; and it changes the footprint from
  pure outside observer to injected helper, so it would stay opt-in with the injection-free mode
  remaining the default.

  One possible shape, sketched not settled: a debuggee-side counterpart of the MCP server, a
  small static gateway class with SDB-friendly signatures (primitives/strings in, JSON out) that
  existing mirror invokes can call like any static method. Candidate surface, in rough order of
  value: batch ECS reads (run a query and serialize N entities with selected fields in-process,
  one invoke instead of thousands); reflection-driven member-path projections over query results
  (covers most lambda use without compilation); JSON-shaped writes and invoke arguments
  (deserialize onto the real struct debuggee-side, dissolving the coercion ceiling); managed
  (class) `IComponentData` access via the object-based EntityManager APIs (unreachable over
  mirrors today); temporal captures (record a value across N frames, return the series); plus a
  version/handshake method (detect a stale helper after a plugin update; no reload until game
  restart) and structured try/catch so in-game exceptions come back as data. User-compiled
  lambda execution would come only as a later layer on the same gateway, where the no-unload
  leak actually bites.

### GameObject/MonoBehaviour tools

The current surface is ECS + static/system invokes; add tools for the classic Unity object model
(scene hierarchy, GameObject/MonoBehaviour inspection and mutation).
