# Prefabs and assets

Verified against game version 1.6.0f1.

How to find, edit, clone, synthesise and register the game's data-driven content from code, and how to get your own files in front of the game.

Authoring the content itself — meshes, textures, surfaces, maps, editor scenes — is out of scope; the database calls that _store and retrieve_ that content are in.
`ecs-in-this-game` owns the component model everything below sits on, and `mod-lifecycle-and-ordering` owns which phase a system runs in.

## The word "prefab" names three different things

This is the number-one conceptual trap in the subject, and almost every prefab bug an agent writes is a confusion between two of these three layers.

| Layer             | What it is                                                                                        | Lives as                                                |
| ----------------- | ------------------------------------------------------------------------------------------------- | ------------------------------------------------------- |
| **Authoring**     | the values a designer typed, one managed object per prefab, plus a list of attached components    | `PrefabBase` / `ComponentBase`, both `ScriptableObject` |
| **Prefab entity** | the ECS entity the game registers for that prefab, carrying blittable `*Data` copies of the above | an `Entity` with `PrefabData` on it                     |
| **Instance**      | one placed building, vehicle, tree or net segment, whose archetype the prefab declared            | an `Entity` with `PrefabRef` on it                      |

One vanilla file shows all three at once.
`Game.Prefabs.DeathcareFacility` is an authoring `ComponentBase` holding `m_HearseCapacity`, `m_StorageCapacity`, `m_ProcessingRate` and `m_LongTermStorage`.
Its `GetPrefabComponents` asks for `DeathcareFacilityData` on the prefab entity, and its `Initialize` copies those four fields into it.
Its `GetArchetypeComponents` puts `Game.Buildings.DeathcareFacility` on every placed instance, alongside `Efficiency`, `OwnedVehicle`, `ServiceDispatch` and `ServiceDistrict`.

So three distinct types share one short name, and they are different in kind rather than merely in namespace:

- `Game.Prefabs.DeathcareFacility` is a managed object and **cannot appear in a query at all**;
- `Game.Prefabs.DeathcareFacilityData` is the blittable authoring copy, on the prefab entity;
- `Game.Buildings.DeathcareFacility` is instance runtime state — `m_TargetRequest`, `m_Flags`, `m_ProcessingState`, `m_LongTermStoredCount` — and holds no copy of an authoring value.

**The field name does not survive the copy, which is the trap inside the trap.**
The same quantity is spelled three ways across the layers: authoring `Workplace.m_Workplaces`, prefab-entity `WorkplaceData.m_MaxWorkers`, instance `Game.Companies.WorkProvider.m_MaxWorkers`.
Grep for `m_MaxWorkers` and you find the second and third and never learn the first exists.

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

**1. `GetPrefabComponents` populates the prefab entity, and the prefab system's `AddPrefab` is its only consumer.**
`AddPrefab` walks every attached authoring component, unions their output, adds `UnlockRequirement` and `Locked` when the prefab is unlockable, adds `Created` and `Updated` unconditionally, and creates the entity once.
`PrefabBase` seeds the set with `PrefabData` and `LoadedIndex`, plus a mod-prerequisite component when the prefab came from an asset carrying a platform id.

**2. `GetArchetypeComponents` populates the instance archetype — and nothing consumes it directly.**
`PrefabBase` seeds it with `PrefabRef` alone.
The set is materialised by a `RefreshArchetype` method called from `LateInitialize`, and **six independent `RefreshArchetype` families exist, each writing the resulting `EntityArchetype` into a different prefab-data component**:

| Prefab base class        | Destination                        |
| ------------------------ | ---------------------------------- |
| `ArchetypePrefab`        | `ArchetypeData.m_Archetype`        |
| `ObjectPrefab`           | `ObjectData.m_Archetype`           |
| `BrushPrefab`            | `BrushData.m_Archetype`            |
| `ChirpPrefab`            | `ChirpData.m_Archetype`            |
| `EventPrefab`            | `EventData.m_Archetype`            |
| `NotificationIconPrefab` | `NotificationIconData.m_Archetype` |

