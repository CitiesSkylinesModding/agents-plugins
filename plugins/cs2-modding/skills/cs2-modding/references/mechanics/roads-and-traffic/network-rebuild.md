# Making the network re-derive

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

The lane layer is derived, not authored: change what it is derived from — prefabs, composition flags, upgrades, district policy — mark the owner, and let the pipeline regenerate every lane.
The pipeline runs inside the `Modification*` phases in a fixed order; `SystemOrder.cs` registers it and is regenerated per game version, so re-check it by system name.

| Phase | This topic's systems, in order (`src/Game/Game.Common/SystemOrder.cs`) |
| --- | --- |
| Modification3 | `CompositionSelectSystem` — edge flags become composition prefab references |
| Modification4 | `NetCompositionSystem`, `Game.Net.GeometrySystem`, `LaneSystem` — the full relane |
| Modification4B | `LaneOverlapSystem`, `TrafficLightInitializationSystem`, `SecondaryLaneSystem` |
| Modification5 | `LaneConnectionSystem`, `LaneBlockSystem`, `LanePoliciesSystem` |
| ModificationEnd | `LaneDataSystem`, `ParkingLaneDataSystem`, `LanesModifiedSystem` — into the pathfind graph |

## Three triggers of very different weight

1. `Game.Common.PathfindUpdated`, a zero-size tag on a lane: `LaneDataSystem` re-derives that lane's pathfind specification, and `LanesModifiedSystem` turns it into an `UpdateAction` on the queue, without relaning anything — vanilla writes it from dozens of systems, a parked car appearing, a lane connection changing, a district boundary or policy moving and the vehicle AIs among them (`src/Game/Game.Common/PathfindUpdated.cs`, `src/Game/Game.Pathfind/LaneDataSystem.cs`).
2. `Updated` on a lane: the same re-derivation and graph action, through the update pipeline's own marker.
3. `Updated` on the edge or node: the full relane — `LaneSystem`'s query is `All = SubLane`, `Any = Updated | Deleted`, `None` excluding outside connections and areas, which never relane — and everything from Modification4B onward re-runs behind it the same frame (`src/Game/Game.Net/LaneSystem.cs`).

`LanesModifiedSystem` is the bridge into the graph — created, updated and deleted lanes become `CreateAction`/`UpdateAction`/`DeleteAction`, over the non-slave lanes [route-selection.md](route-selection.md) scopes the graph to (`src/Game/Game.Pathfind/LanesModifiedSystem.cs`).
A mod that adds `Updated` to an edge when it wanted one agent to reconsider has relaned the whole segment for nothing; the agent-side lever is `PathFlags.Obsolete` ([pathfind-queue.md](pathfind-queue.md)).

**All the update markers are one-frame tags, so one added during a modification phase reaches only the systems that run later that same frame.**
`CleanUpSystem` removes `Created`, `Updated`, `Applied`, `EffectsUpdated`, `BatchesUpdated` and `PathfindUpdated` in `SystemUpdatePhase.Cleanup`; a change made inside the pipeline defers its marker to the next frame through a tag of the mod's own.
Source: `src/Game/Game.Common/CleanUpSystem.cs`, `src/Game/Game.Common/SystemOrder.cs`.

**Marking only the entity you edited under-reaches: a node edit fans out to every connected edge and each edge's far node.**
An edge's composition is selected from the flags of both its ends, and a node's lanes are generated from the compositions of every connected edge — so a change at one node reaches the far node's lanes through the shared edge.
Source: `src/Game/Game.Net/CompositionSelectSystem.cs`, `src/Game/Game.Net/LaneSystem.cs`.

A change that moves geometry also marks `BatchesUpdated`, and the road's `Aggregated.m_Aggregate` where one spans it, or the rendering never rebuilds; a routing-only change needs neither.
A node dies with its edges: the node-generation pass deletes one only once every edge attached to it is going (`GenerateNodesSystem.WillBeOrphan`, `src/Game/Game.Tools/GenerateNodesSystem.cs`), so removing an edge marks the surviving neighbours `Updated` rather than touching the node directly.
Changing an edge's endpoints is delete-and-recreate for the edge, which is why node edits fan markers so widely.

## Replacing a network system

Some behaviour has no data seam — yield flags, for instance, are written onto lanes during generation and regenerated on every relane ([junctions.md](junctions.md)) — and the only route is substituting the generating system whole.
The pattern: `World.GetOrCreateSystemManaged<T>().Enabled = false`, then register the replacement `UpdateBefore<Mine, T>` at the phase vanilla registers `T` — anchoring on the disabled system, which preserves its ordering slot.
The replacement must also take over the plumbing its original served: `LaneSystem` registers as a terrain height reader, a writer on `LaneReferencesSystem`'s skip-lane queue, and a producer on the modification barrier (`src/Game/Game.Net/LaneSystem.cs`).

**Coexistence with a still-enabled `LaneSystem` silently corrupts the skip-lane hand-off.**
`LaneReferencesSystem.GetSkipLaneQueue()` allocates a fresh queue and overwrites the field on every call, and `AddSkipLaneWriter` overwrites the dependency rather than combining it — two callers lose one queue and one dependency.
Source: `src/Game/Game.Net/LaneReferencesSystem.cs`.

The price is the file: lanes are regenerated wholesale, the game exposes no hook inside, so a replacement reproduces nearly all of `LaneSystem` — the largest file in `Game.Net` — to change one branch.

(VOLATILE: every component, field, system, method, phase name and `Source:` path this file names — their declarations under `src/Game/` in `Game.Common`, `Game.Net`, `Game.Pathfind`, `Game.Prefabs` and `Game.Tools`, at the files cited beside each; the phase table, against `SystemOrder.cs` re-checked by system name.)
