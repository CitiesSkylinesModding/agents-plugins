# Zoning, buildings and land value

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Zoning is painted per 8 m cell onto blocks the game generates from road edges; a rectangle of same-zoned free cells becomes a `VacantLot`, the derived buffer the spawner scores.
Demand is computed per residential density and per commercial or industrial resource, and gates a spawner that turns the best vacant lot into a level-1 building through the placement pipeline.
Land value lives on the road edge and feeds the rent every renter is re-charged each pass; renters covering the upkeep raise `BuildingCondition`, failing it lowers, and a full bar swaps the prefab for a level+1 twin while one driven far enough below empty abandons the building.
Districts are areas that scope policies and services by position.

## The map

Default reads: a zone-prefab component is reached from `SpawnableBuildingData.m_ZonePrefab`, a building-prefab component through the instance's `PrefabRef.m_Prefab`, and a parameter component is a `GetSingleton<T>` ([`ecs-in-this-game`](../../technique/ecs-in-this-game/ecs-in-this-game.md) carries the call); a row states its own shape only where it differs.

The zone, the cell and the lot:

| The game models | Component | Access shape |
| --- | --- | --- |
| A zone type | `ZonePrefab` producing `ZoneData { m_ZoneType, m_AreaType, m_ZoneFlags, m_MinOddHeight, m_MinEvenHeight, m_MaxHeight }` (`src/Game/Game.Prefabs/ZonePrefab.cs`, `ZoneData.cs`) | the runtime `ZoneType.m_Index` resolves back to the prefab entity through `ZoneSystem.GetPrefabs()` (`src/Game/Game.Prefabs/ZonePrefabs.cs`) |
| The area type | `Game.Zones.AreaType { None, Residential, Commercial, Industrial }` (`src/Game/Game.Zones/AreaType.cs`) | on `ZoneData`; office is the `ZoneFlags.Office` bit, tested by `ZoneData.IsOffice()` |
| A zone block | `Block { m_Position, m_Direction, m_Size }` entity with a `DynamicBuffer<Cell>` (`src/Game/Game.Zones/Block.cs`) | generated off a road edge, never authored; its `Owner` is that edge |
| One 8 m cell | `Cell { m_State: CellFlags, m_Zone: ZoneType, m_Height }` (`src/Game/Game.Zones/Cell.cs`, `CellFlags.cs`) | buffer element on the block; this is what the zoning tool paints |
| A buildable spot | `VacantLot { m_Area: int4, m_Type, m_Height, m_Flags }` (`src/Game/Game.Zones/VacantLot.cs`) | buffer on the block, derived by the cell pipeline ([zone-blocks-and-cells.md](zone-blocks-and-cells.md)) |
| A growable prefab | `SpawnableBuildingData { m_ZonePrefab, m_Level }` (`src/Game/Game.Prefabs/SpawnableBuildingData.cs`) | on the building prefab; its chunk is keyed by the shared `BuildingSpawnGroupData.m_ZoneType` (`src/Game/Game.Prefabs/BuildingSpawnGroupData.cs`) |
| Lot dimensions | `BuildingPrefab.m_LotWidth` / `m_LotDepth` becoming `BuildingData.m_LotSize` (`src/Game/Game.Prefabs/BuildingPrefab.cs`, `BuildingData.cs`) | per building prefab |
| Zone-side balance | `ZonePropertiesData`, `ZonePollutionData`, `ZoneServiceConsumptionData` (`src/Game/Game.Prefabs/ZonePropertiesData.cs`, `ZonePollutionData.cs`, `ZoneServiceConsumptionData.cs`) | per zone prefab |
| Building-side balance | `BuildingPropertyData { m_ResidentialProperties, m_AllowedSold, m_AllowedInput, m_AllowedManufactured, m_AllowedStored, m_SpaceMultiplier }`, `ConsumptionData`, `PollutionData` (`src/Game/Game.Prefabs/BuildingPropertyData.cs`) | per building prefab, written at initialisation from the zone's components unless the building prefab authors its own overriding components (see Mechanisms) |
| A theme | `ThemePrefab` (empty `ThemeData` marker); `ThemeObject` binds a prefab to one (`src/Game/Game.Prefabs/ThemePrefab.cs`, `ThemeObject.cs`) | `ThemeObject` sits on the asset prefab; the binding and the city's active theme are [districts-and-themes.md](districts-and-themes.md) |
| Level-up materials | `LevelUpResourceData` / `ZoneLevelUpResourceData` (`src/Game/Game.Prefabs/LevelUpResourceData.cs`, `ZoneLevelUpResourceData.cs`) | buffers: building prefab, then zone prefab, then the `BuildingConfigurationData` singleton entity, the arm chosen by buffer presence — the two `ZoneLevelUpResourceData` steps both filtered by level, and a chosen buffer with no entry for the current level requests nothing (no fallthrough) |

