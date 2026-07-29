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
(SDB): discovery, live type reflection, C# expression evaluation, ECS entity/component/buffer
read-write, and breakpoint/pause debugging (`debug_*`, `advance`), through one persistent
lazy-attach session.

### Cross-platform support

Discovery is netstat-based and Windows-only. Port discovery to Linux/macOS (parse `/proc` or
`lsof`); the server itself now ships as a platform-agnostic NuGet dotnet tool, so distribution
needs no per-RID artifacts.

### Entity archetype dump (`ecs_list_components`)

Discovering where a piece of state lives currently means playing twenty questions with
`HasComponent<T>` in `eval` (surfaced live hunting for a building's attractiveness, which turned
out to sit on the prefab, not the building). One call listing every component on an entity, and
optionally on its `PrefabRef` target, answers "what does this entity carry" in one shot:
`EntityManager.GetComponentTypes(e)` is mirror-reachable, and the result composes directly with
`eval` for the follow-up reads.

### Type search by fragment

`find_types` requires an exact fully-qualified name, so agents without domain knowledge of the
game fall back to offline decompilation to harvest candidates (the driving skill documents that
workaround). Add a substring/pattern mode over the loaded type list; SDB's `GetTypes` cannot
search, but enumerating assemblies and their types over mirrors (with a per-session cache) can.

### The fixed invoke cost of an ECS operation

Every invoke is a round-trip to a suspended game, so the count per operation is the cost that
matters. Three avoidable ones sit in the ECS path today, all predating the converged entity
naming that surfaced them:

- `Ecs.MakeEntity` reads `Entity.Null` off the debuggee purely to obtain a two-int struct template
  it immediately overwrites client-side. `EvalInterpreter.DefaultMirrorFor` already builds a
  `StructMirror` client-side with no invoke at all (the vendored constructor is internal to the
  same assembly), so the same trick applies. This halves the explicit `index:version` path, which
  the converged rule made the primary one, and also cuts a third invoke off writing an
  Entity-typed field through `CoerceArg`.
- `Ecs.PickWorld` already reads each candidate world's `Name` to match it, then the constructor
  invokes `get_Name` again on the winner. Returning the name it already holds removes that.
- `Invoker.ResolveType` calls `GetTypes` on every lookup while `FindTypeOrNull`, twelve lines
  below, has exactly the hit-cache it wants. Worth care rather than a copy: that cache
  deliberately does not memoize misses, because the debuggee loads assemblies over time.

Together with the per-operation `Ecs` construction (three property invokes re-paid each call,
since `SdbContext.Ecs` builds a fresh instance), setup is currently about half the round-trips of
a component read. Any caching of `World` / `EntityManager` across operations has to keep a
liveness check, since a world can die between calls.

### An `ecs_query` seam in the SDB library

Removing the query-scanning entity lookup left `ecs_query` the sole owner of the whole query
lifecycle inside the MCP layer: create, `try`/`finally` dispose, paging, and the
`"<systemTypeFullName>:<method>"` label calling convention, against sibling tools that are
one-liners over `sdb/`. The layer is meant to hold little logic, and the dispose discipline and
label convention currently have no home in the library and no integration-test seam. Moving them
into `Ecs` would give the next consumer of "list the entities matching these components" something
to call.

### Injected in-game helper (exploratory, opt-in)

The next tier beyond the shipped client-side evaluator (which by design excludes lambdas, LINQ,
loops, and control flow): compile client-side, load into the debuggee via an
`Assembly.Load(byte[])` invoke. Unlocks lambdas/LINQ, and above all batching (one in-game call
instead of thousands of mirror round-trips for bulk reads/edits). Constraints that would shape
any design: Mono cannot unload assemblies, so one persistent helper loaded once per game session
(never per-expression compilation, which leaks an assembly per eval); compiling user expressions
against game types needs the game's `Managed/` assemblies on disk as references; and it changes
the footprint from pure outside observer to injected helper, so it would stay opt-in with the
injection-free mode remaining the default.

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

The current surface is ECS + expression evaluation; add tools for the classic Unity object model
(scene hierarchy, GameObject/MonoBehaviour inspection and mutation).
