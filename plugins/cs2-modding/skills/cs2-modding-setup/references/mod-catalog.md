# Community mod source worth reading

Verified against game version 1.6.0f1.

Open-source mods are the only place several modding techniques are written down at all, and reading one is usually faster than deriving the technique from the decompile.
This catalog answers two questions: which repository demonstrates the thing the user is about to build, and what a local corpus should contain if they want one.

An entry certifies exactly two things: the source is public, and it demonstrates the techniques named beside it.
Treat the list as provisioning input on that basis, and read a mod's own repository and store page for anything else you need to know about it.

Each entry separates what the mod **does** for a player from what its source **demonstrates** to a mod author.
Match on the second when answering "what should I read for X".
An entry carrying a plain note under its `Source:` line needs more than a default-branch clone, and that note says what.

## Provisioning a corpus

Optional, separable from the decompile, and worth doing only for a user who wants to read at length or grep across several mods at once.
For a single question, recommend one repository and link it.

Agree on a corpus root, clone shallow, and record the root under `Mod corpus root` so later sessions find it:

```powershell
$corpus = "<corpus root>"
git clone --depth 1 https://github.com/<owner>/<repo>.git "$corpus\<repo>"
```

The corpus is read, never harvested: each repository carries its author's own licence, and this plugin's knowledge prose states techniques on its own authority rather than copying code or crediting a mod.

## Reading a mod's published description

No browser needed, and no clone either.
The store pages render client-side, so a plain fetch returns the page title and nothing else, but the store API answers with JSON: `https://api.paradox-interactive.com/mods?modId=<id>&os=Windows`, where `modDetail.longDescription` holds the full page text.
The same text also lives in the repository, at the mod project's `Properties/PublishConfiguration.xml`, which is the file the toolchain publishes from.

## Custom tools and the placement pipeline

### Advanced Line Tool

