# Vanilla data providers and their handle protocol

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

The systems a mod's job is most likely to take data from, most of them following the reader/writer handle protocol in [`performance-and-memory`](performance-and-memory.md) — the tool systems' work lists are the second shape, a single getter with no register-back, whose discipline that reference states.
Take the data with the right `readOnly` flag, combine the returned handle into your schedule, and register the **scheduled** handle back with every provider you took from.

The protocol is used throughout the game assembly; the ones below are what a mod actually meets.

## The six search systems

Each owns one or two `NativeQuadTree` instances.
**The type arguments vary by system**, so match the pair in the table below rather than assuming `<Entity, QuadTreeBoundsXZ>` everywhere.

| Namespace's `SearchSystem` | Trees | Type arguments | Accessors |
| --- | --- | --- | --- |
| `Game.Objects` | static, moving | `<Entity, QuadTreeBoundsXZ>` | `GetStaticSearchTree`, `GetMovingSearchTree` |
| `Game.Net` | net, lane | `<Entity, QuadTreeBoundsXZ>` | `GetNetSearchTree`, `GetLaneSearchTree` |
| `Game.Zones` | one | `<Entity, Bounds2>` | `GetSearchTree` |
| `Game.Areas` | one | `<AreaSearchItem, QuadTreeBoundsXZ>` | `GetSearchTree` |
| `Game.Routes` | one | `<RouteSearchItem, QuadTreeBoundsXZ>` | `GetSearchTree` |
| `Game.Effects` | one | `<SourceInfo, QuadTreeBoundsXZ>` | `GetSearchTree` |

Each accessor has a matching `Add…Reader` and `Add…Writer` named after the same tree.
Source: `src/Game/Game.Objects/SearchSystem.cs`, `src/Game/Game.Net/SearchSystem.cs`, `src/Game/Game.Zones/SearchSystem.cs`, `src/Game/Game.Areas/SearchSystem.cs`, `src/Game/Game.Routes/SearchSystem.cs`, `src/Game/Game.Effects/SearchSystem.cs`.

**The net tree covers nodes as well as edges, and the lane tree only the lanes that carry geometry or parking data** — the net query is `Any = {Edge, Node}`, so an intersection is in the tree beside the segments meeting at it, while a lane with neither component is absent — which is why [`roads-and-traffic`](../../mechanics/roads-and-traffic/roads-and-traffic.md) is the heaviest area to query.
Source: `src/Game/Game.Net/SearchSystem.cs`.

**They assign in their writer method rather than combining**, the areas system alone excepted.
Source: `src/Game/Game.Objects/SearchSystem.cs`, `src/Game/Game.Net/SearchSystem.cs`, `src/Game/Game.Zones/SearchSystem.cs`, `src/Game/Game.Routes/SearchSystem.cs`, `src/Game/Game.Effects/SearchSystem.cs` (the assignment), `src/Game/Game.Areas/SearchSystem.cs` (the exception).

A seventh queryable tree sits outside the `SearchSystem` naming: the buildings namespace's local-effect system owns an `<EffectItem, EffectBounds>` tree behind the same three-method protocol.

## Terrain and water surfaces

- `TerrainSystem.AddCPUHeightReader`, `AddCPUDownsampleHeightReader`.
- `WaterSystem.AddSurfaceReader`, `AddVelocitySurfaceReader`, `AddMaxHeightSurfaceReader`, `AddActiveReader`.
- `CellMapSystem.AddReader`, for the cell maps [`utilities-and-flow-networks`](../../mechanics/utilities-and-flow-networks/utilities-and-flow-networks.md) and [`environment-and-pollution`](../../mechanics/environment-and-pollution/environment-and-pollution.md) read.

## The demand and counting singletons

