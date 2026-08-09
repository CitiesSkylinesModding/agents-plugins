# Settings and input

**Baseline.** Decompiled game 1.6.0f1; mod corpus read 2026-08-03 at the commits the 20-repository checkout carried; wiki fetched live 2026-08-03 (the bot challenge did not fire, so no snapshot substitution was needed).
The frontend claims added in the 2026-08-03 re-sweep read the user's own installed copy at 1.6.0f1 — the same build the decompile was taken from — through two artifacts the original pass had no list naming: the shipped UI bundle, cited as a copy of `Cities2_Data/Content/Game/UI/index.js` reformatted with prettier at its defaults and read at `DecompiledCitiesSkylines2/src-ui/source.js`, **135,021 lines**; and the official UI mod scaffold's declarations at `@colossalorder/create-csii-ui-mod/template/types/`, cited package-relative.
Check a fresh reformat against that line count before trusting a line number from the bundle.

## Findings

### The class a mod subclasses, and where its identity comes from

`public abstract class ModSetting : Setting` (`src/Game/Game.Modding/ModSetting.cs:13`), and `public abstract class Setting : IEquatable<Setting>` (`src/Game/Game.Settings/Setting.cs:15`).
The mod-facing half is `ModSetting`; the half that drives the options screen and the save file is `Setting`, which the game's own eleven settings classes share (`SharedSettings.general/audio/gameplay/radio/graphics/editor/userInterface/input/keybinding/benchmark/modding`, `src/Game/Game.Settings/SharedSettings.cs:18-38`).

**The constructor takes the `IMod` and derives two strings from it** (`ModSetting.cs:36-44`):

```
id   = modType.Assembly.GetName().Name + "." + modType.Namespace + "." + modType.Name
name = GetType().Name
```

`id` comes from the **mod** type, `name` from the **settings** type (`:39-40`).
Both carry `[SettingsUIHidden]` so they do not become widgets themselves (`:23-30`, alongside `keyBindingRegistered`).
The constructor also writes `instances[id] = this` into a static registry (`:42`, declared `:17`) and calls `InitializeKeyBindings()` (`:43`).

`id` is used for four different things at once, and a reader who does not know that is surprised by three of them:

- the options-screen page id (`RegisterInOptionsUI()` calls `RegisterInOptionsUI(id, addPrefix: true)`, `:46-49`);
- the prefix on every localization key the page generates (`GetSettingsLocaleID`, `GetOptionLabelLocaleID` and the seven siblings at `:303-371`);
- the **input action map name** for every action the mod declares (`GetAction(name)` is `InputManager.instance.FindAction(id, name)`, `:289-292`);
- the key under which a keybinding-conflict notification is pushed and resolved back to the mod's display name and thumbnail (`src/Game/Game.Input/InputManager.cs:1509`, which looks the map name up in `ModSetting.instances`).

`protected internal sealed override bool builtIn => false` (`ModSetting.cs:21`) against `Setting.builtIn => true` (`Setting.cs:22`), and that one bool is what sorts every mod page after every vanilla page in the options screen (`OptionsUISystem.sortedPages` orders `builtIn descending, index`, `src/Game/Game.UI.Menu/OptionsUISystem.cs:483-486`).

Rots: the `id` composition formula — re-check `src/Game/Game.Modding/ModSetting.cs:39`.

**Two settings classes for one mod share an id and collide.** `id` derives from the `IMod` type, not from the settings type, so a second `ModSetting` subclass in the same mod produces the same `id`, overwrites `instances[id]` (`:42`), and its `RegisterInOptionsUI()` overwrites `pages[page.id]` in the options system (`OptionsUISystem.cs:634`).
No corpus mod does this: all 19 `ModSetting` subclasses across the corpus are one per mod (`Anarchy/Anarchy/Settings/AnarchyModSettings.cs:28`, `AreaBucket/Setting.cs:15`, `BetterBulldozer/BetterBulldozer/Settings/BetterBulldozerModSettings.cs:20`, `CS2-MoveIt/Code/MoveIt/Settings/Settings.cs:36`, `CS2-NetworkTools/NetworkTools.Mod/Settings/Settings.cs:28`, `CS2-Platter/Platter/Settings/PlatterModSettings.cs:30`, `ExtraAssetsImporter/MOD/Setting.cs:14`, `ExtraDetailingTools/MOD/Settings.cs:15`, `FindIt-CSII/FindIt/Setting.cs:15`, `InfoLoom/InfoLoom/Setting.cs:23`, `LineTool-CS2/Code/ModSettings.cs:18`, `NodeController/NodeController/Setting.cs:31`, `PlopTheGrowables/Code/ModSettings.cs:22`, `Recolor/Recolor/Settings/Setting.cs:25`, `RoadBuilder-CSII/RoadBuilder/Setting.cs:14`, `Time2Work/NightShift/Setting.cs:24`, `Traffic/Code/ModSettings.cs:23`, `Tree_Controller/Tree_Controller/Settings/TreeControllerSettings.cs:22`, `Water_Features/Water_Features/Settings/WaterFeaturesSettings.cs:27`).
The twentieth repository, CS2-WriteEverywhere, keeps its settings class inside the closed-source `BasicIMod` framework `mod-lifecycle-and-ordering.md:458` records as absent from the checkout; only its data class `BelzontWE/WEModData.cs:17` is readable here.
Two of the nineteen split the class across `partial` files by concern (`NodeController/NodeController/Setting.cs:31` with `Setting.Keybindings.cs`, `Traffic/Code/ModSettings.cs:23` with `ModSettings.Keybindings.cs`).

### The one abstract member, and the two hooks the game never calls for a mod

`Setting` declares exactly one abstract member: `public abstract void SetDefaults()` (`Setting.cs:163`).

**Nothing in the game ever calls it on a mod's setting.** Several of the game's own settings classes call their own `SetDefaults()` from their own constructors (`src/Game/Game.Settings/AudioSettings.cs:78`, `GameplaySettings.cs:42`, `GeneralSettings.cs:188`, `InputSettings.cs:148`, `EditorSettings.cs:74`, `GraphicsSettings.cs:534/552`), and the only external caller is `SharedSettings`, which iterates the game's own list and no other (`SharedSettings.cs:126-127/136-138`).
So `SetDefaults()` is a member a mod must implement and must also invoke.
Nine of the nineteen corpus settings classes call it, and eight of those nine call it from their own constructor — which is the whole point, because the live instance and the throwaway defaults instance passed to `LoadSettings` are then seeded identically (`Anarchy/Anarchy/Settings/AnarchyModSettings.cs:104-108`, `Water_Features/Water_Features/Settings/WaterFeaturesSettings.cs:88-92`, `BetterBulldozer/.../BetterBulldozerModSettings.cs:26-30`, `InfoLoom/InfoLoom/Setting.cs:269-272`, `NodeController/NodeController/Setting.cs:44-49`, `Recolor/Recolor/Settings/Setting.cs:82-86`, `Traffic/Code/ModSettings.cs:137-144`, `Tree_Controller/.../TreeControllerSettings.cs:50-55`).
The ninth calls it conditionally from a property setter instead (`Time2Work/NightShift/Setting.cs:176`), and two of the eight call it a second time from a reset button (`BetterBulldozer/.../BetterBulldozerModSettings.cs:116`, `Recolor/Recolor/Settings/Setting.cs:124`).
The ten that never call it rely on C# property initialisers, which run for both instances anyway (`CS2-NetworkTools/NetworkTools.Mod/Settings/Settings.cs:58`), or leave `SetDefaults` writing values that are already the CLR defaults (`LineTool-CS2/Code/ModSettings.cs:87-90`).
`public virtual void SetNewPlayerDefaults()` (`Setting.cs:165`) is likewise reached only through `SharedSettings.cs:61/127`, never for a mod.

**`Apply()` and `ApplyAndSave()` are the write path.** `public virtual void Apply()` logs and raises `event OnSettingsAppliedHandler onSettingsApplied` (`Setting.cs:157-161`, event at `:24`); `public virtual async void ApplyAndSave()` calls `Apply()` then `await AssetDatabase.global.SaveSpecificSetting(GetType().Name)` (`:151-155`).
Note the argument: the **settings class's own type name**, which `AssetDatabase.GetTargetSetting` matches against `fragment.source.GetType().Name` (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/AssetDatabase.cs:822-838`) — not the `name` string handed to `LoadSettings`.
Every widget the reflection engine builds calls `itemData.setting.ApplyAndSave()` in its setter, so a value the player changes in the screen is applied and written without the mod doing anything — twelve call sites, one per widget builder (`src/Game/Game.UI.Menu/AutomaticSettings.cs:1212/1265/1300/1337/1369/1395/1457/1483/1514/1540/1551/1593`).
A mod calling `ApplyAndSave()` itself is how a value changed from the mod's own UI reaches disk; seven repositories do — Anarchy, Better Bulldozer, Network Tools, Find It, Recolor, Traffic and Tree Controller (`BetterBulldozer/BetterBulldozer/Systems/BetterBulldozerUISystem.cs:814`, `CS2-NetworkTools/NetworkTools.Mod/Systems/UI/UISystem.Handlers.cs:70/85/100/115`, `FindIt-CSII/FindIt/Systems/FindItUISystem.Setup.cs:66/78`, `Recolor/Recolor/Systems/Palettes/PalettesUISystem.Main.cs:217/548`, among others).
Two repositories override `Apply()` to push the new values into a live system before calling `base.Apply()` (`Traffic/Code/ModSettings.cs:179-190`, which feeds its overlay system; `Time2Work/NightShift/Setting.cs:281`), and two subscribe to `onSettingsApplied` instead (`Traffic/Code/UISystems/ModUISystem.cs:79/291`, `Tree_Controller/Tree_Controller/Settings/TreeControllerSettings.cs:410-414`).
The event form is the one that survives a settings object the subscriber does not own.

### The attribute catalog, grouped by what each class of attribute does

`src/Game/Game.Settings/` holds 38 `SettingsUI*` types at 1.6.0f1, plus `IgnoreEqualsAttribute` and `ModdingToolchainUIButtonAttribute` which are not part of this surface.
Grouping them by the job they do, rather than by the wiki's four headings, is what makes the set learnable — several attributes in the wiki's "modification" bucket do completely different things.

Rots: the whole attribute set, its names and its constructor parameters — re-read `src/Game/Game.Settings/`.

**Widget selectors** (the attribute decides which control the property becomes; see the next finding for how):
`SettingsUIButtonAttribute` (`SettingsUIButtonAttribute.cs:6`, no parameters), `SettingsUISliderAttribute` (`SettingsUISliderAttribute.cs:6`, public fields `min`, `max = 100f`, `step = 1f`, `unit = "integer"`, `scalarMultiplier = 1f`, `scaleDragVolume`, `updateOnDragEnd`), `SettingsUIDropdownAttribute(Type itemsGetterType, string itemsGetterMethod)` (`SettingsUIDropdownAttribute.cs:12`), `SettingsUITextInputAttribute` (`:6`), `SettingsUIDirectoryPickerAttribute` (`:6`), `SettingsUIMultilineTextAttribute(string icon = null)` (`:10`), `SettingsUIConfirmationAttribute(string overrideConfirmMessageId = null, string overrideConfirmMessageValue = null)` (`:11`, which upgrades a button to a confirmed button).

