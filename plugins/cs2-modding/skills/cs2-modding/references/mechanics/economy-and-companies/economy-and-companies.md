# Economy and companies

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

The economy is companies: entities instantiated from company prefabs that rent a building, employ citizens, and hold everything they own — money included — in a `Game.Economy.Resources` buffer.
Each company runs one recipe, the `IndustrialProcessData` on its prefab, and the sum of those recipes is the whole production graph: extractors make materials, industry makes material goods, offices make weightless goods, commercial companies convert or resell them to citizens.
The zone side is the prefab's `CompanyPrefab.zone`, whose `AreaType.Commercial` and `AreaType.Industrial` branches are the only ones adding company data and role tags (`src/Game/Game.Prefabs/CompanyPrefab.cs`).
An office company is zoned Industrial: office is `ZoneData`'s `ZoneFlags.Office` bit rather than a third area type, and [`zoning-buildings-and-land-value`](../zoning-buildings-and-land-value/zoning-buildings-and-land-value.md) records the test.
Production per day is one formula over building efficiency, a sector efficiency and the employees' workforce; profit is production times a one-line price spread minus wages, and everything downstream — hiring, tax accrual, bankruptcy — reads those two numbers.
The city's own money is a `PlayerMoney` component moved by `BudgetApplySystem`, which sums income and expense slots filled from tax projections, service fees, utility trade and loan interest.
Tourism is a city-level attractiveness score turned into tourist households whose spending reaches companies through the ordinary sale paths.

## The map

Default reads: a parameter singleton is a one-type `EntityQuery` and `GetSingleton<T>` ([`ecs-in-this-game`](../../technique/ecs-in-this-game/ecs-in-this-game.md) carries the call); a company-prefab component is reached from the instance's `PrefabRef.m_Prefab`; a per-resource component sits on that resource's prefab entity, indexed by `ResourceSystem.GetPrefabs()` (`src/Game/Game.Prefabs/ResourcePrefabs.cs`); a row states its own shape only where it differs.

Resources:

