# Diagnostics: finding out why a mod is not working

**Baseline.** Decompiled game version 1.6.0f1.
The installed game was read on 2026-08-05 at `C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II`, reporting `Game version: 1.6.0f1 (419.d6c6) [6216.19404]` and `Unity version: 2022.3.71f1`.
The user data path read on the same date is `C:\Users\Morgan\AppData\LocalLow\Colossal Order\Cities Skylines II`; its logs are from a session that started 2026-08-05 09:38 on a **debug-patched** install running `--developerMode --uiDeveloperMode`.
Mod corpus (22 repositories under `C:\Users\Morgan\Documents\Projets\cs2-third-party-mods\`) read 2026-08-05.
Wiki fetched live 2026-08-05 — the bot challenge did not fire, so `Logging` and `Debugging` are cited from the live pages rather than through `survey-wiki-inventory.md`'s snapshot.
UI bundle citations are to the shipped bundle reformatted with prettier at its defaults, at `DecompiledCitiesSkylines2/src-ui/source.js`, coming to 135,021 lines — confirmed on this machine, and the count is what tells a later reader whether their own copy still agrees with these line numbers.

---

## Findings

### The boundary against `mod-lifecycle-and-ordering`, stated because both files treat the same code

The mod-lifecycle-and-ordering pass owns the **lifecycle mechanism**: which hook throws, whether the system is left enabled, what the mod's `State` becomes, and where `OnDispose` fits.
This reference owns the **diagnosis order and the log surfaces**: which file to open first, what each line proves, and what reaches the player.

Where the two touch, this file re-derived the shared code rather than trusting `mod-lifecycle-and-ordering.md`, and the re-derivation found two things that file states wrongly or not at all.
Both are recorded at the findings below, and the second is the more consequential: **the "silent disable" is not silent to the player.**

### The log directory, and the six files that matter

`LogManager.kDefaultLogPath` is `Application.persistentDataPath + "/Logs"` (`src/Colossal.Logging/Colossal.Logging/LogManager.cs:45`), which resolves on Windows to `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs`.
The static constructor also writes the resolved path to stdout as `Logs at {0}` (`:46`), and that line reaches `Player.log` — so the directory never has to be guessed.
Confirmed on the install: `Player.log` line 33 reads `Logs at C:/Users/Morgan/AppData/LocalLow/Colossal Order/Cities Skylines II/Logs`.

Every logger except `Default` gets its own file, named after the logger: `logPath = kDefaultLogPath + "/" + name + ".log"` (`UnityLogger.cs:297-306`, the `name != "Default"` guard at `:300`).
So the `Default` logger has no `logPath`, `isValid` is false (`:177-187`), `Open()` throws `LogInvalidException` (`:362`) — and `CustomLogHandler` short-circuits it anyway, sending anything logged through `LogManager.Default` straight to Unity's own handler (`CustomLogHandler.cs:134-137`).

The files a diagnosis actually opens, all read from the install on 2026-08-05:

| File | What it is | Why it is opened |
| --- | --- | --- |
| `Logs/SceneFlow.log` | the `SceneFlow` logger, which is also `COSystemBase.baseLog` | boot transcript, launch arguments, versions, and **every system-level exception** |
| `Logs/Modding.log` | the `Modding` logger | the mod loader's own transcript: playset, per-mod load timings, load failures |
| `Logs/<LoggerName>.log` | one per logger, mod loggers included | the mod's own stream, at whatever level it set |
| `Player.log` | Unity's own log, at the user data root, not in `Logs/` | engine boot, the debug-patch signals, and **every `Warn` and above from every logger** |
| `Player-prev.log` | the previous session's `Player.log` | what a process that died last run printed before dying |
| `FallbackSettings.coc` | user data root, plain text | persisted per-logger settings (below) |

`Logs/` on this install held 22 files, including `Cs2TestMod.Mod.log`, `HallOfFame.log`, `FindIt.log` and `Plop the Growables.log`.
Files are never deleted, so the directory accumulates a file per logger that has ever run rather than a file per logger in the current session — four of the 22 had timestamps from earlier sessions.
That is what makes a mod's logger name visible on disk before the game is started, which is the cheapest way to learn what a shipped mod calls its log.

Rots: the log file names, which are the logger names the game happens to use — `SceneFlow` (`src/Colossal.Core/Colossal.Entities/COSystemBase.cs:9`), `Modding` (`src/Game/Game.Modding/ModManager.cs:178`), `FileSystem` and `Default` (`LogManager.cs:11-13`), `UI`, `InputManager`, `Platforms`, `PdxSdk`, `Radio`, `TestScenarios`, `Automation`, `Discord`.

### The severity ladder is eleven names, and the game's own picker shows ten

`Level` declares eleven statics with severities 10,000 down to 0: `Disabled 10000`, `Emergency 9000`, `Fatal 8000`, `Critical 7000`, `Error 6000`, `Warn 5000`, `Info 4000`, `Debug 3000`, `Trace 2000`, `Verbose 1000`, `All 0` (`src/Colossal.Logging/Colossal.Logging/Level.cs:8-50`).
A message is written when `level >= effectivenessLevel` (`UnityLogger.cs:263-270`), so setting `effectivenessLevel = Level.Verbose` lets everything from `Verbose` up through; setting `Level.Disabled` lets nothing through and closes the stream (`:150-161`).

**Verdict: the wiki's list of eleven levels is correct, and the enumerator the game's own UI drives off omits one.**
`Logging` (https://cs2.paradoxwikis.com/Logging, fetched live 2026-08-05) lists eleven levels DISABLED through ALL, which matches the declarations exactly.
But `Level.GetLevels()` yields only ten — `Critical` is absent from the `yield return` sequence (`Level.cs:60-72`), while `GetLevel(int)` and `GetLevel(string)` both resolve it (`:81`, `:99`).
The only consumer of `GetLevels()` is the developer menu's Logs tab, which builds both of its level dropdowns from that list (`src/Game/Game.Debug/LogsDebugUI.cs:22`, the per-logger effectiveness field under the loop at `:23` and the "Show stack trace below levels" field at `:74`).
So a logger sitting at `Critical` shows as index `-1` in that dropdown and `Critical` cannot be selected there.
This matters more than it looks, because `Critical` is the level the game logs a system's own `OnUpdate` exception at (below).

Rots: the `Level` member names and their severity constants — `src/Colossal.Logging/Colossal.Logging/Level.cs:8-50`.

### The global level override, and the typo that silences everything

`--logsEffectiveness=<LEVEL>` is registered in `GameManager.ParseOptions` and calls `LogManager.SetDefaultEffectiveness(Level.GetLevel(option))` (`src/Game/Game.SceneFlow/GameManager.cs:358-361`).
`SetDefaultEffectiveness` writes `defaultEffectiveness` **and loops over every already-created logger**, so it applies retroactively as well as to loggers created later (`LogManager.cs:50-57`).
The flag is parsed after the boot default is set to `Info` (`GameManager.cs:537`) and after the `SceneFlow` logger exists (`:538`), which is why the retroactive loop is needed.

**Three traps ride with it, all provable from the same code.**

1. **`Level.GetLevel(string)` falls back to `Disabled` for anything it does not recognise** (`Level.cs:92-109`, the `_ => Disabled` at `:107`). It upper-cases first (`:94`), so `--logsEffectiveness=debug` works — but `--logsEffectiveness=DEBUGG`, `=verbose ` with a trailing space, or any other typo turns **every log in the game off**, with no error and no warning. A run that produced empty log files is a run whose command line should be checked first.
2. **A mod that sets its own level in `OnLoad` overrides the flag for its own logger.** Mod loading runs at `GameManager.cs:618`, long after `ParseOptions` at `:540`, and eight of twenty-two corpus repositories set their own level there (below). So `--logsEffectiveness=DEBUG` is not a way to get a shipped mod's debug lines.
3. The flag is spelled with `=`, so `--logsEffectiveness DEBUG` as two arguments is not the same thing. `Mono.Options` handles both forms for a `name=` option, but the wiki writes only the joined one (https://cs2.paradoxwikis.com/Logging).

Unconfirmed: whether the separated form works. `Mono.Options`' `OptionSet` is in the decompile (`src/Colossal.Core/Mono.Options/OptionSet.cs`) and would settle it by reading `Parse`; this pass did not read that method for the value-separation rule, having read it only for the prefix rule quoted in `conflicts.md`'s launch-flag entry.

### `--duplicateLogToDefault` is parsed and never read

`GameManager.Configuration` declares `duplicateLogToDefault` (`GameManager.cs:93`) and `ParseOptions` sets it (`:362-365`).
A grep of all of `src/` for the identifier returns exactly those two sites and nothing else — no consumer anywhere.
The mechanism it names does **not** exist in the copy-both form the name promises. `ILog.redirectToDefault` makes even `Info`-level lines go to Unity's handler (`CustomLogHandler.cs:76-87`) — and then `if (log.redirectToDefault && !LogManager.stdOutActive) { return; }` at `:88-90` returns **before** either `Internal_WriteStream` call (`:96`, `:100`), so the logger's own `<Name>.log` gets nothing. `stdOutActive` is false unless `--captureStdout` was passed (`GameManager.cs:2030-2031`). So it is a redirect, not a duplicate.
The citation range in the first draft of this finding was `:76-87`, ending exactly one line before the guard that overturns it — `docs/solutions/decompile-read-stopped-at-the-confirming-line.md`.
`ILog.SetRedirectToDefault` sets it (`src/Colossal.Logging/Colossal.Logging/ILog.cs:101-105`) and nothing in the game calls that setter.
Unconfirmed: whether the flag can be set from `FallbackSettings.coc` without touching the mod. It is public and settable and appears in `UnityLogger.Copy()` (`:249-260`), which is the shape `AssetDatabase.LoadSettings` deserializes into (`AssetDatabase.cs:613`), so the route is plausible — but no install carries the key and nothing was run to confirm it takes.

So the flag is dead at 1.6.0f1. Worth stating because its name promises exactly the thing a reader wants ("put my mod's lines in `Player.log` too") and it does not deliver it.

Rots: that this flag is inert — re-grep `duplicateLogToDefault` over `src/`.

### Where a log line actually goes, and the rule is the level

`ILog.Info(...)` and friends call `unityLogger.LogFormat(...)` with the log, the exception and the level packed into the `args` array (`UnityLogger.cs:427-435`), and `CustomLogHandler` — which has replaced Unity's own handler since its constructor ran (`CustomLogHandler.cs:26-35`) — unpacks them (`:128-133`).

`PostProcessFormat` then decides the destinations (`:74-101`):

- **The logger's own `<Name>.log` always.** `log.Internal_WriteStream(...)` at `:96` or `:100`.
- **Unity's own handler, and therefore `Player.log`, only when `logType != LogType.Log`** (`:77`). `ConvertLevel` maps `Verbose`..below-`Warn` to `LogType.Log`, `Warn`..below-`Error` to `Warning`, and `Error`..below-`Disabled` to `Error` (`UnityLogger.cs:410-425`). **So `Warn` and above land in `Player.log` as well, and `Info` and below do not.**
- **stdout, only when the game was started with `--captureStdout=console|capture|redirect`** (`GameManager.cs:427-430`, `:2022-2037`), which sets `LogManager.stdOutActive` and `colorOutputEnabled` (`:2030-2031`) and is what `GetStdStream` consults (`CustomLogHandler.cs:56-72`).

The `Player.log` half is confirmed empirically on the install: `Player.log` carries `[UI] [WARN]  …` and `[SceneFlow] [WARN]  …` lines and no `[INFO]` lines from any logger.
The prefix is the logger name, written by `PostProcessFormat` as `"[{0}] {1}"` (`:85`).

**This is the single most useful fact in the whole file for a first pass.** A mod that logs a problem at `Warn` or `Error` has already put it in `Player.log`, so one file answers "did anything go wrong anywhere" across the game and every mod at once.

The `<Name>.log` line format is `[yyyy-MM-dd HH:mm:ss,fff] [LEVEL]<indent>message`, assembled at `UnityLogger.cs:319-329` with the level tag coming from the `"[{0}]{2}{1}"` format at `:429`.
The indent is not zero by default: `Indent`'s constructor sets `indent = 1` (`src/Colossal.Logging/Colossal/Indent.cs:49-52`), so every line carries a leading `"  "`, which is why the log reads `[INFO]  Modding runtime: Builtin` with two spaces.

The file is **opened and closed around every single message** unless `keepStreamOpen` is set: `Internal_WriteStream` opens if closed, flushes, and closes again (`:312-315`, `:342-346`), and `keepStreamOpen` defaults false and is never set by the game or by any corpus mod.
The first open of a session uses `FileMode.Create` and every later one `FileMode.Append` (`:375`, the flag flipped at `:381`), so **a log file is truncated at the session's first message**, not appended across runs. There is no rotation and no previous copy, which is what makes `Player-prev.log` the only surviving record of a session that died.

Rots: the timestamp format and the `[LEVEL]` tag shape — `UnityLogger.cs:319-329` and `:429`.

### An `Error` from a mod's logger is a modal dialog and a paused simulation

This is the fact every other user-facing behaviour in this file follows from, and it is the reason twelve of the eighteen corpus mods that create a logger write `SetShowsErrorsInUI(false)` on the same line.

`ErrorDialogManager` subscribes to two static `UnityLogger` events in its constructor (`src/Game/Game.UI/ErrorDialogManager.cs:95-99`):

- `OnException` → `OnException(...)` (`:165-186`), raised by `CustomLogHandler.LogException` (`CustomLogHandler.cs:176-187`).
- `OnWarnOrHigher` → `OnWarnOrHigher(...)` (`:188-208`), raised for any message at `Warn` or above (`CustomLogHandler.cs:160-163`).

The gate in `OnWarnOrHigher` is `m_Enabled && (log == null || log.showsErrorsInUI) && level >= Level.Error` (`:190`).
Three consequences, each load-bearing:

- **Despite the event's name, a `Warn` never produces a dialog** — `level >= Level.Error` excludes it. The `Severity.Warning` branch at `:195` is therefore unreachable through this path and only fires for a dialog pushed directly.
- **`showsErrorsInUI` defaults to `true` on every new logger** (`UnityLogger.cs:288`). A mod that calls `LogManager.GetLogger(name)` and stops there has opted **in** to the dialog.
- **A `null` log passes the gate**, which is what makes `UnityEngine.Debug.LogError` unsilenceable (next finding).

What the player then gets: `EnqueueOrUpdate` calls `HandlePause()` (`:561`), which caches `SimulationSystem.selectedSpeed` and sets it to `0` (`:259-270`); the speed is restored only when the queue empties (`:272-282`).
The dialog itself is bound as `app.currentError` (`:103`) and rendered by `game-ui/common/panel/dialog/error-dialog.tsx` — the error icon, the title or the fallback `Common.ERROR_DIALOG_TITLE`, a repeat-count badge when `count > 1`, the message, a scrollable details pane with a copy button, and one button per action (`DecompiledCitiesSkylines2/src-ui/source.js:67940-68065`, the binding read at `:68086`).
Default actions are `Continue | Quit`, and in a loaded game or the editor `SaveAndQuit` is added (`ErrorDialog.cs:294`, `ErrorDialogManager.cs:388-411`).

Repeats are merged rather than stacked. A `Fingerprint` of `(exception type, message, details, identifier)` keys a `FingerprintState` (`:106-115`, `:544-597`), a burst detector over 1-second bins across a 6-second horizon marks a fingerprint as spam (`:60-72`, `:471-542`), and once it is, a `Mute` action appears whose cooldown comes from `SharedSettings.instance.userInterface.errorMuteCooldownSeconds` (`:457-469`, `:284-304`).

**Verdict: this corrects `mod-lifecycle-and-ordering.md`'s claim that a lifecycle hook throwing has "no user-visible symptom".**
That file's table (`mod-lifecycle-and-ordering.md:344-349`) records `OnWorldReady` / `OnGamePreload` / `OnGameLoaded` as producing "one log line, no user-visible symptom".
The log call is `COSystemBase.baseLog.Error(exception, ...)` in all five wrappers (`src/Game/Game/GameSystemBase.cs:41`, `:68`, `:80`, `:93`, `:106`), `baseLog` is `LogManager.GetLogger("SceneFlow")` (`COSystemBase.cs:9`), and the install's `FallbackSettings.coc` carries no `SceneFlow Logger` entry, so it holds the `true` default (`UnityLogger.cs:288`).

Verdict: **the flag is written elsewhere, and a grep for the setter method misses it.** `SetShowsErrorsInUI` has no call on this logger, but plain property assignment does the same job at `src/Game/Game/UpdateSystem.cs:193` and `:241` (the editor suppression, below), at `src/Game/Game.Prefabs/PrefabInitializeSystem.cs:131`/`:181`/`:205`, at `src/Game/Game.Prefabs/PrefabSystem.cs:813`, and from the developer menu at `src/Game/Game.Debug/LogsDebugUI.cs:48`.
None of these touch the five hook wrappers, so the claim about them stands — but it stands on the assignment sweep rather than on the setter grep, which is `docs/solutions/empty-grep-read-as-proof-of-absence.md` exactly.
`ErrorDialogManager` is constructed at `GameManager.cs:579`, before `CreateWorld()` at `:591` and long before mods load at `:618`, and nothing in `src/Game/` ever sets its `enabled` to false.
So a hook that throws pops a modal error dialog carrying the system's type name and the stack trace, and pauses the simulation.

Verdict: the dialog appears, corroborated against the running game. The static chain above derives it, and the maintainer confirmed on 2026-08-05 that it is a behaviour they have seen in play — the running game being the source `docs/SOURCES.md` makes authoritative for what the game actually does. So the claim ships flat in both `diagnostics` and `mod-lifecycle-and-ordering` with no evidence marker.

### `UnityEngine.Debug.LogError` and `LogException` cannot be silenced, and four mods use them as the safe fallback

When a message reaches `CustomLogHandler.LogFormat` **without** the three-element `args` array that `ILog` packs, there is no `ILog` to consult: `arg` stays null and the message is forwarded to Unity's handler with `level` derived from the `LogType` alone (`CustomLogHandler.cs:154-158`, `ConvertLogType` at `:103-118`).
`OnWarnOrHigher` then fires with `log == null`, which the `ErrorDialogManager` gate treats as permission (`ErrorDialogManager.cs:190`).

- `UnityEngine.Debug.LogError(...)` → `LogType.Error` → `Level.Error` → **dialog, always**.
- `UnityEngine.Debug.LogException(...)` goes through `CustomLogHandler.LogException`, which raises `OnException` (`:181`) — and `ErrorDialogManager.OnException` checks only `m_Enabled` (`:167`). **Dialog, always, with no `showsErrorsInUI` consideration at all.**
- `UnityEngine.Debug.LogWarning(...)` → `Level.Warn` → no dialog, `Player.log` only.
- `UnityEngine.Debug.Log(...)` → `LogType.Log` → no dialog, `Player.log` only, and it reaches no `.log` file.

Four of twenty-two corpus repositories reach for `Debug.LogException` in exactly the place where they want to be quiet — a `catch` around settings or localization registration, where the mod's own logger may not exist yet: `CS2-NetworkTools/NetworkTools.Mod/Settings/Settings.cs:128` and `:142`, `CS2-Platter/Platter/Settings/PlatterModSettings.cs:82` and `:93`, `SceneExplorer/SceneExplorer/Logging.cs:18` and `Settings.cs:90`, `Traffic/Code/Mod.cs:215` and `:236`, `Traffic/Code/ModSettings.cs:129`, `Traffic/Code/Systems/ModCompatibility/TLEDataMigrationSystem.cs:56`.
`Traffic/Code/Mod.cs:173` goes further and uses `Debug.LogError` with a formatted message.
None of them can have intended a modal dialog on the player's screen; the technique is the loudest channel in the game.

One corpus mod also logs warnings from inside simulation code that runs per citizen — `Time2Work/NightShift/Systems/Time2WorkCitizenBehaviorSystem.cs:1549` and `Systems/Time2WorkLeisureSystem.cs:1334`/`:1569` — which is `Player.log` volume rather than a dialog, but is the same reflex.

### A system throwing in `OnUpdate` logs at `Critical`, not `Error`

`UpdateSystem.Update` wraps each system's `Update()` and, on an exception, saves `COSystemBase.baseLog.showsErrorsInUI`, sets it false **only when `GameManager.instance.gameMode.IsEditor()`**, logs, and restores it (`src/Game/Game/UpdateSystem.cs:188-197`, identically in the three-argument overload at `:236-245`).

The log call is `baseLog.CriticalFormat(exception, "System update error during {0}->{1}:", phase, systemTypeName)`.
`mod-lifecycle-and-ordering.md:340` records the message and the loop-continues behaviour correctly and does not state the level; `Critical` is 7000 (`Level.cs:14`), above `Error`, so in a game (not editor) session **this pops the error dialog and pauses the simulation, every frame the system throws**, until the spam detector offers `Mute`.
The editor gets a collected list instead: `EditorErrorPanelSystem` subscribes to `UnityLogger.OnErrorOrHigher` while in editor mode and accumulates fingerprinted entries into a binding (`src/Game/Game.UI.Editor/EditorErrorPanelSystem.cs:70-82`, `:88-108`).

Rots: the two message strings and the `IsEditor` suppression condition — `src/Game/Game/UpdateSystem.cs:188-197`.

### The logger name is the log file name and the settings key, and nothing else

The wiki instructs: "Ensure your log name matches both your ModsSettings folder name and your Settings file name" (https://cs2.paradoxwikis.com/Logging).

**Verdict: that is a convention with no mechanism behind it, and the reference should say so rather than repeat it as a requirement.**
The logger name is read in exactly two places: `logPath = kDefaultLogPath + "/" + name + ".log"` (`UnityLogger.cs:302-303`), and the settings asset key `name + " Logger"` (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/AssetDatabase.cs:68-71`).
A grep of all of `src/` for `ModsSettings` returns nothing — that directory is created by mods themselves, not by the game (`HallOfFame/HallOfFame/Mod.cs:71-72` builds it with `Path.Combine(EnvPath.kUserDataPath, "ModsSettings", nameof(HallOfFame))`).
Nothing couples the three names.
The convention is still worth following for the reason the wiki does not give: a user reporting a problem sends the log file, and a file whose name matches the mod's settings folder is the one they can find.

