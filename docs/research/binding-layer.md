# The binding layer (C# ↔ frontend)

**Baseline.** Decompiled game 1.6.0f1 (`src/Game/Properties/AssemblyInfo.cs`, `VersionInternal("1.6.0f1 (419.d6c6) [6216.19404]")`); mod corpus read 2026-08-23 at the commits the 22-repository checkout carried; wiki `UI Modding` fetched live 2026-08-23 through `index.php?action=raw` (no bot challenge, no snapshot substitution needed).
Frontend claims read the user's own installed copy at 1.6.0f1 — the build the decompile was taken from — and cite it through a copy of `Cities2_Data/Content/Game/UI/index.js` reformatted with prettier at its defaults at `DecompiledCitiesSkylines2/src-ui/source.js`, **135,021 lines**; check a fresh reformat against that count before trusting a line number from it.
Live claims marked **Settled live** were read from the running game over the `coherent-gameface` plugin (source 9) on 2026-08-23, in a loaded city with no mods in the playset, by `game_eval` of `engine.on`/`engine.trigger`/`engine.call` only — no click, no input, no state change.
Scaffold citations are to `@colossalorder/create-csii-ui-mod` version `1.0.0` (`@colossalorder/create-csii-ui-mod/package.json:3`); they expand through the npm-global junction to `<install>/Cities2_Data/Content/Game/.ModdingToolchain/npx-create-csii-ui-mod/<path>`, the game's own files, versioned by the install and not by that npm version (conflicts.md's ruled scaffold-citation entry).

**Reach of the namespace sweep.** Every one of the 66 `.cs` files under `src/Colossal.UI.Binding/Colossal.UI.Binding/` was read in full (3,360 lines total), plus the assembly's `Properties/AssemblyInfo.cs` (`AssemblyVersion("0.0.0.0")`, no `VersionInternal`), its `.csproj` and the generated `UnitySourceGeneratedAssemblyMonoScriptTypes_v1.cs`.

## Findings

### The namespace is the whole mechanism, and it is small

`Colossal.UI.Binding` ships as its own assembly, `Cities2_Data/Managed/Colossal.UI.Binding.dll`, and a mod project reaches it only through an explicit `<Reference Include="Colossal.UI.Binding"><HintPath>$(ManagedPath)\Colossal.UI.Binding.dll</HintPath>` in its own csproj (`Anarchy/Anarchy/Anarchy.csproj:78-79`, and the same two lines in every corpus mod that binds).
The toolchain does not supply it: `Mod.props` under `%CSII_TOOLPATH%` carries exactly one `<Reference Include>`, for `$(MSCORLIBPath)`, so every game assembly is the mod author's to name.

Nothing outside the `Game` assembly implements the namespace's two extension interfaces: `IJsonWritable` appears in 106 files, all under `src/Game/`, plus the four declaring/consuming files inside `Colossal.UI.Binding` itself.

The namespace's public surface splits three ways, and the split is the whole model:

- **Push** — C# owns a value, the frontend observes it. `ValueBinding<T>`, `GetterValueBinding<T>`, `RawValueBinding`, `StackBinding<T>`, `RawEventBinding`, `EventBinding`, `EventBinding<T>`, `RawMapBinding<K>`, `GetterMapBinding<K,V>`.
- **Pull with no answer** — the frontend calls, C# runs, nothing comes back. `TriggerBinding` and `TriggerBinding<T1..T4>`, `RawTriggerBinding`.
- **Request/response** — the frontend calls, C# returns a value into a promise. `CallBinding<TResult>` and its five further arities.

`CompositeBinding` is the fourth thing and is not a kind: it is a container that holds bindings and forwards `Attach`/`Detach`/`Update` to them (`src/Colossal.UI.Binding/Colossal.UI.Binding/CompositeBinding.cs:9-114`).

Rots: the class roster of `Colossal.UI.Binding` — re-list `src/Colossal.UI.Binding/Colossal.UI.Binding/`.

### Every binding is a path, and the path is `group + "." + name`

`BindingBase` takes `(string group, string name)` and computes `path = group + "." + name` once (`BindingBase.cs:24-33`). `ToString()` returns the path (`:45-48`). There is no registry of legal group names, no validation, and no uniqueness check anywhere: two bindings may share a path and both will register their handlers on the view.

The path is a plain string and dots inside `name` are legal. `CS2-WriteEverywhere` uses a group of `k45::we` with names like `main.setTabActive` (`CS2-WriteEverywhere/_Frontends/UI/k45-we-vuio/src/index.tsx:35`), which the layer treats as one opaque path.

**The game has 77 binding groups.** Derived by taking every `const string kGroup = "…"` in `src/` (76 distinct) and unioning with every string literal passed as the first constructor argument to a binding type (`editorTutorials` is the only one that appears as a literal and never as a `kGroup`). Groups whose first argument is a `group` parameter rather than a literal — 92 constructions, all in `Game.UI.Widgets`, where the widget factory is handed its owner's group — add nothing new.
The full set: `achievements app audio avatars benchmark bikesInfo budget camera chirper cityInfo climate companyInfoview debug devTree disasterInfo editor editorHierarchy editorPanel editorTool editorTutorials educationInfo electricityInfo eventJournal feature fireAndRescueInfo game garbageInfo glossary healthcareInfo infoviews input inputRebinding l10n landValueInfo levelInfo lifePath loan mapTiles menu milestone naturalResourceInfo notification options outsideInfo overlay paradox photoMode policeInfo policies pollutionInfo populationInfo postInfo prefabs production radio roadsInfo selectedInfo serviceBudget signatureBuildings statistics telecomInfo time tool toolbar toolbarBottom tooltip tourismInfo trafficInfo transportationOverview transportInfo tutorials upgradeMenu user waterInfo wealthInfo whatsnew workplaces`.

Rots: the group roster — re-derive with the two greps above.

**Every corpus mod uses its own mod id as the group**, so a mod's paths are `<ModId>.<name>`: `new ValueBindingHelper<T>(new(AnarchyMod.Id, key, …))` on the C# side (`Anarchy/Anarchy/Extensions/ExtendedUISystemBase.cs:15`), `bindValue<SelectionMode>(mod.id, "SelectionMode")` on the TS side (`Anarchy/Anarchy/UI/src/mods/AnarchyComponentsToolSections/anarchyComponentsToolSections.tsx:59`). Nothing enforces this; it is convention, and it is what keeps two mods from colliding on a path.

### The subscribe protocol, both ends, and the exact wire strings

`EventBindingBase` is the base of every push binding except the map ones. On `Attach` it registers two view events and computes a third name (`EventBindingBase.cs:20-32`):

```
updateEventName = path + ".update"
view.RegisterForEvent(path + ".subscribe",   OnSubscribe)
view.RegisterForEvent(path + ".unsubscribe", OnUnsubscribe)
```

`OnSubscribe` increments `observerCount`; `OnUnsubscribe` decrements it, floored at zero (`:45-58`). `active => observerCount > 0` (`:16`). `Attach` and `Detach` both reset the count to zero (`:29`, `:41`).

`ValueBinding<T>` and `GetterValueBinding<T>` override `OnSubscribe` to push the current value immediately, inside a try/catch that logs `"Error in value binding '<path>'"` on the `UI` logger and swallows (`ValueBinding.cs:24-35`, `GetterValueBinding.cs:34-45`). That catch exists only on the subscribe path; a throw from an ordinary `Update` is not caught here.

**A push is `BeginEvent(updateEventName, 1)`, one written value, `EndEvent()`** (`ValueBinding.cs:46-54`). Nothing is written when `active` is false, so an unobserved binding costs one boolean test.

The frontend side is `game-ui/common/data-binding/binding.ts`, registered at `DecompiledCitiesSkylines2/src-ui/source.js:25882`. `bindValue(group, name, fallback)` constructs the value-binding class at `:25323-25325` (the class at `:25570-25673`), which composes the same four strings (`:25629-25633`):

```
subscribeTrigger   = `${path}.subscribe`
unsubscribeTrigger = `${path}.unsubscribe`
updateTrigger      = `${path}.update`
patchTrigger       = `${path}.patch`
```

and whose `connect()` does `engine.on(updateTrigger, …)`, `engine.on(patchTrigger, …)`, then `engine.trigger(subscribeTrigger)` (`:25643-25657`).

**Settled live: the subscribe round trip is synchronous.** With a handler registered on `l10n.locales.update` and nothing else, `engine.trigger("l10n.locales.subscribe")` returns with the handler already called exactly once and the payload in hand. That is what lets the frontend's own wrapper read `_value` on the line after `trigger` and throw when it is still `undefined` (`:25651-25657`).

**Settled live: every `subscribe` fans the push out to every JS listener on that path, not just the new one.** Two `engine.on("time.timeSettings.update", …)` handlers, then a second `engine.trigger("time.timeSettings.subscribe")`: the first handler's count went to 2 and the second's to 1. This follows directly from `OnSubscribe` calling `TriggerUpdate()`, which emits one `.update` event that the JS event bus broadcasts. The shipped wrapper absorbs it by comparing against its cached value before notifying listeners (`:25599-25606`).

### What the frontend does when the C# binding is not there

**Settled live, and the two layers behave differently.**

At the raw engine layer, nothing happens and nothing is reported: `engine.trigger("csmodding.doesNotExist.subscribe")` returns normally, throws nothing, and no `.update` ever fires.

At the `cs2/api` layer, `bindValue` throws on first read of `.value`, with this exact text:

```
'csmodding.doesNotExist.update' was not called after subscribe!
Did you forget to add the binding on the C# side?
```

(`source.js:25651-25657`.) The map binding has the sibling message, naming the key: `'<path>.updateMapEntry' was not called after subscribing the key '<key>'!` (`:25786-25788`).
So a mod whose C# binding never registered — a typo in the group — surfaces to its frontend as that message (only when `bindValue` was given no fallback: with one, `connect()` throws only on `void 0 === this._value`, `source.js:25651-25657`, so the panel renders the fallback forever) and to no log line on the C# side at all. An `OnCreate` that threw is different: `UpdateSystem.UpdateAt` is `Register(++m_AddIndex, base.World.GetOrCreateSystemManaged<SystemType>(), phase)` (`src/Game/Game/UpdateSystem.cs:141-143`), the throw leaves `Mod.OnLoad`, and `ModManager.ModInfo.Load` records it (`src/Game/Game.Modding/ModManager.cs:138-142`) with the `Error initializing mod` log line (`:454`) and no dialog — `Dispose()` at `:453` sets `State.Disposed` (`:172`), below the notification gate at `:270`, so `Modding.log` is the whole record.

A **payload whose `__Type` no component is registered for** does not throw. The typed renderer substitutes a yellow-on-red box reading `Unknown element type <typeName>` (`source.js:49792-49796`, the three dispatchers at `:49767-49790`). That is the visible signature of a `TypeBegin` string that does not match what the frontend expects.

### Registration and teardown

`UISystemBase : GameSystemBase` is the route every UI *system* uses (`src/Game/Game.UI/UISystemBase.cs:11`), and it is 70 lines:

