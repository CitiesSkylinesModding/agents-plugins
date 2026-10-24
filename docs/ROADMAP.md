# Roadmap

Planned facets for the `csmodding` marketplace plugins. The two toolkits are generic, developed and
verified against Cities: Skylines II but application-agnostic; `cs2-modding` is knowledge about that
game specifically.

Every entry opens with a triage line: **Priority** (high, medium, low) and **Cost** as the code reads
at triage (trivial, cheap, moderate, large), either one `unsure` when nobody has judged it yet. An
entry that recorded tool failures bear on adds **Hits**: the running count of those failures, dated
by the last `scan-mcp-failures` run that added to it. Entry prose carries no hit count: **Hits** is the
only one kept current.

## coherent-gameface

Drive a running Coherent Gameface UI over CDP; the MCP server (`gameface-devtools-mcp`, published
on npm as `@csmodding/gameface-devtools-mcp`) is the first facet; richer instrumentation is
planned. When these land they become standard plugin components: `commands/`, `agents/`, and
`skills/` directories auto-discovered by the plugin manifest (`skills/` already ships the
`gameface` and `gameface-driving` skills).

### Standing instructions for clients that load no skill

**Priority:** medium · **Cost:** trivial

Procedure lives in `skills/`, which a standalone npm consumer of
`@csmodding/gameface-devtools-mcp` never loads: they get the tool descriptions and nothing else.
`McpServer` takes an `instructions` string served during the handshake, which Coherent Labs uses to
carry a standing directive to every client. Two sentences here — reach for `game_status` when a tool
fails, and act on the construct a rejected selector names — would close the gap as a pointer rather
than a duplicate, leaving one tier per fact intact.

### A `game_eval` promise that never settles

**Priority:** medium · **Cost:** cheap · **Hits:** 3 as of 2026-09-29

With `awaitPromise`, `game_eval` awaits the returned promise, and one that never settles surfaces as `CDP call
'Runtime.evaluate' timed out after 15000ms` — seen awaiting an XHR against an `assetdb://` URL. The message names the transport rather than the cause, so the agent cannot tell a
hung engine from its own unresolved promise. Say that the call timed out while awaiting the promise:
either it never settled or the engine stopped answering. A per-call `timeoutMs` on `game_eval` is the larger half, since `GAMEFACE_CALL_TIMEOUT_MS`
is server-wide; wait for its hits to grow before building it.

### A `game_eval` that blocks the game's frames

**Priority:** medium · **Cost:** trivial

`gameface-driving` does not warn that the UI script runs on the game's frame: a `requestAnimationFrame`
loop busy-waiting 70ms per frame, set up from `game_eval` to simulate slow frames in Cities: Skylines II,
hung the game until crashpad killed it, and the mod's settings file had lost a saved value by the next
start. One sentence in the skill's traps: a script that blocks frame after frame takes the game down
with it, so measure slow-frame behaviour from the game's own slow frames instead.

### Removing a breakpoint the UI is paused at

**Priority:** low · **Cost:** cheap

`game_debug_remove_breakpoint` removes the registration and reports success, saying nothing about a
pause still standing at that very breakpoint, which leaves the UI frozen with the thing that froze
it gone. The pause accessor the screenshot gate reads answers this at no cost: name the pause in the
result, and route to `game_debug_step`. Worth weighing whether removal should offer to resume, which
the rest of the debugger surface refuses to do on the agent's behalf.

### The CDP surface across engine versions

**Priority:** low · **Cost:** unsure

Two questions stand open about the CDP surface, both of them cases where Coherent Labs' server,
built for 3.1.2+, assumes the opposite of what this plugin measured or never exercised at all.

Input is settled on our side. A raw CDP session at Cohtml 2.2.1.3 re-ran what 1.64 had shown:
`Input.dispatchMouseEvent`, `Input.dispatchKeyEvent` and `Input.insertText` each answer with an
empty result and no error, and no DOM handler fires (the probe catalogue's CDP entry holds the
session and its controls). That is why every input tool here
dispatches bubbling DOM events instead, and why `game_hover` warns that the CSS `:hover` state
stays unset. What stays open is their side: their input path rests on `Input.dispatchMouseEvent`,
`Input.dispatchTouchEvent` and `Input.insertText` — the touch one probed at neither version here —
and their hover is commented as triggering `:hover`, so either CDP input works on a 3.x Player and
our claim needs a host qualifier rather than only the two versions it now carries, or their
interaction tools do not work.

The DOM domain is the second. They report `DOM.resolveNode` returning an empty object,
`DOM.setAttributeValue` silently no-opping and `DOM.performSearch` never returning a `searchId`,
while `DOM.getBoxModel` and `Runtime.evaluate` both work. Every tool here reaches the page through
`Runtime.evaluate`, so that surface has never been exercised on the reference target at all. The
answer gates whether a `nodeId`-addressed path is available here — the addressing their assertions
and DOM search depend on.

One probe session against a running game answers the DOM question, and the result belongs in
`skills/gameface/` carrying the version and host it was verified on. The input question needs a
host this repo does not have: it closes on a 3.x Player, or on Coherent Labs answering for their
own tools, and until then the claim keeps the two versions it carries.

### Computed styles as a tool

**Priority:** medium · **Cost:** cheap

Reading resolved styles means hand-writing `getComputedStyle` through `game_eval` and knowing to
wrap it in two nested rAFs, since a computed value settles two to three frames after the change that
caused it: the `gameface` skill teaches the idiom and nothing enforces it. A tool taking a selector
and an optional property list would fold that wait in and return the resolved values, the way
Coherent Labs' `get_computed_styles` does — with the frame discipline theirs has no reason to carry.

### Gate the documented performance rules, then decide on a lint

**Priority:** low · **Cost:** moderate

Coherent Labs' `perf_lint` walks the rendered tree for six shapes their docs name as expensive:
`align-items: stretch` on a flex container, flex items with no explicit `flex-basis`,
`display: simple` children that are not absolutely positioned, inline `data:`/SVG assets that defeat
Instaload, `:root`-scoped custom properties, and `opacity` on an element with children where
`coh-simple-opacity` would do. Three are already prose in
`skills/gameface/references/performance.md`; the other three name 3.x features and mean nothing on
the reference target's engine until gated. Gate them first, which improves the reference whatever
follows, and let the surviving set decide whether a lint tool earns its place — a rule that fails
gating is a false positive waiting to fire.

Frame-timing measurement is a separate idea and does not port. rAF ticks inside the host's per-frame
`View::Advance`, so inter-frame deltas measure the game's frame rate rather than the UI's
contribution to it, and a number that looks meaningful while being unrelated to the question is
worse than no number. Measuring UI cost against a live game starts from Cohtml's own per-stage
markers, and is its own investigation.

### Value-binding reads

**Priority:** medium · **Cost:** cheap

Reading a C# value binding from the page means hand-writing the subscribe dance through
`game_eval` — `engine.on("<group>.<name>.update", cb)`, then `engine.trigger("<group>.<name>.subscribe")`,
then the matching unsubscribe — which the cs2-modding research pipeline ran to reach
simulation-side data the DOM never renders (the workaround `docs/SOURCES.md` entry 9 records).
A `game_binding` tool would make it one call: subscribe, capture the first payload, unsubscribe,
return it.

### Layout assertions

**Priority:** medium · **Cost:** moderate

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

**Priority:** medium · **Cost:** moderate

`game_debug_pause_state` and `game_debug_evaluate` render an object local as its bare description
(`Object`), the defect `game_console` no longer has: `mcp/src/console.ts` owns a page-context
serializer, a stored value tree and a depth-aware renderer, none of it coupled to console capture.
Lift that trio into a module both facets import, then give the debugger tools the same `depth`
parameter. Reuse rather than a second serializer is the point: two of them drift on markers, clip
width and the DOM-node idiom, so the same object prints differently depending on which tool showed
it.

### A probed support matrix for the engine the game actually ships

**Priority:** low · **Cost:** moderate

`skills/gameface/references/version-gating.md` infers what a game supports from the changelog, then
sends the agent to probe. Coherent Labs derived theirs the other way round, from a feature-detection
sweep against a live engine: per-property forbidden *values* (`align-items` rejects `baseline`,
`position` rejects `sticky`), selectors that parse but never match, and missing JS globals down to
per-class property counts — evidence no changelog carries. Their sweep ran against 3.x and does not
ship, and the interesting version is the one a modder actually has.

For the reference target the mechanism is now the repo-only `gameface-update` skill and the probe
catalogue beside it (`.agents/skills/gameface-update/probes.md`): every claim the plugin rests on a
probe, re-run through the `game_*` tools whenever the target's engine moves, as it did to the 2.2
line CS2 ships as of 1.6.2f1 (`src/cohtml.Net/Properties/AssemblyInfo.cs:16`). The catalogue's own
as-of line says whether its answers have been probed yet or are still the plugin's inferred claims.
What remains beyond that is the part anyone can point at their own game: a probe script beside the
`gameface` skill's `scripts/fetch-doc.mjs` running the same three sweeps over a live CDP connection —
CSS property and value acceptance by style round-trip, selector acceptance by `querySelector`
try/catch, and JS global and per-class property presence — exhaustive where the catalogue covers
only what the plugin already states.

