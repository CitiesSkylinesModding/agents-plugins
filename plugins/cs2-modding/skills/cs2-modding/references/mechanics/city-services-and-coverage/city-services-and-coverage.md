# City services and coverage

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

A city service reaches citizens three ways, and every service in this topic uses one or more of them: **coverage** written onto road edges by a pathfind from the service building, **dispatch** of a vehicle to a request entity, and **per-building efficiency** effects on the buildings the service touches.
Electricity, water and sewage solve a flow graph instead and belong to `utilities-and-flow-networks`.
Coverage is a property of roads, not of area: the pathfind's result lands in a buffer on each reachable road edge, and a building or a citizen reads it back at its own position along its edge ([coverage.md](coverage.md)).
Dispatch is a uniform request/reconcile protocol — the request is an entity, the cheapest source is chosen by a pathfind, and failure backs off exponentially ([dispatch.md](dispatch.md)).
The budget slider does exactly two things — scales the money upkeep linearly and writes one efficiency factor — and everything a building can field flows from the efficiency product ([budget-workforce-and-upkeep.md](budget-workforce-and-upkeep.md)).
Fees are a buffer on the city entity, billed per occupant, with behavioural terms hand-written at two sites inside this topic ([fees.md](fees.md)).
Parks are the one service whose coverage is modified at runtime, telecom is the one that is a cell map rather than road coverage, and the leisure supply side lands with them ([parks-leisure-and-telecom.md](parks-leisure-and-telecom.md)).

## The map

Default reads: a parameter singleton is a `GetSingleton<T>` (`ecs-in-this-game` carries the call); a prefab component is reached from an instance through `PrefabRef.m_Prefab`, or enumerated with `ecs_query` on the component itself, since this game's prefab entities carry `PrefabData`; a row states its own shape only where it differs.

Coverage:

| The game models | Component | Access shape |
| --- | --- | --- |
| A building's coverage tuning | `Game.Prefabs.CoverageData { m_Service, m_Range, m_Capacity, m_Magnitude }`, added by the authoring class `Game.Prefabs.ServiceCoverage` (`src/Game/Game.Prefabs/ServiceCoverage.cs`) | prefab |
| Which service a covering building serves | `Game.Net.CoverageServiceType`, a **shared** component | instance; chunk-level, `eval`-only over the debugger |
| The search result | `Game.Pathfind.CoverageElement { m_Edge, m_Cost }` buffer | on the covering building, rebuilt each rotation |
| Stored coverage | `Game.Net.ServiceCoverage { float2 m_Coverage }` buffer, indexed by `(int)CoverageService` (`src/Game/Game.Net/CoverageService.cs`) | on a road edge that carries zone blocks; read `NetUtils.GetServiceCoverage(buffer, service, curvePos)` against `Building.m_RoadEdge` and `m_CurvePosition` |
| A park's runtime override | `Game.Buildings.ModifiedServiceCoverage` | instance, parks only ([parks-leisure-and-telecom.md](parks-leisure-and-telecom.md)) |
| District scoping | `Game.Areas.ServiceDistrict` buffer on the building; `Game.Areas.BorderDistrict` on the edge | empty buffer means "serves everywhere" ([dispatch.md](dispatch.md)) |
| Telecom coverage | `Game.Simulation.TelecomCoverage`, a cell map — belongs below, not to the edge buffer | a system-owned map, not a component: `TelecomCoverageSystem.GetData(readOnly, out deps)` returns the `CellMapData<TelecomCoverage>` that `TelecomCoverage.SampleNetworkQuality` samples ([parks-leisure-and-telecom.md](parks-leisure-and-telecom.md)) |

Dispatch — the same four components for every dispatched service:

| The game models | Component | Access shape |
| --- | --- | --- |
| A request | an entity with `Game.Simulation.ServiceRequest { m_FailCount, m_Cooldown, m_Flags }` plus one per-kind payload component named `<X>Request` | enumerate the kinds by listing `src/Game/Game.Simulation/*Request.cs`, minus `ServiceRequest.cs` and `HandleRequest.cs` themselves |
| Accepted work | `Game.Simulation.ServiceDispatch` buffer | on the dispatcher (station, depot, facility) |
| The chosen source | `Game.Simulation.Dispatched { m_Handler }` | on the request |
| A completion or failure report | `Game.Simulation.HandleRequest`, a one-shot entity created with the `Event` tag | reconciled at `ModificationEnd` ([dispatch.md](dispatch.md)) |
| Update spreading | `Game.Simulation.RequestGroup`, replaced by a random `UpdateFrame` shared component | write `RequestGroup` on creation, never `UpdateFrame` directly |

