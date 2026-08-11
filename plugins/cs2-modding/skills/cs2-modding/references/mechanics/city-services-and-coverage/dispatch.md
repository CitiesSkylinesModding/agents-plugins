# Service dispatch

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Every dispatched service — ambulance, hearse, fire engine, patrol car, garbage truck, post van, maintenance vehicle — runs one protocol: a request entity is created by whatever detects the need, a per-service dispatch system asks the pathfinder for the cheapest eligible source, and `ServiceRequestSystem` reconciles the reports.
A request kind is one payload component named `<X>Request` beside `Game.Simulation.ServiceRequest`; list `src/Game/Game.Simulation/*Request.cs` for the set, minus `ServiceRequest.cs` and `HandleRequest.cs` themselves.

## Request lifecycle

Source: `src/Game/Game.Simulation/ServiceRequestSystem.cs`, `src/Game/Game.Simulation/SimulationUtils.cs`, `src/Game/Game.Simulation/RequestGroup.cs`.

```
UpdateRequestGroupJob (every new request):
  index = random.NextUInt(RequestGroup.m_GroupCount)     // drawn once, per request
  remove RequestGroup; add shared UpdateFrame(index)

HandleRequestJob (ModificationEnd), per reported request, all HandleRequests collapsed first
                 (the first report is kept; a later Completed replaces it wholesale, a later
                  PathConsumed merges only that flag, any other later report is discarded):
  skip                                   when the request no longer has ServiceRequest
  destroy the request                    when m_Completed
  else when m_Handler != Null:                                    // accepted
    request already Dispatched  -> replace Dispatched(m_Handler); m_Cooldown = 0
    else                        -> add Dispatched(m_Handler)      // cooldown NOT zeroed
    when m_PathConsumed         -> remove PathInformation, PathElement
  else when the request has Dispatched:                           // failed
    ResetFailedRequest; remove PathInformation, PathElement, Dispatched

ResetFailedRequest:  m_FailCount = min(255, m_FailCount + 1)
                     m_Cooldown  = (1 << min(8, m_FailCount)) - 1
ResetReverseRequest: same, but m_Cooldown = max(4, ...)
TickServiceRequest:  ready = (m_Cooldown == 0) | SkipCooldown
                     m_Cooldown = max(0, m_Cooldown - 1); clear SkipCooldown
```

So a request that keeps failing waits 1, 3, 7, 15, 31, 63, 127, then 255 update ticks between attempts, forever.
Nothing logs a failing request; the backoff is the only trace.

**A dispatch system enqueues a request's path searches four `UpdateFrame` slots before it consumes the results.**
`HealthcareDispatchSystem` is the pattern every `*DispatchSystem` follows: the chunk whose `UpdateFrame` index equals `(current + 4) & mask` gets its searches enqueued (skipped entirely while the chunk has `Dispatched` or `PathInformation`), and the chunk matching the *current* index consumes the results — the pathfinder's headroom; for a four-group request kind the wrap makes enqueue and consume the same chunk, one whole cycle apart.
Source: `src/Game/Game.Simulation/HealthcareDispatchSystem.cs`, `src/Game/Game.Simulation/FireRescueDispatchSystem.cs`.

## The dispatch cost

A dispatch is a two-ended pathfind, never a distance sort: the dispatch system enqueues a `SetupQueueItem(request, parameters, origin, destination)`, the matching `*PathfindSetup` job seeds every eligible source with a starting cost, and the pathfinder returns one `PathInformation` naming the cheapest origin, which `DispatchVehicle` accepts.
Source: `src/Game/Game.Simulation/HealthcareDispatchSystem.cs`, `src/Game/Game.Simulation/HealthcarePathfindSetup.cs`.

```
FindVehicleSource (ambulance):         weights (time, behaviour, money, comfort) = (1, 0, 0, 0)
                                       m_MaxSpeed = 277.77777, methods Pedestrian|Road|Flying|Boarding
FindVehicleSource (hearse):            weights (1, 1, 1, 1), m_MaxSpeed = 111.111115

SetupAmbulancesJob, per source:
  return                               when the chunk is an OutsideConnection and
                                       !CityOption.ImportOutsideServices           // whole-chunk gate
  hospital:
    roadTypes  = Car        if HospitalFlags.HasAvailableAmbulances
               | Helicopter if HospitalFlags.HasAvailableMedicalHelicopters
               , only when AreaUtils.CheckServiceDistrict(district, hospital) passed
    roadTypes &= what the request's target accepts
    seed cost  = weights.time * 10f     when any road type survives
  ambulance already on the road:
    only when flagged Returning and neither Dispatched, Transporting nor Disabled
    (or a parked one not Disabled), and its owner station passes the same district check
    seed cost  = 0f
```

