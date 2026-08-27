# Taxes and the city budget

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`BudgetApplySystem` sums fifteen expense and fourteen income slots and adds the difference to `PlayerMoney` in 1/1024 slices, 1024 times a game day — but it is not `PlayerMoney`'s only writer, so a mod auditing the treasury cannot reconcile it against the slot sums alone: systems from loans and tile purchases to transit fares write the component directly.

## Tax rates

Sources: `src/Game/Game.Simulation/TaxSystem.cs`, `src/Game/Game.City/TaxRate.cs`, `src/Game/Game.Prefabs/TaxParameterData.cs`.

A tax rate is not a component: `TaxSystem` owns a persistent 92-slot `NativeArray<int>`, handed out by `GetTaxRates()` with `AddReader(JobHandle)`, and the layout is documented by an enum in another namespace:

```
Game.City.TaxRate: Main = 0, ResidentialOffset = 1, CommercialOffset = 2, IndustrialOffset = 3, OfficeOffset = 4, EducationZeroOffset = 5, CommercialResourceZeroOffset = 10, IndustrialResourceZeroOffset = 51, Count = 92
GetTaxRate(area)             = rates[0] + rates[(int)area]
GetResidentialTaxRate(level) = GetTaxRate(Residential) + rates[5 + level]
GetCommercialTaxRate(res)    = GetTaxRate(Commercial)  + rates[10 + denseIndex(res)]
GetIndustrialTaxRate(res)    = GetTaxRate(Industrial)  + rates[51 + denseIndex(res)]
GetOfficeTaxRate(res)        = GetTaxRate(Office)      + rates[51 + denseIndex(res)]
SetDefaults: rates[0] = 10, everything else 0
```

Every rate is a sum of the global and offsets, never stored absolutely, so a mod writing a rate writes an *offset* — the setters store one for you, `SetTaxRate` subtracting the global and the per-level and per-resource setters the area rate — and setting the global runs the `EnsureAreaTaxRateLimits` cascade, re-limiting the area rates, the job levels, and the per-resource offsets its own test picks against `TaxParameterData`'s seven `int2` limits — never the full offset block.
Industrial and office share the 51 block, which is why `SetIndustrialTaxRate` and `SetOfficeTaxRate` write the same slot.
Which per-resource sliders exist starts from the authored `TaxableResource` component (`src/Game/Game.Prefabs/TaxableResource.cs`): the taxation UI reads its managed `m_TaxAreas` array — a null or empty array shows the resource under each of the three zoned areas — while `Initialize` bakes a non-empty array into `TaxableResourceData.m_TaxAreas`, the byte the runtime tests; no resource carries the Residential bit, and the residential list never consults the array — residential tax is per job level instead.
`ClampResourceTaxRates` and `GetResourceTaxRateRange` select by that baked bit, and the `EnsureAreaTaxRateLimits` cascade picks by a different test again — `EconomyUtils.IsCommercialResource`, produceable-with-weight, and `IsOfficeResource` — so the authored array, the baked byte and the cascade's test are three places a resource's tax coverage can disagree.

## Collection

Sources: `src/Game/Game.Simulation/TaxSystem.cs`, `src/Game/Game.Agents/TaxPayer.cs`.

Tax is charged from an accrual on the payer, not computed at collection: income arrives into `TaxPayer.m_UntaxedIncome` with `m_AverageTaxRate` lerped toward the current rate weighted by the new income's share, so a slider change takes effect gradually per payer.
The writers are the production and wage systems — `ProcessingCompanySystem`, `ServiceCompanySystem`, `ExtractorCompanySystem` and, for households, `PayWageSystem` — not anything in `TaxPayer.cs` itself, which is only the struct.

```
TaxSystem (kUpdatesPerDay = 32, UpdateFrame), three jobs over three queries -- the definition of who pays what:
  residential: TaxPayer + UpdateFrame + Resources + Household
  commercial:  TaxPayer + UpdateFrame + Resources + ServiceAvailable
  industrial:  TaxPayer + UpdateFrame + Resources + ProcessingCompany, excluding StorageCompany and ServiceAvailable
PayTax: tax = round(0.01 * m_AverageTaxRate * m_UntaxedIncome)
  deduct round(m_PaidMultiplier * tax) from the payer's money
  industrial payer whose prefab output has m_Weight == 0 -> booked into the OfficeTaxableIncome statistic; the parallel IncomeSource.TaxOffice reassignment is a dead store, and the budget's TaxOffice row is filled from the statistic
  book m_UntaxedIncome * 32 into the per-area taxable-income statistic
  m_UntaxedIncome = 0; m_AverageTaxPaid = tax * 32
```

