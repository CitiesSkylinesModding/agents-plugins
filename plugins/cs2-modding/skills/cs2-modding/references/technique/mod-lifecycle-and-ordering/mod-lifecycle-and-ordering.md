# Mod lifecycle, loading and system ordering

Verified against game version 1.6.0f1.

How a mod gets code running at the right moment.
What that code does once it runs belongs to the reference for the mechanism it touches.

## The contract is two methods

`Game.Modding.IMod` declares exactly two members:

```csharp
void OnLoad(UpdateSystem updateSystem);
void OnDispose();
```

The whole interface file is eight lines.
There is no `OnCreateWorld`, no `OnGameLoad`, and no initialisation callback of any other name.
A class that declares a method by one of those names compiles, ships, and is never called, with no error and no log line — the failure looks exactly like a mod that does nothing.

`OnLoad`'s parameter is the entire ordering API.
Everything below that schedules a system goes through that object.

## How the game finds the mod class, and why the two tests differ

Detection and instantiation use different rules, and the gap between them is the trap.

**Detection** scans the assembly's metadata.
It walks the top-level types of the main module and, for each, reads the interface list, looking for a directly declared interface whose full name is `Game.Modding.IMod`.
A class that reaches `IMod` only through a base class in another assembly does not match, and neither does a nested class.
An assembly that fails this test is not registered as a mod at all.

**Instantiation** then uses reflection over every type in the assembly, asking which are assignable to `IMod`.
That question is transitive, sees nested types, and applies no abstract filter.

Three rules follow.

- **Declare `IMod` explicitly in the base list of a top-level class**, even when a base class already implements it.
  The redundant redeclaration is what the metadata scan requires, and it is the reason detection succeeds for a mod built on a shared base class.
- **Every `IMod` implementation in the assembly is instantiated**, and `OnLoad` is called on each in turn.
  A second implementation left behind from a refactor is a second mod entry point running in the same session.
- **Keep every `IMod` implementation in the assembly concrete.**
  Nothing filters an abstract one out before construction, and the loader constructs each implementation it finds without calling a constructor, so a shared base is safer expressed as a plain class the mod entry point holds than as a base the entry point derives from.

Other gates the asset passes before `OnLoad` is reached, each failing to a distinct state on the mod's record:

- the assembly must be marked required, and must be either a mod or a reference;
- same-named assemblies are resolved to a single winner, ordered by already-loaded, then local, then version descending, so a second copy of the same assembly name simply loses;
- every assembly reference must resolve, or the mod is refused with a missed-dependencies state;
- an asset that ships a copy of a game assembly beside itself is **skipped** with a warning reading `Assembly "{0}" is in-game assembly and it should NOT be shipped with mod "{1}"`.

(VOLATILE: the mod-state names — `Unknown`, `Loaded`, `Disposed`, `IsNotModWarning`, `IsNotUniqueWarning`, `GeneralError`, `MissedDependenciesError`, `LoadAssemblyError`, `LoadAssemblyReferenceError` — `ModInfo.State`; they are also the localisation keys the failure dialog interpolates.)

## The mod class is never constructed; its systems are

The loader allocates the mod instance without running any constructor.
**Instance field initialisers therefore never run either**, because the compiler folds them into the constructor.
A field declared `private readonly List<X> m_Things = new();` is `null` for the whole life of the mod, and the first use is a null reference inside `OnLoad`.

Static fields are unaffected: the static constructor still runs on first access.
So mod-level state goes in a static field, or is assigned inside `OnLoad`.

Systems are the exact opposite case.
The ECS creates a system through ordinary activation, so a mod's own system **does** get its parameterless constructor and its field initialisers — and a system type with no parameterless constructor throws at creation.

That contrast is worth holding as one fact: the mod object is a hollow shell with two methods on it, and everything with real construction semantics is a system.

## Where `OnLoad` sits in the boot sequence

Game startup is a single ordered sequence, and mod loading sits late in it:

1. the world is created, along with the prefab system, the update system, and the load and save game systems;
2. the mod manager is constructed;
3. **every vanilla system is created and registered** by the game's own system-order class;
4. **mods are registered and loaded, and `OnLoad` is called on each**;
5. prefabs are loaded;
6. the world reaches its ready state and the world-ready event fires.

Four consequences, each load-bearing:

- **The world exists and all of vanilla is already registered.**
  A mod cannot change how the world is built, cannot get in front of the vanilla registration pass, and cannot pre-empt a vanilla registration.
  It can only append — which is why the band rules below are the whole of a mod's leverage over ordering.
- **Nothing has updated yet.**
  The world does not tick until step 6, so no phase has run once when `OnLoad` executes.
  Reading simulation state, querying for populated entities, or assuming any vanilla system has done its work is guaranteed to be wrong at that point.
- **Prefabs are not loaded yet.**
  Anything needing the prefab database waits — see the deferral section, and `prefabs-and-assets` for what to do once it is there.
- **The world-ready event has not fired.**
  A system created during `OnLoad` receives `OnWorldReady`, because the system base subscribes to that event in its own `OnCreate`.
  A system created after step 6 misses it permanently, which is the one lifecycle hook late creation silently costs.

The world keeps updating during a save load, so phases continue to run while the loading screen is up.

Mods can also be re-initialised without restarting the game — a playset or mod-status change re-runs the whole registration and `OnLoad` pass — and the manager pushes a "restart required" notification when it cannot.
An `OnLoad` that assumes it runs exactly once per process is wrong on that path.

## Ordering is imperative, and the stock ECS attributes do nothing here

`[UpdateInGroup]`, `[UpdateBefore]` and `[UpdateAfter]` — the attributes, as opposed to the methods of the same name below — compile and have **no effect whatsoever** in this game.
They are not merely discouraged; nothing reads them.

The reason is structural rather than a matter of policy.
Those attributes are consumed by the sorter that orders the members of a component system group, and this game creates no system group.
It does not run the default world initialisation that would build them; it constructs a bare world and adds four systems to it by hand, then registers every other system imperatively.
The clearest demonstration is in the game's own code: a stock ECS system that carries `[UpdateInGroup(typeof(InitializationSystemGroup))]` is registered by the game with an explicit `UpdateBefore` call into a phase, and the attribute on it changes nothing.

So an attribute on a mod's system is silent decoration at best.
At worst it is a lie in the source: an attribute can state a relation that the imperative registration inverts, and the registration wins every time.
An agent arriving from stock ECS should read the attributes it knows as inert and reach only for the methods.

## The five registration methods and the three bands

`UpdateSystem` exposes exactly five registration methods, and nothing else registers a system for updating.
There is no unregister method; registration is one-way for the session.

| Method                           | Effect                              |
| -------------------------------- | ----------------------------------- |
| `UpdateBefore<T>(phase)`         | front band of `phase`               |
| `UpdateAt<T>(phase)`             | middle band of `phase`              |
| `UpdateAfter<T>(phase)`          | back band of `phase`                |
| `UpdateBefore<T, TOther>(phase)` | spliced immediately before `TOther` |
| `UpdateAfter<T, TOther>(phase)`  | spliced immediately after `TOther`  |

Each single-type call assigns the system a monotonically increasing index, offset by minus one million for the front band and plus one million for the back band.
The update system then sorts by phase, then by that index, and rebuilds one contiguous run of systems per phase.

Three rules follow, and they are the ones to act on.

