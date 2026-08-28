# ECS in this game

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

How to write ECS code that reads like the rest of this codebase.
Stock Entities practice and this game's practice diverge in several places, and each divergence below is a thing an agent arriving from a tutorial gets wrong.

_When_ a system runs belongs to `mod-lifecycle-and-ordering`, and this reference states no phase ordering: where a command buffer's flush point appears below, it is the barrier's own registration and nothing more.
What a component costs in bytes and allocations is `performance-and-memory`; what it costs in a save file is `save-serialization`.
Both take their declaration rules from here.

## The five component kinds, and how unevenly the game uses them

| Kind | Where it appears |
| --- | --- |
| `IComponentData` | Everywhere. Over a thousand game types; the default choice. |
| `IBufferElementData` | Common. A variable-length list owned by one entity. |
| `ISharedComponentData` | **A handful** of game types; the simulation's bucketing is the one a mod meets. |
| `IEnableableComponent` | **Uncommon**, and some of them change what your query means. |
| `ICleanupComponentData` | Honoured by the engine, and not the game's own cleanup idiom — see below. |

**A shared component's value lives once per chunk, not once per entity**, so every distinct value is a distinct set of chunks.
That is the whole reason to declare one and the whole reason to be careful: a shared component with many values shatters an archetype into many part-full chunks.
The game takes that trade sparingly.
Source: `src/Unity.Entities/Unity.Entities/ArchetypeChunk.cs` (the value read once per chunk).

**A cleanup component survives `DestroyEntity`.**
The entity moves into a residue archetype holding only the cleanup components and an internal marker, and dies for real only once you remove the cleanup component.
That is the one correct way for a component to own a handle — an unmanaged allocation, or a managed mesh or material pinned inside an otherwise blittable struct — because it guarantees a disposal system gets to see the entity after deletion.
Forget the removal and you leak entities silently, with nothing in the log.
`performance-and-memory` owns the pattern itself — the disposal system's shape, and what a save and load do to a residue entity.
Source: `src/Unity.Entities/Unity.Entities/EntityComponentStore.cs` (the residue archetype, and the entity dying only once the component is removed).

The game's own answer to "clean up after me" is not a cleanup component at all: it is the `Deleted` tag plus a frame of grace, and the tag section below is where that lives.

**`IQueryTypeParameter` on a component declaration is a decompiler artifact, not house style and not something to copy.**
`IComponentData` already derives from it, so the `SystemAPI.Query<T>()` constraint — the only thing constrained on it — is satisfied without naming it, and a mod component never needs to.
The decompiler prints the transitive interface closure, which is why every `IComponentData` struct under `src/Game/` lists it and no `IBufferElementData` struct does: that interface does not derive from it.
Source: `src/Unity.Entities/Unity.Entities/IComponentData.cs` (the derivation), `src/Unity.Entities/Unity.Entities/IBufferElementData.cs` (the one that does not), `src/Unity.Entities/Unity.Entities/SystemAPI.cs` (the `Query<T>` constraint).

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