### Addressing an input target by expression

**Priority:** low · **Cost:** moderate · **Hits:** 3 as of 2026-09-29

`game_click` and `game_screenshot` take a CSS selector, so an element only a construct the engine
rejects can reach — `button:has(img[src$="connect.svg"])` — costs a round trip: tag it with a
`data-*` attribute through `game_eval`, then address the tag. The rejection message already names
the construct and the workaround, so each recovery costs exactly that extra call. A
target mode taking a JS expression that returns the element would absorb the whole class rather than
one selector.

### Console call sites

**Priority:** low · **Cost:** moderate

Every `consoleAPICalled` event carries a `stackTrace`, but its frame urls arrive empty, so naming
the file:line a log came from needs a `scriptId → url` lookup. That means the `Debugger` domain
enabled permanently plus a script map that is empty in the common lazy-attach case, since the engine
does not replay `scriptParsed` — too much standing cost for the payoff, so `game_console` prints no
call site. Revisit as opportunistic resolution: when the debugger is already attached and its
existing script map answers, render the frame; otherwise print nothing.

### Localization and custom effects in the engine skill

**Priority:** low · **Cost:** unsure

Coherent Labs' documentation corpus covers two topics `skills/gameface/` does not: localization
(text expansion, RTL mirroring) and custom effects, which `references/performance.md` names only as
a trap, since a stylesheet merely containing `coh-custom-effect` reverts the whole view to
synchronous style solving. Both plausibly bear on a mod shipping translated UI or a styled panel.
Scope before writing, and take the facts one at a time under
[ADR 0011](adr/0011-coherent-labs-documentation-corpus-stays-out-of-the-gameface-skill.md): the
subset that survives gating against the reference target's engine may be small enough to fold into
the existing references rather than earn new ones.

### Network inspection

**Priority:** low · **Cost:** moderate

Gameface implements the `Network` domain (observe + `getResponseBody` + cookies), but `Fetch` is
missing (no request interception). Surface request/response observation as tools. Note what already
arrives without it: `game_console` captures `Log.entryAdded`, which is where the engine reports a
failed resource load, so the error case is visible today and the marginal value here is bodies,
headers and timing rather than learning that a request failed at all.

### `fetch-doc.mjs sitemap` against a site with no sitemap

**Priority:** medium · **Cost:** cheap

`skills/gameface/scripts/fetch-doc.mjs sitemap` reads
`docs.coherent-labs.com/cpp-gameface/sitemap.xml`, which now answers 404, as do `/sitemap.xml`,
`/sitemap-index.xml` and `/robots.txt` (checked 2026-10-12), so the skill's documented way to find a
page fails and an agent falls back to guessing URLs, which mostly 404 too. The site's navigation
is the replacement: every page of `/cpp-gameface/` front-loads the full menu, about 745
`href="/cpp-gameface/…"` links, so the sitemap mode can take its URL list from the index page's
anchors, dropping the `_astro/` assets, and keep its output format.

### A UI trigger from a shell loop

**Priority:** medium · **Cost:** cheap

A before-and-after measurement in Cities: Skylines II repeats one UI action dozens of times across
builds, waiting on the mod's log between steps, and `game_eval` gives each step a tool call and a
15 s cap. A Node script of about fifteen lines (the global `WebSocket`, one `Runtime.evaluate` to
`ws://localhost:9444/json/devtools/page/0`) let a bash loop drive it unattended, beside the server's
own connection (prototype: `.scratch/tools/ui-eval.mjs` in Morgan's Find It checkout, local, not in
the repo). A one-shot CLI in the package, or the pattern in `gameface-driving`, would make this a
documented route.

### A wrapping flex item with margins is sized short of its content

**Priority:** medium · **Cost:** trivial

At Cohtml 2.2.1.3 in Cities: Skylines II, a flex item in a `flex-wrap: wrap` row came out narrower
than its content by its `margin-right` minus its `margin-top` and `margin-bottom`: an item with
`margin: 4rem 24rem 4rem 0` was 16px short, and its text overflowed the box. Nothing shows until the
box paints, so it read as a misplaced hover background, and three padding tweaks went nowhere before
measurement found it. The margins were what mattered: `margin-right: 0` made the item 8px too wide,
40 made it 32 short, all margins 0 made it exact, while the font size, `flex-shrink: 0` and wrapping
the text in its own element changed nothing. Spacing the items with padding, or with flex `gap`, which
lays out since 2.2, sizes them right. This belongs in `layout-styling-text.md` beside the margin and
`gap` notes, verified on one more row shape (no wrap, a column), since the probe covered only a
wrapping row of inline-content items. Found in Hall of Fame's Creator Dashboard detail caption,
session `449ee1b0-8f4e-4e34-a306-6263c9118606`.

## unity-devtools

Drive a running Unity Mono development build from the outside over the Mono Soft Debugger protocol
(SDB): discovery, live type reflection, C# expression evaluation, ECS entity/component/buffer
read-write, breakpoint/pause debugging (`debug_*`, `advance`) and screen capture, through one
persistent lazy-attach session.

### Cross-platform support

**Priority:** low · **Cost:** unsure

Discovery no longer stands in the way: `BeaconListener` joins the multicast group through
`NetworkInterface`, and the server ships as a platform-agnostic NuGet dotnet tool, so distribution
needs no per-RID artifacts. Both server-lifetime watchdogs now carry a Unix branch beside their
Windows one — `getppid` reparenting for `ParentWatchdog`, `poll`'s POLLHUP for `StdinWatchdog` —
each verified on Linux, so a reconnect no longer strands the previous server holding the exclusive
SDB slot (`docs/solutions/unity-mcp-server-stranded-on-reconnect.md`).

What remains is macOS, which nothing here has ever run on. Both Unix branches are POSIX rather than
Linux-specific and should hold, which is a prediction and not a verification; a live attach from
Linux to a game under Proton is the only non-Windows evidence there is.

### A network interface that appears after the server did

**Priority:** medium · **Cost:** moderate

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

### Wedge detection on wire progress rather than on age

**Priority:** medium · **Cost:** moderate

`UnitySession.Break` decides whether to sever the operation holding the gate by how long it has held
it, refusing anything younger than `wedgeAfter`. No value there can be right. An operation is any
number of bounded waits — the greeting poll, a suspend, N invokes each carrying its own park and
invoke bounds, a resume — so a healthy `eval` walking a large expression and a session stuck on a
wait that never fired are indistinguishable by age, and the threshold is honest today only because
its docblock says it is a speed bump against a reflex retry rather than a diagnosis. Its magnitude
is undecidable; only its ORDER is derived, from the gate wait it has to outlast, and a replacement
has to keep that or the ration stops firing on the one route the tools teach.

Last wire progress is the quantity that separates them: a timestamp bumped whenever the connection
completes a reply, against the largest single wait any one operation can legally sit in. Silence
longer than that is a wedge by construction, whatever the operation's total age, and it retires both
`wedgeAfter` and the ask-twice dance the refusal currently leans on. It would also give `status` a
field that means liveness — `busySeconds` reports age, which reads like health and is not.

The cost is where the stamp has to live. Every command funnels through the vendored
`Connection.SendReceive` and its reply path, so the natural site is another anchored patch in
`scripts/update-vendored-sdb.ts` plus its `VENDOR.md` bullet, beside the reply-deadline one already
there. Weigh that against surfacing it from `SdbSession` instead and accepting a coarser signal,
since every added patch is one more anchor that can fail to apply on the next re-vendor.

The same pass should close the one wait that is still unbounded, since a progress stamp does not
reach it. `Connection.Send`'s async reply path invokes its callback without checking the reply's
error code, and `Thread_GetFrameInfo`'s callback assumes a success payload — so an error reply
strands `ThreadMirror.FetchFrames`' latch and leaves `GetFrames` waiting untimed on an event nothing
will set. Every invoke ends in that frame refresh, so it is not an obscure corner. An error-code
check in the callback wrapper is the smaller of the two fixes and probably the right one;
`docs/solutions/a-wedged-session-that-swallowed-its-own-escape-hatch.md` has the chain.

Worth settling in the same pass: the sever branch ships unexecuted. `gateWait` took a constructor
override so a test could drive the refusal, and `detachWait` now derives from it, but nothing
reaches `Break`'s `Abort()` without waiting out `wedgeAfter` for real. A progress stamp is
straightforward to drive from a test in a way an age is not.

### Finding a Unity Editor

**Priority:** medium · **Cost:** unsure

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

### Several entities in one `ecs_get_component` call

**Priority:** medium · **Cost:** moderate

`ecs_query`'s `select` reads component values off every entity a query lists, which served the
censuses this entry used to record. What it cannot reach is an entity no query sees: `ecs_query`
excludes entities tagged `Prefab` or `Disabled` and those whose queried enableable component is off,
and an entity list can come from a buffer's elements or a `follow` hop rather than from a query. A
multi-entity mode on `ecs_get_component` is the only batch route to those, and it should build on the
row read `select` added to `Ecs`.
Shape settled so far: a second parameter per axis (`entities` beside `entity`) rather than silently
widening the existing one, and an entity-level failure reported as a row rather than failing the
call. A "rectangle" tool merging this with `ecs_list_components` was rejected: one answers what is on
an entity, the other what is in a component.

