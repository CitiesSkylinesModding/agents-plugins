# Localization

Verified against game version 1.6.0f1.

Producing text the game will display: the dictionary source a mod registers, the keys it writes, the strategies for shipping those strings, and the helpers that render a number, a fraction, a date or a duration in the player's own units.

Where that text appears belongs to other references.
`settings-and-input` owns the options page, its widgets and the input actions; this reference owns the strings those widgets look up, and the seam between them is the eleven key-building methods on the settings base class.
`binding-layer`, in the UI skill, owns the wire that carries a localized element from C# to the frontend; this reference owns what goes into one.

Everything here funnels into one object, `GameManager.instance.localizationManager`, and one interface, `IDictionarySource`.

## The dictionary-source contract

`IDictionarySource` declares exactly two members.

```csharp
IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts);
void Unload();
```

`IDictionaryEntryError` is an **empty marker interface** with no members and no implementation anywhere in the game, so the `errors` list is a channel nothing writes to.
The manager allocates a fresh list, passes it, and logs whatever comes back — for a mod's source that is always nothing.
`indexCounts` is the live per-locale index table described under the key grammar below; a source shipping no indexed keys ignores it.
Both parameters tolerate `null` at a direct call, because a source that returns a stored dictionary never touches either.

The whole implementation is usually this small.

```csharp
public class MyLocaleSource : IDictionarySource
{
    private readonly Dictionary<string, string> m_Entries;

    public MyLocaleSource(Dictionary<string, string> entries) => m_Entries = entries;

    public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts) => m_Entries;

    public void Unload() { }

    public override string ToString() => "MyMod.Locale.en-US";
}
```

**Override `ToString()`.**
It is the only identifier in the single log line that reports a failed import, so a source that does not override it reports itself as a bare type name shared with every other mod that copied the same shape.

`MemorySource` ships in the game and wraps a dictionary you already have, which is the same thing without the class — reach for it when there is nothing to compute and for your own class when there is.

**The manager exists before any mod does.**
It is constructed during boot with `en-US` as the hard-coded fallback locale, and `LoadAvailableLocales` then enumerates every locale asset in the global asset database, ordering the fallback first and registering each one as a locale **and** as a source.
All of that happens before the ECS world is created, and the world already exists when `OnLoad` runs (`mod-lifecycle-and-ordering` owns that frame), so by the time a mod loads, every shipped locale is registered and `GetSupportedLocales()` can be trusted.
That is what makes the standard loader shape — loop over `GetSupportedLocales()`, look for a translation of that name, add it — correct rather than merely lucky.

## Three ways a source gets in

**`AddSource(string localeId, IDictionarySource source)`** is the mod-facing entry point, and it is what almost every mod calls.
It throws on a null argument, records the `(localeId, source)` pair, and then:

- **silently does nothing when `localeId` is not a registered locale** — no error, no log line, no entry.
  A translation for a locale the game does not ship is dropped without a word;
- appends the source to that locale's list and logs `Added localization source '<source>' to <locale>` at **Debug** level;
- if the locale is the active one, reads the source straight into the active dictionary and raises `onActiveDictionaryChanged`;
- if the locale is the **fallback** (`en-US`), reads it into the fallback dictionary and then merges the fallback's missing entries into the active one.

That last branch is the whole fallback story.
The merge uses `TryAdd`, so an entry already present in the active locale wins and only genuinely missing keys are filled from `en-US`, flagged as fallback entries.
**A mod that registers an `en-US` source gets English fallback in every other language for free; a mod that registers none shows raw keys wherever a translation is missing.**

**`AddLocale(...)`** registers a _locale_, not a source: it adds an empty locale entry plus its display name and raises `onSupportedLocalesChanged`.
It is what makes `AddSource` for a locale the game does not ship stop being a no-op, and it is the only way to introduce one.

**A locale asset in the asset database registers itself.**
The manager subscribes to the global asset database's change event for locale assets, and re-adds each one as a source on any change; a bulk change reloads every locale from scratch.
`LocaleAsset` implements `IDictionarySource` itself, so writing one into a database is the third way in and needs no `AddSource` call at all.

### `RemoveSource` and `AddSource` are not inverses

The pair list `AddSource` writes is never pruned: `RemoveSource` takes the source out of the _locale's_ list and rebuilds the affected dictionary, and leaves the pair recorded.
Two consequences follow from that one omission, and they point in opposite directions.

**A removed pair cannot be re-added.**
`AddSource` does its work only when the `(localeId, source)` pair is not already recorded, so a second call with the same locale and the same source **instance** is a complete no-op — the pair is still recorded from the first call, and the source stays absent from the locale.

**And a removed pair comes back anyway, later.**
`LoadAvailableLocales` replays every recorded pair after reloading the locale assets, and any bulk asset-database change reaches it.
The replay path has no such guard, so every pair ever added is restored — including the ones a mod removed on purpose.

The rule for a mod that wants to swap a source out and back in: **construct a new source instance for the second `AddSource`**, or accept that the removal is permanent until the next bulk asset change silently undoes it.