Office has no query and no multiplier of its own: an office company is caught by the industrial query and rebooked into the office statistic inside `PayTax`, with `m_PaidMultiplier` taken from the industrial slot of `ModeSettingData.m_TaxPaidMultiplier` regardless.

## Fees

Sources: `src/Game/Game.Simulation/ServiceFeeSystem.cs`, `src/Game/Game.City/ServiceFee.cs`, `src/Game/Game.Prefabs/ServiceFeeParameterData.cs`.

Every charge reads the `ServiceFee` buffer on the city entity (`ServiceFeeSystem.GetFee`); the parameter component seeds it at city creation, and no charge reads the parameter afterwards — other systems still do, for the flags and baselines below and for the consumption curves other topics own.
Whether a fee is player-adjustable at all is `FeeParameters.m_Adjustable` on `ServiceFeeParameterData` — check the field before building anything against a fee, because adjustability is authored per fee, not a property of fees in general.
Four `PlayerResource` members have no fee parameters — `Mail`, `PublicTransport`, `Sewage` and `Parking` — and `GetFeeParameters` returns an empty default for each: parking income is booked from parking-lane fees in `PersonalCarAISystem`, a prefab base with the district and building `ParkingFee` modifiers applied by `ParkingLaneDataSystem`; transport income is ticket prices in `ResidentAISystem`.
`GetEducationResource` maps school levels 3 and 4 onto the one `HigherEducation` fee bucket.
`ServiceFeeSystem` (`kUpdatesPerDay = 128`, no `UpdateFrame`) charges through `PayFeeJob` and books into a per-resource `CollectedCityServiceFeeData` buffer of internal, export and import figures scaled to daily rates; `GetServiceFees(resource)` returns `int3(internal, export, import)` and `GetServiceFeeIncomeEstimate` is `internalCount * fee`.

**Three disagreeing fee-default sets exist, and only the live buffer is charged.**
`ServiceFeeParameterData.m_Default`, the C# switch `ServiceFee.GetDefaultFee` (reached from the new-game deserialize path and from two save migrations — the garbage-fee reset, and a missing-Water backfill), and the city's `ServiceFee` buffer all differ at 1.6.0f1 — read the buffer, and treat the other two as where a stale figure hides.
Source: `src/Game/Game.City/ServiceFee.cs`, `src/Game/Game.Prefabs/ServiceFeeParameterData.cs`, `src/Game/Game.Simulation/CitySystem.cs`, `src/Game/Game.Serialization/RequiredComponentSystem.cs`.

## Service trade

Sources: `src/Game/Game.Prefabs/OutsideTradeParameterData.cs`, `src/Game/Game.Simulation/CityServiceBudgetSystem.cs`.

Utility trade is priced per unit on `OutsideTradeParameterData` (`m_ElectricityImportPrice`, `m_ElectricityExportPrice`, `m_WaterImportPrice`, `m_WaterExportPrice`, `m_SewageExportPrice`), and the five outside service imports are per capita, each gated on `CityOption.ImportOutsideServices`:

```
fee helper returns -(int)(fee * (population / m_OCServiceTradePopulationRange + 1) * m_OCServiceTradePopulationRange) with CityModifierType.CityServiceImportCost applied; the expense slot stores its negation, so the stored cost is positive
```

So the charge steps in whole population blocks, and turning the city option off zeroes all five.

**`Exportable(Sewage)` is always false, and `Importable(Sewage)` is true whenever `m_SewageExportPrice` is nonzero — inverted relative to the field name.**
`OutsideTradeParameterData.GetFee`'s sewage arm returns `m_SewageExportPrice` on the *import* branch and 0 on the export branch, and `Importable`/`Exportable` are just "is the fee nonzero".
Source: `src/Game/Game.Prefabs/OutsideTradeParameterData.cs`.

## The budget