Budget, fees, efficiency, workforce:

| The game models | Component | Access shape |
| --- | --- | --- |
| A service's budget percentage | `Game.Simulation.ServiceBudgetData { m_Service, m_Budget }` buffer | on a plain singleton entity whose buffer holds only moved sliders — a missing entry reads as 100, a prefab outside the system's collected budget map as 0; `CityServiceBudgetSystem.GetServiceBudget(servicePrefab)` / `SetServiceBudget` are the read and write ([budget-workforce-and-upkeep.md](budget-workforce-and-upkeep.md)) |
| A service and its slider gate | `Game.Prefabs.ServiceData { m_Service, m_BudgetAdjustable }` on each `ServicePrefab` entity | `ecs_query` on the component |
| The fees | `Game.City.ServiceFee { m_Resource, m_Fee }` buffer, indexed by member scan | on the city entity (`CitySystem.City`); read `ServiceFeeSystem.GetFee(resource, fees)` ([fees.md](fees.md)) |
| Building efficiency | `Game.Buildings.Efficiency` buffer of `(EfficiencyFactor, value)` pairs | instance; a factor at exactly 1 is absent — read `BuildingUtils.GetEfficiencyFactor`, never presence ([budget-workforce-and-upkeep.md](budget-workforce-and-upkeep.md)) |
| Upkeep lines | `Game.Prefabs.ServiceUpkeepData { m_Upkeep, m_ScaleWithUsage }` buffer | prefab, folded over `InstalledUpgrade` |
| Usage for scaled upkeep | `Game.Buildings.ServiceUsage { m_Usage }` | instance; the upkeep fold multiplies `m_ScaleWithUsage` lines by it ([budget-workforce-and-upkeep.md](budget-workforce-and-upkeep.md)) |
| Workplaces | `Game.Prefabs.WorkplaceData { m_Complexity, m_MaxWorkers, … }`; `Game.Companies.WorkProvider`; `Game.Companies.Employee` buffer | prefab / instance / instance |

Per-service state — demand, dispatcher, and what failing looks like (for garbage, post and telecom, failing is a graded `EfficiencyFactor` written on the building that needed the service):

