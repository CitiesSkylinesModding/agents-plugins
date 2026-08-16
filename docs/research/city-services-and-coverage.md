# City services and coverage

**Baseline.** Every claim was established against game version 1.6.0f1 (Unity 2022.3.71f1), assembly `VersionInternal("1.6.0f1 (419.d6c6) [6216.19404]")` at `src/Game/Properties/AssemblyInfo.cs:19`.
Decompiled C# citations are to a checkout regenerated from that install's managed assemblies, under `src/`, one directory per assembly.
Live values were read from the user's running city through the sibling Unity plugin on 2026-08-11; the simulation was **paused** (`SimulationSystem.selectedSpeed == 0`, frame 8,435,350) for the whole session — the dead ends record the consequences.
Wiki pages were fetched live on 2026-08-11 and rendered past the bot challenge.
The mod corpus at `C:\Users\Morgan\Documents\Projets\cs2-third-party-mods` was read on 2026-08-11, 22 repositories.
UI-bundle citations are to the reformatted copy at `DecompiledCitiesSkylines2/src-ui/source.js`, produced with prettier at its defaults, **135,021 lines** — the count matches this file's citations.
Installed-game citations are install-relative paths under `C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II`.

---

## Rulings that govern this whole file

**Ruled (2026-08-06, the citizens-and-households pass; conflicts.md).** A shipped reference states **no prefab value**. It names the component and the field, and that is the whole of what it says about the magnitude. Three things are untouched and are the spine of this topic:

- **C# constants ship, as numbers.** A `const` or `static readonly` compiled into the decompiled source is the operative value, offline-checkable and citable to a line. This topic is unusually rich in them: the coverage falloff exponent, the whole education-mix kernel, the fire-hazard and crime-coverage falloffs, the request backoff, the vehicle-capacity floor, and the update cadences are all C# and all ship.
- **Formulas ship whole.** The expression a system evaluates, its baseline and its step functions are invariant structure. Every coverage, efficiency, upkeep, dispatch-cost and land-value formula recorded below ships; the parameter values they read do not.
- **The map ships**, with the access shape beside each entry, because a reader cannot write the read from a field name alone. This topic has four distinct shapes: a parameter singleton (`GetSingleton<T>`, which `ecs-in-this-game` already carries), a per-building-prefab component behind a `PrefabRef` hop, a **buffer on the city entity** (`Game.City.ServiceFee`), and a **buffer indexed by an enum member** (`Game.Net.ServiceCoverage` on a road edge, indexed by `CoverageService`). A fifth is the one a reader will get wrong: `ServiceBudgetData` is a buffer on a plain singleton entity holding only moved sliders, so an empty buffer is a legitimate state and `CityServiceBudgetSystem.GetServiceBudget` supplies the 100 defaults (see the parameter map).

Two consequences bind particular passages here. **A derived ratio is a magnitude wearing a mechanism's clothes** — "a hospital reaches four times as far as a mailbox" is arithmetic over two prefab values and does not ship; the direction ("range is a per-prefab field, and the services differ widely in it") does. **A non-numeric prefab value is still a prefab value** — `ServiceData.m_BudgetAdjustable` and `FeeParameters.m_Adjustable` decide whether the player can move a slider at all, and they ship as the fields to check before building against a budget or a fee, never as a fact about any particular service.

**Ruled (2026-08-08, the zoning-buildings-and-land-value pass under delegated authority, with a same-day addendum; conflicts.md).** A **field initializer on a `PrefabBase`/`ComponentBase` subclass** is a Unity-serialized default the shipped asset overrides. It ships as **no number**. A reference whose map or traps send a reader into a file carrying them states once, **as a trap**, that these are Unity-serialized defaults the shipped asset overrides, with nothing in the C# marking which survived. The test is what consumes the value: a value the code reads from the class ships as a number; a value Unity deserialisation can replace is a prefab value whatever file declares it.

**This topic's parameter prefab classes do carry the shape** — see "The parameter prefabs' initializers are right until they are catastrophically wrong" below. `BuildingEfficiencyParametersPrefab`, `PoliceConfigurationPrefab`, `FireConfigurationPrefab`, `PostServiceConfigurationPrefab`, `WorkProviderParameterPrefab`, `LeisureParametersPrefab` and `DisasterConfigurationPrefab` all declare initializers and copy them into their data components in `LateInitialize`. Checked live, `BuildingEfficiencyParameterData`'s sixteen and `FireConfigurationData`'s eleven match their initializers exactly, and `PoliceConfigurationData` has two that do not — one off by a factor of 2,500.

---

## Findings

### Coverage is a pathfind, not a circle — and that is the whole shape of this topic

`Game.Simulation.ServiceCoverageSystem` (`src/Game/Game.Simulation/ServiceCoverageSystem.cs`) does not sample a radius. For each covering building it enqueues a **pathfind search** through `PathfindQueueSystem` (`:653-659`), and what comes back is a `DynamicBuffer<Game.Pathfind.CoverageElement>` on the building — one element per reachable **road edge**, carrying `Entity m_Edge` and a `float2 m_Cost` (`src/Game/Game.Pathfind/CoverageElement.cs:7-13`).

The value written per edge is (`ServiceCoverageSystem.cs:269-272`):

```
coverage = max(0, 1 - cost * cost) * CoverageData.m_Magnitude * buildingEfficiency
```

`cost` is a `float2` — one component per end of the edge — already normalised into `[0, 1]` by the pathfind (next finding). So coverage falls off **quadratically** from the building's own magnitude at cost 0 to exactly zero at cost 1. `buildingEfficiency` is `BuildingUtils.GetEfficiency(entity, ref m_Efficiencies)` taken from the **top-level owner**, found by walking `Owner` up until it stops resolving (`:212-218`). Where the covering object is a sub-object of something else, it is additionally forced to 0 when **the sub-object itself** carries `BuildingOption.Inactive` or `Destroyed` (`:219-222`) — so a switched-off wing of a hospital contributes nothing while the hospital's own efficiency still governs the rest.

The result lands in `DynamicBuffer<Game.Net.ServiceCoverage>` on the **road edge**, indexed by `(int)CoverageService` (`:268`, `src/Game/Game.Net/ServiceCoverage.cs:7-10`). A building reads its own coverage by sampling that buffer at its position along its road edge: `NetUtils.GetServiceCoverage(coverages, service, curvePos)` is `lerp(m_Coverage.x, m_Coverage.y, curvePos)` (`src/Game/Game.Net/NetUtils.cs:429-437`), against `Building.m_RoadEdge` and `Building.m_CurvePosition`.

**So coverage is a property of roads, not of area.** A building with no road edge has no coverage of any service, and land with no road on it has none either. `Game.Net.ServiceCoverage` is only added to a road prefab that carries a zone block (`src/Game/Game.Prefabs/RoadPrefab.cs:65-72`) — so highways and other unzonable nets carry no coverage buffer at all, and nothing on them can be covered. Live at 1.6.0f1 the city held **5,564 `Game.Net.Edge` entities and 676 of them carried `ServiceCoverage`** (two `ecs_query` calls).

`CoverageService` has eight members plus `Count` (`src/Game/Game.Net/CoverageService.cs:3-13`): `Healthcare, FireRescue, Police, Park, PostService, Education, EmergencyShelter, Welfare`.

*Rots:* `ServiceCoverageSystem`, `CoverageData`, `Game.Net.ServiceCoverage`, `Game.Pathfind.CoverageElement`, and the `CoverageService` member set and their order — the order is the buffer index and a reordering silently reassigns every stored value. Re-check at `src/Game/Game.Net/CoverageService.cs` and `src/Game/Game.Simulation/ServiceCoverageSystem.cs`.

### `m_Range` enters twice, and neither use is a radius

The search's only tuning input is `CoverageParameters { PathMethod m_Methods; float m_Range; }` (`src/Game/Game.Pathfind/CoverageParameters.cs`), built in `SetupCoverageSearchJob` from the **prefab's** `CoverageData.m_Range` (`ServiceCoverageSystem.cs:380-391`). `CoverageJobs` turns that one number into two bounds (`src/Game/Game.Pathfind/CoverageJobs.cs:94-95`):

```
m_MinDistance = m_Range * float4(0,   0.6, 0,   0.6)
m_MaxDistance = m_Range * float4(2,   1.2, 2,   1.2)
```

The `x`/`z` lanes are a **path cost** and the `y`/`w` lanes a **raw distance**. Cost accumulates as `PathUtils.CalculateCost(spec, coverageParameters, delta) = length * m_Density * |Δ|` (`src/Game/Game.Pathfind/PathUtils.cs:37-40`), where `PathSpecification.m_Density` is `sqrt(density)` on car lanes and the literal 1 on pedestrian lanes (`PathUtils.cs:934`, `:1281`), and distance as `length * |Δ|` (`CoverageJobs.cs:355`, `:367`) — so for the pedestrian services cost is distance itself. (`PathUtils.MIN_DENSITY` at `:26` is declared and referenced nowhere.) The search stops expanding a node once its distance reaches `m_MaxDistance.y` (`:162`, `:439`, `:454`), and every produced result is normalised and clamped (`:441`, `:445`; the mirrored backward case at `:456`, `:460`):

```
normalised = saturate(max((cost, distance) - m_MinDistance) / (m_MaxDistance - m_MinDistance))
```

So `m_Range` is **a cost budget of `2 × m_Range` and a distance budget of `1.2 × m_Range` with the first `0.6 × m_Range` free**, and whichever of the two lanes runs out first decides the result. Because the cost lane multiplies length by the graph's own density term, **a car-travelling service reaches further along a low-density stretch than a high-density one for the same metres**, while the distance lane caps the reach absolutely regardless of density. Both bound vectors are C# literals and ship; `m_Range` itself is prefab data and does not. (The `Game.Net.Density` component the capacity debit reads is a different value on a different entity; see the capacity finding.)

`ProcessResultsJob` (`CoverageJobs.cs:507`) collapses the per-lane results onto owners: for each covered lane it takes the minimum cost per owning edge, orients the `float2` to the edge's own direction (`GetCost`, `CoverageJobs.cs:572-576` — the select is per end, so an end whose lane does not span the edge reads `float.MaxValue` while the element is still added), then clears and rebuilds the building's `CoverageElement` buffer (`:555-567`).

**How the search travels is per service, and it is the sharpest split in the topic** (`ServiceCoverageSystem.SetupPathfindMethods`, `:665-700`):

- `PostService`, `Education`, `EmergencyShelter`, `Welfare` — `PathMethod.Pedestrian`, `RoadTypes.None`. These reach on foot only.
- `Park` — pedestrian too, plus a `SetupQueueTarget.m_ActivityMask` naming twelve `ActivityType` members (`BenchSitting`, `PullUps`, `Standing`, `GroundLaying`, `GroundSitting`, `PushUps`, `SitUps`, `JumpingJacks`, `JumpingLunges`, `Squats`, `Yoga`, `Reading`), so a park's coverage originates at its activity spots rather than at its road connection.
- everything else (`Healthcare`, `FireRescue`, `Police`) — `PathMethod.Road`, `RoadTypes.Car`.

The pathfind parameters are shared and are C# literals (`:628-635`): `m_MaxSpeed = 111.111115f` (400 km/h), `m_WalkSpeed = 5.555556f` (20 km/h), `m_Weights = PathfindWeights(1,1,1,1)`, `PathfindFlags.Stable | IgnoreFlow`, and an `m_IgnoredRules` mask covering blockage and every traffic-restriction rule.