(VOLATILE: the re-add guard on the recorded pair list, and that removal never prunes it — the localization manager's add-source and remove-source methods.)

## How entries reach the dictionary

One `try`/`catch` wraps the entire enumeration of a source, and that shapes two failure modes neither of which reaches the UI.

**One bad entry drops every entry after it.**
The dictionary's `Add` throws on a null-or-whitespace key and on a null value.
Because the catch sits around the whole loop rather than around one pair, the source is abandoned at the first bad entry and everything later in the enumeration is lost.
The survivors are exactly the entries the enumeration produced before the bad one, which for a lazily-yielding source is an order you wrote and for a stored dictionary is one you do not control.
The only trace is a single `Error` line naming the source's `ToString()`.

**Later sources overwrite earlier ones.**
`Add` assigns through the indexer, so there is no duplicate-key error and the last source added wins.
Locale assets load first and mod sources arrive during `OnLoad`, so **a mod can override any vanilla string simply by shipping the same key**, and two mods claiming one key resolve by mod load order and by nothing else.
Guard against doing it by accident by testing `activeDictionary.ContainsID(key)` before adding a generated key into a vanilla namespace.

**`ReadEntries` is a pull, re-run on every locale change.**
Switching locale builds a brand-new dictionary and calls `ReadEntries` on every source registered for it; reloading the active locale additionally calls `Unload()` on each source first.
So a source may compute its entries at read time and stay current.
Register one instance under every supported locale and have its `ReadEntries` walk the mod's live objects, yielding a name and a description per object, and everything the player creates after registration localizes with no further calls.

## The key grammar

A localization identifier is parsed by four regexes and nothing else.

| Shape           | Regex                                                              | Written as         |
| --------------- | ------------------------------------------------------------------ | ------------------ |
| `Single`        | `^(?!\d)([\w$]+)\.(?!\d)([\w$]+)$`                                 | `Group.ID`         |
| `Hashed`        | `^(?!\d)([\w$]+)\.(?!\d)([\w$]+)\[([-a-zA-Z0-9+/*._&<> ]+)\]+$`    | `Group.ID[hash]`   |
| `Indexed`       | `^(?!\d)([\w$]+)\.(?!\d)([\w$]+):([0-9]+)$`                        | `Group.ID:0`       |
| `HashedIndexed` | `^(?!\d)([\w$]+)\.(?!\d)([\w$]+)\[([-a-zA-Z0-9+/*._&<> ]+)\]:\d+$` | `Group.ID[hash]:0` |

Read off the regexes: **exactly one dot separates group from id**, neither part may start with a digit, both are `\w`-or-`$`, and the hash body accepts a far wider set — letters, digits, `-+/*._&<>` and the space — which is why a generated key can carry a whole dotted type name or a slashed path inside the brackets.

Those four shapes account for **every one of the 22,120 keys the game itself ships**, with nothing left over: 16,627 hashed, 3,715 indexed, 1,656 single, 122 hashed-and-indexed.
So the one-dot grammar is not merely what the compiler enforces, it is what the shipped data obeys.

(VOLATILE: the four identifier regexes — the localization validation type.)

**Nothing on the mod path validates a key.**
An in-memory source parses only to maintain the index counts and returns its raw dictionary regardless; a locale asset yields stored entries with no parsing at all; the dictionary accepts any non-empty key; and the frontend's `translate` is a plain map lookup.
The only validating path is the build-time compiler that turns raw CSV into a locale asset, and no mod-facing API reaches it.

So a key with four dots works, and keys of that shape are widespread.
What an invalid identifier actually forfeits is **index support**: a key that does not parse never contributes to the index counts, so `Group.ID:0`-style random variants only work on a well-formed identifier.
Match the vanilla grammar anyway — it costs nothing, it is the shape with 22,120 worked examples behind it, and it keeps the indexed mechanism available.

### Argument placeholders

The two ends of the wire disagree about what a placeholder is, and the difference is usable.

C# extracts argument names with `{(?!\d)([A-Z0-9_]+)}`, so `{UPPER_SNAKE}` is what the C# side can see.
The frontend substitutes with the much broader `/{([^{}]+)}/g` and supports an inline format spec C# cannot even parse: `{NAME:UnitName}` and `{NAME:UnitName signed}`, where the part after the colon is looked up in the `Unit` enum by **member name** and used to format the value as a number.

```
"Costs {AMOUNT:Money} per month"
```

That formats through the game's own money formatter with no C# formatting code at all.
The C# argument-name extractor will not list `AMOUNT`, because the whole `{AMOUNT:Money}` token fails its character class.
A placeholder whose value is missing is left in the output verbatim.

The game leans on the inline spec exactly once across all thirteen locales, and the unit it reaches for there — `DurationSeconds` — is one of the five that exist only on the frontend.
So the frontend-only tail of the unit list is not a toolchain artefact: the game's own strings depend on it.

### Indexed keys pick a random variant

An entity carries a buffer of chosen indices, one per localization slot, generated from a count buffer; a helper turns a base id plus that index into `"<id>:<index>"` and returns the bare id when the index is `-1`.
The counts reach the frontend as the index-counts binding, answered from the active dictionary.

The game uses this heavily and mods do not: the shipped English data declares **260 indexed keys totalling 3,837 variants**, and that total accounts for every indexed entry in the file exactly.
The largest pools are the generated district names at 1,015 variants and city names at 501, then five road-name keys at 210 each.
A mod that wants one name out of a pool writes `Group.ID:0` through `Group.ID:n` and lets the mechanism pick.

## The keys the options screen expects

The settings base class exposes eleven public key builders.
`<id>` is the page id and `<name>` is the settings class's own type name; `settings-and-input` owns how both are composed and owns the widget on the other end of each key.

| Helper                                       | Key produced                                    |
| -------------------------------------------- | ----------------------------------------------- |
| `GetSettingsLocaleID()`                      | `Options.SECTION[<id>]`                         |
| `GetOptionLabelLocaleID(opt)`                | `Options.OPTION[<id>.<name>.<opt>]`             |
| `GetOptionDescLocaleID(opt)`                 | `Options.OPTION_DESCRIPTION[<id>.<name>.<opt>]` |
| `GetOptionWarningLocaleID(opt)`              | `Options.WARNING[<id>.<name>.<opt>]`            |
| `GetOptionTabLocaleID(tab)`                  | `Options.TAB[<id>.<tab>]`                       |
| `GetOptionGroupLocaleID(group)`              | `Options.GROUP[<id>.<group>]`                   |
| `GetEnumValueLocaleID<T>(value)`             | `Options.<id>.<ENUMTYPENAME>[<Member>]`         |
| `GetOptionFormatLocaleID(opt)`               | `Options.FORMAT[<id>.<name>.<opt>]`             |
| `GetBindingKeyLocaleID(action[, component])` | `Options.OPTION[<id>/<action>/<component>]`     |
| `GetBindingKeyHintLocaleID(action)`          | `Common.ACTION[<id>/<action>]`                  |
| `GetBindingMapLocaleID()`                    | `Options.INPUT_MAP[<id>]`                       |

Build the key with the helper and never by hand: the page id is derived, and a hand-written key that drifts from it renders as itself.

Every one of the eleven is a well-formed hashed identifier except `GetEnumValueLocaleID`, which puts the page id **between** the group and the id rather than inside the brackets.
Since the page id is itself dotted, that key has four or more dots outside the brackets and parses as none of the four shapes.
It works for the same reason any over-dotted key works, and nothing else in the eleven has that property.

Two of the eleven are worth a note.

`GetBindingMapLocaleID()` names the mod's action map in the rebinding UI and in the keybinding-conflict notification, so leaving its string unwritten shows the player the raw key in both places.

`GetOptionFormatLocaleID` has **no C# consumer at all**; its consumer is the frontend slider widget, which builds the same key, defaults it to `"{SIGN}{VALUE}"`, substitutes `VALUE` and `SIGN`, and consults it **only when the slider's unit is `custom`**.
That unit comes from the custom-format attribute, which is `settings-and-input`'s material.
A slider carrying that attribute and no `Options.FORMAT` string renders the default rather than failing.

### Three attributes override the generated key, and all three drop the page prefix

The display-name, description and confirmation attributes each take an override id, producing `Options.OPTION[<id verbatim>]`, `Options.OPTION_DESCRIPTION[<id verbatim>]` and `Options.WARNING[<id verbatim>]`.
Given an override _value_ and no id they skip the dictionary entirely and render the literal; given neither they fall through to the generated key.

The important half is that an override id is **not** prefixed with the page id, so it is a bare, globally shared key — a collision with another mod or with the game is possible in a way the generated keys make impossible.
The one thing an override buys is worth having: point a `const string` at it and put that same constant on both the display-name and the description attribute, and renaming the property no longer orphans every translation.
Keep the constant mod-scoped for the same reason vanilla keys are namespaced.

## Packaging: six strategies, and what each costs

Every strategy ends at `AddSource`.
What differs is where the strings live, who can edit them, and what happens when a translator sends a file back.

**1. A hand-written C# source, English only.**
One class holding a dictionary built in its constructor from the eleven key helpers, returned unchanged from `ReadEntries`.
Costs nothing to build, and it is the only strategy where `nameof(...)` keeps the key and the property in lockstep, so a renamed setting breaks the build rather than the label.
It cannot be translated without editing C#, and the anti-pattern it decays into is a second full C# class per language — a translator should never have to open a source file.

**2. Embedded per-locale JSON, one resource per locale.**
`{AssemblyName}.l10n.{localeID}.json` as an `EmbeddedResource`, read with `Colossal.Json.JSON.Load(text).Make<Dictionary<string, string>>()` and handed to a `MemorySource`.
Loop over `GetSupportedLocales()` and look for the matching resource, so a file named for a locale the game does not ship is simply never opened, and wrap each file in its own `try`/`catch` so one malformed translation cannot kill the rest.
The strings ship inside the DLL, so a translation fix needs a rebuild and a republish.
Usually paired with strategy 1 for English.

**3. Embedded per-locale CSV with packed option keys.**
The same embedding, with a two-column CSV and a hand-written quote-aware reader — comma or tab, `""` for an embedded quote, multi-line quoted values.
The idea that earns it is **key packing**: a row writes `Options.OPTION:MyProperty` and the loader expands the prefix through the settings object into the long generated key, so a translator edits a spreadsheet and never sees a page id.
The trap is the expander's default arm — an unrecognised prefix maps the row to the page-title key instead of failing, so one typo in a prefix silently rewrites the mod's page title.
Make that arm throw.

**4. One embedded JSON per locale set, loaded through a shared helper.**
A helper reads one named resource as the base (`en-US`) and every sibling resource whose name extends it as another locale, yielding one source per locale for the caller to add.
Same rebuild-to-fix cost as strategy 2, but the mod's whole translation set is two resources rather than one per locale, and the base covers English so no separate C# class is needed.

**5. Loose JSON files beside the DLL.**
Resolve the mod's own install directory through `GameManager.instance.modManager.TryGetExecutableAsset(mod, out var asset)`, enumerate a subdirectory of it, and take each file's base name as the locale id.
The csproj must copy those files to the output directory, and a mod that loads its own files owns its own parse errors.
It is the only strategy a **user** can fix without a rebuild, and the only one that supports a mod-private language dropdown.
The same idea moved one directory over — reading per-locale JSON out of the _user's_ asset folders — lets a content author ship translations alongside the assets they wrote.

**6. A locale asset written into an asset database.**
Build a `LocaleData` from the locale id and the entries, add a `LocaleAsset` at a computed asset path, call `SetData(localeData, localizationManager.LocaleIdToSystemLanguage(localeID), localizationManager.GetLocalizedName(localeID))` and `Save()`, then `AddLocale(localeAsset)`.
The asset then registers itself as a source through the manager's asset-database subscription, so no `AddSource` is needed.
A `.loc` is a binary file and the mod now owns an asset's lifetime; what it buys is translations that survive as data rather than as a live object, and a path to `AddLocale` for a locale the game does not ship.
Marshal both calls onto the main thread if the import runs on a worker.

**There is no folder convention in the engine.**
Nothing in the localization or asset-database code knows any directory name: locale loading reads whatever locale assets the database holds, and every other route is a mod calling `AddSource` with a dictionary it built itself.
A `lang/` or `l10n/` directory in a mod repository is a translation-platform source directory or a build-time convention, never a runtime path.

**Taking a community localization library as a dependency is the one route this reference cannot verify**, and it teaches the mechanism instead.
All six strategies above are reachable with the game's own types alone, so what such a dependency buys is a parser somebody else maintains and a single agreed place for the `AddLocale` call that makes an unshipped locale addressable — not a capability a mod lacks.

### The thirteen locales the game ships

`de-DE`, `en-US`, `es-ES`, `fr-FR`, `it-IT`, `ja-JP`, `ko-KR`, `pl-PL`, `pt-BR`, `ru-RU`, `zh-HANS`, `zh-HANT` and `uk-UA`.
Twelve are complete at 22,120-odd entries; `uk-UA` ships separately and is about 12% short of the English key set.
No content pack adds a locale of its own — a pack's strings live in these same files.

Three locales mod translations commonly carry — `nl-NL`, `pt-PT` and `ar-SA` — are **not** among them.
A source added for any of the three is a silent no-op until something calls `AddLocale`, which is the mechanism behind mods whose store page says a given language needs a companion mod installed.

(VOLATILE: the shipped locale set and the per-locale entry counts — the game's own locale assets.)

### A mod-private language, chosen independently of the game's

A mod may offer its own language dropdown, and the mechanism is not obvious: it does **not** switch the game's locale.
It adds its translation for the chosen language as a source **under the game's currently active locale id**.

```csharp
manager.RemoveSource(gameLocale, sources[gameLocale]);       // its own translation for the game's language
manager.RemoveSource(gameLocale, sources[chosenLanguage]);   // in case of a repeat
manager.AddSource(gameLocale, sources[chosenLanguage]);      // the language the player picked in the mod
```

Subscribe to `onActiveDictionaryChanged` to repeat the swap whenever the player changes the game's language, and carry a re-entrancy flag so the swap's own `AddSource` does not retrigger it.
This is the design most exposed to the re-add guard above: every one of those calls is a remove-then-add on one pair, so the sources must be fresh instances or the swap works exactly once.
It also costs a few hundred lines of state machine, so build it only where the mod's translations genuinely outrun the game's.

## Formatting numbers, fractions, dates and durations

**C# offers exactly four localized element types**: `LocalizedString`, `LocalizedNumber<T>`, `LocalizedFraction<T>` and `LocalizedBounds<T>`, all implementing an empty `ILocElement` that extends `IJsonWritable`.
There is **no C# percentage, date, duration or time element, and no C# formatting function of any kind**.
Each numeric element writes its raw value, an optional unit **string** and one flag; the frontend does all the work.

`LocalizedString`'s factories are the whole string surface: `Id(id)` and `Id(id, params (string, ILocElement)[])`, `IdHash<T>(id, hash)` and its substitutions overload, `Value(value)`, and `IdWithFallback(id, value)` and `IdWithFallback<T>(id, hash, value)`.
`IdHash` composes `$"{id}[{hash}]"`, which is the hashed shape.

**There is an implicit conversion from `string`, and it produces `Id(...)` rather than `Value(...)`.**
A bare string literal handed anywhere a `LocalizedString` is expected is treated as a **key**, so text meant to be shown as written is looked up, misses, and renders as itself.
That is the single most likely way to put a raw string on screen where a translation belonged, and the fix is to write `LocalizedString.Value(...)` whenever the text is already final.

Substitution reads as:

```csharp
LocalizedString.Id("MyMod.STATUS", ("LOADED", new LocalizedNumber<int>(n, Unit.kInteger)), ("TOTAL", new LocalizedNumber<int>(total, Unit.kInteger)));
```

`NameTooltipPair`'s implicit conversion pairs an id with `id + "_TOOLTIP"`, and `CachedLocalizedStringBuilder<T>` memoises a key-building lambda per value — reach for it when the same key is rebuilt every frame.

### The unit is a string, and the two sides carry different lists

`Game.UI.Unit` is a static class of 33 `const string`s, from `kInteger = "integer"` to `kCustom = "custom"`.
The frontend's own `Unit` enum carries **38**: those 33 plus `PercentagePrecise`, `BodiesPerMonth`, `TemperaturePrecise`, `Height` and `DurationSeconds`.
Those five have no C# constant, so reaching them from C# means writing the literal string.

(VOLATILE: the unit constant list on both sides — the UI unit static class, and the frontend's own unit enum.)

**The frontend's number formatter is a lookup table with a visible fallback.**
An unrecognised unit renders the number followed by the unit name in angle brackets — `1234 <myUnit>` — which is the symptom of a typo'd unit string and the reason a mis-spelled unit never throws.

The table is where the player's preferences bite:

- `Integer` renders plain and thousands-separated; `IntegerRounded` switches to a thousand-suffixed key above 1,000 and a million-suffixed one above 1,000,000.
- `Length` renders metres below 1,000 and kilometres above for a metric player, yards below 1,609 and miles above for a freedom-units player.
- `Area`, `Volume`, `Weight`, `WeightPerCell`, `WeightPerMonth`, `Height`, `NetElevation`, `MoneyPerDistance` and `MoneyPerDistancePerMonth` all branch on the unit system the same way.
- `Temperature` branches on the temperature preference across Celsius, Fahrenheit and Kelvin.
- The two `*Precise` units render as `PercentageSingleFraction` and `Temperature` do — same key, and `TemperaturePrecise` branches on the temperature preference exactly as `Temperature` does — and differ only in precision, at two fraction digits against one for `PercentageSingleFraction` and none for `Temperature`.
- `Power` divides the raw value by 10 and renders kilowatts below 10,000, and divides by 10,000 for megawatts above it — so **the value C# passes is in units of 100 W**.
- `Money` has no unit-system branch at all.

Most branches render through a `Common.VALUE_*` key, and the separators come from the dictionary too — a thousands-separator key and a decimal-separator key, applied per call.
The sign prefix is `-` for a negative value always, and `+` for a positive one or `±` for zero **only when the signed flag is set**; otherwise both are empty.

**Fractions and bounds support far fewer units than numbers, and fail ugly outside them.**
`LocalizedFraction` handles eleven — `Volume`, `VolumePerMonth`, `Weight`, `WeightPerMonth`, `Power`, `Energy`, `BodiesPerMonth`, `XP`, `Integer`, `IntegerPerMonth`, `IntegerRounded` — and renders `${value} / ${total} <${unit}>` for anything else.
`LocalizedBounds` handles three — `Power`, `PercentageSingleFraction`, `Temperature` — renders `${min}–${max} <${unit}>` otherwise, and short-circuits to a plain number when min equals max.
Both default to `Integer` when no unit is given.

**`BodiesPerMonth` is the one unit with a fraction entry and no number entry** — `LocalizedFraction` renders it, `LocalizedNumber` prints the angle-bracket fallback.

### Percentage, date and duration exist only on the frontend

`LocalizedPercentage(value, max)` computes `100 * value / max` and renders it as a percentage-unit number — but **clamps any positive result to a minimum of 1**, so 0.2% displays as 1%, and a value or max at or below zero renders 0.

`LocalizedDate({ year, month })` renders a medium-date-format key with the month resolved through the indexed key `Common.MONTH_SHORT:<month>`.
The month is **zero-based**, and a game year is twelve days, so a day _is_ a month — the game's own producer passes `dayOfYear - 1` as the month.

`LocalizedDuration({ value, daysPerYear, maxMonths })` takes a value **in days** and picks a years key at or above `maxMonths` (defaulting to `daysPerYear`), a months key above one, a month key above 23.5/24 of a day, and otherwise falls through to a time-format key with hours and minutes derived from the fraction.

**There is no exported way to display a time of day, and the reason is an export list rather than a missing feature.**
The game has `LocalizedTime`, `LocalizedDateTime` and `LocalizedTimestamp`, plus time-format, date-format and number-formatting hooks, and the time component already branches on the player's 12/24-hour preference.
The public l10n module exports **eleven names and no more**: `Localized`, `LocalizedBounds`, `LocalizedDate`, `LocalizedDuration`, `LocalizedEntityName`, `LocalizedFraction`, `LocalizedNumber`, `LocalizedPercentage`, `LocalizedString`, `Unit` and `useLocalization`.
So formatting a time by hand from the player's preference is the answer for the public module, and reaching the real component is the same errand as reaching any other unexported one, which is `frontend-and-injection`'s material.

**One enum is exported and three are not.**
`Unit` **is** exported and its members are real string values, so `Unit.Money` works at runtime.
The time-format, temperature-unit and unit-system enums are **not** on that list even though the type declaration declares all three, so from the public module their values are written as literals: 24-hour `0` / 12-hour `1`, Celsius `0` / Fahrenheit `1` / Kelvin `2`, metric `0` / freedom `1`.
All three are registered in the frontend's module registry, so a mod already reaching in there gets the live enums instead — `frontend-and-injection` owns that route.

## The player's unit and format preferences

Three enums live on the interface settings class, all under one group of the interface options page:

- `TimeFormat { TwentyFourHours, TwelveHours }`
- `TemperatureUnit { Celsius, Fahrenheit, Kelvin }`
- `UnitSystem { Metric, Freedom }`

with defaults `TwentyFourHours`, `Celsius` and `Metric`.

**From C#**, read them off `SharedSettings.instance.userInterface`.
**From the frontend**, they arrive as one unit-settings struct on the options binding group, and `useLocalization()` returns `{ translate, unitSettings }` — so a component has them without declaring a binding of its own.

The practical answer is that **a mod formatting through `LocalizedNumber` with the right unit never reads them at all**: the formatter branches on them itself.
Read them directly only for the cases the unit table does not cover — a time of day, most of all.

(VOLATILE: the three preference enums and their member order — the interface settings class.)

**The active language lives beside them, and changing it is a settings write.**
The persisted `locale` is a hidden string defaulting to the literal `"os"`, which the manager resolves to the system language.
The visible dropdown is a separate, unserialized property that resolves `"os"` to the live active id on read, and its item list pairs each supported locale id with a **literal** display name taken from the locale asset's own header rather than with a key.
Applying the interface settings calls `SetActiveLocale`, and the frontend's own locale-selection trigger does both halves — switch the manager and write the setting back.

## The vanilla key namespaces

Each row is a **group**: the segment before the first dot.
**Ids** counts the distinct `Group.ID` pairs in the group, ignoring hash and index.
**Entries** counts the actual rows, so a group whose ids are mostly hashed or indexed carries far more entries than ids — and the gap between the two columns is what tells you whether to look a key up or construct one.

**75 groups, 2,153 ids, 22,120 entries**, counted in English, which is the fallback locale and therefore the set that defines what exists.

| Group                         | Ids | Entries | Covers                                                                                                                                           |
| ----------------------------- | --: | ------: | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Achievements`                |   2 |      82 | achievement `TITLE`/`DESCRIPTION`, both hashed by achievement id                                                                                 |
| `AirPollutionInfoPanel`       |   1 |       1 | the air-pollution info view's average readout                                                                                                    |
| `AnimationCurve`              |   2 |       2 | axis labels on a curve editor                                                                                                                    |
| `Assets`                      |  30 |   12028 | prefab display names and descriptions, citizen and vehicle name formats, address formats, themes, upgrades, and the indexed generated-name pools |
| `BikesInfoPanel`              |   2 |       2 | parked bikes and bike-parking availability                                                                                                       |
| `Budget`                      |   7 |      35 | budget-panel tooltips, including the tax breakdowns                                                                                              |
| `Chirper`                     | 116 |     341 | every Chirper message, most of them indexed variants                                                                                             |
| `CinematicCamera`             |  24 |      24 | the cinematic camera editor                                                                                                                      |
| `CityInfoPanel`               |  14 |      70 | demand factors and their descriptions                                                                                                            |
| `Climate`                     |   1 |       4 | `SEASON`, hashed by season                                                                                                                       |
| `Common`                      | 153 |     438 | shared actions, dialog scaffolding, separators, month names, and **every `VALUE_*`, `FRACTION_*` and `BOUNDS_*` unit string**                    |
| `CompanyInfoPanel`            |   3 |       3 | commercial, industrial and office profitability                                                                                                  |
| `Content`                     |   2 |      15 | content-pack name and prerequisite                                                                                                               |
| `DefaultTool`                 |   1 |      15 | `INFOMODE_TOOLTIP`                                                                                                                               |
| `DisasterInfoPanel`           |   3 |       3 | shelter capacity and evacuation                                                                                                                  |
| `EconomyPanel`                | 114 |     373 | the whole economy panel: budget lines, taxation, loans, production                                                                               |
| `Editor`                      | 263 |     678 | the map and asset editors, end to end                                                                                                            |
| `EditorTutorials`             |   3 |     143 | editor tutorial scaffolding                                                                                                                      |
| `EducationInfoPanel`          |   7 |      20 | education availability and distribution                                                                                                          |
| `ElectricityInfoPanel`        |   8 |       8 | electricity availability, trade, battery charge                                                                                                  |
| `EventJournal`                |   4 |      20 | event journal entries and effects                                                                                                                |
| `FireAndRescueInfoPanel`      |   1 |       1 | average fire hazard                                                                                                                              |
| `GameListScreen`              |  39 |      70 | the save-game list and its city summary fields                                                                                                   |
| `GarbageInfoPanel`            |   4 |       4 | garbage rate, landfill availability, processing                                                                                                  |
| `Glossary`                    |   8 |     521 | the in-game glossary: 48 categories, 229 sections with a title and a body each, 11 tabs                                                          |
| `GroundPollutionInfoPanel`    |   1 |       1 | average ground pollution                                                                                                                         |
| `HealthcareInfoPanel`         |  10 |      10 | health, cemetery and crematorium availability                                                                                                    |
| `InfoPanels`                  |   6 |       6 | labels shared across info panels — capacity, consumption, output, processing, production, stored                                                 |
| `Infoviews`                   |  67 |     430 | info view names and their legend tooltips                                                                                                        |
| `ISO`                         |   1 |     249 | `COUNTRY`, hashed by ISO code                                                                                                                    |
| `LandValueInfoPanel`          |   1 |       1 | average land value                                                                                                                               |
| `LevelInfoPanel`              |   9 |       9 | building level names per zone kind                                                                                                               |
| `LifePath`                    |  33 |     130 | the citizen life-path panel's event descriptions                                                                                                 |
| `Loading`                     |   2 |      39 | loading screen title and hint messages                                                                                                           |
| `Main`                        |  69 |      69 | the main toolbar and its per-button tooltips                                                                                                     |
| `Maps`                        |   4 |      49 | map titles, descriptions, outside connections                                                                                                    |
| `MapTilePurchase`             |  17 |      44 | the tile-purchase panel's resource summary                                                                                                       |
| `Menu`                        |  98 |     185 | main menu, achievements warnings, asset upload, notifications                                                                                    |
| `NaturalResourcesInfoPanel`   |   8 |       8 | fertility, ore, oil, fish availability                                                                                                           |
| `NoisePollutionInfoPanel`     |   1 |       1 | average noise pollution                                                                                                                          |
| `Notifications`               |   2 |     142 | `TITLE`/`DESCRIPTION` for a pushed notification, hashed by its key                                                                               |
| `Options`                     | 149 |    1588 | the whole options screen, including the mod-page keys the settings helpers generate                                                              |
| `OutsideConnectionsInfoPanel` |   2 |       2 | top imports and exports                                                                                                                          |
| `Overlay`                     |  19 |      19 | platform overlay actions and controller-disconnect prompts                                                                                       |
| `Paradox`                     |  82 |     168 | account linking, the mods UI, playsets                                                                                                           |
| `PhotoMode`                   |  19 |     321 | photo-mode property titles and tooltips                                                                                                          |
| `PoliceInfoPanel`             |  10 |      10 | crime probability, arrests, success rate                                                                                                         |
| `Policy`                      |   2 |      44 | `TITLE`/`DESCRIPTION`, hashed by policy prefab name                                                                                              |
| `PopulationInfoPanel`         |  15 |      15 | age distribution, birth and death rates                                                                                                          |
| `PostInfoPanel`               |   4 |       4 | mail collected, delivered, rate                                                                                                                  |
| `Progression`                 |  74 |     320 | milestones, development trees, unlock panels                                                                                                     |
| `Properties`                  |  68 |     138 | the per-prefab stat rows in the selected-info and tooltip panels                                                                                 |
| `Radio`                       |  14 |      19 | radio station UI and emergency messages                                                                                                          |
| `Resources`                   |   1 |      41 | `TITLE`, hashed by resource name                                                                                                                 |
| `RoadsInfoPanel`              |   4 |       4 | parking availability and income                                                                                                                  |
| `SelectedInfoPanel`           | 323 |     926 | the largest group by ids: every row of the selected-info panel                                                                                   |
| `Services`                    |   2 |      32 | `NAME`/`DESCRIPTION` for a service or asset-menu prefab                                                                                          |
| `Statistics`                  |   2 |     214 | statistics panel title and per-statistic label                                                                                                   |
| `StatisticsPanel`             |   4 |     441 | statistic titles and time-scale labels                                                                                                           |
| `SubServices`                 |   1 |      64 | `NAME` for an asset-category prefab                                                                                                              |
| `Toolbar`                     |  44 |      44 | the asset menu, theme and asset-pack panels, brush controls                                                                                      |
| `ToolOptions`                 |  22 |      96 | the tool-options panel's titles and tooltips                                                                                                     |
| `Tools`                       |  33 |      70 | tool tooltips — area size, resource yields, flow and consumption labels                                                                          |
| `TourismInfoPanel`            |   4 |       4 | attractiveness, hotel price, tourism rate                                                                                                        |
| `Transport`                   |  27 |      69 | transport overlay legends and line UI                                                                                                            |
| `TransportInfoPanel`          |  10 |      22 | passengers, cargo, line counts                                                                                                                   |
| `Tutorials`                   |  36 |    1096 | tutorial and advisor scaffolding                                                                                                                 |
| `UpgradesMenu`                |   1 |       1 | `TITLE`                                                                                                                                          |
| `VirtualKeyboard`             |   1 |      15 | `TITLE`, hashed by what is being named                                                                                                           |
| `WaterInfoPanel`              |   8 |       8 | water and sewage treatment and trade                                                                                                             |
| `WaterPollutionInfoPanel`     |   1 |       1 | average water pollution                                                                                                                          |
| `WealthInfoPanel`             |  13 |      17 | average wealth, income, rent, fees, upkeep and resource cost, with a wealth-tier key                                                             |
| `WhatsNew`                    |  10 |      10 | the what's-new panel's per-release copy                                                                                                          |
| `WorkplacesInfoPanel`         |   4 |       4 | workplaces, workers, availability                                                                                                                |
| `ZoningFactors`               |   3 |      19 | zoning factor panel title and positive/negative labels                                                                                           |

(VOLATILE: the whole table — its group set, both count columns and the coverage of each group all move with the game's shipped locale data.)

**Only 21 of the 75 groups are named as string literals anywhere in the game's C#** — `Editor`, `Common`, `Options`, `Properties`, `Paradox`, `Assets`, `Tools`, `Menu`, `PhotoMode`, `DefaultTool`, `SelectedInfoPanel`, `Services`, `Maps`, `Infoviews`, `Policy`, `GameListScreen`, `SubServices`, `StatisticsPanel`, `Radio`, `Notifications` and `Loading`.
The other 54 are built entirely in the frontend, so grepping the decompile for a namespace and finding nothing proves nothing about whether it exists.

### Reusing one

Most reuse is a convention: write a key in a vanilla group and the panel that reads that group picks it up.
The groups a mod normally writes into are `Assets.NAME[<prefab name>]` and `Assets.DESCRIPTION[<prefab name>]` for anything it registers as a prefab, the `Options.*` keys the settings helpers generate, `Common.ACTION[<map>/<action>]` for input hints, and `SelectedInfoPanel.*`, `Toolbar.*`, `ToolOptions.*`, `Services.NAME`/`DESCRIPTION`, `SubServices.NAME` or `PhotoMode.PROPERTY_TITLE` when it extends the matching panel.

**One reuse is a mechanism rather than a convention, and it is not optional.**
A prefab a mod registers has its display name and description looked up by the prefab UI system, which picks the key pair from the prefab's type and components:

| Prefab shape                           | Key pair                                                                |
| -------------------------------------- | ----------------------------------------------------------------------- |
| `UIAssetMenuPrefab` or `ServicePrefab` | `Services.NAME[<name>]` / `Services.DESCRIPTION[<name>]`                |
| `UIAssetCategoryPrefab`                | `SubServices.NAME[<name>]` / `Assets.SUB_SERVICE_DESCRIPTION[<name>]`   |
| carries a service-upgrade component    | `Assets.UPGRADE_NAME[<name>]` / `Assets.UPGRADE_DESCRIPTION[<name>]`    |
| anything else                          | `Assets.NAME[<name>]` / `Assets.DESCRIPTION[<name>]`                    |
| unresolvable                           | the obsolete identifier's name, and `Assets.MISSING_PREFAB_DESCRIPTION` |

So naming a prefab is not a free choice: the mod ships the key the system will ask for, and the prefab's name and its key are one decision rather than two.
`prefabs-and-assets` owns the registration on the other side of that seam.

(VOLATILE: the branch that picks a prefab's name and description keys — the prefab UI system's title-and-description lookup.)

**An invented namespace is where mods collide with each other.**
A group that is not a vanilla one works fine, because nothing validates it — and several mods reaching independently for the same obvious name is exactly what happens.
Put the mod id inside the brackets of every key you invent, so a shared group still cannot produce a shared key.

## Writing a name straight into the live dictionary

The dictionary's `Add` is public and `activeDictionary` is a public property, so a mod can bypass sources entirely.

```csharp
GameManager.instance.localizationManager.activeDictionary.Add("Assets.NAME[" + name + "]", displayName);
```

That is the fast path for a name computed at runtime — a prefab generated on the fly, a name copied off another prefab's localized name.
The cost is that the entry is **not in any source**, so it does not survive a locale change, an active-locale reload or a bulk asset change, each of which rebuilds the dictionary from the source list.
This is the one technique here whose failure mode is "works until the player changes language".
Prefer a source whose `ReadEntries` computes the same entries, which survives all three.

## Exporting a key dump for translators

Two different dumps, for two different jobs.

**Dumping the mod's own English source** is what a translation platform needs.
Instantiate the English source, read it, serialize, write.

```csharp
var entries = new MyLocaleSource(...).ReadEntries(new List<IDictionaryEntryError>(), new Dictionary<string, int>())
    .ToDictionary(kv => kv.Key, kv => kv.Value);

File.WriteAllText(path, Colossal.Json.JSON.Dump(entries));
```

Gate it behind a build symbol so it never runs for a player, and **do not write to a hard-coded developer path** — that is the most common defect in exports of this shape, and it makes the export useless on any other machine.
Two fixes work: derive the destination from the compiling file's own location with `[CallerFilePath]`, so the export lands in the repository whoever built it, or write beside the mod's own executable asset.
Pushing an in-game notification naming the file it wrote is worth the three lines, because a silent export that did not happen looks exactly like one that did.

**Dumping the whole active dictionary**, vanilla keys included, is what you do to find a key to reuse.

```csharp
var all = GameManager.instance.localizationManager.activeDictionary.entries
    .OrderBy(kv => kv.Key)
    .ToDictionary(kv => kv.Key, kv => kv.Value);

File.WriteAllText(Path.Combine(Application.persistentDataPath, "locale-dictionary.json"), Colossal.Json.JSON.Dump(all));
```

`entries` is a lazy projection that drops the fallback flag, so the dump cannot distinguish a real translation from an English fallback.
**Placement decides the contents**: run it after the mod's own `AddSource` calls and the dump carries the mod's keys too, run it before and it is vanilla only.

## Diagnosing a key that renders as itself

A missing key renders as the key on the wire path: the C# translate returns the key, and a localized element carries it through.
A component built from the frontend's own generated dictionary renders its fallback instead, or an empty string when it has none — it shows the id only where the call site asks, which the options screen and the keybinding rows do.
So the causes below surface as a raw key, and as a blank label in a frontend component of your own.

Walk them in this order.

1. **The key is not in any source the active locale carries.** Adding a source under an unregistered locale id is a silent no-op, so a translation for a locale the game does not ship never lands.
2. **The source failed mid-import.** A single `Error` line naming the source's `ToString()` is the only trace, and every entry after the bad one is missing while everything before it is present.
3. **The key the code writes is not the key the engine looks up.** For an options row this is nearly always the derived page path disagreeing with the helper — `settings-and-input` owns the inherited-property case where the two provably differ.
4. **A later source overwrote it.** Load order decides, and nothing reports it.
5. **The entry was written straight into the active dictionary and the player changed language.**

The game ships a diagnostic for exactly this, in the developer-mode debug window under a `Localization` tab:

- a **Language** dropdown calling `SetActiveLocale` directly;
- a **Debug Mode** dropdown over `None` / `Id` / `Fallback`, labelled "Show Translations" / "Show IDs" / "Show Fallback", which switches the whole UI to rendering ids, or to rendering the fallback text, with no restart — that is how you tell a missing key from a key whose value happens to look like a key;
- a **Print input bindings and controls** button that logs a ready-to-paste tab-separated block of `Options.OPTION[...]` and `Options.OPTION_DESCRIPTION[...]` rows for every rebindable binding;
- a **Print asset categories** button that writes a category CSV into the user data path.

(VOLATILE: the localization debug tab's contents — the developer-mode localization debug UI.)

The other diagnostic is the log: the `Localization` logger writes `Added localization source ...` at Debug and the import failure at Error, and the `ToString()` override is what makes either line identify your mod.
`diagnostics` owns the debug window itself and the log channels.

## What this reference hands to others

`settings-and-input` generates the keys this reference supplies strings for, and the seam is the eleven helpers above: it owns the widget, the page and the action; this owns the string, the source and the file it lives in.

`simulation-time-and-units` is the mechanics topic this surface feeds most directly, and the seam is the unit table.
Every quantity a mod displays goes through a unit string that decides whether it is converted for a freedom-units player, and the conversion thresholds belong to the mechanic rather than to the presentation — power divides by 10 for kilowatts, so a C# value is in hundreds of watts.
The calendar is the other half: a game year is twelve days, months are zero-based, and the frontend derives a date from the time binding group rather than from anything a localized element carries.

`prefabs-and-assets` owns the only mechanical key requirement here: the branch that picks a prefab's name and description keys makes the prefab's name and its localization key one decision.

`mod-lifecycle-and-ordering` decides when a source can be added and who wins a key collision — the manager and every shipped locale exist before `OnLoad`, and because later sources overwrite earlier ones, two mods claiming one key are resolved by mod load order alone.

`mod-compatibility` inherits that collision: a mod overriding a vanilla key silently changes what every other mod's UI shows, and the only guard is testing the active dictionary for the key before adding it.

`binding-layer`, in the UI skill, carries every localized element across the wire — the four element types are `IJsonWritable` structs whose type names are what the frontend switches on, and the `l10n` binding group with its locale list, debug mode, dictionary-changed signal, index counts and locale-selection trigger is that reference's material.

`frontend-and-injection`, in the UI skill, owns two errands this topic generates: the generated typed dictionary of vanilla keys, which is how a mod reaches a vanilla key as a component rather than as a string literal, and the time and date formatters the public l10n module does not export.

`custom-tools` is where the reused tooltip namespaces land: `ToolOptions.*` and `Toolbar.*` are the groups a mod's tool-options rows and toolbar entry are looked up in.

The mechanics references each own a namespace in the table above, which is the route from "I changed this mechanic" to "here is the string the panel already shows for it" — `Services`/`SubServices`/`Properties` for `city-services-and-coverage`, `EconomyPanel`/`Budget`/`CompanyInfoPanel` for `economy-and-companies`, `Transport`/`TransportInfoPanel`/`BikesInfoPanel` for `transportation-and-vehicles`, the four pollution info panels plus `NaturalResourcesInfoPanel` for `environment-and-pollution`, `PopulationInfoPanel`/`LifePath`/`WealthInfoPanel` for `citizens-and-households`, `LevelInfoPanel`/`ZoningFactors`/`LandValueInfoPanel` for `zoning-buildings-and-land-value`, `RoadsInfoPanel` for `roads-and-traffic`, and `Progression` for `city-state-and-progression`.
