# Translatable population milestone notifications

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod that celebrates population milestones. When the city's population crosses 1,000, 5,000, 10,000, 50,000, 100,000 and so on, I want a notification to pop up in the game saying the city has reached that many citizens.

I'd like the mod's text to be translatable by the community without me rebuilding anything: a translator should be able to drop a file for their language into a folder next to the mod's DLL. And whenever I load a save, the mod should pick up whatever files are in that folder at that moment, so a translator can edit their file, load a city again and see the result.

## Checklist

### A translation file for a language the game does not ship is not registered blind

- Falls in: the loader takes a locale id from each file's name and hands it to `AddSource` with no test that the game knows that locale and no `AddLocale` for it, so a file for an unshipped language is silently dropped.
- Avoids: the loader tests the locale against `SupportsLocale` or `GetSupportedLocales()` before `AddSource` and logs or skips the files the game has no locale for, or introduces the locale with `AddLocale` before adding the source.
- Reference: cs2-modding/references/technique/localization/localization.md
- Source: src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs:340

### The notification's text reaches the UI as a key with substitutions, not as a C# string

- Falls in: a `string` is passed where the notification system expects a `LocalizedString`: the English text itself, an interpolated string, or a translation fetched from the dictionary by hand and formatted with `string.Format`, so the implicit conversion makes a key out of text meant to be shown as written.
- Avoids: `LocalizedString.Id(key)` or `LocalizedString.Id(key, substitutions)` with the population carried as an `ILocElement` such as `LocalizedNumber<int>`, and `LocalizedString.Value(...)` for any text that is already final.
- Reference: cs2-modding/references/technique/localization/localization.md
- Source: src/Game/Game.UI.Localization/LocalizedString.cs:124

### A reload adds a new source instance rather than re-adding the one it removed

- Falls in: on reload the code calls `RemoveSource` and then `AddSource` with the same source instance it registered the first time (refilling the dictionary that instance wraps, or not), or calls `AddSource` again with that instance expecting it to be re-read.
- Avoids: each reload constructs a new source instance for `AddSource`, removing the previous instance first, or keeps one source whose `ReadEntries` re-reads the file and refreshes it through `ReloadActiveLocale()`.
- Reference: cs2-modding/references/technique/localization/localization.md
- Source: src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs:323

### The source names the mod in `ToString()`

- Falls in: the strings are handed to the manager through `MemorySource`, or through a class of the mod's own that implements `IDictionarySource` without overriding `ToString()`, so a failed import logs a bare type name.
- Avoids: a class of the mod's own implementing `IDictionarySource` that overrides `ToString()` with a mod-specific name, such as the mod id and the locale.
- Reference: cs2-modding/references/technique/localization/localization.md
- Source: src/Colossal.Localization/Colossal.Localization/LocalizationManager.cs:405

## Roots

- decompile
