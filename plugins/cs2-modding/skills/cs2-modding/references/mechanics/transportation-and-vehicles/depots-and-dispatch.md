# Depots, dispatch and the taxi

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Three systems, in registration order within `GameSimulation`: `TransportDepotAISystem`, `TransportVehicleDispatchSystem`, `TransportLineSystem` (`src/Game/Game.Common/SystemOrder.cs`).

## What a depot declares

`TransportDepot : ComponentBase, IServiceUpgrade` copies `m_TransportType`, `m_EnergyTypes`, `m_SizeClass`, `m_DispatchCenter`, `m_VehicleCapacity`, `m_ProductionDuration` and `m_MaintenanceDuration` into `TransportDepotData`, rewrites an `Undefined` size class in C# — `Small` for `TransportType.Taxi`, `Large` otherwise — and gives the building `UpdateFrameData(2)`; a taxi depot alone also gets `ServiceDistrict` (`src/Game/Game.Prefabs/TransportDepot.cs`, `TransportDepotData.cs`).
The instance half is `Game.Buildings.TransportDepot { m_TargetRequest, m_Flags, m_AvailableVehicles, m_MaintenanceRequirement }` with `TransportDepotFlags` = `HasAvailableVehicles`, `HasDispatchCenter` (`src/Game/Game.Buildings/TransportDepot.cs`).

**Upgrades combine additively for the numbers and by OR for the energy mask and the dispatch-centre flag, and `m_TransportType` and `m_SizeClass` do not combine at all.**
`TransportDepotData.Combine` runs `m_EnergyTypes |=`, `m_DispatchCenter |=`, `m_VehicleCapacity +=`, `m_ProductionDuration +=`, `m_MaintenanceDuration +=` and nothing else, which is why an upgrade prefab declares `m_TransportType = None`: it contributes only the combinable fields, and "upgrade the depot to run electric buses" is that `|=` on `m_EnergyTypes` plus the selection filter below.
Source: `src/Game/Game.Prefabs/TransportDepotData.cs`.

## The depot tick

`TransportDepotAISystem.Tick`, interval 256 (`src/Game/Game.Simulation/TransportDepotAISystem.cs`, `src/Game/Game.Buildings/BuildingUtils.cs`):

```
capacity = BuildingUtils.GetVehicleCapacity(min(efficiency, immediateEfficiency), prefab.m_VehicleCapacity)
         = select(0, clamp(int(efficiency * capacity), 1, capacity), efficiency > 0.001 && capacity > 0)      // any efficiency -> at least one
walk OwnedVehicle:
    DummyTraffic vehicle          -> skipped entirely (the two transit arms; the taxi arm carries no such test)
    parked, odometer nonzero      -> odometer and RequiresMaintenance cleared, backlog add attempted (trap below)
    parked, lane or location gone -> vehicle deleted
    out of the depot              -> takes a slot; Disabled toggled against a second capacity scaled by immediate efficiency alone
if m_MaintenanceDuration > 0:
    m_MaintenanceRequirement -= 256 / (262144 * m_MaintenanceDuration) * efficiency, floored at 0
    free slots -= ceil(m_MaintenanceRequirement - 0.001)
m_ProductionDuration > 0 -> advance Produced { m_Completed } on the one vehicle carrying it, spawning one to carry it when none does and a free slot exists (it takes that slot); on completion raise the VehicleLaunchData event of this transport type (the space program's path) -- both spawn and event are skipped while the building carries Game.Events.SpectatorSite, which the launch event it raised puts there from its preparation through its termination window
drain ServiceDispatch: spawn at most one vehicle this tick for a dispatched TransportVehicleRequest or TaxiRequest; a surviving surplus request burns one free slot per loop pass before it is dropped, so a tick with any surplus ends at zero free slots
cull parked vehicles at random while parked > max(0, prefab.m_VehicleCapacity - outCount)
m_AvailableVehicles = clamp(free slots, 0, 255)
free slots > 0 -> set HasAvailableVehicles and, when m_TargetRequest no longer holds a live ServiceRequest, file a reversed request (TaxiRequest at RequestGroup(16) for a taxi depot, else TransportVehicleRequest(depot, available / capacity) at RequestGroup(8))
HasDispatchCenter = m_DispatchCenter && efficiency > 0.001
```

