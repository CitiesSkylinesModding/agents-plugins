# Travel: trips, purposes and pathfind weights

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`TripNeeded` is a buffer on the citizen — `{ m_TargetAgent, m_Purpose, m_Data, m_Resource, m_Priority }` — and a behaviour system appends one to request a trip (`src/Game/Game.Citizens/TripNeeded.cs`).
`TravelPurpose` is a component — `{ m_Purpose, m_Data, m_Resource }` — holding what the citizen is currently doing, and its absence means idle: the worker and student systems find available citizens by excluding it (`src/Game/Game.Citizens/TravelPurpose.cs`, `src/Game/Game.Simulation/WorkerSystem.cs`, `src/Game/Game.Simulation/StudentSystem.cs`).
The `Purpose` enum declares the full set of activities, ordinary life through services, crime, tourism, logistics and the failure states (`src/Game/Game.Citizens/Purpose.cs`).
It is serialized as a `byte`, so reordering its members changes what old saves mean (`save-serialization` owns the version gates).

## From request to path to arrival

Sources: `src/Game/Game.Simulation/TripNeededSystem.cs`, `src/Game/Game.Simulation/CitizenTravelPurposeSystem.cs`, `src/Game/Game.Citizens/CitizenUtils.cs`, `src/Game/Game.Common/SystemOrder.cs`.

```
TripNeededSystem (interval 16, no UpdateFrame partition) reads trips[0]:
  citizen already in the target building:
    enable Arrived, add TravelPurpose directly, clear the trips (a teleport)
  else:
    priority = TripPriorityParametersData.GetPriority(trips[0].m_Purpose, citizen)
    cost ceiling = m_BaseMaxCost * priority / 128       // TripPriority.kDefault = 128
    mode: bicycle at 20 % for an enabled BicycleOwner, shifted by the home district's
          BikeProbability modifier; a reserved car overrides
    build PathfindParameters + SetupQueueItem, enqueue to the pathfind queue;
    on the citizen-level purposes, add Target { Entity.Null } as the pending marker
  a GoingToWork path that returns no destination removes Worker unless a household
    car is free to try (see employment-and-wages.md)

CitizenTravelPurposeSystem (ordered before TripNeededSystem) promotes on arrival:
  GoingToWork -> Working, GoingToSchool -> Studying, Hospital -> InHospital,
  GoingToJail -> InJail, GoingToPrison -> InPrison, Deathcare -> InDeathcare,
  EmergencyShelter -> InEmergencyShelter
  purposes that end on arrival (GoingHome, Shopping, Leisure, ...) clear TravelPurpose
  a GoingHome arrival as ArriveType.Resident is the write that sets HouseholdFlags.MovedIn

CitizenUtils.GetPathfindWeights(citizen, household, householdCitizens):
  time      = 5 * (4 - 3.75 * m_LeisureCounter / 255)
  behaviour = 2
  money     = 2500 * max(1, householdSize) / max(250, household.m_ConsumptionPerDay)
              * 0.1 when the household is not MovedIn and the citizen is not
              MovingAwayReachOC / Tourist / Commuter
  comfort   = 1 + 2 * GetPseudoRandom(TrafficComfort).NextFloat()
```

`GetPathfindWeights` is the citizen's whole contribution to route choice — a mod changing how citizens weigh routes changes this one function, not each system.
The exception is `Purpose.EmergencyShelter`: the requesting systems overwrite the result with literal weights, so evacuation routing never reads the citizen's weighting (`src/Game/Game.Simulation/TripNeededSystem.cs`, `ResidentAISystem.cs`, `PersonalCarAISystem.cs`, `TaxiAISystem.cs`).
`PathUtils.IsPathfindingPurpose` names the purposes that take the citizen-level pathfind branch rather than the target-seeker one: `GoingHome, Hospital, Safety, EmergencyShelter, Crime, Escape, Sightseeing, VisitAttractions` (`src/Game/Game.Pathfind/PathUtils.cs`).

**A pending pathfind can be a null `Target`.**
On the citizen-level purposes `PathUtils.IsPathfindingPurpose` names, `TripNeededSystem` adds `Target { Entity.Null }` as its own in-flight marker, so reading `Target` as a destination dereferences null on those citizens mid-pathfind.
Source: `src/Game/Game.Simulation/TripNeededSystem.cs`.

## Priorities

`GetPriority` maps purposes onto the fields of `TripPriorityParametersData`, a singleton; the reader reads the magnitudes themselves (`src/Game/Game.Prefabs/TripPriorityParametersData.cs`):

| Purpose | Field |
| --- | --- |
| `GoingHome` | `m_PriorityGoingHome` |
| `GoingToWork` | `m_PriorityGoingToWork` |
| `GoingToSchool` | `m_PriorityGoingToSchool` |
| `Shopping`, `CompanyShopping` | `m_PriorityShopping` |
| `Hospital` / `Safety` / `Escape` / `EmergencyShelter` / `Deathcare` | `m_PriorityHospital` / `m_PrioritySafety` / `m_PriorityEscape` / `m_PriorityEmergencyShelter` / `m_PriorityDeathcare` |
| `MovingAway` | `m_PriorityMovingAway` |
| `Traveling`, `Relaxing`, `Sightseeing`, `VisitAttractions` | `m_PriorityTourist` |
| `Crime`, `GoingToJail`, `GoingToPrison` | `m_PriorityCriminal` |
| `Leisure` | `lerp(m_LeisurePriorityMin, m_LeisurePriorityMax, 1 - m_LeisureCounter / 255)` |
| anything else | 128 |

So a citizen with a full leisure counter paths for leisure at `m_LeisurePriorityMin`, and a save older than `FormatTags.TripPriority` reads back 128 for every stored trip (`src/Game/Game.Citizens/TripNeeded.cs`).
A priority buys pathfind cost budget through `GetMaxCost` — purposes are never ranked against each other.

## The idle loop

`CitizenBehaviorSystem` (interval 16, UpdateFrame bucket) is what a citizen does with no `TravelPurpose` (`src/Game/Game.Simulation/CitizenBehaviorSystem.cs`).
It checks in order: dead, imprisoned (skipped entirely), moving away (sent to an outside connection with `Purpose.MovingAway`, stripping `Worker`, `Student` and `Leisure`); then resolves the citizen's home — rented property, homeless temp home, tourist hotel, or an outside connection for a commuter — and continues: meeting, work or study hours, sleep, shopping, leisure.
Its constants: `kMinLeisurePossibility = 80`, `kLeisureSeekerCooldownFrames = 20000`, `kMaxPathfindCost = 17000`.
The leisure counter's decay is rolled inside the happiness job, at the chance fields on `LeisureParametersData` (`src/Game/Game.Simulation/CitizenHappinessSystem.cs`).

```
GetSleepTime (CitizenBehaviorSystem.cs):
  window starts at (0.875, 0.175) of a day
  + a per-citizen SleepOffset draw in [0, 0.2)
  - 0.05 for Elderly, - 0.1 for a Child, + 0.05 for a Teen
  then moved off the citizen's work or study hours
```

(VOLATILE: every component, system, purpose, field and constant this file names, the `Purpose` members and their order most of all — declared in `Game.Citizens`, `Game.Common`, `Game.Pathfind`, `Game.Prefabs` and `Game.Simulation` under `src/Game/`, at the files the sections cite.)
