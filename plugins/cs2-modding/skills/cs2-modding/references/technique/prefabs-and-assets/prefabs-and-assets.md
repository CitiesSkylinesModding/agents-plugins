# Prefabs and assets

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

How to find, edit, clone, synthesise and register the game's data-driven content from code, and how to get your own files in front of the game.

Authoring the content itself — meshes, textures, surfaces, maps, editor scenes — is out of scope; the database calls that _store and retrieve_ that content are in.
`ecs-in-this-game` owns the component model everything below sits on, and `mod-lifecycle-and-ordering` owns which phase a system runs in.

## The word "prefab" names three different things

This is the number-one conceptual trap in the subject, and almost every prefab bug an agent writes is a confusion between two of these three layers.

| Layer | What it is | Lives as |
| --- | --- | --- |
| **Authoring** | the values a designer typed, one managed object per prefab, plus a list of attached components | `PrefabBase` / `ComponentBase`, both `ScriptableObject` |
| **Prefab entity** | the ECS entity the game registers for that prefab, carrying unmanaged `*Data` copies of the above | an `Entity` with `PrefabData` on it |
| **Instance** | one placed building, vehicle, tree or net segment, whose archetype the prefab declared | an `Entity` with `PrefabRef` on it |

**None of the three is Unity's own entity-prefab machinery, whose words this table borrows.**
`Unity.Entities.Prefab`, the engine tag that hides an entity from every query that does not pass `IncludePrefab`, is named nowhere in this game's code, and no vanilla code calls `EntityManager.Instantiate` — so a prefab entity here is queryable like any other, and an instance is built from the archetype the prefab cached rather than copied from the prefab entity.
Source: `src/Game/Game.Objects/ObjectEmergeSystem.cs` and `src/Game/Game.Prefabs/ObjectData.cs` (the cached archetype an instance is created from), against `src/Unity.Entities/Unity.Entities/Prefab.cs` (the engine tag the vocabulary borrows).

One vanilla file shows all three at once.
`Game.Prefabs.DeathcareFacility` is an authoring `ComponentBase` holding `m_HearseCapacity`, `m_StorageCapacity`, `m_ProcessingRate` and `m_LongTermStorage`.
Its `GetPrefabComponents` asks for `DeathcareFacilityData` on the prefab entity, and its `Initialize` copies those four fields into it.
Its `GetArchetypeComponents` puts `Game.Buildings.DeathcareFacility` on every placed instance; when the prefab is not a service upgrade it adds `OwnedVehicle`, `ServiceDispatch` and `ServiceDistrict` beside it — `Efficiency` too on a city-service building — and a nonzero storage capacity adds `Patient`.

So three distinct types share one short name, and they are different in kind rather than merely in namespace:

- `Game.Prefabs.DeathcareFacility` is a `ScriptableObject` implementing no ECS component interface — its base carries the game's own `IComponentBase`, which is not `IComponentData` — so it **cannot appear in a query at all**;
- `Game.Prefabs.DeathcareFacilityData` is the unmanaged authoring copy, on the prefab entity;
- `Game.Buildings.DeathcareFacility` is instance runtime state — `m_TargetRequest`, `m_Flags`, `m_ProcessingState`, `m_LongTermStoredCount` — and holds no copy of an authoring value.

Source: `src/Game/Game.Prefabs/ComponentBase.cs` (the base and the interface it carries), `src/Game/Game.Prefabs/DeathcareFacility.cs`, `src/Game/Game.Prefabs/DeathcareFacilityData.cs`, `src/Game/Game.Buildings/DeathcareFacility.cs`.

**The field name does not survive the copy, which is the trap inside the trap.**
The same quantity is spelled three ways across the layers: authoring `Workplace.m_Workplaces`, prefab-entity `WorkplaceData.m_MaxWorkers`, instance `Game.Companies.WorkProvider.m_MaxWorkers`.
Grep for `m_MaxWorkers` and you find the second and third and never learn the first exists.
Source: `src/Game/Game.Prefabs/Workplace.cs`, `src/Game/Game.Prefabs/WorkplaceData.cs`, `src/Game/Game.Companies/WorkProvider.cs`.

**Which layer an API hands you:**

- `PrefabSystem.TryGetPrefab(...)` and `GetPrefab<T>(...)` return the **authoring** object.
- `PrefabSystem.GetEntity(prefab)` and `TryGetEntity` go the other way, authoring object to **prefab entity**.
- `PrefabRef.m_Prefab` on an instance points at the **prefab entity**, never at the authoring object.
  It carries an implicit conversion to `Entity`, so `prefabRefLookup[instance]` used where an `Entity` is expected silently yields the prefab entity — convenient, and one more way to lose track of which layer you hold.
- `PrefabData.m_Index` on the prefab entity is an index into the prefab system's managed list of authoring objects.
  That one `int` is the entire bridge from ECS back to managed data.
- `ComponentBase.prefab` is the back-pointer from an attached authoring component to the authoring prefab that owns it.

## The archetype declaration hooks, and what each populates

`ComponentBase` declares exactly two abstract members, and nothing else is required of an authoring component:

```csharp
public abstract void GetPrefabComponents(HashSet<ComponentType> components);
public abstract void GetArchetypeComponents(HashSet<ComponentType> components);
```

Four hook families exist across the prefab namespace, each with a different consumer.

