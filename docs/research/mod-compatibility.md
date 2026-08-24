# Mod compatibility

**Baseline.** Decompiled game 1.6.0f1, read under `C:\Users\Morgan\Documents\Projets\DecompiledCitiesSkylines2\src\`.
Installed game read at the same version: the shipped locale package `Cities2_Data/Content/Game/Locale.cok`, and the UI bundle `Cities2_Data/Content/Game/UI/index.js` cited through a copy reformatted with prettier at its defaults at `DecompiledCitiesSkylines2/src-ui/source.js`, **135,021 lines** — check your copy's count before trusting a line number from it.
Mod corpus read 2026-08-06, 22 repositories under `C:\Users\Morgan\Documents\Projets\cs2-third-party-mods\`.
The official UI mod scaffold read through the npm global root and cited as `create-csii-ui-mod/<path>`; that root is a junction to `<install>/Cities2_Data/Content/Game/.ModdingToolchain/npx-create-csii-ui-mod/`, so the files are the game's own, versioned by the install and not by npm (conflicts.md's ruled scaffold-citation entry).
Prevalence figures come from the user's Paradox mods cache at `%CSII_USERDATAPATH%/.cache/Mods/pdx_mods/`, read 2026-08-06: **432 directories, 391 distinct mod ids, 147 of them shipping a managed assembly**.
The wiki was not fetched: it has no page on this subject (see `## Dead ends`).
The game was not running for this pass; every live question below records its experiment instead of its result.

---

## Findings

### One assembly name, one loaded copy, and the loser is a mod that never runs

The whole of this topic's hardest failure mode is one method. **The loader deduplicates every executable asset by its simple assembly name across the entire install**, mods and the libraries they ship alike.

`ExecutableAsset.ResolveModAssets` groups all assets by `a.definition.Name.Name` and makes every member of a group a duplicate of the others (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ExecutableAsset.cs:341-360`).
`isUnique` is then `GetUniqueVersionAsset() == this` (`:177`), and the winner is picked by

```
from d in m_Duplicates.Append(this)
where (d.isLoaded || d.canBeLoaded) && (isMod || isReference)
orderby d.isLoaded descending, d.isLocal descending, d.version descending, d.id
select d
```

(`:181-191`).
`LoadAssembly` does not load the asset it was called on — it calls `GetUniqueVersionAsset()` and loads that (`:213-217`), and `LoadAssemblyImpl` loads every reference the same way before itself (`:228-231`).
So a mod that ships library `L` beside its own DLL still runs against whichever copy of `L` won globally, even though reference resolution first looked for a sibling in the mod's own folder (`:366-368`).

**The loser, when it is a mod rather than a library, does not load at all.**
`ModInfo.Load` returns early with `state = State.IsNotUniqueWarning` on `asset.isMod && !asset.isUnique` (`src/Game/Game.Modding/ModManager.cs:104-108`), before `LoadAssembly`, before `IMod` instantiation, before `OnLoad`.
That state is `>= IsNotModWarning`, so it reaches the player as a notification and a dialog (see the surfacing finding below).
A duplicated _library_ produces no state and no warning: nothing is silent about a duplicated mod and everything is silent about a duplicated dependency.

**The order is more deterministic at boot than the four clauses suggest, and this refines `patching.md:85`.**
That file reads the ordering as "first by which copy loaded first, then by locality, and only then by version — so it is neither 'highest version wins' nor deterministic".
The first clause cannot fire on a cold start: `isLoaded` is `assembly != null` (`ExecutableAsset.cs:151`), and the only path that sets `assembly` before `LoadAssemblyImpl` is `GetModAssets`, which matches an already-loaded `AppDomain` assembly **by file location** (`:314-334`, the match at `:321`).
Mod assemblies are loaded from a byte array (`:236`/`:241`), and a byte-array-loaded assembly has an empty `Location`, so no mod or mod-shipped library is ever `isLoaded` when `RegisterMods` runs.
At first initialization the order therefore reduces to **local beats non-local, then highest version wins, then asset id**; `isLoaded` only bites on a mid-session re-initialization (`ModManager.cs:244`, `GameManager.cs:1628`), where a copy already in the process wins over a higher-versioned one.
Both statements are true of the same code and a reader needs the second, because it is what makes "a locally built copy shadows the published one" predictable rather than a coin toss.

**One quirk of that filter is worth recording exactly as written.** The `where` clause reads `(d.isLoaded || d.canBeLoaded) && (isMod || isReference)` — the second half has no `d.` and so tests the _calling_ asset rather than each candidate (`:188`).
The IL distinguishes `ldarg.0` from the lambda parameter unambiguously, so this is the shipped code and not a decompiler artifact.
Practically it means the eligibility half of the filter is a guard on the caller: an asset that is neither a mod nor anyone's reference resolves to itself through the `?? this` fallback.

**Two live cases exist in the census sample.** Of the 391 distinct mod ids in the Paradox cache, exactly four assembly file names are shipped by more than one id: `0Harmony.dll` (33 ids), `Newtonsoft.Json.dll` (2), and `Platter.dll` with its Burst sidecar `Platter_win_x86_64.dll` (2).
The last is the mod case rather than the library case: mod id `125278` ships `Platter, Version=1.6.3.0` and mod id `130324` ships `Platter, Version=1.5.0.0`, both `PublicKeyToken=null`, both non-local.
A player enabling both gets one loaded mod and one `IsNotUniqueWarning` dialog, and by the ordering above the 1.6.3.0 copy is the one that loads.

**Two more loader rules ride with this, and both are about what a mod ships.**
An assembly a mod ships that resolves to a file under `Cities2_Data/Managed/` is dropped from the mod asset list entirely, with `Assembly "{0}" is in-game assembly and it should NOT be shipped with mod "{1}"` in the `Modding` logger (`ExecutableAsset.cs:325-329`).
And a mod whose references cannot all be resolved never loads: `canBeLoaded` is `m_References.Values.All(r => r != null)` (`:175`), and a false there is `State.MissedDependenciesError` with the unresolved reference names joined into `loadError` (`ModManager.cs:109-116`).

Rots: the four duplicated file names and the two `Platter` versions — re-derive by walking `%CSII_USERDATAPATH%/.cache/Mods/pdx_mods/` and grouping `*.dll` by file name across distinct mod ids, and by reading each candidate's `[Reflection.AssemblyName]::GetAssemblyName(...)`.

**Ruled (2026-08-06, the mod-compatibility pass).** What happens after the loader picks a winner is standard .NET rather than a question about this game: nothing here is strong-named, so the version is no part of binding identity and the simple name match is the whole of it.

**Verdict: the runtime is method-granular, and it is silent on three constructs.** Established by direct experiment under Unity's own Mono 6.13 (`Editor/Data/MonoBleedingEdge/bin/mono.exe` of the installed 2022.3.71f1, the runtime family the game ships) during the mod-compatibility pass's review: a library compiled with a member, an app compiled against it, then the library recompiled without that member and swapped in.
A memberref is resolved while the **containing method** is JIT-compiled, not when control reaches the call. A method whose first statement was a write and whose second was `try { Shared.NewApi(); } catch (MissingMethodException) {…}` printed nothing and let the exception escape to its caller. The same held for a removed field, a removed interface method and a removed base virtual: it is the memberref token that fails to resolve, not the vtable slot. So a `try`/`catch` around the call cannot fire; isolating the call in its own `[MethodImpl(MethodImplOptions.NoInlining)]` method made the guard catch, and reflection returned null instead of throwing. Without `NoInlining` the guard also caught in that run because Mono's inliner declined an unresolvable callee — discretion, not a guarantee, which is why the attribute is what to prescribe.
Silent cases: against the swapped library the app printed `const Answer = 41` where the loaded copy declared 4242, an enum member as 2 where the loaded copy declared 99, and an optional parameter's default as 1 where the loaded copy's was 555 — no exception and no log line. A `static readonly` on the same type correctly read the new value, which proves the mechanism is compile-time baking into the calling assembly rather than staleness.
So "a legible fault, never silent misbehaviour" was wrong in both halves, and the shipped reference now states the method granularity, the guard shape and the three baked constructs. The same correction applies to `patching.md`.

**Ruled (2026-08-06, the mod-compatibility pass): the two-mod case is not a scenario to teach from.** A player enabling two mod ids that ship the same mod assembly does not happen in practice, so the `Platter` pair stays here as the census evidence that the grouping is by simple name across the whole install, and the shipped reference states the rule and its library consequence rather than that collision.

### Six ways the corpus detects another mod, and the one that works from `OnLoad`

No mod in the corpus takes a compile-time reference on another mod. Eleven of the twenty-two repositories detect at least one foreign mod at runtime — `Traffic`, `FindIt-CSII`, `Anarchy`, `CS2-MoveIt`, `Time2Work`, `InfoLoom`, `ExtraDetailingTools`, `LineTool-CS2`, `Tree_Controller`, `Water_Features` and `BetterBulldozer` — and they use six distinct routes to detect the mod, plus a seventh that detects its data instead.

**One — the enabled-mod name list.** `ModManager.ListModsEnabled()` returns the `asset.fullName` of every **loaded** mod concatenated with the `name` of every `UIModuleAsset` in the global database (`ModManager.cs:221-227`).
**Verdict (the mod-compatibility pass's review): "loaded mod" is loaded _assembly_, libraries included.** `m_ModsInfos` holds a `ModInfo` for every IL assembly asset, and a library a mod ships is registered, marked required through `isReference`, and loaded by `ModInfo.Load` exactly like a mod — it just finds no `IMod` type and lands at `State.Loaded`. `isLoaded` is `assembly != null`, so that library's own full name is in the array beside the code mods, and a presence test written as "is this name in the list" can match a library rather than the mod that ships it.
`fullName` is `definition.FullName` (`ExecutableAsset.cs:159`), so a code mod appears as `Name, Version=a.b.c.d, Culture=neutral, PublicKeyToken=null` and a UI module appears as its `mod.json` id (`UIModuleAsset.cs:32`).
The corpus matches on a prefix and caches the answer in a `static bool?`: `Traffic/Code/Mod.cs:42-46` (`"C2VM.CommonLibraries.LaneSystem"` and `"RoadBuilder, Version"`), `FindIt-CSII/FindIt/Mod.cs:36-38` (`"ExtraDetailingTools, "`, `"AssetIconLibrary, "`, `"RoadBuilder, "`), `FindIt-CSII/FindIt/Mod.cs:119` (`"Gooee,"`), `ExtraDetailingTools/MOD/EditEntities.cs:18` (`"AssetIconLibrary,"`).
The comma-or-`", Version"` suffix is the discipline that stops `RoadBuilder` matching a hypothetical `RoadBuilderExtra`; Traffic's TLE check omits it and its Find It sibling does not, so the practice is not uniform even inside one file.

**Two — enumerating the mod manager.** `ModManager` is `IEnumerable<ModManager.ModInfo>` over `m_ModsInfos.Values` (`:28`, `:192-200`), and `ModInfo` exposes `asset`, `isLoaded`, `isBursted`, `name`, `assemblyFullName`, `state` and `instances` (`:47-73`).
`Time2Work/NightShift/Mod.cs:67-84` walks it inside `OnLoad` matching `modInfo.asset.name` — the **simple** assembly name — against `"RealPop"`, `"RealisticPathFinding"` and `"RealLife"`; `InfoLoom/InfoLoom/Setting.cs:347-367` does the same with `Contains("CustomChirps")`.

**This is the route that works from `OnLoad`, and route one is not.**
`ModManager.Initialize` calls `RegisterMods()` and then `InitializeMods()` (`:263-264`), and `RegisterMods` populates `m_ModsInfos` for every executable asset before any `IMod.OnLoad` runs (`:397-429`, the loop at `:406`).
So enumerating the manager from `OnLoad` sees every mod that will load, in any order.
`ListModsEnabled()` filters on `x.isLoaded` (`:223-224`), which is only true for mods already initialized — and initialization order is `Dictionary<Identifier, ModInfo>` iteration order (`:180`, iterated at `:438`).
Called from `OnLoad`, it therefore returns a _prefix_ of the mod set determined by hash order, and a mod later in that order is invisible.
This is why `Traffic` defers every one of its detection routines to `MainThreadDispatcher.RegisterUpdater` (`Traffic/Code/Mod.cs:112-114`) while `Time2Work` does its detection inline: the deferral is not politeness, it is what makes route one correct.
`MainThreadDispatcher.RegisterUpdater(Action)` wraps the action in a `Func<bool>` returning true, so it runs exactly once (`src/Colossal.Core/Colossal.Core/MainThreadDispatcher.cs:97-104`), on the next `GameManager.Update()` (`:85-95`, called at `src/Game/Game.SceneFlow/GameManager.cs:713`) — which is after `InitializeModManager` has returned (`:664-670`) and therefore after every mod's `OnLoad`.

**Three — the asset database, by exact name.** `AssetDatabase.global.GetAsset<ExecutableAsset>(SearchFilter<ExecutableAsset>.ByCondition(a => a.isLoaded && a.name.Equals("...")))`, then `asset.assembly.GetType("Full.Type.Name", false)`.
`Traffic` uses it twice: `Traffic/Code/Systems/ModCompatibility/TLEDataMigrationSystem.cs:34-35` for `C2VM.CommonLibraries.LaneSystem.CustomLaneDirection`, and `Traffic/Code/Systems/ModCompatibility/RoadBuilderCompatibilitySystem.cs:28-29` for `RoadBuilder.Domain.Components.RoadBuilderUpdateFlagComponent`; `Traffic/Code/Mod.cs:145-151` uses the `TryGetAsset` form to reach `Game.Net.C2VMPatchedLaneSystem`.
**`ByCondition` with an exact `Equals` is the safe form and the implicit string filter is the trap.** `SearchFilter<T>`'s implicit conversion from `string` sets `str` with `caseInsensitive = true`, and `MatchSkipType` accepts any asset whose `name.IndexOf(str, OrdinalIgnoreCase) != -1` — a case-insensitive **substring** match (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/SearchFilter.cs:45-52`, `:100-103`).
So `GetAsset<ExecutableAsset>("RoadBuilder")` would also match `RoadBuilderExtras`, and every corpus site uses `ByCondition` instead.
`SearchFilter.Match` also requires an exact type unless the filter was built with `inherited: true`, since `notInherited = !inherited` (`:19-35`, `:79-88`).

