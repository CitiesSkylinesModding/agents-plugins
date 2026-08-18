# Disasters

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

A disaster is an ordinary event entity: an `EventPrefab` carrying `Game.Prefabs.WeatherPhenomenon` declares `WeatherPhenomenonData` as its prefab component and `Game.Events.WeatherPhenomenon`, `HotspotFrame`, `Duration`, `DangerLevel`, `TargetElement` and `InterpolatedTransform` as its archetype, and `EventData.m_Archetype` is what a spawner instantiates (`src/Game/Game.Prefabs/WeatherPhenomenon.cs`).
Which event prefabs exist is an install's content set, DLC included — enumerate with `ecs_query` on `Game.Prefabs.EventData`; the weather disasters are the `WeatherPhenomenonData` carriers and the water ones the `WaterLevelChangeData` carriers.

## Spawn

Source: `src/Game/Game.Simulation/WeatherHazardSystem.cs`.

```
every 2048 frames, per event prefab with EventData + WeatherPhenomenonData:
skip when the prefab's Locked is enabled          // progression gate; Locked is enableable,
                                                  // so HasComponent is true either way
skip when m_DamageSeverity != 0 and natural disasters are switched off   // the city setting
tFactor = max(0, 1 − ((temperature − centre(m_OccurenceTemperature)) / max(0.5, extents))²)
rFactor and cFactor, from m_OccurenceRain and m_OccurenceCloudiness:
    a range of exactly (0,0)          → 1        // no constraint
    max > 0.999 and min >= 0.001      → saturating ramp upward from min
    max <= 0.999 and min < 0.001      → saturating ramp downward to max
    open at both ends (such as (0,1), the authoring default) → 1
    closed at both ends               → 1
p = m_OccurenceProbability * tFactor * rFactor * cFactor * m_TimeDelta
while random.NextFloat(100) < p:  create the event;  p −= 100
```

**Only a range open at exactly one end constrains the roll.**
A rain or cloudiness window closed at both ends — or open at both, which is the authoring default — is silently ignored, and the phenomenon spawns as if the window were absent.
Source: `src/Game/Game.Simulation/WeatherHazardSystem.cs`.

**A phenomenon with zero `m_DamageSeverity` spawns even with natural disasters switched off.**
The setting's gate tests `m_DamageSeverity != 0`, which is why harmless weather events keep firing under it.
Source: `src/Game/Game.Simulation/WeatherHazardSystem.cs`.

## Initialisation and tick

Sources: `src/Game/Game.Events/InitializeSystem.cs`, `src/Game/Game.Simulation/WeatherPhenomenonSystem.cs`, `src/Game/Game.Events/EventUtils.cs`, `src/Game/Game.Simulation/WeatherDamageSystem.cs`.

```
initialise:
    radii rolled from the prefab's ranges, the hotspot as a fraction of the phenomenon radius
    m_StartFrame delayed by CityModifierType.DisasterWarningTime, only when m_DangerFlags != 0
        // that delay is the early-warning window: the entity exists, the disaster has not begun
    m_EndFrame = m_StartFrame + random(m_Duration) * 60     // m_Duration is seconds
    position = first TargetElement with a Transform, else random xz in ±6000
    early-warning buildings stamped only when the prefab carries EarlyDisasterWarningEventData
tick, with num = 4/15 seconds per update (one update every 16 frames):
    intensity ramps ±0.2 * num, clamped to [0,1] — up between the frames, down past m_EndFrame,
        but the delete below lands on the first update past it, so the down-ramp is at most two ticks
    while intensity != 0 — so nothing moves or damages during the early-warning window:
        position.xz += 20 * Wind.SampleWind(windMap, position) * num, re-grounded to water-or-terrain height
        the hotspot chases the centre with an instability term and a radial correction keeping it inside
        damage only when m_DamageSeverity != 0; lightning on its own timer;
            traffic accidents when the prefab also carries TrafficAccidentData
    unconditionally, intensity 0 included — which is what makes the early-warning window work:
        endangerment (below); DangerLevel = the prefab's m_DangerLevel between its frames, 0 otherwise
        past m_EndFrame: Deleted, and every early-warning building gets EffectsUpdated
severity and damage:
    severity   = m_Intensity * m_DamageSeverity * (1 − distance(position, hotspot) / m_HotspotRadius)
    damageRate = severity / structuralIntegrity     // integrity >= 1e8 is the indestructible sentinel
    buildings scale by CityModifierType.DisasterDamageRate; then rate = min(0.5, rate * 1.0666667)
    an object with a Damaged component accumulates rate into m_Damage.x, capped at 1;
        a Destroy event is created when its total damage reaches exactly 1
    an object without one gets a Damage event carrying the rate instead
```

