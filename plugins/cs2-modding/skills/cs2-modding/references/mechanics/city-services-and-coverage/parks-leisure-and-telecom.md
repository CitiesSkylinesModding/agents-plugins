# Parks, leisure and telecom

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Two services break the road-coverage mould: a park's coverage strength moves with its maintenance at runtime, and telecom is a cell map with no road buffer at all.
Leisure lands here on the supply side, while the citizen's leisure demand belongs to `citizens-and-households`.

## Parks: coverage that moves with maintenance

`Game.Buildings.ModifiedServiceCoverage { m_Range, m_Capacity, m_Magnitude }` is the only per-instance override of `CoverageData` in the game; a non-upgrade park gets it from `Park.GetArchetypeComponents`, and the coverage pass applies it through `ReplaceData` before computing anything ([coverage.md](coverage.md)).
`ParkAISystem` recomputes it every update from scratch (`ParkInitializeSystem` seeds the same value at creation):

```
m_Maintenance = max(0, m_Maintenance - (400 + 50 * renterCount) / kUpdatesPerDay)
                                          // integer division; kUpdatesPerDay = 256
fill  = m_Maintenance / max(1, ParkData.m_MaintenancePool)
steps = floor(fill / 0.3)                 // 0..3: the vehicle's refill caps m_Maintenance at the pool
m_Magnitude = prefab magnitude * (0.95 + 0.05 * min(1, steps) + 0.1 * max(0, steps - 1))
m_Magnitude = ApplyModifier(m_Magnitude, CityModifierType.ParkEntertainment)
m_Range     = prefab range * (0.95 + 0.05 * steps)
m_Capacity  = the prefab's, untouched
a MaintenanceRequest (with RequestGroup(32)) is raised while none is outstanding and m_MaintenancePool - m_Maintenance - m_MaintenancePool / 10 > 0    // a 10% deadband
```

Source: `src/Game/Game.Prefabs/Park.cs`, `src/Game/Game.Simulation/ParkAISystem.cs`, `src/Game/Game.Buildings/ParkInitializeSystem.cs`, `src/Game/Game.Buildings/ModifiedServiceCoverage.cs`, `src/Game/Game.Simulation/MaintenanceVehicleAISystem.cs` (the refill cap).

**`ModifiedServiceCoverage.m_Range` never reaches the coverage search.**
`ReplaceData` copies it into the working struct, but the pass reads only magnitude and capacity out of that struct, and the search takes its range straight off the prefab's `CoverageData` — so a neglected park loses coverage strength but not reach, and the range multiplier above buys nothing (re-check: `ReplaceData`'s one call site in `ProcessCoverageJob`, and `SetupCoverageSearchJob`'s prefab read).
Source: `src/Game/Game.Simulation/ServiceCoverageSystem.cs`, `src/Game/Game.Buildings/ModifiedServiceCoverage.cs`.

Maintenance requests are answered by `Game.Buildings.MaintenanceDepot` (`MaintenanceDepotFlags { HasAvailableVehicles }`), whose work kinds — `Game.Simulation.MaintenanceType`, on `Game.Prefabs.MaintenanceDepotData` — also cover roads, snow and vehicles.

## Leisure: the supply side

`Game.Prefabs.LeisureProviderData { m_Efficiency, m_Resources, m_LeisureType }` is a per-prefab component a park or a commercial venue carries, and the `Game.Buildings.LeisureProvider` tag is added to the archetype only when the authoring class's `m_Efficiency` is above zero — so a query on the tag finds the instances of prefabs that declared a working provider.
What turns a park into a wellbeing bonus is its `CoverageService.Park` coverage, through `CitizenHappinessSystem.GetEntertainmentBonuses` ([coverage.md](coverage.md)); the trip generation, the citizen's leisure counter and `LeisureType`'s members are `citizens-and-households`'s.
Source: `src/Game/Game.Prefabs/LeisureProvider.cs`, `src/Game/Game.Prefabs/LeisureProviderData.cs`.

**Leisure has no failure surface — no request kind, no `EfficiencyFactor` member, no notification — and choosing a venue never reads building efficiency.**
`SetupLeisureTargetJob` rejects a candidate only on `BuildingOption.Inactive`, a missing `LeisureProviderData`, a `LeisureType` mismatch and, for `Commercial` and `Meals`, insufficient `ServiceAvailable`; a search that finds nothing removes the citizen's `Leisure` and `TravelPurpose` and stamps `Game.Citizens.LeisureSeekerCooldown { m_SimulationFrame }` — `TripNeededSystem` stamps the same component when a leisure trip's path fails — overwritten per failure rather than counted, so the only trace is the citizen's leisure counter drifting down, which `citizens-and-households` owns.
Source: `src/Game/Game.Simulation/CitizenPathfindSetup.cs`, `src/Game/Game.Simulation/LeisureSystem.cs`, `src/Game/Game.Simulation/TripNeededSystem.cs`, `src/Game/Game.Citizens/LeisureSeekerCooldown.cs`.