The placed building:

| The game models | Component | Access shape |
| --- | --- | --- |
| The building record | `Game.Buildings.Building { m_RoadEdge, m_CurvePosition, m_OptionMask, m_Flags }` (`src/Game/Game.Buildings/Building.cs`) | `m_RoadEdge` is the join to land value, service coverage and resource availability |
| Place in the world | `Game.Objects.Transform { m_Position, m_Rotation }`, `Elevation`, `Attached { m_Parent, m_OldParent, m_CurvePosition }` (`src/Game/Game.Objects/`); `Game.Buildings.Lot`, four float3 of terraformed edge heights (`src/Game/Game.Buildings/Lot.cs`) | instance components; `Transform` and `Lot` are archetype-declared (`ObjectPrefab` / `BuildingPrefab.GetArchetypeComponents`), while `Elevation` and `Attached` belong to [`placement-definitions`](../../technique/placement-definitions/placement-definitions.md); `Attached.m_OldParent` survives no save |
| Land value | `Game.Net.LandValue { m_LandValue, m_Weight }` on the road edge, plus a 128 × 128 `LandValueCell` map sampled by position (`src/Game/Game.Net/LandValue.cs`, `src/Game/Game.Simulation/LandValueCell.cs`) | rent reads the edge through `Building.m_RoadEdge`; both writers and the map are [level-up-loop.md](level-up-loop.md) |
| Its flags | `Game.Buildings.BuildingFlags { HighRentWarning, StreetLightsOff, LowEfficiency, Illuminated, Historical }` (`BuildingFlags.cs`) | `Historical` pins both ends of the condition bar — no level-up and no abandonment — honoured by the condition tick ([level-up-loop.md](level-up-loop.md)) |
| The level bar | `BuildingCondition { m_Condition }` (`src/Game/Game.Buildings/BuildingCondition.cs`) | instance component; the whole loop is [level-up-loop.md](level-up-loop.md) |
| Kind markers | `ResidentialProperty`, `CommercialProperty`, `IndustrialProperty`, `OfficeProperty`, `StorageProperty`, `ExtractorProperty` (declared by `BuildingProperties.AddArchetypeComponents`), plus `Signature` from `SignatureBuilding` (`src/Game/Game.Buildings/`) | empty tags queries dispatch on, except `CommercialProperty` and `IndustrialProperty`, which carry `m_Resources` |
| Rental state | `Renter` buffer on the building; `PropertyRenter { m_Property, m_Rent }` on the renter; `PropertyOnMarket { m_AskingRent }`; `PropertyToBeOnMarket` (each in its own file under `src/Game/Game.Buildings/`) | listing, asking rent and `PropertyRenter.m_Rent` are written in [level-up-loop.md](level-up-loop.md); the renter side belongs to [`citizens-and-households`](../citizens-and-households/citizens-and-households.md) and [`economy-and-companies`](../economy-and-companies/economy-and-companies.md) |
| Distress | `Abandoned { m_AbandonmentTime }`, `Condemned` (`src/Game/Game.Buildings/`); `Destroyed` (`src/Game/Game.Common/Destroyed.cs`) | instance components read by presence; abandonment and the abandoned-to-destroyed timer are [level-up-loop.md](level-up-loop.md), condemnation [zone-blocks-and-cells.md](zone-blocks-and-cells.md) |
| District membership | `CurrentDistrict` on the building, `BorderDistrict { m_Left, m_Right }` on a road (`src/Game/Game.Areas/BorderDistrict.cs`) | recomputed from position, never stored on the district ([districts-and-themes.md](districts-and-themes.md)) |
| The archetype | declared by `BuildingPrefab.GetArchetypeComponents` and rebuilt by `RefreshArchetype` (`src/Game/Game.Prefabs/BuildingPrefab.cs`) | not a component to read — `RefreshArchetype` folds in every attached `ComponentBase` plus `Created` and `Updated`, and a `BuildingUpgradeElement` buffer further adds `InstalledUpgrade`, `SubNet` and `SubRoute`; the declared list is a floor |

