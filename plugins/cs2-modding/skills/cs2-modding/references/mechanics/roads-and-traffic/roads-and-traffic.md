# Roads and traffic

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`Game.Net` models the network as four entity kinds: nodes and edges carry the graph, lanes carry the behaviour, and composition prefabs carry the cross-section.
An edge joins two node entities and owes its shape to one cubic bezier, and every position on the network — lane objects, blockage ranges, path elements, parking slots — is a curve position in `[0, 1]` along one.
Lanes are entities of their own, listed in their owner's `SubLane` buffer, and everything vehicles react to — speed limits, congestion, blockage, parking, signals — lives on them.
The pathfinder plans over master lanes only ([route-selection.md](route-selection.md)), prices an edge as a dot product against four agent weights ([travel-weights.md](travel-weights.md)), and serves every request through one queue that can slow the game clock itself ([pathfind-queue.md](pathfind-queue.md)).
A road's properties split three ways: the road prefab carries what is true of the class, the composition prefab what is true of one cross-section variant, and the edge entity what is true of one segment right now.

## The map

Default reads: a prefab component is reached from an instance through `PrefabRef.m_Prefab`, or enumerated with `ecs_query` on the component itself; a runtime component sits on the entity its row names; a row states its own shape only where it differs.

The graph (`src/Game/Game.Net/` throughout):

| The game models | Component | Access shape |
| --- | --- | --- |
| A junction | `Node { m_Position, m_Rotation }` (`Node.cs`) | its edges are its `ConnectedEdge` buffer |
| A segment | `Edge { m_Start, m_End }`, two node entities (`Edge.cs`) | its `ConnectedNode` buffer lists nodes touching it mid-span, each with its curve position |
| The shape | `Curve { m_Bezier, m_Length }`, a `Bezier4x3` (`Curve.cs`) | `m_Length` is not saved; `Deserialize` recomputes it from the bezier |
| Rendered geometry | `EdgeGeometry`, `NodeGeometry`, `StartNodeGeometry`/`EndNodeGeometry` (`EdgeGeometry.cs`, `NodeGeometry.cs`) | rebuilt by `GeometrySystem`; not what the pathfinder reads |
| A roundabout | `Roundabout { m_Radius }` on the node (`Roundabout.cs`) | allowed only where the road's `NetGeometryData` carries `GeometryFlags.SupportRoundabout` |

The cross-section:

| The game models | Component | Access shape |
| --- | --- | --- |
| This segment's variants | `Composition { m_Edge, m_StartNode, m_EndNode }` on the edge (`src/Game/Game.Net/Composition.cs`) | three references to composition prefab entities, filled by `CompositionSelectSystem` |
| The variant flags | `CompositionFlags`, one `General` set and one `Side` set per side (`src/Game/Game.Prefabs/CompositionFlags.cs`) | the declaration also carries the `nodeMask`/`optionMask`/`directionalMask` partitions |
| Cross-section data | `NetCompositionData` on the composition prefab (`src/Game/Game.Prefabs/NetCompositionData.cs`) | widths and `m_HeightRange`, which `GeometrySystem` folds into the edge's collision bounds |
| The road-specific copy | `RoadComposition { m_ZoneBlockPrefab, m_SpeedLimit, m_Priority, m_Flags }` (`src/Game/Game.Prefabs/RoadComposition.cs`) | what `RoadSafetySystem` and `LaneSystem` actually read |
| Player upgrades | `Upgraded.m_Flags`, a `CompositionFlags` (`src/Game/Game.Net/Upgraded.cs`) | turn restrictions are its `Side` bits ([junctions.md](junctions.md)) |

The lanes (`src/Game/Game.Net/` throughout):