**Formatting of the chosen widget:** `SettingsUICustomFormatAttribute` (`:6`, fields `fractionDigits`, `separateThousands = true`, `maxValueWithFraction = 100f`, `signed`) — setting it replaces the slider's `unit` with the literal `"custom"` and hands the numeric formatting to the frontend (`AutomaticSettings.cs:1305-1311` for the int slider, `:1342-1350` for the float slider).

**Verdict (2026-08-03 re-sweep): the four fields are not symmetric across the two sliders, and the original pass read the attribute rather than the two builders.** The int builder copies `separateThousands` and `signed` only (`AutomaticSettings.cs:1305-1311`); the float builder copies all four, clamping `fractionDigits` at 0 (`:1342-1350`). The two first-party sources outside `src/` agree with the builders and with each other: the scaffold declares `IntSliderField extends IntSliderFieldBase<number>, WarningSign { separateThousands?: boolean; signed?: boolean }` against `FloatSliderField`'s `fractionDigits` (inherited from `FloatSliderFieldBase`), `separateThousands`, `maxValueWithFraction` and `signed` (`@colossalorder/create-csii-ui-mod/template/types/bindings.d.ts:664-697`), and the bundle's slider row destructures exactly the float set (`DecompiledCitiesSkylines2/src-ui/source.js:73256-73273`). So `fractionDigits` and `maxValueWithFraction` on an `int` property are inert, and no source reports it.

**The frontend half of the custom format, first-party.** The slider row branches on `unit === Unit.Custom` and, only then, resolves the locale id `Options.FORMAT[<path>]` with a `"{SIGN}{VALUE}"` default and `VALUE`/`SIGN` substitution (`source.js:73252-73273`), which is the same key `ModSetting.GetOptionFormatLocaleID` builds as `"Options.FORMAT[" + id + "." + name + "." + optionName + "]"` (`src/Game/Game.Modding/ModSetting.cs:338-341`). `localization.md:190-196` owns that key and already states it; recorded here so the attribute's other end is not left dangling.

**Layout and ordering:** `SettingsUISectionAttribute` (`:16-30`, three overloads), `SettingsUITabOrderAttribute(params string[] tabs)` or `(Type checkType, string checkMethod)` (`:15/20`), `SettingsUIGroupOrderAttribute` with the same two shapes (`:15/20`), `SettingsUIShowGroupNameAttribute()` / `(params string[] groups)` (`:13/18`), `SettingsUIButtonGroupAttribute(string name)` (`:10`), `SettingsUIPathAttribute(string overridePath)` (`:10`).

`SettingsUISectionAttribute` is the one whose overloads mislead. `(tab, simpleGroup, advancedGroup)` is the full form; `(tab, group)` sets both groups; and the **single-argument overload names a group, not a tab** — it forwards `this(null, group, group)` and the constructor turns a null tab into the literal `"General"` (`SettingsUISectionAttribute.cs:16-30`, constant `kGeneral` at `:8`).
Both corpus repositories that use the single-argument form name their constants accordingly and land, correctly, in one `General` tab with several groups (`CS2-NetworkTools/NetworkTools.Mod/Settings/Settings.cs:56/73/119/156`, `PlopTheGrowables/Code/ModSettings.cs:47/67`); 23 of the corpus's 521 `[SettingsUISection]` uses take it.

