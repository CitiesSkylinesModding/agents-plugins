# The vehicle as an object

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`VehiclePrefab` adds `VehicleData` — one bone index, no gameplay state — to the prefab, and the zero-size `Game.Vehicles.Vehicle` tag, `Color` and `Surface` to the instance (`src/Game/Game.Prefabs/VehiclePrefab.cs`, `VehicleData.cs`, `src/Game/Game.Vehicles/Vehicle.cs`).
Everything a reader would call the vehicle's stats lives on one *physical* component chosen by body kind plus one *payload* component chosen by role, and the service fleet uses this same machinery.

## The body

| Body | Prefab class | Component | Fields |
| --- | --- | --- | --- |
| road | `CarBasePrefab` → `CarPrefab` | `CarData` | `m_SizeClass`, `m_EnergyType`, `m_MaxSpeed`, `m_Acceleration`, `m_Braking`, `m_PivotOffset`, `m_Turning` |
| rail | `TrainPrefab` | `TrainData` | `m_TrackType`, `m_EnergyType`, `m_TrainFlags`, `m_MaxSpeed`, `m_Acceleration`, `m_Braking`, `m_Turning`, `m_BogieOffsets`, `m_AttachOffsets` |
| water | `WatercraftPrefab` | `WatercraftData` | `m_SizeClass`, `m_EnergyType`, `m_MaxSpeed`, `m_Acceleration`, `m_Braking`, `m_Turning`, `m_AngularAcceleration` |
| air | `AircraftPrefab` | `AircraftData` | `m_SizeClass`, `m_GroundMaxSpeed`, `m_GroundAcceleration`, `m_GroundBraking`, `m_GroundTurning` |

(`src/Game/Game.Prefabs/CarData.cs`, `TrainData.cs`, `WatercraftData.cs`, `AircraftData.cs` and the prefab classes beside them.)

**`m_MaxSpeed` is authored in km/h and stored in m/s, and `m_Turning` is degrees on the class and radians on the component.**
`VehicleInitializeSystem` and the watercraft and aircraft prefab classes divide by `3.6f` and call `math.radians` while baking, so a reader querying `CarData.m_MaxSpeed` expecting km/h is out by 3.6; `AircraftData` has no energy type and its speed field is `m_GroundMaxSpeed` — the air speed sits on a second component beside it, `HelicopterData.m_FlyingMaxSpeed` or `AirplaneData.m_FlyingSpeed` (a min/max pair), added by `HelicopterPrefab` and `AirplanePrefab` and baked through the same `3.6f`.
Source: `src/Game/Game.Prefabs/VehicleInitializeSystem.cs`, `src/Game/Game.Prefabs/WatercraftPrefab.cs`, `src/Game/Game.Prefabs/AircraftPrefab.cs`, `src/Game/Game.Prefabs/HelicopterPrefab.cs`, `src/Game/Game.Prefabs/AirplanePrefab.cs`.

## The payload

A transit or taxi vehicle's capacity lives on one of three components: `PublicTransportVehicleData { m_TransportType, m_PassengerCapacity, m_PurposeMask, m_MaintenanceRange }`, `CargoTransportVehicleData { m_Resources, m_CargoCapacity, m_MaxResourceCount, m_MaintenanceRange }`, `TaxiData { m_PassengerCapacity, m_MaintenanceRange }` (`src/Game/Game.Prefabs/`, one file each).
`m_MaxResourceCount` is the mechanism a reader will not guess: it caps how many *distinct* resources one vehicle carries at once, independently of `m_CargoCapacity`, and `m_Resources` is a `Resource` mask restricting which — a specialised cargo car is a mask of one and a count of one.
`m_MaintenanceRange` is authored in kilometres on the class and multiplied by `1000f` into the component ([depots-and-dispatch.md](depots-and-dispatch.md) owns the loop it feeds).
`PublicTransportPurpose` — `TransportLine`, `Evacuation`, `PrisonerTransport`, `Other` — on `m_PurposeMask` is what separates the transit fleet from the evacuation and prisoner fleets sharing these components; a bus authored `Evacuation` alone can never be assigned to a line, though the mask is flags and a prefab may carry both (`src/Game/Game.Prefabs/PublicTransportPurpose.cs`).
A service vehicle's payload component is its own, added the same way — `Ambulance` adds `AmbulanceData { m_PatientCapacity }`, `GarbageTruck` adds `GarbageTruckData { m_GarbageCapacity, m_UnloadRate }` — one `ComponentBase` per role under `src/Game/Game.Prefabs/`, each `Initialize` copying the class's authored capacity into the component of the same name, so a role's field is read off that class rather than guessed from the transit three.
`Game.Vehicles.PassengerTransport` is a bare tag carrying nothing (`src/Game/Game.Vehicles/PassengerTransport.cs`).

