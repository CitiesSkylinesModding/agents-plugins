# Roadmap

Planned facets for the `csmodding` marketplace plugins. The two toolkits are generic, developed and
verified against Cities: Skylines II but application-agnostic; `cs2-modding` is about that game
specifically.

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

Input is settled on our side. A raw CDP session at Cohtml 2.2.11.3 re-ran what 1.64 and 2.2.1.3 had
shown, `Input.dispatchMouseEvent`, `Input.dispatchKeyEvent` and `Input.insertText` answering with an
empty result and no error while no DOM handler fires, and `Input.dispatchTouchEvent`, probed there
first, does the same (the probe catalogue's CDP entry holds the session and its controls). That is
why every input tool here dispatches bubbling DOM events instead, and why `game_hover` warns that
the CSS `:hover` state stays unset. What stays open is their side: their input path rests on
`Input.dispatchMouseEvent`, `Input.dispatchTouchEvent` and `Input.insertText`, all inert at 2.2.11.3
here, and their hover is commented as triggering `:hover`, so either CDP input works on a 3.x Player
and our claim needs a host qualifier rather than only the versions it now carries, or their
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
then the matching unsubscribe — the route to simulation-side data the DOM never renders.
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
line CS2 ships as of 1.6.4f1 (`cohtml.Net`'s `AssemblyVersion`). The catalogue's own
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

At Cohtml 2.2.1.3 in Cities: Skylines II, and still at 2.2.11.3 (re-measured 2026-11-11, past the 2.2.2,
2.2.4 and 2.2.7 flex fixes), a flex item in a `flex-wrap: wrap` row came out narrower than its content by
its `margin-right` minus its `margin-top` and `margin-bottom`: an item with `margin: 4rem 24rem 4rem 0`
was 16px short, and its text overflowed the box. Nothing shows until the box paints, so it read as a
misplaced hover background, and three padding tweaks went nowhere before measurement found it. The
margins were what mattered: `margin-right: 0` made the item 8px too wide, 40 made it 32 short, all
margins 0 made it exact, while the font size, `flex-shrink: 0` and wrapping the text in its own element
changed nothing. Spacing the items with padding, or with flex `gap`, which lays out since 2.2, sizes them
right. This belongs in `layout-styling-text.md` beside the margin and `gap` notes, verified on one more
row shape (no wrap, a column), since the probe covered only a wrapping row of inline-content items. Found
in Hall of Fame's Creator Dashboard detail caption, session `449ee1b0-8f4e-4e34-a306-6263c9118606`.

### A type name's first shape bounds later writes

**Priority:** medium · **Cost:** trivial

`scripting-data-binding.md` says two payloads under one type name must carry the same present properties, and
that an omitted property breaks the cached binding. At Cohtml 2.2.1.3 in Cities: Skylines II only one direction
fails. Move It 0.5.16 writes `FOEntryState` with and without a `Checkbox` object in every update of one value
binding, the larger shape first, about 40 times a second: each of over 2,000 updates reached `engine.on` intact.
A type name first written without `Checkbox` and then with it (`FOPopoutEntryState`, injected from the `unity`
eval) delivered the extra property under a garbage numeric key, a different one per write (`"731839520"`,
`"731840096"`), in place of `Checkbox`: the engine reads past the cached shape. Hall of Fame's state writer did
the same, `{status}` and then `{status, screenshots, stats}`, and that crashed the game with an access violation
at address 0 in `v8.dll`. At 2.2.11.3 (re-measured 2026-11-11, raw `JsonWriter` events from the `unity` eval)
the larger shape first still reads right, and a single write adding a property to a name first written smaller
hung the game until it exited, twice. The passage should state the asymmetry: a later object may omit properties
the name's first write carried, a property beyond them reads out of bounds, as garbage keys or a native crash,
and the first write is whichever a payload's order reaches first, so keeping the varying presence in the type
name stays the safe rule. Session `12ee6d3f-9851-4277-a276-db808e87c85d`.

### Idea: trim the version timeline and fold the `VOLATILE:` markers into the probes

**Priority:** medium · **Cost:** unsure

Just ideas, from the `cs2-modding` purge; nothing measured says either one is needed.
`version-gating.md`, the largest skill file, spends most of its length on a changelog timeline that
`mise skills:check-changelog` could fetch live; it could shrink to the probes and the gates that bite.
The `VOLATILE:` markers label claims the engine moves while unmarked lines promise nothing; the
`gameface-update` probe catalogue could own those claims instead, the way the trap check owns the
`cs2-modding` traps.

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

### An `attach(port)` endpoint that a drop silently replaces

**Priority:** medium · **Cost:** moderate

Once the agent pins an endpoint with `attach`, a drop hands the next call back to the beacon, and
the reply does not say so: with the pinned game gone and another one advertising, every later call,
writes included, lands on the other game. The pin should hold, failing until the agent attaches
again, or the reply should name the endpoint change. Hit on 2026-11-04 (session
`ac38ed25-c564-4400-b38d-4985a1d6f20f`): `attach(56000)` was refused because the release debuggee
on it had exited, and the next calls reached a different game through its beacon.

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

A Unity game's own logs are the primary evidence for a whole class of diagnostics questions — the
version block, the debugger-port lines, the absence of a warning — and no `unity` tool reads them;
`cs2-dev`'s `logs` serves one game, on its own machine. `System.IO.File.ReadAllText` through `eval`
covers a game's own log files, but `Player.log` is held open by the player and answers
`Sharing violation`; the workaround is
`new FileStream(path, Open, Read, FileShare.ReadWrite)` wrapped in a `StreamReader`, which is a lot
of incantation for "show me the log". A read-file or tail-N-lines verb, sharing-mode correct by
construction, would serve each of them directly.

### A system's query descriptor

**Priority:** low · **Cost:** moderate

`ecs_query` builds its own query and cannot be pointed at one a system already holds, so reading
what `SerializerSystem.m_Query` or `ClearSystem.m_ClearQuery` actually matches took reflection plus
`GetEntityQueryDesc()` and then hand-indexed reads of a 19-element `ComponentType[]` — no loops, so
two calls and some 600 characters to list one query. A verb reporting a named system's queries with
All/Any/None resolved to type names would collapse roughly six round trips per query to one, and the
same read recurs whenever a claim is about what a vanilla system sees.

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

**Priority:** medium · **Cost:** moderate · **Hits:** 5 as of 2026-10-28

`(Game.Net.LabelPosition[])em.Debug.GetComponentBoxed(entity, type)` fails to parse:
`unsupported: array type`. The workaround is `(System.Array)` plus `GetValue(int)`, which reads the
elements back but renders each one truncated (`float3 {...}`), so a call returning an array is
unusable at its natural type. Every `EntityManager.Debug` route that boxes a buffer hands back
`object` over a `T[]`, and so does any game method returning an array, so accepting `T[]` in the
cast grammar — and in `typeof` and `default` with it — closes a class of read rather than one call.
`default(string[])` fails the same way, though `unity-driving` says `default(T)` builds any type's zero value or null; a plain `null` assigns where it would (2026-11-12, session `e386823e-50c5-4015-819a-9005bd7c779a`).

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

**Priority:** low · **Cost:** moderate · **Hits:** 2 as of 2026-11-02

Inference reads only a parameter typed as the type parameter itself and takes each argument's
runtime type. It refuses two arguments fixing `T` to different types, and a null beside an argument
fixing `T` to a type that takes null. C# also infers through `T[]` and `List<T>`, and fixes `T` to a
type every argument converts to. Each refusal is loud and names its fix, writing the type argument,
as `Enumerable.ToList` over an `IReadOnlyCollection<T>` did with `ToList is generic and TSource
cannot be inferred`. Reading `T[]` and generic-instance parameters is the step agents would reach
first; the common-type cases need static types the evaluator does not track.

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

**Priority:** medium · **Cost:** moderate · **Hits:** 8 as of 2026-10-28

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
that route in `unity-driving`'s "Evaluating C# in the game" section, which calls such a method out
of reach, is the cheap fix; packing the trailing arguments into the parameter's array type when no
overload binds them as written is the full one.

### Collection initializers in `eval`

**Priority:** low · **Cost:** unsure

`new System.Collections.Generic.List<string> { "x" }` fails with `unsupported: collection
initializer expression`, though the tool description lists `new` with initializers, which reads as
covering both forms. Passing a one-string `IList<string>` (`AppBindings.RemoveActiveUIModLocation`)
took `System.Text.RegularExpressions.Regex.Split("x", ";")`, a `string[]` the grammar cannot build
directly. Accepting the form, or naming the object-only scope in the description, closes it.

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
plugin's own invokes (`InvokeOptions.DisableBreakpoints`) is the likely fix, untested. A plain
breakpoint does the same in the field: an `eval` of `world.CreateSystemManaged(...)` hit one armed
on a constructor it ran, and the session dropped after 60 s (Do It, 2026-11-10, session `94247bc1-3c7c-4c2c-89a4-ca5a5e6a0892`).
Until the fix lands, `unity-driving` should warn against arming a breakpoint on code an `eval` runs.

### An invoke releases a thread paused at a breakpoint

**Priority:** medium · **Cost:** unsure

Every invoke goes out with `InvokeOptions.None`, so the agent resumes the whole VM while it runs. A
worker thread paused at a breakpoint runs on during a frameless `eval`: in the fixture, a worker
paused inside a static constructor finished it and exited, and the next `debug_evaluate` failed with
the `has no managed frames` error. `SingleThreaded` holds every other thread, at the price
of deadlocking an invoke that waits on one of them; which callers can take that is what the cost
turns on.

### A held suspend shows the main thread no managed frame

**Priority:** low · **Cost:** moderate · **Hits:** 3 games as of 2026-11-04

A plain `suspend` usually catches the main thread in engine code between managed callbacks, and
Mono's agent keeps reporting no managed frame for it even once it parks for an invoke: it parks
before the callback runs, and an invoke restores that same context. `debug_pause_state` reports
the empty stack and `debug_evaluate` fails, so a method breakpoint is the dependable route to
frames. An opt-in "run the main thread to its next managed entry" would give a hold frames, at the
price of moving the game, so it can never be what a hold does by default; what that run-to costs
(a step or method-entry request against a thread outside managed code) is what the cost turns on.

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
fix to another author's mod cannot add: Asset Icon Library's unpack fixes were tested this way. The
pause can change the call's own inputs too: a Do It red case for a failed `OnCreate` broke at
`Game.Modding.ModManager+ModInfo.OnLoad` during boot, by which the mod's assembly is loaded and its
types can take a breakpoint, armed one on the mod's constructor, and nulled its argument with
`debug_evaluate` (2026-11-10, session `94247bc1-3c7c-4c2c-89a4-ca5a5e6a0892`). A paragraph in the skill's breakpoint
section would carry it.

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
So the skill promises what the server refuses: in the mod template on 2026-10-23, `var s = default(UnityEngine.InputSystem.LowLevel.KeyboardState);` failed with `unsupported: default expression`, and the agent fell back to `new KeyboardState { }`.
Bumping `<Version>` in `UnityDevtools.Mcp.csproj` and the `.mcp.json` pin with every server change, and a check that fails a server change landing without the bump, keeps the two in step.

### A mutating call on a struct with a fixed buffer fails

**Priority:** medium · **Cost:** unsure

In the mod template on 2026-10-23, on the 2026-10-10 server build above: `var s = new UnityEngine.InputSystem.LowLevel.KeyboardState { }; s.Set(UnityEngine.InputSystem.Key.LeftCtrl, true);` passed, and the next `s.Set(UnityEngine.InputSystem.Key.K, true);` failed with `Unable to cast object of type 'System.Byte[]' to type 'System.Byte'`.
`KeyboardState` holds its keys in a `fixed byte` buffer, which the wire carries as the compiler's `<keys>e__FixedBuffer` struct, so writing the local back after the call seems to assign the buffer's bytes where one byte is expected.
Unverified against the current source: reproduce with a fixture struct holding a fixed buffer and a method writing to it.
The workaround was to build the input from the device's own layout instead (next entry).

### Pressing a key the game reads natively

**Priority:** medium · **Cost:** moderate

`game_key` reaches the UI's DOM handlers only, and nothing in either plugin presses a key that Unity's Input System reads, the route a game's own key bindings take.
In the mod template on 2026-10-23, verifying a mod's Ctrl+K binding took four evals.
A state event built from the device's layout (`StateEvent.From(Keyboard.current, out var ev, Allocator.Persistent)`, then `InputControlExtensions.WriteValueIntoEvent(kb.kKey, 1f, ev)` and `InputSystem.QueueEvent(ev)`) was dropped while the game window was unfocused: the Input System's `backgroundBehavior` was `ResetAndDisableNonBackgroundDevices`.
Switching it to `IgnoreFocus` and calling `InputSystem.EnableDevice(kb)` let the press and release through, after which the setting was restored.
A `unity` `key` tool taking a chord (`ctrl+k`) would press, advance a frame, release, and lift the background gate only for its own window, restoring it after.
An agent driving a game on another machine or on Wayland has no other way to send that key.

### Recreating a managed system through `eval` breaks the game until a restart

**Priority:** medium · **Cost:** trivial

`unity-driving` does not say that destroying a managed system and creating it again through `eval` (to rerun its `OnCreate`) leaves the game's `UpdateSystem` holding the destroyed instance: the log then takes `[CRITICAL] System update error during UIUpdate->MenuTourUISystem: InvalidOperationException: system state is not initialized or has already been destroyed` every frame until the game restarts.
The rule to add beside the managed-systems passage: rerun a system's creation path by relaunching the game, never by recreating the system in place. Hit in Hall of Fame on 2026-10-29 (session `e6cda669-8347-4811-a7a8-465d1e6f75b8`), where it threw 622 times before a clean quit.

### Attaching to a game on another machine

**Priority:** low · **Cost:** unsure

`attach` takes a port and connects to this machine, so a game in a VM or on another host is reached only through a tunnel: `ssh -f -N -L <port>:127.0.0.1:<port>` to the game's machine, then `attach` on that port.
A host parameter would remove the tunnel. Hit on 2026-11-03 (session `bd9d090f-c93c-439b-8abe-772f2fadddb5`), attaching to a game in a Windows VM.

### A blocking `.Result` in `eval` deadlocks a task that needs the main thread

**Priority:** medium · **Cost:** trivial

An `eval` on the main thread that reads `.Result` or calls `.Wait()` on a task whose completion needs that thread blocks the very thread the task waits on, and the game freezes for good.
In Cities: Skylines II, the PDX SDK's playset calls (`EnableModInActivePlayset`) are such tasks.
Add the rule to `unity-driving`: start the task in one `eval`, poll its `IsCompleted` in later ones, and read `Result` once it holds true.
Hit on 2026-11-06 (session `cc528b48-bcdf-4841-b0eb-25d5950058f8`), re-enabling mods in a playset.

### A class result shows its type and address only

**Priority:** medium · **Cost:** cheap

`eval` renders a struct with its fields, and an instance of a class as `Type#address` (`Invoker.Format`, `ObjectMirror`), so a C# record, a report object or any class value reads as nothing useful.
String interpolation goes through the same formatter, so `$"{obj}"` gives `Type#address` where C# calls `ToString()`, and the agent learns the value only by ending on `obj.ToString()`, which nothing in the schema or `unity-driving` tells it.
Render a class instance's fields to the same depth as a struct's, or call its `ToString()` when the type overrides it, the record case; interpolation should call `ToString()` as C# does.
Hit on 2026-11-12 (session `e23153a2-abdb-40f0-9617-1f18776877c1`), reading a capture report record from a Cities: Skylines II mod.

## cs2-modding

Skills teaching an agent to write Cities: Skylines II code mods, a working loop and the traps the
decompile and the live game would not warn of, and the `cs2-dev` MCP server over the game's
lifecycle. An entry carrying a game fact is a trap candidate: it joins a body only if it meets the
trap bar.

### `Time` in a system body wakes the Entities generator

**Priority:** medium · **Cost:** cheap

A Do It UI system under a file-scoped namespace kept compiling until its `OnUpdate` read
`UnityEngine.Time.unscaledTime`, with no `SystemAPI` call or schedule site: the generator then
emitted `ActionsUISystem__System_*.g.cs` into the global namespace, and the build failed with
`CS0311` at `UpdateAt<T>` and `GetExistingSystemManaged<T>` rather than on `OnCreateForCompiler`.
Establish which generator module matches `Time`, then amend the two passages on the trap,
`cs2-mod-project/SKILL.md` and `references/build-pipeline.md`, with that trigger and this symptom.

### A system that catches its own `OnCreate` failure keeps updating

**Priority:** medium · **Cost:** trivial

Between an uncaught `OnCreate` throw and an `OnUpdate` throw sits a third pattern: an `OnCreate` that catches and logs its
own failure leaves the system enabled and half-built, and its `OnUpdate` throws on the missing
fields, which `UpdateSystem` logs as Critical on every update. A Do It UI system took 21 of those
errors before the main menu (2026-11-10, session `94247bc1-3c7c-4c2c-89a4-ca5a5e6a0892`). The rule: set `Enabled = false` in such a catch.

### `ProxyBinding` equality ignores the key

**Priority:** medium · **Cost:** trivial

`ProxyBinding`'s `Equals`, `==`, `!=` and `GetHashCode` all go through `Comparer.defaultComparer`,
whose options are `MapName`, `ActionName`, `Name` and `Device` (`ProxyBinding.Comparer`,
`ProxyBinding.Equals`, at 1.6.4f1): a binding compares equal to itself after a rebind, whatever its new path and
modifiers, so a `==`, a `Distinct` or a dictionary keyed on a binding misses the change.
The right move: `PathEquals` or `ConflictsWith` as the comparison for the key itself.

### Extracting the shipped localization dictionaries

**Priority:** low · **Cost:** cheap

The game's compiled `.loc` assets are the only first-party, version-known source for the vanilla
localization key set, and decoding them is mechanical: `Locale.cok` is a plain stored zip, and the
payload is a flat `BinaryWriter` stream whose reader is its own specification. A base locale file
can carry trailing bytes past its declared end, so end-of-file is not end-of-data and a decoder has
to stop on the counts.

The consumer, met in a mod's translation pass: an agent translating a mod into the game's
languages wants each locale's official terms (photo mode, milestone, playset) so the mod reads like
the vanilla UI, and decoding the dictionaries by hand was a detour. `cs2-modding-setup` could decode
them into a machine-local directory recorded in `~/.cs2-modding/setup.md`, beside the decompile;
a local copy commits nothing, so it stays on the right side of the copyright line: the
translations are the publisher's copyrighted text.

### What serializes a `CallBinding`'s result

**Priority:** medium · **Cost:** cheap

A `CallBinding` hands its callback to `view.BindCall` (`RawCallBindingBase.Attach`), so its return
value reaches JavaScript through cohtml's own reflection `Exporter`, never through the
`IWriter`/`IJsonWritable` path a `ValueBinding` takes, so a returned type's own `Write` never runs.
No skill says which serializer a call result meets (`Colossal.UI.Binding.RawCallBindingBase`,
cohtml's `Exporter`).

### `SystemAPI` needs the Entities source generator

**Priority:** medium · **Cost:** cheap

Every `SystemAPI` member's body is `throw InternalCompilerInterface.ThrowCodeGenException()`
(`Unity.Entities.SystemAPI`): the call only works where the Entities source
generator rewrote it at compile time, and `SystemAPI.Query<T>()` and `SystemAPI.QueryBuilder()` read
as ordinary forms. Whether the mod template's build runs the generator is the fact to establish
first; a trap then either says so or steers to `GetComponentLookup`/`EntityQueryBuilder` on the
system itself.

### `EntityQuery.Matches` and the unchecked query-mask limit

**Priority:** medium · **Cost:** trivial

A world has 1024 query masks: `Matches` (or `GetEntityQueryMask`) takes one for each distinct query descriptor it is called on, deduplicated by descriptor hash (`Unity.Entities.EntityQueryManager`), and frees none before the world goes.
The limit check, `EntityQueryManager.ThrowIfEntityQueryMasksIsGreaterThanLimit`, is `[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]`/`[Conditional("UNITY_DOTS_DEBUG")]`, so the game's release build skips it, and the 1025th mask writes past `Archetype.QueryMaskArray` (`fixed byte[128]`) instead of throwing.
The rule to add: call `Matches` only on a bounded set of queries with fixed descriptors, never on queries whose component set varies with the input. Found while reviewing a Find It change that filters changed prefabs with each processor's cached query (23 masks in a loaded world).

### A disposed mod stays in `modManager` with its assembly set

**Priority:** medium · **Cost:** trivial

`GeneralError` reads as "`OnLoad` threw", but `ModInfo.Load` runs `GetTypesDerivedFrom<IMod>()` inside the same `try` (`Game.Modding.ModManager.ModInfo.Load`, its catch-all), so a mod whose assembly loads and whose types do not, such as one a game update left outdated, lands there too.
Like the other rethrowing states it ends at `Disposed`, and the entry stays in `modManager` with `asset.assembly` set, so reflecting over it throws `ReflectionTypeLoadException` again.
The rule to add: code that enumerates `modManager` and reflects over each assembly skips a mod whose `state` is not `Loaded`. Found while reviewing Asset Icon Library, whose API calls now do; Find It's API registration did not, and threw in a dispatcher updater on every frame.
`RegisterUpdater(Action)` has a throw path too: the action unregisters only once it returns, so one that throws re-runs every tick, as Find It's did in game (2026-10-21, with Asset Icon Library's broken-mod fixture).

### The game's culture is not invariant after boot

**Priority:** medium · **Cost:** trivial

`GameManager.Awake` sets `CultureInfo.CurrentCulture = CultureInfo.InvariantCulture` (`Game.SceneFlow.GameManager`), which reads as making a `ToUpper()`/`ToUpperInvariant()` enum-key mismatch inert.
But `Awake` is `async void` and sets it before its first `await`, and a culture set inside an async method is undone for its caller at that await: at the main menu, a `unity` eval on the main thread read `CultureInfo.CurrentCulture.Name` as `en-US` (Proton, en-US locale). Thread-pool threads carry the OS locale too (desktop Mono under `LANG=tr_TR` read `tr-TR`; not checked in game).
So culture-sensitive calls (`ToLower()`, `ToUpper()`, `StartsWith(string)`, a culture `IndexOf`) follow the player's locale, and the enum-key mismatch is live under a Turkish or Azeri one.
Colossal.Json is one: it decodes a JSON number into a `string` field through `Convert.ChangeType(data, typeof(string))` with no provider (`Colossal.Json.JSON`), so `6.5` arrives as `"6,5"` under a comma-decimal locale (confirmed in game under `fr-FR`; Hall of Fame's photo mode settings read NaN that way).
The rules: use the invariant or ordinal forms wherever a string must compare the same for every player, and decode a JSON number into a numeric field. Found while reviewing a Find It search change.

### The local mod details call never suspends

**Priority:** low · **Cost:** trivial

The PDX SDK's local mod lookup completes synchronously: `ModsInformationService.GetLocalModDetails` reads only the local cache and returns `Task.FromResult` on both paths (`PDX.SDK.Internal.Service.Mods.Services.ModsInformationService`), so a mod's `async` method awaiting it never yields and runs to its end inside the call.
The fact: code after that await runs before the caller's next line, so no frame or other thread sees the method half done. Found while reviewing a Find It change, whose reviewers raised a race on the mod dates it fills that cannot happen.

### A mod-held runtime prefab survives the load's asset unload

**Priority:** low · **Cost:** trivial

Whether a prefab made with `ScriptableObject.CreateInstance` outlives `Resources.UnloadUnusedAssets`, which `GameManager.CleanupMemory` runs on a load (`Game.SceneFlow.GameManager`), goes unsaid.
Unity keeps what script fields and statics reach, and a managed system is reachable from the static `World.s_AllWorlds` (`Unity.Entities.World`), so an instance a system's field (or an object it holds) references survives without `HideFlags`. Read from the decompile and Unity's documented rule, not tested in game. Found while reviewing a Find It change, where three reviewers raised the opposite as a bug.

### An updated prefab's old entity can come first, and still resolves

**Priority:** medium · **Cost:** trivial

Regenerating a prefab through `PrefabSystem.UpdatePrefab` drains it with two consequences for code that reads both entities in the `Deleted` frame.
The drain tags the old entity `Deleted` before it creates the new one (`Game.Prefabs.PrefabSystem.UpdatePrefabs`), so when the rebuilt component set is new, the old entity's `Deleted` archetype is created first and a query over both lists the old entity before its replacement; with an unchanged set the replacement reuses the boot archetype and comes first. Shown in game: an `EditorAssetCategoryOverride` added before the call flipped the order.
And the old entity keeps its `PrefabData` (the drain copies it to the new one), so `TryGetPrefab(Entity)` still resolves it to the live prefab during that frame.
The rule: code that pairs an updated prefab's old and new entities matches them by prefab, never by query order, and checks `Deleted` before `TryGetPrefab`. Found while fixing Find It's partial index, which lost an updated prefab's fields to both.

### Escape and the game's UI actions

**Priority:** medium · **Cost:** cheap

No skill says how a key reaches a UI `InputActionConsumer` (`cs2/input`), so a mod that wants Escape to close its panel starts from the decompile.
Escape is bound to several UI actions (`Back`, `Pause Menu`, `Close`, `Default Tool UI`): the UI input stack sends each action's stack index through `input.setActionPriority`, and `InputActionBindings.ResolveConflicts` (`Game.UI.InputActionBindings`) enables only the highest-priority action per key, so a consumer claiming `Back` above them blocks the pause menu and an open panel's `Close`, even when its handler returns `false`.
An `ignoreFocusState` consumer attaches to the input root in mount order and the last attached wins; changing its `actions` keeps its slot, and on a first mount of the tree React runs child effects first, so a consumer mounted with the game screen lands before the game's own chain.
The rule: mount the consumer only while it should take the key, and yield to the game's panels while one is open (`game.activeGamePanel$`, `selectedInfo.activeSelection$`).
While a text field has focus, `InputManager.GetMaskForControlScheme` (`Game.Input.InputManager`) masks keyboard and mouse to the mouse (`hasInputFieldFocus`), so no keyboard UI action fires and Escape reaches only the field's DOM `keydown`.
To check routing in game: a `unity` eval of `InputActionBindings.m_UIActionStates[m_ActionOrder["Back"]]` gives each action's `priority` and `state`, and `engine.trigger("input.onActionPerformed.update", {action: "Back", value: null})` fires an action where a real key press can't be queued (an unfocused game window).
Found while making Escape close Find It's panel.

### A second confirmation dialog orphans the first's callback

**Priority:** low · **Cost:** trivial

`AppBindings.ShowConfirmationDialog` (`Game.UI.AppBindings`) keeps one callback slot: each request overwrites it, and `OnConfirmationDialogCallback` calls the slot once and clears it, while the UI stacks the dialogs and moves focus to the dialog layer.
So of two requests in flight only the latest is answered, the first's callback never firing, and a reentrancy guard around a confirmation buys nothing: a second request cannot double-act, it can only strand an awaiter.
Found while reviewing Hall of Fame's delete confirmation, a verifier having checked the decompile.

### Harvesting the mods' `docs/solutions/` into the plugins

**Priority:** medium · **Cost:** moderate

A mod built with the cantrips loop records each hard-won problem in `docs/solutions/<slug>.md` (frontmatter `date`, `area`, `symptoms`, `tags`, then the problem, its root cause and the fix), and many of those are engine gotchas no plugin carries: every other mod relearns them, and the mod's copy drifts as the game changes.
The plugins are the layer meant to carry that knowledge.
Hall of Fame's folder (`~/Documents/Dev/HallOfFame/docs/solutions/`) is the seed: most of its 16 entries are engine-level, such as `awaiting-a-web-request-resumes-the-caller-twice`, `cs2-value-import-is-undefined-in-the-game`, `mod-ui-vanishes-when-its-module-throws-on-load`, `runtime-css-reads-and-overrides-mislead-in-cohtml`, `saving-settings-inside-a-sync-cancels-it` (`ApplyAndSave` re-entering through `onSettingsApplied`), and its vanilla `Dropdown`, tooltip, portal, input-action and markdown entries; its Crowdin, server and lint entries are the mod's own.

A repo-only skill in `.agents/skills/`, beside `scan-mcp-failures`, would:

- read every `docs/solutions/*.md` of the CS2 mods checked out on the machine, and class each as engine-level or the mod's own;
- check whether a plugin skill already carries it, and verify the rest against the game: the decompile or the readable UI bundle for a claim about code, the live game through the `unity` and gameface tools for behaviour;
- fold each verified fix into the skill that owns it (`gameface`, `gameface-driving`, `unity-driving`, `cs2-modding`, `cs2-modding-ui`, `cs2-mod-project`), a `cs2-modding` claim only as a trap that meets the bar;
- delete the harvested files from the mod repos once the plugin change has landed, and leave the mod's own.

Shape, not settled: whether the deletion is a commit the skill makes in each mod repo or a handoff to that repo's session.

### Measuring a mod's cost in the running game

**Priority:** medium · **Cost:** cheap

No skill says how to measure a mod's own time or managed allocations, so each optimisation pass works its method out again.
Find It's performance work did, in machine-local files of `~/Documents/Dev/cs2-findit` (not in its git history): the Diagnostics section of `CLAUDE.local.md`, and `.scratch/tools/`.

- Time inside compiled code, a bench class the `unity` eval calls or a hooked build: the eval's own timing of ECS work overstates it (`ToComponentDataArray` over one query read ~30 ms from the eval, 0.4–0.6 ms in a hooked build).
- A managed `for` over a `NativeArray` costs about 10× the same loop in a Burst `IJob` run with `.Run()` on the main thread (198k rows: 18–20 ms against about 2 ms, the ECS copies included), so a scan of every placed object is a Burst job even for a one-off click.
- Compare two builds only from runs of one sitting, since the same build's work drifted from 6.2 to 8.6 s across a day, and diff the CPU's thermal throttle counters around each run (`.scratch/tools/throttle.sh`).
- Per-call allocations: set `GarbageCollector.GCMode` to `Disabled` and run several passes reading `GC.GetTotalMemory(false)`, the last agreeing passes giving the figure (`AllocBench` in `.scratch/tools/Item3Bench.cs`).
  A single pass after `GC.Collect` undercounts small objects, which refill the freed slots (a ~120 B/call `int[]` read 0), and `GC.GetAllocatedBytesForCurrentThread` returns 0 in the game's Mono.
- A frame-time recording (rAF in the Gameface page) takes no other call into the game while it runs: each `unity` or gameface eval stalls the game and shows as a long frame.
- A UI mod's load cost: during boot the UI view advances about once per second until the main menu, so each async step of the mod loader takes a tick and in-page timings read seconds. Hall of Fame timed `UIManager.Update` through Harmony, compared deployed-file variants interleaved, and attributed JS through the CDP `Profiler` domain, which works on Cohtml 2.2.1.3 and still on 2.2.11.3 (`docs/solutions/measuring-the-ui-bundles-load-cost.md` in `toverux/HallOfFame`).

Shape, not settled: the two eval traps a line each in `unity-driving` and `gameface-driving`, where an agent timing through the tools reads them.
The figures come from one Linux machine under Proton; verify each in game before it ships.
The unattended loop driving such runs belongs to the UI-trigger-from-a-shell-loop entry under `coherent-gameface`, and the game threads' OS culture, from the same notes, to the culture entry above.

### Shipping the Linux toolchain setup as a script

**Priority:** medium · **Cost:** moderate

`cs2-mod-project`'s `linux-toolchain.md` walks an agent through the Linux toolchain setup by hand, step by step, and each mod repo then keeps its own script that automates the same steps: `scripts/setup-linux-toolchain.sh` in `~/Documents/Dev/HallOfFame` and in `~/Documents/Dev/cs2-greenfield-mod-template`, run through each repo's `mise setup:linux` task.
The two copies are byte-identical today and will drift, and a mod started without one of them repeats the manual walk.

- Ship the script with `cs2-mod-project` (a `scripts/` folder beside `references/`), and make `linux-toolchain.md` lead with running it, keeping the manual steps as what the script does and the fallback when it fails.
- Start from the mod template's copy: its review gate of 2026-10-23 fixed the search that reads only the root Steam library (a game or Proton in a secondary library dead-ends) and the `tail` that hides the "Unity failed" message under `set -e` when Unity never wrote its log.
  The same gate confirmed and left two more: `pgrep -f 'Cities2\.exe'` matches any process whose command line mentions the exe, an agent's own shell included, so the script can refuse to run while the game is closed; and the idempotency checks test a path an interrupted extraction creates early, so a rerun after a Ctrl-C reports "Already installed" over a broken runtime.
- Once the plugin ships it, drop the copy and its `setup:linux` task from each repo that has one, and point the repo's docs at the skill.

### A binding's own subscriptions follow `Attach` and `Detach`

**Priority:** medium · **Cost:** cheap

Navigation detaches and re-attaches the whole binding registry (`UserInterface.OnNavigateTo`, `OnReadyForBindings`), and a system's `OnDestroy` is where an event subscription is unhooked; neither says what that means for a binding that subscribes to something itself.
Such a binding acquires in an `Attach(View)` override and releases in `Detach()`, the pair the game runs on every navigation and on teardown: a subscription made in the constructor and released in `Detach` is dead after the first navigation, and one released in a finalizer is never released, since the subscription roots the binding.
A `ValueBinding`'s `value` survives `Detach`, and `OnSubscribe` re-sends it after the re-attach, so transient state (an input action's phase) is reset in `Detach`.
Overriding `Attach` needs a `cohtml.Net` reference for `View`.
The worked instance is `Greenfield/Utils/InputActionBinding.cs` in `~/Documents/Dev/cs2-greenfield-mod-template` (a `ProxyBinding.Watcher` and `ProxyAction.onInteraction`); its review gate of 2026-10-23 caught a fix that released in `Detach` while still acquiring in the constructor.

### How Paradox Mods serves a mod's published images

**Priority:** low · **Cost:** cheap

`cs2-mod-project`'s `publishing.md` covers the `Thumbnail` and `Screenshot` fields but not what the site does with the files, which decides whether compressing them pays.
Checked on Hall of Fame (mod 90641) on 2026-10-23, through the public `https://api.paradox-interactive.com/mods?modId=<id>&os=Windows`:

- Screenshots are served byte for byte as uploaded (`content/screenshots/screenshot_NN.png` matched the committed file), plus a generated 240×135 `_thumb.png` for the gallery strip, so a visitor opening one downloads the uploaded file whole.
- The thumbnail is re-encoded to JPEG (`content/covers/cover_<version>.jpg`), so a lossy pass on it only costs quality.
- Unverified: whether a JPEG screenshot upload stays JPEG.

The facts to add to `publishing.md`, with the practice they lead to: quantize the screenshots (pngquant left 1920×1080 previews indistinguishable at a third of their lossless size) and keep the thumbnail lossless.
The worked instance is the `images:optimize` task in `~/Documents/Dev/HallOfFame/mise.toml`.

### Listing the Paradox Mods catalogue without a login

**Priority:** low · **Cost:** cheap

No reference says how to list or rank the catalogue, which picking a mod to study or to integrate with needs; the documented `GET /mods` answers 400 without a `modId`, and `/mods/search` answers 403.
The website's own search answers anonymously, checked on 2026-11-09:

- `POST https://api.paradox-interactive.com/mods?metadata=yes` with `{"gameNames":["cities_skylines_2"],"tags":["Code Mod"],"sortBy":"popularity","orderBy":"desc","page":0,"pageSize":100}` returns `{totalCount, mods}`; `popularity` orders by `installedCount`, pages count from 0, and `pageSize` is capped at 100 without an error.
- A mod can publish no version of its own: `userModVersion` is null in the search row and absent from the `?modId=` detail, leaving `modVersion`, the revision number Paradox counts uploads with.
- The `?modId=` detail answers 400 for an unknown id, and `os=Windows` also serves mods published for `Any`.
- The rate limit runs about 120 requests per 10 s in a burst and 60 per minute sustained.

The worked instance is `scripts/bridges.ts` in `~/Documents/Dev/cs2-doit`, a working client of both calls, in daily use.
The facts belong beside the detail call the entry above reads, in a Paradox Mods API passage of `cs2-mod-project`.

### A settings reset that calls `SetDefaults` leaves key bindings alone

**Priority:** medium · **Cost:** cheap

`SetDefaults()` never touches key bindings, so a reset that calls only it keeps the player's rebinds: an all-settings reset calls `ResetKeyBindings()` as well.
`ResetKeyBindings()` applies and saves only when `keyBindingRegistered` (`Game.Modding.ModSetting.ResetKeyBindings`), so a reset that must save whatever the mod registers keeps its own `ApplyAndSave()`.
Verified in game on 2026-10-23: a binding rebound to J stayed J through a `SetDefaults`-only reset, and returned to Ctrl+K once the reset called `ResetKeyBindings()`.
The worked instance is the template's `ResetSettings` (CitiesSkylinesModding/greenfield-mod-template, `Greenfield/Settings.cs`); HallOfFame's `Settings.cs` has the gap.

### `getModule(path, 'classes')` returns the game's live classes object

**Priority:** medium · **Cost:** cheap

No `cs2-modding-ui` skill says what `getModule` hands back for a stylesheet module.
Its `classes` getter returns the object the vanilla component styles with, often its default theme, so a mod writing into it (fallback names for missing classes, say) changes the classes the game and every other mod read; a mod copies it before writing.
The registry throws one `Module <path>@<export> was not found.` error for a missing module and a missing export alike, so a resolver cannot tell the two apart from the error.
Traced in the readable bundle copy on 2026-10-23 (the registry's `getModule`, and `game-ui/common/tooltip/tooltip.module.scss`'s registration).
The worked instance is the template's `getClassesModule` (CitiesSkylinesModding/greenfield-mod-template, `Greenfield/UI/src/utils/vanilla-modules.ts`); HallOfFame's copy writes into the live object.

### Running the game without mods

**Priority:** medium · **Cost:** cheap

No skill says how to start a vanilla run, which an upstream bug report stands on and a mod-conflict bisect starts from.
Two sources load mods: the user data folder's local mods, and the active Paradox playset.
The game loads every folder under the user data path whose name does not begin with `.`, so a mod folder renamed to `Mods.disabled` still loads; a hidden name (`.Mods.disabled`) keeps it out.
`Colossal.IO.FileSystem.Snapshot.IsExcluded` skips names starting with `.` or `~`; that it is the scan the mod loader uses is not traced.
Moving the local mods out leaves the playset's mods loading: switching to an empty playset in the game's Paradox Mods screen is the other half, where hand-editing `.cache/Mods/playset_config.json` would fight the playset sync.
Found on 2026-10-25 setting up a vanilla repro of a photo mode bug in Hall of Fame; the folder-name rule is Morgan's, from experience.

### A mod loaded late misses the main menu's loading completion

**Priority:** high · **Cost:** trivial

Mod loading reads as happening before the world is ready, but a Paradox Mods mod can load after the main menu: when the Paradox login and the mod sync outlast the boot, `RegisterDatabase` logs `Defer Mods load` in `SceneFlow.log`, waits for the ready state, then calls `ModManager.Initialize(reinitialize: true)` (`Game.SceneFlow.GameManager.RegisterPdxSdk`, its local `RegisterDatabase`).
The systems such a mod creates never get that main menu's `OnGameLoadingComplete` (raised in `GameManager.Load`), and nothing logs it.
The rule: a system that acts on a loading completion replays it at the end of `OnCreate` when `gameMode` is `MainMenu` and `state` is `WorldReady`; the game sets that state right after raising the event, so a system sees the main menu's event or the replay, never neither. A read made during the replay sees only the mods loaded before this one. A city or editor load first awaits `GameManager.WaitForModsLoaded()`, which the session's first mods-database registration completes (`GameManager.RegisterPdxSdk`), so a late load from that registration lands inside the city load and gets its completion. A late load still misses it when no registration has started 60 s into the wait (`kModsRegistrationTimeout`; the wait is unbounded once one has), where the load logs `Mods registration did not start within {0}s, continuing`; after a later login; and for a mod enabled later through a playset or mod-status change.
Found in Hall of Fame's usage statistics, where about 9% of 2026.6.0 sessions never tracked the main menu; its `docs/solutions/a-late-loaded-mod-misses-the-main-menus-loading-completion.md` carries the evidence.

### A mod's settings are written only on a clean quit

**Priority:** medium · **Cost:** trivial

The game writes settings to disk only on a clean quit (`AssetDatabase.global.SaveAllSettings()`, `Game.SceneFlow.GameManager.TerminateGame`), so a crash loses every change made since the launch.
The base `Setting.ApplyAndSave` is the obvious way out and the wrong one: it applies first, raising `onSettingsApplied`, and it picks the file to write from the short class name, so two mods with a `Settings` class write each other's file.
The rule: save a change that must survive a crash through `AssetDatabase.global.SaveAllSettings()`, which writes every mod's settings without applying them; the game queues those saves with one running and one waiting and drops the rest, so a burst costs at most two.
Found in Hall of Fame, whose menu tour came back after crashes; its `docs/solutions/a-setting-is-lost-when-the-game-crashes.md` carries the fix.

### A game lock across sessions

**Priority:** low · **Cost:** cheap

`cs2-dev`'s one-call slot lives in its server process, so two sessions hold a slot each and can drive one game at once: a `restart` in one stops the game under the other's wait.
A lock in the user data's `.cs2-dev` folder, naming its holder's PID so a killed server cannot leave it held, would refuse the second call.
Found reviewing the server, and hit when a stdio script drove the bundle while `/mcp` reconnected the session's own server beside it.

### A failed load from the UI takes `wait_load`'s whole wait

**Priority:** medium · **Cost:** unsure

A load that fails after a UI trigger holds `wait_load` to its ceiling: the game stays where it was and logs no fallback, so no `MainMenu reached` line ends the wait as it ends a failed `restart` load.
`MenuUISystem`'s load catch (`Game.UI.Menu.MenuUISystem`) only logs, which makes that entry the signal to try.
Found reviewing the server, by reading the catch; no live failed load has been seen.

### A `crash_dumps` tool

**Priority:** low · **Cost:** moderate

A `cs2-dev` tool that lists a run's crashpad reports and gives each one's verdict, in place of reading each dump by hand with `minidump-stackwalk`.
Build it when a session answering `A new crash report appeared` or `The game went away with no cs2-dev stop` read another run's report, took a `Simulated Exception` for a crash, or stopped because `minidump-stackwalk` was missing.
That last case decides the shape: the server would have to bundle a minidump parser, since it cannot install the tool.
Hit on 2026-11-03 (session `bd9d090f-c93c-439b-8abe-772f2fadddb5`): after a crash forced on Windows, a report beside the hint's `which may be a hang snapshot` was read as a boot snapshot until `minidump-stackwalk` showed `EXCEPTION_ACCESS_VIOLATION`.

### A `saves` tool

**Priority:** low · **Cost:** cheap

A `cs2-dev` tool that lists the saves and maps `restart` accepts.
Build it when sessions keep listing `Saves/` from the shell before a `restart`, or keep hitting `No save named`.
Try the smaller fix first: that error naming the saves it did find answers the second case with no new tool.

### Changing the playset from a tool

**Priority:** medium · **Cost:** unsure

A `cs2-dev` tool, or a skill teaching a recipe, that switches the active playset and enables, disables, subscribes or unsubscribes a mod in it, with or without a running game.
The same tool could search the Paradox Mods API for mods, so a session finds a mod's id, author and versions without the in-game screen or the website.
Without it, a session drives the in-game Paradox Mods screen by hand, or hands the step to the user; that screen renders in its own view, out of the `game_*` tools' reach, so only computer use clicks through it.
The game reaches the playset through the PDX SDK: `ModManager` disables a failed mod with `EnableModInActivePlayset(platformID, enable: false)` (`Game.Modding.ModManager.Initialize`), and a change in a running game reaches `OnEntryIsInActivePlaysetChanged`, which re-runs `ModManager.Initialize(reinitialize: true)` (`Game.SceneFlow.GameManager.RegisterPdxSdk`, its local `OnEntryIsInActivePlaysetChanged`).
That second path is how a session loads a mod late on purpose, the only way to reproduce a bug that needs a playset sync, so the tool would also make that repro deterministic.
`.cache/Mods/playset_config.json` is not the seam: hand-editing it fights the playset sync, as "Running the game without mods" notes.
Hit on 2026-11-05 (session `47ef7feb-8a68-4b88-8947-3f03f487d22d`): reproducing a ToggleMarkers bug that shows only when Paradox mods load after boot took a subscription the user made by hand, then a disable and re-enable through the in-game screen, since boots kept logging `Preload Mods at boot` rather than `Defer Mods load`.
A recipe that forces the late load with no tool: move one subscribed mod's folder out of `.cache/Mods/pdx_mods/` (out of the user data path too, or the user database loads it as a local mod) before the launch.
Online, `onLoggedIn` assigns the mods database task only after `SyncMods`, and `Initialize` waits on it after a fixed `Task.Delay(2000)` in `RegisterPdxSdk` (`Game.SceneFlow.GameManager`), so a sync that has to download the folder again outlasts the wait and the boot logs `Defer Mods load`; offline, `onNoLogin` registers at once and the boot preloads.
Hit on 2026-11-06 (session `cc528b48-bcdf-4841-b0eb-25d5950058f8`): reproducing Extra Assets Importer's losses on a late playset load, a 175 MB subscription moved aside gave `Defer Mods load` on each of three boots, against two preloads and two late loads by chance before it; local edits of `activePlaysetId` and of `isEnabled` in `playset_config.json` held across those online boots.

### `restart` names a mod's folder only through its Burst line

**Priority:** low · **Cost:** cheap

`restart` with `mod` names where the mod loaded from only through `Loaded additional Burst code <path>` (`describeModLoad`, `plugins/cs2-modding/mcp/src/tools.ts`), so for a mod with no Burst code its result cannot tell the local `Mods/<Mod>/` from a `.cache/Mods/pdx_mods/` copy.
`Modding.log`'s `Registered UI Module … [assetdb://user/Mods/<Mod>/<Mod>.mjs]` line names the folder for any mod with a UI, the line to read beside the Burst one.
Hit on 2026-11-03 (session `994514f5-49e9-40e9-a829-1522b6a7d659`): the mod template's setup skill had to send the agent to `Modding.log` itself to confirm a local load, since a template mod without its sample slice ships no Burst code.

### `restart` reports a mod loaded when its `OnLoad` never ran

**Priority:** medium · **Cost:** unsure

`InitializeMods` logs `Loaded <mod> in <n>ms` around `ModInfo.Load` whatever the outcome, and `Load` sets `MissedDependenciesError` and returns without throwing when a reference is unresolved (`Game.Modding.ModManager.ModInfo.Load`), so a mod that never ran logs a load and no error.
Its Burst line is no proof either: a dependent mod's load pulls the assembly in as a reference and logs `Loaded additional Burst code` for it.
`restart` reads both lines and answers `Modding.log logs its load … and no error` with the Burst path.
Candidate signals: the mod's own log not rewritten by this run, or a load time under a few milliseconds with no Burst line in the same initialize pass; the game's own record is the `ModsLoadingFailed` notification, which reaches no log.
Hit on 2026-11-06 (session `cc528b48-bcdf-4841-b0eb-25d5950058f8`): a local Extra Assets Importer whose dependency ExtraLib was a late-loaded subscription got `Loaded … in 2.5ms, and no error` and a Burst path, while `ExtraAssetsImporter.log` still held the previous run's lines.

### How the game shapes an edge's curve

**Priority:** medium · **Cost:** cheap

Nothing says how the game places a `Curve`'s bezier handles, so a mod that refits a road (moving a
node, reshaping its neighbours) invents its own rule. Two facts would cover it: in
plan, `NetUtils.FitCurve(startPos, startTangent, endTangent, endPos)` fits the curve to the end
tangents (`Game.Net.NetUtils.FitCurve`); in height, `NetToolSystem.LinearizeElevation` sets
`b.y` and `c.y` at a third and two thirds of the end-to-end change, by curve parameter, whatever the
plan shape (`Game.Tools.NetToolSystem`), and every curve the net tool draws
(simple, complex, continuous, parallel) goes through it; Replace instead refits the plan with
`FitCurve` and keeps the old `b.y` and `c.y` (`NetToolSystem`'s `CreateReplacement`). A
refit that shares the rise by plan position instead pulls a road that curves in plan off the game's
height profile.
Hit on 2026-11-04 (session `030e656a-6e9b-4907-992a-1680d06ee970`): fixing Move It's neighbour
reshape, the first rule shared the rise by plan projection, and only a review against the decompile
found the game's thirds.

### A forced `stop` truncates the settings files the game is writing

**Priority:** high · **Cost:** cheap

On a clean quit the game writes every settings file (`AssetDatabase.global.SaveAllSettings()`), and with a heavy playset that quit outlasts `stop`'s 60 s, so the kill lands mid-write and leaves files cut short at a 1024-byte boundary.
The game then fails to parse each one (`COCParserUnexpectedEndException` in `FileSystem.log`) and falls back to defaults: the user's own settings are lost, `Settings.coc` included.
The fix is a snapshot of the user data's `*.coc` files (and `ModsSettings/`) before the close request and their restore after a forced end, said in the result; a longer wait alone only moves the threshold.
Hit on 2026-11-06 (session `cc528b48-bcdf-4841-b0eb-25d5950058f8`): one of three forced stops of a 329-mod playset truncated `Settings.coc`, `FallbackSettings.coc` and four mod settings files, two of them to 0 bytes, with no backup of the game's own.
A playset change still running when the close request lands makes the quit wait on its mod load too.
Hit on 2026-11-07 (session `cc528b48-bcdf-4841-b0eb-25d5950058f8`): a `stop` sent 20 s after a mod was enabled in the running game waited on that load, and the session took the wait for a stall; waiting first for `FileSystem.log`'s `Mod '<name>' enabled in <n>s` line avoids it.

### `restart`'s boot wait ends on age while the boot still moves

**Priority:** medium · **Cost:** cheap

`restart` gives a boot `BOOT_SECONDS` (`plugins/cs2-modding/mcp/src/cycle.ts`) to log `MainMenu reached`, and a boot still moving past it fails as if it stalled.
A deferred online boot adds the subscribed mods during the menu's own load, and a mod that holds the first load until they arrive pushes the menu past ten minutes on a heavy playset.
End the wait on progress instead: fail once `SceneFlow.log` and `Player.log` stop growing for a span, and keep a high ceiling only against a log that grows forever, as "Wedge detection on wire progress rather than on age" argues on the unity side.
Hit on 2026-11-07 (session `cc528b48-bcdf-4841-b0eb-25d5950058f8`): a deferred boot on the development player, with ModLoadingFixes holding the first load for 622 s, reached the menu 11.6 min after launch; `restart` had returned `No main menu after 600 s` while the logs still grew.

### A hang snapshot's event says nothing of recovery

**Priority:** medium · **Cost:** cheap

The monitor's `A new crash report appeared, which may be a hang snapshot` fires on each watchdog snapshot, and a deferred boot takes some whenever the main thread stays busy adding late mods.
The event cannot tell a stall the game came out of from one it died in, so each costs the session a look at the logs.
The monitor already reads the logs: check them again a little after the report, and say in the event whether the game kept logging or went silent.
Hit on 2026-11-07 (session `cc528b48-bcdf-4841-b0eb-25d5950058f8`): the event fired on every deferred boot of the session, the game recovered each time, and the boots' main-thread busy spells ran up to 117 s.
Hit on 2026-11-09 (session `cc528b48-bcdf-4841-b0eb-25d5950058f8`): one snapshot 72 s into a boot and one during a save load, the game recovering from both; each weighs about 130 MB in `.cache/backtrace/crashpad/reports`.
Wanted too: hang snapshots off during development, or opt-in.
The watchdog is Backtrace's ANR thread (`Backtrace.Unity.Runtime.Native.Windows.NativeClient`): it starts only when `BacktraceConfiguration.HandleANR` is set, true by default, and snapshots when the main thread's update time stays still across one `AnrWatchdogTimeout`, which the game raises to 15 s (`Colossal.Logging.Backtrace.BacktraceHelper`).
`PauseAnrThread(true)` and `Disable()` (`Backtrace.Unity.Runtime.Native.Base.NativeClientBase`), reached through `BacktraceClient.Instance.NativeClient`, stop it on a running game, so `restart` could take an option that calls one through the `unity` eval on a debug-patched game. (Unverified: whether an eval reaches the game before a boot's first busy spell.)

### An exception in a coroutine's first segment never reaches `StartCoroutine`'s caller

**Priority:** low · **Cost:** trivial

`StartCoroutine` runs a coroutine up to its first `yield` before it returns, and the engine logs an exception thrown in that part and stops the coroutine: a `try`/`catch` around `StartCoroutine` never sees it.
A mod that starts a coroutine inside a `try` to catch its start-up failure gets a silent stop, the error only in `Player.log`.
The rule: catch inside the coroutine, and have it report a failure to whatever waits on it.
Seen on 2026-11-07 (session `cc528b48-bcdf-4841-b0eb-25d5950058f8`), reviewing ExtraAssetsImporter's import pass: `Player.log` held a coroutine's first-segment exception while the `catch` around its start stayed silent.

### The error monitor is gone after a context summary

**Priority:** high · **Cost:** unsure

The `cs2-dev-errors` monitor starts only on `on-skill-invoke:cs2-modding:cs2-modding`, and an agent resuming from a context summary rarely loads the skill again, so it drives the game with no monitor and learns of a new error, a dead game or a crash report only from the next `cs2-dev` result's hint.
Two ways to weigh: start the monitor on the first `cs2-dev` tool call too, if Claude Code's monitor triggers allow it; or have every `cs2-dev` result's hint say that no monitor runs and that loading the skill starts one.
Hit on 2026-11-07 (session `cc528b48-bcdf-4841-b0eb-25d5950058f8`): after a summary the session ran two game boots without a monitor until the user asked for it, and loading the skill started it.
Wanted: the monitor in every session that drives the game, however the agent got there; meanwhile a user-level instruction to load the skill again after each summary covers it, for a user who wrote one (session `cc528b48-bcdf-4841-b0eb-25d5950058f8`, 2026-11-09).

### A throw from `OnDispose` after a failed `OnLoad` stops the mods after it

**Priority:** medium · **Cost:** trivial

A mod whose `OnLoad` threw still gets `OnDispose`, and the failure reads as logged.
`InitializeMods` calls `Dispose` inside its catch with no `try` of its own (`Game.Modding.ModManager.InitializeMods`), so a throw from that `OnDispose` escapes: the `Error initializing mod` line never comes, no mod after it loads, and no UI module registers.
The rule: an `OnDispose` that runs after a failed `OnLoad` must not throw, since only the quit wraps each dispose in a `try` (`ModManager.Dispose`).
Seen on 2026-11-08 (session `cc528b48-bcdf-4841-b0eb-25d5950058f8`), in ExtraAssetsImporter: with its `OnLoad` made to throw before its settings loaded, its `OnDispose` threw a `NullReferenceException`, and 7 mods loaded and no UI module registered, against 151 mods and 45 UI modules once guarded.

### One null value drops a whole `MemorySource`

**Priority:** medium · **Cost:** trivial

One bad entry in a localization source drops every entry after it, the survivors being those enumerated before it; `MemorySource` loses them all.
Its `ReadEntries` parses every entry in a first pass and only then returns the dictionary (`Colossal.Localization.MemorySource.ReadEntries`), so a null value throws there, an `ArgumentNullException` from `Regex.Matches`, before any entry is read.
The source stays registered, so it fails again each time the locale is read.
The rule: a `MemorySource` holding one null value adds nothing, so a mod batching many assets' strings into one source keeps nulls out of it.
Seen on 2026-11-08 (session `cc528b48-bcdf-4841-b0eb-25d5950058f8`), in ExtraAssetsImporter: a pack with no name put a null into the batch of every pack's default names, a name added before the null was lost, and `Localization.log` logged the failure three more times at quit.

### One settings name in two `.coc` files breaks the settings save at quit

**Priority:** medium · **Cost:** trivial

A `.coc` file holds one named block per settings class, and the `ModsSettings/<Mod>/<Mod>` folder is a convention; two files can hold the same name.
The quit's settings save then fails with "An item with the same key has already been added", then a sharing violation on the `.coc`, and the game appends a second block to the file `FileLocation` names.
The rule: a settings name lives in one file, so a mod that moves its `FileLocation` deletes the file it left. (Unverified: the move case; a mod update that moves its `FileLocation` would settle it.)
Seen on 2026-11-09 (session `cc528b48-bcdf-4841-b0eb-25d5950058f8`), in ExtraAssetsImporter: a bench script wrote the mod's settings to `ModsSettings/ExtraAssetsImporter/ExtraAssetsImporter.coc` beside the root `ExtraAssetsImporter.coc` that its `[FileLocation("ExtraAssetsImporter")]` names, and the quit of each of four runs logged the error.

### Two mods' same-named images answer each other's requests

**Priority:** unsure · **Cost:** cheap

The scaffold emits images as `images/[name][ext][query]` under `publicPath: "coui://ui-mods/"`, and every mod's folder is a location of that one host, tried in an order no mod controls, so two mods each shipping `images/icon.svg` request one URL and one of them gets the other's file.
The right move: give the images output a mod-prefixed directory through the asset rule's `generator.filename`.
A raster base name the game also ships in its `UI/SharedImages` resources is shadowed whatever the directory.
The symbol would be `Colossal.UI.DefaultResourceHandler`, the base of the `GameUIResourceHandler` the game's UI runs on, rather than the engine's stock `cohtml` handler of the same name.

### A `try`/`catch` around a call to a missing member never runs

**Priority:** unsure · **Cost:** cheap

Mono resolves a member reference when the containing method is JIT-compiled, so a member another mod or a game update removed throws before the method's first statement, and a `try`/`catch` around the call never fires.
The right move: put the call in its own `[MethodImpl(MethodImplOptions.NoInlining)]` method and guard the call to that, or look the member up by reflection, where a missing one is a testable `null`.
It rests on the runtime rather than game code, so the trap would carry `[symbol: none]`.
Not re-run in the game's Mono.

### Another assembly's constants go stale in a mod without a sign

**Priority:** unsure · **Cost:** cheap

A `const`, an enum member's value and an optional parameter's default are copied into the calling mod at compile time, so after a game update or another mod changes one, the mod keeps running with the old value, with no exception and no log line.
The right move: rebuild against the current assemblies after a game update, and read a value that must track its source through a `static readonly` field or a property.
It rests on the compiler rather than game code, so the trap would carry `[symbol: none]`.

### The game's assemblies carry no nullable annotations

**Priority:** medium · **Cost:** trivial

No decompiled game assembly has a nullable context (no `#nullable enable` in `Game`, `Colossal.Core` or `Colossal.PSI.Common`), so a mod built with `Nullable` and `TreatWarningsAsErrors` gets no warning on a null a game type hands back.
`Colossal.PSI.Common.Mod.tags` is null for a playset's mods in a game not signed in to a Paradox account, and `mod.tags.Contains(…)` compiled cleanly, then threw `ArgumentNullException` on every screenshot capture of the session.
The right move: treat each reference read from a game type as possibly null unless the decompile or the running game proves it set.
Hit on 2026-11-12 (session `e386823e-50c5-4015-819a-9005bd7c779a`), Hall of Fame's asset mods list.
