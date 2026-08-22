# City state and progression

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

The city is one entity, held by `CitySystem.City`, and what the city tracks as a whole is the set of components and buffers on it; `CitySystem` itself is not a `GetSingleton<T>` target, while a component that lives only on the city entity is (`MilestoneSystem`, `DevTreeSystem` read `MilestoneLevel` and `DevTreePoints` that way).
Statistics are entities of their own, one sample buffer per `(StatisticType, parameter)` key, fed through a queue the owning system hands out ([statistics.md](statistics.md)).
Progression is a chain: XP producers enqueue gains, `MilestoneSystem` steps the milestone level when XP crosses the next prefab's requirement, and the milestone pays money and credit directly, dev points and map-tile permits through events and queries, and an `Unlock` event that cascades ([progression.md](progression.md)).
Unlocking is one enableable tag, one requirement buffer and a fixpoint loop, and every requirement kind is a prefab that locks itself until its own evaluator emits the event ([unlocking.md](unlocking.md)).
A map tile is an area entity whose ownership is the absence of `Native`; permits are a budget recomputed from the unlocked milestones, and price is a per-tile value scaled by how many tiles are already owned ([map-tiles.md](map-tiles.md)).
A policy lives in a `Policy` buffer on whatever entity it applies to, and its effect is a modifier buffer rebuilt from scratch on every change ([policies.md](policies.md)).
A notification is an icon entity raised through a per-frame command buffer, and the icon-prefab family is wider than the simulation's failure states ([notifications.md](notifications.md)).

## The map

Default reads: city state is `EntityManager.GetComponentData<T>(citySystem.City)` or `GetBuffer<T>(citySystem.City)` — the entity index is serialized and moves between saves, so it is read from the system each time, never cached; a prefab component is enumerated with `ecs_query` on the component, or reached through an instance's `PrefabRef`.

On the city entity (`CitySystem.PostDeserialize` in `src/Game/Game.Simulation/CitySystem.cs` is the declared roster):

| The game models | Component | Access shape |
| --- | --- | --- |
| Milestone reached | `Game.City.MilestoneLevel { m_AchievedMilestone }` | zero before the first milestone; on the shipped roster `MilestoneData.m_Index` starts at 1 and the locale's `MILESTONE_NAME:0` has no prefab (`ecs_query` re-derives it) |
| Experience | `Game.City.XP { m_XP, m_MaximumPopulation, m_MaximumIncome, m_XPRewardRecord }` | `CitySystem.XP` caches `m_XP` each frame; `XPRewardFlags` has the one member `ElectricityGridBuilt` |
| Development points | `Game.City.DevTreePoints { m_Points }` | `DevTreeSystem.points` is the getter/setter pair |
| City options | `Game.City.City { m_OptionMask }`, one bit per `CityOption` member | `CityUtils.CheckOption`; derived from the active policies and rebuilt on every refresh, so a direct write is overwritten ([policies.md](policies.md)) |
| City-wide modifiers | `Game.City.CityModifier { float2 m_Delta }` buffer, indexed by `CityModifierType` | `CityUtils.ApplyModifier` / `GetModifier`; length is `max(used type) + 1`, and a missing index reads as `default` ([policies.md](policies.md)) |
| Active policies | `Game.Policies.Policy { m_Policy, m_Flags, m_Adjustment }` buffer | written through a `Modify` event, and seeded once by `DefaultPoliciesSystem` ([policies.md](policies.md)) |
| Population, tourism | `Game.City.Population`, `Game.City.Tourism` | contents belong to `citizens-and-households`; `CountHouseholdDataSystem` writes `Population`, `TourismSystem` writes `Tourism` |
| Money, credit, loan, fees, trade costs, specialization | `PlayerMoney`, `Creditworthiness`, `Loan`, `ServiceFee` buffer, `TradeCost` buffer, `SpecializationBonus` buffer | belong to `economy-and-companies`; a milestone writes the first two directly ([progression.md](progression.md)) |
| Danger | `Game.City.DangerLevel` | the default read; `CityDangerLevelSystem` overwrites it each pass with the max over live events' `Game.Events.DangerLevel` (`environment-and-pollution`'s) |

