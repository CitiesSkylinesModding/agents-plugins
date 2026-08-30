# Budget, workforce and upkeep

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

The chain a mod tunes here is: budget percentage → efficiency factor → efficiency product → vehicles fielded, coverage magnitude, and everything downstream of those.
The budget itself is `ServiceBudgetData { Entity m_Service; int m_Budget; }`, a buffer of percentages on a plain singleton entity, reached through `CityServiceBudgetSystem.GetServiceBudget(servicePrefab)` / `SetServiceBudget(servicePrefab, percentage)`.
The buffer only gains an entry when a slider is first moved, so an untouched city's buffer is legitimately empty: a missing entry reads as 100, while `GetServiceBudget` returns 0 for a prefab outside the system's collected budget map and `SetServiceBudget` silently no-ops on one.
Source: `src/Game/Game.Simulation/ServiceBudgetData.cs`, `src/Game/Game.Simulation/CityServiceBudgetSystem.cs`.

## The slider's two effects

**Effect one: the money upkeep scales linearly.**
Source: `src/Game/Game.Simulation/CityServiceBudgetSystem.cs`, `src/Game/Game.Simulation/CityServiceUpkeepSystem.cs`.

```
CityServiceBudgetSystem.CityServiceBudgetJob, per building, per upkeep line (prefab lines plus installed upgrades'; a line flagged m_ScaleWithUsage is multiplied by ServiceUsage.m_Usage — a prefab line by the building's, an upgrade line by the upgrade's own; an inactive upgrade under an active building keeps a tenth of its Money line and drops its other lines):
  value = amount * marketPrice(resource), skipped when amount <= 0
  cost  = value                             // a non-money line is never budget-scaled
  on the Resource.Money line only:
    value  = ApplyModifier(value, CityServiceBuildingBaseUpkeepCost)  // += delta.x, then += value*delta.y
    value += GetUpkeepOfEmployeeWage(...)   // 0 outright when the building is Inactive; wages join after the city modifier
    value *= 0.1                            when the building is Inactive
    cost   = value * (budget / 100)
  accumulate rounded cost into m_Cost and rounded value into m_FullCost on CollectedCityServiceUpkeepData
```

**The slider cuts the wage bill along with the maintenance bill** — wages join the money line above before the budget multiply.
`ServiceUsage.m_Usage` is written by five AI systems — hospital (patients over capacity), school (students over capacity), emergency shelter, and the two utility systems [`utilities-and-flow-networks`](../utilities-and-flow-networks/utilities-and-flow-networks.md) owns — plus a load-time back-fill seeding 1 on any budget-bearing building that lacks the component; every other service building's upkeep is flat (re-check: the files under `src/Game/` naming `ServiceUsage`).

**Effect two: every building of the service takes an efficiency factor.**
`CityServiceEfficiencySystem` (at `ModificationEnd`, re-run over every building when a budget changes) writes `EfficiencyFactor.ServiceBudget = m_ServiceBudgetEfficiencyFactor.Evaluate(budget / 100)` — but only where the prefab, or an installed upgrade, has a `Resource.Money` upkeep line, and writes 1 otherwise.
**A service building with no money upkeep is immune to its own budget slider**; `HasMoneyUpkeep` is the test.
The curve is asset data on `BuildingEfficiencyParameterData`; read live, its shape is a flat floor below half budget, a steep rise across the 50–100% band, and a shallow, capped gain above 100% — the re-check is evaluating `m_ServiceBudgetEfficiencyFactor` on the live singleton.
Source: `src/Game/Game.Buildings/CityServiceEfficiencySystem.cs`, `src/Game/Game.Prefabs/BuildingEfficiencyParameterData.cs`.

