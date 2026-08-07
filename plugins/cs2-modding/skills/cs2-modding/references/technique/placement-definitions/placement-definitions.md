# Placement definitions

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

The seam between a tool and the world.
A tool never mutates the city: it creates an entity describing what should happen — a **definition** — and a generation system in a later phase of the same frame does the work.
Everything the placed thing ends up carrying is derived from that definition plus the prefab entity it names, with nothing carried over from the tool, which is why rewriting a definition in flight is enough to change what the game builds.

`custom-tools` owns the tool side — the base classes, the tool list, the raycast, `ApplyMode`, snapping, overlays, tooltips and the input actions — and this reference owns what a tool emits once it has decided, and what happens to that emission afterwards.
`mod-lifecycle-and-ordering` owns which phase and which band a system lands in, and every registration below is a decision that reference settles.
`ecs-in-this-game` owns the frame-scoped tags and the barrier playback points this mechanism rides on.
`prefabs-and-assets` owns the prefab layers a definition points into.

## A definition is a request, and it lives for exactly one frame

The whole seam is one component.
`CreationDefinition : IComponentData, IQueryTypeParameter` carries seven fields and nothing else:

| Field                   | What it names                                                                                                                                     |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Entity m_Prefab`       | The prefab **entity** to instantiate; null on a definition that only acts on an existing entity                                                   |
| `Entity m_SubPrefab`    | A second prefab entity beside it, which the vanilla object producer fills from the tool's transform prefab and a producer without one leaves null |
| `Entity m_Original`     | The existing entity this definition acts on — delete, select, relocate, upgrade, duplicate                                                        |
| `Entity m_Owner`        | An existing owner for the thing created, used when the owner is already in the world                                                              |
| `Entity m_Attached`     | The entity to attach to, read when `CreationFlags.Attach` is set                                                                                  |
| `CreationFlags m_Flags` | What kind of act this is; see below                                                                                                               |
| `int m_RandomSeed`      | Becomes the created entity's permanent variation seed                                                                                             |

It is a request rather than a result: the entity carrying it describes something the game should create, delete, move or select, and a later system creates a **separate** entity to satisfy it.

The fact a definition author gets wrong first is that `m_Prefab` and `m_SubPrefab` are prefab **entities**, not `PrefabBase` objects, and the archetype the resulting instance gets is `ObjectData.m_Archetype` on that prefab entity — `prefabs-and-assets` owns both layers.

**The minimal definition is three components and no archetype.**
Every producer in the game builds it the same way, through a command buffer:

```csharp
Entity definition = commandBuffer.CreateEntity();

