# The universal tags, one by one

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

[ecs-in-this-game.md](ecs-in-this-game.md) carries the three rules a reader acts on — tag the graphics, exclude the preview, delete by tag. This file is the catalogue behind them: what removes the frame-scoped six, what each tag asks for, which tags outlive a frame, and `Temp`'s full shape.

## The frame-scoped six, and what removes them

Six zero-field components form a frame-scoped change protocol: `Created`, `Updated`, `Applied`, `EffectsUpdated`, `BatchesUpdated` and `PathfindUpdated`.
`Created` and `Updated` are added to every prefab-instance archetype at birth, so a freshly spawned entity carries both and a system querying `WithAll<Created>()` sees it exactly once.

They are removed by a pair of systems.
A preparation system at the very end of the main loop snapshots two sets: everything carrying `Deleted` or `Event`, and everything carrying one of the six tags but _not_ `Deleted`.
A cleanup system at the end of the frame then destroys the first set and strips the six tags from the second.
Source: `src/Game/Game.Common/PrepareCleanUpSystem.cs` (the two snapshot queries) and `src/Game/Game.Common/CleanUpSystem.cs` (the destroy, and the six-type strip).

Three consequences a mod needs:

1. **`Deleted` means the entity dies later in the frame, not now.**
   That gap is the point: every system holding a reference gets a window to query `WithAll<Deleted>()` and unhook.
   This is why the game deletes by adding `Deleted` far more often than it calls `DestroyEntity` — do the same, and reserve `DestroyEntity` for entities nothing else can be holding.
2. **An `Event` entity lives exactly one frame.**
   It is in the destroy set with no exclusion, so an event entity spawned during a frame is gone by the end of it.
   Consume it in the same frame or not at all.
3. **A tag written after the snapshot survives an extra frame.**
   A tag added from a simulation system misses that frame's snapshot, is picked up at the end of the _next_ frame's main loop and removed at the end of that frame — which is precisely what makes it visible to the next frame's modification, tool, UI and rendering work.
   `mod-lifecycle-and-ordering` has the frame structure this rests on.

## What each tag asks for

- `Created` — this entity is new.
- `Updated` — something non-visual changed; re-run the modification pipeline over me. The general-purpose "I touched this" tag.
- `BatchesUpdated` — **the graphics for this entity need rebuilding, and this is the tag a mod forgets.** The culling system reads it, the batch instance and batch data systems branch on it, and the frame-scoped cleanup removes it with the other five. If you change anything visible on an entity and do not add `BatchesUpdated`, the renderer keeps drawing the old batch and your change is invisible with no error anywhere. Tag the sub-objects too, not only the parent: vanilla adds it to sub-objects and upgrades separately, and a building tagged alone renders with stale props.
  Source: `src/Game/Game.Rendering/PreCullingSystem.cs` (the read), `src/Game/Game.Common/CleanUpSystem.cs` (the removal, in the six-type set), `src/Game/Game.Simulation/CityServiceUpkeepSystem.cs` (a sub-object tagged on its own).
- `Applied` — added by the tool apply systems when a preview becomes real.
- `EffectsUpdated` and `PathfindUpdated` — narrow, for visual effects and for lane pathfinding parameters respectively.

## The tags that outlive a frame

These never pass through the cleanup pair:

- `Overridden` — this object conflicts with another object or network but is not deleted. Persists across a save; raycasting skips overridden geometry, and lane generation copies the tag onto the lanes it derives from an overridden original.
- `Native` — marks map-native content. Persists.
- `Owner` — a single `Entity m_Owner`, the standard back-reference from a sub-object to its parent, and the shape to copy when attaching your own entity to a game entity. Networks are dense graphs reached through it, which is why `roads-and-traffic` leans on it hardest.
- `PseudoRandomSeed` — a `ushort` seed plus `GetRandom(uint reason)`, which derives an independent stream per reason from the one stored seed. This is how the game gets stable per-entity randomness that survives a save without storing a stream, and a mod wanting reproducible per-entity variation should use it rather than seeding its own. It also forces the seed non-zero before constructing the generator, which a hand-rolled seed has to do for itself.

Source: `src/Game/Game.Common/Overridden.cs`, `src/Game/Game.Common/Native.cs`, `src/Game/Game.Common/Owner.cs` and `src/Game/Game.Common/PseudoRandomSeed.cs` (each declaration, its fields and what it persists), `src/Game/Game.Objects/RaycastJobs.cs` (the raycast skip) and `src/Game/Game.Net/LaneSystem.cs` (the propagation onto derived lanes).

## `Temp`, the tool-preview tag

**`Temp` is the one to exclude.**
It lives in the tools namespace, is not serialized, and carries `m_Original` — the real entity this preview stands for — plus a curve position, a value, a cost and flags.
The tool pipeline works entirely on `Temp` copies, and the apply systems read `m_Original` to write back onto the real entity.
**Nearly every game query excludes it**, and `None = { Deleted, Temp }` is the canonical pair: a query that forgets it will see the player's uncommitted hover preview as a real building.
`Hidden` is its sibling for the same reason.
Source: `src/Game/Game.Tools/Temp.cs` (the fields, and the serializer interface it does not declare) and `src/Game/Game.Simulation/AgingSystem.cs` (the canonical `None` pair).

(VOLATILE: the type set the cleanup system strips — that system's own query. The tag type names above and `Temp`'s field names — each tag's own declaration.)
