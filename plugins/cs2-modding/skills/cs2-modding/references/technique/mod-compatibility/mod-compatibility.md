# Mod compatibility

Verified against game version 1.6.0f1.

What a mod does about the other mods loaded beside it: surviving what it shares with them, finding out they are there, offering them something, reaching into their data, composing a patch or a UI registration with theirs, holding up when the player changes the mod set mid-session, and telling the player when none of that worked.
Helping a player pick or troubleshoot their own mod list is not this plugin's subject; everything below is code a mod ships.

One structural fact sits under all of it: **a compile-time reference on another mod makes your own mod refuse to load whenever that mod is absent**, so every technique here is a way to get the benefit of one without the coupling.

Where mod-to-mod interaction shows up in the simulation itself:
`roads-and-traffic` — several mods write the same network components, and more than one takes over the vanilla lane system, so "which vanilla system does this mod replace" is the question that decides whether two of them can be loaded together at all.
`zoning-buildings-and-land-value` — mods there publish a tag component that a second mod reads in order to exclude the entities carrying it, so a change in that area has to account for marks it did not make.

## No declared dependency binds a code mod

Three dependency channels exist and none of them makes another mod's presence a runtime guarantee for code.

- **The publish-time declaration** in the mod project's publish configuration is a store relationship.
  It is composed into the upload metadata and nothing in the running game reads it.
  `cs2-mod-project` owns it.
- **The prefab-level prerequisite** is real and enforced: a prefab whose asset carries a platform id gets a mod-requirement dependency, and prefab registration silently returns false without registering when the required mod is not in the active playset.
  The limit is where it comes from — the prefab's **asset**.
  A prefab a code mod creates at runtime has no asset, so this channel covers asset mods and never a code mod's synthesized prefabs.
  `prefabs-and-assets` owns prefab registration.
- **The UI-module declaration** in a module's manifest is parsed into asset tags and read by nothing else.

So a code mod that needs another mod present detects it itself, at runtime, every run.

## One assembly name, one loaded copy

The asset loader groups every executable asset by **simple assembly name across the entire install** — mods and the libraries they ship alike — and loads exactly one winner per group.

At first initialization the winner is **local before non-local, then highest version, then asset id**.
A fourth key, "already loaded", sits ahead of those and cannot fire at boot.
It bites on a mid-session re-initialization, where a copy already in the process beats both a local one and a higher-versioned one.

**For a library your mod ships, this is silent.**
Reference resolution looks in your own mod folder first and then loads whichever copy of that name won globally anyway, so your code runs against a copy you may not have compiled against.
Nothing is logged, no state is set, and the player sees nothing.
Nothing here is strong-named either, so the version is no part of what binds: the simple name matches, and the copy that won is the one your call sites resolve against.

**From there it is ordinary .NET, and the failure is legible in one direction only.**
A member that moved or went away throws a missing-member exception naming it — when the calling method is first invoked, before that method's first statement, so a `try`/`catch` written around the call never gets the chance to run.
Guarding a call into a shipped library therefore means isolating it in its own `[MethodImpl(MethodImplOptions.NoInlining)]` method, or resolving the member by reflection, where a member that went away is a `null` you can test.
Silent in the other direction: a `const`, an enum member's value and an optional parameter's default are copied into your assembly at compile time, so a change to any of them keeps running with the value you built against.

The posture that follows: **agree a shipped library's version across the ecosystem rather than choosing one per project.**
Harmony is the case that matters most, since exactly one copy is in the process and it carries the static patch registry every patching mod writes into.
`cs2-mod-project` owns the pin.

**For a mod assembly it is loud instead.**
The losing mod never loads at all — the loader returns before the assembly is loaded, before the `IMod` is instantiated and before `OnLoad` — and the player gets a notification and a dialog.
A mod whose references cannot all be resolved never loads either, with the unresolved reference names carried into the error the player sees.

## Detecting another mod at runtime

The routes answer different questions.

