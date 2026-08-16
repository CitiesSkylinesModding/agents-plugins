# Transportation and vehicles

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`Game.Routes` models a transit line as a ring of entities: one route entity carrying the line's state, one waypoint entity per point visited, one segment entity per stretch between waypoints — each segment its own pathfinding request — while the stop is an ordinary world object the waypoint links to.
A vehicle is likewise an entity, owned by a depot through `OwnedVehicle` and assigned to a line by holding `CurrentRoute`; the fleet is sized, requested and shed by `TransportLineSystem` ([lines-and-fleet.md](lines-and-fleet.md)) and delivered through the same service-request machinery every city service uses ([depots-and-dispatch.md](depots-and-dispatch.md)).
Everything a mode *is* — capacities, speeds, headways, energy types, comfort — is authored prefab data on `*Data` components; the code carries only the formulas that consume them.
Citizens meet the system at the stop edge of the routing graph ([transit-routing.md](transit-routing.md)) and at the vehicle door ([stops-and-boarding.md](stops-and-boarding.md)); the vehicle itself is a stack of body, payload and state components ([vehicles.md](vehicles.md)).
The outside connection is one placed object playing several roles at once — depot, stop, storage company, workplace, school, hospital — with which roles on which mode a per-prefab shape ([outside-connections.md](outside-connections.md)).

## The map

Default reads: a prefab component is reached from an instance through `PrefabRef.m_Prefab`, or enumerated with `ecs_query` on the component itself; a runtime component sits on the entity its row names; a row states its own shape only where it differs.

The line and its entities (`src/Game/Game.Routes/` unless noted):

| The game models | Component | Access shape |
| --- | --- | --- |
| A route | `Route { m_Flags, m_OptionMask }`; `RouteType` is `TransportLine` or `WorkRoute` plus sentinels (`Route.cs`, `RouteType.cs`) | the option mask reads through `RouteUtils.CheckOption` ([lines-and-fleet.md](lines-and-fleet.md)) |
| The line's state | `TransportLine { m_VehicleRequest, m_VehicleInterval, m_UnbunchingFactor, m_Flags, m_TicketPrice }` (`TransportLine.cs`) | on the route entity; [lines-and-fleet.md](lines-and-fleet.md) |
| The line's membership | the `RouteWaypoint`, `RouteSegment`, `RouteVehicle`, `VehicleModel`, `RouteModifier` and `DispatchedRequest` buffers (one file each) | on the route entity |
| A waypoint | `Waypoint { m_Index }` plus `Position` and `Owner`; at a stop also `Connected { m_Connected }` and `VehicleTiming`; `WaitingPassengers` only when the line prefab's `m_PassengerTransport` is true (`Waypoint.cs`, `Connected.cs`, `src/Game/Game.Prefabs/TransportLinePrefab.cs`) | |
| A segment | `Segment { m_Index }` plus a `CurveElement` buffer, its own `PathElement` buffer, `PathInformation`, `RouteInfo` and `PathTargets` (`Segment.cs`, `CurveElement.cs`) | the stretch from waypoint *i* to *i + 1* |
| The stop | `TransportStop { m_AccessRestriction, m_ComfortFactor, m_LoadingFactor, m_Flags }`, a `ConnectedRoute` buffer, `BoardingVehicle`, one per-mode tag — `BusStop` and siblings (`TransportStop.cs`, `ConnectedRoute.cs`) | a world object, not a route entity; waypoint to stop through `Connected`, stop back to waypoint through `ConnectedRoute` |
| A building's routes | the `SubRoute { m_Route }` buffer (`SubRoute.cs`) | |
| The four archetypes | `RouteData { m_RouteArchetype, m_WaypointArchetype, m_ConnectedArchetype, m_SegmentArchetype }`, built by `RoutePrefab.LateInitialize` running every attached component's `GetArchetypeComponents` four times, seeded with `Route`, `Waypoint`, `Waypoint + Connected` and `Segment` (`src/Game/Game.Prefabs/RouteData.cs`) | |
| A vehicle's line | `CurrentRoute { m_Route }` (`CurrentRoute.cs`) | added and removed, never nulled: absence is what "not on a line" means |

The vehicle's buildings and the tuning surfaces:

| The game models | Component | Access shape |
| --- | --- | --- |
| The vehicle's body and payload | `CarData` / `TrainData` / `WatercraftData` / `AircraftData`, plus `PublicTransportVehicleData` / `CargoTransportVehicleData` / `TaxiData`, and `VehicleSideEffectData { m_Min, m_Max }` for road wear, noise and air pollution (`src/Game/Game.Prefabs/`) | prefab; speeds bake km/h→m/s and angles degrees→radians, so the component is not the class's figure — fields, units and runtime state in [vehicles.md](vehicles.md) |
| A depot | `TransportDepotData` on the prefab, `Game.Buildings.TransportDepot` on the building | the prefab data folds over `InstalledUpgrade` by `Combine`; [depots-and-dispatch.md](depots-and-dispatch.md) |
| A station | `TransportStationData` on the prefab, `Game.Buildings.TransportStation` on the building | comfort, loading and the four refuel masks; [stops-and-boarding.md](stops-and-boarding.md) |
| A cargo terminal | `CargoTransportStationData { m_WorkMultiplier }` on the prefab, `Game.Buildings.CargoTransportStation { m_WorkAmount }` on the building | workload is tonnage moved; [stops-and-boarding.md](stops-and-boarding.md) |
| An outside connection | `OutsideConnectionData { m_Type, m_Remoteness }` and the role components beside it | [outside-connections.md](outside-connections.md) |
| Transit routing costs | `PathfindTransportData { m_OrderingCost, m_StartingCost, m_TravelCost }` on the pathfind prefab `TransportLineData.m_PathfindPrefab` names (`src/Game/Game.Prefabs/PathfindTransportData.cs`) | [transit-routing.md](transit-routing.md) |
| The singleton | `RouteConfigurationData` (`src/Game/Game.Prefabs/RouteConfigurationData.cs`) | `GetSingleton`; the per-medium route-visualisation prefabs, the pathfind and gate-bypass notifications, `m_GateBypassEfficiency` |

The modes: `TransportType` is a C# enum plus sentinels (`src/Game/Game.Prefabs/TransportType.cs`), and which members have a line prefab is a swept shape of one install, DLC included — the sweep is `ecs_query` on `Game.Prefabs.TransportLineData`, labelled through `PrefabSystem.GetPrefabName`, then `TransportLineData` and `RouteConnectionData` read per entity, and that query is the re-check:

| `TransportType` | Lines | Access connection | Route connection | Discriminator |
| --- | --- | --- | --- | --- |
| `Bus` | passenger | `Pedestrian` | `Road` | `RoadTypes.Car` |
| `Tram` | passenger | `Pedestrian` | `Track` | `TrackTypes.Tram` |
| `Subway` | passenger | `Pedestrian` | `Track` | `TrackTypes.Subway` |
| `Train` | passenger and cargo | `Pedestrian` / `Cargo` | `Track` | `TrackTypes.Train` |
| `Ship` | passenger and cargo | `Pedestrian` / `Cargo` | `Road` | `RoadTypes.Watercraft` |
| `Ferry` | passenger | `Pedestrian` | `Road` | `RoadTypes.Watercraft` |
| `Airplane` | passenger and cargo | `Pedestrian` / `Cargo` | `Road` | `RoadTypes.Airplane` |
| `Taxi`, `Post`, `Helicopter`, `Rocket`, `Work`, `Bicycle`, `Car` | none | | | |

- A taxi is not a line: the stand carries the line's half itself ([depots-and-dispatch.md](depots-and-dispatch.md)), and `Work` is the resource-extraction work route — the same route machinery from a narrower prefab (`src/Game/Game.Prefabs/WorkRoutePrefab.cs`, `WorkStop.cs`), whose cargo belongs to `economy-and-companies`.
- No route connection is ever `Air`: ships and aircraft ride `Road` with a `RoadTypes` discriminator, and `RouteUtils.GetPathMethods` treats `Road` and `Air` alike, deriving `PathMethod.Flying` from `RoadTypes.Helicopter | RoadTypes.Airplane` rather than from the connection type (`src/Game/Game.Routes/RouteUtils.cs`).
- The line prefab's `m_SizeClass` bakes into two components: `RouteConnectionData.m_RouteSizeClass` is what `GetPathMethods` reads, adding `PathMethod.MediumRoad` at `Medium` or below, while `TransportLineData.m_SizeClass` drives the route tool's lane check and vehicle selection — the ferry is the one passenger line authored below `Large`, and a prefab-phase mod changing a mode's size class writes both.

