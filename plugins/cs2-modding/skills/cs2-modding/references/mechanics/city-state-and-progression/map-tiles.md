# Map tiles

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

A map tile is an ordinary `Game.Areas` area: `MapTilePrefab : AreaPrefab` adds `MapTileData`, a `MapFeatureData` buffer, `AreaGeometryData` and `TilePurchaseCostFactor` to the prefab, and `MapTile`, a `MapFeatureElement` buffer and `Geometry` to the archetype (`src/Game/Game.Prefabs/MapTilePrefab.cs`).
Ownership is the absence of `Game.Common.Native`: `MapTilePurchaseSystem.UnlockTile` removes `Native` and adds `Updated`, the owned query is `[MapTile, Exclude<Native>]` and the purchasable one `[MapTile, Native, Area]` (`src/Game/Game.Simulation/MapTilePurchaseSystem.cs`).
`MapFeature` (`src/Game/Game.Areas/MapFeature.cs`) is the declared feature set and indexes both buffers.

## Generation and start tiles

Source: `src/Game/Game.Areas/MapTileSystem.cs` (`PostTool`; `PostDeserialize`).

```
PostDeserialize:
    NewGame at version >= Version.editorMapTiles: drop null entries from m_StartTiles, remove Native from the rest
    NewGame below that version: LegacyGenerateMapTiles(editorMode: false); NewMap: LegacyGenerateMapTiles(editorMode: true)
LegacyGenerateMapTiles(editorMode):
    destroy existing tiles; create 529 entities from the MapTilePrefab archetype (const LEGACY_GRID_WIDTH = 23, LEGACY_GRID_LENGTH = 23, LEGACY_CELL_SIZE = 623.3043f, centred on the origin)
    add Native to all of them unless editorMode              // a NewMap generation leaves every tile owned
    AddOwner on the 3×3 block (10,10)..(12,12), index = y * 23 + x: remove Native, append to m_StartTiles
```

`m_StartTiles` is a `NativeList<Entity>` the system serializes itself, pruned on tile deletion; `GetStartTiles()` is public and feeds both the permit floor and the cost exclusion below.

## Permits, price, upkeep, status

Source: `src/Game/Game.Simulation/MapTilePurchaseSystem.cs` (`GetAvailableTiles`, `UpdateStatus`, `CalculateOwnedTilesCost`, `GetMapTileUpkeepCostMultiplier`, `GetFeatureAmount`).

```
constants: static readonly int kAutoUnlockedTiles = 9
           kMapTileSizeModifier = 1.0 / 623.304347826087²        // the reciprocal of a tile's area
           kResourceModifier    = 8.0718994140625E-07
           kMapFeatureBaselineModifiers[8] = { size, size, resource, resource, resource, resource, 1.0, resource }
           GetBaselineModifier(j) = j < 8 ? kMapFeatureBaselineModifiers[j] : 1.0

permits = max(0, max(kAutoUnlockedTiles, startTiles.Length) + Σ m_MapTiles over [MilestoneData, Exclude<Locked>] - ownedTiles)

tileValue(tile) = Σ_j MapFeatureElement[j].m_Amount * GetBaselineModifier(j) * 10.0 * MapFeatureData[j].m_Cost * TilePurchaseCostFactor.m_Amount
CalculateOwnedTilesCost(includeSelection):
    over the selection when includeSelection and one exists, else over the owned tiles;
    start tiles are skipped; returns Σ tileValue, and adds each tile's amounts into m_FeatureAmounts

UpdateStatus:
    permits == 0            → status = IsMilestonesLeft() ? NoCurrentlyAvailable : NoAvailable; return
    no selection            → status = NoSelection; return
    owned = owned-tile count; sel = CalculateOwnedTilesCost(true); num5 = sel
    per selected tile carrying MapTile and Native: values += tileValue; num5 += tileValue;
        m_FeatureAmounts[j] += amount   // a second time for the same tiles
    sort values ascending; cost = Σ_k values[len - 1 - k] * (owned + k)
    m_Upkeep = num5 * mult(owned + selected) - sel * mult(owned)
    selected > permits      → status |= InsufficientPermits
    cost > moneyAmount      → status |= InsufficientFunds

mult(tileCount) = tileCount <= kAutoUnlockedTiles ? 0 : EconomyParameterData.m_MapTileUpkeepCostMultiplier.Evaluate(tileCount)
CalculateOwnedTilesUpkeep() = round(CalculateOwnedTilesCost(false) * mult(owned))
GetFeatureAmount(feature) = m_FeatureAmounts[(int)feature], FertileLand converted through NaturalResourceSystem.ResourceAmountToArea   // every other feature raw
```

