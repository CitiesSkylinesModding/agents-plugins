# Utilities and flow networks

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Electricity, fresh water and sewage reach a building by solving a maximum flow over a graph, not by radiating coverage from a service building.
`Game.Simulation.Flow` is a self-contained max-flow library that never names a utility; `Game.Simulation.ElectricityFlowSystem` instantiates it over one layer and `Game.Simulation.WaterPipeFlowSystem` over two — fresh and sewage — sharing one topology.
The persistent graph is ECS entities of their own, a shadow graph beside the net, built from every net edge that carries the utility, the buildings whose own sub-net markers hook them up — every other consumer rides its road edge's aggregate — and every outside connection ([flow-graph.md](flow-graph.md)).
Consumption is edge capacity the adjust systems write toward the sink, production is edge capacity the building AI systems write from the source, and fulfilment is the solved flow read back per building, pro-rata across a road edge's members for buildings without their own connection — clamped to `m_WantedConsumption` on electricity, unclamped on water ([consumption-and-dispatch.md](consumption-and-dispatch.md), [production-and-sources.md](production-and-sources.md)); fresh-water pollution rides the same graph on its own cadence — pumps write it, `WaterPipePollutionSystem` walks it along the last apply's flows, dispatch reads it back — with [production-and-sources.md](production-and-sources.md) and [consumption-and-dispatch.md](consumption-and-dispatch.md) splitting that chain.
Both solvers run a fixed 128-frame cycle — adjust, prepare, 124 solve frames, apply, then dispatch, trade and statistics — with the water cycle 64 frames out of phase ([solve-cycle.md](solve-cycle.md)).
Climate enters this topic's own systems at two points, both electric — a swept set, re-checked by a `ClimateSystem` grep across `Game.Simulation/`: `ElectricityParameterData.m_TemperatureConsumptionMultiplier.Evaluate(ClimateSystem.temperature)` scales every building's consumption and `ClimateSystem.cloudiness` lerped against `m_CloudinessSolarPenalty` scales solar output; nothing in the water path reads `ClimateSystem` (rain reaches a pump only through the surface-water bodies `environment-and-pollution` owns), and wind power reads `WindSystem`.

## The map

Default reads: a parameter singleton is a `GetSingleton<T>` (`ecs-in-this-game` carries the call); a prefab component is reached from an instance through `PrefabRef.m_Prefab`, or enumerated with `ecs_query` on the component itself, since this game's prefab entities carry `PrefabData`; a row states its own shape only where it differs.

The graph — each component on a dedicated flow entity, reached from a building or net edge through its connection component:

| The game models | Component | Access shape |
| --- | --- | --- |
| A flow node | `ElectricityFlowNode`, a scratch `int m_Index` and nothing saved; `WaterPipeNode` adds `float m_FreshPollution`, the one field it serializes (`src/Game/Game.Simulation/ElectricityFlowNode.cs`, `WaterPipeNode.cs`) | its own entity; adjacency is the node's `ConnectedFlowEdge` buffer |
| A flow edge | `ElectricityFlowEdge { m_Start, m_End, m_Capacity, m_Flow, m_Flags }` with `direction` over the low flag bits; `WaterPipeEdge { m_Start, m_End, m_FreshCapacity, m_SewageCapacity, m_FreshFlow, m_SewageFlow, m_FreshPollution, m_Flags }`; each plus a scratch `m_Index` (`src/Game/Game.Simulation/ElectricityFlowEdge.cs`, `WaterPipeEdge.cs`) | endpoints live on the edge and adjacency on the nodes, so an edit must keep both consistent |
| The diagnosis | `ElectricityFlowEdgeFlags { None, Forward, Backward, Bottleneck, BeyondBottleneck, Disconnected, ForwardBackward }`; `WaterPipeEdgeFlags { None, WaterShortage, SewageBackup, WaterDisconnected, SewageDisconnected }` (`src/Game/Game.Simulation/ElectricityFlowEdgeFlags.cs`, `WaterPipeEdgeFlags.cs`) | read off the edge, valid after the cycle's apply frame |
| A net edge's place in the graph | `ElectricityNodeConnection` / `WaterPipeNodeConnection`, stamped on net edges and net nodes (`src/Game/Game.Simulation/ElectricityNodeConnection.cs`, `WaterPipeNodeConnection.cs`) | points at the flow node; a net edge's own is its middle node |
| A building's own hookup | `ElectricityBuildingConnection { m_TransformerNode, m_ProducerEdge, m_ConsumerEdge, m_ChargeEdge, m_DischargeEdge }`; `WaterPipeBuildingConnection { m_ProducerEdge, m_ConsumerEdge }` (`src/Game/Game.Simulation/ElectricityBuildingConnection.cs`, `WaterPipeBuildingConnection.cs`) | the four edge fields store edges whose nodes are recovered through its helper methods over a `ComponentLookup` of the edge type; the transformer node is stored directly |
| An outside connection's graph role | `TradeNode` tag on its flow node (`src/Game/Game.Simulation/TradeNode.cs`) | queried by both trade systems; its two edges start disabled and the solver turns one on |

