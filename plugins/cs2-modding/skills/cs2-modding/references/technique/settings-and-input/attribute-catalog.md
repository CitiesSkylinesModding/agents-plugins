# The settings attribute catalog

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

The constructor parameters and field defaults behind the attribute groups in the entry file, and the table the reflection engine dispatches on — what you read once you have chosen an attribute and are writing it.
Which attribute does which job, and every trap in the set, is the grouped listing in the entry file.

(VOLATILE: the attribute set, its member names and its constructor parameters — the settings namespace, one file per attribute.)

## Widget selectors

| Attribute | Parameters |
| --- | --- |
| `SettingsUIButton` | none |
| `SettingsUISlider` | fields `min`, `max = 100f`, `step = 1f`, `unit = "integer"`, `scalarMultiplier = 1f`, `scaleDragVolume`, `updateOnDragEnd` |
| `SettingsUIDropdown` | `(Type itemsGetterType, string itemsGetterMethod)` |
| `SettingsUITextInput` | none |
| `SettingsUIDirectoryPicker` | none |
| `SettingsUIMultilineText` | `(string icon = null)` |
| `SettingsUIConfirmation` | `(string overrideConfirmMessageId = null, string overrideConfirmMessageValue = null)` — upgrades a button to a confirmed button |

`SettingsUICustomFormat` carries the fields `fractionDigits`, `separateThousands = true`, `maxValueWithFraction = 100f` and `signed`, and only a float slider reads all four; the entry file states what that costs on an `int`.

## Layout and ordering

`SettingsUITabOrder` and `SettingsUIGroupOrder` each take either `params string[]` or a `(Type checkType, string checkMethod)` pair; `SettingsUIShowGroupName` takes either nothing — meaning every group — or `params string[]`.
`SettingsUIButtonGroup(string name)` and `SettingsUIPath(string overridePath)`.
`SettingsUISection`'s three overloads are `(tab, simpleGroup, advancedGroup)`, `(tab, group)` and a single-argument form the entry file warns about.

## Text, reaction and warnings

`SettingsUIDisplayName` and `SettingsUIDescription` each have two constructors — `(string overrideId = null, string overrideValue = null)` and `(Type getterType, string getterMethod)`.
`SettingsUISetter(Type setterType, string setterMethod)` and `SettingsUIValueVersion(Type versionGetterType, string versionGetterMethod)`.
`SettingsUIWarning(Type checkType, string checkMethod)` on a property, `SettingsUITabWarning(string tab, Type checkType, string checkMethod)`, and `SettingsUIPageWarning(Type checkType, string checkMethod)`.

## The dispatch table

The reflection pass reads each public instance property's type, its accessors and its attributes, and dispatches to a widget type.

| Property type | Condition | Widget built |
| --- | --- | --- |
| `bool` | `[SettingsUIButton]` + `[SettingsUIConfirmation]` | button with confirmation |
| `bool` | `[SettingsUIButton]` | button, inside a button row |
| `bool` | readable **and** writable | toggle field |
| `bool` | write-only | button, inside a button row |
| `int` | `[SettingsUIDropdown]` | int dropdown |
| `int` | `[SettingsUISlider]` | int slider |
| `float` | `[SettingsUISlider]` | float slider |
| `string` | read+write, `[SettingsUITextInput]` | string input field |
| `string` | read+write, `[SettingsUIDropdown]` | string dropdown |
| `string` | read+write, `[SettingsUIDirectoryPicker]` | directory picker |
| `string` | read-only, `[SettingsUIMultilineText]` | multiline text |
| `string` | read-only, no attribute | localized value field |
| `LocalizedString` | read-only | localized value field |
| enum | `[SettingsUIDropdown]` | int dropdown |
| enum | no attribute | enum field |
| `ProxyBinding` | unconditional | input binding field |
| anything else | `[SettingsUIDropdown]`, and the type is both `IJsonWritable` and `IJsonReadable` with a parameterless constructor | dropdown built by reflection |

Everything else produces no widget, and the three consequences of that which every settings page runs into are stated in the entry file.

(VOLATILE: the property-type-to-widget dispatch above and the widget class names behind it — the automatic settings page builder, and the menu widgets namespace.)