**1. `GetPrefabComponents` populates the prefab entity, and the prefab system is its only consumer.**
`AddPrefab` walks every attached authoring component, unions their output, adds `UnlockRequirement` and `Locked` when the prefab is unlockable, adds `Created` and `Updated` unconditionally, and creates the entity once.
`PrefabBase` seeds the set with `PrefabData` and `LoadedIndex`, plus a mod-prerequisite component when the prefab came from an asset carrying a platform id.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`, `src/Game/Game.Prefabs/PrefabBase.cs`.

**2. `GetArchetypeComponents` populates the instance archetype — and nothing consumes it directly.**
`PrefabBase` seeds it with `PrefabRef` alone.
The set is materialised from `LateInitialize` — through a `RefreshArchetype` method on some bases, inline on the others — and each writes the resulting `EntityArchetype` into a prefab-data component — `ObjectPrefab` into `ObjectData.m_Archetype`, and so on.
Every one of them adds `Created` and `Updated` to the archetype unconditionally.
A subclass may override its parent's, and the building override is the instructive one: it adds `InstalledUpgrade`, a sub-net buffer and a sub-route buffer, but only when the prefab entity already carries a `BuildingUpgradeElement` buffer, so the same prefab class yields two different instance archetypes depending on prefab-entity state at refresh time.
Source: `src/Game/Game.Prefabs/PrefabBase.cs` (the `PrefabRef` seed), `src/Game/Game.Prefabs/ObjectPrefab.cs` (a refresh writing `ObjectData.m_Archetype`), `src/Game/Game.Prefabs/BuildingPrefab.cs` (the conditional override).

**One method may write more than one archetype**: the train override writes the object archetype, the stopped archetype and both controller archetypes.
So read the whole of the `RefreshArchetype` that runs for your base rather than stopping at the first write — patch one where it writes several, and the instances carrying the others stay on the unpatched archetype.
Some bases declare no `RefreshArchetype` at all and build their archetypes inline — the net prefab among them — so start from your base's own `LateInitialize` and follow where it goes, rather than searching for the method name.
Source: `src/Game/Game.Prefabs/TrainPrefab.cs` (the four writes), `src/Game/Game.Prefabs/NetPrefab.cs` (an inline builder).

**A prefab may run the hook several times with different seeds**, so the contents of the set at call time carry meaning.
The net prefab builds two, one pre-seeded with `Node` and one with `Edge`, and writes `NetData.m_NodeArchetype` and `NetData.m_EdgeArchetype` — from its `LateInitialize` directly, since it declares no `RefreshArchetype`.
Others run more passes and seed them differently — count them in the method your own base runs.
Some then _read the set they were handed_ and branch — the net one adds `ConnectedEdge` when `Node` is present and `ConnectedNode` when `Edge` is, and the vehicle ones test `Moving`, `Stopped` and `LayoutElement` — while others contribute the same components to every pass, the lane prefab among them.
So `GetArchetypeComponents` is not a pure emit, and adding unconditionally lands your component on every archetype the hook builds — which is what vanilla wants for some of its own and gates for others.
Source: `src/Game/Game.Prefabs/NetPrefab.cs` (the two seeds and the branch that reads them), `src/Game/Game.Prefabs/PublicTransport.cs` (an authoring component testing the vehicle seeds).

**3. `IServiceUpgrade.GetUpgradeComponents` declares what an upgrade contributes to its _host_ building** rather than to the upgrade's own entity.
The workplace component shows the shape: its `GetArchetypeComponents` adds `WorkProvider` and `Employee` only when the component is _not_ a service upgrade, and its `GetUpgradeComponents` adds the same pair whenever the workplace count is non-zero.
Only the service-upgrade system reads this hook.
Source: `src/Game/Game.Prefabs/IServiceUpgrade.cs`, `src/Game/Game.Prefabs/Workplace.cs`, `src/Game/Game.Buildings/ServiceUpgradeSystem.cs`.

**4. `IZoneBuildingComponent` adds a zone-and-level-parameterised pair**, `GetBuildingPrefabComponents(HashSet<ComponentType>, BuildingPrefab, byte level)` and its archetype twin.
The prefab machinery never calls these: the spawnable- and signature-building components call them from inside their own two hooks, forwarding to the zone prefab, which fans out to every `IZoneBuildingComponent` it carries.
**So a growable building's archetype is partly decided by the zone prefab it belongs to and by its level**, not only by its own components — see `zoning-buildings-and-land-value`.
Source: `src/Game/Game.Prefabs/IZoneBuildingComponent.cs`, `src/Game/Game.Prefabs/SpawnableBuilding.cs`, `src/Game/Game.Prefabs/ZonePrefab.cs`.

Overriding either of the first two is the whole mechanism for putting a component of your own on every prefab entity or on every instance, and it needs no system.
Call `base` first, then add — and where the archetype hook runs more than once, test the set you were handed before adding, as the vanilla vehicle components do.
An authoring component whose two overrides are both **empty** is a supported and useful shape: it contributes nothing to the ECS and exists purely as a managed marker you test with `prefab.Has<T>()`, which is exactly what the vanilla obsolete-identifiers component is.
Source: `src/Game/Game.Prefabs/ObsoleteIdentifiers.cs`.

(VOLATILE: which prefab bases build an archetype, how many seeds each runs, and where each writes what it built — each prefab base's own `LateInitialize` and whatever `RefreshArchetype` it calls.)

## What `PrefabSystem` exposes for lookup

The prefab system is created by hand during world creation, before any mod loads, so it always exists.
It holds a handful of managed dictionaries and lists, and every lookup below is a read of one of them.

**By `PrefabID` — string-keyed, and the only total form.**
`TryGetPrefab(PrefabID id, out PrefabBase prefab)` consults the index dictionary and does no cast.
This is the workhorse; write the type half as `nameof(SomePrefabType)` rather than a string literal, so a rename is a compile error rather than a lookup that returns false.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

**By entity, `PrefabData` or `PrefabRef` — and here the generic form returns `true` while handing back `null`.**

```csharp
public bool TryGetPrefab<T>(PrefabData prefabData, out T prefab) where T : PrefabBase
{
    if (prefabData.m_Index >= 0)
    {
        prefab = m_Prefabs[prefabData.m_Index] as T;
        return true;
    }
    prefab = null;
    return false;
}
```

The `as T` cast is unchecked and its result is never tested, so **`true` means "this entity is a live prefab", not "you got a `T`"**.
The entity and `PrefabRef` overloads delegate to this one.
The three single-argument `GetPrefab<T>` overloads share the same unchecked `as T` and return null on a failed cast — but they carry no live-prefab guard at all, so a dead prefab throws out of the index rather than returning null.
**Null-check the out parameter yourself** on every typed lookup — `TryGetPrefab(x, out T p) && p != null` — or wrap it once in an extension method and call only that.
Write that test as `!= null` and not as `is not null`: `PrefabBase` is a `UnityEngine.Object`, whose `==` also reports an object whose native side was destroyed, and the pattern form tests the reference alone and misses it.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs` (the unchecked cast and the missing guard), `src/UnityEngine.CoreModule/UnityEngine/Object.cs` and `src/Game/Game.Prefabs/PrefabBase.cs` (the destroyed-object comparison, and that a prefab is a `UnityEngine.Object`).

