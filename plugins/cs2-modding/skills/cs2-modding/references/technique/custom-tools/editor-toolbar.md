# Registering into the map editor's toolbar

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there; the tooltip key answers only to the install's own UI bundle.
`cs2-modding-setup` provisions it.

Read this when your mod adds an entry to the map editor's toolbar.
The entry list shares no code with `ToolSystem.tools`, and an entry is an `IEditorTool` object rather than a `ToolBaseSystem`.
An entry can open a panel, drive a `ToolBaseSystem`, or both — the panel-only entry is the vanilla majority shape — and a tool it drives is built as [`custom-tools`](custom-tools.md) teaches, while the entry itself owns activating it (below).

## Registration is an array you grow yourself

`Game.UI.Editor.EditorToolUISystem.tools` is an `IEditorTool[]` behind a public property, and there is no registration API: the vanilla entries are constructed inline in the system's `OnCreate`, and nothing else in the game writes the property.
So registering is reading the array, building one that is one longer with your entry appended, and assigning it back.
**Assign through the property, never around it.**
The setter also rebuilds a parallel disabled-flags array the update walks index-for-index, so a write that bypasses the setter leaves the two arrays out of step.
Source: `src/Game/Game.UI.Editor/EditorToolUISystem.cs` (the property whose setter rebuilds the disabled array, and the vanilla entries in `OnCreate`).

**The UI resolves a selection by id with a first-match walk, so a duplicate id makes the later entry unreachable.**
The `editorTool` binding group exposes `tools`, `activeTool` and `selectTool`, and the select handler takes the first tool whose id matches.
Source: `src/Game/Game.UI.Editor/EditorToolUISystem.cs` (the binding group and the id walk).

(VOLATILE: `EditorToolUISystem.tools` and the `editorTool` binding names — `Game.UI.Editor`.)

## Register once per process, from the main thread

The world outlives every load, and the vanilla entries exist before any mod code runs; [`mod-lifecycle-and-ordering`](../mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md) owns that boot sequence.
So an append from your own system's `OnCreate` runs once and survives every load, while an append from a per-load hook runs again on every load and needs a duplicate guard.
Source: `src/Game/Game.UI.Editor/EditorToolUISystem.cs` (the vanilla entries constructed in `OnCreate`).

**Assign from the main thread, where every vanilla write happens: nothing synchronizes the property against the binding layer reading it.**
(UNVERIFIED: whether a background-thread assignment can corrupt a concurrent binding read — derived from the absence of any lock; dispatching the write to the main thread and watching the binding would settle it.)

## The entry's own contract

The shipped `EditorTool` class implements the interface and adds settable `panel` and `tool`, both optional; its `IsActive()` compares the active panel against `panel`, and consults `tool` only when it is non-null.
A panel-only entry sets `panel` alone and needs no `ToolBaseSystem` at all.
An entry that drives a tool sets `tool`: enabling the entry assigns it to `ToolSystem.activeTool`, and disabling restores through `ActivatePrefabTool(null)` only while that tool is still the active one — leave the restore to the entry rather than wiring [`custom-tools`](custom-tools.md)'s own previous-tool restore into its path.
**An entry with neither set reports active whenever no panel is open**, so set at least one of the two.
Source: `src/Game/Game.UI.Editor/EditorTool.cs` (the two `[CanBeNull]` members, the comparison, and the guarded enable and disable paths).
(VOLATILE: the `panel` and `tool` members and the activity comparison — `EditorTool`.)

The tooltip's locale key is `Editor.TOOL[<id>]`, indexed by the entry's own id — a panel title may reuse the same key, as the vanilla bulldoze entry's does — and [`localization`](../localization/localization.md) owns adding it.
Source: the shipped UI bundle (`Cities2_Data/Content/Game/UI/index.js`) (the editor toolbar rendering each button's tooltip from the key and the entry's id).
