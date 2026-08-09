# A non-power-of-two update interval, and where the failure lands

## Prompt

A Cities: Skylines II code mod registers a system in its `OnLoad` with `updateSystem.UpdateAt<MySystem>(SystemUpdatePhase.UIUpdate)`, and that system overrides `GetUpdateInterval(SystemUpdatePhase phase)` to return `10`, intending to run on every tenth pass of the phase. Describe what actually happens when the game loads that mod, including how far the failure reaches. Then state what would still be wrong if the override returned `8` instead.

## Verified answer

**With 10, the mod does not load at all.** `UpdateSystem.GetInterval` calls the system's `GetUpdateInterval` and rejects any value that is not a power of two:

- `src/Game/Game/UpdateSystem.cs:286` — `		if (!math.ispow2(interval))`
- `src/Game/Game/UpdateSystem.cs:288` — `			throw new Exception("System update interval not power of 2");`

That check is on the **registration** path, not the update path: `GetInterval` is called from both private `Register` overloads (`:256` and `:263`), and `UpdateAt<SystemType>` is `Register(++m_AddIndex, base.World.GetOrCreateSystemManaged<SystemType>(), phase);` at `:143`. So the throw happens synchronously inside the `UpdateAt<T>` call in `OnLoad`, in every phase, before the system ever runs.

**The failure takes the whole mod, not just the system.** The exception unwinds out of `IMod.OnLoad`, so nothing the mod registers after that line is reached. `ModManager.ModInfo.Load` catches it, sets `state = State.GeneralError` (`src/Game/Game.Modding/ModManager.cs:140`) and rethrows (`:142`); `InitializeMods`' own catch then calls `modInfo2.Dispose()` (`:453`) and logs `"Error initializing mod {0} ({1})"` (`:454`).

Worth knowing for debugging, though not required by the question: `Dispose()` overwrites the state with `State.Disposed` at `:172`, and the reporting loop at `:270` skips any mod whose state is below `IsNotModWarning`, so the player gets no failure notification and no error dialog — only that log line.

**With 8 the mod loads, and the interval is ignored anyway.** The interval is consulted only by the three-argument `UpdateSystem.Update(SystemUpdatePhase phase, uint updateIndex, int iterationIndex)` (`:206`), which gates each system at `:224`:

`				if ((updateIndex & (uint)(systemData.m_Interval - 1)) != (uint)systemData.m_Offset)`

That overload has exactly three call sites in the decompile, all in `Game.Simulation.SimulationSystem`: `:173` for `LoadSimulation`, `:282` for `EditorSimulation`, `:286` for `GameSimulation`. Every other phase is driven by the one-argument `Update(SystemUpdatePhase)` at `:166`, whose loop body never mentions `m_Interval` — including `UIUpdate`, driven from `Game.UI/UIUpdateSystem.cs:19`. A `UIUpdate` system therefore runs on every pass of the phase whatever its interval says.

(`GameSystemBase.GetUpdateInterval(SystemUpdatePhase phase)` at `Game/GameSystemBase.cs:131` is the only signature; it defaults to `1` and is called on every registration in every phase, which is why `10` throws even in a phase that will not honour it.)

## Rubric

- 4: Says an exception is thrown because the interval is not a power of two, and that it is thrown during registration — inside the `UpdateAt<T>` call in `OnLoad` — rather than when the system would update.
- 3: Says the failure takes down the whole mod rather than only that system: the exception leaves `OnLoad`, so nothing registered afterwards runs and the mod is disposed.
- 3: Says that with `8` the mod loads but the interval has no effect in `UIUpdate`, because only the simulation phases (`LoadSimulation`, `EditorSimulation`, `GameSimulation`) consult it.

## Roots

- decompile
