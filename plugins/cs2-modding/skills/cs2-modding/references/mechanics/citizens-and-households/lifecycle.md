# Citizen lifecycle: aging, birth, death and household splits

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

## Life stages

`CitizenAge` declares `Child, Teen, Adult, Elderly` (`src/Game/Game.Citizens/CitizenAge.cs`).
The thresholds are C# static methods on `AgingSystem`, not prefab data:

| Becomes | At age (days) | Declared |
| --- | --- | --- |
| Teen | 21 | `AgingSystem.GetTeenAgeLimitInDays()` |
| Adult | 36 | `GetAdultAgeLimitInDays()` |
| Elderly | 84 | `GetElderAgeLimitInDays()` |

Converting any of them to game years divides by `TimeSettingsData.m_DaysPerYear` (`simulation-time-and-units` owns the conversion).

Sources: `src/Game/Game.Simulation/AgingSystem.cs`.

```
AgingJob (interval 16384, UpdateFrame bucket unless s_DebugAgeAllCitizens -- so each household is visited once per day):
  iterates households; for each member:
    age = TimeSystem.GetDay(frame, timeData) - citizen.m_BirthDay
  Child  at 21: leave school if enrolled, SetAge(Teen), enable BicycleOwner
  Teen   at 36: leave school if enrolled, SetAge(Adult), add LeaveHouseholdTag
  Adult  at 84: remove Worker, remove TravelPurpose if GoingToWork or Working, SetAge(Elderly)
```

**Elderly citizens cannot hold a job, and the ban is enforced in `AgingSystem`, not in the job systems.**
The Adult-to-Elderly transition strips `Worker`; `CitizenFindJobSystem` merely skips the elderly afterwards.
Source: `src/Game/Game.Simulation/AgingSystem.cs`, `src/Game/Game.Simulation/CitizenFindJobSystem.cs`.

`AgingSystem.s_DebugAgeAllCitizens` bypasses the bucket filter and ages the whole city in one pass — a switch a mod can flip to test an ageing change without waiting.

## Birth

Sources: `src/Game/Game.Simulation/BirthSystem.cs`.

```
BirthSystem (kUpdatesPerDay = 16, UpdateFrame bucket), per candidate:
  candidate: CitizenAge.Adult, not Male | Tourist | Commuter, household has a PropertyRenter        // so a homeless household has no births
  p  = CitizenParametersData.m_BaseBirthRate
  p += m_AdultFemaleBirthRateBonus   if the household holds an adult Male
  p *= m_StudentBirthRateAdjust      if the candidate is a Student
  birth if random < p / kUpdatesPerDay
```

The baby is created with `m_BirthDay = 0`, which fires `CitizenCoupleMadeBaby` or `CitizenSingleMadeBaby` depending on how many adults the household holds (`src/Game/Game.Citizens/CitizenInitializeSystem.cs`).

## Spawning: `m_BirthDay` is a recipe before it is a day

**`m_BirthDay` is a day number, not an age.**
Age is `TimeSystem.GetDay(frame, timeData) - m_BirthDay`, so it goes negative for citizens born before day zero.
Source: `src/Game/Game.Citizens/Citizen.cs`.

At creation, `CitizenInitializeSystem` first reads small `m_BirthDay` values as an enum, then overwrites the field with the real day number:

| `m_BirthDay` at creation | Result |
| --- | --- |
| 0 | Newborn child, birth triggers fire |
| 1 | Adult, random age in [36, 84), education drawn from levels 0–3 (0–4 for a commuter) |
| 2 | Child or teen, split on `DemandParameterData.m_TeenSpawnPercentage` |
| 3 | Elderly, age 84 + rand(5), education 0–4 |
| anything else | Adult, age 36 + rand(daysPerYear), education 2–3 |

`HouseholdInitializeSystem.SpawnCitizen` passes these literals: student count spawns with 4, adult count with 1, child count with 2, elder count with 3 (`src/Game/Game.Citizens/HouseholdInitializeSystem.cs`).
Initial health and wellbeing are `40 + rand(20)` each; the leisure counter starts at `128 + rand(92)` for a resident and `rand(128)` for a tourist; gender is a coin flip (`CitizenInitializeSystem.cs`).
Education is a weighted pick from `DemandParameterData.m_NewCitizenEducationParameters` over the band the age allows — the seam deciding the education mix of everyone who moves in, and prefab data rather than code.

## Death

Sources: `src/Game/Game.Simulation/DeathCheckSystem.cs`, `src/Game/Game.Simulation/HospitalAISystem.cs`.

