# Production and sources

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Once per cycle the building AI systems recompute each producer's capacity from its prefab terms and its sources, write it onto the producer edge, and read last cycle's solved flow back as `m_LastProduction`.

## Power plants

Sources: `src/Game/Game.Simulation/PowerPlantAISystem.cs`, `src/Game/Game.Buildings/EfficiencyFactor.cs`.

```
PowerPlantAISystem, per building with ElectricityProducer and ElectricityBuildingConnection:
  m_LastProduction = producerEdge.m_Flow            -- last cycle's flow, not the capacity
  efficiency computed with factors 17..20 forced to 1, so a plant's output is not multiplied by its own shortfall
  each term is a float2 (actual, potential); the five non-fuelled prefab components combine over InstalledUpgrade first, the fuelled one does not:
  fuelled:  PowerPlantData      summed per upgrade, each keeping its own upgrade's ResourceConsumer -- actual = efficiency * m_ElectricityProduction, zeroed while that m_ResourceAvailability == 0 (potential stays)
  garbage:  GarbagePoweredData  clamp(GarbageFacility.m_ProcessingRate / m_ProductionPerUnit, 0, m_Capacity), actual == potential
  wind:     WindPoweredData     potential = efficiency * m_Production;
            actual = potential * saturate((lengthsq(wind) / m_MaximumWind^2) ^ 1.5), wind from WindSystem.GetWind(position)
  water:    WaterPoweredData.m_ProductionFactor gates the term; the dam's dimensions live on the runtime Game.Buildings.WaterPowered -- potential = efficiency * min(waterPowered.m_Length * waterPowered.m_Height, 1000000) * m_CapacityFactor;
            actual is a per-sub-net line integral over the dam curve, sampling height, depth and velocity on both banks
  groundwater: GroundWaterPoweredData  potential = efficiency * m_Production;
            actual = potential * clamp(cell.m_Amount / m_MaximumGroundWater, 0, 1)
  solar:    SolarPoweredData    potential = efficiency * m_Production;
            actual = clamp(potential * sunLight, 0, potential)
  m_Capacity = producerEdge.m_Capacity = round(sum of actuals)
  shortfalls (potential - actual) become EfficiencyFactor 17..20 -- MaterialSupply, WindSpeed, WaterDepth, SunIntensity -- via BuildingUtils.ApproximateEfficiencyFactors over the weights (fuel, wind, water + groundwater, solar): hydro and the aquifer plant share the WaterDepth slot, and garbage carries no weight at all
  ServiceUsage.m_Usage = m_LastProduction / m_Capacity, zeroed while out of fuel -- computed before this cycle's capacity write, so it divides by LAST cycle's capacity

sunLight, once per update on the main thread:
  sun = max(0, -PlanetarySystem.SunLight.transform.forward.y) * SunLight.additionalData.intensity / 110000
  sun *= lerp(1, 1 - ElectricityParameterData.m_CloudinessSolarPenalty, ClimateSystem.cloudiness.value)
```

## Pumping stations

Sources: `src/Game/Game.Simulation/WaterPumpingStationAISystem.cs`, `src/Game/Game.Prefabs/AllowedWaterTypes.cs`.