| Route                                                                                                                                                       | What it answers                              | When it is correct             |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------- | ------------------------------ |
| Enumerate the mod manager — it is `IEnumerable<ModInfo>`; match `modInfo.asset.name`, the **simple** assembly name                                          | Is it registered, before any load is decided | Anywhere, `OnLoad` included    |
| `modManager.ListModsEnabled()`                                                                                                                              | Is it installed — code mods and UI modules   | After every mod has loaded     |
| Asset database by exact name, then `asset.assembly.GetType(...)`                                                                                            | Is its assembly loaded, and hand me a type   | After that mod has loaded      |
| `AppDomain.CurrentDomain.GetAssemblies()`, matching a name or probing each for a type                                                                       | Is its code in the process                   | After that mod has loaded      |
| `Type.GetType("Ns.Type, Assembly")`, falling back to the assembly scan                                                                                      | Hand me a type by assembly-qualified name    | After that mod has loaded      |
| A game registry another mod pushed itself into — `ToolSystem.tools` is a plain `List<ToolBaseSystem>` you scan for a `toolID`, or a system resolved by type | Hand me its live instance                    | After that mod's system exists |
| An `EntityQuery` over a component type resolved by name, plus `RequireForUpdate`                                                                            | **Is it doing something right now**          | In a system's update           |

**The mod manager is the only route correct from `OnLoad`.**
Reach it as `GameManager.instance.modManager` and enumerate it directly.
Registration populates it before any mod's `OnLoad` runs, so enumerating it there sees every mod that will load, in any order.
It walks every IL assembly in the database, so the enumeration also holds the libraries mods ship, the losing copy of a duplicated name and a mod whose references did not resolve.
`state` stays `Unknown` until that entry's own load runs, so it tells you nothing from `OnLoad`.
Test `modInfo.isValid` — a mod that won its name — together with `asset.canBeLoaded`, one whose references all resolved.

`ListModsEnabled()` filters on mods that have already loaded, and mod initialization order is the iteration order of the dictionary the manager keeps them in — so called from `OnLoad` it returns a _prefix_ of the mod set, and a mod later in that order is invisible.
That list is two halves concatenated: every loaded assembly by its full name, and every UI module by its manifest id, which is where a UI module appears at all.
A library a mod ships is registered, required and loaded exactly like a mod, so it carries its own full name into that first half.

**Everything else defers to after all mods have loaded.**
A one-shot main-thread callback registered from `OnLoad` runs on the next frame, which is after every mod's `OnLoad` has returned; `mod-lifecycle-and-ordering` owns the idiom.
The deferral is not politeness: every other route reads something another mod's load created, and from `OnLoad` that thing may not exist yet.
It is enough for what that mod's load itself creates — its assets, its assembly, its types.
It is not enough for a system or a registry entry, which that mod's own deferred registration may create after yours has already run, so resolve those where they are used rather than probing once and caching.

**Match the whole name, with its delimiter.**
A full-name prefix test written without the trailing `", "` or `", Version"` also matches a mod whose name merely starts with the one you meant.
The asset database is worse: its search filter's implicit conversion from `string` is a **case-insensitive substring** match, so asking it for `"SomeMod"` also returns `SomeModExtras`.
Build the filter by condition with an explicit `Equals` instead.

**An assembly name is not a stable identifier.**
It is whatever that mod's author last called the project, and a republished mod can change it, which is why a detector that has to survive a rename tries several candidate names before giving up.
Probe for the _type_ you actually need where you can, wrap the probe so a miss reads as "absent" rather than as an error, and never let a detection failure fail your own load.

**Reach another mod's type and a static, not its live `IMod` instance.**
The manager exposes each mod's instances publicly, but a mod object is constructed without running any constructor, so no field is guaranteed set beyond what `OnLoad` wrote.

**Cache the answer, and know which answers are safe to cache.**
A `static bool?` filled on first ask is the shape, so a probe that answers no is not re-run on every call.
The mod set can change under a running process: _When the player changes the mod set mid-session_ below owns the signal that it did, and which routes stay trustworthy across it.

