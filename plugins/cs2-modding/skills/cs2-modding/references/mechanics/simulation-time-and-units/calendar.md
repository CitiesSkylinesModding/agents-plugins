# The calendar

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Every date the game shows is `TimeSystem` arithmetic over three inputs: `SimulationSystem.frameIndex`, the `TimeData` singleton, and `TimeSettingsData.m_DaysPerYear`.
`TimeSystem` (`src/Game/Game.Simulation/TimeSystem.cs`) declares no `GetUpdateInterval`, so `UpdateTime` recomputes `normalizedTime`, `normalizedDate`, `year` and `daysPerYear` every simulation frame, and `PostDeserialize` runs it once more so the clock is valid before the first frame of a load.

## The expressions

Source: `src/Game/Game.Simulation/TimeSystem.cs`, `src/Game/Game.Common/TimeData.cs`; `D = settings.m_DaysPerYear`.

```
TimeData.TimeOffset          = m_StartingHour / 24 + m_StartingMinutes / 1440 + 1e-05   // the setter inverts it
TimeData.GetDateOffset(D)    = m_StartingMonth / D

GetTicks(frame)              = (int)(frame - data.m_FirstFrame)                       // protected, as are GetTimeOfDay and GetTimeOfYear below; the rest are public
                             + round(TimeOffset * 262144)
                             + round(GetDateOffset(D) * 262144 * D)       // = m_StartingMonth * 262144
GetTimeOfDay                 = (GetTicks % 262144) / 262144
GetTimeOfYear                = (GetTicks % (262144 * D)) / (262144 * D)
GetYear                      = data.m_StartingYear + GetTicks / (262144 * D)          // integer division
GetElapsedYears              = (frameIndex - data.m_FirstFrame) / (262144 * D)
GetStartingDate              = (GetTicks(data.m_FirstFrame) % (262144 * D)) / (262144 * D)
GetDay(frame, data)          = floor((frame - data.m_FirstFrame) / 262144 + TimeOffset)  // static; days since founding

the rendering overloads take a double renderingFrame in place of frameIndex - m_FirstFrame, and are public:
GetTimeWithOffset(rf)        = rf + TimeOffset * 262144 + GetDateOffset(D) * 262144 * D   // protected
GetTimeOfDay(rf)             = GetTimeWithOffset(rf) % 262144 / 262144
GetTimeOfYear(rf)            = GetTimeWithOffset(rf % (262144 * D)) / (262144 * D)
GetYear(rf)                  = data.m_StartingYear + floor(GetTimeWithOffset(rf) / (262144 * D))

GetDateTime / GetCurrentDateTime:
    hour   = floor(24 * timeOfDay)
    minute = floor(60 * (24 * timeOfDay - hour))
    day    = 1 + floor(D * timeOfYear) % D                                   // DateTime's *day* slot holds the game's month
    CreateDateTime(year, day, hour, minute, second) builds from DateTime(0, Utc) and adds one hour when the result IsDaylightSavingTime()

DebugAdvanceTime(minutes):   data.m_FirstFrame -= (uint)(minutes * 262144) / 1440
```

`daysPerYear` on `TimeSystem` reads the singleton lazily and substitutes `1` for a zero.

**`GetDay` is a day number since founding, not a day of the year.**
It never resets; `AgingSystem` and the citizen life-cycle read it that way.
Source: `src/Game/Game.Simulation/TimeSystem.cs`, `src/Game/Game.Simulation/AgingSystem.cs`.

## A day is a month, and months are zero-based

`TimeSystem` has no month concept: every "month" on any surface is that day-of-year index.
The game's own code compiles a twelve-day year in where no data replaces it: `CalendarEventMonths` is a twelve-member power-of-two enum indexed by `1 << floor(normalizedDate * 12f)` (`src/Game/Game.Prefabs/CalendarEventMonths.cs`, `src/Game/Game.Simulation/CalendarEventLaunchSystem.cs`); `ClimateSystem.CalculateMeanTemperatureEkholmModen` opens with `Assert.AreEqual(12, m_TimeSystem.daysPerYear)` over an `int[12, 5]` lookup, and `CalculateTemperatureAverage` branches on `daysPerYear == 12` to reach it, falling to a plain mean otherwise (`src/Game/Game.Simulation/ClimateSystem.cs`); `TimeUISystem` falls back to `12` on an empty query.
The operative value is `TimeSettingsData.m_DaysPerYear`, a prefab field a mod may change; what changes it pays is the degraded temperature estimator and a `SampleClimate` that evaluates the prefab's curves at `t * daysPerYear` against an axis fixed at authoring.

