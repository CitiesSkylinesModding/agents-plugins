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

Entries in this section run **best payoff per unit of effort first**. Place a new one by that
ranking rather than appending it to the end.

### Standing instructions for clients that load no skill

Procedure lives in `skills/`, which a standalone npm consumer of
`@csmodding/gameface-devtools-mcp` never loads: they get the tool descriptions and nothing else.
`McpServer` takes an `instructions` string served during the handshake, which Coherent Labs uses to
carry a standing directive to every client. Two sentences here — reach for `game_status` when a tool
fails, and act on the construct a rejected selector names — would close the gap as a pointer rather
than a duplicate, leaving one tier per fact intact.

### Removing a breakpoint the UI is paused at

`game_debug_remove_breakpoint` removes the registration and reports success, saying nothing about a
pause still standing at that very breakpoint, which leaves the UI frozen with the thing that froze
it gone. The pause accessor the screenshot gate reads answers this at no cost: name the pause in the
result, and route to `game_debug_step`. Worth weighing whether removal should offer to resume, which
the rest of the debugger surface refuses to do on the agent's behalf.

### The CDP surface across engine versions

Two load-bearing findings here rest on one engine version and are written as flat facts about
Gameface. Coherent Labs' server, built for 3.1.2+, assumes the opposite of both.

Input is the sharp one. `tools.ts` records that Gameface accepts `Input.dispatchMouseEvent` but
never routes it into the Cohtml DOM event system — "(verified: handlers never fire)", with no
version attached, though the plugin's verification context is 1.64 alone. That finding is why every
input tool here dispatches bubbling DOM events instead, and why `game_hover` warns that the CSS
`:hover` state stays unset. Their input path rests on `Input.dispatchMouseEvent`,
`Input.dispatchTouchEvent` and `Input.insertText`, and their hover is commented as triggering the
`:hover` state. Either CDP input works on a 3.x Player and our claim needs a version and a host
qualifier, or their interaction tools do not work — and only one of those readings leaves our
shipped prose correct.

The DOM domain is the other. They report `DOM.resolveNode` returning an empty object,
`DOM.setAttributeValue` silently no-opping and `DOM.performSearch` never returning a `searchId`,
while `DOM.getBoxModel` and `Runtime.evaluate` both work. Every tool here reaches the page through
`Runtime.evaluate`, so that surface has never been exercised against 1.64 at all. The answer gates
whether a `nodeId`-addressed path is available here — the addressing their assertions and DOM search
depend on.

One probe session against a running game answers both, and the result belongs in `skills/gameface/`
carrying the version and host it was verified on rather than stated flat.

### Computed styles as a tool

Reading resolved styles means hand-writing `getComputedStyle` through `game_eval` and knowing to
wrap it in two nested rAFs, since a computed value settles two to three frames after the change that
caused it: the `gameface` skill teaches the idiom and nothing enforces it. A tool taking a selector
and an optional property list would fold that wait in and return the resolved values, the way
Coherent Labs' `get_computed_styles` does — with the frame discipline theirs has no reason to carry.

### Gate the documented performance rules, then decide on a lint

Coherent Labs' `perf_lint` walks the rendered tree for six shapes their docs name as expensive:
`align-items: stretch` on a flex container, flex items with no explicit `flex-basis`,
`display: simple` children that are not absolutely positioned, inline `data:`/SVG assets that defeat
Instaload, `:root`-scoped custom properties, and `opacity` on an element with children where
`coh-simple-opacity` would do. Three are already prose in
`skills/gameface/references/performance.md`; the other three name 3.x features and mean nothing on a
1.x engine until gated. Gate them first, which improves the reference whatever follows, and let the
surviving set decide whether a lint tool earns its place — a rule that fails gating is a false
positive waiting to fire.

Frame-timing measurement is a separate idea and does not port. rAF ticks inside the host's per-frame
`View::Advance`, so inter-frame deltas measure the game's frame rate rather than the UI's
contribution to it, and a number that looks meaningful while being unrelated to the question is
worse than no number. Measuring UI cost against a live game starts from Cohtml's own per-stage
markers, and is its own investigation.

