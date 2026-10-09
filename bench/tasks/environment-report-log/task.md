# A periodic environment report in the log

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod. I want it to write a short environment report to the log a few times per in-game day, so I can watch how my city's surroundings change over a long session. Each report should give the city-wide totals of ground, air and noise pollution, what share of the city's groundwater is currently polluted, which way the wind is blowing across the map and how strong it is, and how much of each natural resource (fertile land, ore, oil and fish) is still available across the whole map.

## Checklist

### The map handle is completed before the array is read

- Falls in: the `JobHandle` that `GetMap` or `GetData` outputs is discarded (`out _`) or stored and never completed, and the returned array is indexed on the main thread, or handed to a job scheduled without that handle in its dependencies.
- Avoids: each handle is `Complete()`d before its array is indexed on the main thread, or the job reading the array is scheduled with the handle in its dependency chain and registered back with `AddReader`.
- Reference: cs2-modding/references/mechanics/environment-and-pollution/cell-maps.md
- Source: src/Game/Game.Simulation/CellMapSystem.cs:156

### The groundwater pollution share is taken against the water present

- Falls in: the polluted share divides the summed `GroundWater.m_Polluted` by the summed `m_Max`, or treats `m_Polluted` as a per-cell percentage and averages or sums it on its own.
- Avoids: the share is `m_Polluted / m_Amount`, per cell or over the sums, with `m_Amount` as the denominator and `m_Polluted` as an absolute quantity.
- Reference: cs2-modding/references/mechanics/environment-and-pollution/water-and-groundwater.md
- Source: src/Game/Game.Simulation/WaterPumpingStationAISystem.cs:139

### The wind is read from the wind simulation, not from `ClimateSystem`

- Falls in: the wind direction or strength comes from `ClimateSystem.wind`.
- Avoids: the wind comes from `WindSimulationSystem.constantWind`, or from the `WindSystem` cell map through `Wind.SampleWind`, `WindSystem.GetWind` or an average over its cells.
- Reference: cs2-modding/references/mechanics/environment-and-pollution/climate-and-weather.md
- Source: src/Game/Game.Simulation/ClimateSystem.cs:178

### The used fish amount is read from the cell's field, not from `GetUsedResources`

- Falls in: the available resources come from `NaturalResourceCell.GetUsedResources()` (alone or subtracted from `GetBaseResources()`), with its `w` component reported as fish.
- Avoids: fish is `cell.m_Fish.m_Base - cell.m_Fish.m_Used` (or `m_Fish.m_Used` read directly), and `GetUsedResources()` is either unused or its `w` component is not what fish is reported from.
- Reference: cs2-modding/references/mechanics/environment-and-pollution/natural-resources.md
- Source: src/Game/Game.Simulation/NaturalResourceCell.cs:55

## Roots

- decompile
