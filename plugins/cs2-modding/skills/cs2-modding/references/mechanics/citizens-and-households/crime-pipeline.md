# The crime pipeline

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

A citizen becomes a criminal through the event system, never directly: a probability system creates a crime event entity, the event initializer resolves it to citizens, and a merge system writes the `Criminal` component (`m_Event, m_JailTime, m_Flags`; flags `Robber, Prisoner, Planning, Preparing, Monitored, Arrested, Sentenced` in `src/Game/Game.Citizens/CriminalFlags.cs`).

## The pipeline

Sources: `src/Game/Game.Simulation/CrimeCheckSystem.cs`, `src/Game/Game.Events/InitializeSystem.cs`, `src/Game/Game.Events/AddCriminalSystem.cs`, `src/Game/Game.Simulation/CriminalSystem.cs`.

```
CrimeCheckSystem (kUpdatesPerDay = 1, UpdateFrame tested inside the job):
  query: Citizen + UpdateFrame, None: HealthProblem, Worker, Student, Deleted, Temp
  the code also skips Child and Elderly, and anyone whose Criminal.m_Event != Entity.Null
  t = wellbeing <= 25 ? wellbeing / 25 : ((100 - wellbeing) / 75)^2
  for each unlocked crime event prefab whose random target type is Citizen:
    p = lerp over CrimeData.m_OccurenceProbability by t (m_RecurrenceProbability instead for a citizen already carrying Criminal)
    the CrimeProbability city modifier applies
    ceiling = max(population / PoliceConfigurationData.m_CrimePopulationReduction * 100, 100)
    crime event created if random.NextFloat(ceiling) < p
    a repeat offender is further suppressed by home welfare coverage * PoliceConfigurationData.m_WelfareCrimeRecurrenceFactor, against the same ceiling

Game.Events InitializeSystem.InitializeCrimeEvent:
  resolves the event's targets to citizens and emits
  AddCriminal { m_Event, m_Target, CriminalFlags.Planning, + Robber for CrimeType.Robbery }

AddCriminalSystem (SystemUpdatePhase.Modification4):
  merges AddCriminal into the target's Criminal component

CriminalSystem (interval 16, UpdateFrame tested inside the job) runs the state machine:
  Planning -> Preparing -> (crime committed) -> Arrested -> Sentenced -> Prisoner -> released
  sentencing: rand(100) < CrimeData.m_PrisonProbability, duration from m_PrisonTimeRange, then the PrisonTime city modifier
  m_JailTime is stored as duration * 262144 / 256 and decremented per pass
```

Because the roll's ceiling grows with population, crime per citizen falls as the city grows unless `m_CrimePopulationReduction` is tuned with it — and the welfare shield thins the same way.
The per-crime balance is `CrimeData` on each crime event prefab, behind an enableable `Locked`: occurrence and recurrence probabilities, alarm delay, duration, absolute and relative incomes, and jail and prison time ranges, all as min/max ranges, plus a plain prison probability (`src/Game/Game.Prefabs/CrimeData.cs`).

**`Criminal` outlives its episode.**
An escape clears only `Monitored`, so the robber keeps `Criminal { m_Event = Null, m_Flags = Robber }`, and the game's criminal count and occupation label read that residue.
Read `m_Flags` for the stage — a sentenced or imprisoned citizen also has `m_Event == Entity.Null`.
Source: `src/Game/Game.Simulation/CriminalSystem.cs`, `src/Game/Game.Simulation/CrimeCheckSystem.cs`.

**Only unemployed, unenrolled, healthy adults and teens are ever rolled for a new crime.**
The query excludes `HealthProblem`, `Worker` and `Student`, so an employed or enrolled citizen cannot start an episode.
Source: `src/Game/Game.Simulation/CrimeCheckSystem.cs`.

**Criminality is not an archetype.**
The citizen keeps its household, its education and whatever job it held before the episode; only the component and its flags move.
Source: `src/Game/Game.Simulation/CriminalSystem.cs`, `src/Game/Game.Events/AddCriminalSystem.cs`.

## Traps

**A prisoner's happiness comes from the prison building instance, added on top of a school's.**
`CitizenHappinessSystem` reads `Game.Buildings.Prison.m_PrisonerHealth/m_PrisonerWellbeing` on the building — not `PrisonData` on its prefab — and the prison and school branches accumulate into the same variable, because nothing stops an imprisoned citizen from staying enrolled.
Source: `src/Game/Game.Simulation/CitizenHappinessSystem.cs`, `src/Game/Game.Buildings/Prison.cs`.

While the flags hold `Prisoner`, `Arrested` or `Sentenced`, `CitizenBehaviorSystem` skips the citizen's idle loop entirely (`src/Game/Game.Simulation/CitizenBehaviorSystem.cs`).

(VOLATILE: every component, flag, system, field and constant this file names — their declarations in `Game.Citizens`, `Game.Events`, `Game.Buildings`, `Game.Simulation` and `Game.Prefabs` under `src/Game/`, at the files the sections cite.)