### An array-level read route for `select`

**Priority:** low · **Cost:** moderate

`select` reads per entity through the generic accessor, one invoke per distinct selected component
per row plus its guards. `EntityQuery.ToComponentDataArray` would read a whole column in a constant
number of invokes whatever the row count. It applies only to a component the query filters on, it
reads every match whatever `limit` lists, and this plugin has never called it: its allocator argument
has to be settled live, since `ToEntityArray` and `GetComponentTypes` already disagree on theirs on
the same target. It wants a measured census whose freeze justifies it before it is built.

### `Unity.Entities` stubs in the integration fixture

**Priority:** low · **Cost:** large

No test covers any ECS path: the integration fixture is a Mono console debuggee that cannot carry
Unity Entities, so `ecs_query`'s row read, the presence gate and the storage-kind gate are all
verified by hand against a running game. Stub types under the `Unity.Entities` names the library
reaches — `World`, `EntityManager`, `ComponentType`, `TypeManager`'s flag constants, the generic
accessors — would give every ECS tool a test seam.

### A buffer filter on `ecs_query`

**Priority:** low · **Cost:** cheap

`ecs_query` matches on component presence, so it cannot tell an entity whose `DynamicBuffer` holds
elements from one whose buffer is empty. Settling whether the debug proxy's buffer read pins a
`bool`-carrying element type needed a non-empty buffer, and finding one meant probing entities one
at a time: `Game.Rendering.Emissive` twice, `Game.Net.ArrowPosition`, `Game.Rendering.Skeleton` —
all length 0 — before `Game.Net.LabelPosition` came back with 51. Either report the length beside
each listed entity when a queried type is a buffer, or take a minimum-length filter; the first is
cheaper and answers the same question.

### A by-reference generic struct into an assembly whose metadata cannot be read

**Priority:** low · **Cost:** trivial

`eval` refuses a generic struct or nullable bound by reference, since the debugger agent disconnects
the VM on one, but it learns how a parameter is passed from the assembly's metadata. Where that
cannot be read, a dynamic or unfetchable assembly, a plain argument into an `in` parameter is sent
anyway: `SendableByReference` passes on null modes (`modes?[i] is ParameterMode.Ref or
ParameterMode.In`). No hit records it. The fix refuses an at-risk value there whatever the slot,
trading a rare by-value call into such an assembly for the game staying up.

### Reading a log file off the game machine

**Priority:** medium · **Cost:** cheap

The game's own logs are the primary evidence for a whole class of diagnostics claims — the version
block, the modding runtime line, the debugger-port lines, the absence of a warning — and no tool
reads them. `System.IO.File.ReadAllText` through `eval` covers `Logs/*.log`, but `Player.log` is
held open by the player and answers `Sharing violation`; the 1.6.2f1 sweep's workaround was
`new FileStream(path, Open, Read, FileShare.ReadWrite)` wrapped in a `StreamReader`, which is a lot
of incantation for "show me the log". A read-file or tail-N-lines verb, sharing-mode correct by
construction, would serve every such claim directly.

### A system's query descriptor

**Priority:** low · **Cost:** moderate

`ecs_query` builds its own query and cannot be pointed at one a system already holds, so reading
what `SerializerSystem.m_Query` or `ClearSystem.m_ClearQuery` actually matches took reflection plus
`GetEntityQueryDesc()` and then hand-indexed reads of a 19-element `ComponentType[]` — no loops, so
two calls and some 600 characters to list one query. A verb reporting a named system's queries with
All/Any/None resolved to type names would collapse roughly six round trips per query to one, and the
same read recurs whenever a claim is about what a vanilla system sees.

### `find_types` reports no statics

**Priority:** medium · **Cost:** trivial

With `members: true` the tool lists instance fields and properties and omits static ones, so
`Game.Version` came back with empty `fields` and `properties` and the sweep had to guess
`Game.Version.current` rather than discover it. Statics are exactly where a game keeps its
singletons and version surfaces, which makes them the members an orienting agent most needs.

### `find_types` cannot search by what a type implements

**Priority:** low · **Cost:** cheap

`search` matches names, so a type known only by its role is found by guessing. Needing one shared
and one managed component to verify `ecs_query`'s refusals, a session guessed
`Game.Common.UpdateFrame` (wrong namespace), searched `(Shared|Managed)` by name for fifteen
unrelated hits, and guessed `Unity.Entities.CompanionLink`, which turned out a plain component; the
managed case stayed unverified. A filter on an implemented interface, or on the storage kind the ECS
tools already classify, would answer it in one call.

### Reaching a prefab by name

**Priority:** medium · **Cost:** cheap

`ecs_query`'s `label` annotates each listed entity through a managed system's method, but nothing
filters on what it produces, and no tool takes a prefab name. Reading eight named
`NotificationIconData` prefabs out of 160 during the 1.6.2f1 live-read sweep therefore meant listing
all 160 with labels and reading the indices back off the dump. `eval` reaches one prefab whose type
and name are known: `PrefabSystem.TryGetPrefab(new Game.Prefabs.PrefabID(type, name), out var p)`,
then `PrefabSystem.GetEntity(p)` for its entity. Releases up to 1.3.0 bind no trailing default, so
there the hash is written out, `new Colossal.Hash128()`. A name pattern, or a name of unknown type,
stays out of reach. Either let `label` double as a filter, or take a name pattern beside the
component list.

### `ecs_query` ANDs its components, with no any and no none

**Priority:** medium · **Cost:** cheap

The tool matches entities carrying ALL the listed types, so the two other shapes the engine's own
`EntityQuery` vocabulary carries — `WithAny` and `WithNone` — have no expression. In the same sweep
the post-facility census (`PostFacilityData` or `MailBoxData`) took two queries and a hand-reconciled
overlap, and counting owned map tiles (`MapTile` without `Native`) took two queries and a
subtraction. An `any` and a `none` list beside `components` makes both one call.

### `eval` cannot cast to an array type

**Priority:** medium · **Cost:** moderate · **Hits:** 4 as of 2026-09-29

`(Game.Net.LabelPosition[])em.Debug.GetComponentBoxed(entity, type)` fails to parse:
`unsupported: array type`. The workaround is `(System.Array)` plus `GetValue(int)`, which reads the
elements back but renders each one truncated (`float3 {...}`), so a call returning an array is
unusable at its natural type. Every `EntityManager.Debug` route that boxes a buffer hands back
`object` over a `T[]`, and so does any game method returning an array, so accepting `T[]` in the
cast grammar — and in `typeof` with it — closes a class of read rather than one call.

### A cast to `object` does not steer overload choice

**Priority:** medium · **Cost:** unsure

Reproduced in the fixture: `TestFixture.Overloads.Pick((object) o)` with an int `o` returns
`"int"` where C# binds `Pick(object)`. A reference cast on a client-side value is a no-op, so the
argument reaches overload matching as the int it was. The call succeeds and runs the wrong method,
which is the one outcome the eval contract rules out — an edge may diverge, but loudly. Carrying
the cast's static type to `SelectOverload`, or boxing at the cast, would make the written type the
one that binds; which is cheaper is what the cost turns on. Member lookup ignores a cast the same
way: `((System.Collections.ICollection) array).Count` fails, as an array implements `Count`
explicitly and the lookup runs on `T[]`.

### A mutation through a box no slot holds is lost

**Priority:** medium · **Cost:** moderate

A mutating call on a struct boxed behind an interface stores the changed struct back only when a
field, an array element, or a frame slot holds the box. Two shapes reach a box with nothing behind
it and drop the call silently. A getter returning it: with `t` a `Tuple<IBump>`,
`t.Item1.Bump(); t.Item1.P` reads 0 where C# reads 1. A cast to the interface:
`((IBump) Boxes.Ifc).Bump()` leaves `P` at 0. Silent is the one outcome the eval contract rules out.
The wire decodes a box as its struct and carries no box identity, so the call runs on a client copy;
running it on the agent's own object instead is what the cost turns on. The member-assignment form
through a getter (`t.Item1.P = 3`) fails loudly today, where C# accepts it on an interface
reference.

### `?:` with a nullable branch

**Priority:** low · **Cost:** cheap

In C#, `flag ? h.Maybe : 0` has type `int?`, the `0` lifted. The evaluator returns whichever branch
ran as it is, so the result is an `int?` on one branch and a plain int on the other, and a member
only the nullable has (`.HasValue`) fails when the plain branch ran. Lifting the other branch when
one is nullable is the fix.

### Type-argument inference beyond a plain `T` parameter

**Priority:** low · **Cost:** moderate · **Hits:** 1 as of 2026-10-21

