# Performance and memory in this game: not leaking and not stalling

**Baseline.** Decompiled game version 1.6.0f1, Unity 2022.3.71f1. Mod corpus (22 repositories under `C:\Users\Morgan\Documents\Projets\cs2-third-party-mods\`) read 2026-08-04. Wiki fetched live 2026-08-04 — the bot challenge did not fire on the second attempt, so `How To Avoid Memory Leaks` is cited from the live page rather than through `survey-wiki-inventory.md`'s snapshot; its own footer states last edited 7 June 2024, game version 1.0.
Two further first-party sources appear here and neither is the decompile. The **installed official modding toolchain**, reached at `%CSII_TOOLPATH%` and cited here as `cs2-moddingtools/<file>:<line>` after the package that delivers it, settles what a mod project compiles with and what the post-processor does to it; it carries no version of its own. The **installed game** at `C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\` supplies the post-processor's own command-line surface. One prevalence claim uses the Paradox mods cache at `%CSII_USERDATAPATH%/.cache/Mods/pdx_mods/`, 431 mod directories read the same day, and is marked as such.
The **running game** is a fourth, reached two ways. Over the Mono soft debugger, whose findings sit in the "Settled against the running game" section below and cite the expressions evaluated rather than file lines; and through a purpose-built mod at `C:\Users\Morgan\Documents\Projets\cs2-test-mod\`, whose findings sit in the section after it and state the method that established each rather than citing a file, since the mod is stripped back to empty once an experiment closes. Each section states its own dates.

## Findings

### The ECS world outlives every city, so `OnDestroy` is a process-exit hook and nothing else

This is the fact the rest of the topic hangs off, and it is not what a reader arriving from stock ECS expects.

`GameManager.CreateWorld()` is called from exactly one place — the boot sequence inside `Awake`, before any city exists (`src/Game/Game.SceneFlow/GameManager.cs:591`, the method at `:2363-2382`, `m_World = new World("Game")` at `:2368`).
`GameManager.DestroyWorld()` is called from exactly two — `GameManager.OnDestroy()` (`:750-756`) and `TerminateGame()` (`:793`) — and both mean the application is going away. Its body is `World.DisposeAllWorlds()` plus `DefaultWorldInitialization.CleanupEntityComponentStore()` (`:2412-2418`).

So one world is created at process start and torn down at process exit. Loading a city, returning to the main menu, loading another city and quitting to the menu again all happen inside the same world with the same system instances.

Three consequences a mod has to design around:

- **`OnCreate` runs once per process and `OnDestroy` runs once per process.** A `Allocator.Persistent` container allocated in `OnCreate` and disposed in `OnDestroy` is correct and costs one allocation for the whole session. A container allocated per loaded city and disposed in `OnDestroy` accumulates one leak per city the player opens, and the player sees it only as memory climbing across a long session.
- **Per-city state must be cleared, not recreated.** The game's own spatial indices demonstrate the shape: `Game.Objects.SearchSystem` allocates its two quadtrees in `OnCreate` (`src/Game/Game.Objects/SearchSystem.cs:359-360`) and in `PreDeserialize` completes the outstanding handles and calls `Clear()` on the trees rather than disposing and reallocating them (`:453-465`).
- **`OnDestroy` is not a place to observe leaks.** By the time it runs, the process is exiting and nothing will report what was still allocated. The load-boundary hooks are where a mod can see its own accumulation.

Rots: the two call sites of `DestroyWorld`. Re-check `src/Game/Game.SceneFlow/GameManager.cs` for any new caller before assuming the world still spans a session.

### Managed against unmanaged, and where a mod's own data actually sits

Four places, and only one of them is garbage-collected.

**ECS component data is unmanaged, always.** The game declares zero managed components: a grep of `src/Game/` for `class <Name> : ... IComponentData` returns nothing, and the same grep over all 22 corpus repositories returns nothing. Every component in this game is a struct in chunk memory. `NativeArray<T>` refuses a managed `T` outright, and the check is `[BurstDiscard]` and `[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]` (`src/UnityEngine.CoreModule/Unity.Collections/NativeArray.cs:304-313`, the `IsUnmanagedAndThrow` helper), which is the first of many checks below that do not exist in the shipped build.

**Chunk memory itself is unmanaged and owned by the entity store, not by the mod.** A mod declaring a component pays for it in chunk space and pays nothing to manage it.

**Native containers a mod allocates are unmanaged, untracked and invisible.** `NativeArray<T>` in this build carries exactly three fields — `m_Buffer`, `m_Length`, `m_AllocatorLabel` (`NativeArray.cs:218-222`) — and allocates through `UnsafeUtility.MallocTracked(size, align, allocator, 0)` (`:295-302`). Nothing about it is reachable from the GC, so a leaked one never shows up in a managed heap dump and, for the reason in the leak-detection finding below, never shows up anywhere else either.

**A mod's managed objects are GC'd normally, with one large exception.** Systems, the `IMod` implementation, settings objects and anything a mod hangs off them are ordinary managed objects. Prefabs are the exception, and it is a permanent one: `PrefabBase` derives from `ComponentBase`, and `ComponentBase` derives from `UnityEngine.ScriptableObject` (`src/Game/Game.Prefabs/ComponentBase.cs:13`, `src/Game/Game.Prefabs/PrefabBase.cs:16`). `PrefabSystem` holds every registered prefab in a `List<PrefabBase>` and in three `Dictionary<PrefabBase, …>` keyed by the object (`src/Game/Game.Prefabs/PrefabSystem.cs:44/50/52/54`, initialised at `:75-80`). A prefab a mod synthesises is therefore a managed object rooted for as long as it stays registered, and the game's own `Resources.UnloadUnusedAssets()` sweeps cannot collect it while it is. `PrefabSystem.RemovePrefab` is public and releases every one of those roots — swap-removing from `m_Prefabs` (`:252/256`), erasing from `m_Entities` (`:257`) and `m_IsUnlockable` (`:258`) — so registration rather than the process is what bounds the rooting (`src/Game/Game.Prefabs/PrefabSystem.cs:191-259`). It is not a bookkeeping call: it also adds `Deleted` to the prefab's entity (`:203`) and rewrites a `PrefabData.m_Index` to a sentinel (`:254-255`).

**The bridge between the two sides, where the game needs one, is an integer index rather than a reference.** `Game.Net.ArrowMaterial` is a shared component whose whole content is `int m_Index` (`src/Game/Game.Net/ArrowMaterial.cs:6-9`); the `UnityEngine.Material` it names lives in a managed array on the managed system that consumes it (`src/Game/Game.Rendering/AggregateMeshSystem.cs:1458-1478`, the shared type handle at `:1066/1131`, the query at `:1239`). That is the vanilla pattern for "an entity needs a managed thing": the entity carries a number, a managed system owns the table.

### Four allocators, not three, and each one's lifetime

The wiki names three (`Temp`, `TempJob`, `Persistent`, https://cs2.paradoxwikis.com/How_To_Avoid_Memory_Leaks). The enum has three usable values plus two sentinels and a custom range:

```
Invalid = 0, None = 1, Temp = 2, TempJob = 3, Persistent = 4, AudioKernel = 5, FirstUserIndex = 64
```

(`src/UnityEngine.CoreModule/Unity.Collections/Allocator.cs:5-13`.)

**Verdict: the wiki's three are right as far as they go and the list is incomplete.** A fourth allocator is in constant use in this game and the wiki never names it: `World.UpdateAllocator`, a `RewindableAllocator` reached as `base.World.UpdateAllocator.ToAllocator`, whose handle index sits in the custom range (`AllocatorHandle.IsCustomAllocator => Index >= 64`, `src/Unity.Collections/Unity.Collections/AllocatorManager.cs:55`; `ToAllocator` at `:46-53`). `src/Game/` uses it 79 times and three corpus mods use it. It belongs in any list a reference ships.

What each one is, and what the evidence for it is:

| Allocator               | Lifetime                       | Freed by                              | Evidence                                                                                                                                                                                                                                                                                                           |
| ----------------------- | ------------------------------ | ------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Temp`                  | The current frame, per thread  | The native job system, automatically  | Wiki; and the game relies on it, allocating `Temp` containers inside job bodies and never disposing them (`src/Game/Game.Simulation/WaterPipeFlowJob.cs:526-527`)                                                                                                                                                  |
| `TempJob`               | Four frames                    | The caller's `Dispose`                | Wiki only; nothing in the decompile states the four-frame figure, and see the leak-detection finding for why nothing enforces it here                                                                                                                                                                              |
| `Persistent`            | Until disposed                 | The caller's `Dispose`                | Wiki; and `src/Game/`'s own systems allocate in `OnCreate` and dispose in `OnDestroy` (`src/Game/Game.Simulation/CommercialDemandSystem.cs:426-433` against `:443-453`)                                                                                                                                            |
| `World.UpdateAllocator` | The current frame and the next | The world's own rewind, automatically | `DoubleRewindableAllocators.Update()` swaps between two rewindable allocators and rewinds the one it swaps to (`src/Unity.Collections/Unity.Collections/DoubleRewindableAllocators.cs:35-41`), driven once per `MainLoop` tick from `GameManager.UpdateWorld` (`src/Game/Game.SceneFlow/GameManager.cs:2385-2391`) |

The double-buffering is the whole point of the fourth one: because the allocator that gets rewound is the one _not_ in use, memory handed to a job scheduled this frame is still valid when that job runs, and no `Dispose` is needed or wanted. The game reaches for it wherever it gathers a query result to feed a job — `ToEntityListAsync(base.World.UpdateAllocator.ToAllocator, out outJobHandle)` (`src/Game/Game.Citizens/CitizenInitializeSystem.cs:311`, `src/Game/Game.Citizens/HouseholdInitializeSystem.cs:372-375`, `src/Game/Game.Rendering/NetColorSystem.cs:2675`).

**The rewind is gated on the world updating.** `UpdateWorld` only calls `ResetUpdateAllocator` when `shouldUpdateWorld` is true (`GameManager.cs:2385-2391`), so on a frame where the world is not ticking the allocator is not rewound and its contents persist. Nothing in the corpus depends on this either way.

Corpus census of the three named allocators, over 22 repositories: `Allocator.Temp` 529, `Allocator.TempJob` 193, `Allocator.Persistent` 150, `Allocator.None` 0. The game's own: `Temp` 1431, `TempJob` 907, `Persistent` 645, `None` 2, `Invalid` 0. The ratios are close enough that no lesson hangs on them; what is worth carrying is that `Temp` dominates both, and that four repositories (`ExtraAssetsImporter`, `HallOfFame`, `LineTool-CS2`, `RoadBuilder-CSII`) allocate almost nothing at all.

Rots: the enum members and the `FirstUserIndex` value. Re-check `src/UnityEngine.CoreModule/Unity.Collections/Allocator.cs`.

### The collections safety system is compiled out of everything a mod links against, and the mod's own compile does not put it back

This is why every other finding in this file matters more here than it would in an editor project.

**In the shipped assemblies the checks are empty.** `NativeArray<T>` has no `AtomicSafetyHandle` field at all — three fields, none of them a safety handle (`NativeArray.cs:218-222`). Every guard method is `[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]` with an empty body: `CheckElementReadAccess` (`:315-319`), `CheckElementWriteAccess` (`:321-324`), and eleven more in the same file. `CheckAllocateArguments` still has a body but is `[Conditional]` and is not called from `Allocate` (`:279-302`), so `new NativeArray<T>(-1, Allocator.None)` is not rejected. The indexer is a raw `UnsafeUtility.ReadArrayElement` / `WriteArrayElement` with no bounds check (`:233-246`). 44 files under `src/Unity.Collections/` carry the same conditional, and `Colossal.Collections`' own containers match — `NativeQuadTree<TItem, TBounds>.CheckRead()` and `CheckWrite()` are empty (`src/Colossal.Collections/Colossal.Collections/NativeQuadTree.cs:35-43`).

**The mod project does not define the symbol.** The toolchain's `Mod.props` contains no `DefineConstants` element of any kind — its property groups set `OutputType`, `TargetFramework`, `LangVersion` and a long list of paths, and nothing else (`cs2-moddingtools/Mod.props`, whole file, 82 lines). `Mod.targets` adds none either (same directory, 158 lines). So a mod compiles without `ENABLE_UNITY_COLLECTIONS_CHECKS`, without `UNITY_EDITOR` and without `DEVELOPMENT_BUILD` unless its own csproj adds them, and even if it did, the guards it would re-enable live in the game's already-compiled IL.

What this costs, concretely: an out-of-bounds index into a native container is a read or write of unrelated memory; two jobs writing the same container concurrently is silent corruption; disposing a container a running job still holds is a use-after-free. None of the three throws. The failure surfaces as a crash somewhere else, or as wrong numbers, at some later frame.

**The one guard that does fire.** `NativeArray<T>.Dispose(JobHandle)` throws `InvalidOperationException` when the array came from a custom allocator, and that check is not conditional (`NativeArray.cs:344-353`). So `someArray.Dispose(handle)` on an array allocated from `World.UpdateAllocator` throws at runtime — the one place in this area where the mistake announces itself.

Unconfirmed: whether the same call on a `NativeList<T>` is a double free. `NativeList<T>.Dispose(JobHandle)` carries no equivalent guard and schedules the destroy job unconditionally (`src/Unity.Collections/Unity.Collections/NativeList.cs:249-263`), yet the game itself disposes an `UpdateAllocator`-backed list exactly that way and ships (`src/Game/Game.Rendering/NetColorSystem.cs:2675` allocating, `:2834` disposing with a handle). Either the rewindable allocator's free is a no-op for the block or the pairing is benign for another reason; settling it means running the game with a mod that does the same and watching for a crash, which the sibling Unity plugin can drive.

### The game turns native leak detection off, in a method called `EnableMemoryLeaksDetection`

`GameManager.EnableMemoryLeaksDetection()` has one statement: `NativeLeakDetection.Mode = NativeLeakDetectionMode.Disabled;` (`src/Game/Game.SceneFlow/GameManager.cs:1877-1880`). It is called once, from `Awake`, early in the boot sequence (`:529`).

`NativeLeakDetection.Mode` is a live setter into the native runtime (`src/UnityEngine.CoreModule/Unity.Collections/NativeLeakDetection.cs`, wrapping `UnsafeUtility.SetLeakDetectionMode`, declared `extern` at `src/UnityEngine.CoreModule/Unity.Collections.LowLevel.Unsafe/UnsafeUtility.cs:110`), and the mode enum is `Disabled = 1, Enabled, EnabledWithStackTrace` (`src/UnityEngine.CoreModule/Unity.Collections/NativeLeakDetectionMode.cs:7-11`). Because the allocation path goes through `MallocTracked` / `FreeTracked` (`NativeArray.cs:299`, `:337`), leak tracking is a native facility rather than a `[Conditional]` one — it is present in the shipped build and switched off.

**So a leaked `TempJob` or `Persistent` allocation produces no warning, no log line, and no console output in this game.** The four-frame `TempJob` rule the wiki states is real Unity behaviour and has no enforcement here.

A mod can switch it back on, from anywhere, and one does. `Time2Work/NightShift/Mod.cs:175` sets `NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace` as the last statement of `OnLoad`, unconditionally, in shipped code. `Traffic/Code/Mod.cs:116` has the identical line commented out — the author used it and then took it out of the shipped build. Nobody else in the corpus touches it.

Two things follow and they pull in opposite directions. The switch is the only leak diagnostic this game has, and `EnabledWithStackTrace` names the allocation site, which is exactly what a mod author debugging their own accumulation needs. It is also process-global: it is set on the native runtime, not on the mod, so a mod that enables it imposes stack-trace capture on every native allocation the game and every other loaded mod makes. `GameManager.Awake` has already run by the time any mod loads, so a mod's setting sticks for the session.

**Ruled (2026-08-04, ticket 18; conflicts.md).** The reference teaches the switch, bound to a condition. A reader whose memory climbs gets the one instrument this game has, and the reference states in the same breath that it must never reach a shipped default path — set it from a debug configuration, or behind a mod setting that is off unless the player turns it on. The ground is that the cost is not paid by the mod that opts in: the mode is a property of the native allocator rather than of the calling assembly, so it lands on the game's own allocations and on every other mod in the player's load order. That is what makes this the one place the reference asks a reader to think about other people's mods, and the asymmetry is the reason rather than an exception to a style rule.

Two things follow for the prose. The size of the cost is not established here, so the reference states the scope — every native allocation in the process, plus a managed stack capture under `EnabledWithStackTrace` — and claims no figure. And the reference does not name the corpus mod that ships it enabled; that a practice exists in the wild is not something shipped prose credits, and the technique stands on its own authority either way.

Rots: the method name and its line. `EnableMemoryLeaksDetection` disabling detection is the kind of line that gets fixed; re-check `src/Game/Game.SceneFlow/GameManager.cs`.

### Disposing a native container a job may still be reading: two mechanisms, and which containers offer which

The mechanism the wiki teaches is real and is the right default. `NativeArray<T>.Dispose(JobHandle inputDeps)` builds a `NativeArrayDisposeJob` carrying the raw buffer pointer and the allocator label, schedules it on `inputDeps`, nulls the local's pointer and returns the new handle (`NativeArray.cs:344-368`). The free happens on a worker thread after the job that was reading finishes; the caller does not block.

**Not every container has that overload.** Counted by declaration across `src/Unity.Collections/Unity.Collections/`, `src/Colossal.Collections/Colossal.Collections/` and `src/UnityEngine.CoreModule/Unity.Collections/`:

Sixteen do: `NativeArray`, `NativeList`, `NativeHashMap`, `NativeHashSet`, `NativeParallelHashMap`, `NativeParallelHashSet`, `NativeParallelMultiHashMap`, `NativeQueue`, `NativeRingQueue`, `NativeStream`, `NativeReference`, `NativeBitArray`, `NativeText`, `NativeKeyValueArrays`, and Colossal's own `NativeAccumulator` (`src/Colossal.Collections/Colossal.Collections/NativeAccumulator.cs:91`) and `NativeParallelQueue`.

Count the ones that DO, because that list is short and stable: across the 41 files of `src/Colossal.Collections/Colossal.Collections/`, exactly two declare `Dispose(JobHandle)` — `NativeAccumulator` (`NativeAccumulator.cs:91`) and `NativeParallelQueue` (`NativeParallelQueue.cs:312`). The rest either offer only the synchronous `Dispose()` — `NativeQuadTree` (`NativeQuadTree.cs:50-55`) and `NativeValue` (`NativeValue.cs:37-40`) among them — or have nothing to dispose at all, which is where `StackList<T>` (`StackList.cs:11`), the `AnimationCurve` structs and the static `CurveSampling` sit. Counting the ones that lack it went wrong three times in this pipeline — five, then three, then six — because the set depends on where you draw the line around helper and `Unsafe*` types; the two that have it does not.

**For every container without the overload the discipline is complete-then-dispose, and the vanilla search systems show it.** `Game.Objects.SearchSystem.OnDestroy` completes the read handle and the write handle for each tree before disposing it (`src/Game/Game.Objects/SearchSystem.cs:364-373`) — four `Complete()` calls guarding two `Dispose()` calls. `PreDeserialize` does the same before `Clear()` (`:453-465`).

The two mechanisms are not interchangeable and the choice is forced by the type, not by taste. `Dispose(JobHandle)` is free and asynchronous; `Complete()` is a main-thread stall until the job finishes. A mod holding a `NativeQuadTree` has no way to avoid the stall at teardown, which is fine because teardown happens once per process.

### The canonical shape, and it is ten lines

`Game.Areas.ServiceDistrictSystem.OnUpdate` is the whole idiom with nothing else in it (`src/Game/Game.Areas/ServiceDistrictSystem.cs:78-90`):

```csharp
JobHandle outJobHandle;
NativeList<Entity> deletedDistricts = m_DeletedDistrictQuery.ToEntityListAsync(Allocator.TempJob, out outJobHandle);
JobHandle jobHandle = JobChunkExtensions.ScheduleParallel(new RemoveServiceDistrictsJob
{
    m_DeletedDistricts = deletedDistricts,
    m_ServiceDistrictType = InternalCompilerInterface.GetBufferTypeHandle(ref __TypeHandle.__Game_Areas_ServiceDistrict_RW_BufferTypeHandle, ref base.CheckedStateRef)
}, m_ServiceDistrictQuery, JobHandle.CombineDependencies(base.Dependency, outJobHandle));
deletedDistricts.Dispose(jobHandle);
base.Dependency = jobHandle;
```

Five things happen and every one of them is load-bearing: the gather is asynchronous and hands back its own handle; that handle is combined with `base.Dependency` rather than replacing it; the container is disposed against the scheduled handle rather than immediately; `base.Dependency` is assigned so the next system waits; and nothing is completed.

**Verdict: the wiki's worked example teaches the right mechanism and does not compile.** https://cs2.paradoxwikis.com/How_To_Avoid_Memory_Leaks ships a sample whose `OnUpdate` reads `JobHandle jobHandle = JobChunkExtensions.ScheduleParallel(job, query, JobHandle.CombineDependencies(base.Dependency, jobHandle));` — using `jobHandle` in its own initialiser — never assigns `base.Dependency`, declares a job field `m_NativeQueue` and assigns `job.m_ActionQueue`, and constructs `new NativeArray<Object>(Allocator.Temp)` with no length and a managed element type. The concepts it states are all confirmed above; the code is illustrative rather than copyable, and a reference that borrows the sample would ship four errors. The decompiled game is authoritative here and `ServiceDistrictSystem` is the sample that works.

### The job-handle discipline, as the vanilla systems keep it

Six rules, each with the site that proves it.

1. **Combine, never replace, when consuming someone else's handle.** `JobHandle.CombineDependencies(base.Dependency, dependencies)` at every schedule site. Where more than three handles are involved the game uses `Colossal.Entities.JobUtils.CombineDependencies`, twelve overloads that take more handles than Unity's three-argument maximum and nest it (`src/Colossal.Core/Colossal.Entities/JobUtils.cs`, whole file), used 62 times under `src/Game/`. Unity's own overloads stop at three, which is why the helper exists.
2. **Assign `base.Dependency` after scheduling.** Nothing downstream waits for a job whose handle was never published.
3. **Register with every provider whose data you took.** See the reader/writer protocol below.
4. **Register with the barrier if you wrote through its command buffer.** `AddJobHandleForProducer` — the contract is `ecs-in-this-game`'s and is not restated here.
5. **Dispose against the handle, not against nothing.** 429 `Dispose(<handle>)` calls under `src/Game/`, of which 42 are `Dispose(base.Dependency)` and the rest pass a named local.
6. **Complete only where the type forces it.** `.Complete()` appears in the corpus 125 times, concentrated in `Traffic` (23), `CS2-WriteEverywhere` (20), `InfoLoom` (17) and `AreaBucket` (13). Each one is a main-thread stall, and most of them are in teardown or in a main-thread readback where they are correct.

### The reader/writer protocol: 41 systems, 53 readers, 55 writers, and an asymmetry that is not decoration

Where one system owns data other systems' jobs read, the game exposes three methods rather than a property. The shape, from `Game.Objects.SearchSystem` (`src/Game/Game.Objects/SearchSystem.cs:421-451`):

```csharp
public NativeQuadTree<Entity, QuadTreeBoundsXZ> GetStaticSearchTree(bool readOnly, out JobHandle dependencies)
{
    dependencies = (readOnly ? m_StaticWriteDependencies : JobHandle.CombineDependencies(m_StaticReadDependencies, m_StaticWriteDependencies));
    return m_StaticSearchTree;
}

public void AddStaticSearchTreeReader(JobHandle jobHandle)
{
    m_StaticReadDependencies = JobHandle.CombineDependencies(m_StaticReadDependencies, jobHandle);
}

public void AddStaticSearchTreeWriter(JobHandle jobHandle)
{
    m_StaticWriteDependencies = jobHandle;
}
```

Read it as a reader/writer lock expressed in handles. A reader needs only the writer's handle, because readers do not conflict with each other. A writer needs the readers' and the writer's, because it conflicts with everything. That is what the `readOnly` flag selects.

**The asymmetry is the part that is easy to copy wrongly.** `AddReader` combines; `AddWriter` assigns. Assignment is only safe when there is one writer, and the owning system is it — its own `OnUpdate` calls `AddStaticSearchTreeWriter(base.Dependency)` right after scheduling (`:417`). Five of the game's six search systems assign (`src/Game/Game.Objects/SearchSystem.cs:438`, `src/Game/Game.Net/SearchSystem.cs:850/860`, `src/Game/Game.Zones/SearchSystem.cs:186-189`, `src/Game/Game.Routes/SearchSystem.cs:407-410`, `src/Game/Game.Effects/SearchSystem.cs:411-414`); the sixth combines (`src/Game/Game.Areas/SearchSystem.cs:302-305`). The inconsistency is in the game, not in the reading of it, and combining is the form that survives a second writer.

**Census of the protocol under `src/Game/`:** 53 `public void Add*Reader(JobHandle)` methods across 41 files, and 55 `Add*Writer(JobHandle)`. Not all of them guard a spatial index. The ones a mod is most likely to meet:

- The six search trees — `Game.Objects` (static and moving), `Game.Net` (net and lane), `Game.Zones`, `Game.Areas`, `Game.Routes`, `Game.Effects`.
- Terrain and water surfaces — `TerrainSystem.AddCPUHeightReader`, `AddCPUDownsampleHeightReader` (`src/Game/Game.Simulation/TerrainSystem.cs`), `WaterSystem.AddSurfaceReader`, `AddVelocitySurfaceReader`, `AddMaxHeightSurfaceReader`, `AddActiveReader` (`src/Game/Game.Simulation/WaterSystem.cs`).
- The demand and counting singletons — `CommercialDemandSystem.AddReader`, `IndustrialDemandSystem.AddReader`, `ResidentialDemandSystem.AddReader`, `CountHouseholdDataSystem.AddHouseholdDataReader`, `CountCompanyDataSystem.AddReader`, `CountVehicleDataSystem.AddVehicleDataReader`, `TaxSystem.AddReader`, `ResourceSystem.AddPrefabsReader`, `ZoneSystem.AddPrefabsReader`.
- The update-collect systems that publish dirty bounds — `Game.Objects`, `Game.Net`, `Game.Zones`, `Game.Areas`, each an `UpdateCollectSystem` with one or four `Add*BoundsReader`.
- The pathfinding queue — `PathfindQueueSystem.AddDataReader` (`src/Game/Game.Pathfind/PathfindQueueSystem.cs:434`).
- The culling and batching data — `PreCullingSystem.AddCullingDataReader`, `BatchManagerSystem`'s three.

**The producer side of the same protocol, when a mod owns the data.** `CommercialDemandSystem` is the tidiest example of a system that publishes several arrays through one write handle (`src/Game/Game.Simulation/CommercialDemandSystem.cs`): seven `Allocator.Persistent` containers allocated in `OnCreate` (`:426-433`), disposed in `OnDestroy` (`:443-453`), four `Get*(out JobHandle deps)` accessors that all hand back `m_WriteDependencies` (`:364-386`), one `AddReader` that combines (`:388-391`), and an `OnUpdate` that schedules against `JobUtils.CombineDependencies(base.Dependency, m_ReadDependencies, outJobHandle, deps)`, stores the result in `m_WriteDependencies`, and then registers itself as a reader with the three systems whose data it consumed (`:628-632`).

### The cleanup-component pattern, for a component that owns a managed graphics resource

Native containers are one half of disposal and the harder half is the other: an entity that owns a `Material`, a `Mesh` or any other managed object with an unmanaged resource behind it. `Dispose(JobHandle)` is no help, because the thing to free is not in the container and freeing it is a main-thread call. The engine's answer is `ICleanupComponentData`, and the whole of what it does is delay the entity's destruction.

**What the engine does with it.** `ICleanupComponentData` is an empty marker interface extending `IComponentData` and `IQueryTypeParameter` (`src/Unity.Entities/Unity.Entities/ICleanupComponentData.cs:6-8`); two siblings exist for the other component kinds, `ICleanupBufferElementData` and `ICleanupSharedComponentData` (same directory). The behaviour is entirely in the entity store, in four steps:

1. **Any archetype containing one is flagged.** `ArchetypeCleanupNeeded` walks the archetype's types and returns true on the first `IsCleanupComponent`; the flag is set when the archetype is created (`src/Unity.Entities/Unity.Entities/EntityComponentStore.cs:2650-2660`, set at `:2621-2624`).
2. **Each such archetype gets a residue archetype**, built as `{Entity}` plus an internal `CleanupEntity` tag plus _every cleanup component and nothing else_ — the loop copies only types where `IsCleanupComponent` is true (`:3656-3683`; `CleanupEntity` is an internal zero-size component at `src/Unity.Entities/Unity.Entities/CleanupEntity.cs:6-8`).
3. **Destroying the entity moves it instead of freeing it.** `DestroyBatch` deallocates outright when `!archetype->CleanupNeeded`, and otherwise moves the batch into the residue archetype (`EntityComponentStore.cs:4386-4400`). So after `DestroyEntity`, the entity handle is still live and still carries your cleanup component; every ordinary component is gone.
4. **The entity is freed when the last cleanup component is removed.** An archetype of exactly `{Entity, CleanupEntity}` — `TypesCount == 2` with the second type being `CleanupEntity` — is flagged `CleanupComplete` (`:2641-2648`), and every `Move` into a `CleanupComplete` archetype deallocates rather than moving (`:3380-3387`, `:3405-3414`).

One guard rail exists upstream and is compiled out here: `AssertArchetypeDoesNotRemoveCleanupComponents` would throw `"Cleanup components may not be removed via SetArchetype"` (`:5184-5216`), but it carries `[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]` and `[Conditional("UNITY_DOTS_DEBUG")]` (`:5182-5184`) and has zero call sites in the shipped assemblies — `EntityManager.SetArchetype` goes straight to `StructuralChange.MoveEntityArchetype` (`src/Unity.Entities/Unity.Entities/EntityManager.cs:1900-1912`). So `SetArchetype` drops a cleanup component silently: the entity carries on without it, and since its archetype is then no longer `CleanupNeeded`, the eventual `DestroyEntity` deallocates it outright. Removing one is a deliberate `RemoveComponent`, which is the point.

So the contract a mod is signing up to is: _the entity outlives its own destruction until you say otherwise, and if you never say otherwise it never goes away._

**The one corpus implementation, end to end.** `CS2-WriteEverywhere` is the only mod in twenty-two repositories that uses the interface, with three declarations. Two of them own managed graphics resources and are the worked example:

`WETextDataMaterial` is `IComponentData, IDisposable, ICleanupComponentData` (`CS2-WriteEverywhere/BelzontWE/Components/WETextData/WETextDataMaterial.cs:14`). Its managed payload is one field, `private GCHandle ownMaterial` (`:37`), assigned `GCHandle.Alloc(materialArray)` where `materialArray` is a `Material[]` (`:301`). `GCHandle` is the mechanism the whole pattern turns on: it is an unmanaged 8-byte token, so the struct stays blittable and can live in a chunk, while the managed array it names is reachable through `ownMaterial.Target`. Its `Dispose()` is five lines that matter (`:243-257`):

```csharp
if (ownMaterial.IsAllocated)
{
    if (ownMaterial.Target is Material[] matArray)
    {
        foreach (var material in matArray) { GameObject.Destroy(material); }
    }
    ownMaterial.Free();
}
ownMaterial = default;
```

Two frees, not one, and they are different things. `GameObject.Destroy(material)` releases the engine-side resource; `ownMaterial.Free()` releases the GC handle. Skipping the second leaks the handle table entry and, because `GCHandle.Alloc` with no type argument produces a **strong** handle, roots the `Material[]` for the life of the process.

`WETextDataMesh` is the same three interfaces (`CS2-WriteEverywhere/BelzontWE/Components/WETextData/WETextDataMesh.cs:13`) over `private GCHandle basicRenderInformation` (`:17`), and it makes the opposite choice: `GCHandle.Alloc(bri, GCHandleType.Weak)` (`:105`, `:117`). A weak handle does not root its target, so the render information can be collected out from under the component — which the mod handles by re-checking `Target as IBasicRenderInformation` for null on every use and rebuilding when it has gone (`:137-147`). Its `Dispose()` is correspondingly just `basicRenderInformation.Free()` (`:127-133`). The pair is the useful half of the example: strong when the component owns the object's lifetime, weak when a cache owns it and the component is only a reference.

**The disposal system.** `WETemplateDisposalSystem` is a `GameSystemBase` registered at `SystemUpdatePhase.Cleanup` (`CS2-WriteEverywhere/BelzontWE/Templates/WETemplateDisposalSystem.cs:14`, registered at `CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:81`). Three things about it are the pattern rather than the mod:

- **What it queries, and why that one query catches two different populations.** `m_componentsToDispose` matches `Any = {WETextDataMain, WETextDataMaterial}` with `None = {WETextComponentValid}` (`:30-44`). `WETextDataMain` and `WETextComponentValid` are both ordinary `IComponentData` (`CS2-WriteEverywhere/BelzontWE/Components/WETextData/WETextDataMain.cs:8`, `WETextComponentValid.cs:5-8`); `WETextDataMaterial` is the cleanup one. So the query matches a _live_ entity the mod has invalidated by stripping its own tag, through the `WETextDataMain` arm — and it also matches a _residue_ entity somebody else destroyed, through the `WETextDataMaterial` arm, because that component survived the destruction while the validity tag did not. One query, both paths, and it is the cleanup interface that makes the second one reachable at all. The textbook form is narrower — "has the cleanup component, lacks the ordinary component that always accompanies it", which is precisely the residue archetype's shape — and this mod's `Any`/`None` pair is a superset of it that also serves its own explicit teardown.
- **Why the job enqueues instead of disposing.** `WEComponentDisposalJob` reads each doomed entity through a `ComponentLookup`, `Enqueue`s the component _value_ into a `NativeQueue<WETextDataMesh>` or `NativeQueue<WETextDataMaterial>`, and issues `RemoveComponent<T>` plus `DestroyEntity` into an `EndFrameBarrier` command buffer (`:149-172`). It never calls `Dispose()`. It cannot: `Dispose()` calls `GameObject.Destroy` and `GCHandle.Free`, both managed calls into the Unity runtime, and neither is legal from a job — which is the same reason the component holds a handle rather than a reference in the first place. Enqueuing the value rather than the entity is what makes that work: the handle travels out of the job as plain unmanaged data, so the main thread can still reach the managed object after the component itself is gone.
- **Why the free happens on the main thread.** `OnUpdate` gates on `!m_componentsToDispose.IsEmpty`, schedules the job, `.Complete()`s it immediately, then drains both queues on the main thread calling `meshData.Dispose()` and `materialData.Dispose()` per element (`:85-121`). The `Complete()` is a stall and is unavoidable here; the job exists to _find_ the work in parallel, not to do it.

**Why `Cleanup` is the phase.** `Cleanup` is driven from `GameManager.PostUpdateWorld`, after the whole of `MainLoop` and before `LateUpdate` and `DebugGizmos` (`src/Game/Game.SceneFlow/GameManager.cs:2384-2409`). Everything driven from `MainLoop` — the modification phases, the tools, the UI, rendering — has therefore already run, so nothing is going to ask for the resource again this frame. That is the property the phase is chosen for, and it is the same reason a mod's own teardown work generally belongs there.

The barrier the system writes through is `EndFrameBarrier`, registered `UpdateBefore` within `MainLoop` alongside an `AllowBarrier<EndFrameBarrier>` (`src/Game/Game.Common/SystemOrder.cs:49`, `:62`). Unconfirmed: exactly where a buffer created from `Cleanup` plays back relative to that registration. The mod ships creating one there and `SafeCommandBufferSystem` throws outright when the window is shut (`src/Game/Game/SafeCommandBufferSystem.cs:16-23`), so the window is demonstrably open — but resolving the playback point is `mod-lifecycle-and-ordering`'s phase tree, not a read this topic made.

`Cleanup` is also the phase where the interval override the mod wrote does nothing: `GetUpdateInterval` returns 256 (`:76-79`) and only the three simulation phases consult it, so this system runs every frame. What keeps that cheap is the gate rather than the interval — `RequireAnyForUpdate(m_componentsToDispose, m_templatesToDispose)` (`:73`) means `OnUpdate` is not entered at all while nothing is pending.

**What breaks if a step is skipped.** Three failure modes, and they are not equally visible.

- **The component is an ordinary `IComponentData` rather than a cleanup one.** `DestroyBatch` takes the `!CleanupNeeded` branch and deallocates the chunk data immediately (`EntityComponentStore.cs:4386-4392`). The `GCHandle` is gone with it, never freed. With a strong handle that is a permanent managed root plus an undestroyed engine resource; with a weak one it is a leaked handle-table slot. Nothing reports either, for the reason the leak-detection finding gives — and this class of leak is _not_ native-container leakage at all, so even re-enabling `NativeLeakDetection` would not see it.
- **Nothing ever removes the cleanup component.** The entity stays in the residue archetype forever: still live, still occupying a chunk slot, still matching any query written over the cleanup component alone. `DestroyEntity` on it is a no-op in the sense that matters — it is already destroyed and already residue. The symptom is entity count and chunk count that climb and never fall, which reads as an entity leak rather than a resource one.
- **The disposal is attempted from inside the job.** The free needs `GameObject.Destroy` and `GCHandle.Free`, and no corpus or vanilla site calls either from a job body. Unconfirmed: which way it fails — a Burst-compiled job rejects the managed call at compile time, while an unbursted one would reach a Unity API from a worker thread, and this pass did not establish which error a reader actually meets. The enqueue-then-drain shape exists so the question never arises.

**Cautions, because there is one practitioner and no vanilla precedent.** The game declares zero cleanup components of any kind, so nothing in `src/Game/` can be read to check this shape against. Two consequences. The engine half above is first-party and solid — it is read from the entity store, not from the mod — but the _ergonomics_ half is one author's design, and the mod's own choice to gate on a self-maintained validity tag rather than on the absence of its ordinary component is a departure from the textbook form that this pipeline has no second example to weigh. And the shape is visibly still moving in that repository: a shipped test asserts `WEOwner` implements `ICleanupComponentData` (`CS2-WriteEverywhere/BelzontWE.Tests/Components/GamePropComponentsTests.cs:30-31`) while the declaration is a plain `IComponentData` (`CS2-WriteEverywhere/BelzontWE/Components/WEOwner.cs:5`).

What a save and a load do to a residue entity is settled below, under "Settled against the running game", the serializable-cleanup-component case included.

Rots: `ICleanupComponentData` is the renamed form of `ISystemStateComponentData`, and the obsolete spelling is still present as an upgrade alias (`src/Unity.Entities/Unity.Entities/ComponentType.cs:30-33`). Re-check the interface name and the `CleanupNeeded` / `CleanupComplete` flag logic in `EntityComponentStore.cs`.

### Mod-owned spatial indices: what the corpus builds, and the three places it diverges from vanilla

`NativeQuadTree<TItem, TBounds>` is `Colossal.Collections`' own, `[NativeContainer]`, `IDisposable`, with `TItem : unmanaged, IEquatable<TItem>` and `TBounds : unmanaged, IEquatable<TBounds>, IBounds2<TBounds>` (`src/Colossal.Collections/Colossal.Collections/NativeQuadTree.cs:9-33`). The instantiation everything uses is `NativeQuadTree<Entity, QuadTreeBoundsXZ>`. Its surface is `Add`/`TryAdd`, `Update`/`TryUpdate`, `AddOrUpdate`, `Remove`/`TryRemove`, `Get`/`TryGet`, `Clear`, and four `Iterate` overloads plus `Select`, driven by the iterator and selector interfaces beside it (`INativeQuadTreeIterator`, `INativeQuadTreeIteratorWithSubData`, `INativeQuadTreeSelector` and their `IUnsafe*` counterparts, same directory). The non-`Try` forms throw a bare `System.Exception` on miss (`:57-63`, `:70-76`, `:88-94`, `:101-108`), which is a real difference from the rest of the collections here: those four throws are unconditional and will fire in a player's game.

The constructor takes a minimum item size and an allocator. The game passes `1f` for objects, nets and lanes (`src/Game/Game.Objects/SearchSystem.cs:359-360`, `src/Game/Game.Net/SearchSystem.cs:742-743`); `Traffic` passes `4` for its connector and lane-handle trees (`Traffic/Code/Systems/LaneConnections/SearchSystem.cs:51-52`) and `CS2-Platter` passes `1f` (`CS2-Platter/Platter/Systems/Parcels/P_ParcelSearchSystem.cs:82`).

Corpus usage: 39 references to `NativeQuadTree` across the checkout. Three repositories build their own trees — `Traffic` (two), `CS2-Platter` (one), and `CS2-MoveIt`, which implements a custom `INativeQuadTreeIterator` for marquee selection rather than owning a tree (`CS2-MoveIt/Code/MoveIt/Searcher/SearcherJob.cs`, `SearcherIterator.cs`). Five repositories read the vanilla trees.

**Traffic's is a near-verbatim copy of the vanilla protocol** — `GetSearchTree(bool readOnly, out JobHandle)`, `AddSearchTreeReader` combining, `AddSearchTreeWriter` assigning, allocation in `OnCreate` (`Traffic/Code/Systems/LaneConnections/SearchSystem.cs:88-118`, `:51-52`) — **with the completion dropped**. Its `OnDestroy` is `_searchTree.Dispose(); _laneSearchTree.Dispose();` with no `Complete()` on any of its four handles (`:120-124`), against vanilla's four completions before two disposes (`src/Game/Game.Objects/SearchSystem.cs:364-373`). Because `NativeQuadTree` has no `Dispose(JobHandle)` and no safety handle, that is a use-after-free if any job is still reading at process exit rather than a diagnosable error.

**Platter's keeps the completion and the load-time clear** — `OnDestroy` completes both handles before disposing (`CS2-Platter/Platter/Systems/Parcels/P_ParcelSearchSystem.cs:85-93`), and `IPreDeserialize.PreDeserialize` fetches the tree with `readOnly: false`, completes, clears and sets a first-load flag (`:52-59`), which is `Game.Objects.SearchSystem.PreDeserialize` reproduced. It also carries a vestigial pair of "moving" handles it never uses, an artefact of copying a vanilla system that has two trees.

**The reader side is where mods get it wrong, and the mistake is one identifier.** The correct form registers the handle of the job that was _scheduled_:

```csharp
var handle = snapJob.Schedule(combined);
m_NetSearchSystem.AddNetSearchTreeReader(handle);
m_ObjectSearchSystem.AddStaticSearchTreeReader(handle);
```

(`CS2-NetworkTools/NetworkTools.Mod/Systems/Tools/Base/BaseToolSystem.Snap.cs:153-160`, the four `Get*(true, out …)` calls at `:107-110`.) Two corpus sites instead pass back the handle they were _given_:

- `CS2-Platter/Platter/Systems/Roads/P_RoadConnectionSystem.cs:112` takes the tree with `out var parcelSearchJobHandle`, schedules against it at `:118-121`, and at `:124` calls `AddSearchTreeReader(parcelSearchJobHandle)` — the input handle, not `createEntitiesQueueJobHandle`.
- `CS2-Platter/Platter/Systems/Buildings/P_BuildingToParcelReferenceSystem.cs:65` and `:73` do the same with `updateJobHandle` scheduled at `:72` and `parcelSearchJobHandle` registered.

Registering the input handle is a no-op: the owner already waited for it. The owning system's next write then does not wait for the reading job, and the tree is mutated while a job walks it.

A third shape is an omission rather than a wrong argument. `CS2-NetworkTools`' snap job holds `zoneTree` and never registers with `m_ZoneSearchSystem` (`BaseToolSystem.Snap.cs:109` against `:157-160`), and `CS2-Platter/Platter/Patches/ToolSystemPatch.cs:164-166` registers with `zoneSearchSystem` twice and never with the object or parcel systems whose trees the same job holds (`:123-124`).

So the protocol a reference teaches has to be stated as four steps in order, because three separate corpus mods each drop a different one: take the tree with the right `readOnly` flag, combine the returned handle into the schedule, register the _scheduled_ handle with _every_ provider you took from, and complete before disposing anything the container cannot dispose asynchronously.

Rots: the vanilla method names — `GetStaticSearchTree`, `AddStaticSearchTreeReader` and their siblings are public API a mod calls by name. Re-check `src/Game/Game.*/SearchSystem.cs`.

### Chunk geometry, buffers, and what a component costs

Taken over from `ecs-in-this-game.md`'s bridge and re-verified here, because the numbers are the ones a mod sizes its data against.

**A chunk is 16 KB and holds at most 128 entities.** `Chunk.kChunkSize = 16384`, `kBufferSize = kChunkBufferSize = 16320` (the 64-byte header comes off the front, `kBufferOffset = 64`), `kMaximumEntitiesPerChunk = 128` (`src/Unity.Entities/Unity.Entities/Chunk.cs:28-39`). So the per-entity budget is 16320 divided by the sum of every component's size in the archetype, capped at 128 entities however small the components are — which means a tag-only archetype wastes most of a chunk, and an archetype whose components total more than 127 bytes per entity is chunk-bound rather than entity-bound.

**A dynamic buffer's in-chunk footprint is a 16-byte header plus its internal capacity.** `BufferHeader` is `byte* Pointer` at 0, `int Length` at 8, `int Capacity` at 12 (`src/Unity.Entities/Unity.Entities/BufferHeader.cs:11-29`). The default internal capacity is `128 / sizeof(element)` when no attribute is present (`src/Unity.Entities/Unity.Entities/TypeManager.cs:2292`), so a default buffer costs 16 + 128 bytes of every entity's chunk slot whether or not the buffer has anything in it.

**`[InternalBufferCapacity(0)]` moves the whole payload out of the chunk**, leaving the 16-byte header. Overflow allocates from `Allocator.Persistent` with a minimum capacity of 8 elements, and frees to the same (`BufferHeader.cs:19`, `:57`, `:67`, `:92`, `:99`, `:124`). So a zero-capacity buffer trades chunk density for one heap allocation per non-empty buffer, which is the right trade when most entities have none.

The game uses the attribute 169 times, 118 of them with `(0)`. The corpus uses it 19 times, 12 of them with `(0)` — so the practice carried over, at a fifth the rate.

**Adding or removing a component is an archetype move**, which copies the entity out of one chunk and into another, and on the main thread it is also a full sync point (see the structural-change finding). Nothing about a tag is free except its storage.

### Throttling: the update interval, and the three phases where it exists

The interval mechanism itself — `GameSystemBase.GetUpdateInterval(phase)` and `GetUpdateOffset(phase)`, the power-of-two assertion, the `(updateIndex & (interval - 1)) != offset` mask, and the fact that only `LoadSimulation`, `EditorSimulation` and `GameSimulation` consult it — is derived and owned by `mod-lifecycle-and-ordering.md`, under "The update interval is power-of-two, and it only bites in three phases". It is not re-derived here. What this topic owns is what the mechanism buys and how to choose an interval, plus the facts this pass found which that one did not record.

**What it buys.** The mask is evaluated in `UpdateSystem`'s dispatch loop before `Update()` is called (`src/Game/Game/UpdateSystem.cs:224`), so a skipped frame costs one bitwise and and one comparison. Everything a system would otherwise do — the query gate, `OnUpdate`, the type-handle refresh, the schedule — is skipped whole.

**The offset is auto-assigned and you should let it be.** `GetUpdateOffset`'s default return is `-1` (`src/Game/Game/GameSystemBase.cs:136-139`), and `UpdateSystem.Refresh` treats a negative offset as "spread me": it walks the systems of one phase grouped by interval and hands out offsets in a bit-reversal sequence — `num3 += num2 >> num5; num3 &= num2 - 1;` where `num5` is the position of the lowest set bit of the running count (`src/Game/Game/UpdateSystem.cs:326-351`, the negative-offset branches at `:397-407` and `:426-432`). The effect is that N systems sharing an interval land on maximally separated frames rather than piling onto the same one. A system that returns an explicit offset opts out of that spreading; the game does it 70 times and the whole corpus does it once (`Time2Work/NightShift/Systems/Time2WorkCitizenBehaviorSystem.cs:63-65`, copying the vanilla system it replaces).

**The vanilla instance of the dead-interval mistake.** 271 files under `src/Game/` declare a `GetUpdateInterval` override, 264 of them in `Game.Simulation`. Of the seven outside it, six are registered in `GameSimulation` anyway (`src/Game/Game.Common/SystemOrder.cs:308/408/410/438/455/596`). The seventh is `WeatherAudioSystem`, which returns 16 (`src/Game/Game.Audio/WeatherAudioSystem.cs:135-138`) and is registered at `SystemUpdatePhase.Modification2` (`SystemOrder.cs:115`), where nothing reads it. The game makes the same mistake a reader is about to.

**Two corpus dead intervals the sibling file does not list**, found by re-running the sweep over 22 repositories: `InfoLoom/InfoLoom/Systems/CommercialSystems/CommercialCompanyData/CommercialCompanyDataSystem.cs` returning 1024 on a system registered at `UIUpdate` (`InfoLoom/InfoLoom/Mod.cs:59`), and `Time2Work/NightShift/Systems/DemandParameterUpdaterSystem.cs` returning `262144 / 8` on a system registered at `PrefabUpdate` and `PrefabReferences` (`Time2Work/NightShift/Mod.cs:158-159`). With the four the sibling already records, six corpus systems carry an interval that does nothing.

**The positive exemplar is one system.** `InfoLoom/InfoLoom/Systems/DemographicsData/Demographics.cs` returns 512 (`:323-326`), is registered at `GameSimulation` where that is honoured (`InfoLoom/InfoLoom/Mod.cs:49`), and opens `OnUpdate` with `if (!IsPanelVisible) return;` (`:328-333`) — so the query runs every 512 simulation ticks and only while the panel the data feeds is on screen. Interval plus a visibility gate, in the phase where the interval works.

Corpus census: 47 `override int GetUpdateInterval` across six of 22 repositories — `Time2Work` 29, `InfoLoom` 10, `Water_Features` 4, `Tree_Controller` 2, `CS2-NetworkTools` 1, `CS2-WriteEverywhere` 1 — and one `GetUpdateOffset`.

### Throttling: the query gates, and the cost ladder underneath them

Three mechanisms, in increasing order of what they let you skip.

**`RequireForUpdate` / `RequireAnyForUpdate` skip `OnUpdate` entirely.** `ShouldRunSystem` returns false as soon as any required query is empty (`src/Unity.Entities/Unity.Entities/SystemState.cs:420-447`), and `SystemBase.OnUpdate` is only reached when `base.Enabled && ShouldRunSystem()` (`src/Unity.Entities/Unity.Entities/SystemBase.cs:42`). The check is `IsEmptyIgnoreFilter` per required query, which is `GetMatchingChunkCache().Length == 0` — a length read on a cached list, no chunk walk, no dependency sync (`src/Unity.Entities/Unity.Entities/EntityQueryImpl.cs:75`).

**It ignores your filter, and that is stated in the code.** `ShouldRunSystem` calls `IsEmptyIgnoreFilter`, not `IsEmpty`. A system whose required query carries a `SetSharedComponentFilter` runs whenever the _unfiltered_ query has chunks. The consequence for query correctness is `ecs-in-this-game.md`'s ("The query gates, and the one that ignores your filter"); the consequence here is that a filter is not a throttle.

Census: `RequireForUpdate(` 572 and `RequireAnyForUpdate(` 28 under `src/Game/`, plus 62 `RequireForUpdate<`. Corpus: 167 and 29, with zero uses of the generic form.

**`IsEmptyIgnoreFilter` as an in-`OnUpdate` early exit** is the same O(1) check spelled by hand, and is what the game reaches for when one system schedules several independent jobs and each needs its own gate — `Game.Objects.SearchSystem.OnUpdate` (`src/Game/Game.Objects/SearchSystem.cs:388`), `Traffic/Code/Systems/LaneConnections/SearchSystem.cs:57` and `:70`. 581 uses under `src/Game/`, 117 in the corpus.

**The chunk-level early exit inside the job body** is the finest-grained form, and it is what the simulation actually runs on. `AgingSystem`'s `Execute` opens with

```csharp
if (!m_DebugAgeAllCitizens && chunk.GetSharedComponent(m_UpdateFrameType).m_Index != m_UpdateFrameIndex)
{
    return;
}
```

(`src/Game/Game.Simulation/AgingSystem.cs:66-71`.) The alternative is to push the same test into the query as a shared-component filter before scheduling — `m_BuildingGroup.SetSharedComponentFilter(new UpdateFrame(updateFrame))` after computing the frame from `SimulationUtils.GetUpdateFrame(m_SimulationSystem.frameIndex, kUpdatesPerDay, 16)` (`src/Game/Game.Simulation/BuildingUpkeepSystem.cs:1149-1150`; the helpers at `src/Game/Game.Simulation/SimulationUtils.cs:153-166`). The game prefers the in-job test 89 to 33. The corpus barely uses either: two filter sites (`Tree_Controller/Tree_Controller/Systems/DeciduousSystem.cs:113`, `Tree_Controller/Tree_Controller/Systems/FindTreesAndBushesSystem.cs:104`) and one in-job test (`Time2Work/NightShift/Systems/Time2WorkDeathCheckSystem.cs:309`), across 18 files that mention `UpdateFrame` at all.

**Ruled (2026-08-02, ticket 10; conflicts.md).** The per-entity job interface is the default the technique family teaches for new mod code, and both interfaces ship. The chunk-level early exit above is one of the three things the per-entity form has no equivalent for — the others are the chunk-scoped accessors and `unfilteredChunkIndex` as a parallel command buffer's sort key — and the reference names all three so a reader converting a fork knows what has no per-entity replacement. Which means: a mod that wants the sixteen-bucket throttle and writes a per-entity job cannot express it in the job body and has to use the query filter form instead. The `UpdateFrame` shared component itself, its sixteen buckets and their per-prefab assignment belong to `simulation-time-and-units`, not here.

### Burst: what it costs, what the toolchain does with it, and four ways it is gated

**The mod post-processor runs on every build, unconditionally, and Burst-compiles for three platforms.** `Mod.targets` composes the command as

```
"$(ModPostProcessorFullPath)" PostProcess "$(TargetPath)" -u "$(UnityModProjectFullPath)" @(ReferencePath->'-r "%(Identity)"', ' ') -p Windows -p macOS -p Linux -d -v
```

and executes it from a target with `AfterTargets="AfterBuild"` and no configuration condition (`cs2-moddingtools/Mod.targets:94-104`, the condition on all of them being `'$(NeedBuild)'`, set true unless publishing an update at `:43-53`). The executable's own help resolves the two short flags: `-d, --debug` and `-v, --verbose` (`Cities2_Data/Content/Game/.ModdingToolchain/ModPostProcessor/ModPostProcessor.exe PostProcess --help`, run against the 1.6.0f1 install). There is no switch in the toolchain that turns the pass off, and `-d` is passed in Debug and Release alike.

**So Burst output ships as three native libraries beside the managed assembly, and it is produced whether or not the mod has any Burst jobs.** From the Paradox mods cache — used here for prevalence only, over 431 cached mod directories: 180 ship a managed `.dll`, and 167 of those also ship the trio `<Name>_win_x86_64.dll`, `<Name>_linux_x86_64.so`, `<Name>_mac_x86_64.bundle`. The sizes tell you how much was compiled: `AdvancedRoadTools` ships 2,048 / 2,136 / 8,328 bytes beside a 41 KB assembly, and `Anarchy` ships 91,648 / 318,168 / 136,792 beside a 403 KB one. A `.pdb` for the Windows Burst library appears in some (`AdvancedRoadTools_win_x86_64.pdb`, 61 KB), which is what `--debug` buys.

**Burst is why a job cannot be stepped, and the corpus's answer is a preprocessor gate.** Ten of 22 repositories wrap `[BurstCompile]` in `#if`, and the symbol differs by author. Counted as `#if <SYMBOL>` and `#if !<SYMBOL>` directives rather than as attributes, since one directive can guard more than one thing: `WITH_BURST` (`Traffic` 68), `USE_BURST` (`CS2-Platter` 49, `CS2-NetworkTools` 22, `CS2-MoveIt` 12), `BURST` (`CS2-WriteEverywhere` 24, `BetterBulldozer` 20, `Water_Features` 19, `Tree_Controller` 15, `Recolor` 11, `Anarchy` 7). The corresponding `[BurstCompile]` counts run from `Traffic` 57 and `CS2-Platter` 46 down to `CS2-WriteEverywhere` 1.

**Two of those ten actually turn Burst off in Debug.** `Traffic/Code/Traffic.csproj:23-26` defines `WITH_BURST` only in the Release property group, and its Debug group defines a different set entirely (`:17-21`). `CS2-Platter/Platter/Platter.csproj:27-35` does the same with `USE_BURST`. `CS2-MoveIt/Code/MoveIt/MoveIt.csproj:77-93` defines `USE_BURST` in all three of its configurations, so its gate is vestigial.

**In seven repositories the gating symbol is defined nowhere in the checkout, so the attributes never reach the compiler.** `Anarchy`, `BetterBulldozer`, `Recolor`, `Tree_Controller` and `Water_Features` contain no `DefineConstants`, no `Directory.Build.props`, no `.props` and no `.targets` file at all — a grep for `BURST` across every build file in each returns nothing, while `#if BURST` appears 72 times between them (the shape is `#if BURST` / `[BurstCompile]` / `#endif` immediately above a job struct, e.g. `Anarchy/Anarchy/Systems/AnarchyComponentsTool/AnarchyComponentsToolSystem.cs:643-646`). `CS2-NetworkTools` uses `#if USE_BURST` 22 times and defines it in no csproj. `CS2-WriteEverywhere` sets a custom `<Bursted>` property for both configurations (`CS2-WriteEverywhere/BelzontWE/BelzontWE.csproj:7-8`) whose only possible consumer is `$(SolutionDir)\_Build\belzont_public.targets`, imported at `:16` and not present in the checkout.

Unconfirmed: whether those seven ship unbursted. As checked out the attributes are unreachable, and that is provable. Whether the published artefact matches the checkout is not — `Anarchy`'s cached Burst libraries are 91 KB and 318 KB, which is not what an empty compile looks like, so either the published build defined the symbol some other way or those sizes come from generic instantiations of vanilla jobs. Settling it means decompiling the cached `Anarchy.dll` and looking for `[BurstCompile]`, which this pass did not do.

**The first-party off switch nobody in the corpus uses.** Burst compilation can be disabled at runtime, with no rebuild and no cooperation from the mod. `BurstCompilerOptions`' static constructor reads `Environment.GetCommandLineArgs()` and sets `ForceDisableBurstCompilation` on `--burst-disable-compilation`, sets `ForceBurstCompilationSynchronously` on `--burst-force-sync-compilation`, and then also honours the environment variable `UNITY_BURST_DISABLE_COMPILATION` when it is set to anything other than empty or `"0"` (`src/Unity.Burst/Unity.Burst/BurstCompilerOptions.cs:681-707`, the two constants at `:12` and `:14`). `IsEnabled` is `EnableBurstCompilation && !ForceDisableBurstCompilation` (`:252-262`), and setting `EnableBurstCompilation` on the global options also assigns `JobsUtility.JobCompilerEnabled` (`:264-287`).

**The managed body survives in the assembly, which is why disabling it works.** The direct-call shim the post-processor generates for a `[BurstCompile]` static method reads

```csharp
if (BurstCompiler.IsEnabled)
{
    IntPtr functionPointer = GetFunctionPointer();
    if (functionPointer != (IntPtr)0) { return ((delegate* unmanaged[Cdecl]<float, float, float>)functionPointer)(x, y); }
}
return SimplexNoise_0024BurstManaged(x, y);
```

(`src/Game/Game.Prefabs.Climate/ClimatePrefab.cs:542-553`; the same shape at `src/Game/Game.Rendering/WaterRenderSystem.cs:120` and `src/Game/Game.Simulation/HeightDataReader.cs`, five sites under `src/Game/` in total.) The `$BurstManaged` fallback is ordinary IL and is steppable.

Corpus uses of the launch flag or the environment variable: zero. Nobody documents it either.

**Ruled (2026-08-04, ticket 18; conflicts.md).** The reference teaches both gates, and leads with the runtime one. The order is the ruling: `--burst-disable-compilation` or `UNITY_BURST_DISABLE_COMPILATION` is what a reader reaches for to get a job into a debugger, because it is first-party, needs no build-system change and no rebuild, and has no silent failure mode; the `#if` gate is what to set up if you are going to do this often enough that a launch argument becomes tiresome.

The evidence that decides it is the corpus's failure rate rather than a preference. Of the ten repositories that gate `[BurstCompile]` behind a preprocessor symbol, two define it in Release only, one defines it everywhere so the gate never fires, and seven define it nowhere in the checkout at all. So the reference states the fact underneath that: a preprocessor symbol that is defined nowhere produces no warning, no error and a build indistinguishable from a working one. That is a fact about C# rather than about this game, and it is what makes the compile-time form the more dangerous thing to hand an agent — an agent writing a `#if` into a csproj it cannot run is exactly the case the failure rate describes.

The honest cost, stated because the reference should carry it: the runtime flag is first-party and unrun against this game. Nothing establishes that it restores steppable execution in this AOT player build, and the reference marks that rather than implying it is proven. This is the second place the plugin puts an untested first-party path ahead of a proven corpus one; `PostTool` in `placement-definitions` was the first, ruled the same way.

Rots: the two argument spellings and the environment variable name. Re-check `src/Unity.Burst/Unity.Burst/BurstCompilerOptions.cs`.

### Main-thread scans that look harmless, and the cost ladder they sit on

Four rungs, each with what the code actually does.

**Rung 1 — `IsEmptyIgnoreFilter`, effectively free.** `GetMatchingChunkCache().Length == 0` (`src/Unity.Entities/Unity.Entities/EntityQueryImpl.cs:75`). No sync, no walk.

**Rung 2 — `IsEmpty`, free when there is no filter.** It short-circuits to `IsEmptyIgnoreFilter` unless the query has a filter or an enableable component; otherwise it calls `SyncFilterTypes()` and walks chunks (`:57-73`). So `IsEmpty` on a plain query is rung 1 and on a filtered one is rung 3.

**Rung 3 — `CalculateEntityCount`, a filter sync plus a walk of every matching chunk.** `SyncFilterTypes()` then `ChunkIterationUtility.CalculateEntityCount(...)` (`:180-184`). `CalculateChunkCount` is the same shape (`:192-197`). 48 `CalculateEntityCount` and 4 `CalculateChunkCount` under `src/Game/`; 52 and 13 in the corpus.

**Rung 4 — `ToEntityArray` / `ToComponentDataArray`, a count, a main-thread block, and a full copy.** The body is three statements:

```csharp
int entityCount = CalculateEntityCount();
_Access->DependencyManager->CompleteWriteDependency(TypeManager.GetTypeIndex<Entity>());
return ChunkIterationUtility.CreateEntityArray(_QueryData->MatchingArchetypes, allocator, default(EntityTypeHandle), outer, entityCount);
```

(`:406-412`; `ToComponentDataArray<T>` identically at `:464-472` with the component's own type index.) `CompleteWriteDependency` resolves to `m_DependencyHandles[num].WriteFence.Complete()` (`src/Unity.Entities/Unity.Entities/ComponentDependencyManager.cs:269-276`, `:303-306`) — the main thread stops until every job writing that type has finished. Then every matching entity is copied into a fresh allocation.

**The async form exists and the game always uses it.** `ToEntityArrayAsync` and `ToComponentDataArrayAsync` take an `out JobHandle` and combine the dependency instead of completing it (`:374-378`, `:430-434`), and their `List` counterparts do the same (`:398-403`, `:455-461`). Under `src/Game/`: `ToEntityListAsync` 53, `ToComponentDataListAsync` 19, `ToEntityArrayAsync` 0, `ToComponentDataArrayAsync` 0 — the game uses the list forms exclusively and never the array ones. The corpus mirrors it: 20 and 2 against 0 and 0.

Against that, `ToEntityArray` is used 237 times under `src/Game/` and 208 times in the corpus, and `ToComponentDataArray` 110 and 21. So the synchronous form is not forbidden — the game's own load paths and editor paths use it heavily — but every use is a stall, and a system that calls it every frame stalls every frame.

**The failure mode is a stall, not a leak, and the diagnosis is different.** A leaked container costs memory and shows nothing; a per-frame `ToEntityArray` costs frame time and shows as the mod's system name in a profile.

### Structural changes on the main thread complete every job in the world

The heaviest sync point available, and it is one line of mod code.

Every `EntityManager` method marked `[StructuralChangeMethod]` — 73 of them in `src/Unity.Entities/Unity.Entities/EntityManager.cs` — routes through `EntityDataAccess.BeforeStructuralChange()`, whose body is `m_DependencyManager.CompleteAllJobsAndInvalidateArrays()` unless a transaction is open (`src/Unity.Entities/Unity.Entities/EntityDataAccess.cs:391-397`, called from `BeginStructuralChanges` at `:399-406`), and that resolves to `CompleteAllJobs()` (`src/Unity.Entities/Unity.Entities/ComponentDependencyManager.cs:144-147`). Not the jobs touching your components — all of them.

`EntityManager.SetComponentData<T>` and the `GetComponentDataRW` path are cheaper but not free: each completes the read-and-write dependency for that one type (`EntityDataAccess.cs:729-733`, `:747-756`).

**A barrier's command buffer is the alternative and it costs the mod nothing to own.** `EntityCommandBufferSystem` allocates each buffer from a per-barrier `RewindableAllocator` seeded at 16 KB out of `Allocator.Persistent` (`src/Unity.Entities/Unity.Entities/EntityCommandBufferSystem.cs:18-20`, `:32-34`), plays every pending buffer back in its own `OnUpdate` and disposes it, then rewinds the allocator (`:58-79`). So `barrier.CreateCommandBuffer()` produces a buffer the barrier owns, disposes and recycles; a mod never disposes one and never should. The exception is a hand-rolled `new EntityCommandBuffer(Allocator.TempJob)`, which the corpus does 34 times, mostly in `CS2-MoveIt`'s undo queue — those the caller owns.

The game's `SafeCommandBufferSystem` adds a window guard on top: `CreateCommandBuffer` throws `"Trying to create EntityCommandBuffer when it's not allowed!"` once the barrier has played back for the frame (`src/Game/Game/SafeCommandBufferSystem.cs:16-30`). The barrier contract itself is `ecs-in-this-game`'s.

**Census.** `EntityManager.AddComponent` 205 and `RemoveComponent` 67 under `src/Game/`, against 195 and 117 across the corpus. The game's are concentrated in load, migration, editor and rendering paths — the largest per-namespace groups are `Game.UI.Editor`, `Game.Serialization.DataMigration` and `Game.Rendering` at four files each, with only three files in `Game.Simulation` — so the game does almost none of it per frame. The corpus does roughly as many in a fiftieth of the code, and the raw ratio is not a defect count because a structural change in a load hook is correct; what it does say is that a reference cannot assume the reader knows the cost.

### The game's own three forced garbage collections, and where they are

`GameManager.CleanupMemory()` is `Resources.UnloadUnusedAssets()`, then `ClearCachedUnusedImages()` on every UI system, then `GC.Collect()` (`src/Game/Game.SceneFlow/GameManager.cs:915-923`). It is the only `GC.Collect` in `src/Game/`, and it has three callers: immediately before serializing a save (`:927`), immediately after `onGamePreload` fires and before deserializing (`:1024`), and after loading finishes and immediately before `onGameLoadingComplete` fires (`:1125`).

Two things a mod can act on. The game never forces a collection per frame, so managed allocation in a hot path is the mod's own problem and nothing will hide it. And a mod's `OnGamePreload` / `OnGameLoadingComplete` hooks run adjacent to a full blocking GC, so work done there is already inside the frame the player experiences as a load-time hitch — which makes those hooks the cheap place to do expensive managed setup, and the wrong place to add more.

### Colossal's own collections, and what each is for

`Colossal.Collections` is a small library and its contents are worth naming because they are what vanilla jobs are written against, so a mod forking one meets them immediately. Usage counts under `src/Game/` and then across the corpus:

- `NativeValue<T>` — 137 / 33. A one-element `NativeArray<T>`, so a job can write a scalar (`src/Colossal.Collections/Colossal.Collections/NativeValue.cs`, whole file, 46 lines). No `Dispose(JobHandle)`.
- `NativeAccumulator<T> where T : IAccumulable<T>` — 69 / 8. A per-thread striped accumulator: its `ParallelWriter` is `[NativeContainerIsAtomicWriteOnly]` and indexes by `[NativeSetThreadIndex]`, so parallel accumulation needs no atomics and the reduction happens on read (`src/Colossal.Collections/Colossal.Collections/NativeAccumulator.cs:13-47`). Has `Dispose(JobHandle)` (`:91`).
- `NativeParallelQueue<T>` — 59 / 0. A block-pooled parallel queue with its own `NativeParallelQueueBlockPool`.
- `NativeQuadTree<TItem, TBounds>` — the spatial index, above. 39 corpus references.
- `NativeHeapAllocator` / `UnsafeHeapAllocator` — 54 / 0 and 2 / 0. A sub-allocator handing out `NativeHeapBlock` ranges inside one buffer; the rendering systems' answer to churning GPU-visible buffers.
- `UnsafeLinearAllocator` — 10 / 0. A bump allocator; `PathfindQueueSystem` gives each of its worker threads one, created from `Allocator.Persistent` (`src/Game/Game.Pathfind/PathfindQueueSystem.cs:672`).
- `NativeMinHeap` / `UnsafeMinHeap` — 12 / 0. Priority queues; the flow solver allocates two per call from `Allocator.Temp` inside the job (`src/Game/Game.Simulation/WaterPipeFlowJob.cs:526-527`).
- `StackList<T>` — 46 / 11. A stack-allocated list for small fixed-bound collections inside a job body.
- `NativeCurve`, `AnimationCurve1`–`4`, `CurveSampling` — the burst-compatible curve evaluation the prefab data uses.

The pattern worth naming: most of these exist so a job body can accumulate, queue or sort without touching a shared container, and all but `NativeAccumulator` and `NativeParallelQueue` lack the asynchronous dispose, so a mod holding one has to complete before tearing down.

### The heaviest subsystem to query, and it has its own thread budget

For the `roads-and-traffic` bridge, and it is a performance fact rather than a mechanics one.

`PathfindQueueSystem` is not a job-system consumer like everything else here. It sets `m_MaxThreadCount = math.max(1, JobsUtility.JobWorkerCount / 2)` in `OnCreate` (`src/Game/Game.Pathfind/PathfindQueueSystem.cs:326`) and stands up that many `ThreadData` entries, each with its own `AllocatorHelper<UnsafeLinearAllocator>` created from `Allocator.Persistent` (`:346-347`, `:672`), plus a pool of `WorkerData` and `WorkerActions`, also `Persistent` (`:341-344`, `:698`). It uses `System.Threading` directly (`:4`) and exposes the handle protocol through `AddDataReader` (`:434`), and it implements `IPreDeserialize` so the queues clear on load (`:419-427`).

So half the machine's job worker threads are reserved for pathfinding, permanently, and a mod's parallel job competes with that. `Game.Net.SearchSystem` on top of it holds two `Allocator.Persistent` quadtrees over every net and every lane in the city (`src/Game/Game.Net/SearchSystem.cs:742-743`) — the two largest spatial indices in the game.

### Catalog gaps found in this sweep

Six entries in `plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md` were proposed a sentence in their **Demonstrates** paragraph, because nothing in the catalog pointed a reader at any of this material.

**Outcome: four landed, reworded.** Two were already covered by existing entries and three of the six carried superlatives this sweep did not establish, so what shipped is narrower than what is drafted below. The drafts are kept for their source lines, which are the part that can never ship — read them as evidence, not as prose to apply.

**Write Everywhere** (`### Write Everywhere`, currently "Rendering that leaves the entity renderer entirely…"). Add: _It is also the only mod here that ties a managed resource to an entity — a struct component holding a `GCHandle`, declared `ICleanupComponentData` so it survives the entity's destruction, with a dedicated disposal system registered in the cleanup phase that drains the doomed components through a queue and frees each handle on the main thread._ Source lines: `CS2-WriteEverywhere/BelzontWE/Components/WETextData/WETextDataMesh.cs:13` (the three interfaces), `:17` (the `GCHandle` field), `:105`/`:117` (`GCHandle.Alloc(..., GCHandleType.Weak)`), `:127-133` (`Dispose` freeing it); `CS2-WriteEverywhere/BelzontWE/Templates/WETemplateDisposalSystem.cs:85-121` (the job enqueues, the main thread dequeues and disposes), registered at `CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:81`.

**Traffic** (`### Traffic`, currently the save-migration entry). Add: _It also builds two mod-owned quadtrees behind the game's own reader/writer handle protocol, and gates every one of its Burst attributes behind a symbol its Release configuration alone defines._ Source lines: `Traffic/Code/Systems/LaneConnections/SearchSystem.cs:51-52` and `:88-118`; `Traffic/Code/Traffic.csproj:23-26` against `:17-21`.

**Platter** (`### Platter`, currently the serializable-component entry). Add: _Its parcel search system is the corpus's most faithful copy of a vanilla spatial index, completing its outstanding handles before disposal and clearing the tree from the pre-deserialize hook rather than rebuilding it._ Source lines: `CS2-Platter/Platter/Systems/Parcels/P_ParcelSearchSystem.cs:52-59` and `:85-93`.

**Info Loom** (`### Info Loom`, currently the two module-registry calls). Add: _Its demographics system is the corpus's clearest throttle — an update interval in a phase that honours one, plus an early return while the panel it feeds is off screen._ Source lines: `InfoLoom/InfoLoom/Systems/DemographicsData/Demographics.cs:323-333`, registered at `InfoLoom/InfoLoom/Mod.cs:49`.

**Network Tools** (`### Network Tools`, currently the shared tool base class). Add: _Its snapping path is the corpus's correct example of consuming four vanilla data providers at once — take each with its own out-handle, combine them all into one schedule, and register the scheduled handle back with every provider._ Source lines: `CS2-NetworkTools/NetworkTools.Mod/Systems/Tools/Base/BaseToolSystem.Snap.cs:107-110` and `:153-160`.

**Move It** (`### Move It`, currently the fullest tool lifecycle). Add: _It is also the corpus's only user of a hand-rolled command buffer it owns and plays back itself, and its custom quadtree iterator is the one example of walking a vanilla search tree with mod-defined selection._ Source lines: `CS2-MoveIt/Code/MoveIt/Managers/QueueManager.cs:28-32`; `CS2-MoveIt/Code/MoveIt/Searcher/SearcherJob.cs`, `SearcherIterator.cs`, `Searcher.cs:142-143`.

### Source-list gaps found in this sweep

Two entries in `docs/SOURCES.md` needed amending and one addition was worth considering. **Both amendments have since shipped into entry 7**, so the drafts below stand as the evidence behind them rather than as work outstanding.

**Entry 7, the official modding toolchain, understates what it settles and does not name the file that settles it.** It says the toolchain is "authoritative for how a mod is built, post-processed and published". It is also the only source for what a mod project _compiles with_ — and the answer, which nothing else in the pipeline can give, is that `Mod.props` sets no `DefineConstants` at all, so no conditional-compilation symbol a mod might rely on is present unless the mod's own csproj adds it. Add a clause to the effect that `Mod.props` and `Mod.targets` are the record of the mod compile's own configuration, and that the absence of a property there is as load-bearing as its presence. The two files are reached at `%CSII_TOOLPATH%`, which entry 7 already names.

**Entry 7 should also name the post-processor's own help output as a readable surface.** `ModPostProcessor.exe PostProcess --help` at `Cities2_Data/Content/Game/.ModdingToolchain/ModPostProcessor/` resolves every short flag in the command `Mod.targets` composes — this pass used it to establish that `-d` is `--debug` — and nothing else in the pipeline documents them. The entry currently names the executable's location and not that it is self-documenting.

**Entry "What looks like a source and is not" correctly bars the Paradox cache for behaviour and permits it for prevalence, and that permission carried a claim in this file.** No amendment needed; recorded so the next pass knows the rule was exercised and held. The measurement was file-name and file-size only, over 431 directories, with no mod decompiled.

### Settled against the running game, 2026-08-04 and 2026-08-05

A later pass drove the running game over the Mono soft debugger, after the reference had shipped its evidence markers. The baseline above covers the decompile; this section's source is the live process, game version 1.6.0f1, and its citations are the expressions evaluated rather than file lines, since that is what a later reader would re-run.

**Burst does block stepping here, and turning it off restores it.** A breakpoint on `Game.Rendering.PreCullingSystem+InitializeCullingJob.Execute` binds — the managed body is in the assembly, and the debugger reports only "no debug info for this method" — and never fires across 25 seconds, while a control breakpoint on the owning system's `OnUpdate` pauses the main thread on the first frame. With `BurstCompiler.Options.EnableBurstCompilation` set false, which also flips `JobsUtility.JobCompilerEnabled` false, a breakpoint on `PreCullingSystem+TreeCullingJob1.Execute` hits within seconds; the frame below it is `Unity.Jobs.IJobParallelForExtensions+ParallelForJobStruct<TreeCullingJob1>.Execute`, so the fallback really is the managed path, and locals and `this` read fully. The first attempt at this measured `InitializeCullingJob` with Burst off and saw nothing, which read as the toggle failing; it was the job not being scheduled every frame. Arming several of a system's jobs at once is what distinguishes the two.

**The launch argument is settled (2026-08-05).** The game was relaunched with `--burst-disable-compilation` on its command line and `UNITY_BURST_DISABLE_COMPILATION` unset, which makes the argument the only variable. `BurstCompiler.IsEnabled`, `BurstCompiler.Options.EnableBurstCompilation` and `JobsUtility.JobCompilerEnabled` all read false, and a method-entry breakpoint on `Game.Rendering.PreCullingSystem+TreeCullingJob1.Execute` — a vanilla Burst job — bound and hit, with `IJobParallelForExtensions+ParallelForJobStruct<TreeCullingJob1>.Execute` beneath it and `this` and the locals reading fully. So the argument reaches this build and produces the state that restores stepping, not merely the flag.

The variable was empty for this run, so nothing here exercises its own path; settling it directly would need one more relaunch with the variable set and the argument absent. The reference ships the variable unmarked anyway: the two are read by the same static constructor a few lines apart, the argument half is established, and the maintainer judged the remaining gap too narrow to spend a reader's attention on. That is a maintainer call recorded here, not a ruling — no `conflicts.md` entry stands behind it, and nothing downstream should read it as one.

**Every vanilla job checked is Burst-compiled, so there is no unbursted control job in this game.** `IsDefined(typeof(BurstCompileAttribute))` is true for all five `PreCullingSystem` jobs tested and for four `Game.UI.InGame` section jobs, which were the likeliest candidates to be exempt.

**The safety system is confirmed absent from the live process,** independently of the decompile's field list. `JobsUtility.JobDebuggerEnabled` is false and `NativeLeakDetection.Mode` is `Disabled` at boot. A `NativeArray<T>` captured live inside a scheduled job struct formats as exactly `m_Buffer`, `m_Length`, `m_AllocatorLabel` — no `m_Safety`, no `m_DisposeSentinel`.

**A residue entity survives a save and a load, and the save query is not the fixed vanilla list this first reading took it for.** `Game.Serialization.SerializerSystem.m_Query` — the query the save writes from, matching 434 355 entities in the test city — has an empty `All`, eighteen `Any` and five `None`. The `Any` list is `ElectricityFlowEdge, WaterPipeEdge, PrefabRef, CoordinatedMeeting, TimeData, City, WaterSourceData, FloodCounterData, ElectricityFlowNode, WaterPipeNode, ServiceRequest, SchoolSeeker, JobSeeker, ServiceBudgetData, LookingForPartner, MeshColorPalette, CityStatistic, LoadedIndex`; the `None` list is `Game.Tools.Temp, Game.Effects.EffectInstance, Game.Prefabs.NetCompositionData, Game.Routes.LivePath, Game.Common.Deleted`.

`Unity.Entities.CleanupEntity` is in neither, and the exclusion is by absence rather than by rule: a fresh entity carrying only `Game.Objects.Transform` does not match, adding `Game.Prefabs.PrefabRef` makes it match, and adding `CleanupEntity` on top leaves it matching. Since `DestroyEntity` strips every non-cleanup component, a residue entity carries none of the eighteen and is skipped by the save.

**Verdict: the live readout is the union's empty case, and the conclusion drawn from it was wrong.** `SerializerSystem.CreateQuery` takes a parameter and unions it into `Any` — it seeds the eighteen literals, then `foreach (ComponentType serializableComponent in serializableComponents) { hashSet.Add(...) }` before assigning `m_Query` (`src/Game/Game.Serialization/SerializerSystem.cs:171-209`); `OnCreate` calls it with `Array.Empty<ComponentType>()` (`:56`) and `OnUpdate` re-calls it with the serializer library's output (`:82-83`). That output is every `TypeManager` type implementing `ISerializable` or `IEmptySerializable` whose `type.Assembly != assembly`, where `assembly` is the Game assembly (`src/Colossal.Core/Colossal.Serialization.Entities/ComponentSerializerLibrary.cs:42/50-56/73-79`) — with no filter on component kind, and a cleanup component passes `IsComponentType` because the cleanup flag `0x2000000` sits outside that mask (`src/Unity.Entities/Unity.Entities/TypeIndex.cs:32-39/59-65`). Mod assemblies reach it by design: `ModManager.AfterLoadAssembly` calls `TypeManager.InitializeAdditionalTypes(assembly)` then `SerializerSystem.SetDirty()` (`src/Game/Game.Modding/ModManager.cs:146-150`). The probe could not have seen this — it tested `Transform`, `PrefabRef` and `CleanupEntity`, all Game or Unity types, which the assembly filter never admits.

**And the entity is not cleared either, so it survives regardless.** `ClearSystem`'s query is a hardcoded nineteen-type `Any` with no extension point, destroyed wholesale at `base.EntityManager.DestroyEntity(m_ClearQuery)` (`src/Game/Game.Serialization/ClearSystem.cs:22-51`, `:57`, registered `UpdateBefore<ClearSystem>(SystemUpdatePhase.Deserialize)` at `src/Game/Game.Common/SystemOrder.cs:795`). A residue archetype carries none of the nineteen. No other teardown exists: the world is created once at boot and disposed only on quit (`src/Game/Game.SceneFlow/GameManager.cs:2363-2374`, `:2412-2415`), and no `DestroyAllEntities` or universal-query destroy exists anywhere.

So both cases invert the earlier conclusion. A non-serializable cleanup component is neither saved nor cleared, so the residue persists into the next city with its handle live and the teardown delayed rather than lost. A serializable one is additionally written to the save, and the load deserializes a fresh copy carrying a handle value from the previous process beside the survivor.

**Confirmed live, 2026-08-05, with a mod that declares serializable components.** With nine code mods loaded but none declaring one, the save query stayed at eighteen `Any` — so the original probe's eighteen was the union's empty case rather than evidence against a union. Enabling `PlopTheGrowables` moved the save query to **twenty-one** `Any`, and the three added entries resolve through `TypeManager.GetType` to `PlopTheGrowables.LevelLocked`, `PlopTheGrowables.PloppedBuilding` and `PlopTheGrowables.SpawnedBuilding`, all reporting assembly `PlopTheGrowables`. The serializer count rose by the same three, 745 to 748. The clear query stayed at nineteen `Any` and two `None` throughout. That is the asymmetry end to end: the save query gains mod types, the clear query does not.

Rots: the two selection lists, and the assembly filter that extends the save's. Re-read `SerializerSystem.CreateQuery` and its callers, `ComponentSerializerLibrary.Initialize`, and `ClearSystem`.

**What `EnabledWithStackTrace` actually reports (2026-08-05).** Setting `NativeLeakDetection.Mode` to it and leaking four tracked allocations through `UnsafeUtility.MallocTracked` — three `TempJob`, one `Persistent` — produced, in `Player.log`, one `TempJob leak at address <addr>:` block per `TempJob` allocation, each followed by a ~20-frame callstack, plus `Internal: JobTempAlloc has allocations that are more than the maximum lifespan of 4 frames old - this is not allowed and likely a leak`. The `Persistent` allocation produced nothing over the minutes the session ran.

**Settled at shutdown (2026-08-05).** A second run leaked two 32 KB `Persistent` blocks with the mode set and then called `UnityEngine.Application.Quit()`. The exit log carries no per-allocation `Persistent` block and no callstack for either. What it does carry is one summary line, `##utp:{"type":"MemoryLeaks","version":2,"phase":"Immediate", … "allocatedMemory":17234842,"memoryLabels":[… {"NativeArray":4276620} …]}` — byte totals per memory label, inside which the 64 KB is invisible. Alongside it, `Internal: There are remaining Allocations on the JobTempAlloc. This is a leak, and will impact performance`, again naming `-diag-job-temp-memory-leak-validation`, which is the earlier session's undisposed `TempJob` blocks surfacing at exit as a count rather than as sites.

So the instrument is asymmetric, and that is the fact worth shipping: `EnabledWithStackTrace` names allocation sites for `TempJob` only. `Persistent` leaks reach the log as a label total at exit and never as a site, at either point in the session.

So the four-frame figure is settled: real, and enforced in this build whenever the mode is on. The wiki's claim holds and the earlier `Unconfirmed:` on it is closed.

One limit on the callstack. Its frames were native — `(UnityPlayer)`, `(mono-2.0-bdwgc)` and address-only entries — with the only managed entries being `runtime-invoke` wrappers. A leak from ordinary mod code reads entirely differently; see the mod-driven run below. Two things differ between the two runs, the debugger's invoke path and `UnsafeUtility.MallocTracked` against the `NativeArray` constructor, so which of them emptied the managed half here is not established.

**The log names a launch flag the pipeline had not found:** `To Debug, run app with -diag-job-temp-memory-leak-validation cmd line argument. This will output the callstacks of the leaked allocations.` That reaches the same evidence without any mod setting the process-global mode, which makes it the better first reach for an author chasing their own accumulation. Its own behaviour has not been exercised.

**The cost of `EnabledWithStackTrace` remains unmeasured, and the obvious measurement does not work.** Frame counts over two 20-second windows came to 494 both with the mode disabled and with it set — but `Application.targetFrameRate` is 15 while the game sits unfocused, so the cap absorbs any regression and the null result proves nothing. Measuring this needs the game focused and under load, which a debugger-driven pass cannot arrange for itself.

### Settled against a purpose-built mod, 2026-08-05

Three observations the debugger could not reach, made with a throwaway mod built for the purpose and then stripped back to empty; the baseline above gives its path, and its own `CLAUDE.md` documents the loop. The experiment sources were not kept; the run's logs are at `cs2-test-mod/evidence/`. The game ran with `--developerMode --uiDeveloperMode` and no Burst flag, so Burst was on — confirmed from `SceneFlow.log`'s `Command line:` block.

**Burst rejects the engine-resource destroy at build time, and accepts the handle free.** Verified by building a `[BurstCompile] IJob` through the ordinary `dotnet build`, whose post-processor runs Burst AOT unconditionally, once per call under test. `GCHandle.Free()` alone builds, and the job really is Burst-compiled — the pass reports one method against zero for the same project with no job in it. `GCHandle.Target` and `UnityEngine.Object.Destroy` each fail the build with `BC1016`, "the managed function … is not supported". Burst reports one such error and aborts, which is why the calls needed separate builds; and since `Target` is the only way to reach the managed object from a job body, the realistic `Object.Destroy((Object)Handle.Target)` form is rejected at the getter, one call before `Destroy`.

**Unbursted, the same body runs on a worker thread and only the destroy throws.** Verified by scheduling the job from a `SystemBase` in `SystemUpdatePhase.MainLoop` and reading its own thread id, which came back a worker rather than the main thread. `Handle.Target` read fine and returned the object; `Object.Destroy` threw `UnityException: Destroy can only be called from the main thread.`; `GCHandle.Free()` returned normally, with nothing logged. So neither guard covers the free.

After the job's `Free()` returned, the main thread's own copy of the handle still read `IsAllocated == true`. The obvious reading is struct copy semantics, but the run does not separate that from a silently failed free, so treat the mechanism as unconfirmed.

**A leak from ordinary mod code puts the mod's own frames at the top of the callstack.** Verified by setting `NativeLeakDetection.Mode` to `EnabledWithStackTrace` from the mod and leaking a `NativeArray<int>(64, Allocator.TempJob)` three call levels deep in mod code. The `TempJob leak at address …` block carries ten managed Mono JIT frames — the mod's three nested methods and its `OnUpdate`, then `SystemBase.Update`, `Game.UpdateSystem.Update` and `GameManager.Update` — before the stack turns native at `runtime-invoke`, `(mono-2.0-bdwgc)` and `(UnityPlayer)`. That inverts what the reference shipped. The `Internal: JobTempAlloc has allocations that are more than the maximum lifespan of 4 frames old` line followed, as it did in the debugger-driven run.

Two limits the shipped prose is scoped around rather than stating. The `(at <file>:<line>)` suffix on each managed frame needs the mod's debug symbols deployed beside the assembly, which this Debug build's deploy stage did; whether a build without them still names the methods was not tested. And the descent through `SystemBase.Update` is a property of allocating inside `OnUpdate` rather than of the instrument, so a leak from a load hook or a patch would show a different path below the mod's own frames.

Rots: the `BC1016` code and the two error strings — the Burst package's diagnostic tables, reached through the post-processor the toolchain runs. The `UnityException` text is Unity's own, in the engine's threading guard.

### A log call is a synchronous file round trip, added 2026-08-05 from ticket 19's pass

Evidence belongs to `diagnostics.md`'s logging findings and is cross-noted here because the shipped `performance-and-memory` reference now carries the cost rule, at the maintainer's request after observing mods — agent-written ones especially — logging from update hooks.

`UnityLogger.Internal_WriteStream` takes `lock (_syncObject)`, calls `Open()` when the stream is closed, writes, `Flush()`es, and calls `Close()` again unless `keepStreamOpen` (`src/Colossal.Logging/Colossal.Logging/UnityLogger.cs:308-346`).
`keepStreamOpen` defaults false and is set by nothing in `src/` or in the 22-repository corpus (`diagnostics.md:612-613`), so that path is what every logger does today — but it is a public settable property (`src/Colossal.Logging/Colossal.Logging/ILog.cs:61`), so a mod turns it off in one line, which is the lever the shipped rule now names.
The `lock` is what extends the cost past the calling thread: a job that logs contends with the main thread and with every other logger in the process.

The level filter does not save the message string. `*Format` overloads check `isLevelEnabled` before formatting (`UnityLogger.cs:941-947` is representative), but an interpolated `$"..."` argument is built at the call site before the call is made — which is what makes the `[Conditional]` gate materially different from a runtime check rather than a stylistic alternative, since it removes the call site and its argument expressions outright.

Rots: nothing here — the open/close discipline and the argument-evaluation rule are architecture rather than names.

## Bridge

This is a technique topic and everything that schedules a job sits on top of it. Eight topics need something specific.

- **`ecs-in-this-game`** is the seam and the traffic runs both ways. It hands this topic the chunk geometry, the buffer capacity default and the archetype-move cost, all re-verified above (`src/Unity.Entities/Unity.Entities/Chunk.cs:28-39`, `TypeManager.cs:2292`, `BufferHeader.cs:11-29`). This topic hands back three things that file's bridge asked for: the absence of the collections safety system, now established from the field list rather than assumed (`src/UnityEngine.CoreModule/Unity.Collections/NativeArray.cs:218-246`) and reinforced by the toolchain defining no symbols; the barrier command buffer's allocator, which is why a mod never disposes one (`src/Unity.Entities/Unity.Entities/EntityCommandBufferSystem.cs:18-34`, `:58-79`); and the fourth allocator, since `World.UpdateAllocator` is what the `…ListAsync` gather forms that file cites are actually allocating from. It should also take the cleanup-component semantics from here rather than restating them: that file counts `ICleanupComponentData` among the component kinds a mod may declare, and what the entity store actually does with one — the residue archetype, the delayed destruction, the freeing on removal (`src/Unity.Entities/Unity.Entities/EntityComponentStore.cs:2641-2660`, `:3656-3683`, `:4386-4400`, `:3380-3414`) — is a lifetime fact rather than a declaration fact and is derived above. The job-interface ruling recorded at the throttling finding is the shared boundary: the chunk-level early exit is a throttle here and a job-shape fact there.
- **`diagnostics`** needs the leak-detection finding whole, because it is the answer to "my mod's memory climbs and nothing is logged": the game sets `NativeLeakDetectionMode.Disabled` in `GameManager.Awake` (`src/Game/Game.SceneFlow/GameManager.cs:1877-1880`, called at `:529`), a mod's `OnLoad` runs long after and can set it back (`Time2Work/NightShift/Mod.cs:175`), and the mode is process-global rather than per-mod. It also needs the shape of the failure this topic produces: an out-of-bounds native read or a use-after-free is a process death with no managed exception, so the diagnosis order for "the game crashed without a stack trace" starts here rather than in the log. And it should know that the Burst off switch is a debugging tool as much as a build one — `--burst-disable-compilation` or `UNITY_BURST_DISABLE_COMPILATION` puts the managed body back in the debugger's reach without a rebuild (`src/Unity.Burst/Unity.Burst/BurstCompilerOptions.cs:681-707`).
- **`roads-and-traffic`** is the heaviest area to query and the reason is structural rather than incidental: pathfinding reserves half the job worker threads with its own persistent per-thread allocators (`src/Game/Game.Pathfind/PathfindQueueSystem.cs:326`, `:672`), and the net and lane quadtrees are the two largest spatial indices in the game (`src/Game/Game.Net/SearchSystem.cs:742-743`). A mod querying either takes `GetNetSearchTree(readOnly: true, out …)` / `GetLaneSearchTree`, combines, and registers the scheduled handle with `AddNetSearchTreeReader` / `AddLaneSearchTreeReader` (`:850`, `:860`) — and both writers assign rather than combine, so a mod that writes to a vanilla net tree must have taken it with `readOnly: false` first or it drops the game's own handle out of the chain.
- **`mod-lifecycle-and-ordering`** owns the interval mechanism and this topic defers to it entirely; what travels the other way is the world-lifetime finding, since "`OnCreate` once per process, `OnDestroy` once per process, and the load hooks in between" is a lifecycle fact this topic derived (`src/Game/Game.SceneFlow/GameManager.cs:591`, `:750-756`, `:793`, `:2363-2418`) and a reader cannot allocate correctly without.
- **`save-serialization`** needs the same world-lifetime fact for a different reason: a mod's per-city native state is not disposed between cities, so a load must clear it, and the vanilla precedent is `IPreDeserialize` clearing a container rather than reallocating it (`src/Game/Game.Objects/SearchSystem.cs:453-465`, copied at `CS2-Platter/Platter/Systems/Parcels/P_ParcelSearchSystem.cs:52-59`). It should also know that `GC.Collect()` runs immediately before serialize and immediately before deserialize (`GameManager.cs:927`, `:1024`).
- **`debug-menu`** should take the polling cost: its own structure entry notes that a widget getter runs every frame the tab is open, and this topic's answer is that a getter doing a `CalculateEntityCount` is rung 3 of the cost ladder and one doing a `ToEntityArray` is rung 4 with a main-thread block (`src/Unity.Entities/Unity.Entities/EntityQueryImpl.cs:180-184`, `:406-412`).
- **`custom-tools`** and **`placement-definitions`** both consume vanilla search trees during a raycast or a snap, so both need the reader half of the protocol and the "register the scheduled handle" rule. The corpus's two wrong-argument sites are both in placement-adjacent systems (`CS2-Platter/Platter/Systems/Roads/P_RoadConnectionSystem.cs:124`, `CS2-Platter/Platter/Systems/Buildings/P_BuildingToParcelReferenceSystem.cs:73`).
- **`utilities-and-flow-networks`** and **`environment-and-pollution`** each read a cell map or a surface through the same protocol — `CellMapSystem.AddReader` (`src/Game/Game.Simulation/CellMapSystem.cs`), `WaterSystem`'s four readers, `TerrainSystem`'s two — and need nothing from this topic beyond it.

## Dead ends

- **The wiki page answered live on 2026-08-04, on the second attempt.** The first fetch returned HTTP 503; the second returned content. `survey-wiki-inventory.md`'s snapshot was not needed. Everything the page says about the three allocators is confirmed above except the `TempJob` four-frame figure, which nothing in the decompile states and nothing in this build enforces.
- **`How To Avoid Memory Leaks` is the only wiki page on this subject and it does not go past allocation.** It carries nothing on the reader/writer handle protocol, nothing on update intervals, nothing on Burst gating, nothing on query cost, and nothing on structural-change sync points. Fetched and checked: its three headings are `Managed vs Unmanaged Code`, `Unity's Unmanaged Collections`, `Example Code`. Everything in this file past the allocator table is uncorroborated by the wiki because the wiki does not go there.
- **`ICleanupComponentData` has one practitioner and no vanilla precedent.** Re-confirmed here: zero declarations under `src/Game/`, three in `CS2-WriteEverywhere` and nowhere else in 22 repositories. `ecs-in-this-game.md` recorded the same at twenty repositories and it still holds at twenty-two. So the cleanup-component pattern this topic owns is taught from one mod plus the engine's own semantics, with the game providing no example at all.
- **No managed `IComponentData` exists anywhere.** Searched `src/Game/` and all 22 repositories for `class <Name> : … IComponentData`; both return zero. The engine supports it (`ToComponentDataArray<T>() where T : class` exists at `src/Unity.Entities/Unity.Entities/EntityQueryImpl.cs:474-480`) and nobody in this ecosystem uses it. Recorded so the next pass does not treat the absence as a gap.
- **The wiki's `Systems` page was not re-fetched.** Its power-of-two claim is already verified and carried in `mod-lifecycle-and-ordering.md`, and nothing this topic needed turned on it.
- **`Allocator.Temp` passed into a scheduled job: not found in the game and not found in the corpus.** Searched both for a job-field initialiser assigned a `Allocator.Temp` container. Every hit is a `Temp` allocation made _inside_ a job body, feeding a nested helper struct — `src/Game/Game.Simulation/WaterPipeFlowJob.cs:526-527` inside `FluidFlowPhase`, `src/Game/Game.Vehicles/FixParkingLocationSystem.cs:1451`, and three `Traffic` migration jobs (`Traffic/Code/Systems/Serialization/TrafficDataMigrationSystem.ValidateLoadedDataJob.cs:272` and its two siblings). That is the legitimate use and the wiki's own sample shows it. What could not be established is what happens if someone does pass one in: the safety system that would catch it is compiled out, so there is no error path to read. Settling it needs an experiment against the running game.
- **The job-body free cannot be settled from the debugger.** Breaking inside a job and evaluating `GameObject.Destroy` or `GCHandle.Free` from that frame looks like the experiment, and is not: the SDB tooling marshals method and property calls to the game's main thread even when the paused frame belongs to a worker, so it can never reproduce a worker thread reaching a Unity API. It took a mod that actually makes the call from a job body, built and run once bursted and once not, which is what the purpose-built-mod section above records. Recorded so the next pass does not spend a live session rediscovering that the obvious route is closed, as a frame-time comparison against a frame-rate cap was before it.
- **How many corpus native-container allocations lack a matching dispose was not counted.** Separating them means matching allocation sites to disposal sites across `OnCreate`/`OnUpdate`/`OnDestroy` boundaries and through `using` statements, which is a read of every site rather than a grep. Do not ship a leak rate.
- **The published artefacts of the seven mods with dead Burst gates were not decompiled.** The claim above is about the checkout and is provable there; whether the shipped `.dll` carries the attributes is a different question and needs `ilspycmd` over the cached copies.
- **`survey-mods-techniques.md` §3.5 and §3.6 are superseded rather than confirmed.** §3.5's per-repo `[BurstCompile]` counts were taken over twelve repositories and no longer match at twenty-two — it reports `Traffic 64 / Platter 46 / BetterBulldozer 20` against 57 / 46 / 20 measured here, and it never saw `InfoLoom` (34), `Time2Work` (28) or `CS2-NetworkTools` (22). Its `IJobChunk 123 / IJob 30 / IJobParallelFor 17 / IJobFor 9` totals are superseded by `ecs-in-this-game.md`'s twenty-repository counts. Its claim that source-generated `IJobEntity` "doesn't play well with the modding toolchain" was already refuted there. §3.6's three spatial-index exemplars all still exist and all three citations were re-checked and hold. §3.5's partial-class-per-job claim holds with a moved citation: `Traffic`'s `<DependentUpon>` entries are 44 at `Traffic/Code/Traffic.csproj:170-299`, not "~40 at 594-727", and the survey's "Platter and MoveIt copy the convention" is now half true — `CS2-MoveIt` has 10 and `CS2-Platter` has none, while `Recolor` (14) and `SceneExplorer` (2) have picked it up.
- **`survey-decompile-moddable-surface.md` was checked for an allocation or job section and has none.** It is a namespace and assembly map; nothing in it bears on this topic beyond telling you where `Colossal.Collections` sits.
- **The mod catalog's `Demonstrates` half names nothing in this area.** Swept all 22 entries: the closest is `Area Bucket`'s "a whole geometry algorithm expressed as a chain of Burst jobs", which is about the algorithm rather than about Burst's cost or gating, and `Info Loom`'s "reading simulation state cheaply through a visibility-gated job", which is the throttle but is stated as a UI property. Six gaps are recorded above rather than a dead end, because the sweep found material to add rather than nothing.
- **`Colossal.Collections.Generic` is not a job-facing library.** Its nine files are `BiDictionary`, `OrderedDictionary`, `KeyedCollection2` and friends — ordinary managed collections, nothing native. Checked so the next pass does not open the directory expecting allocators.
- **No profiler surface was found that a mod can read.** `Unity.MemoryProfiler` and `Unity.Profiling.Core` ship as assemblies, and `src/Game/` was not swept for whether anything exposes them to a mod. `Colossal.PerformanceCounter` is used by `GameManager` for boot and shutdown timing (`src/Game/Game.SceneFlow/GameManager.cs:522`, `:574`, `:774`, `:1088`) and was not followed further; whether it is usable from a mod is unestablished and belongs to `diagnostics` if anywhere.
- **Two entries went to `conflicts.md` and both were ruled the same day (2026-08-04, ticket 18).** Whether shipped prose teaches re-enabling native leak detection given that the switch is process-global — ruled yes, bound to a condition; and which Burst gate the reference teaches given that the compile-time one is what the corpus reaches for and gets wrong seven times out of ten while the runtime one is first-party and unused — ruled both, runtime first. Each ruling is written in at the finding it governs rather than only here. Nothing else in this topic resisted the decompile.