Endangerment is geometric: `FindEndangeredObjects` builds a segment from the phenomenon centre along the wind for `DisasterWarningTime` seconds minus the time still remaining before the start frame — zero as the warning opens, the full window once the disaster begins — and everything within the phenomenon radius of it gets an `Endanger` event: the early-warning building literally extends a lookahead downwind.
`InDanger` marks the endangered object; what a citizen does with `DangerFlags` belongs to the evacuation half of `citizens-and-households`.

**Ticking both authoring booleans yields `StayIndoors` only.**
The prefab exposes `m_Evacuate` and `m_StayIndoors`, and its initialiser assigns the flags in sequence rather than or-ing them, so the second assignment wins.
Source: `src/Game/Game.Prefabs/WeatherPhenomenon.cs`.

**`Game.Prefabs.EarlyDisasterWarningSystem` is not a system.**
It is a two-method `ComponentBase` adding the `Game.Buildings.EarlyDisasterWarningSystem` tag; the mechanism lives in `Game.Events.InitializeSystem` and `WeatherPhenomenonSystem`, and the name sends a reader to the wrong file.
Source: `src/Game/Game.Prefabs/EarlyDisasterWarningSystem.cs`.

## Fire

`FireHazardSystem` (`src/Game/Game.Simulation/FireHazardSystem.cs`) accrues `noRainDays` (reset to zero on rain) and rolls a hazard for anything with `Building` or `Tree`, but the climate factor — `FireConfigurationPrefab.m_TemperatureForestFireHazard` times `m_NoRainForestFireHazard`, evaluated in `src/Game/Game.Simulation/EventHelpers.cs` — multiplies the tree path only; a building's hazard comes from its own prefab data, zone multiplier, fire-rescue coverage and district modifiers, with no weather term.
`FireSimulationSystem` applies the same forest factor to spreading fires; `OnFire` is the marker, and dispatch and rescue belong to `city-services-and-coverage`.

## The flood that cannot fire

Sources: `src/Game/Game.Simulation/SoilWaterSystem.cs`, `src/Game/Game.Simulation/WaterLevelChangeSystem.cs`, `src/Game/Game.Common/SystemOrder.cs`.

`SoilWaterSystem` carries a complete rain-flood mechanism:

```
rain    = max(0, pow(2 * max(0, precipitation − 0.5), 2))
counter = max(0, 0.98 * counter + 2 * rain − 0.1)         // on the FloodCounterData singleton
counter > 20 and no flood exists → create a Flood event from the flood prefab
a flood exists and counter == 0  → delete it
otherwise                        → WaterLevelChange.m_Intensity = max(0, (counter − 20) / 80)
```

It never runs: the system's only `SystemOrder.cs` entry is a `Deserialize`-phase ordering, so `OnUpdate` is registered into no update phase and the soil-water map stays all zeros.
The other half of the absence is `WaterLevelChangeSystem`, which drives `WaterLevelChangeType.Sine` with a full two-phase schedule — reserving `TsunamiEndDelay` frames at the end for the wave to cross the map — and executes an empty branch for every other member of `WaterLevelChangeType`.
So a `RainControlled` water-level event has no creator (the counter above never ticks) and no animator (the empty branch); which change type an install's flood and tsunami prefabs carry is `WaterLevelChangeData.m_ChangeType`, read live.
Registering the system into `GameSimulation` is not enough: a loaded city's map reads all zeros and the diffusion's `0/0` silently no-ops, so a reviving mod first re-seeds each cell's `m_Amount` and `m_Max` (the `OnCreate` values) — the mechanism then runs.
It also ships no `GetUpdateInterval`, so a bare registration runs every tick — 256× the `kUpdatesPerDay = 1024` design cadence, draining the counter in seconds and over-creating floods until the first command-buffer playback — and the mod throttles it itself.

(VOLATILE: every system, component, field, enum, flag, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Events`, `Game.Prefabs`, `Game.Buildings`, `Game.Common`, `Game.City`, `Game.Objects` and `Game.Rendering`, at the files the sections cite.)
