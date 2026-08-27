# Natural resources

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`NaturalResourceCell { m_Fertility, m_Ore, m_Oil, m_Fish }`, each a `NaturalResourceAmount { ushort m_Base, ushort m_Used }` (`src/Game/Game.Simulation/NaturalResourceCell.cs`).
Available is `m_Base − m_Used` everywhere the game reads it; the regeneration job writes `m_Base` only for fish, capped at the literal `10000` — the land resources' bases are map data a game-mode boost can push far higher, clamped at 65535.
`MapFeature` (`src/Game/Game.Areas/MapFeature.cs`) is the enum naming the layers and is wider than the cell: `Forest` is derived from placed trees, `GroundWater` from its own map, `SurfaceWater` from the GPU depths, `Area` and `BuildableLand` from geometry.
An area entity carries the per-district results as a `MapFeatureElement { m_Amount, m_RenewalRate }` buffer, one slot per enum member — the specialised-industry gate `economy-and-companies` reads.

## Regeneration and scarring

Source: `src/Game/Game.Simulation/NaturalResourceSystem.cs`.

```
per cell, per update:
fertility:
    m_Used = min(m_Base, max(0, m_Used − 25 + RoundToIntRandom(groundPollution * m_FertilityGroundMultiplier / 32)))
    // 25 is the literal OnUpdate compiles into the job
fish:
    over the water-simulation sub-cells covering this cell, with d = max(0, depth − 2):
        waterTerm += d;  pollutionTerm += d * m_Polluted     // both scaled by 300 / subCellsPerCell
    pollutionTerm += waterVolume * noisePollution * 6.25e-05
    newBase = min(10000, waterVolume); a newBase of 1..19 snaps to 0, and a current m_Base of 1..19 counts as 20 in the test below
    m_Base is written (and the area tree notified) only when |newBase − current| >= 20
    m_Used chases clamp(pollutionTerm * 50, 0, 10000):
        up by RoundToIntRandom(pollutionTerm * 3.125) per update, down by the flat 25
```

Ground pollution scars used fertility rather than depleting the endowment: the effect is on `m_Used`, is bounded by `m_Base`, and reverses at the flat rate once the pollution goes.
Water depth sets the fish stock, and water pollution plus traffic noise sets fish's standing loss — fish is fully derived, not extracted down in the usual sense.

## Extraction and depletion

Source: `src/Game/Game.Simulation/AreaLotSimulationSystem.cs`, `src/Game/Game.Prefabs/ExtractorParameterData.cs`.

How much an extractor wants (`Extractor.m_ExtractedAmount`) belongs to `economy-and-companies`; this layer turns it into `m_Used`:

```
pick the best cell under the extractor's triangles
fertility, fish:  m_Used += min(what the chosen cell scores, extracted), capped at 65535
    // only ore and oil take the raw extracted amount; every available read clamps at 0
ore, oil:
    original = m_Base * 1e-4;  current = (m_Base − m_Used) * 1e-4
    m_Used  += RoundToIntRandom(mu * original * exp(−(log(original) − log(current))) * extracted * 10000)
    // the expression reduces to mu * (m_Base − m_Used) * extracted, with mu = 1 / m_OreConsumption or 1 / m_OilConsumption
a cell with nothing available is skipped
```

The reduced form is the mechanism: the fuller the deposit, the more a unit of extraction depletes it, an exponential approach to empty that never quite arrives.

**`GetUnlimitedTotalAmount` is public, uncalled and arithmetically broken.**
It sits beside `GetUnlimitedUsage`, divides integers inside `log` and binds `/ mu` to the wrong term; nothing calls it and neither should a mod.
Source: `src/Game/Game.Simulation/AreaLotSimulationSystem.cs`.

**`NaturalResourceCell.GetUsedResources` returns oil twice, and `GetStride` omits fish.**
The `w` component repeats `m_Oil.m_Used` instead of reading fish, and `AreaResourceSystem` subtracts that result from the base amounts — so a district's fish figure is discounted by oil's usage, not fish's; a mod wanting the four used amounts reads the fields itself.
Source: `src/Game/Game.Simulation/NaturalResourceCell.cs`, `src/Game/Game.Areas/AreaResourceSystem.cs`.

## The per-district recompute, and forest

Sources: `src/Game/Game.Areas/AreaResourceSystem.cs`, `src/Game/Game.Objects/ObjectUtils.cs`.

`AreaResourceSystem` rebuilds an updated area's `MapFeatureElement` buffer and its extractor's `Extractor.m_ResourceAmount` and `m_MaxConcentration`: the four cell resources sum over the area's triangles, groundwater and buildable land derive, and `Forest` sums the live wood of the trees inside the triangles, its renewal slot the summed growth rate.
A tree's wood is `ObjectUtils.CalculateWoodAmount` — a growth-stage lerp times the prefab's `TreeData.m_WoodAmount`, scaled down by damage and by `Plant.m_Pollution` ([map-dynamics.md](map-dynamics.md)), zero for a dead tree or a stump — and the extractor's `m_MaxConcentration` is the single best tree's `wood / m_WoodAmount`, a per-tree max capped at 1 rather than an area average, so forest depletes by cutting or sickening trees rather than by any cell write.

## Where the layer comes from, and how it can refill

`NaturalResourceSystem.SetDefaults` fills fertility, ore and oil from Perlin noise on a new game unless the map asset overrides them; the editor paints all of it through the brush path in [cell-maps.md](cell-maps.md).
`GameModeNaturalResourcesAdjustSystem` (`src/Game/Game.Simulation/GameModeNaturalResourcesAdjustSystem.cs`) multiplies every cell's `m_Base` once by a game-mode factor, then each update subtracts `m_Base * percentPerDay / 100 / kUpdatesPerDay` from `m_Used` for oil, ore and fertility.
"Ore and oil are non-renewable" is therefore a property of the default mode, not of the mechanism: `ModeSettingData.m_PercentOilRefillAmountPerDay` and its siblings turn them renewable.

(VOLATILE: every system, component, field, enum, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Prefabs`, `Game.Prefabs.Modes`, `Game.Areas` and `Game.Objects`, at the files the sections cite.)