### Turning a detection into a decision

What a positive detection leads to:

- **Yield the shared affordance.** Skip widening a vanilla mask with a mode of your own, or hand a display setting over by zeroing yours, when a competitor that also offers it is present.
- **Disable the foreign system**, where your mod is taking over the slot it occupies.
- **Register an extra system conditionally.**
- **Honour the foreign component** — read a tag another mod owns and exclude, or include, the entities carrying it.
- **Report it and change nothing.**

**Gate an expensive or fragile probe behind a cheap precondition**, so it never runs on a normal install.

**Registering a system conditionally is where this goes wrong.**
Register it from the deferred callback rather than from `OnLoad`, for the reason above: mod initialization order is nobody's to control, so anything the other mod's load creates — its assembly, its component types, its systems — may not exist yet while your `OnLoad` runs.
Registering at `OnLoad` is safe only where the mod manager answered your question and the system you are registering names nothing that mod's load creates.
Registering late takes effect for the single-type `UpdateAt`, `UpdateBefore` and `UpdateAfter` forms, reaching the update system as `World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<UpdateSystem>()` — but do not anchor to the other mod's system with a two-type form, where an anchor not registered in the phase you named is filed where nothing reads it.
The callback runs once, after your own load, at boot or the moment the player enabled you, so a registration that could not go ahead there re-runs from the mod-set event below.
Guard it so the registration happens once however it is reached: there is no unregister, and a second call runs the system twice in the phase.
Let nothing throw out of the callback or out of that event handler, since neither is guarded — a throw in the dispatcher tick strands an arbitrary set of other mods' deferred work for the rest of the session and stops the game's own platform update with it.
`mod-lifecycle-and-ordering` owns the dispatcher, that anchoring silence, and the phase bands.

## Offering something other mods can call

The provider's surface and the consumer's facade, neither side taking a compile-time reference: [exposing an API to other mods](cross-mod-api.md).

## Sharing what nobody arbitrates

Nothing arbitrates two mods claiming one runtime resource: it is first-come-first-served or last-write-wins, with the loser finding out at runtime and at most a log line to say so.

The general move: **state your position relative to what you need, rather than claiming the front.**
`custom-tools` owns the tool list, the procedure for taking a position in it, and the one gate under which the front of it is safe.

The rule that keeps the rest quiet is duller and it works: **name anything global after your own mod.**
Registering a runtime prefab, a resource host, a notification identifier or a settings asset: [the namespaces nobody arbitrates](shared-namespaces.md) says what each does when two mods land on one name.
Your assembly's own simple name is a claim in the same sense and the only one that is loud — _One assembly name, one loaded copy_ above owns it, and the loser there does not load at all.

`localization` owns one more of the same kind: a mod overriding a vanilla key silently changes what every other mod's UI shows, and the guard is testing whether the id is already there before adding.

## The frontend chains where C# does not

`frontend-and-injection` owns the UI module registry.
The compatibility half is one property: **`extend` and `append` wrap whatever is already at the path and therefore chain across mods, while `override` replaces an export outright and `reset` restores vanilla and strips every other mod's changes with it.**
`add` is not in that company: it registers a path that does not exist yet and throws when one does, so it can never take another mod's module, and it is the only call that puts a path in the registry for anything else to extend.

**Chaining is not composing.**
`extend` hands your callback the current value but cannot make you render it, so a wrapper that returns an empty fragment under some condition drops vanilla and every earlier mod's wrapper on that path with it.
Render what you were handed on every branch, and put your own condition inside that component rather than around it.

Whenever the mod set changes the reset runs and every registrar runs again, restoring every overridden export but leaving every added path in place.
So a registrar that calls `add` throws the second time through, and the registrars are one unguarded loop, so that throw takes every later mod's registration with it.
Nothing removes an added path, which is also the remedy: guard your `add` so it runs once, and let every later run fall through to the `extend` and `append` calls the reset did wipe.

## Another mod's data