| The game models | Component | Access shape |
| --- | --- | --- |
| Lane membership | `SubLane { m_SubLane, m_PathMethods }` buffer on edge or node (`SubLane.cs`) | the mask filters which modes may use the lane |
| Graph identity | `Lane { m_StartNode, m_MiddleNode, m_EndNode }`, three `PathNode`s (`Lane.cs`) | identity in the pathfind graph, not geometry |
| A carriageway | `MasterLane` and `SlaveLane`, sharing `m_Group` and a `m_MinIndex`/`m_MaxIndex` range into the `SubLane` buffer (`MasterLane.cs`, `SlaveLane.cs`) | only the master becomes a pathfind edge |
| A drivable lane | `CarLane` plus the 32-member `CarLaneFlags` (`CarLane.cs`, `CarLaneFlags.cs`) | its four `byte` range fields are curve positions scaled by 255, read back through `blockageBounds`/`cautionBounds` |
| A footway | `PedestrianLane` plus `PedestrianLaneFlags` (`PedestrianLane.cs`) | |
| Parking | `ParkingLane`, `GarageLane` (`ParkingLane.cs`, `GarageLane.cs`) | [parking.md](parking.md) |
| A hookup | `ConnectionLane` plus `ConnectionLaneFlags` (`ConnectionLane.cs`) | joins the network to buildings, outside connections, parking facilities and areas |
| Rail | `TrackLane` (`TrackLane.cs`) | |
| A lane's stretch | `EdgeLane { m_EdgeDelta }` on edge lanes, `NodeLane` on node lanes (`EdgeLane.cs`, `NodeLane.cs`) | |
| Objects on a lane | `LaneObject { m_LaneObject, m_CurvePosition }` buffer, sorted by position (`LaneObject.cs`) | `IEmptySerializable` — rebuilt on load, never saved |

The road classes:

| The game models | Component | Access shape |
| --- | --- | --- |
| The class taxonomy | the `NetGeometryPrefab` subclasses, `RoadPrefab` among them (`src/Game/Game.Prefabs/NetGeometryPrefab.cs`) | each class line declares its parent |
| Road authoring | `RoadPrefab { m_RoadType, m_SpeedLimit, m_ZoneBlock, m_TrafficLights, m_HighwayRules }` (`src/Game/Game.Prefabs/RoadPrefab.cs`) | authored asset data; the initializers in the file are not the values (trap below) |
| The baked class | `RoadData { m_ZoneBlockPrefab, m_SpeedLimit, m_Flags }` with `Game.Prefabs.RoadFlags` (`src/Game/Game.Prefabs/RoadData.cs`, `RoadFlags.cs`) | written by `NetInitializeSystem`; `ecs_query` on it enumerates every road class |
| Per-segment state | `Road` (traffic flow, `Game.Net.RoadFlags`), `NetCondition { m_Wear }`, `LaneCondition` on lanes (`src/Game/Game.Net/`) | [congestion-and-blockage.md](congestion-and-blockage.md) |

Which flags each road family carries is asset data the C# collects nowhere, so it is read live: `ecs_query` on `Game.Prefabs.RoadData`, named through `PrefabSystem.GetPrefabName`.
Read at 1.6.0f1 the shape is: zoneable families carry `EnableZoning` and the larger of them add `PreferTrafficLights`; highway families carry `UseHighwayRules` and never `EnableZoning`; a one-way variant adds `DefaultIsForward`; the public-transport road (`RoadType.PublicTransport`) carries no flags.
(UNVERIFIED: whether any DLC road family breaks that pattern — one representative per family was read, and the query above reads them all.)
`SeparatedCarriageways` and `HasStreetLights` are never authored: `NetCompositionSystem` derives them per composition, the latter from any `NetCompositionObject` whose prefab has `StreetLightData` (`src/Game/Game.Prefabs/NetCompositionSystem.cs`).

The pathfinding surface (`src/Game/Game.Pathfind/` unless noted):

