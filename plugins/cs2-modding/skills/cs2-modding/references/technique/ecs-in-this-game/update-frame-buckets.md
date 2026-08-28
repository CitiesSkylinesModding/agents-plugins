# Reading a system's `UpdateFrame` bucketing before you fork it

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Every step below is a read of that tree, so without one there is nothing here to run.
`cs2-modding-setup` provisions it.

[ecs-in-this-game.md](ecs-in-this-game.md) states what `UpdateFrame` is and shows the two forms a system skips on; neither says which index the system compares against or where it got it, which is what this file supplies.
Read the original in this order.
`mod-lifecycle-and-ordering` owns the interval and offset overrides themselves — their defaults, and the gate the update system runs them through — and what is below only reads them.

## First, check what it does with `UpdateFrame` — per job, not per system

Three answers, not two: the two skip forms, or it reads that index as data and never skips on it.
The third is not partitioning, and completing it into a filter breaks what it was actually computing.
The filter is `SetSharedComponentFilter` or `AddSharedComponentFilter`, either optionally preceded by `ResetFilter`, and its argument is a constructor call or an object initializer — so a grep shaped like the entry file's example misses the systems that spell it otherwise.
The answer is not a property of the system: one query's job may test two different indices, a filtered query may sit beside an unfiltered one, and a job may iterate a list rather than a query — taking the index from a cached copy, or from a filtered query already drained into that list before any job sees the component.
Naming the component in a query is none of the three, and adding a filter the original does not have runs the fork at a fraction of the vanilla rate with nothing logged.
Where it does none of the three, check first whether it _writes_ `UpdateFrame` rather than reading it: an assigner picks a bucket from the family of the entity it is stamping, and no clock feeds the assignment.

**Read `GetUpdateInterval` and `GetUpdateOffset` either way**, since a bucketed system carries them as readily as an unbucketed one, and both take the phase — a system can declare one pair for loading and another for play.
Where a system overrides the offset and its family's prefabs pin one index, that offset carries the bucket scaled by the frames per bucket — the interval over the group count — and equals the bucket only where those two match.
Not every member of a pinned family overrides it, so a pin does not promise an offset.
An absent offset override is -1, which resolves to 0 at interval 1 and to an arbitrary slot above it, so a fork declaring neither override runs every frame rather than landing anywhere in particular.
The bucket itself is read from whatever declares the pin — the prefab class, or one of the component types offered under its component menu — and never from the offset.
Source: `src/Game/Game/GameSystemBase.cs` (the two virtuals, their phase argument and their defaults), `src/Game/Game/UpdateSystem.cs` (the gate, and where an absent offset resolves), `src/Game/Game.Prefabs/UpdateFrameData.cs` (the pin the bucket is read from).

**Where none of the three fits and the system writes no index either, the interval and offset are the whole answer**: the system is not bucketed at all, it simply runs on the frames the gate selects, and neither step below applies to it.
Source: `src/Game/Game/UpdateSystem.cs` (the gate that selects those frames).

## Then check which clock feeds it

That is what sets the cadence.
`frameIndex` is `SimulationSystem.frameIndex` on the simulation side: a system reaches that system managed from its own `OnCreate` and holds the reference, which is what every partitioned simulation system does.
`RenderingSystem.frameIndex` is that index shifted for interpolation, so it is a different number, and rendering-side consumers of `UpdateFrame` mostly feed it rather than the simulation value — though not all of them do.
`TrafficRoutesSystem`, in the tools namespace, partitions off a counter it increments and wraps itself rather than off any frame index at all, so read what the system you are forking actually feeds its computation rather than inferring it from the namespace.

## Then take the group count off that system

