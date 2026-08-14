# Travel weights and trip ceilings

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Every pathfind request carries a `PathfindWeights` — the four axes an edge's cost four-vector is dotted against.

**The constructor is positional `(time, behaviour, money, comfort)`, and only `time` and `money` have named properties.**
`behaviour` is `.y` and `comfort` is `.w`, reachable only through `m_Value`; the authoring struct `PathfindCostInfo` names all four in full.
Source: `src/Game/Game.Pathfind/PathfindWeights.cs`, `src/Game/Game.Prefabs/PathfindCostInfo.cs`.

## Citizens

Source: `src/Game/Game.Citizens/CitizenUtils.cs`.

```
CitizenUtils.GetPathfindWeights(citizen, household, householdCitizens):
  time      = 5 * (4 - 3.75 * citizen.m_LeisureCounter / 255)
  behaviour = 2                                              // every citizen
  money     = 2500 * max(1, householdCitizens)
                   / max(250, household.m_ConsumptionPerDay)
  comfort   = 1 + 2 * citizen.GetPseudoRandom(TrafficComfort).NextFloat()
  money    *= 0.1  when the household is not MovedIn and the citizen is not
                   MovingAwayReachOC / Tourist / Commuter
```

This is the only computation of a citizen's weights, its three parameters are all it reads, and every call site passes the same three — the weighting does not vary by age.
The one exception is not a computation: a citizen travelling under `Purpose.EmergencyShelter` has the result overwritten with a literal profile after the call, at the walking, personal-car, taxi and general-trip sites.
What the computed weights vary by: leisure debt (a leisure-starved citizen, `m_LeisureCounter` low, is the one in a hurry — time runs from 20 down to 1.25), per-capita household consumption inverted (high consumption per head means a small money weight), one per-citizen random draw fixed for life in `[1, 3)`, and house-hunting status (the tenfold money reduction while choosing a home).

## Everyone else

A non-citizen request writes its weights as a literal, and the sweep — grep `new PathfindWeights(` under `src/Game/` — resolves to four recurring profiles plus one single-site oddity, the tourist attraction target's `(0.1, 0.1, 0.1, 0.2)` (`src/Game/Game.Simulation/TouristFindTargetSystem.cs`):

- `(1, 1, 1, 1)`, the neutral profile: every routine service, dispatch, transport and delivery system, route pathfinding, and the coverage and tool-preview analyses — `GarbageCollectorDispatchSystem` is a representative site (`src/Game/Game.Simulation/GarbageCollectorDispatchSystem.cs`).
- `(1, 0, 0, 0)`, the emergency profile: fire and police dispatch, ambulance dispatch (a hearse request is neutral), those vehicles on the urgent leg — outbound, or carrying a critical patient in — and a citizen's own trip to an emergency shelter at the general-trip site alone — `FireRescueDispatchSystem` is a representative site.
- `(1, 0.2, 0, 0.1)`, the urgent profile: evacuation dispatch, an evacuating bus, and the `Purpose.EmergencyShelter` override on a walking, driving or taxi-riding citizen.
- `(0.01, 0.01, transportCost, 0.01)`, the freight profile: buying, exporting and storage transfer, where `transportCost = EconomyUtils.GetTransportCost(…)` for the actual load (`src/Game/Game.Simulation/ResourceBuyerSystem.cs`).

A zero weight erases every cost in that component, so the emergency profile prices a route by time alone whatever the manoeuvre penalties — and the emergency dispatch sites also set `m_IgnoredRules` to the union of all six restriction rules, stripping the behaviour accrual too; the vehicles' own AI systems ignore smaller, prefab-dependent rule sets.
That pair is the whole of "emergency vehicles drive dangerously": no separate model, and the vehicle switches back to neutral weights for the trip home.
The freight profile makes the load's own haulage cost the money weight, so a heavier or larger consignment weighs distance proportionally harder — selling locally is cheaper by construction.

## Trip ceilings

A search abandons once its running total passes `PathfindParameters.m_MaxCost`, with 0 meaning no limit (`src/Game/Game.Pathfind/PathfindJobs.cs`).
Two ceilings feed it:

- `CitizenBehaviorSystem.kMaxPathfindCost = 17000f`, a `public static readonly` C# value, used by `PersonalCarAISystem`, `ResidentAISystem` and `TaxiAISystem` (`src/Game/Game.Simulation/CitizenBehaviorSystem.cs`).
- `TripPriorityParametersData.GetMaxCost(priority) = m_BaseMaxCost * priority / 128`, where `GetPriority(purpose, citizen)` maps each `Purpose` to one of the singleton's priority bytes, defaulting to 128, with `Purpose.Leisure` alone interpolated: `round(lerp(m_LeisurePriorityMin, m_LeisurePriorityMax, 1 - m_LeisureCounter / 255))` (`src/Game/Game.Prefabs/TripPriorityParametersData.cs`).

The bytes and the base are prefab data on the `TripPriorityParametersData` singleton — `GetSingleton` reads them, and read live they rank going home and moving away above work and school, and those above shopping, tourism and drained leisure.
Every trip takes its ceiling plain — `TripNeededSystem` applies `GetMaxCost(priority)` unmodified for every `Purpose` (`src/Game/Game.Simulation/TripNeededSystem.cs`); the 1.1 multiplier lives in the job- and school-seeker target searches, not in any trip, and the home-, shop- and leisure-target searches bound themselves by the same bytes unmultiplied (`FindJobSystem.cs`, `FindSchoolSystem.cs`, `HouseholdFindPropertySystem.cs`, `ResourceBuyerSystem.cs`, `LeisureSystem.cs`).

(VOLATILE: every component, field, enum, system, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Pathfind`, `Game.Citizens`, `Game.Prefabs` and `Game.Simulation`, at the files cited beside each; the weight-profile shape, against the `new PathfindWeights(` sweep it states; the citizen listing, against `CitizenUtils.cs`.)
