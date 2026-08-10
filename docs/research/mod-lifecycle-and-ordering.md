# Mod lifecycle, loading and system ordering

**Baseline.** Decompiled game version 1.6.0f1. Mod corpus (20 repositories under `C:\Users\Morgan\Documents\Projets\cs2-third-party-mods\`) read 2026-08-02. Wiki fetched live 2026-08-02 — the bot challenge did not win this time, so the `Systems` and `ECS - Entity Component System` pages are cited from the live pages rather than through `survey-wiki-inventory.md`'s snapshot.

**Re-swept 2026-08-03 (the new-sources resweep) against `docs/SOURCES.md`, which did not exist when this pass ran.** This topic makes no frontend claim, so the two sources the original pass could not have reached bear on it at exactly one seam: `AddUIModule`, where `@colossalorder/create-csii-ui-mod/template/types/modding.d.ts` declares the `ModuleRegistry` a loaded module talks to — `get`, `add`, `override`, `extend`, `append`, plus `hasAppend`, `find` and `reset`, and seven `AppendHookTargets`. That surface belongs to `frontend-and-injection`; what belongs here is the C# gate, amended at the deferral finding below. Nothing else in this file needed either source.

## Findings

### The contract is two methods, and nothing else

`Game.Modding.IMod` declares exactly `void OnLoad(UpdateSystem updateSystem)` and `void OnDispose()` (`src/Game/Game.Modding/IMod.cs:5/7`).
The whole file is eight lines.
There is no `OnCreateWorld`, no `OnGameLoad`, no initialisation callback of any other name.

**Verdict: the wiki's `OnCreateWorld()` does not exist on `IMod` at 1.6.0f1.**
The wiki's `Systems` page (https://cs2.paradoxwikis.com/Systems) says "When creating a system in your mod's `OnCreateWorld()` method, you must specify which phase your system will update at."
The decompile shows only `OnLoad`; the page states it was last updated 23 March 2025 for game version 1.0, so this is a stale API name rather than a second entry point.
The name was evidently real once: `CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:22` overrides a framework method still called `DoOnCreateWorld(UpdateSystem)`, and that framework wraps `IMod`.
A reader who writes `OnCreateWorld` gets a method the game never calls, with no error.

Rots: the member names on `IMod`, and the wiki page's currency — re-read `src/Game/Game.Modding/IMod.cs`.

### How the game finds a mod: two different tests, and the narrower one runs first

Detection and instantiation use different rules, and the gap between them is a trap.

**Detection.** `ExecutableAsset.ResolveModAssets(typeof(IMod), assets)` walks `assemblyDefinition.MainModule.Types` and, for each, `type.Interfaces`, setting `isMod = true` on the first type whose interface list contains a `FullName` equal to `Game.Modding.IMod` (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ExecutableAsset.cs:381-395`).
This is a Cecil metadata scan of **top-level types** for a **directly declared** interface.
A class that reaches `IMod` only through a base class in another assembly does not match, and neither does a nested one.

**Instantiation.** `ModInfo.Load` then does `asset.assembly.GetTypesDerivedFrom<IMod>()` (`src/Game/Game.Modding/ModManager.cs:119`), which is `assembly.GetTypes().Where(t => typeof(IMod).IsAssignableFrom(t))` (`src/Colossal.Core/Colossal.Reflection/ReflectionUtils.cs:508-513`) — reflection over every type, transitive, nested types included, and with no abstract-class filter.
So the loader instantiates **every** `IMod` implementation in the assembly, not one, and calls `OnLoad` on each in turn (`ModManager.cs:152-158`).

**Corpus corroboration, 20 of 20.** Every mod declares `IMod` explicitly in its base list, including the three that also inherit a framework base class: `CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:18` (`: BasicIMod, IMod`), `CS2-NetworkTools/NetworkTools.Mod/NetworkToolsMod.cs:32` (`: LucaModBase<NetworkToolsMod>, IMod`), `InfoLoom/InfoLoom/Mod.cs:29` (`: ModsCommonBase<InfoLoomMod>, IMod`).
That redundant redeclaration is exactly what the Cecil scan requires, and none of the three explains why it is there — the practice is universal without being documented.

Other loading gates on the same asset, all read before `Load` proceeds (`ModManager.cs:95-116`):
`asset.isRequired`; `isMod || isReference`; `isUnique`, which resolves same-named duplicate assemblies to one winner ordered by loaded, then local, then version descending (`ExecutableAsset.cs:177/181-191`); and `canBeLoaded`, which requires every assembly reference to have resolved (`:175`).
Failing each sets a distinct `ModInfo.State` and stops (`ModManager.cs:99-116`).
`GetModAssets` also warns and **skips** an asset whose file resolves to a game assembly the mod shipped alongside itself: `Assembly "{0}" is in-game assembly and it should NOT be shipped with mod "{1}"` (`ExecutableAsset.cs:325-329`).

Rots: the `ModInfo.State` member names — `Unknown`, `Loaded`, `Disposed`, `IsNotModWarning`, `IsNotUniqueWarning`, `GeneralError`, `MissedDependenciesError`, `LoadAssemblyError`, `LoadAssemblyReferenceError` (`ModManager.cs:32-43`); they are also the localisation keys the failure dialog interpolates (`:295`).

### The mod class is never constructed, but its systems are

`ModInfo.Load` builds each instance with `FormatterServices.GetUninitializedObject(item)` (`ModManager.cs:121`).
That allocates the object without running any constructor, which means **instance field initialisers never run either** — they are compiled into the constructor.
Static fields are unaffected: the static constructor still runs on first access.

The contrast with systems is exact and worth teaching side by side.
Unity constructs a system with `Activator.CreateInstance(systemType)` (`src/Unity.Entities/Unity.Entities/TypeManager.cs:2989-2997`), so a mod's own systems **do** get their parameterless constructor and their field initialisers, and a system without a parameterless constructor throws at creation.

**Corpus corroboration, 20 of 20.** No mod relies on an instance field initialiser on its `IMod` class. State lives in statics (`Anarchy/Anarchy/AnarchyMod.cs:50`, `FindIt-CSII/FindIt/Mod.cs:33`) or is assigned inside `OnLoad` (`PlopTheGrowables/Code/Mod.cs:49-52`).
The one instance initialiser in the corpus is `LineTool-CS2/Code/Mod.cs:30`, `private string s_assemblyPath = null` — which is harmless precisely because its value is the default.
No mod comments on why, so the convention is followed without being understood.

### Where mod loading sits in the boot sequence, and what that forecloses

The whole of `GameManager.Initialize()` is one async method (`src/Game/Game.SceneFlow/GameManager.cs:569-662`), and the relevant order is fixed by its statement order:

1. `CreateWorld()` (`:591`, body at `:2363-2374`) — `new World("Game")`, then `PrefabSystem`, `UpdateSystem`, `LoadGameSystem`, `SaveGameSystem` created by hand.
2. `new ModManager(configuration.disableCodeModding)` (`:605`).
3. `CreateSystems()` (`:615`, body at `:2376-2383`) — `SystemOrder.Initialize(m_UpdateSystem)` (`:2380`), which is where every vanilla system is created and registered.
4. `InitializeModManager()` (`:618`, body at `:664-670`) — `m_ModManager.Initialize(m_UpdateSystem)`, which registers, loads and calls `OnLoad` on every mod (`ModManager.cs:244-364`, `:431-459`).
5. `LoadPrefabs(AssetDatabase.global)` (`:624`).
6. `m_State = State.WorldReady` (`:626`), then `onWorldReady?.Invoke()` (`:629`).

Four consequences, each load-bearing:

- **The world already exists and every vanilla system is already registered when `OnLoad` runs.** A mod cannot change how the world is built, cannot get in front of `SystemOrder`, and cannot pre-empt a vanilla registration. It can only append.
- **Nothing has updated yet.** `shouldUpdateWorld => m_State >= State.WorldReady` (`:294`), and that state is only set at `:626`, after `OnLoad`. So no phase has run a single time when `OnLoad` executes: a mod may not read simulation state, query a populated world, or assume any vanilla system has done its `OnUpdate` work.
- **Prefabs are not loaded yet.** `LoadPrefabs` is step 5. Anything that needs the prefab database has to wait — see the deferral section.
- **`onWorldReady` has not fired yet**, so a system created during `OnLoad` will receive `OnWorldReady`; `GameSystemBase.OnCreate` subscribes to it (`src/Game/Game/GameSystemBase.cs:27`), and a system created after step 6 misses it permanently.

`shouldUpdateWorld` also explains why the world keeps ticking during a load: `State` is declared `Booting, Terminated, UIReady, BootingPrefabs, WorldDisposed, WorldReady, Quitting, Loading` (`:122-132`), so `Loading` (7) satisfies `>= WorldReady` (5).
The enum's declaration order is load-bearing for that comparison and is not chronological.

Rots: the `GameManager.State` member names and their declaration order — re-read `src/Game/Game.SceneFlow/GameManager.cs:122-132`.