`CommercialDemandSystem.AddReader`, `IndustrialDemandSystem.AddReader`, `ResidentialDemandSystem.AddReader`, `CountHouseholdDataSystem.AddHouseholdDataReader`, `CountCompanyDataSystem.AddReader`, `CountVehicleDataSystem.AddVehicleDataReader`, `TaxSystem.AddReader`, `ResourceSystem.AddPrefabsReader`, `ZoneSystem.AddPrefabsReader`.

[`economy-and-companies`](../../mechanics/economy-and-companies/economy-and-companies.md), [`zoning-buildings-and-land-value`](../../mechanics/zoning-buildings-and-land-value/zoning-buildings-and-land-value.md), [`citizens-and-households`](../../mechanics/citizens-and-households/citizens-and-households.md) and [`city-state-and-progression`](../../mechanics/city-state-and-progression/city-state-and-progression.md) are the mechanics topics that need these.

## The rest

- **Dirty bounds.** Each of the objects, net, zones and areas namespaces has an `UpdateCollectSystem` publishing what changed this frame: one `Add*BoundsReader` on objects and on zones, **two** on net (`AddNetBoundsReader`, `AddLaneBoundsReader`), four on areas. Take both of net's if your job holds both.
- **Pathfinding.** `PathfindQueueSystem.AddDataReader`.
  It caps how many worker jobs it keeps in flight at half the job-worker count — a high-priority backlog raises that cap — and backs each with a bump allocator taken from a pool on demand, returned on completion and rewound every update.
  **Those workers come out of the same shared job pool as everything else**, so a mod scheduling a parallel job competes with them rather than against a reserved set of threads.
  Source: `src/Game/Game.Pathfind/PathfindQueueSystem.cs`.
- **Budget and fees.** `CityServiceBudgetSystem.GetIncomeArray`/`GetExpenseArray` + `AddArrayReader`, and `ServiceFeeSystem.GetFeeQueue` + `AddQueueWriter` — the queue is handed out to be written into, so the registration is a writer's.
- **City state.** The same handed-out-queue shape twice more, `XPSystem.GetQueue` + `AddQueueWriter` and `CityStatisticsSystem.GetStatisticsEventQueue` + `AddWriter`, and one without a handle: `IconCommandSystem.CreateCommandBuffer` returns a fresh buffer and no dependency, registered back with `AddCommandBufferWriter`, and the buffers are cleared every update, so obtain, fill and register within one frame; [`city-state-and-progression`](../../mechanics/city-state-and-progression/city-state-and-progression.md) owns what each carries.
- **Rendering.** `PreCullingSystem.AddCullingDataReader`, and three on `BatchManagerSystem`.

## The producer side, when a mod owns the data

The vanilla commercial-demand system is the tidiest example of publishing several containers behind one write handle:

- Seven `Allocator.Persistent` containers allocated in `OnCreate`, disposed in `OnDestroy`.
- Four `Get*(out JobHandle deps)` accessors, all handing back the same write handle.
- One `AddReader` that **combines**.
- An `OnUpdate` that schedules against its own dependency combined with the read handle and every provider handle it took, stores the result as the write handle, and then registers itself as a reader with three other systems.

Copy that last step: a producer is also a consumer, and it owes the registration exactly as any other reader does.
Copy the shape rather than the list, because the vanilla list is wrong: that system registers with a counting system it took nothing from, and skips the one whose handle it actually combined — the discipline is register with every provider you read, and the anchor is a demonstration of the mechanism, not of the bookkeeping.
Source: `src/Game/Game.Simulation/CommercialDemandSystem.cs` (the shape, and the mismatch), `src/Game/Game.Simulation/CountCompanyDataSystem.cs` (the provider it reads without registering with).

(VOLATILE: every accessor and registration name on this page — the `SearchSystem` and `UpdateCollectSystem` types in the objects, net, zones, areas, routes and effects namespaces, the simulation namespace's terrain, water, cell-map, demand, counting, budget, service-fee, XP and city-statistics systems, the notifications namespace's icon command system, the prefabs namespace's resource and zone systems, the pathfind namespace's queue system, and the rendering namespace's culling and batch-manager systems.)
