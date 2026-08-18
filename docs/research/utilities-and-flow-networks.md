# Utilities and flow networks

**Baseline.** Every claim was established against game version 1.6.0f1 (Unity 2022.3.71f1).
Decompiled C# citations are to a checkout regenerated from that install's managed assemblies, under `src/`, one directory per assembly.
Live values were read from the user's running city through the sibling Unity plugin on 2026-08-11; the simulation was **paused** (`SimulationSystem.selectedSpeed == 0`) for the whole session, which matters for one finding below and for nothing else.
Wiki pages were fetched live on 2026-08-11 and rendered past the bot challenge; no substitution through `survey-wiki-inventory.md` was needed.
The mod corpus at `C:\Users\Morgan\Documents\Projets\cs2-third-party-mods` was read on 2026-08-11, 22 repositories.
UI-bundle citations are to the reformatted copy at `DecompiledCitiesSkylines2/src-ui/source.js`, produced with prettier at its defaults, **135,021 lines** — the count matches this file's citations.

---

## Rulings that govern this whole file

**Ruled (2026-08-06, the citizens-and-households pass; conflicts.md).** A shipped reference states **no prefab value**. It names the component and the field, and that is the whole of what it says about the magnitude. Three things are untouched and are the spine of this topic:

- **C# constants ship, as numbers.** A `const` or `static readonly` compiled into the decompiled source is the operative value, offline-checkable and citable to a line. This topic is unusually rich in them — the whole solve cadence is `const int` on two systems.
- **Formulas ship whole.** The expression a system evaluates, its baseline and its step functions are invariant structure. Every consumption and production formula recorded below ships; the parameter values they read do not.
- **The map ships**, with the access shape beside each entry, because a reader cannot write the read from a field name alone. For a parameter singleton the machinery is `GetSingleton<T>` and `ecs-in-this-game` already carries it; for a per-prefab component the shape is a `PrefabRef` hop, and this topic has both.

Two consequences bind particular passages here. **A derived ratio is a magnitude wearing a mechanism's clothes** — "a high-voltage line carries ten times what a road does" is arithmetic over two prefab values and does not ship; the direction ("high-voltage capacity exceeds low-voltage") does. **A non-numeric prefab value is still a prefab value** — `WaterPumpingStationData.m_Types` (`AllowedWaterTypes`) decides whether a pump can draw groundwater at all, and it ships as the field to check before building against a pump, not as a fact about any particular pump.

**Ruled (2026-08-08, the zoning-buildings-and-land-value pass under delegated authority, with a same-day addendum; conflicts.md).** A **field initializer on a `PrefabBase`/`ComponentBase` subclass** is a Unity-serialized default the shipped asset overrides. It ships as **no number**. A reference whose map or traps send a reader into a file carrying them states once, **as a trap**, that these are Unity-serialized defaults the shipped asset overrides, with nothing in the C# marking which survived. The test is what consumes the value: a value the code reads from the class ships as a number; a value Unity deserialisation can replace is a prefab value whatever file declares it.

That ruling lands squarely on this topic, and this pass produced fresh evidence for it — see "The parameter prefabs' initializers are wrong more often than right" below. `ElectricityParametersPrefab` and `WaterPipeParametersPrefab` are both written this way, and 6 of the 11 initializers checked live disagree with the shipped asset.

**Ruled (2026-08-06, the citizens-and-households pass; conflicts.md).** No mechanics reference borrows a wiki stat table's numbers. **First-party or nothing.** The wiki is a lead generator and never a shipped citation. This pass hit the failure mode the ruling was made for: the `Climate` page's cloudiness figure is exactly the C# field initializer and is not what the shipped asset carries (verdict below).

---

## Findings

### The three services are one solver, instantiated twice

`Game.Simulation.Flow` is a self-contained max-flow library with no knowledge of electricity or water. It holds `Node` (`src/Game/Game.Simulation.Flow/Node.cs`), `Edge` (`Edge.cs`), `Connection` (`Connection.cs`), `MaxFlowSolver` (`MaxFlowSolver.cs`), `FluidFlowSolver` (`FluidFlowSolver.cs`), `Layer`, `CutElement`, `Identifier` and their save-state structs — 16 files, 1,698 lines total.

Two systems instantiate it. `Game.Simulation.ElectricityFlowSystem` (`src/Game/Game.Simulation/ElectricityFlowSystem.cs`) solves **one** layer; `Game.Simulation.WaterPipeFlowSystem` (`src/Game/Game.Simulation/WaterPipeFlowSystem.cs`) solves **two**, fresh and sewage, over one shared node/connection topology (`WaterPipeFlowSystem.cs:484-496` schedules two `WaterPipeFlowJob`s against `m_FreshData` and `m_SewageData` and combines their handles).

The two are structurally parallel to the point of being copy-edited from each other: both declare a private `Phase { Prepare, Flow, Apply }` enum (`ElectricityFlowSystem.cs:26-31`, `WaterPipeFlowSystem.cs:25-30`), both hold `PrepareNetworkJob` / `PrepareNodesJob` / `PrepareEdgesJob` / `PrepareConnectionsJob` / `PopulateNodeIndicesJob` / `ApplyEdgesJob`, and both implement `IDefaultSerializable, ISerializable, IPostDeserialize`.

**This is the answer to "why a graph and not a coverage circle".** No cell map decides reach in any of the three services — the only cell map in the topic is the groundwater aquifer, and it constrains what a pump can *produce*. Fulfilment is the value of a maximum flow from a single artificial source node to a single artificial sink node across a graph whose edges are the player's own network. A building either has a path with residual capacity to the source or it does not.

*Rots:* the two system names and the `Game.Simulation.Flow` namespace. Re-check against `src/Game/Game.Simulation/` and `src/Game/Game.Simulation.Flow/`.

### A flow node and a flow edge are entities of their own, in a shadow graph

The solver's `Node`/`Edge` structs are working arrays rebuilt every cycle. The persistent graph is ECS entities.

`ElectricityFlowNode` (`src/Game/Game.Simulation/ElectricityFlowNode.cs:6-9`) is a single `int m_Index` and is `IEmptySerializable` — the index is scratch, recomputed each prepare pass, and nothing about a node is saved.
`WaterPipeNode` (`src/Game/Game.Simulation/WaterPipeNode.cs:6-21`) is the same index plus `float m_FreshPollution`, and serializes only the pollution.
`ElectricityFlowEdge` (`src/Game/Game.Simulation/ElectricityFlowEdge.cs:7-52`) carries `m_Start`, `m_End`, `m_Capacity`, `m_Flow`, `ElectricityFlowEdgeFlags m_Flags`, plus the scratch `m_Index`; `direction` is a property over the low two flag bits.
`WaterPipeEdge` (`src/Game/Game.Simulation/WaterPipeEdge.cs:7-49`) carries `m_Start`, `m_End`, `m_FreshFlow`, `m_FreshPollution`, `m_SewageFlow`, `m_FreshCapacity`, `m_SewageCapacity`, `WaterPipeEdgeFlags m_Flags`, with `flow` and `capacity` convenience `int2` properties at `:27-29`.

Each node entity carries a `DynamicBuffer<ConnectedFlowEdge>` (`src/Game/Game.Simulation/ConnectedFlowEdge.cs:7-24`), an `IEmptySerializable` buffer of edge entities with an implicit conversion to `Entity`. **The adjacency is stored on the node and the endpoints on the edge**, so both directions are walkable and both must be kept consistent — which is precisely what one corpus mod exists to check (see the catalog gaps below).

The archetypes are created in `OnCreate` and exposed as public properties, which is what makes the graph extendable from a mod: `nodeArchetype`, `chargeNodeArchetype`, `dischargeNodeArchetype`, `edgeArchetype`, `sourceNode`, `sinkNode` (`ElectricityFlowSystem.cs:391-401`, archetypes built at `:410-413`; the water equivalents at `WaterPipeFlowSystem.cs:381-382`).

The source and sink entities are created once, in `PostDeserialize`, for a new map or a map with none (`ElectricityFlowSystem.cs:645-649`). Read live at 1.6.0f1: electricity source/sink were entities 97703 and 97704, water 227974 and 227975 — the point being that they are ordinary entities of the plain node archetype, not singletons and not tagged.

*Rots:* every component and field name in this finding.

### The flags are the diagnosis, and the two services diagnose differently

`ElectricityFlowEdgeFlags` (`src/Game/Game.Simulation/ElectricityFlowEdgeFlags.cs:6-15`): `None, Forward = 1, Backward = 2, Bottleneck = 4, BeyondBottleneck = 8, Disconnected = 0x10, ForwardBackward = 3`.
`WaterPipeEdgeFlags` (`src/Game/Game.Simulation/WaterPipeEdgeFlags.cs:6-13`): `None, WaterShortage = 1, SewageBackup = 2, WaterDisconnected = 4, SewageDisconnected = 8`.

**Electricity has a bottleneck concept and water does not.** The apply pass maps the solver's min-cut label onto the flag: `-2 → Bottleneck`, `-3 → BeyondBottleneck`, `-1 → None`, anything else `→ Disconnected` (`src/Game/Game.Simulation/ElectricityFlowSystem.cs:252-261`). The water apply pass maps `-1 → WaterShortage`, anything but `-2 → WaterDisconnected`, and the same pair on the sewage layer (`WaterPipeFlowSystem.cs:246-262`). The labels themselves are `private const` in the two jobs — electricity `kConnectedNodeLabel = -1`, `kShortageNodeLabel = -2`, `kBeforeBottleneckNodeLabel = -3`, `kBeyondBottleneckNodeLabel = -4`, and the public edge labels `kConnectedEdgeLabel = -1`, `kBottleneckEdgeLabel = -2`, `kBeyondBottleneckEdgeLabel = -3` (`src/Game/Game.Simulation/ElectricityFlowJob.cs:48-60`); water's are `kShortageNodeLabel = -1`, `kConnectedNodeLabel = -2`, `kShortageEdgeLabel = -1`, `kConnectedEdgeLabel = -2`, `kSinkEdgeLabel = -200` (`WaterPipeFlowJob.cs:77-83`). **The two services use the same numbers for different meanings**, which is a real trap for anyone reading one job after the other.

*Rots:* both enum member sets and every label constant. Re-check at the two `*FlowEdgeFlags.cs` files and the two flow jobs.

### The graph is built from the net, one middle node per edge

`ElectricityEdgeGraphSystem` runs on newly `Created` net edges carrying `Game.Net.ElectricityConnection` (`src/Game/Game.Simulation/ElectricityEdgeGraphSystem.cs:208`) and does exactly this per edge (`:59-83`):

1. get-or-create a flow node for the net edge's `m_Start` node and another for its `m_End` node;
2. create a **third** flow node for the edge itself, stamped onto the net edge as `ElectricityNodeConnection` (`src/Game/Game.Simulation/ElectricityNodeConnection.cs:6-18`);
3. create flow edges start→middle and middle→end, both taking `m_Capacity` and `m_Direction` from the net prefab's `ElectricityConnectionData`;
4. connect every `ConnectedNode` in the edge's buffer to the middle node the same way (`ElectricityEdgeGraphSystem.cs:86-99`);
5. for each endpoint, walk `ConnectedEdge` and create a flow edge to every neighbouring net edge's middle node that does not already have one (`:101-114`).

`WaterPipeEdgeGraphSystem` is the same shape against `Game.Net.WaterPipeConnection` (`src/Game/Game.Simulation/WaterPipeEdgeGraphSystem.cs:208`), stamping `WaterPipeNodeConnection` and passing `m_FreshCapacity`/`m_SewageCapacity` instead of one capacity and a direction (`:135-138`).

So **a road segment is three flow nodes and at least two flow edges**, not one. Live at 1.6.0f1 the electricity graph held **1,758 `ElectricityFlowNode` and 2,077 `ElectricityFlowEdge` entities** against **925 `ElectricityConsumer`s**, and the water graph **1,542 `WaterPipeNode` and 1,857 `WaterPipeEdge`** — read with `ecs_query` on each component. The graph is the same order of magnitude as the network, not as the building count.

The registration order of the graph builders is a readable map of the modification band and belongs to `mod-lifecycle-and-ordering` (`src/Game/Game.Common/SystemOrder.cs`): delete at `Modification1` (`:108-109`), edges at `Modification2B` (`:133-134`), outside connections at `Modification3` (`:141-142`), buildings at `Modification4B` (`:185-186`), road connections at `Modification5` (`:246-247`), reference fixups at `ModificationEnd` (`:290-291`).

### Which nets carry which utility, and the composition gate

Two prefab components decide it, and they are **not symmetric**.

`Game.Prefabs.ElectricityConnection` (`src/Game/Game.Prefabs/ElectricityConnection.cs:9-38`) is a `ComponentBase` carrying `Voltage m_Voltage` (`Low = 0, High = 1, Invalid = 255`, `:11-16`), `FlowDirection m_Direction`, `int m_Capacity`, and **three `NetPieceRequirements[]` arrays** — `m_RequireAll`, `m_RequireAny`, `m_RequireNone`. `NetInitializeSystem` bakes those into `CompositionFlags` on `ElectricityConnectionData` and errors if any of them is a section flag rather than a piece flag (`src/Game/Game.Prefabs/NetInitializeSystem.cs:2460-2467`).