Price is linear in the tiles already owned, and the sort pairs the most valuable selected tile with the lowest multiplier.
Permits are recomputed from the unlocked milestone set on every ask — there is no permit component and no permit currency — and a milestone contributes the moment its prefab unlocks.
`GetMapTileUpkeepEnabled()` is a probe, not a flag: false under `unlockMapTiles`, otherwise true on the first positive sample of the curve at `0, 10, …, 100`.

**The whole milestone track's permits need not reach every tile on the grid.**
The ceiling is `max(kAutoUnlockedTiles, startTiles.Length) + Σ m_MapTiles` over every milestone prefab, and the grid is the `MapTile` entity count; at 1.6.0f1 the ceiling falls short of the legacy grid, and the remainder is reachable only through `CityConfigurationSystem.unlockMapTiles` — re-derive with `ecs_query` on `Game.Prefabs.MilestoneData` summing `m_MapTiles` against `ecs_query` on `Game.Areas.MapTile`.
Source: `src/Game/Game.Simulation/MapTilePurchaseSystem.cs`, `src/Game/Game.Areas/MapTileSystem.cs`.

**`MapFeature` has nine members and the modifier array eight.**
`Fish` is index 8, so its baseline modifier is the out-of-range `1.0` where `Ore` and `Oil` take `kResourceModifier`; a `MapFeatureData.m_Cost` for fish above zero makes fish dominate the tile price, and nothing warns.
Source: `src/Game/Game.Areas/MapFeature.cs`, `src/Game/Game.Simulation/MapTilePurchaseSystem.cs`.

**`MapTilePurchaseSystem` is in no update phase, and its values are stale unless the tile view is open.**
`MapTilesUISystem.OnUpdate` calls `Update()` by hand while `mapTileViewActive`; `PurchaseSelection()` re-runs `UpdateStatus()` itself before spending, and `CityServiceBudgetSystem` calls `CalculateOwnedTilesUpkeep()` directly.
Source: `src/Game/Game.Simulation/MapTilePurchaseSystem.cs`, `src/Game/Game.Common/SystemOrder.cs`, `src/Game/Game.UI.InGame/MapTilesUISystem.cs`, `src/Game/Game.Simulation/CityServiceBudgetSystem.cs`.

**`TilePurchaseErrorFlags` is a `[Flags]` enum assigned as a scalar in two branches.**
`NoCurrentlyAvailable`, `NoAvailable` and `NoSelection` return early alone; `InsufficientPermits` and `InsufficientFunds` are OR-ed and are the only pair that coexists, and the install's locale carries one composite `MapTilePurchase.PURCHASE_STATUS[…]` key for exactly that pair.
Source: `src/Game/Game.Simulation/TilePurchaseErrorFlags.cs`, `src/Game/Game.Simulation/MapTilePurchaseSystem.cs`, `Cities2_Data/Content/Game/Locale.cok`.

Selection is the generic tool: `MapTilePurchaseSystem.selecting` sets `SelectionToolSystem.selectionType = SelectionType.MapTiles`, and `SelectionToolSystem` refuses map-tile selection in a game while `GetAvailableTiles() == 0`, the editor exempt (`src/Game/Game.Tools/SelectionToolSystem.cs`).

(VOLATILE: every system, component, field, property, enum, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Areas`, `Game.Prefabs`, `Game.Common`, `Game.Tools`, `Game.UI.InGame` and the root `Game` namespace, at the files the sections cite; the `MapTilePurchase.*` key family — the install's `Locale.cok`, which no C# declares; and the reach fact the permits trap states, re-derived by the query beside it.)
