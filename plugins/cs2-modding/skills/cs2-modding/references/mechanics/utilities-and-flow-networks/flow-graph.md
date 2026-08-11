# The flow graph

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

The solver's node and edge arrays are rebuilt every cycle; the persistent graph is ECS entities.
Both flow systems expose their archetypes and endpoints as public properties — `nodeArchetype`, `edgeArchetype`, `sourceNode`, `sinkNode`, and on electricity also `chargeNodeArchetype` and `dischargeNodeArchetype` — which is what makes the graph extendable from a mod (`src/Game/Game.Simulation/ElectricityFlowSystem.cs`, `WaterPipeFlowSystem.cs`).
The source and sink are ordinary entities of the plain node archetype, created in `PostDeserialize` when a map has none; nothing tags them, so hold on to the properties rather than querying for them.
The builders below run in the modification phases of the frame an edit lands, spread from deletion at `Modification1` to reference fixups at `ModificationEnd` (`src/Game/Game.Common/SystemOrder.cs`), so a read mid-band sees a half-built graph.

## One net edge, three flow nodes

Sources: `src/Game/Game.Simulation/ElectricityEdgeGraphSystem.cs`, `src/Game/Game.Simulation/WaterPipeEdgeGraphSystem.cs`.

```
ElectricityEdgeGraphSystem (newly Created net edges carrying Game.Net.ElectricityConnection,
                            through ModificationBarrier2B):
  per net edge:
    startNode = get-or-create the flow node of netEdge.m_Start   -- stamps ElectricityNodeConnection
    endNode   = get-or-create the flow node of netEdge.m_End
    middleNode = a new flow node, stamped on the net edge itself as ElectricityNodeConnection
    create flow edges startNode -> middleNode and middleNode -> endNode,
      direction and capacity from the prefab's ElectricityConnectionData
    for each ConnectedNode of the edge whose prefab has ElectricityConnectionData:
      create a flow edge from that node's flow node to middleNode, with that prefab's data
    for each endpoint, walk ConnectedEdge: create a flow edge to every neighbouring net
      edge's middle node that does not already have one
WaterPipeEdgeGraphSystem: the same shape against Game.Net.WaterPipeConnection, stamping
  WaterPipeNodeConnection and passing (m_FreshCapacity, m_SewageCapacity)
  where electricity passes (m_Direction, m_Capacity)
```

So the graph scales with the network, not with the building count.

**`CreateFlowEdge` appends the new edge to both endpoints' `ConnectedFlowEdge` buffers as well as writing the edge.**
A hand-rolled edge that skips either append leaves the graph half-linked, and nothing in the game detects it before the prepare pass misbehaves.
Source: `src/Game/Game.Simulation/ElectricityGraphUtils.cs`, `src/Game/Game.Simulation/WaterPipeGraphUtils.cs`.

## A building joins one of two ways, and only one is per-building

Sources: `src/Game/Game.Simulation/ElectricityBuildingGraphSystem.cs`, `src/Game/Game.Simulation/WaterPipeBuildingGraphSystem.cs`, `src/Game/Game.Simulation/ElectricityRoadConnectionGraphSystem.cs`, `src/Game/Game.Simulation/WaterPipeRoadConnectionGraphSystem.cs`, `src/Game/Game.Simulation/AdjustElectricityConsumptionSystem.cs`, `src/Game/Game.Simulation/AdjustWaterConsumptionSystem.cs`.

```
ElectricityBuildingGraphSystem:
  markerNodes = net nodes in the building's own SubNet buffer and each non-inactive
    InstalledUpgrade's, kept when they carry ElectricityValveConnection or are orphans,
    and their prefab has ElectricityConnectionData
  if markerNodes found, the building is not Destroyed, and it carries at least one of
      Game.Buildings.Transformer | ElectricityProducer | ElectricityConsumer | Battery:
    transformer: m_TransformerNode = a plain node
    producer:    m_ProducerEdge  = source -> new node, Forward, capacity 0
    consumer:    m_ConsumerEdge  = new node -> sink,   Forward, capacity 0
    battery:     m_ChargeEdge    = new charge-archetype node -> sink,      None, 0
                 m_DischargeEdge = source -> new discharge-archetype node, None, 0
    producer & consumer: internal edge producerNode -> consumerNode, Forward,
      kMaxEdgeCapacity (the both-systems unconstrained sentinel)
    producer & battery:  producerNode -> chargeNode,     None, kMaxEdgeCapacity
    consumer & battery:  dischargeNode -> consumerNode,  None, kMaxEdgeCapacity
    each marker net node gets a valve node, stored on it as ElectricityValveConnection,
      between the marker's flow node and the building's: valveNode -> markerFlowNode
      takes the marker prefab's direction and capacity, and every building-to-valve
      edge takes the same capacity -- transformerNode -> valve keeps the prefab
      direction, producerNode -> valve and valve -> consumerNode are Forward,
      valve -> chargeNode and dischargeNode -> valve are None
  else: the building's nodes are deleted
WaterPipeBuildingConnection is the two-field version: producer and consumer edges only,
  no direction (water edges carry two capacities instead), no transformer, no battery;
  its valve nodes ride WaterPipeValveConnection the same way.

A consumer with no building connection rides its road edge in aggregate:
ElectricityRoadConnectionGraphSystem (on RoadConnectionUpdated) and
AdjustElectricityConsumptionSystem.UpdateEdgesJob (on any member's change) maintain
  one flow edge roadEdgeNode -> sink whose capacity is the SUM of m_WantedConsumption
  over the road edge's ConnectedBuilding entries that have ElectricityConsumer
  and no ElectricityBuildingConnection
WaterPipeRoadConnectionGraphSystem and AdjustWaterConsumptionSystem.UpdateEdgesJob are
  the water twins, writing one summed wanted figure to both the fresh and sewage
  capacities of the same aggregate edge
```