**A maintenance backlog eats vehicle slots, so a fleet shrinks without the player changing anything**; `262144` is the simulation day in frames and `256` the update interval, making the divisor days of maintenance work.
**The parked-vehicle cull uses the raw prefab capacity, not the efficiency-scaled one** — a depot at low efficiency disables vehicles but does not delete its parked fleet.

**The tick's own odometer-to-backlog add looks the maintenance range up through the depot's `PrefabRef`, not the parked vehicle's, and no vanilla depot prefab carries the payload components.**
The `[ComponentMenu]` filters put `Taxi`, `PublicTransport` and `CargoTransport` on vehicle prefab classes and `TransportDepot` on building ones — an editor filter, not a runtime guard, so a mod adding one of the three data components to a depot prefab makes the add fire — and that lookup misses on every vanilla depot, so the parked vehicle's odometer and `RequiresMaintenance` are cleared without feeding the backlog; the conversion that keys on the vehicle's own prefab runs in `Game.Vehicles.ReferencesSystem`, when a depot-owned vehicle carrying an `Odometer` is deleted.
Source: `src/Game/Game.Simulation/TransportDepotAISystem.cs`, `src/Game/Game.Vehicles/ReferencesSystem.cs`, `src/Game/Game.Prefabs/Taxi.cs`, `src/Game/Game.Prefabs/TransportDepot.cs`.

## Matching a vehicle to a line

`TransportVehicleDispatchSystem` (interval 16) is a two-sided pathfinding matcher rather than a search (`src/Game/Game.Simulation/TransportVehicleDispatchSystem.cs`):

- A line's request runs `FindVehicleSource`: origin `SetupTargetType.TransportVehicle`, destination `SetupTargetType.RouteWaypoints` with the route as the entity, weights `(1, 1, 1, 1)`, path methods from `RouteUtils.GetPathMethods` over the line prefab's `RouteConnectionData`, and five ignored rules — `ForbidCombustionEngines | ForbidHeavyTraffic | ForbidPrivateTraffic | ForbidSlowTraffic | AvoidBicycles`.
- A depot's reversed request runs `FindVehicleTarget`, deriving methods through a defaultless hardcoded switch on `TransportDepotData.m_TransportType`: the three rail modes read their track type directly, the rest build a synthetic `CarData` / `WatercraftData` / `AircraftData` carrying only the depot's size class — an unlisted transport type keeps the pre-switch zero path methods, so its request pathfinds against nothing and fails silently.
- The match lands as a `ServiceDispatch` appended on the source, which the depot tick above turns into a spawn.

So a vehicle reaches a line the same way a service vehicle reaches an incident — `ServiceRequest` plus a payload request component, matched by the pathfinder into a `ServiceDispatch` buffer; `city-services-and-coverage` owns that machinery, and `TransportVehicleRequest { m_Route, m_Priority }` and `TaxiRequest` are two more members of its family.
**Putting a vehicle on a line, or taking one off, is therefore a request rather than a write**: hand-writing `CurrentRoute` and the `RouteVehicle` buffer skips the depot's slot accounting and the odometer-and-maintenance loop.

Which prefab spawns is `TransportVehicleSelectData`'s: `CreateVehicle` matches on transport type, size class (track type instead, on the rail modes), `PublicTransportPurpose` and cargo resources, rejects any car, train or watercraft candidate whose `m_EnergyType` is declared and does not intersect the depot's combined mask — the airplane, helicopter and rocket arms never read an energy type, since `AircraftData` declares none, and the helicopter arm filters on `HelicopterData.m_HelicopterType` instead — and builds a `Controller` + `LayoutElement` consist for the three rail modes (`src/Game/Game.Prefabs/TransportVehicleSelectData.cs`).

## Maintenance, refuelling and the energy model

The whole model is four moving parts, and no fuel gauge exists.

