# Production and profit

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

A company's tick is: multiply efficiencies into a production figure, consume inputs and emit output, accrue untaxed income from the profit projection, and let the AI, profitability and move-away passes read the results.

## The production formula

Sources: `src/Game/Game.Economy/EconomyUtils.cs`, `src/Game/Game.Prefabs/EconomyParameterData.cs`.

```
EconomyUtils.GetCompanyProductionPerDay (kCompanyUpdatesPerDay = 256, a static readonly int):
  sectorEfficiency = IsExtractorResource(output) ? m_ExtractorProductionEfficiency
                   : isIndustrial               ? m_IndustrialEfficiency
                   :                              m_CommercialEfficiency
                     // the extractor mask is tested FIRST, so an extractor resource takes its own sector efficiency even on the isIndustrial call path
  work    = buildingEfficiency * sectorEfficiency * GetWorkforce(employees) * 256
  perUnit = isIndustrial ? ResourceData.m_NeededWorkPerUnit.x : .y
  units   = ceil(output.m_Amount * work / perUnit)
  commercial only, a taper on own stock of service:
    saturation = ServiceAvailable.m_ServiceAvailable / ServiceCompanyData.m_MaxService
    if (saturation >= 0.8) units = ceil(lerp(units, 0, saturate((saturation - 0.8) / 0.2)))

GetWorkforce = sum over the Employee buffer of GetWorkerWorkforce(citizen.Happiness, m_Level)
GetWorkerWorkforce(happiness, level) = ((level == 0 ? 2 : 1) + 2.5 * level) * (0.75 + happiness / 200)
```

**A commercial company that is fully stocked produces nothing** — the taper reaches a hard zero, which is the mechanism behind a shop idling.
`citizens-and-households` owns the workforce ladder and `EconomyUtils.CalculateNumberOfWorkplaces`; what this topic adds is that `WorkplaceComplexity` comes off the company prefab's `WorkplaceData` and the level off the rented building's `SpawnableBuildingData` — a company has no level of its own.

## Building efficiency

Sources: `src/Game/Game.Buildings/BuildingUtils.cs`, `src/Game/Game.Buildings/EfficiencyFactor.cs`, `src/Game/Game.Buildings/Efficiency.cs`.

`Efficiency { m_Factor, m_Efficiency }` is a buffer on the **building** the company rents, not on the company, and `BuildingUtils.GetEfficiency` multiplies every entry, clamps each at ≥ 0, and returns `max(0.01, round(100 * product) / 100)` — quantised to whole percent, floored at 1% unless a factor sits at or below 0, which collapses the whole product to 0.
The factor list is the answer to "what are a company's profitability inputs", declared by `EfficiencyFactor`:

```
Destroyed, Abandoned, Disabled, Fire, ServiceBudget, NotEnoughEmployees, SickEmployees, EmployeeHappiness, ElectricitySupply, ElectricityFee, WaterSupply, DirtyWater, SewageHandling, WaterFee, Garbage, Telecom, Mail, MaterialSupply, WindSpeed, WaterDepth, SunIntensity, NaturalResources, CityModifierSoftware, CityModifierElectronics, CityModifierIndustrialEfficiency, CityModifierOfficeEfficiency, CityModifierHospitalEfficiency, SpecializationBonus, CityModifierFishInput, CityModifierFishHub, LackResources, GateBypass, Count
```

The writers this topic owns: `ProcessingCompanySystem.UpdateEfficiencyFactors` writes `SpecializationBonus`, `CityModifierOfficeEfficiency` for an office output (a commercial office output gets it pinned at 1 with the modifier skipped), `CityModifierIndustrialEfficiency` only for a non-commercial non-office output — an ordinary commercial company gets neither city-modifier factor — one of `CityModifierSoftware` or `CityModifierElectronics` (by which the output is), and `CityModifierFishInput` for a fish-input recipe; the system's own chunk loop writes `LackResources` after the input caps; `ExtractorCompanySystem` writes `NaturalResources` and `CityModifierFishHub`; `WorkProviderSystem` writes `NotEnoughEmployees`, `SickEmployees` and `EmployeeHappiness`.
The rest belong to `city-services-and-coverage` and the utility topics.
`ProcessingCompanySystem` reads `GetEfficiencyExcludingFactor(buffer, LackResources)` rather than the plain product, because it is about to write `LackResources` itself from whether the inputs allowed any output — reading it would feed last tick's shortage into this tick's production.

