# The developer menu

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there, and the few claims naming the shipped UI bundle answer only to the install's own copy.
`cs2-modding-setup` provisions it.

The developer menu as a surface: what it already gives you, how a mod puts its own material into it, and how it behaves on a running game.
Why a mod is not working is `diagnostics` — that reference owns the diagnosis order and the log surfaces; this one owns the instrument.
Building a player-facing panel is the UI skill's business: the menu takes no HTML, no CSS and no UI mod — a mod contributes C# widget objects, and the game's own React renders them.

## What `--developerMode` actually gates

The flag sets one configuration bool, described in the game's own help text as "Enable developer mode" (`GameManager.ParseOptions`).
What reads it is the input system and the settings screen, and the menu is neither:

- `InputManager.TryGetComposite` refuses to build any binding whose composite is marked developer-only while the flag is off, and every composite in the `Debug` action map is.
- A settings property carrying `SettingsUIDeveloperAttribute` is dropped from the options UI without the flag, and the "About" settings tab is added only with it.

The bindings the flag turns on, read off the `Debug` action map:

| Action              | Bindings                      |
| ------------------- | ----------------------------- |
| `Debug UI`          | `Tab`, gamepad right shoulder |
| `Debug Prefab Tool` | `O`                           |
| `Debug Multiplier`  | `Shift`                       |

(VOLATILE: the map name, the three action names and their bindings — the input action asset inside `Cities2_Data/resources.assets`, or a live read of `InputManager.instance.FindActionMap("Debug")`.)

**Write the double-dash `--developerMode`, and treat other dash forms as variants rather than errors.**
The game parses its command line with Mono.Options, whose option pattern accepts `--`, `-` and `/` interchangeably and dispatches on the name alone, matched case-sensitively — the camel case is load-bearing where the dash count is not.
Source: `src/Colossal.Core/Mono.Options/OptionSet.cs`.

**Without the flag the menu is unreachable by keyboard and fully reachable by code.**
(UNVERIFIED: a run without the flag — every gate in the code says yes, but confirming it needs a launch without it.)
`DebugUISystem.debugSystemEnabled` is the literal `true`, the system is registered into `SystemUpdatePhase.UIUpdate` unconditionally, and `Show()`'s only gate is the `debug.enabled` binding that literal seeds and nothing ever writes.
Source: `src/Game/Game.UI.Debug/DebugUISystem.cs`, `src/Game/Game.Common/SystemOrder.cs`.

Opening it from a mod is one call:

```csharp
World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<DebugUISystem>().Show();
```

A first open can raise an in-game confirmation dialog, and the panels build only on its yes: the call flips visibility at once, and a yes with "don't show again" ticked skips the dialog for every later caller.
A no leaves a visible shell with `DebugSystem` disabled, and a retried `Show()` is then a no-op until `Hide()` runs.
`Hide()` disables `DebugSystem`.
Source: `src/Game/Game.UI.Debug/DebugUISystem.cs`.

## The rest of the developer command line

Every option the game itself registers is declared in one block, `GameManager.ParseOptions` — open it to see what exists rather than trusting any list.
The ones this topic's reader reaches for:

- `--uiDeveloperMode` — "Enable UI debugger and memory tracker" in the game's help text; it turns on the UI debugger, the memory tracker and live reload together, and forces the whole game to keep running in background.
  The debugger is one CDP endpoint on port 9444 — the settings default — serving the game's single UI view, boot, menu and in-game alike.
- `--qaDeveloperMode` — "Enable tests and automation"; without it the `Test Scenarios` and `Platforms` tabs return `null` and never exist, and part of the `Logs` tab stays hidden.
- `--logsEffectiveness=<level>` — the global logger override `diagnostics` owns; the `Logs` tab is its live equivalent.
- `--disableModding` and `--disableCodeModding` — the off switches `diagnostics` checks first.

`--developerMode` itself is free — three key bindings — but `--uiDeveloperMode` is not: its debugger forces `Application.runInBackground`, so an unfocused game keeps running at full rate, and live reload adds OS file watchers over the UI files.
The menu being open has a cost of its own, which is the cost-model section below.

