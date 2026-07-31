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

### Open questions the invoke-cost pass left behind

Measuring the wire settled the cost model and unsettled some of the reasoning built on it: an invoke
costs ~1 ms while every other wire command costs ~35-90 µs, so several decisions taken to "save round
trips" turn out to have saved the cheap kind
([`docs/solutions/sdb-round-trips-are-not-equal-cost.md`](solutions/sdb-round-trips-are-not-equal-cost.md)).
Four things deserve a deliberate answer rather than the implicit one they have now.

- **Whether to cache the world handle at all.** It nets about one invoke per operation once the
  `IsCreated` liveness check is paid back, and it buys that by holding an `EntityManager` across
  operations — a raw pointer into the entity store, on builds where the collections safety checks are
  compiled out. `EcsCatalog.StillStands` and its `WorldSelection` modes are the most intricate
  reasoning in the ECS layer and exist only to keep that pointer honest. One invoke is a thin return
  on the change's sharpest failure mode; dropping the cache and re-selecting per operation is a real
  option, and the listing wins do not depend on it either way.
- **A per-operation memo tier on `Invoker`.** Its caches are all per-attach, so it can say "remember
  for this attach" but has no way to say "remember for this operation". That gap shows: `Ecs.refused`
  and `Ecs.storeBound` sit on `Ecs` because there is nowhere else to put them, and
  `Invoker.EnumTableFor` had to choose between latching a failure that describes the moment and
  re-paying a refused batched read on every rendered value, with no third option. A per-operation
  scope beside the per-attach one would let each memo state its own lifetime.
- **Invokes still on the table**, each worth roughly one. `EvalInterpreter.EnumMemberValue` spends two
  per enum constant through a debuggee-side `Enum.Parse` + `Convert`, and the interpreter is
  per-operation, so a conditional breakpoint naming a constant re-pays them on every hit — the
  per-attach enum table already holds those facts and only needs its name-to-value direction exposed.
  `Ecs.ComponentTypeOf` is memoized for the identity proof but not for `CreateQuery`, so `ecs_query`
  re-pays `ComponentType.ReadWrite` per named component on every call. `Ecs.fullNameGetter` memoizes a
  property of loaded code on the per-operation `Ecs`, where every sibling memo of that kind is
  per-attach.
- **Whether a storage kind should stay a bare string.** Six literals are produced at two sites and
  compared at two more, and `Kind is not "component"` is the only guard standing between a
  buffer-element entry and `GetComponentData`. An `EcsKind` enum rendered to its wire name at the MCP
  edge makes that comparison checked; it is small, and it is the one place where a typo is a safety
  problem rather than a bug.

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
