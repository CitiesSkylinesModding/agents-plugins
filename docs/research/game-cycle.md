# The game cycle: restarting the game, driving it, and its test framework

**Baseline.** Game version 1.6.2f1, Unity 2022.3.71f1, decompile at HEAD `43da1009` ("Decompile with ilspycmd 11.0.0.9375"), read 2026-11-02 at `~/Documents/Dev/cs2-decompile`.
UI bundle citations are to `src-ui/source.js`, 140,104 lines.
Mod corpus at `~/Documents/Dev/cs2-mods`; the template at `~/Documents/Dev/cs2-greenfield-mod-template`, HEAD `0c66590`.
`cs2-dev` read as shipped at `ebc1ebe`, paths below relative to the repo root.
Live evidence comes from tickets 01 to 05 of the feature that built the server, one Linux machine, Proton Experimental `experimental-11.0-20261014`; each run is cited as a run with what it showed, as those tickets' notes are working material and do not survive the feature.
Two runs were made for this file on 2026-11-02, `camera` and `build-open`, on the debug-patched game on Linux; the runs still wanted are listed under "Runs that would settle what is open".

---

## Findings

### What this topic owns, and what it leaves

The topic is the run half of the dev loop: what surrounds the `cs2-dev` tools, and what no description of theirs can carry.

- The tools' own traps stay on their descriptions (the next section lists them, so the reference does not repeat them).
- The log catalogue, the diagnosis order and what each line proves belong to `diagnostics`, shipped at `plugins/cs2-modding/skills/cs2-modding/references/technique/diagnostics/diagnostics.md`. It already states that a relaunch destroys a crashed run's evidence, and that `Player-prev.log` is the only automatic copy (`plugins/cs2-modding/skills/cs2-modding/references/technique/diagnostics/diagnostics.md:77-79`). This topic adds what the tool keeps beside that.
- Launch flags and `runOnce.txt` as a mechanism belong to `debug-menu` (`plugins/cs2-modding/skills/cs2-modding/references/technique/debug-menu/debug-menu.md:65-68`). This topic adds only that `restart`'s `gameOptions` go through that file.
- Reading a minidump is `crash-dumps`', and nothing here teaches it.

### What each tool's description already says

From `plugins/cs2-modding/mcp/src/server.ts:111-310`, so the reference points rather than repeats.

- **`restart`.** Stops the game if it runs, keeps the ended run's logs and crash reports, launches through Steam, returns at the main menu or once the save or new city loaded and its loading screen went. The stop is `stop`'s: up to 60 s, then forced; only a close past boot saves settings; a debugger-held game takes the whole wait. Its blocking ceilings are stated. Fails at once on a game that dies and on a load falling back to the main menu; a timeout leaves the game running and says so. Unknown `gameOptions` are ignored by the game silently and whitespace is refused; `save` and `newCity` are required for load-type options; `mod` is the assembly name.
- **`stop`.** Asks once, waits up to 60 s, forces. Says clean or forced. "Only a close past boot saves settings." "A game held by a debugger takes the whole wait, then is forced." (`server.ts:154-166`.)
- **`wait_load`.** A load may not be logged yet right after its trigger; fails on a stopped game, on a death, on a fallback; a timeout leaves the game running.
- **`test`.** Passes when the game reports success, a test finished, and nothing logged an error; the game alone calls a preparation that threw a success; any error naming the mod in any log but `TestScenarios.log` fails, Unity-logged and ignored ones included; a run with no scenario fails, which is a Release build's ending; stops unless `keepOpen`.
- **`status`, `logs`, `ignore_error`.** State, reading, and silencing. `logs` with `previous: true` reads the kept run.

**Consequence for the author:** nothing above needs restating. What is left is order, reasons, and what happens between tool calls.

### 1. The cycle

**The order.** Stop, build, restart, confirm the build, read.

1. `stop`, when a C# build is next. A restart stops the game itself, so a change that does not rebuild the mod's assembly goes straight to `restart`.
2. Build.
3. `restart`, with `mod` set, a `save` or `newCity` to reach a city. A load goes through `--startGame`, which starts as soon as the game booted (`src/Game/Game.SceneFlow/GameManager.cs:645-647`, `:1241-1264`), so it races nothing in the menu (`plugins/cs2-modding/mcp/src/tools.ts:420-423`). `gameOptions` go into `runOnce.txt`, which the game reads and deletes (`GameManager.cs:443-447`) and splits on spaces into a set (`:328-339`), so duplicates collapse and order is lost.
4. Read what the result says of the mod's load (section 5).

**Whether the build really needs the game stopped is not what the template says.**
The template's reason is "the running game holds the mod's DLL open" (`.claude/skills/gfd-game-cycle/SKILL.md:15`).
The managed assembly is loaded from bytes through a stream closed at once (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ExecutableAsset.cs:232-242`), so the managed DLL is not held by that load.
The PDB is read through `LongFile.OpenRead(text).ReadAllBytes()` with no dispose (`:236`), which leaves a handle until a finalizer.
The Burst library is what is held in place for a local mod: `BurstRuntime.LoadAdditionalLibrary(text3)` is called on the deployed path (`:246-255`), and a subscribed mod's library is first copied to a temp folder (`:247-254`), a copy that reads as the game avoiding that lock on a shared cache file (inferred, no comment states it).
Verdict: the order stands, the reason narrows to "the Burst library and possibly the PDB". The claim is the template's, the source is the load path, and the load path wins.
**Run `build-open`, Linux, 2026-11-02.** With the game in a new city and a template mod with a Burst library loaded, `dotnet build -c Debug --no-incremental` exited 0 with no warning, and every deployed file changed: the managed DLL and the Burst library both took new hashes and mtimes. `/proc/<pid>/maps` then showed the Burst library as `(deleted)`, so the toolchain replaced the file rather than overwriting it, the game kept running on the old mapping, and it stayed up. **So on Linux a build beside a running game succeeds and the game goes on running the old code, with nothing saying so.** The stop before a build is not needed there; the restart after it is.
On Windows the same build fails: the deploy's `RemoveDir` meets the library the game holds open (section 8).

**What a clean quit saves.** `Application.wantsToQuit` is hooked to `WantsToQuit` (`GameManager.cs:536`, `:745-758`), which runs `TerminateGame` (`:769-844`):

- waits 30 frames, cancels running work;
- `TaskManager.instance.Complete("SaveLoadGame")` (`:794`), so **a save in flight finishes**. In the live run of ticket 01, a quit during a forced autosave ended with a whole `.cok`;
- `if (quittingState >= State.WorldReady)` saves launcher settings and calls `SaveAllSettings` (`:795-799`), and `AssetDatabase.SaveAllSettings` writes every `SettingAsset`, the mods' included (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/AssetDatabase.cs:704`);
- `m_ModManager?.Dispose()` (`GameManager.cs:802`): **a quit reaches a mod's `OnDispose` only when it is clean**, and a throwing `OnLoad` reaches it with no quit (`ModManager.cs:454-456`);
- 10 s bounds on `DisposePlatforms` (`GameManager.cs:805`) and `CompleteAndClear` (`:812`), then `Game terminated successfully` or `due to timeout` (`:825/:829`), and `GameManager destroyed` (`:786`).

**When it saves nothing.**

- A quit before `State.WorldReady`. The `State` enum runs Booting, Terminated, UIReady, BootingPrefabs, WorldDisposed, WorldReady, Quitting, Loading (`GameManager.cs:124-134`), so the guard passes during a load. `WorldReady` is set after prefabs loaded and just before `Boot completed` (`:632`, `:639`). A quit past that saves, a quit before it does not.
- Any forced end. SIGTERM and SIGKILL wrote neither line in the live run of ticket 01, and nothing in the decompile runs on them.
- **The city, ever.** No path above saves the game world. A close loses unsaved play exactly as a kill does.

