# Custom tools

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

How to build a tool that behaves like a vanilla one: it claims the cursor, previews what it would do, and commits on click.

What a tool ultimately emits — the creation definitions the game turns into entities — is [`placement-definitions`](../placement-definitions/placement-definitions.md), and a mod that only wants to change _what_ an existing tool places rewrites those definitions instead of writing a tool at all.
[`mod-lifecycle-and-ordering`](../mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md) owns which phase a system runs in, and every phase named below is a decision that reference already settles.
[`ecs-in-this-game`](../ecs-in-this-game/ecs-in-this-game.md) owns the frame-scoped tags and the command-buffer barriers the apply path rides on.
The map editor's toolbar is a separate surface — its entry list shares no code with `ToolSystem.tools` — and adding an entry to it is [registering into the map editor's toolbar](editor-toolbar.md).

## Two base classes, and one thing forces the heavier one

`ToolBaseSystem` is abstract, derives from `GameSystemBase`, implements `IEquatable<ToolBaseSystem>`, and is what every tool derives from.
`ObjectToolBaseSystem` is the only intermediate abstract class the game ships.
The game has eleven concrete tools — nine directly under `ToolBaseSystem`, plus the object tool and the upgrade tool under the heavier base — and the rest of the tools namespace is the consumer half: the apply and generate system families, validation, the clear and apply dispatchers, and the components and enums everything below names.
(VOLATILE: the number of vanilla tools and the split between the two base classes — the vanilla tool registrations in the game's system-order class for the count, and the tool classes' own declarations in the tools namespace for the split.)

**What the heavier base adds is exactly one protected helper.**
`CreateDefinitions(...)` takes 23 parameters, schedules the game's own definition job wired with dozens of component and buffer lookups, and emits `CreationDefinition` and `ObjectDefinition` entities through `ToolOutputBarrier` — sub-objects, sub-nets, sub-lanes, sub-areas, placeholder resolution and attachment included.
The rest of the class is bookkeeping for that call — four cached system references, the tool output barrier, the object search system, the water system and the terrain system, assigned in its `OnCreate` — apart from `GetFirstNodeIndex`, a `public static` helper six systems outside the class call and that has nothing to do with definitions.
Source: `src/Game/Game.Tools/ObjectToolBaseSystem.cs`.

So the rule is one line: **derive from `ObjectToolBaseSystem` when the tool places objects and wants vanilla-quality previews for free, and from `ToolBaseSystem` for everything else**, including a tool that emits its own definitions by hand.
The heavier base is the minority choice, because most tools select, edit or paint rather than place.

Two shapes are worth adopting.
**Every tool is declared `partial`**, which the source generators require anyway ([`ecs-in-this-game`](../ecs-in-this-game/ecs-in-this-game.md)) and which lets a large tool split across files by concern rather than by nothing.
**A family of tools that all raycast and mark eligibility the same way gets its own abstract layer** between the tools and `ToolBaseSystem`; that is the answer to "I have six tools and they differ only in what they do with the hit", and it scales to eight tools under two such layers without strain.
**An abstract layer with no concrete descendant yet created kills the developer menu's whole `Simulation` tab**: that tab's tool enumeration instantiates every `ToolBaseSystem` descendant through the world with no abstract filter, so the layer is safe only once a concrete tool below it exists — [`debug-menu`](../debug-menu/debug-menu.md) owns the mechanism and the repair.
Source: `src/Game/Game.Debug/DebugSystem.cs`.

## Registration is automatic, and it happens in `OnCreate`

`ToolBaseSystem.OnCreate` ends with `m_ToolSystem.tools.Add(this)`, and nothing else in the game adds to that list.
Registering the tool into `SystemUpdatePhase.ToolUpdate` is _not_ what appends it.
The two are easy to confuse because `UpdateAt<T>` calls `GetOrCreateSystemManaged<T>()`, so the phase registration is usually the thing that constructs the tool and therefore the thing that triggers the append.

The distinction bites twice.
A tool constructed by any other route — `GetOrCreateSystemManaged` from another system's `OnCreate` — is already in the list before the mod registers its phase.
And **a tool whose `OnCreate` override forgets `base.OnCreate()` is in no list at all**, with no action states, no error query and no default snap either.
Source: `src/Game/Game.Tools/ToolBaseSystem.cs`.

`OnCreate` also sets `Enabled = false`, so a tool is inert from birth and the tool system alone turns it on.
Setting it false again yourself is harmless and redundant.

## The lifecycle contract, method by method

`ToolBaseSystem` seals the parameterless `OnUpdate()` and redirects it: it assigns `Dependency = OnUpdate(Dependency)`, then clears its focus-changed and force-update flags.
A tool therefore overrides `protected virtual JobHandle OnUpdate(JobHandle inputDeps)` and can never override the other one.
The seal assigns rather than combines, so returning a handle that does not chain `inputDeps` publishes a dependency missing the fence it carries — which is why the base's own body returns `inputDeps` unchanged.

