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
Its two runtime patches are small and targeted, applied only to refresh a private UI binding.

### Move It

Source: [yenyang/CS2-MoveIt](https://github.com/yenyang/CS2-MoveIt)

Clone an alpha branch rather than the default one: the architectural changes worth reading are there.
`git ls-remote --heads https://github.com/yenyang/CS2-MoveIt.git 'Alpha*'` lists them, and the newest goes to `git clone --branch`.

**Does:** Selects, moves, rotates, copies, and deletes anything already placed — trees, props, decals, buildings, network nodes, and segment curves — with marquee selection, a manipulation mode for dragging segment control points, and alignment helpers.

**Demonstrates:** The fullest tool lifecycle in the corpus, built on the game's object tool base and split across partial classes by concern (lifecycle, update, jobs, filtering).
Typed raycast wrappers registered with the game's own raycast system.
Overlay rendering dispatched per entity kind, with the draw and update passes as jobs.
Cross-mod integration by reflection over loaded assemblies rather than a compile-time reference, so a missing sibling mod costs nothing.
Emitting the game's own placement definitions by hand for every selected entity and each of its sub-nets and sub-areas, switching one flag field between relocate, delete, hidden and recreate, rather than using the definition helper its base class offers.

### Area Bucket

Source: [Cmyna/AreaBucket](https://github.com/Cmyna/AreaBucket)

**Does:** Fills an enclosed region with a surface or area in one click, flood-filling outward from the click point against roads, lots, existing areas and lanes as boundaries, instead of tracing the outline by hand.

**Demonstrates:** A whole geometry algorithm expressed as a chain of Burst jobs — collect boundaries, generate rays, drop intersected and obscured ones, merge into polylines, emit an area definition.
Terrain-only raycasting by narrowing the raycast type mask.
A preview entity path that renders an outline before anything is committed.
Its own native containers and a line-sweep intersection pass, which is what reading it costs and what it teaches.
Each job owns its component and buffer lookups behind an assign-once and a refresh-per-update method, which is the clearest hand-written form of the type-handle discipline the source generator otherwise supplies.
No runtime patching anywhere.

### Network Tools

Source: [lucarager/CS2-NetworkTools](https://github.com/lucarager/CS2-NetworkTools)

**Does:** Freehand network editing beyond the road tools — adding and removing nodes, sliding and dragging them, connecting distant nodes with generated curves, offsetting a selection into parallel networks, and generating grids and circles of segments.

**Demonstrates:** A shared tool base class carrying raycast filtering, eligibility marking and handle lifecycle for a family of tools.
Resolving an edge raycast hit down to the nearest node.
One output-mode switch that decides whether a job writes preview definitions or mutates the network directly on apply.
Interactive 3D handles as their own drawable, cullable entities.
A source generator that emits the TypeScript binding declarations from the C# side, which is the only answer in this corpus to C#-and-frontend drift.
A watchdog that re-runs the game's own deserialize-time graph verification at runtime, so an edit cannot quietly bake a corrupt graph into a save.

### Node Controller

Source: [bruceyboy24804/NodeController](https://github.com/bruceyboy24804/NodeController)

**Does:** Per-node control over how roads meet: node style, shift, twist and slope, dragging or rotating individual segment ends and corners, crosswalk overrides, and underground editing for tunnels.

**Demonstrates:** Disabling the game's geometry system outright and scheduling a replacement in the same slot — the clearest example of substitution over patching, and it ships no patches at all.
Per-node settings as a versioned serializable component, so the edits survive a save.
A tool built as a state machine of interaction modes, one class per gesture.
Its own spatial search rather than the tool raycast, picking segment ends by mouse ray and camera field of view.
Registering scroll-wheel input actions the input API does not expose, by reflecting into the input manager.

### Extra Detailing Tools

Source: [AlphaGaming7780/ExtraDetailingTools](https://github.com/AlphaGaming7780/ExtraDetailingTools)

**Does:** A precision transform tool with per-axis numeric input and configurable increments, a menu exposing net lanes, surfaces and decals for detailing, and the editor's snap-to-surface behaviour brought into the main game so objects snap to rooftops and walls.

**Demonstrates:** Extending vanilla tool behaviour by patching the two methods that decide rotation and snap masks, rather than replacing the tool.
A reusable generic base for adding custom snap modes to any tool.
A batched custom raycast where several callers share one pass per frame, keyed by context.
Runtime bridging to another mod through a dedicated bridge class.
Declaring its own input usage string beside the built-in ones, so its transform-tool actions are not reported as conflicting with vanilla bindings they share keys with.
A port of the game's own selection-definition builder that branches on what the selected entity is and emits the matching definition kind for each — network course, object, area nodes, route waypoints, notification icon, aggregate elements — which is the widest coverage of that mechanism outside the game itself.

### Platter

Source: [lucarager/CS2-Platter](https://github.com/lucarager/CS2-Platter)

**Does:** Adds placeable "parcels" holding zone cells, so zoning can be placed anywhere and at any angle with snapping and setbacks, while the game's own demand, land value and growth logic still decides what spawns on them.

**Demonstrates:** Custom prefab subclasses and runtime prefab variant generation.
Deserialize-phase jobs that rebuild the links between mod entities and game entities after a load.
Rewriting a vanilla system's private entity query at runtime so stock code skips the mod's entities — the most aggressive compatibility technique in the corpus, and worth reading as much for its risk as for its power.
Prefab-data initialisation systems anchored immediately after the vanilla initializer whose output they overwrite, which is the readable way to correct derived prefab data rather than fight for it.
Hand-built input actions registered by reflection, because scroll bindings are not exposed.
Taking a placement definition away from its vanilla consumer by removing the creation component from a system spliced immediately before that consumer, played back synchronously because a phase barrier would land after the consumer has already run.
An in-engine test scenario system, debug-only.
A build-time export of its English string table into the repository, locating the destination from the compiler's caller-file-path rather than a hard-coded developer directory.

### Traffic

Source: [krzychu124/Traffic](https://github.com/krzychu124/Traffic)

**Does:** A lane connector for rewiring turning lanes at an intersection and a priorities tool for yield and stop rules, with modifier keys selecting unsafe, track-only, road-only or shared connections.

**Demonstrates:** The reference example of save-data migration: a version constant, per-version repair jobs, and validation passes that fix or drop data an older version wrote.
Disabling the vanilla lane system and taking over its slot, with no patching anywhere in the codebase.
Registering a system frames after the mod loaded, from a deferred main-thread callback, which is how it disables another mod's system once that mod is known to be present.
A custom raycast system registered in the raycast phase.
Rebindable actions declared as settings attributes and consumed by the tool, scoped with its own usage strings beside the built-in ones.
Letting the player choose, from the settings screen, between the mod's own bindings and watchers that keep them equal to the vanilla apply and cancel bindings.
Migrating another mod's saved data on load, and detecting an incompatible build of it by scanning loaded assemblies.
Its own placement-definition component beside the game's, with a generator, a validator, a clear system and an apply system each spliced next to the vanilla one it parallels — the corpus's only extension of that protocol to a new kind.
An empty prefab class with no content, registered in the pre-deserialize hook, whose only job is to give the mod's own entities a prefab reference the game's load-time reference remapping can resolve.
Its own language dropdown independent of the game's, registering the chosen translation under whatever locale the game is currently set to and re-applying the swap whenever the player changes language.

## Replacing a vanilla system instead of patching it

### Plop the Growables

Source: [algernon-A/PlopTheGrowables](https://github.com/algernon-A/PlopTheGrowables)

**Does:** Lets growable buildings be placed anywhere and stops them being condemned when the zoning under them is wrong, missing or later changed, optionally keeping the requirement for buildings that grew naturally.

**Demonstrates:** The cleanest small example of the substitution pattern — disable the zone check system, run a fork of its exact job pipeline that special-cases the mod's own entities.
Empty tag components using the engine's own serialization, so per-entity mod state rides along with the entity and needs no save section of its own.
A deserialize-phase system that backfills entities from saves written before the mod existed.
Reusing the game's existing "historical" flag rather than inventing a parallel lock.
Translations as embedded per-locale CSV with its own quote-aware reader, settings keys written as a short packed prefix the loader expands into the long generated key, so a translator edits a two-column spreadsheet.

### Realistic Trips

Source: [ruzbeh0/Time2Work](https://github.com/ruzbeh0/Time2Work)

**Does:** Rewrites when and why citizens travel — rush hours, quiet nights, lunch breaks, weekday and weekend patterns, shift work, remote work and school attendance — with presets and an optional seven-day week and slowed clock.

**Demonstrates:** Substitution at the largest scale here: roughly a dozen vanilla simulation systems disabled and replaced.
Reimplementing the game's time model, deriving ticks per day from the vanilla constant scaled by a factor, which is where to look for how the simulation's time units actually work.
The corpus's only override of a system's update offset, copied from the vanilla system it forks along with the interval, which is what puts a fork on the same simulation frames as the original.
Burst-compiled per-citizen work with a per-citizen deterministic random stream.
A versioned serializable component for per-citizen state, with a fallback path that reads saves written by older versions.
Carries the game's own generated type-handle struct into each fork and refreshes every handle by hand at the top of the update, which is what a forked system must do once the source generator is no longer writing that code for it.
Runtime detection of sibling mods by name, including keeping a dead system registered purely so old saves still load.

## Prefabs and assets from code

### Road Builder

Source: [JadHajjar/RoadBuilder-CSII](https://github.com/JadHajjar/RoadBuilder-CSII)

**Does:** Builds custom roads in-game by combining lanes, medians and sidewalks in a drag-and-drop panel, then places them or retroactively updates every placed instance.
Custom roads are saved inside the city and travel with it.

**Demonstrates:** Composing a network prefab in code — instantiating the prefab, assembling its sections, edge and node states from lane-group prefabs, rather than cloning a fixed template.
Versioned custom save serialization with migration constants and post-load repair of invalid references.
Mimicking the built-in apply and cancel bindings by copying them from the input manager, so a custom tool obeys the user's remaps.
Registering roughly fifteen systems across specific phases, which is a readable map of where prefab, serialize and tool work has to sit.
Regenerating a live prefab in place, which replaces the prefab entity outright, so the mod tags the outgoing one, throttles the rebuild, and clears the stale entity out of a vanilla system's private dictionary that the engine's own reference sweep cannot reach.
A null-checking wrapper around the generic prefab lookup, which otherwise reports success while handing back null whenever the requested type does not match.
A dictionary source registered under every supported locale whose entries are generated on each read, so names for roads the player builds at runtime localize without re-registering anything.

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

**Demonstrates:** The save-safety pattern worth copying — a before-serialize system that collapses the mod's custom state back into vanilla fields, so the save loads correctly for someone who removes the mod, with the restoring half registered behind the writer so the running session keeps its state.
Registering the same systems into three phases, so one implementation serves the simulation, the editor and the save pipeline.
Registering custom prefabs at load, gated by whether the game is in game or editor mode, from a preload hook on a system it creates but never gives a phase — the timing answer for prefab work that has to happen before a game loads and after the asset database is populated.
One tool serving both game and editor by branching on the tool system's action mode.
Burst jobs that tag vanilla simulation entities with the mod's own components through a command buffer.

### Tree Controller

Source: [yenyang/Tree_Controller](https://github.com/yenyang/Tree_Controller)

**Does:** Changes the age, type and colour of existing trees and bushes by single tree, building, radius or whole map, plops trees at a chosen age, and offers curated forest brushes, paused tree growth and a winter dead-model look.

**Demonstrates:** Rewriting object definitions after the tool creates them and before the game consumes them — the interception point for changing what a tool places without touching the tool.
Modifying existing prefabs at load, adding a missing component and zeroing a cost field.
Restoring the original value by reading it back off the authoring prefab object rather than off the prefab entity it overwrote, which is the corpus's only worked example of treating the authoring layer as the vanilla baseline.
A "safely remove" system that resets custom model state on demand, because some of this state is not safe to leave in a save.
Extending brush strength past the vanilla cap with a single targeted patch.

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
Naming prefabs it generates at runtime by copying the original's localized name out of the active dictionary and writing a fallback straight back into it, which is the fast path and is lost the moment the player changes language.

### Info Loom

Source: [bruceyboy24804/InfoLoom](https://github.com/bruceyboy24804/InfoLoom)

**Does:** Adds panels for demand, workforce and workplace structure, demographics, residential, commercial and industrial data, trade costs and districts, and injects extra sections into the game's selected-object panel.
Read-only; it changes no rules.

**Demonstrates:** The two module-registry calls a UI mod needs — appending a panel to a named region, and extending the selected-info section list with its own sections.
Reading simulation state cheaply: a job gated on whether the panel is visible, with an update interval so the query runs only every few hundred ticks — on its simulation-phase systems, since an interval on a UI-phase system does nothing, and this source carries examples of both.
Replacing the game's own JSON binding output by returning false from a prefix, when the vanilla writer truncates what the panel needs.
The corpus's only source-generated per-entity job, which is the proof that the Entities source generators the official toolchain ships do work in a mod project.

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
Player-authored translations: the player names and translates their own palettes in-game, each locale written to a JSON file beside the palette and registered as an in-memory source, guarded by a check that the game supports that locale at all.

### Write Everywhere

Source: [klyte45/CS2-WriteEverywhere](https://github.com/klyte45/CS2-WriteEverywhere)

**Does:** Attaches custom text and images to buildings, props and vehicles, driven by properties of the target object, with reusable layouts, custom fonts, image atlases and imported meshes.

**Demonstrates:** Rendering that leaves the entity renderer entirely — hooking the render pipeline's context callback and issuing draw calls per element with hand-built matrices.
Burst-compiled glyph layout writing vertices into a native queue, on top of a hand-written font file parser.
Loading meshes from disk at runtime and composing per-entity materials.
Attaching mod data to game entities by owner back-reference instead of spawning a parallel object graph.
Versioned serialization with an explicit migration scheme.
The corpus's only cleanup components, keeping a residue entity alive after deletion so a disposal system can release the mesh and material handles a component owns.
A documented update-order dependency graph kept in a comment above the system registration, which is the discipline this ordering model actually needs.

## Error checks, overrides and save-safe state

### Anarchy

Source: [yenyang/Anarchy](https://github.com/yenyang/Anarchy)

**Does:** Suppresses placement error checks so objects can overlap, sit inside other objects and cross the playable border, adds relative elevation control and an elevation lock, and overhauls network upgrade placement with constant slope and forced ground or elevated modes.

**Demonstrates:** Widening a tool's raycast layer mask from a postfix on its raycast initialization — the smallest useful example of tool interception.
Custom serializable components recording state that must survive a save, paired with query-and-command-buffer systems that strip and restore vanilla components at scale.
Soft integration with another mod through a bridge type located by reflection, with no hard dependency in either direction.
Cloning a vanilla prefab component by component and rebuilding its UI component from scratch rather than copying it, with the clearest comment in this corpus on why a clone must not keep a reference to its source.
Remembering the previously active tool by subscribing to the tool system's tool-changed event rather than latching it at activation, which is the only form that survives an activation the tool did not initiate.
Mimicking a vanilla binding declaratively, so a mod action sits on a button the game reserves and follows the player's rebinds, including the two-property form that mimics an axis.
Suppressing a placement error by setting a disable flag on the error's own prefab rather than by patching the validation system, paired with a restore system in a later phase that runs once and switches itself off.
The corpus's largest definition rewriter, walking a whole run of net-course definitions in order and writing each course's end elevation into the next course's start to force a constant slope.

### Better Bulldozer

Source: [yenyang/BetterBulldozer](https://github.com/yenyang/BetterBulldozer)

**Does:** Adds exclusive-target bulldozer modes for invisible paths, markers, surfaces and lane fences, a mode for removing vehicles and citizens, and a sub-element mode that strips individual props, trees and decals from otherwise protected assets, with automatic cleanup options.

**Demonstrates:** Two complementary raycast techniques — rewriting the type, layer, collision and area masks per mode before the cast, and vetoing a hit afterwards from a prefix on the result getter.
A secondary tool that masquerades as the bulldozer by delegating its prefab methods to the vanilla one, so it needs no toolbar entry of its own.
Serializable records that make a destructive edit reversible.
The reason "permanent" removal needs records at all: the game's own systems keep recreating sub-elements from the asset definition, so the mod re-detects and re-deletes them rather than deleting once.
Reinserting its two tools at the front of the tool list once loading has completed, which is later than the tool's own creation and therefore wins any race with a mod that reorders at `OnCreate`.

## Inspection and debugging tooling

### Scene Explorer

Source: [krzychu124/SceneExplorer](https://github.com/krzychu124/SceneExplorer)

**Does:** Inspects a clicked entity in its own windows over the running game, listing every component it carries with the field values live-refreshed while the simulation runs, alongside a search over every registered component type, a configurable entity query, and editor-only snapshots of the entities a query returned.
It writes no simulation values; what it does change is the vanilla highlight on the entity under the cursor, which it removes again, plus bookkeeping entities of its own.

**Demonstrates:** Reading a component whose type is not known until runtime, by caching `MakeGenericMethod` results over the entity manager's own generic getters and classifying each `ComponentType` into unmanaged, managed, shared, buffer, tag and enableable before rendering it, which is what a mod needs when it must handle types it cannot name at compile time.
Building an entity query at runtime from the type manager's registry, with all-any-none sets staged through pending add and remove collections so a UI callback never mutates a live set mid-layout.
The vanilla selection glow on an arbitrary entity, by adding the game's own highlight component paired with the batch-update component that forces the re-render — the second half is what makes the first take effect.
Driving both the overlay buffer and the gizmo batcher from Burst jobs, with the writer registration the gizmo side requires.
Passing UI intent to renderer systems through ECS components rather than object references: each window owns an entity of a published archetype and writes its hover target into it, and the rendering system rebuilds its map from whichever of those entities changed.
Distinguishing a structural change from a value change on every refresh — comparing the entity's component-type list against the cached one, rebuilding the whole model when it differs and re-reading values when it does not.
Registering an editor tool by growing the editor's own tool array.
Snapshotting entities into memory by transitively following entity-typed fields, holding the two prefab references back so the walk does not drag the whole prefab graph in with them, and rendering live and frozen data through one code path.
No patching anywhere in the codebase.
Its helper and service classes are where all of the above is legible; its two thousand-line tool and highlight systems carry near-duplicate dispatch ladders and repay reading only for the specific mechanism you came for.