All six derive straight from `PrefabBase` and all six add `Created` and `Updated` to the archetype unconditionally.
Three subclasses override the object one — the building, moving-object and train prefabs.
The building override is the instructive one: it adds `InstalledUpgrade`, a sub-net buffer and a sub-route buffer, but only when the prefab entity already carries a `BuildingUpgradeElement` buffer, so the same prefab class yields two different instance archetypes depending on prefab-entity state at refresh time.

**The net prefab is the exception worth knowing, because it calls the hook twice with different seeds.**
Its `LateInitialize` builds two sets, one pre-seeded with `Node` and one with `Edge`, runs every component's `GetArchetypeComponents` against both, and writes `NetData.m_NodeArchetype` and `NetData.m_EdgeArchetype`.
Its own override then _reads the set it was handed_ and branches, adding `ConnectedEdge` when `Node` is present and `ConnectedNode` when `Edge` is.
So `GetArchetypeComponents` is not a pure emit: the contents of the set at call time carry meaning, and an override may be invoked more than once per prefab with different contents.

**3. `IServiceUpgrade.GetUpgradeComponents` declares what an upgrade contributes to its _host_ building** rather than to the upgrade's own entity.
The workplace component shows the shape: its `GetArchetypeComponents` adds `WorkProvider` and `Employee` only when the component is _not_ a service upgrade, and its `GetUpgradeComponents` adds the same pair whenever the workplace count is non-zero.
Only the service-upgrade system reads this hook.

**4. `IZoneBuildingComponent` adds a zone-and-level-parameterised pair**, `GetBuildingPrefabComponents(HashSet<ComponentType>, BuildingPrefab, byte level)` and its archetype twin.
The prefab machinery never calls these: the spawnable- and signature-building components call them from inside their own two hooks, forwarding to the zone prefab, which fans out to every `IZoneBuildingComponent` it carries.
**So a growable building's archetype is partly decided by the zone prefab it belongs to and by its level**, not only by its own components — see `zoning-buildings-and-land-value`.

Overriding either of the first two is the whole mechanism for putting a component of your own on every prefab entity or on every instance, and it needs no system.
Call `base` first, then add.
An authoring component whose two overrides are both **empty** is a supported and useful shape: it contributes nothing to the ECS and exists purely as a managed marker you test with `prefab.Has<T>()`, which is exactly what the vanilla obsolete-identifiers component is.

(VOLATILE: the six `RefreshArchetype` destinations and their component names, and `NetData.m_NodeArchetype` / `m_EdgeArchetype` — the prefab namespace, where `void Get[A-Za-z]*Components\(HashSet<ComponentType>` finds them.)

## What `PrefabSystem` exposes for lookup

The prefab system is created by hand during world creation, before any mod loads, so it always exists.
It holds a handful of managed dictionaries and lists, and every lookup below is a read of one of them.

**By `PrefabID` — string-keyed, and the only total form.**
`TryGetPrefab(PrefabID id, out PrefabBase prefab)` consults the index dictionary and does no cast.
This is the workhorse; write the type half as `nameof(SomePrefabType)` rather than a string literal, so a rename is a compile error rather than a lookup that returns false.

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
**Null-check the out parameter yourself** on every typed lookup — `TryGetPrefab(x, out T p) && p is not null` — or wrap it once in an extension method and call only that.

**By query singleton.**
`GetSingletonPrefab<T>(EntityQuery)` and `TryGetSingletonPrefab<T>` exist; the `Try` form gates on the query ignoring its filter, so a shared-component filter does not narrow the gate.
That is the same trap `ecs-in-this-game` records for `RequireForUpdate`.

**Authoring object to prefab entity.**
`GetEntity(PrefabBase)` is a bare dictionary index and throws for a prefab that was never added or has been removed; `TryGetEntity` is the safe form.
The throwing form is idiomatic immediately after an `AddPrefab` that returned true, and a mistake anywhere else.

**Reading the authoring layer once you hold the object.**
`PrefabBase.TryGet<T>` matches the type _or any subclass of it_, and also matches the prefab object itself; `TryGetExactly<T>` matches only the exact type, again including the prefab itself.
`Has<T>` follows the exact rule, `HasSubclassOf` follows the subclass rule.
`ComponentBase.GetComponent<T>()` is `prefab.TryGet<T>` and throws when the component has no owning prefab.