| Member | Kind | What it is for |
| --- | --- | --- |
| `toolID` | `public abstract string` | Identity for the UI binding and for cross-mod string checks |
| `GetPrefab()` | `public abstract PrefabBase` | The prefab the toolbar should highlight; `null` is legal |
| `TrySetPrefab(PrefabBase)` | `public abstract bool` | Claim or decline a prefab during the tool-list walk |
| `OnUpdate(JobHandle)` | `protected virtual JobHandle` | The per-frame body; return a handle chained onto `inputDeps`, which the seal assigns to `Dependency` outright |
| `InitializeRaycast()` | `public virtual void` | Configure this frame's cast; the base resets every field first |
| `GetRaycastResult(out ControlPoint)` and its `out bool forceUpdate` twin | `protected virtual bool` | Turn a hit into a control point; the seam for substituting your cast |
| `GetAllowApply()` | `protected virtual bool` | Whether the current preview may be committed |
| `GetAvailableSnapMask(out Snap, out Snap)` | `public virtual void` | Which snap flags exist, and which of them are forced on |
| `GetUIModes(List<ToolMode>)` and `uiModeIndex` | `public virtual` | Publish the tool's modes and say which one is current |
| `SetUnderground(bool)`, `ElevationUp()`, `ElevationDown()`, `ElevationScroll()` | `public virtual void` | Empty hooks. The UI's underground toggle calls `SetUnderground`; in game the elevation arrows render for the net tool alone, and the map editor's screen registers an elevation input over the same hooks. `ElevationScroll` is reachable too — the UI binds a `tool.elevationScroll` trigger that dispatches to the active tool — so a mod's own UI can fire it |
| `OnCreate`, `OnStartRunning`, `OnStopRunning` | overridden by the base itself | Call `base`: the tool-specific work lives in the base body |

The game-lifecycle hooks a tool shares with every other system — loading-complete, focus changed, preload, loaded, world ready — behave exactly as [`mod-lifecycle-and-ordering`](../mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md) describes, including which of them disable the system when they throw.

`OnStartRunning` sets the force-update flag and calls the base's action setup; `OnStopRunning` nulls the infoview, clears the infomodes and resets the actions.
**The force-update flag is therefore true for exactly the first frame after activation**, which is how a tool knows to rebuild its preview from nothing rather than diff against last frame's.
Source: `src/Game/Game.Tools/ToolBaseSystem.cs`.