`stop` tells these apart: `hasCleanShutdown` needs `^Game terminated ` at the end of `Player.log` and `GameManager destroyed` in `SceneFlow.log` (`plugins/cs2-modding/mcp/src/cycle.ts:508`), and `describeStop` reports closed clean, ended with no clean shutdown, or forced (`tools.ts:508`).

Measured in ticket 01: a close through `wine taskkill` 13.4 to 15.0 s on a loaded city; main-menu quits logged 11,275 to 11,720 ms; an eval quit mid-autosave about 17.2 s. The 60 s bound is 3.4 times the worst seen. Ticket 02: `stop` on a loaded city 15 s.
Rots: `TerminateGame`'s steps and its two log lines, `Game.SceneFlow.GameManager`.

**The agent's full control.** An agent at work owns the game.
No tool asks first, no tool takes a PID, and none takes a force flag: `stop`, `restart` and `test` find the game by process name (`pgrep -x Cities2.exe`, `plugins/cs2-modding/mcp/src/game-host.ts:13`, `:168`; `tasklist` on Windows, `:237`) and act on whatever that finds, a game the user launched by hand and a city with unsaved play included.
`stop` decides the force itself after the bound (`cycle.ts:181-214`). The one exclusivity is between calls: one `restart`, `stop` or `test` at a time, others failing at once naming it (`plugins/cs2-modding/mcp/src/tool-run.ts`), while read-only tools and `wait_load` run freely.
Consequence: asking is the agent's own judgement, not the tool's. A session sharing the game with a person states what it is about to do, since the tool will not.

**Why a launch never comes from a shell.** Two reasons, and only the first is the template's.

1. A Steam started from the agent's shell inherits that shell's environment, and the game inherits Steam's. The tool launches through `systemd-run --user --collect --quiet steam steam://rungameid/949230` (`game-host.ts:146`), which detaches the launch from the caller's environment, and its comment states why.
2. A launch from a shell gets none of what `restart` does: `--noSplash`, the options file, the wait for `Cities2.exe`, its retries, deleting `runOnce.txt` on failure (`cycle.ts:252`), and the evidence kept beforehand.

**How the environment reaches Colossal Order, the mechanism.** Two carriers, and neither needs a crash.

- **Managed reports.** `Annotations.SetEnvironmentVariables` copies every environment variable (`src/Backtrace.Unity/Backtrace.Unity.Model.JsonData/Annotations.cs:56-74`, JSON key `Environment Variables` at `:79`), through `src/Backtrace.Unity/Backtrace.Unity.Model/BacktraceData.cs:105`. Such a report goes out for every log above `Warn` unless `disableBacktrace` is set, and for `LogException` (`src/Colossal.Logging/Colossal.Logging/CustomLogHandler.cs:142-152`, `:182`). **So an ordinary logged error uploads the environment.**
- **Minidumps.** A dump holds the process's environment block. The one boot dump opened, kept in ticket 01 (139 MB), held 111 entries in UTF-16; only the names were read, among them `STEAM_COMPAT_*`, `SteamUser`, `USER`, `WINEPREFIX` and `LD_PRELOAD`.
- **Most boots write one.** In ticket 01 every boot wrote a dump 38 to 44 s in, `Simulated Exception`, top frame `BacktraceCrashpadWindows.dll`, 10 of 17 launches with ticket 01's nine counted (`crash-dumps.md`, finding 8), and 7 of 8 reports carried `{"response":"ok"}` in crashpad's `metadata`: uploaded. The hang path is `NativeClient.HandleAnr` (`src/Backtrace.Unity/Backtrace.Unity.Runtime.Native.Windows/NativeClient.cs:115-150`).

The trigger is `AnrWatchdogTimeout`: the game sets 15000 (`src/Colossal.Logging/Colossal.Logging.Backtrace/BacktraceHelper.cs:20`) after the native client copied the library's default of 5000 (`src/Backtrace.Unity/Backtrace.Unity.Model/BacktraceConfiguration.cs:19`), which a live read confirmed (`crash-dumps.md`, finding 8).
Verdict: the template's "blocked over 5 s" (`gfd-game-cycle/SKILL.md:44`) holds. The sleep is 5 s, and the watchdog compares two samples a sleep apart (`NativeClient.cs:125-142`, `:154`), so the exact blocked time at which it fires is between one and two sleeps.

Unconfirmed: that a token was actually seen in an upload. It settles nothing that matters here, since the dump held the block and the report said uploaded. That a running Steam's forwarded URL carries no caller environment is reasoned from Steam forwarding the URL to its own process, not observed; the Linux launch does not depend on it.
Rots: the watchdog value, `Backtrace.Unity.Model.BacktraceConfiguration` and `_nativeClient.AnrWatchdogTimeout` on a running game; the annotation key, `Backtrace.Unity.Model.JsonData.Annotations`.

### 2. Arrival: how a long call reaches the agent

**Claude Code, main conversation (ticket 02).** Every run past two minutes moved to a background task at 120 s, and its result arrived later as a notification, with the agent free to work meanwhile. Observed: `restart` to the main menu 45 s (in turn); on a large save 226 and 211 s; a new city 104 s; a game killed mid-boot failed at once; a game frozen mid-boot failed at 600 s and was left running, its `stop` forced at 68 s; a launch that never happens took three attempts, 3 minutes.

**Claude Code, headless (`-p`, ticket 03).** Main menu in 64 s, in turn. Nothing over two minutes was run there. By the documentation `-p` and subagent calls stay blocking, so they get no auto-background; not observed.

**Codex CLI (ticket 03).** `codex exec` 0.164.0 through code mode, read-only sandbox, approval never, no prompt: closed a running game clean in 14 s and reached the main menu at 62 s. The call took 76 s; the script yielded at 31 s and the harness polled twice with `wait`, the second returning. Cost 75,823 input tokens (70,528 cached) and 241 output.
An earlier probe with a plain sleep: 330 s against `tool_timeout_sec` 400, default limit 300 s, nine polls, 179k input tokens, and a timeout sends no cancellation. Code mode was the only mode on that account.

**Not observed, on either harness.**

- A long call on Codex against the shipped `tool_timeout_sec` of 2460 (`plugins/cs2-modding/.codex-plugin/mcp.json`). The value was only read back from `codex mcp get`.
- Whether `test`'s progress notifications every 60 s (`HEARTBEAT_SECONDS`, `plugins/cs2-modding/mcp/src/scenario-run.ts:29-34`) reset Claude Code's 30-minute stdio silence abort. The code comment asserts the abort; the reset is unverified, and the ceiling of 1800 s sits at that limit.
- Windows on either harness: ticket 10 drove the server with a bare MCP client (section 8).

**Errors between calls.** On Claude Code the monitor (`plugins/cs2-modding/monitors/monitors.json`) delivers while the agent drives other tools: in ticket 04, a notification 4 s after an error during a `game_eval` loop, three during a 7.5-minute storm, and the next `status` did not repeat it. Codex has no monitor, and every result's hint carries the same news (`plugins/cs2-modding/AGENTS.md`, "The `cs2-dev` server"). Status lagged about 3 s after a launch.

Rots: harness behaviours above belong to the harnesses and to no game file, so they take no marker; a version sweep could not close one.

### 3. The running-game moves