## The runtime state

Beside `Game.Vehicles.PublicTransport { m_TargetRequest, m_State, m_DepartureFrame, m_RequestCount, m_PathElementTime, m_MaxBoardingDistance, m_MinWaitingDistance }`, its near-twin `CargoTransport` lacking only the two boarding-distance fields, or `Taxi` (`src/Game/Game.Vehicles/PublicTransport.cs`, `CargoTransport.cs`, `Taxi.cs`), a transit vehicle carries:

- `CurrentRoute { m_Route }` — added and removed rather than nulled; **absence, not a flag, is what "not assigned to a line" means** (`src/Game/Game.Routes/CurrentRoute.cs`).
- `Passenger { m_Passenger }`, `[InternalBufferCapacity(0)]` and `IEmptySerializable` — rebuilt on load (`src/Game/Game.Vehicles/Passenger.cs`).
- `Odometer { m_Distance }`, and `Produced { m_Completed }` on a vehicle still being built at a producing depot (`src/Game/Game.Vehicles/Odometer.cs`, `Produced.cs`).
- `LoadingResources` and `ReturnLoad` on the cargo side (`src/Game/Game.Vehicles/LoadingResources.cs`, `ReturnLoad.cs`).
- `Controller { m_Controller }` and the `LayoutElement { m_Vehicle }` buffer for a consist: the front unit owns the layout and each car points back — and `LayoutElement` is `ISerializable` while `Controller`, `Passenger`, `OwnedVehicle` and `GuestVehicle` are `IEmptySerializable`, so **only one side of each link is saved and the other is rebuilt in `SystemUpdatePhase.Deserialize`** — `ControllerSystem` from `LayoutElement`, `PassengerSystem` from the passengers' own `CurrentVehicle`/`CurrentTransport`, `OwnedVehicleSystem` from `Owner`, `GuestVehicleSystem` from `Target`, and writing a rebuilt side without the saved side it is derived from loses the edit on load (`src/Game/Game.Vehicles/Controller.cs`, `LayoutElement.cs`, `src/Game/Game.Common/SystemOrder.cs`).
- `OwnedVehicle { m_Vehicle }` on the depot or company owning it, `GuestVehicle` on a building it merely visits (`src/Game/Game.Vehicles/OwnedVehicle.cs`, `GuestVehicle.cs`).
- `ParkedCar { m_Lane, m_CurvePosition }` or `ParkedTrain { m_ParkingLocation, m_FrontLane, m_RearLane, m_CurvePosition }` when stopped — a parked road vehicle is addressed as a lane entity plus a curve parameter, [`roads-and-traffic`](../roads-and-traffic/roads-and-traffic.md)'s convention (`src/Game/Game.Vehicles/ParkedCar.cs`, `ParkedTrain.cs`).
- The body-specific navigation components (`CarNavigation`, `CarCurrentLane` and their rail, water and air counterparts) beside `PathOwner`, `PathElement`, `Target` and `Blocker` — [`roads-and-traffic`](../roads-and-traffic/roads-and-traffic.md)'s territory, except that rail alone puts the path components on the `LayoutElement` holder rather than on every unit (`src/Game/Game.Prefabs/TrainPrefab.cs`).