- `OnCreate` allocates two lists (`:22-27`).
- `AddBinding(IBinding)` appends to the system's own list **and** to `GameManager.instance.userInterface.bindings` (`:48-52`).
- `AddUpdateBinding(IUpdateBinding)` calls `AddBinding` and additionally appends to the system's `m_UpdateBindings` (`:54-58`).
- `OnUpdate` walks `m_UpdateBindings` and calls `Update()` on each (`:40-46`).
- `OnDestroy` removes every binding from the registry (`:30-37`).

`userInterface.bindings` is typed `IBindingRegistry` and is a `CompositeBinding` (`src/Game/Game.SceneFlow/UserInterface.cs:25`, `:43`, `:49`). `CompositeBinding.AddBinding` appends and, if the view is already attached, attaches the new binding immediately (`CompositeBinding.cs:42-49`); `RemoveBinding` detaches first, then removes (`:51-58`). Both wrap the attach/detach in a try/catch that logs `"Error while attaching binding {0}"` (`:92-113`).
It is not the only route: anything holding that `IBindingRegistry` can register directly, which is what `UserInterface`'s own nine groups do; `ErrorDialogManager` registers its one binding into `AppBindings`, a `CompositeBinding` child of the registry, rather than into the registry itself (`src/Game/Game.UI/ErrorDialogManager.cs:101-104`, `src/Game/Game.UI/AppBindings.cs:22`) — the same nesting, one level down. A mod does not need a `UISystemBase` to publish a binding — it needs a reference to `GameManager.instance.userInterface.bindings` and something to call `RemoveBinding` on teardown.

**A binding added through `UISystemBase.AddUpdateBinding` is *not* in the registry's update list.** `UISystemBase` only ever calls `IBindingRegistry.AddBinding`, and `CompositeBinding.AddUpdateBinding` (`:71-75`) is reachable only from `UserInterface`'s constructor. So the two pumps are separate:

- The nine top-level groups `UserInterface` creates — `LocalizationBindings`, `AppBindings`, `OverlayBindings`, `AudioBindings`, `UserBindings`, `InputBindings`, `InputActionBindings`, `InputHintBindings`, `ParadoxBindings` (`UserInterface.cs:62-70`) — are pumped by `UserInterface.Update()` → `m_Bindings.Update()` (`:90-93`), called from `GameManager.UpdateUI()` (`src/Game/Game.SceneFlow/GameManager.cs:1788-1792`).
- Every `UISystemBase` subclass's update bindings are pumped by that system's own `OnUpdate`.

**When `Update()` runs.** `UIUpdateSystem` is registered at `SystemUpdatePhase.MainLoop` (`src/Game/Game.Common/SystemOrder.cs:67`) and its whole body is `m_UpdateSystem.Update(SystemUpdatePhase.UIUpdate)` (`src/Game/Game.UI/UIUpdateSystem.cs:17-20`). That overload takes no update index (`src/Game/Game/UpdateSystem.cs:166-202`), so **`GetUpdateInterval` and `GetUpdateOffset` are ignored at `UIUpdate` and every registered UI system's `OnUpdate` runs every frame** — the interval-aware overload is a different method (`:206-247`) driven only by the simulation phases. `mod-lifecycle-and-ordering.md:314` records four corpus overrides that are dead for exactly this reason.
A throw out of a UI system's `OnUpdate` is caught per system and logged as `"System update error during {0}->{1}:"` at Critical (`UpdateSystem.cs:188-197`, the message at `:195`); the frame continues and the remaining systems still run.

**Frame order.** `GameManager.Update()` runs `UpdateWorld()` (which drives `MainLoop`, and inside it `UIUpdate`), then `UpdateUI()`, then `PostUpdateWorld()` (`GameManager.cs:702-712`, `:2385-2400`). `UpdateUI()` is `m_UIManager.Update(); userInterface.Update();` (`:1788-1792`), and `UIManager.Update()` reaches `UIView.Update()` → `m_View.Advance(...)` (`src/Colossal.UI/Colossal.UI/UIView.cs:314-320`), which is where the queued binder events are handed to the page. So a value pushed during `UIUpdate` reaches JS later in the same frame.

**Teardown.** `UISystemBase.OnDestroy` calls `base.OnDestroy()` and then removes each binding from the registry, which detaches it (`UISystemBase.cs:30-37` → `CompositeBinding.cs:51-58`). It does not `Dispose` anything: `CompositeBinding.DisposeBindings` (`:60-69`) is called only from `UserInterface.Dispose` (`UserInterface.cs:100`). A binding holding an event subscription therefore has to unhook it itself — `LocalizationBindings` implements `IDisposable` for precisely that (`src/Game/Game.UI.Localization/LocalizationBindings.cs:53-57`).

**A UI system is never destroyed by a game-mode change, and neither is the view.** `userInterface` is constructed once inside `InitializeUI`, awaited immediately after `CreateWorld()` and long before `CreateSystems()` and `InitializeModManager()` (`GameManager.cs:591`, `:593`, `:615`, `:618`, the construction at `:1762`) — and is released only from `TerminateGame` (`:795`, `ReleaseUI` at `:1794-1799`). `DestroyWorld` is likewise called only at quit or on the `MonoBehaviour`'s `OnDestroy` (`:750-756`, `:793`).
So a mod's `AddBinding` in `OnLoad`-created systems always finds a live `userInterface`, and its bindings stay registered and attached for the whole process. Loading a save, returning to the main menu and entering the editor change none of that.

The one thing that does detach is navigation: `UserInterface.OnNavigateTo` calls `m_Bindings.Detach()` (`:130-134`), and `OnReadyForBindings` re-attaches the whole composite (`:122-128`). A view reload therefore re-attaches every binding and resets every `observerCount` to zero (`EventBindingBase.cs:29`).

Rots: the nine top-level binding groups `UserInterface` constructs — re-read `src/Game/Game.SceneFlow/UserInterface.cs:62-70`.

### The game-mode gate, and what it does and does not stop

`UISystemBase.gameMode` defaults to `GameMode.All` (`UISystemBase.cs:19`), and `OnGamePreload` sets `base.Enabled = (gameMode & mode) != 0` (`:60-64`).
`GameMode` is `[Flags]`: `None = 0, Other = 1, Game = 2, Editor = 4, MainMenu = 8, GameOrEditor = 6, All = 0xF` (`src/Game/Game/GameMode.cs:5-15`).
`OnGamePreload` is dispatched by `GameSystemBase` from `GameManager.onGamePreload` inside a try/catch that disables the system on a throw (`src/Game/Game/GameSystemBase.cs:85-96`); `OnGameLoadingComplete` is the sibling hook, fired from `onGameLoadingComplete` after the load (`:60-70`, `GameManager.cs:1126`), and `UISystemBase` does not override it.

**Seventeen vanilla UI systems override `gameMode`**, and the whole set is: `EditorBottomBarUISystem`, `EditorErrorPanelSystem`, `EditorHierarchyUISystem`, `EditorToolUISystem` and `MapRequirementSystem` at `Editor`; `EditorPanelUISystem`, `InfoviewsUISystem`, `NaturalResourcesInfoviewUISystem`, `PollutionInfoviewUISystem` and `TooltipUISystem` at `GameOrEditor`; `InfoSectionBase`, `InfoviewUISystemBase`, `PrefabUISystem`, `SelectedInfoUISystem`, `ToolbarUISystem`, `TransportationOverviewUISystem` and `UpgradeMenuUISystem` at `Game`.

Rots: that roster — re-derive with `grep -rn "override GameMode gameMode" src/`.

**What the gate stops is `OnUpdate`, and nothing else.** `Enabled = false` only skips the system's update; the bindings it registered in `OnCreate` are still in the registry and still attached to the view, so `subscribe` still reaches them and `ValueBinding`/`GetterValueBinding` still push their current value on subscribe. A `GetterValueBinding` on a disabled system runs its getter on the first-ever subscribe and answers every later one from its cached value — `TriggerUpdate()` calls the getter only when `m_ValueDirty` is set (`GetterValueBinding.cs:74-85`), the field initialiser sets it once (`private bool m_ValueDirty = true;`, `:14`), and afterwards only the inactive branch of `Update()` sets it (`:60-63`), which a disabled system never runs — and then never updates again until the mode changes back.
The same is true of `RequireForUpdate<T>`, which several vanilla UI systems use (`src/Game/Game.UI.InGame/ServiceBudgetUISystem.cs:107-110`): a missing singleton silences the pump without unregistering anything.

### Every binding kind, with its signature and its use

Counts below are occurrences of the constructor across `src/` (the game) and across the 22-repository corpus, both counted with the same `grep -o` over `new <Type>(`.

| Kind | Constructor | Game | Corpus (repos) |
| --- | --- | --- | --- |
| `ValueBinding<T>` | `(group, name, T initialValue, IWriter<T> writer = null, EqualityComparer<T> comparer = null)` | 188 | 94 (9) |
| `GetterValueBinding<T>` | `(group, name, Func<T> getter, IWriter<T> writer = null, EqualityComparer<T> comparer = null)` | 257 | 93 (16) |
| `RawValueBinding` | `(group, name, Action<IJsonWriter> writerDelegate)` | 75 | 10 (2) |
| `TriggerBinding` | `(group, name, Action callback)` | 87 | 83 (15) |
| `TriggerBinding<T1..T4>` | `(group, name, Action<…> callback, IReader<T1> reader1 = null, …)` | 222 | 161 (19) |
| `RawTriggerBinding` | `(group, name, Action<IJsonReader> callback)` | 1 | 0 |
| `CallBinding<TResult>` … `<T1,T2,T3,T4,T5,TResult>` | `(group, name, Func<…,TResult> callback, IReader<T1> reader1 = null, …)` | 11 | 0 |
| `RawMapBinding<K>` | `(group, name, Action<IJsonWriter,K> onRequestUpdate, IReader<K> keyReader = null, IWriter<K> keyWriter = null)` | 34 | 2 (1) |
| `GetterMapBinding<K,V>` | `(group, name, Func<K,V> getter, IReader<K> keyReader = null, IWriter<K> keyWriter = null, IWriter<V> valueWriter = null, EqualityComparer<V> comparer = null)` | 11 | 0 |
| `EventBinding` | `(group, name)` | 5 | 0 |
| `EventBinding<T>` | `(group, name, IWriter<T> writer = null)` | 7 | 1 (1) |
| `RawEventBinding` | `(group, name)` | 4 | 0 |
| `StackBinding<T>` | `(group, name, IWriter<T> elementWriter = null)` | 1 | 0 |

Signatures are from `ValueBinding.cs:16`, `GetterValueBinding.cs:26`, `RawValueBinding.cs:15`, `TriggerBinding.cs:15/51/79/111/147`, `RawTriggerBinding.cs:10`, `CallBinding.cs:10/35/64/97/134/175`, `RawMapBinding.cs:9`, `GetterMapBinding.cs:18`, `EventBinding.cs:5/19`, `RawEventBinding.cs:5`, `StackBinding.cs:26`.