**`menu.loadGame`.** Declared at `src/Game/Game.UI.Menu/MenuUISystem.cs:476`, `TriggerBinding<LoadGameArgs, bool>`, arguments at `:108-135`: `saveId`, `cityName`, `options`, `gameMode`. The frontend calls it with the args and the dismiss flag (`source.js:47811-47813`). The reference does not ship this route: a listing and a pointer each drew findings on the frontend's semantics, so `restart`'s `save` is the one taught. The frontend's call is the shape to use, `s` being the entry in `menu.saves`: `engine.trigger("menu.loadGame", {saveId: s.id, cityName: s.cityName, options: s.options, gameMode: s.gameMode || "NormalMode"}, false)` (`source.js:89640-89642`, `:89661-89664`, `:89699`). It merges `menu.defaultGameOptions` under the entry's options, a no-op on six local saves, each carrying all five keys. The template's `options: {}` and fixed `NormalMode` override both for the next save. Passing `true` persists a dismissal of the unsaved-city warning. Not run live. The `id` shape `User/<guid>` was read from a live eval on 2026-09-21; `src/Game/Game.Assets/SaveGameMetadata.cs:54` sets it.

The handler (`SafeLoadGame`, `MenuUISystem.cs:784`) enqueues on `SaveLoadGame`; `LoadGame` looks the save up with `m_SavesBinding.value.Find` (`:797`) and dereferences the result (`:798`).

**The NRE is real, and is not thrown to anyone.** With the id absent, `saveInfo` is null, the `NullReferenceException` is caught at `:815` and logged through `COSystemBase.baseLog.Error` (`:817`). So the trigger returns normally, no load starts, and an Error lands in `SceneFlow.log`. A wrong `saveId` takes exactly the same path as a list not yet filled, so the log does not tell them apart.
Verdict: "it throws a `NullReferenceException`" (`gfd-game-cycle/SKILL.md:26`, `docs/ROADMAP.md:1145` at `ad4a7b4`) is true of the frame and false of the caller. Decompile wins.

**When the list fills.** There is no timer and no constant.
`m_SavesBinding` is written only by `UpdateSaves` (`MenuUISystem.cs:558-562`, through `MenuHelpers.UpdateMeta`, `src/Game/Game.UI.Menu/MenuHelpers.cs:85-104`), and that runs on two events: `AssetDatabase.global.onAssetDatabaseChanged` for `SaveGameMetadata` (`MenuUISystem.cs:381`, dispatched to the main thread at `:578-581`) and `PrefabSystem.onContentAvailabilityChanged` (`:374`, `:495-500`, fired at `src/Game/Game.Prefabs/PrefabSystem.cs:480/517`). `OnCreate` does not fill it.
`UpdateMeta` drops an asset whose `target` is null (`FilterSaves`, `MenuUISystem.cs:564-576`), and boot awaits asset caching before `Boot completed` (`GameManager.cs:594-595`, `:612`).
So the order against `MainMenu reached` is not settled by the source. Verdict: "some 30 s after `MainMenu reached`" has no decompile support, and on the source it is not a constant at all; treat it as an observation of unknown spread.
**The durable form:** wait on the state, not on a time. The list is the binding `menu.saves`, so a page can read it (`docs/SOURCES.md` entry 9 gives the `engine.on` subscription form). Send the trigger once the save's id is in it.
**The better move:** a load from a stopped or restarting game goes through `restart`'s `save`, which skips the menu entirely. `menu.loadGame` is for changing save in a game kept running, the one case worth its timing.
After the trigger, `wait_load` follows the load from the main menu; from a city it passes on the old load until a new `Loading mode` line exists, since it takes the last one (`plugins/cs2-modding/mcp/src/cycle.ts:397-407`).
Unconfirmed: how long the list takes; settled by `saves-list`.
Rots: the trigger name and argument names, `Game.UI.Menu.MenuUISystem` and `LoadGameArgs`; the id shape, `Game.Assets.SaveGameMetadata`.

**The `GameManager.instance.Load` trap, as observed.** In one run on 2026-09-21: an eval of `Game.SceneFlow.GameManager.instance.Load(GameMode.Game, Purpose.LoadGame, Hash128.Parse(...))` came back after 60 s with `the game took a call and did not answer it within 60s, so the debugger session is being dropped`. `SceneFlow.log` stopped at `Starting game from '<save>'` and `Loading mode Game with purpose LoadGame`, with no further line for over 4 minutes. The game was killed; `menu.loadGame` then loaded the same save.
The next line due was `HandleLocaleCaching`, after `await UnityTask.WaitForGPUFrame()` (`GameManager.cs:1097-1100`; `src/Colossal.Core/Game.Threading/UnityTask.cs:30-38`, which waits on an `AsyncGPUReadback` callback, so a rendered frame).
An eval runs on the main thread inside a debugger invoke (`plugins/unity-devtools/sdb/Invoker.cs:432-448`), and it does not await a returned `Task`: nothing in the eval path holds one. So the part of `Load` before its first real suspension runs inside that invoke.
**What is established:** the invoke did not return in 60 s, and the load did not progress.
**What is not:** the exact mechanism. An async method should hand back at its first `await`, so "holds the main thread the load needs" (`gfd-game-cycle/SKILL.md:26`, `docs/ROADMAP.md:1145` at `ad4a7b4`) is a plausible reading rather than a demonstrated one, and "the game must be killed" rests on one run in which no `stop` was tried.
Verdict: ship the move, "never start a load from the eval; load through `restart`'s `save`", with the observation as its evidence, not the mechanism. Settled further by `eval-load`.
The same is expected of `TestScenarioSystem.RunScenario` from the eval whenever the scenario's synchronous prefix starts a load (the template's reading, `gfd-game-testing/SKILL.md:55`; reasoned here, not run).

The template's rule "`await this.WaitFrames(2)` before `GameManager.instance.Load`" (`gfd-game-testing/SKILL.md:55`) is only needed for a scenario started from the eval: under automation `OnPrepare` runs synchronously from `GameManager.Update` → `TestScenarioSystem.Update` → `RunScenario` (`src/Colossal.TestFramework/Colossal.TestFramework/TestScenarioSystem.cs:118`), where the caller already is the game loop. It costs nothing to keep. Unconfirmed that it is sufficient from the eval, as no run compared them.

**The camera freeze does not hold on the gameplay camera.**
`CameraController.inputEnabled` exists at `src/Game/Game/CameraController.cs:248`, an auto-property implementing `IGameCameraController` (`src/Game/Game.Rendering/IGameCameraController.cs:18`).
**Nothing reads it on that class.** A search of `src/Game` and `src/Colossal.Core` finds its reads only in `src/Game/Game/OrbitCameraController.cs:227` and `src/Game/Game/CinematicCameraController.cs:192`, and its writes only in `src/Game/Game.UI.InGame/CinematicCameraUISystem.cs:80-81/507-508`, which target those two controllers. `CameraController.cs` holds the declaration and no use of it.
`CameraController.UpdateCamera` gates all input, edge scrolling included, on `m_CameraMap.enabled` (`CameraController.cs:351`, edge scrolling at `:356`), and `m_CameraMap` is `InputManager.instance.FindActionMap("Camera")` (`:326`).
Verdict: "`CameraController.inputEnabled = false` stops edge scrolling and the camera keys" (`docs/ROADMAP.md:1146` at `ad4a7b4`, `gfd-game-cycle/SKILL.md:49`) does not hold from the decompile. The line cite `:248` holds; the effect does not. A corpus search finds the property used only on the cinematic controller. A captured take that looked frozen proves nothing, since a game without focus or with an idle pointer does not move either.

What does hold, from the source:

- **The whole map.** `ProxyActionMap.enabled` is true only when no barrier on it is blocked (`src/Game/Game.Input/ProxyActionMap.cs:130-141`). `InputManager.instance.CreateMapBarrier("Camera", "<name>")` (`src/Game/Game.Input/InputManager.cs:2120`) returns an `InputBarrier` whose `blocked = true` disables it (`src/Game/Game.Input/InputBarrier.cs:27-41`), and the branch at `CameraController.cs:351` is then skipped whole. The game itself uses this: `src/Game/Game.UI/InputBindings.cs:47/87`, blocked while the frontend observes `input.cameraBarrier` (`source.js:31186`, `:34512`).
- **Edge scrolling alone.** `CameraController.edgeScrolling` (`CameraController.cs:278`) is a plain settable property, read at `:356`, set from settings once at `Awake` (`:314-319`), toggled by the debug UI (`src/Game/Game.Debug/CameraDebugUI.cs:224-229`). `false` stops the edge and leaves the keys.

The barrier is the one that answers the need.
**Run `camera`, 2026-11-02, in a new city.** `var b = Game.Input.InputManager.instance.CreateMapBarrier("Camera", "agent-freeze"); b.blocked = true` took `FindActionMap("Camera").enabled` from true to false. A second eval, holding no reference, reached it through the internal `barriers` (`ProxyActionMap.cs:30`): `var l = System.Linq.Enumerable.ToList<Game.Input.InputBarrier>(m.barriers); l[l.Count - 1].Dispose()`, the type argument written because the eval does not infer it. `enabled` went back to true. The list held two barriers, the frontend's and this one, last. No key was held and no position read: the run proves the map's state and the undo, and `CameraController.cs:351` proves what the state gates.
A map barrier does not touch the tool map, so a click still acts.
Rots: `Camera` map name, `Game.Input.InputManager.kCameraMap`; the read sites, `Game.CameraController`.

**The autosave.** `src/Game/Game/AutoSaveSystem.cs`, in the `LateUpdate` phase (`src/Game/Game.Common/SystemOrder.cs:77`).

- **Interval.** A setting, not a constant. `GeneralSettings.autoSave`, `autoSaveInterval`, `autoSaveCount` (`src/Game/Game.Settings/GeneralSettings.cs:92/96/100`), enum values 60, 120, 300, 600, 1800, 3600 s (`:35-43`). `SetDefaults` gives off, one minute, one (`:192-200`); `SetNewPlayerDefaults` gives on, five minutes, two (`:202-208`), and applies only when no settings file exists yet, or on a settings reset (`src/Game/Game.Settings/SharedSettings.cs:57-63`, `:121-130`).
- **On this machine** `Settings.coc` reads `"autoSaveInterval": "FiveMinutes", "autoSaveCount": "Two", "autoSave": false` under `General Settings`: off.
- **Verdict.** "The game autosaves every five minutes" (`gfd-game-cycle/SKILL.md:28`) is the new-player default, not a fact about a given install. The setting wins. The two `30-October-*` autosaves in this user's saves came from `autoSaveNow` forced in ticket 01, a developer switch (`GeneralSettings.cs:105`).
- **When.** Only in `GameMode.Game`, only with `autoSave` on, not during a benchmark (`AutoSaveSystem.cs:85-95`), on real time since startup measured from load completion (`:97-105`). The watch arms at the end of a `LoadGame` or `NewGame` load and only if `autoSave` is on then (`:75-82`), or at a settings Apply once it is on (`:42-47`), while turning it off takes effect at once. Nothing in the check reads the simulation speed, so a paused game still autosaves.
- **What.** A full save named `dd-MMMM-HH-mm-ss` (`:147`) into `AssetDatabase.user`, enqueued on `SaveLoadGame` (`:142-167`). Prunes older autosaves down to `autoSaveCount` (`:107-129`).
- **Why it matters to a test build.** The save carries the mod's serialized components as that build wrote them, and `GameManager.Save` also records it as `lastSaveGameMetadata` (`GameManager.cs:995`), so the game's continue target becomes that save (reasoned from that field and `LauncherContinueGame`, `GameManager.cs:655`); the prune then removes the player's earlier autosaves. A session on a real city can leave a save whose mod data a later build may not read, and take away the ones before it.
- **Off.** The option; or edit `Settings.coc` with the game stopped.

The move: tell the user before the session, since the prune spans every city's autosaves (`AutoSaveSystem.cs:115-120`) and a throwaway save does not stop it. A toggle from the eval is not the move: the timer runs on while it is off, so setting it back in a city fires an overdue autosave (`:99`), and a clean quit persists the off. Tell the user too when an autosave of a real city holds a test build.
Rots: the setting's name and defaults, `Game.Settings.GeneralSettings`; the phase, `Game.Common.SystemOrder`.

### 4. The evidence

**What is kept, and where.** `<user data>/.cs2-dev/previous-run/`: `Logs/`, `Player.log`, and `crashpad/reports` holding the reports with mtime at or after the run's start (`plugins/cs2-modding/mcp/src/evidence.ts:23-53`). Built aside in a `previous-run.next-*` folder and swapped in, so a failed swap never launches. `logs` with `previous: true` reads it.

**When.** `endRun` runs stop, keep, then a last hint, for `restart` and for `test` (`tools.ts:388-418`). So **`test` replaces it too**, the template's "second restart" being any second call of either. `stop` alone keeps nothing, and needs not: the run's files stand on disk until the next launch.

**One generation.** Each call replaces the folder: a second `restart` overwrites the copy of the run before last, whatever that run showed. A crash from run A read after two restarts is gone.
A `restart` right after a boot that died replaces a useful run with a useless one, as the keep is unconditional (`evidence.ts:23`).
The move: copy `.cs2-dev/previous-run` aside, or read it, before the next `restart` or `test` whenever it holds something the session still needs.

**What outlives it, and what does not.** `Player-prev.log` is the engine's one copy (`plugins/cs2-modding/skills/cs2-modding/references/technique/diagnostics/diagnostics.md:79`). Crashpad prunes reports on its own, about 10 minutes into a run (`crash-dumps.md`, finding 2). Only the next `restart` or `test` makes the copy (`plugins/cs2-modding/mcp/src/tools.ts:403-405`), so a report pruned before that is gone.
Verdict: "Crashpad prunes them within the hour" (`gfd-game-cycle/SKILL.md:44`) is wrong: a report lasts until a run reaches about 10 minutes with it already on disk, days if runs are short (`crash-dumps.md`, finding 2).
Verdict: "Every log is written afresh at each launch" (`gfd-game-cycle/SKILL.md:43`): a `Logs/<Name>.log` is truncated at its session's first message and never deleted, so a logger silent this run leaves last run's file standing (`plugins/cs2-modding/skills/cs2-modding/references/technique/diagnostics/diagnostics.md:68-79`). `diagnostics` wins, and it follows that `previous-run/Logs` can hold files older than the run it copies.
Crash reports are in `.cache/backtrace/crashpad/reports` (`plugins/cs2-modding/mcp/src/hint.ts:56-58`). A hint says "A new crash report appeared, which may be a hang snapshot" (`hint.ts:246`), and most boots write one, so a report alone means nothing.

### 5. The build check

**What the log shows.** `Logs/Modding.log` carries, per mod:

- `Loaded additional Burst code <path>` (`ExecutableAsset.cs:256`), whose path is the loaded asset's own. On this machine: `.../Cities Skylines II/Mods/<name>/<name>_win_x86_64...` for a local mod, `.../.cache/Mods/pdx_mods/<id>_<version>/...` for a subscribed one. **Only a mod with a Burst library writes it** (`:243-245`).
- `Loaded <assembly>, Version=..., in <n>ms`, with no path.