**Employee happiness enters production twice, multiplicatively.**
`GetWorkforce` reads each employee's happiness directly, and `WorkProviderSystem` separately writes `EfficiencyFactor.EmployeeHappiness` as the ratio of the present healthy employees' actual workforce to their workforce at happiness 50, plus `WorkplaceData.m_WorkConditions * 0.01` — so one happiness change moves the same term through both paths.
Source: `src/Game/Game.Simulation/WorkProviderSystem.cs`, `src/Game/Game.Economy/EconomyUtils.cs`.

The specialization factor is asymptotic, never a threshold:

```
ProductionSpecializationSystem (kUpdatesPerDay = 512, no UpdateFrame):
  every produced unit accumulates into the city's SpecializationBonus buffer at the output's resource index, then the whole buffer decays: m_Value = floor(0.999 * m_Value)
SpecializationBonus.GetBonus(maxBonus, coefficient) = maxBonus * m_Value / (m_Value + coefficient)
ProcessingCompanySystem writes 1 + GetBonus(m_MaxCitySpecializationBonus, m_ResourceProductionCoefficient) as EfficiencyFactor.SpecializationBonus
```

The ceiling and coefficient are `EconomyParameterData` fields; the expression approaches `1 + m_MaxCitySpecializationBonus` and never reaches it.
Source: `src/Game/Game.Simulation/ProductionSpecializationSystem.cs`, `src/Game/Game.City/SpecializationBonus.cs`, `src/Game/Game.Simulation/ProcessingCompanySystem.cs`.

## The production tick

Sources: `src/Game/Game.Simulation/ProcessingCompanySystem.cs`, `src/Game/Game.Simulation/ServiceCompanySystem.cs`, `src/Game/Game.Simulation/ExtractorCompanySystem.cs`.

All three run at the per-entity rate `kCompanyUpdatesPerDay = 256`, partitioned by `UpdateFrame`.

```
ProcessingCompanySystem (every ProcessingCompany renting a property, EXCLUDING extractors, which carry the tag too; isCommercial = chunk.Has(ServiceAvailable)):
  UpdateEfficiencyFactors(...)   // the writes listed above
  buildingEfficiency = GetEfficiencyExcludingFactor(property buffer, LackResources)
  units = RoundToIntRandom(GetCompanyProductionPerDay(...) / 256)
  if (input1 == output && input2 == NoResource && input1.amount == output.amount) continue
    // the retail pass-through short-circuit: such a company never runs this block
  units = min(units, stock(input1) / (input1.amount / output.amount))   // same for input2
  SetEfficiencyFactor(LackResources, units != 0 ? 1 : 0)
  if (units > 0):
    if (isCommercial && stock(output) > 5000) continue   // commercial stock cap; the continue also skips the accrual below
    consume inputs (randomized rounding)
    cap output: weighted   -> at StorageLimitData.m_Limit minus weighted input stock
                weightless -> at IndustrialAISystem.kMaxVirtualResourceStorage = 100000
    if (!isCommercial && property is not an OfficeProperty)
      add units to the shared office-consumption counter OfficeAISystem divides up
    add output; count into the production statistics and the specialization queue
  accrual = GetCompanyProfitPerDay(...) / 256
  if (input1.resource != output.resource && accrual > 0):
    TaxPayer.m_AverageTaxRate lerps toward the current commercial-or-industrial rate, weighted by accrual / (accrual + m_UntaxedIncome)
    TaxPayer.m_UntaxedIncome += accrual
  industrial with weighted output and stock: TrySelectItem picks a truck; a ResourceExporter is added when item.m_Cost / min(stock, item.m_Capacity) < 0.03
```