**Months are zero-based on the wire and one-based in `DateTime`.**
`MenuUISystem` builds its save-info date with `currentDateTime.DayOfYear - 1`, and the frontend's date function returns `month: mod(dayIndex, daysPerYear)`; `CalendarEventMonths` spells its ninth member `Septermber`, which a mod naming it reproduces.
Source: `src/Game/Game.UI.Menu/MenuUISystem.cs`, `src/Game/Game.Prefabs/CalendarEventMonths.cs`, the shipped UI bundle's `time-bindings.ts`.

## The `time` binding group

`TimeUISystem.OnCreate` (`src/Game/Game.UI.InGame/TimeUISystem.cs`) registers, all in group `"time"`: the values `timeSettings` (`{ ticksPerDay, daysPerYear, epochTicks, epochYear }`, written with `__Type` `Game.UI.InGame.TimeUISystem+TimeSettings`), `ticks`, `day`, `lightingState`, `simulationPaused`, `simulationSpeed`; the event `simulationPausedBarrier`; the triggers `setSimulationPaused` and `setSimulationSpeed`.

Source: `src/Game/Game.UI.InGame/TimeUISystem.cs`, `src/Game/Game.Rendering/LightingSystem.cs`, the shipped UI bundle's `game-ui/game/data-binding/time-bindings.ts`.

```
GetTimeSettings.epochTicks   = round(TimeOffset * 262144) + round(GetDateOffset(D) * 262144 * D)   // TimeSystem.GetTicks's two offset terms
GetTicks                     = floor(floor((frameIndex - m_FirstFrame) / 182.04445f) * 182.04445f)  // whole in-game minutes
GetDay                       = TimeSystem.GetDay(frameIndex, TimeData)
GetLightingState             = LightingSystem.state, or when Invalid:
                               normalizedTime in [7/24, 0.875] ? Day : Night

frontend, s = timeSettings, mod(a, b) = ((a % b) + b) % b:
date(s, ticks):      n = s.epochTicks + ticks; d = floor(n / s.ticksPerDay)
                     { year: s.epochYear + floor(d / s.daysPerYear), month: mod(d, s.daysPerYear) }
dateFromDays(s, d) = date(s, d * s.ticksPerDay)
dateTime(s, ticks):  as date, plus o = mod(n, s.ticksPerDay)
                     hour = trunc(o / s.ticksPerDay * 24), minute = trunc(mod(o / s.ticksPerDay * 1440, 60))
minuteOfDay(s, ticks) = 60 * hour + minute, over the same o
LightingState enum: Dawn 0, Sunrise 1, Day 2, Sunset 3, Dusk 4, Night 5   // C# adds Invalid 6, which never crosses the wire
```

`182.04445f` is `262144 / 1440` as a literal unrelated to `kTicksPerDay` in the source; a mod reproducing the quantization uses the same literal, or its minute boundaries drift from the vanilla clock's.

**A duration crossing to the frontend for display is a day count.**
`LocalizedDuration` takes `value` in days; its thresholds belong to [`units-and-formatting`](../../technique/units-and-formatting/units-and-formatting.md).
Source: the shipped UI bundle's `game-ui/common/localization/localized-duration.tsx`.

**The `simulationTime` chart scale defaults `ticksPerDay` to `1 << 17`, half a day.**
Its `defaults.timeSettings` is `{ daysPerYear: 12, ticksPerDay: 1 << 17, epochTicks: 0, epochYear: 0 }`; the live binding always supplies real settings, so a mod reusing the scale passes `timeSettings` or gets an axis whose day is half the game's, while its tick intervals (`ticksPerDay / 24`, `/ 8`, `/ 4`, `/ 2`, `ticksPerDay`, `3 * ticksPerDay`, `daysPerYear * ticksPerDay`) are derived from whatever it is given.
Source: the shipped UI bundle's `simulationTime` chart scale.

(VOLATILE: every system, component, field, property, enum, method, constant, binding name and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Common`, `Game.Prefabs`, `Game.Rendering`, `Game.UI.InGame` and `Game.UI.Menu`, at the files the sections cite; and the frontend functions, enum, scale defaults and module paths — the shipped UI bundle's `time-bindings.ts`, `localized-duration.tsx` and chart scale registrations.)