### Per-logger settings persist in `FallbackSettings.coc`, and survive the session

`AssetDatabase.OnPriorityDataCached` installs a settings provider (`AssetDatabase.cs:328`) whose `ApplySettings` does `m_Database.LoadSettings(log.name + " Logger", log, log.Copy())` (`:59-71`).
`UnityLogger.ReadSettings` calls it for every non-`Default` logger (`UnityLogger.cs:297-306`), and `ReadSettings` runs both from `LogManager.AddLogger` — that is, from the first `GetLogger(name)` — and from `LogManager.RefreshSettings()`, which `SetSettingsProvider` triggers for every existing logger (`LogManager.cs:78`, `:88-100`).
`LoadSettings` deserializes a JSON fragment into the live logger object (`AssetDatabase.cs:613`), and with no `FileLocationAttribute` on `UnityLogger` the asset lands in `FallbackSettings.coc` (`:644`).

The install confirms all of it. `FallbackSettings.coc` at the user data root is plain text, 122 blocks of the form:

```
FindIt Logger
{
    "showsErrorsInUI": false
}
```

with `HallOfFame Logger` carrying `{"effectivenessLevel": "ALL"}` and `IBLIV Logger` carrying `{"showsStackTraceAboveLevels": "ERROR"}`.

Two things follow.

