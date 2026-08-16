# Transit in the routing graph

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

## The line's own route is planned congestion-blind and deterministically

`RoutePathSystem` (`SystemUpdatePhase.ModificationEnd`) issues one pathfind per route segment (`src/Game/Game.Routes/RoutePathSystem.cs`):

```
m_MaxSpeed      = 277.77777f          // the 1000 km/h sentinel
m_WalkSpeed     = 5.555556f
m_Weights       = (1, 1, 1, 1)
m_PathfindFlags = Stable | IgnoreFlow
m_IgnoredRules  = HasBlockage | ForbidCombustionEngines | ForbidHeavyTraffic
                | ForbidPrivateTraffic | ForbidSlowTraffic | AvoidBicycles
                (+ ForbidTransitTraffic unless the route is a Car road route)
m_Methods       = RouteUtils.GetPathMethods over the route's RouteConnectionData
```

Three consequences: `Stable` removes the per-edge random multiplier `roads-and-traffic` records, so two identical lines take the same path; `IgnoreFlow` makes the planned duration a free-flow duration; and the rule union means a line's route ignores blockages, the combustion-engine and heavy-traffic district options, and the private-traffic, slow-traffic and bicycle-avoidance lane rules — while a bus line, the Car road case the listing carves out, still pays a district's transit-traffic ban.
A mod that wants transit to route around congestion changes these flags, not the weights — measured congestion reaches transit only through `VehicleTiming.m_AverageTravelTime` and the `RouteInfo` scaling below.
`RoutePathSystem.SetupPathfind` is private, so reaching this decision is `patching`'s — and the edge builders below, though `public static` and pure, are called only from `[BurstCompile]` jobs in `RoutesModifiedSystem` and `LanesModifiedSystem`, so a patch on one is inert with Burst on and the seam is the managed schedule in those two systems.

## What a transit leg costs

Sources: `src/Game/Game.Pathfind/PathUtils.cs`, `src/Game/Game.Prefabs/PathfindTransportData.cs`.

**The stop edge** — `PathUtils.GetTransportStopSpecification` builds a zero-length edge, `Forward` only while the stop is `Active`, `Backward` when built for a stop rather than a waypoint, `AllowEnter` from `StopFlags`, `AllowExit` from the presence of an access restriction, methods from the `m_PassengerTransport` / `m_CargoTransport` pair, then:

```
stopDuration    = RouteUtils.GetStopDuration(lineData, stop)
wait            = max(line.m_VehicleInterval * 0.5, m_AverageWaitingTime) - stopDuration
startingCost.x  = max(0, startingCost.x + wait)      // time
startingCost.z *= line.m_TicketPrice                 // money
startingCost.w *= 1 - stop.m_ComfortFactor           // comfort
```

Three things a reader acts on: the expected wait has a floor of half the headway, so a measured wait below it never lowers the routing cost — only a shorter stored headway does, and [lines-and-fleet.md](lines-and-fleet.md) owns how `m_VehicleInterval` is computed; the money term is a multiplication, so a zero-price line costs nothing in money whatever the pathfind prefab declares; and a perfectly comfortable stop costs nothing in comfort.

**The line edge** — `GetTransportLineSpecification`:

```
m_Length   = routeInfo.m_Distance
m_MaxSpeed = max(1, routeInfo.m_Distance) / max(1, routeInfo.m_Duration)
Forward    only when distance and duration are both positive
methods    = on a m_PassengerTransport line: PublicTransportDay and Night, each
             dropped by its own inactive flag; on a m_CargoTransport line:
             CargoTransport, dropped only when both inactive flags are set
costs     += m_TravelCost * routeInfo.m_Distance
```