**Four — scanning `AppDomain.CurrentDomain.GetAssemblies()`.** The most common route: seven repositories use it directly, and two more as the fallback inside route five below. It takes two shapes.
By assembly name: `LineTool-CS2/Code/Systems/LineToolSystem.cs:657-668` (`FullName.Contains("TopoToggle,")`), `CS2-MoveIt/Code/MoveIt/Tool/MoveItToolSystem.Lifecycle.cs:116-121` (`FullName.Contains("Anarchy,")`), `LineTool-CS2/Code/CompatibilityHoverColors.cs:34-54` (three alternative names for one mod, then three alternative type names as a fallback, every iteration wrapped in a bare `catch`).
By probing for a type directly: `Anarchy/Anarchy/Systems/MoveItIntegration/CopyAnarchyComponentsSystem.cs:63-72` (`MoveIt.Components.MIT_Original`), `Anarchy/Anarchy/Systems/Common/AnarchyUISystem.cs:332-342` (`Platter.Components.ParcelPlaceholderData`), `BetterBulldozer/BetterBulldozer/Tools/SubElementBulldozerTool.cs:259-270` and `BetterBulldozer/BetterBulldozer/Systems/RemoveRegeneratedSubelementPrefabsSystem.cs:325` (`PlopTheGrowables.LevelLocked`), `Tree_Controller/Tree_Controller/Tools/TreeControllerTool.cs:374-381`, `Water_Features/Water_Features/Tools/CustomWaterToolSystem.cs:406-413`.
The three-name fallback in `CompatibilityHoverColors` is the honest record of what this route costs: a mod's assembly name is whatever its author last called the project, and a republished mod can change it.