Inference reads only a parameter typed as the type parameter itself and takes each argument's
runtime type. It refuses two arguments fixing `T` to different types, and a null beside an argument
fixing `T` to a type that takes null. C# also infers through `T[]` and `List<T>`, and fixes `T` to a
type every argument converts to. Each refusal is loud and names its fix, writing the type argument.
Reading `T[]` and generic-instance parameters is the step agents would reach first; the common-type
cases need static types the evaluator does not track.

### Calls C# rejects as ambiguous bind silently

**Priority:** low · **Cost:** large

Where C# reports CS0121, the evaluator picks one overload and runs it: a null into a nullable slot
against a reference slot, variance and array-covariance ties, related interfaces, a lifted
conversion against a numeric widening, and an integral argument against a narrower and an enum
sibling. No valid program contains these calls, so there is no C# answer to diverge from, yet the
call runs a method the caller did not choose. Reporting the tie needs a better-conversion ranking
the binder does not have, which is the cost.

### A call on a boxed primitive fails with the agent's own error

**Priority:** low · **Cost:** cheap

A method called on a boxed primitive a debuggee call returned (an `object` holding an int) fails
with the agent's raw invoke error, which names neither the box nor a way out. A refusal naming the
box and suggesting the cast to its primitive type would make it actionable.

### The evaluator's structure

**Priority:** low · **Cost:** moderate

Two refactors parked for a go-ahead. Overload binding (tiers, inference, cost ranking, and the
null rules) has grown inside `EvalInterpreter` into an `OverloadResolver` waiting to be extracted.
`FrameScope` repeats its `this`, local, and parameter dispatch across `TryResolveValue`,
`TrySetValue`, `StructWriteBack`, and `BoxSlotType`, and the last two are two queries the
interpreter reassembles into one link; one slot query feeding `SlotLink` would merge them.

### `eval` cannot build an `EntityQuery`

**Priority:** medium · **Cost:** large · **Hits:** 17 as of 2026-10-20

Two independent walls, both hit reaching for `ToComponentDataArray`. User-defined implicit
conversions are not applied, so `new Unity.Entities.EntityQueryBuilder(Unity.Collections.Allocator.Temp)`
fails and the explicit cast is rejected as well (`cannot cast Allocator to AllocatorHandle`); the
workaround is the ready-made handle `Unity.Collections.AllocatorManager.Temp`, or one built by hand,
`new AllocatorManager.AllocatorHandle { Index = 2, Version = 0 }`. Past that, chaining
`WithAll<T>()` throws `BadImageFormatException: Cannot box IsByRefLike type
'Unity.Entities.EntityQueryBuilder'`. The builder can be skipped: `em.CreateEntityQuery(all)` takes a
`ComponentType[]` built as the `params` entry below says. The whole-set field census that
drove both attempts is served by `ecs_query`'s `select`; what stays open is the builder itself,
through honouring `ref struct` receivers. The cost line prices that route; the
implicit-conversion half alone reads cheaper.

### `ecs_query`'s `label` is empty for prefab entities

**Priority:** medium · **Cost:** cheap

`label` with `Game.UI.NameSystem:GetRenderedLabelName` returns `""` for every prefab entity, so a
prefab census comes back as bare indices with nothing to match a name against. Naming 126 prefabs
took four batched `eval`s calling `PrefabSystem.GetPrefabName` per entity. A `label` that falls back
to `PrefabSystem` when the name system has nothing, or a `names: true` flag that does, collapses
that to one call — and it is the same read the prefab-by-name entry above wants to filter on.

### `eval` cannot read an iterator-backed property

**Priority:** low · **Cost:** moderate · **Hits:** 1 as of 2026-10-21

`ProxyAction.bindings` is unreachable: `.Count` fails on the compiler-generated iterator type and
nothing can enumerate it. The read only completed by falling back to the private `m_Bindings` field
behind it. Enumerating a returned `IEnumerable` into a rendered list is the general fix; short of
that, "a private backing field is the escape hatch" is a fact the `unity-driving` skill should
carry, because the failure gives no hint that one exists. The same fix serves a plain `List<T>`,
which renders as its type name: reading the game's binding table (`InputManager.GetBindings`, a
`List<ProxyBinding>`) took `System.String.Concat<ProxyBinding>(list)` written to a file on the game
machine. A method returning an iterator fails the same way, a cast to
`System.Collections.IEnumerable` included, since member lookup ignores the cast.

### The `eval` grammar's type-name surface

**Priority:** medium · **Cost:** moderate · **Hits:** 7 as of 2026-10-21

Three rejections in the same family, each costing a round trip to discover. A *generic* `typeof` or
cast with a fully-qualified name fails — `typeof(Unity.Collections.NativeArray<int>)` gives
`parse error at offset 15: unsupported: qualified name` — while a non-generic qualified `typeof`
works, so the
grammar handles qualified names and generic arguments but not their combination. And a nested type
is unreachable through C# `.` syntax, which made every job-attribute check in the sweep go through
`System.Type.GetType("Outer+Nested, Game")`. The same `System.Type.GetType("Ns.Type, Asm")` string
is the workaround for both, which is the tell that one resolver handles the string form and the
grammar does not reach it. `new` with a generic qualified type fails the same way; with an instance
of the type at hand, `System.Activator.CreateInstance(x.GetType())` builds another, which a typed
parameter and an indexer accept as `object`. An explicit method type argument fails too:
`System.Linq.Enumerable.Any<System.Func<FindIt.Domain.PrefabIndex, System.Boolean>>(x)` gives
`unsupported: qualified name`, closing the route an inference refusal names when the type needs
qualifying. And a predefined type keyword fails as a receiver:
`object.ReferenceEquals(a, b)` gives `unsupported: predefined type`, where
`System.Object.ReferenceEquals(a, b)` works.

### `params` expansion in `eval`

**Priority:** medium · **Cost:** moderate · **Hits:** 8 as of 2026-10-20

Overload matching takes a variadic method's array as one argument and array creation is outside the
grammar, so `CreateEntityQuery(typeof(A), typeof(B))`, `String.Join(",", a, b)` and `Split('\n')`
need the array built first: `System.Array.CreateInstance(typeof(T), n)` plus `SetValue(x, i)` builds
one, and a local holding it binds a `T[]` parameter (`em.CreateEntityQuery(all)` worked). Documenting
that route at `unity-driving/SKILL.md:74`, which calls such a method out of reach, is the cheap fix;
packing the trailing arguments into the parameter's array type when no overload binds them as
written is the full one.

### Breakpoints in async methods

**Priority:** medium · **Cost:** moderate · **Hits:** 1 as of 2026-09-29

`debug_set_breakpoint` on an `async` method fails with `Cannot set breakpoint on the specified IL
offset`, since the body lives in a compiler-generated state machine. A session found
`+<Next>d__12` and its `MoveNext` by hand. Following `AsyncStateMachineAttribute` (and its iterator
sibling) to `MoveNext` makes the method name the user sees the one that works.

### An invoke that throws while an exception break is armed never answers

**Priority:** high · **Cost:** unsure

With `debug_break_on_exception` armed on every exception, an invoke whose target throws in the game
stops at the break instead of returning: the pump records an `exception` pause, the invoke waits out
its 60 s limit, and the session is dropped. Reproduced in the fixture by harvesting a fresh
`TypeCatalog` over an assembly whose `GetTypes` throws. Every invoke goes out with
`InvokeOptions.None`, so a throwing `eval` should hang the same way, and a mistyped type name in a
debug tool now reaches it through the catalog's first harvest. Suppressing breakpoints for the
plugin's own invokes (`InvokeOptions.DisableBreakpoints`) is the likely fix, untested.

### An invoke releases a thread paused at a breakpoint

**Priority:** medium · **Cost:** unsure

Every invoke goes out with `InvokeOptions.None`, so the agent resumes the whole VM while it runs. A
worker thread paused at a breakpoint runs on during a frameless `eval`: in the fixture, a worker
paused inside a static constructor finished it and exited, and the next `debug_evaluate` failed with
`the paused thread has no managed frames`. `SingleThreaded` holds every other thread, at the price
of deadlocking an invoke that waits on one of them; which callers can take that is what the cost
turns on.

### The frame-write journal's blind spots

**Priority:** low · **Cost:** moderate

`FrameScope` answers a frame local's reads from what the current pause wrote to it by name, since the
agent serves a register-held local stale after an invoke. Three shapes fall outside it, each listed
among the plugin's divergences. Under a held suspend, which publishes no pause, a local written
before an invoke reads its old value from the next evaluation on, `debug_pause_state` included. A
write reaching the local through another frame's `ref` parameter or struct `this` is missed. A
breakpoint condition's writes do not carry into the pause it publishes. Keying the journal to the
session's resume count rather than to the pause covers the first and the third; the second needs
alias tracking.

### A frame write of a client value into a nullable slot

**Priority:** low · **Cost:** cheap

`maybe = 5` at a breakpoint is refused for an `int?` local, where C# wraps it; the workaround is a
debuggee-side int of the underlying type. Building the `Nullable` struct client-side, or checking
that the agent wraps a client primitive as it does a debuggee one, would lift the refusal.

### A static read in a worker frame while the main thread runs the constructor

**Priority:** low · **Cost:** cheap