### The authoring layer is the vanilla baseline

Nothing that writes a prefab entity writes back to the authoring object: `Initialize` copies one way and there is no reverse path.
**So the authoring field is the durable original, and the `*Data` value on the prefab entity is whatever the last writer left there.**
A prefab-entity value that matches vanilla may simply be a value no mod has touched yet, which is not the same thing.

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
Some authoring components have **no `*Data` counterpart at all** — the colour-properties component that declares which colour channels may be modified externally is one — so the managed object is the only place that information exists.
Anything cataloguing assets rather than reading simulation state lives there too: the UI object, theme object, asset-pack item, content prerequisite, spawn-location and LOD-properties components are all authoring-only.

(VOLATILE: the prefab system's member names and signatures, and the authoring-side accessor names on `PrefabBase` — the prefab system's lookup region.)

## What `PrefabSystem` exposes for mutation

**`AddPrefab(PrefabBase, string parentName = null, PrefabBase parentPrefab = null, ComponentBase parentComponent = null)` returns `bool` and swallows every exception.**
It returns false for a null prefab, for an already-registered prefab, for a prefab whose content prerequisite is unavailable, and for any throw, which is caught and logged.
The three optional parameters exist only so the warning names the parent that pulled the prefab in.
**Check the return value.**

The availability gate deserves its own sentence: a prefab is available only when both its asset's platform id and any content-prerequisite component resolve to content the player has, and the system mints a content prefab per mod id on demand.
So a prefab declaring a DLC or another mod as a prerequisite **silently does not register** when that content is absent.

**`RemovePrefab` is a swap-with-last, and it rewrites another prefab's index.**
It tags the entity `Deleted`, unregisters every id the prefab owns, then moves the last prefab in the list into the freed slot and rewrites _that_ prefab's `PrefabData.m_Index` and all of its index entries.
The removed prefab's own index becomes a large negative sentinel.
**A cached `PrefabData` or a cached index is invalidated by any removal anywhere**, not only by removal of the prefab it names — so cache the `Entity`, or re-look-up.

**`UpdatePrefab(PrefabBase prefab, Entity sourceInstance = default)` does no work**; its whole body records the request in a pending map, and the work happens in the prefab system's next update.
`AddOrUpdatePrefab` picks between add and update on whether the prefab is already registered.

**`DuplicatePrefab(template, name)` is `Clone`, then `Remove<ObsoleteIdentifiers>()`, then `AddPrefab`** — and the middle step is not optional, for the reason the cloning section gives.

**The prefab-entity accessors all index the registry directly and throw for an unregistered prefab**: `HasComponent<T>`, `HasEnabledComponent<T>`, `GetComponentData<T>`, `TryGetComponentData<T>`, `GetBuffer<T>`, `TryGetBuffer<T>`, `AddComponentData<T>`, `RemoveComponent<T>`.
The `Try` prefix on two of those refers to the _component_, not to the prefab.
They are conveniences over `EntityManager`, and going through the `EntityManager` with an entity you already hold is equivalent.

`AddUnlockRequirement(unlocker, unlocked)` is the one domain-specific mutator, appending to the unlock-requirement buffer and warning when either side is not unlockable.