```
ServiceCompanySystem (every commercial company renting a property; the restock half):
  produced = RoundToIntRandom(GetCompanyProductionPerDay(...) / 256)   // plain GetEfficiency here
  ServiceAvailable.m_ServiceAvailable = min(m_MaxService, m_ServiceAvailable + produced)
  if (produced > 0):
    accrual = ceil(max(0, produced * GetServicePrice(output)))   // the margin, m_Price.y
    TaxPayer.m_UntaxedIncome += accrual, THEN the average rate is lerped -- the increment lands before the weighting (accrual / (accrual + already-incremented income)), so the same accrual moves the rate half as far as ProcessingCompanySystem's lerp-then-add order right after a tax collection zeroes the income, converging toward parity as income accrues between collections
    the rate here folds in DistrictModifierType.LowCommercialTax when the property has a CurrentDistrict -- the only accrual site that applies a district modifier
  no-customers notification: fires on saturation above m_NoCustomersServiceLimit, but only while the company holds stock (> 200, a bare literal) or has no resource; a hotel with free rooms is judged on free-room share above m_NoCustomersHotelLimit instead
```

A commercial *converter* — a recipe whose input differs from its output — passes both systems' gates, so it accrues untaxed income from both per tick, where a pass-through retail company accrues only in `ServiceCompanySystem`.
(UNVERIFIED: whether the converter double accrual is intended and what it sums to — settling it means reading one converter's `TaxPayer.m_UntaxedIncome` across one tick in a running game.)

The extractor tick is [extraction-and-depletion.md](extraction-and-depletion.md).

## Hiring

Sources: `src/Game/Game.Simulation/CompanyUtils.cs`, `src/Game/Game.Simulation/CommercialAISystem.cs`, `src/Game/Game.Simulation/IndustrialAISystem.cs`, `src/Game/Game.Simulation/ExtractorAISystem.cs`.

How many workers fit is a per-cell density on the recipe, not on the workplace:

```
commercial:          ceil(ServiceCompanyData.m_MaxWorkersPerCell * BuildingData.m_LotSize.x * .y * (1 + 0.5 * buildingLevel) * BuildingPropertyData.m_SpaceMultiplier)
industrial / office: the same with IndustrialProcessData.m_MaxWorkersPerCell
extractor:           max(1, ceil(m_MaxWorkersPerCell * area / 2))
                     // area = sum, over the hub's and its installed upgrades' sub-areas that carry a Lot, of Geometry.m_SurfaceArea / 64; the formula's spaceMultiplier parameter is always passed 1
GetCompanyMaxFittingWorkers dispatches in this order: ServiceCompanyData, then ExtractorCompanyData, then IndustrialProcessData
```

`WorkProvider.m_MaxWorkers` is a target the AI adjusts per pass — and only for a company with a property; a propertyless company's target never moves.
Moving in snaps it first: `PropertyProcessingSystem` sets it to at least two thirds of the property's fitting capacity, capped at that capacity, so a company never crawls up from the minimum.
All three AI systems run at `kUpdatesPerDay = 32` with `kMinimumEmployee = 5`:

```
CommercialAISystem:  -1 if m_MaxWorkers > 5 and service at or above m_MaxService
                     +1 if fully staffed, more than one fitting worker spare, and service at or below m_MaxService / 4
IndustrialAISystem   (four ordered tests, then clamp(m_MaxWorkers, 5, fittingWorkers)):
  worth < kMinWorthRequire = -50000            -> -2 if profit < 0, else -1
  worth < kMinWorthRequirePositiveProfit = -10000 and profit < 0 -> -1
  stock >= limit/2 and demand < production for the output resource -> -1
  fully staffed, room to grow, stock <= limit/4 -> +1
  a weightless output substitutes kMaxVirtualResourceStorage = 100000 for limit/2 and its half for limit/4
  worth and profit are CompanyStatisticData.m_LastUpdateWorth and m_Profit (below)
ExtractorAISystem:   -1 if m_MaxWorkers > 5 and stock at or above the upgrade-combined storage limit -- that guard is the only floor, there being no closing clamp here
                     else, fully staffed with room: jump to min(kMaximumInitEmployee = 80, fittingWorkers) while below 80, then +1 per pass
commercial and industrial, on a company not already seeking (a renting one only on a 1-in-4 roll per pass): live GetCompanyTotalWorth > kLowestCompanyWorth = -10000 enables PropertySeeker; at or below it, a propertyless company is Deleted and a renting one is left alone. An extractor has no worth test, and this pass seeks only a propertyless one.
```

`m_MaxWorkers` becomes jobs by level through `EconomyUtils.CalculateNumberOfWorkplaces`, which `citizens-and-households` records.

## Profit, the two profitability numbers, and the income statement

Sources: `src/Game/Game.Simulation/CompanyProfitabilitySystem.cs`, `src/Game/Game.Simulation/CompanyUtils.cs`, `src/Game/Game.Simulation/CompanyEconomyStatisticSystem.cs`, `src/Game/Game.Economy/EconomyUtils.cs`.

`GetCompanyProfitPerDay = GetCompanyProfitPerUnit * production - CalculateTotalWage(employees)`; `GetCompanyMaxProfitPerDay` is the projection at `m_MaxWorkers`, efficiency 1 and every worker at happiness 50, with no commercial saturation taper — and `zoning-buildings-and-land-value` records that `RentAdjustSystem` compares the asked rent against it.
`GetCompanyTotalWorth` is money plus every stocked resource at price plus every loaded owned truck's cargo — a commercial company's *output* stock at the market price, everything else (truck cargo included) at the industrial price.

```
CompanyProfitabilitySystem (kUpdatesPerDay = 1, UpdateFrame):
  m_Profitability  = clamp((totalWorth - m_LastTotalWorth) / 100, -127, 128) + 127
  m_LastTotalWorth = totalWorth
  // 127 is break-even; the clamp saturates at a worth change of -12,700 / +12,800
  same job rolls CompanyStatisticData monthly counters: m_MonthlyCustomerCount and m_MonthlyCostBuyingResources take the current counters, which reset -- "monthly" means "since the last daily pass"
```

**`CompanyProfitabilitySystem` looks `IndustrialProcessData` up on the wrong entity, so a commercial company's output stock is valued at the industrial price.**
The job resolves the company's `PrefabRef` and discards it, then reads `IndustrialProcessData` off the company entity — which never carries it, since `ProcessingCompany.GetPrefabComponents` and `StorageCompany.GetPrefabComponents` declare it prefab-side only — so the process stays `default`, `GetCompanyTotalWorth` sees output `NoResource`, and the market-price arm is never taken; `CompanyEconomyStatisticSystem` performs the same lookup correctly on the resolved prefab, a mod reading `Profitability` inherits the bug, and one fixing it changes the infoview.
Source: `src/Game/Game.Simulation/CompanyProfitabilitySystem.cs`, `src/Game/Game.Simulation/CompanyEconomyStatisticSystem.cs`, `src/Game/Game.Prefabs/ProcessingCompany.cs`, `src/Game/Game.Prefabs/StorageCompany.cs`.

**The building colour and the `Profitability` component are two different numbers with the same name and the same 127 midpoint.**
`CompanyUtils.GetCompanyProfitability` maps `CompanyStatisticData.m_Profit` onto 0–255 across `EconomyParameterData.m_ProfitabilityRange` (0 at `.x`, 127 at zero profit, 255 at `.y`; a flat 127 when `.x >= 0` and `.y <= 0`, and a saturated 0 or 255 when only one end degenerates), and its callers are the building-colour and company-infoview UI systems — only the component is the worth delta.
Source: `src/Game/Game.Simulation/CompanyUtils.cs`, `src/Game/Game.Rendering/ObjectColorSystem.cs`, `src/Game/Game.UI.InGame/CompanyInfoviewUISystem.cs`.

```
CompanyEconomyStatisticSystem (kUpdatesPerDay = 128, UpdateFrame) -- a PROJECTION, not a ledger:
  zeroes eleven fields, then recomputes from current state:
    m_RentPaid   = PropertyRenter.m_Rent  (an upkeep share exists in the code but see below)
    m_ElectricityPaid / m_WaterPaid / m_SewagePaid = the property's fulfilled consumption times the city's current ServiceFee -- sewage priced with the WATER fee, there being no sewage PlayerResource fee row
    m_WagePaid   = CalculateTotalWage over the Employee buffer
    commercial:  m_Income = ceil(production * marketPrice(output))
                 m_TaxPaid = ceil(production * servicePrice(output)) * m_AverageTaxRate / 100
    industrial:  m_Income = production * industrialPrice(output)
                 (extractors substitute m_LastUpdateProduce for the recomputation)
                 m_TaxPaid = (m_Income - m_CostBuyResource) * m_AverageTaxRate / 100
    m_CostBuyResource = implied input consumption at industrial prices
    m_GarbagePaid = ConsumptionData.m_GarbageAccumulation * garbage fee / renter count
    m_Worth = GetCompanyTotalWorth; m_LastUpdateWorth = m_Worth
    m_Profit = m_Income - (rent + wage + utilities + garbage + tax + inputs)
```

None of it is what the company actually paid — it is what it would pay at this instant's rates — and `IndustrialAISystem` hires and fires off `m_LastUpdateWorth` and `m_Profit`, so the projection drives real behaviour.

**Two asymmetries inside that job read like bugs and ship as facts.**
The commercial and industrial `m_TaxPaid` use different bases (margin-priced production against income minus inputs), and the rent's upkeep share is gated on `upkeep < m_Worth` while `m_Worth` is still 0 from the zeroing above, so the upkeep share is never added.
Source: `src/Game/Game.Simulation/CompanyEconomyStatisticSystem.cs`.

**The Production tab's per-consumer breakdown has four dead slots, and its Industry slot counts only office goods.**
`CityProductionStatisticSystem` keys `CityResourceUsage` by nine `Consumer` slots; `Retail`, `Commercial`, `Office` and `Heating` are requested nowhere in `src/`, and `Consumer.Industrial`'s only writer is `OfficeAISystem` booking the industrial sector's office-goods purchases — so a mod reading the breakdown gets four permanent zeros, and an Industry figure that is zero for every non-office resource.
Source: `src/Game/Game.Simulation/CityProductionStatisticSystem.cs`, `src/Game/Game.Simulation/OfficeAISystem.cs`.

## Dividends and leaving

Sources: `src/Game/Game.Simulation/CompanyDividendSystem.cs`, `src/Game/Game.Simulation/CompanyMoveAwaySystem.cs`, `src/Game/Game.Simulation/CompanyUtils.cs`.

```
CompanyDividendSystem (kUpdatesPerDay = 1, UpdateFrame):
  money >= 0 and employees exist: each employee's household gets max(0, money / (8 * count)), the company is debited that times count -- an eighth of cash daily, level-blind
CompanyMoveAwaySystem (kUpdatesPerDay = 16, UpdateFrame; skips prefabs with no WorkplaceData; the query excludes ExtractorCompany, so none of this fires for one):
  chance = (taxRate - 10) * 5 / 2      // the per-resource commercial, industrial or office rate, picked by ServiceAvailable / OfficeProperty
         + 5  if an uneducated-workers notification is up
         + 20 if an educated-workers notification is up
  NextInt(100) < chance -> MovingAway, every pass
  else worth < EconomyParameterData.m_CompanyBankruptcyLimit:
    stamp CompanyStatisticData.m_LastFrameLowIncome once, and MovingAway only after |frame - stamp| > 65536 -- a quarter game day of unbroken insolvency
  else: the stamp is rewritten to the current frame every pass, so the window restarts on any solvent pass
the same system's second job executes MovingAway promptly: property freed, notification icons removed, company Deleted -- it is not a state a company sits in
```

(VOLATILE: every system, component, field, formula and constant this file names — their declarations in `Game.Simulation`, `Game.Economy`, `Game.Companies`, `Game.Buildings`, `Game.City`, `Game.Agents`, `Game.Areas`, `Game.Prefabs`, `Game.Rendering` and `Game.UI.InGame` under `src/Game/`, at the files the sections cite.)
