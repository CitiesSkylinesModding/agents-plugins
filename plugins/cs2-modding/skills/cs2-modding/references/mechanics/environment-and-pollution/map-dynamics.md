# Map dynamics

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

## Air: advected by wind, diffused by a shift, faded by a flat subtraction

Sources: `src/Game/Game.Simulation/AirPollutionSystem.cs`, `src/Game/Game.Simulation/WindSystem.cs`.

```
every 2048 frames, one serial IJob over all cells:
scratch[i] = bilinear sample of the air map at cellCentre − m_WindAdvectionSpeed * (wind.x, 0, wind.y)
    // semi-Lagrangian backtracking to the upwind point; m_WindAdvectionSpeed is metres per update
then, scratch into the live map:
p  = scratch[i]
p += scratch[left]  >> kSpread          // kSpread = 3; an off-map neighbour contributes 0
p += scratch[right] >> kSpread
p += scratch[down]  >> kSpread
p += scratch[up]    >> kSpread
p -= scratch[i] >> (kSpread - 2)        // half of itself
p -= RoundToIntRandom(m_AirFade / kUpdatesPerDay)
map[i] = clamp(p, 0, 32767)
```

The diffusion is not conservative — each cell gives away half and receives four eighths, which balances only on a uniform field, so a peak loses mass and a boundary cell loses it outright.
The fade is a flat subtraction rather than a decay, stochastically rounded so the field does not quantise into a staircase.
Air pollution has no rain term, no temperature term and no climate input of any kind: the system holds a `WindSystem` and a `SimulationSystem` and nothing else; the debug menu's reset action (`Game.Debug.DebugSystem`) zeroes the map outright.

## Ground: fade and nothing else

Source: `src/Game/Game.Simulation/GroundPollutionSystem.cs`.

```
every 2048 frames, per cell:
if m_Pollution > 0:
    m_Pollution = max(0, m_Pollution − RoundToIntRandom(m_GroundFade / kUpdatesPerDay))
```

No diffusion, no advection, no interaction with water or terrain: ground pollution sits exactly where it was stamped and decays linearly; the same debug reset zeroes this map too.

## Noise: rebuilt from scratch every update

Sources: `src/Game/Game.Simulation/NoisePollutionSystem.cs`, `src/Game/Game.Simulation/NoisePollution.cs`.

```
every 2048 frames, two parallel jobs:
swap:  m_Pollution = temp[centre]/4 + (temp of N,S,E,W)/8 + (temp of the 4 corners)/16
       // weights sum to 1: a uniform field is preserved, a point source spread without gain;
       // off-map neighbours read 0
clear: every m_PollutionTemp = 0
```

The noise map is a snapshot of the current emitters, blurred once — remove a noisy building and its noise is gone at the next update, with no fade.
Both emission systems complete one full pass per swap period, so every emitter contributes exactly once per snapshot; `m_PollutionTemp` is serialized anyway, because a save can land mid-accumulation.

**A mod writing noise writes `m_PollutionTemp`, never `m_Pollution`.**
`NoisePollution.Add` sets the temp field; anything written to `m_Pollution` is overwritten at the next swap, and anything written to `m_PollutionTemp` is consumed and cleared.
Source: `src/Game/Game.Simulation/NoisePollution.cs`, `src/Game/Game.Simulation/NoisePollutionSystem.cs`.

## Plants: a saturating sickness accumulator

Sources: `src/Game/Game.Simulation/ObjectPolluteSystem.cs`, `src/Game/Game.Objects/ObjectUtils.cs`.

```
per plant, once per 8192 frames — ObjectPolluteSystem.kUpdatesPerDay = 32, sharded 16 ways,
    a quarter of the maps' 128 updates per day:
Plant.m_Pollution = saturate(m_Pollution + (m_PlantGroundMultiplier * ground
                                          + m_PlantAirMultiplier   * air
                                          − m_PlantFade) / kUpdatesPerDay)
```

`Plant.m_Pollution` scales a tree's growth and wood yield as `* (1 − m_Pollution)` in `ObjectUtils`: pollution sickens a plant toward zero yield and never deletes it.

## Terrain attractiveness: a neighbourhood max over forest and shore

Sources: `src/Game/Game.Simulation/TerrainAttractivenessSystem.cs`, `src/Game/Game.Simulation/TerrainAttractiveness.cs`.

```
prepare: cache (waterDepth, terrainHeight, forestAmbience) per cell
per cell, over the neighbourhood within max(m_ForestDistance, m_ShoreDistance):
    m_ForestBonus = max of saturate(1 − dist / m_ForestDistance) * forestAmbienceThere
    m_ShoreBonus  = max of saturate(1 − dist / m_ShoreDistance)  * (waterDepthThere > 2 ? 1 : 0)
EvaluateAttractiveness = m_ForestEffect * forest + m_ShoreEffect * shore
                       + min(m_HeightBonus.z, max(0, terrainHeight − m_HeightBonus.x) * m_HeightBonus.y)
```

The shore test is a hard 2-metre depth threshold, a C# literal — the same literal the fish derivation applies to water depth ([natural-resources.md](natural-resources.md)).
The forest input is `ZoneAmbienceSystem.GetZoneAmbience(GroupAmbienceType.Forest, …)`, so trees reach attractiveness through the zone-ambience map rather than through this one; `EvaluateAttractiveness` is the public static that feeds `AttractionSystem`.

(VOLATILE: every system, component, field, constant, method and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Objects` and `Game.Debug`, at the files the sections cite.)