| The game models | Component | Access shape |
| --- | --- | --- |
| A graph node key | `PathNode`, one packed `ulong` (`PathNode.cs`) | owner entity index in the high 32 bits, a 15-bit curve position, a secondary bit, a 16-bit lane index |
| A request's weights | `PathfindWeights (time, behaviour, money, comfort)` (`PathfindWeights.cs`) | [travel-weights.md](travel-weights.md) |
| Per-lane costs | the pathfind prefab named by `NetLaneData.m_PathfindPrefab` (`src/Game/Game.Prefabs/NetLaneData.cs`) | [route-selection.md](route-selection.md) carries the chain |
| The result | `PathInformation`, the `PathElement` buffer, `PathOwner`, `PathFlags` (`PathInformation.cs`, `PathElement.cs`, `PathOwner.cs`, `PathFlags.cs`) | [pathfind-queue.md](pathfind-queue.md) |
| Congestion | `CarLane.m_FlowOffset`, computed from the lane's own flow history and rolled up into `Road`'s four accumulators (`src/Game/Game.Net/CarLane.cs`, `Road.cs`) | [congestion-and-blockage.md](congestion-and-blockage.md) |
| A border queue | `Game.Net.OutsideConnection { m_Delay }` (`src/Game/Game.Net/OutsideConnection.cs`) | [intercity-traffic.md](intercity-traffic.md) |
| Warning icons | `TrafficConfigurationData`, nine notification prefab references — bottleneck, dead end, and the connection warnings (`src/Game/Game.Prefabs/TrafficConfigurationData.cs`) | singleton |

## Traps

**Two different enums are both named `RoadFlags`, and one line of the safety job tests both.**
`Game.Prefabs.RoadFlags` is the seven-member class description (`EnableZoning`, `UseHighwayRules`, …); `Game.Net.RoadFlags` is five members of per-edge state (`IsLit`, `LightsOff`, …); importing the wrong one is a silently wrong bit test.
Source: `src/Game/Game.Prefabs/RoadFlags.cs`, `src/Game/Game.Net/RoadFlags.cs`, `src/Game/Game.Simulation/RoadSafetySystem.cs`.

**`Composition.m_StartNode` is a reference to a composition prefab, not to the edge's start node.**
All three fields name prefabs — `GeometrySystem` indexes them straight into prefab composition data — so marking `m_StartNode` `Updated` marks a prefab shared by every edge of that cross-section; the node is `Edge.m_Start`.
Source: `src/Game/Game.Net/Composition.cs`, `src/Game/Game.Net/GeometrySystem.cs`, `src/Game/Game.Net/CompositionSelectSystem.cs`.

**Speed limits are authored in km/h and stored in m/s.**
`NetInitializeSystem` divides by 3.6 when baking `RoadData.m_SpeedLimit`, and every downstream field — `RoadComposition`, `CarLane.m_SpeedLimit` — is m/s.
Source: `src/Game/Game.Prefabs/NetInitializeSystem.cs`.

**A field initializer on a prefab class in this topic is a Unity-serialized default the shipped asset overrides, not the value.**
`CarPathfind`, `TrafficAccident`, `RoadPrefab` and `LaneDeterioration` all declare initialized fields, some survive on some assets and some do not, and nothing in the C# marks which; the value the game reads is the baked component, enumerated live.
Source: `src/Game/Game.Prefabs/CarPathfind.cs`, `src/Game/Game.Prefabs/TrafficAccident.cs`, `src/Game/Game.Prefabs/RoadPrefab.cs`.

**A `PathNode` stores an entity index without its version, so it does not survive entity recycling.**
`ReplaceOwner` and `SetOwner` exist to repoint one; a stale key silently addresses whatever entity now holds the index.
Source: `src/Game/Game.Pathfind/PathNode.cs`.

## Mechanisms

