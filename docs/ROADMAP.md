# Roadmap

Planned facets for the `csmodding` marketplace plugins. The two toolkits are generic, developed and
verified against Cities: Skylines II but application-agnostic; `cs2-modding` is knowledge about that
game specifically.

## coherent-gameface

Drive a running Coherent Gameface UI over CDP; the MCP server (`gameface-devtools-mcp`, published
on npm as `@csmodding/gameface-devtools-mcp`) is the first facet; richer instrumentation is
planned. When these land they become standard plugin components: `commands/`, `agents/`, and
`skills/` directories auto-discovered by the plugin manifest (`skills/` already ships the
`gameface` and `gameface-driving` skills).

### Selector pre-flight

Cohtml throws a bare "Invalid CSS selector" on `:not()`, `:has()` and `:first-of-type`, and each of
the nine selector-taking tools surfaces that as an opaque failure an agent tends to retry unchanged.
The fact lives in the skills — `gameface-driving` and the `gameface` scripting reference — which a
standalone MCP client never loads, so the tool tier carries none of it, and restating it in nine
descriptions is the wrong shape for one engine constraint.

Screen the selector server-side instead, before the CDP round trip, and name the unsupported
construct and a rewrite. A heuristic scan suffices: no CSS parser, and missing an exotic case costs
nothing beyond the opaque error the caller already gets. What it must not do is refuse a selector
the engine would have accepted — the unsupported set is verified against one Cohtml version, so the
screen either gates on the version `game_status` already reports or warns while letting the call
through.

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

### Committing a programmatic fill

`game_fill` sets a React-controlled input's DOM value without committing it: fields whose change
handler only updates local state and whose blur handler fires the real commit read back the old
value after a fill. The cs2-modding debug-menu pass hit this on the game's debug text inputs and
had to follow every fill with a hand-written
`el.dispatchEvent(new FocusEvent('focusout', {bubbles: true}))` through `game_eval` (the
workaround its `driving-the-menu.md` reference now teaches). A `commit` option on `game_fill` —
dispatch a bubbling `focusout` after setting the value — would make the fill one call.

### Value-binding reads

Reading a C# value binding from the page means hand-writing the subscribe dance through
`game_eval` — `engine.on("<group>.<name>.update", cb)`, then `engine.trigger("<group>.<name>.subscribe")`,
then the matching unsubscribe — which the cs2-modding research pipeline ran to reach
simulation-side data the DOM never renders (the workaround `docs/SOURCES.md` entry 9 records).
A `game_binding` tool would make it one call: subscribe, capture the first payload, unsubscribe,
return it.

## unity-devtools

Drive a running Unity Mono development build from the outside over the Mono Soft Debugger protocol
(SDB): discovery, live type reflection, C# expression evaluation, ECS entity/component/buffer
read-write, and breakpoint/pause debugging (`debug_*`, `advance`), through one persistent
lazy-attach session.

### Cross-platform support

Discovery is netstat-based and Windows-only. Port discovery to Linux/macOS (parse `/proc` or
`lsof`); the server itself now ships as a platform-agnostic NuGet dotnet tool, so distribution
needs no per-RID artifacts.

### Session-level discovery narrowing

Only `status` takes a process-name prefix; every acting tool resolves discovery itself and fails
outright when several processes look SDB-shaped (an IDE, GitKraken and Steam all qualified in one
session), and the `UNITY_MCP_PROCESS` env filter is read once at server start, so the only recovery
mid-session is editing the MCP config and having the user reconnect the server — the workaround a
live session actually ran. Either honor a narrowed `status` call as a sticky session filter, or
accept the prefix on every tool.

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

### Screenshots as inline images

An `eval` of `UnityEngine.ScreenCapture.CaptureScreenshot(path)` already writes the composited frame
(3D scene and UI together) to the game machine's disk, verified live; the `unity-driving` skill
carries the recipe and the freeze interaction that makes it subtle. That covers the normal case
where the agent shares a machine with the game. What it does not cover is the image arriving in the
tool result, which a remote game needs and which spares everyone else the read-back.

Two gaps of very different size. Capturing in memory (`CaptureScreenshotAsTexture`, or a
`RenderTexture` render, then `EncodeToPNG`) only works after end-of-frame, so it needs debuggee-side
execution and rides on the injected-helper tier above rather than on the evaluator. Returning it is
the small half: tools here return records the SDK serializes as text and `mcp/` has no image content
block anywhere, but the `ModelContextProtocol` SDK supports returning `DataContent` with an
`image/png` mime type. Size the result in pixels rather than bytes, since an image costs the client's
context in proportion to its pixel area: a downscale knob is the lever worth exposing, and an
encoder quality setting is not one.