1. `Odometer { m_Distance }` accumulates on every transit vehicle (`src/Game/Game.Vehicles/Odometer.cs`).
2. The threshold is `m_MaintenanceRange` on `PublicTransportVehicleData`, `CargoTransportVehicleData` or `TaxiData`, and every test requires `m_MaintenanceRange > 0.1f`, so a zero range disables the mechanic for that prefab (`src/Game/Game.Simulation/TransportCarAISystem.cs`).
3. Crossing it sets `RequiresMaintenance`, but only while `Refueling` is clear — except on the taxi, whose flag set has no `Refueling` member at all.
4. At the next stop the owning station decides: the car, train and watercraft AIs test their mode's refuel mask on the station's live `Game.Buildings.TransportStation` against the vehicle's `m_EnergyType`, while the aircraft AI tests its mask against `EnergyTypes.Fuel` since `AircraftData` carries no energy type — a pass clears `RequiresMaintenance` and boarding begins with `Refueling` set; a fail with `RequiresMaintenance` set clears `EnRoute` and **removes `CurrentRoute` outright**, sending the vehicle back to its depot (`src/Game/Game.Simulation/TransportCarAISystem.cs`, `TransportTrainAISystem.cs`, `TransportWatercraftAISystem.cs`, `TransportAircraftAISystem.cs`).

`TransportStation` declares the four masks — `m_CarRefuelTypes`, `m_TrainRefuelTypes`, `m_WatercraftRefuelTypes`, `m_AircraftRefuelTypes` — and its `Initialize` ORs them into `TransportStationData` rather than assigning, so a prefab carrying both `TransportStation` and `CargoTransportStation` merges the two declarations; an upgrade's masks reach the building instead through `TransportStationData.Combine`, applied by `UpgradeUtils.CombineStats` in `TransportStationAISystem` (`src/Game/Game.Prefabs/TransportStation.cs`, `TransportStationData.cs`, `src/Game/Game.Simulation/TransportStationAISystem.cs`).
That tick writes the combined masks onto the building's `Game.Buildings.TransportStation` — what the vehicle AIs read — and assigns `EnergyTypes.None` to all four when efficiency is not above zero, so an unpowered or unstaffed station refuels nothing.
`EnergyTypes` — `Fuel`, `Electricity`, `FuelAndElectricity`, `None` — is the entire energy model: a compatibility mask between vehicle, depot and station, with distance-since-maintenance the only thing that accumulates (`src/Game/Game.Vehicles/EnergyTypes.cs`).

## The taxi

`TransportType.Taxi` has no line prefab; what it has instead:

- `TaxiStand { m_TaxiRequest, m_Flags, m_StartingFee }` on the stand object, which also carries the waypoint's half — `AccessLane`, `RouteLane`, `WaitingPassengers` — plus `BoardingVehicle`, `RouteVehicle` and `DispatchedRequest` (`src/Game/Game.Routes/TaxiStand.cs`, `src/Game/Game.Prefabs/TransportStop.cs`).
- `Game.Vehicles.Taxi` state with `TaxiFlags`, where `FromOutside` marks a cab spawned at a road outside connection — the road connection is itself a taxi depot ([outside-connections.md](outside-connections.md)).
- `RouteUtils.GetMaxTaxiCount(waitingPassengers) = 3 + (m_Count + 3 >> 2)` — a base of three plus a quarter of the queue, rounded up (`src/Game/Game.Routes/RouteUtils.cs`).
- `TaxiRequestType` is `Stand`, `Customer`, `Outside` and a `None` sentinel a city depot's own reversed request carries, and every `Customer` request is skipped while the candidate depot lacks `TransportDepotFlags.HasDispatchCenter` — so a city depot without the dispatch-centre upgrade picks up only at stands, and a dispatch centre in a depot with no efficiency stops working; `Outside` is gated on the outside connection and `TaxiFlags.FromOutside` instead, never on the dispatch centre, and the same tests bar `FromOutside` cabs from `Stand` requests — they serve citizens hailing at the connection lane and no stand (`src/Game/Game.Simulation/TransportPathfindSetup.cs`).

(VOLATILE: every component, field, enum, system, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Routes`, `Game.Vehicles`, `Game.Prefabs`, `Game.Simulation`, `Game.Buildings` and `Game.Common`, at the files cited beside each claim.)
