# Settings and input

Verified against game version 1.6.0f1.

The player-facing configuration surface: the page a mod adds to the options screen, the file that page persists to, and the input actions declared beside it.
One class carries all three, and one string derived from the mod type ties them together.

`localization` owns every string this surface generates a key for.
This reference establishes the shape of those keys and where the engine generates the matching ones; the sources that answer them belong there.

`custom-tools` owns the three actions the tool base class hands every tool for free — apply, secondary apply and cancel — and states the split from its own side.
This reference owns every action a mod declares itself, which is everything else.

`mod-lifecycle-and-ordering` owns the `OnLoad` and `OnDispose` frame this whole topic sits inside, and two of its facts are load-bearing here: the ECS world already exists when `OnLoad` runs, and `OnDispose` is called even on a mod whose `OnLoad` threw.

## The class, and the one string everything hangs off

`ModSetting` is abstract and derives from `Setting`, which the game's own eleven settings classes share.
The mod-facing half is `ModSetting`; the half that drives the options screen and the save file is `Setting`.

**The constructor takes the mod instance and derives two strings from it:**

```csharp
id   = modType.Assembly.GetName().Name + "." + modType.Namespace + "." + modType.Name
name = GetType().Name
```

`id` comes from the **mod** type, `name` from the **settings** type.
Both are properties on the base class and both carry `[SettingsUIHidden]`, alongside the registration flag, so that the reflection pass below does not turn them into widgets.
The constructor also files the instance into a static registry keyed by `id`, and initialises the key bindings declared on the class.

`id` does four jobs at once, and three of them surprise a reader who only expected the first:

- the options-screen page id;
- the prefix on every localization key the page generates;
- the **input action map name** for every action the mod declares, so that `GetAction(name)` is a lookup of `(id, name)` in the input manager;
- the key under which a keybinding-conflict notification is resolved back to the mod's display name and thumbnail.