`restart` with `mod` reports `Mod X: Burst code loaded from <path>` or `No Burst code logged for X.`, and any `Error initializing mod X` line (`describeModLoad`, `tools.ts:446`).
Rots: the Burst line, `Colossal.IO.AssetDatabase.ExecutableAsset`; already marked in `tools.ts:443`.

**An absent error is no proof.**
`No Burst code logged` does not mean a failed load: a mod without a Burst library says the same.
A mod that was never found or is disabled in the playset never reaches the init path (the never-found half from `ModManager.cs`, which loads only what `GetModAssets` found; the disabled half reasoned), so no error line exists to be absent: the sign is no `Loaded <assembly full name> in <n>ms` line in `Modding.log`. The `Enabled Mods` block lists the playset alone, so it cannot serve.
`diagnostics` owns which line proves what, and the shape doc's own worked example is that a `Loaded` line proves nothing. The reference points there.

**Local against subscribed.** When a mod exists both ways, `GetUniqueVersionAsset` orders `isLoaded`, then `isLocal`, then `version`, descending (`ExecutableAsset.cs:181-191`), and `isLocal` is `database == AssetDatabase.user` (`:153`). So **a local copy wins over a subscribed one by default, a higher subscribed version included**, for assets the game groups as duplicates (`:357`, the grouping not read further).
Verdict: the template's step 4 (`gfd-game-cycle/SKILL.md:22`) is a good check and reads as though a subscription could shadow a local build freely. From the source that takes `isLoaded` on the subscribed copy or a difference in identity.
Unconfirmed: whether a subscribed copy can win in practice; the path check stays, since it costs one line of a result. Settled by `shadow`.

`test`'s stale-build warning compares deployed `<localMods>/<mod>/<mod>.dll` with the sources (`scenario-run.ts:108-137`); it says nothing of which copy loaded.

### 6. The test framework

Sources: `src/Colossal.TestFramework/Colossal.TestFramework/`, shortened to the file name below.

**The contract a scenario meets.**

- Base: `public abstract class TestScenario`, public constructor. `protected static ILog log`, `World`, virtual `OnPrepare`, `OnCleanup`, `RunTestMethods`, `WaitFrames(int = 30)`, `SendStatisticValue`.
- Descriptor: `[TestDescriptor(string description, Category category, bool skipBeta = false, TestPhase testPhase = TestPhase.Default, bool disabled = false)]`, `Inherited = false` (`TestDescriptorAttribute.cs:5`).
- Methods: `[Test]`, `[TestPrepare]`, `[TestCleanup]`, each returning `void` or `Task` with no parameters (`TestScenario.cs:133-148`). A `Task<T>` is assignable and passes.
- Registration: `TestScenarioSystem.instance.scenarios[descriptor.description] = new TestScenarioSystem.Scenario { category, testPhase, test = type, disabled }`.
- Reference: `Colossal.TestFramework.dll` sits in `Cities2_Data/Managed`; a mod references it like `Game` (template `Greenfield/Greenfield.csproj:48`).

**The registry, usable with no template.** The game discovers its own scenarios once, in a constructor at `GameManager.cs:588` (`RegisterScenarios`, `TestScenarioSystem.cs:140-156`), before mods load at `GameManager.cs:624`, so a mod is never discovered and must add itself.
`scenarios` returns `m_Scenarios` itself (`:41`), the same object the automation enumerates (`AutomationClientSystem.cs:366`) and the debug UI reads, so writing into it from `OnLoad` needs no reflection.
Verdict: the roadmap's "registering needs no reflection" holds, and its "unless the order matters" does not. `SortScenarios` is public static and returns a new dictionary, which a caller can copy back with `Clear` and re-adds on the live `scenarios`, so re-sorting needs no reflection either. An entry added after the sort runs after the game's, in insertion order, ignoring `testPhase`.
Enumerating an assembly's types with `typeof(TestScenario).IsAssignableFrom(t)` and reading the descriptor is the whole of it (template `Greenfield/Scenarios/ScenarioRegistry.cs`). Write it from `OnLoad`: a write after the automation took its enumerator invalidates it, and its `MoveNext` then throws in `Update` each frame (reasoned from `Dictionary`'s version check; `OnLoad` runs well before).

**A base class, usable with no template.** It needs three things, each answering a behaviour below:

- in the constructor, `TestScenarioSystem.ConsumeErrorCount()` (`TestScenarioSystem.cs:55`), logging how many it dismissed, or pending boot errors abort the scenario;
- `OnCleanup` guarded against what `OnPrepare` never built, since cleanup runs when prepare did not;
- a helper for a new city: `WaitFrames(2)`, then `CityConfigurationSystem` overrides, then `GameManager.instance.Load(GameMode.Game, Purpose.NewGame, map)`, and `GameManager.instance.MainMenu()` in `OnCleanup` when `gameMode is GameMode.Game` (template `Greenfield/Scenarios/ModTestScenario.cs`).

**Debug-only is the mod's own arrangement, and the game has no such gate.** No `isDebugBuild` check stands in the framework's run path. The template gets it from the csproj: `<ItemGroup Condition="'$(Configuration)' != 'Debug'"><Compile Remove="Scenarios\**" /></ItemGroup>` (`Greenfield/Greenfield.csproj:85-87`) and an `#if DEBUG` around `ScenarioRegistry.Register()` (`Greenfield/Mod.cs:88-91`). A Release build therefore carries none, and the `test` tool can tell that only by the run's ending (`scenario-run.ts:42-47`): ticket 05 saw `no scenario ran` 60 s after launch, each game scenario skipped, then `Finished`.
Verdict: the template comment that the runner "tells a Debug build by this class's name" (`Greenfield/Scenarios/ScenarioRegistry.cs:12`) is superseded; `test` makes no such check.
Unconfirmed: that scenarios run on a stock, unpatched game. No gate was read; every run was on the debug-patched install. Settled by `stock`.

**The run path under automation.** `--categoryFilter=<x>` (`AutomationClientSystem.cs:225`, `Enum.Parse` with `+`, numbers work; a bad value throws out of the constructor to `GameManager.cs:667` as Fatal). State waits unless the filter is `None` (`AutomationClientSystem.cs:285`), and starts 5 s after the first main menu (`:179`, `OnMainMenuReached`, `GameManager.cs:569-575`). In ticket 05 a Debug template scenario passed in 117 s.
**A `--startGame` load holds it back:** only a load completing in `GameMode.MainMenu` calls `OnMainMenuReached` (`GameManager.cs:569-575`), so the automation waits for a main menu that a load at launch never reaches. A scenario loads what it needs instead.
Rots: `Starting test scenarios.`, `Finished test scenarios.`, and the start and finish lines, marked in `plugins/cs2-modding/mcp/src/scenario-log.ts:56-80`.

**The false success.** `TestScenario.Run` (`TestScenario.cs:86-117`) catches everything from `InitializeTestScenario`, `CheckForErrorsAtStart`, `OnPrepare` and `RunTestMethods` at `:103` into `HandleException`, which logs and sends a message that goes nowhere without a client (`AutomationClientSystem.cs:558`). Scenario counts are added only per test (`TestScenario.cs:235-236`).
So every one of these reaches `FINISH TEST SCENARIO (Success, exceptions: 0, errors: 0)` (`TestScenarioSystem.cs:102-103`): a throw in `OnPrepare`, the errors-at-start abort, two `[TestPrepare]` methods, an error logged rather than thrown in `OnPrepare`, an error logged after the last test.
`test` catches the first three through "it ran no test" (`scenario-log.ts:189-191`). **A logged-not-thrown preparation error, or one after the last test, passes the game and the "ran no test" rule; `test` catches it only if the entry sits in `TestScenarios.log` at error level (`:199`) or names the mod** (`scenario-run.ts:237-238`).
A scenario with no `[Test]` also fails there, correctly.