### Value-binding reads

Reading a C# value binding from the page means hand-writing the subscribe dance through
`game_eval` — `engine.on("<group>.<name>.update", cb)`, then `engine.trigger("<group>.<name>.subscribe")`,
then the matching unsubscribe — which the cs2-modding research pipeline ran to reach
simulation-side data the DOM never renders (the workaround `docs/SOURCES.md` entry 9 records).
A `game_binding` tool would make it one call: subscribe, capture the first payload, unsubscribe,
return it.

### Layout assertions

Verifying a layout costs a screenshot the agent has to eyeball, or a hand-written
`getBoundingClientRect` comparison through `game_eval`: the first is expensive in context and the
second is rewritten from scratch every time. Coherent Labs' own Gameface MCP settles the three
recurring cases as data instead — content overflowing its own box (`scrollWidth`/`scrollHeight`
against `clientWidth`/`clientHeight`), two boxes intersecting, and an element escaping its
container, its named ancestor, or the viewport. Ship the same three, selector-addressed and computed
in one page-context evaluate rather than through their `nodeId` → `getBoxModel` →
`elementFromPoint` bridge, which exists only to work around a `DOM.resolveNode` that returns nothing
usable. Their tolerances are worth taking: 0.5px on geometry for subpixel jitter, 1px on text fit.
Layout here is a frame behind JS, so each check needs the rAF wrap the `gameface` skill prescribes,
or a result that says plainly it read last frame's geometry.

The plugin's shipped `WHY.md` deep-links this heading twice, so retitling or retiring the entry
means updating those links; they resolve silently to the top of the page rather than failing.

### Expanded values in the debugger tools

`game_debug_pause_state` and `game_debug_evaluate` render an object local as its bare description
(`Object`), the defect `game_console` no longer has: `mcp/src/console.ts` owns a page-context
serializer, a stored value tree and a depth-aware renderer, none of it coupled to console capture.
Lift that trio into a module both facets import, then give the debugger tools the same `depth`
parameter. Reuse rather than a second serializer is the point: two of them drift on markers, clip
width and the DOM-node idiom, so the same object prints differently depending on which tool showed
it.

### A probed support matrix for the engine the game actually ships

`skills/gameface/references/version-gating.md` infers what a game supports from the changelog, then
sends the agent to probe. Coherent Labs derived theirs the other way round, from a feature-detection
sweep against a live engine: per-property forbidden *values* (`align-items` rejects `baseline`,
`position` rejects `sticky`), selectors that parse but never match, and missing JS globals down to
per-class property counts — evidence no changelog carries. Their sweep ran against 3.x and does not
ship, and the interesting version is the one a modder actually has. Ship a probe script beside
`scripts/fetch-doc.mjs` running the same three sweeps over a live CDP connection: CSS property and
value acceptance by style round-trip, selector acceptance by `querySelector` try/catch, and JS
global and per-class property presence. Running it against CS2's 1.64 turns the reference's inferred
claims into probed ones for the reference target, while the script stays generic enough for anyone
to point at their own game.

### Console call sites

Every `consoleAPICalled` event carries a `stackTrace`, but its frame urls arrive empty, so naming
the file:line a log came from needs a `scriptId → url` lookup. That means the `Debugger` domain
enabled permanently plus a script map that is empty in the common lazy-attach case, since the engine
does not replay `scriptParsed` — too much standing cost for the payoff, so `game_console` prints no
call site. Revisit as opportunistic resolution: when the debugger is already attached and its
existing script map answers, render the frame; otherwise print nothing.

### Localization and custom effects in the engine skill

Coherent Labs' documentation corpus covers two topics `skills/gameface/` does not: localization
(text expansion, RTL mirroring) and custom effects, which `references/performance.md` names only as
a trap, since a stylesheet merely containing `coh-custom-effect` reverts the whole view to
synchronous style solving. Both plausibly bear on a mod shipping translated UI or a styled panel.
Scope before writing, and take the facts one at a time under
[ADR 0011](adr/0011-coherent-labs-documentation-corpus-stays-out-of-the-gameface-skill.md): the
subset that survives gating against a 1.x engine may be small enough to fold into the existing
references rather than earn new ones.