- **A logger's settings are durable across launches, and are what the developer menu's Logs tab writes into.** That tab exposes `effectivenessLevel`, `showsErrorsInUI` (`src/Game/Game.Debug/LogsDebugUI.cs:44`), `logStackTrace`, `showsStackTraceAboveLevels` (`:74`) and — only under `--qaDeveloperMode` — `disableBacktrace` (the gate at `:60`), for every logger the game has (`:23`).
- **Mod code still wins at runtime.** `AssetDatabase.CacheAssets(priorityAssets: true)` is awaited at `GameManager.cs:587`, before mods load at `:618`, so the file is applied to a mod's logger at `GetLogger` time and the chained `.SetShowsErrorsInUI(...)` / `effectivenessLevel = ...` that follows overrides it. The file governs the window between the two, and governs entirely for a mod that sets nothing.

This file is where to look when a mod's log level is not what its source says, and it is not on `docs/SOURCES.md` (see the source-list finding below).

Rots: the settings key suffix `" Logger"` and the fallback file name `FallbackSettings.coc` — `AssetDatabase.cs:70` and `:644`.

### The corpus's logging idiom, in full

All 22 repositories were swept.

**Eighteen create a logger through `LogManager.GetLogger`.** The other four reach logging through a framework base class whose source is absent from the checkout — `CS2-MoveIt` (`QCommonLib`), `CS2-NetworkTools` (`LucaModBase<T>`), `CS2-WriteEverywhere` (`BasicIMod`) and `InfoLoom` (`ModsCommonBase<T>`), the same four gaps `mod-lifecycle-and-ordering.md:465` records.

**Twelve call `SetShowsErrorsInUI(false)` unconditionally** — Anarchy (`Anarchy/Anarchy/AnarchyMod.cs:83`), AreaBucket (`AreaBucket/Mod.cs:24`), Platter (`CS2-Platter/Platter/PlatterMod.cs:106-108`), ExtraAssetsImporter (`ExtraAssetsImporter/EAI.cs:27`), ExtraDetailingTools (`ExtraDetailingTools/EDT.cs:30`), FindIt (`FindIt-CSII/FindIt/Mod.cs:33`), NodeController (`NodeController/NodeController/Mod.cs:24-25`), Recolor (`Recolor/Recolor/Mod.cs:82`), SceneExplorer (`SceneExplorer/SceneExplorer/Logging.cs:14`), Time2Work (`Time2Work/NightShift/Mod.cs:29`), Tree Controller (`Tree_Controller/Tree_Controller/TreeControllerMod.cs:72`), Water Features (`Water_Features/Water_Features/WaterFeaturesMod.cs:74`).

**Two deliberately leave it on and one switches on the build.** `RoadBuilder-CSII/RoadBuilder/Mod.cs:25` and `HallOfFame/HallOfFame/Mod.cs:79` pass `true` explicitly; `BetterBulldozer/BetterBulldozer/BetterBulldozerMod.cs:80` passes `true` under `#if DEBUG || VERBOSE` and `false` otherwise (`:82`).

**Three never touch it**, so they ship with the dialog on by default: `LineTool-CS2/Code/Mod.cs:85`, `PlopTheGrowables/Code/Mod.cs:53`, `Traffic/Code/Logger.cs:9`.

**Eight gate the level on build configuration, and seven use the same shape**, `#if VERBOSE / #elif DEBUG / #else` around `effectivenessLevel`: Anarchy (`AnarchyMod.cs:86-90`), Better Bulldozer (`BetterBulldozerMod.cs:86-90`), Recolor (`Mod.cs:84-88`), Tree Controller (`TreeControllerMod.cs:74-78`), Water Features (`WaterFeaturesMod.cs:77-81`), LineTool with two branches (`Mod.cs:88-91`), PlopTheGrowables with one (`PlopTheGrowables/Code/Mod.cs:55-58`, `#if DEBUG` only).
The eighth is the only one using the fluent setters: `CS2-Platter/Platter/PlatterMod.cs:109-113` re-assigns `Log = Log.SetBacktraceEnabled(true).SetEffectiveness(Level.All)` under `#if IS_DEBUG`, and is also the corpus's only `SetBacktraceEnabled` call anywhere.

**Two use `[Conditional]` on the wrapper instead of `#if` at the call site**, which is the better technique and neither explains why: `Traffic/Code/Logger.cs:14-51` declares seven category methods each carrying `[Conditional("DEBUG_TOOL")]`, `[Conditional("DEBUG_CONNECTIONS")]`, `[Conditional("SERIALIZATION")]` and so on; `SceneExplorer/SceneExplorer/Logging.cs:26-38` does the same with three.
The payload is that `[Conditional]` removes the **call site including its argument expressions**, so an interpolated `$"..."` message is never built in a build lacking the symbol — which is what `if (Log.isDebugEnabled)` buys you at runtime and this buys you at compile time.
Note that both route their "debug" categories to `_log.Info`, so `effectivenessLevel` never filters them: the compile symbol is the only filter.

**Ten hold the logger in a `static` field or property initializer on the mod class; eight assign it inside `OnLoad`; none uses an instance field initializer.**
That is not decoration. `ModInfo.Load` builds the mod instance with `FormatterServices.GetUninitializedObject` (`src/Game/Game.Modding/ModManager.cs:121`), so instance field initializers never run — an `ILog` in one would be null at every use. A static initializer is safe because the static constructor still runs on first access.

**Four wrap the logger, in two shapes.** The `[Conditional]` façades above are one; the other two add behaviour — `CS2-Platter/Platter/Utils/PrefixedLogger.cs:13-40` prefixes every message with a per-system tag and is instantiated per system from a shared base, and `HallOfFame/HallOfFame/Logging/ModLog.cs` wraps the show-errors-in-UI flag (below).

**One treats logger creation as fallible.** `SceneExplorer/SceneExplorer/Logging.cs:11-20` puts `LogManager.GetLogger` in a static constructor inside `try/catch`, falling back to `UnityEngine.Debug.LogException`. Nothing in `LogManager.GetLogger` can throw out to the caller — it catches and returns `LogManager.Default` (`LogManager.cs:122-133`) — so the guard is unnecessary, and the fallback it chose is the loudest channel there is.

### The corpus's one worked example of the error dialog as a feature

`HallOfFame` is the only repository that keeps `showsErrorsInUI` on **and builds its user-facing error channel out of it** (`HallOfFame/HallOfFame/Mod.cs:78-81`, which also chains `.SetEffectiveness(Level.All)`).

`ModLog` (`HallOfFame/HallOfFame/Logging/ModLog.cs`) exposes two error shapes that differ only in the localized sentence they prepend — `ErrorRecoverable` (`:42-47`) and `ErrorFatal` (`:49-54`), both ending in `log.Error(exception, message)` — and a `Silently(...)` helper that saves `showsErrorsInUI`, sets it false, logs, and restores it (`:60-68`), backing three `ErrorSilent` overloads (`:33-40`).
So the mod's default is "the player sees this", and quietness is the explicit case.