City-level configuration is on no component: `CityConfigurationSystem` (`src/Game/Game.City/CityConfigurationSystem.cs`) holds the city name, theme (`defaultTheme` resolves in `zoning-buildings-and-land-value`), required content, the option toggles — `unlockAll` and `unlockMapTiles` among them — and `usedMods` as properties, and serializes them with the camera as its own save section (`save-serialization` owns the gates); `usedMods` is cumulative, every mod the city has ever been saved with.

Statistics ([statistics.md](statistics.md)):

| The game models | Component | Access shape |
| --- | --- | --- |
| A statistic's definition | `Game.Prefabs.StatisticsData { m_Category, m_Group, m_StatisticType, m_CollectionType, m_UnitType, m_Color, m_Stacked }` on a `StatisticsPrefab` | prefab; a `ParametricStatistic` adds a `StatisticParameterData` buffer, one instance per element |
| A statistic's series | `Game.City.CityStatistic { m_Value, m_TotalValue }` buffer on the statistic instance | `CityStatisticsSystem.GetLookup()` maps `StatisticsKey(type, parameter)` to the entity; `GetStatisticValue(lookup, bufferLookup, type, parameter)` reads the latest total and returns 0 for an unknown key |
| A statistic-driven trigger | `Game.Prefabs.StatisticTriggerData` | `StatisticTriggerSystem` turns it into `TriggerType.StatisticsValue` |

Progression ([progression.md](progression.md)):

| The game models | Component | Access shape |
| --- | --- | --- |
| XP per head and per happiness point | `Game.Prefabs.XPParameterData { m_XPPerPopulation, m_XPPerHappiness }` | `GetSingleton<T>`; `XPParametersMode` under `src/Game/Game.Prefabs.Modes/` can rewrite it on load |
| XP for a placed object, upgrade or net | `PlaceableObjectData.m_XPReward`, `ServiceUpgradeData.m_XPReward`, `PlaceableNetData.m_XPReward` | prefab |
| A milestone | `Game.Prefabs.MilestoneData { m_Index, m_Reward, m_DevTreePoints, m_MapTiles, m_LoanLimit, m_XpRequried, m_Major, m_IsVictory }` | `ecs_query` on the component; `MilestoneSystem.TryGetMilestone` is a linear scan over it; the field is spelled `m_XpRequried`; a milestone's name and description are the locale keys `Progression.MILESTONE_NAME:N` / `MILESTONE_DESCRIPTION:N` |
| A dev-tree node | `Game.Prefabs.DevTreeNodeData { m_Cost, m_Service }` plus a `DevTreeNodeRequirement` buffer | `ecs_query` on the component; `DevTreeSystem.Purchase(node)` is the gate |
| A node's service | `Game.Prefabs.ServiceData { m_Service, m_BudgetAdjustable }` on a `ServicePrefab` | what the service offers belongs to `city-services-and-coverage` |

Unlocking ([unlocking.md](unlocking.md)):

| The game models | Component | Access shape |
| --- | --- | --- |
| Locked | `Game.Prefabs.Locked`, an empty enableable tag | `HasEnabledComponent<Locked>`, never `HasComponent`; the tag stays on the entity after unlocking |
| A prefab's prerequisites | `Game.Prefabs.UnlockRequirement { m_Prefab, m_Flags }` buffer, `UnlockFlags = RequireAll, RequireAny` | on any prefab `PrefabSystem.IsUnlockable` accepts |
| A requirement's progress | `Game.Prefabs.UnlockRequirementData { m_Progress }` plus one `*RequirementData` per kind | `ObjectBuiltRequirementSystem` gates on `m_Progress` as its running counter, zeroed on load and recounted; the others write it from counters kept elsewhere, and `PrefabUnlockedRequirementSystem` never writes it |
| An unlock | `Game.Prefabs.Unlock { m_Prefab }` on an `Event` entity | created by any system; `UnlockSystem` consumes it |

Map tiles ([map-tiles.md](map-tiles.md)):

