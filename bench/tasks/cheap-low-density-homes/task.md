# Cheaper low-density housing

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod. I want living in low-density residential buildings to be cheaper in my city: those buildings should cost half as much to keep up, and the households living in them should pay a quarter less rent than they do now.

## Checklist

### Low density is computed from the zone's data, not read off a name or a field

- Falls in: a zone counts as low-density residential by its prefab name (`name.Contains("Low")`, `StartsWith("ResidentialLow")`, a list of zone names) or by a density field read off `ZonePrefab`, `ZoneData` or `ZonePropertiesData`, which no component carries.
- Avoids: the test calls `PropertyUtils.GetZoneDensity(zoneData, zonePropertiesData)` and compares the result with `ZoneDensity.Low` under `ZoneData.m_AreaType == AreaType.Residential`, or evaluates the same two inputs itself: `AreaType.Residential` with `ZonePropertiesData.m_ScaleResidentials` false.
- Reference: cs2-modding/references/mechanics/zoning-buildings-and-land-value/zoning-buildings-and-land-value.md
- Source: src/Game/Game.Buildings/PropertyUtils.cs:693

### The upkeep is changed on the building prefab's `ConsumptionData.m_Upkeep`, where the simulation reads it

- Falls in: the mod writes the zone prefab's `ZoneServiceConsumptionData.m_Upkeep` (or its `ZoneServiceConsumption` authoring component), or `EconomyParameterData.m_ResidentialUpkeepLevelExponent`, after the game has initialised its prefabs, and expects the buildings' upkeep to follow.
- Avoids: the mod writes `ConsumptionData.m_Upkeep` on each building prefab carrying `SpawnableBuildingData` whose zone is low-density residential, or intercepts the bake itself, `BuildingInitializeSystem`'s one call to `PropertyRenterSystem.GetUpkeep`.
- Reference: cs2-modding/references/mechanics/zoning-buildings-and-land-value/level-up-loop.md
- Source: src/Game/Game.Prefabs/BuildingInitializeSystem.cs:1075

### The upkeep change is re-applied on every load, after the save's game mode has run

- Falls in: the prefab write runs once per session from a hook that fires after a load's Deserialize phase, such as `OnGameLoadingComplete` behind a one-shot `m_Applied`-style guard; or it runs from `IMod.OnLoad` or a system's `OnCreate`, before the prefabs exist.
- Avoids: the write runs on every load after the Deserialize phase, from `OnGameLoadingComplete`, `GameManager.onGameLoadingComplete` or a system ordered after `GameModeSystem` in `SystemUpdatePhase.Deserialize`, and derives its value from something the mod has not itself scaled, an original cached on first sight or the bake formula over the zone's `ZoneServiceConsumptionData`, rather than halving whatever it finds; or it lands at prefab initialisation, from a `SystemUpdatePhase.PrefabUpdate` system ordered after `BuildingInitializeSystem`, before any load's mode pass caches its defaults.
- Reference: cs2-modding/references/mechanics/zoning-buildings-and-land-value/zoning-buildings-and-land-value.md
- Source: src/Game/Game.Prefabs.Modes/ZoneServiceConsumptionGlobalMode.cs:111

### The rent cut is re-applied after every rent pass, not written once

- Falls in: `PropertyRenter.m_Rent` (or `PropertyOnMarket.m_AskingRent`) is scaled in a one-off pass, at load, from a hook, or from a system running on a cadence of its own with no ordering against the game's rent pass, so the next rent pass puts the full value back.
- Avoids: the cut is re-applied continuously: a `SystemUpdatePhase.GameSimulation` system registered `UpdateAfter<..., RentAdjustSystem>` at that system's interval (`262144 / (RentAdjustSystem.kUpdatesPerDay * 16)`) rewrites `PropertyRenter.m_Rent` on the renters of every building in the `UpdateFrame` bucket the pass just processed.
- Reference: cs2-modding/references/mechanics/zoning-buildings-and-land-value/level-up-loop.md
- Source: src/Game/Game.Simulation/RentAdjustSystem.cs:307

## Roots

- decompile