**`disabled: true`.** `disabled` is the descriptor's fifth parameter; no game scenario sets it. The framework skips only prepare, test and cleanup bodies (`TestScenario.cs:166`), logs `START TEST: <name> (Disabled)` (`:165`) and `FINISH TEST (Success)` (`:238`). **`OnPrepare` and `OnCleanup` still run**, which the ticket's wording left out. The automation does not filter on it (`AutomationClientSystem.cs:374`), so `test` counts it passed: `testFinishPattern` matches. Line cites `TestScenario.cs:166` and `:238` hold.

**The aborted scenario.** `CheckForErrorsAtStart` (`TestScenario.cs:278-285`) throws `InvalidProgramException("Errors found at test start. Count:\n{num}")` when errors are pending since the last scenario, boot errors of the game and of other mods included. It is a false success as above. The count is static (`TestScenarioSystem.cs:60-68`).

**`[TestPrepare]`, one.** `CollectTestMethods` (`TestScenario.cs:133-148`) uses a single-method lookup for prepare and cleanup, and two throw `InvalidOperationException("More than one method found with attribute ...")` (`src/Colossal.Core/Colossal.Reflection/ReflectionUtils.cs:374-377`), wrapped in `TestScenarioException("Error collecting scenario ...")` (`TestScenario.cs:146`). This runs after `OnPrepare`, runs no test, and reports Success. The same holds for `[TestCleanup]`.
The lookup uses Instance, Static, Public, NonPublic (`ReflectionUtils.cs:353-379`): a **private** `[Test]` or `[TestPrepare]` on a base class is invisible and silently never runs, while a protected one counts toward the two. A wrong signature is silently no test.
Verdict: the template's "takes one `[TestPrepare]` per scenario, base classes included, and throws on two" holds for inherited visible members, and the throw comes out as a success. Decompile wins on the scope.

**The cleanup traps, which no source named.**

- `OnCleanup` runs when `OnPrepare` never did, so it must tolerate nothing built.
- `IsFinished = true` (`TestScenario.cs:116`) runs only if the `finally` does not throw. **A throw in `OnCleanup` leaves `IsFinished` false, and `Run`'s task is unobserved (`TestScenarioSystem.cs:136`), so `FINISH` is never logged and the automation stays in `RunningScenario`**: `test` ends `stuck` after 360 s (`scenario-run.ts:40`), with nothing naming the cause.
- A throwing `[TestCleanup]` (`TestScenario.cs:199-212`, in a `finally`) escapes the loop, skips later tests, leaves that test uncounted and logs no `FINISH TEST`.
- A constructor throw comes through `Activator.CreateInstance` (`TestScenarioSystem.cs:134`) as `TargetInvocationException` out of `Update` (`GameManager.cs:716`): `START` is logged, no `FINISH`, the state stays queueing, so `test` says it did not finish.

**The main-menu exception.** A system's exception lands via `src/Game/Game/UpdateSystem.cs:188-197` in `SceneFlow` at Critical and in `Player.log`, naming the mod only in its stack, and at any time, not only the main menu. A pending one then aborts the next scenario. `test` fails on it through the stack match (`scenario-run.ts:237-238`).

**"Any logged error fails the test under way" narrows.** The per-test window runs from the test's start; errors logged in `OnPrepare` are cleared at `TestScenario.cs:163-164`, so they are outside it, and so is the time after the last test. The template's line (`gfd-game-testing/SKILL.md:40`) holds inside a test only.

**The live dictionary and the category.** The filter at `AutomationClientSystem.cs:374`, with a filter set: skip when `category == Debug` or `(category & filter) != category`. So **all of a scenario's category bits must lie in the filter**, and `Category.None` (0) runs under every filter. Skips are logged as `Skipping <category> scenario: <name>` to `Automation.log`. Cite `:374` holds.
`Category.cs` declares None 0, Debug 2, Serialization 4, Timings 8, General 16, Gameplay 32, QA 64, RunInAllCategories 128 (no special logic reads it), AssetDatabase 256, AssetPipeline 512, IO 1024, Performance 136, Runtime 1972, Nightly 128, Default 1980.
`test` runs a category as a whole, and it judges every scenario in the log with no mod filter (`scenario-log.ts:90`). **So a second local Debug mod on the same bit joins the run and its failures fail it.** Fix: a distinct bit per mod, `(Category)(1 << 20)` being only the template's default. Not exercised live.
The developer menu's tab needs `--qaDeveloperMode` (`src/Game/Game.Debug/TestsDebugUI.cs:15`, `GameManager.cs:376`) and lists `category.ToString()`, so a custom bit shows as a number. Rots: the enum's values, `Colossal.TestFramework.Category`; the filter expression, `AutomationClientSystem`.

**`IgnoreLogForTestResult`.** Set on the scenario (`TestScenario.cs:53`), it guards only `OnErrorOrHigher` (`:268`); `OnException` (`:261`) is not guarded. `ILog.Error(ex)` goes through `OnErrorOrHigher` alone (`CustomLogHandler.cs:164-167`), while an uncaught exception or `Debug.LogException` reaches `OnException` (`:176-187`). The static count (`TestScenarioSystem.cs:60-68`) is unguarded.
With it set, a thrown test's `HandleException` log is ignored and the test logs `FINISH TEST (Success)` though `Exception` is set (`TestScenario.cs:226-229` only sends).
**Verdict on the ticket's "keeps an error out of the game's counts while `test` still fails on it":** over-rounded. `test` fails only when the entry is in `TestScenarios.log` at error level, which `HandleException`'s is, or when it names the mod. **An error of the game's or another mod's under the flag fails neither the game's count nor `test`.** Nothing in the decompile, the template or the corpus sets it (the decompile's only hits are `TestScenario.cs:53` and `:268`; the ticket's range `:266-275` holds).

### 7. The trunk

The trunk's "Going deeper" list at `plugins/cs2-modding/skills/cs2-modding/SKILL.md:112-126` is alphabetical by label, so the line goes after "ECS in this game" and before "Localization":

```markdown
- [The game cycle](references/technique/game-cycle/game-cycle.md) — restarting the game on a build and driving it to a city: the order of a cycle, what a clean quit saves, the moves a running game takes, what a run leaves behind, and the game's own test framework.
```

The current description (`SKILL.md:3`) has no running trigger. Proposed, with its other clauses unchanged:

> How Cities: Skylines II is built and how a mod changes it. Use when writing or debugging CS2 mod code, when deciding how to change something the game simulates, when locating the systems, components or prefab data behind a game mechanic, when restarting the game after a build or running in-game tests, or when a mod runs at the wrong moment, breaks another mod, or does not survive a save.

The monitor starts on this skill's invocation (`monitors/monitors.json`, `when: on-skill-invoke:cs2-modding:cs2-modding`), so this trigger also decides whether a restarting agent gets its notifications.

### 8. The Windows half

**Run `windows`, ticket 10, 2026-11-03.** The `windows-vm` skill's `windows-gpu` VM, the laptop's own Windows 11 25H2 install in French, on its RTX 4070, game 1.6.2f1 with the debug patch. The shipped bundle ran under Node 22.23 inside the signed-in desktop session, served over TCP and driven from Linux by an MCP client over SSH; no harness was in the loop. The desktop session matters: run from the SSH session, `taskkill` without `/F` answers that the process can only be ended by force.