| The game models | Component | Access shape |
| --- | --- | --- |
| A tile | `Game.Areas.MapTile` tag, `MapFeatureElement { m_Amount }` buffer indexed by `MapFeature`, `Geometry` | owned when `Game.Common.Native` is absent |
| Tile pricing | `Game.Prefabs.MapFeatureData { m_Cost }` buffer and `TilePurchaseCostFactor { m_Amount }` on the `MapTilePrefab` | through the tile's `PrefabRef` |
| Tile upkeep | `Game.Prefabs.EconomyParameterData.m_MapTileUpkeepCostMultiplier`, an `AnimationCurve1` over owned-tile count | `GetSingleton<T>`; the component belongs to `economy-and-companies` |
| Purchase status | `Game.Simulation.TilePurchaseErrorFlags` | `MapTilePurchaseSystem.status`, fresh only while the tile view is open |

Policies ([policies.md](policies.md)):

| The game models | Component | Access shape |
| --- | --- | --- |
| A policy | `Game.Prefabs.PolicyData { m_Visibility }` on a `PolicyPrefab`; `PolicySliderData { m_Range, m_Default, m_Step, m_Unit }` on a `PolicySliderPrefab` | `ecs_query` on `PolicyData` |
| Its effect | `CityModifierData`, `DistrictModifierData`, `BuildingModifierData`, `RouteModifierData` buffers of `{ m_Type, m_Mode, m_Range }`; `CityOptionData`, `DistrictOptionData`, `BuildingOptionData`, `RouteOptionData` masks | on the policy prefab; which it carries decides its scope |
| A district's or building's modifiers | `Game.Areas.DistrictModifier`, `Game.Buildings.BuildingModifier` buffers | `AreaUtils.ApplyModifier`, `BuildingUtils.ApplyModifier` |
| A building-side city effect | `Game.Prefabs.CityEffects` authoring, `CityEffectProvider` on the instance or upgrade | folded into the city buffer by efficiency |
| Defaults | `Game.Prefabs.DefaultPolicyData` buffer | on a building, district or route prefab; the city's sit on the `ServiceFeeParameterData` prefab |

Notifications ([notifications.md](notifications.md)):

| The game models | Component | Access shape |
| --- | --- | --- |
| An icon kind | `Game.Prefabs.NotificationIconData { m_Archetype }` and enableable `NotificationIconDisplayData { m_CategoryMask, … }` on a `NotificationIconPrefab` | found through the field naming it on a configuration singleton (`BuildingConfigurationData.m_CondemnedNotification` is the shape), never by name |
| An icon | `Game.Notifications.Icon { m_Location, m_Priority, m_ClusterLayer, m_Flags }` entity with `Owner` and `PrefabRef`; `IconElement { m_Icon }` buffer on the owner | written only through `IconCommandBuffer` |
| A tool error | `Game.Prefabs.ToolErrorData { m_Error, m_Flags }` beside `NotificationIconData` | belongs to `placement-definitions`; raised by the tools and never by the simulation |

The frontend half is a `kGroup` on each panel's UI system under `src/Game/Game.UI.InGame/` — `MilestoneUISystem` is the shape — and `binding-layer` owns the binding forms; lock state surfaces on any group binding a prefab, and `CityInfoUISystem` carries `happiness` beside `zoning-buildings-and-land-value`'s demand.

## Traps

**A field initializer on this topic's prefab classes is a Unity-serialized default the shipped asset overrides, not the value.**
`MapTilePrefab.m_PurchaseCostFactor`, `PolicySliderPrefab`'s slider fields, `NotificationIconPrefab`'s display fields and the requirement prefabs' thresholds all carry one, and nothing in the C# marks which survived — read the baked component live.
Source: `src/Game/Game.Prefabs/MapTilePrefab.cs`, `src/Game/Game.Prefabs/PolicySliderPrefab.cs`, `src/Game/Game.Prefabs/NotificationIconPrefab.cs`, `src/Game/Game.Prefabs/CitizenRequirementPrefab.cs`.