```
DeathCheckSystem (kUpdatesPerDay = 16, UpdateFrame bucket), per citizen:
  skip while riding a vehicle (ResidentFlags.InVehicle) and skip the already Dead
  draw = citizen.GetPseudoRandom(CitizenPseudoRandom.Death).NextFloat()
  old age: die if draw < HealthcareParameterData.m_DeathRate.Evaluate((ageInDays + normalizedTimeOfDay - 0.5) / m_DaysPerYear / kMaxAgeInGameYear)   // kMaxAgeInGameYear = 9
           (m_LegacyDeathRate for saves predating the curve swap)
  else, if HealthProblemFlags.Sick | Injured:
    n = 10 - m_Health / 10
    die if rand(kUpdatesPerDay * 1000) <= n*n + 8     // the +8 is a floor no health escapes
    else roll recovery against a fail threshold: Logistic(3, 1000, 6, n/10 - 0.35), minus 10 * Game.Buildings.Hospital.m_TreatmentBonus while inside an active hospital, then the RecoveryFailChange modifier; recover when a float draw in [0, 1000) lands at or above the threshold
```

**A given citizen's old-age death draw never changes.**
The roll comes from the citizen's own `m_PseudoRandom` with the `Death` seed, so retuning the curve moves the threshold, never the draw.
Source: `src/Game/Game.Simulation/DeathCheckSystem.cs`, `src/Game/Game.Citizens/Citizen.cs`.

**Only a citizen the old-age draw spares reaches the sickness roll, and it requires `Sick | Injured` specifically.**
The sickness roll sits in the `else` of the old-age branch, and `Trapped` or `RequireTransport` alone never qualifies.
Source: `src/Game/Game.Simulation/DeathCheckSystem.cs`.

**`HospitalData`'s treatment bonus is not what the death check reads.**
`DeathCheckSystem` reads the building instance `Game.Buildings.Hospital.m_TreatmentBonus`, which `HospitalAISystem` rewrites from the prefab's `HospitalData.m_TreatmentBonus`, upgrades combined, scaled by efficiency and resource shortage — so a write to the live building is overwritten on the next pass.
Source: `src/Game/Game.Simulation/DeathCheckSystem.cs`, `src/Game/Game.Simulation/HospitalAISystem.cs`.

Death is `HealthProblemFlags.Dead` on the citizen's `HealthProblem`; `CitizenUtils.IsDead` is the check everything else uses, and the entity survives until a hearse collects it (`Purpose.Deathcare` / `InDeathcare`, `CitizenUtils.IsCorpsePickedByHearse`).
Source: `src/Game/Game.Citizens/HealthProblemFlags.cs`, `src/Game/Game.Citizens/CitizenUtils.cs`.

## Leaving home, partnering, divorce

`AgingSystem` stamps `LeaveHouseholdTag` on every new adult; `LeaveHouseholdSystem` makes the split conditional (`src/Game/Game.Simulation/LeaveHouseholdSystem.cs`).
The old household must hold more than `2 * kNewHouseholdStartMoney` (`kNewHouseholdStartMoney = 2000`), and the citizen must already carry `Worker`.
The new household entity is created from a household prefab flagged `m_DynamicHousehold` (`src/Game/Game.Prefabs/HouseholdPrefab.cs`), which also excludes such prefabs from random citizen spawning.
With more than ten free residential properties the new household becomes a `PropertySeeker`; otherwise it converts to a commuter household and the citizen gains `CitizenFlags.Commuter`.

**The household split waits on a job.**
The `Worker` requirement sits in the same guard as the money check, so the tag just stays until a job appears — though the divorce path below moves adults with no such test.
Source: `src/Game/Game.Simulation/LeaveHouseholdSystem.cs`.

**Splitting a household mints money.**
`AddResources` accumulates, so the old buffer ends at twice its money minus 2000 while the new household separately receives 2000.
Source: `src/Game/Game.Simulation/LeaveHouseholdSystem.cs`, `src/Game/Game.Economy/EconomyUtils.cs`.

`LookForPartnerSystem` marks a candidate `CitizenFlags.LookingForPartner` at `CitizenParametersData.m_LookForPartnerRate`, picking a `PartnerType` (`Same, Other, Any`) from `m_LookForPartnerTypeRate` and the citizen's own `PartnerType` pseudo-random seed.
The candidate must be adult or elderly and alive, the household moved in and neither tourist nor commuter, and the household must hold fewer than two adult-or-elderly members (`src/Game/Game.Simulation/LookForPartnerSystem.cs`).
Candidates queue in the city-level `LookingForPartner` buffer (`src/Game/Game.Citizens/LookingForPartner.cs`); `PartnerSystem` matches compatible entries, and `DivorceSystem` splits a household holding two or more adult-or-elderly members at `m_DivorceRate / kUpdatesPerDay` (`src/Game/Game.Simulation/DivorceSystem.cs`).

(VOLATILE: every component, flag, system, field and constant this file names, the spawn-recipe codes included — their declarations in `Game.Agents`, `Game.Buildings`, `Game.Citizens`, `Game.Creatures`, `Game.Economy`, `Game.Prefabs` and `Game.Simulation` under `src/Game/`, at the files the sections cite.)