1. **All three methods land inside the phase you name.**
   Nothing runs "before the phase" or "after the phase" despite the names.
   The offsets are far larger than the registration counter ever reaches — vanilla makes on the order of a thousand registrations in total — so the bands never interleave: every `UpdateBefore` in a phase runs before every `UpdateAt`, and every `UpdateAt` before every `UpdateAfter`.
   Choosing among the three chooses a band within one phase, and only that.
   (VOLATILE: the ±1,000,000 band offset, and the `AllowBarrier` and `ModificationBarrierN` type names below — `UpdateSystem`'s registration methods, and the vanilla system-order class.)
2. **Within a band, registration order is execution order.**
   The index is a plain counter and is the only tiebreak after phase, so two systems a mod registers with `UpdateAt` into the same phase run in the order the `OnLoad` body calls them.
3. **A mod always registers after all of vanilla**, because the vanilla registration pass is step 3 of boot and `OnLoad` is step 4.
   So a mod's `UpdateAt` lands after every vanilla `UpdateAt` in that phase; its `UpdateBefore` lands after every vanilla `UpdateBefore` but ahead of all vanilla `UpdateAt`; its `UpdateAfter` lands after everything.
   Short of anchoring, that is the entire lever.

Two vanilla arrangements make rule 3 concrete.
Each modification phase is bracketed by an allow-barrier registration in the front band and the matching barrier in the back band, so a mod's `UpdateBefore` there sits between the allow-barrier and vanilla's first `UpdateAt`, and its `UpdateAfter` sits after the barrier has played back its command buffer.
The `Deserialize` phase uses all three bands as a designed sandwich: pre-deserialize wrappers in front, readers and migrations in the middle, post-deserialize wrappers behind.
The barriers and the command-buffer contract belong to `ecs-in-this-game`.

## Anchoring to a named system

The two-type forms splice a system immediately beside another system wherever that one ended up, rather than putting it in a band.
This is how a mod says "run directly after this vanilla system", and it survives the vanilla ordering changing around it.

The splice works regardless of which band the anchor itself sits in, and it is recursive: a mod can anchor to a system it registered earlier in the same `OnLoad`, and chains several deep resolve correctly.
Past a hundred levels of nesting the rebuild throws `Too deep system order`.

**The failure mode is total silence.**
The splice is conditional on a phase match: the anchored system is only consumed when the anchor turns out to be registered in the phase that was passed.
The rebuild walks the registered systems and never enumerates the pending anchors on their own, so if the anchor is not registered in that phase — wrong phase, or a type nothing ever registers — the system goes into a dictionary and **never runs**.
No exception, no log line, no symptom other than absence.

So the anchor's phase is not optional context: pass the phase the vanilla system is actually registered in.
Anchoring's failure is the hardest one in this reference to diagnose from a log, because there is nothing in the log.

## The phases nest; they do not run flat

There are 32 update phases.
Their declaration order in the enum is **not** their execution order — the ordinal is used only to index the update system's per-phase ranges and carries no timing meaning.

The real structure is a tree.
Only the game manager sits on the engine's own update callbacks; every other phase is driven from inside some system's `OnUpdate`, which is what makes the nesting.

No single file states this tree.
It is derived from two things read together: the vanilla registration table, which fixes where each driver system itself runs, and the calls that drive a phase from inside a system's own `OnUpdate`, which supply the nesting.
Re-checking it means reading those two against each other again.

Indentation below is nesting.
A phase listed under a system runs entirely inside that system's update.

```
engine Update()  →  GameManager.Update()
  MainLoop
    (UpdateBefore band, in registration order)
      UpdateWorldTimeSystem, PathfindQueueSystem, EndFrameBarrier
    (UpdateAt band, in registration order)
      RaycastSystem                  →  Raycast
      PrefabSystem                   →  PrefabUpdate
      CityConfigurationSystem
      ToolSystem                     →  PreTool
                                     →  ToolUpdate
                                          ToolOutputSystem (back band)
                                            →  ClearTool  or  ApplyTool
                                     →  PostTool
      LoadGameSystem                 →  Deserialize
                                          ResolvePrefabsSystem
                                            →  PrefabReferences
      ModificationSystem             →  Modification1, 2, 2B, 3, 4, 4B, 5, ModificationEnd
      UnlockSystem
      AllowBarrier<EndFrameBarrier>
      PreRenderSystem                →  PreCulling
      PathfindSetupSystem
      PathfindResultSystem
      AchievementTriggerSystem
      UIUpdateSystem                 →  UIUpdate
                                          TooltipUISystem
                                            →  UITooltip
      RenderingSystem                →  Rendering
      SaveGameSystem                 →  Serialize
                                          BeginPrefabSerializationSystem
                                            →  PrefabReferences
      DebugWatchSystem
    (UpdateAfter band)
      PrepareCleanUpSystem
  (the UI update)
  Cleanup

engine LateUpdate()  →  GameManager.LateUpdate()
  LateUpdate
      DebugSystem
      SimulationSystem               →  PreSimulation
                                     →  0..8 × GameSimulation and/or EditorSimulation
                                        (while loading, instead: 8 × LoadSimulation)
                                     →  PostSimulation
      CompleteRenderingSystem        →  CompleteRendering
      GizmosSystem
      AutoSaveSystem
  DebugGizmos
```

(VOLATILE: the driver system names in this tree — the vanilla system-order class, and the systems that drive a phase from their own `OnUpdate`.)

Four consequences a reader needs before choosing a phase:

- **Everything in `MainLoop` — modification, tools, UI, rendering — runs before the simulation for that frame**, because the simulation phases hang off a system in `LateUpdate`.
  A `GameSimulation` system therefore sees the world as the modification phases left it, and its own output is first observed by the _next_ frame's modification phases.
  A mod that writes in `GameSimulation` and reads in `Modification4` is reading data one frame old, by design and unavoidably.
- **`GameSimulation` runs a variable number of times per rendered frame, including zero.**
  The step count is clamped to 0–8 from the selected game speed; `PreSimulation` and `PostSimulation` still run exactly once per frame even at zero steps.
  `GameSimulation` and `EditorSimulation` are each gated on the current action mode, and both can be skipped entirely.
  What a simulation step is worth in game time belongs to `simulation-time-and-units`.
- **`Deserialize` and `Serialize` fire once per load and once per save, not per frame.**
  Their driving systems are disabled by default and flipped on for a single run.
  Several other phases are conditional on their driver's own state as well — the two simulation phases on the action mode, `LoadSimulation` on a loading counter, and `ClearTool` and `ApplyTool` on the tool system's apply mode, which selects at most one of the two.
  A phase whose driver runs is not thereby a phase that runs, so check the driver before sizing per-update work.
- **`PrefabReferences` is reached from two different parents**, inside `Deserialize` and inside `Serialize`, so a system registered there runs in both directions of the save pipeline.

## Choosing a phase

Six choices cover almost every mod, and each is a position in the tree above rather than a preference.

- **Simulation work on the city** goes in `GameSimulation`, which is where the whole vanilla simulation lives, and which runs zero to eight times per frame.
  The editor drives `EditorSimulation` instead, so a system that must also run in the editor is registered into both phases; registering into `GameSimulation` alone leaves it silently dead there.
- **Anything the player looks at in the UI** goes in `UIUpdate`, which holds every vanilla UI system.
- **A tooltip system** goes in `UITooltip` and nowhere else; that one is a hard requirement rather than a convention, and `custom-tools` owns the mechanism behind it.
- **A tool** goes in `ToolUpdate`, because the tool system enables the active tool immediately before driving that phase.
- **Changing entities while the frame is still building them** goes in one of the eight modification phases, anchored beside the vanilla system whose output you need or whose input you are supplying.
- **Anything that must read or write a save** goes in `Deserialize` or `Serialize`, both of which fire once per load or save rather than per frame.

Read [the phase catalogue](phase-catalogue.md) for what characterises each of the 32 phases, which is what separates two candidates none of the six covers.

## The update interval, and the two halves of the rule

A `GameSystemBase` may override `GetUpdateInterval(phase)` and `GetUpdateOffset(phase)`, defaulting to 1 and -1.
Both halves of the following matter, and either alone is unusable.

**The power-of-two check fires at registration, in every phase, and takes the whole mod down.**
The update system throws `System update interval not power of 2` while registering the system — that is, inside the `UpdateAt<T>` call in `OnLoad`.
The exception propagates out of `OnLoad`, is caught by the mod manager, and fails the **entire mod** with a general-error state, not just the offending system.
A returned interval of 10 is therefore not a slow system; it is a mod that does not load.

**The interval itself is consulted in only three phases.**
Only the overload that carries an update index reads the interval and offset, and that overload is called from exactly three places: `LoadSimulation`, `EditorSimulation` and `GameSimulation`.
A `GetUpdateInterval` override on a system registered in any other phase — `UIUpdate`, `Cleanup`, a modification phase — **has no effect at all**, and the system runs every time its phase runs.
The game itself ships one such dead override, and this is a common mistake: a mod that "throttles" a UI system with an interval has throttled nothing.

Where a system is registered into more than one phase, branch the override on the `phase` argument rather than returning one constant.
The vanilla systems that span phases all do this.

The mask is `(updateIndex & (interval - 1)) != offset → skip`, where the update index is the simulation frame index.
A negative offset — the default — asks the update system to assign one, spreading same-interval systems across different frames so they do not all fire together.
Returning an explicit offset opts out of that spreading, which is what a fork wants and almost nothing else does.

The vanilla idiom, and the one to copy: declare `public static readonly int kUpdatesPerDay = <n>` and return `262144 / kUpdatesPerDay`, where 262144 is the number of simulation frames in an in-game day.
A system that splits its entity set across sub-frames returns `262144 / (kUpdatesPerDay * 16)` and pairs it with the vanilla helper that computes which sub-frame the current frame belongs to.
What that cadence is worth in simulated time is `simulation-time-and-units`.

A system whose interval is below the maximum iteration count also has its job dependency reset between iterations, which is where this interacts with `performance-and-memory`.

## When a lifecycle hook throws

`GameSystemBase` wraps five lifecycle hooks in try/catch and subscribes them during `OnCreate`.
**They do not behave the same way, and the log message does not tell you which happened.**

| Hook                                       | Log message                                              | Disables the system? |
| ------------------------------------------ | -------------------------------------------------------- | -------------------- |
| `OnWorldReady()`                           | `<TypeName>: Error on game preload, disabling system...` | **yes**              |
| `OnGamePreload(Purpose, GameMode)`         | `<TypeName>: Error on game preload, disabling system...` | **yes**              |
| `OnGameLoaded(Context)`                    | `<TypeName>: Error on game load, disabling system...`    | **yes**              |
| `OnGameLoadingComplete(Purpose, GameMode)` | `<TypeName>: Error on state change, disabling system...` | **no**               |
| `OnFocusChanged(bool)`                     | `<TypeName>: Error on Focus change`                      | no                   |

Two things in that table are the payload.

- **`OnGameLoadingComplete` says "disabling system..." and does not disable the system.**
  A reader diagnosing from the log message alone draws the wrong conclusion: the system is still running, and whatever it left half-initialised is still running with it.
- **`OnWorldReady` and `OnGamePreload` emit the identical message**, both reading "Error on game preload".
  The log cannot distinguish which of the two threw; the stack trace in the logged exception is the only way to tell them apart.

All five log through the system base's own logger, named `SceneFlow` — **not** the mod's logger, which is where a mod author looks first and finds nothing.

`OnCreate` and `OnUpdate` are covered by neither mechanism, and each fails a third way.

- **A throw out of a system's `OnCreate` kills the whole mod.**
  The ECS catches it, removes the half-built system from the world, and rethrows; the throw propagates out of the system-creation call inside `UpdateAt<T>`, out of `OnLoad`, and into the mod manager, which disposes the mod and logs `Error initializing mod {0} ({1})`.
- **A throw out of `OnUpdate` disables nothing and repeats forever.**
  The update system wraps each system's update and logs `System update error during {0}->{1}:` with the phase and the system type, then continues to the next system; the throwing system runs again next frame, and logs again, every frame.

So there are four distinct failure surfaces, and "the silent disable" is exactly one of them:

| Where it throws                                              | Outcome                                                                                                                           |
| ------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------- |
| The mod's `OnLoad`, including any system's `OnCreate`        | whole mod fails with a general-error state, a clickable failure notification carries the stack trace, `OnDispose` is still called |
| A system's `OnWorldReady`, `OnGamePreload` or `OnGameLoaded` | that system is disabled for the session, one log line, no user-visible symptom                                                    |
| A system's `OnGameLoadingComplete` or `OnFocusChanged`       | logged, system keeps running                                                                                                      |
| A system's `OnUpdate`                                        | logged every frame, system keeps running                                                                                          |

The mod-loading logger suppresses its errors from the UI, so an `OnLoad` failure is louder in the log than on screen.
Reading those log files is `diagnostics`.

(VOLATILE: all five log-message strings above, the mod-initialisation error string, the per-frame update error string, and the `SceneFlow` logger name — `GameSystemBase`'s hook wrappers, `UpdateSystem.Update`, and the mod manager.)

## Disabling a vanilla system and slotting a fork into its place

Setting `Enabled = false` on a system does not unregister it.
The update still runs, skips `OnUpdate`, calls `OnStopRunning` once, and the system stays in the phase's run.
**That is what makes the substitution pattern work: a disabled system is still a valid anchor.**

The recipe:

```csharp
public void OnLoad(UpdateSystem updateSystem)
{
    updateSystem.World.GetOrCreateSystemManaged<Game.Net.GeometrySystem>().Enabled = false;
    updateSystem.UpdateBefore<MyGeometrySystem, Game.Net.GeometrySystem>(
        SystemUpdatePhase.Modification4);
}
```

The fork lands in the dead original's exact slot, and stays there if a game update moves the original within the phase.
Pass the phase the original is registered in; the anchor is otherwise dropped in silence, as above.

Two details that bite:

- `GetOrCreateSystemManaged` **creates** the system when it is missing, so a mistyped or never-registered type yields a live-but-never-updated system rather than an error.
  Reaching it through the `World` on the `OnLoad` parameter needs no extra import; the default injection world is the same object.
- **A fork in `GameSimulation` copies the original's interval _and_ its offset**, not just the interval.
  Matching only the interval puts the fork on a different set of simulation frames than the system it replaced, because the update system will assign it a spreading offset of its own.
  Copying both is what makes a fork fire on exactly the frames the original did.

Where a fork is the realistic change — the large simulation areas, `citizens-and-households`, `economy-and-companies`, `city-services-and-coverage` — this is the recipe those references assume.
Where the change is a behaviour inside a method rather than a whole system, `patching` is the cheaper tool.

**Registration is not confined to `OnLoad`.**
Calling any of the five methods later — from a deferred callback, or from a system's own `OnCreate` — works: registration marks the update system dirty and the next update of that phase rebuilds every phase's ranges from scratch.
That is the escape hatch for anything that cannot be decided during `OnLoad`, including anything conditional on another mod being present; `mod-compatibility` owns the detection side.

## Deferring work until the mod manager has settled

The main-thread dispatcher is the mechanism, and it has four shapes with different semantics:

| Call                          | Semantics                                                                                             |
| ----------------------------- | ----------------------------------------------------------------------------------------------------- |
| `RegisterUpdater(Action)`     | runs **once** on the next dispatcher tick, then unregisters itself                                    |
| `RegisterUpdater(Func<bool>)` | re-runs **every tick until the delegate returns true** — the polling form, for waiting on a condition |
| `RunOnMainThread(Action)`     | runs inline when already on the main thread, otherwise defers                                         |
| `WaitXFrames(int)`            | returns a task completing after N ticks                                                               |

The tick sits in the frame update **outside** the guard that stops the world, so deferred work runs even while the world is not updating.

Reach for it when the work needs a world that `OnLoad` has not finished building.
Adding a UI module through the mod manager is the hard case: the call is gated on the manager having finished initialising, and that gate is a **silent no-op rather than a throw**, so a call from inside `OnLoad` registers nothing and reports nothing.
It has to be deferred.
The same applies to reflecting over a vanilla system's private state, to registering with another mod's API, and to marshalling prefab registration back to the main thread from a background import.

## The pre-deserialize hook and its siblings

Three generic wrapper systems exist, each forwarding to one method on the wrapped system and passing it the load or save context:

- `PreDeserialize<T>`, where `T` implements `IPreDeserialize`;
- `PostDeserialize<T>`, with `IPostDeserialize`;
- `PreSerialize<T>`, with `IPreSerialize`.

There is no `PostSerialize<T>`: the far side of the writer is reached by an ordinary `UpdateAfter` registration in the `Serialize` phase instead.

**The wrapper is what gets registered, not the wrapped system:**

```csharp
updateSystem.UpdateBefore<PreDeserialize<MySystem>>(SystemUpdatePhase.Deserialize);
```

Vanilla registers nine `PreDeserialize<T>` in the front band of `Deserialize` — the six spatial search systems, the instance counter, and the two pathfinding queue systems — and that is the pattern's purpose: **clear a mod's own index, cache or spatial tree before the loader starts writing entities into it.**
Any mod holding a quadtree or a lookup keyed by entity wants this, and wants it in the front band so it runs before the readers.

The `Deserialize` phase cannot host everything that looks like it belongs there.
A migration that needs data another phase produces — network compositions, for instance — has to be registered into the modification phase that produces it instead, anchored before whichever system consumes it.
The alternative shape, for save rather than load, is bands rather than wrappers: an `UpdateBefore` in `Serialize` collapses mod state into vanilla fields and one or more `UpdateAfter` registrations restore it once the writer has run.
What goes inside any of those methods is `save-serialization`.

## Not every mod system needs a phase

A system that lands in a vanilla collection which is itself pumped does not get registered with the update system at all.
The info-section base classes are the case in point: create the system with `GetOrCreateSystemManaged` and then call the selected-info UI system's public add-section method yourself, after which that system drives your section from its own update and the phase question does not arise.
The base class does not do this for you — its `OnCreate` caches the UI system and registers nothing, and the vanilla sections reach the panel through a hard-coded list your section is not on.

**Ask what kind of collection the base joins, not merely whether it joins one.**
A collection that is pumped replaces a phase registration; a collection that is only a lookup does not.
The tool base class is the trap here: it appends every tool to the tool system's list from `OnCreate`, but that list decides prefab claim order alone, so a tool still has to be registered into `ToolUpdate` or its `OnUpdate` never runs.

## `OnDispose` hygiene

`OnDispose` is called on every `IMod` instance at shutdown, **and on a mod whose `OnLoad` threw**, at whatever point it got to.
So every null guard in an `OnDispose` body is load-bearing rather than defensive style: the fields it reaches for may never have been assigned.

What belongs there:

- **Undo the mod's Harmony patches.**
  A patch outlives the mod object that applied it, and a mod can be re-initialised without the process restarting, so patches left in place stack a second set over the first.
  `patching` owns the call and the identity it keys on.
- **Unregister the settings from the options UI**, and null the field, in the shape `if (Settings != null) { Settings.UnregisterInOptionsUI(); Settings = null; }`.
  See `settings-and-input`.
- **Null the mod's own static instance and any other static state**, since statics outlive a re-initialisation that constructs a new mod object.
- **Undo anything registered outside the mod's own world** — a UI host location, a temporary directory, an entry in another mod's registry.

What does not belong there: unregistering systems.
Neither `IMod` nor the update system offers a way to, and the five registration methods are the complete surface.
A mod's systems live until the world does.

## What this reference hands to others

`ecs-in-this-game` for what goes inside a system; `save-serialization` for the contents of the two save phases, whose position in the tree and once-per-load firing come from here; `diagnostics` for reading the log files these failures write into; `performance-and-memory` for what the update interval buys; `patching` for changing behaviour without owning the system, and for the patch lifecycle a dispose has to close; `mod-compatibility` for deciding at load time what another mod has already done.
Every mechanics reference needs a phase for its change, and the modification-phase decomposition in the phase catalogue is the part `roads-and-traffic` and `zoning-buildings-and-land-value` lean on hardest.
