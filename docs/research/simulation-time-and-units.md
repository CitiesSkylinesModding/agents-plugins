# Simulation time and units

**Baseline.** Game version 1.6.0f1, established against the decompiled C# under `DecompiledCitiesSkylines2/src/`, against the installed game's UI bundle read through the reformatted copy at `DecompiledCitiesSkylines2/src-ui/source.js` (prettier at its defaults, **135,021 lines** — check your copy's count before trusting a line number from it), and against the user's running city — a debug-patched development build, read live over the sibling `unity-devtools` plugin and the `coherent-gameface` plugin — on 2026-08-22. Wiki pages fetched 2026-08-22. The mod corpus at `cs2-third-party-mods` was read on 2026-08-22 (22 repositories).

**The live save is a throwaway continued city, not a fresh one.** `SimulationSystem.frameIndex = 5,632,217` against `TimeData.m_FirstFrame = 5,600,069`, so the city is 32,148 frames — about 2 h 56 min of in-game clock — old inside a process whose frame counter had already reached 5.6 million. That gap is itself a finding (below); nothing else here depends on the save's age, because everything live read is either a singleton the game rebuilds each load or a prefab component.

**Ruled (2026-08-06, the citizens-and-households pass; conflicts.md), governing every finding in this file.** A shipped reference states **no prefab value**. It names the component and the field, with the access shape, and that is the whole of what it says about the magnitude. Three things survive untouched: **C# constants ship, as numbers** — a `const` or `static readonly` the consuming code compiled in is offline-checkable and citable to a line; **formulas ship whole** — the expression a system evaluates, its baseline and its step functions are invariant structure rather than balance; and **the component-to-number-family map ships whole**, with the access shape beside each component, because an agent cannot perform the read without it. Two consequences bind: a derived ratio is a magnitude wearing a mechanism's clothes, and an adverb carrying the same magnitude ("far longer", "roughly twice") counts as the number.
**This topic sits unusually well under that ruling and the split runs through the middle of the clock.** The tick count per day is `public const int TimeSystem.kTicksPerDay = 262144` — a `const` the consuming code compiles in, at 271 sites across the assembly — so it ships as the number. The days-per-year is `TimeSettingsPrefab.m_DaysPerYear`, a Unity-serialized field copied into `TimeSettingsData` — so **twelve does not ship as a prefab value**, even though it is what the shipped asset carries. What rescues it is that consuming C# compiles a twelve in independently, in four places that no data can replace (the `CalendarEventMonths` enum's twelve members, `ClimateSystem`'s `Assert.AreEqual(12, …)` and its `int[12,5]` lookup table, `TimeUISystem`'s query-empty fallback), and those are constants. So the reference states **"the game's own code compiles a twelve-day year in as an assertion, an enum and a table dimension, while the operative value is `TimeSettingsData.m_DaysPerYear` and a mod may change it"** — never "`m_DaysPerYear` is 12".

**Ruled (2026-08-08, the zoning-buildings-and-land-value pass; conflicts.md), governing this file's prefab classes.** A field initializer on a `ComponentBase` / `ScriptableObject`-derived prefab class is a Unity-serialized default the shipped asset overrides. It ships as **the field, never as the figure**, and a reference whose map or traps send a reader into a file carrying them states once, as a trap, that these are Unity-serialized defaults the shipped asset overrides, with nothing in the C# marking which survived. The test is what consumes the value, not where it is written.
Four classes in this topic carry the shape and one of them is the day-length itself.
`TimeSettingsPrefab.m_DaysPerYear = 12` (`src/Game/Game.Prefabs/TimeSettingsPrefab.cs:10`), copied into `TimeSettingsData` by `LateInitialize` (`:23-30`). `ClimatePrefab` initializes `m_Latitude = 61.49772f`, `m_Longitude = 23.767042f`, `m_MaxSunElevationAngle = 90.0`, `m_SunElevationClampStart = 45.0` (`src/Game/Game.Prefabs.Climate/ClimatePrefab.cs:612/615/619/623`) — two of which the live map overrides (read live: latitude 35.20208, longitude −101.887344, sun limit (π/4, π/2), so **two of the four survive and two do not**, on one asset). `ClimateSystem.SeasonInfo` initializes `m_TempNightDay = new float2(5f, 20f)` and ten siblings (`src/Game/Game.Simulation/ClimateSystem.cs:29-82`), all `[Serializable]` on a per-season authored object. `DayNightCycleData : ScriptableObject` initializes thirty-odd rendering fields (`src/Game/DayNightCycleData.cs`).
**The live diff on `ClimatePrefab` is the trap's own worked example** and belongs in the reference's trap sentence: a reader who opens the file, sees `m_Latitude = 61.49772f` (Tampere) and writes it down has a number the map replaced with a different continent's.

**Ruled (2026-08-06, the citizens-and-households pass; conflicts.md), governing this file's use of the wiki.** No mechanics reference borrows a wiki stat table's numbers at all — first-party or nothing. It does not bite hard here: the wiki has no stat table in this area. What it does have is four constants (below) that all reconcile exactly with the decompile, so they are corroboration rather than source.

---

## Findings

### The tick is the simulation frame, and `SimulationSystem.frameIndex` is the only clock

`SimulationSystem` (`src/Game/Game.Simulation/SimulationSystem.cs:18`, `GameSystemBase`, `IDefaultSerializable`, `ISerializable`) owns `public uint frameIndex { get; private set; }` (`:68`) and is the only writer. Every notion of elapsed time in this game is a difference of two `frameIndex` values.

Four public read-only properties come off the same system and they are not interchangeable (`:68-113`):

| Property | Type | What it is |
| --- | --- | --- |
| `frameIndex` | `uint` | the simulation tick counter, integral, advanced only inside the step loop |
| `frameTime` | `float` | the sub-frame remainder, `m_Timer * 60f` (`:270`) — the fractional part between two ticks |
| `smoothSpeed` | `float` | a rendered-frame-smoothed estimate of achieved speed, for the UI and for interpolation |
| `frameDuration` | `float` | real seconds one simulation step cost last time, `stopwatchTicks / (Frequency * stepCount)` (`:194`) |

`selectedSpeed` is a settable `float` with one guard: a write during loading is dropped (`:72-85`). `performancePreference` is `{ FrameRate, Balanced, SimulationSpeed }` (`:20-25`), taken from `SharedSettings.instance.general.performancePreference` at `OnCreate` (`:120`).

`Rots:` every system, component, field and binding name in this file. Re-check against `src/Game/Game.Simulation/`, `src/Game/Game.Common/`, `src/Game/Game.Prefabs/`, `src/Game/Game.Prefabs.Climate/`, `src/Game/Game.UI.InGame/`, `src/Game/Game.Effects/`, `src/Game/Game/UpdateSystem.cs` and `src/Game/Game.Common/SystemOrder.cs`, at the files cited beside each claim. The frontend cites re-resolve only against a copy whose line count matches the baseline.

### `kTicksPerDay = 262144` is a `const`, and the code almost never spells it

`public const int kTicksPerDay = 262144` sits on `TimeSystem` (`src/Game/Game.Simulation/TimeSystem.cs:18`). It is 2^18, which is what makes every derived interval a power of two and so legal as an update interval.

**The symbol has almost no users; the literal has 271.** `TimeSystem`'s own methods write the bare `262144` (`:91/96/101/106/111/116/122/128/134/140/147/152/159`), and so does everything else. Grepping `src/` for `262144` returns 271 hits, of which the ones outside `Game.Simulation` are the interesting half because they show the day is the unit for *durations*, not only for cadences:

- `src/Game/Game.Events/InitializeSystem.cs:720-721` — `m_StartFrame = frameIndex + (uint)(262144f * m_PreparationDuration)`, `m_EndFrame = m_StartFrame + (uint)(262144f * m_ActiveDuration)`. Event durations on prefab data are **in days**.
- `src/Game/Game.Events/InitializeSystem.cs:827` — `m_EndFrame = m_StartFrame + (uint)(m_Duration * 262144 / 4)`, a quarter-day unit for one event family.
- `src/Game/Game.Net/NetUtils.cs:656/660/664` and `src/Game/Game.Objects/ObjectUtils.cs:361/365/369` — `recent.m_ModificationFrame + 262144f * economyParameterData.m_RoadRefundTimeRange.x` in `NetUtils` and `m_BuildRefundTimeRange.x` in `ObjectUtils` (and `.y`, `.z`). Both refund windows on `EconomyParameterData` are **in days**.
- `src/Game/Game.Prefabs/PoliceCar.cs:69` — `uint shiftDuration = (uint)(m_ShiftDuration * 262144f)` baked into `PoliceCarData`. A shift duration on the class is in days and on the component is in frames.
- `src/Game/Game.Tools/RecentClearSystem.cs:35` — `uint num = m_SimulationFrame - 262144`, i.e. the `Recent` tag lives exactly one day.
- `src/Game/Game.Triggers/CreateChirpSystem.cs:179` — `m_InactiveFrame = (uint)((float)m_SimulationFrame + num * 262144f)`.
- `src/Game/Game.UI.InGame/TimeUISystem.cs:150/152` — the frontend's `ticksPerDay` is this literal, not the symbol.

**So the rule an agent needs is: a duration authored on a prefab is in days and is multiplied by 262144 at the point of use, while a duration stored on an instance component is already in frames.** Getting that backwards is silent — both are numbers, and 262144× is not an error any type system catches.

`Rots:` the 271 count and the site list. Re-derive with `grep -rn 262144 src/`.

### The step loop: what advances the frame index, and by how much

`SimulationSystem.OnUpdate` (`:189-296`) is called once per rendered frame. **The system that drives the whole simulation is itself registered at `SystemUpdatePhase.LateUpdate`** (`updateSystem.UpdateAt<SimulationSystem>(SystemUpdatePhase.LateUpdate)`, `src/Game/Game.Common/SystemOrder.cs:74`) — so the three simulation phases are nested inside a late-update pass, not siblings of it. Transcribed:

```
if selectedSpeed == 0:  num = 0; smoothSpeed = 0
else:
  num2  = Time.deltaTime * selectedSpeed
  num3  = 1
  if pathfindResultSystem.pendingSimulationFrame < uint.MaxValue:            # backpressure
      slack = max(0, pendingSimulationFrame - frameIndex - 1)
      num3  = min(1, slack * PENDING_FRAMES_SPEED_FACTOR)                    # 1/48, :38
      num2 *= num3
  m_Timer += num2
  num  = floor(m_Timer * 60)                                                 # :238
  num2 *= 60
  if backpressure active: num = min(num, slack); num2 = min(num2, slack)
  if performancePreference != SimulationSpeed:                               # :246-253
      f    = (endFrameBarrier.lastElapsedTime - currentElapsedTime) / max(0.001, frameDuration)
      cap  = max(1, FrameRate ? floor(f) : ceil(f))
      num  = min(num, cap); num2 = min(num2, cap)
  m_Timer = clamp(m_Timer - num / 60, 0, 1/60)                               # :254
  steps   = max(1, min(8, round(selectedSpeed * num3 * 2)))                  # :255
  num     = clamp(num, 0, steps)                                            # :256
frameTime = m_Timer * 60
UpdateSystem.Update(PreSimulation)                                           # :272, always, even at 0 steps
for i in 0..num-1:                                                           # :277-288
    frameIndex++
    if actionMode.IsEditor(): UpdateSystem.Update(EditorSimulation, frameIndex, i)
    if actionMode.IsGame():   UpdateSystem.Update(GameSimulation,   frameIndex, i)
UpdateSystem.Update(PostSimulation)                                          # :295
```

