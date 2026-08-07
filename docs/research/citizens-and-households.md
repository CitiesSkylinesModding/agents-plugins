# Citizens and households

**Baseline.** Established against Cities: Skylines II **1.6.0f1**, Unity 2022.3.71f1, from a decompile regenerated 2026-06-24 (`src/Game/Properties/AssemblyInfo.cs`).
Mod corpus read 2026-08-06 at `C:\Users\Morgan\Documents\Projets\cs2-third-party-mods`.
Wiki fetched live 2026-08-06; the `Citizens` page returned content rather than the bot challenge, so no snapshot substitution was needed.
Live readings come from the user's running 1.6.0f1 development build on a loaded city of ~13,400 citizens, over the sibling Unity plugin, 2026-08-06; each such finding names the method that produced it, because the city moves on and the artifact is gone.

---

**Ruled (2026-08-06, ticket 22; conflicts.md).** Two entries were ruled together, and they govern **every finding below** rather than one of them, which is why the ruling sits here instead of at a finding.

**The decision: the reference states no prefab value.** It names the component and the field, and that is the whole of what it says about the magnitude. No wiki stat table is borrowed either — first-party or nothing, and for a prefab value "first-party" is a number the reader reads themselves.
Ruled first as "prefab-singleton" and widened the same day to prefab values generally: per-prefab components — a school's capacity, a prison's prisoner wellbeing, a crime's probability — rot at the same rate and a mod overwrites them as easily. [ADR 0004](../adr/0004-a-mechanics-reference-names-the-component-not-the-balance-value.md) is the durable record.

**So every prefab value this file read off a running game stops at this file**, whatever the note above it reads — `**Live**:`, `**Live values**`, `**Live defaults**`, `**Live distribution**`, `(live read)` and the rest are one kind of mark. They are why the ruling could be made, and they are evidence rather than copy. That covers the wage ladder, the benefits, the happiness factor magnitudes, the birth and divorce rates, the school-entry probabilities, the trip priorities, the fees, the leisure and consumption parameters, the tourist wealth band and the property-seeking parameters. A number this file computed from a C# formula at C# inputs — the work-efficiency ladder, the `GetMaxHealth` points — is not a prefab value and ships.

**What the reference owes instead**, and this is a substitution rather than a subtraction:

- **C# constants ship, as numbers**, because they are offline-checkable and citable to a line. The life-stage thresholds at `AgingSystem.cs:244-257` are static methods rather than prefab data, so 21 / 36 / 84 ship. So do `kNewHouseholdStartMoney`, `kMaxAgeInGameYear`, `kCoolDown`, `kMinimumShoppingMoney`, `kElementaryMinAgeInDays`, `TripPriority.kDefault` and the update-interval table.
- **Formulas ship whole.** `GetWorkerWorkforce`, the happiness sum with its baseline of 50, the ±1 random walk, `GetMaxHealth`'s step function, the apartment curve, the dropout calculation, the crime probability lerp, `GetPathfindWeights`. This is the invariant structure and none of it is balance.
- **The map ships, and it is the constructive half.** Which parameter component owns which family of numbers — `EconomyParameterData` for wages and benefits, `CitizenHappinessParameterData` for happiness magnitudes, `CitizenParametersData` for birth and divorce, `EducationParameterData` for school entry, `TripPriorityParametersData` for trip priorities, `ServiceFeeParameterData` for fees — is a mechanism table, since an agent cannot perform the read without it. Hand the reader the lookup in place of the number.
- **The read machinery is already shipped and is bridged to, not restated** — for a singleton. `ecs-in-this-game` carries `GetSingleton<T>`, which is the route to a parameter component. (`prefabs-and-assets`' `GetSingletonPrefab<T>(EntityQuery)` returns the prefab object and is a different call.)
  **For anything else the map carries the access shape beside the component**, because a reader cannot write the call from a field name: `HappinessFactorParameterData` is a buffer on its own singleton entity, and `CrimeData` is per crime-event prefab behind an enableable `Locked`.
  `ServiceFeeParameterData` is a singleton read, but its fee entries are **not what a household pays**: they seed the city entity's `ServiceFee` buffer at city creation (`CitySystem.cs:97-102`) and every charge afterwards reads that buffer (`ServiceFeeSystem.cs:126`), so the map needs both rows or a reader retunes the wrong one.
  `SchoolData`, `PrisonData` and `HospitalData` are the sharpest case — they are prefab components whose happiness and treatment figures the simulation never reads, taking the efficiency-scaled twins in `Game.Buildings` instead, and nothing sums a building's upgrades onto a prefab for you. See [a prefab value read where the simulation reads an instance](../solutions/prefab-data-read-where-the-simulation-reads-an-instance.md).

Two traps the ruling names explicitly:

- **A derived ratio is a magnitude, not a shape.** "A tax rise costs a highly educated citizen 12.5 times what it costs an uneducated one" and "going home is worth twice the path cost of going to work" are arithmetic over singleton values; they rot invisibly when either end moves. State the direction — the tax multiplier rises with education level, going home outranks going to work — and name the field.
- **A non-numeric prefab value is still a prefab value.** `FeeParameters.m_Adjustable` does not ship as "basic education is not player-adjustable". It ships as the field to check before building against a fee, with the reason.

**One clause the ruling did not have to state and this file needs anyway: an observation of one loaded city is not a fact about the game.** The census counts, the education distribution, the happiness aggregate, the criminal and tourist and commuter tallies below are all one save at one moment. Several of them established a **structural** finding, and it is the finding that ships while the counts stay here — that a component query and the census count different populations, that a `Criminal` flag outlives its episode, that a query on the age tags matches nothing. State the mechanism; do not state the tally.

---

## Findings

### A citizen is one entity, and its archetype is short

`CitizenPrefab.GetArchetypeComponents` (`src/Game/Game.Prefabs/CitizenPrefab.cs:21-33`) adds `Citizen`, `TripNeeded` (buffer), `CrimeVictim`, `MailSender`, `Arrived`, `CarKeeper`, `BicycleOwner`, `HasJobSeeker`, `UpdateFrame`.
**It is not the whole archetype.** Its first line is `base.GetArchetypeComponents(components)`, which reaches `PrefabBase.cs:372-375` and contributes `PrefabRef`; `ArchetypePrefab.RefreshArchetype` (`ArchetypePrefab.cs:26-39`) then unions in every active attached `ComponentBase` and adds `Created` and `Updated` before `CreateArchetype`. Same correction for `HouseholdPrefab` below. The live read at 17 components is the evidence — it lists `PrefabRef`, which the override does not.
The prefab itself carries `CitizenData`, a single `bool m_Male` used to pick a model matching the citizen's gender bit (`:15-19`, consumed at `src/Game/Game.Citizens/CitizenUtils.cs:232-264`).

Everything else a citizen has is added and removed at runtime by the behaviour systems: `HouseholdMember`, `CurrentBuilding`, `CurrentTransport`, `Worker`, `Student`, `TravelPurpose`, `Criminal`, `HealthProblem`, `Leisure`, `HasSchoolSeeker`, `SchoolSeekerCooldown`, the `Adult`/`Child`/`Teen`/`Elderly` tags.
**This is the single most important structural fact for a mod in this area**: presence of a component _is_ the citizen's state, so a query on `Worker` is the employed population and a query on `Student` is the enrolled one. There is no status field to switch on.

**Verified live.** `ecs_list_components` on a working citizen (entity 50743 in the loaded city) returned exactly the prefab archetype plus `HouseholdMember`, `CurrentBuilding`, `Worker`, `Criminal`, `SchoolSeekerCooldown`, `PrefabRef`, `RandomLocalizationIndex` and `Simulate` — 17 components, no surprises against the decompiled declaration.

Rots: the archetype list. Re-check `src/Game/Game.Prefabs/CitizenPrefab.cs:21-33`.

### `Citizen` is a bit-packed record, and the accessors are its only safe API

`src/Game/Game.Citizens/Citizen.cs:11-29` declares ten fields: `m_PseudoRandom` (ushort), `m_State` (`CitizenFlags`), `m_WellBeing`, `m_Health`, `m_LeisureCounter`, `m_PenaltyCounter` (all `byte`), `m_UnemploymentCounter` (int), `m_BirthDay` (short), `m_UnemploymentTimeCounter` (float), `m_SicknessPenalty` (int).

`m_State` is a 16-bit flag word (`src/Game/Game.Citizens/CitizenFlags.cs:8-23`) and **three multi-valued fields are packed into it alongside the plain booleans**:

| Meaning                       | Bits                                                                   | Accessor                                                              |
| ----------------------------- | ---------------------------------------------------------------------- | --------------------------------------------------------------------- |
| Age                           | `AgeBit1` (1), `AgeBit2` (2)                                           | `GetAge()` / `SetAge()` (`Citizen.cs:109-117`)                        |
| Education level 0–4           | `EducationBit1` (0x10), `EducationBit2` (0x20), `EducationBit3` (0x40) | `GetEducationLevel()` / `SetEducationLevel()` (`:47-82`)              |
| Failed education attempts 0–3 | `FailedEducationBit1` (0x80), `FailedEducationBit2` (0x100)            | `GetFailedEducationCount()` / `SetFailedEducationCount()` (`:84-107`) |

Age and the failed-education count are ordinary two-bit integers; **education is not**. `EducationBit3` alone means level 4, and levels 0–3 are `2*bit1 + bit2` (`:47-54`), so the three bits encode five values with three combinations unused. A mod that writes the bits directly rather than calling `SetEducationLevel` will produce levels the game never produces.

The remaining flags are plain booleans: `MovingAwayReachOC`, `Male`, `Tourist`, `Commuter`, `LookingForPartner`, `ValidCitizen`, `BicycleUser`, `Homeless`.

`Happiness` is a read-only property, `(m_WellBeing + m_Health) / 2` (`Citizen.cs:31`). There is no stored happiness.

`m_BirthDay` is a **day number, not an age**: age is `TimeSystem.GetDay(simulationFrame, timeData) - m_BirthDay` (`:33-36`), so it goes negative for citizens born before day zero. A live citizen read `m_BirthDay=-72`.

`GetPseudoRandom(CitizenPseudoRandom reason)` (`:38-45`) derives a deterministic `Random` from `m_PseudoRandom` and a magic constant per purpose (`src/Game/Game.Citizens/CitizenPseudoRandom.cs:5-13`: `WorkOffset`, `PartnerType`, `TrafficComfort`, `SleepOffset`, `StudyWillingness`, `Death`, `SpawnResident`, `BicycleModel`, `CarProbability`).
**This is the mechanism behind every "personality" the game gives a citizen** — the same citizen always sleeps at the same offset, is always the same amount willing to study, always values traffic comfort the same. A mod that overwrites `m_PseudoRandom` rerolls all nine at once.

Rots: `CitizenFlags` member values, `CitizenPseudoRandom` constants. Re-check both files.

### A household is the container, and it owns the money

`HouseholdPrefab.GetArchetypeComponents` (`src/Game/Game.Prefabs/HouseholdPrefab.cs:52-62`): `Household`, `HouseholdNeed`, `HouseholdCitizen` (buffer), `TaxPayer`, `Game.Economy.Resources` (buffer), `PropertySeeker`, `UpdateFrame`.

`Household` (`src/Game/Game.Citizens/Household.cs:8-22`) carries `m_Flags` (`HouseholdFlags`: `Tourist`, `Commuter`, `MovedIn` — `src/Game/Game.Citizens/HouseholdFlags.cs:8-12`), `m_Resources` (goods on hand, not money), `m_ConsumptionPerDay`, `m_ShoppedValuePerDay`, `m_ShoppedValueLastDay`, `m_LastDayFrameIndex`, `m_SalaryLastDay`, `m_MoneySpendOnBuildingLevelingLastDay`.

**Money is not on `Household`.** It is `Resource.Money` in the household's `Game.Economy.Resources` buffer (`src/Game/Game.Simulation/PayWageSystem.cs:154`, `src/Game/Game.Simulation/ServiceFeeSystem.cs:128`). `Household.m_Resources` is the household's stock of _shopped goods_, decremented daily by consumption (`src/Game/Game.Simulation/HouseholdBehaviorSystem.cs:255-257`).

The link runs both ways: the household holds a `HouseholdCitizen` buffer of its members (`src/Game/Game.Citizens/HouseholdCitizen.cs:7-10`, `[InternalBufferCapacity(5)]`), and each citizen holds `HouseholdMember.m_Household` back (`src/Game/Game.Citizens/HouseholdMember.cs:8`).
A household whose buffer empties deletes itself (`HouseholdBehaviorSystem.cs:151-155`).

Three household variants are separate components rather than flags on the struct, each carrying its own state: `TouristHousehold { m_Hotel, m_LeavingTime }` (`src/Game/Game.Citizens/TouristHousehold.cs:8-10`), `CommuterHousehold { m_OriginalFrom }` (`CommuterHousehold.cs:8`), `HomelessHousehold { m_TempHome }` (`HomelessHousehold.cs:8`). `HouseholdFlags.Tourist` and `.Commuter` duplicate the first two as bits; both are set at spawn (`src/Game/Game.Simulation/TouristSpawnSystem.cs:97-106`, `CommuterSpawnSystem.cs:78-90`).

**Verified live.** A resident household (entity 79124) carried exactly the prefab archetype plus `PropertyRenter`, `PathInformation`, `HouseholdAnimal` and `OwnedVehicle`; `Household { m_Flags=MovedIn, m_Resources=5695, m_ConsumptionPerDay=3584 }`. The `PathInformation` is the household's own — property seeking pathfinds from the household entity, not from a citizen.

### `HouseholdFlags.MovedIn` is set in exactly one place, and it gates almost everything

`src/Game/Game.Simulation/CitizenTravelPurposeSystem.cs:354-370`: when a citizen arrives at the building its household rents, as `ArriveType.Resident`, the flag goes on and a `CitizensMovedIn` statistic fires.
A grep of `src/Game/` for a write of `HouseholdFlags.MovedIn` returns that one site.

Everything downstream reads it. Happiness skips a citizen whose household is not moved in unless the citizen is a tourist (`src/Game/Game.Simulation/CitizenHappinessSystem.cs:294`). `CountHouseholdDataSystem` sets `CitizenFlags.ValidCitizen` only for moved-in households and clears it otherwise (`src/Game/Game.Simulation/CountHouseholdDataSystem.cs:315-343`), and the population counters read that flag (`:540`). School application requires it (`src/Game/Game.Simulation/ApplyToSchoolSystem.cs:127`). Pathfinding weights are cut to a tenth of their money weight for a not-yet-moved-in resident (`src/Game/Game.Citizens/CitizenUtils.cs:87`).

**So a mod that creates a household and expects it to count toward population must either drive a citizen through the arrival path or set the flag itself.** No other system will.

Rots: the single-writer property. Re-grep `HouseholdFlags.MovedIn` under `src/Game/`.

### Four life stages, and every transition is one system

`CitizenAge` is `Child, Teen, Adult, Elderly` (`src/Game/Game.Citizens/CitizenAge.cs:3-9`).
`AgingSystem` owns all three ageing transitions, in one job over households (`src/Game/Game.Simulation/AgingSystem.cs:66-139`), at `kUpdatesPerDay = 1` (`:173`, interval `262144 / (1 * 16)` at `:202-205`), registered at `SystemUpdatePhase.GameSimulation` (`src/Game/Game.Common/SystemOrder.cs:513`).

The thresholds are hard-coded static methods, not prefab data (`AgingSystem.cs:244-257`):

| Becomes | At age (days) | Verified live                              |
| ------- | ------------- | ------------------------------------------ |
| Teen    | 21            | `AgingSystem.GetTeenAgeLimitInDays()` → 21 |
| Adult   | 36            | `GetAdultAgeLimitInDays()` → 36            |
| Elderly | 84            | `GetElderAgeLimitInDays()` → 84            |

Those three day counts are C# and ship as numbers.
The **conversion to game years does not**: it divides by `TimeSettingsData.m_DaysPerYear`, a prefab-singleton value (read live at 12, which is why 1.75 / 3 / 7 game years is what a reader would have seen at 1.6.0f1). This is the derived-ratio trap the ruling names — the reference states the thresholds in days, names `m_DaysPerYear` as the divisor, and leaves the arithmetic to a reader who has read their own value.
Same shape one line down: `DeathCheckSystem.kMaxAgeInGameYear = 9` is C# and ships (`src/Game/Game.Simulation/DeathCheckSystem.cs:342`); "so the old-age death curve saturates at 108 days" is that constant times the singleton and does not.

Each transition does more than flip bits (`AgingSystem.cs:104-136`):

- **Child → Teen**: leaves school if enrolled, enables `BicycleOwner`.
- **Teen → Adult**: leaves school if enrolled, adds `LeaveHouseholdTag`.
- **Adult → Elderly**: removes `Worker`, and removes `TravelPurpose` if it was `GoingToWork` or `Working`. **Elderly citizens cannot hold a job**, and that is enforced here rather than in the job systems.

`AgingSystem.s_DebugAgeAllCitizens` (`:183`) bypasses the update-frame filter and ages the whole city in one pass — a debug switch a mod can flip to test an ageing change without waiting.

**The `Adult`/`Child`/`Teen`/`Elderly` tag components are a dead save-format representation, not live state.**
They exist as zero-size components (`src/Game/Game.Citizens/Adult.cs` and siblings) and nothing in `src/Game/` adds one. The single consumer is a load-time migration: `RequiredComponentSystem`'s `m_AgeGroupQuery` matches any `Citizen` carrying one of the four (`src/Game/Game.Serialization/RequiredComponentSystem.cs:604-614`), reads which tag is present, writes the equivalent through `Citizen.SetAge` and **removes the tag** (`:1447-1479`).
Verified live: an `ecs_query` on `Game.Citizens.Adult` in the loaded 1.6.0f1 city returned **0**, against 6,454 adults by the census. A mod must read `Citizen.GetAge()`; a query on the tags matches nothing.

### Birth: only adult non-male citizens in a rented property, and one bonus for a partner

`BirthSystem` (`src/Game/Game.Simulation/BirthSystem.cs:113-159`), `kUpdatesPerDay = 16` (`:221`).
The candidate must be `CitizenAge.Adult`, must not carry `Male`, `Tourist` or `Commuter` (`:117`), and the household must have a `PropertyRenter` (`:123-130`) — so a homeless household has no births.

Probability per update is `m_BaseBirthRate`, plus `m_AdultFemaleBirthRateBonus` when the household contains an adult male (`:133-146`), multiplied by `m_StudentBirthRateAdjust` when the mother is a student (`:147-150`), divided by `kUpdatesPerDay` (`:151`).

**Live values** from `CitizenParametersData` (`src/Game/Game.Prefabs/CitizenParametersData.cs:8-33`), read off the singleton in the running city: `m_BaseBirthRate = 0.02`, `m_AdultFemaleBirthRateBonus = 0.08`, `m_StudentBirthRateAdjust = 0.5`.

The baby is created with `m_BirthDay = 0`, which `CitizenInitializeSystem` reads as "newborn child" (`src/Game/Game.Citizens/CitizenInitializeSystem.cs:119-150`) and which also fires the `CitizenCoupleMadeBaby` or `CitizenSingleMadeBaby` trigger depending on how many adults the household holds.

### `m_BirthDay` doubles as a spawn recipe

`CitizenInitializeSystem` (registered at `SystemUpdatePhase.Modification5`, `SystemOrder.cs:230`) treats small `m_BirthDay` values as an enum rather than a day (`CitizenInitializeSystem.cs:119-183`):

| `m_BirthDay` at creation | Result                                                                                |
| ------------------------ | ------------------------------------------------------------------------------------- |
| 0                        | Newborn child, birth triggers fire                                                    |
| 1                        | Adult, random age in `[36, 84)`, education drawn from levels 0–3 (0–4 for a commuter) |
| 2                        | Child or teen, split on `DemandParameterData.m_TeenSpawnPercentage`                   |
| 3                        | Elderly, age `84 + rand(5)`, education 0–4                                            |
| anything else            | Adult, age `36 + rand(daysPerYear)`, education 2–3                                    |

`HouseholdInitializeSystem.SpawnCitizen` passes these literals (`src/Game/Game.Citizens/HouseholdInitializeSystem.cs:165-181`): student count spawns with 4, adult count with 1, child count with 2, elder count with 3.
The initializer then overwrites `m_BirthDay` with the real day number (`CitizenInitializeSystem.cs:210`).

Initial health and wellbeing are `40 + rand(20)` each (`:93-94`). The leisure counter starts at `128 + rand(92)` for a resident and `rand(128)` for a tourist (`:97-102`). Gender is a coin flip (`:103-106`).

Education for a spawned citizen is drawn from `DemandParameterData.m_NewCitizenEducationParameters`, a weighted pick over the level band the age allows (`:184-207`) — **this is the seam that decides the education mix of everyone who moves into the city**, and it is prefab data rather than code.

### Death: an age curve, plus a sickness roll

`DeathCheckSystem` (`src/Game/Game.Simulation/DeathCheckSystem.cs:180-235`), `kUpdatesPerDay = 16` (`:340`).
Old age: `citizen.GetPseudoRandom(CitizenPseudoRandom.Death).NextFloat()` is compared against `HealthcareParameterData.m_DeathRate` (or `m_LegacyDeathRate` for old saves) evaluated at `ageInDays / daysPerYear / 9` (`:196-204`).
**Because the roll comes from the citizen's own pseudo-random rather than a fresh one, a given citizen's death draw is fixed** — the curve moves, the draw does not.

The two rolls are **sequential, not independent**, and behind two guards the first draft missed. A citizen riding in a vehicle is skipped before either (`:184-191`, `ResidentFlags.InVehicle`). The sickness roll sits in the `else` of the old-age comparison (`:200-206`), so a citizen that passes old age is not rolled for sickness that pass, and it is further gated on `HealthProblemFlags.Sick | Injured` specifically — `Trapped` or `RequireTransport` alone never qualifies. Then (`:212-215`): `num4 = 10 - health/10`, and the citizen dies if `rand(kUpdatesPerDay * 1000) <= num4*num4 + 8` — the additive 8 over a base of 16000 is a floor a full-health sick citizen never escapes. The old-age curve is `m_DeathRate` or `m_LegacyDeathRate`, selected by a toggle at `:200`. Recovery is a logistic in the same quantity, reduced by a hospital's `m_TreatmentBonus` and the `RecoveryFailChange` city modifier (`:220-232`).

Death is expressed as `HealthProblemFlags.Dead` on the citizen's `HealthProblem` component (`src/Game/Game.Citizens/HealthProblemFlags.cs:8-15`), and `CitizenUtils.IsDead` is the check everything else uses (`src/Game/Game.Citizens/CitizenUtils.cs:31-52`). The entity survives until a hearse collects it — `IsCorpsePickedByHearse` tests for `Purpose.Deathcare`/`InDeathcare` (`:54-70`).

### Leaving home, partnering, divorcing

`AgingSystem` stamps `LeaveHouseholdTag` on a citizen becoming an adult (`AgingSystem.cs:121`). `LeaveHouseholdSystem` (`SystemOrder.cs:510`, `kUpdatesPerDay = 2` at `src/Game/Game.Simulation/LeaveHouseholdSystem.cs:193`) then makes the split conditional (`:73-140`):

- The old household must hold more than `2 * kNewHouseholdStartMoney` = ₡4,000 (`:92`, constant at `:195`), and **the citizen must already have a `Worker` component**. An unemployed new adult simply stays home.
- A new household entity is created from a `DynamicHousehold` prefab (`:96-102`; the prefab flag is `src/Game/Game.Prefabs/HouseholdPrefab.cs:37-49`, which also excludes such prefabs from random citizen spawning at `HouseholdInitializeSystem.cs:156-159`).
- **Nothing moves.** `:103` is `EconomyUtils.AddResources(Resource.Money, resources2 - kNewHouseholdStartMoney, resources)` on the OLD household's buffer, where `resources2` is that household's own money read at `:79-80` and `AddResources` accumulates rather than assigns (`EconomyUtils.cs:320-330`). The parents end at `2 * money - 2000`; the new household separately gets a fresh buffer with 2000 (`:104-105`). The guard at `:92` means `money > 4000`, so the delta is always positive. Splitting a household mints money — a game defect, since every other debit passes a negative amount.
- If the city has more than ten free residential properties the new household becomes a `PropertySeeker`; otherwise **it leaves the city and becomes a commuter household**, and the citizen gains `CitizenFlags.Commuter` (`:119-135`).

Partnering is three systems, all at `kUpdatesPerDay = 4` and all in `GameSimulation` (`SystemOrder.cs:507-509`). `LookForPartnerSystem` sets `CitizenFlags.LookingForPartner` at rate `m_LookForPartnerRate` and picks a `PartnerType` from `m_LookForPartnerTypeRate` (`src/Game/Game.Simulation/LookForPartnerSystem.cs:97-132`); the candidate must be adult or elderly and not already looking or dead (`:102`), the household must be moved in and neither tourist nor commuter (`:107`), and the household must hold **fewer than two adult-or-elderly members** (`:111-122`).
`PartnerSystem` matches two entries whose declared types are compatible (`src/Game/Game.Simulation/PartnerSystem.cs:115-137`); `DivorceSystem` splits a household of two at `m_DivorceRate / kUpdatesPerDay` (`src/Game/Game.Simulation/DivorceSystem.cs:131`).
`LookingForPartner` is a city-level buffer of `{ m_Citizen, m_PartnerType }` (`src/Game/Game.Citizens/LookingForPartner.cs`), and `PartnerType` is `None, Same, Other, Any` (`src/Game/Game.Citizens/PartnerType.cs`).
**Live values**: `m_DivorceRate = 0.16`, `m_LookForPartnerRate = 0.08`.

### Education: five levels, and the climb is four systems in a loop

`CitizenEducationLevel` is `Uneducated, PoorlyEducated, Educated, WellEducated, HighlyEducated` = 0–4 (`src/Game/Game.Citizens/CitizenEducationLevel.cs`).
`SchoolLevel` is `Elementary = 1, HighSchool, College, University, Outside` (`src/Game/Game.Prefabs/SchoolLevel.cs:3-10`) — **the school level is the education level the citizen will hold on graduating**, so `SchoolLevel.Elementary` yields education 1.

The cycle:

1. **`ApplyToSchoolSystem`** (`SystemOrder.cs:363`, interval 512) decides whether a citizen wants to enrol (`src/Game/Game.Simulation/ApplyToSchoolSystem.cs:99-166`). The target level is `GetEducationLevel() + 1`, except a child always targets Elementary and a citizen with zero failed attempts **and `age > CitizenAge.Teen`** skips College straight to University (`:121-123` — the age term is load-bearing and a teen keeps College). A child must be at least `kElementaryMinAgeInDays = 10` days old (`:110`, `:243`). Elderly citizens never apply (`:103`).
   **An age/level admission matrix then gates the application** (`:125`, enforced `:127`): `age == Child || (age == Teen && schoolLevel >= HighSchool && schoolLevel < University) || (age == Adult && schoolLevel >= HighSchool)`, plus `CitizenUtils.HasMovedIn`. So Elementary is child-only, and a teen or adult still at education level 0 targets it, is refused, and never enrols again. The seeker is created only when the household rents a property and is neither tourist nor moving away (`:135`); failing that, not even the failure count rises.
   `GetEnteringProbability` (`:333-369`): Elementary is 1.0 for a child and 0 for anyone else; High School is `m_EnterHighSchoolProbability` for a teen non-worker and `m_AdultEnterHighSchoolProbability` otherwise; College is `0.5 * (worker ? m_WorkerContinueEducationProbability : 1) * log(1.6n + 1)` and University `0.3 * (…) * n` with `n = wellbeing/60 * (0.5 + studyWillingness)`, University further scaled by the `UniversityInterest` city modifier.
   **Live values** from `EducationParameterData` (`src/Game/Game.Prefabs/EducationParameterData.cs:6-15`): `m_EnterHighSchoolProbability = 0.75`, `m_AdultEnterHighSchoolProbability = 0.1`, `m_WorkerContinueEducationProbability = 0.05`, `m_InoperableSchoolLeaveProbability = 0.1`.
   A refusal above High School increments the failed-education count and stamps `SchoolSeekerCooldown`, blocking reapplication for `kCoolDown = 20000` frames (`:157-165`, `:241`).
   Success creates a **separate seeker entity** carrying `SchoolSeeker { m_Level }`, `Owner` pointing back at the citizen and `CurrentBuilding`, and stamps `HasSchoolSeeker` on the citizen (`:138-155`).

2. **`FindSchoolSystem`** (`SystemOrder.cs:401`, interval 16) pathfinds the seeker to a school and, on a hit, adds `Student { m_School, m_LastCommuteTime, m_Level }` to the citizen, clears the failed-education count and removes the cooldown (`src/Game/Game.Simulation/FindSchoolSystem.cs:255-297`). Pathfind weights come from `CitizenUtils.GetPathfindWeights` (`:109`).

3. **`StudentSystem`** (`SystemOrder.cs:374`, interval 16) queues the daily `Purpose.GoingToSchool` trip and ends `Purpose.Studying` when the school day closes (`src/Game/Game.Simulation/StudentSystem.cs:77-134`, `:175-206`). Attendance is not daily: `IsTimeToStudy` first rolls `min(40, round(100 / sqrt(m_TrafficReduction * population)))` percent against a per-citizen-per-day seed (`:316-337`), so **a larger city sends a smaller fraction of its students out on any given day** — an explicit traffic-reduction lever, not an emergent one. The school day is `[m_WorkDayStart - commute, m_WorkDayEnd]` shifted by a per-citizen offset in ±10922/262144 of a day (`:311-314`, `:339-351`).

4. **`GraduationSystem`** (`SystemOrder.cs:514`, interval 16384) rolls graduation (`src/Game/Game.Simulation/GraduationSystem.cs:76-152`). It skips half its candidates outright with `random.NextInt(2) != 0` (`:89`).
   `GetGraduationProbability(level, wellbeing, graduationModifier, collegeModifier, uniModifier, studyWillingness, efficiency)` (`:295-329`) computes `n = saturate((0.5 + studyWillingness) * wellbeing / 75)` and then, per level: 1 → `smoothstep(0,1, 0.6n + 0.41)`; 2 → `0.3 * log(2.6n + 1.1)`; 3 → `90 * log(1.6n + 1) / 100` plus the `CollegeGraduation` city modifier; 4 → `70n / 100` plus `UniversityGraduation`. Then `p = 1 - (1-p)/efficiency` and `+ schoolData.m_GraduationModifier`. **Building efficiency at or below 0.001 returns 0** (`:297-300`).
   On success `SetEducationLevel(max(current, schoolLevel))` (`:126`) and the citizen leaves school above level 1. On failure above level 2 the branch splits at `:140`: under three failed attempts the count rises and `GetDropoutProbability` is rolled, amplified by `1 - (1-p)^32` (`:142-149`); **at three the citizen is expelled outright** by the `else` at `:152-156`, with no roll and a `CitizenFailedSchool` trigger rather than `CitizenDroppedOutSchool`.
   `GetDropoutProbability` (`:267-286`) is an economic calculation: it compares lifetime earnings at the current wage against expected earnings after graduating, net of the school fee and the unemployment benefit forgone, and returns 1.0 (certain dropout) when studying does not pay.

`SchoolData` carries the school side **as designed** (`src/Game/Game.Prefabs/SchoolData.cs:9-17`): `m_StudentCapacity`, `m_GraduationModifier`, `m_EducationLevel`, `m_StudentWellbeing`, `m_StudentHealth`.
Two corrections the first draft needed, both in [a prefab value read where the simulation reads an instance](../solutions/prefab-data-read-where-the-simulation-reads-an-instance.md). The last two fields are **not** what a student's happiness receives: `CitizenHappinessSystem.cs:175` declares `ComponentLookup<Game.Buildings.School>` and `:328-332` reads that instance component, which `SchoolAISystem.cs:145-146` writes as `clamp(round(efficiency * combined), -100, 100)`. And `ICombineData` does not make upgrades sum in — it declares `Combine` (`:19-26`) and each consumer walks `InstalledUpgrade` into a local itself (`GraduationSystem.cs:112-115`), with nothing writing a combined value back onto a prefab.

**Live distribution** across 11,511 moved-in citizens, from `CountHouseholdDataSystem.GetHouseholdCountData()`: uneducated 1,310, poorly educated 5,375, educated 3,298, well educated 1,277, highly educated 251; 4,385 students. The mass sits at level 1, which is where the Elementary→education-1 mapping puts everyone who finished school once.

### School fees are charged per student per tick, from the household's money

`ServiceFeeSystem` charges the fee to the **household**, not the citizen (`src/Game/Game.Simulation/ServiceFeeSystem.cs:108-137`): for each student in a school building's `Game.Buildings.Student` buffer, `PayFee(student, GetEducationResource(student.m_Level))` subtracts **`round(GetFee(resource, cityFees) / 128)`** from the household's `Resource.Money` and enqueues a `FeeEvent` (`:122-137` — `:126-128` reads the float and rounds before the debit). The interval is 2048 (`:350-353`) against `kUpdatesPerDay = 128` (`:330`), and `PayFeeJob` carries no `UpdateFrame`, so all 128 daily passes charge every student: a household pays `128 * round(GetFee / 128)` per student per day — the fee quantized to multiples of 128, and zero below 64. The city ledger books the unrounded `m_Cost` (`:133`, `:196`), so the two disagree at small fees.

`GetEducationResource` maps three fee buckets onto four school levels (`:464-478`): level 1 → `BasicEducation`, level 2 → `SecondaryEducation`, levels 3 **and 4** → `HigherEducation`. **College and University share one fee.**

**Live defaults** from `ServiceFeeParameterData` (`src/Game/Game.Prefabs/ServiceFeeParameterData.cs:16-20`, `FeeParameters` at `src/Game/Game.Prefabs/FeeParameters.cs:8-12`): basic 25 (max 100, `m_Adjustable = false`), secondary 50 (max 200), higher 100 (max 400).
**Basic education is not player-adjustable at 1.6.0f1** — that is a fact only the live read produced, since `m_Adjustable` is prefab data.

The same fee feeds the dropout calculation (`GraduationSystem.cs:143`) and the school's own average-graduation-time estimate (`src/Game/Game.Simulation/SchoolAISystem.cs:95`).

### Employment: education caps the job level, and nothing else does

The chain is four hops and each is one line:

1. `CitizenFindJobSystem` (`SystemOrder.cs:468`, `kUpdatesPerDay = 256` at `src/Game/Game.Simulation/CitizenFindJobSystem.cs:261`) creates a seeker entity carrying `JobSeeker { m_Level = citizen.GetEducationLevel(), m_Outside }` (`:182-186`). **The seeker's level is the citizen's education level, verbatim.**
2. `FindJobSystem` (`SystemOrder.cs:402`, interval 16) pathfinds it to a workplace and asks `FreeWorkplaces.GetBestFor(seeker.m_Level)` (`src/Game/Game.Simulation/FindJobSystem.cs:338-339`), which walks **down** from that level and returns the highest free slot at or below it, or -1 (`src/Game/Game.Companies/FreeWorkplaces.cs:49-59`).
3. `Worker { m_Workplace, m_LastCommuteTime, m_Level, m_Shift }` is added with `m_Level = bestFor` (`FindJobSystem.cs:360-364`), and an `Employee { m_Worker, m_Level }` entry is added to the workplace (`:352-354`, `src/Game/Game.Companies/Employee.cs:8-10`).
4. Wage is `EconomyParameterData.GetWage(worker.m_Level)` (`src/Game/Game.Simulation/PayWageSystem.cs:114`).

So **education is a ceiling, never a floor**: an educated citizen will take an uneducated job if that is what is free, and be paid the uneducated wage.
`CitizenFindJobSystem` also handles the promotion case: an employed citizen whose `Worker.m_Level` is below their education level looks again, gated on there being more than 100 free workspaces in the band (`:134-161`).

**How many slots of each level a workplace has** is `EconomyUtils.CalculateNumberOfWorkplaces(totalWorkers, complexity, buildingLevel)` (`src/Game/Game.Economy/EconomyUtils.cs:1370-1400`). It centres a triangular distribution at `4 * (int)complexity + buildingLevel - 1` and gives each level weight `max(0, 8 - |centre - 4*level|)` out of 16, with the tails folded into levels 0 and 4.
`WorkplaceComplexity` is `Manual, Simple, Complex, Hitech` (`src/Game/Game.Prefabs/WorkplaceComplexity.cs:3-8`), carried on `WorkplaceData` (`src/Game/Game.Prefabs/WorkplaceData.cs:9-19`) beside `m_MaxWorkers`, `m_MinimumWorkersLimit`, the evening and night shift probabilities, and `m_WorkConditions` (documented on the authoring component as "Offset to employee happiness", `src/Game/Game.Prefabs/Workplace.cs:28-29`).

**Work efficiency is a function of happiness and job level** (`EconomyUtils.cs:1447-1450`):

```
GetWorkerWorkforce(happiness, level) = ((level == 0 ? 2 : 1) + 2.5 * level) * (0.75 + happiness / 200)
```

Verified live: `(50, 0)` → 2, `(50, 4)` → 11, `(100, 4)` → 13.75. At happiness 50 the multiplier is exactly 1, so the ladder is **2, 3.5, 6, 8.5, 11** — the level-0 special case lifts the bottom rung from 1 to 2, which makes the gap from uneducated to poorly educated the smallest on the ladder and every rung above it worth 2.5.

That number is summed over a company's `Employee` buffer (`:1402-1414`) and multiplies directly into production: `buildingEfficiency * sectorEfficiency * GetWorkforce(employees, citizens) * kCompanyUpdatesPerDay` (`:1480`).
**This is the whole of "happiness feeds the economy"** — one term in one product, at `EconomyUtils.cs:1480`.

Shifts: `Worker.m_Shift` is a `Game.Companies.Workshift`, assigned from the workplace's evening and night probabilities, and shifts the work window by +0.33 and +0.67 of a day (`src/Game/Game.Simulation/WorkerSystem.cs:379-403`).
The work window itself is `[m_WorkDayStart, m_WorkDayEnd]` rounded to the hour, offset per citizen by `GetWorkOffset` (the same ±10922/262144 as study, from the same `WorkOffset` seed), and extended backward by the last commute time — with a **40000-frame fallback when no commute has been measured** (`:390-402`).
Attendance rolls the same population-scaled `IsTodayOffDay` check as school (`:350-359`), so a citizen with no measured commute leaves absurdly early on their first trip and then settles.

Losing the job is `WorkerSystem` finding the workplace gone (`:139-149`, `:224-235`): `Worker` is removed, `TravelPurpose` cleared, the failed-education count reset to zero, and a `CitizenBecameUnemployed` trigger fired.
`TripNeededSystem` also fires it when a work commute finds no path and the citizen has no car available (`src/Game/Game.Simulation/TripNeededSystem.cs:1250-1267`) — **an unreachable workplace un-employs the citizen**, and the same block writes `m_LastCommuteTime` from the path duration on success.

### The money attached to a citizen

`PayWageSystem` (`SystemOrder.cs:477`, `kUpdatesPerDay = 32` at `src/Game/Game.Simulation/PayWageSystem.cs:253`) walks every household's citizens and pays each one `1/32` of a daily figure into the household's money (`:95-163`).

| Case                      | Amount per day                                                                               | Source     |
| ------------------------- | -------------------------------------------------------------------------------------------- | ---------- |
| Employed                  | `GetWage(worker.m_Level)`, ×`m_CommuterWageMultiplier` for a commuter                        | `:114-119` |
| Employed at a company     | the same amount is _debited_ from the company                                                | `:120-127` |
| Child                     | `m_FamilyAllowance`                                                                          | `:138-140` |
| Elderly                   | `m_Pension`                                                                                  | `:141-143` |
| Adult or teen, unemployed | `m_UnemploymentBenefit`, while `m_UnemploymentCounter < m_UnemploymentAllowanceMaxDays * 32` | `:144-151` |

Taxable income is `amount - m_ResidentialMinimumEarnings / 32`, accumulated into `TaxPayer.m_UntaxedIncome` with a running average rate (`:155-162`).
**Commuters and outside-connection workers are excluded from that accumulation entirely** (`:158`) — they earn and pay no residential tax. The wiki says the same and it is verified here.

Being paid clears `m_UnemploymentCounter` (`:128-132`); being unemployed increments it once per pay tick (`:147`).
A **separate** counter, `m_UnemploymentTimeCounter`, is the one happiness reads — and it is not a failed-search counter, which the first draft had wrong twice. `CitizenFindJobSystem` advances it by `1/256` on every pass over an unemployed teen or adult, including passes where no search happens at all (`:99-103`, seek cooldown; `:105-109`, household already moving away) and including the pass on which a job is found, since `:114` advances before the workplace scan. The write census is `CitizenFindJobSystem.cs:95` (child or elderly), `:101`/`:107`/`:114` (the advances), `:136` (employed — unconditional), and `CitizenBehaviorSystem.cs:476` (the `Worker` branch) and `:482` (the `Student` one). The Student site is **not** an equivalent of being hired: `:482` sits inside `DoLeisure`, behind a `LeisureSeekerCooldown` frame check and a `m_LeisureRandomFactor` roll, so a student clears the penalty eventually rather than on enrolment — and one that never passes the roll keeps it, with the counter frozen because the unemployed query excludes `Student`.

`EconomyUtils.GetHouseholdIncome` (`src/Game/Game.Economy/EconomyUtils.cs:428-467`) computes the same figures as a projection for the UI and for the move-away decision, applying the residential tax rate above the minimum-earnings threshold (`:441-449`). It differs from `PayWageSystem` in one place worth knowing: it pays `m_FamilyAllowance` to **teens as well as children** (`:453-456`), where the payment system pays teens the unemployment benefit.

Verdict: the wiki's `Citizens` benefit table is partly stale at 1.6.0f1, and the split is clean.
The wiki (https://cs2.paradoxwikis.com/Citizens, fetched 2026-08-06, page banner "verified for 1.0", wage table headed 1.3.3f1) states wages ₡1500/1800/2100/2400/2700, Family Allowance ₡400, Pension ₡1200, Unemployment Benefit ₡800 capped at 10 days, Residential Minimum Earnings ₡1400, Commuter Wage Multiplier 1.1.
Reading the live `EconomyParameterData` singleton in the running 1.6.0f1 game gives **wages 1500/1800/2100/2400/2700 (match), minimum earnings 1400 (match), commuter multiplier 1.1 (match), unemployment max days 10 (match) — and family allowance 600, pension 1800, unemployment benefit 900 (all three stale on the wiki)**.
The first-party source wins as `SOURCES.md` requires; what makes the verdict worth recording is that it is not uniform, so "the wiki's numbers are stale" and "the wiki's numbers are fine" are both wrong about this table.
Rots: every number in the paragraph above. Re-read the `EconomyParameterData` singleton in a running game.

### Happiness: two bytes, twenty-six factors, one averaged output

`Citizen.Happiness` is `(m_WellBeing + m_Health) / 2` (`Citizen.cs:31`). `CitizenUtils.GetHappinessKey` buckets it for display: >70 Happy, >55 Content, >40 Neutral, >25 Sad, else Depressed (`src/Game/Game.Citizens/CitizenUtils.cs:160-179`, enum at `src/Game/Game.Citizens/CitizenHappiness.cs`).

`CitizenHappinessSystem` (`SystemOrder.cs:378`, interval 16 at `src/Game/Game.Simulation/CitizenHappinessSystem.cs:974-977`) recomputes both bytes for a sixteenth of the citizens per pass.
It declares 26 factors (`:34-63`): `Telecom, Crime, AirPollution, Apartment, Electricity, Healthcare, GroundPollution, NoisePollution, Water, WaterPollution, Sewage, Garbage, Entertainment, Education, Mail, Welfare, Leisure, Tax, Buildings, Consumption, TrafficPenalty, DeathPenalty, Homelessness, ElectricityFee, WaterFee, Unemployment`.

**Each factor returns an `int2` of (health delta, wellbeing delta)**, and the two totals are formed separately (`:530-531`):

```
wellbeing = max(0, 50 + trafficPenalty + deathPenalty + consumption + electricity + electricityFee
                 + water + waterFee + sewage + healthcare + leisure + building + waterPollution
                 + noise + garbage + crime + entertainment + mail + education + telecom
                 + apartment + welfare + tax + homelessness + unemployment)
health    = 50 + healthcare + sickness + deathPenalty + building + groundPollution + airPollution
                 + electricity + water + sewage + waterPollution + garbage + apartment + welfare
                 + homelessness + unemployment  (+1 if BicycleUser)
```

Both baselines are **50** — **but the sums are the wrong place to read the mapping from.** Four terms in the health sum (`int7.x` apartment, `int8.x` electricity, `int24.x` welfare, `int27.x` unemployment) are never assigned by any producer and are structurally zero, and `int16.y` (water pollution) is the mirror case in the wellbeing sum. The mapping has to come from each factor's producer, which was checked one by one:

Wellbeing only (sixteen): tax, leisure, education, entertainment, **noise pollution**, crime, mail, telecom, the traffic penalty, the consumption bonus, the electricity and water fees, **apartment**, **electricity supply**, **welfare** and **unemployment**.
Health only (three): **air pollution**, **ground pollution** and **water pollution**.
Both (seven): healthcare, water supply, sewage, garbage, the school-or-prison building bonus, the death penalty and homelessness.
Off the enum: sickness reaches health alone, and so does the `+1` for `BicycleUser`.
`WaterFee` is the odd one — its producer fills both halves (`:1123-1130`) and the caller never adds `int15.x` to the health sum, though it does fold it into the city average, so the panel reports a water-fee health figure no citizen received.
The sickness penalty is accumulated into the **Healthcare** factor's aggregate (`:517-519`, the same accumulator as `:411-413`), so the reported healthcare average is coverage and sickness mixed.

The result is applied as **two ±1 random walks, one per byte, each drawn separately against its own target** (`:543-547`) — the first draft recorded one shared delta, which would couple health to wellbeing's target:

```
d = (random.NextInt(100) > 50 + m_WellBeing - wellbeingTarget) ? +1 : -1
m_WellBeing = clamp(m_WellBeing + d, 0, 100)
d = (random.NextInt(100) > 50 + m_Health    - healthTarget)    ? +1 : -1
m_Health    = clamp(m_Health + d, 0, GetMaxHealth(ageInYears))
```

**So a mod that changes a happiness input sees the population drift toward the new value over many passes rather than jump.** With interval 16 and sixteen update buckets, one citizen is touched once per 256 simulation frames.

`GetMaxHealth(ageInYears)` is a step function (`:1491-1506`): 100 under 2 years, 90 under 3, 80 under 6, then `80 - 10 * floor(age - 5)`. Verified live: 80 at 5 years, 60 at 7, 40 at 9. **This is the "seniors slowly lose maximum health until they die" the wiki describes**, and it is age in _years_ against `TimeSettingsData.m_DaysPerYear`, not in days.

Selected factor formulas worth having (all `CitizenHappinessSystem.cs`):

- **Apartment** (`:1050-1053`): `0.8 * (4*(level-1) + (24.55531 - 70.21 / (1 + (sizePerResident/0.03690514)^25.2376)^0.01494523))`, where `sizePerResident = m_SpaceMultiplier * lotX * lotY / (householdSize * residentialProperties)` (`:477`). A homeless citizen is scored as `GetApartmentWellbeing(0.01, 1)` (`:487`).
- **Leisure** (`:1473-1480`): `(m_LeisureCounter - 128) / 16` for a resident; a flat **+7** for a tourist.
- **Tax** (`:1357-1383`): `(10 - residentialTaxRate) * -multiplier`, the multiplier per education level, with the `TaxHappiness` city modifier applied to the bracket before the multiply. **Live**: uneducated −0.4, poorly −1, educated −2, well −4, highly −5. So a tax rise costs a highly educated citizen 12.5 times what it costs an uneducated one, and the neutral rate is 10%.
- **Unemployment** (`:1345-1355`): `-min(m_MaxAccumulatedUnemployedWellbeingPenalty, m_UnemploymentTimeCounter * m_UnemployedWellbeingPenaltyAccumulatePerDay)`, zero for tourists. **Live**: accumulate 35/day, cap 70.
- **Welfare** (`:1385-1391`): `welfareCoverage * m_WelfareMultiplier * max(0, (50 - currentHappiness) / 50)` — **it only helps citizens below 50 happiness**, and scales to nothing at 50.
- **Sickness** (`:1406-1418`): on first tick with a health problem, `m_SicknessPenalty` is latched at `m_Health / 2` and applied as a health penalty until the problem clears.
- **Homelessness** (`:1420-1423`): flat `(m_HomelessHealthEffect, m_HomelessWellbeingEffect)`. **Live**: −10 and −10.
- **Death penalty** (`:1425-1441`): any dead member of the household costs every member `(-m_DeathHealthPenalty, -m_DeathWellbeingPenalty)`. **Live**: −10 health, −20 wellbeing.
- **Education** (`:1189-1199`): `sqrt(n) * m_EducationWellbeingMultiplier * (educationCoverage - m_NeutralEducation)`, and **`n` is not the number of children**. The loop at `:309-316` runs over `householdCitizens` but tests `citizen.GetAge()` — the loop-invariant citizen being scored (`:293`) — never dereferencing `householdCitizens[j]`. So `n` is the whole household's size when the scored citizen is itself a child, and 0 for every teen, adult and elderly citizen. A game defect: the UI estimator counts the children correctly at `:1665-1668`, so the panel and the simulation disagree. **Live**: multiplier 3, neutral 5.
- **Consumption** (`:317-318`): the citizen job does **not** use the exported `GetConsumptionBonuses` helper. It uses `min(15, household.m_ShoppedValueLastDay / 50)` when that value is positive and zero otherwise — a wellbeing bonus capped at +15 for a household that shopped yesterday.
  `GetConsumptionBonuses` (`:1466-1471`), `clamp(round(20*log(1 + 0.2n) + 12500/(2n + 190) - 112), -40, 40)` with `n = dailyConsumption / citizens`, is a **different formula for a different consumer**: it estimates a _building's_ happiness for the UI (`src/Game/Game.Objects/SubObjectSystem.cs:3185`, `src/Game/Game.UI.InGame/DeveloperInfoUISystem.cs:3690`). Do not read the exported helper as the simulation's rule.
- **Traffic penalty** (`:502`, `:526-529`): `m_PenaltyCounter` decays by 1 per pass and while non-zero costs `m_PenaltyEffect` wellbeing. **Live**: −25.

Two modifiers apply after the sums (`:535-540`): `LocalEffectSystem` position-based `Wellbeing`/`Health` modifiers, and the home district's `DistrictModifierType.Wellbeing`.

Per-factor city averages are aggregated separately for the UI: each chunk enqueues an `int4` of (sum, count, healthSum, wellbeingSum) per factor into a queue (`:558-713`), `HappinessFactorJob` folds them into a 26×16 array (`:733-772`), and `GetHappinessFactor` averages across the sixteen buckets and subtracts the factor's `HappinessFactorParameterData.m_BaseLevel` (`:990-1003`).
`HappinessFactorParameterData { m_BaseLevel, m_LockedEntity }` is a buffer on the parameter prefab (`src/Game/Game.Prefabs/HappinessFactorParameterData.cs:5-11`); **a factor whose `m_LockedEntity` is a locked (not-yet-unlocked) feature reports zero**, which is how progression hides factors the player has not reached.

**Live aggregate** across 11,511 moved-in citizens: total happiness 969,181 (mean 84.2), total wellbeing 1,104,704 (mean 96.0), total health 838,662 (mean 72.9). The gap is the point — in a well-run city wellbeing saturates near its 100 cap while health is held down by `GetMaxHealth`'s age ceiling, so **happiness in a mature city is mostly a demographic fact about the age pyramid rather than a service one**.

Rots: the `HappinessFactor` enum's 26 members and their order (the factor index is `(int)factor + 26 * updateFrame` at `:979-982`, so inserting a member reindexes the whole array), and every parameter name on `CitizenHappinessParameterData`.

### What happiness feeds

Four consumers, and they are the whole list this pass found:

1. **Work efficiency**, through `GetWorkerWorkforce(citizen.Happiness, employee.m_Level)` into company production (`EconomyUtils.cs:1410`, `:1480`).
2. **Crime probability**: `CrimeCheckSystem.TryAddCrime` derives `t` from wellbeing — below 25 it is `wellbeing/25`, above it is `((100 - wellbeing)/75)^2` — and lerps the crime prefab's `m_OccurenceProbability` (or `m_RecurrenceProbability` for a repeat offender) across it (`src/Game/Game.Simulation/CrimeCheckSystem.cs:130-158`).
3. **Moving away**: `HouseholdBehaviorSystem` rolls the household's mean happiness against a fitted curve, `rand(1000) < -53.35h + 5.408*sqrt(95.96h² + 1013h + 6576) - 298.5`, and stamps `MovingAway` with `MoveAwayReason.NotHappy` (`src/Game/Game.Simulation/HouseholdBehaviorSystem.cs:158-176`).
4. **Graduation and school entry**, through `m_WellBeing` in both probability functions (`GraduationSystem.cs:301`, `ApplyToSchoolSystem.cs:355`).

Plus two cosmetic ones: the citizen's selected sound (`CitizenUtils.cs:181-202`) and the welfare factor's own self-damping (above).

### Travel purpose is the state, and `TripNeeded` is the request

Two components, and the direction between them is the thing to hold:

- **`TripNeeded`** is a _buffer_ on the citizen: `{ m_TargetAgent, m_Purpose, m_Data, m_Resource, m_Priority }` (`src/Game/Game.Citizens/TripNeeded.cs:9-17`). A behaviour system **appends** one.
- **`TravelPurpose`** is a _component_: `{ m_Purpose, m_Data, m_Resource }` (`src/Game/Game.Citizens/TravelPurpose.cs:9-13`). It is what the citizen is currently doing, and its **absence means idle** — several queries exclude `TravelPurpose` to find citizens available for a new activity (`src/Game/Game.Simulation/StudentSystem.cs:361`, `src/Game/Game.Simulation/WorkerSystem.cs:415`).

`Purpose` is 41 members plus `Count` (`src/Game/Game.Citizens/Purpose.cs:5-45`), spanning ordinary life (`Shopping, Leisure, GoingHome, GoingToWork, Working, Sleeping, Studying, GoingToSchool`), services (`Hospital, InHospital, Deathcare, InDeathcare, Safety, EmergencyShelter, InEmergencyShelter, Escape`), crime (`Crime, GoingToJail, GoingToPrison, InJail, InPrison`), tourism (`Traveling, Relaxing, Sightseeing, VisitAttractions`), logistics reused for non-citizen agents (`Exporting, Delivery, UpkeepDelivery, StorageTransfer, Collect, ReturnUnsortedMail, ReturnLocalMail, ReturnOutgoingMail, ReturnGarbage, SendMail, CompanyShopping`), and the failure states `PathFailed`, `WaitingHome`, `Disappear`, `MovingAway`.

**The road from a `TripNeeded` to a path is `TripNeededSystem`** (`SystemOrder.cs:393`, with `CitizenTravelPurposeSystem` ordered before it at `:392`). It reads `trips[0]`, and either:

- Teleports: if the citizen is already in the target building, it sets `CurrentBuilding`, enables `Arrived` and adds `TravelPurpose` directly (`TripNeededSystem.cs:1536-1552`).
- Enqueues a pathfind: it builds a `PathfindParameters` and a `SetupQueueItem` and pushes it to the pathfind queue, adding a null `Target` as the pending marker (`:1518-1523`, and the second site at `:1176`).

Priority is assigned at that moment from `TripPriorityParametersData.GetPriority(purpose, citizen)` (`:1071`, `:1392`; the table at `src/Game/Game.Prefabs/TripPriorityParametersData.cs:39-81`), and the pathfinder's cost ceiling is `GetMaxCost(priority) = m_BaseMaxCost * priority / 128` (`:83-86`). `TripPriority.kDefault = 128` (`src/Game/Game.Citizens/TripPriority.cs:5`) is the neutral value, and a save older than `FormatTags.TripPriority` reads back 128 for every trip (`TripNeeded.cs:51-59`).

**Live priority table** from the singleton: base max cost 8000; going home 192, moving away 192, hospital/safety/escape/shelter/deathcare/criminal 128, work 96, school 96, shopping 64, tourist 64, leisure lerped between 32 and 96 by `1 - leisureCounter/255`.
So **going home is worth twice the path cost of going to work**, and a citizen with a full leisure counter will barely path for leisure at all.

`PathUtils.IsPathfindingPurpose` (`src/Game/Game.Pathfind/PathUtils.cs:1852-1868`) names the eight purposes that take the _citizen-level_ pathfind branch rather than the resource/target-seeker branch: `GoingHome, Hospital, Safety, EmergencyShelter, Crime, Escape, Sightseeing, VisitAttractions`.

**The citizen's own contribution to pathfinding is `CitizenUtils.GetPathfindWeights`** (`src/Game/Game.Citizens/CitizenUtils.cs:81-89`), and it is four numbers:

```
time     = 5 * (4 - 3.75 * m_LeisureCounter / 255)
behaviour= 2
money    = 2500 * max(1, householdSize) / max(250, household.m_ConsumptionPerDay)
comfort  = 1 + 2 * GetPseudoRandom(TrafficComfort).NextFloat()
```

with `money` multiplied by 0.1 for a resident whose household has not yet moved in.
Ten call sites use it — job seeking, school seeking, property seeking, leisure, resource buying, personal car, taxi, resident AI (`FindJobSystem.cs:201`, `FindSchoolSystem.cs:109`, `HouseholdFindPropertySystem.cs:392/398`, `LeisureSystem.cs:593`, `PersonalCarAISystem.cs:825`, `ResidentAISystem.cs:3102`, `ResourceBuyerSystem.cs:772`, `TaxiAISystem.cs:736`).
**A mod changing how citizens choose routes changes this one function**, not each system.

The mode decision also lives in `TripNeededSystem` (`:1466-1517`): a bike is chosen with probability 20%, adjusted by the home district's `BikeProbability` modifier; a reserved car overrides it; either sets `m_Methods`, `m_MaxSpeed`, parking target and ignored rules on the parameters.

Arrival is `CitizenTravelPurposeSystem` (`src/Game/Game.Simulation/CitizenTravelPurposeSystem.cs:153-265`), which promotes `GoingToWork → Working`, `GoingToSchool → Studying`, `Hospital → InHospital`, `GoingToJail → InJail`, `Deathcare → InDeathcare` and so on, and clears `TravelPurpose` for the purposes that end on arrival.

Rots: the `Purpose` enum's 41 members and their order — it is serialized as a `byte` (`TravelPurpose.cs:18`), so a reordering changes what old saves mean.

### Sleep and the idle loop

`CitizenBehaviorSystem` (`SystemOrder.cs:375`, interval 16 at `src/Game/Game.Simulation/CitizenBehaviorSystem.cs:1016`) is what a citizen does when it has no `TravelPurpose`. Its main job (`:616-745`) resolves the citizen's "home" — rented property, homeless temp home, tourist hotel, or an outside connection for a commuter (`:695-729`) — and then checks, in order: dead, imprisoned, moving away, meeting, sleep, leisure, shopping.

`GetSleepTime` (`:1026-1075`) starts from `(0.875, 0.175)` of a day, adds a per-citizen offset of up to 0.2 from the `SleepOffset` seed, shifts by −0.05 for elderly, −0.1 for children, +0.05 for teens, and then slides the window to avoid the citizen's work or study hours.
`kMinLeisurePossibility = 80`, `kLeisureSeekerCooldownFrames = 20000`, `kMaxPathfindCost = 17000` (`:982-986`).

`LeisureParametersData` tunes the leisure counter (live read): `m_ChanceCitizenDecreaseLeisureCounter = 2`, `m_ChanceTouristDecreaseLeisureCounter = 20`, `m_AmountLeisureCounterDecrease = 1`, `m_LeisureRandomFactor = 512`, `m_TouristLodgingConsumePerDay = 30`, `m_TouristServiceConsumePerDay = 10`. The decrease is rolled inside the happiness job, not the leisure one (`CitizenHappinessSystem.cs:497-501`).

### Criminals

`Criminal { m_Event, m_JailTime, m_Flags }` (`src/Game/Game.Citizens/Criminal.cs:8-12`), flags `Robber, Prisoner, Planning, Preparing, Monitored, Arrested, Sentenced` (`src/Game/Game.Citizens/CriminalFlags.cs:8-14`).

**A citizen becomes a criminal through the event system, not directly.** `CrimeCheckSystem` (`SystemOrder.cs:515`, `kUpdatesPerDay = 1` at `src/Game/Game.Simulation/CrimeCheckSystem.cs:260`) rolls a citizen against every crime event prefab's probability and creates a crime **event** entity. The candidate set is narrower than "adult or teen": children and elderly are excluded by age (`:108-111`) and the query at `:296-311` also excludes `HealthProblem`, `Worker` and `Student`, so only unemployed, non-studying, healthy adults and teens are ever rolled. `Game.Events.InitializeSystem.InitializeCrimeEvent` then resolves the event's targets to citizens and emits an `AddCriminal { m_Event, m_Target, m_Flags }` with `CriminalFlags.Planning`, plus `Robber` when the crime type is `Robbery` (`src/Game/Game.Events/InitializeSystem.cs:752-789`). `AddCriminalSystem` (`SystemOrder.cs:168`, `Modification4`) merges that into the `Criminal` component (`src/Game/Game.Events/AddCriminalSystem.cs:52-84`).

The probability scale is a population-relative one: the roll is `random.NextFloat(max) < probability` with `max = max(population / m_CrimePopulationReduction * 100, 100)` (`CrimeCheckSystem.cs:159-160`), so **crime rate per citizen falls as the city grows unless the parameter is tuned**. A repeat offender's roll is further suppressed by welfare coverage at their home, against the same ceiling (`:164-174`), so welfare's protection thins with growth too. Both fields are on `PoliceConfigurationData` (`src/Game/Game.Prefabs/PoliceConfigurationData.cs:21`, `:25`) — a component the first draft named nowhere, which left `m_CrimePopulationReduction` unroutable.

`CrimeData` on the event prefab carries the balance (`src/Game/Game.Prefabs/CrimeData.cs:8-28`): `m_OccurenceProbability`, `m_RecurrenceProbability`, `m_AlarmDelay`, `m_CrimeDuration`, `m_CrimeIncomeAbsolute`, `m_CrimeIncomeRelative`, `m_JailTimeRange`, `m_PrisonTimeRange`, `m_PrisonProbability`, all as `Bounds1` min/max ranges.

`CriminalSystem` (`SystemOrder.cs:553`, interval 16, `SYSTEM_UPDATE_INTERVAL = 16u` at `src/Game/Game.Simulation/CriminalSystem.cs:836`) runs the state machine (`:162-403`): `Planning → Preparing → (crime committed) → Arrested → Sentenced → Prisoner → released`. `m_JailTime` is stored as `duration * 262144 / 256` (`:261`, `:371`) and decremented per pass. While `Prisoner`, the citizen's happiness picks up the prison building's prisoner health and wellbeing — from `Game.Buildings.Prison` on the building entity (`CitizenHappinessSystem.cs:172`, read at `:324-327`), not from `PrisonData` on its prefab, and **added to** a school's rather than replacing it: `:323-331` initialises one accumulator and both branches `+=` into it with no guard between them, and `ApplyToSchoolSystem`'s query does not exclude `Criminal`, so an imprisoned citizen can be enrolled and carry both, and `CitizenBehaviorSystem` skips the citizen's idle loop entirely while `Prisoner | Arrested | Sentenced` (`CitizenBehaviorSystem.cs:627-630`).

**Criminality is not a citizen archetype**: the citizen keeps its job, its household and its education, and the `Criminal` component is added and removed around the episode. The UI reflects that — `CitizenUIUtils.GetOccupation` returns `Robber` or `Criminal` ahead of `Worker` and `Student` purely as a display precedence (`src/Game/Game.UI.InGame/CitizenUIUtils.cs:108-144`).

**Verified live.** 20 citizens carried `Criminal` out of 13,441 in the loaded city, and the one sampled (entity 50743) was simultaneously an evening-shift level-2 `Worker` with `CriminalFlags.Robber` and no event — a finished episode whose flag had not been cleared. So a mod filtering on `Criminal` presence alone will include citizens who are not currently committing anything; the `m_Event != Entity.Null` test at `CrimeCheckSystem.cs:113` is how the game itself distinguishes them.

### Tourists

A tourist is a whole household, spawned by `TouristSpawnSystem` (`SystemOrder.cs:380`, interval 16) from the city's `Tourism.m_Attractiveness` and the current tourist count via `TourismSystem.GetTouristProbability`, which also takes weather, temperature and precipitation (`src/Game/Game.Simulation/TouristSpawnSystem.cs:76-116`).
The household gets `HouseholdFlags.Tourist`, a `TouristHousehold { m_Hotel = null, m_LeavingTime = 0 }` and a `CurrentBuilding` at a randomly chosen outside connection weighted by `m_TouristOCSpawnParameters`.

Its citizens are then spawned by `HouseholdInitializeSystem` with `CitizenFlags.Tourist` (`src/Game/Game.Citizens/HouseholdInitializeSystem.cs:105-113`, the `tourist` argument taken from the chunk carrying `TouristHousehold` at `:149`). Initial money is `rand(m_TouristInitialWealthRange) - range/2 + m_TouristInitialWealthOffset` rather than the household prefab's own band (`:162`). **Live**: range 6000, offset 3000.

What differs from a resident, mechanically:

- **Leisure**: starts at `rand(128)` instead of `128 + rand(92)` (`CitizenInitializeSystem.cs:97-102`), decays ten times as fast (`m_ChanceTouristDecreaseLeisureCounter = 20` against 2), and scores a **flat +7** wellbeing regardless of the counter (`CitizenHappinessSystem.cs:1473-1479`).
- **Tax**: `GetTaxBonuses` is skipped for a tourist (`:508-511`), and they are not in the residential tax base.
- **Unemployment**: `GetUnemploymentBonuses` returns zero (`:1345-1353`).
- **Consumption**: multiplied by `m_TouristConsumptionMultiplier` (**live: 2**) (`HouseholdBehaviorSystem.cs:237-239`).
- **Happiness eligibility**: a tourist is the one exception to the moved-in gate (`CitizenHappinessSystem.cs:294`).
- **No bicycle** (`CitizenInitializeSystem.cs:209`), no births (`BirthSystem.cs:117`), not workable (`CitizenUtils.cs:219-230`), not a resident for any of the four `IsResident`/`TryGetResident` predicates (`CitizenUtils.cs:72-140`).
- **Lodging**: `TouristHouseholdBehaviorSystem` (`SystemOrder.cs:365`) and `HouseholdBehaviorSystem` add `LodgingSeeker` when the household has no hotel or its hotel is gone (`HouseholdBehaviorSystem.cs:240-253`). `MoveAwayReason` carries three tourist-specific exits: `TouristNoTarget`, `TouristNoHotel`, `TouristNoMoney` (`src/Game/Game.Agents/MoveAwayReason.cs:3-14`).

**Verified live**: 17 tourist households, 30 tourist citizens. A sampled tourist household carried `TouristHousehold { m_Hotel = null }`, `Household { m_Flags = Tourist, m_Resources = 341 }`, an enabled `PropertySeeker` and a `Target` — so tourists use the ordinary property-seeking machinery to find their hotel.

### Commuters

`CommuterSpawnSystem` (`SystemOrder.cs:381`, interval 16) is demand-driven rather than probabilistic: it computes `(freeWorkplaces[2..4] - employables[2..4]) / m_CommuterSlowSpawnFactor` and spawns exactly that many households (`src/Game/Game.Simulation/CommuterSpawnSystem.cs:59-93`).
**Commuters exist to fill educated vacancies the local population cannot** — levels 0 and 1 are excluded from both sides of that subtraction.

Each gets `HouseholdFlags.Commuter`, `CommuterHousehold { m_OriginalFrom }` naming the outside connection it came from, and `CurrentBuilding` there.
Its citizens carry `CitizenFlags.Commuter`.

Differences: wage ×`m_CommuterWageMultiplier` (**live: 1.1**) and no residential tax at all (`PayWageSystem.cs:115-118`, `:158`); no pets (`HouseholdInitializeSystem.cs:183`); no bicycle (`CitizenInitializeSystem.cs:209`); not workable and not a resident (`CitizenUtils.cs:219-230`, `:72-79`); education drawn from the wider 0–4 band at spawn (`CitizenInitializeSystem.cs:156`); and a commuter child or elderly citizen is **deleted outright** by `CitizenBehaviorSystem` (`:662-667`).
`JobSeeker.m_Outside` is set from the commuter flag (`CitizenFindJobSystem.cs:185`), and a commuter's job seeker starts from its `CurrentBuilding` rather than a home property (`:171-174`).

A commuter is also created out of a resident: `LeaveHouseholdSystem` converts a departing new adult into one when the city has fewer than eleven free residential properties (`LeaveHouseholdSystem.cs:119-135`).

**Verified live**: 733 commuter households.

### Homelessness

`HomelessHousehold { m_TempHome }` is added by `HouseholdBehaviorSystem` when a moved-in household loses its `PropertyRenter` (`HouseholdBehaviorSystem.cs:178-186`).
A homeless household **loses ₡1 per pass** (`:195`) instead of consuming goods, and looks **harder** for a home, not less: `num8` at `:216-221` is the denominator of a `random.NextInt(num8) == 0` roll, and `:219` divides it by `m_LookForHomeHomelessDivisor` — **live: 10** — which multiplies the per-pass chance tenfold. One with no usable temp home skips the roll entirely and is enabled as a `PropertySeeker` outright (`:199-203`).
`CountHouseholdDataSystem` mirrors the state onto every member as `CitizenFlags.Homeless` (`CountHouseholdDataSystem.cs:324`, `:405`).
Happiness applies `GetHomelessBonuses` and scores the apartment factor at the floor (`CitizenHappinessSystem.cs:485-495`).

**Verified live, and the two counts disagree.** An `ecs_query` on `Game.Citizens.HomelessHousehold` returned **404** households; `CountHouseholdDataSystem.GetHouseholdCountData()` reported **249** homeless households and 304 homeless citizens in the same session.
The gap is real, and the first explanation written for it was wrong. The census's household tally at `:451-454` applies **no** moved-in filter — it adds `chunk.Count` for every `HomelessHousehold` chunk. It comes out smaller for three other reasons: the query excludes `Deleted` and `Temp` (`:1017-1025`), a chunk carrying `MovingAway` returns early and is booked as moving-away (`:438-443`), and `OnUpdate` publishes the previous pass's snapshot (`:1224`). The moved-in filter is real one level down, for citizens: `CitizenFlags.Homeless` and `ValidCitizen` are stamped only on members of moved-in households (`:315-324`) and `CountCitizensJob` requires `ValidCitizen` (`:540`).
`HouseholdBehaviorSystem` is also not the only writer of `HomelessHousehold`: `PropertyProcessingSystem.cs:435` and `Game.Serialization/RenterSystem.cs:104` add it for shelter tenancy with no moved-in test, and `HouseholdMoveAwaySystem.cs:125` adds it too.
**A mod counting homelessness gets a different number depending on which it asks**, and the census is the one the game's own UI reports.

### Household consumption, shopping and moving away

`HouseholdBehaviorSystem` (`SystemOrder.cs:364`, `kUpdatesPerDay = 256` at `HouseholdBehaviorSystem.cs:425`) is the household's own tick.

Consumption (`:231-261`): while `m_Resources > 0`, it burns `GetConsumptionMultiplier(m_ResourceConsumptionMultiplier, totalWealth) * m_ResourceConsumptionPerCitizen * citizenCount` per pass and records `m_ConsumptionPerDay = 256 * that`. **Live**: `m_ResourceConsumptionPerCitizen = 1`.
When resources hit zero it raises a `HouseholdNeed { m_Resource, m_Amount }` and the household goes shopping, gated on `kMinimumShoppingMoney = 1000` spendable (`:266-269`, constants at `:423-433`: `kCarAmount = 50`, `kMaxShoppingPossibility = 80`, `kMaxHouseholdNeedAmount = 2000`, `kCarBuyingMinimumMoney = 10000`).

Moving away (`:156-177`) has three triggers, evaluated in order: **`NoAdults`** (no member is adult or older), **`NotHappy`** (the happiness curve above), **`NoMoney`** (`totalWealth + dailyIncome < -1000`). `CitizenUtils.HouseholdMoveAway` stamps `MovingAway { m_Reason }` (`CitizenUtils.cs:266-280`), and `CitizenBehaviorSystem` then sends every member to an outside connection with `Purpose.MovingAway`, stripping `Worker`, `Student` and `Leisure` on the way (`CitizenBehaviorSystem.cs:673-694`).

Property seeking (`:197-226`) rolls `1 / clamp(m_LookForHomePopulationFactor * population, clamp.x, clamp.y)` adjusted by how far the rent-to-income ratio sits outside `m_LookForHomeRentIncomeIdealBand`. **Live**: ideal band (0.3, 0.7), chance multiplier (0.5, 2.0), population factor 0.015, clamp (16, 256).

### The aggregate seam: `CountHouseholdDataSystem`

`CountHouseholdDataSystem` (`SystemOrder.cs:386`, interval 16) is the one place that walks the whole population and produces a census. Its `HouseholdData` struct (`src/Game/Game.Simulation/CountHouseholdDataSystem.cs:37-130`) carries moving-in/moving-away/moved-in household and citizen counts, commuter households, tourist citizens, homeless households and citizens, the four age counts, student count, the five education counts, workable and city-worker counts, dead count, the three happiness totals, and five `m_EmployableByEducation*` counts.

Five public methods, not three (`:984-1010`): `GetHouseholdCountData()`, `GetResourceNeeds(out JobHandle)`, `IsCountDataNotReady()`, `GetEmployables(out JobHandle)` and **`AddHouseholdDataReader(JobHandle)`**, beside 26 read-only properties over the same snapshot (`:862-977`). The last is the other half of the protocol the two handle-returning accessors start: they hand out `m_HouseholdDataWriteDependencies` (`:991`, `:1002`) and `:1271` combines `m_HouseholdDataReadDependencies` into the next `ResultJob`, which is the only thing stopping it overwriting `m_ResourceNeed`/`m_EmployableByEducation` under a live reader. Every vanilla job-scheduling reader registers back (`CommuterSpawnSystem.cs:174`+`:185`, `FindJobSystem.cs:586`+`:593`, `IndustrialDemandSystem.cs:1177`+`:1208`); main-thread readers that complete before returning do not. `GetHouseholdCountData()` is a managed instance method and cannot be called from inside a Bursted job.
`CommuterSpawnSystem` consumes `GetEmployables` (`CommuterSpawnSystem.cs:174`).
The workplace side of the same picture is a sibling, `CountWorkplacesSystem`, whose `GetUnemployedWorkspaceByLevel()` and `GetFreeWorkplaces()` are what `CitizenFindJobSystem` gates its two job-seek branches on (`CitizenFindJobSystem.cs:356`, `:380`).
**Those two systems together are the labour-market seam**: a mod changing who can be hired reads both.

**This is the cheapest read in the topic**: a mod wanting population statistics calls `GetHouseholdCountData()` instead of building its own query.
The same system also owns the `ValidCitizen` and `Homeless` flag maintenance (`:294-408`), which is why it is not merely a counter.

**Verified live** by calling `GetHouseholdCountData()` through the debugger: 4,943 moved-in households, 11,511 moved-in citizens, 1,009 moving in, 660 moving away, 733 commuter households, 30 tourist citizens, 249 homeless households / 304 homeless citizens, ages 2,781/756/6,454/1,520, 4,385 students, 5,494 workable, 4,897 city workers, 0 dead. Employables by education: 41/877/169/7/1 — i.e. the unemployment sits almost entirely at education level 1, which is what a city of Elementary graduates and Complex workplaces produces.

### Partitioning is per-system, and the constant is not the rate

Two independent facts decide a system's rate, and the first draft of this finding collapsed them into one.
**Passes per day is `262144 / GetUpdateInterval()`, with no exceptions.**
**Consuming `UpdateFrame` divides the per-entity rate by sixteen on top of that**, and whether a system does is its own decision — not a property of the area, and not readable from its query.
Declaring `ComponentType.ReadOnly<UpdateFrame>()` is not consuming it, and declaring nothing is not skipping it: ten systems here declare it and take the real test inside the job (`CrimeCheckSystem.cs:301` declares it, `:93` tests it — the canonical form), five partition without declaring it at all (`AgingSystem.cs:68` among them), and only `CitizenHappinessSystem` filters at the query (`:1820`).
Ten, five and one account for the sixteen the table below marks yes; a roster that does not sum against it has misfiled a system.

The set of unpartitioned systems is far larger than the four seeker-and-singleton ones first recorded. A `grep -c UpdateFrame` returns zero for `StudentSystem`, `CountHouseholdDataSystem`, `CitizenTravelPurposeSystem`, `TripNeededSystem`, `CountWorkplacesSystem`, `HouseholdSpawnSystem`, `PartnerSystem`, `LeaveHouseholdSystem` and `ServiceFeeSystem`, on top of `FindJobSystem`, `FindSchoolSystem`, `TouristSpawnSystem` and `CommuterSpawnSystem`.

The measured intervals and rates at 1.6.0f1:

| System                                                                                                                 | Interval | `UpdateFrame` | Per entity, per day |
| ---------------------------------------------------------------------------------------------------------------------- | -------- | ------------- | ------------------- |
| `AgingSystem`, `CrimeCheckSystem`, `GraduationSystem`                                                                  | 16384    | yes           | 1                   |
| `LeaveHouseholdSystem`                                                                                                 | 8192     | **no**        | **32**              |
| `LookForPartnerSystem`, `DivorceSystem`                                                                                | 4096     | yes           | 4                   |
| `PartnerSystem`                                                                                                        | 4096     | **no**        | **64**              |
| `ServiceFeeSystem`                                                                                                     | 2048     | **no**        | 128                 |
| `BirthSystem`, `DeathCheckSystem`                                                                                      | 1024     | yes           | 16                  |
| `PayWageSystem`, `ApplyToSchoolSystem`                                                                                 | 512      | yes           | 32                  |
| `HouseholdBehaviorSystem`, `CitizenFindJobSystem`, `LeisureSystem`                                                     | 64       | yes           | 256                 |
| `CitizenBehaviorSystem`, `CitizenHappinessSystem`, `WorkerSystem`, `CriminalSystem`                                    | 16       | yes           | 1024                |
| `StudentSystem`, `CitizenTravelPurposeSystem`, `TripNeededSystem`, `CountHouseholdDataSystem`, `CountWorkplacesSystem` | 16       | **no**        | **16384**           |
| `FindJobSystem`, `FindSchoolSystem`, `HouseholdSpawnSystem`, `TouristSpawnSystem`, `CommuterSpawnSystem`               | 16       | no            | 16384               |

**`kUpdatesPerDay` is not the rate**, and two conventions coexist.
Most constant-carrying systems compute `262144 / (kUpdatesPerDay * 16)`, where the constant is the _intended_ per-entity rate and matches the last column only because the system also partitions — it does not for `LeaveHouseholdSystem` (2 against 32) or `PartnerSystem` (4 against 64).
`ServiceFeeSystem` uses `262144 / kUpdatesPerDay` with the constant as passes per day (`:330`, `:352`), and `LeisureSystem` uses that convention and spells it `kUpdatePerDay = 4096` (`LeisureSystem.cs:868`, `:916`), giving interval 64 and a real rate of 256.
`GraduationSystem` declares `kUpdatesPerDay = 1` (`:233`) and never reads it, hard-coding both the interval and the bucket argument.
Where the constant does matter is inside the job: `BirthSystem.cs:151`, `DivorceSystem.cs:131`, `DeathCheckSystem.cs:215`, `CitizenFindJobSystem.cs:101/107/114`, `PayWageSystem.cs:119-157` and `HouseholdBehaviorSystem.cs:256` each divide a daily probability or amount by it.

**A fork must reproduce the interval and the partitioning decision**, and getting either wrong is silent — adding a filter the vanilla system lacks runs the fork at a sixteenth of the rate, dropping one it has runs it sixteen times too often.
The bucket index is `SimulationUtils.GetUpdateFrameWithInterval(frame, interval, groupCount)`, and `GetUpdateFrame(frame, updatesPerDay, groupCount)` is the same function with `262144 / (updatesPerDay * groupCount)` substituted for the interval (`SimulationUtils.cs:153-161`).
The corpus's one large substitution does reproduce it, and that is checkable: `Time2Work/NightShift/Systems/Time2WorkCitizenBehaviorSystem.cs:63` overrides `GetUpdateInterval` to 16, `:312` recomputes `SimulationUtils.GetUpdateFrameWithInterval(frameIndex, interval, 16)` and `:332`/`:376` hand it to the job as a `SharedComponentTypeHandle<UpdateFrame>` plus an index, exactly as the vanilla system does. `Time2WorkWorkerSystem.cs:46`/`:364` and `Time2WorkStudentSystem.cs:40` are the same shape.
It also overrides `GetUpdateOffset(phase) => 11` (`Time2WorkCitizenBehaviorSystem.cs:65`), and this took three readings to get right.
The first draft called it a hook vanilla does not use, which staggers the fork away from the disabled system's slot — wrong on both halves, since `CitizenBehaviorSystem.cs:1021-1024` returns 11 itself and the fork is pinning to the vanilla offset rather than moving off it.
The second called the override redundant, on the grounds that `UpdateSystem.cs:389-392` gives an anchored system its anchor's offset when the intervals match and the fork's own offset is still negative — also wrong, because that inheritance reaches only systems in `m_RefMap`, which the four-argument `Register` populates and only the two-type `UpdateBefore<S, Other>` / `UpdateAfter<S, Other>` overloads call.
`Mod.cs:122` registers this fork with the single-type `UpdateAt<Time2WorkCitizenBehaviorSystem>(SystemUpdatePhase.GameSimulation)`, so it has no anchor and inherits nothing.
**The explicit 11 is therefore load-bearing**: given `UpdateAt`, it is the only thing putting the fork on the frames the disabled system ran on, and it works because `CitizenBehaviorSystem` declares its offset as a literal a fork can copy.
This paragraph is research only and **does not travel to the reference**: whatever it rests on, its conclusion is registration and ordering technique that `mod-lifecycle-and-ordering` owns rather than anything this mechanics topic teaches. Recorded here so the next pass does not re-derive it. The literal is the fallback rather than the technique. Anchoring the fork with `UpdateBefore<Fork, Original>` would have made it unnecessary, because the inheritance copies the anchor's **resolved** offset — so it also works for an original that declares none, which is the case a copied literal cannot cover. And that case is the common one: 70 `Game.Simulation` systems override `GetUpdateOffset` against 264 overriding `GetUpdateInterval` with a non-unit value, so roughly three in four systems a fork would target take their offset from the spreading and expose no literal to copy.

Rots: every number in that table.

### The corpus: one mod substitutes this area wholesale

`ruzbeh0/Time2Work` (catalogued as **Realistic Trips**) is the only repository in the corpus that replaces citizen simulation rather than reading it.
`Time2Work/NightShift/Mod.cs:94-101` disables, by name:

- `Game.Simulation.CitizenBehaviorSystem`
- `Game.Simulation.CitizenTravelPurposeSystem`
- `Game.Simulation.WorkerSystem`
- `Game.Simulation.LeisureSystem`
- `Game.Simulation.StudentSystem`
- `Game.Simulation.TourismSystem`, `TouristSpawnSystem`, `AttractionSystem`
- `Game.Simulation.BuyingCompanySystem`
- `Game.Simulation.DeathCheckSystem` (conditionally, at `:107-115`, skipped when a named competing mod is present)

and registers forks in `SystemUpdatePhase.GameSimulation` at `:116-145`: `Time2WorkCitizenBehaviorSystem`, `Time2WorkCitizenTravelPurposeSystem`, `Time2WorkWorkerSystem`, `Time2WorkLeisureSystem`, `Time2WorkStudentSystem`, `Time2WorkDeathCheckSystem`, `Time2WorkTourismSystem`, `Time2WorkTouristSpawnSystem`, plus its own `Time2WorkTimeSystem`, `WorkPlaceShiftUpdateSystem`, `WorkerShiftUpdateSystem`, `CitizenScheduleSystem`, `SocialTripSystem`, `HospitalStaySystem`.
It also injects parameter-rewriting systems, **which is how it retunes the balance data this whole topic reads from prefab singletons without editing any prefab asset** — but not in the way first recorded here. There is no band between two phases: `TimeSettingsMultiplierSystem`, `HealthEventProbabilityScalerSystem` and `DemandParameterUpdaterSystem` are each registered **twice** (`Mod.cs:154-159`), `UpdateAfter(PrefabUpdate)` and `UpdateBefore(PrefabReferences)`, which buys two run opportunities rather than a span; `EconomyParameterUpdaterSystem` is registered `UpdateAt(GameSimulation)` (`:143`) and re-applies on its own interval; and `HealthEventProbabilityScalerSystem` rewrites `HealthEventData` per health-event prefab, not a singleton at all.
The generalisable technique is **re-apply on every load**, and the first two drafts of this finding both got it wrong. `LateInitialize` is the first writer, called from `PrefabInitializeSystem` in `PrefabUpdate` for entities carrying `Created` — but it is not the only one. `GameModeSystem` (registered in the front band of `Deserialize`, `SystemOrder.cs:796`) calls `RestoreDefaultData` then `ApplyMode` on every `GameManager.Load`, and eight of the eleven parameter components this topic reads have a mode class under `Game.Prefabs.Modes` that rebuilds them field by field from authored prefab data (`EconomyParametersMode.cs:156`, `:207`, and seven siblings). `TimeSettingsData`, `CitizenParametersData` and `TripPriorityParametersData` have none and are genuinely write-once.
Where the mode pass sits took two wrong readings before this one. It is registered `UpdateBefore<GameModeSystem>(Deserialize)` (`SystemOrder.cs:796`), so it runs in the **front band** — first in the phase, not last. Everything registered `UpdateAt(Deserialize)` runs after it, including `SerializerSystem` (`:798`), `ResolvePrefabsSystem` (`:801`) and the whole `PrefabReferences` sub-phase that one drives, and a mod's registration is later still. So an ordinary `Deserialize` placement already lands behind the pass and needs no anchor; what the pass is, is the last vanilla **writer** of these components in a load, which is the fact the guidance actually rests on. So a one-shot overwrite holds for its own session and is gone from the next load, which is why `EconomyParameterUpdaterSystem`'s `GameSimulation` placement with an interval works and a `PrefabUpdate` one-shot would not.
**Live, 1.6.0f1, the user's running game**: `GameModeSystem.modeSetting` was `EasyMode`, whose `m_ModePrefabs` holds 21 entries including `CitizenHappinessParameterMode`, `EconomyParametersMode`, `HealthcareParametersMode` and `PoliceConfigurationMode` — while `NormalMode` holds **zero**. So whether a load rewrites a given parameter component is a per-mode fact, not a general one, and no code read can answer it: `ModeSetting.m_ModePrefabs` is authored asset data, and a mode prefab is a base-game one, so it sits in `Cities2_Data/resources.assets` behind a field-order derivation rather than in any `.cok`. Read it through `PrefabSystem.GetPrefab<ModeSetting>` on the two `GameModeSettingData` entities. This is the finding that turns "re-apply on every load" from cautious advice into the rule.
**How far this generalises stayed open, and nothing ships on it** — reach and scope are both authored asset data, so no code read settles whether a given player's save rewrites a given component. The passage that would have carried this was cut from ticket 22 after fourteen review rounds; [retuning a parameter component the game mode rewrites](../solutions/retuning-a-parameter-component-the-game-mode-rewrites.md) records what the investigation established and what it did not, and is where the next ticket starts. No shipped reference warns a reader about it yet.

**A type-name grep will not find these writers**: `ModeSetting` dispatches through `ApplyModeData`/`RestoreDefaultData` on a `ModePrefab` base, and the concrete write is a bare `SetComponentData` inside each mode class. `Game.Prefabs.Modes` writes 65 parameter and service components this way, so it is the directory to check before calling any parameter singleton write-once. [A search taken for a census](../solutions/empty-grep-read-as-proof-of-absence.md), for the second time in this ticket.

The corpus settles nothing about the game, per `SOURCES.md` entry 11. What it establishes is the **boundary of the fork unit**: the five citizen-behaviour systems come as a set, because `CitizenBehaviorSystem` decides idleness and `WorkerSystem`/`StudentSystem`/`LeisureSystem` each append trips into the same buffer that `CitizenTravelPurposeSystem` consumes. Disabling one of the five and not the others leaves the buffer being written by a mix of vanilla and forked schedules.
It also shows what a fork does with the archetype: `Time2WorkCitizenBehaviorSystem.cs:281` rebuilds the household archetype with the vanilla seven components **plus its own `HouseholdShoppingCooldown`**, so a fork extends the entity rather than mirroring it.

**Notably it does not touch** `CitizenHappinessSystem`, `GraduationSystem`, `ApplyToSchoolSystem`, `CitizenFindJobSystem`, `FindJobSystem`, `PayWageSystem`, `AgingSystem`, `BirthSystem`, `CriminalSystem` or `HouseholdBehaviorSystem`. So the largest citizen mod in the corpus leaves happiness, education outcomes, employment matching and the money flows entirely vanilla — which is evidence that those are separable, and evidence about what nobody has tried.

### Catalog gap: `Realistic Trips` (`ruzbeh0/Time2Work`)

Closed: a **Demonstrates** line on parameter-rewriting systems shipped to `plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md`.

Source lines behind it: `Time2Work/NightShift/Mod.cs:154-159` (the three `UpdateAfter`/`UpdateBefore` pairs), against the singletons this file reads at `src/Game/Game.Prefabs/EconomyParameterData.cs` and `src/Game/Game.Prefabs/CitizenParametersData.cs`.

### Catalog gap: `InfoLoom`

Closed: a **Demonstrates** line on the read-only citizen census shipped to `mod-catalog.md`. The proposed wording called it the corpus's only such census and claimed the vanilla predicates are undiscoverable; both are claims about a corpus one repository cannot establish, and neither shipped.

Source lines: `InfoLoom/InfoLoom/Systems/DemographicsData/Demographics.cs:53-57` (the lookups), `:77-101` (the exclusions, which reproduce `CitizenUtils.IsResident`'s logic inline), `:133-248` (the age/education/occupation bucketing); and `InfoLoom/InfoLoom/Systems/WorkforceData/WorkforceSystem.CountEmploymentJob.cs:71-104`, which reimplements `CitizenUtils.IsWorkableCitizen` rather than calling it.
That last detail is the instructive one for a mechanics reader: a mod author independently arrived at the same predicate the game exports, which is evidence the predicate is the right query boundary — and evidence that its existence at `src/Game/Game.Citizens/CitizenUtils.cs:219-230` is not discoverable.

The sweep over the rest of the catalog found no other entry whose **Demonstrates** names a system in this area. `PlopTheGrowables`, `NodeController` and `Traffic` demonstrate substitution but over zoning, geometry and lanes; `Anarchy`, `Recolor` and the tool mods reference `Game.Citizens` only through bulldozer and selection filtering. Recorded as a dead end below.

### Save serialization: what a citizen writes, and the version gates

`Citizen.Serialize` writes ten values in a fixed order (`Citizen.cs:119-141`). `Deserialize` (`:143-184`) discards a leading `uint` in a pre-`Version.saveOptimizations` save, and gates five fields: `m_PenaltyCounter` on `Version.penaltyCounter`, `m_PseudoRandom` on `Version.snow`, `m_UnemploymentCounter` on `Version.economyFix`, `m_UnemploymentTimeCounter` on `FormatTags.UnemploymentAffectHappiness`, `m_SicknessPenalty` on `FormatTags.SicknessHealthPenalty`.

Household gates: `Version.householdRandomSeedRemoved` (a discarded `uint`), `FormatTags.HouseholdConsumptionFix` (the three shopping fields), `FormatTags.TrackCitizenEconomyStats` (`m_SalaryLastDay`, `m_MoneySpendOnBuildingLevelingLastDay`) (`Household.cs:44-76`). `Household.Deserialize` also **clamps `m_Resources` to zero** on load (`:56-59`).

Others: `Student.m_Level` defaults to `byte.MaxValue` before `Version.educationTrading` (`Student.cs:30-38`), and `GraduationSystem` reads 255 as "use the school's own level" (`GraduationSystem.cs:106-110`). `HasSchoolSeeker.m_Seeker` needs `Version.seekerReferences`. `CommuterHousehold.m_OriginalFrom` needs `Version.commuterOriginalFrom`. `Criminal` widened its flags from `byte` to `ushort` at `Version.policeImprovement2` (`Criminal.cs:28-39`). `TripNeeded.m_Priority` defaults to 128 before `FormatTags.TripPriority`, and `m_Resource` was an `int` before `Version.resource32bitFix` (`TripNeeded.cs:41-59`).

`HouseholdCitizen` is `IEmptySerializable` — the buffer's contents are not written and are rebuilt from members' `HouseholdMember` back-references on load, the same shape as the `SubObject` buffer described in `conflicts.md`'s third addendum to the empty-prefab entry.

**These version tags are the record of which fields are newest**, and so of where the next version is most likely to add one.
Rots: every `Version.*` and `FormatTags.*` name in this finding.

### Source-list gap: no source missing, one entry grown

`docs/SOURCES.md` entries 1, 8, 10 and 11 covered everything this pass needed, and each behaved as described.
One entry grew on what this pass found: entry 8 said the running game settles "live component values, real ECS query results". For this topic it settled more than that — **every balance number in the area lives in a prefab singleton, and the running game reads it in one call**, where entry 5 correctly warns that a base-game prefab in `resources.assets` needs a parser. The two are consistent; the running game is the short road entry 5 already names, and this pass is a worked instance of that advice paying off. Entry 8 now also carries the reach this pass established — that the running game reaches the managed object behind a prefab entity, which no component on that entity carries.

### Evidence for the `conflicts.md` entry on the wiki's stat tables — ruled, and kept as the evidence it was ruled on

**Ruled (2026-08-06, ticket 22; conflicts.md).** This finding is the evidence, not an open question. The entry it was written for is ruled and the decision is at the head of this file: no wiki stat table is borrowed, and no prefab-singleton value ships. Read what follows as why that ruling could be made, and take no instruction from it — in particular, the table below is a map of where the numbers live, which is the part the reference does owe its reader, and not a list of numbers to state.

The entry "A pre-launch balance page whose values are stale and whose schema is not" named `citizens-and-households` among the seven topics that borrow a wiki stat table. What it asked each topic for was which of those numbers could be re-derived first-party, and at what cost.

For this topic the answer is unusually favourable: **every number the wiki's `Citizens` page tabulates lives in a prefab singleton reachable from a running game in one component read**, not in `resources.assets`.

| Wiki table                                                | First-party source                                 | Cost               |
| --------------------------------------------------------- | -------------------------------------------------- | ------------------ |
| Wages by education                                        | `EconomyParameterData.m_Wage0..4`                  | one read           |
| Family allowance, pension, unemployment benefit, max days | same component                                     | same read          |
| Residential minimum earnings, commuter multiplier         | same component                                     | same read          |
| School fees                                               | `ServiceFeeParameterData.m_BasicEducationFee` etc. | one read           |
| Work efficiency formula                                   | `EconomyUtils.GetWorkerWorkforce`                  | decompile, no cost |
| Life-stage lengths                                        | `AgingSystem.GetTeenAgeLimitInDays()` etc.         | decompile, no cost |
| Happiness factor list                                     | `CitizenHappinessSystem.HappinessFactor`           | decompile, no cost |
| Happiness factor magnitudes                               | `CitizenHappinessParameterData`                    | one read           |

This pass ran all of those, against the eight money claims the wiki's page makes. **Four matched** (the five wages, residential minimum earnings, commuter wage multiplier, unemployment allowance max days). **Three were stale** (family allowance, pension, unemployment benefit). **One came back at exactly half the wiki's figures** — the school fees, 25/50/100 against ₡50/₡100/₡200.
That half-ratio is worth flagging for the ruling rather than resolved here: it could be a balance change, or it could be that the wiki's "per month" is a different unit from `m_Default`, since `ServiceFeeSystem` charges `m_Default / 128` per invocation and this pass did not derive the per-month total. Either reading leaves the wiki's number unusable as a citable figure without stating which unit it is in.
So for this topic the entry's added option — point the reference at first-party numbers instead of borrowing the page — costs one running game and no parser, and the evidence says the page's numbers cannot be borrowed wholesale in any case.

---

## Bridge

The techniques a change in this area needs:

- **`ecs-in-this-game`** is the hard prerequisite, and specifically its `UpdateFrame` material. Sixteen of the systems here partition into sixteen buckets, all but one by the in-job chunk test — that reference carries both forms at `plugins/cs2-modding/skills/cs2-modding/references/technique/ecs-in-this-game/ecs-in-this-game.md:97-108`, and the per-job reading is in its sibling `plugins/cs2-modding/skills/cs2-modding/references/technique/ecs-in-this-game/update-frame-buckets.md:13-19`. `CitizenHappinessSystem` filters at the query instead (`:1820`), and the thirteen systems the table marks **no** do not partition at all. The interval table above is unusable without this material. That reference's finding that a gate tests its query ignoring the filter (`ecs-in-this-game.md:154-156`) applies to `CitizenHappinessSystem` alone, since it is the only system here whose gate is a filtered query — what it costs is unmeasured, and the sibling systems at the same interval run on the same passes regardless.
- **`simulation-time-and-units`** owns the frame arithmetic these systems are all expressed in: `262144` frames per day (`AgingSystem.cs:204`), `kUpdatesPerDay`, `SimulationUtils.GetUpdateFrame`, `TimeSystem.GetDay`, and `TimeSettingsData.m_DaysPerYear` (a prefab-singleton value, so the reference names the field and not the number), which is the conversion every age threshold here needs. A citizen's `m_BirthDay` is a day number and `GetMaxHealth` takes years, so a mod touching ageing crosses that boundary twice.
- **`save-serialization`** owns the version and format tags catalogued above. The specific hazard this topic contributes: `Citizen` packs age, education and failed-education counts into one serialized `short`, so **a mod adding a flag to `CitizenFlags` takes a bit out of a word that is written as-is**, and `HouseholdCitizen` is `IEmptySerializable` and rebuilt on load, so a mod holding a citizen reference must survive that rebuild.
- **`zoning-buildings-and-land-value`** (mechanics) is on the other side of `PropertyRenter` and `Renter`. This topic decides _that_ a household seeks, moves in, pays rent and moves away; the building's level, its `BuildingPropertyData.m_ResidentialProperties` and `m_SpaceMultiplier`, and its lot size are that topic's, and they feed straight into the apartment happiness factor (`CitizenHappinessSystem.cs:472-484`). `HouseholdFlags.MovedIn` is the join.
- **`city-services-and-coverage`** (mechanics) supplies almost every happiness input: `NetUtils.GetServiceCoverage(serviceCoverage, CoverageService.X, curvePosition)` is called for healthcare, parks, education, welfare and telecom (`CitizenHappinessSystem.cs:410-429`), and garbage, crime and mail read producer components on the home building. That topic owns what those coverage numbers mean; this one owns what they do to a citizen. School capacity (`SchoolData.m_StudentCapacity`, upgrade-combined by the caller), building efficiency in the graduation probability (`GraduationSystem.cs:118`) and the prisoner pair on `Game.Buildings.Prison` are the same join in the other direction.

A sixth bridge is worth naming even though the ticket's list did not carry it: **`economy-and-companies`** is the other end of employment.
This topic produces a `Worker` with a level; that topic consumes it as an `Employee` and turns happiness into production at `EconomyUtils.cs:1480`. `FreeWorkplaces`, `WorkplaceData`, `CalculateNumberOfWorkplaces` and the wage debit from the company sit on the boundary and are cited here only as far as the citizen's side needs.
The slug is named in the topic's own boundary statement ("what a company does with the labour to `economy-and-companies`"), so it is not invented here — but it is absent from the ticket's bridge list, and that gap is flagged rather than closed.

---

## Dead ends

- **The mod corpus outside `Time2Work` and `InfoLoom`.** A grep for `Game.Citizens`, `CitizenHappinessSystem`, `HouseholdCitizen` and `Game.Simulation.WorkerSystem` across all 22 checked-out repositories returned hits in only five: `Time2Work`, `InfoLoom`, and then `Anarchy`, `CS2-Platter`, `ExtraDetailingTools` and `Recolor` — the last four only through bulldozer/selection filtering and component-inspection tooling, never touching a citizen mechanic. Matching on **Demonstrates** as the catalog directs, no entry other than the two above names a system in this area. Recorded rather than re-walked.
- **`resources.assets` for the balance numbers.** Not opened. `SOURCES.md` entry 5 records that base-game prefabs there carry no field names and need a parser driven by the decompiled field order. The running game answered every number this topic needed in single component reads, so the parser was never worth building. Anyone re-deriving these offline still faces that cost.
- **The compiled UI bundle.** Not read for this topic. The frontend surfaces citizen state through `Game.UI.InGame`'s C# systems (`CitizenSection`, `CitizenUIUtils`, `AverageHappinessSection`, `EducationInfoviewUISystem`, `CityInfoUISystem.cs:309` for the per-factor averages), all of which are in the decompile, and the topic's subject ships as C# rather than as data or JavaScript — so `SOURCES.md`'s precedence rule puts the decompile first and the bundle adds nothing here. A `citizens` reference that wanted to name the UI _strings_ would need the locale data (entry 4) and `method-decoding-shipped-locale-data.md`.
- **The `Adult`/`Child`/`Teen`/`Elderly` tag components as live state.** Walked and settled rather than left open: the grep over `src/Game/` found one consumer and no producer, and a live `ecs_query` returned zero against 6,454 adults. The finding is above. What dead-ends here is the assumption that a tag component declared in `Game.Citizens` means anything at runtime — some of them are load-time migration fossils.
- **The exact per-month school fee.** `ServiceFeeSystem.PayFee` charges `GetFee(resource, fees) / 128` per invocation at interval 2048 (`ServiceFeeSystem.cs:126`, `:350-353`), and turning that into a per-month figure comparable with the wiki's needs the fee system's own update-frame partitioning resolved against the day length. Not derived; the raw `m_Default` values are recorded instead, which is what a mod actually reads.
  **Moot under the ruling**, and recorded because the reason is instructive: the derivation existed only to compare a first-party figure against the wiki's, and no shipped prose now states either. What the reference owes here is the `/ 128` divisor and the interval, both of which are C#, so a reader can do this arithmetic against their own reading of `m_Default`.
- **Measuring the happiness random walk's settling time.** The ±1 step and the 1-in-256-frames-per-citizen rate are both established from source, but no experiment was run to see how many in-game days a population takes to converge after a service change. The method would be: read `CountHouseholdDataSystem.GetHouseholdCountData().m_TotalMovedInCitizenWellbeing` at intervals across an `advance` window while a service is toggled. Cheap, and worth running if the reference wants to state a settling time.