Specialized industry, which is areas rather than zones — there is no `ZonePrefab` for forestry or mining, whatever the toolbar's grouping suggests:

| The game models | Component | Access shape |
| --- | --- | --- |
| The painted area | `LotPrefab` with `ExtractorArea`, producing `ExtractorAreaData { m_MapFeature, m_ObjectSpawnFactor, m_MaxObjectArea, m_RequireNaturalResource, m_WorkAmountFactor }` (`src/Game/Game.Prefabs/LotPrefab.cs`, `ExtractorArea.cs`) | per area prefab; `m_RequireNaturalResource` is the field to check, not a fact per specialization |
| The area instance | `Game.Areas.Extractor { m_ResourceAmount, m_MaxConcentration, m_ExtractedAmount, m_WorkAmount, m_HarvestedAmount, m_TotalExtracted, m_WorkType }` (`src/Game/Game.Areas/Extractor.cs`) | instance component |
| The resource layer | `MapFeature { None, Area, BuildableLand, FertileLand, Forest, Oil, Ore, SurfaceWater, GroundWater, Fish }` (`src/Game/Game.Areas/MapFeature.cs`) | enum on `ExtractorAreaData` |
| The hub building | `Game.Buildings.ExtractorFacility { m_Flags, m_Timer, m_MainBuildingFlags }` (`src/Game/Game.Buildings/ExtractorFacility.cs`) | instance; its production belongs to [`economy-and-companies`](../economy-and-companies/economy-and-companies.md) |
| Pollution | a zoned building's comes from `ZonePollution` on its zone prefab, scaled by lot size into `PollutionData`; an extractor hub's from its building prefab's own `Pollution` component (`src/Game/Game.Prefabs/ZonePollution.cs`, `Pollution.cs`) | per prefab either way, and no tier, band or threshold exists anywhere: pollution is three floats |

Where the tuning numbers live, all singletons:

| Family of numbers | Component |
| --- | --- |
| Land-value baseline, bonus and penalty multipliers, the common-factor cap | `LandValueParameterData` (`src/Game/Game.Prefabs/LandValueParameterData.cs`) |
| Rent bases and land-value modifiers per area type, upkeep level exponents, the mixed-building rent share | `EconomyParameterData.m_RentPriceBuildingZoneTypeBase`, `m_LandValueModifier`, `m_ResidentialUpkeepLevelExponent`, `m_CommercialUpkeepLevelExponent`, `m_IndustrialUpkeepLevelExponent`, `m_MixedBuildingCompanyRentPercentage` (`src/Game/Game.Prefabs/EconomyParameterData.cs`) |
| Demand weights, neutrals and requirements | `DemandParameterData` (`src/Game/Game.Prefabs/DemandParameterData.cs`) |
| Spawn-suitability significances and neutral land values | `ZonePreferenceData` (`src/Game/Game.Prefabs/ZonePreferenceData.cs`) |
| Condition increment per area type and decrement, leveling notification prefabs, the fallback material list (a `ZoneLevelUpResourceData` buffer on the same entity), the abandoned-destroy delay | `BuildingConfigurationData` (`src/Game/Game.Prefabs/BuildingConfigurationData.cs`) |

## Traps

Each sibling carries further traps beside its listings.

**`ZoneType.m_Index` is a runtime index: assigned at load, remapped on every load, stable across nothing.**
`Game.Prefabs.ZoneSystem` hands each created zone prefab with an `AreaType` a fresh index (reusing holes left by removed zones), and on every load `ResolvePrefabsSystem` rewrites every `Cell` and `VacantLot` through a translation table, writing `ZoneType.None` — silently unzoning — where a loaded prefab no longer resolves; a mod persisting an index in its own data, or shipping a zone type a save can outlive, gets exactly that.
Source: `src/Game/Game.Prefabs/ZoneSystem.cs`, `src/Game/Game.Serialization/ResolvePrefabsSystem.cs`.