What carries a utility, per net prefab:

| The game models | Component | Access shape |
| --- | --- | --- |
| Electricity carriage | authoring `Game.Prefabs.ElectricityConnection { m_Voltage (Low/High/Invalid), m_Direction, m_Capacity, m_RequireAll, m_RequireAny, m_RequireNone }`, baked into `ElectricityConnectionData` with three `CompositionFlags` sets (`src/Game/Game.Prefabs/ElectricityConnection.cs`, `ElectricityConnectionData.cs`) | only capacity, direction and voltage serialize; the composition sets rebuild from the prefab at load |
| Water carriage | `Game.Prefabs.WaterPipeConnection` → `WaterPipeConnectionData { m_FreshCapacity, m_SewageCapacity, m_StormCapacity }`, no requirement arrays (`src/Game/Game.Prefabs/WaterPipeConnection.cs`, `WaterPipeConnectionData.cs`) | per layer |
| The verdict on an edge instance | `Game.Net.ElectricityConnection`, a tag; `Game.Net.WaterPipeConnection`, carrying the three capacities copied per instance from the prefab (`src/Game/Game.Net/`) | presence is what the graph builders query |

`NetInitializeSystem` bakes the requirement arrays into the composition sets, and `GenerateEdgesSystem` re-tests them on every placement or upgrade, adding or removing the runtime tag — empty sets pass unconditionally ([flow-graph.md](flow-graph.md) holds the test).
Which requirements a net declares is asset data: enumerated live with the `ecs_query` above, 123 prefabs carried `ElectricityConnectionData` — roads, rail, tram and subway tracks, quays, bridges, the power lines and cables, and the voltage markers, highways included — and 60 carried `WaterPipeConnectionData` — roads, quays, pipes and the pipe markers, no rail track and no highway; counts are of one install, DLC included, and the query is the re-check.
The worked example: `Highway Twoway - 3 lanes` declares `m_CompositionAll = { General = Lighting }`, so a highway carries electricity only once upgraded with street lighting.

Buildings — prefab components reached through `PrefabRef` and folded over `InstalledUpgrade` by `UpgradeUtils.CombineStats` (except `PowerPlantData` and `EmergencyGeneratorData`, which their AI systems sum per upgrade so each upgrade keeps its own fuel and activation state), instance components on the building:

| The game models | Component | Access shape |
| --- | --- | --- |
| Demand | `ConsumptionData.m_ElectricityConsumption`, `m_WaterConsumption` (`src/Game/Game.Prefabs/ConsumptionData.cs`) | prefab |
| Wanted and fulfilled | `Game.Buildings.ElectricityConsumer { m_WantedConsumption, m_FulfilledConsumption, m_CooldownCounter, m_Flags }`; `WaterConsumer { m_WantedConsumption, m_FulfilledFresh, m_FulfilledSewage, m_FreshCooldownCounter, m_SewageCooldownCounter, m_Pollution, m_Flags }` — one wanted figure serves both layers (`src/Game/Game.Buildings/`) | instance; stale after a load — and while paused — until a full cycle's apply and dispatch |
| Electricity production | `Game.Buildings.ElectricityProducer { m_Capacity, m_LastProduction }`, summed from `PowerPlantData`, `GarbagePoweredData`, `WindPoweredData`, `WaterPoweredData`, `GroundWaterPoweredData`, `SolarPoweredData` (`src/Game/Game.Prefabs/`) | instance plus prefab terms ([production-and-sources.md](production-and-sources.md)) |
| Stored energy | `Game.Buildings.Battery { m_StoredEnergy, m_Capacity, m_LastFlow }`, `BatteryData { m_Capacity, m_PowerOutput }`, `EmergencyGeneratorData { m_ElectricityProduction, m_ActivationThreshold }` (`src/Game/Game.Buildings/Battery.cs`, `src/Game/Game.Prefabs/`) | the two unit-conversion properties are `Battery.storedEnergyHours` and `BatteryData.capacityTicks` |
| Fresh-water production | `WaterPumpingStationData { m_Types, m_Capacity, m_Purification }` and `Game.Buildings.WaterPumpingStation` (`src/Game/Game.Prefabs/WaterPumpingStationData.cs`, `src/Game/Game.Buildings/`) | check `AllowedWaterTypes m_Types` before building against a pump — it decides whether groundwater or surface water is drawn at all |
| Sewage handling | `SewageOutletData { m_Capacity, m_Purification }` and `Game.Buildings.SewageOutlet { m_Capacity, m_LastProcessed, m_LastPurified, m_UsedPurified }` (`src/Game/Game.Prefabs/`, `src/Game/Game.Buildings/SewageOutlet.cs`) | instance plus prefab |
| City totals | `IElectricityStatisticsSystem`, `IWaterStatisticsSystem` (`src/Game/Game.Simulation/`) | system properties; the UI reads them through the `electricityInfo` and `waterInfo` binding groups (`src/Game/Game.UI.InGame/ElectricityInfoviewUISystem.cs`, `WaterInfoviewUISystem.cs`) |