The re-initialisation pass only ever adds a mod, never reloads one (corrected 2026-08-04 by the patching orchestrator pass, which found the earlier reading contradicted by `patching`'s unpatch-lifecycle finding).
`m_ModManager?.Initialize(m_UpdateSystem, reinitialize: true)` (`:1628`) runs on a playset or mod-status change and re-runs `RegisterMods()` then `InitializeMods()` (`ModManager.cs:263-264`).
`RegisterMods` keeps an existing entry's state through `ModInfo.TransferState` and gives only a genuinely new asset a fresh `ModInfo` (`ModManager.cs:397-428`, the transfer at `:414`), and `ModInfo.Load` returns immediately unless `state == State.Unknown` (`:95-98`).
So an already-loaded mod is skipped rather than disposed and re-loaded, and `OnLoad` runs exactly once per mod per process.
Disabling a loaded IL assembly mid-session cannot unload it: that branch calls `RequireRestart()` (`GameManager.cs:1572`, the notification at `ModManager.cs:524-545`), while `!isLoaded && isInActivePlayset` queues the assembly for a first load (`GameManager.cs:1576`).

### Ordering is imperative, and the stock ECS attributes are inert here

**Verdict: `[UpdateAfter]`, `[UpdateBefore]` and `[UpdateInGroup]` do nothing in this game.**
This is the first of the two open conflict entries (`conflicts.md:28-40`), whose claim originates in hand-written orientation prose at `DecompiledCitiesSkylines2/AGENTS.md:56`.
Re-verified independently, and the evidence is stronger than "the attributes are absent":

- **The grep.** `Grep` for `\[UpdateAfter|\[UpdateBefore|\[UpdateInGroup` over `C:\Users\Morgan\Documents\Projets\DecompiledCitiesSkylines2\src\Game` returns **0 matches across 0 files**. Widening to all of `src/` returns 26 files, every one of them inside the Unity packages themselves (`src/Unity.Entities/`, `src/Unity.Scenes/`, `src/Unity.Transforms/`, `src/Unity.Entities.Hybrid/`) — the stock system groups and their own members. Not one is a game type.
- **There is no consumer.** The attributes are read by `ComponentSystemSorter`, which sorts the members of a `ComponentSystemGroup`. The game never calls `DefaultWorldInitialization.Initialize`; the only reference to that class anywhere under `src/Game/` is `DefaultWorldInitialization.CleanupEntityComponentStore()` at teardown (`GameManager.cs:2416`). The world is `new World("Game")` (`:2368`) with four systems added by hand. No stock system group is created, so nothing exists that would read an attribute even if one were present.
- **The game demonstrates the override in its own code.** `Unity.Entities.UpdateWorldTimeSystem` carries `[UpdateInGroup(typeof(InitializationSystemGroup))]` (`src/Unity.Entities.Hybrid/Unity.Entities/UpdateWorldTimeSystem.cs:10`), and the game ignores it, registering that exact system imperatively with `updateSystem.UpdateBefore<UpdateWorldTimeSystem>(SystemUpdatePhase.MainLoop)` (`src/Game/Game.Common/SystemOrder.cs:47`). A stock-ECS system with a group attribute, driven from a phase instead. That single line is the clearest available proof.
- **`SystemOrder.cs` is the only vanilla registrar.** A count of `\.UpdateAt<|\.UpdateBefore<|\.UpdateAfter<|RegisterGPUSystem` over `src/Game/` returns 1013 occurrences in `Game.Common/SystemOrder.cs` and 3 in `Game/UpdateSystem.cs` (its own internal call sites), and nothing anywhere else. There is no second ordering mechanism to have missed.

**Two of the twenty corpus mods ship these attributes anyway, and in one of them the attribute states the opposite of what happens.**

- `Time2Work/NightShift/Systems/CitizenScheduleSystem.cs:36` declares `[UpdateAfter(typeof(WeekSystem))]`. Its registrations are `updateSystem.UpdateAt<CitizenScheduleSystem>(SystemUpdatePhase.GameSimulation)` (`Time2Work/NightShift/Mod.cs:145`) against `updateSystem.UpdateAfter<WeekSystem>(SystemUpdatePhase.GameSimulation)` (`:136`). Because the `UpdateAt` band sorts ahead of the `UpdateAfter` band (below), `CitizenScheduleSystem` actually runs **before** `WeekSystem` — the exact inverse of its own declaration. `TruckScheduleSystem.cs:39` carries the same attribute and is never registered with `UpdateSystem` at all.
- `Time2Work/NightShift/Systems/CitizenScheduleSection.cs:31` carries `[UpdateInGroup(typeof(InitializationSystemGroup))]` and is only ever brought into existence by `GetOrCreateSystemManaged` (`Mod.cs:160`). It is not inert-but-harmless there — it is an info section driven by another mechanism entirely (below).
- `CS2-WriteEverywhere/BelzontWE/Templates/WEPrefabLayoutSystem.cs:14` and `WETemplateUpdateSystem.cs:21` carry `[UpdateAfter]`. Here the imperative registration happens to agree with the attribute (`WriteEverywhereCS2Mod.cs:77/78/80`), so the attribute is redundant decoration rather than a lie. The mod's own ASCII dependency graph in the same file draws arrows labelled `[UpdateAfter]` (`:46/48`) that are in fact realised by registration order.

So the exposure is not confined to one checkout's prose: a shipped mod in this corpus carries an ordering attribute whose declared relation is inverted by the mechanism that actually runs.

**Ruled (2026-08-02, the mod-lifecycle-and-ordering pass; `conflicts.md`).** The reference states this, as a plain negative fact about the game and not as a correction of any document: the attributes exist, compile, and do nothing here.
Rest it on the no-consumer proof — the game imperatively registers a stock system carrying `[UpdateInGroup]` and never creates a system group — rather than on the absence of the attributes from game code, because the reader who needs the warning is arriving from stock ECS having read nothing, and "there is nothing here that reads them" is what they can act on.
Name no source, no document and no mod.

Rots: nothing here. This is architecture — the absence of a system-group world is not a name that moves.

### The three registration bands, and how order resolves inside one phase

`UpdateSystem` exposes five registration methods (`src/Game/Game/UpdateSystem.cs:141-164`):

| Method | `addIndex` assigned | Effect |
| --- | --- | --- |
| `UpdateBefore<T>(phase)` | `++m_AddIndex - 1000000` | front band of `phase` |
| `UpdateAt<T>(phase)` | `++m_AddIndex` | middle band of `phase` |
| `UpdateAfter<T>(phase)` | `++m_AddIndex + 1000000` | back band of `phase` |
| `UpdateBefore<T, Other>(phase)` | `++m_AddIndex - 1000000` | spliced immediately before `Other` |
| `UpdateAfter<T, Other>(phase)` | `++m_AddIndex + 1000000` | spliced immediately after `Other` |

The single-type forms append to `m_Systems`; the two-type forms instead append to `m_RefMap[Other]` (`:254-275`).
`Refresh()` sorts `m_Systems` by `(phase, addIndex)` (`:29-37`, `:299`) and walks each phase's run building `m_Updates`, splicing in the `m_RefMap` entries recursively as it goes (`:292-363`, `:365-438`); `Update(phase)` then walks one contiguous range of `m_Updates` and returns (`:172-183`).

Three rules follow, and they are the ones a reader needs:

1. **The bands never interleave.** The ±1,000,000 offsets are far larger than the registration counter will ever reach — vanilla makes 1013 registrations total — so every `UpdateBefore` in a phase runs before every `UpdateAt`, and every `UpdateAt` before every `UpdateAfter`. Nothing runs "before the phase" or "after the phase" despite the names; all three land inside it. The wiki says otherwise ("`UpdateBefore<T>()` - registers a system to run before a phase", https://cs2.paradoxwikis.com/Systems). **Verdict: the decompile wins; the three methods choose a band within one phase, and a mod that reads the wiki literally will expect its system to run outside the phase it named.**
2. **Within a band, registration order is execution order**, because `addIndex` is a monotonic counter and is the sole tiebreak after phase.
3. **A mod always registers after all of vanilla**, because `SystemOrder.Initialize` runs at `GameManager.cs:615` and mod `OnLoad` at `:618`. So a mod's `UpdateAt` lands after every vanilla `UpdateAt` in that phase, its `UpdateBefore` after every vanilla `UpdateBefore` but ahead of all vanilla `UpdateAt`, and its `UpdateAfter` last of everything. This is the only lever a mod has over vanilla ordering short of anchoring.

The modification phases make rule 3 concrete: each is bracketed by `UpdateBefore<AllowBarrier<ModificationBarrierN>>` and `UpdateAfter<ModificationBarrierN>` (`SystemOrder.cs:78-93`), so a mod's `UpdateBefore` in `Modification5` sits between the allow-barrier and vanilla's first `UpdateAt`, and a mod's `UpdateAfter` sits after the barrier playback.
The `Deserialize` phase uses all three bands as a designed sandwich: `UpdateBefore<PreDeserialize<T>>` (`:738-746`), then `UpdateAt` readers and migrations (`:801` among them), then `UpdateAfter<PostDeserialize<T>>` (`:863`, `:892-897`).

Corpus census of the three single-type forms, across all 20 repositories: **238 `UpdateAt`, 48 `UpdateAfter`, 40 `UpdateBefore`.**

Rots: the ±1,000,000 band constant (`UpdateSystem.cs:148/153/158/163`) and the `AllowBarrier`/`ModificationBarrierN` type names (`SystemOrder.cs:78-93`).

### Anchoring to another system by type, and the way it fails silently

`UpdateBefore<T, Other>` / `UpdateAfter<T, Other>` splice `T` immediately beside `Other` wherever `Other` ended up.
The splice is conditional on a phase match: `AddSystemUpdate` only consumes an `m_RefMap` entry when `systemData2.m_Phase == systemData.m_Phase` (`UpdateSystem.cs:382/412`).

**The failure mode.** `Refresh()` iterates `m_Systems` only; `m_RefMap` is never enumerated on its own (`:292-363`).
So if `Other` is not registered in the phase you passed — wrong phase, or a type nothing ever registers — your system is added to a dictionary and **never runs**, with no exception, no log line and no symptom other than absence.
Anchoring is also recursive, with a depth guard that throws `Too deep system order` past 100 levels (`:365-370`).

**Corpus, 9 of 20 anchor to another system by type**: Anarchy, CS2-MoveIt, CS2-Platter, NodeController, PlopTheGrowables, Recolor, RoadBuilder-CSII, Traffic, Tree_Controller.
Every anchor in the corpus names a system that really is registered in the phase passed — I checked all of them against `SystemOrder.cs`, and there are no dead anchors. Examples with both sides cited:

- `Anarchy/Anarchy/AnarchyMod.cs:149` → `CompositionSelectSystem` at `SystemOrder.cs:139` (Modification3). ✓
- `NodeController/NodeController/Mod.cs:59` → `Game.Net.GeometrySystem` at `:150` (Modification4). ✓
- `NodeController/NodeController/Mod.cs:65/66` registers **one** system, `NcStretchSystem`, into two phases, anchored to a different vanilla system in each: after `GeometrySystem` in Modification4 and after `SecondaryLaneSystem` (`:184`) in Modification4B.
- `Traffic/Code/Mod.cs:93/94` → `ApplyNetSystem` at `:715` (ApplyTool). ✓
- `Recolor/Recolor/Mod.cs:149-154` is the deepest chain: three systems anchored after vanilla `MeshColorSystem` (`:648`, PreCulling), then `CustomMeshColorSystem` anchored after one of _those_ — anchoring to the mod's own system, resolved by the recursion.
- `PlopTheGrowables/Code/Mod.cs:82` anchors `SelectiveZoneCheckSystem` after its own `PloppedBuildingSystem` with the comment "must run after we've assigned ploppable flags" — the clearest statement in the corpus of why anchoring beats a bare phase.
- `CS2-MoveIt/Code/MoveIt/Mod.cs:92` → `ApplyPrefabsSystem`, which vanilla registers with `UpdateAfter` (`:298`) rather than `UpdateAt`; the splice works regardless of which band the anchor sits in.

The catalog's `Move It`, `Anarchy`, `Recolor`, `Platter` and `Node Controller` entries do not name anchoring to a vanilla system by type. The catalog's `Recolor` entry does not name that it holds the corpus's deepest anchor chain.

### The 32 phases nest; they do not run flat

This was the second open conflict entry, and the derivation below is what it asked for.
The wiki still carries the literal placeholder `[insert infographic here]` where the ordering diagram belongs (https://cs2.paradoxwikis.com/Systems, confirmed on the live page 2026-08-02), so nothing corroborates this.

**Ruled (2026-08-02, the mod-lifecycle-and-ordering pass; `conflicts.md`).** The ordering ships, and it ships as a tree.
A flat phase list is not a simplification of this material but a different and false claim, since everything driven from `MainLoop` runs before the frame's simulation steps.
Uncorroborated is acceptable: being the first source to state it is this plugin's value rather than a risk.
What the reference owes instead is provenance — one sentence saying the tree was derived from the registration table and the phase drivers rather than read from one file, so a reader re-checking it knows what to re-run.
Rots: the per-phase system names and counts, which is where the version moves — re-check against the vanilla system-order class and the systems that drive a phase from their own `OnUpdate`. The tree's shape is architecture and carries no marker.
The weak points recorded below stay in this file and do not ship: they tell the authoring agent how far to reach, and prose hedging each of them would teach a reader to distrust the one source that checked.

**The enum's declaration order is not the execution order.** `SystemUpdatePhase` declares `Invalid = -1`, then `MainLoop`, `LateUpdate`, the eight modification phases, `PreSimulation`, `PostSimulation`, `GameSimulation`, `EditorSimulation`, `Rendering`, `PreTool`, `PostTool`, `ToolUpdate`, `ClearTool`, `ApplyTool`, `Serialize`, `Deserialize`, `UIUpdate`, `UITooltip`, `PrefabUpdate`, `DebugGizmos`, `LoadSimulation`, `PreCulling`, `CompleteRendering`, `Raycast`, `PrefabReferences`, `Cleanup` (`src/Game/Game/SystemUpdatePhase.cs:3-38`) — 32 members plus `Invalid`. The ordinal is used only to index `m_UpdateRanges` (`UpdateSystem.cs:172/180`); it carries no timing meaning.

**Method.** I swept every `Update(SystemUpdatePhase` call site under `src/Game/`: 37 matches, of which 2 are `UpdateSystem`'s own overload declarations (`UpdateSystem.cs:166/206`), leaving **35 call sites in 16 owner types**. For each owner I found its registration in `SystemOrder.cs`. Because `SystemOrder.cs` is provably the only vanilla registrar (previous section), and because only `GameManager` sits on Unity's player-loop callbacks, the tree closes.

**Root.** `GameManager` is a `MonoBehaviour` (`GameManager.cs:61`) and makes the only four calls that are not nested inside another system's update: `Update()` → `UpdateWorld()` → `MainLoop` (`:2390`), then `UpdateUI()`, then `PostUpdateWorld()` → `Cleanup` (`:2398`); and `LateUpdate()` → `LateUpdateWorld()` → `LateUpdate` (`:2406`) then `DebugGizmos` (`:2407`).
`MainThreadDispatcher.UpdateUpdaters()` sits at `:713`, outside the `shouldUpdateManager` guard, so deferred callbacks run even when the world does not.

**The tree.** Indentation is nesting; each nested phase is driven from the `OnUpdate` of the system listed above it, and each driver's own position is its `SystemOrder.cs` registration.

```
Unity Update()  →  GameManager.Update()                         GameManager.cs:702-715
  MainLoop                                                      GameManager.cs:2390
    (UpdateBefore band, in registration order)
      UpdateWorldTimeSystem, PathfindQueueSystem, EndFrameBarrier    SystemOrder.cs:47-49
    (UpdateAt band, in registration order)
      RaycastSystem            :55   →  Raycast                 RaycastSystem.cs:808
      PrefabSystem             :56   →  PrefabUpdate            PrefabSystem.cs:759
      CityConfigurationSystem  :57
      ToolSystem               :58   →  PreTool                 ToolSystem.cs:255
                                     →  ToolUpdate              ToolSystem.cs:327
                                          ToolOutputSystem  :694 (UpdateAfter band)
                                            →  ClearTool  or  ApplyTool   ToolOutputSystem.cs:25/28
                                     →  PostTool                ToolSystem.cs:257
      LoadGameSystem           :59   →  Deserialize             LoadGameSystem.cs:53
                                          ResolvePrefabsSystem :801
                                            →  PrefabReferences ResolvePrefabsSystem.cs:514
      ModificationSystem       :60   →  Modification1, 2, 2B, 3, 4, 4B, 5, ModificationEnd
                                                                ModificationSystem.cs:19-26
      UnlockSystem             :61
      AllowBarrier<EndFrameBarrier>  :62
      PreRenderSystem          :63   →  PreCulling              PreRenderSystem.cs:23
      PathfindSetupSystem      :64
      PathfindResultSystem     :65
      AchievementTriggerSystem :66
      UIUpdateSystem           :67   →  UIUpdate                UIUpdateSystem.cs:19
                                          TooltipUISystem  :910
                                            →  UITooltip        TooltipUISystem.cs:55
      RenderingSystem          :68   →  Rendering               RenderingSystem.cs:134
      SaveGameSystem           :69   →  Serialize               SaveGameSystem.cs:82
                                          BeginPrefabSerializationSystem :733
                                            →  PrefabReferences BeginPrefabSerializationSystem.cs:272
      DebugWatchSystem         :71
    (UpdateAfter band)
      PrepareCleanUpSystem     :50
  (GameManager.UpdateUI)
  Cleanup                                                       GameManager.cs:2398

Unity LateUpdate()  →  GameManager.LateUpdate()                 GameManager.cs:717-723
  LateUpdate                                                    GameManager.cs:2406
      DebugSystem              :70
      SimulationSystem         :74   →  PreSimulation           SimulationSystem.cs:272 (or :168 while loading)
                                     →  0..8 x GameSimulation and/or EditorSimulation
                                                                SimulationSystem.cs:282/286
                                        (while loading, instead: 8 x LoadSimulation, :173)
                                     →  PostSimulation          SimulationSystem.cs:295 (or :175)
      CompleteRenderingSystem  :75   →  CompleteRendering       CompleteRenderingSystem.cs:53
      GizmosSystem             :76
      AutoSaveSystem           :77
  DebugGizmos                                                   GameManager.cs:2407
```

All 32 phases appear. The four consequences a reader most needs:

- **Everything in `MainLoop` — modification, tools, UI, rendering — runs before the simulation for that frame**, because the simulation phases hang off `SimulationSystem` in `LateUpdate`. A `GameSimulation` system sees the world as the modification phases left it, and its own output is first observed by the _next_ frame's modification phases.
- **`GameSimulation` runs a variable number of times per rendered frame, including zero.** `SimulationSystem.OnUpdate` clamps the step count to `[0, 8]` (`SimulationSystem.cs:255-256`) from the selected game speed; `PreSimulation` and `PostSimulation` still run exactly once per frame even at zero steps (`:272/295`). `EditorSimulation` and `GameSimulation` are gated on `m_ToolSystem.actionMode` (`:280/284`), and both can be skipped.
- **`Deserialize` and `Serialize` fire once per load and once per save, not per frame.** `LoadGameSystem` and `SaveGameSystem` set `Enabled = false` in `OnCreate` and are flipped on by an awaited `RunOnce()` (`LoadGameSystem.cs:40/43-48`, `SaveGameSystem.cs:59-64`). Everything else in the tree runs unconditionally: I checked all 16 drivers for `RequireForUpdate` and for a disabled default, and these two are the only gated ones.
- **`PrefabReferences` is reached from two different parents**, `Deserialize` and `Serialize`, and its four occupants (`SystemOrder.cs:726-729`) therefore run in both directions of the save pipeline.

**Where the derivation is weakest, stated plainly.**
It rests on four things: that `SystemOrder.cs` is the only vanilla registrar (proved by count, 1013 + 3 + 0); that the 35 call sites are complete (a text grep for `Update(SystemUpdatePhase` — a driver invoking the method through a delegate or reflection would not appear, and I found no evidence of one but did not prove its absence); that `SystemBase.Update()` runs `OnUpdate` whenever `Enabled && ShouldRunSystem()` (`src/Unity.Entities/Unity.Entities/SystemBase.cs:38-63`), which I verified only for the 16 drivers and not for their `RequireMatchingQueriesForUpdate` flag; and that Unity calls every `Update()` before any `LateUpdate()`, which is engine behaviour I did not verify from the decompile.
Within `MainLoop` the ordering is not derived at all — it is read directly off `SystemOrder.cs:47-77`, which is the strongest part.
Nothing corroborates the tree as a whole, and its shape is a synthesis over ~50 cited lines rather than a read of one file.

Rots: every driver system name and its `SystemOrder.cs` line, and the `SystemUpdatePhase` member names — re-derive by re-running the `Update(SystemUpdatePhase` sweep against the new version.

### The phase catalogue

Vanilla registrations per phase, counted over `SystemOrder.cs` (occurrence counts, so a system registered twice counts twice). Corpus repositories touching each phase, out of 20, in brackets.

**Driven from `GameManager.Update()`**

- **`MainLoop`** — 20 [3]. The frame's spine, and the only phase whose members drive the other phases. A mod registering here (`FindIt-CSII/FindIt/Mod.cs:70/71`, `RoadBuilder-CSII/RoadBuilder/Mod.cs:65/74`, `ExtraAssetsImporter/EAI.cs:162`) lands after `RenderingSystem` and `SaveGameSystem` and before `PrepareCleanUpSystem`. By the time it fires, everything else in the frame except `Cleanup` has already run.
- **`Raycast`** — 1 [3], `ToolRaycastSystem` (`:1058`). First of the `UpdateAt` band in MainLoop, so nothing else has run this frame. A mod raycast system belongs here (`Traffic/Code/Mod.cs:88`, `NodeController/NodeController/Mod.cs:69`).
- **`PrefabUpdate`** — 23 [6]. `TextureStreamingSystem`, `GeometryAssetLoadingSystem`, `PrefabInitializeSystem`, `MeshSystem`, `UIInitializeSystem`, `ObjectInitializeSystem`, `ZoneSystem`, `ZonePrefabInitializeSystem`, `TriggerPrefabSystem` (`:1004-1026`). Driven every `MainLoop` frame, unconditionally: `PrefabSystem.OnUpdate` calls `UpdatePrefabs()` — which returns early on an empty update map — then drives the phase regardless, and gates only `ReplacePrefabSystem.FinalizeReplaces()` on there having been updates (`PrefabSystem.cs:755-763`). `PrefabSystem` registers no `RequireForUpdate`, so nothing suppresses its own update either. Verdict: an earlier pass of this file read the early return in `UpdatePrefabs()` as gating the phase drive; it does not, and the corrected reading is what `prefabs-and-assets.md` establishes independently. Where prefab-shaping systems go.
- **`PreTool`** — 1 [1], `OriginalDeletedSystem` (`:698`).
- **`ToolUpdate`** — 15 [15]. The eleven vanilla tools (`:699-709`) plus `UpgradeDeletedSystem`, bracketed by `AllowBarrier<ToolOutputBarrier>` and `ToolOutputBarrier` (`:693/695`). The most-used phase in the corpus after `UIUpdate`. **`ToolSystem` sets `m_LastTool.Enabled = true` immediately before driving this phase and `false` when a tool stops being active** (`ToolSystem.cs:316/325/327`), which is the mechanism behind the wiki's "`ToolBaseSystem` uses `ToolUpdate`" — a tool elsewhere would still be enable-gated by the tool system but would run at the wrong moment.
- **`ClearTool`** / **`ApplyTool`** — 1 [2] / 9 [3]. Driven from `ToolOutputSystem` at the tail of `ToolUpdate`, mutually exclusive on `ToolSystem.applyMode` (`ToolOutputSystem.cs:22-30`). `ApplyTool` holds the nine `Apply*System` consumers (`:712-720`).
- **`PostTool`** — 7 [1]. `ToolFeedbackSystem`, `SelectedUpdateSystem`, `CourseSplitSystem`, `SubElementDeleteSystem`, `MapTileSystem` (`:721-725`).
- **`Deserialize`** — 161 [6]. The largest phase after `GameSimulation`, and the only one whose three bands are used as a designed pipeline (above). Fires once per load.
- **`PrefabReferences`** — 4 [1]. `PrimaryPrefabReferencesSystem`, `CheckPrefabReferencesSystem`, `SecondaryPrefabReferencesSystem`, `CheckPrefabReferencesSystem` again (`:726-729`). Reached from inside both `Deserialize` and `Serialize`.
- **`Modification1`** — 18 [6]. The `Generate*System` family (`:94-103`) plus graph-delete systems. Where entities get created from definitions.
- **`Modification2`** — 14 [4]. Edges, routes, buildings initialise; `DamageSystem`, `DestroySystem` (`:110-121`).
- **`Modification2B`** — 15 [**0**]. Cross-references and area geometry (`:122-134`). **No corpus mod uses it.**
- **`Modification3`** — 10 [5]. `SubObjectReferencesSystem`, `FindOwnersSystem`, `AttachSystem`, `CompositionSelectSystem` (`:135-142`). The phase mods anchor into for network composition work.
- **`Modification4`** — 33 [4]. Modifiers, `SubNetReferencesSystem`, `Game.Net.GeometrySystem` (`:150`), `LaneSystem` (`:155`). The two most-forked vanilla systems in the corpus both live here.
- **`Modification4B`** — 16 [3]. `ObjectEmergeSystem`, `LaneReferencesSystem`, `SecondaryLaneSystem`, `BuildingStateEfficiencySystem` (`:174-187`).
- **`Modification5`** — 62 [6]. `RemovedSystem`, the `UpdateCollect` systems, the search-tree and graph systems (`:188-247`).
- **`ModificationEnd`** — 60 [10]. `InstanceCountSystem`, `LaneDataSystem`, `ZoneCheckSystem` (`:258`), `ValidationSystem` (`:264`), `ApplyPrefabsSystem` (`:298`), notification triggers (`:305`). The last chance to touch an entity before the frame's tool and render work.
- **`PreCulling`** — 19 [3]. `CameraUpdateSystem`, `PreCullingSystem`, `OverlayInfomodeSystem`, `MeshColorSystem` (`:648`), `WindTextureSystem` (`:631-649`). Where per-instance colour work goes.
- **`UIUpdate`** — 83 [19]. Every vanilla UI system (`:898-1003`); used by more corpus repositories than any other phase. The wiki's "`UISystemBase` uses `UIUpdate`" is convention — `UISystemBase` (`src/Game/Game.UI/UISystemBase.cs:11`) constrains no phase.
- **`UITooltip`** — 23 [14]. The `Temp*TooltipSystem` family and tool tooltips (`:911-933`). **This one is a hard requirement, not a convention:** `TooltipUISystem.OnUpdate` clears `groups` (`TooltipUISystem.cs:51`), drives `UITooltip` (`:55`), then reads the list back into its bindings (`:62-65`). A `TooltipSystemBase` running anywhere else writes into a list that has already been consumed. The corpus obeys exactly this line — `CS2-NetworkTools` registers `NT_ActionTooltipSystem : TooltipSystemBase` at `UITooltip` and `NT_UITooltipSystem : UISystemBase` at `UIUpdate` (`CS2-NetworkTools/NetworkTools.Mod/NetworkToolsMod.cs:73/72`, class declarations at `Systems/Tooltips/ActionTooltipSystem.cs:18` and `Systems/Tooltips/UITooltipSystem.cs:25`), which reads like an inconsistency and is not.
- **`Rendering`** — 40 [10]. `BatchInstanceSystem`, the `Initialize*` family, `ObjectColorSystem` (`:665`), `BatchDataSystem` (`:684`), `AreaRenderSystem` (`:686`), `VFXSystem` (`:650-689`). Runs after `UIUpdate` in the same frame.
- **`Serialize`** — 7 [2]. `TrimPathsSystem` and two `PreSerialize<T>` in the front band, then `BeginPrefabSerializationSystem`, `SerializerSystem`, `EndPrefabSerializationSystem`, `WriteSystem` (`:730-736`). Vanilla registers **nothing** in the `UpdateAfter` band, so a mod's `UpdateAfter` here is the last thing that runs before the save completes.
- **`Cleanup`** — 6 [2]. `AudioManager`, `AnimatedSystem`, `BatchUploadSystem`, `CleanUpSystem` (`:51-54`), `CompleteCullingSystem`, `CompleteEnabledSystem` (`:690/691`). Driven from `PostUpdateWorld`, after `UpdateUI`. Where a disposal system goes (`CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:81`).

**Driven from `GameManager.LateUpdate()`**

- **`LateUpdate`** — 5 [0]. `DebugSystem`, `SimulationSystem`, `CompleteRenderingSystem`, `GizmosSystem`, `AutoSaveSystem` (`:70-77`). **No corpus mod uses it**, which is unsurprising: registering here means running between the drivers of the whole simulation.
- **`PreSimulation`** — **0** [0]. Driven twice (`SimulationSystem.cs:168/272`) and occupied by nobody, vanilla or corpus. The one genuinely empty phase, and the only place to run exactly once per frame immediately before the frame's simulation steps.
- **`GameSimulation`** — 297 [7]. By far the largest phase; the whole city simulation. Runs 0-8 times per frame with the update-interval mask applied.
- **`EditorSimulation`** — 9 [2]. `TimeSystem`, `ClimateSystem`, `SnowSystem`, `WindSimulationSystem`, `WindSystem`, `NaturalResourceSystem`, `FireSimulationSystem`, `StreetLightSystem` (`:602-609`) — environment only, no city. A mod wanting to work in the editor registers into both this and `GameSimulation` (`Water_Features/Water_Features/WaterFeaturesMod.cs:149-158`).
- **`LoadSimulation`** — 20 [0]. Navigation and AI systems (`:610-629`), run 8 iterations per frame while `SimulationSystem.m_LoadingCount > 0` — 1024 total for a new game (`SimulationSystem.cs:160/164-186`). **No corpus mod uses it.**
- **`PostSimulation`** — 1 [0], `WaterSystem` (`:630`). Runs once per frame after the steps.
- **`CompleteRendering`** — 1 [0], `NotificationIconRenderSystem` (`:692`). Driven after `CompleteRenderingSystem` has completed every GPU upload (`CompleteRenderingSystem.cs:43-53`).
- **`DebugGizmos`** — 31 [1]. The `*DebugSystem` family (`:1027-1057`). Last phase of the frame; `Traffic/Code/Mod.cs:71` puts its gizmo system here behind `#if DEBUG_GIZMO`.

Where a phase's purpose is stated above without a name from the enum itself, that purpose is inferred from its occupants in `SystemOrder.cs` — the enum carries no documentation and neither does any vanilla comment, so occupancy is the only evidence available.

Rots: this entire table. Every count, every system name and every line number moves with the version — re-derive by re-grepping `SystemUpdatePhase\.\w+` over `src/Game/Game.Common/SystemOrder.cs`.

### The update interval is power-of-two, and it only bites in three phases

`UpdateSystem.GetInterval` calls `GameSystemBase.GetUpdateInterval(phase)` and `GetUpdateOffset(phase)` (defaults 1 and -1, `src/Game/Game/GameSystemBase.cs:131-139`) and throws `System update interval not power of 2` when `!math.ispow2(interval)` (`UpdateSystem.cs:277-290`).

Two facts about that throw that a reader needs together:

- **It fires at registration**, inside `Register`, i.e. inside the `UpdateAt<T>` call in the mod's `OnLoad`. It therefore propagates out of `OnLoad`, is caught by `ModManager.InitializeMods` and takes the **whole mod** down — not just the offending system. The state it ends in is `Disposed` rather than `State.GeneralError`, which is what keeps it below the notification gate; the finding below on `OnLoad` throwing carries that mechanism. Corrected 2026-08-10 against the earlier reading, which this file already contradicted at that finding.
- **It applies in every phase**, whereas the interval itself is consulted in only three.

Because the interval is read only by the three-argument `Update(phase, updateIndex, iterationIndex)` overload (`:224`), and that overload is called from exactly three sites — `LoadSimulation` (`SimulationSystem.cs:173`), `EditorSimulation` (`:282`) and `GameSimulation` (`:286`) — **a `GetUpdateInterval` override on a system registered in any other phase has no effect at all.** The one-argument `Update(phase)` (`:166-204`) never reads `m_Interval` or `m_Offset`.

The game itself ships a dead one: `Game.Audio/WeatherAudioSystem.cs:135-138` returns 16 unconditionally and is registered only at `Modification2` (`SystemOrder.cs:115`). Where a vanilla system sits in more than one phase, the override branches — `NaturalResourceSystem.cs:334-341` and `WindSimulationSystem.cs:158-165` return 1 outside `GameSimulation`; `RandomTrafficDispatchSystem.cs:387-394` and `TrafficSpawnerAISystem.cs:647-654` return a smaller interval during `LoadSimulation`.

The mask is `(updateIndex & (interval - 1)) != offset → skip` where `updateIndex` is `SimulationSystem.frameIndex` (`UpdateSystem.cs:224`, `SimulationSystem.cs:279`). `Refresh()` auto-assigns an offset to spread same-interval systems across frames, but only where the system returned a negative one (`:326-351`, `:397-407`).
`m_ResetInterval` is the interval for a `GameSystemBase` and `int.MaxValue` otherwise (`:25`); when it is `<= iterationIndex` the system's `Dependency` is reset (`:230-232`, `GameSystemBase.cs:141-144`), which only reaches systems with an interval below the maximum 8 iterations.

**The vanilla idiom, and the corpus copies it verbatim.** Systems declare `public static readonly int kUpdatesPerDay = <n>` and return `262144 / kUpdatesPerDay` — 2^18 simulation frames per in-game day (`src/Game/Game.Simulation/AirPollutionSystem.cs:74/86`, `AvailabilityInfoToGridSystem.cs:140/150`, `BudgetApplySystem.cs:86`). Systems that split their entity set across 16 sub-frames use `262144 / (kUpdatesPerDay * 16)` paired with `SimulationUtils.GetUpdateFrame(frameIndex, kUpdatesPerDay, 16)` (`AgingSystem.cs:173/204/262`).
`Water_Features/Water_Features/Systems/SeasonalStreamsSystem.cs:36/64` is `AirPollutionSystem`'s formula character for character. `Time2Work` uses `SimulationUtils.GetUpdateFrameWithInterval(frameIndex, GetUpdateInterval(GameSimulation), 16)` in eight systems (`Time2Work/NightShift/Systems/Time2WorkWorkerSystem.cs:364` among them).

**Corpus census: 47 `public override int GetUpdateInterval` declarations across 6 of the 20 repositories — CS2-NetworkTools, CS2-WriteEverywhere, InfoLoom, Time2Work, Tree_Controller, Water_Features — and exactly one `GetUpdateOffset` override in the whole corpus.** That one is `Time2Work/NightShift/Systems/Time2WorkCitizenBehaviorSystem.cs:63/65`, returning interval 16 and offset 11 — the exact values of the vanilla system it replaces (`src/Game/Game.Simulation/CitizenBehaviorSystem.cs:1016-1024`). That copy reproduces the vanilla frames only because the vanilla system itself declares a non-negative offset: the two-type registration inherits the anchor's resolved offset only while the fork's own is still negative (`UpdateSystem.cs`'s `m_Offset < 0` guard at `:389`/`:414`), so an explicit `GetUpdateOffset` override at or above zero cancels that inheritance, and matching the interval while leaving the offset alone is the general recipe; nobody else in the corpus overrides the offset at all. The catalog's `Realistic Trips` entry does not name this.

Several corpus overrides are dead by the rule above: `CS2-WriteEverywhere/BelzontWE/Templates/WETemplateDisposalSystem.cs:76` on a system registered at `Cleanup`; `InfoLoom/InfoLoom/Systems/SankeyUISystems/WorkforcePipelineSankeySystem.cs:331` and `Systems/CommercialSystems/CommercialDemandData/CommercialSystem.cs:249` on systems registered at `UIUpdate` (`InfoLoom/InfoLoom/Mod.cs:64/53`); `Time2Work/NightShift/Systems/SpecialEventsUISystem.cs:285` at `UIUpdate`. The catalog's `Info Loom` entry says it demonstrates "an update interval so the query runs only every few hundred ticks", which holds for its `GameSimulation` systems and not for its `UIUpdate` ones.

**The wiki agrees on the power-of-two rule** ("the returned interval must be a power of 2", https://cs2.paradoxwikis.com/Systems). **Verdict: corroborated by the decompile at `UpdateSystem.cs:286-289`.** It says nothing about which phases consult the interval.

### The silent disable: which hooks, which message, which do not

`GameSystemBase` subscribes four lifecycle hooks in `OnCreate` (`src/Game/Game/GameSystemBase.cs:17-31`) and wraps each in try/catch. **They do not behave the same way.**

| Wrapper | Hook | Log message | Sets `Enabled = false`? |
| --- | --- | --- | --- |
| `WorldReady` `:98-109` | `OnWorldReady()` | `"<TypeName>: Error on game preload, disabling system..."` | **yes** `:107` |
| `GamePreload` `:85-96` | `OnGamePreload(Purpose, GameMode)` | `"<TypeName>: Error on game preload, disabling system..."` | **yes** `:94` |
| `GameLoaded` `:72-83` | `OnGameLoaded(Context)` | `"<TypeName>: Error on game load, disabling system..."` | **yes** `:81` |
| `GameLoadingComplete` `:60-70` | `OnGameLoadingComplete(Purpose, GameMode)` | `"<TypeName>: Error on state change, disabling system..."` | **no** — the line is absent |
| `FocusChanged` `:33-43` | `OnFocusChanged(bool)` | `"<TypeName>: Error on Focus change"` | no |

Two things in that table are the payload:

- **`OnGameLoadingComplete` says "disabling system..." and does not disable the system** (`:68` against `:81`/`:94`/`:107`). A reader diagnosing from the log message alone will draw the wrong conclusion.
- **`OnWorldReady` and `OnGamePreload` emit the identical message**, both reading "Error on game preload" (`:106` and `:93`), so the log cannot distinguish which hook threw. This looks like a copy-paste in the game's source; the stack trace in the logged exception is the only way to tell them apart.

All five log through `COSystemBase.baseLog`, which is `LogManager.GetLogger("SceneFlow")` (`src/Colossal.Core/Colossal.Entities/COSystemBase.cs:9`) — not the mod's own logger, which is where a mod author will look first and find nothing.

**`OnCreate` and `OnUpdate` are covered by neither mechanism, and each fails differently.**

- **`OnCreate` throwing kills the whole mod.** Unity's `AddSystem_OnCreate_Internal` catches, removes the half-built system from the world, and **rethrows** (`src/Unity.Entities/Unity.Entities/World.cs:303-316`). The throw propagates out of `GetOrCreateSystemManaged` inside `UpdateAt<T>` (`UpdateSystem.cs:143`), out of `OnLoad`, and into `ModManager.InitializeMods`'s catch — which disposes the mod and logs `"Error initializing mod {0} ({1})"` (`ModManager.cs:451-455`).
- **`OnUpdate` throwing disables nothing and repeats forever.** `UpdateSystem.Update` wraps each system's `Update()` and logs `"System update error during {0}->{1}:"` with the phase name and the system's type name (`:188-197`, and identically in the three-argument overload at `:236-245`). The loop continues to the next system; the throwing one runs again next frame. Errors are suppressed from the UI only in editor mode (`:191-193`).

So there are three distinct failure surfaces for a mod, and the "silent disable" belongs to exactly one of them:

| Where it throws | Outcome |
| --- | --- |
| Mod `OnLoad` (including any system's `OnCreate`) | whole mod fails, `OnDispose` still called, state ends `Disposed` |
| A system's `OnWorldReady` / `OnGamePreload` / `OnGameLoaded` | that system disabled for the session |
| A system's `OnGameLoadingComplete` / `OnFocusChanged` | logged, system keeps running |
| A system's `OnUpdate` | logged every frame at `Critical`, system keeps running |

Verdict: a hook throwing is **not** invisible to the player, against this file's earlier reading of it as "one log line, no user-visible symptom".
The five hook wrappers log at `Error` through `COSystemBase.baseLog`, which is the `SceneFlow` logger, and that logger's `showsErrorsInUI` holds its `true` default — so each raises the modal error dialog and pauses the simulation.
**The `OnUpdate` row is scoped and the others are not:** `UpdateSystem.Update` sets `showsErrorsInUI = false` around its own log call when `GameManager.instance.gameMode.IsEditor()` and restores it after (`src/Game/Game/UpdateSystem.cs:190-196`, identically `:238-245`), so that one raises no dialog in the editor.
The `OnLoad` row is quieter still: `ModInfo.Dispose()` overwrites the error state with `Disposed` before the notification pass tests `state >= IsNotModWarning` (`src/Game/Game.Modding/ModManager.cs:170-173`, `:264` against `:270`), so it pushes no notification either.
Established by the diagnostics pass, which re-derived this code rather than trusting this file; `diagnostics.md` owns the surface and carries the chain from the logged level to the dialog.

Verdict: the dialog appears, confirmed by the maintainer on 2026-08-05 against the running game, so the claim ships flat rather than marked.

**A mod's `OnLoad` throwing is a different mechanism entirely.** `ModManager.InitializeMods` catches, calls `modInfo2.Dispose()` — which calls `OnDispose()` on every instance (`ModManager.cs:160-173`) — and logs (`:451-455`). The mod's state becomes `GeneralError` — and then `Disposed`, because `Dispose()` sets it (`:170-173`), which is what keeps it below the notification pass's `state >= IsNotModWarning` gate (`:270`). So **no notification is pushed for it**, corrected 2026-08-05 against the earlier reading of `:266-336`. The logger is `LogManager.GetLogger("Modding").SetShowsErrorsInUI(false)` (`:178`), so there is no dialog either, and `Modding.log` is the whole record.

**A corpus author found this by hand.** `ExtraAssetsImporter/EAI.cs:164-168` catches, logs with the comment "Doing this, because the game isn't logging any error", then `throw ex;` with "This should still send the error to the game and so start the OnDispose." The second half is exactly right — the rethrow is what triggers `Dispose()`. The first half is not quite: `ModManager.cs:454` does log it, to the `Modding` logger with UI errors suppressed.

**The consequence for `OnDispose`: it is called on a mod whose `OnLoad` threw**, at whatever point it got to. Every null guard in the corpus's `OnDispose` bodies is load-bearing rather than defensive style.

Rots: all five log-message strings and the `SceneFlow` / `Modding` logger names.

### Disabling a vanilla system and slotting a fork into its place

`Enabled = false` on a `ComponentSystemBase` does not unregister it. `SystemBase.Update()` skips `OnUpdate` when `!Enabled` but still calls `OnStopRunning()` once (`src/Unity.Entities/Unity.Entities/SystemBase.cs:38-63`, `:64-68`), and the system stays in `m_Updates`. **That is what makes the pattern work: a disabled system is still a valid anchor**, so `UpdateBefore<Fork, Vanilla>(phase)` puts the fork in the dead original's exact slot.

**Corpus: 4 of 20 disable a vanilla system inside `OnLoad`.**

- `NodeController/NodeController/Mod.cs:58-59` — the textbook form. Disable `Game.Net.GeometrySystem`, then `UpdateBefore<NcGeometrySystem, GeometrySystem>(Modification4)`, matching the vanilla registration at `SystemOrder.cs:150`.
- `Traffic/Code/Mod.cs:76-79` — the same shape on `Game.Net.LaneSystem` (`SystemOrder.cs:155`), then a chain: `TrafficLaneSystem` before `LaneSystem`, and two sync systems before `TrafficLaneSystem`.
- `PlopTheGrowables/Code/Mod.cs:74/82` — the weaker form. Disables `ZoneCheckSystem` (`SystemOrder.cs:258`, ModificationEnd) but anchors `SelectiveZoneCheckSystem` after its own `PloppedBuildingSystem` rather than to the disabled original. Same phase, different position, and the mod's comment explains why.
- `Time2Work/NightShift/Mod.cs:94-115` — the largest scale, eleven vanilla systems disabled by name via `World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<T>().Enabled = false`, replaced by bare `UpdateAt` registrations in `GameSimulation` (`:117-149`) with no anchoring at all. Two of the disables are conditional on another mod being present (`:107-115`).

Two idioms for reaching the system, both present: `updateSystem.World.GetOrCreateSystemManaged<T>()` (NodeController, PlopTheGrowables, Traffic) and `World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<T>()` (Time2Work, Traffic's deferred path). The first needs no extra using and is available from the `OnLoad` parameter.
Note that `GetOrCreateSystemManaged` **creates** the system if it is missing, so a mistyped or unregistered type silently produces a live-but-never-updated system rather than an error.

**Registration is not confined to `OnLoad`.** `Traffic/Code/Mod.cs:167` calls `World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<UpdateSystem>().UpdateAfter<TLEDataMigrationSystem>(SystemUpdatePhase.Deserialize)` from inside a deferred callback that runs frames after `OnLoad`, alongside disabling a _third-party_ mod's system found by reflection (`:150/158/166`). This works because `Register` sets `m_IsDirty` and the next `Update(phase)` calls `Refresh()`, which rebuilds every phase's ranges from scratch (`UpdateSystem.cs:168-171`, `:292-363`).
The catalog's `Traffic` entry does not name late registration through the update system.

### Deferring work until the mod manager has settled

`Colossal.Core.MainThreadDispatcher` is the answer, and it has two shapes with different semantics:

- `RegisterUpdater(Action)` wraps the action in a `Func<bool>` that returns `true`, so it runs **exactly once** on the next dispatcher tick and unregisters itself (`src/Colossal.Core/Colossal.Core/MainThreadDispatcher.cs:97-104`, `:85-95`).
- `RegisterUpdater(Func<bool>)` re-runs **every tick until the delegate returns true** (`:106-116`) — the polling form, for waiting on a condition.
- `RunOnMainThread(Action)` runs inline if already on the main thread and otherwise defers (`:32-42`, `isMainThread` at `:20`).
- `WaitXFrames(int)` returns a `Task` completing after N ticks (`:44-49`); `GameManager.Initialize` uses it four times to pace its own boot (`GameManager.cs:609/612/617/628`).

The tick is `MainThreadDispatcher.UpdateUpdaters()` at `GameManager.cs:713`, called from `GameManager.Update()` **outside** the `shouldUpdateManager` guard — so deferred work runs even while the world is not updating.

**Corpus: 6 of 20 use it** — CS2-WriteEverywhere, ExtraAssetsImporter, ExtraDetailingTools, FindIt-CSII, RoadBuilder-CSII, Traffic. What they defer:

- Cross-mod detection and compatibility fixes: `Traffic/Code/Mod.cs:112-115` defers four callbacks including the third-party system disable above.
- Cross-mod API registration: `FindIt-CSII/FindIt/Mod.cs:73-74`.
- Another mod's bridge: `ExtraDetailingTools/EDT.cs:93`.
- Forcing a UI module asset to load through `GameManager.instance.modManager.AddUIModule` — which requires `ModManager.m_Initialized`, so it cannot run inside `OnLoad` at all (`CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:104-109`, gate at `ModManager.cs:475-483`).
  **Amended 2026-08-03 (the new-sources resweep): the gate is `if (m_Initialized)` with no `else`** (`src/Game/Game.Modding/ModManager.cs:477-482`), so an early call is a silent no-op and not a throw — which is the half a reader diagnosing a missing UI module needs, and which this file had left to inference. The body it skips is the one that registers the `"ui-mods"` host location for the module's directory and pushes the module's `coui` path into the frontend's app bindings, so nothing partial happens either.
- Lazily reflecting a private field on a vanilla system that must already exist: `RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderSystem.cs:63`.
- Marshalling prefab registration back to the main thread from a background import: `ExtraAssetsImporter/MOD/AssetImporter/ImportersUtils.cs:67/198/310` uses `RunOnMainThread`, and `AssetsImporterManager.cs:402` blocks a worker on `WaitXFrames(2).Wait()`.

The catalog's `Traffic`, `Find It`, `Extra Detailing Tools` and `Road Builder` entries do not name deferral through the main-thread dispatcher.

Rots: the `MainThreadDispatcher` type and method names.

### The pre-deserialize hook, and its three siblings

Four generic wrapper systems exist, each a `GameSystemBase` whose `OnUpdate` forwards to one method on the wrapped system, passing the load or save context:

- `PreDeserialize<T> where T : ComponentSystemBase, IPreDeserialize` → `T.PreDeserialize(m_LoadGameSystem.context)` (`src/Game/Game.Serialization/PreDeserialize.cs`, interface at `IPreDeserialize.cs`).
- `PostDeserialize<T> where T : ComponentSystemBase, IPostDeserialize` (`PostDeserialize.cs`, `IPostDeserialize.cs`).
- `PreSerialize<T>` and `IPreSerialize` (`SystemOrder.cs:731-732` uses it for `ClimateSystem` and `AudioManager`).

The wrapper is what gets registered, not the wrapped system: `updateSystem.UpdateBefore<PreDeserialize<MySystem>>(SystemUpdatePhase.Deserialize)`.
Vanilla registers 57 `PreDeserialize<T>` in the Deserialize front band (`SystemOrder.cs:738-794`). Verdict: re-derived at the review gate of 2026-08-04, which found the earlier count of nine had been taken from the leading contiguous block rather than the full registration span. The first nine state the pattern's purpose — the six spatial `SearchSystem`s, `InstanceCountSystem`, `PathfindQueueSystem`, `PathfindResultSystem` (`:738-746`): **clear a mod's index or cache before the loader starts writing entities into it.** The remaining 48 span UI, infoview, rendering, audio, tool, pathfinding, buffer and tutorial systems, so the pattern is not a presentation-layer one.

**Corpus: 2 of 20.** `Traffic/Code/Mod.cs:68/101` wraps `ModUISystem` (a `UISystemBase`, `Traffic/Code/UISystems/ModUISystem.cs:20`) and `ModDefaultsSystem` (`Traffic/Code/Systems/ModDefaultsSystem.cs:11`). `CS2-Platter/Platter/PlatterMod.cs:174` wraps `P_ParcelSearchSystem` (`CS2-Platter/Platter/Systems/Parcels/P_ParcelSearchSystem.cs:31`), a quadtree owner — exactly vanilla's own use.
Nobody in the corpus uses `PostDeserialize<T>` or `PreSerialize<T>`.

**The Deserialize phase has a limit a corpus author documented.** `Traffic/Code/Mod.cs:81-82`: "data migration - requires NetCompositions to work correctly - not possible to run in `SystemUpdatePhase.Deserialize`" — the migration is registered into `Modification4` instead, anchored before the sync system. A mod migration sometimes cannot run in the phase built for it.

`Water_Features` builds the save-safety sandwich out of bands rather than wrappers: `UpdateBefore<BeforeSerializeSystem>(Serialize)` collapses mod state into vanilla fields, and five `UpdateAfter<...>(Serialize)` registrations restore it after `WriteSystem` has run (`Water_Features/Water_Features/WaterFeaturesMod.cs:142-147`). The catalog's `Water Features` entry names the collapse half and not the restore half, and does not name that the mod registers the same systems into three phases (`GameSimulation`, `Serialize`, `EditorSimulation`).

### `OnDispose` hygiene

`ModInfo.Dispose()` calls `OnDispose()` on every instance, clears the list and sets `State.Disposed` (`ModManager.cs:160-173`). It is called from two places: `ModManager.Dispose()` at shutdown (`:495-522`) and `InitializeMods`'s catch when `OnLoad` threw (`:453`).

**Corpus census, 20 of 20 accounted for:**

- **18 implement `OnDispose` directly.** The two that do not — `CS2-NetworkTools` and `InfoLoom` — inherit it from a framework base class whose source is not in the corpus checkout.
- **7 unpatch Harmony**: Anarchy (`Anarchy/Anarchy/AnarchyMod.cs:162`), BetterBulldozer (`BetterBulldozer/BetterBulldozer/BetterBulldozerMod.cs:136`), ExtraDetailingTools (`ExtraDetailingTools/EDT.cs:130`), LineTool (`LineTool-CS2/Code/Mod.cs:137`), Time2Work (`Time2Work/NightShift/Mod.cs:182-183`), Tree_Controller (`Tree_Controller/Tree_Controller/TreeControllerMod.cs:138`), Water_Features (`Water_Features/Water_Features/WaterFeaturesMod.cs:167`).
- **15 unregister settings from the options UI**, near-universally in the shape `if (Settings != null) { Settings.UnregisterInOptionsUI(); Settings = null; }` (`AreaBucket/Mod.cs:105-109` is representative).
- **4 null the mod's own static instance**: CS2-Platter (`PlatterMod.cs:140`), LineTool (`:140`), PlopTheGrowables (`Code/Mod.cs:91`), and Traffic nulls its settings field (`Traffic/Code/Mod.cs:127`).
- **1 is empty**: `CS2-WriteEverywhere/BelzontWE/WriteEverywhereCS2Mod.cs:94-96`.
- **2 do real cleanup beyond registrations**: `RoadBuilder-CSII/RoadBuilder/Mod.cs:89-96` removes a UI host location and deletes a temp directory; `ExtraAssetsImporter/EAI.cs:176-177` relocates its database and clears its caches.
- **0 unregister their systems.** Nothing in `IMod` or `UpdateSystem` offers a way to, and no mod attempts it. `UpdateSystem` has no unregister method (`UpdateSystem.cs:141-164` is the complete registration surface).

Time2Work's unpatch is worth flagging: it constructs a _new_ `Harmony(harmonyID)` in `OnDispose` to call `UnpatchAll(harmonyID)` (`NightShift/Mod.cs:182-183`) rather than holding the instance from `OnLoad` — which works because the id is the key, and is a consequence of the mod class having no usable instance state across the two calls.

### Not every mod system needs a phase

Two systems in the corpus are brought into existence with `GetOrCreateSystemManaged` and deliberately never registered with `UpdateSystem`: `Time2Work/NightShift/Mod.cs:160` (`CitizenScheduleSection`) and `InfoLoom/InfoLoom/Mod.cs:66-70` (five `IL*Section` types).

They are driven by a different mechanism. `CitizenScheduleSection : ExtendedInfoSectionBase` calls `m_InfoUISystem.AddMiddleSection(this)` from its own `OnCreate` (`Time2Work/NightShift/Systems/CitizenScheduleSection.cs:41`), which appends it to `SelectedInfoUISystem.m_MiddleSections` (`src/Game/Game.UI.InGame/SelectedInfoUISystem.cs:207-210`); that system then drives every member with `RequestUpdate()` and `PerformUpdate()` from its own update (`:472-474`, `:501-503`).
So the phase question does not arise: creating the system is the registration.
This is why the `[UpdateInGroup]` attribute on that class is doubly inert, and it is the one place where "which phase?" is the wrong question. Where a mod system extends a vanilla base that self-registers into a vanilla collection, the collection's owner decides when it runs.

## Bridge

This is a technique topic; every mechanics reference exercises it, but they do not exercise it equally, and only four need something specific from here.

- **`simulation-time-and-units`** needs the most. The power-of-two interval, the `262144 / kUpdatesPerDay` formula, `SimulationSystem.frameIndex` as the mask input, and the 0-8 steps per rendered frame with the speed clamp (`src/Game/Game.Simulation/SimulationSystem.cs:255-256/277-288`) are all half of that topic's material and all live here. A claim about "how often X happens" is meaningless without the interval mechanism, and a claim about "per day" resolves through `262144`.
- **`citizens-and-households`**, **`economy-and-companies`** and **`city-services-and-coverage`** all sit in `GameSimulation`, the 297-registration phase, and all three are where forking a vanilla simulation system is the realistic change. Anything written there needs the fork recipe from this file — disable, anchor with the two-type form, match the original's interval and leave `GetUpdateOffset` alone, since the two-type registration inherits the anchor's resolved offset and an override at or above zero cancels it — plus the fact that a `GameSimulation` change is not visible to the modification phases until the following frame.
- **`roads-and-traffic`** needs the modification-phase decomposition specifically: `Modification3` for composition (`CompositionSelectSystem`, `SystemOrder.cs:139`), `Modification4` for geometry and lanes (`:150/155`), `Modification4B` for secondary lanes (`:184`), `ModificationEnd` for validation (`:264`). The two most-forked vanilla systems in the entire corpus, `GeometrySystem` and `LaneSystem`, are both in `Modification4`, and a network change that picks the wrong one of those four phases runs against stale or not-yet-written data.
- **`zoning-buildings-and-land-value`** needs the `ModificationEnd` slot where `ZoneCheckSystem` lives (`:258`) and the `GameSimulation` slot where `BuildingConstructionSystem` lives (`:585`); the corpus's cleanest substitution example spans exactly those two (`PlopTheGrowables/Code/Mod.cs:74-82`).
- **`environment-and-pollution`** is the one mechanics area with a live `EditorSimulation` presence (`SystemOrder.cs:602-609`), so a change there needs the dual-registration pattern rather than a single `GameSimulation` registration.

Going the other way, three technique topics need something from here and should be told so rather than rediscovering it: `save-serialization` owns the contents of the `Deserialize` and `Serialize` phases but takes their position in the tree and their once-per-load firing from here; `diagnostics` owns the log file but takes the five silent-disable message strings and the `ModInfo.State` values from here; `performance-and-memory` owns allocation but takes the update-interval mechanism and the `ResetDependency` interaction (`UpdateSystem.cs:230-232`) from here.

## Dead ends

- **`survey-wiki-inventory.md`'s snapshot was not needed.** A live `WebFetch` of both wiki pages returned real content on 2026-08-02; the bot challenge did not fire. Both pages are cited by URL above.
- **The `ECS - Entity Component System` wiki page carries nothing on this topic.** Fetched and checked: it covers entities, components, archetypes and queries, mentions that mods can create systems, and redirects to the `Systems` page for anything further. It contributed no claim to this file.
- **No update-phase ordering exists on the wiki.** The `[insert infographic here]` placeholder recorded at `survey-wiki-inventory.md:93/288` is still there on the live page. Nothing corroborates the derived tree; that is the whole substance of the second open conflict.
- **`[UpdateAfter]` / `[UpdateBefore]` / `[UpdateInGroup]` in `src/Game/`: zero.** Searched, empty, recorded above. Do not re-run this without also checking that no stock system group is created, which is the stronger claim and the one that closes the question.
- **Corpus search for `UpdateSystem.currentPhase`: zero hits.** The property exists and is public (`UpdateSystem.cs:73`), maintained across nested `Update` calls with a save/restore (`:176-203`), so a system could read which phase it is in. No mod does. Not worth shipping as a technique, but worth knowing it exists before someone re-derives it.
- **`SystemUpdatePhase.PreSimulation` has zero registrations in vanilla and zero in the corpus.** Verified by grepping `SystemOrder.cs` (31 of 32 phases appear; `PreSimulation` does not) and the corpus. It is driven twice per frame regardless (`SimulationSystem.cs:168/272`). Genuinely empty, not merely unpopular.
- **`LateUpdate`, `LoadSimulation`, `PostSimulation` and `CompleteRendering` have zero corpus users.** Confirmed by a corpus-wide grep for each phase name. Anything the reference says about them is derived from vanilla occupancy alone, with no practice to check it against.
- **`Modification2B` has zero corpus users** despite 15 vanilla registrations. Same caveat.
- **The framework base classes are not readable.** `LucaModBase<T>` (CS2-NetworkTools), `ModsCommonBase<T>` (InfoLoom) and `BasicIMod` (CS2-WriteEverywhere) are all imported through shared props/targets whose sources are absent from the corpus checkout — `CS2-NetworkTools/NetworkTools.Mod/Common/` and `InfoLoom/InfoLoom/Common/` are empty directories, and no file in the corpus declares any of the three types. Everything claimed about those three mods above comes from their own derived classes and their overrides. In particular, their `OnLoad` bodies and their `OnDispose` implementations could not be read.
- **`Cities2-TLE` is gone from the corpus.** `survey-mods-techniques.md` was written against 12 repositories including `Cities2-TrafficLightsEnhancement`; the checkout now holds 20 and that one is not among them. Its claims — notably the `EntityQueryUtils` query-rewriting technique at `survey-mods-techniques.md:107` — could not be re-verified and are not carried into this file. `Traffic` still references it by assembly name at runtime (`Traffic/Code/Mod.cs:146`), which is the only trace of it left.
- **Whether `FormatterServices.GetUninitializedObject` throws on an abstract `IMod` implementation was not verified against a running game.** `GetTypesDerivedFrom<IMod>` applies no abstract filter (`ReflectionUtils.cs:508-513`), so an abstract base declaring `IMod` inside a mod's own assembly would be passed to it. .NET documents that call as throwing for abstract types, which would make the whole mod fail to load, but nothing in the decompile or the corpus exercises it and I did not test it. Do not ship this as a rule without a test.
- **No entry was appended to `conflicts.md`.** Everything unsettled here falls inside the two existing open entries, whose evidence is recorded above; nothing else resisted the decompile.
