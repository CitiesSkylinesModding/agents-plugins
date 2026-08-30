# The outside connection as an object

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

The outside connection is one placed object playing several roles at once, and its "capacity" is not one number: each role carries its own.

## What the object is

The authoring class `OutsideConnection : ComponentBase` declares `m_TradedResources`, `m_Commuting`, `m_TransferType` and `m_Remoteness`, writes only the last two into `OutsideConnectionData { m_Type, m_Remoteness }`, folds the traded resources into `StorageCompanyData.m_StoredResources`, and sets `TransportCompanyData.m_MaxTransports = int.MaxValue` (`src/Game/Game.Prefabs/OutsideConnection.cs`, `OutsideConnectionData.cs`).
Its archetype is the answer to "what enters through it": the `Game.Objects.OutsideConnection` tag, `Economy.Resources`, `Game.Companies.StorageCompany`, `TradeCost`, `StorageTransferRequest`, `TripNeeded`, `ResourceSeller`, `TransportCompany`, `OwnedVehicle`, `GoodsDeliveryFacility`.
So its trade capacity is literally unbounded delivery vehicles — the transport-count cap city companies are held to never fires on it — while its resource capacity is finite, through `Game.Companies.StorageLimitData.m_Limit` (`src/Game/Game.Companies/TransportCompanyData.cs`, `StorageLimitData.cs`).
[`economy-and-companies`](../economy-and-companies/economy-and-companies.md) owns the storage and trade machinery those components belong to; what is this topic's is which of them sit on which connection.

**`m_Commuting` is dead at 1.6.0f1.**
A whole-decompile grep returns only its declaration — a serialized authoring field with no consumer — so it gates nothing; commuter spawning lives in `CommuterSpawnSystem` and belongs to [`citizens-and-households`](../citizens-and-households/citizens-and-households.md).
Source: `src/Game/Game.Prefabs/OutsideConnection.cs`.

**`OutsideConnectionTransferType` is a flag enum with a gap: `Ship` skips bit 3.**
The members run `None = 0`, `Road = 1`, `Train = 2`, `Air = 4`, `Ship = 0x10`, `Last = 0x20`, with `All = 0x17` excluding `Last`, so a mask composed arithmetically rather than by name is wrong; `BuildingUtils.GetOutsideConnectionType` reads the type off the prefab, returning `None` on a miss, and `GetRandomOutsideConnectionByTransferType` filters a list by mask.
Source: `src/Game/Game.Prefabs/OutsideConnectionTransferType.cs`, `src/Game/Game.Buildings/BuildingUtils.cs`.

**`m_Remoteness` is geometry, not a cost term.**
`OutsideConnectionSystem` copies it onto each generated connection node and is its only consumer: each connection's immediate-neighbour lane is created unconditionally, remoteness differences decide which *further* off-map connections also gain lanes, and `CalculateCurve` bulges the off-map bezier between two connections by `50f + abs(remoteness2 - remoteness1) * 0.5f` — so remoteness shapes the off-map network and how long a vehicle spends outside the map, and the two literals are C# and ship.
Source: `src/Game/Game.Net/OutsideConnectionSystem.cs`.

## Which roles sit on which connection

A swept shape of one install over its six outside-connection prefabs — two road, two rail, one sea, one air; the sweep is `ecs_query` on `Game.Prefabs.OutsideConnectionData`, labelled through `PrefabSystem.GetPrefabName`, then per-component presence checks across the set, and that query is the re-check:

| Role component | Road | Rail | Sea | Air |
| --- | --- | --- | --- | --- |
| `TransportDepotData` | ✓ (`Taxi`) | absent | ✓ (`Ship`) | ✓ (`Airplane`) |
| `TransportStopData` | ✓ (passenger only) | ✓ (passenger and cargo) | ✓ (passenger and cargo) | ✓ (passenger and cargo) |
| `WorkplaceData` | ✓ | ✓ | absent | absent |
| `SchoolData` | ✓ | ✓ | absent | absent |
| `HospitalData` | ✓ | ✓ | ✓ | ✓ |
| `TrafficSpawnerData` | ✓ | ✓ | ✓ | ✓ |

The shapes in the gaps:

- **A road, sea or air connection is a transport depot; a rail one is not.** Intercity ships and aircraft spawn at the connection itself — this install has no city-side depot prefab for `Ship` or `Airplane` at all, re-checked by `ecs_query` on `Game.Prefabs.TransportDepotData` — while rail rolling stock comes from a city rail yard, so the rail connection needs no depot half; the road connection is a *taxi* depot, which is where `TaxiFlags.FromOutside` cabs come from.
- **`WorkplaceData`, `SchoolData` and `HospitalData` on a connection are the outbound half, not what imports anyone.** They make the connection a job, a study place and treatment *outside* the city for its own citizens — the workplace and school halves excluded from the city's censuses, the healthcare one gated on `CityOption.ImportOutsideServices`, and `WorkProviderSystem` overwriting a connection's `WorkProvider.m_MaxWorkers` with a hardcoded `Workplaces` set so the authored figure never applies; inbound commuter arrival never consults these components at all — it is `CommuterSpawnSystem`'s, [`citizens-and-households`](../citizens-and-households/citizens-and-households.md)' as the `m_Commuting` trap above already routes (`src/Game/Game.Simulation/WorkProviderSystem.cs`, `CountWorkplacesSystem.cs`, `CountStudyPositionsSystem.cs`, `CommuterSpawnSystem.cs`, `HealthcarePathfindSetup.cs`, `src/Game/Game.Buildings/BuildingUtils.cs`).
- **Cargo leaves a road connection by delivery truck rather than by line**, which is why its stop half is passenger-only.
- **Every connection carries `TrafficSpawnerData`** — [`roads-and-traffic`](../roads-and-traffic/roads-and-traffic.md)'s intercity generator — and its road or track type pair is what tells the sea and air connections apart from the road one.

**The city's placed transport connections are the `Game.Objects.OutsideConnection` tag minus the two utility markers; a query on `Game.Prefabs.OutsideConnectionData` returns the handful of prefabs instead.**
`ElectricityOutsideConnection` and `WaterPipeOutsideConnection` both add the very same tag beside their own marker and declare no `OutsideConnectionData` at all — so the instance query is the tag with the two utility markers excluded — the shape `CommuterSpawnSystem` composes, its excludes also dropping `Building`, `Deleted` and `Temp` — and one instance's transfer type reads through its `PrefabRef` (`BuildingUtils.GetOutsideConnectionType`); anything about what the two utility connections carry is [`utilities-and-flow-networks`](../utilities-and-flow-networks/utilities-and-flow-networks.md)', and the discriminator is this topic's to state.
Source: `src/Game/Game.Prefabs/ElectricityOutsideConnection.cs`, `src/Game/Game.Prefabs/WaterPipeOutsideConnection.cs`, `src/Game/Game.Objects/OutsideConnection.cs`, `src/Game/Game.Simulation/CommuterSpawnSystem.cs`, `src/Game/Game.Buildings/BuildingUtils.cs`.

(VOLATILE: every component, field, enum, system, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Prefabs`, `Game.Objects`, `Game.Companies`, `Game.Buildings`, `Game.Net`, `Game.Simulation` and `Game.Vehicles`, at the files cited beside each claim; plus the six-connection role table, against the running game's prefab set, re-derived by the `ecs_query` stated above it.)