commandBuffer.AddComponent(definition, new CreationDefinition
{
    m_Prefab = prefabEntity,
    m_RandomSeed = seed,
});
commandBuffer.AddComponent(definition, new ObjectDefinition { /* ... */ });
commandBuffer.AddComponent(definition, default(Updated));
```

No tool anywhere in the game declares a definition archetype; the only two `CreateArchetype` calls for a definition are in the simulation, and they exist for a reason covered below.

**`Updated` is load-bearing and not decoration.**
Every consumer's query requires it, and the game's cleanup system strips it from every tagged entity in the `Cleanup` phase at the end of the frame, as it does every frame-scoped tag — `ecs-in-this-game` owns that protocol and its timing.
So a definition is visible to its consumer for exactly the frame it was created in, and a definition emitted without `Updated` is invisible to every consumer while still matching the sweep that destroys stale definitions.

## The kind component decides the consumer, and there are nine of them

A `CreationDefinition` alone does nothing.
What gets built is decided by the **second** component on the definition entity, and each kind is claimed by a fixed set of consumers — one apiece, except the two kinds whose work spans two modification phases:

| Kind component              | Consumer                      | Phase           |
| --------------------------- | ----------------------------- | --------------- |
| `ObjectDefinition`          | `GenerateObjectsSystem`       | `Modification1` |
| `NetCourse`                 | `GenerateNodesSystem`         | `Modification1` |
| `NetCourse`                 | `GenerateEdgesSystem`         | `Modification2` |
| `Zoning`                    | `GenerateZonesSystem`         | `Modification1` |
| `Game.Areas.Node` buffer    | `GenerateAreasSystem`         | `Modification1` |
| `WaypointDefinition` buffer | `GenerateWaypointsSystem`     | `Modification1` |
| `WaypointDefinition` buffer | `GenerateRoutesSystem`        | `Modification2` |
| `IconDefinition`            | `GenerateNotificationsSystem` | `Modification1` |
| `BrushDefinition`           | `GenerateBrushesSystem`       | `Modification1` |
| `AggregateElement` buffer   | `GenerateAggregatesSystem`    | `Modification1` |
| `WaterSourceDefinition`     | `GenerateWaterSourcesSystem`  | `Modification1` |

Every one of those queries is `{CreationDefinition, <kind>, Updated}`, some written with the kind in an `Any` clause so that one system can claim two kinds: the object generator matches `Any = {ObjectDefinition, NetCourse}`, and the node generator matches the same pair.

Two more components ride along rather than selecting a consumer.
`OwnerDefinition { Entity m_Prefab; float3 m_Position; quaternion m_Rotation; }` names an owner that **does not exist yet**: a sub-object of a building being placed points at the building's own definition rather than at an entity, which is what lets a whole composite be described before any of it is created.
`ColorDefinition { Color32 m_Color; }` is written by the route tool and read by the route generator, and colours a transport line.

**`NetCourse` is the richest kind**: two `CoursePos` endpoints plus `Bezier4x3 m_Curve`, `float2 m_Elevation`, `float m_Length` and `int m_FixedIndex`.
`CoursePos` carries `m_Entity`, `m_Position`, `m_Rotation`, `m_Elevation`, `m_CourseDelta`, `m_SplitPosition`, `CoursePosFlags m_Flags` and `m_ParentMesh`.
`CoursePosFlags : uint` has fifteen members — `IsFirst = 1`, `IsLast = 2`, `HalfAlign = 4`, `IsParallel = 8`, `IsRight = 0x10`, `IsLeft = 0x20`, `IsFixed = 0x40`, `FreeHeight = 0x80`, `LeftTransition = 0x100`, `RightTransition = 0x200`, `ForceElevatedNode = 0x400`, `ForceElevatedEdge = 0x800`, `DisableMerge = 0x1000`, `IsGrid = 0x2000`, `DontCreate = 0x4000`.

**`ObjectDefinition` has thirteen fields**: `m_Position`, `m_LocalPosition`, `m_Scale`, `m_Rotation`, `m_LocalRotation`, `m_Elevation`, `m_Intensity`, `m_Age`, `m_IsDecoration`, `m_ParentMesh`, `m_GroupIndex`, `m_Probability`, `m_PrefabSubIndex`.
A hand-built one has to reproduce the non-zero defaults the vanilla producer seeds for a free-standing object: `m_Probability = 100`, `m_PrefabSubIndex = -1`, `m_Scale = 1f`, `m_Intensity = 1f`, `m_ParentMesh = -1`.
A zeroed `m_Scale` places an object of size zero, and a zeroed `m_Probability` places nothing at all.

`ZoningFlags : uint` — `FloodFill = 1`, `Marquee = 2`, `Zone = 4`, `Dezone = 8`, `Paint = 0x10`, `Overwrite = 0x20`.

(VOLATILE: the `CreationDefinition` field set, the kind component types and their own members, and the phase each consumer is registered at — the tools namespace, and the vanilla system-order class.)

## `CreationFlags`, and the one flag that changes everything

`CreationFlags : uint` has twenty members: `Permanent = 1`, `Select = 2`, `Delete = 4`, `Attach = 8`, `Upgrade = 0x10`, `Relocate = 0x20`, `Invert = 0x40`, `Align = 0x80`, `Hidden = 0x100`, `Parent = 0x200`, `Dragging = 0x400`, `Recreate = 0x800`, `Optional = 0x1000`, `Lowered = 0x2000`, `Native = 0x4000`, `Construction = 0x8000`, `SubElevation = 0x10000`, `Duplicate = 0x20000`, `Repair = 0x40000`, `Stamping = 0x80000`.

**`Permanent` decides whether the definition produces a preview at all.**
The consumer builds a `Temp` component for the entity it is about to create and attaches it only when the flag is absent.
A definition without `Permanent` becomes a `Temp` preview waiting for `ApplyTool`; a definition **with** `Permanent` becomes a real, committed entity in the same pass, with no preview, no validation icon and no apply step.
That is the flag the simulation uses, and a tool that sets it has skipped the entire tool lifecycle its user expects.

The rest of the flags are translated by the consumer into `TempFlags` and into components on the created entity:

| `CreationFlags`                                   | Effect on the created entity                                                                                           |
| ------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `Delete`                                          | `TempFlags.Delete`, plus a refund computed from the original's `Recent` component                                      |
| `Select`                                          | `TempFlags.Select`; with `Dragging`, also `TempFlags.Dragging`                                                         |
| `Relocate`                                        | `TempFlags.Modify`, plus a relocation cost when the transform actually moved                                           |
| `Upgrade`                                         | `TempFlags.Upgrade`, plus an upgrade cost diffed against the original prefab's cost                                    |
| `Duplicate`                                       | `TempFlags.Duplicate`                                                                                                  |
| `Repair`                                          | A rebuild cost when the original carries `Destroyed`, and suppresses copying `Damaged`/`Destroyed` onto the new entity |
| `Parent`                                          | `TempFlags.Parent`                                                                                                     |
| `Optional`                                        | `TempFlags.Optional` on a create                                                                                       |
| `Lowered`                                         | `ElevationFlags.Lowered`                                                                                               |
| `Attach`                                          | An `Attached` component built from `m_Attached`                                                                        |
| `Native`                                          | A `Native` component                                                                                                   |
| `Construction`                                    | Routes the new building through the under-construction path                                                            |
| Neither `Delete` nor `Select` nor an `m_Original` | `TempFlags.Create`, and a cost equal to the construction cost                                                          |

**`m_RandomSeed` outlives the definition.**
The consumer writes `new PseudoRandomSeed((ushort)definition.m_RandomSeed)` onto the created entity whenever the original had none, so the definition's seed becomes that entity's permanent variation seed.
Rewriting `m_RandomSeed` therefore changes what the placed thing looks like forever, not just for this frame — and reading it back is how a rewriter that substitutes prefabs keeps its own choice stable across the frames a preview survives.

(VOLATILE: the `CreationFlags` member set and its mapping onto `TempFlags`, and the component set a consumer stacks onto the entity it creates — the creation-flags enum, and the object generation system.)

## What the consumer builds, and what becomes of the original

The consumer does not modify the definition; it creates or revives a separate entity and leaves the definition untouched.

On the **create** path it spawns an entity from the prefab's archetype and stacks on it: the `Temp` built from the flags, a `Transform` from `ObjectDefinition.m_Position` and `m_Rotation`, an `Elevation` when the definition asks for one, a `PseudoRandomSeed` from `m_RandomSeed`, an `Attached` when `CreationFlags.Attach` is set, a `Surface` for a physical geometry, and `Damaged`/`Destroyed` copied off the original unless `Repair` is set.
On the **revive** path — where the previous frame's preview entity is reused rather than respawned — it removes `Deleted`, adds `Updated` and overwrites the same components in place.

**The original is hidden rather than removed.**
When `m_Original` is non-null, the first thing the consumer does is add `Hidden` and `BatchesUpdated` to it.
`Hidden` is a zero-size tag; the apply systems remove it on commit and the clear system removes it on discard, which `custom-tools` records from the tool side.
So the visual illusion of moving something is a hidden original standing behind a `Temp` that stands in front of it, and both halves are decided by one `Entity` field on the definition.

## The window: emitted at `ToolUpdate`, consumed at `Modification1`

Both drivers sit in `MainLoop`, the tool system first and the modification system a few registrations later, so one frame runs:

```
MainLoop
  ToolSystem            PreTool     OriginalDeletedSystem
                        ToolUpdate  the eleven tools -> definitions emitted
                                    ToolOutputSystem -> ClearTool | ApplyTool
                                    ToolOutputBarrier playback
                        PostTool    ToolFeedbackSystem, SelectedUpdateSystem,
                                    CourseSplitSystem, ...
                                    ToolReadyBarrier playback
  ModificationSystem    Modification1   Generate{Objects,Nodes,Zones,Areas,Waypoints,
                                        Notifications,Brushes,Aggregates,WaterSources}
                        Modification2   GenerateEdgesSystem, GenerateRoutesSystem
                        ...
                        ModificationEnd ValidationSystem, then ValidationSystem.Components
