---
name: cs2-modding
description: 'How Cities: Skylines II is built and how a mod changes it. Use when writing or debugging CS2 mod code, when deciding how to change something the game simulates, when locating the systems, components or prefab data behind a game mechanic, when restarting the game after a build or running in-game tests, or when a mod runs at the wrong moment, breaks another mod, or does not survive a save.'
---

# Modding Cities: Skylines II

A mod is a .NET assembly the game loads through `Game.Modding.IMod`.
City state lives in ECS components that systems change in phases the game schedules itself, and most content is prefab data.
This skill holds a working loop and the traps an agent meets with the decompile open; look everything else up in the decompile each time.

## Trap reports

A **trap** is a game fact an agent holding the decompile and the live game still gets wrong by default.
A wrong move you made this session is a trap candidate when it passes two tests:

- _Silent_: nothing the compiler, the game or its logs printed pointed at the fault; a message pointing elsewhere still counts as silent.
- _Led there_: Unity's or C#'s conventions, or the game's own names, lead to that move, so another agent would make it on another mod.

A fault in the user's own code is not a candidate.
Before keeping one, confirm the game's real behaviour in the decompile or the running game, and check that no skill of this plugin already states it.
A stated trap the game proves wrong is a candidate too.

Say nothing of candidates until the user's task is done.
Then, unless the setup record reads `Trap reports: off`, end the closing message with this report:

```markdown
I hit {one trap / N traps} this plugin's skills miss or misstate: {what they touch, in one clause}.

- {The wrong default, then the right move, in one short sentence.}

{With `gh` installed:} Want me to open an issue for it on the plugin's tracker?
{Without `gh`:} Want the issue text to post yourself? With `gh` installed I can post it for you next time.
To stop these reports, ask me to turn them off.
```

Ask once per report, and post only on a yes: `gh issue create --repo CitiesSkylinesModding/agents-plugins`.
The issue gives each trap's wrong default, its right move, the fully qualified type whose code carries it where one does, and the game version.
It holds nothing of the user's project: no mod name, no code, no paths.
Asked to turn reports off, write `Trap reports: off` into the setup record.

## The working loop

1. Find the vanilla precedent, how the game itself does the thing, and copy its shape: its phase, its query, its barrier.
   [The mod catalog](../cs2-modding-setup/references/mod-catalog.md) names a mod that demonstrates each technique.
2. Before writing a field, find every system that writes it: a value a vanilla pass recomputes reverts on that pass, so write after it, every time.
3. Change the game by the first means that works: a system of the mod's own, then disable-and-fork a vanilla one, then rewrite a vanilla query from outside, then a cached reflection accessor, and only then a Harmony patch.
4. Build and restart through the `cs2-dev` tools, as the game cycle below says.
5. Verify the change in the running game before calling it done.
   That needs the sibling `unity-devtools` plugin and the debug patch `cs2-modding-setup` applies, which `SceneFlow.log`'s `Game configuration: Development (Mono)` confirms; where either is missing, say what you did not verify.
6. On a surprise, stop, establish what the game really does, and note it as a trap candidate.

## Which source answers what

- The decompile answers for anything C# names.
  Its tree is `src/<Assembly>/<Namespace>/<Type>.cs` with a few files one level up, so search `src/**`; `SystemOrder.cs` holds every phase registration the game makes.
- The install answers for strings, packaged content and the frontend bundle, so an empty decompile grep about those settles nothing; constants are inlined at every use, too.
- The setup record `cs2-modding-setup` keeps says where the decompile, the mod corpus and the UI bundle copy live; where one is absent or `(none)`, route the user to that skill rather than guess a path.
- The toolchain's `CSII_*` environment variables locate the install; a missing one is the signal to ask.
- `[CompilerGenerated]` sits on many hand-written game classes: never skip a file for it. [symbol: none]

## The game cycle

The `cs2-dev` tools own it:

1. `stop` before a C# build: only a relaunch loads a build, and on Windows a build beside a running game fails to replace a Burst mod's files and leaves no managed DLL deployed.
2. Build.
3. `restart` with `mod` set, and `save` or `newCity` to reach a city.
4. Read what the result says of the mod's load: a Burst path under `pdx_mods` means the subscribed copy loaded instead of your build. [symbol: Colossal.IO.AssetDatabase.ExecutableAsset]

- The tools act on whatever game is running, a hand-launched one with unsaved play included: with a person sharing the game, say what you will do before calling one.
  A clean quit saves settings, never the city.
- Launch only through `restart`: a Steam started from your shell inherits its environment, tokens included, and the game's error reports upload it.
- A call past two minutes may come back as a notification; it is still running, so never reissue it.
- Load a save through `restart`'s `save`: `GameManager.instance.Load` from the eval stalls the game.
- `restart` and `test` keep only the last ended run, in `<user data>/.cs2-dev/previous-run/`; copy it aside before the next call while it still matters.
- With autosave on, a session leaves autosaves of a test build and prunes older ones of any city: tell the user first. [symbol: Game.AutoSaveSystem]
- `CameraController.inputEnabled` is read by nothing: hold the camera still with `InputManager.instance.CreateMapBarrier("Camera", …)`. [symbol: Game.CameraController]
- The game's test framework logs `Success` for a scenario that reached no test or is `disabled`: check that each test ran. [symbol: Colossal.TestFramework.TestScenario]