### Network inspection

Gameface implements the `Network` domain (observe + `getResponseBody` + cookies), but `Fetch` is
missing (no request interception). Surface request/response observation as tools. Note what already
arrives without it: `game_console` captures `Log.entryAdded`, which is where the engine reports a
failed resource load, so the error case is visible today and the marginal value here is bodies,
headers and timing rather than learning that a request failed at all.

## unity-devtools

Drive a running Unity Mono development build from the outside over the Mono Soft Debugger protocol
(SDB): discovery, live type reflection, C# expression evaluation, ECS entity/component/buffer
read-write, and breakpoint/pause debugging (`debug_*`, `advance`), through one persistent
lazy-attach session.

### Cross-platform support

Discovery no longer stands in the way: `BeaconListener` joins the multicast group through
`NetworkInterface`, and the server ships as a platform-agnostic NuGet dotnet tool, so distribution
needs no per-RID artifacts. What remains is the pair of server-lifetime watchdogs, both Windows-only
— `ParentWatchdog` reads the parent pid through a Win32 call and `StdinWatchdog` watches the pipe
with `PeekNamedPipe`. Elsewhere the stdio transport's own shutdown is the only lifetime tie, so an
MCP reconnect can strand the previous server still holding the exclusive SDB slot
(`docs/solutions/unity-mcp-server-stranded-on-reconnect.md`). Port those two, then verify a live
attach on Linux and macOS: nothing here has ever run on either.

### A network interface that appears after the server did

`BeaconListener` enumerates interfaces once, in its constructor, and holds those multicast
memberships for the process's life. A server started before the VPN connects, before WSL or a
hypervisor switch comes up, or across a Wi-Fi adapter bouncing, is never a member of the group on
that interface — and the group is joined on every interface precisely because the packets arrive on
whichever the OS chose. The symptom is total and permanent: every `status` reports no beacon and
every attach fails against a game running normally, curable only by restarting the server, which
nothing tells the user to do.

Re-enumerating costs a timed receive in place of the blocking one, so each of the per-port loops
wakes periodically and rejoins whatever is new. Retry ladders against filtered multicast were ruled
out deliberately, and this is a narrower thing: the membership set going stale, not a fallback for a
network that refuses the group.

A second silence rides on the same loop. `ReceiveLoop` treats every `SocketException` as the
transient WSAECONNRESET an ICMP unreachable produces, and backs off 200 ms; one that keeps coming
back instead — the interface carrying the membership torn away — spins the thread for the process's
life without ever ending that port's listen, so the port names itself in no `Fault` entry and
`Listening` still counts it among those that can deliver. Bounding it needs a run of failures told
apart from a lone hiccup, and the ports carrying no traffic never complete a receive to reset a
counter on, so the bound has to be the elapsed time a socket has spent failing without a gap. The
timed receive above supplies exactly that clock, which is why the two belong in one change.

### Finding a Unity Editor

A Unity Editor runs the same soft-debugger agent as a player and drives identically once attached.
Whether it appears on the PlayerConnection beacon is unsettled: nothing recorded says it does not,
and an Editor's own log prints its host string — `[Debug] 1` included — against 54997 and 34997,
without saying whether it is announcing itself or only joining to receive. Settling it costs
nothing: next time an Editor is open, look at whether `status` reports it.

Where it does not, build what a live session ran by hand: enumerate the Editor process by name,
confirm a listener in the SDB range, and pass that port to `attach`. Every step of that is
mechanical, which is what makes it a gap rather than a limitation. An Editor's port looked like
`56000 + (pid % 1000)` on a single sample, so treat that as a hint worth checking first rather than
as the answer.

Where it does turn up, the listener needs a way to tell it from a player before reporting an
endpoint. `SdbPort` falls back to `56000 + (guid % 1000)`, evidenced only against recorded player
runs, and the pid formula above suggests an Editor derives its port differently — so a beacon
carrying no `:port` suffix would be given a confidently wrong endpoint, and with a game also
running the two sightings would alternate. Settle what an Editor actually advertises before
anything reads its beacon as a player's.

