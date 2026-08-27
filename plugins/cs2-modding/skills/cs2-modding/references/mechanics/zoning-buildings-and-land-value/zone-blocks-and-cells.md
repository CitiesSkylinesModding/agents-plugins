# Zone blocks, cells and the zone check

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

## Where blocks come from

Sources: `src/Game/Game.Zones/BlockSystem.cs`, `src/Game/Game.Prefabs/RoadFlags.cs`, `src/Game/Game.Prefabs/NetInitializeSystem.cs`, `src/Game/Game.Prefabs/ZoneBlockPrefab.cs`, `src/Game/Game.Zones/ZoneUtils.cs`.

```
BlockSystem (SystemUpdatePhase.Modification4):
  gate: the edge's RoadComposition carries RoadFlags.EnableZoning -- no flag, no blocks
      // NetInitializeSystem sets the flag when RoadPrefab.m_ZoneBlock names a block prefab, which is what makes highways unzonable without any zoning code knowing what a highway is
  three further suppressors: an owner chain carrying Building, an Elevated or Tunnel composition, and a per-side Raised or Lowered flag
  block prefab: RoadPrefab.m_ZoneBlock -> RoadData.m_ZoneBlockPrefab -> RoadComposition.m_ZoneBlockPrefab
  a CHAIN of blocks per side of the edge -- segments split by curvature and again at 10 cells -- plus node blocks only where the composition is a Roundabout
  per side, ZoneUtils.GetCellWidth(m_Width -+ m_MiddleOffset * 2) = ceil(width / 8 - 0.01) cells, and the row sits cellWidth * 4 metres out from the curve; a block's own m_Size is its chain segment's length capped at 10 cells wide, by 6 deep
  instantiates ZoneBlockData.m_Archetype, which ZoneBlockPrefab.LateInitialize built:
      Block, CurvePosition, ValidArea, BuildOrder, Cell, CullingInfo, MeshBatch, Created, Updated, plus PrefabRef from the base prefab machinery; BlockSystem then adds Owner = the road edge, outside the archetype
```

**A block without a zoning road edge as `Owner` never spawns anything, silently.**
`ZoneSpawnSystem`'s query requires `Owner` beside `Block` and `VacantLot` — a component the archetype above does not declare — and its scoring job returns before scoring any lot whose owner carries no `ResourceAvailability` buffer, so a mod-built block with the wrong owner is invisible to the spawner rather than an error — unless that owner carries a `ResourceAvailability` buffer without `Game.Net.LandValue`, a hand-made pair that faults in the Burst job.
Source: `src/Game/Game.Simulation/ZoneSpawnSystem.cs`, `src/Game/Game.Zones/BlockSystem.cs`.

**`NetZoneData` is dead at 1.6.0f1.**
The struct exists with no producer and no consumer outside its own file; the live path is the `RoadComposition.m_ZoneBlockPrefab` chain above, so do not chase it.
Source: `src/Game/Game.Prefabs/NetZoneData.cs`, `src/Game/Game.Prefabs/RoadComposition.cs`.

## From painted cells to vacant lots

Sources: `src/Game/Game.Zones/CellCheckSystem.cs`, `src/Game/Game.Zones/CellOccupyJobs.cs`, `src/Game/Game.Zones/ZoneUtils.cs`, `src/Game/Game.Zones/CellFlags.cs`.

```
CellCheckSystem (Modification5), short-circuiting unless a collect system reports an update, then a fixed job chain (the schedule in its OnUpdate is the authoritative order):
  0 CellCheckHelpers.CollectBlocksJob    - builds the block array every later job iterates
  1 CellBlockJobs.BlockCellsJob          - blocks cells against roads and areas, writes ValidArea, and writes the Roadside / RoadLeft / RoadRight / RoadBack direction bits
  2 CellCheckHelpers.FindOverlappingBlocksJob -> GroupOverlappingBlocksJob
  3 CellOccupyJobs.ZoneAndOccupyCellsJob - marks cells occupied by existing objects, occupies any cell whose height headroom is under max(ZoneData.m_MinOddHeight, m_MinEvenHeight), and inherits zone types from deleted blocks
  4 ZoneToggleJob                        - sets only CellFlags.Blocked, for a composition carrying the ZonesDisabled side flag (the road upgrade that turns zoning off); CityConfigurationSystem.leftHandTraffic is only its tie-break for which side a block is on
  5 CellOverlapJobs.CheckBlockOverlapJob - resolves the overlap groups; ZoneUtils.CanShareCells and IsNeighbor both dispatch on BuildOrder.m_Order
  6 CellCheckHelpers.UpdateBlocksJob, then LotSizeJobs.UpdateLotSizeJob - writes the VacantLot buffer
  7 LotSizeJobs.UpdateBoundsJob          - feeds the changed bounds back to the zone update collector
```

`CellFlags` is the whole state a cell can be in, declared at `src/Game/Game.Zones/CellFlags.cs`:

```
Blocked, Shared, Roadside, Visible, Overridden, Occupied, Selected, Redundant, Updating, RoadLeft, RoadRight, RoadBack, Highlight
```

`Visible` is the bit every consumer tests first.