The same repository reaches the dialog a second way, through reflection, and does not need to. `HallOfFame/HallOfFame/Reflection/ErrorDialogManagerAccessor.cs:18-23` reads the private `AppBindings.m_ErrorDialogManager` field to call `ShowError` (used at `Systems/CommonUISystem.cs:249`, `Systems/SlideshowUISystem.cs:303`, `Systems/Capture/CaptureUISystem.cs:109`, `Reflection/ParadoxConnection.cs:15`).
At 1.6.0f1 `AppBindings.ShowErrorDialog(ErrorDialog)` is public and forwards to exactly that call (`src/Game/Game.UI/AppBindings.cs:370-373`).
The reflection is therefore redundant at this version, and the reference should teach the public wrapper.

Unconfirmed: whether `ShowErrorDialog` existed when that mod was written. The decompile only shows 1.6.0f1, and nothing in this pipeline reaches an older build.

### `ErrorDialog`, built by hand

A mod that wants the dialog without logging an error constructs one and calls `appBindings.ShowErrorDialog(dialog)` (`AppBindings.cs:370-373` → `ErrorDialogManager.ShowError`, `:219-233`, which pauses the simulation the same way).
The public fields are `severity` (`Warning` or `Error`, `ErrorDialog.cs:286-292`), `actions` (default `Continue | Quit`, `:294`), `localizedTitle` (`:296`), `localizedMessage` (`:298`), `errorDetails` (`:301`), plus `count` and `fingerprint` which the manager fills in (`:303`, `:305`, set at `:586-587`).
`ActionBits` is `Continue = 1`, `Ignore = 2`, `Mute = 0x100`, `SaveAndContinue = 0x200`, `SaveAndQuit = 0x400`, `Quit = 0x20000`, `Rename = 0x40000` (`:13-23`); an empty `actions` is normalised to `Continue` (`ErrorDialogManager.cs:588-591`).

Rots: the `ActionBits` values and the `ErrorDialog` field names — `src/Game/Game.UI/ErrorDialog.cs:13-23` and `:286-305`.

### The boot transcript is the diagnosis's first page

`SceneFlow.log`'s opening is a fixed sequence, all of it first-party and all of it useful before any mod-specific question is asked. Read from the install on 2026-08-05:

- **`Command line: …`**, one argument per line, written by `GameManager` at `GameManager.cs:446` through `MaskArguments`. On this install it reads `--developerMode` and `--uiDeveloperMode`, which is how a question like "is developer mode actually on" is answered without asking the user.
- **`GameManager created! (…ms)`** (`:524`), then `Creating ECS world`.
- **The version block**, `log.Info(GetVersionsInfo())` (`:600`, body at `:2083-2106`): `Date`, `Game version: 1.6.0f1 (419.d6c6) [6216.19404] Windows Steamworks`, **`Game configuration: Development (Mono)`**, `COre version`, `Localization version`, `UI version`, `Unity version: 2022.3.71f1`, `Cohtml version: 1.64.0.7`, `ATL Version`, the platform's own versions, then one line per installed DLC and radio pack.
- **The system info block** (`:601`) and **the configuration dump** (`:602`).

`Game configuration` is `UnityEngine.Debug.isDebugBuild ? "Development" : "Release"` (`:2090`) — the debug-patch signal, in a text log, at a known line. See the debug-patch finding below.

`Modding.log` opens with its own fixed sequence (`ModManager.cs` and `GameManager.cs`), all confirmed against the install's copy:

1. `Modding runtime: Builtin` — `s_ModdingRuntime`, written by `ListHarmonyPatches` (`GameManager.cs:2158`). The alternative value names a BepInEx assembly and its version (`DetectModdingRuntimeName`, `:2255-2280`).
2. If code modding is off: `Modding is disabled`, and nothing else ever (`ModManager.cs:246-249`). `ModManager` is constructed with `configuration.disableCodeModding` (`GameManager.cs:605`), which `--disableCodeModding` sets and `--disableModding` sets as a side effect (`:394-402`).
3. `======= Active Playset =======` and `======= Enabled Mods =======`, one indented line per mod as `\t - <displayName> v<userModVersion> (<id>)` (`ModManager.cs:366-395`).
4. `Mods registered in {0}ms` (`:401`).
5. Per mod, in load order: optionally `Loaded additional Burst code <path>` (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ExecutableAsset.cs:256`), then `Loaded <assembly full name> in {0}ms` (`ModManager.cs:445`).
6. `Mods initialized in {0}ms` (`:435`).
7. `Registered UI Module <moduleInfo JSON> from <asset>`, one per UI module (`:469`).

**Verdict: `Loaded <assembly full name> in …ms` proves only that the loader reached the mod, against this file's earlier reading of it as proof that `OnLoad` completed.**
The line is written from the callback of `using (PerformanceCounter.Start(...)) { modInfo2.Load(updateSystem); }` (`:443-449`), and `PerformanceCounter.Dispose()` invokes that callback unconditionally (`src/Colossal.Core/Colossal/PerformanceCounter.cs:47-54`) — so the `using`'s finally emits it on the throwing path and on every early return inside `Load` as well.
A mod whose `OnLoad` threw, one whose dependencies did not resolve, one that lost a duplicate resolution, and one that was never required all produce the same success-shaped line; only the first three are then followed by `Error initializing mod …`.
Corrected 2026-08-05 in the diagnostics pass's review, after the shipped reference had already been fixed — the earlier reading here cited `:443-446` and stopped one line before the `using`'s closing brace.

**Both slots of that error line carry the assembly identity**, not the display name: the call is `log.ErrorFormat(exception, "Error initializing mod {0} ({1})", modInfo2.name, modInfo2.assemblyFullName)` (`:456`), and `ModInfo.name => asset.fullName` resolves to the Cecil full name (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ExecutableAsset.cs:159`).

**A trap the install makes visible.** The `======= Enabled Mods =======` block lists only mods in the Paradox playset. The install's block names ten mods; `Modding.log` then reports `Loaded Cs2TestMod, Version=1.0.0.0…` and `Loaded HallOfFame, Version=2026.2.4.0…`, neither of which is in that block — both are local mods under `%CSII_LOCALMODSPATH%`.
So **a mod's absence from the playset block says nothing about whether it loaded**, and a diagnosis that stops there on a locally-built mod stops at the wrong line.

Rots: every one of these message strings, and the `Modding` / `SceneFlow` logger names.

### The loader's nine states, and what each one says about the assembly

`ModManager.ModInfo.State` is `Unknown, Loaded, Disposed, IsNotModWarning, IsNotUniqueWarning, GeneralError, MissedDependenciesError, LoadAssemblyError, LoadAssemblyReferenceError` (`ModManager.cs:32-43`), assigned in `ModInfo.Load` (`:91-144`). Declaration order is load-bearing: the failure notification fires only for `state >= IsNotModWarning` (`:270`).

| State | Set at | What it says about the assembly |
| --- | --- | --- |
| `Unknown` | never assigned; the initial value | The loader **never tried**. `Load` returns immediately when `state != Unknown` or `!asset.isRequired` (`:95-98`). |
| `Loaded` | `:124` | `OnLoad` ran on every `IMod` implementation in the assembly and returned. |
| `Disposed` | `:172` | `OnDispose` has run — at shutdown (`:495-521`) or because `OnLoad` threw (`:453`). |
| `IsNotModWarning` | `:101`, unreachable | Verdict: **never assigned at 1.6.0f1.** `Load` returns at `!asset.isRequired` (`:95-98`), and `isRequired` is `isMod ? true : isReference` (`ExecutableAsset.cs:161-172`) — the identical condition to the `!isMod && !isReference` guard at `:99` that would set this state. An assembly with no top-level `IMod` that a required mod references is `isReference`, so it passes both guards and ends at `Loaded`. |
| `IsNotUniqueWarning` | `:107` | Another asset with the same assembly **name** won the duplicate resolution. `GetUniqueVersionAsset` orders by `isLoaded` desc, then `isLocal` desc, then `version` desc, then id (`ExecutableAsset.cs:181-191`). **A local build beats a subscribed copy of the same name** — which is the reason a locally-deployed mod overrides the store copy, and the reason a stale local copy silently shadows an updated one. |
| `MissedDependenciesError` | `:111` | At least one assembly reference resolved to null (`canBeLoaded`, `ExecutableAsset.cs:175`). `loadError` is the newline-joined list of unresolved reference names (`:112-114`) and is shown in the dialog. |
| `LoadAssemblyError` | `:128` | `Assembly.Load` over the mod's own bytes threw (`ExecutableAsset.cs:270-274`). Bad IL, a target framework the runtime rejects, a truncated file. |
| `LoadAssemblyReferenceError` | `:134` | Loading one of the mod's **referenced** assemblies threw (`ExecutableAsset.cs:260-268`). The message chain distinguishes a direct reference from a sub-reference. |
| `GeneralError` | `:140` | Anything else out of `Load`, which in practice means **`OnLoad` threw**. `loadError` is the extracted stack trace (`:141`). |

**Three of the four error states rethrow and one does not**, and the difference decides whether anything is logged.
`LoadAssemblyError`, `LoadAssemblyReferenceError` and `GeneralError` all end in `throw;` (`:130`, `:136`, `:142`), so `InitializeMods` catches, calls `modInfo2.Dispose()` — which runs `OnDispose` on every instance — and logs `Error initializing mod {0} ({1})` with the mod name and the assembly full name (`:451-455`).
`MissedDependenciesError` returns instead (`:115`), so **it never reaches that catch: nothing is written to `Modding.log`, `OnDispose` is not called, and the notification and its dialog are the only report.**
The two warning states return the same way (`:102`, `:108`).

Rots: the `State` member names and their order — they are also the interpolated half of the dialog's localization key `Common.DIALOG_MESSAGE_MODDING[{state}]` (`:295`), so a rename moves a key too.

### What the loader never reports at all

Three ways for a mod to be absent with no state, no notification and no log line.

- **The file is not a managed assembly.** `ExecutableAsset.PostCreate` reads the assembly with Cecil and swallows `BadImageFormatException` with an empty catch (`ExecutableAsset.cs:281-292`, the catch at `:290`). `definition` stays null, `isILAssembly` is false (`:137-146`), and `GetModAssets` filters on exactly that (`:314`). A native DLL or a corrupt file is invisible.
- **The mod shipped a copy of a game assembly.** `GetModAssets` resolves each asset against the loaded AppDomain and, if the resolved location sits under `Cities2_Data/Managed`, warns `Assembly "{0}" is in-game assembly and it should NOT be shipped with mod "{1}"` and **`continue`s** — the asset never enters the list (`:327-329`). This one does log, to `Modding.log` at `Warn`, and therefore to `Player.log` as well.
- **The asset is not required.** `isRequired` is `isMod || isReference` (`:163-172`), and `Load` returns at `:95` for anything else, leaving `State.Unknown` and no notification.