The static-constructor guard admits a type whose constructor the main thread is running, since every
invoke runs there. With the main thread blocked inside `T`'s constructor and a worker paused at a
breakpoint, `debug_evaluate` in the worker's frame reads `T`'s partly assigned statics where C# on
the worker would wait. Scanning only the frame's own thread when a frame scope is present would
close it.

### Unnamed threads reported as the main thread

**Priority:** low · **Cost:** trivial

`DebugController` labels any thread without a name `(main)`, so a pause on an unnamed worker
reports `thread=(main)`. Comparing the thread with `Invoker.MainThread` would name it right.

### Extension methods through instance syntax

**Priority:** low · **Cost:** moderate · **Hits:** 3 as of 2026-09-29

`gameMode.IsEditor()` fails, and the miss names the static form (`GameModeExtensions.IsEditor(gameMode)`)
when a bounded lookup finds the declaring class. Resolving extension methods for the receiver, so the
instance form just works, is what remains.

### `eval` does not read an inherited static field

**Priority:** low · **Cost:** cheap

`Derived.Count` fails when `Count` is a static field its base type declares, though C# resolves it
and an inherited static property already reads through the accessor lookup. The miss names the
declaring type (`Base.Count`), so recovery costs one retry. The static field read and write look at
the type written alone; walking the base chain there makes the expression work as written.

### `ecs_set_component` writes no vector fields

**Priority:** low · **Cost:** unsure · **Hits:** 1 as of 2026-09-29

`ecs_set_component` rejects a `Unity.Mathematics.float2` field with `unsupported target type`, so
the write falls back to an `eval` get-modify-set. Worth doing only if the whole
`Unity.Mathematics` vector family comes in one change.

### A tool exception with an empty message

**Priority:** low · **Cost:** trivial · **Hits:** 1 as of 2026-09-29

`ToolGuard` forwards `ex.Message` as is, so an exception carrying none surfaced as `An error occurred
invoking 'ecs_get_buffer'.` with nothing to act on. Fall back to the exception's type name.

### `advance` drops its `after` snippet's failure

**Priority:** low · **Cost:** cheap

`advance` takes care of the window and of what surrounds it: a `before` snippet that ran and then a
window that failed comes back with the flip named and the after snippet run to undo it. The `after`
snippet itself runs outside that care, so its own failure propagates raw — three calls this session
ended on `statement 1 (var p = System.IO.Path.GetTempPath() + "probe.png";) failed at offset 0: The
vm is not suspended`, with the window spent, the game advanced and neither fact reported. The value
the snippet existed to read is gone, and the caller cannot tell a snippet bug from a session one.
Report a failed `after` the way a failed window already is: what the window did, then what the
snippet could not do.

### Watching for a debuggee-side change costs a window per sample

**Priority:** low · **Cost:** moderate

`advance` clamps to 0.1 s and the `eval` grammar has no loop, so waiting on a state the game
produces means one tool call per sample, each spending a full window whether or not the state
arrived. Bracketing when `ScreenCapture.CaptureScreenshot` puts its file on disk took four calls and
still never caught the write; a state that passes faster cannot be observed at all. A predicate mode
— advance until a snippet returns true, bounded by a timeout and reporting how long it took —
collapses the loop into the one place that can run it without a round trip per turn, and is the
shape `debug_wait` already has for pauses.

### Fault injection at a paused call

**Priority:** medium · **Cost:** cheap

`unity-driving` arms a conditional breakpoint to catch a hot path, never to make a call fail. A
method-entry breakpoint on a BCL call, conditioned on its argument (`System.IO.Directory.Move` with
`sourceDirName.EndsWith(".new")`), pauses the game just before the call; changing the outside world
under the pause (moving the folder away, corrupting a zip), then resuming, fails the call as a
player's machine would. That builds a red case for an I/O failure path with no test seam, which a
fix to another author's mod cannot add: Asset Icon Library's unpack fixes were tested this way. A
paragraph in the skill's breakpoint section would carry it.

### Injected in-game helper (exploratory, opt-in)

**Priority:** low · **Cost:** large

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
mirrors today); temporal captures (record a value across N frames, return the series); an
in-memory screen capture, which needs end-of-frame execution and would give the `screenshot`
tool a debuggee-side pixel size through `ImageConversion.EncodeArrayToPNG`, the only place a
downscale costs no server-side image decoder (settle first whether such a capture still
composites the UI, since a `RenderTexture` render draws one camera and may drop the layer that
matters); plus a
version/handshake method (detect a stale helper after a plugin update; no reload until game
restart) and structured try/catch so in-game exceptions come back as data. User-compiled
lambda execution would come only as a later layer on the same gateway, where the no-unload
leak actually bites.

### A native screen capture beside the engine route

**Priority:** low · **Cost:** large

`screenshot` drives the engine's own `ScreenCapture.CaptureScreenshot`, which is a request only a
rendered frame fulfils, and everything awkward about the tool follows from that: it has to run the
game to get its frame, so it spends up to 0.2 s of simulation inside a caller's suspend window,
budgets two windows and a retry, has to tell a slow renderer apart from a game drawing nothing, and
refuses outright while a breakpoint holds the game. The frame then crosses SDB as several megabytes
of base64, freezing the main thread for the encode. A compositor capture (`Windows.Graphics.Capture`)
reads an already-presented frame instead and erases all of it — including the refusal, since the
last presented frame is exactly what an agent inspecting a breakpoint hit wants, and the engine
route structurally cannot give it. It also pairs with the deferred downscale knob: both want pixels
held server-side, which is why the shipped tool has no size lever.

Three costs. It is Windows-only, and the plugin no longer has any Windows-only code — the watchdogs
above each carry a Unix branch now — so this would be its first, and a user-visible feature that
simply does not exist elsewhere rather than a platform detail nobody sees. It needs a dependency and carries a trap: `BitBlt`/`PrintWindow` commonly return black
for GPU-composited or exclusive-fullscreen games, which is most builds worth screenshotting, so
doing it properly means `Windows.Graphics.Capture` (Win10 1803+) and its capture-border quirk,
against the two package references `mcp/` carries today. And it does not escape the debugger: the
beacon carries host, port, guid and an id string but no pid and no window handle, so finding the
window still costs one `eval` of `System.Diagnostics.Process.GetCurrentProcess().Id` per attach and
a match through `EnumWindows`.

The two routes also photograph different things. The engine route returns the game's own back
buffer at native resolution whatever window sits on top of it; a compositor capture returns what a
screen recorder sees, so occlusion becomes a failure mode the engine route does not have. Keep the
engine route as the portable default and gate a native fast path on the server sharing a desktop
session with the game.

### GameObject/MonoBehaviour tools

**Priority:** unsure · **Cost:** large

The current surface is ECS + expression evaluation; add tools for the classic Unity object model
(scene hierarchy, GameObject/MonoBehaviour inspection and mutation).

### Unity on CoreCLR

**Priority:** low · **Cost:** unsure

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

### A `unity` write right after a UI trigger hung the game

**Priority:** medium · **Cost:** unsure

In Cities: Skylines II, a `unity` eval setting `ToolSystem.activeTool`, sent right after a gameface `engine.trigger`, deadlocked the main thread in `cohtmlNative.EndEvent` ← `RawValueBinding.Update` ← `ToolUISystem.OnToolChanged`: the UI port stopped answering and the watchdog wrote a hang dump.
Seen once, cause not traced: the write calls into the UI view from inside the eval's suspend window, while the view may still be handling the trigger.
Reproduce it, then either have `eval` warn on a write that reaches a UI binding, or add the rule to `unity-driving`'s cross-plugin loop: drive the UI through gameface, and keep the `unity` evals between triggers to reads. Found while verifying a Find It review finding.

### The NuGet server lags the skill under one version number

**Priority:** high · **Cost:** cheap

The plugin launches its server with `dotnet dnx UnityDevtools.Mcp --version 1.3.0`, and `dnx` serves a version from the NuGet cache once it holds one.
On this machine that cache holds a 1.3.0 server built on 2026-10-10, while plugin 1.3.0's skill and source, refreshed on 2026-10-22, document features committed after it under the same version, `default(T)` among them (1c66a8e, 2026-10-17).
So the skill promises what the server refuses: in ToveruxModTemplate on 2026-10-23, `var s = default(UnityEngine.InputSystem.LowLevel.KeyboardState);` failed with `unsupported: default expression`, and the agent fell back to `new KeyboardState { }`.
Bumping `<Version>` in `UnityDevtools.Mcp.csproj` and the `.mcp.json` pin with every server change, and a check that fails a server change landing without the bump, keeps the two in step.

### A mutating call on a struct with a fixed buffer fails

**Priority:** medium · **Cost:** unsure

