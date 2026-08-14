# Junctions

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Yield, stop and right of way come out of one derived priority number; traffic lights are demand-actuated, not fixed-cycle; both are written onto lanes at generation time.

## The priority number and the yield decision

Sources: `src/Game/Game.Prefabs/NetCompositionSystem.cs`, `src/Game/Game.Net/LaneSystem.cs`.

```
NetCompositionSystem:                              // per road composition
  m_Priority = RoadData.m_SpeedLimit               // m/s
  m_Priority -= 1.25 if CompositionFlags.General.Gravel
  when the composition carries NetCompositionLanes:
    count forward/backward lanes flagged Road and not Master
    m_Priority += max(fwd, bwd)                          under UseHighwayRules
    m_Priority += max(fwd, bwd) * 0.5 + (fwd + bwd) * 0.25   otherwise

LaneSystem.CalculateYieldOffset(source, sources, targets):   // per junction arm
  node composition has AllWayStop                  -> 2 (stop)
  node composition has LevelCrossing or TrafficLights -> 0   // signals replace priority
  scan other source arms for m_Priority exceeding this arm's by more than 0.99:
    two or more such arms                          -> 1 (yield)
    exactly one -> scan target arms too (the found arm excluded);
                   one outranking this arm -> 1
  no higher-priority arm, and this arm has UseHighwayRules:
    this arm has a non-turning or gentle-turning movement      -> 0
    another arm does                               -> 1
  otherwise                                        -> 0
```

`CreateNodeLane` maps the value onto the generated lane — `1 → CarLaneFlags.Yield`, `2 → Stop`, `-1 → RightOfWay` — with junction arms fed by `CalculateYieldOffset` and roundabout creation passing `1` to entering lanes and `-1` to exiting ones where two or more sources feed the node, the circulating arcs carrying no flag; `CompositionFlags.General.TrafficLights` on the intersection separately adds `CarLaneFlags.TrafficLights` (`src/Game/Game.Net/LaneSystem.cs`).
The `> 0.99` threshold means one m/s of speed limit — or, by the lane term above, one extra `Road` lane per direction — assigns priority, so identical arms never sign each other while a class step, a widening or a gravel surface does; at an unsignalled junction of identical roads no lane carries a sign, and each conflict point falls to the overlap's handed give-way rule below.
Because the flags are written during lane creation and lanes are regenerated from scratch on every relane, there is no place to write a sign after generation: changing signage means changing the inputs — `AllWayStop` or `TrafficLights` on the node composition — or owning generation itself ([network-rebuild.md](network-rebuild.md)).
Player-set turn restrictions ride the same inputs: `ForbidLeftTurn`, `ForbidStraight` and `ForbidRightTurn` are `CompositionFlags.Side` bits on `Upgraded.m_Flags` (`src/Game/Game.Prefabs/CompositionFlags.cs`, `src/Game/Game.Net/Upgraded.cs`).

## Traffic lights

`TrafficLights { m_State, m_Flags, m_SignalGroupCount, m_CurrentSignalGroup, m_NextSignalGroup, m_Timer }` sits on the node, and each controlled lane carries `LaneSignal { m_Petitioner, m_Blocker, m_GroupMask, m_Priority, m_Default, m_Signal, m_Flags }` (`src/Game/Game.Net/TrafficLights.cs`, `LaneSignal.cs`).
`TrafficLightSystem.GetNextSignalGroup` scans every lane signal for the highest priority — clamped to 127 normally, to 1 on a moveable bridge — accumulates the group masks at that priority into an eligibility mask, resets each signal's priority to `m_Default` as it goes, and returns the next eligible group in cyclic index order from the current one; `RequireEnding` ends the current phase when any lane still showing `Go` is not in the next group (`src/Game/Game.Simulation/TrafficLightSystem.cs`).
So petitions decide which phases are eligible — an unpetitioned phase is skipped — and how long one lasts, never the order, and `m_Petitioner` names the entity that asked.
The system ticks every 4 frames over junctions spread across 16 update-frame groups — a 64-frame period per junction; `TrafficLightInitializationSystem` builds the signal groups during Modification4B.

## Roundabouts and conflict points

A roundabout's lane count is computed per arc between consecutive arms, not per roundabout: `LaneSystem.GetRoundaboutLaneCount` quantises the arc's angular span to quarter turns and derives the arc's count from the entering arm's matching lanes and what the surrounding arms take, through two branches whose arithmetic differs — read the method before predicting a count — floored at 1, with handedness entering through the left-hand-traffic rotation choice (`src/Game/Game.Net/LaneSystem.cs`).
Where lanes cross, `LaneOverlap { m_Other, m_Flags, m_ThisStart, m_ThisEnd, m_OtherStart, m_OtherEnd, m_Parallelism, m_PriorityDelta }` is the sorted per-lane buffer built by `LaneOverlapSystem`, its four range bytes curve positions scaled by 255 (`src/Game/Game.Net/LaneOverlap.cs`).
`m_PriorityDelta` is the give-way decision at the conflict point: where the two lanes' flags differ the difference decides it, and where they do not — identical unsignalled roads — a handed tangent test awards the point to one side, priority-to-the-right under right-hand traffic (`src/Game/Game.Net/LaneOverlapSystem.cs`).
`LaneReservation` is the runtime claim a vehicle stakes on a conflict point, maintained by `NetLaneReservationSystem` in `GameSimulation` (`src/Game/Game.Net/LaneReservation.cs`).

(VOLATILE: every component, field, enum, system, method and `Source:` path this file names — their declarations under `src/Game/` in `Game.Net`, `Game.Prefabs` and `Game.Simulation`, at the files cited beside each; the listing, against `NetCompositionSystem.cs` and `LaneSystem.cs`.)
