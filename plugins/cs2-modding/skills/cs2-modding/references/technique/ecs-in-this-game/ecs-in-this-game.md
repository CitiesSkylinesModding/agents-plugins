# ECS in this game

Verified against game version 1.6.0f1.

How to write ECS code that reads like the rest of this codebase.
Stock Entities practice and this game's practice diverge in several places, and each divergence below is a thing an agent arriving from a tutorial gets wrong.

_When_ a system runs belongs to `mod-lifecycle-and-ordering`, and this reference states no phase ordering: where a command buffer's flush point appears below, it is the barrier's own registration and nothing more.
What a component costs in bytes and allocations is `performance-and-memory`; what it costs in a save file is `save-serialization`.
Both take their declaration rules from here.

## The five component kinds, and how unevenly the game uses them

| Kind                    | Where it appears                                                                  |
| ----------------------- | --------------------------------------------------------------------------------- |
| `IComponentData`        | Everywhere. Over a thousand game types; the default choice.                       |
| `IBufferElementData`    | Common. A variable-length list owned by one entity.                               |
| `ISharedComponentData`  | **Five** game types, one of them load-bearing. A mod rarely needs to declare one. |
| `IEnableableComponent`  | **Twelve** game types, listed below, two of which change what your query means.   |
| `ICleanupComponentData` | **Zero** game types. The engine honours it; the game never reaches for it.        |

The two small rows are the finding.

**A shared component's value lives once per chunk, not once per entity**, so every distinct value is a distinct set of chunks.
That is the whole reason to declare one and the whole reason to be careful: a shared component with many values shatters an archetype into many part-full chunks.
The game takes that trade exactly once, for simulation bucketing (below).

**A cleanup component survives `DestroyEntity`.**
The entity moves into a residue archetype holding only the cleanup components and an internal marker, and dies for real only once you remove the cleanup component.
That is the one correct way for a component to own a handle — an unmanaged allocation, or a managed mesh or material pinned inside an otherwise blittable struct — because it guarantees a disposal system gets to see the entity after deletion.
Forget the removal and you leak entities silently, with nothing in the log.

The game's own answer to "clean up after me" is not a cleanup component at all: it is the `Deleted` tag plus a frame of grace, and the tag section below is where that lives.

**Every component the game declares also implements `IQueryTypeParameter`**, an empty marker with no members.
It is the constraint on the `SystemAPI.Query<T>()` foreach and on nothing else, so it buys the game nothing and is pure house style — but a mod component without it cannot be used in that foreach, and adding it costs nothing.
Declare it.

## Archetypes come from prefabs; `CreateArchetype` is for events

Two archetype surfaces exist, and a mod meets them in different places.

**The prefab-instance archetype.**
`ComponentBase` declares two abstract members that every prefab component implements:

```csharp
public abstract void GetPrefabComponents(HashSet<ComponentType> components);
public abstract void GetArchetypeComponents(HashSet<ComponentType> components);
```

`GetPrefabComponents` shapes the **prefab entity**; `GetArchetypeComponents` shapes **every instance of that prefab**.
`PrefabBase` seeds the prefab-entity set with `PrefabData` and `LoadedIndex`, and the instance set with `PrefabRef` alone, so an override calls `base` first and then adds its own.
The prefab system unions every attached component's `GetPrefabComponents` contribution, **adds `Created` and `Updated` unconditionally**, and calls `EntityManager.CreateEntity` — that builds the prefab entity, not the instance archetype.
The instance archetype is built separately, by a refresh method run from the prefab's late initialization, and several prefab families override that method — so which hook shapes your instances depends on what kind of prefab it is.
`prefabs-and-assets` owns that path and the families it splits into.

So a mod that wants a component on every instance of a prefab overrides `GetArchetypeComponents`, and a mod that wants it on the prefab entity overrides `GetPrefabComponents`.
Neither needs a system.

Archetypes built this way are cached on the prefab-data components and read from inside a job through a `ComponentLookup`, which is how a job spawns a fully-formed instance without touching the `EntityManager`.
(VOLATILE: the prefab-data types that cache an archetype — `ArchetypeData`, `ObjectData`, `NetData`, `NetLaneArchetypeData` — and the `Created`/`Updated` seeding; the prefab archetype refresh.)