`ElectricityConnectionData` (`src/Game/Game.Prefabs/ElectricityConnectionData.cs:7-19`) therefore carries `m_Capacity`, `m_Direction`, `m_Voltage`, `m_CompositionAll`, `m_CompositionAny`, `m_CompositionNone`. **Only the first three serialize** (`:21-29`); the composition flags are rebuilt from the prefab at load.

`Game.Prefabs.WaterPipeConnection` (`src/Game/Game.Prefabs/WaterPipeConnection.cs:9-28`) carries three capacities and **no requirements at all**, and adds the runtime `Game.Net.WaterPipeConnection` only when the archetype already has `Edge` (`:22-28`).

The gate fires at placement. `GenerateEdgesSystem` tests the edge's upgraded composition against the prefab's flags and adds or removes `Game.Net.ElectricityConnection` accordingly, and flips the tool's temp flags from `Upgrade` to `Replace` when the answer changed (`src/Game/Game.Tools/GenerateEdgesSystem.cs:1589-1608`). The test itself is `NetCompositionHelpers.TestEdgeFlags(ElectricityConnectionData, CompositionFlags)` — all-of `m_CompositionAll`, none-of `m_CompositionNone`, and any-of `m_CompositionAny` when it is non-default (`src/Game/Game.Prefabs/NetCompositionHelpers.cs:591-602`).

**Read live at 1.6.0f1, this is what the gate actually does:**

- 123 net prefabs carry `ElectricityConnectionData`; 60 carry `WaterPipeConnectionData` (`ecs_query` counts).
- The electricity set includes every road family, every train, tram and subway track, both quay families, every bridge prefab, the high- and low-voltage lines, both ground cables, and the three voltage marker prefabs. The water set includes roads, alleys, gravel roads, pedestrian streets, quays, the six pipe prefabs and the three pipe marker prefabs — **and no rail track and no highway**.
- `Small Road` has all three composition flag sets empty, so the test is unconditional.
- `Highway Twoway - 3 lanes` has `m_CompositionAll = { General = Lighting }`. **A highway carries electricity only once its composition includes street lighting.** Confirmed live: `HighwayHasWaterPipe=False`, `GravelHasWaterPipe=True`, `TrainHasWaterPipe=False`, `PedStreetHasWater=True`, `AlleyHasWater=True`.

