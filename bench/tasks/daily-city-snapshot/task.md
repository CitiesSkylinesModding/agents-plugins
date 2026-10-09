# A daily city snapshot appended to a CSV file

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod. Once per in-game day I want it to append one line to a CSV file with a snapshot of my city: the day number, the population, the number of households, the number of workers and of unemployed citizens, how many citizens are currently students, and how many households are living in a building that has no electricity at that moment. The figures the game's statistics panel already tracks should match what the panel shows, so take the snapshot right after the game has refreshed its statistics rather than at a random point in the day. When the file is first created, start it with a header line naming my mod and the version of the game that produced it, so I can tell apart files written before and after a game update.

## Checklist

### The game version is read from `Game.Version.current`

- Falls in: the header's game version comes from assembly metadata or from a library's own version: `Assembly.GetName().Version` on a game type's assembly (the `AssemblyVersion` is `0.0.0.0`), `Colossal.Version.GetCurrent()` with no argument (which reads the mod's own assembly), `Colossal.Core.Version.current` (the core library's version), or a version string copied out of the decompile, such as `1.5.7f1`.
- Avoids: `Game.Version.current.fullVersion`, `.shortVersion` or another member of `Game.Version.current`, or `Colossal.Version.GetCurrent(assembly)` handed the `Game` assembly.
- Reference: cs2-modding/references/technique/navigating-the-decompile/navigating-the-decompile.md
- Source: src/Game/Game.SceneFlow/GameManager.cs:2104

### The order after the statistics refresh is a registration call, not an attribute

- Falls in: the system carries `[UpdateAfter(typeof(CityStatisticsSystem))]`, `[UpdateBefore(...)]` or `[UpdateInGroup(...)]` as its ordering and is registered with a plain `updateSystem.UpdateAt<...>(phase)`.
- Avoids: `updateSystem.UpdateAfter<TheSystem, CityStatisticsSystem>(SystemUpdatePhase.GameSimulation)`, or a subscription to `CityStatisticsSystem.eventStatisticsUpdated`, with no stock ECS ordering attribute on the system.
- Reference: cs2-modding/references/technique/navigating-the-decompile/navigating-the-decompile.md
- Source: src/Game/Game/UpdateSystem.cs:34

### The student count queries the citizen-side `Student`

- Falls in: the query counts entities carrying `Game.Buildings.Student`, a buffer element on school buildings, so the figure is the number of schools; or a bare `Student` that resolves to it.
- Avoids: `Game.Citizens.Student`, the component on each enrolled citizen, in the query (qualified where `Game.Buildings` is also imported), or the lengths of the schools' `Game.Buildings.Student` buffers summed.
- Reference: cs2-modding/references/technique/navigating-the-decompile/navigating-the-decompile.md
- Source: src/Game/Game.Buildings/Student.cs:7

## Roots

- decompile