**Visibility and enablement,** and these split by _when_ they are evaluated.
Evaluated once, at page build, and the property is simply skipped: `SettingsUIHiddenAttribute` (`:6`), `SettingsUIDeveloperAttribute` (`:6`, which additionally consults `GameManager.instance.configuration.developerMode`), `SettingsUIPlatformAttribute(Platform platforms, bool debugConditional = false)` (`:19`).
Evaluated every frame the page is open, through a `Func<bool>` the widget holds: `SettingsUIHideByConditionAttribute(Type checkType, string checkMethod[, bool invert])` (`:14/20`) and `SettingsUIDisableByConditionAttribute` with the identical shape (`:14/20`).
Evaluated at page build but affecting placement rather than presence: `SettingsUIAdvancedAttribute` (`:6`, moves the option behind the screen's advanced toggle) and `SettingsUISearchHiddenAttribute` (`:6`, keeps it out of the search index).

**Text:** `SettingsUIDisplayNameAttribute` and `SettingsUIDescriptionAttribute`, each with two constructors — `(string overrideId = null, string overrideValue = null)` and `(Type getterType, string getterMethod)` (`SettingsUIDisplayNameAttribute.cs:16/22`, `SettingsUIDescriptionAttribute.cs:16/22`).

**Reaction:** `SettingsUISetterAttribute(Type setterType, string setterMethod)` (`:12`), `SettingsUIValueVersionAttribute(Type versionGetterType, string versionGetterMethod)` (`:12`).

**Verdict (2026-08-03 re-sweep): "tells the frontend when a dropdown's item list has changed underneath it" was wrong on both halves, and the install is what shows it.** The string `valueVersion` does not occur anywhere in the shipped bundle (grep of `DecompiledCitiesSkylines2/src-ui/source.js`), and neither does `itemsVersion`; nothing about it crosses the boundary. It is a **C#-side change-detection strategy**. `AutomaticSettings.GetValueVersionAction` turns the attribute into a `Func<int>` (`src/Game/Game.UI.Menu/AutomaticSettings.cs:510-520`, stored at `:321`) which is handed to one of two widget slots: `itemsVersion` on `DropdownField<T>` and `EnumField`, and `valueVersion` on `ReadonlyField<T>`. `DropdownField.Update` re-reads its item list only when that int changes, against an `m_ItemsVersion` seeded to `-1` and a `?? 0` fallback (`src/Game/Game.UI.Widgets/DropdownField.cs:10/63-75`) — so **a dropdown with no attribute reads its items exactly once, on its first update, and never again**. `ReadonlyField.Update` uses it the same way over the widget's value, falling back to `ValueEquals` when it is absent, so a value field without one still tracks changes (`src/Game/Game.UI.Widgets/ReadonlyField.cs:17/40-62`). The input-binding field is the one widget that supplies its own default, `InputManager.instance.actionVersion` (`AutomaticSettings.cs:1517/1521-1523`). Whatever the widget then republishes reaches the frontend as an ordinary property update, which is why nothing over there names a version. The game's own two uses are `GraphicsSettings`' resolution dropdowns (`src/Game/Game.Settings/GraphicsSettings.cs:114/135`).

**Warnings:** `SettingsUIWarningAttribute(Type checkType, string checkMethod)` on a property (`:12`), `SettingsUITabWarningAttribute(string tab, Type checkType, string checkMethod)` (`:14`), `SettingsUIPageWarningAttribute(Type checkType, string checkMethod)` (`:12`). Zero corpus uses of all three.

**Persistence:** `SettingsUIForceSaveAttribute` (`:6`) — see the persistence finding.

**Input:** the three action attributes `SettingsUIKeyboardActionAttribute` / `SettingsUIMouseActionAttribute` / `SettingsUIGamepadActionAttribute`, all `[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]` and all deriving from `SettingsUIInputActionAttribute`; the three binding attributes `SettingsUIKeyboardBindingAttribute` / `SettingsUIMouseBindingAttribute` / `SettingsUIGamepadBindingAttribute`, all `[AttributeUsage(AttributeTargets.Property)]` and all deriving from `SettingsUIKeybindingAttribute`; and `SettingsUIBindingMimicAttribute(string map, string action)` (`SettingsUIBindingMimicAttribute.cs:6-16`).
Treated in full below.

**Class-level fallback is not uniform, and this is the single most confusing thing in the catalog.**
`SettingsUISection`, `SettingsUIAdvanced`, `SettingsUISearchHidden`, `SettingsUIDisableByCondition` and `SettingsUIHideByCondition` all check the property first and then fall back to `property.declaringType.GetCustomAttributes(...)` (`AutomaticSettings.cs:411/420/475/498/774`).
`SettingsUIHidden`, `SettingsUIDeveloper` and `SettingsUIPlatform` do **not**: they read the property alone (`:735-742/744-747/749-756`), so putting `[SettingsUIHidden]` on the settings class has no effect at all even though its `AttributeUsage` permits it.
And the fallback target is `declaringType`, not the concrete settings type — a property declared on a shared base class does not pick up a `[SettingsUISection]` written on the derived class.

### The reflection engine: one pass over public instance properties

`Setting.GetPageData(id, addPrefix)` is `virtual` and returns `AutomaticSettings.FillSettingsPage(this, id, addPrefix)` (`Setting.cs:169-172`), so a mod can override it and build a page by hand.
Nothing in the corpus does; the game itself does, in `ModdingSettings.GetPageData`, which calls `base.GetPageData(...)` and then appends and inserts hand-built items (`src/Game/Game.Settings/ModdingSettings.cs:222-258`, `AddItem` at `:240`, `InsertItem(item, 0)` at `:252`).
Each of those items wraps an `AutomaticSettings.ManualProperty` — a hand-built `IProxyProperty` carrying its own getter, setter and attribute list (`ModdingSettings.cs:226/241/263/281/302`, type at `AutomaticSettings.cs:596-651`).
That is the sanctioned escape hatch for a row whose shape no property can express.

The automatic path is `FillSettingsPage(SettingPageData, Setting)` (`AutomaticSettings.cs:985-1047`) and it runs exactly once per registration:

1. Read `[SettingsUITabOrder]` and `[SettingsUIGroupOrder]` off the settings **class**, resolving the getter-method overload first and falling back to the literal array (`:987-1022`). Each name is entered into an ordered dictionary; the index is what later sorts the tabs and the groups.
2. Enumerate `setting.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)` (`:1023`). Inherited properties are included, which is why `ModSetting.id`, `name` and `keyBindingRegistered` need `[SettingsUIHidden]` on them.
3. Skip the property when `IsSupportedOnPlatform` is false, `IsHidden` is true, or `IsDeveloperOnly` is true (`:1027`).
4. `GetWidgetType(property)` (`:1031`, body `:1049-1144`); `WidgetType.None` skips the property.
5. `GetSections(property)` returns one `SectionInfo` per `[SettingsUISection]`, falling back to the declaring type's attributes and then to a synthetic `General` section with empty group names (`:758-794`). **A property carrying several `[SettingsUISection]` attributes appears once per tab**, as a separate `SettingItemData` each time (`:1036-1045`).
6. Each `SettingItemData` resolves its path, display name, description and the seven delegates in its constructor (`:307-325`).

`GetWidgetType` is a flat dispatch on the property's CLR type and its accessors (`:1049-1144`):

| Property type     | Condition                                                                                                 | `WidgetType`                 | Widget built                                                                       |
| ----------------- | --------------------------------------------------------------------------------------------------------- | ---------------------------- | ---------------------------------------------------------------------------------- |
| `bool`            | `[SettingsUIButton]` + `[SettingsUIConfirmation]`                                                         | `BoolButtonWithConfirmation` | `ButtonWithConfirmation` (`:1169-1191`)                                            |
| `bool`            | `[SettingsUIButton]`                                                                                      | `BoolButton`                 | `Button` in a `ButtonRow` (`:1219-1244`)                                           |
| `bool`            | readable **and** writable                                                                                 | `BoolToggle`                 | `ToggleField` (`:1193-1217`)                                                       |
| `bool`            | write-only                                                                                                | `BoolButton`                 | `Button` in a `ButtonRow`                                                          |
| `int`             | `[SettingsUIDropdown]`                                                                                    | `IntDropdown`                | `DropdownField<int>` (`:1246-1272`)                                                |
| `int`             | `[SettingsUISlider]`                                                                                      | `IntSlider`                  | `IntSliderField` (`:1274-1313`)                                                    |
| `float`           | `[SettingsUISlider]`                                                                                      | `FloatSlider`                | `FloatSliderField` (`:1315-1352`)                                                  |
| `string`          | read+write, `[SettingsUITextInput]`                                                                       | `StringTextInput`            | `StringInputField` (`:1354-1374`)                                                  |
| `string`          | read+write, `[SettingsUIDropdown]`                                                                        | `StringDropdown`             | `DropdownField<string>` (`:1376-1402`)                                             |
| `string`          | read+write, `[SettingsUIDirectoryPicker]`                                                                 | `DirectoryPicker`            | `DirectoryPickerField` (`:1527-1555`)                                              |
| `string`          | read-only, `[SettingsUIMultilineText]`                                                                    | `MultilineText`              | `MultilineText` (`src/Game/Game.UI.Widgets/MultilineTextSettingItemData.cs:17-28`) |
| `string`          | read-only, no attribute                                                                                   | `StringField`                | `LocalizedValueField` (`:1404-1421`)                                               |
| `LocalizedString` | read-only                                                                                                 | `LocalizedStringField`       | `LocalizedValueField` (`:1423-1440`)                                               |
| enum              | `[SettingsUIDropdown]`                                                                                    | `AdvancedEnumDropdown`       | `DropdownField<int>` (`:1442-1464`)                                                |
| enum              | no attribute                                                                                              | `EnumDropdown`               | `EnumField` (`:1466-1490`)                                                         |
| `ProxyBinding`    | unconditional                                                                                             | `KeyBinding`                 | `InputBindingField` (`:1492-1525`)                                                 |
| anything else     | `[SettingsUIDropdown]` and the type is `IJsonWritable` + `IJsonReadable` with a parameterless constructor | `CustomDropdown`             | `DropdownField<T>` by reflection (`:1557-1576`)                                    |

Everything else is `WidgetType.None`.
Three consequences worth stating flat.
An `int` or `float` with neither a slider nor a dropdown attribute is silently invisible — there is no plain numeric field.
A `bool` **button** must be write-only: `AddBoolButtonProperty` returns null the moment `canRead` is true (`:1221-1224`), so `[SettingsUIButton]` on a `{ get; set; }` bool produces no widget while the attribute-free version of the same property produces a toggle.
And `ProxyBinding` is the only type whose dispatch ignores the accessors entirely (`:1127-1130`), while the binding machinery itself requires both (`ModSetting.keyBindingProperties` filters `p.CanRead && p.CanWrite`, `ModSetting.cs:32-34`) — a getter-only `ProxyBinding` property therefore yields a keybinding row over an unregistered `default(ProxyBinding)`.

**Buttons group by side effect.** `GetButtonsGroup(name, out ButtonRow, Button)` keeps a static dictionary for the duration of one `FillSettingsPage` call, cleared at both ends (`:718-733`, `:958/981`).
The first button under a given name returns the `ButtonRow`; every later one is appended to that row's children and its own `GetWidget()` returns null (`:725/1186-1190/1239-1243`).
The key is `[SettingsUIButtonGroup].name`, defaulting to `declaringType.Name + "." + propertyName + "_ButtonGroup"` — so an ungrouped button is a row of one, and a grouped row takes the position of its **first** member.
Zero corpus uses of `[SettingsUIButtonGroup]`.

**Where the text comes from.** `path` is `[SettingsUIPath].path`, or `prefix + "." + declaringType.Name + "." + propertyName` where `prefix` is the page id when `addPrefix` is set (`:327-339`, `SettingPageData.prefix` at `:65-75`).
Display name and description default to `LocalizedString.Id("Options.OPTION[" + path + "]")` and `"Options.OPTION_DESCRIPTION[" + path + "]"` (`:341-373`).
Those are exactly what `ModSetting.GetOptionLabelLocaleID(optionName)` and `GetOptionDescLocaleID` build — `"Options.OPTION[" + id + "." + name + "." + optionName + "]"` (`ModSetting.cs:308-316`) — **provided `name` and `declaringType.Name` agree**, which they do only for a property declared on the concrete settings class. A property inherited from a shared settings base gets a widget path carrying the base's name and a helper-generated key carrying the derived one, and the label renders as its raw key.
Enum members resolve through `GetEnumValues(enumType, "Options." + prefix)` to `"Options.<id>.<ENUMTYPENAME>[<Member>]"` (`:857-917`, key built at `:908-912`), matching `ModSetting.GetEnumValueLocaleID<T>` (`ModSetting.cs:333-336`); a `[SettingsUIHidden]` on an enum **field** drops that member from the dropdown (`:906`).
The strings behind these keys are `localization`'s topic, not this one.

**`TryGetAction<T>` is the resolver behind every `Type`+`methodName` attribute** (`:796-834`). It searches methods first, then read-only properties, matching on name, exact return type `T` and zero parameters; instance members are only searched when `type.IsInstanceOfType(setting)`.
It swallows a miss (returns false, the delegate stays null) and only throws when reflection itself fails, wrapping the cause as `$"TryGetAction error with {name} in {type}"` (`:828-831`).
`GetSetterAction` is the stricter sibling: it demands a `void` method with one parameter, accepting the property's type, `int`, or the enum's underlying type for an enum property (`:423-462`).

**One `string`-typed pair reads as a bug and is not.** A read-only `string` property renders its _runtime value_ through a `LocalizedValueField` (`:1404-1421`, `LocalizedString.Value(...)`), which is how the corpus's ubiquitous `public string Version => Mod.Version;` rows work (`Anarchy/Anarchy/Settings/AnarchyModSettings.cs:250-251`, `CS2-NetworkTools/NetworkTools.Mod/Settings/Settings.cs:147/149/151`).
Adding `[SettingsUIMultilineText]` to the same property makes the widget render the _localized string for its path_ and ignore the value entirely (`MultilineTextSettingItemData.cs:17-28`, which passes `displayName` and never touches the accessor). Seven repositories use the multiline form for a paragraph of prose or a linked image (`CS2-MoveIt`, `InfoLoom`, `LineTool-CS2`, `Time2Work`, `Traffic`, `Tree_Controller`, `Water_Features`).

### The page's shape: one page, tabs as sections, groups as labelled runs

The screen's model is three levels deep and each level is named differently in the code than in the UI.

A **page** is one mod's whole entry in the options list (`OptionsUISystem.Page`, `OptionsUISystem.cs:31-112`).
A **section** is what the player sees as a tab down the left of the page; there is one per distinct tab name, built from a `SettingTabData` (`Section`, `:115-325`; `BuildTab` at `AutomaticSettings.cs:215-239`).
Its id is `pageData.id + "." + tabId` when the prefix is on (`:218`), which is exactly what `ModSetting.GetOptionTabLocaleID(tabName)` builds (`ModSetting.cs:323-326`).
A **group** is not an object at all: it is an integer index carried on each option, and the renderer emits a `Breadcrumbs` separator when consecutive options disagree about it _and_ the index resolves to a known group name (`Section.GetItems`, `OptionsUISystem.cs:148-180`, the separator at `:162-175`, the name guard at `:165`). An option whose group name was never registered carries `int.MaxValue` (`AutomaticSettings.cs:232-233`) and therefore gets no separator at all.

**Ordering is by first mention, not alphabetical.** `SettingPageData.AddTab` and `AddGroup` are `TryAdd(name, count)` into an ordered dictionary (`AutomaticSettings.cs:156-164`), populated first from `[SettingsUITabOrder]` and `[SettingsUIGroupOrder]` and then from each property as it is scanned (`:987-1022`, `:1043-1044`).
Tabs sort by that index with unlisted names last (`SortTabs`, `:124-138`); options sort by it inside a tab (`OptionsUISystem.cs:152-155`).
So the class-level order attributes are the only reliable control, and a group named only by a property lands after every group the attribute listed, in property-declaration order.

**Each option carries two group indexes, and the advanced toggle picks which.** `simpleGroupIndex` comes from `[SettingsUISection]`'s `simpleGroup`, `advancedGroupIndex` from its `advancedGroup` falling back to `simpleGroup` (`AutomaticSettings.cs:232-233`), and `Option.GetGroupIndex(isAdvanced)` chooses (`OptionsUISystem.cs:342-349`).
In simple mode the renderer additionally drops every option marked `isAdvanced` (`:152-155`); in advanced mode it shows all of them and regroups by the second index.
That is the whole of what the three-argument `[SettingsUISection(tab, simpleGroup, advancedGroup)]` overload buys: an option that sits in one group for a casual player and another for someone who has turned advanced on.

**A group's heading only renders when the class asks for it.** `Section.GetItems` attaches a `Label` to the separator only when the group name is in `groupToShowName`, and emits a bare separator otherwise (`:165-173`), and that set comes from `[SettingsUIShowGroupName]` — the parameterless form sets `showAllGroupNames` and the `params string[]` form names individual groups (`AutomaticSettings.cs:919-931`, `:964-977`, `groupToShowName` at `:87-97`).
The label's key is `"Options.GROUP[" + pageId + "." + groupId + "]"` (`BuildGroupLabel`, `OptionsUISystem.cs:1042-1051`), matching `ModSetting.GetOptionGroupLocaleID` (`ModSetting.cs:328-331`).
15 of 20 repositories carry `[SettingsUIShowGroupName]` and 17 carry `[SettingsUIGroupOrder]`, which together are the corpus's near-universal page-shape idiom: name every group in both attributes, in the same order.

**Corpus: the conventional page is two tabs and a handful of groups, all as `const string`.**
`Traffic/Code/ModSettings.cs:19-22` is the fullest instance — `[SettingsUITabOrder(GeneralTab, KeybindingsTab)]`, then `[SettingsUIGroupOrder]` and `[SettingsUIShowGroupName]` over the same nine group constants, with the constants declared on the class (`:28-34`, and four more at `ModSettings.Keybindings.cs:37-40`) and referenced from every `[SettingsUISection]`.
`CS2-MoveIt/Code/MoveIt/Settings/Settings.cs:13-15` is the same shape at two tabs and five groups, and is the corpus's only user of `[SettingsUIAdvanced]`.
An `About` group holding read-only `string` rows is close to universal (`Anarchy/Anarchy/Settings/AnarchyModSettings.cs:250-251`, `CS2-NetworkTools/NetworkTools.Mod/Settings/Settings.cs:119/147/149/151`, `Traffic/Code/ModSettings.cs:113/116/119`), and a `Keybindings` tab holding nothing but `ProxyBinding` properties is the convention wherever a mod has more than one or two (`Traffic/Code/ModSettings.Keybindings.cs:42-107`, `NodeController/NodeController/Setting.Keybindings.cs`).

### Debugging a widget that never appears

The engine reports nothing when it drops a property, so the diagnosis is a walk down the same path in order. In the order the code takes them:

1. **The property is not public, or not an instance property.** `GetProperties(BindingFlags.Instance | BindingFlags.Public)` (`AutomaticSettings.cs:1023`).
2. **It is filtered before dispatch** — `[SettingsUIPlatform]` excludes the running platform, `[SettingsUIHidden]` is present, or `[SettingsUIDeveloper]` is present and `GameManager.instance.configuration.developerMode` is false (`:1027`, `:735-756`).
3. **`GetWidgetType` returned `None`** — the type/accessor/attribute combination is not in the table above (`:1049-1144`). A numeric property with no slider or dropdown, a read-write `string` with no attribute, and any collection type all land here.
4. **The builder returned null even though the type matched.** `AddBoolButtonProperty` rejects a readable property (`:1221`); `AddIntDropdownProperty`, `AddStringDropdownProperty` and `AddCustomDropdownProperty` reject a missing `[SettingsUIDropdown]` (`:1248/1378/1559`); `AddIntSliderProperty` rejects a missing `[SettingsUISlider]` (`:1277`); `AddCustomDropdownProperty` also rejects a type that is not both `IJsonWritable` and `IJsonReadable` with a parameterless constructor (`:1563-1574`); a second button in a group returns null by design (`:1239-1243`); and the base `SettingItemData.GetWidget()` maps `WidgetType.MultilineText` to null, which only the `MultilineTextSettingItemData` subclass rescues (`:303`, `MultilineTextSettingItemData.cs:17`).
   `SettingTabData.BuildTab` skips every item whose `widget` is null without a word (`:222-237`).
5. **The widget exists and hides itself.** `[SettingsUIHideByCondition]`'s delegate is re-evaluated by `Page.UpdateVisibility` → `Section.UpdateVisibility` → `Widget.UpdateVisibility()` on every frame the page is open (`OptionsUISystem.cs:50-58/182-231/920-946`), and a section with no visible option is itself invisible (`:204-208`).
6. **The page was never registered**, or was registered before the ECS world existed — `Setting.RegisterInOptionsUI` resolves `World.DefaultGameObjectInjectionWorld?.GetOrCreateSystemManaged<OptionsUISystem>()` and **returns false silently** when the world is null (`Setting.cs:179-188`). The return value is discarded by `ModSetting.RegisterInOptionsUI()` (`ModSetting.cs:46-49`), so there is no signal at all.
7. **The label is missing, not the widget.** A row whose localization key has no entry renders the raw key; that is `localization`'s failure mode, reached through the path/key mismatch described above.

An attribute whose `Type`+`methodName` pair does not resolve never fails loudly either: `TryGetAction` returns false and the widget simply has no setter, no disable condition or no item list (`:796-834`). A dropdown whose getter returns the wrong element type — anything other than `DropdownItem<T>[]` for the matching `T` — gets a null `itemsAccessor` and renders empty (`:836-844`, and the constructor's own type check at `:661-673` is only reached on the other overload).

### Registering and unregistering the page

`public void RegisterInOptionsUI()` and `public void UnregisterInOptionsUI()` are the two members a mod calls (`ModSetting.cs:46-54`); the underlying `Setting.RegisterInOptionsUI(string name, bool addPrefix)` is `internal` and unreachable, so a mod cannot choose its own page id or turn the prefix off (`Setting.cs:174-177`).

`OptionsUISystem.RegisterSetting(Setting, string id, bool addPrefix)` (`OptionsUISystem.cs:614-641`) builds the page from scratch — `setting.GetPageData(id, addPrefix).BuildPage()` — stamps `page.builtIn = setting.builtIn`, keeps the existing index if a page with that id was already registered and otherwise appends, indexes the sections, runs the three update passes, replaces `pages[page.id]`, and updates the binding.
So **the page is a snapshot**: tab order, group order, section membership and the widget set are all frozen at registration, and the only way to change them is to call `RegisterInOptionsUI()` again, which rebuilds in place and keeps the page's position.
What _is_ live is per-frame: `[SettingsUIHideByCondition]`, `[SettingsUIDisableByCondition]`, the display-name and description getters, and the warning getters (`:920-946`).

`OptionsUISystem.UnregisterSettings(string id)` is `pages.Remove(id)`, a re-selection if the removed page was the active one, and two refreshes (`:643-652`).
It does **not** touch the mod's input actions: `InputManager` has no removal API at all — grepping `src/Game/Game.Input/` for a `RemoveAction`, `RemoveActions` or map removal returns nothing, against `AddActions` at `InputManager.cs:663` and `ProxyActionMap.AddAction` at `ProxyActionMap.cs:104`.
An action a mod registers is in the input system for the rest of the process.

**Corpus: 15 of 20 unregister in `OnDispose`**, in the shape `if (Settings != null) { Settings.UnregisterInOptionsUI(); Settings = null; }` (`AreaBucket/Mod.cs:105-109` is representative; the same at `Anarchy/Anarchy/AnarchyMod.cs:165`, `BetterBulldozer/.../BetterBulldozerMod.cs:139`, `CS2-MoveIt/Code/MoveIt/Mod.cs:102`, `CS2-Platter/Platter/PlatterMod.cs:143`, `FindIt-CSII/FindIt/Mod.cs:144`, `LineTool-CS2/Code/Mod.cs:133`, `NodeController/NodeController/Mod.cs:143`, `PlopTheGrowables/Code/Mod.cs:96`, `Recolor/Recolor/Mod.cs:169`, `RoadBuilder-CSII/RoadBuilder/Mod.cs:87`, `Time2Work/NightShift/Mod.cs:187`, `Traffic/Code/Mod.cs:125`, `Tree_Controller/.../TreeControllerMod.cs:142`, `Water_Features/.../WaterFeaturesMod.cs:170`).
`ExtraAssetsImporter` and `ExtraDetailingTools` register and never unregister; `mod-lifecycle-and-ordering.md:420` records the same count from the disposal side.

**The `OnLoad` sequence, and why its two orderings both work.** The corpus writes the settings block in two orders.
Register-keys-then-load: `Traffic/Code/Mod.cs:55-58`, `NodeController/NodeController/Mod.cs:45-57`.
Load-then-register-keys: `Anarchy/Anarchy/AnarchyMod.cs:93-126`, `CS2-Platter/Platter/PlatterMod.cs:233-237`, `LineTool-CS2/Code/Mod.cs:107-117`.
Both are correct, and the reason is `[AfterDecode] protected internal void ApplyKeyBindings()` (`ModSetting.cs:269-277`): when the JSON is written into the settings object, that hook fires and, **only if `keyBindingRegistered` is already true**, pushes the loaded bindings into `InputManager.SetBindings`. Register first and the load path repairs the actions; register second and `RegisterKeyBindings` reads the already-deserialized property values and builds the actions from them directly.
`RegisterKeyBindings` is idempotent by an explicit guard (`:143-146`).

### Persistence: the `.coc` file, its format, and the moment it is written

`[FileLocation(string fileName)]` lives in `Colossal.IO.AssetDatabase` and forces the extension: `fileName = Path.ChangeExtension(fileName, ".coc")` (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/FileLocationAttribute.cs`).
It is `[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]`, and the path it carries is relative to the `User` database's root, which is `EnvPath.kUserDataPath` = `Application.persistentDataPath` (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/User.cs:11`, `src/Colossal.PSI.Common/Colossal.PSI.Environment/EnvPath.cs:85`) — on Windows, `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II`. `User.canWriteSettings => true` is what makes that database the one settings are written back to (`User.cs:15`).

Verdict: `Naming Folder And Files` (https://cs2.paradoxwikis.com/Naming_Folder_And_Files) presents `ModsSettings/YourMod/YourMod.coc` as the standard location, and the decompile shows it is a pure convention rather than a mechanism — nothing in `FileLocationAttribute` or in the `User` data source knows the folder name, and any relative path under `EnvPath.kUserDataPath` works identically (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/FileLocationAttribute.cs`, `User.cs:11`).
The wiki is describing an agreement among mod authors, and the corpus shows the agreement is only partly kept.

**Corpus: 20 of 20 carry a `FileLocation`, and four different naming conventions are live.**
A bare mod name at the root: `AreaBucket/Setting.cs:11`, `CS2-MoveIt/Code/MoveIt/Settings/Settings.cs:12`, `CS2-NetworkTools/.../Settings.cs:14`, `ExtraAssetsImporter/MOD/Setting.cs:11`, `FindIt-CSII/FindIt/Setting.cs:10`, `InfoLoom/InfoLoom/Setting.cs:19`, `LineTool-CS2/Code/ModSettings.cs:17`, `NodeController/NodeController/Setting.cs:26`, `PlopTheGrowables/Code/ModSettings.cs:18`, `Traffic/Code/ModSettings.cs:19`.
An author-prefixed flat name: `Anarchy/.../AnarchyModSettings.cs:19` (`"Mods_Yenyang_Anarchy"`), `BetterBulldozer/.../BetterBulldozerModSettings.cs:19`, `Recolor/Recolor/Settings/Setting.cs:23`, `Water_Features/.../WaterFeaturesSettings.cs:22`, `CS2-WriteEverywhere/BelzontWE/WEModData.cs:17` (`"K45_WE_settings"`).
The `ModsSettings/<Mod>/<Mod>` convention the wiki documents: `CS2-Platter/Platter/Settings/PlatterModSettings.cs:26`, `RoadBuilder-CSII/RoadBuilder/Setting.cs:9`, `Time2Work/NightShift/Setting.cs:21` (with a backslash separator rather than a forward slash), `Tree_Controller/.../TreeControllerSettings.cs:19` (`"ModsSettings/yenyang/Tree_Controller"`, an author folder rather than a mod folder).
`ExtraAssetsImporter/MOD/Setting.cs:10` carries the `ModsSettings` form commented out directly above the flat one it actually uses, which is the migration this convention asks for, left undone.

**A `.coc` file is a sequence of `<name>` lines each followed by a `{ ... }` JSON block.** `COCParser.Parse` reads a name, then a brace-delimited object, and returns a dictionary from name to the block's span (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/COCParser.cs:68-144`, name parsing at `:146`); `DefaultAssetFactory.CreateSettingAssets` turns each entry into a `SettingAsset` named for that key and adds the raw JSON substring as a `Fragment` (`DefaultAssetFactory.cs:187-229`, fragment added at `:211`).
**That name is the first argument to `LoadSettings`**, not the type name and not the file name — so `AssetDatabase.global.LoadSettings("AnarchyMod", Settings, new AnarchyModSettings(this))` writes a block headed `AnarchyMod` inside `Mods_Yenyang_Anarchy.coc` (`Anarchy/Anarchy/AnarchyMod.cs:125`). Several settings classes sharing one `FileLocation` therefore coexist as separate named blocks in one file, which is what the wiki's "a mix of JSON and group/section names" describes.
The writer confirms it: `SettingAsset.Save` writes the asset name and then the JSON as two consecutive lines through `SaveSettingsHelper.WriteAsync` (`SettingAsset.cs:180-195`, helper at `Colossal.IO.AssetDatabase.Internal/SaveSettingsHelper.cs:26-45`).
`kExtension = ".coc"` and `kExtensionBackup = ".coc~"` (`SettingAsset.cs:77-79`).

Verdict: `Creating a Settings File` (https://cs2.paradoxwikis.com/Creating_a_Settings_File) says "it won't serialize properties that match the default value", and the decompile confirms it with a condition the page omits — the comparison is against the object handed to `LoadSettings` as its third argument, so a mod that omits that argument gets no diff at all.

**Only non-default values are written**, and the reference for "default" is the third `LoadSettings` argument. `LoadSettings(string name, object obj, object defaultObj = null, bool userSetting = false)` stores `defaultObj` on each fragment (`AssetDatabase.cs:613-666`, assignment at `:632/663`); `SettingAsset.SaveWithPersist` then calls `DiffUtility.DiffWithPersistent(fragment.source, fragment.@default, ...)` unless the database is configured to save everything (`SettingAsset.cs:217-238`).
**Passing `null` for the defaults writes every property, every save**: `DiffObject` returns its result whenever `defaultObject == null`, regardless of whether anything differs (`src/Colossal.Core/Colossal.Json/DiffUtility.cs:440-487`, the branch at `:458`).
`[SettingsUIForceSave]` overrides the diff for one property, re-adding it to the written object at its current value (`DiffWithForceSave`, `:158-197`); note that it is discovered by a **string match on the attribute type name** containing `"ForceSaveAttribute"` and only on public readable **properties**, so the class-level usage its `AttributeUsage` permits does nothing (`GetForceSaveProperties`, `:250-265`). Zero corpus uses.
And when the resulting JSON trims to `{}`, the writer is handed null and `SaveSettingsHelper.Dispose` **deletes the file** (`SettingAsset.cs:185-193`, `SaveSettingsHelper.cs:48-66`) — an all-default settings file does not exist on disk.

**When it is written:** `Setting.ApplyAndSave()` → `AssetDatabase.global.SaveSpecificSetting(GetType().Name)`, which enqueues a `"SaveSettings"` task, re-reads the target file, and re-saves every setting that writes to that same file so the untouched blocks survive (`AssetDatabase.cs:792-820`).
It is `async void` at the `Setting` level (`Setting.cs:151`), so a caller cannot await it.
The whole-database `SaveSettings()` and `SaveAllSettings()` exist as well (`:692-726`).

**Keybindings persist inside the mod's own settings object, as `ProxyBinding` properties.** `ProxyBinding` is a struct whose `[Include]`d fields are `m_MapName`, `m_ActionName`, `m_Component`, `m_Name`, `m_Device`, `m_Path` and `m_Modifiers`; `m_OriginalPath`, `m_OriginalModifiers` and the `CompositeInstance` source are `[Exclude]` (`src/Game/Game.Input/ProxyBinding.cs:12/239-268`).
So a deserialized binding carries no source and reports `rebindOptions = None`, `modifierOptions = Disallow` and `usages = empty` until it is matched back against a registered action (`:320-322/389`).
The game's own rebinds live elsewhere and are not mixed in: `KeybindingSettings.bindings` persists `InputManager.GetBindings(Effective, OnlyRebound | OnlyBuiltIn)` into the game's `Settings.coc` (`src/Game/Game.Settings/KeybindingSettings.cs:8-27`).

### Declaring input actions: two attribute layers, and what each supplies

An action needs two declarations and they sit at different levels.

**The property level declares a binding**, one `ProxyBinding` property per device-and-component: `[SettingsUIKeyboardBinding]`, `[SettingsUIMouseBinding]`, `[SettingsUIGamepadBinding]`, all deriving from `SettingsUIKeybindingAttribute(actionName, device, type, component)` (`src/Game/Game.Settings/SettingsUIKeybindingAttribute.cs:20-25`).
Each has six constructors, whose shape is the whole grammar (`SettingsUIKeyboardBindingAttribute.cs:138-178`): `(actionName)`, `(AxisComponent, actionName)`, `(Vector2Component, actionName)`, and the same three again with a leading default key plus `alt`, `ctrl`, `shift` bools.
The component overload is what sets the action type — `AxisComponent` implies `ActionType.Axis`, `Vector2Component` implies `ActionType.Vector2`, the plain overload implies `ActionType.Button` (`:139/144/149`).
`actionName` defaults to the property name when omitted (`ModSetting.cs:78/87/96`).
The default key resolves through a switch to a Unity control path such as `"<Keyboard>/f2"` (`SettingsUIKeyboardBindingAttribute.cs:100-117`), and the modifiers to `"<Keyboard>/shift"`, `"<Keyboard>/ctrl"`, `"<Keyboard>/alt"` (`:119-136`).
`BindingKeyboard` has 96 members, `None = 0` through `OEM5 = 110` (`src/Game/Game.Input/BindingKeyboard.cs`), `BindingMouse` six — `None`, `Left = 1`, `Right = 2`, `Middle = 3`, `Forward = 4`, `Backward = 5` (`BindingMouse.cs:3-11`) — and `BindingGamepad` 23 distinct values with three alias sets layered on the face buttons: `North/East/South/West`, `Y/B/A/X` and `Triangle/Circle/Cross/Square` all resolve to 5/6/7/8 (`BindingGamepad.cs:3-33`).

Rots: the three binding enums and the control-path strings they map to — re-read `src/Game/Game.Input/BindingKeyboard.cs`, `BindingMouse.cs`, `BindingGamepad.cs` and `src/Game/Game.Settings/SettingsUIKeyboardBindingAttribute.cs:22-117`.

**The class level declares the action** the bindings feed, and is the only place its behaviour can be set: `[SettingsUIKeyboardAction]`, `[SettingsUIMouseAction]`, `[SettingsUIGamepadAction]`, all `AllowMultiple = true` on the class and all deriving from `SettingsUIInputActionAttribute` (`src/Game/Game.Settings/SettingsUIInputActionAttribute.cs:7-71`).
Its readonly fields are `name`, `device`, `type`, `rebindOptions`, `modifierOptions`, `canBeEmpty`, `developerOnly`, `mode`, `interactions`, `processors` and a `usages` property (`:19-51`), and its documented defaults are constants on the type: `kDefaultRebindOptions = RebindOptions.All`, `kDefaultModifierOptions = ModifierOptions.Allow`, `kDefaultCanBeEmpty = true`, `kDefaultDeveloperOnly = false`, `kDefaultMode = Mode.DigitalNormalized` (`:9-17`).
The keyboard and gamepad variants differ only in the device and the default `Mode` — `DigitalNormalized` for keyboard, `Analog` for gamepad (`SettingsUIKeyboardActionAttribute.cs:9`, `SettingsUIGamepadActionAttribute.cs:9`).
`RebindOptions` is `[Flags] { None = 0, Key = 1, Modifiers = 2, All = 3 }` (`src/Game/Game.Input/RebindOptions.cs:8-13`), `ModifierOptions` is `{ Disallow, Allow, Ignore }` (`ModifierOptions.cs:3-8`), `Mode` is `{ DigitalNormalized, Digital, Analog }` (`Mode.cs:3-8`), `ActionType` is `{ Button, Axis, Vector2 }` (`ActionType.cs:3-8`).

**Declaring the binding without the class-level action is legal and quietly takes every default.** `RegisterKeyBindings` looks the attribute up by `(actionName, device)` and falls back to `RebindOptions.All` + `ModifierOptions.Allow` alone when it finds none — no usages block, no interactions, no processors, and `canBeEmpty` left at the `CompositeInstance` default (`ModSetting.cs:183-197`).
That is why 11 repositories declare bindings while only 7 declare keyboard actions.

**`ModifierOptions` decides whether modifiers are matched at all**, and this is the field most likely to surprise.
Only `Allow` adds modifier parts to the composite: `CreateCompositeBinding` attaches every supported modifier for the device and marks each one with a prohibition processor unless the user's binding names it (`InputManager.cs:2076-2099`, the guard at `:2090`).
The supported set is `shift`/`ctrl`/`alt` for keyboard and mouse and the two stick presses for gamepad (`kModifiers`, `:206-219`).
So with `Allow`, an action bound to plain `E` does **not** fire while Ctrl is held; with `Disallow` or `Ignore`, no modifier parts exist and the action fires regardless.
`Disallow` and `Ignore` differ only in what they tell the rebinding UI — `ProxyBinding.disallowModifiers` and `ignoreModifiers` have no C# consumer in `src/Game/` (`ProxyBinding.cs:314-318`).

**`RegisterKeyBindings()` is the whole registration, in one pass** (`ModSetting.cs:141-231`).
It groups the `ProxyBinding` properties by `actionName`, skipping any whose component implies a different `ActionType` than the group already has (`:167-170`) — which is the mechanism that lets one action carry several bindings: two properties naming the same action with `AxisComponent.Negative` and `AxisComponent.Positive` become the two halves of one axis, and four `Vector2Component` properties become one vector.
Per device it builds one `CompositeInstance` with `builtIn = false` (`:179-182`), copies the class-level attribute's options into it (`:183-192`), and finally hands the assembled `ProxyAction.Info[]` to the internal `InputManager.AddActions` (`:210`).
Then it creates a watcher per property so a rebind writes the new `ProxyBinding` straight back into the property (`:212-218`) — which is why the value the player sets survives into the next `ApplyAndSave`.
Component-to-binding-name is fixed by the composite table: `Press` → `"binding"`, `Negative`/`Positive` → `"negative"`/`"positive"`, `Up`/`Down`/`Left`/`Right` → the lowercase name (`src/Game/Game.Input/ButtonWithModifiersComposite.cs`, `AxisSeparatedWithModifiersComposite.cs`, `Vector2SeparatedWithModifiersComposite.cs`, each in `GetCompositeData()`; component→type mapping at `src/Game/Game.Input/CompositeUtility.cs:38-51`).
Those strings are the last segment of `GetBindingKeyLocaleID(actionName, component)` → `"Options.OPTION[" + id + "/" + actionName + "/" + componentName + "]"` (`ModSetting.cs:343-361`), and `GetBindingKeyHintLocaleID` → `"Common.ACTION[" + id + "/" + actionName + "]"` (`:363-366`) is the one an input-hint tooltip reads.

`protected void ResetKeyBindings()` regenerates every binding from its attributes, pushes them through `SetBindings` and calls `ApplyAndSave()` (`:279-287`) — the standard "reset key bindings" button, exposed by `Traffic/Code/ModSettings.Keybindings.cs:108-120` as a write-only `bool`.

### Usage contexts, and how a conflict with a vanilla binding actually resolves

A usage is a **string**, interned into a global index on first use (`Usages.AddOrGetUsage`, `src/Game/Game.Input/Usages.cs:313-321`) and stored as a bitset per composite.
Eleven names are built in as constants: `kMenuUsage = "Menu"`, `kDefaultUsage = "DefaultTool"`, `kOverlayUsage = "Overlay"`, `kToolUsage = "Tool"`, `kCancelableToolUsage = "CancelableTool"`, `kDebugUsage = "Debug"`, `kEditorUsage = "Editor"`, `kPhotoModeUsage = "PhotoMode"`, `kOptionsUsage = "Options"`, `kTutorialUsage = "Tutorial"`, `kDiscardableToolUsage = "DiscardableTool"` (`Usages.cs:44-64`), mirrored as `[Flags] BuiltInUsages` (`BuiltInUsages.cs:5-21`).
An action attribute that names no custom usage gets `Usages.defaultUsages`, which is `BuiltInUsages.DefaultSet = 0x41E` — **five** members: `DefaultTool | Overlay | Tool | CancelableTool | DiscardableTool` (`SettingsUIInputActionAttribute.cs:41-51`, `Usages.cs:72`, `BuiltInUsages.cs:20`).
Custom strings are free-form: `Traffic/Code/ModSettings.Keybindings.cs:177-183` declares `"Traffic.Tool"`, `"Traffic.Tool.Priorities"`, `"Traffic.Tool.LaneConnector"` and `"Traffic.Tool.SelectedIntersection"` (the fourth is declared and never used on an action); `ExtraDetailingTools/MOD/Settings.cs:11-14` uses `"EDT.InTransformTool"`; `CS2-MoveIt/Code/MoveIt/Settings/Settings.cs:16-35` puts every one of its twenty actions on a single `"MoveIt_Input"`.

Rots: the eleven usage constants and `DefaultSet`'s membership — re-read `src/Game/Game.Input/Usages.cs:44-64` and `BuiltInUsages.cs:20`.

**Resolution is a three-tier sweep, and mod actions are always the bottom tier.**
`InputConflictResolution.RefreshActions` partitions every action in the manager into system actions, UI actions and mod actions, on `isBuiltIn` first and `isSystemAction` second (`src/Game/Game.Input/InputConflictResolution.cs:116-136`).
`isBuiltIn` is a property of the action's composites, deserialized from the input asset (`ProxyAction.cs:221`); an action a mod registers is never built-in because `RegisterKeyBindings` stamps `builtIn = false` on the `CompositeInstance` it creates (`ModSetting.cs:181`).
`isSystemAction` is built-in **and** either flagged or alias-free, where the flag is set for six named maps — `"Splash screen"`, `"Engagement"`, `"Camera"`, `"Tool"`, `"Editor"`, `"Benchmark"` (`ProxyAction.cs:235-249`, assignment at `:640`).
`ResolveConflicts` then runs system-versus-UI, system-versus-mod and UI-versus-mod, and never mod-versus-mod (`:138-198`).
A losing action gets `m_HasConflict = true`, which makes `State.enabled` false, which `Apply()` turns into `ProxyAction.ApplyState(false, ...)` and a real `InputAction.Disable()` (`:14-24/37-40`, `ProxyAction.cs:577-599`).

Verdict: the wiki's priority sentence is verified, and understates the hierarchy in one direction.
`Mod Key Binding` (https://cs2.paradoxwikis.com/Mod_Key_Binding) says "Built-in game bindings have higher priority to mod bindings and all conflicted mod bindings will be disabled. Between mods, conflicts are marked but remain enabled."
The decompile confirms both halves at `InputConflictResolution.cs:138-198`, and adds a tier the page does not mention: non-system built-in actions beat mod actions as well, and system actions beat those in turn.
The decompile wins on the detail and the wiki is right on the substance.

**Verdict on usages and conflict resolution.** The same wiki page says "Conflict detection uses usage strings" and lists four defaults — "Default, Overlay, Tool, and CancelableTool".
The decompile splits that claim in two and corrects the list.
The **displayed** conflict is usage-aware: `ProxyBinding.hasConflicts` and `ProxyBinding.conflicts` both call `ConflictsWith(x, y, checkUsage: true)`, which requires `Usages.TestAny(x.usages, y.usages)` on top of a matching device and control path (`ProxyBinding.cs:447/492`, `:680-698`).
The **runtime disable** is not: `InputConflictResolution` goes through `InputManager.HasConflicts`, which calls `ConflictsWith(x, y, checkUsage: false)` (`InputManager.cs:718-755`, the call at `:746`).
What scopes the disable instead is enablement — `ResolveConflicts` only considers actions whose `preResolvedEnable` is currently true (`InputConflictResolution.cs:148-182`), and that is decided by the action's map, its activators and its input barriers (`ProxyAction.UpdateState`, `ProxyAction.cs:524-575`), not by its usages.
The default set is also five members rather than four; the wiki's list drops `DiscardableTool` and writes `Default` for the constant whose value is `"DefaultTool"`.
The decompile wins.
**Verdict: a third consumer exists outside those two namespaces, and an earlier pass of this file recorded the opposite.** `src/Game/Game.UI.Menu/InputRebindingUISystem.cs` reads `Usages` at eleven sites (`:443/444/454/463/469/472/638/644/679/684/723`). The gate is `:644`, `if (!Usages.TestAny(usages, y.usages)) { continue; }` inside `ProcessConflict`, so a competing binding sharing no usage with the accumulated set never enters the cascade; the set grows through `Usages.Combine` at `:454`, `:679` and `:684`, the last of those walking `y.action.m_LinkedActions`. What the cascade emits is not display — `:253-256` and `:269-272` feed `InputManager.SetBindings`, so usages decide which other bindings a player's rebind swaps onto the new key, empties, or reports unsolvable. The claim that this file previously carried ("a grep returns no other consumer") was wrong twice over: the grep is not empty, and an empty one would not have settled the question anyway.

**Ruled (2026-08-03, the settings-and-input pass; conflicts.md).** The reference teaches both halves, as two mechanisms that happen to share a name, and hands the reader `shouldBeEnabled` gated on the mod's own state as the thing that scopes an action at runtime.

What the reference owes, concretely:

- A usage narrows the conflict the player is **shown** — the warning triangle on the options row, and the per-map notification. That is the whole of its effect anywhere this pipeline can look at 1.6.0f1.
- The pass that **disables** an action ignores usages entirely. It pairs any two currently-enabled actions sharing a control path, and a mod action always loses. State this flat, as a fact about the code.
- What does scope the disable is enablement, so the reference's answer to "how do I stop my binding colliding with a vanilla one" is `shouldBeEnabled`, gated on the mod's own state — which is also what 16 of 20 repositories already do through the tool lifecycle, established above.
- Neither half ships alone. The first alone leaves a reader debugging a dead hotkey against a mechanism that never touched it; the second alone tells them the usage strings they wrote do nothing, without giving them the thing that works.
- The prose does not argue with the wiki page and does not characterise what other mods do. The corpus is input here as everywhere. State the split on the code's own authority.
- The `checkUsage: false` claim carries the shipped volatility marker: it is one argument at one call site, and the next version's sweep re-checks whether it is still `false`.

Rots: the `checkUsage: false` argument that makes the disabling pass usage-blind — re-read `src/Game/Game.Input/InputManager.cs:746`.

**Two conflict signals reach the player, and they are computed differently.**
`InputManager.CheckConflicts` walks every binding's usage-aware `hasConflicts` and, per map, pushes a notification named for that map (`InputManager.cs:1389-1435`); for a mod map, `SetModConflictNotification` resolves the map name back through `ModSetting.instances`, titles the notification with the mod's own display name and thumbnail, and makes clicking it open that mod's options page (`:1498-1534`).
`InputBindingField.warning` is the per-row triangle, and it is broader for a mod than for the game: `hasConflicts & (isBuiltIn ? WithBuiltIn : All)` (`src/Game/Game.UI.Menu/InputBindingField.cs:54-64`), so a mod binding is flagged for conflicting with another **mod** as well, even though nothing disables it in that case.
`ConflictType` is `[Flags] { None = 0, WithBuiltIn = 1, WithNotBuiltIn = 2, All = 3 }` (`ProxyBinding.cs:224-231`).

Rots: the six map names that make an action a system action, and the eleven map-name constants — re-read `src/Game/Game.Input/ProxyAction.cs:640` and `src/Game/Game.Input/InputManager.cs:182-202`.
Both are cached against `InputManager.actionVersion` and recomputed when it moves (`:418-422`, version bumped at `InputManager.cs:657`).

### Reading an action, and turning it on

`settings.GetAction(name)` is `InputManager.instance.FindAction(id, name)` (`ModSetting.cs:289-292`), and `GetActions()` returns the whole map or an empty array (`:294-301`).
`ProxyAction` answers `IsPressed()`, `IsInProgress()`, `WasPressedThisFrame()`, `WasReleasedThisFrame()`, `WasPerformedThisFrame()`, `GetMagnitude()`, `ReadValue<T>()` and `ReadValueAsObject()` (`src/Game/Game.Input/ProxyAction.cs:402/413/470/484/489/494/499/505`), plus `event Action<ProxyAction, InputActionPhase> onInteraction` for a callback rather than a poll (`:365`).
`valueType` resolves from the underlying control layout — `float` for Button and Axis, `Vector2` for Vector2 (`:273-301`).

**An action a mod registers is inert until the mod enables it.** `shouldBeEnabled` is the switch, and it lazily creates the action's default `InputActivator` on first set (`:322-347`); it throws `"Built-in actions can not be enabled directly"` for a built-in action, which is the wall a mod hits when it reaches for a vanilla action (`:334-337`).
`UpdateState` ORs every enabled activator's device mask together and then subtracts the mask of every blocked `InputBarrier` (`:524-575`); `InputBarrier` is public, attaches to a map or to individual actions, and is the mechanism by which a context blocks input (`src/Game/Game.Input/InputBarrier.cs:59-114`).
16 of 20 repositories set `shouldBeEnabled`, and the dominant idiom is to gate it on the mod's tool being active — `AreaBucket/Systems/AreaBucketToolSystem.cs:234-236` in `OnStartRunning` against `:307-309` in `OnStopRunning`, and the same pairing in `BetterBulldozer/BetterBulldozer/Tools/SubElementBulldozerTool.cs:222/238` and `Anarchy/Anarchy/Systems/AnarchyComponentsTool/AnarchyComponentsToolSystem.cs:269-270`.
`Anarchy/Anarchy/Systems/Common/AnarchyUISystem.cs:345/523-536/585-598` is the fullest version: a block of `shouldBeEnabled` assignments recomputed whenever the active tool changes, including two mutually exclusive bindings chosen by a bool setting.
`CS2-MoveIt/Code/MoveIt/Systems/InputSystem.cs:14-138` wraps the whole thing in a declarative registry — `RegisterBinding(new(action: Mod.Settings.GetAction(...), context: QInput_Contexts.ToolEnabled, trigger: ...))` — which is the corpus's only abstraction over enablement.

### Using a button the game reserves, and still following the player's rebinds

Two entirely different mechanisms answer this, and they answer different halves of it.

**For a tool's own apply, secondary apply and cancel, nothing is needed.** `ToolBaseSystem.OnCreate` fetches per-tool wrappers over the shared vanilla actions and exposes them as `applyAction`, `secondaryApplyAction` and `cancelAction`; `custom-tools.md:319-347` establishes that path in full, including why the raw `ProxyAction` cannot be taken.

**For any other action a mod declares, mimicking copies the vanilla binding's control path onto the mod's own action and keeps copying it.**
Declaratively: `[SettingsUIBindingMimic(string map, string action)]` on a `ProxyBinding` property (`src/Game/Game.Settings/SettingsUIBindingMimicAttribute.cs:6-16`).
`ModSetting.TryGetSourceBindingForMimic` resolves the named action, **requires `action.isBuiltIn`**, then requires a composite for the property's device and a binding for its component (`ModSetting.cs:119-139`); any of those four failing returns false and the property silently falls back to `CreateBinding` with its declared default (`:112-116`).
When it succeeds, `CreateMimicBinding` copies `path`, `originalPath` and `modifiers` from the vanilla binding (`:254-267`), and `RegisterKeyBindings` additionally attaches a watcher on the **source** binding that re-copies path and modifiers into the mod's binding through `SetBinding` every time the player rebinds the vanilla one (`:219-228`).
That watcher is the whole value of the technique: a plain default key would go stale the moment the player rebinds.
The map name comes from `InputManager`'s constants — `kToolMap = "Tool"` and `kShortcutsMap = "Shortcuts"` among eleven (`InputManager.cs:182-202`).

**Corpus: three repositories use the attribute, and one of them has it commented out.**
`Anarchy/Anarchy/Settings/AnarchyModSettings.cs:212-216` mimics `("Tool", "Secondary Apply")` on a mouse binding, and `:278-281`/`:287-290` mimic `("Shortcuts", "Change Elevation")` on **two** properties — `AxisComponent.Positive` and `AxisComponent.Negative` sharing one action name — which is the only worked example of mimicking an axis rather than a button.
`CS2-MoveIt/Code/MoveIt/Settings/Settings.cs:209` and `:215` mimic `("Tool", "Apply")` and `("Tool", "Cancel")`.
`AreaBucket/Setting.cs:53-56` carries a `("Tool", "Apply")` mimic commented out (the attribute itself at `:54`).
All the live ones pair the mimic with `[SettingsUIHidden]`, because a mimicked binding must not be offered for rebinding — it would be overwritten by the next watcher callback.

**Three imperative variants exist, and one of them is better than the attribute.**
`RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderToolSystem.cs:66-81` is the one-shot form: look the vanilla action up, copy `.path` and `.modifiers` onto the mod's binding, `SetBinding`. It does not follow later rebinds.
`AreaBucket/Utils/BindingUtils.cs:12-41` and `Traffic/Code/ModSettings.cs:192-198` both build the watcher by hand with the **public** `new ProxyBinding.Watcher(binding, onChange)` constructor (`src/Game/Game.Input/ProxyBinding.cs:169-221`, ctor at `:185`; the `CreateWatcher` helper the settings class uses is `internal`, `:727`), then apply once immediately (`BindingUtils.cs:39-40`). That is functionally the attribute's behaviour, reachable at runtime and revocable.
Traffic uses exactly that to make mimicking a **player setting**: `UseVanillaToolActions` is a bool with a `[SettingsUISetter]` that registers or disposes watchers on the vanilla Apply and Secondary Apply mouse bindings, while the mod's own two `ProxyBinding` properties carry `[SettingsUIDisableByCondition]` on the same flag so the rows grey out when the mimic is on (`Traffic/Code/ModSettings.Keybindings.cs:42-54` for the three properties, `:122-158` for the watchers).
That is the only place in the corpus where the player chooses between mimicking and rebinding.

**When the attributes cannot express the input at all, the map is built by hand.**
`CS2-Platter/Platter/PlatterMod.cs:246-331` clones the vanilla `("Tool", "Precise Rotation")` mouse composite, flips its `CompositeInstance.builtIn` to false, replaces its modifiers, and registers the result through the internal `InputManager.AddActions` reached by reflection (`:282-331` is the per-action helper, invoked three times at `:255-274` for three modifier combinations).
The map name it passes is the literal `"Platter.Platter.PlatterMod"`, which is exactly the `ModSetting.id` formula for that mod, so the resulting actions are reachable through `settings.GetAction(...)` and through `InputManager.instance.FindAction("Platter.Platter.PlatterMod", "BlockWidthAction")` alike (`CS2-Platter/Platter/Systems/UI/P_UISystem.cs:168`), and their locale keys sit under the mod's own map (`CS2-Platter/Platter/L10n/EnUsConfig.cs:75`).
The reason is stated in the mod's own comment and holds against the decompile: there is no scroll-wheel member in `BindingMouse` (`src/Game/Game.Input/BindingMouse.cs:3-11`), so a `Vector2` scroll action with modifiers cannot be declared with `[SettingsUIMouseBinding]`.
The same file feeds one of those actions to an `InputHintTooltip` (`CS2-Platter/Platter/Systems/UI/P_TooltipSystem.cs:50`), which is the seam into `custom-tools`' tooltip material.

### Catalog gaps found

**`Traffic` demonstrates custom usage contexts and a player-chosen mimic, and its entry names neither.**
`plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md:137` already says "Rebindable actions declared as settings attributes and consumed by the tool", which does not reach either technique.
Sentence to add: "Scoping input actions with its own usage strings so a binding is only live while the relevant mod tool is selected, and offering the player a choice between the mod's own bindings and watchers that keep them equal to the vanilla apply and cancel bindings."
**Corrected on landing (the settings-and-input pass).** The first clause asserted the mechanism this topic's ruling overturns — a usage does not decide whether a binding is live. What went into the catalog scopes the claim to what the source shows: the actions are declared with the mod's own usage strings beside the built-in ones, and the player choice is stated on its own.
Source: `Traffic/Code/ModSettings.Keybindings.cs:7-31` (thirteen class-level action attributes, eleven of them scoped by three of the four custom usage strings defined at `:177-183`), `:42-54` (the `UseVanillaToolActions` setter and the two bindings it disables), `:122-158` (register and dispose of the watchers), `Traffic/Code/ModSettings.cs:192-198` (the watcher helper).

**`Anarchy` demonstrates declarative binding mimicry, including the axis form, and its entry does not say so.**
`mod-catalog.md:287-293` names seven techniques, none of them input.
Sentence to add: "Mimicking a vanilla binding declaratively so a mod action sits on a button the game reserves and follows the player's rebinds, including the two-property form that mimics an axis by sharing one action name across a positive and a negative component."
Source: `Anarchy/Anarchy/Settings/AnarchyModSettings.cs:212-216`, `:278-281` and `:287-290`, with the enable/disable pairing at `Anarchy/Anarchy/Systems/Common/AnarchyUISystem.cs:523-536`.

**`Extra Detailing Tools` demonstrates a mod-private usage string, and its entry does not say so.**
`mod-catalog.md:107-111` names five techniques, all tool or raycast side.
Sentence to add: "Declaring its own usage string alongside the built-in ones so several of its actions coexist with vanilla bindings on the same keys while its transform tool is active."
**Corrected on landing (the settings-and-input pass).** "Coexist" claims the usage keeps both actions working, which is the mechanism the ruling overturns; what a usage changes is the conflict the player is shown. The catalog says the actions are not _reported_ as conflicting.
Source: `ExtraDetailingTools/MOD/Settings.cs:9-14`, where four of six actions carry `"EDT.InTransformTool"` beside `Usages.kDefaultUsage` and `Usages.kToolUsage`.

**Not a gap.** `Platter`'s entry already carries "Hand-built input actions registered by reflection, because scroll bindings are not exposed" (`mod-catalog.md:123`), which is the technique this file establishes at `CS2-Platter/Platter/PlatterMod.cs:246-331`.
`Move It`'s declarative binding registry (`CS2-MoveIt/Code/MoveIt/Systems/InputSystem.cs:14-138`) was considered and left out: it is a wrapper over `QCommonLib`, whose base class `QInputSystem` is not in the checkout, so what an author can read is the call shape rather than the mechanism.

## Bridge

**Mechanics this technique serves.** Settings is the shallow-and-wide technique — nearly every mechanics change a mod makes ends up with a toggle — so only the links with material in the decompile are asserted here.

- **`simulation-time-and-units`** is the one mechanics topic this surface owns outright. `InterfaceSettings` carries `timeFormat`, `temperatureUnit` and `unitSystem` as three enums (`src/Game/Game.Settings/InterfaceSettings.cs:32-49/184-190`), and `OptionsUISystem` publishes all three to the frontend as a single `UnitSettings` struct (`src/Game/Game.UI.Menu/OptionsUISystem.cs:393-399`, binding at `:589`). Anything that renders a number to a player resolves through those, and a mod panel that formats its own values has to read them from `SharedSettings.instance.userInterface` rather than assume metric.
- **`simulation-time-and-units`** takes the three-enum interface settings struct from here, and the 2026-08-03 re-sweep confirmed the frontend half first-party: the bundle carries the same three fields as one object, defaulted `{ timeFormat: TwentyFourHours, temperatureUnit: Celsius, unitSystem: Metric }` (`DecompiledCitiesSkylines2/src-ui/source.js:26105-26108`), and `unitSystem === Metric` is the branch every number formatter takes (`:29165/29173/29177/29181/29189`). A mod panel formatting its own values reads the shared settings rather than assuming metric, and that is now a claim about the frontend checked against the frontend.
- **Camera input has no mechanics topic of its own, and the material stays here.** `InputSettings` is entirely camera input: `mouseScrollSensitivity`, the keyboard/mouse/gamepad move, rotate and zoom sensitivities, the four invert flags and `elevationDraggingEnabled` (`src/Game/Game.Settings/InputSettings.cs:31-129`), all on the `"Camera"` map, which is one of the six maps whose actions are system actions and therefore beat every mod binding (`src/Game/Game.Input/ProxyAction.cs:640`). That is a fact about which vanilla bindings outrank a mod's, which this topic owns outright; none of the approved 26 references covers the camera as a subject, so there is no bridge to name here.
- **`diagnostics`** is two-sided here. A keybinding conflict is one of the few failures in this area that reaches the player by itself, as a notification titled with the mod's display name that opens the mod's options page (`src/Game/Game.Input/InputManager.cs:1498-1534`); and `[SettingsUIDeveloper]` is the only attribute in the catalog gated on `GameManager.instance.configuration.developerMode` (`src/Game/Game.UI.Menu/AutomaticSettings.cs:749-756`), which is how a settings page carries a debug section that ordinary players never see. Everything else in this area fails silently, which is why the "widget that never appears" finding above is a walk down a code path rather than a list of log messages.

**Sibling techniques.**

- **`localization`** owns every string this topic generates keys for. The seam is exactly eleven methods on `ModSetting` (`src/Game/Game.Modding/ModSetting.cs:303-371`): `GetSettingsLocaleID`, `GetOptionLabelLocaleID`, `GetOptionDescLocaleID`, `GetOptionWarningLocaleID`, `GetOptionTabLocaleID`, `GetOptionGroupLocaleID`, `GetEnumValueLocaleID<T>`, `GetOptionFormatLocaleID`, `GetBindingKeyLocaleID` (three overloads), `GetBindingKeyHintLocaleID` and `GetBindingMapLocaleID`. This file establishes the shapes those keys take and where the reflection engine generates the matching ones; the `IDictionarySource` implementations that answer them, and the four packaging strategies the corpus uses, belong there (`survey-mods-techniques.md:371-383` is that topic's lead).
- **`custom-tools`** and this topic split input cleanly, and `custom-tools.md:406` already states the split from the other side: that reference owns the three actions `ToolBaseSystem` hands every tool for free, this one owns every action a mod declares. Two facts cross the line and are worth restating on this side. `custom-tools.md:360-363` records the corpus at "two of twenty repositories use `SettingsUIBindingMimic`"; at the twenty-repository read this pass counted three files carrying it, the third being `AreaBucket/Setting.cs:53-56` — where it is commented out, so the substantive count of two is right and the greppable count is three. And the corpus's dominant enablement idiom is the tool lifecycle: `shouldBeEnabled = true` in `OnStartRunning`, false in `OnStopRunning` (`AreaBucket/Systems/AreaBucketToolSystem.cs:234-236/307-309`).
- **`mod-lifecycle-and-ordering`** owns the `OnLoad`/`OnDispose` frame this whole topic sits in, and two of its established facts are load-bearing here: the ECS world already exists when `OnLoad` runs (`mod-lifecycle-and-ordering.md:68`), which is why `GetOrCreateSystemManaged<OptionsUISystem>()` succeeds inside `RegisterInOptionsUI`; and `OnDispose` is called even on a mod whose `OnLoad` threw (`:349`), which is why every corpus unregistration is null-guarded.
- **`frontend-and-injection`, in the UI skill**, receives everything this topic produces. The options screen is one binding group, `"options"`, carrying `pages`, `activePage`, `activeSection`, a `WidgetBindings` set and the `InputBindingField.Bindings` trigger factory — `rebindInput`, `unsetInputBinding`, `resetInputBinding` (`src/Game/Game.UI.Menu/OptionsUISystem.cs:567-600`, `src/Game/Game.UI.Menu/InputBindingField.cs:12-51`). The widget classes the engine instantiates all live in `src/Game/Game.UI.Widgets/`, and a mod that wants a control the automatic path cannot build either overrides `GetPageData` or builds its own panel over there instead.
  **Verdict (2026-08-03 re-sweep): both halves confirmed first-party, unchanged.** The bundle names the group as `const "options"` in `game-ui/menu/data-binding/options-bindings.ts` (`DecompiledCitiesSkylines2/src-ui/source.js:26002/26109`), and the receiving module is where `OptionsWidgetType` is declared (`:26110-26115`). `ConflictType` comes over with the same three members this file records from `src/` — `None = 0`, `WithBuiltIn = 1`, `WithNotBuiltIn = 2` (`:26100-26103`).

## Dead ends

- **The wiki's three settings pages were all fetched live on 2026-08-03**; the bot challenge did not fire, so nothing here is cited through `survey-wiki-inventory.md`'s snapshot. `Options UI` (https://cs2.paradoxwikis.com/Options_UI) turned out to be a bare list of attribute names by category with no per-attribute parameters, so everything in the catalog finding above is the decompile's; the page's value is that its four categories match the decompile's grouping badly enough to be worth replacing. `Creating a Settings File` (https://cs2.paradoxwikis.com/Creating_a_Settings_File) and `Naming Folder And Files` (https://cs2.paradoxwikis.com/Naming_Folder_And_Files) are both accurate as far as they go and are verified in the persistence finding.
- **Which vanilla actions are built-in cannot be read from `src/`.** `custom-tools.md:417` already records this: the `builtIn` flag is deserialized from the input asset (`src/Game/Game.Input/CompositeInstance.cs:128/261`), which is not decompiled source. It bites this topic in one specific place — `[SettingsUIBindingMimic]` requires `action.isBuiltIn` (`ModSetting.cs:126`) and silently does nothing otherwise, so whether a given map/action pair can be mimicked at all is not provable here. The three corpus mimics all target `"Tool"` and `"Shortcuts"` actions and are the only evidence that those are built-in.
  **Settled 2026-08-03 (the new-sources resweep), against the running game, and the answer is flat.** Every vanilla action is built-in; the only actions that are not are the ones a mod registers. Two things establish it together. The mechanism: `m_BuiltIn` defaults to `true` on `CompositeInstance` (`src/Game/Game.Input/CompositeInstance.cs:20`) and **exactly one site in the game clears it**, `ModSetting.cs:181`, inside key-binding registration — a grep for `builtIn = false` across `Game/` and `Colossal.*/` returns that line and one unrelated hit on a settings _page_'s own `builtIn` (`src/Game/Game.UI.Menu/OptionsUISystem.cs:618`). The observation: reading `InputManager.instance.FindAction(map, action).isBuiltIn` live at 1.6.0f1 returns `True` for seventeen actions sampled across all eleven vanilla maps — `Tool/Apply`, `Tool/Cancel`, `Tool/Precise Rotation`, `Tool/Toggle Snapping`, `Shortcuts/Bulldozer`, `Shortcuts/Quicksave`, `Shortcuts/Hide UI`, `Shortcuts/Universal Mod Panel`, `Menu/Start Game`, `Camera/Move`, `Debug/Debug UI`, `Navigation/Move Horizontal`, `Photo mode/Take Photo`, `Editor/Clone`, `Engagement/Continue`, `Benchmark/Stop`, `Splash screen/Skip` — and `False` for the one mod map present in that session. So `[SettingsUIBindingMimic]`'s built-in requirement never rejects a vanilla target, and a mimic that silently does nothing failed on one of the resolver's other three conditions.
  The map list read live is `Splash screen, Navigation, Menu, Camera, Tool, Shortcuts, Photo mode, Editor, Debug, Engagement, Benchmark` plus one per loaded mod, which confirms this file's eleven map constants against the running process.
  **The static route was chased first and does not reach, which is why it is recorded rather than dropped.** The asset is `Resources.Load<InputActionAsset>("Input/InputActions")` (`src/Game/Game.Input/InputManager.cs:1301`), so it lives in `Cities2_Data/resources.assets`, not in any `.cok` package. That file is a Unity serialized-object file **without type trees**: it carries type names (`grep -ac ServiceConsumption` → 203, `BuildingPrefab` → 257) and no field names (`m_BuiltIn`, `m_Upkeep`, `"bindings"` → 0 each), and the InputSystem's JSON form is absent too. So reading the flag statically needs a Unity serialized-file parser driven by the field order of the decompiled class, which is a derivation rather than a read. Recorded so a later pass reaches for the running game first rather than repeating this.
- **`@colossalorder/create-csii-ui-mod/template/types/input.d.ts` was read in full and bears on nothing this topic owns.** Its 812 lines are the frontend's own input surface: focus navigation (`FocusController`, `NavigationScopeProps`, the focus-key vocabulary), `UISound`, the input-hint and control-icon components, `InputActionBarrier`/`InputActionConsumer`, and `InputActionsDefinition` — a flat list of some 130 UI-map action names typed as `Action`, `Action1D` or `Action2D`, with no map names and no binding paths. It is `frontend-and-injection`'s and `binding-layer`'s material, not this file's: nothing in it names a mod-declared action, the settings screen's widgets, or the built-in flag. Recorded so the next pass does not re-open it expecting the mimic answer.
- **No removal API for actions or maps exists.** Grepping `src/Game/Game.Input/InputManager.cs` and `ProxyActionMap.cs` for `RemoveAction`, `RemoveMap`, `m_Maps.Remove` and `m_Actions.Remove` returns nothing. `ModSetting.instances` is likewise only ever written (`ModSetting.cs:42`) and read (`InputManager.cs:1509`), never cleared. A mod that unregisters its options page still has its actions and its registry entry in the process.
- **Zero corpus uses of nine attributes**, swept across all 20 repositories: `SettingsUIButtonGroup`, `SettingsUIWarning`, `SettingsUITabWarning`, `SettingsUIPageWarning`, `SettingsUIPath`, `SettingsUIPlatform`, `SettingsUISearchHidden`, `SettingsUIDeveloper`, `SettingsUIForceSave`. Everything stated about them above comes from the decompile alone and has no worked example behind it.
- **No corpus mod overrides `Setting.GetPageData`, and none uses `AutomaticSettings` directly.** Grepping all 20 repositories for `GetPageData`, `ManualProperty` and `AutomaticSettings` returns nothing. The only worked example of the manual page-building route is the game's own `src/Game/Game.Settings/ModdingSettings.cs:222-258`.
- **`Setting.Equals`/`GetHashCode` were checked and are not part of this surface.** `Setting.Equals` compares every public readable property that is not `[IgnoreEquals]`, with a short-circuit on a property literally named `"enabled"` (`Setting.cs:26-60`), and `GetHashCode` calls `GetValue(...).GetHashCode()` on every public property with no null guard (`:62-71`). No consumer of either was found for a `ModSetting`; the pair exists for the game's quality-settings comparison.
- **`InputRebindingUISystem` was not pursued.** The rebinding flow the options screen runs when the player presses a key is entirely inside `src/Game/Game.UI.Menu/InputRebindingUISystem.cs`, driven by the three triggers `InputBindingField.Bindings` publishes, and no mod in the corpus touches it. It matters to a mod only through the `RebindOptions` and `canBeEmpty` values the action attribute supplies.
- **The `.coc` reader's platform suffix was noted and not chased.** `DefaultAssetFactory.IsPlatformRelevant` parses the text after the last dot in a settings file's name as a `Platform` and skips the file when it does not match the running platform (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/DefaultAssetFactory.cs:163-185`). No corpus mod uses a platform-suffixed settings file, and `[SettingsUIPlatform]` is the per-property equivalent.