**The slider's bounds are frontend literals, and the C# setter has none.**
`budget-slider-item.tsx` renders `min: 50, max: 150` and the panel shows the slider only where `ServiceData.m_BudgetAdjustable` is set (`budgetAdjustable` in the bound data); `CityServiceBudgetSystem.SetServiceBudget` applies no clamp at all, so a mod may set any integer and the curve clamps outside its own domain.
Which services are adjustable is asset data: read live, every service prefab but one was — `Landscaping` is the exception — and the re-check is an `ecs_query` on `Game.Prefabs.ServiceData` with `PrefabSystem.GetPrefabName` as the label.
Source: `src/Game/Game.UI.InGame/ServiceBudgetUISystem.cs`, `src/Game/Game.Prefabs/ServiceData.cs`, the game's UI bundle (module `budget-slider-item.tsx` in the reformatted copy).

## The efficiency product

Source: `src/Game/Game.Buildings/BuildingUtils.cs`, `src/Game/Game.Buildings/Efficiency.cs`, `src/Game/Game.Buildings/EfficiencyFactor.cs`.

```
GetEfficiency(buffer)   = product of max(0, value) over the buffer's entries
                          = 0                       when the product is <= 0
                          = max(0.01, round(100 * product) / 100)   otherwise
SetEfficiencyFactor     = writes the entry, or REMOVES it when |value - 1| <= 0.001
GetEfficiencyFactor     = returns 1 for a missing entry            // the correct read
GetImmediateEfficiency  = the same product over ONLY Destroyed, Abandoned, Disabled and ServiceBudget — the dispatchers' fast read; dispatch.md has the fleet consequence
ApproximateEfficiencyFactors = the inverse: splits one target efficiency across two weighted factors in closed form, or four by 16-step bisection
```

Three consequences a reader gets wrong from the field names alone:

**A factor at exactly 1 is not in the buffer.**
A query for a building's `ServiceBudget` entry returns nothing at 100% budget, because `SetEfficiencyFactor` deleted it.

**Efficiency is not capped at 1.**
`EmployeeHappiness` is `currentWorkforce / employedAverage + workConditions * 0.01`, unbounded above — a well-staffed, happy service building runs above 100% and its vehicle count rounds up accordingly ([dispatch.md](dispatch.md) has the fleet formula).
Source: `src/Game/Game.Simulation/WorkProviderSystem.cs`.

**The floor is 0.01 unless some factor is exactly zero, which zeroes the whole product.**
`Destroyed`, `Abandoned` and `Disabled` are written as literal `0f` by `BuildingStateEfficiencySystem`, and `Fire` by `FireSimulationSystem` while a fire burns.
Source: `src/Game/Game.Buildings/BuildingStateEfficiencySystem.cs`, `src/Game/Game.Simulation/FireSimulationSystem.cs`.

## Workforce and the education mix

A service building's workplaces come from `Game.Prefabs.WorkplaceData { m_Complexity, m_MaxWorkers, m_EveningShiftProbability, m_NightShiftProbability, m_MinimumWorkersLimit, m_WorkConditions }`, with `WorkplaceComplexity` = `Manual, Simple, Complex, Hitech`.
`CityServiceWorkplaceInitializeSystem` adds or removes `WorkProvider`, keeps `m_MaxWorkers` in step, arms the grace period at `-m_ServiceBuildingEfficiencyGracePeriod` when it adds the component, and subtracts another grace period on every later workplace change.
Source: `src/Game/Game.Prefabs/WorkplaceData.cs`, `src/Game/Game.Buildings/CityServiceWorkplaceInitializeSystem.cs`.

**The education requirement is a triangular kernel, entirely C#.**
Source: `src/Game/Game.Economy/EconomyUtils.cs`.

```
CalculateNumberOfWorkplaces(totalWorkers, complexity, buildingLevel):
  centre = 4 * (int)complexity + buildingLevel - 1
  for education level i in 0..4:
    weight  = max(0, 8 - |centre - 4i|)
    weight += max(0, 8 - |centre + 4|)   at i == 0    // the ends absorb the tails
    weight += max(0, 8 - |centre - 20|)  at i == 4
    workplaces[i] = totalWorkers * weight / 16        // rounding remainder carried forward, capped by what is left

GetWorkerWorkforce(happiness, level) = ((level == 0 ? 2 : 1) + 2.5 * level) * (0.75 + happiness / 200)
```