### The failure notification, and the dialog behind it

For every mod whose state reached `IsNotModWarning` or worse, `Initialize` pushes a notification keyed by the asset's GUID (`ModManager.cs:267-336`).
`ProgressState` is `Warning` for the two warning states and `Failed` for the four error states (`:278-287`), the title is the mod's display name from its metadata and the thumbnail is its store thumbnail scaled to `NotificationUISystem.width` (`:277`, `:289-290`).

Clicking it opens a `MessageDialog` (`:292-315`):

- Title `Common.DIALOG_TITLE_MODDING[ModLoadingWarning]` or `[ModLoadingError]` (`:294`).
- Message `Common.DIALOG_MESSAGE_MODDING[<State>]` with a `MODNAME` substitution (`:295-299`).
- Where `loadError` is non-null, it becomes the dialog's **details** pane with a copy button, after escaping backslashes and asterisks (`:305-308`).
- For a non-local mod, two extra actions: `[ModPage]` and `[Disable]` (`:300-304`), whose callbacks open the store page or disable the mod in the active playset (`:316-335`).

The callback's integer is positional: `0` is the confirm action, `1` the cancel action, and the `otherActions` array starts at `2` — read off `ModManager`'s own `Callback` (`:316-335`) against `ConfirmationDialogBase`'s constructor order (`src/Game/Game.UI/ConfirmationDialogBase.cs:31-40`).

At the end of the pass a summary notification replaces the progress one: `ModsLoadingDone` with `LOADED` and `TOTAL` counts, or `ModsLoadingDoneZero` (`:337-350`).
If the whole `Initialize` throws, `ModsLoadingAllFailed` is shown instead and the exception goes to `Modding.log` at `Error` (`:352-358`).

**A mod's `OnLoad` stack trace reaches the player nowhere.**
The `Modding` logger is created with `SetShowsErrorsInUI(false)` (`:178`) so it raises no dialog, and `loadError` never reaches the notification either: `GeneralError` rethrows, `InitializeMods`' catch calls `Dispose()` which sets `state = State.Disposed` (`:170-173`, `:453`), and the notification pass at `:264-270` runs afterwards and skips anything below `IsNotModWarning`.
`Modding.log`'s `Error initializing mod …` line is the whole of the record.

### The silent disable, re-derived

`GameSystemBase.OnCreate` subscribes four lifecycle hooks and `Application.focusChanged`, each wrapped (`src/Game/Game/GameSystemBase.cs:17-31`):

| Wrapper | Hook | Message | `Enabled = false`? |
| --- | --- | --- | --- |
| `WorldReady` `:98-109` | `OnWorldReady()` | `"<Type>: Error on game preload, disabling system..."` | yes, `:107` |
| `GamePreload` `:85-96` | `OnGamePreload(Purpose, GameMode)` | `"<Type>: Error on game preload, disabling system..."` | yes, `:94` |
| `GameLoaded` `:72-83` | `OnGameLoaded(Context)` | `"<Type>: Error on game load, disabling system..."` | yes, `:81` |
| `GameLoadingComplete` `:60-70` | `OnGameLoadingComplete(Purpose, GameMode)` | `"<Type>: Error on state change, disabling system..."` | **no** — the line is absent |
| `FocusChanged` `:33-43` | `OnFocusChanged(bool)` | `"<Type>: Error on Focus change"` | no |

Re-derived from the file rather than taken from `mod-lifecycle-and-ordering.md`, and both of that file's two payload observations hold: `OnGameLoadingComplete` says "disabling system..." and does not disable, and `OnWorldReady` and `OnGamePreload` emit the byte-identical message so the log cannot tell which threw.

What this file adds:

- **All five go to `SceneFlow.log`, at `Error`, through `COSystemBase.baseLog`** (`COSystemBase.cs:9`) — not the mod's own logger, which is where its author looks. And because `Error` is above `Warn`, **the same line is in `Player.log`** prefixed `[SceneFlow] [ERROR]`.
- **And it pops the error dialog** — see the dialog finding above. "Silent" describes the mod's own log, not the session.
- The type name in the message is `GetType().Name`, so it is the mod's system's short name and greppable.

Rots: all five message strings — `src/Game/Game/GameSystemBase.cs:33-109`.

### The diagnosis order

Synthesised from the findings above; every step's evidence is cited at its own finding.

1. **Is code modding on at all?** `Modding.log` reading `Modding is disabled` ends the diagnosis. Check `SceneFlow.log`'s `Command line:` block for `--disableModding` / `--disableCodeModding`.
2. **Did the loader reject it?** `Modding.log`, `Error initializing mod …` with the stack trace — covering the three rethrowing states only. An unresolved dependency and a shadowed duplicate return instead, so they reach no log line and are read off the in-game failure notification and the dialog behind it, whose message key names the state.
3. **Did the assembly load, or only get as far as being timed?** `Modding.log`, `Loaded <assembly full name> in …ms`, which is emitted on every path (verdict above) and therefore proves only that the loader reached the mod. Do not use the `======= Enabled Mods =======` block for this — it lists playset mods only and never local ones.
4. **Did a system fail to construct?** A throw from any system's `OnCreate` propagates out of `UpdateAt<T>` and takes the **whole mod** down as `GeneralError` — so this presents exactly as step 3, and the stack trace is what separates them.
5. **Did a lifecycle hook throw?** `SceneFlow.log` (or `Player.log`), `<Type>: Error on game preload|game load|state change, disabling system...`. Three of the five leave the system disabled for the rest of the session.
6. **Is a system throwing every frame?** `SceneFlow.log`, `System update error during <Phase>-><SystemType>:` at `Critical`. The system keeps running and keeps throwing.
7. **Is the system registered but never running?** Nothing logs this. It is the anchoring failure `mod-lifecycle-and-ordering` owns: `UpdateBefore<T, Other>` with `Other` in a different phase adds the system to a dictionary that `Refresh()` never enumerates, with no exception and no line.
8. **Is the mod's own logger reaching the file?** `Logs/<Name>.log` exists but is empty or short: check `effectivenessLevel` in the mod's source, then `FallbackSettings.coc`'s `<Name> Logger` block, then the command line for a mistyped `--logsEffectiveness`.

Steps 1-6 are answerable from three text files with the game closed, which is the property worth teaching.

**Ruled (2026-08-04, the performance-and-memory pass; conflicts.md).** The native leak-detection ruling does **not** reach this reference, and the order above stops where it stops.
That ruling put the one memory instrument this game has — `NativeLeakDetection.Mode`, which the game switches off at boot — in `performance-and-memory`, bound to a condition: a debug configuration or a player-facing setting that is off by default, never a shipped default path, because the mode is a property of the native allocator and the cost lands on the game's own allocations and on every other mod in the player's load order.
It was to touch `diagnostics` only if this reference also stated a diagnosis order for a mod whose memory grows. It does not, and cannot: memory growth produces no log line at 1.6.0f1, so there is no line for an order to point at.
**What this reference owes:** it states no memory diagnosis order and teaches no leak switch. Where a reader's symptom is memory rather than silence, it bridges to `performance-and-memory` and stops.

### Reading the game's own tool-error feedback

Validation, not logging: the game reports why a placement is illegal through entities, not text.

`ValidationSystem` writes a `Game.Tools.Error` tag (`src/Game/Game.Tools/Error.cs`, a zero-size `IComponentData`) onto the temporary entities a tool produced, and carries the reason in a job-local `ErrorData { m_TempEntity, m_PermanentEntity, m_Position, m_ErrorType, m_ErrorSeverity }` (`src/Game/Game.Tools/ErrorData.cs`).

- `ErrorType` has 32 members, of which 30 are real error kinds beside `None` and the trailing `Count` (`src/Game/Game.Tools/ErrorType.cs:3-35`): `OverlapExisting`, `InvalidShape`, `NotEnoughMoney`, `PathfindFailed`, `NoRoadAccess`, `NoCarAccess`, `NoPedestrianAccess`, `LongDistance`, `TightCurve`, `NoTrainAccess`, `NoTrackAccess`, `AlreadyUpgraded`, `InWater`, `NoCargoAccess`, `NoWater`, `ExceedsCityLimits`, `NotOnShoreline`, `AlreadyExists`, `ShortDistance`, `LowElevation`, `SmallArea`, `SteepSlope`, `ExceedsLotLimits`, `NotOnBorder`, `NoGroundWater`, `OnFire`, `NoPortAccess`, `NotEnoughClearance`, `NoBicycleAccess`, `NotEditable`.
- `ErrorSeverity` is `None, Override, Warning, Error, Cancel, CancelError` (`src/Game/Game.Tools/ErrorSeverity.cs`).

**How the reason reaches the player.** Each error type has a prefab carrying `Game.Prefabs.ToolErrorData { m_Error, m_Flags }` beside a `NotificationIconData` (`src/Game/Game.Prefabs/ToolErrorData.cs`, authored by `src/Game/Game.Prefabs/ToolError.cs`). `ValidationSystem` builds a `NativeArray<Entity>` indexed by `(int)ErrorType` from the query `NotificationIconData + ToolErrorData` (`src/Game/Game.Tools/ValidationSystem.cs:1822`, filled at `:1212-1230`), and `AddIcon` looks the prefab up by `m_ErrorPrefabs[(int)error.m_ErrorType]` to place the icon (`:1649-1660`).
`ToolErrorFlags` is `TemporaryOnly = 1, DisableInGame = 2, DisableInEditor = 4` (`src/Game/Game.Prefabs/ToolErrorFlags.cs`), and the fill job **skips a prefab whose flag matches the current mode** (`:1220-1227`), leaving `Entity.Null` in that slot — which is the mechanism behind suppressing a tool error by editing its prefab rather than the tools. That technique belongs to `placement-definitions`; what belongs here is that a missing icon means a disabled error prefab and not an absent error.