**Ruled (2026-08-11, the utilities-and-flow-networks pass under the maintainer's delegated authority; conflicts.md).** Same ruling as the pipe-capacity finding below: the gate mechanism ships flat as C#, and what the asset data instantiates ships as the shape the enumeration shows with the reproducing query beside it — the two `ecs_query` counts, the membership stated as shape (no rail track and no highway in the water set), and the highway's `m_CompositionAll = { Lighting }` as the one cited worked example, never a roster of which road declares what.

Corroboration from the corpus, and it is independent: `RoadBuilder-CSII` **authors** these components on generated road prefabs, and reads the same gate back when importing a vanilla one — `NetworkPrefab.TryGet<ElectricityConnection>(out var electricityConnections) && electricityConnections.m_RequireAll.Contains(NetPieceRequirements.Lighting)` sets its own `RoadAddons.RequiresUpgradeForElectricity` (`RoadBuilder-CSII/RoadBuilder/Utilities/NetworkConfigGenerationUtil.cs:79-82`), and generation emits the mirror image (`RoadBuilder-CSII/RoadBuilder/Utilities/NetworkPrefabGenerationUtil.cs:558-570`).

*Rots:* the prefab component names, `NetPieceRequirements.Lighting`, and every count in this finding. Re-check the counts with the two `ecs_query` calls.

### Water and sewage pipes have no capacity limit; electricity lines do

`ElectricityFlowSystem.kMaxEdgeCapacity = 1073741823` (`src/Game/Game.Simulation/ElectricityFlowSystem.cs:333`) — `int.MaxValue / 2`, the value used for every edge that is meant to be unconstrained. `WaterPipeFlowSystem` declares the identical constant (`src/Game/Game.Simulation/WaterPipeFlowSystem.cs:328`).

Read live at 1.6.0f1: `Small Road`'s `ElectricityConnectionData.m_Capacity = 500000`, `High-voltage Line` 5,000,000, `Low-voltage Line` 1,000,000, `Double Train Track` 500,000 — all finite, and a road flow edge read back off a live entity carried exactly `m_Capacity = 500000`. Against that, **every one of the sixty `WaterPipeConnectionData` carriers in this install reads `1073741823` on the layers it serves and `0` on the ones it does not** — a full census through `ecs_query` on the component plus a per-entity read of all sixty, orchestrator-run on 2026-08-11, installed DLC included since the running game's prefab set merges it: `Small Road` 1073741823/1073741823/0, `Small Water Pipe` 1073741823/0/0, `Small Sewage Pipe` 0/1073741823/0, `Combined Small Pipe` 1073741823/1073741823/0 — **and `Large Water Pipe` is identical to `Small Water Pipe`**. The census is of one install's DLC set, the same caveat the zoning and economy passes carry.

So the pipe you draw has no throughput at 1.6.0f1: pipe size is a cost, a footprint and a visual, and the solver cannot tell a small pipe from a large one. `Bottleneck`/`BeyondBottleneck` exist on the electricity edge type and not the water one because the two label vocabularies differ: `ElectricityFlowJob` runs a `LabelBottlenecks` pass, while `WaterPipeFlowJob` labels only shortage and connectivity — a water min cut still binds (producer edges carry finite capacities), it just earns no bottleneck flag. (Corrected 2026-08-11 during the review gate: this sentence previously claimed electricity lines are the only place a min cut can bind.)

**Ruled (2026-08-11, the utilities-and-flow-networks pass under the maintainer's delegated authority; conflicts.md).** A structural fact read off prefab values is a swept set's shape and ships the way ADR 0006 ships one: as the shape, with the derivation and the query that reproduces it attached, never as a bare fact the plugin vouches for. So the reference states the mechanism flat — `WaterPipeConnectionData` declares a fresh, a sewage and a storm capacity per net prefab, and `WaterPipeEdgeFlags` has no bottleneck member — and states the absence as what the enumeration shows, naming the `ecs_query` on the component as the check, with `Large Water Pipe` against `Small Water Pipe` as the one cited worked example. No prefab roster, and no flat "water pipes have no capacity limit" without its derivation clause.

`ElectricityConnectionGlobalMode` (`src/Game/Game.Prefabs.Modes/ElectricityConnectionGlobalMode.cs:13-36`) multiplies `m_Capacity` over every entity carrying `ElectricityConnectionData` by `m_CapacityMultiplier` and touches nothing else. **A game mode can rescale every line in the game and cannot re-wire which net carries which utility** — the same mechanism/balance line the economy pass found inside `ProcessingCompanyGlobalMode`, restated for this topic.

### The sewage layer carries handling capacity, not sewage

The two water layers share one topology, one `Connection` list and one set of node entities (`WaterPipeFlowSystem.cs:162-194`); only the `Edge` arrays differ. That forces the sewage layer to run in the **same direction** as the fresh layer, source → sink, which is backwards from the physics.

What actually flows on the sewage layer is **capacity to accept sewage**, emitted by treatment plants and outlets and consumed by buildings:

- a building's consumer edge runs consumerNode → sink and takes `m_FreshCapacity = m_SewageCapacity = wantedConsumption` (`src/Game/Game.Simulation/AdjustWaterConsumptionSystem.cs:154-168`);
- a producer's edge runs source → producerNode (`src/Game/Game.Simulation/WaterPipeBuildingGraphSystem.cs:227-232`), and `SewageOutletAISystem` writes `value.m_SewageCapacity = reference.m_Capacity` onto it (`src/Game/Game.Simulation/SewageOutletAISystem.cs:123-129`) while `WaterPumpingStationAISystem` writes `value.m_FreshCapacity` onto the same edge (`src/Game/Game.Simulation/WaterPumpingStationAISystem.cs:178-182`).

The trade capacities confirm the reading. `ScheduleFlowJob(m_FreshData, 1073741823, 1073741823, …)` against `ScheduleFlowJob(m_SewageData, 1073741823, 0, …)` (`WaterPipeFlowSystem.cs:489-490`) gives the sewage layer unlimited **import** and zero **export** — an outside connection can supply unlimited sewage-handling capacity and can never receive any. And `WaterTradeSystem`'s sum job asserts exactly that: on a sink-side edge, `Assert.AreEqual(0, waterPipeEdge.m_SewageFlow)` (`src/Game/Game.Simulation/WaterTradeSystem.cs:57`), while the source-side edge's `m_SewageFlow` is what it counts as `m_SewageExport` (`:62-67`).

**So "sewage exported" is measured on the edge that imports handling capacity.** That inversion is the single most confusing thing in this topic and a reference that omits it leaves a reader reading the sign of every sewage number backwards.

### A building joins the graph one of two ways, and only one of them is per-building

**With its own connection.** `ElectricityBuildingGraphSystem` gives a building an `ElectricityBuildingConnection` when its sub-nets or upgrades contain a marker node **and** the building carries at least one of `Game.Buildings.Transformer`, `ElectricityProducer`, `ElectricityConsumer`, `Game.Buildings.Battery` (`src/Game/Game.Simulation/ElectricityBuildingGraphSystem.cs:137-162`). `ElectricityBuildingConnection` (`ElectricityBuildingConnection.cs:6-36`) holds five entity fields — `m_TransformerNode`, `m_ProducerEdge`, `m_ConsumerEdge`, `m_ChargeEdge`, `m_DischargeEdge` — with four helpers that resolve the node behind each edge through a `ComponentLookup<ElectricityFlowEdge>`, because **only the edges are stored and the nodes are recovered from them** (`:18-36`).

The construction is at `ElectricityBuildingGraphSystem.cs:239-342`: producer edge source→newNode with `FlowDirection.Forward` and capacity 0; consumer edge newNode→sink, forward, capacity 0; charge edge chargeNode→sink and discharge edge source→dischargeNode, both `FlowDirection.None` (the flow job turns them on, see the battery finding); and, where the building is both producer and consumer, a producer→consumer internal edge at `kMaxEdgeCapacity` (`:329-340`).

Marker nodes are found by walking `Game.Net.SubNet` on the building and on each non-inactive `InstalledUpgrade`, keeping net nodes that either carry `ElectricityValveConnection` or are orphans, and whose prefab has `ElectricityConnectionData` (`:189-237`). Live, the three marker prefabs are `High-voltage Marker`, `Low-voltage Marker`, `Low-voltage Marker - Small`, and the water side has `Small Water Marker`, `Small Sewage Marker`, `Small Combined Marker`.

`WaterPipeBuildingConnection` is the two-field version — `m_ProducerEdge`, `m_ConsumerEdge` (`WaterPipeBuildingConnection.cs:6-20`) — with no transformer and no battery.

**Without one: through the road edge, in aggregate.** A consumer with no `ElectricityBuildingConnection` is served by a single flow edge from its road edge's flow node to the sink, whose capacity is the **sum** of the wanted consumption of every such building on that road edge. `ElectricityRoadConnectionGraphSystem` creates, updates or clears that edge on every `RoadConnectionUpdated` event (`src/Game/Game.Simulation/ElectricityRoadConnectionGraphSystem.cs:86-138`, the sum at `:106-122`), and `AdjustElectricityConsumptionSystem`'s `UpdateEdgesJob` re-sums it whenever any member's consumption changed (`src/Game/Game.Simulation/AdjustElectricityConsumptionSystem.cs:226-254`).

This is why `BuildingInitializeSystem` derives per-building **required layers** from the prefab's own data and stamps `BuildingFlags.HasLowVoltageNode` / `HasWaterNode` / `HasSewageNode` / `RequireRoad` (`src/Game/Game.Prefabs/BuildingInitializeSystem.cs:226-299`): `m_ElectricityConsumption > 0` requires `Layer.PowerlineLow`, `m_WaterConsumption > 0` requires `Layer.WaterPipe | Layer.SewagePipe`, a pump or tower requires `Layer.WaterPipe`, an outlet or treatment plant `Layer.SewagePipe`, a transformer `Layer.PowerlineLow` (`:228-260`); a building whose own sub-nets satisfy a layer gets the `Has…Node` flag and is not forced onto a road for it (`:261-298`).

The "not connected" notifications come off the same layer arithmetic in `ConnectionWarningSystem` (`src/Game/Game.Net/ConnectionWarningSystem.cs:1745-1775`, the icons at `:2354-2361`), with two special cases worth naming: a producer that is also a transformer and is connected on low voltage has its high-voltage disconnection cleared (`:1760-1763`), and the fold of a road edge's `m_LocalConnectLayers` into a building's connected set is masked with `& 0xFFFFFFE5` (`:1758`) — clearing exactly `PowerlineLow | WaterPipe | SewagePipe` (`src/Game/Game.Net/Layer.cs:9-12`), so the three core utility layers never arrive by it. (Added 2026-08-11 at the review gate, decompile-verified twice; the discovery pass had recorded only the transformer case.)

### Consumption: electricity, and the only place temperature enters

`AdjustElectricityConsumptionSystem` (`src/Game/Game.Simulation/AdjustElectricityConsumptionSystem.cs`) computes, per building (`:136-193`):

```
c  = ConsumptionData.m_ElectricityConsumption                    // prefab, combined over InstalledUpgrade
c *= ElectricityParameterData.m_TemperatureConsumptionMultiplier.Evaluate(ClimateSystem.temperature)
c *= ServiceFeeParameterData.m_ElectricityFeeConsumptionMultiplier.Evaluate(fee / m_ElectricityFee.m_Default)
c  = AreaUtils.ApplyModifier(c, DistrictModifierType.EnergyConsumptionAwareness)   // if in a district
c *= FlowUtils.GetRenterConsumptionMultiplier(...)               // unless Park or StorageProperty
c  = max(c, 1) if c was > 0                                      // never rounds a live consumer to zero
wanted = MathUtils.RoundToIntRandom(random, c)
wanted /= 10 if BuildingUtils.CheckOption(building, BuildingOption.Inactive)
```

Every step is cited: upgrade combination at `:140-143`, temperature at `:145`, fee at `:132-134` and `:146`, district modifier at `:147-154`, renter multiplier at `:155-160`, the floor-at-1 selects at `:159` and `:163`, rounding at `:166`, the inactive divide at `:167-170`.

**A city service building skips the fee entirely** — a chunk carrying `CityServiceUpkeep` gets multiplier 1 and efficiency factor 1 (`:125-129`).

The renter multiplier is `FlowUtils.GetRenterConsumptionMultiplier` (`src/Game/Game.Simulation/FlowUtils.cs:27-67`), and its whole body reduces to one expression (`:64`):

```
5 * n / (level + 0.5 * (sumEducationLevel / n))
```

where `n` is the number of citizens across the building's `Renter` buffer — household members via `HouseholdCitizen`, or workers via `Employee` — `sumEducationLevel` is their summed `Citizen.GetEducationLevel()`, and `level` is `SpawnableBuildingData.m_Level` or **5** where the building has none (`:63`). It returns 0 for an empty building (`:66`) — **but the floor-at-1 guard is computed before the multiplier** (`flag3 = electricityConsumption > 0f` at `AdjustElectricityConsumptionSystem.cs:157`, applied at `:159`; the water twin at `AdjustWaterConsumptionSystem.cs:138-140`), so a building whose prefab consumption is positive lands at a wanted consumption of 1 when empty, never 0. So consumption rises with occupancy and falls with both building level and occupant education, with a floor of 1. (Corrected 2026-08-11 by the orchestrator against the decompile; this file first concluded an empty building consumes nothing at all.)

The result is written to `ElectricityConsumer.m_WantedConsumption` and, only when it changed, pushed onto the consumer edge's `m_Capacity` and the road edge's update queue (`AdjustElectricityConsumptionSystem.cs:171-188`).

The fee also drives a building-efficiency factor: `BuildingEfficiencyParameterData.m_ElectricityFeeFactor.Evaluate(relativeFee)` into `EfficiencyFactor.ElectricityFee` (`AdjustElectricityConsumptionSystem.cs:134/189-192/475-478`).

### Consumption: water and sewage, which are the same number

`AdjustWaterConsumptionSystem` (`src/Game/Game.Simulation/AdjustWaterConsumptionSystem.cs:97-172`) is the same shape with **four differences**, and each of them is a fact worth shipping:

- **No temperature term.** Nothing in the water path reads `ClimateSystem`.
- **No district modifier.** There is no water counterpart to `EnergyConsumptionAwareness` in this system.
- **Inactive is applied first and as a factor** — `m_WaterConsumption * 0.1f` before the fee (`:134`) — where electricity divides the rounded integer by 10 after (`AdjustElectricityConsumptionSystem.cs:167-170`).
- **The fee terms are the water fee's** — `PlayerResource.Water` against `m_WaterFee.m_Default`, `m_WaterFeeConsumptionMultiplier` (`:376-379`), `m_WaterFeeFactor` (`:381-384`), written as `EfficiencyFactor.WaterFee` (`:170`). (Added 2026-08-11 at the review gate: the count previously stopped at three with the fee swap recorded below but never counted.)

And the load-bearing one: the single `wanted` is written to **both** capacities on the consumer edge, `value.m_FreshCapacity = num3; value.m_SewageCapacity = num3;` (`AdjustWaterConsumptionSystem.cs:160-166`). **A building's sewage demand is identical to its fresh-water demand, always.** Confirmed live at 1.6.0f1: `freshConsumption` and `sewageConsumption` both read 137,556 off `WaterStatisticsSystem`.

Four efficiency factors come out of the water path against electricity's two — `WaterFee` here (`AdjustWaterConsumptionSystem.cs:167-171`), and `WaterSupply`, `DirtyWater`, `SewageHandling` from the dispatch pass.

### Production: electricity, and the only place cloud cover enters

`PowerPlantAISystem` (`src/Game/Game.Simulation/PowerPlantAISystem.cs:119-263`) sums six independent production terms per building and writes the rounded total to both `ElectricityProducer.m_Capacity` and the producer edge's `m_Capacity` (`:246-248`). Each term is a separate prefab component, and each is combined across `InstalledUpgrade` first (`:206-213`):

| Term | Prefab component | Formula |
| --- | --- | --- |
| Fuelled plant | `PowerPlantData.m_ElectricityProduction` | `efficiency * production`, zeroed when `ResourceConsumer.m_ResourceAvailability == 0` (`:266-270`) |
| Garbage | `GarbagePoweredData.m_Capacity`, `m_ProductionPerUnit` | `clamp(GarbageFacility.m_ProcessingRate / productionPerUnit, 0, capacity)` (`:272-275`) |
| Wind | `WindPoweredData.m_Production`, `m_MaximumWind` | from `WindSystem.GetWind(position, windMap)` (`:220-231`) |
| Water | `WaterPoweredData.m_ProductionFactor`, `m_CapacityFactor` | a per-sub-net line integral over the dam curve, sampling water height, depth and velocity on both banks (`:277-322`) |
| Ground water | `GroundWaterPoweredData.m_Production`, `m_MaximumGroundWater` | from the `GroundWater` cell map at the building's position (`:237-240`) |
| Solar | `SolarPoweredData.m_Production` | `clamp(efficiency * production * sunLight, 0, efficiency * production)` (`:324-328`) |

`m_SunLight` is computed once per update on the main thread (`:490-497`):

```
sun = max(0, -PlanetarySystem.SunLight.transform.forward.y) * SunLight.additionalData.intensity / 110000
sun *= lerp(1, 1 - ElectricityParameterData.m_CloudinessSolarPenalty, ClimateSystem.cloudiness.value)
```

`110000` is a C# literal in the system and ships as a number; the penalty is prefab data and does not.

Each term is a `float2` of `(actual, potential)`, and the shortfall between them is converted back into efficiency factors 17–20 — `MaterialSupply`, `WindSpeed`, `WaterDepth`, `SunIntensity` in `EfficiencyFactor` order (`src/Game/Game.Buildings/EfficiencyFactor.cs:22-25`) — by `BuildingUtils.ApproximateEfficiencyFactors(targetEfficiency, weights)` with `weights = (fuelShortfall, windShortfall, water+groundwaterShortfall, solarShortfall)`: hydro and groundwater accumulate into one `float2` so they share the `WaterDepth` slot, and the garbage term carries no weight (`PowerPlantAISystem.cs:232-240/246-259`). The fuelled term is also the one component not combined over `InstalledUpgrade`: it is summed per upgrade, each keeping its own `ResourceConsumer` state (`:189-199`), while the other five combine at `:206-213`. (Both clauses corrected 2026-08-11 by the orchestrator against the decompile.) Factors 17–20 are forced to 1 before the building's own efficiency is computed (`:141-153`) so a plant's output is not multiplied by its own output shortfall.

`ServiceUsage.m_Usage` is set from `m_LastProduction / m_Capacity`, zeroed when the plant is out of fuel (`:160-183`).

**The last-cycle flow, not the capacity, is `m_LastProduction`**: `reference.m_LastProduction = value.m_Flow` reads the producer edge's solved flow (`:159-161`).

### Production: fresh water, and where the cell maps come in

`WaterPumpingStationAISystem` (`src/Game/Game.Simulation/WaterPumpingStationAISystem.cs:77-198`) recomputes a pump's capacity from its water sources each cycle. `WaterPumpingStationData` is `AllowedWaterTypes m_Types` (`Groundwater = 1, SurfaceWater = 2`, `src/Game/Game.Prefabs/AllowedWaterTypes.cs`), `int m_Capacity`, `float m_Purification`.

**Groundwater branch** (`WaterPumpingStationAISystem.cs:136-149`): reads the `GroundWater` cell under the building; the availability fraction is `cell.m_Amount / WaterPipeParameterData.m_GroundwaterPumpEffectiveAmount`; the contribution is `clamp(fraction * m_Capacity, 0, m_Capacity - already)`; pollution contributes `cell.m_Polluted / max(1, cell.m_Amount)` weighted by the contribution. Then it **actually consumes** the aquifer: `ceil(lastProduction * m_GroundwaterUsageMultiplier)` units, capped at what the cell holds, through `GroundWaterSystem.ConsumeGroundWater` (`:145-148`). The `NotEnoughGroundwater` notification fires when the fraction is below 0.75 *and* the cell is below 75% of its own max (`:144`) *and* total availability is under 10% of capacity (`:192-193`).

**Surface-water branch** (`:150-170`): for each `WaterSourceData` sub-object, availability comes from `GetSurfaceWaterAvailability(position, types, waterSurfaceData, m_SurfaceWaterPumpEffectiveDepth)`, pollution from `WaterUtils.SamplePolluted`, and the pump **writes back** into the water body — `componentData.m_Height = -0.0001f * totalAvailability * efficiency` (`:165`), so pumping visibly draws the surface down. `-0.0001f` is a C# literal.

**Neither type** (`m_Types == None`): capacity is the flat prefab capacity, no source, no pollution (`:172-177`). That is the water tower / unconditioned-source case.

The result: `m_Capacity = round(efficiency * availability + purifiedFromOwnOutlet)` and `m_Pollution = (1 - data.m_Purification) * weightedPollution / m_Capacity` (`:178-181`), both pushed onto the producer edge as `m_FreshCapacity` and `m_FreshPollution` (`:180-182`). `EfficiencyFactor.WaterDepth` (index 19) is forced to 1 before the pump's own efficiency is read (`:110/116`), then rewritten as `(availability + lastPurified) / (m_Capacity + lastPurified)` (`:183-191`). The surface arm also writes `m_Polluted = 0f` onto each intake's water source (`:164`) — which can flatten what a co-located outlet wrote — and assigns its scarcity flag per intake, so with several the last one decides (`:163`).

**A pump that is also an outlet recycles its own purified water.** Where the same building carries `SewageOutlet`, `m_UsedPurified = min(lastProduction, lastPurified)` is subtracted from the water it must draw from the ground or the surface (`:123-129`), and the outlet's **full** `m_LastPurified` — not the `m_UsedPurified` minimum — is what capacity adds: `m_Capacity = round(efficiency * availability + m_LastPurified)` (`num2` set at `:126`, added at `:178`). That is the wastewater treatment plant. (Corrected 2026-08-11 by the orchestrator against the decompile; this file first wrote the add ambiguously as "added back".)

### Sewage: outlets, purification, and what goes back into the river

`SewageOutletAISystem` (`src/Game/Game.Simulation/SewageOutletAISystem.cs:69-133`). `SewageOutletData` is `int m_Capacity`, `float m_Purification`; `Game.Buildings.SewageOutlet` is `m_Capacity`, `m_LastProcessed`, `m_LastPurified`, `m_UsedPurified` (`src/Game/Game.Buildings/SewageOutlet.cs:6-14`).

Per cycle, **the discharge runs first and reads the previous cycle's fields** (`SewageOutletAISystem.cs:97-122`): `dirty = max(0, lastProcessed - lastPurified)`, `unusedClean = lastPurified - usedPurified`, `total = dirty + unusedClean`; each `WaterSourceData` sub-object then gets `m_Height = min(2.5f, WaterPipeParameterData.m_SurfaceWaterUsageMultiplier * total)` and `m_Polluted = dirty / total` (`:108-118`). **`2.5f` is a C# literal cap.** So an outlet with 100% purification still raises the water level and pollutes nothing; one with 0% raises it and pollutes fully — and what reaches the river lags the solved flow by one cycle.

Only then do this cycle's writes land (`:123-129`): `m_Capacity = round(efficiency * data.m_Capacity)`, written onto the producer edge's `m_SewageCapacity`; `m_LastProcessed = producerEdge.m_SewageFlow`; `m_LastPurified = round(data.m_Purification * m_LastProcessed)`; `m_UsedPurified` reset to 0 for the pump pass to claim. (Reordered 2026-08-11 by the orchestrator against the decompile; this file first listed the writes before the discharge, which reads as if `m_UsedPurified` were zeroed before the discharge subtracts it.)

`Game.Buildings.WastewaterTreatmentPlant` (`m_StoredWater`, `m_LastStoredWater`) and `Game.Buildings.WaterTower` (`m_StoredWater`, `m_Polluted`, `m_LastStoredWater`) are **read by nothing in `Game.Simulation`**, and `m_StoredWater` is written by nothing outside the components' own `Deserialize`: its one reader is an effects test on `m_LastStoredWater != m_StoredWater` (`src/Game/Game.Effects/EffectControlData.cs:351`), permanently false — the type-name hits in `Game.Rendering/ObjectColorSystem.cs` and `Game.Notifications/MarkerCreateSystem.cs` are presence tests, not field reads (corrected 2026-08-11 by the orchestrator; this file first listed four reader categories that do not read the field). `WaterTowerData` (`src/Game/Game.Prefabs/WaterTowerData.cs:8-10`) and `TransformerData` (`TransformerData.cs:8-10`) are both zero-size tags. **There is no water storage simulation at 1.6.0f1** — a tower is a pump with `m_Types == None`, confirmed live 2026-08-11: all three `WaterTower0x` prefabs carry `WaterPumpingStationData { m_Types = None }`, and `WastewaterTreatmentPlant01` the same with base capacity 0, so its whole fresh capacity is recycled purified water.

### Batteries: two extra nodes and a phase of the solver to themselves

`Game.Buildings.Battery` (`src/Game/Game.Buildings/Battery.cs:6-14`) is `long m_StoredEnergy`, `int m_Capacity`, `int m_LastFlow`, with `storedEnergyHours => m_StoredEnergy / 85` (`:14`). `BatteryData` (`src/Game/Game.Prefabs/BatteryData.cs:6-18`) is `m_Capacity`, `m_PowerOutput`, with `capacityTicks => 85 * m_Capacity` (`:12`) and a `Combine` that adds both (`:14-18`).

**`85` is `kUpdatesPerHour`** (`ElectricityFlowSystem.cs:315`). Stored energy is therefore in flow-units × solver ticks, and the two accessors are the unit conversion between "power" and "energy" in this game. That is the whole of the unit story and belongs beside `units-and-formatting`.

`BatteryAISystem` (`src/Game/Game.Simulation/BatteryAISystem.cs:73-175`) reads the two edges the graph system created, computes `net = chargeEdge.m_Flow - dischargeEdge.m_Flow`, and clamps `m_StoredEnergy + net` into `[0, capacityTicks]` (`:100-105`). It then sets the edge capacities for the next cycle (`:172-175`):

```
dischargeEdge.m_Capacity = efficiency > 0 ? min(m_PowerOutput, m_StoredEnergy) : 0
chargeEdge.m_Capacity    = min(round(efficiency * m_PowerOutput), capacityTicks - m_StoredEnergy)
```

An emergency generator rides on the same building: `EmergencyGeneratorData` is `m_ElectricityProduction` and a `Bounds1 m_ActivationThreshold`, and the generator runs while the charge fraction is below `threshold.min`, or below `threshold.max` while already running — a hysteresis band (`:130-137`). Its output is added straight to stored energy rather than to the graph (`:158`), and while it runs its `PollutionEmitModifier` fields are set to 0 rather than −1 (`:142-148`), which is how a generator pollutes only when firing.

### The solve is a three- (or four-) phase incremental job with a step budget

`ElectricityFlowJob.Phase` is `Initial, Producer, PostProducer, Battery, PostBattery, Trade, PostTrade, Complete` (`ElectricityFlowJob.cs:18-28`). The three `MaxFlowPhase` runs are three complete max-flow solves on the same graph with different edges enabled:

1. **Producer** — batteries and outside connections are off (`FlowDirection.None`), so the solve is production against demand alone.
2. **PostProducer** (`:232-239`) labels every node reachable-with-shortage backwards from the sink (`LabelShortages`, `:241-283`), then enables **discharge** edges only at nodes in a shortage sub-graph (`:285-312`) and **charge** edges only at nodes outside one (`:314-341`). **A battery decides to charge or discharge from the min-cut of the previous solve, per node**, not from a global surplus figure.
3. **Battery** — solve again with those edges on.
4. **PostBattery** (`:343-351`) turns every battery edge back off, re-labels shortages, and enables an outside connection's **import** edge at a shortage node and its **export** edge at a non-shortage node (`EnableTradeConnections`, `:365-388`).
5. **Trade** — solve again.
6. **PostTrade** (`:390-396`) sets import on and export off, then labels connectivity and bottlenecks for the flag pass.

`WaterPipeFlowJob.Phase` is `Initial, Producer, PostProducer, Trade, PostTrade, FluidFlow, Complete` (`WaterPipeFlowJob.cs:19-27`) — no battery phase, plus a final `FluidFlow` pass, gated on `WaterPipeFlowSystem.fluidFlowEnabled` (`:362-371`).

**The `FluidFlowSolver` is a second, cosmetic-ish pass over the max-flow result.** `MaxFlowSolver` returns *a* maximum flow, and which of many equal-value flows it returns is arbitrary; `FluidFlowSolver` (`src/Game/Game.Simulation.Flow/FluidFlowSolver.cs`) reruns the assignment with two `NativeMinHeap`s — `LabelHeapData` for a shortest-path label pass, `PushHeapData` for a push pass (`WaterPipeFlowJob.cs:519-529`) — so the water visibly takes short routes. It preflows from the sink backwards (`FluidFlowSolver.cs:38-50`) and steps label/push until the push queue empties (`:110-121`).

**The budget.** Both jobs run under `int budget = max(100, m_LastTotalSteps / 124)` per frame, where `m_LastTotalSteps` is the step count the previous complete solve took (`ElectricityFlowJob.cs:112-117`, `Finalize` at `:125-131`). On the final frame the loop ignores the budget and runs to completion (`:114`, and inside `MaxFlowPhase` at `:210`). `m_LastTotalSteps` is the **only** thing either flow system serializes besides the source and sink entities — water keeping one per layer (`ElectricityFlowSystem.cs:579-588`, `WaterPipeFlowSystem.cs:545-548`) — so a save carries the solver's own workload estimate.

The initial electricity estimate is `new ElectricityFlowJob.State(20000)` (`ElectricityFlowSystem.cs:421`, and again in `Reset()` at `:451`) — 20000/124 ≈ 161 steps in the first cycle before the estimate self-corrects; each water layer starts ten times larger, `State(200000)` (`WaterPipeFlowSystem.cs:406-407`).

Errors are handled by a poison flag: `m_Error` is set true at the top of `Execute` and cleared at the bottom, so a throw inside leaves it set and every later frame short-circuits until the final frame logs `"Electricity solver error in phase: {phase}"` and resets (`ElectricityFlowJob.cs:99-123`; the water twin logs `"Water pipe solver error in phase: {phase}"` at `WaterPipeFlowJob.cs:133`). That is the one diagnostic this subsystem prints.

`MaxFlowSolver.kMaxNodes = 16777216` (`src/Game/Game.Simulation.Flow/MaxFlowSolver.cs:13`) doubles as the initial node height in `ResetNetwork` (`:118-136`), which is the classic push-relabel sentinel.

### The cadence, in frames, and why the two solvers never collide

Both flow systems are registered plainly into `SystemUpdatePhase.GameSimulation` (`SystemOrder.cs:442-443`) with **no `GetUpdateInterval` override**, so they update every simulation frame and gate themselves on `SimulationSystem.frameIndex % 128`.

`ElectricityFlowSystem` constants (`ElectricityFlowSystem.cs:311-333`) and `WaterPipeFlowSystem` constants (`WaterPipeFlowSystem.cs:306-330`):

| | electricity | water |
| --- | --- | --- |
| `kUpdateInterval` | 128 | 128 |
| `kUpdateOffset` | — | 64 |
| `kUpdatesPerDay` | 2048 | 2048 |
| `kUpdatesPerHour` | 85 | — |
| `kAdjustFrame` | 0 | 64 |
| `kPrepareFrame` | 1 | 65 |
| `kFlowFrames` | 124 | 124 |
| `kFlowCompletionFrame` | 125 | 61 |
| `kApplyFrame` | 126 | 62 |
| `kStatusFrame` | 127 | 63 |
| `kMaxEdgeCapacity` | 1073741823 | 1073741823 |
| `kLayerHeight` (private) | 20 | 20 |

The gates match: prepare at `frameIndex % 128 == 1` (`ElectricityFlowSystem.cs:474`) and `== 65` (`WaterPipeFlowSystem.cs:430`); apply at `== 126` (`ElectricityFlowSystem.cs:566`) and `== 62` (`WaterPipeFlowSystem.cs:524`); the flow phase asserts it is on none of those four frames (`ElectricityFlowSystem.cs:533`, `WaterPipeFlowSystem.cs:487`). **The water solver runs exactly half a cycle out of phase with the electricity solver**, which keeps each solver's four bookkeeping frames clear of the other's; the budgeted solve frames overlap on most of the cycle, each job under its own budget. (Corrected 2026-08-11 by the orchestrator against the two asserts; this file first concluded the two never contend for the same frame budget.)

Everything else in the topic hangs off the same 128-frame interval with an offset chosen to land in the right slot:

| System | interval | offset | source |
| --- | --- | --- | --- |
| `AdjustElectricityConsumptionSystem` | 128 | 0 | `src/Game/Game.Simulation/AdjustElectricityConsumptionSystem.cs:385-393` |
| `BatteryAISystem` | 128 | 0 | `src/Game/Game.Simulation/BatteryAISystem.cs:258-266` |
| `PowerPlantAISystem` | 128 | 0 | `src/Game/Game.Simulation/PowerPlantAISystem.cs:462-470` |
| `DispatchElectricitySystem` | 128 | 126 | `src/Game/Game.Simulation/DispatchElectricitySystem.cs:222-230` |
| `ElectricityTradeSystem` | 128 | 126 | `src/Game/Game.Simulation/ElectricityTradeSystem.cs:145-153` |
| `ElectricityStatusSystem` | 128 | 127 | `src/Game/Game.Simulation/ElectricityStatusSystem.cs:196-204` |
| `ElectricityStatisticsSystem` | 128 | 127 | `src/Game/Game.Simulation/ElectricityStatisticsSystem.cs:156-164` |
| `AdjustWaterConsumptionSystem` | 128 | 64 | `src/Game/Game.Simulation/AdjustWaterConsumptionSystem.cs:352-360` |
| `WaterPumpingStationAISystem` | 128 | 64 | `src/Game/Game.Simulation/WaterPumpingStationAISystem.cs:313-321` |
| `SewageOutletAISystem` | 128 | 64 | `src/Game/Game.Simulation/SewageOutletAISystem.cs:233-241` |
| `GroundWaterSystem` | 128 | 64 | `src/Game/Game.Simulation/GroundWaterSystem.cs:127-135` |
| `GroundWaterPollutionSystem` | 128 | 64 | `src/Game/Game.Simulation/GroundWaterPollutionSystem.cs:38-46` |
| `DispatchWaterSystem` | 128 | 62 | `src/Game/Game.Simulation/DispatchWaterSystem.cs:278-286` |
| `WaterTradeSystem` | 128 | 62 | `src/Game/Game.Simulation/WaterTradeSystem.cs:187-195` |
| `WaterStatisticsSystem` | 128 | 63 | `src/Game/Game.Simulation/WaterStatisticsSystem.cs:158-166` |
| `WaterPipePollutionSystem` | 64 | — | `src/Game/Game.Simulation/WaterPipePollutionSystem.cs:155-158` |

**The order inside one 128-frame cycle is the mechanism, not a coincidence**: adjust (write capacities) → prepare (snapshot the graph) → 124 frames of solving → apply (write flows back onto the edge components) → dispatch (turn flows into per-building fulfilment) and trade (bill it) → status and statistics.

**Consumption is not recomputed for every building every cycle.** `AdjustElectricityConsumptionSystem` filters on the shared `UpdateFrame` component against `SimulationUtils.GetUpdateFrame(frameIndex, 128, 16)` (`AdjustElectricityConsumptionSystem.cs:414`, the filter at `:108-111`), which is `(frame / (262144 / (128 * 16))) & 15` = `(frame / 128) & 15` (`src/Game/Game.Simulation/SimulationUtils.cs:158-161`). Sixteen groups, one per cycle, so **each building's wanted consumption is recomputed 128 times per in-game day** — which is exactly the system's own `private const int kFullUpdatesPerDay = 128` (`AdjustElectricityConsumptionSystem.cs:365`). `TimeSystem.kTicksPerDay = 262144` (`src/Game/Game.Simulation/TimeSystem.cs:18`) is the constant that makes the arithmetic close. `AdjustWaterConsumptionSystem` uses the same call and constant.

Both adjust systems assert their own interval at construction: `Assert.IsTrue(GetUpdateInterval(SystemUpdatePhase.GameSimulation) >= 128)` (`AdjustElectricityConsumptionSystem.cs:399`, `AdjustWaterConsumptionSystem.cs:366`) — a first-party statement that lowering the interval is unsupported.

*Rots:* every constant, frame index, system name and interval in this finding. Re-check the two constant tables at the two flow systems and each listed system's `GetUpdateInterval`/`GetUpdateOffset`.

### `ready` is false until a full cycle completes, and the dispatch does nothing until then

`ElectricityFlowSystem.ready` and `WaterPipeFlowSystem.ready` are set true only in `ApplyPhase` (`ElectricityFlowSystem.cs:575`, `WaterPipeFlowSystem.cs:534`) and set false only in `Reset()` (`ElectricityFlowSystem.cs:452`, `WaterPipeFlowSystem.cs:407`), which `PostDeserialize` calls unconditionally (`ElectricityFlowSystem.cs:705`). `DispatchWaterSystem.OnUpdate` is wrapped in `if (m_WaterPipeFlowSystem.ready)` (`src/Game/Game.Simulation/DispatchWaterSystem.cs:303`).

Observed live at 1.6.0f1 on a loaded, **paused** city at frame 8,435,350: `elecReady=False`, `waterReady=False`, `fluidFlowEnabled=True`. That is the expected state — the simulation had not advanced a frame since the load, so neither solver had reached its apply frame.

**The trap this hands a mod author, and a live prober:** immediately after a load, and for as long as the game is paused, `ElectricityConsumer.m_FulfilledConsumption` and `WaterConsumer.m_FulfilledFresh`/`m_FulfilledSewage` hold whatever the save carried, and no system will refresh them. A mod that reads fulfilment in `OnGameLoadingComplete` reads the previous session's answer.

### Applying the result: two different splits, one for the simulation and one for the UI

`DispatchElectricitySystem` (`src/Game/Game.Simulation/DispatchElectricitySystem.cs:59-124`) turns solved flow into `m_FulfilledConsumption`:

- **Own connection:** `fulfilled = min(consumerEdge.m_Flow, m_WantedConsumption)` (`:105`), with `beyondBottleneck` and `disconnected` read off the same edge's flags (`:76-87`).
- **Road-edge aggregate:** if the shared edge is saturated (`m_Capacity == m_Flow`) the building gets all it wanted; otherwise `floor(wanted * flow / capacity)` (`:91-104`). **A pro-rata share, floored, so every building on an undersupplied road is equally short.**

`Connected` is then set when a wanting building got all of it, or when a building wanting nothing is not on a disconnected edge (`:110-117`) — that second clause is why an empty lot still shows a power symbol.

The warning is on a cooldown counter, not on the instant: `m_CooldownCounter` climbs while short, capped at 10000, and the icon appears once it reaches `kAlertCooldown` (`:126-143`). `public static readonly short kAlertCooldown = 2` (`:208`) — and the identically-named field in `DispatchWaterSystem` is also 2 (`src/Game/Game.Simulation/DispatchWaterSystem.cs:256`). Both are `static readonly` and so are operative C# values that ship as numbers. Which icon appears depends on `isBeyondBottleneck`: the plain "no electricity" icon, or the building-bottleneck icon (`DispatchElectricitySystem.cs:134`).

`DispatchWaterSystem` (`src/Game/Game.Simulation/DispatchWaterSystem.cs:65-182`) handles the fresh and sewage layers separately over the same two arms, plus pollution: `m_Pollution = freshFlow > 0 ? edge.m_FreshPollution : 0` (`:143`), and a dirty-water icon above `WaterPipeParameterData.m_MaxToleratedPollution` (`:142/158-171`). Four differences from the electricity dispatch, added 2026-08-11 by the orchestrator against the decompile after "is the same" shipped them wrong: fulfilment takes the edge's **raw** flow with no `min(flow, wanted)` clamp (`:86-87/133-134`) — a shape difference only, since the consumer edge's capacity is the same wanted figure — the cooldowns are bytes capped at `byte.MaxValue` rather than 10000 (`:189-192`), `WaterConsumerFlags` carries connectivity only — `None/WaterConnected/SewageConnected`, rebuilt each pass (`:149-157`, `src/Game/Game.Buildings/WaterConsumerFlags.cs:8-10`) — and a non-wanting building's connectivity is re-derived from the edge's flag pair after the cooldown pass (`:144-148`). The `Locked` gate is NOT a difference: both dispatch systems skip their cooldown calls against their own asset-menu prefab (`:137-141`, `DispatchElectricitySystem.cs:106-108`) while the efficiency factors still compute from the frozen counters (`:172-180`), and the water gate additionally covers the dirty-water icon (`:158-171`). (Corrected 2026-08-11 at the review gate: the gate was listed as the fourth water-side difference.)

Efficiency comes out of both (`DispatchWaterSystem.cs:172-180`, `DispatchElectricitySystem.cs:118-122`), and the formula is worth shipping:

```
ElectricitySupply = 1 - m_ElectricityPenalty * saturate(cooldown / m_ElectricityPenaltyDelay)
WaterSupply       = 1 - m_WaterPenalty      * saturate(freshCooldown / m_WaterPenaltyDelay)
DirtyWater        = 1 - m_WaterPollutionPenalty * round(pollution * 100) / 100
SewageHandling    = 1 - m_SewagePenalty     * saturate(sewageCooldown / m_SewagePenaltyDelay)
```

**All notifications in both dispatch systems are gated on progression**: they are skipped entirely while the service's asset menu prefab is `Locked` (`DispatchElectricitySystem.cs:61`, `DispatchWaterSystem.cs:67`).

**The UI splits the same aggregate differently.** `FlowUtils.ConsumeFromTotal(demand, ref totalSupply, ref totalDemand)` (`src/Game/Game.Simulation/FlowUtils.cs:12-25`) walks a road edge's buildings apportioning a shared supply, clamped between the pessimistic `totalSupply - (totalDemand - demand)` and `totalSupply`. It is called from exactly three places, all presentation: `src/Game/Game.UI.Tooltip/RaycastElectricityTooltipSystem.cs:307`, `src/Game/Game.UI.Tooltip/RaycastWaterTooltipSystem.cs:273`, and `src/Game/Game.Rendering/NetColorSystem.cs:2117`. **No simulation system calls it.** A reader who finds it first and assumes it is the fulfilment rule has the wrong answer.

### Trade with outside connections, and how it becomes money

`ElectricityOutsideConnectionGraphSystem` (`src/Game/Game.Simulation/ElectricityOutsideConnectionGraphSystem.cs:36-52`) adds `TradeNode` to the outside connection's flow node and creates two `kMaxEdgeCapacity` edges — source→node and node→sink — both `FlowDirection.None`, waiting for the flow job to enable one of them.

`ElectricityTradeSystem` (`src/Game/Game.Simulation/ElectricityTradeSystem.cs:22-104`) sums, over every `TradeNode`'s `ConnectedFlowEdge` buffer, flow on edges whose `m_End` is the sink (export) and whose `m_Start` is the source (import), then bills:

```
exportAmount = exportFlow / 2048
importAmount = importFlow / 2048
exportCost   = exportAmount * OutsideTradeParameterData.m_ElectricityExportPrice
importCost   = importAmount * OutsideTradeParameterData.m_ElectricityImportPrice
```

queued as `ServiceFeeSystem.FeeEvent` with `m_Outside = true` and the import amount **negated** (`:80-104`). The `2048` is `kUpdatesPerDay`, so the division converts a per-tick flow into a per-day amount.

`WaterTradeSystem` (`src/Game/Game.Simulation/WaterTradeSystem.cs:23-130`) is the same with four sums instead of two — fresh export, polluted export, fresh import, sewage export — and two extra rules:

- **Exported water is discounted for pollution**: `pollutedExport += min(round(freshPollution / m_WaterExportPollutionTolerance * freshFlow), freshFlow)` (`:59-60`), and the export revenue is `(freshExport - pollutedExport) / 2048 * m_WaterExportPrice` (`:99-103`). Dirty water sold abroad earns nothing.
- **Fresh export is capped at what the city actually has spare**: `m_FreshExport.Count = max(min(m_AvailableWater, m_FreshExport.Count), 0)` (`:98`).

### Water pollution travels through the pipes

`WaterPipePollutionSystem` (`src/Game/Game.Simulation/WaterPipePollutionSystem.cs`) runs at interval 64 — twice per flow cycle — and is two jobs.

`NodePollutionJob` (`:20-73`) sets each node's `m_FreshPollution` to the **flow-weighted mean** of its incoming edges' pollution, counting an edge as incoming when its `m_Start` is not this node, or when it is and the flow is negative (`:47-51`). A node with no inflow decays instead: `max(0, pollution - WaterPipeParameterData.m_StaleWaterPipePurification)` (`:56-59`).

`EdgePollutionJob` (`:74-105`) pushes it back onto the edges: an edge takes its start node's pollution when flow is positive, its end node's when negative, and the **mean of both** when the flow is exactly zero (`:88-95`). Edges whose start is the source node are skipped, because their pollution is the pump's own.

`m_Purify` (`:86`, `:91-94`) only lets a value through when it is zero — a purification tick that can clean but never dirty.

The spread rate is `WaterPipeParameterData.m_WaterPipePollutionSpreadInterval`, whose tooltip says "The interval at which pollution spreads in pipes. Higher numbers = slower spread and faster cleanup" (`src/Game/Game.Prefabs/WaterPipeParametersPrefab.cs:52-54`).

### Groundwater is a 256×256 cell map with diffusion and self-purification

`GroundWater` (`src/Game/Game.Simulation/GroundWater.cs:6-52`) is three `short`s — `m_Amount`, `m_Polluted`, `m_Max` — with a `Consume(int)` that keeps the pollution ratio constant (`:14-22`) and a 6-byte stride (`:49-52`).

`GroundWaterSystem` is a `CellMapSystem<GroundWater>` with `public static readonly int kTextureSize = 256` (`src/Game/Game.Simulation/GroundWaterSystem.cs:123`), over `CellMapSystem<T>.kMapSize = 14336` metres (`src/Game/Game.Simulation/CellMapSystem.cs:100`) — so **56 m per cell**, from `kMapSize / textureSize` (`:185`).

Each update (`GroundWaterSystem.cs:80-120`) it diffuses over right and down neighbour pairs, then replenishes and purifies per cell:

```
pollution move = clamp((own share of the pair's pollution at uniform concentration
                        - own m_Polluted) / 4, bounded by each side's clean headroom / 4)   (:33-35)
amount move    = clamp((neighbour fill deficit - own fill deficit) / 4, ...)                (:54-55)
                 -- fill deficit is m_Amount - m_Max, so water flows toward the cell
                    further below its own per-cell ceiling (m_Max, authored map data), and the
                    moved water carries its source's pollution ratio (:58-68); both /4 are integer
then per cell, the two accumulated neighbour moves land:
m_Amount   = min(m_Amount + amountMoves + ceil(m_GroundwaterReplenish * m_Max), m_Max)
m_Polluted = clamp(m_Polluted + pollutionMoves - m_GroundwaterPurification, 0, m_Amount)
```
(`:108-114`). (The diffusion lines were corrected 2026-08-11 by the orchestrator against the decompile; this file first wrote both passes as "a quarter of the difference". The per-cell lines were corrected 2026-08-18 by the environment-and-pollution gate: the accumulated moves land in pass 3, which this file had dropped, and the ceiling is `m_Max` map data, not Perlin noise — `SetDefaults`' Perlin fill is dead for current saves.) The purification field's tooltip states the tick rate directly: "How much the groundwater cell purifies itself per tick (2048 ticks per day)" (`src/Game/Game.Prefabs/WaterPipeParametersPrefab.cs:37-38`).

`GroundWaterPollutionSystem` (`src/Game/Game.Simulation/GroundWaterPollutionSystem.cs:11-32`) samples `GroundPollution` bilerped at the cell's centre — an equal-weight average of four pollution cells (`:24`) — and adds `sample / 200` per tick, integer division so a sample under 200 adds exactly zero, clamped to the cell's water amount. **`200` is a C# literal.**

`GroundWaterPoweredData` (`m_Production`, `m_MaximumGroundWater`) makes the same map a power source — the geothermal plant — through `PowerPlantAISystem.GetGroundWaterProduction` (`src/Game/Game.Simulation/PowerPlantAISystem.cs:554`).

### Temperature and cloud cover, and the wiki's two claims

**Verdict: the wiki's temperature-curve description is correct at 1.6.0f1, and it is prefab data all the same.** `Climate` (https://cs2.paradoxwikis.com/Climate, fetched 2026-08-11, banner "At least some were last verified for version 1.0") states: "The city consumes the least electricity at temperatures between 18°C/64°F and 22°C/71°F. Above and below those temperatures the electricity consumption increases until it reaches 200% below -18°C/-0.4°F or above 58°C/136°F."

Read live from the `ElectricityParameterData` singleton's `AnimationCurve1 m_TemperatureConsumptionMultiplier` (entity 29:1, evaluated through `eval`): `-30 → 2`, `-18 → 2`, `-17 → 1.9704`, `17 → 1.0029`, `18 → 1.0003`, `20 → 1`, `22 → 1.0003`, `23 → 1.0029`, `57 → 1.9704`, `58 → 2.0`, `70 → 2.0`. The curve's own fields read `m_MinTime = -18`, `m_LengthFactor = 0.39473686`, `length = 58`.

The wiki matches to the digit. It still may not be borrowed: the ruling is first-party or nothing, and the operative value is on a prefab. **What ships is the shape** — a U with a flat minimum in a comfort band, rising symmetrically to a cap at both ends and clamped beyond them — plus the component, the field, and the call: `GetSingleton<ElectricityParameterData>().m_TemperatureConsumptionMultiplier.Evaluate(ClimateSystem.temperature)`, which is what `AdjustElectricityConsumptionSystem.GetTemperatureMultiplier` does (`src/Game/Game.Simulation/AdjustElectricityConsumptionSystem.cs:461-468`), returning `1f` when the singleton is absent (`:463-466`).

**Verdict: the wiki's cloudiness figure is wrong at 1.6.0f1, and its source is visible.** The same page states "On cloudy weather solar power plants produce 25% less electricity." `ElectricityParametersPrefab.m_CloudinessSolarPenalty = 0.25f` is the class's **field initializer** (`src/Game/Game.Prefabs/ElectricityParametersPrefab.cs:19-21`); the live singleton reads **0.1**. The wiki reported the C# default, and the shipped asset overrode it. The authoritative source is the install's own asset, read through the running game (`docs/SOURCES.md` entries 5 and 8) — the wiki loses, exactly as `docs/SOURCES.md` entry 10 prescribes for internals.

**Nothing else in this topic reads the climate.** Water consumption, sewage capacity, groundwater replenishment and pipe pollution are all temperature-independent; the only two climate inputs are the temperature curve on electricity consumption and `ClimateSystem.cloudiness` on solar output (`PowerPlantAISystem.cs:497`). Wind enters through `WindSystem`, not `ClimateSystem` (`:222`).

### The parameter prefabs' initializers are wrong more often than right

`ElectricityParametersPrefab : PrefabBase` (`src/Game/Game.Prefabs/ElectricityParametersPrefab.cs:10`) declares initializers at `:14` and `:21` and copies all of its fields into `ElectricityParameterData` in `LateInitialize` (`:67-88`).
`WaterPipeParametersPrefab : PrefabBase` (`src/Game/Game.Prefabs/WaterPipeParametersPrefab.cs:9`) declares nine at `:35-58` and copies them at `:83-111`.

Initializer against live singleton, 1.6.0f1:

| Field | Initializer | Live | |
| --- | --- | --- | --- |
| `m_InitialBatteryCharge` | 0.1 | **0.5** | differs |
| `m_CloudinessSolarPenalty` | 0.25 | **0.1** | differs |
| `m_GroundwaterReplenish` | 0.004 | **0.05** | differs |
| `m_GroundwaterPurification` | 1 | 1 | same |
| `m_GroundwaterUsageMultiplier` | 0.1 | 0.1 | same |
| `m_GroundwaterPumpEffectiveAmount` | 4000 | **3000** | differs |
| `m_SurfaceWaterUsageMultiplier` | 5e-05 | **1e-06** | differs |
| `m_SurfaceWaterPumpEffectiveDepth` | 4 | 4 | same |
| `m_MaxToleratedPollution` | 0.1 | **0.05** | differs |
| `m_WaterPipePollutionSpreadInterval` | 5 | 5 | same |
| `m_StaleWaterPipePurification` | 0.001 | 0.001 | same |

**Six of eleven differ, one of them by a factor of 50.** Nothing in the C# marks which. `m_GroundwaterReplenish` and `m_GroundwaterPumpEffectiveAmount` both feed the groundwater balance, and a reader taking the initializers would be off in opposite directions on the two halves of the same calculation.

**One exception, and it matters for a mod author.** A mod that builds a prefab component in code has no Unity asset behind it, so its own initializers *are* the operative values. `RoadBuilder-CSII` relies on this: `yield return ScriptableObject.CreateInstance<WaterPipeConnection>()` with no field assignment at all (`RoadBuilder-CSII/RoadBuilder/Utilities/NetworkPrefabGenerationUtil.cs:608`), taking `m_FreshCapacity = m_SewageCapacity = 1073741823` and `m_StormCapacity = 0` straight from `src/Game/Game.Prefabs/WaterPipeConnection.cs:11-15`. The ruling's test — what consumes the value — gives the right answer in both directions.

*Rots:* the field lists on both prefab classes.

### Nineteen game-mode classes rebuild this topic's parameters on every load

`GameModeSystem` runs `RestoreDefaultData` then `ApplyMode` on each load, and this topic has nineteen mode classes under `src/Game/Game.Prefabs.Modes/`: `ElectricityParametersMode`, `WaterPipeParametersMode`, `OutsideTradeParametersMode` (rewrites `OutsideTradeParameterData`, the water trade prices and pollution tolerance included), `ElectricityConnectionGlobalMode`, `ServiceConsumptionGlobalMode` and `ZoneServiceConsumptionGlobalMode` (both rescale `ConsumptionData.m_ElectricityConsumption` and `m_WaterConsumption`), `BuildingEfficiencyParametersMode`, `ServiceFeeParameterMode`, `SoilWaterMode`, and the ten `LocalModePrefab` production modes — `GroundWaterPoweredMode`, `WaterPoweredMode`, `WaterPumpingStationMode`, `PowerPlantMode`, `BatteryMode`, `SolarPoweredMode`, `WindPoweredMode`, `GarbagePoweredMode`, `EmergencyGeneratorMode`, `SewageOutletMode` — each rewriting via `ApplyModeData` a production component the reference's tables name. (Corrected 2026-08-11 during the review gate, twice: the census stopped at nine, then at sixteen — each sweep matched only the family shapes the previous one held. The re-check is not a recount: grep `Game.Prefabs.Modes/` for every component name this file carries.)

Three shapes are represented and they are worth distinguishing:

- `EntityQueryModePrefab` rebuilding a singleton field by field — `ElectricityParametersMode` (`src/Game/Game.Prefabs.Modes/ElectricityParametersMode.cs:11-30`), whose own initializers are `m_InitialBatteryCharge = 0.1f` and `m_CloudinessSolarPenalty = 0.25f` (`:13-19`), the same stale defaults as the parameters prefab;
- `EntityQueryModePrefab` rescaling every entity carrying a component — `ElectricityConnectionGlobalMode` (`src/Game/Game.Prefabs.Modes/ElectricityConnectionGlobalMode.cs:13-36`), which multiplies `ElectricityConnectionData.m_Capacity` and touches nothing else;
- `LocalModePrefab` naming specific prefabs — `WaterPumpingStationMode.ModeData { m_Prefab, m_CapacityMultifier, m_Purification }` (`src/Game/Game.Prefabs.Modes/WaterPumpingStationMode.cs:10-19`; the typo in `m_CapacityMultifier` is the game's).

Which mode a save applies is authored asset data, so no static read detects it. That is the second half of why a number in this topic cannot ship.

### The parameter map, with access shapes

**Singletons — `GetSingleton<T>()`, `ecs-in-this-game`'s route:**

| Component | Owns |
| --- | --- |
| `Game.Prefabs.ElectricityParameterData` | `m_InitialBatteryCharge`, `m_TemperatureConsumptionMultiplier`, `m_CloudinessSolarPenalty`, plus eleven notification/service/menu prefab references (`src/Game/Game.Prefabs/ElectricityParameterData.cs:6-35`) |
| `Game.Prefabs.WaterPipeParameterData` | `m_GroundwaterReplenish`, `m_GroundwaterPurification`, `m_GroundwaterUsageMultiplier`, `m_GroundwaterPumpEffectiveAmount`, `m_SurfaceWaterUsageMultiplier`, `m_SurfaceWaterPumpEffectiveDepth`, `m_MaxToleratedPollution`, `m_WaterPipePollutionSpreadInterval`, `m_StaleWaterPipePurification`, plus twelve prefab references (`src/Game/Game.Prefabs/WaterPipeParameterData.cs:5-49`) |
| `Game.Prefabs.BuildingEfficiencyParameterData` | `m_ElectricityPenalty`, `m_ElectricityPenaltyDelay`, `m_ElectricityFeeFactor`, `m_WaterPenalty`, `m_WaterPenaltyDelay`, `m_WaterPollutionPenalty`, `m_SewagePenalty`, `m_SewagePenaltyDelay`, `m_WaterFeeFactor` (`src/Game/Game.Prefabs/BuildingEfficiencyParameterData.cs:12-28`) |
| `Game.Prefabs.ServiceFeeParameterData` | `m_ElectricityFee` and `m_WaterFee` (each a `FeeParameters` with `m_Default` and `m_Adjustable`), `m_ElectricityFeeConsumptionMultiplier`, `m_WaterFeeConsumptionMultiplier` (`src/Game/Game.Prefabs/ServiceFeeParameterData.cs:10/12/24/26`) — the topic shares this component with six other services |
| `Game.Prefabs.OutsideTradeParameterData` | `m_ElectricityImportPrice`, `m_ElectricityExportPrice`, `m_WaterImportPrice`, `m_WaterExportPrice`, `m_WaterExportPollutionTolerance`, `m_SewageExportPrice` (`src/Game/Game.Prefabs/OutsideTradeParameterData.cs:9-19`) |

**Per-net-prefab — reachable from an instance through `PrefabRef`, or by an `ecs_query` on the component itself since prefab entities carry `PrefabData` rather than Unity's `Prefab` tag:**
`ElectricityConnectionData` (capacity, direction, voltage, three composition-flag sets), `WaterPipeConnectionData` (fresh, sewage, storm capacity), `UtilityLaneData` (`m_UtilityTypes`, `m_VisualCapacity`, `m_Hanging`, three prefab references), `PipelineData` (a tag).

**Per-building-prefab — the same `PrefabRef` hop, and each is `ICombineData` so `UpgradeUtils.CombineStats` folds installed upgrades in before use (except `PowerPlantData` and `EmergencyGeneratorData`, which `PowerPlantAISystem` and `BatteryAISystem` sum by hand per upgrade so each upgrade keeps its own fuel or activation state — the production and battery findings have the reads):**
`ConsumptionData` (`m_Upkeep`, `m_ElectricityConsumption`, `m_WaterConsumption`, `m_GarbageAccumulation`, `m_TelecomNeed`), `PowerPlantData`, `SolarPoweredData`, `WindPoweredData`, `GarbagePoweredData`, `GroundWaterPoweredData`, `WaterPoweredData`, `BatteryData`, `EmergencyGeneratorData`, `WaterPumpingStationData`, `SewageOutletData`, `WastewaterTreatmentPlantData`; and the two tags `TransformerData`, `WaterTowerData`.

**A trap that belongs beside the map.** `ConsumptionData.AddArchetypeComponents` gives a building `ElectricityConsumer` only when `m_ElectricityConsumption > 0f` and `WaterConsumer` only when `m_WaterConsumption > 0f` (`src/Game/Game.Prefabs/ConsumptionData.cs:21-39`). **Raising a prefab's consumption from zero at runtime does not make the building a consumer** — the component is not there. `ServiceConsumption : ComponentBase, IServiceUpgrade` is the authoring side, and it routes through `GetArchetypeComponents` only when the prefab is not itself a `ServiceUpgrade`, and through `GetUpgradeComponents` when it is (`src/Game/Game.Prefabs/ServiceConsumption.cs:29-40`).

*Rots:* every component and field name in both maps. Re-check at their declaration files under `src/Game/Game.Prefabs/`.

### Statistics: what the city-level numbers actually mean

`IElectricityStatisticsSystem` is `production`, `consumption`, `fulfilledConsumption`, `batteryCharge`, `batteryCapacity` (`src/Game/Game.Simulation/IElectricityStatisticsSystem.cs:3-14`); `IWaterStatisticsSystem` is `freshCapacity`, `freshConsumption`, `fulfilledFreshConsumption`, `sewageCapacity`, `sewageConsumption`, `fulfilledSewageConsumption` (`IWaterStatisticsSystem.cs:3-14`).

**`production` is potential, not output.** `CountElectricityProductionJob` sums `ElectricityProducer.m_Capacity`, not `m_LastProduction` (`ElectricityStatisticsSystem.cs:21-43`, the sum at `:34`). Read live at 1.6.0f1: `prod=3408009` against `cons=273414` and `fulfilled=273414`. A reference that lets a reader read that ratio as "the city uses 8% of what it makes" has misled them; it is 8% of what it *could* make.

`batteryCharge` sums `Battery.storedEnergyHours` and `batteryCapacity` sums `Battery.m_Capacity` (`:72-96`) — consistent only because `capacityTicks = 85 * m_Capacity` and `storedEnergyHours = m_StoredEnergy / 85` are inverses.

`freshCapacity` sums `WaterPumpingStation.m_Capacity` (`WaterStatisticsSystem.cs:21-42`), `sewageCapacity` sums `SewageOutlet.m_Capacity` (`:44-65`), and both consumption figures come from the same `WaterConsumer.m_WantedConsumption` (`:67-90`). Live: `freshCap=1110000 freshCons=137556 sewCap=300000 sewCons=137556`.

The UI reads them through two binding groups. `ElectricityInfoviewUISystem` publishes `electricityInfo` with nine bindings (`src/Game/Game.UI.InGame/ElectricityInfoviewUISystem.cs:14`, `:59-67`), `WaterInfoviewUISystem` publishes `waterInfo` with ten (`src/Game/Game.UI.InGame/WaterInfoviewUISystem.cs:14`, `:61-70`). Both groups are consumed in the shipped bundle at `DecompiledCitiesSkylines2/src-ui/source.js:34690-34708`. Three of the electricity bindings are derived rather than raw: `electricityAvailability = IndicatorValue.Calculate(production, consumption)`, `electricityTransmission = IndicatorValue(0, consumption, fulfilledConsumption)`, `electricityTrade` normalises net trade revenue against `consumption * m_ElectricityExportPrice` clamped to ±1 (`ElectricityInfoviewUISystem.cs:83-109`).

The per-building selected-info panel is thinner than the infoview: `ElectricitySection` publishes only `capacity` and `production` off `ElectricityProducer`, visible only where the entity has one, with tooltip keys added per power source (`src/Game/Game.UI.InGame/ElectricitySection.cs:24-64`). `WaterSection` and `SewageSection` sit beside it.

Reading a binding from a running game needs no click and is the `coherent-gameface` route in `docs/SOURCES.md` entry 9.

*Rots:* the binding group and key names, and the two interfaces' member lists.

### The seams a mod can reach, and the one that re-applies on load

**Public static graph helpers.** `ElectricityGraphUtils` (`src/Game/Game.Simulation/ElectricityGraphUtils.cs`) is a complete public API for the shadow graph: `HasAnyFlowEdge`, three `TryGetFlowEdge` overloads, `TrySetFlowEdge`, three `CreateFlowEdge` overloads (`EntityCommandBuffer`, `EntityCommandBuffer.ParallelWriter`, `EntityManager`), two `DeleteFlowNode` overloads, and `DeleteBuildingNodes`. `WaterPipeGraphUtils` is the exact mirror with `(freshCapacity, sewageCapacity)` where the electricity one takes `(direction, capacity)` (`src/Game/Game.Simulation/WaterPipeGraphUtils.cs:8-147`). **`CreateFlowEdge` appends to both endpoints' `ConnectedFlowEdge` buffers as well as writing the edge** (`ElectricityGraphUtils.cs:80-81`) — hand-rolling an edge without that leaves the graph half-linked, which is exactly what one corpus mod exists to detect.

**Three public switches, and none is a mod's private one.** `WaterPipeFlowSystem.fluidFlowEnabled { get; set; } = true` (`src/Game/Game.Simulation/WaterPipeFlowSystem.cs:372`) and `DispatchWaterSystem.freshConsumptionDisabled` / `sewageConsumptionDisabled` (`src/Game/Game.Simulation/DispatchWaterSystem.cs:274-276`). Setting the dispatch pair makes every building's fulfilment equal its demand and masks the shortage flags out of the connectivity test (`DispatchWaterSystem.cs:123-132`); turning `fluidFlowEnabled` off skips the second solver pass entirely. (Corrected 2026-08-11 by the orchestrator against the decompile; this file first recorded them as "set by nothing in the game" and "an unlimited-water switch with no UI".) All three have first-party writers: the developer menu ships them as `DebugUI.BoolField`s — "Water Pipe Fluid Flow", "Disable Water consumption", "Disable Sewage generation" (`src/Game/Game.Debug/DebugSystem.cs:2127-2153`) — and `WaterPipeFlowSystem.PostDeserialize` resets the dispatch pair on every load, setting them `true` for a pre-`waterPipeFlowSim` save and `false` otherwise (`WaterPipeFlowSystem.cs:594-605`), so a mod's write holds only until the next load.

**The deserialize-time re-apply, and it is electricity-only.** `Game.Serialization.ElectricityGraphSystem` runs in `SystemUpdatePhase.Deserialize` (`SystemOrder.cs:841`) and rewrites every net edge's flow edges from the prefab's current `ElectricityConnectionData` — direction and capacity — reversing an edge's endpoints and negating its flow if it finds it stored the other way round (`src/Game/Game.Serialization/ElectricityGraphSystem.cs:99-120`), and warning `"ElectricityFlowEdge for net edge {index} not found!"` when it cannot (`:73-76`). Where the prefab has no `ElectricityConnectionData` it falls back to `m_Capacity = 400000, m_Direction = Both` (`:70-72`) — **a C# literal that ships as a number**, and notably not the 500,000 vanilla roads carry.

**There is no water equivalent.** `Game.Serialization/` holds `ElectricityGraphSystem.cs` and nothing else for utilities. So a mod that changes `ElectricityConnectionData.m_Capacity` sees the change on every existing edge after a reload; the same change to `WaterPipeConnectionData` reaches only edges created afterwards.

### The save format has been reworked seventeen times in this area

`src/Game/Game/Version.cs` carries, in this topic alone: `stormWater` (`:39`), `electricityTrading` (`:57`), `electricityFeeEffect` (`:180`), `electricityFlashFix` (`:207`), `batteryRework` (`:273`), `timoSerializationFlow` (`:276`), `electricityImprovements` (`:423`), `electricityImprovements2` (`:432`), `batteryRework2` (`:468`), `electricityStats` (`:483`), `batteryStats` (`:486`), `batteryLastFlow` (`:489`), `waterPipeFlowSim` (`:531`), `waterPipePollution` (`:534`), `flowJobImprovements` (`:543`), `waterPipeFlags` (`:570`), `groundWaterPollutionFix` (`:669`).

The migrations are readable in the components themselves. `ElectricityFlowEdge.Deserialize` reads the flags byte only above `electricityImprovements2`, reads and discards an int between `electricityImprovements` and that, and otherwise synthesises `ForwardBackward` (`src/Game/Game.Simulation/ElectricityFlowEdge.cs:64-77`). `WaterPipeEdge.Deserialize` reads and discards two ints written between `stormWater` and `waterPipeFlowSim` (`src/Game/Game.Simulation/WaterPipeEdge.cs:74-78`) and reads the flags byte only from `waterPipeFlags` (`:79-83`). `ElectricityFlowSystem.Deserialize` (`src/Game/Game.Simulation/ElectricityFlowSystem.cs:590-634`) has five separate version bands, one of which reads a whole legacy entity list and a `NativeList<int>` and throws them away (`:609-618`), and `PostDeserialize` (`:643-706`) migrates the two legacy outside-connection nodes into per-connection `TradeNode`s.

**This is the least stable subsystem in the game by save-format churn**, and a reference that sends a reader to fork one of these systems owes them that.

*Rots:* every `Version.cs` entry name and line, and each `Deserialize` band. Re-check at `src/Game/Game/Version.cs` and the three components' `Deserialize` methods.

### Stormwater is declared everywhere and solved nowhere

`UtilityTypes.StormwaterPipe = 4` (`src/Game/Game.Net/UtilityTypes.cs:11`). `Layer.StormwaterPipe = 0x20` (`src/Game/Game.Net/Layer.cs:13`). `Game.Net.WaterPipeConnection.m_StormCapacity` (`src/Game/Game.Net/WaterPipeConnection.cs:12`), defaulting to **5000** for saves older than `Version.stormWater` (`:37`). `WaterPipeConnectionData.m_StormCapacity` (`src/Game/Game.Prefabs/WaterPipeConnectionData.cs:12`). `NetInitializeSystem` sets `Layer.StormwaterPipe` when the capacity is non-zero (`src/Game/Game.Prefabs/NetInitializeSystem.cs:2487-2489`) and `NetGeometryData.m_IntersectLayers`/`m_MergeLayers` include it (`:2213-2214`). `ConnectionWarningSystem` tests it alongside the other four (`src/Game/Game.Net/ConnectionWarningSystem.cs:1755`).

**And nothing solves it.** `WaterPipeFlowSystem` schedules exactly two layers (`src/Game/Game.Simulation/WaterPipeFlowSystem.cs:489-490`). `WaterPipeEdge` has no storm field. `UtilityLane.GetArchetypeComponents` explicitly *excludes* stormwater from the set that gets `EdgeMapping` and `SubFlow` — `(m_UtilityType & ~(StormwaterPipe | Fence | Catenary)) != None` (`src/Game/Game.Prefabs/UtilityLane.cs:53-57`), and `RequiredBatchesSystem` repeats the same exclusion (`src/Game/Game.Rendering/RequiredBatchesSystem.cs:355`).

Read together with `src/Game/Game.Simulation/WaterPipeEdge.cs:74-78` — two ints written between `stormWater` and `waterPipeFlowSim` and discarded since — the conclusion is that stormwater was a simulated third layer that was removed when the flow simulation was rewritten, leaving its data surface behind. **A modder who finds `StormwaterPipe` and assumes it works is the failure this is worth a trap for.**

The census settled the data half: all sixty `WaterPipeConnectionData` carriers in this install read `m_StormCapacity = 0` (the orchestrator's 2026-08-11 per-entity read, installed DLC included). One install's DLC set, as ever; the query on the component is the check for any other.

### The naming traps in this area

Five:

- **`Game.Net.LaneFlow` and `Game.Net.SecondaryFlow` are traffic, not utilities.** Both are `float4 m_Duration, float4 m_Distance, float2 m_Next` (`src/Game/Game.Net/LaneFlow.cs:51-57`, `src/Game/Game.Net/SecondaryFlow.cs:17-23`) and belong to `TrafficFlowSystem`.
- **`Game.Net.SubFlow` is rendering.** A `sbyte` buffer with `InternalBufferCapacity(16)` (`src/Game/Game.Net/SubFlow.cs:6-10`) read only by `NetColorSystem`, `BatchDataSystem` and `ManagedBatchSystem` — the visible flow animation on a utility lane, not the solved flow.
- **`Game.Net.FlowResource` has two members, `None = -1` and `WaterPipes = 1`** (`src/Game/Game.Net/FlowResource.cs:3-7`), and is not what the flow solver uses.
- **`Game.Simulation.Flow.Node` is a union.** `[StructLayout(LayoutKind.Explicit)]` puts `m_CutElementId`/`m_Retreat` and `m_Distance`/`m_Predecessor`/`m_Enqueued` at the same offsets 20/24/28 (`src/Game/Game.Simulation.Flow/Node.cs:5-37`), because `MaxFlowSolver` and `FluidFlowSolver` reuse the same array for different algorithms. Reading `m_Distance` after a max-flow pass returns a cut-element index.
- **The two flow jobs' label constants collide in value and differ in meaning** (`src/Game/Game.Simulation/ElectricityFlowJob.cs:48-60`, `WaterPipeFlowJob.cs:77-83`) — see the flags finding.

*Rots:* every type name in this finding. Re-check at the cited declaration files.

### Catalog gap: `CS2-NetworkTools` verifies the electricity flow graph, and its entry does not say so

The catalog entry's **Demonstrates** (`plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md:87-95`) already carried a one-line watchdog sentence, but it described the mod as re-running "the game's own deserialize-time graph verification" — the mod in fact ships its own 206-line mirror of that check — and said nothing about the shared read-only verifier or why the watchdog debounces. The orchestrator replaced that line rather than appending a near-duplicate; the replacement was revised again at the review gate, and the catalog entry is the record.

`CS2-NetworkTools/NetworkTools.Mod/Systems/ElectricityGraphVerifier.cs` is a 206-line static verifier that mirrors both `Game.Serialization.ElectricityGraphSystem` and `Game.Simulation.ElectricityEdgeGraphSystem`: for each net edge it resolves `ElectricityNodeConnection` on the edge and on both `Game.Net.Edge` endpoints, requires a flow edge between each endpoint's node and the edge's middle node, walks `ConnectedNode` behind the same `PrefabRef` + `ElectricityConnectionData` gate the game uses (`:102-107`), and validates each node's `ConnectedFlowEdge` buffer element by element (`:130-140`). Its five issue kinds are `InvalidNode, MissingBuffer, CorruptBuffer, MissingNodeConnection, MissingFlowEdge` (`:16-22`).

`CS2-NetworkTools/NetworkTools.Mod/Systems/ElectricityWatchdogSystem.cs` runs it live at `GetUpdateInterval => 64` (`:60`) over a query of `Game.Net.ElectricityConnection, ElectricityNodeConnection, Edge, PrefabRef` with `None<Temp, Deleted>` (`:49-52`), and reports only corruption that survives two consecutive scans — its own comment gives the reason, and it is a first-hand observation about the game: "The flow graph is rebuilt asynchronously over several frames after any network edit (`ElectricityEdgeGraphSystem` only acts on freshly `Created` edges via `ModificationBarrier2B`), so a single scan can momentarily see a half-built graph" (`:20-26`).

Both consumers ship commented out of `RegisterSystems` (`NetworkToolsMod.cs:79-80`): the pair is a debug harness to enable, not a live guard.

Its `Temp`/`Deleted` exclusion is a second small teaching — tool previews never get a flow graph, so including them produces only false positives (`:47-48`).

### Catalog gap: `RoadBuilder-CSII` authors both utility connection components

The entry's **Demonstrates** (`mod-catalog.md:211-216`) covers composing a network prefab in code, custom save serialization, marker entities, input mimicry, phase registration and live prefab regeneration. It did not mention that the composition includes the utility layer; the orchestrator appended the sentence below on 2026-08-11.

`RoadBuilder-CSII/RoadBuilder/Utilities/NetworkPrefabGenerationUtil.cs:558-570` instantiates `Game.Prefabs.ElectricityConnection` and sets `m_Voltage = Low`, `m_Direction = Both`, `m_Capacity = 400000`, and `m_RequireAll` to either `{ NetPieceRequirements.Lighting }` or empty depending on the config; `:606-609` instantiates `WaterPipeConnection` bare. `NetworkConfigGenerationUtil.cs:69-82` reads the same three facts back off a vanilla prefab when importing one.

**Sentence added:**
> Authoring a road's utility carriage as part of the prefab: the electricity and water-pipe connection components added in code, with the composition requirement that gates electricity on a lighting upgrade read back off a vanilla road when importing one.

### Catalog sweep: what the rest of the corpus does not have

Swept all 22 repositories for `ElectricityFlow`, `WaterPipeFlow`, `ElectricityConsumer`, `WaterConsumer`, `ElectricityProducer`, `WaterPumpingStation`, `SewageOutlet`, `GroundWater`, `ElectricityBuildingConnection`, `WaterPipeNode`, `ElectricityNodeConnection`, `ConsumptionData`, `ServiceConsumption`, `Layer.PowerlineLow/High`, `Layer.WaterPipe/SewagePipe`, `electricityInfo`, `waterInfo`. Beyond the two entries above, the hits are:

- `Anarchy/Anarchy/Systems/Common/AnarchyUISystem.cs` and `Anarchy/Anarchy/Settings/LocaleEN.cs` — the strings only.
- `BetterBulldozer/BetterBulldozer/Tools/SubElementBulldozerTool.cs:106/121/141` and `BetterBulldozer/BetterBulldozer/Patches/BulldozeToolSystemInitializeRaycastPatch.cs:42` — widening `ToolRaycastSystem.netLayerMask` to include `PowerlineLow | PowerlineHigh | WaterPipe | SewagePipe`, which is a `custom-tools` fact rather than a utilities one.
- `Traffic/Code/Systems/Traffic_LaneSystem.cs:4098` and `:4509` — testing `NetData.m_RequiredLayers` against the four utility layers to *skip* those nets while generating lanes.
- `PlopTheGrowables/Code/Systems/ExistingBuildingSystem.cs` — one incidental reference.
- `InfoLoom/InfoLoom/Systems/Sections/ILCitizenSection.cs:130` and `Time2Work/NightShift/Systems/Time2WorkLeisureSystem.cs:207` — `ConsumptionData` read for household spending, not for utilities.
- `Water_Features` — a water-source tool. It touches surface water bodies, which are the *input* to a pumping station, and never the pipe graph. This is the "water tool that sounds close" the ticket warned about, and it is recorded here rather than read for the flow solver.

**No corpus mod solves, forks, patches or disables any of the simulation systems in this topic** — the sixteen listed in the cadence table above and the two flow systems, plus the twelve graph builders (an electricity and a water system at each of the six modification phases). Nobody replaces `ElectricityFlowSystem`, `WaterPipeFlowSystem`, either dispatch system, either adjust system or `PowerPlantAISystem`.

### SOURCES gap: none found, and one entry earned its keep

`docs/SOURCES.md` named every source this pass used, and no entry was found wrong or stale.

Two entries were load-bearing and are worth recording as confirmed rather than merely used. Entry 8's note that "this game's prefab entities carry `PrefabData` rather than Unity's own prefab tag, so `ecs_query` on a data component returns them directly, labelled through `PrefabSystem.GetPrefabName`" is what made the road-carriage finding cheap — 123 and 60 prefabs enumerated in two calls. Entry 8's note that "no established route builds an `EntityQuery` inside `eval`" is also confirmed: `em.CreateEntityQuery(typeof(T))` fails overload matching against all three signatures, exactly as described, so counts came from `ecs_query` per component instead.

Entry 3's line-count check on the reformatted bundle passed (135,021).

---

## Bridge

### Techniques a change here needs

**`ecs-in-this-game`** — the whole topic is written in the idioms that reference owns. `GetSingleton<T>()` is the read for all five parameter components (`AdjustElectricityConsumptionSystem.cs:439-440`, `PowerPlantAISystem.cs:490`, `DispatchWaterSystem.cs:317-318`). Every job here is `IJobChunk` except the flow jobs and the edge-aggregation jobs, which are plain `IJob` because they walk a graph (`ElectricityFlowJob.cs:16`, `AdjustElectricityConsumptionSystem.cs:203`). The shared-component update-frame filter (`chunk.GetSharedComponent(m_UpdateFrameType).m_Index != m_UpdateFrameIndex` → early return, `:108-111`) is the chunk-level early exit that reference names as what a per-entity job cannot do. Archetype-derived component presence — `ConsumptionData.AddArchetypeComponents` (`src/Game/Game.Prefabs/ConsumptionData.cs:21-39`) — is why a query on `ElectricityConsumer` is a query on "buildings whose prefab declared non-zero electricity consumption".

**`prefabs-and-assets`** — adding a utility to a net means adding `Game.Prefabs.ElectricityConnection` or `Game.Prefabs.WaterPipeConnection` to a `NetPrefab`, whose `GetPrefabComponents`/`GetArchetypeComponents` split decides whether the data lands on the prefab entity or the instance archetype (`src/Game/Game.Prefabs/ElectricityConnection.cs:30-37`, `src/Game/Game.Prefabs/WaterPipeConnection.cs:17-28`). `LateInitialize` copying a prefab class's fields into a singleton component is the shape both parameter prefabs use (`src/Game/Game.Prefabs/ElectricityParametersPrefab.cs:67-88`). And the initializer trap belongs to both references.

**`performance-and-memory`** — this topic is the game's own worked example of amortising an expensive global computation. The solver is spread over 124 frames under a self-tuning step budget derived from the previous solve's cost (`ElectricityFlowJob.cs:112`), its state is `Allocator.Persistent` lists held across frames and disposed in `OnDestroy` (`ElectricityFlowSystem.cs:414-425`, `:429-445`), its per-frame scratch is `Allocator.Temp` inside the job and a `NativeQueue<int>(Allocator.TempJob)` disposed on the job handle (`:549`, `:555`), and consumers are bucketed sixteen ways so no frame touches every building. `MaxFlowPhase` allocates a `NativeArray<UnsafeList<int>>` of `m_LayerHeight` (20) temp lists per call and disposes each (`ElectricityFlowJob.cs:181-224`).

**`save-serialization`** — seventeen `Version` entries, five version bands in one `Deserialize`, an `IPostDeserialize` that migrates legacy graph topology, and two components with hand-written `Serialize`/`Deserialize` that read-and-discard removed fields. If any reference needs a worked example of a long-lived migration chain, this is it.

**`mod-lifecycle-and-ordering`** — the twelve graph-building systems (an electricity and a water twin per phase) are spread across `Modification1`, `2B`, `3`, `4B`, `5` and `ModificationEnd`, and the ordering is load-bearing: deletes first, edges before outside connections before buildings before road connections before reference fixups (`SystemOrder.cs:108-109/133-134/141-142/185-186/246-247/290-291`). The `ready` flag is a cross-system gate a mod must respect.

**`units-and-formatting`** — `85` ticks per hour and `2048` ticks per day are the conversions between the solver's per-tick flow and every human-readable figure; `TimeSystem.kTicksPerDay = 262144` is the root. `Battery.storedEnergyHours` and `BatteryData.capacityTicks` are the two conversions written as properties.

**`diagnostics`** — the six `Debug.LogError` sites that fire when the graph is malformed (`PowerPlantAISystem.cs:156`, `BatteryAISystem.cs:91`, `WaterPumpingStationAISystem.cs:104`, `SewageOutletAISystem.cs:88`, `DispatchElectricitySystem.cs:85`, `DispatchWaterSystem.cs:93`), the two phase-named solver errors (`ElectricityFlowJob.cs:106`, `WaterPipeFlowJob.cs:133`), `Game.Serialization.ElectricityGraphSystem`'s load-time warnings (`src/Game/Game.Serialization/ElectricityGraphSystem.cs:75/93/258`), `WaterPipeFlowSystem.PostDeserialize`'s legacy-pipe and null-node warnings (`WaterPipeFlowSystem.cs:597/619`), and a runtime warning each in `GroundWaterSystem` (`:200`) and `WaterPipeGraphDeleteSystem` (`:49`) are the whole diagnostic surface. (Corrected 2026-08-11 during the review gate, twice: the roster grew from five sites to this census — each earlier sweep matched only the shape it already held; the re-check is a grep for `LogError|LogWarning|WarnFormat` over the topic's systems and jobs, not a recount.) The corpus watchdog above exists because the load-time warning arrives too late.

**`navigating-the-decompile`** — `Game.Simulation.Flow` is the clearest case in the game of an algorithm namespace kept free of domain vocabulary, and finding it from the word "electricity" requires knowing that. The five naming traps above are the other half.

**`patching`** — nothing here needs Harmony. Every seam is a public archetype property, a public static helper, a public settable property or an ordinary component write.

### Adjacent mechanics topics

**`roads-and-traffic`** owns the substrate. The flow graph is built on `Game.Net.Edge`, `Game.Net.Node`, `ConnectedEdge` and `ConnectedNode`; `NetCompositionData` and `NetPieceRequirements` decide utility carriage; a road *upgrade* (lighting) flips a highway's electricity on. The boundary: which nets exist and how they connect is theirs; what flows along them is this topic's.

**`city-services-and-coverage`** owns everything that reaches citizens by range or by vehicle, and shares the building-side vocabulary with this topic: `ConsumptionData` carries `m_GarbageAccumulation` and `m_TelecomNeed` beside the two utilities, `Efficiency`/`EfficiencyFactor` carries six utility factors among thirty-two (`src/Game/Game.Buildings/EfficiencyFactor.cs:13-18`), `ServiceUsage`, `ServiceFee` and the budget are shared, and `ConnectionWarningSystem` emits both families' icons. The line the ticket draws — a service that reaches by range and vehicle is theirs, a service that solves a flow graph is this topic's — holds cleanly in the code: garbage is dispatched by `GarbageCollectorDispatchSystem` and pathfinding, and never touches `Game.Simulation.Flow`.

**`environment-and-pollution`** owns the cell maps and the water bodies this topic reads and writes. `GroundPollution` feeds `GroundWaterPollutionSystem`; `WaterSourceData`, `SurfaceWater` and `WaterUtils.SamplePolluted` are read by the pumping station and written by the sewage outlet; `PollutionEmitModifier` is toggled by the emergency generator. Water pollution *inside the pipes* is this topic's (`WaterPipePollutionSystem`); water pollution in the world is theirs.

**`economy-and-companies`** owns `ServiceFeeSystem` and the fee/price parameters. Both trade systems queue `ServiceFeeSystem.FeeEvent` with `m_Outside = true`, and both adjust systems read `ServiceFeeSystem.GetFee` and divide by the prefab default to get the relative fee the curves are indexed by.

**`zoning-buildings-and-land-value`** owns `SpawnableBuildingData.m_Level` and the `Renter` buffer, both of which the renter consumption multiplier reads; a building's level directly divides its utility consumption.

**`simulation-time-and-units`** owns `TimeSystem.kTicksPerDay`, `SimulationSystem.frameIndex` and `SimulationUtils.GetUpdateFrame`, which are the substrate of the whole cadence table above.

**`transportation-and-vehicles`** touches this topic only through the rail/tram/subway track prefabs carrying `ElectricityConnectionData`, and `city-state-and-progression` only through the `Locked` gate on the two asset-menu prefabs that suppresses the two dispatch systems' consumer warnings — the producer-side icons are ungated (see the notifications finding).

---

## Dead ends

- **The mod corpus has no example of solving, forking or replacing any utility system.** Twenty-two repositories, swept on every component and system name in this topic. What exists is one read-only verifier, one prefab author, two raycast-mask wideners and two incidental reads. The ticket predicted the flow solver would be untouched and it is; the two catalog gaps above are what the sweep did turn up.
- **`Game.Buildings.WaterTower` and `Game.Buildings.WastewaterTreatmentPlant` are simulated by nothing.** A grep across `src/` for each name returns only rendering, notification, initialize and tool-feedback sites. `m_StoredWater` is never written by a simulation system. Do not look for a water-storage model; there is none at 1.6.0f1.
- **Stormwater has no solver.** Searched `WaterPipeFlowSystem`, `WaterPipeFlowJob`, `WaterPipeEdge`, `Game.Simulation.Flow` and every `StormwaterPipe` reference across `src/`. The full sweep is in the finding above.
- **There is no deserialize-time water-pipe capacity fixer** — but the name-filtered `ls` this dead end first rested on was too narrow: `Game.Serialization/ConnectedFlowEdgeSystem.cs` (no utility word in its name) runs in the same deserialize band and rebuilds the `ConnectedFlowEdge` adjacency for both `ElectricityFlowEdge` and `WaterPipeEdge` (`SystemOrder.cs:840`), touching no capacities. (Corrected 2026-08-11 by the orchestrator; the conclusion narrows to capacities and survives.)
- **`em.CreateEntityQuery(typeof(T))` does not work in `eval`.** All three overloads reject a `RuntimeType` argument, exactly as `docs/SOURCES.md` entry 8 records. Every count in this file came from `ecs_query` instead. Do not spend a call re-testing it.
- **The wiki `Services` page's electricity and water sections are thin and one of their claims is stale.** Fetched live 2026-08-11; the page carries "At least some were last verified for version 1.0" and a "to be split" tag. Its only quantitative claim in this area is that a service fee "Every 1% below 100% increases Electricity Consumption by +0.2%" and "every 1% above 100% decreases Electricity Consumption by -0.4%". Read live, `ServiceFeeParameterData.m_ElectricityFeeConsumptionMultiplier` evaluates to 1.3 at a relative fee of 0, 1.206 at 0.5, 1.0 at 1, 0.719 at 1.5 and 0.4 at 2 — a curve, not a linear rate, and the endpoints work out to roughly 0.3%/1% below and 0.6%/1% above. The water curve is identical. **Verdict: the page's rates do not hold at 1.6.0f1, and it is a curve rather than a rate either way**; the mechanism (an `AnimationCurve1` indexed by `fee / m_Default`) ships and the numbers do not. Its qualitative statements — transformers, outside connections both ways, sewage outlets versus treatment plants, surface versus ground water — all check out against the code and are useful as an orientation lead and as nothing else.
- **The wiki has no page on the flow solver, the graph, bottlenecks or transmission capacity.** The `Services` page mentions transformers in one sentence and never mentions a network capacity limit. The flow solver is decompile-only, as the ticket said.
- **`FlowUtils.ConsumeFromTotal` is not the fulfilment rule.** Three call sites, all UI. Recorded above rather than here because a reader will find it and it needs a positive correction, not a silence.
- **No `.cok` or `resources.assets` decode was attempted.** Every prefab value in this file was read from the running game, which `docs/SOURCES.md` entry 5 names as the shorter road for base-game prefabs — and the live census of all sixty `WaterPipeConnectionData` carriers covered the installed DLC too, so the packaged-content route would only add prefabs from packs this install does not own.