**Verdict on the survey's binding census.** `survey-mods-techniques.md:286` says the corpus uses `ValueBinding<T>`, `GetterValueBinding<T>` and `TriggerBinding` arity 0-4, and that "`RawValueBinding` / `CallBinding` are **not** used anywhere."
At twelve repositories that was right; at twenty-two it is half right. `RawValueBinding` is constructed ten times across two repositories — six in `InfoLoom` (`InfoLoom/InfoLoom/Systems/InfoviewUISystems/ILEducationInfoviewUISystem.cs:130/132`, `Systems/SankeyUISystems/BudgetUISankeySystem.cs:42`, `Systems/SankeyUISystems/WorkforcePipelineSankeySystem.cs:315`, `Systems/UI/InfoLoomUISystem.cs:284`, `Systems/IndustrialSystems/StorageCompanies/Systems/StoragePropertyCompanies.cs:44`) and four in `Time2Work` (`Time2Work/NightShift/Systems/SpecialEventsUISystem.cs:114` and three in `Systems/Time2WorkStatisticsUISystem.cs`) — and `RawMapBinding<Entity>` twice, both in `Time2Work/NightShift/Systems/Time2WorkStatisticsUISystem.cs:84` and `:125`, the second delegating straight to the vanilla `PrefabUISystem.BindPrefabRequirements`. `CallBinding` remains at zero, and so do `RawTriggerBinding`, `RawEventBinding`, `GetterMapBinding` and `StackBinding`.
The corpus's single `EventBinding` construction is not a mod-owned binding at all: `Time2Work/NightShift/Systems/Time2WorkTimeUISystem.cs:73` registers `new EventBinding<bool>("time", "simulationPausedBarrier")` — the vanilla group and the vanilla name — because that file is a fork of `TimeUISystem` and has to re-publish the barrier the original owned. Two bindings on one path is legal (there is no uniqueness check), and both will receive every subscribe.

**The one class the survey names that does not exist is `JsonWriter.FalseEqualityComparer<T>`** (`survey-mods-techniques.md:286`). Grepping the whole decompile for `FalseEqualityComparer` returns nothing, and `JsonWriter` (`JsonWriter.cs:6-117`) is a thin `IJsonWriter` over `cohtmlNative` with no nested types at all. The real artifact is a mod's own class: `HallOfFame/HallOfFame/Utils/AlwaysFalseEqualityComparer.cs:5-9`, `EqualityComparer<T>` with `Equals => false` and `GetHashCode => 0`, passed as the `comparer` argument at `HallOfFame/HallOfFame/Utils/InputActionBinding.cs:57`. `HallOfFame/HallOfFame/Systems/CommonUISystem.cs:256-259` carries a second, `FakeSettingComparer`, differing only in keeping the real hash code. **The game itself never passes a custom comparer to any binding.**

### `ValueBinding<T>` versus `GetterValueBinding<T>`: the difference that decides which to use

`ValueBinding<T>.Update(newValue)` compares against the held value with the comparer and pushes only on a difference (`ValueBinding.cs:37-44`). `TriggerUpdate()` pushes unconditionally when active (`:46-54`). The value is C#-owned and the frontend never sees a redundant push, but nothing polls: if the owner forgets to call `Update`, the binding is stale forever.

`GetterValueBinding<T>.Update()` — the `IUpdateBinding` method, no argument — calls the getter, compares against the previous value, and pushes on a difference, returning whether it pushed (`GetterValueBinding.cs:47-66`). **The getter runs on every pump whether or not anything changed**, so the getter's cost is paid every frame the binding is observed. When not active it sets `m_ValueDirty = true` (`:60-63`) so the next subscribe re-reads rather than trusting a stale cache.

Three consequences worth stating plainly:

- **`ValueBinding<T>` with a mutable reference type never pushes on mutation.** `EqualityComparer<T>.Default` on a class without `IEquatable<T>` is reference equality, so `Update(sameInstanceMutated)` compares equal and returns. This is why `StackBinding<T>` mutates its list and then calls `m_Binding.TriggerUpdate()` directly rather than `Update` (`StackBinding.cs:48-74`).
- **A struct payload wants `IEquatable<T>`.** Without it the default comparer falls back to `ValueType.Equals`, which boxes and compares field by field reflectively. `TimeUISystem.TimeSettings : IJsonWritable, IEquatable<TimeSettings>` declares it with a hand-written `Equals` (`src/Game/Game.UI.InGame/TimeUISystem.cs:19`, `:43-50`); most vanilla payload structs do not — `AppBindings.FrameTiming` (`src/Game/Game.UI/AppBindings.cs:24`, bound at `:197`), `EmploymentData` (`src/Game/Game.UI.InGame/EmploymentData.cs:9`), `UIMapTileResource` (`src/Game/Game.UI.InGame/MapTilesUISystem.cs:18`) and `SaveabilityStatus` (`src/Game/Game.UI.Menu/MenuUISystem.cs:229`) are all `IJsonWritable` alone.
- **The always-false comparer is the escape hatch when the value is genuinely opaque**, and the corpus's one use is exactly that case: `ProxyBinding` is recreated whole every time the player rebinds a key, so `HallOfFame` forces a push rather than trying to define equality on it (`HallOfFame/HallOfFame/Utils/InputActionBinding.cs:45-65`, the comparer argument at `:57`).

**The engine once policed every-frame pushes and the check is unreachable.** `GetterValueBinding` declares `private const int MAX_CONSECUTIVE_UPDATES = 100`, a `HashSet<string> m_LoggedWarnings`, and `private bool CheckConsecutiveUpdates()` returning `m_ConsecutiveUpdates > 100` (`GetterValueBinding.cs:20`, `:22`, `:68-72`). Grepping all of `src/` for each of the three names returns only those declarations: nothing calls `CheckConsecutiveUpdates`, nothing reads `m_LoggedWarnings`, and `m_ConsecutiveUpdates` is only ever zeroed (`:64`). So there is no warning and no cap; a binding that pushes on all 100 consecutive frames does so in silence.

**The game's own default is push-on-change, not poll.** Across `src/`, `AddBinding(` appears 766 times against 156 for `AddUpdateBinding(`. Of 75 `RawValueBinding` constructions, 73 are registered with `AddBinding` and driven by an explicit `Update()` call from the owning system; exactly one is registered with `AddUpdateBinding` (`src/Game/Game.UI.InGame/ServiceBudgetUISystem.cs:111`). That matters because **`RawValueBinding.Update()` has no comparison at all** — it writes the whole payload every time it is called (`RawValueBinding.cs:47-56`), so registering one with `AddUpdateBinding` serialises the entire structure into the binder every frame it is observed.

**The game's own throttle for expensive UI work is `UIUpdateState`**, not the binding layer. `UIUpdateState.Create(world, updateInterval)` holds a simulation-frame counter and `Advance()` returns true only once the interval has elapsed or `ForceUpdate()` was called (`src/Game/Game.UI/UIUpdateState.cs:16-43`). Six systems use it, all at an interval of 256 simulation frames: `InfoviewUISystemBase.cs:32` (whose `OnUpdate` is `if (Active && (Modified || m_UpdateState.Advance())) PerformUpdate();`, `:38-43`), `CityInfoUISystem.cs:117`, `ProductionUISystem.cs:202`, `SelectedInfoUISystem.cs:181` and two more. It is an ordinary class a mod can construct.

### The reader and writer registries, and the four asymmetries in them

`ValueWriters` and `ValueReaders` are static `Dictionary<Type, object>` registries with public `Register` overloads and a `Create<T>()` that throws when it cannot resolve (`ValueWriters.cs:39-79`, `ValueReaders.cs:46-84`).

`Create(Type)` resolves in a fixed order — registry hit, then `IJsonWritable`/`IJsonReadable` (wrapped in `ValueWriter<>`/`ValueReader<>`), then array, then list, then dictionary, then `throw new ArgumentException($"Unable to create writer for type {type}")` (`ValueWriters.cs:54-79`, `ValueReaders.cs:61-84`).

**What is registered out of the box** (both static constructors, `ValueWriters.cs:11-37` and `ValueReaders.cs:10-44`):
`bool`, `int`, `uint`, `float`, `double`, `string`; then `MathematicsWriters.Register()` / `MathematicsReaders.Register()` for `int2 int3 int4 float2 float3 float4 quaternion Vector2 Vector3 Vector4 Vector2Int Vector3Int Bounds1 Bounds2 Bounds3 Bezier4x3` (`MathematicsWriters.cs:9-27`, `MathematicsReaders.cs:9-27`); then `UnityWriters.Register()` / `UnityReaders.Register()` for `Entity`, `Color`, `Color32`, `Keyframe`, `AnimationCurve`, and readers only for `Keyframe[]` (`UnityWriters.cs:8-15`, `UnityReaders.cs:9-17`).

The four asymmetries, all of them traps:

1. **`long` and `ulong` are registered as readers and not as writers.** `ValueReaders` registers both as plain `reader.Read(out value)` (`ValueReaders.cs:25-32`); `ValueWriters` registers neither. So `new ValueBinding<long>(group, name, 0L)` throws `Unable to create writer for type System.Int64` at construction, while `new TriggerBinding<long>(…)` works. The writer you are expected to pass is `LongWriter`, which encodes as a two-element array of 32-bit halves, low then high (`LongWriter.cs:10-16`, `ULongWriter.cs:10-16`) — because JavaScript numbers lose integers above 2^53. Its counterpart `LongReader.ReadFromArray` **requires** that array and throws `"long numbers need to be represented by an array of length 2"` on anything else (`LongReader.cs:13-36`). The default registered reader and the array writer therefore disagree about representation; pair `LongWriter` with an explicit `LongReader`, or neither. The game's only user of the pair is the editor's `EnumField` (`src/Game/Game.UI.Widgets/EnumField.cs:40-41`, `EnumMember.cs:25`).
2. **Enums resolve to nothing on either side.** An enum is not registered, does not implement `IJsonWritable`, and is not array/list/dictionary, so `Create` throws. `EnumWriter<T>` writes the int (`EnumWriter.cs:5-10`), `EnumNameWriter<T>` writes `Enum.GetName` (`EnumNameWriter.cs:5-10`), `EnumReader<T>` reads an int and casts (`EnumReader.cs:5-11`); all three must be passed explicitly. The game does it both ways — an inline `DelegateWriter` casting to int (`src/Game/Game.UI.InGame/TimeUISystem.cs:87-90`), and `new EnumNameWriter<AccountLinkProvider>()` where the frontend wants the member name (`src/Game/Game.UI.Menu/ParadoxBindings.cs:351`). A mod's copy of the community helper handles it with `initialValue is Enum ? new EnumReader<T>() : null` (`BetterBulldozer/BetterBulldozer/Extensions/ExtendedUISystemBase.cs:25`).
3. **`string` is nullable on read and non-nullable on write.** `ValueReaders` registers `new StringReader().Nullable()` (`ValueReaders.cs:41`); `ValueWriters` registers a bare `StringWriter` (`ValueWriters.cs:34`), whose `Write` on a null **writes the null and then throws** `ArgumentNullException("value", "Null passed to non-nullable string writer")` (`StringWriter.cs:7-16`). Since `ValueBinding.TriggerUpdate` has no try/catch around the writer (`ValueBinding.cs:46-54`), the throw escapes with `EndEvent()` never called. `ValueWriter<T>`, `ArrayWriter<T>`, `ListWriter<T>`, `CollectionWriter<T>` and `DictionaryWriter<K,V>` all do the same write-then-throw on null (`ValueWriter.cs:7-16`, `ArrayWriter.cs:16-31`, `ListWriter.cs:17-32`, `CollectionWriter.cs:17-32`, `DictionaryWriter.cs:21-36`).
   The fix is the wrapper, and the game uses it 27 times: `ValueWriters.Nullable(new StringWriter())` (`src/Game/Game.UI/AppBindings.cs:194`, `:199`; `src/Game/Game.UI.InGame/RadioUISystem.cs:82-83`; `src/Game/Game.UI.Menu/MenuUISystem.cs:401-402`; and twenty more), `ValueWriters.Nullable(new ValueWriter<SaveInfo>())` (`AppBindings.cs:208`), `new NullableWriter<string[]>(new ArrayWriter<string>())` (`AppBindings.cs:209`).
