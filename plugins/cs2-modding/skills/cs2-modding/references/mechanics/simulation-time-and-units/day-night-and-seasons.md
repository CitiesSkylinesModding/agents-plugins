# Day, night and seasons

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

There is no `DayNightSystem` and no shared day-night predicate.
Each consumer compares `TimeSystem.normalizedTime` against its own constants, `PlanetarySystem` places the sun by a real solar calculation at the map's latitude, `LightingSystem` reads the sun's angle into a `State`, and a season is a year fraction on the climate prefab.

## The boundaries

| Consumer | Test | Day window, as hours of a 24-hour day |
| --- | --- | --- |
| `EffectFlagSystem` | `night = t >= kNightBegin \|\| t < kDayBegin`, `0.75f` and `0.25f` `static readonly` | 06:00–18:00 |
| `ClimateSystem` | `day = t >= EffectFlagSystem.kDayBegin && t < kNightBegin` | 06:00–18:00 |
| `TimeUISystem.GetLightingState` | `day = !(t < 7f / 24f) && !(t > 0.875f)`, the fallback when `LightingSystem.state` is `Invalid` | 07:00–21:00 |
| `TransportLineSystem` | `isNight = t < 0.25f \|\| t >= 11f / 12f` | 06:00–22:00 |
| `CitizenBehaviorSystem.IsSleepTime` | `t` inside `GetSleepTime(…)`'s `float2`, wrapping when `y < x` | per citizen; the window's derivation belongs to [`citizens-and-households`](../citizens-and-households/citizens-and-households.md) |
| `CalendarEventLaunchSystem` | `(CalendarEventTimes)(1 << floor(t * 4f))`, `Night = 1, Morning = 2, Afternoon = 4, Evening = 8` | `Night` is 00:00–06:00, `Evening` 18:00–24:00 |

Source: `src/Game/Game.Effects/EffectFlagSystem.cs`, `src/Game/Game.Simulation/ClimateSystem.cs`, `src/Game/Game.UI.InGame/TimeUISystem.cs`, `src/Game/Game.Simulation/TransportLineSystem.cs`, `src/Game/Game.Simulation/CitizenBehaviorSystem.cs`, `src/Game/Game.Simulation/CalendarEventLaunchSystem.cs`, `src/Game/Game.Prefabs/CalendarEventTimes.cs`.

`RoadSafetySystem` and `TrafficFlowSystem` use the same `normalizedTime * 4f` without flooring it, as the centre of a saturated tent of four weights over the `Road` component's four per-quarter slots ([`roads-and-traffic`](../roads-and-traffic/roads-and-traffic.md)).

## The sun

Source: `src/Game/Game.Simulation/PlanetarySystem.cs`, `src/Game/Game.Simulation/ClimateSystem.cs`, `src/Game/Game.Prefabs.Climate/ClimatePrefab.cs`.

```
PlanetarySystem keeps a real-world date: m_Year, m_Day, m_Hour, m_Minute, m_Second, m_Latitude, m_Longitude
    kDaysInYear = 365f, kHoursInDay = 24f, kSecsInHour = 3600f, kLunarCyclesPerYear = 12f   // private const
    kDefaultLatitude, kDefaultLongitude                                                     // private static readonly; SetDefaults restores them
    serializes latitude and longitude only

OnUpdate (PreCulling):
    lat, lon = latitude, longitude
    in GameMode.Game:
        if !overrideTime and !gameplay.dayNightVisual:
            lat = 51.2277f; lon = 6.7735f; time = 14.5f; day = 177; year = 2020; treat as overridden
        if not overridden, with TimeSettingsData and TimeData singletons:
            renderingFrame = (RenderingSystem.frameIndex - data.m_FirstFrame) + RenderingSystem.frameTime
            UpdateTime(TimeSystem.GetTimeOfYear(settings, data, renderingFrame), TimeSystem.GetTimeOfDay(settings, data, renderingFrame) * debugTimeMultiplier, TimeSystem.GetYear(settings, data))
    in GameMode.Editor: the same without the dayNightVisual branch, and GetYear(…, renderingFrame)
    then the sun direction from the date, lat, lon and sunLimit

normalizedDayOfYear setter:   dayOfYear = value * 365f + 1f
moonDay                     = floor(day * (1f / 12f) * numberOfLunarCyclesPerYear)

ClimateSystem.PostDeserialize, once per load (the currentClimate setter never touches PlanetarySystem):
    planetary.latitude = prefab.m_Latitude; .longitude = prefab.m_Longitude; .sunLimit = prefab.sunLimitRadians
ClimatePrefab.sunLimitRadians = double2(radians(m_SunElevationClampStart), radians(m_MaxSunElevationAngle))
```

So the game year's `timeOfYear` fraction is stretched across 365 astronomical days and the sun's position is a solar calculation, which is why day length varies with the season and with the map's latitude and no curve or constant states it.
`overrideTime` and `debugTimeMultiplier` are the developer menu's, reached through [`debug-menu`](../../technique/debug-menu/debug-menu.md).

**The visual day-night cycle is rendering, and `DayNightCycleData` sets none of its timing.**
No field on that `ScriptableObject` is a time — they are sun-elevation angles, exposure and contrast limits, colours, LUT settings and light multipliers: `DawnStartAngle` and `DuskEndAngle` say which look to use once the sun is at an angle.
Source: `src/Game/DayNightCycleData.cs`, `src/Game/Game.Rendering/LightingSystem.cs`.

## Seasons

Source: `src/Game/Game.Prefabs.Climate/ClimatePrefab.cs` (`EnsureSeasonsOrder`, `FindSeasonByTime`, `CountElapsedSeasons`, `Intersect`), `src/Game/Game.Simulation/ClimateSystem.cs` (`UpdateSeason`, `SampleClimate`).

```
ClimateSystem.SeasonInfo: [Serializable] class { SeasonPrefab m_Prefab; [Range(0,1)] float m_StartTime; eleven climate fields }
ClimatePrefab.m_Seasons: SeasonInfo[], any length
m_SeasonsOrder: indices sorted by m_StartTime, built lazily

FindSeasonByTime(t):
    no seasons     → (null, 0, 1)
    one season     → (m_Seasons[0], 0, 1)
    for each ordered i: start = this.m_StartTime; end = next.m_StartTime (+1 when it wraps below start)
        if t in [start, end): return (season, start, end)
        if end > 1 and t < end - 1: return (season, start, end)
    fallback        → (m_Seasons[0], 0, 1)

CountElapsedSeasons(startTime, elapsedTime): 0 with no seasons, 1 with one, else the number of ordered (start, next start) spans [startTime, startTime + elapsedTime) intersects, wrapping
    // AchievementTriggerSystem's "a full year has passed"

UpdateSeason(prefab, normalizedDate):
    (m_CurrentSeason, start, end) = prefab.FindSeasonByTime(normalizedDate)
    only when the season object changed: recompute seasonTemperature, seasonPrecipitation, seasonCloudiness over [start, end)
SampleClimate(prefab, t): evaluates the prefab's curves at t * daysPerYear
```

`m_StartTime` is a fraction of the year in `[0, 1)`, so a season's boundary in frames is `m_StartTime * 262144 * m_DaysPerYear`.

(VOLATILE: every system, component, field, property, enum, method, constant and `Source:` path this file names — their declarations under `src/Game/` in the root `Game` namespace, `Game.Simulation`, `Game.Effects`, `Game.Prefabs`, `Game.Prefabs.Climate`, `Game.Rendering`, `Game.Net`, `Game.UI.InGame`, `Game.Achievements`, `Game.Settings` and the assembly's global namespace, at the files the sections cite.)
