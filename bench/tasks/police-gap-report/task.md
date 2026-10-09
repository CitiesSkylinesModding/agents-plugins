# Log poorly policed homes once per in-game day

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod to work out why some of my neighbourhoods stay unsafe. Once per in-game day, I want it to log every residential building with poor police coverage. Next to each one, log the closest police station that is allowed to respond there, that station's efficiency with its staffing and budget factors broken out, how many patrol cars it can put on the road right now, and what the police budget slider is currently set to.

## Checklist

### Police coverage is read off the home's road edge

- Falls in: coverage is derived from the home's distance to police station positions, such as comparing it against the station prefab's `CoverageData.m_Range` as a radius, or from a `ServiceCoverage` buffer looked up on the home entity itself.
- Avoids: the `Game.Net.ServiceCoverage` buffer of the home's `Building.m_RoadEdge` is read at `CoverageService.Police`, through `NetUtils.GetServiceCoverage(buffer, CoverageService.Police, building.m_CurvePosition)` or an equivalent indexed read of that edge buffer.
- Reference: cs2-modding/references/mechanics/city-services-and-coverage/coverage.md
- Source: src/Game/Game.Simulation/CrimeAccumulationSystem.cs:109

### A station with no district assigned serves everywhere

- Falls in: a station is eligible only when its `ServiceDistrict` buffer contains the home's district, so an empty buffer, or a station carrying no buffer, excludes it from every home.
- Avoids: eligibility goes through `AreaUtils.CheckServiceDistrict`, or an empty or missing `ServiceDistrict` buffer makes the station eligible for every home before any contains test.
- Reference: cs2-modding/references/mechanics/city-services-and-coverage/dispatch.md
- Source: src/Game/Game.Areas/AreaUtils.cs:150

### An efficiency factor missing from the buffer is 1

- Falls in: the staffing or budget factor is read by scanning the station's `Efficiency` buffer, and an absent `EfficiencyFactor` entry is reported as 0, as missing, or as a distinct "not found" state.
- Avoids: the factor is read with `BuildingUtils.GetEfficiencyFactor(buffer, factor)`, or by a hand scan whose fallback for an absent entry is `1f`.
- Reference: cs2-modding/references/mechanics/city-services-and-coverage/budget-workforce-and-upkeep.md
- Source: src/Game/Game.Buildings/BuildingUtils.cs:258

### A working station fields at least one patrol car

- Falls in: the patrol-car count is `efficiency * m_PatrolCarCapacity` truncated, floored or rounded with no lower bound, so a low-efficiency station reports zero cars.
- Avoids: the count comes from `BuildingUtils.GetVehicleCapacity(efficiency, capacity)`, or is clamped to at least 1 whenever efficiency is above 0.001 and the capacity is positive.
- Reference: cs2-modding/references/mechanics/city-services-and-coverage/dispatch.md
- Source: src/Game/Game.Buildings/BuildingUtils.cs:378

### A budget slider nobody has moved reads as 100

- Falls in: the `ServiceBudgetData` buffer is scanned for the police service prefab, and a missing entry is reported as 0, as unset, or skipped.
- Avoids: the budget comes from `CityServiceBudgetSystem.GetServiceBudget(servicePrefab)`, or from a hand scan whose fallback for a missing entry is 100.
- Reference: cs2-modding/references/mechanics/city-services-and-coverage/budget-workforce-and-upkeep.md
- Source: src/Game/Game.Simulation/CityServiceBudgetSystem.cs:1080

## Roots

- decompile
