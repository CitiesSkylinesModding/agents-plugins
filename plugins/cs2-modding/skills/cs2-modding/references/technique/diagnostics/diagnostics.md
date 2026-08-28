# Diagnosing a mod that does not work

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

Finding out why a mod is not working: which file to open first, what each line proves, and what the player sees when something fails.
Fixing what the diagnosis found belongs to whichever reference owns the mechanism.

## The order to diagnose in, and which file answers each step

Steps 1 to 6 are answerable from three text files with the game closed, and that is the property to work from: reach for a running game only once they come back clean.

**Every step but the last reads lines the game writes about your mod, whether or not your mod logs anything at all.**
Only step 8 reads the mod's own log, which exists only once something has been written through the mod's logger — creating one writes no file.
Source: `src/Game/Game.Modding/ModManager.cs`, `src/Game/Game/GameSystemBase.cs`, `src/Game/Game/UpdateSystem.cs`.

1. **Is code modding on at all?**
   `Modding.log` reading `Modding is disabled` ends the diagnosis; that file still carries a modding-runtime line from before the loader exists and a disposal line at shutdown, but nothing per-mod for the rest of the session.
   `SceneFlow.log`'s `Command line:` block names the flag responsible — `--disableCodeModding`, or `--disableModding`, which sets it as a side effect.