Cleanup                 CleanUpSystem -- strips Updated, destroys Deleted
LateUpdate              SimulationSystem -> GameSimulation
```

The rewrite window is everything between the `ToolOutputBarrier` playback and the consumer.

**Put a definition rewriter in the front band of `Modification1`**, registered `UpdateBefore<MySystem>(SystemUpdatePhase.Modification1)`, which lands after the phase's own barrier and ahead of every vanilla `UpdateAt` in it.
Where the rewrite concerns one kind only, splice it against that kind's consumer instead — `UpdateBefore<MySystem, GenerateZonesSystem>(SystemUpdatePhase.Modification1)` — which survives a vanilla reordering inside the phase.
A rewriter that needs the vanilla `Temp` entities to already exist goes in a later modification phase, and reads them rather than the definitions.

**The band matters more than the phase, and this is the mistake that costs a day.**
`UpdateAt<MySystem>(SystemUpdatePhase.Modification1)` places the system after every vanilla `UpdateAt` in that phase, which means after `GenerateObjectsSystem` has already consumed everything.
The system runs, the query matches, the writes land, and nothing changes — `mod-lifecycle-and-ordering` owns the banding rule that explains why.

**Write synchronously.**
`ModificationBarrier1` is registered `UpdateAfter` the phase, so it plays back at the **end** of `Modification1`, after every consumer has read.
A rewrite queued into that barrier lands after the thing it meant to change was already built, so the write goes through `EntityManager` directly, or through an `EntityCommandBuffer(Allocator.Temp)` the system allocates, fills and plays back itself before returning.
This is a hard requirement of the window rather than a stylistic preference, and it is the single fact a definition-rewriting mod most needs.

**`PostTool` is the window the game's own definition rewriter uses.**
The vanilla course-splitting system queries `{CreationDefinition, NetCourse, Updated}` at `PostTool` and rewrites those definitions through `ToolReadyBarrier`, splitting one drawn course wherever it crosses an existing node; at over four thousand lines it is the largest piece of definition-rewriting code in the game.
The property that makes the window worth knowing is that `ToolReadyBarrier` plays back **before** the modification phases, so a rewrite queued into it lands in time — which means the rewrite can be a scheduled job rather than a main-thread loop, the one thing the `Modification1` window cannot offer.
So a rewriter heavy enough to want a job goes here, and `Modification1` stays the default for everything else.
(UNVERIFIED: whether a mod system registered at `PostTool` sees the frame's definitions at all — registering one in a running game and watching its query match would settle it, and until somebody has, this window is architecture rather than a walked path.)

(VOLATILE: that the `PostTool` window is still open — the game's own course-splitting system, and the tool-ready barrier's phase registration.)

## The query that catches a definition in flight

```csharp
m_DefinitionQuery = SystemAPI.QueryBuilder()
    .WithAllRW<ObjectDefinition>()
    .WithAll<CreationDefinition, Updated>()
    .WithNone<Deleted, Overridden>()
    .Build();
