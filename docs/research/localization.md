# Localization

**This file backs two shipped references, not one.**
`units-and-formatting` was split out of `localization` at the review gate of 2026-08-03 over the first technique batch.
Everything below about **rendering a quantity** — the unit string table and its two declaring sides, the number, fraction, percentage, date and duration formatters, per-unit fraction and bounds coverage, the localized element types, and the three interface preference enums — is now that reference's material.
Everything about **producing text the game will display** — the dictionary-source contract, entry ingestion, the key grammar, packaging, the vanilla namespaces, the translator export, the diagnosis walk — stays `localization`'s.
The pass that produced this file predates the split, so it is left whole rather than cut in two: a research file records what one pass found, and slicing it after the fact would break the citations without adding a fact.
A later pass amending the formatting half amends it here and says which reference it feeds.

**Baseline.** Decompiled game 1.6.0f1; mod corpus read 2026-08-03 at the commits the 20-repository checkout carried; wiki (`Localize your mod`) fetched live 2026-08-03, so no snapshot substitution was needed.
A fourth source is the user's own installed copy at `1.6.0f1 (419.d6c6) [6216.19404]` — the same build the decompile was taken from — and it holds two things the decompile cannot: the strings themselves as compiled `.loc` assets, and the whole frontend as a plain 2.2 MB bundle at `Cities2_Data/Content/Game/UI/index.js`.
Both are first-party and version-known, so they outrank everything else on anything they can answer, and the install is read-only throughout.
The bundle ships minified to one line, so the frontend claims here cite a copy of it reformatted with prettier at its defaults and read at `DecompiledCitiesSkylines2/src-ui/source.js`, **135,021 lines** — the count to check a fresh reformat against before trusting a line number below.

No frontend claim rests on `CS2-Platter/Platter/UI/tools/source.js` any more, the unversioned copy a corpus author vendored into a repository — every one of them was re-derived from the shipped bundle in a pass on 2026-08-03, and it is cited below only where the two disagree.
Its divergences turned out to be staleness rather than error, in the same one-way direction the `.loc` data had already bounded.

## Findings

### The dictionary-source contract is two methods, and the manager is the only consumer

`public interface IDictionarySource` declares exactly two members (`src/Colossal.Core/Colossal/IDictionarySource.cs:5-10`):

```
IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts);
void Unload();
```

`IDictionaryEntryError` is an **empty marker interface** with no members and no implementation anywhere in `src/` (`src/Colossal.Core/Colossal/IDictionaryEntryError.cs:3-5`), so the `errors` list is a channel nothing writes to. The manager allocates a fresh list, passes it, and logs whatever it finds (`src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs:395-419`) — for a mod source that is always nothing.
`indexCounts` is the live per-locale index table (see the key-grammar finding); a source that ships no indexed keys ignores it.

Both parameters are handed `null` by some corpus callers with no ill effect, because _their own_ source ignores them. **Verdict: the causal clause is wrong and an earlier pass of this file generalised it from that one mod.** The game's own stored-dictionary sources do touch both — `LocaleAsset.ReadEntries` throws `ArgumentNullException` on a null `errors` (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/LocaleAsset.cs:175-180`) and dereferences `indexCounts` at `:196-201`, and `MemorySource.ReadEntries` writes into `indexCounts` for every `Indexed` or `HashedIndexed` key (`src/Colossal.Localization/Colossal.Localization/MemorySource.cs:14-29`). The observation held only because a source that returns a stored dictionary _may_ touch them (`Traffic/Code/Mod.cs:63` passes `null, null`; `Traffic/Code/Localization.ModLocale.cs:49` does the same).

`LocalizationManager` is the single consumer. It is created once, before the ECS world exists, with `en-US` as the hard-coded fallback locale (`src/Game/Game.SceneFlow/GameManager.cs:2356-2361`):

```
localizationManager = new LocalizationManager("en-US", SystemLanguage.English, "English");
localizationManager.LoadAvailableLocales();
```

and is reachable from a mod as `GameManager.instance.localizationManager` (`GameManager.cs:298`).
`LoadAvailableLocales` enumerates every `LocaleAsset` in the global asset database, ordering the fallback locale first, registers each as a locale **and** adds it as a source (`LocalizationManager.cs:132-146`).
Because that runs before `CreateWorld` (`GameManager.cs:2363`), and `mod-lifecycle-and-ordering.md:68` establishes that the world already exists when `OnLoad` runs, every shipped locale is registered by the time a mod's `OnLoad` is called. That is why eleven of the twenty repositories, across eighteen files, open a locale loader with `foreach (string localeID in GameManager.instance.localizationManager.GetSupportedLocales())` and trust the answer (`Anarchy/Anarchy/AnarchyMod.cs:176`, `PlopTheGrowables/Code/Localization.cs:50`, `LineTool-CS2/Code/Localization.cs:50`, `ExtraAssetsImporter/MOD/AssetImporter/Importers/LocalizationImporter.cs:47`, `RoadBuilder-CSII/RoadBuilder/Utilities/RoadNameUtil.cs:26`, and the same shape in Better Bulldozer, Move It, Platter, Recolor, Tree Controller and Water Features).

Rots: `LoadAvailableLocales` running before `CreateWorld` — re-check `src/Game/Game.SceneFlow/GameManager.cs:2356-2372`.
**Ruled (2026-08-03, the localization pass) not volatile**, and the reference ships it unmarked: boot ordering is architecture, and a marker here would put a claim that reads the same every sweep onto the next version's checklist. Recorded rather than deleted so a later pass does not re-propose it.

### Three ways in, and only one of them is what a mod normally wants

**`AddSource(string localeId, IDictionarySource source)`** is the mod-facing entry point (`LocalizationManager.cs:313-328`). It throws on a null argument, and otherwise does its work **only if the `(localeId, source)` pair is not already in `m_UserSources`** — it records the pair and calls the private `AddSourceInternal` inside that same guard (`:323-327`).
`AddSourceInternal` (`:330-355`):

- **silently does nothing when `localeId` is not a registered locale** — its whole body sits behind `if (m_LocaleInfos.TryGetValue(localeId, out var value) && !value.m_Sources.Contains(source))` (`:340`). A mod adding a translation for a locale the game does not ship gets no error, no log line, no entry;
- appends the source to that locale's list and logs `Added localization source '<source>' to <locale>` at **Debug** level (`:342-343`);
- if the locale is the active one, reads the source straight into the active dictionary and raises `onActiveDictionaryChanged` (`:344-348`);
- if the locale is the **fallback** (`en-US`), reads it into the fallback dictionary and then merges the fallback's missing entries into the active dictionary (`:349-353`, `AddMissingEntriesFromFallback` at `:421-427`).

That last branch is the whole fallback story: `LocalizationDictionary.MergeFrom` uses `TryAdd`, so an entry already present in the active locale wins and only genuinely missing keys are filled from `en-US`, flagged `fallback: true` (`LocalizationDictionary.cs:115-121`, the `Entry.fallback` field at `:14`).
So a mod that registers an `en-US` source gets automatic English fallback in every other language for free, and a mod that does not register one shows raw keys wherever a translation is missing.

**`AddLocale(LocaleAsset)` / `AddLocale(string, SystemLanguage, string)`** registers a _locale_, not a source (`:272-290`). It adds an empty `LocaleInfo` and the localized display name, and raises `onSupportedLocalesChanged`. It is what makes `AddSource` for a new locale stop being a no-op.
One corpus mod calls it: `ExtraAssetsImporter/MOD/AssetImporter/Importers/LocalizationImporter.cs:58`, and only in its asset-pack branch.

