# The education pipeline

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`CitizenEducationLevel` declares the five levels, `Uneducated` through `HighlyEducated` = 0–4 (`src/Game/Game.Citizens/CitizenEducationLevel.cs`).
`SchoolLevel` declares `Elementary = 1, HighSchool, College, University, Outside` (`src/Game/Game.Prefabs/SchoolLevel.cs`).

**A school's level is the education level it grants.**
`SchoolLevel.Elementary` yields education level 1, which is why a city of elementary graduates masses at `PoorlyEducated`.
Source: `src/Game/Game.Prefabs/SchoolLevel.cs`, `src/Game/Game.Simulation/GraduationSystem.cs`.

## The four-system loop

Sources: `src/Game/Game.Simulation/ApplyToSchoolSystem.cs`, `src/Game/Game.Simulation/FindSchoolSystem.cs`, `src/Game/Game.Simulation/StudentSystem.cs`, `src/Game/Game.Simulation/GraduationSystem.cs`.

```
ApplyToSchoolSystem (interval 512, UpdateFrame bucket):
  skip Elderly; skip while SchoolSeekerCooldown fresher than kCoolDown = 20000 frames
  Child: skip under kElementaryMinAgeInDays = 10 days; target Elementary
  else:  target = GetEducationLevel() + 1
         0 failed attempts and age > Teen and target == College -> target University
         // the age term is load-bearing: a teen keeps College
  admission: age == Child or (age == Teen  and HighSchool <= target < University) or (age == Adult and target >= HighSchool)
  requires CitizenUtils.HasMovedIn(household)
  roll GetEnteringProbability (below):
    pass, and the household rents, is no tourist and is not moving away:
      seeker entity { SchoolSeeker.m_Level, Owner -> citizen, CurrentBuilding = home } + HasSchoolSeeker on the citizen
    fail above HighSchool: failed-education count + 1 (capped at 3), SchoolSeekerCooldown

FindSchoolSystem (interval 16, no bucket):
  pathfinds the seeker with CitizenUtils.GetPathfindWeights
  hit, and the school is under SchoolData.m_StudentCapacity (upgrades combined by the caller):
    Student { m_School, m_Level, m_LastCommuteTime } on the citizen
    an entry in the school's Game.Buildings.Student buffer
    failed-education count reset, cooldown removed
  miss: SchoolSeekerCooldown

StudentSystem (interval 16, no bucket):
  attendance, per citizen per day:
    rand(100) <= min(40, round(100 / max(1, sqrt(m_TrafficReduction * population))))
  school day is [m_WorkDayStart - commute, m_WorkDayEnd], shifted per citizen by the WorkOffset seed in +-10922/262144 of a day
  queues Purpose.GoingToSchool, ends Purpose.Studying when the day closes

GraduationSystem (interval 16384, UpdateFrame bucket):
  skips half its candidates outright: random.NextInt(2) != 0
  Student.m_Level == 255 reads the school's own SchoolData.m_EducationLevel instead
  p = GetGraduationProbability (below)
  pass: SetEducationLevel(max(current, level)); leaves school above Elementary
  fail at level > 2:
    under three failed attempts: count + 1, then a dropout roll amplified as 1 - (1-p)^32
    at three: expelled outright, CitizenFailedSchool instead of CitizenDroppedOutSchool
```

**A teen or adult still at education level 0 can never enrol in anything.**
Their target resolves to Elementary, the admission test makes Elementary child-only, and a refusal below College does not even raise the failed count.
Source: `src/Game/Game.Simulation/ApplyToSchoolSystem.cs`.

**Enrolling costs an employed citizen their job.**
`FindSchoolSystem` removes `Worker` and the workplace's `Employee` entry in the same block that adds `Student`.
Source: `src/Game/Game.Simulation/FindSchoolSystem.cs`.

**`SchoolData`'s wellbeing and health figures never reach a student.**
Happiness reads the instance component `Game.Buildings.School`, which `SchoolAISystem` writes as `clamp(round(efficiency * combined), -100, 100)` — and `ICombineData` does not sum upgrades for you, each consumer walks `InstalledUpgrade` itself.
Source: `src/Game/Game.Simulation/CitizenHappinessSystem.cs`, `src/Game/Game.Simulation/SchoolAISystem.cs`, `src/Game/Game.Prefabs/SchoolData.cs`.