**Passenger against cargo is three switches, and nothing else.**
One: the access connection — `Pedestrian` on a passenger line, `Cargo` on a cargo route (the table above), deciding what kind of lane joins stop to payload.
Two: the `m_PassengerTransport` / `m_CargoTransport` pair carried identically on `TransportLineData` and `TransportStopData`, which adds `WaitingPassengers` to the waypoint archetype (`TransportLinePrefab.cs`), selects `PublicTransportDay | PublicTransportNight` against `CargoTransport` path methods on the stop and line edges (`src/Game/Game.Pathfind/PathUtils.cs`), and routes the line's speed into the passenger or cargo slot of the global heuristic ([transit-routing.md](transit-routing.md)) — and what `Game.Routes.InitializeSystem` turns into the `PublicTransportPurpose`, cargo `Resource` and capacity ranges `TransportVehicleSelectData.SelectVehicle` picks the line's `VehicleModel` from, so flipping the pair on a line with no matching vehicle prefab leaves it with none.
Three: which payload components the vehicle carries — `Game.Vehicles.PublicTransport` with a `Passenger` buffer against `Game.Vehicles.CargoTransport` with `Economy.Resources` and `LoadingResources` buffers ([vehicles.md](vehicles.md)).

## Traps

**`WaitingPassengers` lives on the waypoint, never on the stop — except at a taxi stand.**
Two lines sharing a stop have two independent queues, one per line's waypoint there; the taxi branch of `TransportStop.GetArchetypeComponents` is the one case that puts `AccessLane`, `RouteLane` and `WaitingPassengers` on the stop object, because a stand has no line to carry them.
Source: `src/Game/Game.Prefabs/TransportLinePrefab.cs`, `src/Game/Game.Prefabs/TransportStop.cs`.

**A field initializer on a prefab class in this topic is a Unity-serialized default the shipped asset overrides, not the value.**
Prefab classes across this topic declare initialized fields, some survive on some assets and some do not, and nothing in the C# marks which; the value the game reads is the baked `*Data` component, enumerated live.
Source: `src/Game/Game.Prefabs/TransportLinePrefab.cs`, `src/Game/Game.Prefabs/TransportDepot.cs`, `src/Game/Game.Prefabs/PublicTransport.cs`, `src/Game/Game.Prefabs/CargoTransport.cs`, `src/Game/Game.Prefabs/Taxi.cs`.

**`PublicTransportFlags` and `CargoTransportFlags` share twelve member names, and only the first four — `Returning`, `EnRoute`, `Boarding`, `Arriving` — keep the same bit.**
From `RequiresMaintenance` on the values diverge (`0x80` against `0x10`), so casting one enum to the other silently reads the wrong flag; vanilla consumers test both components and default the absent one to zero — `TransportLineSystem.CheckVehicles` is the canonical shape — and a mod handling only one has dropped the cargo half or the passenger half of the fleet.
Source: `src/Game/Game.Vehicles/PublicTransportFlags.cs`, `src/Game/Game.Vehicles/CargoTransportFlags.cs`, `src/Game/Game.Simulation/TransportLineSystem.cs`.

**`RouteUtils`' seven `public const float`s appear at their use sites as bare literals, and patching the constants changes nothing.**
A C# `const` is compiled into every consumer, so the decompile renders `0.25f`, `11f / 12f` or `0.03f` where `TRANSPORT_DAY_START_TIME`, `TRANSPORT_DAY_END_TIME` and `TAXI_DISTANCE_FEE` were written — the declarations are the only documentation these numbers have, and changing one means patching every consuming method separately.
Source: `src/Game/Game.Routes/RouteUtils.cs`.

## Mechanisms

| Mechanism | Vanilla owner | Listing |
| --- | --- | --- |
| Options, activity, fleet sizing, vehicle requests, unbunching | `TransportLineSystem`, `RouteUtils`, `TransportBoardingHelpers` | [lines-and-fleet.md](lines-and-fleet.md) |
| Stop state, the waiting estimate, boarding, fares, cargo loading | `TransportStopSystem`, `TransportStationAISystem`, `WaitingPassengersSystem`, `ResidentAISystem`, `TransportBoardingHelpers` | [stops-and-boarding.md](stops-and-boarding.md) |
| Depots, dispatch matching, vehicle selection, maintenance and energy, the taxi | `TransportDepotAISystem`, `TransportVehicleDispatchSystem`, `TransportVehicleSelectData`, the transit vehicle AI systems | [depots-and-dispatch.md](depots-and-dispatch.md) |
| Planning the line's route, transit edge costs, the heuristic feed, gate bypass | `RoutePathSystem`, `PathUtils`, `RoutePathReadySystem`, `TransportLineSystem` | [transit-routing.md](transit-routing.md) |
| The vehicle's components, units, flags and emissions | `VehicleInitializeSystem`, the four navigation systems | [vehicles.md](vehicles.md) |
| The outside connection as an object | `OutsideConnectionSystem`, `BuildingUtils` | [outside-connections.md](outside-connections.md) |

## Bridges