**`EntityManager.CreateArchetype` called directly** is, in the overwhelming majority of the game's several hundred call sites, a one-shot **event archetype**: two or three types, built in `OnCreate`, stashed in an `EntityArchetype` field, and spawned from inside a job through a command buffer.

```csharp
Entity entity = m_CommandBuffer.CreateEntity(unfilteredChunkIndex, m_UnlockEventArchetype);
m_CommandBuffer.SetComponent(unfilteredChunkIndex, entity, new Unlock(prefab));
```

**Build archetypes in `OnCreate`, never in `OnUpdate`.**
The call takes the managed `EntityManager` and cannot run inside a job at all, so an archetype needed by a job is a field the job reads.

## Chunks: what a mod actually touches

A chunk is a 16 KB block holding parallel arrays of components for entities sharing one archetype.
Sixty-four bytes of that are header, leaving 16320 usable, and **a chunk holds at most 128 entities however small the archetype is**.
That 128 is why the per-chunk enabled mask is a `v128` — two 64-bit words, one bit per entity.

Three chunk operations show up in code a mod writes:

- **`chunk.GetNativeArray(ref handle)`** — the workhorse, returning a `NativeArray<T>` that aliases the chunk's storage and has length `chunk.Count`.
  When the component is not in the chunk's archetype it returns a **length-zero array rather than throwing**, which is what makes an optional-component read safe and an unguarded index read a silent out-of-bounds.
  Pair it with `chunk.Has` or check `.Length` before indexing.
- **`chunk.Has(ref handle)`** — a presence test that branches once per chunk instead of once per entity.
- **`chunk.GetSharedComponent(handle)`** — reads the chunk's single shared value.

**The game's one real shared component is `UpdateFrame`**, a single `uint` index that partitions simulated entities into sixteen buckets so each pass touches a sixteenth of them.
The index comes from the prefab's update-group data and is written through a command buffer's `SetSharedComponent`; at load, entities are seeded round-robin across the sixteen.
Two ways to consume it, and both appear in vanilla:

```csharp
// Filter the query, so only matching chunks are visited at all.
m_BuildingGroup.SetSharedComponentFilter(new UpdateFrame(updateFrame));

// Or test inside the job, which is the same skip at chunk granularity.
if (chunk.GetSharedComponent(m_UpdateFrameType).m_Index != m_UpdateFrameIndex)
{
    return;
}
```

A fork of a vanilla per-frame system inherits whichever the original used, and copying the in-job test verbatim is correct.
What a bucket is worth in simulated time belongs to `simulation-time-and-units`.
(VOLATILE: the five shared-component type names, `UpdateFrame`, `UpdateFrameData.m_UpdateGroupIndex`, the sixteen-bucket count and the 128-entity chunk maximum — the `ISharedComponentData` implementors, and the chunk constants.)

## The query APIs, and what decides between them

Four APIs exist in the package, and the game reaches for them unevenly.

| Form                                       | Expresses                       | Needs the generators |
| ------------------------------------------ | ------------------------------- | -------------------- |
| `GetEntityQuery(ComponentType…)`           | `All`, `None`                   | no                   |
| `GetEntityQuery(new EntityQueryDesc{…})`   | `All`, `Any`, `None`, `Options` | no                   |
| `SystemAPI.QueryBuilder()`                 | all four, fluently              | **yes**              |
| `SystemAPI.Query<T>()`, `Entities.ForEach` | iteration, not a query object   | **yes**              |

**Every iteration query in the game is hand-built with `GetEntityQuery`.**
The game's use of `SystemAPI` is singleton access and nothing else: `GetSingleton<T>`, `TryGetSingleton<T>`, `GetSingletonEntity<T>`, `GetSingletonBuffer<T>`, `HasSingleton<T>`.
The builder is equally correct in a mod, and needs no more than the generators the toolchain already wires in, so the choice between the two is made per system rather than per project.