**Every prefab table in this topic is one install's set.**
Milestones, dev nodes, policies, statistics and icons are all asset data a content pack can extend; `ecs_query` on the `*Data` component with `PrefabSystem.GetPrefabName` as the label is the re-check.
Source: `src/Game/Game.Prefabs/MilestonePrefab.cs`, `src/Game/Game.Prefabs/PolicyPrefab.cs`.

**`Game.City.TaxRates` is a declared buffer no entity carries.**
`TaxSystem` keeps the rates in its own persistent `NativeArray<int>`, sized by a literal equal to `TaxRate.Count` rather than by the enum, and serializes them itself, so writing that buffer onto the city changes nothing; the rates belong to `economy-and-companies`.
Source: `src/Game/Game.City/TaxRates.cs`, `src/Game/Game.Simulation/TaxSystem.cs`, `src/Game/Game.City/TaxRate.cs`.

**Victory is console-only.**
`MilestoneUISystem` checks `PopulationVictoryConfigurationData.m_populationGoal` under `Platform.Consoles.IsPlatformSet`, and `PopulationVictoryConfigurationPrefab.LateInitialize` leaves the goal at -1 off consoles; `MilestoneData.m_IsVictory` reaches nothing else.
Source: `src/Game/Game.UI.InGame/MilestoneUISystem.cs`, `src/Game/Game.Prefabs/PopulationVictoryConfigurationPrefab.cs`.

## Mechanisms

| Mechanism | Vanilla owner | Listing |
| --- | --- | --- |
| Statistic collection modes, the write queue, the read, the trigger | `CityStatisticsSystem`, `StatisticTriggerSystem` | [statistics.md](statistics.md) |
| XP producers and the queue, the milestone step, dev-point accrual and purchase | `XPAccumulationSystem`, `XPBuiltSystem`, `NetXPSystem`, `XPSystem`, `MilestoneSystem`, `DevTreeSystem` | [progression.md](progression.md) |
| The unlock predicate, the fixpoint loop, the requirement family, adding a requirement | `UnlockSystem`, `PrefabSystem`, the `*RequirementSystem`s, `UIGroupPrefab` | [unlocking.md](unlocking.md) |
| Tile generation, permits, price, upkeep, status | `MapTileSystem`, `MapTilePurchaseSystem` | [map-tiles.md](map-tiles.md) |
| Applying a policy, modifier composition, default policies | `ModifiedSystem`, `CityModifierUpdateSystem`, `DefaultPoliciesSystem` | [policies.md](policies.md) |
| Raising, ordering, clearing and classifying icons | `IconCommandSystem`, `NotificationIconPrefabSystem`, `MarkerCreateSystem` | [notifications.md](notifications.md) |

## Bridges