Where the tuning numbers live, all singletons:

| Family of numbers | Component |
| --- | --- |
| Battery start charge, the temperature-consumption curve, the solar cloudiness penalty, and the notification prefab references | `ElectricityParameterData` (`src/Game/Game.Prefabs/ElectricityParameterData.cs`) |
| Groundwater replenish, purification, usage multiplier and pump effective amount; surface usage multiplier and pump effective depth; tolerated pollution; pipe-pollution spread and stale purification; and the notification and asset-menu prefab references | `WaterPipeParameterData` (`src/Game/Game.Prefabs/WaterPipeParameterData.cs`) |
| The supply and pollution efficiency penalties, their delays, and the two fee-efficiency curves | `BuildingEfficiencyParameterData` (`src/Game/Game.Prefabs/BuildingEfficiencyParameterData.cs`) |
| The two fees and the two fee-consumption curves — the fees belong to `economy-and-companies`; the curves are evaluated here | `ServiceFeeParameterData` (`src/Game/Game.Prefabs/ServiceFeeParameterData.cs`) |
| Import and export prices — the sewage export price included — and the export pollution tolerance; belongs to `economy-and-companies`, and `m_WaterExportPollutionTolerance` is evaluated here | `OutsideTradeParameterData` (`src/Game/Game.Prefabs/OutsideTradeParameterData.cs`) |

## Traps

**A field initializer on `ElectricityParametersPrefab` or `WaterPipeParametersPrefab` is a Unity-serialized default, not the value.**
The shipped asset overwrites the fields before `LateInitialize` copies them into the parameter singletons, and read live at 1.6.0f1 many differ from their initializers; the test is what consumes the value, so a component a mod instantiates in code — with no asset behind it — does keep its initializers.
Source: `src/Game/Game.Prefabs/ElectricityParametersPrefab.cs`, `src/Game/Game.Prefabs/WaterPipeParametersPrefab.cs`.

**A game mode rebuilds this topic's parameters on every load.**
`GameModeSystem` runs the mode classes after prefab initialization — a mode class per tuning singleton above rewrites it, `ElectricityConnectionGlobalMode` multiplies every `ElectricityConnectionData.m_Capacity`, the two `ServiceConsumption` global modes rescale `ConsumptionData`'s demand figures, and a `LocalModePrefab` mode class per producer family rewrites its production component: ten under `src/Game/Game.Prefabs.Modes/`, one for every production component this file's tables name, `PowerPlantMode` and `BatteryMode` included — and which mode a save applies is authored asset data no static read detects.
Source: `src/Game/Game.Prefabs.Modes/GameModeSystem.cs`, `src/Game/Game.Prefabs.Modes/ElectricityConnectionGlobalMode.cs`, `src/Game/Game.Prefabs.Modes/PowerPlantMode.cs`.

**The sewage layer carries handling capacity, so every sewage number reads backwards.**
Both water layers run source to sink; outlets emit capacity-to-accept-sewage from the source side, buildings consume it, and "sewage exported" is summed on edges importing handling capacity — `WaterTradeSystem` bills it as a cost and asserts sink-side sewage flow is zero.
Source: `src/Game/Game.Simulation/WaterPipeFlowSystem.cs`, `src/Game/Game.Simulation/SewageOutletAISystem.cs`, `src/Game/Game.Simulation/WaterTradeSystem.cs`.