**The mechanism behind that choice is the one thing to internalise.**
Every `SystemAPI` member in the shipped assembly is a body that throws.
The real work is done by Roslyn source generators, which rewrite each call site at compile time into a cached `EntityQuery` field, an `__AssignQueries(ref SystemState)` method that builds it, and an `OnCreateForCompiler` override that calls it.
Three consequences:

1. **The system class must be `partial`**, because the generator emits `OnCreateForCompiler` into the other half.
2. **A `SystemAPI` call the generator did not rewrite throws at runtime**, since the shipped body is a throw. There is no graceful degradation.
3. **The generators only run inside a mod project.** The official toolchain wires them in as analyzers and hard-errors at build time if the package they come from is missing, so a project built through it has them and a project assembled by hand may not.

The generation happens during the C# compile.
The post-processing step the toolchain runs _after_ the build is the Burst and IL pass; it generates no queries, and the two fail differently — a missing generator is a compile error, a missing post-processor costs Burst.

Then the small rules:

- **Build queries in `OnCreate`.** Universal in the game, and the generated form does the same thing from `OnCreateForCompiler`.
- **Mark components read-only unless you write them.** The generated handle names encode the mode, so a decompiled system tells you its intent at a glance.
- **The varargs form cannot express `Any`.** It has `ReadOnly`, `ReadWrite` and `Exclude`, which map to `All` and `None` and nothing else; reach for `EntityQueryDesc`, or construct `EntityQueryBuilder` by hand, which takes an allocator and needs no generator.
- **A fork of a vanilla system inherits the vanilla form**, because the starting point is decompiled source.

### The gates, and the one that ignores your filter

`RequireForUpdate(query)` appends to a list and `ShouldRunSystem` returns false if **any** required query is empty, so repeated calls are ANDed.
`RequireAnyForUpdate(params EntityQuery[])` decomposes the queries you pass and rebuilds them into a single OR query, and is the only way to express "run if either matches".

**The gate tests the query ignoring its filter.**
A query narrowed with `SetSharedComponentFilter` still gates on the unfiltered set, so a system gated on a per-bucket query runs on every pass and does nothing on fifteen passes out of sixteen.
That is by design and the game relies on it; it is only a surprise if you expected the gate to save the update.

## Jobs: write per-entity, read per-chunk

**Write new jobs as `IJobEntity`.**
It is the modern replacement for `IJobChunk` and it drops the whole fetch-array-and-index preamble: the parameter list _is_ the query, and `Execute` is called once per entity.
The source generators it needs are wired in as analyzers by the official toolchain, so it compiles and runs in a mod project today.

```csharp
[BurstCompile]
private partial struct AgeCitizensJob : IJobEntity
{
    public float m_DeltaTime;

    private void Execute(Entity entity, ref Citizen citizen, in HouseholdMember member)
    {
        // one entity per call; the parameter list is the query
    }
}
```

`ref` for write, `in` for read-only, `Entity` for the entity itself.
**Both the job struct and the system that schedules it must be `partial`**, because the generator emits the `Execute` plumbing and the schedule extension into the other half.
That is the first thing an agent hits, and its absence is a compile error rather than a runtime one, which is the good case.

**Hold the discrepancy in mind before you open vanilla source.**
The game contains not one `IJobEntity`: every jobified system in it — several hundred — is `IJobChunk`.
That is a fact about the codebase's age rather than about what works, so it is not a reason to follow it; what it decides is what you read and what you fork.
**A fork of a vanilla job starts from decompiled source and therefore arrives as `IJobChunk` before you have written a line of it.**

### `IJobChunk`: what you read, and what a fork starts as

```csharp
public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
    bool useEnabledMask, in v128 chunkEnabledMask)
{
    NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
    NativeArray<Citizen> citizens = chunk.GetNativeArray(ref m_CitizenType);
    // …
}
```

Schedule it with `JobChunkExtensions.Schedule` or `JobChunkExtensions.ScheduleParallel`, passing the query and the incoming dependency, and assign the returned handle back to `Dependency`.

**Three facilities come from holding the chunk**, and they are why the game's own jobs hold one:

- **The chunk-level early exit.** The per-bucket shared-component skip above rejects a whole chunk with one read.
- **The chunk-scoped accessors** — `chunk.GetSharedComponent`, `chunk.GetBufferAccessor`, `chunk.DidChange`, `chunk.Has`. All four are questions about a chunk.
- **`unfilteredChunkIndex`.** It is handed to `Execute` and is exactly the stable sort key a parallel command buffer wants.

A per-entity `Execute` never holds the chunk, so it has none of the three: no per-chunk early exit — the same test would run up to 128 times more often — no shared-component read, and a parallel-writer sort key it generates for itself rather than the vanilla number.
Where a fork needs none of the three, converting it to a per-entity job is mechanical.
Where it needs one, keep the chunk form for that job rather than emulating the chunk from inside a per-entity `Execute`.

### Burst is a choice, not a default

`[BurstCompile]` behind a conditional compilation symbol keeps both builds reachable from one source, which is what you want because a Burst-compiled job cannot be stepped in a debugger.
Bursting every job and bursting none are both workable, so decide it per project.
Keep the unbursted build reachable either way, since attaching a debugger to a bursted job simply shows nothing.

## Type handles: what they index, and what breaks when one is stale

A `ComponentTypeHandle<T>` caches the type index, the component's size in a chunk, a read-only flag, a lookup cache, and **the global system version**.
`Update(ref SystemState state)` refreshes exactly one of those: the version.

**That single field is the whole point.**
Read-write chunk access stamps the chunk with the handle's version.
A stale handle stamps a stale version, and every change filter downstream — `chunk.DidChange(ref handle, version)`, `SetChangedVersionFilter` — then reports "unchanged" for a chunk you just wrote.
Nothing throws, nothing logs, and the symptom is a system further down the frame that quietly stops seeing your writes.

**Nothing throws because this build has no safety system.**
Handles, lookups and `ArchetypeChunk` carry no safety field, and the bounds and aliasing assertions are all conditional on a collections-checks define that is compiled out of the shipped assembly.
A stale handle, an out-of-bounds chunk index, or two jobs writing the same component in parallel produce wrong data or a crash, never a diagnostic.
`performance-and-memory` owns what that means for scheduling; here it means the handle discipline has no backstop.

**With the generator, the discipline is free.**
`SystemAPI.GetComponentTypeHandle<T>()`, `GetComponentLookup<T>()`, `GetBufferTypeHandle<T>()` and their siblings are rewritten into a generated nested `TypeHandle` struct — one field per handle, assigned once from `OnCreateForCompiler`, and refreshed at the point of use every update.
Skipping the refresh is not something you can do by accident when you use those calls.

**Two hand-rolled idioms exist, and you need one whenever the generator is not writing that code for you** — which is exactly the case in a fork built from decompiled source.

- **Carry the generated struct into the fork and refresh it yourself.**
  The pasted struct is now ordinary source; the generator will not regenerate it, and nothing will refresh its fields.
  Open `OnUpdate` with one explicit call per handle:

  ```csharp
  __TypeHandle.__Game_Citizens_Citizen_RW_ComponentTypeHandle.Update(ref CheckedStateRef);
  __TypeHandle.__Game_City_Population_RO_ComponentLookup.Update(ref CheckedStateRef);
  ```

- **Put the handles on the job and give the job two methods**, `AssignHandles(ref SystemState)` called from the system's `OnCreate` and `UpdateHandles(ref SystemState)` called from its `OnUpdate`.
  This keeps each handle beside the job that reads it and is the cleanest hand-written form.

(VOLATILE: the generated handle-name scheme `__<Namespace>_<Type>_<RO|RW>_<Kind>` and the compiler-interface method names behind `SystemAPI` — both belong to the Entities generator version the toolchain pins, not to the game.)

## The chunk-enabled mask

`IJobChunk.Execute` receives `useEnabledMask` and a `v128` mask, and the correct loop consumes both:

```csharp
ChunkEntityEnumerator enumerator =
    new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

while (enumerator.NextEntityIndex(out int i))
{
    // …
}
```

When the flag is false the enumerator degenerates to a plain counted loop, so this form is always safe.
The game only bothers where the query names an enableable component; everywhere else it loops `for (int i = 0; i < chunk.Count; i++)`, because a query that names no enableable component cannot match a disabled entity anyway.

