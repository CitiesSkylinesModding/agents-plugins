# Performance and memory

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

Not leaking, and not stalling.
Writing the job itself belongs to `ecs-in-this-game`; this reference owns what the job's memory costs and what its scheduling has to respect.

Everything here rests on one property: **this build has no collections safety system**, so almost every mistake below is silent.
An out-of-bounds native read, two jobs writing one container, a container disposed while a job still holds it — none of the three throws, and each surfaces later as a crash somewhere else or as wrong numbers.
The guards that would catch them are compiled out of the assemblies a mod links against, and a mod's own compile cannot put them back.
Source: `src/UnityEngine.CoreModule/Unity.Collections/NativeArray.cs` (the guards and the symbol they are conditional on), `%CSII_TOOLPATH%/Mod.props`, `%CSII_TOOLPATH%/Mod.targets` (the mod compile defining no symbols).

[`data-providers.md`](data-providers.md) enumerates the vanilla systems that publish data through the reader/writer handle protocol.
[`colossal-collections.md`](colossal-collections.md) describes the game's own container library, which a fork of a vanilla job meets immediately.
[`cleanup-components.md`](cleanup-components.md) carries the pattern for an entity that owns a managed engine resource, whose destruction frees nothing without it.
[`burst-at-debug-time.md`](burst-at-debug-time.md) carries what Burst costs a debugger and the two ways to turn it off, since a breakpoint in a Burst-compiled job binds and never fires.

## The world outlives every city, so `OnDestroy` is a process-exit hook

One ECS world is created during the boot sequence, before any city exists, and destroyed only when the application goes away.
Loading a city, returning to the main menu, loading another city and quitting again all happen inside that same world, with the same system instances.
(VOLATILE: the world creation and destruction call sites — `GameManager` in the scene-flow namespace.)

Three consequences a mod designs around:

- **`OnCreate` runs once per process and `OnDestroy` runs once per process**, nothing in the supported lifecycle destroying a system mid-session.
  An `Allocator.Persistent` container allocated in `OnCreate` and disposed in `OnDestroy` is correct and costs one allocation for the whole session.
  A container allocated per loaded city and disposed in `OnDestroy` leaks once per city the player opens, and the player sees only memory climbing across a long session.
  Source: `src/Game/Game.SceneFlow/GameManager.cs`.
- **Per-city state is cleared, not recreated.**
  The vanilla spatial indices show the shape: they allocate their trees in `OnCreate`, and in the pre-deserialize hook they complete their outstanding handles and call `Clear()` rather than disposing and reallocating.
  Source: `src/Game/Game.Objects/SearchSystem.cs`.