In ToveruxModTemplate on 2026-10-23, on the 2026-10-10 server build above: `var s = new UnityEngine.InputSystem.LowLevel.KeyboardState { }; s.Set(UnityEngine.InputSystem.Key.LeftCtrl, true);` passed, and the next `s.Set(UnityEngine.InputSystem.Key.K, true);` failed with `Unable to cast object of type 'System.Byte[]' to type 'System.Byte'`.
`KeyboardState` holds its keys in a `fixed byte` buffer, which the wire carries as the compiler's `<keys>e__FixedBuffer` struct, so writing the local back after the call seems to assign the buffer's bytes where one byte is expected.
Unverified against the current source: reproduce with a fixture struct holding a fixed buffer and a method writing to it.
The workaround was to build the input from the device's own layout instead (next entry).

### Pressing a key the game reads natively

**Priority:** medium · **Cost:** moderate

`game_key` reaches the UI's DOM handlers only, and nothing in either plugin presses a key that Unity's Input System reads, the route a game's own key bindings take.
In ToveruxModTemplate on 2026-10-23, verifying a mod's Ctrl+K binding took four evals.
A state event built from the device's layout (`StateEvent.From(Keyboard.current, out var ev, Allocator.Persistent)`, then `InputControlExtensions.WriteValueIntoEvent(kb.kKey, 1f, ev)` and `InputSystem.QueueEvent(ev)`) was dropped while the game window was unfocused: the Input System's `backgroundBehavior` was `ResetAndDisableNonBackgroundDevices`.
Switching it to `IgnoreFocus` and calling `InputSystem.EnableDevice(kb)` let the press and release through, after which the setting was restored.
A `unity` `key` tool taking a chord (`ctrl+k`) would press, advance a frame, release, and lift the background gate only for its own window, restoring it after.
An agent driving a game on another machine or on Wayland has no other way to send that key.

## cs2-modding

Skills and references teaching an agent to write Cities: Skylines II code mods, distilled from the
decompiled game, the wiki and a corpus of open-source mods. Knowledge only: no MCP server, no
runtime, and no shipped code artifacts.

### A check for the cites research files make into each other

**Priority:** medium · **Cost:** trivial

`docs/research/README.md` lets a file cite a sibling as `<name>.md:<line>`, and those rot the moment
the target gains a line above the target — which every authoring pass and every sweep does. The
1.6.2f1 review found 32 of them landing on blank or out-of-range lines; a sampling reviewer found 6
of the 32, and a twenty-line script found all of them in a second. The check is mechanical: resolve
each cite against the citing file's own directory, open the line, fail on empty or out of range.
It belongs beside `check-skill-content.ts` in `mise check`, where nothing external is needed — unlike
`research:cite-audit`, which reads a local decompile and so cannot run in CI.

### Extracting the shipped localization dictionaries

**Priority:** low · **Cost:** cheap

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
the honest framing rather than a user-facing feature. And every base locale file carries trailing bytes past
their declared end, so end-of-file is not end-of-data and the decoder has to stop on the counts.

A second consumer, met in a mod's translation pass: an agent translating a mod into the game's
languages wants each locale's official terms (photo mode, milestone, playset) so the mod reads like
the vanilla UI, and decoding the dictionaries by hand was a detour. `cs2-modding-setup` could decode
them into a machine-local directory recorded in `~/.cs2-modding/setup.md`, beside the decompile;
a local copy commits nothing, so it stays on the right side of the copyright line.

### Marker namespace lint

**Priority:** low · **Cost:** cheap

A mechanics file's `VOLATILE:` marker lists the namespaces its names live in, and re-closing that
list by hand drifted across six files in one review gate. `check-skill-content.ts` can enforce the
mechanical half: every `src/Game/<namespace>/` path a file cites must have its namespace in the
file's marker. Types named without a path stay the reviewer's job.

### A source-first sweep of the shipped references

**Priority:** medium · **Cost:** moderate

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

**Priority:** low · **Cost:** moderate

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

### What serializes a `CallBinding`'s result

**Priority:** medium · **Cost:** cheap

A `CallBinding` hands its callback to `view.BindCall` (`RawCallBindingBase.Attach`), so its return
value reaches JavaScript through cohtml's own reflection `Exporter`, never through the
`IWriter`/`IJsonWritable` path a `ValueBinding` takes, so a returned type's own `Write` never runs.
Neither `binding-layer.md`
nor any other reference says which serializer a call result meets; a task author met it while
writing a panel task and found nothing to cite. A short section in `binding-layer.md` would settle
it, sourced to `RawCallBindingBase.cs` and `cohtml.Net/Exporter.cs`.

### `SystemAPI` needs the Entities source generator

**Priority:** medium · **Cost:** cheap

Every `SystemAPI` member's body is `throw InternalCompilerInterface.ThrowCodeGenException()`
(`src/Unity.Entities/Unity.Entities/SystemAPI.cs`): the call only works where the Entities source
generator rewrote it at compile time. `ecs-in-this-game.md` lists `SystemAPI.Query<T>()` and
`SystemAPI.QueryBuilder()` as ordinary forms, and `placement-definitions.md` uses
`SystemAPI.QueryBuilder()` in an example, without saying a mod build has to run the generator for
any of them. Whether the mod template's build does is the fact to establish first; the references
then either say so or steer to `GetComponentLookup`/`EntityQueryBuilder` on the system itself.

### A crashed session's minidump

**Priority:** medium · **Cost:** cheap

`diagnostics.md` saves a crashed run's logs but never names its minidump, the one record of a native
crash: one in V8 left `Player.log` without its last seconds and no managed exception anywhere.
Crashpad writes it to `.cache/backtrace/crashpad/reports/<id>.dmp` under the user data root, beside
the snapshots it takes on a quit or a hang, which read `Simulated Exception` and are no crash.
`minidump-stackwalk` reads one on Linux without symbols, and module offsets alone tell a V8 frame
called from cohtml from Mono (a script or binding crash) from an engine one. Its `--brief` prints
only the crashing thread, which in a hang snapshot is the Backtrace watchdog: the main thread is
Thread 0 of the full output. Which quits write a snapshot is the fact to settle first; seen on a
Proton install.

### Restarting the game and waiting on it, unattended

**Priority:** high · **Cost:** moderate

No reference says how an agent restarts the game after a build and knows when it is ready, so each mod session works it out again.
Morgan's mod template has a working version to port: `scripts/game.ts`, its log reader `scripts/game-log.ts`, and the `tmt-game-cycle` skill, in `~/Documents/Dev/ModernModTemplate` (`toverux-mod-template`, headed for the CitiesSkylinesModding organisation).
It supersedes NetworkTools' machine-local `game.sh` (`~/Documents/Dev/cs2-networktools/.scratch/tools/`), which it was built from.

- A cross-platform Bun CLI: `restart [--load <save> | --new [<map>]] [<game options>]`, `stop`, `wait-load`.
  Game options come bare: mise strips the first `--` from a task's arguments. The game's parser (Mono.Options) takes `--opt`, `-opt` and `/opt`, stops at `--` and ignores an unknown option silently, so the launch prints the options it wrote to `runOnce.txt`.
  It loads through the game's `--startGame=<guid>` option at launch, which races no main-menu trigger, and reads a save's guid from its `.cok`, a stored zip, since the guid changes at each save.
  Readiness comes from `Logs/SceneFlow.log`: `MainMenu reached`, or a `Loading mode Game|Editor` line followed by `Loading completed`, after which the loading screen stays up some 20 s more.
- Process control: `Cities2.exe` by exact name and, on Linux, Steam's reaper for the app, which outlives the game and makes Steam ignore a launch while it lives; a launch starts and ends a first reaper before the game's, so a launch waits for `Cities2.exe` itself, and crash detection and the game-left-running report read `Cities2.exe` alone.
  Launch through `systemd-run --user` on Linux, because a Steam started from the agent's shell passes that environment, tokens included, into crashpad dumps uploaded to Colossal Order.
  Stop: a minute's grace for a game quitting from the `unity` eval (a script cannot share that session's exclusive debugger slot), then SIGTERM, then SIGKILL (`taskkill`, then `/F`, on Windows).
- It recovers from a game that crashes, hangs or never launches, or hands the game back with its PIDs: a failed launch stops what it started and deletes `runOnce.txt`, so the next launch does not inherit its options. Verified on Linux by killing, freezing (SIGSTOP) and never launching the game; the Windows branch is unverified.
- The template's in-game test runner (`scripts/test-game.ts`, `scripts/scenario-log.ts`, the `tmt-game-testing` skill) uses the launcher and could follow it: the game's runner reports a scenario a success when its preparation throws, and aborts a scenario while earlier errors are pending, which the runner and a scenario base class work around.
  The runner takes one `[TestPrepare]` per scenario, base classes included, and throws on two. An exception a system throws at the main menu lands in `SceneFlow.log`, naming the mod only in its stack. `TestScenarioSystem.instance.scenarios` returns the live dictionary, so registering needs no reflection unless the order matters.
- Loading another save from a running game: the gameface `menu.loadGame` trigger, once the menu's save list filled (`src/Game/Game.UI.Menu/MenuUISystem.cs:797`), some 30 s after `MainMenu reached`; sent earlier, it throws a `NullReferenceException`. `GameManager.instance.Load` from the eval holds the main thread the load needs, and the game must be killed.
- For a screen recording or any scripted run in a loaded city, `CameraController.inputEnabled = false` (`src/Game/Game/CameraController.cs:248`, through `CameraController.TryGet`) stops edge scrolling and the camera keys from moving the view.