Whatever finds it must also reattach to it: a domain reload — a script compile, entering or leaving
play mode — drops the connection, and the reattach resolves from the beacon, which may not describe
the Editor. A process-anchored find therefore has to be re-runnable rather than one-shot.

### An `ecs_query` seam in the SDB library

Removing the query-scanning entity lookup left `ecs_query` the sole owner of the whole query
lifecycle inside the MCP layer: create, `try`/`finally` dispose, paging, and the
`"<systemTypeFullName>:<method>"` label calling convention, against sibling tools that are
one-liners over `sdb/`. The layer is meant to hold little logic, and the dispose discipline and
label convention currently have no home in the library and no integration-test seam. Moving them
into `Ecs` would give the next consumer of "list the entities matching these components" something
to call.

### Batch component reads over an entity list

`eval` has no established route to construct an `EntityQuery` — entry 8 of `docs/SOURCES.md`
records what failed and the one untried route. So the working recipe for "read one component
off each of N entities" is
`ecs_query` for the list, then one `eval` per batch of entities with a long interpolated final
expression — a discovery pass read 55 prefab components in seven such calls, the batch size limited
by statement count. The exclusions landed in the tool description, and with the recipe in the
`unity-driving` skill, on 2026-08-10; what remains open is an `ecs_get_component` mode accepting
several entities in one call, which would beat the recipe outright.

### A buffer filter on `ecs_query`

`ecs_query` matches on component presence, so it cannot tell an entity whose `DynamicBuffer` holds
elements from one whose buffer is empty. Settling whether the debug proxy's buffer read pins a
`bool`-carrying element type needed a non-empty buffer, and finding one meant probing entities one
at a time: `Game.Rendering.Emissive` twice, `Game.Net.ArrowPosition`, `Game.Rendering.Skeleton` —
all length 0 — before `Game.Net.LabelPosition` came back with 51. Either report the length beside
each listed entity when a queried type is a buffer, or take a minimum-length filter; the first is
cheaper and answers the same question.

### What a failed `eval` reports

On failure `eval` reports the failing statement, the in-game exception, and every local evaluated so
far, verbatim and uncapped. That dump is the tool's best diagnostic and also the one place a result
can be arbitrarily large: reading a screenshot back off the game machine bound a 3.7 MB base64 string
to a local, and one failed statement returned the whole of it. Clip each local to a budget and name
what was elided, the way `ecs_query` and `find_types` already report what their limits cut.

The same failure named the wrong cause — `a previous result was garbage-collected after the game
resumed; re-evaluate it instead of using _`, on a call that used no `_`. The local it choked on was a
`Stopwatch` read successfully two statements earlier, so the likely mechanism is the large allocation
triggering a collection that invalidated the mirror. If that holds, the message should name the
collected local and the constraint behind it — an allocation mid-sequence can invalidate object
mirrors held across it — rather than a slot the caller never used.

### `eval` cannot cast to an array type

`(Game.Net.LabelPosition[])em.Debug.GetComponentBoxed(entity, type)` fails to parse:
`unsupported: array type`. The workaround is `(System.Array)` plus `GetValue(int)`, which reads the
elements back but renders each one truncated (`float3 {...}`), so a call returning an array is
unusable at its natural type. Every `EntityManager.Debug` route that boxes a buffer hands back
`object` over a `T[]`, and so does any game method returning an array, so accepting `T[]` in the
cast grammar — and in `typeof` with it — closes a class of read rather than one call.

### `eval` will not bind an enum argument

`System.Runtime.InteropServices.GCHandle.Alloc(o, System.Runtime.InteropServices.GCHandleType.Pinned)`
returns `Incorrect number or types of arguments (Parameter 'arguments')`, with `o` already bound to
a local by a preceding statement. The enum member access itself resolves, so the failure is in
overload matching — likely the argument arriving as the enum type where the parameter check wants
its underlying integer, or the reverse. It blocked settling a runtime question in its general form
and left only the field types the game happens to ship as evidence.

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
encoder quality setting is not one. One trap worth naming, observed in Coherent Labs' Gameface MCP
server: `take-screenshot.ts` builds a correct `{ type: "image", … }` block and `index.ts` then wraps
it in the uniform `JSON.stringify` text path every one of its tools takes, so the image arrives as a
wall of base64 text and nothing fails loudly.