The edge's speed is the line's own measured distance over duration, and `TransportLineSystem` rewrites each segment's `RouteInfo.m_Duration` as the planned duration scaled by `max(1, (max(planned leg, VehicleTiming.m_AverageTravelTime) + the stop's dwell) / planned leg)` — the dwell rides only in the numerator, so a line nobody delays still scales above 1, and measured-faster-than-planned never shortens the stored duration — stamping `PathfindUpdated` on the segment when it moves — **that scale factor is congestion's route into the line edge, and through the recomputed headway it reaches the stop edge's wait floor too**, correcting a path that was planned with flow ignored ([lines-and-fleet.md](lines-and-fleet.md) computes the inputs).

**The taxi edges** — `GetTaxiStopSpecification` is the stop shape with `PathMethod.Taxi`, the raw average wait (no headway floor, since a taxi has no headway) and `startingCost.z *= taxiStand.m_StartingFee`; `GetTaxiDriveSpecification` is a car-lane edge that multiplies the travel cost's money component by `0.03f` — `RouteUtils.TAXI_DISTANCE_FEE`, inlined — before scaling by length, so a taxi ride accrues money per metre.
`m_OrderingCost` is consumed at one pathfind edge — the parking-lane taxi-availability edge, with `GetTaxiAvailabilityDelay` added to its time component — and read a second time by `ResourceAvailabilitySystem` to estimate taxi response time (`src/Game/Game.Simulation/ResourceAvailabilitySystem.cs`).

**`TransportPathfind`'s field initializers are the initializer trap at its worst.**
Read live, the comfort component of the class's initialized `m_StartingCost` is authored larger on every shipped line-mode pathfind prefab while its three neighbours survive — a plausible wrong number in one slot of four — and `m_TravelCost`, the per-metre term the line edge scales by `routeInfo.m_Distance`, diverges from the class too; read both off `Game.Prefabs.PathfindTransportData` with `ecs_query` rather than off the class, since where `m_TravelCost` is authored away the line edge adds no cost of its own and everything beyond the ride's own travel time comes from the per-stop `m_StartingCost` — that query is the re-check.
Source: `src/Game/Game.Prefabs/TransportPathfind.cs`, `src/Game/Game.Prefabs/PathfindTransportData.cs`.

## Method selection, day and night

`RouteUtils.GetPublicTransportMethods` returns `PathMethod.PublicTransportDay` inside `[0.25, 11/12)` of the normalized day and `PublicTransportNight` outside it, after offsetting the query time by a `predictionOffset` defaulting to `1f / 48f`; the resident overload, `GetTaxiMethods`, and `GetPathMethods`' own default arm — any connection type outside pedestrian, road, air and track — are the three selectors that can refuse (`src/Game/Game.Routes/RouteUtils.cs`).

**A refusing selector returns the complement of the whole `PathMethod` mask, and reading it as a wide mask is exactly backwards.**
`PathMethod` fills all sixteen bits of its `ushort`, so the complement of the sixteen members is literally `(PathMethod)0` — the game's idiom for "no method", written as a complement.
Source: `src/Game/Game.Routes/RouteUtils.cs`, `src/Game/Game.Pathfind/PathMethod.cs`.

## The heuristic feed

`TransportLineSystem` keeps a persistent two-element array — passenger, cargo — reset to zero each tick and raised to the maximum per-leg speed over every line of the matching kind, per the line prefab's `m_PassengerTransport` / `m_CargoTransport` pair; `PathfindQueueSystem` reads it through `GetMaxTransportSpeed` on every worker-job schedule, and `SetDefaults` seeds both slots at `277.77777f`.
**So the fastest passenger line sets the search bounds for every query whose methods include public transport — citizen trips, mostly — and the fastest cargo line for the queries carrying cargo transport, which are the system-issued resource transfers rather than citizen trips.**
A city with no lines does not fall back to the seeded sentinel: the tick zeroes both slots before its empty-query guard, and the pathfinder clamps the zero at 0.01 m/s — the slow extreme, not the fast one.
Source: `src/Game/Game.Simulation/TransportLineSystem.cs`, `src/Game/Game.Pathfind/PathfindQueueSystem.cs`, `src/Game/Game.Pathfind/PathfindJobs.cs`.

## Verified paths and the gate bypass

Some transport buildings own routes carrying `VerifiedPath`, planned with forced direction against the building and its upgrades rather than between waypoints (`src/Game/Game.Routes/RoutePathSystem.cs`).
`RoutePathReadySystem` (`SystemUpdatePhase.Modification1`) reads the results: a resolved path whose origin or destination building shares the route's top owner gets the gate-bypass warning icon, and the owning building's `EfficiencyFactor.GateBypass` is set to `1 + RouteConfigurationData.m_GateBypassEfficiency` whenever any segment of the route resolved a path at all, back to `1` when none did (`src/Game/Game.Routes/RoutePathReadySystem.cs`).

**A verified route that resolves a path is the failure case: the penalty lands when the bypass exists, and a route none of whose segments resolved is the clean state.**
That clean state also adds `HiddenRoute`, which hides the route from rendering, raycasts and the transit UI and nothing more; `city-services-and-coverage` owns the efficiency machinery, and `RouteConfigurationData` carries the magnitude as a singleton field.
Source: `src/Game/Game.Routes/RoutePathReadySystem.cs`, `src/Game/Game.Routes/HiddenRoute.cs`, `src/Game/Game.Prefabs/RouteConfigurationData.cs`.

(VOLATILE: every component, field, enum, system, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Routes`, `Game.Pathfind`, `Game.Prefabs`, `Game.Simulation` and `Game.Buildings`, at the files cited beside each claim, with `Game.Common` for the `PathfindUpdated` stamps; plus the pathfind-prefab shape read live, against the `ecs_query` stated beside it.)
