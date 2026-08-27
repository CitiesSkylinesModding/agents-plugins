# The production graph

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

**The graph lives in `IndustrialProcessData` on company prefabs, and nowhere else.**
There is no table, no chart asset and no chain system: each recipe is one component on one company prefab, and the "graph" is the join over all of them by resource.
A mod reaches a recipe by query, never by prefab name: an `EntityQuery` on `Game.Prefabs.IndustrialProcessData` filtered on its output resource.
The game's own join is narrower: every `ZonePrefab` declares a `ProcessEstimate` buffer whose element at a dense resource index carries `m_ProcessEntity`, but `ZonePrefabInitializeSystem` fills it only on non-office industrial zone prefabs, from every recipe on a prefab carrying `IndustrialCompanyData` — office and extractor recipes included; no retail, converter or warehouse recipe resolves through it, and a resource with several producer prefabs keeps whichever was written last (`src/Game/Game.Zones/ProcessEstimate.cs`, `src/Game/Game.Prefabs/ZonePrefabInitializeSystem.cs`).
The edges below were enumerated from one install's full prefab set at 1.6.0f1; the query above is the check, and content packs extend the set.
Recipe *amounts* are asset data — the `ResourceStack.m_Amount` fields on each `IndustrialProcessData` — and a game mode multiplies them: `ProcessingCompanyGlobalMode` scales `m_Input1`/`m_Input2`/`m_Output` amounts over every recipe and never touches `m_Resource` (`src/Game/Game.Prefabs.Modes/ProcessingCompanyGlobalMode.cs`), so the edges are mode-invariant and the amounts are not.

## Materials — extractor recipes, no input

The ten resources with `ResourceData.m_IsMaterial == true`: Wood, Grain, Vegetables, Livestock, Cotton, Oil, Ore, Coal, Stone, Fish — the same ten `EconomyUtils.IsExtractorResource` declares as a C# mask (`src/Game/Game.Economy/EconomyUtils.cs`).
Extractor company prefab names are asset data with no derivable convention — `Industrial_OreExtractor` (output Ore) is the worked example, and the rest are reached by the query above, never by name.

## Material goods — industrial recipes

| Inputs | Output |
| --- | --- |
| Ore | Metals |
| Metals + Coal | Steel |
| Metals + Steel | Machinery |
| Stone | Minerals |
| Stone | Concrete |
| Oil | Petrochemicals |
| Grain | Petrochemicals |
| Minerals + Oil | Chemicals |
| Petrochemicals + Chemicals | Plastics |
| Chemicals | Pharmaceuticals |
| Minerals + Plastics | Electronics |
| Metals + Plastics | Vehicles |
| Grain | Beverages |
| Vegetables | Beverages |
| Grain | ConvenienceFood |
| Livestock | ConvenienceFood |
| Fish | ConvenienceFood |
| Vegetables + Livestock | Food |
| Fish | Food |
| Cotton | Textiles |
| Livestock | Textiles |
| Petrochemicals | Textiles |
| Wood | Timber |
| Timber | Paper |
| Timber | Furniture |

One prefab carries each row, named `Industrial_<Product>Factory` with mill, plant, refinery and smelter variants; `Industrial_SteelPlant` (Metals + Coal → Steel) is the worked example.
A resource with several rows has one producer prefab per input path.

## Immaterial goods — office recipes

The four resources `EconomyUtils.IsOfficeResource` masks (`src/Game/Game.Economy/EconomyUtils.cs`), all with `ResourceData.m_Weight == 0`:

| Inputs | Output |
| --- | --- |
| Electronics | Software |
| Electronics + Software | Telecom |
| Software | Financial |
| Software | Media |

The prefabs are named `Office_*`; `Office_SoftwareCompany` (Electronics → Software) is the worked example.

## Leisure — commercial converter recipes

The four outputs with `ResourceData.m_IsProduceable == false` and `m_IsLeisure == true`:

| Inputs | Output |
| --- | --- |
| Food | Lodging |
| Food | Meals |
| Beverages | Entertainment |
| *(none)* | Recreation |

The Recreation recipe's input stack names `NoResource`, so Recreation is produced from nothing; its prefab, the family's worked example, is `Commercial_RecreactionStore` — the spelling is the game's own.

## Retail and warehouses — rules, not rosters