`BuildingInitializeSystem` derives which layers a building prefab needs — positive electricity consumption needs `Layer.PowerlineLow`, positive water consumption needs `Layer.WaterPipe | Layer.SewagePipe`, a pump needs water, an outlet sewage, a transformer low voltage — and stamps `BuildingFlags.RequireRoad` unless the prefab's own sub-nets satisfy them, recording what they do satisfy as `HasLowVoltageNode`/`HasWaterNode`/`HasSewageNode` (`src/Game/Game.Prefabs/BuildingInitializeSystem.cs`).
`ConnectionWarningSystem` emits the not-connected icons from the same layer arithmetic, with two special cases: the masked fold below, and a producer that is also a transformer and is connected on low voltage has its high-voltage warning cleared (`src/Game/Game.Net/ConnectionWarningSystem.cs`).
**A road edge's `m_LocalConnectLayers` folds into a building's connected set only masked: the low-voltage and both water layers never arrive by it.**
The mask at the fold site says what does.
Source: `src/Game/Game.Net/ConnectionWarningSystem.cs`, `src/Game/Game.Net/Layer.cs`.

## The carriage gate fires at placement

Sources: `src/Game/Game.Prefabs/NetInitializeSystem.cs`, `src/Game/Game.Tools/GenerateEdgesSystem.cs`, `src/Game/Game.Prefabs/NetCompositionHelpers.cs`.

```
NetInitializeSystem: bakes ElectricityConnection.m_RequireAll/Any/None into
  ElectricityConnectionData.m_CompositionAll/Any/None, erroring if any entry is a
  section flag rather than a piece flag
GenerateEdgesSystem, per placed or upgraded edge whose prefab has ElectricityConnectionData:
  pass = NetCompositionHelpers.TestEdgeFlags(data, upgradedComposition)
       = ((m_CompositionAll | m_CompositionNone) & flags) == m_CompositionAll
         and (m_CompositionAny == default or (m_CompositionAny & flags) != 0)
  pass: add Game.Net.ElectricityConnection to the edge
  fail on an edge that had it: remove it
  verdict differs from the original edge's: the tool's Temp flags flip Upgrade -> Replace
Game.Prefabs.WaterPipeConnection has no gate: it adds Game.Net.WaterPipeConnection
  whenever the archetype has Edge
```

## The load-time re-apply is electricity-only

`Game.Serialization.ElectricityGraphSystem` runs in the `Deserialize` phase and rewrites every net edge's flow edges — direction and capacity — from the prefab's current `ElectricityConnectionData`, reversing an edge stored the other way round and negating its flow, and warning when one is missing (`src/Game/Game.Serialization/ElectricityGraphSystem.cs`).
Where the prefab no longer has `ElectricityConnectionData` it writes the fallback `m_Capacity = 400000, m_Direction = Both` — a C# literal, tied to no prefab's authored capacity.
There is no water capacity re-apply: the one other utility system in `Game.Serialization/`, `ConnectedFlowEdgeSystem`, rebuilds both graphs' `ConnectedFlowEdge` adjacency in the same phase and touches no capacities.
**A mod's change to `ElectricityConnectionData.m_Capacity` reaches every existing edge on the next load; the same change to `WaterPipeConnectionData` reaches only edges created afterwards.**
Source: `src/Game/Game.Serialization/ElectricityGraphSystem.cs`, `src/Game/Game.Serialization/ConnectedFlowEdgeSystem.cs`.

## The seams a mod reaches

`ElectricityGraphUtils` is a complete public static API over the graph: `HasAnyFlowEdge`, `TryGetFlowEdge`, `TrySetFlowEdge`, `CreateFlowEdge` (an overload each for `EntityCommandBuffer`, its `ParallelWriter`, and `EntityManager`), `DeleteFlowNode` and `DeleteBuildingNodes` (`src/Game/Game.Simulation/ElectricityGraphUtils.cs`).
`WaterPipeGraphUtils` is the mirror, taking `(freshCapacity, sewageCapacity)` where the electricity API takes `(direction, capacity)` (`src/Game/Game.Simulation/WaterPipeGraphUtils.cs`).
The topic's settable properties are the developer menu's toggles, not graph seams — the dispatch listing carries the two that reset on every load.
`ElectricityOutsideConnectionGraphSystem` shows the outside-connection shape: `TradeNode` on the flow node plus two `kMaxEdgeCapacity` edges, source to node and node to sink, both created `FlowDirection.None` for the solver to enable; `WaterPipeOutsideConnectionGraphSystem` is the twin, creating both edges at zero capacity — the water solve job supplies the trade limits from its own per-layer import and export figures in the solver's scratch arrays, so the ECS edge's capacities stay zero and only its flows and flags are written back (`src/Game/Game.Simulation/ElectricityOutsideConnectionGraphSystem.cs`, `WaterPipeOutsideConnectionGraphSystem.cs`).

(VOLATILE: every component, field, system, property, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Prefabs`, `Game.Net`, `Game.Buildings`, `Game.Tools`, `Game.Common` and `Game.Serialization`, at the files the sections cite.)