| Service | Demand carried on | Dispatcher state | Failure surface |
| --- | --- | --- | --- |
| Healthcare | `Game.Citizens.HealthProblem` with `HealthProblemFlags` (`Sick`, `Dead`, `RequireTransport`, …) — belongs to `citizens-and-households` | `Game.Buildings.Hospital { m_TargetRequest, m_Flags, m_TreatmentBonus, m_MinHealth, m_MaxHealth }`, `HospitalFlags` | a problem timer past `HealthcareParameterData.m_TransportWarningTime` raises the ambulance notification (`src/Game/Game.Simulation/HealthProblemSystem.cs`); a hospital without `HasRoomForPatients` takes a flat cost handicap in dispatch rather than dropping out (`src/Game/Game.Simulation/HealthcarePathfindSetup.cs`) |
| Deathcare | the same `HealthProblem` with `Dead` | `Game.Buildings.DeathcareFacility`, `DeathcareFacilityFlags { HasAvailableHearses, HasRoomForBodies, CanProcessCorpses, CanStoreCorpses, IsFull }` | `IsFull`; a corpse whose request keeps failing backs off and the body stays put |
| Garbage | `Game.Buildings.GarbageProducer { m_CollectionRequest, m_Garbage, m_Flags, m_DispatchIndex }` | `Game.Buildings.GarbageFacility`, `GarbageFacilityFlags` | an efficiency penalty on the **producing** building, `EfficiencyFactor.Garbage`, graded between the warning and max limits (the formula below) |
| Fire | no demand component; a `Game.Events.Fire` event against a hazard roll — the event belongs to `environment-and-pollution` | `Game.Buildings.FireStation`, `FireStationFlags` | `EfficiencyFactor.Fire = 0` while the fire's intensity is above 0.01 (`src/Game/Game.Simulation/FireSimulationSystem.cs`) — a burning building does nothing; coverage suppresses the hazard, not the fire ([coverage.md](coverage.md)) |
| Disaster control | `Game.Buildings.RescueTarget { m_Request }`, `EvacuationRequest` | `FireStationData.m_DisasterResponseCapacity` → `FireStationFlags.DisasterResponseAvailable` → engines flagged `FireEngineFlags.DisasterResponse`, matched against `FireRescueRequestType.Disaster` (`src/Game/Game.Simulation/FirePathfindSetup.cs`); `Game.Buildings.EmergencyShelter` + `EmergencyShelterFlags`; every building tagged `EarlyDisasterWarningSystem` gets `EarlyDisasterWarningDuration` when a warned event spawns (`src/Game/Game.Events/InitializeSystem.cs`) | no shelter space, no disaster-capable engine |
| Police | `Game.Buildings.CrimeProducer { m_PatrolRequest, m_Crime, m_DispatchIndex }` | `Game.Buildings.PoliceStation`, `PoliceStationFlags { HasAvailablePatrolCars, HasAvailablePoliceHelicopters, NeedPrisonerTransport }`; `Game.Buildings.Prison` | crime accumulates toward `PoliceConfigurationData.m_MaxCrimeAccumulation`; a patrol visit subtracts up to `PoliceCarData.m_CrimeReductionRate` (the formula below) |
| Administration | none | `Game.Buildings.AdminBuilding`, an empty tag | nothing — see the trap below |
| Education | `Game.Citizens.Student` on the citizen (belongs to `citizens-and-households`); `Game.Buildings.Student` buffer on the school | `Game.Buildings.School { m_AverageGraduationTime, m_AverageFailProbability, m_StudentWellbeing, m_StudentHealth }`, `SchoolData` | efficiency at or below 0.001 makes each student leave with probability `EducationParameterData.m_InoperableSchoolLeaveProbability`; otherwise efficiency enters the graduation roll directly (`src/Game/Game.Simulation/SchoolAISystem.cs`) |
| Post | `Game.Buildings.MailProducer { m_MailRequest, m_SendingMail, m_ReceivingMail, m_DispatchIndex, m_LastUpdateTotalMail }` — bit 15 of `m_ReceivingMail` is the `mailDelivered` flag, so read the `receivingMail` property, never the raw field | `Game.Buildings.PostFacility`, `PostFacilityFlags`; `Game.Routes.MailBox` | an efficiency penalty on the producing building, `EfficiencyFactor.Mail`, driven by `max(sending, receiving)` mail past `m_NegligibleMail` (`src/Game/Game.Simulation/MailAccumulationSystem.cs`) |
| Telecom | `Game.Buildings.TelecomConsumer` (empty tag) + `ConsumptionData.m_TelecomNeed` | `Game.Buildings.TelecomFacility`, `TelecomFacilityFlags { HasCoverage }` | `EfficiencyFactor.Telecom` penalty where sampled network quality is under `m_TelecomBaseline` ([parks-leisure-and-telecom.md](parks-leisure-and-telecom.md)) |
| Welfare | none of its own — welfare acts through coverage alone | `Game.Buildings.WelfareOffice` | there is no welfare parameter singleton — its happiness weight is `CitizenHappinessParameterData.m_WelfareMultiplier` and its crime-recurrence factor `PoliceConfigurationData.m_WelfareCrimeRecurrenceFactor` |
| Leisure and parks | `Game.Citizens.Leisure` (belongs to `citizens-and-households`); a park's maintenance demand is `Game.Simulation.MaintenanceConsumer` | `Game.Buildings.LeisureProvider` tag + `LeisureProviderData`; `Game.Buildings.MaintenanceDepot`, with its work kinds as `Game.Simulation.MaintenanceType` on `Game.Prefabs.MaintenanceDepotData` | leisure: none — no request kind, no `EfficiencyFactor` member, no notification; a failed trip stamps `Game.Citizens.LeisureSeekerCooldown` on the citizen and nothing counts the failures. A neglected park loses coverage magnitude and raises a `MaintenanceRequest` ([parks-leisure-and-telecom.md](parks-leisure-and-telecom.md)) |

Where the tuning numbers live, all singletons:

| Family of numbers | Component |
| --- | --- |
| The budget-efficiency curve, the garbage, mail, telecom, staffing and sick penalties, the grace period, plus the utility penalties `utilities-and-flow-networks` owns | `Game.Prefabs.BuildingEfficiencyParameterData` (`src/Game/Game.Prefabs/BuildingEfficiencyParameterData.cs`) |
| Per-fee `FeeParameters { m_Default, m_Max, m_Adjustable }`, one field per `PlayerResource` that has a fee, plus the two utility consumption curves | `Game.Prefabs.ServiceFeeParameterData` |
| Healthcare: transport warning time, death rates, notification prefabs | `Game.Prefabs.HealthcareParameterData` |
| Garbage: the accumulation limits and balances, homeless produce, happiness steps | `Game.Prefabs.GarbageParameterData` |
| Police: crime accumulation cap and tolerance, the coverage factor, welfare recurrence factor | `Game.Prefabs.PoliceConfigurationData` |
| Fire: structural integrity levels, response-time range and its darkness and telecom modifiers | `Game.Prefabs.FireConfigurationData` |
| Post: mail accumulation cap, tolerance, outgoing percentage | `Game.Prefabs.PostConfigurationData` |
| Education: the school-leave and enter probabilities | `Game.Prefabs.EducationParameterData` |
| Hiring notifications and their thresholds, the senior employee level | `Game.Prefabs.WorkProviderParameterData` |
| Disaster: flood damage, shelter exit probabilities, notification prefabs | `Game.Prefabs.DisasterConfigurationData` |
| Leisure and tourist consumption chances | `Game.Prefabs.LeisureParametersData` |
| One field each — the owning service prefab entity | `Game.Prefabs.TelecomParameterData`, `Game.Prefabs.ParkParameterData` |
| The coverage land-value multipliers (health, education, police, telecom) — the component belongs to `zoning-buildings-and-land-value` | `Game.Prefabs.LandValueParameterData` |
| The coverage happiness multipliers and `m_WelfareMultiplier` — the component belongs to `citizens-and-households` | `Game.Prefabs.CitizenHappinessParameterData` |

## Traps

**A field initializer on these parameter prefab classes is a Unity-serialized default the shipped asset overrides, and nothing in the C# marks which survived.**
`BuildingEfficiencyParametersPrefab`, `PoliceConfigurationPrefab`, `FireConfigurationPrefab` and their siblings declare initializers and copy the fields into their data components in `LateInitialize`; read live, some components match their initializers field for field while others differ wildly — the value is the asset's, so read the live singleton and never the class.
Source: `src/Game/Game.Prefabs/PoliceConfigurationPrefab.cs`, `src/Game/Game.Prefabs/BuildingEfficiencyParametersPrefab.cs`.

**Adding `CoverageData` to a prefab at runtime does not make its instances covering buildings.**
`ServiceCoverage.GetArchetypeComponents` is what puts `CoverageServiceType` and the `CoverageElement` buffer on the instance archetype, and the archetype is built at prefab initialization.
Source: `src/Game/Game.Prefabs/ServiceCoverage.cs`, `src/Game/Game.Net/CoverageServiceType.cs`.

**`ServiceCoverage.Initialize` assigns the service by testing the prefab's own service data component in a fixed order, and a prefab matching none silently becomes a Healthcare coverer.**
The no-match arm logs `"Unknown coverage service type: {0}"`, but the component write below the if/else chain still runs with `m_Service` at its default — `CoverageService.Healthcare`, the enum's first member — so the instances pathfind and write healthcare coverage.
Source: `src/Game/Game.Prefabs/ServiceCoverage.cs`, `src/Game/Game.Net/CoverageService.cs`.

**Which prefabs carry `CoverageData` at all is asset data, and parks dominate it.**
Enumerated live, parks outnumber every other coverage family put together in one install with DLC; the re-check is an `ecs_query` on `Game.Prefabs.CoverageData` joined per service data component.
Source: `src/Game/Game.Prefabs/CoverageData.cs`.

**District assignment and district policy are unrelated code paths.**
`ServiceDistrict` (a buffer on the service building) scopes who a station serves; `DistrictModifier` (a buffer on the district, applied through `AreaUtils.ApplyModifier`) scales how much work a district generates — garbage production, fire hazard, fire response time, crime accumulation — and neither reads the other.
Source: `src/Game/Game.Areas/AreaUtils.cs`, `src/Game/Game.Simulation/CrimeAccumulationSystem.cs`.

