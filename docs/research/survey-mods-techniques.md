# Capability map: CS2 code-mod techniques across 12 open-source mods

> **Seed survey.** Produced 2026-07-31 during the interview that became the `cs2-modding` spec, before the discovery pipeline existed.
> Read the open-source mod corpus only, at the commits each repository carried on 2026-07-31.
> Kept as it was written, citations intact; its recommendations are that pass's opinion, not decisions.
> Where a later pass overturned a claim here, the correction rides beside it as a dated `[ed. …]` note rather than as an edit to the sentence, so the survey still reads as the snapshot it is.

**Corpus root:** `C:\Users\Morgan\Documents\Projets\cs2-third-party-mods\`

**Scale/health snapshot** (C# file count / last commit):

| Mod | .cs | .ts(x) | Last commit | Character |
| --- | --- | --- | --- | --- |
| CS2-WriteEverywhere | 236 | 41 | 2026-07-27 | Most technically extreme (rendering, fonts, custom RPC, own framework) |
| CS2-MoveIt | 141 | 35 | 2026-05-03 | Deepest tool/undo architecture |
| Traffic | 128 | 32 | 2026-01-27 | Deepest ECS/Burst/serialization; best all-round reference |
| CS2-Platter | 103 | 37 | 2026-06-22 | Best modern Harmony + prefab + tests + CI |
| RoadBuilder-CSII | 101 | 54 | 2026-05-15 | Best prefab synthesis; almost zero Burst |
| FindIt-CSII | 83 | 32 | 2026-06-11 | Best prefab indexing / cross-mod API |
| Cities2-TLE | 62 | 102 | 2026-04-06 | System-replacement instead of Harmony; JSON-string bindings |
| Anarchy | 53 | 27 | 2026-05-27 | Best "intercept tool definitions" reference |
| Tree_Controller | 37 | 19 | 2026-07-19 | Mixed quality; legacy DOM hacks |
| BetterBulldozer | 30 | 15 | 2026-04-29 | Raycast-filtering reference; legacy DOM hacks |
| LineTool-CS2 | 23 | 14 | 2026-07-05 | Cleanest single-tool reference |
| PlopTheGrowables | 12 | 0 | 2026-05-18 | Cleanest "replace a vanilla system" reference |

---

## 1. Entry point and lifecycle

### 1.1 The baseline `IMod` shape

Ten of twelve implement `Game.Modding.IMod` directly; `OnLoad(UpdateSystem)` is _the_ bootstrap and does, in an order that is remarkably stable across authors:

1. `Instance = this`, `LogManager.GetLogger(...).SetShowsErrorsInUI(false)`
2. construct `ModSetting` subclass → `RegisterKeyBindings()` → `RegisterInOptionsUI()`
3. `GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(settings))` + loop over `GetSupportedLocales()`
4. `AssetDatabase.global.LoadSettings(name, settings, new Settings(this))` — **note the third "defaults" argument**; TLE omits it (`Cities2-TrafficLightsEnhancement/TrafficLightsEnhancement/Mod.cs:91`), which is the odd one out
5. `new Harmony(id).PatchAll()`
6. a block of `updateSystem.UpdateAt/UpdateBefore/UpdateAfter<T>(SystemUpdatePhase.X)`

**Best exemplar (canonical, well-commented):** `Anarchy/Anarchy/AnarchyMod.cs:80-156`. It shows all six steps plus `#if DEBUG` locale export and `DUMP_VANILLA_LOCALIZATION`.

**Cleanest minimal exemplar:** `PlopTheGrowables/Code/Mod.cs:47-83` — 35 lines, does everything, no Harmony at all.

### 1.2 System ordering vocabulary actually used

Across the corpus the observed `SystemUpdatePhase` values are: `PrefabUpdate`, `Deserialize`, `Modification1..5`, `Modification4B`, `ModificationEnd`, `Raycast`, `PreTool`, `ToolUpdate`, `ApplyTool`, `ClearTool`, `PostTool`, `UIUpdate`, `UITooltip`, `Rendering`, `PreCulling`, `GameSimulation`, `MainLoop`, `Cleanup`, `EndFrame`, `DebugGizmos`.

Three ordering idioms recur and a skill must teach them explicitly:

- **Anchor to a vanilla system, not a phase:** `updateSystem.UpdateBefore<TempNetworkSystem, CompositionSelectSystem>(SystemUpdatePhase.Modification3)` (`Anarchy/Anarchy/AnarchyMod.cs:149`), `UpdateAfter<ApplyMoveItSystem, ApplyNetSystem>(SystemUpdatePhase.ApplyTool)` (`CS2-MoveIt/Code/MoveIt/Mod.cs:91`), `UpdateAfter<ToolOverlaySystem, AreaRenderSystem>(SystemUpdatePhase.Rendering)` (`Traffic/Code/Mod.cs:73`).
- **Disable a vanilla system and slot a fork in front of it:** `Traffic/Code/Mod.cs:76-77` disables `Game.Net.LaneSystem` and registers `TrafficLaneSystem` before it; `PlopTheGrowables/Code/Mod.cs:74` disables `Game.Simulation.ZoneCheckSystem` and registers `SelectiveZoneCheckSystem` at `ModificationEnd`.
- **`PreDeserialize<T>` wrapper systems:** `updateSystem.UpdateBefore<PreDeserialize<ModUISystem>>(SystemUpdatePhase.Deserialize)` (`Traffic/Code/Mod.cs:68,101`). The wrapped system implements `Game.Serialization.IPreDeserialize`; see `Traffic/Code/Systems/ModDefaultsSystem.cs:11,26` and `CS2-Platter/Platter/Systems/Parcels/P_ParcelSearchSystem.cs:30,52` (clears its quadtree before a load).

### 1.3 Deferred work: `MainThreadDispatcher.RegisterUpdater`

`Colossal.Core.MainThreadDispatcher.RegisterUpdater(Action)` is the corpus-wide answer to "I need to do X _after_ the mod manager/prefab DB has settled." Used for mod-compat detection (`Traffic/Code/Mod.cs:112-115`), for forcing a UI module asset to load (`CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:104-109`), for cross-mod API discovery (`FindIt-CSII/FindIt/Mod.cs:73-74`), and for lazily reflecting a private field once the system exists (`RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderSystem.cs:63`).

### 1.4 Variations worth teaching

- **`OnDispose` is under-implemented.** Only Anarchy, BetterBulldozer, Tree_Controller, LineTool, Platter unpatch Harmony. Traffic/MoveIt/FindIt/RoadBuilder only unregister settings. `CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:94` is literally empty.
- **Framework-mediated entry point:** WriteEverywhere extends an external `Belzont.Interfaces.BasicIMod` (shipped as a compiled dependency, imported via `$(SolutionDir)\_Build\belzont_public.targets`), overriding `DoOnLoad()` / `DoOnCreateWorld(UpdateSystem)` / `CreateSettingsFile()` instead of `OnLoad` (`CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:18-127`). Same for MoveIt's `QCommonLib` shared projitems.
- **Documented dependency graph as a comment block** — `CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:24-66` draws the whole system DAG in ASCII with an instruction to keep it updated. Best practice worth stealing.
- **Compat detection & user-facing warnings:** `Traffic/Code/Mod.cs:179-217` reads the private static `GameManager.s_ModdingRuntime` to detect BepInEx, then pushes a `Game.PSI.NotificationSystem.Push(...)` with an `onClicked` that opens a `Game.UI.MessageDialog` via `GameManager.instance.userInterface.appBindings.ShowMessageDialog`. Best exemplar of "tell the user something is wrong."

---

## 2. Harmony patching

Only **6 of 12** use Harmony at all: Anarchy, BetterBulldozer, Platter, LineTool, Tree_Controller, and (via a third-party `Redirector` wrapper) WriteEverywhere. Traffic, TLE, MoveIt, FindIt, RoadBuilder, PlopTheGrowables ship **zero** Harmony patches. **That is the single most important finding for the skill: in CS2, Harmony is a last resort, not the default.** The default is system insertion + ordering.

### 2.1 What actually gets patched

Every patch in the corpus falls into four buckets:

**(a) Raycast configuration — the most common patch by far.**
`ToolBaseSystem.InitializeRaycast` / `GetRaycastResult` overloads, to widen or narrow what the vanilla tools can hit.

- `Anarchy/Anarchy/Patches/NetToolSystem_InitializeRaycast.cs:31-49` — postfix adds `Layer.Pathway | TrainTrack | PublicTransportRoad | TramTrack | SubwayTrack` to `m_ToolRaycastSystem.netLayerMask` when `actualMode == NetToolSystem.Mode.Replace`. Uses `Traverse.Create(__instance).Field<ToolRaycastSystem>("m_ToolRaycastSystem")`.
- `BetterBulldozer/BetterBulldozer/Patches/BulldozeToolSystemInitializeRaycastPatch.cs:20-70` — the fullest catalogue of `typeMask` / `netLayerMask` / `raycastFlags` / `utilityTypeMask` / `collisionMask` / `areaTypeMask` combinations in the corpus. Best single reference for CS2 raycast masks.
- `BetterBulldozer/.../ToolBaseSystemGetRaycastResultPatch.cs:20-132` — prefix on the `(out Entity, out RaycastHit)` overload, disambiguated with `new Type[]{...}, new ArgumentType[]{ ArgumentType.Out, ArgumentType.Out }`. Returns `false` to suppress the hit. **This is the reference for patching overloaded methods with `out`/`ref` params.**
- `CS2-Platter/Platter/Patches/MarkerPatches.cs:45-162` — the most _disciplined_ version: it records in a `[ThreadStatic] bool` whether _it_ added `RaycastFlags.Markers`, and only filters results in that case, so it composes with other mods and future patch versions. Explanatory comment at lines 25-29.

