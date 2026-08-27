# The pathfinding queue

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Every pathfind, coverage and availability query, and every graph modification, goes through `PathfindQueueSystem`; requests are staged by `PathfindSetupSystem` and delivered by `PathfindResultSystem`, all three in `SystemUpdatePhase.MainLoop` (`src/Game/Game.Common/SystemOrder.cs`).

## Requesting a path

`PathfindSetupSystem.GetQueue(this, maxDelayFrames, spreadFrames = 0)` is public, `GetSetupQuery` exposes the system's own query factory, and the request is a `SetupQueueItem { m_Owner, m_Parameters, m_Origin, m_Destination }` whose two `SetupQueueTarget`s each carry a `SetupTargetType` (`src/Game/Game.Simulation/PathfindSetupSystem.cs`, `src/Game/Game.Pathfind/SetupQueueItem.cs`, `SetupQueueTarget.cs`).

**`SetupTargetType` is a closed set: a mod cannot add a member.**
`FindTargets` dispatches on a hardcoded switch whose default logs `"Invalid target type in Pathfind setup "` plus the value and returns no targets, so a new enum value silently produces nothing; `SetupTargetType.CurrentLocation` is the generic "where this entity is" case a mod reaches for instead.
Source: `src/Game/Game.Simulation/PathfindSetupSystem.cs`, `src/Game/Game.Pathfind/SetupTargetType.cs`.

Every vanilla `GetQueue` caller falls into three deadline classes — the sweep is grep `GetQueue(this` under `src/Game/`: 64 frames for reactive work (the dispatch and AI systems throughout, one of which tightens to 16 during the new-game warm-up), 80 spread over 16 for four of the systems asking on behalf of a whole population (`FindJobSystem`, `HouseholdFindPropertySystem`, `ResourceBuyerSystem`, `TripNeededSystem` — `FindSchoolSystem` asks the same way and sits at 64), and 512 for background work (`AreaLotSimulationSystem`).
Systems that skip the setup stage and call `PathfindQueueSystem.Enqueue` directly carry deadlines of their own — the coverage simulation enqueues at 256, and the tool previews with no deadline at all — so the sweep above is of pathfind requests, not of everything the queue serves.
A mod adding a queue matches the class of work it is doing.
`spreadFrames` is a per-frame proportional dequeue — `ceil(queueCount * step / (remainingFrames + step))` while the window lasts, unbounded once it elapses — which is what keeps a population-wide re-plan from landing as one spike.

## The deadlines throttle the game clock

Sources: `src/Game/Game.Simulation/PathfindSetupSystem.cs`, `src/Game/Game.Pathfind/PathfindResultSystem.cs`, `src/Game/Game.Simulation/SimulationSystem.cs`.

```
GetQueue:      m_ResultFrame = frameIndex + maxDelayFrames
pendingSimulationFrame:
  min over every outstanding request's m_ResultFrame, queued and in flight, composed across the setup and result systems; uint.MaxValue when none
SimulationSystem.OnUpdate:
  if pendingSimulationFrame < uint.MaxValue:     // no queue, no throttle
    slack  = max(0, pendingSimulationFrame - frameIndex - 1)
    dt    *= min(1, slack / 48)          // the time step scales down linearly
    frames = min(frames, slack)          // and the frame count is capped
  frames = min(frames, performance-preference cap)
  frames = clamp(frames, 0, max(1, min(8, round(speed * min(1, slack / 48) * 2))))
                                         // the step ceiling collapses with the same slack ratio — a second throttle
```

When the queue falls behind its nearest deadline the simulation slows, and at the deadline it advances no frames at all — the game cannot outrun its own pathfinder.
`DebugSystem`'s Pathfind tab displays the same margin (`src/Game/Game.Debug/DebugSystem.cs`).

## Inside the queue

`ActionType` is `Create, Update, Delete, Pathfind, Coverage, Availability, Density, Time, Flow`, drained in three strict classes — high priority first, then the modification types (`Create/Update/Delete/Density/Time/Flow`), then the rest — returning early the moment the next item's job dependencies are incomplete (`src/Game/Game.Pathfind/PathfindQueueSystem.cs`).
The graph is double-buffered (`WORKER_DATA_COUNT = 2`); queries read one copy while modifications are scheduled onto both, first flushing pending worker jobs and combining both copies' read and write handles.
So every network edit serialises the entire pathfinder, and a mod editing the network every frame stalls every query in the game every frame.
The queue keeps up to half the job-worker count of worker jobs in flight per update (`m_MaxThreadCount = max(1, JobWorkerCount / 2)`) — a high-priority backlog raises that cap, and the jobs come out of the shared pool rather than a reserved set of threads — each with a persistent linear allocator starting at 1 MiB; `GetGraphSize`, `GetGraphMemory` and `GetQueryMemory` are the supported way to see what the graph costs.
`Coverage` and `Availability` are the service simulation's query types; they share these workers, so saturating the pathfinder degrades service coverage too, and vice versa.
`Density`, `Time` and `Flow` write single scalars onto existing edges without rebuilding them — `Flow` carries the congestion byte, `Time` an edge's time cost, the border delay among its writers ([congestion-and-blockage.md](congestion-and-blockage.md), [intercity-traffic.md](intercity-traffic.md)).

## The result

The answer lands on the requesting entity: `PathInformation { m_Origin, m_Destination, m_Distance, m_Duration, m_TotalCost, m_Methods, m_State }` plus a `PathElement { m_Target, m_TargetDelta, m_Flags }` buffer, one element per graph edge traversed, `[InternalBufferCapacity(0)]` so it always heap-allocates (`src/Game/Game.Pathfind/PathInformation.cs`, `PathElement.cs`).
`PathOwner { m_ElementIndex, m_State }` is the consumer's cursor into that buffer (`src/Game/Game.Pathfind/PathOwner.cs`).
`PathFlags` in full: `Pending`, `Failed`, `Obsolete`, `Scheduled`, `Append`, `Updated`, `Stuck`, `WantsEvent`, `AddDestination`, `Debug`, `Divert`, `DivertObsolete`, `CachedObsolete` (`src/Game/Game.Pathfind/PathFlags.cs`).
The vanilla lever for making one agent re-plan is `PathFlags.Obsolete` on its `PathOwner` — `VehicleUtils.SetTarget` sets it while clearing `Failed`, and `RequireNewPath` acts on it, or on `DivertObsolete`, only while none of `Pending`, `Failed` or `Stuck` is set (`src/Game/Game.Vehicles/VehicleUtils.cs`).
That is a different act from making the network re-derive, which has its own levers and costs ([network-rebuild.md](network-rebuild.md)).

**`PathFlags.Pending` never survives a save.**
Both deserializers rewrite it to `Obsolete`, so every in-flight request is re-issued on load rather than resumed — the `PathElement` buffer on each requester is the only route persistence the game has.
Source: `src/Game/Game.Pathfind/PathOwner.cs`, `src/Game/Game.Pathfind/PathInformation.cs`.

(VOLATILE: every component, field, enum, system, method, constant, quoted warning string and `Source:` path this file names — their declarations under `src/Game/` in `Game.Pathfind`, `Game.Simulation`, `Game.Vehicles`, `Game.Debug` and `Game.Common`, at the files cited beside each; the deadline-class shape, against the `GetQueue(this` sweep it states; the throttle listing, against `SimulationSystem.cs`.)
