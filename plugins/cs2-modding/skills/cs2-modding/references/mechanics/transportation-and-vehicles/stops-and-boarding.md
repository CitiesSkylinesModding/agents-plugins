# Stops, boarding and fares

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

## The stop's state is derived every tick

Sources: `src/Game/Game.Simulation/TransportStopSystem.cs`, `src/Game/Game.Simulation/TransportStationAISystem.cs`, `src/Game/Game.Prefabs/TransportStation.cs`.

`TransportStopSystem` (`GameSimulation`, interval 256) recomputes three fields of every `Game.Routes.TransportStop` from the prefab and the owning station:

```
comfort = saturate(TransportStopData.m_ComfortFactor)
loading = max(0, 1 + TransportStopData.m_LoadingFactor)
if a TransportStation owns the stop (the first Owner carrying one, stepping
                                     exactly one hop further when that owner's
                                     own owner is also a station):
    comfort = saturate(comfort + (1 - comfort) * station.m_ComfortFactor)
    loading = max(0, loading * station.m_LoadingFactor)
    active  = station has TransportStationFlags.TransportStopsActive
else:
    active  = true
when any of the three changed: PathfindUpdated on every ConnectedRoute waypoint,
                               and on the stop itself when it is a taxi stand
```

**Comfort composes as a diminishing blend and loading as a product** — that shape is the mechanism; the magnitudes are prefab values.
The station half comes from `TransportStationAISystem` (interval 256), with the prefab data first combined over installed upgrades by `UpgradeUtils.CombineStats`:

```
station.m_ComfortFactor = saturate(prefab.m_ComfortFactor * efficiency)
station.m_LoadingFactor = max(0, (1 + prefab.m_LoadingFactor) * efficiency)
efficiency > 0 ? copy the four refuel masks and set TransportStopsActive
               : zero the four masks and clear it
then: active and offering any refuelling, but zero connected lines
      -> clear TransportStopsActive again
```

**A refuelling station with no line through it deactivates its own stops.**
`BuildingUtils.GetNumberOfConnectedLines` returning zero clears the flag the first branch just set, so connect a line before expecting such a station's stops to work.
Source: `src/Game/Game.Simulation/TransportStationAISystem.cs`.

**The instance `TransportStop.m_ComfortFactor` and `m_LoadingFactor` are derived, so `TransportStopData` is only the seed.**
A reader who reads the prefab component expecting the operative number has read the input of the blend above, not its output; read the instance component.
Source: `src/Game/Game.Simulation/TransportStopSystem.cs`, `src/Game/Game.Prefabs/TransportStopData.cs`.

## The published wait is an estimator, not a counter

`WaitingPassengers { m_Count, m_OngoingAccumulation, m_ConcludedAccumulation, m_SuccessAccumulation, m_AverageWaitingTime }` sits on the waypoint, and only `m_Count` means what its name says (`src/Game/Game.Routes/WaitingPassengers.cs`).
`WaitingPassengersSystem` (`GameSimulation`, interval 256) runs three jobs (`src/Game/Game.Simulation/WaitingPassengersSystem.cs`):

```
clear: m_Count = 0, m_OngoingAccumulation = 0 on every waypoint
count: per resident at a transport stop, or queued with a positive target radius:
    m_Count               += 1 + groupCreatures.Length
    m_OngoingAccumulation += resident.m_Timer * that * (2/15)   // timer units to seconds
tick:
    1 chance in 64: m_SuccessAccumulation++ (capped at 65535)
    quotients = (m_OngoingAccumulation, m_ConcludedAccumulation)
                ceil-divided by max(1, (m_Count, m_SuccessAccumulation))
    avg = min(65535, cmax(quotients) rounded down to a multiple of 5)
    if avg != m_AverageWaitingTime: PathfindUpdated on the waypoint
    decay = (m_SuccessAccumulation + random(0..255)) >> 8
    m_ConcludedAccumulation -= decay * quotients.y   (floored at 0)
    m_SuccessAccumulation   -= decay
    m_AverageWaitingTime     = avg
```

The published wait is the larger of two estimates — time per waiting passenger and time per successful boarding — quantised to five seconds, and the quantisation is what stops it re-stamping `PathfindUpdated` every tick.
`ResidentAISystem` feeds `m_ConcludedAccumulation` three ways — a boarding, a timed-out wait, and a pathfind-time estimate — and only the boarding also adds a success, so the other two raise the average unopposed and the 1-in-64 free success is what keeps it decaying at a stop nobody uses.

## Boarding and the fare

Sources: `src/Game/Game.Simulation/ResidentAISystem.cs`, `src/Game/Game.Simulation/TransportCarAISystem.cs`.

Capacity is enforced on the boarding resident, not on the vehicle: `ResidentAISystem.GetFreeSpace` computes `capacity - (passengers flagged InVehicle, each counting itself plus pending group members)`, taking capacity from `PublicTransportVehicleData`, `TaxiData` or `PersonalCarData`, falling back to `1000000` when a vehicle with a `Passenger` buffer carries none of the three, and returning zero for a vehicle with no `Passenger` buffer at all.
`TryFindVehicle` scores each car of a consist with a group leader's score capped at the space the group needs, so among cars that fit the group the nearest wins and room only separates cars that do not fit; a following member scores a small preference for the leader's car and a car with room, and is never refused.
**The whole-vehicle verdict is per capacity class: a `PublicTransportVehicleData` vehicle accepts on total free space, while `TaxiData` and `PersonalCarData` accept only a completely empty vehicle** — so raising a taxi's capacity still yields single-fare cabs.
Two door-side gates ride on `Game.Vehicles.PublicTransport` and `Taxi`: a resident farther than `m_MaxBoardingDistance` does not board and instead lowers `m_MinWaitingDistance`, and both reset at each boarding; `PublicTransportFlags.RequireStop` is a passenger's request that the vehicle stop — set by a rider wanting off, or by a waiting resident asking a testing vehicle to halt.