**Two different enums are both named `TrainFlags`, and they share the member name `Pantograph` at different bit values.**
`Game.Vehicles.TrainFlags` is runtime state — `Reversed`, `BoardingLeft`, `BoardingRight`, `Pantograph`, `IgnoreParkedVehicle` — while `Game.Prefabs.TrainFlags` is authoring, `MultiUnit` and `Pantograph`; importing the wrong one is a silently wrong bit test.
Source: `src/Game/Game.Vehicles/TrainFlags.cs`, `src/Game/Game.Prefabs/TrainFlags.cs`.

`Game.Vehicles.CarFlags`' two transit members, `UsePublicTransportLanes` and `PreferPublicTransportLanes`, are how a bus is allowed onto a public-transport-only lane; the declaration carries the full set (`src/Game/Game.Vehicles/CarFlags.cs`).

## Noise and pollution

The per-vehicle emission model is one lerp; the car and train systems carry it whole, the watercraft and aircraft ones without the lane-length normalisation (`src/Game/Game.Simulation/CarNavigationSystem.cs`, `TrainNavigationSystem.cs`, `WatercraftNavigationSystem.cs`, `AircraftNavigationSystem.cs`):

```
achievedSpeed = laneDuration == 0
              ? min(scaled lane speed limit, the prefab's cornering limit for the lane's curviness)
              : distance / laneDuration
ratio       = saturate((achievedSpeed / prefab max speed)^2)
sideEffects = lerp(VehicleSideEffectData.m_Min, VehicleSideEffectData.m_Max, ratio)
sideEffects *= (min(1, distance / max(1, laneLength)), duration, duration)   // car, train
sideEffects *= (distance, duration, duration)                                // watercraft, aircraft
```

`VehicleSideEffectData { m_Min, m_Max }` packs `(roadWear, noisePollution, airPollution)` per axis, filled by `VehicleSideEffects.Initialize`; the `.x` becomes lane wear on cars and trains — watercraft and aircraft write only the pollution pair — and the `.yz` become net pollution; [`roads-and-traffic`](../roads-and-traffic/roads-and-traffic.md) and [`environment-and-pollution`](../environment-and-pollution/environment-and-pollution.md) own the consumers (`src/Game/Game.Prefabs/VehicleSideEffectData.cs`, `VehicleSideEffects.cs`).

**The aircraft system's `HelicopterData` and `AirplaneData` overloads zero the accumulators and emit nothing; only its `AircraftData` arm emits.**
`CalculateNoise` below exists on the car system alone, so only road traffic feeds the ambience layer.
Source: `src/Game/Game.Simulation/AircraftNavigationSystem.cs`, `src/Game/Game.Simulation/CarNavigationSystem.cs`.

**No emission path reads `m_EnergyType`: whether an electric vehicle emits less is authored per prefab, not a code rule.**
On this install the electric taxi is authored quieter and cleaner than the fuel taxi while the electric bus is authored identical to the fuel bus — a mod expecting electric to mean cleaner authors that itself, and one patching the emission code to branch on energy type patches a branch that does not exist; the re-check is `ecs_query` on `Game.Prefabs.VehicleSideEffectData` beside the body component, both read live.
Source: `src/Game/Game.Simulation/CarNavigationSystem.cs`, `src/Game/Game.Prefabs/VehicleSideEffects.cs`.

**`CalculateNoise` reads the air-pollution axis, and its result is audio rather than pollution.**
It lerps `m_Min.z` to `m_Max.z` — the axis `Initialize` fills from `m_AirPollution`, not `m_NoisePollution` — and its one caller enqueues the result as a `TrafficAmbienceEffect`, the ambient sound layer; a prefab's authored air-pollution factor is what drives how loud it is.
Source: `src/Game/Game.Simulation/CarNavigationSystem.cs`, `src/Game/Game.Prefabs/VehicleSideEffects.cs`.

(VOLATILE: every component, field, enum, system, method and `Source:` path this file names — their declarations under `src/Game/` in `Game.Vehicles`, `Game.Prefabs`, `Game.Routes`, `Game.Simulation`, `Game.Serialization` and `Game.Common`, at the files cited beside each claim; plus the electric-against-fuel authoring shape, against the running game's prefab set, re-derived by the `ecs_query` stated beside it.)
