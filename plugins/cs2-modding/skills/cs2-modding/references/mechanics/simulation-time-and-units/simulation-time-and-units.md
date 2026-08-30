# Simulation time and units

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

The simulation's only clock is `SimulationSystem.frameIndex`, a `uint` the step loop increments once per simulation frame; every elapsed time in the game is a difference of two of its values ([clock-and-speed.md](clock-and-speed.md)).
A day is `TimeSystem.kTicksPerDay = 262144` frames, a `const` the consuming code almost always spells as the bare literal, and a day is also a month: `TimeSystem` has no month concept, and every surface that says "month" reads the day-of-year index.
The year is `262144 * TimeSettingsData.m_DaysPerYear` frames, where the field is prefab data a mod may change; the game's own code compiles a twelve in as an assertion, an enum and a table dimension, so changing it degrades what those touch ([calendar.md](calendar.md)).
The calendar is `TimeSystem` arithmetic over `frameIndex`, the `TimeData` singleton's epoch `m_FirstFrame` and offsets, and that one settings field; the epoch is whatever `frameIndex` read when the city was founded, so city age is `frameIndex - TimeData.m_FirstFrame` and never `frameIndex` alone.
A system's cadence in the three simulation phases is the `UpdateSystem` mask over `frameIndex`, so an interval of `N` is `262144 / N` passes a day, and a duration a mod computes from its own interval is worth `N / 262144` of a day per pass ([cadence.md](cadence.md)).
Day and night have no single boundary: each consumer compares `TimeSystem.normalizedTime` against its own constants, the sun runs on a 365-day astronomical year stretched over the game year, and a season is a year fraction on the climate prefab ([day-night-and-seasons.md](day-night-and-seasons.md)).
What a raw C# value crossing to the frontend is denominated in — metres, kilograms, hundreds of watts, per in-game day — is stated nowhere in the decompile; the frontend's formatter table is the only first-party statement and it ships here whole, beside the few bake conventions the C# side does state ([units.md](units.md)).

## The map

Default reads: `SimulationSystem`, `TimeSystem`, `RenderingSystem` and `PlanetarySystem` are managed systems reached with `World.GetExistingSystemManaged<T>()`, never components; `TimeData` is `GetSingleton<TimeData>()` on a plain entity, or `TimeData.GetSingleton(EntityQuery)` which returns the `kDefault*` struct when the query is empty; `TimeSettingsData` is `GetSingleton<TimeSettingsData>()` on the `TimeSettings` prefab entity, which an ordinary query finds because this game's prefab entities carry `PrefabData` rather than a `Prefab` tag.

The clock ([clock-and-speed.md](clock-and-speed.md)):

| The game models | Component or member | Access shape |
| --- | --- | --- |
| The tick | `Game.Simulation.SimulationSystem.frameIndex`, `uint` | read-only; advanced only by the step loop and the loading burn |
| The sub-frame remainder | `SimulationSystem.frameTime`, `float` in `[0, 1]` | `m_Timer * 60f`, the fraction between two ticks |
| The selected speed | `SimulationSystem.selectedSpeed`, `float` | settable; a write during loading is dropped; `0` is paused; `TimeUISystem` forces it to `0` while its barrier is up |
| The achieved speed | `SimulationSystem.smoothSpeed`, `frameDuration` | read-only; a smoothed estimate, and real seconds one step cost |
| The performance clamp | `SimulationSystem.performancePreference`, `{ FrameRate, Balanced, SimulationSpeed }` | copied from `SharedSettings.instance.general.performancePreference` at `OnCreate` |
| The visual clock | `Game.Rendering.RenderingSystem.frameIndex`, `frameTime` | a second counter interpolated around the simulation's; a visual reads this one, a simulation decision never does |

The calendar ([calendar.md](calendar.md)):

