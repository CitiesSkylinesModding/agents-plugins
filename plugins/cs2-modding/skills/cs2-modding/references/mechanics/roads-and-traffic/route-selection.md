# Route selection

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

The search is an A* over a graph whose edges are non-slave lanes — car, parking, pedestrian, connection and track — so a path through a multi-lane carriageway is a sequence of master lanes, never of physical lanes.
`LanesModifiedSystem` excludes `SlaveLane` from the car, parking, pedestrian and connection queries it turns into graph actions; its track-lane query carries no such exclusion (`src/Game/Game.Pathfind/LanesModifiedSystem.cs`).
The physical lane is chosen at drive time: `CarNavigationSystem.GetSlaveLaneFromMasterLane` resolves a master lane to one of `lanes[m_MinIndex … m_MaxIndex]` by `NetUtils.ChooseClosestLane` — toward the target object's position when approaching one, otherwise toward the point where the vehicle will leave its current lane — or by reservoir sampling over the group's `SubLane.m_PathMethods`, a successful closest-lane pick stamped `Game.Vehicles.CarLaneFlags.FixedStart` (`src/Game/Game.Simulation/CarNavigationSystem.cs`).
`Game.Vehicles.CarLaneFlags.FixedLane` marks a navigation lane exempt from that resolution — stamped on parking, connection and no-master lanes among others — and, with `Reserved`, it is what bars re-picking a lane already chosen.
A mod that wants to steer lane use therefore edits that resolution or the `m_PathMethods` masks, not the path.

## The cost model

Sources: `src/Game/Game.Pathfind/PathfindJobs.cs`, `src/Game/Game.Pathfind/PathUtils.cs`.

```
PathfindExecutor.CalculateCost(spec, flags, rules, delta):   // rules already masked by the
  speed = PathUtils.CalculateSpeed(spec, parameters)         // agent's m_IgnoredRules
  value = spec.m_Costs.m_Value                               // the edge's baked float4 costs
  value.xyw += spec.m_Length * float3(1 / speed,
                 rules has any Forbid* rule ? 1 : 0,         // behaviour, per metre
                 rules has AvoidBicycles    ? 0.1 : 0)       // comfort, per metre
  value.y += 100 when having a matching authorization differs from
             the edge's RequireAuthorization flag
  value.xyz = 0 when the edge is free in the travelled direction (FreeForward /
              FreeBackward), or the crossing is Boarding-only
  return dot(value, weights.m_Value) * abs(delta.y - delta.x)

PathUtils.CalculateSpeed(spec, parameters):
  agent = m_WalkSpeed on pedestrian edges, else m_MaxSpeed    // each a float2; a
          .y on an EdgeFlags.Secondary edge, else .x          // Secondary edge uses .y
  speed = min(agent, spec.m_MaxSpeed)            // spec.m_MaxSpeed unclamped for modes
                                                 // outside Pedestrian / Road / Track /
                                                 // Flying / MediumRoad / Bicycle
  return speed - spec.m_FlowOffset / 256 * speed // congestion penalty, off under IgnoreFlow

PathfindExecutor.AddConnections:
  costFactor *= 0.5 under ParkingReset — armed on a parking-method edge and
                     latched for every edge after it, through the item's
                     PathfindItemFlags.ReducedCost propagating to successors
  costFactor *= random in [0.5, 1)   unless PathfindFlags.Stable

PathfindExecutor.Initialize:                     // the heuristic
  m_HeuristicCostFactor = min over the agent's enabled PathMethods of
      dot(that mode's cheapest per-metre costs + 1 / max(0.01, speed)
          in the time component, agent weights)
      // speed: the agent's own m_WalkSpeed.x on foot and its m_MaxSpeed.x
      // for car, track, flying and off-road alike, a literal 111.11 for
      // taxi, and the network-wide transport and cargo top speeds passed
      // in; 1000000 when no enabled method has a branch; the transport
      // and cargo modes start from zero costs
  factor = 0             under NoHeuristics      // A* becomes Dijkstra
  factor *= 2            unless Stable           // greedier, less exact
  factor *= 0.5          under ParkingReset

PathfindExecutor.CalculateTotalCost:
  total = baseCost + distance-to-target-bounds * m_HeuristicCostFactor
```

Time accrues as length over congestion-reduced speed; behaviour accrues per metre on any edge carrying an unignored `Forbid*` rule — `HasBlockage` accrues nothing — plus a flat 100 on an authorization mismatch; comfort accrues per metre only for `AvoidBicycles`; money never accrues here — it comes entirely from the edge's baked costs.
Route variation is that `[0.5, 1)` draw on every edge, so two identical agents on identical journeys take different routes; a mod that needs reproducible routing sets `PathfindFlags.Stable`.
`PathfindFlags` in full: `Stable`, `IgnoreFlow`, `ForceForward`, `ForceBackward`, `NoHeuristics`, `ParkingReset`, `Simplified`, `MultipleOrigins`, `MultipleDestinations`, `IgnoreExtraStartAccessRequirements`, `IgnoreExtraEndAccessRequirements`, `IgnorePath`, `SkipPathfind` (`src/Game/Game.Pathfind/PathfindFlags.cs`).
The per-mode cheapest costs come from `NetInitializeSystem`, which reduces each mode's to the minimum over every lane-referenced pathfind prefab — driving cost for car and track, walking cost for pedestrians, and the transport, airway and area costs for the taxi, flying and off-road modes — so registering a net prefab cheaper than any shipped one lowers the heuristic for every agent in the game and widens every search (`src/Game/Game.Prefabs/NetInitializeSystem.cs`).
`PathUtils.CalculateCost` is the simplified sibling used outside the search: time plus baked costs, the same dot product, the same `[0.5, 1)` draw.