Five things fall out:

1. **The nominal rate is 60 simulation frames per real second at speed 1.** That is the `* 60f` at `:238` against a timer accumulating `deltaTime * selectedSpeed`, and it is the constant every `/ 60` frames-to-seconds conversion in the mechanics references resolves through.
2. **A rendered frame carries at most eight simulation steps and at most `max(1, round(selectedSpeed * 2))`.** At 1× the ceiling is 2, at 2× it is 4, at 4× it is 8, and the `min(8, …)` means the ceiling saturates there. So the highest a mod can drive the clock by writing `selectedSpeed` is 8 steps per rendered frame regardless of the value written.
3. **Pausing is exactly `selectedSpeed == 0`, and it freezes `frameIndex` bit-for-bit** — no steps run, `smoothSpeed` goes to 0, and `PreSimulation`/`PostSimulation` still fire. Verified live: held at speed 0 across 4 real seconds, `frameIndex` read 5,634,509 before and 5,634,509 after.
4. **Two independent throttles sit above the nominal rate**, and both make "frames per real second" a machine fact rather than a game fact. The pathfinder's `pendingSimulationFrame` backpressure (`:231-245`) also feeds the step-count clamp through `num3`, so a pathfinding backlog slows the clock and lowers the per-frame ceiling at the same time. And unless `performancePreference == SimulationSpeed`, the frame-budget clamp at `:246-253` caps steps by measured headroom.
5. **`PreSimulation` and `PostSimulation` run once per *rendered* frame, not once per simulation frame**, and they run even at zero steps. `PreSimulation` has zero vanilla registrations (`mod-lifecycle-and-ordering.md`), so a mod registering there gets a render-rate hook that keeps firing while paused — which is either exactly what it wants or a bug it will not notice.

**The whole loop runs a second time during loading, on a different phase.** `OnGamePreload` sets `m_LoadingCount = (purpose == Purpose.NewGame) ? 1024 : 0` (`:160`) and `UpdateLoadingProgress` (`:164-186`) then runs eight `LoadSimulation` steps per rendered frame, incrementing `frameIndex` on each, until the count drains. **So a new game burns 1,024 simulation frames — a 256th of a day — before the player sees anything, and a loaded save burns none.** That is where the wiki's "`LoadSimulation` executes 8 times in a row" comes from.

### Live: the frame rate at each speed, and why the number is not a constant

Read over the sibling Unity plugin on 2026-08-22, on the user's throwaway city, `performancePreference = Balanced`, starting at `frameIndex = 5,632,217`. Each window is a held `suspend` released for 6 real seconds with `selectedSpeed` set in the before-snippet:

| `selectedSpeed` | frames in 6 s | frames/s | `smoothSpeed` | render fps |
| --- | --- | --- | --- | --- |
| 1 | 285 | 47.5 | 0.975 | 47.8 |
| 2 | 599 | 99.8 | 1.72 | 45.8 |
| 4 | 1220 | 203 | 3.25 | 30.9 |
| 0 | 0 | 0 | 0 | — |

The ratios are 1 : 2.10 : 4.28, so the clock scales linearly with `selectedSpeed` exactly as the formula says. The absolute rate is below the nominal 60/s because the `Balanced` frame-budget clamp was binding and because the measurement window includes suspend/resume overhead.
**The durable claim is the nominal one — 60 frames per real second per unit of `selectedSpeed`, so 262144 / 60 ≈ 4,369 real seconds ≈ 72.8 real minutes for one in-game day at 1×.** The observed one is machine-specific and must not ship as a figure. The wiki reaches the same conclusion from the other end and is the only source that states it in prose (see the wiki finding).

`Unconfirmed:` whether `performancePreference == SimulationSpeed` actually reaches the nominal 60/s on this machine. The branch that would be skipped is `:246-253`; the experiment is the same six-second window with `simulationSystem.performancePreference = PerformancePreference.SimulationSpeed` in the before-snippet. Not run, because it changes a persisted user setting.

### There are three selectable speeds and they are 1×, 2× and 4×, not 1/2/3

`TimeUISystem` publishes `simulationSpeed` as an **index**, not a multiplier, and converts both ways with a power of two (`src/Game/Game.UI.InGame/TimeUISystem.cs:228-236`):

```
private static float IndexToSpeed(int index) => Mathf.Pow(2f, Mathf.Clamp(index, 0, 2));
private static int   SpeedToIndex(float speed) => speed > 0f ? Mathf.Clamp((int)Mathf.Log(speed, 2f), 0, 2) : 0;
```

So the three buttons are indices 0, 1, 2 and the speeds are **1, 2, 4**. Read live off the `time.simulationSpeed` binding: `2` while paused, because `GetSimulationSpeed` reports `SpeedToIndex(m_SpeedBeforePause)` when paused (`:188-191`) — **the binding never reads 0 for "paused"; `time.simulationPaused` is the separate boolean that does.**