**The count is per family, and two different families sit behind it.**
The nine load-balanced families — the ones `UpdateGroupSystem` assigns into — are sixteen each, declared as `*_UPDATE_GROUP_COUNT` in `SimulationUtils` and mirrored in the size of each family's group array.
Service requests are not one of those: `ServiceRequestSystem` stamps a request with a bucket drawn against a `m_GroupCount` field carried on its request group, and those groups are constructed with 4, 8, 16 and 32, which `SimulationUtils` declares as `*_DISPATCH_GROUP_COUNT`.
The dispatch systems then read that same shared component and gate on the matching modulus, so **for a request family the dispatch count _is_ the bucket count**.
Carrying sixteen over to a fork of a four- or eight-count family visits each request a quarter or a half as often; carrying it to a fork of a thirty-two-count family leaves buckets sixteen through thirty-one matching nothing, so half that family's requests are never served at all.
Neither is logged.
Source: `src/Game/Game.Simulation/SimulationUtils.cs` (the two constant families), `src/Game/Game.Simulation/UpdateGroupSystem.cs` (the nine group arrays), `src/Game/Game.Simulation/ServiceRequestSystem.cs` (the bucket drawn against the request group's own count field).

A search for either constant's name finds none of its consumers — a compile-time constant is inlined at every use, which `navigating-the-decompile` explains — and at the request write the count arrives through the component field, so even a value search lands on the `new RequestGroup(…)` construction sites rather than the write.

**Check what a masked value is before reading its bound as a count.**
An `&` mask is one less than the count and a `%` modulus is the count itself, but only where the value masked is a bucket index.
The moving-object systems compute a four-entry interpolation ring slot from the same frame index in the same shapes, and `CarMoveSystem` puts a bucket and a ring slot on adjacent lines.
Where the masked value is a frame index minus a bucket index rather than a bucket index, it is neither.
`SimulationUtils` keeps the interpolation, dispatch and update constants side by side, so a literal matches the wrong entry readily.
Source: `src/Game/Game.Simulation/CarMoveSystem.cs` (a bucket and a ring slot on adjacent lines) and `src/Game/Game.Simulation/SimulationUtils.cs` (the three constant families in one block).

Where a system calls `SimulationUtils.GetUpdateFrameWithInterval(frameIndex, interval, groupCount)` the count is an argument you can read; `GetUpdateFrame(frameIndex, updatesPerDay, groupCount)` substitutes `262144 / (updatesPerDay * groupCount)` for the interval and is otherwise the same function, so which of the two a system calls says nothing about its behaviour.
That `interval` argument is commonly the system's own `GetUpdateInterval`, so a fork that copies the job without the override runs many times too often, with nothing logged.

## Two things bite after that

- **The shift or divisor beside the mask is the cadence, and it is a separate number.** `frameIndex & 0xF` and `(frameIndex >> 2) % 16` both partition into sixteen, over periods of sixteen frames and sixty-four.
  Source: `src/Game/Game.Simulation/SimulationUtils.cs` (`GetUpdateFrameWithInterval`, where the divisor and the mask are separate arguments).
- **A system may visit only some of its buckets, and what it serves pins itself to those.**
  `AnimalMoveSystem` computes an index over sixteen and acts on three, and the pin is not on the prefab class at all: the animal prefab adds no `UpdateFrameData`, and the `Pet`, `Domesticated` and `Wildlife` component types offered under its component menu each supply one naming one of the three gated buckets.
  Attach none and the instance takes a load-balanced index the gate never selects, so it never moves, with nothing logged.
  Dropping the gate instead costs little, since the buckets it skips are empty within that query — but the same gate repeats in the systems that navigate that family, and one copy of it guards a call rather than a query, so dropping it in one place leaves the entity served by some of its systems and not the others.
  Source: `src/Game/Game.Simulation/AnimalMoveSystem.cs` and `src/Game/Game.Simulation/AnimalNavigationSystem.cs` (the gate, and the copies that guard a call), `src/Game/Game.Prefabs/Pet.cs`, `src/Game/Game.Prefabs/Domesticated.cs` and `src/Game/Game.Prefabs/Wildlife.cs` (the three pins), `src/Game/Game.Prefabs/AnimalPrefab.cs` (which declares none).

Copy the original's form rather than converting between the two, and carry the index and the count these steps established across with it — the form is the only part a fork inherits for free.
What a bucket is worth in simulated time belongs to `simulation-time-and-units`.

(VOLATILE: the load-balanced and dispatch group counts — `SimulationUtils`'s `*_UPDATE_GROUP_COUNT` and `*_DISPATCH_GROUP_COUNT` declarations, `UpdateGroupSystem`'s group arrays, and the request group's own count field. The interval and offset defaults this page reads — `GameSystemBase`'s `GetUpdateInterval` and `GetUpdateOffset`. The `GetUpdateFrame` helpers' signatures — `SimulationUtils`. The interpolation ring's length — `TransformFrame`'s buffer-capacity attribute.)