| Mechanism | Vanilla owner | Listing |
| --- | --- | --- |
| Edge cost, heuristic, deliberate randomisation, lane choice | `PathfindJobs`, `PathUtils`, `CarNavigationSystem` | [route-selection.md](route-selection.md) |
| Who requests, with which weights, under which cost ceiling | `CitizenUtils`, the dispatch systems, `TripPriorityParametersData` | [travel-weights.md](travel-weights.md) |
| Congestion feedback, blockage, wear, deadlocks | `TrafficFlowSystem`, `LaneDataSystem`, `StuckMovingObjectSystem`, `TrafficBottleneckSystem` | [congestion-and-blockage.md](congestion-and-blockage.md) |
| Road accidents | `RoadSafetySystem` | [accidents.md](accidents.md) |
| Yield, stop, right of way, traffic lights, roundabouts | `NetCompositionSystem`, `LaneSystem`, `TrafficLightSystem` | [junctions.md](junctions.md) |
| Parking search and pricing | `ParkingLaneDataSystem`, `PathUtils`, the car AI systems | [parking.md](parking.md) |
| Intercity generation and border delay | `TrafficSpawnerAISystem`, `RandomTrafficDispatchSystem`, `OutsideConnectionDelaySystem` | [intercity-traffic.md](intercity-traffic.md) |
| The queue, its deadlines, the clock throttle | `PathfindQueueSystem`, `PathfindSetupSystem`, `SimulationSystem` | [pathfind-queue.md](pathfind-queue.md) |
| Re-deriving lanes after an edit | `LaneSystem`, `LanesModifiedSystem`, the `Modification*` phases | [network-rebuild.md](network-rebuild.md) |

## Bridges

- `prefabs-and-assets` — every balance number here is a baked prefab component (`RoadData`, `PathfindCarData`, `TrafficAccidentData`, `LaneDeteriorationData`); changing one is a prefab-phase system overwriting the component, never a patch on the class field the asset overrides anyway.
- `ecs-in-this-game` — the `Updated`/`PathfindUpdated` markers every rebuild rides, and the singleton and `PrefabRef` reads behind every map row.
- `mod-lifecycle-and-ordering` — the `Modification*` phase order [network-rebuild.md](network-rebuild.md) walks, and the anchoring a replacement system needs.
- `patching` — reaching the weight literals inside dispatch jobs, and substituting a system the game gives no seam into.
- `custom-tools` — a tool that edits the network ends by writing exactly the markers [network-rebuild.md](network-rebuild.md) names.
- `placement-definitions` — node and edge edits travel as `NetCourse` definitions through the placement pipeline; splitting an edge in place is `CoursePos.m_SplitPosition`, written by the net tool, computed for sub-courses by `CourseSplitSystem`, and consumed by the node- and edge-generation pass.
- `performance-and-memory` — the pathfinder's per-thread linear allocators, its half-of-workers thread budget, and the `GetGraphMemory`/`GetQueryMemory` getters.
- `transportation-and-vehicles` — owns the outside connection as an object, transit lines and the vehicle fleet itself; the agents' movement on this network is this topic's, and `PathfindQueueSystem` feeds the transit network's top speeds into the heuristic's transport branches.
- `city-services-and-coverage` — its coverage and availability queries run on this topic's pathfind workers ([pathfind-queue.md](pathfind-queue.md)), and road maintenance owns the repair side of wear.
- `utilities-and-flow-networks` — owns the utility carriage a road composition declares; the network substrate is this topic's, what flows in the pipes is theirs.
- `environment-and-pollution` — owns what a vehicle's noise and air side effects become once written to the edge's `Pollution`.
- `simulation-time-and-units` — owns what a frame and an update interval are worth in game time, the time-of-day tent [congestion-and-blockage.md](congestion-and-blockage.md)'s `float4` slots are blended with, and the step clamp [pathfind-queue.md](pathfind-queue.md) transcribes.

(VOLATILE: every component, field, enum, system, method and `Source:` path this file names — their declarations under `src/Game/` in `Game.Net`, `Game.Prefabs`, `Game.Pathfind`, `Game.Simulation` and `Game.Tools`, at the files the rows and traps cite; plus the road-class flag shape, against the running game's prefab set, re-derived by the `ecs_query` stated beside it.)