## Where an edge's baked costs come from

From a lane entity: `PrefabRef` → the lane prefab → `NetLaneData.m_PathfindPrefab` → the pathfind prefab entity, which carries one cost component per mode (`src/Game/Game.Prefabs/NetLaneData.cs`).
The `unity` tools follow one reference per call, so from the lane prefab it is one `ecs_list_components` with `values=true` and `follow="Game.Prefabs.NetLaneData:m_PathfindPrefab"`, after a `PrefabRef` read from the instance.
Every cost is a `PathfindCosts` — one `float4 m_Value` in `(time, behaviour, money, comfort)` order, no named fields; `PathfindCostInfo` is the authoring-side struct that names all four and converts (`src/Game/Game.Pathfind/PathfindCosts.cs`, `src/Game/Game.Prefabs/PathfindCostInfo.cs`).

| Mode | Component and fields (`src/Game/Game.Prefabs/`) |
| --- | --- |
| Car | `PathfindCarData { m_DrivingCost, m_TurningCost, m_UnsafeTurningCost, m_UTurnCost, m_UnsafeUTurnCost, m_CurveAngleCost, m_LaneCrossCost, m_ParkingCost, m_SpawnCost, m_ForbiddenCost }` |
| Pedestrian | `PathfindPedestrianData { m_WalkingCost, m_CrosswalkCost, m_UnsafeCrosswalkCost, m_SpawnCost }` |
| Connections | `PathfindConnectionData { m_BorderCost, m_PedestrianBorderCost, m_DistanceCost, m_AirwayCost, m_InsideCost, m_AreaCost, m_CarSpawnCost, m_BicycleSpawnCost, m_PedestrianSpawnCost, m_HelicopterTakeoffCost, m_AirplaneTakeoffCost, m_TaxiStartCost, m_ParkingCost, m_BicycleParkingCost }` |
| Track | `PathfindTrackData { m_DrivingCost, m_TwowayCost, m_SwitchCost, m_DiamondCrossingCost, m_CurveAngleCost, m_SpawnCost }` |
| Transport | `PathfindTransportData { m_OrderingCost, m_StartingCost, m_TravelCost }` |

`PathUtils.GetCarDriveSpecification` bakes them onto the edge, and the conditions are the mechanism: driving cost scales by length, curve-angle cost by the angle between the bezier's end tangents projected flat — an S-curve nets toward zero, and slope never counts — and lane-cross cost by `CarLane.m_LaneCrossCount`.
Only when `Game.Net.CarLaneFlags.Approach` is absent, it adds the forbidden cost on a `Forbidden` lane or an `Unsafe` highway U-turn, the turning cost on any turn flag, and the U-turn cost — unsafe variant on an `Unsafe` lane — on a U-turn.
The same method derives the edge's rules from lane flags — `PublicOnly` → `ForbidPrivateTraffic`, `Highway` → `ForbidSlowTraffic`, a shared car-and-bicycle lane with no bike-only lane in its group → `AvoidBicycles` (`src/Game/Game.Pathfind/PathUtils.cs`).
`RuleFlags` in full: `HasBlockage`, `ForbidCombustionEngines`, `ForbidTransitTraffic`, `ForbidHeavyTraffic`, `ForbidPrivateTraffic`, `ForbidSlowTraffic`, `AvoidBicycles` (`src/Game/Game.Pathfind/RuleFlags.cs`); an agent's `m_IgnoredRules` is masked out before pricing, with `m_TaxiIgnoredRules` substituted on any edge whose methods include `PathMethod.Taxi`.
`PathMethod` in full: `Pedestrian`, `Road`, `Parking`, `PublicTransportDay`, `Track`, `Taxi`, `CargoTransport`, `CargoLoading`, `Flying`, `PublicTransportNight`, `Boarding`, `Offroad`, `SpecialParking`, `MediumRoad`, `Bicycle`, `BicycleParking` (`src/Game/Game.Pathfind/PathMethod.cs`).

**A `PathfindCostInfo` initializer in `CarPathfind.cs` is not the shipped cost, and there is no single shipped cost per field.**
The shipped pathfind prefabs override some fields and keep others, differently per prefab, so cite the component and read the prefab live — `ecs_query` on `Game.Prefabs.PathfindCarData` enumerates them.
Source: `src/Game/Game.Prefabs/CarPathfind.cs`, `src/Game/Game.Prefabs/PathfindCarData.cs`.

(VOLATILE: every component, field, enum, flag member, system, method and `Source:` path this file names — their declarations under `src/Game/` in `Game.Pathfind`, `Game.Prefabs`, `Game.Net`, `Game.Vehicles` and `Game.Simulation`, at the files cited beside each; the two listings, against `PathfindJobs.cs` and `PathUtils.cs`.)
