# A city diary that logs once per in-game month

## Kind

bug report

## Prompt

I'm working on a Cities: Skylines II mod that keeps a city diary: once per in-game month it logs one line giving the current month and year, the year the city was founded, how many days the city has existed, and the current season. The code is in this workspace.

It isn't behaving. I get exactly one diary line when a city loads and then nothing more, however long I let it run. That one line says January and winter every time, whatever the date in the game actually is. And when I quit the game, start it again and load my save, the founding year in the line reads 0.

## Checklist

### The month is read from the DateTime's day slot

- Falls in: the code reads `.Month` off the `DateTime` that `TimeSystem.GetCurrentDateTime()` or `GetDateTime(...)` returns, or formats it with a month specifier (`"MMMM"`, `"MMM"`, `"M"`), whether to name the month or to detect that it changed.
- Avoids: the month is that `DateTime`'s `.Day` or `.DayOfYear` (zero-based once `- 1` is applied), or `floor(normalizedDate * daysPerYear)`, or the month change is detected as a change of `TimeSystem.GetDay(...)`.
- Reference: cs2-modding/references/mechanics/simulation-time-and-units/calendar.md
- Source: src/Game/Game.UI.Menu/MenuUISystem.cs:869

### The city's age in days is counted from the founding frame

- Falls in: the day count is `frameIndex / 262144`, `frameIndex / TimeSystem.kTicksPerDay` or any expression over `SimulationSystem.frameIndex` alone, with `TimeData.m_FirstFrame` never subtracted.
- Avoids: the day count is `TimeSystem.GetDay(frameIndex, timeData)`, or `(frameIndex - timeData.m_FirstFrame) / 262144` with the `TimeData` singleton read from a query.
- Reference: cs2-modding/references/mechanics/simulation-time-and-units/simulation-time-and-units.md
- Source: src/Game/Game.Simulation/TimeSystem.cs:152

### The founding year is the TimeData singleton's

- Falls in: the founding year is `TimeSystem.startingYear`, read directly or through a difference such as `year - startingYear`.
- Avoids: the founding year is `TimeData.m_StartingYear` from the singleton, or the age in years is `TimeSystem.GetElapsedYears(settings, timeData)`.
- Reference: cs2-modding/references/mechanics/simulation-time-and-units/simulation-time-and-units.md
- Source: src/Game/Game.Simulation/TimeSystem.cs:83

### The season comes from the climate prefab

- Falls in: the season is derived from the month or the date by a mapping the mod wrote itself, such as a `switch` or range test sending December to February to winter, or any quarter-of-the-year arithmetic over the month index or `normalizedDate`.
- Avoids: the season is `ClimateSystem.currentSeasonName` or `currentSeason`, or the result of `ClimatePrefab.FindSeasonByTime(normalizedDate)` on the prefab behind `ClimateSystem.currentClimate`.
- Reference: cs2-modding/references/mechanics/simulation-time-and-units/day-night-and-seasons.md
- Source: src/Game/Game.Simulation/ClimateSystem.cs:659

## Roots

- decompile