Another mod's components are in the world as soon as its assembly loaded, and a component type resolved by name is enough to query them and take them away.

1. Resolve the component type off the other mod's **loaded assembly**, not off its asset: `Type.GetType("Their.Namespace.TheirComponent, TheirAssembly")`, falling back to a scan of `AppDomain.CurrentDomain.GetAssemblies()`.
   A mod's assembly is loaded from a byte array rather than from a path the runtime probes, so that fallback carries the case and is not decoration.
   Match the scanned assembly's simple name with an exact `Equals`, for the reason the detection rules above give.
   The asset route answers a different question: a mid-session disable deletes the other mod's assets and leaves its assembly and its components alone, and a reader of persisted data has no moment where that cannot already have happened.
   Turn the type into a runtime `ComponentType` through `TypeManager.GetTypeIndex`, and hold it for the life of the process.
   A **failed** resolution is a detection answer like any other, so it re-runs from the mod-set event below, along with the registration gated on it.
   Log the two failures apart: no assembly of that name means the other mod's code never ran here, while an assembly whose `GetType` returns null means your type name is wrong, and treating the second as the first hides a typo behind a diagnosis nobody re-examines.
   Neither may reach `GetTypeIndex`, which throws on a type it does not know rather than handing back an empty index.
2. Build an `EntityQueryDesc` whose `All` mixes the foreign type with your own anchor components, and `RequireForUpdate` it, so the system idles until the other mod's data actually exists.
3. Register the system from the deferred callback rather than from `OnLoad`, and only once the component type has resolved — a query cannot name a type you could not resolve.
   The registration rules above hold here in full, the single-type forms and the once-guard included.

**Resolving the type is not enough to read a field**, and Replace is the posture that needs to.
`GetComponentData<T>` needs a compile-time `T`, so a type you resolved at runtime does not reach it.
The Entities package has runtime-typed reads that do, and each gives something up.
`ArchetypeChunk.GetDynamicComponentDataArrayReinterpret<T>`, off a `DynamicComponentTypeHandle`, lets a job read the chunk — but `T` is your own mirror of the foreign struct, so you are asserting a layout you do not own, and a change to it misreads rather than throws.
`EntityManager.Debug.GetComponentBoxed` needs no layout and costs a boxed copy per entity on the main thread, and it throws outright on a component carrying a `bool` or a `char`.

**Where the other mod wrote its effect into vanilla components, re-derive from those instead.**
That is the migration a change to the foreign struct cannot break, and it holds the foreign type to what its name reliably buys.

`save-serialization` owns the format, the interfaces and the migration machinery, and states the two facts this rests on: a foreign mod's save section is skipped rather than fatal when that mod is gone, and its components are readable and removable by anyone who can resolve the type.
`ecs-in-this-game` owns queries and component types.

**Three postures a mod takes toward another mod's data:**

- **Replace** — migrate the data into your own components, remove the foreign component, and disable the other mod's system that would otherwise act on it.
  Resolve that system by type through `World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged` and clear its `Enabled`, retrying from your own system's update rather than once from the deferred callback.
  Clear it before you remove anything: the `RequireForUpdate` above stops your own update the moment the last foreign component goes, so a retry that had not yet found the system never runs again, and the other mod refills the data you migrated.
- **Cooperate** — read the foreign component purely as a signal, and write only your own.
- **Coexist** — leave it alone.

Which posture your mod takes is its author's design decision.
The consequences are not a matter of taste:

- Removal is permanent inside the player's save.
- The other mod is never told: `IMod` carries no hook that could tell it, and `OnDispose` reaches only a mod's own instances — at shutdown, or when that mod's own load threw.
- Replacing is coherent while your mod has taken over the vanilla system that produced the data; otherwise you have deleted data whose producer is still running.
- Data outside the save — a directory under the user-data path — has no restore path at all, which is what separates the on-disk case from the in-save one.
  Surfacing it with a button that opens the directory leaves the deletion to the player.
