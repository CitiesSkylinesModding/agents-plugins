# Employment and wages

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Employment is a four-hop chain: the citizen grows a seeker, the seeker pathfinds to a slot, the hire writes two mirrored records, and the pay system reads one of them.

## The chain

Sources: `src/Game/Game.Simulation/CitizenFindJobSystem.cs`, `src/Game/Game.Simulation/FindJobSystem.cs`, `src/Game/Game.Companies/FreeWorkplaces.cs`, `src/Game/Game.Simulation/PayWageSystem.cs`.

```
CitizenFindJobSystem (kUpdatesPerDay = 256, UpdateFrame bucket):
  Child or Elderly: m_UnemploymentTimeCounter = 0, skip
  still inside the seek cooldown (HasJobSeeker.m_LastJobSeekFrameIndex
      + rand(kJobSeekCoolDownMin..Max)), or the household is moving away:
    m_UnemploymentTimeCounter += 1/256, skip -- these two sit ABOVE the branch split,
      so an employed citizen inside its cooldown ticks up instead of being zeroed
  unemployed branch: m_UnemploymentTimeCounter += 1/256, so also on the pass
      that creates the seeker
    proceed only when the free workspaces at levels 0..education pass a rand(100) gate
  employed branch (promotion; this branch's job is scheduled only when a float draw in
      [0, 1) exceeds m_SwitchJobRate, so a higher rate means fewer passes): m_UnemploymentTimeCounter = 0
    looks again only when the current level -- 0 for an outside-connection workplace,
    Worker.m_Level otherwise -- is below the education level AND the band between them
    holds more than 100 free workspaces (plus a rand(500) gate)
  seeker entity: JobSeeker { m_Level = citizen.GetEducationLevel(), m_Outside = Commuter bit },
    Owner -> citizen, CurrentBuilding = the home property
    (the temp home for a homeless household, the current building for a commuter)

FindJobSystem (interval 16, no bucket):
  pathfinds the seeker to a workplace
  FreeWorkplaces.GetBestFor(seeker.m_Level) walks DOWN from that level and returns
    the highest free slot at or below it, or -1
  hire: Worker { m_Workplace, m_Level = bestFor, m_LastCommuteTime, m_Shift } on the citizen,
        Employee { m_Worker, m_Level } appended to the workplace's buffer
        m_Shift drawn from WorkplaceData.m_EveningShiftProbability / m_NightShiftProbability

PayWageSystem (kUpdatesPerDay = 32, UpdateFrame bucket):
  walks every household's citizens and pays each 1/32 of a daily figure (table below)
  into the household's Resource.Money; a company workplace is debited the same amount
```

| Case                      | Daily figure                                                                                    |
| ------------------------- | ----------------------------------------------------------------------------------------------- |
| Employed                  | `EconomyParameterData.GetWage(worker.m_Level)`, times `m_CommuterWageMultiplier` for a commuter |
| Child                     | `m_FamilyAllowance`                                                                             |
| Elderly                   | `m_Pension`                                                                                     |
| Teen or adult, unemployed | `m_UnemploymentBenefit`, while `m_UnemploymentCounter < m_UnemploymentAllowanceMaxDays * 32`    |

Being paid a wage clears `m_UnemploymentCounter`; each unemployed pay tick increments it.
Taxable income is the amount minus `m_ResidentialMinimumEarnings / 32`, accumulated into `TaxPayer.m_UntaxedIncome` with a running average rate — commuters and outside-connection workers are excluded from that accumulation entirely, so they pay no residential tax.

## Traps

**Education is a ceiling, never a floor.**
`GetBestFor` walks down, so an educated citizen takes an uneducated slot if that is what is free — and `m_Level` on both records is the slot's level, so the wage follows the job, not the education.
Source: `src/Game/Game.Companies/FreeWorkplaces.cs`, `src/Game/Game.Simulation/PayWageSystem.cs`.

**A stale `m_Workplace` earns nothing, not even the unemployment fallback.**
Being paid requires the citizen to appear in the workplace's `Employee` buffer, not merely to hold a `Worker` pointing at it.
Source: `src/Game/Game.Simulation/PayWageSystem.cs`.

**There are two unemployment counters, and happiness reads the one that is not a search counter.**
`m_UnemploymentCounter` caps the benefit; `m_UnemploymentTimeCounter` feeds the wellbeing penalty, advances on every `CitizenFindJobSystem` pass over an unemployed citizen, and is cleared for workers on sight but for students only through a leisure-path roll they may keep failing.
Source: `src/Game/Game.Simulation/CitizenFindJobSystem.cs`, `src/Game/Game.Simulation/CitizenBehaviorSystem.cs`.

**An unreachable workplace un-employs the citizen.**
When a `GoingToWork` pathfind returns no destination, `TripNeededSystem` removes `Worker` unless a household car is free to try — the household owning none, or the citizen already keeping one, both un-employ; on success the same block writes `m_LastCommuteTime` from the path duration.
Source: `src/Game/Game.Simulation/TripNeededSystem.cs`.

**Losing the workplace also resets the failed-education count.**
`WorkerSystem` clears it when the workplace is gone, so unemployment re-opens school doors the failure cap had closed.
Source: `src/Game/Game.Simulation/WorkerSystem.cs`.

**The UI's household income projection is not the payment system.**
`EconomyUtils.GetHouseholdIncome` pays teens `m_FamilyAllowance` where `PayWageSystem` pays them the unemployment benefit, so panel and ledger disagree on such households.
Source: `src/Game/Game.Economy/EconomyUtils.cs`, `src/Game/Game.Simulation/PayWageSystem.cs`.

## Workplaces, workforce, the work day

Sources: `src/Game/Game.Economy/EconomyUtils.cs`, `src/Game/Game.Simulation/WorkerSystem.cs`.

```
CalculateNumberOfWorkplaces(totalWorkers, complexity, buildingLevel):
  centre = 4 * (int)complexity + buildingLevel - 1
           // WorkplaceComplexity: Manual, Simple, Complex, Hitech
  weight(level) = max(0, 8 - |centre - 4*level|) out of 16, tails folded into levels 0 and 4
  // complexity from WorkplaceData via PrefabRef; totalWorkers is the instance WorkProvider.m_MaxWorkers

GetWorkerWorkforce(happiness, level):
  ((level == 0 ? 2 : 1) + 2.5 * level) * (0.75 + happiness / 200)
  // at happiness 50 the ladder is 2, 3.5, 6, 8.5, 11: the level-0 special case lifts
  // the bottom rung, so the smallest gap on the ladder is the first one
  summed over the Employee buffer, then multiplied into company production:
  buildingEfficiency * sectorEfficiency * GetWorkforce(employees) * kCompanyUpdatesPerDay

work window (WorkerSystem.cs):
  [m_WorkDayStart, m_WorkDayEnd] rounded to the hour
  + a per-citizen WorkOffset in +-10922/262144 of a day (the WorkOffset seed)
  + 0.33 of a day for Workshift.Evening, 0.67 for Workshift.Night
  start extended back by 60 * m_LastCommuteTime,
    or by 40000 frames when that product comes to under 60 -- an unmeasured commute included
  off day: rand(100) > min(40, round(100 / max(1, sqrt(m_TrafficReduction * population))))
```

The 40000-frame commute fallback means a citizen with no measured commute leaves absurdly early on their first trip, then settles.

(VOLATILE: every component, system, field, formula and constant this file names — their declarations in `Game.Citizens`, `Game.Companies`, `Game.Economy`, `Game.Simulation` and `Game.Prefabs` under `src/Game/`, at the files the sections cite.)