**Five — `Type.GetType("Ns.Type, Assembly")`, with an assembly scan as fallback.** `InfoLoom/InfoLoom/Bridge/CustomChirpsBridge.cs:96-116` and its near-copy in a second, differently authored mod, `Time2Work/NightShift/Bridge/CustomChirpsBridge.cs:111-131` (the two files differ — 119 lines against 135 — but this passage is the same in both, which is the third mod's integration recipe travelling by copy-paste), each write `Type.GetType("CustomChirps.Systems.CustomChirpApiSystem, CustomChirps") ?? FindType("CustomChirps.Systems.CustomChirpApiSystem")`, where `FindType` is the route-four scan.
`Traffic/Code/Mod.cs:189-190` uses the bare form to probe for a BepInEx-loaded build.

**Six — a game registry keyed by a string the other mod chose.** `Anarchy/Anarchy/Systems/MoveItIntegration/CopyAnarchyComponentsSystem.cs:119` finds Move It through `ToolSystem.tools.Find(x => x.toolID.Equals("MoveItTool"))` and then reflects the `Copying` property and the static `GetOriginalEntity` method off the instance it found (`:123-124`).
The same shape reaches a foreign _system_: `Traffic/Code/Mod.cs:158` resolves the type by route three and then `World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged(type)`.

**And there is a seventh route that detects the mod's data rather than the mod.** A component type resolved by name becomes an `EntityQuery`, and the query's own emptiness is the detection: `Traffic`'s `RoadBuilderCompatibilitySystem` builds two `EntityQueryDesc`s over the foreign tag plus its own components and calls `RequireForUpdate` (`RoadBuilderCompatibilitySystem.cs:37-49`), so the system idles until the other mod actually touches something.
`Anarchy`'s Move It integration does the same with `MIT_Original` (`CopyAnarchyComponentsSystem.cs:75-113`).
This is the only route that answers "is the other mod doing something right now" rather than "is it installed".

Rots: every foreign assembly name and type name quoted above — they belong to mods this project does not track, and each is a string literal in the citing file.

### The corpus's one failed detection, and what makes a detection a decision

`Traffic/Code/Mod.cs:179-217` is the only detector that reports rather than adapts, and it is worth reading whole.
It reflects the private static `GameManager.s_ModdingRuntime` (`BindingFlags.Static | BindingFlags.NonPublic`), returns immediately unless the value is `"BepInEx"`, and only then probes for two TLE plugin types by assembly-qualified name.
Finding one, it derives the offending directory from `Assembly.Location` and pushes a player-facing report.
The two-stage gate is the technique: the expensive and fragile probe never runs on a normal install, so the detector costs nothing to the 99% of players it does not concern.

Everywhere else in the corpus a detection is followed by an adaptation, and there are four kinds:

- **Yield a shared UI affordance.** Three tools add `Snap.ContourLines` to both snap masks only when a named competitor is absent — `LineTool-CS2/Code/Systems/LineToolSystem.cs:499-510`, `Tree_Controller/Tree_Controller/Tools/TreeControllerTool.cs:121-131`, `Water_Features/Water_Features/Tools/CustomWaterToolSystem.cs:343-351`. `LineTool` does the same for a guideline transparency setting, handing the value to the other mod by setting its own to zero (`LineToolSystem.cs:649-651`).
- **Disable the foreign system.** `Traffic/Code/Mod.cs:166-168` sets the resolved TLE `LaneSystem` fork's `Enabled = false` and disables the vanilla `LaneSystem` too, because it is taking over the slot.
- **Register an extra system.** `Traffic/Code/Mod.cs:167` and `:232` both issue an `UpdateSystem.UpdateAfter`/`UpdateBefore` from inside a deferred main-thread callback, which is the only way to make a system's registration conditional on another mod being present.
- **Honour the foreign component.** `BetterBulldozer` reads `PlopTheGrowables.LevelLocked` and excludes those entities; `Anarchy` reads `Platter.Components.ParcelPlaceholderData` the same way.

### Exposing an API without becoming a dependency: a static bridge whose signatures name no mod type

The provider half is a `public static class` in a namespace the consumer can hard-code, whose every method takes and returns **only types both sides already reference** — engine types, game types, primitives.

`Anarchy/Anarchy/Bridge/AnarchyBridge.cs:17-277` is the fullest example: sixteen public statics over `ToolBaseSystem`, `Entity`, `NativeArray<Entity>`, `EntityQuery`, `Game.Objects.Transform` and `ComponentType`.
Two design moves inside it are the whole technique.
`GetAnarchyComponentType()` and `GetTransformLockComponentType()` hand out the mod's own component types as `ComponentType` values, so a consumer can query for them without ever naming them (`:263-276`).
`TryAddToolSystem(ToolBaseSystem)` inverts the relationship: another mod's tool **registers itself** with Anarchy's compatible-tool list (`:24-34`), which is a `HashSet<string>` of tool ids guarded against double-adds (`Anarchy/Anarchy/Systems/Common/AnarchyUISystem.cs:145-154`).
`CS2-MoveIt/Code/MoveIt/Tool/MoveItToolSystem.Lifecycle.cs:107-150` is the matching consumer: it finds the Anarchy assembly, finds `Anarchy.Bridge.AnarchyBridge` by `FullName.Contains`, gets `TryAddToolSystem` by name and binding flags, invokes it with `this`, and latches `m_RegisteredWithAnarchy` on a `true` result.
So the same pair of mods is provider-and-consumer in both directions: Anarchy reflects Move It's tool, Move It pushes itself into Anarchy's registry.

`ExtraDetailingTools/MOD/AnarchyBridge.cs` is the consumer half done at full size, and is the shape a reference should teach.
It resolves the bridge type once, lazily, behind a `_initialized` flag (`:41-66`); caches sixteen `MethodInfo`s resolved by **explicit parameter-type array** rather than by name alone, which is what disambiguates the four overload pairs (`:68-91`); routes every call through `InvokeBool`/`InvokeVoid` helpers that log and return a neutral value when the method is missing or the invocation throws (`:93-128`); and exposes `IsAvailable => GetBridgeType() != null` (`:32`) so callers never branch on reflection state themselves.
Every public method opens with `if (GetBridgeType() == null) return ...`, so the whole facade degrades to no-ops when the other mod is absent.

**Two other provider shapes exist and each carries a different bet.**

`FindIt-CSII/FindIt/Mod.cs:77-100` is a **duck-typed extension point**: Find It walks the mod manager, takes each mod's first `IMod`-derived type, looks for a `public static` method named `GetFindItSearchMethod`, and accepts it only if the signature is exactly `Func<string,bool>(string)` (`:81-86`).
The provider therefore needs no knowledge of Find It at all beyond a method name and a signature, and the check is on the signature rather than on an attribute or an interface.
Find It also publishes an ordinary static of its own, `GetIconsMap()` returning `Dictionary<string,string>` (`:110-113`).

`CS2-WriteEverywhere/BelzontWE/Bridge/` is seven static classes each carrying `[Obsolete("Don't reference methods on this class directly. Always use reverse patch to access them, and don't use this mod DLL as hard dependency of your own mod.", true)]` (`CS2-WriteEverywhere/BelzontWE/Bridge/FontManagementBridge.cs:9`, and the six siblings).
The `error: true` argument makes any compile-time reference a hard compiler error, which is the point: the author is using the C# compiler to enforce the no-hard-dependency rule that the other two shapes only ask for.
The instruction to use a Harmony reverse patch instead of reflection has **no consumer anywhere in the corpus** — `patching.md`'s sweep found zero `[HarmonyReversePatch]` across all 22 repositories, and this pass reproduced it.
So the technique's advertised access path is unexercised, while its enforcement half works.

**Provider signatures decide whether the bridge is usable at all.** `WEModIntegrationUtility.GetModIdentifier` takes an `Assembly` or an `AssetData` and returns a string (`CS2-WriteEverywhere/BelzontWE/Utils/WEModIntegrationUtility.cs`), which a consumer can call reflectively with values it already has; a signature naming a mod-owned type would force the consumer to construct one, which reflection makes possible and unreadable.

Unconfirmed: whether a Harmony reverse patch actually reaches a method carrying `[Obsolete(..., true)]` in this runtime. Nothing in the corpus tries it, and the question is Harmony's rather than the game's — it would be settled by decompiling `0Harmony.dll` for whether `HarmonyReversePatch` inspects attributes on the target, which this pass did not do.

### Four shared namespaces the game hands out first-come-first-served

`custom-tools` owns the tool list and its ruling stands (restated below in `## Bridge`).
Four further global namespaces have the same character — no arbitration, no diagnostic, and the loser discovers it at runtime.

**Prefab identity.** `PrefabID` is `(type name, prefab name, hash)`, and the hash comes from the prefab **asset**: the publishing mod's `platformID` if it has one, otherwise the asset guid (`src/Game/Game.Prefabs/PrefabID.cs:15-35`).
A prefab a code mod builds at runtime through `PrefabBase.Create<T>(name)` has `asset == null` (`src/Game/Game.Prefabs/PrefabBase.cs:417-422`), so its hash is `default` and its identity is nothing but the type name and the name.
`PrefabSystem.AddPrefab` handles the collision by keeping the first registrant's index and logging `Duplicate prefab ID: {0}` — the second prefab's entity is still created and appended to `m_Prefabs`, so it exists and is simply unreachable by id (`src/Game/Game.Prefabs/PrefabSystem.cs:151-159`, the append at `:179-180`), and `AddPrefab` returns `true` either way (`:181`).
`ObsoleteIdentifiers` widens the exposure: every obsolete id a prefab claims goes through the same first-come check (`:164-178`).
**Corrected 2026-08-06 (the mod-compatibility pass's review):** the direction was stated backwards here and shipped that way. The obsolete loop carries the same `ContainsKey` guard as the primary registration and warns rather than overwrites — `if (m_PrefabIndices.ContainsKey(prefabID)) { WarnFormat(prefab, "Duplicate prefab ID: {0} ({1})", …); } else { m_PrefabIndices.Add(…); }` — so an id another mod already holds is never taken, and nothing about it is silent. The real exposure is the mirror: the migrating mod claims the id first and locks out the prefab that legitimately owns it.
Asset-mod prefabs are namespaced by mod id and code-mod runtime prefabs are not, which is the asymmetry a reader has to hold.

**UI resource hosts.** `UISystem.AddHostLocation(hostName, path, ...)` inserts a path into a **list** under the host name, ordered by priority (`src/Colossal.UI/Colossal.UI/UISystem.cs:254-273`), and `DefaultResourceHandler` walks that list in order, returning the first path that resolves (`src/Colossal.UI/Colossal.UI/DefaultResourceHandler.cs:607-621`).

**Verdict: at equal priority the winner is a binary-search artifact, and the shipped reference first said "whichever registered first", which is wrong.** Established during the mod-compatibility pass's review. `AddHostLocation` does `int num = value.BinarySearch(item, kPathPriorityComparer); if (num < 0) { num = ~num; } value.Insert(num, item);`, and `kPathPriorityComparer` (`:155`) compares priority alone. `ModManager.InitializeUIModules` (`ModManager.cs:468`, `:479`) passes `isLocal` as the third positional argument, which is `shouldWatch`, so every `ui-mods` path sits at priority 0 and the whole list compares equal. `BinarySearch` on an all-equal run returns its first probed midpoint, so registering A, B, C, D yields `[C, D, B, A]` — the third registrant is served, and with five it is `[C, E, D, B, A]`. Neither "first wins" nor "last wins" survives past the third registration.
**And the priority integer is a live lever elsewhere:** `GameManager.InitializeUI` passes `asset.priority` for every `UIHostAsset` (`GameManager.cs:1754`, `:1758`), and the shipped `.uiHost` files carry non-default values — the base `gameui` host at `-1000` and each content pack at `-10`. The comparer is ascending and the handler walks front-to-back, so a **lower** number is asked first and wins; any prose implying "raise the priority to win" is inverted. Nothing registers under `ui-mods` at a non-default priority.
Every mod's UI module directory is registered under the **same** host, `"ui-mods"` (`src/Game/Game.Modding/ModManager.cs:468`), so `coui://ui-mods/<file>` is a shared search path across every installed UI mod.
What keeps it collision-free is a naming convention rather than a mechanism: `UIModuleAsset.couiPath` is `"coui://ui-mods/" + Path.GetFileName(path)` (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/UIModuleAsset.cs:40`), and the official scaffold emits the bundle as `<mod.json id>.mjs` into `Mods\<id>\` (`create-csii-ui-mod/template/webpack.config.js:14`, `:28-30`), so the module id doubles as the filename.
Mods that serve their own files pick a private host name instead and never collide: `"platter"` (`CS2-Platter/Platter/PlatterMod.cs:162`), `"findit"` (`FindIt-CSII/FindIt/Mod.cs:50`), `"roadbuilderthumbnails"` and `"roadbuildericons"` (`RoadBuilder-CSII/RoadBuilder/Mod.cs:36`, `:40`), and Hall of Fame's (`HallOfFame/HallOfFame/Mod.cs:153`).
The single-argument `RemoveHostLocation(uri)` drops a whole host and every path under it (`UISystem.cs:306-317`); the game itself always uses the two-argument form when unregistering a UI module (`ModManager.cs:489`).

**Notification identifiers.** `NotificationSystem.Push(identifier, ...)` calls `AddOrUpdateNotification` (`src/Game/Game.PSI/NotificationSystem.cs:22-25`), so two mods pushing the same identifier overwrite each other, and either can `Pop` the other's.
The corpus prefixes: `Traffic` uses `"Traffic_Compatibility_Detector"` (`Traffic/Code/Mod.cs:194`); the game's own are `"ModLoadingStatus"`, `"RestartRequired"` and one per failing mod's asset guid (`ModManager.cs:240`, `:533`, `:288`).

**Settings asset names.** `AssetDatabase.LoadSettings(name, obj, ...)` resolves `name` as a named `SettingAsset` across every database and writes each fragment into the object (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/AssetDatabase.cs:613-666`); the file it lands in comes from a `[FileLocation]` attribute, defaulting to `FallbackSettings.coc` (`:643-649`).
**Verdict: only the `name` collides, and the shipped file first said the file name did too.** Corrected during the mod-compatibility pass's review, against `settings-and-input.md:272` and the save path: `GroupSettingsByFile` keys a `Dictionary<string, List<SettingAsset>>` by `item.meta.path`, and `ProcessSingleSettingsFile` reads the existing file back and re-saves every setting written to it, each as its own `name` block. So several settings classes sharing one `[FileLocation]` coexist as separate named blocks and untouched blocks survive; two mods choosing the same `name` genuinely share a store. `settings-and-input` owns the mechanism; the collision is this topic's.

**Ruled (2026-08-06, the mod-compatibility pass): this topic ships no volatility marker.** The maintainer's call on the review's proposal — the `ui-mods` host name, the scaffold's `<id>.mjs` filename convention and the module-registry operation names do not rot, so none of them earns a sweep entry. The `Rots:` lines below stay as this file's own re-derivation notes and are not to be turned into shipped markers by a later pass.

Rots: the `"ui-mods"` host name and the `coui://ui-mods/<file>` composition — `ModManager.InitializeUIModules` and `UIModuleAsset.couiPath`.

### The module registry composes, and the corpus never uses the operation that does not

The frontend's answer to two mods touching the same component is better than the C# side's, and it is first-party readable in the shipped bundle.

The registry object itself is `DecompiledCitiesSkylines2/src-ui/source.js:13373-13477`.
`override(path, export, value)` saves the pre-existing value into a backup map **only the first time** and then replaces (`:13384-13388`, the backup at `:13387`).
`extend(path, name, cb)` is implemented as `this.override(path, name, cb(current))` (`:13389-13408`, the delegation at `:13406`), so it reads whatever is there — which may already be another mod's wrapper — and installs a wrapper around it.
**Extends therefore chain and overrides clobber**, and the chain's order is registrar order.
`append` is built on `extend` against a modding-hook module, so appends compose too (`:13409-13456`).
`add(path, module)` throws `Module ${e} was already registered...` when the path exists (`:13379-13383`).
An SCSS `extend` called without a callback **merges** class strings rather than replacing them (`:13396-13402`), which is the composable form for styles.
`reset()` clears the append-target set and restores every backed-up vanilla export (`:13471-13475`), so a mod calling it would strip every other mod's UI changes.

**The corpus is unanimous.** Across the 19 repositories that touch the registry, there are **57 `extend` calls and 39 `append` calls, and zero `override`, zero `add`, zero `reset`, zero `find`, zero `get`, zero `hasAppend`** (swept over `**/*.ts` and `**/*.tsx` in all 22 repositories, matching `moduleRegistry.<op>(`).
The hottest targets are shared without incident: `game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.tsx` is extended by nine repositories and `game-ui/game/components/tool-options/tool-options-panel.tsx` by ten.
The append hook targets are `"Game"` (15 sites), `"Editor"` (11), `"GameTopLeft"` (8), `"UniversalModMenu"` (3), `"Menu"` (2), and the declared set is `"Menu" | "Editor" | "Game" | "GameTopLeft" | "GameTopRight" | "GameBottomRight" | "UniversalModMenu"` (`create-csii-ui-mod/template/types/modding.d.ts:6`).
`hasAppend(target)` exists so a component can ask whether anybody appended: the game's own bundle uses it for `"UniversalModMenu"` (`source.js:127073`, the implementation at `:13476`).

**Registrar order is import-completion order, which is nondeterministic.** The loader `import()`s every active UI module's `.mjs` in parallel and pushes each default export into an array as it resolves, then runs them (`source.js:134919-134941`, the push at `:134933`, the completion counter at `:134926`).
`pR` is the runner: `Q.reset(); gR.clear(); for (const t of e) t(Q);` (`:47118-47121`).
So the frontend has the same "order is whatever the runtime gave you" property as the C# side's dictionary iteration, and it is survivable for the same reason — every operation the corpus uses composes.
It also means the registry is **rebuilt from vanilla on every mod-set change**, since `reset()` runs before the registrars.

Rots: the module paths and the append-hook target names, and the two operation counts — re-grep the corpus for `moduleRegistry.` and re-read the registry object in the reformatted bundle.

### Migrating another mod's data: one worked example, and it needs no serialization machinery

`save-serialization.md:310-324` establishes the mechanism and hands this topic the question of when it is legitimate.
The mechanism, restated with its citations because the authoring agent reads this file: `Traffic`'s `TLEDataMigrationSystem` never implements a serialization interface.
The foreign mod's component is already in the world by the time the system runs, because the other assembly loaded and the serializer library reflected over its types like any other.

The four steps, all in `Traffic/Code/Systems/ModCompatibility/TLEDataMigrationSystem.cs`:

1. **Resolve the foreign type by name**, out of the asset database, and turn it into a runtime `ComponentType` via `TypeManager.GetTypeIndex` (`:34-44`); any failure sets `Enabled = false` and logs (`:36-41`, `:52-57`).
2. **Query on it** with an `EntityQueryDesc` whose `All` mixes the foreign type with the mod's own anchors, plus `RequireForUpdate` (`:45-50`).
3. **Run in the deserialize phase**, registered lazily from a main-thread callback once the other mod is known present (`Traffic/Code/Mod.cs:111-112`, `:137`, `:167`).
   **Corrected 2026-08-06 (the mod-compatibility pass's review):** the worked example's choice of the **back** band is one mod's decision, not a requirement, and shipping it as one contradicted the reference that owns the bands — `mod-lifecycle-and-ordering.md:157` puts "readers and migrations in the middle", and a mod's `UpdateAt` already lands after every vanilla `UpdateAt`, which is the only ordering a migration needs. The band belongs to that reference; this topic states the phase and the deferral.
4. **Remove the foreign component afterwards**, commented `// delete TLE data components to prevent data corruption` (`:89-91`), and disable the other mod's system that would otherwise act on it (`Traffic/Code/Mod.cs:166`).

It then tells the player what it did, with a `MessageDialog` naming both mods and the intersection count (`:94-98`).

**Verdict: the worked migration never reads the foreign component's fields, and this file first described it as though it did.** Settled during the mod-compatibility pass's review. `MigrateCustomLaneDirectionsJob` carries no dynamic type handle and no reference to the resolved component — every handle and lookup on it is a vanilla type (`EntityTypeHandle`, `BufferTypeHandle<ConnectedEdge>`, `BufferTypeHandle<SubLane>`, `ComponentLookup<Composition>`, `ComponentLookup<CarLane>`, `ComponentLookup<Curve>`). The foreign type is used for exactly two things: query membership, and `EntityManager.RemoveComponent(entities, _tleComponent)`. The migration re-derives the lane connections from the vanilla state that mod had already written.
A corpus-wide sweep for `DynamicComponentTypeHandle`, `GetDynamicComponentDataArrayReinterpret`, `GetComponentBoxed` and `EntityManager.Debug` returns zero hits in cross-mod interop, and every foreign component named in interop anywhere in the corpus is an empty tag with no fields to read. So the corpus establishes membership-and-removal, and establishes nothing at all about reading foreign values — the two runtime routes that exist for that are in the Entities package and the shipped reference states them on the package's authority, not the corpus's.

**Verdict: the two Entities read routes ship named, with their costs and nothing else.** Getting there took an overshoot in each direction. The review's first attempt stated both APIs precisely and every particular was wrong: `GetComponentBoxed` pins the boxed struct through `GCHandle.Alloc(..., Pinned)`, so it throws `ArgumentException` on any component carrying a `bool` or `char` — a very common mod component shape — which makes "needs no knowledge of the layout" false in the cases that matter; `GetDynamicComponentDataArrayReinterpret<T>` has no one-argument overload, taking `(ref DynamicComponentTypeHandle, int expectedTypeSize)`; the handle's constructor is `internal`, so it must come from `GetDynamicComponentTypeHandle(componentType)`, which is also what registers the job dependency; and a size mismatch does not merely misread bytes, because the returned array's length is `Count * realSize / SizeOf<T>()`, so a `chunk.Count` loop reads past the chunk.
Stripping the names out then went too far the other way: a reader could resolve the type, build the query, register the system and remove the component, and still not write one line that reads a field — and the plugin points at no package docs to fall back on, so the Replace posture became one the file prescribes and does not enable. (That reasoning originally cited the self-sufficiency rule, which [ADR 0005](../adr/0005-every-reference-is-read-beside-the-decompile.md) has since retired; the conclusion survives it, because the decompile the reader now has open is the game's and not the Entities package's.)
What ships is the middle: both member names, verified against the local Entities assembly, each with the one cost that decides which you use — the reinterpret route makes you mirror a layout you do not own, and the boxed route throws on any component carrying a `bool` or a `char`.
The scope lesson stands even though the retreat was reversed: this pipeline establishes the game, not the Entities package, so a package-level claim ships only as a name plus a cost a reader acts on, never as a description of how the API behaves.
One more clause had to come off after that. The boxed route's throw shipped as "on a component carrying a `bool` or a `char` — which most of them do", and the prevalence half was invented rather than counted: parsing every `IComponentData` under `src/Game/` gives 862 components, 56 carrying one of those types directly and 43 transitively, so under a tenth. The clause was the only thing steering the route choice, and it steered readers to the layout-asserting route for nineteen components in twenty. The throw is real and stays; the frequency was never established and is gone.

**Ruled (2026-08-06, the mod-compatibility pass): the reinterpret-does-not-throw claim ships without a marker.** The review proposed `VOLATILE:` on it, since `ArchetypeChunk`'s size check is `[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]` and a future Entities version could enable it. Dropped: the shipped sentence already tells the reader the mismatch reinterprets rather than throws, and this plugin does not version-track the Entities package, so the token would add a sweep entry nobody can close on a game-version pass.

**The same machinery serves a non-destructive purpose, and the corpus has that case too.**
`RoadBuilderCompatibilitySystem` resolves `RoadBuilder.Domain.Components.RoadBuilderUpdateFlagComponent` the same way and queries for it alongside Traffic's own components (`RoadBuilderCompatibilitySystem.cs:28-49`), but it never writes to the foreign component and never removes it — it uses the other mod's tag purely as a signal that a road was regenerated, and resets its own state on those entities (`:52-76`).
`Anarchy`'s Move It integration is the third variant: it reads Move It's `MIT_Original` and copies **its own** components onto the copies Move It made (`CopyAnarchyComponentsSystem.cs:171-195`), touching nothing of the other mod's.

So the corpus has three positions on the same mechanism — read and delete, read only, read and write your own — and only one of them is destructive.

**The destructive position also exists outside the save.** `FindIt-CSII/FindIt/Mod.cs:115-136` deletes two directory trees belonging to a third mod (`ModsData/Gooee` and `Mods/Gooee`) recursively, gated on that mod not being enabled, from a deferred callback, inside a bare `catch {}`.
`CS2-MoveIt/Code/MoveIt/Settings/FileUtils.cs:13-22` faces the same residue and takes the opposite position: it tests for the folders and surfaces a settings warning with a button that **opens** them (`CS2-MoveIt/Code/MoveIt/Settings/LocaleEN.cs:222-240`), leaving the deletion to the player.

**Ruled (2026-08-06, the mod-compatibility pass; conflicts.md).** All three positions ship, as **postures** a mod takes toward another mod's data, over one statement of the mechanism above.
Whether a mod replaces another mod, cooperates with it or ignores it is the mod author's own design decision, so the reference does not license one outcome and attach a condition to it — a condition reads as a permission, and an agent satisfying it would delete on this plugin's authority rather than on its author's.
The ruling touches `save-serialization` only if that reference also states who may remove a foreign component.

The three, named: **replace** migrates the data, removes the foreign component and disables the other mod's system; **cooperate** reads the foreign component as a signal and writes only its own; **coexist** leaves it alone.

What the reference owes beside them is the consequence set, which is not a matter of taste:

- Removal is permanent inside the player's save.
- The other mod is never told, because `IMod` carries no hook that could tell it (`src/Game/Game.Modding/IMod.cs`, and `OnDispose` fires only at process shutdown — `src/Game/Game.SceneFlow/GameManager.cs:792`).
- A directory removed outside the save has no restore path at all, which is what separates the on-disk case from the in-save one rather than a second judgement about intent.
- Replacing is coherent when the mod has taken over the vanilla system that produced the data (`Traffic/Code/Mod.cs:76`, `:168`); otherwise it has deleted data whose producer is still running.
- The worked example tells the player what it did, in a dialog naming both mods and the count (`TLEDataMigrationSystem.cs:94-98`), because the player is the party losing the data.

### What a mod sees when the player changes the active mod set, and what it can do

Nothing calls back into `IMod`. The whole surface is `GameManager`'s playset-change handler and one flag on `ModManager`.

`GameManager` subscribes `OnEntryIsInActivePlaysetChanged` on the Paradox mods data sources (`src/Game/Game.SceneFlow/GameManager.cs:1502`, `:1508`) and dispatches per asset kind (`:1514-1636`). For an `ExecutableAsset` that is an IL assembly:

- **Enabled mid-session** (`!isLoaded && isInActivePlayset`): the asset is collected (`:1574-1577`) and, if any were, `ModManager.Initialize(m_UpdateSystem, reinitialize: true)` runs (`:1626-1629`). That re-runs `RegisterMods` and `InitializeMods`, so the new mod's `OnLoad` fires **there and then**, mid-session, with no restart. Every already-loaded mod is a no-op because `ModInfo.Load` returns immediately on `state != State.Unknown` (`src/Game/Game.Modding/ModManager.cs:95-98`).
- **Disabled mid-session** (`isLoaded && !isInActivePlayset`): `ModManager.RequireRestart()` (`:1570-1573`). The mod is **not** unloaded, its systems keep running, and `OnDispose` is not called. `RequireRestart` sets `restartRequired`, logs `Restart required`, and pushes a `"RestartRequired"` notification whose click opens a confirmation dialog offering to quit the game (`ModManager.cs:524-545`).
- **UI modules** are handled live in both directions — added through `ModManager.AddUIModule` and removed through `RemoveUIModule` (`:1559-1566`, `:1630-1633`, the implementations at `:475-493`) — which is what changes `activeUIModsLocation` on the frontend and re-runs every registrar from a reset registry.
- **Prefab assets** are added to or removed from `PrefabSystem` in batches of a fixed size with a four-frame yield between batches (`:1530-1544`, `:1579-1601`).

**`IMod.OnDispose` fires only at process shutdown**, from `ModManager.Dispose()` at `GameManager.cs:792`, and from `ModManager.InitializeMods`'s catch on a mod whose `Load` threw (`:451-455`). There is no "my mod is being disabled" hook.

What a mod can do about it: read `GameManager.instance.modManager.restartRequired` (`:190`) and re-run its own detection — the corpus's `static bool?` caches never invalidate.
`ModManager.AreCodeModsLoaded()` and the static `AreModsEnabled()`/`GetModsEnabled()` are the other public reads (`:206-232`).

**Verdict: "and nothing else" was wrong — there is a push signal, and it fires at the right moment.** Established during the mod-compatibility pass's review, which this file's first pass missed by searching only `Game.Modding` and `Game.SceneFlow`.
`ParadoxModsDataSource` declares two public events (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ParadoxModsDataSource.cs:30`, `:32`), and `GameManager` subscribes to both (`GameManager.cs:1502-1503`, `:1508-1509`).
`onAfterActivePlaysetOrModStatusChanged` is a plain `Action` invoked at `:161` and `:292`, in each case **after** `await InvokeOnEntryIsInActivePlaysetChanged(...)` has returned — and that awaited call is the one `GameManager` handles by running `ModManager.Initialize(reinitialize: true)`.
So a newly enabled mod's `OnLoad` has already run by the time this event fires, which makes it the correct place to **invalidate** a detection cache.
It is not a place to re-detect and latch, and **re-arming a deferred callback from it does not fix that** — a correction this review made twice before getting it right. The newly enabled mod's own `RegisterUpdater` one-shot is queued into the same `m_Updaters` dictionary, pumped in the same tick, in an order nothing exposes. The boot guarantee does not transfer: at boot `InitializeModManager` completes before the first `GameManager.Update()`, so a deferred reader only has to be after every `OnLoad`; mid-session there is no such fence between two mods' one-shots. So the shipped advice splits the routes by whether their answer is trustworthy when the callback runs. The mod manager and the loaded assemblies are populated inside the awaited handler, before the event, so a no from route one, two, four or five is a real absence and safe to cache. Routes six and seven read what the other mod publishes from its **own** deferred callback, so their no may mean "not yet" — and since the event has already fired, nothing will come along to clear a latched one. Those get resolved per ask instead of cached.
This is what a `static bool?` refilled with `??=` cannot express, and writing the caching code from the earlier draft is what exposed it.

**Verdict: the data path and the mid-session path collide, and the shipped recipe had to name a second resolution route.** Found by writing the migration code for a mod disabled mid-session. Step 1's asset-database route returns nothing once the disable has deleted the assets, while the foreign components are still in the world and the other mod's systems still writing them — so a migration system registered from a fresh `OnLoad` inside that city never registers at all, and one already running either clears a `ComponentType` it can never refill or knowingly ignores the caching rule. The route that survives is the one the detection table already lists and the data section never reached for: `Type.GetType("Ns.Type, Assembly")` with an `AppDomain` scan behind it, against the assembly that is still loaded. A resolved `ComponentType` is process-lifetime and is not a detection answer, so it is held rather than re-resolved.

**Verdict: on a disable the routes actively disagree, and the asset-database one is the odd man out.** The disable branch runs `RemoveEntry(item)` for every one of the mod's entries — reaching `FileSystemDataSource.RemoveEntry` and the database's `Deleted` path — **before** raising the event (`ParadoxModsDataSource.cs:149-161`). Nothing removes the mod's `ModInfo` and its assembly stays loaded, so `GetAsset<ExecutableAsset>` now returns `default` while the mod manager, an `AppDomain` scan and an `EntityQuery` over its components all still report it present and its systems keep updating. A compatibility system that re-runs the asset-database route on this event and honours its own "on failure, disable and log" branch switches itself off while the mod it exists to reconcile is still writing.
A mod reaches the data source the same way `ModRequirement` does: `((ParadoxModsDataSource)AssetDatabase<ParadoxMods>.instance.dataSource)` (`src/Game/Game.Prefabs/ModRequirement.cs:28`).

**Ruled (2026-08-06, the mod-compatibility pass), confirmed by the maintainer against their own play:** enabling a code mod mid-session runs its `OnLoad` there and then, and disabling one leaves it running and only asks for a restart. The reading above therefore ships flat and carries no evidence marker.

### How the game surfaces a mod failure, and how a mod surfaces one of its own

**The game's own path is a notification that opens a dialog, one per failing mod, and it is fully localized.**
`ModManager.Initialize` loops the mod infos after initialization and pushes a notification for every mod whose `state >= ModInfo.State.IsNotModWarning`, keyed by the mod's asset guid, carrying the mod's display name, its store thumbnail and a `ProgressState` mapped per state — `Warning` for the two warnings, `Failed` for the four errors (`src/Game/Game.Modding/ModManager.cs:267-292`).
Clicking it opens a `MessageDialog` titled `Common.DIALOG_TITLE_MODDING[ModLoadingWarning|ModLoadingError]` with body `Common.DIALOG_MESSAGE_MODDING[{state}]` and a `MODNAME` argument; when `loadError` is set the dialog also carries a copyable details pane, with backslashes and asterisks escaped (`:292-315`).
A non-local mod gets two extra actions, `[ModPage]` and `[Disable]`, whose callbacks call `PdxSdkPlatform.ShowModDetail` and `EnableModInActivePlayset(..., false)` (`:300-304`, `:316-335`).
So the player can disable the offending mod from the dialog.

**Decoding the shipped `en-US.loc` confirms every one of these keys exists at 1.6.0f1** — 22,120 entries, decoded with the recipe in `method-decoding-shipped-locale-data.md` from `Cities2_Data/Content/Game/Locale.cok`.
Present: `Common.DIALOG_TITLE_MODDING[ModLoadingWarning]`, `[ModLoadingError]`; `Common.DIALOG_MESSAGE_MODDING[IsNotModWarning]`, `[IsNotUniqueWarning]`, `[GeneralError]`, `[MissedDependenciesError]`, `[LoadAssemblyError]`, `[LoadAssemblyReferenceError]`, `[ModPage]`, `[Disable]`; `Common.DIALOG_MESSAGE[EnabledModsChanged]`; `Menu.NOTIFICATION_TITLE[ModsLoading]`, `[EnabledModsChanged]`; `Menu.NOTIFICATION_DESCRIPTION[ModsLoadingWaiting]`, `[ModsLoadingInitialize]`, `[ModsLoadingDone]`, `[ModsLoadingDoneZero]`, `[ModsLoadingFailed]`, `[ModsLoadingAllFailed]`, `[EnabledModsChanged]`.
That is exactly the six failure states and no more — there is no key for `Loaded`, `Unknown` or `Disposed`, which matches the `state >= IsNotModWarning` gate.
The translated text is the publisher's and stays out of this file.

**The frontend contributes nothing.** The reformatted bundle's only occurrence of any of these is the generated `Loc` dictionary entry `DIALOG_MESSAGE_MODDING: new Tc("MODNAME")` (`DecompiledCitiesSkylines2/src-ui/source.js:26853`); `ModLoadingWarning`, `EnabledModsChanged` and `ModsLoading` do not appear at all. The whole surfacing path is C#.

**A mod reporting its own incompatibility copies that path exactly.** `Traffic/Code/Mod.cs:195-210` is the corpus's worked example and matches the game's shape step for step: `NotificationSystem.Push(id, title, text, progressState: ProgressState.Failed, onClicked: ...)`, and the click builds a `Game.UI.MessageDialog(title, message, details, copyButton: true, LocalizedString.Id("Common.OK"))` shown through `GameManager.instance.userInterface.appBindings.ShowMessageDialog(dialog, _ => NotificationSystem.Pop(id))` — the callback popping the notification it came from.
The message strings are literals with `**bold**` markup, not localization keys, and the file path is escaped for the same markup (`:203`).
`TLEDataMigrationSystem.cs:94-98` uses the dialog alone with no notification, for a success report rather than a failure.
`CS2-MoveIt`'s Gooee case uses neither: it puts the warning and its action button on the mod's own settings page (`CS2-MoveIt/Code/MoveIt/Settings/FileUtils.cs:13-22`, `LocaleEN.cs:222-240`).

Rots: the eighteen localization key ids listed above — re-derive by decoding `Cities2_Data/Content/Game/Locale.cok` and filtering the `en-US` key set for `MODDING`, `ModsLoading` and `EnabledModsChanged`.

### The declared dependency channels, and what each one actually enforces

Three exist. Only one of them stops anything.

**Publish-time, to the store: enforced by the platform, invisible to the game.**
`<Dependency Id="<pdx mod id>" DisplayName="..." Version="..." />` in the mod project's `Properties/PublishConfiguration.xml` becomes a `PDX.SDK.Contracts.Service.Mods.Models.ModDependency` with `Type = DependencyType.Mod` at upload (`src/Colossal.PSI.PdxSdk/Colossal.PSI.PdxSdk/PdxSdkPlatform.cs:2049-2057`, composed into the staging metadata at `:1830`).
Sixteen of the 22 repositories declare at least one real id; the most common by far is `Id="74417"` (Unified Icon Library) in thirteen of them, ten of those pinned at `Version="1.0.13"`.
Three more carry only the scaffold's placeholder, unfilled: `NodeController/NodeController/Properties/PublishConfiguration.xml:56`, `SceneExplorer/SceneExplorer/Properties/PublishConfiguration.xml:37`, `Traffic/Code/Properties/PublishConfiguration.xml:95`.
Nothing in `src/Game/` reads this at runtime.

**Prefab-level, inside the game: enforced, and automatic for asset mods.**
`PrefabBase.GetPrefabComponents` adds `ModPrerequisiteData` to any prefab whose asset carries a non-empty `platformID` (`src/Game/Game.Prefabs/PrefabBase.cs:362-370`), and `GetDependencies` pulls in a `ContentPrefab` named `"Mod:<platformID>"` (`:348-360`), created on demand by `PrefabSystem.GetOrCreateContentPrefab` with a `ModRequirement` component (`src/Game/Game.Prefabs/PrefabSystem.cs:330-340`).
`ModRequirement.CheckRequirement()` is `((ParadoxModsDataSource)AssetDatabase<ParadoxMods>.instance.dataSource).ContainsActiveMod(m_ModId)` (`src/Game/Game.Prefabs/ModRequirement.cs:26-29`), and `ContentPrefab.Initialize` sets `ContentFlags.RequireMod` from its presence (`src/Game/Game.Prefabs/ContentPrefab.cs:37-40`).
`PrefabSystem.AddPrefab` refuses outright when `IsAvailable(prefab)` is false, logging `Dependency not available in {0}: {1}` (`PrefabSystem.cs:112-128`, the check at `:484-519`).
A prefab may also declare an explicit `ContentPrerequisite` pointing at any `ContentPrefab`, which the same availability check honours (`:505-514`).
**The limit is the one that matters here:** the hash and the prerequisite both come from `prefab.asset`, and a prefab a code mod creates at runtime has none, so this channel covers asset mods and never a code mod's synthesized prefabs.

**UI-module level: declared and inert.**
A `.mjs` carries a leading block comment with `Id`, `Author`, `Version` and `Dependencies`, parsed line by line into `UIModuleAsset.ModuleInfo` (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/UIModuleAsset.cs:11-25`, `:44-110`, the `Dependencies` case at `:103-107`).
`PostCreate` turns the module id, the author and **each declared dependency** into asset **tags** and does nothing else with them (`:112-127`, `AddTags` at `:123`).
A grep of the whole decompile for `m_UIModuleDependencies` returns that field, its parse and its `AddTags` call, and no consumer anywhere.
The official scaffold populates it from `mod.json`'s `dependencies` array through the webpack banner (`create-csii-ui-mod/template/webpack.config.js:16-23`, `create-csii-ui-mod/template/mod.json`).
**All 19 corpus UI mods declare `"dependencies": []`**, so the channel is unused as well as unenforced.

Rots: the `Id="74417"` pin and the thirteen repositories carrying it — re-grep `**/PublishConfiguration.xml` for `<Dependency`.

### The three patching facts that are compatibility facts first

`patching.md:351` hands these over and asks that they be stated once. Re-derived here rather than restated; the ownership proposal is in `## Bridge`.

**Exactly one `0Harmony` is in the process.** This is the same deduplication as the first finding: 33 of the 147 code mods in the Paradox cache ship `0Harmony.dll`, and the loader collapses them to one by simple assembly name.
Because that copy's `Harmony` type carries the static patch registry, every patching mod in the process shares one registry, which is what makes cross-mod patch order a real question.
None of the copies is strong-named, so version binding is not enforced (`patching.md:87-88`).

**Cross-mod patch order on a shared target is unspecified in practice.** Harmony sorts patches by priority descending then registration order (`patching.md:170`), mod initialization order is `Dictionary<Identifier, ModInfo>` iteration order (`src/Game/Game.Modding/ModManager.cs:180`, iterated at `:438`), and no corpus repository uses `HarmonyPriority`, `HarmonyBefore` or `HarmonyAfter` (`patching.md:375`).
So registration order is the only tiebreak anyone uses and nobody controls it.

**Flag ownership is what lets two mods patch one surface and survive.** `CS2-Platter/Platter/Patches/MarkerPatches.cs` reads a raycast flag before setting it, sets it only if it was clear, records in a `[ThreadStatic] bool` whether it was the one that set it (`:33-37`, `:54-61`), and runs its result filter only when that flag is true (`:170-193`, `:199-218`).
Its own comment gives the reason (`:25-29`).
The general rule underneath is the transferable part: **a patch that widens something and then narrows the results owns both halves or neither**, because the narrowing is only correct for what its own widening caused, and a filter that runs unconditionally vetoes hits that belong to somebody else — with the symptom landing in the other mod.
It also degrades to a no-op if a future game build sets the flag itself.

## Bridge

**Mechanics topics this technique serves.** Compatibility is a property of every mechanics area rather than of one, so only the links with a mechanism behind them are asserted.

- **`roads-and-traffic`** is where the corpus's cross-mod collisions actually happen, and it is the only mechanics area with a worked cross-mod migration. `Traffic`, `NodeController`, `RoadBuilder` and the TLE lane-system fork all write to `Game.Net.Node` and `Edge`; `Traffic` disables the vanilla `LaneSystem` outright (`Traffic/Code/Mod.cs:76`) and so does the TLE fork it replaces (`:168`), which is why two mods in this area cannot both be loaded without one of them knowing about the other. The reference for that area is where "which vanilla system does this mod take over" has to be answerable.
- **`zoning-buildings-and-land-value`** carries the second collision: `PlopTheGrowables`' `LevelLocked` tag is read by `BetterBulldozer` (`SubElementBulldozerTool.cs:259-270`) and `Platter`'s `ParcelPlaceholderData` by `Anarchy` (`AnarchyUISystem.cs:332-342`), so a component two mods share is a zoning component in both cases.
- **`city-state-and-progression`** owns `CityConfigurationSystem` and therefore `usedMods`, the cumulative record of every mod a city has ever been saved with (`save-serialization.md:358`). That is the only durable, in-save trace of the mod set, and it is a superset of what the city currently needs.

**Sibling techniques.**

- **`patching`** is the boundary partner and the file that handed three findings here (`patching.md:351`). The proposed split: **this topic states them, `patching` points at them.** The reason is that all three are consequences of one mechanism this topic owns end to end — assembly-name deduplication (`ExecutableAsset.cs:341-360`) — and the flag-ownership discipline is a rule about composing with a stranger rather than a rule about Harmony. `patching` keeps the Harmony-specific halves it alone can state: the priority comparer and the `Priority` constants (`patching.md:170`), the version census of `0Harmony.dll` copies (`:87`), and the unguarded-postfix property that makes an unconditional filter dangerous (`:290`). `custom-tools` keeps the raycast instance (`custom-tools.md:216-218`), as the two already agreed.
- **`save-serialization`** owns the format, the interfaces and the migration machinery; this topic owns when reaching into another mod's data is legitimate, which was ruled on 2026-08-06 as the mod author's own choice between three postures — see the migration finding. Take from that file, unchanged: a foreign mod's save section is skipped rather than fatal, `usedMods` is cumulative, and a foreign mod's components are readable and removable by anyone who can resolve the type by name (`save-serialization.md:310-324`, `:358`, `:386`).
- **`custom-tools`** owns `ToolSystem.tools` and its ruling is not reopened here. **Ruled (2026-08-02, the custom-tools pass; conflicts.md):** the restrained form is the default — a tool takes the slot of the one tool it must precede, read back with `tools.IndexOf(...)` — and index 0 ships as an exception bound to returning `true` from `TrySetPrefab` only while already active. This topic's share is the general shape that ruling is an instance of: a shared ordered resource with no arbitration, where the cooperative move is to state your position relative to what you need rather than to claim the front. The other three unarbitrated namespaces are above and belong here.
- **`mod-lifecycle-and-ordering`** owns `IMod.OnLoad`, phase registration and the deferred-work idiom. Two facts established here belong to it rather than to a compatibility reference, and it should state them: `MainThreadDispatcher.RegisterUpdater(Action)` runs its action exactly once on the next `GameManager.Update()` (`MainThreadDispatcher.cs:97-104`, driven at `GameManager.cs:713`), and mod initialization order is dictionary iteration order (`ModManager.cs:180`/`:438`). This topic depends on both and should not re-teach either.
- **`cs2-mod-project`** owns the build and the publish. Three facts here are its material and should be stated once, there: the `<Dependency Id="..." />` element in `PublishConfiguration.xml` and that it is a store relationship with no runtime effect; that `mod.json`'s `dependencies` array reaches the game as inert asset tags; and that shipping a copy of a game assembly gets it dropped with a warning (`ExecutableAsset.cs:325-329`). The compatibility reference should point at them rather than repeat them.
- **`localization`** already states the collision from its own side (`localization.md:657`): a mod overriding a vanilla key silently changes what every other mod's UI shows, and the only guard anyone uses is `activeDictionary.ContainsID(...)` before adding (`ExtraAssetsImporter/MOD/OldImporters/DecalsImporter.cs:181-182`), because later sources overwrite earlier ones (`src/Colossal.Localization/Colossal.Localization/LocalizationDictionary.cs:87`). That is a fifth unarbitrated namespace and it stays in that reference; the compatibility reference names the class of hazard and points there.
- **`prefabs-and-assets`** owns `PrefabID`, `PrefabSystem.AddPrefab` and `ObsoleteIdentifiers`. The duplicate-id behaviour above is a compatibility consequence of its mechanism; whichever states it, the first-come-first-served rule and the empty hash on a runtime-created prefab have to appear together, because either alone is misleading.
- **`settings-and-input`** owns `LoadSettings`, `[FileLocation]` and the input-conflict machinery. The settings-name collision above is one sentence of its material seen from this side.
- **`diagnostics`** owns the log. Take from here the strings a compatibility problem produces, all on the `Modding` logger (`ModManager.cs:178`, `ExecutableAsset.cs:117`): `Assembly "{0}" is in-game assembly and it should NOT be shipped with mod "{1}"`, `Error loading assembly {0}`, `Error loading assembly reference {0}`, `Error initializing mod {0} ({1})`, `Restart required`, plus `Duplicate prefab ID: {0}` and `Dependency not available in {0}: {1}` on the base log. `Modding.log` also carries the `======= Active Playset =======` and `======= Enabled Mods =======` block written before any mod loads (`ModManager.cs:366-395`), which is the fastest way to see what the player actually had on.
- **`frontend-and-injection`, in the UI skill**, owns the module registry. The composability finding above is the compatibility half of its subject: `extend` and `append` chain, `override` and `add` do not, `reset()` is global, and registrar order is import-completion order. The two references must not both teach the registry API; this one should state only the composition property and the count behind it.

---

## Dead ends

- **The wiki has no page on mod-to-mod compatibility, and this was checked rather than assumed.** `survey-wiki-inventory.md` lists 42 pages with `**\`Name\`**`headings plus the thin player-side cluster, and none of them is about two mods interacting.`Mod security` (`survey-wiki-inventory.md:71`) is player-facing — trusted sources, out-of-date mods, Skyve, and "BepInEx & BepInEx mods – do not use". `Mods` (`:75`) is a stub about installation. `Naming Folder And Files` (`:50`) is the closest thing to a compatibility page and is a filesystem-layout convention rather than a mechanism: the decompile has no `ModsData`or`ModsSettings`constant anywhere (grepped over all of`src/`; the only hits are `ParadoxModsDataSource`and its siblings), so those paths are a convention the corpus follows by hand —`Anarchy/Anarchy/Systems/Common/AnarchyUISystem.cs:243`, `AreaBucket/Utils/DumpUtils.cs:22`, `CS2-MoveIt/Code/MoveIt/Settings/FileUtils.cs:13`all compose them from`EnvPath.kUserDataPath`. The wiki was not fetched live: there was no page to fetch.
- **The frontend as a source on mod conflicts.** The reformatted bundle was searched for `ModLoadingWarning`, `DIALOG_MESSAGE_MODDING`, `EnabledModsChanged`, `ModsLoading` (one hit, the generated `Loc` entry at `source.js:26853`) and for `Dependenc` (12 hits, all react-spring animation internals around `:95430-95511`). The mod-loading dialogs, the restart prompt and the whole dependency story are C#-side; the store browser that would show a dependency is Paradox-hosted and not in this bundle. The absence is a finding — a mod cannot extend or intercept the conflict UI, because there is none to extend.
- **A first-party arbitration mechanism for two mods claiming the same runtime resource.** Searched `Game.Modding`, `Game.Tools/ToolSystem.cs`, `Colossal.UI/UISystem.cs`, `Game.Prefabs/PrefabSystem.cs` and `Game.PSI/NotificationSystem.cs` for any priority, ownership or arbitration concept beyond the ones above. There are exactly two: the resource-host priority integer on `AddHostLocation` (`UISystem.cs:254`) and the tool-list index. Everything else is first-come-first-served or last-write-wins.

  **Two corrections from the mod-compatibility pass's review.** The priority integer is not "never passed non-zero" — `GameManager.InitializeUI` passes each `UIHostAsset`'s own value and the shipped `.uiHost` files use it; what is true is that no `ui-mods` registration passes one. And `Game.Input` was not in the searched set: `InputConflictResolution.ResolveConflicts` does arbitrate, disabling a mod action that collides with a vanilla one and pushing a conflict notification. It runs system×UI, system×mod and UI×mod and has **no mod×mod loop**, so for two mods claiming one binding it neither picks a winner nor notifies — `ConflictType.WithNotBuiltIn` is read only by the options-page display predicate. The dead end's conclusion therefore stands for mod-versus-mod, which is this topic's scope, but the sweep that reached it was narrower than the claim.

- **`ModInfo.instances` as a route to another mod's `IMod` object.** It is `public IReadOnlyList<IMod>` (`ModManager.cs:47`), so a mod can reach another mod's live instance and reflect over it. No corpus repository does: every one of the eleven detectors goes through the _type_ (`asset.assembly.GetTypesDerivedFrom<IMod>()` in Find It's case, `ModManager.cs:119`) and reaches a static, never an instance. The reason is visible in the loader — mods are constructed with `FormatterServices.GetUninitializedObject` (`:121`), so no constructor ran and no instance field is guaranteed initialized beyond what `OnLoad` set.
- **`Colossal.PSI.PdxSdk` as a runtime dependency source.** `PdxSdkPlatform` carries `GetAllDependencies`, `GetDependencyCandidates` and a `ModDependency` model (`PDX.SDK/PDX.SDK.Contracts.Service.Mods/IModsInformationService.cs:15-19`), all of it store-side and reached only from the upload path (`PdxSdkPlatform.cs:1830`, `:2049-2065`). Nothing in `src/Game/` queries a mod's declared dependencies at load. The only runtime read of the active mod set is `ParadoxModsDataSource.ContainsActiveMod`, through `ModRequirement`.
- **The corpus's use of `moduleRegistry.override`.** Swept all 22 repositories' `.ts` and `.tsx` for each of the eight registry operations. `override`, `add` and `reset` return zero on any receiver, aliased or not, and that absence is a real finding: nobody has hit the `add`-throws-on-second-run hazard that `pR`'s `Q.reset()` loop implies.

  **Corrected 2026-08-06 (the mod-compatibility pass's review):** the same sweep's zero for `find` and `get` was an artifact of pinning the receiver to the literal `moduleRegistry.`, and this is [a search taken for a census](../solutions/empty-grep-read-as-proof-of-absence.md) exactly. `source.js:47074-47075` aliases them — `const uR = Q.find, dR = Q.get;` — and exports the pair to mods as `findModule`/`getModule` on `cs2/modding` (`:13367`, handed over at `:47086`), so the registry's read side is in fact the operation the corpus uses most, across roughly 147 files. Mods also read the backing map directly through aliased receivers a receiver-pinned pattern cannot see (`registry.registry.get(...)`, `this.registryData.registry.get(...)`, `for (const [key, module] of moduleRegistry.registry)`). Re-sweep by operation name across every binding, not by receiver.

- **A Harmony reverse-patch consumer of Write Everywhere's bridge.** Grepped all 22 repositories for `ReversePatch` and `HarmonyReversePatch`: zero, reproducing `patching.md:373`. The provider's own instruction has no worked example anywhere this pipeline can reach.
- **The live game.** `mcp__unity__status` found no Cities: Skylines II process on 2026-08-06; the four SDB ports belonged to IDEs and Git clients. Every live question in this file therefore records its experiment rather than its result. What the game would have to be carrying for each is named at the claim.
- **`docs/research/conflicts.md`.** Read in full, ruled entries included. No entry names this topic. The `custom-tools` ruling of 2026-08-02 (the custom-tools pass) governs `ToolSystem.tools` and is restated in `## Bridge` rather than contradicted. One new entry was appended (2026-08-06, the mod-compatibility pass).

---

## Catalog gaps

All five were applied to `plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md` during the mod-compatibility pass. Kept here for the source lines, which a later pass reuses.

- **Anarchy — the provider half of its bridge**, which its entry named only from the consumer side: `Anarchy/Anarchy/Bridge/AnarchyBridge.cs:17` (the class), `:24-34` (`TryAddToolSystem`), `:42-52` and `:60-75` (the `Try*` component adds), `:263-276` (the two `Get*ComponentType` statics), `Anarchy/Anarchy/Systems/Common/AnarchyUISystem.cs:145-154` (the registry it writes into).
- **Move It — the push direction**, the corpus's only instance: `CS2-MoveIt/Code/MoveIt/Tool/MoveItToolSystem.Lifecycle.cs:107-150`, `CS2-MoveIt/Code/MoveIt/Tool/MoveItToolSystem.Bridge.cs:15-25`.
- **Extra Detailing Tools — what makes its facade survive**, rather than that it has one: `ExtraDetailingTools/MOD/AnarchyBridge.cs:32` (`IsAvailable`), `:41-66` (the one-shot type resolution), `:68-91` (the signature-qualified `GetMethod` calls), `:93-128` (the invoke helpers).
- **Traffic — the non-destructive compatibility system** beside the migration: `Traffic/Code/Systems/ModCompatibility/RoadBuilderCompatibilitySystem.cs:28-29` (the resolution), `:37-49` (the query descriptors and `RequireForUpdate`), `:52-76` (the reset).
- **Advanced Line Tool — its yielding** of shared affordances: `LineTool-CS2/Code/CompatibilityHoverColors.cs:26-63`, `LineTool-CS2/Code/Systems/LineToolSystem.cs:499-510` (the snap yield), `:649-651` (the transparency yield), `:657-668` (the second competitor's detection).

**Correction (2026-08-06, the mod-compatibility pass's review):** the Extra Detailing Tools entry originally proposed "whose sixteen methods" — the provider carries fifteen `public static` members, and the consumer caches sixteen `MethodInfo`s, one of which resolves to nothing and leaves that entry point permanently a no-op. The count was dropped rather than corrected, so nothing shipped wrong; the mismatch is better evidence for the facade technique than the figure was.

## Source-list gaps

- **`docs/SOURCES.md` entry 6 did not name the scaffold's `mod.json`.** Applied. The template's `mod.json` carries lowercase `id`, `author`, `version` and `dependencies`; `create-csii-ui-mod/template/webpack.config.js:16-23` interpolates them into a banner using the capitalised `Id`/`Author`/`Version`/`Dependencies` keys that `UIModuleAsset.cs:72-110` parses back. The two casings are not interchangeable, and the banner reaches the bundle through the minifier's `extractComments.banner` option (`webpack.config.js:97-99`) rather than a banner plugin.
- **Entry 11's pointer to the catalog.** No correction proposed. This pass used the corpus as entry 11 prescribes — as evidence of what mod authors do, never to settle a fact about the game.