```
WaterPumpingStationAISystem, per pump, data = WaterPumpingStationData over upgrades:
  m_LastProduction = producerEdge.m_FreshFlow
  a building that is also a SewageOutlet recycles itself: m_UsedPurified = min(lastProduction, outlet.m_LastPurified) reduces the draw below, and the outlet's FULL m_LastPurified re-enters the capacity sum -- the wastewater treatment plant
  m_Types & Groundwater:
    cell = GroundWater map at the building; fraction = cell.m_Amount / WaterPipeParameterData.m_GroundwaterPumpEffectiveAmount
    contribution = clamp(fraction * m_Capacity, 0, m_Capacity - already)
    pollution weighted by cell.m_Polluted / max(1, cell.m_Amount)
    consumes the aquifer: ceil(draw * m_GroundwaterUsageMultiplier) units, capped at the cell, through GroundWaterSystem.ConsumeGroundWater
  m_Types & SurfaceWater: per WaterSourceData sub-object:
    availability from GetSurfaceWaterAvailability(position, m_SurfaceWaterPumpEffectiveDepth)
    contribution = clamp(availability * m_Capacity, 0, m_Capacity - already)
    pollution from WaterUtils.SamplePolluted, weighted by the contribution
    writes back into the body: source.m_Polluted = 0 and source.m_Height = -0.0001 * total * efficiency -- pumping visibly draws the surface down, and the zeroed m_Polluted can flatten what a co-located outlet wrote
    the scarcity flag is assigned per intake, so with several the last one decides
  m_Types == None: capacity is the flat prefab capacity, no source, no pollution -- the water-tower case: all three tower prefabs carry m_Types = None (read live; the query on WaterPumpingStationData is the check)
  m_Capacity = round(efficiency * availability + outlet.m_LastPurified), onto the producer edge's m_FreshCapacity; m_Pollution = (1 - m_Purification) * weightedPollution / m_Capacity, onto m_FreshPollution
  EfficiencyFactor.WaterDepth is forced to 1 before the pump's own efficiency is read, then rewritten as (availability + lastPurified) / (data.m_Capacity + lastPurified) -- the PREFAB capacity, not the instance figure written above, and only while data.m_Capacity > 0
  notifications (none gated on the Locked progression state): not-enough-groundwater when the cell fraction < 0.75 and the cell is below 0.75 of its own max and availability < 0.1 * capacity; not-enough-surface-water the same way off its own scarcity test; the dirty-pump icon above m_MaxToleratedPollution; the shortage icon off the edge's WaterShortage flag
```

## Sewage outlets

Sources: `src/Game/Game.Simulation/SewageOutletAISystem.cs`.

```
SewageOutletAISystem, per outlet:
  discharge first, from the PREVIOUS cycle's fields:
    dirty = max(0, m_LastProcessed - m_LastPurified); clean = m_LastPurified - m_UsedPurified
    total = dirty + clean
    per WaterSourceData sub-object:
      source.m_Height = min(2.5, WaterPipeParameterData.m_SurfaceWaterUsageMultiplier * total)
      source.m_Polluted = dirty / total (0 when total is 0) -- a fully purifying outlet still raises the water level and pollutes nothing
  then this cycle's writes:
    m_Capacity = round(efficiency * SewageOutletData.m_Capacity), onto the producer edge's m_SewageCapacity
    m_LastProcessed = producerEdge.m_SewageFlow
    m_LastPurified  = round(m_Purification * m_LastProcessed); m_UsedPurified = 0 for the pump pass to claim
  -- so what reaches the river lags the solved flow by one full cycle
```

**There is no water storage simulation.**
`Game.Buildings.WaterTower` and `WastewaterTreatmentPlant` carry `m_StoredWater` fields nothing writes outside their own `Deserialize`, so the one real reader — an effects test on `m_LastStoredWater != m_StoredWater` — is permanently false.
Source: `src/Game/Game.Buildings/WaterTower.cs`, `src/Game/Game.Buildings/WastewaterTreatmentPlant.cs`, `src/Game/Game.Effects/EffectControlData.cs`.

## Batteries and the emergency generator

Sources: `src/Game/Game.Simulation/BatteryAISystem.cs`, `src/Game/Game.Buildings/Battery.cs`, `src/Game/Game.Prefabs/BatteryData.cs`.