**A field initializer on a prefab-authoring class is a Unity-serialized default, not the value.**
`LandValuePrefab` and `ZonePreferencePrefab` initialize every field in C#, and every prefab-authoring class this file routes into carries the same shape: `ComponentBase`, the base of `PrefabBase` and of every such component, descends from `ScriptableObject`, so the shipped asset overwrites whatever a field carries and nothing in the decompile marks which initializer survived — read live at 1.6.0f1, many fields of both parameter components differ from their initializers, one family by three orders of magnitude.
Only a `const` or `static readonly` the code reads is citable as a number.
Source: `src/Game/Game.Prefabs/ComponentBase.cs`, `src/Game/Game.Prefabs/LandValuePrefab.cs`, `src/Game/Game.Prefabs/ZonePreferencePrefab.cs`.

**There is no `Office` area type.**
Office is `AreaType.Industrial` plus the `ZoneFlags.Office` bit, so every switch on area type has three real arms and office rides inside the industrial one; the spawner's scoring arm keys on an empty `ProcessEstimate` buffer instead of the flag ([building-spawning.md](building-spawning.md)).
Source: `src/Game/Game.Zones/AreaType.cs`, `src/Game/Game.Prefabs/ZoneFlags.cs`, `src/Game/Game.Prefabs/ZoneData.cs`.

**No prefab stores a density.**
`ZoneDensity` is computed from `ZoneData` and `ZonePropertiesData` by the formula below and stored nowhere; the commercial branch has two outcomes, so a medium-density commercial zone cannot exist.
Source: `src/Game/Game.Buildings/PropertyUtils.cs`, `src/Game/Game.Prefabs/ZoneDensity.cs`.

**A zone type has no lot dimensions.**
Its apparent tile range is the observed spread of `BuildingData.m_LotSize` over the building prefabs whose `SpawnableBuildingData.m_ZonePrefab` names it — no field on any zone component could hold one.
Source: `src/Game/Game.Prefabs/BuildingPrefab.cs`, `src/Game/Game.Prefabs/ZonePrefab.cs`, `src/Game/Game.Prefabs/ZonePropertiesData.cs`.

**A game mode rewrites this topic's balance data on load.**
The `Game.Prefabs.Modes` classes reassign parameter singletons and multiply per-prefab consumption and pollution data after prefab initialisation on every load — the save's mode, or `NormalMode` when it names none, each applying its own authored list — so a retune written at initialisation can silently vanish or drift; find what a mode touches by the namespace.
Source: `src/Game/Game.Prefabs.Modes/GameModeSystem.cs`, `src/Game/Game.Prefabs.Modes/ModeSetting.cs`.

**A signature building has no stat catalog: its stats are a growable's components.**
`BuildingPropertyData`, `ConsumptionData` and `PollutionData` reach its prefab through the same `IZoneBuildingComponent` dispatch (Mechanisms below), and `BuildingData.m_LotSize` is written by `BuildingInitializeSystem` as for any building.
Source: `src/Game/Game.Prefabs/SignatureBuilding.cs`, `src/Game/Game.Prefabs/ZonePrefab.cs`, `src/Game/Game.Prefabs/BuildingInitializeSystem.cs`.

## Formulas

Rent, asked per renter and re-derived every pass:

```
PropertyUtils.GetRentPricePerRenter (src/Game/Game.Buildings/PropertyUtils.cs):
  base    = EconomyParameterData.m_RentPriceBuildingZoneTypeBase[areaType]   // .x res, .y com, .z ind
  lvMod   = EconomyParameterData.m_LandValueModifier[areaType]               // same switch
  asked   = ZonePropertiesData.m_IgnoreLandValue
            ? base * level * lotSize * m_SpaceMultiplier
            : (landValue * lvMod + base * level) * lotSize * m_SpaceMultiplier
  renters = IsMixedBuilding                       // m_ResidentialProperties > 0 and sells or manufactures
            ? round(m_ResidentialProperties / (1 - m_MixedBuildingCompanyRentPercentage))
            : CountProperties()                   // residential count + (sells ? 1 : 0) + (stores or makes ? 1 : 0)
  rent per renter = round(asked / renters)
  // landValue is the road edge's Game.Net.LandValue; level defaults to 1 without SpawnableBuildingData
```

`GetRentPriceDebugInfo`, directly under it, labels the terms A–D in the game's own words.
`m_IgnoreLandValue` removes the land-value term wherever the caller passes it — the rent pass does, but `PropertyProcessingSystem` omits the optional `ignoreLandValue` argument when it computes a fresh listing's asking rent, so a first asking rent includes land value even in such a zone.