**No net prefab in this install throttles the fresh or sewage flow it carries: pipe size is cost and footprint, not throughput.**
`WaterPipeConnectionData` declares a capacity per layer, but an `ecs_query` census of every carrier in one install (DLC included) read `1073741823` — `kMaxEdgeCapacity`, the both-systems constant for unconstrained — on each layer served and `0` on the rest, `Large Water Pipe` identical to `Small Water Pipe`; that census is the re-check, and `WaterPipeEdgeFlags` has no bottleneck member because `WaterPipeFlowJob` runs no bottleneck-labelling pass — its labels know only shortage and connectivity, while `ElectricityFlowJob`'s pass earns electricity edges `Bottleneck`/`BeyondBottleneck`.
Source: `src/Game/Game.Prefabs/WaterPipeConnectionData.cs`, `src/Game/Game.Simulation/WaterPipeFlowSystem.cs`, `src/Game/Game.Simulation/ElectricityFlowSystem.cs`.

**Stormwater is declared everywhere and solved nowhere.**
`UtilityTypes.StormwaterPipe`, `Layer.StormwaterPipe` and `m_StormCapacity` all exist, but `WaterPipeFlowSystem` holds exactly two datasets and schedules exactly two jobs, `WaterPipeEdge`'s only storm trace is a legacy read-and-discard in its `Deserialize`, and the same census read `m_StormCapacity = 0` on every carrier.
Source: `src/Game/Game.Simulation/WaterPipeFlowSystem.cs`, `src/Game/Game.Simulation/WaterPipeEdge.cs`, `src/Game/Game.Net/UtilityTypes.cs`.

**The statistics' `production` is capacity, not output.**
`ElectricityStatisticsSystem` sums `ElectricityProducer.m_Capacity` — the instance capacity the AI systems recomputed, not the prefab nameplate — and never `m_LastProduction`, so production against consumption is headroom rather than usage; `freshCapacity`/`sewageCapacity` are the same kind of sum on the water side.
Source: `src/Game/Game.Simulation/ElectricityStatisticsSystem.cs`, `src/Game/Game.Simulation/WaterStatisticsSystem.cs`.

**The names that sound like this topic's flow mostly are not it.**
`Game.Net.LaneFlow` and `SecondaryFlow` are traffic, `Game.Net.SubFlow` is the rendered flow animation, `Game.Net.FlowResource` is a two-member enum nothing references, and `Layer` is two types — `Game.Net.Layer`, the carriage bitmask, and `Game.Simulation.Flow.Layer`, the solver's own cut bookkeeping; the real solver lives in `Game.Simulation.Flow`.
Source: `src/Game/Game.Net/LaneFlow.cs`, `src/Game/Game.Net/SubFlow.cs`, `src/Game/Game.Net/FlowResource.cs`, `src/Game/Game.Net/Layer.cs`, `src/Game/Game.Simulation.Flow/Layer.cs`.

**Raising a prefab's consumption from zero at runtime does not make its buildings consumers.**
`ConsumptionData.AddArchetypeComponents` adds `ElectricityConsumer` and `WaterConsumer` only where the figure is above zero when the archetype is built, so instances of a zero-consumption prefab never carry the component every system here queries.
Source: `src/Game/Game.Prefabs/ConsumptionData.cs`.

Each sibling carries further traps beside its listings.

## Mechanisms

| Mechanism | Vanilla owner | Listing |
| --- | --- | --- |
| Building the shadow graph, utility carriage, the mod seams, the load-time re-apply | `ElectricityEdgeGraphSystem`, `ElectricityBuildingGraphSystem`, `ElectricityRoadConnectionGraphSystem`, `ElectricityOutsideConnectionGraphSystem` and the water twins; `GenerateEdgesSystem`; `Game.Serialization.ElectricityGraphSystem` | [flow-graph.md](flow-graph.md) |
| The 128-frame cycle, the solver phases, the step budget, `ready` | `ElectricityFlowSystem` + `ElectricityFlowJob`, `WaterPipeFlowSystem` + `WaterPipeFlowJob`, `MaxFlowSolver`, `FluidFlowSolver` | [solve-cycle.md](solve-cycle.md) |
| Wanted consumption, fulfilment, warnings, efficiency, trade billing | `AdjustElectricityConsumptionSystem`, `AdjustWaterConsumptionSystem`, `DispatchElectricitySystem`, `DispatchWaterSystem`, `ElectricityTradeSystem`, `WaterTradeSystem` | [consumption-and-dispatch.md](consumption-and-dispatch.md) |
| Production, pumping, sewage treatment, batteries, groundwater, pipe pollution | `PowerPlantAISystem`, `WaterPumpingStationAISystem`, `SewageOutletAISystem`, `BatteryAISystem`, `GroundWaterSystem`, `GroundWaterPollutionSystem`, `WaterPipePollutionSystem` | [production-and-sources.md](production-and-sources.md) |

