# The clock and its speed

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`SimulationSystem` (`src/Game/Game.Simulation/SimulationSystem.cs`) is registered `UpdateAt<SimulationSystem>(SystemUpdatePhase.LateUpdate)` in `src/Game/Game.Common/SystemOrder.cs`, so the three simulation phases run nested inside one late-update pass, once per rendered frame.

## The step loop

Source: `src/Game/Game.Simulation/SimulationSystem.cs` (`OnUpdate`, `UpdateLoadingProgress`, `OnGamePreload`).

```
frameDuration = stepCount != 0 ? stopwatchTicks / (Stopwatch.Frequency * stepCount) : 0
if m_IsLoading:
    if loadingProgress != 1: UpdateLoadingProgress(); return
    if !GameManager.isGameLoading:
        m_IsLoading = false                                                  // the selectedSpeed setter drops writes while m_IsLoading
        selectedSpeed = gameplay.pausedAfterLoading ? 0 : 1
else if GameManager.isGameLoading: selectedSpeed = 0

if selectedSpeed == 0:
    steps = 0; smoothSpeed = 0
else:
    advance = deltaTime * selectedSpeed
    slackFactor = 1
    if pathfindResultSystem.pendingSimulationFrame < uint.MaxValue:        // pathfind backpressure
        slack = max(0, pendingSimulationFrame - frameIndex - 1)
        slackFactor = min(1, slack * PENDING_FRAMES_SPEED_FACTOR)            // 1f / 48f
        advance *= slackFactor
    m_Timer += advance
    steps = floor(m_Timer * 60)                                              // 60 frames per real second at speed 1
    if backpressure: steps = min(steps, slack)
    if performancePreference != SimulationSpeed:                             // frame-budget clamp
        headroom = (endFrameBarrier.lastElapsedTime - currentElapsedTime) / max(0.001, frameDuration)
        cap = max(1, FrameRate ? floor(headroom) : ceil(headroom))
        steps = min(steps, cap)
    m_Timer = clamp(m_Timer - steps / 60, 0, 1 / 60)
    ceiling = max(1, min(8, round(selectedSpeed * slackFactor * 2)))
    steps = clamp(steps, 0, ceiling)
    smoothSpeed = <a lerp toward the achieved rate, bounded by the change in selectedSpeed>
frameTime = m_Timer * 60
UpdateSystem.Update(PreSimulation)                                           // every rendered frame, even at 0 steps
for i in 0 .. steps - 1:
    frameIndex++
    if actionMode.IsEditor(): UpdateSystem.Update(EditorSimulation, frameIndex, i)
    if actionMode.IsGame():   UpdateSystem.Update(GameSimulation, frameIndex, i)
UpdateSystem.Update(PostSimulation)

UpdateLoadingProgress, once per rendered frame, if m_LoadingCount > 0:      // 1024 on Purpose.NewGame, 0 otherwise
    Update(PreSimulation)
    8 times: frameIndex++; Update(LoadSimulation, frameIndex, i)
    Update(PostSimulation)
    m_LoadingCount -= 8
```

The nominal rate is 60 simulation frames per real second per unit of `selectedSpeed`, from the `* 60f` against a timer accumulating `deltaTime * selectedSpeed`, so a day is `262144 / 60` real seconds at 1x; the achieved rate sits under it by whatever the two clamps take, so frames per real second is a machine fact and only the 60 is a game fact.
A rendered frame carries at most `max(1, round(selectedSpeed * 2))` steps and never more than 8, so writing a larger `selectedSpeed` buys nothing past 8 steps a frame.
A new game burns `1024` frames of `LoadSimulation` before the player sees anything; a loaded save burns none.

**`PreSimulation` and `PostSimulation` run once per rendered frame, not once per simulation frame, and keep running while paused.**
A mod registering there to do per-tick work gets render-rate work instead, and a duration it accumulates there tracks wall time.
Source: `src/Game/Game.Simulation/SimulationSystem.cs`.

## The speeds

