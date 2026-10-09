# A city stats export that reports without stopping the game

## Kind

bug report

## Prompt

I'm working on a Cities: Skylines II mod that appends one line of city stats (population, money, average happiness) to a CSV file each time a city finishes loading, and puts an entry in the notification corner saying which city was exported and where. The entry is meant to stay up and update on each export. The code is in this workspace.

Three things are off. When the CSV is open in Excel the write fails, which is fine, but the game throws up an error window and pauses the simulation; I only want that noted in the log and the game left alone. Second, I can't find a log file for the mod under Logs/ like other mods have; the only place my lines show up is Player.log. And after I load a second city in the same session the notification entry still names the first city, even though the rest of it updates.

## Checklist

### The mod logs through a logger of its own

- Falls in: the mod's lines are written through `LogManager.Default`, through a logger fetched under the name `Default` or a name the game already uses (`SceneFlow`, `Modding`, `Simulation`, `Rendering`), or through `UnityEngine.Debug.Log`, `Debug.LogWarning`, `Debug.LogError` or `Debug.LogException`.
- Avoids: an `ILog` from `LogManager.GetLogger(...)` named after the mod's own assembly or display name, with every line the mod writes going through it.
- Reference: cs2-modding/references/technique/diagnostics/diagnostics.md
- Source: src/Colossal.Logging/Colossal.Logging/UnityLogger.cs:300

### A failed write does not stop the player's game

- Falls in: the write failure is logged at `Error` or `Critical` through a logger whose `showsErrorsInUI` is never set (left at its default), or through `UnityEngine.Debug.LogError` or `Debug.LogException`.
- Avoids: the mod's logger has `SetShowsErrorsInUI(false)` (or `showsErrorsInUI = false`) applied, or the failure is logged at `Warn` or below.
- Reference: cs2-modding/references/technique/diagnostics/diagnostics.md
- Source: src/Game/Game.UI/ErrorDialogManager.cs:191

### Literal notification text is passed as a value, not as a key

- Falls in: a plain or interpolated C# string is handed to the notification's `title:` or `text:` parameter (or to a dialog's `LocalizedString` field), relying on the implicit conversion from `string`.
- Avoids: the text is wrapped in `LocalizedString.Value(...)`; a `LocalizedString.Id(...)` naming a key the mod registers with the localization system also avoids it.
- Reference: cs2-modding/references/technique/diagnostics/diagnostics.md
- Source: src/Game/Game.UI.Localization/LocalizedString.cs:126

### The per-city part of the notification is not its title

- Falls in: the same identifier is pushed on every export with the city name, a count or a path interpolated into `title:`.
- Avoids: `title:` carries fixed text and the per-city part sits in `text:`; or the entry is removed with `NotificationSystem.Pop` (default delay) before the new push; or the identifier varies per city.
- Reference: cs2-modding/references/technique/diagnostics/reporting-to-the-player.md
- Source: src/Game/Game.UI.Menu/NotificationUISystem.cs:416

## Roots

- decompile