Source: [algernon-A/LineTool-CS2](https://github.com/algernon-A/LineTool-CS2)

**Does:** Places objects in straight lines, curves, circles and grids alongside the game's own line tool, with fence and wall-to-wall alignment, elevation modes, spacing and rotation control, and a live preview that can be adjusted before committing.

**Demonstrates:** Cooperative tool design — it inserts itself into the tool list at `OnCreate` and hands control back to the previously active tool instead of replacing anything.
Its `CreateDefinitions` is a Burst-compiled port of the game's own object-creation pipeline, so placed objects go through the same `CreationDefinition`/`ObjectDefinition` path as vanilla, sub-objects and sub-nets included.
Line modes are a strategy hierarchy, one class per mode, each owning its click, drag and overlay behaviour.
Its single runtime patch is small and targeted, applied only to refresh a private UI binding, and the same binding is read elsewhere through a cached reflection accessor rather than a second patch.
Yielding shared player-facing affordances to competitor mods rather than fighting for them — a snap mode it stops offering and a transparency setting it hands over — with one competitor's detection isolated in a file of its own that tries three possible assembly names and three type names before giving up.

### Move It

Source: [yenyang/CS2-MoveIt](https://github.com/yenyang/CS2-MoveIt)

Clone an alpha branch rather than the default one: the architectural changes worth reading are there.
`git ls-remote --heads https://github.com/yenyang/CS2-MoveIt.git 'Alpha*'` lists them, and the newest goes to `git clone --branch`.

**Does:** Selects, moves, rotates, copies, and deletes anything already placed — trees, props, decals, buildings, network nodes, and segment curves — with marquee selection, a manipulation mode for dragging segment control points, and alignment helpers.

**Demonstrates:** The fullest tool lifecycle in the corpus, built on the game's object tool base and split across partial classes by concern (lifecycle, update, jobs, filtering).
Typed raycast wrappers registered with the game's own raycast system.
Overlay rendering dispatched per entity kind, with the draw and update passes as jobs.
Cross-mod integration by reflection over loaded assemblies rather than a compile-time reference, so a missing sibling mod costs nothing.
The push direction of the same arrangement: invoking a static on another mod's bridge class to register itself with it, latching the result so the push happens once, and exposing a public static of its own in a partial file whose only purpose is that other mod's reflection.
Emitting the game's own placement definitions by hand for every selected entity and each of its sub-nets and sub-areas, switching one flag field between relocate, delete, hidden and recreate, rather than using the definition helper its base class offers.
Command buffers the mod allocates itself and plays back on the spot inside a system, rather than handing the work to a barrier.
A custom quadtree iterator walking three vanilla search trees with mod-defined selection, serving its marquee, bounds, point and ray searches.
The vanilla node and edge generation systems ignore a change to a node's elevation, so the mod ships a system spliced ahead of composition selection whose only job is to set the modify flag the pipeline needs.
Bezier control points modelled as mod-created entities of a three-component archetype — prefab reference, culling info, and its own control-point component — so handles are hoverable and selectable through the same paths as real entities.
Worked build engineering of the UI seam: the npm build hooked after the deploy target with the wipe it dodges written down beside it, MSBuild switches for turning the UI half off, a lockfile stamp so `npm ci` runs only when the lockfile changed, timeouts on both npm calls, and a failure that aborts a release build and only warns in Debug.

### Area Bucket

Source: [Cmyna/AreaBucket](https://github.com/Cmyna/AreaBucket)

**Does:** Fills an enclosed region with a surface or area in one click, flood-filling outward from the click point against roads, lots, existing areas and lanes as boundaries, instead of tracing the outline by hand.

**Demonstrates:** A whole geometry algorithm expressed as a chain of Burst jobs — collect boundaries, generate rays, drop intersected and obscured ones, merge into polylines, emit an area definition.
Terrain-only raycasting by narrowing the raycast type mask.
A preview entity path that renders an outline before anything is committed.
Its own native containers and a line-sweep intersection pass, which is what reading it costs and what it teaches.
Each job owns its component and buffer lookups behind an assign-once and a refresh-per-update method, which is the clearest hand-written form of the type-handle discipline the source generator otherwise supplies.
Adding a panel to the game's developer menu imperatively — one panel taken from the render pipeline's panel registry at mod load and held in a static, appended to from each system's own `OnCreate`, so the menu shows live getters, toggles and numeric fields with no game type involved beyond the registry itself.
Naming every UI registry path and export as a typed constant in one file and spreading the pair into the registry call, which is the shape that survives a game version renaming a path.
No runtime patching anywhere.

### Network Tools

Source: [lucarager/CS2-NetworkTools](https://github.com/lucarager/CS2-NetworkTools)

**Does:** Freehand network editing beyond the road tools — adding and removing nodes, sliding and dragging them, connecting distant nodes with generated curves, offsetting a selection into parallel networks, and generating grids and circles of segments.

**Demonstrates:** A shared tool base class carrying raycast filtering, eligibility marking and handle lifecycle for a family of tools.
That base class and one intermediate layer are both abstract, with concrete tools registered at load — the layering the developer menu's tool enumeration survives only because a concrete descendant already exists when it runs.
A vanilla React widget table that pulls its multi-component float sliders out of the game's debug UI module tree, where alone those widgets exist, while every other entry comes from the editor's.
Resolving an edge raycast hit down to the nearest node.
One output-mode switch that decides whether a job writes preview definitions or mutates the network directly on apply.
Interactive 3D handles as their own drawable, cullable entities.
A source generator that emits the TypeScript binding declarations from the C# side, which is the only answer in this corpus to C#-and-frontend drift.
A read-only structural verifier for the vanilla electricity flow graph — the mod's own mirror of the game's deserialize-time check — shared between a deserialize-phase checker and a runtime watchdog that debounces across two scans because the graph rebuilds asynchronously after every network edit; both ship commented out of registration, a harness for catching corruption during play before a save carries it.
Consuming five vanilla data providers in one job, four of them taken with their own handle and combined into a single schedule, with one of the five never registered back.
Vanilla systems disabled per tool rather than per session, each tool declaring which ones it cannot coexist with, and every one restored from both the stop hook and the destroy hook so a tool killed mid-run cannot leave the game's validation off.
Splitting an edge without touching it: a zero-length course at the hit point whose course positions carry the curve parameter as their split position, leaving the game's own course-split pass to perform the split.
A port of the game's own network snap job — three spatial trees, terrain and water beside them, and component lookups by the dozen — reproducing layer compatibility, height-range intersection, strict-node priority, composition half-widths, buildable net areas and the rule that an owned edge may only be snapped at its endpoints.

### Node Controller

Source: [bruceyboy24804/NodeController](https://github.com/bruceyboy24804/NodeController)

**Does:** Per-node control over how roads meet: node style, shift, twist and slope, dragging or rotating individual segment ends and corners, crosswalk overrides, and underground editing for tunnels.

**Demonstrates:** Disabling the game's geometry system outright and scheduling a replacement in the same slot — the clearest example of substitution over patching, and it ships no patches at all.
Per-node settings as a versioned serializable component, so the edits survive a save.
A tool built as a state machine of interaction modes, one class per gesture.
Its own spatial search rather than the tool raycast, picking segment ends by mouse ray and camera field of view.
Registering scroll-wheel input actions the input API does not expose, by reflecting into the input manager.
Cloning a shared composition entity to give one segment end its own lane cross-section, with the clone stripped of its creation and update markers so the systems that own compositions do not adopt it, and the original held for restore.
Marking a network edit for rebuild through the road aggregate as well as the node, the edge and the edge's far node.
The forked geometry system differs from the original by a single line, which is the honest measure of what substitution costs when the game exposes no hook.

### Extra Detailing Tools

Source: [AlphaGaming7780/ExtraDetailingTools](https://github.com/AlphaGaming7780/ExtraDetailingTools)

**Does:** A precision transform tool with per-axis numeric input and configurable increments, a menu exposing net lanes, surfaces and decals for detailing, and the editor's snap-to-surface behaviour brought into the main game so objects snap to rooftops and walls.

**Demonstrates:** Extending vanilla tool behaviour by patching the two methods that decide rotation and snap masks, rather than replacing the tool.
A reusable generic base for adding custom snap modes to any tool.
A batched custom raycast where several callers share one pass per frame, keyed by context.
Runtime bridging to another mod through a dedicated bridge class, whose methods are each resolved once by explicit parameter-type array rather than by name, cached, and invoked through helpers that return a neutral value on a missing member or a thrown call — so every entry point degrades to a no-op when the other mod is absent.
Declaring its own input usage string beside the built-in ones, so its transform-tool actions are not reported as conflicting with vanilla bindings they share keys with.
A port of the game's own selection-definition builder that branches on what the selected entity is and emits the matching definition kind for each — network course, object, area nodes, route waypoints, notification icon, aggregate elements — which is the widest coverage of that mechanism outside the game itself.
Reading a game-owned buffer of child entities and re-emitting them as the placement pipeline's definition buffer, filling each definition's original, position and connection from three separate components on the child.

### Platter

Source: [lucarager/CS2-Platter](https://github.com/lucarager/CS2-Platter)

**Does:** Adds placeable "parcels" holding zone cells, so zoning can be placed anywhere and at any angle with snapping and setbacks, while the game's own demand, land value and growth logic still decides what spawns on them.

**Demonstrates:** A serializable component in its minimal form — fields written and read in the same order, a nested game struct passed straight through, and a flags enum cast to its underlying type in both directions, since the reader and writer have no enum overload.
Custom prefab subclasses and runtime prefab variant generation.
Deserialize-phase jobs that rebuild the links between mod entities and game entities after a load.
Rewriting a vanilla system's private entity query at runtime so stock code skips the mod's entities — the most aggressive compatibility technique in the corpus, and worth reading as much for its risk as for its power.
Prefab-data initialisation systems anchored immediately after the vanilla initializer whose output they overwrite, which is the readable way to correct derived prefab data rather than fight for it.
A patch that records whether it was the one that widened a filter, and narrows the results only in that case, so it composes with another mod doing the same thing and degrades to a no-op if the game starts doing it too.
Hand-built input actions registered by reflection, because scroll bindings are not exposed.
Taking a placement definition away from its vanilla consumer by removing the creation component from a system spliced immediately before that consumer, played back synchronously because a phase barrier would land after the consumer has already run.
An in-engine test scenario system, debug-only: its scenario classes carry the test framework's own descriptor attribute, and the mod reflects them into that framework's private scenario dictionary after load, so they appear as buttons on the developer menu's `--qaDeveloperMode`-gated test tab beside the game's own — the framework registers its roster once, before a mod's assembly is reachable, and the re-sort it exposes returns a new dictionary, which is what forces the write back through the private field.
Hiding the developer menu's own UI system from test setup, so a scenario runs against a clean screen.
A build-time export of its English string table into the repository, locating the destination from the compiler's caller-file-path rather than a hard-coded developer directory.
The community binding helper in its most readable copy — a UI system subclass with typed create-binding overloads over a reflection-driven writer and reader — which is the shape nine repositories here share and the place to read it, defects included: its getter bindings are registered without the update pump, so they never refresh, and its writer reflects over the payload's members on every push.
A mod-owned spatial index following the vanilla search systems closely: outstanding handles completed before disposal, and the tree cleared from the pre-deserialize hook and refilled on the next update through a first-load flag.
Building and owning zone blocks outside the road network — creating cell buffers, running a fork of the vanilla cell-check pipeline after it, and managing a block's `Owner` by hand, which the vanilla spawner requires of any block it will spawn on.
A second fork beside the cell-check one: the vanilla road-connection pass re-run for the mod's own entity kind, consuming the game's network quadtree through its reader-registration protocol.
Cleaning up orphaned notifications from a settings action, by sweeping every icon entity whose owner reference has gone null, and an in-engine test asserting that the icon buffer holds exactly one notification after the placement.

### Traffic

Source: [krzychu124/Traffic](https://github.com/krzychu124/Traffic)

**Does:** A lane connector for rewiring turning lanes at an intersection and a priorities tool for yield and stop rules, with modifier keys selecting unsafe, track-only, road-only or shared connections.

**Demonstrates:** The reference example of save-data migration: a version constant, per-version repair jobs, and validation passes that fix or drop data an older version wrote.
A migration version kept in a save section owned by a system rather than on any entity, with a defaults hook supplying version zero for saves written before the mod existed.
A formerly-serialized-as attribute on that system, so a save written under its earlier namespace still resolves.
Importing the persisted components of a different mod: the foreign type is resolved by name and queried through a runtime component type, but the chunk job that does the migration reads only vanilla components — the foreign type is a marker for which entities that mod touched, the result is re-derived from the state it had already written, and the marker is then removed from every entity that carried it.
A second compatibility system beside it taking the non-destructive position on the same machinery: it resolves another mod's tag component by name and queries on it purely as a signal, resetting only its own state on the entities that carry it.
Disabling the vanilla lane system and taking over its slot, with no patching anywhere in the codebase.
Registering a system frames after the mod loaded, from a deferred main-thread callback, which is how it disables another mod's system once that mod is known to be present.
Reporting a failure to the player end to end: a notification carrying a failed progress state whose click opens a message dialog with a copyable details pane, the dialog's callback popping the notification.
A logging facade built on compile-time categories, each method carrying a conditional-compilation attribute so a build without the symbol drops the call and the message it would have built, with one symbol gating both an info-level and an error-level method.
A custom raycast system registered in the raycast phase.
Rebindable actions declared as settings attributes and consumed by the tool, scoped with its own usage strings beside the built-in ones.
Letting the player choose, from the settings screen, between the mod's own bindings and watchers that keep them equal to the vanilla apply and cancel bindings.
Generating the frontend's binding types from the C# rather than writing them twice — a typings generator configured to substitute the types the game's own declaration files export for the engine's entity and binding-proxy types, so the binding names, the enums and the payload shapes all come from one side.
Renaming the webpack image output directory away from the scaffold's default, because every mod's images resolve under one shared `coui://` host and the default collides.
Migrating another mod's saved data on load, and detecting an incompatible build of it by scanning loaded assemblies.
Its own placement-definition component beside the game's, with a generator, a validator, a clear system and an apply system each spliced next to the vanilla one it parallels — the corpus's only extension of that protocol to a new kind.
An empty prefab class with no content, registered in the pre-deserialize hook, whose only job is to give the mod's own entities a prefab reference the game's load-time reference remapping can resolve.
Its own language dropdown independent of the game's, registering the chosen translation under whatever locale the game is currently set to and re-applying the swap whenever the player changes language.
Two mod-owned quadtrees published behind the game's own reader/writer handle protocol, and every Burst attribute gated behind a symbol only its Release configuration defines.
A gizmo debug system on the game's own debug base class that renders its own developer-menu panel rather than joining the vanilla gizmos tab — a port of the game's option-rendering method into a mod-created panel, with the whole registration behind a build symbol so a release build carries neither the panel nor the system.
A near-verbatim fork of the disabled lane system, thousands of lines of it, with mod-authored changes flagged by `NON-STOCK` markers — a fork you can diff against its original by eye.
The forked system re-registering itself on both sides of the vanilla plumbing its original served — as a terrain height reader, as a writer on the downstream system's queue, and as a producer on its barrier — which is what makes substitution invisible to everything downstream.
A working implementation of the rival approach — narrowing the vanilla system's private query by reflection so it skips the mod's entities — shipped but never registered, the substitution having been chosen over it.
A mod-owned mirror of the engine's temporary-entity component, because the real one on a mod entity is claimed by the systems that own the placement pipeline.
Writing a lane's yield, stop and right-of-way rules as flags on the generated lane component during lane creation, which is why the feature requires owning lane generation rather than editing anything after it.
Resolving a vanilla error-notification prefab without naming it, by walking the chunks of the query that pairs error data with notification-icon data and matching the error kind, latched so the scan runs once — with the resulting icon added against the tool's preview entities rather than the real ones.

## Replacing a vanilla system instead of patching it

### Plop the Growables

Source: [algernon-A/PlopTheGrowables](https://github.com/algernon-A/PlopTheGrowables)

**Does:** Lets growable buildings be placed anywhere and stops them being condemned when the zoning under them is wrong, missing or later changed, optionally keeping the requirement for buildings that grew naturally.

**Demonstrates:** The cleanest small example of the substitution pattern — disable the zone check system, run a fork of its exact job pipeline that special-cases the mod's own entities.
Empty tag components using the engine's own serialization, so per-entity mod state rides along with the entity and needs no save section of its own.
A deserialize-phase system that backfills entities from saves written before the mod existed.
Reusing the game's existing "historical" flag rather than inventing a parallel lock.
Translations as embedded per-locale CSV with its own quote-aware reader, settings keys written as a short packed prefix the loader expands into the long generated key, so a translator edits a two-column spreadsheet.
Reversing an abandonment by hand — what level-down strips: the consumer and producer components, the market state, the building condition, and the road-edge refresh that rebuilds the utility connections.
The singleton that maps a game-wide notification concept to its prefab entity, carried by value into a Burst job beside the icon command buffer, so a forked check can add and remove the same icon the vanilla one did.

### Realistic Trips

Source: [ruzbeh0/Time2Work](https://github.com/ruzbeh0/Time2Work)

**Does:** Rewrites when and why citizens travel — rush hours, quiet nights, lunch breaks, weekday and weekend patterns, shift work, remote work and school attendance — with presets and an optional seven-day week and slowed clock.

**Demonstrates:** Substitution at the largest scale here: roughly a dozen vanilla simulation systems disabled and replaced.
Reimplementing the game's time model, deriving ticks per day from the vanilla constant scaled by a factor, which is where to look for how the simulation's time units actually work.
The corpus's only override of a system's update offset, copied from the vanilla system it forks along with the interval.
Read the `cs2-modding` skill's [`mod-lifecycle-and-ordering`](../../cs2-modding/references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md) reference before copying the pattern, because the offset does not transfer.
Burst-compiled per-citizen work with a per-citizen deterministic random stream.
A serializable component for per-citizen state whose deserializer wraps its reads in a catch that cannot fire on the size mismatch it is written for.
A system save section whose read is gated on one of the game's own save-format tags while its write is unconditional, which is the coupling hazard rather than a pattern to copy.
Carries the game's own generated type-handle struct into each fork and refreshes every handle by hand at the top of the update, which is what a forked system must do once the source generator is no longer writing that code for it.
Replacing one branch of a vanilla job-scheduling method from a prefix — a private target resolved by explicit signature, the type handles and lookups the vanilla method would have refreshed rebuilt by hand, and the substitute job's handle returned rather than completed, so the caller's temporary allocations outlive the work.
Runtime detection of sibling mods by name, including keeping a dead system registered purely so old saves still load.
Retuning the game's balance without editing a prefab asset, by overwriting parameter components in place once prefab initialization has written them — some systems registered into two phases by two calls so they run inside the load and save pipelines as well, and one rewriting a per-prefab component rather than a singleton.
The corpus's only use of the module registry's SCSS form — merging a mod's own class map into a vanilla `.module.scss` so both rule sets apply — and extending a vanilla widget and its newer replacement in the same registrar, since both ship.
The corpus's only keyed UI bindings: one map binding keyed by statistics group, and a second delegating straight to the vanilla prefab-requirements writer, beside hand-written JSON writers for the panels a plain value binding could not shape.
Reaching a vanilla system's private per-source accumulator array through reflected field handles from a postfix on its update, paired with a postfix on the method that produced the value, because correcting the producer alone leaves the consumer's own cached copy untouched.
Toggling a pair of vanilla simulation systems off and back on against the in-game clock rather than once at load, so they sit disabled for part of every day.
Toggling a shared prefab component for the duration of an in-game event by scaling one field and inferring the applied state from the field's own magnitude rather than a stored flag — the failure mode to recognise rather than a pattern to copy, since the prefab is shared by every instance and anything else moving the field past the threshold breaks the toggle.
Building pathfinding requests by hand: the component pair added to the traveller, the path-method set composed per leg, a parked vehicle addressed as a lane entity plus a position along its curve, and a repath forced by removing the result components rather than by any update marker.
Replacing the game's weather sampler wholesale from a prefix that returns false, rebuilding the sample by evaluating the climate prefab's own curves at a rescaled time, which is how a mod that changes the length of the year keeps the seasons landing where the map's climate says they should.
Consuming a vanilla cell map from a forked system: taken through the owning system's data accessor, carried into a Burst job and scored by the game's own static evaluator rather than a reimplementation, with both the cell-map reader and the terrain height reader registered back after the schedule.
Writing to the city's statistics from a job through the owning system's own protocol — the event queue taken with its dependency handle, a statistics event carrying a statistic type and a delta enqueued per occurrence, and the schedule registered back as a writer — which is how a mod's simulation change reaches the player's graphs at all.
Reading a statistic back inside a Burst job as a three-part construction — the owning system's key-to-entity lookup, a buffer lookup for the per-city samples, and the game's own static resolver over the two — because the value lives behind a hash map rather than on a component.
Forking the statistics panel because a changed day length breaks it rather than because its content is wrong: the vanilla sample arrays come back shorter than the sample count and the chart's frame-to-date conversion drifts, so the fork left-pads every array and rescales the axis, and it reproduces four vanilla game-mode gates — map-tile upkeep, unlimited money, government subsidies and an absent transport type — that decide which statistics are shown at all.

## Prefabs and assets from code

### Road Builder

Source: [JadHajjar/RoadBuilder-CSII](https://github.com/JadHajjar/RoadBuilder-CSII)

**Does:** Builds custom roads in-game by combining lanes, medians and sidewalks in a drag-and-drop panel, then places them or retroactively updates every placed instance.
Custom roads are saved inside the city and travel with it.

**Demonstrates:** Composing a network prefab in code — instantiating the prefab, assembling its sections, edge and node states from lane-group prefabs, rather than cloning a fixed template.
Versioned custom save serialization with migration constants and post-load repair of invalid references.
Marker entities written into the save from a system registered in the serialize phase's front band, one per placed custom road, and deleted again later in the same frame, so a load knows which of the mod's on-disk files that city needs and can rebuild them from the save when the files are gone.
Mimicking the built-in apply and cancel bindings by copying them from the input manager, so a custom tool obeys the user's remaps.
Registering roughly fifteen systems across specific phases, which is a readable map of where prefab, serialize and tool work has to sit.
Regenerating a live prefab in place, which replaces the prefab entity outright, so the mod tags the outgoing one, throttles the rebuild, and clears the stale entity out of a vanilla system's private dictionary that the engine's own reference sweep cannot reach.
A null-checking wrapper around the generic prefab lookup, which otherwise reports success while handing back null whenever the requested type does not match.
A dictionary source registered under every supported locale whose entries are generated on each read, so names for roads the player builds at runtime localize without re-registering anything.
Authoring a road's utility carriage as part of the prefab: the electricity and water-pipe connection components added in code, with the composition requirement that gates electricity on a lighting upgrade read back off a vanilla road when importing one.
Propagating a prefab change to every placed instance by walking from the changed prefab to its edges, their neighbours across each shared node, and then each edge's compositions, nodes, sub-lanes and sub-objects — a walk the engine's own prefab-replacement pass does not perform for road edges.
Manufacturing the network pieces a composition needs rather than authoring them — cloning one wide vanilla piece per width and rewriting its width, geometry, surfaces and lane list — driven by a four-phase state machine advancing one phase per update, because each phase's prefabs must be registered before the next reads them.
Cloning vanilla prefabs selected by literal name out of a data-component query, stripping a name prefix, and re-attaching the service and UI components a clone needs to appear in the toolbar — with the literal-name coupling as the fragile half of the technique.
Authoring a generated prefab's unlock requirements: every requirement prefab in the game indexed by name from one query spanning the feature, dev-tree-node and three built-requirement data components, then attached as a require-all list and a built-on-unlock list whose element type the component's own field forces to one requirement family — with the requirement names as hard-coded literals chosen by category and computed width, which is the fragile half.
Adding an editor-toolbar entry from a preload hook gated on editor mode, resizing the editor UI system's tool array and assigning it back, guarded against a repeat by scanning the existing entries for its own id — with the entry's enable and disable hooks overridden without calling base, so activation runs through the mod's own UI system instead of the base's panel-and-tool switch.

### Extra Assets Importer

Source: [AlphaGaming7780/ExtraAssetsImporter](https://github.com/AlphaGaming7780/ExtraAssetsImporter)

**Does:** Imports custom ground surfaces, decals and lane decals from folders of textures and JSON dropped into a mods data directory, and groups the result into an in-game asset pack alongside vanilla content.

**Demonstrates:** Registering a second asset database beside the game's own and populating it from a custom data source — the deepest asset-pipeline example available.
Building prefabs at runtime, attaching components, saving them as assets and registering them live with the prefab system from the main thread.
Driving the texture importer directly to turn image files into texture assets, with per-file locking against concurrent imports.
Mapping arbitrary JSON keys onto shader properties.
Content hashing to skip unchanged assets on the next load, and per-asset error handling so one malformed user folder does not fail the batch.
Marshalling work back to the main thread from a background import, including blocking a worker on a frame count, which is the corpus's most demanding use of the main-thread dispatcher.
Turning user-supplied per-locale JSON into a compiled locale asset in its own database, which the localization manager picks up through its asset-changed subscription rather than through an explicit source registration.

### Water Features

Source: [yenyang/Water_Features](https://github.com/yenyang/Water_Features)

**Does:** An in-game water tool for placing and reshaping streams, rivers, lakes and seas, plus optional detention and retention basins, seasonal stream flow tied to climate, and waves and tides.

**Demonstrates:** The save-safety pattern worth copying — a before-serialize system that collapses the mod's custom state back into vanilla fields, so the save loads correctly for someone who removes the mod, with the restoring half registered behind the writer so the running session keeps its state; the whole system self-gates on the game's legacy-water-sources flag, so the guarantee covers only that half of the water model.
Registering the same systems into three phases, so one implementation serves the simulation, the editor and the save pipeline.
Registering custom prefabs at load, gated by whether the game is in game or editor mode, from a preload hook on a system it creates but never gives a phase — the timing answer for prefab work that has to happen before a game loads and after the asset database is populated.
One tool serving both game and editor by branching on the tool system's action mode.
Burst jobs that tag vanilla simulation entities with the mod's own components through a command buffer.
Retuning a running vanilla simulation by writing its public tuning fields every update rather than forking it, with the pre-mod value captured once at system creation as the only record of it, and a companion that swaps the original back while the terrain tool is active and counts a cooloff down before restoring the mod's.
Two vanilla simulation systems switched off from a setting, paired with a one-shot cleanup that reverses what they already did — the event and damage components, the notification-icon buffer entries and the icon entities — because disabling a system does not undo its output.
Reflection into the climate system's private state for facts it publishes no accessor for, with the date read back through its string form because the property's value reads zero — read it for the reflection helper, not as the way to ask what season it is.
A mod-created simulation entity standing in for a global the game does not expose: a bare zero-radius water source the simulation ignores, holding the sea floor while every real sea source oscillates around it, destroyed from every exit path including before-serialize so it never reaches the save.
Calling the game's own validity calculation in a retry loop, growing the input until it stops returning the failure value and reporting the adjustment to the player, because the vanilla call reports an unusable result as a plain number with no error.
Update cadence expressed as updates per simulated day rather than as a frame count — a per-system constant divided into the day's tick count, the same idiom across four systems, so the number in the source reads as a rate instead of a period.
Putting a tool on the map editor's toolbar by copying the editor UI system's tool array into a longer one and assigning it back — from a UI system's `OnCreate` rather than a per-load hook, which is what makes a duplicate guard unnecessary, since the world is built once per process and the write survives every load.

### Tree Controller

Source: [yenyang/Tree_Controller](https://github.com/yenyang/Tree_Controller)

**Does:** Changes the age, type and colour of existing trees and bushes by single tree, building, radius or whole map, plops trees at a chosen age, and offers curated forest brushes, paused tree growth and a winter dead-model look.

**Demonstrates:** Rewriting object definitions after the tool creates them and before the game consumes them — the interception point for changing what a tool places without touching the tool.
Modifying existing prefabs at load, adding a missing component and zeroing a cost field.
Restoring the original value by reading it back off the authoring prefab object rather than off the prefab entity it overwrote, which is the corpus's only worked example of treating the authoring layer as the vanilla baseline.
A "safely remove" system that resets custom model state on demand, because some of this state is not safe to leave in a save.
Extending brush strength past the vanilla cap with a single targeted patch.
Forking the game's own resource-area update pipeline — the bounds sweep and quadtree walk that decide which objects an extractor area covers — re-run with the mod's own enableable marker written per object, and the mod registered as a reader on the vanilla system it duplicates.
Reading the current season the way the game defines it — the climate system's current climate entity resolved to its prefab, then that prefab asked which season the current date falls in — and handing the result into a Burst job as a plain enum, on an update-frame slice so only a fraction of the entities are touched per update.

## UI panels, info views and injection

### Find It

Source: [JadHajjar/FindIt-CSII](https://github.com/JadHajjar/FindIt-CSII)

**Does:** A searchable, filterable panel listing every placeable asset, vanilla and modded, with categories, favourites and a picker tool that selects an asset by clicking one already placed.
The panel stays open while placing.

**Demonstrates:** UI injection by replacing the game's own React components through the module registry — extending the asset menu, the right menu and the tool options panel, then appending its own tree.
This is the injection point, and the mod ships no patches at all.
Prefab indexing split into one processor class per category, discovered by reflection and instantiated at startup.
Full rebuild on load versus incremental updates driven by created and updated queries.
An extension hook that lets any other mod contribute a search predicate, found by reflection with no shared assembly.
Deriving hundreds of new prefabs from the loaded asset database by cherry-picking a few components off each original instead of cloning it, sharing the mesh reference, and declaring obsolete identifiers so a rename migrates saves.
Classifying prefabs by which payload data component they carry rather than by prefab class.
Naming prefabs it generates at runtime by copying the original's localized name out of the active dictionary and writing a fallback straight back into it, which is the fast path and is lost the moment the player changes language.

### Info Loom

Source: [bruceyboy24804/InfoLoom](https://github.com/bruceyboy24804/InfoLoom)

The shared build directory the project imports is not in the default branch, so its package references and the bootstrap that applies its patch are not readable from a plain clone; the patch body is.

**Does:** Adds panels for demand, workforce and workplace structure, demographics, residential, commercial and industrial data, trade costs and districts, and injects extra sections into the game's selected-object panel.
Read-only; it changes no rules.

**Demonstrates:** The two module-registry calls a UI mod needs — appending a panel to a named region, and extending the selected-info section list with its own sections.
Reading simulation state cheaply: a job gated on whether the panel is visible, with an update interval so the query runs only every few hundred ticks — on its simulation-phase systems, since an interval on a UI-phase system does nothing, and this source carries examples of both.
Replacing the game's own JSON binding output by returning false from a prefix, when the vanilla writer truncates what the panel needs.
The corpus's only source-generated per-entity job, which is the proof that the Entities source generators the official toolchain ships do work in a mod project.
A read-only census over the citizen population, naming the component set a demographic query needs and applying the moved-in, tourist, commuter and dead exclusions by hand rather than calling the predicates the game exports.
Reading a vanilla simulation system's published state instead of forking it: demand, tax and company-count state read through the owning systems' own getters, with the reader's job handle registered back through the add-reader calls at the two demand-data read sites — the repository's other read sites skip that registration, so copy the registered pair, never a skip.
Taking a panel's display grouping from the game's own UI configuration prefab rather than inventing one, so a category that aggregates several enum members stays aggregated the way the game aggregates it.
Reproducing a game figure by calling the game's own evaluator with the live city-modifier buffer rather than reimplementing the maths, and the storage that forces it: city-wide state is a component and a buffer on the city entity reached through the owning system, not a singleton, while the prefab-side description of the same effect is a separate buffer on the prefab.
Writing a panel's payload by hand through a raw value binding when the shape is a table rather than an object, with the update pushed explicitly from the system instead of polled.

### Recolor

Source: [yenyang/Recolor](https://github.com/yenyang/Recolor)

**Does:** Recolours individual placed buildings, vehicles, props and lane fences from a section in the selected-info panel, or in bulk with a painter tool over a radius, and manages shareable colour palettes.

**Demonstrates:** Extending the selected-info panel through a base class rather than a patch, and shipping no patches at all.
The corpus's deepest ordering chain — three systems anchored by type after one vanilla system, and a fourth anchored after one of those, which is the readable example of anchoring resolving recursively.
Per-instance colour as a serializable buffer element with hand-written read and write.
Palettes as the mod's own prefab type, building their components in the prefab lifecycle methods and cross-referencing vanilla theme and zone prefabs for filtering.
Burst jobs for "apply within a radius" against both transforms and curves.
One of its own components shadows a vanilla component of the same name that arrived later, and the source has both in use side by side, which is the readable case for namespace-qualifying every component a mod shares a name with.
Three tools that decline every prefab — `TrySetPrefab` returns false and `GetPrefab` returns null — so they are reachable only from the mod's own UI and cost the toolbar nothing wherever they sit in the tool list.
Emitting a selection definition against an existing entity so the game hands back a temporary copy the mod can edit, which is how it applies a colour through the placement pipeline instead of writing the live entity.
Writing a game-owned component and then adding the game's own change-event component so the vanilla propagation runs, instead of forking the system that would have propagated it, with the mod's own persistent buffer parked on the same entity.
Catching entities that join a relationship after the edit, through a second system whose query pairs the vanilla link component with the game's created and updated markers.
Player-authored translations: the player names and translates their own palettes in-game, each locale written to a JSON file beside the palette and registered as an in-memory source, guarded by a check that the game supports that locale at all.
The live season matched against each colour variation's own group identifier as a filter over prefab data, with the climate prefab resolved lazily and cached, and both resolution failures logged and answered with a false rather than a throw.

### Write Everywhere

Source: [klyte45/CS2-WriteEverywhere](https://github.com/klyte45/CS2-WriteEverywhere)

Clone with `--recurse-submodules`: three submodules carry shared code the mod project references, a plain clone leaves all three empty, and the compatibility patching lives in one of them.

**Does:** Attaches custom text and images to buildings, props and vehicles, driven by properties of the target object, with reusable layouts, custom fonts, image atlases and imported meshes.

**Demonstrates:** Rendering that leaves the entity renderer entirely — hooking the render pipeline's context callback and issuing draw calls per element with hand-built matrices.
Burst-compiled glyph layout writing vertices into a native queue, on top of a hand-written font file parser.
Loading meshes from disk at runtime and composing per-entity materials.
Attaching mod data to game entities by owner back-reference instead of spawning a parallel object graph.
A version int read into a local that bails when the save was written by a newer build of the mod, then one appended conditional block per revision that added a field, so a new field costs no edit to any existing branch.
The same classes serve as both the mod's XML file format and its save payload, which works because the reader's overload for a reference type deserializes into an instance the caller has already allocated.
The corpus's only cleanup components, keeping a residue entity alive after deletion so a disposal system can release the mesh and material handles a component owns.
A documented update-order dependency graph kept in a comment above the system registration, which is the discipline this ordering model actually needs.
Reading a chain of game-owned link components for display, and the limit that comes with it: some of its tests are disabled because the game's buffer types cannot be JIT-compiled outside the running game.
The corpus's only registration of an entirely new game panel type: two registry `extend` calls that mutate the panel-type enum and the type-to-component map the game's own panel renderer keys on, paired with a C# panel class whose emitted type name is the key.
A transpiler that repairs the developer menu's tool enumeration for every mod in the session, not just its own, and stands down when the patched loop's opcodes show another mod already fixed it.
(UNVERIFIED: the transpiler's own source — it lives in the submodule a plain clone leaves empty, and its shape is read from the repository's change record.)
An editor-toolbar entry carrying nothing but an id and an icon served from its own UI, appended from an editor-mode preload hook behind a one-shot latch — with neither a panel nor a tool set, which the base class's activity test then reports active whenever no editor panel is open, since it compares the active panel against the entry's own null one.

### Hall of Fame

Source: [toverux/HallOfFame](https://github.com/toverux/HallOfFame)

**Does:** Takes supersampled screenshots of the player's city, uploads them to a community server, and presents other players' screenshots as the main menu's background, with the creator, the city name and controls over the image.
It also replaces the loading screen's background with the last image it loaded.

**Demonstrates:** A mod whose product is its frontend, so its C# exists to serve one.
Extending vanilla React components the module registry exposes and no shipped declaration file names — the menu shell, its backdrops, the master screen, the loading-screen overlay, the photo-mode panel — each wrapper returning the vanilla component untouched when the feature is off, which is what makes an injection reversible from a setting.
A registrar split into one function per UI area and composed into a single default export.
One local module per vanilla module it imports, filed under that module's own `game-ui/…` path and resolving the export through the registry behind a type guard and a fallback, so a module no declaration file names still reads as an ordinary typed import at every call site.
Reaching the vanilla DOM from such a wrapper by resolving the wrapped component's scoped CSS-module class names through the module registry and mounting a React portal into the node they find, with every module and class accessor guarded so a renamed class costs a missing button rather than a broken menu.
A frontend binding facade, one module per binding group, whose value bindings are created on first read rather than at import.
Forcing a push for a value the binding layer would deduplicate, with an equality comparer that answers false to everything — the supported hook for a payload whose identity changes on every edit.
Keeping the logger's show-errors-in-UI flag on and treating a logged error as the mod's user-facing error dialog, with a helper that flips the flag off and back around the errors it wants kept out of the player's way.
Two frontend cache techniques the engine forces: an optional sub-object's presence encoded into the type name its payload is written under, and hidden nodes on the document body holding images resident across the screens the game unmounts.
Runtime-written images served to the UI over the mod's own `coui://` host.
A hand-rolled supersampled capture — hide the UI view, force the graphics settings that grain the image, render the camera repeatedly into an oversized texture, and restore every one of those in a `finally`.
Reflection proxies as a declared layer rather than scattered calls, each acquiring its member once, logging what it could not find, and answering with a fallback afterwards.
An input action owned by the frontend: a composite binding publishing the binding configuration and the action's phase, enabling the action while the frontend holds a subscription.
Tests on both sides: xUnit over plain classes kept free of engine types, and UI component tests that load the game's own UI bundle, inject the repository's React into it, and answer binding subscriptions from a mock engine.
The four city-wide facts a mod reads to describe a save, each with the guard its own storage demands: the milestone level as a singleton query that is empty outside a loaded game, the population as a component the city entity may not carry, the city name as a nullable system property, and the map name as a localization key that falls back to the raw save name when the map mod is gone.
Four divergences from the official UI build, each with its reason recorded in the config's own header: the stylesheet-presence export written by hand in the entry module instead of injected by the scaffold's plugin, CSS-module class names prefixed so they are addressable from outside, the game's own image URLs excluded from the stylesheet loader's resolver, and the game's type declarations taken from a versioned dependency instead of a vendored folder.

## Error checks, overrides and save-safe state

### Anarchy

Source: [yenyang/Anarchy](https://github.com/yenyang/Anarchy)

**Does:** Suppresses placement error checks so objects can overlap, sit inside other objects and cross the playable border, adds relative elevation control and an elevation lock, and overhauls network upgrade placement with constant slope and forced ground or elevated modes.

**Demonstrates:** Widening a tool's raycast layer mask from a postfix on its raycast initialization — the smallest useful example of tool interception.
Custom serializable components recording state that must survive a save, paired with query-and-command-buffer systems that strip and restore vanilla components at scale.
Soft integration with another mod through a bridge type located by reflection, with no hard dependency in either direction.
The provider half of that arrangement as well: a static bridge class whose every signature uses only engine and game types, handing out its own component types as runtime component-type values, and letting another mod's tool register itself into a list this mod keeps.
Cloning a vanilla prefab component by component and rebuilding its UI component from scratch rather than copying it, with the clearest comment in this corpus on why a clone must not keep a reference to its source.
Remembering the previously active tool by subscribing to the tool system's tool-changed event rather than latching it at activation, which is the only form that survives an activation the tool did not initiate.
Mimicking a vanilla binding declaratively, so a mod action sits on a button the game reserves and follows the player's rebinds, including the two-property form that mimics an axis.
Suppressing a placement error by setting a disable flag on the error's own prefab rather than by patching the validation system, paired with a restore system in a later phase that runs once and switches itself off.
The corpus's largest definition rewriter, walking a whole run of net-course definitions in order and writing each course's end elevation into the next course's start to force a constant slope.
A change applied inside a modification phase deferred to the next frame through the mod's own tag, because the update marker does not survive being added in the phase that made the change.
Suppressing clearance detection by collapsing the derived prefab field the collision pass reads — the composition height range — rather than patching validation, with the original recorded on the composition entity and a companion system restoring it.
The reference copy of the community's vanilla-component resolver, the lazy name-to-path map many UI mods share for pulling unexported game components out of the module registry, carrying the discovery workflow in its own comments.

### Better Bulldozer

Source: [yenyang/BetterBulldozer](https://github.com/yenyang/BetterBulldozer)

**Does:** Adds exclusive-target bulldozer modes for invisible paths, markers, surfaces and lane fences, a mode for removing vehicles and citizens, and a sub-element mode that strips individual props, trees and decals from otherwise protected assets, with automatic cleanup options.

**Demonstrates:** Two complementary raycast techniques — rewriting the type, layer, collision and area masks per mode before the cast, and vetoing a hit afterwards from a prefix on the result getter.
A secondary tool that masquerades as the bulldozer by delegating its prefab methods to the vanilla one, so it needs no toolbar entry of its own.
Serializable records that make a destructive edit reversible.
The reason "permanent" removal needs records at all: the game's own systems keep recreating sub-elements from the asset definition, so the mod re-detects and re-deletes them rather than deleting once.
Reinserting its two tools at the front of the tool list once loading has completed, which is later than the tool's own creation and therefore wins any race with a mod that reorders at `OnCreate`.
Detecting another mod's per-entity state by reflected component type — on the entity itself or on its edge's endpoint nodes — and re-marking the hits for update a frame later.
A removal tool splitting one target set — any of the vehicle, animal and human tags, minus deleted and temporary entities — into moving and parked query halves by the presence of the interpolated transform, so one tool covers both without a per-entity branch.

## Inspection and debugging tooling

### Scene Explorer

Source: [krzychu124/SceneExplorer](https://github.com/krzychu124/SceneExplorer)

**Does:** Inspects a clicked entity in its own windows over the running game, listing every component it carries with the field values live-refreshed while the simulation runs, alongside a search over every registered component type, a configurable entity query, and editor-only snapshots of the entities a query returned.
It writes no simulation values; what it does change is the vanilla highlight on the entity under the cursor, which it removes again, plus bookkeeping entities of its own.

**Demonstrates:** Reading a component whose type is not known until runtime, by caching `MakeGenericMethod` results over the entity manager's own generic getters and classifying each `ComponentType` into unmanaged, managed, shared, buffer, tag and enableable before rendering it, which is what a mod needs when it must handle types it cannot name at compile time.
Building an entity query at runtime from the type manager's registry, with all-any-none sets staged through pending add and remove collections so a UI callback never mutates a live set mid-layout.
The runtime query taken off the entity manager through the allocator-backed builder rather than off a system — the caller-owned form, disposed after each read, which is what keeps a per-user-choice query from accumulating in a system's query cache for the world's lifetime.
The vanilla selection glow on an arbitrary entity, by adding the game's own highlight component paired with the batch-update component that forces the re-render — the second half is what makes the first take effect.
Driving both the overlay buffer and the gizmo batcher from Burst jobs, with the writer registration the gizmo side requires.
Passing UI intent to renderer systems through ECS components rather than object references: each window owns an entity of a published archetype and writes its hover target into it, and the rendering system rebuilds its map from whichever of those entities changed.
Distinguishing a structural change from a value change on every refresh — comparing the entity's component-type list against the cached one, rebuilding the whole model when it differs and re-reading values when it does not.
A logging facade whose three category methods each carry a conditional-compilation attribute, so a build without the symbol drops the call and the message it would have built, behind a static constructor that suppresses the logger's UI errors as it creates it.
Registering an editor tool by growing the editor UI system's tool array and assigning it back through the property, whose setter rebuilds the parallel disabled-state cache the update loop indexes in lockstep with the array — paired with a panel on the game's editor-panel base, a unique tool id, and a locale entry under the editor's tool-tooltip key namespace.
Snapshotting entities into memory by transitively following entity-typed fields, holding the two prefab references back so the walk does not drag the whole prefab graph in with them, and rendering live and frozen data through one code path.
Walking a game-owned index-and-buffer chain to render it as gizmos — a segment buffer stepped between two index components, wrapping at the end, recursing into each segment's own path buffer.
No patching anywhere in the codebase.
The unlock graph rendered without being walked: the unlock component shown as its two requirement-array lengths, and the generic descent depth-clamped whenever the section being rendered is that component, because both arrays are prefab references into the dev tree.
Its helper and service classes are where all of the above is legible; its two thousand-line tool and highlight systems carry near-duplicate dispatch ladders and repay reading only for the specific mechanism you came for.