- **`OnDestroy` is not where you observe a leak.**
  By the time it runs the process is exiting and nothing reports what was still allocated.
  The load-boundary hooks are where a mod can see its own accumulation.
  Source: `src/Game/Game.SceneFlow/GameManager.cs` (the world's teardown at process exit, and the boot call that disables leak detection).

`mod-lifecycle-and-ordering` owns the hooks themselves; `save-serialization` owns the load boundary this makes load-bearing.

## Managed against unmanaged, and where a mod's own data sits

Four places, and only one is garbage-collected.

**ECS component data is unmanaged, always.**
The game declares no managed components, and every component in it is a struct in chunk memory.
A native container refuses a managed element type outright, and even that refusal is a compiled-out check.
Source: `src/UnityEngine.CoreModule/Unity.Collections/NativeArray.cs` (the refusal, and that it is compiled out).

**Chunk memory is unmanaged and owned by the entity store.**
A mod declaring a component pays for it in chunk space and pays nothing to manage it.
What that costs per entity is `ecs-in-this-game`'s chunk geometry.
Source: `src/Unity.Entities/Unity.Entities/Chunk.cs`, `src/Unity.Entities/Unity.Entities/EntityComponentStore.cs` (the store that allocates the chunk).

**Native containers a mod allocates are unmanaged, untracked and invisible.**
A `NativeArray<T>` here is three fields — a raw buffer pointer, a length and an allocator label — and allocates through the native tracked-malloc path.
Nothing about it is reachable from the GC, so a leaked one never appears in a managed heap dump, and for the reason below it appears nowhere else either.
Source: `src/UnityEngine.CoreModule/Unity.Collections/NativeArray.cs`.

**A mod's managed objects are collected normally, with one permanent exception.**
Systems, the `IMod` implementation, settings objects and anything hanging off them are ordinary managed objects.
Prefabs are not: a prefab derives from `ScriptableObject`, and the prefab system holds every registered prefab in a list and in dictionaries keyed by the object itself.
A prefab a mod synthesises is therefore rooted for as long as it stays registered, and the game's own unused-asset sweeps cannot collect it while it is.
Unregistering it through the prefab system's own removal method is what releases those roots, and that method is not bookkeeping: it marks the prefab's entity deleted and reindexes another prefab's, so it is a structural change against a live city rather than a memory-hygiene call.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`, `src/Game/Game.Prefabs/PrefabBase.cs`, `src/Game/Game.Prefabs/ComponentBase.cs`.

**Where an entity needs a managed thing, the game stores an integer and not a reference.**
The shared component carries an index; a managed system owns the table it indexes into.
Copy that shape before reaching for the cleanup-component pattern in [`cleanup-components.md`](cleanup-components.md), which is the heavier answer and exists for the case where the entity genuinely owns the resource's lifetime.
Source: `src/Game/Game.Net/ArrowMaterial.cs`, `src/Game/Game.Rendering/AggregateMeshSystem.cs`.

## Four allocators, and each one's lifetime

Three are the ones everybody names, and the fourth is in constant use throughout the game.

| Allocator | Lifetime | Freed by |
| --- | --- | --- |
| `Allocator.Temp` | The current frame, per thread | The native job system, automatically |
| `Allocator.TempJob` | Four frames | Your `Dispose` |
| `Allocator.Persistent` | Until disposed | Your `Dispose` |
| `World.UpdateAllocator` | This frame and the next | The world's own rewind, automatically |

`Allocator.Temp` is for a container allocated and consumed inside one job body or one main-thread block; it is freed automatically whether or not you dispose it, and the game does both.
**Never assign a `Temp` container into a job struct you schedule.**
The allocation lives in the allocating thread's own temp region, so a worker thread reading it is an out-of-thread read of memory that may already be gone, and nothing on this build reports it.
`TempJob` or `World.UpdateAllocator` is the form for anything a scheduled job touches.
`Allocator.TempJob` is the default for a container that a scheduled job reads and that you dispose against that job's handle.
What that four-frame limit costs you when it is missed is the leak section's, below.
`Allocator.Persistent` is for state a system owns across the session, allocated in `OnCreate`.
Source: `src/UnityEngine.CoreModule/Unity.Collections/Allocator.cs`, `src/UnityEngine.CoreModule/Unity.Collections.LowLevel.Unsafe/UnsafeUtility.cs` (the allocation is an `extern`; the lifetimes and the ban on passing a `Temp` container into a job field are the engine's own, per https://docs.unity3d.com/2022.3/Documentation/Manual/JobSystemNativeContainer.html).

**The fourth is the one to reach for when gathering query results to feed a job.**
It is a double-buffered rewindable allocator, reached as `base.World.UpdateAllocator.ToAllocator`, and the rewind swaps between two buffers so the one being reset is never the one in use.
Memory handed to a job scheduled this frame is therefore still valid when that job runs, and no `Dispose` is needed or wanted:

```csharp
NativeList<Entity> entities = m_Query.ToEntityListAsync(
    base.World.UpdateAllocator.ToAllocator, out JobHandle outJobHandle);
```

**The list comes back empty**: the gather runs as a scheduled job, and `outJobHandle` is what says it has finished.
Combine that handle into every schedule that reads the list, as the canonical shape below does — dropping it and iterating the list is a read of a buffer a worker thread is still filling.
Source: `src/Unity.Collections/Unity.Collections/DoubleRewindableAllocators.cs`, `src/Game/Game.SceneFlow/GameManager.cs` (the double buffering and the per-tick rewind), `src/Unity.Entities/Unity.Entities/EntityQueryImpl.cs` (the asynchronous gather and the handle it hands back).

Two further constraints on it.
The rewind happens only on a frame where the world actually ticks, so on a frame the world is not updating the allocator is not rewound and its contents persist.
And `Dispose(JobHandle)` on a `NativeArray` from a custom allocator throws `InvalidOperationException` — one of the very few mistakes in this area that announces itself.
`NativeList<T>` carries no such guard: its `Dispose(JobHandle)` frees through the list's own allocator handle, and a rewindable allocator's free path returns the memory to nobody, so disposing an update-allocator-backed list against a handle is not a double free — which is why the vanilla rendering systems ship doing exactly that.

(VOLATILE: the allocator enum's members and the custom-allocator index threshold — the `Allocator` enum and `AllocatorManager` in the collections package.)

## One dependency graph, and what `Complete()` drains

Every scheduled job hangs off one world-wide dependency graph, and the scheduler tracks it per component type, per system.
When your update runs, `base.Dependency` is derived fresh from that graph: for every type your system reads, the handle of the last chain that wrote it; for every type it writes, that handle plus every reader's.
Each of those handles is a whole system's combined output — a system publishes one handle covering everything it scheduled — so your job waits on jobs touching components it never uses, and every system after you inherits your handle the same way.
None of that inheritance stalls the main thread by itself: a dependency defers a job's start behind its chain on the workers, and the main thread waits only when something completes.

**`Complete()` finishes the job and every job upstream of its handle — the chain jumps the worker queue, and the completing thread attempts the job itself.**
So the cost of completing a handle is not bounded by your job's duration — it is the job plus however much of the chain upstream of you has not finished, which for a job scheduled against simulation components is a slice of the frame's simulation.
It is still one chain: the sync that completes **every job the dependency graph knows** is the structural change, whose section below owns that cost.
Source: `src/Unity.Entities/Unity.Entities/ComponentDependencyManager.cs`, `src/UnityEngine.CoreModule/Unity.Jobs/JobHandle.cs` (`Complete` is an `extern`; the semantics are the engine's own, per https://docs.unity3d.com/2022.3/Documentation/Manual/JobSystemJobDependencies.html and https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Unity.Jobs.JobHandle.Complete.html).

Complete when the main thread must have the value now; record the outcome from inside the job and let a barrier play it back when the output is a write, per the structural-change section below; and at teardown a completion guards nothing the world has not already completed, per the disposal section and its two gaps.
A completion to hunt for: a second job scheduled against `base.Dependency` instead of against the job that filled its input, with a `Complete()` between them doing the ordering a chained handle would have done for free.

### Taking a lookup mid-frame is a hidden, one-time sync

`GetComponentLookup` and `GetBufferLookup` register the type with your system, and the first registration of a type completes your system's tracked dependencies on the spot — for the type being registered, read-write waits for its in-flight writers and readers, read-only for the writers alone, and every type you had already registered enters the same wait.
`GetComponentTypeHandle` and `GetBufferTypeHandle` take the same registering path; the shared-component and entity handles register nothing and never wait.
A type first taken read-only and later taken read-write fires it a second time.
Every later acquisition of the same type is free, which makes the cost easy to misattribute: a `GetComponentLookup` call inside `OnUpdate` stalls on the first frame and never again.
The shape that never pays it is vanilla's own: acquire in `OnCreate`, keep the lookup in a field, and call its `Update(this)` at the top of `OnUpdate` — on this build that refresh is a version bump and nothing else.
`SystemAPI.GetComponentLookup` is already that shape: the generator hoists the acquisition into its compiler-run create hook and leaves only the refresh at the call site, so the idiomatic form never pays the stall — the direct `GetComponentLookup` call on the system is what does.
Source: `src/Unity.Entities/Unity.Entities/SystemState.cs`, `src/Unity.Entities/Unity.Entities.Internal/InternalCompilerInterface.cs`.

On this build *using* a lookup you hold never syncs and never warns — the indexer is a raw read or write with every guard compiled out, so a main-thread write through it while jobs are in flight is a silent race — and acquisition is the only point where the lookup itself ever waits.
The generated `SystemAPI.GetComponent`-style single-entity accessors are a different call: each one completes the type's fences before touching the data, a per-call stall the acquire-once shape never pays.
(VOLATILE: the first-acquisition completion and the `Update` refresh — `SystemState` and `ComponentLookup` in the entities package.)

## Disposing a container a job may still be reading

Two mechanisms, and the type forces the choice rather than taste deciding it.

**`Dispose(JobHandle)` is the default and it does not block.**
It builds a dispose job carrying the raw buffer pointer and the allocator label, schedules it on the handle you pass, and returns a new handle.
The free happens on a worker thread after the reading job finishes.
Every stock container has this overload.
Source: `src/UnityEngine.CoreModule/Unity.Collections/NativeArray.cs`.

**Most of the game's own containers do not have it**, and for those the discipline is complete-then-dispose; [`colossal-collections.md`](colossal-collections.md) names the two that do.
`Complete()` is a main-thread stall until the job and every job upstream of its handle finish — the dependency-graph section above owns why that chain can be a slice of the frame.
Complete **every** outstanding handle before disposing or clearing while the world is live: the vanilla search systems take each tree `readOnly: false` and complete the one combined handle it returns before the load-boundary `Clear()`.
Source: `src/Colossal.Collections/Colossal.Collections/NativeAccumulator.cs`, `src/Colossal.Collections/Colossal.Collections/NativeParallelQueue.cs` (the two asynchronous disposes the rest of that library lacks), `src/Game/Game.Objects/SearchSystem.cs`, `src/Game/Game.Net/SearchSystem.cs`, `src/Game/Game.Zones/SearchSystem.cs`, `src/Game/Game.Areas/SearchSystem.cs`, `src/Game/Game.Routes/SearchSystem.cs`, `src/Game/Game.Effects/SearchSystem.cs` (the complete-then-clear at the load boundary).

`OnDestroy` is the exception, and vanilla's own teardowns split exactly there — some search systems complete first, the rest dispose bare, and both forms are safe.
By the time `OnDestroy` runs on the world's own teardown — the only path this game takes to it — every published job is complete: `World.Dispose` completes all tracked jobs before it destroys the first system.
So a bare `Dispose()` in `OnDestroy` touches nothing a job still holds, provided every job that touched the container published its handle through `base.Dependency` — an unpublished handle is one gap a teardown completion would still cover, and a job scheduled without being published is already a bug against the discipline below.
The other gap is a system torn down individually by a direct world call — nothing in the supported lifecycle does that, per `mod-lifecycle-and-ordering` — which completes only that system's own published handle on the way down, so a foreign job still holding the container leaves that `OnDestroy` under the live-world rule above.
Source: `src/Unity.Entities/Unity.Entities/World.cs`.
(VOLATILE: the complete-before-destroy ordering — `World.Dispose` in the entities package.)

### The canonical shape, and it is ten lines

The vanilla service-district system is the whole idiom with nothing else in it:

```csharp
JobHandle outJobHandle;
NativeList<Entity> deletedDistricts =
    m_DeletedDistrictQuery.ToEntityListAsync(Allocator.TempJob, out outJobHandle);

JobHandle jobHandle = JobChunkExtensions.ScheduleParallel(new RemoveServiceDistrictsJob
{
    m_DeletedDistricts = deletedDistricts,
    // …handles…
}, m_ServiceDistrictQuery, JobHandle.CombineDependencies(base.Dependency, outJobHandle));

deletedDistricts.Dispose(jobHandle);
base.Dependency = jobHandle;
```

Five things happen and each is load-bearing: the gather is asynchronous and hands back its own handle; that handle is **combined** with `base.Dependency` rather than replacing it; the container is disposed against the _scheduled_ handle rather than immediately; `base.Dependency` is assigned so the next system waits; and nothing is completed.

### The job-handle discipline, in six rules

1. **Combine, never replace, when consuming someone else's handle.**
   `JobHandle.CombineDependencies` takes at most three handles, or a `NativeArray<JobHandle>`; past three the game usually calls `Colossal.Entities.JobUtils.CombineDependencies`, whose overloads take more, though it also nests the three-argument form. Either is idiomatic.
   Source: `src/UnityEngine.CoreModule/Unity.Jobs/JobHandle.cs`, `src/Colossal.Core/Colossal.Entities/JobUtils.cs`.
2. **Assign `base.Dependency` after scheduling.**
   Nothing downstream waits for a job whose handle was never published.
   Source: `src/Unity.Entities/Unity.Entities/SystemState.cs`, `src/Unity.Entities/Unity.Entities/ComponentDependencyManager.cs`.
3. **Register with every provider whose data you took**, using the handle you got back from scheduling.
   The protocol is below.
4. **Register with the barrier if you wrote through its command buffer.**
   `AddJobHandleForProducer`, and the contract is `ecs-in-this-game`'s.
5. **Complete only where something outside the chained graph forces it: a genuine main-thread readback, the conflicting work before a `Run`, an unregistered provider's next write, or a container without `Dispose(JobHandle)` before a live-world dispose or clear.**
   Every `Complete()` is a main-thread stall until the job and its whole upstream chain finish; at teardown the world has already completed everything, per the disposal section.
   Source: `src/UnityEngine.CoreModule/Unity.Jobs/JobHandle.cs` (the stall; `Complete` is an `extern` and the chain is the engine's own, per https://docs.unity3d.com/2022.3/Documentation/Manual/JobSystemJobDependencies.html), `src/Unity.Entities/Unity.Entities/World.cs` (the teardown completion).
6. **Do not read a handle back out of `base.Dependency` across simulation iterations.**
   A simulation phase runs up to eight times in one frame, and a system the interval gate lets run has `base.Dependency` reset to `default` immediately before its own `Update`, on every iteration whose index its interval is at or below.
   That reset pre-empts the completion stock `SystemBase.Update` would have performed, so last iteration's handle is not completed for you before the next one runs — while `base.Dependency` still comes back derived from the graph, carrying nothing for a container the graph does not track.
   Anything that must outlive an iteration is held in your own field, and combined rather than replaced when you consume it.
   Source: `src/Game/Game/UpdateSystem.cs` and `src/Game/Game/GameSystemBase.cs` (the reset and its interval gate), `src/Unity.Entities/Unity.Entities/SystemState.cs` (the pre-empted completion).

### Choosing the schedule form

`ScheduleParallel` splits the work across workers chunk by chunk, and is the default wherever every write goes through a parallel-safe sink — a `ParallelWriter`, a parallel command buffer, or the entity's own components.
`Schedule` runs the whole job as one sequential unit on one worker: plain container writes with no `ParallelWriter`, state carried across chunks, a guaranteed iteration order — still off the main thread and overlapped with everything else, as long as nothing completes it early.
`Run` executes the job body on the main thread immediately, and a job struct's `Run` completes nothing first: complete your conflicting work before it — `CompleteDependency()` for the ECS graph, plus a `Complete()` on any provider handle you took, which lives outside that graph — because the safety system that would have caught a job still holding your data is compiled out, and the overlap is silent.
Source: `src/Unity.Entities/Unity.Entities/JobChunkExtensions.cs`.

**`Schedule()` followed immediately by `Complete()` adds only a scheduling round trip to `Run`: complete the same conflicting work and write `Run` instead.**
The scheduler attempts a completed job on the completing thread itself, so the pairing buys a scheduling round trip and nothing else — it does not even reliably buy a worker.
Source: `src/UnityEngine.CoreModule/Unity.Jobs/JobHandle.cs` (`Complete` is an `extern`; the completing thread's participation is the engine's own, per https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Unity.Jobs.JobHandle.Complete.html).

## The reader/writer protocol

Where one system owns data other systems' jobs read, the game exposes three methods rather than a property.
The shape, from the object search system:

```csharp
public NativeQuadTree<Entity, QuadTreeBoundsXZ> GetStaticSearchTree(bool readOnly, out JobHandle dependencies)
{
    dependencies = readOnly
        ? m_StaticWriteDependencies
        : JobHandle.CombineDependencies(m_StaticReadDependencies, m_StaticWriteDependencies);
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

Read it as a reader/writer lock expressed in handles.
A reader needs only the writer's handle, because readers do not conflict with each other.
A writer needs the readers' and the writer's, because it conflicts with everything — and that is what the `readOnly` flag selects.

**The asymmetry is the part copied wrongly.**
`AddReader` combines; `AddWriter` assigns.
Assignment is safe only when there is exactly one writer, and in the vanilla systems the owner is it — the owning system calls its own `AddWriter(base.Dependency)` right after scheduling.
Combining is the form that survives a second writer, so combine in a mod-owned provider.
Source: `src/Game/Game.Objects/SearchSystem.cs`.

### Consuming a provider: four steps, in order

1. **Take the data with the right `readOnly` flag.**
   `true` if your job only reads it; `false` if it writes.
   The flag decides which handle you get back: `true` returns the owner's write handle alone, `false` returns the readers' and the writer's combined.
   That is why a writing job must take it `false` — taken `true` it never waits for the other readers, and writes into a tree they are still walking.
2. **Combine the returned handle into your schedule**, alongside `base.Dependency` and every other provider's handle.
3. **Register the handle you got back from `Schedule`** with _every_ provider you took from.
   Passing back the handle the provider gave you is a no-op — the owner already waited for it — and the owner's next write then does not wait for your job, so the container is mutated while your job walks it.
   Registering with only some of the providers you took from has the same effect on the ones you skipped.
4. **Complete before disposing anything that has no asynchronous dispose while the world is live**, per the containers named above — at teardown nothing needs it, per the disposal section.

**A second provider shape exists, and the register-back step has nothing to bind to.**
Several vanilla tool systems publish a work list through a single getter that hands out the owner's own `base.Dependency` and the container, with no reader or writer registration method beside it.
A provider that never learns your handle cannot wait for you, so nothing stops its next write from landing while your job still reads — the returned handle orders you after the owner, never the owner after you.
Consume it the way the game's own rendering consumers do — schedule against the returned handle in the same frame and publish through `base.Dependency` — and treat the loan as over once the owner updates again, since nothing orders the owner's next write after you: a job that can still be running by then needs its own completion or its own copy of the data.
Source: `src/Game/Game.Tools/NetToolSystem.cs`, `src/Game/Game.Rendering/GuideLinesSystem.cs`.

`custom-tools` and `placement-definitions` both consume vanilla search trees during a raycast or a snap, and `roads-and-traffic` is the heaviest area to query.

### A mod-owned spatial index

`NativeQuadTree<TItem, TBounds>` is the game's own, and **the item type is a parameter rather than always `Entity`**.
A job field or an iterator written against a provider has to match that provider's pair, not a single assumed one; [`data-providers.md`](data-providers.md) tables them per system.
Its constructor takes a minimum item size — a tuning choice scaled to the items it will hold rather than a constant to copy — and an allocator; the vanilla trees pass `1f`.
Its surface is `Add`/`TryAdd`, `Update`/`TryUpdate`, `AddOrUpdate`, `Remove`/`TryRemove`, `Get`/`TryGet`, `Clear`, four `Iterate` overloads and `Select`, driven by the iterator and selector interfaces beside it.
Source: `src/Colossal.Collections/Colossal.Collections/NativeQuadTree.cs`, `src/Game/Game.Objects/SearchSystem.cs`, `src/Game/Game.Net/SearchSystem.cs` (the `1f` the vanilla trees pass).

**Four of the non-`Try` forms throw a bare `System.Exception`, and those throws are not conditional** — unlike almost everything else in this collections stack, they will fire in a player's game.
`Update`, `Remove` and `Get` throw on a **miss**; `Add` throws on a **hit**, when the item is already in the tree.
`AddOrUpdate` has no hit-or-miss guard at all, which is what makes it the form to reach for on a re-registration.
Use the `Try` forms unless the outcome is genuinely impossible.
Source: `src/Colossal.Collections/Colossal.Collections/NativeQuadTree.cs`.

What a mod-owned index needs, beyond the container:

- **Allocation in `OnCreate` from `Allocator.Persistent`**, because the world spans the session.
- **A `Clear()` from the pre-deserialize hook**, not a dispose-and-reallocate, so the tree survives city changes.
  Take the tree with `readOnly: false`, complete the handle it returns, and only then clear — the vanilla systems all complete before clearing, and clearing under a running reader is an unsynchronised structural mutation with no diagnostic.
  Source: `src/Game/Game.Objects/SearchSystem.cs`, `src/Game/Game.Net/SearchSystem.cs`, `src/Game/Game.Zones/SearchSystem.cs`, `src/Game/Game.Areas/SearchSystem.cs`, `src/Game/Game.Routes/SearchSystem.cs`, `src/Game/Game.Effects/SearchSystem.cs`.
- **The three-method protocol above**, with the writer combining rather than assigning.
- **A `Dispose()` in `OnDestroy`, which may be bare** once every job that touched the tree published through `base.Dependency` — the disposal section owns the gaps.

(VOLATILE: the vanilla accessor names — `GetStaticSearchTree`, `AddStaticSearchTreeReader` and their siblings across the search systems in the objects, net, zones, areas, routes and effects namespaces.)

## Throttling: run less often, and enter `OnUpdate` less often

Three mechanisms, in increasing order of what they let you skip.

**The update interval skips the whole update.**
Your `GetUpdateInterval` override is called **once, at registration**, and the value is cached; the dispatch loop then tests the cached mask _before_ `Update()` is called, so a skipped frame costs one bitwise-and and one comparison, and the query gate, `OnUpdate`, the handle refresh and the schedule are all skipped whole.
Because the override runs once, an interval computed from mutable state — a settings slider, a load-time flag — is frozen at whatever it returned during registration, and nothing reports that it never changes again.
The mechanism, its power-of-two requirement and the three phases that honour it belong to `mod-lifecycle-and-ordering`.
Source: `src/Game/Game/UpdateSystem.cs`, `src/Game/Game/GameSystemBase.cs`.

- **Only the simulation phases consult it.**
  An interval on a system registered anywhere else is dead code that reads like a throttle, and the game itself ships one such system.
  Check the phase before you write the override.
  Source: `src/Game/Game/UpdateSystem.cs`, `src/Game/Game.Simulation/SimulationSystem.cs` (the only caller that passes an update index), `src/Game/Game.Audio/WeatherAudioSystem.cs`, `src/Game/Game.Common/SystemOrder.cs` (the shipped interval nothing reads).
- **Let the offset be auto-assigned.**
  The default is negative, which the dispatcher reads as "spread me": it hands out offsets so that systems sharing an interval land on different frames instead of piling onto one.
  Do not predict which frame you land on relative to another system — the assignment order is not something to build a producer/consumer handoff on.
  Returning an explicit offset opts out of that.
  Source: `src/Game/Game/GameSystemBase.cs`, `src/Game/Game/UpdateSystem.cs`.

The shape to copy is an interval **plus** a relevance gate — a system feeding an on-screen panel returns a large interval and opens `OnUpdate` with an early return while the panel is hidden.

**`RequireForUpdate` / `RequireAnyForUpdate` skip `OnUpdate` entirely** when a required query is empty.
The check is a length read on a cached chunk list: no chunk walk, no dependency sync.
It tests the query **ignoring your filter and its enableable components' state**, so a query narrowed with `SetSharedComponentFilter` still gates on the unfiltered set — a filter is not a throttle, and neither is a disabled marker.
`IsEmptyIgnoreFilter` is the same free check spelled by hand, and is what to use inside `OnUpdate` when one system schedules several independent jobs each needing its own gate — with the same blind spot, so a gate over an enableable marker never closes.
`ecs-in-this-game` owns what the gate does and does not see.
Source: `src/Unity.Entities/Unity.Entities/SystemState.cs`, `src/Unity.Entities/Unity.Entities/EntityQueryImpl.cs`.

**The chunk-level early exit inside the job body is the finest-grained form**, and is what the simulation actually runs on: one shared-component read rejects a whole chunk's worth of entities.
A per-entity job cannot express it — that is one of the three things holding the chunk buys you, per `ecs-in-this-game` — so a per-entity job wanting the same throttle pushes the test into the query as a shared-component filter before scheduling instead.
`ecs-in-this-game`'s bucketing sibling owns which index and which count to filter on — guessing either runs the job at the wrong cadence or on nothing, with nothing logged; what that cadence is worth in simulated time is `simulation-time-and-units`.
Source: `src/Game/Game.Simulation/AgingSystem.cs` (the in-job test), `src/Game/Game.Simulation/BuildingUpkeepSystem.cs` (the query-filter form).

## A log call is a synchronous file write, so logging is a per-frame cost like any other

**A message that passes the level filter opens the log file, writes, flushes, and closes it again**, under a lock.
Nothing batches and nothing buffers across messages: the stream is held open only if the logger's `keepStreamOpen` is set, which defaults off and which nothing in the game turns on.
So a log call that gets written is not a cheap side effect that disappears in a release build — it is a file-system round trip on the calling thread, serialised against every other logger in the process.
Source: `src/Colossal.Logging/Colossal.Logging/UnityLogger.cs`, `src/Colossal.Logging/Colossal.Logging/ILog.cs`.

That makes the cost scale with the thing a mod is tempted to log from.
Once per load is free.
Once per frame is a file open and close every frame.
Once per entity in a system's update is a file open and close per entity per frame, which is enough to be felt on its own — before counting the string that had to be built to pass to it.

**The level filter is the cheap gate, and it runs first.**
Every level-specific method tests the logger's `effectivenessLevel` before doing any of the above, so a line below the shipped level is neither formatted nor written — though its arguments are evaluated before the test runs.
That is the first lever to reach for.
Source: `src/Colossal.Logging/Colossal.Logging/UnityLogger.cs`.

The rest follow, in the order to reach for them.

- **Pass the format and its arguments separately rather than interpolating.**
  The `*Format` overloads check the level _before_ formatting, so a filtered-out `DebugFormat` skips building the message entirely — while the arguments themselves are evaluated at the call site regardless, so a costly call in a format parameter runs whether or not the line is written.
- **Keep the log out of the per-entity loop, and out of the update itself.**
  Aggregate to a count or a worst case and write that single line when the aggregate changes, not every frame it holds.
  A managed job that logs pays the same round trip and serialises on the same lock as the main thread; a Burst-compiled one cannot call a logger at all, and its only channel is the engine's own logging entry point.
- **Where a line has to survive in a release build and fire often, hold the stream open.**
  Setting `keepStreamOpen` on the logger removes the per-message open and close, which is the bulk of the cost, and is one line in the mod's load hook.
- **To pay nothing at all in a release build, gate the call at compile time rather than at runtime.**
  A logging method marked `[Conditional("SYMBOL")]` has its **call sites removed entirely** when the symbol is undefined — arguments included — so an interpolated message is never built and the runtime level check never runs.
  The removal reaches nothing above the call: a costly expression hoisted into a named local survives the stripping and runs for a value nothing reads.
  One method per category, each with its own symbol, gives per-category logging that costs a release build nothing.
  An undefined symbol is as silent here as it is on the Burst gate in [`burst-at-debug-time.md`](burst-at-debug-time.md), and it resolves differently: `[Conditional]` is applied where the call is compiled, not where the method is declared.
  So the symbol goes in the project holding the call sites: a logging helper living in a shared assembly is stripped or kept by the _calling_ mod's configuration, never by the shared assembly's.
- **Where the line must survive into a shipped build, throttle it like any other periodic work** — the update-interval and change-detection mechanisms above apply unchanged to a logging system.

(VOLATILE: the `keepStreamOpen` and `effectivenessLevel` property names and the `*Format` overload family — the logging library's logger type.)

## Main-thread scans that look harmless

Four rungs, and a mod's per-frame code should stay on the first two.

| Rung | Call | Cost |
| --- | --- | --- |
| 1 | `IsEmptyIgnoreFilter` | A length read on a cached chunk list. Effectively free. |
| 2 | `IsEmpty` | Rung 1 when the query has no filter and no enableable component, or when the unfiltered query is already empty. Otherwise a filter sync plus a walk that stops at the first matching chunk. |
| 3 | `CalculateEntityCount` / `CalculateChunkCount` | A filter sync plus a walk of every matching chunk. |
| 4 | `ToEntityArray` / `ToComponentDataArray` | A count, a **main-thread block** on every job writing that type, and a full copy. |

**The async list forms are the pairing to copy.**
`ToEntityListAsync` and `ToComponentDataListAsync` take an `out JobHandle` and _combine_ the dependency instead of completing it; the game calls those and never the async array forms.
Source: `src/Unity.Entities/Unity.Entities/EntityQueryImpl.cs`.

The synchronous forms are not forbidden, and the game's own load and editor paths use them heavily; the rule is that each one is a stall, so a system calling `ToEntityArray` every frame stalls every frame.
`debug-menu` inherits this directly: a widget getter runs every frame its tab is open, so a getter doing a count is rung 3 and one doing a `ToEntityArray` is rung 4.

**The failure mode here is a stall, not a leak, and the diagnosis differs.**
A leaked container costs memory and shows nothing.
A per-frame `ToEntityArray` costs frame time and shows up under the mod's own system name in a profile.

### Structural changes on the main thread complete every job the dependency graph knows

This is the heaviest sync point available and it is one line of mod code.
Every `EntityManager` method marked `[StructuralChangeMethod]` — adding a component, removing one, creating or destroying an entity, and assigning a shared component value — routes through a call that completes **every job the dependency graph knows**, not just the ones touching your components — a handle you kept in a field and never published through `Dependency` is not one of them, and survives the drain still running.
Chained structural calls pay the drain once — the first completes everything in flight and the rest find nothing — but each add is still its own archetype move, so build with `CreateEntity` plus an archetype rather than as a create followed by adds.
`EntityManager.SetComponentData` and the read-write data path are cheaper but not free: each completes the read and write dependency for that one type.
Source: `src/Unity.Entities/Unity.Entities/EntityDataAccess.cs`, `src/Unity.Entities/Unity.Entities/ComponentDependencyManager.cs`.

**A barrier's command buffer is the alternative to completing, not only to `EntityManager`.**
Where the reason to complete is to act on a job's output — add a component from a result, destroy what a scan found, write a computed value back — the job records the action into the buffer instead, component writes included, and the barrier completes its own producers at its own phase before playing back.
The stall the mod would have paid mid-frame merges into the wait the game's own producers already share at that barrier.
The work itself does not shrink: playback performs every recorded change, so a deferred per-entity churn is a per-entity churn the barrier pays.
What a buffer cannot replace is a value the mod itself must read on the main thread this frame: deferral moves a write, not a read.
Source: `src/Unity.Entities/Unity.Entities/EntityCommandBufferSystem.cs`.

The barrier allocates each buffer from its own rewindable allocator, plays every pending buffer back in its own update, disposes it and rewinds — so a mod never disposes a buffer it got from `CreateCommandBuffer()` and never should.
A hand-rolled `new EntityCommandBuffer(Allocator.TempJob)` is the exception: that one the caller owns and disposes.
The barrier contract itself is `ecs-in-this-game`'s.

Adding or removing a component is also an archetype move, which copies the entity between chunks.
Nothing about a tag is free except its storage.

### Sizing, where it changes a decision

`ecs-in-this-game` owns the chunk constants; what they decide is here.

An archetype is **chunk-bound** when its components are large enough that the per-entity budget runs out before the entity cap does, and **entity-bound** when the cap is what binds — the first wastes bytes, the second wastes slots, and a tag-only archetype is the extreme of the second.
Work out which you are before adding a field: past the threshold, one more byte per entity costs you a whole extra chunk's worth of them.

A dynamic buffer costs its header plus its internal capacity in **every** entity's chunk slot, occupied or not.
`[InternalBufferCapacity(0)]` moves the payload out of the chunk entirely, leaving the header, and overflow allocates from `Allocator.Persistent`.
That trades chunk density for one heap allocation per non-empty buffer, which is the right trade when most entities have none.
**The spill does not undo itself.** A buffer that outgrows its internal capacity moves to the heap and stays there until something asks for it back, paying the allocation and the reserved inline bytes together however short it later gets.
So size for the maximum a buffer will reach rather than for its typical length, or call `TrimExcess()` once the spike is over: it reallocates to the length you are actually holding, and returns the payload to the chunk when that length fits the internal capacity.
Source: `src/Unity.Entities/Unity.Entities/DynamicBuffer.cs`, `src/Unity.Entities/Unity.Entities/BufferHeader.cs`.

## The game's three forced garbage collections

The game runs a full unused-asset sweep plus `GC.Collect()` around the load boundary and nowhere else: immediately before serializing a save, immediately before deserializing one, and immediately after loading finishes.
There is no per-frame collection, so managed allocation in a hot path is the mod's own problem and nothing will hide it.

The useful corollary: a mod's preload and loading-complete hooks run adjacent to a full blocking collection, already inside the frame the player experiences as a load-time hitch.
That makes them the cheap place to do expensive managed setup — and the wrong place to add more than you need.

## The one leak instrument this game has, and its condition

Native leak detection is **switched off**, from a method in the boot sequence whose name says the opposite.
(VOLATILE: the method name and its call site — `GameManager` in the scene-flow namespace.)
Leak tracking is a native facility rather than a compiled-out one, so it is present in the shipped build and simply disabled.
Source: `src/Game/Game.SceneFlow/GameManager.cs`, `src/UnityEngine.CoreModule/Unity.Collections/NativeLeakDetection.cs`, `src/UnityEngine.CoreModule/Unity.Collections.LowLevel.Unsafe/UnsafeUtility.cs`.

**So a leaked `TempJob` or `Persistent` allocation produces no warning, no log line and no console output while it stays off.**
Every output shape in this section is read off captured runs rather than from the decompile: the emitter is native, so no string of it exists in C# and a run with the mode on is what confirms the format.
Set the mode to `EnabledWithStackTrace` and a `TempJob` allocation older than four frames logs a leak line per allocation, each with a callstack — which is what separates "something is leaking" from "this is leaking".
A leaked `Persistent` one is never named individually, at runtime or at exit.
What exit produces instead is a single `MemoryLeaks` summary line keyed by memory label — `NativeArray`, `JobScheduler`, `Manager` and the rest, each with a byte total — so a `Persistent` leak tells you that something went and roughly what kind, never where.
That asymmetry decides which allocator to reach for while you are hunting one: a `TempJob` container you deliberately leave undisposed past the threshold will name its own allocation site, and a `Persistent` one will not.

A mod can switch it back on from anywhere, and the boot sequence has long finished by the time a mod loads, so the setting sticks for the session:

```csharp
NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
```

**A leak from ordinary mod code names that code.**
The frames above the allocation are the mod's own methods as Mono JIT frames, and the stack turns native only below the game's own — `(UnityPlayer)`, `(mono-2.0-bdwgc)` and address-only entries.
(VOLATILE: the `MemoryLeaks` summary line, its label roster and the native frame spellings above — a run with the mode on, which is where this output exists and nowhere in C#.)

**Bind it to a condition, and never let it reach a shipped default path.**
Set it from a debug configuration, or behind a mod setting that is off unless the player turns it on.
The reason is that the cost is not paid by the mod that opts in: the mode is a property of the native allocator rather than of the calling assembly, so stack-trace capture lands on every native allocation the game makes and on every other mod in the player's load order.
Source: `src/UnityEngine.CoreModule/Unity.Collections/NativeLeakDetection.cs` and `src/UnityEngine.CoreModule/Unity.Collections.LowLevel.Unsafe/UnsafeUtility.cs` (the mode is a native, process-global setting — both are thin wrappers over `extern`s, which is what makes it one).

## What this reference hands to others

A reader leaves with what a job's memory costs and what its scheduling has to respect: which of the four allocators a container belongs in, the handle discipline that keeps one alive until every reader is done with it, the three throttles, and the fact that on this build almost every mistake in that area is silent.

- `ecs-in-this-game` owns writing the job itself, the chunk geometry a mod sizes its data against and the barrier contract; what travels the other way is what the chunk, the buffer and the barrier cost, and what a mod may not do to a container a job still holds.
- `mod-lifecycle-and-ordering` owns the update-interval mechanism and the system hooks; this reference owns what an interval buys, and that a world spanning the whole session makes `OnCreate` the place to allocate and the load boundary the place to clear.
- `save-serialization` owns the load boundary that same lifetime makes load-bearing.
- `custom-tools` and `placement-definitions` take the reader half of the provider protocol and the register-the-scheduled-handle rule, and `roads-and-traffic` is the heaviest area to query.
- `debug-menu` takes the cost ladder, applied to a widget getter that runs every frame its tab is open.
- `simulation-time-and-units` says what a throttled cadence is worth in simulated time, and `patching` takes from [`burst-at-debug-time.md`](burst-at-debug-time.md) the launch switch that turns Burst off for a session.
- `diagnostics` assumes a live process still writing lines, so the failures this reference produces do not start there: an out-of-bounds native read or a use-after-free is a process death with no managed exception, and "the game crashed and there is no stack trace" belongs here. What `diagnostics` does own for a dead process is the evidence rule — the crashed run's logs are copied before anything is relaunched.
