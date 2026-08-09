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

[`data-providers.md`](data-providers.md) enumerates the vanilla systems that publish data through the reader/writer handle protocol.
[`colossal-collections.md`](colossal-collections.md) describes the game's own container library, which a fork of a vanilla job meets immediately.

## The world outlives every city, so `OnDestroy` is a process-exit hook

One ECS world is created during the boot sequence, before any city exists, and destroyed only when the application goes away.
Loading a city, returning to the main menu, loading another city and quitting again all happen inside that same world, with the same system instances.
(VOLATILE: the world creation and destruction call sites — `GameManager` in the scene-flow namespace.)

Three consequences a mod designs around:

- **`OnCreate` runs once per process and `OnDestroy` runs once per process.**
  An `Allocator.Persistent` container allocated in `OnCreate` and disposed in `OnDestroy` is correct and costs one allocation for the whole session.
  A container allocated per loaded city and disposed in `OnDestroy` leaks once per city the player opens, and the player sees only memory climbing across a long session.
- **Per-city state is cleared, not recreated.**
  The vanilla spatial indices show the shape: they allocate their trees in `OnCreate`, and in the pre-deserialize hook they complete their outstanding handles and call `Clear()` rather than disposing and reallocating.
- **`OnDestroy` is not where you observe a leak.**
  By the time it runs the process is exiting and nothing reports what was still allocated.
  The load-boundary hooks are where a mod can see its own accumulation.

`mod-lifecycle-and-ordering` owns the hooks themselves; `save-serialization` owns the load boundary this makes load-bearing.

## Managed against unmanaged, and where a mod's own data sits

Four places, and only one is garbage-collected.

**ECS component data is unmanaged, always.**
The game declares no managed components, and every component in it is a struct in chunk memory.
A native container refuses a managed element type outright, and even that refusal is a compiled-out check.

**Chunk memory is unmanaged and owned by the entity store.**
A mod declaring a component pays for it in chunk space and pays nothing to manage it.
What that costs per entity is `ecs-in-this-game`'s chunk geometry.

**Native containers a mod allocates are unmanaged, untracked and invisible.**
A `NativeArray<T>` here is three fields — a raw buffer pointer, a length and an allocator label — and allocates through the native tracked-malloc path.
Nothing about it is reachable from the GC, so a leaked one never appears in a managed heap dump, and for the reason below it appears nowhere else either.

**A mod's managed objects are collected normally, with one permanent exception.**
Systems, the `IMod` implementation, settings objects and anything hanging off them are ordinary managed objects.
Prefabs are not: a prefab derives from `ScriptableObject`, and the prefab system holds every registered prefab in a list and in dictionaries keyed by the object itself.
A prefab a mod synthesises is therefore rooted for as long as it stays registered, and the game's own unused-asset sweeps cannot collect it while it is.
Unregistering it through the prefab system's own removal method is what releases those roots, and that method is not bookkeeping: it marks the prefab's entity deleted and reindexes another prefab's, so it is a structural change against a live city rather than a memory-hygiene call.

**Where an entity needs a managed thing, the game stores an integer and not a reference.**
The shared component carries an index; a managed system owns the table it indexes into.
Copy that shape before reaching for the cleanup-component pattern below, which is the heavier answer and exists for the case where the entity genuinely owns the resource's lifetime.

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

**The fourth is the one to reach for when gathering query results to feed a job.**
It is a double-buffered rewindable allocator, reached as `base.World.UpdateAllocator.ToAllocator`, and the rewind swaps between two buffers so the one being reset is never the one in use.
Memory handed to a job scheduled this frame is therefore still valid when that job runs, and no `Dispose` is needed or wanted:

```csharp
NativeList<Entity> entities = m_Query.ToEntityListAsync(
    base.World.UpdateAllocator.ToAllocator, out JobHandle outJobHandle);
```

**The list comes back empty**: the gather runs as a scheduled job, and `outJobHandle` is what says it has finished.
Combine that handle into every schedule that reads the list, as the canonical shape below does — dropping it and iterating the list is a read of a buffer a worker thread is still filling.

