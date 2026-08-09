# Driving the developer menu from outside

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The routes hold without one, but the binding endpoints named below are checkable only there, and the frontend claims only against the install's shipped UI bundle.
`cs2-modding-setup` provisions it.

Operating the menu on a running game over the UI's remote-debugging socket: what works, in what order, and the traps.
The general operating manual — connecting, clicking, filling, waiting on a live view — belongs to the sibling Coherent Gameface plugin, installed separately; this file carries only what is specific to the menu.
[The developer menu](debug-menu.md) is the entry reference this file details.

## The `debug` binding group

`DebugUISystem` declares the group; this table quotes its declarations:

| Binding | Kind |
| --- | --- |
| `debug.enabled` | `ValueBinding<bool>`, the literal `true` |
| `debug.visible` | `ValueBinding<bool>` |
| `debug.panels` | `GetterValueBinding<List<string>>` — the tab names, in order |
| `debug.selectedIndex` | `GetterValueBinding<int>` |
| `debug.selectedPanel` | `ValueBinding<Panel>`, nullable, carrying `displayName` only |
| `debug.children` | `RawValueBinding` — the whole widget tree |
| `debug.observedBinding` | `ValueBinding<IDebugBinding>`, nullable |
| `debug.bindingTriggered` | `EventBinding<IDebugBinding>` |
| `debug.developerInfoVisible` | `GetterValueBinding<bool>` |
| `debug.watches` | `GetterValueBinding<List<DebugWatchSystem.Watch>>` |
| `debug.show`, `debug.hide` | `TriggerBinding` |
| `debug.selectPanel` | `TriggerBinding<int>` |
| `debug.selectPreviousPanel`, `debug.selectNextPanel` | `TriggerBinding` |

