# The deserialize phase, band by band

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The census below is read straight off that tree's registrations, so without one there is nothing here to act on.
`cs2-modding-setup` provisions it.

What vanilla registers in each band of the deserialize phase, and the twelve migration systems it ships.
Read this when placing a system in that phase, or when deciding whether the state your migration needs exists yet at the point you are running.
The `Serialize` phase's own bands are `mod-lifecycle-and-ordering`'s.

## Three bands, used deliberately

- **Front band (`UpdateBefore`)** — the deserialization barrier's opener, then 57 pre-deserialize wrappers, then the world-clearing system and the game-mode system.
- **Middle band (`UpdateAt`)** — the serializer system, the read system, then the rebuilders: the loaded-entity filter, the prefab resolver, the required-component backfiller, and 39 systems that reconstruct derived state deliberately left out of the save — sub-lanes, sub-objects, household citizens, owned vehicles, connected routes, the electricity graph and their kin.
  Eleven of the shipped migration systems sit last in this band.
- **Back band (`UpdateAfter`)** — the deserialization barrier first, then a mix: two rebuilders, the three resets, the obsolete-prefab initializer and one migration, then 21 post-deserialize wrappers, then six more plain systems, then the remaining 11 wrappers.
  **The barrier plays back at the front of this band, not at the end of the phase.**
  It is the band's first registration and order within a band is registration order, so by the time any other back-band system runs the deserialization barrier has already played back and closed itself — it reopens only in the front band of the next load.
  A system that asks it for a command buffer here throws, the phase driver swallows and logs the throw, and the work silently never happens.
  Use a different barrier, or structural changes applied directly.

## The twelve shipped migration systems

Eleven register `UpdateAt` as one contiguous block in the vanilla system-order class; the resident pseudo-random one alone sits in the back band, and their sources sit under `Game.Serialization.DataMigration`.

`BicyclePathfindFixSystem`, `CargoPortCleanupSystem`, `CompanyAndCargoFixSystem`, `HomelessAndWorkerFixSystem`, `HouseholdPetLimitSystem`, `LaneDirectionNetObjectSystem`, `PlaceholderCleanupSystem`, `QuantityObjectMissingSystem`, `ResidentPseudoRandomSystem`, `ShortLaneRemoveSystem`, `TradeCostFixSystem`, `UpdateCitizenFlagsFromHouseholdsSystem`.

They all gate before repairing, and most also skip when their own query is empty, but what splits them is what the gate tests.

**Eight test a format tag.**
The household pet limit system is the compact form, forty lines with the gate as an early return; most of the other seven invert it into a positive condition around the repair instead, and two test no query at all:

```csharp
if (m_LoadGameSystem.context.format.Has(FormatTags.HouseholdPetLimit) || m_HouseholdQuery.IsEmptyIgnoreFilter)
{
    return;
}
// trim every animal buffer to the limit, Deleted-tag the surplus,
// remove the buffer from households left with none
```

**Four compare the save's version against a named constant instead** — the lane-direction, placeholder-cleanup, quantity-object and resident-pseudo-random systems:

```csharp
if (!(m_LoadGameSystem.context.version >= Version.laneDirectionNetObject) && !m_Query.IsEmptyIgnoreFilter)
{
    // repair
}
```

**Those four are the shape a mod copies**, since a mod cannot add a format tag: its own version int stands where the `Version` constant does.
What they repair _with_ does not follow from the gate — both groups contain systems that repair on the main thread through `EntityManager` and systems that schedule Burst jobs writing to a command buffer.

(VOLATILE: the system names, the band counts and the registrations above — the vanilla system-order class's deserialize-phase registrations.)
