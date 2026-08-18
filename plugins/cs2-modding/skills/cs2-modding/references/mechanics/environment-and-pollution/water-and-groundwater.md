# Water and groundwater

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

## Groundwater: flow, equalisation, replenishment, purification

Sources: `src/Game/Game.Simulation/GroundWaterSystem.cs`, `src/Game/Game.Simulation/GroundWater.cs`, `src/Game/Game.Simulation/GroundWaterPollutionSystem.cs`.

`GroundWater { m_Amount, m_Polluted, m_Max }` are all `short`; `m_Polluted` is an absolute quantity bounded by `m_Amount`, so the concentration is `m_Polluted / m_Amount` and must be derived.
`GroundWaterSystem` runs one serial `IJob` in three passes, each pairing a cell with its +x and +z neighbours:

```
pass 1, pollution equalisation:
    target = bothAmounts > 0 ? amountHere * (pollutedHere + pollutedThere) / (amountHere + amountThere) : 0
    delta  = clamp((target − pollutedHere) / 4,
                   −(amountThere − pollutedThere) / 4,
                    (amountHere  − pollutedHere ) / 4)
    // toward equal concentration, a quarter of the way per pair per update,
    // bounded by the clean water available on each side
pass 2, flow:
    flow = clamp(((amountThere − maxThere) − (amountHere − maxHere)) / 4,
                 −amountHere / 4, amountThere / 4)
    // head is measured against each cell's own m_Max, so a high-capacity cell pulls from
    // a low-capacity one at equal absolute fill; flow carries pollution at the source's concentration
pass 3, per cell:
    m_Amount   = min(m_Amount + flow + ceil(m_GroundwaterReplenish * m_Max), m_Max)
    m_Polluted = clamp(m_Polluted + pollutionDelta − m_GroundwaterPurification, 0, m_Amount)
```

Replenishment is a fraction of the cell's `m_Max` while purification is a flat absolute subtraction — two independent fields on `WaterPipeParameterData`, tied by nothing.
`GroundWaterPollutionSystem` is the contamination inlet: per cell, `m_Polluted += sampledGroundPollution / 200`, clamped to `m_Amount`.
Contamination is therefore per cell, never per deposit, and it spreads to neighbours through the equalisation pass and with the flow, which carries the source's concentration — truncating integer arithmetic throughout, so a small gap stalls outright and a ground-pollution sample under 200 adds exactly zero.

`ConsumeGroundWater` is a public static with an unusual contract: it splits the draw bilinearly across the four cells around the position in proportion to what each holds, preserves each cell's pollution concentration — pumping does not clean the aquifer — and logs a warning rather than failing when asked for more than is available.

`GroundWaterSystem`'s own `SetDefaults` override fills the map from noise only for pre-`Version.timoSerializationFlow` new games — dead code for a current save, which falls through to the base class's zeroing — so a modern map's groundwater is authored map data, painted or imported in the editor as one of its resource layers.
It arrives through `Deserialize` — the brush path (`ApplyBrushesSystem`) runs only in the `ApplyTool` phase, on tool input — and the sampled non-empty cells arrived full, `m_Amount == m_Max`; the editor brushes are how the author painted what the map asset then serializes.

## Surface water: a GPU simulation with a source-entity steering surface

Sources: `src/Game/Game.Simulation/WaterSystem.cs`, `src/Game/Game.Simulation/WaterSimulation.cs`, `src/Game/Game.Simulation/SurfaceWater.cs`, `src/Game/Game.Simulation/WaterSourceData.cs`, `src/Game/Game.Prefabs/WaterSource.cs`.

The fluid step never runs on the CPU: `WaterSystem` is an `IGPUSystem` and `WaterSimulation` is one compute shader whose kernels do the whole simulation.
The CPU face is an asynchronous readback — `WaterSystem.GetSurfaceData(out JobHandle)` yields a surface a job samples as `SurfaceWater { m_Depth, m_Polluted, m_Velocity }`, unpacked from the water texture's `float4` with pollution in `w`.
The write surface is the source list: every entity carrying `Game.Simulation.WaterSourceData` plus a `Transform` is cached one frame and dispatched one kernel each the next, so editing that component is the whole supported write.

```
the cached radius is m_Radius * m_Modifier; a source whose cached radius is 0,
    or which lies outside the terrain bounds, is skipped
if m_Polluted > 0:   Add kernel — inject volume 0.3165952 * SimulationCycleSteps * m_Height
                     per step, at pollution fraction m_Polluted   // m_Height is an output rate
else:                AddConstant kernel — hold the water level at m_Position.y + m_Height,
                     or at lerp(0, −1, m_Height / −150000) when m_Height < 0
```

`m_Modifier` is not serialized and resets to 1 on deserialize.

**`Game.Prefabs.WaterSourceData` and `Game.Simulation.WaterSourceData` are different structs sharing one name.**
The prefab struct (`m_Radius, m_height, m_InitialPolluted`, written by the `WaterSource` authoring component) is copied into the runtime struct by `WaterSourceInitializeSystem`; a using-directive that resolves the wrong namespace compiles and reads the wrong component.
Source: `src/Game/Game.Prefabs/WaterSourceData.cs`, `src/Game/Game.Simulation/WaterSourceData.cs`, `src/Game/Game.Simulation/WaterSourceInitializeSystem.cs`.

## The sewage-to-intake chain

Sources: `src/Game/Game.Simulation/SewageOutletAISystem.cs`, `src/Game/Game.Simulation/WaterPumpingStationAISystem.cs`, `src/Game/Game.Simulation/DispatchWaterSystem.cs`.

`SewageOutletAISystem` writes each of its sub-object water sources: `m_Height = min(2.5, m_SurfaceWaterUsageMultiplier * total)`, `m_Polluted = unpurified / total`, and `m_Modifier = 0` while nothing flows — an on/off gate rather than a deletion.
The GPU advects the plume downstream.
`WaterPumpingStationAISystem` reads back at its own sub-object — `WaterUtils.SamplePolluted` for surface intake, the per-cell concentration `m_Polluted / max(1, m_Amount)` for groundwater intake — and publishes `(1 − WaterPumpingStationData.m_Purification) * weightedPollution / capacity` onto the producer edge as `WaterPipeEdge.m_FreshPollution`.
That edge is the seam: everything downstream of `m_FreshPollution` belongs to `utilities-and-flow-networks`.
`WaterPipeParameterData.m_MaxToleratedPollution` gates the two dirty-water notifications — the pump's and the consumer's in `DispatchWaterSystem` — while the building-efficiency penalty is ungated, scaling with the pollution fraction through `BuildingEfficiencyParameterData.m_WaterPollutionPenalty`.

There is no path from the ground-pollution map into surface water: the only pollution inlet into the water texture is a source with `m_Polluted > 0`, and the simulation's own writer of one is the sewage outlet's sub-object — the authoring `WaterSource` component and the editor's water tool can author one too.

(VOLATILE: every system, component, field, method, kernel selection and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Prefabs` and the root `Game` namespace, at the files the sections cite.)