**The null slot suppresses the whole error, not only its icon**, which is the half a reader diagnosing a blocked apply needs: `ProcessError` returns early when `m_ErrorPrefabs[(int)error.m_ErrorType] == Entity.Null`, before either `AddIcon` or `AddError` runs (`src/Game/Game.Tools/ValidationSystem.cs:1536-1541`, `:1559-1566`), so a disabled prefab raises no `Error` tag and therefore does not block the apply at all.
Added 2026-08-05: the shipped reference stated this and the finding above did not cite it, so the sentence was correct and unre-checkable.

`TemporaryOnly`'s effect is the third one and is cited nowhere else in this pipeline: `src/Game/Game.Tools/ApplyNotificationsSystem.cs:93` reads the bit off the error prefab's `ToolErrorData` and, when set, adds `Deleted` to the error's icon entity at apply time rather than promoting it, so the icon shows while previewing and is gone once the placement lands.

**How a tool reads whether it is blocked.** `ToolBaseSystem` holds `m_ErrorQuery = GetEntityQuery(ComponentType.ReadOnly<Error>())` (`src/Game/Game.Tools/ToolBaseSystem.cs:110`, `:313`), and `GetAllowApply()` is `(m_ToolSystem.ignoreErrors || m_ErrorQuery.IsEmptyIgnoreFilter) && !m_OriginalDeletedSystem.GetOriginalDeletedResult(0)` (`:533-539`).
So the diagnostic question "why will this not apply" is answered by whether that query is empty, and `ToolSystem.ignoreErrors` is a public settable bool (`src/Game/Game.Tools/ToolSystem.cs:159`) that overrides the whole check. The developer menu exposes it as a toggle (`src/Game/Game.Debug/DebugSystem.cs:3264-3267`), which is what the wiki's `Developer mode` page calls "bypass validation results".

**Nothing writes an error to a log.** A tool error produces an icon and a blocked apply and no text anywhere, which is why this section exists at all.

Rots: the `ErrorType` member list and its count, and the `ErrorSeverity` and `ToolErrorFlags` members — `src/Game/Game.Tools/ErrorType.cs`, `ErrorSeverity.cs`, `src/Game/Game.Prefabs/ToolErrorFlags.cs`.

### Surfacing a problem through a notification

`Game.PSI.NotificationSystem` is a static facade over the UI system, and its whole surface is three methods (`src/Game/Game.PSI/NotificationSystem.cs`):

- `Push(string identifier, LocalizedString? title, LocalizedString? text, string titleId, string textId, string thumbnail, ProgressState? progressState, int? progress, Action onClicked)`
- `Pop(...)` with the same parameters and `float delay = 0f` inserted second, after `identifier`
- `Exist(string identifier)`

All three no-op through a null-conditional call when `s_System` is null (`:24`/`:29`/`:34`), which it is until `NotificationUISystem.OnCreate` binds it (`src/Game/Game.UI.Menu/NotificationUISystem.cs:140`) and again after `OnDestroy` unbinds it (`:158-161`).

`ProgressState` is `None, Progressing, Indeterminate, Complete, Failed, Cancelled, Warning` (`src/Colossal.PSI.Common/Colossal.PSI.Common/ProgressState.cs`).

Three behaviours a caller needs:

- **`Pop` with a non-zero delay shows the notification first.** It calls `AddOrUpdateNotification` and only then schedules the removal (`NotificationUISystem.cs:459-476`), so `Pop(id, 5f, text: …)` is the idiom for "say this, then fade" — which is exactly what the mod loader does for its own completion message (`ModManager.cs:350`).
- **`AddOrUpdateNotification` merges by identifier** and only fills an `onClicked` that is currently null (`:436-439`, `:442-457`), so a second push cannot replace the first one's click handler.
- **`titleId` / `textId` are wrapped into vanilla key shapes**, `Menu.NOTIFICATION_TITLE[<id>]` and `Menu.NOTIFICATION_DESCRIPTION[<id>]` (`:164-172`). A mod passing its own key there gets a key that does not exist. A mod should pass `title:` / `text:` with `LocalizedString.Value(...)` or its own id instead.

**The corpus's worked example is `Traffic`**, twice. `Traffic/Code/Mod.cs:180-217` detects an incompatible mod, pushes a notification with `ProgressState.Failed` whose `onClicked` opens a `MessageDialog` with `copyButton: true` and pops the notification from the dialog's callback. `Traffic/Code/Utils/VanillaSystemHelpers.cs:19-27` pushes a `ProgressState.Warning` notification whose `onClicked` merely pops itself, as an acknowledgeable "something went wrong" with no detail. `Traffic/Code/Localization.cs:62-64` uses the same shape for a success message.

**A trap those examples contain.** `LocalizedString` has an implicit conversion from `string` and it is `LocalizedString.Id(id)`, not `Value` (`src/Game/Game.UI.Localization/LocalizedString.cs:124-127`).
So `new MessageDialog("Traffic Mod Compatibility Report", …)` passes literal English as a **localization id**. It renders as itself only because an unresolved id falls back to its own text.
`localization` owns the fallback rule; what belongs here is that a mod writing literal text into a dialog or notification should use `LocalizedString.Value(...)` explicitly.

### Surfacing a problem through a dialog

`GameManager.instance.userInterface.appBindings` carries five public entry points (`src/Game/Game.UI/AppBindings.cs`):

- `ShowMessageDialog(MessageDialog, Action<int> callback)` (`:396-400`) — one confirm action plus optional others.
- `ShowConfirmationDialog(ConfirmationDialog, Action<int>)` (`:379-383`) and `ShowConfirmationDialogAndWait(ConfirmationDialog)` returning `Task<int>` (`:385-392`).
- `ShowConfirmationDialog(DismissibleConfirmationDialog, Action<int, bool>)` (`:402-406`), whose second callback argument is the "do not show again" checkbox.
- `ShowErrorDialog(ErrorDialog)` (`:370-373`) and `DismissAllErrors()` (`:375-378`).

`MessageDialog` has two constructors, the second adding `details` and `copyButton` (`src/Game/Game.UI/MessageDialog.cs:8-16`); both forward to `ConfirmationDialogBase(title, message, details, copyButton, confirmAction, cancelAction, otherActions)` (`src/Game/Game.UI/ConfirmationDialogBase.cs:31-40`), which is also the field order the dialog serialises to the frontend (`:42-74`).
`MessageDialog` passes `cancelAction: null`, so its callback yields `0` for confirm and `2..n` for the other actions.

Only one dialog is in flight: `AppBindings` stores a single `m_ConfirmationDialogCallback` and overwrites it on each show (`:379-400`), so a second dialog opened before the first is answered loses the first's callback.

Rots: the `AppBindings` method names and the callback index convention — `src/Game/Game.UI/AppBindings.cs:370-406`.

### The game ships a Harmony patch census that can never see a mod's patches

`ListHarmonyPatches` reflects over whichever loaded assembly has "Harmony" in its name, finds `HarmonyLib.Harmony.GetAllPatchedMethods`, and logs `Patched Method: {declaringType}.{name}` per method followed by every prefix, postfix, transpiler and finalizer (`src/Game/Game.SceneFlow/GameManager.cs:2155-2207`, with `PrintPatchDetails` and `PrintIndividualPatches` immediately below it).

**It is called at `GameManager.cs:582`, and mods load at `:618`.**
Under the built-in modding runtime no mod assembly — and therefore no copy of `0Harmony.dll` — is in the AppDomain when it runs, so `assembly == null` and the method returns after its first line (`:2162-2165`).
Confirmed on the install: `Modding.log`'s first line is `Modding runtime: Builtin` and the file contains no `Harmony found.` and no `Patched Method:` line, despite loading ten mods.

The census exists for the BepInEx case, where the loader is in the process before `GameManager` runs — which is what `DetectModdingRuntimeName` distinguishes by scanning loaded assemblies for the name `BepInEx`, then for the substring, then for a type in a `BepInEx*` namespace (`:2255-2280`).

So: **an empty patch census in `Modding.log` proves nothing about what is patched.** An agent that wants the list calls `Harmony.GetAllPatchedMethods()` itself from its own code, after mods have loaded.

`UpdateModdingBacktraceAttributes()` runs immediately after mod initialisation (`:619`) and again on a playset change (`:1484`), so the crash-report attributes do carry the mod set even though the patch census does not.

### Verifying the debug patch before attaching anything

The setup skill's shipped procedure (`plugins/cs2-modding/skills/cs2-modding-setup/references/debug-patching.md:30-40`) offers two signals: the "Development Build" watermark in the bottom-right corner, and `Player.log`'s `Player connection` line carrying `[Debug] 1`.
Both hold. The install carries three stronger ones, and the strongest splits the two halves of the patch apart.

**Read from the patched install on 2026-08-05:**

- `Logs/SceneFlow.log` carries **`Game configuration: Development (Mono)`** in the version block. The source is `UnityEngine.Debug.isDebugBuild` (`GameManager.cs:2090`), which is a property of the player binary — so this line reports whether **step 2**, the `UnityPlayer.dll` swap, took. On an unpatched install it reads `Release (Mono)`.
- `Player.log` line 4 reads **`Starting managed debugger on port 56639`**, and line 5 `Using monoOptions --debugger-agent=transport=dt_socket,embedding=1,server=y,suspend=n,address=0.0.0.0:56639`. This is the mono debugger agent coming up, which is what **step 3**, `player-connection-debug=1` in `boot.config`, turns on — and it names the port, which is dynamic per launch rather than fixed.
- `Player.log` line 9 is the `Player connection … [Debug] 1` line the shipped reference already names.

Two purely static checks, with the game closed:

- `Cities2_Data\boot.config` on the install ends with a blank line and then `player-connection-debug=1`. Step 3, checkable with a text editor.
- The install root holds `UnityPlayer_Win64_player_development_mono_x64.pdb` beside `UnityPlayer.dll`. The name identifies the variation folder it was copied from, so its presence is evidence step 2 was done from the right directory. Nothing requires the pdb to be copied, so its **absence** proves nothing.

A mod can also read `UnityEngine.Debug.isDebugBuild` at runtime — the game does, at `GameManager.cs:2090` and `src/Game/Game.Prefabs/UIObject.cs:57`/`:70` and `src/Game/Game.Settings/About.cs:29`.