### GameObject/MonoBehaviour tools

The current surface is ECS + expression evaluation; add tools for the classic Unity object model
(scene hierarchy, GameObject/MonoBehaviour inspection and mutation).

## cs2-modding

Skills and references teaching an agent to write Cities: Skylines II code mods, distilled from the
decompiled game, the wiki and a corpus of open-source mods. Knowledge only: no MCP server, no
runtime, and no shipped code artifacts.

### Extracting the shipped localization dictionaries

The game's compiled `.loc` assets are the only first-party, version-known source for the vanilla
localization key set, and decoding them is mechanical: `Locale.cok` is a plain stored zip, and the
payload is a flat `BinaryWriter` stream whose reader is its own specification. A one-off decoder
produced the 75-group namespace table the `localization` reference bakes.
`docs/research/method-decoding-shipped-locale-data.md` holds the recipe, the two traps and the
citations.

Ship that as a script, so the table is regenerable rather than re-derived by hand each game version.
Shape, sketched not settled: it belongs to `cs2-modding-setup`, which is where procedure over the
user's own install already lives, and it would run against a recorded install path the way the
decompile step already does. The output worth committing is the group table and the identifier-shape
distribution, not the strings — the key identifiers are mechanism and the translations are the
publisher's copyrighted text, which is the line the recipe file states and any script inherits.

Two constraints that would shape it. The plugin ships no executable content by decision, so a script
here is repository tooling of the kind `scripts/` already holds, not something the marketplace copies
into a user's install; that makes it a maintenance tool for regenerating a shipped table, which is
the honest framing rather than a user-facing feature. And two shipped files carry trailing bytes past
their declared end, so end-of-file is not end-of-data and the decoder has to stop on the counts.

### Marker namespace lint

A mechanics file's `VOLATILE:` marker lists the namespaces its names live in, and re-closing that
list by hand drifted across six files in one review gate. `check-skill-content.ts` can enforce the
mechanical half: every `src/Game/<namespace>/` path a file cites must have its namespace in the
file's marker. Types named without a path stay the reviewer's job.

### A second benchmark question set

The first full `bench/` invocation scored 9.81 for the control arm against 10.00 for the treatment
one, three of its four questions saturating at 10/10 in both: what the skill measurably buys on that
set is cost and time, roughly a fifth of the dollars and a third of the turns, and not correctness.
A recall question the references cover is one Opus at medium effort answers cold given the same
roots. Getting off that ceiling needs harder questions, and the set can only widen once every
techniques and mechanics file has landed — until then a new question either duplicates one of the
four or aims at prose that does not exist yet.

Code generation is the candidate shape. A recall question asks what the game does; a code-generation
one asks for a system, a component or a patch, which is where the traps bite — update ordering,
values baked at initialisation, surviving a save, the update interval that must be a power of two —
and where a good practice either shows up in the produced code or does not. It also breaks the
saturation, since a run can miss one trap while getting everything else right.

What the harness would need, smallest first:

- Nothing in the arms. An answer already travels as text and code travels the same way, so
  `Read,Glob,Grep` stays the right tool set: no workspace writes, no build, no new isolation
  question.
- Nothing in the rubric format. A rubric line is free prose with a weight, so "does not read the
  value before the system that writes it has run" scores exactly like a fact point, and the judge
  stays blind and tool-free.
- A far more expensive verified answer: a reference implementation the maintainer has compiled and
  run, not a paragraph. That authoring cost, not the harness, is what gates this facet.
- An open question on compilation. The judge cannot build, so scoring "it compiles" means a scratch
  mod project, a per-question skeleton the answer drops into, and a `dotnet build` per run. Worth
  building only if judged scores turn out to disagree with the compiler — build a handful of answers
  by hand first and find out.

### Asset, map and editor authoring

Scope is code mods, so loading assets _from code_ is covered and authoring the assets themselves is
not: meshes, textures, import setup, texture sharing, map creation and the in-game editor. That is a
GUI and DCC-tool discipline an agent cannot drive, and the mod corpus offers no evidence base for
it — a possible later facet rather than an omission, and one that would need sources of its own.
