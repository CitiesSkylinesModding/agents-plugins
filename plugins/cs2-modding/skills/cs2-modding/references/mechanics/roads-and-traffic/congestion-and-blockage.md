# Congestion, blockage and wear

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

## The feedback loop

Sources: `src/Game/Game.Simulation/CarNavigationSystem.cs`, `src/Game/Game.Simulation/TrafficFlowSystem.cs`, `src/Game/Game.Net/NetUtils.cs`, `src/Game/Game.Pathfind/PathUtils.cs`.

```
CarNavigationSystem.ApplySideEffects:              // per traversed car lane with any progress
  skipped while CarLaneFlags.ResetSpeed is set     // ResetSpeed marks the just-entered stretch after a spawn or connection, cleared ~10 m in
  flow = float2(duration * min(prefab m_MaxSpeed, lane m_SpeedLimit * f), distance) / max(1, curveLength)    // f = 2 for an emergency vehicle, else 1
  flow = -flow  for bicycles                       // the sign routes the accumulation
ApplyLaneEffectsJob:
  any negative component -> SecondaryFlow.m_Next -= flow   // bicycles, as magnitudes
  else                   -> LaneFlow.m_Next += flow
  LaneCondition.m_Wear = min(m_Wear + sideEffects.x * LaneDeteriorationData.m_TrafficFactor, 10)   // capped, C#
  sideEffects.yz add into the owner edge's Game.Net.Pollution
TrafficFlowSystem (a 512-frame tick over lanes spread across 16 groups, so each lane 32 times a day):
  m_Duration and m_Distance are float4s, one slot per quarter of the day
  each lerps toward m_Next with t = m_TimeFactors * 0.125, then m_Next clears
                                                   // m_TimeFactors: a tent over the current time-of-day quarter
UpdateLaneFlow:
  flowSpeed = NetUtils.GetTrafficFlowSpeed(LaneFlow + SecondaryFlow, summed)
            = saturate(distance / duration)        // achieved over free-flow, 0..1
  CarLane.m_FlowOffset = clamp(256 - round(dot(flowSpeed, timeFactors) * 256), 0, 255)
  when the byte changed -> enqueue FlowActionData onto the pathfinding queue
PathUtils.CalculateSpeed:
  speed -= m_FlowOffset / 256 * speed              // unless PathfindFlags.IgnoreFlow
```

So congestion is a per-lane, per-time-of-day-quarter, exponentially smoothed byte, and the whole loop runs through `CarLane.m_FlowOffset`: a mod changing how routing responds to congestion writes that byte or sets `IgnoreFlow`, and a mod reading congestion reads it.
The byte re-merges what the sign split, so bicycle and car flow separate in the accumulators and not in the penalty.
On a multi-lane carriageway the same job also writes the master lane's byte from the slave group's summed flow, so the lane the pathfinder actually prices ([route-selection.md](route-selection.md)) carries the penalty without carrying `LaneFlow` itself.
The same system rolls each lane's `LaneFlow` up to the edge: `Road` holds four `float4`s — duration and distance for each end, the four day-quarter slots inside each — fed by which end of `EdgeLane.m_EdgeDelta` a lane touches, a lane with no `EdgeLane` feeding both ends whole, or a third to each on a roundabout (`src/Game/Game.Net/Road.cs`).
`TrafficFlowSystem.cityAverageTrafficFlow` and `cityAverageTrafficVolume` are the city-wide readable surface.
A lane carries `LaneFlow` only when its pathfind prefab opts in — `PathfindPrefab.m_TrackTrafficFlow`, mirrored as `LaneFlags.TrackFlow` onto `NetLaneData` by `NetInitializeSystem` — and never on master lanes; a bicycle-capable lane carries `SecondaryFlow` instead of or beside it (`src/Game/Game.Prefabs/CarLane.cs`, `PathfindPrefab.cs`, `NetInitializeSystem.cs`).

## Blockage

