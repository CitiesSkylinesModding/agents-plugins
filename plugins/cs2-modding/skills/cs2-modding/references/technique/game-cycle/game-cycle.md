# The game cycle

Verified against game version 1.6.4f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

## The cycle

1. `stop`, when a C# build is next.
2. Build.
3. `restart` with `mod` set, and a `save` or `newCity` to reach a city.
4. Read what the result says of the mod's load, under "The build check" below.

**Only the restart loads a build, and on Windows a build beside a running game breaks a deployed mod with a Burst library.**
In a captured run on Linux the build exited 0, replaced the deployed files and the game stayed up on the old code, with nothing saying so.
In a captured run on Windows the build failed with `MSB3231`, unable to remove the deploy folder for the `<mod>_win_x86_64.dll` the game holds, after deleting the mod's other files from it, so the folder held no managed DLL until a build after the stop.

**A clean quit runs `OnDispose` and, past boot, saves settings, the mods' included; a forced end does neither, and no close saves the city.**
A close finishes a save in flight and otherwise loses unsaved play exactly as a kill does. (VOLATILE: the steps of `TerminateGame` — `Game.SceneFlow.GameManager`.)
Source: `src/Game/Game.SceneFlow/GameManager.cs`.

**An agent at work owns the game, and no tool asks first.**
`stop`, `restart` and `test` act on whatever process bears the game's name, a game the user launched by hand and a city with unsaved play included.
A session sharing the game with a person says what it is about to do before calling one.

**Launch through `restart`, never from a shell.**
A Steam started from the agent's shell inherits that environment, tokens included, and the game inherits Steam's.
A logged error then uploads every environment variable, and in captured runs on Linux most boots wrote a hang dump holding the environment block, most of them uploaded.
`restart` detaches the launch from the agent's environment, through the user's service manager on Linux and the desktop's Explorer on Windows.
In a captured run on Windows, a Steam that `cmd /c start` started from the tool held the tool's environment, and one Explorer started held none of it.
Source: `src/Backtrace.Unity/Backtrace.Unity.Model.JsonData/Annotations.cs` (the copy), `src/Colossal.Logging/Colossal.Logging/CustomLogHandler.cs` (the upload on a logged error).

**Where `stop` was forced because its close never left, quit from the eval, and only there.**
Call `UnityEngine.Application.Quit()` through the `unity` plugin's eval on a debug-patched game, then `detach`, then `stop` or `restart`.
A stuck game is out of reach here, since the eval needs the main thread.

## Arrival

In captured runs, a call past two minutes on Claude Code's main conversation moved to a background task and its result arrived as a notification, and on Codex CLI the harness polled with `wait`.
Either way the call is still running, so never reissue it.

## Moves on a running game

**Load through `restart`'s `save`, never with `GameManager.instance.Load` from the eval.**
In a captured run that eval came back after 60 s with its debugger session dropped, and the load stalled until the game was killed.

Read [`running-game.md`](running-game.md) before holding the camera still, which `CameraController.inputEnabled` does not do.

**With autosave on, a session can cost the user autosaves of any city.**
`GeneralSettings.autoSave`, `autoSaveInterval` and `autoSaveCount` decide it per install, and a paused game still autosaves.
An autosave carries the mod's components as the test build wrote them, then prunes older autosaves, any city's, so a session can leave a save a later build cannot read and take away the ones before it.
Tell the user before the session, and when an autosave of a real city holds a test build.
Source: `src/Game/Game/AutoSaveSystem.cs`.
(VOLATILE: the setting's names — `Game.Settings.GeneralSettings`.)

## The evidence

**`restart` and `test` keep one generation, the ended run, in `<user data>/.cs2-dev/previous-run/`, so a second call overwrites it.**
The keep is unconditional: a `restart` right after a boot that died replaces a useful run with a useless one.
Copy the folder aside, or read it, before the next call whenever it holds something the session still needs.
Crashpad prunes its reports on its own about 10 minutes into a run, and only the next `restart` or `test` makes the copy, so a report pruned before that is gone.
(VOLATILE: the prune's timing — `crashpad_handler.dll`.)

## The build check

A Burst path under `pdx_mods` in `restart`'s or `test`'s result means the subscribed copy loaded instead of your build.
(VOLATILE: the Burst line — `Colossal.IO.AssetDatabase.ExecutableAsset`; the `pdx_mods` folder — `PDX.SDK.Internal.Util.IO.PathFactory`.)

**An absent error is no proof of a clean load.**
`No Burst code logged` reads the same for a mod with no Burst library at all.
A mod never found, or disabled in the playset, reaches no init path and so logs no error: the sign is no `Loaded <assembly full name> in <n>ms` line in `Modding.log`, whose absence `restart` and `test` report. (UNVERIFIED: the silence of a disabled mod — a run with the mod disabled would settle it.)
Source: `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ExecutableAsset.cs`, `src/Game/Game.Modding/ModManager.cs`.

## In-game scenarios

The game reports success for scenarios that failed or never ran a test, and `test` passes some of those.
Read [`test-framework.md`](test-framework.md) before writing a scenario or reading its run.

## What this reference hands to others

[`diagnostics`](../diagnostics/diagnostics.md) owns every read of a run, the kept one and its crash dumps included, and which line proves what: a success-shaped line read without it proves nothing.
[`debug-menu`](../debug-menu/debug-menu.md) owns the flags `restart`'s `gameOptions` take.
[`save-serialization`](../save-serialization/save-serialization.md) owns whether a later build reads what a test build saved.
[`cs2-modding-ui`](../../../../cs2-modding-ui/SKILL.md): a UI-only change can deploy with the game open; that skill says when.