- **`taskkill /IM Cities2.exe` without `/F` is a clean quit.** `stop` closed the game in 11 s from the main menu and in 15 s from a loaded save, both reported clean, both with `GameManager destroyed` in `SceneFlow.log` and `Game terminated successfully` in `Player.log`, and the settings files (`*.coc`) rewritten at the close.
- **`taskkill /F` is the force** (`game-host.ts:258-260`). On a game whose threads were all suspended with `NtSuspendProcess`, `stop` waited the bound and reported it forced after 63 s.
- **The launch carried the agent's environment, and goes through Explorer now.** The server ran with a canary variable. With Steam closed, `cmd /c start steam://rungameid/949230` started a Steam whose environment block held it, read from the process's PEB. `launch` now runs `explorer.exe steam://rungameid/949230` (`game-host.ts:241-247`), which hands the URL to the desktop's running Explorer: with Steam closed again, neither Steam nor the game held the canary. Explorer exits 1 after a successful hand-off, so the exit code is not read. Windows boot dumps hold the game's environment block too (`USERNAME=` in each of three read), so the leak reached uploads there as on Linux.
- **A crash report held open failed the restart.** With the newest report opened with no sharing, the committed bundle failed `restart` at the evidence copy with `EBUSY: resource busy or locked, copyfile`, before any launch; the copy now skips a report another process holds, as it skips a pruned one (`evidence.ts:101-107`), and the same hold let the fixed bundle launch.
- **`restart`** reached the main menu in 337 s and loaded `Colossal City` in 226 s through `--startGame`, the guid read from the `.cok`, each reporting the mod's Burst path. A launch with `Cities2.exe` renamed failed after 3 launches and 185 s with the Steam hint, and `runOnce.txt` was gone after it; Steam showed `Missing game executable` once per launch.
- **`test`** passed the template's two scenarios in 341 s with `keepOpen`, and reported the previous run, which the Linux OOM killer had ended with the VM, as gone with no `cs2-dev` stop.
- **`status`, `logs`, `wait_load`, `ignore_error`** read the stopped game, the main menu and a city, `previous: true` included; `wait_load` failed at once on the stopped game.
- **Paths.** The defaults held: user data under `<home>/AppData/LocalLow/Colossal Order/Cities Skylines II`, reports under `.cache/backtrace/crashpad/reports/` there, local mods in its `Mods`.
- **`tasklist` on a French Windows.** PIDs parsed, and the localised no-match text read as a stopped game.
- **Reapers** are empty on Windows (`game-host.ts:239`), so "Steam still cleaning up" never shows.
- **`build-open` on Windows.** With the game at the main menu and the template mod loaded, `dotnet build -c Debug --no-incremental`, its UI step skipped, failed with `MSB3231: Unable to remove directory ... Access to the path 'Greenfield_win_x86_64.dll' is denied` at `Mod.targets(111)`, the `RemoveDir` of `DeployWIP`. It had deleted every other deployed file first, so the folder held the Burst library alone. Overwriting that library by hand failed the same way.
- **Boot dumps.** Three read on Linux with `minidump-stackwalk`: `Simulated Exception`, frame 0 in `BacktraceCrashpadWindows.dll`, Thread 0 unnamed and equal to `thread.main`, the module table as on Proton with native `d3d11.dll` and `dxgi.dll` and NVIDIA's `nvwgf2umx.dll` in place of DXVK. Every boot wrote at least one, four in the boot onto the save.
- **The prune.** About 11 minutes into a run, crashpad cut the folder from 16 reports to the newest 3.
- **A crash.** `UnityEngine.Diagnostics.Utils.ForceCrash(ForcedCrashCategory.AccessViolation)`, called through the `unity` plugin's eval, wrote a report. `minidump-stackwalk`, run on Windows from its prebuilt release, read `EXCEPTION_ACCESS_VIOLATION` with Thread 0 `(crashed)`, and WinDbg's `cdb` opened it. The next `restart` kept it in `previous-run` with the run's other reports. An access violation on a thread injected from outside wrote no report anywhere.
- **Held reports.** Polled each second for 15 s after the injected death, after the crash and after a clean `stop`, no report was ever held without sharing, so the copy's EBUSY skip guards against holders other than crashpad.
- **The logs' mtime.** Over 3 minutes of boot, Node's `stat` saw the logs grow 28 times, the mtime moving with each, so the quiet window reads a current time.
- **Stack shape.** The Windows `Player.log` writes an error's stack as `0x… (Mono JIT Code)` frames, and the scanner kept the error one entry.
- **A console per command.** A server started with no console gave the child of each `run()` a visible console window; `windowsHide` on the spawn (`game-host.ts:308`) leaves it none.

Unconfirmed on Windows: Visual Studio opening the dumps; how a call arrives on either harness, as none was in the loop.

### The eval quit fallback

`UnityEngine.Application.Quit()` from the `unity` eval, then `detach`, then `stop` or `restart`. For a machine where `stop` was forced because its close never left, and never a cycle step.

**When it applies.** `stop` names it itself: "With the unity plugin installed, quit from its eval first" (`describeStop`, `tools.ts:531-535`, on `closeFailure`). It needs the unity plugin and a debug-patched build. It cannot reach a stuck game, as the eval needs the main thread.

**Why this order.**

- `Quit` first, because it needs the debugger session and `detach` ends it. It returns at once: the eval came back `quit requested t=191.3` with the game still up, since `TerminateGame` runs over the following frames.
- `detach` next, because it resumes the game fully and frees the session (`plugins/unity-devtools/mcp/SessionTools.cs:109-120`). `TerminateGame` waits on frames, and a game suspended in a debugger runs none. That is the reading behind a game "held by a debugger takes the whole wait" (reasoned; no run held a game suspended through a close). The eval also needs no second session: a further unity call would reattach by itself.
- The tool last, because it waits for the exit and reports it, and it keeps the evidence on a `restart`. The tool does not skip its wait when its own close failed, as that would kill a game already shutting down from the eval (design recorded in ticket 01).

**Evidence.** Ticket 01, launch 6: quit from the eval during a forced autosave, then `detach`; game gone 16.2 s after `detach`, about 17.2 s after the quit, both pass lines logged, `GameManager destroyed` at 16,023 ms.
Unconfirmed: whether `detach` is needed when nothing is suspended. An attached but running session may close just as fast, as every run detached. Harmless either way, so the order stands; settled by `quit-no-detach`.
The template's step 1 made this the cycle's opening move and gave the game "a minute" (`gfd-game-cycle/SKILL.md:13-14`, `docs/ROADMAP.md:1141` at `ad4a7b4`). Verdict: both are superseded by the shipped `stop`, which closes on its own, and the eval quit is the fallback only.

### Template and roadmap claims, with verdicts

"T" is the template's `gfd-game-cycle`, "TT" its `gfd-game-testing`, "R" `docs/ROADMAP.md:1132-1149` at `ad4a7b4`.

**Did not hold.**

| Claim | Source | Finding |
|---|---|---|
| `CameraController.inputEnabled = false` stops edge scrolling and the camera keys | T:49, R:1146 (since corrected) | Nothing reads it on that class; input gates on `m_CameraMap.enabled` |
| Autosaves every five minutes | T:28 | New-player default; a setting, off on this machine |
| Sent early, `menu.loadGame` throws | T:26, R:1145 | Caught and logged; the caller sees nothing |
| List fills some 30 s after the main menu | T:26, R:1145 | No such timer; event driven; time unmeasured |
| Crashpad prunes within the hour | T:44 | About 10 minutes into a run, `crash-dumps.md` |
| Every log is written afresh at each launch | T:43 | Truncated at first message; stale files stay |
| The running game holds the mod's DLL open | T:15 | Managed loaded from bytes; Burst library held, PDB handle undisposed |
| `IgnoreLogForTestResult`: `test` still fails on it | ticket | Only for an entry in `TestScenarios.log` or naming the mod |
| Any logged error fails the test under way | TT:40 | Per-test window; not preparation, not after the last test |
| `disabled` skips its test bodies | ticket | True, and `OnPrepare`, `OnCleanup` still run |
| Quit from the eval as step 1; a minute's grace | T:13-14, R:1141 | Superseded; fallback only |
| `mise game`, `refuses to start while the game runs`, the Debug check | T, TT:18 | Obsolete: `test` stops the game, and has no such check |

