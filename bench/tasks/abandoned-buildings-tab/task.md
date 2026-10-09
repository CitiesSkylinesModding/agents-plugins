# A developer menu page counting abandoned buildings

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod and I'd like to give it its own page in the game's developer menu. The page should show how many buildings in the city are currently abandoned, and I'd also like to see that number graphed over time in the developer menu, sampled roughly once per in-game hour, so I can see whether a policy change is making things better or worse. The page also needs a toggle that turns my mod's verbose logging on and off, and a short line of text at the top explaining what the number means. None of this should cost anything while I'm not looking at it.

## Checklist

### The graphed value is a watch on a system the world holds

- Falls in: the graph is attempted with no `[DebugWatchValue]` at all (a hand-rolled history rendered into the page, or a `DebugUI.HistoryBoolField`), or `[DebugWatchValue]` sits on a member of a static class, a plain object or the `IMod` class, none of which the sweep over the world's systems reaches.
- Avoids: `[DebugWatchValue]` sits on an instance field, property or method of a class deriving from `GameSystemBase` (or any `ComponentSystemBase`) that the mod registers with the update system.
- Reference: cs2-modding/references/technique/debug-menu/debug-menu.md
- Source: src/Game/Game.Debug/DebugWatchSystem.cs:469

### The watch's sampling interval is a power of two

- Falls in: the attribute carries `updateInterval` set to a value that is not a power of two, such as `262144 / 24`, `10922` or `10000`; or it is left unset and the owning system's `GetUpdateInterval` returns one.
- Avoids: `updateInterval` is a power of two, such as `8192` or `16384`, or it is left unset and the owning system's `GetUpdateInterval` returns a power of two.
- Reference: cs2-modding/references/technique/debug-menu/debug-menu.md
- Source: src/Game/Game.Debug/DebugWatchSystem.cs:130

### The counting system runs only while someone is watching

- Falls in: the system that counts abandoned buildings is registered with `UpdateAt` and nothing turns it off: no `[DebugWatchOnly]` on its class, and `Enabled` never set to `false`, so its `OnUpdate` runs on its cadence for the whole session whether the menu is open or not.
- Avoids: `[DebugWatchOnly]` on the system class (the game's own such systems also set `Enabled = false` in `OnCreate`, so nothing runs before the menu first builds the tab); or no periodic work exists because the count is computed only when a watch or a button asks for it.
- Reference: cs2-modding/references/technique/debug-menu/debug-menu.md
- Source: src/Game/Game.Debug/DebugWatchSystem.cs:593

### No widget getter runs a query

- Falls in: the `getter` of the `DebugUI.Value` (or `IntField`) showing the count calls `CalculateEntityCount()`, `ToEntityArray`, `ToComponentDataArray`, creates an `EntityQuery`, or completes a `JobHandle`.
- Avoids: the getter returns a field the counting system's `OnUpdate` or a `DebugUI.Button`'s action wrote; the query runs there, never inside the getter.
- Reference: cs2-modding/references/technique/debug-menu/debug-menu.md
- Source: src/Game/Game.UI.Debug/ValueField.cs:30

### The text line is a widget kind the menu renders

- Falls in: the explanatory line is a `DebugUI.MessageBox`, `DebugUI.ObjectField`, `DebugUI.ObjectListField` or `DebugUI.ObjectPopupField`, none of which the widget translation matches.
- Avoids: the line is a `DebugUI.Value` whose `displayName` or string `getter` carries the text, or any other kind the translation matches (`Foldout`, `Container`, `Button`, `BoolField`, `TextField` and the numeric fields).
- Reference: cs2-modding/references/technique/debug-menu/debug-menu.md
- Source: src/Game/Game.UI.Debug/DebugWidgetBuilders.cs:107

## Roots

- decompile