Unconfirmed: that the two signals really are independent, i.e. that a retail `UnityPlayer.dll` with `player-connection-debug=1` set produces the debugger line without the `Development` configuration, or vice versa. Only the both-applied case was observed. What settles it: revert one step at a time on this install and re-read the two lines. This matters because the split is what would let the reference say **which** step failed rather than that something did.

**Symbols.** `ExecutableAsset.LoadAssemblyImpl` loads `Path.ChangeExtension(path, "pdb")` beside the mod's dll when it exists, passing both byte arrays to `Assembly.Load` (`ExecutableAsset.cs:232-241`).
The toolchain deploys it without being asked: `DeployWIP` copies `$(OutDir)\**\*.*` into `$(LocalModsPath)\$(TargetName)` (`%CSII_TOOLPATH%/Mod.targets:106-112`), and the install's `Mods/HallOfFame/` and `Mods/Cs2TestMod/` each hold a `.pdb` beside the `.dll`.
So a locally deployed mod gets file and line numbers in every logged stack trace, through `StackTraceHelper.ExtractStackTrace(skipFrames, fNeedFileInfo: true)` (`src/Colossal.Logging/Colossal.Logging.Diagnostics/StackTraceHelper.cs:41-46`).

**Ruled (2026-08-04, the performance-and-memory pass; conflicts.md).** The Burst-gate ruling stays in `performance-and-memory`, and this reference stops at the attach precondition.
That ruling teaches both gates and leads with the runtime one — `--burst-disable-compilation` or `UNITY_BURST_DISABLE_COMPILATION` is what a reader reaches for to get a job into a debugger, and the `#if` gate is what to set up if they will do it often — on the ground that a preprocessor symbol defined nowhere produces no warning, no error, and a build indistinguishable from a working one.
It was to touch `diagnostics` only if this reference also stated how to get a mod's job into a debugger.
**What this reference owes:** it owns whether a debugger can attach at all — the log signals and the static checks above — and says nothing about Burst gates or `#if` symbols. Getting a Burst-compiled job into the debugger once attached is `performance-and-memory`'s, and this reference bridges there rather than repeating it. The material already sits in the setup skill's debug-patching reference as well, so a third telling would be the one that goes stale.

### An `Error` from a mod ships a crash report with the mod's log file attached

`CustomLogHandler.LogFormat` ends the `ILog` branch with: if `!log.disableBacktrace && level > Level.Warn`, send a Backtrace report — the exception with the message as an extra attribute, or the message alone — **passing `log.logPath` as an attachment** (`CustomLogHandler.cs:142-152`).
`BacktraceHelper.SendReport` no-ops when `BacktraceClient.Instance` is null (`src/Colossal.Logging/Colossal.Logging.Backtrace/BacktraceHelper.cs:47-62`).

The client is present on this install. `GameManager` calls `BacktraceHelper.SetDefaultAttributes(...)` at `:528`, which logs `BacktraceClient instance is null` through `Debug.LogWarning` when it is missing (`BacktraceHelper.cs:16`) — and neither `Player.log` nor any file under `Logs/` contains that string for the 2026-08-05 session.

So a mod that logs at `Error` or above uploads its own log file to Colossal's crash service, unless it sets `disableBacktrace` (or `SetBacktraceEnabled(false)`, `src/Colossal.Logging/Colossal.Logging/ILog.cs:107-111`).
One corpus mod touches it, and in the direction that keeps reporting on: `CS2-Platter/Platter/PlatterMod.cs:112` calls `SetBacktraceEnabled(true)` under `#if IS_DEBUG`, which writes the default value back. Nobody turns it off.
The developer menu exposes the toggle only under `--qaDeveloperMode` (`LogsDebugUI.cs:60`).
`Warn` is excluded by the strict `>` comparison.

### An unobserved faulted Task pops the error dialog

`GameManager.TryCatchUnhandledExceptions` subscribes two process-wide handlers, both logging at `Critical` to the `SceneFlow` logger (`GameManager.cs:2044-2056`):

- `TaskScheduler.UnobservedTaskException` → `log.Critical(e.Exception, "Unobserved exception triggered")`, after calling `e.SetObserved()`.
- `AppDomain.CurrentDomain.UnhandledException` → `log.Critical(exception, "Unhandled domain exception triggered")`.

`Critical` is above `Error` on a logger whose `showsErrorsInUI` is true, so both produce the modal dialog.
The first is the one that bites a mod: a `Task` a mod started and never awaited, whose exception surfaces whenever the finalizer runs — so the dialog appears at an arbitrary later moment, with a stack trace pointing at code that stopped running long ago.

Rots: the two message strings — `src/Game/Game.SceneFlow/GameManager.cs:2044-2056`.

### The wiki's six logging tips, checked one by one

