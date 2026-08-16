# Lines and the fleet

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`TransportLineSystem` runs in `SystemUpdatePhase.GameSimulation` at a 256-frame interval and recomputes every line's price, activity, headway and fleet each pass; `RouteUtils` holds the helpers and `TransportBoardingHelpers` applies the departure timing at the door.
Sources: `src/Game/Game.Simulation/TransportLineSystem.cs`, `src/Game/Game.Routes/RouteUtils.cs`, `src/Game/Game.Simulation/TransportBoardingHelpers.cs`, `src/Game/Game.Common/SystemOrder.cs`.

## Options and modifiers

A line option is a policy applied to the route entity: `RouteOption` — `Day`, `Night`, `Inactive`, `PaidTicket` — is the whole player-facing option set, read as a bit of `Route.m_OptionMask` through `RouteUtils.CheckOption(route, option)`, and authored by `RouteOptions` on a policy prefab ORing its `RouteOption[]` into `RouteOptionData.m_OptionMask` (`src/Game/Game.Routes/RouteOption.cs`, `src/Game/Game.Prefabs/RouteOptions.cs`).
A reader looking for where the day/night toggle lives finds a policy prefab, not a field — which is why `TransportLinePrefab` puts `Game.Policies.Policy` on the route archetype.
The numeric adjustments are exactly two — `RouteModifierType.TicketPrice` and `RouteModifierType.VehicleInterval` — carried as a `RouteModifier { m_Delta }` buffer on the route entity and authored by `RouteModifiers` on a policy prefab filling a `RouteModifierData { m_Type, m_Mode, m_Range }` buffer (`src/Game/Game.Routes/RouteModifierType.cs`, `RouteModifier.cs`, `src/Game/Game.Prefabs/RouteModifiers.cs`, `RouteModifierData.cs`).

```
RouteUtils.ApplyModifier(ref value, modifiers, type):
    if modifiers.Length > (int)type:
        value += modifiers[(int)type].m_Delta.x        // additive term
        value += value * modifiers[(int)type].m_Delta.y  // then multiplicative
```

**The buffer is positional — index 0 is ticket price, index 1 is vehicle interval — and the length test is what makes a short buffer mean "no modifier" rather than an out-of-range read.**

## Activity, day and night

`TransportLineSystem.OnUpdate` computes `isNight = normalizedTime < 0.25f || normalizedTime >= 11f / 12f`, and the tick job resolves each line to a `bool3` — running now, runs by day, runs by night:

```
Inactive option set              -> (false, false, false)
no active building on the line   -> (false, false, false)
Day option set                   -> (!isNight, true, false)
Night option set                 -> (isNight, false, true)
otherwise                        -> (true, true, true)
```

The `.yz` become `RouteInfoFlags.InactiveDay` / `InactiveNight` on every segment's `RouteInfo`, which the line-edge builder turns into the presence or absence of the two `PublicTransport*` path methods ([transit-routing.md](transit-routing.md)) — so a day-only line loses the night path method and a citizen planning a night trip cannot route over it; on night ticks its target count is also zeroed, so `AbandonVehicles` sends the fleet home until morning.

**"No active building on the line" walks every waypoint's stop up its `Owner` chain, and returns false only when waypoints resolve to buildings and every one is inactive.**
`CheckIfIsThereAnyActiveBuildingsOnTheLine` answers true when no waypoint resolves to a building at all, so a line of plain street stops always runs, while a line whose every station the player switched off stops.
Source: `src/Game/Game.Simulation/TransportLineSystem.cs`.

## Fleet sizing

Two public statics on `TransportLineSystem` are the whole sizing model, and the tick evaluates them per line:

```
CalculateVehicleCount(vehicleInterval, lineDuration) = max(1, round(lineDuration / max(1, vehicleInterval)))
CalculateVehicleInterval(lineDuration, vehicleCount) = lineDuration / max(1, vehicleCount)

per line, per tick:
    interval    = TransportLineData.m_DefaultVehicleInterval, then ApplyModifier(VehicleInterval)
    ticketPrice = PaidTicket option set ? clamp(round(ApplyModifier(0, TicketPrice)), 0, 65535) : 0
    RefreshLineSegments -> lineDuration, stableDuration
    targetCount = CalculateVehicleCount(interval, stableDuration)
    newInterval = min(interval * 10, CalculateVehicleInterval(lineDuration, targetCount))
    if newInterval differs from the stored m_VehicleInterval by 1 or more: store it
    if it was stored, or the price changed:
        PathfindUpdated on every waypoint carrying VehicleTiming
    targetCount = 0 when the line is not running now
```