| The game models | Component or member | Access shape |
| --- | --- | --- |
| The day length | `TimeSystem.kTicksPerDay = 262144`, `const` | compiled in; the code spells the literal |
| The epoch and the starting date | `Game.Common.TimeData { m_FirstFrame, m_StartingYear, m_StartingMonth, m_StartingHour, m_StartingMinutes }` | `TimeOffset` and `GetDateOffset(daysPerYear)` are its derived fractions; `kDefaultStartingYear = 2021`, `kDefaultStartingMonth = 5`, `kDefaultStartingHour = 7`, `kDefaultStartingMinutes = 0` are its `const` defaults, which a load lacking the entity gets |
| The year length | `Game.Prefabs.TimeSettingsData { m_DaysPerYear }` | prefab; `TimeSettingsPrefab.LateInitialize` copies its serialized field in; no class under `src/Game/Game.Prefabs.Modes/` rewrites it on load |
| Time of day, of year, the year | `TimeSystem.normalizedTime`, `normalizedDate`, `year`, `daysPerYear` | two `[0, 1)` fractions and two `int`s cached by `UpdateTime` every simulation frame; `startingYear` on the same system is a settable nothing maintains after a new game |
| The day number | `TimeSystem.GetDay(frame, TimeData)`, static | days since founding, never resetting; `Citizen.m_BirthDay` is one of its values |
| The frontend clock | the `time` binding group on `Game.UI.InGame.TimeUISystem` | registered in `OnCreate`; [calendar.md](calendar.md) lists its values, event and triggers |

Cadence ([cadence.md](cadence.md)):

| The game models | Component or member | Access shape |
| --- | --- | --- |
| A system's cadence | `GameSystemBase.GetUpdateInterval(phase)`, `GetUpdateOffset(phase)` | virtual, defaults `1` and `-1`; registering one belongs to [`mod-lifecycle-and-ordering`](../../technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md) |
| A per-entity rate | `SimulationUtils.GetUpdateFrame(frame, updatesPerDay, groupCount)` and its two siblings | static; paired with `interval = 262144 / (kUpdatesPerDay * groupCount)` |
| A solver hour | `ElectricityFlowSystem.kUpdatesPerHour = 85`, `const` | the divisor in `Battery.storedEnergyHours` and the factor in `BatteryData.capacityTicks` |

Day, night and seasons ([day-night-and-seasons.md](day-night-and-seasons.md)):

| The game models | Component or member | Access shape |
| --- | --- | --- |
| Night, for effects and climate | `EffectFlagSystem.kNightBegin = 0.75f`, `kDayBegin = 0.25f`, `static readonly` | compared against `normalizedTime`; other consumers compile their own literals |
| The day quarter and the month bit | `Game.Prefabs.CalendarEventTimes`, `CalendarEventMonths` | `1 << floor(normalizedTime * 4f)` and `1 << floor(normalizedDate * 12f)` in `CalendarEventLaunchSystem`; the month bit is [calendar.md](calendar.md)'s |
| Sleep | `CitizenBehaviorSystem.GetSleepTime(entity, citizen, …)`, static | a per-citizen window from a compiled-in base, shifted clear of `EconomyParameterData`'s work hours; `IsSleepTime` is the test, and the derivation belongs to [`citizens-and-households`](../citizens-and-households/citizens-and-households.md) |
| The sun | `Game.Simulation.PlanetarySystem { latitude, longitude, sunLimit, day, time, year }` | properties on the managed system, seeded from `ClimatePrefab.m_Latitude`, `m_Longitude`, `sunLimitRadians` once per load and stale after a runtime climate switch; `kDefaultLatitude`, `kDefaultLongitude` are its `private static readonly` fallbacks |
| A season | `ClimateSystem.SeasonInfo { m_Prefab, m_StartTime, … }` in `ClimatePrefab.m_Seasons` | a `[Serializable]` class array, not a component; `FindSeasonByTime(normalizedDate)` on `PrefabSystem.GetPrefab<ClimatePrefab>(ClimateSystem.currentClimate)` is the read; what a season does belongs to [`environment-and-pollution`](../environment-and-pollution/environment-and-pollution.md) |
| The visual cycle's look | `DayNightCycleData : ScriptableObject` | sun-angle thresholds, exposure and colour only; it decides no timing |

Units ([units.md](units.md)):

| The game models | Component or member | Access shape |
| --- | --- | --- |
| A value's unit | `Game.UI.Unit`'s `const string`s, carried by `LocalizedNumber<T>` | opaque strings; the frontend's formatter table gives each its divisor and threshold |
| The player's unit preferences | `Game.Settings.InterfaceSettings.unitSystem`, `temperatureUnit` | read off `SharedSettings.instance.userInterface`; enums whose ordinals the frontend declares identically; rendering and `timeFormat` belong to [`units-and-formatting`](../../technique/units-and-formatting/units-and-formatting.md) |

## Traps