**An emergency is a weight vector, not a priority number** — the ambulance weighs time alone; the hearse on the same system weighs all four.
**A station starts ten cost units behind any free vehicle, parked or returning**, which is what makes the game re-task a returning vehicle before sending a fresh one.
`FirePathfindSetup`, `PolicePathfindSetup`, `GarbagePathfindSetup` and `PostServicePathfindSetup` repeat the shape with their own flags; `FirePathfindSetup` additionally refuses a station without `DisasterResponseAvailable`, and an engine without `FireEngineFlags.DisasterResponse`, for a `FireRescueRequestType.Disaster` request.
Source: `src/Game/Game.Simulation/HealthcarePathfindSetup.cs`, `src/Game/Game.Simulation/FirePathfindSetup.cs`, `src/Game/Game.Simulation/PolicePathfindSetup.cs`, `src/Game/Game.Simulation/GarbagePathfindSetup.cs`, `src/Game/Game.Simulation/PostServicePathfindSetup.cs`.

## District scoping

`AreaUtils.CheckServiceDistrict` is the rule, in three overloads:

```
no buffer, or an empty buffer          -> true    (serves everywhere)
buffer non-empty, target district Null -> false   (excludes anything in no district)
otherwise                              -> the buffer contains that district

the overloads differ at the edges: the border-road one (two districts) fails only
when BOTH sides are Null and passes when either is in the buffer, and the building
one passes any building carrying no CurrentDistrict at all
```

Source: `src/Game/Game.Areas/AreaUtils.cs`.

**Which buildings can be assigned to districts at all is decided per prefab class, not per service.**
Each service's `GetArchetypeComponents` decides whether to add the `ServiceDistrict` buffer, and each gates it differently: `Hospital` and `FireStation` gate it on `GetComponent<UniqueObject>() == null` — a unique landmark station gets no assignment — while `PostFacility` adds it only when `m_PostVanCapacity > 0`, `TransportDepot` only for taxi depots, and `MaintenanceDepot` and `TelecomFacility` never, so a maintenance depot cannot be district-restricted even though `RoadPathfindSetup` calls the check.
Source: `src/Game/Game.Prefabs/Hospital.cs`, `src/Game/Game.Prefabs/FireStation.cs`, `src/Game/Game.Prefabs/PostFacility.cs`, `src/Game/Game.Prefabs/TransportDepot.cs`, `src/Game/Game.Prefabs/MaintenanceDepot.cs`, `src/Game/Game.Prefabs/TelecomFacility.cs`, `src/Game/Game.Simulation/RoadPathfindSetup.cs`.

## Imported services

An outside connection is an ordinary service building wearing a `Game.Objects.OutsideConnection` tag — the same dispatch machinery, gated by the chunk-level city-option check above.
The bill is an expense line per population rather than a fee:

```
per imported dispatched service, only while CityOption.ImportOutsideServices is on:
  importFee = m_<X>ImportServiceFee                                // OutsideTradeParameterData
  importFee = ApplyModifier(importFee, CityModifierType.CityServiceImportCost)
  expense   = importFee * (population / m_OCServiceTradePopulationRange + 1)
                        * m_OCServiceTradePopulationRange          // a population step
                                                                   // function, truncated to int
```

with one such getter for each of the five dispatched imports, accumulated as a positive figure against its `ExpenseSource.Import*` member; imported electricity and water bill differently, through `ServiceFeeSystem.GetServiceFees`.
Source: `src/Game/Game.Simulation/CityServiceBudgetSystem.cs`, `src/Game/Game.Prefabs/OutsideTradeParameterData.cs`.

## Fleet size

One helper connects efficiency to everything a dispatcher can physically field:

```
GetVehicleCapacity(efficiency, capacity)
  = 0                                            when efficiency <= 0.001 or capacity <= 0
  = clamp((long)(efficiency * capacity), 1, capacity)   otherwise
```

**A working building always fields at least one vehicle, and a building at zero efficiency fields none.**
`FireStationAISystem` is the pattern: what can be fielded from the yard is sized with `min(efficiency, immediateEfficiency)`, while vehicles already out are counted against `GetVehicleCapacity(immediateEfficiency, capacity)` and flagged `FireEngineFlags.Disabled` beyond that count.
`GetImmediateEfficiency` multiplies only the `Destroyed`, `Abandoned`, `Disabled` and `ServiceBudget` factors — **a budget cut recalls vehicles already on the road at once, while a staffing or supply problem only shrinks what gets fielded next, through the slower full product**.
The vehicles themselves get a work-rate term, `FireStationData.m_VehicleEfficiency * (0.5 + efficiency * 0.5)`, stamped onto the engine at each dispatch — a parked engine is re-stamped when sent out again, so only a trip in progress keeps an old rate — and read as its extinguishing rate; a station at the 0.01 efficiency floor fields its one engine at essentially half its `m_VehicleEfficiency`.
Source: `src/Game/Game.Buildings/BuildingUtils.cs`, `src/Game/Game.Simulation/FireStationAISystem.cs`, `src/Game/Game.Simulation/FireEngineAISystem.cs`.

(VOLATILE: every component, flag enum, system, method and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Buildings`, `Game.Areas`, `Game.Prefabs`, `Game.Vehicles`, `Game.City`, `Game.Pathfind` and `Game.Objects`, at the files each listing and trap cites; the backoff shifts, weight vectors, speed constants and the `10f` seed are literals in those same files.)