Shape, not settled: a `cs2-modding` technique reference on the dev loop's run half, with the CLI as tooling it ships.
Once it ships, the template drops its copy (`scripts/game.ts`, the `game` mise task, the `tmt-game-cycle` skill) and points its runner and AGENTS.md at the plugin's; annotate the template's spec, `.scratch/mod-template/spec.md`, if it is still there.

### `EntityQuery.Matches` and the unchecked query-mask limit

**Priority:** medium · **Cost:** trivial

`ecs-in-this-game.md` never mentions query masks. A world has 1024: `Matches` (or `GetEntityQueryMask`) takes one for each distinct query descriptor it is called on, deduplicated by descriptor hash (`src/Unity.Entities/Unity.Entities/EntityQueryManager.cs:576-596`, `:1043`), and frees none before the world goes.
The limit check, `ThrowIfEntityQueryMasksIsGreaterThanLimit` (`:1032`), is `[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]`/`[Conditional("UNITY_DOTS_DEBUG")]`, so the game's release build skips it, and the 1025th mask writes past `Archetype.QueryMaskArray` (`fixed byte[128]`, `Archetype.cs:80`) instead of throwing.
The rule to add: call `Matches` only on a bounded set of queries with fixed descriptors, never on queries whose component set varies with the input. Found while reviewing a Find It change that filters changed prefabs with each processor's cached query (23 masks in a loaded world).

### A disposed mod stays in `modManager` with its assembly set

**Priority:** medium · **Cost:** trivial

`loader-states.md` says `GeneralError` "in practice means `OnLoad` threw", but `ModInfo.Load` runs `GetTypesDerivedFrom<IMod>()` inside the same `try` (`src/Game/Game.Modding/ModManager.cs:120`, its catch-all at `:141`), so a mod whose assembly loads and whose types do not, such as one a game update left outdated, lands there too.
Like the other rethrowing states it ends at `Disposed`, and the entry stays in `modManager` with `asset.assembly` set, so reflecting over it throws `ReflectionTypeLoadException` again.
The rule to add: code that enumerates `modManager` and reflects over each assembly skips a mod whose `state` is not `Loaded`. Found while reviewing Asset Icon Library, whose API calls now do; Find It's API registration did not, and threw in a dispatcher updater on every frame.
The `RegisterUpdater(Action)` row of `mod-lifecycle-and-ordering.md` needs the throw path too: the action unregisters only once it returns, so one that throws re-runs every tick, as Find It's did in game (2026-10-21, with Asset Icon Library's broken-mod fixture).

### The game's culture is not invariant after boot

**Priority:** medium · **Cost:** trivial

`localization.md` closes the `ToUpper()`/`ToUpperInvariant()` enum-key mismatch as inert (`:161`, `:675`), because `GameManager.Awake` sets `CultureInfo.CurrentCulture = CultureInfo.InvariantCulture` (`src/Game/Game.SceneFlow/GameManager.cs:542`).
But `Awake` is `async void` (`:518`) and sets it before its first `await` (`:557`), and a culture set inside an async method is undone for its caller at that await: at the main menu, a `unity` eval on the main thread read `CultureInfo.CurrentCulture.Name` as `en-US` (Proton, en-US locale). Thread-pool threads carry the OS locale too (desktop Mono under `LANG=tr_TR` read `tr-TR`; not checked in game).
So culture-sensitive calls (`ToLower()`, `ToUpper()`, `StartsWith(string)`, a culture `IndexOf`) follow the player's locale, and the enum-key mismatch is live under a Turkish or Azeri one.
Colossal.Json is one: it decodes a JSON number into a `string` field through `Convert.ChangeType(data, typeof(string))` with no provider (`src/Colossal.Core/Colossal.Json/JSON.cs:199`), so `6.5` arrives as `"6,5"` under a comma-decimal locale (confirmed in game under `fr-FR`; Hall of Fame's photo mode settings read NaN that way).
Correct both passages, and add the rules: use the invariant or ordinal forms wherever a string must compare the same for every player, and decode a JSON number into a numeric field. Found while reviewing a Find It search change.

### The local mod details call never suspends

**Priority:** low · **Cost:** trivial

No reference says the PDX SDK's local mod lookup completes synchronously: `ModsInformationService.GetLocalModDetails` reads only the local cache and returns `Task.FromResult` on both paths (`src/PDX.SDK/PDX.SDK.Internal.Service.Mods.Services/ModsInformationService.cs:92-130`), so a mod's `async` method awaiting it never yields and runs to its end inside the call.
The fact to add, beside the SDK's entry in `navigating-the-decompile`: code after that await runs before the caller's next line, so no frame or other thread sees the method half done. Found while reviewing a Find It change, whose reviewers raised a race on the mod dates it fills that cannot happen.

### A mod-held runtime prefab survives the load's asset unload

**Priority:** low · **Cost:** trivial

`prefabs-and-assets.md` covers `ScriptableObject.CreateInstance` (`:337`) but not whether such an object outlives `Resources.UnloadUnusedAssets`, which `GameManager.CleanupMemory` runs on a load (`src/Game/Game.SceneFlow/GameManager.cs:927`).
Unity keeps what script fields and statics reach, and a managed system is reachable from the static `World.s_AllWorlds` (`src/Unity.Entities/Unity.Entities/World.cs:57`), so an instance a system's field (or an object it holds) references survives without `HideFlags`. Read from the decompile and Unity's documented rule, not tested in game. The fact to add there; found while reviewing a Find It change, where three reviewers raised the opposite as a bug.

### An updated prefab's old entity can come first, and still resolves

**Priority:** medium · **Cost:** trivial

`regenerating-a-prefab.md`'s "What the call does" lists the drain's steps but not two consequences for code that reads both entities in the `Deleted` frame.
The drain tags the old entity `Deleted` before it creates the new one (`src/Game/Game.Prefabs/PrefabSystem.cs:782`, `:798`), so when the rebuilt component set is new, the old entity's `Deleted` archetype is created first and a query over both lists the old entity before its replacement; with an unchanged set the replacement reuses the boot archetype and comes first. Shown in game: an `EditorAssetCategoryOverride` added before the call flipped the order.
And the old entity keeps its `PrefabData` (`:799` copies it to the new one), so `TryGetPrefab(Entity)` (`:669`) still resolves it to the live prefab during that frame.
The facts to add there, with the rule: code that pairs an updated prefab's old and new entities matches them by prefab, never by query order, and checks `Deleted` before `TryGetPrefab`. Found while fixing Find It's partial index, which lost an updated prefab's fields to both.

### Escape and the game's UI actions

**Priority:** medium · **Cost:** cheap

No reference says how a key reaches a UI `InputActionConsumer` (`cs2/input`), so a mod that wants Escape to close its panel starts from the decompile.
Escape is bound to several UI actions (`Back`, `Pause Menu`, `Close`, `Default Tool UI`): the UI input stack sends each action's stack index through `input.setActionPriority`, and `InputActionBindings.ResolveConflicts` (`src/Game/Game.UI/InputActionBindings.cs`) enables only the highest-priority action per key, so a consumer claiming `Back` above them blocks the pause menu and an open panel's `Close`, even when its handler returns `false`.
An `ignoreFocusState` consumer attaches to the input root in mount order and the last attached wins; changing its `actions` keeps its slot, and on a first mount of the tree React runs child effects first, so a consumer mounted with the game screen lands before the game's own chain.
The rule: mount the consumer only while it should take the key, and yield to the game's panels while one is open (`game.activeGamePanel$`, `selectedInfo.activeSelection$`).
While a text field has focus, `InputManager.GetMaskForControlScheme` (`src/Game/Game.Input/InputManager.cs:630`) masks keyboard and mouse to the mouse (`hasInputFieldFocus`), so no keyboard UI action fires and Escape reaches only the field's DOM `keydown`.
To check routing in game: a `unity` eval of `InputActionBindings.m_UIActionStates[m_ActionOrder["Back"]]` gives each action's `priority` and `state`, and `engine.trigger("input.onActionPerformed.update", {action: "Back", value: null})` fires an action where a real key press can't be queued (an unfocused game window).
The facts to add to `frontend-and-injection.md`'s `cs2/input` passage. Found while making Escape close Find It's panel.

### Harvesting the mods' `docs/solutions/` into the plugins

**Priority:** medium · **Cost:** moderate

A mod built with the cantrips loop records each hard-won problem in `docs/solutions/<slug>.md` (frontmatter `date`, `area`, `symptoms`, `tags`, then the problem, its root cause and the fix), and many of those are engine gotchas no plugin carries: every other mod relearns them, and the mod's copy drifts as the game changes.
The plugins are the layer meant to carry that knowledge.
Hall of Fame's folder (`~/Documents/Dev/HallOfFame/docs/solutions/`) is the seed: most of its 16 entries are engine-level, such as `awaiting-a-web-request-resumes-the-caller-twice`, `cs2-value-import-is-undefined-in-the-game`, `mod-ui-vanishes-when-its-module-throws-on-load`, `runtime-css-reads-and-overrides-mislead-in-cohtml`, `saving-settings-inside-a-sync-cancels-it` (`ApplyAndSave` re-entering through `onSettingsApplied`), and its vanilla `Dropdown`, tooltip, portal, input-action and markdown entries; its Crowdin, server and lint entries are the mod's own.

A repo-only skill in `.agents/skills/`, beside `scan-mcp-failures`, would:

- read every `docs/solutions/*.md` of the CS2 mods checked out on the machine, and class each as engine-level or the mod's own;
- check whether a plugin skill already carries it, and verify the rest against the game: the decompile or the readable UI bundle for a claim about code, the live game through the `unity` and gameface tools for behaviour;
- fold each verified fix into the skill that owns it (`gameface`, `gameface-driving`, `unity-driving`, `cs2-modding`, `cs2-modding-ui`, `cs2-mod-project`), a `cs2-modding` claim through `docs/authoring/reference-ticket-protocol.md` like any other;
- delete the harvested files from the mod repos once the plugin change has landed, and leave the mod's own.

Shape, not settled: whether the deletion is a commit the skill makes in each mod repo or a handoff to that repo's session.

### Measuring a mod's cost in the running game

**Priority:** medium · **Cost:** cheap

`performance-and-memory.md` says what a job, a scan or an allocator costs and how to catch a native leak, but not how to measure a mod's own time or managed allocations, so each optimisation pass works its method out again.
Find It's performance work did, in machine-local files of `~/Documents/Dev/cs2-findit` (not in its git history): the Diagnostics section of `CLAUDE.local.md`, and `.scratch/tools/`.

- Time inside compiled code, a bench class the `unity` eval calls or a hooked build: the eval's own timing of ECS work overstates it (`ToComponentDataArray` over one query read ~30 ms from the eval, 0.4–0.6 ms in a hooked build).
- A managed `for` over a `NativeArray` costs about 10× the same loop in a Burst `IJob` run with `.Run()` on the main thread (198k rows: 18–20 ms against about 2 ms, the ECS copies included), so a scan of every placed object is a Burst job even for a one-off click.
- Compare two builds only from runs of one sitting, since the same build's work drifted from 6.2 to 8.6 s across a day, and diff the CPU's thermal throttle counters around each run (`.scratch/tools/throttle.sh`).
- Per-call allocations: set `GarbageCollector.GCMode` to `Disabled` and run several passes reading `GC.GetTotalMemory(false)`, the last agreeing passes giving the figure (`AllocBench` in `.scratch/tools/Item3Bench.cs`).
  A single pass after `GC.Collect` undercounts small objects, which refill the freed slots (a ~120 B/call `int[]` read 0), and `GC.GetAllocatedBytesForCurrentThread` returns 0 in the game's Mono.
- A frame-time recording (rAF in the Gameface page) takes no other call into the game while it runs: each `unity` or gameface eval stalls the game and shows as a long frame.

Shape, not settled: a `measuring.md` sibling in `performance-and-memory/`, with the two eval traps also a line each in `unity-driving` and `gameface-driving`, where an agent timing through the tools reads them.
The figures come from one Linux machine under Proton; verify each in game before it ships.
The unattended loop driving such runs belongs to the UI-trigger-from-a-shell-loop entry under `coherent-gameface`, and the game threads' OS culture, from the same notes, to the culture entry above.

### Shipping the Linux toolchain setup as a script

**Priority:** medium · **Cost:** moderate

`cs2-mod-project`'s `linux-toolchain.md` walks an agent through the Linux toolchain setup by hand, step by step, and each mod repo then keeps its own script that automates the same steps: `scripts/setup-linux-toolchain.sh` in `~/Documents/Dev/HallOfFame` and in `~/Documents/Dev/ModernModTemplate`, run through each repo's `mise setup:linux` task.
The two copies are byte-identical today and will drift, and a mod started without one of them repeats the manual walk.

- Ship the script with `cs2-mod-project` (a `scripts/` folder beside `references/`), and make `linux-toolchain.md` lead with running it, keeping the manual steps as what the script does and the fallback when it fails.
- Start from ModernModTemplate's copy: its review gate of 2026-10-23 fixed the search that reads only the root Steam library (a game or Proton in a secondary library dead-ends) and the `tail` that hides the "Unity failed" message under `set -e` when Unity never wrote its log.
  The same gate confirmed and left two more: `pgrep -f 'Cities2\.exe'` matches any process whose command line mentions the exe, an agent's own shell included, so the script can refuse to run while the game is closed; and the idempotency checks test a path an interrupted extraction creates early, so a rerun after a Ctrl-C reports "Already installed" over a broken runtime.
- Once the plugin ships it, drop the copy and its `setup:linux` task from each repo that has one, and point the repo's docs at the skill.

### A binding's own subscriptions follow `Attach` and `Detach`

**Priority:** medium · **Cost:** cheap

`binding-layer.md` says navigation detaches and re-attaches the whole registry (`UserInterface.OnNavigateTo`, `OnReadyForBindings`), and that a system's `OnDestroy` is where an event subscription is unhooked, but not what that means for a binding that subscribes to something itself.
Such a binding acquires in an `Attach(View)` override and releases in `Detach()`, the pair the game runs on every navigation and on teardown: a subscription made in the constructor and released in `Detach` is dead after the first navigation, and one released in a finalizer is never released, since the subscription roots the binding.
A `ValueBinding`'s `value` survives `Detach`, and `OnSubscribe` re-sends it after the re-attach, so transient state (an input action's phase) is reset in `Detach`.
Overriding `Attach` needs a `cohtml.Net` reference for `View`.
The worked instance is `ToveruxModTemplate/Utils/InputActionBinding.cs` in `~/Documents/Dev/ModernModTemplate` (a `ProxyBinding.Watcher` and `ProxyAction.onInteraction`); its review gate of 2026-10-23 caught a fix that released in `Detach` while still acquiring in the constructor.
The facts to add to `binding-layer.md`'s teardown passage.

