# Progression: XP, milestones and the development tree

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

XP flows through one queue: `XPSystem` (`src/Game/Game.Simulation/XPSystem.cs`, `ModificationEnd`) hands out `NativeQueue<XPGain>` through `GetQueue(out deps)` / `AddQueueWriter(handle)`, adds every non-zero `XPGain { entity, amount, XPReason reason }` to `XP.m_XP`, and mirrors `amount` and `reason` into an `XPMessage` queue that `TransferMessages(IXPMessageHandler)` drains for the UI; `entity` is read by nothing.
`XPReason` (`src/Game/Game.Simulation/XPReason.cs`) is the declared reason set; `Income` has no producer, and `XP.m_MaximumIncome`, the field it would be paid from, has no reader.

## XP producers

Sources: `src/Game/Game.Simulation/XPAccumulationSystem.cs`, `src/Game/Game.Simulation/XPBuiltSystem.cs`, `src/Game/Game.Simulation/NetXPSystem.cs`, `src/Game/Game.Debug/DebugSystem.cs`.

```
XPAccumulationSystem, every 262144 / kUpdatesPerDay frames (static readonly int kUpdatesPerDay = 32):
    skip when Population.m_Population < 10
    gained = max(0, population - XP.m_MaximumPopulation); XP.m_MaximumPopulation = max(old, population)
    income tally: for i in 0..4: num2 = ResidentialTaxableIncome[i]       // `=`, not `+=`: only i == 4 survives
                  num2 += Σ resources (Commercial + Industrial + Office)TaxableIncome[resource]
                  XP.m_MaximumIncome = max(old, num2 / 10)               // read by nothing
    enqueue Population: floor(m_XPPerPopulation * gained / kUpdatesPerDay)
    enqueue Happiness:  floor(m_XPPerHappiness * m_AverageHappiness / kUpdatesPerDay)

XPBuiltSystem, only while actionMode.IsGame(), over Created entities without Temp:
    prefab with PlaceableObjectData and no PlacedSignatureBuildingData:
        m_XPReward > 0 → enqueue ServiceBuilding
        prefab has SignatureBuildingData → add PlacedSignatureBuildingData to the PREFAB entity (pays once ever)
    prefab with ServiceUpgradeData: m_XPReward > 0 → enqueue ServiceUpgrade
    once, while XP.m_XPRewardRecord lacks ElectricityGridBuilt: the first ElectricityConsumer with
        m_FulfilledConsumption > 0 enqueues kElectricityGridXPBonus (static readonly int = 25)
        as ElectricityNetwork and latches the flag

NetXPSystem, no IsGame gate, over created and deleted edges without Temp:
    per edge with PlaceableNetData.m_XPReward > 0:
        bonus = 1 when the prefab is a road and either end node has Elevation.m_Elevation.x > 0 and .y > 0, else 0
        xp = (m_XPReward + bonus) * Curve.m_Length / kXPRewardLength      // static readonly float = 112
        bucketed by prefab: road, train, tram, subway, waterway, pipe, power line
    deleted edges are counted the same way and subtracted per bucket
    only the largest bucket is enqueued, floored, when > 0; the other six are discarded

DebugSystem enqueues XPReason.Unknown from the developer menu
```

Population XP is paid on a new maximum, so a city that shrinks and regrows earns nothing until it passes its old peak.

## The milestone step

Source: `src/Game/Game.Simulation/MilestoneSystem.cs` (`ModificationEnd`).