(VOLATILE: the two contribution signatures and the seeded components above — `ComponentBase` and `PrefabBase`; the unconditional `Created`/`Updated` pair — the vanilla prefab system; the instance-archetype refresh — the `RefreshArchetype` overrides, `ArchetypePrefab` and `ObjectPrefab` among them.)
Source: `src/Game/Game.Prefabs/ComponentBase.cs` and `src/Game/Game.Prefabs/PrefabBase.cs` (the two members and the seeded components), `src/Game/Game.Prefabs/PrefabSystem.cs` (the prefab entity's creation), `src/Game/Game.Prefabs/ArchetypePrefab.cs` (the instance-archetype refresh and where it caches the result).

**`EntityManager.CreateArchetype` called directly** is, in the overwhelming majority of the game's several hundred call sites, a one-shot **event archetype**: two or three types, built in `OnCreate`, stashed in an `EntityArchetype` field, and spawned from inside a job through a command buffer.

```csharp
Entity entity = m_CommandBuffer.CreateEntity(unfilteredChunkIndex, m_UnlockEventArchetype);
m_CommandBuffer.SetComponent(unfilteredChunkIndex, entity, new Unlock(prefab));
```

**Build archetypes in `OnCreate`, never in `OnUpdate`.**
The call takes the managed `EntityManager` and cannot run inside a job at all, so an archetype needed by a job is a field the job reads.
Source: `src/Unity.Entities/Unity.Entities/EntityManager.cs` (`CreateArchetype` on the managed type), `src/Game/Game.Prefabs/ProcessingRequirementSystem.cs` (the `OnCreate` build and the in-job spawn).

## Chunks: what a mod actually touches

A chunk is a 16 KB block holding parallel arrays of components for entities sharing one archetype.
Sixty-four bytes of that are header, leaving 16320 usable, and **a chunk holds at most 128 entities however small the archetype is**.
That 128 is why the per-chunk enabled mask is a `v128` — two 64-bit words, one bit per entity.
Source: `src/Unity.Entities/Unity.Entities/Chunk.cs` (the size, header and entity-count constants), `src/Unity.Entities/Unity.Entities/ChunkEntityEnumerator.cs` (the two 64-bit mask words).

Three chunk operations show up in code a mod writes:

- **`chunk.GetNativeArray(ref handle)`** — the workhorse, returning a `NativeArray<T>` that aliases the chunk's storage and has length `chunk.Count`.
  When the component is not in the chunk's archetype it returns a **length-zero array rather than throwing**, which is what makes an optional-component read safe.
  Nothing signals the mistake at the call: the returned array wraps a null pointer and the shipped indexer bounds-checks nothing, so the fault lands on the read rather than on the call that handed it back.
  Pair it with `chunk.Has` or check `.Length` before indexing.
- **`chunk.Has(ref handle)`** — a presence test that branches once per chunk instead of once per entity.
  It answers archetype membership only: for an enableable component it is true even where every entity in the chunk has it disabled, and `chunk.IsComponentEnabled(ref handle, i)` is the per-entity question.
- **`chunk.GetSharedComponent(handle)`** — reads the chunk's single shared value.

Source: `src/Unity.Entities/Unity.Entities/ArchetypeChunk.cs` (all three operations), `src/UnityEngine.CoreModule/Unity.Collections/NativeArray.cs` (the unchecked indexer).

**The shared component a mod meets is `UpdateFrame`**, a single `uint` index that partitions simulated entities into buckets so each pass touches a fraction of them.
**The bucket count is per-family rather than a constant**, and the index has an authored path and an assigned one.
A prefab that declares `UpdateFrameData` — on the prefab class, or through a component type attached under its component menu — pins its instances' index outright; everything else `UpdateGroupSystem` load-balances into the least-loaded bucket of its family, with further writers assigning one at request creation and when an old save is migrated.
**A prefab you add that declares no `UpdateFrameData`, and inherits none from the base you derived from, therefore takes a load-balanced index**, which is how a new prefab lands outside the buckets a gated vanilla system actually visits, so it is never served and nothing is logged.
Source: `src/Game/Game.Simulation/UpdateFrame.cs` and `src/Game/Game.Prefabs/UpdateFrameData.cs` (the shared component and the authored pin), `src/Game/Game.Simulation/UpdateGroupSystem.cs` (the pin-then-load-balance assignment and the per-family group arrays), `src/Game/Game.Simulation/SimulationUtils.cs` (the per-family counts).

Two ways to skip on it, and both appear in vanilla:

```csharp
// Filter the query, so only matching chunks are visited at all.
m_BuildingGroup.SetSharedComponentFilter(new UpdateFrame(updateFrame));

// Or test inside the job, which is the same skip at chunk granularity.
if (chunk.GetSharedComponent(m_UpdateFrameType).m_Index != m_UpdateFrameIndex)
{
    return;
}
```

**Neither form says where the index or the bucket count comes from, and a fork that guesses either runs at a fraction of the vanilla rate or never runs at all, with nothing logged.**
Source: `src/Game/Game.Simulation/BuildingUpkeepSystem.cs` (the filtered query) and `src/Game/Game.Simulation/AgingSystem.cs` (the in-job test).

Read [update-frame-buckets.md](update-frame-buckets.md) before forking a system that partitions on it, or before adding a prefab to a family a gated vanilla system serves — it carries both the read and the gated-bucket failure above.
What a bucket is worth in simulated time belongs to `simulation-time-and-units`.

(VOLATILE: the 128-entity chunk maximum — the chunk constants. `UpdateFrame`'s field name and the load-balancing assignment — `UpdateGroupSystem` and its group arrays. The authored path — `UpdateFrameData`. The request-creation and save-migration writers — `ServiceRequestSystem` and `RequiredComponentSystem`.)

## The query APIs, and what decides between them

Several query forms exist in the package, and the game reaches for them unevenly.

| Form | Expresses | Needs the generators |
| --- | --- | --- |
| `GetEntityQuery(ComponentType…)` | `All`, `None` | no |
| `GetEntityQuery(new EntityQueryDesc{…})` | `All`, `Any`, `None`, `Disabled`, `Absent`, `Present`, `Options` | no |
| `SystemAPI.QueryBuilder()` | the same, fluently | **yes** |
| `SystemAPI.Query<T>()`, `Entities.ForEach` | iteration, not a query object | **yes** |

**Every iteration query in the game is hand-built with `GetEntityQuery`.**
In query position the game's use of `SystemAPI` is singleton access and nothing else: `GetSingleton<T>`, `TryGetSingleton<T>`, `GetSingletonEntity<T>`, `GetSingletonBuffer<T>`, `HasSingleton<T>`.
Outside it the game leans on `SystemAPI` heavily — every vanilla `__TypeHandle` field is a `SystemAPI` handle or lookup call the generator rewrote, entity, buffer and shared-component handles included, which is the mechanism the type-handle section below turns on.
The builder is equally correct in a mod, and needs no more than the generators the toolchain already wires in, so the choice between the two is made per system rather than per project.
Source: `src/Game/Game.Simulation/AdjustElectricityConsumptionSystem.cs` (the generated `__query_` fields and the singleton reads they serve).

**The mechanism behind that choice is the one thing to internalise.**
Every `SystemAPI` member in the shipped assembly is a body that throws.
The real work is done by Roslyn source generators, which rewrite each call site at compile time into a cached `EntityQuery` field, an `__AssignQueries(ref SystemState)` method that builds it, and an `OnCreateForCompiler` override that calls it.
Three consequences:

1. **The system class must be `partial`**, because the generator emits `OnCreateForCompiler` into the other half.
2. **A `SystemAPI` call the generator did not rewrite throws at runtime**, since the shipped body is a throw. There is no graceful degradation.
3. **The generators only run inside a mod project.** The official toolchain wires them in as analyzers and hard-errors at build time if the package they come from is missing, so a project built through it has them and a project assembled by hand may not.

Source: `src/Unity.Entities/Unity.Entities/SystemAPI.cs` (the throwing bodies), `src/Game/Game.Simulation/AdjustElectricityConsumptionSystem.cs` (a rewritten call site), `%CSII_TOOLPATH%/Mod.props` and `%CSII_TOOLPATH%/Mod.targets` (the generators wired as analyzers, and the build error when they are absent).

The generation happens during the C# compile.
The post-processing step the toolchain runs _after_ the build is the Burst and IL pass; it generates no queries, and the two fail differently — a missing generator is a compile error, a missing post-processor costs Burst.

Then the small rules:

- **Build queries in `OnCreate`.** Universal in the game, and the generated form does the same thing from `OnCreateForCompiler`.
  The mechanism is ownership: `GetEntityQuery` compares the requested shape against every query the system already holds, appends a new one to a list that lives as long as the system, and joins it to the system's job-dependency tracking — so repeated identical calls are cheap, and a query built per call from a **runtime-chosen** type set grows that list for the world's lifetime, with nothing logged.
  For a type set decided at runtime — a user choice, another mod's components — build with `EntityQueryBuilder.Build(EntityManager)`, which returns a query the caller owns and disposes after the read; the system-taking overloads — `Build(SystemBase)`, `Build(ref SystemState)` — route back into the system's cache.
  Source: `src/Unity.Entities/Unity.Entities/SystemState.cs` (the comparison, the append and the dependency join), `src/Unity.Entities/Unity.Entities/EntityQueryBuilder.cs` (which `Build` overload owns the result).
- **Mark components read-only unless you write them.** The generated handle names encode the mode, so a decompiled system tells you its intent at a glance.
- **The varargs form cannot express `Any`.** It has `ReadOnly`, `ReadWrite` and `Exclude`, which map to `All` and `None` and nothing else; reach for `EntityQueryDesc`, or construct `EntityQueryBuilder` by hand, which takes an allocator and needs no generator.
  Source: `src/Unity.Entities/Unity.Entities/ComponentType.cs` (the three access modes), `src/Unity.Entities/Unity.Entities/EntityQueryManager.cs` (`Exclude` routed into `None` and everything else into `All`).
- **A fork of a vanilla system inherits the vanilla form**, because the starting point is decompiled source.

### The gates, and the one that ignores your filter

`RequireForUpdate(query)` appends to a list and `ShouldRunSystem` returns false if **any** required query is empty, so repeated calls are ANDed.
`RequireAnyForUpdate(params EntityQuery[])` decomposes the queries you pass and rebuilds them into a single OR query, and is the only way to express "run if either matches".

**The gate tests the query ignoring its filter and its enableable components' state.**
A query narrowed with `SetSharedComponentFilter` still gates on the unfiltered set, so a system gated on a per-bucket query runs on every pass and does nothing on all but one bucket's worth of them.
A query naming an enableable component gates on the entities that carry it, enabled or not, so a gate over `Locked` stays open once everything is unlocked.
That is by design and the game relies on it; it is only a surprise if you expected the gate to save the update.
Source: `src/Unity.Entities/Unity.Entities/SystemState.cs` (`ShouldRunSystem` and the two require calls), `src/Unity.Entities/Unity.Entities/EntityQueryImpl.cs` (`IsEmptyIgnoreFilter` beside the `IsEmpty` that does branch on the filter and the bits).

## Jobs: write per-entity, read per-chunk

**Write new jobs as `IJobEntity`.**
It is the modern replacement for `IJobChunk` and it drops the whole fetch-array-and-index preamble: the parameter list _is_ the query, and `Execute` is called once per entity.
The source generators it needs are wired in as analyzers by the official toolchain, so it compiles and runs in a mod project today.
Source: `src/Unity.Entities/Unity.Entities/IJobEntity.cs` (the empty marker the generator fills in), `%CSII_TOOLPATH%/Mod.props` (the generator wired as an analyzer).

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
Source: `%CSII_UNITYMODPROJECTPATH%/Library/PackageCache/com.unity.entities@%CSII_ENTITIESVERSION%/Unity.Entities/SourceGenerators/Source~/JobEntityGenerator/JobEntitySyntaxReceiver.cs` (the job struct's `partial` test) and `%CSII_UNITYMODPROJECTPATH%/Library/PackageCache/com.unity.entities@%CSII_ENTITIESVERSION%/Unity.Entities/SourceGenerators/Source~/SystemGenerator.Common/PartialSystemTypeGenerator.cs` (the system half).

**That same generator mishandles a file-scoped namespace, so both files take a block namespace.**
It emits its half into the global namespace instead, making the generated type a different type from yours, and the build fails inside generated code on members you never wrote — `cs2-mod-project` carries the cause and a lint that catches it while the file still compiles.
Declaring the job is enough to trigger it; the system half waits until something schedules or a `SystemAPI` call appears.
Source: `%CSII_UNITYMODPROJECTPATH%/Library/PackageCache/com.unity.entities@%CSII_ENTITIESVERSION%/Unity.Entities/SourceGenerators/Source~/Common/TypeCreationHelpers.cs` (the ancestor walk that tests each parent for `SyntaxKind.NamespaceDeclaration`).

**Hold the discrepancy in mind before you open vanilla source.**
The game contains not one `IJobEntity`: every jobified system in it — several hundred — is `IJobChunk`.
That is a fact about the codebase's age rather than about what works, so it is not a reason to follow it; what it decides is what you read and what you fork.
**A fork of a vanilla job starts from decompiled source and therefore arrives as `IJobChunk` before you have written a line of it.**
Source: `src/Game/Game.Simulation/AgingSystem.cs` (the vanilla job form a fork starts from).

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

Source: `src/Unity.Entities/Unity.Entities/ArchetypeChunk.cs` (the chunk-scoped accessors), `src/Unity.Entities/Unity.Entities/IJobChunk.cs` (the `Execute` signature that hands over the chunk and the index), `src/Game/Game.Simulation/AgingSystem.cs` (the chunk-level early exit).

A per-entity `Execute` never holds the chunk, so it has none of the three: no per-chunk early exit — the same test would run up to 128 times more often — no shared-component read, and a parallel-writer sort key it generates for itself rather than the vanilla number.
Where a fork needs none of the three, converting it to a per-entity job is mechanical.
Where it needs one, keep the chunk form for that job rather than emulating the chunk from inside a per-entity `Execute`.

### Burst is a choice, not a default

Bursting every job and bursting none are both workable, so decide it per project.
What the decision costs you is stepping: a Burst-compiled job cannot be stepped, so keep a route back to an unbursted run.
Disable Burst compilation at launch rather than gating `[BurstCompile]` behind a conditional-compilation symbol, which silently ships the mod unbursted when the symbol is defined nowhere.
`performance-and-memory` owns both gates in full.

## Type handles: what they index, and what breaks when one is stale

A `ComponentTypeHandle<T>` caches the type index, the component's size in a chunk, a read-only flag, a lookup cache, and **the global system version**.
`Update(ref SystemState state)` refreshes exactly one of those: the version.

**That single field is the whole point.**
Read-write chunk access stamps the chunk with the handle's version.
A stale handle stamps a stale version, and every change filter downstream — `chunk.DidChange(ref handle, version)`, `SetChangedVersionFilter` — then reports "unchanged" for a chunk you just wrote.
Nothing throws, nothing logs, and the symptom is a system further down the frame that quietly stops seeing your writes.
Source: `src/Unity.Entities/Unity.Entities/ComponentTypeHandle.cs` (the cached fields, and the one `Update` refreshes), `src/Unity.Entities/Unity.Entities/ArchetypeChunk.cs` (the read-write stamp and `DidChange`).

**Nothing throws because this build has no safety system.**
Handles, lookups and `ArchetypeChunk` carry no safety field, and the bounds and aliasing assertions are all conditional on a collections-checks define that is compiled out of the shipped assembly.
A stale handle, an out-of-bounds chunk index, or two jobs writing the same component in parallel produce wrong data or a crash, never a diagnostic.
`performance-and-memory` owns what that means for scheduling; here it means the handle discipline has no backstop.
The same absence covers what a structural change does to data you are already holding: adding or removing a component, destroying an entity or assigning a shared component value can move the entity to another chunk, so a `DynamicBuffer`, a chunk `NativeArray` or a component pointer taken before the change points at the old storage afterwards — reacquire it, because nothing here invalidates it for you: the engine call that would have, in an editor build, has that half compiled out.
Source: `src/Unity.Entities/Unity.Entities/ComponentTypeHandle.cs` and `src/Unity.Entities/Unity.Entities/ArchetypeChunk.cs` (no safety field, and the conditional assertions), `src/Unity.Entities/Unity.Entities/EntityDataAccess.cs` and `src/Unity.Entities/Unity.Entities/ComponentDependencyManager.cs` (the call a structural change routes through, and the body it has left).

**With the generator, the discipline is free.**
`SystemAPI.GetComponentTypeHandle<T>()`, `GetComponentLookup<T>()`, `GetBufferTypeHandle<T>()` and their siblings are rewritten into a generated nested `TypeHandle` struct — one field per handle, assigned once from `OnCreateForCompiler`, and refreshed at the point of use every update.
Skipping the refresh is not something you can do by accident when you use those calls.
Source: `src/Game/Game.Simulation/AgingSystem.cs` (the generated struct, assigned from `OnCreateForCompiler` and refreshed at use), `src/Unity.Entities/Unity.Entities.Internal/InternalCompilerInterface.cs` (the refresh those calls compile into).

**Two hand-rolled idioms exist, and you need one whenever the generator is not writing that code for you** — which is exactly the case in a fork built from decompiled source.
Either way acquire in `OnCreate`: the first `GetComponentLookup` or `GetComponentTypeHandle` call for a type completes the system's tracked jobs on the spot — `performance-and-memory` owns that sync — and the generated struct's create-time assignment is what dodges it.
Source: `src/Unity.Entities/Unity.Entities/SystemState.cs` (the acquisition, and the completion it triggers on a type the system has not read before).

- **Carry the generated struct into the fork and refresh it yourself.**
  The pasted struct is now ordinary source; the generator will not regenerate it, and nothing will refresh its fields.
  Open `OnUpdate` with one explicit call per handle:

  ```csharp
  __TypeHandle.__Game_Citizens_Citizen_RW_ComponentTypeHandle.Update(ref CheckedStateRef);
  __TypeHandle.__Game_City_Population_RO_ComponentLookup.Update(ref CheckedStateRef);
  ```

- **Put the handles on the job and give the job two methods**, `AssignHandles(ref SystemState)` called from the system's `OnCreate` and `UpdateHandles(ref SystemState)` called from its `OnUpdate`.
  This keeps each handle beside the job that reads it and is the cleanest hand-written form.

(VOLATILE: the generated handle-name scheme, which `navigating-the-decompile` states in full, and the compiler-interface method names behind `SystemAPI` — the generated `TypeHandle` structs across `src/Game`.)

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
Source: `src/Unity.Entities/Unity.Entities/ChunkEntityEnumerator.cs`.

## Enableable components: a bit flip, and a filter you did not write

Toggling an enableable component is a bit flip rather than an archetype move, which is the entire reason to declare one.

**A query naming an enableable component matches only entities where it is enabled**, unless the query carries `EntityQueryOptions.IgnoreComponentEnabledState`.
So a query is narrower than it reads whenever one of its components is enableable, and whether a given one is costs a single read — the interface list on that component's own declaration.
**`None` inverts rather than excludes.** An enableable type in `None` does not keep its archetype out: the archetype still matches, and what comes through is every entity carrying the type *disabled*, plus every entity not carrying it at all.
That is exactly "not currently flagged", so it is the query a marker you flip on and off wants, and it is what vanilla writes wherever it asks for "unlocked" — `ComponentType.Exclude<Locked>()`, which the query builder routes into `None`.
`Absent` is the other category and not a substitute for it: it rejects the archetype whatever the bit says, so it answers "never carries this type" where `None` answers "does not carry it right now".
Source: `src/Unity.Entities/Unity.Entities/EntityQueryManager.cs` (the two categories and the `Exclude` routing), `src/Game/Game.City/CityConfigurationSystem.cs` (a vanilla "unlocked" query).

**A flip on an entity whose archetype never carried the type corrupts the chunk list.**
That is the other half of what a `None` query hands you, so the two compose into it: the index lookup returns `-1` on a miss, and every write route — `EntityManager`, `ComponentLookup<T>`, the chunk handle, and the command buffer at playback — indexes the archetype's arrays with that `-1` and writes over the archetype's own chunk-list entry.
Reading the bit is no safer except off the chunk handle, the one surface that tests the miss and returns `false`; the other two read out of bounds and hand you an arbitrary answer.
So test membership before either, which is true whatever the bit says: `HasComponent<T>` on `EntityManager` and `ComponentLookup<T>`, `Has<T>` off the chunk handle as above.
An entity the query returned because it never carried the type needs `AddComponent`, not a flip.
Source: `src/Unity.Entities/Unity.Entities/ChunkDataUtility.cs` and `src/Unity.Entities/Unity.Entities/ArchetypeChunkData.cs`.
A buffer element can carry `IEnableableComponent` the same way, which is what the enabled-buffer helpers below exist for.
Some enableable components carry a disabled state a reader would never guess from the name:

- **`PrefabData` disabled means "obsolete prefab".** The loader disables it on prefabs a save references but the current install no longer has, and the prefab system uses its enabled state as the "does this prefab still exist" test when writing a save. So `WithAll<PrefabData>()` gives you live prefabs only — almost always what you want, but it is a filter you did not write, and it explains a prefab count that does not match the installed mod list.
  Source: `src/Game/Game.Serialization/ResolvePrefabsSystem.cs` (the loader's disables) and `src/Game/Game.Prefabs/PrefabSystem.cs` (the enabled state read as the existence test).
- **`Locked` disabled means "unlocked".** So a progression query on `WithAll<Locked>()` silently returns only what is _still_ locked, and unlocking is not the bit flip that implies. `city-state-and-progression` depends on this.
  Source: `src/Game/Game.Prefabs/UnlockSystem.cs`.

Toggle from a job through the command buffer, as the vanilla aging system does at the child-to-teen transition:

```csharp
m_CommandBuffer.SetComponentEnabled<BicycleOwner>(unfilteredChunkIndex, citizen, true);
```

That route defers the flip to the barrier, so the rest of the job's walk and every system before the playback still read the old value.
`ComponentLookup<T>.SetComponentEnabled` and the chunk handle's own `SetComponentEnabled` flip it inside the job instead, which is what you want when a later step of the same job has to see the new state.
The cost of choosing them is that you own the ordering: both flip the mask word with a compare-and-swap loop, so parallel writers do not lose each other's bits — but two threads writing opposite values to one entity's bit resolve last-writer-wins, and nothing reports it.
That cover is theirs alone, so do not carry it across to the third route: the `EnabledMask` an `IJobChunk` takes off `chunk.GetEnabledMask` writes the word plainly, and two threads flipping different entities of one chunk through it can lose an update.

**Unlocking is not that call.**
`Locked` is flipped on the main thread through the `EntityManager`, inside the unlock system, which also raises the `Unlock` event that the UI, achievement and prefab-requirement systems query.
The milestone systems are what _raise_ that event rather than watch it, so reaching for one to observe an unlock finds no subscription.
So unlock by creating an event entity on the archetype the game builds — `CreateArchetype(ComponentType.ReadWrite<Game.Common.Event>(), ComponentType.ReadWrite<Unlock>())` in `OnCreate` — setting `new Unlock(prefabEntity)` on it, and letting the unlock system do the flip.
An event created from the archetype alone carries `Entity.Null` as its prefab, which the unlock system skips without a log line.
**`Game.Common.Event` is what puts the entity in the destroy set**, so one carrying `Unlock` alone is processed and then never destroyed, re-matching every consumer's query for the rest of the session.
Flipping the bit yourself leaves all of them unnotified, with nothing logged.
Source: `src/Game/Game.Prefabs/UnlockSystem.cs` (the main-thread flip, the event archetype, and a null prefab skipped without a log line), `src/Game/Game.Common/PrepareCleanUpSystem.cs` (`Event` in the destroy query).

(VOLATILE: what a disabled `PrefabData` and a disabled `Locked` mean — the prefab system and the loader for the first, the unlock system for the second.)

## Command buffers: twelve named barriers, and one contract

The game exposes command buffers as **named barrier systems**, not as raw `EntityCommandBuffer`s.
Resolve the one you want once in `OnCreate` with `World.GetOrCreateSystemManaged<T>()` and hold it in a field of its concrete type.

| Barrier | Plays back |
| --- | --- |
| `EndFrameBarrier` | front of the main loop — see the window rule below |
| `ModificationBarrier1` | end of `Modification1` |
| `ModificationBarrier2` | end of `Modification2` |
| `ModificationBarrier2B` | end of `Modification2B` |
| `ModificationBarrier3` | end of `Modification3` |
| `ModificationBarrier4` | end of `Modification4` |
| `ModificationBarrier4B` | end of `Modification4B` |
| `ModificationBarrier5` | end of `Modification5` |
| `ModificationEndBarrier` | end of `ModificationEnd` |
| `ToolOutputBarrier` | end of `ToolUpdate` |
| `ToolReadyBarrier` | end of `PostTool` |
| `DeserializationBarrier` | front of `Deserialize`'s back band |

`DeserializationBarrier` is the one that does not play back at the end of its phase: it is the first `UpdateAfter` registration in `Deserialize`, so it plays back before the rest of that band rather than after it, and a system placed there cannot ask it for a command buffer.
`save-serialization` maps that band, in the census file its entry points to, and states what a system placed there should use instead.

A thirteenth type, `AudioEndBarrier`, exists in the assembly and is registered in no phase.
It has a companion opener like the others, and that opener is unregistered too, so nothing ever re-opens the barrier after its first playback attempt closes it.
Reach for one of the twelve instead.

**The contract is three calls, and each of the three has a failure mode.**

1. **`CreateCommandBuffer()` once per `OnUpdate`, not once per job.** Every call appends another buffer to the barrier's flush list.
2. **`AddJobHandleForProducer(handle)` after scheduling.** Without it the barrier plays back while your job is still writing into the buffer.
3. **Playback runs in list order and then rewinds the allocator**, so a buffer is single-playback and the handle you got is dead after the barrier updates. Recording into a dead one is unguarded here: the engine refuses it in an editor build and that refusal is compiled out with the rest of the safety system, so a cached buffer field records into rewound memory and nothing objects. (UNVERIFIED: whether such a write is a silent no-op, a corruption of whatever the allocator has since handed out, or a fault — one run with a barrier's buffer held in a field across two updates settles it.)

Source: `src/Unity.Entities/Unity.Entities/EntityCommandBufferSystem.cs` (the pending list, the producer handle, and the playback that rewinds the allocator), `src/Unity.Entities/Unity.Entities/EntityCommandBuffer.cs` (the single-playback policy, and the emptied recording guard).

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
Source: `src/Unity.Entities/Unity.Entities/EntityCommandBuffer.cs` (the `sortKey` parameter, and the ascending chain merge at playback).

**Writing to a barrier outside its window throws, loudly.**
Each barrier closes itself immediately before playing back, and a companion system re-opens it; creating a buffer while it is closed raises `Trying to create EntityCommandBuffer when it's not allowed!`.
This is one of the few places in this ECS where the failure is an exception rather than silence, so trust it.
The other on this surface is calling `Playback` on a buffer twice, which throws here for real.
Source: `src/Game/Game/SafeCommandBufferSystem.cs` and `src/Game/Game/AllowBarrier.cs` (the close, the message and the re-open), `src/Unity.Entities/Unity.Entities/EntityCommandBuffer.cs` (the second-playback throw).

**Write only to the barrier belonging to the phase you are running in.**
That is the general rule, and it falls out of where each barrier's opener sits.
Eleven of the twelve open at the start of their own phase and play back in its closing band — the deserialization barrier ahead of the post-load wrappers that follow it — so each is open for the duration of that one phase and shut from its playback until the phase runs again.
For most of the eleven that means the next frame; for the deserialization barrier it means the next load, since its phase fires once per load rather than every frame.
Where the opener and the playback sit within the phase, and what that costs a system registered beside them, is `mod-lifecycle-and-ordering`.
Source: `src/Game/Game.Common/SystemOrder.cs` (each barrier's opener and playback registrations).

**`EndFrameBarrier` is the exception, and its window is the widest rather than the narrowest.**
Its opener and its playback sit far apart inside the main loop rather than bracketing one phase, so it is open from partway through the frame, across the phases that run after that, and on to the next frame.
Simulation systems use it freely and that is where nearly all of vanilla's use of it sits.
What it does not cover is the front of the frame: **an `OnUpdate` body running before the opener — the modification, tool, raycast, prefab-update and deserialize phases — cannot create an `EndFrameBarrier` command buffer**, and uses its own phase's barrier instead.
This rule is about `OnUpdate` bodies, and a lifecycle hook such as `OnGameLoadingComplete` fires outside the frame's phase walk entirely.
(UNVERIFIED: whether a buffer created from a lifecycle hook lands inside the open window or throws against a closed barrier — the hook's invocation site in `GameSystemBase` read against the barrier's opener and playback registrations in the vanilla system-order class, or one run of the game with a buffer created there.)
Source: `src/Game/Game.Common/SystemOrder.cs` (the opener's position in the main loop, against the systems that drive the earlier phases).

**One crack in the gate, and it is in the type system.**
The safety check lives on a method that _shadows_ the base `EntityCommandBufferSystem.CreateCommandBuffer()` rather than overriding it, and the base method is not virtual.
A call through a variable typed as the base class therefore binds to the base method and skips the check entirely — no exception, and a buffer that flushes at an unpredictable time instead.
So a mod that stores barriers in an `EntityCommandBufferSystem`-typed dictionary, or hands one to a generic helper, has traded a loud failure for a silent one.
Hold the concrete barrier type everywhere.
Source: `src/Game/Game/SafeCommandBufferSystem.cs` (the shadowing `new` method) and `src/Unity.Entities/Unity.Entities/EntityCommandBufferSystem.cs` (the non-virtual base method).

(VOLATILE: the twelve barrier type names and the exception message string — the vanilla system-order class, and the safe command buffer base.)

## The universal tags, and the protocol they carry

Six zero-field components form a frame-scoped change protocol — `Created`, `Updated`, `Applied`, `EffectsUpdated`, `BatchesUpdated` and `PathfindUpdated` — added and stripped inside a frame by a preparation-and-cleanup pair at the end of the main loop.
Three of the rules they carry are ones a mod is wrong without.

**Tag the graphics, or your change is invisible.**
If you change anything visible on an entity and do not add `BatchesUpdated`, the renderer keeps drawing the old batch, with no error anywhere — and tag the sub-objects too, since a building tagged alone renders with stale props.
Source: `src/Game/Game.Rendering/PreCullingSystem.cs` (the read), `src/Game/Game.Simulation/CityServiceUpkeepSystem.cs` (a sub-object tagged on its own).

**Exclude the preview, or you will count it as real.**
`Temp` is the tool-preview tag and nearly every game query excludes it; `None = { Deleted, Temp }` is the canonical pair, and a query that forgets it sees the player's uncommitted hover preview as a building that exists.
Source: `src/Game/Game.Tools/Temp.cs`, `src/Game/Game.Simulation/AgingSystem.cs` (the canonical `None` pair).

**Delete by tag, not by `DestroyEntity`.**
`Deleted` means the entity dies later in the frame, and that gap is the point: every system holding a reference gets a window to query `WithAll<Deleted>()` and unhook. Reserve `DestroyEntity` for entities nothing else can be holding.
Source: `src/Game/Game.Common/PrepareCleanUpSystem.cs`, `src/Game/Game.Common/CleanUpSystem.cs`.

(VOLATILE: the six frame-scoped tag names, `Deleted` and `Temp` above — each tag's own declaration, and the cleanup system's query for the frame-scoped set.)

[The universal tags, one by one](universal-tags.md) is the catalogue — reach for it when you need to know what a specific tag asks for, which tags survive a frame and which a save, or why a tag you added took an extra frame to be seen.

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

Source: `src/Game/Game.Modding/ModManager.cs` (the reflection pass after a mod assembly loads) and `src/Unity.Entities/Unity.Entities/TypeManager.cs` (what it accepts, the double-registration throw, and the generic attribute).

**A name matching a vanilla component is a different type and does not clash**, since the namespace is part of the identity.
It is still expensive: the game absorbs mod concepts across versions, so a name that was unique when a mod shipped can collide with a vanilla type a later patch introduces, and every touch of either then needs full namespace qualification to stay readable.
Prefix your components rather than naming them after the concept alone.

**Runtime cost, by kind:**

| Kind | Cost |
| --- | --- |
| Zero-field `IComponentData` | No per-entity bytes at all. The cost is the extra archetype: adding or removing it moves every affected entity between chunks. |
| `IComponentData` with fields | Its size, per entity, in every chunk of every archetype carrying it. |
| `IBufferElementData` | `InternalBufferCapacity` elements reserved inline in the chunk, spilling to the heap the first time the length exceeds them and staying there until something asks for it back. Default capacity is 128 bytes' worth of elements. |
| `ISharedComponentData` | Nothing per entity, and one set of chunks per distinct value. Assigning a value is a structural change — the entity moves to a chunk carrying it — which is why vanilla writes one through a command buffer rather than the `EntityManager`. |
| `IEnableableComponent` | Its own bits in the chunk's enabled masks, and a toggle that is not a structural change. |
| `ICleanupComponentData` | A residue entity that outlives `DestroyEntity` until you remove the component. |

`[InternalBufferCapacity(0)]` means **never inline**: every non-empty buffer becomes a heap allocation and an empty one allocates nothing, which keeps chunks dense when most entities carry an empty buffer.
Split the decision deliberately: `(0)` for a sparsely-populated buffer, and a small explicit capacity for one that almost always holds one to three elements.
Pick a capacity the buffer will not exceed rather than a typical one: shrinking the length leaves the payload on the heap, so a buffer that overflows once pays the heap allocation and the reserved inline bytes together until something asks for it back.
`performance-and-memory` owns that trade and the call that asks.
Source: `src/Unity.Entities/Unity.Entities/TypeManager.cs` (the default capacity, and the reservation paid per entity slot), `src/Unity.Entities/Unity.Entities/BufferHeader.cs` (growth that never takes the inline arm) and `src/Unity.Entities/Unity.Entities/DynamicBuffer.cs` (`Clear` against `TrimExcess`).

**Save cost is decided by one interface and nothing else.**
The serializer library walks every type the type manager knows and registers a serializer for each that implements one of two interfaces; a component implementing neither is simply not written.
Source: `src/Colossal.Core/Colossal.Serialization.Entities/ComponentSerializerLibrary.cs` (the walk, and the branch that picks each serializer).

| Declares | Result |
| --- | --- |
| `IEmptySerializable` | Presence is persisted; there is no payload. The whole declaration for a tag that must survive a save. |
| `ISerializable` | Your `Serialize<TWriter>` and `Deserialize<TReader>` are called. |
| Either, plus `IEnableableComponent` | An enableable-aware serializer, so the enabled bit persists too. |
| Either, plus `ISerializeAsEnabled` | The plain serializer instead: the disabled state is **not** persisted. |
| Neither | Not written. Reconstruct it on load or lose it. |

So a persisted tag is one line:

```csharp
public struct MyPloppedMarker : IComponentData, IEmptySerializable { }
```

**The library rebuilds after a mod assembly loads**, in the same step that registers the types, so a mod component becomes saveable purely by implementing the interface.
Implementing neither and rebuilding the component on load is the cheaper and safer default: a component in a save is a compatibility obligation forever.
The versioning discipline inside `Serialize` and `Deserialize` — writing a version number first and branching on it when reading — belongs to `save-serialization`, and you want it before the first release, not after.
Source: `src/Game/Game.Modding/ModManager.cs` (the dirty flag set beside the type registration) and `src/Game/Game.Serialization/SerializerSystem.cs` (the re-initialize it triggers).

(VOLATILE: the serializer selection above — the component serializer library.)

## The helper extensions the game already ships

`Colossal.Entities.EntitiesExtensions` is a static class of extension methods over `EntityManager`, `ComponentLookup<T>` and `BufferLookup<T>` that collapse the has-then-get dance into one call: `TryGetComponent`, `TryGetBuffer`, `TryGetSharedComponent`, `HasEnabledComponent`, `HasEnabledBuffer`, `TryGetEnabledComponent`, `TryGetEnabledBuffer`.

```csharp
using Colossal.Entities;

if (EntityManager.TryGetComponent(entity, out PrefabRef prefabRef))
{
    // …
}
```

The `Enabled` half of that list is not a convenience variant: `TryGetComponent` and `TryGetBuffer` test archetype membership alone and succeed on a disabled component, handing back its stored value, while `HasEnabledComponent`, `HasEnabledBuffer`, `TryGetEnabledComponent` and `TryGetEnabledBuffer` are the four that consult the bit — so pick by whether the component is enableable.
Source: `src/Colossal.Core/Colossal.Entities/EntitiesExtensions.cs`.

These ship with the game rather than coming from anywhere else, so they cost a mod no dependency at all.
Reach for them before writing your own.

## What this reference hands to others

`mod-lifecycle-and-ordering` for which phase a system belongs in and how to register it there — everything above assumes that decision is already made.
`performance-and-memory` for allocators, job dependencies, and what the chunk geometry and buffer capacity above mean for a frame budget.
`save-serialization` for the save format and the versioning discipline inside `Serialize` and `Deserialize`.

Every mechanics reference sits on top of this one.
`citizens-and-households` exercises it most directly, since the citizen aging system is the canonical shape: a query excluding `Deleted` and `Temp`, a buffer handle walked per chunk, a scattered-write lookup, and `EndFrameBarrier` used to add, remove and toggle components.
`zoning-buildings-and-land-value` and `city-services-and-coverage` need the `BatchesUpdated` rule most, because both are about things the player looks at.
`roads-and-traffic` needs `Owner` and `Temp` more than any other area.
`city-state-and-progression` needs the `Locked` trap.
`simulation-time-and-units` owns what a bucket is worth in simulated time; the buckets themselves are [update-frame-buckets.md](update-frame-buckets.md)'s.
`economy-and-companies`, `utilities-and-flow-networks`, `transportation-and-vehicles` and `environment-and-pollution` each need the query, job and barrier material without needing anything unique from it.