**Held, some with a scope.**

| Claim | Source | Finding |
|---|---|---|
| Blocked over 5 s makes a hang report | T:44 | 5 s read live, `BacktraceHelper`'s 15000 never reaches the native client (`crash-dumps.md`, finding 8) |
| `menu.saves` lookup at `MenuUISystem.cs:797` | R:1145 | Holds |
| `CameraController.cs:248` declares it | R:1146 (since corrected) | The line holds, the effect does not |
| `Load` from the eval holds the main thread; kill | T:26, R:1145 | Observed no answer, no progress; mechanism not shown, `stop` untried |
| Launch waits for `Cities2.exe` | R | Holds (`cycle.ts:252`) |
| `taskkill`, then `/F` | R:1141 | Holds, on Windows too (section 8) |
| Stuck 6 min, 10 min boot, 30 min run | TT:23-24 | Hold: 360, 600, 1800 s |
| One `[TestPrepare]`, throws on two | R, TT:53 | Holds, and the throw surfaces as Success |
| `OnPrepare` throw reports success | TT:41 | Holds |
| Pending errors abort a scenario | TT:43 | Holds, also a false success |
| Main-menu exception in `SceneFlow.log`, mod only in stack | R | Holds, and it is not specific to the main menu |
| `scenarios` is the live dictionary | R | Holds |
| `TestScenario.cs:166`, `:238`, `:266-275`, `AutomationClientSystem.cs:374` | ticket | Hold |
| Five seconds after its first main menu | TT:19 | Holds |
| Tokens reach dumps, launch by `systemd-run` | T:24, R | Holds, and an ordinary logged error also carries them |
| Loading screen 20 s after a load | `cycle.ts:53` | Tool constant, no decompile source; `LoadingScreen.cs` runs on task progress |
| Local build shows in `Modding.log`, `pdx_mods` if subscribed | T:22 | Path holds, Burst mods only; local wins by default |

### Runs that would settle what is open

Each is a run on the user's game; the slug is what the text above cites. `camera` and `build-open` on Linux were run and are settled in place.

- **`saves-list`:** subscribe to `menu.saves` from boot, log when it fills against `MainMenu reached`, across a few launches; trigger `menu.loadGame` before and after. Settles the time and its spread.
- **`eval-load`:** repeat `GameManager.instance.Load` from the eval on a throwaway save, then `stop`, and let it sit past 60 s. Settles whether the game recovers and whether `stop` closes it clean. Read the main thread's stack during the wait for the mechanism.
- **`wait-gap`:** the gap between `menu.loadGame` and its first logged line. Bounds what `wait_load` must tolerate.
- **`shadow`:** the same mod local and subscribed, each version order, reading the Burst path. Settles whether a subscription can win.
- **`stock`:** a scenario on an unpatched game. Settles whether the framework runs without the debug patch.
- **`scenario-traps`:** four scenarios through `test`: a `disabled: true` one (reported passed, `OnPrepare` ran); a throwing `OnCleanup` (`stuck` after 360 s); `IgnoreLogForTestResult` with a game-logged error (passes); a second local Debug mod on the same bit (joins the run).
- **`quit-no-detach`:** the eval quit with the session left attached and running. Settles whether `detach` matters past a suspended game.
- **`codex-long`:** a Codex call past 300 s, a boot on a slow load, against `tool_timeout_sec` 2460.
- **`heartbeat`:** a `test` past 30 minutes on Claude Code. Settles whether progress resets the stdio abort.
- **Windows past ticket 10:** a call through Claude Code or Codex on Windows, for arrival (section 8).

Not asked for in any of these: a run of `stop` on a fresh stuck game. The stuck game seen twice in ticket 01 (launch 2, and a stop forced at 61 s) has no known cause, and it would take reproducing it first.

### Catalog and `SOURCES.md` gaps

- **Catalog gap, Network Tools.** `plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md:87-93` had no in-engine scenario line, landed at `:95`. The repo holds them: `cs2-networktools/NetworkTools.Mod/Common/Mod/LucaModBase.cs:235-237` (`#if IS_DEBUG`) and `:308-339` reflect scenarios into `m_Scenarios` and write the sorted dictionary back; `NetworkTools.Mod/Tests/Integration/TunnelModeTest.cs:40` uses `Category.QA`. The proposed `**Demonstrates**` line: "An in-engine scenario system on the game's `Category.QA`, reflected into the framework's private scenario dictionary in debug builds and re-sorted, so `--categoryFilter=QA` runs them beside the game's." `plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md:148` already covers the same reflection for another entry.
- **`SOURCES.md` gap.** No entry named `<user data>/.cache/backtrace/crashpad/` or the framework's test logs as a source; landed at `docs/SOURCES.md:189-191`. This topic's Backtrace claims rest on the decompile and on ticket 01.

---

## Bridge

Slugs checked on disk under `plugins/cs2-modding/skills/`.

- **`diagnostics`:** owns which log to open and what each line proves. This topic hands it every read of a run, the kept one included. It needs nothing changed; its line "Copy `Player.log` and the whole `Logs/` directory before the game is started again" stays true for a launch outside the tool, and the reference can state that `restart` does this copy, once.
- **`debug-menu`:** owns the flags and `runOnce.txt`; `--qaDeveloperMode` for the scenario tab points there.
- **`mod-lifecycle-and-ordering`:** a quit reaches `OnDispose` only when clean, the same fact from the lifecycle side; `OnLoad` as the place to register scenarios.
- **`save-serialization`:** an autosave of a test build is a save written by that build.
- **`settings-and-input`:** a mod's settings persist through `SaveAllSettings`, so a forced end loses them; input barriers are its neighbour for the camera freeze.
- **`navigating-the-decompile`:** the first move when a scenario's framework behaviour needs reading.
- **`cs2-modding-setup`:** the debug patch and launch options the fallback and the unity eval need.
- **`cs2-mod-project`:** the build and the deploy folder the cycle's build step lands in; the csproj condition's neighbour.
- **`cs2-modding-ui`:** a UI-only change can deploy with the game open; that skill says when.

## Dead ends

- **A timer behind "some 30 s".** Searched `MenuUISystem`, `MenuHelpers` and `GameManager`'s boot: none exists. The list is event driven.
- **A reader of `CameraController.inputEnabled`.** Searched `Game` and `Colossal.Core` whole, and the corpus: none on that class. The freeze in the template came from a repo's practice, and no tracked source holds a test of it.
- **The eval awaiting a returned `Task`.** Searched the unity server's `sdb/` and `mcp/`: nothing awaits one, so a blocking await inside eval is not the `Load` mechanism.
- **A Debug-build gate in the framework.** No `isDebugBuild` read in `Colossal.TestFramework` or `TestsDebugUI` gates a run.
- **`disabled` or `IgnoreLogForTestResult` in use.** No game scenario, no template, no corpus mod sets either.
- **A city save on close.** No path from `WantsToQuit` writes the city; the only save touched is one already running.
- **A crash-free way to keep a report alive.** The prune is the shipped handler's and the decompile holds only the client; the tool's copy, or a copy by hand, keeps one.
- **Conflicts.** None needs a ruling: `docs/research/conflicts.md` holds nothing on restarting or logs, and every disagreement above has a first-party winner. Nothing was appended.