### GameObject/MonoBehaviour tools

The current surface is ECS + expression evaluation; add tools for the classic Unity object model
(scene hierarchy, GameObject/MonoBehaviour inspection and mutation).

### Unity on CoreCLR

Unity 6.7 LTS is the last release built on Mono and ships an experimental CoreCLR desktop player;
6.8 removes Mono. CoreCLR is desktop-only, IL2CPP stays for consoles, mobile and web, and an IL2CPP
player speaks the soft debugger protocol, so SDB keeps a target set after Mono dies. Nothing here is
urgent: an LTS project ships for years, and a released game does not change runtime.

The gating unknown is what a CoreCLR player exposes. Unity's manual asserts only that CoreCLR
supports managed-code debugging, its March 2026 scripting status update does not mention debugging
at all, and the two possibilities are far apart: an SDB-compatible agent over CoreCLR, which Unity
built once already for IL2CPP and which would make the port small, or CoreCLR's own ICorDebug, which
makes it a rewrite. One cheap experiment settles it — a 6.7 CoreCLR desktop player built with script
debugging, then watch whether it still multicasts the PlayerConnection beacon with `[Debug] 1` and
whether anything listens in 56000-56999. `BeaconListener` is the tool for that.

Two things to recognise it by, both read off JetBrains' Unity player listener rather than any Unity
source. A CoreCLR beacon inserts a `[ProcessId]` field after `[Port]`, which `PlayerConnectionBeacon`
would parse and ignore as it stands, and `[Flags]` gains a bit at `1 << 7` for CoreCLR that Unity's
published `MulticastFlags` enum does not carry — this plugin reads no flags at all today, so that bit
is the cheapest place to start.

On the ICorDebug branch there is no wire protocol to speak: attach is by PID through `dbgshim`, with
`mscordbi` version-matched to the target runtime, on the same machine — so remote attach dies unless
a proxy runs beside the game. The parser, AST, operator semantics, tool surface, skill and beacon
discovery survive; every mirror-typed layer is rewritten against a different value currency
(`EvalInterpreter`, `DebugController`, `Ecs`, `Invoker`, `TypeCatalog`, and the `mcp/` tools that
handle mirrors directly). Three mismatches are design work rather than API translation: counted
suspend windows against a stop/go model where a func-eval only runs once the process continues;
func-eval requiring its thread at a GC-safe point in managed code, which a main thread inside native
engine code is not; and the loss of cross-machine attach. Two things get better — direct
process-memory reads would take ECS chunk reads off the invoke budget entirely, and the debuggee
being real .NET retires both the Mono-fork answer quirks and the Mono test fixture.

Do not pre-abstract a backend seam ahead of that experiment: it would be an interface with one
implementation, guessed against a shape different enough to get it wrong. The live alternative to
writing an ICorDebug client is the injected-helper tier above — an in-process agent speaking its own
protocol restores remote attach, cheap batching and main-thread scheduling for less work, at the
cost of the injection-free default this plugin is built on.

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

### A source-first sweep of the shipped references

Four of eight shipped claims sampled in one pass came back contradicted by the first-party source
they describe: a mod-failure state the player is never shown, an upkeep share the game computes and
never reads, an input option the prose said nothing reads, and a cell map glossed as the panel it
does not feed. None was found by an audit. The benchmark ticket needed its answers verified against
the owning source rather than against our own prose, so each agent was barred from reading
`plugins/` and `docs/`, told to derive the answer from source before comparing it with the claim,
and told that a disagreement was a useful result.

Run the same fan-out deliberately over every shipped reference file, rather than over the claims a
benchmark happened to touch. The plugin's `AGENTS.md` already prescribes this briefing for a review
gate, but a gate runs at authoring time, on prose the same session wrote, aimed at one reference;
nothing re-derives a shipped claim afterwards. What it costs is an agent per claim family and the
maintainer's time ruling on what comes back. What it buys is the first evidence about the rest of
the corpus, against none today.

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