The shared widget action triggers land in the same group through `WidgetBindings`: `debug.invoke`, `debug.setValue`, `debug.setExpanded`, the list operations and `debug.setCurrentPageIndex`.
(VOLATILE: every binding name above — `Game.UI.Debug.DebugUISystem`'s binding declarations and `Game.UI.Widgets.WidgetBindings`.)

## Reading and triggering

Reading a value binding is the engine pair: `engine.on("debug.<name>.update", cb)`, then `engine.trigger("debug.<name>.subscribe")`, with `engine.trigger("debug.<name>.unsubscribe")` to end it.
`debug.children` returns the whole widget tree, each node `{path, props: {__Type, …}, children}`; `props.__Type` is the fully qualified C# widget type the frontend dispatches on.
Triggers are plain calls: `engine.trigger("debug.show")`, `engine.trigger("debug.selectPanel", 2)`.

**`debug.selectPanel` takes a zero-based index into `debug.panels`, not a name.**
An out-of-range index yields a null selected panel and an empty tree rather than an error.
Source: `src/Game/Game.UI.Debug/DebugUISystem.cs`.

Triggering `debug.show` runs `DebugUISystem.Show()` — the trigger is bound directly to it.

The whole route works without `--developerMode` — the entry file carries the mechanism.
(UNVERIFIED: a run without the flag — every gate in the code says yes, but confirming it needs a launch without it.)

## The traps

**`debug.selectPanel` is a no-op while the menu is hidden — and while hidden there is almost nothing to select.**
Closing the menu tears down every game-contributed panel, so the driving order is fixed: `debug.show`, let the binding round-trip, then `debug.selectPanel`.
Source: `src/Game/Game.UI.Debug/DebugUISystem.cs`.

**A first `debug.show` can raise the in-game confirmation dialog instead of the panels.**
When the dialog appears, `Show()` has already flipped `debug.visible` but defers enabling `DebugSystem` into its yes, so `debug.panels` holds only the rendering-owned survivors until someone answers in-game — a near-empty panel list right after `debug.show` means the dialog is up, not a broken route.
After a no, `debug.visible` stays true, so retrying `debug.show` is a no-op until `debug.hide` runs.
Source: `src/Game/Game.UI.Debug/DebugUISystem.cs`.

**Setting a text input's DOM value does not commit it; the field commits on blur.**
The debug text input's change handler only updates local React state, and its blur handler is what fires the `setValue` trigger — so a programmatic fill must be followed by a bubbling `focusout` on the input: `el.dispatchEvent(new FocusEvent('focusout', {bubbles: true}))`.
The integer input has the same shape.
Source: `Cities2_Data/Content/Game/UI/index.js`.

Clicks need no such care: an arrow control's buttons step the value, a toggle flips, a button runs its action, a foldout header expands, and the panel rebuilds in place where the change demands it.

## Selectors

The menu's class names are content-hashed and change on any UI rebuild, so treat every selector below as a fingerprint of this version and re-derive them from the DOM when they miss.
(VOLATILE: every class name below — the menu's class maps in the shipped bundle, `Cities2_Data/Content/Game/UI/index.js`.)

The root is `div.debugging_dvz` holding `div.debug-ui_M_y`; the tab bar is `div.tab-bar_b_c` with one `button.button_BNH` per tab, the active one also carrying the literal class `selected`.
A field is `div.field_vGA` with `div.label_KyX` and `div.control_b3l`; a plain value's control is `div.content_EQJ`, a toggle is `div.toggle_x2y` plus `checked`/`unchecked`, an arrow control is two `button.button__Wn` around `div.value_fMT`, a text input is `input.text-input_Y20`, an action button is `button.button_hxl`, a foldout header is `.foldout-button_Ugi`, a widget group's title is `div.title_Xkf`.
The literal `selected` class is the one durable hook.
The menu shell's own class map also carries a `titleBar` and a `title` of its own, and neither is ever rendered — the `title` that renders is the group title above, from the container's map.

## State lives in C#

The menu survives a view reload — which is what a UI-mod rebuild causes: the tab bar, the selected panel and its content all come back, because the frontend merely re-subscribes and is handed the same values.

## A gizmo never shows in a UI-side capture

A gizmo toggled on is drawn by the game's renderer into the 3D view and never reaches the Cohtml view, so a UI-side screenshot shows the toggle and not the gizmo.
A capture that composites the scene and the UI does show it.
(UNVERIFIED: the composited half — no such capture was taken against an enabled gizmo; one full-screen capture would settle it.)

## Watches

`debug.watches` returns each enabled watch's ring buffer, already rotated oldest-first: `{__Type: "debug.HistoryWatch", name, color, history: [{x: frameIndex, y: value}, …]}`, or `debug.DistributionWatch` with a `buckets` array.
A mod's own `[DebugWatchValue]` field arrives the same way after the tab's `Refresh System List`.
(UNVERIFIED: that read — no loaded mod carried the attribute in the session that established these routes; a mod annotating an `int` field on a world-resident system, the refresh, then a read of `debug.watches` would settle it.)

## The module registry hands out dead accessors

`window["cs2/modding"]` exposes exactly two functions, `findModule` and `getModule` — there is no `getModuleRegistry` in the shipped build, whatever a tutorial says.
**`getModule` on a data-binding module's mutable exports throws `ReferenceError: <name> is not defined`.**
The registry shim's accessors for those exports return pre-minification source names no scope declares, so the error is a free-variable miss rather than an initialization-order problem — the bindings themselves are alive, and only their registry projection is dead.
Function and enum exports in the same module resolve correctly, and the defect runs across nearly every data-binding module in the bundle: a packaging fault, not a property of bindings.
Source: `Cities2_Data/Content/Game/UI/index.js`.

What to do instead: `findModule` is always safe — it enumerates keys without evaluating an accessor — and a binding is read through the engine pair above, or rebuilt through `window["cs2/api"]`: `bindValue("debug", "visible", false).subscribe().value` reaches the same C# binding.

One false friend while exploring: `game-ui/debug/debug-shortcuts.ts` exports a hook binding Ctrl+R to a view reload, and has nothing to do with the menu.
