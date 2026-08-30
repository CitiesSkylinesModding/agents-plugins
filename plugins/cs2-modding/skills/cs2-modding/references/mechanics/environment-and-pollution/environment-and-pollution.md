# Environment and pollution

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

The environment is a stack of square cell maps over the playable world, each owned by a `CellMapSystem<T>` subclass and reached through one shared reader/writer protocol ([cell-maps.md](cell-maps.md)).
Buildings stamp pollution into the ground, air and noise maps, roads into air and noise ([emission.md](emission.md)); each map then evolves by its own rule — air advects along the wind and fades, ground only fades, noise is rebuilt from scratch every update ([map-dynamics.md](map-dynamics.md)).
Ground pollution leaks into the aquifer and scars fertile land, sewage travels the surface water into pump intakes ([water-and-groundwater.md](water-and-groundwater.md)), and natural resources sit on the same grid as four per-cell amounts that extractors deplete in place ([natural-resources.md](natural-resources.md)).
Above the maps, climate is a managed system of overridable properties: the date resolves the season from the climate's season list, and curves generated from those seasons drive weather, wetness and snow — but not the wind, which is authored per map ([climate-and-weather.md](climate-and-weather.md)).
A disaster is an ordinary event entity, spawned by a probability roll against the live weather and advected by the wind ([disasters.md](disasters.md)).

## The map

The five pollution kinds are stored four different ways, so "how polluted is this spot" is four different reads (cell structs in `src/Game/Game.Simulation/`):

| Kind | Storage | Read |
| --- | --- | --- |
| Ground | cell map of `GroundPollution { m_Pollution }` (`GroundPollution.cs`) | `GroundPollutionSystem.GetMap` ([cell-maps.md](cell-maps.md)); saturates at 32767 on `Add` |
| Air | cell map of `AirPollution { m_Pollution }` (`AirPollution.cs`) | `AirPollutionSystem.GetMap`; same saturation |
| Noise | cell map of `NoisePollution { m_Pollution, m_PollutionTemp }` (`NoisePollution.cs`) | `NoisePollutionSystem.GetMap`; writes go through `m_PollutionTemp` ([map-dynamics.md](map-dynamics.md)) |
| Groundwater | a field on the groundwater cell, `GroundWater { m_Amount, m_Polluted, m_Max }` (`GroundWater.cs`) | `GroundWaterSystem.GetMap`; concentration is `m_Polluted / m_Amount`, derived |
| Surface water | the `w` channel of the GPU water texture, surfaced as `SurfaceWater { m_Depth, m_Polluted, m_Velocity }` (`SurfaceWater.cs`) | `WaterSystem.GetSurfaceData` async readback — readable, never writable; steer through source entities ([water-and-groundwater.md](water-and-groundwater.md)) |
| Piped water | `WaterPipeEdge.m_FreshPollution`, a fraction per graph element | belongs to [`utilities-and-flow-networks`](../utilities-and-flow-networks/utilities-and-flow-networks.md); this topic owns the pump-intake write ([water-and-groundwater.md](water-and-groundwater.md)) |

The other layers this topic owns, all cell maps read the same way:

| Layer | Cell | Owner |
| --- | --- | --- |
| Natural resources | `NaturalResourceCell` — fertility, ore, oil, fish, each `{ m_Base, m_Used }` | `NaturalResourceSystem` ([natural-resources.md](natural-resources.md)) |
| Wind | `Wind { m_Wind }`, flattened from a 3D pressure volume | `WindSystem` ([climate-and-weather.md](climate-and-weather.md)) |
| Terrain attractiveness | `TerrainAttractiveness { m_ShoreBonus, m_ForestBonus }` | `TerrainAttractivenessSystem` ([map-dynamics.md](map-dynamics.md)) |
| Soil water | `SoilWater` — never ticked at 1.6.0f1 | `SoilWaterSystem` ([disasters.md](disasters.md)) |