**A larger city sends a smaller fraction of its students out on any given day.**
The attendance roll shrinks with population — a deliberate congestion-reduction lever, not an emergent effect, and workers roll the same check.
Source: `src/Game/Game.Simulation/StudentSystem.cs`, `src/Game/Game.Simulation/WorkerSystem.cs`.

## The probabilities

```
GetEnteringProbability (ApplyToSchoolSystem.cs):
  Elementary:  1 for a Child, 0 for anyone else
  HighSchool:  m_AdultEnterHighSchoolProbability for an adult or a worker, m_EnterHighSchoolProbability otherwise
  n = wellbeing / 60 * (0.5 + studyWillingness)      // StudyWillingness pseudo-random seed
  College:     0.5 * (worker ? m_WorkerContinueEducationProbability : 1) * log(1.6n + 1)
  University:  0.3 * (worker ? m_WorkerContinueEducationProbability : 1) * n, then the UniversityInterest city modifier
  // the m_* fields live on EducationParameterData, a singleton

GetGraduationProbability (GraduationSystem.cs):
  0 when building efficiency <= 0.001
  n = saturate((0.5 + studyWillingness) * wellbeing / 75)
  level 1: smoothstep(0, 1, 0.6n + 0.41)
  level 2: 0.3 * log(2.6n + 1.1)
  level 3: 90 * log(1.6n + 1), then the CollegeGraduation modifier, / 100
  level 4: 70n, then the UniversityGraduation modifier, / 100
  then p = 1 - (1-p) / efficiency, + SchoolData.m_GraduationModifier

GetDropoutProbability (GraduationSystem.cs):
  compares lifetime earnings at the current wage against expected earnings after graduating, net of the school fee and the unemployment benefit forgone
  returns 1.0 (certain dropout) where studying does not pay
```

## Fees

`ServiceFeeSystem` charges the fee to the household, per student, from `Resource.Money` (`src/Game/Game.Simulation/ServiceFeeSystem.cs`).

```
PayFeeJob (ServiceFeeSystem.cs, interval 2048, no UpdateFrame => 128 passes per day, each pass over every student in every school's Game.Buildings.Student buffer):
  debit = round(GetFee(resource, city ServiceFee buffer) / 128)
  the ledger books the unrounded cost, so ledger and household disagree at small fees
  GetEducationResource: level 1 -> BasicEducation, 2 -> SecondaryEducation, 3 and 4 -> HigherEducation
```

**College and University charge the same fee.**
Source: `src/Game/Game.Simulation/ServiceFeeSystem.cs`.

**A fee lands quantized to multiples of 128 per day, and at 64 or below it rounds to zero.**
Each of the 128 daily passes debits `round(fee / 128)` — `math.round`, half-to-even — so the household pays `128 * round(fee / 128)`.
Source: `src/Game/Game.Simulation/ServiceFeeSystem.cs`.

The fee defaults, caps and adjustability are `FeeParameters { m_Default, m_Max, m_Adjustable }` per fee on `ServiceFeeParameterData` — check `m_Adjustable` before building against a fee, because not every fee is player-adjustable.
That singleton seeds the city entity's `ServiceFee` buffer at city creation (`src/Game/Game.Simulation/CitySystem.cs`); every charge afterwards reads the buffer, so retuning the singleton moves no charge — but it stays live as the relative-fee baseline the happiness factors divide by (`src/Game/Game.Simulation/CitizenHappinessSystem.cs`) and as the utility consumption curves (`src/Game/Game.Simulation/AdjustWaterConsumptionSystem.cs`, `AdjustElectricityConsumptionSystem.cs`).
The same `GetFee` read feeds the dropout calculation and the school's own average-graduation-time estimate (`GraduationSystem.cs`, `SchoolAISystem.cs`).

(VOLATILE: every component, system, field, probability expression and constant this file names — their declarations in `Game.Buildings`, `Game.Citizens`, `Game.Prefabs` and `Game.Simulation` under `src/Game/`, at the files the sections cite.)