**The trap is the parameterless constructor.**
`new ChunkEntityEnumerator()` leaves the entity count at zero, so `NextEntityIndex` returns false on the very first call and the loop body never runs — no exception, no warning, just a job that silently does nothing.
Always pass all three arguments.

## Enableable components: twelve, and two of them narrow your query

Toggling an enableable component is a bit flip rather than an archetype move, which is the entire reason to declare one.

The twelve game types: `HasJobSeeker`, `PropertySeeker`, `Arrived`, `BicycleOwner`, `CarKeeper`, `CrimeVictim`, `MailSender`, `Decoration`, `Locked`, `NotificationIconDisplayData`, `PrefabData`, and `CustomMeshColor` — the last being the only enableable buffer in the game.

**A query naming an enableable component matches only entities where it is enabled**, unless the query carries `EntityQueryOptions.IgnoreComponentEnabledState`.
Two of the twelve make that bite:

- **`PrefabData` disabled means "obsolete prefab".** The loader disables it on prefabs a save references but the current install no longer has, and the prefab system uses its enabled state as the "does this prefab still exist" test when writing a save. So `WithAll<PrefabData>()` gives you live prefabs only — almost always what you want, but it is a filter you did not write, and it explains a prefab count that does not match the installed mod list.
- **`Locked` disabled means "unlocked".** Unlocking is `SetComponentEnabled<Locked>(entity, false)`, so a progression query on `WithAll<Locked>()` silently returns only what is _still_ locked. `city-state-and-progression` depends on this.

Toggle from a job through the command buffer:

```csharp
m_CommandBuffer.SetComponentEnabled<BicycleOwner>(unfilteredChunkIndex, citizen, true);
```

(VOLATILE: this list of twelve — the `IEnableableComponent` implementors across the game assembly. The set has grown across versions, and one of its members reached vanilla only after mods had shipped their own component of the same name.)

## Command buffers: twelve named barriers, and one contract

The game exposes command buffers as **named barrier systems**, not as raw `EntityCommandBuffer`s.
Resolve the one you want once in `OnCreate` with `World.GetOrCreateSystemManaged<T>()` and hold it in a field of its concrete type.

| Barrier                  | Plays back                                         |
| ------------------------ | -------------------------------------------------- |
| `EndFrameBarrier`        | front of the main loop — see the window rule below |
| `ModificationBarrier1`   | end of `Modification1`                             |
| `ModificationBarrier2`   | end of `Modification2`                             |
| `ModificationBarrier2B`  | end of `Modification2B`                            |
| `ModificationBarrier3`   | end of `Modification3`                             |
| `ModificationBarrier4`   | end of `Modification4`                             |
| `ModificationBarrier4B`  | end of `Modification4B`                            |
| `ModificationBarrier5`   | end of `Modification5`                             |
| `ModificationEndBarrier` | end of `ModificationEnd`                           |
| `ToolOutputBarrier`      | end of `ToolUpdate`                                |
| `ToolReadyBarrier`       | end of `PostTool`                                  |
| `DeserializationBarrier` | front of `Deserialize`'s back band                 |

`DeserializationBarrier` is the one that does not play back at the end of its phase: it is the first `UpdateAfter` registration in `Deserialize`, so it plays back before the rest of that band rather than after it, and a system placed there cannot ask it for a command buffer.
`save-serialization` maps that band, in the census file its entry points to, and states what a system placed there should use instead.

A thirteenth type, `AudioEndBarrier`, exists in the assembly and is registered in no phase.
It has a companion opener like the others, and that opener is unregistered too, so nothing ever re-opens the barrier after its first playback attempt closes it.
Reach for one of the twelve instead.

**The contract is three calls, and each of the three has a failure mode.**

1. **`CreateCommandBuffer()` once per `OnUpdate`, not once per job.** Every call appends another buffer to the barrier's flush list.
2. **`AddJobHandleForProducer(handle)` after scheduling.** Without it the barrier plays back while your job is still writing into the buffer.
3. **Playback runs in list order and then rewinds the allocator**, so a buffer is single-playback and the handle you got is dead after the barrier updates.