- Tell the player what you did, through the surface _Telling the player_ below prescribes, naming both mods and how many entities you touched, because the player is the party losing the data.

`city-state-and-progression` owns `usedMods`, the only durable in-save trace of the mod set a city has been saved with.

## When the player changes the mod set mid-session

Nothing calls back into `IMod`.
What there is: the playset-change handler's own effects, a restart-required flag, and one event.

- **A code mod enabled mid-session runs its `OnLoad` there and then, with no restart.**
  The handler re-initializes the mod manager, which registers and initializes the newly enabled assets; every already-loaded mod is a no-op, because its load returns immediately once its state is no longer the initial one.
- **A code mod disabled mid-session keeps running.**
  It is not unloaded, its systems keep updating, and `OnDispose` is not called.
  The game sets a restart-required flag, logs it, and pushes a notification whose click offers to quit.
- **UI modules are handled live in both directions**, added and removed as the player toggles them.
- **Prefab assets are added and removed in batches**, spread across frames.

**The event is `ParadoxModsDataSource.onAfterActivePlaysetOrModStatusChanged`**, a public `Action` raised on a playset change and a mod-status change alike; reach the data source by casting `AssetDatabase<ParadoxMods>.instance.dataSource`, which is how the game's own prefab prerequisite gets there.
It fires after the playset handler it follows has been awaited, so a mod enabled mid-session has already run its `OnLoad` by the time your callback sees the change.
Clear your cached answer there — but **only the mod manager and an assembly scan are safe to refill from it.**
Those two are populated before your callback runs, and both still report a mod the player disabled but whose code is still running, which is the question a compatibility system is asking.
A route that reads the other mod's systems or its registry entries can answer no purely because that mod's own deferred registration has not run yet — resolve those on each ask rather than caching them.

**A disable is not a departure, and the detection routes disagree about it.**
The mod's assets are deleted from the database before the event fires, while its assembly stays loaded and its systems keep running — so the asset-database route answers absent for a mod that is still writing components, and the mod manager, an assembly scan and a query over its own data all still answer present.
Ask the question you mean: a compatibility system wants to know whether the other mod's code is running, not whether the player still has it ticked.

The restart-required flag is the other half: `GameManager.instance.modManager.restartRequired` rises only when a loaded mod leaves the playset, never when one is enabled underneath you, so read it to know a disable the player asked for has not taken effect.
A mod whose disabling has to have an effect has nowhere to put it, which is why the restart prompt exists.

The design consequence is on the other side: **your `OnLoad` runs either at boot or the moment the player enables your mod, which can be inside a loaded city**, so detection and registration written for boot have to be correct there too.
`mod-lifecycle-and-ordering` owns `OnLoad` and phase registration.

## Composing a patch with another mod's

**Cross-mod patch order on a shared target is unspecified in practice.**
Harmony sorts by priority descending and then by registration order, and registration order follows mod initialization order, which is dictionary iteration order and nobody's to control.

**A patch that widens something and then narrows the results owns both halves or neither.**
Record whether _you_ were the one that set the flag, and run your narrowing only in that case: a filter that runs unconditionally vetoes hits belonging to somebody else, and the symptom lands in the other mod where nobody can diagnose it.

`patching` owns Harmony's vocabulary, the priority comparer and the worked ownership-flag code.
`custom-tools` owns the raycast case, which is where this rule was learned.

## Telling the player

**Copy the shape the game uses for a mod that failed to load**: a notification keyed to the thing that failed, carrying a failed progress state and an `onClicked`, whose click opens a message dialog and pops the notification it came from.
`diagnostics` owns both surfaces, what the game puts in each, and their traps — above all that a bare string reaching a dialog or a notification is read as a localization key rather than as text.

**A milder problem belongs on your own settings page instead**, as a warning with a button that does something about it.
It costs the player no interruption, and it is where they are already looking when they wonder about a mod.

The frontend contributes nothing here: the whole mod-loading conflict path is C#, so there is no conflict UI for a mod to extend or intercept.