**Retail is the pass-through recipe `X → X`, one commercial prefab per material good households buy, and the shape is load-bearing.**
`ProcessingCompanySystem` short-circuits on exactly `input1 == output && input2 == NoResource && equal amounts`, so a retail company never runs the production block, never accrues untaxed income there, and never gains a `ResourceExporter` — its whole simulation is `ServiceCompanySystem` plus `ResourceBuyerSystem` ([production-and-profit.md](production-and-profit.md), [trade-and-restocking.md](trade-and-restocking.md)).
Whether a given good has a retail prefab is checked from its resource prefab: household consumption (`ResourceData.m_BaseConsumption`, `m_CarConsumption`) and the commercial bit of `TaxableResourceData.m_TaxAreas`.
The prefabs are named `Commercial_<Good>Store`, with the odd domain name (a gas station for Petrochemicals).

**A warehouse exists per material and per material good, and none for the four office resources.**
`Industrial_Warehouse<Resource>` storage prefabs carry the process `NoResource → <Resource>` with `StorageCompanyData.m_StoredResources` set to that one resource (UNVERIFIED: the authored `NoResource → <Resource>` process on every warehouse — two were read live and the rest are name-inferred; one `ecs_query` over the storage prefabs plus batched `eval` reads settles it); a weightless resource stores against `IndustrialAISystem.kMaxVirtualResourceStorage` instead of any `StorageLimitData`, and `IndustrialSpawnSystem`'s warehouse branch finds no `StorageCompanyData` prefab to instantiate for it.
Source: `src/Game/Game.Prefabs/StorageCompany.cs`, `src/Game/Game.Simulation/IndustrialSpawnSystem.cs`, `src/Game/Game.Simulation/ProcessingCompanySystem.cs`.

## Company spawning

Sources: `src/Game/Game.Simulation/CommercialSpawnSystem.cs`, `src/Game/Game.Simulation/IndustrialSpawnSystem.cs`.

```
CommercialSpawnSystem (runs only on one frame in every 128, and only while the commercial company demand is positive), per resource:
  skip unless demand > 0 and the last spawn for it is older than DemandParameterData.m_FrameIntervalForSpawning.y
  skip if any propertyless commercial company already produces it
  else instantiate a uniformly random CommercialCompanyData prefab whose output masks the resource
IndustrialSpawnSystem (same gate on another frame, against the industrial + storage + office demand sum), per resource:
  produceable non-material: roll NextInt(round(5000 / min(5, max(1, log10(1 + population))))) < demand, skip if a propertyless industrial company already produces it, else spawn
  produceable material: spawn an extractor when no propertyless extractor company produces it -- no demand test -- and break out of the resource loop, ending the pass: no later resource's roll or warehouse check runs
  independently, any tradable resource with warehouse demand > 0: spawn a warehouse
```

Both spawn gates also test an empty-signature-building count that nothing in `src/` ever assigns, so that bypass cannot fire — a reader tracing a signature building's company should look at the demand systems instead.

In-city demand here is not consumption: `CountCompanyDataSystem` publishes, per resource, the input requirement implied by each *housed* producer's current production rate — a propertyless producer contributes nothing to it — beside production, sales capacity and the propertyless counts the demand systems read (`src/Game/Game.Simulation/CountCompanyDataSystem.cs`); the zone-level arithmetic on top is `zoning-buildings-and-land-value`.

## Traps

**`EconomyUtils.IsProducedFrom` is a second, C#-resident statement of the graph, it has drifted from the recipes in both directions, and the wrong one decides where warehouses and factories want to be built.**
Decoding its masks against the recipes finds edges it asserts that no recipe has — Chemicals → Paper, Beverages → Lodging and Beverages → Meals among them — and recipe edges it omits, Metals → Steel and Timber → Paper among those, so re-derive both sides rather than trusting either; its two callers are both in `ZoneEvaluationUtils` — `GetStorageScore` and `GetTransportScore` — so storage and industrial zone suitability read the drifted adjacency, while every production, purchase and tax system reads the recipes.
Source: `src/Game/Game.Economy/EconomyUtils.cs`, `src/Game/Game.Simulation/ZoneEvaluationUtils.cs`.

**`ProcessEstimate.m_BaseProfitabilityPerCell` is never written.**
The `EconomyUtils.BuildPseudoTradeCost` call that would produce it has its return value discarded at all three call sites, so nothing in `src/` consumes that function's result and the field is always zero — the function is live, only its results are dead.
Source: `src/Game/Game.Prefabs/ZonePrefabInitializeSystem.cs`, `src/Game/Game.Debug/EconomyDebugSystem.cs`.

(VOLATILE: every edge, name convention, component, field, system and `Source:` path this file names — the recipes are the `IndustrialProcessData` components over the install's company prefabs, and the declarations live in `Game.Prefabs`, `Game.Prefabs.Modes`, `Game.Economy`, `Game.Companies`, `Game.Zones`, `Game.Simulation` and `Game.Debug` under `src/Game/`, at the files the sections cite.)