2. **Did the mod load?**
   `Modding.log` carries two lines per mod, and the pair is the answer — neither alone is.
   `Loaded <assembly full name> in <n>ms` says the loader reached your mod and got as far as timing it.
   `Error initializing mod <assembly full name> (<assembly full name>)`, with a stack trace, says the load threw — **both slots carry the assembly identity, not the display name the user gave you**, so grep for the assembly name or for the message alone.
   **A `Loaded` line on its own proves nothing**: the timer wrapping the load reports from a `finally`, so a mod that threw, one whose dependencies did not resolve, and one that lost a duplicate resolution all produce the same success-shaped line.
   **No log line confirms a clean load.**
   `Loaded` with no `Error initializing mod` beside it is also exactly what an unresolved dependency and a lost duplicate resolution leave behind, and those two are told from a real success only by the in-game failure notification or by looking at the running game.
   Source: `src/Game/Game.Modding/ModManager.cs` (both lines and the states behind them), `src/Colossal.Core/Colossal/PerformanceCounter.cs` (the timer's unconditional report).
3. **Did the mod fail somewhere the loader does not report?**
   Three ways to be dropped with no error line and no state, listed below under what the loader never reports.
   One of the three still gets the `Loaded` line, which is why step 2 cannot end the diagnosis on its own.
   Where mods are missing in a block rather than one at a time, this step is the wrong one: that is the pass-wide abort, and where a load failure is reported has its signature.
   The `======= Enabled Mods =======` block is a separate question again: it lists the Paradox playset alone, so a locally deployed mod is absent from it while loading perfectly well.
4. **Did a system fail to construct?**
   A throw out of any system's `OnCreate` propagates through the registration call, out of `OnLoad`, and fails the **whole mod**, so it presents exactly as step 2 and the stack trace is the only thing that separates them.
5. **Did a lifecycle hook throw?**
   `SceneFlow.log` carries `<Type>: Error on game preload, disabling system...`, or the `game load`, `state change` or `Focus change` wording, and three of the five wrapped hooks leave that system disabled for the rest of the session.
6. **Is a system throwing every frame?**
   `SceneFlow.log` carries `System update error during <Phase>-><SystemType>:` at `Critical`, and the system keeps running and keeps throwing.
7. **Is the system registered and never running?**
   Nothing logs this at all.
   It is the anchoring failure `mod-lifecycle-and-ordering` owns: a system spliced beside a type registered in a different phase is filed in a dictionary the rebuild never enumerates, with no exception and no line.
8. **Is the mod's own logger reaching its file?**
   Check `Player.log` first: the mod's lines there while its own file is missing mean the level is not the problem, and the severity ladder below names the three causes that are.
   Otherwise a `Logs/<Name>.log` that exists and is short is a level problem: read `effectivenessLevel` in the mod's source, then the `<Name> Logger` block in `FallbackSettings.coc`, then the launch options the user actually set.
   The `Command line:` block cannot answer the last one — it masks the value of every `name=value` argument — so a mistyped `--logsEffectiveness` has to be read from the launcher rather than from the log.

Every _failure_ line steps 2 to 6 produce is `Warn` or above, so each of them reaches `Player.log` as well: the load error at `Error`, the shipped-game-assembly warning at `Warn`, the lifecycle throw at `Error`, and the per-frame update throw at `Critical`.
Step 1's lines and step 2's `Loaded … in <n>ms` are `Info` and stay out of it — see the ladder below.
Where the symptom is memory growth rather than silence, this order has nothing to offer: no log line records it at all, and `performance-and-memory` owns the one instrument that does.
A process that died with no managed exception is the other symptom this order cannot take: it assumes a live process still writing lines, and `performance-and-memory` owns the native failures that end a run without one.
Copy the logs before going there — the log-directory section below states the deadline.

(VOLATILE: the loader and lifecycle message strings quoted above — the mod manager, `GameSystemBase`'s hook wrappers, and `UpdateSystem.Update`.)

## The log directory, and the six files that matter

The log directory is `Logs/` under the user data path, which the toolchain names in `%CSII_USERDATAPATH%` and which on Windows is `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II`.
It never has to be guessed: the log manager writes the resolved path to standard output as `Logs at <path>`, and that line lands near the top of `Player.log`.

| File | What it is | Why it is opened |
| --- | --- | --- |
| `Logs/SceneFlow.log` | the `SceneFlow` logger, which is also the system base's own logger | boot transcript, launch arguments, versions, and every system-level exception |
| `Logs/Modding.log` | the `Modding` logger | the mod loader's transcript: playset, per-mod load timings, load failures |
| `Logs/<LoggerName>.log` | one file per logger, mod loggers included | a mod's own stream, at whatever level it set |
| `Player.log` | Unity's own log, at the user data root rather than in `Logs/` | engine boot, the debug-patch signals, and every `Warn` and above from every logger |
| `Player-prev.log` | the previous session's `Player.log` | what a process that died last run printed before dying |
| `FallbackSettings.coc` | user data root, plain text | every logger's persisted settings |

Every logger except `Default` gets a file named after itself, and `Default` gets none — anything logged through it goes straight to Unity's handler.
Files are never deleted, so the directory accumulates one file per logger that has _ever_ run rather than one per logger in the current session.
That is what makes a shipped mod's logger name readable from disk with the game closed, which is the cheapest way to learn what a mod calls its log.

A `Logs/<Name>.log` is **truncated at the session's first message**, not appended across runs, and there is no rotation and no previous copy.
**So it is the relaunch that destroys a crashed run's evidence, not the crash.**
Copy `Player.log` and the whole `Logs/` directory before the game is started again: until then the dying session's own files are all still on disk.
Afterwards `Player.log` survives as `Player-prev.log`, and a `Logs/<Name>.log` survives only until its own logger writes again — so the file of a mod disabled after the crash still holds its last lines.
Source: `src/Colossal.Logging/Colossal.Logging/UnityLogger.cs` (the truncation and the absence of rotation; the `Player-prev.log` rename is the engine's own).

**A `<Name>.log` is created by the first message that reaches its logger, not at startup**, so an absent file is a fact about the logger rather than about the crash — the severity ladder below names what puts a mod in that state.
Source: `src/Colossal.Logging/Colossal.Logging/UnityLogger.cs` (the lazy open, from the write path alone).

A line in a `<Name>.log` reads `[yyyy-MM-dd HH:mm:ss,fff] [LEVEL]  message`, with two leading spaces before the message.
A line in `Player.log` is prefixed with the logger name instead, as `[SceneFlow] [ERROR]  message`.

(VOLATILE: the `SceneFlow`, `Modding` and `Default` logger names, the timestamp and level-tag shape, and the user data path this directory sits under — the log manager, the system base, the mod manager, and the game's own `Logs/` directory, which lists every logger that has run.)

## The severity ladder, and where a line goes

Eleven levels, from `Disabled` at 10,000 down through `Emergency`, `Fatal`, `Critical`, `Error`, `Warn`, `Info`, `Debug`, `Trace`, `Verbose` to `All` at 0.
A message is written when its level is at or above the logger's `effectivenessLevel`, so `Verbose` lets everything through and `Disabled` lets nothing through and closes the stream.

Three destinations, decided per message:

- **The logger's own `<Name>.log`**, unless that logger redirects to the default (below).
  Source: `src/Colossal.Logging/Colossal.Logging/CustomLogHandler.cs`.
- **`Player.log`, for `Warn` and above**, so a logger's `Info` and below do not reach it.
  Source: `src/Colossal.Logging/Colossal.Logging/CustomLogHandler.cs` (the test that forwards to Unity's handler), `src/Colossal.Logging/Colossal.Logging/UnityLogger.cs` (the level-to-log-type mapping it tests).
- **Standard output, only when the game was started with `--captureStdout=console|capture|redirect`.**
  Source: `src/Colossal.Logging/Colossal.Logging/CustomLogHandler.cs` (the stream it picks), `src/Game/Game.SceneFlow/GameManager.cs` (the option and the flag it sets).

A mod that logs a problem at `Warn` or `Error` has already put it in `Player.log`, so one file answers "did anything go wrong anywhere" across the game and every mod at once.

**Two things bypass that threshold, and both matter to a reader who takes it as absolute.**
Anything logged through `UnityEngine.Debug` rather than through a logger carries no logger for the rule to consult, so it goes straight to Unity's handler and lands in `Player.log` **at any level** — a plain `Debug.Log` included.
And a logger whose `redirectToDefault` is set sends everything it writes to `Player.log` at every level — but that is a **redirect and not a copy**: unless the game was started with `--captureStdout`, the same setting stops the logger writing its own `<Name>.log` at all.
**A missing `<Name>.log` whose lines are turning up in `Player.log` has three causes, and the common one is the first.**
Each of the three keeps the file from ever being opened, which is why the file is absent rather than empty.
The mod is logging through `UnityEngine.Debug` rather than through a logger, which is the case just above — read its source for those calls before anything else.
Or `redirectToDefault` is set: reachable, since it is a plain settable property and the persisted settings would carry it, but nothing in the game, the developer menu or the mod corpus turns it on, so it is the second place to look and not the first.
Or the open itself failed and was swallowed — the directory create and the file create are wrapped in an untyped `catch`, so an unwritable `Logs/` directory, a locked file or a path the filesystem rejects produces no file, no warning, and a `NullReferenceException` per message on the writer that was never built.
Source: `src/Colossal.Logging/Colossal.Logging/CustomLogHandler.cs` (the first two routes), `src/Colossal.Logging/Colossal.Logging/ILog.cs` (the property and its unused setter), `src/Colossal.Logging/Colossal.Logging/UnityLogger.cs` (the open, its untyped catch and the unvalidated path).

`--duplicateLogToDefault` is the launch flag whose name promises a copy, and nothing acts on it — the parsed value is only echoed back in the boot transcript's `Configuration:` dump.
(VOLATILE: that this flag is still inert — the game manager's configuration type, whose fields are all echoed into that dump.)

`Critical` sits above `Error`, which is the threshold that raises the modal error dialog.

(VOLATILE: the `Level` member names and their severity constants — the logging library's `Level` type.)

## Setting a logger's level

Four places set it, each overriding the one before it.

- **`--logsEffectiveness=<LEVEL>` on the command line** sets the default and loops over every logger already created, so it applies retroactively as well as forward.
  **An unrecognised value falls back to `Disabled`, silently.**
  The parser upper-cases first, so `=debug` works, but a typo or a trailing space turns _every log in the game off_ with no error and no warning.
  A run that produced empty log files is a run whose command line is checked first.
  Source: `src/Colossal.Logging/Colossal.Logging/Level.cs` (the upper-casing and the fallback), `src/Game/Game.SceneFlow/GameManager.cs` (the option), `src/Colossal.Logging/Colossal.Logging/LogManager.cs` (the retroactive loop).
- **`FallbackSettings.coc`** at the user data root is plain text and holds a block per logger, keyed `<LoggerName> Logger`, carrying whichever of the logger's own settings were persisted — `effectivenessLevel`, `showsErrorsInUI`, `showsStackTraceAboveLevels`, `logStackTrace`, `keepStreamOpen`, and the redirect and backtrace flags.
  It is applied when the logger is first fetched, and it survives launches.
  This is where to look when a mod's log level is not what its source says.
  `showsStackTraceAboveLevels` defaults to `Error`, which is why an `Error` already carries a stack trace without `logStackTrace` being set.
  Source: `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/AssetDatabase.cs` (the key and the file it lands in), `src/Colossal.Logging/Colossal.Logging/UnityLogger.cs` (the settings a logger carries, and that default).
- **The mod's own code wins over both**, because it runs after the flag is parsed and after the persisted settings are applied.
  So `--logsEffectiveness=DEBUG` is not a way to get a shipped mod's debug lines out of it; a mod that sets its level in `OnLoad` has overridden you.
  Source: `src/Game/Game.SceneFlow/GameManager.cs` (option parsing and the asset cache both ahead of mod initialisation).
- **The developer menu's Logs tab**, which writes those same persisted settings live and is the fastest way to raise a mod's level mid-session.
  Its level dropdown is built from an enumerator that **omits `Critical`**, so a logger sitting at that level shows no selection at all and cannot be set to it there — which is worth knowing because `Critical` is the level a system's own update exception logs at.
  `debug-menu` owns the menu.
  Source: `src/Game/Game.Debug/LogsDebugUI.cs` (the fields and the dropdown), `src/Colossal.Logging/Colossal.Logging/Level.cs` (the enumerator that skips `Critical`).

**The logger name is an identity, not a label**, and this is where a mod can break the game for everyone else.
The log manager keeps one logger object per name and hands the existing one back on a match, silently — the "already exists" warning sits on a path fetching a logger never takes.
So two mods choosing the same name share one object, one file and one set of settings, and a mod choosing a name the game already uses borrows the game's own logger.
The game has taken dozens of ordinary names — `SceneFlow`, `Modding`, `Rendering`, `Simulation` and `Default` among them — so treat any word describing what a mod does as already gone.
`SceneFlow` is the one the five lifecycle wrappers log through: fetching it and setting `showsErrorsInUI` to false turns the modal error dialog off for every system in the game rather than for yours.
`Default` fails the other way, and quietly: it is the one name that gets no file at all.
Name the logger after the mod's own assembly or display name.
Matching it to the mod's settings folder name is worth doing too, for a reason no mechanism enforces: a user reporting a problem has to be able to find the file to send.
Source: `src/Colossal.Logging/Colossal.Logging/LogManager.cs` (one object per name, and where the "already exists" warning sits), `src/Colossal.Logging/Colossal.Logging/UnityLogger.cs` (the name as the file name, and `Default` getting none), `src/Colossal.Core/Colossal.Entities/COSystemBase.cs` (`SceneFlow` as the lifecycle wrappers' logger), `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/AssetDatabase.cs` (the name as the settings key).

What a log call costs, and how to make one cheap enough to leave in a shipped build, is `performance-and-memory`.

(VOLATILE: the settings key suffix `" Logger"`, the file name `FallbackSettings.coc`, and which logger names the game has already taken — the asset database's settings provider, the game's own user data root, and the `Logs/` directory of a vanilla run, which lists every logger the game itself creates.)

## What the boot transcript answers before any mod question is asked

`SceneFlow.log` opens with a fixed first-party sequence, and three parts of it settle questions that would otherwise be asked of the user.

- **`Command line:`**, one argument per line, is how "is developer mode actually on" and "was modding disabled" are answered without asking.
- **The version block** carries `Game version`, `Unity version`, `Cohtml version`, and one line per installed DLC and radio pack.
- **`Game configuration: Development (Mono)` or `Release (Mono)`** is the debug-patch signal, in a text log, at a known place — see the debug-patch section below.

`Modding.log` opens with its own sequence: `Modding runtime: Builtin` (the alternative names a third-party loader assembly and its version), then the playset and enabled-mod blocks, then per mod in load order the optional `Loaded additional Burst code <path>` and the `Loaded <assembly full name> in <n>ms` line, then `Mods initialized in <n>ms`, then one `Registered UI Module …` line per UI module.

`Modding.log` also carries the game's own Harmony patch census, and **an empty census there proves nothing about what is patched.**
The census runs before any mod assembly is in the process, so under the built-in modding runtime it finds no Harmony to reflect over and returns immediately.
Getting a real list means calling for it from a mod's own code after load, which `patching` owns.
Source: `src/Game/Game.SceneFlow/GameManager.cs`.

## Where a load failure is reported

**The three states that rethrow do not survive to be reported.**
`GeneralError`, `LoadAssemblyError` and `LoadAssemblyReferenceError` all rethrow, so the loader catches, runs `OnDispose` — **which overwrites the state with `Disposed`** — and only then tries to write `Error initializing mod …`.
The notification pass runs afterwards and skips anything below `IsNotModWarning`, so `Disposed` is below the bar and **a mod whose `OnLoad` threw produces no per-mod notification and no dialog.**
`MissedDependenciesError` and `IsNotUniqueWarning` return instead of throwing, so their state survives: those two are the only ones the per-mod pass ever shows.
Source: `src/Game/Game.Modding/ModManager.cs`.

**That `Error initializing mod` line appears only when the assembly itself loaded**, one of its two slots reading through the loaded assembly.
**Where it does not, the whole modding pass goes with it.** Evaluating that argument throws inside the very handler meant to report the failure, so it escapes the per-mod loop and lands in the loader's outer handler: every mod behind the failing one is never loaded, the UI-module registration pass never runs, the manager never marks itself initialised, and the player gets the global mods-failed notification.
**`OnDispose` is a second way into that same abort.** The handler calls it before it builds the message, unguarded — and it is calling it on an instance the loader allocated without running a constructor, on a load that did not finish. A teardown body that unpatches Harmony or unregisters a settings page then dereferences a field that was never set, and takes the pass down with it.
So a single mod presents as _nothing loaded_ rather than as one mod missing.
**`Mods initialized in <n>ms` is still written on the way out**, from a timer wrapping the whole loop, so its presence does not mean the pass finished — and it is the anchor to search for, because the escaped exception's own stack lands in `Modding.log` directly after it, logged bare and carrying no message string to find it by.
The last `Loaded` line before that is the mod whose load was in flight.
Source: `src/Game/Game.Modding/ModManager.cs` (the property the message reads, the constructorless allocation, the dispose call ahead of it, the two timers, the two nested handlers, and what the outer one skips).

**An assembly-load failure also logs upstream, in the asset loader, before the state is ever set.**
Those lines carry the exception that actually failed, so search `Modding.log` for `Error loading assembly` alongside `Error initializing mod` rather than instead of it.
Source: `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ExecutableAsset.cs` (the loader's own logging, and the exceptions it wraps).

**A load failure is a log-file diagnosis, not an on-screen one**: `Modding.log` holds it, and the absence of an in-game warning proves nothing.
The unresolved dependency and the shadowed duplicate are the exceptions in the other direction — they are the two the per-mod pass shows, and the two that produce no `Error initializing mod` line, so in the log they are indistinguishable from a clean load.
The pass-wide abort above is the third, and it reads the other way still: the player gets a failure naming no mod, while the log carries the unlabelled stack and the last `Loaded` line that between them place it.
Source: `src/Game/Game.Modding/ModManager.cs`.

[The loader states](loader-states.md) take them one by one — reach for it when a mod is missing from the list and you need to know which state to look for, or when you have a state name in hand and need what it says about the assembly.

(VOLATILE: the state member names quoted above — the mod manager's own state enum.)

## What the loader never reports at all

Three ways for a mod to be dropped, none of them producing a state, a notification, or an `Error initializing mod` line.
Only the second is timed like any other mod and gets the success-shaped `Loaded` line.
The first is absent from `Modding.log` altogether, and the third reaches it as its `Warn` and nothing else — both are dropped before the loader has anything to time.

- **The file is not a managed assembly.** A native DLL or a corrupt file fails the metadata read, the exception is swallowed, and the asset is filtered out as not an IL assembly — invisible from end to end.
  Source: `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ExecutableAsset.cs`.
- **The assembly is neither a mod nor a reference.**
  Anything declaring no `IMod` that no loading mod references returns immediately from its load, leaving the state `Unknown`.
  This is the case to check when a built DLL sits in the folder and nothing at all happened: the question is whether a **top-level** type implements the interface.
  Source: `src/Game/Game.Modding/ModManager.cs` (the return), `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ExecutableAsset.cs` (what makes an asset required).
- **The mod shipped a copy of a game assembly.** This one _does_ log: the asset is skipped with `Assembly "{0}" is in-game assembly and it should NOT be shipped with mod "{1}"` at `Warn`, which puts it in `Modding.log` and in `Player.log`.
  Source: `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ExecutableAsset.cs`.

## A lifecycle hook that throws logs somewhere else, and stops the player

The system base wraps five hooks, and a throw from any of them is logged through the `SceneFlow` logger — **not the mod's own logger, which is where its author looks and finds nothing.**
Source: `src/Game/Game/GameSystemBase.cs` (the five wrappers), `src/Colossal.Core/Colossal.Entities/COSystemBase.cs` (the logger they use).

So the mod's own log is silent while the session is not.
That logger's errors are not suppressed from the UI, so every wrapper's `Error` **pops the modal error dialog and pauses the simulation** — and lands in `Player.log` besides.
Source: `src/Colossal.Logging/Colossal.Logging/UnityLogger.cs` (the default that is never turned off for it), `src/Game/Game.UI/ErrorDialogManager.cs` (the gate and the pause), `src/Colossal.Logging/Colossal.Logging/CustomLogHandler.cs` (the `Player.log` half).

The type name in the message is the system's short name, so it greps.

**Two of those lines cannot be read at face value.**

- `Error on game preload, disabling system...` is emitted **byte-identically by two different hooks**, `OnWorldReady` and `OnGamePreload`, so the message cannot say which of them threw.
  The logged stack trace is the only thing that separates them.
  Source: `src/Game/Game/GameSystemBase.cs`.
- `Error on state change, disabling system...` says "disabling system" and **does not disable the system**.
  It keeps running, half-initialised, for the rest of the session, so a reader who trusts the message concludes the system is out of the picture when it is the thing still misbehaving.
  Source: `src/Game/Game/GameSystemBase.cs`.

Which of the five hooks each message belongs to, and what a disabled system leaves running in the rest of the mod, is `mod-lifecycle-and-ordering`.

## A developer-menu tab that throws logs under two names

A debug-menu tab method that throws loses its tab, and the exception surfaces under two names on two separate paths: the attribute scan logs `Failed to register '<tab name>' Debug UI` once, and an explicit rebuild of that delegate logs the same message with the method name — repeating on every rebuild while the menu stays open.
Grep for `Failed to register`, and read repeated method-name lines as one defect re-thrown rather than several.
`debug-menu` owns the menu, its registration paths, and the one mod shape known to throw there.
Source: `src/Game/Game.Debug/DebugSystem.cs`.

## An `Error` from a logger is a modal dialog and a paused simulation

**`showsErrorsInUI` defaults to true on every new logger.**
A mod that fetches a logger and stops there has opted _in_: the first `Error` it writes stops the player's game.
Source: `src/Colossal.Logging/Colossal.Logging/UnityLogger.cs` (the default), `src/Game/Game.UI/ErrorDialogManager.cs` (what it opts into).

The gate is the logger's flag plus a level at or above `Error`, and three things follow from it.

- **A `Warn` never produces a dialog**, despite the event that drives it being named for warn-or-higher.
  Source: `src/Game/Game.UI/ErrorDialogManager.cs`.
- **`UnityEngine.Debug.LogError` and `Debug.LogException` cannot be silenced.**
  Neither carries a logger for the gate to consult, and a null logger is treated as permission — `LogException` skips the flag check entirely.
  So the quiet-looking fallback for code running before a mod's own logger exists is in fact the loudest channel in the game.
  `Debug.LogWarning` and `Debug.Log` raise no dialog; both still land in `Player.log`, and neither reaches any `Logs/<Name>.log`.
  Source: `src/Colossal.Logging/Colossal.Logging/CustomLogHandler.cs` (the branch with no logger to carry), `src/Game/Game.UI/ErrorDialogManager.cs` (the null-logger gate and the exception path that skips it).
- **A system throwing in `OnUpdate` logs at `Critical`**, which is above `Error`, so in a normal game session it raises the dialog and pauses the simulation _every frame it throws_, until the spam detector offers a mute.
  In the game's own map and asset editor — not the Unity editor, which no mod author runs — the update wrapper suppresses the flag around that one call and the failures are collected into a panel instead.
  Source: `src/Game/Game/UpdateSystem.cs` (the level and the editor suppression), `src/Colossal.Logging/Colossal.Logging/Level.cs` (`Critical` sitting above `Error`).

What the player gets is a modal dialog carrying an icon, a title, the message, a scrollable details pane with a copy button, and its buttons; the simulation speed is cached and set to zero until the queue empties.
Repeats are merged rather than stacked, keyed on a fingerprint of exception type, message, details and identifier, and a burst detector adds a `Mute` action once a fingerprint is spamming.

An unobserved faulted `Task` reaches the same dialog: both the process-wide unobserved-task and unhandled-domain-exception handlers log at `Critical` to `SceneFlow`.
The first is the one that bites a mod — a `Task` started and never awaited surfaces whenever the finalizer runs, so the dialog appears at an arbitrary later moment with a stack trace pointing at code that stopped running long ago.

One more consequence of logging at `Error`: where the game's crash-reporting client is present, **the message is uploaded to it with the logger's own log file attached**, unless the logger sets `disableBacktrace`.
`Warn` is excluded, and the send is a no-op when no client was created.
Source: `src/Colossal.Logging/Colossal.Logging/CustomLogHandler.cs` (the send, its level test and the attachment), `src/Colossal.Logging/Colossal.Logging.Backtrace/BacktraceHelper.cs` (the no-op without a client).

So `showsErrorsInUI` decides whether a mod's errors stop the player, and the level alone decides whether its log file leaves the machine.
Turning it off and reaching for the dialog deliberately, where a failure genuinely needs the player's attention, is the shape to copy; the next section is how.

## Surfacing a problem to the player on purpose

Two surfaces: the notification tray, and the modal dialogs.
Notifications go through a static facade whose whole surface is push, pop and exists, and which silently no-ops before the UI system is created.
Dialogs go through the app bindings, in message, confirmation, dismissible-confirmation and error flavours, and only one is on screen at a time.
The message and plain-confirmation entry points also share one callback slot, so a second opened before the first is answered destroys the first's callback.

**One trap belongs up here because a reader who does not follow the pointer gets it wrong.**
A bare string handed to either surface is taken as a **localisation key**, not as literal text, because that is what the implicit conversion produces.
It renders as itself in every locale, because no dictionary carries that key — so testing catches it nowhere, and what it costs is text no translation can ever reach.
Write the value form explicitly whenever the text is already final.
Source: `src/Game/Game.UI.Localization/LocalizedString.cs`.

The entry points, their arguments, the progress states, the error dialog's fields and its action bits are in [reporting-to-the-player.md](reporting-to-the-player.md).

## Reading the game's own tool-error feedback

Placement validation is not logging: the game reports why a placement is illegal through **entities**, not text.

The validation system tags the temporary entities a tool produced with `Game.Tools.Error`, a zero-size component, and carries the reason in a job-local `ErrorData` record naming the temporary and permanent entities, a position, an `ErrorType` and an `ErrorSeverity`.
That record is job-local rather than a component, so the tagged entity carries the fact that it failed and not why; the reason survives on the icon entities the same pass creates, each pointing at its error type's own prefab.
**Nothing writes any of it to a log**, which is why a tool that will not apply leaves no trace to grep for.
Source: `src/Game/Game.Tools/ValidationSystem.cs` (the tagging and the icons), `src/Game/Game.Tools/Error.cs` and `src/Game/Game.Tools/ErrorData.cs` (the tag component and the job-local record).

**How a tool knows it is blocked.** The tool base holds a query over that error component, and the base implementation of `GetAllowApply` is true when the tool system's `ignoreErrors` is set _or_ that query is empty, and the original-deleted check also passes.
So "why will this not apply" is answered by whether that query is empty.
`ignoreErrors` is a plain public settable bool, and the developer menu's validation-bypass toggle is what writes it — but it clears only the error-query half, and the original-deleted check still gates the return.
So an apply that still refuses with `ignoreErrors` set is the second clause, not a broken toggle; `custom-tools` owns that clause and overriding `GetAllowApply` in a tool of your own, and `debug-menu` owns the menu.
Source: `src/Game/Game.Tools/ToolBaseSystem.cs` (the query and the base `GetAllowApply`), `src/Game/Game.Tools/ToolSystem.cs` (`ignoreErrors`), `src/Game/Game.Debug/DebugSystem.cs` (the toggle that writes it).

**How the reason reaches the player.** Each error type has a prefab carrying its notification icon, and a prefab whose flags disable it in the current mode is skipped, leaving an empty slot.
So **a missing icon means a disabled error prefab rather than an absent error** — and a disabled one raises no `Error` tag either, so it does not block the apply at all.
Source: `src/Game/Game.Tools/ValidationSystem.cs` (the skipped prefab and the early return before both the icon and the tag), `src/Game/Game.Prefabs/ToolErrorFlags.cs` (the flags it is skipped on).

`placement-definitions` owns the error prefab: the error type, severity and flag enumerations by name, and how to suppress an error by editing its prefab and put it back afterwards.
`TemporaryOnly` is the flag that changes what you see rather than what applies: it marks an error whose icon is deleted at apply time instead of being promoted, so it shows while previewing and is gone once the placement lands.

## Verifying the debug patch before attaching anything

Confirm the patch took before pointing a debugger at anything: an unpatched build refuses the connection in a way that reads as "the debugger cannot find the process", which sends people looking in the wrong place.
Two log lines and two file checks do it, each sourced in one of the patch's two edits.
Whether one of the two log lines going missing on its own identifies which edit failed is untested, so read them together rather than singly.
They are in [debug-patch-signals.md](debug-patch-signals.md), with the port to attach to.

Applying the patch is the setup skill's `debug-patching` reference.
This reference stops at whether a debugger can attach at all; getting a Burst-compiled job into it once attached is `performance-and-memory`.

## What this reference hands to others

`mod-lifecycle-and-ordering` owns the mechanism behind steps 4 to 7 of the order — which hook, which state, whether the system stays enabled, and the anchoring failure that logs nothing.
`navigating-the-decompile` is where a reader goes once a log line has handed them a type name or a message string, which is what the quoted strings here are for.
`city-state-and-progression` owns the notification system from the other side: the simulation's own failure-state icons, rather than a mod reporting one.
`performance-and-memory` owns what a log line costs and every lever that makes one cheap, where this reference owns where a line goes and how to change that.
It also owns the two symptoms the order above cannot take: memory growth, and a process death with no managed exception.

Every question in the order that a log cannot answer is a live-state question — whether a system is registered, whether it is enabled, whether a query matches anything, whether a patch took.
Those are answered against a running game through the sibling Unity plugin, on an install already debug-patched, and it is never a requirement.
