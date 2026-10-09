# All-way stops on small-street junctions

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod that puts all-way stops on small-street junctions for me. Wherever three or more roads meet and every one of them has a speed limit of 30 km/h or less, the junction should become an all-way stop, with stop signs on every approach. Skip junctions that already have traffic lights, and skip roundabouts. It should cover the junctions already in the city when a save loads and the ones I build afterwards.

## Checklist

### Stop signs enter through the junction's composition flags, not its lanes

- Falls in: the code ORs `CarLaneFlags.Stop` into `CarLane.m_Flags` on the entities of the node's `SubLane` buffer, or writes `CompositionFlags.General.AllWayStop` into the `NetCompositionData` of a composition prefab, which every junction of that cross-section shares.
- Avoids: the code sets `CompositionFlags.General.AllWayStop` in the node's own `Upgraded.m_Flags.m_General`, adding `Upgraded` where the node has none, and writes no lane flag itself.
- Reference: cs2-modding/references/mechanics/roads-and-traffic/junctions.md
- Source: src/Game/Game.Net/CompositionSelectSystem.cs:900

### The junction's edges carry the update marker

- Falls in: `Updated` is added to the node alone, or to nothing, with no marker on the edges listed in the node's `ConnectedEdge` buffer.
- Avoids: every edge in the node's `ConnectedEdge` buffer receives `Updated` alongside the node, and the far nodes, read from `Edge.m_Start` and `Edge.m_End`, with them.
- Reference: cs2-modding/references/mechanics/roads-and-traffic/network-rebuild.md
- Source: src/Game/Game.Net/CompositionSelectSystem.cs:1457

### The speed limit is compared in metres per second

- Falls in: a baked `m_SpeedLimit`, on `RoadData`, `RoadComposition` or `CarLane`, is compared against the km/h figure as written, such as `<= 30f`.
- Avoids: the threshold is converted before the comparison, `30f / 3.6f` or a constant near 8.33, or the comparison reads the authoring `RoadPrefab.m_SpeedLimit` through `PrefabSystem`, which stays in km/h.
- Reference: cs2-modding/references/mechanics/roads-and-traffic/roads-and-traffic.md
- Source: src/Game/Game.Prefabs/NetInitializeSystem.cs:1952

## Roots

- decompile