**`AdminBuilding` and `DisasterFacility` are empty tags the simulation never reads.**
A grep of `src/` returns only the notification-marker and object-colour systems (the info-view classification reads the prefab-side `*Data` twin), so there is no administration mechanic to find — an administration building is upkeep, workplaces and whatever `CityEffects` modifiers its prefab carries (`city-state-and-progression`) — and disaster response runs through the fire station's disaster capacity and the emergency shelter instead.
Source: `src/Game/Game.Buildings/AdminBuilding.cs`, `src/Game/Game.Buildings/DisasterFacility.cs`, `src/Game/Game.Prefabs/CityEffects.cs`.

**`CityService` has members no `ServicePrefab` instantiates.**
Read live, `Districts` and `Zones` back no service prefab, no budget line and no upkeep — they are toolbar categories; the re-check is an `ecs_query` on `Game.Prefabs.ServiceData` with `PrefabSystem.GetPrefabName` as the label.
Source: `src/Game/Game.City/CityService.cs`, `src/Game/Game.Prefabs/ServicePrefab.cs`.

## Formulas

The two expressions no sibling carries — every other transcription lives in the sibling its mechanism owns:

```
EfficiencyFactor.Garbage = 1 - m_GarbagePenalty
    * saturate((garbage - m_WarningGarbageLimit) / (m_MaxGarbageAccumulation - m_WarningGarbageLimit))
a patrol visit subtracts   min(PoliceCarData.m_CrimeReductionRate, m_Crime)
```

Source: `src/Game/Game.Simulation/GarbageAccumulationSystem.cs`, `src/Game/Game.Simulation/PoliceCarAISystem.cs`.

## Mechanisms

| Mechanism | Vanilla owner | Listing |
| --- | --- | --- |
| The coverage rotation, the pathfind, the capacity merge, district halving, who reads coverage | `Game.Simulation.ServiceCoverageSystem`, `CoverageJob` + `ProcessResultsJob` (`Game.Pathfind`), `NetUtils` | [coverage.md](coverage.md) |
| Requests, reconciliation, retry backoff, dispatch cost, imported services, fleet size | `ServiceRequestSystem`, the per-service `*DispatchSystem` + `*PathfindSetup` pairs, `SimulationUtils`, `BuildingUtils` | [dispatch.md](dispatch.md) |
| The budget's two effects, the efficiency product, workforce and the education kernel, upkeep | `CityServiceBudgetSystem`, `CityServiceEfficiencySystem`, `WorkProviderSystem`, `CityServiceUpkeepSystem`, `EconomyUtils` | [budget-workforce-and-upkeep.md](budget-workforce-and-upkeep.md) |
| Fee billing and the two behavioural fee terms | `ServiceFeeSystem`, `GraduationSystem`, `SicknessCheckSystem` | [fees.md](fees.md) |
| Park maintenance-scaled coverage, leisure supply, the telecom cell map | `ParkAISystem`, `TelecomCoverageSystem`, `TelecomEfficiencySystem` | [parks-leisure-and-telecom.md](parks-leisure-and-telecom.md) |

## Bridges

