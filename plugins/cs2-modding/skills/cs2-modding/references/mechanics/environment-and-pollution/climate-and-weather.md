# Climate and weather

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Climate is a managed system whose whole state is C# properties — there is no per-cell climate and no climate component on any entity, and the two `IComponentData` types in `Game.Prefabs.Climate` (`ClimateData`, `SeasonData`) are zero-size tags.

## The five properties, and how to read and pin them

Sources: `src/Game/Game.Simulation/ClimateSystem.cs`, `src/Game/Game/OverridableProperty.cs`.

`temperature`, `precipitation`, `cloudiness`, `aurora` and `fog` are `OverridableProperty<float>`, plus `currentDate`; a seventh, `thunder`, is declared and read by nothing in `src/`.
An `OverridableProperty<T>` holds a value, an override value, an override flag and an optional synchroniser; setting `.overrideValue` also sets `.overrideState`, and the implicit conversion returns the override when set, otherwise the synchroniser's result, otherwise the plain value.
So a mod pins the weather with one assignment to `.overrideValue` and releases it by setting `.overrideState = false`; `SampleClimate(t)` honours the overrides on every channel, so every consumer reading through the implicit conversion sees them — while the game's own `.value` readers (solar output, outdoor leisure, the soil-water rain term) never do.
`isRaining`, `isSnowing` and `isPrecipitating` derive from `precipitation > 0` split by `temperature` against `freezingTemperature`.

**`.value` is not the value.**
`currentDate` is backed by a synchroniser reading `TimeSystem.normalizedDate`, so its `.value` is a never-written backing field reading 0; read every climate property through the implicit conversion.
Source: `src/Game/Game.Simulation/ClimateSystem.cs`, `src/Game/Game/OverridableProperty.cs`.

**The cached properties are stale while the simulation is paused.**
`OnUpdate` refreshes them and the system runs only in `GameSimulation` and `EditorSimulation`, so `SampleClimate(t)` is the read that always works and the properties are the read that works while the game is running.
Source: `src/Game/Game.Simulation/ClimateSystem.cs`.

## A climate as data, and the seasons

Sources: `src/Game/Game.Prefabs.Climate/ClimatePrefab.cs`, `src/Game/Game.Simulation/ClimateSystem.cs`.

`ClimatePrefab` carries latitude, longitude, the sun-elevation clamp, `m_FreezingTemperature`, `m_Seasons[]`, the weather prefab set and five hidden `AnimationCurve`s — generated from the seasons, each `SeasonInfo` keyed at its mid-time, rather than authored.
The generated curves run on a fixed 0..12 time axis — `kYearDuration`, inlined as literals throughout `ClimatePrefab` — while `SampleClimate` evaluates them at `t * daysPerYear`, which comes from `TimeSettingsPrefab.m_DaysPerYear`, also 12 by coincidence rather than through a shared constant, so a mod changing the year length desyncs the sample point from the curve axis; what a year, season or day is worth in real time belongs to `simulation-time-and-units`.
`m_Seasons` is a plain array `FindSeasonByTime(normalizedDate)` walks — a climate may hold any number of seasons, not four.
`currentSeason` resolves to the season prefab's entity and `currentSeasonName` to its name.
Average temperature switches estimator on `daysPerYear == 12` — the Ekholm–Modén weighted monthly mean over a hard-coded table and sample hours, against a plain mean otherwise — so a mod changing the year length silently switches the game to the other estimator.

Weather above the curves is prefab selection: the live cloudiness picks the *next* `WeatherPrefab` by nearest `m_CloudinessRange` and *current* as its predecessor in the climate's array, each expanded through its placeholder buffer filtered against the current season, and the selected prefabs' last non-`Irrelevant` `m_Classification` becomes `ClimateSystem.classification` (`Irrelevant` through `Stormy`).
Everything else about a weather prefab is rendering; the gameplay surface is the five curve samples, that enum, and `hail` — a plain settable property nothing in the game writes, read first in `HandleTriggers`, where any value above 0.001 forces `WeatherStormy`, so a mod can drive a hailstorm the five channels cannot reach.
Climate reaches the trigger system too: `TriggerType.Temperature` every update, then at most one weather trigger (`WeatherStormy`, `WeatherRainy`, `WeatherSnowy`, `WeatherSunny`, `WeatherClear`, `WeatherCloudy`, `AuroraBorealis`), cut by C# literals in `HandleTriggers` — a cloudy, dry night fires none.

## What climate drives

The simulation-side consumers, each read at its citation:

| Consumer (`src/Game/Game.Simulation/` unless noted) | Input | Effect |
| --- | --- | --- |
| `AdjustElectricityConsumptionSystem` | `temperature` | consumption times a prefab `AnimationCurve` over temperature |
| `BuildingUpkeepSystem` | `temperature` | `GetHeatingMultiplier(t) = max(0, 15 − t)`, a public static — the `m_TemperatureUpkeep` it feeds is read by nothing at 1.6.0f1 |
| `PowerPlantAISystem` | `cloudiness.value` | solar output `*= lerp(1, 1 − ElectricityParameterData.m_CloudinessSolarPenalty, cloudiness.value)` |
| `FireHazardSystem`, `FireSimulationSystem` | `isRaining`, `temperature` | fire hazard, [disasters.md](disasters.md) |
| `WeatherHazardSystem` | `temperature`, `precipitation`, `cloudiness` | disaster spawn probability, [disasters.md](disasters.md) |
| `LeisureSystem` | `precipitation.value`, `temperature` | leisure appeal per type: beach `0.05 + 4 * saturate(0.35 − precipitation) * saturate((t − 20) / 30)`, park `2 * (1 − 0.95 * precipitation)`, indoors flat `1`, travel `0.5 + saturate((30 − t) / 50)` |
| `TourismSystem`, `TouristSpawnSystem` | `classification`, `temperature`, `precipitation`, `isRaining`, `isSnowing` | tourist spawn probability against `AttractivenessParameterData`'s weather fields |
| `WetnessSystem` | `precipitation`, `temperature` | wetness and snow targets and rates, below |
| `SnowSystem` | `temperature`, `precipitation` | parameters into the snow compute shader, below |
| `SoilWaterSystem` | `precipitation.value` | the rain-flood counter — never ticks at 1.6.0f1 ([disasters.md](disasters.md)) |
| `IndustrialFindPropertySystem` | `averageTemperature` | property scoring input |
| `EffectFlagSystem` (`Game.Effects`), `ClimateRenderSystem` (`Game.Rendering`), the colour and audio systems | various | presentation |

