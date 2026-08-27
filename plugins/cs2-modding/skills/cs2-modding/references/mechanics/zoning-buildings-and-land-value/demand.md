# Demand

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Three systems produce six demand values, all in `SystemUpdatePhase.GameSimulation` on the interval and staggered offsets `DemandUtils` declares as constants: `kUpdateInterval = 16`, count-companies at offset 1, commercial at 4, industrial at 7, residential at 10, zone spawn at 13 (`src/Game/Game.Simulation/DemandUtils.cs`) — declarations the systems inline as literals, so `DemandUtils` itself has no call sites and patching it changes nothing.
Every factor below is signed, and there is no separate downward path: demand falls by the same machinery that raises it, made asymmetric only by the clamps and by a per-sign weight.

## Residential

Sources: `src/Game/Game.Simulation/ResidentialDemandSystem.cs`, `src/Game/Game.Simulation/CountResidentialPropertySystem.cs`, `src/Game/Game.Buildings/PropertyUtils.cs`, `src/Game/Game.Prefabs/DemandParameterData.cs`.

```
ResidentialDemandSystem.UpdateResidentialDemandJob:
  unlocked = bool3 over the unlocked residential zone prefabs' computed densities (PropertyUtils.GetZoneDensity)
  factors, every weight and neutral from DemandParameterData:
    population decay:   20 - smoothstep(0, 20, population / 20000)
    happiness:          m_HappinessEffect * (max(m_MinimumHappiness, average happiness) - m_NeutralHappiness)
    homeless, down:     min(-m_HomelessEffect * r, kMaxFactorEffect), r = clamp(2 * homelessHouseholds / m_NeutralHomelessness, 0, 2)
    homeless, up:       clamp(+m_HomelessEffect * r, 0, kMaxFactorEffect)      // same r, opposite sign
    taxes:              m_TaxEffect.x * mean over the 5 education levels of -(residential rate - 10)
    simple workplaces:  clamp(m_AvailableWorkplaceEffect * (free - total * m_NeutralAvailableWorkplacePercentage / 100), 0, 40)
    complex workplaces: the same shape, clamped to [0, 20]                     // both can only push up
    students:           m_StudentEffect * clamp(study positions at levels 1-4 / 200, 0, 5)
    unemployment:       m_NeutralUnemployment - unemployment rate
  each factor then through GetFactorValue: multiplied by weight.x when negative, weight.y when positive, truncated to int
  m_HouseholdDemand = min(200, decay + happiness + homeless(down) + taxes + unemployment + students + max(simple workplaces, complex workplaces))
  per density i in (low, medium, high):
    pressure[i]  = round(100 * (m_FreeResidentialRequirement[i] - free[i]) / m_FreeResidentialRequirement[i])
                   // negative the moment free properties exceed the requirement
    factor slots (the reported arrays): [7] happiness, [11] taxes, [5] unemployment, [6] simple workplaces (halved for low density), [12] students (medium and high), [8] homeless(up) (high only), [13] pressure[i]
                   // homeless(up) in the high sum only: the negative half reaches every density through m_HouseholdDemand / 2, the positive half only high
    factorSum[i] = that density's slots summed, the whole sum zeroed when pressure[i] < 0
    m_BuildingDemand[i] = clamp(m_HouseholdDemand / 2 + pressure[i] + factorSum[i], 0, 100)
                   // pressure sits in the slots too, so it enters twice when nonnegative
  a density with no unlocked zone prefab is forced to zero, whatever the arithmetic said
  m_UnlimitedDemand (the debug override) then forces 100, after every clamp
  free[i] / total[i] are CountResidentialPropertySystem's ResidentialPropertyData { m_FreeProperties, m_TotalProperties }
```

`kMaxFactorEffect = 15` is `static readonly` and ships as written.