**A store install with no launch-options field still has a way to pass flags.**
Before parsing, the game reads `runOnce.txt` under the user data path, appends its contents to the command line, and deletes the file.
(VOLATILE: the file name — `GameManager`'s command-line merge.)
Source: `src/Game/Game.SceneFlow/GameManager.cs`.

**`--burst-disable-compilation` is not a game option, and its double dash is mandatory.**
Unity Burst parses the raw argument list itself with an exact string compare, so the dash freedom the game's own options enjoy does not apply; the same block accepts `--burst-force-sync-compilation` and the `UNITY_BURST_DISABLE_COMPILATION` environment variable.
Source: `src/Unity.Burst/Unity.Burst/BurstCompilerOptions.cs`.

Turning Burst off makes every job run managed: steppable by a Mono debugger, and far slower for normal play; `performance-and-memory` owns that trade, the environment variable's accepted values included.

**There is no in-game text console.**
The console classes in the debug namespace are a Win32 stdout capture, constructed only when `--captureStdout=` selects a capturing mode; nothing in the game reads a typed command, so a reader arriving from a game that has one should look for nothing here.
Source: `src/Game/Game.Debug/ConsoleWindow.cs`, `src/Game/Game.SceneFlow/GameManager.cs`.

## What the menu is: three layers

The widget model is the render pipeline's: `UnityEngine.Rendering.DebugUI` owns `Panel`, `Container`, `Foldout`, `Button`, `Value`, `Field<T>` and the rest, and `DebugManager` owns the panel list — vendored Unity code the game did not write, which is why this extension API reads unlike the rest of the game.
`Game.Debug.DebugSystem` populates it, reflecting over the attributes below and registering panels with `DebugManager`.
`Game.UI.Debug.DebugUISystem` publishes the result as the `debug` binding group, `DebugWidgetBuilders` translates each `DebugUI.Widget` into the same `Game.UI.Widgets.IWidget` layer the editor and options screen use, and the game's own React under `game-ui/debug/` in the shipped bundle renders it.

**Opening the menu builds every panel the game contributes, and closing it tears them all down.**
`DebugSystem` starts disabled and is enabled only by `Show()`; its `OnStartRunning` runs the reflection scan and its `OnStopRunning` removes everything it registered.
While the menu is shut, the only panels standing are the ones the rendering stack registers with `DebugManager` on its own.
Source: `src/Game/Game.Debug/DebugSystem.cs`.

## The tabs

A tab is a method carrying `[DebugTab]` on a class carrying `[DebugContainer]`; the vanilla declarations all sit under `src/Game/Game.Debug/`.
What a running game shows is neither that list nor a subset of it:

- The rendering stack registers panels of its own directly with `DebugManager` — display stats, lighting, rendering and their siblings — which the game never declares and which survive the menu closing.
- `Test Scenarios` and `Platforms` exist only under `--qaDeveloperMode`.
- Several tabs are gated on game mode: `Actions` only in game, `Climate` and the `Simulation` body only in game or editor.
  Returning `null` from a `[DebugTab]` method is the sanctioned "no tab right now" — registration skips a null list entirely, which is why the menu at the main menu is much shorter than in a city.

(VOLATILE: every tab name and menu label quoted in this file — the `[DebugTab]` declarations and widget-building methods under `src/Game/Game.Debug/`.)

## The two ECS tabs, and the question neither answers

`ECS Components` is an archetype and chunk census, not an entity view.
It walks every archetype, counts archetypes per component type, sums entity counts against chunk capacities, and shows the top 100 component types by archetype count — the constant is in `ComponentDebugUtils.GetCommonComponents`.
Its widgets are a case-insensitive substring filter over the component's `FullName`, an "exclude used archetypes" toggle that keeps only archetypes with zero chunks, and a `Refresh` button; nothing computes until the first press, and nothing recomputes until the next.
Component names print with the leading `Game.` stripped.

`Search` finds entities and moves the camera to them.
It builds a real `EntityQuery` from three user-assembled component sets — All, Any, None — then walks the result matching a string against the prefab name, its localized name, sub-mesh names, batch meshes and the citizen-activity enum.
With `Deep search` off it stops at the first match and remembers where it stopped, so pressing Apply again resumes the scan; `Must have transform` filters the results, which page as buttons.
**Running a search moves the player's camera whether or not they click anything.**
Clicking a result — and Apply itself, on the first result — sets `ToolSystem.selected`, points the orbit camera controller at the entity, sets rotation and zoom from the prefab's `ObjectGeometryData` bounds, and makes the orbit controller the active one.
Source: `src/Game/Game.Debug/DebugSystemSearch.cs`.

**Nothing in the menu enumerates the components of one entity.**
The only component-type reads in the debug namespace are over archetypes, so there is no vanilla "inspect this entity" view to find.
The sibling Unity devtools plugin, installed separately, is the instrument that reads one entity's components on a running game; the menu never will.
Source: `src/Game/Game.Debug/ComponentDebugUtils.cs`.

## What the menu tells you about one specific entity

The developer section of the selected-info panel, off by default: the `Gameplay` tab carries a "Show developer info" toggle writing `DebugUISystem.developerInfoVisible`, and `SelectedInfoUISystem` writes the section only while it is on.
The content is a long roster of hand-written subsections, all registered by `DeveloperInfoUISystem.OnCreate`, each deciding for itself whether it applies to the selected entity.
**A component nobody wrote a formatter for is invisible, so a mod's own component is invisible by construction.**
The only generic row is the entity's debug name — prefab name plus entity index, from `NameSystem.GetDebugName`; everything else is a domain formatter, and there is no component list, no raw field dump and no buffer view.
Source: `src/Game/Game.UI.InGame/DeveloperInfoUISystem.cs`.

## Adding a tab: the attribute route

Both attributes are public, in `Game.Debug`: `[DebugContainer]` on a class or struct, `[DebugTab(name, priority)]` on a method returning `List<DebugUI.Widget>`.
Both derive from `UnityEngine.Scripting.PreserveAttribute`, so code stripping cannot remove the annotated members.

```csharp
[DebugContainer]
internal sealed class MyModDebugUI
{
    [DebugTab("My Mod", 100)]
    private List<DebugUI.Widget> BuildMyModDebugUI()
    {
        return new List<DebugUI.Widget>
        {
            new DebugUI.BoolField
            {
                displayName = "Verbose",
                getter = () => s_Verbose,
                setter = value => s_Verbose = value,
            },
        };
    }
}
```

The scan sweeps every loaded assembly whose name does not start with `Unity`, so a mod is found with no registration call and no ordering concern — the reflection type cache is invalidated on every assembly load, so an assembly loaded late is still found.
The method may be private (every vanilla one is), and may take any of four shapes, decided by parameter count and declaring type:

| Shape                                                       | What the game does                                                                                                                                        |
| ----------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Method, no parameters, on a plain concrete class            | `Activator.CreateInstance` on the class — needs a public parameterless constructor — then invoke on the instance, static or not                           |
| Method, no parameters, on a `ComponentSystemBase`           | `World.GetOrCreateSystemManaged` on the declaring type, then invoke on the system                                                                         |
| Static method, no parameters, on a static or abstract class | invoked with no target                                                                                                                                    |
| Method taking one `World`                                   | invoked with the world — on the created instance for a plain concrete class, with no target otherwise, so on a system or abstract class it must be static |

Returning `null` registers no tab, and a method taking two or more parameters registers nothing at all — no exception and no log line.
A plain container class implementing `IDisposable` is disposed on menu close, which is the hook for turning a debug-only helper off with the menu.

**A mod assembly named `Unity…` is invisible to the entire extension surface.**
The sweep skips assemblies whose full name starts with `Unity` — and so does every other reflection sweep in the game that shares the helper.
Source: `src/Colossal.Core/Colossal.Reflection/ReflectionUtils.cs`.

**A static class works; an instance tab method on an abstract class does not.**
"Concrete" in the scan helper's name only means "not an interface", and no instance is created for an abstract class — which to reflection includes every static class, and several vanilla containers are static classes whose static tab methods take the no-target branch.
An instance tab method on an abstract class is invoked with no target and fails into the caught-and-logged path.
Source: `src/Game/Game.Debug/DebugSystem.cs`.

**A tab method that throws loses its tab silently, apart from one log line.**
Each tab's exception is caught and logged as `Failed to register '<name>' Debug UI`; that string is what to grep for, and `diagnostics` owns the log surfaces it appears in.
(VOLATILE: the message string — `DebugSystem`'s registration catch blocks.)
Source: `src/Game/Game.Debug/DebugSystem.cs`.

Rebuilding a tab from your own code is `DebugSystem.Rebuild(...)`, public and static, in two delegate shapes matching the table's parameterless and `World`-taking rows; it throws — caught and logged on the same line — unless the delegate's method carries `[DebugTab]`.
That is how a tab whose content depends on state redraws itself; the `ECS Components` `Refresh` button is exactly this call.

## Adding a panel: the imperative route

`DebugUISystem` renders every `DebugManager` panel that is not editor-only and has at least one non-editor-only child, not only the ones `DebugSystem` created.
So a mod can take a panel straight from the registry and it appears, with no attribute and no game type involved beyond `DebugManager`:

```csharp
var panel = DebugManager.instance.GetPanel("My Mod", createIfNull: true);
panel.children.Add(new DebugUI.Foldout { displayName = "Diagnostics" });
```

**An imperatively created panel is never torn down and never rebuilt**: the menu's close pass removes only what the attribute scan registered, so the panel's widgets live for the process and its getters keep running whenever the frontend is subscribed.
The attribute route's panels are rebuilt from scratch on every open, which is what lets a tab reflect state that changed while it was closed — so the attribute route is the default, and the imperative route is for a panel that must outlive the menu or be built before `DebugSystem` exists.
Source: `src/Game/Game.Debug/DebugSystem.cs`.

**A panel name that already exists merges instead of duplicating — except between two `[DebugTab]`s spelling the name identically, where the later replaces the earlier.**
`GetPanel` returns the existing panel rather than overriding it and the caller's widgets join its children, matched invariant-culture case-insensitively — which is how a `[DebugTab]` joins a panel the rendering stack registered.
But `DebugSystem.AddPanel` also keeps its own name-to-widget-list dictionary, ordinally keyed, and removes the list it previously registered under the same exact name before adding the new one: two `[DebugTab]`s naming `"Gizmos"` leave only the later scan's widgets standing, while `[DebugTab("gizmos")]` misses the ordinal key and genuinely appends to the vanilla tab.
The priority applies only on creation, so a mod appending to an existing tab cannot move it; tab order is priority ascending through an unstable sort, so equal priorities land in arbitrary order.
Source: `src/Unity.RenderPipelines.Core.Runtime/UnityEngine.Rendering/DebugManager.cs`, `src/Game/Game.Debug/DebugSystem.cs`.

## What a mod gets by annotating a field: the watch attributes

The `Watchers` tab is built by `DebugWatchSystem` from four public attributes in `Game.Debug`:

| Attribute            | Target                    | What it does                                                        |
| -------------------- | ------------------------- | ------------------------------------------------------------------- |
| `[DebugWatchValue]`  | field, property or method | a graphed value, with `color`, `updateInterval` and `historyLength` |
| `[DebugWatchOption]` | field                     | an enum field rendered as an option control                         |
| `[DebugWatchDeps]`   | field                     | a `JobHandle` the accessor completes before reading                 |
| `[DebugWatchOnly]`   | class                     | the system runs only while its foldout is open or a watch is active |

**The sweep is over `World.Systems`, not over types.**
The attributes work on any managed system already created in the world — a mod's included — and on nothing else: a static class, a plain object or an unmanaged `ISystem` never appears.
Private members work; the collection runs over fields, properties and methods alike.
Source: `src/Game/Game.Debug/DebugWatchSystem.cs`.

**A watch on a type outside the accepted set renders a labelled row with a blank value — not an error.**
A plain field, property or method graphs only as `int`, `uint` or `float`; the two- and three-component vector forms graph only wrapped in a `NativeValue<T>`, so a bare `float3` field gets the blank row exactly as a `long`, a `double` or a `bool` does.
`DebugWatchDistribution` graphs as a bucket view, and a `NativePerThreadSumInt` field is special-cased to graph its count.
(VOLATILE: the accepted type set — `DebugWatchSystem`'s watch factory and `Game.Reflection.ValueAccessorUtils`.)
A `NativeArray<T>` field is special-cased into per-element value rows and per-element watch toggles, capped at 100 elements — the constant is in the array branch of `DebugWatchSystem.BuildSystemFoldouts`.
Source: `src/Game/Game.Debug/DebugWatchSystem.cs`.

**`updateInterval` must be a power of two.**
The sampler tests `frameIndex & (updateInterval - 1)`, so any other value samples on a bit pattern rather than a period; left at its default, the interval is the system's own simulation cadence.
**A watch samples on simulation frames, not render frames**, so a rarely running system draws a nearly flat graph.
Source: `src/Game/Game.Debug/DebugWatchSystem.cs`.

`[DebugWatchOption]` renders only for an enum field, and is dropped without a message for anything else.
`[DebugWatchDeps]` completes the marked job handle before the watched value is read, so a watch on a field a job writes reads a settled value rather than a torn one.
`[DebugWatchOnly]` on the system class enables the system only while its foldout is open or one of its watches is active — a debug-only system that costs nothing while nobody looks, and the attribute a mod author most wants and least finds.
The system list rebuilds only on the tab's `Refresh System List` button, so a system created after the tab was built is missing until it is pressed.

## The Gizmos tab cannot be joined by deriving

**Deriving from `BaseDebugSystem` does not put a system on the `Gizmos` tab, and no registration can.**
The tab's builder is a hard-coded list of `AddSystemGizmoField<T>` calls, one per vanilla system, with no reflection anywhere; there is no registry the base class writes itself into.
Source: `src/Game/Game.Debug/DebugSystem.cs`.

What `BaseDebugSystem` buys is the content of a gizmo entry, not its display: an option list built by the protected `AddOption(displayName, defaultEnabled)`, the `OnEnabled(DebugUI.Container)` / `OnDisabled(DebugUI.Container)` hooks for extra widgets, and a `JobHandle OnUpdate(JobHandle)` override point.
Running it also takes a phase: the vanilla gizmo systems live in `SystemUpdatePhase.DebugGizmos`, driven after `LateUpdate`, and a mod placing a system there needs `mod-lifecycle-and-ordering`'s imperative phase registration.

Two display routes are open to a mod:

- Render the toggle yourself into your own panel: one `DebugUI.EnumField` switching `system.Enabled`, with the system's options beneath it while enabled — the same shape `AddSystemGizmoField` builds for the vanilla tab.
- Declare `[DebugTab("gizmos")]`, case-varied on purpose, and let the name merge append your widgets to the vanilla tab — the exact spelling `[DebugTab("Gizmos")]` hits `DebugSystem`'s ordinal bookkeeping and only one of the two widget lists survives, by scan order (the panel-name trap above).
  (UNVERIFIED: that the case-varied tab's widgets render beside the vanilla gizmo rows — every step of the merge says they must, but nobody has declared one; a test mod carrying `[DebugTab("gizmos")]` and a read of `debug.children` with that tab selected would settle it.)

## Adding to the selected-entity developer section

One public method and no attribute: `SelectedInfoUISystem.AddDeveloperInfo(ISubsectionSource)`.
The interface is two methods over `IJsonWritable` — `DisplayFor(Entity entity, Entity prefab)` and `OnRequestUpdate(Entity entity, Entity prefab)` — and a mod rarely implements it, because three shipped helpers take a `shouldDisplay` predicate and an `onUpdate` action and do the rest:

- `GenericInfo` — a label and a string value, plus an optional target entity that renders the row as a link.
- `InfoList` — a label and a list of text rows, each optionally a link.
- `CapacityInfo` — a label, a value and a max.

Call it from a `UISystemBase`'s `OnCreate` after `GetOrCreateSystemManaged<SelectedInfoUISystem>()`, which is how the game's own subsections register (`DeveloperInfoUISystem`).
The section renders only while "Show developer info" is on and at least one subsection's `DisplayFor` returns true.
It also opts into four cases the ordinary sections opt out of — destroyed objects, outside connections, buildings under construction, and upgrades — so a subsection is asked about entities the player-facing panel never shows (`DeveloperSection`).

## Which widget kinds reach the frontend

The translation is a chain of `is` tests in `DebugWidgetBuilders.TryBuildWidget` — open it for the full map of `DebugUI` kinds to frontend widgets.
The consequences a mod author needs:

- An `IntField`, `UIntField` or `FloatField` renders as a slider only when both `min` and `max` are set away from the type's extremes, and as an arrow control otherwise; a `Vector*Field` is always a slider group.
- A subclass inherits its base's case: `HBox`, `VBox` and `Table` all render as a flat group, losing their layout intent, and a history field renders as its plain base.
- **Four kinds are dropped in silence.**
  `ObjectField`, `ObjectListField`, `ObjectPopupField` and `MessageBox` match nothing, and the builder skips them with no log line and no placeholder.
  (VOLATILE: the unmatched kind set — `DebugWidgetBuilders.TryBuildWidget`'s chain against `UnityEngine.Rendering.DebugUI`'s kind declarations.)
  Source: `src/Game/Game.UI.Debug/DebugWidgetBuilders.cs`.
- **`isHiddenCallback` does nothing in this menu.**
  The translation never reads it, so a callback that hides a widget in the render pipeline's own IMGUI renderer changes nothing here; `isEditorOnly` is the flag that works, skipping the widget and, when a panel has no other children, the panel.
  Source: `src/Game/Game.UI.Debug/DebugWidgetBuilders.cs`.
- **An unmapped widget type renders as a red box reading `Unknown element type <name>` — no throw, no warning.**
  The frontend dispatches on the widget's fully qualified C# type name against a fixed component map covering exactly what `DebugWidgetBuilders` produces, so vanilla never shows one; a mod reaching the widget layer another way can, since most of the shared widget layer's kinds are unmapped here.
  Source: `Cities2_Data/Content/Game/UI/index.js`.

The menu's own React components are registered modules under `game-ui/debug/` a UI mod may import like any other — the multi-component float slider fields exist only in that tree — which is the one seam where this material touches a player-facing UI; `binding-layer` owns the machinery underneath.

## The tool enumeration a mod can break

The `Simulation` tab builds its "Active tool" control by reflecting every type assignable to `ToolBaseSystem` across loaded assemblies and calling `World.GetOrCreateSystemManaged` on each — with no abstract filter.
Vanilla survives its own abstract bases because a created system registers its whole base-type chain in the world's system lookup, so the abstract types resolve to an existing system and never reach construction.
Miss that lookup and construction ends in `Activator.CreateInstance`, which throws on an abstract type.

**An abstract `ToolBaseSystem` descendant with no concrete descendant already created kills the whole `Simulation` tab.**
Abstract intermediate tool layers are a natural mod design, and each one is this hazard unless some concrete descendant is guaranteed created before the menu opens.
Source: `src/Game/Game.Debug/DebugSystem.cs`, `src/Colossal.Core/Colossal.Reflection/ReflectionUtils.cs`, `src/Unity.Entities/Unity.Entities/World.cs`.

**The failure logs under both names: `Failed to register 'Simulation' Debug UI` from the attribute scan, and the method-name form from every rebuild.**
Both catches see the same exception, so a grep for `Simulation` finds the failure — and the method-name line repeats while the menu stays open, the same defect re-thrown on every rebuild rather than new ones.
Source: `src/Game/Game.Debug/DebugSystem.cs`.

The repair is a Harmony transpiler, and `patching` owns the technique; the shape that works here:

- Inject a call that filters abstract types out of the reflected array immediately after it is produced — before anything else runs, because the two parallel arrays that follow both size themselves from that array's length, and a repair landing later must fix both or leave trailing null entries behind.
- Match the unpatched loop's opcode sequence before injecting, and stand down when it differs, on the assumption that another mod already applied a compatible fix — this is a repair every affected session needs exactly once.
  (UNVERIFIED: the opcode-match guard and the cost of repairing inside the loop instead — read from the repairing mod's own change record, not its source; a checkout of that mod's code submodule would settle it.)

Nothing else in this reference needs patching at all: every extension surface above is public.

## What an open widget costs

The widget tree updates once per rendered frame while anything is subscribed to `debug.children`, and not at all otherwise — an event binding is active only while it has observers, so a closed menu runs nothing.
Four rules govern a widget that is open:

1. **An editable field's getter runs once per rendered frame.**
   Every `BoolField`, `IntField`, `FloatField`, `TextField`, `EnumField`, `BitField`, `ColorField` and `Vector*Field` getter is called unconditionally each update and its result compared.
   Source: `src/Game/Game.UI.Widgets/ReadonlyField.cs`.
2. `DebugUI.Value` is the one throttled kind: its getter runs on a per-widget `refreshRate` timer defaulting to a tenth of a second, and it is also the only kind whose `formatString` is honoured.
3. **A collapsed `Foldout` costs nothing; a `Container` costs its whole subtree unconditionally.**
   The expandable group reports no visible children while collapsed, so the walk never descends; a plain group always reports all of them.
   Put an expensive getter under a `Foldout`, never under a `Container`.
   Source: `src/Game/Game.UI.Widgets/ExpandableGroup.cs`.
4. The widget layer's `hidden` callback would skip a node's update, but nothing on the debug path ever sets it — the `isHiddenCallback` finding above — so it is not a lever here.

So the getter contract: called at frame rate unless on a `DebugUI.Value`, and therefore no allocation, no job-handle completion, no `EntityQuery`.
The vanilla model is compute-on-button: the `ECS Components` `Refresh` button runs the census once and the getters return captured locals.

## When a mod's tab registers, and what the timing forecloses

The attribute scan runs from `DebugSystem.OnStartRunning` — the first time the menu opens, again on every open, and again on every save load while the menu is up.
It never runs at game load.

- Late loading is not foreclosed — the type-cache invalidation above — so there is no race to win and nothing to register in `OnLoad`.
- **It forecloses one-shot side effects in the tab method.**
  The method runs on every open and every save load, so anything beyond building widgets happens repeatedly — the vanilla `Search` tab resets its search state on every call, which is why a search does not survive closing the menu.
  Source: `src/Game/Game.Debug/DebugSystem.cs`.
- **It forecloses holding a widget reference across a close.**
  The close pass removes the widgets and disposes the container instances, so a `DebugUI.Widget` stashed in a static is orphaned after the first close and never rendered again.
  Source: `src/Game/Game.Debug/DebugSystem.cs`.
- The imperative route sidesteps all of this, at the price named in its section: never torn down, never rebuilt.

## Driving it from outside

Everything the menu does is reachable over its `debug` binding group on a running game with the UI debugger on — reading the widget tree, opening and closing the menu, selecting tabs, clicking controls, committing text.
The routes, the selectors and the traps — a fill that never commits, a select that silently no-ops, a module registry that hands out dead accessors — are [driving the menu from outside](driving-the-menu.md).

## What this reference hands to others

`diagnostics` owns why a mod is not working; it takes from here the `Logs` tab as the live face of the logger configuration it reads off disk, and the `Failed to register '<name>' Debug UI` line, written with the tab name by the attribute scan and the method name by the explicit rebuild.
`custom-tools` owns the tool base classes; the abstract-descendant hazard belongs beside any decision to ship an abstract tool layer.
`mod-lifecycle-and-ordering` owns `SystemUpdatePhase.DebugGizmos` and the imperative phase registration a gizmo system needs, and its `ComponentSystemBase` distinction is what the four tab-method shapes turn on.
`performance-and-memory` owns the per-frame rules the cost model instantiates, and gains the `[DebugWatchOnly]` idiom — a first-party pattern for a system that costs nothing while nobody watches.
`ecs-in-this-game` owns archetypes, chunks and queries; the `ECS Components` census and the `Search` query builder are those concepts with a UI on them.
`patching` owns the transpiler that repairs the tool enumeration — an instructive case, since the injection point is forced by two allocations sizing themselves from the array it rewrites.
`binding-layer` owns everything under the `debug` binding group and the shared widget-over-bindings machinery; the module-registry defect recorded in [driving the menu from outside](driving-the-menu.md) bites any binding, not just this group's.
A reader leaves knowing the menu is a C# widget tree the game's own React renders, that a mod joins it by attribute or by registry with no patching, and that the launch flags gate keys and tooling rather than capability.
