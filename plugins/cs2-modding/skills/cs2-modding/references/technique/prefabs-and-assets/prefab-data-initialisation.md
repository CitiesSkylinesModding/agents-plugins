# How prefab data is initialised

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

The two layers that fill a prefab entity's data once it exists, the order they run in, and where a system of your own fits among them.
The prefab layer above is [prefabs and assets](prefabs-and-assets.md).

## Layer one: the managed per-component pass

The prefab initialize system queries `{Created, PrefabData}` and runs three passes over the batch:

1. collect every newly created prefab entity and its authoring object;
2. call `ComponentBase.Initialize(EntityManager, Entity)` on every attached component, then `GetDependencies` — **any dependency prefab not already registered is added and initialised in the same pass**, through a queue drained until empty, which is how a prefab pulls its referenced prefabs in without anyone registering them;
3. call `LateInitialize` on every component, then hand the accumulated dependency list to the unlockable base.

Source: `src/Game/Game.Prefabs/PrefabInitializeSystem.cs`.

**The split between the two hooks is a contract, not a style.**
`Initialize` may only touch its own prefab entity, because other prefabs may not be registered yet.
`LateInitialize` may resolve cross-prefab references, because by then they are — which is also why the initialisation-time `RefreshArchetype` calls run from `LateInitialize`, the archetype being able to name components another prefab's hooks contributed.
Source: `src/Game/Game.Prefabs/PrefabInitializeSystem.cs` (the two hooks, in the order they run), `src/Game/Game.Prefabs/ObjectPrefab.cs` (a refresh running from `LateInitialize`).

Both passes wrap each component in a try/catch that logs and continues.
**A component whose `Initialize` throws leaves its `*Data` component present at default values, with one log line and no other symptom** — a prefab whose numbers are all zero is this failure until proven otherwise.
Source: `src/Game/Game.Prefabs/PrefabInitializeSystem.cs`.

## Layer two: the derived-data systems

**Layer two is the `*InitializeSystem` family, and it computes what authoring cannot state.**
The prefab-update phase carries 23 vanilla systems, in this order: texture streaming, geometry asset loading, **prefab initialize**, mesh, animated prefab, UI initialize, terrain initialize, net initialize, object initialize, zone, area initialize, company initialize, resource, zone prefab initialize, building initialize, lot initialize, route initialize, infoview initialize, vehicle initialize, effect initialize, vehicle capacity, notification icon prefab, trigger prefab.
The managed pass is third, so **every derived-data system runs after `Initialize` and `LateInitialize`, in the same frame**.
Source: `src/Game/Game.Common/SystemOrder.cs`.

These are ordinary ECS systems reading prefab-entity components and writing derived ones.
The object initialize system gates on `{PrefabData, ObjectData}` with `Any = {Created, Deleted}` and derives `ObjectGeometryData` — size, bounds and some twenty `GeometryFlags` bits — from the prefab's meshes and its other components.
Nothing an authoring component wrote is recomputed; what is computed is everything depending on geometry or on another prefab.
Source: `src/Game/Game.Prefabs/ObjectInitializeSystem.cs`.

## What drives the phase

The prefab system's own update calls the pending-update drain, then drives the phase unconditionally, then finalises replacements only if something was actually replaced.
It registers no `RequireForUpdate` and never disables itself, which is why the phase runs on every frame and each occupant gates itself.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

## Correcting what vanilla derived

**The readable pattern for correcting derived prefab data is to let vanilla derive it and then overwrite**, with your system anchored immediately after the vanilla initializer whose output you are correcting:

```csharp
updateSystem.UpdateAfter<MyParcelInitializeSystem, ObjectInitializeSystem>(
    SystemUpdatePhase.PrefabUpdate);
```

Build the query in the vanilla shape — `WithAll<PrefabData, Created>()` plus read-write access to what you write — gate on it, and finish by adding `Updated` to the query so the systems that consume that tag see the change.
Anchoring, and the silence that follows a wrong phase argument, belong to `mod-lifecycle-and-ordering`.

(VOLATILE: the 23-system prefab-update list and its order, and the `GeometryFlags` member names — the vanilla system-order class, and the object initialize system.)