The vanilla shape, worth copying exactly:

```csharp
job.m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter();
Dependency = JobChunkExtensions.ScheduleParallel(job, m_HouseholdQuery, Dependency);
m_EndFrameBarrier.AddJobHandleForProducer(Dependency);
```

**The sort key is not decoration.**
Every `ParallelWriter` method takes an `int sortKey` first, and playback merges the per-thread command chains in ascending sort-key order.
Passing `unfilteredChunkIndex` — the parameter `IJobChunk.Execute` already hands you — makes recording order deterministic across runs regardless of how threads happened to be scheduled.
A constant or a thread index there makes your mod's structural changes order-dependent on the scheduler, which is a bug that reproduces once a week.

**Writing to a barrier outside its window throws, loudly.**
Each barrier closes itself immediately before playing back, and a companion system re-opens it; creating a buffer while it is closed raises `Trying to create EntityCommandBuffer when it's not allowed!`.
This is the one place in this ECS where the failure is an exception rather than silence, so trust it.

**Write only to the barrier belonging to the phase you are running in.**
That is the general rule, and it falls out of where each barrier's opener sits.
Eleven of the twelve open at the start of their own phase and play back at the end of it, so each is open for the duration of that one phase and shut from the end of it until its phase runs again.
For most of the eleven that means the next frame; for the deserialization barrier it means the next load, since its phase fires once per load rather than every frame.
Where the opener and the playback sit within the phase, and what that costs a system registered beside them, is `mod-lifecycle-and-ordering`.

**`EndFrameBarrier` is the exception, and its window is the widest rather than the narrowest.**
Its opener and its playback sit far apart inside the main loop rather than bracketing one phase, so it is open from partway through the frame, across the phases that run after that, and on to the next frame.
Simulation systems use it freely and that is where nearly all of vanilla's use of it sits.
What it does not cover is the front of the frame: **an `OnUpdate` body running before the opener — the modification, tool, raycast, prefab-update and deserialize phases — cannot create an `EndFrameBarrier` command buffer**, and uses its own phase's barrier instead.
This rule is about `OnUpdate` bodies, and a lifecycle hook such as `OnGameLoadingComplete` fires outside the frame's phase walk entirely.
(UNVERIFIED: whether a buffer created from a lifecycle hook lands inside the open window or throws against a closed barrier — the hook's invocation site in `GameSystemBase` read against the barrier's opener and playback registrations in the vanilla system-order class, or one run of the game with a buffer created there.)

**One crack in the gate, and it is in the type system.**
The safety check lives on a method that _shadows_ the base `EntityCommandBufferSystem.CreateCommandBuffer()` rather than overriding it, and the base method is not virtual.
A call through a variable typed as the base class therefore binds to the base method and skips the check entirely — no exception, and a buffer that flushes at an unpredictable time instead.
So a mod that stores barriers in an `EntityCommandBufferSystem`-typed dictionary, or hands one to a generic helper, has traded a loud failure for a silent one.
Hold the concrete barrier type everywhere.

(VOLATILE: the twelve barrier type names and the exception message string — the vanilla system-order class, and the safe command buffer base.)

## The universal tags, and the protocol they carry

Six zero-field components form a frame-scoped change protocol: `Created`, `Updated`, `Applied`, `EffectsUpdated`, `BatchesUpdated` and `PathfindUpdated`.
`Created` and `Updated` are added to every prefab-instance archetype at birth, so a freshly spawned entity carries both and a system querying `WithAll<Created>()` sees it exactly once.

They are removed by a pair of systems.
A preparation system at the very end of the main loop snapshots two sets: everything carrying `Deleted` or `Event`, and everything carrying one of the six tags but _not_ `Deleted`.
A cleanup system at the end of the frame then destroys the first set and strips the six tags from the second.

Three consequences a mod needs:

1. **`Deleted` means the entity dies later in the frame, not now.**
   That gap is the point: every system holding a reference gets a window to query `WithAll<Deleted>()` and unhook.
   This is why the game deletes by adding `Deleted` far more often than it calls `DestroyEntity` — do the same, and reserve `DestroyEntity` for entities nothing else can be holding.
