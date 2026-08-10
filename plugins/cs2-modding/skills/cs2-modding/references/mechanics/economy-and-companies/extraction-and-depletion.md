# Extraction and depletion

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

The extractor economy spans four namespaces, and the join is the hub building's `Attached.m_Parent`: `Game.Areas.Extractor` is the per-area state, the `Game.Prefabs` extractor components the authored side, `Game.Companies.ExtractorCompany` the company tag and `Game.Buildings.ExtractorFacility` the hub's own state.
`zoning-buildings-and-land-value` records the placement half; what this file owns is the chain from deposit to viability:

```
NaturalResourceCell (the map grid -- environment-and-pollution)
  -> Extractor.m_ResourceAmount, .m_MaxConcentration     (AreaResourceSystem, per area)
  -> GetEffectiveConcentration(params, feature, conc)    (ExtractorCompanySystem)
  -> EfficiencyFactor.NaturalResources on the hub's Efficiency buffer
  -> buildingEfficiency in GetCompanyProductionPerDay
  -> units produced -> profit -> TaxPayer accrual
```

The chain ends at the accrual: `CompanyMoveAwaySystem` and `IndustrialAISystem` both exclude `ExtractorCompany` from their queries, so a depleted extractor company never goes bankrupt and never moves away — it idles at whatever the concentration allows.

## Concentration sets efficiency

Sources: `src/Game/Game.Simulation/ExtractorCompanySystem.cs`, `src/Game/Game.Prefabs/ExtractorParameterPrefab.cs`, `src/Game/Game.Areas/Extractor.cs`.

```
GetEffectiveConcentration(params, feature, concentration) = min(1, concentration / X)
  X = m_FullOil | m_FullFertility | m_FullFish | m_FullOre by map feature, 1 otherwise
GetBestConcentration: the surface-area-weighted mean effective concentration over every
  sub-area and installed upgrade of the hub; returns false at zero, which skips the
  company's entire production pass
ExtractorCompanySystem tick (per-entity rate kCompanyUpdatesPerDay = 256):
  EfficiencyFactor.NaturalResources = that concentration, written BEFORE the zero check,
    so a depleted extractor's building shows 0%
  production and profit both run with isIndustrial: true, which buys the industrial
    price and the industrial work-per-unit; the sector efficiency is
    m_ExtractorProductionEfficiency because the extractor mask is tested first,
    independent of that flag
  accrual into TaxPayer as the industrial rate; CompanyStatisticData.m_LastUpdateProduce
    = produced * 256, the one field the income statement reads back instead of recomputing
  ProcessArea, per sub-area: share = produced * area / max(1, concentration * totalSize)
    a deposit-requiring area (both flags below) clamps to
      clamp(share * effectiveConcentration, 0, remaining)
    m_ExtractedAmount and m_TotalExtracted += share * GetExtractionMultiplier(sub-area)
      (m_FertilityConsumption | m_FishConsumption | m_ForestConsumption by feature, else 1)
      -- accrued for every area, flags or not
    m_WorkAmount += share * ExtractorAreaData.m_WorkAmountFactor
```

The concentration parameter is a *sufficiency threshold*, not a stock — the `m_Full*` tooltips on `ExtractorParameterPrefab` describe the same threshold behaviour — so a deposit is at full efficiency until concentration falls below the `m_Full*` field and only then starts dropping.

**A resource that does not require a deposit bypasses the concentration gate.**
The gate is a conjunction of two prefab flags — `ResourceData.m_RequireNaturalResource` on the resource and `ExtractorAreaData.m_RequireNaturalResource` on the area prefab — and with either off, the whole surface area counts at concentration 1 and the per-area remaining cap never applies; extraction still accrues into the area's counters, and whether the grid is written down keys on the area's map feature rather than on these flags.
Source: `src/Game/Game.Simulation/ExtractorCompanySystem.cs`, `src/Game/Game.Prefabs/ResourceData.cs`, `src/Game/Game.Prefabs/ExtractorAreaData.cs`.

**An extraction area attached to an unconnected upgrade produces nothing while still moving the hub's displayed efficiency.**
`ExtractorFacilityData.m_Requirements` (`RouteConnect`, `NetConnect`) makes the production tick skip an installed upgrade's sub-areas when the required cargo route or resource-connection node is missing, a `MapFeature.Fish` upgrade with no valid route is skipped the same way, and so is a `BuildingOption.Inactive` upgrade — but `GetBestConcentration` applies none of those filters and counts a non-deposit area at concentration 1, so the skipped areas still enter the efficiency mean and dilute every producing area's share.
Source: `src/Game/Game.Simulation/ExtractorCompanySystem.cs`, `src/Game/Game.Prefabs/ExtractorFacilityData.cs`.

## What extraction takes out of the ground

Sources: `src/Game/Game.Simulation/AreaLotSimulationSystem.cs`, `src/Game/Game.Prefabs/ExtractorParameterPrefab.cs`.

`AreaLotSimulationSystem` writes the grid down once an area's `m_ExtractedAmount` reaches `max(1, feature is Ore or Oil ? 1 : m_ResourceAmount * 0.001)` — fertile land and fish subtract the extracted amount from the best cell directly, while ore and oil go through:

```
GetUnlimitedUsage(originalConcentration, currentConcentration, mu = 1 / m_OreConsumption
                                                                 (or 1 / m_OilConsumption)):
  n     = log(originalConcentration) - log(currentConcentration)
  usage = RoundToIntRandom(mu * originalConcentration * exp(-n) * extractedAmount * 10000)
  // exp(-(log o - log c)) = c/o, so usage = mu * currentConcentration * extracted * 10000:
  // concentration decays as exp(-extracted / m_OreConsumption) in cumulative units
```

So an ore or oil deposit yields exponentially less as it is worked, reaching 1/e of its original concentration after exactly `m_OreConsumption` (or `m_OilConsumption`) extracted units — the field's own tooltip states the same 1/2.71 figure, phrased as efficiency — and viability drops only once the decayed concentration crosses below the `m_Full*` threshold above.
That is the whole answer to what depletion does to a specialized industry: a formula, not a cliff — with one edge, since the decay is a single stochastic Euler step per write-down and the cell's used counter caps at 65535 rather than at its base, so one large step can overshoot and zero the cell outright.
`CityModifierType.OreResourceAmount` and `OilResourceAmount` scale a cell's base amount where availability is read to pick the best cell, not inside `GetUnlimitedUsage`.
Forest is absent from the depletion switch entirely: a forestry area is put on the same recompute list with no write-down, and its concentration comes from tree state, which `environment-and-pollution` owns.

(VOLATILE: every system, component, field, formula and `Source:` path this file names — their declarations in `Game.Simulation`, `Game.Areas`, `Game.Companies`, `Game.Buildings`, `Game.City`, `Game.Agents` and `Game.Prefabs` under `src/Game/`, at the files the sections cite.)