Upkeep, computed once per prefab (when, and what that bakes in, is [level-up-loop.md](level-up-loop.md)):

```
PropertyRenterSystem.GetUpkeep (src/Game/Game.Simulation/PropertyRenterSystem.cs):
  residential:             round(pow(level, m_ResidentialUpkeepLevelExponent) * ZoneServiceConsumptionData.m_Upkeep * lotSize)
  commercial / industrial: round(pow(level, m_CommercialUpkeepLevelExponent or m_IndustrialUpkeepLevelExponent) * ZoneServiceConsumptionData.m_Upkeep * lotSize * (isStorage ? 0.5 : 1))
  AreaType.None:           exponent 1, so the factor is level itself, on the commercial/industrial return
```

The level-up bar's two ends:

```
BuildingUtils.GetLevelingCost (src/Game/Game.Buildings/BuildingUtils.cs):
  level >= 5 -> 1073741823                        // level 5 never levels again; returned before the modifier below
  residential:             CountProperties() * round(pow(2, 2 * level) * 40)
  commercial / industrial: CountProperties() * round(pow(2, 2 * level) * 160), * 4 when m_AllowedStored != NoResource
  AreaType.None:           1073741823, returned before the modifier below
  then CityModifierType.BuildingLevelingCost applied

BuildingUtils.GetAbandonCost:
  the leveling cost, except at level 5 where it is level 4's
  multi-apartment residential: round(cost * (6 - level) / sqrt(m_ResidentialProperties))
```

Density, computed rather than stored:

```
PropertyUtils.GetZoneDensity (src/Game/Game.Buildings/PropertyUtils.cs):
  Residential: !m_ScaleResidentials -> Low
               m_ResidentialProperties < m_SpaceMultiplier -> Medium, else High
  Commercial:  m_SpaceMultiplier > 1 -> High, else Low
  Industrial:  office ? (m_SpaceMultiplier < 10 ? Low : High) : Low
  AreaType.None: asserts and returns Low
```

`ZoneProperties`' field tooltips state the same relations in the authors' own words (`src/Game/Game.Prefabs/ZoneProperties.cs`).

Apartments per building, computed at prefab initialisation for each level — skipped entirely for a building prefab authoring its own `BuildingProperties`, whose fields then stand unscaled:

```
ZoneProperties.GetBuildingPropertyData (src/Game/Game.Prefabs/ZoneProperties.cs):
  m_ResidentialProperties = round((m_ScaleResidentials ? (1 + 0.25 * (level - 1)) * lotSize : 1) * ZoneProperties.m_ResidentialProperties)
```

There is no level-3-or-5 branch anywhere: apartment counts jump at whatever levels the rounding makes them jump for a given lot.

## Mechanisms

| Mechanism | Vanilla owner | Listing |
| --- | --- | --- |
| Blocks from road edges, cell validity, vacant lots | `Game.Zones.BlockSystem`, `Game.Zones.CellCheckSystem` | [zone-blocks-and-cells.md](zone-blocks-and-cells.md) |
| Condemnation, the zone index remap | `ZoneCheckSystem`, `ResolvePrefabsSystem` | [zone-blocks-and-cells.md](zone-blocks-and-cells.md) |
| Demand, in both directions | `ResidentialDemandSystem`, `CommercialDemandSystem`, `IndustrialDemandSystem`, `CountResidentialPropertySystem` | [demand.md](demand.md) |
| Demand becoming a building | `ZoneSpawnSystem`, `ZoneEvaluationUtils` | [building-spawning.md](building-spawning.md) |
| Land value, rent, collection, the market | `LandValueSystem`, `RentAdjustSystem`, `PropertyRenterSystem`, `PropertyProcessingSystem` | [level-up-loop.md](level-up-loop.md) |
| Condition, level-up, abandonment | `BuildingUpkeepSystem` | [level-up-loop.md](level-up-loop.md) |
| Districts, service scoping, themes, zone-built unlocks | `CurrentDistrictSystem`, `ServiceDistrictSystem`, `AreaUtils`, `CityConfigurationSystem`, `ZoneBuiltRequirementSystem` | [districts-and-themes.md](districts-and-themes.md) |
| Zone data flowing into building prefabs | `ZonePrefab` dispatching `IZoneBuildingComponent`, `BuildingInitializeSystem` | below |