**The `Leisure` info view is the parks-and-recreation view, and it has no UI system.**
Its infomode set — read live, because the infoview-to-infomode mapping is authored asset data no static read reaches — is five entries: park-maintenance buildings, park buildings, park-maintenance vehicles, the leisure-provider colouring (`BuildingStatusType.LeisureProvider`), and park coverage (`InfoviewCoverageData { m_Service = Park }`); there is no `LeisureInfoviewUISystem` among the `*InfoviewUISystem.cs` files.
Source: `src/Game/Game.Prefabs/BuildingStateInfomodePrefab.cs`, `src/Game/Game.UI.InGame/` (the absence).

## Telecom: a cell map, not a road buffer

`TelecomCoverageSystem` runs on a 4096-frame interval and rebuilds a 128 × 128 map (`TEXTURE_SIZE = 128`) from scratch each time; per facility (stats folded over installed upgrades):

```
capacity = ApplyModifier(TelecomFacilityData.m_NetworkCapacity, CityModifierType.TelecomCapacity)
range    = TelecomFacilityData.m_Range * sqrt(efficiency)
capacity *= efficiency
skip the facility           when range < 1 or capacity < 1
users    = CalculateNetworkUsers(the facility's signal share of density over the range's cells)
add capacity / max(1, users) to each covered cell, weighted by the facility's share of the cell's accumulated signal and skipped where that accumulated signal is ~0 (obstruction slopes per facility; TelecomFacilityData.m_PenetrateTerrain exempts a mast)
```

**Telecom is the one service in this topic whose reach genuinely moves with efficiency** — as a square root on range, linearly on capacity; every road-coverage service moves only in magnitude ([coverage.md](coverage.md)).
Source: `src/Game/Game.Simulation/TelecomCoverageSystem.cs`, `src/Game/Game.Prefabs/TelecomFacilityData.cs`.

Each cell stores two bytes, and quality derives from them:

```
m_SignalStrength = clamp(strength * 255, 0, 255)
m_NetworkLoad    = clamp(127.5 / max(0.0001, capacity), 0, 255)
networkQuality   = m_SignalStrength * 510 / (255 + (m_NetworkLoad << 1))
SampleNetworkQuality = bilinear interpolation of min(1, strength / (127.5 + load)) over the four surrounding cells
```

Source: `src/Game/Game.Simulation/TelecomCoverage.cs`, `src/Game/Game.Simulation/TelecomCoverageSystem.cs` (the two byte clamps).

The city-wide `TelecomStatus.m_Quality` is a **density-weighted** mean over all cells, so empty land does not dilute it.
The map's readers this topic cares about: `TelecomEfficiencySystem`, on a 32-frame interval (`kUpdatesPerDay = 512`, sixteen `UpdateFrame` groups), writes `EfficiencyFactor.Telecom = 1 - (1 - quality / m_TelecomBaseline)² * 0.01 * ConsumptionData.m_TelecomNeed` where quality is under the baseline and 1 otherwise; `LandValueSystem`'s cell job adds `quality * m_TelecomCoverageBonusMultiplier`, capped at `m_CommonFactorMaxBonus` (`zoning-buildings-and-land-value` owns the system); and `FireSimulationSystem` scales a fire's response time by sampled quality through `FireConfigurationData.m_TelecomResponseTimeModifier`.
Source: `src/Game/Game.Simulation/TelecomEfficiencySystem.cs`, `src/Game/Game.Simulation/LandValueSystem.cs`, `src/Game/Game.Simulation/FireSimulationSystem.cs`.

**The efficiency write is progression-gated; the map is not.**
`TelecomEfficiencySystem` schedules nothing while `TelecomParameterData.m_TelecomServicePrefab` carries an enabled `Locked` — a not-yet-unlocked city's buildings keep whatever `EfficiencyFactor.Telecom` entry they already held, left unwritten rather than zeroed — while `TelecomCoverageSystem` rebuilds the map on its own interval regardless.
Source: `src/Game/Game.Simulation/TelecomEfficiencySystem.cs`, `src/Game/Game.Simulation/TelecomCoverageSystem.cs`.

(VOLATILE: every component, field, system, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Prefabs`, `Game.Buildings`, `Game.Citizens`, `Game.Agents`, `Game.City`, `Game.Companies` and `Game.UI.InGame`, at the files each listing and trap cites; plus the live-read `Leisure` infomode set, against the running game's infoview prefab through its `InfoviewMode` buffer with `PrefabSystem.GetPrefabName` as the label.)