`WindSimulationSystem`, `IndustrialDemandSystem` and `TrafficSpawnerAISystem` each hold a `ClimateSystem` reference and never read it — most importantly, climate does not drive the wind.

## Wind

Sources: `src/Game/Game.Simulation/WindSimulationSystem.cs`, `src/Game/Game.Simulation/WindSystem.cs`, `src/Game/Game.Simulation/Wind.cs`.

`WindSimulationSystem` holds a 64×64×16 volume of `WindCell { m_Pressure, m_Velocities }`, alternating a velocity pass over terrain and water heights with a pressure pass pushing the boundary toward a target built from the prevailing wind and a 1/7 power-law altitude profile.
`WindSystem` flattens that volume into the 2D `Wind { m_Wind }` cell map by sampling each cell at terrain height plus 25 metres.
The prevailing wind is `WindSimulationSystem.constantWind`: serialized with the cells, written by nothing in the simulation, and set only by the map editor's climate panel through `SetWind(direction, pressure)` — wind direction is a map property, and a mod changing it calls `SetWind`, which resets every cell.
Its three gameplay consumers are the air-map advection, disaster movement ([disasters.md](disasters.md)), and wind-turbine output in `PowerPlantAISystem`.

**`ClimateSystem.wind` is dead, and it is the first wind a reader finds.**
It is an auto-property with no reader and no writer anywhere in `src/`; the operative value is `WindSimulationSystem.constantWind`.
Source: `src/Game/Game.Simulation/ClimateSystem.cs`, `src/Game/Game.Simulation/WindSimulationSystem.cs`.

## Snow and wetness

Sources: `src/Game/Game.Simulation/WetnessSystem.cs`, `src/Game/Game.Objects/Surface.cs`, `src/Game/Game.Buildings/BuildingUtils.cs`, `src/Game/Game.Simulation/SnowSystem.cs`.

`SnowSystem` is a second compute-shader simulation over a snow-depth render texture — like water, unreachable from a job.
The gameplay side is `Game.Objects.Surface { m_Wetness, m_SnowAmount, m_AccumulatedWetness, m_AccumulatedSnow, m_Dirtyness }`: the first four bytes are driven by `WetnessSystem` from precipitation and temperature, each chasing its target with a per-object random jitter, while `m_Dirtyness` belongs to `DirtynessSystem` and takes no climate input.
`m_AccumulatedSnow >= 15` is the one gameplay threshold, a C# literal: it raises `ObjectRequirementFlags.Snow` (swapping the object to its snow variant) and flips `BuildingUtils.GetMaintenanceType` from `MaintenanceType.Road` to `MaintenanceType.Snow`.

**A snowplough repairs road wear and does not remove snow.**
Nothing writes `Surface.m_AccumulatedSnow` except `WetnessSystem`, and the snow maintenance branch performs the same wear repair as the road one — snow only re-labels the request, and it leaves when the temperature-driven dry rate takes it; the depots the label routes to belong to `city-services-and-coverage`.
Which depots respond is `MaintenanceDepotData.m_MaintenanceType`, per depot prefab — the base road depot carries `Road | Snow | Vehicle`, one mask serving both labels, and the masks are an install's content set, re-derived by `ecs_query` on `Game.Prefabs.MaintenanceDepotData` reading each prefab's `m_MaintenanceType`.
Source: `src/Game/Game.Simulation/WetnessSystem.cs`, `src/Game/Game.Buildings/BuildingUtils.cs`, `src/Game/Game.Simulation/MaintenanceVehicleAISystem.cs`.

## Day and night

`EffectFlagSystem.kNightBegin = 0.75f` and `kDayBegin = 0.25f` are the game's definition of night — `m_IsNightTime = normalizedTime >= kNightBegin || normalizedTime < kDayBegin` — and `ClimateSystem.HandleTriggers` uses the same pair inline (`src/Game/Game.Effects/EffectFlagSystem.cs`, `src/Game/Game.Simulation/ClimateSystem.cs`).
The cycle's effect on the world is those effect flags plus `PlanetarySystem`'s sun position from the climate's latitude, longitude and sun-elevation clamp; simulation systems that behave differently at night each compare `TimeSystem.normalizedTime` against their own windows, and what the cycle is worth in time belongs to `simulation-time-and-units`.

(VOLATILE: every system, component, field, property, enum, constant, method and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Prefabs.Climate`, `Game.Prefabs`, `Game.Objects`, `Game.Buildings`, `Game.Effects`, `Game.Rendering`, `Game.Triggers` and the root `Game` namespace, at the files the sections cite; plus the base road depot's mask the snowplough trap states, re-derived by the query beside it.)
