# Localization

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

Producing text the game will display: the dictionary source a mod registers, the keys it writes, and the strategies for shipping those strings.

Where that text appears belongs to other references.
`settings-and-input` owns the options page, its widgets and the input actions; this reference owns the strings those widgets look up, and the seam between them is the eleven key-building methods on the settings base class.
`units-and-formatting` owns rendering a quantity in the player's own units — the unit strings, the number, fraction, percentage, date and duration formatters, and the interface preferences they branch on; this reference owns the text a formatted quantity is placed inside.
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
**Pass real instances when you call `ReadEntries` yourself**, rather than `null` for either.
A source you wrote may ignore both, but the game's own do not: the locale-asset source throws on a null error list, and `MemorySource` writes into `indexCounts` for every indexed key it sees.
Source: `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/LocaleAsset.cs`, `src/Colossal.Localization/Colossal.Localization/MemorySource.cs`.

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
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs`.

`MemorySource` ships in the game and wraps a dictionary you already have, which is the same thing without the class — but it declares no `ToString` override of its own, so an import failure in one reports the bare shared type name that the rule above exists to avoid.
Reach for it where the diagnostic does not matter, and for your own class wherever it does.

**The manager exists before any mod does.**
It is constructed during boot with `en-US` as the hard-coded fallback locale, and `LoadAvailableLocales` then enumerates every locale asset in the global asset database, ordering the fallback first and registering each one as a locale **and** as a source.
All of that happens before the ECS world is created, and the world already exists when `OnLoad` runs (`mod-lifecycle-and-ordering` owns that frame), so by the time a mod loads, every shipped locale is registered and `GetSupportedLocales()` can be trusted.
That is what makes the standard loader shape — loop over `GetSupportedLocales()`, look for a translation of that name, add it — correct rather than merely lucky.
Source: `src/Game/Game.SceneFlow/GameManager.cs`, `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs`.

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
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs`, `src/Colossal.Localization/Colossal.Localization/LocalizationDictionary.cs`.

**`AddLocale(...)`** registers a _locale_, not a source: it adds an empty locale entry plus its display name and raises `onSupportedLocalesChanged`.
It is what makes `AddSource` for a locale the game does not ship stop being a no-op, and it is the only way to introduce one.

**A locale asset in the asset database registers itself.**
The manager subscribes to the global asset database's change event for locale assets, and re-adds each one as a source on any change; a bulk change reloads every locale from scratch.
`LocaleAsset` implements `IDictionarySource` itself, so writing one into a database is the third way in and needs no `AddSource` call at all.
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs`, `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/LocaleAsset.cs`.

### `RemoveSource` and `AddSource` are not inverses

The pair list `AddSource` writes is never pruned: `RemoveSource` takes the source out of the _locale's_ list and rebuilds the affected dictionary, and leaves the pair recorded.
Two consequences follow from that one omission, and they point in opposite directions.

**A removed pair cannot be re-added.**
`AddSource` does its work only when the `(localeId, source)` pair is not already recorded, so a second call with the same locale and the same source **instance** is a complete no-op — the pair is still recorded from the first call, and the source stays absent from the locale.
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs`.