**`ZoneUtils`' five constants are `const`, so a grep for their names finds only the declaration.**
The compiler inlines a `const` at every call site and the decompiler prints the literal — `ZoneUtils` itself writes `8f` and `4f` throughout — so the empty search is not disuse; the five are `CELL_SIZE = 8f`, `CELL_AREA = 64f`, `MAX_ZONE_WIDTH = 10`, `MAX_ZONE_DEPTH = 6`, `MAX_ZONE_TYPES = 339`.
Source: `src/Game/Game.Zones/ZoneUtils.cs`.

**`MAX_ZONE_TYPES = 339` is not the operative cap.**
`ZoneSystem` allocates its shader color arrays as `Vector4[1023]` and `ZoneUtils.GetColorIndex` indexes them at `4 + m_Index * 4` plus a 0–3 variant, so the highest index that fits is 253.
(UNVERIFIED: that a zone prefab at index 254 actually faults — nobody has registered that many; a mod registering enough zone prefabs and watching the load would settle it.)
Source: `src/Game/Game.Zones/ZoneUtils.cs`, `src/Game/Game.Prefabs/ZoneSystem.cs`.

**A zone's support flags and height limits are derived from its registered buildings, never authored.**
`ZoneSystem` seeds `m_MinOddHeight`, `m_MinEvenHeight`, `m_MaxHeight` at `ushort.MaxValue`, `ushort.MaxValue`, 0, and `BuildingInitializeSystem` writes narrow and corner support plus the min heights back from each level-1, non-signature building's lot and geometry, and `m_MaxHeight` from every level — so a zone with no registered buildings has nonsensical limits, and `CellOccupyJobs.ZoneAndOccupyCellsJob` consumes them.
(UNVERIFIED: whether a zone prefab registered after `BuildingInitializeSystem` has initialised the buildings referencing it gets those flags at all — registering a zone plus a matching level-1 building from a mod and reading `ZoneData.m_ZoneFlags` live would settle it.)
Source: `src/Game/Game.Prefabs/ZoneSystem.cs`, `src/Game/Game.Prefabs/BuildingInitializeSystem.cs`, `src/Game/Game.Zones/CellOccupyJobs.cs`.

## The zone index across a save

Sources: `src/Game/Game.Serialization/ResolvePrefabsSystem.cs`, `src/Game/Game.Prefabs/ZoneSystem.cs`.

```
ZoneSystem.InitializeZonePrefabs, at prefab creation:
  each zone prefab with an AreaType gets a fresh ZoneType.m_Index from GetNextIndex(), starting at 1 and reusing holes left by removed zones
  an AreaType.None prefab keeps index 0, gains ZoneFlags.SupportNarrow and height limits of 1
      // that is the "Remove Zoning" eraser

ResolvePrefabsSystem, on every load:
  FillZoneTypeArrayJob - maps each loaded zone prefab's old index to the actual prefab's new ZoneType, writing ZoneType.None where the loaded prefab resolves to nothing
  FixZoneTypeJob       - rewrites every Cell.m_Zone and every VacantLot.m_Type through that table
```

So a save whose zone prefab has gone — a mod uninstalled, a pack disabled — does not fail to load: every cell that carried the zone becomes unzoned.

**A disabled `PrefabData` marks obsolescence, not locking.**
`PrefabData` is an `IEnableableComponent` that `ResolvePrefabsSystem` disables on loaded prefabs that no longer resolve, and both the zone check below and the level-up job gate on `IsComponentEnabled` — the name reads like player-facing unlocking and is not.
Source: `src/Game/Game.Prefabs/PrefabData.cs`, `src/Game/Game.Serialization/ResolvePrefabsSystem.cs`, `src/Game/Game.Buildings/ZoneCheckSystem.cs`, `src/Game/Game.Simulation/BuildingUpkeepSystem.cs`.

## What condemns a building

Sources: `src/Game/Game.Buildings/ZoneCheckSystem.cs`.

```
ZoneCheckSystem (SystemUpdatePhase.ModificationEnd), three stages over the updated zone bounds:
  1 FindSpawnableBuildingsJob - every object with Building whose prefab has SpawnableBuildingData and not SignatureBuildingData
  2 CollectEntitiesJob        - sort and dedupe
  3 CheckBuildingZonesJob     - validate each, then add or remove Condemned and its icon:
    editor mode passes everything
    ValidateAttachedParent: passes a building attached to a parent whose prefab has PlaceholderBuildingData naming the SAME zone prefab
    ValidateZoneBlocks: false outright for the obsolete-prefab case -- ZoneData present, ZoneType not None, and PrefabData DISABLED; every other case proceeds:
        every cell under the footprint must be Visible and carry exactly that ZoneType, and the lot's front row must meet a road (the accumulated direction bits)
```

(VOLATILE: every system, job, component, field, flag, constant and `Source:` path this file names, the shader-array arithmetic included — their declarations in `Game.Zones`, `Game.Prefabs`, `Game.Buildings`, `Game.Net`, `Game.Common`, `Game.Rendering`, `Game.City`, `Game.Simulation` and `Game.Serialization` under `src/Game/`, at the files the section sources cite.)