From `Guidelines and Tips` (https://cs2.paradoxwikis.com/Logging, fetched live 2026-08-05).

| Tip | Verdict |
| --- | --- |
| Use plain messages without complex operations | Correct and unremarkable. |
| `DebugFormat()` avoids string allocation when filtered out | **Correct, and worth the reference stating why.** Every `*Format` overload checks `isLevelEnabled(level)` **before** calling `CheckedString.Format` (`UnityLogger.cs:941-947` is representative). The residual cost is boxing the `object` parameters at the call site, which `[Conditional]` on a wrapper removes entirely and this does not. |
| Do not call costly functions in format parameters | **Correct, and it is the limit of the tip above.** The arguments are evaluated at the call site regardless of level. |
| Use `if (Log.isDebugEnabled)` before expensive work | Correct. `isDebugEnabled => isLevelEnabled(Level.Debug)` (`UnityLogger.cs:163`); the sibling properties are `isTraceEnabled`, `isVerboseEnabled`, `isInfoEnabled`, `isWarnEnabled`, `isErrorEnabled`, `isFatalEnabled` (`:163-175`). **Zero corpus users.** |
| `Log.logStackTrace = true` for stack traces | Correct. Consumed at `UnityLogger.cs:336-341`, which captures with `skipFrames: 7`. There is also a scoped form nothing documents: `using (log.stackTraceScoped)` sets and restores it (`src/Colossal.Logging/Colossal.Logging/ILog.cs:9-32`, `:65`), and `CustomLogHandler` uses it internally to honour `showsStackTraceAboveLevels` (`CustomLogHandler.cs:92-99`). **Zero corpus users** for either. |
| `using (Log.indent.scoped)` for hierarchical output | Correct. `Indent.scoped` increments and returns `this`, `Dispose` decrements (`src/Colossal.Logging/Colossal/Indent.cs:12-18`, `:44-47`). The base indent is 1, not 0 (`:49-52`). **Zero corpus users.** |

**Three of six tips have no corpus user at all**, verified by grepping all 22 repositories for `logStackTrace`, `stackTraceScoped`, `indent.scoped`, `isDebugEnabled` and `isVerboseEnabled`: the only hits are `Traffic`'s own unrelated UI binding named `isDebugEnabled` (`Traffic/Code/CommonData/UIBindingConstants.cs:6`, `Traffic/Code/Debug/NetworkDebugUISystem.cs:22`).
The page is correct and unused, which is a different problem from being wrong and is worth the reference knowing: these are cheap wins nobody has taken.

The page's `showsStackTraceAboveLevels` default is not stated anywhere and is `Level.Error` (`UnityLogger.cs:289`), which is why an `Error` already carries a trace without `logStackTrace` being set.

### The debug menu's Logs tab, named here and owned by `debug-menu`

The tab is declared by `[DebugTab("Logs", 0)]` (`src/Game/Game.Debug/LogsDebugUI.cs:18`) and every logger in the process appears in it, with its effectiveness level, its "Show errors in UI" flag (`:44`), "Log stack trace", and "Show stack trace below levels" (`:74`) — all live, all writable, and a `Refresh` button that rebuilds the list.
`LogManager.GetAllLoggers()` is what it enumerates (`:23`, `LogManager.cs:136-139`), so a mod's own logger is there without registering anything.

That is the whole of what this reference needs to say about it: **the fastest way to raise a mod's log level mid-session is that tab, and what the flag does is this file's business while the menu is `debug-menu`'s.**

### Catalog gap: `Traffic` demonstrates the corpus's only complete player-facing failure report

`plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md:141`'s **Demonstrates** for `Traffic` names save-data migration only.

Sentence to add: _It also carries the corpus's only complete example of reporting a failure to the player — a notification carrying a failed progress state whose click opens a message dialog with a copyable details pane, with the dialog's callback popping the notification — and a category-gated logger whose methods carry `[Conditional]` attributes so a disabled category costs nothing at the call site._

Source lines: `Traffic/Code/Mod.cs:180-217` (the detection, the push and the dialog), `Traffic/Code/Utils/VanillaSystemHelpers.cs:19-27` (the acknowledge-only form), `Traffic/Code/Localization.cs:62-64` (the success form), `Traffic/Code/Logger.cs:14-51` (seven `[Conditional]` categories).

### Catalog gap: `Hall of Fame` demonstrates the error dialog used deliberately

`mod-catalog.md:311`'s **Demonstrates** for `Hall of Fame` reads "A mod whose product is its frontend, so its C# exists to serve one."

Sentence to add: _It is also the only mod here that keeps the logger's show-errors-in-UI flag on and treats a logged error as its user-facing error dialog, with a helper that flips the flag off and back around the errors it wants kept out of the player's way._

Source lines: `HallOfFame/HallOfFame/Mod.cs:78-81` (`SetShowsErrorsInUI(true).SetEffectiveness(Level.All)`), `HallOfFame/HallOfFame/Logging/ModLog.cs:33-40` and `:60-68` (the `Silently` wrapper and its three `ErrorSilent` overloads), `:42-54` (the recoverable/fatal shapes).

### Catalog gap: `Scene Explorer` demonstrates the compile-time log category

`mod-catalog.md:360`'s **Demonstrates** for `Scene Explorer` is about runtime component reflection and does not name its logging.

Sentence to add: _Its logging façade also shows the compile-time category pattern — one method per category carrying a `[Conditional]` attribute, so a build without the symbol drops the call and the message it would have built._

Source lines: `SceneExplorer/SceneExplorer/Logging.cs:26-38`.

A fourth candidate was considered and is not proposed: the build-configuration log level appears in seven repositories in one shape and an eighth in another, and belongs to no one of them, so the reference should teach it without a catalog entry claiming it.

### Source-list gap: the log directory is not on `docs/SOURCES.md`

`docs/SOURCES.md` names the installed game's assemblies, UI bundle, locale data and packaged content, the running game through both sibling plugins, and — under "What looks like a source and is not" — the `.coc` files at the user data root and the built assemblies under the mods paths.
It does not name **the logs the game writes**, which are first-party, version-stamped, present without the game running, and the only artifact that records what a _particular run_ did.

Entry to add, as a source in its own right rather than a note on an existing one: _the user data path's `Logs/` directory plus `Player.log` and `Player-prev.log` at its root, under `%CSII_USERDATAPATH%`._ Authoritative for what one run of the game did: the resolved launch arguments, the game and Unity versions, whether the build is a development one, which mods were in the active playset, which assemblies loaded and how long each took, every `Warn` and above from every logger in one file, and the last thing a process that died printed. Each logger writes `<Name>.log`, truncated at the session's first message, so there is no history beyond `Player-prev.log`.

### Source-list gap: `FallbackSettings.coc` is dismissed too quickly

`docs/SOURCES.md`'s "What looks like a source and is not" says of the `.coc` files that they "are saved settings, not code and not content".
That is true and it hides one: `FallbackSettings.coc` at the user data root is plain text and carries every logger's persisted settings, keyed `"<LoggerName> Logger"` — the durable override for a mod's log level and for whether its errors reach the player.

Correction to that bullet: keep the "not code and not content" point, and add that `FallbackSettings.coc` is readable plain text and is where a logger's persisted `effectivenessLevel`, `showsErrorsInUI`, `showsStackTraceAboveLevels` and `logStackTrace` live, so it settles "why is this mod's log level not what its source says".

---

## Bridge

This is a technique topic, and its bridge runs mostly to other techniques, because a diagnosis crosses every mechanism rather than belonging to one.

- **`mod-lifecycle-and-ordering`** is the closest neighbour and the boundary is stated at the top of this file. That reference owns the mechanism — which hook, which state, whether the system stays enabled, where `OnDispose` fits. This one owns the order, the files and what reaches the player. **Two corrections travel with the boundary and must land in both:** a lifecycle hook's failure is not invisible to the player, and the `OnUpdate` failure is logged at `Critical` rather than `Error`. The unregistered-anchor failure — a system spliced against a type registered in a different phase, which runs never and logs nothing — is that reference's finding and is step 7 of the diagnosis order here.
- **`navigating-the-decompile`** is where a reader goes once a log line has given them a type name or a message string. The messages in this file are the greppable half: a mod author holding `System update error during Modification4->MyLaneSystem:` finds the format string in `src/Game/Game/UpdateSystem.cs` and works outward. That reference's "what an empty grep does and does not prove" applies directly to one finding here — the Harmony patch census being empty in the log proves nothing about what is patched.
- **`debug-menu`** owns the developer menu whole, including the `Logs` tab this file names, the `ignoreErrors` toggle, and everything `--developerMode` gates. This file stops at what those switches change: the tab writes the flags described here, and `ignoreErrors` overrides the validation gate described here.
- **The setup skill's debug patching** (`plugins/cs2-modding/skills/cs2-modding-setup/references/debug-patching.md`) owns applying the patch; this reference owns confirming it took before a debugger is pointed at anything. The three log signals and the two static checks in the debug-patch finding above are what the confirmation is; the shipped procedure carries one of them (`Player connection … [Debug] 1`) plus the in-game watermark, and the four it does not carry include the two that separate the failed step from the working one.
- **`placement-definitions`** owns suppressing a tool error by editing its prefab; this file owns reading which errors the game raised and why an apply is blocked. `ToolErrorFlags.DisableInGame` is the seam: this reference explains that a missing icon can mean a disabled error prefab, and that one explains how to disable it.
- **`custom-tools`** owns `GetAllowApply` as something a tool overrides; this file states what the base implementation reads, so a tool author diagnosing "my apply never fires" knows to look at the error query first.
- **`localization`** owns the fallback for an unresolved key. This file's dialog and notification findings depend on it twice: `LocalizedString`'s implicit conversion from `string` is `Id`, not `Value`, and the notification helper wraps a bare `titleId` into a vanilla key namespace.
- **`patching`** — the game's own patch census cannot see a mod's patches, so a reader wanting the list calls `Harmony.GetAllPatchedMethods()` from their own code after load.
- **`performance-and-memory`** — two of `conflicts.md`'s ruled entries bind conditionally to this topic, and both conditions are addressed below.
- **Softly, the sibling Unity plugin.** Every question in the diagnosis order that a log cannot answer is a live-state question: whether a system is registered, whether it is enabled, whether a query matches anything, whether a patch took. The plugin is where those are answered, on a game that is already debug-patched for the debugger. It is never a requirement — steps 1 through 6 of the diagnosis order are three text files with the game closed.

**Every mechanics reference consumes this one indirectly**, since a change anywhere fails the same way: no line, a line in the wrong file, or a modal dialog the author did not mean to raise. The one mechanics topic with a direct claim is **`city-state-and-progression`**, which owns the notification system as the enumeration of failure states the simulation reports; the `Game.PSI.NotificationSystem` facade described here is the same surface reached from a mod's side rather than the simulation's.

### The two conditional rulings, both discharged

Both are written in at the findings they govern rather than here, so neither passage can be authored without its ruling.
The native leak-detection ruling sits at **The diagnosis order** and does not reach this reference: it states no memory diagnosis order and bridges to `performance-and-memory` instead.
The Burst-gate ruling sits at **Verifying the debug patch before attaching anything** and stops this reference at the attach precondition: no Burst gates, no `#if` symbols, a bridge to `performance-and-memory` for getting a job into the debugger once attached.

---

## Dead ends

- **`src/Game/Game.Debug/` is 69 files and only one bears on this topic.** `LogsDebugUI.cs` is the logging surface; everything else is the developer menu's tabs and per-domain gizmo systems, which the boundary amendment of 2026-08-04 moved to `debug-menu` whole. `ErrorSpammer.cs` and `TestScenarioHelperUnhandledException.cs` were opened and are test fixtures for the error-dialog machinery rather than anything a mod uses.
- **`src/Game/Game.PSI/` is seven files and only one bears on this topic.** `NotificationSystem.cs` is the whole of the notification surface; `Telemetry.cs`, `RichPresenceUpdateSystem.cs`, `VirtualKeyboard.cs`, `PlatformSupport.cs`, `ModTags.cs` and `ExcludeGeneratedModTagAttribute.cs` carry nothing diagnostic. The actual notification implementation is not in that namespace at all — it is `src/Game/Game.UI.Menu/NotificationUISystem.cs`, which `docs/SOURCES.md`'s and the structure file's source list for this topic do not name.
- **No log settings file per logger.** `LogManager.SetSettingsProvider` has exactly one caller (`AssetDatabase.cs:328`) and the provider it installs reads a settings **asset**, not a file a user can drop in. `FallbackSettings.coc` is where it lands and there is no per-logger file to create.
- **`ILog.redirectToDefault` has no setter call anywhere in `src/`.** Searched; the only writes are the property declaration, the `Copy()` initialiser and the unused `SetRedirectToDefault` extension. The one thing that would set it is the settings file above.
- **`--duplicateLogToDefault` has no consumer.** Searched across all of `src/`; recorded as a finding above rather than only here, because the flag's name promises a behaviour a reader will look for.
- **No corpus mod uses `logStackTrace`, `stackTraceScoped`, `indent.scoped`, `isDebugEnabled` or `isVerboseEnabled`.** Grepped over all 22 repositories. Half the wiki's tips are unexercised, and there is no practice to check the reference against.
- **`keepStreamOpen` is set by nothing in the game and nothing in the corpus.** Grepped over all of `src/` outside `Colossal.Logging` itself, and over all 22 corpus repositories: zero. So the open-write-close-per-message behaviour is what every logger does today, and the `Log '{0}' opened at {1}` / `closed at` lines it gates (`UnityLogger.cs:379`, `:395`) appear in no log.
  **That is a fact about the game and the corpus, not about what a mod can do**: it is a public settable property on the interface itself (`src/Colossal.Logging/Colossal.Logging/ILog.cs:61`) and one of the seven members `UnityLogger.Copy()` carries (`:249-260`), which is the shape `AssetDatabase.LoadSettings` deserializes a `FallbackSettings.coc` block into. A mod sets it in one line, and a user's settings file could too.
- **`redirectToDefault` has no corpus user either**, same sweep. `disableBacktrace` has exactly one, recorded above, and it sets the default value.
- **No corpus mod calls `NotificationSystem.Exist`.** Only `Push` and `Pop` appear, in one repository.
- **The `Developer mode` wiki page was not fetched.** It is the third page this ticket's source list names, and the boundary amendment of 2026-08-04 moved everything it covers to `debug-menu`. `survey-wiki-inventory.md:68-69` records its headings and its known-issues section; nothing there bears on the diagnosis order, the log surfaces or the dialogs. The launch-flag spelling contradiction it carries is already ruled (`conflicts.md`, the setup-skill pass).
- **`survey-mods-techniques.md` §7.4's TLE example could not be re-verified.** It cites `Cities2-TrafficLightsEnhancement/TrafficLightsEnhancement/Mod.cs:119-129` for a `Mod.Assert(condition, message, showInUI, [CallerArgumentExpression] expression)` helper. That repository is not in the 22-repository checkout — `mod-lifecycle-and-ordering.md:466` records the same absence. The technique is not carried into this file. A `[CallerArgumentExpression]`-based assert is a plain C# technique needing nothing from the game, so nothing is lost beyond the worked example.
- **`survey-mods-techniques.md` §7.4's MoveIt `QLog` could not be read as a logging wrapper.** `CS2-MoveIt` contains no `LogManager.GetLogger` call; its logging comes through the shared `QCommonLib` projitems, whose source is absent from the checkout — the same gap `mod-lifecycle-and-ordering.md:465` records for three other framework base classes.
- **No entry was appended to `conflicts.md`.** Nothing here resisted the decompile or the install. What could not be settled by reading is marked `Unconfirmed:` at the finding it qualifies rather than listed here, since a count of them goes stale as they are closed — the error-dialog one already has been, by the maintainer's own observation. Each is an experiment rather than a judgement, so none is the maintainer's to rule on. The two conditional rulings this file owes are recorded in the bridge section, where they govern the prose.