**Verdict.** The wiki states three speeds "1x, 2x, and 3x" (https://cs2.paradoxwikis.com/Applying_Animation_Curves_to_Multiple_Emissive_Light_Sources, `§ Duration`). The decompile shows the third is 4×. `docs/SOURCES.md` makes the decompile ground truth for anything C# names; the wiki page is naming the three buttons by ordinal and got the multiplier wrong. **The decompile wins: 1, 2, 4.**

The developer menu offers eight instead, `static readonly float[] { 0, 0.125, 0.25, 0.5, 1, 2, 4, 8 }` against the labels `0x, 1/8x, 1/4x, 1/2x, 1x, 2x, 4x, 8x` (`src/Game/Game.Debug/DebugSystem.cs:224-236`, wired at `:1265-1268`). Both arrays are `static readonly` and ship as numbers. The eight-step-per-frame clamp is already saturated from 4× up, so the debug menu's 8× buys no more steps a frame than 4× does.

Two more clock controls reach `selectedSpeed` from outside the UI: photo mode's `"Simulation Speed"` property, a 0–8 slider over `selectedSpeed` whose reset writes 0 (`src/Game/Game.Rendering/PhotoModeRenderSystem.cs:767-778`), and the climate-render debug path (`src/Game/Game.Rendering/ClimateRenderSystem.cs:448`), which writes 0.

### The pause a mod cannot lift: the paused barrier

`TimeUISystem` holds `EventBinding<bool> m_SimulationPausedBarrierBinding` (`:65`, registered at `:93`) on `time.simulationPausedBarrier`, and `pausedBarrierActive => m_SimulationPausedBarrierBinding.observerCount > 0` (`:73`). While any frontend observer is subscribed to that event — or while the platform reports the app unfocused — `OnUpdate` forces `selectedSpeed = 0` every UI update and sets `m_UnpausedBeforeForcedPause`, restoring `m_SpeedBeforePause` when the barrier lifts (`:119-142`). `SetSimulationSpeed` checks the barrier (`:219`) and writes `m_SpeedBeforePause = IndexToSpeed(speedIndex)` instead of the live speed when it is up (`:224`); `SetSimulationPaused` under the barrier only records `m_UnpausedBeforeForcedPause = !paused` (`:205-215`) and writes nothing else. `OnUpdate` opens by copying any positive `selectedSpeed` into `m_SpeedBeforePause` (`:121-124`) before it forces zero.
**So a mod that writes `SimulationSystem.selectedSpeed` while a modal UI holds the barrier has its write captured into `m_SpeedBeforePause` and zeroed on the next UI update, then restored when the barrier lifts, with nothing logged** — the write lands when the barrier drops, not when it was made. `time.setSimulationSpeed` routes into the same private field. Not exercised by any corpus mod; `Time2Work` reimplements the whole barrier rather than working around it (`Time2Work/NightShift/Systems/Time2WorkTimeUISystem.cs:131-150`).

### `frameIndex` is not zero at city founding, and city age is `frameIndex − TimeData.m_FirstFrame`

`SimulationSystem` serializes exactly one value, `frameIndex` (`:135-146`), and `SetDefaults` puts it back to 0 (`:148-153`). `SetDefaults` is called only for systems the loaded stream did not carry (`src/Colossal.Core/Colossal.Serialization.Entities/EntityDeserializer.cs:614-618`), so whether it fires on a given load is a property of the file, not of the purpose.

The epoch that makes the calendar work is elsewhere. `TimeSystem.PostDeserialize` (`src/Game/Game.Simulation/TimeSystem.cs:69-87`) creates the `TimeData` singleton if the save has none, and **on `Purpose.NewGame` only** writes `singleton.m_FirstFrame = m_SimulationSystem.frameIndex` and `singleton.m_StartingYear = startingYear`. So the epoch is "whatever the frame counter read when this city was created", and every date calculation subtracts it.

Read live at 1.6.0f1: `m_FirstFrame = 5,600,069` against `frameIndex = 5,632,217`. **The epoch is 5.6 million, not zero**, and the city is the 32,148-frame difference old. Any code treating `frameIndex` as time-since-founding is wrong by whatever the counter held at creation.

`Unconfirmed:` *why* the counter was already at 5.6 M. Two candidates — the main menu's background city driving `SimulationSystem`, or a previous city in the same process — and neither is settled. The experiment that settles it: read `SimulationSystem.frameIndex` at the main menu before loading anything, then start a new game and read `TimeData.m_FirstFrame`. Not run, because starting a new game is an act on the user's running game.

One consumer visibly assumes zero: `RecentClearSystem.OnUpdate` guards its whole job with `if (m_SimulationSystem.frameIndex >= 262144)` (`src/Game/Game.Tools/RecentClearSystem.cs:93`), an underflow guard for `m_SimulationFrame - 262144` at `:35`. With a nonzero epoch the guard is always satisfied, so it is inert rather than wrong — but it is first-party evidence that the counter was designed to start at zero.

`uint` gives 4.29 × 10^9 frames = 16,384 in-game days before the counter wraps, but `TimeSystem.GetTicks` casts the difference to `int` (`:91/96`), so the calendar goes wrong after 2^31 frames since founding — 8,192 days, 682 in-game years. Not a practical limit; it is the reason a mod must not widen the epoch arbitrarily.

### `TimeData` and `TimeSettingsData`: two singletons, two entities, two read shapes

**`Game.Common.TimeData`** (`src/Game/Game.Common/TimeData.cs:7`) is an `IComponentData` on a plain entity — not a prefab — carrying five serialized fields (`:17-25`):

| Field | Type | Meaning |
| --- | --- | --- |
| `m_FirstFrame` | `uint` | the founding frame, the epoch every date subtracts |
| `m_StartingYear` | `int` | the calendar year the city starts in |
| `m_StartingMonth` | `byte` | the starting day-of-year index, zero-based |
| `m_StartingHour` | `byte` | the starting hour |
| `m_StartingMinutes` | `byte` | the starting minute |

Two derived properties turn three of them into fractions (`:27-43`): `TimeOffset => hour/24 + minutes/1440 + 1e-05f` (the `1e-05` is a rounding nudge, and the setter inverts it), and `GetDateOffset(daysPerYear) => m_StartingMonth / daysPerYear`. Its `SetDefaults` writes 2021 / month 5 / 07:00 (`:45-52`), matching the four `public const` defaults at `:9-15` — `kDefaultStartingYear = 2021`, `kDefaultStartingMonth = 5`, `kDefaultStartingHour = 7`, `kDefaultStartingMinutes = 0`. Those four are `const` and ship as numbers.

**The read shape is a static helper, not `GetSingleton`.** `TimeData.GetSingleton(EntityQuery)` (`:82-91`) returns the singleton if the query is non-empty and a defaulted struct otherwise, so a caller never has to guard. `TimeUISystem` uses it in three places; `TimeSystem` uses the raw `GetSingleton<TimeData>()` and takes `RequireForUpdate` on the query instead (`:64-66`).

**`Game.Prefabs.TimeSettingsData`** (`src/Game/Game.Prefabs/TimeSettingsData.cs:5-8`) is one `int m_DaysPerYear` and it lives on **a prefab entity**, built by `TimeSettingsPrefab.LateInitialize` (`src/Game/Game.Prefabs/TimeSettingsPrefab.cs:23-30`) from the class's own serialized field. Read live, the query returns exactly one entity, index 12, which carries `PrefabData` and whose `PrefabSystem.GetPrefabName` is `TimeSettings`; `TimeData` lives on entity 65911, which carries no `PrefabData` and only two components.

**Prefab-twin check** (`docs/solutions/prefab-data-read-where-the-simulation-reads-an-instance.md`): there is **no twin**. `TimeSettingsData` exists on exactly one entity and the simulation reads that same prefab entity. What makes it safe is that the query carries no prefab exclusion — this game's prefab entities carry `PrefabData` rather than Unity's `Prefab` tag, so an ordinary `GetEntityQuery(ComponentType.ReadOnly<TimeSettingsData>())` finds it (`TimeSystem.cs:63`). `PlanetarySystem` builds the same two queries with `EntityQueryOptions.IncludeSystems` (`src/Game/Game.Simulation/PlanetarySystem.cs:545-553`), which is unrelated to prefabs and is the codegen's doing.

**Game-mode check** (`docs/solutions/retuning-a-parameter-component-the-game-mode-rewrites.md`): **no mode class touches the clock.** Grepping `src/Game/Game.Prefabs.Modes/` for `TimeSettings`, `ClimatePrefab` and `DayNight` returns zero files, so `GameModeSystem`'s `RestoreDefaultData` / `ApplyMode` pass cannot rebuild `TimeSettingsData` or the day-night data on load. That makes this topic's parameter component one of the few a mod may write once, at load, without the mode pass scaling or discarding it. It does **not** exempt the climate and economy components this topic's neighbours read: `WeatherPhenomenonMode`, `EconomyParametersMode` and `HealthcareParametersMode` all exist.

### The calendar: five expressions and one epoch

Everything the game calls a date is `TimeSystem` arithmetic over `frameIndex`, `TimeData` and `TimeSettingsData`. Transcribed from `src/Game/Game.Simulation/TimeSystem.cs:89-153`, with `D = settings.m_DaysPerYear`:

```
ticks(frame)        = (int)(frame - data.m_FirstFrame)
                    + round(data.TimeOffset * 262144)
                    + round(data.GetDateOffset(D) * 262144 * D)          # :89-97
timeOfDay           = (ticks % 262144) / 262144                          # :109-112   → [0,1)
timeOfYear          = (ticks % (262144 * D)) / (262144 * D)              # :120-124   → [0,1)
year                = data.m_StartingYear + floor(ticks / (262144 * D))  # :144-148
elapsedYears        = (frame - data.m_FirstFrame) / (262144 * D)         # :126-130
GetDay(frame, data) = floor((frame - data.m_FirstFrame) / 262144 + data.TimeOffset)   # :150-153, static
```

Note `GetDateOffset(D) * 262144 * D` collapses to `m_StartingMonth * 262144` — the starting month is simply that many whole days into the year. And **`GetDay` is a day number since founding, not a day of the year**; it goes to 1 for the first time roughly a day after the city starts and never resets. `AgingSystem` and the citizen life-cycle read it that way, and `Citizen.m_BirthDay` is one of its values.

The whole set has a second, `double`-taking overload keyed on a *rendering* frame rather than the simulation frame (`:99-107/114-118/138-142`), which is what makes the visual clock smooth (below).

`GetDateTime` and `GetCurrentDateTime` (`:175-195`) turn the fractions into a `System.DateTime`: `hour = floor(24 * timeOfDay)`, `minute = floor(60 * (24 * timeOfDay - hour))`, `day = 1 + floor(D * timeOfYear) % D`, all fed to `CreateDateTime` (`:163-173`) which builds from `DateTime(0)` and **adds an hour when the result lands in daylight saving time** — a real-calendar artifact leaking into the game clock.

`UpdateTime` (`:203-211`) recomputes `m_Time`, `m_Date`, `m_Year` and caches `m_DaysPerYear` off the singleton, and it is called from `OnUpdate` and from `PostDeserialize`. **`TimeSystem` declares no `GetUpdateInterval`**, so it runs every simulation frame, registered `UpdateAt<TimeSystem>(SystemUpdatePhase.GameSimulation)` (`src/Game/Game.Common/SystemOrder.cs:358`) and `EditorSimulation` (`:602`), with `PostDeserialize<TimeSystem>` in the `Deserialize` phase (`:860`).

**Live cross-check.** `frameIndex - m_FirstFrame = 32,148`; `TimeOffset = 0.29167667`; `GetDateOffset(12) = 5/12`. `ticks = 32148 + 76461 + 1310720 = 1,419,329`. `timeOfDay = 1419329 % 262144 / 262144 = 0.4143105`; `timeOfYear = 1419329 / 3145728 = 0.4511925`. `TimeSystem` reported `normalizedTime = 0.41431046`, `normalizedDate = 0.45119253`, `year = 2024`, `daysPerYear = 12`. The formula reproduces both to seven decimals.

**Trap: `TimeSystem.startingYear` is not the city's starting year.** It is a plain settable property `PostDeserialize` reads once when creating a new game (`:34`, used at `:83`) and nothing maintains afterwards. Read live it is **0** while the city's actual starting year is 2024, which lives on `TimeData.m_StartingYear`. A mod reading `timeSystem.startingYear` gets zero on every loaded save.

### A day is a month, a year is twelve of them, and months are zero-based

The identity that catches every reader: **one in-game day *is* one in-game month.** `TimeSystem` has no month concept at all — `GetDateTime` fills `DateTime`'s *day* slot from `timeOfYear` — and every surface that says "month" is reading the same day index.

Four independent, compiled-in statements of twelve, none of them a prefab value:

- **`Game.Prefabs.CalendarEventMonths`** (`src/Game/Game.Prefabs/CalendarEventMonths.cs:3-17`) is a twelve-member flags enum, `January = 1` through `December = 0x800`, indexed by `1 << floor(normalizedDate * 12f)` (`src/Game/Game.Simulation/CalendarEventLaunchSystem.cs:113`). The `* 12f` is a literal in consuming code and the enum has twelve members; neither can be replaced by data. (The member is spelled `Septermber` — a first-party typo a mod naming it must reproduce.)
- **`ClimateSystem.CalculateMeanTemperatureEkholmModen` opens with `Assert.AreEqual(12, m_TimeSystem.daysPerYear)`** (`src/Game/Game.Simulation/ClimateSystem.cs:320`) and indexes `private static readonly int[,] kLut = new int[12, 5]` by day (`:157`, with `kSampleTimes = { 7, 13, 19 }` at `:173`).
- **`CalculateTemperatureAverage` branches on `if (m_TimeSystem.daysPerYear == 12)`** (`:402`), taking the Ekholm–Modén weighted monthly mean at twelve and a plain mean otherwise. So the game already handles a non-twelve year, degrading the temperature estimator rather than failing.
- **`TimeUISystem.GetTimeSettingsData` falls back to `new TimeSettingsData { m_DaysPerYear = 12 }` when the query is empty** (`src/Game/Game.UI.InGame/TimeUISystem.cs:193-204`, the literal at `:199`).

**Months are zero-based on the wire and one-based in `DateTime`.** `MenuUISystem` builds its save-info date as `new SimulationDateTime(currentDateTime.Year, currentDateTime.DayOfYear - 1, currentDateTime.Hour, currentDateTime.Minute)` (`src/Game/Game.UI.Menu/MenuUISystem.cs:869`) — the `- 1` is the zero-basing — and the frontend's `PA` returns `month: mod(dayIndex, daysPerYear)`, also zero-based (`DecompiledCitiesSkylines2/src-ui/source.js:45947-45954`). The wiki's localization page states the same from the mod-author side: *"the month being 0-indexed"* (https://cs2.paradoxwikis.com/Localize_your_mod, `§ Format date`).

The derived quantities every duration calculation needs, all exact consequences of `262144` and `D = 12`:

| Unit | Simulation frames | Note |
| --- | --- | --- |
| day (= month) | 262,144 | `2^18` |
| year | 262,144 × `m_DaysPerYear` (3,145,728 at twelve) | |
| quarter-day | 65,536 | the day-quarter index is `floor(normalizedTime * 4)` |
| hour | 262144 / 24 = 10,922.67 | not an integer, and not a power of two |
| minute | 262144 / 1440 ≈ 182.0444 | not an integer |
| second | 262144 / 86400 ≈ 3.034 | |

**The hour and the minute are not whole frame counts**, which is why the game quantizes rather than dividing (below) and why no update interval can be exactly an hour.

### The UI clock quantizes to whole in-game minutes, and the constant is a bare float

`TimeUISystem.GetTicks` (`:157-161`):

```csharp
float num = 182.04445f;
return Mathf.FloorToInt(Mathf.Floor((float)(m_SimulationSystem.frameIndex - TimeData.GetSingleton(m_TimeDataQuery).m_FirstFrame) / num) * num);
```

`182.04445` is 262144/1440 written as a literal with no relation to `kTicksPerDay` in the source. **So the `time.ticks` binding is quantized to in-game minute boundaries**, which is what stops the clock re-rendering sixty times a second. Read live: `ticks = 34406`, which is `floor(189 * 182.04445)` — the `FloorToInt` makes the binding a whole tick count, not an exact multiple.

That number is the one thing the wiki's units page does state, to eighteen digits (`182.044444444444444444`, https://cs2.paradoxwikis.com/Commonly_units_in_the_game). **Verdict: corroborated by the decompile at `TimeUISystem.cs:159`, which carries the float literal the game actually uses.** A mod reproducing the quantization must use the game's `182.04445f`, not the exact ratio, or its minute boundaries drift from the vanilla clock's over a long city. `Time2Work` copies the literal rather than deriving it, in three places (`Time2Work/NightShift/Systems/Time2WorkTimeUISystem.cs:58/169`, `Time2WorkUISystem.cs:148`), scaling it by its own factor.

### The `time` binding group is the whole frontend contract, and it is six values and two triggers

`TimeUISystem.OnCreate` (`src/Game/Game.UI.InGame/TimeUISystem.cs:84-95`) registers, all in group `"time"` (`:53`):

| Name | Kind | Payload |
| --- | --- | --- |
| `timeSettings` | value | `{ ticksPerDay, daysPerYear, epochTicks, epochYear }` |
| `ticks` | value | `int`, minute-quantized frames since `m_FirstFrame` |
| `day` | value | `int`, `TimeSystem.GetDay` |
| `lightingState` | value | `int`, a `LightingSystem.State` |
| `simulationPaused` | value | `bool` |
| `simulationSpeed` | value | `int`, the 0–2 **index** |
| `simulationPausedBarrier` | event | observer-counted; see the barrier finding |
| `setSimulationPaused` | trigger | `bool` |
| `setSimulationSpeed` | trigger | `int` index |

`TimeSettings` is an `IJsonWritable` struct whose `TypeBegin` is its own `GetType().FullName` (`:19-50`), so the wire carries `__Type: "Game.UI.InGame.TimeUISystem+TimeSettings"`.

`GetTimeSettings` (`:144-155`) composes `epochTicks` as `round(TimeOffset * 262144) + round(GetDateOffset(D) * 262144 * D)` — **the same two offset terms `TimeSystem.GetTicks` adds**, which is what lets the frontend do the whole calendar itself from `ticks` alone.

**Read live off the running UI**, subscribing to each `time.<name>.update` and triggering `time.<name>.subscribe`:

```json
{"timeSettings":{"__Type":"Game.UI.InGame.TimeUISystem+TimeSettings","ticksPerDay":262144,"daysPerYear":12,"epochTicks":1387181,"epochYear":2024},
 "ticks":34406,"day":0,"simulationSpeed":2,"simulationPaused":true,"lightingState":2}
```

`epochTicks` 1,387,181 is the formula's `76461 + 1310720` exactly, with `round(TimeOffset * 262144)` computed from the nudged `TimeOffset`; `76459` is the no-nudge figure.

### The frontend recomputes the calendar itself, and the arithmetic is four functions

`game-ui/game/data-binding/time-bindings.ts` (`DecompiledCitiesSkylines2/src-ui/source.js:45986`) exports the bindings above plus the conversions, and those are the mechanism half a UI mod needs (`:45943-45977`):

```js
MA(e, t) = ((e % t) + t) % t                                  // positive modulo, :45975
PA(s, ticks):  n = s.epochTicks + ticks; d = floor(n / s.ticksPerDay)
               → { year: s.epochYear + floor(d / s.daysPerYear), month: MA(d, s.daysPerYear) }   // :45947
OA(s, days) = PA(s, days * s.ticksPerDay)                     // :45943
AA(s, ticks):  as PA, plus  o = MA(n, s.ticksPerDay)
               hour   = trunc(o / s.ticksPerDay * 24)
               minute = trunc(MA(o / s.ticksPerDay * 1440, 60))                                  // :45955
LA(s, ticks) = 60 * trunc(...*24) + trunc(MA(...*1440, 60))   // minute-of-day, :45968
wA(minutes)  = { hour: floor(minutes/60) % 24, minute: minutes % 60 }                             // :45940
```

The frontend's `LightingState` enum (`:45978-45985`) declares `Dawn=0, Sunrise=1, Day=2, Sunset=3, Dusk=4, Night=5`, matching C# `LightingSystem.State` member for member except that C# carries a seventh, `Invalid = 6` (`src/Game/Game.Rendering/LightingSystem.cs:28-37`). **`TimeUISystem.GetLightingState` never sends `Invalid`** — it substitutes a time-window fallback (`:168-179`), so the frontend's six-member enum is complete for what crosses the wire.

**Trap in the chart scale.** The `simulationTime` Chart.js scale declares `defaults.timeSettings = { daysPerYear: 12, ticksPerDay: 1 << 17, epochTicks: 0, epochYear: 0 }` (`source.js:113155-113160`). `1 << 17` is **131,072 — half a day**. It is latent because the live binding always supplies real settings, but a mod reusing that scale without passing `timeSettings` gets an axis whose day is half the game's. Its tick intervals are the useful half and are derived correctly from whatever is passed: `ticksPerDay / 24` for an hour, `/ 8`, `/ 4`, `/ 2`, `ticksPerDay` for a day, `3 * ticksPerDay`, `daysPerYear * ticksPerDay` for a year (`:113186-113205`), and `ticksPerDay / 1440` for a minute in the tooltip hit-test (`:113331/113346`).

`LocalizedDuration` takes its value **in days** and converts up: `round(value) >= maxMonths ? round(value / daysPerYear) years : value months`, with `maxMonths` defaulting to `daysPerYear` (`source.js:29949-29978`). So a duration crossing to the frontend for display is a day count, matching the C# convention that a prefab duration is in days.

### Update interval and offset: the real expression, and what a cadence is worth

`GameSystemBase` declares the two hooks with defaults `1` and `-1` (`src/Game/Game/GameSystemBase.cs:131-139`). `UpdateSystem.GetInterval` reads both and throws `"System update interval not power of 2"` on `!math.ispow2(interval)` (`src/Game/Game/UpdateSystem.cs:277-290`).

**The mask, transcribed** (`UpdateSystem.cs:224`, inside the three-argument `Update(phase, updateIndex, iterationIndex)`):

```csharp
if ((updateIndex & (uint)(systemData.m_Interval - 1)) != (uint)systemData.m_Offset)
{
    continue;
}
```

`updateIndex` is `SimulationSystem.frameIndex` at the call sites (`SimulationSystem.cs:173/282/286`). **So a system runs on the frames where `frameIndex & (interval - 1) == offset` — compared against the offset, not against zero.** An interval of 1 forces offset 0 (`UpdateSystem.cs:399-401`, and again at `:428-430`); a negative offset is the sentinel that asks `Refresh` to assign one, spreading same-interval systems in the same phase across distinct residues by a bit-reversal walk (`:326-361`).

Three consequences a mechanics reader needs:

1. **A system with interval N runs once every N simulation frames**, so its passes per day are `262144 / N` and one pass covers `N / 262144` of a day. The vanilla idiom inverts that: `public static readonly int kUpdatesPerDay = <n>` and `return 262144 / kUpdatesPerDay`.
2. **`UpdateFrame` sharding divides that again.** `SimulationUtils.GetUpdateFrame(frame, updatesPerDay, groupCount) = (frame / (262144 / (updatesPerDay * groupCount))) & (groupCount - 1)` (`src/Game/Game.Simulation/SimulationUtils.cs:158-161`), paired with `interval = 262144 / (kUpdatesPerDay * groupCount)`. A system using it runs `kUpdatesPerDay * groupCount` times a day and touches **each entity** `kUpdatesPerDay` times. `GetUpdateFrameWithInterval(frame, interval, groupCount) = (frame / interval) & (groupCount - 1)` (`:153-156`) is the same thing keyed on the interval instead, and `GetUpdateFrameRare(frame, daysPerUpdate, groupCount) = (frame / (daysPerUpdate * 262144 / groupCount)) & (groupCount - 1)` (`:163-166`) is the multi-day form. **`groupCount` is 16 everywhere in vanilla.**
3. **The interval is read in exactly three phases.** The three-argument overload is called only from `LoadSimulation` (`SimulationSystem.cs:173`), `EditorSimulation` (`:282`) and `GameSimulation` (`:286`); the one-argument `Update(phase)` never reads `m_Interval`. So a `GetUpdateInterval` override anywhere else — `UIUpdate` above all — is inert. `mod-lifecycle-and-ordering` owns this; it is repeated here because a mod computing "once per day" on a UI system gets every rendered frame instead.

**The cadence roster.** `kUpdatesPerDay` is declared 74 times in `src/`, all in `Game.Simulation`, and **all but two are a `const` or a `static readonly`** (`CrimeCheckSystem.cs:260` and `SicknessCheckSystem.cs:243` declare an instance `public readonly int kUpdatesPerDay = 1`), so the roster ships as numbers under the ruling. The declared values span 1 to 2048.
**The interval column is the per-entity cadence, not necessarily the system's own interval**: about half of the examples shard, returning `262144 / (kUpdatesPerDay * 16)` and touching each entity once per `kUpdatesPerDay` pass — `AgingSystem.cs:204`, `CompanyProfitabilitySystem`, `LeaveHouseholdSystem`, `DivorceSystem`, `PartnerSystem`, `CrimeEffectSystem`, `DeathCheckSystem`, `PropertyRenterSystem`, `TaxSystem`, `CitizenFindJobSystem` all do — while `LandValueSystem`, `AirPollutionSystem`, `TradeSystem`, `GroundPollutionSystem`, `ProductionSpecializationSystem`, `BudgetApplySystem` and `TrafficAmbienceSystem` return the bare `262144 / kUpdatesPerDay`. So `AgingSystem` runs sixteen times a day at interval 16,384 and ages each citizen once, which is what "1" means on its row:

| `kUpdatesPerDay` | per-entity interval (frames) | one pass is | example declaration |
| --- | --- | --- | --- |
| 1 | 262,144 | a whole day | `AgingSystem.cs:173`, `CompanyProfitabilitySystem.cs:175`, `GraduationSystem.cs:233` |
| 2 | 131,072 | half a day | `LeaveHouseholdSystem.cs:193` |
| 4 | 65,536 | a quarter-day | `DivorceSystem.cs:226`, `PartnerSystem.cs:178`, `CrimeEffectSystem.cs:84` |
| 16 | 16,384 | 90 in-game minutes | `BirthSystem.cs:221`, `DeathCheckSystem.cs:340`, `PropertyRenterSystem.cs:207` |
| 32 | 8,192 | 45 minutes | `TaxSystem.cs:191`, `LandValueSystem.cs:260`, `CityStatisticsSystem.cs:357` |
| 128 | 2,048 | ~11 minutes | `AirPollutionSystem.cs:74`, `TradeSystem.cs:259`, `GroundPollutionSystem.cs:47` |
| 256 | 1,024 | ~5.6 minutes | `CitizenFindJobSystem.cs:261`, `CrimeAccumulationSystem.cs:234` |
| 512 | 512 | ~2.8 minutes | `BuildingEfficiencySystem.cs:121`, `ProductionSpecializationSystem.cs:74` |
| 1024 | 256 | ~84 seconds | `BudgetApplySystem.cs:74`, `SoilWaterSystem.cs:222`, `TrafficAmbienceSystem.cs:30` |
| 2048 | 128 | ~42 seconds | `ElectricityFlowSystem.cs:313`, `WaterPipeFlowSystem.cs:310` |

**16,384 frames is 90 in-game minutes exactly**, which is the wiki's second constant and the only other number on its units page. **Verdict: corroborated** — 262144 / 16384 = 16 and 16 × 90 min = 1,440 min = 24 h, so the stub's two numbers and the decompile's day constant are one arithmetic.

**Trap: `kUpdatesPerDay` is a naming convention, not a mechanism.** Nothing reads the field by reflection; a system divides it into 262144 itself, or declares it and returns the quotient as a bare literal, or shards by it — `BuildingEfficiencySystem` declares `kUpdatesPerDay = 512` (`src/Game/Game.Simulation/BuildingEfficiencySystem.cs:121`), returns `32` (`:135`) and re-types `512` at its `GetUpdateFrame` call (`:152`), so the constant alone lands sixteen times too slow; read `GetUpdateInterval`, never the constant. `ElectricityFlowSystem` declares all three of `kUpdateInterval = 128`, `kUpdatesPerDay = 2048` and `kUpdatesPerHour = 85` as `const` (`:311/313/315`) and gates on `frameIndex % 128` rather than on `GetUpdateInterval` at all. **85 is `2048 / 24` truncated**, and it is the conversion behind `Battery.storedEnergyHours => m_StoredEnergy / 85` (`src/Game/Game.Buildings/Battery.cs:14`) and `BatteryData.capacityTicks => 85 * m_Capacity` (`src/Game/Game.Prefabs/BatteryData.cs:12`) — an in-game **hour** measured in solver ticks.

### The day-night cycle has no single boundary — five different definitions, all constants

There is no `DayNightSystem` and no shared day/night predicate. Each consumer compares `TimeSystem.normalizedTime` against its own thresholds, and they disagree:

| Consumer | Test | Day window |
| --- | --- | --- |
| `EffectFlagSystem.cs:155` | `night = t >= kNightBegin \|\| t < kDayBegin` | 06:00–18:00 |
| `ClimateSystem.cs:491` | `day = t >= kDayBegin && t < kNightBegin` | 06:00–18:00 |
| `TimeUISystem.cs:176` | `day = !(t < 7f/24f) && !(t > 0.875f)` | 07:00–21:00 |
| `TransportLineSystem.cs:824` | `isNight = t < 0.25f \|\| t >= 11f/12f` | 06:00–22:00 |
| `CitizenBehaviorSystem.cs:1076-1090` | `IsSleepTime` against `GetSleepTime(…)`'s `float2` (`:1026-1074`) | per citizen: `(0.875f, 0.175f)` + `SleepOffset` pseudo-random in `[0, 0.2)`, `-0.05f` elderly, `-0.1f` child, `+0.05f` teen, `frac`, then shifted clear of `GetTimeToWork` / `GetTimeToStudy` |

`EffectFlagSystem.kNightBegin = 0.75f` and `kDayBegin = 0.25f` are `public static readonly float` (`src/Game/Game.Effects/EffectFlagSystem.cs:27/29`) and ship as numbers; so do the `7f/24f`, `0.875f`, `0.25f` and `11f/12f` literals, which are compiled into their consuming expressions. The sleep window is not prefab data: `grep -rn m_SleepTime src/` returns nothing, and `EconomyParameterData` carries `m_WorkDayStart` / `m_WorkDayEnd` only — `GetSleepTime` takes the parameters solely to pass them through to the work and study helpers. Its literals are compiled in and ship.

**So "is it night?" has no single answer in this game**, and a mod that picks one of these and expects the others to agree gets behaviour that changes at a boundary it did not choose. The reference owes that as a trap with the table.

Two more day-derived indices, both compiled in:

- **The day quarter.** `normalizedTime * 4f` is the centre of a saturated tent of four weights in `RoadSafetySystem.cs:275-277` and `TrafficFlowSystem.cs:315-317` (`float4 x = saturate(new float4(max(num - 3f, 1f - num), 1f - abs(num - new float3(1f, 2f, 3f))))`, dotted across the `Road` component's four per-quarter slots — not floored, not an index), and `floor(normalizedTime * 4f)` is a bit in `CalendarEventLaunchSystem.cs:114`: `(CalendarEventTimes)(1 << floor(normalizedTime * 4f))`. `Game.Prefabs.CalendarEventTimes` names them `Night = 1, Morning = 2, Afternoon = 4, Evening = 8` (`src/Game/Game.Prefabs/CalendarEventTimes.cs:3-9`) — **so the game's own name for 00:00–06:00 is `Night` and for 18:00–24:00 is `Evening`**, which is a fifth disagreement with the table above.
- **The month bit.** `(CalendarEventMonths)(1 << floor(normalizedDate * 12f))` (`CalendarEventLaunchSystem.cs:113`).

**The visual day-night cycle is rendering, and `DayNightCycleData` sets none of its timing.** `DayNightCycleData : ScriptableObject` (`src/Game/DayNightCycleData.cs`, in the assembly's global namespace, `[CreateAssetMenu]`) carries thirty-odd fields, and **every one is a light angle, exposure limit, colour, tint, LUT or contrast**. The four that sound like schedule — `DawnStartAngle`, `SunriseMidpointAngle`, `SunsetMidpointAngle`, `DuskEndAngle` — are *sun elevation angles in degrees*, not times. Nothing in this asset decides when dawn is; the sun's position decides, and the asset says which appearance to use once the sun is at a given angle. **A reader sent to that file looking for the day length finds thirty numbers and none of them is one**, and under the initializer ruling none of them ships anyway.

### The sun runs on a real 365-day astronomical year driven by the game's twelve-day one

`PlanetarySystem` (`src/Game/Game.Simulation/PlanetarySystem.cs:23`, registered at `SystemUpdatePhase.PreCulling`, `SystemOrder.cs:639`) is the day-night cycle's actual driver, and it does not use the game calendar. It keeps a real-world date — `m_Year`, `m_Day`, `m_Hour`, `m_Minute`, `m_Second`, `m_Latitude`, `m_Longitude` (`:142-154`) — and its constants are the real ones: `kDaysInYear = 365f`, `kHoursInDay = 24f`, `kSecsInHour = 3600f`, `kLunarCyclesPerYear = 12f`, all `private const float` (`:122-140`).

`OnUpdate` (`:414-454`) maps one calendar onto the other:

```csharp
double renderingFrame = (double)(m_RenderingSystem.frameIndex - value2.m_FirstFrame) + (double)m_RenderingSystem.frameTime;
float timeOfYear = m_TimeSystem.GetTimeOfYear(value, value2, renderingFrame);
float num3      = m_TimeSystem.GetTimeOfDay(value, value2, renderingFrame) * debugTimeMultiplier;
int   num4      = m_TimeSystem.GetYear(value, value2);
UpdateTime(timeOfYear, num3, num4);
```

and `normalizedDayOfYear`'s setter is `dayOfYear = value * 365f + 1f` (`:314-324`). **So the simulation's `timeOfYear` fraction is stretched across 365 astronomical days**, and `CreateDateTime` (`:397-404`) turns that plus latitude and longitude into a Julian date — including a `-43200 * longitude / 180` seconds shift for the meridian — which `SunMoonData.GetLimitedSunPosition` converts into a sun direction. The moon runs on `moonDay => floor(day * (1f/12f) * numberOfLunarCyclesPerYear)` (`:350`).

**That is why day length varies with season and with the map**, and why the wiki's `Climate` page is right that "the length of the day-night cycle is based on the map's climate and current season": it is a genuine solar calculation at the map's latitude, not a curve. `kDefaultLatitude = 41.9028f` and `kDefaultLongitude = 12.4964f` are `private static readonly float` (`:102-104`) — Rome — and `SetDefaults` restores them (`:520-524`); the system serializes only those two floats (`:526-540`).

`ClimateSystem` overwrites them from the map's climate prefab **in `PostDeserialize` only** (`:561`), once per load — the `currentClimate` setter (`:204-222`) re-orders seasons, recomputes the average temperature and updates the season and weather, and never touches `m_PlanetarySystem`, so a climate switched at runtime keeps the previous latitude, longitude and sun limit until the next load: `m_PlanetarySystem.latitude = prefab2.m_Latitude; …longitude = prefab2.m_Longitude; …sunLimit = prefab2.sunLimitRadians;` (`src/Game/Game.Simulation/ClimateSystem.cs:588-590`), and the editor's climate panel does the same (`src/Game/Game.UI.Editor/ClimatePanelSystem.cs:260-262`). `ClimatePrefab.sunLimitRadians => new double2(radians(m_SunElevationClampStart), radians(m_MaxSunElevationAngle))` (`src/Game/Game.Prefabs.Climate/ClimatePrefab.cs:658`) — **so the sun's maximum elevation is authored in degrees and stored in radians**, the same degrees-to-radians bake `transportation-and-vehicles` records for turning angles.

**Two overrides bypass all of it**, both compiled-in literals (`PlanetarySystem.cs:418-430`): `overrideTime`, which the developer menu sets (`DebugSystem.cs:1842-1846`), and the `gameplay.dayNightVisual` setting, whose off-branch pins `latitude = 51.2277f, longitude = 6.7735f, time = 14.5f, day = 177, year = 2020`. That is a fixed mid-afternoon midsummer sun over Düsseldorf, and it is what a player who turned the day-night cycle off is looking at.

**Live** (2026-08-22, paused at `frameIndex = 5,632,217`): `lat = 35.20208, lon = −101.887344, year = 2024, day = 165, time = 10.153096, normalizedDayOfYear = 0.4504741, lunarCycles = 1, moonDay = 13, sunLimit = (0.7853982, 1.5707963)` — π/4 and π/2, so the map authored 45° and 90°, the class initializers' own values. Astronomical day 165 is mid-June against a simulation `normalizedDate` of 0.4512, which is day 5 of 12 with a starting month of 5 — the two calendars agreeing on "early summer" through the fraction and nothing else.

### The rendering clock is a second, interpolated frame index, and it is the one the visuals read

`RenderingSystem` keeps its own `public uint frameIndex` and `public float frameTime` (`src/Game/Game.Rendering/RenderingSystem.cs:57/59`), advanced from the simulation's plus an interpolation offset (`:180-218`), reset to the simulation's exactly on a discontinuity (`:292-293`). It runs ahead of or behind `SimulationSystem.frameIndex` by a small signed amount and carries a fractional remainder, which is what gives the sun, the clock hands and the climate curves a smooth motion between two discrete simulation ticks.

Read live while paused: `simFrame = 5,634,509`, `renderFrame = 5,634,506`, `renderFrameTime = 0.6243`. **The two counters are not equal even at rest**, and the difference is not a bug.

`RenderingSystem` also derives its own periodic values from it: `(frameIndex % uint2(60, 3600) + frameTime)` for shader time (`:224`) and `(frameIndex % 216000) + frameTime` (`:227`) — 216,000 being 60 × 3600, an hour of *real-time-equivalent* frames rather than an in-game hour.

**So the rule is: a simulation decision reads `SimulationSystem.frameIndex`, and a visual reads `RenderingSystem.frameIndex` with its `frameTime`.** `PlanetarySystem` and `ClimateRenderSystem` take the second; every `Game.Simulation` system takes the first. Using the rendering one in a simulation system makes the result depend on frame rate.

### Seasons are prefab data, and their unit is the year fraction

`ClimateSystem.SeasonInfo` (`src/Game/Game.Simulation/ClimateSystem.cs:29-82`) is a `[Serializable]` class, not a component: a `SeasonPrefab m_Prefab`, a `[Range(0,1)] float m_StartTime`, and eleven authored climate quantities. `ClimatePrefab.m_Seasons` is a plain array of them (`src/Game/Game.Prefabs.Climate/ClimatePrefab.cs:646`) with a lazily built `m_SeasonsOrder` sorting indices by `m_StartTime` (`:654/947-951`).

**`m_StartTime` is a fraction of the year in [0,1)**, so the season boundary in frames is `m_StartTime * 262144 * m_DaysPerYear`. A season runs from its own start to the next one's, wrapping (`FindSeasonByTime`, `:1331-1360`); `GetSeasonAndMidTime(index)` returns the ordered season and its midpoint (`:1281-1284`); `CountElapsedSeasons(startTime, elapsedTime)` counts boundaries crossed over a span (`:1286-1308`) and is what the achievement system uses to ask "has a full year passed" (`src/Game/Game.Achievements/AchievementTriggerSystem.cs:815-824`).

**A climate may declare any number of seasons.** Nothing constrains the array length, `FindSeasonByTime` short-circuits at 0 and 1 elements, and the modulo walk handles the rest. The 1.6.0-verified wiki page says the same in prose (https://cs2.paradoxwikis.com/Map_Creation:_Climate): *"A climate may contain one season, two seasons, four seasons, six seasons, or any other number."* **Verdict: corroborated by the decompile at `ClimatePrefab.cs:1286-1360`.** The 1.0-stamped `Climate` page's "four distinct seasons" and "each season lasts three months" is the stale statement; `docs/SOURCES.md` entry 10 already prefers the `Map Creation:` family, and the decompile settles it independently.

`ClimateSystem.UpdateSeason(prefab, normalizedDate)` (`:656-666`) recomputes the season's mean temperature, precipitation and cloudiness **only when the season entity changes**, and `SampleClimate(prefab, t)` evaluates the prefab's five curves at `t * daysPerYear` (`:607-609`). Live: `currentSeason = SeasonSummer`, `currentClimate = ContinentalCorralRichesClimate`, at `normalizedDate = 0.4512`.

**The curve axis and the calendar are two twelves that are not the same twelve.** `ClimatePrefab`'s generated curves run on a fixed 0..12 axis inlined as literals, while `SampleClimate` evaluates at `t * daysPerYear` from the prefab field — `environment-and-pollution.md`'s climate facet already records this and it holds. So a mod changing `m_DaysPerYear` desynchronises the sample point from the curve axis, on top of degrading the temperature estimator at `ClimateSystem.cs:402`. **That is the concrete cost of changing the year length and it belongs in this reference's traps**, since this is the reference that says the field is changeable.

### What else the game measures in: the full unit table

**This is a mechanism table and ships baked.** An agent cannot tell what a C# field's magnitude means without it: the same `int` is hundreds of watts on one component and kilograms on another, and the only first-party statement of which is the frontend formatter that renders it.

The C# side is `Game.UI.Unit`, a static class of 33 `public const string` values (`src/Game/Game.UI/Unit.cs:3-69`) — `kInteger = "integer"`, `kPower = "power"`, and so on. A `LocalizedNumber<T>` carries one of those strings and the frontend dispatches on it.

**The frontend's value-formatter table is `game-ui/common/localization/unit.ts`'s enum (`DecompiledCitiesSkylines2/src-ui/source.js:28978-29017`, 38 members) against the formatter map at `:29136-29269`.** Transcribed whole; every conversion is the divisor or expression the bundle applies to the raw C# value, and the `Sc.Common.*` names are the localization keys the result is rendered into.

| Unit string | Metric branch | Freedom branch | So the C# value is in |
| --- | --- | --- | --- |
| `integer` | `VALUE`, 0 fraction digits, thousands-separated | — | whatever it is |
| `integerRounded` | `<1e3` plain; `<1e6` `/1e3` + `VALUE_THOUSAND`, 1 digit; else `/1e6` + `VALUE_MILLION`, 1 digit | — | |
| `integerPerMonth` | `VALUE_PER_MONTH` | — | **per in-game day** (a month is a day) |
| `integerPerHour` | `VALUE_PER_HOUR` | — | per in-game hour |
| `floatSingleFraction` / `floatTwoFractions` / `floatThreeFractions` | 1 / 2 / 3 fraction digits | — | |
| `percentage` | `VALUE_PERCENT`, 0 digits | — | percent, already ×100 |
| `percentageSingleFraction` | `VALUE_PERCENT`, 1 digit | — | |
| `percentagePrecise` | `VALUE_PERCENT`, 2 digits | — | |
| `angle` | `VALUE_ANGLE`, 1 digit | — | **degrees** |
| `length` | `<1e3` → `VALUE_METER`, 1 digit; else `/1e3` → `VALUE_KILOMETER`, 1 digit | `<1609` → `yards(v)` `VALUE_YARD`; else `miles(v/1e3)` `VALUE_MILE`, 1 digit | **metres** |
| `height` | `VALUE_METER`, 0 digits | `feet(v)` `VALUE_FOOT`, 0 digits | metres |
| `netElevation` | `VALUE_METER`, **2 digits** | **`3 * v`** `VALUE_FOOT`, 2 digits | metres |
| `area` | `<1e5` → `VALUE_SQUARE_METER`, 0 digits; else `/1e6` → `VALUE_SQUARE_KILOMETER`, 1 digit | `<1e5` → `squareFeet(v)`; else `acres(v)`, 1 digit | **square metres** |
| `volume` | `VALUE_CUBIC_METER`, 1 digit | `gallons(v)` `VALUE_GALLON`, 0 digits | **cubic metres** |
| `volumePerMonth` | `VALUE_CUBIC_METER_PER_MONTH`, 1 digit | `gallons(v)` `VALUE_GALLON_PER_MONTH`, 1 digit | m³ per in-game day |
| `weight` | `<100` → `VALUE_KILOGRAM`, 1 digit; `<1e6` → `/1e3` `VALUE_TON`, 2 digits; else `/1e6` `VALUE_KILOTON`, 2 digits | `<100` → `pounds(v)`, 1 digit; `<9071847.4` → `shortTons(v)` `VALUE_SHORT_TON`, 2 digits; else `shortTons(v)/1e3` `VALUE_SHORT_KILOTON`, 2 digits | **kilograms** |
| `weightPerCell` | `/1e3` `VALUE_TON_PER_CELL`, 2 digits | `shortTons(v)` `VALUE_SHORT_TON_PER_CELL`, 2 digits | kg per cell |
| `weightPerMonth` | `<100` → `VALUE_KG_PER_MONTH`, 1 digit; else `/1e3` `VALUE_TON_PER_MONTH`, 2 digits | `<100` → `pounds(v)`, 1 digit; else `shortTons(v)` `VALUE_SHORT_TON_PER_MONTH`, 2 digits | kg per in-game day |
| `power` | `<1e4` → **`/10`** `VALUE_KILOWATT`, 1 digit; else **`/1e4`** `VALUE_MEGAWATT`, 2 digits | — | **hundreds of watts** |
| `energy` | **`/1e4`** `VALUE_MEGAWATT_HOURS`, 1 digit | — | **hundreds of watt-hours** |
| `dataRate` | `VALUE_GIGABIT_PER_SECOND`, 1 digit | — | **gigabits per second** |
| `dataBytes` | binary ladder over `[byte, kilobyte, megabyte, gigabyte, terabyte]` | — | **bytes** |
| `dataMegabytes` | `1024 * v * 1024` then ladder over `[megabyte, gigabyte, terabyte]` | — | **megabytes** |
| `money` | `VALUE_MONEY`, 0 digits | — | currency units |
| `moneyPerCell` | `VALUE_MONEY_PER_CELL`, 1 digit | — | |
| `moneyPerMonth` | `VALUE_MONEY_PER_MONTH`, 0 digits | — | per in-game day |
| `moneyPerHour` | `VALUE_MONEY_PER_HOUR`, 0 digits | — | per in-game hour |
| `moneyPerDistance` | `VALUE_MONEY_PER_KILOMETER` | **`/1.6`** `VALUE_MONEY_PER_MILE` | **per kilometre** |
| `moneyPerDistancePerMonth` | `VALUE_MONEY_PER_KILOMETER_PER_MONTH` | **`/1.6`** `VALUE_MONEY_PER_MILE_PER_MONTH` | per km per in-game day |
| `xp` | `VALUE_XP`, 0 digits | — | |
| `temperature` | `VALUE_CELSIUS`, 0 digits | `fahrenheit(v)` / `kelvin(v)` per `temperatureUnit` | **degrees Celsius** |
| `temperaturePrecise` | as above, 2 digits | as above, 2 digits | |
| `screenFrequency` | 3 digits, no thousands separator, fraction up to 1e3 | — | hertz |
| `durationSeconds` | `VALUE_SHORT_SECOND`, 0 digits | — | **real seconds** |
| `custom` | dispatches on the caller's own format and fraction-digit arguments | — | |
| `bodiesPerMonth` | **no entry in the value table** | — | see the trap below |

**The US-customary conversions, verbatim** (`source.js:28937-28969`, exported as `game-ui/common/localization/units-us-customary.ts` at `:29027`):

```js
fahrenheit(e) = (9 * e) / 5 + 32      kelvin(e)  = e + 273.16
gallons(e)    = 264.172 * e           pounds(e)  = e / 0.45359237
shortTons(e)  = e / 907.18474         yards(e)   = e / 0.9144
miles(e)      = e / 1.609344          squareFeet(e) = e / 0.092903
acres(e)      = e / 4046.873          feet(e)    = 3.28084 * e
fromFeet(e)   = e / 3.28084
```

`kelvin` adds **273.16, not 273.15** — the triple point rather than absolute zero, a first-party off-by-0.01 worth reproducing rather than correcting if a mod wants to match the vanilla display.

**Three traps in the table itself:**

1. **`netElevation`'s Freedom branch multiplies by 3, not by 3.28084**, where every other length uses `feet()` (`:29173-29177`). A road at 12 m reads 36 ft in the game and 39.4 ft anywhere else.
2. **`bodiesPerMonth` has a fraction entry and no value entry.** It is declared in the enum (`:28989`) and appears in the fraction table as `FRACTION_BODIES_PER_MONTH` (`:29593-29594`, key declared at `:26872`), but the value-formatter map at `:29136-29269` has no key for it, so a plain `LocalizedNumber` carrying it falls through to `` `${value} <bodiesPerMonth>` `` (`:29133-29135`). It is also one of the five units missing from the C# constant list.
3. **The C# constant list is five short of the frontend enum.** `Game.UI.Unit` declares 33 strings; the frontend declares 38. Missing from C#: `percentagePrecise`, `bodiesPerMonth`, `temperaturePrecise`, `height`, `durationSeconds`. A mod wanting any of those must pass the raw string rather than a `Unit.k*` constant.

**Fractions and bounds have their own tables and they are not the same table.** `LocalizedFraction`'s map (`_u`, `:29550-29604`, exported at `:29618`) covers `volume`, `volumePerMonth`, `weight`, `weightPerMonth`, `power`, `energy`, `bodiesPerMonth`, `xp`, `integer`, `integerPerMonth`, `integerRounded` — eleven units — and **its `energy` entry has a kilowatt-hours branch (`/10`) the value table lacks**, confirming energy is hundreds of watt-hours from the other side. `LocalizedBounds`' map (`gu`, `:29503-29519`) covers three: `power`, `percentageSingleFraction`, `temperature`. Any other unit passed to a fraction falls through to a `` `${value} / ${total} <unit>` `` placeholder; bounds render equal `min`/`max` as a plain `LocalizedNumber` before consulting the table (`:29498-29501`), and otherwise fall through to `` `${min}–${max} <unit>` ``.

**The player's unit preferences are three settings**, `Game.Settings.InterfaceSettings` (`src/Game/Game.Settings/InterfaceSettings.cs`): `TimeFormat { TwentyFourHours, TwelveHours }` (`:32-36`), `TemperatureUnit { Celsius, Fahrenheit, Kelvin }` (`:38-43`), `UnitSystem { Metric, Freedom }` (`:45-49`), all under the `"Unit"` settings section (`:185-190`) and defaulting to 24 h / Celsius / Metric (`:242-244`). The frontend's enums declare the same ordinals (`source.js:26093-26099`), so the wire is the integer and the two ends agree.

`Rots:` the unit enum, its 38 members, the C#/frontend gap and every threshold above. Re-check against `src/Game/Game.UI/Unit.cs` and `source.js`'s `:28978-29017` / `:29136-29269`, at a copy whose line count matches the baseline.

### The C# side's own unit conventions, cross-checked

The unit table says what the frontend does to a value. The other half — what the simulation stores — is stated by the bake sites, and three families are worth carrying because getting one wrong is silent:

- **Speed is km/h on the authoring class and m/s on the component.** `NetInitializeSystem` divides by 3.6 baking `RoadData.m_SpeedLimit`; vehicle prefabs do the same for `m_MaxSpeed` (`roads-and-traffic.md:90-91`, `transportation-and-vehicles.md:23-24`). The pathfinder's sentinel top speed is `277.77777f` m/s = 1000 km/h.
- **Angles are degrees on the class and radians on the component**, both for vehicle turning and for `ClimatePrefab.sunLimitRadians` (`ClimatePrefab.cs:658`).
- **Durations are days on prefab data and frames on instance components** (the 262144 finding above), except where a prefab field is explicitly named seconds — `DisasterData.m_Duration`, multiplied by 60 rather than by 262144 (`environment-and-pollution`'s disasters facet), because **60 frames is one real second at 1×**, which is the same constant the step loop uses.

**So this game has three different "duration" conventions on prefab data — days, real seconds and raw frames — and the field name does not distinguish them.** The multiplier at the use site is the only way to tell: `× 262144` is days, `× 60` is real seconds, no multiplier is frames.

### The wiki says four things about the clock, all correct, on three pages that never cite one another

Fetched 2026-08-22 through the MediaWiki API (a plain HTTP fetch returned the JS bot challenge; `WebFetch` rendered past it, as `docs/SOURCES.md` entry 10 predicts).

**`Commonly units in the game`** (https://cs2.paradoxwikis.com/Commonly_units_in_the_game, page id 1740) is **429 bytes, last edited 2024-07-18, in no category, with no `{{Version}}` and no stub template**. Its entire body under `== Units ==` is one subsection, `=== Time :uint ===`, stating four things: the unit is a `uint` aliased `frameIndex` or `UpdateInterval`; as an update interval it must be a power of 2; one minute ≈ `182.044444444444444444`; 90 minutes = `16384`. **Its own opening sentence promises the rest — "we will use many units, some of them are very important and an attempt will be made to document them here" — and no other unit is written.** Three pages link to it, all the same index-list entry; no article cites it in prose.

**Verdict on all four claims: corroborated by the decompile.** 182.044 is `TimeUISystem.cs:159`'s `182.04445f`; 16384 is `262144 / 16`, exactly 90 minutes; the power-of-two rule is `UpdateSystem.cs:286-288`. The page's silence about everything else is accurate rather than misleading — **the wiki genuinely has no source for any non-time unit**, which is why this reference's unit table has to come from the bundle.

**Verdict on the offset rule.** `Systems` (https://cs2.paradoxwikis.com/Systems, `{{Version|1.0}}`, in `Category:Potentially outdated`) adds *"the offset must be `0 <= offset < interval`, otherwise the system will not be updated"*. **The decompile overturns the "otherwise" half**: `GameSystemBase.GetUpdateOffset` returns `-1` by default (`GameSystemBase.cs:136-139`) and a negative offset is the sentinel asking `UpdateSystem.Refresh` to assign one (`UpdateSystem.cs:326-361`, `:399-401`), which is what every vanilla system relies on. The wiki's rule is right for a value you choose and wrong about what happens when you do not.

The **day constant lives in a code comment on that same page** — `// One day (or month) in-game is '262144' ticks` — and nowhere in the wiki's gameplay prose. The **year length lives in a JSX prop comment** on `Localize your mod` (https://cs2.paradoxwikis.com/Localize_your_mod, `§ Format duration`): `daysPerYear={12} // mandatory, and remember that days are months are the same thing in CS, so there are 12 days per year`. **Verdict: corroborated** — the identity is `TimeSystem`'s, which has no month concept at all.

`Systems and Components catalog` states one thing found nowhere else and it is correct: *"the `updateIndex` (`frameIndex`) … is constantly incrementing from the beginning of the game"* while `iterationIndex` resets per rendered frame. That matches `SimulationSystem.cs:277-288` exactly, including the detail that the counter is process-scoped rather than city-scoped, which is the finding above.

`Common ECS Components` (https://cs2.paradoxwikis.com/Common_ECS_Components) **lists `TimeData`'s five fields with every description cell blank** — the wiki's sharpest silence in this area, since it names the component the whole calendar hangs off and says nothing about any field.

`Applying Animation Curves to Multiple Emissive Light Sources` carries the wiki's only statement about wall-clock time, and it is the right one: *"the animation is normalised against the in-game simulation time … Simulation frames are aligned to the in-game simulation speed rather than real-world time. As cities grow larger … the overall simulation speed can slow down."* **Verdict: corroborated by `SimulationSystem.cs:246-253`** — the frame-budget clamp is exactly that mechanism. It is also where the "1x, 2x, 3x" error lives.

### Corpus: `Time2Work` changes the clock by scaling the constant, and by disabling twelve vanilla systems

Read 2026-08-22 at `cs2-third-party-mods/Time2Work` (project root `NightShift/`). Matched on the catalog's **Demonstrates** half, which names it as *"Reimplementing the game's time model, deriving ticks per day from the vanilla constant scaled by a factor"* (`mod-catalog.md:206-209`). It settles nothing about the game; what it shows is **which vanilla surfaces you have to take over to change the clock**, and that list is the useful output.

**It derives its day from the vanilla constant and a setting** (`Time2Work/NightShift/Systems/Time2WorkTimeSystem.cs:42-52`, repeated every update at `:203-212`):

```csharp
if (Mod.m_Setting.slow_time_factor != 1f) {
    timeReductionFactor = Mod.m_Setting.slow_time_factor;
    kTicksPerDay = (int)Math.Floor(timeReductionFactor * TimeSystem.kTicksPerDay);
} else { kTicksPerDay = TimeSystem.kTicksPerDay; timeReductionFactor = 1f; }
```

**Correction to a widely-repeated description**: this is *not* `262144 / kUpdatesPerDay`. Those are two different things and `Time2Work` uses both — the scaled constant for the calendar, and `262144 / N` for its own update intervals, always under the comment `// One day (or month) in-game is '262144' ticks` (`WeekSystem.cs:44` and eleven siblings).

**What it had to take over** — twelve vanilla systems `Enabled = false` at `Mod.cs:94-109`, including `TimeUISystem` and `StatisticsUISystem`, each replaced by a fork; **`TimeSystem` itself patched method by method rather than disabled** — a postfix on `OnUpdate` writing `m_Time`, `m_Date` and `m_Year` back into the vanilla instance through `Traverse` (`Patches/Time2WorkPatches.cs:34-42`), plus `false`-returning prefixes on eight of its public accessors (`:59-169`). `ClimateSystem.SampleClimate` is prefixed and rebuilt at a rescaled time (`:171-191`). `TimeSettingsData.m_DaysPerYear` is multiplied in place by a settings factor by a system registered into both `PrefabUpdate` and `PrefabReferences` (`Systems/TimeSettingsMultiplierSystem.cs:50-52`, `Mod.cs:154-155`).
**`SimulationSystem` and `PlanetarySystem` are not touched at all.** That is the shape of the answer: the tick rate and the sun are not reachable by the route this mod takes, so it changes what a tick *means* instead.

**It is also the corpus's only `GetUpdateOffset` override** — interval 16, offset 11 on `Time2WorkCitizenBehaviorSystem` (`Systems/Time2WorkCitizenBehaviorSystem.cs:63-65`), copying `CitizenBehaviorSystem`'s own values. `mod-lifecycle-and-ordering.md:312` already owns why that copy works and why it is not the general recipe.

**One thing it proves about the vanilla clock and nothing else does.** Its lighting fallback hard-codes `0.29166666` and `0.875` (`Systems/Time2WorkTimeUISystem.cs:63-70`) — exactly `TimeUISystem.cs:176`'s `7f/24f` and `0.875f`. An independent author reading the same code arrived at the same two boundaries, which is weak corroboration that those literals are the operative day window on the UI side.

Two defects in it worth knowing before anyone cites it as a model: its `ClimateSystem` prefix assigns the **aurora** curve's value to the `fog` channel (`Patches/Time2WorkPatches.cs:180/187`), and `CompanyDisableNightNotificationSystem`'s window is `hour > 23 || hour < 6` where `hour` is an int hour-of-day, so the first clause is unreachable (`Systems/CompanyDisableNightNotificationSystem.cs:26-40`).

### Catalog gap: `Water Features` is the corpus's clean worked example of a day-derived cadence and its entry does not say so

Four of its systems express their interval as updates per simulated day, with a named per-system constant — the clearest demonstration in the corpus of "pick a cadence in updates-per-day, not in frames":

- `Water_Features/Water_Features/Systems/SeasonalStreamsSystem.cs:36` — `public static readonly int kUpdatesPerDay = 128;`, used at `:62-64` as `return 262144 / kUpdatesPerDay;`
- `Water_Features/Water_Features/Systems/AutomatedWaterSourceSystem.cs:32-42` — `UpdatesPerDay = 1024`, under the doc comment *"Used to calcuate how many times this system runs during a simulated game day."*
- `Water_Features/Water_Features/Systems/DetentionBasinSystem.cs:40-42` and `RetentionBasinSystem.cs:49-51` — the same expression at 128 (`:31` and `:32`).

**Sentence to add to `Water Features`' Demonstrates block** (`mod-catalog.md:279` area):

> Update cadence expressed as updates per simulated day rather than as a frame count — a per-system constant divided into the day's tick count, the same idiom across four systems, so the number in the source reads as a rate instead of a period.

**Two further catalog observations, neither proposed as an edit.** `Info Loom`'s existing sentence (`mod-catalog.md:324`) already teaches the acting rule — an interval on a UI-phase system does nothing — and a second sentence about the same file's `262144 / (kUpdatesPerDay * 16)` arithmetic buys nothing. And `Tree Controller` declares `public const int UPDATES_PER_DAY = 32;` under the comment *"Relates to the update interval although the GetUpdateInterval isn't even using this"* while returning a bare `512` (`Tree_Controller/.../DeciduousSystem.cs:29-33/51-53`, and identically in `FindTreesAndBushesSystem.cs:29/48`). That is a defect in the mod rather than a technique.

### Source-list gap: `docs/SOURCES.md` entry 3 does not say the bundle is the only source for the unit semantics

Entry 3 credits the UI bundle with *"every number and string formatter"*, which is true and undersells what that means for a mechanics topic. The formatter table is the **only** first-party statement of what a C# field's magnitude is in: nothing in the decompile says `ElectricityConsumption` is in hundreds of watts, and `Game.UI.Unit`'s constants are opaque strings. **Proposed amendment to entry 3**, appended to its authority paragraph:

> It is also the only first-party statement of what a C# value's *magnitude* means. `Game.UI.Unit`'s constants are opaque strings; the divisor and the threshold that turn one into kilowatts or tonnes exist only in the bundle's formatter table, so a question of the form "what unit is this field in?" is answered here and nowhere else.

No other entry needed correcting for this topic.

---

## Bridge

**The boundary against `mod-lifecycle-and-ordering`, stated from this side.** That reference owns *registering* a system at an interval: the `GetUpdateInterval` / `GetUpdateOffset` hooks, the power-of-two throw, the phases that consult the interval and the three that do not, the fork-and-anchor recipe, and the offset-inheritance rule. **This reference owns what the resulting cadence is worth in simulated time**: that the mask input is a 262144-frame day, that interval N means `262144 / N` passes a day, that `UpdateFrame` sharding divides a per-entity rate by sixteen, and what a pass is therefore worth as a duration. The two meet at `SimulationSystem.frameIndex`, and a reader needs both to write a correct rate. The mask expression itself is transcribed in both because it is the seam.

**The seam with `units-and-formatting`.** That technique topic owns *rendering* — the `LocalizedNumber` / `LocalizedFraction` / `LocalizedBounds` components, the `cs2/l10n` exports, the interface unit preferences as a settings surface, and how a mod formats its own value. **This reference owns the unit's meaning**: which physical quantity a raw C# value is denominated in, and the conversion thresholds that are part of the mechanic rather than of the presentation — 100 kg to tonnes, 1e6 to kilotonnes, `/10` to kilowatts, 1e3 m to kilometres. The full table sits here because a mechanics reader needs it to interpret a component field they will never render. `units-and-formatting` takes the table as given and teaches the call.
`transportation-and-vehicles.md:109` bridges to `units-and-formatting` for display; `utilities-and-flow-networks.md:119` and `production-and-sources.md:147` bridge the `85` conversion here, since "what it is" lands here.

**The seam with `environment-and-pollution`.** That reference owns what the weather and the seasons *do* — the climate sampler, the season's effect on temperature, precipitation, snow and wetness, and the `Climate` cell maps. **This reference owns what a season is worth in simulation time**: `SeasonInfo.m_StartTime` as a year fraction, the year as `262144 * m_DaysPerYear` frames, the season boundary as a wrap-around walk over an unordered array, and the day-night boundary table. Its climate facet (`environment-and-pollution/climate-and-weather.md:33/89-92`) already routes both questions here in writing and the routing is correct.

### Mechanics references that state a rate and take their meaning from here

The shipped mechanics directory is nine reference *families*, 63 files. **58 carry a rate, cadence, interval or unit claim.** Listed by family with what each takes; a facet is named where the family hub does not carry the claim itself.

- **`citizens-and-households`** — already bridges here (`:144`). Its hub states the `262144 / GetUpdateInterval` identity as its own rule (`:114-115`) and routes `TimeSettingsData.m_DaysPerYear` conversion here (`:59`). Facets: `lifecycle` (age thresholds in days, `TimeSystem.GetDay`, `m_BirthDay` as a day number), `education-pipeline` (128 passes per day, the `/128` fee quantization), `employment-and-wages` (a `±10922/262144`-of-a-day work offset — that is the in-game hour), `happiness` (once per 256 frames per citizen), `crime-pipeline` (`m_JailTime` stored as `duration * 262144 / 256`), `travel-and-trips` (`m_ConsumptionPerDay`).
- **`city-services-and-coverage`** — already bridges here (`:155`). Facets: `budget-workforce-and-upkeep` (a whole `## Cadence` section naming the `262144 / (kUpdatesPerDay * 16)` idiom), `coverage` (the 256-frame eight-service rotation, and `m_Range` in metres), `dispatch` (the 1/3/7/…/255-update backoff, which is only a duration once you know what an update tick is), `fees` (the 2048-frame interval as the per-day-to-per-charge divisor), `parks-leisure-and-telecom` (`kUpdatesPerDay` 256 and 512, a 4096-frame map rebuild).
- **`city-state-and-progression`** — already bridges here (`:140`). Facets: `statistics` (its whole trap is that the 8192-frame sample cadence is four unrelated literals and one sample is a thirty-second of a day), `progression` (`262144 / kUpdatesPerDay` written longhand), `policies` (interval 256).
- **`economy-and-companies`** — already bridges here (`:141`). Its hub carries the sharpest statement of the two `kUpdatesPerDay` conventions (`:89-90`), which is this reference's material restated. Facets: `production-and-profit` (`kCompanyUpdatesPerDay = 256`, and `65536` named as a quarter game day), `taxes-and-budget` ("1024 times a game day", every loan figure daily), `tourism-and-lodging` (`GetTouristRandomStay()` returning the literal 262144 = one game day), `trade-and-restocking`, `extraction-and-depletion`.
- **`environment-and-pollution`** — already bridges here (`:127`). Facets: `climate-and-weather` (the year, the season model, the day-night boundaries — the densest overlap in the set), `emission` (`pollution / kUpdatesPerDay` budgets, "per second" emission), `map-dynamics` (metres per update, 128 updates per day), `natural-resources` (`percentPerDay / kUpdatesPerDay`), `disasters` (`m_Duration * 60` — real seconds, not days), `water-and-groundwater` (per-update rates with no interval named), `cell-maps` (`kMapSize = 14336` metres).
- **`roads-and-traffic`** — **no bridge to this topic today, and it needs one.** Its hub states the km/h-to-m/s bake (`:90-91`), and its facets state cadences throughout: `congestion-and-blockage` (a 512-frame tick over sixteen groups = each lane 32 times a day, and `Road`'s four day-quarter slots — blended by a tent centred on this reference's `normalizedTime * 4`), `pathfind-queue` (transcribes the whole `SimulationSystem` step clamp), `accidents` (4096-frame interval), `junctions` (m/s, a 64-frame period), `route-selection` and `travel-weights` (the `time` cost axis denominated in `length / speed`), `intercity-traffic` (`m_SpawnRate * 4.266667f`), `parking` (metres on a repurposed field).
- **`transportation-and-vehicles`** — already bridges here (`:108`) and to `units-and-formatting` (`:109`). Facets: `depots-and-dispatch` (states the `262144 / 256` division as days of maintenance), `lines-and-fleet` (names this reference as the owner of what a frame is, at `:94`, and carries the `0.25 / 11f/12f` night window), `transit-routing` (`PublicTransportDay` vs `Night` on the same window), `stops-and-boarding` (256-frame intervals, five-second quantisation), `vehicles` (km/h→m/s, degrees→radians).
- **`utilities-and-flow-networks`** — already bridges here (`:119`), the `85` conversion included. Facets: `solve-cycle` (the densest cadence file shipped — `kTicksPerDay`, `GetUpdateFrame`, `frameIndex % 128`, `kFullUpdatesPerDay`), `consumption-and-dispatch` (2048 as the per-tick-to-per-day divisor, written as a bare literal), `production-and-sources` (`kUpdatesPerHour = 85` as the power-to-energy conversion), `flow-graph`.
- **`zoning-buildings-and-land-value`** — **no bridge to this topic today, and it needs one.** Its hub states that a system's interval is not how often one building is touched (`:194`), which is this reference's `UpdateFrame` material. Facets: `level-up-loop` (restates `262144 / GetUpdateInterval` verbatim and carries five `kUpdatesPerDay` systems), `demand` (`kUpdateInterval = 16` with staggered offsets 1/4/7/10/13 — the one place in the shipped set where an explicit offset is the mechanism), `building-spawning` (interval 16, offset 13), `zone-blocks-and-cells` (`CELL_SIZE = 8f` metres).

**Five files carry no rate, cadence, interval or unit claim** and need nothing from here: `city-state-and-progression/notifications.md`, `city-state-and-progression/unlocking.md`, `city-state-and-progression/map-tiles.md`, `roads-and-traffic/network-rebuild.md`, `zoning-buildings-and-land-value/districts-and-themes.md`. Three of them do state something about the frame — but as an *ordering* unit inside one simulation step ("all the update markers are one-frame tags"), not as a cadence. That distinction is worth keeping: a bridge naming all 63 slugs teaches nothing about which the topic actually settles.

### Techniques a change here needs

- **`mod-lifecycle-and-ordering`** — the boundary above, plus the registration mechanics for anything that reads the clock. A system reading `TimeSystem.normalizedTime` must be in a phase where `TimeSystem` has already run this frame: `UpdateAt<TimeSystem>(SystemUpdatePhase.GameSimulation)` (`SystemOrder.cs:358`) puts it early in that phase, and `PostDeserialize<TimeSystem>` in `Deserialize` (`:860`) is what makes the clock valid before the first simulation frame of a load.
- **`ecs-in-this-game`** — the read shapes. `TimeSettingsData` is `GetSingleton<T>` on a **prefab** entity; `TimeData` is `GetSingleton<T>` on a plain entity, or `TimeData.GetSingleton(EntityQuery)` which handles the empty case; `SimulationSystem`, `TimeSystem` and `PlanetarySystem` are managed systems reached with `GetExistingSystemManaged`, not components.
- **`prefabs-and-assets`** — `TimeSettingsPrefab` is a `PrefabBase` whose `LateInitialize` writes the component, so a mod changing the year length either edits the prefab before initialisation or writes `TimeSettingsData` after it. Also where the Unity-serialized-default trap for `ClimatePrefab` and `SeasonInfo` belongs mechanically.
- **`save-serialization`** — `SimulationSystem` serializes `frameIndex` and `TimeData` serializes all five of its fields, so the clock survives a save and the epoch travels with the city. A mod storing an absolute frame number in its own save data is storing a value that only means anything against that save's `m_FirstFrame`.
- **`patching`** — the only route to `TimeSystem`'s arithmetic, since its methods are instance methods on a system nothing else can substitute for. `Time2Work`'s method-by-method prefix set is the worked example of what that costs.
- **`diagnostics`** and **`debug-menu`** — the developer menu is the cheapest clock instrument the game ships. `DebugSystem.cs:1265-1268` is the eight-speed radio; `:1783-1855` exposes `PlanetarySystem`'s latitude, longitude, day, time, lunar cycles, `overrideTime` and `debugTimeMultiplier` as live widgets; `:1860-1882` are three buttons calling `TimeSystem.DebugAdvanceTime(60 | 720 | 8640)`, which advances the clock by rewinding the epoch: `m_FirstFrame -= (uint)(minutes * 262144) / 1440u` (`TimeSystem.cs:155-161`). The `"Weather & climate"` foldout at `:1648-1660` reports the current climate and season prefab names.
- **`performance-and-memory`** — the frame-budget clamp at `SimulationSystem.cs:246-253` is where a mod's per-frame cost turns into a slower game clock rather than a lower frame rate, and `frameDuration` is the game's own measurement of it.

### The UI skill

- **`binding-layer`** — the `time` group's nine members above, with `TimeSettings`'s `__Type` string `Game.UI.InGame.TimeUISystem+TimeSettings`, and the observer-counted `simulationPausedBarrier` event, which is the one binding in this group whose *subscription* is the mechanism rather than its payload.
- **`frontend-and-injection`** — three registry paths a UI mod reaching the clock needs: `game-ui/game/data-binding/time-bindings.ts` (`source.js:45986`) for the bindings and the tick arithmetic, `game-ui/common/localization/unit.ts` (`:29018-29026`) for the `Unit` enum, `game-ui/common/localization/units-us-customary.ts` (`:29027`, functions at `:28937-28969`) for the eleven conversion functions, and `game-ui/common/localization/localized-date.tsx` (`:29896-29943`) for the date, time and duration components `cs2/l10n` does not export.
- **`ui-build-and-devloop`** — nothing specific from here.

---

## Dead ends

- **No system owns the day-night cycle.** Searched `src/Game/` for one: `PlanetarySystem` computes the sun, `LightingSystem` reads its angle to pick a `State`, `EffectFlagSystem` and five other systems each compare `normalizedTime` to their own thresholds.
- **The unit table cannot be reached from the decompile.** `Game.UI/Unit.cs` holds 33 opaque strings and nothing in `src/` says what any of them means. Searched for a C# formatter, a divisor table and a `[Unit]` attribute: none exists. The bundle is the only source, which is the SOURCES amendment above.
- **Nothing reads `kUpdatesPerDay` generically.** Grepped for reflection over the name and for an attribute: nothing; the only shared consumer is `SimulationUtils.GetUpdateFrame`, which takes the value as a plain `int`.
- **The wiki has no page about the simulation clock.** Thirty-seven candidate titles tested through `api.php?action=query&titles=…`; only `Climate` and `Commonly units in the game` exist, with `Seasons` and `Weather` redirecting into `Climate`. A full-text search for `simulation time frameIndex day length units` returns zero. The four constants the wiki does carry are split across three pages that never cite one another.
- **The wiki's units page has no non-time content and never had any.** Its absence from `Category:Potentially outdated` proves nothing, for the reason `docs/SOURCES.md` entry 10 already gives.
- **No corpus mod demonstrates unit conversion.** Swept all 22 repositories for `UnitSettingsData`, unit-system references and number formatting: nothing. The unit table has no corpus cross-check and does not need one, but the absence is worth recording so the next pass does not go looking.
- **No entry was appended to `conflicts.md`.** Everything this topic could not settle is either an `Unconfirmed:` with a named experiment or a dead end above; nothing here is a judgement about what ships that the two existing rulings do not already decide.
