# Parking

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

A parking lane enters the pathfind graph as a zero-length edge, its capacity smuggled through two fields named for something else.

**On a parking edge, `m_MaxSpeed` is metres of free space and `m_Density` is the fitted slot width.**
`GetParkingSpaceSpecification` builds `{ m_Length = 0, m_MaxSpeed = max(1, ParkingLane.m_FreeSpace), m_Density = VehicleUtils.GetParkingSize(...).x }` — a derived width, not `m_SlotSize.x` raw — and the search rejects a candidate when `math.any(m_ParkingSize > float2(m_Density, m_MaxSpeed))`: a fit test, which the pathfind job bypasses entirely when the request's methods include `PathMethod.Boarding` while the target seeker's copy of the same test never bypasses.
Source: `src/Game/Game.Pathfind/PathUtils.cs`, `src/Game/Game.Pathfind/PathfindJobs.cs`, `src/Game/Game.Pathfind/PathfindTargetSeeker.cs`, `src/Game/Game.Vehicles/VehicleUtils.cs`.

`ParkingLane { m_AccessRestriction, m_AdditionalStartNode, m_Flags, m_FreeSpace, m_ParkingFee, m_ComfortFactor, m_TaxiAvailability, m_TaxiFee }` is the kerbside lane; `GarageLane { m_ParkingFee, m_ComfortFactor, m_VehicleCount, m_VehicleCapacity }` is a structure's aggregate (`src/Game/Game.Net/ParkingLane.cs`, `GarageLane.cs`).
A garage repurposes the same two spec fields as an on-off switch: both a huge constant while `m_VehicleCount < m_VehicleCapacity` and the connection lane is not flagged `Disabled`, and 1 and 0 otherwise.
The lane prefab side is `ParkingLaneData { m_SlotSize, m_SlotAngle, m_SlotInterval, m_MaxCarLength, m_RoadTypes }`, whose `m_RoadTypes` decides the methods the edge offers — non-bicycle types get `Parking | Boarding`, bicycle gets `BicycleParking`, and `ParkingLaneFlags.SpecialVehicles` gets `Boarding | SpecialParking` instead (`src/Game/Game.Prefabs/ParkingLaneData.cs`, `src/Game/Game.Pathfind/PathUtils.cs`).
A `ParkingLaneFlags.VirtualLane` serves `Boarding` only, and a `ParkingLaneFlags.ParkingDisabled` lane has its free space forced to 1 — on the edge the two read alike, since the spec's `max(1, …)` floors the virtual lane's zero free space to the same 1, and only the methods tell them apart.

## Free space

Sources: `src/Game/Game.Pathfind/ParkingLaneDataSystem.cs`, `src/Game/Game.Net/NetUtils.cs`, `src/Game/Game.Vehicles/VehicleUtils.cs`.

```
ParkingLaneDataSystem.CalculateFreeSpace(curve, lane, laneData, objects, overlaps, blocked):
  VirtualLane flag                       -> 0
  laneData.m_SlotInterval != 0           // slotted: lots, angled or perpendicular kerb
    place the slot run on the curve: at the end away from a StartingLane or EndingLane flag, centred otherwise
    walk it slot by slot (NetUtils.GetParkingSlotCount / GetParkingSlotInterval, unspawned parked cars skipped)
    a slot clear of parked cars, lane overlaps and the blocked range
                                         -> return m_MaxCarLength immediately
    none clear                           -> 0
  else                                   // continuous kerb
    walk the sorted parked cars (unspawned ones skipped) and merged overlaps
    gap = distance between consecutive obstacles, minus each one's parking offsets plus one metre (VehicleUtils.GetParkingOffsets + 1), a flat half metre for an overlap or an unflagged lane end, shortened by the blocked range
                                         -> the largest gap, clamped to m_MaxCarLength when that is set
```

So a slotted lane reports a binary free-or-full dressed as a length, and a continuous lane reports a real length — a reader averaging `m_FreeSpace` across lanes is mixing the two.

## Price and comfort

The parking cost is multiplied per lane before the dot product: `m_ParkingCost.money *= m_ParkingFee`, and `m_ParkingCost.comfort *= (65535 - m_ComfortFactor) / 65535` — `m_ComfortFactor` is a `ushort` where 65535 is perfectly comfortable and costs nothing (`src/Game/Game.Pathfind/PathUtils.cs`).
A garage does the same through its own fee and comfort fields, against the connection pathfind prefab's parking costs.
The fee itself is written by `ParkingLaneDataSystem.GetParkingStats`, and building beats district: a lane whose owner chain roots in a building takes that building's `PaidParking` option and `ParkingFee` modifier and returns, while a kerbside lane — owned by its road edge — falls through to the district's pair; a mod writing `m_ParkingFee` directly is overwritten on the system's next pass (`src/Game/Game.Pathfind/ParkingLaneDataSystem.cs`).
`LanePoliciesSystem` computes no fee: it reads `DistrictOptionData` for the five-member `DistrictOption` enum — `PaidParking`, `ForbidCombustionEngines`, `ForbidTransitTraffic`, `ForbidHeavyTraffic`, `ForbidBicycles` — and tags affected lanes `PathfindUpdated` (`src/Game/Game.Areas/DistrictOption.cs`, `src/Game/Game.Pathfind/LanePoliciesSystem.cs`).

## The vehicle re-validates constantly

A car does not trust its reserved space: whenever it is not disembarking, needs no new path, and its path is not pending, failed or stuck, `PersonalCarAISystem.CheckParkingSpace` re-validates the space, and on failure picks a replacement index into the remaining path with `random(n) * (random(n) + 1) / n` over `n = min(40000, remaining)` — strongly biased toward the near end, so the car looks close to where it already is first — then tries `VehicleUtils.FindFreeParkingSpace` on a parking-lane pick, or the capacity test on a garage pick — a pick that is neither returns silently, and even a failed test re-paths (`PathFlags.Obsolete`) only when no parking lane remains between the car and the pick (`src/Game/Game.Simulation/PersonalCarAISystem.cs`).
The re-path is cheap by design: `PathfindFlags.ParkingReset` halves the heuristic, and halves edge costs from the first parking-method edge onward ([route-selection.md](route-selection.md)).

(VOLATILE: every component, field, enum, system, method and `Source:` path this file names — their declarations under `src/Game/` in `Game.Pathfind`, `Game.Net`, `Game.Prefabs`, `Game.Vehicles`, `Game.Areas` and `Game.Simulation`, at the files cited beside each; the listing, against `ParkingLaneDataSystem.cs`.)