(VOLATILE: the mutator names above and the negative index sentinel `-1000000000` — the prefab system's registry region.)

## The short names that compile on both sides

Comparing the prefab namespace's type names against every other game namespace returns **111 short names declared in both**, and almost every one of those collisions fails loudly.
98 are authoring classes shadowing an ECS component elsewhere — `Hospital`, `School`, `Park`, `FireStation`, `Hearse`, `Resident` and ninety-odd more — where the prefab-namespace type is not a component at all, so naming it in a query is a compile error.
Of the thirteen that are not authoring types, seven fail loudly too: five are enum pairs and two pair a component with a plain struct, and neither kind is interchangeable with its twin.

**The remaining six are the dangerous ones, because both sides compile.**
They are `WaterSourceData`, which is `IComponentData` on both sides, `CompanyInitializeSystem`, which is a system class on both sides and so is a valid type argument either way, and four buffers that exist once on the prefab entity and once on the instance under the same short name:

| Short name  | Prefab-entity version (the recipe)                                                 | Instance version                                 |
| ----------- | ---------------------------------------------------------------------------------- | ------------------------------------------------ |
| `SubObject` | `m_Prefab`, `m_Flags`, `m_Position`, `m_Rotation`, `m_ParentIndex`, `m_GroupIndex` | `Game.Objects.SubObject.m_SubObject`, one entity |
| `SubNet`    | `m_Prefab`, `m_Curve`, `m_NodeIndex`, `m_ParentMesh`, `m_InvertMode`, `m_Upgrades` | `Game.Net.SubNet.m_SubNet`                       |
| `SubLane`   | `m_Prefab`, `m_Curve`, `m_NodeIndex`, `m_ParentMesh`                               | `Game.Net.SubLane.m_SubLane`, `m_PathMethods`    |
| `SubArea`   | `m_Prefab`, `m_NodeRange`                                                          | `Game.Areas.SubArea.m_Area`                      |

Both members of each pair are `IBufferElementData` and both bind in a query.
Reading the wrong one gives you the list of prefabs a building is _made of_ when you wanted the entities it _has_, or the reverse, with no error anywhere.
**Fully qualify a short name that appears on both sides**, in queries and in lookups alike.

(VOLATILE: the collision count, the 98-to-13 split and which pairs compile on both sides — the prefab namespace's type declarations, against every other game namespace.)

## What initialises prefab data, and when

**Layer one is managed and per-component.**
The prefab initialize system queries `{Created, PrefabData}` and runs three passes over the batch:

1. collect every newly created prefab entity and its authoring object;
2. call `ComponentBase.Initialize(EntityManager, Entity)` on every attached component, then `GetDependencies` — **any dependency prefab not already registered is added and initialised in the same pass**, through a queue drained until empty, which is how a prefab pulls its referenced prefabs in without anyone registering them;
3. call `LateInitialize` on every component, then hand the accumulated dependency list to the unlockable base.

**The split between the two hooks is a contract, not a style.**
`Initialize` may only touch its own prefab entity, because other prefabs may not be registered yet.
`LateInitialize` may resolve cross-prefab references, because by then they are — which is also why every `RefreshArchetype` runs from `LateInitialize`, the archetype being able to name components another prefab's hooks contributed.

Both passes wrap each component in a try/catch that logs and continues.
**A component whose `Initialize` throws leaves its `*Data` component present at default values, with one log line and no other symptom** — a prefab whose numbers are all zero is this failure until proven otherwise.

**Layer two is the `*InitializeSystem` family, and it computes what authoring cannot state.**
The prefab-update phase carries 23 vanilla systems, in this order: texture streaming, geometry asset loading, **prefab initialize**, mesh, animated prefab, UI initialize, terrain initialize, net initialize, object initialize, zone, area initialize, company initialize, resource, zone prefab initialize, building initialize, lot initialize, route initialize, infoview initialize, vehicle initialize, effect initialize, vehicle capacity, notification icon prefab, trigger prefab.
The managed pass is third, so **every derived-data system runs after `Initialize` and `LateInitialize`, in the same frame**.

These are ordinary ECS systems reading prefab-entity components and writing derived ones.
The object initialize system gates on `{PrefabData, ObjectData}` with `Any = {Created, Deleted}` and derives `ObjectGeometryData` — size, bounds and some twenty `GeometryFlags` bits — from the prefab's meshes and its other components.
Nothing an authoring component wrote is recomputed; what is computed is everything depending on geometry or on another prefab.

**The prefab-update phase is driven once per frame, always.**
The prefab system's own update calls the pending-update drain, then drives the phase unconditionally, then finalises replacements only if something was actually replaced.
It registers no `RequireForUpdate` and never disables itself.
So the phase is not gated on pending prefab work: **the gating is per system**, each vanilla occupant carrying its own `RequireForUpdate` on a `Created`-shaped query, which is why nothing runs on a quiet frame.
A mod system registered into that phase gets an `OnUpdate` every frame and must gate itself the same way.

**The readable pattern for correcting derived prefab data is to let vanilla derive it and then overwrite**, with your system anchored immediately after the vanilla initializer whose output you are correcting:

```csharp
updateSystem.UpdateAfter<MyParcelInitializeSystem, ObjectInitializeSystem>(
    SystemUpdatePhase.PrefabUpdate);
```

Build the query in the vanilla shape — `WithAll<PrefabData, Created>()` plus read-write access to what you write — gate on it, and finish by adding `Updated` to the query so the systems that consume that tag see the change.
Anchoring, and the silence that follows a wrong phase argument, belong to `mod-lifecycle-and-ordering`.

(VOLATILE: the 23-system prefab-update list and its order, and the `GeometryFlags` member names — the vanilla system-order class, and the object initialize system.)

## Editing a prefab: what reaches placed buildings, and what does not

The general claim that prefab edits do not reach placed instances is false, and so is its opposite.
**The rule is per-field, and there are three cases.**

**1. A value the simulation reads off the prefab entity each pass changes immediately.**
This is the common case by a wide margin: most simulation jobs hold a `ComponentLookup` and index it with `prefabRef.m_Prefab` inside the loop.
A sweep of the simulation namespace finds 292 such indexed lookups, 210 of them written in exactly that form.
A write to the prefab entity is live on the next pass, for every placed instance, with nothing to trigger.

**2. A value copied into an instance component once, at creation, stays stale until something re-runs the copy.**
Worker limits are the canonical one.
The city-service workplace initialize system is what writes `WorkProvider.m_MaxWorkers` from `WorkplaceData`, and its query is two descs: `{CityServiceUpkeep, Created}` excluding `{ServiceUpgrade, Deleted, Temp}`, or `{ServiceUpgrade}` with `Any = {Created, Deleted}` excluding `{Temp, OutsideConnection}`.
Read that query and the remedies fall out of it: **`Created` on the building means "rebuild it", and `Created`-or-`Deleted` on a service upgrade means "add or remove an extension"**.
Note what is absent: `Updated` is in neither desc, so tagging the building `Updated` does not re-run it, and neither does replacing the prefab outright.

**3. A change to the instance _archetype_ never reaches an existing instance at all.**
The archetype is fixed when the entity is created from the prefab's cached `ObjectData.m_Archetype`; adding a type to `GetArchetypeComponents` afterwards does nothing to what is already placed.
The one vanilla reconciliation compares an instance's actual archetype against the prefab's current hook output and adds or removes the difference — but it is called from a single site, over a hard-coded set of three types (`Stack`, `MeshColor`, `MeshGroup`), and only when the prefab's mesh changed.

**The split is not even uniform within one field.**
The work-provider system recomputes `m_MaxWorkers` from `WorkplaceData` every pass _for school buildings_ and folds in installed-upgrade stats, while leaving the field alone for every other building kind.
So "does my edit reach placed buildings" is answered by reading the systems that touch the field, not by a rule.

### The four remedies

1. **Ask for the player action that already triggers the refresh.**
   Rebuilding the building, or adding and removing an extension or upgrade, is exactly what the query above matches, and it is free of side effects because it is the path the game itself uses.
2. **Find the vanilla job that does the copy and run it yourself** when you want, from an options control or a hotkey.
   The cost is finding it; the query shapes above are how you recognise the right one.
3. **Harmony-patch the copy** where no reachable hook exists.
   Cheaper to write than remedy 2 and brittle on patch days; `patching` owns the technique.
4. **Tag the prefab entity `Updated`.**
   `EntityManager.AddComponent<Updated>(prefabEntity)` is a one-liner, and it re-derives vehicle capacity, because the vehicle capacity system queries `{VehicleData, PrefabData}` with `Any = {Updated, Deleted}`.
   **Exactly three systems in the prefab namespace query for `Updated` at all** — vehicle capacity, area initialize and unlock — so it is not a general "recompute everything downstream" signal, and reaching for it as one produces a partial refresh that looks like a bug somewhere else.

**Mutating the instance component directly is the remedy of last resort.**
Before reaching for it, grep the field name across the simulation namespace and list every system that writes it; where that list is not empty, one of the four remedies above is the answer instead.
A computed runtime value written from outside is where side effects come from.
Where asking the player to rebuild is acceptable, it is both easier and safe, and every _new_ building is already handled by the prefab-entity edit.

(VOLATILE: the workplace initialize system's two query descs and its `Modification5` registration, the three prefab-namespace systems that consume `Updated`, and the 292/210 lookup counts — the workplace initialize system, and `.m_Prefab]` across the simulation namespace.)

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
4. **Register** with `prefabSystem.AddPrefab(prefabBase)`, checked for false.

Derive your class from the closest vanilla prefab base rather than from `PrefabBase` whenever you want vanilla behaviour — a static-object prefab for something placeable, a road prefab for something drivable — because the base is what supplies the archetype refresh and the initialisation hooks.

### When you may do it

**Prefabs are loaded after every mod's `OnLoad`**, batched with a yield between batches, even though the prefab system itself exists long before.
So `OnLoad` is too early to _find_ a vanilla prefab to build on, and a mod that needs one defers.
The workable timings, in rough order of how much control they give:

| Timing                                                                                                 | Use it when                                                                            |
| ------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------- |
| A system registered into the prefab-update phase, anchored after the vanilla initializer it depends on | the new prefab's data has to be right by the time the rest of the phase reads it       |
| `OnGamePreload` on a system you create but never register into a phase                                 | registration must happen before a game loads and after the asset database is populated |
| `IPreDeserialize` through the pre-deserialize wrapper                                                  | the prefab entity must exist before the save's entities are read                       |
| The game manager's loading-complete event                                                              | nothing in the load path depends on the prefab                                         |
| The main-thread dispatcher, from a background import                                                   | the prefab is built off-thread and only registration must be on the main thread        |

Creating a system without giving it a phase is a real option, not a workaround: `mod-lifecycle-and-ordering` records the same shape under "Not every mod system needs a phase".

**Every one of those paths can run more than once**, and each for its own reason.
The prefab-update phase is driven from a system that updates every frame, so a routine registered there re-runs continuously rather than once.
The load hooks fire once per city load, and a session loads as many cities as the player opens.
A background import completes whenever it completes.
Open the routine with a `TryGetPrefab` on the id you are about to mint and return early when it resolves.

## Cloning a vanilla prefab, and the reference hygiene a clone needs

**`PrefabBase.Clone(string newName = null)` is the sanctioned route.**
It instantiates the same concrete type, JSON-round-trips the prefab's own fields with the component list and the name override stripped from the payload, names the result `newName ?? name + " (copy)"`, and rebuilds the component list by calling `AddComponentFrom` for each source component.
Two consequences follow from that body, and both matter:

- **The clone's components are fresh objects.** Mutating one cannot reach back into the original's.
- **The clone has no asset**, because `asset` is a property rather than a serialized field and the round trip does not carry it.
  That makes the clone not builtin, not packaged and not read-only, and it makes its id hash invalid, since the hash is computed from the asset.
  The clone's identity is therefore its type's short name and the string you assigned, so **picking a fresh name is not optional**.

**Strip the obsolete identifiers.**
`Clone` copies that component along with the rest, and `AddPrefab` registers every entry in it as an additional index pointing at the clone — or, if the original registered them first, logs a duplicate-id warning and drops them.
So a save referring to the original by an obsolete id resolves to whichever of the two registered first.
`DuplicatePrefab` does the removal for you; a hand-rolled `Clone` must do it itself.

**Three strategies exist and they are not equivalent.**

- **Clone, then strip.** Shortest, and inherits everything you did not think about.
- **Hand-copy each component.** Instantiate the target type, walk the source's components and `AddComponentFrom` each, then `Remove<T>()` and rebuild by hand the ones that must not be shared in spirit — the UI object above all, so that your prefab gets its own name and icon rather than the original's.
- **Derive selectively.** Create a fresh prefab of the type you want, share only what is genuinely shareable by reference — the mesh array is the usual one — and copy the four or five authoring components you actually need.
  This is the only strategy that cannot inherit something you did not consider, and it is the right default when the result is meant to be a different thing rather than a variant.

**One hygiene rule holds across all three.**
`AddComponentFrom` reads only the JSON of the source component and never its `prefab` field, so **there is never a reason to assign `componentBase.prefab` on a component you are copying _from_**.
Doing so repoints the source's own component at your prefab, leaving the vanilla prefab holding components that believe they belong to you — after which `ComponentBase.GetComponent<T>()` called on any of them resolves against the wrong prefab.
The game detects that condition and logs it, but only from `PrefabBase.OnEnable`, which has already run by then, and nothing re-checks it later.

When you copy a UI object across, **null its group** unless you want your prefab to join the original's toolbar group.

(VOLATILE: `Clone`'s two stripped JSON keys and the `isBuiltin` / `isPackaged` / `isReadOnly` property chain — `PrefabBase` itself.)

## Regenerating a prefab in place, and what that leaves stale

**`UpdatePrefab` does not update anything; it destroys and re-creates.**
The pending map is drained at the top of the prefab system's update, and for each entry it:

1. tags the **old prefab entity** `Deleted`;
2. rebuilds the prefab-component set from scratch by re-running every attached component's `GetPrefabComponents`, and creates a **brand-new entity**;
3. copies the old `PrefabData`, so the new entity keeps the same index and the managed list needs no change;
4. carries the unlocked state across;
5. repoints the registry at the new entity;
6. calls the replacement system with the old and new entities.

Each iteration is individually try/caught, logged and skipped on failure.

**The prefab entity's identity changes, and that is the whole story of what goes stale.**
The ECS side is covered thoroughly: the replacement system queries a 14-way `Any` over `PrefabRef`, `SubObject`, `SubNet`, `SubArea`, `PlaceholderObjectElement`, `ServiceUpgradeBuilding`, `BuildingUpgradeElement`, `Effect`, `ActivityLocationElement`, `CharacterElement`, `SubMesh`, `LodMesh`, `UIGroupElement` and `TutorialPhaseRef`, rewrites every `m_Prefab` field from old to new, tags each touched instance `Updated`, then tags any entity whose `PrefabRef` points at a touched instance so owners see their children change.
Upgrade-element buffers are copied by hand, system-held references are patched through a dedicated pass, and mesh batches are rebuilt when the prefab carried mesh data.

**What that sweep cannot reach is any `Entity` value held in managed memory.**
That is the entire residue, and it is not hypothetical: a vanilla UI system keeps a `Dictionary<Entity, Entity>` of last-selected assets, private, invisible to any query, and a stale entry there points at an entity that is about to carry `Deleted`.
Reaching it means reflection, and reaching it one frame after your own `OnCreate`, because that system does not exist yet during it.
**Audit your own managed caches on the same principle**: any dictionary, list or field of yours keyed on or holding a prefab entity is stale the moment you request a regeneration.

Two practical costs come with the technique.

- **Throttle it.** Queue requests rather than acting on one inline, refuse to drain the queue while a drag is in progress, and hold a cooldown between rebuilds — under a second is enough to keep a slider usable without rebuilding on every intermediate value.
  (UNVERIFIED: the sub-second cooldown figure — nobody has timed a regeneration against a running game.)
- **Both entities exist for a while.** Between your call and the prefab system's next update, the old and the new prefab entity are both live.
  Tag the outgoing one with a marker of your own and exclude that marker from your other queries.

**On a vanilla prefab the entity graph survives and the vanilla caches do not.**
That split is the whole of what this call costs, and the first half is the one that misleads: run against a vanilla building, pathway and trailer prefab in a loaded city, each old prefab entity was destroyed, a new one took its place, every placed instance came back pointing at the new entity, and a save and reload round trip completed with the city intact.
The game's own editor does the same thing on a routine path — the duplicate-and-replace-a-mesh and inspector-reparenting flows both hand `UpdatePrefab` whatever prefab is being edited, which is often a vanilla one.

**What breaks is any managed state the game keys by prefab `Entity`**, because that key is exactly what the rebuild throws away.
The vanilla case is not hypothetical: the game mode that restores service-consumption defaults holds a cache keyed by prefab entity, and after regenerating a city-service building it logs `Cached ServiceUpkeepData not found` against the new entity — during a save, long after the call that caused it.
Nothing in the ECS graph is wrong at that point, which is why the failure reads as unrelated to anything you did.

So **regenerate prefabs you minted and treat a vanilla one as a last resort**, and when you do reach for one, expect the damage to surface somewhere that never mentions prefabs.

**The same rule catches your own state.**
A prefab entity does not survive its own update, so a handle taken before the call names a destroyed entity afterwards, and the safe form is to hold the `PrefabBase` and re-resolve the entity after each rebuild.
Note also that the drain wraps each prefab in its own `try`/`catch` that logs and moves on, so a prefab that fails to rebuild leaves the registry pointing at the old entity and reports nothing to your caller — check the result rather than assuming the update took.

(VOLATILE: the replacement system's 14-component query list and the three types its instance-archetype reconciliation covers — the replacement system.)

## Prefab identity, and where the collisions come from

**A `PrefabID` is three fields: `m_Type`, `m_Name`, `m_Hash`.**
Equality compares all three; the hash code deliberately ignores the type.
Three facts about how one is built decide everything else:

- **`m_Type` is `GetType().Name` — the short name, with no namespace.**
  A game `ParcelPrefab` and your own `ParcelPrefab` produce byte-identical ids.
- **`m_Hash` comes from the asset, or is absent.**
  For an asset carrying a platform id the hash is computed from that string, otherwise from the asset guid — and when there is no asset, which is every prefab a mod creates at runtime, the hash stays default.
- The hash only round-trips through a save in new enough formats, so older saves resolve on type and name alone.

**A mod-created prefab is therefore identified by nothing but its class's short name and the string you assigned.**
That is the whole namespace of collisions, and `AddPrefab` handles one the worst possible way: it logs a duplicate-id warning and **skips only the index registration**, while still appending the prefab, creating its entity, and returning `true`.
The loser of a collision is a fully live prefab that `TryGetPrefab(PrefabID)` can never find, and the only trace is one warning line.
Two mods that both create a `StaticObjectPrefab` named `"Bench"` produce exactly that.

**Prefix every prefab name you mint with a token of your own**, and prefer a distinctive class name over a generic one, since the type half of the id is just as collidable as the name half.

**Declaring your prefab class inside the game's own prefab namespace buys nothing for identity.**
Because the id stores only the short type name, the namespace is invisible to lookup and to serialization alike, and no namespace-based lookup exists anywhere.
What the placement does buy is that one `using` resolves your type in your own files; what it costs is that your type now competes for a short name against the twelve hundred-odd types already in that namespace, so a future vanilla type of the same name turns every reference into an ambiguity at compile time.

**`ObsoleteIdentifiers` is the rename-migration mechanism.**
It is an authoring component with two empty hooks and one array of identifier entries, and `AddPrefab` registers every entry as an additional index into the same prefab, so a save written against the old name still resolves.
`PrefabBase.MarkCurrentPrefabObsolete()` appends the current identity to that array, and a malformed hash string in an entry throws during id construction.
Add an entry whenever you rename a prefab you have already shipped, or the player's placed copies do not come back.

### How that identity survives a save

The prefab system writes **a list of `PrefabID`s, not entities**: live prefabs first, then obsolete ones, each guarded by the enabled state of `PrefabData` — the flag `ecs-in-this-game` records as meaning "this prefab still exists".
On load each id is looked up; ids that resolve become index pairs, and ids that do not are kept as obsolete entries keyed by their save index.
Every prefab entity's `LoadedIndex` buffer is then cleared and refilled, so it ends up holding **every save-file index that resolves to that prefab** — which is how a prefab that absorbed two obsolete ids owns several.
Prefabs a save references and the install no longer has get a negative index and a placeholder name for display.
The format itself is `save-serialization`.

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
`patching` owns remedy 3.