```
return while m_TutorialSystem.tutorialIntroUnfinished
achieved = MilestoneLevel.m_AchievedMilestone
m_LastRequired = TryGetMilestone(achieved) ? its m_XpRequried : 0
if TryGetMilestone(achieved + 1):
    m_NextRequired = its m_XpRequried                       // assigned nowhere else
    if CitySystem.XP >= m_NextRequired:
        m_AchievedMilestone++; write the singleton back
        NextMilestone(achieved + 1):
            found: create MilestoneReachedEvent(milestone, index) and Unlock(milestone) on ModificationEndBarrier
                   PlayerMoney.Add(m_Reward); Creditworthiness.m_Amount += m_LoanLimit
                       // both written directly on the city entity, outside the command buffer
            missing: create MilestoneReachedEvent(Entity.Null, index); warn "did not find data for milestone N"
m_Progress = XP - max(0, m_LastRequired); m_NextMilestone = MilestoneLevel.m_AchievedMilestone + 1   // after the increment
TryGetMilestone(index): linear scan of the MilestoneData query, two TempJob arrays per call
```

Requirements and rewards rise with `m_Index`; the values are the prefabs'.
`m_DevTreePoints` reaches the city through the `MilestoneReachedEvent` (below) and `m_MapTiles` through a permit budget summed over unlocked milestones on every ask ([map-tiles.md](map-tiles.md)); the `Unlock` event cascades through `UnlockSystem` ([unlocking.md](unlocking.md)).
`m_Major` reaches only the `milestone` binding.

**`MilestoneSystem.progress` and `requiredXP` are meaningless once the last milestone is reached, and nothing in the game reads them.**
`m_NextRequired` is assigned only inside the next-milestone branch, so past the last prefab it holds zero in a session that loaded there — `requiredXP` negative, `progress` non-positive on a city that earned its milestones and a plausible positive fraction on one whose level was forced ahead of its XP (`unlockAll`) — or the last requirement in a session that crossed it, dividing by zero; `MilestoneUISystem` never reads `progress` and branches on `m_LockedMilestoneQuery.IsEmpty` (its `maxMilestoneReached` binding), which is the test a mod replicates.
Source: `src/Game/Game.Simulation/MilestoneSystem.cs`, `src/Game/Game.UI.InGame/MilestoneUISystem.cs`.

## Development points

Source: `src/Game/Game.City/DevTreeSystem.cs` (`ModificationEnd`), `src/Game/Game.Prefabs/DevTreeNodePrefab.cs`.

```
accrual, when MilestoneReachedEvent entities exist:
    per event: m_Points += event.m_Milestone != Entity.Null
                   ? MilestoneData.m_DevTreePoints
                   : GetDefaultPoints(index)                 // the missing-prefab path only
    GetDefaultPoints(level) = level <= 0 ? 0 : level >= 19 ? 10 : (level + 1) / 2 + 1

Purchase(node), public, also a DevTreeNodePrefab overload:
    unlock when m_Cost <= points
        and Locked is enabled on the node
        and (m_Service == Entity.Null or Locked is not enabled on it)
        and (no DevTreeNodeRequirement buffer, or no non-null entry in it, or ANY listed non-null node has Locked disabled)
    then points -= m_Cost; create Unlock(node) on EndFrameBarrier; Telemetry.DevNodePurchased
```

`GetDefaultPoints` is a degradation path for a milestone index with no prefab; it does not describe what a milestone grants.
`DevTreeNodePrefab` is `[RequireComponent(typeof(ManualUnlockable))]`, which adds a self-referencing `RequireAll` entry so `UnlockSystem` never unlocks a node on its own; the node's any-of requirement is what lets a tree branch, where `UnlockRequirement` defaults to all-of.

**`DevTreeNodeAutoUnlock` has no reader.**
`DevTreeNodePrefab` adds it to a node whose `m_Cost == 0`; such a node unlocks through `Purchase` like any other.
Source: `src/Game/Game.Prefabs/DevTreeNodePrefab.cs`, `src/Game/Game.Prefabs/DevTreeNodeAutoUnlock.cs`.

(VOLATILE: every system, component, field, property, enum, method, constant, quoted log string, binding name and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.City`, `Game.Prefabs`, `Game.Debug`, `Game.Net`, `Game.Buildings`, `Game.Common`, `Game.Tools`, `Game.PSI` and `Game.UI.InGame`, at the files the sections cite.)