**The factor arrays are indexed by integer literal against the `DemandFactor` enum.**
`m_LowDemandFactors[7]` is `Happiness` and `[13]` is `EmptyBuildings`; the `[EnumArray(typeof(DemandFactor))]` attribute on the field declarations is the only thing tying index to meaning, so read the two side by side.
Source: `src/Game/Game.Simulation/ResidentialDemandSystem.cs`, `src/Game/Game.Simulation/DemandFactor.cs`.

**The reported factor arrays are edited after the demand arithmetic read them.**
The pressure value is rerouted between `EmptyBuildings` `[13]` and `BuildingDemand` `[18]` by whether the density has any properties, the no-workplaces guard zeroes a positive `[6]`, and the zero-population guard zeroes `[5]` — all after the sums were taken, so the infoview breakdown is not the arithmetic's input.
Source: `src/Game/Game.Simulation/ResidentialDemandSystem.cs`.

One `DemandFactor` enum serves all six values, declared whole at `src/Game/Game.Simulation/DemandFactor.cs`:

```
StorageLevels, UneducatedWorkforce, EducatedWorkforce, CompanyWealth, LocalDemand, Unemployment, FreeWorkplaces, Happiness, Homelessness, TouristDemand, LocalInputs, Taxes, Students, EmptyBuildings, EmptyZones, PoorZoneLocation, PetrolLocalDemand, Warehouses, BuildingDemand, Count
```

## Commercial

Sources: `src/Game/Game.Simulation/CommercialDemandSystem.cs`, `src/Game/Game.Prefabs/DemandParameterData.cs`.

```
CommercialDemandSystem.UpdateCommercialDemandJob, per commercial resource:
  tax term = -0.05 * (commercial tax rate for the resource - 10) * m_TaxEffect.y + a game-mode offset (m_CommercialTaxEffectDemandOffset, latched at load)
  demand   = resource == Lodging
             ? (int(m_HotelRoomPercentRequirement * current tourists) > lodging capacity ? 100 : the 0 every slot was reset to at the top of Execute)
             : max(0, round(m_CommercialStorageEffect * (m_CommercialStorageMinimum - 100 * current / (1 + total))))
  demand   = round((1 + tax term) * demand)     // so no shortfall means lodging demand 0, tax or no tax
  building demand for the resource = demand, but only when free properties - propertyless companies <= 0          // companies exist with nowhere to go
  totals: both divided by the count of resources with COMPANY demand and clamped 0-100, so a resource with company demand and no building demand dilutes the building total
  building demand zeroed outright when no commercial zone type is unlocked
  m_UnlimitedDemand then forces both totals to 100
```

So commercial demand is per resource, never per zone: a commercial building's spawn chance sums the demand of what its `BuildingPropertyData.m_AllowedSold` lets it sell ([building-spawning.md](building-spawning.md)).

## Industrial and office

`IndustrialDemandSystem` produces industrial, storage and office demand in one job (`src/Game/Game.Simulation/IndustrialDemandSystem.cs`).
It is one file and reads whole; the shapes that orient it:
the office split is per resource on `ResourceData.m_Weight == 0`, and the per-resource office values land in the same `m_IndustrialCompanyDemands` / `m_IndustrialBuildingDemands` arrays the spawner reads — there is no office per-resource array;
per-resource building demand is 50 or 0 for a weightless or processed resource — gated on free properties minus propertyless companies at or below zero — and a flat ungated 1 for a material resource with company demand;
storage building demand's final form is `ceil(pow(20 * value, 0.75))` while storage company demand stays a raw sum;
the industrial building total is `2 * sum / resource count` then clamped — and zeroed outright when no industrial zone type is unlocked, the commercial gate's twin — the office company total squares itself through `*= 2 * value / count` and is never clamped, and the office building total is clamped and never scaled;
`m_UnlimitedDemand` forces the two building totals to 100 after the clamps.

(VOLATILE: the system names, job shapes, parameter fields, factor indices and constants this file names — their declarations in `Game.Simulation`, `Game.Buildings` and `Game.Prefabs` under `src/Game/`, at the files the sections cite.)