2. **An `Event` entity lives exactly one frame.**
   It is in the destroy set with no exclusion, so an event entity spawned during a frame is gone by the end of it.
   Consume it in the same frame or not at all.
3. **A tag written after the snapshot survives an extra frame.**
   A tag added from a simulation system misses that frame's snapshot, is picked up at the end of the _next_ frame's main loop and removed at the end of that frame — which is precisely what makes it visible to the next frame's modification, tool, UI and rendering work.
   `mod-lifecycle-and-ordering` has the frame structure this rests on.

**What each tag asks for:**

- `Created` — this entity is new.
- `Updated` — something non-visual changed; re-run the modification pipeline over me. The general-purpose "I touched this" tag.
- `BatchesUpdated` — **the graphics for this entity need rebuilding, and this is the tag a mod forgets.** The culling system reads it, the batch instance and batch data systems branch on it, and the culling completion clears it. If you change anything visible on an entity and do not add `BatchesUpdated`, the renderer keeps drawing the old batch and your change is invisible with no error anywhere. Tag the sub-objects too, not only the parent: vanilla adds it to sub-objects and upgrades separately, and a building tagged alone renders with stale props.
- `Applied` — added by the tool apply systems when a preview becomes real.
- `EffectsUpdated` and `PathfindUpdated` — narrow, for visual effects and for lane pathfinding parameters respectively.

**Four more tags are not frame-scoped and never pass through the cleanup pair:**

- `Overridden` — this object conflicts with another object or network but is not deleted. Persists across a save; raycasting and lane generation both skip overridden geometry.
- `Native` — marks map-native content. Persists.
- `Owner` — a single `Entity m_Owner`, the standard back-reference from a sub-object to its parent, and the shape to copy when attaching your own entity to a game entity. Networks are dense graphs reached through it, which is why `roads-and-traffic` leans on it hardest.
- `PseudoRandomSeed` — a `ushort` seed plus `GetRandom(uint reason)`, which derives an independent stream per reason from the one stored seed. This is how the game gets stable per-entity randomness that survives a save without storing a stream, and a mod wanting reproducible per-entity variation should use it rather than seeding its own.

**`Temp` is the tool-preview tag, and it is the one to exclude.**
It lives in the tools namespace, is not serialized, and carries `m_Original` — the real entity this preview stands for — plus a curve position, a value, a cost and flags.
The tool pipeline works entirely on `Temp` copies, and the apply systems read `m_Original` to write back onto the real entity.
**Nearly every game query excludes it**, and `None = { Deleted, Temp }` is the canonical pair: a query that forgets it will see the player's uncommitted hover preview as a real building.
`Hidden` is its sibling for the same reason.