4. **`Nullable` means two different things on the two sides of the value/reference divide.** `ValueWriters.Nullable<T>` is constrained `where T : class` and its `NullableWriter<T>` constructor throws `"Cannot create nullable writer for non-nullable type T!"` if `T` turns out to be a value type (`ValueWriters.cs:91-94`, `NullableWriter.cs:11-16`). For a struct you need the other extension class entirely: `ValueWritersStruct.Nullable<T>` where `T : struct`, giving `NullableStructWriter<T> : IWriter<T?>` (`ValueWritersStruct.cs:5-8`, `NullableStructWriter.cs:5-24`). `ValueReaders.Nullable<T>` is unconstrained but its `NullableReader<T>` throws the same way for a value type (`ValueReaders.cs:104-107`, `NullableReader.cs:10-17`), so there is no nullable reader for a struct at all.

**No mod in the corpus calls `ValueWriters.Register` or `ValueReaders.Register`.** Both are `public static` and both take either an `IWriter<T>`/`IReader<T>` or a `WriterDelegate<T>`/`ReaderDelegate<T>`, so registering a type once at `OnLoad` and never passing a writer again is fully supported. Zero occurrences across all 22 repositories. Three mods instead reach the private backing dictionary by reflection: `(Dictionary<Type, object>)typeof(ValueReaders).GetField("s_Readers", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)` (`NodeController/NodeController/Extensions/GenericUIReader.cs:13`, `FindIt-CSII/FindIt/Utilities/ExtendedUISystemBase.cs:323`, and `RoadBuilder-CSII` through the same `GenericUIReader<T>.Create()` shape). They do it to get a *fallback* where `Create<T>` would throw, which `Register` would have given them without reflection.

### How a custom type crosses, and what `__Type` really is

`IJsonWritable` is one method, `void Write(IJsonWriter writer)` (`IJsonWritable.cs:3-6`); `IJsonReadable` is `void Read(IJsonReader reader)` (`IJsonReadable.cs:3-6`). Implementing the first makes `ValueWriters.Create<T>()` resolve to `ValueWriter<T>`; implementing the second plus a public parameterless constructor makes `ValueReaders.Create<T>()` resolve to `ValueReader<T>`, which is constrained `where T : IJsonReadable, new()` (`ValueWriter.cs:5`, `ValueReader.cs:6`).

`IJsonWriter` is a `string debugName { get; }` (`IJsonWriter.cs:5`) and sixteen methods, no more (`IJsonWriter.cs:3-38`): `TypeBegin(string) / TypeEnd()`, `MapBegin(uint) / MapEnd()`, `ArrayBegin(uint) / ArrayEnd()`, `PropertyName(string)`, `WriteNull()`, and `Write` for `bool int uint long ulong float double string`. `JsonWriterExtensions` adds `int`-taking `ArrayBegin`/`MapBegin`, `WriteEmptyArray`, `WriteEmptyMap`, `Write(string[])`, `Write(int[])`, `Write<T>(T) where T : IJsonWritable`, `Write<T>(T?) where T : struct, IJsonWritable`, `WriteNullable<T>(T) where T : class, IJsonWritable`, `Write<T>(IList<T>)`, `Write(IList<string>)` and three `IReadOnlyDictionary` overloads (`JsonWriterExtensions.cs:6-182`).

The canonical shape is `TypeBegin` / a `PropertyName` + value pair per field / `TypeEnd`:

```csharp
public void Write(IJsonWriter writer)
{
    writer.TypeBegin(GetType().FullName);
    writer.PropertyName("ticksPerDay"); writer.Write(ticksPerDay);
    writer.PropertyName("daysPerYear"); writer.Write(daysPerYear);
    writer.TypeEnd();
}
```

(`src/Game/Game.UI.InGame/TimeUISystem.cs:29-41`.)

**Settled live: `TypeBegin(s)` arrives as a `__Type` string property on the JS object; `MapBegin` arrives as a plain object with no `__Type`; `ArrayBegin` arrives as an array.** Read off `time.timeSettings`, `options.layoutMap` and `l10n.locales` respectively:

```json
{ "__Type": "Game.UI.InGame.TimeUISystem+TimeSettings", "ticksPerDay": 262144, "daysPerYear": 12,
  "epochTicks": 1387181, "epochYear": 2024 }
{ "backquote": { "__Type": "Game.Input.ControlPath", "name": "²", "device": "None", "displayName": "²" }, … }
```

Two things the first payload proves: `GetType().FullName` on a nested type yields the CLR `Outer+Inner` spelling and that `+` reaches the frontend verbatim (the bundle switches on `"Game.UI.InGame.LineVisualizerSection+LineVehicle"` at `source.js:116929`), and the `__Type` string is data rather than reflection.

**The `__Type` string is a free-form contract and is often not a C# type name.** Of the 121 distinct `TypeBegin` string literals in `src/`, most are `<group>.<TypeName>` short forms — `chirper.Chirp`, `toolbar.AssetCategory`, `prefabs.CityModifier`, `milestone.Milestone`, `tool.UITool` (confirmed live on `tool.activeTool`, which arrives as `{"__Type":"tool.UITool", "id":"Default Tool", …}`). Three of them name a namespace the game does not have: `Game.UI.InGame.IntProperty` writes `TypeBegin("Game.UI.Common.NumberProperty")` (`src/Game/Game.UI.InGame/IntProperty.cs:21`), `Int2Property` writes `Game.UI.Common.Number2Property` (`src/Game/Game.UI.InGame/Int2Property.cs:22`), and `StringProperty` writes `Game.UI.Common.StringProperty` (`src/Game/Game.UI.InGame/StringProperty.cs:17`). `Game.UI.Common` exists nowhere under `src/`, and the scaffold declares all three by their wire names (`bindings.d.ts:1076`, `:1085`, `:1103`).
A few of the short-form tags are gathered in `src/Game/Game.UI/TypeNames.cs` (40 lines of `public static readonly string k…`, 17 tags); the rest of the 121 literals sit inline in their owning file (`production.Resource`, `serviceBudget.Service`, `toolbar.Asset`, …), with `src/Game/Game.UI/PropertyNames.cs` (168 lines) doing the same for property names.

**Of the 278 `TypeBegin` call sites in `src/`, 132 pass a string literal and 146 do not** — 76 pass `GetType().FullName`, and the rest pass `typeof(X).FullName`, a field, or a parameter (`widget.propertiesTypeName` at `src/Game/Game.UI.Widgets/Widget.cs:133/172`, itself `GetType().FullName` by default at `:74`).

**A generic writable must use a literal.** `LocalizedString` (non-generic) uses `GetType().FullName` (`src/Game/Game.UI.Localization/LocalizedString.cs:78`), but `LocalizedNumber<T>` hard-codes `TypeBegin("Game.UI.Localization.LocalizedNumber")` (`LocalizedNumber.cs:29`), as do `LocalizedFraction<T>` (`:29`) and `LocalizedBounds<T>` (`:29`). What forces the literal is exact string equality, not the prefix regex: the localized-element dispatch is `switch (t.__Type) { case au.Number: … default: return "<INVALID TYPE>" }` (`source.js:29634-29647`, constants at `:29431-29436`) and the typed renderer is an exact key lookup `e[t.__Type]` falling through to the unknown-element box (`:49769-49773`, `:49792-49796`). The prefix regex `new RegExp("^" + t + ",?")` (`:29983-29989`) has no end anchor, so a generic `Type.FullName` with its backtick arity suffix WOULD match it — but that regex is `isBindingType`, applied only to the `names.*` union and the infoview menu tag (`:29994`, `:30047`, `:30054`, `:35766`), never to a localized element. `localization.md` carries the matching frontend dispatch for these four (`source.js:29431-29436`, `:29634-29647`) as this topic's worked example; reuse it rather than re-deriving.

`Entity` is the one payload every mod sends and it is pre-registered: `TypeBegin("Unity.Entities.Entity")`, `index`, `version` (`UnityWriters.cs:17-25`), read back symmetrically (`UnityReaders.cs:19-27`). Confirmed live on `selectedInfo.selectedEntity`.

**A custom reader is any `IReader<T>`, and a delegate does as well as a class.** `ReaderDelegate<T>` (`ReaderDelegate.cs:3`) and `DelegateReader<T>` (`DelegateReader.cs:6-14`) are public, `ValueReaders.Register<T>(ReaderDelegate<T>)` exists (`ValueReaders.cs:46-49`), and the static constructor registers every primitive as an anonymous method (`:13-40`). The game's only nested class example is seven lines: `private class PlayerResourceReader : IReader<PlayerResource> { public void Read(IJsonReader reader, out PlayerResource value) { reader.Read(out int value2); value = (PlayerResource)value2; } }` (`src/Game/Game.UI.InGame/ServiceBudgetUISystem.cs:51-57`), passed inline at `:114`. Its writer counterparts are `LoanUISystem.LoanWriter : IWriter<LoanInfo>` (`src/Game/Game.UI.InGame/LoanUISystem.cs:11`) and `ModdingToolchainDependencyWriter : IWriter<IToolchainDependency>` (`src/Game/Game.UI.Menu/ModdingToolchainDependencyWriter.cs:7`) — those two and `DebugBindingWriter` (`DebugBindingWriter.cs:3-15`) are the only hand-written `IWriter<>` implementations in the game.

Rots: the `__Type` strings themselves. Every one is a shipped string on both ends; re-derive with `grep -rhoP 'TypeBegin\("\K[^"]+' src/` and compare against the scaffold's `bindings.d.ts`.

### The sweep of `types/bindings.d.ts` against the C# writers

**Method and reach.** Two matchings were run over `@colossalorder/create-csii-ui-mod/template/types/bindings.d.ts` (3,646 lines, the largest of the ten declaration files; the other nine total 2,096 lines).

*Matching one, on wire tags.* Every dotted string literal in `bindings.d.ts` — the `__Type` tags it declares as enum values and `const`s — was extracted (`grep -hoP '"[A-Za-z][\w.+]*\.[\w.+]+"'`): **186 distinct**. Against them, the 121 distinct `TypeBegin` string literals in `src/` (from 132 literal-argument call sites), plus a per-name resolution of every `Game.*` tag to a C# type declared in the namespace directory of the same name.
Result: **185 of 186 have a C# producer.** 35 appear verbatim as a `TypeBegin` literal; the other 150 are `Game.*` names resolving to a C# type whose writer emits `GetType().FullName` or `typeof(X).FullName`.
The one that does not is **`prefabs.AdjustHappinessEffect`** (`bindings.d.ts:1119`, the interface at `:1214-1218` with fields `targets: string[]`, `wellbeingEffect: number`, `healthEffect: number`, the union member at `:1125`). `AdjustHappiness`, `wellbeingEffect` and `healthEffect` appear nowhere in the decompiled game. The shipped bundle declares and renders it (`source.js:44259`, `:89333`), so both frontend artifacts agree and the C# producer is absent at 1.6.0f1.
Unconfirmed: whether it is a removed effect the frontend has not caught up with or one a content pack supplies. The decompile covers 164 assemblies against 243 managed DLLs in the install, so a producer in an undecompiled assembly is not excluded; decompiling the remaining DLLs and grepping for `wellbeingEffect` settles it.