**(b) Overriding vanilla tool behaviour via prefab/property swaps.**
`CS2-Platter/Platter/Patches/ToolSystemPatch.cs` — prefixes on `ObjectToolSystem.TrySetPrefab(PrefabBase)` that mutate the `ref PrefabBase prefab` argument then return `true` (lines 202-247), a postfix on `ObjectToolSystem.GetObjectPrefab`, a prefix on `ObjectToolSystem.GetAllowRotation` returning `false` to skip the original (lines 71-95), and a postfix on `ToolBaseSystem.GetActualSnap(Snap,Snap,Snap)` OR-ing in `Snap.ContourLines` (lines 31-50).

**(c) Replacing a job schedule inside a vanilla system — the most advanced idiom present.**
`CS2-Platter/Platter/Patches/ToolSystemPatch.cs:100-178`: prefix on `ObjectToolSystem.SnapControlPoint(JobHandle) : JobHandle`. It pulls ~8 private system references and `m_ControlPoints` out of `__instance` via cached reflection delegates, schedules its own `AdhocParcelSnapJob`, registers readers on `zoneSearchSystem` / `netSearchSystem` / `waterSystem`, `.Complete()`s, assigns `__result`, and returns `false`. This is the pattern for "swap out a Burst job the game schedules" without ever patching Burst-compiled code itself.

**(d) Killing or neutering vanilla systems.**
`Anarchy/Anarchy/Patches/UniqueAssetTrackingSystemOnCreatePatch.cs:14-25` — postfix on `OnCreate` that sets `system.Enabled = false`. `Anarchy/.../ToolUISystemGetElevationRangePatch.cs:24-35` — prefix that writes `ref Bounds1 __result` and returns `false`.

**(e) Render pipeline.** `Tree_Controller/Tree_Controller/Patches/WindGlobalPropertiesPatch.cs:15-52` — prefix on `Game.Rendering.WindControl.SetGlobalProperties(CommandBuffer, WindVolumeComponent)` calling `.Override()` on the HDRP volume parameters.

### 2.2 Idioms observed, and gaps

- **Prefix returning `bool`, postfix with `ref __result`, `__instance`, `___privateField`** — all present. `LineTool-CS2/Code/Patches/ToolbarUISystemPatches.cs:25` uses the `___m_AgeMaskBinding` triple-underscore private-field injection form.
- **No transpilers anywhere.** Zero `CodeInstruction` / `IEnumerable<CodeInstruction>` in 180k lines.
- **No `[HarmonyReversePatch]`, no `Finalizer`, no generic-method patching.**
- **No patching of Burst-compiled jobs.** The corpus consistently avoids this and instead (i) patches the _scheduler_ (Platter), (ii) mutates the _inputs_ the job reads — the `ObjectDefinition` interception pattern of §3.4, or (iii) disables the system and forks it.
- **Explicit ID + selective `PatchAll(assembly)`:** `CS2-Platter/Platter/PlatterMod.cs:340-346` calls `m_Harmony.PatchAll(typeof(PlatterMod).Assembly)` then logs `GetPatchedMethods()` — a good verification habit.
- **Patcher lifecycle wrapper:** `LineTool-CS2/Code/Patches/Patcher.cs:26-96` guards against a stale singleton by unpatching a previous instance before patching.

### 2.3 The Harmony-free alternatives (teach these first)

- **Cached reflection accessors instead of patches:** `CS2-Platter/Platter/Patches/Accessors/ObjectToolSystemFieldAccessor.cs:23-50` builds `Func<ObjectToolSystem,T>` closures once in a static ctor via `AccessTools.Field`; `ObjectToolSystemAccessor.cs:18-34` builds a `Delegate.CreateDelegate` for a protected property setter. Explicit rationale comment: "to avoid repeated reflection lookups in hot paths."
- **Rewriting a vanilla system's `EntityQuery` from outside:** `Cities2-TrafficLightsEnhancement/TrafficLightsEnhancement/Utils/EntityQueryUtils.cs:21-74` reads the private `EntityQuery` field, decomposes it with `GetEntityQueryDescs()`, rebuilds it through `EntityQueryBuilder` with extra `None` constraints, and writes it back. Called at `Mod.cs:70-73` to make `TrafficLightInitializationSystem` and `TrafficLightSystem` skip nodes carrying `CustomTrafficLights`. **This is the most elegant "carve an exception into a vanilla system" trick in the whole corpus** and needs no patching.
- **A third-party patching abstraction:** WriteEverywhere's `Redirector` base (`AddRedirect(MethodInfo original, MethodInfo prefix, MethodInfo postfix)`) — `CS2-WriteEverywhere/BelzontWE/Overrides/PrefabSystemOverrides.cs:18-24` redirects `PrefabSystem.UpdatePrefabs` and `AssetImportPipeline.GetTextureReferenceCount`; note `AccessTools.FieldRef<PrefabSystem, List<PrefabBase>>("m_Prefabs")` at line 14-17 for zero-cost repeated private-field access.

---

## 3. ECS work

### 3.1 Custom components

Counts of files declaring `IComponentData`: WriteEverywhere 23, Traffic 20, Platter 13, Anarchy 8, MoveIt/RoadBuilder 7, TLE/Tree_Controller 6, BetterBulldozer/PlopTheGrowables 3, FindIt/LineTool 0.

Kinds present:

- **Empty tags that persist into savegames:** `struct PloppedBuilding : IComponentData, IQueryTypeParameter, IEmptySerializable` (`PlopTheGrowables/Code/Systems/PloppedBuilding.cs:15`; also `LevelLocked.cs:15`, `SpawnedBuilding.cs:15`). `IEmptySerializable` is the cheapest way to have a mod flag survive save/load — highlight this.
- **Versioned serializable data components:** `Traffic/Code/Components/LaneConnections/ModifiedLaneConnections.cs:10-72` — `IBufferElementData, IEquatable<>, ISerializable` with `[InternalBufferCapacity(0)]`, a version int written first and a branch in `Deserialize`. `CS2-Platter/Platter/Components/Parcel.cs:33-78` is the simpler unversioned form.
- **Cleanup components holding unmanaged/managed resources:** `CS2-WriteEverywhere/BelzontWE/Components/WETextData/WETextDataMesh.cs:13` and `WETextDataMaterial.cs:14` are `IComponentData, IDisposable, ICleanupComponentData`, with a dedicated `WETemplateDisposalSystem` registered in `SystemUpdatePhase.Cleanup` (`WriteEverywhereCS2Mod.cs:81`). This is the only correct pattern in the corpus for ECS components that own Unity `Material`/`Mesh` handles.
- **Buffer components for graph data:** `Traffic/Code/Components/LaneConnections/GeneratedConnection.cs`, `CS2-Platter/Platter/Components/ConnectedParcel.cs`, `LinkedParcel.cs`.

### 3.2 Queries

Both APIs are in use and both should be taught:

- Modern `SystemAPI.QueryBuilder().WithAll<>().WithAllRW<>().WithAny<>().WithNone<>().Build()` — `Anarchy/Anarchy/Systems/ObjectElevation/ElevateObjectDefinitionSystem.cs:51-56`, `CS2-Platter/Platter/Systems/Parcels/P_ParcelInitializeSystem.cs:72-86`, `RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderSystem.cs:261,325`.
- Legacy `GetEntityQuery(new EntityQueryDesc[]{...})` — `Traffic/Code/Systems/LaneConnections/ApplyLaneConnectionsSystem.cs:30-40`, `Anarchy/.../DisableToolErrorsSystem.cs:50-60`.
- `RequireForUpdate` / `RequireAnyForUpdate(q1,q2)` (`ApplyLaneConnectionsSystem.cs:41`) as the throttling mechanism.
- The vanilla change-detection tags that mod queries key off: `Created`, `Updated`, `Deleted`, `Temp`, `Overridden`, `BatchesUpdated`, `TransformUpdated`, `Initialized`, `Applied`.

### 3.3 Barriers / EntityCommandBuffer

The corpus uses the game's named barriers rather than raw ECBs: `ModificationBarrier1..5`, `ModificationEndBarrier`, `ToolOutputBarrier`, `EndFrameBarrier`.

- `_toolOutputBarrier.CreateCommandBuffer()` + `_toolOutputBarrier.AddJobHandleForProducer(jobHandle)` — `Traffic/Code/Systems/LaneConnections/ApplyLaneConnectionsSystem.cs:80-96`. **Best exemplar of the full barrier contract.**
- `ModificationBarrier5` in Anarchy's `DisableToolErrorsSystem.cs:71`, `ModificationBarrier2` in `CopyAnarchyComponentsSystem`, `ModificationBarrier1` in `RoadBuilderSystem`.
- MoveIt is the exception: it threads a bare `EntityCommandBuffer` through its action queue and plays it back manually (`CS2-MoveIt/Code/MoveIt/Managers/QueueManager.cs:28-32`).

### 3.4 The single most important ECS pattern: intercepting tool _definitions_

CS2 tools don't mutate the world directly; they emit temporary entities carrying `CreationDefinition` + `ObjectDefinition` / `NetCourse` / `OwnerDefinition`, and later systems consume them. Mods therefore insert a system in `Modification1`–`Modification3` that queries `WithAllRW<ObjectDefinition>().WithAll<CreationDefinition, Updated>().WithNone<Deleted, Overridden>()` and rewrites the definition before the game consumes it.

