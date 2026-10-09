# A low-funds alert with an options page and a toggle hotkey

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod. I want it to warn me in the log when my city's money drops below a threshold I set in the mod's options, so I don't bankrupt a city while I'm busy on the other side of the map. The alert should be on by default and the threshold should default to 50,000, with a checkbox in the options to turn the alert off, and a hotkey that toggles it as well, F9 unless I rebind it. Put the hotkey and a button that resets it to its default on their own tab, separate from the alert settings.

## Checklist

### The defaults are seeded by the settings class itself

- Falls in: `SetDefaults()` assigns the default values (`AlertEnabled = true`, `Threshold = 50000`) and nothing in the mod calls it, or `OnLoad` calls it on the live instance alone; the properties carry no initialisers.
- Avoids: the settings constructor calls `SetDefaults()`, or the properties carry initialisers (`{ get; set; } = true`) and `SetDefaults()` repeats or omits them.
- Reference: cs2-modding/references/technique/settings-and-input/settings-and-input.md
- Source: src/Game/Game.Modding/ModSetting.cs:36

### The toggle action is switched on before it is read

- Falls in: the code resolves the action with `GetAction(...)` and polls it (`WasPerformedThisFrame()`, `WasPressedThisFrame()`, `IsPressed()`) or subscribes `onInteraction`, and nowhere assigns `shouldBeEnabled = true` on it.
- Avoids: `shouldBeEnabled = true` is assigned on the resolved action, in `OnLoad`, `OnCreate` or `OnStartRunning`, before or alongside the first read.
- Reference: cs2-modding/references/technique/settings-and-input/settings-and-input.md
- Source: src/Game/Game.Input/ProxyAction.cs:566

### The threshold carries a slider or dropdown attribute

- Falls in: the threshold is a public `int` or `float` property with no `[SettingsUISlider]` and no `[SettingsUIDropdown]`, relying on the type alone to produce a widget; or a `float` property carrying `[SettingsUIDropdown]` with no `[SettingsUISlider]`.
- Avoids: the threshold property is an `int` carrying `[SettingsUISlider(...)]` or `[SettingsUIDropdown(typeof(...), nameof(...))]` with a matching items getter, or a `float` carrying `[SettingsUISlider(...)]`.
- Reference: cs2-modding/references/technique/settings-and-input/settings-and-input.md
- Source: src/Game/Game.UI.Menu/AutomaticSettings.cs:1080

### The reset button is a write-only property

- Falls in: the reset property is a `bool` with `[SettingsUIButton]`, no `[SettingsUIConfirmation]`, and a getter (`{ get; set; }`, `get => false`, or a backing-field getter).
- Avoids: the reset property is a `bool` with a setter and no getter (`public bool ResetHotkeys { set { ... } }`), with or without `[SettingsUIButton]`.
- Reference: cs2-modding/references/technique/settings-and-input/settings-and-input.md
- Source: src/Game/Game.UI.Menu/AutomaticSettings.cs:1220

### The hotkeys tab is named through a section overload that takes a tab

- Falls in: the hotkey and reset properties carry the single-argument `[SettingsUISection("Hotkeys")]` (or a constant naming the tab), while the class lists that name in `[SettingsUITabOrder]`, or the tab is named nowhere but in `[SettingsUITabOrder]`.
- Avoids: those properties carry `[SettingsUISection(tab, group)]` or `[SettingsUISection(tab, simpleGroup, advancedGroup)]` with the hotkeys tab as the first argument.
- Reference: cs2-modding/references/technique/settings-and-input/settings-and-input.md
- Source: src/Game/Game.Settings/SettingsUISectionAttribute.cs:29

## Roots

- decompile
