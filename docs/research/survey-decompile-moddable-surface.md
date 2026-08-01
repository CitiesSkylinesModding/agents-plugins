# Moddable Surface of Cities: Skylines II — Decompile Survey

> **Seed survey.** Produced 2026-07-31 during the interview that became the `cs2-modding` spec, before the discovery pipeline existed.
> Read the decompiled game only, at version 1.6.0f1 (build 6216.19404, changelist 419.d6c6).
> Kept as it was written, citations intact; its recommendations are that pass's opinion, not decisions.

Corpus: `C:\Users\Morgan\Documents\Projets\DecompiledCitiesSkylines2\`
Layout: `src/<AssemblyName>/<Namespace>/<TypeName>.cs` — **two levels only**, flat, one directory per namespace, one file per top-level type.

A note on the shipped docs first: `AGENTS.md` and `docs/*.md` are useful orientation but contain at least one materially wrong claim for modders — `AGENTS.md:56` says "Use `[UpdateAfter]` or `[UpdateBefore]` to inject custom systems." **There are zero `[UpdateAfter]`/`[UpdateBefore]`/`[UpdateInGroup]` attributes anywhere in `src/Game/`** (verified by grep). Ordering is done imperatively via `UpdateSystem.UpdateAt/UpdateBefore/UpdateAfter<T>(SystemUpdatePhase)`. Treat the docs as a map, not as ground truth.

---

## 1. Assembly triage

163 assemblies under `src/` (the prompt's "~130" undercounts). Sorted by relevance to a complex code mod.

### Tier A — core modding surface (you will read these)

| Assembly                    | .cs files | Why it matters                                                                                                                                                                                                                                                             |
| --------------------------- | --------: | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Game`                      |  **4388** | Everything: simulation, prefabs, tools, UI, the modding API itself (`Game.Modding`), `SystemUpdatePhase`, `GameSystemBase`, `SystemOrder`. This is ~90% of what a mod touches.                                                                                             |
| `Colossal.Core`             |       303 | `COSystemBase` (root of every game system), `Colossal.Entities` ECS extension methods, `Colossal.Serialization.Entities` (save/load `Context`, `Purpose`, `IJsonWritable` plumbing), `Colossal.Json`, `Colossal.Randomization`, `Colossal.Reflection`, `Colossal.Version`. |
| `Colossal.IO.AssetDatabase` |       165 | Mod discovery and loading (`ExecutableAsset`), `AssetDatabase.global/game/user/packages`, `LocaleAsset`, `PrefabAsset`, `UIModuleAsset`, `ParadoxModsDataSource`. The asset-injection entry point.                                                                         |
| `Colossal.UI.Binding`       |        69 | The entire C#↔JS binding vocabulary. Any mod with UI reads all 69 files' worth of concepts.                                                                                                                                                                                |
| `Colossal.UI`               |        43 | `UIManager`, `UIView`, `DefaultResourceHandler`, `UISystem` — how you register a UI resource host / mod UI module.                                                                                                                                                         |
| `Colossal.Localization`     |        18 | `LocalizationManager.AddSource(localeId, IDictionarySource)` (`Colossal.Localization/Colossal.Localization/LocalizationManager.cs:313`) — how mods add translations. `MemorySource`, `CSVFileSource`.                                                                      |
| `Colossal.Mathematics`      |        27 | `Bezier4x3`, `Bounds3`, `Line3` — pervasive in net/geometry code; you cannot read `Game.Net` without it.                                                                                                                                                                   |
| `Colossal.Collections`      |        54 | `NativeQuadTree`, `NativeHeap`, `NativeAccumulator` etc. used by nearly every jobified system.                                                                                                                                                                             |
| `Colossal.Logging`          |        20 | `LogManager.GetLogger` — the logger every mod uses.                                                                                                                                                                                                                        |

**Tier A total: ~5,100 files.** That is the realistic reading universe.

### Tier B — occasionally relevant (read when your mod goes there)

| Assembly                                                    |  .cs files | When                                                                                                                                                                                                                                                                        |
| ----------------------------------------------------------- | ---------: | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Unity.Entities`                                            |        654 | You need DOTS semantics: `EntityQuery`, `ComponentLookup`, `EntityCommandBuffer`, `IJobChunk`, `TypeManager`. Reference material, not modding surface — but `TypeManager.InitializeAdditionalTypes` is called on your assembly (`src/Game/Game.Modding/ModManager.cs:148`). |
| `Colossal.PSI.Common`                                       |         89 | Achievements, platform gating, DLC checks. `PlatformManager`.                                                                                                                                                                                                               |
| `PDX.ModsUI`                                                |        240 | Paradox Mods browser UI. Relevant only if you touch mod distribution/playsets.                                                                                                                                                                                              |
| `PDX.SDK`                                                   |        909 | Paradox backend contracts (`PDX.SDK.Contracts.Service.Mods.Models` is imported by `ModManager`). Mostly opaque; skim only.                                                                                                                                                  |
| `Colossal.Collections`/`Colossal.IO`                        |      54/33 | Low-level containers and file IO.                                                                                                                                                                                                                                           |
| `Cohtml.Runtime` / `cohtml.Net` / `Cohtml.RenderingBackend` | 69/160/193 | Only if you are doing something exotic with the HTML view itself. The `Colossal.UI` wrapper is normally sufficient.                                                                                                                                                         |
| `Unity.Mathematics`                                         |         79 | `float3`, `quaternion`, `math.*`. Reference.                                                                                                                                                                                                                                |
| `Unity.Collections`                                         |        195 | `NativeArray`, `NativeList`, `Allocator`. Reference.                                                                                                                                                                                                                        |
| `Unity.Burst`                                               |         38 | `[BurstCompile]` semantics.                                                                                                                                                                                                                                                 |
| `Colossal.OdinSerializer`                                   |        214 | Prefab/ScriptableObject serialization internals. Read only when debugging asset load failures.                                                                                                                                                                              |
| `Colossal.AssetPipeline`                                    |        132 | Custom asset import (meshes, textures). Relevant for asset-creation mods.                                                                                                                                                                                                   |
| `Colossal.Mono.Cecil`                                       |        580 | Used by `ExecutableAsset` to scan your DLL for `IMod` without loading it. Understand _that fact_; don't read the assembly.                                                                                                                                                  |
| `Game.ArtPipeline`                                          |         48 | Editor-time art tooling.                                                                                                                                                                                                                                                    |
| `Colossal.ATL`                                              |         89 | Audio tagging library.                                                                                                                                                                                                                                                      |
| `Backtrace.Unity`                                           |        101 | Crash reporting — matters because mod exceptions get reported.                                                                                                                                                                                                              |

### Tier C — noise (never read unless chasing a specific API)

- **BCL / Mono**: `mscorlib` (2312), `System` (1626), `System.Xml` (1080), `System.Data` (704), `System.Core` (579), `System.Runtime.Serialization`, `System.Security`, `System.Drawing`, `System.ComponentModel.Composition`, `System.Configuration`, `System.EnterpriseServices`, `Mono.Security`, `System.Transactions`, `netstandard`, `System.Memory`, `System.Runtime`, … — ~7,500 files of stock .NET. Pure noise.
- **UnityEngine.\*Module** (~90 directories, ~2,700 files): `UnityEngine.CoreModule` (920), `UnityEngine.UIElementsModule` (768), etc. Stock Unity.
- **Unity render pipelines**: `Unity.RenderPipelines.HighDefinition.Runtime` (595), `Unity.RenderPipelines.Core.Runtime` (310) — only for shader/rendering mods.
- **Third-party**: `Newtonsoft.Json` (267), `com.rlabrecque.steamworks.net` (460), `ICSharpCode.SharpZipLib` (116), `Cinemachine` (106), `DiscordSDK` (64), `Unity.TextMeshPro`, `Unity.VectorGraphics`, `Unity.Timeline`, `Unity.InputSystem` (334 — but see caveat below).
- **Test/tooling**: `AssetDatabase.TestScenarios`, `Colossal.Core.TestScenarios`, `Game.TestScenarios`, `Colossal.TestFramework`, `DryDock.Runtime` (a 15-file internal test-harness RPC client, not modding-related).

**Caveat on `Unity.InputSystem`**: it is third-party but _is_ reachable — `Game.Input` wraps it with `ProxyAction`/`ProxyBinding`, and `ModSetting` keybinding attributes produce `ProxyBinding` values (`src/Game/Game.Modding/ModSetting.cs:33`). Read `Game.Input`, not `Unity.InputSystem`.

---

## 2. Namespace map of `src/Game/` (4,388 files, 70 directories)

Ranked by size. "Rel" = mod relevance (★★★ = you will live here).

| Namespace dir                                                                                                                                                                                                       |    Files | Rel | What lives there                                                                                                                                                                                                                                                                      |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------: | :-: | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Game.Prefabs`                                                                                                                                                                                                      | **1274** | ★★★ | The data-driven layer. 280 `ComponentBase` subclasses, 112 `PrefabBase` subclasses, 390 files with `IComponentData`, plus `PrefabSystem`. Biggest namespace by far.                                                                                                                   |
| `Game.Simulation`                                                                                                                                                                                                   |      479 | ★★★ | 300 `GameSystemBase` subclasses — the actual simulation. Citizens, traffic, economy, water, climate, pathfinding setup.                                                                                                                                                               |
| `Game.UI.InGame`                                                                                                                                                                                                    |      224 | ★★★ | 67 UI systems + their DTOs: info panels, infoviews, Chirper, budget, selected-info sections.                                                                                                                                                                                          |
| `Game.Rendering`                                                                                                                                                                                                    |      155 |  ★  | 63 systems: batching, culling, overlays, infoview rendering. Only for visual mods.                                                                                                                                                                                                    |
| `Game.Net`                                                                                                                                                                                                          |      148 | ★★★ | Road/rail/pipe network components + 26 systems. 58 files with `IComponentData` (`Edge`, `Node`, `Lane`, `Curve`, …).                                                                                                                                                                  |
| `Game.UI.Widgets`                                                                                                                                                                                                   |      145 | ★★  | The declarative widget model (`IWidget`, `DropdownField`, `IntSliderField`, …) used by the editor and by the options UI that renders `ModSetting`.                                                                                                                                    |
| `Game.Buildings`                                                                                                                                                                                                    |      145 | ★★★ | 80 files with `IComponentData` — building state components.                                                                                                                                                                                                                           |
| `Game.UI.Editor`                                                                                                                                                                                                    |      111 |  ★  | Map/asset editor panels.                                                                                                                                                                                                                                                              |
| `Game.Tools`                                                                                                                                                                                                        |      111 | ★★★ | `ToolBaseSystem` (`Game.Tools/ToolBaseSystem.cs:28`) + 53 tool systems. `Temp`, `Hidden`, `Error` components live here.                                                                                                                                                               |
| `Game.Vehicles`                                                                                                                                                                                                     |       92 | ★★  | Vehicle state components (28 with `IComponentData`) + AI-adjacent data.                                                                                                                                                                                                               |
| `Game.Prefabs.Modes`                                                                                                                                                                                                |       86 |  ★  | Game-mode prefab definitions.                                                                                                                                                                                                                                                         |
| `Game.Tutorials`                                                                                                                                                                                                    |       85 |  ·  | Tutorial triggers (32 `IComponentData`). Mostly noise.                                                                                                                                                                                                                                |
| `Game.Objects`                                                                                                                                                                                                      |       85 | ★★★ | `Transform`, `Elevation`, `Static`, `Attached` — the spatial-object components everything references.                                                                                                                                                                                 |
| `Game.Settings`                                                                                                                                                                                                     |       80 | ★★★ | `Setting` base class + **all 40+ `SettingsUI*Attribute` types** that `ModSetting` uses.                                                                                                                                                                                               |
| `Game.Pathfind`                                                                                                                                                                                                     |       80 |  ★  | Pathfinding data structures and the async pathfind service.                                                                                                                                                                                                                           |
| `Game.Serialization`                                                                                                                                                                                                |       74 | ★★  | 62 systems: `LoadGameSystem`, `SaveGameSystem`, `SerializerSystem`. Mods that persist data must understand this.                                                                                                                                                                      |
| `Game.Routes`                                                                                                                                                                                                       |       70 | ★★  | Transit lines, waypoints, stops.                                                                                                                                                                                                                                                      |
| `Game.Debug`                                                                                                                                                                                                        |       69 |  ★  | `BaseDebugSystem` (29 subclasses), debug UI/watch — useful for mod diagnostics.                                                                                                                                                                                                       |
| `Game.Citizens`                                                                                                                                                                                                     |       64 | ★★★ | `Citizen`, `Household`, `Worker`, `Student`, `TravelPurpose`.                                                                                                                                                                                                                         |
| `Game.Events`                                                                                                                                                                                                       |       63 |  ★  | Disasters, crime, accidents.                                                                                                                                                                                                                                                          |
| `Game.Input`                                                                                                                                                                                                        |       61 | ★★  | `InputManager`, `ProxyAction`, `ProxyBinding` — mod keybindings.                                                                                                                                                                                                                      |
| `Game.Areas`                                                                                                                                                                                                        |       51 |  ★  | Districts, map tiles, lots, surfaces.                                                                                                                                                                                                                                                 |
| `Game.Common`                                                                                                                                                                                                       |       49 | ★★★ | **`SystemOrder.cs` (the master system registry)**, plus the universal tag components: `Created`, `Updated`, `Deleted`, `Applied`, `Destroyed`, `Overridden`, `Owner`, `Target`, `Temp`-adjacent, the 8 `ModificationBarrier*` command-buffer systems, `RaycastSystem`, `TimeData`.    |
| `Game.UI`                                                                                                                                                                                                           |       41 | ★★★ | `UISystemBase` — the base class for any mod UI system.                                                                                                                                                                                                                                |
| `Game.UI.Tooltip`                                                                                                                                                                                                   |       39 | ★★  | `TooltipSystemBase` (24 subclasses).                                                                                                                                                                                                                                                  |
| `Game.Creatures`                                                                                                                                                                                                    |       37 |  ★  | Pedestrians, animals.                                                                                                                                                                                                                                                                 |
| `Game.City`                                                                                                                                                                                                         |       34 | ★★  | City-level singletons: stats, milestones, policies, budget.                                                                                                                                                                                                                           |
| `Game.Companies`                                                                                                                                                                                                    |       30 | ★★  | Commercial/industrial/office company components.                                                                                                                                                                                                                                      |
| `Game.Zones`                                                                                                                                                                                                        |       27 | ★★  | Zone blocks and cells.                                                                                                                                                                                                                                                                |
| `Game` (root ns)                                                                                                                                                                                                    |       27 | ★★★ | **`SystemUpdatePhase`, `UpdateSystem`, `GameSystemBase`, `GameMode`, `Version`, `EndFrameBarrier`, `SafeCommandBufferSystem`, `AutoSaveSystem`, camera controllers.** Tiny but the single most important directory.                                                                   |
| `Game.SceneFlow`                                                                                                                                                                                                    |       22 | ★★★ | `GameManager` (2425 lines — the app lifecycle god-object), `UserInterface`, `AssetLibrary`, loading screens.                                                                                                                                                                          |
| `Game.UI.Menu`                                                                                                                                                                                                      |       21 | ★★★ | `OptionsUISystem` and **`AutomaticSettings`** — the reflection engine that turns your `ModSetting` + attributes into widgets.                                                                                                                                                         |
| `Game.Triggers`                                                                                                                                                                                                     |       20 |  ·  | Chirper/social triggers.                                                                                                                                                                                                                                                              |
| `Game.Prefabs.Climate`                                                                                                                                                                                              |       19 |  ·  | Weather prefabs.                                                                                                                                                                                                                                                                      |
| `Game.Modding.Toolchain.Dependencies`                                                                                                                                                                               |       19 |  ·  | Dev-toolchain dependency install (.NET SDK etc.). Not runtime modding.                                                                                                                                                                                                                |
| `Game.Effects`                                                                                                                                                                                                      |       18 |  ·  | VFX/SFX effect components.                                                                                                                                                                                                                                                            |
| `Game.Notifications`                                                                                                                                                                                                |       17 |  ★  | Icon/notification system.                                                                                                                                                                                                                                                             |
| `Game.Simulation.Flow`                                                                                                                                                                                              |       16 |  ·  | Electricity/water flow graph internals.                                                                                                                                                                                                                                               |
| `Game.Reflection`                                                                                                                                                                                                   |       15 |  ★  | `DelegateAccessor`, `IValueAccessor` — used by the widget/settings binding machinery.                                                                                                                                                                                                 |
| `Game.Serialization.DataMigration`                                                                                                                                                                                  |       12 |  ★  | 12 systems that upgrade old saves — the practical reference for "how save versioning works".                                                                                                                                                                                          |
| `Game.UI.Localization`                                                                                                                                                                                              |       11 | ★★★ | `LocalizedString`, `LocalizedNumber`, `LocalizationUtils` — how UI text is produced.                                                                                                                                                                                                  |
| `Game.Rendering.Utilities`, `Game.Prefabs.Effects`                                                                                                                                                                  |    11/11 |  ·  |                                                                                                                                                                                                                                                                                       |
| `Game.UI.Debug`, `Game.Modding.Toolchain`                                                                                                                                                                           |    10/10 |  ·  |                                                                                                                                                                                                                                                                                       |
| `Game.Policies`, `Game.Achievements`, `Colossal.Atmosphere`                                                                                                                                                         |   8 each | ★/· | Policies matter for gameplay mods.                                                                                                                                                                                                                                                    |
| `Game.PSI`, `Game.Economy`, `Game.Assets`, `Game.Agents`                                                                                                                                                            |   7 each | ★★  | `Game.Economy` is tiny but holds `Resource`/`EconomyUtils`.                                                                                                                                                                                                                           |
| **`Game.Modding`**                                                                                                                                                                                                  |    **3** | ★★★ | `IMod.cs`, `ModManager.cs`, `ModSetting.cs`. The entire public mod API is three files.                                                                                                                                                                                                |
| `Game.Dlc`, `Game.AssetPipeline`, `Game.Rendering.CinematicCamera`, `Game.UI.Editor.Widgets`, `Colossal.Rendering`                                                                                                  |      2–3 |  ·  |                                                                                                                                                                                                                                                                                       |
| `Game.Glossary`, `Game.Audio`, `Game.Prefabs.Terrain`, `Game.Rendering.Debug`                                                                                                                                       |      5–6 |  ·  |                                                                                                                                                                                                                                                                                       |
| `Game.UI.Thumbnails`, `Game.Audio.Radio`, `Game.Prefabs.Water`, `Game.Rendering.Legacy`                                                                                                                             |        2 |  ·  |                                                                                                                                                                                                                                                                                       |
| `Properties`, `Unity.Mathematics`, `Unity.Entities.CodeGeneratedRegistry`, `System.Runtime.CompilerServices`, `Game.PSI.Internal`, `Game.CinematicCamera`, `Colossal.Atmosphere.Internal`, `Game.Rendering.Climate` |        1 |  ·  | Codegen/attribute stubs.                                                                                                                                                                                                                                                              |
| `src/Game/*.cs` (root, no namespace dir)                                                                                                                                                                            |       10 |  ·  | `__JobReflectionRegistrationOutput__17016606566994089001.cs`, `-BurstDirectCallInitializer.cs`, `UnitySourceGeneratedAssemblyMonoScriptTypes_v1.cs` + a handful of orphans (`ShowIfAttribute`, `DayNightCycleData`, `GameModeSettingData`, `DebugCamera`). **Pure codegen — ignore.** |

**Biggest:** `Game.Prefabs` (1274). **Most mod-relevant, in order:** `Game` (root, 27 files), `Game.Modding` (3), `Game.Common` (49), `Game.Simulation` (479), `Game.Prefabs` (1274), `Game.UI` + `Game.UI.InGame`, `Game.Settings`.

---

## 3. The modding API proper

### 3.1 `IMod` — the entire contract

`src/Game/Game.Modding/IMod.cs:3`

```csharp
public interface IMod
{
    void OnLoad(UpdateSystem updateSystem);
    void OnDispose();
}
```

That's it. `OnLoad` receives the `UpdateSystem`, which is the _only_ sanctioned injection point.

### 3.2 `SystemUpdatePhase` — every phase, verbatim

`src/Game/Game/SystemUpdatePhase.cs:3-38`

```csharp
public enum SystemUpdatePhase
{
	Invalid = -1,
	MainLoop,
	LateUpdate,
	Modification1,
	Modification2,
	Modification2B,
	Modification3,
	Modification4,
	Modification4B,
	Modification5,
	ModificationEnd,
	PreSimulation,
	PostSimulation,
	GameSimulation,
	EditorSimulation,
	Rendering,
	PreTool,
	PostTool,
	ToolUpdate,
	ClearTool,
	ApplyTool,
	Serialize,
	Deserialize,
	UIUpdate,
	UITooltip,
	PrefabUpdate,
	DebugGizmos,
	LoadSimulation,
	PreCulling,
	CompleteRendering,
	Raycast,
	PrefabReferences,
	Cleanup
}
```

33 phases (`Invalid` + 32). Each `Modification<N>` phase has a matching `ModificationBarrier<N>` `EntityCommandBufferSystem` in `Game.Common` — `src/Game/Game.Common/SystemOrder.cs:79-94` shows the `AllowBarrier<ModificationBarrierN>` before / `ModificationBarrierN` after pairing.

### 3.3 `UpdateSystem` — the injection API

`src/Game/Game/UpdateSystem.cs:13` (`public class UpdateSystem : GameSystemBase`)

| Method                                                         | Line                          |
| -------------------------------------------------------------- | ----------------------------- |
| `RegisterGPUSystem<SystemType>()`                              | `UpdateSystem.cs:128`         |
| `RegisterGPUSystem(IGPUSystem)`                                | `UpdateSystem.cs:133`         |
| `UpdateAt<SystemType>(SystemUpdatePhase)`                      | `UpdateSystem.cs:141`         |
| `UpdateBefore<SystemType>(SystemUpdatePhase)`                  | `UpdateSystem.cs:146`         |
| `UpdateAfter<SystemType>(SystemUpdatePhase)`                   | `UpdateSystem.cs:151`         |
| `UpdateBefore<SystemType, OtherType>(SystemUpdatePhase)`       | `UpdateSystem.cs:156`         |
| `UpdateAfter<SystemType, OtherType>(SystemUpdatePhase)`        | `UpdateSystem.cs:161`         |
| `Update(phase)` / `Update(phase, updateIndex, iterationIndex)` | `UpdateSystem.cs:166`, `:206` |
| `currentPhase` property                                        | `UpdateSystem.cs:73`          |

Ordering mechanics worth teaching: `UpdateBefore` registers with `addIndex - 1000000`, `UpdateAfter` with `addIndex + 1000000`, `UpdateAt` with plain `addIndex` (`UpdateSystem.cs:143-153`). Sorting is `(phase, addIndex)` (`UpdateSystem.cs:29-37`). So _within a phase_, all `UpdateBefore`-registered systems run first (in registration order), then `UpdateAt`, then `UpdateAfter`. The two-type overloads register relative to another system via `m_RefMap` (`UpdateSystem.cs:261`).

Also: update intervals must be powers of two — `UpdateSystem.cs:286` throws `"System update interval not power of 2"`. Intervals come from `GameSystemBase.GetUpdateInterval(phase)` / `GetUpdateOffset(phase)` (`src/Game/Game/GameSystemBase.cs:131`, `:136`).

Exceptions thrown by a system's `OnUpdate` are caught per-system and logged, not propagated (`UpdateSystem.cs:188-197`) — a broken mod system spams the log every frame rather than crashing.

### 3.4 The game's own registration table

`src/Game/Game.Common/SystemOrder.cs:42` — `public static class SystemOrder`, `Initialize(UpdateSystem)` at `:44`, **1012 `UpdateAt/Before/After<T>` calls** in 1060 lines. Called from `src/Game/Game.SceneFlow/GameManager.cs:2380`.

This file is the single best navigation artifact in the whole decompile: it is a complete, ordered index of every game system and the phase it runs in. An agent asked "when does X run?" should grep `SystemOrder.cs` first.

### 3.5 Mod loading — `Game.SceneFlow`

`src/Game/Game.SceneFlow/GameManager.cs` (2425 lines):

- `GameManager.instance` — `:258`
- `modManager` property — `:276`
- `userInterface` property — `:300`
- lifecycle events `onGamePreload` / `onGameLoadingComplete` / `onWorldReady` — `:306`, `:308`, `:310`
- `m_ModManager = new ModManager(configuration.disableCodeModding)` — `:605`
- `InitializeModManager()` → `m_ModManager.Initialize(m_UpdateSystem)` — `:664`, `:668`
- `m_UpdateSystem = m_World.GetOrCreateSystemManaged<UpdateSystem>()` — `:2371`
- `SystemOrder.Initialize(m_UpdateSystem)` — `:2380`
- Main loop pumps: `Update(SystemUpdatePhase.MainLoop)` `:2390`, `Cleanup` `:2398`, `LateUpdate` `:2406`
- Hot-reload path: `m_ModManager?.Initialize(m_UpdateSystem, reinitialize: true)` — `:1628`

`src/Game/Game.Modding/ModManager.cs`:

- `public class ModManager : IEnumerable<ModManager.ModInfo>, IEnumerable, IDisposable` — `:28`
- `ModInfo` nested class — `:30`; `ModInfo.State` enum (`Unknown, Loaded, Disposed, IsNotModWarning, IsNotUniqueWarning, GeneralError, MissedDependenciesError, LoadAssemblyError, LoadAssemblyReferenceError`) — `:32-43`
- `ModInfo.Load(UpdateSystem)` — `:91`; instantiates every `IMod` type via `FormatterServices.GetUninitializedObject(item)` (`:121`) — **note: your `IMod` constructor is never called**
- `AfterLoadAssembly` → `TypeManager.InitializeAdditionalTypes(assembly)` + `SerializerSystem.SetDirty()` — `:146-150` (this is how custom `IComponentData` in a mod gets registered with DOTS)
- `ModManager.AreModsEnabled()` / `GetModsEnabled()` / `ListModsEnabled()` — `:206`, `:216`, `:222`
- `Initialize(UpdateSystem, bool reinitialize = false)` — `:242`
- `RequireRestart()` — `:524`
- `TryGetExecutableAsset(IMod, out ExecutableAsset)` — `:547`; `TryGetExecutableAsset(Assembly, out ExecutableAsset)` — `:564` (this is how mods find their own install directory)

### 3.6 `ModSetting` and the settings attributes

`src/Game/Game.Modding/ModSetting.cs:13` — `public abstract class ModSetting : Setting`

- ctor `ModSetting(IMod mod)` — `:36`; `id` is computed as `assemblyName + "." + namespace + "." + typeName` (`:39`)
- `RegisterInOptionsUI()` — `:46`; `UnregisterInOptionsUI()` — `:51`
- `builtIn => false` — `:21`
- Key-binding properties are auto-discovered by scanning for `ProxyBinding`-typed read/write properties (`:32-34`) and initialised in `InitializeKeyBindings()` (`:56`)

Base class `src/Game/Game.Settings/Setting.cs`: `ApplyAndSave()` `:151`, `Apply()` `:157`, `abstract SetDefaults()` `:163`, `RegisterInOptionsUI` `:174`/`:179`.

The attributes live in `src/Game/Game.Settings/` — one file each. Full list as found on disk:
`SettingsUIAdvancedAttribute`, `SettingsUIBindingMimicAttribute`, `SettingsUIButtonAttribute`, `SettingsUIButtonGroupAttribute`, `SettingsUIConfirmationAttribute`, `SettingsUICustomFormatAttribute`, `SettingsUIDescriptionAttribute`, `SettingsUIDeveloperAttribute`, `SettingsUIDirectoryPickerAttribute`, `SettingsUIDisableByConditionAttribute`, `SettingsUIDisplayNameAttribute`, `SettingsUIDropdownAttribute`, `SettingsUIForceSaveAttribute`, `SettingsUIGamepadActionAttribute`, `SettingsUIGamepadBindingAttribute`, `SettingsUIGroupOrderAttribute`, `SettingsUIHiddenAttribute`, `SettingsUIHideByConditionAttribute`, `SettingsUIInputActionAttribute`, `SettingsUIKeybindingAttribute`, `SettingsUIKeyboardActionAttribute`, `SettingsUIKeyboardBindingAttribute`, `SettingsUIMouseActionAttribute`, `SettingsUIMouseBindingAttribute`, `SettingsUIMultilineTextAttribute`, `SettingsUIPageWarningAttribute`, `SettingsUIPathAttribute`, `SettingsUIPlatformAttribute`, `SettingsUISearchHiddenAttribute`, `SettingsUISectionAttribute`, `SettingsUISetterAttribute`, `SettingsUIShowGroupNameAttribute`, `SettingsUISliderAttribute`, `SettingsUITabOrderAttribute`, `SettingsUITabWarningAttribute`, `SettingsUITextInputAttribute`, `SettingsUIValueVersionAttribute`, `SettingsUIWarningAttribute` (38), plus `IgnoreEqualsAttribute` and `ModdingToolchainUIButtonAttribute`.

The renderer is `src/Game/Game.UI.Menu/AutomaticSettings.cs:21` (`public static class AutomaticSettings`), which reflects over the `Setting` and emits `IWidget`s: `AddBoolToggleProperty` `:1193`, `AddIntDropdownProperty` `:1246`, `AddIntSliderProperty` `:1274`, `AddFloatSliderProperty` `:1315`, `AddStringTextInputProperty` `:1354`, `AddStringDropdownProperty` `:1376`, `AddLocalizedStringFieldProperty` `:1423`, etc. Host system: `src/Game/Game.UI.Menu/OptionsUISystem.cs:28` with nested `Page` (`:31`) / `Section` (`:115`) / `Group` model.

### 3.7 `Colossal.IO.AssetDatabase` — mod/asset entry points

`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/AssetDatabase.cs`:

- `public class AssetDatabase : IAssetDatabase, IDisposable` — `:24`
- `static bool exists` — `:177`
- `static AssetDatabase global` — `:179`
- `static ILocalAssetDatabase game` — `:181`
- `static ILocalAssetDatabase user` — `:183`
- `static ILocalAssetDatabase packages` — `:185`
- `static ILocalAssetDatabase GetTransient(long maxChunkSize = 0, string rootPath = null)` — `:301`
- `public class AssetDatabase<T> : IAssetDatabaseInternal, ILocalAssetDatabase, …` — `:1135`; `static instance` `:1171`, `GetInstance(T descriptor)` `:1246`

`ExecutableAsset.cs` — the mod DLL wrapper:

- `public class ExecutableAsset : AssetData` — `:13`
- `isBursted` `:135`, `isILAssembly` `:137`, `isLoaded` `:151`, `isLocal` `:153`, `isMod` `:155`, `isReference` `:161`, `isRequired` `:163`, `isUnique` `:177`
- `LoadAssembly(Action<Assembly> afterLoadAction, out ExecutableAsset uniqueAsset)` — `:213`
- **`isMod` is determined by Cecil-scanning the assembly's top-level types for a direct `IMod` interface implementation** — `:381-392`. Practical consequence: your `IMod` implementor must be a top-level (non-nested) type and must implement `IMod` _directly_, not inherit it from a base class.

Other asset types a mod cares about (all in the same directory, one file each): `PrefabAsset`, `LocaleAsset`, `UIModuleAsset`, `UIHostAsset`, `ImageAsset`/`TextureAsset`/`AtlasAsset`, `SettingAsset`, `PackageAsset`, `SaveGameData`, `Identifier`, `SearchFilter`, `Metadata`/`SourceMeta`, `ParadoxModsDataSource`, `FileSystemDataSource`, `SteamCloudDataSource`, `IDataSourceProvider`, `IAssetFactory`.

### 3.8 UI binding surface — `Colossal.UI.Binding` (complete enumeration)

All under `src/Colossal.UI.Binding/Colossal.UI.Binding/`.

**Interfaces**

| Type                               | path:line                               |
| ---------------------------------- | --------------------------------------- |
| `IBinding`                         | `IBinding.cs:5`                         |
| `IUpdateBinding : IBinding`        | `IUpdateBinding.cs:3`                   |
| `IBindingGroup`                    | `IBindingGroup.cs:5`                    |
| `IBindingRegistry : IBindingGroup` | `IBindingRegistry.cs:3`                 |
| `IDebugBinding`                    | `IDebugBinding.cs`                      |
| `IJsonReader` / `IJsonWriter`      | `IJsonReader.cs` / `IJsonWriter.cs`     |
| `IJsonReadable` / `IJsonWritable`  | `IJsonReadable.cs` / `IJsonWritable.cs` |
| `IReader` / `IWriter`              | `IReader.cs` / `IWriter.cs`             |

**Abstract bases**

| Type                                                        | path:line                    |
| ----------------------------------------------------------- | ---------------------------- |
| `BindingBase : IBinding, IDebugBinding`                     | `BindingBase.cs:7`           |
| `EventBindingBase : BindingBase`                            | `EventBindingBase.cs:6`      |
| `RawEventBindingBase : EventBindingBase`                    | `RawEventBindingBase.cs:6`   |
| `RawTriggerBindingBase : BindingBase`                       | `RawTriggerBindingBase.cs:6` |
| `RawCallBindingBase<TResult> : BindingBase`                 | `RawCallBindingBase.cs:6`    |
| `MapBindingBase<K> : BindingBase, IUpdateBinding, IBinding` | `MapBindingBase.cs:7`        |

**Concrete bindings** (C# → JS state)

| Type                                                                           | path:line                 |
| ------------------------------------------------------------------------------ | ------------------------- |
| `ValueBinding<T> : RawEventBindingBase`                                        | `ValueBinding.cs:6`       |
| `GetterValueBinding<T> : RawEventBindingBase, IUpdateBinding`                  | `GetterValueBinding.cs:6` |
| `RawValueBinding : RawEventBindingBase, IUpdateBinding`                        | `RawValueBinding.cs:7`    |
| `GetterMapBinding<K,V> : MapBindingBase<K>`                                    | `GetterMapBinding.cs:6`   |
| `RawMapBinding<K> : MapBindingBase<K>`                                         | `RawMapBinding.cs:5`      |
| `StackBinding<T> : IBinding, IBindingGroup`                                    | `StackBinding.cs:8`       |
| `CompositeBinding : IUpdateBinding, IBinding, IBindingRegistry, IBindingGroup` | `CompositeBinding.cs:9`   |

**Concrete bindings** (JS → C# invocation)

| Type                                        | path:line                                |
| ------------------------------------------- | ---------------------------------------- |
| `TriggerBinding : BindingBase`              | `TriggerBinding.cs:7`                    |
| `TriggerBinding<T>` … `<T1,T2,T3,T4>`       | `TriggerBinding.cs:45, 71, 101, 135`     |
| `RawTriggerBinding : RawTriggerBindingBase` | `RawTriggerBinding.cs:6`                 |
| `CallBinding<TResult>` … `<T1..T5,TResult>` | `CallBinding.cs:6, 29, 56, 87, 122, 161` |
| `EventBinding : EventBindingBase`           | `EventBinding.cs:3`                      |
| `EventBinding<T> : RawEventBindingBase`     | `EventBinding.cs:15`                     |
| `RawEventBinding : RawEventBindingBase`     | `RawEventBinding.cs:3`                   |

**Serialization helpers** (readers/writers registry, ~30 files): `JsonReader`/`JsonWriter`, `ValueReaders`/`ValueWriters`/`ValueWritersStruct`, `ArrayReader`/`ArrayWriter`, `ListReader`/`ListWriter`, `DictionaryReader`/`DictionaryWriter`, `CollectionWriter`, `EnumReader`/`EnumWriter`/`EnumNameWriter`, `NullableReader`/`NullableWriter`/`NullableStructWriter`, `StringReader`/`StringWriter`, `LongReader`/`LongWriter`, `ULongReader`/`ULongWriter`, `DelegateReader`/`DelegateWriter`, `ReaderDelegate`/`WriterDelegate`, `MathematicsReaders`/`MathematicsWriters`, `UnityReaders`/`UnityWriters`, `JsonWriterExtensions`, `RawValueBindingExtensions`, `DebugBindingWriter`/`DebugBindingType`.

**Registration side** — `src/Game/Game.UI/UISystemBase.cs:11`:

```csharp
protected void AddBinding(IBinding binding)          // :48  → GameManager.instance.userInterface.bindings.AddBinding
protected void AddUpdateBinding(IUpdateBinding b)    // :54  → also pumped from OnUpdate (:40-46)
public virtual GameMode gameMode => GameMode.All;    // :19  → gates Enabled via OnGamePreload (:60)
```

Bindings are removed in `OnDestroy` (`:29-37`). 57 classes in `Game` derive from `UISystemBase`; the canonical mod pattern is to write your own and register it with `UpdateSystem.UpdateAt<MyUISystem>(SystemUpdatePhase.UIUpdate)`.

---

## 4. ECS shape

### 4.1 System hierarchy

```
Unity.Entities.SystemBase
 └─ Colossal.Entities.COSystemBase                 src/Colossal.Core/Colossal.Entities/COSystemBase.cs:7
     └─ Game.GameSystemBase                        src/Game/Game/GameSystemBase.cs:13
         ├─ Game.UI.UISystemBase                   src/Game/Game.UI/UISystemBase.cs:11        (57 subclasses)
         │   ├─ InfoviewUISystemBase                                                          (24)
         │   └─ EditorPanelSystemBase                                                         (14)
         ├─ Game.Tools.ToolBaseSystem              src/Game/Game.Tools/ToolBaseSystem.cs:28   (10 direct + ObjectToolBaseSystem→2)
         ├─ Game.UI.Tooltip.TooltipSystemBase                                                 (24)
         ├─ Game.Debug.BaseDebugSystem                                                        (29)
         ├─ Game.Common.SafeCommandBufferSystem    src/Game/Game/SafeCommandBufferSystem.cs   (13, incl. ModificationBarrier1..5, EndFrameBarrier)
         ├─ Game.Simulation.CellMapSystem<T>                                                  (13 closed generics)
         ├─ Game.Tutorials.TutorialTriggerSystemBase / TutorialDeactivationSystemBase         (8 / 4)
         └─ Game.UpdateSystem                      src/Game/Game/UpdateSystem.cs:13
```

Counts in `src/Game/`:

- **726 classes declare `: GameSystemBase` directly** (740 including the ones only matched by the narrower pattern).
- **929 files named `*System.cs`** (the naming convention is essentially universal).
- **1012 registrations in `SystemOrder.Initialize`** (some systems are registered in multiple phases, e.g. `DebugWatchSystem` in three — `SystemOrder.cs:71-73`).

Realistic answer: **~800–950 distinct game systems**, of which ~300 are in `Game.Simulation`, 67 in `Game.UI.InGame`, 63 in `Game.Rendering`, 62 in `Game.Serialization`, 53 in `Game.Tools`, 34 in `Game.Prefabs`.

`GameSystemBase` adds five lifecycle hooks over `SystemBase` that a mod system will override:

```
OnWorldReady()                                  GameSystemBase.cs:111
OnGamePreload(Purpose, GameMode)                GameSystemBase.cs:115
OnGameLoaded(Context serializationContext)      GameSystemBase.cs:119
OnGameLoadingComplete(Purpose, GameMode)        GameSystemBase.cs:123
OnFocusChanged(bool)                            GameSystemBase.cs:127
GetUpdateInterval(SystemUpdatePhase) → 1        GameSystemBase.cs:131
GetUpdateOffset(SystemUpdatePhase) → -1         GameSystemBase.cs:136
```

All of these are exception-wrapped and will **silently disable your system** (`base.Enabled = false`) on throw — `GameSystemBase.cs:81, 94, 107`. That is a critical debugging gotcha to teach.

### 4.2 Component data

In `src/Game/`:

- **991 `IComponentData` struct declarations** across 1008 files
- **148 `IBufferElementData`** structs
- **2 `ISharedComponentData`** (notably `UpdateFrame` in `Game.Simulation`)
- **1383 `IJob*` structs** — of which **583 files contain `IJobChunk`**. There is **zero `IJobEntity`** in the entire `Game` assembly; the codebase predates / avoids it and uses `IJobChunk` + explicit `TypeHandle`s exclusively.
- 758 files carry `[BurstCompile]`

Clustering of `IComponentData` by namespace:

| Namespace                        | files with `IComponentData` |
| -------------------------------- | --------------------------: |
| `Game.Prefabs`                   |                         390 |
| `Game.Buildings`                 |                          80 |
| `Game.Net`                       |                          58 |
| `Game.Tutorials`                 |                          32 |
| `Game.Objects`                   |                          31 |
| `Game.Citizens`                  |                          29 |
| `Game.Vehicles`                  |                          28 |
| `Game.Events`                    |                          28 |
| `Game.Routes`                    |                          27 |
| `Game.Tools`                     |                          23 |
| `Game.Companies`                 |                          22 |
| `Game.Simulation`                |                          21 |
| `Game.Areas`                     |                          15 |
| `Game.Creatures` / `Game.Common` |                     14 each |
| `Game.City`                      |                          11 |
| rest                             |                     ≤5 each |

The dominance of `Game.Prefabs` (390/991) reflects the two-tier design: for most gameplay concepts there is a _prefab-side_ `IComponentData` (the template, e.g. `BuildingData`) and an _instance-side_ one (`Game.Buildings.Building`). Understanding that split is the single most important conceptual thing for a prefab-modifying mod.

### 4.3 The prefab/authoring layer

```
UnityEngine.ScriptableObject
 └─ Game.Prefabs.ComponentBase : IComponentBase, IComparable   Game.Prefabs/ComponentBase.cs:13   (280 subclasses)
     └─ Game.Prefabs.PrefabBase : ISerializationCallbackReceiver, IPrefabBase
                                                               Game.Prefabs/PrefabBase.cs:16      (112 subclasses)
```

`ComponentBase` extension points a mod overrides (`ComponentBase.cs`):

```csharp
public virtual IEnumerable<string> modTags { get; }                          // :23
public virtual void GetDependencies(List<PrefabBase> prefabs)                // :73
public virtual void Initialize(EntityManager, Entity)                        // :79
public virtual void LateInitialize(EntityManager, Entity)                    // :83
public abstract void GetPrefabComponents(HashSet<ComponentType>)             // :85
public abstract void GetArchetypeComponents(HashSet<ComponentType>)          // :87
```

`GetPrefabComponents` declares what goes on the _prefab entity_; `GetArchetypeComponents` declares what goes on _spawned instances_. This is the hook for adding custom components to existing assets.

`PrefabSystem` (`src/Game/Game.Prefabs/PrefabSystem.cs`, 996 lines) is the runtime registry:
`AddPrefab` `:88`, `RemovePrefab` `:191`, `DuplicatePrefab` `:269`, `AddOrUpdatePrefab` `:294`, `UpdatePrefab` `:306`, `GetPrefab<T>(PrefabData|Entity|PrefabRef)` `:643/648/653`, `TryGetPrefab<T>` `:658/669/679`, `GetEntity(PrefabBase)` `:705`, `TryGetEntity` `:710`, `HasComponent<T>` `:715`, `GetComponentData<T>` `:725`, `AddComponentData<T>` `:745`, `RemoveComponent<T>` `:750`, `AddUnlockRequirement` `:601/620`, `GetOrCreateContentPrefab(string modId)` `:330`.

### 4.4 Canonical system shape

`src/Game/Game.Simulation/AgingSystem.cs` is the cleanest teaching example (296 lines, one job). Structure that repeats ~700 times:

1. `[CompilerGenerated] public class XSystem : GameSystemBase` (`:18-19`)
2. Nested `[BurstCompile] private struct XJob : IJobChunk` with `[ReadOnly]`-annotated `BufferTypeHandle<>`/`ComponentTypeHandle<>`/`ComponentLookup<>` fields and an `EntityCommandBuffer.ParallelWriter` (`:22-57`)
3. `private struct TypeHandle` holding fields named `__Game_Citizens_Citizen_RW_ComponentLookup` etc. + `__AssignHandles(ref SystemState)` (`:147-166`)
4. `OnCreate`: `World.GetOrCreateSystemManaged<...>()` for dependencies, `GetEntityQuery(new EntityQueryDesc { All/None })`, `RequireForUpdate(query)` (`:206-229`)
5. Optional `GetUpdateInterval(phase)` override (`:201-204`)
6. `OnUpdate`: build the job struct via `InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__…, ref base.CheckedStateRef)`, `base.Dependency = JobChunkExtensions.ScheduleParallel(jobData, query, base.Dependency)`, then `barrier.AddJobHandleForProducer(base.Dependency)` (`:259-280`)
7. `__AssignQueries(ref SystemState)` and `OnCreateForCompiler()` (`:283-294`) — **pure codegen, ignore**
8. `[Preserve] public XSystem() { }` (`:296`)

Cross-system communication is by `Game.Common` tag components (`Created`, `Updated`, `Deleted`, `Applied`, `BatchesUpdated`, `PathfindUpdated`, `EffectsUpdated`) written through the phase-matched `ModificationBarrier<N>` command buffers.

---

## 5. Navigation ergonomics for an agent

### 5.1 What works

**Glob patterns.** The layout is `src/<Assembly>/<FullNamespace>/<TypeName>.cs`, exactly two levels, no nesting. So:

| Goal                                 | Pattern                                                                            |
| ------------------------------------ | ---------------------------------------------------------------------------------- |
| Find a type by name                  | `src/**/<TypeName>.cs` — works ~99% of the time, one file per type                 |
| All systems in a domain              | `src/Game/Game.Simulation/*System.cs`                                              |
| All components in a domain           | `src/Game/Game.Citizens/*.cs`                                                      |
| Everything in a namespace            | `src/Game/<Namespace>/` — the directory _is_ the namespace, verbatim, dots and all |
| Find which assembly owns a namespace | `find src -maxdepth 2 -type d -name '<Namespace>'`                                 |

**Grep patterns that work.**

| Question                           | Pattern                                                                                                     |
| ---------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| "when does system X run?"          | grep `X>` in `src/Game/Game.Common/SystemOrder.cs` — **always start here**                                  |
| "what systems run in phase P?"     | grep `SystemUpdatePhase.P` in `SystemOrder.cs`                                                              |
| "who reads component C?"           | `ComponentLookup<C>\|ComponentTypeHandle<C>\|ReadOnly<C>()`                                                 |
| "who writes component C?"          | `_RW_ComponentLookup` + `C` in the same file; or `SetComponentData<C>\|AddComponent<C>`                     |
| "which archetype has C?"           | grep `C` in `src/Game/Game.Prefabs/*.cs` for the `GetArchetypeComponents` that adds it                      |
| "how does the UI get value V?"     | grep `"V"` (the literal JS-side key string) across `src/Game/Game.UI*/` — binding names are string literals |
| find all subclasses of B           | `class [A-Za-z0-9_]+ : B\b`                                                                                 |
| find all bindings a system exposes | `AddBinding\|AddUpdateBinding` in that file                                                                 |

**File-per-type is near-perfect**: only 5 files in all of `src/Game/` (4388 files) declare more than one top-level type — `Game.Reflection/DelegateAccessor.cs`, `Game.Settings/QualitySetting.cs`, `Game.UI.Editor/DualPopupValueField.cs`, `Game.UI.Editor/HierarchyMenu.cs`, `Game.UI.Widgets/FloatSliderField.cs`. Nested types are always inside the parent's file.

**Naming conventions that hold**

- Systems: `*System.cs` (929 files) — plus the barrier exceptions (`EndFrameBarrier`, `ModificationBarrier1`, `AudioEndBarrier`, `AllowBarrier<T>`) which do _not_ end in `System`.
- Fields: `m_` prefix on instance, `s_` on static, `k` on const (`kUpdatesPerDay`).
- Jobs: nested `struct <Verb>Job : IJobChunk` inside the owning system.
- Prefab authoring components: `Game.Prefabs/<Name>.cs` where `<Name>` is the ScriptableObject name; the matching runtime data struct is usually `<Name>Data` in the same directory.
- Attributes are always `<Name>Attribute.cs`, one per file — so `grep -l "SettingsUI" src/Game/Game.Settings/` enumerates the whole settings vocabulary.

### 5.2 Where the layout is misleading

1. **Namespace ≠ assembly.** Several `Colossal.*` namespaces live _inside_ `src/Game/`: `src/Game/Colossal.Rendering/`, `src/Game/Colossal.Atmosphere/`, `src/Game/Colossal.Atmosphere.Internal/`. And several `Game.*` namespaces live _outside_ it: `src/Colossal.Core/Game.Threading/`, `src/Colossal.IO/Game.UI.Editor/NativeHelpers.cs`. `Colossal.Rendering` and `Colossal.IO` are each **split across two assemblies**. `Colossal/CORuntimeApplication.cs` is inside the patched **`Unity.Entities`** assembly. Never assume `Game.*` → `src/Game/`; always glob `src/**/<Namespace>/`.
2. **`Colossal.IO.AssetDatabase/Game.cs`** is a _type_ named `Game` (an asset-database descriptor struct), not the game assembly. Searching for "Game" is useless.
3. **The `Game` root namespace directory is `src/Game/Game/`** — 27 files, and it holds the four most important files in the corpus (`SystemUpdatePhase`, `UpdateSystem`, `GameSystemBase`, `Version`). Easy to skip past because it looks like a duplicate path segment.
4. **`SystemOrder` is in `Game.Common`, not `Game`.** The most valuable index file is not where you'd look.
5. **`Colossal.Core` is a grab-bag**: it contains `CliWrap` (a shell-exec library), `Mono.Options`, `Colossal.Json`, and `Game.Threading`, alongside the genuinely important `Colossal.Entities` and `Colossal.Serialization.Entities`.
6. **Duplicate type names.** Within `src/Game/` alone: 6× `SearchSystem.cs`, 6× `RaycastJobs.cs`, 6× `InitializeSystem.cs`, 5× `ReferencesSystem.cs`, 5× `ValidationHelpers.cs`, 4× `UpdateCollectSystem.cs`, 3× `Node.cs`/`Edge.cs`/`OutsideConnection.cs`/`GeometryFlags.cs`, and ~200 two-way collisions (e.g. `WaterTower.cs` in both `Game.Buildings` and `Game.Prefabs`). **Always qualify a glob with the namespace directory.** The game itself has to write `Game.Citizens.Student` and `Game.Events.InitializeSystem` fully-qualified in source (`Game.Simulation/AgingSystem.cs:43`, `Game.Common/SystemOrder.cs:110`) — that's your tell that a name is ambiguous.
7. **Generic arities collapse into one file.** `TriggerBinding<T1,T2,T3,T4>` is at `TriggerBinding.cs:135`; `CallBinding<T1,T2,T3,T4,T5,TResult>` at `CallBinding.cs:161`. Globbing `CallBinding*.cs` finds one file containing six types.

### 5.3 What is genuinely hard to find

- **Anything driven by string literals.** UI binding names (`"ModLoadingStatus"`, `"ModsLoading"`) and localization keys are strings with no type-level trace. You must grep the literal, and the JS side isn't in this corpus at all.
- **Reflection-driven behaviour.** `AutomaticSettings` builds the whole options UI by reflecting over attributes — there is no call graph from `[SettingsUISlider]` to the slider widget. Same for `ModSetting`'s keybinding discovery (property-type scanning, `ModSetting.cs:32`).
- **Who writes a component.** Because writes go through `EntityCommandBuffer.ParallelWriter` inside a Burst job, and the handle is a `TypeHandle` field with a mangled name, "find all writers of `Citizen`" requires grepping `__Game_Citizens_Citizen_RW_ComponentLookup` — the mangled name is actually _more_ greppable than the type. Learn the pattern: `__<Namespace_With_Underscores>_<Type>_<RO|RW>_<ComponentLookup|ComponentTypeHandle|BufferTypeHandle|SharedComponentTypeHandle>`.
- **Burst-compiled function pointers.** `-BurstDirectCallInitializer.cs` and `__JobReflectionRegistrationOutput__*.cs` reference mangled types like `Game_002ERendering_002EDequeueAndSort_00004B5A_0024BurstDirectCall` (`src/Game/Properties/AssemblyInfo.cs:14-18`) — `_002E` is `.`, `_0024` is `$`. These are dead ends; the real method is `Game.Rendering.DequeueAndSort`.
- **No `.js`/`.css`/HTML.** The Cohtml front-end is not decompiled. Only the C# binding side exists. Say so rather than searching.

### 5.4 Decompilation artifacts an agent must be warned about

The decompiler is ILSpy in C# 12 mode (file-scoped namespaces, primary constructors on structs — see `UpdateSystem.cs:15`, `:40`). Verified artifacts:

1. **`[CompilerGenerated]` on hand-written classes.** 877 files carry it. `AgingSystem.cs:18` has `[CompilerGenerated]` on `public class AgingSystem` — this is _not_ generated code; it's the DOTS source generator having rewritten the class. **Do not skip a file because it says `[CompilerGenerated]`.** This is the single most misleading artifact in the corpus.
2. **`__TypeHandle` / `__AssignHandles` / `__AssignQueries` / `OnCreateForCompiler`.** Present in nearly every system (`AgingSystem.cs:147, 200, 283, 289`). Pure DOTS codegen. `__AssignQueries` is frequently a no-op body: `new EntityQueryBuilder(Allocator.Temp).Dispose();` (`AgingSystem.cs:285`). Ignore all of it — but note that the _field names inside_ `TypeHandle` are the best index of what a system reads/writes.
3. **`InternalCompilerInterface.Get*` wrappers.** `InternalCompilerInterface.GetComponentLookup(ref __TypeHandle.__X, ref base.CheckedStateRef)` is the codegen'd form of what a mod author writes as `SystemAPI.GetComponentLookup<X>()` or `GetComponentLookup<X>()`. Mods should write the ordinary form.
4. **Events lowered to `Delegate.Combine`/`Delegate.Remove`.** `GameSystemBase.cs:25` reads `loadGameSystem.onOnSaveGameLoaded = (LoadGameSystem.EventGameLoaded)Delegate.Combine(loadGameSystem.onOnSaveGameLoaded, new LoadGameSystem.EventGameLoaded(GameLoaded));` — that is source-level `+=`. Appears wherever a field-like event is used.
5. **Meaningless local names.** `num`, `num2`, `flag`, `flag2`, `text2`, `list`, `value2`, `array`. Worse: locals named after their type with a numeric suffix — `int2 int5 = m_UpdateRanges[(int)phase];` (`UpdateSystem.cs:180`, `:220`). An agent must not infer meaning from local identifiers.
6. **Named arguments are partly reconstructed, partly not.** 754 files do contain `isReadOnly: true` (ILSpy restored these from the boolean-literal heuristic), but most other call sites show bare positional literals. Don't trust an absent argument name to mean anything.
7. **Deconstruction noise.** `var (_, modInfo2) = (KeyValuePair<Identifier, ModInfo>)(ref modsInfo);` (`ModManager.cs:266`) — a `foreach` over a dictionary re-rendered oddly.
8. **`[Preserve]` everywhere** (1020 files) — Unity IL2CPP link preservation, semantically irrelevant. It appears on `OnCreate`/`OnUpdate`/`OnDestroy`/constructors. Noise.
9. **Explicit no-arg constructors** appended to every system (`AgingSystem.cs:296`, `UpdateSystem.cs:503`). Not in original source.
10. **`goto` and label residue** in 41 files where loop/switch reconstruction failed.
11. **`unsafe` blocks and raw pointers** in 71 files (mostly native interop and `Colossal.Collections`).
12. **Codegen-only files with illegal-ish names**: `src/Game/-BurstDirectCallInitializer.cs` (12 copies across assemblies), `src/Game/__JobReflectionRegistrationOutput__17016606566994089001.cs`, `UnitySourceGeneratedAssemblyMonoScriptTypes_v1.cs` (67 copies), `Unity.Entities.CodeGeneratedRegistry/AssemblyTypeRegistry.cs` (10 copies). Exclude these from every search.
13. **Compiler-generated closure/iterator classes are largely absent.** I found **zero** occurrences of `<>c__DisplayClass`, `_003C`, `_003E`, or `<>c` in `src/Game/` — ILSpy successfully re-inlined lambdas into normal C# lambda syntax (see `ModManager.cs:112-114`, `:222-226`). Only 24 files retain a visible `MoveNext()`, i.e. an unrecovered iterator/async state machine. **This is better than a typical decompile** and is worth stating positively: lambdas and LINQ read normally.
14. **Generic type arguments are preserved**, including on the `CellMapSystem<T>` closed generics and `AllowBarrier<ModificationBarrier1>`. I found no evidence of lost generics.
15. **`[assembly: AssemblyVersion("0.0.0.0")]`** (`src/Game/Properties/AssemblyInfo.cs:20`) — the real version is in `VersionInternal`, see below. Don't read `AssemblyVersion`.

### 5.5 A suggested agent search order

1. Type name known → `Glob src/**/<Name>.cs`. If >1 hit, disambiguate by namespace dir.
2. "When/where does X run" → `Grep "X" src/Game/Game.Common/SystemOrder.cs`.
3. "What is phase P for" → `Grep "SystemUpdatePhase.P" src/Game/Game.Common/SystemOrder.cs`.
4. "What data does system X use" → read its nested `TypeHandle` struct and its `OnCreate` `EntityQueryDesc`.
5. "What components does prefab type P produce" → `Grep "GetArchetypeComponents" src/Game/Game.Prefabs/P*.cs`.
6. "How do I expose Y to the UI" → find an existing `UISystemBase` in `Game.UI.InGame` that does something similar; the binding types are all in `Colossal.UI.Binding`.
7. Always exclude: `src/mscorlib`, `src/System*`, `src/UnityEngine*`, `src/Unity.RenderPipelines*`, `src/Newtonsoft.Json`, `src/Colossal.Mono.Cecil`, `src/PDX.SDK`, `**/UnitySourceGeneratedAssemblyMonoScriptTypes_v1.cs`, `**/-BurstDirectCallInitializer.cs`, `**/__JobReflectionRegistrationOutput__*.cs`, `**/AssemblyTypeRegistry.cs`.

---

## 6. Version

**Cities: Skylines II `1.6.0f1`, build `6216.19404`, changelist `419.d6c6`.**

Primary evidence — `src/Game/Properties/AssemblyInfo.cs:19`:

```csharp
[assembly: VersionInternal("1.6.0f1 (419.d6c6) [6216.19404]")]
```

Corroborating:

- `src/Colossal.UI/Properties/AssemblyInfo.cs:7` — `VersionInternal("1.0.0f1 (419.d6c6) [6216.19385]")`
- `src/Colossal.Localization/Properties/AssemblyInfo.cs:6` — `VersionInternal("1.0.0a1 (419.d6c6) [6216.19385]")`
- `src/Colossal.Core/Properties/AssemblyInfo.cs:8` — `VersionInternal("1.0.0f1")` (no build stamp)
- Git history of the decompile repo: `ec7c3720 1.6.0f1` (HEAD), preceded by `8027f747 1.5.9f1` and `c3eeaa11 1.5.7f1`.

Save-format history is in `src/Game/Game/Version.cs` — 200+ `[VersionConstant("<game version> [<build>]")]` fields spanning `0.9.0a1 [3651.20586]` (`Version.cs:8`) through `1.5.7f1 [6157.21012]` (`Version.cs:821`), with `Version.cs:825` holding the bare `current`:

```csharp
[VersionConstant]
public static readonly Colossal.Version current = new Colossal.Version(1, 315255277153176524L, 27514566);
```

Note the last _named_ save-migration constant is `1.5.7f1`, i.e. `1.6.0f1` introduced no new save-format break — a useful fact for save-compatibility mods. There is no CHANGELOG file in the repo; `README.md` is a single line.

---

## Things I could not confirm

- **Exact distinct system count.** 726 direct `: GameSystemBase` declarations, 929 `*System.cs` files, 1012 `SystemOrder` registrations — these disagree because some systems register in multiple phases, some `*System.cs` files hold non-system types, and some systems derive from intermediate bases. I did not build a full type graph. "~800–950" is my honest range.
- **Whether all 33 `SystemUpdatePhase` values are actually driven.** I verified `MainLoop`, `LateUpdate`, `Cleanup` are pumped from `GameManager.cs:2390-2406` and that `SystemOrder.cs` registers into `Modification1..ModificationEnd`, but I did not trace the pump sites for every phase (e.g. `GameSimulation`, `Raycast`, `PrefabReferences`).
- **The JS/HTML side of the UI.** Not present in this corpus at all. Any claim about the front-end contract beyond the C# binding types would be a guess.
- **Whether `ModSetting` requires `RegisterInOptionsUI()` to be called manually.** The method exists (`ModSetting.cs:46`) and is not called from `ModManager`; I saw no auto-registration path, which implies the mod must call it — but I did not exhaustively search for an alternative registration site.
- **`docs/colossal.md` and `docs/cohtml.md`** — I read `docs/game.md` and `AGENTS.md` only; given `game.md` contained the incorrect `[UpdateAfter]` guidance echoed in `AGENTS.md:56`, I would not rely on the other two without verification.