The reverse direction of the same matching: of the 121 C# `TypeBegin` literals, 35 are quoted in `bindings.d.ts`, 60 more correspond to a declaration there or in a sibling `.d.ts` under a name rather than as a quoted tag, and **26 have no declaration under any name in any of the ten files** — `achievements.Achievement`, `avatars.AvatarData`, `CloudinessRange`, `debug.DistributionBucket`, `debug.DistributionWatch`, `debug.HistoryWatch`, `debug.WatchHistoryValue`, `devTree.Node`, `devTree.NodeDetails`, `EditorError`, `eventJournal.UIEventData`, `ExternalLinkData`, `Game.UI.InGame.UITransportLine`, `HierarchyItem`, `infoviews.ColorLegend`, `Keyframe`, `LocalizationFieldEntry`, `mapTiles.UIMapTileResource`, `menu.ThemeInfo`, `milestone.XPMessage`, `production.ProductionCompanyInfo`, `radio.Clip`, `SaveabilityStatus`, `SeasonBoundary`, `statistics.ChartDataSets`, `tool.UITool`. Several of those are near-misses the scaffold renamed (`DevTreeNode`, `XpMessage`, `RadioClip`), so the honest reading is that name matching is a lower bound on agreement rather than a defect count.

*Matching two, on type names.* All 126 distinct C# type names implementing `IJsonWritable` (from `grep -rhnE '(struct|class)\s+\w+.*\bIJsonWritable\b'` over `src/`, minus the one `where T : IJsonWritable` constraint match) against all 509 type names declared across the ten `.d.ts` files.
Result: **84 of 126 C# writable type names are declared nowhere in the scaffold.** The undeclared side is coherent rather than random — it is the editor, the developer menu, the benchmark and the platform shell: `AnimationCurveField ClimateCurveField DirectoryPickerField DirectoryPickerButton PopupSearchField SearchField Int2Property IntProperty IntRangeProperty UpkeepIntProperty UpkeepInt2Property FlexLayout PageLayout PagedList Viewport ViewportItem HierarchyItem EditorError EditorTool Watch DistributionBucket DataPoint FrameTiming BenchmarkResultBinding BenchmarkMetricStatsBinding GameOptions DefaultGameOptions GameModeInfo ScreenResolution ParadoxDialog ModBinding ErrorDialog ConfirmationDialogBase MapInfo SaveInfo ThemeInfo TemperatureData PrecipitationData CloudinessData AuroraData FogData ProxyBinding ControlPath InputHint InputHintItem InputHintQuery TutorialInputHintQuery`, and 37 more.
Conversely 155 of `bindings.d.ts`'s 375 declared interfaces, enums and type aliases have no C# type of the same name, which is expected: many are frontend-only shapes (`FocusKey`, `UniqueFocusKey`, `ChartData`, `TooltipPos`), several are renames of a C# type (`NumberProperty` for `IntProperty`, `Number2Property` for `Int2Property`, `UpkeepNumberProperty` for `UpkeepIntProperty`, `XpMessage` for the tag `milestone.XPMessage`, `DevTreeNode` for `devTree.Node`, `RadioClip` for `radio.Clip`), and the rest are the `Typed<T>`/`TypeFromMap<T>` machinery (`bindings.d.ts:51-56`).

**What the sweep establishes for a mod author**: `bindings.d.ts` is the wire format written down, and it is written down only for what the *game's own React code consumes through the public modules*. It is not a census of what the C# side can emit, and a payload type absent from it is not thereby absent from the game.

### The request/response escape hatch

`CallBinding<…,TResult>` registers with `view.BindCall(path, Func<TResult>)` rather than `RegisterForEvent` (`RawCallBindingBase.cs:22-27`), and its `Callback` logs `"Error in call binding callback '<path>'"` and then **rethrows** (`CallBinding.cs:18-27`) — the only binding kind that deliberately re-raises after logging, where every trigger binding logs and swallows (`TriggerBinding.cs:21-31`, `RawTriggerBinding.cs:16-26`).

On the frontend, `call(group, name, ...args)` is `engine.call(`${group}.${name}`, ...args)` (`source.js:25349-25351`), and `engine.call` allocates a request id, sends the message and returns a `Promise` (`:163-173`). Resolution comes back through `_Result` (`:175`); a name with no C# handler rejects, on the next animation frame, with the string `No handler registered with name '<path>'` (`:183-189`).

**Settled live**, both halves: `await engine.call("app.arePrerequisitesMet", null)` returned `true`, and `await engine.call("csmodding.noSuchCall", 1)` rejected with exactly that message.

The game registers eleven call bindings: `app.arePrerequisitesMet` (`src/Game/Game.UI/AppBindings.cs:210`), `editorTutorials.getSubstituteString` (`src/Game/Game.UI.Editor/EditorTutorialsUISystem.cs:56`), `menu.getMapResourceMultipliers` (`src/Game/Game.UI.Menu/MenuUISystem.cs:416`), `paradox.canClickPrerequisite` (`src/Game/Game.UI.Menu/ParadoxBindings.cs:378`), five on `cinematicCamera` (`src/Game/Game.UI.InGame/CinematicCameraUISystem.cs:106-114`), and two on the editor's animation-curve widget (`src/Game/Game.UI.Widgets/AnimationCurveField.cs:15`, `:43`).
The pattern across all eleven is the same: **the answer is a value the frontend cannot compute and does not want to cache** — is this DLC owned, what index did the keyframe end up at, what does this controller stick read right now.

**When it beats a trigger-plus-value round trip.** A trigger plus a `ValueBinding` gives you an answer too, but the answer arrives as a push with no correlation to the request, so two callers cannot tell whose answer they got, and a value equal to the last one is deduplicated away by the comparer and never arrives at all. `engine.call` correlates by request id and always resolves. Against it: the C# callback runs on the main thread inside `View.Advance` like every other binding callback (`src/Colossal.UI/Colossal.UI/UIView.cs:314-320`, driven from `GameManager.UpdateUI()` at `GameManager.cs:1788-1792`), so an expensive answer costs a frame; and the corpus does not use `CallBinding` at all — `CS2-WriteEverywhere` is the corpus's only request/response channel and it registers its 26-plus named delegates through its own `IBelzontBindable.SetupCallBinder(Action<string, Delegate>)` abstraction (`CS2-WriteEverywhere/BelzontWE/Controllers/FileController.cs:12-16`, `Controllers/Library/WEFontManagementController.cs:20-27`), consumed as `await engine.call("k45::we.file.listFiles", folder, ext)` (`CS2-WriteEverywhere/_Frontends/UI/k45-we-vuio/src/services/FileService.tsx:10`).
Unconfirmed: whether that abstraction bottoms out in `CallBinding` or in a direct `View.BindCall`. Its implementation is in the `BelzontWE/Commons` submodule, which is declared in `.gitmodules` and empty in the checkout — the same limitation `mod-lifecycle-and-ordering.md:458` records for this repository. Cloning with `--recurse-submodules` settles it.

### Map bindings: a keyed value the frontend subscribes to one key at a time

`MapBindingBase<K>` uses a **different verb set** from every other push binding (`MapBindingBase.cs:36`, `:49-50`):

```
path + ".subscribeMapEntry"     (frontend sends the key)
path + ".unsubscribeMapEntry"   (frontend sends the key and a keepAlive bool)
path + ".updateMapEntry"        (C# sends key, then value)
```

`OnSubscribe` reads the key, bumps that key's observer count, updates and pushes (`:68-91`); `OnUnsubscribe` reads the key **and a trailing bool**, and only drops the key when the count reaches zero *and* that bool is false (`:93-109`). `Update()` (the `IUpdateBinding` method) calls `UpdateAll()`, which walks every observed key (`:120-141`); `Update(K key)` pushes one (`:143-150`). A write is `BeginEvent(updateEventName, 2)`, the key, then the value (`:154-164`).

**Every map binding secretly registers a second, ordinary binding under the same path.** `MapBindingBase`'s constructor builds `m_DebugValue = new RawValueBinding(group, name, BindDebug)` (`:40`), attached and detached alongside (`:51`, `:56`), which serialises the whole observed set as an array of `[key, value]` pairs (`:166-177`). So `<group>.<name>.subscribe` on a map binding answers with the currently-observed entries, and `debugType` is delegated to it (`:29`).
**Settled live**: with one key held on `l10n.indexCounts`, `engine.trigger("l10n.indexCounts.subscribe")` returned `[["Loading.HINTMESSAGE", 38]]`. With no key held it returns an empty array — distinguishable from a missing binding, which fires no `.update` at all, but read by a probe as an empty value binding rather than as the keyed one it is; the trap for anyone probing a map binding with the value-binding verb.

`RawMapBinding<K>` always reports the key as changed and re-serialises on every ask (`RawMapBinding.cs:15-18`); `GetterMapBinding<K,V>` caches per key and compares with the comparer (`GetterMapBinding.cs:27-59`); its protected `TriggerUpdate(K)` throws `$"Attempted to trigger update for unsubscribed key {key}"` (`:58`), but every public path guards ahead of it — `Update(K key)` is `if (m_ObserverCounts.TryGetValue(key, out var value) && value > 0 && UpdateValue(key))` (`MapBindingBase.cs:143-145`) and `UpdateAll()` walks `m_ObserverCounts.Keys` (`:126-133`) — so pushing an unobserved key from C# is a silent no-op, not a throw.

The frontend half is `bindMap(group, name, keyStringifier)` (`source.js:25329-25331`, the class at `:25715-25814`), whose default key stringifier is `stringifySortedIgnoreBindingType` — a `JSON.stringify` over the key's own properties **sorted by name and with `__Type` dropped** (`:25310-25318`). That is why an `Entity` key works as a map key at all. `bindMapPersistent` (exported under that name by the `game-ui/common/data-binding/binding.ts` module map, `source.js:25894-25904`; declared by neither name in the scaffold's `api.d.ts`) is the sibling factory that passes `keepAlive = true` (`:25332-25334`), which is the bool the C# `OnUnsubscribe` reads.

**Settled live.** `engine.on("l10n.indexCounts.updateMapEntry", (k, v) => …)` then `engine.trigger("l10n.indexCounts.subscribeMapEntry", "Loading.HINTMESSAGE")` delivered `("Loading.HINTMESSAGE", 38)` synchronously; `engine.trigger("l10n.indexCounts.unsubscribeMapEntry", "Loading.HINTMESSAGE", false)` released it. The C# writer is `LocalizationBindings.BindIndexCounts` (`src/Game/Game.UI.Localization/LocalizationBindings.cs:47`, `:70-73`).

