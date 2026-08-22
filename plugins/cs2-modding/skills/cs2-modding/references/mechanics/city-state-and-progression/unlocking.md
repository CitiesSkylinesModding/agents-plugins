# Unlocking

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`Locked` (`src/Game/Game.Prefabs/Locked.cs`) is an empty, enableable, `IEmptySerializable` tag: unlocking is `SetComponentEnabled<Locked>(entity, false)`, the component never leaves, and `HasEnabledComponent<Locked>` is the only right read.
`PrefabSystem.AddPrefab` adds `UnlockRequirement` and `Locked` to a prefab entity exactly when `IsUnlockable(prefab)` — cached per prefab, true for any `UnlockRequirementPrefab`, any prefab carrying an `UnlockableBase` component, or any prefab one of whose dependencies is unlockable, honouring `canIgnoreUnlockDependencies` and each component's `ignoreUnlockDependencies` (`src/Game/Game.Prefabs/PrefabSystem.cs`).
`UnlockFlags` is `RequireAll = 1, RequireAny = 2` (`src/Game/Game.Prefabs/UnlockFlags.cs`).

## The predicate and the loop

Source: `src/Game/Game.Prefabs/UnlockSystem.cs` (`MainLoop`; enabled only while `mode.IsGame()`).

```
run when a pending Unlock event names a still-locked prefab, on the first update after load,
    or when the Locked + UnlockRequirement + Updated query is non-empty
ProcessEvents: each Unlock.m_Prefab still Locked → disable Locked, no new event
loop:
    per locked entity with an UnlockRequirement buffer, over each entry (break once blockedAll):
        locked       = Locked is enabled on entry.m_Prefab
        blockedAll   |= locked and (flags & RequireAll)
        blockedAny   |= locked and (flags & RequireAny)
        satisfiedAny |= !locked and (flags & RequireAny)
    unlock when !blockedAll and (satisfiedAny or !blockedAny)
    each unlocked entity: disable Locked, create an Unlock event, log "Prefab unlocked: {0}"
    until a pass unlocks nothing
```

An empty buffer unlocks as soon as the system looks at it, and so does one whose `RequireAll` entries are all unlocked and which has no `RequireAny` entry; a cascade of any depth resolves in one frame at one full parallel pass per level.

## The requirement family

`UnlockRequirementPrefab` is abstract, carries `string m_LabelID` — a localization key the frontend resolves — and adds `UnlockRequirementData { m_Progress }` (`src/Game/Game.Prefabs/UnlockRequirementPrefab.cs`).
Every subclass's `LateInitialize` adds `UnlockRequirement(entity, RequireAll)` pointing at itself, so under the predicate above `UnlockSystem` can never unlock a requirement; only its own evaluator's explicit `Unlock` event does.
`PrefabUISystem.BindUnlockRequirement`'s `is`-chain is where the subclasses are declared as a set (`src/Game/Game.UI.InGame/PrefabUISystem.cs`); the rows below are its branches, each writing one data component and having one evaluator:

| Prefab | Data component | Evaluator |
| --- | --- | --- |
| `CitizenRequirementPrefab` | `CitizenRequirementData { m_MinimumPopulation, m_MinimumHappiness }` | `CountHouseholdDataSystem` (`src/Game/Game.Simulation/CountHouseholdDataSystem.cs`) |
| `ObjectBuiltRequirementPrefab` | `ObjectBuiltRequirementData { m_MinimumCount }` | `ObjectBuiltRequirementSystem` |
| `StrictObjectBuiltRequirementPrefab` | `StrictObjectBuiltRequirementData` | `StrictObjectBuiltRequirementSystem` |
| `ZoneBuiltRequirementPrefab` | `ZoneBuiltRequirementData` | `ZoneBuiltRequirementSystem`, which clears on squares and count together |
| `ProcessingRequirementPrefab` | `ProcessingRequirementData { m_ResourceType, m_MinimumProducedAmount }` | `ProcessingRequirementSystem` |
| `TransportRequirementPrefab` | `TransportRequirementData` | `TransportRequirementSystem`, over `TransportUsageData`'s train, ship and airplane counters only, `TransportType.None` summing the three — a requirement on any other `TransportType` never advances |
| `PrefabUnlockedRequirementPrefab` | `PrefabUnlockedRequirement` buffer | `PrefabUnlockedRequirementSystem` |