**With `PaidTicket` off the price is forced to zero whatever the modifier says, and the operative headway is capped at ten times the requested interval** — the cap is what stops a broken or enormous line reporting an absurd figure, and a sub-second headway move is never stored at all.
The two durations differ: `stableDuration` sums each segment's planned `PathInformation.m_Duration` plus the prefab `m_StopDuration` per stop, while `lineDuration` floors each leg at the waypoint's measured `VehicleTiming.m_AverageTravelTime` and charges the real dwell, `RouteUtils.GetStopDuration(lineData, stop) = lineData.m_StopDuration / max(0.25f, stop.m_LoadingFactor)` — the `0.25` floor caps the dwell penalty at four times the base.
**So the fleet is sized on the congestion-blind plan, and the reported headway comes from what the vehicles actually achieved.**
The same pass rewrites each segment's `RouteInfo` and raises the two-slot transport-speed maximum, both of which [transit-routing.md](transit-routing.md) carries.
No usage figure is stored anywhere on the line: the panel's occupancy is recomputed per read by `TransportUIUtils`, walking `RouteVehicle` into each consist's `LayoutElement` cars and summing `Passenger` length or `Economy.Resources` against the passenger, cargo or work-vehicle capacity, and a mod wanting it calls `TransportUIUtils.GetRouteVehiclesCount` — `public static`, returning the vehicle count with cargo and capacity through its two `ref int` parameters — rather than reproducing the walk (`src/Game/Game.UI.InGame/TransportUIUtils.cs`).

## Requesting and abandoning vehicles

`CheckVehicles` drops any `RouteVehicle` entry whose vehicle no longer points back through `CurrentRoute`, and counts as continuing only vehicles neither flagged `AbandonRoute` nor failing the line's `VehicleModel` buffer — a model-failing vehicle is not merely uncounted, it is queued for `AbandonRoute` itself.
`RouteUtils.CheckVehicleModel` reads that buffer as a selection: an empty buffer, or one whose entries are all `Entity.Null`, means any model; `m_PrimaryPrefab` is the engine and `m_SecondaryPrefab` the carriage — a vehicle whose prefab carries `MultipleUnitTrainData` satisfies the secondary test outright, and any other consist satisfies it when at least one `LayoutElement` car is a listed `m_SecondaryPrefab` (`src/Game/Game.Routes/VehicleModel.cs`, `RouteUtils.cs`).
Fleet changes act by odometer: over target, `AbandonVehicles` flags the highest-mileage eligible vehicles `AbandonRoute`; under target with abandoned vehicles still around, `CancelAbandon` unflags the lowest-mileage ones first.
A shortage becomes a request entity of the `{ ServiceRequest, TransportVehicleRequest, RequestGroup(8) }` archetype with `TransportVehicleRequest(line, 1 - vehicleCount / targetCount)` — the priority is the shortfall fraction, so an empty line outbids a nearly-full one.
An outstanding request is reused only while its `ServiceRequest.m_FailCount < 2`; at two failures the line sets `TransportLineFlags.NotEnoughVehicles` and raises `TransportLineData.m_VehicleNotification` at `IconPriority.Problem` — skipped while the line has no waypoints — both cleared on the next pass once the line is no longer blocked on a twice-failed request — the clearing arm is a plain else, not an arrival test.
[depots-and-dispatch.md](depots-and-dispatch.md) carries the matching and spawning half.

## Unbunching

One expression, `RouteUtils.CalculateDepartureFrame`, evaluated when a vehicle flagged `EnRoute` begins boarding; a vehicle not yet `EnRoute` gets a flat `simulationFrame + 60` instead, and that arm leaves `VehicleTiming.m_LastDepartureFrame` unwritten (`TransportBoardingHelpers.cs`):

```
elapsed   = (simulationFrame - lastDepartureFrame) / 60
if elapsed < 0: return simulationFrame
requested = ApplyModifier(lineData.m_DefaultVehicleInterval, modifiers, VehicleInterval)
headway   = line.m_VehicleInterval
wait      = min(requested, 2 * headway * headway / (elapsed + headway) - headway) * line.m_UnbunchingFactor
wait      = max(wait + targetStopTime, 1)
return simulationFrame + uint(wait * 60)
```

**The whole of bunching control is that hyperbola**: a vehicle arriving early is held for close to `headway * m_UnbunchingFactor`, a late one gets a negative term floored to the one-second minimum dwell; the `60` is the frames-per-second divisor, and `simulation-time-and-units` owns what a frame is.
The `elapsed < 0` arm is dead: the frame subtraction is unsigned, so a future `m_LastDepartureFrame` wraps to a huge elapsed and the wait collapses to the one-second floor rather than taking that return.
`TransportLine.m_UnbunchingFactor` is seeded from `TransportLineData.m_DefaultUnbunchingFactor` by the component's own field initializers when the line is created, and the tick rewrites the interval, the price, the flags and `m_VehicleRequest` — `m_UnbunchingFactor` is the one field it never touches; a plain `IComponentData` construction, not the Unity-serialized kind the initializer trap covers (`src/Game/Game.Routes/TransportLine.cs`).
The waypoint's `VehicleTiming.m_AverageTravelTime` updates at the same boarding: the first sample is taken whole, every later one as `lerp(old, sample, 0.5)`, and a `departureFrame` of zero skips the update (`RouteUtils.UpdateAverageTravelTime`).

(VOLATILE: every component, field, enum, system, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Routes`, `Game.Prefabs`, `Game.Simulation`, `Game.UI.InGame`, `Game.Vehicles`, `Game.Policies`, `Game.Economy` and `Game.Common`, at the files cited beside each claim.)