- **Best exemplar:** `Anarchy/Anarchy/Systems/ObjectElevation/ElevateObjectDefinitionSystem.cs:51-120` (registered at `AnarchyMod.cs:146` with `UpdateBefore<...>(Modification1)`) — adjusts `ObjectDefinition.m_Elevation` / `m_Position.y`, with a `StackData` special case.
- Also: `Tree_Controller/Tree_Controller/Systems/TreeObjectDefinitionSystem.cs` (26 hits), `Anarchy/Anarchy/Systems/NetworkAnarchy/NetworkDefinitionSystem.cs` (52 hits — the largest definition-rewriting system in the corpus), `CS2-Platter/Platter/Systems/Tool/P_GenerateZonesSystem.cs`.
- **Reimplementing the definition producer:** `LineTool-CS2/Code/Systems/CreateDefinitions.cs` is a Burst-compiled copy of the game's own definition-creation job, with a header comment admitting it (`lines 7-10: "Substantial portions derived from game code"`) and ~60 `ComponentLookup`/`BufferLookup` fields wired in `LineToolSystem.cs:1035-1100`. This is the reference for "my tool must produce vanilla-quality previews."

### 3.5 Burst jobs

`[BurstCompile]` occurrences: Traffic 64, Platter 46, BetterBulldozer 20, Tree_Controller 18, WriteEverywhere 12, MoveIt 8, TLE 9, Anarchy 7, PlopTheGrowables 3, FindIt 1, LineTool 1, **RoadBuilder 0**.

Job interfaces: `IJobChunk` 123 uses, `IJob` 30, `IJobParallelFor` 17, `IJobFor` 9. `IJobEntity` is essentially absent — source-generated `IJobEntity` doesn't play well with the modding toolchain, so everyone hand-writes `IJobChunk` with `ArchetypeChunk` + `ComponentTypeHandle<T>`.

- **Best `IJobChunk` exemplar with `v128` chunk-enabled-mask handling:** `Anarchy/Anarchy/Systems/OverridePrevention/PreventCullingSystem.cs:164-190`.
- **Conditional Burst via `DefineConstants`:** Traffic gates every `[BurstCompile]` behind `#if WITH_BURST`, set only in Release (`Traffic/Code/Traffic.csproj:450`). MoveIt uses `USE_BURST`; WriteEverywhere uses a `<Bursted>` property. Teach this: Burst makes stepping/debugging impossible, so gate it.
- **Partial-class-per-job file layout** — Traffic splits every job into `SystemName.JobName.cs` with `<DependentUpon>` metadata in the csproj (`Traffic/Code/Traffic.csproj:594-727`, ~40 entries). Platter and MoveIt copy the convention. This is the corpus's answer to "systems get enormous."

### 3.6 Custom spatial indices

`NativeQuadTree<Entity, QuadTreeBoundsXZ>` (from `Colossal.Collections`) with the reader/writer dependency protocol is used to build mod-owned search trees:

- `Traffic/Code/Systems/LaneConnections/SearchSystem.cs` — two trees (connectors, lane handles), each with `GetSearchTree(bool readOnly, out JobHandle)` / `AddSearchTreeReader(JobHandle)`.
- `CS2-Platter/Platter/Systems/Parcels/P_ParcelSearchSystem.cs:42-80` — static + moving trees, with `IPreDeserialize` clearing on load.
- `CS2-MoveIt/Code/MoveIt/Searcher/SearcherJob.cs` + `SearcherIterator.cs` implement a custom `INativeQuadTreeIterator` for marquee selection.

### 3.7 Archetype manipulation

Done through prefab declaration rather than raw `EntityManager.CreateArchetype`: overriding `PrefabBase.GetArchetypeComponents(HashSet<ComponentType>)` adds components to every _instance_ of the prefab, and `GetPrefabComponents` adds them to the _prefab entity_. See `CS2-Platter/Platter/Prefabs/ParcelPrefab.cs:37-52` and `Traffic/Code/Helpers/FakePrefab.cs:14-24`.

---

## 4. Prefab and asset manipulation

### 4.1 The three modes of prefab work

**(a) Synthesise a brand-new prefab type.**
`ScriptableObject.CreateInstance<TPrefab>()` → set fields → `prefabBase.AddComponentFrom(component)` / `AddComponent<T>()` → `prefabSystem.AddPrefab(prefabBase)`.

- `CS2-Platter/Platter/Systems/P_PrefabsCreateSystem.cs:220-345` — creates 100+ sized `ParcelPrefab`/`ParcelPlaceholderPrefab` pairs, a `UIAssetCategoryPrefab` for the toolbar tab, a cloned `ZonePrefab`, and a single `ParcelSelectorPrefab`. Registered `UpdateAfter<P_PrefabsCreateSystem, ObjectInitializeSystem>(SystemUpdatePhase.PrefabUpdate)` (`PlatterMod.cs:181`).
- **Custom `PrefabBase` subclasses:** `ParcelPrefab`/`ParcelPlaceholderPrefab` (Platter), `RoadBuilderPrefab : RoadPrefab, INetworkBuilderPrefab`, `LaneGroupPrefab : PrefabBase`, `FenceBuilderPrefab`, `PathBuilderPrefab`, `TrackBuilderPrefab` (RoadBuilder), `FakePrefab : PrefabBase` (Traffic).
- **Gotcha to teach:** Platter declares `ParcelPrefab` inside `namespace Game.Prefabs` (`CS2-Platter/Platter/Prefabs/ParcelPrefab.cs:6`) — prefab identity is `(TypeName, name)` via `PrefabID`, and this placement is a deliberate identity/serialization choice.
- **Custom `ComponentBase` subclasses** (prefab-authoring components, not ECS components): `RoadBuilder-CSII/RoadBuilder/Domain/Components/Prefabs/RoadBuilderLaneInfo.cs:19` and 5 siblings; `FindIt-CSII/FindIt/Domain/FindItGenerated.cs:9`. These override `GetPrefabComponents`/`GetArchetypeComponents` to inject ECS component types.

**(b) Clone an existing prefab.**

- **Best-commented exemplar:** `Anarchy/Anarchy/ExtendedRoadUpgrades/UpgradesManager.cs:143-205`. It grabs the vanilla "Grass" `FencePrefab`, copies `grassUpgradePrefab.components` one by one (reassigning `componentBase.prefab = fencePrefab`), then explicitly `Remove<UIObject>()` and rebuilds a _fresh_ `UIObject` — with a 5-line comment explaining exactly why ("I need to be sure that we're not keeping any unintended reference to the source object"). Phase 2 happens in `GameManager.instance.onGameLoadingComplete` where `PlaceableNetData.m_SetUpgradeFlags`/`m_UnsetUpgradeFlags` (`CompositionFlags`) are written onto the prefab entity.
- `PrefabBase.Clone(newName)` is the shortcut — `CS2-Platter/Platter/Systems/P_PrefabsCreateSystem.cs:296` (`(ZonePrefab)originalUnzonedPrefab.Clone("PlatterUnzoned")`).
- **Bulk derivation from vanilla assets:** `FindIt-CSII/FindIt/Systems/AutoVehiclePropGeneratorSystem.cs:92-180` turns vehicle prefabs into static props: reuses `original.m_Meshes`, copies `UIObject`/`ThemeObject`/`AssetPackItem`/`ContentPrerequisite` via `AddComponentFrom`, adds `EditorAssetCategoryOverride`, adds `ObsoleteIdentifiers` for rename migration, synthesises locale entries, and honours `original.asset.database == AssetDatabase<ParadoxMods>.instance` for per-mod categorisation. **Best exemplar for "generate hundreds of assets from the loaded database."**

**(c) Regenerate a prefab in-place at runtime — the hardest case.**
`RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderSystem.cs:241-274` `UpdatePrefab(NetGeometryPrefab)`:

```
EntityManager.AddComponent<DiscardedRoadBuilderPrefab>(entity);
prefabSystem.UpdatePrefab(prefab, entity);
// then: remove the entity from ToolbarUISystem's private m_LastSelectedAssets dictionary
// then: scan every UIGroupElement buffer and RemoveAt the stale reference
```

The two clean-up steps exist because `PrefabSystem.UpdatePrefab` leaves the toolbar and UI-group buffers holding stale references. Combined with the throttle at `RoadBuilderSystem.OnUpdate:65-88` (`lastUpdateRequest > DateTime.Now.AddSeconds(-0.66)` and a queue) this is the corpus's most hard-won prefab code.

Generation itself: `RoadBuilder-CSII/RoadBuilder/Utilities/NetworkPrefabGenerationUtil.cs:36-128` — builds `m_Sections` (`NetSectionInfo[]`), `m_NodeStates` (`NetNodeStateInfo[]`), `m_EdgeStates` (`NetEdgeStateInfo[]`), `m_AggregateType`, `m_InvertMode = CompositionInvertMode.InvertLefthandTraffic`, destroys and rebuilds `prefab.components`, and mints an ID as `{typeInitial}{Guid}-{PlatformManager.instance.userSpecificPath}`.

### 4.2 Prefab lookup surface used

`PrefabSystem.TryGetPrefab(PrefabID)`, `TryGetPrefab<T>(PrefabData)`, `TryGetSpecificPrefab<T>(PrefabRef)`, `GetPrefab<PrefabBase>(Entity)`, `TryGetEntity(PrefabBase, out Entity)`, `GetEntity(PrefabBase)`, `AddPrefab`, `UpdatePrefab`. `new PrefabID("FencePrefab", name)` / `new PrefabID(nameof(RenderPrefab), meshName)` is the string-keyed lookup form.

### 4.3 Prefab-data initialisation systems

Because `AddPrefab` only creates the prefab entity, mods add a `PrefabUpdate`-phase system that fills in the ECS prefab-data components on `WithAll<PrefabData, Created>`:
`CS2-Platter/Platter/Systems/Parcels/P_ParcelInitializeSystem.cs:25-120` writes `ObjectGeometryData` (`GeometryFlags.WalkThrough | Marker | ExclusiveGround | HasLot | Brushable`), `PlaceableObjectData` (`PlacementFlags.OwnerSide`), `ParcelData` — with a long comment (lines 36-53) explaining each flag choice and why `GeometryFlags.Marker` forces the Harmony marker patches.