Milestones and dev-tree nodes gate unlocks without being subclasses: `MilestonePrefab` derives from `PrefabBase` and declares no unlock component in C#, `DevTreeNodePrefab` requires `ManualUnlockable`, and `PrefabUISystem.GetRequirements` is where the kinds are sorted — `MilestoneData`, `DevTreeNodeData`, `UnlockRequirementData`, and a locked tutorial prefab ([progression.md](progression.md)).

`ObjectBuiltRequirementSystem` is the shape the others follow (`src/Game/Game.Prefabs/ObjectBuiltRequirementSystem.cs`):

```
query: PrefabRef with Any { Created, Deleted }, None { Native, Temp };
    on the first update after load, every PrefabRef entity without Native or Temp instead, so the city is recounted once
per instance whose prefab carries an UnlockOnBuildData buffer, per listed requirement:
    num = max(m_Progress + (deleted ? -1 : +1), 0)
    m_Progress = min(m_MinimumCount, num)                       // the display saturates
    if Locked is enabled and m_MinimumCount <= num:             // the comparison does not
        create Unlock(requirement), sort key = the outer (entity) loop index     // the SetComponent above uses unfilteredChunkIndex
```

`UnlockOnBuild.m_Unlocks` is typed `ObjectBuiltRequirementPrefab[]` (`src/Game/Game.Prefabs/UnlockOnBuild.cs`), which is what restricts the built-count route to that one requirement class.
`CitizenRequirementData`'s progress is two-phase — `min(population, m_MinimumPopulation)` until population clears the bar (or for ever when `m_MinimumHappiness == 0`), then `min(averageHappiness, m_MinimumHappiness)` — while the unlock needs both.

## How a mod adds one

- **`PrefabSystem.AddUnlockRequirement(unlocker, unlocked)`** appends a `RequireAll` entry to the unlocked prefab's buffer; both sides must be unlockable or it warns — `"is trying to add unlock requirements, but is non-unlockable"` for the unlocker, `"is trying to add unlock requirement to non-unlockable prefab"` for the target — and does nothing, and since `IsUnlockable` is cached the moment it is first asked, a target already added without qualifying has no buffer to append to.
- **Authoring `Unlockable`** (`src/Game/Game.Prefabs/Unlockable.cs`) with `m_RequireAll`, `m_RequireAny`, `m_IgnoreDependencies`: its `LateInitialize` adds a `RequireAll` entry per unlockable dependency unless ignored, then the two arrays, skipping any entry that is not itself unlockable.
- **A new requirement kind** is an `UnlockRequirementPrefab` subclass plus the system that emits its `Unlock`; anything the `is`-chain does not match renders through `BindUnknownUnlockRequirement` as the generic `prefabs.UnlockRequirement` carrying `entity`, `labelId`, `progress`, `locked` — so `m_LabelID` is the only thing that makes it say anything specific (`src/Game/Game.UI.InGame/PrefabUISystem.cs`).

**`UIGroupPrefab` unlocks on any member, and `AddElement` is the only way to join one.**
Its `LateInitialize` adds a self-referencing `RequireAny` entry and `AddElement` adds `UnlockRequirement(element, RequireAny)` beside the `UIGroupElement`, so a `UIGroupElement` appended without that requirement entry is invisible to the group's unlock.
Source: `src/Game/Game.Prefabs/UIGroupPrefab.cs`.

**`ecs_query` on `Locked` returns only the still-locked prefabs.**
The default query filtering drops entities whose enableable component is disabled, so the unlocked ones are the complement, never a result.
Source: `src/Game/Game.Prefabs/Locked.cs`.

`ProgressionUtils.GetRequiredMilestone` (`src/Game/Game.UI.InGame/ProgressionUtils.cs`) walks the graph through `RequireAll` edges, short-circuiting on the self-entry, and is the supported answer to "which milestone does this prefab need".

(VOLATILE: every system, component, field, property, enum, method, quoted log string, binding name and `Source:` path this file names — their declarations under `src/Game/` in `Game.Prefabs`, `Game.Simulation`, `Game.Common`, `Game.Tools` and `Game.UI.InGame`, at the files the sections cite; plus the requirement-class rows — the `is`-chain in `PrefabUISystem.BindUnlockRequirement`.)