(VOLATILE: all fourteen tag type names above, the six-member type set the cleanup system strips, and `Temp`'s field names — the common and tools namespaces.)

## Declaring components of your own

**Registration is automatic and needs no attribute.**
After a mod assembly loads, the mod manager reflects over its types and registers every supported component with the type manager.
There is no codegen requirement, no registration call, and no manifest entry.

Two consequences:

- **A type cannot be registered twice**, and the second attempt throws with the type named.
- **A generic component needs an explicit assembly attribute**, because reflection cannot enumerate closed generics:

  ```csharp
  [assembly: RegisterGenericComponentType(typeof(MyMarker<Citizen>))]
  ```

**A name matching a vanilla component is a different type and does not clash**, since the namespace is part of the identity.
It is still expensive: the game absorbs mod concepts across versions, so a name that was unique when a mod shipped can collide with a vanilla type a later patch introduces, and every touch of either then needs full namespace qualification to stay readable.
Prefix your components rather than naming them after the concept alone.

**Runtime cost, by kind:**

| Kind                         | Cost                                                                                                                                                |
| ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| Zero-field `IComponentData`  | No per-entity bytes at all. The cost is the extra archetype: adding or removing it moves every affected entity between chunks.                      |
| `IComponentData` with fields | Its size, per entity, in every chunk of every archetype carrying it.                                                                                |
| `IBufferElementData`         | `InternalBufferCapacity` elements reserved inline in the chunk, spilling to the heap beyond that. Default capacity is 128 bytes' worth of elements. |
| `ISharedComponentData`       | Nothing per entity, and one set of chunks per distinct value.                                                                                       |
| `IEnableableComponent`       | Its own bits in the chunk's enabled masks, and a toggle that is not a structural change.                                                            |
| `ICleanupComponentData`      | A residue entity that outlives `DestroyEntity` until you remove the component.                                                                      |

`[InternalBufferCapacity(0)]` means **never inline**: every buffer becomes a heap allocation, which keeps chunks dense when most entities carry an empty buffer.
Split the decision deliberately: `(0)` for a sparsely-populated buffer, and a small explicit capacity for one that almost always holds one to three elements.
`performance-and-memory` owns that trade in full.

**Save cost is decided by one interface and nothing else.**
The serializer library walks every type the type manager knows and registers a serializer for each that implements one of two interfaces; a component implementing neither is simply not written.

| Declares                            | Result                                                                                                |
| ----------------------------------- | ----------------------------------------------------------------------------------------------------- |
| `IEmptySerializable`                | Presence is persisted; there is no payload. The whole declaration for a tag that must survive a save. |
| `ISerializable`                     | Your `Serialize<TWriter>` and `Deserialize<TReader>` are called.                                      |
| Either, plus `IEnableableComponent` | An enableable-aware serializer, so the enabled bit persists too.                                      |
| Either, plus `ISerializeAsEnabled`  | The plain serializer instead: the disabled state is **not** persisted.                                |
| Neither                             | Not written. Reconstruct it on load or lose it.                                                       |

So a persisted tag is one line:

```csharp
public struct MyPloppedMarker : IComponentData, IQueryTypeParameter, IEmptySerializable { }
```

**The library rebuilds after a mod assembly loads**, in the same step that registers the types, so a mod component becomes saveable purely by implementing the interface.
Implementing neither and rebuilding the component on load is the cheaper and safer default: a component in a save is a compatibility obligation forever.
The versioning discipline inside `Serialize` and `Deserialize` — writing a version number first and branching on it when reading — belongs to `save-serialization`, and you want it before the first release, not after.

(VOLATILE: the serializer selection above and the two game types that opt out of persisting enabled state — the component serializer library.)

## The helper extensions the game already ships

`Colossal.Entities.EntitiesExtensions` is a static class of extension methods over `EntityManager`, `ComponentLookup<T>` and `BufferLookup<T>` that collapse the has-then-get dance into one call: `TryGetComponent`, `TryGetBuffer`, `TryGetSharedComponent`, `HasEnabledComponent`, `HasEnabledBuffer`, `TryGetEnabledComponent`, `TryGetEnabledBuffer`.

```csharp
using Colossal.Entities;

if (EntityManager.TryGetComponent(entity, out PrefabRef prefabRef))
{
    // …
}
```

These ship with the game rather than coming from anywhere else, so they cost a mod no dependency at all.
Reach for them before writing your own.

## What this reference hands to others

`mod-lifecycle-and-ordering` for which phase a system belongs in and how to register it there — everything above assumes that decision is already made.
`performance-and-memory` for allocators, job dependencies, and what the chunk geometry and buffer capacity above mean for a frame budget.
`save-serialization` for the save format and the versioning discipline inside `Serialize` and `Deserialize`.

Every mechanics reference sits on top of this one.
`citizens-and-households` exercises it most directly, since the citizen aging system is the canonical shape: a query excluding `Deleted` and `Temp`, a buffer handle walked per chunk, a scattered-write lookup, and `EndFrameBarrier` used to add, remove and toggle components.
`zoning-buildings-and-land-value` and `city-services-and-coverage` need the `BatchesUpdated` rule most, because both are about things the player looks at.
`roads-and-traffic` needs `Owner` and `Temp` more than any other area, plus the enableable-buffer case.
`city-state-and-progression` needs the `Locked` trap.
`simulation-time-and-units` owns the frequency half of the bucketing whose chunk half is above.
`economy-and-companies`, `utilities-and-flow-networks`, `transportation-and-vehicles` and `environment-and-pollution` each need the query, job and barrier material without needing anything unique from it.