(VOLATILE: the composition formula for `id` — the mod settings base class's constructor.)

`ModSetting` seals `builtIn` to false against the base class's true, and that one bool is what sorts every mod page after every vanilla page: the options system orders its pages by `builtIn` descending, then by index.

**One settings class per mod.**
`id` derives from the mod type rather than from the settings type, so a second `ModSetting` subclass in the same assembly produces the same `id`, overwrites the registry entry, and overwrites the page the first one registered.
A settings class that has outgrown one file splits across `partial` files by concern instead — bindings in one, everything else in another.

**A property declared on a shared base class is the one shape that misbehaves.**
The engine falls back to `declaringType` for its class-level attributes, not to the concrete settings type, so a property inherited from a base does not pick up a `[SettingsUISection]` written on the derived class.
Worse, the widget's path carries the base's type name while the helper methods that build localization keys carry `name` — the derived type's name — so the two disagree and the row renders its raw key.
Declare properties on the concrete settings class.

## The members the game calls, and the two it does not

`Setting` declares exactly one abstract member, `SetDefaults()`.

**Nothing in the game ever calls it on a mod's setting.**
The game's own settings classes call their own from their own constructors, and the only external caller iterates the game's own list.
So it is a member a mod must implement _and_ must invoke, and the place to invoke it is the settings class's own constructor:

```csharp
public MyModSettings(IMod mod) : base(mod)
{
    SetDefaults();
}
```

That is the point of the constructor call rather than a call from `OnLoad`.
The live instance and the throwaway defaults instance handed to `LoadSettings` are both constructed, so both are seeded identically — which is what makes the persistence diff below mean anything.
C# property initialisers achieve the same thing for the same reason, and a class using them can leave `SetDefaults` empty; the failure is a class that seeds only the live instance.
`SetNewPlayerDefaults()` is likewise reached only for the game's own settings and never for a mod's.

**`Apply()` and `ApplyAndSave()` are the write path.**
`Apply()` raises the `onSettingsApplied` event; `ApplyAndSave()` calls it and then asks the asset database to save the setting whose type name matches this one.
Note which name that is: the **settings class's own type name**, matched against the type of each loaded fragment's source object — not the name handed to `LoadSettings`.

Every widget the engine builds calls `ApplyAndSave()` from its setter, so a value the player changes on the screen is applied and written with the mod doing nothing.
A mod calling `ApplyAndSave()` itself is how a value changed from the mod's own UI reaches disk.

Two ways to react to a change, and they are not equivalent.
Overriding `Apply()` to push new values into a live system before calling `base.Apply()` keeps the reaction next to the values.
Subscribing to `onSettingsApplied` is the form that survives a subscriber who does not own the settings object, which is the usual case for a system reacting to a setting.
`ApplyAndSave()` is `async void` at the base-class level, so no caller can await it.

## The attributes, grouped by what each one does

The settings namespace holds 38 `SettingsUI*` attributes.
Grouping them by the job they do is what makes the set learnable: several attributes that look adjacent do entirely different things, and the grouping the game's own categories imply cuts across the code's behaviour.

**Widget selectors** — the attribute decides which control the property becomes: `SettingsUIButton`, `SettingsUISlider`, `SettingsUIDropdown`, `SettingsUITextInput`, `SettingsUIDirectoryPicker`, `SettingsUIMultilineText`, and `SettingsUIConfirmation`, which upgrades a button to a confirmed button.

**Formatting of the chosen widget:** `SettingsUICustomFormat`, with fields `fractionDigits`, `separateThousands = true`, `maxValueWithFraction = 100f` and `signed`.
Setting it replaces a slider's `unit` with the literal `"custom"` and hands the numeric formatting to the frontend.
**Only a float slider reads all four.** The int-slider builder copies `separateThousands` and `signed` and drops the other two, and the frontend's own int-slider type declares only those two — so `fractionDigits` and `maxValueWithFraction` on an `int` property are inert.

**Layout and ordering:** `SettingsUISection`, `SettingsUITabOrder`, `SettingsUIGroupOrder`, `SettingsUIShowGroupName`, `SettingsUIButtonGroup`, `SettingsUIPath`.

**`SettingsUISection`'s three overloads are the one place in the catalog that misleads.**
`(tab, simpleGroup, advancedGroup)` is the full form and `(tab, group)` sets both groups, but the **single-argument overload names a group, not a tab**: it forwards to `(null, group, group)` and the constructor turns a null tab into the literal `"General"`.
So a class using the one-argument form lands correctly in a single `General` tab with several groups, and a class that meant to name tabs with it has instead named groups.

**Visibility and enablement,** which split by _when_ they are evaluated.

Evaluated once at page build, with the property simply skipped: `SettingsUIHidden`, `SettingsUIDeveloper` — which additionally consults the game manager's developer-mode flag — and `SettingsUIPlatform(Platform platforms, bool debugConditional = false)`.
Evaluated every frame the page is open, through a delegate the widget holds: `SettingsUIHideByCondition(Type checkType, string checkMethod, bool invert = false)` and `SettingsUIDisableByCondition` with the identical shape.
Evaluated at page build but affecting placement rather than presence: `SettingsUIAdvanced`, which moves the option behind the screen's advanced toggle, and `SettingsUISearchHidden`, which keeps it out of the search index.

**Text:** `SettingsUIDisplayName` and `SettingsUIDescription`.

**Reaction:** `SettingsUISetter`, and `SettingsUIValueVersion`, which nothing sends to the frontend: it replaces the widget's own value-equality check with a version counter, and a dropdown carrying none reads its item list exactly once.

**Warnings:** `SettingsUIWarning` on a property, `SettingsUITabWarning`, and `SettingsUIPageWarning`.

**Persistence:** `SettingsUIForceSave`, treated under persistence below.

**Input:** the three class-level action attributes and the three property-level binding attributes, all treated in full below, plus `SettingsUIBindingMimic`, treated in [mimicking a vanilla binding, and building input by hand](mimicking-and-hand-built-input.md).

**Class-level fallback is not uniform, and that is the single most confusing thing in the set.**
`SettingsUISection`, `SettingsUIAdvanced`, `SettingsUISearchHidden`, `SettingsUIDisableByCondition` and `SettingsUIHideByCondition` all check the property first and then fall back to the declaring type.
`SettingsUIHidden`, `SettingsUIDeveloper` and `SettingsUIPlatform` do **not**: they read the property alone, so `[SettingsUIHidden]` on the settings class has no effect at all even though its `AttributeUsage` permits writing it there.

(VOLATILE: the attribute set and its member names — the settings namespace, one file per attribute.)

Read [the settings attribute catalog](attribute-catalog.md) for every attribute's constructors, parameters and defaults, and for the property-type-to-widget table the engine below dispatches on.

## The reflection engine: one pass over public instance properties

`Setting.GetPageData(id, addPrefix)` is virtual and forwards to `AutomaticSettings.FillSettingsPage`, so a mod can override it and build a page by hand.
The game does exactly that for its own modding settings: it calls the base and then appends and inserts hand-built items, each wrapping a manual property that carries its own getter, setter and attribute list.
That is the sanctioned escape hatch for a row whose shape no property can express, and the only worked example of it is the game's own.

The automatic path runs **exactly once per registration**, in this order:

1. Read `[SettingsUITabOrder]` and `[SettingsUIGroupOrder]` off the settings **class**, resolving the getter-method overload first and falling back to the literal array. Each name enters an ordered dictionary, and its index is what later sorts tabs and groups.
2. Enumerate the settings type's public instance properties. **Inherited properties are included**, which is why the base class hides its own three.
3. Skip the property when its platform filter excludes the running platform, when it is hidden, or when it is developer-only and developer mode is off.
4. Dispatch on the property's type and accessors to a widget type; a property that dispatches to none is skipped.
5. Build one item per `[SettingsUISection]` on the property, falling back to the declaring type's attributes and then to a synthetic `General` section with empty group names. **A property carrying several sections appears once per tab**, as a separate item each time.
6. Resolve each item's path, display name, description and its condition delegates in its constructor.

### The dispatch table

Which property type, accessor set and attribute combination produces which widget is the dispatch table in [the settings attribute catalog](attribute-catalog.md).
Everything else produces no widget.
Three consequences are worth stating flat.

**There is no plain numeric field**: an `int` or a `float` carrying neither a slider nor a dropdown attribute is silently invisible.

**A bool button must be write-only.**
The button builder returns null the moment the property is readable, so `[SettingsUIButton]` on a `{ get; set; }` bool produces nothing at all, while the same property without the attribute produces a working toggle.

**`ProxyBinding` is the only type whose dispatch ignores the accessors**, but the binding machinery behind it filters for properties that are both readable and writable.
A getter-only `ProxyBinding` property therefore renders a keybinding row over a default-valued, unregistered binding.

(VOLATILE: the property-type-to-widget dispatch and the widget class names behind it — the automatic settings page builder, and the menu widgets namespace.)

**Buttons group by side effect.**
The builder keeps a dictionary of button rows for the duration of one page build, cleared at both ends.
The first button under a given name creates and returns the row; every later one is appended to that row's children and yields no widget of its own.
The key is `[SettingsUIButtonGroup]`'s name, defaulting to `<declaringType>.<property>_ButtonGroup` — so an ungrouped button is a row of one, and a grouped row takes the position of its **first** member.

**Where the text comes from.**
A widget's path is the `[SettingsUIPath]` override, or `<prefix>.<declaringType>.<property>` where the prefix is the page id.
Display name and description then default to `Options.OPTION[<path>]` and `Options.OPTION_DESCRIPTION[<path>]`, which is exactly what the base class's key helpers build from `id`, `name` and the property name.
Enum members resolve to `Options.<id>.<ENUMTYPENAME>[<Member>]`, and `[SettingsUIHidden]` on an enum **field** drops that member from the dropdown.

**One resolver stands behind every `Type` + method-name attribute.**
It searches methods first, then read-only properties, matching on name, on exact return type and on taking no parameters; instance members are searched only when the named type is the settings object's own.
It swallows a miss — the delegate simply stays null — and throws only when reflection itself fails.
The setter attribute is the stricter sibling: it demands a `void` method taking one parameter, accepting the property's own type, `int`, or an enum property's underlying type.

**One `string`-typed pair reads as a bug and is not.**
A read-only `string` property renders its _runtime value_ through a localized value field, which is how a read-only `Version => Mod.Version` row works.
Adding `[SettingsUIMultilineText]` to that same property makes the widget render the _localized string for its path_ and ignore the accessor entirely, which is how a paragraph of prose or a linked image is placed on the page.

## The page's shape: tabs are sections, groups are runs

The screen's model is three levels deep, and each level is named differently in the code than in the UI.

A **page** is one mod's whole entry in the options list.
A **section** is what the player sees as a tab down the left of the page; there is one per distinct tab name, and its id is `<pageId>.<tabId>`, which is exactly what the base class's tab-key helper builds.
A **group** is not an object at all: it is an integer index carried on each option, and the renderer emits a separator whenever consecutive options disagree about it _and_ the index resolves to a known group name.
An option whose group name was never registered carries the maximum integer and therefore never gets a separator.

**Ordering is by first mention, not alphabetical.**
Tabs and groups are entered into an ordered dictionary, populated first from the class-level order attributes and then from each property as it is scanned.
Tabs sort by that index with unlisted names last, and options sort by it inside a tab.
So the class-level order attributes are the only reliable control, and a group named only by a property lands after every group the attribute listed, in declaration order.

**Each option carries two group indexes, and the advanced toggle picks which.**
The first comes from the section's `simpleGroup`, the second from its `advancedGroup` falling back to the first.
In simple mode the renderer additionally drops every option marked advanced; in advanced mode it shows all of them and regroups by the second index.
That is the whole of what the three-argument section overload buys: an option that sits in one group for a casual player and another for someone who has turned advanced on.

**A group's heading only renders when the class asks for it.**
The renderer attaches a label to a separator only when the group name is in the set `[SettingsUIShowGroupName]` produced, and emits a bare separator otherwise.
The label's key is `Options.GROUP[<pageId>.<groupId>]`, matching the base class's group-key helper.

The page-shape idiom that follows from all of this is one line long: declare every tab and group as a `const string` on the settings class, list them in `[SettingsUITabOrder]` and `[SettingsUIGroupOrder]` in the order you want, repeat the group list in `[SettingsUIShowGroupName]`, and reference the constants from every `[SettingsUISection]`.

## Debugging a widget that never appears

The engine reports nothing when it drops a property, so the diagnosis is a walk down the same path it took, in order.

1. **The property is not public, or is static.** The enumeration asks for public instance properties and nothing else.
2. **It was filtered before dispatch** — the platform filter excludes the running platform, `[SettingsUIHidden]` is present, or `[SettingsUIDeveloper]` is present and developer mode is off.
3. **Dispatch produced nothing** — the type, accessor and attribute combination is not in the dispatch table. A numeric property with neither slider nor dropdown, a read-write `string` with no attribute, and any collection type all land here.
4. **The builder returned null even though the type matched.** The bool-button builder rejects a readable property; the three dropdown builders reject a missing `[SettingsUIDropdown]`; the int-slider builder rejects a missing `[SettingsUISlider]`; the custom-dropdown builder also rejects a type that is not both `IJsonWritable` and `IJsonReadable` with a parameterless constructor; and a second button in a group returns null by design. The tab builder then skips every item with a null widget without a word.
5. **The widget exists and hides itself.** A hide-by-condition delegate is re-evaluated on every frame the page is open, and a section with no visible option is itself invisible.
6. **The page was never registered**, or was registered before the ECS world existed. Registration resolves the options system through the default world and **returns false silently** when that world is null, and the mod-facing wrapper discards the return value — so there is no signal at all.
7. **The label is missing rather than the widget.** A row whose localization key has no entry renders the raw key; that is `localization`'s failure mode, reached most often through the inherited-property path mismatch above.

An attribute whose `Type` and method-name pair fails to resolve never fails loudly either: the resolver returns false and the widget simply has no setter, no disable condition or no item list.
A dropdown whose getter returns the wrong element type — anything but an array of `DropdownItem<T>` for the matching `T` — gets a null accessor and renders empty.

Nearly everything in this area fails silently, which is why the walk above is a code path rather than a list of log messages; `diagnostics` owns what the game does tell you when something goes wrong, and the keybinding conflict below is the one failure here that reaches the player by itself.

## Registering the page, and what unregistering does not undo

`RegisterInOptionsUI()` and `UnregisterInOptionsUI()` are the two members a mod calls.
The underlying registration on `Setting` that takes a page name and a prefix flag is internal and unreachable, so a mod cannot choose its own page id or turn the key prefix off.

`OptionsUISystem` builds the page **from scratch** on every registration: it stamps the built-in flag, keeps the existing index when a page with that id was already registered and otherwise appends, indexes the sections, runs the update passes, replaces the page and updates the binding.

**So the page is a snapshot.**
Tab order, group order, section membership and the widget set are all frozen at registration, and the only way to change any of them is to call `RegisterInOptionsUI()` again, which rebuilds in place and keeps the page's position.
What _is_ live is evaluated per frame: the hide and disable conditions, the display-name and description getters, and the warning getters.

Unregistering removes the page, re-selects if the removed page was active, and refreshes.
**It does not touch the mod's input actions.**
The input manager has no removal API of any kind — no action removal, no map removal — and the registry keyed by `id` is only ever written and read, never cleared.
An action a mod registers, and the registry entry that resolves its map name back to the mod, are in the process for the rest of the session.

The disposal shape that follows is null-guarded, because `OnDispose` runs even for a mod whose `OnLoad` threw:

```csharp
public void OnDispose()
{
    if (Settings != null)
    {
        Settings.UnregisterInOptionsUI();
        Settings = null;
    }
}
```

**The `OnLoad` block has two orderings and both are correct.**

```csharp
Settings = new MyModSettings(this);
Settings.RegisterInOptionsUI();
Settings.RegisterKeyBindings();

AssetDatabase.global.LoadSettings("MyMod", Settings, new MyModSettings(this));
```

Swapping the load above the key registration works just as well, and the reason is a decode hook on the base class: when the JSON is written into the settings object, that hook fires and — **only if the bindings were already registered** — pushes the loaded bindings into the input manager.
Register first and the load path repairs the actions; register second and the registration reads the already-deserialized property values and builds the actions from them directly.
Key-binding registration is idempotent behind an explicit guard, so calling it twice costs nothing.

## Persistence: one file, its format, and the moment it is written

`[FileLocation(string fileName)]` on the settings class is what gives it a file, and the attribute forces the extension to `.coc` whatever you write.
The path it carries is relative to the user data database's root, which on Windows is `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II`.
That database is the one settings are written back to, because it is the only one that reports itself writable.

Nothing in the attribute or in the data source knows about folders: a nested path and a flat name work identically, and the widely-used `ModsSettings/<Mod>/<Mod>` layout is an agreement among mod authors rather than a mechanism.
Pick one and keep it, because changing it later orphans the player's existing file.

**A `.coc` file is a sequence of `<name>` lines, each followed by a brace-delimited JSON block.**
The reader parses a name, then an object, and hands each entry to the asset factory as a separately named setting asset carrying the raw JSON as its fragment.
**That name is the first argument to `LoadSettings`** — not the type name and not the file name.
Several settings classes sharing one `FileLocation` therefore coexist as separate named blocks inside one file, which is what the mixed-looking format is.

```csharp
AssetDatabase.global.LoadSettings("MyMod", Settings, new MyModSettings(this));
```

**Only non-default values are written, and the reference for "default" is that third argument.**
The loader stores the defaults object on each fragment, and the save path diffs the live object against it.
**Passing null for the defaults writes every property on every save**, because the diff returns its whole result unconditionally when there is no default object to compare against.

`[SettingsUIForceSave]` overrides the diff for one property, re-adding it to the written object at its current value.
It is discovered by a string match on the attribute's type name and only on public readable **properties**, so the class-level usage its `AttributeUsage` permits does nothing.

And when the diff trims the JSON to `{}`, the writer is handed null and **deletes the file**.
An all-default settings file does not exist on disk, so a first run finds nothing to load and so does a run in which the player changed nothing back and forth.

**When it is written:** `ApplyAndSave()` enqueues a save task for the setting matching this type's name, which re-reads the target file and re-saves every setting written to that same file, so untouched blocks survive.
Whole-database saves exist as well, but the per-setting path is the one a mod wants.

**Key bindings persist inside the mod's own settings object, as `ProxyBinding` properties.**
The struct serializes its map name, action name, component, name, device, control path and modifiers, and deliberately excludes the original path, the original modifiers and its link back to the composite that created it.
So a deserialized binding carries no source and reports no rebind options, no modifier options and no usages until it is matched back against a registered action — which is what the decode hook and the key-binding registration above do.
The game's own rebinds live elsewhere and are never mixed into a mod's file.

## Declaring an input action: two attribute layers

An action needs two declarations, and they sit at different levels.

**The property level declares a binding**, one `ProxyBinding` property per device and per component: `[SettingsUIKeyboardBinding]`, `[SettingsUIMouseBinding]` and `[SettingsUIGamepadBinding]`.
Each has six constructors, and their shape is the whole grammar: `(actionName)`, `(AxisComponent, actionName)`, `(Vector2Component, actionName)`, and the same three again with a leading default key plus `alt`, `ctrl` and `shift` bools.

**The component overload is what sets the action type.**
An axis component implies `ActionType.Axis`, a vector component implies `ActionType.Vector2`, and the plain overload implies `ActionType.Button`.
`actionName` defaults to the property name when omitted.
The default key resolves through a switch to a control path such as `"<Keyboard>/f2"`, and the modifiers to the shift, ctrl and alt paths.

The three key enums: `BindingKeyboard` has 96 members from `None = 0` to `OEM5 = 110`; `BindingMouse` has six — `None`, `Left = 1`, `Right = 2`, `Middle = 3`, `Forward = 4`, `Backward = 5`; and `BindingGamepad` has 23 distinct values with three alias sets layered on the face buttons, so `North`/`East`/`South`/`West`, `Y`/`B`/`A`/`X` and `Triangle`/`Circle`/`Cross`/`Square` all resolve to the same four values.
**There is no scroll-wheel member**, which is the boundary of what the binding attributes can express and the reason a scroll action has to be built by hand.

(VOLATILE: the three binding enums and the control-path strings they resolve to — the input namespace's binding enums, and the keyboard binding attribute's path switch.)

**The class level declares the action** those bindings feed, and is the only place its behaviour can be set: `[SettingsUIKeyboardAction]`, `[SettingsUIMouseAction]` and `[SettingsUIGamepadAction]`, all repeatable on the class.
Their fields are `name`, `device`, `type`, `rebindOptions`, `modifierOptions`, `canBeEmpty`, `developerOnly`, `mode`, `interactions`, `processors` and a `usages` list, with these defaults:

| Field             | Default                                                          |
| ----------------- | ---------------------------------------------------------------- |
| `rebindOptions`   | `RebindOptions.All`                                              |
| `modifierOptions` | `ModifierOptions.Allow`                                          |
| `canBeEmpty`      | `true`                                                           |
| `developerOnly`   | `false`                                                          |
| `mode`            | `Mode.DigitalNormalized` for keyboard, `Mode.Analog` for gamepad |

`RebindOptions` is a flags enum — `None = 0`, `Key = 1`, `Modifiers = 2`, `All = 3`.
`ModifierOptions` is `Disallow`, `Allow`, `Ignore`; `Mode` is `DigitalNormalized`, `Digital`, `Analog`; `ActionType` is `Button`, `Axis`, `Vector2`.

**Declaring a binding without its class-level action is legal and quietly takes every default.**
The registration looks the attribute up by action name and device and, finding none, falls back to `RebindOptions.All` with `ModifierOptions.Allow` alone: no usages, no interactions, no processors.
So a mod that never writes the class-level attribute has actions, and has no control over any of their behaviour.

**`ModifierOptions` decides whether modifiers are matched at all**, and it is the field most likely to surprise.
Only `Allow` adds modifier parts to the composite, attaching every modifier the device supports and marking each one with a prohibition processor unless the player's binding names it.
The supported set is shift, ctrl and alt for keyboard and mouse, and the two stick presses for gamepad.
So under `Allow`, an action bound to a plain `E` does **not** fire while Ctrl is held; under `Disallow` or `Ignore`, no modifier parts exist and the action fires regardless.
`Disallow` and `Ignore` differ only in what they tell the rebinding UI, and nothing in the game's own C# reads that difference.

**Registration is one pass.**
It groups the `ProxyBinding` properties by action name, skipping any whose component implies a different action type than the group already carries — which is the mechanism that lets one action carry several bindings.
Two properties naming the same action with the negative and positive axis components become the two halves of one axis, and four vector components become one vector.
Per device it builds one composite marked not built-in, copies the class-level attribute's options into it, and hands the assembled action descriptions to the input manager.
Then it attaches a watcher per property, so a rebind writes the new `ProxyBinding` straight back into the property — which is why the value the player sets survives the next save.

Component-to-binding-name is fixed by the composite tables: press becomes `"binding"`, the axis components become `"negative"` and `"positive"`, and the vector components become their own lowercase names.
Those strings are the last segment of the binding-key locale id, `Options.OPTION[<id>/<actionName>/<componentName>]`, and the input-hint key is `Common.ACTION[<id>/<actionName>]`.

`ResetKeyBindings()` is protected: it regenerates every binding from its attributes, pushes them through the input manager and saves.
The standard "reset key bindings" row is a write-only `bool` property that calls it, write-only so that the dispatch table turns it into a button rather than a toggle.

## Usage contexts, and how a conflict with a vanilla binding resolves

A usage is a **string**, interned into a global index on first use and stored as a bitset on each composite.
Eleven are built in: `"Menu"`, `"DefaultTool"`, `"Overlay"`, `"Tool"`, `"CancelableTool"`, `"Debug"`, `"Editor"`, `"PhotoMode"`, `"Options"`, `"Tutorial"` and `"DiscardableTool"`.
An action attribute naming no usage takes the default set, which is five of them: `DefaultTool`, `Overlay`, `Tool`, `CancelableTool` and `DiscardableTool`.
Custom strings are free-form, and a mod naming its own — one per tool, say — declares a scope nothing vanilla shares.

**Naming any usage replaces the default set rather than adding to it**, which is why an action that wants its own scope _and_ the ordinary tool scope lists the built-in constants beside its own string.

(VOLATILE: the eleven built-in usage constants and the membership of the default set — the input namespace's usages type and its built-in usage flags.)

**Two mechanisms share the word "conflict", and they behave differently.**
Teaching one without the other leaves a reader either debugging a dead hotkey against a mechanism that never touched it, or convinced the usage strings they wrote do nothing.

**A usage narrows the conflict the player is _shown_.**
The per-row warning triangle and the per-map notification both go through a usage-aware comparison, which requires the two bindings to share at least one usage on top of matching device and control path.
A usage has one further effect, in the options screen's rebinding flow: the cascade that resolves a rebind skips any competing binding sharing no usage with the set it has accumulated so far, and that set grows transitively through the linked actions it walks.
So usages decide which _other_ bindings a player's rebind swaps onto the new key, empties, or reports unsolvable — writes, not display.

**The pass that _disables_ an action ignores usages entirely.**
It goes through a comparison called with usage checking switched off, so it pairs any two currently-enabled actions sharing a device and a control path, whatever usages either carries.

(VOLATILE: that the disabling pass compares bindings with usage checking off — the input manager's conflict test, at its single call site from conflict resolution.)

**A mod action always loses that pass.**
Resolution partitions every action in the manager into three tiers.
An action is built-in when its composites say so, which is deserialized from the game's input asset, and key-binding registration stamps a mod's composites as not built-in — so **no action a mod registers is ever built-in**.
An action is a system action when it is built-in _and_ either flagged or alias-free, and the flag is set for six named maps: `"Splash screen"`, `"Engagement"`, `"Camera"`, `"Tool"`, `"Editor"` and `"Benchmark"`.
Resolution then runs system against UI, system against mod, and UI against mod — **and never mod against mod**.
A losing action is marked conflicted, which forces its enabled state false and disables the underlying input action outright.

(VOLATILE: the six map names whose actions are system actions, and the eleven map-name constants — the proxy action type's system-action test, and the input manager's map constants.)

**What scopes the disable is enablement, not usages.**
Resolution only ever considers actions whose enabled state is currently true, and that state is decided by the action's map, its activators and its input barriers.
So the answer to "how do I stop my binding colliding with a vanilla one" is `shouldBeEnabled`, gated on the mod's own state — the binding is only live while the mod is in the mode that wants it, and a pass that never sees it enabled never disables it.
The idiom that follows is the tool lifecycle: set it true in `OnStartRunning` and false in `OnStopRunning`, so the action exists all session and is only contendable while its tool is active.

Write the usage strings as well, because they are what makes the warning the player sees accurate.
A binding left on the default set is shown as conflicting with every vanilla binding on the same key that shares any of those five, including ones it could never contend with; a binding scoped to the mod's own string is shown as conflicting with nothing vanilla at all.

**Two conflict signals reach the player, and they are computed differently.**
The per-map notification walks every binding's usage-aware conflict state and pushes one notification per map; for a mod's map it resolves the map name back through the registry keyed by `id`, titles the notification with the mod's own display name and thumbnail, and makes clicking it open that mod's options page.
The per-row warning triangle is broader for a mod than for the game: it tests `hasConflicts` against `WithBuiltIn` for a built-in binding and against `All` for everything else, so a mod binding is flagged for conflicting with **another mod** as well, even though nothing disables it in that case.
`ConflictType` is a flags enum — `None = 0`, `WithBuiltIn = 1`, `WithNotBuiltIn = 2`, `All = 3`.

## Reading an action, and turning it on

`settings.GetAction(name)` is a lookup of `(id, name)` in the input manager, and `GetActions()` returns the whole map or an empty array.
A `ProxyAction` answers `IsPressed()`, `IsInProgress()`, `WasPressedThisFrame()`, `WasReleasedThisFrame()`, `WasPerformedThisFrame()`, `GetMagnitude()`, `ReadValue<T>()` and `ReadValueAsObject()`, and raises an `onInteraction` event carrying the action and its phase for a callback rather than a poll.
Its value type resolves from the underlying control layout: `float` for a button and an axis, `Vector2` for a vector.

**An action a mod registers is inert until the mod enables it.**
`shouldBeEnabled` is the switch, and it lazily creates the action's default activator on first set.
It throws `"Built-in actions can not be enabled directly"` for a built-in action, which is the wall a mod hits the moment it reaches for a vanilla action.

Behind it, the action's state ORs every enabled activator's device mask together and then subtracts the mask of every blocked input barrier.
`InputBarrier` is public and attaches either to a whole map or to individual actions, and it is the mechanism by which a context blocks input — which is how a mod's actions can be silenced wholesale while a modal panel is up, without touching any individual action's own flag.

Cache the action once rather than resolving it per frame:

```csharp
protected override void OnCreate()
{
    base.OnCreate();

    m_ToggleAction = Mod.Settings.GetAction(nameof(MyModSettings.ToggleBinding));
}

protected override void OnStartRunning()
{
    base.OnStartRunning();

    m_ToggleAction.shouldBeEnabled = true;
}

protected override void OnStopRunning()
{
    base.OnStopRunning();

    m_ToggleAction.shouldBeEnabled = false;
}
```

A mod with several actions whose enablement depends on which tool is active recomputes the whole block from the tool-changed event rather than scattering the assignments — `custom-tools` owns that event and warns that it is a plain delegate field, so subscribe with `+=`.

## Using a button the game reserves, and still following the player's rebinds

Two mechanisms answer this, and they answer different halves of it.

**For a tool's own apply, secondary apply and cancel, nothing is needed.**
The tool base class fetches per-tool wrappers over the shared vanilla actions in its own `OnCreate` and exposes them as `applyAction`, `secondaryApplyAction` and `cancelAction`, which follow the player's rebinds automatically.
`custom-tools` establishes that path in full, including why the raw action underneath cannot be taken.

**For any other action a mod declares, mimicking copies the vanilla binding's control path onto the mod's own action and keeps copying it.**
It has a declarative form, an attribute on a `ProxyBinding` property, and an imperative one the mod owns and can revoke; past both sits the hand-built map, the route a scroll-wheel action forces.
Read [mimicking a vanilla binding, and building input by hand](mimicking-and-hand-built-input.md) for all three, for the rule that a mimicked binding is never offered for rebinding, and for the four ways the declarative form silently falls back to its declared default key.

## What this reference hands to others

`localization` owns every string this surface generates a key for.
The seam is the eleven key-building methods on the settings base class — for the page title, an option's label, description and warning, a tab, a group, an enum value, a value format, a binding key in three overloads, a binding hint, and the binding map's own name.
This reference establishes the shapes those keys take and where the reflection engine generates the matching ones; the dictionary sources that answer them belong there.

`custom-tools` owns the three actions the tool base class provides and the tool-changed event that drives the enablement idiom above; this reference owns every action a mod declares.
The two halves meet exactly once, at mimicking: the tool's own apply and cancel never need it, and anything else the tool wants on a reserved button does.

`[SettingsUIDeveloper]` is the only attribute in the catalog gated on the game's developer-mode flag, which is how a settings page carries a debug section ordinary players never see; `debug-menu` owns what that flag gates.

`units-and-formatting` owns the three interface preferences the game's own settings carry — the time format, the temperature unit and the unit system — and everything a mod does with them when it renders a number to a player.

Camera input has no mechanics topic of its own and stays here: most of the game's input settings are camera — the keyboard, mouse and gamepad move, rotate and zoom sensitivities and the four invert flags, which the game groups under a camera section — beside a few it groups elsewhere, including mouse scroll sensitivity under navigation and the elevation-dragging and legacy-camera toggles under a general one.
These are setting _values_ rather than actions on a map, so the system-action tier that outranks every mod binding does not reach them; what that tier governs is the composites the settings page builds its keybinding widgets over, and those span every vanilla map.

`frontend-and-injection` receives everything this topic produces.
The options screen is one binding group carrying the pages, the active page and section, the widget bindings and the rebinding triggers, and the widget classes the engine instantiates are ordinary frontend widgets.
A mod that wants a control the automatic path cannot build either overrides the page-data method here or builds its own panel over there.

`mod-lifecycle-and-ordering` owns the `OnLoad` and `OnDispose` frame, and settles the two facts this reference leans on: the world already exists when `OnLoad` runs, so the options system resolves; and `OnDispose` runs even after a failed load, so unregistration is null-guarded.
