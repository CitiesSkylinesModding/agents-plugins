# Custom tools

**Baseline.** Decompiled game 1.6.0f1; mod corpus read 2026-08-02 at the commits the 20-repository checkout carried; wiki fetched live 2026-08-02 (the bot challenge did not fire, so no snapshot substitution was needed).

## Findings

### There are two tool base classes, and only one thing forces the heavier one

`ToolBaseSystem : GameSystemBase, IEquatable<ToolBaseSystem>` is abstract and is the base every tool derives from (`src/Game/Game.Tools/ToolBaseSystem.cs:28`).
`ObjectToolBaseSystem : ToolBaseSystem` is the only intermediate abstract class the game ships (`src/Game/Game.Tools/ObjectToolBaseSystem.cs:25`).

**The game has eleven concrete tools, not the fifty-three systems `Game.Tools` holds** (`survey-decompile-moddable-surface.md:373` counts the namespace's systems, which is a different number from its tools).
Nine derive from `ToolBaseSystem` directly — `AreaToolSystem`, `BulldozeToolSystem`, `DefaultToolSystem`, `NetToolSystem`, `RouteToolSystem`, `SelectionToolSystem`, `TerrainToolSystem`, `WaterToolSystem`, `ZoneToolSystem` — and two from `ObjectToolBaseSystem`: `ObjectToolSystem` (`src/Game/Game.Tools/ObjectToolSystem.cs:33`) and `UpgradeToolSystem` (`src/Game/Game.Tools/UpgradeToolSystem.cs:24`).
The rest of `Game.Tools` is the consumer half — the `Apply*System` family, the `Generate*System` family, `ValidationSystem`, `ToolClearSystem`, `ToolApplySystem` — plus the components and enums.

**What `ObjectToolBaseSystem` adds is exactly one protected helper and four cached system references.**
The helper is `protected JobHandle CreateDefinitions(...)` (`ObjectToolBaseSystem.cs:2363`), a 23-parameter method that schedules the game's own `CreateDefinitionsJob` (`:35`) wired with roughly seventy `ComponentLookup`/`BufferLookup` handles, and that emits `CreationDefinition` + `ObjectDefinition` entities through `ToolOutputBarrier` including sub-objects, sub-nets, sub-lanes, sub-areas, placeholders and attachment resolution (`:2363-2452`).
The four references are `m_ToolOutputBarrier`, `m_ObjectSearchSystem`, `m_WaterSystem`, `m_TerrainSystem`, all set in its `OnCreate` (`:2343-2361`).
So the rule is: derive from `ObjectToolBaseSystem` when the tool places objects and wants vanilla-quality previews for free; derive from `ToolBaseSystem` for anything else, including tools that emit their own definitions by hand.

**Corpus: 29 concrete mod tools across 15 of 20 repositories, and only two use the heavier base.**
`ObjectToolBaseSystem`: `CS2-MoveIt/Code/MoveIt/Tool/MoveItToolSystem.cs:75` and `LineTool-CS2/Code/Systems/LineToolSystem.cs:39`.
`ToolBaseSystem`, derived directly: `Anarchy/Anarchy/Systems/AnarchyComponentsTool/AnarchyComponentsToolSystem.cs:38`, `AreaBucket/Systems/AreaBucketToolSystem.cs:30` and `AreaBucket/Systems/AreaReplacementToolSystem/AreaReplacementToolSystem.cs:21`, `BetterBulldozer/BetterBulldozer/Tools/RemoveVehiclesCimsAndAnimalsTool.cs:32` and `Tools/SubElementBulldozerTool.cs:34`, `CS2-NetworkTools/NetworkTools.Mod/Systems/Tools/Base/BaseToolSystem.cs:61` and `Systems/Tools/PrefabCache/PrefabCacheToolSystem.cs:28`, `CS2-Platter/Platter/Systems/Tests/P_TestToolSystem.cs:25`, `ExtraDetailingTools/MOD/Systems/Tools/GrassToolSystem.cs:21` and `Tools/TransformGizmoTool.cs:46`, `FindIt-CSII/FindIt/Systems/PickerToolSystem.cs:22`, `NodeController/NodeController/Main/Tools/NodeControllerTool.cs:23`, `Recolor/Recolor/Systems/Tools/ColorPainterToolSystem.Main.cs:40` and `Tools/ColorPickerToolSystem.cs:30` and `Tools/SelectNetLaneFencesToolSystem.cs:22`, `RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderToolSystem.cs:30`, `Traffic/Code/Tools/LaneConnectorToolSystem.cs:35` and `Tools/PriorityToolSystem.cs:27`, `Tree_Controller/Tree_Controller/Tools/TreeControllerTool.cs:39`, `Water_Features/Water_Features/Tools/CustomWaterToolSystem.cs:36`.
One repository interposes its own abstract layers, which is where nine of the 29 live: `NT_BaseToolSystem` is abstract (`CS2-NetworkTools/NetworkTools.Mod/Systems/Tools/Base/BaseToolSystem.cs:61`) with six concrete tools under it — `NT_AddNodeToolSystem` (`Systems/Tools/AddNode/AddNodeToolSystem.cs:26`), `NT_ConnectToolSystem` (`Systems/Tools/Connect/ConnectToolSystem.cs:17`), `NT_GenerateToolSystem` (`Systems/Tools/Generate/GenerateToolSystem.cs:17`), `NT_RemoveNodeToolSystem` (`Systems/Tools/RemoveNode/RemoveNodeToolSystem.cs:23`), `NT_SlideNodeToolSystem` (`Systems/Tools/SlideNode/SlideNodeToolSystem.cs:28`), `NT_SuperNodeToolSystem` (`Systems/Tools/SuperNode/SuperNodeToolSystem.cs:23`) — plus a second abstract layer `NT_PathSelectionToolSystem` (`Systems/Tools/PathSelection/PathSelectionToolSystem.cs:46`) carrying `NT_ParallelToolSystem` (`Systems/Tools/Parallel/ParallelToolSystem.cs:23`) and `NT_RoadShapeToolSystem` (`Systems/Tools/RoadShape/RoadShapeToolSystem.cs:18`).
That is the only tool-family abstraction in the corpus, and it is the answer to "I have six tools that all raycast and mark eligibility the same way".
Every mod tool is declared `partial`, and the six largest split across files by concern.
A thirtieth tool exists whose base class could not be read: `CS2-WriteEverywhere/BelzontWE/Tools/WEWorldPickerTool.cs:20` declares `: IBelzontToolSystem` yet overrides `toolID`, `GetPrefab`, `TrySetPrefab` and `uiModeIndex` (`:24/34/39/44`), so it derives from `ToolBaseSystem` transitively through the closed-source framework `mod-lifecycle-and-ordering.md:458` records as absent from the checkout.

**Verdict on the survey's tool census.** `survey-mods-techniques.md:226` says "Nine custom tools exist" over twelve repositories and names `ObjectToolBaseSystem` as "needed when you want the vanilla object-preview/definition machinery."
The count is stale at twenty repositories — 29 concrete tools now — and the reason given for the heavier base is right, but understated: the machinery is one method, and everything else `ObjectToolBaseSystem` carries is bookkeeping for it.

### Registration is automatic, and the survey's account of it is wrong

`ToolBaseSystem.OnCreate` ends with `m_ToolSystem.tools.Add(this)` (`src/Game/Game.Tools/ToolBaseSystem.cs:315`).
Nothing else in `src/Game/` adds to that list, and `ToolSystem.tools` lazily creates it on first access (`src/Game/Game.Tools/ToolSystem.cs:163-173`).

**Verdict.** `survey-mods-techniques.md:244` states "Tools registered via `updateSystem.UpdateAt<T>(SystemUpdatePhase.ToolUpdate)` are appended to `ToolSystem.tools`."
The decompile shows the append happens in `ToolBaseSystem.OnCreate`, not in the phase registration.
The two are easy to confuse because `UpdateSystem.UpdateAt<T>` calls `World.GetOrCreateSystemManaged<T>()` (`src/Game/Game/UpdateSystem.cs:139-142`), so registering a tool is usually the thing that constructs it and therefore the thing that triggers the append.
The distinction matters twice: a tool constructed by any other route (`GetOrCreateSystemManaged` from another system's `OnCreate`) is already in the list before the mod registers its phase, and a tool that never calls `base.OnCreate()` is in no list at all.
One mod records the mechanism correctly in a comment — `AreaBucket/Systems/AreaBucketToolSystem.cs:198`, `m_ToolSystem.tools.Remove(this); // rollback added self in base.OnCreate`.

`ToolBaseSystem.OnCreate` also sets `base.Enabled = false` (`:310`), so a tool is inert from birth and the tool system alone turns it on.
Two mods set `Enabled = false` themselves anyway, one before `base.OnCreate()` and one after (`Anarchy/Anarchy/Systems/AnarchyComponentsTool/AnarchyComponentsToolSystem.cs:134`, `Water_Features/Water_Features/Tools/CustomWaterToolSystem.cs:358`); both are redundant and neither is harmful.

Rots: `ToolSystem.tools` being a mutable `List<ToolBaseSystem>` rather than a read-only view — re-check `src/Game/Game.Tools/ToolSystem.cs:163-173`.

### Position in the tool list decides who claims a prefab

`ToolSystem.ActivatePrefabTool(PrefabBase)` walks `tools` in order and stops at the first tool whose `TrySetPrefab` returns `true`, making it active; if none claims the prefab it falls back to `m_DefaultToolSystem` and returns `false` (`src/Game/Game.Tools/ToolSystem.cs:263-278`).
That single loop is the whole meaning of the ordering: index 0 gets first refusal on every prefab the toolbar hands out.

The vanilla list order is the registration order in `src/Game/Game.Common/SystemOrder.cs:699-709` — `AreaToolSystem`, `BulldozeToolSystem`, `DefaultToolSystem`, `NetToolSystem`, `ObjectToolSystem`, `RouteToolSystem`, `SelectionToolSystem`, `UpgradeToolSystem`, `ZoneToolSystem`, `TerrainToolSystem`, `WaterToolSystem` — so a mod tool registered from `IMod.OnLoad` lands after all eleven and never sees a prefab first.

**Corpus: six repositories move themselves in the list, and they disagree about when.**
At `OnCreate`, immediately after `base.OnCreate()` has appended them: `LineTool-CS2/Code/Systems/LineToolSystem.cs:579-607` (removes itself, then inserts at index 0 unless `toolList[0].toolID` is `"Tree Controller Tool"`, in which case index 1), `AreaBucket/Systems/AreaBucketToolSystem.cs:198-199` and `AreaBucket/Systems/AreaReplacementToolSystem/AreaReplacementToolSystem.cs:72-73` (`// applied before vanilla systems`), `CS2-NetworkTools/NetworkTools.Mod/Systems/Tools/Base/BaseToolSystem.cs:381-382` and `Systems/Tools/PrefabCache/PrefabCacheToolSystem.cs:46-47`.
At `OnGameLoadingComplete`, which runs after every mod's `OnCreate` and therefore wins any race: `Tree_Controller/Tree_Controller/Tools/TreeControllerTool.cs:363-364` and `BetterBulldozer/BetterBulldozer/Systems/BetterBulldozerUISystem.cs:365-368` (which moves two tools it does not own, from a UI system rather than from either tool).
One variant does not reach for index 0 at all: `ExtraDetailingTools/MOD/Systems/Tools/GrassToolSystem.cs:111-113` reads `m_ToolSystem.tools.IndexOf(objectToolSystem)` and reinserts itself at exactly that index, so it precedes the object tool and nothing else.

**`TrySetPrefab` has three observed shapes, and the safe one is not the obvious one.**
Return `false` unconditionally, so the tool never claims anything and is reached only by hotkey or UI: `Recolor/Recolor/Systems/Tools/ColorPainterToolSystem.Main.cs:130-133`, whose `GetPrefab()` returns `null` (`:124-127`).
Claim only while already active, so being at index 0 costs other tools nothing: `LineTool-CS2/Code/Systems/LineToolSystem.cs:468-482` gates on `m_ToolSystem.activeTool == this && prefab is ObjectGeometryPrefab` and excludes `BuildingPrefab`.
Claim by prefab kind and a separate enable flag: `AreaBucket/Systems/AreaBucketToolSystem.cs:324-329`, `return Mod.areaToolEnabled && Active`.
A tool at index 0 that returns `true` for prefabs it does not own hijacks the toolbar for every other tool, which is why the two mods that sit at index 0 both gate on already being active.

`GetPrefab()` and `TrySetPrefab(PrefabBase)` are the only two abstract members besides `toolID` (`src/Game/Game.Tools/ToolBaseSystem.cs:142/426/428`), so every tool must answer both even when the answer is "nothing" and "no".

**Ruled (2026-08-02, ticket 12; conflicts.md).** The restrained form is what the reference teaches as the default: a tool takes the slot of the one tool it must precede, read back with `m_ToolSystem.tools.IndexOf(...)` as `ExtraDetailingTools/MOD/Systems/Tools/GrassToolSystem.cs:111-113` does, rather than the front of the list.
Index 0 is not withheld — it is the answer for a tool that wants to claim a prefab kind a vanilla tool already claims — but it ships bound to its condition rather than as advice: return `true` from `TrySetPrefab` only while already active, which is what both mods sitting at index 0 do and is why they cost the tools behind them nothing.
The reference owes the vanilla list order as a baked table, since a reader choosing a slot has to know which eleven names are already in the list and in what order (`src/Game/Game.Common/SystemOrder.cs:699-709`, reproduced at `:53` above).
It also owes the reason the hook is `OnCreate`: a position stated relative to a tool does not need to win a race, because a mod inserting at index 0 later does not stop you preceding the object tool.
`OnGameLoadingComplete` is deliberately not taught, even though two mods use it and it beats every `OnCreate` reorder — teaching it is teaching an agent to beat other mods to the front, and the first two tools written from that instruction fight each other.

### The lifecycle contract, method by method

`ToolBaseSystem` seals `OnUpdate()` and redirects it: `protected sealed override void OnUpdate()` assigns `base.Dependency = OnUpdate(base.Dependency)` and then clears `m_FocusChanged` and `m_ForceUpdate` (`src/Game/Game.Tools/ToolBaseSystem.cs:411-417`).
A tool therefore overrides `protected virtual JobHandle OnUpdate(JobHandle inputDeps)` (`:420-423`) and can never override the parameterless one.

| Member                                                                                                              | Kind                                                                                            | What it is for                                                                      |
| ------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| `toolID`                                                                                                            | `public abstract string` (`:142`)                                                               | Identity for the UI binding and for cross-mod string checks                         |
| `GetPrefab()`                                                                                                       | `public abstract PrefabBase` (`:426`)                                                           | The prefab the toolbar should highlight; `null` is legal                            |
| `TrySetPrefab(PrefabBase)`                                                                                          | `public abstract bool` (`:428`)                                                                 | Claim or decline a prefab during `ActivatePrefabTool`                               |
| `OnUpdate(JobHandle)`                                                                                               | `protected virtual JobHandle` (`:420`)                                                          | The per-frame body; must return the tool's job handle                               |
| `InitializeRaycast()`                                                                                               | `public virtual void` (`:430`)                                                                  | Configure this frame's raycast; base resets every field                             |
| `GetAvailableSnapMask(out Snap, out Snap)`                                                                          | `public virtual void` (`:445`)                                                                  | Declare which snap flags exist and which are forced                                 |
| `GetRaycastResult(out ControlPoint)` and its `out bool forceUpdate` overload                                        | `protected virtual bool` (`:561/572`)                                                           | Convert a raycast hit into a control point; the seam for substituting a custom cast |
| `GetAllowApply()`                                                                                                   | `protected virtual bool` (`:533`)                                                               | Whether the current preview may be committed                                        |
| `SetUnderground(bool)`, `ElevationUp()`, `ElevationDown()`, `ElevationScroll()`                                     | `public virtual void` (`:451/455/459/463`)                                                      | Empty hooks the tool-options UI calls                                               |
| `GetUIModes(List<ToolMode>)`                                                                                        | `public virtual void` (`:282`)                                                                  | Publish the tool's modes to the UI                                                  |
| `uiModeIndex`                                                                                                       | `public virtual int`, default `0` (`:144`)                                                      | Which of those modes is current                                                     |
| `OnGameLoadingComplete(Purpose, GameMode)`, `OnFocusChanged(bool)`, `OnGamePreload`, `OnGameLoaded`, `OnWorldReady` | `protected virtual` on `GameSystemBase` (`src/Game/Game/GameSystemBase.cs:111/115/119/123/127`) | The game-lifecycle hooks a tool shares with every other system                      |
| `OnCreate` / `OnStartRunning` / `OnStopRunning`                                                                     | overridden by `ToolBaseSystem` itself (`:292/325/333`), so an override must call `base`         | Ordinary system lifecycle, with the tool-specific work in the base body             |

`OnStartRunning` sets `m_ForceUpdate = true` and calls `SetActions()`; `OnStopRunning` nulls `infoview`, clears `infomodes` and calls `ResetActions()` (`ToolBaseSystem.cs:324-339`).
`m_ForceUpdate` is therefore true for exactly the first frame after activation, which is how a tool knows to rebuild its preview from nothing rather than diff against last frame's.

**A block of the base class is unreachable from a mod, because `private protected` does not cross an assembly boundary.**
`m_DefaultApply`, `m_DefaultSecondaryApply`, `m_DefaultCancel`, `m_MouseApply`, `m_MouseCancel` (`:130-138`), the `toolActions` enumerable (`:242`), `actionsEnabled` (`:264`), and the three virtuals `SetActions()`, `ResetActions()`, `UpdateActions()` (`:341/347/362`) are all `private protected`, which C# resolves as "protected **and** internal" — derived classes in `Game.dll` only.
The consequence is concrete: the vanilla pattern of overriding `UpdateActions()` to recompute `shouldBeEnabled` every frame (`src/Game/Game.Tools/BulldozeToolSystem.cs:1398-1406`, and `src/Game/Game.Tools/ObjectToolSystem.cs:2709`) cannot be copied by a mod.
A mod tool sets `shouldBeEnabled` from `OnStartRunning`, `OnStopRunning` or its own `OnUpdate` instead, which is what the corpus does (`LineTool-CS2/Code/Systems/LineToolSystem.cs:937-938/952/972`).
`ProxyAction.DeferStateUpdating()`, which every vanilla `UpdateActions` body wraps itself in, is `internal static` as well (`src/Game/Game.Input/ProxyAction.cs:715`), so the batching it provides is unavailable too.
What _is_ reachable: `applyAction`, `secondaryApplyAction`, `cancelAction` and the three `*Override` setters are plain `protected` (`ToolBaseSystem.cs:188-240`).

Rots: the `private protected` accessibility of `UpdateActions` and the action fields — re-check `src/Game/Game.Tools/ToolBaseSystem.cs:130-138/341-364`.

### Activation, and restoring the tool that was there

Activation is one assignment: `m_ToolSystem.activeTool = this`.
The setter compares against the current value, calls `RequireFullUpdate()` and fires `EventToolChanged` (`src/Game/Game.Tools/ToolSystem.cs:84-99`).
Deactivation is the same assignment pointing elsewhere; `DefaultToolSystem` is what the game falls back to and is what `PreDeserialize` forces on load (`:610-614`).

**The enable/disable dance is the tool system's, not the tool's.**
`ToolSystem.ToolUpdate()` notices `activeTool != m_LastTool`, sets `m_LastTool.Enabled = false` and calls `m_LastTool.Update()` — one final pump with the system disabled, which is what drives the outgoing tool's `OnStopRunning` — then latches the new tool, sets `Enabled = true`, and drives `SystemUpdatePhase.ToolUpdate` (`ToolSystem.cs:308-327`).
So a mod tool registered at `ToolUpdate` runs only while it is the active tool, and needs no gate of its own.
`mod-lifecycle-and-ordering.md:252` records the same mechanism from the phase side.

**Three idioms for remembering the previous tool, and the third is the only complete one.**
Latch at activation: `LineTool-CS2/Code/Systems/LineToolSystem.cs:514-529` stores `_previousTool = m_ToolSystem.activeTool` inside its own `EnableTool()`, and `RestorePreviousTool()` at `:539-552` assigns it back, logging an error when it is null.
Latch at activation with a fallback: `CS2-MoveIt/Code/MoveIt/Tool/MoveItToolSystem.Lifecycle.cs:203` and `:226`, `m_ToolSystem.activeTool = _PreviousTool ?? m_DefaultToolSystem`.
Subscribe instead: `Anarchy/Anarchy/Systems/AnarchyComponentsTool/AnarchyComponentsToolSystem.cs:144-149` seeds `m_PreviousToolSystem = m_DefaultToolSystem` then hooks `m_ToolSystem.EventToolChanged` and records every tool that is not itself.
Only the third survives an activation route the tool did not initiate — the toolbar selecting a prefab, another mod switching tools, a save load — because the first two only record what was active at the moment their own entry point ran.

**`EventToolChanged` is also the corpus's standard way to gate a helper system on a tool.**
`Enabled = tool == m_NetToolSystem` appears verbatim in `Anarchy/Anarchy/Systems/NetworkAnarchy/NetworkDefinitionSystem.cs:55` and `Systems/NetworkAnarchy/SetRetainingWallSegmentElevationSystem.cs:52`; `Enabled = tool == m_DefaultToolSystem` in `Anarchy/Anarchy/Systems/AnarchyComponentsTool/AnarchyComponentsToolUISystem.cs:121`.
`ToolSystem` exposes four such events — `EventToolChanged`, `EventPrefabChanged`, `EventInfoviewChanged`, `EventInfomodesChanged` (`ToolSystem.cs:30-36`) — and they are plain public delegate fields rather than events, so `+=` and a raw assignment both compile and a careless `=` wipes every other subscriber.

**The UI cannot activate a mod tool.** The `tool.selectTool` trigger binding takes a string (`src/Game/Game.UI.InGame/ToolUISystem.cs:168`) and routes it through `GetToolSystem(string)` (`:344`), a hard-coded switch over the nine vanilla tool IDs — `"Net Tool"`, `"Area Tool"`, `"Zone Tool"`, `"Route Tool"`, `"Object Tool"`, `"Terrain Tool"`, `"Upgrade Tool"`, `"Bulldoze Tool"`, `"Selection Tool"` — whose default arm returns `m_DefaultToolSystem`.
`tool.selectToolMode` (`:169`) is the same story: `SelectToolMode(int)` type-tests the active tool against `NetToolSystem`, `ZoneToolSystem`, `BulldozeToolSystem`, `AreaToolSystem` and `ObjectToolSystem` and does nothing for anything else (`:306`), even though `BindToolModes` faithfully publishes whatever `GetUIModes` returns for any tool (`:240`).
So a mod tool may advertise modes to the UI and read `uiModeIndex` back, but the vanilla mode selector will not set them; activation and mode switching both have to come back through the mod's own C#.
The mode icon path the UI synthesises is `"Media/Tools/" + toolID + "/" + modeName + ".svg"` (`:254`), which is a game-content path rather than a `coui://` host, so a mod tool's mode icons resolve to nothing.
The corpus's one substantive `uiModeIndex` override behaves exactly as that predicts: `ExtraDetailingTools/MOD/Systems/Tools/TransformGizmoTool.cs:953` returns `(int)m_Mode`, and the mod does not read it back through the `"tool"` group at all — it publishes its own `GetterValueBinding<int>` in its own `"EDT"` binding group keyed on the tool's `toolID` (`ExtraDetailingTools/MOD/Systems/UI/TransformGizmoToolUI.cs:49`).
The only other override in twenty repositories is a no-op forwarding to the base (`CS2-WriteEverywhere/BelzontWE/Tools/WEWorldPickerTool.cs:44`).

**Mods do use `ActivatePrefabTool`, and for the reverse purpose.**
Six repositories call it, none of them to activate their own tool: they hand it a prefab and let the tool list decide.
`AreaBucket/Systems/AreaBucketToolUISystem.cs:84-88` is the clearest, a `ReActivateTool()` that reads `_toolSystem.activePrefab` and passes it straight back to `ActivatePrefabTool` — forcing the list walk to run again after a setting changed what its `TrySetPrefab` would answer.
The same re-activation appears at `AreaBucket/Systems/AreaReplacementToolSystem/AreaReplacementToolSystem.cs:326`.
The others hand it a prefab chosen in their own UI: `CS2-NetworkTools/NetworkTools.Mod/Systems/UI/UISystem.Handlers.cs:24`, `CS2-Platter/Platter/Systems/UI/P_UISystem.cs:691`, `FindIt-CSII/FindIt/Systems/FindItUISystem.Bindings.cs:24` (which passes `null`, i.e. deliberately falls through to `DefaultToolSystem`).
`Anarchy/Anarchy/Patches/ToolbarUISystemActivatePrefabToolPatch.cs:17-33` goes further and patches the vanilla `ToolbarUISystem.ActivatePrefabTool`, calling `toolSystem.ActivatePrefabTool(prefab)` itself from inside the patch.

Rots: the hard-coded tool-ID switch and the mode type-test in `ToolUISystem` — re-check `src/Game/Game.UI.InGame/ToolUISystem.cs`.

### The raycast: what the vanilla masks can see

`InitializeRaycast()` is not called by the tool; `ToolRaycastSystem.OnUpdate` calls it on `m_ToolSystem.activeTool` at the top of every frame, then builds one `RaycastInput` from its own properties and hands it to `RaycastSystem.AddInput(this, input)` (`src/Game/Game.Tools/ToolRaycastSystem.cs:90-130`).
`ToolRaycastSystem` is registered at `SystemUpdatePhase.Raycast` (`src/Game/Game.Common/SystemOrder.cs:1058`), and that phase is driven from the first line of `RaycastSystem.OnUpdate` in `MainLoop`, before it performs the cast (`src/Game/Game.Common/RaycastSystem.cs:808`; `SystemOrder.cs:55`).

`ToolBaseSystem.InitializeRaycast()` clears every field before the override gets a turn (`ToolBaseSystem.cs:430-443`): it strips sixteen flags from `raycastFlags`, sets `collisionMask = OnGround | Overground`, and zeroes `typeMask`, `netLayerMask`, `areaTypeMask`, `routeType`, `transportType`, `iconLayerMask`, `utilityTypeMask`, `rayOffset` and `owner`.
A tool that calls `base.InitializeRaycast()` first therefore starts from a clean slate and only has to set what it wants; the four control flags `DebugDisable`, `UIDisable`, `ToolDisable`, `FreeCameraDisable` are deliberately not in the cleared set, because `ToolRaycastSystem` manages `ToolDisable` from `m_ToolSystem.fullUpdateRequired` and `UIDisable` from `InputManager.instance.controlOverWorld` on the same pass (`ToolRaycastSystem.cs:98-113`), and any of the four set makes `RaycastInput.IsDisabled()` true, which `RaycastSystem.AddInput` turns into `m_TypeMask = TypeMask.None` (`src/Game/Game.Common/RaycastInput.cs:38-41`, `src/Game/Game.Common/RaycastSystem.cs:770-778`).

The eleven settable fields, all `{ get; set; }` on `ToolRaycastSystem` (`ToolRaycastSystem.cs:26-46`): `raycastFlags`, `typeMask`, `collisionMask`, `netLayerMask`, `areaTypeMask`, `routeType`, `transportType`, `iconLayerMask`, `utilityTypeMask`, `rayOffset`, `owner`.

**The enums, in full.**

`TypeMask : uint` (`src/Game/Game.Common/TypeMask.cs:6-23`), what kind of thing may be hit — `Terrain = 1` (`:8`), `StaticObjects = 2` (`:9`), `MovingObjects = 4` (`:10`), `Net = 8` (`:11`), `Zones = 0x10` (`:12`), `Areas = 0x20` (`:13`), `RouteWaypoints = 0x40` (`:14`), `RouteSegments = 0x80` (`:15`), `Labels = 0x100` (`:16`), `Water = 0x200` (`:17`), `Icons = 0x400` (`:18`), `WaterSources = 0x800` (`:19`), `Lanes = 0x1000` (`:20`), `None = 0` (`:21`), `All = uint.MaxValue` (`:22`).

`RaycastFlags : uint` (`src/Game/Game.Common/RaycastFlags.cs:6-28`) — `DebugDisable = 1` (`:8`), `UIDisable = 2` (`:9`), `ToolDisable = 4` (`:10`), `FreeCameraDisable = 8` (`:11`), `ElevateOffset = 0x10` (`:12`), `SubElements = 0x20` (`:13`), `Placeholders = 0x40` (`:14`), `Markers = 0x80` (`:15`), `NoMainElements = 0x100` (`:16`), `UpgradeIsMain = 0x200` (`:17`), `OutsideConnections = 0x400` (`:18`), `Outside = 0x800` (`:19`), `Cargo = 0x1000` (`:20`), `Passenger = 0x2000` (`:21`), `Decals = 0x4000` (`:22`), `EditorContainers = 0x8000` (`:23`), `SubBuildings = 0x10000` (`:24`), `PartialSurface = 0x20000` (`:25`), `BuildingLots = 0x40000` (`:26`), `IgnoreSecondary = 0x80000` (`:27`).

`CollisionMask` (`src/Game/Game.Common/CollisionMask.cs:6-12`) — `OnGround = 1` (`:8`), `Overground = 2` (`:9`), `Underground = 4` (`:10`), `ExclusiveGround = 8` (`:11`).

`Game.Net.Layer : uint` (`src/Game/Game.Net/Layer.cs:6-29`), the network layer filter — `Road = 1` (`:8`), `PowerlineLow = 2` (`:9`), `PowerlineHigh = 4` (`:10`), `WaterPipe = 8` (`:11`), `SewagePipe = 0x10` (`:12`), `StormwaterPipe = 0x20` (`:13`), `TrainTrack = 0x40` (`:14`), `Pathway = 0x80` (`:15`), `Waterway = 0x100` (`:16`), `Taxiway = 0x200` (`:17`), `TramTrack = 0x400` (`:18`), `SubwayTrack = 0x800` (`:19`), `Fence = 0x1000` (`:20`), `MarkerPathway = 0x2000` (`:21`), `MarkerTaxiway = 0x4000` (`:22`), `PublicTransportRoad = 0x8000` (`:23`), `LaneEditor = 0x10000` (`:24`), `ResourceLine = 0x20000` (`:25`), `NetFence = 0x40000` (`:26`), `None = 0` (`:27`), `All = uint.MaxValue` (`:28`).

`AreaTypeMask` (`src/Game/Game.Areas/AreaTypeMask.cs:6-14`) — `None = 0` (`:8`), `Lots = 1` (`:9`), `Districts = 2` (`:10`), `MapTiles = 4` (`:11`), `Spaces = 8` (`:12`), `Surfaces = 0x10` (`:13`).

`Game.Net.UtilityTypes : byte` (`src/Game/Game.Net/UtilityTypes.cs:6-17`) — `None = 0` (`:8`), `WaterPipe = 1` (`:9`), `SewagePipe = 2` (`:10`), `StormwaterPipe = 4` (`:11`), `LowVoltageLine = 8` (`:12`), `Fence = 0x10` (`:13`), `Catenary = 0x20` (`:14`), `HighVoltageLine = 0x40` (`:15`), `Resource = 0x80` (`:16`).

`IconLayerMask : uint` (`src/Game/Game.Notifications/IconLayerMask.cs:3-9`) — `None = 0` (`:5`), `Default = 1` (`:6`), `Marker = 2` (`:7`), `Transaction = 4` (`:8`). Not a `[Flags]` enum despite the name and the power-of-two values.

`RouteType` (`src/Game/Game.Routes/RouteType.cs:3-9`) — `None = -1` (`:5`), `TransportLine = 0` (`:6`), `WorkRoute = 1` (`:7`), `Count = 2` (`:8`). A plain enum, so this field selects one route kind rather than a set.

`Game.Prefabs.TransportType` (`src/Game/Game.Prefabs/TransportType.cs:3-21`) — `None = -1` (`:5`), then `Bus`, `Train`, `Taxi`, `Tram`, `Ship`, `Post`, `Helicopter`, `Airplane`, `Subway`, `Rocket`, `Work`, `Ferry`, `Bicycle`, `Car` at 0 through 13 (`:6-19`), `Count = 14` (`:20`). Also a plain enum. Note the namespace: it is `Game.Prefabs.TransportType`, not `Game.Routes`, and `System.Net.TransportType` exists as well, so an unqualified `TransportType` in a file with both usings is ambiguous.

**Verdict on the wiki's raycast tables.** `Creating a Tool` (https://cs2.paradoxwikis.com/Creating_a_Tool) is the only source that gathers these enums into one place, and every table it prints is a strict subset of the 1.6.0f1 declarations.
`RaycastFlags` stops at `EditorContainers` and omits `SubBuildings`, `PartialSurface`, `BuildingLots`, `IgnoreSecondary`.
`Layer` stops at `LaneEditor` and omits `ResourceLine` and `NetFence`.
`UtilityTypes` omits `Resource`.
`TypeMask`, `CollisionMask` and `AreaTypeMask` match exactly.
The page also omits three settable fields entirely — `routeType`, `transportType`, `iconLayerMask` — plus `rayOffset` and `owner`.
The decompile wins; the wiki's tables are correct as far as they go and were written against an earlier build.

Rots: every member of all nine enums above, and the eleven `ToolRaycastSystem` field names — re-read `src/Game/Game.Common/`, `src/Game/Game.Net/`, `src/Game/Game.Areas/`, `src/Game/Game.Routes/`, `src/Game/Game.Prefabs/TransportType.cs` and `src/Game/Game.Tools/ToolRaycastSystem.cs`.

**Reading the result.** `ToolBaseSystem.GetRaycastResult(out Entity, out RaycastHit)` calls `m_ToolRaycastSystem.GetRaycastResult(out RaycastResult)` and rejects a hit whose owner carries `Deleted` (`ToolBaseSystem.cs:542-553`).
`RaycastResult` is `{ RaycastHit m_Hit; Entity m_Owner; }` and implements `IAccumulable<RaycastResult>`, keeping the nearest hit and breaking ties on `m_HitEntity.Index` (`src/Game/Game.Common/RaycastResult.cs:6-18`).
`RaycastHit` carries `m_HitEntity`, `m_Position`, `m_HitPosition`, `m_HitDirection`, `m_CellIndex`, `m_NormalizedDistance`, `m_CurvePosition` (`src/Game/Game.Common/RaycastHit.cs:9-21`) — `m_Owner` is the thing that was hit, `m_HitEntity` the sub-element that took the hit, and the two differ whenever `SubElements`, `SubBuildings` or lane casting is on.
`BulldozeToolSystem.GetRaycastResult(out ControlPoint)` shows what a tool does with that difference: when the owner is a `Game.Net.Node` and `hit.m_HitEntity` is an `Edge`, it substitutes the edge (`src/Game/Game.Tools/BulldozeToolSystem.cs:1647-1663`).
`GetRaycastResult(out Entity, out RaycastHit, out bool forceUpdate)` folds in `m_OriginalDeletedSystem.GetOriginalDeletedResult(1) || m_ForceUpdate` (`:555-559`), which is how the tool learns that an entity one of its previews was standing in for disappeared and the preview has to be rebuilt from scratch.

**The corpus's fullest mask catalogue** is `BetterBulldozer/BetterBulldozer/Patches/BulldozeToolSystemInitializeRaycastPatch.cs:20-90`, a postfix that switches on its own UI mode and writes `typeMask`, `netLayerMask`, `raycastFlags`, `utilityTypeMask` and `collisionMask` per mode, then subtracts flags for the vanilla mode's filter toggles.
23 files in the corpus override `InitializeRaycast`.

### Mod-owned entities need a second cast, and there are three ways to get one

The vanilla cast iterates the game's own search trees — `Game.Zones.SearchSystem`, `Game.Areas.SearchSystem`, `Game.Net.SearchSystem`, `Game.Objects.SearchSystem`, `Game.Routes.SearchSystem` (`src/Game/Game.Common/RaycastSystem.cs:738-742`) — so an entity a mod created and never inserted into one of those trees is invisible to every mask combination there is.

**Route one: add a second input to the vanilla system.**
`RaycastSystem.AddInput(object context, RaycastInput)` (`src/Game/Game.Common/RaycastSystem.cs:770`) and `NativeArray<RaycastResult> GetResult(object context)` (`:781`) are both public, and the results are keyed by the `context` object reference, with `GetResult` returning the contiguous sub-array of every input that shared it.
`CS2-MoveIt/Code/MoveIt/Raycast.cs:43-88` builds a small `RaycastBase` hierarchy on exactly that: two subclasses, `RaycastTerrain` (`:11-24`, `m_TypeMask = TypeMask.Terrain`) and `RaycastSurface` (`:26-40`, `TypeMask.Areas` plus `AreaTypeMask.Surfaces | Spaces | Lots`), each registering itself as its own context in its constructor (`:60`) and reading back with `_RaycastSystem.GetResult(this)` (`:68`).
The registration is per frame rather than once, and the way that happens is easy to miss: both objects are freshly constructed at the end of the tool's `InitializeRaycast()` override (`CS2-MoveIt/Code/MoveIt/Tool/MoveItToolSystem.Methods.cs:25-42`, the two `new` calls at `:41-42`), which is the one method the raycast system calls on the active tool every frame.
It has to be per frame, because `RaycastSystem.CompleteRaycast()` clears the input list and its context list each pass (`src/Game/Game.Common/RaycastSystem.cs:826-836`), and it is called from `AddInput`, `GetResult` and `OnUpdate` alike.
That buys a mod extra concurrent casts with different masks in the same frame, which the single `ToolRaycastSystem` cannot express — but it still only sees what the vanilla search trees hold.

**Route two: a parallel pipeline registered at `SystemUpdatePhase.Raycast`.**
Two repositories build one, and both put it in the same phase for the same reason: that phase runs inside `RaycastSystem.OnUpdate` before the vanilla cast, so the mod's result is ready by the time the tool's own `OnUpdate` asks for it.
`Traffic/Code/Systems/ModRaycastSystem.cs:25-172` holds `NativeReference<CustomRaycastInput>` and `NativeReference<CustomRaycastResult>`, clears both at the top of each update, runs a terrain job when the input asks for `TypeMask.Terrain`, then either `RaycastJobs.FindLaneHandleFromTreeJob` or `FindConnectionNodeFromTreeJob` against the mod's own quadtrees, then `RaycastLaneHandles` or `RaycastLaneConnectionSubObjects` accumulating into a `NativeAccumulator<RaycastResult>` (`:51-142`); it exposes `SetInput(CustomRaycastInput)` (`:149`) and `GetRaycastResult(out CustomRaycastResult)` (`:153`), registered at `Traffic/Code/Mod.cs:88`.
The tool side mirrors the vanilla shape so the state machine reads the same either way: `Traffic/Code/Tools/LaneConnectorToolSystem.cs:298-303` overrides `InitializeRaycast` to call `base.InitializeRaycast()` and then its own `InitializeCustomRaycastInput()` (`:305-392`, which reuses `ToolRaycastSystem.CalculateRaycastLine(_mainCamera)` for the ray), and `:469-495` adds a private `GetCustomRaycastResult(out ControlPoint)` pair whose signatures match the base class's `GetRaycastResult` pair, so `OnUpdate` picks one or the other by state.
It also toggles the raycast system's `Enabled` with the tool (`:278/294`).
`ExtraDetailingTools/MOD/Gizmos/GizmosRaycastSystem.cs:14-156` is the second, and it is a near-verbatim reimplementation of the vanilla `RaycastSystem`'s own shape — `m_InputContext`/`m_ResultContext` `List<object>`, `NativeList<GizmosRaycastInput>`, `NativeList<RaycastResult>`, `CompleteRaycast()`, `NativeAccumulator` and a `RaycastResultJob` — so it supports several contexts per frame the way the vanilla one does; registered at `ExtraDetailingTools/EDT.cs:71` and consumed with `AddInput(this, input)` / `GetResult(this)` from the tool (`MOD/Systems/Tools/TransformGizmoTool.cs:1115/1341/1476`).

**Route three: widen the vanilla masks and filter the results.**
Cheapest, and the only one that works when the target _is_ a vanilla entity the masks merely exclude.
`BetterBulldozer` widens masks in a postfix on `InitializeRaycast` and vetoes hits in a prefix on the `(out Entity, out RaycastHit)` overload of `GetRaycastResult`; `CS2-Platter/Platter/Patches/MarkerPatches.cs:30-100` does the same but records in two `[ThreadStatic] bool` fields (`:33/36`) whether it was the one that set `RaycastFlags.Markers`, and filters only in that case, so it composes with other mods doing the same thing; the patch set is a postfix on `BulldozeToolSystem.InitializeRaycast` (`:45-47`) plus prefixes on both `GetRaycastResult` overloads, disambiguated by explicit `Type[]`/`ArgumentType[]` arrays because one takes `ref ControlPoint` (`:69-72`, `:84-86`).

### Apply and cancel is a three-value state machine driven from outside the tool

`ApplyMode` has exactly three members: `None = 0`, `Apply = 1`, `Clear = 2` (`src/Game/Game.Tools/ApplyMode.cs:3-8`).
`ToolBaseSystem.applyMode` is `public ApplyMode { get; protected set; }` (`ToolBaseSystem.cs:182`), and `ToolSystem.applyMode` simply forwards `m_LastTool.applyMode`, returning `None` when no tool has ever been active (`ToolSystem.cs:147-157`).

`ToolOutputSystem` — registered `UpdateAfter` in `ToolUpdate` (`src/Game/Game.Common/SystemOrder.cs:694`) — reads that value and drives one phase or neither:
`Clear` drives `SystemUpdatePhase.ClearTool`, `Apply` drives `SystemUpdatePhase.ApplyTool`, and `None` drives nothing (`src/Game/Game.Tools/ToolOutputSystem.cs:20-31`).
So the three values mean: **`Clear`** — throw away the preview entities and rebuild, **`Apply`** — commit them, **`None`** — leave last frame's preview exactly as it is, which is the cheap path a tool sits in while the mouse has not moved.

`ClearTool` holds one system: `ToolClearSystem` queries every `Temp` entity, adds `Deleted` to it, and for each `temp.m_Original` that carries `Hidden` adds `BatchesUpdated` and removes `Hidden` — restoring whatever the preview was standing in for (`src/Game/Game.Tools/ToolClearSystem.cs:62-112`, query at `:218`).
`ApplyTool` holds nine: `ToolApplySystem` plus the eight `Apply*System` consumers (`SystemOrder.cs:712-720`).
`ToolApplySystem` splits the `Temp` set by `TempFlags`: entities flagged `Delete` get `Deleted`; the rest get `Updated` and `Overridden`, except those carrying `TempFlags.Cancel` (`src/Game/Game.Tools/ToolApplySystem.cs:42-70`).

`Temp` is the preview tag — `{ Entity m_Original; float m_CurvePosition; int m_Value; int m_Cost; TempFlags m_Flags; }` (`src/Game/Game.Tools/Temp.cs:5-15`) — and `ecs-in-this-game.md:375` establishes that it lives in `Game.Tools`, is not serializable, and is excluded by nearly every vanilla query.
`TempFlags : uint` has eighteen members: `Create = 1` (`src/Game/Game.Tools/TempFlags.cs:8`), `Delete = 2` (`:9`), `IsLast = 4` (`:10`), `Essential = 8` (`:11`), `Dragging = 0x10` (`:12`), `Select = 0x20` (`:13`), `Modify = 0x40` (`:14`), `Regenerate = 0x80` (`:15`), `Replace = 0x100` (`:16`), `Upgrade = 0x200` (`:17`), `Hidden = 0x400` (`:18`), `Parent = 0x800` (`:19`), `Combine = 0x1000` (`:20`), `RemoveCost = 0x2000` (`:21`), `Optional = 0x4000` (`:22`), `Cancel = 0x8000` (`:23`), `SubDetail = 0x10000` (`:24`), `Duplicate = 0x20000` (`:25`).
`Hidden` is a zero-size tag (`src/Game/Game.Tools/Hidden.cs:7`) put on the _original_ entity so the preview can stand in for it — `src/Game/Game.Tools/GenerateObjectsSystem.cs:967` adds it, the `Apply*System` family removes it on commit (`src/Game/Game.Tools/ApplyObjectsSystem.cs:479`, `ApplyNetSystem.cs:545/556/650`, `ApplyAreasSystem.cs:171`), and `ToolClearSystem` removes it on discard.
`Error` is another zero-size tag (`src/Game/Game.Tools/Error.cs:7`), and the only thing `ToolBaseSystem` queries for it (`m_ErrorQuery`, `ToolBaseSystem.cs:313`).
Verdict: this file's own earlier claim that `ValidationSystem` adds the tag is wrong, re-derived from the decompile under ticket 13 and corrected here.
The tag is added at `src/Game/Game.Tools/ValidationSystem.cs:468` and removed at `:431`, both inside the nested `public class Components : GameSystemBase` declared at `:321` — a **separate** system registered `UpdateAfter<ValidationSystem.Components, ValidationSystem>(ModificationEnd)` (`SystemOrder.cs:265`), against `ValidationSystem`'s own `UpdateAt` at `:264`.
`ValidationSystem` produces the `ErrorData` records and tags nothing.
The distinction bites exactly once, on a mod anchoring to `ValidationSystem`, and `placement-definitions.md` carries that case.
`ErrorType` enumerates thirty causes plus `None` and `Count` (`src/Game/Game.Tools/ErrorType.cs:3-37`) and `ErrorSeverity` six levels — `None`, `Override`, `Warning`, `Error`, `Cancel`, `CancelError` (`src/Game/Game.Tools/ErrorSeverity.cs:3-11`) — carried on `ErrorData { m_TempEntity, m_PermanentEntity, m_Position, m_ErrorType, m_ErrorSeverity }` (`src/Game/Game.Tools/ErrorData.cs:6-17`).

**`GetAllowApply()` is the gate, and it has a second clause tools forget.**
The base implementation returns false when errors exist and `ignoreErrors` is off, and _also_ returns false when `m_OriginalDeletedSystem.GetOriginalDeletedResult(0)` is true (`ToolBaseSystem.cs:533-540`).
`OriginalDeletedSystem` walks every `Temp` and sets a flag when `temp.m_Original` carries `Deleted` or no longer exists at all, keeping a two-frame ring so `GetOriginalDeletedResult(0)` covers last frame and this one (`src/Game/Game.Tools/OriginalDeletedSystem.cs:31-49/100-111/113-128`); it runs at `PreTool` (`SystemOrder.cs:698`).
Two of Traffic's tools override `GetAllowApply` to drop that clause, commented `// workaround for vanilla OriginalDeletedSystem result (fix bug)`, substituting `_toolFeedbackQuery.IsEmptyIgnoreFilter && (m_ToolSystem.ignoreErrors || m_ErrorQuery.IsEmptyIgnoreFilter)` (`Traffic/Code/Tools/LaneConnectorToolSystem.cs:551-555`, `Traffic/Code/Tools/PriorityToolSystem.cs:257-261`).
The mechanism is exactly what the decompile shows; whether it is a game bug or a consequence of Traffic's own `Temp` entities pointing at originals the check considers gone is not something the sources settle, and the comment is the mod's claim rather than an established fact.

**The canonical loop** is `BulldozeToolSystem.OnUpdate` (`src/Game/Game.Tools/BulldozeToolSystem.cs:1510-1594`): a `switch` over a private `State` enum (`Default`, `Applying`, `Waiting`, `Confirmed`, `Cancelled`), with the pre-switch guard `if (m_State == State.Applying && !applyAction.enabled)` resetting to `Default`, setting `ApplyMode.Clear` and calling `DestroyDefinitions` (`:1525-1531`).
Its `Update(JobHandle, bool fullUpdate)` (`:1686-1737`) is the readable statement of the three modes: no raycast hit at all → `Clear` and destroy the definitions; first control point → `Clear` and rebuild; subsequent frames → `None` unless the control point actually moved, and `Clear` again when it did.
`Apply(JobHandle)` (`:1618-1645`) checks `GetAllowApply()`, plays a sound, sets `ApplyMode.Apply`, clears its control points and calls `DestroyDefinitions` — so a commit sweeps the definitions that produced the previews being committed, and creates none to replace them.
Verdict: this file's own earlier claim that a commit destroys the definitions "in the same frame the `Apply*System` family consumes them" is wrong, re-derived under ticket 13 and corrected here.
The query shape rules it out: `GetDefinitionQuery()` excludes `Updated` (`ToolBaseSystem.cs:689-692`) and `CleanUpSystem` strips `Updated` at `Cleanup` (`src/Game/Game.Common/CleanUpSystem.cs:48-56`), so the sweep matches the _previous_ frame's definitions and never the ones just emitted.
Exactly one generation is alive at a time; `placement-definitions.md` owns the lifetime.
`DestroyDefinitions(EntityQuery, ToolOutputBarrier, JobHandle)` is a protected helper on the base class, an `IJobChunk` that destroys every entity in the group through the barrier's parallel writer (`ToolBaseSystem.cs:506-519`), and `GetDefinitionQuery()` is the query it expects: `{CreationDefinition}` with `Exclude<Updated>` (`:689-692`).

`ToolOutputBarrier : SafeCommandBufferSystem` plays back at the end of `ToolUpdate` (`src/Game/Game.Tools/ToolOutputBarrier.cs:6`, `SystemOrder.cs:695`); `ecs-in-this-game.md:283-284/325` records its window and that 14 of 20 corpus repositories use it.

Rots: `TempFlags`, `ErrorType` and `ErrorSeverity` member sets — re-read `src/Game/Game.Tools/`.

### Snapping is one formula plus a mask the tool declares

`public static Snap GetActualSnap(Snap selectedSnap, Snap onMask, Snap offMask) => (selectedSnap | ~offMask) & onMask` (`src/Game/Game.Tools/ToolBaseSystem.cs:467-470`), with a protected instance overload reading the tool's own `selectedSnap`, `m_SnapOnMask` and `m_SnapOffMask` (`:472-475`).
Read it as three cases.
A flag absent from `onMask` is never on, whatever the user chose.
A flag in `onMask` but not in `offMask` is _always_ on — it cannot be switched off, because `~offMask` supplies it regardless of `selectedSnap`.
A flag in both masks is the only kind the user's `selectedSnap` decides.
So `GetAvailableSnapMask(out Snap onMask, out Snap offMask)` (`:445-449`, base returns `None`/`None`) is simultaneously the declaration of what exists and of what is mandatory, and returning the same flag in both masks is what makes it a user-facing toggle.
`selectedSnap` defaults to `Snap.All` in `OnCreate` (`:309`).

`Snap : uint` (`src/Game/Game.Tools/Snap.cs:6-30`) — `ExistingGeometry = 1` (`:8`), `CellLength = 2` (`:9`), `StraightDirection = 4` (`:10`), `NetSide = 8` (`:11`), `NetArea = 0x10` (`:12`), `OwnerSide = 0x20` (`:13`), `ObjectSide = 0x40` (`:14`), `NetMiddle = 0x80` (`:15`), `Shoreline = 0x100` (`:16`), `NearbyGeometry = 0x200` (`:17`), `GuideLines = 0x400` (`:18`), `ZoneGrid = 0x800` (`:19`), `NetNode = 0x1000` (`:20`), `ObjectSurface = 0x2000` (`:21`), `Upright = 0x4000` (`:22`), `LotGrid = 0x8000` (`:23`), `AutoParent = 0x10000` (`:24`), `PrefabType = 0x20000` (`:25`), `ContourLines = 0x40000` (`:26`), `Distance = 0x80000` (`:27`), `None = 0` (`:28`), `All = uint.MaxValue` (`:29`).

`public const Snap kSnapAllIgnoredMask = Snap.AutoParent | Snap.PrefabType | Snap.ContourLines` (`ToolBaseSystem.cs:98`) — the three flags an "all snapping" UI toggle is meant to leave alone. Grepping `src/Game/` returns that declaration and no consumer, so it exists for the frontend and for mods.

The UI reads all of this through four bindings on `ToolUISystem`: `tool.availableSnapMask` and `tool.allSnapMask` both call `activeTool.GetAvailableSnapMask` (`src/Game/Game.UI.InGame/ToolUISystem.cs:126-143`), `tool.selectedSnapMask` reads `activeTool.selectedSnap` (`:144`), and the `tool.setSelectedSnapMask` trigger writes it back (`:170`).
Unlike `selectTool` and `selectToolMode`, these four go through the `ToolBaseSystem` virtuals and therefore work for a mod tool unchanged.

**Declarative use in the corpus is thin and unanimous.** Three tools override `GetAvailableSnapMask`, and all three do the same thing — add `Snap.ContourLines` to both masks, gated on a named competitor mod not being loaded: `LineTool-CS2/Code/Systems/LineToolSystem.cs:499-510`, `Tree_Controller/Tree_Controller/Tools/TreeControllerTool.cs:121-131`, `Water_Features/Water_Features/Tools/CustomWaterToolSystem.cs:343-351`.
Adding a flag to both masks is what makes it a toggle rather than a forced behaviour, and all three want a toggle.

**By hand means replacing a private method.** The vanilla object tool's snapping lives in `private JobHandle SnapControlPoint(JobHandle)` (`src/Game/Game.Tools/ObjectToolSystem.cs:4196`), called from seven places in its update (`:3726/3744/3786/3887/3920/3980/4092`); its mask comes from a `GetAvailableSnapMask` override that branches on the prefab's `BuildingData`, `AssetStampData` and `PlaceableObjectData` before delegating to a static overload (`:3554-3568`).
Because `SnapControlPoint` is private, a mod that wants different snapping for the vanilla tool patches it: `CS2-Platter/Platter/Patches/ToolSystemPatch.cs:101-178` prefixes it, schedules `AdhocParcelSnapJob` against zone, net and parcel quadtrees, registers the search-tree readers, completes, assigns `__result` and returns `false`; the same file postfixes `ToolBaseSystem.GetActualSnap` to OR in `Snap.ContourLines` (`:32-50`).
A mod's _own_ tool has no such problem: it calls `GetActualSnap()` itself and schedules whatever snap job it likes, which is what `BulldozeToolSystem.SnapControlPoints` does (`src/Game/Game.Tools/BulldozeToolSystem.cs:1741-1760`).
`ExtraDetailingTools/MOD/ExtraSnap/ExtraSnap.cs:50-171` generalises this into an `ExtraSnapBase<TTool, TSnap>` where `TTool : ToolBaseSystem` and `TSnap` is a `[Flags]` enum the static constructor validates as `uint`-backed (`:58-77`), with abstract `InitializeRaycast()` (`:159`) and `SnapControlPoint(JobHandle)` (`:161`) and a `GetActualToolSnap()` helper that calls the tool's own `GetAvailableSnapMask` and feeds `ToolBaseSystem.GetActualSnap` (`:164-168`).

Rots: the `Snap` member set and `kSnapAllIgnoredMask`'s contents — re-read `src/Game/Game.Tools/Snap.cs` and `ToolBaseSystem.cs:98`.

### Overlays: one buffer, one dependency contract, two places to draw from

`OverlayRenderSystem.GetBuffer(out JobHandle dependencies)` lazily allocates its persistent lists and returns a `Buffer` struct plus the accumulated writer handle; `AddBufferWriter(JobHandle)` combines a handle back in (`src/Game/Game.Rendering/OverlayRenderSystem.cs:592-627`).
The system is registered at `SystemUpdatePhase.Rendering`, right after `AreaRenderSystem` (`src/Game/Game.Common/SystemOrder.cs:686-687`).
`Buffer` is a `struct` holding four `NativeList`s, a `NativeValue<BoundsData>` and two terrain-derived floats (`OverlayRenderSystem.cs:106-120`), with `DrawCircle`, `DrawLine`, `DrawDashedLine`, `DrawCurve`, `DrawDashedCurve`, `DrawCustomMesh` and `DrawText` in plain and styled overloads (`:122-253`), the styled ones taking `outlineColor`, `fillColor`, `outlineWidth` and `StyleFlags { Grid = 1, Projected = 2, DepthFadeBelow = 4 }` (`:98-104`).

Rots: the draw-method set on `Buffer` and the `StyleFlags` members — re-read `src/Game/Game.Rendering/OverlayRenderSystem.cs:98-253`.

The contract is: take the buffer and its handle, combine that handle into your job's input dependency, schedule, and hand the resulting handle back with `AddBufferWriter`.
`Anarchy/Anarchy/Systems/AnarchyComponentsTool/AnarchyComponentsToolSystem.cs:307/315` is the compact form, five times over in one method; `CS2-NetworkTools/NetworkTools.Mod/Systems/Rendering/OverlaySystem.cs:77/99` and `:105/126` and `:132/149` the same shape per drawable kind; `CS2-MoveIt/Code/MoveIt/Overlays/MIT_OverlaySystem.cs:146/170` likewise.

Two placements, both in wide use.
From the tool's own `OnUpdate`, which keeps the draw next to the state it draws: `BetterBulldozer/BetterBulldozer/Tools/RemoveVehiclesCimsAndAnimalsTool.cs:200-205`, the Anarchy tool above.
From a dedicated system registered at `SystemUpdatePhase.Rendering`: `CS2-MoveIt/Code/MoveIt/Mod.cs:88`, `CS2-NetworkTools/NetworkTools.Mod/NetworkToolsMod.cs:75-77` (three of them), `CS2-Platter/Platter/PlatterMod.cs:212`, `ExtraDetailingTools/EDT.cs:70`, `FindIt-CSII/FindIt/Mod.cs:68`.
One of those registrations anchors rather than merely places: `Traffic/Code/Mod.cs:73` is `UpdateAfter<ToolOverlaySystem, AreaRenderSystem>(SystemUpdatePhase.Rendering)`, putting the mod's overlay system after `AreaRenderSystem` — which is exactly where the vanilla `OverlayRenderSystem` itself sits (`SystemOrder.cs:686-687`).
`survey-mods-techniques.md:272` names the same file.

**One corpus shortcut worth knowing about rather than copying.** `LineTool-CS2/Code/Systems/LineToolSystem.cs:618` calls `GetBuffer(out var _)` once in `OnCreate`, caches the `Buffer` struct in a field, discards the dependency handle, never calls `AddBufferWriter`, and draws from the main thread inside `OnUpdate` (`:873/878`).
It works because the draws are synchronous and the underlying lists are `Allocator.Persistent`, but the struct also snapshots two terrain-scale floats at construction (`OverlayRenderSystem.cs:622`), so it is a shortcut that depends on the buffer never being reallocated rather than on the documented contract.

### Tooltips are a separate system, in a phase that is a hard requirement

A tool draws no tooltips itself. `TooltipSystemBase : GameSystemBase` (`src/Game/Game.UI.Tooltip/TooltipSystemBase.cs:9`) offers exactly three protected members: `AddGroup(TooltipGroup)`, which rejects a duplicate path with a `Debug.LogError` (`:20-30`); `AddMouseTooltip(IWidget)`, same duplicate check against `mouseGroup.children` (`:32-42`); and `static float2 WorldToTooltipPos(Vector3, out bool onScreen)`, which flips Unity's screen Y for the UI's coordinate space (`:44-50`).

`TooltipUISystem.OnUpdate` clears `groups` and `mouseGroup.children`, drives `SystemUpdatePhase.UITooltip`, then reads the lists back into its widget bindings — and it skips the whole thing when `InputManager.instance.mouseOverUI` or `hideTooltips` is set (`src/Game/Game.UI.Tooltip/TooltipUISystem.cs:46-69`).
`mod-lifecycle-and-ordering.md:267` establishes that this makes `UITooltip` a hard requirement rather than a convention: a `TooltipSystemBase` registered anywhere else writes into a list that has already been consumed.
The mouse group is positioned at `mousePosition + (0, 16)` in flipped screen space and is only emitted when it has children and the mouse is on screen (`TooltipUISystem.cs:14/56-60`).

Widget types available, one file each in `Game.UI.Tooltip`: `StringTooltip` (a `LocalizedString value`, `src/Game/Game.UI.Tooltip/StringTooltip.cs:6-31`), `FloatTooltip`, `IntTooltip`, `NumberTooltip`, `IconTooltip` (the base the string tooltip extends), `LabelIconTooltip`, `NameTooltip`, `NotificationTooltip`, `ProgressTooltip`, `InputHintTooltip`, `ZoningEvaluationTooltip`, plus `TooltipColor` and `TooltipGroup`.

Rots: the widget type names above — re-read `src/Game/Game.UI.Tooltip/`.

The vanilla shape is: construct the widgets once in `OnCreate` with a stable `path` and a `LocalizedString.Id(...)` label, bail out of `OnUpdate` unless the tool you belong to is active, then set values and call `AddMouseTooltip` (`src/Game/Game.UI.Tooltip/AreaToolTooltipSystem.cs:78-95` for construction, `:99-103` for the `m_ToolSystem.activeTool != m_AreaTool` guard, `:155-172` for the adds).
`Traffic/Code/UISystems/LaneConnectorToolTooltipSystem.cs:35-47/101/113-132` is the corpus version of the same shape, including `color = TooltipColor.Success` and `TooltipColor.Warning` on individual tooltips.
14 of 20 repositories ship exactly one `TooltipSystemBase` subclass each — Anarchy, BetterBulldozer, CS2-MoveIt, CS2-NetworkTools, CS2-Platter, CS2-WriteEverywhere, ExtraDetailingTools, FindIt-CSII, LineTool-CS2, NodeController, Recolor, Traffic, Tree_Controller, Water_Features.

### The input actions a tool needs come free, and the vanilla ones are not takeable

`ToolBaseSystem.OnCreate` fetches five action states, keyed by the tool's own type name (`ToolBaseSystem.cs:300-305`):

```
string name = GetType().Name;
m_DefaultApply          = InputManager.instance.toolActionCollection.GetActionState("Apply", name);
m_DefaultSecondaryApply = InputManager.instance.toolActionCollection.GetActionState("Secondary Apply", name);
m_DefaultCancel         = InputManager.instance.toolActionCollection.GetActionState("Cancel", name);
m_MouseApply            = InputManager.instance.toolActionCollection.GetActionState("Mouse Apply", name);
m_MouseCancel           = InputManager.instance.toolActionCollection.GetActionState("Mouse Cancel", name);
```

This runs for a mod tool exactly as for a vanilla one, because it is base-class code.
`toolActionCollection` is `internal` (`src/Game/Game.Input/InputManager.cs:522`, backing field `:232`, loaded from `Resources.Load<UIInputActionCollection>("Input/Tool Input Actions")` at `:1303`), so a mod cannot call it — but it does not need to.
The three protected properties `applyAction`, `secondaryApplyAction`, `cancelAction` return those states, falling back to the defaults when no override is set (`ToolBaseSystem.cs:188-192`).

**What `GetActionState` returns is a per-source wrapper over the shared action, not a copy of it.**
`UIInputActionCollection.GetActionState(actionName, source)` finds the `UIBaseInputAction` whose `aliasName` matches and calls `GetState(actionName + " (" + source + ")")` (`src/Game/Game.Input/UIInputActionCollection.cs:11-19`).
`UIInputAction.GetState(string source)` resolves the underlying `ProxyAction` and wraps it in a `State` carrying its own `InputActivator` and `DisplayNameOverride` (`src/Game/Game.Input/UIInputAction.cs:159-164`).
`State` implements `IProxyAction` (`src/Game/Game.Input/IProxyAction.cs:6-25`: `shouldBeEnabled`, `enabled`, `onInteraction`, `WasPressedThisFrame`, `WasReleasedThisFrame`, `IsPressed`, `IsInProgress`, `GetMagnitude`, `ReadValue<T>`) and gates every read on its own `shouldBeEnabled` (`UIInputAction.cs:62-114`), while `shouldBeEnabled` toggles that activator alone (`:25-39`).
`ProxyAction.UpdateState` then ORs every activator's device mask together and subtracts every blocked barrier's (`src/Game/Game.Input/ProxyAction.cs:524-575`).
The practical upshot: a mod tool's `applyAction` **is** the user's Apply binding, follows their rebinds automatically, and enabling it affects nobody else, because the tool holds its own activator on the shared action.

**Why the raw action cannot be taken instead.**
`InputManager.FindAction(string mapName, string actionName)` is public (`InputManager.cs:556`) and `InputManager.kToolMap` is the constant `"Tool"` (`:190`), so `FindAction("Tool", "Apply")` compiles — but `ProxyAction.shouldBeEnabled`'s setter opens `if (isBuiltIn) throw new Exception("Built-in actions can not be enabled directly")` (`ProxyAction.cs:322-347`), and the public `InputActivator` constructor throws `ArgumentException("Activator can not be created for built-in action")` on the same condition (`src/Game/Game.Input/InputActivator.cs:55-69`).
The only way past it is the `internal InputActivator(bool ignoreIsBuiltIn: true, ...)` overload (`:60`), which is precisely what `UIInputAction.State` uses (`UIInputAction.cs:58`).
`isBuiltIn` resolves through `ProxyAction.isBuiltIn` (`ProxyAction.cs:221`) and `ProxyComposite.isBuiltIn => m_Source.builtIn` (`src/Game/Game.Input/ProxyComposite.cs:33`) to a `CompositeInstance.builtIn` flag deserialized from the input asset (`src/Game/Game.Input/CompositeInstance.cs:128/261`), so **which** actions carry it is asset data outside `src/` and cannot be read here.
What can be read is that the base class routes around the check for every tool, and that of the 30 corpus files naming `applyAction` or `cancelAction`, every live one reads the inherited property; the only file reaching for the raw action through reflection is dead code (`ExtraDetailingTools/MOD/Patches/NetToolSystem.cs:42-105`, entirely commented out).

**A tool's own extra actions are declared as settings, and they live in the mod's own map.**
`ModSetting.GetAction(string name)` is `InputManager.instance.FindAction(id, name)`, where `id` is the mod's settings id (`src/Game/Game.Modding/ModSetting.cs:289-292`), and `RegisterKeyBindings()` builds that map from the class-level `SettingsUIInputActionAttribute` declarations and the property-level `ProxyBinding` defaults (`:141`).
Those actions are not built-in, so `shouldBeEnabled` works on them normally.
`RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderToolSystem.cs:62-64` is the readable example: three actions pulled from settings, two of them named `"RoadBuilderApply"` and `"RoadBuilderCancel"`.

**Mimicking, and what it is actually for.**
Making a mod-declared action fire on whatever button the user rebound the vanilla Apply to needs one of two moves.
Declarative: `[SettingsUIBindingMimic(InputManager.kToolMap, "Secondary Apply")]` alongside `[SettingsUIMouseBinding(...)]` and `[SettingsUIHidden]` on a `ProxyBinding` property (`Anarchy/Anarchy/Settings/AnarchyModSettings.cs:212-216`, also `[SettingsUIBindingMimic(InputManager.kShortcutsMap, "Change Elevation")]` at `:279/288`; `CS2-MoveIt/Code/MoveIt/Settings/Settings.cs:207-217` for `"Apply"` and `"Cancel"`).
The attribute itself is two readonly strings, `map` and `action` (`src/Game/Game.Settings/SettingsUIBindingMimicAttribute.cs:6-16`).
Imperative: `RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderToolSystem.cs:66-81` looks up `InputManager.instance.FindAction(InputManager.kToolMap, "Apply")`, copies the `.path` and `.modifiers` of its mouse binding onto the mod's own binding, and calls `InputManager.instance.SetBinding(mimicBinding, out _)` (`:80-81`) — twice, for Apply and Cancel.

**Verdict on the survey's framing.** `survey-mods-techniques.md:363` says "Mod tools cannot take over the vanilla Apply/Cancel actions", and presents mimicking as the answer.
At 1.6.0f1 that is half right and misleads on the important half: a mod tool cannot enable the raw `ProxyAction`, but it does not need to, because `ToolBaseSystem` hands it a scoped wrapper over the same action in `OnCreate`, and that wrapper is what the corpus's tools read.
Mimicking is what a mod needs for its _own additional_ actions — a second modifier, a mode toggle, an action it wants to appear in the settings screen — when those should sit on the same physical button as a vanilla one.
Two of twenty repositories use `SettingsUIBindingMimic` at all.

Rots: the five action alias strings (`"Apply"`, `"Secondary Apply"`, `"Cancel"`, `"Mouse Apply"`, `"Mouse Cancel"`) and the `internal` accessibility of `toolActionCollection` — re-read `src/Game/Game.Tools/ToolBaseSystem.cs:300-305` and `src/Game/Game.Input/InputManager.cs:522`.

### Small things that are easy to miss

`ToolSystem` installs a `"Tool"` map barrier blocked whenever the game is loading or not in game/editor, and a per-action mouse barrier that blocks while the pointer is over UI (`src/Game/Game.Tools/ToolSystem.cs:201-222`, refresh at `:280-294`) — so a tool's actions go quiet over the UI without the tool doing anything.
`ToolBaseSystem.ToggleToolOptions(bool)` exists for the tool-options panel to suppress a tool's own actions while a widget has focus (`ToolBaseSystem.cs:397-401`), and `actionsEnabled` additionally goes false whenever `InputManager.instance.hasInputFieldFocus` (`:264-278`).
`static event Action<ProxyAction> ToolBaseSystem.EventToolActionPerformed` fires for any tool action reaching `InputActionPhase.Performed` (`:280/403-409`).

Committing a placement should play the matching UI sound: `ToolUXSoundSettingsData` is a singleton component carrying entity references including `m_BulldozeSound`, `m_PlaceBuildingSound`, `m_PlacePropSound`, `m_NetStartSound`, `m_NetNodeSound` (`src/Game/Game.Prefabs/ToolUXSoundSettingsData.cs:5/17/23/29/59/61`), read from a `GetEntityQuery(ComponentType.ReadOnly<ToolUXSoundSettingsData>())` singleton and played with `AudioManager.PlayUISound(Entity, float volume = 1f)` (`src/Game/Game.Audio/AudioManager.cs:805`).
`BulldozeToolSystem.Apply` picks between two of them on whether anything substantial was demolished (`src/Game/Game.Tools/BulldozeToolSystem.cs:1621-1630`).

A tool can drive the infoview: `UpdateInfoview(Entity prefab)` reads the prefab's `PlaceableInfoviewItem` buffer, sets `infoview` from the first entry and fills `infomodes` from the rest (`ToolBaseSystem.cs:477-504`), and `ToolSystem.ToolUpdate` diffs those against the last frame's after driving `ToolUpdate` (`ToolSystem.cs:328-368`).
`OnStopRunning` clears both, so the infoview follows the tool automatically.

The `require*` properties — `requireZones`, `requireUnderground`, `requirePipelines`, `requireNetArrows`, `requireStopIcons`, `requireAreas`, `requireRoutes`, `requireStops`, `requireNet` (`ToolBaseSystem.cs:158-174`) — are `public … { get; protected set; }` and tell the rendering side what to show while the tool is active; `BulldozeToolSystem.OnUpdate` sets four of them every frame (`BulldozeToolSystem.cs:1512-1523`).
`allowUnderground` is `public virtual bool { get; protected set; }` and `SetUnderground(bool)` an empty virtual, and the UI binds both (`ToolUISystem.cs:155-156`); `Traffic/Code/Tools/LaneConnectorToolSystem.cs:159/228/276/563-564` is the corpus implementation.

### Catalog gaps found

**`Better Bulldozer` demonstrates tool-list reordering, and its entry does not say so.**
`plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md:291` names only its two raycast techniques.
Sentence to add: "Reinserting its two tools at the front of the tool list once loading has completed, which is later than the tool's own creation and therefore wins any race with a mod that reorders at `OnCreate`."
Source: `BetterBulldozer/BetterBulldozer/Systems/BetterBulldozerUISystem.cs:353-372`, a `OnGameLoadingComplete` override on a UI system that removes and reinserts two tool systems it does not own.

**`Recolor` demonstrates the tool that never claims a prefab, and its entry does not say so.**
`mod-catalog.md:251` names only the info-panel base class and the absence of patches.
Sentence to add: "Three tools that decline every prefab — `TrySetPrefab` returns false and `GetPrefab` returns null — so they are reachable only from the mod's own UI and cost the toolbar nothing wherever they sit in the tool list."
Source: `Recolor/Recolor/Systems/Tools/ColorPainterToolSystem.Main.cs:121/124-133`, with the sibling tools at `Systems/Tools/ColorPickerToolSystem.cs:30` and `Systems/Tools/SelectNetLaneFencesToolSystem.cs:22`.

**`Anarchy` demonstrates the subscription form of previous-tool restore, and its entry does not say so.**
`mod-catalog.md:280` names only its raycast postfix.
Sentence to add: "Remembering the previously active tool by subscribing to the tool system's tool-changed event rather than latching it at activation, which is the only form that survives an activation the tool did not initiate."
Source: `Anarchy/Anarchy/Systems/AnarchyComponentsTool/AnarchyComponentsToolSystem.cs:142-149`, with the restore at `:120-123`.

**Not a gap.** The catalog already names Traffic's raycast system in the raycast phase (`mod-catalog.md:133`), Extra Detailing Tools' batched context-keyed raycast (`:108`), Move It's typed wrappers over the game's raycast system (`:58`), Advanced Line Tool's cooperative insertion and previous-tool handback (`:43`), Network Tools' shared tool base and its edge-hit-to-node resolution (`:81-82`), Node Controller's own spatial search in place of the tool raycast (`:97`), Area Bucket's terrain-only cast by narrowing the type mask (`:69`), and Better Bulldozer's two raycast techniques (`:291`).

## Bridge

The ticket named five mechanics topics as expected bridges. All five are supported by the sources, none had to be dropped, and nothing in the sweep produced a sixth strong enough to assert.

- **`placement-definitions`** is the topic every tool that builds anything hands off to. A tool does not mutate the world: it emits `CreationDefinition` entities (`src/Game/Game.Tools/CreationDefinition.cs:5-15`, carrying `m_Prefab`, `m_SubPrefab`, `m_Original`, `m_Owner`, `m_Attached`, `CreationFlags m_Flags`, `m_RandomSeed`) that the `Generate*System` family turns into `Temp` entities during the modification phases and the `Apply*System` family commits at `ApplyTool`. `CreationFlags : uint` has twenty members (`src/Game/Game.Tools/CreationFlags.cs:6-28`): `Permanent`, `Select`, `Delete`, `Attach`, `Upgrade`, `Relocate`, `Invert`, `Align`, `Hidden`, `Parent`, `Dragging`, `Recreate`, `Optional`, `Lowered`, `Native`, `Construction`, `SubElevation`, `Duplicate`, `Repair`, `Stamping`. `prefabs-and-assets.md:428` already establishes that `CreationDefinition.m_Prefab` holds a prefab _entity_ rather than a `PrefabBase`, which is the fact a tool author gets wrong first. The seam runs both ways: `ObjectToolBaseSystem.CreateDefinitions` is the sanctioned producer, and a mod that only wants to change _what_ an existing tool places rewrites the definitions instead of writing a tool at all.
- **`settings-and-input`** owns the declaration side of everything the input section above consumes: `ModSetting.GetAction` resolving into the mod's own action map (`src/Game/Game.Modding/ModSetting.cs:289-292`), `RegisterKeyBindings()` building that map from attributes (`:141`), and `SettingsUIBindingMimicAttribute` (`src/Game/Game.Settings/SettingsUIBindingMimicAttribute.cs:6-16`). A tool needing anything beyond apply, secondary apply and cancel has to go through that topic, and the split is clean: this reference owns the three actions the base class provides, that one owns every action a mod declares.
- **`roads-and-traffic`** is the mechanics area the network half of the raycast surface exists for. `Game.Net.Layer`'s nineteen members are all network layers (`src/Game/Game.Net/Layer.cs:6-29`), `Game.Net.UtilityTypes` filters the utility networks under them (`src/Game/Game.Net/UtilityTypes.cs:6-17`), `TypeMask.Net` and `TypeMask.Lanes` are the two masks a network tool sets, and `NetToolSystem` is the largest tool in the game at 7802 lines. The corpus's two deepest tool state machines are both here — `Traffic/Code/Tools/LaneConnectorToolSystem.cs` and `CS2-NetworkTools/NetworkTools.Mod/Systems/Tools/Base/BaseToolSystem.cs:61` with a family of eight tools under it across two abstract layers — and the lane connector is one of the corpus's two complete parallel raycast pipelines, built because lane connectors are mod-owned entities no vanilla search tree holds.
- **`zoning-buildings-and-land-value`** is reached through `TypeMask.Zones`, `Snap.ZoneGrid`, `Snap.LotGrid`, `Snap.CellLength` and `AreaTypeMask.Lots`, and through `ZoneToolSystem` as the tool that writes zoning. The strongest evidence is a mod: `CS2-Platter` adds placeable parcels holding zone cells, which needs a custom snap job against zone blocks, net edges and other parcels (`CS2-Platter/Platter/Patches/ToolSystemPatch.cs:101-178`) and prefab-data flags chosen so the placement pipeline treats a parcel correctly (`CS2-Platter/Platter/Systems/Parcels/P_ParcelInitializeSystem.cs`, and `prefabs-and-assets.md:428` on the two prefab-entity components the pipeline reads). A tool that places anything on the ground meets that topic's grid.
- **`frontend-and-injection`, in the UI skill**, is the tool options panel and it is a two-sided bridge. The C# side is `ToolUISystem`'s `"tool"` binding group (`src/Game/Game.UI.InGame/ToolUISystem.cs:44/125-187`), and the finding above matters to it: `availableSnapMask`, `allSnapMask`, `selectedSnapMask`, `setSelectedSnapMask`, `color`, `brushSize`, `brushStrength`, `brushAngle`, `undergroundModeSupported`, `undergroundMode` and `activeTool` all go through `ToolBaseSystem` virtuals and work for a mod tool, while `selectTool` and `selectToolMode` are hard-coded to the vanilla tools and do not. The TS side is `moduleRegistry.extend` on two paths, both confirmed in the corpus: `game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.tsx` → `MouseToolOptions` (`Anarchy/Anarchy/UI/src/index.tsx:22/27/41/43`, `BetterBulldozer/BetterBulldozer/UI/src/index.tsx:15`, `AreaBucket/UI/area-bucket/src/constants.ts:6`, `CS2-Platter/Platter/UI/src/components/vanilla/Components.tsx:10`) and `game-ui/game/components/tool-options/tool-options-panel.tsx` → `useToolOptionsVisible`, which is what makes the panel appear at all for a tool the game does not know (`Anarchy/.../index.tsx:34`, `BetterBulldozer/.../index.tsx:17`, `AreaBucket/UI/area-bucket/src/constants.ts:5`, `CS2-Platter/Platter/UI/src/index.tsx:24`).

Two mechanics topics the ticket did not name have a weaker claim and are recorded rather than asserted: `city-services-and-coverage` through `PlaceableInfoviewItem` and the infoview a tool activates (`ToolBaseSystem.cs:477-504`), and `environment-and-pollution` through `TerrainToolSystem`, `WaterToolSystem`, `TypeMask.Terrain`, `TypeMask.Water`, `TypeMask.WaterSources` and `Snap.Shoreline`. Neither was swept, so neither is claimed.
(This file first named that second topic `terrain-and-water`, which is not a reference in the approved structure; `environment-and-pollution` claims landscaping, surfaces and terrain in its boundary. Corrected under ticket 13.)

## Dead ends

- **`kSnapAllIgnoredMask` has no consumer in `src/Game/`.** A grep across the whole decompile returns only its declaration (`src/Game/Game.Tools/ToolBaseSystem.cs:98`). It is public API for the frontend and for mods, and its meaning has to be inferred from its name and contents rather than read from a call site.
- **Whether the vanilla `"Tool"` map's Apply, Secondary Apply and Cancel are built-in actions cannot be proved from `src/`.** The `builtIn` flag is deserialized from the input asset (`src/Game/Game.Input/CompositeInstance.cs:128/261`), which is not decompiled source. Everything downstream of the flag is readable and stated above; the flag's value for a specific action is not.
- **No mod in the corpus overrides `GetUIModes`.** Grepped across all 20 repositories: zero hits. `uiModeIndex` fares slightly better with two overrides, treated in the findings above. So there is no worked example of a mod tool publishing modes to the vanilla mode selector, which is consistent with `SelectToolMode` being unable to set them back.
- **`ToolReadyBarrier` has zero corpus uses**, already recorded at `ecs-in-this-game.md:335`; nothing in the tool sweep changed that.
- **The editor tool list is a different mechanism and was not pursued.** `EditorToolUISystem.tools` is a plain array that two mods resize and append to (`CS2-WriteEverywhere/BelzontWE/Systems/WEMainUISystem.cs:29-35`, `RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderToolSystem.cs:88-99`), and the appended item is an `EditorTool`-shaped object rather than a `ToolBaseSystem`. It shares no code with `ToolSystem.tools` and belongs to whichever topic owns the editor, if any does.
- **`ExtraDetailingTools/MOD/Patches/NetToolSystem.cs:42-105` is entirely commented out.** It reads as an attempt to reach `applyActionOverride`, `secondaryApplyActionOverride` and `cancelActionOverride` on a vanilla tool through `Traverse`, and it is the only corpus material touching those three properties. Because it is dead code it proves nothing about whether the approach works.
- **`ToolSystem`'s four events are public delegate fields, not events**, so nothing prevents a mod from assigning over another mod's subscription. One corpus file uses `Delegate.Combine` explicitly rather than `+=` (`LineTool-CS2/Code/Systems/LineToolUISystem.cs:58`); everything else uses `+=`. No mod was observed clobbering another, so the hazard is real in the type system and unobserved in practice.