**And a removed pair comes back anyway, later.**
`LoadAvailableLocales` replays every recorded pair after reloading the locale assets, and any bulk asset-database change reaches it.
The replay path has no such guard, so every pair ever added is restored — including the ones a mod removed on purpose.
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs`.

The rule for a mod that wants to swap a source out and back in: **construct a new source instance for the second `AddSource`**, or accept that the removal is permanent until the next bulk asset change silently undoes it.
**"New instance" means new to the guard, which compares with `Equals` rather than by reference.** A source type that overrides `Equals` — `LocaleAsset` does, on asset id — is refused however many instances you build, so for those the second add needs a source the comparison genuinely distinguishes.
**And test the removal itself before trusting it.** `RemoveSource` rebuilds one dictionary, chosen by which of the two the locale is: the active one, or — when the locale is only the fallback — the fallback one. Removing a fallback-locale source while the player is on another language therefore rebuilds a dictionary nobody is reading, and the entries it contributed stay in the active one, where they arrived by the fallback fill. Test a withdrawal in a language other than the one you registered under.

(VOLATILE: the re-add guard on the recorded pair list, and that removal never prunes it — the localization manager's add-source and remove-source methods.)

## How entries reach the dictionary

One `try`/`catch` wraps the entire enumeration of a source, and that shapes two failure modes neither of which reaches the UI.

**One bad entry drops every entry after it.**
The dictionary's `Add` throws on a null-or-whitespace key and on a null value.
Because the catch sits around the whole loop rather than around one pair, the source is abandoned at the first bad entry and everything later in the enumeration is lost.
The survivors are exactly the entries the enumeration produced before the bad one, which for a lazily-yielding source is an order you wrote and for a stored dictionary is one you do not control.
The only trace is a single `Error` line naming the source's `ToString()`.
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs`, `src/Colossal.Localization/Colossal.Localization/LocalizationDictionary.cs`.

**Later sources overwrite earlier ones.**
`Add` assigns through the indexer, so there is no duplicate-key error and the last source added wins.
Locale assets load first and mod sources arrive during `OnLoad`, so **a mod can override any vanilla string simply by shipping the same key**, and two mods claiming one key resolve by mod load order and by nothing else.
Guard against doing it by accident by testing `activeDictionary.ContainsID(key)` before adding a generated key into a vanilla namespace.
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationDictionary.cs`, `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs`.

**`ReadEntries` is a pull, re-run on every locale change.**
Switching to any locale other than the fallback builds a brand-new dictionary and calls `ReadEntries` on every source registered for it; reloading the active locale additionally calls `Unload()` on each source first.
So a source may compute its entries at read time and stay current.
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs`.