**A `.loc` asset in the database registers itself.** The manager subscribes to `AssetDatabase.global.onAssetDatabaseChanged` for `LocaleAsset` in its constructor (`:56`), and `UpdateSource` removes and re-adds the asset as a source on any change, skipping transient assets (`:162-182`). A bulk change reloads every locale from scratch (`PerformBulkOperation(ReloadAvailableLocales)`, `:166`, `:123-130`, `:147-151`).
`LocaleAsset` implements `IDictionarySource` itself (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/LocaleAsset.cs:9`), so writing one into a database is the third way in — the one `ExtraAssetsImporter` takes.

**Verdict: `RemoveSource` and `AddSource` are not inverses, and the asymmetry cuts both ways.**
`m_UserSources` is written by `AddSource` (`:323-326`) and by nothing else. `RemoveSource` removes the source from the _locale's_ list and rebuilds the affected dictionary, but **never touches `m_UserSources`** (`:357-382`, the removal at `:367`).

Two consequences follow from that one omission, and they point in opposite directions.

**A removed pair cannot be re-added.** `AddSource`'s guard is `if (!m_UserSources.Contains((localeId, source)))` (`:323`), so a second `AddSource` with the same locale and the same source instance is a **complete no-op** — the entry is still in `m_UserSources` from the first call, so `AddSourceInternal` is never reached and the source stays absent from the locale.
Traffic's whole language-swap design rests on the pair being re-addable, and four of its call sites are remove-then-add on one pair: `Traffic/Code/Localization.LocaleManager.cs:41-44`, `:74/79`, `:161-162` and `:197-199`. The clearest instance is `:197-199`, `RemoveSource(gameLocale, LocaleSources[currentLanguage].Item3)` immediately followed by `AddSource(gameLocale, ...)` on the same two values — after the first time that pair has been added, the `AddSource` half can never take effect again.
The instances that use a locale id and its own source (`:44`, `:190`) are the same pair the initial load registered (`Traffic/Code/Localization.cs:38` adds each file under its own locale id), so those are no-ops from the first frame.

**And a removed pair comes back anyway, later.** `LoadAvailableLocales` replays every entry of `m_UserSources` through `AddSourceInternal` after reloading the locale assets (`:141-144`), and `ReloadAvailableLocales` calls it on any bulk asset-database change (`:147-151`, reached from `UpdateSource` at `:166`). `AddSourceInternal` has no `m_UserSources` check, so every pair ever added is restored — including the ones the mod removed on purpose.

The practical rule for a mod that wants to swap a source out and back in: construct a **new** source instance for the second `AddSource`, or accept that the removal is permanent until the next bulk asset change undoes it.

Rots: `AddSource`'s `m_UserSources.Contains` guard and `RemoveSource` never pruning that list — re-read `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs:313-382`.

### Loading a source is one try/catch around the whole enumeration, and the last source wins

`LoadLocaleSource` is the only place entries reach a dictionary (`LocalizationManager.cs:393-419`):

```
try { foreach (var item in source.ReadEntries(list, target.indexCounts)) target.Add(item.Key, item.Value); }
catch (Exception exception) { log.Error(exception, $"Error while importing localization source '{source}'"); }
```

Two consequences a mod author hits and neither is announced in the UI.

**One bad entry drops every entry after it.** `LocalizationDictionary.Add` throws `ArgumentException` on a null-or-whitespace key and `ArgumentNullException` on a null value (`LocalizationDictionary.cs:77-88`). The catch is around the whole loop, so the source is abandoned at the first bad pair and everything later in the enumeration is lost. With a lazily-yielding source — `CSVFileSource` and `LocaleAsset` both use `yield return` (`CSVFileSource.cs:48-71`, `LocaleAsset.cs:175-203`) — the surviving entries are exactly those before the bad one. The only trace is one `Error` line naming the source's `ToString()`.
That is why `IDictionarySource.ToString()` is worth overriding: `Traffic/Code/Localization.ModLocale.cs:82-85` returns `Traffic.Locale.<localeId>`, which is the only corpus example.

**Later sources overwrite earlier ones.** `Add` assigns through the indexer, `m_Dict[entryID] = new Entry(value, fallback)` (`:87`), so there is no duplicate-key error at runtime and the last source added wins. Locale assets are loaded first (`LoadAvailableLocales`, `:132-140`) and mod sources arrive during `OnLoad`, so **a mod can override any vanilla string simply by shipping the same key**, and two mods overriding the same key resolve by `OnLoad` order.
No corpus mod overrides a vanilla key deliberately; `ExtraAssetsImporter` guards against doing so by accident, checking `activeDictionary.ContainsID(...)` before adding a generated `Assets.NAME[...]` (`ExtraAssetsImporter/MOD/OldImporters/DecalsImporter.cs:181-182`, and the same pair in `NetLanesDecalImporter.cs:40-41/179-180` and `SurfacesImporter.cs:129-130`).

**`ReadEntries` is a pull, re-run on every locale change.** `SetActiveLocale` builds a brand-new `LocalizationDictionary` and calls `ReadEntries` on every source registered for that locale (`:206-234`, `LoadLocale` at `:384-392`); `ReloadActiveLocale` additionally calls `Unload()` on each source first (`:236-249`).
So a source can compute its entries at read time and stay current. `RoadBuilder-CSII/RoadBuilder/Utilities/RoadNameUtil.cs:32-46` is the corpus's only exploitation of this: a single `IDictionarySource` instance registered under **every** supported locale (`:26-29`) whose `ReadEntries` iterates the mod's live road configurations and yields a title and a generated description per road. Roads created after registration localize with no further calls.

### The key grammar: four shapes, one dot, and what a mod can get away with breaking

A localization identifier is parsed by four compiled regexes and nothing else (`src/Colossal.Localization/Colossal.Localization/LocalizationValidation.cs:22-25`):

| Shape | Regex | Written as |
| --- | --- | --- |
| Plain | `^(?!\d)([\w$]+)\.(?!\d)([\w$]+)$` | `Group.ID` |
| Hashed | `^(?!\d)([\w$]+)\.(?!\d)([\w$]+)\[([-a-zA-Z0-9+/*._&<> ]+)\]+$` | `Group.ID[hash]` |
| Indexed | `^(?!\d)([\w$]+)\.(?!\d)([\w$]+):([0-9]+)$` | `Group.ID:0` |
| HashedIndexed | `^(?!\d)([\w$]+)\.(?!\d)([\w$]+)\[([-a-zA-Z0-9+/*._&<> ]+)\]:\d+$` | `Group.ID[hash]:0` |

`LocalizationEntry.IdentifierType` names the four (`LocalizationEntry.cs:8-14`) and `GetFullIdentifier` reassembles each (`:78-88`).
Read off the regexes: **exactly one dot separates group from id**, neither part may start with a digit, both are `\w`-or-`$`, and the hash body accepts a much wider set — letters, digits, `-+/*._&<>` and the space — which is why the game's own generated keys can put a whole dotted type name or a slashed path inside the brackets.

Rots: the four identifier regexes — re-read `src/Colossal.Localization/Colossal.Localization/LocalizationValidation.cs:22-25`.

**Verdict: the wiki's recommended key shape is not a valid identifier, and works anyway.**
`Localize your mod` (https://cs2.paradoxwikis.com/Localize_your_mod, fetched 2026-08-03) prescribes `ModNamespace[.SubNamespaces][.ComponentName].KEY_NAME[[Variant]]` and gives `Disasters.UI.DisasterControl.DisasterPanel.DISASTER_DIALOG_MESSAGE` as an example. That has four dots and matches none of the four regexes.
It still works, because nothing on the mod path validates. `MemorySource.ReadEntries` calls `ParseEntry` only to maintain `indexCounts`, discards a null result and returns the raw dictionary regardless (`MemorySource.cs:14-29`); `LocaleAsset.ReadEntries` yields stored entries with no parsing at all (`LocaleAsset.cs:175-203`); `LocalizationDictionary.Add` accepts any non-empty key (`:77-88`); and the frontend's `translate` is a plain map lookup (`src/Game/Game.UI.Localization/UILocalizationManager.cs:19-29`).
The only validating path is `LocalizationCompiler`, which turns raw CSV into `.loc` files, logs `Skipping entry with invalid ID format` and **drops** the entry (`LocalizationValidation.cs:29-43`, `LocalizationCompiler.cs:143-144`). No mod-facing API reaches it.
So the decompile and the wiki are both right about different things: the wiki describes a naming convention that runs, and the grammar the parser enforces is narrower. What an invalid key actually forfeits is index support — `ParseEntry` returning null means the key never contributes to `indexCounts`, so `Group.ID:0`-style random variants only work on a well-formed identifier.

The corpus proves the latitude in the wild. `Traffic/Code/Localization.UIKeys.cs:7-45` declares 32 UI keys of the form `Traffic.UI.Tools[LaneConnector].Toolbox.Action[RemoveAllConnections].Tooltip.Title` — multiple dots, nested bracket groups, none parseable — and eight of them (`:21-24/26-29`) have lost the separating dot entirely to a typo, producing `TrafficUI.Tools[...]`. They render correctly because the same constant is used to write the entry and to look it up.
`Anarchy/Anarchy/Settings/LocaleEN.cs:198-216` is the disciplined opposite: four private helpers producing `Anarchy.TOOLTIP_DESCRIPTION[key]`, `Anarchy.TOOLTIP_TITLE[key]`, `Anarchy.SECTION_TITLE[key]` and `Anarchy.UI_TEXT[key]` from `AnarchyMod.Id = "Anarchy"` (`Anarchy/Anarchy/AnarchyMod.cs:50`) — all well-formed Hashed identifiers.

**Argument placeholders are `{UPPER_SNAKE}`, and the two ends disagree about what counts.**
The C# side extracts argument names with `{(?!\d)([A-Z0-9_]+)}` (`LocalizationValidation.cs:26`, `GetArgNames` at `:71-84`).
The frontend substitutes with the much broader `/{([^{}]+)}/g` and supports an inline format spec the C# regex cannot even see: `{NAME:UnitName}` and `{NAME:UnitName signed}`, where the part after the colon is looked up in the `Unit` enum by **member name** and used to format the value as a number (`DecompiledCitiesSkylines2/src-ui/source.js:29380-29395`, registered as `game-ui/common/utils/substitute.ts` at `:29396`). A placeholder whose value is missing is left in the output verbatim (`:29393`).
So `"Costs {AMOUNT:Money} per month"` formats through the game's own money formatter with no C# code at all, and `GetArgNames` will not list `AMOUNT` because the whole `{AMOUNT:Money}` token fails its `[A-Z0-9_]+` class.
The shipped strings confirm the spec first-party rather than leaving it on the vendored bundle's authority, and show how rarely the game itself leans on it — one occurrence across all thirteen locales, detailed in the compiled-locale-data finding below.

**Indexed keys are how a random variant is picked.** `RandomLocalizationIndex` is a serialized buffer element holding one chosen index per localization slot, generated from a `LocalizationCount` buffer (`src/Game/Game.Common/RandomLocalizationIndex.cs:9-23`, `kNone` is `-1` at `:11`), and `LocalizationUtils.AppendIndex` turns a base id plus that index into `"<id>:<index>"`, returning the bare id when the index is `-1` (`src/Game/Game.UI.Localization/LocalizationUtils.cs:5-14`).
The counts reach the frontend as the `l10n`/`indexCounts` raw map binding, answered from `activeDictionary.indexCounts` (`src/Game/Game.UI.Localization/LocalizationBindings.cs:47`, `:70-73`).
Zero corpus mods ship an indexed key.

### The eleven generated key helpers, and what the options screen actually looks up

`ModSetting` exposes eleven public key builders (`src/Game/Game.Modding/ModSetting.cs:303-371`). `id` is the page id, `name` is the settings class's type name; both are established in `settings-and-input.md` and re-verified here at `ModSetting.cs:36-44`.

| Helper | Key produced | Verified against |
| --- | --- | --- |
| `GetSettingsLocaleID()` | `Options.SECTION[<id>]` | `ModSetting.cs:303-306` |
| `GetOptionLabelLocaleID(opt)` | `Options.OPTION[<id>.<name>.<opt>]` | `:308-311` vs `AutomaticSettings.cs:341-373` |
| `GetOptionDescLocaleID(opt)` | `Options.OPTION_DESCRIPTION[<id>.<name>.<opt>]` | `:313-316` vs `AutomaticSettings.cs:341-373` |
| `GetOptionWarningLocaleID(opt)` | `Options.WARNING[<id>.<name>.<opt>]` | `:318-321` vs `AutomaticSettings.cs:1160-1166` |
| `GetOptionTabLocaleID(tab)` | `Options.TAB[<id>.<tab>]` | `:323-326` |
| `GetOptionGroupLocaleID(group)` | `Options.GROUP[<id>.<group>]` | `:328-331` vs `OptionsUISystem.cs:1042-1051` |
| `GetEnumValueLocaleID<T>(value)` | `Options.<id>.<ENUMTYPENAME>[<Member>]` | `:333-336` vs `AutomaticSettings.cs:846-855`, `:906-912` |
| `GetOptionFormatLocaleID(opt)` | `Options.FORMAT[<id>.<name>.<opt>]` | `:338-341` vs `source.js:73267` |
| `GetBindingKeyLocaleID(action[,c])` | `Options.OPTION[<id>/<action>/<component>]` | `:343-361` |
| `GetBindingKeyHintLocaleID(action)` | `Common.ACTION[<id>/<action>]` | `:363-366` |
| `GetBindingMapLocaleID()` | `Options.INPUT_MAP[<id>]` | `:368-371` vs `InputManager.cs:1508` |

Every one of these is a well-formed **Hashed** identifier except `GetEnumValueLocaleID`, which puts the page id between the group and the id rather than inside the brackets. Since the page id is itself dotted — `<Assembly>.<Namespace>.<ModType>` (`ModSetting.cs:39`) — the result has four or more dots outside the brackets and matches none of the four regexes. It works for the same reason the wiki's shape does, and nothing else in the eleven has that property.

**Two of the eleven are riskier than they look.**

`GetEnumValueLocaleID<T>` uppercases with the culture-sensitive `typeof(T).Name.ToUpper()` (`:335`) while the engine that reads the key uses `enumType.Name.ToUpperInvariant()` (`AutomaticSettings.cs:908`). The game forces `CultureInfo.CurrentCulture = CultureInfo.InvariantCulture` at boot (`src/Game/Game.SceneFlow/GameManager.cs:536`), so the two agree in practice; the mismatch is real in the source and inert in the process.

`GetOptionFormatLocaleID` has **no C# consumer at 1.6.0f1** — a grep of `src/Game/` for `Options.FORMAT` returns only its own definition — and **zero corpus uses**. Its consumer is the frontend: the slider widget builds `` `Options.FORMAT[${e.path}]` `` with the default `"{SIGN}{VALUE}"` and substitutes `VALUE` and `SIGN`, and only when the slider's unit is `custom` (`DecompiledCitiesSkylines2/src-ui/source.js:73253-73273`, the custom branch also carrying the widget's `fractionDigits`, `separateThousands` and `maxValueWithFraction` props). The widget's `path` is `<pageId>.<declaringType>.<property>` (`AutomaticSettings.cs:327-339`), which is exactly what the helper builds, so the pair is correct.
A slider gets `unit = "custom"` from `[SettingsUICustomFormat]` (`AutomaticSettings.cs:1305-1311` for `int`, `:1342-1350` for `float`). One corpus mod carries that attribute and ships no `Options.FORMAT` string for it, so it renders the default (`LineTool-CS2/Code/ModSettings.cs:34-36`).

**Three attributes can override the generated key, and all three drop the page prefix.**
`[SettingsUIDisplayName(overrideId, overrideValue)]` produces `Options.OPTION[<overrideId verbatim>]` and `[SettingsUIDescription]` produces `Options.OPTION_DESCRIPTION[<overrideId verbatim>]`, each through `LocalizedString.IdWithFallback(key, overrideValue)`; with only a value and no id they produce `LocalizedString.Value(...)` and skip the dictionary entirely; with neither they fall through to the path-derived key (`AutomaticSettings.cs:341-373`).
`[SettingsUIConfirmation(overrideConfirmMessageId, overrideConfirmMessageValue)]` behaves the same way for `Options.WARNING[...]` (`:1146-1167`).
The important half is that the override id is **not** prefixed with the page id, so it is a bare, globally-shared key rather than a mod-scoped one — a name collision with another mod or with the game is possible in a way the generated keys make impossible.

One corpus mod uses it, and does the right thing with it: `PlopTheGrowables/Code/ModSettings.cs:68-69` puts `[SettingsUIDisplayName(overrideId: MakePloppedHistorical)]` and `[SettingsUIDescription(overrideId: MakePloppedHistorical)]` on a property whose own name is `LockPloppedBuildings`, so a rename of the property does not orphan the translations (`MakePloppedHistorical` is a `const string` at `:29`). Its CSV then carries the two keys written out in full — `"Options.OPTION[MakePloppedHistorical]"` and `"Options.OPTION_DESCRIPTION[MakePloppedHistorical]"` (`PlopTheGrowables/l10n/en-US.csv:6-7`, and the same two rows in every other locale file) — which passes through that mod's own key-packing unpacker untouched, because a row with no colon is returned unchanged (`PlopTheGrowables/Code/Localization.cs:231-236`).
Nothing else in the corpus overrides a key: all 31 `GetOptionWarningLocaleID` uses across nine repositories go with the generated form, and no repository uses `overrideValue` in any of the three.

Corpus usage of the eleven, swept across all 20 repositories:

| Helper | Uses | Repositories |
| --- | ---: | ---: |
| `GetOptionLabelLocaleID` | 560 | 19 |
| `GetOptionDescLocaleID` | 533 | 14 |
| `GetEnumValueLocaleID` | 164 | 5 |
| `GetOptionGroupLocaleID` | 111 | 10 |
| `GetBindingKeyLocaleID` | 61 | 6 |
| `GetOptionTabLocaleID` | 37 | 10 |
| `GetOptionWarningLocaleID` | 31 | 9 |
| `GetSettingsLocaleID` | 16 | 15 |
| `GetBindingMapLocaleID` | 5 | 5 |
| `GetBindingKeyHintLocaleID` | 1 | 1 |
| `GetOptionFormatLocaleID` | 0 | 0 |

`GetBindingMapLocaleID` names the mod's action map in the rebinding UI and in the conflict notification, and five mods set it (`Anarchy/Anarchy/Settings/LocaleEN.cs:95`, `CS2-MoveIt/Code/MoveIt/Settings/LocaleEN.cs:31`, `CS2-Platter/Platter/L10n/EnUsConfig.cs:32`, `Recolor/Recolor/Settings/LocaleEN.cs:99`, `Traffic/Code/Localization.LocaleEN.cs:48`).
`GetBindingKeyHintLocaleID` has exactly one worked example (`Traffic/Code/Localization.LocaleEN.cs:225`).
Network Tools is the only repository that sets both the option label **and** the binding key label for every action, which is what makes the keybinding row read as prose rather than as a property name (`CS2-NetworkTools/NetworkTools.Mod/L10n/EnUsConfig.cs:32-34` and the same triple repeated per action through `:80`).

### Six packaging strategies the corpus proves work, and what each costs

Every one of these ends at `AddSource`. What differs is where the strings live, who can edit them, and what happens when a translator sends a file back.

**1. Hand-written C# `IDictionarySource`, English only** — 10 repositories.
A class holding one `Dictionary<string, string>` built in its constructor from the eleven helpers, returned unchanged from `ReadEntries` (`Anarchy/Anarchy/Settings/LocaleEN.cs:14-28/219-222` is the archetype; the same shape at `AreaBucket/BasicLocale.cs`, `BetterBulldozer/BetterBulldozer/Settings/LocaleEN.cs`, `CS2-MoveIt/Code/MoveIt/Settings/LocaleEN.cs`, `CS2-NetworkTools/NetworkTools.Mod/L10n/EnUsConfig.cs:17-30`, `CS2-Platter/Platter/L10n/EnUsConfig.cs`, `Recolor/Recolor/Settings/LocaleEN.cs`, `Time2Work/NightShift/Setting.cs:1018`, `Tree_Controller/Tree_Controller/Settings/LocaleEN.cs`, `Water_Features/Water_Features/Settings/LocaleEN.cs`).
Cost: nothing to build, and it is the only strategy where `nameof(...)` keeps the key and the property in lockstep, so a renamed setting breaks the build rather than the label. It cannot be translated without editing C#.
Time2Work is the strategy taken to its conclusion and the corpus's clearest anti-pattern: a second full C# class, `LocalePT`, for Brazilian Portuguese, registered beside the English one (`Time2Work/NightShift/Setting.cs:1474`, registered at `Time2Work/NightShift/Mod.cs:87-88`). A translator has to open a 1,500-line settings file.

**2. Embedded per-locale JSON, one resource per locale** — 6 repositories, and the corpus's most-copied block.
`{AssemblyName}.l10n.{localeID}.json` as an `EmbeddedResource`, read with `Colossal.Json.JSON.Load(text).Make<Dictionary<string,string>>()` into a `MemorySource` (`Anarchy/Anarchy/AnarchyMod.cs:170-215`, verbatim in `BetterBulldozer/BetterBulldozer/BetterBulldozerMod.cs:144-190`, `CS2-MoveIt/Code/MoveIt/Mod.cs:107-151`, `CS2-Platter/Platter/PlatterMod.cs:489-522`, `Recolor/Recolor/Mod.cs:174-215`, `Tree_Controller/Tree_Controller/TreeControllerMod.cs:147-190`, `Water_Features/Water_Features/WaterFeaturesMod.cs:175-215`).
The loop iterates `GetSupportedLocales()` and looks for a matching resource, so a file named for a locale the game does not ship is simply never opened — and a per-file `try`/`catch` keeps one malformed translation from killing the rest (`AnarchyMod.cs:206-210`).
Cost: the strings ship inside the DLL, so a translation fix needs a rebuild and a republish. Paired with strategy 1 for English, which is why these six also carry a `LocaleEN` class.

**3. Embedded per-locale CSV with packed option keys** — 2 repositories, both algernon's, from a shared file.
`{AssemblyName}.l10n.{localeID}.csv`, parsed by a hand-written quote-aware reader that accepts comma or tab, `""` for an embedded quote, and multi-line quoted values (`PlopTheGrowables/Code/Localization.cs:43-211`; the doc comment at `:25-40` is a complete spec). `LineTool-CS2/Code/Localization.cs` is the same file with one difference: it accepts a null settings object and then disables key packing (`:137`).
The distinguishing idea is **key packing**: a CSV row writes `Options.OPTION:MyProperty` and `UnpackOptionsKey` expands the prefix through the settings object (`PlopTheGrowables/Code/Localization.cs:228-252`). Four prefixes are recognised — `Options.GROUP`, `Options.OPTION`, `Options.OPTION_DESCRIPTION`, `Options.WARNING`.
Cost: a translator edits a spreadsheet and never sees the long generated keys. The trap is the `switch`'s default arm: **any unrecognised prefix silently maps the row to `GetSettingsLocaleID()`** (`:250`), so one typo in a prefix rewrites the mod's page title instead of failing.

**4. One embedded JSON per locale-set, loaded through a shared `LocaleHelper`** — 4 repositories.
`LocaleHelper(string dictionaryResourceName)` reads the named resource as the base (`en-US`) and every sibling resource whose name extends it as another locale, yielding one `DictionarySource` per locale (`RoadBuilder-CSII/RoadBuilder/Utilities/LocaleHelper.cs:16-73`, its nested `DictionarySource` at `:75-93`). The caller loops and adds each (`RoadBuilder-CSII/RoadBuilder/Mod.cs:51-59` — twice, for two separate dictionaries).
The same file appears in `FindIt-CSII/FindIt/Utilities/LocaleHelper.cs`, `InfoLoom/InfoLoom/Extensions/LocaleHelper.cs` and `NodeController/NodeController/Extensions/LocaleHelper.cs` with only namespace and formatting differences; FindIt's copy adds a second constructor taking a dictionary directly.
Cost: the same rebuild-to-fix as strategy 2, but the mod's whole translation set is two files rather than fifteen, and `GetAvailableLanguages()` covers English too so no `LocaleEN` class is needed. All four also ship a static `Translate(id, fallback)` over `activeDictionary.TryGetValue` (`LocaleHelper.cs:52-60`).

**5. Loose JSON files beside the DLL** — 2 repositories, and the only strategy a user can fix without a rebuild.
Traffic resolves its own install directory through `GameManager.instance.modManager.TryGetExecutableAsset(mod, out var asset)`, enumerates `Localization/*.json` beside it, and takes the file's base name as the locale id (`Traffic/Code/Localization.cs:26-46`). 18 locale files ship that way (`Traffic/Code/Localization/`).
Cost: the files must be copied to the output directory by the csproj, and a mod that loads its own files owns the parse errors. What it buys is everything in the next finding.
`ExtraAssetsImporter` takes the same idea one step further: the JSON files are in the **user's** asset folders, not the mod's, so a content author ships translations with their assets (`ExtraAssetsImporter/MOD/AssetImporter/Importers/LocalizationImporter.cs:75-86`).

**6. A `LocaleAsset` written into an asset database** — 1 repository, and the only strategy that reaches the game's own locale plumbing.
`ExtraAssetsImporter/MOD/AssetImporter/Importers/LocalizationImporter.cs:51-59` builds `new LocaleData(localeID, entries, new())`, adds a `LocaleAsset` at a computed `AssetDataPath`, calls `SetData(localeData, localizationManager.LocaleIdToSystemLanguage(localeID), localizationManager.GetLocalizedName(localeID))` and `Save()`, then `AddLocale(localeAsset)` on the main thread.
The asset then registers itself as a source through the manager's asset-database subscription (`LocalizationManager.cs:56`, `:162-182`), so no `AddSource` is needed.
Cost: a `.loc` is a binary file (`LocaleAsset.kExtension = ".loc"`, format version 1, `LocaleAsset.cs:11-13`, writer at `:136-165`) and the mod is now responsible for an asset's lifetime. What it buys is that the translations survive as data rather than as a live object, and that `AddLocale` can introduce a locale the game does not ship.
The importer marshals both calls onto the main thread with `MainThreadDispatcher.RunOnMainThread` because the import runs on a worker (`:58`, `:62`, with a one-frame wait at `:65`).

**Verdict: the wiki's `lang/` folder is I18n EveryWhere's convention, not the game's.**
The page prescribes a lowercase `lang/` directory holding `en-US.json`, copied to the output by the csproj. Nothing in `Colossal.Localization` or `Colossal.IO.AssetDatabase` knows any folder name: `LoadAvailableLocales` reads whatever `LocaleAsset`s the database holds (`LocalizationManager.cs:132-140`), and every other route is a mod calling `AddSource` with a dictionary it built itself.
The corpus confirms it. `L10n/lang/` exists in two repositories and is a **Crowdin source directory**, not a runtime path: `CS2-NetworkTools/crowdin.yaml:1-3` maps `/NetworkTools.Mod/L10n/lang/en-US.json` to `/NetworkTools.Mod/L10n/lang/%locale%.json`, and `CS2-Platter/Platter/PlatterMod.cs:361-373` writes its export there. Traffic's loose files sit in `Localization/`, algernon's in `l10n/`, the JSON-per-locale-set mods embed theirs.

**Verdict: I18n EveryWhere is a wiki recommendation the corpus does not exercise in code.**
The page recommends taking mod 75426 as a dependency to avoid writing a parser. Exactly one of twenty repositories declares it — `Time2Work/NightShift/Properties/PublishConfiguration.xml:137` — and that mod hand-writes two C# dictionary classes anyway. Two more name it in prose only, as the thing a player needs installed for European Portuguese (`Anarchy/Anarchy/Properties/Stable/PublishConfiguration.xml:25`, `BetterBulldozer/.../PublishConfiguration.xml:17`).
No repository references its API, and its source is not in the checkout, so nothing about how it works is verifiable here. That the two mods naming it tie it specifically to `pt-PT` is consistent with the mechanism established above — `AddSource` for an unregistered locale is a silent no-op, so a locale the base game does not ship needs someone to call `AddLocale` first.

### A mod-private language, chosen independently of the game's

Traffic is the corpus's only mod whose settings screen carries its own language dropdown, and the mechanism is worth stating because it is not obvious: it does **not** switch the game's locale. It adds its own translation for language X as a source **under the game's currently active locale id** (`Traffic/Code/Localization.LocaleManager.cs:179-210`, the substantive lines at `:194-199`):

```
manager.RemoveSource(gameLocale, LocaleSources[gameLocale].Item3);      // its own translation for the game's language
manager.RemoveSource(gameLocale, LocaleSources[currentLanguage].Item3); // in case of a repeat
manager.AddSource(gameLocale, LocaleSources[currentLanguage].Item3);    // the language the player chose in the mod
```

`LocaleSources` is a static map from locale id to `(display name, coverage percentage, source)` populated as each file loads (`Traffic/Code/Localization.ModLocale.cs:56-60`).
A `VanillaLocalizationObserver` subscribes to `LocalizationManager.onActiveDictionaryChanged` and repeats the swap whenever the player changes the game's language, with a re-entrancy flag so its own `AddSource` calls do not retrigger it (`Localization.LocaleManager.cs:95-177`, the flag at `:99/109-117`, the swap at `:149-164`).
The same load pass computes a translation-coverage percentage against the English entry count and writes it into the settings page as a label (`Localization.ModLocale.cs:46-54`), and fills every key missing from a translation from the English source with `TryAdd` (`:49-53`) — a manual reimplementation of what `AddMissingEntriesFromFallback` already does for `en-US`.
Cost: it is 213 lines of state machine, and it is the design most exposed to the `m_UserSources` finding above.

### Formatting numbers: four element types in C#, and the whole apparatus on the frontend

**C# offers exactly four `ILocElement` implementations** (`src/Game/Game.UI.Localization/`): `LocalizedString` (`LocalizedString.cs:9`), `LocalizedNumber<T>` (`LocalizedNumber.cs:8`), `LocalizedFraction<T>` (`LocalizedFraction.cs:8`) and `LocalizedBounds<T>` (`LocalizedBounds.cs:8`). `ILocElement` is an empty interface extending `IJsonWritable` (`ILocElement.cs:5-6`).
There is **no** C# percentage, date, duration or time element, and no C# formatting function of any kind: a grep of `src/Game/` for `FormatDuration`, `FormatDate` and `FormatPercentage` returns nothing. Each of the three numeric elements writes its raw value, an optional unit **string**, and one flag, and the frontend does all the work (`LocalizedNumber.cs:27-37`, `LocalizedFraction.cs:27-37`, `LocalizedBounds.cs:27-37`).

`LocalizedString`'s seven static factory overloads across four names are the whole surface (`LocalizedString.cs:39-74`): `Id(id)` and `Id(id, params (string,ILocElement)[])`, `IdHash<T>(id, hash)` and its substitutions overload, `Value(value)`, and `IdWithFallback(id, value)` and `IdWithFallback<T>(id, hash, value)`. `IdHash` composes `$"{id}[{hash}]"` (`:52`), which is the Hashed shape. There is an implicit conversion from `string`, so a bare string literal anywhere a `LocalizedString` is expected becomes `Id(...)` and not `Value(...)` (`:124-127`) — the single most likely way to end up with a raw key on screen.
`NameTooltipPair`'s implicit conversion pairs `id` with `id + "_TOOLTIP"` (`src/Game/Game.UI.Localization/NameTooltipPair.cs:9-18`), and `CachedLocalizedStringBuilder<T>` memoises a key-building lambda per value (`CachedLocalizedStringBuilder.cs:6-38`).

The game's own worked examples of substitution: `src/Game/Game.Modding/ModManager.cs:337-347` builds a mod-loading notification with `LOADED` and `TOTAL` as `LocalizedNumber<int>(n, "integer")`, and `src/Game/Game.Settings/GraphicsSettings.cs:634-655` builds a resolution label from `WIDTH`, `HEIGHT` and a `LocalizedNumber<double>` for `REFRESH_RATE` whose unit switches between `"screenFrequency"` and `"integer"`.

**The unit is a string constant, and the C# list is shorter than the frontend's.**
`Game.UI.Unit` is a static class of 33 `const string`s — `kInteger = "integer"` through `kCustom = "custom"` (`src/Game/Game.UI/Unit.cs:3-70`).
The frontend's own `Unit` enum has **38** members: the 33 plus `PercentagePrecise`, `BodiesPerMonth`, `TemperaturePrecise`, `Height` and `DurationSeconds` (`DecompiledCitiesSkylines2/src-ui/source.js:28979-29018`, registered as `game-ui/common/localization/unit.ts` at `:29019-29026`). Those five have no C# constant, so reaching them from C# means writing the literal.
The official UI mod scaffold's `cs2/l10n` declaration declares the same 38 (`@colossalorder/create-csii-ui-mod/template/types/l10n.d.ts:17-56`), so it is a faithful declaration of the runtime enum rather than a source in its own right.
That file is vendored per mod and drifts: seven distinct versions across 18 copies in the corpus — at 202, 203, 205, 209, 210, 211 and 213 lines — the oldest at 202 lines (`Time2Work/NightShift/Time2WorkUI/src/types/l10n.d.ts`), missing `PercentagePrecise`, `TemperaturePrecise`, `ScreenFrequency`, `Height`, `Custom` and `DurationSeconds` outright.
The newest, at 213 lines in 7 repositories, is byte-identical to the scaffold's apart from line endings — so a vendored copy is the one artefact here that can be wrong about the unit list, and the scaffold is the copy that cannot.

Rots: the unit list on both sides — re-read `src/Game/Game.UI/Unit.cs`, and the `Unit` enum in a fresh reformat of `Cities2_Data/Content/Game/UI/index.js` (`src-ui/source.js:28979-29018` in this one).

**The frontend's number formatter is a lookup table with a garbage fallback.**
The dispatch is a lookup on the unit with a fallback lambda that renders the number followed by the unit name in angle brackets, so an unrecognised unit string prints as `1234 <myUnit>` (`DecompiledCitiesSkylines2/src-ui/source.js:29133-29135`). That is the visible symptom of a typo'd unit.
The table itself is where the player's preferences bite (`:29136-29269`). A sample, with the locale key each branch renders through:

- `Integer` → plain, thousands-separated. `IntegerRounded` switches to `Common.VALUE_THOUSAND` above 1,000 and `Common.VALUE_MILLION` above 1,000,000 (`:29145-29151`).
- `Length` → metric: `Common.VALUE_METER` below 1,000, `Common.VALUE_KILOMETER` above; freedom: `Common.VALUE_YARD` below 1,609, `Common.VALUE_MILE` above (`:29164-29171`).
- `Area`, `Volume`, `Weight`, `WeightPerCell`, `WeightPerMonth`, `Height`, `NetElevation`, `MoneyPerDistance`, `MoneyPerDistancePerMonth` all branch on `unitSettings.unitSystem` the same way (`:29172-29244`).
- `Temperature` branches on `unitSettings.temperatureUnit` across `Common.VALUE_CELSIUS`, `VALUE_FAHRENHEIT`, `VALUE_KELVIN` (`:29246-29255`).
- `Power` divides by 10 and renders kilowatts below 10,000, by 10,000 for megawatts above — so the raw C# value is in units of 100 W (`:29220-29223`).
- `Money` uses `Common.VALUE_MONEY` with no unit-system branch at all (`:29230`).

The separators come from the dictionary too: `Common.THOUSANDS_SEPARATOR` and `Common.DECIMAL_SEPARATOR` are rendered per call and applied by regex (`:29273-29306`). The sign prefix is `-` for a negative value always, and `+` for a positive one or `±` for zero **only when `signed` is set** — otherwise both are empty (`:29332-29334`).

**Verdict: `PercentagePrecise` and `TemperaturePrecise` have formatter entries.**
`PercentagePrecise` renders `Common.VALUE_PERCENT` at two fraction digits against `PercentageSingleFraction`'s one (`:29160-29163`), and `TemperaturePrecise` branches on `unitSettings.temperatureUnit` exactly as `Temperature` does, also at two — against `Temperature`'s none, since it renders through the integer formatter's `toFixed(0)` rather than the fraction one (`:29256-29265`, `Temperature` at `:29246-29255`, the two formatters at `:29274` and `:29290`).
Both are live in the map editor's climate curve readouts — the weather-classification keyframe formatter and the temperature keyframe formatter (`:56729-56730`, `:56758-56760`).

**One unit of the 38 has no number formatter: `BodiesPerMonth`.**
The number table covers 37 (`:29136-29269`); `BodiesPerMonth` appears only in the fraction table (`:29593-29594`), so `LocalizedNumber` with it hits the angle-bracket fallback while `LocalizedFraction` with it renders correctly.

**`LocalizedFraction` and `LocalizedBounds` support far fewer units than `LocalizedNumber`, and fail loudly-but-ugly outside them.**
Fractions handle exactly eleven: `Volume`, `VolumePerMonth`, `Weight`, `WeightPerMonth`, `Power`, `Energy`, `BodiesPerMonth`, `XP`, `Integer`, `IntegerPerMonth` and `IntegerRounded`; anything else renders `` `${value} / ${total} <${unit}>` `` (`:29541-29605`, the fallback at `:29545`).
The vendored bundle had ten, missing `IntegerRounded` (`:29599-29604`).
Bounds handle exactly three: `Power`, `PercentageSingleFraction`, `Temperature`; anything else renders `` `${min}–${max} <${unit}>` `` (`:29493-29520`, the fallback at `:29498`). Bounds also short-circuits to `LocalizedNumber` when `min === max` (`:29495-29500`).
Both default to `Unit.Integer` when no unit is given, which is why an unspecified fraction renders through `Common.FRACTION_INTEGER` rather than failing.

**Percentage, date and duration exist only on the frontend.**
`LocalizedPercentage(value, max)` computes `100 * value / max` and renders it as a `Percentage`-unit number — but **clamps any positive result to a minimum of 1** (`source.js:30111-30118`), so 0.2% displays as 1%. The clamp sits behind a `value > 0 && max > 0` guard whose else-branch renders a plain `0`, so a zero or negative max never divides (`:30114`).
`LocalizedDate({year, month})` renders `Common.MEDIUM_DATE_FORMAT` with `MONTH` resolved through the indexed key `Common.MONTH_SHORT:<month>` (`source.js:29795-29810`, `:29845-29847`). The month is **zero-based**: the game's only C# producer of a `SimulationDateTime` passes `currentDateTime.DayOfYear - 1` as the month (`src/Game/Game.UI.Menu/MenuUISystem.cs:869`), and a game year is twelve days (`ClimateSystem` asserts `daysPerYear == 12`, `src/Game/Game.Simulation/ClimateSystem.cs:320`), so a day _is_ a month.
`LocalizedDuration({value, daysPerYear, maxMonths})` takes a value in days and picks `Common.VALUE_YEARS`/`VALUE_YEAR` at or above `maxMonths` (defaulting to `daysPerYear`), `Common.VALUE_MONTHS` above one, `Common.VALUE_MONTH` above 23.5/24 of a day, and otherwise falls through to `Common.TIME_FORMAT` with hours and minutes derived from the fraction (`source.js:29947-29979`). The first two thresholds compare the **rounded** value, the third the raw one (`:29950-29965`).

**Verdict: the wiki is right that there is no simple way to display a time, and the reason is an export list rather than a missing feature.**
The page says "as of build 1.1.2f1, there seems to be no way to display time in a simple way" and recommends formatting by hand from `unitSettings.timeFormat`.
The game has `LocalizedTime`, `LocalizedDateTime` and `LocalizedTimestamp`, plus `useTimeFormat`, `useDateFormat`, `formatInteger` and `formatFloat` — all registered in the module registry under `game-ui/common/localization/localized-date.tsx` and `.../localized-number.tsx` (`source.js:29896-29945`, `:29336-29379`). `LocalizedTime` already branches on `unitSettings.timeFormat` and renders `Common.TIME_FORMAT` or `Common.TIME_FORMAT_12` with `Common.TIME_PERIOD_AM`/`PM` (`:29811-29832`, the AM/PM helper at `:29891-29895`). `LocalizedTimestamp` goes further and interprets a `yyyy/MM/dd/HH/hh/mm/aa` pattern out of `Common.TIMESTAMP_FORMAT` (`:29848-29890`).
What `cs2/l10n` exports at runtime is **eleven names and no more**: `Localized`, `LocalizedBounds`, `LocalizedDate`, `LocalizedDuration`, `LocalizedEntityName`, `LocalizedFraction`, `LocalizedNumber`, `LocalizedPercentage`, `LocalizedString`, `Unit`, `useLocalization` (`source.js:12413-12427`, attached to `window` as `cs2/l10n` at `:47076-47089` alongside `cs2/api`, `cs2/bindings`, `cs2/ui`, `cs2/utils`, `cs2/input`, `cs2/modding`, `cohtml/cohtml` and `chart.js`).
So the wiki's advice is right for the public module and wrong about the capability; the time formatters are reachable the same way any unexported game component is, which is `frontend-and-injection`'s material.

**Verdict on the wiki's minified-enum warning, narrowed to the one enum that matters.**
The page warns that enums from `cs2/l10n` cannot have their values read at runtime and recommends hard-coding the numbers.
The runtime export list settles it precisely: `Unit` **is** exported and its members are live string values (`source.js:12425`, the enum body at `:28979-29018`), so `Unit.Money` works. `TimeFormat`, `TemperatureUnit` and `UnitSystem` are **not** on that list, even though the `.d.ts` declares all three as `export enum` (`@colossalorder/create-csii-ui-mod/template/types/l10n.d.ts:95-107`). Their values are `TwentyFourHours = 0`/`TwelveHours = 1`, `Celsius = 0`/`Fahrenheit = 1`/`Kelvin = 2`, `Metric = 0`/`Freedom = 1` (`source.js:26088-26099`).

**But they are not out of reach, which narrows the wiki's advice by one step.**
All three are registered in the module registry under `game-ui/menu/data-binding/options-bindings.ts`, beside `BindingConflict` and `defaultUnitSettings` (`source.js:26110`, the three getters at `:26345-26362`).
So writing the literals is what the public module forces, and a mod already reaching into the registry — which is `frontend-and-injection`'s subject — can have the live enums instead.

### The player's unit and format preferences, and how a mod reads them

Three enums on one settings class, all under a `Unit` group in the interface settings page (`src/Game/Game.Settings/InterfaceSettings.cs:183-190`):

- `TimeFormat { TwentyFourHours, TwelveHours }` (`:32-36`)
- `TemperatureUnit { Celsius, Fahrenheit, Kelvin }` (`:38-43`)
- `UnitSystem { Metric, Freedom }` (`:45-49`)

Defaults are `TwentyFourHours`, `Celsius`, `Metric` (`:242-244`).

**From C#**, read them off `SharedSettings.instance.userInterface` — the property is typed `InterfaceSettings` and `settings-and-input.md:409` establishes the same seam from the other side.
**From the frontend**, they arrive as one `UnitSettings` struct on the `options` binding group: `new GetterValueBinding<UnitSettings>("options", "unitSettings", () => new UnitSettings(SharedSettings.instance.userInterface), ...)` (`src/Game/Game.UI.Menu/OptionsUISystem.cs:589`, the struct at `:393-399`, written as three ints at `:401-411`). `useLocalization()` returns `{ translate, unitSettings }`, so a component has them without a binding of its own (`@colossalorder/create-csii-ui-mod/template/types/l10n.d.ts:108-112`, implementation at `source.js:26380-26402`, the React context it reads at `:26376-26379` and the provider that fills it at `:67874-67903`).
The practical answer is that a mod formatting a number through `LocalizedNumber` with the right unit never has to read them at all — the formatter branches on `unitSettings` itself. Reading them directly is for the cases the unit table does not cover.

**The active language lives beside them, and changing it is a settings write.**
`InterfaceSettings.locale` is a hidden string defaulting to the literal `"os"` (`:90-91`, `:211`), which `LocalizationManager.SetActiveLocale` resolves to the system language through `Application.systemLanguage` (`LocalizationManager.cs:206-234`, `kOsLanguage = "os"` at `:23`, `GetSystemLocaleId` at `:184-192`).
`currentLocale` is the visible dropdown, `[Exclude]`d from serialization, resolving `"os"` to the live active id on read (`:71-88`); its item list is `GetLanguageValues()`, which pairs each supported locale id with `LocalizedString.Value(GetLocalizedName(id))` — a **literal** display name from the locale asset's header, not a key (`:255-271`, `LocaleAsset.localizedName` at `:61`, read at `:83-92`).
`InterfaceSettings.Apply()` calls `SetActiveLocale(locale)` (`:249-253`), and the frontend's own `l10n`/`selectLocale` trigger does both — switch the manager and write the setting back (`src/Game/Game.UI.Localization/LocalizationBindings.cs:75-83`).
The `l10n` binding group is four bindings and one trigger: `locales`, `debugMode`, `activeDictionaryChanged`, `indexCounts`, `selectLocale` (`LocalizationBindings.cs:44-48`).

Rots: the three preference enums and their member order — re-read `src/Game/Game.Settings/InterfaceSettings.cs:32-49`.

### The game's own compiled locale data, and how to read it

Every vanilla string ships as a compiled `.loc` asset, and those assets are readable from disk with no game process and no decompiler.
They are the first-party source for the namespace table in the next finding, and for several claims elsewhere in this file that previously rested on the vendored UI bundle.
The artifact is the **user's own installed copy of the game** rather than anything this repository or the decompile checkout holds, so it is cited as an install-relative path plus the installed version, and the install is read-only throughout.

**The installed version is `1.6.0f1 (419.d6c6) [6216.19404]`, which is the decompile's baseline.**
Neither file usually reached for answers it: `Cities2_Data/app.info` holds only the publisher and product name, and `Cities2_Data/boot.config` only a Unity build GUID and graphics flags.
The game itself answers it — `GameManager` logs `Version.current.fullVersion` at startup and writes the same string into a `version` file in its persistent data folder (`src/Game/Game.SceneFlow/GameManager.cs:2295`, `:2336`, the string composed at `src/Colossal.Core/Colossal/Version.cs:58-62`) — and both read that build.
So the shipped data and the decompiled source are the same build, and every count below is a 1.6.0f1 count rather than an approximation of one.

**Thirteen locales, in two places.**
Twelve are packed into `Cities2_Data/Content/Game/Locale.cok`: `de-DE`, `en-US`, `es-ES`, `fr-FR`, `it-IT`, `ja-JP`, `ko-KR`, `pl-PL`, `pt-BR`, `ru-RU`, `zh-HANS`, `zh-HANT`.
The thirteenth, `uk-UA`, sits loose as `Cities2_Data/StreamingAssets/uk-UA.loc`, and it is the only `.loc` outside the package — the sixteen DLC and radio-pack directories under `Cities2_Data/Content/` ship none, so a content pack's strings live in the base locale files rather than in a locale file of its own.
A `.cok` is the asset-database package extension (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/PackageAsset.cs:7-9`) and is a plain zip archive: `ZipPackageWriter` adds each asset with `CompressionMethod.Stored` and a sibling `<name>.cid` entry holding the asset's GUID as UTF-8 text (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ZipPackageWriter.cs:51-74`).
So the payload comes out with any zip reader and no decompression step.

**The payload is a flat `BinaryWriter` stream, and the reader is the whole specification.**
`LocaleAsset.Load` (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/LocaleAsset.cs:109-134`) and `LocalizationCompiler.WriteLocale` (`src/Colossal.Localization/Colossal.Localization/LocalizationCompiler.cs:206-227`) are mirror images of each other:

```
ushort formatVersion               // LocaleAsset.kFormatVersion == 1 (LocaleAsset.cs:11-13)
string systemLanguage              // parsed into UnityEngine.SystemLanguage, ex. "English"
string localeId                    // "en-US"
string localizedName               // "English" - the literal the language dropdown shows
int    entryCount
entryCount      x (string key, string value)
int    indexCountCount
indexCountCount x (string key, int count)
```

Every `string` is `BinaryWriter`'s own encoding — a 7-bit-encoded byte length followed by UTF-8 — so `BinaryReader.ReadString` decodes one without a hand-written string reader.
There is no compression, no checksum, no table of contents and no offset table, which is why `ReadHeader` can take the locale id and display name by reading just the first four fields and stopping (`LocaleAsset.cs:83-92`).

**Decoding all thirteen.** `en-US` holds **22,120 entries and 260 index-count keys**; the other eleven packed locales hold 22,122 entries each and the same 260 index-count keys; `uk-UA` holds 19,560 entries and 253 index-count keys.
The two entries every locale has and `en-US` lacks are `Assets.NAME[HedgeHigh Placeholder]` and `Assets.NAME[HedgeLow Placeholder]` — placeholder rows a translator filled and English did not.
`uk-UA` is a partial translation shipped outside the package, missing about 12% of the key set.
Because `en-US` is the hard-coded fallback locale (`src/Game/Game.SceneFlow/GameManager.cs:2356-2361`), its key set is the one that defines what exists, and every count in the next finding is an `en-US` count.

**Verdict: the shipped locale set is thirteen, and it is not the thirteen the corpus translates into.**
`GetSupportedLocales()` returns whatever `LocaleAsset`s the database holds, which could not be read from `src/` (see the dead end below, now closed).
The install settles it: the twelve packed ids plus `uk-UA`.
The corpus's most-translated set differs from it by two swaps rather than matching — it carries `nl-NL` in 29 or more translation folders, `pt-PT` in 34 and `ar-SA` in 24, and **none of those three is a base-game locale**, while `uk-UA`, which the game does ship, appears in only five paths across the twenty repositories.
So a mod translating into Dutch, European Portuguese or Arabic is shipping strings for a locale the game never registers, and `AddSource` for an unregistered locale is a silent no-op — something has to call `AddLocale` first.
That is the mechanism behind the two mods whose store pages say European Portuguese needs a companion mod.

**Verdict: the four identifier shapes account for every vanilla key, with no exceptions.**
Running the four regexes from `LocalizationValidation.cs:22-25` over all 22,120 `en-US` keys classifies every one of them and leaves nothing over: 16,627 Hashed, 3,715 Indexed, 1,656 Single, 122 HashedIndexed.
So the one-dot grammar is not merely what the compiler enforces, it is what the shipped data obeys — no vanilla key has the extra dots the wiki's recommended mod shape has, and a mod matching vanilla's shape is matching a rule with 22,120 worked examples behind it.

**Indexed keys are a vanilla workhorse, which is worth knowing because no mod ships one.**
The `en-US` index-count table declares 260 keys totalling **3,837 variants**, and that total is exactly the number of Indexed plus HashedIndexed entries in the same file — the table accounts for every indexed entry with none left over.
`Chirper` owns 211 of the 260 keys (each a hashed base id such as `Chirper.POLICIES[No Smoking]`, with up to 11 variants), `LifePath` 25, `Assets` 13, and `Common`, `EconomyPanel`, `Loading`, `Progression`, `Properties`, `SelectedInfoPanel` and `StatisticsPanel` the remaining 11.
The largest are `Assets.DISTRICT_NAME` at 1,015 variants and `Assets.CITY_NAME` at 501, then five road-name keys at 210 each — which is the generated-name pool the game draws from, and the reason the `Assets` group carries 12,028 of the file's 22,120 entries against only 30 distinct ids.

**Verdict: the `{NAME:UnitName}` inline format spec is real, and the game uses it exactly once.**
That claim previously rested only on the vendored bundle's substitution code. Sweeping every value in all thirteen locales finds exactly one occurrence, and it is the same key in twelve of them: `Common.ERROR_ACTION[Mute]` is `Mute ({TIME:DurationSeconds})` in English and the equivalent elsewhere, absent only from the incomplete `uk-UA`.
Two things follow. The spec is confirmed from shipped data rather than from a third-party copy of the UI. And the one unit the game itself reaches for through it, `DurationSeconds`, is one of the five that have **no** C# constant in `Game.UI.Unit` — so the frontend-only tail of the unit list is not a toolchain artefact, the game's own strings depend on it.
Everything else is the plain `{UPPER_SNAKE}` form: 420 of the 22,120 `en-US` values carry a placeholder at all, and 421 carry any `{...}` token.

**Two shipped files carry trailing bytes, and the decompile says why.**
`zh-HANS.loc` has 2,506 bytes past the end of its structure and `zh-HANT.loc` has 8,183; the other eleven have none.
`LocalizationCompiler.WriteLocale` opens its output with `FileMode.OpenOrCreate, FileAccess.Write` and never truncates (`LocalizationCompiler.cs:208`), so a build that emits a shorter file over a longer previous one leaves the old tail in place.
Nothing reads it — both counts in the format are explicit, so `LocaleAsset.Load` stops at the declared end — but a parser that treats end-of-file as end-of-data will trip on those two, and any tool writing a `.loc` through the compiler inherits the same defect.

Rots: the locale set and the per-locale entry counts — re-derive by decoding `Cities2_Data/Content/Game/Locale.cok` and `Cities2_Data/StreamingAssets/` in a current install.

### The vanilla localization-key namespaces

**Ruled (2026-08-03, the localization pass; conflicts.md).** The table ships in full — all 75 groups, both count columns — and the recipe for decoding it does not.
The provenance question this table was contested on is gone: it is first-party, taken from the game's own shipped data at a stated version, and it outranks both the decompile's 21-namespace subset and the vendored bundle.
So the reference bakes it as fact, in its own voice, with no hedge about where it came from and no per-row marking.

What the reference does **not** carry is how to decode a `.loc`. That is procedure over the user's install, the reference's subject is writing strings, and a decode recipe in the middle of it would be teaching a maintenance task to a reader who came to name a setting.
The recipe lives at `method-decoding-shipped-locale-data.md`, and shipping it as a script is roadmap work (`docs/ROADMAP.md`, "Extracting the shipped localization dictionaries").
Both count columns ship rather than the ids column alone: a reader who sees `Assets` at 30 ids has no way to guess it carries 12,028 rows, and the gap between the two columns is what tells them a group is hashed or indexed rather than flat, which is the difference between looking a key up and constructing one.

The whole table carries the volatility marker: the group set and both columns move with the game version, and the ticket already rules the namespaces among the claims that rot.

The table below is derived from the shipped `en-US.loc` at 1.6.0f1, decoded as the previous finding describes.
Each row is a **group** — the segment before the first dot, which is what the four identifier regexes call the group (`src/Colossal.Localization/Colossal.Localization/LocalizationValidation.cs:22-25`).
**Ids** counts the distinct `Group.ID` pairs in that group, ignoring the hash and the index, which is the unit the game's own generated TypeScript dictionary counts in (`src/Colossal.Localization/Colossal.Localization/TypeScriptLocalizationCodeGenerator.cs:70-109`).
**Entries** counts the actual rows in the file, so a group whose ids are mostly hashed or indexed has far more entries than ids.

**75 groups, 2,153 ids, 22,120 entries.**

| Group | Ids | Entries | Covers |
| --- | ---: | ---: | --- |
| `Achievements` | 2 | 82 | achievement `TITLE`/`DESCRIPTION`, both hashed by achievement id |
| `AirPollutionInfoPanel` | 1 | 1 | the air-pollution info view's average readout |
| `AnimationCurve` | 2 | 2 | axis labels on a curve editor |
| `Assets` | 30 | 12028 | prefab display names and descriptions, citizen and vehicle name formats, address formats, themes, upgrades, and the indexed generated-name pools |
| `BikesInfoPanel` | 2 | 2 | parked bikes and bike-parking availability |
| `Budget` | 7 | 35 | budget-panel tooltips, including the tax breakdowns |
| `Chirper` | 116 | 341 | every Chirper message, most of them indexed variants |
| `CinematicCamera` | 24 | 24 | the cinematic camera editor |
| `CityInfoPanel` | 14 | 70 | demand factors and their descriptions |
| `Climate` | 1 | 4 | `SEASON`, hashed by season |
| `Common` | 153 | 438 | shared actions, dialog scaffolding, separators, month names, and **every `VALUE_*`, `FRACTION_*` and `BOUNDS_*` unit string** |
| `CompanyInfoPanel` | 3 | 3 | commercial, industrial and office profitability |
| `Content` | 2 | 15 | DLC/content pack name and prerequisite |
| `DefaultTool` | 1 | 15 | `INFOMODE_TOOLTIP` |
| `DisasterInfoPanel` | 3 | 3 | shelter capacity and evacuation |
| `EconomyPanel` | 114 | 373 | the whole economy panel: budget lines, taxation, loans, production |
| `Editor` | 263 | 678 | the map and asset editors, end to end |
| `EditorTutorials` | 3 | 143 | editor tutorial scaffolding |
| `EducationInfoPanel` | 7 | 20 | education availability and distribution |
| `ElectricityInfoPanel` | 8 | 8 | electricity availability, trade, battery charge |
| `EventJournal` | 4 | 20 | event journal entries and effects |
| `FireAndRescueInfoPanel` | 1 | 1 | average fire hazard |
| `GameListScreen` | 39 | 70 | the save-game list and its city summary fields |
| `GarbageInfoPanel` | 4 | 4 | garbage rate, landfill availability, processing |
| `Glossary` | 8 | 521 | the in-game glossary: 48 categories, 229 sections with a title and a body each, 11 tabs |
| `GroundPollutionInfoPanel` | 1 | 1 | average ground pollution |
| `HealthcareInfoPanel` | 10 | 10 | health, cemetery and crematorium availability |
| `InfoPanels` | 6 | 6 | labels shared across info panels — capacity, consumption, output, processing, production, stored |
| `Infoviews` | 67 | 430 | info view names and their legend tooltips |
| `ISO` | 1 | 249 | `COUNTRY`, hashed by ISO code |
| `LandValueInfoPanel` | 1 | 1 | average land value |
| `LevelInfoPanel` | 9 | 9 | building level names per zone kind |
| `LifePath` | 33 | 130 | the citizen life-path panel's event descriptions |
| `Loading` | 2 | 39 | loading screen title and hint messages |
| `Main` | 69 | 69 | the main toolbar and its per-button tooltips |
| `Maps` | 4 | 49 | map titles, descriptions, outside connections |
| `MapTilePurchase` | 17 | 44 | the tile-purchase panel's resource summary |
| `Menu` | 98 | 185 | main menu, achievements warnings, asset upload, notifications |
| `NaturalResourcesInfoPanel` | 8 | 8 | fertility, ore, oil, fish availability |
| `NoisePollutionInfoPanel` | 1 | 1 | average noise pollution |
| `Notifications` | 2 | 142 | `TITLE`/`DESCRIPTION` for a pushed notification, hashed by its key |
| `Options` | 149 | 1588 | the whole options screen, including the mod-page keys the settings helpers generate |
| `OutsideConnectionsInfoPanel` | 2 | 2 | top imports and exports |
| `Overlay` | 19 | 19 | platform overlay actions and controller-disconnect prompts |
| `Paradox` | 82 | 168 | Paradox account linking, mods UI, playsets |
| `PhotoMode` | 19 | 321 | photo-mode property titles and tooltips |
| `PoliceInfoPanel` | 10 | 10 | crime probability, arrests, success rate |
| `Policy` | 2 | 44 | `TITLE`/`DESCRIPTION`, hashed by policy prefab name |
| `PopulationInfoPanel` | 15 | 15 | age distribution, birth and death rates |
| `PostInfoPanel` | 4 | 4 | mail collected, delivered, rate |
| `Progression` | 74 | 320 | milestones, development trees, unlock panels |
| `Properties` | 68 | 138 | the per-prefab stat rows in the selected-info and tooltip panels |
| `Radio` | 14 | 19 | radio station UI and emergency messages |
| `Resources` | 1 | 41 | `TITLE`, hashed by resource name |
| `RoadsInfoPanel` | 4 | 4 | parking availability and income |
| `SelectedInfoPanel` | 323 | 926 | the largest group by ids: every row of the selected-info panel |
| `Services` | 2 | 32 | `NAME`/`DESCRIPTION` for a service or asset-menu prefab |
| `Statistics` | 2 | 214 | statistics panel title and per-statistic label |
| `StatisticsPanel` | 4 | 441 | statistic titles and time-scale labels |
| `SubServices` | 1 | 64 | `NAME` for an asset-category prefab |
| `Toolbar` | 44 | 44 | the asset menu, theme and asset-pack panels, brush controls |
| `ToolOptions` | 22 | 96 | the tool-options panel's titles and tooltips |
| `Tools` | 33 | 70 | tool tooltips — area size, resource yields, flow and consumption labels |
| `TourismInfoPanel` | 4 | 4 | attractiveness, hotel price, tourism rate |
| `Transport` | 27 | 69 | transport overlay legends and line UI |
| `TransportInfoPanel` | 10 | 22 | passengers, cargo, line counts |
| `Tutorials` | 36 | 1096 | tutorial and advisor scaffolding |
| `UpgradesMenu` | 1 | 1 | `TITLE` |
| `VirtualKeyboard` | 1 | 15 | `TITLE`, hashed by what is being named |
| `WaterInfoPanel` | 8 | 8 | water and sewage treatment and trade |
| `WaterPollutionInfoPanel` | 1 | 1 | average water pollution |
| `WealthInfoPanel` | 13 | 17 | average wealth, income, rent, fees, upkeep and resource cost, with a wealth-tier key |
| `WhatsNew` | 10 | 10 | the what's-new panel's per-release copy |
| `WorkplacesInfoPanel` | 4 | 4 | workplaces, workers, availability |
| `ZoningFactors` | 3 | 19 | zoning factor panel title and positive/negative labels |

Rots: the whole namespace table, its group set and both count columns — re-derive by decoding `en-US.loc` out of `Cities2_Data/Content/Game/Locale.cok` in a current install.

**Verdict: the shipped UI bundle and the shipped locale data agree exactly, and the per-id typing is first-party too.**
The generated `Loc` dictionary in the shipped bundle carries **75 groups and 2,153 ids** (`DecompiledCitiesSkylines2/src-ui/source.js:26620-28937`, registered as `game-ui/common/localization/loc.generated.ts` at `:28971-28978`) — the same 75 groups and the same 2,153 ids the `en-US.loc` decode gives, reached from a different artifact by a different route.
It also carries the two things the `.loc` format cannot encode.
Each id's **identifier shape** is which of four classes it is constructed from, and each class is recognisable by the key template it builds: the bare id, `` `${id}[${hash}]` ``, `` `${id}:${index}` `` and `` `${id}[${hash}]:${index}` `` (`:26557-26610`, the templates at `:26562`, `:26577`, `:26589`, `:26603`). The counts are 1,656 Single, 334 Hashed, 153 Indexed and 10 HashedIndexed.
Each id's **argument names** are that constructor's arguments — `Assets.ADDRESS_NAME_FORMAT` is built with `"NUMBER"` and `"ROAD"` (`:26625`).
The Single count cross-checks against the decode exactly: 1,656 Single ids against 1,656 Single entries, which is what a shape carrying one entry per id has to give.
So the per-id typing and argument names are first-party at a stated version.

**Verdict: the vendored bundle's list was accurate and stale, a build or more behind.**
It lists 72 groups and 2,013 ids (`CS2-Platter/Platter/UI/tools/source.js:25770-27958`), short by three whole groups — `BikesInfoPanel` (2 ids), `Glossary` (8) and `WealthInfoPanel` (13) — and by 117 further ids spread across fifteen groups, the largest being `Options` (+29), `SelectedInfoPanel` (+22), `Paradox` (+17), `Tutorials` (+10), `Chirper` (+7) and `Common` (+7).
The divergence runs one way only, which is what a copy taken from an older build looks like and not what a wrong copy looks like — and it is the same one-way staleness behind the two formatter divergences this pass found.

**Verdict: all twenty-one namespaces the decompiled C# names as string literals exist in the shipped data.**
`Editor`, `Common`, `Options`, `Properties`, `Paradox`, `Assets`, `Tools`, `Menu`, `PhotoMode`, `DefaultTool`, `SelectedInfoPanel`, `Services`, `Maps`, `Infoviews`, `Policy`, `GameListScreen`, `SubServices`, `StatisticsPanel`, `Radio`, `Notifications` and `Loading` all appear, none of them misspelled or renamed.
They are 21 of 75, so **72% of the vanilla namespaces are invisible from C# and built entirely in the frontend** — which is why the decompile alone could never have produced this table.
Representative C# anchors: `src/Game/Game.UI/LocaleIds.cs:5-13` holds five format constants (`Assets.NAME[{0}]`, `Policy.TITLE[{0}]`, `StatisticsPanel.STAT_TITLE[{0}]`, `Maps.MAP_TITLE[{0}]`, `Maps.MAP_DESCRIPTION[{0}]`), and `src/Game/Game.UI/NameSystem.cs:433-491` holds the citizen, train and address name keys.

**Verdict: the wiki's namespace table is real, correct as far as it goes, and misses 29 of the 75 groups.**
`Localize your mod` (https://cs2.paradoxwikis.com/Localize_your_mod, fetched 2026-08-03) names 46 distinct namespaces across roughly 50 rows, and **every one of the 46 exists** — nothing on that page is invented or renamed.
What it does is collapse and omit. One row, described as "Various translations for various info panels", lists 23 namespaces as bare `X.*` with no detail at all: sixteen `*InfoPanel` groups plus `EconomyPanel`, `InfoPanels`, `CinematicCamera`, `Infoviews`, `LifePath`, `PhotoMode` and `Transport`.
It also writes `Climate.SEASON[Season]` as if the namespace were the whole key, when `Climate` is the group and `SEASON` its only id.
The 29 groups it never names are `AirPollutionInfoPanel`, `AnimationCurve`, `BikesInfoPanel`, `Content`, `DefaultTool`, `Editor`, `EditorTutorials`, `EventJournal`, `FireAndRescueInfoPanel`, `Glossary`, `ISO`, `LandValueInfoPanel`, `Loading`, `Main`, `MapTilePurchase`, `NoisePollutionInfoPanel`, `OutsideConnectionsInfoPanel`, `Overlay`, `Paradox`, `Radio`, `Resources`, `RoadsInfoPanel`, `TourismInfoPanel`, `Tutorials`, `UpgradesMenu`, `VirtualKeyboard`, `WealthInfoPanel`, `WhatsNew` and `ZoningFactors` — nine of them `*InfoPanel` groups, and `Editor` the single largest group in the game at 263 ids.
Where it beats the shipped data is per-key prose: its rows explain what `Assets.STREET_NAME:0` or `Budget.TOOLTIP_TITLE_TAX[ZoneType]` is _for_, which no count can.

**What the corpus actually reuses.** Sweeping all 20 repositories for vanilla-prefixed keys, in C#, TypeScript and translation JSON, gives a short list and it is not the same list as the table:

- `Assets.NAME[<prefab name>]` — 1,077 occurrences, and `Assets.DESCRIPTION` 375, overwhelmingly from mods that create prefabs.
- `Options.*` — the settings-page keys the helpers generate, 4,602 `Options.OPTION` alone.
- `Common.ACTION[<map>/<action>]` — 452, from `GetBindingKeyHintLocaleID` and from mods writing input-hint labels by hand.
- `SelectedInfoPanel.*`, `Toolbar.*`, `ToolOptions.*`, `Editor.TOOL`, `Services.NAME`/`DESCRIPTION`, `SubServices.NAME`, `PhotoMode.PROPERTY_TITLE` — tens each, from mods extending the corresponding panel.
- `Tooltip.LABEL[...]` — 1,706 occurrences and **not a vanilla group at all**. It comes from the shared `LocaleHelper.GetTooltip` helper, which builds `Tooltip.LABEL[<ModId>.<key>]` (`RoadBuilder-CSII/RoadBuilder/Utilities/LocaleHelper.cs:62-65`, copied into FindIt, InfoLoom and Node Controller). Four mods share an invented namespace and stay out of each other's way only because the mod id is inside the brackets.

**The one reuse that is a mechanism rather than a convention.** A mod that registers a prefab gets its display name and description looked up by `PrefabUISystem.GetTitleAndDescription`, which picks the key pair from the prefab's type and components (`src/Game/Game.UI.InGame/PrefabUISystem.cs:1498-1527`):

- `UIAssetMenuPrefab` or `ServicePrefab` → `Services.NAME[<name>]` / `Services.DESCRIPTION[<name>]`
- `UIAssetCategoryPrefab` → `SubServices.NAME[<name>]` / `Assets.SUB_SERVICE_DESCRIPTION[<name>]`
- carries `Game.Prefabs.ServiceUpgrade` → `Assets.UPGRADE_NAME[<name>]` / `Assets.UPGRADE_DESCRIPTION[<name>]`
- anything else → `Assets.NAME[<name>]` / `Assets.DESCRIPTION[<name>]`
- unresolvable prefab → the obsolete identifier's name and `Assets.MISSING_PREFAB_DESCRIPTION`

So naming a prefab is not a choice: the mod ships the key the system will ask for. That is the rule behind Extra Assets Importer's generated `Assets.NAME[...]` entries and Find It's generated prop names.

Rots: the four-way branch in `GetTitleAndDescription` — re-read `src/Game/Game.UI.InGame/PrefabUISystem.cs:1498-1527`.

### Writing a name straight into the live dictionary

`LocalizationDictionary.Add` is public (`LocalizationDictionary.cs:77-88`), and `activeDictionary` is a public property (`LocalizationManager.cs:39`), so a mod can bypass sources entirely.
One repository does: `FindIt-CSII/FindIt/Systems/AutoVehiclePropGeneratorSystem.cs:170` writes `activeDictionary.Add("Assets.NAME[" + name + "]", ...)` for each prop it generates at runtime, after first trying to copy the original's localized name (`:140`); `AutoQuantityPropGeneratorSystem.cs:146` does the copy half only.
The cost is that the entry is **not** in any source, so it does not survive `SetActiveLocale` — which rebuilds the dictionary from the source list (`LocalizationManager.cs:206-234`) — nor `ReloadActiveLocale` nor a bulk asset change. The same mod's own `LocaleHelper` sources are unaffected. This is the one technique here whose failure mode is "works until the player changes language".

### Exporting a key dump for translators

Two different dumps, and the corpus does both.

**Dumping the mod's own English source** is what a Crowdin project needs. The shape is identical in five repositories: instantiate the English `IDictionarySource`, call `ReadEntries(new List<IDictionaryEntryError>(), new Dictionary<string, int>())`, `ToDictionary`, serialize, write.

- `Anarchy/Anarchy/AnarchyMod.cs:98-110`, behind `#if DEBUG`, serializing with `JsonConvert.SerializeObject(..., Formatting.Indented)` — and writing to a **hard-coded absolute developer path**, `C:\Users\TJ\source\repos\Anarchy\...`. `CS2-MoveIt/Code/MoveIt/Mod.cs:60-79` carries it twice over, writing the same JSON to two hard-coded paths under `C:\Users\TJ\source\repos\CS2-MoveIt\`; `Recolor/Recolor/Mod.cs:107-127` and `BetterBulldozer/BetterBulldozer/BetterBulldozerMod.cs:110-143` have the same defect.
- `CS2-Platter/Platter/PlatterMod.cs:361-373` fixes it: `GetThisFilePath([CallerFilePath] string path = null)` (`:381`) recovers the compiling source file's own path, and the export lands at `<that directory>/L10n/lang/en-US.json` regardless of whose machine built it. Gated on `#if IS_DEBUG && EXPORT_EN_US` with the constant set by a dedicated build configuration (`CS2-Platter/Platter/Platter.csproj:6/37-38`).
- `Traffic/Code/Localization.cs:48-67`, behind `#if LOCALIZATION_EXPORT`, resolves the mod's own install directory through `modManager.TryGetExecutableAsset`, writes `Localization/TranslationSource.json` with `Colossal.Json.JSON.Dump`, and then **pushes an in-game notification naming the file it wrote** (`:62-64`) — the only export in the corpus that tells the author it happened.
- `CS2-NetworkTools/NetworkTools.Mod/NetworkToolsMod.cs:83-87` exposes it as an override of a framework hook, driven by a third build configuration named `I18N` (`NetworkTools.Mod/NetworkTools.csproj:21`).

The generic form, independent of the mod's strategy: `Colossal.Json.JSON.Dump(entries)` where entries came from the source's own `ReadEntries`.

**Dumping the whole active dictionary** — vanilla keys included — is what a mod needs to find a key to reuse. Three repositories carry it, all behind a `DUMP_VANILLA_LOCALIZATION` symbol that ships commented out:

```
GameManager.instance.localizationManager.activeDictionary.entries
    .OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value)
```

serialized with `Colossal.Json.JSON.Dump` to `Path.Combine(Application.persistentDataPath, "locale-dictionary.json")` (`Anarchy/Anarchy/AnarchyMod.cs:111-121`, `Recolor/Recolor/Mod.cs:128-138`, and declared-but-unused at `Tree_Controller/Tree_Controller/TreeControllerMod.cs:6`).
`entries` is a lazy projection over the dictionary that drops the fallback flag (`LocalizationDictionary.cs:35-44`), so the dump cannot distinguish a real translation from an `en-US` fallback. Placement matters: run it after the mod's own `AddSource` calls and the dump contains the mod's keys too, run it before and it is vanilla only. This is the wiki's own recipe and it is verified here.

**Verdict on the wiki's export advice.** The page's C# snippet is the same three lines and is correct. Its first recommendation — export from I18n EveryWhere's options tab — could not be verified: that mod is not in the checkout and nothing in the decompile knows about it.

### Diagnosing a key that renders as itself

**Verdict: a missing key renders as the key on the C# path and as nothing on the frontend's own.**
`UILocalizationManager.Translate` sets the data to the looked-up value or, failing that, to the key itself (`src/Game/Game.UI.Localization/UILocalizationManager.cs:19-29`).
The frontend's localization provider inspects exactly that: its `translate` calls the engine's, compares the result to the id it asked for, and treats equality as a miss, returning the caller's fallback or `null` (`DecompiledCitiesSkylines2/src-ui/source.js:67905-67908`; the Show-IDs and Show-Fallback debug modes are two sibling one-line implementations at `:67909-67914`).

Two renderers consume that, and they differ.

- **A localized element sent from C#** renders `translate(id, value) ?? id`, so a miss shows the **id** (`:29680-29693`). This is the path every `LocalizedString`, `LocalizedNumber`, `LocalizedFraction` and `LocalizedBounds` off the wire takes, dispatched on `__Type` at `:29634-29647`.
- **A component built from the generated `Loc` dictionary** renders `translate(id, fallback) || (showIdOnFail && id ? id : "")`, so a miss with no fallback shows the **empty string** unless the call site opted in (`:26509-26522`).

`showIdOnFail` is set at twelve call sites in the whole bundle, and every one of them renders one of five keys: `Common.ACTION` (`:39282`, `:39613`, `:74798`), `Options.OPTION` (`:66242`, `:66397`, `:72665`, `:72702`), `Options.INPUT_MAP` (`:66388`, `:72659`, `:72696`), `Options.SECTION` (`:74355`) and `Options.FORMAT` (`:73265-73271`) — the input hints, the keybinding rows, the rebinding-conflict dialog, the options page section title and the slider's custom format string.
Eleven set it as a JSX prop; the twelfth passes it positionally as the fifth argument to the render helper, which is why a grep for the prop name alone finds one fewer than exists.
All five are keys a `ModSetting` helper generates, so the frontend asks to see the id in precisely the places where a mod is the likely author of the missing string.
The familiar raw `Options.OPTION[...]` on screen is therefore that opt-in and not the general rule, and a mod's own frontend component with a missing key renders blank instead.

The game ships a diagnostic for exactly this, in the developer-mode debug window under a `Localization` tab (`src/Game/Game.Debug/LocalizationDebugUI.cs:18-49`):

- a **Language** dropdown calling `SetActiveLocale` directly (`:66-80`);
- a **Debug Mode** dropdown over `LocalizationBindings.DebugMode { None, Id, Fallback }`, labelled "Show Translations" / "Show IDs" / "Show Fallback" (`:21-26`, `:81-95`, enum at `src/Game/Game.UI.Localization/LocalizationBindings.cs:10-15`). It writes the `l10n`/`debugMode` binding (`LocalizationBindings.cs:29-39/45`), so the whole UI switches to rendering ids, or to rendering the fallback text, without a restart;
- a **Print input bindings and controls** button that logs a ready-to-paste block of `Options.OPTION[...]` and `Options.OPTION_DESCRIPTION[...]` rows, tab-separated, for every rebindable binding (`:96-126`);
- a **Print asset categories** button that writes `category_locale.csv` into `EnvPath.kUserDataPath` (`:127-143`).

Rots: the debug tab's contents — re-read `src/Game/Game.Debug/LocalizationDebugUI.cs:48-145`.

The other diagnostic is the log. `LogManager.GetLogger("Localization")` is the channel (`LocalizationManager.cs:21`), it writes `Added localization source ...` at Debug and the source-import failure at Error (`:343`, `:405`), and the `IDictionarySource.ToString()` override is what makes those lines identify a mod.

### Catalog gaps found

**`Plop the Growables` demonstrates the CSV-plus-packed-keys translation format, and its entry names no localization at all.**
`plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md:152-155` names four techniques, all zoning-side.
Sentence to add: "Translations as embedded per-locale CSV parsed by its own quote-aware reader, with settings keys written as a short packed prefix that the loader expands into the long generated key, so a translator edits a two-column spreadsheet."
Source: `PlopTheGrowables/Code/Localization.cs:25-40` (the format spec), `:43-211` (the parser), `:228-252` (`UnpackOptionsKey` and its four recognised prefixes). The same file ships in `LineTool-CS2/Code/Localization.cs`, differing only in accepting a null settings object at `:137`.

**`Road Builder` demonstrates a locale source computed at read time, and its entry does not say so.**
`mod-catalog.md:180-185` names six techniques, none of them localization.
Sentence to add: "A dictionary source registered once under every supported locale whose entries are generated on each read, so names for roads the player builds at runtime localize without re-registering anything."
Source: `RoadBuilder-CSII/RoadBuilder/Utilities/RoadNameUtil.cs:26-46`, with the reload path that re-reads it at `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs:206-234`. The same repository's `LocaleHelper.cs:16-93` is the four-repository shared helper for one-JSON-per-locale-set packaging.

**`Traffic` demonstrates a mod-private language independent of the game's, and its entry does not say so.**
`mod-catalog.md:134-142` already names nine techniques; the localization one is absent.
Sentence to add: "Its own language dropdown, which registers the chosen translation under whatever locale the game is currently set to and re-applies the swap whenever the player changes the game's language, with a per-file translation-coverage percentage shown in the settings page."
Source: `Traffic/Code/Localization.LocaleManager.cs:179-210` (the swap), `:95-177` (the observer and its re-entrancy flag), `Traffic/Code/Localization.ModLocale.cs:27-69` (loading a loose JSON file and computing coverage), `Traffic/Code/Localization.cs:26-46` (enumerating the files beside the DLL).

**`Extra Assets Importer` demonstrates writing a compiled locale asset, and its entry does not say so.**
`mod-catalog.md:193-198` names six techniques, all asset-pipeline.
Sentence to add: "Turning user-supplied per-locale JSON into a compiled locale asset saved in its own database, which the localization manager then picks up through its asset-changed subscription, with an in-memory source as the fallback path."
Source: `ExtraAssetsImporter/MOD/AssetImporter/Importers/LocalizationImporter.cs:39-67`, against `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/LocaleAsset.cs:94-102/136-165` and the manager's subscription at `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs:56/162-182`.

**`Platter` demonstrates a build-time translation export that survives leaving its author's machine, and its entry does not say so.**
`mod-catalog.md:120-126` names seven techniques, none of them localization.
Sentence to add: "Exporting its English string table to the repository at build time under its own build configuration, locating the destination from the compiler's caller-file-path rather than a hard-coded developer directory."
Source: `CS2-Platter/Platter/PlatterMod.cs:357-381`, gated at `:119-121`, with the configuration at `CS2-Platter/Platter/Platter.csproj:6/37-38`.

**`Find It` demonstrates writing generated names into the live dictionary, and its entry does not say so.**
`mod-catalog.md:233-238` names UI-injection techniques only.
Sentence to add: "Naming prefabs it generates at runtime by copying the original's localized name out of the active dictionary and writing a fallback straight back into it — which is the fast path, and is lost the moment the player changes language."
Source: `FindIt-CSII/FindIt/Systems/AutoVehiclePropGeneratorSystem.cs:140/170` and `AutoQuantityPropGeneratorSystem.cs:146`, against `src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs:206-234`.

**`Recolor` demonstrates player-authored translations, and its entry does not say so.**
`mod-catalog.md:258-265` names selected-info-panel techniques.
Sentence to add: "Letting the player name and translate their own palettes in-game, writing each locale to a JSON file beside the palette and registering it as an in-memory source, guarded by a check that the game supports that locale at all."
Source: `Recolor/Recolor/Systems/Palettes/PalettesUISystem.Localization.cs:321-361`, and the load-time counterpart at `Recolor/Recolor/Systems/Palettes/AddPalettePrefabsSystem.cs:205`.

**Not a gap.** `Network Tools`' entry already names its source generator as the answer to C#-and-frontend drift (`mod-catalog.md:86`), and its `I18N` build configuration is the same export technique Platter's sentence covers, implemented in a framework base class that is not in the checkout (`CS2-NetworkTools/NetworkTools.Mod/NetworkToolsMod.cs:32/87`). `Anarchy`, `Better Bulldozer`, `Move It`, `Tree Controller` and `Water Features` all ship the same embedded-JSON block as each other, so none of them demonstrates it better than the others and no entry is proposed.

## Bridge

**Mechanics this technique serves.** Localization is the last step of anything a mod puts on screen, so only the links with a mechanism behind them are asserted.

- **`simulation-time-and-units`** is the mechanics topic this surface feeds most directly, and the seam is the unit table. Every quantity a mod displays goes through a unit string that decides whether it is converted for a Freedom-units player, and the conversion thresholds are part of the mechanic rather than of the presentation — `Power` divides by 10 for kilowatts (so the C# value is in hundreds of watts), `NetElevation` renders two fraction digits, `Weight` crosses from kilograms to tons at 100 and to kilotons at 1,000,000 (`DecompiledCitiesSkylines2/src-ui/source.js:29136-29269`). The other half is the calendar: a game year is twelve days (`src/Game/Game.Simulation/ClimateSystem.cs:320`), months are zero-based (`src/Game/Game.UI.Menu/MenuUISystem.cs:869`), and the frontend computes the date from the `time` binding group's `timeSettings { ticksPerDay, daysPerYear, epochTicks, epochYear }`, `ticks` and `day` (`src/Game/Game.UI.InGame/TimeUISystem.cs:19-50/84-86`).
- **`prefabs-and-assets`** is where the only mechanical key requirement lives. A prefab a mod registers is looked up by name through `PrefabUISystem.GetTitleAndDescription`'s four-way branch (`src/Game/Game.UI.InGame/PrefabUISystem.cs:1498-1527`), so the prefab's name and its `Services`/`SubServices`/`Assets.UPGRADE_*`/`Assets` key pair are one decision, not two. `LocaleIds.kAssetNameFormat` and its four siblings are the constants for the rest (`src/Game/Game.UI/LocaleIds.cs:5-13`).
- **`city-services-and-coverage`, `economy-and-companies`, `transportation-and-vehicles`, `environment-and-pollution`, `citizens-and-households`, `zoning-buildings-and-land-value`, `roads-and-traffic`, `city-state-and-progression`** each have a namespace of their own in the vanilla table above, which is the practical route from "I changed this mechanic" to "here is the string the panel already shows for it": `Services`/`SubServices`/`Properties`, `EconomyPanel`/`Budget`/`CompanyInfoPanel`, `Transport`/`TransportInfoPanel`/`BikesInfoPanel`, the four `*PollutionInfoPanel` groups plus `NaturalResourcesInfoPanel`, `PopulationInfoPanel`/`LifePath`/`WealthInfoPanel`, `LevelInfoPanel`/`ZoningFactors`/`LandValueInfoPanel`, `RoadsInfoPanel`, and `Progression`.
  Three of those groups — `BikesInfoPanel`, `WealthInfoPanel` and `Glossary` — are invisible from both the decompile and the vendored bundle and became nameable only by decoding the shipped locale data, so a bridge drawn from either source alone would have missed them.
- **`diagnostics`** owns the developer-mode debug window this topic's own diagnostic lives in — the `Localization` tab's Show IDs / Show Fallback modes and the two print buttons (`src/Game/Game.Debug/LocalizationDebugUI.cs:48-145`). Everything else here fails through the log at Debug and Error on the `Localization` logger (`src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs:21/343/405`), which is that reference's material too.

**Sibling techniques.**

- **`settings-and-input`** generates the keys this topic supplies strings for, and the seam is the eleven `ModSetting` helpers (`src/Game/Game.Modding/ModSetting.cs:303-371`). That file's own bridge (`settings-and-input.md:415`) states the split from the other side and it holds: it owns the widget, the page and the action; this owns the string, the source and the file it lives in. Three of its established facts are load-bearing here and were re-verified — the `id` and `name` composition (`ModSetting.cs:36-44`), the widget path formula `prefix + "." + declaringType.Name + "." + propertyName` (`AutomaticSettings.cs:327-339`), and that a property inherited from a shared settings base gets a widget path carrying the base's name while the helper builds one carrying the derived name, so the row renders its raw key.
- **`mod-lifecycle-and-ordering`** decides when a source can be added and who wins a key collision. The locale manager and every shipped locale exist before `OnLoad` (`src/Game/Game.SceneFlow/GameManager.cs:2356-2372`), and because later sources overwrite earlier ones (`LocalizationDictionary.cs:87`), two mods claiming the same key are resolved by mod load order and by nothing else.
- **`mod-compatibility`** inherits that collision directly: a mod that overrides a vanilla key silently changes what every other mod's UI shows, and the only guard anyone uses is `activeDictionary.ContainsID(...)` before adding (`ExtraAssetsImporter/MOD/OldImporters/DecalsImporter.cs:181-182`). The invented `Tooltip.LABEL[...]` namespace shared by four repositories is the same hazard one level down.
- **`binding-layer`, in the UI skill**, carries every `ILocElement` across the wire. `LocalizedString`, `LocalizedNumber<T>`, `LocalizedFraction<T>` and `LocalizedBounds<T>` are `IJsonWritable` structs whose `TypeBegin` names are the strings the frontend switches on — `Game.UI.Localization.LocalizedString` and its three siblings (`src/Game/Game.UI.Localization/LocalizedString.cs:76-86`, `LocalizedNumber.cs:29`, `LocalizedFraction.cs:29`, `LocalizedBounds.cs:29`). The far end of that wire is first-party too, and this is the worked example: the frontend declares the same four strings as an enum and dispatches an incoming value on them (`DecompiledCitiesSkylines2/src-ui/source.js:29431-29436`, the dispatch at `:29634-29647`), matching the C# `TypeBegin` names exactly. The localized-element dispatch is an exact `switch` on `__Type` against those four constants, falling through to a literal `<INVALID TYPE>` (`:29634-29647`), so a generic `FullName` carrying trailing type arguments does not resolve; the prefix regex at `:29983-29989` is `isBindingType`, applied to the `names.*` union and never to a localized element.
  The `l10n` binding group itself — `locales`, `debugMode`, `activeDictionaryChanged`, `indexCounts` and the `selectLocale` trigger — is that reference's material, and both ends agree: five members on the C# side (`src/Game/Game.UI.Localization/LocalizationBindings.cs:41-51`) and the same five subscribed on the frontend, with `debugMode` defaulting to `None` there (`source.js:29410-29424`).
- **`frontend-and-injection`, in the UI skill**, owns the module registry, and this topic has three concrete errands for it — plus the file itself, since the shipped bundle this pass read is that reference's whole subject. The `Loc` generated dictionary is registered as `game-ui/common/localization/loc.generated.ts` (`DecompiledCitiesSkylines2/src-ui/source.js:28971-28978`), which is how a mod reaches a vanilla key as a typed component rather than as a string literal. The time formatters that `cs2/l10n` does not export live at `game-ui/common/localization/localized-date.tsx` and `.../localized-number.tsx` (`:29896-29945`, `:29336-29379`), which is the answer to the one formatting gap the public module leaves. And the three unit-preference enums the public module also withholds are registered at `game-ui/menu/data-binding/options-bindings.ts` (`:26110`, `:26345-26362`).
  The whole localization surface is nineteen registry entries under `game-ui/common/localization/` (`:26450`-`:30128`, plus the input-path one at `:34415` and the provider at `:67916`).
- **`custom-tools`** is where the reused tooltip namespaces land: `ToolOptions.*` and `Toolbar.*` are the groups a mod's tool-options row and toolbar entry are looked up in, and the corpus reuses both (32 and 22 occurrences respectively across the 20 repositories).

## Dead ends

- **~~The set of locales the game ships cannot be read from `src/`.~~ Closed by decoding the install.** `GetSupportedLocales()` returns whatever `LocaleAsset`s the database holds (`LocalizationManager.cs:132-140/260-265`), and those are binary `.loc` files outside the decompile — but they are readable from the user's own installed game with a zip reader and a `BinaryReader`, which is what the compiled-locale-data finding above does. It is left recorded because the inference it replaced was wrong in an instructive way: the corpus's most-translated ids are not the game's, so reading a locale set off translation folders gets `nl-NL` for `uk-UA` and adds two locales the game never registers.
- **I18n EveryWhere could not be examined.** Mod 75426 is not in the 20-repository checkout, no corpus mod references its API, and the decompile has no knowledge of it. Everything the wiki says about its export tab, its dependency declaration and its parser is unverified.
- **~~The vendored UI bundle carries no version.~~ Closed by reading the game's own.** `CS2-Platter/Platter/UI/tools/source.js` is a beautified copy of the game's compiled UI with no provenance note in that repository and no version string found in it, and every frontend claim here once rested on it. The game ships the same file plain on disk at `Cities2_Data/Content/Game/UI/index.js`, first-party and at the installed build, so the question of the copy's age stopped mattering: each claim was re-derived from the shipped bundle in the 2026-08-03 pass, and the copy is now cited only where the two disagree.
  It is left recorded for the rule it leaves. All three divergences ran one way — two units missing a formatter entry, one missing from the fraction table, three groups and 117 ids missing from the generated dictionary — which is what an older copy looks like and not what a wrong one does. So a vendored artifact that disagrees with the install is stale until something shows otherwise, and there is no reason to take a frontend fact from a mod repository while the game is installed.
- **`Colossal.Localization`'s CSV sources are unreachable from a mod in practice, and were checked.** `CSVFileSource` is abstract with two concrete subclasses — `HeaderCsvFileSource`, which finds its columns by the header names `"ID"` and `"Translated Text"` on a configurable header row, and `IndexedCSVFileSource`, which takes column and row indices (`CSVFileSource.cs:8-88`, `HeaderCsvFileSource.cs:6-26`, `IndexedCSVFileSource.cs:5-18`). Both are public and both read a file path, so a mod _could_ use them; none does, and the two algernon mods that ship CSV wrote their own parser instead. The default delimiter is a tab, not a comma (`CSVFileSource.cs:12`).
- **`LocalizationCompiler` has no mod-facing entry point.** It reads a directory tree of `<localeId>/*.csv` plus a `#System.csv` header file naming the system language and localized name, validates, strips every key starting with `old.`, and writes a `.loc` (`LocalizationCompiler.cs:34/117-156/206-227`). It also injects an `Options.LANGUAGE[<localeId>]` entry per locale, taking its value from that locale's `Common.LANGUAGE_NAME` (`:184-204`). Nothing constructs it inside `src/Game/`, so it is a build-pipeline tool; it is worth knowing only because it is the one path that _does_ enforce the identifier grammar.
- **`TypeScriptLocalizationCodeGenerator` has no mod-facing entry point either.** It is the tool that produced `loc.generated.ts` (`TypeScriptLocalizationCodeGenerator.cs:70-109`) and nothing in `src/Game/` calls it, so a mod cannot generate a typed dictionary for its own keys this way.
- **`ToUpper()` versus `ToUpperInvariant()` in the enum key builders is inert.** `ModSetting.GetEnumValueLocaleID<T>` uses the culture-sensitive form (`ModSetting.cs:335`) and `AutomaticSettings.GetEnumValues` the invariant one (`AutomaticSettings.cs:908`); they can only disagree for a type name containing `i` under a Turkish or Azeri culture, and the game pins `CultureInfo.CurrentCulture` to invariant at boot (`src/Game/Game.SceneFlow/GameManager.cs:536`). Checked and closed.
- **No corpus mod ships an indexed key.** Sweeping all 20 repositories for `:0`-suffixed identifiers and for `RandomLocalizationIndex` returns nothing, so the random-variant mechanism has no worked mod example. It is not short of vanilla ones: the shipped `en-US.loc` declares 260 indexed keys totalling 3,837 variants, which is the whole of the mechanism's evidence base and is first-party.
- **Network Tools' loading path is not readable.** Its mod class derives from `LucaModBase<T>`, which is not in the checkout (`CS2-NetworkTools/NetworkTools.Mod/NetworkToolsMod.cs:32`), so how its `L10n/lang/*.json` files reach the manager cannot be established — only that the files exist, that Crowdin is wired to them (`CS2-NetworkTools/crowdin.yaml:1-3`), and that an `I18N` build configuration regenerates the English one. The same limitation applies to Move It's `QCommonLib` and Write Everywhere's `BasicIMod`, as `settings-and-input.md:36` and `mod-lifecycle-and-ordering.md:458` already record.
- **Crowdin is a publishing convention with one game-side hook.** 22 `PublishConfiguration.xml` files across the corpus carry `<ExternalLink Type="crowdin" Url="..." />`, and the toolchain's own comment enumerates the accepted link types — discord, github, youtube, twitch, x, paypal, patreon, buymeacoffee, kofi, crowdin, gitlab, gofundme (`CS2-NetworkTools/NetworkTools.Mod/Properties/PublishConfiguration.xml:95`, `NodeController/NodeController/Properties/PublishConfiguration.xml:64`). Nothing else about translator recruitment is verifiable from these sources; the wiki's Discord channel and its GitHub list of mods seeking translators were not checked.