**A duration authored on prefab data is in days and is multiplied by `262144` at the point of use; the same duration stored on an instance component is already in frames.**
The multiplier at the use site says which, and [cadence.md](cadence.md) rosters the sites, the `* 60f` real-seconds exception included.
Source: `src/Game/Game.Events/InitializeSystem.cs`, `src/Game/Game.Net/NetUtils.cs`, `src/Game/Game.Prefabs/PoliceCar.cs`.

**`frameIndex` is not zero when a city is founded.**
`TimeSystem.PostDeserialize` writes `m_FirstFrame = m_SimulationSystem.frameIndex` on `Purpose.NewGame`, and the counter already holds whatever the process advanced before that; `RecentClearSystem`'s `frameIndex >= 262144` guard is written as if it started at zero.
Source: `src/Game/Game.Simulation/TimeSystem.cs`, `src/Game/Game.Simulation/SimulationSystem.cs`, `src/Game/Game.Tools/RecentClearSystem.cs`.

**A field initializer on this topic's prefab classes is a Unity-serialized default the shipped asset overrides, not the value.**
`TimeSettingsPrefab.m_DaysPerYear`, `ClimatePrefab.m_Latitude` and its three sun siblings, `SeasonInfo`'s eleven climate fields and most `DayNightCycleData` fields carry one, and nothing in the C# marks which survived.
Source: `src/Game/Game.Prefabs/TimeSettingsPrefab.cs`, `src/Game/Game.Prefabs.Climate/ClimatePrefab.cs`, `src/Game/Game.Simulation/ClimateSystem.cs`, `src/Game/DayNightCycleData.cs`.

**"Is it night?" has no single answer.**
Each consumer draws the boundary at its own constant, and a mod picking one gets behaviour that changes at boundaries it did not choose ([day-night-and-seasons.md](day-night-and-seasons.md) tabulates them).
Source: `src/Game/Game.Effects/EffectFlagSystem.cs`, `src/Game/Game.Simulation/ClimateSystem.cs`, `src/Game/Game.UI.InGame/TimeUISystem.cs`, `src/Game/Game.Simulation/TransportLineSystem.cs`, `src/Game/Game.Simulation/CitizenBehaviorSystem.cs`, `src/Game/Game.Prefabs/CalendarEventTimes.cs`.

**`TimeSystem.startingYear` is not the city's starting year.**
It is a settable property `PostDeserialize` reads once on a new game; the city's value is `TimeData.m_StartingYear`, and the property reads `0` on every loaded save.
Source: `src/Game/Game.Simulation/TimeSystem.cs`.

## Mechanisms

| Mechanism | Vanilla owner | Listing |
| --- | --- | --- |
| The step loop, the speeds, the pause barrier, the loading burn, the epoch's write, the rendering clock | `SimulationSystem`, `TimeUISystem`, `DebugSystem`, `TimeSystem`, `RenderingSystem` | [clock-and-speed.md](clock-and-speed.md) |
| The calendar expressions, the `time` bindings and the frontend's tick arithmetic | `TimeSystem`, `TimeUISystem`, `game-ui/game/data-binding/time-bindings.ts` | [calendar.md](calendar.md) |
| The update mask, updates per day, `UpdateFrame` sharding, the solver hour | `UpdateSystem`, `SimulationUtils`, `ElectricityFlowSystem` | [cadence.md](cadence.md) |
| The day-night boundaries, the sun, the seasons | `EffectFlagSystem`, `CalendarEventLaunchSystem`, `PlanetarySystem`, `ClimateSystem`, `ClimatePrefab` | [day-night-and-seasons.md](day-night-and-seasons.md) |
| The unit table, the US-customary functions, the C# bake conventions | `game-ui/common/localization/localized-number.tsx`, `unit.ts`, `Game.UI.Unit` | [units.md](units.md) |

## Bridges

- [`mod-lifecycle-and-ordering`](../../technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md) — owns registering a system at an interval and offset, the power-of-two throw and the phases that consult the interval; this topic owns what the resulting cadence is worth.
  A system reading `TimeSystem.normalizedTime` runs after `TimeSystem`, which is `UpdateAt<TimeSystem>(SystemUpdatePhase.GameSimulation)` and `PostDeserialize<TimeSystem>` in `Deserialize`.