### The patch protocol, and the one binding kind that uses it

`RawValueBinding` alone exposes a second event, `path + ".patch"`, written as `BeginEvent(m_PatchEventName, 2)` — a path array, then a value (`RawValueBinding.cs:18`, `:35-45`). `RawValueBindingExtensions` gives two helpers for the path argument (`RawValueBindingExtensions.cs:5-23`). `PatchBegin` opens with `Assert.IsTrue(base.attached)`, and the decompiled file carries `#define UNITY_ASSERTIONS` at line 1, so the assertion is live in the shipped assembly (the same signal `environment-and-pollution.md:659` uses).

The frontend applies a patch by cloning down the path and replacing the leaf, then running the ordinary update path; an empty path means "replace the whole value", and a non-container along the way throws `cannot patch object of type <typeof>` (`source.js:25607-25628`).

The game has two users. `ProductionCompanyUISystem.Patch(int index, string fieldName, int value)` patches one field of one array element on a plain `RawValueBinding` with an `[index, fieldName]` path (`src/Game/Game.UI.InGame/ProductionCompanyUISystem.cs:204`, `:320-329`) — the non-widget shape a mod would copy. The other is the widget tree. `WidgetBindings : CompositeBinding, IReader<IWidget>` holds one `RawValueBinding` for the whole children array (`src/Game/Game.UI.Widgets/WidgetBindings.cs:9-29`) and, on each update, either re-sends the array or walks the tree emitting one patch per changed node (`:65-112`). `Widget.PatchWidget` picks the narrowest patch the change flags allow — `"path"`, `"props"` (re-opening a `TypeBegin(widget.propertiesTypeName)` for just the properties), `"children"`, or the whole widget (`src/Game/Game.UI.Widgets/Widget.cs:161-186`), with `WritePatchPath` interleaving `"children"` between indices (`:189-199`, `:201-215`).
The reverse direction is `SettableBindings`, the game's single `RawTriggerBinding`: `<group>.setValue` reads a widget path, resolves it against the live tree through `WidgetBindings`' own `IReader<IWidget>`, and calls `ISettable.SetValue(reader)` — logging `"Widget does not implement ISettable"` or `"Invalid widget path"` and skipping the value otherwise (`src/Game/Game.UI.Widgets/SettableBindings.cs:11-27`).

This is how the options screen and the developer menu are drawn: `OptionsUISystem` registers two widget trees, `new WidgetBindings("options")` for the page and `new WidgetBindings("options", "directoryBrowser")` for the folder picker (`src/Game/Game.UI.Menu/OptionsUISystem.cs:567-569`, `:596-598`), and `DebugUISystem` registers one for `debug` (`src/Game/Game.UI.Debug/DebugUISystem.cs:147-148`). A mod adding a settings page never touches any of it directly — `settings-and-input` owns that route.

### C#-initiated events, and the barrier idiom

`EventBinding` (no payload) calls `view.TriggerEvent(updateEventName)` directly, with no `active` check and a null-conditional on the view (`EventBinding.cs:10-13`). `EventBinding<T>` writes a payload inside `try/finally` so `EndEvent` always runs, and does nothing when inactive (`:25-39`). `RawEventBinding` exposes `EventBegin()`/`EventEnd()` for a caller writing its own payload (`RawEventBinding.cs:10-19`).
The frontend receives all three through `bindEvent(group, name)` (`source.js:25335-25337`, the class at `:25833-25879`), which subscribes on the first listener and unsubscribes on the last.

The game's uses: `l10n.activeDictionaryChanged` (`src/Game/Game.UI.Localization/LocalizationBindings.cs:46`), `app.confirmationDialog` and `app.checkContinueGamePrerequisites` (`src/Game/Game.UI/AppBindings.cs:203`, `:212`), `input.onActionPerformed` / `onActionReleased` / `onActionsRefreshed` (`src/Game/Game.UI/InputActionBindings.cs:554-559`), `chirper.chirpAdded` (`src/Game/Game.UI.InGame/ChirperUISystem.cs:117`), `milestone.xpMessageAdded` (`src/Game/Game.UI.InGame/MilestoneUISystem.cs:289`), `radio.segmentChanged` (`src/Game/Game.UI.InGame/RadioUISystem.cs:87`), `debug.bindingTriggered` (`src/Game/Game.UI.Debug/DebugUISystem.cs:150`), `paradox.onModDetailCompleted` / `onModSubscribeError` (`ParadoxBindings.cs:380`, `:382`).

**`observerCount` is public, and the game uses subscription itself as a signal.** Three bindings exist only to be counted and never carry a payload that matters:

- `time.simulationPausedBarrier`, an `EventBinding<bool>` whose `observerCount > 0` forces `m_SimulationSystem.selectedSpeed = 0f` every frame (`src/Game/Game.UI.InGame/TimeUISystem.cs:93`, the property at `:73`, the effect at `:126-140`). Any frontend component that subscribes pauses the simulation for as long as it holds the subscription.
- `input.cameraBarrier` and `input.toolBarrier`, whose counts feed `m_CameraInputBarrier.blocked` and `m_ToolInputBarrier.blocked` (`src/Game/Game.UI/InputBindings.cs:42-43`, `:87-88`). The frontend side is `bindEvent(…, "cameraBarrier")` / `"toolBarrier"` (`source.js:31141-31142`).

All three are declared `EventBinding<bool>` and none is ever `Trigger`ed; the payload type is vestigial. The sibling `input.toolActionPerformed` on the same class is the control case — same type, and it *is* triggered (`InputBindings.cs:41`, `:94`), so the distinction is in the use rather than in the kind.

The same lever is available on `active`: `ChirperUISystem` skips its whole query when nothing is listening (`ChirperUISystem.cs:130`), and `ProductionCompanyUISystem` gates three separate computations that way (`src/Game/Game.UI.InGame/ProductionCompanyUISystem.cs:223/227/231`).

**The corpus's one worked example is a mod-side subclass that owns a resource.** `HallOfFame` subclasses `ValueBinding<InputActionPhase>` and overrides `OnSubscribe`, `OnUnsubscribe` and `Detach` to enable the underlying `ProxyAction` exactly while the frontend holds a subscription (`HallOfFame/HallOfFame/Utils/InputActionBinding.cs:73-115`), wrapped with the binding-configuration binding in a `CompositeBinding` that names the two children `<name>.binding` and `<name>.phase` (`:22-36`). It is the readable template for "make this cost nothing when no panel is open".

### The community's binding helper, what it is and where it is wrong

**Nine of 22 repositories carry the same copy-pasted trio.** `ExtendedUISystemBase` + `ValueBindingHelper<T>` + `GenericUIWriter<T>` / `GenericUIReader<T>`, in Anarchy, BetterBulldozer, Find It, Node Controller, Platter, Recolor, Realistic Trips (Time2Work), Road Builder and Tree Controller. `Anarchy/Anarchy/Extensions/`, `CS2-Platter/Platter/Extensions/`, `NodeController/NodeController/Extensions/`, `Recolor/Recolor/Extensions/`, `Time2Work/NightShift/Extensions/`, `Tree_Controller/Tree_Controller/Extension/`, `BetterBulldozer/BetterBulldozer/Extensions/` (writer + helper only), `FindIt-CSII/FindIt/Utilities/ExtendedUISystemBase.cs` and `RoadBuilder-CSII/RoadBuilder/Systems/UI/ExtendedUISystemBase.cs` (all three classes in one file). The copyright header on the best-formatted copy names Luca Rager (`CS2-Platter/Platter/Extensions/ExtendedUISystemBase.cs:1-4`); nothing about it is in the game API.

**What it does**, in three parts:

1. `ExtendedUISystemBase : UISystemBase` adds `CreateBinding` / `CreateTrigger` overloads that fill in the mod id as the group and a generic writer/reader, so a call site reads `var mode = CreateBinding("SelectionMode", "SetSelectionMode", SelectionMode.Single);` (`CS2-Platter/Platter/Extensions/ExtendedUISystemBase.cs:11-87`).
2. `ValueBindingHelper<T>` wraps a `ValueBinding<T>` behind a `Value { get => Binding.value; set => Binding.Update(value); }` property, plus `public static implicit operator T` so the helper reads like a plain field (`CS2-Platter/Platter/Extensions/ValueBindingHelper.cs:10-39`).
3. `GenericUIWriter<T> : IWriter<T>` reflects over public properties and fields and emits `TypeBegin(type.FullName)` + one `PropertyName`/value pair each, honouring `IJsonWritable` first and special-casing `int bool uint float double string Enum Entity Color`, arrays and `IEnumerable` (`CS2-Platter/Platter/Extensions/GenericUIWriter.cs:14-133`). `GenericUIReader<T>` is the mirror (`GenericUIReader.cs:15-141`).

**Two variants exist and the difference matters.** Platter and Node Controller prefix every key — `$"BINDING:{key}"` and `$"TRIGGER:{key}"` (`CS2-Platter/Platter/Extensions/ExtendedUISystemBase.cs:13/22`, `NodeController/NodeController/Extensions/ExtendedUISystemBase.cs:21/26`, where it is switchable via `UseKeyPrefixes`). Anarchy, BetterBulldozer, Find It, Recolor, Time2Work, Tree Controller and Road Builder pass the key through unchanged. So `survey-mods-techniques.md:288`'s "with the same key convention `BINDING:{key}` / `TRIGGER:{key}`" is a description of two of the nine copies, not of the trio.

**Verdict on the survey's count.** `survey-mods-techniques.md:288` says "copy-pasted into 7 of 12 mods" and names Anarchy, BetterBulldozer, Tree_Controller, Platter, FindIt, RoadBuilder, TLE. At 22 repositories it is nine, TLE is no longer in the checkout, and Node Controller, Recolor and Time2Work have joined. The survey's characterisation — de-facto standard, not part of the game API — holds.

**Three defects ride in every copy, and an agent writing this itself should not reproduce them:**

- **`GetterValueBinding` is registered with `AddBinding`, not `AddUpdateBinding`.** `CreateBinding<T>(string key, Func<T> getterFunc)` calls `AddBinding(binding)` in Platter, Anarchy, BetterBulldozer, Recolor, Time2Work and Tree Controller (`CS2-Platter/Platter/Extensions/ExtendedUISystemBase.cs:40-46`). Nothing ever calls `Update()`, so the getter runs once, on the first-ever subscribe (`m_ValueDirty` starts true and nothing re-arms it), and every later subscribe replays that cached value. Node Controller is the only copy that fixed it, with an `autoUpdate` parameter selecting `AddUpdateBinding` (`NodeController/NodeController/Extensions/ExtendedUISystemBase.cs:74-89`).
- **The reader's `IJsonReadable` test is inverted.** `if (type.IsAssignableFrom(typeof(IJsonReadable)))` asks whether the interface is assignable to the concrete type, which is false for every real payload; it should be `typeof(IJsonReadable).IsAssignableFrom(type)`. Present identically in all six copies checked (`CS2-Platter/Platter/Extensions/GenericUIReader.cs:22`, `Anarchy/…/GenericUIReader.cs:24`, `NodeController/…/GenericUIReader.cs:44`, `Recolor/…/GenericUIReader.cs:24`, `Tree_Controller/…/GenericUIReader.cs:24`, `Time2Work/…/GenericUIReader.cs:22`). A type's own `Read` is therefore never called and it falls through to reflective field-by-field reading.
- **`List<T>` reading calls `type.GetElementType()`**, which returns `null` for a generic list (it is defined for arrays and pointers only), so the list branch cannot work (`CS2-Platter/Platter/Extensions/GenericUIReader.cs:99-113`, and the same branch in Anarchy, Recolor, Time2Work and Tree Controller at their own offsets). `type.GenericTypeArguments[0]` is what the game's own `ValueReaders.Create` uses for the same job (`ValueReaders.cs:77`).