- `ecs-in-this-game` — every parameter row is its `GetSingleton<T>`; the `Efficiency` buffer is its "absence is a value" caution generalised; `CoverageServiceType` is the shared-component chunk filter, and `RequestGroup`'s one-time random draw is this topic's version of the sixteen-way `UpdateFrame` bucketing.
- `prefabs-and-assets` — making a building cover, dispatch or charge is adding a `ComponentBase` subclass whose `GetPrefabComponents`/`GetArchetypeComponents` split decides where the data lands; `IServiceUpgrade.GetUpgradeComponents` is the third arm, and `FireStation` refuses to add its runtime component to an upgrade that itself carries a `ServiceCoverage`; `UpgradeUtils.CombineStats` folds an installed upgrade's `ICombineData` stats into the parent's before use.
- `citizens-and-households` — owns the demand side: `HealthProblem`, `Game.Citizens.Student`, `Game.Citizens.Leisure`, household income, and `CitizenHappinessSystem`; the boundary is the `NetUtils.GetServiceCoverage` call — producing the number is this topic's, consuming it is theirs.
- `zoning-buildings-and-land-value` — owns `LandValueSystem` and the district-scoping mechanism (`AreaUtils.CheckServiceDistrict`) this topic's dispatch and coverage apply; this topic feeds it three coverage terms per road edge, and coverage enters where households choose to live through `PropertyUtils` ([coverage.md](coverage.md)).
- `transportation-and-vehicles` — owns every vehicle as an object: the physical and payload `*Data` components, the odometer-and-maintenance loop, the side-effect and energy-type data, and the transit fleet's own systems; the service-vehicle `*AISystem`s that drive this topic's dispatches and report back with `HandleRequest` are this topic's; `TransportDepot` is theirs even though it carries `ServiceDispatch` (and, as a taxi depot, `ServiceDistrict`).
- `city-state-and-progression` — owns `Locked`, `CityModifier` and `CityEffectProvider`; the coverage happiness bonuses return zero while the service prefab carries an enabled `Locked` ([coverage.md](coverage.md)).
- `utilities-and-flow-networks` — a service that solves a flow graph is theirs; a service that reaches by coverage or dispatched vehicle is this topic's; `ConsumptionData`, `ServiceUsage` and `BuildingEfficiencyParameterData` are shared surfaces both references map.
- `roads-and-traffic` — owns the substrate coverage is stored on and searched over: `Game.Net.Edge`, `Curve` and the pathfind graph; the `RoadPrefab` zone-block gate deciding which edges carry a coverage buffer at all is stated at [coverage.md](coverage.md).
- `environment-and-pollution` — owns the disaster and fire events this topic's dispatched response answers; the event is theirs, the response capacity, shelter and early warning are this topic's.
- `economy-and-companies` — owns the market prices, wages, the income/expense enumeration `CityServiceBudgetSystem` calls into, and the fee machinery's money half (`ServiceFeeSystem`'s billing, `UtilityFeeSystem`) — [fees.md](fees.md) maps the service side; the budget panel is a joint surface — the slider and upkeep are this topic's, the income and expense sources theirs.
- `simulation-time-and-units` — owns `TimeSystem.kTicksPerDay = 262144` and `SimulationUtils.GetUpdateFrame`, the substrate of every cadence here, the 256-frame coverage rotation included.
- `mod-lifecycle-and-ordering` — `ServiceRequestSystem`, `CityServiceEfficiencySystem` and `CityServiceBudgetSystem` run at `ModificationEnd` while the coverage, AI and dispatch systems run at `GameSimulation` (`src/Game/Game.Common/SystemOrder.cs`), so dispatch state read during simulation is always at least one reconciliation old; the phase nesting is that reference's.
- `save-serialization` — `ServiceRequest`, `Dispatched`, `ServiceBudgetData` and `ServiceFee` all carry hand-written version bands in their `Deserialize`, and `Game.Serialization.ServiceCoverageSystem` is a pure buffer-growth migration.
- `performance-and-memory` — the coverage rotation is the game's worked example of amortising a global pathfind ([coverage.md](coverage.md)).
- `diagnostics` — the printed surface is a scatter of one-line warnings, not a channel; the two worth knowing by name are the unknown-coverage-service error above and `PathfindSetupSystem`'s invalid-target warning (the line a custom dispatch kind's wrong `SetupTargetType` hits); a request that never gets served backs off silently, so there is no per-request log to find.
- `patching` — nothing here needs Harmony: the budget is a public getter/setter pair on a managed system, fees and coverage are ordinary buffers, and every dispatcher's state is an unmanaged component; the coverage pathfind is extended by giving a prefab `ServiceCoverage`, not by patching.
- `navigating-the-decompile` — the material splits across five namespaces by role: `Game.Prefabs` (authoring and data), `Game.Buildings` (instance state and efficiency), `Game.Simulation` (AI, dispatch, coverage), `Game.Pathfind` (the search), `Game.Net` (where the answer is stored); `Game.Simulation.ServiceCoverageSystem` and `Game.Serialization.ServiceCoverageSystem` share a short name and only the first simulates.

(VOLATILE: every component, field, enum member, system, method and `Source:` path this file names — their declarations under `src/Game/` in `Game.Prefabs`, `Game.Net`, `Game.Pathfind`, `Game.Simulation`, `Game.Buildings`, `Game.Companies`, `Game.Areas`, `Game.Citizens`, `Game.City`, `Game.Routes`, `Game.Vehicles`, `Game.Events`, `Game.Serialization`, `Game.Economy` and `Game.Common`, at the files the rows and traps cite; plus the live-read structural facts this file states — the coverage-prefab spread and the uninstantiated `CityService` members — against the running game's prefab set, re-derived by the query each states beside itself.)