**One block of the base class is unreachable from a mod, and the reason is an access modifier.**
The five action-state fields, the tool-actions enumerable, `actionsEnabled`, and the three virtuals `SetActions()`, `ResetActions()` and `UpdateActions()` are all `private protected` — which C# resolves as protected _and_ internal, meaning derived classes inside the game assembly only.
The consequence is concrete: the vanilla pattern of overriding `UpdateActions()` to recompute `shouldBeEnabled` every frame cannot be copied.
Set `shouldBeEnabled` from `OnStartRunning`, `OnStopRunning` or your own `OnUpdate` instead.
The deferral helper every vanilla `UpdateActions` body wraps itself in is `internal static` as well, so the batching it provides is unavailable too.
What _is_ reachable: `applyAction`, `secondaryApplyAction`, `cancelAction` and their three `*Override` setters are plain `protected`.
Source: `src/Game/Game.Tools/ToolBaseSystem.cs` (every accessibility above) and `src/Game/Game.Input/ProxyAction.cs` (the deferral helper's own).

(VOLATILE: the `private protected` accessibility of the action fields and of `SetActions` / `ResetActions` / `UpdateActions` — `ToolBaseSystem`'s field and action region.)

## Activation, and restoring the tool that was there

Activation is one assignment:

```csharp
m_ToolSystem.activeTool = this;
```

The setter compares against the current value, requires a full update, and fires the tool-changed event.
Deactivation is the same assignment pointing elsewhere; the default tool is the fallback, and is what the pre-deserialize pass forces on load.

**The enable and disable dance belongs to the tool system, not to the tool.**
Its update notices that the active tool differs from the last one, disables the last tool and pumps it once more — that final disabled update is what drives the outgoing tool's `OnStopRunning` — then latches the new tool, enables it, and drives `SystemUpdatePhase.ToolUpdate`.
So a tool registered at `ToolUpdate` runs only while it is the active tool and needs no gate of its own; [`mod-lifecycle-and-ordering`](../mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md) records the same mechanism from the phase side.
Source: `src/Game/Game.Tools/ToolSystem.cs` (the tool update that performs the swap).

**Remember the previous tool by subscribing, not by latching it at your own entry point.**

```csharp
protected override void OnCreate()
{
    base.OnCreate();

    m_PreviousTool = m_DefaultToolSystem;
    m_ToolSystem.EventToolChanged += tool =>
    {
        if (tool != this)
        {
            m_PreviousTool = tool;
        }
    };
}
```

Latching records only what was active at the moment your own `EnableTool` ran.
Subscription is the only form that survives an activation route the tool did not initiate — the toolbar selecting a prefab, another mod switching tools, a save load — and it seeds cleanly, since the default tool is always a valid thing to hand back to.
Restoring is the same assignment, with the fallback kept: `m_ToolSystem.activeTool = m_PreviousTool ?? m_DefaultToolSystem`.
Source: `src/Game/Game.Tools/ToolSystem.cs` (the `activeTool` setter, which fires the event on every route into it).

**The same event is how a helper system gates itself on a tool**, in the shape `Enabled = tool == m_NetToolSystem`.
`ToolSystem` exposes four of them — tool changed, prefab changed, infoview changed, infomodes changed — and **they are plain public delegate fields rather than events**, so a bare `=` compiles and silently wipes every other subscriber, mods and vanilla alike.
Subscribe with `+=`.
Source: `src/Game/Game.Tools/ToolSystem.cs` (the four fields' declarations).

**The vanilla UI cannot activate a mod tool.**
The tool-selection trigger binding takes a string and routes it through a hard-coded switch over the nine vanilla tool ids, whose default arm returns the default tool.
The mode-selection trigger is the same story: it type-tests the active tool against five vanilla tool types, and for anything else only re-pushes the unchanged active tool, so the click changes nothing on screen, even though the binding layer faithfully publishes whatever `GetUIModes` returns for any tool.
So a mod tool may advertise modes to the UI and read `uiModeIndex` back, but nothing vanilla will set them: **activation and mode switching both have to come back through the mod's own C#**, driven by its own binding group rather than by the `"tool"` group.
The mode icon path the UI synthesises is `"Media/Tools/" + toolID + "/" + modeName + ".svg"`, a game-content path rather than a `coui://` host, so a mod tool's mode icons resolve to nothing.
Source: `src/Game/Game.UI.InGame/ToolUISystem.cs` (the two triggers and the synthesised path) and `Cities2_Data/Content/Game/UI/Media/Tools/` (the vanilla tool directories that path resolves into).
[`frontend-and-injection`](../../../../cs2-modding-ui/references/frontend-and-injection/frontend-and-injection.md) owns the panel that replaces all of this, and [`prefabs-and-assets`](../prefabs-and-assets/prefabs-and-assets.md) owns host locations.

(VOLATILE: the hard-coded tool-id switch and the mode type-test — the tool UI system.)

## Position in the tool list decides who claims a prefab

`ToolSystem.ActivatePrefabTool(PrefabBase)` walks `tools` in order, stops at the first tool whose `TrySetPrefab` returns `true` and makes it active; when nobody claims the prefab it falls back to the default tool and returns `false`.
That single loop is the entire meaning of the ordering: **index 0 gets first refusal on every prefab the toolbar hands out.**
A mod tool appended from `OnLoad` lands behind every tool already constructed and so never sees a prefab first.
Source: `src/Game/Game.Tools/ToolSystem.cs` (the ordered walk in `ActivatePrefabTool`).

**`GetPrefab()` and `TrySetPrefab(PrefabBase)` are abstract**, alongside `toolID`, so every tool answers both even when the answers are "nothing" and "no".
A tool reached only from a mod's own UI or a hotkey returns `null` and `false` unconditionally, and then costs the toolbar nothing wherever it sits.
Source: `src/Game/Game.Tools/ToolBaseSystem.cs` (the three abstract members).

**Handing a prefab _to_ the list is the useful direction, and `ActivatePrefabTool` is public.**
Pass it the tool system's current `activePrefab` again to force the walk to re-run after a setting changed what your `TrySetPrefab` would answer; pass a prefab chosen in your own UI to let the list decide who takes it; pass `null` to fall through to the default tool deliberately.
Source: `src/Game/Game.Tools/ToolSystem.cs` (the method's accessibility and its `null` arm).

Only a tool that must claim a prefab kind a vanilla tool already claims has to contend for a position at all.
That is its own procedure — why the order is a live list rather than a fixed one, the reinsertion recipe, and the gate that makes the front of the list safe: [contending for a prefab from the toolbar](toolbar-position.md).

## The raycast: what the vanilla masks can see

`InitializeRaycast()` is not called by the tool.
`ToolRaycastSystem` calls it on the active tool at the top of every frame, builds one `RaycastInput` from its own properties, and hands that to the raycast system.
It is registered at `SystemUpdatePhase.Raycast`, and that phase is driven from the first line of the raycast system's own update, before it performs the cast.

`base.InitializeRaycast()` clears every field before your override gets a turn: it strips sixteen flags from `raycastFlags`, sets `collisionMask` to `OnGround | Overground`, and zeroes the type mask, net layer mask, area type mask, route type, transport type, icon layer mask, utility type mask, ray offset and owner.
**Call it first and then set only what you want.**
Source: `src/Game/Game.Tools/ToolBaseSystem.cs`.

Four control flags are deliberately outside that cleared set — `DebugDisable`, `UIDisable`, `ToolDisable`, `FreeCameraDisable` — because the raycast system manages two of them on the same pass, taking `ToolDisable` from the tool system's full-update flag and `UIDisable` from the input manager's control-over-world flag.
Any one of the four being set makes the input report itself disabled, which the raycast system turns into `TypeMask.None`: a cast that silently returns nothing.

Eleven fields are settable, all plain properties on `ToolRaycastSystem`: `raycastFlags`, `typeMask`, `collisionMask`, `netLayerMask`, `areaTypeMask`, `routeType`, `transportType`, `iconLayerMask`, `utilityTypeMask`, `rayOffset` and `owner`.

### The enums, in full

**`TypeMask : uint`** — what kind of thing may be hit.
`Terrain = 1`, `StaticObjects = 2`, `MovingObjects = 4`, `Net = 8`, `Zones = 0x10`, `Areas = 0x20`, `RouteWaypoints = 0x40`, `RouteSegments = 0x80`, `Labels = 0x100`, `Water = 0x200`, `Icons = 0x400`, `WaterSources = 0x800`, `Lanes = 0x1000`, `None = 0`, `All = uint.MaxValue`.

**`RaycastFlags : uint`** — how the cast behaves and what it descends into.
`DebugDisable = 1`, `UIDisable = 2`, `ToolDisable = 4`, `FreeCameraDisable = 8`, `ElevateOffset = 0x10`, `SubElements = 0x20`, `Placeholders = 0x40`, `Markers = 0x80`, `NoMainElements = 0x100`, `UpgradeIsMain = 0x200`, `OutsideConnections = 0x400`, `Outside = 0x800`, `Cargo = 0x1000`, `Passenger = 0x2000`, `Decals = 0x4000`, `EditorContainers = 0x8000`, `SubBuildings = 0x10000`, `PartialSurface = 0x20000`, `BuildingLots = 0x40000`, `IgnoreSecondary = 0x80000`.

**`CollisionMask`** — which vertical band counts.
`OnGround = 1`, `Overground = 2`, `Underground = 4`, `ExclusiveGround = 8`.
`Underground` alone casts against objects the player cannot see — the going-underground bullet under Input barriers names the members that open the view.

**`Game.Net.Layer : uint`** — the network layer filter.
`Road = 1`, `PowerlineLow = 2`, `PowerlineHigh = 4`, `WaterPipe = 8`, `SewagePipe = 0x10`, `StormwaterPipe = 0x20`, `TrainTrack = 0x40`, `Pathway = 0x80`, `Waterway = 0x100`, `Taxiway = 0x200`, `TramTrack = 0x400`, `SubwayTrack = 0x800`, `Fence = 0x1000`, `MarkerPathway = 0x2000`, `MarkerTaxiway = 0x4000`, `PublicTransportRoad = 0x8000`, `LaneEditor = 0x10000`, `ResourceLine = 0x20000`, `NetFence = 0x40000`, `None = 0`, `All = uint.MaxValue`.

**`AreaTypeMask`** — which kind of area.
`None = 0`, `Lots = 1`, `Districts = 2`, `MapTiles = 4`, `Spaces = 8`, `Surfaces = 0x10`.

**`Game.Net.UtilityTypes : byte`** — the utility networks under the layers.
`None = 0`, `WaterPipe = 1`, `SewagePipe = 2`, `StormwaterPipe = 4`, `LowVoltageLine = 8`, `Fence = 0x10`, `Catenary = 0x20`, `HighVoltageLine = 0x40`, `Resource = 0x80`.

**`IconLayerMask : uint`** — which notification layer.
`None = 0`, `Default = 1`, `Marker = 2`, `Transaction = 4`.
It is **not** a flags enum, despite the name and the power-of-two values.

**`RouteType`** — a plain enum, so this field selects one route kind rather than a set.
`None = -1`, `TransportLine = 0`, `WorkRoute = 1`, `Count = 2`.

**`Game.Prefabs.TransportType`** — also a plain enum.
`None = -1`, then `Bus`, `Train`, `Taxi`, `Tram`, `Ship`, `Post`, `Helicopter`, `Airplane`, `Subway`, `Rocket`, `Work`, `Ferry`, `Bicycle`, `Car` at 0 through 13, and `Count = 14`.
Note the namespace: it is `Game.Prefabs.TransportType`, not the routes namespace, and `System.Net.TransportType` exists as well, so an unqualified `TransportType` in a file carrying both usings is ambiguous.

(VOLATILE: every member of the nine enums above, the eleven settable raycast fields, and any mask combination built from them — the common, net, areas, routes and prefabs namespaces, and the tool raycast system.)

### Reading the result

`GetRaycastResult(out Entity, out RaycastHit)` asks the tool raycast system for its result and rejects a hit whose owner carries `Deleted`.
A `RaycastResult` is a hit plus an owner entity; it accumulates by keeping the nearest and breaking ties on entity index.
The hit carries `m_HitEntity`, `m_Position`, `m_HitPosition`, `m_HitDirection`, `m_CellIndex`, `m_NormalizedDistance` and `m_CurvePosition`.

**The owner is the thing that was hit; the hit entity is the sub-element that took the hit**, and the two differ whenever sub-element, sub-building or lane casting is on.
The vanilla bulldoze tool shows what a tool does with that difference: when the owner is a network node and the hit entity is an edge, it substitutes the edge.
Source: `src/Game/Game.Objects/RaycastJobs.cs` (the owner walked up the ownership chain while the hit entity stays what was struck) and `src/Game/Game.Tools/BulldozeToolSystem.cs` (the substitution).

The three-out overload folds in the original-deleted result and the force-update flag, which is how a tool learns that an entity one of its previews was standing in for has disappeared and the preview has to be rebuilt from scratch.

## Mod-owned entities are invisible to every mask, and there are three ways to see them

The vanilla cast iterates the game's own search trees — zones, areas, nets, objects, routes — so **an entity a mod created and never inserted into one of those is invisible to every mask combination there is.**
Which of the three routes below you want is decided by that sentence: the third is for a vanilla entity the masks merely exclude, the first two for an entity no tree holds.
Source: `src/Game/Game.Common/RaycastSystem.cs` (the five search systems the cast iterates).

**Route one: add a second input to the vanilla raycast system.**
`AddInput(object context, RaycastInput)` and `GetResult(object context)` are both public, results are keyed by the context object reference, and `GetResult` returns the contiguous sub-array of every input that shared it.
A small typed wrapper per cast — one for terrain, one for surfaces — that registers itself as its own context in its constructor and reads back with `GetResult(this)` is the readable shape.
**Register per frame, from the tail of your `InitializeRaycast` override**, because the raycast system clears its input and context lists on every completion pass, and that pass runs from `AddInput`, `GetResult` and its own update alike.
This buys concurrent casts with different masks in one frame, which the single tool raycast system cannot express — but it still only sees what the vanilla trees hold.
Source: `src/Game/Game.Common/RaycastSystem.cs` (the context keying, and the completion pass that clears both lists).

**Route two: a parallel pipeline registered at `SystemUpdatePhase.Raycast`.**
That phase runs inside the vanilla raycast system's update, before the cast, so your result is ready by the time your tool's `OnUpdate` asks for it — which is the whole reason to put it there.
Hold your own input and result in `NativeReference` fields, clear both at the top of each update, run a terrain job when the input asks for terrain, then your own tree jobs, accumulating into a `NativeAccumulator<RaycastResult>`, and expose a set-input and a get-result method.
Reuse `ToolRaycastSystem.CalculateRaycastLine(camera)` for the ray itself rather than deriving it again, and toggle the system's `Enabled` with the tool.

**Mirror the vanilla signatures on the tool side.**
Give the tool a private `GetCustomRaycastResult(out ControlPoint)` pair whose shape matches the base class's `GetRaycastResult` pair, so the state machine below reads identically either way and `OnUpdate` picks one by state.
Where the mod needs several concurrent contexts of its own, the shape that scales is a near-copy of the vanilla raycast system: parallel input and result context lists, native lists of inputs and results, a completion method, and an accumulating result job.

**Route three: widen the vanilla masks and filter the results.**
Cheapest, and the only one that works when the target _is_ a vanilla entity the masks merely exclude: postfix the vanilla tool's `InitializeRaycast` to add what you need, and prefix `GetRaycastResult` to veto the hits you do not want.
[`patching`](../patching/patching.md) owns the technique; the composition hazard belongs here.
**Record whether you were the one that set the flag** — a `[ThreadStatic] bool` written in the postfix — and filter only in that case, so two mods widening the same mask do not each veto the other's hits.
Disambiguate the `GetRaycastResult` overloads with explicit type arrays when you declare the patches, since the name is overloaded several times over and every parameter of every one is `out` — a by-ref type in that array.
Source: `src/Game/Game.Tools/ToolRaycastSystem.cs` (the per-frame mask state every postfix writes) and `src/Game/Game.Tools/ToolBaseSystem.cs` (the overloads a prefix has to tell apart).

## Apply and cancel is a three-value state machine driven from outside the tool

`ApplyMode` has exactly three members: `None`, `Apply`, `Clear`.
`applyMode` is public with a protected setter, and the tool system simply forwards the last tool's value, returning `None` when no tool has ever been active.

`ToolOutputSystem`, registered in the back band of `ToolUpdate`, reads that value and drives one phase or neither: `Clear` drives `SystemUpdatePhase.ClearTool`, `Apply` drives `SystemUpdatePhase.ApplyTool`, `None` drives nothing.
So the three values mean:

- **`Clear`** — throw the preview entities away and rebuild them.
- **`Apply`** — commit them.
- **`None`** — leave last frame's preview exactly as it is, the cheap path a tool sits in while nothing has moved.

`ClearTool` holds one system: it queries every `Temp` entity, adds `Deleted` to it, and for each original carrying `Hidden` adds `BatchesUpdated` and removes `Hidden`, restoring whatever the preview was standing in for.
`ApplyTool` holds nine: the apply dispatcher plus the eight consumers.
**The dispatcher splits the `Temp` set by component, not by flag** — a chunk carrying the warning component gets `Deleted`, one carrying the override component gets `Updated` and `Overridden` — and `TempFlags` enters only to keep a `Cancel`ed entity out of the cost total. `TempFlags.Delete` is read nowhere in it, so setting that flag does not delete anything; the warning component comes from validation.
Source: `src/Game/Game.Tools/ToolApplySystem.cs` (the component tests), `src/Game/Game.Tools/ValidationSystem.cs` (what produces the warning).

`Temp` is the preview tag and [`ecs-in-this-game`](../ecs-in-this-game/ecs-in-this-game.md) owns why nearly every query excludes it.
What a tool needs from it is the flag set.

**`TempFlags : uint`** — `Create = 1`, `Delete = 2`, `IsLast = 4`, `Essential = 8`, `Dragging = 0x10`, `Select = 0x20`, `Modify = 0x40`, `Regenerate = 0x80`, `Replace = 0x100`, `Upgrade = 0x200`, `Hidden = 0x400`, `Parent = 0x800`, `Combine = 0x1000`, `RemoveCost = 0x2000`, `Optional = 0x4000`, `Cancel = 0x8000`, `SubDetail = 0x10000`, `Duplicate = 0x20000`.

`Hidden` is a zero-size tag put on the **original** entity so the preview can stand in for it: the generation systems add it, the apply family removes it on commit, and the clear system removes it on discard.
`Error` is another zero-size tag, added in `ModificationEnd` by `ValidationSystem.Components` — a separate system spliced after `ValidationSystem`, which produces the error records but tags nothing itself — and it is the only thing `ToolBaseSystem` keeps a query for.
Behind that tag is an error record naming a cause and a severity, and the severity is what decides whether the validation ends in the blocking tag at all; [`placement-definitions`](../placement-definitions/placement-definitions.md) owns the record, its causes, the severity levels and the error prefabs that decide which of them are raised, which is also where the suppression technique lives.

(VOLATILE: the `TempFlags` member set — the tools namespace.)

**`GetAllowApply()` is the gate, and it has a second clause tools forget.**
The base returns false when errors exist and the tool system's ignore-errors flag is off, and _also_ returns false when the original-deleted system reports a result for the current window.
That system walks every `Temp` and sets a flag when the original carries `Deleted` or no longer exists at all, keeping a two-frame ring so the first index covers last frame and this one; it runs at `PreTool`.
A tool whose previews point at originals that legitimately vanish therefore refuses to apply.
**Override `GetAllowApply()` and keep only the error clause** when your tool does that deliberately; the second clause is the one you are dropping, and you drop it for the whole tool.
Source: `src/Game/Game.Tools/ToolBaseSystem.cs` (the two clauses), `src/Game/Game.Tools/OriginalDeletedSystem.cs` (the two-frame ring) and `src/Game/Game.Common/SystemOrder.cs` (its `PreTool` registration).
(UNVERIFIED: whether that refusal is a game bug or the intended consequence of pointing `Temp` at originals the check considers gone — watching the check fire in a running game against a tool that does it deliberately would settle it.)

**The canonical loop is the vanilla bulldoze tool**, and it is worth reading before writing your own.
It is a switch over a private state enum — default, applying, waiting, confirmed, cancelled — with a pre-switch guard that resets to default, sets `ApplyMode.Clear` and destroys the definitions whenever the state is "applying" and the apply action has stopped being enabled.
Its update helper is the readable statement of the three modes:

- no raycast hit at all → `Clear`, and destroy the definitions;
- the first control point → `Clear`, and rebuild;
- subsequent frames → `None`, unless the control point actually moved, and `Clear` again when it did.

Source: `src/Game/Game.Tools/BulldozeToolSystem.cs` (the state machine and its update helper).

Its apply path checks `GetAllowApply()`, plays a sound, sets `ApplyMode.Apply`, clears its control points and destroys the definitions — so **committing sweeps away the definitions that produced the previews being committed, and creates none to replace them.**
`DestroyDefinitions(EntityQuery, ToolOutputBarrier, JobHandle)` and the `GetDefinitionQuery()` it expects are both protected on the base class, and the tool's whole share of the mechanism is where it calls them: the pre-switch guard, the lost-raycast branch and the apply path above — each pairing the call with the `ApplyMode` it sets — plus the head of its own definition-update helper, which destroys before it re-creates.
Source: `src/Game/Game.Tools/BulldozeToolSystem.cs` (the apply path and the four call sites) and `src/Game/Game.Tools/ToolBaseSystem.cs` (the two protected helpers).
[`placement-definitions`](../placement-definitions/placement-definitions.md) owns what that query matches, why the sweep runs a frame behind, and who collects a definition a tool leaves behind.

What goes _into_ a definition, and what the generation systems make of it, is [`placement-definitions`](../placement-definitions/placement-definitions.md).

## Snapping is one formula plus a mask the tool declares

```csharp
public static Snap GetActualSnap(Snap selectedSnap, Snap onMask, Snap offMask)
    => (selectedSnap | ~offMask) & onMask;
```

A protected instance overload reads the tool's own selected snap and its two masks, and `selectedSnap` defaults to `Snap.All`.
Read the formula as three cases:

- a flag absent from the on-mask is never on, whatever the user chose;
- a flag in the on-mask but not in the off-mask is **always** on, because the complement supplies it regardless of the selection;
- a flag in **both** masks is the only kind the user's selection decides.

So `GetAvailableSnapMask(out Snap onMask, out Snap offMask)` is simultaneously the declaration of what exists and of what is mandatory, and **returning the same flag in both masks is what makes it a user-facing toggle**.
That is the shape a tool almost always wants; the base returns `None` and `None`, meaning "no snapping at all".
Source: `src/Game/Game.Tools/ToolBaseSystem.cs`.

**`Snap : uint`** — `ExistingGeometry = 1`, `CellLength = 2`, `StraightDirection = 4`, `NetSide = 8`, `NetArea = 0x10`, `OwnerSide = 0x20`, `ObjectSide = 0x40`, `NetMiddle = 0x80`, `Shoreline = 0x100`, `NearbyGeometry = 0x200`, `GuideLines = 0x400`, `ZoneGrid = 0x800`, `NetNode = 0x1000`, `ObjectSurface = 0x2000`, `Upright = 0x4000`, `LotGrid = 0x8000`, `AutoParent = 0x10000`, `PrefabType = 0x20000`, `ContourLines = 0x40000`, `Distance = 0x80000`, `None = 0`, `All = uint.MaxValue`.

`kSnapAllIgnoredMask` is a public constant equal to `AutoParent | PrefabType | ContourLines`: the three flags an "all snapping" toggle is meant to leave alone.
The tool UI system applies it to build `allSnapMask` — the tool's user-selectable set minus those three — and the panel's "All" button toggles exactly that set, leaving contour lines a control of their own.
(VOLATILE: the `Snap` member set and the contents of `kSnapAllIgnoredMask` — the snap enum and the base tool class.)

Four UI bindings read all of this: two that call `GetAvailableSnapMask`, one that reads the selected snap, and a trigger that writes it back.
Unlike tool selection and mode selection, **these four go through the base-class virtuals and therefore work for a mod tool unchanged.**
Source: `src/Game/Game.UI.InGame/ToolUISystem.cs`.

**Snapping by hand is a job you schedule yourself.**
The vanilla object tool's snapping lives in a private method called from seven places across its cancel, apply and update paths, so a mod that wants different snapping _for that vanilla tool_ has to prefix it: schedule your own job against the zone, net and whatever other quadtrees you need — combining the handle the method was given into the schedule — register the search-tree readers, assign the scheduled handle as the result and return false: the vanilla method does exactly that and never completes, its callers chaining the handle it returns, and [`patching`](../patching/patching.md) states the same rule for every prefix that schedules.
Source: `src/Game/Game.Tools/ObjectToolSystem.cs`.
Your own tool has no such problem: call `GetActualSnap()`, branch on the flags it returns and schedule whatever snap job you like, which is exactly what the vanilla bulldoze tool does.
Where a mod carries several tools that all snap, the generalisation that pays is a base class parameterised on the tool type and on a flags enum of the mod's own snap kinds, with abstract raycast-initialisation and snap methods and a helper that feeds the tool's own `GetAvailableSnapMask` into `GetActualSnap`.

The grids behind `ZoneGrid`, `LotGrid` and `CellLength` are [`zoning-buildings-and-land-value`](../../mechanics/zoning-buildings-and-land-value/zoning-buildings-and-land-value.md); the network flags are [`roads-and-traffic`](../../mechanics/roads-and-traffic/roads-and-traffic.md).

## Overlays: one buffer, one dependency contract, two places to draw from

`OverlayRenderSystem.GetBuffer(out JobHandle dependencies)` lazily allocates its persistent lists and returns a `Buffer` struct plus the accumulated writer handle; `AddBufferWriter(JobHandle)` combines your handle back in.
The system is registered at `SystemUpdatePhase.Rendering`, immediately after the area render system.

`Buffer` is a struct holding four native lists, a bounds value and two terrain-derived floats, with `DrawCircle`, `DrawLine`, `DrawDashedLine`, `DrawCurve`, `DrawDashedCurve`, `DrawCustomMesh` and `DrawText` in plain and styled overloads.
The styled ones take an outline colour, a fill colour, an outline width and `StyleFlags { Grid = 1, Projected = 2, DepthFadeBelow = 4 }`.

(VOLATILE: the draw-method set on `Buffer` and the `StyleFlags` members — the overlay render system.)

The contract is four steps, and every draw site repeats them:

```csharp
OverlayRenderSystem.Buffer buffer = m_OverlayRenderSystem.GetBuffer(out JobHandle bufferDeps);
job.m_OverlayBuffer = buffer;

JobHandle handle = job.Schedule(JobHandle.CombineDependencies(Dependency, bufferDeps));

m_OverlayRenderSystem.AddBufferWriter(handle);
Dependency = handle;
```

Two placements are both in wide use and the choice is about cohesion rather than correctness.
From the tool's own `OnUpdate`, which keeps the draw next to the state it draws — there the last step is `return handle;` instead, since the seal assigns `Dependency` from what the override returns and the block's own assignment is dead.
Or from a dedicated system registered at `Rendering` — and anchoring that system after the area render system puts it exactly where the vanilla overlay system itself sits, which is the placement to copy when order against vanilla overlays matters.

**One shortcut is worth recognising rather than copying.**
Taking the buffer once in `OnCreate`, caching the struct in a field, discarding the dependency handle and drawing from the main thread works only while no other overlay job is in flight.
The draw methods append with a plain list add and read-modify-write a shared bounds value, and the handle that shortcut discards is the accumulated one every other writer registered.
Nothing drains those writers until the overlay system's own update at `Rendering`, and the safety system that would have caught the overlapping write is compiled out of this build, so it corrupts a frame's overlay quietly rather than throwing.
The struct also snapshots two terrain-scale floats at construction, so a cached copy carries whatever the terrain scale was when it was taken.
Source: `src/Game/Game.Rendering/OverlayRenderSystem.cs` (everything above except the compiled-out safety system, which is an absence no single file shows).

## Tooltips are a separate system, in a phase that is a hard requirement

A tool draws no tooltips itself.
`TooltipSystemBase` offers exactly three protected members: `AddGroup(TooltipGroup)`, which rejects a duplicate path with a logged error; `AddMouseTooltip(IWidget)`, with the same duplicate check against the mouse group's children; and a static world-to-tooltip-position helper that flips Unity's screen Y into the UI's coordinate space.

The tooltip UI system clears its group list and the mouse group's children, drives `SystemUpdatePhase.UITooltip`, then reads the lists back into its widget bindings — which is why that phase is a hard requirement rather than a convention, as [`mod-lifecycle-and-ordering`](../mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md) records.
It skips the whole pass while the pointer is over UI or tooltips are hidden.
The mouse group sits at the mouse position plus sixteen pixels in flipped screen space, and is emitted only when it has children and the mouse is on screen.

The widget types available, one file each in the tooltip namespace: `StringTooltip`, `FloatTooltip`, `IntTooltip`, `NumberTooltip`, `IconTooltip`, `LabelIconTooltip`, `NameTooltip`, `NotificationTooltip`, `ProgressTooltip`, `InputHintTooltip` and `ZoningEvaluationTooltip`, plus `TooltipColor` and `TooltipGroup`.

(VOLATILE: the widget type names above — the tooltip namespace, one file each.)

The shape: construct the widgets once in `OnCreate` with a stable path and a localised-string id, return early from `OnUpdate` unless your tool is the active tool, then set the values and call `AddMouseTooltip`.
Colour is per widget, so a warning and a success line can sit in the same group.

## The three actions a tool gets free, and why the vanilla ones cannot be taken

`ToolBaseSystem.OnCreate` fetches five action states from the game's tool action collection, keyed by the tool's own type name:

```csharp
string name = GetType().Name;
m_DefaultApply          = ...GetActionState("Apply", name);
m_DefaultSecondaryApply = ...GetActionState("Secondary Apply", name);
m_DefaultCancel         = ...GetActionState("Cancel", name);
m_MouseApply            = ...GetActionState("Mouse Apply", name);
m_MouseCancel           = ...GetActionState("Mouse Cancel", name);
```

This runs for a mod tool exactly as it does for a vanilla one, because it is base-class code.
The collection is `internal`, so a mod cannot make that call itself — and does not need to: the three protected properties `applyAction`, `secondaryApplyAction` and `cancelAction` return those states, falling back to the defaults whenever no override is set.

**What the call returns is a per-source wrapper over the shared action, not a copy of it.**
The lookup finds the underlying action by alias and wraps it in a state carrying its own activator and display-name override; every read on that state gates on its own `shouldBeEnabled`, and the underlying action ORs every activator's device mask together and subtracts every blocked barrier's.
The practical upshot is the whole point: **a mod tool's `applyAction` _is_ the user's Apply binding**, follows their rebinds automatically, and enabling it affects nobody else, because the tool holds its own activator on the shared action.
Source: `src/Game/Game.Input/UIInputActionCollection.cs` and `src/Game/Game.Input/UIInputAction.cs` (the lookup and the wrapper it returns), `src/Game/Game.Input/ProxyAction.cs` (the device masks combined across activators).

**Why the raw action cannot be taken instead.**
`InputManager.FindAction(mapName, actionName)` is public and the tool map's name is a public constant, so looking up the vanilla Apply action compiles.
Enabling it does not: setting `shouldBeEnabled` on a built-in action throws, and the public activator constructor throws on the same condition.
The only way past is an internal constructor overload that ignores the check — which is precisely what the wrapper the base class hands you is built on.
Which specific actions carry the built-in flag is asset data rather than code: the flag defaults to true, and the only thing that can clear it on a vanilla action is the input asset itself, which no grep of the source reads.
The one site in the game that clears it is a mod's own key-binding registration, so treat a vanilla action as built-in and **the inherited property as the sanctioned way in.**
Source: `src/Game/Game.Input/ProxyAction.cs` and `src/Game/Game.Input/InputActivator.cs` (the two throws and the internal overload past them), `src/Game/Game.Modding/ModSetting.cs` (the one site that clears the flag).

(VOLATILE: the five action alias strings above and the `internal` accessibility of the tool action collection — the base tool class's create body, and the input manager.)

**A tool's own extra actions are declared as settings, and they live in the mod's own action map**, where nothing is built in and `shouldBeEnabled` works normally; [`settings-and-input`](../settings-and-input/settings-and-input.md) owns that declaration side, and the split is clean — this reference owns the three actions the base class provides, that one owns every action a mod declares.
To make one of them fire on whatever button the user rebound a vanilla action to, mimic it: declaratively with the binding-mimic attribute naming the map and the action, or imperatively by looking up the vanilla action, copying its binding path and modifiers onto your own binding, and setting the binding back.
Mimicking is for a mod's _additional_ actions — a second modifier, a mode toggle, something that must appear on the settings screen — not for apply and cancel, which the base class already gave you.
Source: `src/Game/Game.Modding/ModSetting.cs` (the mod's own map, and the registration that leaves nothing built in) and `src/Game/Game.Settings/SettingsUIBindingMimicAttribute.cs` (the declarative form).

## Input barriers, the commit sound, and two prefab-side gates

- The tool system installs a barrier on the whole tool map, blocked while the game is loading or not in game, and a per-action mouse barrier that blocks while the pointer is over UI — so a tool's actions go quiet over the UI without the tool doing anything.
- `ToggleToolOptions(bool)` exists for the tool-options panel to suppress a tool's own actions while a widget has focus, and the base's actions-enabled state additionally goes false whenever an input field has focus.
- A static event on the base class fires for any tool action reaching the performed phase, which is a cheap hook for a mod that wants to observe tool input globally.
- **Committing a placement should play the matching UI sound.** `ToolUXSoundSettingsData` is a singleton component of entity references — bulldoze, place building, place prop, net start, net node and more — read through a singleton query and played with `AudioManager.PlayUISound(entity)`. The vanilla bulldoze tool picks between two of them on whether anything substantial was demolished.
  Source: `src/Game/Game.Prefabs/ToolUXSoundSettingsData.cs` (the component's entity fields) and `src/Game/Game.Tools/BulldozeToolSystem.cs` (the singleton read and the pick).
- `UpdateInfoview(Entity prefab)` reads the prefab's placeable-infoview-item buffer, sets the tool's infoview from the first entry and fills its infomodes from the rest; `OnStopRunning` clears both, so the infoview follows the tool automatically.
- The `require*` properties — `requireZones`, `requireUnderground`, `requirePipelines`, `requireNetArrows`, `requireStopIcons`, `requireAreas`, `requireRoutes`, `requireStops`, `requireNet` — tell the rendering side what to show while the tool is active, and the vanilla bulldoze tool sets four of them every frame.
- **Going underground takes more than `CollisionMask.Underground`, which does nothing visible on its own.**
  `allowUnderground` gates the underground control: the tool-options entry exists only while the active tool returns true, the top-toolbar toggle greys out without it, and both forward to the `SetUnderground(bool)` hook — so a tool that never overrides `allowUnderground` has a dead hook and, in game, no player-facing path into its underground mode (the map editor's screen registers an elevation input over the same hooks, so they can still fire there).
  And what opens the underground _view_ is `requireUnderground`, not the collision mask — a tool that sets only the mask hits buried objects the player cannot see.
  Source: `src/Game/Game.UI.InGame/ToolUISystem.cs`, `src/Game/Game.Rendering/UndergroundViewSystem.cs`, and the shipped UI bundle (`Cities2_Data/Content/Game/UI/index.js`) for what renders.

## What this reference hands to others

[`placement-definitions`](../placement-definitions/placement-definitions.md) is the seam every tool that builds anything hands off to: a tool does not mutate the world, it emits creation definitions that the generation systems turn into `Temp` entities and the apply systems commit.
That reference owns the definition components and their flags, and it owns the other direction too — a mod that wants to change what an existing tool places rewrites definitions rather than writing a tool.

[`settings-and-input`](../settings-and-input/settings-and-input.md) owns every action a mod declares, and the mimicking attributes above are its vocabulary.
[`frontend-and-injection`](../../../../cs2-modding-ui/references/frontend-and-injection/frontend-and-injection.md) owns the tool-options panel, which is where a mod tool has to put its activation control and its mode switcher, since the vanilla ones are hard-coded to vanilla tools; a tool overriding none of the panel-triggering virtuals above gets no panel at all until that reference's visibility hook is extended.

[`roads-and-traffic`](../../mechanics/roads-and-traffic/roads-and-traffic.md) is what the network half of the raycast surface exists for, and it carries the deepest tool state machines: a tool over lanes or connections is also the case that most often needs a parallel raycast pipeline, because the things it selects are mod-owned entities no vanilla search tree holds.
[`zoning-buildings-and-land-value`](../../mechanics/zoning-buildings-and-land-value/zoning-buildings-and-land-value.md) is reached through the zone and lot masks and the grid snap flags, and any tool that places something on the ground meets that grid.

[`performance-and-memory`](../performance-and-memory/performance-and-memory.md) owns every job this reference tells you to schedule: what `base.Dependency` carries, how a provider handle is taken and registered back, and why a container held across frames has to be disposed in `OnDestroy` with nothing to tell you when it is not.

[`patching`](../patching/patching.md) owns the third raycast route and any change to a vanilla tool's own behaviour.
[`ecs-in-this-game`](../ecs-in-this-game/ecs-in-this-game.md) owns `Temp`, the frame-scoped tags and the barrier contract the definition destruction above uses.
[`prefabs-and-assets`](../prefabs-and-assets/prefabs-and-assets.md) owns the prefab layers a tool's `GetPrefab` and `TrySetPrefab` deal in — a definition holds a prefab _entity_, not the authoring object.