**By query singleton.**
`GetSingletonPrefab<T>(EntityQuery)` and `TryGetSingletonPrefab<T>` exist; the `Try` form gates on the query ignoring its filter, so a shared-component filter does not narrow the gate.
That is the same trap `ecs-in-this-game` records for `RequireForUpdate`.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs` (the guard the `Try` form tests), `src/Unity.Entities/Unity.Entities/EntityQueryImpl.cs` (that the property it tests skips the filter `IsEmpty` consults).

**Authoring object to prefab entity.**
`GetEntity(PrefabBase)` is a bare dictionary index and throws for a prefab that was never added or has been removed; `TryGetEntity` is the safe form.
The throwing form is idiomatic immediately after an `AddPrefab` that returned true, and a mistake anywhere else.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs` (the dictionary index, its `TryGetValue` twin, and the remove path that drops the entry).

**Reading the authoring layer once you hold the object.**
`PrefabBase.TryGet<T>` matches the type _or any subclass of it_, and also matches the prefab object itself; `TryGetExactly<T>` matches only the exact type, again including the prefab itself.
`Has<T>` follows the exact rule; `HasSubclassOf` matches strict subclasses only, so unlike `TryGet<T>` it rejects the exact type.
`ComponentBase.GetComponent<T>()` is `prefab.TryGet<T>` and throws when the component has no owning prefab.
Source: `src/Game/Game.Prefabs/PrefabBase.cs` (the subclass and exact-type comparisons, and that each matches the prefab itself first), `src/Game/Game.Prefabs/ComponentBase.cs` (the delegation and the null-prefab throw).

### The authoring layer is the vanilla baseline

