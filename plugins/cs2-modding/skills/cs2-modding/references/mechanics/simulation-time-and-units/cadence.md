# Cadence

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

A system's interval and offset come from `GameSystemBase.GetUpdateInterval(phase)` and `GetUpdateOffset(phase)`, virtual with defaults `1` and `-1` (`src/Game/Game/GameSystemBase.cs`).
Registering one, and the power-of-two throw that fires there, is [`mod-lifecycle-and-ordering`](../../technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md)'s.

## The mask

Source: `src/Game/Game/UpdateSystem.cs` (`Update(phase, updateIndex, iterationIndex)`, `Refresh`, `AddSystemUpdate`), `src/Game/Game.Simulation/SimulationSystem.cs`.

```
for each SystemData in the phase's range:
    if (updateIndex & (uint)(m_Interval - 1)) != (uint)m_Offset: continue    // updateIndex is SimulationSystem.frameIndex
    if m_ResetInterval <= iterationIndex: system.ResetDependency()
    system.Update()

Refresh, for a system whose offset is negative:
    interval == 1                      → offset 0
    ordered before or after a system of the same interval and phase
                                       → inherits that system's offset, declared or assigned
    otherwise                          → an offset assigned by a bit-reversal walk over the systems sharing its interval, spreading them across residues
```

The three-argument `Update` is called from `LoadSimulation`, `EditorSimulation` and `GameSimulation` only; the one-argument `Update(phase)` never reads `m_Interval`, so an interval override on a system in any other phase is inert and the system runs every call.

**A system runs on the frames where `frameIndex & (interval - 1) == offset`, compared against the offset and not against zero.**
A mod counting "frames since I last ran" as `interval` is right; one assuming it runs when `frameIndex % interval == 0` is off by its assigned offset, which `Refresh` chose and which it never declared.
Source: `src/Game/Game/UpdateSystem.cs`.

## What an interval is worth

Source: `src/Game/Game.Simulation/SimulationUtils.cs`, `src/Game/Game.Simulation/AgingSystem.cs`, `src/Game/Game.Simulation/BirthSystem.cs`.

```
passes per day            = 262144 / interval
one pass                  = interval / 262144 of a day
                          = interval / 60 real seconds at speed 1

the vanilla idiom:  public static readonly int kUpdatesPerDay = N;
                    GetUpdateInterval(phase) => 262144 / kUpdatesPerDay
                    or, sharded:              262144 / (kUpdatesPerDay * 16)

GetUpdateFrame(frame, updatesPerDay, groupCount) = (frame / (262144 / (updatesPerDay * groupCount))) & (groupCount - 1)
GetUpdateFrameWithInterval(frame, interval, groupCount) = (frame / interval) & (groupCount - 1)
GetUpdateFrameRare(frame, daysPerUpdate, groupCount) = (frame / (daysPerUpdate * 262144 / groupCount)) & (groupCount - 1)

a sharded system with interval 262144 / (kUpdatesPerDay * groupCount) runs kUpdatesPerDay * groupCount times a day and touches each entity kUpdatesPerDay times, the entities whose UpdateFrame.m_Index equals GetUpdateFrame(frameIndex, …) on that pass
```

`AgingSystem` declares `kUpdatesPerDay = 1` and returns `262144 / (kUpdatesPerDay * 16)`, so it runs sixteen times a day and ages each citizen once; `BirthSystem` does the same at `16`, and scales a per-day probability by `/ kUpdatesPerDay` inside the job.
Since an hour is `262144 / 24` frames and not a power of two, no interval is exactly an hour: the nearest intervals are `8192` (`32` passes a day, forty-five in-game minutes) and `16384` (`16` passes, ninety).

**`kUpdatesPerDay` is a naming convention, not a mechanism.**
Nothing reads the field by reflection or through a shared helper; a system divides it into `262144` itself, or returns the quotient as a bare literal, or shards by it — `BuildingEfficiencySystem` declares `512`, returns `32` and re-types `512` at its `GetUpdateFrame` call.
Read `GetUpdateInterval`, never the constant.
Source: `src/Game/Game.Simulation/ElectricityFlowSystem.cs`, `src/Game/Game.Simulation/CityStatisticsSystem.cs`, `src/Game/Game.Simulation/BuildingEfficiencySystem.cs`.

## The solver hour

`ElectricityFlowSystem` declares `kUpdateInterval = 128`, `kUpdatesPerDay = 2048` and `kUpdatesPerHour = 85` as `const` and gates its phases on `frameIndex % 128` rather than through `GetUpdateInterval` (`src/Game/Game.Simulation/ElectricityFlowSystem.cs`).
`85` is `2048 / 24` truncated: `Battery.storedEnergyHours => m_StoredEnergy / 85` and `BatteryData.capacityTicks => 85 * m_Capacity` (`src/Game/Game.Buildings/Battery.cs`, `src/Game/Game.Prefabs/BatteryData.cs`) are an in-game hour measured in solver ticks.

## The three duration conventions

A prefab field holding a duration is in days, in real seconds, or in frames, and the field name does not say which; the multiplier at the use site does.
`× 262144` is days: `InitializeSystem`'s event `m_PreparationDuration` and `m_ActiveDuration`, the `EconomyParameterData.m_RoadRefundTimeRange` and `m_BuildRefundTimeRange` windows in `NetUtils` and `ObjectUtils`, `PoliceCar.m_ShiftDuration` baked into `PoliceCarData`, and `CreateChirpSystem`'s `m_ActiveDays` into `Chirp.m_InactiveFrame`; `262144 / 4` is the quarter-day one event family uses (`src/Game/Game.Events/InitializeSystem.cs`, `src/Game/Game.Net/NetUtils.cs`, `src/Game/Game.Objects/ObjectUtils.cs`, `src/Game/Game.Prefabs/PoliceCar.cs`, `src/Game/Game.Triggers/CreateChirpSystem.cs`).
`× 60f` is real seconds at speed 1: a disaster's `m_Duration` and its `DisasterWarningTime` modifier, in the same `InitializeSystem`.
No multiplier is frames: `RecentClearSystem` clears a `Recent` tag at `m_SimulationFrame - 262144`, one day, and every `m_StartFrame` / `m_EndFrame` pair on an instance is already a frame (`src/Game/Game.Tools/RecentClearSystem.cs`).

(VOLATILE: every system, component, field, property, method, constant, enum and `Source:` path this file names — their declarations under `src/Game/` in the root `Game` namespace, `Game.Simulation`, `Game.Buildings`, `Game.City`, `Game.Prefabs`, `Game.Events`, `Game.Net`, `Game.Objects`, `Game.Tools` and `Game.Triggers`, at the files the sections cite.)