Two further constraints on it.
The rewind happens only on a frame where the world actually ticks, so on a frame the world is not updating the allocator is not rewound and its contents persist.
And `Dispose(JobHandle)` on a `NativeArray` from a custom allocator throws `InvalidOperationException` — one of the very few mistakes in this area that announces itself.
`NativeList<T>` carries no such guard: its `Dispose(JobHandle)` frees through the list's own allocator handle, and a rewindable allocator's free path returns the memory to nobody, so disposing an update-allocator-backed list against a handle is not a double free — which is why the vanilla rendering systems ship doing exactly that.

(VOLATILE: the allocator enum's members and the custom-allocator index threshold — the `Allocator` enum and `AllocatorManager` in the collections package.)

## Disposing a container a job may still be reading

Two mechanisms, and the type forces the choice rather than taste deciding it.

**`Dispose(JobHandle)` is the default and it does not block.**
It builds a dispose job carrying the raw buffer pointer and the allocator label, schedules it on the handle you pass, and returns a new handle.
The free happens on a worker thread after the reading job finishes.
Every stock container has this overload.

**Most of the game's own containers do not have it**, and for those the discipline is complete-then-dispose; [`colossal-collections.md`](colossal-collections.md) names the two that do.
`Complete()` is a main-thread stall until the job finishes, which is acceptable at teardown because teardown happens once per process — and it is the only option those types leave you.
Complete **every** outstanding handle before disposing: the vanilla object search system makes four `Complete()` calls to guard two `Dispose()` calls, one per reader and writer handle per tree.

Vanilla is not consistent here, and the inconsistency is worth knowing before you copy one.
Only the object search system completes; the other five dispose their trees bare in `OnDestroy`.
They get away with it because the process is exiting, which is not a property a mod should lean on — complete first, and treat the five bare teardowns as something to fix in a fork rather than to reproduce.

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
2. **Assign `base.Dependency` after scheduling.**
   Nothing downstream waits for a job whose handle was never published.
3. **Register with every provider whose data you took**, using the handle you got back from scheduling.
   The protocol is below.
4. **Register with the barrier if you wrote through its command buffer.**
   `AddJobHandleForProducer`, and the contract is `ecs-in-this-game`'s.
5. **Complete only where the type forces it.**
   Every `Complete()` is a main-thread stall, and the legitimate places are teardown and a genuine main-thread readback.
6. **Do not carry a handle across simulation iterations.**
   A simulation phase runs up to eight times in one frame, and a system the interval gate lets run has `base.Dependency` reset to `default` immediately before its own `Update`, on every iteration whose index its interval is at or below — so a handle you published last iteration is no longer chained to anything.
   Anything that must outlive an iteration is held in your own field, not read back out of `base.Dependency`.

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

### Consuming a provider: four steps, in order

1. **Take the data with the right `readOnly` flag.**
   `true` if your job only reads it; `false` if it writes.
   The flag decides which handle you get back: `true` returns the owner's write handle alone, `false` returns the readers' and the writer's combined.
   That is why a writing job must take it `false` — taken `true` it never waits for the other readers, and writes into a tree they are still walking.
2. **Combine the returned handle into your schedule**, alongside `base.Dependency` and every other provider's handle.
3. **Register the handle you got back from `Schedule`** with _every_ provider you took from.
   Passing back the handle the provider gave you is a no-op — the owner already waited for it — and the owner's next write then does not wait for your job, so the container is mutated while your job walks it.
   Registering with only some of the providers you took from has the same effect on the ones you skipped.
4. **Complete before disposing anything that has no asynchronous dispose**, per the containers named above.

`custom-tools` and `placement-definitions` both consume vanilla search trees during a raycast or a snap, and `roads-and-traffic` is the heaviest area to query.

### A mod-owned spatial index

`NativeQuadTree<TItem, TBounds>` is the game's own, and **the item type is a parameter rather than always `Entity`**.
A job field or an iterator written against a provider has to match that provider's pair, not a single assumed one; [`data-providers.md`](data-providers.md) tables them per system.
Its constructor takes a minimum item size and an allocator; every vanilla tree passes `1f`.
Its surface is `Add`/`TryAdd`, `Update`/`TryUpdate`, `AddOrUpdate`, `Remove`/`TryRemove`, `Get`/`TryGet`, `Clear`, four `Iterate` overloads and `Select`, driven by the iterator and selector interfaces beside it.

**Four of the non-`Try` forms throw a bare `System.Exception`, and those throws are not conditional** — unlike almost everything else in this collections stack, they will fire in a player's game.
`Update`, `Remove` and `Get` throw on a **miss**; `Add` throws on a **hit**, when the item is already in the tree.
`AddOrUpdate` has no hit-or-miss guard at all, which is what makes it the form to reach for on a re-registration.
Use the `Try` forms unless the outcome is genuinely impossible.

What a mod-owned index needs, beyond the container:

- **Allocation in `OnCreate` from `Allocator.Persistent`**, because the world spans the session.
- **A `Clear()` from the pre-deserialize hook**, not a dispose-and-reallocate, so the tree survives city changes.
  Take the tree with `readOnly: false`, complete the handle it returns, and only then clear — the vanilla systems all complete before clearing, and clearing under a running reader is an unsynchronised structural mutation with no diagnostic.
- **The three-method protocol above**, with the writer combining rather than assigning.
- **Completion of every reader and writer handle in `OnDestroy` before `Dispose()`**, because `NativeQuadTree` has no `Dispose(JobHandle)`.
  Skipping that completion is a use-after-free with no diagnostic rather than an error.

(VOLATILE: the vanilla accessor names — `GetStaticSearchTree`, `AddStaticSearchTreeReader` and their siblings across the search systems in the objects, net, zones, areas, routes and effects namespaces.)

## The cleanup-component pattern, for a component owning a managed resource

Native containers are the easy half of disposal.
The hard half is an entity that owns a `Material`, a `Mesh` or any other managed object with an engine resource behind it: `Dispose(JobHandle)` is no help, because the thing to free is not in a container and freeing it is a main-thread call.

The engine's answer is `ICleanupComponentData`, and all it does is delay the entity's destruction.

**What the entity store does with one:**

1. Any archetype containing a cleanup component is flagged.
2. Each such archetype gets a **residue archetype**, built as the entity plus an internal marker plus every cleanup component and nothing else.
3. `DestroyEntity` **moves** the entity into that residue archetype instead of freeing it.
   After the destroy, the entity handle is still live and still carries your cleanup component; every ordinary component is gone.
4. The entity is freed for real when the last cleanup component is removed.

Removing one is a deliberate `RemoveComponent`, and that is the point.
The engine's guard against stripping a cleanup component through `SetArchetype` is compiled out of this build, so `SetArchetype` drops one silently instead of throwing: the entity carries on without it, the resource is orphaned, and the eventual `DestroyEntity` frees the entity outright.
So the contract is: **the entity outlives its own destruction until you say otherwise, and if you never say otherwise it never goes away.**

**The component holds a `GCHandle`, not a reference.**
A `GCHandle` is an unmanaged 8-byte token, so the struct stays blittable and lives in a chunk, while the managed object is reachable through `Target`.
The handle kind is the design decision:

- **Strong** (`GCHandle.Alloc(obj)`) when the component owns the object's lifetime.
  Its `Dispose()` then does two frees, and they are different things: destroy the engine resource, _then_ `Free()` the handle.
  Skipping the second leaks the handle-table entry and roots the object for the life of the process.
- **Weak** (`GCHandle.Alloc(obj, GCHandleType.Weak)`) when a cache owns the object and the component is only a reference.
  The target can then be collected out from under you, so every use re-checks `Target` for null and rebuilds when it has gone, and `Dispose()` is just `Free()`.

Both cases guard on `IsAllocated` and end by resetting the field to `default`.
`Free()` throws `InvalidOperationException` on a handle that was never allocated or already freed, and so does `Target` — one such element aborts the whole drain below and abandons every handle still queued behind it.
A component added at entity creation and populated on first use carries an unallocated handle until then, which is the ordinary way an entity reaches disposal with one.

**The disposal system, and why it is shaped the way it is.**
Register it in the `Cleanup` phase: that phase runs after the whole main loop, so the modification phases, the tools, the UI and rendering have all already run and nothing will ask for the resource again this frame.

- **Query for the doomed entities as "has the cleanup component, lacks the ordinary component that always accompanies it"** — which is exactly the residue archetype's shape, and is what makes an entity somebody else destroyed reachable at all.
- **Gate with `RequireAnyForUpdate` on that query**, so `OnUpdate` is never entered while nothing is pending.
  An update interval will not help you here: only the three simulation phases consult one, and `Cleanup` is not among them.
- **The job enqueues; it does not dispose.**
  `GameObject.Destroy` needs the main thread.
  Read each doomed entity through a lookup, `Enqueue` the component _value_ into a `NativeQueue<T>`, and issue `RemoveComponent<T>` plus `DestroyEntity` into an `EndFrameBarrier` command buffer.
  **`EndFrameBarrier` is the one to use, and the choice is forced**: no barrier belongs to the `Cleanup` phase, so the ordinary rule of writing to your own phase's barrier has nothing to name here, and a phase-local barrier taken from `Cleanup` throws.
  Issuing both the removal and the destroy is correct rather than redundant — the removal is what frees an entity already sitting in residue, and the destroy is what kills one still live; on an entity the removal already freed, the destroy is a silent no-op.
  Enqueuing the value is what makes this work: the handle travels out of the job as plain unmanaged data, so the main thread can still reach the managed object after the component is gone.
- **`OnUpdate` completes the job and drains the queue on the main thread**, calling `Dispose()` per element.
  That `Complete()` is a stall and is unavoidable; the job's work is the lookup and the command-buffer recording, which is what keeps the disposal itself off the worker thread.
  If you schedule it parallel, the queue needs its `ParallelWriter` and so does the command buffer — on this build neither omission throws.

**Neither guard catches the handle free.**
With `[BurstCompile]` on the job the build fails at the read of the handle's `Target`: fetching the managed object is an unsupported call and it is the one a real body reaches first, with `Object.Destroy` unsupported behind it.
Take the attribute off and it builds, and the destroy throws on the worker thread instead — `Destroy can only be called from the main thread.`
Taking it off is the only route to that second case: a body that will not compile has no artifact for the launch switch below to unburst.
`Free()` is rejected by neither.

**Two failure modes, neither of them visible:**

- **The component is an ordinary `IComponentData` instead of a cleanup one.**
  `DestroyEntity` deallocates the chunk data immediately, the `GCHandle` goes with it and is never freed.
  Strong: a permanent managed root plus an undestroyed engine resource.
  Weak: a leaked handle-table slot.
  This is not native-container leakage, so even the leak-detection switch below would not see it.
- **Nothing ever removes the cleanup component.**
  The entity stays in the residue archetype forever — still live, still occupying a chunk slot, still matching any query over the cleanup component alone.
  The symptom is entity and chunk counts that climb and never fall, which reads as an entity leak rather than a resource one.

The game declares no cleanup components of its own, so there is no vanilla example to read this against; the engine half above is read from the entity store rather than inferred.
**A residue entity survives a save and a load**, and that is the case to design for.
The clear that empties the world before a load selects on a fixed list of vanilla components, and `DestroyEntity` has already stripped every one of them, so a residue entity matches nothing and is never destroyed — it persists into whatever city loads next, handle still live.
Your disposal system does still match it there, so the teardown is delayed rather than lost.
A cleanup component that is itself serializable is written into the save on top of that, because the save query gains every serializable component declared outside the game assembly.
The load then deserializes a fresh copy beside the residue that already survived, carrying whatever your own `Serialize` wrote where a live handle used to be.
So keep the cleanup component out of the save entirely: declare neither `ISerializable` nor `IEmptySerializable` on it, and nothing of it is ever written.
`IEmptySerializable` is the one to watch — it looks like a tag marker rather than a serialization interface, and it is what `ecs-in-this-game` teaches for a tag that must survive a save.
`save-serialization` owns both queries and the asymmetry between them.
(VOLATILE: the interface name and the cleanup-flag logic — `ICleanupComponentData` is the renamed form of `ISystemStateComponentData` and the obsolete spelling survives as an upgrade alias; both live in the entities package's component-type and entity-component-store types.)

## Throttling: run less often, and enter `OnUpdate` less often

Three mechanisms, in increasing order of what they let you skip.

**The update interval skips the whole update.**
Your `GetUpdateInterval` override is called **once, at registration**, and the value is cached; the dispatch loop then tests the cached mask _before_ `Update()` is called, so a skipped frame costs one bitwise-and and one comparison, and the query gate, `OnUpdate`, the handle refresh and the schedule are all skipped whole.
Because the override runs once, an interval computed from mutable state — a settings slider, a load-time flag — is frozen at whatever it returned during registration, and nothing reports that it never changes again.
The mechanism, its power-of-two requirement and the three phases that honour it belong to `mod-lifecycle-and-ordering`.

- **Only the simulation phases consult it.**
  An interval on a system registered anywhere else is dead code that reads like a throttle, and the game itself ships one such system.
  Check the phase before you write the override.
- **Let the offset be auto-assigned.**
  The default is negative, which the dispatcher reads as "spread me": it hands out offsets so that systems sharing an interval land on different frames instead of piling onto one.
  Do not predict which frame you land on relative to another system — the assignment order is not something to build a producer/consumer handoff on.
  Returning an explicit offset opts out of that.

The shape to copy is an interval **plus** a relevance gate — a system feeding an on-screen panel returns a large interval and opens `OnUpdate` with an early return while the panel is hidden.

**`RequireForUpdate` / `RequireAnyForUpdate` skip `OnUpdate` entirely** when a required query is empty.
The check is a length read on a cached chunk list: no chunk walk, no dependency sync.
It tests the query **ignoring your filter**, so a query narrowed with `SetSharedComponentFilter` still gates on the unfiltered set — a filter is not a throttle.
`IsEmptyIgnoreFilter` is the same free check spelled by hand, and is what to use inside `OnUpdate` when one system schedules several independent jobs each needing its own gate.

**The chunk-level early exit inside the job body is the finest-grained form**, and is what the simulation actually runs on: one shared-component read rejects a whole chunk's worth of entities.
A per-entity job cannot express it — that is one of the three things holding the chunk buys you, per `ecs-in-this-game` — so a per-entity job wanting the same throttle pushes the test into the query as a shared-component filter before scheduling instead.
`ecs-in-this-game`'s bucketing sibling owns which index and which count to filter on — guessing either runs the job at the wrong cadence or on nothing, with nothing logged; what that cadence is worth in simulated time is `simulation-time-and-units`.

## Burst: what it costs at debug time, and how to gate it

**The post-processor runs on every build and Burst-compiles for Windows, macOS and Linux.**
It is invoked from an after-build target with no configuration condition — Debug and Release alike, with a debug flag passed in both — and there is no toolchain switch that turns the pass off.
So Burst output ships as three native libraries beside the managed assembly, and is produced whether or not the mod has any Burst jobs.
The one thing that skips it is the publisher's update command, which turns off the toolchain's own targets — the output clear, the post-processing pass and the deploy — while the ordinary compile still runs, so an update can ship a freshly compiled assembly beside native libraries left from the previous build.
`cs2-mod-project` owns the build pipeline itself.

**The cost is that a Burst-compiled job cannot be stepped.**
The managed body is still in the assembly and a breakpoint in it still binds; the native compilation runs instead, so the breakpoint never fires.

**Reach for the runtime switch first.**
Burst compilation is disabled at launch, with no rebuild and no change to the mod:

```
--burst-disable-compilation
```

or the environment variable `UNITY_BURST_DISABLE_COMPILATION`, set to anything other than empty or `0`.
The managed body is always in the assembly, so it runs instead of the native one.
It is read from the game process's own command line, which is the route to reach for; the environment variable is the fallback for a launcher that will not pass an argument through.

**Reach for a compile-time gate only if you will do this often enough that a launch argument becomes tiresome**, and then treat it as the more dangerous of the two.
The form is `[BurstCompile]` wrapped in `#if`, with the symbol defined in the Release configuration only:

```csharp
#if USE_BURST
[BurstCompile]
#endif
private partial struct MyJob : IJobEntity { }
```

The hazard is plain C#: **a preprocessor symbol defined nowhere produces no warning, no error and a build indistinguishable from a working one.**
The `#if` compiles, the attribute vanishes, and the mod ships unbursted with nothing to tell you.
If you write one, verify the symbol reaches the compiler in the configuration you meant.

(VOLATILE: the `[BurstCompile]` attribute's spelling, the launch argument and the environment variable name — the Burst package's attribute declarations and `BurstCompilerOptions`.)

## A log call is a synchronous file write, so logging is a per-frame cost like any other

**A message that passes the level filter opens the log file, writes, flushes, and closes it again**, under a lock.
Nothing batches and nothing buffers across messages: the stream is held open only if the logger's `keepStreamOpen` is set, which defaults off and which nothing in the game turns on.
So a log call that gets written is not a cheap side effect that disappears in a release build — it is a file-system round trip on the calling thread, serialised against every other logger in the process.

That makes the cost scale with the thing a mod is tempted to log from.
Once per load is free.
Once per frame is a file open and close every frame.
Once per entity in a system's update is a file open and close per entity per frame, which is enough to be felt on its own — before counting the string that had to be built to pass to it.

**The level filter is the cheap gate, and it runs first.**
Every level-specific method tests the logger's `effectivenessLevel` before doing any of the above, so a line below the shipped level is neither formatted nor written — though its arguments are evaluated before the test runs.
That is the first lever to reach for.

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
  One method per category, each with its own symbol, gives per-category logging that costs a release build nothing.
  An undefined symbol is as silent here as it is on the Burst gate above, and it resolves differently: `[Conditional]` is applied where the call is compiled, not where the method is declared.
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

The synchronous forms are not forbidden, and the game's own load and editor paths use them heavily; the rule is that each one is a stall, so a system calling `ToEntityArray` every frame stalls every frame.
`debug-menu` inherits this directly: a widget getter runs every frame its tab is open, so a getter doing a count is rung 3 and one doing a `ToEntityArray` is rung 4.

**The failure mode here is a stall, not a leak, and the diagnosis differs.**
A leaked container costs memory and shows nothing.
A per-frame `ToEntityArray` costs frame time and shows up under the mod's own system name in a profile.

### Structural changes on the main thread complete every job in the world

This is the heaviest sync point available and it is one line of mod code.
Every `EntityManager` method marked `[StructuralChangeMethod]` — adding a component, removing one, creating or destroying an entity — routes through a call that completes **all** jobs, not just the ones touching your components.
`EntityManager.SetComponentData` and the read-write data path are cheaper but not free: each completes the read and write dependency for that one type.

**A barrier's command buffer is the alternative, and it costs the mod nothing to own.**
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

## The game's three forced garbage collections

The game runs a full unused-asset sweep plus `GC.Collect()` around the load boundary and nowhere else: immediately before serializing a save, immediately before deserializing one, and immediately after loading finishes.
There is no per-frame collection, so managed allocation in a hot path is the mod's own problem and nothing will hide it.

The useful corollary: a mod's preload and loading-complete hooks run adjacent to a full blocking collection, already inside the frame the player experiences as a load-time hitch.
That makes them the cheap place to do expensive managed setup — and the wrong place to add more than you need.

## The one leak instrument this game has, and its condition

Native leak detection is **switched off**, from a method in the boot sequence whose name says the opposite.
(VOLATILE: the method name and its call site — `GameManager` in the scene-flow namespace.)
Leak tracking is a native facility rather than a compiled-out one, so it is present in the shipped build and simply disabled.

**So a leaked `TempJob` or `Persistent` allocation produces no warning, no log line and no console output while it stays off.**
Turn it on and a `TempJob` allocation older than four frames logs a leak line per allocation, each with a callstack.
A leaked `Persistent` one is never named individually, at runtime or at exit.
What exit produces instead is a single `MemoryLeaks` summary line keyed by memory label — `NativeArray`, `JobScheduler`, `Manager` and the rest, each with a byte total — so a `Persistent` leak tells you that something went and roughly what kind, never where.
That asymmetry decides which allocator to reach for while you are hunting one: a `TempJob` container you deliberately leave undisposed for a frame will name its own allocation site, and a `Persistent` one will not.

A mod can switch it back on from anywhere, and the boot sequence has long finished by the time a mod loads, so the setting sticks for the session:

```csharp
NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
```

`EnabledWithStackTrace` prints a callstack per leaked allocation, which is what separates "something is leaking" from "this is leaking".
**A leak from ordinary mod code names that code.**
The frames above the allocation are the mod's own methods as Mono JIT frames, and the stack turns native only below the game's own — `(UnityPlayer)`, `(mono-2.0-bdwgc)` and address-only entries.

**Bind it to a condition, and never let it reach a shipped default path.**
Set it from a debug configuration, or behind a mod setting that is off unless the player turns it on.
The reason is that the cost is not paid by the mod that opts in: the mode is a property of the native allocator rather than of the calling assembly, so stack-trace capture lands on every native allocation the game makes and on every other mod in the player's load order.

The shape of the failures the rest of this reference produces belongs here rather than to a log-reading pass: an out-of-bounds native read or a use-after-free is a process death with no managed exception, so "the game crashed and there is no stack trace" starts in this reference and not in `diagnostics`, whose order assumes a live process still writing lines.
What `diagnostics` does own for a dead process is the evidence rule: the crashed run's logs are copied before anything is relaunched.