```
BatteryAISystem, per battery:
  net = chargeEdge.m_Flow - dischargeEdge.m_Flow
  m_StoredEnergy = clamp(m_StoredEnergy + net, 0, BatteryData.capacityTicks)
  emergency generator upgrades (EmergencyGeneratorData, over non-inactive upgrades):
    m_ActivationThreshold is a running max over the upgrades; runs while efficiency > 0 and charge fraction < threshold.min, or < .max while already running -- hysteresis
    production = sum of ceil(efficiency * m_ElectricityProduction) over the upgrades that still HasResources -- an unfuelled generator contributes nothing
    it deposits straight into m_StoredEnergy, capped at the remaining capacity, never through the graph
    its PollutionEmitModifier fields flip 0 while firing, -1 while not -- a generator pollutes only when it runs
  m_Capacity and m_LastFlow are bookkeeping writes each pass, and a battery hitting empty raises the battery-empty notification
  next cycle's edge capacities:
    dischargeEdge.m_Capacity = efficiency > 0 ? min(m_PowerOutput, m_StoredEnergy) : 0
    chargeEdge.m_Capacity    = min(round(efficiency * m_PowerOutput), capacityTicks - m_StoredEnergy)
```

`Battery.storedEnergyHours => m_StoredEnergy / 85` and `BatteryData.capacityTicks => 85 * m_Capacity` are the unit conversion: the literal `85` both properties write matches `ElectricityFlowSystem.kUpdatesPerHour`, so stored energy is flow-units times solver ticks, and those two properties are the whole power-to-energy story (`simulation-time-and-units`).

## Groundwater and pipe pollution

Sources: `src/Game/Game.Simulation/GroundWaterSystem.cs`, `src/Game/Game.Simulation/GroundWater.cs`, `src/Game/Game.Simulation/GroundWaterPollutionSystem.cs`, `src/Game/Game.Simulation/WaterPipePollutionSystem.cs`, `src/Game/Game.Simulation/CellMapSystem.cs`.

The aquifer is a `CellMapSystem<GroundWater>` of `kTextureSize = 256` cells per side over the map's `kMapSize = 14336` metres; each cell is three `short`s — `m_Amount`, `m_Polluted`, `m_Max` — and `Consume` keeps the pollution ratio constant.
The map layer itself belongs to `environment-and-pollution` with the rest of the cell maps; here it constrains what a pump or a `GroundWaterPoweredData` plant can produce, never who a pipe can reach.

```
GroundWaterSystem, per update, over right and down neighbour pairs:
  pollution: move a quarter of the gap between a cell's pollution and its share of the pair's total at uniform concentration, clamped to a quarter of each side's clean-water headroom
  amount: move a quarter of the difference in fill deficit (m_Amount - m_Max) -- water flows toward the cell further below its own per-cell ceiling (m_Max, authored map data), not toward less water -- and the moved water carries its source's pollution ratio
  both moves are integer divisions, so small gaps truncate to no move at all
  then per cell, the two accumulated neighbour moves land:
    m_Amount   = min(m_Amount + flow + ceil(m_GroundwaterReplenish * m_Max), m_Max)
    m_Polluted = clamp(m_Polluted + pollutionDelta - m_GroundwaterPurification, 0, m_Amount)
GroundWaterPollutionSystem: samples GroundPollution bilerped at the cell's centre (an equal-weight average of four pollution cells) and adds sample / 200 per update -- integer division, so sampled pollution under 200 adds exactly zero -- clamped to the cell's water amount
WaterPipePollutionSystem (interval 64, twice per flow cycle):
  purify = frameIndex/64 % WaterPipeParameterData.m_WaterPipePollutionSpreadInterval != 0 -- true on every update EXCEPT the spread tick, so purification is the ordinary update and propagation the rare one; at an interval of 1 it is never true
  NodePollutionJob: each WaterPipeNode.m_FreshPollution = the flow-weighted mean of its incoming edges' pollution; a node with no inflow decays by m_StaleWaterPipePurification, floored at 0
  EdgePollutionJob: an edge (source-side edges skipped) takes its start node's pollution when fresh flow is positive, its end node's when negative, the mean when exactly zero; on a purify tick the new value lands only when it is zero -- it can clean, never dirty
```

(VOLATILE: every system, component, field, property, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Buildings`, `Game.Prefabs` and `Game.Effects`, at the files the sections cite; plus the live-read `m_Types` census on the three tower prefabs, re-derived by the query the pumping listing names.)