- `prefabs-and-assets` — changing what a mode is means a prefab-phase system overwriting the baked `*Data` component, never patching the class field the asset overrides anyway.
- `ecs-in-this-game` — a line is four archetypes plus two-way waypoint-to-stop references that `WaypointConnectionSystem` maintains and snaps within `WAYPOINT_CONNECTION_DISTANCE`; a mod building route entities directly reproduces both, and every `PathfindUpdated` stamp here is an `EntityCommandBuffer` write.
- `placement-definitions` — the supported creation path for a line is the route tool's, through `WaypointDefinition`; `RouteToolSystem` and `ApplyRoutesSystem` consume it.
- `roads-and-traffic` — owns the network under a line, the pathfind queue and cost model, and intercity traffic, its generation and spawn rate included; the one hard dependency runs the other way, `PathfindQueueSystem` reading `TransportLineSystem.GetMaxTransportSpeed` on every schedule, so the fastest line in the city bounds every pathfind whose methods include public or cargo transport ([transit-routing.md](transit-routing.md)).
- `city-services-and-coverage` — owns the `ServiceRequest` / `ServiceDispatch` / `ServiceDistrict` machinery and the service-vehicle AI systems; the five transit-and-taxi AI systems — `TransportCarAISystem`, `TransportTrainAISystem`, `TransportWatercraftAISystem`, `TransportAircraftAISystem`, `TaxiAISystem` — are this topic's, and the vehicle-as-object machinery in [vehicles.md](vehicles.md) serves the service fleet too.
- `economy-and-companies` — owns `StorageCompany`, `StorageTransferRequest`, `TradeCost`, the resource graph, and the delivery truck's dispatch and money (`DeliveryTruckAISystem`); this topic owns the load moving through the transit vehicle at boarding, the work route's machinery against its payload, and the delivery truck only as a vehicle — the same body-plus-payload machinery ([vehicles.md](vehicles.md)).
- `citizens-and-households` — owns the resident and the commuter and tourist spawn paths; this topic owns where a resident meets a vehicle — free space, boarding distance, the fare, the waiting accumulators.
- `environment-and-pollution` — owns what a vehicle's side effects become once written to the network; the per-vehicle emission lerp is this topic's ([vehicles.md](vehicles.md)).
- `city-state-and-progression` — owns unlocks; this topic supplies the `TransportUsageData` counters a `TransportRequirementData` unlock reads, which cover rail, sea and air only ([stops-and-boarding.md](stops-and-boarding.md)).
- `utilities-and-flow-networks` — owns the electricity and water outside connections; the query discriminator between theirs and this topic's is in [outside-connections.md](outside-connections.md).
- `simulation-time-and-units` — owns the 262144-frame day and the frame count behind every `/ 60` frames-to-seconds conversion in this topic's formulas.
- `units-and-formatting` — speed is m/s on the component and km/h in the UI, turning angles are radians against degrees, maintenance range metres against kilometres, and prices are `ushort` ([vehicles.md](vehicles.md)).
- `save-serialization` — many components here gate fields on named format versions (`Version.routePolicies`, `Version.transportLinePolicies` and kin), and the route buffers split: `VehicleModel` and `RouteModifier` survive a save while `RouteWaypoint`, `RouteSegment`, `RouteVehicle`, `ConnectedRoute` and `SubRoute` are `IEmptySerializable`, rebuilt by the matching `Game.Serialization` systems at `SystemUpdatePhase.Deserialize`; the vehicle's own buffers split the same way ([vehicles.md](vehicles.md)).
- `patching` — the transit edge builders are Burst-inlined at every call site, so the seam into transit routing is the managed schedule rather than a prefix on `PathUtils`, and `RoutePathSystem.SetupPathfind` is private ([transit-routing.md](transit-routing.md)).
- `mod-lifecycle-and-ordering` — decides the phase for every write above; the route systems register from `Modification1` through `ModificationEnd` and `GameSimulation` to the `Deserialize` rebuilds, with tool, rendering and debug registrations besides (`src/Game/Game.Common/SystemOrder.cs`).
- `localization` — reading transit state for a UI is a string-table job over the components this file's map names.
- `debug-menu` — `RouteDebugSystem` draws routes as gizmos, and the developer info panel renders a stop's waiting time; a reader who wants to see a line goes there rather than reasoning blind.

(VOLATILE: every component, field, enum, system, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Routes`, `Game.Vehicles`, `Game.Prefabs`, `Game.Simulation`, `Game.Pathfind`, `Game.Buildings`, `Game.Net`, `Game.Economy` and `Game.Common`, at the files the rows and traps cite; plus the per-mode table's line-prefab shape, against the running game's prefab set, re-derived by the `ecs_query` stated above it.)