Complexity slides a width-16 triangle across the five education levels in steps of four.
**For a city service building the level is always 1**: the callers resolve it through `PropertyUtils.GetBuildingLevel`, which returns the literal 1 for a prefab with no `SpawnableBuildingData` — so complexity alone decides a service building's education mix.
Source: `src/Game/Game.Buildings/PropertyUtils.cs`, `src/Game/Game.Simulation/WorkProviderSystem.cs`.

**Staffing feeds efficiency through three factors at once, and a vacancy costs nothing immediately.**
Source: `src/Game/Game.Simulation/WorkProviderSystem.cs`, `src/Game/Game.Economy/EconomyUtils.cs`.

```
average  = Σ workplaces[i] * GetWorkerWorkforce(50, i)              // the ideal staffing
when average <= 0: the cooldown zeroes and all three factors are written as 1
CalculateCurrentWorkforce -> currentWorkforce, employedAverage, sickWorkforce
missing  = average - employedAverage - sickWorkforce
UpdateCooldown runs on the RAW missing figure, climbing 1 per update while short, before the ramp below reads it; full staffing zeroes only a POSITIVE cooldown, so the grace period's negative balance survives
missing *= saturate(m_EfficiencyCooldown / m_MissingEmployeesEfficiencyDelay)  // the ramp
missing *= m_MissingEmployeesEfficiencyPenalty
sick    *= m_SickEmployeesEfficiencyPenalty
(NotEnoughEmployees, SickEmployees) = ApproximateEfficiencyFactors((average - missing - sick) / average, (missing, sick))
EmployeeHappiness = (employedAverage > 0 ? currentWorkforce / employedAverage : 1) + workConditions * 0.01
```

**The two hiring notifications use their own thresholds, and the educated one truncates before it compares.**
The uneducated test is `(float)freeSlots / (float)slots >= m_UneducatedNotificationLimit` — a float ratio; the educated test one screen later is `(float)(freeWeighted / weighted) >= m_EducatedNotificationLimit` with both operands `int` (weighted as educated + 2×wellEducated + 2×highlyEducated), so the quotient is 0 for any partial vacancy and 1 only when every weighted educated slot is free — the limit acts as an all-or-nothing gate, never as the fraction its name and its uneducated twin suggest.
Source: `src/Game/Game.Simulation/WorkProviderSystem.cs`.

## Cadence

`CityServiceUpkeepSystem` consumes upkeep resources on a 256-frame interval (`kUpdatesPerDay = 64`, sixteen `UpdateFrame` groups); the money lines are accumulated by `CityServiceBudgetSystem` at `ModificationEnd` and applied to the treasury by `BudgetApplySystem` on its own interval; [`simulation-time-and-units`](../simulation-time-and-units/simulation-time-and-units.md) owns the `262144 / (kUpdatesPerDay * 16)` idiom every figure here comes from.
Source: `src/Game/Game.Simulation/CityServiceUpkeepSystem.cs`, `src/Game/Game.Simulation/CityServiceBudgetSystem.cs`, `src/Game/Game.Simulation/BudgetApplySystem.cs`.

(VOLATILE: every component, field, system, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Buildings`, `Game.Prefabs`, `Game.Economy` and `Game.UI.InGame`, at the files each listing and trap cites; plus the frontend slider bounds, against the `budget-slider-item.tsx` module in the game's UI bundle; plus the live-read adjustability census, against the running game's `ServiceData` prefabs by the query stated beside it; plus the live-read curve shape, against `m_ServiceBudgetEfficiencyFactor` on the live `BuildingEfficiencyParameterData` singleton.)