- [`ecs-in-this-game`](../../technique/ecs-in-this-game/ecs-in-this-game.md) — the read shapes above.
- [`prefabs-and-assets`](../../technique/prefabs-and-assets/prefabs-and-assets.md) — `TimeSettingsPrefab.LateInitialize` writes `TimeSettingsData`, so a mod changing the year length edits the prefab before initialisation or the component after it.
- [`save-serialization`](../../technique/save-serialization/save-serialization.md) — `SimulationSystem` serializes `frameIndex` and `TimeData` all five fields, so a frame number a mod stores in its own save data means something only against that save's `m_FirstFrame`.
- [`patching`](../../technique/patching/patching.md) — the only route into `TimeSystem`'s instance arithmetic, which nothing substitutes for; `GetDay` is static and called directly.
- [`debug-menu`](../../technique/debug-menu/debug-menu.md) — the eight-speed radio and the three advance buttons that move the clock by rewinding the epoch.
- [`diagnostics`](../../technique/diagnostics/diagnostics.md) — `frameDuration` is the game's own measurement of what a simulation step costs.
- [`performance-and-memory`](../../technique/performance-and-memory/performance-and-memory.md) — the frame-budget clamp in the step loop is where a mod's per-frame cost becomes a slower clock rather than a lower frame rate.
- [`binding-layer`](../../../../cs2-modding-ui/references/binding-layer/binding-layer.md) — the `time` group; `simulationPausedBarrier`'s subscription is the mechanism.
- [`frontend-and-injection`](../../../../cs2-modding-ui/references/frontend-and-injection/frontend-and-injection.md) — `game-ui/game/data-binding/time-bindings.ts`, `game-ui/common/localization/unit.ts`, `localized-number.tsx` and `units-us-customary.ts` are the registry paths a UI mod reaching the clock or a unit needs.
- [`units-and-formatting`](../../technique/units-and-formatting/units-and-formatting.md) — owns rendering: the `LocalizedNumber` call, the `cs2/l10n` exports, the preference settings as a surface; the unit table's magnitudes are this topic's.
- [`environment-and-pollution`](../environment-and-pollution/environment-and-pollution.md) — owns what weather and seasons do; takes the year as `262144 * m_DaysPerYear`, `SeasonInfo.m_StartTime` as a year fraction and the day-night boundary table from here.
- [`citizens-and-households`](../citizens-and-households/citizens-and-households.md) — age thresholds and `m_BirthDay` are `GetDay` values, per-citizen cadences are `UpdateFrame` shards, and a work-shift offset is an hour of `262144 / 24` frames.
- [`city-services-and-coverage`](../city-services-and-coverage/city-services-and-coverage.md) — the `262144 / (kUpdatesPerDay * 16)` idiom, the eight-service rotation and the dispatch backoff are all durations only once a tick is worth something.
- [`city-state-and-progression`](../city-state-and-progression/city-state-and-progression.md) — the `8192`-frame statistic sample is a thirty-second of a day, which is what makes a `Daily` series a day.
- [`economy-and-companies`](../economy-and-companies/economy-and-companies.md) — owns `EconomyParameterData`; its per-day wages, taxes and loans are per `262144` frames, and `65536` is a quarter-day.
- [`roads-and-traffic`](../roads-and-traffic/roads-and-traffic.md) — km/h to m/s on `RoadData.m_SpeedLimit`, the tent over `normalizedTime * 4` that blends the `Road` component's four day-quarter slots, and the pathfind backpressure that slows the step loop.
- [`transportation-and-vehicles`](../transportation-and-vehicles/transportation-and-vehicles.md) — km/h to m/s and degrees to radians on vehicle prefabs, and the `0.25f` / `11f / 12f` night window on transport lines.
- [`utilities-and-flow-networks`](../utilities-and-flow-networks/utilities-and-flow-networks.md) — `frameIndex % 128` solver ticks and `kUpdatesPerHour = 85` as the power-to-energy conversion.
- [`zoning-buildings-and-land-value`](../zoning-buildings-and-land-value/zoning-buildings-and-land-value.md) — a system's interval is not how often one building is touched, which is `UpdateFrame` sharding.

(VOLATILE: every system, component, field, property, enum, method, constant, binding name and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Common`, `Game.Prefabs`, `Game.Prefabs.Climate`, `Game.Prefabs.Modes`, `Game.Rendering`, `Game.Effects`, `Game.Events`, `Game.Net`, `Game.Tools`, `Game.Buildings`, `Game.Settings`, `Game.Debug`, `Game.UI`, `Game.UI.InGame`, `Game.UI.Localization`, `Game.Citizens`, the root `Game` namespace and the assembly's global namespace, at the files the rows and traps cite; and the frontend module paths it names — the shipped UI bundle's module registry.)