```

Swap the read-write kind for whichever one you mean, take `CreationDefinition` read-write as well when you intend to change the prefab or the flags, and finish with `RequireForUpdate(query)` so the system costs nothing on the frames no tool is drawing.

**The two exclusions are not equivalent, and only one of them is a filter.**

`Deleted` **is** reachable on a definition entity.
The zone-spawn and area-spawn systems build their definitions from an archetype that includes `Deleted` at birth, and the vanilla consumers do not exclude it, so those definitions are consumed normally and then destroyed by the frame's cleanup with no sweep needed.
Excluding `Deleted` is therefore a real decision: it leaves the simulation's own placements alone and confines the rewrite to what a tool is drawing, which is almost always what a mod wants.

`Overridden` is **not** reachable on a definition entity.
Eight sites across the tools, net, objects and areas namespaces add it, and every one of them targets a real instance or a `Temp` clone rather than a definition.
The exclusion is inert; it is worth keeping only as documentation that the author knew the difference between a definition and a `Temp`.

The vanilla side keeps the **complement** of this query for a different purpose: `ToolBaseSystem.GetDefinitionQuery()` is `{CreationDefinition}` with `Exclude<Updated>`, matching only the stale definitions of a previous frame, which is what the sweep below consumes.

(VOLATILE: that `Overridden` is still unreachable on a definition entity, and the count of sites that add it, and that the simulation's spawn archetypes still bake `Deleted` into theirs — every writer of `Overridden` across the tools, net, objects and areas namespaces, and the simulation's zone and area spawn systems.)

## Rewriting a definition changes what the game builds, without touching the tool

The vanilla tool, the vanilla toolbar, the vanilla preview and a different result: that is the technique in its strongest form, and it is a one-system mod.

Read the definitions of the frame, decide, write back, in ascending order of how much each rewrite changes:

- **`ObjectDefinition.m_Elevation` and `m_Position.y`** raise or lower what the object tool is about to place.
  A stacked prop measures elevation differently from a free-standing one, so a rewriter that means "raise this by _n_" adjusts the position alone when the target carries the stacking data, and both fields otherwise.
- **`NetCourse`'s two `CoursePos` endpoints** force a shape across a whole drawn run.
  Walk the frame's definitions in array order and write each course's end elevation into the next course's start, and a dragged road holds a constant slope instead of following the terrain; a parallel-course variant repeats it per side.
- **`CreationDefinition.m_Prefab`** changes what is built.
  Pick a different prefab per definition, seed a `Unity.Mathematics.Random` from that definition's own `m_RandomSeed` so the substitution survives the frames the preview is rebuilt across, write the definition back, and adjust the kind component to match — `ObjectDefinition.m_Age` for a tree, for instance, since the age the tool chose belonged to the prefab it thought it was placing.

**Gate the rewriter on the active tool.**
A system whose query is `{CreationDefinition, Updated}` matches every definition in the world, including the ones the simulation emits and the ones other tools draw, so the first lines of the update should return early unless the tool system's active tool is the one this rewrite is meant for, and unless that tool is in a mode that places rather than selects.

## Suppressing a placement: destroy the definition, or take its `CreationDefinition` away

Two forms, differing in what the vanilla consumer sees afterwards.

**Destroy the entity.**
`EntityManager.DestroyEntity(definition)` on a definition whose `m_Prefab` matches a cached list of prefab entities is how a mod stops the game placing something it always places — the decorative surface a building brings with it, say — and the same move applied to a geometric test is how a square vanilla brush is constrained to a round one, by destroying every definition whose position falls outside the radius or fails a slope filter.
Cache the prefab entities to compare against rather than resolving them per definition, and let the system start disabled and enable itself once the world has loaded.

**Strip the `CreationDefinition` and keep the rest.**
`RemoveComponent<CreationDefinition>(definition)` leaves the kind component and its data in place while the vanilla consumer stops matching the entity, so a mod that has read what it needs out of a `Zoning` or a `NetCourse` can go on to handle the placement itself.
Do it from a system spliced immediately before the consumer, and only on the definitions your mod actually claims, so everything else still goes down the vanilla path.

Both forms depend on the synchronous-playback rule above: a removal or a destruction queued into `ModificationBarrier1` lands after the consumer has already acted on the entity.

## Definitions are not a tool-only mechanism, and that is why `Permanent` exists

A tool's definition is a preview that the player may still cancel; a `Permanent` one is a commitment the emitter has already made, and the flag is what separates them.
**Test `CreationFlags.Permanent` rather than the producing namespace**, which discriminates nothing: the simulation emits `Permanent` definitions and so does at least one system in the tools namespace, the one handling upgrades on deleted buildings.

Producers worth knowing, because each shows a different shape:

- the building-construction system emits one per sub-area and per sub-net of a building that has finished construction, with `m_Owner` set to the building;
- the zone-spawn system emits `Permanent | Construction` for a growable it is about to spawn, and plain `Permanent` for that building's sub-areas and sub-nets;
- the placeholder system resolves a placeholder into a concrete variation with `Permanent | Native` during deserialization.

**A producer in the simulation phases gets the one-frame lifetime for free**, which is the other half of why `Permanent` is worth recognising.
`GameSimulation` is driven from `LateUpdate`, after the frame's modification phases have already run, so a definition the simulation emits in frame _N_ is consumed at `Modification1` of frame _N+1_, still carrying `Updated`.
The `Deleted` baked into its spawn archetype then has the cleanup system destroy it at the end of that frame: exactly one consumption, and no sweep needed.

So a mod that wants to change what **grows** on a lot has a seam here that has nothing to do with any tool, and a definition rewriter that does not exclude `Deleted` will find these definitions in its query.

Definitions are also **read** rather than produced, and a reader makes a hand-built definition look right for free.
The guide-lines system, at `Rendering`, queries `All = {CreationDefinition}` with `Any = {NetCourse, WaypointDefinition, Zoning, Game.Areas.Node, ObjectDefinition}` and draws the placement guides and distance labels.
The net-course tooltip system, at `UITooltip`, queries `{CreationDefinition, NetCourse}` and shows the length and elevation readout.
A mod tool that emits a well-formed `NetCourse` definition gets both without writing a line of UI.

The serialization clear pass lists `CreationDefinition` among the component types whose entities are destroyed on load, which is the statement that a definition never survives a save.

(VOLATILE: the flags and the phase each named producer and reader uses — each named system's own declaration.)

## Who destroys a definition

A definition entity is not destroyed by its consumer: nothing in the generation family removes or destroys anything on the definition side.

Tool-emitted definitions are swept by the tool itself, one frame late.
`ToolBaseSystem.DestroyDefinitions(EntityQuery, ToolOutputBarrier, JobHandle)` schedules a chunk job that destroys every entity in the query through the barrier's parallel writer, and the query it expects is `GetDefinitionQuery()` — `{CreationDefinition}` excluding `Updated`.
Because the cleanup system strips `Updated` at the end of each frame, that query matches precisely last frame's definitions, so a tool calling `DestroyDefinitions` before creating this frame's leaves exactly one generation alive.
Every vanilla tool does it, and `custom-tools` records where in the tool's own state machine the calls sit.

**The default tool sweeps too, and its query is global.**
Its definition update opens with the same `DestroyDefinitions` call against a query filtered on nothing but `CreationDefinition` and the absence of `Updated`, so the fallback tool cleans up any stale definition in the world, including one a mod left behind.
It is the only garbage collector this mechanism has, and it only runs when the player has no other tool active.

The default tool is also a definition **producer**, for a purpose that is easy to miss: it emits a `CreationDefinition` carrying only `m_Original` plus `CreationFlags.Select`, or `Parent | Duplicate` for a parent, and attaches an `IconDefinition` or an `AggregateElement` buffer where the selected entity calls for one.
That is how the selection highlight works — a `Select` definition becomes a `Temp` with `TempFlags.Select` standing in for the real entity — and it is the vanilla template for any mod that wants a temporary copy of a live entity to edit.

## Producing definitions: the sanctioned helper, and what "vanilla-quality" actually means

`ObjectToolBaseSystem` exists for one protected method, `CreateDefinitions(...)`, which schedules the game's own definition job and emits through `ToolOutputBarrier`.
`custom-tools` owns that helper — its signature, the cost behind it and the choice of base class it decides; what belongs here is what the job actually produces.

**The job's job is composition.**
It resolves the control points into a placement, emits the definition for the object itself, and then recurses through the prefab's structure — sub-objects, sub-nets, sub-lanes, sub-areas — threading an `OwnerDefinition` down so every sub-element points at its not-yet-created parent.
Around that walk it does placeholder resolution, attachment resolution, the lowered-parent test, the lot-clearing triangles, spreading along a bezier for line and curve modes, and brush scattering that iterates the object search tree to avoid what is already there.

**So "vanilla-quality preview" is not about rendering — no tool renders anything.**
It means a definition tree complete enough that the generation system produces the same `Temp` entities the vanilla tool would, which is what makes a placed building bring its own driveway, lawn and lamp posts along with it.

A mod that reimplements the job — expect fourteen hundred lines and some sixty injected lookup fields, wired one by one and executed on the main thread rather than scheduled — keeps and drops along a clean line.

**What has to be kept** is everything driven by prefab structure: the recursive walk over sub-objects, sub-nets and sub-areas, the `OwnerDefinition` threading, placeholder variation resolution, the attached-parent resolution, the lowered-parent test, the parent-prefab check and the clear-area plumbing.
**What can be dropped** is everything driven by tool state: brush scattering and with it the object search tree, snapping and distance, the frame delta, removal and stamping, decoration mode, the lane editor, the transform prefab — and dropping that last one means the fork never sets `CreationDefinition.m_SubPrefab` — and the attachment and service-upgrade data.
Collapsing the vanilla `NativeList<ControlPoint>` to a single `ControlPoint` and calling the whole thing once per placed object rather than once per gesture is the other half of that trade, and the residue is a create-only, single-point, no-brush, no-snap definition producer: exactly the shape a mod needs when it owns the spacing itself.

`ControlPoint` is the input side of the seam — `m_Position`, `m_HitPosition`, `m_Direction`, `m_HitDirection`, `m_Rotation`, `m_OriginalEntity`, `m_SnapPriority`, `m_ElementIndex`, `m_CurvePosition`, `m_Elevation`, plus an `EqualsIgnoreHit` that compares with a 0.001 tolerance.
`LocalTransformCache` is how an existing entity's `m_Probability` and `m_PrefabSubIndex` survive a rebuild, read back off the original before the new definition overwrites them.

**Hand-building a definition without the helper is the norm, and each kind has a small template.**

- **Net course**: a static emitter taking a command buffer, an edge description, the flags and the original entity, setting `m_FixedIndex = -1`, the two `m_CourseDelta` endpoints to 0 and 1, and both `m_ParentMesh` to -1, is enough to share one preview path across a family of network tools.
- **Object, from an existing entity**: emit `CreationFlags.Select` with `m_Original` set, copy the entity's own `PseudoRandomSeed` into `m_RandomSeed` so the copy looks identical, and resolve an editor-container owner into `m_Prefab`/`m_SubPrefab` where the target sits inside one.
  A definition emitted purely to obtain a `Temp` copy is how a mod edits an entity through the placement pipeline instead of writing the live one.
- **Delete**: `m_Original` set, `m_Prefab` left null, `CreationFlags.Delete` — the vanilla bulldoze tool's whole definition.
- **Relocate, delete, duplicate**: one `CreationFlags` field switched between `Relocate`, `Delete`, `Hidden` and `Recreate | Parent` covers a move tool's entire vocabulary, with a definition per sub-net and per sub-area, each given an `OwnerDefinition` built from the owner's live transform.
- **Area**: a two-line helper that adds the `CreationDefinition` beside a `Game.Areas.Node` buffer filled from the polygon and closed into a ring.
- **Brush**: a `BrushDefinition` naming the brush prefab entity; extra data your own consumer needs can be written onto that prefab entity rather than onto the definition, which keeps the definition's shape vanilla.

**Add the `CreationDefinition` last, after the kind component**, which is the ordering every vanilla producer uses.

Where a mod ports the vanilla selection path wholesale, the shape is one `AddEntity(Entity original, Entity owner, OwnerDefinition, bool isParent, bool attachParentCreated)` that branches on what the original is and attaches the matching kind — a `NetCourse` for an edge or a node, an `ObjectDefinition` for anything with a transform, a `Game.Areas.Node` buffer copied wholesale for an area, a `WaypointDefinition` buffer rebuilt from the route's waypoints, an `IconDefinition` from the live icon, an `AggregateElement` buffer copied wholesale — and recurses into sub-areas with an `OwnerDefinition` built from the parent's prefab and transform.
Gate the `CreationFlags.Select` branch on a field of your own rather than setting it unconditionally, and the same code can build a definition tree without lighting the selection up.

## A mod can add a definition kind of its own

The protocol is open rather than a fixed list of nine: nothing in it privileges the vanilla kinds.

Declare an ordinary `IComponentData` of your own, emit it exactly the way a vanilla tool emits `ObjectDefinition` — `CreateEntity()`, `AddComponent<CreationDefinition>`, `AddComponent<MyDefinition>`, `AddComponent(default(Updated))`, plus any buffer your consumer needs — and then reproduce the vanilla four-stage shape around it, each stage spliced next to the vanilla system it parallels:

| Your system  | Where it goes                                                                  |
| ------------ | ------------------------------------------------------------------------------ |
| Generator    | The modification phase your kind needs, before your consumer's inputs are gone |
| Validator    | `UpdateAfter<MyValidation, ValidationSystem>(ModificationEnd)`                 |
| Clear system | `ClearTool`                                                                    |
| Apply system | `UpdateBefore<MyApply, ApplyNetSystem>(ApplyTool)`                             |

A generator that needs the vanilla `Temp` entities to exist runs later than `Modification1` — `Modification3` with a second query of `{Node, Temp, Updated}` is the worked shape — because at `Modification1` the vanilla nodes it wants to attach to have not been created yet.

**Feed your validation back into the vanilla error protocol rather than replacing it**: add `Game.Tools.Error` plus `BatchesUpdated` to the offending `Temp` entity _and_ to the `Temp`'s `m_Original`, so the vanilla apply gate blocks on your error too.
Where the mod also wants its own feedback buffer, override `GetAllowApply()` to test that buffer's query alongside the vanilla error query — `custom-tools` owns that override from the tool side.

## Mod-created entities need a prefab reference the load pass can resolve

**The rule.**
An entity that carries a `PrefabRef` must point at a prefab entity registered through `PrefabSystem.AddPrefab`, carrying `PrefabData`, by the time the load pass runs.
Every entity carrying a `PrefabRef` and not `Temp` or `Deleted` is matched by the serialization pass that remaps prefab references, and its job indexes the prefab-data lookup with the referenced entity directly — no `TryGetComponent`, no null guard — so a reference to anything else faults inside a Burst job during load.
The pre-deserialize hook is the last place a mod can register a prefab before that pass runs, which is why registration goes there and not in `OnLoad` or in a system's `OnCreate`.

**The practice built on it.**
A mod whose own entities have no natural prefab gives them one: an empty `PrefabBase` subclass with no fields and no behaviour, whose two archetype hooks add a single marker component of the mod's own to the prefab entity and to the instance archetype.
It is instantiated with `ScriptableObject.CreateInstance<T>()` in the pre-deserialize hook, named with the mod's own prefix, marked active, handed to `PrefabSystem.AddPrefab`, and its entity cached in a static field; that entity is then stamped as `PrefabRef` onto every entity the mod creates — at generate time, at apply time, and again in a load-time repair pass that adds one to any entity found without it and corrects any that points elsewhere.

The practice reaches further than the rule, and it earns the reach: the rule binds an entity that already carries a `PrefabRef`, and the practice gives one to entities that might have gone without.

**An entity with no `PrefabRef` costs nothing while the game runs and kills the process at the next world transition.**
Stripping the component off a live sub-object disturbs nothing at runtime — no exception, no stall, the simulation ticks on and the entity keeps working — and the save writes normally.
That silence is the trap: nothing connects the failure to the thing that caused it.

The bill comes due whenever the game tears the world down and rebuilds it — reloading a save, or merely returning to the main menu.
The serialization pass that rebuilds each owner's `SubObject` buffer from its members' `Owner` back-references logs `Owner has no SubObject: <index>:<version>`, naming the offending entity, and the process dies on that line.
So a mod that leaves its own entities without a prefab does not crash the session that creates them; it crashes the player who quits to the menu.

**Give every entity you create a `PrefabRef`**, and where the entity has no natural prefab, mint the empty one above rather than leaving the component off.

(VOLATILE: that the owner/sub-object rebuild is where this surfaces — the serialization namespace's sub-object system.)

The distinction is worth the extra sentence because what you are deciding is an entity archetype, the most expensive thing to change once a save format depends on it: adding the reference late means a migration, and adding it needlessly means a dead component in every save.

(VOLATILE: the unguarded prefab-data index in the load-time reference remap, and the query that selects entities for it — the serialization namespace's prefab-reference types.)

## A tool error is a prefab, an enum member and three tag components

The question "how do I suppress the error blocking my apply" cannot be answered without this, and a sweep confined to the tools namespace misses half of it: **a tool error is authored as a prefab**, and the prefab type lives in `Game.Prefabs`.

`ToolError : ComponentBase` is a prefab component bound by a `ComponentMenu` to `NotificationIconPrefab`.
It carries an `ErrorType m_Error` and three booleans — `m_TemporaryOnly`, `m_DisableInGame`, `m_DisableInEditor` — and its `Initialize` folds them into the runtime component `ToolErrorData { ErrorType m_Error; ToolErrorFlags m_Flags; }` on the prefab entity.
`ToolErrorFlags` is used as a bitmask but is **not** declared `[Flags]`: `TemporaryOnly = 1`, `DisableInGame = 2`, `DisableInEditor = 4`.
Its host prefab contributes `NotificationIconData` and `NotificationIconDisplayData`, which is why every query for tool-error prefabs is `{NotificationIconData, ToolErrorData}`.

`ErrorType` is a plain enum of thirty causes between `None = 0` and `Count = 31`: `OverlapExisting`, `InvalidShape`, `NotEnoughMoney`, `PathfindFailed`, `NoRoadAccess`, `NoCarAccess`, `NoPedestrianAccess`, `LongDistance`, `TightCurve`, `NoTrainAccess`, `NoTrackAccess`, `AlreadyUpgraded`, `InWater`, `NoCargoAccess`, `NoWater`, `ExceedsCityLimits`, `NotOnShoreline`, `AlreadyExists`, `ShortDistance`, `LowElevation`, `SmallArea`, `SteepSlope`, `ExceedsLotLimits`, `NotOnBorder`, `NoGroundWater`, `OnFire`, `NoPortAccess`, `NotEnoughClearance`, `NoBicycleAccess`, `NotEditable`.
`ErrorSeverity` has six levels — `None`, `Override`, `Warning`, `Error`, `Cancel`, `CancelError` — and `ErrorData { m_TempEntity, m_PermanentEntity, m_Position, m_ErrorType, m_ErrorSeverity }` is the in-flight record.

**`ValidationSystem` is the only producer, and it runs at `ModificationEnd`.**
It updates only when a `Temp` set exists — its guard query is `{Temp, Updated}` excluding `Deleted`, `Relative`, `Moving` and `Stopped` — and then, in order:

1. A job runs over `{NotificationIconData, ToolErrorData}` and fills a `NativeArray<Entity>` of one slot per `ErrorType` value, indexed by that value, **skipping any prefab whose flags carry the disable bit for the current mode** — `DisableInEditor` in the editor, `DisableInGame` otherwise.
2. The validation jobs enqueue `ErrorData` records.
3. Each record is dequeued and returns immediately when its slot in that array is `Entity.Null`.
   Otherwise a severity at or above `Cancel` cancels the `Temp` entity, and anything below it adds a temporary icon through the icon command buffer and records the entity in a map of entity to severity.
4. `ValidationSystem.Components` — a **separate** system, registered immediately after — turns that map into components, removing stale `Error`, `Warning` and `Override` tags and adding the current ones through `ModificationEndBarrier`, tagging everything it touches `BatchesUpdated`.

The three tags are zero-size.
The apply dispatcher reads two of them at `ApplyTool`: a chunk carrying `Warning` gets `Deleted`, a chunk carrying `Override` gets `Updated` and `Overridden`.
`ToolBaseSystem.GetAllowApply()` reads only `Error`, through its own error query.

**The nesting matters exactly once, and it is the case a mod hits.**
`ValidationSystem` itself tags nothing, so a mod system spliced `UpdateAfter<Mine, ValidationSystem>(ModificationEnd)` still runs after `ValidationSystem.Components` has added and removed this frame's tags — two systems anchored on the same target splice in registration order and vanilla registered first.
That is the position from which a mod adds `Error` tags of its own and expects them to survive the frame.

(VOLATILE: the `ErrorType`, `ErrorSeverity` and `ToolErrorFlags` member sets, the error-prefab array sized to the `ErrorType` count, and the `{NotificationIconData, ToolErrorData}` prefab query — the tools and prefabs namespaces, and the validation system.)

## Turning a tool error off means editing its prefab, and putting it back afterwards

Step 1 above is the lever.
**An error type whose prefab was skipped there produces no icon, no cancel, no `Error` tag and therefore no apply block**, because step 3 returns on the null slot.
So setting `ToolErrorFlags.DisableInGame` on the prefab's `ToolErrorData` is the entire suppression mechanism: no patching, no forked validation system, and no other system involved.

It ships as a pair, and both phases are load-bearing:

- **Disable at `Modification5`**, writing the ORed flags back through `ModificationBarrier5`, which plays back at the end of that phase — so the edit is committed before `ModificationEnd` reads the prefabs.
- **Restore at `ModificationEnd`**, clearing the flags through `ModificationEndBarrier`, which plays back at the end of that phase — so the restore lands after `ValidationSystem` has read them.

Either half in the wrong phase silently does nothing, or leaves the errors off permanently.
Have the restore system start disabled, set `Enabled = false` on itself at the end of its update so it runs exactly once per disable, and have the disable system re-arm it; the prefabs are then untouched outside a single phase gap, which is what makes the technique safe to leave installed.

Bail out of the disable system while no tool is active, so the flags are only ever off during a placement.

**These prefabs are ordinary named assets**, so the one you want can also be fetched by identity — `new PrefabID("NotificationIconPrefab", "Already Exists")` resolves the already-exists error — which is the fallback when a `ToolErrorData` query cannot distinguish two of them.
Some of them ship with a disable bit already set — which ones is authored prefab data rather than code, and cannot be read out of the game's source — so a restore pass that blindly clears both flags re-enables errors the game itself had switched off.
Keep an exclusion list for the ones you find that way, and re-enable nothing your own pass did not disable.

The complementary move is to **reuse** a vanilla error prefab rather than suppress it: scan the same `{NotificationIconData, ToolErrorData}` query for the `ErrorType` whose icon and description fit what your own validation wants to say, cache the entity, and raise your icons with it instead of authoring a notification icon prefab of your own.

## What this reference hands to others

`custom-tools` is the other half of this seam, and neither reference is complete without it: it owns `ObjectToolBaseSystem` as a choice of base class, the `ApplyMode` state machine that decides when definitions are rebuilt and destroyed, and the `GetAllowApply()` gate that the error findings above fill in the first place.

`prefabs-and-assets` sits on the other side of every `Entity` field here: `m_Prefab` and `m_SubPrefab` are prefab entities, `PlaceableObjectData` and `ObjectGeometryData` are what a definition producer reads off them — the producer seeds `ObjectDefinition.m_Probability` to 100 and overwrites it from the first only when that prefab's placement flags carry `HasProbability` — and the tool-error prefabs of the last two sections are authored prefabs found by `PrefabID` and edited at runtime.

`zoning-buildings-and-land-value` is reached twice over: every act of zoning and dezoning passes through a `Zoning` definition, and growth itself emits `Permanent` definitions for the building, its sub-areas and its sub-nets, so a mod changing what grows on a lot has a seam here that involves no tool at all.

`roads-and-traffic` owns `NetCourse`, the richest kind, and both of its consumers; `CoursePosFlags`' fifteen members are all network concepts, and the game's own definition rewriter exists to split courses at intersections.

`environment-and-pollution` owns the other two kinds outright, through terraforming and water: `BrushDefinition` carries the terrain tool's line, angle, size, strength, time and target, and `WaterSourceDefinition` carries position, constant depth, radius, multiplier, pollution, height and source id.
`city-services-and-coverage` is reached through `IconDefinition`, which is how a notification icon is previewed alongside the thing that will carry it, and through the notification icon prefab the error prefabs share with service notifications.
`transportation-and-vehicles` is reached through `WaypointDefinition` and `ColorDefinition` and the two-phase route pipeline.

`ecs-in-this-game` owns `Updated`, `Temp` and the barrier playback points the whole window argument rests on.
`mod-lifecycle-and-ordering` owns the band-versus-phase rule that decides whether a rewriter runs before or after its consumer.