What emits, as data (a prefab component reads through the instance's `PrefabRef` unless noted):

| The game models | Component | Access shape |
| --- | --- | --- |
| A building's emission | `PollutionData { m_GroundPollution, m_AirPollution, m_NoisePollution, m_ScaleWithRenters }` (`src/Game/Game.Prefabs/PollutionData.cs`) | summed over `InstalledUpgrade`; a zoned building's copy is baked from the zone's `ZonePollution` rates times lot size ([emission.md](emission.md)) |
| A service upgrade's emission multiplier | `PollutionModifierData` (`src/Game/Game.Prefabs/PollutionModifierData.cs`) | summed over upgrades, applied as `max(0, 1 + m)` per channel |
| One instance's override | `PollutionEmitModifier` (`src/Game/Game.Buildings/PollutionEmitModifier.cs`) | runtime component on the building itself, serialized — the per-instance hook ([emission.md](emission.md)) |
| A net's emission factors | `NetPollutionData { m_Factors }` plus runtime `Game.Net.Pollution { m_Pollution, m_Accumulation }` on nodes and edges | [emission.md](emission.md) |
| A vehicle's emission rating | `VehicleSideEffectData { m_Min, m_Max }`, channels (wear, noise, air) | a range interpolated by speed, never a number ([emission.md](emission.md)) |
| A water source | `Game.Simulation.WaterSourceData` on the source entity; authoring twin in `Game.Prefabs` | [water-and-groundwater.md](water-and-groundwater.md) |
| A weather disaster | `WeatherPhenomenonData` on the event prefab; `WaterLevelChangeData` for the water ones | [disasters.md](disasters.md) |
| A climate | `ClimatePrefab` (`src/Game/Game.Prefabs.Climate/ClimatePrefab.cs`) | curves generated from its seasons ([climate-and-weather.md](climate-and-weather.md)) |
| An object's snow and wetness | `Game.Objects.Surface`, five bytes | [climate-and-weather.md](climate-and-weather.md) |
| A plant's sickness | `Plant.m_Pollution` | [map-dynamics.md](map-dynamics.md) |
| A layer as an info view | `HeatmapData` (`src/Game/Game.Rendering/HeatmapData.cs`) | the closed enum of renderable layers; `OverlayInfomodeSystem` switches on it |

The topic's tuning numbers live on parameter singletons, each a plain `GetSingleton<T>` read — except the rain-flood counter, which runs on C# literals ([disasters.md](disasters.md)):

| Singleton | Owns |
| --- | --- |
| `PollutionParameterData` (`src/Game/Game.Prefabs/PollutionParameterData.cs`) | every emission multiplier, radius, fade rate, the wind-advection speed, the distance exponent, the notification limits, abandoned and homeless noise, the plant and fertility multipliers, the land-value divisor |
| `WaterPipeParameterData` | groundwater replenish and purification, the surface and groundwater usage multipliers, `m_MaxToleratedPollution` — the component belongs to [`utilities-and-flow-networks`](../utilities-and-flow-networks/utilities-and-flow-networks.md) |
| `SoilWaterParameterData` | the soil-moisture diffusion tuning; the rain-flood counter reads none of it ([disasters.md](disasters.md)) |
| `ExtractorParameterData` | `m_OreConsumption`, `m_OilConsumption` — the depletion divisors ([natural-resources.md](natural-resources.md)) |
| `AttractivenessParameterData` | forest and shore distances and effects, the height bonus; the tourism weather fields belong to [`economy-and-companies`](../economy-and-companies/economy-and-companies.md) |
| `LandValueParameterData` | the three pollution penalty multipliers — the component belongs to [`zoning-buildings-and-land-value`](../zoning-buildings-and-land-value/zoning-buildings-and-land-value.md) |
| `BuildingEfficiencyParameterData` | `m_WaterPollutionPenalty`, the ungated dirty-water efficiency penalty ([water-and-groundwater.md](water-and-groundwater.md)) — the component belongs to [`utilities-and-flow-networks`](../utilities-and-flow-networks/utilities-and-flow-networks.md) |
| `ElectricityParameterData` | `m_CloudinessSolarPenalty`, the solar-output cloudiness penalty ([climate-and-weather.md](climate-and-weather.md)) — the component belongs to [`utilities-and-flow-networks`](../utilities-and-flow-networks/utilities-and-flow-networks.md) |
| `CitizenHappinessParameterData` | the pollution bonus caps and divisor; the budget they feed belongs to [`citizens-and-households`](../citizens-and-households/citizens-and-households.md) |

The cross-layer links, each mechanism at its citation:

| From | To | Mechanism |
| --- | --- | --- |
| Building, zone | ground, air, noise maps | distance-weighted stamp ([emission.md](emission.md)) |
| Vehicle | road `Pollution`, then air and noise maps | side-effect accumulator, then a star stamp ([emission.md](emission.md)) |
| Wind | air map; disaster movement and wind-turbine output | semi-Lagrangian advection ([map-dynamics.md](map-dynamics.md)); the wind consumers in [climate-and-weather.md](climate-and-weather.md) |
| Ground map | groundwater `m_Polluted` | per-cell contamination ([water-and-groundwater.md](water-and-groundwater.md)) |
| Ground map | fertile land `m_Used` | pollution scarring ([natural-resources.md](natural-resources.md)) |
| Ground and air maps | `Plant.m_Pollution` | saturating sickness ([map-dynamics.md](map-dynamics.md)) |
| Groundwater, surface water | `WaterPipeEdge.m_FreshPollution` | pump intake sampling ([water-and-groundwater.md](water-and-groundwater.md)) |
| Sewage outlet | surface water | polluted source write ([water-and-groundwater.md](water-and-groundwater.md)) |
| Surface water depth and pollution, noise map | fish stock and loss | derivation ([natural-resources.md](natural-resources.md)) |
| Ground and air maps | citizen health — and the noise map, wellbeing | `CitizenHappinessSystem`'s three public statics (`src/Game/Game.Simulation/CitizenHappinessSystem.cs`) |
| All three maps | land value, and rent through it | weighted penalty (`src/Game/Game.Simulation/LandValueSystem.cs`) |
| All three maps | zone spawning, the pollution warning icons, property choice | `RentAdjustSystem` raises the icons; `HouseholdFindPropertySystem` and `CitizenPathfindSetup` reuse the happiness statics; `ZoneSpawnSystem` samples the maps raw |
| Air map | `TriggerType.AverageAirPollution` | `PollutionTriggerSystem` averages the air happiness bonus over households (`src/Game/Game.Simulation/PollutionTriggerSystem.cs`) |
| Piped water `m_FreshPollution` | building efficiency, citizen health | `DispatchWaterSystem` and `CitizenHappinessSystem.GetWaterPollutionBonuses`; the graph itself belongs to [`utilities-and-flow-networks`](../utilities-and-flow-networks/utilities-and-flow-networks.md) ([water-and-groundwater.md](water-and-groundwater.md)) |
| Climate | consumption, fire, leisure, tourism, wetness, disasters | the consumer table in [climate-and-weather.md](climate-and-weather.md) |

Rain does not scrub the air map, water flow does not clean the ground map, and ground pollution never reaches surface water — the update rules are in [map-dynamics.md](map-dynamics.md) and [water-and-groundwater.md](water-and-groundwater.md).

## Traps

**A declared constant in this topic's files is not necessarily what the system runs on — check for a reader before citing one.**
`NaturalResourceSystem` declares `FERTILITY_REGENERATION_RATE` at 32× the `25` its `OnUpdate` compiles into the job, and the topic carries more declarations nothing reads; the operative number is the literal at the consuming line, and the check is one grep for the constant's name over `src/`.
Source: `src/Game/Game.Simulation/NaturalResourceSystem.cs`, `src/Game/Game.Simulation/GroundWaterSystem.cs`, `src/Game/Game.Simulation/WeatherHazardSystem.cs`, `src/Game/Game.Simulation/FireHazardSystem.cs`, `src/Game/Game.Events/EventUtils.cs`.

**A field initializer on a prefab class in this topic is a Unity-serialized default the shipped asset overrides, not the value.**
`PollutionPrefab` initializes every field and copies them into `PollutionParameterData` at load, but the shipped asset overrides the initializers and nothing in the C# marks which survived — the value the game runs on is the baked component, read live.
Source: `src/Game/Game.Prefabs/PollutionPrefab.cs`, `src/Game/Game.Prefabs/PollutionParameterData.cs`.

**The parameter components above are not all write-once.**
`GameModeSystem` has the loaded game mode rebuild parameter components on every load — a class under `src/Game/Game.Prefabs.Modes` naming yours makes it a candidate, and whether the loaded mode runs that class is authored asset data no code read settles.
Source: `src/Game/Game.Prefabs.Modes/GameModeSystem.cs`, `src/Game/Game.Prefabs.Modes/PollutionPrefabMode.cs`.

## Mechanisms

| Mechanism | Vanilla owner | Listing |
| --- | --- | --- |
| Cell-map storage, sampling, the read/write protocol, serialization | `CellMapSystem<T>` and each layer's static sampler | [cell-maps.md](cell-maps.md) |
| Building, zone, road and vehicle emission | `BuildingPollutionAddSystem`, `NetPollutionSystem`, the four navigation systems | [emission.md](emission.md) |
| Advection, diffusion, fade, the noise rebuild, plant damage, attractiveness | `AirPollutionSystem`, `GroundPollutionSystem`, `NoisePollutionSystem`, `ObjectPolluteSystem`, `TerrainAttractivenessSystem` | [map-dynamics.md](map-dynamics.md) |
| Groundwater flow and contamination, surface water, the sewage-to-intake chain | `GroundWaterSystem`, `GroundWaterPollutionSystem`, `WaterSystem`/`WaterSimulation`, `SewageOutletAISystem`, `WaterPumpingStationAISystem` | [water-and-groundwater.md](water-and-groundwater.md) |
| Resource regeneration, extraction, depletion, refill, the per-district recompute | `NaturalResourceSystem`, `AreaLotSimulationSystem`, `AreaResourceSystem`, `GameModeNaturalResourcesAdjustSystem` | [natural-resources.md](natural-resources.md) |
| Climate, seasons, weather, wind, snow, day and night | `ClimateSystem`, `WindSimulationSystem`/`WindSystem`, `SnowSystem`/`WetnessSystem`, `EffectFlagSystem` | [climate-and-weather.md](climate-and-weather.md) |
| Disaster spawn, tick, damage, endangerment, fire, the dead flood | `WeatherHazardSystem`, `Game.Events.InitializeSystem`, `WeatherPhenomenonSystem`, `WeatherDamageSystem`, `FireHazardSystem`, `SoilWaterSystem` | [disasters.md](disasters.md) |

## Bridges

- [`ecs-in-this-game`](../../technique/ecs-in-this-game/ecs-in-this-game.md) — `GetSingleton` parameter reads, `UpdateFrame` sharding and `GetUpdateInterval` throttles are its machinery; what this topic adds is the `CellMapSystem<T>` reader/writer protocol, which is no `EntityQuery` at all but a `NativeArray` behind two hand-rolled `JobHandle` chains with an asymmetry no ECS analogue has ([cell-maps.md](cell-maps.md)).
- [`performance-and-memory`](../../technique/performance-and-memory/performance-and-memory.md) — the air, ground and groundwater systems each walk their full map in a single serial `IJob` while noise and resources run parallel jobs, so a mod adding a full-map pass copies the parallel shape; the water and snow layers are GPU simulations unreachable from a job, making "sample the water" a readback-latency question; and the emission stamp `stackalloc`s a buffer scaling with the square of a radius parameter inside a Burst job ([emission.md](emission.md)).
- [`utilities-and-flow-networks`](../utilities-and-flow-networks/utilities-and-flow-networks.md) — owns everything downstream of `WaterPipeEdge.m_FreshPollution`; the seam is the pump intake, where a sampled concentration becomes a graph value, and the sewage outlet, where a graph value becomes an environmental one ([water-and-groundwater.md](water-and-groundwater.md)).
- [`economy-and-companies`](../economy-and-companies/economy-and-companies.md) — owns what a deposit is worth and decides the extractor's `m_ExtractedAmount`; this topic turns that into per-cell `m_Used` ([natural-resources.md](natural-resources.md)), hands it the per-district `MapFeatureElement` gate, and takes back the industrial city modifiers and the `efficiency > 0` gate on building emission ([emission.md](emission.md)).
- [`citizens-and-households`](../citizens-and-households/citizens-and-households.md) — owns the happiness budget behind `CitizenHappinessSystem`'s pollution statics — a mod that changes pollution changes where households move — and owns what a citizen does once a disaster marks it `InDanger` ([disasters.md](disasters.md)).
- [`roads-and-traffic`](../roads-and-traffic/roads-and-traffic.md) — owns the net substrate the road `Pollution` component rides — nodes, edges, compositions — and what a sound barrier or beautification upgrade is; what those upgrades multiply is [emission.md](emission.md).
- [`transportation-and-vehicles`](../transportation-and-vehicles/transportation-and-vehicles.md) — owns the vehicles, their navigation systems and the per-vehicle side-effect lerp; what the written `Game.Net.Pollution` becomes from there is this topic's ([emission.md](emission.md)).
- [`zoning-buildings-and-land-value`](../zoning-buildings-and-land-value/zoning-buildings-and-land-value.md) — owns `LandValueSystem`'s formula and the zone machinery; `ZonePollution` bakes zone rates into a zoned building's `PollutionData` ([emission.md](emission.md)).
- [`city-services-and-coverage`](../city-services-and-coverage/city-services-and-coverage.md) — owns the response: fire dispatch and rescue once `OnFire` is set, disaster response capacity, shelters and the building-side early-warning half ([disasters.md](disasters.md)), and the maintenance depots the snow label routes to ([climate-and-weather.md](climate-and-weather.md)).
- [`city-state-and-progression`](../city-state-and-progression/city-state-and-progression.md) — owns `CityModifierType`, applied to industrial emission ([emission.md](emission.md)) and disaster warning and damage ([disasters.md](disasters.md)), and the `Locked` progression gate on event prefabs.
- [`simulation-time-and-units`](../simulation-time-and-units/simulation-time-and-units.md) — owns what a frame, an update interval and `kUpdatesPerDay` are worth in game time; every rate in this topic is stated per update or per 2048-frame pass.
- [`save-serialization`](../../technique/save-serialization/save-serialization.md) — owns save-format versioning and migration; the cell maps serialize through their base class, so a map write lands in the save ([cell-maps.md](cell-maps.md)).
- [`prefabs-and-assets`](../../technique/prefabs-and-assets/prefabs-and-assets.md) — retuning this topic means a prefab-phase system overwriting the baked `*Data` component, never patching the class field the asset overrides anyway.
- [`mod-lifecycle-and-ordering`](../../technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md) — decides the phase for that write, and carries the `OnGameLoaded` hook that fires after the game-mode rebuild the traps name.

(VOLATILE: every component, field, enum, system, method and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Prefabs`, `Game.Prefabs.Climate`, `Game.Buildings`, `Game.Net`, `Game.Objects`, `Game.Events`, `Game.Areas`, `Game.Rendering`, `Game.Triggers`, `Game.City` and `Game.Prefabs.Modes`, at the files the rows and traps cite.)
