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
read-write, through one persistent lazy-attach session.

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

### Deterministic simulation advance (`advance`)

"Let the simulation react, then verify" currently means an eval to unpause, a wall-clock sleep on
the client, and an eval to re-pause: crude and racy (surfaced live waiting for the attraction
system to recompute a building's attractiveness from fresh prefab data). Two layers, only one of
them generic: releasing a held debugger suspend for N seconds and re-taking it is pure SDB and
fits the server; a game's OWN pause (CS2's simulation speed) is game logic no SDB operation can
lift, and that was the actual blocker in the live scenario. Keep the game knowledge caller-side:
`advance` could take optional before/after eval snippets (e.g. the CS2 speed writes), with the
per-game recipe living in a driving skill, never hardcoded in the server.

### Frame-context evaluation (`debug_evaluate`)

The `eval` tool (shipped) interprets C# client-side over mirror primitives with a pluggable
binding-scope chain; today only the frameless scope exists (builtins + type roots). The seam is
there for a breakpoint/pause toolset (twin of gameface's `game_debug_*`): a `StackFrame`-backed
scope (`StackFrame.GetValues`/`SetValues` exist in the vendored client) would give expressions
frame locals and `this` with zero grammar or walker changes.

### Evaluator integration tests against a headless Mono debuggee (exploratory)

Offline coverage stops one layer short of results on each side: parser tests assert source to AST,
PrimitiveOps tests assert operator semantics on unwrapped client values, and the walker
(`EvalInterpreter`), where evaluation semantics actually live, runs only against live SDB mirrors
(the vendored mirror layer is concrete and wire-bound, unmockable). Classic Mono ships the same
debugger agent (`mono --debugger-agent=...`, version-negotiated at attach like Unity's), so an
integration suite could launch a tiny fixture program under Mono, attach through the real
`SdbSession`/`Invoker`, evaluate raw C# strings, and assert results in CI. The fixture assembly
can carry exactly the shapes the reference game (compiled as C# 9) cannot provide: a struct with a
declared parameterless ctor, a ulong-backed enum above `long.MaxValue`, a derived `new` member
shadowing a base one, a receiver-mutating struct method with out params, rank > 1 arrays; that
turns the walker semantics verified live-only today into asserted regressions. Costs: a Mono
runtime as a new toolchain dependency (mise-pinned locally, apt on CI), a net4x fixture buildable
via reference assemblies, and debuggee process lifecycle in an xUnit collection fixture. Caveat:
upstream Mono's agent is not byte-for-byte Unity's fork, so live verification against the
reference game stays the final gate for Unity-specific behavior; this complements it.

The eval grammar is frozen as shipped. What it accepts composes directly into mirror primitives
with bounded wire cost, and three review rounds (2026-07) found the defects in core-operation
semantics (equality, overload binding, coercion, struct-write persistence), never in syntax
breadth, so shrinking the grammar buys nothing while rejecting idiomatic C# taxes every agent
call with a failed attempt; anything needing debuggee-side execution (lambdas, LINQ, loops,
control flow) stays out of the client-side walker and belongs to the injected-helper tier below.
The semantic boundary is a contract, not a node list: common agent workflows evaluate exactly as
C# would; edge semantics may diverge but must fail loudly with an actionable message, never
succeed silently wrong; deliberate divergences stay documented (today: numeric-to-enum
convenience, in-range integral narrowing, enum/numeric operator mixing, `entity(index)` version
defaulting). New evaluator effort goes to enforcing this contract through the Mono-debuggee suite
below, and to focused delta reviews of new mechanisms, not to chasing further semantic
completeness or another full review pass.

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