`LaneDataSystem.CheckBlockage` unions the curve range of every `LaneObject` without a `Moving` component — a stalled car, a parked wreck and an accident block the same range — and `AddBlockageData` writes it into `CarLane.m_BlockageStart/End` as bytes; the caution range rides along on every non-master copy for an accident or a stopped emergency vehicle, and for any other blockage except on a slave lane flagged both `StartingLane` and `EndingLane`, while only accident involvement sets the secured flag (`src/Game/Game.Pathfind/LaneDataSystem.cs`).
`PathUtils.GetCarDriveSpecification` then sets `RuleFlags.HasBlockage` whenever `m_BlockageEnd >= m_BlockageStart`, and the search rejects any traversal whose delta range overlaps the blocked range; an agent opts out through `m_IgnoredRules & HasBlockage` — `m_TaxiIgnoredRules` on a taxi-method edge (`src/Game/Game.Pathfind/PathUtils.cs`, `PathfindJobs.cs`).

**An unblocked lane is `m_BlockageStart = 255, m_BlockageEnd = 0` — start above end.**
The inverted sentinel is what makes the `>=` test mean "blocked", and it is what `LaneDataSystem` resets to and what the save-migration path fills in; reading the raw bytes as a `0..255` range gets it exactly backwards.
Source: `src/Game/Game.Pathfind/LaneDataSystem.cs`, `src/Game/Game.Net/CarLane.cs`.

## Wear

Wear accrues only on lanes whose prefab carries `LaneDeteriorationData { m_TrafficFactor, m_TimeFactor }`, each accrual capping itself at 10: traffic through `m_TrafficFactor` — the loop above for cars, and the train navigation running the same accrual on track lanes — and `NetDeteriorationSystem` adding `m_TimeFactor` per day in sixteen traffic-independent steps, so a mod silencing one writer alone leaves wear climbing (`src/Game/Game.Simulation/TrainNavigationSystem.cs`, `src/Game/Game.Simulation/NetDeteriorationSystem.cs`); `LaneDeterioration.GetArchetypeComponents` adds `LaneCondition` to every non-master lane of such a prefab (`src/Game/Game.Prefabs/LaneDeterioration.cs`).
`NetCondition { m_Wear }` is the edge-level `float2`, and wear's one routing consequence is the accident-safety term ([accidents.md](accidents.md)); the repair side belongs to the maintenance service.
The `sideEffects` rate is quadratic in the achieved-over-maximum speed ratio, lerped between the vehicle prefab's own min and max, and the emitted `yz` scale by time spent on the lane — what those components become is `environment-and-pollution`'s.

## Deadlock and bottleneck detection

`StuckMovingObjectSystem` runs every 4 frames over everything carrying `Blocker { m_Blocker, m_Type, m_MaxSpeed }` — vehicles and creatures alike — spread across 16 groups: a mover whose blocker is slow (`m_MaxSpeed < 6`, type not `Temporary`) is stuck at once when that blocker is a parked train, or a parked car under a non-car mover; otherwise the blocker chain is walked, and 100 hops or a return to the mover, its vehicle or its target earns the mark — `PathFlags.Stuck` on a path owner, `CreatureLaneFlags.Stuck` on an animal lane (`src/Game/Game.Simulation/StuckMovingObjectSystem.cs`, `src/Game/Game.Vehicles/Blocker.cs`).
`Stuck` is terminal for the current plan — `RequireNewPath` refuses a stuck owner — and each vehicle AI decides what follows, service vehicles typically turning for base.
`TrafficBottleneckSystem` groups `Continuing` blocker chains, stamps `Game.Net.Bottleneck` at the head of a group once it reaches 50 vehicles, raises the notification named by `TrafficConfigurationData.m_BottleneckNotification` on a timer, and logs a line starting `"TrafficBottleneckSystem: Self blocking entity"` — a prefix worth grepping a player log for (`src/Game/Game.Simulation/TrafficBottleneckSystem.cs`, `src/Game/Game.Prefabs/TrafficConfigurationData.cs`).

(VOLATILE: every component, field, enum, system, method, constant, quoted log string and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Net`, `Game.Pathfind`, `Game.Vehicles` and `Game.Prefabs`, at the files cited beside each; the loop listing, against `CarNavigationSystem.cs` and `TrafficFlowSystem.cs`.)