**Verdict: the wiki's claim that the budget changes a service's coverage *range* does not hold at 1.6.0f1.** `Services` (https://cs2.paradoxwikis.com/Services, fetched 2026-08-11, banner "At least some were last verified for version 1.0", tagged "candidate for splitting" and "Potentially outdated") states: "Changing the budget for a service often has easily observed effects, like the number of vehicles a service building can field. Other effects may be less obvious, such as the range of the service coverage." The vehicle half is exactly right (see the vehicle-capacity finding). The range half is not: the only place efficiency enters coverage is the magnitude multiplier at `ServiceCoverageSystem.cs:269`, and the range the search runs on is read straight off the **prefab** at `:380-391` with no efficiency term anywhere in the path. What a player observes is the falloff curve scaling down, so the contour where coverage crosses a visible threshold moves inward — the reach does not. First-party wins by default and nothing here overturns it.

*Rots:* the two bound vectors, the per-service `PathMethod` split, and the park activity mask. Re-check at `src/Game/Game.Pathfind/CoverageJobs.cs:94-95` and `ServiceCoverageSystem.cs:665-700`.

### Coverage is rationed: one service per 32 frames, and a capacity that runs out

`ServiceCoverageSystem` is registered plainly into `SystemUpdatePhase.GameSimulation` (`src/Game/Game.Common/SystemOrder.cs:306`) with **no `GetUpdateInterval` override** — it runs every simulation frame and gates itself:

```
GetFrameService(frame) = (CoverageService)(frame % 256 * 8 / 256)      (:702-705)
public const uint COVERAGE_UPDATE_INTERVAL = 256u;                     (:472)
```

Each of the eight services therefore owns **32 consecutive frames of a 256-frame cycle**, and the whole rotation completes once per 256 frames. On the one frame where the service is about to change (`frameService != frameService2`, `:525-532`), the system does two things at once (`:533-614`):

1. **Enqueues the next service's searches.** Every building whose `CoverageServiceType` shared component matches the *next* service is queued with `m_QueueFrame = frameIndex + 192` and `m_ResultFrame = frameIndex + 256` (`:537-552`). `EnqueuePendingCoverages` submits **at least** `ceil(count / 192)` items per call and continues past that only for items whose `m_QueueFrame` has already arrived (`:625-648`) — so a city with many covering buildings spreads its searches across the slot rather than spiking on one frame. `m_ResultFrame` is a deadline rather than a delivery time — `PathfindResultSystem` consumes a coverage action the moment it completes (`src/Game/Game.Pathfind/PathfindResultSystem.cs:303-306`, the pending gate at `:331`) — so **the values a slot applies come from searches enqueued during the previous rotation, landing whenever the pathfinder finished them.**
2. **Applies the current service's results**, as a four-job pipeline: `ClearCoverageJob` zeroes that one index on every edge (`:29-50`), `PrepareCoverageJob` gathers the buildings and sizes the element list (`:93-141`), `ProcessCoverageJob` computes each element's coverage and sorts a building's elements descending by average coverage (`:197-286`), and `ApplyCoverageJob` writes them out (`:300-360`).

`ApplyCoverageJob` is the part nothing else in the game resembles, and it is what `CoverageData.m_Capacity` means. It is a global greedy merge: buildings are kept in a list sorted by their current best element, and on each step the globally-best remaining element is written, its building's remaining capacity is debited, and that building is bubbled back into place (`:320-358`). Two mechanisms sit inside it:

- **A building only ever raises an edge's coverage, never lowers it** (`:326`, `:334-336`): the write is `clamp(candidate - existing, 0, candidate * densityFactor)` added on top. Overlapping stations do not sum; the strongest wins per edge, per side.
- **Capacity decays the magnitude as it is spent** (`:328-333`):

  ```
  spent  = 1 - remaining / total
  factor = 1 - (0.99 * spent)^8
  written = candidate * factor
  ```

  `(0.99 * spent)` squared three times is the eighth power, all C# literals. So a building at 0% spent writes 100% of its magnitude, at 50% spent still ~99.6%, and only collapses near exhaustion — a hard knee rather than a slope.

  The debit is `mean(appliedFraction) * m_LengthFactor * m_DensityFactor` (`:338`), where `m_LengthFactor = edge.m_Length * sqrt(max(0.01, edge.m_Density))` (`:272`). **Capacity is therefore spent in units of road length weighted by the square root of density** — a service building covers a fixed amount of *populated road*, not a fixed number of citizens and not a fixed area.

A building drops out when its elements run out or its remaining capacity hits zero (`:311-315`, `:340-344`), and its remaining elements are simply never written.

*Rots:* `COVERAGE_UPDATE_INTERVAL`, the `% 256 * 8 / 256` slot arithmetic, the `+192`/`+256` queue frames, the 192-item batch, and the capacity exponent. Re-check at `ServiceCoverageSystem.cs:472/537-538/625-626/702-705` and `:328-338`.

### District membership is applied inside the coverage pass, and it halves a border road

`ProcessCoverageJob` reads the covering building's `DynamicBuffer<ServiceDistrict>` (from the top-level owner, `:213-217`) into a `NativeHashSet` and then, per candidate edge (`:242-261`):

- an edge whose `BorderDistrict.m_Left == m_Right` is **skipped entirely** unless that district is in the set;
- an edge straddling two districts is skipped unless at least one side is in the set, and otherwise gets `densityFactor = 0.5` when only one side matches and `1` when both do (`:259`).

`densityFactor` then caps the write and scales the capacity debit (`:335`, `:338`) — entering both, so **a station assigned to a district covers a border road at half strength for a quarter of the capacity spend** (`:335-338`). An empty `ServiceDistrict` buffer means "everywhere" — the check is skipped when `bufferData.Length == 0` (`:224`).

*Rots:* `BorderDistrict`, `ServiceDistrict`, and the 0.5 factor.

### Which coverage values anything reads — and two that are computed for nothing

Every consumer of `Game.Net.ServiceCoverage` at 1.6.0f1, from a grep of `src/Game/` for `CoverageService.` outside the declaring files and for the raw indexed reads:

| Service | Read by | What it does |
| --- | --- | --- |
| `Healthcare` (0) | `CitizenHappinessSystem.GetHealthcareBonuses` (`src/Game/Game.Simulation/CitizenHappinessSystem.cs:1176-1187`); `LandValueSystem` edge job index `[0]` (`src/Game/Game.Simulation/LandValueSystem.cs:186-187`) | health and wellbeing bonus; land-value bonus |
| `FireRescue` (1) | `EventHelpers.GetFireHazard` (`src/Game/Game.Simulation/EventHelpers.cs:59-68`) | `fireHazard *= max(0.01, 1 - coverage*0.01)` and `riskFactor = fireHazard / (1 + coverage*0.5)` |
| `Police` (2) | `CrimeAccumulationSystem` (`:107-111`); `CitizenPathfindSetup` homeless-shelter target cost (`:424-426`, `:604`); `LandValueSystem` index `[2]` (`:190-191`) | crime accumulation, shelter choice, land value |
| `Park` (3) | `CitizenHappinessSystem.GetEntertainmentBonuses` (`:1201-1214`) | wellbeing bonus, `min(1, sqrt(coverage/1.5))` shaped |
| `PostService` (4) | **nothing** | — |
| `Education` (5) | `CitizenHappinessSystem.GetEducationBonuses` (`:1189-1199`); `LandValueSystem` index `[5]` (`:188-189`) | wellbeing bonus scaled by `sqrt(n)`, where the caller's `n` is the household's size when the scored citizen is itself a child and 0 otherwise (the loop at `:309-316` tests the scored citizen's age, never the member's — `citizens-and-households` traps it); land value |
| `EmergencyShelter` (6) | **nothing** | — |
| `Welfare` (7) | `CitizenHappinessSystem.GetWellfareBonuses` / `GetWelfareValue` (`:1385-1396`); `CrimeCheckSystem` recurrence suppression (`src/Game/Game.Simulation/CrimeCheckSystem.cs:167-174`) | wellbeing bonus weighted by how unhappy the citizen already is; a repeat offender's crime is cancelled with probability `coverage * m_WelfareCrimeRecurrenceFactor` against a population-scaled roll (`CrimeCheckSystem.cs:159`, `:170`) |

**`PostService` and `EmergencyShelter` coverage is computed, stored, serialized, rendered in its info view — and read by no simulation system.** Four post prefabs and three shelter prefabs in this install carry `CoverageData`, each pays a full pathfind every 256 frames, and nothing consumes the answer. The post service works entirely through vans and mailboxes (see the per-service table) and the shelter through `EvacuationRequest`.

Three further shapes are worth stating because they are not obvious from the field names:

- **Only three of the eight services move land value**, and each takes the mean of the edge's two ends: `lerp(m_Coverage.x, m_Coverage.y, 0.5f)` times `LandValueParameterData.m_HealthCoverageBonusMultiplier` / `m_EducationCoverageBonusMultiplier` / `m_PoliceCoverageBonusMultiplier` (`LandValueSystem.cs:186-191`). Those three sum with three `ResourceAvailability` terms (commercial services, bus, tram/subway) into `LandValue.m_LandValue = max(0, sum)`, approached at 60% per update (`:206-213`). The **cell** map is a different formula and reads no service coverage at all — it averages nearby edges' land values and adds attractiveness, telecom quality and pollution terms (`:103-147`). `0.5f`, `0.6f`, `0.4f` and the `0.1f` change thresholds are C# literals.
- **Coverage feeds where households choose to live**, through `PropertyUtils.GetPropertyScore` / `GetApartmentQuality` / `GetGenericApartmentQuality`, which take the road edge's whole `ServiceCoverage` buffer plus the healthcare, park, education, telecom, garbage and police service prefabs (`src/Game/Game.Buildings/PropertyUtils.cs:434`, `:554`, `:620`, read at `:580`), called from `HouseholdFindPropertySystem` (`:193`, `:533`, `:602`).
- **The healthcare, education and entertainment bonuses are gated on progression, and the welfare pair is not.** `GetHealthcareBonuses`, `GetEducationBonuses` and `GetEntertainmentBonuses` each return `int2(0,0)` when `locked.HasEnabledComponent(serviceEntity)` (`CitizenHappinessSystem.cs:1178-1181`, `:1191-1194`, `:1203-1206`) — the service prefab entity carrying an enabled `Locked` tag — while `GetWellfareBonuses`/`GetWelfareValue` (`:1385-1393`) open straight on the coverage read. A city that has not unlocked one of the three gated services gets no wellbeing from it even if a mod places the building.

*Rots:* the whole table, every multiplier field name, and the three raw buffer indices in `LandValueSystem` — those are literal `[0]`, `[5]`, `[2]` rather than enum members, so a reorder of `CoverageService` breaks them silently.

### The coverage buffer has nine slots for eight services, and the ninth is the coverage preview's scratch slot

`Game.Net.InitializeSystem` resizes every new edge's `ServiceCoverage` buffer to **9** and zeroes it (`src/Game/Game.Net/InitializeSystem.cs:41-45`); `Game.Serialization.ServiceCoverageSystem` tops a loaded buffer up to 9 (`src/Game/Game.Serialization/ServiceCoverageSystem.cs:26-34`). `CoverageService.Count` is 8. The simulation's `ClearCoverageJob` and `ProcessCoverageJob` clear and write only indices `0..7` — and **`CoveragePreviewSystem` schedules those same jobs at index 8** to render a placement preview, seeding it from the active infoview's service (`src/Game/Game.Tools/CoveragePreviewSystem.cs:441-442/469/474`). The buffer serializes whole, so a save taken with a coverage infoview open persists preview residue there.

Read live at 1.6.0f1, two adjacent road edges both carried a non-zero index 8 tracking their index 3 (`Park`) values to four significant figures without matching them: edge 229397 `(14.142507, 14.148859)` against `(14.13948, 14.147692)`, and edge 229398 `(14.17175, 14.164035)` against `(14.171892, 14.164183)` — consistent with a park-coverage preview once left open in this save. What holds for a reader: the buffer is nine long, `CoverageService.Count` is eight, and a mod indexing it must use the enum member rather than assume `Length` is the service count. `NetUtils.GetServiceCoverage`'s own guard is `(int)service >= coverages.Length` (`src/Game/Game.Net/NetUtils.cs:431-434`), which the extra slot makes unreachable.

### A service building is a dispatcher, and the request is an entity

Range-and-coverage is only half the topic. The other half is a uniform request/dispatch protocol, and it is the same four components for every dispatched service:

- **`ServiceRequest`** (`src/Game/Game.Simulation/ServiceRequest.cs:6-13`) — `byte m_FailCount`, `byte m_Cooldown`, `ServiceRequestFlags m_Flags` (`Reversed = 1`, `SkipCooldown = 2`, `src/Game/Game.Simulation/ServiceRequestFlags.cs:5-10`). It sits beside a per-service payload component: `HealthcareRequest`, `FireRescueRequest`, `PolicePatrolRequest`, `PoliceEmergencyRequest`, `GarbageCollectionRequest`, `GarbageTransferRequest`, `MailTransferRequest`, `PostVanRequest`, `MaintenanceRequest`, `PrisonerTransportRequest`, `EvacuationRequest`, `TaxiRequest`, `TransportVehicleRequest`, `GoodsDeliveryRequest`, `RandomTrafficRequest`.
- **`ServiceDispatch`** (`src/Game/Game.Simulation/ServiceDispatch.cs:6-9`) — a `DynamicBuffer<Entity>` of accepted requests, on the **dispatcher** (station, depot, facility).
- **`Dispatched`** (`src/Game/Game.Simulation/Dispatched.cs:6-8`) — a single `m_Handler` entity, added to the **request** once a source is chosen.
- **`HandleRequest`** (`src/Game/Game.Simulation/HandleRequest.cs:5-13`) — `m_Request`, `m_Handler`, `m_Completed`, `m_PathConsumed`, a one-shot entity a vehicle or building creates to report back.

`ServiceRequestSystem` runs at `SystemUpdatePhase.ModificationEnd` (`SystemOrder.cs:270`) and is the only reconciler: it collapses every `HandleRequest` for one request — the first report is kept, a later `m_Completed` replaces it wholesale, a later `m_PathConsumed` merges only that flag, and any other later report is discarded (`src/Game/Game.Simulation/ServiceRequestSystem.cs:80-97`) — then destroys the request on completion, sets `Dispatched` on acceptance (zeroing the cooldown only when replacing an existing `Dispatched`), or — on `m_Handler == Entity.Null` — calls `SimulationUtils.ResetFailedRequest` and strips `PathInformation`, `PathElement` and `Dispatched` (`:99-139`).

**Retry is exponential backoff with a hard ceiling** (`src/Game/Game.Simulation/SimulationUtils.cs:168-186`):

```
ResetFailedRequest:  m_FailCount = min(255, m_FailCount + 1);  m_Cooldown = (1 << min(8, m_FailCount)) - 1
ResetReverseRequest: same, but m_Cooldown = max(4, ...)
TickServiceRequest:  ready = (m_Cooldown == 0) | SkipCooldown;  m_Cooldown = max(0, m_Cooldown - 1);  clear SkipCooldown
```

So a request that keeps failing waits 1, 3, 7, 15, 31, 63, 127, 255, 255 … update ticks between attempts. Every shift is a C# literal and the whole thing ships.

**Requests are spread across sixteen update groups by a random draw, once.** A newly created request carries `RequestGroup(groupCount)` (`src/Game/Game.Simulation/RequestGroup.cs:5-7`); `UpdateRequestGroupJob` replaces it with a `UpdateFrame(random.NextUInt(groupCount))` shared component and removes the tag (`ServiceRequestSystem.cs:32-44`). Each dispatch job then handles the group whose index matches this update's, and pre-schedules the group four slots ahead for path search — `uint nextUpdateFrameIndex = (num + 4) & mask` (`HealthcareDispatchSystem.cs:794-795`), 64 simulation frames of pathfinder headroom, wrapping to the same slot one cycle apart for a four-group kind like fire (`FireRescueDispatchSystem.cs:645-646`); `index == m_NextUpdateFrameIndex` starts the search, `index == m_UpdateFrameIndex` consumes the result (`HealthcareDispatchSystem.cs:156-201`). The group count is per request kind — `ParkAISystem` creates maintenance requests with `RequestGroup(32u)` (`src/Game/Game.Simulation/ParkAISystem.cs:113`).

*Rots:* every request component name and the flag enums. Re-check by listing `src/Game/Game.Simulation/*Request.cs` and `*DispatchSystem.cs`.

### The dispatch cost is a pathfind, and a station starts ten seconds behind a vehicle already out

A dispatch is a two-ended pathfind, not a distance sort. The request-side job builds a `SetupQueueItem(requestEntity, parameters, origin, destination)` and enqueues it (`src/Game/Game.Simulation/HealthcareDispatchSystem.cs:445-532`); the matching `*PathfindSetup` struct enumerates every eligible source and calls `targetSeeker.FindTargets(sourceEntity, startCost)`; the pathfinder returns one `PathInformation` naming the cheapest origin, and `DispatchVehicle` accepts it (`:433-443`).

Three things decide the cost, and all three are C# and ship:

- **The weights.** An ambulance dispatch runs `PathfindWeights(1f, 0f, 0f, 0f)` — time only, behaviour, money and comfort all zero — with `m_MaxSpeed = 277.77777f` (1,000 km/h) and methods `Pedestrian | Road | Flying | Boarding` (`HealthcareDispatchSystem.cs:473-481`). A **hearse** dispatch on the same system runs `PathfindWeights(1f, 1f, 1f, 1f)` at `m_MaxSpeed = 111.111115f` (`:503-511`). So the game distinguishes an emergency from a routine collection by the weight vector rather than by a priority number.
- **The starting handicap.** In `SetupAmbulancesJob`, a **hospital** as a source is seeded with `float cost = targetSeeker.m_PathfindParameters.m_Weights.time * 10f` (`src/Game/Game.Simulation/HealthcarePathfindSetup.cs:85-90`), while an **ambulance already returning to base** is seeded with `0f` (`:125`). A station therefore starts ten cost units — ten seconds of path time at the ambulance's time weight of 1 — behind any free vehicle already on the road, which is what makes the game re-task a returning vehicle in preference to sending a new one. `FirePathfindSetup`, `PolicePathfindSetup`, `GarbagePathfindSetup` and `PostServicePathfindSetup` follow the same shape.
- **The eligibility gates**, applied before the seeker is ever called: the station's own capability flags (`HospitalFlags.HasAvailableAmbulances` / `HasAvailableMedicalHelicopters` mapped onto `RoadTypes.Car` / `RoadTypes.Helicopter`, `:73-82`), the intersection with what the request will accept (`:82`), and `AreaUtils.CheckServiceDistrict` (`:71`).

**An outside connection is a source only when the city option is on.** Every setup job's first line is `if (chunk.Has<Game.Objects.OutsideConnection>() && !CityUtils.CheckOption(m_CityData[m_City], CityOption.ImportOutsideServices)) return;` (`HealthcarePathfindSetup.cs:55-58`). Read live, one such entity (index 84907) is a train outside connection carrying `Game.Buildings.Hospital`, `Game.Buildings.School`, `ServiceDispatch`, `ServiceDistrict` and `Game.City.ServiceFeeCollector` — the imported-services machinery is a normal service building wearing an `OutsideConnection` tag. The bill is per-population and is an expense line rather than a fee: `GetImportedAmbulanceServiceFee` and its four siblings compute `OutsideTradeParameterData.m_<X>ImportServiceFee * (population / m_OCServiceTradePopulationRange + 1) * m_OCServiceTradePopulationRange`, modified by `CityModifierType.CityServiceImportCost` (`src/Game/Game.Simulation/CityServiceBudgetSystem.cs:257-295`), against `ExpenseSource.ImportPoliceService`, `ImportAmbulanceService`, `ImportGarbageService`, `ImportHearseService` and `ImportFireEngineService` (`:169-198`).

*Rots:* the weight vectors, the `10f` handicap and the `SetupTargetType` member set (`src/Game/Game.Pathfind/SetupTargetType.cs:3-53`).

### The fleet is `efficiency × capacity`, floored at one

One helper connects the budget to everything a dispatcher can physically do (`src/Game/Game.Buildings/BuildingUtils.cs:376-379`):

```
GetVehicleCapacity(efficiency, capacity)
  = 0                                        when efficiency <= 0.001 or capacity <= 0
  = clamp((long)(efficiency * capacity), 1, capacity)   otherwise
```

**A working building always fields at least one vehicle, and a building at zero efficiency fields none.** `FireStationAISystem` calls it five times per station — with `min(efficiency, immediateEfficiency)` for engines, helicopters and disaster response (the counts parked availability is sized from), and with `immediateEfficiency` alone for the two counts that vehicles **already out** are tallied against, flagged `FireEngineFlags.Disabled` beyond them (`src/Game/Game.Simulation/FireStationAISystem.cs:175-186`, `:211-231`). Every other dispatcher AI system does the same with its own capacity fields.

`immediateEfficiency` is `BuildingUtils.GetImmediateEfficiency`, which multiplies **only** the factors `<= Disabled` plus `ServiceBudget` (`BuildingUtils.cs:152-168`, the filter at `:158`) — that is, `Destroyed`, `Abandoned`, `Disabled` and `ServiceBudget`. So a budget cut recalls vehicles already on the road at once, while a staffing or utility problem only shrinks what gets fielded next, through the slower full efficiency.

The vehicles themselves also get a work-rate term: `efficiency2 = FireStationData.m_VehicleEfficiency * (0.5f + efficiency * 0.5f)` (`FireStationAISystem.cs:186`), written to `FireEngine.m_Efficiency` (`:416`) and read as the engine's extinguishing rate (`FireEngineAISystem.cs:858-859`) — so a station at zero efficiency still hands its vehicles half of the prefab's vehicle efficiency. `0.5f` is a C# literal; `m_VehicleEfficiency` is prefab data.

*Rots:* `GetVehicleCapacity`, `GetImmediateEfficiency`'s factor filter, and each AI system's capacity field names.

### The budget slider does two independent things, and the efficiency curve has a floor and a cap

`ServiceBudgetData { Entity m_Service; int m_Budget; }` is a buffer on a singleton entity (`src/Game/Game.Simulation/ServiceBudgetData.cs:6-10`), one element per `ServicePrefab`. `m_Budget` is a **percentage**, and its absence defaults to 100 (`CityServiceBudgetSystem.cs:428-438`, `src/Game/Game.Buildings/CityServiceEfficiencySystem.cs:97-107`).

**Effect one: money spent scales linearly.** In `CityServiceBudgetJob`, a `Resource.Money` upkeep line becomes (`CityServiceBudgetSystem.cs:399-421`):

```
value = amount * marketPrice(resource)
value = ApplyModifier(value, CityModifierType.CityServiceBuildingBaseUpkeepCost)
value += GetUpkeepOfEmployeeWage(...)
value *= 0.1                      if the building is Inactive
cost   = value * (budget / 100)
```

and both `m_Cost` (budget-scaled) and `m_FullCost` (unscaled) are accumulated onto the service's `CollectedCityServiceUpkeepData` (`:415-421`). Non-money upkeep lines are *not* budget-scaled.

**Effect two: every building of that service takes an efficiency factor.** `CityServiceEfficiencySystem` (`src/Game/Game.Buildings/CityServiceEfficiencySystem.cs`) runs at `SystemUpdatePhase.ModificationEnd` (`SystemOrder.cs:292`) and writes (`:53-66`):

```
efficiency = BuildingEfficiencyParameterData.m_ServiceBudgetEfficiencyFactor.Evaluate(budget / 100f)   if the prefab, or any installed upgrade, has a Money upkeep line
           = 1                                                                                        otherwise
→ EfficiencyFactor.ServiceBudget
```

**A service building with no money upkeep is immune to its own budget slider**, and `HasMoneyUpkeep` is the test (`:70-83`). The system re-runs over *every* building when a `ServiceBudgetData` changes and over only the created/updated ones otherwise (`:203`).

**The curve's shape, read live at 1.6.0f1** from the `BuildingEfficiencyParameterData` singleton (entity 63:1) by evaluating `m_ServiceBudgetEfficiencyFactor`:

| budget | 0% | 25% | 50% | 75% | 100% | 125% | 150% | 200% |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| factor | 0.25 | 0.25 | 0.25 | 0.712 | 1.000 | 1.170 | 1.25 | 1.25 |

The curve's own fields read `m_MinTime = 0.5`, `length = 1.5`, `m_LengthFactor = 30` — a domain of exactly `[0.5, 1.5]`, clamped outside. **So the shape is a flat floor below half budget, a steep rise across the 50–100% band, and a shallow, capped gain above 100%** — the reference states that shape, and the numbers are prefab data that does not ship.

**The slider's own bounds are frontend literals and do ship.** `budget-slider-item.tsx` renders `min: 50, max: 150` (`DecompiledCitiesSkylines2/src-ui/source.js:107294-107305`), which matches the curve's domain exactly — the player cannot reach the clamped tails. `ServiceBudgetUISystem` writes the panel and takes the trigger (`src/Game/Game.UI.InGame/ServiceBudgetUISystem.cs:111-115`, the detail writer at `:160-192`, `SetServiceBudget` at `:251-255`), and `CityServiceBudgetSystem.SetServiceBudget` applies **no clamp at all** (`:1084-1102`) — a mod may set any integer.

**Not every service has a slider.** `ServicePrefab.m_BudgetAdjustable` defaults to `true` (`src/Game/Game.Prefabs/ServicePrefab.cs:19-20`) and lands on `ServiceData.m_BudgetAdjustable` (`src/Game/Game.Prefabs/ServiceData.cs:6-11`), which the panel gates the slider on (`source.js:107442-107447`). Read live at 1.6.0f1, **twelve `ServicePrefab` entities exist and eleven are adjustable — `Landscaping` is not**; the twelve are `Communications`, `Education & Research`, `Electricity`, `Fire & Rescue`, `Garbage Management`, `Health & Deathcare`, `Landscaping`, `Parks & Recreation`, `Police & Administration`, `Roads`, `Transportation`, `Water & Sewage`. The `CityService` enum has fifteen members including `Count` (`src/Game/Game.City/CityService.cs:3-19`), so **`Districts` and `Zones` are the two enum members with no service prefab, no budget line and no upkeep** — they are toolbar categories.

**Ruled (2026-08-11, the utilities-and-flow-networks pass under the maintainer's delegated authority; conflicts.md).** A structural fact read off prefab values is a swept set's shape and ships the way [ADR 0006](../adr/0006-a-set-ships-where-the-game-declares-it.md) ships one: as the shape, with the derivation and the query that reproduces it attached, never as a bare fact the plugin vouches for. The C# mechanism beside it ships flat, as it always did.

The reference states the mechanism flat — `ServiceData.m_BudgetAdjustable` gates the slider, and `CityService` carries members that no `ServicePrefab` instantiates — and states the census as what the enumeration shows, naming the `ecs_query` on `Game.Prefabs.ServiceData` plus a `PrefabSystem.GetPrefabName` label as the check, with `Landscaping` as the one cited worked example. No roster of the twelve, and no flat "eleven services are adjustable" without its derivation clause. The same applies to the coverage-prefab census below.

*Rots:* `ServiceBudgetData`, `ServiceData`, `m_ServiceBudgetEfficiencyFactor`, the slider bounds (re-check `source.js` against the line count in this file's baseline), and the twelve-prefab count.

### Efficiency is a product of up to 32 factors, unclamped above 1, rounded to a percent

`EfficiencyFactor` has 32 members plus `Count` (`src/Game/Game.Buildings/EfficiencyFactor.cs:3-37`), and `Efficiency` is a `DynamicBuffer` of `(factor, value)` pairs with `InternalBufferCapacity(8)` (`src/Game/Game.Buildings/Efficiency.cs:7-12`). The rules are all in `BuildingUtils`:

```
GetEfficiency(buffer)  = product of max(0, each value); 0 if the product is <= 0,
                         else max(0.01, round(100 * product) / 100)     (:121-133)
SetEfficiencyFactor    = writes the entry, or REMOVES it when |value - 1| <= 0.001  (:207-228)
GetEfficiencyExcludingFactor / GetImmediateEfficiency                    (:135-168)
```

Three consequences a reader will get wrong otherwise:

- **A factor at exactly 1 is not in the buffer.** A query for a building's `ServiceBudget` factor returns nothing at 100% budget, because `SetEfficiencyFactor` deleted it. `GetEfficiencyFactor` returns `1f` for a missing entry (`:249-259`), which is the correct read.
- **Efficiency is not capped at 1.** Read live at 1.6.0f1, hospital 261319 carried exactly two entries — `NotEnoughEmployees = 0.9944` and `EmployeeHappiness = 1.1634` — for a product of 1.157 and a rounded efficiency of 1.16. `EmployeeHappiness` is `currentWorkforce / averageWorkforce + workConditions * 0.01f` (`src/Game/Game.Simulation/WorkProviderSystem.cs:269-270`), unbounded above. **A well-staffed, happy service building runs above 100% and its vehicle count rounds up accordingly.**
- **The floor is 0.01 unless some factor is exactly zero**, in which case the whole product is 0 (`:128-132`) — which is why `EfficiencyFactor.Destroyed`/`Abandoned`/`Disabled`/`Fire` are written as literal `0f` (`src/Game/Game.Buildings/BuildingStateEfficiencySystem.cs:39-41`, `src/Game/Game.Simulation/FireSimulationSystem.cs:237`).

`ApproximateEfficiencyFactors` is the inverse operation, splitting one target efficiency back across two or four weighted factors — the `float2` form solves a quadratic in closed form (`BuildingUtils.cs:230-247`) and the `float4` form runs a **fixed 16-step bisection** (`:261-286`). Both are pure C# and ship.

*Rots:* the `EfficiencyFactor` member set and its order (the order is the `SetEfficiencyFactors` array index at `:195-205`).

### Workforce, and where the education requirement actually comes from

A city service building's workplaces come from `Game.Prefabs.Workplace` → `WorkplaceData { WorkplaceComplexity m_Complexity; int m_MaxWorkers; float m_EveningShiftProbability; float m_NightShiftProbability; int m_MinimumWorkersLimit; int m_WorkConditions; }` (`src/Game/Game.Prefabs/Workplace.cs:14-65`, `src/Game/Game.Prefabs/WorkplaceData.cs:7-19`), with `WorkplaceComplexity` = `Manual, Simple, Complex, Hitech` (`src/Game/Game.Prefabs/WorkplaceComplexity.cs:3-8`). `CityServiceWorkplaceInitializeSystem` adds or removes `WorkProvider` and keeps `m_MaxWorkers` in step with `CityUtils.GetCityServiceWorkplaceMaxWorkers` (`src/Game/Game.Buildings/CityServiceWorkplaceInitializeSystem.cs:102-120`); the add path assigns `m_EfficiencyCooldown = -m_ServiceBuildingEfficiencyGracePeriod` (`:119`) while the update path `+=`s the same negative (`:111`), so repeated workplace changes drive the cooldown further negative rather than re-arming it.

**The education requirement is a triangular kernel and it is entirely C#** (`src/Game/Game.Economy/EconomyUtils.cs:1370-1400`):

```
CalculateNumberOfWorkplaces(totalWorkers, complexity, buildingLevel):
  centre = 4 * (int)complexity + buildingLevel - 1
  for each education level i in 0..4:
     weight = max(0, 8 - |centre - 4i|)
     weight += max(0, 8 - |centre + 4|)   at i == 0     // the uneducated end absorbs the tail
     weight += max(0, 8 - |centre - 20|)  at i == 4     // the highly-educated end absorbs the other
     workplaces[i] = totalWorkers * weight / 16, with the integer remainder carried forward
```

So complexity slides a width-16 triangle across the five education levels in steps of four, and building level nudges it by one. A `Manual` level-1 workplace centres at 3 and a `Hitech` one at 15. **For a city service building the level is always 1**, because the caller resolves it through `PropertyUtils.GetBuildingLevel`, which returns `SpawnableBuildingData.m_Level` or the literal `1` when the prefab carries no such component (`src/Game/Game.Buildings/PropertyUtils.cs:685-692`), and a service building is not spawnable — so **complexity alone decides a service building's education mix** (`WorkProviderSystem.cs:176-177`, `WorkProviderStatisticsSystem.cs:59`, `WorkplacesInfoviewUISystem.cs:82`). The wiki's `Service buildings` stat column "Max. needed education" is this kernel's top occupied level.

**Staffing feeds efficiency through three factors at once** (`WorkProviderSystem.cs:252-283`):

```
averageWorkforce = Σ workplaces[i] * GetWorkerWorkforce(50, i)          // the ideal
CalculateCurrentWorkforce(...) → currentWorkforce, averageWorkforce2, sickWorkforce
missing = averageWorkforce - averageWorkforce2 - sickWorkforce
missing *= saturate(m_EfficiencyCooldown / m_MissingEmployeesEfficiencyDelay)   // a ramp, not a step
missing *= m_MissingEmployeesEfficiencyPenalty
sick    *= m_SickEmployeesEfficiencyPenalty
(NotEnoughEmployees, SickEmployees) = ApproximateEfficiencyFactors((average - missing - sick) / average,
                                                                   float2(missing, sick))
EmployeeHappiness = currentWorkforce / averageWorkforce2 + workConditions * 0.01
```

with `GetWorkerWorkforce(happiness, level) = ((level == 0 ? 2 : 1) + 2.5 * level) * (0.75 + happiness / 200)` (`EconomyUtils.cs:1447-1450`) — a pure C# formula that ships whole. **A vacancy costs nothing immediately**: `m_EfficiencyCooldown` climbs one per update while under-staffed (`WorkProviderSystem.cs:262`, `:285-298`) and the penalty ramps in over `m_MissingEmployeesEfficiencyDelay` updates, having started at `-m_ServiceBuildingEfficiencyGracePeriod`.

The two hiring notifications are separate from the efficiency and use their own thresholds: uneducated vacancies over `m_UneducatedNotificationLimit` of the uneducated+poorly-educated slots, and educated vacancies over `m_EducatedNotificationLimit` of `educated + 2*wellEducated + 2*highlyEducated` (`:234-251`) — note the weighting of two for the top two levels.

**The two branches are not written the same way, and the educated one is an all-or-nothing gate.** The uneducated test is `(float)num2 / (float)num >= limit` (`:239`); the educated test one screen later is `(float)(num4 / num3) >= limit` (`:248`), with both operands `int` and the cast to `float` after the truncation. Free slots never exceed max slots, so the quotient is exactly 0 for any partial vacancy and exactly 1 only when every weighted educated slot is free — against a limit at or below 1 the notification fires only on a fully vacant educated workforce, a gate rather than the threshold the uneducated branch computes. That is settled from the source and needs no experiment.

**Ruled (2026-08-11, the city-services-and-coverage pass under the maintainer's delegated authority; conflicts.md).** Where the reference covers this notification at all, it states the behaviour flat and cited to the line — the quotient is integer division, 0 on any partial vacancy and 1 only on a fully vacant weighted workforce, an all-or-nothing gate — and may state the arithmetic that produces it. No word about intent ships, and no marker on intent, since no experiment can close it. Whether the notification earns a place at all is the reference's ordinary depth call, not the ruling's.

*Rots:* `WorkplaceData`, `WorkplaceComplexity`, `WorkProvider`, `Workplaces`, and the kernel's constants.

### Upkeep: money, resources, wages, and the one field that scales with use

`ServiceUpkeepData { ResourceStack m_Upkeep; bool m_ScaleWithUsage; }` is a buffer on the building **prefab** (`src/Game/Game.Prefabs/ServiceUpkeepData.cs:6-10`), and `CityServiceUpkeepSystem.GetUpkeepWithUsageScale` gathers the prefab's lines plus every installed upgrade's, applying `ApplyServiceUsage(m_Usage)` — a straight multiply of the amount — to each line flagged `m_ScaleWithUsage` (`src/Game/Game.Simulation/CityServiceUpkeepSystem.cs:645-661`, the multiply at `ServiceUpkeepData.cs:26-37`).

`Game.Buildings.ServiceUsage.m_Usage` is a single float (`src/Game/Game.Buildings/ServiceUsage.cs:6-8`) and is written by five AI systems: `HospitalAISystem` as `patients / max(1, m_PatientCapacity)` (`src/Game/Game.Simulation/HospitalAISystem.cs:419`, zeroed at `:423`), `SchoolAISystem` as `students / m_StudentCapacity` (`src/Game/Game.Simulation/SchoolAISystem.cs:151`), `EmergencyShelterAISystem` (`:218`), and — outside this topic — `PowerPlantAISystem` (`:166`, `:179`) and `BatteryAISystem` (`:151`); plus a load-time back-fill seeding `m_Usage = 1f` on budget-bearing buildings that lack the component (`src/Game/Game.Serialization/RequiredComponentSystem.cs:1143`, query at `:456`), which is where a school's component comes from, since `School` does not add it to its archetype. **Every other service building's upkeep is flat.**

Wages are a separate, un-prefabbed line: `GetUpkeepOfEmployeeWage` sums `EconomyParameterData.GetWage(employee.m_Level, cityServiceJob: true)` over the building's `Employee` buffer and returns 0 for an inactive building (`CityServiceUpkeepSystem.cs:628-643`). It is added to the money line **before** the budget multiplier (`CityServiceBudgetSystem.cs:408-413`), so **the budget slider cuts the wage bill as well as the maintenance bill**.

`CityServiceUpkeepSystem` runs at `GameSimulation` (`SystemOrder.cs:493`) on an interval of `262144 / (kUpdatesPerDay * 16)` with `private static readonly int kUpdatesPerDay = 64` (`CityServiceUpkeepSystem.cs:468`, `:494`) — 256 frames, sixteen groups, 64 passes per in-game day; it consumes upkeep resources and enqueues purchases, while the money arithmetic runs in `CityServiceBudgetSystem` at `ModificationEnd` and lands on the treasury through `BudgetApplySystem` (`kUpdatesPerDay = 1024`, `BudgetApplySystem.cs:57-58`).

*Rots:* `ServiceUpkeepData`, `ServiceUsage`, `CollectedCityServiceUpkeepData`, and `kUpdatesPerDay`.

### Fee elasticity runs in two directions, and inside this topic it reaches two services

`ServiceFee { PlayerResource m_Resource; float m_Fee; }` is a buffer on the **city entity** (`src/Game/Game.City/ServiceFee.cs:6-10`), read through the static `ServiceFeeSystem.GetFee(resource, fees)` (`src/Game/Game.Simulation/ServiceFeeSystem.cs:480-491`). `PlayerResource` has thirteen members (`src/Game/Game.City/PlayerResource.cs:6-21`) and `ServiceFeeParameterData` declares nine `FeeParameters { m_Default, m_Max, m_Adjustable }` (`src/Game/Game.Prefabs/ServiceFeeParameterData.cs:8-47`).

**Which fees the player can move at all is a prefab bool, and at 1.6.0f1 it is two of nine.** Read live from the `ServiceFeeParameterData` singleton (entity 50:1): `m_ElectricityFee` and `m_WaterFee` carry `m_Adjustable = True`; `m_HealthcareFee`, `m_BasicEducationFee`, `m_SecondaryEducationFee`, `m_HigherEducationFee`, `m_GarbageFee`, `m_FireResponseFee` and `m_PoliceFee` all carry `False`. The panel filters on it (`source.js:107448-107456`) so the other seven have no slider. Both adjustable ones belong to `utilities-and-flow-networks`; **this topic's fees are all locked at 1.6.0f1**, which is the fact a reader most needs and which no wiki page states.

**Direction one — the fee is charged.** `ServiceFeeSystem.PayFeeJob` (`:63`) walks each `Game.City.ServiceFeeCollector` building's `Patient` and `Student` buffers (query at `:363-381`) and bills the occupant's household `GetFee(resource, fees) / 128f` per update, queueing a `FeeEvent` (`ServiceFeeSystem.cs:89-140`, the divisor — the literal `128f` — at `:126`; `kUpdatesPerDay = 128` is declared at `:330` and referenced nowhere). The system's interval is 2048 frames (`:350-352`), so the divisor converts a per-day price into a per-charge amount. The debit is the rounded integer, `(int)(0f - math.round(num))` (`:126-131`), while the income side books the unrounded `FeeEvent.m_Cost` times charges (`FeeToCityJob` re-multiplies by `128f` at `:196`; `GetServiceFeeIncomeEstimate` at `:565-576` is charges times the nominal fee) — so a fee at or below 64 debits the household nothing and still books income. Utility and garbage fees are billed elsewhere, by `UtilityFeeSystem` (`src/Game/Game.Simulation/UtilityFeeSystem.cs:70-71`, `:217`).

**Direction two — the fee changes behaviour, at four hand-written sites and nowhere else.** The generic dispatcher `ServiceFeeSystem.GetConsumptionMultiplier` / `GetEfficiencyMultiplier` / `GetHappinessEffect` returns `1f`/`1f`/`1` for every resource except Electricity and Water (`ServiceFeeSystem.cs:522-550`), and those three helpers exist only to feed the budget panel's readout — no simulation system calls them. **Inside this topic only two services have a behavioural fee term at all, education and healthcare**, and each is written at its own site:

- **Education.** `GraduationSystem.GetDropoutProbability(level, lastCommuteTime, fee, …)` takes the fee directly, called from `SchoolAISystem` for the panel's projection (`src/Game/Game.Simulation/SchoolAISystem.cs:95`, `:118`) and from `GraduationSystem` for the real roll, where the per-check probability is raised to `1 - (1 - p)^32` (`src/Game/Game.Simulation/GraduationSystem.cs:143-146`). **A higher education fee makes students drop out.**
- **Healthcare.** `SicknessCheckSystem.CreateHealthEvent` computes the chance a new patient refuses care (`src/Game/Game.Simulation/SicknessCheckSystem.cs:158-168`):

  ```
  fee    = GetFee(PlayerResource.Healthcare, fees)
  income = EconomyUtils.GetHouseholdIncome(...)             // money per day, typically thousands
  p(NoHealthcare) = 10f / citizen.m_Health - fee / 2f * income
  ```

  `HealthProblemFlags.NoHealthcare` then makes `HealthProblemSystem` skip the treatment path entirely (`src/Game/Game.Simulation/HealthProblemSystem.cs:389`, `:453`).

  **Verdict: the healthcare fee's elasticity term is inverted and, at any positive fee at the defaults' scale, degenerate for any earning household.** The decompile is the authority here and there is nothing to overturn: `fee / 2f * (float)num2` is `(fee/2) × income`, so the term is subtracted rather than added, and with the live healthcare fee of 100 and any working household the product is in the tens of thousands — `p` is hugely negative and `NoHealthcare` is never set. At a fee of exactly 0, **or for a household with no income** (the term is then zero at any fee), the expression reduces to `10 / health`, which for a citizen at health 50 is a 20% refusal rate. So **lowering the healthcare fee to zero makes earning citizens refuse healthcare, and raising it stops them refusing.** Three scope facts: a negative fee flips the term positive — `SetFee` applies no clamp and no sign check (`ServiceFeeSystem.cs:508-520`), so from a mod every earning household then refuses; `CreateHealthEvent` returns before the roll entirely for a health-event prefab with `HealthEventData.m_RequireTracking`, the authoring class's default (`src/Game/Game.Prefabs/HealthEvent.cs:20`; the early return at `SicknessCheckSystem.cs:133-139`), so the fee governs only untracked events; and the suppression is the inequality `fee / 2 × income > 10 / health` — a positive fee suppresses only once it clears that bound, which at real incomes anything beyond a sliver does, so the edge is writable from a mod and unreachable from the slider.

  **Ruled (2026-08-11, the city-services-and-coverage pass under the maintainer's delegated authority; conflicts.md).** The reference states this behaviour flat, oriented so the surprise leads — for a household with any income a positive fee suppresses the flag entirely, and only a zero fee or a zero-income household produces refusals — and it may state the arithmetic that produces it (the term is subtracted and scales with household income), because the arithmetic is a fact at the cited line and it is what stops a reader compensating for a mechanic that does not exist. No word about intent ships — not *bug*, not *defect*, not *apparently meant* — and no `UNVERIFIED:` marker on intent, since no experiment can close it. The trap form is the intended vehicle: the stated direction is its own warning.
- **Fire response and police have no fee at all, in either direction.** `PlayerResource.FireResponse` and `PlayerResource.Police` appear in `ServiceFeeParameterData`, in `GetDefaultFees()` and in one `switch` arm of `SendTradeResourceTrigger` (`ServiceFeeSystem.cs:271`, `:274`), and **nowhere else in `src/Game/`** (a grep over the whole assembly). Both carry `m_Default = 0` and `m_Adjustable = False` live. They are declared, defaulted, serialized, and neither charged nor read. Garbage is the third of this topic's declared fees, and its only site is billing — `UtilityFeeSystem` (`src/Game/Game.Simulation/UtilityFeeSystem.cs:217`, `:277`) — with no behavioural term anywhere.

**Verdict: the game carries two disagreeing sets of default fees, and the prefab's is the operative one.** `ServiceFee.GetDefaultFee` is a C# switch returning `BasicEducation 100, SecondaryEducation 200, HigherEducation 300, Healthcare 100, Garbage 0.1, Electricity 0.2, Water 0.1` (`src/Game/Game.City/ServiceFee.cs:44-57`). Live, the `ServiceFeeParameterData` singleton's `m_Default` fields read `25 / 50 / 100 / 100 / 0.1 / 0.2 / 0.5` — **four of the seven differ**: all three education tiers by a factor of four, and water by a factor of five in the other direction. What a new city gets is the prefab's set: `CitySystem` fills the buffer from `singleton.GetDefaultFees()` on creation (`src/Game/Game.Simulation/CitySystem.cs:97-102`) and `RequiredComponentSystem` does the same for a save that lacks the buffer (`src/Game/Game.Serialization/RequiredComponentSystem.cs:807-820`). The C# literals are reached only from `ServiceFee.Deserialize` under `Purpose.NewGame` (`:22-29`) and from a back-fill that adds a missing water fee (`RequiredComponentSystem.cs:821-839`). First-party beats first-party by ownership, and the prefab owns the value.

**And the live save agrees with neither.** Read from the city entity (364051:1) at 1.6.0f1: `Healthcare 100, Electricity 0.2, BasicEducation 50, HigherEducation 200, SecondaryEducation 100, Garbage 0.1, Water 0.3, FireResponse 0, Police 0`. The water figure is exactly the `Version.waterFeeReset` migration constant `0.3f` (`ServiceFee.cs:34-37`); the three education figures match no source in this build. **A fee read out of a running game is the save's number, not the game's.**

The fee slider is a 200-step lerp: the panel sends `lerp(item.min, item.max, t / 200)` and displays `200 * (fee - min) / (max - min)`, with `min` written as the literal 0 and `max` as `FeeParameters.m_Max` (`source.js:107333-107357`, the C# side at `src/Game/Game.UI.InGame/ServiceBudgetUISystem.cs:219-222`).

*Rots:* `PlayerResource`'s member set and order (it is a serialized `int` in `ServiceFee`), `ServiceFeeParameterData`'s field list, `kUpdatesPerDay`, and the fee-slider step count.

### District assignment scopes two different things, and only one of them is `ServiceDistrict`

**Mechanism one: `ServiceDistrict`, a buffer on the service building** (`src/Game/Game.Areas/ServiceDistrict.cs:8-10`), listing the districts it serves. The rule is `AreaUtils.CheckServiceDistrict` in three overloads (`src/Game/Game.Areas/AreaUtils.cs:144-190`):

```
no buffer, or an empty buffer      → true   (serves everywhere)
buffer non-empty, target district Null → false
otherwise                          → the buffer contains that district
```

**An empty list is "everywhere", and a non-empty list excludes anything not in a district at all.** It scopes two things and nothing else:

- **Which stations may be dispatched to a request**, checked inside each `*PathfindSetup` before the target seeker is called. At 1.6.0f1 the call sites are `HealthcarePathfindSetup` (6), `FirePathfindSetup` (7), `PolicePathfindSetup` (7), `GarbagePathfindSetup` (3), `GarbageTruckAISystem` (3, re-checked per stop against `CurrentDistrict`), `PostServicePathfindSetup` (3), `RoadPathfindSetup` (3, road/park maintenance), `TransportPathfindSetup` (3, taxi and transit) and `CitizenPathfindSetup` (1, school seeking) — a grep of `src/Game/` for `CheckServiceDistrict`.
- **Which road edges a station covers**, at half weight on a border road (see the coverage-district finding).

Twelve prefab classes give a building the buffer, and each gates it: `Hospital`, `DeathcareFacility`, `FireStation`, `PoliceStation`, `GarbageFacility`, `School`, `PostFacility`, `Prison`, `WelfareOffice`, `AdministrationBuilding`, `EmergencyShelter`, `TransportDepot` (`src/Game/Game.Prefabs/Hospital.cs:52-55` and siblings). The common gate is `GetComponent<UniqueObject>() == null` — **a unique building (a city's one landmark station) gets no district assignment**; `DeathcareFacility` adds it unconditionally, `TransportDepot` only when `m_TransportType == TransportType.Taxi` (`src/Game/Game.Prefabs/TransportDepot.cs:48-51`), and `PostFacility` only when `m_PostVanCapacity > 0` (`src/Game/Game.Prefabs/PostFacility.cs:56-61`). `MaintenanceDepot` and `TelecomFacility` add it **never** (`src/Game/Game.Prefabs/MaintenanceDepot.cs:40-48`, `src/Game/Game.Prefabs/TelecomFacility.cs:28-36`) — so a maintenance depot cannot be district-restricted from its own panel even though `RoadPathfindSetup` calls the check.

**Mechanism two: `DistrictModifier`, a buffer on the district**, indexed by `DistrictModifierType` and applied as `value += delta.x; value += value * delta.y` (`src/Game/Game.Areas/AreaUtils.cs:129-137`, the type enum at `src/Game/Game.Areas/DistrictModifierType.cs:3-19`, fourteen members). Four of the fourteen bear on this topic and each is applied at exactly one site: `GarbageProduction` (`src/Game/Game.Simulation/GarbageAccumulationSystem.cs:494`), `BuildingFireHazard` (`src/Game/Game.Simulation/EventHelpers.cs:66`), `BuildingFireResponseTime` (`src/Game/Game.Simulation/FireSimulationSystem.cs:252`) and `CrimeAccumulation` (`src/Game/Game.Simulation/CrimeAccumulationSystem.cs:124`). District *policies* therefore change how much work a district generates; district *assignment* changes who does it. The two are unrelated code paths that a reader will conflate.

Live at 1.6.0f1 this city has **zero `Game.Areas.District` entities** and 35 entities carrying a `ServiceDistrict` buffer; the one sampled (index 84907, an outside connection) was length 0 — so nothing here was exercised against a real district. The claims above are all decompile claims.

*Rots:* `ServiceDistrict`, `DistrictModifier`, `DistrictModifierType`'s member set and order (the buffer is indexed by it), and the twelve-class list.

### What the services with no flow graph have in common, and what each failure looks like

The uniform part, established above: a prefab component pair (`<X>` on the prefab class → `<X>Data` on the prefab entity → `Game.Buildings.<X>` on the instance), a `UpdateFrameData(n)` group index, an `Efficiency` buffer, an `AISystem` at `GameSimulation`, and — where the service dispatches — `ServiceDispatch`, `OwnedVehicle` and a request kind. The per-service update groups are C# literals and ship (`src/Game/Game.Prefabs/*.cs`, each class's `Initialize`): `DisasterFacility` 0, `FirewatchTower` 1, `Hospital` 1, `DeathcareFacility` 2, `Prison` 3, `AdministrationBuilding` 4, `GarbageFacility` 5, `WelfareOffice` 5, `School` 6, `FireStation` 7, `PoliceStation` 8, `Park` 9, `MaintenanceDepot` 10, `PostFacility` 11, `ParkingFacility` 12, `TelecomFacility` 13, `ExtractorFacility` 14, `EmergencyShelter` 15.

The variable part is what "failing" means, and it is a different component for every one:

| Service | Demand carried on | Dispatcher state | Failure surface |
| --- | --- | --- | --- |
| **Healthcare** | `Game.Citizens.HealthProblem` with `HealthProblemFlags` (`src/Game/Game.Citizens/HealthProblemFlags.cs:6-15`: `Sick, Dead, Injured, RequireTransport, InDanger, Trapped, NoHealthcare`) | `Game.Buildings.Hospital { m_TargetRequest, m_Flags, m_TreatmentBonus, m_MinHealth, m_MaxHealth }`, `HospitalFlags` (`src/Game/Game.Buildings/HospitalFlags.cs:6-14`) | `HealthProblem.m_Timer` climbing past `HealthcareParameterData.m_TransportWarningTime` raises `m_AmbulanceNotificationPrefab` (`HealthProblemSystem.cs:428-433`); a hospital without `HospitalFlags.HasRoomForPatients` takes a flat `+120f` cost handicap in dispatch rather than dropping out (`HealthcarePathfindSetup.cs:208-211`) |
| **Deathcare** | the same `HealthProblem` with `Dead` | `Game.Buildings.DeathcareFacility`, `DeathcareFacilityFlags { HasAvailableHearses, HasRoomForBodies, CanProcessCorpses, CanStoreCorpses, IsFull }` | `IsFull`; a corpse whose `HealthcareRequest` keeps failing backs off exponentially and the body stays put |
| **Garbage** | `Game.Buildings.GarbageProducer { m_CollectionRequest, m_Garbage, m_Flags, m_DispatchIndex }` | `Game.Buildings.GarbageFacility`, `GarbageFacilityFlags { HasAvailableGarbageTrucks, HasAvailableSpace, IndustrialWasteOnly, IsFull, HasAvailableDeliveryTrucks }` | `GarbageProducerFlags.GarbagePilingUpWarning`, and **an efficiency penalty on the producing building**: `1 - m_GarbagePenalty * saturate((garbage - m_WarningGarbageLimit) / (m_MaxGarbageAccumulation - m_WarningGarbageLimit))` → `EfficiencyFactor.Garbage` (`src/Game/Game.Simulation/GarbageAccumulationSystem.cs:507-511`, applied at `:171-172` and again from the truck at `GarbageTruckAISystem.cs:1043`) |
| **Fire** | no demand component; a `Game.Events.Fire` event created against a hazard roll | `Game.Buildings.FireStation`, `FireStationFlags { HasAvailableFireEngines, HasFreeFireEngines, HasAvailableFireHelicopters, HasFreeFireHelicopters, DisasterResponseAvailable }` | `EfficiencyFactor.Fire = 0` while `m_Intensity > 0.01f` (`FireSimulationSystem.cs:237`) — a burning building does nothing at all; coverage suppresses the hazard rather than the fire |
| **Disaster control** | `Game.Buildings.RescueTarget { m_Request }`, `EvacuationRequest` | `FireStationData.m_DisasterResponseCapacity` → `FireStationFlags.DisasterResponseAvailable` → `FireEngineFlags.DisasterResponse`, matched against `FireRescueRequestType.Disaster` (`src/Game/Game.Simulation/FirePathfindSetup.cs:112`, `:153`); `Game.Buildings.EmergencyShelter` + `EmergencyShelterFlags { HasAvailableVehicles, HasShelterSpace }`; `Game.Buildings.EarlyDisasterWarningSystem` (a tag) granting `EarlyDisasterWarningDuration { m_EndFrame }` to every such building when a warned event spawns (`src/Game/Game.Events/InitializeSystem.cs:561-570`) | no shelter space, no disaster-capable engine; **`Game.Buildings.DisasterFacility` and `DisasterFacilityData` are both empty tags read only by `MarkerCreateSystem`, `ObjectColorSystem` and `InfoviewInitializeSystem`** — the building type exists for the info view and the simulation goes through the fire station and the shelter |
| **Police** | `Game.Buildings.CrimeProducer { m_PatrolRequest, m_Crime, m_DispatchIndex }` | `Game.Buildings.PoliceStation`, `PoliceStationFlags { HasAvailablePatrolCars, HasAvailablePoliceHelicopters, NeedPrisonerTransport }`, `PolicePurpose` on the prefab; `Game.Buildings.Prison` + `PrisonFlags` | crime accumulates to `PoliceConfigurationData.m_MaxCrimeAccumulation` and drives a happiness penalty (`CitizenHappinessSystem.cs:1278`) and a `m_CrimeSceneNotificationPrefab`; a patrol visit subtracts `min(m_CrimeReductionRate, m_Crime)` (`src/Game/Game.Simulation/PoliceCarAISystem.cs:1088-1098`, the aircraft twin at `PoliceAircraftAISystem.cs:1087`) |
| **Administration** | none | `Game.Buildings.AdminBuilding` — an **empty tag** | **nothing.** A grep of `src/Game/` returns `AdminBuilding` in `MarkerCreateSystem`, `ObjectColorSystem` and `InfoviewInitializeSystem` only — and the last matches the prefab-side `AdminBuildingData`, never the instance tag (`src/Game/Game.Prefabs/InfoviewInitializeSystem.cs:177`, `:309`). An administration building's whole simulation contribution is its upkeep, its workplaces and whatever `CityEffects` / `CityEffectProvider` city modifiers it carries (`src/Game/Game.Prefabs/CityEffects.cs:27`, `:33`), which belong to `city-state-and-progression` |
| **Education** | `Game.Citizens.Student` on the citizen, `Game.Buildings.Student` buffer on the school | `Game.Buildings.School { m_AverageGraduationTime, m_AverageFailProbability, … }`, `SchoolData { m_EducationLevel, m_StudentCapacity, m_GraduationModifier, m_StudentWellbeing, m_StudentHealth }` | efficiency at or below 0.001 makes each student leave with probability `EducationParameterData.m_InoperableSchoolLeaveProbability` (`SchoolAISystem.cs:104-108`); otherwise efficiency enters `GetGraduationProbability` directly (`:113`) |
| **Post** | `Game.Buildings.MailProducer { m_MailRequest, m_SendingMail, m_ReceivingMail, m_DispatchIndex, m_LastUpdateTotalMail }`, with `receivingMail` and `mailDelivered` packed into the high bit | `Game.Buildings.PostFacility`, `PostFacilityFlags` (seven members, `src/Game/Game.Buildings/PostFacilityFlags.cs:6-14`); `Game.Routes.MailBox` | **an efficiency penalty on the producing building**, `1 - m_MailEfficiencyPenalty * f(max(sending, receiving) - m_NegligibleMail)` where `f` is zero below 25 and `(min(50, n-25)^2 + 125) / 2625` above (`src/Game/Game.Simulation/MailAccumulationSystem.cs:246-256`) — the 25, 50, 125 and 2625 are C# literals |
| **Telecom** | `Game.Buildings.TelecomConsumer` (an empty tag) plus `ConsumptionData.m_TelecomNeed` | `Game.Buildings.TelecomFacility`, `TelecomFacilityFlags { HasCoverage }` | `EfficiencyFactor.Telecom = 1 - (1 - quality/m_TelecomBaseline)^2 * 0.01 * m_TelecomNeed` when quality is under the baseline, else 1 (`src/Game/Game.Simulation/TelecomEfficiencySystem.cs:74-85`); `-0.01f` is a C# literal |
| **Leisure** | `Game.Citizens.Leisure` and the citizen's `m_LeisureCounter`; `Game.Buildings.LeisureProvider` (an empty tag) + `LeisureProviderData { m_Efficiency, m_Resources, m_LeisureType }` (`src/Game/Game.Prefabs/LeisureProviderData.cs:8-14`) | none — `LeisureSystem` is a citizen-side system | see the leisure finding below |
| **Parks** | `Game.Simulation.MaintenanceConsumer` (declared `src/Game/Game.Simulation/MaintenanceConsumer.cs`), `MaintenanceRequest` | `Game.Buildings.MaintenanceDepot` + `MaintenanceDepotFlags { HasAvailableVehicles }`, `MaintenanceType { Park, Road, Snow, Vehicle }` | see the park finding below |

*Rots:* every component, flag enum and update-group index in this table.

### The census of coverage prefabs, and what it says about parks

Read live at 1.6.0f1 by `ecs_query` on `Game.Prefabs.CoverageData` joined with each service's own data component — the join is exactly the `if`/`else if` chain `ServiceCoverage.Initialize` uses to assign `CoverageData.m_Service` (`src/Game/Game.Prefabs/ServiceCoverage.cs:37-72`, which logs `"Unknown coverage service type: {0}"` for a prefab matching none and still writes the component below the chain — `m_Service` stays at default `CoverageService.Healthcare`, the enum's first member (`:69-73`, `src/Game/Game.Net/CoverageService.cs:5`), so such a prefab silently covers as Healthcare):

| Service | prefabs carrying `CoverageData` |
| --- | --- |
| Park | 97 |
| Police | 14 |
| Healthcare | 11 |
| FireRescue | 10 |
| Education | 9 |
| PostService | 4 (3 post facilities, each also a mailbox, plus 1 standalone mailbox) |
| EmergencyShelter | 3 |
| Welfare | 2 |
| **total** | **150** |

The nine joins sum to 153 against a total of 150 because all three `PostFacilityData` prefabs also carry `MailBoxData` — `PostFacility.GetPrefabComponents` adds it whenever `m_MailBoxCapacity > 0` (`src/Game/Game.Prefabs/PostFacility.cs:34-37`) — and `Initialize` tests `PostFacilityData || MailBoxData` in one arm.

**Ruled (2026-08-11, the utilities-and-flow-networks pass under the maintainer's delegated authority; conflicts.md).** Same ruling as the budget-adjustability census above — a structural fact read off prefab values is a swept set's shape and ships as the shape with its reproducing query attached, never as a bare fact. So the mechanism ships flat — `ServiceCoverage.Initialize` assigns `CoverageData.m_Service` by testing for the building's own service data component, in a fixed order, and a prefab matching none logs the unknown-service error and still gets the write, `m_Service` defaulting to `CoverageService.Healthcare` (the ruling as first recorded said it errors outright; the census half is untouched) — and the census ships as the shape the enumeration shows, with the reproducing query beside it (`ecs_query` on `Game.Prefabs.CoverageData` joined per service data component), and at most one prefab name as a cited worked example. Never a roster. The shape worth stating is that **parks outnumber every other coverage-providing family put together**, and that this is one install's DLC set rather than a fact about the base game — the same caveat the zoning, economy and utilities passes carry.

Four sampled prefabs, live, to show the field shape (the values are prefab data and do not ship): `PoliceStation01` `{ Police, m_Range=6000, m_Capacity=24000, m_Magnitude=6 }`, `ElementarySchool01` `{ Education, 4200, 18000, 15 }`, `EmergencyShelter01` `{ EmergencyShelter, 1800, 6000, 7.5 }`, `PostMailbox01` `{ PostService, 1200, 2400, 1.5 }`. What the sample shows and the reference can state is the **spread**: the three fields move together across a service's building tiers but not in a fixed ratio, and the services differ widely in range — a derived ratio over the sampled values is a prefab magnitude and does not ship (the ruling at the top of this file) — so no reader should reason about one field from another.

*Rots:* the counts and the assignment chain's order.

### Parks are the only service whose coverage moves at runtime, and leisure lands here

`Park.GetArchetypeComponents` gives a non-upgrade park `MaintenanceConsumer`, **`ModifiedServiceCoverage`**, `UpdateFrame` and `CurrentDistrict` (`src/Game/Game.Prefabs/Park.cs:27-42`). `ModifiedServiceCoverage { m_Range, m_Capacity, m_Magnitude }` (`src/Game/Game.Buildings/ModifiedServiceCoverage.cs:7-27`) is the only per-instance override of `CoverageData` in the game, and `ProcessCoverageJob` applies it through `ReplaceData` before computing coverage (`ServiceCoverageSystem.cs:208-211`).

`ParkAISystem` recomputes it every update (`src/Game/Game.Simulation/ParkAISystem.cs:231-243`):

```
fill  = m_Maintenance / max(1, ParkData.m_MaintenancePool)
steps = floor(fill / 0.3f)                              // 0, 1, 2 or 3
m_Magnitude *= 0.95 + 0.05 * min(1, steps) + 0.1 * max(0, steps - 1)
m_Range     *= 0.95 + 0.05 * steps
m_Magnitude  = ApplyModifier(m_Magnitude, CityModifierType.ParkEntertainment)
```

Every constant is a C# literal and the whole thing ships. Maintenance itself decays as `m_Maintenance -= (400 + 50 * renterCount) / kUpdatesPerDay` with `kUpdatesPerDay = 256` (`:85`, `:173`), and a maintenance request is raised when `m_MaintenancePool - m_Maintenance - m_MaintenancePool / 10` is positive (`:106-114`, `:228`) — a 10% deadband. `ParkInitializeSystem` seeds the same value on creation (`src/Game/Game.Buildings/ParkInitializeSystem.cs:63`).

**`ModifiedServiceCoverage.m_Range` is written and never used.** `ProcessCoverageJob` reads only `m_Magnitude` (`:269`) and `m_Capacity` (`:279-280`) out of the replaced struct; the range the pathfind runs on comes from `SetupCoverageSearchJob`, which reads the **prefab's** `CoverageData` and never consults `ModifiedServiceCoverage` (`:380-391`). The two preview systems do the same (`src/Game/Game.Tools/CoveragePreviewSystem.cs:94`, `:132`; `src/Game/Game.Tools/ToolFeedbackSystem.cs:158-160`, `:375-391`, both defaulting an absent `m_Range` to the literal `25000f`). So **a neglected park loses coverage strength only**: `m_Capacity` is copied from the prefab untouched (`ParkAISystem.cs:234`, the constructor copy), magnitude is the one field both written and read, and the range half of the multiplier is dead code at 1.6.0f1.

**Leisure.** The ticket flags that the approved structure's coverage map assigns the `Leisure` info view here while its Owns line omits leisure. Resolved live: the `Leisure` infoview prefab (entity 226:1) carries five `InfoviewMode` entries, read through its `InfoviewMode` buffer and named through `PrefabSystem.GetPrefabName` — **`ParkMaintenance Buildings`, `Park Buildings`, `Park Maintenance Vehicles`, `LeisureProvider`, and `Park Coverage`** (the last marked `m_Optional`). Their data components confirm it: `Park Coverage` is an `InfoviewCoverageData { m_Service = CoverageService.Park, m_Range = [0, 10] }` and `LeisureProvider` is an `InfoviewBuildingStatusData { m_Type = BuildingStatusType.LeisureProvider }` paired with an `InfoviewNetStatusData { m_Type = NetStatusType.LeisureProvider }` — the pairing `BuildingStateInfomodePrefab` creates for exactly that one status type (`src/Game/Game.Prefabs/BuildingStateInfomodePrefab.cs:18-21`, `:31-37`).

**So the `Leisure` info view *is* the parks-and-recreation view**, and the structure's map is right: park coverage, park maintenance and the leisure-provider colouring are one view and one reference's material. The demand side — `Game.Citizens.Leisure`, the citizen's `m_LeisureCounter`, `LeisureType`'s ten members (`src/Game/Game.Agents/LeisureType.cs:3-15`) and `LeisureSystem`'s trip generation — is citizen behaviour and belongs to `citizens-and-households`; what this reference owns is the **supply** side: `Game.Buildings.LeisureProvider` is added only when `m_Efficiency > 0` (`src/Game/Game.Prefabs/LeisureProvider.cs:31-37`), `LeisureProviderData` is a per-prefab component a commercial company or a park carries, and `CoverageService.Park` coverage is what turns a park into a wellbeing bonus (`CitizenHappinessSystem.GetEntertainmentBonuses`, `:1201-1214`).

`Infoviews.INFOVIEW[Leisure]` exists as a locale key in the shipped data — one of 36 `Infoviews.INFOVIEW[...]` keys found by a raw byte-grep of `Cities2_Data/Content/Game/Locale.cok` — and there is **no `LeisureInfoviewUISystem`**: `src/Game/Game.UI.InGame/` holds 24 `*InfoviewUISystem.cs` files (plus `InfoviewUISystemBase.cs`, which the glob does not match) and none of them is Leisure's, so the view is infomodes and rendering only, with no binding group of its own.

**Leisure has no failure surface, and choosing a venue never reads building efficiency.** No `LeisureRequest` exists under `src/Game/Game.Simulation/`, no `EfficiencyFactor` member is leisure's (`src/Game/Game.Buildings/EfficiencyFactor.cs` — 32 members, none leisure), and no notification or statistic prefab names it. `SetupLeisureTargetJob` (`src/Game/Game.Simulation/CitizenPathfindSetup.cs:104-195`) declares no `Efficiency` lookup — the `BufferLookup<Efficiency>` at `:216` belongs to the school-seeker job — and rejects a candidate only on `BuildingOption.Inactive` (`:154`), a missing `LeisureProviderData` (`:159`), a `LeisureType` mismatch (`:165`) and, for `Commercial`/`Meals`, insufficient `ServiceAvailable` (`:169-172`). A search that finds nothing removes the citizen's `Leisure` and `TravelPurpose` and stamps `Game.Citizens.LeisureSeekerCooldown { m_SimulationFrame }` (`src/Game/Game.Simulation/LeisureSystem.cs:422-434`; `TripNeededSystem` stamps the same component on the `Purpose.Leisure` arm when the trip's path fails, `src/Game/Game.Simulation/TripNeededSystem.cs:1571-1578`), overwritten per failure rather than accumulated, read against `kLeisureSeekerCooldownFrames` in `CitizenBehaviorSystem.DoLeisure` — the only observable consequence is the citizen's leisure counter drifting down, which `citizens-and-households` owns.

*Rots:* `ModifiedServiceCoverage`, `ParkData`, the maintenance constants, `LeisureType`'s member set, and the five infomode names.

### Telecom is the one service in this topic that is a cell map

`TelecomCoverageSystem` (`src/Game/Game.Simulation/TelecomCoverageSystem.cs`) runs at `GameSimulation` on a 4096-frame interval (`SystemOrder.cs:429`, `:649`) and rebuilds a **128 × 128** map from scratch each time (`public const int TEXTURE_SIZE = 128`, `:631`). Per facility (`:188-226`):

```
range    = TelecomFacilityData.m_Range * sqrt(efficiency)
capacity = ApplyModifier(m_NetworkCapacity, CityModifierType.TelecomCapacity) * efficiency
skip if range < 1 or capacity < 1
users    = CalculateNetworkUsers(density, ...) over the range's cell rectangle
addCapacity(cells, capacity / max(1, users))
```

**Efficiency scales telecom range as a square root and capacity linearly** — the one service in this topic whose reach genuinely moves with efficiency, and the exception that makes the wiki's coverage-range claim plausible for the wrong service. `TelecomFacilityData` also carries `m_PenetrateTerrain`, and `CalculateSignalStrength` computes obstruction slopes per facility (`:121`), so terrain blocks a mast.

Each cell stores two bytes (`src/Game/Game.Simulation/TelecomCoverage.cs:6-12`):

```
m_SignalStrength = clamp(strength * 255, 0, 255)
m_NetworkLoad    = clamp(127.5f / max(0.0001f, capacity), 0, 255)          (:154-155)
networkQuality   = m_SignalStrength * 510 / (255 + (m_NetworkLoad << 1))   (:12)
```

and `SampleNetworkQuality` bilinearly interpolates `min(1, strength / (127.5 + load))` over the four surrounding cells (`:35-51`). Every constant there is a C# literal.

The city-wide figure is `TelecomStatus { m_Capacity, m_Load, m_Quality }` (`src/Game/Game.Simulation/TelecomStatus.cs:5-9`), with `m_Quality` a **density-weighted** mean of `min(1, 2*strength / (1 + 1/capacity))` over all 16,384 cells (`:162-186`) — so empty land does not dilute it.

The same map is read by `LandValueSystem`'s cell job as `TelecomCoverage.SampleNetworkQuality(...) * m_TelecomCoverageBonusMultiplier`, capped at `m_CommonFactorMaxBonus` (`LandValueSystem.cs:127`, `:131`), and by `TelecomEfficiencySystem` on a 32-frame system interval spread over sixteen `UpdateFrame` groups — `kUpdatesPerDay = 512` (`TelecomEfficiencySystem.cs:127`), the group filter `GetUpdateFrame(frameIndex, 512, 16)` (`:164`), so one building is rewritten every 512 frames (`SystemOrder.cs:430`, `:143`).

*Rots:* `TEXTURE_SIZE`, `TelecomCoverage`'s two byte fields and the `networkQuality` expression, `TelecomFacilityData`'s fields.

### The cadence table

Every system in this topic and how often it runs. `GetUpdateInterval` returns frames; where the source writes `262144 / (kUpdatesPerDay * 16)` the `262144` is `TimeSystem.kTicksPerDay` (`src/Game/Game.Simulation/TimeSystem.cs:18`) and the `16` is the shared `UpdateFrame` group count, so the stated figure is frames between one group's updates.

| System | interval | source |
| --- | --- | --- |
| `ServiceCoverageSystem` | every frame, self-gated on `frame % 256` | `src/Game/Game.Simulation/ServiceCoverageSystem.cs:521-532` |
| `CityServiceEfficiencySystem` | event-driven (`ModificationEnd`, on changed budget or changed building) | `src/Game/Game.Buildings/CityServiceEfficiencySystem.cs:162-186` |
| `ServiceRequestSystem` | event-driven (`ModificationEnd`) | `src/Game/Game.Simulation/ServiceRequestSystem.cs:184-187` |
| `CityServiceUpkeepSystem` | 256 (`kUpdatesPerDay = 64`, 16 groups) | `src/Game/Game.Simulation/CityServiceUpkeepSystem.cs:468`, `:494` |
| `ServiceFeeSystem` | 2048 (`kUpdatesPerDay = 128`) | `src/Game/Game.Simulation/ServiceFeeSystem.cs:330`, `:350-352` |
| `GarbageAccumulationSystem` | 1024 (`kUpdatesPerDay = 16`, 16 groups) | `src/Game/Game.Simulation/GarbageAccumulationSystem.cs:325`, `:355` |
| `CrimeAccumulationSystem` | 64 (`kUpdatesPerDay = 256`, 16 groups) | `src/Game/Game.Simulation/CrimeAccumulationSystem.cs:234-236`, `:256` |
| `MailAccumulationSystem` | 64 (`kUpdatesPerDay = 256`) | `src/Game/Game.Simulation/MailAccumulationSystem.cs:327`, `:361` |
| `TelecomCoverageSystem` | 4096 | `src/Game/Game.Simulation/TelecomCoverageSystem.cs:649` |
| `TelecomEfficiencySystem` | 32 | `src/Game/Game.Simulation/TelecomEfficiencySystem.cs:143` |
| `FireHazardSystem` | 4096 | `src/Game/Game.Simulation/FireHazardSystem.cs:279` |
| `HealthProblemSystem` | 16 | `src/Game/Game.Simulation/HealthProblemSystem.cs:849` |
| `LandValueSystem` | `262144 / kUpdatesPerDay` | `src/Game/Game.Simulation/LandValueSystem.cs:294` |
| `ParkAISystem` | `kUpdatesPerDay = 256` | `src/Game/Game.Simulation/ParkAISystem.cs:173` |

`CrimeAccumulationSystem` asserts its own cadence at construction: `Assert.IsTrue((long)(kUpdateInterval * 16) >= 512L)` (`:283`) — a first-party statement that lowering the interval is unsupported.

The registration block for this topic is one contiguous run: `ServiceRequestSystem` at `ModificationEnd` (`SystemOrder.cs:270`), `CityServiceEfficiencySystem` at `:292`, `CityServiceBudgetSystem` `UpdateAfter` at `:301`, `ServiceCoverageSystem` at `:306`, the vehicle AI systems at `:315-335`, the facility AI systems at `:521-541` (two of which — `SewageOutletAISystem` and `WaterPumpingStationAISystem` — belong to `utilities-and-flow-networks`), and the dispatch systems at `:554-568`, all in `GameSimulation`.

*Rots:* every interval and every `kUpdatesPerDay`. Re-check each listed system's `GetUpdateInterval`.

### The parameter map, with access shapes

**Singletons — `GetSingleton<T>()`, `ecs-in-this-game`'s route:**

| Component | Owns |
| --- | --- |
| `Game.Prefabs.BuildingEfficiencyParameterData` | `m_ServiceBudgetEfficiencyFactor` (the budget curve), `m_LowEfficiencyThreshold`, `m_GarbagePenalty`, `m_NegligibleMail`, `m_MailEfficiencyPenalty`, `m_TelecomBaseline`, `m_MissingEmployeesEfficiencyPenalty`/`Delay`, `m_ServiceBuildingEfficiencyGracePeriod`, `m_SickEmployeesEfficiencyPenalty`, plus the six utility penalties `utilities-and-flow-networks` owns (`src/Game/Game.Prefabs/BuildingEfficiencyParameterData.cs:12-28`) |
| `Game.Prefabs.ServiceFeeParameterData` | nine `FeeParameters` (each `m_Default`, `m_Max`, `m_Adjustable`) plus the two utility consumption curves (`src/Game/Game.Prefabs/ServiceFeeParameterData.cs:8-30`) |
| `Game.Prefabs.HealthcareParameterData` | `m_HealthcareServicePrefab`, three notification prefabs, `m_TransportWarningTime`, `m_NoResourceTreatmentPenalty`, `m_BuildingDestoryDeathRate` (the typo is the game's), `m_DeathRate`, `m_LegacyDeathRate` |
| `Game.Prefabs.GarbageParameterData` | `m_HomelessGarbageProduce`, `m_CollectionGarbageLimit`, `m_RequestGarbageLimit`, `m_WarningGarbageLimit`, `m_MaxGarbageAccumulation`, `m_BuildingLevelBalance`, `m_EducationBalance`, `m_HappinessEffectBaseline`, `m_HappinessEffectStep`, plus the service and two notification prefabs |
| `Game.Prefabs.PoliceConfigurationData` | `m_MaxCrimeAccumulation`, `m_CrimeAccumulationTolerance`, `m_HomeCrimeEffect`, `m_WorkplaceCrimeEffect`, `m_WelfareCrimeRecurrenceFactor`, `m_CrimePoliceCoverageFactor`, `m_CrimePopulationReduction` (`src/Game/Game.Prefabs/PoliceConfigurationData.cs:5-26`) |
| `Game.Prefabs.FireConfigurationData` | five structural-integrity levels plus a default and a building baseline, `m_ResponseTimeRange`, `m_TelecomResponseTimeModifier`, `m_DarknessResponseTimeModifier`, `m_DeathRateOfFireAccident` (`src/Game/Game.Prefabs/FireConfigurationData.cs:6-32`) |
| `Game.Prefabs.PostConfigurationData` | `m_MaxMailAccumulation`, `m_MailAccumulationTolerance`, `m_OutgoingMailPercentage` (`src/Game/Game.Prefabs/PostConfigurationData.cs:5-13`) |
| `Game.Prefabs.EducationParameterData` | `m_InoperableSchoolLeaveProbability`, `m_EnterHighSchoolProbability`, `m_AdultEnterHighSchoolProbability`, `m_WorkerContinueEducationProbability` |
| `Game.Prefabs.WorkProviderParameterData` | two notification prefabs and delays, `m_UneducatedNotificationLimit`, `m_EducatedNotificationLimit`, `m_SeniorEmployeeLevel` |
| `Game.Prefabs.DisasterConfigurationData` | five notification prefabs, `m_FloodDamageRate`, `m_EmergencyShelterDangerLevelExitProbability`, `m_InoperableEmergencyShelterExitProbability` |
| `Game.Prefabs.LeisureParametersData` | three leisure prefabs plus `m_LeisureRandomFactor`, `m_ChanceCitizenDecreaseLeisureCounter`, `m_ChanceTouristDecreaseLeisureCounter`, `m_AmountLeisureCounterDecrease`, `m_TouristLodgingConsumePerDay`, `m_TouristServiceConsumePerDay` |
| `Game.Prefabs.TelecomParameterData`, `Game.Prefabs.ParkParameterData` | one field each: the service prefab entity |
| `Game.Prefabs.LandValueParameterData` | `m_HealthCoverageBonusMultiplier`, `m_EducationCoverageBonusMultiplier`, `m_PoliceCoverageBonusMultiplier`, `m_TelecomCoverageBonusMultiplier`, `m_CommonFactorMaxBonus`, and the pollution penalties (`zoning-buildings-and-land-value` owns the component) |
| `Game.Prefabs.CitizenHappinessParameterData` | `m_HealthCareHealthMultiplier`, `m_HealthCareWellbeingMultiplier`, `m_EducationWellbeingMultiplier`, `m_NeutralEducation`, `m_EntertainmentWellbeingMultiplier`, `m_WelfareMultiplier`, `m_NegligibleCrime`, `m_CrimeMultiplier` (`citizens-and-households` owns the component) |

**There is no welfare parameter singleton** — the welfare service's tuning values are `CitizenHappinessParameterData.m_WelfareMultiplier` and `PoliceConfigurationData.m_WelfareCrimeRecurrenceFactor`.

**Buffers on the city entity — `EntityManager.GetBuffer<T>(CitySystem.City)`:** `Game.City.ServiceFee` (nine elements at 1.6.0f1), `Game.City.CityModifier`.

**A buffer on a plain singleton entity, not the city:** `Game.Simulation.ServiceBudgetData`, reached through `CityServiceBudgetSystem.GetServiceBudget(servicePrefab)` / `SetServiceBudget(servicePrefab, percentage)` (`CityServiceBudgetSystem.cs:1060-1115`; the query is options-free at `:810`, and `OnGameLoaded` creates the entity when a save lacks one, `:830-836`). **An empty buffer is a legitimate state**: `SetServiceBudget` appends the only entries there ever are, a missing entry reads as 100 (`:1081`), `GetServiceBudget` returns 0 for a prefab outside the system's collected budget map (`:1068-1071`), and `SetServiceBudget` silently no-ops on one (`:1087-1090`). This pass read entity 98443 carrying a zero-length buffer while the getter returned figures — which is exactly the untouched-sliders state, the getter supplying the 100 defaults.

**Per-service-prefab — an `ecs_query` on the component, since prefab entities carry `PrefabData` rather than Unity's `Prefab` tag:** `ServiceData { m_Service, m_BudgetAdjustable }`, `CollectedCityServiceBudgetData`, `CollectedCityServiceUpkeepData` (buffer), `CollectedCityServiceFeeData` (buffer, present only when `ServicePrefab.m_CityResources` is non-empty).

**Per-building-prefab — the same `PrefabRef` hop, and most `<X>Data` are `ICombineData` so `UpgradeUtils.CombineStats` folds installed upgrades in before use (the empty tags `AdminBuildingData`, `DisasterFacilityData` and `WelfareOfficeData` are not):** `CoverageData`, `HospitalData`, `DeathcareFacilityData`, `FireStationData`, `PoliceStationData`, `PrisonData`, `GarbageFacilityData`, `SchoolData`, `PostFacilityData`, `MailBoxData`, `TelecomFacilityData`, `MaintenanceDepotData`, `ParkData`, `EmergencyShelterData`, `WelfareOfficeData`, `AdminBuildingData`, `DisasterFacilityData`, `LeisureProviderData`, `WorkplaceData`, `ConsumptionData`, plus the `ServiceUpkeepData` and `ServiceObjectData` that tie a building to its service.

**Per-building-instance:** `Efficiency` (buffer), `ServiceUsage`, `ServiceDispatch` (buffer), `ServiceDistrict` (buffer), `OwnedVehicle` (buffer), `WorkProvider`, `Employee` (buffer), `ModifiedServiceCoverage` (parks only), `Game.Pathfind.CoverageElement` (buffer), and the shared `CoverageServiceType`.

**Two traps belong beside the map.** `CoverageServiceType` is a **shared** component (`src/Game/Game.Net/CoverageServiceType.cs:6-8`), so it is `eval`-only over the debugger and a change to it moves the entity to a different chunk. And `ServiceCoverage.GetArchetypeComponents` adds `CoverageServiceType` and `CoverageElement` while `GetPrefabComponents` adds `CoverageData` (`src/Game/Game.Prefabs/ServiceCoverage.cs:18-27`) — **adding `CoverageData` to a prefab entity at runtime does not make its instances covering buildings**, because the archetype is already built.

*Rots:* every component and field name in this map. Re-check at their declaration files under `src/Game/Game.Prefabs/`.

### The parameter prefabs' initializers are right until they are catastrophically wrong

Seven `PrefabBase` subclasses in this topic declare field initializers and copy them into their data components in `LateInitialize`: `BuildingEfficiencyParametersPrefab` (16 scalars, `src/Game/Game.Prefabs/BuildingEfficiencyParametersPrefab.cs:17-88`), `PoliceConfigurationPrefab` (7, `:18-33`), `FireConfigurationPrefab` (11, `:16-40`), `PostServiceConfigurationPrefab` (3, `:12-16`), `WorkProviderParameterPrefab` (5, `:17-31`), `LeisureParametersPrefab` (6, `:18-33`) and `DisasterConfigurationPrefab` (2, `:22-29`). `ServiceFeeParameterPrefab` declares none.

Initializer against live singleton, 1.6.0f1:

| Component | fields checked | agreeing | disagreeing |
| --- | --- | --- | --- |
| `BuildingEfficiencyParameterData` | 16 | **16** | 0 |
| `FireConfigurationData` | 11 | **11** | 0 |
| `PoliceConfigurationData` | 7 | 5 | **2** |

The two that differ: `m_MaxCrimeAccumulation` reads **25,000** live against an initializer of `100000f`, and `m_CrimePoliceCoverageFactor` reads **5,000** live against an initializer of `2f` — **a factor of 2,500**.

That second one is the load-bearing case, because `CrimeAccumulationSystem` multiplies a building's crime rate by `m_CrimePoliceCoverageFactor * max(0, 5f / (5f + coverage))` (`src/Game/Game.Simulation/CrimeAccumulationSystem.cs:110`). A reader who took the initializer would model police coverage as scaling crime by a factor near 2 rather than near 5,000, and would draw the opposite conclusion about whether the coverage term dominates.

**Nothing in the C# marks which two of the seven were overridden**, and the two components that agreed completely are the ones a reader would be most tempted to trust after checking one field. The pattern is not "initializers are stale", it is "initializers are unmarked and sometimes catastrophically wrong".

*Rots:* the field lists on all seven prefab classes.

### The `Service buildings` wiki page's schema is this topic's, and its numbers are not borrowable

`Service buildings` (https://cs2.paradoxwikis.com/Service_buildings, fetched 2026-08-11) is the wiki's own gathering of this topic's per-building data. Its groupings are eleven of `CityService`'s thirteen members, under the game's own display names — Communications, Education & Research, Electricity, Fire & Rescue, Garbage Management, Healthcare & Deathcare, Parks & Recreation, Police & Administration, Roads, Transportation, Water & Sewage; it omits `Landscaping`, `Districts` and `Zones`, and this pass did not establish why. Its service-stat columns map one-to-one onto first-party fields:

| wiki column | first-party field |
| --- | --- |
| Service range | `CoverageData.m_Range` (`src/Game/Game.Prefabs/ServiceCoverage.cs:12`) |
| Service capacity | `CoverageData.m_Capacity` (`:14`) |
| Service magnitude | `CoverageData.m_Magnitude` (`:16`) |
| Workplaces | `WorkplaceData.m_MaxWorkers` |
| Max. needed education | the top occupied level of `EconomyUtils.CalculateNumberOfWorkplaces(m_MaxWorkers, m_Complexity, level)` |
| Evening / Night shifts | `WorkplaceData.m_EveningShiftProbability` / `m_NightShiftProbability` |
| Electricity / Water consumption, Garbage accumulation | `ConsumptionData.m_ElectricityConsumption` / `m_WaterConsumption` / `m_GarbageAccumulation` |
| Upkeep per month | the `Resource.Money` line of `ServiceUpkeepData` |
| Patient capacity, Fire engines, Garbage trucks | `HospitalData.m_PatientCapacity`, `FireStationData.m_FireEngineCapacity`, `GarbageFacilityData.m_VehicleCapacity` |

The page carries "At least some were last verified for version 1.5."

**Ruled (2026-08-06, the citizens-and-households pass; conflicts.md).** No mechanics reference borrows the `Service building data test` page's columns, and **none borrows a wiki stat table's numbers at all. First-party or nothing.** The ground is mixture rather than staleness: a table where half the rows are current teaches a reader to trust the other half, and nothing on the page marks which is which. The wiki is a lead generator and never a shipped citation.

**This page's own data belongs to this topic, so the ruling lands here rather than anywhere else, and what the reference owes because of it is this**: the schema above ships — as a map from the concept a reader is looking for to the component and field that holds it — and not one number from either page does. `Service building data test` (https://cs2.paradoxwikis.com/Service_building_data_test), the unlinked pre-launch page last edited 2 August 2023, was not fetched by this pass and is not needed: every column it tabulates that has no counterpart on the live page is already a live 1.6.0f1 concept whose owning field the decompile names — workplace complexity at `src/Game/Game.Prefabs/WorkplaceComplexity.cs:3-8`, the circular geometry flag at `src/Game/Game.Areas/ValidationHelpers.cs:233`, garbage accumulation at `src/Game/Game.Prefabs/ConsumptionData.cs:17`. A reader who wants a number reads it off their own game; the reference hands them the field and the read, and states no figure.

The rest of the `Services` page checks out as orientation and nothing else. Its qualitative statements — healthcare gives a passive health bonus to the surrounding area, fire stations reduce hazard rather than fight fires faster, patrolling cars reduce crime along the roads they travel, police can be limited by district, elementary schools give a wellbeing bonus to nearby families, telecom has both a bandwidth capacity and a range and its signal weakens with distance, parks fulfil leisure demand and carry an attractiveness value — all reproduce in the code cited above. Its one mechanism claim that does not is the coverage-range one (verdict in the range finding).

### Catalog gap: `ruzbeh0/Time2Work` toggles a shared prefab component by scaling it, with no stored flag

The catalog entry's **Demonstrates** (`plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md:188-198`) covers substitution at scale, parameter rewriting after prefab initialization, and "one rewriting a per-prefab component rather than a singleton". It does not name the shape below, which is a different and more hazardous thing than a one-way rewrite.

`Time2Work/NightShift/Systems/SpecialEventLeisureEfficiencySystem.cs` multiplies `Game.Prefabs.LeisureProviderData.m_Efficiency` on a venue's **prefab** by a literal 1000 while a special event runs and divides it back afterwards (`:22` for `EfficiencyBoostFactor`, `:83-103` for the toggle). Whether the boost is currently applied is not stored anywhere: the system infers it from the value's own magnitude, applying only when `m_Efficiency <= 200` and reverting only when `m_Efficiency > 200` (`:91`, `:100`). The write is to a prefab entity, so every instance of that venue in the city changes at once.

**Sentence to add to its Demonstrates entry:**

> Toggling a shared prefab component for the duration of an in-game event by scaling one field and inferring the applied state from the field's own magnitude rather than from a stored flag, which is the failure mode to recognise rather than a pattern to copy: the prefab is shared by every instance, and anything else moving the field past the threshold breaks the toggle.

The hazard is in-session only: a prefab entity's components are rebuilt from the asset on load, so a boost left applied at save time does not persist into the next session. Unconfirmed: that rebuild-on-load rule for a prefab field scaled at runtime — assumed from the prefab system's architecture, never exercised; scaling a field, saving, reloading and re-reading it would settle it.

`Time2Work` is also the corpus's only fork of anything in this topic's neighbourhood: `NightShift/Mod.cs:97` and `:98` disable `Game.Simulation.LeisureSystem` and `Game.Simulation.StudentSystem`, `:109` disables `Game.Simulation.DeathCheckSystem` behind a setting, and `Time2WorkLeisureSystem`, `Time2WorkStudentSystem`, `Time2WorkDeathCheckSystem` and a new `HospitalStaySystem` are registered in their place (`:130-135`). `HospitalStaySystem` is the one that reaches this topic's own vocabulary: it holds a citizen in `Purpose.InHospital` against its own `HospitalStay` component, validating the hospital by `EntityManager.HasComponent<Hospital>` and the citizen by `HealthProblemFlags.Dead` (`Time2Work/NightShift/Systems/HospitalStaySystem.cs:194-214`), and clears the vanilla `HealthProblem` when the stay ends (`:94-100`). The existing entry's "roughly a dozen vanilla simulation systems disabled and replaced" already covers the substitution; the addition above is the pattern it does not name.

### Catalog gap: none found for `bruceyboy24804/InfoLoom`

The entry (`mod-catalog.md:279-293`) already names both patterns this pass found in its budget and district code. `InfoLoom/InfoLoom/Systems/SankeyUISystems/BudgetUISankeySystem.cs:24-80` reads the budget entirely through `ICityServiceBudgetSystem`'s public getters — `GetTotalIncome`, `GetTotalExpenses`, `GetIncome(IncomeSource)`, `GetExpense(ExpenseSource)` — and takes its row grouping from the game's own UI configuration prefab's `m_IncomeItems` / `m_ExpenseItems` rather than inventing one, which the entry's "Taking a panel's display grouping from the game's own UI configuration prefab" and "Reading a vanilla simulation system's published state instead of forking it" lines already cover. `Systems/Sections/ILDistrictSection.cs` reads `CurrentDistrict` per entity and never touches `ServiceDistrict`.

### Catalog sweep: what the rest of the corpus does not have

Swept all 22 repositories for `ServiceCoverage`, `CoverageData`, `CoverageService`, `ServiceDispatch`, `ServiceRequest`, `ServiceBudget`, `CityServiceBudget`, `ServiceFee`, `ServiceDistrict`, `GarbageProducer`, `CrimeProducer`, `MailProducer`, `TelecomFacility`, `HealthProblem`, `FireStation`, `PoliceStation`, `GarbageFacility`, `DeathcareFacility`, `PostFacility`, `EmergencyShelter`, `MaintenanceDepot`, `SchoolData`, `Game.Buildings.School`, `WelfareOffice`, `AdminBuilding`, `LeisureProvider` and `Efficiency`. Beyond the two entries above, the hits are:

- `PlopTheGrowables/Code/Systems/ExistingBuildingSystem.cs` — one incidental reference.
- `InfoLoom/.../IndustrialCompanyDomain/EfficiencyFactorInfo.cs` — a DTO wrapping `Game.Buildings.EfficiencyFactor` for a company panel, plus `WorkforceSystem` and `ILEducationInfoviewUISystem` reading workforce and education state. All read-only and all about companies rather than city services.

**No corpus mod computes coverage, forks `ServiceCoverageSystem`, forks any `*DispatchSystem` or `*PathfindSetup`, writes `ServiceBudgetData`, writes `ServiceFee`, or adds a `CoverageData` to a prefab.** The whole coverage-and-dispatch machinery is untouched in the wild at 1.6.0f1.

### SOURCES gap: none found, and three entries earned a note

`docs/SOURCES.md` named every source this pass used, and no entry was found wrong or stale. Three notes are worth recording as confirmed rather than merely used, because each saved or cost a call:

- **Entry 8 needs no amendment from the budget-buffer episode.** The lesson is about the component's own semantics, not about query reach, so the entry's advice stands as written.
- **Entry 8's "no established route builds an `EntityQuery` inside `eval`" is confirmed again.** `em.CreateEntityQuery(ComponentType.ReadOnly<T>())` fails overload matching against all three signatures with the error text the entry describes. Every count in this file came from `ecs_query` instead.
- **Entry 4's raw byte-grep route works for enumerating a key family and is what settled the leisure question's first half.** `grep -a -o -E "Infoviews\.INFOVIEW\[[A-Za-z ]*\]"` over `Cities2_Data/Content/Game/Locale.cok` returned 36 distinct info-view names with no decoding at all, `Leisure` among them, which is exactly the use the entry prescribes ("use it to settle whether a key exists and to enumerate a key family").

Entry 3's line-count check on the reformatted bundle passed (135,021).

---

## Bridge

### Techniques a change here needs

**`ecs-in-this-game`** — every read in this topic is one of that reference's idioms. `GetSingleton<T>()` is the route for every parameter singleton in the map above. The `Efficiency` buffer is the case that reference's "an enableable component's `enabled` is state" caution generalises to: a factor at exactly 1 is *absent* rather than present-and-1, so a presence test is the wrong read and `BuildingUtils.GetEfficiencyFactor` is the right one (`src/Game/Game.Buildings/BuildingUtils.cs:249-259`). `CoverageServiceType` is a **shared** component (`src/Game/Game.Net/CoverageServiceType.cs:6`), which `ProcessCoverageJob`'s chunk-level filter reads with `chunk.GetSharedComponent(...)` and skips the whole chunk on a mismatch (`ServiceCoverageSystem.cs:121-124`) — the chunk-level early exit that reference names as what a per-entity job cannot do. The `UpdateFrame` shared component plus `RequestGroup`'s one-time random draw (`ServiceRequestSystem.cs:32-44`) is the topic's own version of the sixteen-way bucketing that reference describes.

**`prefabs-and-assets`** — making a building cover, dispatch or charge means adding a `ComponentBase` subclass to a `BuildingPrefab`, and every one of them splits `GetPrefabComponents` from `GetArchetypeComponents` in a way that decides whether the data lands on the prefab entity or the instance archetype: `ServiceCoverage` puts `CoverageData` on the prefab and `CoverageServiceType` + `CoverageElement` on the instance (`src/Game/Game.Prefabs/ServiceCoverage.cs:18-27`). `IServiceUpgrade.GetUpgradeComponents` is the third arm, and every service class implements it differently — `FireStation` and `PoliceStation` refuse to add their runtime component to an upgrade that also carries a `ServiceCoverage` (`src/Game/Game.Prefabs/FireStation.cs:51-58`, `src/Game/Game.Prefabs/PoliceStation.cs:56-67`). `UpgradeUtils.CombineStats` and `ICombineData` are what fold an installed upgrade's capacity into the parent's before use. And the initializer trap belongs to both references.

**`mod-lifecycle-and-ordering`** — the ordering is load-bearing and readable: `ServiceRequestSystem` and `CityServiceEfficiencySystem` at `ModificationEnd` (`SystemOrder.cs:270`, `:292`) with `CityServiceBudgetSystem` `UpdateAfter` in the same phase (`:301`), then the whole `GameSimulation` run of coverage, vehicle AI, facility AI and dispatch (`:306`, `:315-335`, `:521-541`, `:554-568`). The band order is what makes that load-bearing: `ModificationEnd` is driven from the main loop and runs **ahead of** the frame's simulation steps, which that reference's nesting tree establishes — so a `HandleRequest` a dispatcher or a vehicle creates during `GameSimulation` is not reconciled until the *next* frame's `ModificationEnd`, and a system reading `Dispatched` from `GameSimulation` always reads an answer at least one frame old.

**`performance-and-memory`** — this topic is the game's worked example of amortising a global pathfind. Eight services rotate through a 256-frame cycle, searches are enqueued 192 at a time with a 64-frame lead (`ServiceCoverageSystem.cs:537-538`, `:625-626`), and the apply pipeline allocates `Allocator.TempJob` lists disposed on the final job handle (`:559-560`, `:604-606`). `ApplyCoverageJob` is a single-threaded `IJob` doing a global sort-and-merge, which is why the capacity mechanism exists at all.

**`save-serialization`** — `ServiceRequest`, `Dispatched`, `ServiceBudgetData` and `ServiceFee` all carry hand-written version bands: `ServiceRequest` reads its flags byte only from `Version.reverseServiceRequests` (`src/Game/Game.Simulation/ServiceRequest.cs:30-34`), `Dispatched` discards a `uint` written before `Version.dispatchRefactoring` (`src/Game/Game.Simulation/Dispatched.cs:19-22`), `ServiceBudgetData` discards an `int` written before `Version.serviceImportBudgets` (`:18-21`), and `ServiceFee` rewrites the water fee to a literal `0.3f` below `Version.waterFeeReset` and re-defaults the garbage fee below `Version.garbageFeeReset` (`src/Game/Game.City/ServiceFee.cs:34-41`). `Game.Serialization.ServiceCoverageSystem` (`SystemOrder.cs:838`) is a pure buffer-growth migration.

**`diagnostics`** — the topic's first-party diagnostic surface is a scatter of one-line warnings, not a channel. Those found: `ComponentBase.baseLog.ErrorFormat(prefab, "Unknown coverage service type: {0}", …)` when a prefab carries `ServiceCoverage` and no service data component (`src/Game/Game.Prefabs/ServiceCoverage.cs:71`), `PathfindSetupSystem`'s `"Invalid target type in Pathfind setup "` warning in the `default:` arm of the dispatcher every `*PathfindSetup` routes through (`src/Game/Game.Simulation/PathfindSetupSystem.cs:553`) — the line a custom dispatch kind's out-of-range `SetupTargetType` hits — `WorkProviderSystem`'s `"Worker {id} had incorrect TravelPurpose {n}!"` warning in `RemoveWorker` (`src/Game/Game.Simulation/WorkProviderSystem.cs:361`), and `LeisureSystem`'s zero-efficiency-provider and type-randomization warnings (`src/Game/Game.Simulation/LeisureSystem.cs:396`, `:553`); the reach behind this list is a `LogWarning|ErrorFormat` grep over the systems this file names, not a census of `src/Game/`, which is why the shipped bridge states the shape and names only the two a modder most likely hits. Otherwise the notification prefabs each parameter singleton holds. There is no solver error and no per-request log; a request that never gets served backs off silently to a 255-tick retry.

**`patching`** — nothing here needs Harmony. The budget is a public getter/setter pair on a managed system, the fees are an ordinary buffer on the city entity, coverage is an ordinary buffer on an edge, and every dispatcher's state is an unmanaged component. The one seam that is not public is the coverage pathfind itself, which a mod extends by giving a prefab `ServiceCoverage` rather than by patching anything.

**`navigating-the-decompile`** — the topic's material is split across five namespaces by role rather than by subject, and finding it from the word "coverage" requires knowing that: `Game.Prefabs` for the authoring class and its data component, `Game.Buildings` for the instance component and the efficiency machinery, `Game.Simulation` for the AI, dispatch and coverage systems, `Game.Pathfind` for the search that computes coverage, and `Game.Net` for where the answer is stored. The one genuinely misleading name is `Game.Simulation.ServiceCoverageSystem` against `Game.Serialization.ServiceCoverageSystem` — same short name, different namespaces, and the second is a five-line buffer-growth migration.

### Adjacent mechanics topics

**`citizens-and-households`** owns the demand side of almost everything here. `HealthProblem` and its flags, `Game.Citizens.Student`, `Game.Citizens.Leisure` and the leisure counter, household income (which the healthcare-fee term reads), and the whole of `CitizenHappinessSystem` are theirs; what this topic owns is the supply that those systems read — the coverage value on the road edge, the school's capacity and graduation modifier, the hospital's treatment bonus. The boundary is the `NetUtils.GetServiceCoverage` call: producing the number is this topic's, consuming it is theirs.

**`zoning-buildings-and-land-value`** owns `LandValueSystem` and `LandValueParameterData`; this topic owns the three coverage terms that feed it (`LandValueSystem.cs:186-191`) and `PropertyUtils.GetPropertyScore`'s dependence on the same buffer. `SpawnableBuildingData.m_Level` is theirs and enters this topic twice — through the education kernel's `buildingLevel` argument and through the fire-hazard level scaling (`EventHelpers.cs:52`).

**`utilities-and-flow-networks`** shares the whole building-side vocabulary: `ConsumptionData` carries `m_GarbageAccumulation` and `m_TelecomNeed` beside the two utilities, `Efficiency`/`EfficiencyFactor` carries six utility factors among 32, `ServiceUsage` and `ServiceFee` and `CityServiceBudgetSystem` are shared, and `BuildingEfficiencyParameterData` is one singleton both references map. The line the structure draws holds cleanly in the code: a service that solves a max-flow graph is theirs, a service that reaches by pathfind coverage or by dispatched vehicle is this topic's, and no system appears in both lists. `CityServiceUpkeep` is the tag that marks a building as budget-bearing for either.

**`transportation-and-vehicles`** owns every vehicle **as an object** — the physical and payload `*Data` components, the `Odometer` → `m_MaintenanceRange` → `RequiresMaintenance` → depot loop, `VehicleSideEffectData` and the energy-type masks — and the five transit-and-taxi `*AISystem`s. The ten service-vehicle `*AISystem`s in the `SystemOrder.cs:315-336` block — `Ambulance`, `GarbageTruck`, `FireEngine`, `PoliceCar`, `MaintenanceVehicle`, `PostVan`, `FireAircraft`, `PoliceAircraft`, `MedicalAircraft`, `Hearse` — are **this topic's**: they drive its dispatches and report back with `HandleRequest`. `TransportDepot` is theirs despite carrying `ServiceDispatch` (and `ServiceDistrict` when it is a taxi depot, `src/Game/Game.Prefabs/TransportDepot.cs:48-52`); `ParkingFacility` is theirs and carries neither.
**Ruled (2026-08-16, the transportation-and-vehicles pass, made by the orchestrating session under the maintainer's delegated authority for that pass; conflicts.md).** The paragraph above is the ruled form: the ten service-vehicle `*AISystem`s are this topic's, not `transportation-and-vehicles`'s. Ownership follows the vehicle's role, not the registration block: what a service vehicle does after the request is the second half of its service's story, so a reader follows an ambulance from request to hospital without crossing a reference boundary. What this topic's reference owes because of it: its bridge line may not defer the ten systems to the sibling — that shipped line was corrected with this ruling — and whether it says more about them than its dispatch coverage already does is its ordinary depth call.

**`roads-and-traffic`** owns the substrate that coverage is stored on and searched over: `Game.Net.Edge`, `Curve`, `Density`, `EdgeLane`, `BorderDistrict`, and the pathfind graph `CoverageJobs` walks. Which roads exist and how dense they are is theirs; what is stored on them by this topic's rotation is this topic's. The `RoadPrefab.m_ZoneBlock` gate that decides whether an edge carries a `ServiceCoverage` buffer at all (`src/Game/Game.Prefabs/RoadPrefab.cs:65-72`) is a fact both references need.

**`environment-and-pollution`** owns the disaster events this topic's disaster control responds to — `Game.Events.WeatherPhenomenon`, `DangerLevel`, the lightning and flood machinery in `WeatherPhenomenonSystem` — and the fire event itself. The boundary the ticket draws holds: the event is theirs, the dispatched response (fire station disaster capacity, emergency shelter, early warning) is this topic's. `FireHazardSystem` sits on the line, and the coverage term inside `EventHelpers.GetFireHazard` is what puts it on this side.

**`economy-and-companies`** owns `ServiceFeeSystem`'s money half, `EconomyParameterData.GetWage`, `EconomyUtils.GetMarketPrice`, `TaxSystem` and `LoanSystem` — all of which `CityServiceBudgetSystem`'s income and expense enumeration calls into (`CityServiceBudgetSystem.cs:142-254`). The budget panel is a joint surface: the slider and the upkeep are this topic's, the fifteen `ExpenseSource` and fourteen `IncomeSource` members are theirs.

**`city-state-and-progression`** owns `Locked`, `UnlockRequirement`, `CityModifier`, `CityEffectProvider` and the XP economy — which is where an administration building's entire contribution lives, since `AdminBuilding` itself is an inert tag. It also owns the gate on this topic's happiness bonuses: three of `CitizenHappinessSystem`'s coverage bonuses return zero while the service prefab carries an enabled `Locked` (`:1178`, `:1191`, `:1203`).

**`simulation-time-and-units`** owns `TimeSystem.kTicksPerDay = 262144`, `SimulationSystem.frameIndex` and `SimulationUtils.GetUpdateFrame`, which are the substrate of every interval in the cadence table and of the 256-frame coverage rotation.

---

## Dead ends

- **`Game.Buildings.AdminBuilding` and `Game.Prefabs.DisasterFacilityData` are simulated by nothing.** A grep of `src/` for each returns `MarkerCreateSystem`, `ObjectColorSystem` and `InfoviewInitializeSystem` and nothing else — notification markers, info-view colouring and info-view building-type classification, and `InfoviewInitializeSystem` matches only the prefab-side `*Data` twin, never the instance tag (`src/Game/Game.Prefabs/InfoviewInitializeSystem.cs:177`, `:309`). Do not look for an administration mechanic or a disaster-facility mechanic; administration is upkeep plus workplaces plus whatever `CityEffects` the prefab carries, and disaster response runs through `FireStationData.m_DisasterResponseCapacity` and `EmergencyShelter`.
- **There is no `LeisureInfoviewUISystem` and no leisure binding group.** The infomode composition was read live rather than derived, because the infoview→infomode mapping is authored asset data that no static read reaches.
- **`CoverageService.PostService` and `CoverageService.EmergencyShelter` coverage is read by nothing.** Searched every `CoverageService.` reference in `src/Game/` outside the declaring files, plus the three raw indexed reads in `LandValueSystem`. Recorded as a finding rather than only here, because a reader will find the values in the buffer and needs a positive correction.
- **`ModifiedServiceCoverage.m_Range` reaches no consumer.** Searched all seven files referencing the component plus both preview systems. The coverage search reads the prefab's `CoverageData.m_Range`; the process job reads only magnitude and capacity out of the replaced struct. Recorded in the park finding.
- **`PlayerResource.FireResponse` and `PlayerResource.Police` are charged by nothing.** A grep of the whole `Game` assembly returns the enum member, `ServiceFeeParameterData`'s field and `GetFeeParameters`/`GetDefaultFees` arms, `ServiceFeeParameterPrefab` and `ServiceFeeParameterMode`, and one `switch` arm in `SendTradeResourceTrigger`. No consumption multiplier, no efficiency factor, no happiness effect, no billing site.
- **`em.CreateEntityQuery(...)` does not work in `eval`.** Do not spend a call re-testing it.
- **An empty `ServiceBudgetData` buffer is not a missing one.** A plain `ecs_query` found the singleton (entity 98443) with a zero-length buffer, which read like the wrong entity and is in fact the untouched-sliders state: entries appear only when `SetServiceBudget` first writes one, and `GetServiceBudget` supplies the 100 defaults. Recorded in the parameter map; do not go hunting for a second, system-owned entity.
- **The live city has no districts.** `ecs_query` on `Game.Areas.District` returned 0, and the one `ServiceDistrict` buffer sampled out of 35 carriers was length 0, so nothing in the district-scoping finding was exercised live. It is all decompile evidence and is marked as such rather than as verified behaviour. The cheap way to close it is to ask the user to draw one district and assign one station to it, then re-read a border road's `ServiceCoverage` for the 0.5 factor.
- **The `Service building data test` wiki page was not fetched.** The ruling forbids borrowing its columns and the live `Service buildings` page supplied the schema comparison, so opening it would have added nothing the ruling allows this pass to use. Recorded so the next pass does not spend the fetch.
- **No `.cok` or `resources.assets` decode was attempted.** Every prefab value in this file was read from the running game, which `docs/SOURCES.md` entry 5 names as the shorter road for base-game prefabs. The one packaged-content read this pass did make was a raw byte-grep of `Locale.cok` for the info-view key family, which needed no decoder.
- **The simulation was paused for the whole live session, at the same frame as the utilities pass (8,435,350).** Two consequences the reader should know: no coverage rotation advanced, so every `ServiceCoverage` value read is whatever the save carried, and no `ServiceRequest` cooldown ticked. Nothing in this file rests on an observed state *change*; every live reading is a snapshot of a stored value.