## Traps

### Loading

- An assembly counts as a mod only when a top-level class implements `IMod` itself; with the interface only on a nested class or inherited from another assembly, the assembly is skipped and nothing logs it. [symbol: Colossal.IO.AssetDatabase.ExecutableAsset]
- The mod object is created without running a constructor, so its instance field initialisers never run: keep mod state static or assign it in `OnLoad`. [symbol: Game.Modding.ModManager]
- `OnDispose` also runs after a failed load, on that unconstructed object: a teardown touching an unset field throws there and stops every mod loading after it. [symbol: Game.Modding.ModManager]
- Every mod's `OnLoad` runs in one pass, so a mod later in it is not loaded yet: register a system naming another mod's types after the pass, from `MainThreadDispatcher.RegisterUpdater`. [symbol: Game.Modding.ModManager]
- The player can change the mod set mid-session: refresh a cached "is that mod here" answer on `ParadoxModsDataSource.onAfterActivePlaysetOrModStatusChanged`. [symbol: Colossal.IO.AssetDatabase.ParadoxModsDataSource]

### Scheduling

- Unity's system-ordering attributes, `[UpdateAfter]` among them, compile and do nothing: order a system through the `UpdateSystem` that `OnLoad` receives. [symbol: Game.UpdateSystem]
- `UpdateBefore<T, TOther>` and `UpdateAfter<T, TOther>` run `T` only in a phase that registers `TOther`; anchored in any other, `T` never runs and nothing logs it. [symbol: Game.UpdateSystem]
- The simulation runs after the frame's cleanup strips `Created` and `Updated` and destroys `Deleted` entities, so a `GameSimulation` system never sees them: react in the `Modification*` phase the vanilla system for that entity uses. [symbol: Game.Common.SystemOrder]
- `GetUpdateInterval` throttles only in the phases `SimulationSystem` updates with a frame index; anywhere else, `PostSimulation` included, the system runs every frame. [symbol: Game.Simulation.SimulationSystem]
- A `GameSystemBase` subclass whose `OnCreate` skips `base.OnCreate()` never gets `OnGameLoaded` or the other load hooks. [symbol: Game.GameSystemBase]

### Entities

- A change to anything drawn needs `BatchesUpdated` on the entity, or the renderer keeps drawing the old one. [symbol: Game.Rendering.PreCullingSystem]
- A query over real things excludes `Temp`, the tool's preview, and `Deleted`, entities in their last frame, as vanilla queries do. [symbol: Game.Tools.Temp] [symbol: Game.Common.Deleted]
- Delete an entity by adding `Deleted`; `DestroyEntity` is for entities nothing else can hold. [symbol: Game.Common.Deleted]

### Data

- A prefab `*Data` value the simulation reads often has an instance twin on the entity, and the system reads the twin: check the consuming system's lookup before editing the prefab. [symbol: none]
- Each city load resets the prefab data a game mode touches and re-applies the mode: write that data again after every load. [symbol: Game.Prefabs.Modes.GameModeSystem]
- A C# `string` passed as a `LocalizedString` becomes a localization key: wrap literal text in `LocalizedString.Value`. [symbol: Game.UI.Localization.LocalizedString]
- `SetDefaults` runs both for a new city and for a save written before the mod existed: tell them apart by `context.purpose`. [symbol: Colossal.Serialization.Entities.EntityDeserializer]

### Diagnosing

- Read the logs before reaching for the live game: the `cs2-dev` `logs` tool, or else `Logs/Modding.log` and `Logs/SceneFlow.log` under the user data path, and `Player.log` at its root, which carries every logger's `Warn` and above.
- A launch truncates `Player.log`, and a `Logs/` file only on its logger's first write; a crash's candidate line is its cause only if it is rare in that log, so count it. [symbol: Colossal.Logging.UnityLogger]
- In `Modding.log`, `Loaded <assembly> in <n>ms` is written from a `finally` and proves nothing; `Error initializing mod` with a stack is a throw from `OnLoad` or any system's `OnCreate`, and ends the mod's load there. [symbol: Game.Modding.ModManager]
- A throw from a system's hooks or update logs to `SceneFlow.log`, not the mod's log, and `Error on state change, disabling system` leaves the system running. [symbol: Game.GameSystemBase]
- A new logger's `showsErrorsInUI` is true, so a mod's first `Error` pops a modal dialog and pauses the game: chain `SetShowsErrorsInUI(false)` on a logger whose errors the player need not see. [symbol: Colossal.Logging.UnityLogger]
- A logger name is shared by everyone who asks for it, the game included: name the mod's logger after its assembly. [symbol: Colossal.Logging.LogManager]
- An unrecognised `--logsEffectiveness` value turns every log off: pass a level name `Level` declares. [symbol: Colossal.Logging.Level]
- `[DebugWatchValue]` works only on an instance member of a system the world holds; on a static class or the mod class it shows nothing. [symbol: Game.Debug.DebugWatchSystem]

The UI a mod ships is `cs2-modding-ui`; the project, its build and publishing are `cs2-mod-project`; provisioning every local source above is `cs2-modding-setup`.