**The fallback locale is the exception, and it is the one most players are on.**
Switching _to_ it re-points the active dictionary at the retained fallback dictionary instead, calling no source at all — so a compute-at-read-time source never refreshes while the fallback locale is active, and a reader who tests only in that language will not see it.
Reload the active locale when your computed entries have changed, rather than relying on a locale switch.
Register one instance under every supported locale and have its `ReadEntries` walk the mod's live objects, yielding a name and a description per object, and everything the player creates after registration localizes with no further calls.
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs`.

## The key grammar

A localization identifier is parsed by four regexes and nothing else.

| Shape | Regex | Written as |
| --- | --- | --- |
| `Single` | `^(?!\d)([\w$]+)\.(?!\d)([\w$]+)$` | `Group.ID` |
| `Hashed` | `^(?!\d)([\w$]+)\.(?!\d)([\w$]+)\[([-a-zA-Z0-9+/*._&<> ]+)\]+$` | `Group.ID[hash]` |
| `Indexed` | `^(?!\d)([\w$]+)\.(?!\d)([\w$]+):([0-9]+)$` | `Group.ID:0` |
| `HashedIndexed` | `^(?!\d)([\w$]+)\.(?!\d)([\w$]+)\[([-a-zA-Z0-9+/*._&<> ]+)\]:([0-9]+)$` | `Group.ID[hash]:0` |

Read off the regexes: **exactly one dot separates group from id**, neither part may start with a digit, both are `\w`-or-`$`, and the hash body accepts a far wider set — letters, digits, `-+/*._&<>` and the space — which is why a generated key can carry a whole dotted type name or a slashed path inside the brackets.
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationValidation.cs`.

Those four shapes account for **every one of the 22,120 keys the game itself ships**, with nothing left over: 16,627 hashed, 3,715 indexed, 1,656 single, 122 hashed-and-indexed.
So the one-dot grammar is not merely what the compiler enforces, it is what the shipped data obeys.
Source: the shipped `en-US.loc` inside `Cities2_Data/Content/Game/Locale.cok`.

(VOLATILE: the four identifier regexes — the localization validation type.)

**Nothing on the mod path validates a key.**
An in-memory source parses only to maintain the index counts and returns its raw dictionary regardless; a locale asset yields stored entries with no parsing at all; the dictionary accepts any non-empty key; and the frontend's `translate` is a plain map lookup.
The only validating path is the build-time compiler that turns raw CSV into a locale asset, and no mod-facing API reaches it.
Source: `src/Colossal.Localization/Colossal.Localization/MemorySource.cs`, `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/LocaleAsset.cs`, `src/Colossal.Localization/Colossal.Localization/LocalizationDictionary.cs`, `src/Game/Game.UI.Localization/UILocalizationManager.cs`, `src/Colossal.Localization/Colossal.Localization/LocalizationCompiler.cs`.

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

The game leans on the inline spec exactly once across all thirteen locales, and the unit it reaches for there — `DurationSeconds` — is one of the five `units-and-formatting` records as existing only on the frontend.
So the frontend-only tail of the unit list is not a toolchain artefact: the game's own strings depend on it.

### Indexed keys pick a random variant

An entity carries a buffer of chosen indices, one per localization slot, generated from a count buffer; a helper turns a base id plus that index into `"<id>:<index>"` and returns the bare id when the index is `-1`.
The counts reach the frontend as the index-counts binding, answered from the active dictionary.

The game leans on it heavily: the shipped English data declares **260 indexed keys totalling 3,837 variants**, and that total accounts for every indexed entry in the file exactly.
The largest pools are the generated district names at 1,015 variants and city names at 501, then five network-name keys at 210 each.
A mod that wants one name out of a pool writes `Group.ID:0` through `Group.ID:n` and lets the mechanism pick.

## The keys the options screen expects

The settings base class exposes eleven public key builders.
`<id>` is the page id and `<name>` is the settings class's own type name; `settings-and-input` owns how both are composed and owns the widget on the other end of each key.

| Helper | Key produced |
| --- | --- |
| `GetSettingsLocaleID()` | `Options.SECTION[<id>]` |
| `GetOptionLabelLocaleID(opt)` | `Options.OPTION[<id>.<name>.<opt>]` |
| `GetOptionDescLocaleID(opt)` | `Options.OPTION_DESCRIPTION[<id>.<name>.<opt>]` |
| `GetOptionWarningLocaleID(opt)` | `Options.WARNING[<id>.<name>.<opt>]` |
| `GetOptionTabLocaleID(tab)` | `Options.TAB[<id>.<tab>]` |
| `GetOptionGroupLocaleID(group)` | `Options.GROUP[<id>.<group>]` |
| `GetEnumValueLocaleID<T>(value)` | `Options.<id>.<ENUMTYPENAME>[<Member>]` |
| `GetOptionFormatLocaleID(opt)` | `Options.FORMAT[<id>.<name>.<opt>]` |
| `GetBindingKeyLocaleID(action[, component])` | `Options.OPTION[<id>/<action>/<component>]` |
| `GetBindingKeyHintLocaleID(action)` | `Common.ACTION[<id>/<action>]` |
| `GetBindingMapLocaleID()` | `Options.INPUT_MAP[<id>]` |

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
It is the only strategy a **user** can fix without a rebuild.
A mod-private language dropdown is not a reason to pick it: that is a source swap over one source instance per language, and any of the six strategies can supply those.
The same idea moved one directory over — reading per-locale JSON out of the _user's_ asset folders — lets a content author ship translations alongside the assets they wrote.

**6. A locale asset written into an asset database.**
Build a `LocaleData` from the locale id and the entries, add a `LocaleAsset` at a computed asset path, call `SetData(localeData, localizationManager.LocaleIdToSystemLanguage(localeID), localizationManager.GetLocalizedName(localeID))` and `Save()`, then `AddLocale(localeAsset)`.
The asset then registers itself as a source through the manager's asset-database subscription, so no `AddSource` is needed.
A `.loc` is a binary file and the mod now owns an asset's lifetime; what it buys is translations that survive as data rather than as a live object, and a path to `AddLocale` for a locale the game does not ship.
Marshal both calls onto the main thread if the import runs on a worker.

**There is no folder convention in the engine.**
Nothing in the localization or asset-database code knows any directory name: locale loading reads whatever locale assets the database holds, and every other route is a mod calling `AddSource` with a dictionary it built itself.
A `lang/` or `l10n/` directory in a mod repository is a translation-platform source directory or a build-time convention, never a runtime path.
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs`.

All six strategies above are reachable with the game's own types alone, so what a localization dependency buys is a parser somebody else maintains and a single agreed place for the `AddLocale` call that makes an unshipped locale addressable — not a capability a mod lacks.

### The thirteen locales the game ships

`de-DE`, `en-US`, `es-ES`, `fr-FR`, `it-IT`, `ja-JP`, `ko-KR`, `pl-PL`, `pt-BR`, `ru-RU`, `zh-HANS`, `zh-HANT` and `uk-UA`.
Twelve are complete at 22,120-odd entries; `uk-UA` ships separately and is about 12% short of the English key set.
No content pack adds a locale of its own — a pack's strings live in these same files.

Three locales mod translations commonly carry — `nl-NL`, `pt-PT` and `ar-SA` — are **not** among them.
**A source added for one of them is a silent no-op, and a later `AddLocale` does not replay it.**
`AddSource` records the pair before checking whether the locale exists, and returns quietly when it does not; `AddLocale` only creates the locale. So `AddLocale` has to have run first — and since mod order is uncontrolled, a mod that needs a companion to register the locale cannot assume it did.
**Retry on the locale signal, but test the locale before you spend a source.** `AddLocale` raises the public supported-locales-changed event, which is the cue to add the source again; the retry is subject to the same `Equals`-keyed re-add guard as the swap case above, so it needs a source that guard tells apart from the one your first attempt recorded.
**Test `SupportsLocale` on that event, and re-run `AddLocale` when it comes back false.** A bulk asset change raises the same signal, and it raises it having just cleared the locale table and rebuilt it from the `.loc` assets in the database — every database, so a `.loc` your mod wrote survives, and a locale you introduced with the string overload and no asset does not. Nothing records or replays an `AddLocale`, so that locale stays gone for the session unless you add it again: adding a source blind on that firing records another dead pair and logs nothing, and testing without re-adding leaves the language silently missing.
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs` (the re-add guard, the silent return when the locale is absent, the event `AddLocale` raises, and that only a bulk locale reload replays recorded sources).

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
This is the design most exposed to the re-add guard above: every one of those calls is a remove-then-add on one pair, so each add needs a source the guard's `Equals` comparison tells apart from the one it recorded, or the swap works exactly once.
It also costs a few hundred lines of state machine, so build it only where the mod's translations genuinely outrun the game's.

## The active language, and changing it

**The active language is a settings write.**
The persisted `locale` is a hidden string defaulting to the literal `"os"`, which the manager resolves to the system language.
The visible dropdown is a separate, unserialized property that resolves `"os"` to the live active id on read, and its item list pairs each supported locale id with a **literal** display name taken from the locale asset's own header rather than with a key.
Applying the interface settings calls `SetActiveLocale`, and the frontend's own locale-selection trigger does both halves — switch the manager and write the setting back.
Source: `src/Game/Game.Settings/InterfaceSettings.cs`, `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs`, `src/Game/Game.UI.Localization/LocalizationBindings.cs`.

## Handing a key to the UI: `LocalizedString`

`LocalizedString`'s factories are the whole string surface: `Id(id)` and `Id(id, params (string, ILocElement)[])`, `IdHash<T>(id, hash)` and its substitutions overload, `Value(value)`, and `IdWithFallback(id, value)` and `IdWithFallback<T>(id, hash, value)`.
`IdHash` composes `$"{id}[{hash}]"`, which is the hashed shape.

**There is an implicit conversion from `string`, and it produces `Id(...)` rather than `Value(...)`.**
A bare string literal handed anywhere a `LocalizedString` is expected is treated as a **key**, so text meant to be shown as written is looked up, misses, and renders as itself.
That is the single most likely way to put a raw string on screen where a translation belonged, and the fix is to write `LocalizedString.Value(...)` whenever the text is already final.
Source: `src/Game/Game.UI.Localization/LocalizedString.cs`.

Substitution reads as:

```csharp
LocalizedString.Id("MyMod.STATUS", ("LOADED", new LocalizedNumber<int>(n, Unit.kInteger)), ("TOTAL", new LocalizedNumber<int>(total, Unit.kInteger)));
```

`NameTooltipPair`'s implicit conversion pairs an id with `id + "_TOOLTIP"`, and `CachedLocalizedStringBuilder<T>` memoises a key-building lambda per value — reach for it when the same key is rebuilt every frame.

The three numeric elements a substitution carries — `LocalizedNumber<T>`, `LocalizedFraction<T>` and `LocalizedBounds<T>` — and everything that renders one are `units-and-formatting`'s material.

## The vanilla key namespaces

The game's own strings occupy **75 groups** — the segment before the first dot — totalling 2,153 ids and 22,120 entries, counted in English, which is the fallback locale and therefore the set that defines what exists.
A group is a naming convention the panels agree on rather than a registered thing, so a mod can write a key into any of them — and whether that key is ever displayed depends on a panel asking for it, which is what the reuse section below governs.

**Only 21 of the 75 groups are named as string literals anywhere in the game's C#**, and the other 54 are built entirely in the frontend, so grepping the decompile for a namespace and finding nothing proves nothing about whether it exists.
Source: the shipped `en-US.loc` inside `Cities2_Data/Content/Game/Locale.cok` (every group that exists), against `src/` (the 21 that appear there as string literals).

Read [the vanilla key namespaces](vanilla-namespaces.md) for the whole group set with an id count, an entry count and a coverage note per group, which is how you find the group a string you want to reuse or override already lives in, and which 21 those are.

### Reusing one

Most reuse is a convention: write a key in a vanilla group and the panel that reads that group picks it up.
The groups a mod normally writes into are `Assets.NAME[<prefab name>]` and `Assets.DESCRIPTION[<prefab name>]` for anything it registers as a prefab, the `Options.*` keys the settings helpers generate, `Common.ACTION[<map>/<action>]` for input hints, and `SelectedInfoPanel.*`, `Toolbar.*`, `ToolOptions.*`, `Services.NAME`/`DESCRIPTION`, `SubServices.NAME` or `PhotoMode.PROPERTY_TITLE` when it extends the matching panel.

**One reuse is a mechanism rather than a convention, and it is not optional.**
A prefab a mod registers has its display name and description looked up by the prefab UI system, which picks the key pair from the prefab's type and components:

| Prefab shape | Key pair |
| --- | --- |
| `UIAssetMenuPrefab` or `ServicePrefab` | `Services.NAME[<name>]` / `Services.DESCRIPTION[<name>]` |
| `UIAssetCategoryPrefab` | `SubServices.NAME[<name>]` / `Assets.SUB_SERVICE_DESCRIPTION[<name>]` |
| carries a service-upgrade component | `Assets.UPGRADE_NAME[<name>]` / `Assets.UPGRADE_DESCRIPTION[<name>]` |
| anything else | `Assets.NAME[<name>]` / `Assets.DESCRIPTION[<name>]` |
| unresolvable | the obsolete identifier's name, and `Assets.MISSING_PREFAB_DESCRIPTION` |

So naming a prefab is not a free choice: the mod ships the key the system will ask for, and the prefab's name and its key are one decision rather than two.
`prefabs-and-assets` owns the registration on the other side of that seam.

(VOLATILE: the branch that picks a prefab's name and description keys — the prefab UI system's title-and-description lookup.)
Source: `src/Game/Game.UI.InGame/PrefabUISystem.cs`.

**An invented namespace is where mods collide with each other.**
A group that is not a vanilla one works fine, because nothing validates it — and nothing reserves it either, so an obvious name is one any other mod may reach for at the same time.
Put the mod id inside the brackets of every key you invent, so a shared group still cannot produce a shared key.
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationDictionary.cs`.

## Writing a name straight into the live dictionary

The dictionary's `Add` is public and `activeDictionary` is a public property, so a mod can bypass sources entirely.

```csharp
GameManager.instance.localizationManager.activeDictionary.Add("Assets.NAME[" + name + "]", displayName);
```

That is the fast path for a name computed at runtime — a prefab generated on the fly, a name copied off another prefab's localized name.
The cost is that the entry is **not in any source**, so an active-locale reload or a bulk asset change drops it, each of them rebuilding the dictionary from the source list.
A locale change drops it too — unless it was written while the fallback locale was active, in which case it went into the fallback dictionary, which a locale switch never clears.
That exception is a trap rather than a reprieve: it means the technique appears to work when tested in the fallback language and fails for everyone else.
Prefer a source whose `ReadEntries` computes the same entries, which survives all three.
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs`, `src/Colossal.Localization/Colossal.Localization/LocalizationDictionary.cs`.

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
Source: `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs`, `src/Colossal.Localization/Colossal.Localization/LocalizationDictionary.cs`.

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
`debug-menu` owns the developer menu itself; `diagnostics` owns the log channels and what each line proves.

## What this reference hands to others

`settings-and-input` generates the keys this reference supplies strings for, and the seam is the eleven helpers above: it owns the widget, the page and the action; this owns the string, the source and the file it lives in.

`units-and-formatting` owns every quantity a mod renders to a player, and the seam is the substitution: a key written here carries a numeric element, and what that element becomes on screen is decided there.
The argument-placeholder section above owns the inline format spec's syntax, and that reference owns the unit each spec names and the bridge onward to `simulation-time-and-units`.

`prefabs-and-assets` owns the only mechanical key requirement here: the branch that picks a prefab's name and description keys makes the prefab's name and its localization key one decision.

`mod-lifecycle-and-ordering` decides when a source can be added and who wins a key collision — the manager and every shipped locale exist before `OnLoad`, and because later sources overwrite earlier ones, two mods claiming one key are resolved by mod load order alone.

`mod-compatibility` inherits that collision: a mod overriding a vanilla key silently changes what every other mod's UI shows, and the only guard is testing the active dictionary for the key before adding it.

`binding-layer`, in the UI skill, carries every localized element across the wire, and the `l10n` binding group with its locale list, debug mode, dictionary-changed signal, index counts and locale-selection trigger is that reference's material.

`frontend-and-injection`, in the UI skill, owns the one errand this topic generates: the generated typed dictionary of vanilla keys, which is how a mod reaches a vanilla key as a component rather than as a string literal.

`custom-tools` is where the reused tooltip namespaces land: `ToolOptions.*` and `Toolbar.*` are the groups a mod's tool-options rows and toolbar entry are looked up in.

The mechanics references each own a namespace in the linked namespace table, which is the route from "I changed this mechanic" to "here is the string the panel already shows for it" — `Services`/`SubServices`/`Properties` for `city-services-and-coverage`, `EconomyPanel`/`Budget`/`CompanyInfoPanel` for `economy-and-companies`, `Transport`/`TransportInfoPanel`/`BikesInfoPanel` for `transportation-and-vehicles`, the four pollution info panels plus `NaturalResourcesInfoPanel` for `environment-and-pollution`, `PopulationInfoPanel`/`LifePath`/`WealthInfoPanel` for `citizens-and-households`, `LevelInfoPanel`/`ZoningFactors`/`LandValueInfoPanel` for `zoning-buildings-and-land-value`, `RoadsInfoPanel` for `roads-and-traffic`, and `Progression` for `city-state-and-progression`.