**How to write it correctly instead**, using only the shipped API: implement `IJsonWritable`/`IJsonReadable` on the payload type and let `ValueWriters.Create<T>()`/`ValueReaders.Create<T>()` resolve it, or call `ValueWriters.Register<T>(writer)` and `ValueReaders.Register<T>(reader)` once during `OnLoad` so no call site ever passes a writer. Both `Register` overloads are public (`ValueWriters.cs:39-47`, `ValueReaders.cs:46-54`); the reflection into `s_Readers` three mods do exists only because they wanted a fallback where `Create` throws, and `Register` supplies it without reflection.

`ExtendedInfoSectionBase : Game.UI.InGame.InfoSectionBase` is the parallel helper for the selected-info panel, present in Anarchy, Node Controller, Platter, Recolor and Time2Work (`Anarchy/Anarchy/Extensions/ExtendedInfoSectionBase.cs:11-88`); `InfoSectionBase` is itself a `UISystemBase` gated to `GameMode.Game` (`src/Game/Game.UI.InGame/InfoSectionBase.cs:26`).

### `ExecuteScript` is not a binding, and why

`UIView` exposes the Cohtml view's `ExecuteScript(string script)`, which is `void` and PInvokes straight to `CSharp_View_ExecuteScript` (`src/cohtml.Net/cohtml.Net/View.cs:320-323`, `cohtmlNativePINVOKE.cs:2898-2899`). It has no return value, no reader, no writer, no registry entry, no path and no observer count: it hands a string of JavaScript to the page and forgets it. Nothing in it can answer, and nothing on the C# side learns whether it parsed.

The game uses it exactly once, to blur the focused element (`src/Cohtml.Runtime/cohtml/CohtmlView.cs:736`). Two corpus mods use it eight times between them to toggle CSS classes by walking the DOM for an `<img>` whose `src` contains a known filename: `BetterBulldozer/BetterBulldozer/Systems/BetterBulldozerUISystem.cs:252-277` and `Tree_Controller/Tree_Controller/Tools/TreeControllerUISystem.cs:462-465/633/1191-1194`. `survey-mods-techniques.md:345-347` already flags it as the pre-`ModRegistrar` anti-pattern, and the mechanism above is why: it is the one C#→UI channel with no contract at either end.

### `IDebugBinding` is the developer menu's inspector, not a registry

`BindingBase` implements `IDebugBinding` (`group`, `name`, `debugType`) alongside `IBinding` (`BindingBase.cs:7`), and `DebugBindingType` is `Unknown, Trigger, Event, Value` (`DebugBindingType.cs:3-9`). `DebugBindingWriter` serialises the three fields under `TypeBegin(typeof(IDebugBinding).FullName)` (`DebugBindingWriter.cs:5-15`).
The only consumers are two bindings in the developer menu: `debug.observedBinding` (a `ValueBinding<IDebugBinding>`) and `debug.bindingTriggered` (an `EventBinding<IDebugBinding>`), both in `DebugUISystem.OnCreate` (`src/Game/Game.UI.Debug/DebugUISystem.cs:149-150`, the `Trigger` helper at `:176-179`).
The live set is enumerable from C#: `IBindingRegistry : IBindingGroup` (`IBindingRegistry.cs:3`), `IBindingGroup` declares `IEnumerable<IBinding> bindings { get; }` (`IBindingGroup.cs:5`), and `CompositeBinding` implements it as the flat list it holds (`CompositeBinding.cs:22`) — so `GameManager.instance.userInterface.bindings.bindings` walks every registered binding, each `ToString()`ing its path (`BindingBase.cs:45-48`), and a nested `CompositeBinding` or `StackBinding` is itself an `IBindingGroup` to recurse into. **From the frontend there is no such walk: the route to "what bindings exist" there is the shipped UI bundle plus the decompile** — which is what `docs/SOURCES.md` entry 9 says.

### Catalog gaps

**Realistic Trips** (`plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md:200`). Add to **Demonstrates**:
> The corpus's only keyed binding: one map binding per statistics group and a second delegating straight to the vanilla prefab-requirements writer, beside hand-written JSON writers for the panels a plain value binding could not shape.
Source lines: `Time2Work/NightShift/Systems/Time2WorkStatisticsUISystem.cs:84` and `:125` (`RawMapBinding<Entity>`, the second calling `m_PrefabUISystem.BindPrefabRequirements`), `:336` (`UpdateAll`), `:99`/`:106`/`:115` and `Systems/SpecialEventsUISystem.cs:114` (`RawValueBinding` with inline writer delegates).

**Info Loom** (`mod-catalog.md:314`). Add to **Demonstrates**:
> Writing a panel's payload by hand through a raw value binding when the shape is a table rather than an object, with the update pushed explicitly from the system instead of polled.
Source lines: `InfoLoom/InfoLoom/Systems/InfoviewUISystems/ILEducationInfoviewUISystem.cs:39-41` and `:130-132`, `Systems/SankeyUISystems/BudgetUISankeySystem.cs:28/42`, `Systems/SankeyUISystems/WorkforcePipelineSankeySystem.cs:60/315`, `Systems/UI/InfoLoomUISystem.cs:284`, `Systems/IndustrialSystems/StorageCompanies/Systems/StoragePropertyCompanies.cs:44`.

**Hall of Fame** (`mod-catalog.md:371`). Its entry already names the frontend-owned input action; add beside it:
> Forcing a push for a value the binding layer would deduplicate, with an equality comparer that answers false to everything — the supported hook for a payload whose identity changes on every edit.
Source lines: `HallOfFame/HallOfFame/Utils/AlwaysFalseEqualityComparer.cs:5-9`, used at `Utils/InputActionBinding.cs:57`; the second copy at `Systems/CommonUISystem.cs:256-259`.

**Platter** (`mod-catalog.md:131`). Add to **Demonstrates**:
> The corpus's best-formatted copy of the community binding helper — a `UISystemBase` subclass with typed create-binding overloads over a reflection-driven writer and reader — which is the shape nine repositories share and the place to read it, defects included.
Source lines: `CS2-Platter/Platter/Extensions/ExtendedUISystemBase.cs:11-87`, `ValueBindingHelper.cs:10-39`, `GenericUIWriter.cs:14-133`, `GenericUIReader.cs:15-141`.

### Source-list gaps

**Entry 6 (the official UI mod scaffold) understates what `bindings.d.ts` is authoritative for.** `docs/SOURCES.md:72` describes it as "`cs2/bindings` (every binding group's payload types, and by far the largest)". That is true and incomplete: the file also carries **186 fully-qualified `__Type` wire tags** as enum values and `const` declarations, which makes it the only written-down record of the strings a `TypeBegin` must emit for the game's own components to render a payload. **Amended in place on 2026-08-23** under that file's own rule for a pass that finds an entry's scope narrower than the truth; the parenthetical now names the wire tags and their count.

**Entry 9 (the running game — the UI) gives the subscribe route for value bindings only, and a reader following it on a map binding will conclude the binding does not exist.** `docs/SOURCES.md:109` says `engine.on("<group>.<name>.update", cb)` followed by `engine.trigger("<group>.<name>.subscribe")`. A map binding answers a different verb set entirely: `engine.on("<group>.<name>.updateMapEntry", (key, value) => …)` then `engine.trigger("<group>.<name>.subscribeMapEntry", key)`, released with `engine.trigger("<group>.<name>.unsubscribeMapEntry", key, false)` (`src/Colossal.UI.Binding/Colossal.UI.Binding/MapBindingBase.cs:36`, `:49-50`; verified live on `l10n.indexCounts`). Beside it: the plain `.subscribe` verb still answers on a map binding rather than erroring — it returns the currently-observed `[key, value]` pairs from the debug binding the map registers under the same path (`MapBindingBase.cs:40`, `:166-177`), so a probe run before anything is subscribed gets an empty array and reads it as absence. **Amended in place on 2026-08-23**, both sentences, under that file's own rule for a pass that finds an entry wrong.

Nothing else in `docs/SOURCES.md` needed correcting for this topic: entries 1, 3, 10 and 11 were used exactly as described, and the wiki's bot challenge did not fire.

## Bridge

**Sibling techniques in the UI skill.**

- **`frontend-and-injection`** owns the other end of every wire here. Three seams are this topic's to hand over: the binding module itself is registered as `game-ui/common/data-binding/binding.ts` (`DecompiledCitiesSkylines2/src-ui/source.js:25882`), which is where `bindValue`, `bindLocalValue`, `bindMap`, `bindMapPersistent`, `bindEvent`, `bindTrigger`, `bindTriggerWithArgs`, `trigger` and `call` live; the `cs2/api` public export list is fifteen names and no more — `bindEvent bindLocalValue bindMap bindTrigger bindTriggerWithArgs bindValue call trigger useMapValue useMapValueOnChange useMapValues useReducedValue useValue useValueOnChange useValueRef` (`source.js:12396-12412`, confirmed live off `window["cs2/api"]`), so `bindMapPersistent` is registry-only; and the typed renderer that turns a `__Type` into a React component is `game-ui/common/typed-renderer/typed-renderer.tsx` (`:49824`), whose failure mode is the `Unknown element type` box (`:49792-49796`). The bundle is that reference's whole subject and this file only reads the far end of the wire.
- **`ui-build-and-devloop`** owns the UI project that consumes the scaffold's `bindings.d.ts`, and the build-time C#→TS type generation whose output keeps a group's binding names, enums and payload shapes in step with the C#; the manifest `id` it teaches as the natural binding group is this topic's group convention stated from the build side.

**The `cs2-mod-project` skill.**

- **`cs2-mod-project`** owns the csproj half of the one build fact here — the UI topic's boundary starts at the webpack build: `Colossal.UI.Binding.dll` is an explicit `<Reference Include>` in the mod's own csproj, resolved off `$(ManagedPath)` (`Anarchy/Anarchy/Anarchy.csproj:78-79`), because the toolchain's `Mod.props` under `%CSII_TOOLPATH%` declares exactly one reference, for mscorlib. The official C# template already emits that reference when generated with its settings option — the `<!--#if (IncludeSetting) -->` block at `content/ModTemplate.csproj:27-42` in `ColossalOrder.ModTemplate.1.0.0.nupkg` — so "add it by hand" holds only for a project generated without it.

**Trunk techniques.**