**`PublicTransportFlags.Full` is not the line-capacity flag.**
It is set in one place, only for a vehicle flagged `Evacuating` or `PrisonerTransport`, cleared there and again by the parking path's state mask, and read by the evacuation and prisoner dispatch and pathfind setups; a full bus on an ordinary line never carries it — it simply fails `GetFreeSpace` at the door.
Source: `src/Game/Game.Simulation/TransportCarAISystem.cs`, `src/Game/Game.Simulation/EvacuationDispatchSystem.cs`.

The line fare is charged at boarding, from the household, to the city: `FinishEnterVehicle` snapshots `GetTicketPrice(vehicle)` — walking the vehicle's `CurrentRoute` to the line's `TransportLine.m_TicketPrice`, zero when either is missing — and the boarding job moves that price out of the household's `Resource.Money`, adds it to `PlayerMoney`, and enqueues a `ServiceFeeSystem.FeeEvent` under `PlayerResource.PublicTransport` — **so the fare is per ride rather than per distance, it is a `ushort`, and a vehicle carrying no line at the door rides free.**
`ExitVehicle` charges a different price: `Taxi.m_CurrentFee`, read only for a group `Leader` in a vehicle carrying `Taxi` — the taxi meter, never the line fare — and for a cab flagged `FromOutside` the fee is negated, so the household still pays while the city credits nothing and no fee event is raised.
**`GetTicketPrice` reads `CurrentRoute` off the boarded car, and every car of a train, tram or subway — the route-carrying head included — sits in one `LayoutElement` buffer `TryFindVehicle` picks from, so on a multi-car rail consist only the riders who land on the head car pay.**
The call is exact everywhere else — a bus, taxi, aircraft or ship has no layout buffer, so the passenger boards the routed entity itself; the caller holds the climbed controller, but the boarding job spends it on the usage statistics rather than on the price (`src/Game/Game.Prefabs/TransportVehicleSelectData.cs`).

## Cargo loading is a storage transfer

`TransportBoardingHelpers.TransportBoardingJob` handles both payloads through one queue (`src/Game/Game.Simulation/TransportBoardingHelpers.cs`).
`BeginBoarding` refuses while another vehicle is `Boarding` at the stop, claims `BoardingVehicle.m_Vehicle`, updates the waypoint's `VehicleTiming`, sets `Boarding` (plus `Refueling` when the station refuels this vehicle, [depots-and-dispatch.md](depots-and-dispatch.md)), then clears `LoadingResources` and runs `UnloadResources` followed by `LoadResources`; `EndBoarding` releases the stop first, then runs one more `LoadResources`.
`UnloadResources` moves every `Economy.Resources` element from the vehicle — or from each `LayoutElement` car — into the target's buffer; `LoadResources` fills from the source's outgoing `StorageTransferRequest`s aimed at the next station; `LoadingResources` is the pending-load manifest, and its real reader is the simulation — the transit vehicle AI systems read it back to stock dummy-traffic vehicles with actual `Economy.Resources`, bounded by the payload's capacity and resource count.
The moved amount climbs the stop's `Owner` chain to the first `Game.Buildings.CargoTransportStation` and lands in `m_WorkAmount` — **a cargo terminal's workload is tonnage moved, not vehicles arrived** — and the consumer multiplies it by the prefab's `CargoTransportStationData.m_WorkMultiplier` (`src/Game/Game.Simulation/AreaLotSimulationSystem.cs`).
A cargo terminal prefab with zero `transports` still gets `StorageCompany`, `TradeCost`, `StorageTransferRequest` and `Economy.Resources` and only conditionally `TransportCompany` — a terminal with no vehicles of its own is a warehouse with a stop on it (`src/Game/Game.Prefabs/CargoTransportStation.cs`).
`economy-and-companies` owns the storage graph those requests come from.

**The transported-passenger and transported-cargo counters cover rail, sea and air only.**
The passenger enqueues in `ResidentAISystem` are themselves gated on the three types, on a non-outside-connection destination, and on the run being neither evacuation nor prisoner transport; the cargo enqueues in `TransportBoardingHelpers` fire for every mode short of an outside-connection at the end being credited — the unload's target, the load's source, and `TransportUsageTrackSystem`'s defaultless three-case switch is what lands the rest as rows of zeroes — `TransportRequirementData` unlocks gate on these counters (`city-state-and-progression` owns the unlock half).
Source: `src/Game/Game.Simulation/TransportUsageTrackSystem.cs`, `src/Game/Game.Simulation/TransportUsageData.cs`, `src/Game/Game.Simulation/ResidentAISystem.cs`, `src/Game/Game.Simulation/TransportBoardingHelpers.cs`.

(VOLATILE: every component, field, enum, system, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Routes`, `Game.Vehicles`, `Game.Prefabs`, `Game.Simulation`, `Game.Buildings` and `Game.Economy`, at the files cited beside each claim.)
