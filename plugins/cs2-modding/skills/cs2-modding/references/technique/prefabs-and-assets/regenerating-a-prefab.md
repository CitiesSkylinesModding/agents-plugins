# Regenerating a prefab in place

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

What `PrefabSystem.UpdatePrefab` actually does to a registered prefab, what the game repairs afterwards, what it cannot, and the costs the technique carries.
The prefab layer above is [prefabs and assets](prefabs-and-assets.md).

## What the call does

**`UpdatePrefab` does not update anything; it destroys and re-creates.**
The pending map is drained at the top of the prefab system's update, and for each entry it:

1. tags the **old prefab entity** `Deleted`;
2. rebuilds the prefab-component set from scratch by re-running every attached component's `GetPrefabComponents`, and creates a **brand-new entity**;
3. copies the old `PrefabData`, so the new entity keeps the same index and the managed list needs no change;
4. carries the unlocked state across;
5. repoints the registry at the new entity;
6. calls the replacement system with the old and new entities.

Each iteration is individually try/caught, logged and skipped on failure.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

## What the game repairs, and what it cannot

**The prefab entity's identity changes, and that is the whole story of what goes stale.**
The ECS side is covered thoroughly: the replacement system queries a 14-way `Any` over `PrefabRef`, `SubObject`, `SubNet`, `SubArea`, `PlaceholderObjectElement`, `ServiceUpgradeBuilding`, `BuildingUpgradeElement`, `Effect`, `ActivityLocationElement`, `CharacterElement`, `SubMesh`, `LodMesh`, `UIGroupElement` and `TutorialPhaseRef`, rewrites every `m_Prefab` field from old to new, tags each touched instance `Updated`, then tags any entity whose `PrefabRef` points at a touched instance so owners see their children change.
Upgrade-element buffers are copied by hand, system-held references are patched through a dedicated pass, and mesh batches are rebuilt when the prefab carried mesh data.
Source: `src/Game/Game.Prefabs/ReplacePrefabSystem.cs`.

**What that sweep cannot reach is any `Entity` value held in managed memory.**
That is the entire residue, and it is not hypothetical: a vanilla UI system keeps a `Dictionary<Entity, Entity>` of last-selected assets, private, invisible to any query, and a stale entry there points at an entity that is about to carry `Deleted`.
Reaching it means reflection, and reaching it one frame after your own `OnCreate`, because that system does not exist yet during it.
**Audit your own managed caches on the same principle**: any dictionary, list or field of yours keyed on or holding a prefab entity is stale the moment you request a regeneration.
Source: `src/Game/Game.UI.InGame/ToolbarUISystem.cs` (the private dictionary), `src/Game/Game.Prefabs/ReplacePrefabSystem.cs` (the sweep that cannot see it).

## The two practical costs

- **Throttle it.** Queue requests rather than acting on one inline, refuse to drain the queue while a drag is in progress, and hold a cooldown between rebuilds — under a second is enough to keep a slider usable without rebuilding on every intermediate value.
  (UNVERIFIED: the sub-second cooldown figure — nobody has timed a regeneration against a running game.)
- **Both entities exist for a while.** The drain creates the replacement during the prefab system's next update, and from then until the `Deleted` tag is collected the old and the new prefab entity are both live.
  Tag the outgoing one with a marker of your own and exclude that marker from your other queries.
  Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

**Hold the `PrefabBase`, not the entity, and check that the rebuild took.**
A prefab entity does not survive its own update, so a handle taken before the call names a destroyed entity afterwards, and the safe form is to re-resolve the entity after each rebuild.
The drain wraps each prefab in its own `try`/`catch` that logs and moves on, so a prefab that fails to rebuild leaves the registry pointing at the old entity and reports nothing to your caller.
Source: `src/Game/Game.Prefabs/PrefabSystem.cs`.

## Doing it to a vanilla prefab

**On a vanilla prefab the entity graph survives and the vanilla caches do not.**
That split is the whole of what this call costs, and the first half is the one that misleads: run against a vanilla building, pathway and trailer prefab in a loaded city, each old prefab entity was destroyed, a new one took its place, every placed instance came back pointing at the new entity, and a save and reload round trip completed with the city intact.
The game's own editor does the same thing on a routine path — the duplicate-and-replace-a-mesh and inspector-reparenting flows both hand `UpdatePrefab` whatever prefab is being edited, which is often a vanilla one.
Source: `src/Game/Game.Prefabs/ReplacePrefabSystem.cs` (the instance remap), `src/Game/Game.UI.Editor/EditorHierarchyUISystem.cs` and `src/Game/Game.UI.Editor/InspectorPanelSystem.cs` (the two editor flows).

**What breaks is any managed state the game keys by prefab `Entity`**, because that key is exactly what the rebuild throws away.
The vanilla case is not hypothetical: the game mode that restores service-consumption defaults holds a cache keyed by prefab entity, and after regenerating a city-service building it logs `Cached ServiceUpkeepData not found` against the new entity — in a captured run, during a save, long after the call that caused it.
Nothing in the ECS graph is wrong at that point, which is why the failure reads as unrelated to anything you did.
Source: `src/Game/Game.Prefabs.Modes/ServiceConsumptionGlobalMode.cs` (the cache and the log line).

So **regenerate prefabs you minted and treat a vanilla one as a last resort**, and when you do reach for one, expect the damage to surface somewhere that never mentions prefabs.

(VOLATILE: the replacement system's 14-component query list — the replacement system.)