### 4.4 External asset import

- **Runtime SVG thumbnail synthesis:** `RoadBuilder-CSII/RoadBuilder/Utilities/ThumbnailGenerationUtil.cs:55-90` composes SVG fragments with `System.Xml.Linq`, saves to `FoldersUtil.TempFolder`, and returns `coui://roadbuilderthumbnails/{id}.svg`, which resolves because `Mod.cs:36` did `UIManager.defaultUISystem.AddHostLocation("roadbuilderthumbnails", FoldersUtil.TempFolder, true)` [ed. 2026-08-04: the registration is what resolves it and the `true` is not — see the 2026-08-04 verdict in `prefabs-and-assets.md` for what that flag does.]
- **Fonts, image atlases, OBJ meshes from disk:** WriteEverywhere loads TTFs (with a full C# port of stb_truetype under `BelzontWE/Font/FileReader/` — `FontInfo.cs`, `CharStringContext.cs`, `Buf.cs`, `FakePtr.cs`, `RectPackContext.cs`), packs sprite atlases (`Font/Sprites/MaxRectsBinPack.cs`, `WEAtlasesLibrary.cs`), and parses `.obj` (`IO/ObjFileHandler.cs`). Roots under `BasicIMod.ModSettingsRootFolder` (`Font/FontServer.cs:32`, `Font/Sprites/WEAtlasesLibrary.cs:35-39`).
- **Riding the PDX asset-upload pipeline:** `CS2-WriteEverywhere/BelzontWE/Overrides/AssetUploadOverrides.cs:14-60` patches `Game.UI.Menu.PdxAssetUploadHandle.CopyPreview` to copy the mod's `.we` sidecar folder (layouts/atlases/meshes) into the upload payload so custom assets carry WE data. Genuinely novel.
- **Reacting to playset changes:** `CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:90` subscribes to `(AssetDatabase<ParadoxMods>.instance.dataSource as ParadoxModsDataSource).onAfterActivePlaysetOrModStatusChanged`.

---

## 5. Tools

Nine custom tools exist. Two base classes: `ToolBaseSystem` (Anarchy's components tool, BetterBulldozer ×2, Platter test tool, FindIt picker, RoadBuilder, Traffic ×2, Tree_Controller) and `ObjectToolBaseSystem` (MoveIt, LineTool — needed when you want the vanilla object-preview/definition machinery).

### 5.1 The `ToolBaseSystem` contract, in full

From `LineTool-CS2/Code/Systems/LineToolSystem.cs` (cleanest 1100-line reference):

- `public override string toolID` (:130)
- `InitializeRaycast()` → set `m_ToolRaycastSystem.typeMask` etc. (:449-455)
- `GetPrefab()` / `TrySetPrefab(PrefabBase)` (:461-482) — return `false` to reject a prefab
- `ElevationUp()` / `ElevationDown()` / `ElevationScroll()` (:487-492)
- `GetAvailableSnapMask(out Snap onMask, out Snap offMask)` (:499-509)
- `OnCreate` / `OnStartRunning` / `OnStopRunning` / `OnGameLoadingComplete(Purpose, GameMode)`
- `protected override JobHandle OnUpdate(JobHandle inputDeps)`

Traffic adds `allowUnderground`, `SetUnderground(bool)`, `requireUnderground`, and overrides `protected override bool GetRaycastResult(out ControlPoint)` and its `forceUpdate` overload (`Traffic/Code/Tools/LaneConnectorToolSystem.cs:159,228,496-512`).

### 5.2 Tool registration and the tool-list ordering hack

Tools registered via `updateSystem.UpdateAt<T>(SystemUpdatePhase.ToolUpdate)` are appended to `ToolSystem.tools`. Order matters for prefab-claim priority, so LineTool removes and reinserts itself at index 0 — unless Tree Controller is already there, in which case index 1:
`LineTool-CS2/Code/Systems/LineToolSystem.cs:578-607`. Activation is `m_ToolSystem.activeTool = this` after stashing `_previousTool` (:514-552).

### 5.3 The apply/cancel loop

`applyMode` (`ApplyMode.Clear` / `.None` / `.Apply`) is the state machine: `Clear` = discard the preview, `None` = keep last frame's preview untouched (the cheap path), `Apply` = commit. `LineToolSystem.OnUpdate:675-926` is the readable version; `Traffic/Code/Tools/LaneConnectorToolSystem.cs:557-912` is the industrial one (60+ `applyMode` assignments across a `State` enum: `Default` / `SelectingSourceConnector` / `SelectingTargetConnector` / `RemovingSourceConnections` / …).

Input comes from `applyAction` / `cancelAction` / `secondaryApplyAction` (`ProxyAction`) with `WasPressedThisFrame()` / `WasReleasedThisFrame()` / `IsPressed()`, and `action.shouldBeEnabled = true` in `OnStartRunning` (`LineToolSystem.cs:931-954`).

Sound: `_audioManager.PlayUISound(_soundEffectsQuery.GetSingleton<ToolUXSoundSettingsData>().m_NetStartSound / m_PlaceBuildingSound / m_PlacePropSound / m_NetNodeSound)` (`LineToolSystem.cs:793-823`) — a nice polish detail most mods miss.

### 5.4 Custom raycasting against mod-owned entities

Vanilla raycasting can't see entities the game doesn't know about. Traffic solves this with a **complete parallel raycast pipeline**:

- `Traffic/Code/Systems/ModRaycastSystem.cs:25-130` registered at `SystemUpdatePhase.Raycast` (`Mod.cs:88`), with `NativeReference<CustomRaycastInput>` / `<CustomRaycastResult>`, a `RaycastTerrainJob`, `RaycastJobs.FindConnectionNodeFromTreeJob` / `FindLaneHandleFromTreeJob` against its own quadtrees, then `RaycastLaneConnectionSubObjects` / `RaycastLaneHandles` accumulating into a `NativeAccumulator<RaycastResult>`.
- The tool feeds it via `InitializeCustomRaycastInput()` (`LaneConnectorToolSystem.cs:298-330`) and reads it via a private `GetCustomRaycastResult(out ControlPoint)` that mirrors the vanilla signature (:469-512).

Simpler approach: BetterBulldozer and Platter widen the _vanilla_ raycast masks and then filter results (see §2.1a).

### 5.5 Snapping

- Declarative: `GetAvailableSnapMask` + `Snap.ContourLines` (LineTool), plus a `ToolBaseSystem.GetActualSnap` postfix (Platter).
- Custom snap job replacing `ObjectToolSystem.SnapControlPoint` — `CS2-Platter/Platter/Systems/Tool/P_SnapSystem.AdhocParcelSnapJob.cs` + the patch at `Patches/ToolSystemPatch.cs:100-178`; snaps parcels to road edges/zone blocks/other parcels using zone, net and parcel quadtrees, publishing `IsSnapped` via a `NativeReference` the UI binds to.
- `CS2-MoveIt/Code/MoveIt/Snapper/Snapper.cs` (276 lines) — MoveIt's own snapping engine.

### 5.6 Tool overlays and tooltips

- Overlays: `World.GetOrCreateSystemManaged<OverlayRenderSystem>().GetBuffer(out JobHandle)` then `buffer.DrawCircle/DrawCurve/DrawLine`. Exemplars: `CS2-MoveIt/Code/MoveIt/Overlays/DrawTools.cs` (328 lines, the richest), `Traffic/Code/Rendering/ToolOverlaySystem.cs` + 5 job partials, `LineTool-CS2/Code/LineModes/LineBase.cs`.
- Tooltips: subclass `Game.UI.Tooltip.TooltipSystemBase` at `SystemUpdatePhase.UITooltip`, emit `StringTooltip` / `FloatTooltip`. Exemplars: `Traffic/Code/UISystems/LaneConnectorToolTooltipSystem.cs`, `Anarchy/Anarchy/Systems/Common/AnarchyTooltipSystem.cs`, `CS2-Platter/Platter/Systems/UI/P_TooltipSystem.cs`.

### 5.7 Editor-mode tools

`CS2-WriteEverywhere/BelzontWE/Systems/WEMainUISystem.cs:29-34` registers a tool in the map editor by **resizing `EditorToolUISystem.tools` in `OnGamePreload`** and appending a `WEEditorTool`. RoadBuilder does the same via `RoadBuilderEditorTool` (`Domain/RoadBuilderEditorTool.cs`).

---

## 6. UI: C# ↔ Cohtml ↔ React

### 6.1 C# side — binding types

`Colossal.UI.Binding` namespace. Observed: `ValueBinding<T>`, `GetterValueBinding<T>`, `TriggerBinding` (arity 0–4), plus `IWriter<T>`/`IReader<T>` implementations (`ListWriter<T>`, `DictionaryWriter<K,V>`, `ValueWriter<T>`, custom `EdgeInfoWriter`), and `IEqualityComparer` overrides (`JsonWriter.FalseEqualityComparer<T>` to force every-frame pushes). `RawValueBinding` / `CallBinding` are **not** used anywhere.

**The `ExtendedUISystemBase` + `ValueBindingHelper<T>` + `GenericUIWriter<T>`/`GenericUIReader<T>` trio is copy-pasted into 7 of 12 mods** (Anarchy, BetterBulldozer, Tree_Controller, Platter, FindIt, RoadBuilder, TLE), with the same key convention `BINDING:{key}` / `TRIGGER:{key}`. Best-formatted copy: `CS2-Platter/Platter/Extensions/ExtendedUISystemBase.cs:11-87` + `ValueBindingHelper.cs:10-39` (note `implicit operator T` so the helper reads like a plain value) + `GenericUIWriter.cs:14-60` (reflection-based POCO → `IJsonWriter`, honouring `IJsonWritable`). A skill should ship this as a template and say "this is the de-facto community standard, it is not in the game API."

There is a parallel `ExtendedInfoSectionBase : Game.UI.InGame.InfoSectionBase` for adding rows to the selected-info panel — `Anarchy/Anarchy/Extensions/ExtendedInfoSectionBase.cs:11-88`, `CS2-Platter/Platter/Extensions/ExtendedInfoSectionBase.cs`.

Bindings are added from a `Game.UI.UISystemBase` subclass registered at `SystemUpdatePhase.UIUpdate`. Best complete UI systems: `LineTool-CS2/Code/Systems/LineToolUISystem.cs` (23 `GetterValueBinding`s), `Traffic/Code/UISystems/ModUISystem.cs`, `CS2-MoveIt/Code/MoveIt/UI/UISystem.cs`.

### 6.2 The Cohtml `engine.call` RPC alternative

WriteEverywhere bypasses bindings for commands. C# registers named delegates:

```
callBinder($"{PREFIX}exportComponentAsXml", ExportComponentAsXml);   // "layouts."
```

`CS2-WriteEverywhere/BelzontWE/Controllers/WELayoutController.cs:24-52` (26 methods), backed by `IBelzontBindable.SetupCallBinder(Action<string,Delegate>)` / `SetupCaller(Action<string,object[]>)` (see `Controllers/WEWorldPickerController.cs:19-135`). TS calls them as promises:

```
static async listCityTemplates(): Promise<Record<string,string>> {
  return await engine.call("k45::we.layouts.listCityTemplates");
}
```

`CS2-WriteEverywhere/_Frontends/UI/k45-we-vuio/src/services/LayoutsService.tsx:5-50`. **This is the only request/response (as opposed to push/observe) C#↔UI channel in the corpus** and is worth teaching as the escape hatch for "I need to return a value."

TLE's variant: everything is a JSON string over `GetterValueBinding<string>` with `Newtonsoft.Json` on both sides, wrapped in `OneWayBinding<T>` / `TwoWayBinding<T>` classes (`Cities2-TrafficLightsEnhancement/TrafficLightsEnhancement/UI/src/bindings.ts:1-60`, `utils/oneWayBinding.ts`, C# side `Systems/UI/UISystem.UIBIndings.cs:53-80`). Simple, type-unsafe, and it works.

### 6.3 TS side — injection into the game's UI

Every mod exports `const register: ModRegistrar = (moduleRegistry) => {...}` from `src/index.tsx`. Two operations:

- `moduleRegistry.append(anchor, Component)` — anchors observed: `'Game'`, `'Editor'`, `'GameTopLeft'`, `'Menu'`.
- `moduleRegistry.extend(modulePath, exportName, hoc)` — the HOC receives the original export and returns a replacement. Paths observed across the corpus:
  - `game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.tsx` → `MouseToolOptions` (7 mods)
  - `game-ui/game/components/tool-options/gamepad-tool-options/gamepad-tool-options.tsx` → `GamepadToolOptions` (Anarchy only)
  - `game-ui/game/components/tool-options/tool-options-panel.tsx` → `useToolOptionsVisible` (5 mods — required to make the panel appear for a custom tool) and `ToolOptionsPanel`
  - `game-ui/game/components/right-menu/right-menu.tsx` → `RightMenu`
  - `game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx` → `selectedInfoSectionComponents`
  - `game-ui/game/components/toolbar/top/toggles.tsx` → `PhotoModeToggle` (MoveIt, FindIt — hijacking a known toggle to add a toolbar button)
  - `game-ui/game/components/asset-menu/asset-menu.tsx` → `AssetMenu`
  - `game-ui/editor/components/toolbar/toolbar.tsx` → `Toolbar`
  - `game-ui/game/data-binding/game-bindings.ts` → `GamePanelType` **and** `game-ui/game/components/game-panel-renderer.tsx` → `gamePanelComponents` — **registering an entirely new game panel type**, `CS2-WriteEverywhere/_Frontends/UI/k45-we-vuio/src/index.tsx:12-26`, paired on the C# side with `GamePanelUISystem.SetDefaultArgs(new WEMainPanel())` and `panelSystem.ShowPanel<WEMainPanel>(x)` (`BelzontWE/WriteEverywhereCS2Mod.cs:101`, `Systems/WEMainUISystem.cs:19`). Best exemplar in the corpus.

Best-commented `index.tsx`: `Anarchy/Anarchy/UI/src/index.tsx:14-47` (includes the "-uiDeveloperMode + localhost:9444" discovery instructions).

### 6.4 `VanillaComponentResolver` — reusing unexported game components

Since the game exports very few components, mods pull them out of the module registry by path+name. The singleton is copy-pasted (with an attribution comment "written by Klyte") into Anarchy, BetterBulldozer, Tree_Controller, RoadBuilder, FindIt, Platter. Reference copy: `Anarchy/Anarchy/UI/src/mods/VanillaComponentResolver/VanillaComponentResolver.tsx:39-84` — a `registryIndex` map of `name → [modulePath, exportName]`, a lazy `cachedData`, and hand-guessed prop types (comment at lines 5-8 explains the discovery workflow: `localhost:9444` → Sources → Index.js → pretty-print → search for the `.tsx`/`.scss` name). It resolves both components (`Section`, `ToolButton`, `StepToolButton`) _and_ SCSS class maps (`*.module.scss` → `classes`) and focus helpers (`FOCUS_DISABLED`, `useUniqueFocusKey`).

### 6.5 Serving custom images

`UIManager.defaultUISystem.AddHostLocation("<scheme>", absolutePath, bool)` → referenced as `coui://<scheme>/foo.svg`. Used by Platter (`PlatterMod.cs:162`), FindIt (`Mod.cs:50`), RoadBuilder (`Mod.cs:36,40`). WriteEverywhere goes further and **patches the Cohtml resource handler itself** to serve generated bytes: `Overrides/GameUIResourceHandlerOverrides.cs:18-100` intercepts `GameUIResourceHandler.OnResourceRequest` / `DefaultResourceHandler.OnResourceStreamRequest` and answers `coui://we.k45/_fonts/*`, `_css/*` (synthesised `@font-face` rules) and `_textureAtlas/*` from memory with `Marshal.Copy` into `response.GetSpace(size)`.

### 6.6 Type sharing between C# and TS

- **Best:** Traffic generates `UI/src/types/traffic.d.ts` from C# with **Reinforced.Typings** (`<PackageReference Include="Reinforced.Typings" Version="1.6.5">` in `Traffic/Code/Traffic.csproj:736`; config at `Traffic/Code/Utils/ReinforcedTypingsConfiguration.cs:19-51`, which substitutes `Unity.Entities.Entity` → `cs2/utils`'s `Entity` and `Game.Input.ProxyBinding` → `WidgetIdentifier`, and exports binding-key constant classes and enums). Consumed in `Traffic/UI/src/bindings/index.ts:1-13`. Nobody else does this and it is clearly the right answer.
- Everyone else hand-writes interfaces or passes JSON strings.

### 6.7 The legacy DOM-injection hack (anti-pattern to flag)

`BetterBulldozer/BetterBulldozer/Systems/BetterBulldozerUISystem.cs:252-277` and `Tree_Controller/Tree_Controller/Tools/TreeControllerUISystem.cs:462-465,633,1191-1194` call `GameManager.instance.userInterface.view.View.ExecuteScript(...)` with a stringified JS loop over `document.getElementsByTagName("img")`, matching on `src.includes("Bulldozer.svg")` to add/remove the `selected` CSS class. It works but is brittle and localisation/theme-dependent. Both are the only mods doing this; treat as "what modding looked like before `ModRegistrar` matured."

---

## 7. Settings, localization, key bindings, logging, save/load

### 7.1 Settings

Subclass `Game.Settings.ModSetting`; decorate with `[FileLocation("Name")]`, `[SettingsUITabOrder(...)]`, `[SettingsUIGroupOrder(...)]`, `[SettingsUIShowGroupName(...)]`. Property attributes seen: `SettingsUISection`, `SettingsUISlider(min,max,step,unit: Game.UI.Unit.kFloatSingleFraction)`, `SettingsUIDropdown(type, methodName)`, `SettingsUIButton`, `SettingsUIConfirmation`, `SettingsUIDisableByCondition`, `SettingsUIHideByCondition`, `SettingsUIHidden`, `SettingsUISetter`, `SettingsUIMultilineText("coui://...")`, `SettingsUIValueVersion`, `SettingsUIAdvanced`.
Persist with `AssetDatabase.global.LoadSettings(name, instance, defaultsInstance)`; a settings _action_ (button) is a `bool` property with only a setter.
**Best exemplar:** `Traffic/Code/ModSettings.cs:18-80` — includes a language dropdown driven by `[SettingsUIValueVersion]` and reset buttons that call into live systems.

### 7.2 Key bindings

Class-level `[SettingsUIKeyboardAction(name, ...usages)]` / `[SettingsUIMouseAction(...)]` / `[SettingsUIGamepadAction(...)]` declare actions; property-level `[SettingsUIKeyboardBinding(BindingKeyboard.R, ActionName, ctrl: true)]` / `[SettingsUIMouseBinding(BindingMouse.Left, ActionName)]` on `ProxyBinding` properties declare defaults. Then `Settings.RegisterKeyBindings()` in `OnLoad`, and `settings.GetAction(name)` returns a `ProxyAction`.
**Best exemplar:** `Traffic/Code/ModSettings.Keybindings.cs:1-110` — 13 actions with custom **usage contexts** (`ModKeyUsages.LaneConnectorToolActive`, `ModKeyUsages.PrioritiesToolActive`, plus `Usages.kToolUsage` / `kEditorUsage` / `kDefaultUsage`), plus a `UseVanillaToolActions` toggle that swaps between mod bindings and `ProxyBinding.Watcher`s on the vanilla ones.

**The "mimic binding" hack (recurs twice, teach it).** Mod tools cannot take over the vanilla Apply/Cancel actions, so:

- Declarative form: `[SettingsUIMouseBinding(AnarchyMod.SecondaryMimicAction)] [SettingsUIBindingMimic(InputManager.kToolMap, "Secondary Apply")] [SettingsUIHidden] public ProxyBinding SecondaryApplyMimic { get; set; }` — `Anarchy/Anarchy/Settings/AnarchyModSettings.cs:212-216`; also used for `Change Elevation` at :278-290.
- Imperative form: `RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderToolSystem.cs:66-81` looks up `InputManager.instance.FindAction(InputManager.kToolMap, "Apply")`, copies `.path` and `.modifiers` onto its own mouse binding, and calls `InputManager.instance.SetBinding(mimicApplyBinding, out _)`.
- Registering an entirely custom `ProxyAction` map by reflecting the internal `InputManager.AddActions`: `CS2-Platter/Platter/PlatterMod.cs:270-330`.

MoveIt wraps all of this in a declarative registry: `CS2-MoveIt/Code/MoveIt/Systems/InputSystem.cs:16-90` (`RegisterBinding(new(action, context: QInput_Contexts.ToolEnabled, trigger: ...))`) plus press/drag/click state machines in `Input/InputButton.cs:40-90`.

### 7.3 Localization

Four distinct strategies, all valid:

1. **Embedded per-locale JSON, one file per locale** — `{Assembly}.l10n.{localeID}.json`, loaded with `Colossal.Json.JSON.Load(...).Make<Dictionary<string,string>>()` into a `MemorySource`. `Anarchy/Anarchy/AnarchyMod.cs:170-214` (verbatim in MoveIt `Mod.cs:107-151`).
2. **Embedded per-locale CSV with a hand-written quote-aware parser and key packing** — `PlopTheGrowables/Code/Localization.cs:24-90` (the doc comment at :26-38 is an excellent spec). Same in LineTool (`l10n/*.csv` as `EmbeddedResource`).
3. **One embedded JSON containing all locales** — `LocaleHelper(string resourceName)` scanning `GetManifestResourceNames()` and yielding `DictionarySource : IDictionarySource` per locale. `RoadBuilder-CSII/RoadBuilder/Utilities/LocaleHelper.cs:16-80`; same file in FindIt and TLE.
4. **Loose JSON files next to the DLL + an in-mod language dropdown** — Traffic (`Localization.cs` split into `.LocaleEN`, `.ModLocale`, `.UIKeys`, `.LocaleManager` partials; csproj copies `Localization/*.json` with `PreserveNewest`).

All four back an `IDictionarySource`-implementing `LocaleEN` for the settings screen, keyed by `settings.GetOptionLabelLocaleID(nameof(Prop))` / `GetOptionDescLocaleID` / `GetSettingsLocaleID` / `GetOptionTabLocaleID` / `GetOptionGroupLocaleID`.
Crowdin integration (`crowdin.yml`) is present in TLE, MoveIt, Platter, FindIt, RoadBuilder.
Localization export for translators: `#if DEBUG` blocks that dump `LocaleEN.ReadEntries(...)` to JSON (`Anarchy/Anarchy/AnarchyMod.cs:98-110`, `Traffic/Code/Mod.cs:117-119` behind `LOCALIZATION_EXPORT`). Note the hard-coded absolute developer paths in Anarchy and MoveIt (`C:\Users\TJ\source\repos\...`) — a smell.

### 7.4 Logging

`Colossal.Logging.LogManager.GetLogger("Mods_Author_Name").SetShowsErrorsInUI(false)` is universal; `Log.effectivenessLevel = Level.Debug/Verbose` gated by `#if DEBUG`/`#if VERBOSE`. Two wrappers worth noting: `CS2-Platter/Platter/Utils/PrefixedLogger` instantiated per-system in a shared `PlatterGameSystemBase.OnCreate` (`CS2-Platter/Platter/Systems/PlatterGameSystemBase.cs:14-23`) and MoveIt's `QLog`. Traffic has category-specific static log methods (`Logger.Serialization`, `Logger.DebugTool`, `Logger.DebugConnections`) compiled out via `DefineConstants`. TLE adds a `Mod.Assert(condition, message, showInUI, [CallerArgumentExpression] expression)` helper (`Cities2-TrafficLightsEnhancement/TrafficLightsEnhancement/Mod.cs:119-129`).

### 7.5 Save/load serialization

Three mechanisms:

1. **`IEmptySerializable` tag components** — free persistence of flags. `PlopTheGrowables/Code/Systems/PloppedBuilding.cs:15`.
2. **`Colossal.Serialization.Entities.ISerializable` on components/buffers** — `Serialize<TWriter>(TWriter) where TWriter : IWriter` / `Deserialize<TReader>`. Simple: `CS2-Platter/Platter/Components/Parcel.cs:60-78`. **Versioned:** `Traffic/Code/Components/LaneConnections/ModifiedLaneConnections.cs:40-72` writes a version int first and branches on read, backed by a central version registry `Traffic/Code/DataMigrationVersion.cs` whose comments name the exact game/mod version that broke the format.
3. **Out-of-band files** — RoadBuilder writes each road config to disk (`RoadBuilder-CSII/RoadBuilder/Utilities/LocalSaveUtil.cs`, driven by `Systems/RoadBuilderSerializeSystem.cs:48-70`, with `CURRENT_VERSION = 5` and named version constants at :26-31) and _also_ writes `NetworkConfigComponent { NetworkId }` marker entities into the save so a load knows which configs it needs. WriteEverywhere serialises layouts to XML (`BelzontWE/IO/WETextDataXml.cs`, `WETextDataXmlTree.cs`).

**Migration systems** are a first-class concern: `Traffic/Code/Systems/Serialization/TrafficDataMigrationSystem.cs` + 4 job partials (`FindIncompleteV1DataJob`, `FindIncompleteV2DataJob`, `ValidateLoadedDataJob`, `ValidateLoadedReferencesJob`), scheduled at `Modification4` rather than `Deserialize` with an explicit comment (`Traffic/Code/Mod.cs:81-82`): _"data migration - requires NetCompositions to work correctly - not possible to run in SystemUpdatePhase.Deserialize."_ There is also a cross-mod migration: `Traffic/Code/Systems/ModCompatibility/TLEDataMigrationSystem.cs` imports TLE's lane directions.

---

## 8. Build / toolchain

### 8.1 The standard csproj shape (11 of 12 follow it)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <LangVersion>9</LangVersion>
    <Configurations>Debug;Release;Stable</Configurations>
    <PublishConfigurationPath>Properties\PublishConfiguration.xml</PublishConfigurationPath>
  </PropertyGroup>
  <!-- Imports must be after PropertyGroup -->
  <Import Project="$([System.Environment]::GetEnvironmentVariable('CSII_TOOLPATH','EnvironmentVariableTarget.User'))\Mod.props" />
  <Import Project="$(...CSII_TOOLPATH...)\Mod.targets" />
```

`Mod.props`/`Mod.targets` come from the official **CSII Modding Toolchain**, which also supplies `$(ManagedPath)` / `$(CSII_MANAGEDPATH)`, `$(DeployDir)`, and a `DeployWIP` target. `CSII_USERDATAPATH` is read on the JS side too.

Game references are `<Reference Include="Game">` etc. with **`<Private>false</Private>` on every single one** — that's the non-obvious, must-teach detail (otherwise the game DLLs get copied into the mod folder). The recurring reference set: `Game`, `Colossal.Core`, `Colossal.Logging`, `Colossal.IO.AssetDatabase`, `Colossal.UI`, `Colossal.UI.Binding`, `Colossal.Localization`, `Colossal.Mathematics`, `Colossal.Collections`, `Colossal.AssetPipeline`, `Colossal.Mono.Cecil`, `Colossal.PSI.Common`, `Unity.Entities`, `Unity.Collections`, `Unity.Burst`, `Unity.Mathematics`, `Unity.InputSystem`, `UnityEngine.CoreModule`, `cohtml.Net`. TFM is `net48` (set by Mod.props); Anarchy overrides to `net472`.

Two styles: inline `<ItemGroup>` (Anarchy, Traffic, WriteEverywhere) vs. a shared `Config/References.csproj` imported by both the mod and its test project (`LineTool-CS2/Config/References.csproj`, `PlopTheGrowables/Config/References.csproj`, `CS2-Platter/References.csproj`). The shared file is clearly better.

Harmony is `<PackageReference Include="Lib.Harmony" Version="2.2.2" />` in all six patching mods.

### 8.2 Publishing metadata

`Properties/PublishConfiguration.xml` (`ModId`, `DisplayName`, `ShortDescription`, `LongDescription`, `Thumbnail`, `Screenshot`, `Tag`, `ForumLink`, `ModVersion`, `GameVersion`, `Dependency`, `ChangeLog`, `ExternalLink`). It's kept in sync from MSBuild:

```xml
<Target Name="SetupAttributes" BeforeTargets="BeforeBuild">
  <XmlPoke XmlInputPath="$(PublishConfigurationPath)" Value="$([System.IO.File]::ReadAllText(.../LongDescription.md))" Query="//LongDescription" />
  <XmlPoke ... Query="//ChangeLog" />
  <XmlPoke ... Value="$(Version)" Query="//ModVersion/@Value" />
</Target>
```

`Anarchy/Anarchy/Anarchy.csproj:206-210`, `Traffic/Code/Traffic.csproj:729-733`, `LineTool-CS2/LineTool.csproj:294-298`.
WriteEverywhere instead declares the metadata as MSBuild properties/items (`<ModId>`, `<DisplayName>`, `<ModTag Include>`, `<Screenshots Include>`, `<Dependency Include="UnifiedIconLibrary">`) and lets its `belzont_public.targets` render the XML — `CS2-WriteEverywhere/BelzontWE/BelzontWE.csproj:761-782`. It also auto-generates a debug version `65534.$([System.DateTime]::Now.ToString("yyyy.Mdd.HHmm"))`.

### 8.3 UI build wiring

The near-universal one-liner:

```xml
<Target Name="InstallUI" AfterTargets="AfterBuild">
  <Exec Command="npm run build" WorkingDirectory="$(ProjectDir)/UI" />
</Target>
```

Platter and MoveIt correct this to `AfterTargets="DeployWIP"` because `Mod.targets`' `DeployWIP` wipes and recreates `$(DeployDir)`; **MoveIt documents this precisely** (`CS2-MoveIt/Code/MoveIt/MoveIt.csproj`, the `CopyFiles` target and its "Why AfterTargets=DeployWIP" comment). MoveIt also adds three MSBuild knobs (`RunMoveItUIBuild`, `RunMoveItNpmCi`, `MoveItForceNpmCiAlways`), a `node_modules/.moveit-npmci.stamp` for lockfile-triggered `npm ci`, timeouts, and hard-fail-on-Release / warn-on-Debug semantics. That csproj is the corpus's best build-engineering artefact.

The webpack config is the `create-csii-ui-mod` scaffold, essentially identical across all 12: `externalsType: "window"` with `react`, `react-dom`, `cs2/modding`, `cs2/api`, `cs2/bindings`, `cs2/l10n`, `cs2/ui`, `cs2/input`, `cs2/utils`, `cohtml/cohtml` all external; `output.library.type: "module"`, `experiments.outputModule: true`, `publicPath: "coui://ui-mods/"`, output straight into `$CSII_USERDATAPATH\Mods\{MOD.id}`; CSS modules with `localIdentName: "[local]_[hash:base64:3]"`; `MiniCssExtractPlugin`; a `mod.json` (`{id, author, version, dependencies}`) that is imported by both webpack and the TSX. Reference: `LineTool-CS2/UI/webpack.config.js` (adds a `CSSPresencePlugin` and a friendly build banner). Platter adds ESLint/Prettier and `cross-env BUILD_PATH` dev variants; Traffic adds `copy-webpack-plugin` for images.

### 8.4 Tests

Only two mods test. `CS2-Platter/Platter.Tests/Platter.Tests.csproj` — NUnit 4 + `Microsoft.NET.Test.Sdk` + `coverlet.collector`, `net48`, `LangVersion 11` re-asserted _after_ the `Mod.props` import (a real gotcha), `ProjectReference` to the mod plus the shared `References.csproj`. Tests cover pure geometry utils only. `CS2-WriteEverywhere/BelzontWE.Tests` is far larger (~40 files) and tests the stb_truetype port, the formula engine, and ECS component value semantics, using reflection to inject into private caches (`Utils/WEFormulaeEvalCoreDispatchTests.cs:47-69`) — with a comment noting that `unmanaged` generic constraints defeat `Reflection.Emit` proxies (`Utils/WELayoutUtilityTests.cs:12`).

### 8.5 CI

Only three workflows exist in the whole corpus:

- `Cities2-TrafficLightsEnhancement/.github/workflows/release.yml` — `workflow_dispatch`, downloads the game's `Managed/` folder from a **secret URL** (`secrets.DependenciesUrl`) so the build works on `ubuntu-latest` without the game, `dotnet build`, zips, `gh release create --draft`. This is the only solution in the corpus to "you can't put game DLLs in CI."
- `CS2-Platter/.github/workflows/main.yml` — `windows-latest`, `dotnet test` + `msbuild`. Note the tail of it is unedited GitHub Windows-App template boilerplate (`Wap_Project_Path`, `PackageCertificateKeyFile`) that cannot work; low-quality.
- `CS2-Platter/.github/workflows/triage.yml` + `prompts/bug-review.prompt.yml` — LLM-assisted issue triage.

---

## 9. Notable hard-won hacks (with their explanatory comments)

Ranked by how much they teach:

1. **Rewrite a vanilla system's `EntityQuery` from outside to exclude your entities.** `Cities2-TrafficLightsEnhancement/TrafficLightsEnhancement/Utils/EntityQueryUtils.cs:21-74`, applied at `Mod.cs:70-73`. Preserves all existing `Any/None/All/Disabled/Absent/Present` descs and appends. No Harmony, no fork.
2. **`FakePrefab` — an empty `PrefabBase` whose only job is to satisfy vanilla validation.** `Traffic/Code/Helpers/FakePrefab.cs:8-11`: _"used purely for vanilla validation workaround with custom entities interacting with vanilla ones."_ Created in `IPreDeserialize.PreDeserialize` (`Traffic/Code/Systems/ModDefaultsSystem.cs:26-48`) and stamped onto mod-created entities as their `PrefabRef` (`ApplyLaneConnectionsSystem.cs:92`).
3. **Prefab regeneration leaves dangling UI state.** `RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderSystem.cs:241-274` — after `prefabSystem.UpdatePrefab`, you must evict the entity from `ToolbarUISystem.m_LastSelectedAssets` (private, reflected at `:63`) and from every `UIGroupElement` buffer. Plus the `DiscardedRoadBuilderPrefab` tombstone tag and the 0.66 s debounce.
4. **Track whether _you_ set a raycast flag before filtering its results.** `CS2-Platter/Platter/Patches/MarkerPatches.cs:33-62` with the rationale comment at :25-29 — "Should markers be already enabled, no filtering is done, this should ensure compatibility with other mods or base game changes." The most mod-compatible patch in the corpus.
5. **Mimic vanilla input bindings** (§7.2) — `[SettingsUIBindingMimic]` (Anarchy) and manual `.path` copying + `InputManager.SetBinding` (RoadBuilder). Solves "my tool needs left-click but the vanilla Apply action is reserved, and it must follow the user's rebinds."
6. **Replace a vanilla system's job scheduling wholesale.** Platter's `ObjectToolSystem.SnapControlPoint` prefix (`Patches/ToolSystemPatch.cs:100-178`), including the correct `AddSearchTreeReader`/`AddSurfaceReader` bookkeeping and an immediate `.Complete()`.
7. **Serve generated bytes over `coui://` by patching the Cohtml resource handler.** `CS2-WriteEverywhere/BelzontWE/Overrides/GameUIResourceHandlerOverrides.cs:26-100` — synthesises `@font-face` CSS on the fly and streams TTF bytes with `Marshal.Copy(arrData, 0, response.GetSpace(size), len)`.
8. **Register a brand-new game panel type** by extending both `GamePanelType` (a TS enum-like object) and `gamePanelComponents` (`CS2-WriteEverywhere/_Frontends/UI/k45-we-vuio/src/index.tsx:12-26`), matched to `GamePanelUISystem.SetDefaultArgs` / `ShowPanel<T>` in C#.
9. **Add a tool to the map editor by resizing an array.** `Array.Resize(ref editorToolUISystem.tools, len+1)` in `OnGamePreload` — `CS2-WriteEverywhere/BelzontWE/Systems/WEMainUISystem.cs:29-34`.
10. **Force-load a UI module asset.** `AssetDatabase.global.GetAsset(SearchFilter<UIModuleAsset>.ByCondition(a => a.name == "k45-we-vuio"))` then `GameManager.instance.modManager.AddUIModule(asset)` — `CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:104-109`.
11. **Insert yourself at a specific index in `ToolSystem.tools`, negotiating with a named competitor.** `LineTool-CS2/Code/Systems/LineToolSystem.cs:578-607` (defers to `"Tree Controller Tool"`).
12. **Un-cull entities the game wants to cull.** `Anarchy/Anarchy/Systems/OverridePrevention/PreventCullingSystem.cs:164-190` — an `IJobChunk` over `CullingInfo` that flips `m_PassedCulling` back on using `RenderingUtils.CalculateMinDistance` / `CalculateLod` re-derived against cached camera state.
13. **Drive rendering off `PreCullingSystem.GetCullingData(true, out JobHandle)`** so mod-drawn meshes respect the game's LOD/visibility — `CS2-WriteEverywhere/BelzontWE/Systems/WEPreCullingSystem.cs:77` feeding `Graphics.DrawMesh(..., ShadowCastingMode.TwoSided, ...)` at `Systems/WERendererSystem.cs:179`.
14. **Emissive light without the game's DOTS light path.** `CS2-WriteEverywhere/BelzontWE/Systems/WEEmissiveLightSystem.cs:14-18`: _"Mirrors the pattern used by CS2's LightCullingSystem, but uses HDAdditionalLightData GameObjects instead of the internal HDRPDotsInputs DOTS buffer (which is inaccessible from mod code without reflection)."_
15. **Cross-mod APIs without a hard assembly reference.**
    - Consumer side: `FindIt-CSII/FindIt/Mod.cs:77-99` walks `GameManager.instance.modManager`, and for each mod's `IMod` type looks for a `public static Func<string,bool> GetFindItSearchMethod(string)` by signature, then wraps it.
    - Provider side: WriteEverywhere's `Bridge/*.cs` classes carry `[Obsolete("Don't reference methods on this class directly. Always use reverse patch to access them, and don't use this mod DLL as hard dependency of your own mod.", true)]` — `CS2-WriteEverywhere/BelzontWE/Bridge/FontManagementBridge.cs:9`.
    - Reflect a _foreign_ mod's type out of the asset DB: `Traffic/Code/Mod.cs:145-151` uses `AssetDatabase.global.TryGetAsset(SearchFilter<ExecutableAsset>.ByCondition(a => a.name == "C2VM.CommonLibraries.LaneSystem"))` then `tleAsset.assembly.GetType("Game.Net.C2VMPatchedLaneSystem")` and disables it.
    - Detect a competitor by assembly scan: `LineTool-CS2/Code/Systems/LineToolSystem.cs:655-668` (`assembly.FullName.Contains("TopoToggle,")`), `Anarchy/.../MoveItIntegration/CopyAnarchyComponentsSystem.cs` reflects MoveIt's `Copying` property and `TryGetOriginalEntity` method.
16. **Reflect a private method to steer the vanilla toolbar.** `CS2-Platter/Platter/Systems/UI/P_UISystem.cs:690-712` invokes `ToolbarUISystem.SelectAsset(Entity, bool)` with a 6-line comment explaining that the toolbar shows the _selector_ prefab while a Harmony `TrySetPrefab` prefix swaps in the correctly-sized real prefab.
17. **Register a fully custom `ProxyAction` map** via `InputManager.instance.TryInvokeMethod("AddActions", ...)` — `CS2-Platter/Platter/PlatterMod.cs:270-330`.
18. **Bypass a vanilla bug in tool-error reporting.** `Traffic/Code/Tools/LaneConnectorToolSystem.cs:553` / `PriorityToolSystem.cs:259`: _"workaround for vanilla OriginalDeletedSystem result (fix bug)"_ — replaces `GetAllowApply()` with a direct `_toolFeedbackQuery.IsEmptyIgnoreFilter && (m_ToolSystem.ignoreErrors || m_ErrorQuery.IsEmptyIgnoreFilter)`.
19. **Disable tool errors by mutating the error _prefabs_, not the tools.** `Anarchy/Anarchy/Systems/ErrorChecks/DisableToolErrorsSystem.cs:63-118` queries `ToolErrorData + NotificationIconData` prefab entities and ORs in `ToolErrorFlags.DisableInGame|DisableInEditor`, with a paired `EnableToolErrorsSystem` at `ModificationEnd` to restore. The whole mod's premise, and no patching involved.

---

## Best exemplar index (what to cite in a skill)

| Technique | Best exemplar |
| --- | --- |
| Minimal correct `IMod` | `PlopTheGrowables/Code/Mod.cs:47-83` |
| Full `IMod` with everything | `Anarchy/Anarchy/AnarchyMod.cs:80-168` |
| System ordering + compat detection | `Traffic/Code/Mod.cs:50-239` |
| System-graph documentation | `CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:24-66` |
| Raycast mask catalogue | `BetterBulldozer/BetterBulldozer/Patches/BulldozeToolSystemInitializeRaycastPatch.cs:20-90` |
| Mod-compatible Harmony patching | `CS2-Platter/Platter/Patches/MarkerPatches.cs:30-231` |
| Patching overloads with out/ref | `BetterBulldozer/.../ToolBaseSystemGetRaycastResultPatch.cs:20-30` |
| Replacing a vanilla job schedule | `CS2-Platter/Platter/Patches/ToolSystemPatch.cs:100-178` |
| Cached reflection accessors | `CS2-Platter/Platter/Patches/Accessors/ObjectToolSystemFieldAccessor.cs:23-50` |
| Harmony-free vanilla-query surgery | `Cities2-TrafficLightsEnhancement/.../Utils/EntityQueryUtils.cs:21-74` |
| Forking a vanilla system | `PlopTheGrowables/Code/Systems/SelectiveZoneCheckSystem.cs` + `Mod.cs:74,82` |
| Intercepting tool definitions | `Anarchy/Anarchy/Systems/ObjectElevation/ElevateObjectDefinitionSystem.cs:51-120` |
| Reimplementing `CreateDefinitions` | `LineTool-CS2/Code/Systems/CreateDefinitions.cs` + `LineToolSystem.cs:1035-1100` |
| Barrier + ECB + job dependency contract | `Traffic/Code/Systems/LaneConnections/ApplyLaneConnectionsSystem.cs:44-98` |
| `IJobChunk` with chunk masks | `Anarchy/.../PreventCullingSystem.cs:164-190` |
| Custom quadtree search system | `Traffic/Code/Systems/LaneConnections/SearchSystem.cs`; `CS2-Platter/.../P_ParcelSearchSystem.cs:30-80` |
| Custom raycast pipeline | `Traffic/Code/Systems/ModRaycastSystem.cs:25-130` + `LaneConnectorToolSystem.cs:298-330,469-512` |
| Cleanest `ToolBaseSystem` | `LineTool-CS2/Code/Systems/LineToolSystem.cs` (whole file) |
| Most complex tool state machine | `Traffic/Code/Tools/LaneConnectorToolSystem.cs:557-912` |
| Undo/redo architecture | `CS2-MoveIt/Code/MoveIt/Managers/QueueManager.cs` + `Actions/` |
| Prefab cloning (documented) | `Anarchy/Anarchy/ExtendedRoadUpgrades/UpgradesManager.cs:143-205` |
| Prefab synthesis at scale | `CS2-Platter/Platter/Systems/P_PrefabsCreateSystem.cs:220-345` |
| Runtime prefab regeneration | `RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderSystem.cs:241-274` + `Utilities/NetworkPrefabGenerationUtil.cs:36-128` |
| Deriving assets from the game DB | `FindIt-CSII/FindIt/Systems/AutoVehiclePropGeneratorSystem.cs:92-180` |
| Prefab-data init system | `CS2-Platter/Platter/Systems/Parcels/P_ParcelInitializeSystem.cs:25-120` |
| Binding helper template | `CS2-Platter/Platter/Extensions/{ExtendedUISystemBase,ValueBindingHelper,GenericUIWriter}.cs` |
| UI injection (`extend`/`append`) | `Anarchy/Anarchy/UI/src/index.tsx:14-47`; `CS2-WriteEverywhere/_Frontends/UI/k45-we-vuio/src/index.tsx:9-45` |
| Vanilla component reuse | `Anarchy/.../UI/src/mods/VanillaComponentResolver/VanillaComponentResolver.tsx:39-84` |
| `engine.call` RPC | `CS2-WriteEverywhere/BelzontWE/Controllers/WELayoutController.cs:24-52` + `_Frontends/.../services/LayoutsService.tsx` |
| C#→TS type generation | `Traffic/Code/Utils/ReinforcedTypingsConfiguration.cs:19-51` |
| Settings + keybinds | `Traffic/Code/ModSettings.cs` + `ModSettings.Keybindings.cs` |
| Versioned save serialization | `Traffic/Code/Components/LaneConnections/ModifiedLaneConnections.cs:40-72` + `DataMigrationVersion.cs` |
| Build engineering | `CS2-MoveIt/Code/MoveIt/MoveIt.csproj` |
| Cross-platform CI without game DLLs | `Cities2-TrafficLightsEnhancement/.github/workflows/release.yml` |

## Quality flags

**Highest quality / most instructive:** Traffic (breadth + Burst + serialization discipline, though it's the least recently updated), CS2-Platter (modern C#, tests, CI, disciplined patching, excellent comments), LineTool-CS2 and PlopTheGrowables (small, StyleCop-clean, heavily documented — ideal teaching material), CS2-MoveIt (best build config; strong architecture).

**Highest ceiling, hardest to learn from:** CS2-WriteEverywhere. Depends on a closed-source `Belzont` framework (`BasicIMod`, `Redirector`, `BindableSystemBase`, `LogUtils`) that isn't in the corpus, so its `IMod`, patching and binding layers are all indirected through code you can't read. Techniques are unmatched (custom rendering, font rasterisation, Cohtml resource interception, asset-upload piggybacking) but nothing is copy-pasteable.

**Weaker / dated:**

- **Tree_Controller** and **BetterBulldozer** — the `ExecuteScript` DOM-manipulation hack (§6.7), 1300-line god-class UI systems (`TreeControllerUISystem.cs`), heavy `World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<T>()` calls _inside_ per-frame Harmony postfixes (`Tree_Controller/.../ToolbarUISystemApplyPatch.cs:31-35` does five of them per call), and string-matched tool IDs (`toolSystem.activeTool.toolID != "Line Tool"`).
- **RoadBuilder-CSII** — zero Burst, zero jobs, main-thread `ToEntityArray` scans over `UIGroupElement`, and thumbnail generation via string-concatenated SVG. Works, but not a performance model.
- **FindIt-CSII** — no custom components at all; a large hard-coded table of prefab names in `AutoVehiclePropGeneratorSystem` that will rot with each game patch.
- **Cities2-TLE** — the JSON-string-over-binding UI protocol is type-unsafe end to end; `CommonLibraries/` is an unpopulated git submodule so `LaneSystem` (its core patched-system fork) isn't actually present in this checkout.
- Cross-cutting smell: **four near-identical copies of `ReflectionExtensions.cs`** (Anarchy, BetterBulldozer, Platter, Tree_Controller) and **seven copies of `ExtendedUISystemBase`/`ValueBindingHelper`/`GenericUIWriter`** — clear evidence that the community lacks a shared library and that a skill should ship these as a scaffold.
- Hard-coded absolute developer paths shipped in source: `Anarchy/Anarchy/AnarchyMod.cs:104` and `CS2-MoveIt/Code/MoveIt/Mod.cs:66,74` (`C:\Users\TJ\source\repos\...`).