## Bridges

- `ecs-in-this-game` — the `GetSingleton<T>` behind every parameter row, the `UpdateFrame` partitioning behind the consumption rate, and archetype-derived presence: a query on `ElectricityConsumer` is a query on buildings whose prefab declared consumption.
- `prefabs-and-assets` — utility carriage is a `ComponentBase` on a net prefab whose `GetPrefabComponents`/`GetArchetypeComponents` split decides where the data lands, and both parameter prefabs copy their fields into singletons at `LateInitialize`.
- `roads-and-traffic` — owns the substrate: `Game.Net.Edge`, `ConnectedEdge`, `ConnectedNode` and the composition machinery; which nets exist and how they connect is theirs, what flows along them is this topic's.
- `city-services-and-coverage` — owns every service that reaches citizens by range or dispatched vehicle, even from a building that also does utilities; the shared vocabulary is `ConsumptionData`, the `Efficiency` buffer and `ConnectionWarningSystem`.
- `environment-and-pollution` — owns the pollution cell maps, the aquifer's `GroundWater` map and the water bodies this topic reads and writes: ground pollution feeds the aquifer, pumps draw surface water down and outlets raise it; pollution inside the pipes is this topic's, pollution in the world is theirs.
- `economy-and-companies` — owns `ServiceFeeSystem` and the fee and trade prices; both trade systems queue its `FeeEvent`s, and both adjust systems index the consumption curves by fee relative to the default.
- `zoning-buildings-and-land-value` — owns `SpawnableBuildingData.m_Level` and the `Renter` buffer, which the renter multiplier turns into per-building consumption.
- `simulation-time-and-units` — owns `TimeSystem.kTicksPerDay = 262144`, `SimulationSystem.frameIndex` and `SimulationUtils.GetUpdateFrame`, the substrate of the whole cadence.
- `units-and-formatting` — 85 solver ticks per hour and 2048 per day are the conversions between per-tick flow and every human-readable figure.
- `mod-lifecycle-and-ordering` — owns the modification-phase machinery the graph builders ride and the update ordering behind the `ready` gate.
- `save-serialization` — a long-lived migration chain: both edge components read and discard removed fields, and `ElectricityFlowSystem.Deserialize` branches over several version bands.
- `performance-and-memory` — the solve is the game's own worked example of amortising a global computation: a self-tuning step budget, persistent solver state, and sixteen-way consumer bucketing.
- `diagnostics` — the printed surface is thin: the missing-edge `Debug.LogError` sites, the two phase-named solver errors, the load-time graph warnings (the water system's legacy-pipe and null-node warnings included), and a runtime warning each in `GroundWaterSystem` and `WaterPipeGraphDeleteSystem`.
- `navigating-the-decompile` — a grep for the utility's name never finds the solver; the Traps name the decoys it does find.
- `patching` — nothing here needs Harmony: every seam is a public archetype property, a public static helper, a public settable property or an ordinary component write — though the settable properties are the dev-menu toggles, and the two dispatch ones reset on every load.
- `city-state-and-progression` — owns the `Locked` state of the two asset-menu prefabs that freezes the dispatch systems' cooldown step — consumer warnings and the supply-efficiency penalties hold still — until the service unlocks; the producer-side icons (pump, battery, outlet) are not gated.

(VOLATILE: every component, field, enum, system, property, method, constant, binding-group name and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Simulation.Flow`, `Game.Prefabs`, `Game.Prefabs.Modes`, `Game.Net`, `Game.Buildings`, `Game.Tools`, `Game.Serialization` and `Game.UI.InGame`, at the files the rows and traps cite; plus the live-read census counts, prefab display names and mode-class count this file states, against the running game's prefab set and the `Game.Prefabs.Modes` directory, re-derived by the re-check each states beside itself.)