- `prefabs-and-assets` — everything here is prefab data written in `Initialize` or `LateInitialize`; the `GetPrefabComponents` / `GetArchetypeComponents` split is the difference between `StatisticsData` and `CityStatistic`, and between `NotificationIconData` and `Icon`; what this topic adds is `PrefabSystem.IsUnlockable`, which decides at add time whether a prefab gets `Locked` at all ([unlocking.md](unlocking.md)).
- `ecs-in-this-game` — `Locked` and `NotificationIconDisplayData` are enableable.
- `performance-and-memory` — the reader/writer handle protocol: `XPSystem.GetQueue` + `AddQueueWriter`, `CityStatisticsSystem.GetStatisticsEventQueue` + `AddWriter`, and `IconCommandSystem.CreateCommandBuffer` + `AddCommandBufferWriter` for a per-frame buffer with no handle — reading one of these without registering back races the owning system's next write.
- `placement-definitions` — owns the tool-error prefab family in full, including the suppress-and-restore technique for turning one off.
- `mod-compatibility` — `usedMods` above is the only durable in-save trace of the mod set.
- `save-serialization` — `CityStatistic` widened `int` to `long` to `double` across `Version.statisticOverflowFix` and `Version.statisticPrecisionFix`, `CityModifier` reads only its relative channel below `Version.modifierRefactoring`, and `MapTileSystem` and `TaxSystem` serialize themselves rather than components.
- `localization` — `Progression.*` carries the requirement labels and the indexed `MILESTONE_NAME:N` / `MILESTONE_DESCRIPTION:N` families, `Policy.TITLE[…]` and `Notifications.DESCRIPTION[…]` are hashed by prefab name, as that reference tabulates, and `UnlockRequirementPrefab.m_LabelID` is a key, not a string.
- `binding-layer` — the groups above; `PrefabUtils.HasUnlockedPrefab<T>` is the per-frame "did anything unlock" test and `PrefabUISystem.BindPrefabRequirements` writes a prefab's requirements, walking the graph through `ProgressionUtils.CollectSubRequirements` so a mod need not.
- `frontend-and-injection` — the progression, statistics, notifications and policy panels are React components over those groups; changing what they show is that reference's.
- `city-services-and-coverage` — `DevTreeNodeData.m_Service` gates a `ServicePrefab`; whether a service is reachable is this topic's, what it offers is theirs, and each service failure's icon is a field on that service's configuration prefab.
- `economy-and-companies` — owns what a milestone pays into and the `CityModifierType` members it reads; a milestone writes `PlayerMoney` and `Creditworthiness` directly, and `Creditworthiness.m_Amount` is the running sum of every reached milestone's `m_LoanLimit`.
- `citizens-and-households` — owns `Population` and `Tourism`'s contents; `CountHouseholdDataSystem` writes the first and evaluates `CitizenRequirementData`.
- `zoning-buildings-and-land-value` — owns what `ZoneBuiltRequirementSystem` tallies: `SpawnableBuildingData`'s zone and level, and the level-ups `BuildingUpkeepSystem` pushes into its queue ([unlocking.md](unlocking.md)).
- `transportation-and-vehicles` — owns the `TransportUsageData` counters a `TransportRequirementData` reads ([unlocking.md](unlocking.md)).
- `utilities-and-flow-networks` — reads `Locked` in the simulation: `DispatchElectricitySystem` and `DispatchWaterSystem` skip the cooldown step while the asset-menu prefab their parameter component names is still locked ([unlocking.md](unlocking.md)).
- `environment-and-pollution` — reads `CityModifierType.IndustrialAirPollution`, `.IndustrialGroundPollution`, `.DisasterWarningTime` and `.DisasterDamageRate`, and `WeatherHazardSystem` skips an event prefab whose `Locked` is enabled, so a disaster's availability is a progression state ([policies.md](policies.md), [unlocking.md](unlocking.md)).
- `simulation-time-and-units` — owns what `CityStatisticsSystem`'s `8192`-frame sample interval is worth in a day, which is what makes a `Daily` statistic a day.
- `diagnostics` — the `"Unlocking"` logger's `Prefab unlocked: {0}` / `Prefab locked: {0}` lines are the cheapest way to watch a cascade; `MilestoneSystem` warns `did not find data for milestone N` when an index has no prefab.
- `debug-menu` — `UnlockAllSystem`, `MilestoneSystem.UnlockAllMilestones`, `MapTilePurchaseSystem.UnlockMapTiles` and `DebugSystem`'s XP grants are reached from it, and `CityConfigurationSystem.unlockAll` / `unlockMapTiles` is what to read before believing a save's milestone level or tile count.

(VOLATILE: every component, field, property, enum, system, method, constant, quoted log string, binding name and `Source:` path this file names — their declarations under `src/Game/` in `Game.City`, `Game.Simulation`, `Game.Prefabs`, `Game.Prefabs.Modes`, `Game.Policies`, `Game.Areas`, `Game.Buildings`, `Game.Common`, `Game.Notifications`, `Game.Triggers`, `Game.Events`, `Game.Debug`, `Game.UI.InGame` and the root `Game` namespace, at the files the rows and traps cite, plus `AnimationCurve1` and `Platform` in the `Colossal` assemblies; and the locale key families it names — the install's `Locale.cok`, where the C# declares only `Policy.TITLE[…]` in `Game.UI.LocaleIds`.)
