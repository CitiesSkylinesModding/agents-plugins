# Happiness: wellbeing and health

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

There is no stored happiness.
`Citizen.Happiness` is the read-only property `(m_WellBeing + m_Health) / 2` (`src/Game/Game.Citizens/Citizen.cs`), and `CitizenUtils.GetHappinessKey` buckets it for display: over 70 Happy, over 55 Content, over 40 Neutral, over 25 Sad, else Depressed (`src/Game/Game.Citizens/CitizenUtils.cs`).
`CitizenHappinessSystem` recomputes both bytes at interval 16 over sixteen update buckets, so one citizen is touched once per 256 simulation frames.
Its `HappinessFactor` enum declares the factor set — twenty-six members plus `Count` (`src/Game/Game.Simulation/CitizenHappinessSystem.cs`).
The factor magnitudes live on `CitizenHappinessParameterData`, a singleton; the per-factor display baselines and progression locks live in the `HappinessFactorParameterData` buffer on its own singleton entity.

## The computation

Sources: `src/Game/Game.Simulation/CitizenHappinessSystem.cs`.

```
UpdateHappinessJob, per citizen:
  skip the dead, and skip households not MovedIn -- unless the citizen is a Tourist
  each factor producer returns an int2 of (health delta, wellbeing delta)
  wellbeingTarget = max(0, 50 + the wellbeing deltas)
  healthTarget    = 50 + the health deltas, + 1 for a BicycleUser
  LocalEffectSystem position modifiers, then the home district's Wellbeing modifier
  then two INDEPENDENT random walks, one per byte:
    d = (rand(100) > 50 + m_WellBeing - wellbeingTarget) ? +1 : -1
    m_WellBeing = clamp(m_WellBeing + d, 0, 100)
    d = (rand(100) > 50 + m_Health - healthTarget) ? +1 : -1
    m_Health = clamp(m_Health + d, 0, GetMaxHealth(ageInYears))

GetMaxHealth(ageInYears):
  100 under 2 years, 90 under 3, 80 under 6, then 80 - 10 * floor(ageInYears - 5)
  ageInYears = ageInDays / TimeSettingsData.m_DaysPerYear
```

**A changed happiness input drifts the population toward the new value; nothing jumps.**
Each touch moves each byte by one point at most, and a citizen is touched once per 256 frames.
Source: `src/Game/Game.Simulation/CitizenHappinessSystem.cs`.

**The two sums are the wrong place to read which factor feeds which byte.**
Several terms in each sum are structurally zero because no producer ever assigns that half; the mapping has to come from each factor's producer function.
Source: `src/Game/Game.Simulation/CitizenHappinessSystem.cs`.

The producers hold surprises: air, ground and water pollution reach health alone, noise pollution reaches wellbeing alone, and sickness and the `BicycleUser` +1 sit outside the enum entirely.
The water fee's health half is computed, folded into the city average, and never added to any citizen's sum, so the panel reports a figure nobody received.
The sickness penalty accumulates into the Healthcare factor's aggregate, so the reported healthcare average mixes coverage and sickness.

**`GetConsumptionBonuses` is not the simulation's consumption rule.**
The citizen job inlines `min(15, household.m_ShoppedValueLastDay / 50)`; the exported helper is a different formula for building-level UI estimates.
Source: `src/Game/Game.Simulation/CitizenHappinessSystem.cs`.

**The education factor never counts the children.**
Its loop tests the scored citizen's own age instead of each member's, so `n` is the whole household size when the scored citizen is itself a child and zero for everyone else — the UI estimator counts the children per member, so panel and simulation disagree.
Source: `src/Game/Game.Simulation/CitizenHappinessSystem.cs`.

**A factor whose `m_LockedEntity` is still locked reports zero.**
That is how progression hides factors the player has not unlocked, and it reads as a broken factor to a mod that queries too early.
Source: `src/Game/Game.Prefabs/HappinessFactorParameterData.cs`, `src/Game/Game.Simulation/CitizenHappinessSystem.cs`.

## Producer formulas worth having