Nothing that writes a prefab entity writes back to the authoring object: `Initialize` copies one way and there is no reverse path.
**So the authoring field is the durable original, and the `*Data` value on the prefab entity is whatever the last writer left there.**
A prefab-entity value that matches vanilla may simply be a value no mod has touched yet, which is not the same thing.
Source: `src/Game/Game.Prefabs/ComponentBase.cs` (the hook's signature, which hands out the entity and nothing back), `src/Game/Game.Prefabs/Workplace.cs` (a copy in one direction).

Any mod offering a reversible edit needs this.
Write your change to the prefab entity; restore by reading the authoring object back, never by remembering the value you overwrote — a second toggle would then restore your own first write.

```csharp
if (EntityManager.TryGetComponent(entity, out PlaceableObjectData data)
    && m_PrefabSystem.TryGetPrefab(entity, out PrefabBase prefab)
    && prefab.TryGet(out PlaceableObject authoring))
{
    data.m_ConstructionCost = authoring.m_ConstructionCost;
    EntityManager.SetComponentData(entity, data);
}
```

Reading the authoring layer is not only about baselines.
Some authoring fields have **no counterpart on the prefab entity at all** — the UI object's icon path is one, since the component it emits carries the group entity and the priority and nothing else — so the managed object is the only place that information exists, and the game's own image system reads it from there.
Cataloguing assets rather than reading simulation state reads the authoring object throughout: the UI object, theme object, asset-pack item, content prerequisite, spawn-location and LOD-properties components are where a catalogue's fields are authored.
Source: `src/Game/Game.Prefabs/UIObject.cs`, `src/Game/Game.Prefabs/UIObjectData.cs`, `src/Game/Game.UI/ImageSystem.cs`.

(VOLATILE: the prefab system's member names and signatures, and the authoring-side accessor names on `PrefabBase` — the prefab system's lookup region.)

## What `PrefabSystem` exposes for mutation

**`AddPrefab(PrefabBase, string parentName = null, PrefabBase parentPrefab = null, ComponentBase parentComponent = null)` returns `bool` and swallows every exception.**
It returns false for a null prefab, for an already-registered prefab, for a prefab whose content prerequisite is unavailable, and for any throw, which is caught and logged.
The three optional parameters exist only so the warning names the parent that pulled the prefab in.
**Check the return value.**
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

The availability gate deserves its own sentence: a prefab is available only when both its asset's platform id and any content-prerequisite component resolve to content the player has, and the system mints a content prefab per mod id on demand.
So a prefab declaring a DLC or another mod as a prerequisite **silently does not register** when that content is absent.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

**`RemovePrefab` is a swap-with-last, and it rewrites another prefab's index.**
It tags the entity `Deleted`, unregisters every id the prefab owns, then moves the last prefab in the list into the freed slot and rewrites _that_ prefab's `PrefabData.m_Index` and all of its index entries.
The removed prefab's own index becomes a large negative sentinel.
**A cached `PrefabData` or a cached index is invalidated by any removal anywhere**, not only by removal of the prefab it names — so cache the `Entity`, or re-look-up.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

**`UpdatePrefab(PrefabBase prefab, Entity sourceInstance = default)` does no work**; its whole body records the request in a pending map, and the work happens in the prefab system's next update.
`AddOrUpdatePrefab` picks between add and update on whether the prefab is already registered.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

**`DuplicatePrefab(template, name)` is `Clone`, then `Remove<ObsoleteIdentifiers>()`, then `AddPrefab`** — and the middle step is not optional, for the reason the cloning section gives.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

**The prefab-entity accessors all index the registry directly and throw for an unregistered prefab**: `HasComponent<T>`, `HasEnabledComponent<T>`, `GetComponentData<T>`, `TryGetComponentData<T>`, `GetBuffer<T>`, `TryGetBuffer<T>`, `AddComponentData<T>`, `RemoveComponent<T>`.
The `Try` prefix on two of those refers to the _component_, not to the prefab.
They are conveniences over `EntityManager`, and going through the `EntityManager` with an entity you already hold is equivalent.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

`AddUnlockRequirement(unlocker, unlocked)` is the one domain-specific mutator, appending to the unlock-requirement buffer and warning when either side is not unlockable.

(VOLATILE: the mutator names above and the negative index sentinel `-1000000000` — the prefab system's registry region.)

## The short names that compile on both sides

Comparing the prefab namespace's type names against every other game namespace returns **111 short names declared in both**, and almost every one of those collisions fails loudly.
98 are authoring classes shadowing an ECS component elsewhere — `Hospital`, `School`, `Park`, `FireStation`, `Hearse`, `Resident` and ninety-odd more — where the prefab-namespace type is not a component at all, so naming it in a query is a compile error.
Of the thirteen that are not authoring types, seven fail loudly too: five are enum pairs and two pair a component with a plain struct, and neither kind is interchangeable with its twin.
Source: `src/Game/Game.Prefabs/Hospital.cs` against `src/Game/Game.Buildings/Hospital.cs` (an authoring class shadowing a component), `src/Game/Game.Prefabs/AgeMask.cs` against `src/Game/Game.Tools/AgeMask.cs` (an enum pair).

**The remaining six are the dangerous ones, because both sides compile.**
They are `WaterSourceData`, which is `IComponentData` on both sides, `CompanyInitializeSystem`, which is a system class on both sides and so is a valid type argument either way, and four buffers that exist once on the prefab entity and once on the instance under the same short name:

| Short name | Prefab-entity version (the recipe) | Instance version |
| --- | --- | --- |
| `SubObject` | `m_Prefab`, `m_Flags`, `m_Position`, `m_Rotation`, `m_ParentIndex`, `m_GroupIndex`, `m_Probability` | `Game.Objects.SubObject.m_SubObject`, one entity |
| `SubNet` | `m_Prefab`, `m_Curve`, `m_NodeIndex`, `m_ParentMesh`, `m_InvertMode`, `m_Upgrades`, `m_Snapping` | `Game.Net.SubNet.m_SubNet` |
| `SubLane` | `m_Prefab`, `m_Curve`, `m_NodeIndex`, `m_ParentMesh` | `Game.Net.SubLane.m_SubLane`, `m_PathMethods` |
| `SubArea` | `m_Prefab`, `m_NodeRange` | `Game.Areas.SubArea.m_Area` |

Both members of each pair are `IBufferElementData` and both bind in a query.
Reading the wrong one gives you the list of prefabs a building is _made of_ when you wanted the entities it _has_, or the reverse, with no error anywhere.
**Fully qualify a short name that appears on both sides**, in queries and in lookups alike.
Source: `src/Game/Game.Prefabs/SubObject.cs` against `src/Game/Game.Objects/SubObject.cs`, `src/Game/Game.Prefabs/WaterSourceData.cs` against `src/Game/Game.Simulation/WaterSourceData.cs`.

(VOLATILE: the collision count, the 98-to-13 split and which pairs compile on both sides — the prefab namespace's type declarations, against every other game namespace.)

## What initialises prefab data, and when

Prefab data arrives in two layers inside one frame: the prefab initialize system calls every authoring component's `Initialize` and then its `LateInitialize`, and the other systems of the prefab-update phase derive what authoring cannot state.

**The split between the two hooks is a contract, not a style.**
`Initialize` may only touch its own prefab entity, because other prefabs may not be registered yet; `LateInitialize` may resolve cross-prefab references, because by then they are.
Source: `src/Game/Game.Prefabs/PrefabInitializeSystem.cs` (the two hooks, in the order they run).

**The prefab-update phase is driven once per frame, always.**
The phase is not gated on pending prefab work: **the gating is per system** — most vanilla occupants carry a `RequireForUpdate` on a `Created`-shaped query and the rest test the same query inline — which is why nothing runs on a quiet frame.
A mod system registered into that phase gets an `OnUpdate` every frame and must gate itself the same way.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

[How prefab data is initialised](prefab-data-initialisation.md) carries both layers pass by pass — reach for it when a prefab's numbers come out at zero, or when the value you need is one the game derives rather than one an authoring component states.

## Editing a prefab: what reaches placed buildings, and what does not

The general claim that prefab edits do not reach placed instances is false, and so is its opposite.
**The rule is per-field, and there are three cases.**

**1. A value the simulation reads off the prefab entity each pass changes immediately.**
This is the common case by a wide margin: most simulation jobs hold a `ComponentLookup` and index it with `prefabRef.m_Prefab` inside the loop.
A sweep of the simulation namespace finds 292 such indexed lookups, 210 of them written in exactly that form.
A write to the prefab entity is live on the next pass, for every placed instance, with nothing to trigger.
Source: `src/Game/Game.Simulation/BuildingUpkeepSystem.cs`.

**2. A value copied into an instance component once, at creation, stays stale until something re-runs the copy.**
Worker limits are the canonical one.
The city-service workplace initialize system is what writes `WorkProvider.m_MaxWorkers` from `WorkplaceData`, and its query is two descs: `{CityServiceUpkeep, Created}` excluding `{ServiceUpgrade, Deleted, Temp}`, or `{ServiceUpgrade}` with `Any = {Created, Deleted}` excluding `{Temp, OutsideConnection}`.
Read that query and the remedies fall out of it: **`Created` on the building means "rebuild it", and `Created`-or-`Deleted` on a service upgrade means "add or remove an extension"**.
Note what is absent: `Updated` is in neither desc, so tagging the building `Updated` does not re-run it, and neither does replacing the prefab outright.
Source: `src/Game/Game.Buildings/CityServiceWorkplaceInitializeSystem.cs`.

**3. A change to the instance _archetype_ never reaches an existing instance at all.**
The archetype is fixed when the entity is created from the prefab's cached `ObjectData.m_Archetype`; adding a type to `GetArchetypeComponents` afterwards does nothing to what is already placed.
The one vanilla reconciliation compares an instance's actual archetype against the prefab's current hook output and adds or removes the difference — but it is called from a single site, over a hard-coded set of three types (`Stack`, `MeshColor`, `MeshGroup`), and only when the prefab's mesh changed.
Source: `src/Game/Game.Prefabs/ObjectPrefab.cs` (the cached archetype), `src/Game/Game.Prefabs/ReplacePrefabSystem.cs` (the reconciliation and its one call site).

**The split is not even uniform within one field.**
The work-provider system recomputes `m_MaxWorkers` from `WorkplaceData` every pass _for school buildings_ and folds in installed-upgrade stats, overwrites it on every outside connection with a hard-coded 600 that reads no prefab data at all — while still reading that prefab's `m_Complexity`, so an edit to one field survives and an edit to the other does not — and leaves the field alone for the rest.
So "does my edit reach placed buildings" is answered by reading the systems that touch the field, not by a rule.
Source: `src/Game/Game.Simulation/WorkProviderSystem.cs`.

### The four remedies

1. **Ask for the player action that already triggers the refresh.**
   Rebuilding the building, or adding and removing an extension or upgrade, is exactly what the query above matches, and it is free of side effects because it is the path the game itself uses.
2. **Find the vanilla job that does the copy and run it yourself** when you want, from an options control or a hotkey.
   The cost is finding it; the query shapes above are how you recognise the right one.
3. **Harmony-patch the copy** where no reachable hook exists.
   Cheaper to write than finding and running the vanilla job yourself, and brittle on patch days; `patching` owns the technique.
4. **Tag the prefab entity `Updated`.**
   `EntityManager.AddComponent<Updated>(prefabEntity)` is a one-liner, and it re-derives vehicle capacity, because the vehicle capacity system queries `{VehicleData, PrefabData}` with `Any = {Updated, Deleted}`.
   **Exactly three systems in the prefab namespace query for `Updated` at all** — vehicle capacity, area initialize and unlock — so it is not a general "recompute everything downstream" signal, and reaching for it as one produces a partial refresh that looks like a bug somewhere else.
   Source: `src/Game/Game.Prefabs/VehicleCapacitySystem.cs`, `src/Game/Game.Prefabs/AreaInitializeSystem.cs`, `src/Game/Game.Prefabs/UnlockSystem.cs`.

**Mutating the instance component directly is the remedy of last resort.**
Before reaching for it, grep the field name across the simulation namespace and list every system that writes it; where that list is not empty, one of the four remedies above is the answer instead.
A computed runtime value written from outside is where side effects come from.
Where asking the player to rebuild is acceptable, it is both easier and safe, and every _new_ building is already handled by the prefab-entity edit.

(VOLATILE: the workplace initialize system's two query descs and its `Modification5` registration, the three types the instance-archetype reconciliation covers, the three prefab-namespace systems that consume `Updated`, and the 292/210 lookup counts — the workplace initialize system, the replacement system, and `.m_Prefab]` across the simulation namespace.)

## Editing vanilla prefabs in bulk, and tagging them for your own queries

A prefab entity is an ordinary entity, so a mod edits vanilla prefabs with an ordinary query.

```csharp
// Over the prefab entities themselves, on the main thread.
SystemAPI.QueryBuilder()
    .WithAllRW<PlaceableObjectData>()
    .WithAll<MyPlantTag>()
    .WithNone<Deleted, Overridden>();
```

Walk it with `ToEntityArray(Allocator.Temp)` and write with `EntityManager.SetComponentData`.
The change is live for every subsequent placement, because the placement pipeline reads `PlaceableObjectData` off the prefab entity each time — case 1 above, and the reason a cost edit needs no refresh trigger while a worker-count edit does.

**A component the game _derives_ is the exception, and a one-shot write to one does not survive.**
An initializer re-derives it on every initialisation and every regeneration, so a write from the loading-complete hook is gone with nothing logged; let vanilla derive it and overwrite after, with your system anchored immediately behind the initializer whose output you are correcting — [how prefab data is initialised](prefab-data-initialisation.md) names which components those are and carries the ordering call.

**That tag is the second half of the technique.**
Where the game has no component expressing the set you care about, add one of your own to the prefab entities once and query on it forever after.
Query the vanilla component that identifies them — `{PlantData}` with `None = {Deleted, MyPlantTag}` for plants — add your tag through a command buffer, and put `RequireForUpdate` on that same query so the system stops firing once every matching prefab is tagged.
Such a tag is deliberately _not_ an archetype-hook component: it never reaches an instance, and exists only to make a query cheap.

A bulk edit the player can turn on and off is driven by a control rather than by the phase walk, so it runs from the loading-complete hook for the initial application and directly from the settings page for a mid-session toggle.
That is the arrangement that needs the authoring baseline above: without it, a second toggle restores your own first write.

(VOLATILE: `PlaceableObjectData.m_ConstructionCost` and the `PlantData` component name — both in the prefab namespace.)

## Synthesising a new prefab type

Four steps.

1. **Instantiate.**
   `PrefabBase.Create<T>(string name)` is `ScriptableObject.CreateInstance<T>()` plus a name assignment, and a reflective `Create(Type, string)` exists that throws for a type that is not a `PrefabBase`.
   Calling `ScriptableObject.CreateInstance<T>()` and setting `name` yourself is the same thing written out, and is what most shipped code does.
2. **Set the authoring fields** on the object directly.
3. **Attach authoring components.**
   `AddComponent<T>()` creates a `ScriptableObject` of that type, names it after the type, sets its `prefab` back-pointer, and appends it — and **throws `InvalidOperationException("Component already exists")` when one is already present**, which is why `AddOrGetComponent<T>()` exists.
   `AddComponentFrom(from)` is the copying form: `AddOrGetComponent` on the source's exact type, then a JSON round-trip from the source onto the target.
   A clean idiom is to build a detached authoring component, set its fields, and hand it to `AddComponentFrom`, rather than mutating an attached one in place.
   Source: `src/Game/Game.Prefabs/PrefabBase.cs`.
4. **Register** with `prefabSystem.AddPrefab(prefabBase)`, checked for false.

Derive your class from the closest vanilla prefab base rather than from `PrefabBase` whenever you want vanilla behaviour — a static-object prefab for something placeable, a road prefab for something drivable — because the base is what supplies the archetype refresh and the initialisation hooks.

### When you may do it

**Prefabs are loaded after every mod's `OnLoad`**, batched with a yield between batches, even though the prefab system itself exists long before.
So `OnLoad` is too early to _find_ a vanilla prefab to build on, and a mod that needs one defers.
Source: `src/Game/Game.SceneFlow/GameManager.cs`.

The workable timings, in rough order of how much control they give:

| Timing | Use it when |
| --- | --- |
| A system registered into the prefab-update phase, anchored after the vanilla initializer it depends on | the new prefab's data has to be right by the time the rest of the phase reads it |
| `OnGamePreload` on a system you create but never register into a phase | registration must happen before a game loads and after the asset database is populated |
| `IPreDeserialize` through the pre-deserialize wrapper | the prefab entity must exist before the save's entities are read |
| The game manager's loading-complete event | nothing in the load path depends on the prefab |
| The main-thread dispatcher, from a background import | the prefab is built off-thread and only registration must be on the main thread |

Creating a system without giving it a phase is a real option, not a workaround: `mod-lifecycle-and-ordering` records the same shape under "Not every mod system needs a phase".

**Every one of those paths can run more than once**, and each for its own reason.
The prefab-update phase is driven from a system that updates every frame, so a routine registered there re-runs continuously rather than once.
The load hooks fire once per city load, and a session loads as many cities as the player opens.
A background import completes whenever it completes.
Open the routine with a `TryGetPrefab` on the id you are about to mint and return early when it resolves.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs` (the phase driven from a system that updates every frame).

## Cloning a vanilla prefab, and the reference hygiene a clone needs

**`PrefabBase.Clone(string newName = null)` is the sanctioned route.**
It instantiates the same concrete type, JSON-round-trips the prefab's own fields with the component list and the name override stripped from the payload, names the result `newName ?? name + " (copy)"`, and rebuilds the component list by calling `AddComponentFrom` for each source component.
Two consequences follow from that body, and both matter:

- **The clone's components are fresh objects.** Mutating one cannot reach back into the original's.
- **The clone has no asset**, because `asset` is a property rather than a serialized field and the round trip does not carry it.
  That makes the clone not builtin, not packaged and not read-only, and it makes its id hash invalid, since the hash is computed from the asset.
  The clone's identity is therefore its type's short name and the string you assigned, so **picking a fresh name is not optional**.

Source: `src/Game/Game.Prefabs/PrefabBase.cs` (`Clone`'s body and the `asset` property), `src/Game/Game.Prefabs/PrefabID.cs` (the hash computed from the asset).

**Strip the obsolete identifiers.**
`Clone` copies that component along with the rest, and `AddPrefab` registers every entry in it as an additional index pointing at the clone — or, if the original registered them first, logs a duplicate-id warning and drops them.
So a save referring to the original by an obsolete id resolves to whichever of the two registered first.
`DuplicatePrefab` does the removal for you; a hand-rolled `Clone` must do it itself.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

**Three strategies exist and they are not equivalent.**

- **Clone, then strip.** Shortest, and inherits everything you did not think about.
- **Hand-copy each component.** Instantiate the target type, walk the source's components and `AddComponentFrom` each, then `Remove<T>()` and rebuild by hand the ones that must not be shared in spirit — the UI object above all, so that your prefab gets its own name and icon rather than the original's.
- **Derive selectively.** Create a fresh prefab of the type you want, share only what is genuinely shareable by reference — the mesh array is the usual one — and copy the four or five authoring components you actually need.
  This is the only strategy that cannot inherit something you did not consider, and it is the right default when the result is meant to be a different thing rather than a variant.

**One hygiene rule holds across all three.**
`AddComponentFrom` reads only the JSON of the source component and never its `prefab` field, so **there is never a reason to assign `componentBase.prefab` on a component you are copying _from_**.
Doing so repoints the source's own component at your prefab, leaving the vanilla prefab holding components that believe they belong to you — after which `ComponentBase.GetComponent<T>()` called on any of them resolves against the wrong prefab.
The game detects that condition and logs it, but only from `PrefabBase.OnEnable`, which has already run by then, and nothing re-checks it later.
Source: `src/Game/Game.Prefabs/PrefabBase.cs` (`AddComponentFrom` and the `OnEnable` check), `src/Game/Game.Prefabs/ComponentBase.cs` (the back-pointer `GetComponent<T>` resolves through).

When you copy a UI object across, **null its group** unless you want your prefab to join the original's toolbar group.
Source: `src/Game/Game.Prefabs/UIObject.cs`.

(VOLATILE: `Clone`'s two stripped JSON keys and the `isBuiltin` / `isPackaged` / `isReadOnly` property chain — `PrefabBase` itself.)

## Regenerating a prefab in place, and what that leaves stale

**`UpdatePrefab` does not update anything; it destroys and re-creates.**
A drain on the prefab system's next update tags the old entity `Deleted`, builds a new one and repoints the registry, and a replacement system rewrites every reference in the ECS graph from old to new — **so the prefab entity's identity changes, and that is the whole story of what goes stale.**
A handle taken before the call names a destroyed entity afterwards: hold the `PrefabBase` and re-resolve after each rebuild, and **audit your own managed caches on the same principle**, since any dictionary, list or field of yours keyed on a prefab entity is stale the moment you request a regeneration and nothing in the ECS sweep reaches managed memory.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`, `src/Game/Game.Prefabs/ReplacePrefabSystem.cs`.

**Throttle the requests, and check that the rebuild took.**
Queue them rather than acting on one inline, refuse to drain while a drag is in progress, and hold a cooldown between rebuilds — otherwise a slider's setter regenerates on every intermediate value; and the drain wraps each prefab in its own `try`/`catch` that logs and moves on, so a failed rebuild leaves the registry on the old entity and reports nothing to your caller.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

So **regenerate prefabs you minted and treat a vanilla one as a last resort**, and when you do reach for one, expect the damage to surface somewhere that never mentions prefabs: the game mode that restores service-consumption defaults holds a cache keyed by prefab entity, and after regenerating a city-service building it logs `Cached ServiceUpkeepData not found` against the new entity — in a captured run, during a save, long after the call that caused it.
Source: `src/Game/Game.Prefabs.Modes/ServiceConsumptionGlobalMode.cs` (the cache and the log line).

[Regenerating a prefab in place](regenerating-a-prefab.md) walks the drain step by step — reach for it before regenerating a prefab you did not mint, or when a regeneration leaves something stale you cannot account for.

## Prefab identity, and where the collisions come from

**A `PrefabID` is three fields: `m_Type`, `m_Name`, `m_Hash`.**
Equality compares all three; the hash code deliberately ignores the type.
Three facts about how one is built decide everything else:

- **`m_Type` is `GetType().Name` — the short name, with no namespace.**
  A game `ParcelPrefab` and your own `ParcelPrefab` produce byte-identical ids.
- **`m_Hash` comes from the asset, or is absent.**
  For an asset carrying a platform id the hash is computed from that string, otherwise from the asset guid — and when there is no asset, which is every prefab a mod creates at runtime, the hash stays default.
- The hash only round-trips through a save in new enough formats, so older saves resolve on type and name alone.

Source: `src/Game/Game.Prefabs/PrefabID.cs`.

**A mod-created prefab is therefore identified by nothing but its class's short name and the string you assigned.**
That is the whole namespace of collisions, and `AddPrefab` handles one the worst possible way: it logs a duplicate-id warning and **skips only the index registration**, while still appending the prefab, creating its entity, and returning `true`.
The loser of a collision is a fully live prefab that `TryGetPrefab(PrefabID)` can never find, and the only trace is one warning line.
Two mods that both create a `StaticObjectPrefab` named `"Bench"` produce exactly that.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`, `src/Game/Game.Prefabs/PrefabID.cs`.

**Prefix every prefab name you mint with a token of your own**, and prefer a distinctive class name over a generic one, since the type half of the id is just as collidable as the name half.

**Declaring your prefab class inside the game's own prefab namespace buys nothing for identity.**
Because the id stores only the short type name, the namespace is invisible to lookup and to serialization alike, and no namespace-based lookup exists anywhere.
What the placement does buy is that one `using` resolves your type in your own files; what it costs is that your type now competes for a short name against the twelve hundred-odd types already in that namespace, so a future vanilla type of the same name turns every reference into an ambiguity at compile time.
Source: `src/Game/Game.Prefabs/PrefabID.cs`.

**`ObsoleteIdentifiers` is the rename-migration mechanism.**
It is an authoring component with two empty hooks and one array of identifier entries, and `AddPrefab` registers every entry as an additional index into the same prefab, so a save written against the old name still resolves.
`PrefabBase.MarkCurrentPrefabObsolete()` appends the current identity to that array, and a malformed hash string in an entry throws during id construction.
Add an entry whenever you rename a prefab you have already shipped, or the player's placed copies do not come back.
Source: `src/Game/Game.Prefabs/ObsoleteIdentifiers.cs`, `src/Game/Game.Prefabs/PrefabIdentifierInfo.cs`, `src/Game/Game.Prefabs/PrefabSystem.cs`, `src/Game/Game.Prefabs/PrefabBase.cs`.

### How that identity survives a save

The prefab system writes **a list of `PrefabID`s, not entities**: live prefabs first, then obsolete ones, each guarded by the enabled state of `PrefabData` — the flag `ecs-in-this-game` records as meaning "this prefab still exists".
On load each id is looked up; ids that resolve become index pairs, and ids that do not are kept as obsolete entries keyed by their save index.
Every prefab entity's `LoadedIndex` buffer is then cleared and refilled, so it ends up holding **every save-file index that resolves to that prefab** — which is how a prefab that absorbed two obsolete ids owns several.
Prefabs a save references and the install no longer has get a negative index and a placeholder name for display.
The format itself is `save-serialization`.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

(VOLATILE: the `PrefabID` field set and the format tag gating its hash, and the `ObsoleteIdentifiers` / `PrefabIdentifierInfo` member names — the prefab id type, and the prefab system's serialization region.)

## Getting content and files in front of the game

`Colossal.IO.AssetDatabase` is the layer under the prefab system, and it is how content gets in: the game's own prefab-loading step is a loop over the database's prefab assets, handing each loaded object to `AddPrefab`.
The frontend reaches a mod's own files — the icons it ships, the thumbnails it generates — through `coui://<host>/<path>`, where a host is a name mapped to one or more directories on disk that the mod registers.
[Assets and resource hosts](assets-and-resource-hosts.md) carries the database's read and write surfaces, locating your own mod's directory, registering a database of your own, host registration and resolution, and the thumbnail chain a prefab with no icon falls back to.

## What this reference hands to others

`zoning-buildings-and-land-value` needs the most from here, because growable buildings are the one place where the instance archetype is decided by a _second_ prefab: the spawnable- and signature-building components forward both hooks to the zone prefab, parameterised by building level.
It also owns the worked example of the stale-instance problem, `WorkplaceData.m_MaxWorkers` against `WorkProvider.m_MaxWorkers`, and the building prefab's conditional archetype, whose three extra types appear only when the prefab entity already carries an upgrade-element buffer.

`roads-and-traffic` needs the twin-archetype case, since a network prefab produces two instance archetypes from one component set, and it needs the prefab-side and net-side `SubNet` and `SubLane` pairs more than any other area — a network prefab's sub-net buffer is curves and prefab references while the instance's is spawned entities, and both bind in a query.
A mod letting the player build networks at runtime lands on the regeneration section whole.

`transportation-and-vehicles` needs the one place where tagging a prefab entity `Updated` genuinely re-derives something, and the two vehicle-specific archetype refresh overrides, since a vehicle's instance archetype is not built by the plain object path.

`placement-definitions` is the seam where a prefab becomes an instance: a creation definition holds prefab **entities**, and the archetype the resulting instance gets is the one cached on that prefab entity.
Everything a definition-rewriting mod does depends on knowing the definition carries the middle layer, not the authoring object and not the placed entity, and `PlaceableObjectData` and `ObjectGeometryData` are the two prefab-entity components that pipeline reads.

`mod-lifecycle-and-ordering` owns when a system runs; the registration timings above are choices within what it establishes.
`save-serialization` owns the save format that the identity section touches.
`frontend-and-injection` owns the frontend that consumes the host locations.
`patching` owns the "**Harmony-patch the copy**" remedy.