`TimeUISystem` publishes `simulationSpeed` as an index and converts with `IndexToSpeed(i) = pow(2, clamp(i, 0, 2))` and `SpeedToIndex(s) = s > 0 ? clamp((int)log2(s), 0, 2) : 0`, so the three buttons are speeds 1, 2 and 4 (`src/Game/Game.UI.InGame/TimeUISystem.cs`).
`DebugSystem`'s `"Sim speed"` radio writes `selectedSpeed` from `kDebugSimulationSpeedValues = { 0, 0.125, 0.25, 0.5, 1, 2, 4, 8 }`, `static readonly` (`src/Game/Game.Debug/DebugSystem.cs`); from 4 up the per-frame ceiling already sits at its 8-step cap.
`PhotoModeRenderSystem`'s `"Simulation Speed"` property is a `0`–`8` slider over `selectedSpeed` whose reset writes `0`, and `ClimateRenderSystem`'s debug path writes `0` (`src/Game/Game.Rendering/`).

**`0` on the `time.simulationSpeed` binding is speed index 0, never "paused".**
`GetSimulationSpeed` returns `SpeedToIndex(m_SpeedBeforePause)` while paused, so the index is the pre-pause speed; `time.simulationPaused` is the boolean that says paused.
Source: `src/Game/Game.UI.InGame/TimeUISystem.cs`.

## The pause barrier

`TimeUISystem` holds an `EventBinding<bool>` on `time.simulationPausedBarrier`, and while any frontend observer is subscribed to it — or the platform reports the app unfocused — its `OnUpdate` writes `selectedSpeed = 0` every UI update, remembering `m_UnpausedBeforeForcedPause` and restoring `m_SpeedBeforePause` when the barrier lifts.
`SetSimulationSpeed` checks the barrier and writes `m_SpeedBeforePause` instead of the live speed while it is up; `SetSimulationPaused` under the barrier only records `m_UnpausedBeforeForcedPause`.

**A mod's write to `SimulationSystem.selectedSpeed` while a modal UI holds the barrier is captured into `m_SpeedBeforePause` and zeroed on the next UI update, then restored when the barrier lifts, with nothing logged.**
`OnUpdate` copies any positive `selectedSpeed` into `m_SpeedBeforePause` before forcing zero, so the write lands once the barrier drops rather than when it was made; `time.setSimulationSpeed` routes into the same private field.
Source: `src/Game/Game.UI.InGame/TimeUISystem.cs`.

## The epoch and the save

`SimulationSystem` serializes `frameIndex` alone, and its `SetDefaults` zeroes it; `EntityDeserializer` calls `SetDefaults` only on the systems the loaded stream did not carry (`src/Colossal.Core/Colossal.Serialization.Entities/EntityDeserializer.cs`).
`TimeSystem.PostDeserialize` creates the `TimeData` entity when the save has none and, on `Purpose.NewGame` only, writes `m_FirstFrame = m_SimulationSystem.frameIndex` and `m_StartingYear = startingYear` (`src/Game/Game.Simulation/TimeSystem.cs`).
`TimeSystem.GetTicks` casts `frameIndex - m_FirstFrame` to `int`, so the calendar holds for 2^31 frames since founding.

## The rendering clock

`RenderingSystem.frameIndex` and `frameTime` (`src/Game/Game.Rendering/RenderingSystem.cs`) are a second counter, set while unpaused to `m_SimulationSystem.frameIndex + offset` each rendered frame where the offset is a small signed interpolation around `smoothSpeed`, frozen wherever they were while `selectedSpeed` is `0` unless the debug `frameOffset` changes, and reset to the simulation's exactly in `PostDeserialize`.
It also derives shader time as `frameIndex % uint2(60, 3600) + frameTime` and `frameIndex % 216000 + frameTime` — real-time-equivalent seconds, minutes and an hour of frames, not in-game ones.

**A simulation decision reads `SimulationSystem.frameIndex`; a visual reads `RenderingSystem.frameIndex` with its `frameTime`.**
`PlanetarySystem` and `ClimateRenderSystem` take the rendering one, every system in a simulation phase the simulation one, and a simulation system reading the rendering clock makes its result depend on frame rate.
Source: `src/Game/Game.Rendering/RenderingSystem.cs`, `src/Game/Game.Simulation/PlanetarySystem.cs`.

(VOLATILE: every system, property, field, constant, enum, method, binding name, quoted widget label and `Source:` path this file names — their declarations under `src/Game/` in the root `Game` namespace, `Game.Simulation`, `Game.Common`, `Game.Pathfind`, `Game.UI.InGame`, `Game.Debug`, `Game.Rendering`, `Game.Settings` and `Game.SceneFlow`, `EntityDeserializer` under `src/Colossal.Core/` and `EventBinding` under `src/Colossal.UI.Binding/`, at the files the sections cite.)
