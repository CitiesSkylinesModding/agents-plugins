# The cleanup-component pattern, for a component owning a managed resource

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

Native containers are the easy half of disposal.
The hard half is an entity that owns a `Material`, a `Mesh` or any other managed object with an engine resource behind it: `Dispose(JobHandle)` is no help, because the thing to free is not in a container and freeing it is a main-thread call.

The engine's answer is `ICleanupComponentData`, and all it does is delay the entity's destruction.

**What the entity store does with one:**

1. Any archetype containing a cleanup component is flagged.
2. Each such archetype gets a **residue archetype**, built as the entity plus an internal marker plus every cleanup component and nothing else.
3. `DestroyEntity` **moves** the entity into that residue archetype instead of freeing it.
   After the destroy, the entity handle is still live and still carries your cleanup component; every ordinary component is gone.
4. The entity is freed for real when the last cleanup component is removed.

Source: `src/Unity.Entities/Unity.Entities/EntityComponentStore.cs`, `src/Unity.Entities/Unity.Entities/CleanupEntity.cs`.

Removing one is a deliberate `RemoveComponent`, and that is the point.
The engine's guard against stripping a cleanup component through `SetArchetype` is compiled out of this build, so `SetArchetype` drops one silently instead of throwing: the entity carries on without it, the resource is orphaned, and the eventual `DestroyEntity` frees the entity outright.
So the contract is: **the entity outlives its own destruction until you say otherwise, and if you never say otherwise it never goes away.**
Source: `src/Unity.Entities/Unity.Entities/EntityComponentStore.cs` (the compiled-out guard), `src/Unity.Entities/Unity.Entities/EntityManager.cs` (`SetArchetype` reaching the archetype move without it).

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
Register it in the `Cleanup` phase: that phase runs after the whole main loop, so the modification phases, the tools, the UI and the render submission have all already run.
**It is not the last phase of the frame**, though — `LateUpdate` follows it, and that is where the simulation and the render completion are driven from — so what `Cleanup` buys is that nothing in `MainLoop` will ask for the resource again, not that nothing will.
Source: `src/Game/Game.SceneFlow/GameManager.cs` (the phase drive order), `src/Game/Game.Common/SystemOrder.cs` (what sits in `LateUpdate`).

- **Query for the doomed entities as "has the cleanup component, lacks the ordinary component that always accompanies it"** — which is exactly the residue archetype's shape, and is what makes an entity somebody else destroyed reachable at all.
- **Gate with `RequireAnyForUpdate` on that query**, so `OnUpdate` is never entered while nothing is pending.
  An update interval will not help you here: only the three simulation phases consult one, and `Cleanup` is not among them.
  Source: `src/Game/Game/UpdateSystem.cs`, `src/Game/Game.Simulation/SimulationSystem.cs`.
- **The job enqueues; it does not dispose.**
  `GameObject.Destroy` needs the main thread.
  Read each doomed entity through a lookup, `Enqueue` the component _value_ into a `NativeQueue<T>`, and issue `RemoveComponent<T>` plus `DestroyEntity` into an `EndFrameBarrier` command buffer.
  **`EndFrameBarrier` is the one to use, and the choice is forced**: no barrier belongs to the `Cleanup` phase, so the ordinary rule of writing to your own phase's barrier has nothing to name here, and a phase-local barrier taken from `Cleanup` throws.
  Issuing both the removal and the destroy is correct rather than redundant — the removal is what frees an entity already sitting in residue, and the destroy is what kills one still live; on an entity the removal already freed, the destroy is a silent no-op.
  Enqueuing the value is what makes this work: the handle travels out of the job as plain unmanaged data, so the main thread can still reach the managed object after the component is gone.
  Source: `src/Game/Game.Common/SystemOrder.cs` (every barrier's phase), `src/Game/Game/SafeCommandBufferSystem.cs` (the throw once a barrier's window is shut).
- **`OnUpdate` completes the job and drains the queue on the main thread**, calling `Dispose()` per element.
  That `Complete()` is the genuine main-thread readback the handle discipline allows, and the find belongs in a parallel schedule — a sequential `Schedule` completed immediately is the `Run` shape [`performance-and-memory.md`](performance-and-memory.md)'s schedule-form rule retires, and `Run` itself would put the whole find on the main thread.
  If you schedule it parallel, the queue needs its `ParallelWriter` and so does the command buffer — on this build neither omission throws.

**Neither guard catches the handle free.**
With `[BurstCompile]` on the job the build fails at the read of the handle's `Target`: fetching the managed object is an unsupported call and it is the one a real body reaches first, with `Object.Destroy` unsupported behind it.
Take the attribute off and it builds, and the destroy throws on the worker thread instead — `Destroy can only be called from the main thread.`
Taking it off is the only route to that second case: a body that will not compile has no artifact for the launch switch in [`burst-at-debug-time.md`](burst-at-debug-time.md) to unburst.
`Free()` is rejected by neither.

**Two failure modes, neither of them visible:**

- **The component is an ordinary `IComponentData` instead of a cleanup one.**
  `DestroyEntity` deallocates the chunk data immediately, the `GCHandle` goes with it and is never freed.
  Strong: a permanent managed root plus an undestroyed engine resource.
  Weak: a leaked handle-table slot.
  This is not native-container leakage, so even the leak-detection switch [`performance-and-memory.md`](performance-and-memory.md) names would not see it.
  Source: `src/Unity.Entities/Unity.Entities/EntityComponentStore.cs`.
- **Nothing ever removes the cleanup component.**
  The entity stays in the residue archetype forever — still live, still occupying a chunk slot, still matching any query over the cleanup component alone.
  The symptom is entity and chunk counts that climb and never fall, which reads as an entity leak rather than a resource one.
  Source: `src/Unity.Entities/Unity.Entities/EntityComponentStore.cs`.

The game declares no cleanup components of its own, so there is no vanilla example to read this against; the engine half above is read from the entity store rather than inferred.
**A residue entity survives a save and a load**, and that is the case to design for.
The clear that empties the world before a load selects on a fixed list of vanilla components, and `DestroyEntity` has already stripped every one of them, so a residue entity matches nothing and is never destroyed — it persists into whatever city loads next, handle still live.
Your disposal system does still match it there, so the teardown is delayed rather than lost.
A cleanup component that is itself serializable is written into the save on top of that, because the save query gains every serializable component declared outside the game assembly.
The load then deserializes a fresh copy beside the residue that already survived, carrying whatever your own `Serialize` wrote where a live handle used to be.
So keep the cleanup component out of the save entirely: declare neither `ISerializable` nor `IEmptySerializable` on it, and nothing of it is ever written.
`IEmptySerializable` is the one to watch — it looks like a tag marker rather than a serialization interface, and it is what [`ecs-in-this-game`](../ecs-in-this-game/ecs-in-this-game.md) teaches for a tag that must survive a save.
[`save-serialization`](../save-serialization/save-serialization.md) owns both queries and the asymmetry between them.
Source: `src/Game/Game.Serialization/ClearSystem.cs` (the clear's fixed list), `src/Game/Game.Serialization/SerializerSystem.cs`, `src/Colossal.Core/Colossal.Serialization.Entities/ComponentSerializerLibrary.cs` (the save query's union over components declared outside the game assembly).
(VOLATILE: the interface name and the cleanup-flag logic — `ICleanupComponentData` is the renamed form of `ISystemStateComponentData` and the obsolete spelling survives as an upgrade alias; both live in the entities package's component-type and entity-component-store types.)