- **`localization`** is the worked example of a polymorphic payload and was verified against, not re-derived. Its `## Bridge` (`localization.md:658-659`) states the four `TypeBegin` names — `Game.UI.Localization.LocalizedString` and its three siblings (`src/Game/Game.UI.Localization/LocalizedString.cs:76-86`, `LocalizedNumber.cs:29`, `LocalizedFraction.cs:29`, `LocalizedBounds.cs:29`) — and the frontend enum and dispatch that consume them (`source.js:29431-29436`, `:29634-29647`), and the exact-equality `switch` that rejects any other string (`:29634-29647`). The `l10n` group's five members are that file's (`localization.md:332`, `LocalizationBindings.cs:41-51`) and are this topic's cheapest live probe.
- **`settings-and-input`** owns the widget tree this topic's patch protocol serialises. A `ModSetting` page reaches the frontend as `Game.UI.Widgets.*` payloads through `OptionsUISystem`'s `WidgetBindings` (`src/Game/Game.UI.Menu/OptionsUISystem.cs:567-569`, `:596-598`), and the reverse channel is the `options.setValue` raw trigger (`src/Game/Game.UI.Widgets/SettableBindings.cs:11-27`). It also owns `ProxyBinding`, the `IJsonWritable` struct behind the corpus's one always-false-comparer use (`src/Game/Game.Input/ProxyBinding.cs:12`).
- **`units-and-formatting`** owns what a number *means* once it crosses. `Game.UI.Unit` is written as a plain string field beside the value on `LocalizedNumber<T>` (`LocalizedNumber.cs:29-37`), and `options.unitSettings` is a `GetterValueBinding<UnitSettings>` carrying three ints (`OptionsUISystem.cs:589`, the struct at `:393-411`) — confirmed live as `{"__Type":"Game.UI.Menu.OptionsUISystem+UnitSettings","timeFormat":0,"temperatureUnit":0,"unitSystem":0}`.
- **`mod-lifecycle-and-ordering`** owns when a UI system may register: `userInterface` is built by `InitializeUI` immediately after `CreateWorld()` and before `CreateSystems()` (`GameManager.cs:591`, `:593`, `:615`, `:1762`), well before mods load, so `AddBinding` from `OnLoad` is always safe. It also owns the `UIUpdate` phase census and already records that a `GetUpdateInterval` override there is dead (`mod-lifecycle-and-ordering.md:272`, `:314`) — the mechanism is `UIUpdateSystem` calling the interval-less `Update(phase)` overload (`UIUpdateSystem.cs:19`, `UpdateSystem.cs:166`).
- **`performance-and-memory`** owns the cost side: a `GetterValueBinding` runs its getter on every frame it is observed (`GetterValueBinding.cs:47-66`), a `RawValueBinding` on an update pump re-serialises its whole payload every frame with no comparison (`RawValueBinding.cs:47-56`), and the engine's own consecutive-push warning is dead code (`GetterValueBinding.cs:20/22/68-72`). The game's own answer is `UIUpdateState` at 256 simulation frames (`src/Game/Game.UI/UIUpdateState.cs:16-43`, six users) and gating on `active`/`observerCount` (`ChirperUISystem.cs:130`, `ProductionCompanyUISystem.cs:223/227/231`).
- **`custom-tools`** is where most mod bindings actually land: a tool's options row is a `UISystemBase` at `UIUpdate` publishing the tool's mode and reading the player's choice back through triggers, and `ToolbarUISystem` is gated to `GameMode.Game` (`src/Game/Game.UI.InGame/ToolbarUISystem.cs:110`).
- **`debug-menu`** owns `DebugUISystem`, which is both the largest single `WidgetBindings` consumer and the only consumer of `IDebugBinding` (`src/Game/Game.UI.Debug/DebugUISystem.cs:142-158`).
- **`diagnostics`** owns where the failures go. Every binding error in this layer is written to the `UI` logger, obtained once as a static on `BindingBase` (`BindingBase.cs:26-29`): `"Error in value binding '<path>'"`, `"Error in trigger binding callback '<path>' '<group>' '<name>'"`, `"Error in call binding callback '<path>'"`, `"Error while attaching binding {0}"`. A throw out of a UI system's `OnUpdate` is a different line — `"System update error during {0}->{1}:"` at Critical from `UpdateSystem` (`UpdateSystem.cs:195`).
- **`ecs-in-this-game`** owns `Entity`, which is the single most common payload across the boundary and pre-registered: `{index, version}` under `Unity.Entities.Entity` (`UnityWriters.cs:17-25`, `UnityReaders.cs:19-27`), stringified for map keys by dropping `__Type` and sorting the rest (`source.js:25310-25318`).

**Mechanics topics this technique exercises.** Every mechanics area with a panel is reached through here, and four are the load-bearing ones:

- **`citizens-and-households`** — the selected-info panel and the life-path panel are the binding layer at its widest, with `SelectedInfoUISystem` gated to `GameMode.Game` and throttled by `UIUpdateState` at 256 frames (`src/Game/Game.UI.InGame/SelectedInfoUISystem.cs:107`, `:181`), and `Game.UI.InGame.HouseholdSidebarSection+HouseholdSidebarItem` among the `__Type` tags a section emits.
- **`city-services-and-coverage`** — `ServiceBudgetUISystem` is the most complete single example in the game of the four kinds together: a `RawValueBinding` for the service list, a `RawMapBinding<Entity>` for per-service details, and three triggers including one with a hand-written `IReader<PlayerResource>` (`src/Game/Game.UI.InGame/ServiceBudgetUISystem.cs:111-115`, `:51-57`, `:118`, `:160`).
- **`city-state-and-progression`** — `milestone.*` and `devTree.*` carry twelve of the game's short-form `__Type` tags (`milestone.Asset`, `milestone.Feature`, `milestone.Milestone`, `milestone.MilestoneDetails`, `milestone.Policy`, `milestone.Service`, `milestone.UnlockDetails`, `milestone.XPMessage`, `devTree.Node`, `devTree.NodeDetails`, `devTree.Service`, `devTree.ServiceDetails`), and `milestone.xpMessageAdded` is a `RawEventBinding` gated on `active` (`src/Game/Game.UI.InGame/MilestoneUISystem.cs:289`, `:361`).
- **`simulation-time-and-units`** — `TimeUISystem` is this file's smallest complete worked example, and its `time.simulationPausedBarrier` is the barrier idiom (`src/Game/Game.UI.InGame/TimeUISystem.cs:19-51`, `:84-95`, `:126-140`). The live payload above states `ticksPerDay = 262144` and `daysPerYear = 12` as the frontend receives them.

**Soft pointer, not a bridge slug.** The sibling `coherent-gameface` plugin (`plugins/coherent-gameface/`) drives the far end of this wire from an agent session; every `Settled live` claim above came from an ordinary `game_eval` of `engine.on`/`engine.trigger`/`engine.call` through it, and reading a mod's own binding back is the cheapest way to tell "the C# never registered" from "the React never subscribed".

## Dead ends

- **`IBindingContext` and `BindingContext` do not exist.** Grepped the whole decompile for both names and for `class BindingContext`: zero hits. The registration context is `IBindingRegistry` (`IBindingRegistry.cs:3-8`), reached as `GameManager.instance.userInterface.bindings` (`UserInterface.cs:43`), and `IBindingGroup` is its read-only half (`IBindingGroup.cs:5-8`).
- **`MapBinding` does not exist as a type.** The two concrete map bindings are `RawMapBinding<K>` and `GetterMapBinding<K,V>`, both over the abstract `MapBindingBase<K>`. `MapBinding<K,V>` is a *frontend* interface name (`bindings.d.ts:9-13`, `api.d.ts:9-13`), which is where the expectation of a C# type by that name comes from.
- **`JsonWriter.FalseEqualityComparer<T>` does not exist**, and neither does any `FalseEqualityComparer` anywhere in the decompile. Recorded as a verdict above; left here because the survey names it as a game API and the next reader will grep for it.
- **The engine shim's local-handler shortcut cannot swallow a trigger in the attached game — checked live and closed.** `engine.trigger` is `this._trigger.apply(...) || this.TriggerEvent.apply(...)` (`source.js:153-157`), and `_trigger` returns `true` whenever `engine.events[name]` holds a handler (`:54-65`), which would mean a JS `engine.on("<group>.<name>", …)` on a trigger binding's own path silently prevents the native hop to C#. It does not happen: when the view is attached, `engine.on` is replaced by a version that calls the native `AddOrRemoveOnHandler` and never populates `events` (`:141-149`). Verified live — `Object.keys(engine.events).length` is `0` in the running game, and registering a handler on `time.timeSettings.subscribe` then triggering it fired both the local handler and the C# push.
- **No corpus mod registers a writer or reader with the supported API.** `ValueWriters.Register` and `ValueReaders.Register` return zero occurrences across all 22 repositories. Recorded as a finding above rather than only here, because the absence is the teaching.
- **`CallBinding` has no corpus example at 22 repositories**, so there is no mod-written model of the request/response route. The game's eleven registrations are the only worked examples, and the corpus's one request/response channel (`CS2-WriteEverywhere`) routes through a framework whose implementation is in an empty submodule.
- **The `debug` binding group cannot be read as a list of live bindings.** `IDebugBinding` looked like a registry hook and is not: its two consumers publish one binding at a time, chosen by the developer menu (`src/Game/Game.UI.Debug/DebugUISystem.cs:149-150`). The registry itself enumerates through `IBindingGroup.bindings` (recorded under `IDebugBinding` above); the `debug` group is not the way in.
- **`Colossal.UI.Binding`'s assembly carries no version of its own.** `src/Colossal.UI.Binding/Properties/AssemblyInfo.cs` is five lines and holds only `AssemblyVersion("0.0.0.0")` — no `VersionInternal`, unlike `Game` (`docs/SOURCES.md:29` records the general case). Dating the binding layer independently of the game build is not possible from the decompile.
- **`Game.UI.Common` was searched for and does not exist.** Three `TypeBegin` literals name it (`IntProperty.cs:21`, `Int2Property.cs:22`, `StringProperty.cs:17`) and the scaffold declares all three; the C# types live in `Game.UI.InGame`. It is a wire namespace with no code behind it, which is the sharpest demonstration that `__Type` is a contract string.
- **The wiki says nothing about the C# half.** `UI Modding` (https://cs2.paradoxwikis.com/UI_Modding, fetched live 2026-08-23 through `index.php?action=raw`; stamped `{{ParadoxVerifiedAmbox|version=1.5.7 f1}}`, one version behind the install) has eighteen sections, none about C#. Its `cs2/api` section lists four of the fifteen exported names — "`bindValue` - creates a subscribe listener for a given value binding on the C# side", "`call` - calls a call binding on the C# side and returns a promise", "`trigger` - calls a trigger binding on the C# side. Does not return a value.", "`useValue` - a hook that provides current binding's value." — all four correct against the shipped bundle and against the live engine. Its `cs2/bindings` section says only "All existing bindings, triggers and calls that are used by the game UI itself" and "Bound values follow a pattern of having a name with a `$` suffix", which the scaffold's 383 `$`-suffixed `const` declarations confirm (beside 100 unsuffixed ones, which are the triggers and calls). Nothing on this page needed a verdict and nothing on it reaches the C# side.
- **No `Register` call was found for the `long`/`ulong` writer gap.** Searched `src/` for a `ValueWriters.Register<long>` or `<ulong>` anywhere outside the static constructor: none. The gap is real rather than filled elsewhere at runtime.
