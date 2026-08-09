# ECS in this game: writing ECS code that matches this codebase

**Baseline.** Decompiled game version 1.6.0f1. Mod corpus (20 repositories under `C:\Users\Morgan\Documents\Projets\cs2-third-party-mods\`) read 2026-08-02. Wiki fetched live 2026-08-02 — the bot challenge did not fire, so `ECS - Entity Component System`, `Queries` and `Common ECS Components` are cited from the live pages rather than through `survey-wiki-inventory.md`'s snapshot.
A fourth source appears in two findings: the **installed official modding toolchain** on the maintainer's machine, at `C:\Users\Morgan\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Modding\`. It carries no version of its own and is not part of the decompile; it is cited by absolute path because nothing in this repository or the decompile holds it, and it is the only source that settles what a mod project can compile.

## Findings

### The five component kinds, and how unevenly the game uses them

Counted over `src/Game/`, excluding `Game/Unity.Entities.CodeGeneratedRegistry/AssemblyTypeRegistry.cs` (a generated 16,000-line type list that would double every count), by files declaring each interface:

| Kind | Files in `src/Game/` | Files in the corpus (of 20 repos) |
| --- | --- | --- |
| `IComponentData` | 1007 | 161, in 17 repos |
| `IBufferElementData` | 242 | 42, in 7 repos |
| `ISharedComponentData` | **5** | **0** |
| `IEnableableComponent` | 13 | 9, in 2 repos |
| `ICleanupComponentData` | **0** | 3 declarations, in 1 repo |

Three repositories declare no component at all: `ExtraAssetsImporter`, `FindIt-CSII` and `LineTool-CS2`.

Two of those rows are the finding.

**`ISharedComponentData` exists in five places and no mod uses it.** The five: `src/Game/Game.Net/ArrowMaterial.cs:6`, `src/Game/Game.Net/CoverageServiceType.cs:6`, `src/Game/Game.Net/LabelMaterial.cs:6`, `src/Game/Game.Prefabs/BuildingSpawnGroupData.cs:6`, and the load-bearing one, `src/Game/Game.Simulation/UpdateFrame.cs:6`. A corpus-wide grep for `ISharedComponentData` returns zero declarations across all 20 repositories.

**`ICleanupComponentData` appears nowhere in the game and only in `CS2-WriteEverywhere`.** Its three declarations are `CS2-WriteEverywhere/BelzontWE/Components/WETemplateForPrefab.cs:5`, `Components/WETextData/WETextDataMaterial.cs:14` and `Components/WETextData/WETextDataMesh.cs:13`, the last two also `IDisposable`; a fourth file, `CS2-WriteEverywhere/BelzontWE.Tests/Components/GamePropComponentsTests.cs:30-31`, asserts the interface by reflection in a unit test, which is the only such assertion in the corpus. The reason is visible in the struct: `WETextDataMesh` holds a `GCHandle` (`WETextDataMesh.cs:17`) — a managed Unity mesh pinned inside a blittable component. A cleanup component survives `DestroyEntity`: `EntityComponentStore` moves the entity into a `CleanupResidueArchetype` holding only `CleanupEntity` plus the cleanup components (`src/Unity.Entities/Unity.Entities/EntityComponentStore.cs:3656-3681`), and the entity only truly dies once that component is removed (`:3710-3730`). That is the one correct way to own an unmanaged or managed handle from a component. The mod pairs it with a disposal system registered in `SystemUpdatePhase.Cleanup` (`CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:81`).

The game's own equivalent of "clean up after me" is not a cleanup component at all: it is the `Deleted` tag plus a frame of grace (below).

Rots: the five shared-component type names, and the file paths of the three cleanup components — re-grep `ISharedComponentData` and `ICleanupComponentData` over `src/Game/`.

### Every component the game declares also implements `IQueryTypeParameter`, and it costs nothing

`IQueryTypeParameter` is an empty marker with no members (`src/Unity.Entities/Unity.Entities/IQueryTypeParameter.cs:3-5`). 996 files under `src/Game/` name it against 1007 declaring `IComponentData`, and the 17-file gap is entirely files that merely _reference_ `IComponentData` in a generic constraint (`src/Game/Game.Prefabs/PrefabSystem.cs`, `src/Game/Game.Tools/ApplyNetSystem.cs` and 15 like them) rather than declaring a component. So: every component declaration in the game carries it.

It is the constraint on `SystemAPI.Query<T1>()` (`src/Unity.Entities/Unity.Entities/SystemAPI.cs:110`, `where T1 : IQueryTypeParameter`) and on nothing else. The game never calls `SystemAPI.Query` (below), so the marker buys the game nothing and is pure house style — but a mod component without it cannot be used in a `SystemAPI.Query<>` foreach, and 51 corpus component declarations across 10 repositories copy the convention anyway (`PlopTheGrowables/Code/Systems/PloppedBuilding.cs:15`, `Tree_Controller/Tree_Controller/Components/LumberResource.cs:13`).

Rots: nothing. This is a marker interface in the Entities package.

### Archetypes are declared through prefabs, and `CreateArchetype` is for events

There are two archetype surfaces and a mod meets them in different places.

**The prefab-instance archetype.** `PrefabBase.GetArchetypeComponents(HashSet<ComponentType>)` and `GetPrefabComponents(HashSet<ComponentType>)` are the two abstract members every `ComponentBase` implements (`src/Game/Game.Prefabs/ComponentBase.cs:87-89`). The base `PrefabBase` seeds each set with one type: `PrefabData` and `LoadedIndex` for the prefab entity, `PrefabRef` for the instances (`src/Game/Game.Prefabs/PrefabBase.cs:362-375`). `ArchetypePrefab.RefreshArchetype` then walks every attached `ComponentBase`, unions their `GetArchetypeComponents` output, **adds `Created` and `Updated` unconditionally**, and calls `EntityManager.CreateArchetype` once (`src/Game/Game.Prefabs/ArchetypePrefab.cs:26-40`). `BuildingPrefab` overrides the same method and writes the result into `ObjectData.m_Archetype` instead, with three extra types when the prefab carries `BuildingUpgradeElement` (`src/Game/Game.Prefabs/BuildingPrefab.cs:58-80`).

So a mod that wants a component on **every instance of a prefab** overrides `GetArchetypeComponents`; a mod that wants it on **the prefab entity** overrides `GetPrefabComponents`. 30 such overrides exist in the corpus, in `CS2-Platter`, `RoadBuilder-CSII`, `FindIt-CSII`, `Traffic` and `Water_Features`. The readable pair is `CS2-Platter/Platter/Prefabs/ParcelPrefab.cs:37-52`, which adds `ParcelData` and `PlaceableObjectData` to the prefab entity and `Parcel` and `ParcelSubBlock` to every instance, each override calling `base` first.

**`EntityManager.CreateArchetype` directly.** 229 call sites in `src/Game/`, and the overwhelming majority build a **one-shot event archetype** in `OnCreate`, stash it in a field, and spawn through a command buffer inside a job. `src/Game/Game.Prefabs/ProcessingRequirementSystem.cs:116` builds `{Event, Unlock}`; `src/Game/Game.City/DevTreeSystem.cs:122` the same pair; `src/Game/Game.Citizens/HouseholdAndCitizenRemoveSystem.cs:370` builds `{Event, RentersUpdated}`; `src/Game/Game.Events/AddHealthProblemSystem.cs:417` builds `{AddEventJournalData, Event}`. The spawn is `m_CommandBuffer.CreateEntity(unfilteredChunkIndex, m_UnlockEventArchetype)` followed by `SetComponent` (`src/Game/Game.Prefabs/ProcessingRequirementSystem.cs:50-51`). 402 `EntityArchetype m_*` fields exist under `src/Game/`, most of them cached on prefab-data components (`src/Game/Game.Prefabs/ArchetypeData.cs:7`, `NetData.cs:9/11`, `NetLaneArchetypeData.cs:8-14`) and read from a job through a `ComponentLookup` (`src/Game/Game.Buildings/RoadConnectionSystem.cs:1325/1428`).

The pattern is worth stating as a rule: **`CreateArchetype` in `OnCreate`, never in `OnUpdate`** — every game call site does it once, because the call takes a managed `EntityManager` and cannot run inside a job.

Rots: the archetype-carrying prefab-data type names (`ArchetypeData`, `ObjectData`, `NetData`, `NetLaneArchetypeData`) and the `Created`/`Updated` seeding at `ArchetypePrefab.cs:36-37`.

### Chunks: what a mod actually touches

The wiki says a chunk is "a 16kb block of data that contains arrays of components" (https://cs2.paradoxwikis.com/ECS_-_Entity_Component_System). **Verdict: verified, with a correction worth carrying.** `Chunk.kChunkSize = 16384` (`src/Unity.Entities/Unity.Entities/Chunk.cs:33`), but the usable component area is `kChunkBufferSize = 16320` after a 64-byte header (`:35/39`), and a chunk holds at most `kMaximumEntitiesPerChunk = 128` entities (`:37`) regardless of how small the archetype is.

That 128 is not trivia: it is exactly why the chunk-enabled mask is a `v128` — two 64-bit words, one bit per entity (`src/Unity.Entities/Unity.Entities/ChunkEntityEnumerator.cs:13-14`, `:36-60`).

Three chunk-level operations appear in mod-reachable game code:

- **`chunk.GetNativeArray(ref handle)`** — the workhorse. It returns a `NativeArray<T>` aliasing the chunk's storage, of length `chunk.Count` (`src/Unity.Entities/Unity.Entities/ArchetypeChunk.cs:652-663`). If the component is not in the chunk's archetype it returns a **length-zero array with `Allocator.Invalid`** rather than throwing (`:656-660`), which is what makes an optional-component read safe and an unguarded index read a silent out-of-bounds.
- **`chunk.Has(ref handle)`** — presence test, used to branch per chunk instead of per entity. `src/Game/Game.Simulation/UpdateGroupSystem.cs:123-140` dispatches an entire chunk to one of nine counters purely on `chunk.Has(ref types.m_MovingType)`, `chunk.Has(ref types.m_PlantType)` and so on.
- **`chunk.GetSharedComponent(handle)`** — reads the chunk's single shared value (`ArchetypeChunk.cs:527-534`). The value lives once per chunk, at `archetype->Chunks.GetSharedComponentValue(index, m_Chunk.ListIndex)` (`:482-483`), not once per entity. **This is the whole reason a shared component exists, and the whole reason to be careful with one:** every distinct value is a distinct set of chunks, so a shared component with many values fragments the archetype into many part-full 16 KB chunks.

**The game's one real use of a shared component is chunk partitioning for the simulation.** `UpdateFrame` carries a single `uint m_Index` (`src/Game/Game.Simulation/UpdateFrame.cs:6-8`) and is assigned by `UpdateGroupSystem` through a command buffer, `m_CommandBuffer.SetSharedComponent(entity, new UpdateFrame(index))` (`src/Game/Game.Simulation/UpdateGroupSystem.cs:328/339/416/425`), with the index taken from the prefab's `UpdateFrameData.m_UpdateGroupIndex` where the prefab declares one (`src/Game/Game.Prefabs/UpdateFrameData.cs:5-7`, read at `UpdateGroupSystem.cs:460-462`) and otherwise load-balanced into the least-loaded bucket of the entity's family (`UpdateGroupSystem.cs:458-473`). The count is per family — nine load-balanced families at 16 and the dispatch families at 4/8/16/32 (`src/Game/Game.Simulation/SimulationUtils.cs:11-57`); `RequiredComponentSystem`'s `new UpdateFrame((uint)(i & 0xF))` (`src/Game/Game.Serialization/RequiredComponentSystem.cs:1627/1666`) is a version-gated plant-and-fence migration seed, not the model.

Systems then consume it in one of two ways, and both are worth a mod's attention:

- **Filter the query**: `m_BuildingGroup.SetSharedComponentFilter(new UpdateFrame(updateFrame))` (`src/Game/Game.Simulation/BuildingUpkeepSystem.cs:1151`), 37 such calls under `src/Game/`. The filtered query then only visits chunks whose shared value matches, so one bucket's worth of the buildings is touched per pass (the building family's count is 16).
- **Test inside the job**: `if (chunk.GetSharedComponent(m_UpdateFrameType).m_Index != m_UpdateFrameIndex) return;` (`src/Game/Game.Simulation/AgingSystem.cs:68-71`), which does the same thing at chunk granularity from inside `IJobChunk.Execute`.

**Corpus: 2 `SetSharedComponentFilter` calls in 20 repositories.** Mods that fork a vanilla per-update-frame system reproduce the in-job test instead, because they copy the vanilla job body verbatim.

Rots: `UpdateFrame`, `UpdateFrameData.m_UpdateGroupIndex`, the sixteen-bucket constant `0xF`, and `kMaximumEntitiesPerChunk` — re-read `src/Game/Game.Simulation/UpdateGroupSystem.cs` and `src/Unity.Entities/Unity.Entities/Chunk.cs:33-39`.

### The query APIs: the game hand-builds, the corpus uses the builder

Four APIs exist. Counts under `src/Game/`, excluding the generated registry:

| API | `src/Game/` | Corpus |
| --- | --- | --- |
| `GetEntityQuery(ComponentType…)` | 1471 | 97 |
| `GetEntityQuery(new EntityQueryDesc{…})` | 412 | 154 |
| `SystemAPI.QueryBuilder()` | 0 literal / 104 generated | 300 |
| `SystemAPI.Query<T>()` (the foreach form) | **0** | **0** |
| `Entities.ForEach` | **0** | **0** |

**`SystemAPI.QueryBuilder()` does not survive decompilation, which is why the game looks like it never uses it.** Every `SystemAPI` member throws `InternalCompilerInterface.ThrowCodeGenException()` in the shipped assembly (`src/Unity.Entities/Unity.Entities/SystemAPI.cs:105-107` for `QueryBuilder`, and identically for `Query<T1..T7>` at `:110-148`, `GetComponentLookup` at `:145`); the Roslyn source generator rewrites each call site into a cached field. In the decompile that rewriting shows up as a `private EntityQuery __query_<hash>_<n>` field, an `__AssignQueries(ref SystemState)` method building it with `new EntityQueryBuilder(Allocator.Temp).WithAll<T>().WithOptions(EntityQueryOptions.IncludeSystems).Build(ref state)`, and `OnCreateForCompiler` calling it (`src/Game/Game.Simulation/AdjustElectricityConsumptionSystem.cs:379-383`, `:480-495`, `:497-502`).

**All 104 of the game's generated queries are singleton accessors.** A sweep of what each `__query_` field is used for returns 97 `GetSingleton`, 12 `TryGetSingleton`, 4 `GetSingletonEntity`, 3 `GetSingletonBuffer`, 1 `HasSingleton`, and nothing else. So `SystemAPI` in the game means `SystemAPI.GetSingleton<T>()` and nothing more (`AdjustElectricityConsumptionSystem.cs:439-440` is the shape): **every iteration query in the game is hand-built with `GetEntityQuery`.** 736 files under `src/Game/` carry a `private TypeHandle __TypeHandle` field; only 66 of them also carry a `__query_` field.

**The corpus went the other way.** 13 of 20 repositories use `SystemAPI.QueryBuilder()`, 300 call sites in total, and five repositories use it exclusively:

| Repo | `QueryBuilder` | `EntityQueryDesc` | `GetEntityQuery(ComponentType…)` |
| --- | --- | --- | --- |
| Recolor | 57 | 5 | 0 |
| CS2-NetworkTools | 46 | 0 | 0 |
| CS2-Platter | 39 | 0 | 0 |
| RoadBuilder-CSII | 36 | 0 | 0 |
| InfoLoom | 28 | 3 | 5 |
| CS2-MoveIt | 20 | 0 | 0 |
| Tree_Controller | 19 | 3 | 1 |
| BetterBulldozer | 18 | 13 | 0 |
| Anarchy | 13 | 19 | 0 |
| Traffic | 9 | 37 | 8 |
| FindIt-CSII | 7 | 1 | 3 |
| PlopTheGrowables | 6 | 0 | 3 |
| Water_Features | 2 | 15 | 1 |
| CS2-WriteEverywhere | 0 | 22 | 0 |
| Time2Work | 0 | 30 | 60 |
| AreaBucket | 0 | 6 | 3 |
| NodeController | 0 | 0 | 5 |
| ExtraDetailingTools | 0 | 0 | 5 |
| LineTool-CS2 | 0 | 0 | 2 |
| ExtraAssetsImporter | 0 | 0 | 0 |

**What decides between them, stated from the evidence rather than from taste.**

1. **`SystemAPI.QueryBuilder()` needs the source generators, and they only run inside a mod project.** `Mod.props` wires twelve analyzer DLLs from the Entities package, `SystemGenerator.SystemAPI.QueryBuilder.dll` among them (`C:\Users\Morgan\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Modding\Mod.props:63-74`), and `Mod.targets` hard-errors if that directory is empty (`Mod.targets:85-87`). The consequence is one-directional and load-bearing: **a call to any `SystemAPI` member that the generator did not rewrite throws at runtime**, because the shipped body is a throw. That is what makes `partial` mandatory on the system class — `InfoLoom/InfoLoom/Systems/DemographicsData/Demographics.cs:22` is `public partial class Demographics : GameSystemBase` — since the generator emits `OnCreateForCompiler` into the other half.
2. **`GetEntityQuery(new EntityQueryDesc{…})` is the only form that expresses `Any`.** `EntityQueryDesc` has `All`, `Any`, `None` and `Options` arrays; the varargs `GetEntityQuery(ComponentType…)` form has only `ReadOnly`/`ReadWrite`/`Exclude`, which maps to `All` and `None`. The builder has `WithAny<>` too, so this only forces the desc form where the generator is unavailable.
3. **A fork of a vanilla system inherits the vanilla form.** `Time2Work` has 60 `GetEntityQuery(ComponentType…)` and 30 `EntityQueryDesc` and zero builder calls, because its systems are copies of decompiled vanilla ones.

The wiki teaches both forms and is right about both (https://cs2.paradoxwikis.com/Queries). **Verdict: its two claims check out.** "Create queries in the system's `OnCreate()`" matches the game universally — `RefreshArchetype` aside, every `GetEntityQuery` under `src/Game/` sits in `OnCreate` or in the generated `__AssignQueries`. "Use ReadOnly by default; ReadWrite only when modifying" matches the generated handle names, which encode the mode: `__Game_Citizens_TravelPurpose_RO_ComponentLookup` against `__Game_Citizens_Citizen_RW_ComponentLookup` (`src/Game/Game.Simulation/AgingSystem.cs:155/160`).

One wiki claim is off by a component. The `Queries` page says "The Mod Post Processor automatically generates queries in `OnCreateForCompiler()` when using SystemAPI methods." **Verdict: the generation is done by the Roslyn source generators, not the post-processor.** `Mod.props:63-74` adds the generators as `<Analyzer>` items, so they run inside the C# compile; the post-processor is a separate `Exec` after build, invoked as `PostProcess "$(TargetPath)" -u <unity project> -r <refs> -p Windows -p macOS -p Linux -d -v` (`Mod.targets:96/100-103`), which is the Burst AOT and IL post-processing pass. The observable result the page describes is real; only the attribution is wrong. It matters because the two fail differently: a missing generator is a compile-time error from `Mod.targets:87`, a missing post-processor step costs Burst.

Rots: the analyzer DLL list and the post-processor argument string in `Mod.props`/`Mod.targets`; they belong to the toolchain, which versions separately from the game.

### The query gates, and the one that ignores your filter

`RequireForUpdate(query)` appends to a list (`src/Unity.Entities/Unity.Entities/SystemState.cs:639-646`); `ShouldRunSystem()` returns false if **any** required query is empty (`:420-438`). So repeated calls are ANDed. `RequireAnyForUpdate(params EntityQuery[])` instead decomposes the given queries and rebuilds them into a single OR query (`:656-690`), which is the only way to express "run if either matches".

**The gate uses `IsEmptyIgnoreFilter` (`:427`).** A query narrowed with `SetSharedComponentFilter` still gates on the unfiltered set, so a system gated on a per-`UpdateFrame` query runs on every pass and does nothing on all but one bucket's worth of them. 560 `IsEmptyIgnoreFilter` occurrences under `src/Game/` show the game reaching for the same semantics deliberately elsewhere.

`RequireForUpdate<T>()` — the generic form — builds its query `WithOptions(EntityQueryOptions.IncludeSystems)` (`:648-653`), which is why `IncludeSystems` accounts for 104 of the 107 `EntityQueryOptions` references under `src/Game/`. The other three are `IgnoreComponentEnabledState`, all in `src/Game/Game.Serialization/ResolvePrefabsSystem.cs:422-431`. **The corpus uses `EntityQueryOptions` zero times.**

`RequireMatchingQueriesForUpdate` — the attribute form that gates on _any_ of the system's queries — appears once in `src/Game/` and zero times in the corpus.

Corpus counts: 168 `RequireForUpdate`, 29 `RequireAnyForUpdate`. `Traffic/Code/Systems/LaneConnections/ApplyLaneConnectionsSystem.cs:41` is the readable `RequireAnyForUpdate(q1, q2)`.

Rots: nothing here is a name that moves; `EntityQueryOptions` member names are Entities-package API.

### The job interface: `IJobChunk` is the game's, and `IJobEntity` does work

Struct declarations under `src/Game/`, against the corpus:

| Interface | `src/Game/` | Corpus |
| --- | --- | --- |
| `IJobChunk` | 771 | 172 declarations / 152 `Execute(in ArchetypeChunk …)` bodies |
| `IJob` | 460 | 93 |
| `IJobParallelFor` | 67 | 28 mentions |
| `IJobParallelForDefer` | 82 | folded into the row above |
| `IJobFor` | 4 | 9 |
| **`IJobEntity`** | **0** | **1** |

The corpus rows for the two parallel-for interfaces count mentions rather than declarations, because several repositories name them only in a schedule call; the `IJobChunk` and `IJobEntity` rows are declarations.

Schedule sites under `src/Game/`: 612 `JobChunkExtensions.ScheduleParallel`, 161 `JobChunkExtensions.Schedule`, 459 `IJobExtensions.Schedule`, 69 `IJobParallelForExtensions.Schedule`, 827 `JobHandle.CombineDependencies`.

**`IJobEntity` in the shipped assembly is an empty marker interface** — the whole file is five lines (`src/Unity.Entities/Unity.Entities/IJobEntity.cs:1-5`). All of its machinery lives in `JobEntityGenerator.dll`, a Roslyn generator.

**Verdict: the seed survey's claim that `IJobEntity` "doesn't play well with the modding toolchain" is wrong at 1.6.0f1.** `survey-mods-techniques.md:154` states it; the toolchain ships `JobEntityGenerator.dll` alongside the other eleven Entities generators (`Mod.props:72`), and one corpus mod uses it successfully end to end: `InfoLoom/InfoLoom/Systems/DemographicsData/Demographics.cs:51` declares `private partial struct PopulationStructureJob : IJobEntity` with a generated-signature `private void Execute(Entity entity, in Citizen citizen, in HouseholdMember member)` (`:77`), fills it from `SystemAPI.GetComponentLookup<T>(true)` calls (`:374-380`), and schedules it as `job.Schedule(citizenQuery, Dependency)` (`:390`) against a query built with `SystemAPI.QueryBuilder()` (`:371`). Both the job struct and the enclosing system are `partial` (`:22`, `:51`), which is what the generator requires.

**What a mod gains or loses by matching `IJobChunk` anyway.**

Gains, all of them concrete:

- **The vanilla job body is copy-pasteable.** The substitution pattern that the corpus lives on — disable a vanilla system, run a fork of it — starts from decompiled source, and that source is always `IJobChunk`. `Time2Work` forks eleven vanilla simulation systems this way and carries 19 `IJobChunk` bodies.
- **Chunk-level early exit.** The per-`UpdateFrame` skip at `src/Game/Game.Simulation/AgingSystem.cs:68-71` rejects a whole chunk with one shared-component read. `IJobEntity`'s per-entity `Execute` cannot see the chunk, so the same skip would run once per entity — up to 128 times more often.
- **Shared-component and buffer access.** `chunk.GetSharedComponent`, `chunk.Has`, `chunk.GetBufferAccessor` and `chunk.DidChange` are all chunk-scoped (`ArchetypeChunk.cs:104-150`, `:527`, `:791`).
- **`ScheduleParallel` with a stable sort key.** `unfilteredChunkIndex` is handed to `Execute` and is exactly what a parallel command buffer wants (below); `IJobEntity` generates its own.

Losses: the boilerplate. An `IJobChunk` body must fetch each `NativeArray` from the chunk, loop, and index — see `src/Game/Game.Prefabs/ProcessingRequirementSystem.cs:37-55` for the compact form.

**Ruled (2026-08-02, the ecs-in-this-game pass; conflicts.md).** The shipped reference teaches `IJobEntity` as the default for new mod code, on the stated ground that it is the more modern replacement for `IJobChunk`. The question was whether to teach it at all; the answer is that it leads.

What the reference owes because of that, in four parts:

- **The discrepancy is stated plainly, not papered over.** The game is `IJobChunk` throughout — 771 declarations against zero — so every line of vanilla code the reader opens next is written the other way. That gap is a fact about the codebase's age, and the reference says so and says which way it has chosen.
- **Both interfaces ship.** `IJobChunk` is taught well enough to read vanilla source and to fork it, because the fork technique starts from a decompiled body and that body is always `IJobChunk`. The per-entity default does not disable forking; it stops the fork from being a paste.
- **The three things with no per-entity equivalent are named**, so a reader converting a fork knows what to solve: the chunk-level early exit (`AgingSystem.cs:68-71`), the chunk-scoped accessors (`chunk.GetSharedComponent`, `GetBufferAccessor`, `DidChange`, `Has`), and `unfilteredChunkIndex` as a parallel command buffer's sort key.
- **The `partial` requirement ships with it** — on both the job struct and its enclosing system (`InfoLoom/InfoLoom/Systems/DemographicsData/Demographics.cs:22/51`). It is what the generator needs and its absence is the first thing an agent hits.

The evidence above is unchanged by the ruling; only its position in the shipped prose is settled. Note that the corpus proportion — one repository in twenty — is a fact about what mod authors did, not a recommendation, and the ruling deliberately goes the other way.

**The corpus's own Burst gate is worth carrying alongside this**, because a Burst-compiled job cannot be stepped: `Traffic` puts every `[BurstCompile]` behind `#if WITH_BURST` (`Traffic/Code/Traffic.csproj:450`), `CS2-MoveIt` uses `USE_BURST`, `CS2-WriteEverywhere` a `<Bursted>` property. `[BurstCompile]` counts per repo run from 57 (Traffic) and 46 (CS2-Platter) down to 0 (`RoadBuilder-CSII`, `ExtraAssetsImporter`), so an unbursted mod is a real and shipped choice.
**Ruled (2026-08-04, the performance-and-memory pass; conflicts.md).** `performance-and-memory` owns both gates and leads with the runtime one — `--burst-disable-compilation` or `UNITY_BURST_DISABLE_COMPILATION` — because seven of the ten corpus repositories using the compile-time gate define the symbol nowhere in the checkout, and a preprocessor symbol defined nowhere produces a build indistinguishable from a working one. This file's prose defers to that ordering. The launch argument was afterwards settled against the running game and ships unmarked, with the environment variable as its stated fallback; `performance-and-memory.md`'s own "Settled against the running game" section carries the evidence.

Rots: the per-interface counts. The `IJobChunk.Execute` signature itself is Entities-package API and does not move within a package version.

### The type-handle idiom: what it indexes, and what breaks when it is skipped

A `ComponentTypeHandle<T>` holds five things: the `TypeIndex`, the component's size in a chunk, a read-only flag, a `LookupCache`, and `m_GlobalSystemVersion` (`src/Unity.Entities/Unity.Entities/ComponentTypeHandle.cs:10-22`). `Update(ref SystemState state)` refreshes exactly one of them — `m_GlobalSystemVersion = state.GlobalSystemVersion` (`:43-46`).

**That single field is the whole point.** Read-write chunk access stamps the chunk with the handle's version: `GetNativeArray` on a non-read-only handle routes to `ChunkDataUtility.GetOptionalComponentDataWithTypeRW(m_Chunk, archetype, 0, typeHandle.m_TypeIndex, typeHandle.GlobalSystemVersion, ref typeHandle.m_LookupCache)` (`src/Unity.Entities/Unity.Entities/ArchetypeChunk.cs:655`). A stale handle stamps a stale version, and every change filter downstream — `chunk.DidChange(ref handle, version)` (`:104-113`), `SetChangedVersionFilter` — then reports "unchanged" for a chunk you just wrote. Nothing throws.

**Nothing throws because this build has no safety system.** `ComponentTypeHandle<T>` carries no `m_Safety` field, and neither does `ComponentLookup<T>` or `ArchetypeChunk`; the bounds and aliasing assertions are all `[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]` (`ArchetypeChunk.cs:697-699` is one of many) and compiled out of the shipped assembly. A stale handle, an out-of-bounds chunk index, or two jobs writing the same component in parallel produce wrong data or a crash, never a diagnostic.

**The game's shape.** 736 files under `src/Game/` carry `private TypeHandle __TypeHandle`, a generated nested struct holding one field per handle, named `__<Namespace>_<Type>_<RO|RW>_<Kind>`. `OnCreateForCompiler` calls `__TypeHandle.__AssignHandles(ref base.CheckedStateRef)` once (`src/Game/Game.Simulation/AgingSystem.cs:289-294`), which fills them from `state.GetComponentLookup<T>(isReadOnly: true)` and friends (`:163-170`). The per-frame refresh happens at the point of use, inside the job initialiser:

```csharp
m_TravelPurposes = InternalCompilerInterface.GetComponentLookup(
    ref __TypeHandle.__Game_Citizens_TravelPurpose_RO_ComponentLookup, ref base.CheckedStateRef),
```

(`AgingSystem.cs:270`), and `InternalCompilerInterface.GetComponentLookup` is three lines: `componentLookup.Update(ref state); return componentLookup;` (`src/Unity.Entities/Unity.Entities/InternalCompilerInterface.cs:183-187`, and identically for `GetComponentTypeHandle` at `:351-355`, `GetBufferTypeHandle` at `:358-362`, `GetSharedComponentTypeHandle` at `:365-369`, `GetEntityTypeHandle` at `:176-180`). So `SystemAPI.GetComponentTypeHandle<T>()` in a mod's source gives you the caching **and** the refresh for free, and skipping `.Update()` is not something you can do by accident when you use it.

Only **one** hand-written `.Update(this)` exists in the whole of `src/Game/`: `UpdateGroupSystem.UpdateGroupTypes.Update(SystemBase)` (`src/Game/Game.Simulation/UpdateGroupSystem.cs:57-72`), a helper struct that bundles thirteen handles so they can be passed to a job as one field.

**Two hand-rolled idioms exist in the corpus, and both are what you need when the generator is not in play.**

- **Carry the decompiled generated struct into the fork.** `Time2Work` does this in 13 files. `Time2Work/NightShift/Systems/CitizenScheduleSystem.cs:48` declares `private CitizenScheduleSystem.TypeHandle __TypeHandle`, `:287-320` (in the sibling `Time2WorkAttractionSystem.cs`) redeclares the generated struct and its `__AssignHandles(ref SystemState)`, `OnCreateForCompiler` calls it (`Time2WorkAttractionSystem.cs:128-132`), and `OnUpdate` opens with a block of nine explicit refreshes:

  ```csharp
  this.__TypeHandle.__Game_City_Population_RO_ComponentLookup.Update(ref this.CheckedStateRef);
  this.__TypeHandle.WorkerTypeHandle.Update(ref this.CheckedStateRef);
  this.__TypeHandle.__Game_Citizens_Citizen_RW_ComponentTypeHandle.Update(ref this.CheckedStateRef);
  ```

  (`CitizenScheduleSystem.cs:130-138`). This is the exact shape a fork must keep: the generator will not regenerate handles for a struct you pasted in.

- **Put the handles on the job and give the job two methods.** `AreaBucket/Systems/AreaBucketToolJobs/CollectAreaLines.cs:131-149` declares `AssignHandle(ref SystemState state)` (one `state.GetBufferLookup<T>()` / `state.GetComponentLookup<T>()` per field) and `UpdateHandle(ref SystemState state)` (one `.Update(ref state)` per field), and the system calls the first in `OnCreate` and the second in `OnUpdate`. `CollectLotLines.cs:100-112` is the same. This keeps the handle next to the job that reads it and is the cleanest hand-rolled version in the corpus.

Corpus-wide: 329 `SystemAPI.GetComponentTypeHandle`, 844 `SystemAPI.GetComponentLookup`, 217 `SystemAPI.GetBufferLookup`, against 59 explicit `.Update(ref …)` calls in four repositories (AreaBucket, Time2Work, NodeController, ExtraDetailingTools) and 4 `.Update(this)` in one (NodeController).

Rots: the generated handle-name scheme `__<Namespace>_<Type>_<RO|RW>_<Kind>` and the `InternalCompilerInterface` method names — both belong to the Entities generator version pinned by `CSII_ENTITIESVERSION`, not to the game.

### The chunk-enabled mask, and the corpus mostly drops it

`IJobChunk.Execute` receives `bool useEnabledMask, in v128 chunkEnabledMask` (`src/Unity.Entities/Unity.Entities/IJobChunk.cs:9`). The correct loop is

```csharp
ChunkEntityEnumerator enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
while (enumerator.NextEntityIndex(out int i)) { … }
```

(`src/Game/Game.Prefabs/ProcessingRequirementSystem.cs:42-43`). When `useEnabledMask` is false the enumerator degenerates to a counted loop (`src/Unity.Entities/Unity.Entities/ChunkEntityEnumerator.cs:24-29`); when true it walks the two 64-bit words bit by bit (`:34-63`).

**The game uses it in 15 places out of 771 `IJobChunk` structs**, all in systems whose query names an enableable component: `src/Game/Game.Prefabs/UnlockSystem.cs:37` (`Locked`), `ProcessingRequirementSystem.cs:42`, `StrictObjectBuiltRequirementSystem.cs:42`, `TransportRequirementSystem.cs:44`, `ZoneBuiltRequirementSystem.cs:161`, `src/Game/Game.Serialization/InitializeObsoleteSystem.cs:121` (`PrefabData`). Everywhere else the game loops `for (int i = 0; i < chunk.Count; i++)` because the query cannot match a disabled entity.

**Corpus: 10 uses across 152 `IJobChunk` bodies**, in CS2-MoveIt (2), CS2-Platter (1), InfoLoom (2) and Traffic (5). So roughly 93% of mod chunk jobs ignore the mask.

**One of those ten does not work.** `Traffic/Code/Systems/ModificationDataSyncSystem.SyncModificationDataJob.cs:25` constructs `new ChunkEntityEnumerator()` with no arguments. That leaves `ChunkEntityCount = 0`, so `NextEntityIndex` computes `Iter(1) <= 0` and returns false on the first call (`ChunkEntityEnumerator.cs:26-28`) — the `while` loop at `:26-39` never executes a single iteration. The plain `for (var i = 0; i < entities.Length; i++)` loop immediately below it (`:40+`) does the same work and is what actually runs, so the mod is correct by accident and the enumerator line is dead. The enumerator has no useful default constructor and must always be given all three arguments.

Rots: nothing structural; the count of enableable game types is the volatile part and is recorded in the next finding.

### Enableable components: twelve in the game, and two of them silently narrow your query

A grep for `IEnableableComponent` over `src/Game/` returns 13 files, of which 12 are declarations and the thirteenth is a generic constraint on `PrefabSystem.HasEnabledComponent<T>` (`src/Game/Game.Prefabs/PrefabSystem.cs:720`). The twelve, in full:

`src/Game/Game.Agents/HasJobSeeker.cs:6`, `Game.Agents/PropertySeeker.cs:6`, `Game.Citizens/Arrived.cs:8`, `Game.Citizens/BicycleOwner.cs:6`, `Game.Citizens/CarKeeper.cs:6`, `Game.Citizens/CrimeVictim.cs:6`, `Game.Citizens/MailSender.cs:6`, `Game.Objects/Decoration.cs:8`, `Game.Prefabs/Locked.cs:8`, `Game.Prefabs/NotificationIconDisplayData.cs:7`, `Game.Prefabs/PrefabData.cs:7`, and `Game.Rendering/CustomMeshColor.cs:8` — the last being the only enableable **buffer** in the game.

**A query naming an enableable component matches only entities where it is enabled**, unless the query carries `EntityQueryOptions.IgnoreComponentEnabledState`. Two of the twelve make that bite mods.

- **`PrefabData` disabled means "obsolete prefab".** `src/Game/Game.Serialization/ResolvePrefabsSystem.cs:511-512` disables it across two queries during a load, re-enables one at `:517`, and permanently disables it on prefabs that a save references but the current install no longer has (`:530-537`, with `m_Index` rewritten to a negative sentinel). `PrefabSystem` uses `IsComponentEnabled<PrefabData>(entity)` as the "does this prefab still exist" test when writing a save (`src/Game/Game.Prefabs/PrefabSystem.cs:837/846`). So `WithAll<PrefabData>()` gives you live prefabs only, which is almost always right — but it is a filter you did not write, and it explains a prefab count that does not match a mod list.
- **`Locked` disabled means "unlocked".** `src/Game/Game.Prefabs/UnlockSystem.cs:208` and `src/Game/Game.Serialization/RequiredComponentSystem.cs:1510` both unlock by `SetComponentEnabled<Locked>(entity, value: false)`. A progression query on `WithAll<Locked>()` therefore returns only what is _still_ locked.

**The corpus uses `EntityQueryOptions` zero times**, and 12 corpus queries name `PrefabData` (`FindIt-CSII/FindIt/Systems/PrefabIndexingSystem.cs:84/533/537`, `RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderNetSectionsSystem.cs:49-50`, `CS2-Platter/Platter/Systems/P_ZoneCacheSystem.cs:61/69`, `Anarchy/Anarchy/Systems/NetworkAnarchy/NetworkAnarchyUISystem.cs:303`, `Time2Work/NightShift/Systems/Time2WorkLeisureSystem.cs:116` among them). None opts out.

**The corpus declares nine enableable components of its own**, eight in `CS2-WriteEverywhere` (`BelzontWE/Components/WEIsPlaceholder.cs:5`, `Components/WETemplateForPrefab.cs:12/13/15`, `Components/WETextData/WETextDataDirtyFormulae.cs:6`, `Components/WEWaitingRendering.cs:8/9`, `Systems/WEPreCullingSystem.cs:885`) and one in `Tree_Controller` (`Tree_Controller/Tree_Controller/Components/LumberResource.cs:13`, `IComponentData, IEnableableComponent, IQueryTypeParameter, IEmptySerializable` — an empty enableable tag that persists). `CS2-WriteEverywhere` also has the only generic enable helper in the corpus, `WEAddAndEnableComponentJob<T> : IJobChunk where T : unmanaged, IComponentData, IEnableableComponent` (`BelzontWE/Utils/WEAddAndEnableComponentJob.cs:12`).

Toggling from a job goes through the command buffer: `m_CommandBuffer.SetComponentEnabled<BicycleOwner>(unfilteredChunkIndex, citizen, value: true)` (`src/Game/Game.Simulation/AgingSystem.cs:114`). `Recolor` does the same on a vanilla enableable **buffer**: `buffer.SetComponentEnabled<Game.Rendering.CustomMeshColor>(instanceEntity, true)` (`Recolor/Recolor/Systems/Palettes/TempAssignedPalettesSystem.cs:428`).

Rots: this entire list of twelve. Re-grep `IEnableableComponent` over `src/Game/` — the set has clearly grown across versions, since `CustomMeshColor` reached vanilla after a mod shipped its own (below).

### Command buffers: twelve named barriers, a safety gate, and one contract

The game exposes command buffers as **named systems**, not as raw `EntityCommandBuffer`s. All of them derive from `SafeCommandBufferSystem : EntityCommandBufferSystem` (`src/Game/Game/SafeCommandBufferSystem.cs:7`).

The twelve, with where each plays back (`src/Game/Game.Common/SystemOrder.cs`):

| Barrier | Registration | Plays back |
| --- | --- | --- |
| `EndFrameBarrier` | `UpdateBefore(MainLoop)` `:49` | front of `MainLoop`; anything recorded after that point waits for the next frame |
| `ModificationBarrier1` | `UpdateAfter(Modification1)` `:86` | end of `Modification1` |
| `ModificationBarrier2` | `UpdateAfter(Modification2)` `:87` | end of `Modification2` |
| `ModificationBarrier2B` | `UpdateAfter(Modification2B)` `:88` | end of `Modification2B` |
| `ModificationBarrier3` | `UpdateAfter(Modification3)` `:89` | end of `Modification3` |
| `ModificationBarrier4` | `UpdateAfter(Modification4)` `:90` | end of `Modification4` |
| `ModificationBarrier4B` | `UpdateAfter(Modification4B)` `:91` | end of `Modification4B` |
| `ModificationBarrier5` | `UpdateAfter(Modification5)` `:92` | end of `Modification5` |
| `ModificationEndBarrier` | `UpdateAfter(ModificationEnd)` `:93` | end of `ModificationEnd` |
| `ToolOutputBarrier` | `UpdateAfter(ToolUpdate)` `:695` | end of `ToolUpdate` |
| `ToolReadyBarrier` | `UpdateAfter(PostTool)` `:697` | end of `PostTool` |
| `DeserializationBarrier` | `UpdateAfter(Deserialize)` `:797` | end of `Deserialize` |

A thirteenth type exists and is dead: `AudioEndBarrier` and its `AllowAudioEndBarrier` (`src/Game/Game.Common/AudioEndBarrier.cs:6`, `AllowAudioEndBarrier.cs:5`) appear in the generated type registry and are registered with `UpdateSystem` nowhere and referenced by no system. Do not reach for it.

`Game.Input/InputBarrier.cs:7` is an unrelated type that blocks input actions, not an `EntityCommandBufferSystem`. The name collides and nothing else does.

**The safety gate is the contract's teeth.** `SafeCommandBufferSystem.CreateCommandBuffer()` shadows the base method and throws `new Exception("Trying to create EntityCommandBuffer when it's not allowed!")` when the barrier has already played back this pass (`src/Game/Game/SafeCommandBufferSystem.cs:16-23`); `OnUpdate` sets `m_IsAllowed = false` before flushing (`:26-30`), and a paired `AllowBarrier<T> : GameSystemBase` re-opens it from its own update (`src/Game/Game/AllowBarrier.cs:17-20`). Vanilla registers an `AllowBarrier<T>` in the front band of every phase whose barrier plays back at the back (`SystemOrder.cs:78-85`, `:693`, `:696`, `:737`), and `AllowBarrier<EndFrameBarrier>` in `MainLoop`'s middle band at `:62`. So **writing to a barrier outside its open window is a thrown exception rather than a silent no-op** — the one place in this ECS where the failure is loud.

**`EndFrameBarrier`'s window is narrower than its popularity suggests, and this is derived rather than read.** Its playback sits at `SystemOrder.cs:49`, in `MainLoop`'s front band; its `AllowBarrier` at `:62`, eighth in `MainLoop`'s `UpdateAt` band. `ModificationSystem` is registered at `:60` and drives all eight modification phases from its own update, `ToolSystem` at `:58` drives the five tool phases, `LoadGameSystem` at `:59` drives `Deserialize`, `RaycastSystem` at `:55` drives `Raycast` and `PrefabSystem` at `:56` drives `PrefabUpdate` — every one of them before `:62`. Since `SafeCommandBufferSystem.OnUpdate` sets `m_IsAllowed = false` unconditionally before flushing, and nothing else in `src/Game/` calls `AllowUsage()` outside `AllowBarrier<T>` and `AllowAudioEndBarrier` (grepped, three hits total), **an `OnUpdate` body running in a modification, tool, raycast, prefab-update or deserialize phase cannot create an `EndFrameBarrier` command buffer** — it throws. Everything in `src/Game/` is consistent with that: of the 202 files touching `EndFrameBarrier`, 177 are in `Game.Simulation` (which runs from `LateUpdate`), and the only `Game.Tools` user, `RecentClearSystem`, is registered at `GameSimulation` (`SystemOrder.cs:410`), while the two `Game.Prefabs` users are registered at `GameSimulation` too (`:455`, `:596`). No vanilla system contradicts it.

**The limit of that derivation, stated plainly.** It holds for `OnUpdate` bodies, which is where the phase walk puts a system. It says nothing about the lifecycle hooks, which fire from outside the walk — and there is one corpus call site that looks like a counter-example and is not: `Recolor/Recolor/Systems/Palettes/PaletteInstanceManagerSystem.cs:167` calls `m_EndFrameBarrier.CreateCommandBuffer()` from a system registered at `ModificationEnd` (`Recolor/Recolor/Mod.cs:155`), but the call is inside `OnGameLoadingComplete`, not `OnUpdate`. `onGameLoadingComplete` is invoked from an async continuation in `GameManager.Load` (`src/Game/Game.SceneFlow/GameManager.cs:1126`, immediately before `m_State = State.WorldReady` at `:1127`), and where that continuation lands relative to the frame's phase walk cannot be determined from the decompile. So: the window claim is about `OnUpdate`, and a lifecycle hook's position in the window is unresolved.

**One more crack in the gate, and it is in the type system.** `SafeCommandBufferSystem.CreateCommandBuffer()` is declared `new`, not `override` (`src/Game/Game/SafeCommandBufferSystem.cs:16`), and the base `EntityCommandBufferSystem.CreateCommandBuffer()` is not virtual (`src/Unity.Entities/Unity.Entities/EntityCommandBufferSystem.cs:18`). A call through a variable typed as the base class therefore binds to the base method and skips the check entirely. Every game and corpus call site holds the concrete barrier type in its field (`src/Game/Game.Simulation/AgingSystem.cs:181`, `Recolor/.../PaletteInstanceManagerSystem.cs:41`), so nobody trips it — but a mod that stores barriers in an `EntityCommandBufferSystem`-typed dictionary or passes one to a generic helper loses the diagnostic and gets a buffer that is flushed at an unpredictable time instead of an exception.

**The contract, from `EntityCommandBufferSystem` itself** (`src/Unity.Entities/Unity.Entities/EntityCommandBufferSystem.cs`):

1. `CreateCommandBuffer()` allocates a fresh `EntityCommandBuffer` with `PlaybackPolicy.SinglePlayback` and appends it to `PendingBuffers` (`:18-21`, `:82-90`). **Call it once per `OnUpdate`, not once per job** — every call adds a buffer to the flush list.
2. `AddJobHandleForProducer(producerJob)` combines your handle into `m_ProducerHandle` (`:23-26`). Without it the barrier flushes while your job is still writing.
3. `OnUpdate` completes the system's own dependency and `m_ProducerHandle`, then plays every pending buffer back in list order and rewinds the allocator (`:50-54`, `:56-80`).

The vanilla shape, in four lines and worth reproducing verbatim (`src/Game/Game.Simulation/AgingSystem.cs:276-281`):

```csharp
m_CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter()
…
base.Dependency = JobChunkExtensions.ScheduleParallel(jobData, m_HouseholdQuery, base.Dependency);
m_EndFrameBarrier.AddJobHandleForProducer(base.Dependency);
```

with the barrier itself resolved once in `OnCreate`: `m_EndFrameBarrier = base.World.GetOrCreateSystemManaged<EndFrameBarrier>()` (`:212`).

**The sort key.** Every `ParallelWriter` method takes an `int sortKey` first (`src/Unity.Entities/Unity.Entities/EntityCommandBuffer.cs:1180-1248`), and playback merges the per-thread chains in ascending sort-key order (`:200-240`). The game always passes `unfilteredChunkIndex` — the parameter `IJobChunk.Execute` already hands you — so recording order is deterministic across runs regardless of thread scheduling. `AgingSystem`'s helper takes it as a parameter for exactly that reason (`AgingSystem.cs:59/109`).

**Census.** `src/Game/`: 493 `CreateCommandBuffer()`, 383 `AddJobHandleForProducer`, 664 `AsParallelWriter()`, and only 17 raw `new EntityCommandBuffer(`. Barrier fields per barrier, counted as declarations under `src/Game/`: `EndFrameBarrier` 193, `ModificationBarrier5` 34, `ModificationBarrier4` 22, `ToolOutputBarrier` 21, `ModificationEndBarrier` 16, `ModificationBarrier1` 14, `DeserializationBarrier` 9, `ModificationBarrier4B` 9, `ModificationBarrier2` 8, `ModificationBarrier2B` 6, `ModificationBarrier3` 5, `ToolReadyBarrier` 2.

**Corpus**, by barrier, with the repositories that touch each:

| Barrier | Mentions | Repositories |
| --- | --- | --- |
| `EndFrameBarrier` | 170 | Anarchy, CS2-WriteEverywhere, ExtraDetailingTools, Recolor, Time2Work, Tree_Controller, Water_Features |
| `ToolOutputBarrier` | 137 | 14 of 20 |
| `ModificationEndBarrier` | 36 | Anarchy, BetterBulldozer, CS2-WriteEverywhere, ExtraDetailingTools, PlopTheGrowables, Recolor, Traffic |
| `ModificationBarrier1` | 22 | Anarchy, AreaBucket, CS2-Platter, RoadBuilder-CSII |
| `ModificationBarrier2` | 15 | Anarchy, CS2-NetworkTools, CS2-Platter, Recolor |
| `ModificationBarrier5` | 14 | Anarchy, BetterBulldozer, CS2-Platter, Traffic |
| `ModificationBarrier4` | 9 | CS2-NetworkTools, CS2-Platter, Traffic |
| `ModificationBarrier3` | 4 | Anarchy, Traffic |
| `ModificationBarrier4B` | 4 | CS2-Platter, Traffic |
| `DeserializationBarrier` | 2 | BetterBulldozer |
| `ModificationBarrier2B` | 1 | CS2-NetworkTools |
| `ToolReadyBarrier` | **0** | — |

Corpus totals: 217 `CreateCommandBuffer()` against 110 `AddJobHandleForProducer` and 118 `AsParallelWriter()` — so roughly half of the mod-created buffers are written from the main thread, where no producer handle is needed, and the ratio is not evidence of a missing call on its own. 34 raw `new EntityCommandBuffer(` exist, `CS2-MoveIt` holding most of them for its undo queue.

The full-contract exemplar the seed survey named still holds: `Traffic/Code/Systems/LaneConnections/ApplyLaneConnectionsSystem.cs:80-96`.

Rots: the barrier type names and their `SystemOrder.cs` line numbers; the `"Trying to create EntityCommandBuffer when it's not allowed!"` message string.

### The six universal tags, and the protocol they carry

Six components form a frame-scoped change protocol. All six are `[StructLayout(LayoutKind.Sequential, Size = 1)] struct X : IComponentData, IQueryTypeParameter` with no fields: `Created`, `Updated`, `Applied`, `EffectsUpdated`, `BatchesUpdated`, `PathfindUpdated` (`src/Game/Game.Common/Created.cs:6-9` and its five siblings in the same directory).

**They are added to every prefab-instance archetype at birth.** `RefreshArchetype` unconditionally adds `Created` and `Updated` to the component set before calling `CreateArchetype` (`src/Game/Game.Prefabs/ArchetypePrefab.cs:36-37`, `src/Game/Game.Prefabs/BuildingPrefab.cs:73-74`). So a freshly spawned entity carries both, and a system querying `WithAll<Created>()` sees it exactly once.

**They are removed by a two-system pair, and the split matters.**

- `PrepareCleanUpSystem`, registered `UpdateAfter<PrepareCleanUpSystem>(MainLoop)` (`SystemOrder.cs:50`), so it runs at the very end of `MainLoop`. It snapshots two queries into lists: everything with `Any = {Deleted, Event}`, and everything with `Any = {Created, Updated, Applied, EffectsUpdated, BatchesUpdated, PathfindUpdated}` and `None = {Deleted}` (`src/Game/Game.Common/PrepareCleanUpSystem.cs:21-41`), handing both to `CleanUpSystem` with their job handles (`:47-53`).
- `CleanUpSystem`, registered `UpdateAt<CleanUpSystem>(Cleanup)` (`SystemOrder.cs:54`). It `DestroyEntity`s the first list and `RemoveComponent`s the six-type `ComponentTypeSet` from the second (`src/Game/Game.Common/CleanUpSystem.cs:24-32`, `:48-56`).

Three consequences a mod needs:

1. **`Deleted` — or `Event` — means the entity dies at `Cleanup`, one phase later.** That gap is the point: every system that holds a reference gets a frame to query `WithAll<Deleted>()` and unhook. This is why the game deletes with `AddComponent<Deleted>` (274 sites under `src/Game/`) far more than with `DestroyEntity` (84, almost all in the loader and prefab paths). The corpus copies the ratio: 79 `AddComponent<Deleted>` against 41 `DestroyEntity`.
2. **An `Event` entity lives exactly one frame.** It is in the destroy query with no `None` clause, so an event archetype spawned during the frame is gone at `Cleanup`. Consume it in the same frame or not at all.
3. **A tag added after `PrepareCleanUpSystem` runs survives an extra frame.** `PrepareCleanUpSystem` is the last thing in `MainLoop`; the simulation phases hang off `SimulationSystem` in `LateUpdate` (`mod-lifecycle-and-ordering.md`, "The 32 phases nest"). So a tag written by a `GameSimulation` system is not in this frame's snapshot; it is picked up at the end of the _next_ frame's `MainLoop` and removed in that frame's `Cleanup`. It is therefore visible to the next frame's modification, tool, UI and rendering phases — which is exactly what the simulation wants.

**What each tag actually asks for.** The wiki's `Common ECS Components` page (https://cs2.paradoxwikis.com/Common_ECS_Components, last edited 7 June 2024) gives verbatim descriptions for four of the six, and they check out:

- `Created` — "Added to entities that are newly created." **Verdict: verified**, seeded by `ArchetypePrefab.cs:36`.
- `Updated` — "Applied to entities that have had components changed other than just a visual change." **Verdict: verified in effect.** 293 mentions under `src/Game/`; it is the general "re-run the modification pipeline over me" tag.
- `BatchesUpdated` — "Added to entities where graphics need to be updated." **Verdict: verified, and this is the tag a mod needs most.** `PreCullingSystem` tests `chunk.Has(ref m_BatchesUpdatedType)` and `m_BatchesUpdatedData.HasComponent(entity)` and ORs in `PreCullingFlags.BatchesUpdated` (`src/Game/Game.Rendering/PreCullingSystem.cs:656`, `:939`, `:1899-1901`); `BatchInstanceSystem`, `BatchDataSystem` and `InitializeAnimatedSystem` all branch on that flag (`src/Game/Game.Rendering/BatchInstanceSystem.cs:338`, `BatchDataSystem.cs:448`, `InitializeAnimatedSystem.cs:64/109`); `CompleteCullingSystem` clears it (`src/Game/Game.Rendering/CompleteCullingSystem.cs:26/49`). **If you change anything visible on an entity and do not add `BatchesUpdated`, the renderer keeps the old batch.** The corpus knows this: 110 mentions across the corpus against 17 under `src/Game/` outside the rendering systems themselves — mods add this tag far more often, proportionally, than the game does.
- `Overridden` — "Applied generally to objects that are conflicting with other objects or networks but are not permanently deleted or removed." **Verdict: consistent with the decompile.** `src/Game/Game.Net/OverrideSystem.cs:476/497` is the only place it is removed, and `RaycastSystem` and `LaneSystem` read it to skip overridden geometry (`src/Game/Game.Common/RaycastSystem.cs:459`, `src/Game/Game.Net/LaneSystem.cs:379`). Unlike the six frame-scoped tags it is `IEmptySerializable` (`src/Game/Game.Common/Overridden.cs:8`) and survives a save.
- `EffectsUpdated` (5 mentions) and `PathfindUpdated` (9) are the narrow two; the wiki's "Added when the path finding parameters for a road lane have been updated" for the latter matches its occupancy.
- `Applied` (24 mentions) carries no wiki description. It is added by the tool `Apply*System` family and consumed by `PreCullingFlags.Applied`.

**Three more tags in `Game.Common` are not frame-scoped and do not go through `CleanUpSystem`:**

- `Native : IComponentData, IQueryTypeParameter, IEmptySerializable` (`src/Game/Game.Common/Native.cs:8`) — persists; marks map-native content.
- `Owner : IComponentData, IQueryTypeParameter, ISerializable` with a single `Entity m_Owner` and hand-written read/write (`src/Game/Game.Common/Owner.cs:6-18`). 97 mentions under `src/Game/` and 105 across the corpus — the standard back-reference from a sub-object to its parent, and the shape a mod copies when attaching its own entity to a game entity.
- `PseudoRandomSeed : IComponentData, IQueryTypeParameter, ISerializable` (`src/Game/Game.Common/PseudoRandomSeed.cs:7`), a `ushort m_Seed` plus `GetRandom(uint reason)` that derives a stream as `new Random(math.max(1u, m_Seed ^ reason))` after two warm-up draws (`:63-69`), with 21 named `k*` reason constants (`:9-49`). 36 corpus mentions. This is how the game gets stable per-entity randomness that survives a save without storing the stream.

**The tool-preview tag lives in `Game.Tools`, not `Game.Common`, and the wiki page does not list it.** `Temp : IComponentData, IQueryTypeParameter` carries `Entity m_Original`, `float m_CurvePosition`, `int m_Value`, `int m_Cost`, `TempFlags m_Flags` (`src/Game/Game.Tools/Temp.cs:5-15`) and is **not** serializable. 765 mentions under `src/Game/` and 276 across the corpus — second only to `Deleted`. Nearly every game query excludes it, and `src/Game/Game.Simulation/AgingSystem.cs:216-221` is the canonical pair: `None = { Deleted, Temp }`. The `Apply*System` family reads `temp.m_Original` to write back onto the real entity (`src/Game/Game.Tools/ApplyObjectsSystem.cs:478`, `ApplyNetSystem.cs:555`). `Hidden` (`src/Game/Game.Tools/Hidden.cs:7`, 47 mentions, 44 in the corpus) is its sibling.

Rots: all fourteen tag type names, the six-member `ComponentTypeSet` at `CleanUpSystem.cs:24-32`, the two `PrepareCleanUpSystem` query shapes, and `TempFlags`' members. Re-read `src/Game/Game.Common/` and `src/Game/Game.Tools/Temp.cs`.

### Declaring components of your own: registration, runtime cost, save cost

**Registration is automatic and needs no attribute.** `TypeManager.Initialize` runs at boot and registers the game's types through each assembly's generated `Unity.Entities.CodeGeneratedRegistry.AssemblyTypeRegistry` (`src/Unity.Entities/Unity.Entities/TypeManager.cs:2430-2445`). A mod assembly loads later, so it takes a different path: `ModManager.AfterLoadAssembly` calls `TypeManager.InitializeAdditionalTypes(assembly)` (`src/Game/Game.Modding/ModManager.cs:146-151`), which reflects over `assembly.GetTypes()` and adds everything `IsSupportedComponentType` accepts (`TypeManager.cs:603-665`). **A component declared in a mod assembly is registered by reflection, with no codegen requirement and no registration call of your own.** Two consequences: a type cannot be registered twice (`AddAllComponentTypes` throws `"ComponentType {type} cannot be initialized more than once."`, `:3686-3688`), and a generic component needs `[assembly: RegisterGenericComponentType(typeof(MyThing<Foo>))]` because reflection cannot enumerate closed generics (`:636-643`).

**A name that matches a vanilla component is a different type and does not clash.** `Recolor.Domain.CustomMeshColor : IBufferElementData, ISerializable` (`Recolor/Recolor/Domain/CustomMeshColor.cs:16`) coexists with `Game.Rendering.CustomMeshColor : IBufferElementData, IEnableableComponent, ISerializable` (`src/Game/Game.Rendering/CustomMeshColor.cs:8`), and the mod uses both, namespace-qualifying the vanilla one at every touch (`Recolor/Recolor/Systems/Palettes/TempAssignedPalettesSystem.cs:422-428`). Its own doc comment records why: "Partially Migrated to vanilla `Game.Rendering.CustomMeshColor`. Still used for NetLanes fences." (`CustomMeshColor.cs:13`). This is the clearest evidence in the corpus that the game absorbs mod concepts across versions, and that a name collision is survivable but expensive to read.

**Runtime cost, by kind:**

- A zero-field `IComponentData` costs one bit in the archetype's type list and no per-entity bytes — `[StructLayout(LayoutKind.Sequential, Size = 1)]` on the game's tags plus `TypeInfo.IsZeroSized` (`ComponentTypeHandle.cs:37`) means a tag adds no chunk storage. It does add a distinct archetype, which is the real cost: adding or removing one moves every affected entity between chunks.
- An `IBufferElementData` reserves `InternalBufferCapacity` elements inline in the chunk and spills to the heap beyond that. With no attribute the capacity is `128 / sizeof(element)` (`TypeManager.cs:2292`, against `DefaultBufferCapacityNumerator = 128` at `:501`). `[InternalBufferCapacity(0)]` means **never inline** — every non-empty buffer is a heap allocation and an empty one allocates nothing, which keeps the chunk dense when most entities have an empty buffer. Overflow allocates from `Allocator.Persistent` with a minimum capacity of 8 elements (`src/Unity.Entities/Unity.Entities/BufferHeader.cs:19/57/67/92/99/124`, re-derived under `performance-and-memory`). The corpus splits deliberately: `Traffic` uses `(0)` on all nine of its buffer types (`Traffic/Code/Components/LaneConnections/ModifiedLaneConnections.cs:9` and siblings), `Recolor` uses `(1)` on five (`Recolor/Recolor/Domain/CustomMeshColor.cs:15`) and `(3)` on `AssignedPalette` (`Recolor/Recolor/Domain/Palette/AssignedPalette.cs:13`), `CS2-Platter` uses `(1)` (`Platter/Components/ConnectedParcel.cs:15`).
- An `ISharedComponentData` costs nothing per entity and splits chunks per distinct value. Nobody in the corpus takes this trade.
- An `IEnableableComponent` costs its own bits in the chunk's enabled masks and lets you toggle without an archetype move — which is the point: it turns a structural change into a bit flip.
- An `ICleanupComponentData` keeps a residue entity alive after `DestroyEntity` until you remove it (`EntityComponentStore.cs:3656-3681`). Forget the removal and you leak entities silently.

**Save cost is decided by one interface, and by nothing else.** `ComponentSerializerLibrary.Initialize` walks every type the `TypeManager` knows (`TypeManager.GetTypeCount()`) and registers a serializer for each that implements `Colossal.Serialization.Entities.IEmptySerializable` or `ISerializable` (`src/Colossal.Core/Colossal.Serialization.Entities/ComponentSerializerLibrary.cs:45-99`). The branch table:

| Declares | Serializer chosen | Line |
| --- | --- | --- |
| `IEmptySerializable`, not enableable | `EmptyComponentSerializer` | `:57-63` |
| `IEmptySerializable` + `IEnableableComponent` | `EnableableEmptyComponentSerializer<T>` / `EnableableEmptyBufferElementSerializer<T>` | `:64-71` |
| `ISerializable` component | `ComponentDataSerializer<T>` or `EnableableComponentDataSerializer<T>` | `:80-83` |
| `ISerializable` buffer | `BufferElementDataSerializer<T>` or `EnableableBufferElementDataSerializer<T>` | `:84-87` |
| `ISerializable` shared component | `SharedComponentDataSerializer<T>` | `:88-91` |
| neither | none — the component is not written | — |

`ISerializeAsEnabled` is the opt-out from the enableable-aware serializer: a type carrying it takes the plain serializer even though it is enableable (`:57`, `:82`, `:86`), i.e. its disabled state is not persisted. Only two game types use it — `PrefabData` and `NotificationIconDisplayData` (`src/Game/Game.Prefabs/PrefabData.cs:7`, `NotificationIconDisplayData.cs:7`) — and no corpus type does.

`IEmptySerializable` is the whole declaration for a tag that must survive a save: `struct PloppedBuilding : IComponentData, IQueryTypeParameter, IEmptySerializable {}` (`PlopTheGrowables/Code/Systems/PloppedBuilding.cs:15`), eighteen lines including the licence header. `ISerializable` is two methods, `Serialize<TWriter>` and `Deserialize<TReader>` (`src/Colossal.Core/Colossal.Serialization.Entities/ISerializable.cs:5-7`), and the game's own smallest implementation is `Owner`, three lines of body (`src/Game/Game.Common/Owner.cs:10-18`).

**The library rebuilds after a mod loads.** `ModManager.AfterLoadAssembly` calls `SerializerSystem.SetDirty()` in the same two lines that register the types (`src/Game/Game.Modding/ModManager.cs:148-149`), and `SerializerSystem` re-runs `Initialize` when dirty (`src/Game/Game.Serialization/SerializerSystem.cs:76-82`). So a mod component becomes saveable purely by implementing the interface — there is no registration call and no manifest.

**Corpus proportions.** 205 component and buffer-element declarations across the corpus; 20 carry `IEmptySerializable` (7 repos), 49 carry `ISerializable` (11 repos). So about two-thirds of mod components are deliberately transient. Zero use `IDefaultSerializable` (`ISerializable` plus `SetDefaults(Context)`, `src/Colossal.Core/Colossal.Serialization.Entities/IDefaultSerializable.cs:3-5`), and zero use `ISerializeAsEnabled`.

The versioning discipline inside `Serialize`/`Deserialize` — writing a version int first and branching on it — belongs to `save-serialization`; the readable example is `Traffic/Code/Components/LaneConnections/ModifiedLaneConnections.cs:40-72`, which writes `DataMigrationVersion.LaneConnectionDataUpgradeV1` before its five fields and reads it back with a `<` branch and a `// DO NOT CHANGE ORDER` comment.

Rots: the serializer class names in the branch table and `ISerializeAsEnabled`'s two game users.

### The helper layer the corpus actually calls

`Colossal.Entities.EntitiesExtensions` is a static class of eleven extension methods over `EntityManager`, `ComponentLookup<T>` and `BufferLookup<T>` (`src/Colossal.Core/Colossal.Entities/EntitiesExtensions.cs:5-108`): `HasEnabledComponent` (two overloads), `HasEnabledBuffer`, `TryGetEnabledComponent` (two), `TryGetEnabledBuffer` (two), `TryGetComponent`, `TryGetBuffer`, `TryGetSharedComponent`. `using Colossal.Entities;` appears 170 times across the corpus, and `TryGetComponent` 941 times, `TryGetBuffer` 445.

These are shipped with the game, not a community package, and they are what turns the three-line `HasComponent` / `GetComponentData` dance into one call. `TryGetComponent` also exists on `ComponentLookup<T>` in the Entities package itself, so the corpus count mixes both.

Rots: the eleven method names — re-read `src/Colossal.Core/Colossal.Entities/EntitiesExtensions.cs`.

### Catalog gaps found in this sweep

Read `plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md` in full. Five of its entries do not name an ECS technique their source demonstrates and no other catalogued mod does. Sentences to add, each to that entry's **Demonstrates** paragraph:

- **Info Loom** — "The only use in this corpus of the source-generated per-entity job interface, showing that the Entities generators the official toolchain ships do work in a mod project." Evidence: `InfoLoom/InfoLoom/Systems/DemographicsData/Demographics.cs:22/51/77/390` and `C:\Users\Morgan\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Modding\Mod.props:72`.
- **Realistic Trips** — "Carries the game's own generated type-handle struct into each fork and refreshes every handle by hand at the top of the update, which is what a forked system must do once the source generator is no longer writing that code for it." Evidence: `Time2Work/NightShift/Systems/CitizenScheduleSystem.cs:48/130-138` and `Time2Work/NightShift/Systems/Time2WorkAttractionSystem.cs:128-132/287-320`.
- **Write Everywhere** — "The corpus's only cleanup components, which keep a residue entity alive after deletion so a disposal system can release the mesh and material handles a component owns." Evidence: `CS2-WriteEverywhere/BelzontWE/Components/WETextData/WETextDataMesh.cs:13/17`, `Components/WETextData/WETextDataMaterial.cs:14`, `BelzontWE/WriteEverywhereCS2Mod.cs:81`.
- **Recolor** — "Its own component shadows a vanilla one of the same name that arrived later, and the source shows both in use side by side, which is the readable case for namespace-qualifying every component a mod shares a name with." Evidence: `Recolor/Recolor/Domain/CustomMeshColor.cs:13/16` against `src/Game/Game.Rendering/CustomMeshColor.cs:8`, used together at `Recolor/Recolor/Systems/Palettes/TempAssignedPalettesSystem.cs:422-428`.
- **Area Bucket** — "Each job owns its own component and buffer lookups behind an assign-once and a refresh-per-update method, which is the clearest hand-written form of the handle discipline the generator otherwise supplies." Evidence: `AreaBucket/Systems/AreaBucketToolJobs/CollectAreaLines.cs:131-149` and `CollectLotLines.cs:100-112`.

Do not edit that catalog from here; these are proposals.

**Applied (2026-08-02, the ecs-in-this-game pass).** All five went into the catalog, each as one sentence in the entry's **Demonstrates** paragraph, reworded for that file's voice rather than pasted verbatim. Nothing here is outstanding.

## Bridge

This is a technique topic, and every mechanics reference sits on top of it. Four need something specific.

- **`simulation-time-and-units`** owns the `UpdateFrame` shared component jointly with this topic and should take the _chunk_ half from here: the per-family bucket counts (`src/Game/Game.Simulation/SimulationUtils.cs:11-57`), the authored-pin-then-load-balance assignment (`UpdateGroupSystem.cs:458-473`), and the two ways to consume it — `SetSharedComponentFilter` (`src/Game/Game.Simulation/BuildingUpkeepSystem.cs:1151`) or the in-job chunk test (`src/Game/Game.Simulation/AgingSystem.cs:68-71`). The _frequency_ half — `262144 / kUpdatesPerDay`, `SimulationUtils.GetUpdateFrame` — is already owned by `mod-lifecycle-and-ordering.md` and belongs to `simulation-time-and-units`, not here.
- **`citizens-and-households`** exercises this topic most directly, because `AgingSystem` is the canonical shape and it is a citizen system: a `Household` query with `None = {Deleted, Temp}` (`AgingSystem.cs:213-221`), a `BufferTypeHandle<HouseholdCitizen>` walked per chunk (`:72`), `ComponentLookup<Citizen>` marked `[NativeDisableParallelForRestriction]` for scattered writes (`:36-37`), and the `EndFrameBarrier` used to add and remove `Student`, `Worker`, `TravelPurpose`, `LeaveHouseholdTag` and to toggle `BicycleOwner` (`:62-63`, `:114`, `:121`, `:129-131`). Anything that reference says about citizen state changing has this file as its mechanism.
- **`zoning-buildings-and-land-value`** and **`city-services-and-coverage`** both need the `BatchesUpdated` rule, because both are about entities the player looks at: `src/Game/Game.Simulation/CityServiceUpkeepSystem.cs:359` and `src/Game/Game.Events/IgniteSystem.cs:117/128` add it to sub-objects and upgrades, not only to the building. A mod that changes a building's visible state and tags only the building gets a stale sub-object.
- **`roads-and-traffic`** needs `Owner` and `Temp` more than any other area: network entities are dense graphs of sub-objects reached through `Owner` (`src/Game/Game.Common/Owner.cs:6`), and the whole tool pipeline works on `Temp` copies whose `m_Original` is written back by the `Apply*System` family (`src/Game/Game.Tools/ApplyNetSystem.cs:555`). It also needs the enableable-buffer case, since `Game.Rendering.CustomMeshColor` is how lane fences are recoloured (`Recolor/Recolor/Systems/Palettes/TempAssignedPalettesSystem.cs:422-428`).
- **`transportation-and-vehicles`**, **`utilities-and-flow-networks`**, **`economy-and-companies`**, **`environment-and-pollution`** and **`city-state-and-progression`** each need the query and barrier material without needing anything unique from it, with one exception: `city-state-and-progression` needs the `Locked` enableable finding, because unlocking is `SetComponentEnabled<Locked>(entity, false)` (`src/Game/Game.Prefabs/UnlockSystem.cs:208`) and a naive `WithAll<Locked>()` query silently means "still locked".

Going the other way, two technique topics take material from here rather than rediscovering it:

- **`performance-and-memory`** owns allocation but should take from here: the 16 KB / 128-entity chunk geometry (`src/Unity.Entities/Unity.Entities/Chunk.cs:33-39`), why a shared component fragments chunks, `InternalBufferCapacity`'s default of `128 / sizeof(element)` (`src/Unity.Entities/Unity.Entities/TypeManager.cs:2292`) and what `(0)` buys, the fact that adding or removing a tag is an archetype move, and the absence of the collections safety system in the shipped build, which is why an aliasing bug here is a crash rather than an exception.
- **`save-serialization`** owns the format and the versioning but should take the _declaration_ rule from here: `IEmptySerializable` or `ISerializable` is the entire opt-in, the branch table at `src/Colossal.Core/Colossal.Serialization.Entities/ComponentSerializerLibrary.cs:45-99` decides which serializer a component gets, `ISerializeAsEnabled` is the opt-out from persisting enabled state, and the library re-scans after a mod assembly loads (`src/Game/Game.Modding/ModManager.cs:148-149`).
- **`mod-lifecycle-and-ordering`** is already shipped and is the seam for everything about _when_ a system runs. This file deliberately states no phase ordering; where a barrier's playback position appears above, it is the `SystemOrder.cs` registration and nothing more.

## Dead ends

- **The wiki answered live on 2026-08-02.** All three pages returned real content; `survey-wiki-inventory.md`'s snapshot was not needed. `ECS - Entity Component System` states "last edited 7 June 2024, game version 1.0"; `Queries` states 22 March 2024, version 1.0; `Common ECS Components` states 7 June 2024 with no version.
- **The `ECS - Entity Component System` page carries nothing on jobs, type handles or command buffers.** Fetched and checked explicitly: it has no mention of `EntityCommandBuffer`, barrier, `TypeHandle`, `ComponentTypeHandle`, `IJobChunk` or Burst. Its two code samples are a bare `GetEntityQuery` in `OnCreate` and a main-thread `ToComponentDataArray` / `CopyFromComponentDataArray` round trip on `ElectricityConsumer`. Everything this file says about jobs, handles and barriers is uncorroborated by the wiki because the wiki does not go there.
- **The wiki's own worked example uses an API nobody uses.** `EntityQuery.CopyFromComponentDataArray` exists (`src/Unity.Entities/Unity.Entities/EntityQuery.cs:269`) and is called **zero** times in `src/Game/` and **zero** times in the corpus. Its field names check out (`ElectricityConsumer.m_WantedConsumption`, `src/Game/Game.Buildings/ElectricityConsumer.cs:8`; `ResidentialProperty` is a real empty tag, `src/Game/Game.Buildings/ResidentialProperty.cs:8`), so the sample compiles — it is simply not how anything in this game is written. Recorded so the next pass does not treat its absence as a gap.
- **`SystemAPI.Query<T>()` and `Entities.ForEach`: zero in `src/Game/`, zero in the corpus.** Both generators ship (`Mod.props:66/68`). Nobody uses either. There is no evidence they fail; there is simply no practice.
- **`ISharedComponentData` in the corpus: zero.** Confirmed by a corpus-wide grep. Everything this file says about shared components comes from the game's five uses, with no mod practice to check it against.
- **`ICleanupComponentData` in `src/Game/`: zero.** Confirmed by grep over the whole of `src/Game/` including the generated registry. The interface exists (`src/Unity.Entities/Unity.Entities/ICleanupComponentData.cs:6`) and the engine honours it (`EntityComponentStore.cs:3656-3730`); the game never reaches for it. The single corpus user is `CS2-WriteEverywhere`, so this is a technique with engine support, one practitioner, and no vanilla precedent.
- **`ToolReadyBarrier` and `AudioEndBarrier`: zero corpus users.** `AudioEndBarrier` additionally has zero _game_ users — declared, registered nowhere, referenced by no system. Searched `src/Game/` for every reference and found only its own two files plus the generated type registry.
- **`EntityQueryOptions` in the corpus: zero.** Searched every repository for `EntityQueryOptions` and for `IgnoreComponentEnabledState`; both come back empty. The enableable-component filtering trap recorded above is therefore derived from the game's three opt-out sites and from what the engine does, with no mod that hit it and worked around it.
- **`RequireMatchingQueriesForUpdate`: one game use, zero corpus uses.** Not worth teaching; recorded so nobody re-derives it as a missing feature.
- **Whether `ComponentSerializerLibrary` skips a mod component whose assembly is the serializer system's own** was not resolved past the branch. The library adds a component to the returned `serializableComponents` list only when `type.Assembly != assembly` (`ComponentSerializerLibrary.cs:53-55`, `:77-79`), where `assembly` is the `SerializerSystem`'s — i.e. `Game.dll`. A mod component is never in `Game.dll`, so it always lands in that list, and it always gets a serializer regardless. What that list is _for_ downstream is `save-serialization`'s question and I did not follow it.
- **No count was taken of how many corpus `CreateCommandBuffer()` calls lack a matching `AddJobHandleForProducer`.** The raw ratio (217 to 110) is not evidence, because a main-thread command buffer legitimately needs no producer handle, and separating the two would mean reading all 217 call sites. Do not ship the ratio as a defect rate.
- **The seed survey's ECS section was superseded rather than confirmed.** `survey-mods-techniques.md` §3.1-3.3 and §3.5 were written against twelve repositories on 2026-07-31; the checkout now holds twenty and `Cities2-TrafficLightsEnhancement` is gone. Its per-repo `IComponentData` and `[BurstCompile]` counts no longer match (it reports `Traffic 20 / Platter 13 / Anarchy 8` for components against `Traffic 20 / CS2-Platter 13 / Anarchy 8` still holding, but `CS2-NetworkTools 28`, `CS2-WriteEverywhere 23` and `Time2Work 11` were not in that pass at all), and its `IJobChunk 123 / IJob 30 / IJobParallelFor 17 / IJobFor 9` totals are superseded by the twenty-repo counts above. Its `IJobEntity` claim is refuted outright (above). Everything else in those sections was re-derived from scratch here rather than carried forward.
- **`EndFrameBarrier` use from a lifecycle hook could not be resolved.** The window claim above is provable for `OnUpdate` bodies and not for `OnGameLoadingComplete`, `OnGameLoaded`, `OnGamePreload` or `OnWorldReady`, because those fire from an async continuation in `GameManager.Load` (`src/Game/Game.SceneFlow/GameManager.cs:1126`) whose position relative to the frame's phase walk is a scheduling property, not a static one. Settling it needs a running game — which is what the sibling Unity plugin is for — not another read.
- **No count was taken of how much of `src/Game/`'s ECS surface is unreachable from a mod.** Everything cited here is `public`, checked at the declaration, but no sweep established which useful types are `internal` and therefore off-limits without reflection.
- **One entry was appended to `conflicts.md`** on whether shipped prose should teach the per-entity job interface at all now that it is proven to work. It was ruled the same day and sits under `## Ruled`; the ruling is carried at the finding it governs above. Nothing else in this topic resisted the decompile.