All in `src/Game/Game.Simulation/CitizenHappinessSystem.cs`; the `m_*` fields live on `CitizenHappinessParameterData` unless noted.

```
apartment    GetApartmentWellbeing(sizePerResident, level):
             0.8 * (4*(level-1) + 24.55531 - 70.21 / (1 + (sizePerResident/0.03690514)^25.2376)^0.01494523)
             a homeless citizen is scored as GetApartmentWellbeing(0.01, 1)
tax          GetTaxBonuses: (10 - residentialTaxRate), the TaxHappiness modifier, then * -multiplier, the multiplier per education level (m_TaxUneducatedMultiplier .. m_TaxHighlyEducatedMultiplier; the magnitude rises with education, scaling both the penalty and the bonus)
welfare      GetWellfareBonuses: coverage * m_WelfareMultiplier * max(0, (50 - happiness) / 50) -- helps only citizens below 50, fading to nothing at 50
unemployment GetUnemploymentBonuses: -min(m_MaxAccumulatedUnemployedWellbeingPenalty, m_UnemploymentTimeCounter * m_UnemployedWellbeingPenaltyAccumulatePerDay)
             zero for tourists
sickness     GetSicknessBonuses: m_SicknessPenalty latched at m_Health / 2 on the first sick tick, applied to health until the problem clears
death        GetDeathPenalty: any dead household member costs every member (-m_DeathHealthPenalty, -m_DeathWellbeingPenalty)
homeless     GetHomelessBonuses: a flat (m_HomelessHealthEffect, m_HomelessWellbeingEffect)
leisure      GetLeisureBonuses: (m_LeisureCounter - 128) / 16 wellbeing; a flat +7 for a Tourist
consumption  inline: min(15, household.m_ShoppedValueLastDay / 50) when positive
traffic      m_PenaltyCounter decays 1 per pass; while non-zero adds m_PenaltyEffect to wellbeing
education    GetEducationBonuses: sqrt(n) * m_EducationWellbeingMultiplier * (educationCoverage - m_NeutralEducation)
```

Two more producers read a building instance component rather than these fields: the student term (`Game.Buildings.School`, [education-pipeline.md](education-pipeline.md)) and the prisoner term (`Game.Buildings.Prison`, [crime-pipeline.md](crime-pipeline.md)).
The coverage inputs — healthcare, parks, education, welfare — arrive through `NetUtils.GetServiceCoverage` on the home building's road edge; telecom samples the telecom coverage cell map at the building's position instead, and garbage, crime and mail read producer components on the home building itself; [`city-services-and-coverage`](../city-services-and-coverage/city-services-and-coverage.md) owns what those numbers mean.

## What happiness feeds

Company production reads `citizen.Happiness` per employee through `GetWorkerWorkforce` — the term is in [employment-and-wages.md](employment-and-wages.md).
Crime probability lerps the crime prefab's occurrence range over a curve of `m_WellBeing` — [crime-pipeline.md](crime-pipeline.md).
Graduation and school entry both read `m_WellBeing` — [education-pipeline.md](education-pipeline.md).
And moving away rolls the household's mean happiness (`src/Game/Game.Simulation/HouseholdBehaviorSystem.cs`):

```
NotHappy roll (HouseholdBehaviorSystem.cs):
  rand(1000) < -53.35h + 5.408 * sqrt(95.96h^2 + 1013h + 6576) - 298.5
  h = the household's mean Happiness; stamps MovingAway { MoveAwayReason.NotHappy }
```

For the UI, per-factor city averages are aggregated separately and reported minus each factor's `HappinessFactorParameterData.m_BaseLevel`.

(VOLATILE: the `HappinessFactor` members and their order — the factor index is `(int)factor + 26 * updateFrame`, so an insertion reindexes the aggregation array — plus every producer function, field and constant this file names; all declared in `CitizenHappinessSystem.cs`, `CitizenHappinessParameterData.cs`, the building components in `Game.Buildings`, `NetUtils` in `Game.Net`, and the other files the sections cite.)
