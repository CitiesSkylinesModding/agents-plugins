# Building the options page

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

The reflection pass that turns a settings class into a page, and the three-level shape of the page it produces.
The entry file keeps the dispatch consequences that decide whether a property becomes a widget at all; what the pass does around them is here.

## The reflection engine: one pass over public instance properties

`Setting.GetPageData(id, addPrefix)` is virtual and forwards to `AutomaticSettings.FillSettingsPage`, so a mod can override it and build a page by hand.
The game does exactly that for its own modding settings: it calls the base and then appends and inserts hand-built items, each wrapping a manual property that carries its own getter, setter and attribute list.
That is the sanctioned escape hatch for a row whose shape no property can express, and the only worked example of it is the game's own.

The automatic path runs **exactly once per registration**, in this order:

1. Read `[SettingsUITabOrder]` and `[SettingsUIGroupOrder]` off the settings **class**, resolving the getter-method overload first and falling back to the literal array. Each name enters an ordered dictionary, and its index is what later sorts tabs and groups.
2. Enumerate the settings type's public instance properties. **Inherited properties are included**, which is why the base class carries `[SettingsUIHidden]` on `id`, on `name` and on its registration flag.
3. Skip the property when its platform filter excludes the running platform, when it is hidden, or when it is developer-only and developer mode is off.
4. Dispatch on the property's type and accessors to a widget type; a property that dispatches to none is skipped.
5. Build one item per `[SettingsUISection]` on the property, falling back to the declaring type's attributes and then to a synthetic `General` section with empty group names. **A property carrying several sections appears once per tab**, as a separate item each time.
6. Resolve each item's path, display name, description and its condition delegates in its constructor.

Source: `src/Game/Game.Settings/Setting.cs` (the virtual entry point), `src/Game/Game.Settings/ModdingSettings.cs` (the hand-built page), `src/Game/Game.UI.Menu/AutomaticSettings.cs` (the pass itself).

Which property type, accessor set and attribute combination produces which widget is the dispatch table in [the settings attribute catalog](attribute-catalog.md).
Everything else produces no widget.

(VOLATILE: the property-type-to-widget dispatch and the widget class names behind it — the automatic settings page builder, and the menu widgets namespace.)

**Buttons group by side effect.**
The builder keeps a dictionary of button rows for the duration of one page build, cleared at both ends.
The first button under a given name creates and returns the row; every later one is appended to that row's children and yields no widget of its own.
The key is `[SettingsUIButtonGroup]`'s name, defaulting to `<declaringType>.<property>_ButtonGroup` — so an ungrouped button is a row of one, and a grouped row takes the position of its **first** member.
Source: `src/Game/Game.UI.Menu/AutomaticSettings.cs`.

**Where the text comes from.**
A widget's path is the `[SettingsUIPath]` override, or `<prefix>.<declaringType>.<property>` where the prefix is the page id.
Display name and description then default to `Options.OPTION[<path>]` and `Options.OPTION_DESCRIPTION[<path>]`, which is exactly what the base class's key helpers build from `id`, `name` and the property name.
Enum members resolve to `Options.<id>.<ENUMTYPENAME>[<Member>]`, and `[SettingsUIHidden]` on an enum **field** drops that member from the dropdown.
Source: `src/Game/Game.UI.Menu/AutomaticSettings.cs`, `src/Game/Game.Modding/ModSetting.cs` (the key helpers).

**One resolver stands behind every `Type` + method-name attribute.**
It searches methods first, then read-only properties, matching on name, on exact return type and on taking no parameters; instance members are searched only when the named type is the settings object's own.
It swallows a miss — the delegate simply stays null — and throws only when reflection itself fails.
The setter attribute is the stricter sibling: it demands a `void` method taking one parameter, accepting the property's own type — or, on an enum property, `int` or its underlying type.
Source: `src/Game/Game.UI.Menu/AutomaticSettings.cs`.

**One `string`-typed pair reads as a bug and is not.**
A read-only `string` property renders its _runtime value_ through a localized value field, which is how a read-only `Version => Mod.Version` row works.
Adding `[SettingsUIMultilineText]` to that same property makes the widget render the _localized string for its path_ and ignore the accessor entirely, which is how a paragraph of prose or a linked image is placed on the page.
Source: `src/Game/Game.UI.Menu/AutomaticSettings.cs`, `src/Game/Game.UI.Widgets/MultilineTextSettingItemData.cs`.

## The page's shape: tabs are sections, groups are runs

The screen's model is three levels deep, and each level is named differently in the code than in the UI.

A **page** is one mod's whole entry in the options list.
A **section** is what the player sees as a tab down the left of the page; there is one per distinct tab name, and its id is `<pageId>.<tabId>`, which is exactly what the base class's tab-key helper builds.
A **group** is not an object at all: it is an integer index carried on each option, and the renderer emits a separator whenever consecutive options disagree about it _and_ the index resolves to a known group name.
An option whose group name was never registered carries the maximum integer and therefore never gets a separator.
Source: `src/Game/Game.UI.Menu/OptionsUISystem.cs`, `src/Game/Game.UI.Menu/AutomaticSettings.cs`, `src/Game/Game.Modding/ModSetting.cs` (the tab-key helper).

**Ordering is by first mention, not alphabetical.**
Tabs and groups are entered into an ordered dictionary, populated first from the class-level order attributes and then from each property as it is scanned.
Tabs sort by that index with unlisted names last, and options sort by it inside a tab.
So the class-level order attributes are the only reliable control, and a group named only by a property lands after every group the attribute listed, in declaration order.
Source: `src/Game/Game.UI.Menu/AutomaticSettings.cs`, `src/Game/Game.UI.Menu/OptionsUISystem.cs`.

**Each option carries two group indexes, and the advanced toggle picks which.**
The first comes from the section's `simpleGroup`, the second from its `advancedGroup` falling back to the first.
In simple mode the renderer additionally drops every option marked advanced; in advanced mode it shows all of them and regroups by the second index.
That is the whole of what the three-argument section overload buys: an option that sits in one group for a casual player and another for someone who has turned advanced on.
Source: `src/Game/Game.UI.Menu/AutomaticSettings.cs`, `src/Game/Game.UI.Menu/OptionsUISystem.cs`.

**A group's heading only renders when the class asks for it.**
The renderer attaches a label to a separator only when the group name is in the set `[SettingsUIShowGroupName]` produced, and emits a bare separator otherwise.
The label's key is `Options.GROUP[<pageId>.<groupId>]`, matching the base class's group-key helper.
Source: `src/Game/Game.UI.Menu/OptionsUISystem.cs`, `src/Game/Game.UI.Menu/AutomaticSettings.cs`, `src/Game/Game.Modding/ModSetting.cs` (the group-key helper).