| The game models | Component | Access shape |
| --- | --- | --- |
| A resource | `Game.Economy.Resource`, a `ulong` enum of powers of two — no `[Flags]` attribute, but a bitmask in use (`src/Game/Game.Economy/Resource.cs`) | a *set* of resources is one value; `EconomyUtils.GetResourceIndex` converts bit to dense index, `GetResource(int)` back, and `ResourceIterator` is the loop everything walks it with (`src/Game/Game.Economy/EconomyUtils.cs`, `ResourceIterator.cs`); per-resource arrays take the dense index, component fields hold the bit |
| Per-resource balance and category | `ResourceData { m_Price, m_IsProduceable, m_IsTradable, m_IsMaterial, m_IsLeisure, m_Weight, m_BaseConsumption, m_CarConsumption, m_RequireNaturalResource, m_NeededWorkPerUnit, ... }` (`src/Game/Game.Prefabs/ResourceData.cs`) | the flags decide which economy a resource belongs to: `m_Weight == 0` is what the code means by "office", `m_IsMaterial` marks extractor output, `m_IsProduceable` gates company spawning, `m_IsTradable` gates outside trade and warehouses, `m_IsLeisure` gates a resource out of baseline household consumption unless leisure is asked for (`HouseholdBehaviorSystem`'s weight functions read it); `m_Price.x` is the industrial price, `m_Price.y` the commercial margin, `m_NeededWorkPerUnit` an `int2` of (industrial, commercial) work |
| Whether a resource appears in the taxation panel, and which areas' clamp covers it | `TaxableResourceData { byte m_TaxAreas }` (`src/Game/Game.Prefabs/TaxableResourceData.cs`) | component presence gates the panel's resource list; the per-area split is the authored `TaxableResource` array the UI reads, baked into this bitmask `1 << (TaxAreaType - 1)` for `TaxSystem`'s clamping and range functions |
| Stock and money | `Resources { m_Resource, m_Amount }` buffer (`src/Game/Game.Economy/Resources.cs`) | on the company (and household) entity; money is the `Resource.Money` row, written through `EconomyUtils.AddResources` and `SetResources` |

The company:

| The game models | Component | Access shape |
| --- | --- | --- |
| The recipe | `IndustrialProcessData { m_Input1, m_Input2, m_Output, m_MaxWorkersPerCell }`, each stack a `ResourceStack { m_Resource, m_Amount }` (`src/Game/Game.Prefabs/IndustrialProcessData.cs`) | company prefab; the join over all recipes is the production graph ([production-graph.md](production-graph.md)); its `m_WorkPerUnit` and `m_IsImport` have no consumer in `src/` — the operative work-per-unit is `ResourceData.m_NeededWorkPerUnit` |
| Company role | empty tags `CommercialCompany`, `IndustrialCompany`, `OfficeCompany`, `ProcessingCompany`, `ExtractorCompany`, `TransportCompany`; `StorageCompany` carries a field and `BuyingCompany` two (`src/Game/Game.Companies/`) | presence of the tag is the state; which tags an instance gets is declared by the prefab's `ComponentBase` conditionally on its recipe (`src/Game/Game.Prefabs/ProcessingCompany.cs`, `ServiceCompany.cs`, `StorageCompany.cs`, `ExtractorCompany.cs`, `CompanyPrefab.cs`) |
| "Is commercial" | `ServiceAvailable { m_ServiceAvailable, m_MeanPriority }` (`src/Game/Game.Companies/ServiceAvailable.cs`) | added by the `ServiceCompany` prefab component; the absence of `ServiceAvailable`, tested in chunk or lookup form, is how the economy systems spell `isIndustrial` |
| Commercial capacity | `ServiceCompanyData { m_MaxService, m_MaxWorkersPerCell, ... }` (`src/Game/Game.Companies/ServiceCompanyData.cs`) | company prefab; its `m_WorkPerUnit` is written as a literal 0 and consumed nowhere — the operative work-per-unit is `ResourceData.m_NeededWorkPerUnit` |
| A warehouse | `StorageCompanyData { m_StoredResources, ... }` plus `StorageLimitData { m_Limit }` (`src/Game/Game.Prefabs/StorageCompanyData.cs`, `src/Game/Game.Companies/StorageLimitData.cs`) | company prefab; a storage prefab adds no `ProcessingCompany` tag, which is how warehouses fall out of every processing query |
| Headcount target | `WorkProvider { m_MaxWorkers, m_UneducatedCooldown, m_EducatedCooldown, notification entities, m_EfficiencyCooldown }` (`src/Game/Game.Companies/WorkProvider.cs`) | instance; the sector AI adjusts it per pass ([production-and-profit.md](production-and-profit.md)) |
| Staff | `Employee { m_Worker, m_Level }` buffer and the `FreeWorkplaces` vacancy cache (`src/Game/Game.Companies/`) | on the company; the hiring pipeline is [`citizens-and-households`](../citizens-and-households/citizens-and-households.md) |
| Trading costs | `TradeCost { m_Resource, m_BuyCost, m_SellCost, m_LastTransferRequestTime }` buffer (`src/Game/Game.Companies/TradeCost.cs`) | on the company ([trade-and-restocking.md](trade-and-restocking.md)) |
| The infoview number | `Profitability { m_Profitability, m_LastTotalWorth }` (`src/Game/Game.Companies/Profitability.cs`) | instance; a worth delta, not an income statement — [production-and-profit.md](production-and-profit.md), traps included |
| The income statement | `CompanyStatisticData` (`src/Game/Game.Companies/CompanyStatisticData.cs`) | instance; a projection recomputed from scratch each pass ([production-and-profit.md](production-and-profit.md)) |
| Tax accrual | `TaxPayer { m_UntaxedIncome, m_AverageTaxRate, m_AverageTaxPaid }` (`src/Game/Game.Agents/TaxPayer.cs`) | instance ([taxes-and-budget.md](taxes-and-budget.md)) |
| A hotel | `LodgingProvider { m_FreeRooms, m_Price }` plus a `Renter` buffer (`src/Game/Game.Companies/LodgingProvider.cs`) | added when the recipe's output is `Lodging` ([tourism-and-lodging.md](tourism-and-lodging.md)) |

The city — each on the `CitySystem.City` entity unless the row says otherwise:

| The game models | Component | Access shape |
| --- | --- | --- |
| The treasury | `PlayerMoney` with `const kMaxMoney = 2000000000` and `m_Unlimited` (`src/Game/Game.City/PlayerMoney.cs`) | component; the `money` getter returns `2000000000` outright while unlimited, so a read there is not a balance |
| Loans | `Loan { m_Amount, m_LastModified }`, `Creditworthiness { m_Amount }` (`src/Game/Game.Simulation/Loan.cs`, `Creditworthiness.cs`) | components; [taxes-and-budget.md](taxes-and-budget.md) |
| Fees charged | `ServiceFee { m_Resource: PlayerResource, m_Fee }` buffer (`src/Game/Game.City/ServiceFee.cs`) | seeded at city creation from `ServiceFeeParameterData.GetDefaultFees()` (`src/Game/Game.Simulation/CitySystem.cs`); every charge reads this buffer, while the parameter singleton stays live for its adjustability flags and its two fee-consumption curves |
| Specialization | `SpecializationBonus` buffer, dense-resource-indexed (`src/Game/Game.City/SpecializationBonus.cs`) | [production-and-profit.md](production-and-profit.md) |
| Tourism | `Tourism { m_CurrentTourists, m_AverageTourists, m_Attractiveness, m_Lodging }` (`src/Game/Game.City/Tourism.cs`) | [tourism-and-lodging.md](tourism-and-lodging.md) |
| Tax rates | not a component: `TaxSystem` owns a persistent 92-slot `NativeArray<int>` handed out by `GetTaxRates()` with an `AddReader(JobHandle)` protocol (`src/Game/Game.Simulation/TaxSystem.cs`) | layout documented by `Game.City.TaxRate`; [taxes-and-budget.md](taxes-and-budget.md) |
| Where city money comes from and goes | the `IncomeSource` and `ExpenseSource` enums (`src/Game/Game.City/`) | read through `CityServiceBudgetSystem`'s accessors ([taxes-and-budget.md](taxes-and-budget.md)) |

Where the tuning numbers live, all singletons:

| Family of numbers | Component |
| --- | --- |
| Sector efficiencies, wages, bankruptcy limit, loan interest range, specialization ceiling and coefficient, office consumption per industrial unit, profitability range, start money | `EconomyParameterData` (`src/Game/Game.Prefabs/EconomyParameterData.cs`) |
| The seven tax-limit ranges | `TaxParameterData` (`src/Game/Game.Prefabs/TaxParameterData.cs`) |
| Fee defaults, maxima and adjustability — nine `FeeParameters { m_Default, m_Max, m_Adjustable }` | `ServiceFeeParameterData` (`src/Game/Game.Prefabs/ServiceFeeParameterData.cs`) |
| Utility trade prices, per-capita outside service fees, weight and distance costs per connection type | `OutsideTradeParameterData` (`src/Game/Game.Prefabs/OutsideTradeParameterData.cs`) |
| Extraction consumption rates and full-concentration thresholds | `ExtractorParameterData` (`src/Game/Game.Prefabs/ExtractorParameterData.cs`) |
| Tourism weather and temperature coefficients — the component's terrain half (forest, shore, height) is `TerrainAttractivenessSystem`'s | `AttractivenessParameterData` (`src/Game/Game.Prefabs/AttractivenessParameterData.cs`) |
| The service and hotel no-customers thresholds, and the two notification prefab entities | `CompanyNotificationParameterData` (`src/Game/Game.Prefabs/CompanyNotificationParameterData.cs`); its `m_NoInputCostLimit` is authored but read nowhere — the live no-inputs gate is `BuyingCompanySystem.kNotificationCostLimit` |
| The worker-notification delays and limits, and the senior-employee level | `WorkProviderParameterData` (`src/Game/Game.Prefabs/WorkProviderParameterData.cs`) |

## Traps

Each sibling carries further traps beside its listings.

**A field initializer on a prefab-authoring class is a Unity-serialized default, not the value.**
`EconomyPrefab`, `ExtractorParameterPrefab`, `AttractivenessParametersPrefab` and `OutsideTradeParameterPrefab` declare serialized fields — many with C# initializers, and some, the wages and the utility trade prices among them, with none at all — and copy them into their parameter components at initialization; the shipped asset overwrites the fields first, and read live at 1.6.0f1 many differ from their initializers, several by an order of magnitude.
Only a `const` or `static readonly` the code reads is citable as a number.
Source: `src/Game/Game.Prefabs/EconomyPrefab.cs`, `src/Game/Game.Prefabs/ExtractorParameterPrefab.cs`, `src/Game/Game.Prefabs/AttractivenessParametersPrefab.cs`, `src/Game/Game.Prefabs/OutsideTradeParameterPrefab.cs`.

**A game mode rewrites this topic's parameters on load.**
The `Game.Prefabs.Modes` classes reassign the parameter singletons and multiply recipe amounts after prefab initialization on every load, per the loaded mode's authored list, and `TaxSystem` separately latches `ModeSettingData.m_TaxPaidMultiplier` into collected tax with nothing in the rates recording it — so a retune written at initialization can silently vanish, and a collected figure can disagree with the rate that produced it.
Source: `src/Game/Game.Prefabs.Modes/GameModeSystem.cs`, `src/Game/Game.Prefabs.Modes/ProcessingCompanyGlobalMode.cs`, `src/Game/Game.Simulation/TaxSystem.cs`.

**There is no one office company test, and the three in play disagree at the edges.**
The `OfficeCompany` tag is added when `EconomyUtils.IsOfficeResource(output)` holds — a declared four-resource mask; the simulation's own test is output weight zero (`ProcessingCompanySystem.IsOffice`, and `TaxSystem.PayTax` rebooking an industrial payer into the office taxable-income statistic), which also fires for the weightless leisure resources and never for Meals, and is kept from mislabelling a hotel or bar only by `isCommercial` guards and the tax query's `ServiceAvailable` exclusion; the tax rate picked for move-away keys on the property's `OfficeProperty` tag instead.
Source: `src/Game/Game.Prefabs/ProcessingCompany.cs`, `src/Game/Game.Simulation/ProcessingCompanySystem.cs`, `src/Game/Game.Simulation/TaxSystem.cs`, `src/Game/Game.Simulation/CompanyUtils.cs`.

**`kUpdatesPerDay` is not one convention.**
A system pairing it with `UpdateFrame` partitioning (interval `262144 / (k * 16)`) touches each entity k times a day, while one without (interval `262144 / k`) runs k passes a day — and `CityServiceBudgetSystem` is neither, sitting in the `ModificationEnd` phase with no interval override, so it recomputes the whole budget on every pass of that phase.
Source: `src/Game/Game.Simulation/TaxSystem.cs`, `src/Game/Game.Simulation/TradeSystem.cs`, `src/Game/Game.Simulation/CityServiceBudgetSystem.cs`, `src/Game/Game.Common/SystemOrder.cs`.

**The names that look like the production chain are dead.**
`ProductionChainDataSystem` is an empty `OnUpdate` on a zero-size struct and `SystemOrder.cs` never registers it; `EconomyParameterData.m_ExtractorCompanyExportMultiplier`, `m_ShopPossibilityIncreaseDivider` and `m_PerOfficeResourceNeededForIndustrial` appear only in the prefab, the struct and the mode class, and the live office-demand path reads `m_OfficeResourceConsumedPerIndustrialUnit` instead — struct fields rather than constants, so the empty grep is real evidence.
Source: `src/Game/Game.Simulation/ProductionChainDataSystem.cs`, `src/Game/Game.Prefabs/EconomyParameterData.cs`, `src/Game/Game.Simulation/OfficeAISystem.cs`.

## Formulas

The price spread every profit figure is built from:

```
EconomyUtils.GetCompanyProfitPerUnit (src/Game/Game.Economy/EconomyUtils.cs):
  ((isIndustrial ? industrialPrice(output) : marketPrice(output)) * output.m_Amount
    - input1.m_Amount * industrialPrice(input1)
    - input2.m_Amount * industrialPrice(input2)) / output.m_Amount
  // industrialPrice = ResourceData.m_Price.x; marketPrice = m_Price.x + m_Price.y
```

That asymmetry — `ResourceData.m_Price.y` accruing only to commercial sellers — is the retail margin, and it is why a pass-through retail company can be profitable at all.

## Mechanisms

| Mechanism | Vanilla owner | Listing |
| --- | --- | --- |
| The production graph, its access shapes, company spawning | `IndustrialProcessData` on company prefabs; `CommercialSpawnSystem`, `IndustrialSpawnSystem`, `ZonePrefabInitializeSystem` | [production-graph.md](production-graph.md) |
| Production, efficiency, hiring, profit, dividends, bankruptcy | `ProcessingCompanySystem`, `ServiceCompanySystem`, `ExtractorCompanySystem`, the three sector AI systems, `WorkProviderSystem`, `CompanyProfitabilitySystem`, `CompanyEconomyStatisticSystem`, `CompanyDividendSystem`, `CompanyMoveAwaySystem` | [production-and-profit.md](production-and-profit.md) |
| Sales, restocking, trade costs, imports and exports, office sales | `ResourceBuyerSystem`, `BuyingCompanySystem`, `TradeSystem`, `ResourceExporterSystem`, `DeliveryTruckAISystem`, `OfficeAISystem` | [trade-and-restocking.md](trade-and-restocking.md) |
| The Production tab's per-consumer ledger | `CityProductionStatisticSystem` | [production-and-profit.md](production-and-profit.md), traps |
| Taxation, fees, service trade, the budget, loans | `TaxSystem`, `ServiceFeeSystem`, `CityServiceBudgetSystem`, `BudgetApplySystem`, `LoanSystem`, `LoanUpdateSystem` | [taxes-and-budget.md](taxes-and-budget.md) |
| Extraction economics and depletion | `ExtractorCompanySystem`, `AreaLotSimulationSystem` | [extraction-and-depletion.md](extraction-and-depletion.md) |
| Attractiveness, tourists, lodging | `TourismSystem`, `AttractionSystem`, `LodgingProviderSystem` | [tourism-and-lodging.md](tourism-and-lodging.md) |
| Production-driven unlocks | `ProcessingRequirementSystem` | below |

**Production-driven unlocks.**
`ProcessingRequirementData { m_ResourceType, m_MinimumProducedAmount }` on an unlock-requirement prefab fires an `Unlock` event when the city's cumulative production of that resource reaches the threshold, writing `UnlockRequirementData.m_Progress` either way — the running total while locked, the threshold itself once reached (`src/Game/Game.Prefabs/ProcessingRequirementSystem.cs`).
The counter it reads is `ProcessingCompanySystem`'s persistent per-resource array, accumulated by both processing and extractor jobs; the unlock machinery around the event is [`city-state-and-progression`](../city-state-and-progression/city-state-and-progression.md).

## Bridges

- [`ecs-in-this-game`](../../technique/ecs-in-this-game/ecs-in-this-game.md) — the `GetSingleton<T>` behind every parameter row, and `UpdateFrame` partitioning behind every rate here.
- [`performance-and-memory`](../../technique/performance-and-memory/performance-and-memory.md) — the reader/writer handle protocol this topic uses more than any other: `TaxSystem.GetTaxRates()` + `AddReader`, `CountCompanyDataSystem`'s getters, `CityServiceBudgetSystem.GetIncomeArray`/`GetExpenseArray` + `AddArrayReader`, `ServiceFeeSystem.GetFeeQueue` + `AddQueueWriter`, `ResourceSystem.GetPrefabs` + `AddPrefabsReader` — reading one of these without registering back races the owning system's next write.
- [`patching`](../../technique/patching/patching.md) — several of this area's thresholds are `static readonly` fields and bare literals on the system classes — the spawn roll, the company-deletion worth floor, the insolvency window, the dividend divisor — so for those there is no component to write and the routes are a Harmony patch or a fork; check where a threshold lives before choosing, since their neighbours (the bankruptcy limit, the worker-notification limits) are prefab data, and a write to prefab data needs the re-apply-on-load discipline because the mode pass above rewrites it.
- [`prefabs-and-assets`](../../technique/prefabs-and-assets/prefabs-and-assets.md) — every recipe, price and parameter here is authored on a prefab; the `ComponentBase` triple deciding a company's archetype from its recipe is the pattern a mod adding a company kind implements, and `Game.Prefabs.ResourceSystem` interning every resource prefab into the array behind `ResourcePrefabs[resource]` is why a mod's own resource must appear in that query.
- [`save-serialization`](../../technique/save-serialization/save-serialization.md) — a struct holding one resource serializes it as an `sbyte` dense *index*, making the order of `Resource`'s members a save-compatibility surface, while a struct holding a resource *set* — `StorageCompanyData.m_StoredResources` and its kin — writes the raw `ulong` mask, making the members' bit values a second one; `Resources.Deserialize` clamps a non-money row to `[0, 1000000]` for a save older than `Version.resetNegativeResource`, and `TaxSystem.Deserialize` shifts the shared industrial/office per-resource block by one to insert Fish for a save without `FormatTags.FishResource`.
- [`zoning-buildings-and-land-value`](../zoning-buildings-and-land-value/zoning-buildings-and-land-value.md) — the border is the building: that topic owns the rent a property asks and the upkeep it costs, this one owns what the renting company earns, meeting at `RentAdjustSystem` reading `EconomyUtils.GetCompanyMaxProfitPerDay` and `BuildingUpkeepSystem` summing renter worth (`GetCompanyTotalWorth` among it) to decide whether the upkeep share is charged to the renters or the building decays; the `Efficiency` buffer production reads lives on the building, and commercial and industrial demand are per resource, so the demand loop is half this topic's (`CountCompanyDataSystem` publishes it).
- [`citizens-and-households`](../citizens-and-households/citizens-and-households.md) — the other end of employment: that topic produces a `Worker`, this one consumes it as an `Employee` and turns happiness into production; `PayWageSystem` debits the company, `CompanyDividendSystem` pays households, and a household's shopping money becomes company revenue in `ResourceBuyerSystem`.
- [`city-services-and-coverage`](../city-services-and-coverage/city-services-and-coverage.md) — owns most `EfficiencyFactor` writers production multiplies by, and the budget slider behind `ServiceBudget`; this topic owns the money side of that slider (`ExpenseSource.ServiceUpkeep`).
- [`city-state-and-progression`](../city-state-and-progression/city-state-and-progression.md) — owns `CityModifier`, policies, milestones and unlocks; every formula here that says `ApplyModifier` reads its machinery, `Creditworthiness` is a running sum of `MilestoneData.m_LoanLimit` (`src/Game/Game.Simulation/MilestoneSystem.cs`), and the game-mode rewrite in Traps is its mode system.
- [`environment-and-pollution`](../environment-and-pollution/environment-and-pollution.md) — owns the natural-resource grid extraction runs on: the cells, `AreaResourceSystem`'s recomputation of area state, and forestry depletion entirely; [extraction-and-depletion.md](extraction-and-depletion.md) marks the seam.
- [`transportation-and-vehicles`](../transportation-and-vehicles/transportation-and-vehicles.md) — owns the delivery truck as a vehicle: its body and payload components, the odometer-and-maintenance loop, and the side-effect data; `DeliveryTruckSelectData.TrySelectItem` — whether a shipment is worth a truck — and `DeliveryTruckAISystem`'s money moves are this topic's, in [trade-and-restocking.md](trade-and-restocking.md).
- [`simulation-time-and-units`](../simulation-time-and-units/simulation-time-and-units.md) — 262,144 frames per game day is the unit every interval, insolvency window and stay length here is written in.
- [`binding-layer`](../../../../cs2-modding-ui/references/binding-layer/binding-layer.md) — `UIEconomyConfigurationPrefab`'s `m_IncomeItems`/`m_ExpenseItems` are the game's own grouping of the income and expense enums into budget-panel rows; read that prefab rather than inventing a grouping.

(VOLATILE: every component, field, enum, system, constant and `Source:` path this file names, the parameter-ownership map most of all — their declarations under `src/Game/` in `Game.Economy`, `Game.Companies`, `Game.Prefabs`, `Game.Prefabs.Modes`, `Game.City`, `Game.Agents`, `Game.Simulation`, `Game.Common`, `Game.Buildings` and `Game.Zones`, at the files the rows and traps cite.)