Sources: `src/Game/Game.Simulation/CityServiceBudgetSystem.cs`, `src/Game/Game.Simulation/BudgetApplySystem.cs`, `src/Game/Game.City/IncomeSource.cs`, `src/Game/Game.City/ExpenseSource.cs`.

```
IncomeSource:  TaxResidential, TaxCommercial, TaxIndustrial, FeeHealthcare, FeeElectricity, GovernmentSubsidy, FeeEducation, ExportElectricity, ExportWater, FeeParking, FeePublicTransport, TaxOffice, FeeGarbage, FeeWater, Count
ExpenseSource: SubsidyResidential, LoanInterest, ImportElectricity, ImportWater, ExportSewage, ServiceUpkeep, SubsidyCommercial, SubsidyIndustrial, SubsidyOffice, ImportPoliceService, ImportAmbulanceService, ImportHearseService, ImportFireEngineService, ImportGarbageService, MapTileUpkeep, Count
```

`CityServiceBudgetSystem` fills the slots each pass: the four tax incomes from `TaxSystem.GetEstimatedTaxAmount(area, TaxResultType.Income, ...)` and the four subsidies from the same call with `TaxResultType.Expense` negated — so a negative slider is an expense row, not a negative income, and a mod reading only the income slots undercounts.
The estimate is `rate * taxable-income statistic / 100` per resource or job level, so **the budget panel is projected from `CityStatisticsSystem`, not from what was collected**.
`BudgetApplySystem` and `CityServiceBudgetSystem` iterate the enums as the bare literals 15 and 14 rather than `Count`, so inserting a member reindexes the statistics and silently drops the last one.

**The sign convention flips between accessors.**
`GetExpense` returns a cost positive, `GetTotalExpenses` sums with `-=` and returns negative, `GetBalance = GetTotalIncome + GetTotalExpenses`, and `GetMoneyDelta` divides the same sum by 24 — an hourly figure.
Source: `src/Game/Game.Simulation/CityServiceBudgetSystem.cs`.

## Loans

Sources: `src/Game/Game.Tools/LoanSystem.cs`, `src/Game/Game.Simulation/LoanUpdateSystem.cs`, `src/Game/Game.Simulation/CityServiceBudgetSystem.cs`.

```
LoanSystem.GetTargetInterest:
  value = 100 * lerp(interestRange.x, interestRange.y, saturate(loan / max(1, creditworthiness)))
  CityModifierType.LoanInterest applied
  return max(0, 0.01 * value)
CalculateLoan: LoanInfo { m_Amount, m_DailyInterestRate, m_DailyPayment = round(amount * rate) }, or an all-zero LoanInfo when amount <= 0
ChangeLoan clamps to [max(0, currentLoan - max(0, money)), Creditworthiness] and enqueues; the queued action does PlayerMoney.Add(newAmount - oldAmount) and stamps a fresh m_LastModified -- no separate disbursement
```

The rate rises linearly with how much of the credit line is drawn, from `EconomyParameterData.m_LoanMinMaxInterestRate.x` at nothing to `.y` at the limit, and every loan figure is daily — there is no monthly loan payment to convert from.
The interest is charged as the `ExpenseSource.LoanInterest` slot, filled from `CalculateLoan(...).m_DailyPayment` and spent by `BudgetApplySystem` like any other expense.
`Creditworthiness` is a running sum of `MilestoneData.m_LoanLimit` per reached milestone (`src/Game/Game.Simulation/MilestoneSystem.cs`).

**`LoanUpdateSystem` charges nothing, whatever its name says.**
Its job computes the interest and discards the result; what it does is fire a `TriggerType.UnpaidLoan` when the loan has stood untouched for more than 262,144 frames while the player holds positive money.
Source: `src/Game/Game.Simulation/LoanUpdateSystem.cs`.

(VOLATILE: every system, component, enum member list, slot index, formula and `Source:` path this file names — their declarations in `Game.Simulation`, `Game.Tools`, `Game.City`, `Game.Agents`, `Game.Companies`, `Game.Citizens`, `Game.Triggers`, `Game.Pathfind`, `Game.Prefabs`, `Game.Prefabs.Modes` and `Game.Serialization` under `src/Game/`, at the files the sections cite.)