### How Paradox Mods serves a mod's published images

**Priority:** low · **Cost:** cheap

`cs2-mod-project`'s `publishing.md` covers the `Thumbnail` and `Screenshot` fields but not what the site does with the files, which decides whether compressing them pays.
Checked on Hall of Fame (mod 90641) on 2026-10-23, through the public `https://api.paradox-interactive.com/mods?modId=<id>&os=Windows`:

- Screenshots are served byte for byte as uploaded (`content/screenshots/screenshot_NN.png` matched the committed file), plus a generated 240×135 `_thumb.png` for the gallery strip, so a visitor opening one downloads the uploaded file whole.
- The thumbnail is re-encoded to JPEG (`content/covers/cover_<version>.jpg`), so a lossy pass on it only costs quality.
- Unverified: whether a JPEG screenshot upload stays JPEG.

The facts to add to `publishing.md`, with the practice they lead to: quantize the screenshots (pngquant left 1920×1080 previews indistinguishable at a third of their lossless size) and keep the thumbnail lossless.
The worked instance is the `images:optimize` task in `~/Documents/Dev/HallOfFame/mise.toml`.

### A settings reset that calls `SetDefaults` leaves key bindings alone

**Priority:** medium · **Cost:** cheap

`settings-and-input.md` covers `ResetKeyBindings()` and the reset-key-bindings button, but not what an all-settings reset button needs.
`SetDefaults()` never touches key bindings, so a reset that calls only it keeps the player's rebinds: an all-settings reset calls `ResetKeyBindings()` as well.
`ResetKeyBindings()` applies and saves only when `keyBindingRegistered` (`src/Game/Game.Modding/ModSetting.cs:279-287`), so a reset that must save whatever the mod registers keeps its own `ApplyAndSave()`.
Verified in game on 2026-10-23: a binding rebound to J stayed J through a `SetDefaults`-only reset, and returned to Ctrl+K once the reset called `ResetKeyBindings()`.
The worked instance is the template's `ResetSettings` (toverux/ToveruxModTemplate, `ToveruxModTemplate/Settings.cs`); HallOfFame's `Settings.cs` has the gap.
Both facts belong in `settings-and-input.md`'s `ResetKeyBindings()` passage.

### `getModule(path, 'classes')` returns the game's live classes object

**Priority:** medium · **Cost:** cheap

No `cs2-modding-ui` reference says what `getModule` hands back for a stylesheet module.
Its `classes` getter returns the object the vanilla component styles with, often its default theme, so a mod writing into it (fallback names for missing classes, say) changes the classes the game and every other mod read; a mod copies it before writing.
The registry throws one `Module <path>@<export> was not found.` error for a missing module and a missing export alike, so a resolver cannot tell the two apart from the error.
Traced in the readable bundle copy on 2026-10-23 (the registry's `getModule`, and `game-ui/common/tooltip/tooltip.module.scss`'s registration).
The worked instance is the template's `getClassesModule` (toverux/ToveruxModTemplate, `ToveruxModTemplate/UI/src/utils/vanilla-modules.ts`); HallOfFame's copy writes into the live object.
Both facts belong in `frontend-and-injection.md`'s module registry passage.