**Zone data flowing into building prefabs.**
`IZoneBuildingComponent` (`src/Game/Game.Prefabs/IZoneBuildingComponent.cs`) is how zone-side components flow into building prefabs: its three methods let a zone prefab contribute components and data to each of its building prefabs at initialisation, dispatched by `ZonePrefab` over whatever implementors it carries — find the implementors by the interface.
A mod adding a zone type implements it — or attaches stock implementors — or its buildings get no property, pollution or consumption data, except what a building prefab authors through its own `BuildingProperties`, `Pollution` or `ServiceConsumption`, each of which overrides the zone's counterpart for that prefab.
`BuildingInitializeSystem` completes the flow outside the dispatch — the zone's `ZoneType` becomes the building prefab's `BuildingSpawnGroupData` chunk key and the upkeep bake writes `ConsumptionData.m_Upkeep` ([level-up-loop.md](level-up-loop.md)), a signature prefab taking only the bake — and writes back the other way, deriving the zone's support flags and height limits from its registered buildings ([zone-blocks-and-cells.md](zone-blocks-and-cells.md)).

## Bridges

- [`prefabs-and-assets`](../../technique/prefabs-and-assets/prefabs-and-assets.md) — everything authored here is a prefab, and the `IZoneBuildingComponent` dispatch above is prefab-lifecycle machinery; `ZoneBlockPrefab.LateInitialize` building an `EntityArchetype` into `ZoneBlockData` is the archetype-declaration pattern.
- [`placement-definitions`](../../technique/placement-definitions/placement-definitions.md) — a growable is defined, never created directly: the spawner writes `CreationDefinition` plus `ObjectDefinition`; level-up's prefab swap goes through `UnderConstruction` and the construction pipeline instead.
- [`save-serialization`](../../technique/save-serialization/save-serialization.md) — what a mod persists instead of a zone index; the remap trap above is why.
- [`ecs-in-this-game`](../../technique/ecs-in-this-game/ecs-in-this-game.md) — the `GetSingleton<T>` behind every parameter row; `BuildingSpawnGroupData` as a shared-component chunk filter on zone type; `UpdateFrame` sharding the rent and condition passes, so a system's interval is not how often one building is touched.
- [`economy-and-companies`](../economy-and-companies/economy-and-companies.md) — this topic owns the rent a property asks and the upkeep it costs; that one owns what the renter earns, met at `EconomyUtils.GetCompanyMaxProfitPerDay` and `GetCompanyTotalWorth`, and commercial and industrial demand are per resource, so the demand loop is half theirs; the `ProcessEstimate` buffer on a zone prefab is their data on this topic's prefab.
- [`city-services-and-coverage`](../city-services-and-coverage/city-services-and-coverage.md) — the `ServiceCoverage` and `ResourceAvailability` buffers the land-value edge job reads are written there; district service scoping's mechanism lives here.
- [`citizens-and-households`](../citizens-and-households/citizens-and-households.md) — the other side of `PropertyRenter`: that topic decides that a household seeks, moves in and leaves; `PropertyUtils.GetPropertyScore` and `GetGenericApartmentQuality` sit on the seam, built from this topic's fields and consumed by that one.
- [`environment-and-pollution`](../environment-and-pollution/environment-and-pollution.md) — `ZonePollution` is the source term; the pollution cell maps the spawner and the land-value job sample back are that topic's.
- [`city-state-and-progression`](../city-state-and-progression/city-state-and-progression.md) — `ZoneBuiltRequirementData` unlocks, tallied by the chunk pass in [districts-and-themes.md](districts-and-themes.md) with level-up's `ZoneBuiltLevelUpdate` records covering only level transitions; district policies arrive through its policy machinery and land in this topic's `DistrictModifier` buffer.
- [`simulation-time-and-units`](../simulation-time-and-units/simulation-time-and-units.md) — owns what a frame, an interval and `kUpdatesPerDay` are worth in game time.

(VOLATILE: every component, field, flag, enum, system and `Source:` path this file names, the parameter-ownership map most of all — their declarations under `src/Game/` in `Game.Zones`, `Game.Prefabs`, `Game.Prefabs.Modes`, `Game.Buildings`, `Game.Areas`, `Game.Net`, `Game.Routes`, `Game.Objects`, `Game.Tools`, `Game.Common`, `Game.City`, `Game.Economy`, `Game.Simulation` and `Game.Serialization`, at the files the rows, traps and fences cite.)
