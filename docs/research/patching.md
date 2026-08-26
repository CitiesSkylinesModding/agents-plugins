# Patching

**Baseline.** Decompiled game 1.6.0f1, read 2026-08-04.
Mod corpus read 2026-08-04 at the commits the checkout carried; the checkout holds **22 repositories**, not the twenty earlier passes measured against, and the twelve-repository seed survey's `Cities2-TrafficLightsEnhancement` is no longer among them (the count and the roster are re-derived below).
Installed game read at 1.6.0f1 — `Cities2_Data/Managed/` under `%CSII_INSTALLATIONPATH%`, the toolchain cache at `%CSII_TOOLPATH%`, and, under `%CSII_USERDATAPATH%`, both the log directory (`Logs/`) and the Paradox mods cache (`.cache/Mods/pdx_mods/`).
Wiki fetched live 2026-08-04; the bot challenge did not fire, so no snapshot substitution was needed.

**One source had to be transformed before it could be read at all.** Harmony ships as a compiled assembly inside mods and nowhere else, so its own behaviour was read from a decompile of it: `ilspycmd` over `0Harmony.dll`, assembly identity `0Harmony, Version=2.2.2.0, Culture=neutral, PublicKeyToken=null`, taken from a copy in the user's Paradox mods cache and emitting a single file of **65,669 lines**.
Citations of the form `0Harmony.decompiled.cs:<line>` are to that file.
Any 2.2.2.0 copy decompiles identically; re-check the line count before trusting a line number, exactly as for the reformatted UI bundle.

---

## Findings

### Patching is the exception, and the evidence is now much stronger than the corpus alone can make it

**The corpus half, re-derived at 22 repositories.**
Nine repositories apply Harmony patches; twelve use no Harmony API anywhere; one is undetermined.

Applying patches, each creating its own `Harmony` instance in `IMod.OnLoad`: `Anarchy/Anarchy/AnarchyMod.cs:128-129`, `BetterBulldozer/BetterBulldozer/BetterBulldozerMod.cs:104-105`, `CS2-Platter/Platter/PlatterMod.cs:340-341`, `ExtraDetailingTools/EDT.cs:111-112`, `LineTool-CS2/Code/Patches/Patcher.cs:92-96`, `Time2Work/NightShift/Mod.cs:164-166`, `Tree_Controller/Tree_Controller/TreeControllerMod.cs:115-116`, `Water_Features/Water_Features/WaterFeaturesMod.cs:121-122`.
A ninth patches through its own closed-source wrapper rather than the Harmony API directly: `CS2-WriteEverywhere/BelzontWE/Overrides/PrefabSystemOverrides.cs:5/20-21`, a `Redirector` subclass whose `AddRedirect(original, prefix, postfix)` is the framework's own name for a patch.

Using no Harmony API at all, in any file: `AreaBucket`, `CS2-MoveIt`, `CS2-NetworkTools`, `ExtraAssetsImporter`, `FindIt-CSII`, `HallOfFame`, `NodeController`, `PlopTheGrowables`, `Recolor`, `RoadBuilder-CSII`, `SceneExplorer`, `Traffic`.
Two of those twelve nevertheless declare the dependency and never use it — `NodeController/NodeController/NodeController.csproj:105` and `RoadBuilder-CSII/RoadBuilder/RoadBuilder.csproj:66` both carry `<PackageReference Include="Lib.Harmony" Version="2.2.2" />` while no `.cs` file in either repository names `HarmonyLib`.
That costs them a shipped file rather than nothing: the toolchain's deploy target copies the whole build output, `<FilesToDeploy Include="$(OutDir)\**\*.*" />` (`%CSII_TOOLPATH%/Mod.targets:106-113`), so an unused package reference ships its DLL beside the mod.

Undetermined: `InfoLoom` declares one patch class, `InfoLoom/InfoLoom/Patches/Patches.cs:17-42`, a prefix on `CityInfoUISystem.WriteDemandFactors` returning `false`.
No file in the repository creates a `Harmony` instance, and its project imports `Common\ModsCommon.props` (`InfoLoom/InfoLoom/InfoLoomTwo.csproj:18`) from a directory absent from the checkout, so its package references cannot be read.
The patch class is therefore present and, on the checked-out source alone, unwired.

**Width, where mods do patch.** Distinct vanilla methods targeted: Time2Work 16, CS2-Platter 13, ExtraDetailingTools 11 (plus two more applied imperatively), Anarchy 6, Tree_Controller 3, BetterBulldozer 2, CS2-WriteEverywhere 2, LineTool-CS2 1, Water_Features 1.
Median 3; five of the nine are three methods or fewer.

**Verdict on the seed survey's ratio.** `survey-mods-techniques.md:71` states "Only **6 of 12** use Harmony at all" and names Anarchy, BetterBulldozer, Platter, LineTool, Tree_Controller and WriteEverywhere.
At 22 repositories the ratio is **9 of 22**, and the survey's roster is a strict subset: Time2Work, ExtraDetailingTools and Water_Features are three patching repositories it never saw.
Its direction holds and its shape is right — "two or three methods wide" matches the median exactly — but it understates the tail, since two mods now sit at 13 and 16 targets.
The survey's conclusion, "in CS2, Harmony is a last resort, not the default. The default is system insertion + ordering," survives the widened corpus.

**The corpus is a biased sample, and the install carries an unbiased one.**
The user's Paradox mods cache holds **390 distinct mod ids**, of which **147 ship a managed assembly of their own** (excluding `0Harmony.dll` and the `*_win_x86_64.dll` Burst sidecars).
Of those 147 code mods, **33 ship `0Harmony.dll`** — 22%, against 41% in the hand-picked corpus.
That is a presence census rather than a reading of source, and it can only over-count: two of the corpus's own repositories prove a mod can ship the DLL and patch nothing.
So at population scale, fewer than a quarter of code mods even carry the patching runtime, and fewer than that use it.

`docs/SOURCES.md` lists the Paradox mods cache under "What looks like a source and is not", on the correct ground that compiled assemblies are not source.
That ruling stands for behaviour and should be narrowed for presence: **which files a mod ships is readable without decompiling anything, and it is the only unbiased sample of the ecosystem this pipeline can reach.** See the source-list finding below.

**The wiki agrees, and states the ordering rather than the ratio.**
`Prefab - Quick Start` (https://cs2.paradoxwikis.com/Prefab_-_Quick_Start, fetched 2026-08-04) gives three remedies for refreshing runtime values after a prefab edit, in order: a player action that makes the game's own job run; custom code that copies the job the player action triggers ("If you can find the specific game job using iLSpy, copy the same method used to complete Option 1, then run it when you like"); and third, "Harmony Patch Code", with its own tradeoff attached — "Sometimes easier than option 2 as there are 1000's of code lines to research and you might not find a lucky hook. Could be brittle on game patch days."
`Creating a Tool` (https://cs2.paradoxwikis.com/Creating_a_Tool, fetched 2026-08-04) is the only other page in the wiki naming Harmony: "You can use Harmony on a vanilla tool system's TrySetPrefab to stop the UI from activating that tool so you can use that menu for picking prefab(s) for your tool. Please only do this while your tool is active."

Rots: every count in this finding — the repository roster, the per-repo target counts, and the cache census. Re-derive by grepping the corpus for `new Harmony(` and `[HarmonyPatch`, and by counting `0Harmony.dll` against managed assemblies under the Paradox mods cache.

### The game knows about Harmony, and its own patch inventory never prints

`GameManager.ListHarmonyPatches()` exists and is thorough: it finds the first loaded assembly whose name contains "Harmony", resolves `HarmonyLib.Harmony`, calls the static `GetAllPatchedMethods()` and `GetPatchInfo(...)`, and logs every patched method followed by its prefixes, postfixes, transpilers and finalizers by declaring type and name (`src/Game/Game.SceneFlow/GameManager.cs:2155-2206`, `PrintPatchDetails` at `:2210-2227`, `PrintIndividualPatches` at `:2229-2248`).
It logs to the `Modding` logger, which is the same one `ModManager` writes to (`src/Game/Game.Modding/ModManager.cs:178`).

**It runs before any mod is loaded, so it always finds nothing.**
The call sits at `GameManager.cs:582`, inside `Initialize()`. `m_ModManager` is not even constructed until `:605`, and `InitializeModManager()` — which is what eventually reaches `IMod.OnLoad` and therefore `PatchAll` — is at `:618`.
The three statements are in one straight-line `async` body with no branch between them.

**Confirmed against the running game (source 8) and its log.**
`%CSII_USERDATAPATH%/Logs/Modding.log` opens on `Modding runtime: Builtin`, which is `ListHarmonyPatches`' own first `InfoFormat` (`GameManager.cs:2158`), and then contains no `Harmony found.` line and zero `Patched Method:` lines across a session that loaded nine mods.
None of those nine happens to patch, and `find_types` against the live process returns no `HarmonyLib.Harmony`, so this run does not discriminate on its own; the call-order read does.

The consequence for a mod author is practical: **there is no game-provided way to see what is patched.** The one mod in the corpus that wanted the answer built it itself, logging `harmony.GetPatchedMethods()` immediately after `PatchAll` (`ExtraDetailingTools/EDT.cs:113-118`).

Rots: the position of `ListHarmonyPatches()` relative to `InitializeModManager()` — re-check `src/Game/Game.SceneFlow/GameManager.cs:582/618`.

### Exactly one Harmony is loaded per process, and which one is not the one you compiled against

Harmony ships in no first-party location. `Cities2_Data/Managed/` holds `Colossal.Mono.Cecil.dll` and no `0Harmony.dll`, and neither `Mod.props` nor `Mod.targets` in the toolchain cache mentions Harmony at all. Every patching mod brings its own copy through `Lib.Harmony`, and the deploy target ships it.

**The loader deduplicates by simple assembly name across every installed mod.**
`ExecutableAsset.ResolveModAssets` groups all executable assets by `a.definition.Name.Name` and marks every member of a group as the others' duplicate (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ExecutableAsset.cs:338-360`).
Reference resolution then prefers a sibling in the mod's own folder — `database.Exists<ExecutableAsset>(AssetDataPath.Create(subPath, assemblyReference.Name, ...))` at `:366-368` — falling back to a global resolver at `:371-379`.
But `LoadAssembly` does not load the asset it was called on: it calls `GetUniqueVersionAsset()` and loads that instead (`:213-217`).
The winner is ordered `isLoaded descending, isLocal descending, version descending, id` (`:181-190`, the `orderby` at `:189`).

So three things follow, and all three are load-bearing.
**One `0Harmony` exists in the process**, which is why the game's own `FirstOrDefault(a => a.GetName().Name.Contains("Harmony"))` is a reasonable thing to write.
**Its static patch registry is therefore shared by every patching mod**, which is what makes cross-mod prefix ordering a real question rather than a hypothetical.
**The winner is decided by locality, then by version, then by asset id, and the `isLoaded` key ahead of them cannot fire at a cold boot.**
`isLoaded` is `assembly != null` (`ExecutableAsset.cs:151`), and the only path setting `assembly` before `LoadAssemblyImpl` is `GetModAssets`, which matches an already-loaded `AppDomain` assembly by file location (`:314-334`, the match at `:321`).
A mod-shipped assembly is loaded from a byte array (`:236`/`:241`) and so has an empty `Location`, leaving that match nothing to hit.
So at first initialization the order reduces to local, then highest version, then asset id — deterministic and decided by the installed set, not by mod initialization order.
`isLoaded` bites only on a mid-session re-initialization (`ModManager.cs:244`, `GameManager.cs:1628`), where a copy already in the process outranks a local one and a higher-versioned one alike; `mod-compatibility.md` carries the full derivation at its first finding.

**The versions in the wild are not all the same.** Across the user's Paradox cache, the 51 shipped `0Harmony.dll` copies carry three identities: `2.2.2.0` (48), `2.3.3.0` (2), `2.4.2.0` (1).
All three are `PublicKeyToken=null`, so nothing is strong-named and version binding is not enforced; a mod compiled against 2.2.2 will silently run against whichever copy won.
Every corpus repository that declares the dependency pins `Version="2.2.2"`, all eleven of them.

**Ruled (2026-08-06, the mod-compatibility pass).** What happens after the loader picks a winner is standard .NET rather than a question about this game: with nothing strong-named the version is no part of binding identity, so the simple name match is the whole of it. A mod built against 2.2.2 runs against whichever copy won, and the experiment this line used to ask for is not owed.

**Verdict: it is neither call-site-granular nor always loud**, established by direct experiment under Unity's own Mono during the mod-compatibility pass's review; `mod-compatibility.md` carries the derivation and the evidence at its first finding. A missing member throws while the **containing method** is JIT-compiled, before that method's first statement, so a `try`/`catch` around the call never runs — the guard has to be an isolated `[MethodImpl(MethodImplOptions.NoInlining)]` method or a reflection lookup. And a `const`, an enum member's value and an optional parameter's default are baked into the calling assembly at compile time, so a change to any of them keeps running silently with the old value.

Rots: the three version numbers and the 2.2.2 pin the corpus shares — re-count `0Harmony.dll` identities under the Paradox mods cache and re-grep the corpus csproj files.

### The idiomatic alternatives, in the order the evidence supports

The wiki gives the ordering principle (patch third, and it is "brittle on game patch days"); the corpus gives four concrete techniques, and every one of them is used by a mod that ships zero patches.

**One: insert a system.** The default, and the reason the corpus patches so little. Nothing here is patching-specific; `mod-lifecycle-and-ordering` owns it. What belongs in a patching reference is the negative: a behaviour reachable from a phase your system can occupy does not need a patch.

**Two: disable a vanilla system and register a fork in its slot.**
`updateSystem.World.GetOrCreateSystemManaged<T>().Enabled = false`, then register your own.
`PlopTheGrowables/Code/Mod.cs:74` disables `Game.Simulation.ZoneCheckSystem` and registers `SelectiveZoneCheckSystem` at `ModificationEnd` behind its own tagging systems (`:76-81`); the whole mod is twelve files and no patches.
`Traffic/Code/Mod.cs:76` disables `Game.Net.LaneSystem` and registers `TrafficLaneSystem` before it; Traffic is 128 files and no patches.
`NodeController/NodeController/Mod.cs:58` disables `GeometrySystem`.
`Time2Work/NightShift/Mod.cs:94-113` is the extreme case, disabling roughly a dozen simulation and UI systems — and it patches anyway, which is what makes it the useful contrast: substitution and patching are not alternatives at the level of a whole mod, only at the level of a behaviour.

**Three: rewrite a vanilla system's own `EntityQuery` from outside, so stock code skips your entities.**
Two repositories carry the same routine, near-verbatim, and neither uses Harmony for it: `Traffic/Code/Utils/VanillaSystemHelpers.cs:12-47` and `CS2-Platter/Platter/PlatterMod.cs:437-484`.
The shape is fixed: reflect the private query field off the target instance; guard on `originalQuery.GetHashCode() == 0`, which is how both detect a system whose `OnCreate` has not run; call `EntityQuery.GetEntityQueryDescs()`; append your own `ComponentType` to each desc's `None`, skipping descs that already contain it; then reflect `ComponentSystemBase.GetEntityQuery(params EntityQueryDesc[])` — `protected internal` at `src/Unity.Entities/Unity.Entities/ComponentSystemBase.cs:449`, hence the reflection, since a mod can call it on itself but not on another system's instance — invoke it _on the target system_ so the query is owned by the right system, write the result back into the field, and call the public `RequireForUpdate(query)` (`:321`).
Traffic appends `ModifiedLaneConnections` to `LaneSystem.m_OwnerQuery` (`src/Game/Game.Net/LaneSystem.cs:9157`, built at `:9180` and required at `:9200`).
Platter appends `ParcelOwner` to `SubBlockSystem.m_Query` (`src/Game/Game.Serialization/SubBlockSystem.cs:64`).
`survey-mods-techniques.md` §9 item 1 credits this to `Cities2-TrafficLightsEnhancement/TrafficLightsEnhancement/Utils/EntityQueryUtils.cs:21-74`, a repository no longer in the checkout; the technique is not lost with it.

**Four: cache a reflection accessor rather than patching to reach a private member.**
Four idioms are present, and they differ in how much work each access does.
`Traverse.Create(__instance).Field<T>("name").Value` does the most: it allocates a `Traverse` per call and reads through `FieldInfo.GetValue`, and its only saving is that the `FieldInfo` lookup itself is cached (`0Harmony.decompiled.cs:12290-12302`). Used at `Anarchy/Anarchy/Patches/NetToolSystem_InitializeRaycast.cs:38`, and to _write_ a vanilla system's private fields with `.SetValue` at `Time2Work/NightShift/Patches/Time2WorkPatches.cs:39-41`.
A `FieldInfo` resolved once in a static constructor and closed over by a `Func<TInstance, TField>` — `CS2-Platter/Platter/Patches/Accessors/ObjectToolSystemFieldAccessor.cs:32-50`, seven fields, with the rationale in its own doc comment: "to avoid repeated reflection lookups in hot paths"; `BulldozeToolSystemAccessor.cs:21-33` is the one-field form. Still a `GetValue` per access, but no lookup and no `Traverse`.
`AccessTools.FieldRef<TInstance, TField>` does the least: it emits a `DynamicMethod` whose whole body is `Ldarg_0; Ldflda; Ret` and returns it as a delegate yielding a `ref` to the field (`0Harmony.decompiled.cs:11979-12001`), so an access is a field load with no reflection at all. `CS2-WriteEverywhere/BelzontWE/Overrides/PrefabSystemOverrides.cs:15-17` builds three, for private `PrefabSystem` dictionaries.
And, inside a patch body only, Harmony's `___fieldName` injection, which needs no accessor at all (below).

**`AccessTools.Field` walks base types, which is why a lookup on the wrong class still works.**
`Platter` asks for `AccessTools.Field(typeof(BulldozeToolSystem), "m_ToolRaycastSystem")`; that field is declared `protected` on `ToolBaseSystem` (`src/Game/Game.Tools/ToolBaseSystem.cs:106`) and not on `BulldozeToolSystem` at all.
It resolves because `AccessTools.Field` runs `FindIncludingBaseTypes`, which climbs `type.BaseType` until a hit (`0Harmony.decompiled.cs:6869-6881`, the loop at `:6804-6817`).

### What the corpus patches, in four groups

Fifty-five distinct vanilla methods across the nine patching repositories, plus `InfoLoom`'s one unwired target. They fall into four groups, and the groups are not about subject matter — they are about **what kind of seam is missing**.

**(a) A tool's per-frame raycast and snap configuration.** The largest group, and the one `custom-tools` shares.
`InitializeRaycast` on three different tools — `BulldozeToolSystem` (`src/Game/Game.Tools/BulldozeToolSystem.cs:1457`), `NetToolSystem` (`NetToolSystem.cs:5760`), `DefaultToolSystem` (`DefaultToolSystem.cs:776`) — patched by `BetterBulldozer/BetterBulldozer/Patches/BulldozeToolSystemInitializeRaycastPatch.cs:20`, `Anarchy/Anarchy/Patches/NetToolSystem_InitializeRaycast.cs:31`, `ExtraDetailingTools/MOD/Patches/DefaultToolSystem.cs:10`, and both bulldoze and default by `CS2-Platter/Platter/Patches/MarkerPatches.cs:45-46/105-106`.
Both `GetRaycastResult` overload families on `ToolBaseSystem` (`ToolBaseSystem.cs:542/555`) and on `BulldozeToolSystem` (`BulldozeToolSystem.cs:1647`), by `BetterBulldozer/.../ToolBaseSystemGetRaycastResultPatch.cs:20` and `MarkerPatches.cs:69-71/84-86/128-130/147-149`.
The snap surface: `ToolBaseSystem.GetActualSnap(Snap, Snap, Snap)` (`ToolBaseSystem.cs:467`) at `CS2-Platter/Platter/Patches/ToolSystemPatch.cs:31-33`; the private static `GetAvailableSnapMask` overloads on `AreaToolSystem` (`AreaToolSystem.cs:3263`) and `ObjectToolSystem` (`ObjectToolSystem.cs:3570`) at `ExtraDetailingTools/MOD/Patches/AreaToolSystem.cs:10-12` and `Patches/ObjectToolSystem.cs:39-43`; `ObjectToolSystem.GetAllowRotation` (`ObjectToolSystem.cs:2877`) and the private `SnapControlPoint(JobHandle)` (`:4196`, called from six places in `OnUpdate` including `:3726`) at `ToolSystemPatch.cs:55-101`.
The reason this group exists is structural: `ToolRaycastSystem` calls `InitializeRaycast()` on the _active tool only_ (`src/Game/Game.Tools/ToolRaycastSystem.cs:90-130`), so a mod that wants to widen what a vanilla tool can hit has nowhere else to stand.

**(b) A value the game publishes to its own UI, produced by a private method behind a binding.**
`ToolUISystem.GetElevationRange` (`src/Game/Game.UI.InGame/ToolUISystem.cs:455`, bound at `:148`), `AllowBrush` (`:583`, bound at `:180`), `SetBrushStrength` (`:606`, bound at `:194`); `ToolbarUISystem.Apply` (`ToolbarUISystem.cs:795`) and `BindAssets` (`:349`); `TimeUISystem.GetDay` and `GetTicks` (`TimeUISystem.cs:163/157`, bound at `:86/85`); `CityInfoUISystem.WriteDemandFactors` (`src/Game/Game.UI.InGame/CityInfoUISystem.cs:277`, called three times at `:239/246/253`); `ActionsSection.OnProcess` and `OnDelete` (`src/Game/Game.UI.InGame/ActionsSection.cs:299/244`, the latter bound at `:153`).
Every one of them is **private**, and every one is reached only through a delegate the system captured in its own `OnCreate`.
There is no seam by construction: the binding is already registered, the system is registered as a concrete type, and the producer is not virtual.
Patched at `Anarchy/Anarchy/Patches/ToolUISystemGetElevationRangePatch.cs:17`, `Tree_Controller/Tree_Controller/Patches/ToolUISystemSetBrushStrengthPatch.cs:19` and `Patches/ToolbarUISystemApplyPatch.cs:18`, `LineTool-CS2/Code/Patches/ToolbarUISystemPatches.cs:26`, `Water_Features/Water_Features/Patches/ToolbarUISystemBindAssetsPatch.cs:17`, `ExtraDetailingTools/MOD/Patches/ToolUISystem.cs:20`, `MOD/Patches/ActionsSection.cs:16/29`, `Time2Work/NightShift/Patches/Time2WorkPatches.cs:93/101`, `InfoLoom/InfoLoom/Patches/Patches.cs:22`.

**(c) A predicate the game asks for and then acts on.**
`UniqueAssetTrackingSystem.IsPlacedUniqueAsset` (`src/Game/Game.UI.InGame/UniqueAssetTrackingSystem.cs:105`) forced to `false` at `Anarchy/.../UniqueAssetTrackingSystemIsPlacedUniqueAssetPatch.cs:22-28`; `GameModeExtensions.IsEditor` (`src/Game/Game/GameModeExtensions.cs:16`, an extension method on an enum) at `ExtraDetailingTools/MOD/Patches/GameModeExtensions.cs:11`; `ObjectToolSystem.TrySetPrefab(PrefabBase)` (`ObjectToolSystem.cs:2987`) and `GetObjectPrefab` (`:3068`) at `ToolSystemPatch.cs:206-234/255-278`.
The outlier that belongs here rather than anywhere else is **neutralising a system from inside its own constructor**: `Anarchy/.../UniqueAssetTrackingSystemOnCreatePatch.cs:20-24` postfixes `UniqueAssetTrackingSystem.OnCreate` and sets `Enabled = false` on it, where the ordinary route is `World.GetOrCreateSystemManaged<T>().Enabled = false` from `OnLoad`.

**(d) A simulation value, or the managed method that schedules a job.**
`TimeSystem` almost entirely: `OnUpdate`, both `GetYear` overloads, `get_normalizedDate`, `GetDay`, `GetCurrentDateTime`, `GetStartingDate`, `GetElapsedYears`, `GetTimeOfYear`, `GetTimeOfDay` (`src/Game/Game.Simulation/TimeSystem.cs:38/104/114/126/132/138/144/150/187`), all at `Time2Work/NightShift/Patches/Time2WorkPatches.cs:34-171`.
`ClimateSystem.SampleClimate(ClimatePrefab, float)` (`src/Game/Game.Simulation/ClimateSystem.cs:607`, against a one-argument overload at `:625`), the public static `CityServiceUpkeepSystem.CalculateUpkeep` (`src/Game/Game.Simulation/CityServiceUpkeepSystem.cs:594`), and `CityServiceBudgetSystem.OnUpdate` (`src/Game/Game.Simulation/CityServiceBudgetSystem.cs:854`) — patched at `Time2WorkPatches.cs:171/193-194/226`.
`Game.Rendering.WindControl.SetGlobalProperties(CommandBuffer, WindVolumeComponent)` — private, at `src/Game/Game.Rendering/WindControl.cs:144` and called at `:114` — at `Tree_Controller/.../WindGlobalPropertiesPatch.cs:15`.
`PrefabSystem.UpdatePrefabs` and `AssetImportPipeline.GetTextureReferenceCount` at `CS2-WriteEverywhere/BelzontWE/Overrides/PrefabSystemOverrides.cs:20-21`.
And the two job-scheduling cases, which the Burst finding below treats on their own: `ObjectToolSystem.SnapControlPoint` (`ToolSystemPatch.cs:100-101`) and `PathfindSetupSystem.FindTargets` (`Time2Work/NightShift/Patches/PathfindSetupSystem_LeisureEventBiasPatch.cs:43-54`).

**Every one of these targets was checked against the decompile and exists at 1.6.0f1**, with the signature the patch claims, including every one whose `Type[]` array turns out to be load-bearing.
Five commented-out `[HarmonyPatch]` blocks were excluded from the count: four at `ExtraDetailingTools/MOD/Patches/NetToolSystem.cs:17/30/150/185` and one at `Time2Work/NightShift/Patches/Time2WorkPatches.cs:44`.

Rots: all 55 target names and their signatures. A patch target is a claim about vanilla code and is re-checkable one grep at a time against `src/Game/`.

### Prefixes, postfixes and injected parameters, in the library's own vocabulary

Read from Harmony's own IL emitter rather than from documentation, so each rule below is the code that enforces it.

**A prefix returns `void` or `bool`, and nothing else.** `MethodPatcher.AddPrefixes` throws at patch time on any other return type: `Prefix patch {fix} has not "bool" or "void" return type` (`0Harmony.decompiled.cs:3467-3471`). Returning `false` means "do not run the original".

**A prefix returning `false` also skips later prefixes — but only the ones that could have skipped it themselves.**
Harmony wraps a prefix in `if (runOriginal)` only when `PrefixAffectsOriginal(fix)` is true (`:3440-3444`), and that predicate is true when the prefix returns `bool`, or takes any parameter that is `out`, `ref`, or a reference type — with `__instance`, `__originalMethod` and `__state` explicitly exempted (`:3400-3433`).
So a void prefix taking only value-type arguments always runs; a void prefix taking `ref Something` does not, once someone ahead of it has returned `false`.

**Postfixes are never guarded.** `AddPostfixes` emits no `runOriginal` check at all (`:3483-3521`), so a postfix runs even when a prefix suppressed the original — which is exactly what happens when `BetterBulldozer`'s prefix on `ToolBaseSystem.GetRaycastResult` returns `false` and `CS2-Platter`'s postfix on the same method still fires.

**Ordering is priority descending, then registration order.** `PatchSorter` compares through `PatchInfoSerialization.PriorityComparer(obj, index, priority)`, which sorts on `priority` descending and falls back to `index` ascending (`:4018-4021`, `:5737-5744`). `Priority` runs `Last = 0` through `First = 800` with `Normal = 400` (`:6630-6649`), and `HarmonyPriority`, `HarmonyBefore` and `HarmonyAfter` all exist (`:4800/4808/4816`).
**No corpus repository uses any of the three.** Combined with mod load order being dictionary iteration order (`ModManager.cs:180/438`), the practical position is that cross-mod patch order on a shared target is unspecified, and every corpus mod that cares handles it by making its patch order-independent instead.

**The injected parameter names are constants in `MethodPatcher`** (`0Harmony.decompiled.cs:2599-2615`): `__instance`, `__originalMethod`, `__args`, `__result`, `__state`, `__exception`, `__runOriginal`, the `__` index prefix for a positional argument, and the `___` prefix for a private field.
Corpus usage is lopsided. Occurrences across all 22 repositories: `__instance` 140, `__result` 80, `__state` 5, `___m_AgeMaskBinding` 3, and no occurrence of `__originalMethod`, `__args`, `__exception` or `__runOriginal` anywhere.

The three that carry traps:

- **`__instance` on a static original is `null`, silently.** `EmitCallParameter` emits `Ldnull` and moves on (`:3154-3158`). `Time2Work/NightShift/Patches/Time2WorkPatches.cs:95` declares `TimeSystem __instance` on a prefix for `TimeSystem.GetDay`, which is `public static` (`TimeSystem.cs:150`); the body never touches it, so it is dead weight rather than a bug. A patch that _did_ dereference it would throw at runtime, and nothing at patch time warns.
- **`__result` is type-checked at patch time**, both ways: `Cannot get result from void method` and `Cannot assign method return type ... to __result type ...` (`:3232-3245`). Taking it `ref` in a postfix is how you rewrite the return value (`Anarchy/.../UniqueAssetTrackingSystemIsPlacedUniqueAssetPatch.cs:22`, `ref bool __result`; `CS2-Platter/.../ToolSystemPatch.cs:262`, `ref ObjectPrefab __result`). Taking it `ref` in a prefix that returns `false` is how you supply one without running the original (`Anarchy/.../ToolUISystemGetElevationRangePatch.cs:24-32`).
- **`___field` resolves against the _original's_ declaring type and does walk base types**, since it goes through `AccessTools.Field` (`:3205`); it throws `No such field defined in class` when it misses. `LineTool-CS2/Code/Patches/ToolbarUISystemPatches.cs:29` takes `ValueBinding<int> ___m_AgeMaskBinding` and calls `.Update(...)` on it — the corpus's only use, and the cheapest way to reach a private field from inside a patch, because there is no accessor to build.

**`__state` is keyed by the patch class**, not by the patched method: the variable is looked up under `patch.DeclaringType.AssemblyQualifiedName` (`:3223`), so a prefix and a postfix share state only when they live in the same class. `CS2-WriteEverywhere/BelzontWE/Overrides/PrefabSystemOverrides.cs:23-38` is the corpus's only use, packing two booleans into an `int` in `BeforeUpdatePrefab` and reading them in `AfterUpdatePrefabs`.

**What the corpus does not use at all, in 22 repositories.** Zero transpilers — no `IEnumerable<CodeInstruction>`, no `CodeMatcher`, no `[HarmonyTranspiler]`. Zero finalizers. Zero `[HarmonyReversePatch]`. Zero `MethodType.Getter`/`Setter` (`Time2Work` reaches a property getter by its metadata name instead, `[HarmonyPatch(typeof(TimeSystem), "get_normalizedDate")]` at `Time2WorkPatches.cs:83`). Three uses of `TargetMethod()` (`ExtraDetailingTools/MOD/Patches/TypePickerPanelPatch.cs:19/39`, `Time2Work/NightShift/Patches/PathfindSetupSystem_LeisureEventBiasPatch.cs:43`).

**Two prefix shapes, and the second is the one that composes.**
Skip the original and supply the answer: return `false`, having written `ref __result`.
Rewrite an argument and let the original run: take the parameter `ref`, mutate it, return `true`. `CS2-Platter/Platter/Patches/ToolSystemPatch.cs:212-224` and `:238-250` are two independent prefixes on the same `ObjectToolSystem.TrySetPrefab(PrefabBase)`, each type-testing `prefab` and returning `true` either way — so both run, in any order, and neither can suppress the other.

### Disambiguating an overload, including `out` and `ref`

`[HarmonyPatch]` takes a parallel `Type[]` and `ArgumentType[]`, and `ParseSpecialArguments` turns the pair into a signature (`0Harmony.decompiled.cs:4657-4685`).

**`ArgumentType.Ref` and `ArgumentType.Out` are the same case in that switch, both `type.MakeByRefType()`** (`:4674-4677`); `ArgumentType.Pointer` is `MakePointerType()`; `Normal` leaves the type alone.
So the two spellings are interchangeable, which is why the corpus's two mods patching the _same_ method with opposite spellings both work: `BetterBulldozer/.../ToolBaseSystemGetRaycastResultPatch.cs:20` writes `ArgumentType.Out, ArgumentType.Out` for `ToolBaseSystem.GetRaycastResult(out Entity, out RaycastHit)`, and `CS2-Platter/Platter/Patches/MarkerPatches.cs:130` writes `ArgumentType.Ref, ArgumentType.Ref` for the same method.
It also means `in` is covered: `in T` is a by-ref parameter in metadata, and `Time2Work` disambiguates one by hand with `typeof(PathfindSetupSystem.SetupData).MakeByRefType()` (`PathfindSetupSystem_LeisureEventBiasPatch.cs:52`), then receives it as `ref` in the patch body (`:59`).

**Where disambiguation is genuinely required, verified against the decompile:**
`AreaToolSystem.GetAvailableSnapMask` has two — the public override `(out Snap, out Snap)` at `src/Game/Game.Tools/AreaToolSystem.cs:3251` and the private static four-parameter one at `:3263` carrying the logic. `ExtraDetailingTools/MOD/Patches/AreaToolSystem.cs:10-12` targets the second.
`ObjectToolSystem.GetAvailableSnapMask` likewise, the seven-parameter private static at `ObjectToolSystem.cs:3570` against the override at `:3554`; `ExtraDetailingTools/MOD/Patches/ObjectToolSystem.cs:39-43` matches it exactly.
`ToolBaseSystem.GetActualSnap` has a public static three-parameter form at `ToolBaseSystem.cs:467` and a protected no-arg one at `:472`; `CS2-Platter/.../ToolSystemPatch.cs:33` names the first.
`TimeSystem.GetTimeOfDay` and `GetTimeOfYear` each have a public overload taking `double renderingFrame` and a protected one without it (`TimeSystem.cs:104/109` and `:114/120`); `GetYear`'s two overloads are both public (`:138/144`) and split on the same parameter. `Time2Work` disambiguates all four with explicit `Type[]` arrays (`Time2WorkPatches.cs:59/71/151/161`), and patches both `GetYear` overloads rather than choosing one.

**Two spellings of the attribute do the same job.** Stacked, one facet per attribute — `[HarmonyPatch(typeof(X))] [HarmonyPatch("Y")] [HarmonyPatch(new Type[]{...}, new ArgumentType[]{...})]` (`CS2-Platter` throughout, `MarkerPatches.cs:69-71`) — or folded into one call (`BetterBulldozer/.../ToolBaseSystemGetRaycastResultPatch.cs:20`, `ExtraDetailingTools/MOD/Patches/AreaToolSystem.cs:10-12`). Both reach the same `HarmonyMethod` info.

**`nameof` works on a private target when a public member shares the name.** `CS2-Platter/.../ToolSystemPatch.cs:32` writes `nameof(ToolBaseSystem.GetActualSnap)` and lands on the static overload; the compiler resolves the token against the accessible overload while Harmony resolves the method against the `Type[]`.

**A prefix mirroring an `out` parameter must assign it even when it returns `true`.** C# forces the assignment, and the original then overwrites it. `BetterBulldozer/.../ToolBaseSystemGetRaycastResultPatch.cs:31-46` writes `entity = Entity.Null; hit = default; return true;` in each of its early-out branches for exactly that reason.

Rots: every overload set named here. Re-check by grepping the named type for the method name.

### A Burst-compiled job is not what you patch; the managed method that schedules it is

**The decompile shows the mechanism directly for a Burst-compiled _method_.**
`ClimatePrefab.SimplexNoise(float, float)` carries `[BurstCompile]`, and its entire body is `return SimplexNoise_00009354_$BurstDirectCall.Invoke(x, y);` (`src/Game/Game.Prefabs.Climate/ClimatePrefab.cs:1105-1109`).
That generated `Invoke` checks `BurstCompiler.IsEnabled`, fetches a native function pointer through `BurstCompiler.GetILPPMethodFunctionPointer2`, and calls it as `delegate* unmanaged[Cdecl]<float, float, float>`; only if Burst is off does it fall through to `SimplexNoise_$BurstManaged`, which holds the real body (`:543-553`, the pointer machinery at `:505-541`).
So a prefix or postfix on `SimplexNoise` still runs — it is an ordinary managed method — but it wraps a two-line trampoline. **You can read and rewrite the arguments and the result; you cannot change the logic**, because the logic is in the native image and in `SimplexNoise_$BurstManaged`, which the native path never calls.
Five methods in `Game.dll` use this form, registered at `src/Game/-BurstDirectCallInitializer.cs:8-16`.

**For a Burst-compiled _job_ the substitution point is not readable in C# at all.**
A job's entry is `JobChunkExtensions.JobChunkProducer<T>.Execute`, registered once through `JobsUtility.CreateJobReflectionData(typeof(JobChunkWrapper<T>), typeof(T), new ExecuteJobFunction(Execute))` (`src/Unity.Entities/Unity.Entities/JobChunkExtensions.cs:38-43`), and `ScheduleInternal` passes that pointer straight to `JobsUtility.Schedule`/`ScheduleParallelFor` (`:166-194`).
`CreateJobReflectionData` is `extern`, so the point at which Burst swaps the compiled body in is native.
The game registers 1,389 jobs this way (`src/Game/__JobReflectionRegistrationOutput__17016606566994089001.cs`, one `EarlyJobInit` call each), and mod jobs are compiled ahead of time into a separate native library the loader mounts at mod-load time — `BurstRuntime.LoadAdditionalLibrary(<mod>_win_x86_64.dll)` at `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ExecutableAsset.cs:255`, logged as `Loaded additional Burst code`, which the running game's `Modding.log` shows for every mod that ships one.

Unconfirmed: that a Harmony patch on a `[BurstCompile]` job's `Execute` has no effect while Burst is enabled. The direct-call case above is proved from decompiled C#; the job case rests on the same architecture with its final step in native code, and no corpus mod tries it, so there is no negative observation either. The experiment the running game settles: patch the `Execute` of one vanilla `[BurstCompile] IJobChunk` with a postfix that logs, and observe whether it fires, then relaunch with Burst disabled and observe again.

**What the corpus does instead is patch one level up, and it works.**
`Time2Work/NightShift/Patches/PathfindSetupSystem_LeisureEventBiasPatch.cs` is the clean case and the whole file is worth reading as one.
Its `TargetMethod()` resolves `PathfindSetupSystem.FindTargets(SetupTargetType, in SetupData)` (`:43-54`), which the decompile confirms as `private JobHandle FindTargets(SetupTargetType targetType, in SetupData setupData)` at `src/Game/Game.Simulation/PathfindSetupSystem.cs:454` — a plain managed `switch` over 40-odd target kinds, called at `:447`.
The prefix returns `true` immediately for every kind but `Leisure` (`:62-63`), and for `Leisure` schedules its own `[BurstCompile] IJobChunk` (`:135-243`), assigns the handle to `ref JobHandle __result`, and returns `false` (`:107-132`).
The vanilla branch it displaces is one `case` line — `PathfindSetupSystem.cs:532-533`, dispatching to `CitizenPathfindSetup.SetupLeisureTarget` (`src/Game/Game.Simulation/CitizenPathfindSetup.cs:961`), which schedules `SetupLeisureTargetJob` at `:972`. That job is `[BurstCompile]`, `private` and nested (`:104-105`), and nothing patches it.
**So the rule is: replace the job, not its body.** The patch never touches a Burst-compiled method; it substitutes a different one at the last managed instruction before the schedule.

Three details in that file generalise.
The prefix rebuilds every handle and lookup by hand off `__instance` (`:87-104`) because a `ComponentTypeHandle` is per-system state the vanilla method would have refreshed.
It reads `SystemBase.Dependency` — `protected`, therefore unreachable from a static patch method in another assembly — through a `MethodInfo` cached in a static field (`:40-41`, invoked at `:66`), which is the accessor-caching alternative used _inside_ a patch.
And it returns the scheduled handle rather than completing it, so the caller's `TempJob` allocation lifetime stays correct, which the file's own header comment calls out.

`CS2-Platter/Platter/Patches/ToolSystemPatch.cs:100-178` is the second instance of the same shape, a prefix on `ObjectToolSystem.SnapControlPoint(JobHandle) : JobHandle` that pulls the zone, net, terrain and water systems and the control-point list out of `__instance` through its cached accessors, schedules its own job, registers the search-tree readers, completes, assigns `__result` and returns `false`.

### Patch lifecycle: when patches apply, and why unpatching almost never matters

**Application.** Every corpus mod patches from `IMod.OnLoad`, and `OnLoad` is the last thing `ModInfo.Load` does before marking the mod loaded (`src/Game/Game.Modding/ModManager.cs:117-124`, dispatching to each `IMod` at `:152-158`).
Two things happen before it and both matter.
`FormatterServices.GetUninitializedObject(item)` constructs the `IMod` **without running any constructor** (`:121`), so field initialisers on the mod class do not run and a `Harmony` instance cannot be created there.
And `LoadAssemblyImpl` loads every referenced assembly first (`ExecutableAsset.cs:228-231`), so `0Harmony` is in the process before the mod's own type is touched.
A mod whose Harmony reference cannot be resolved never reaches `OnLoad` at all: `canBeLoaded` is false, and the mod's state becomes `MissedDependenciesError` with the unresolved names listed (`ModManager.cs:109-116`, `ExecutableAsset.cs:175`).

**`PatchAll()` with no argument reads the _calling frame's_ assembly** — `new StackTrace().GetFrame(1).GetMethod().ReflectedType.Assembly` (`0Harmony.decompiled.cs:5153-5157`).
That is fragile in a way `PatchAll(Assembly)` is not, and the corpus is split: `Anarchy`, `BetterBulldozer`, `Tree_Controller`, `Water_Features` and `LineTool-CS2` use the parameterless form; `CS2-Platter/Platter/PlatterMod.cs:341`, `Time2Work/NightShift/Mod.cs:166` and `ExtraDetailingTools/EDT.cs:112` pass `typeof(X).Assembly` explicitly.
`LineTool` is the case that shows why it matters in principle: its `PatchAll()` sits in a helper class (`LineTool-CS2/Code/Patches/Patcher.cs:96`) rather than in the mod type, and works only because that helper is in the same assembly.

**Imperative patching is available and used once.** `ExtraDetailingTools/MOD/ExtraSnap/ExtraSnap.cs:100-118` resolves `SnapControlPoint` and `InitializeRaycast` off an open generic type parameter `TTool` with `BindingFlags.Instance | Public | NonPublic`, throws `MissingMethodException` when either is absent (`:101-108`), and applies each with `_harmony.Patch(methodInfo, postfix: new HarmonyMethod(...))` (`:110-118`).
That is the only way to write one patch body that serves several tool types, since an attribute cannot name a generic parameter.

**Removal.** `UnpatchAll(harmonyID)` walks every patched method and removes only patches whose owner matches (`0Harmony.decompiled.cs:5197-5220`).
Every patching corpus mod calls it from `IMod.OnDispose`.
**`OnDispose` runs in exactly two situations**, and neither is the one authors seem to expect.
It runs at process shutdown, from `ModManager.Dispose()` called by `GameManager` on destroy (`GameManager.cs:792`, `ModManager.cs:495-522`), where the AppDomain is going away regardless.
And it runs per-mod when that mod's own load throws: `InitializeMods` catches, calls `modInfo.Dispose()`, and logs (`ModManager.cs:450-455`) — which is the case where unpatching genuinely buys something, because a mod that patched and then failed halfway through `OnLoad` would otherwise leave live patches behind a mod that is not there.
It does **not** run when a code mod is disabled mid-session: that path calls `RequireRestart()` and leaves the mod loaded (`GameManager.cs:1567-1573`), and re-initialisation skips any mod already in a non-`Unknown` state (`ModManager.cs:95-98`), so a mod is never unloaded and re-patched inside one run.

**One published mod's unpatch is a no-op, and it is instructive rather than incidental.**
`LineTool-CS2/Code/Patches/Patcher.cs:73` calls `harmonyInstance.UnpatchAll("_harmonyID")` — the literal string, not the field, which is `_harmonyID` and holds the real id.
Since `UnpatchAll` filters on owner, nothing is removed. The same file gets it right ten lines later (`:105`).
The path is only reachable from the stale-singleton guard at `:32-36`, so the bug has never had a consequence; it is worth recording because it is the exact shape of mistake an unpatch call invites, and nothing at runtime reports it.

Rots: the `OnDispose` trigger set — re-check `src/Game/Game.SceneFlow/GameManager.cs:792/1567-1573` and `src/Game/Game.Modding/ModManager.cs:95-98/450-455`.

### The composition discipline, and what recording that you set the flag actually buys

Three mods postfix an `InitializeRaycast` to widen what a vanilla tool can hit, and the first half of the discipline is which operator they use.
`Anarchy/Anarchy/Patches/NetToolSystem_InitializeRaycast.cs:47` ors: `toolRaycastSystem.netLayerMask |= Layer.Pathway | ...`.
`CS2-Platter/Platter/Patches/MarkerPatches.cs:57` and `:117` or too.
`BetterBulldozer/BetterBulldozer/Patches/BulldozeToolSystemInitializeRaycastPatch.cs` does both in one method — `raycastFlags = RaycastFlags.Markers` at `:43` and `= RaycastFlags.Markers | RaycastFlags.Decals` at `:49`, discarding whatever anyone else set that frame, against `raycastFlags |= ...` at `:62`.
So the choice is per branch rather than per mod, and an assignment in any branch is what costs another patch its flags, since `ToolBaseSystem.InitializeRaycast` has already cleared the field that frame (`src/Game/Game.Tools/ToolBaseSystem.cs:432-434`) and every widening postfix is competing to put its own bits back.

**The second half is the one worth teaching, and one mod in 22 does it.**
`MarkerPatches` reads the flag before touching it, sets it only if it was clear, and records in a `[ThreadStatic] bool` whether _it_ was the one that set it (`:33-37`, `:54-61`, once per tool it patches).
Its result filter then does nothing unless that flag is true (`FilterMarkerRaycastResult`, `:170-193` and `:199-218`, both guarded by `if (!result || !weAddedMarkersFlag) return;`).
The class's own doc comment states the reason: "Should markers be already enabled, no filtering is done, this should ensure compatibility with other mods or base game changes" (`:25-29`).

**What it buys, precisely.** A patch that widens a filter and then narrows the results is two halves of one transaction, and the narrowing half is only correct for hits the widening half caused.
If the game already had markers on — because the player enabled marker visibility, or because another mod turned them on for its own purposes — a result filter that runs unconditionally will veto hits that had nothing to do with this mod, and the symptom lands in the _other_ party.
Recording the flag is what makes the patch idempotent with respect to everyone else: it either owns the widening and owns the filtering, or it owns neither.
It also survives a vanilla change: if a future build sets `RaycastFlags.Markers` itself for that tool, the patch degrades to a no-op instead of breaking the tool.

The `[ThreadStatic]` itself is belt-and-braces rather than a requirement at 1.6.0f1. Both ends run on the main thread: `InitializeRaycast` is called by `ToolRaycastSystem.OnUpdate` on the active tool (`src/Game/Game.Tools/ToolRaycastSystem.cs:90-130`) and `GetRaycastResult` from the tool's own managed update. What the pairing does need is that the set and the read happen in the same frame on the same call chain, which they do.

**A second composition property falls out of the Harmony emitter and is worth stating beside it.** Two mods patch `ToolBaseSystem.GetRaycastResult` — `BetterBulldozer` with a prefix that can return `false`, `CS2-Platter` with a postfix. Because postfixes are unguarded (`0Harmony.decompiled.cs:3483-3521`), Platter's filter runs even on frames where BetterBulldozer suppressed the original, and its own `weAddedMarkersFlag` guard is what makes that harmless. Neither mod knows about the other.

`survey-mods-techniques.md:476` already ranks this the corpus's most mod-compatible patch; the mechanism above is why, re-derived rather than restated.

### Catalog gap: Platter's entry does not name the flag-ownership discipline

`plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md:114-128` (the **Platter** entry) names the vanilla-query rewrite, the prefab machinery, the serializable component and six other things, and never names the technique above — which is both the corpus's only worked example of a patch designed to compose with an identical patch from another mod, and the answer this reference's own boundary asks for.

Sentence to add to that entry's **Demonstrates**: _A patch that records whether it was the one that widened a filter, and narrows the results only in that case, so it composes with another mod doing the same thing and degrades to a no-op if the game starts doing it too._
Source lines behind it: `CS2-Platter/Platter/Patches/MarkerPatches.cs:25-29` (the rationale comment), `:33-37` (the two flags), `:48-62` and `:107-122` (the set), `:170-218` (the guarded filter).

### Catalog gap: Realistic Trips' entry does not name the job-substitution patch

`mod-catalog.md:164-177` (the **Realistic Trips** entry) names substitution at scale, the time model, the update-offset override and Burst-compiled per-citizen work. It does not name the one thing in that repository nothing else in the corpus does: replacing a Burst-compiled vanilla job by patching the managed method that schedules it.

Sentence to add to its **Demonstrates**: _Replacing one branch of a vanilla job-scheduling method from a prefix — resolving a private target by explicit signature, rebuilding the type handles and lookups the vanilla method would have refreshed, reading a protected base property through a cached reflection accessor, and returning the substitute job's handle rather than completing it, so the caller's temporary allocations still outlive the work._
Source lines behind it: `Time2Work/NightShift/Patches/PathfindSetupSystem_LeisureEventBiasPatch.cs:40-41`, `:43-54`, `:56-63`, `:87-104`, `:107-132`, `:135-243`.

### Catalog correction: Advanced Line Tool applies one patch, not two

`mod-catalog.md:46` states "Its two runtime patches are small and targeted, applied only to refresh a private UI binding."
At the commit in the checkout the repository contains exactly one patch: a postfix on `ToolbarUISystem.Apply` (`LineTool-CS2/Code/Patches/ToolbarUISystemPatches.cs:26-41`).
The likely second thing counted is a reflection access rather than a patch — `AccessTools.Field(typeof(ToolbarUISystem), "m_AgeMaskBinding")` at `LineTool-CS2/Code/Systems/LineToolUISystem.cs:123` — which reaches the same private field the patch reaches through `___m_AgeMaskBinding`.
Suggested replacement: _Its single runtime patch is small and targeted, applied only to refresh a private UI binding, and the same binding is read elsewhere through a cached reflection accessor rather than a second patch._

### Catalog note: Info Loom's patch cannot be seen in a default-branch clone

`mod-catalog.md:261` (the **Info Loom** entry) certifies "Replacing the game's own JSON binding output by returning false from a prefix, when the vanilla writer truncates what the panel needs."
The prefix is there and is exactly that (`InfoLoom/InfoLoom/Patches/Patches.cs:22-42`, `return false; // don't execute the original`), so the certification is sound about the technique.
What a reader cannot see in the checkout is the half that makes it apply: nothing in the repository creates a `Harmony` instance, and the project's shared `Common\ModsCommon.props` (`InfoLoom/InfoLoom/InfoLoomTwo.csproj:18`) — which carries its package references and, per the same file's comment, its `<Reference>` list — is absent from a default-branch clone.
The catalog's own convention covers this: "An entry carrying a plain note under its `Source:` line needs more than a default-branch clone, and that note says what" (`mod-catalog.md:13`).
Note to add under that entry's `Source:` line: _The shared `Common/` build directory the project imports is not in the default branch, so package references and the Harmony bootstrap are not readable from a plain clone; the patch body is._

The same shape applies to **Write Everywhere**, whose entry does not name patching at all: its two patches go through a `Redirector` base in the closed-source `Belzont.Utils` framework, already recorded as absent from the checkout at `mod-lifecycle-and-ordering.md:458` and `custom-tools.md:29`. No catalog change is proposed there — the entry does not claim the technique, so nothing over-certifies.

### Source-list gap: nothing on the list is authoritative for Harmony

`docs/SOURCES.md` names ten kinds of source and none of them owns the one library this topic's reference is licensed to teach.
The decompiled game is authoritative for what a patch target says and blind to what a patch does; the wiki mentions Harmony twice in passing; the corpus is evidence of practice.
Every semantic claim in the "prefixes, postfixes and injected parameters" and "disambiguating an overload" findings above rests on decompiling `0Harmony.dll` itself, which is a source no entry covers.

Suggested entry, as a new numbered source or as a sub-entry under 7 (the toolchain), since the library arrives with the mod project rather than with the game:

> **The Harmony library.** Ships with no first-party artifact: not in `Cities2_Data/Managed/`, and unmentioned by the toolchain's `Mod.props`/`Mod.targets`. Each mod that patches brings its own `0Harmony.dll` through the `Lib.Harmony` package and deploys it beside its assembly. Authoritative for patch semantics — injected parameter names, prefix and postfix ordering, the `ArgumentType` mapping, unpatch filtering — none of which any other source on this list can answer. Read it by decompiling a copy: `ilspycmd` over any `0Harmony.dll` under `%CSII_USERDATAPATH%/.cache/Mods/pdx_mods/*/`, checking the assembly identity first, since three versions are in circulation and they are not strong-named.

### Source-list gap: the Paradox mods cache is not a source, and is a census

`docs/SOURCES.md`'s "What looks like a source and is not" section rules out "The user's installed mods — the built assemblies under `%CSII_LOCALMODSPATH%` and the Paradox cache" on the ground that they are "Compiled, not source, and whoever wrote one may not be in the corpus at all."
Both halves are right about _behaviour_ and both are the reason the cache is valuable for _prevalence_: it is a few hundred mods nobody selected for this pipeline, and which files a mod ships is readable without decompiling anything.
This pass used it for the only unbiased "how common is this technique" number the pipeline can produce (33 of 147 code mods ship the patching runtime), and the same shape answers other topics — which mods ship a `_win_x86_64.dll` and therefore Burst-compile, which ship a `.mjs` UI module.

Suggested amendment to that bullet, keeping the exclusion and bounding it: _Compiled, not source, and whoever wrote one may not be in the corpus at all — so it settles nothing about how a mod works. What it does settle is how common something is: the set of files a mod ships is readable without decompiling anything, and the cache is the only sample of the ecosystem this pipeline can reach that nobody selected. Use it for prevalence and never for mechanism, and say how many mods the count was over._

---

## Bridge

`custom-tools` — the largest patch group by far is a tool's raycast and snap configuration, and that reference already owns the mask enums, the `InitializeRaycast` contract and the `GetRaycastResult` pair. The patching reference should state _why_ those methods are the ones patched (`ToolRaycastSystem` calls `InitializeRaycast` on the active tool only, `src/Game/Game.Tools/ToolRaycastSystem.cs:90-130`) and hand the mask material over. `custom-tools.md:216-218` already records the three-route raycast material including Platter's flag-ownership pattern, so the two references must not both teach it: patching owns the discipline as a general rule, `custom-tools` owns the raycast instance.

`prefabs-and-assets` — `PrefabSystem.UpdatePrefabs` is a patch target (`CS2-WriteEverywhere/BelzontWE/Overrides/PrefabSystemOverrides.cs:20`), and the wiki's own three-remedy ordering (`Prefab - Quick Start`) is a prefab question answered with a patch as its third option. The alternative that reference owns — re-running the vanilla job the player action triggers — is remedy two, and is the thing a patching reference should send readers to before they reach for remedy three.

`mod-compatibility` — three findings here are compatibility facts before they are patching facts: exactly one `0Harmony` loads per process and which one is not deterministic; cross-mod patch order on a shared target is unspecified because Harmony sorts by priority-then-registration and mod load order is dictionary iteration order; and the flag-ownership discipline is what makes two mods patching the same surface survive each other. Whichever reference states them, they must be stated once.

`cs2-mod-project` — owns the `Lib.Harmony` package id and the pinned version, and now also owes the consequence measured here: the reference should not repeat the id or the pin, and should point at that reference. Two corpus repositories carry the dependency unused and ship the DLL for nothing, because the toolchain deploys the whole output directory (`%CSII_TOOLPATH%/Mod.targets:106-113`).

`mod-lifecycle-and-ordering` — the boundary partner. Patching's whole first finding is "do this instead", and every "instead" is that reference's material: `IMod.OnLoad`, phase registration, disabling a vanilla system, forking one. Two facts discovered here belong to it rather than here: mods are instantiated with `FormatterServices.GetUninitializedObject` so no constructor runs (`src/Game/Game.Modding/ModManager.cs:121`), and `OnDispose` fires only at process shutdown and on a failed load, never when a mod is disabled mid-session.

`ecs-in-this-game` — owns the job interfaces and the Burst story. The patching reference needs one sentence of it (a `[BurstCompile]` job's body is not reachable from a managed patch) and should not re-teach jobs; conversely, the query-rewrite alternative is `EntityQuery` surgery and that reference owns `EntityQueryDesc`.

`performance-and-memory` — the accessor-cost ladder (`Traverse` per access, cached `FieldInfo` closure, `AccessTools.FieldRef`) is a performance fact that a patching reference states because that is where the choice is made, and which that reference should agree with rather than duplicate.

`diagnostics` — the game's Harmony inventory never prints, so the only way to see what is patched is `harmony.GetPatchedMethods()` logged by the mod itself (`ExtraDetailingTools/EDT.cs:113-118`). That is a diagnostics habit shipped from a patching reference.

---

## Dead ends

**The game's `Modding.log` as evidence about patching.** Opened, and it holds the `Modding runtime: Builtin` line and the full mod-load table, but zero `Patched Method:` lines — because the inventory runs before mods load. It remains useful for confirming which mods loaded and which shipped Burst code, and useless for confirming a patch took.

**The live game as a way to observe patch behaviour on this pass.** `find_types` for `HarmonyLib.Harmony` returned nothing: none of the nine enabled mods patches. Every live question in this file therefore records its experiment rather than its result. Enabling a Harmony mod requires a restart, which is a bigger ask than the questions warranted; two of them (cross-version binding, patching a Burst job's `Execute`) are worth the restart if a later pass has one to spend.

**`docs/research/conflicts.md`.** Read in full. No entry, ruled or open, names patching, Harmony, or any of this topic's surfaces. Nothing was appended: every disagreement this pass found was settled by a first-party source, and the four catalog findings and two source-list findings are corrections rather than judgements — each names a file, a line and a replacement sentence, with nothing left for a maintainer to decide beyond accepting it.

**Transpilers, finalizers and reverse patches in the corpus.** Swept for `IEnumerable<CodeInstruction>`, `CodeInstruction`, `CodeMatcher`, `ILGenerator`, `[HarmonyTranspiler]`, `[HarmonyFinalizer]`, `[HarmonyReversePatch]` across all 22 repositories. Zero hits for all of them as patch mechanisms; the four `ILGenerator` hits are `CS2-WriteEverywhere/BelzontWE/Utils/WEFormulaeHelper.cs` emitting a `DynamicMethod` for its own formula language, which is not patching. The absence is a finding, recorded above, not a gap in the sweep.

**Harmony priority attributes in the corpus.** Swept for `HarmonyPriority`, `HarmonyBefore`, `HarmonyAfter`. Zero hits. The attributes exist in 2.2.2 (`0Harmony.decompiled.cs:4800/4808/4816`), so this is a fact about practice.

**The toolchain as a source on Harmony.** `%CSII_TOOLPATH%/Mod.props` and `Mod.targets` were read in full and mention Harmony nowhere; the twelve Roslyn analyzers they wire are all Entities source generators. `Cities2_Data/Managed/` was listed and holds no `0Harmony.dll`. Both negatives are load-bearing (the library is entirely the mod's own) rather than empty.

**`ArgumentType.Ref` versus `ArgumentType.Out` as a possible corpus bug.** Two mods spell the same method's `out` parameters differently, which looked like one of them being wrong. Harmony's own `ParseSpecialArguments` collapses both to `MakeByRefType()`, so there is no bug and no verdict to write beyond the fact itself.

**A shared `IMod` base or framework as the source of `InfoLoom`'s missing Harmony wiring.** Grepped `InfoLoom/` for `PatchAll`, `new Harmony` and any Harmony reference outside the one patch file; nothing. The imported `Common\ModsCommon.props` directory is simply absent from the checkout, so the question is unanswerable from the corpus rather than answerable and negative. Re-cloning that repository with its submodules would settle it.

## Re-sweep 2026-08-26: Unity's documentation (ticket 38)

**This file was named in neither of ticket 38's lists** — not in its tiers, not in its out-of-scope list — and was swept under the ticket's own rule that a file scoped out which turns out to assert engine behaviour is a finding. The verdict on the widening is that it should have been tier 1: the Burst-and-jobs section and the `EntityQuery` rewrite recipe are engine mechanism end to end. Harmony semantics are `SOURCES.md` entry 12's and were left alone throughout.

Unity docs fetched live 2026-08-26 at the version-pinned URLs entry 13 fixes, Burst at `@1.8`; decompile read the same day at 1.6.0f1; Unity package sources (entry 15) read at `com.unity.burst@1.8.23` and `com.unity.entities@1.3.10`. No live game was used, and the mod corpus was not opened.

- **The Burst trampoline has two fall-through conditions, and the shipped sentence named one with an "only".** The generated `Invoke` is carried verbatim as a comment in the post-processor source (`com.unity.burst@1.8.23/Unity.Burst.CodeGen/ILPostProcessing.cs:770-777`) and emitted intact in this build: `if (BurstCompiler.IsEnabled) { var funcPtr = GetFunctionPointer(); if (funcPtr != null) return funcPtr(...); } return OriginalMethod(...);` — see `src/Game/Game.Rendering/WaterRenderSystem.cs:117-128`, the managed body reachable at `:661`. **The null-pointer branch is a second, independent route into the managed body**, and [Burst's build page](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/building-projects.html) supplies why a pointer can be null in a shipped build: the runtime loads the generated library on first invocation, and an invalid or unsupported target builds without Burst rather than failing. The word carried weight because it is what tells a patcher their logic change can never take effect. Reach, reported: every `[BurstCompile]` static method in `src/Game` did get a trampoline — five registered in `src/Game/-BurstDirectCallInitializer.cs`, ten blocks total counting their `_0024BurstManaged` twins — so the generalisation holds over a population of five.
- **"A patch on that `Execute` never runs while Burst is enabled" is exactly true, and the reader could not tell which jobs it covers.** The `extern` half is confirmed: `JobChunkExtensions.EarlyJobInit<T>()` registers through `JobsUtility.CreateJobReflectionData` (`src/Unity.Entities/Unity.Entities/JobChunkExtensions.cs:38-44/96-99`), which is `[MethodImpl(InternalCall)] private static extern` (`src/UnityEngine.CoreModule/Unity.Jobs.LowLevel.Unsafe/JobsUtility.cs:154`), and the game's registration table is `src/Game/__JobReflectionRegistrationOutput__17016606566994089001.cs`. The sentence already carries its own precondition, and the decompile shows that precondition is a real flippable switch rather than a hedge (`src/Unity.Burst/Unity.Burst/BurstCompilerOptions.cs:681-707`, `:264-287`) — which `performance-and-memory` owns and states correctly, so nothing was restated. What shipped is a bridge plus a check: **not every job struct in `src/Game` carries `[BurstCompile]`**, the exceptions being mostly `Game.Debug` gizmo jobs but including three in ordinary territory (`src/Game/Game.Tools/DefaultToolSystem.cs:378`, `src/Game/Game.Simulation/CityServiceBudgetSystem.cs:28`, `src/Game/Game.UI.InGame/WealthInfoviewUISystem.cs:34`). **The discovery pass's two counts did not ship.** A reader acts on *check the attribute*, and a roster swept across an assembly is a search result rather than a declared set; the shape shipped and the figures stayed here.
- **`SystemBase.Dependency` has a public route, so the reflection the bullet singled out "above all" is avoidable.** The protected property owns no storage — it is `get => CheckedState()->Dependency; set => …` (`src/Unity.Entities/Unity.Entities/SystemBase.cs:13-22`) — over a `SystemState.Dependency` that is public (`src/Unity.Entities/Unity.Entities/SystemState.cs:192`), reachable through a public `SystemHandle` (`src/Unity.Entities/Unity.Entities/ComponentSystemBase.cs:59-67`), a public `World.Unmanaged` (`src/Unity.Entities/Unity.Entities/World.cs:82`) and a public `ResolveSystemStateRef` (`src/Unity.Entities/Unity.Entities/WorldUnmanaged.cs:176-178`). The general rule survives and only its worked example moved. **Where the docs are wrong about the precondition, and it did not ship:** [the `ResolveSystemStateRef` page](https://docs.unity3d.com/Packages/com.unity.entities@1.3/api/Unity.Entities.WorldUnmanaged.ResolveSystemStateRef.html) documents an `InvalidOperationException` for an invalid handle, but `ResolveSystemStateChecked` (`src/Unity.Entities/Unity.Entities/WorldUnmanagedImpl.cs:632-645`) throws only on a wrong-world handle or an out-of-bounds id and returns **null** for a destroyed system, which `ResolveSystemStateRef` dereferences unchecked — the same file's `:712-715` carries the null check this path omits. Inside a patch on that system's own method the system is live by construction, which is the only case the shipped bullet is about, so the clause said "valid for as long as the system is alive" and claimed nothing more.

**Superseded in place (review gate, 2026-08-26): the shipped bullet no longer names `ResolveSystemStateRef` at all.** A certifying pass found the four-hop route unnecessary: `SystemBase.CheckedStateRef` is `public unsafe ref SystemState` (`src/Unity.Entities/Unity.Entities/SystemBase.cs:25`) on the `__instance` a patch already holds, reaching the same `SystemState` in one hop and through the same public surface. The game's own code takes the short route throughout. The null-dereference caveat derived above therefore no longer attaches to anything shipped — it stays here as the reason the long route was not worth teaching, not as a caveat on the short one.
- **Appending an enableable marker to `None` is the wrong category, and nothing objects.** [The `EntityQueryDesc` reference](https://docs.unity3d.com/Packages/com.unity.entities@1.3/api/Unity.Entities.EntityQueryDesc.html) distinguishes the two: `None` matches archetypes carrying the type "but only for entities with these components disabled", while `Absent` excludes the archetype. Confirmed here at one line — `TestMatchingArchetypeExcludedComponent(…, query->None, …, ignoreEnableableTypes: true)` against the same call for `Absent` with `false` (`src/Unity.Entities/Unity.Entities/EntityQueryManager.cs:917-919`), the enableable members of `None` being routed into the per-entity mask machinery instead. Silently, because `EntityQueryDesc.Validate` is `[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]` (`src/Unity.Entities/Unity.Entities/EntityQueryDesc.cs:39-41`). **The general teaching was missing from the file that owns it** and was added under this ticket: `ecs-in-this-game` stated the enableable rule for `All` and not for `None`.
- **The rest of the query-rewrite recipe was executed rather than read, and it composes.** The round trip through `GetEntityQueryDescs()` rebuilds all six category arrays plus `Options` (`src/Unity.Entities/Unity.Entities/EntityQueryImpl.cs:1005-1077`) and `ConvertToEntityQueryBuilder` replays every one (`EntityQueryManager.cs:477-509`). Step 4's target is `protected internal` at `src/Unity.Entities/Unity.Entities/ComponentSystemBase.cs:449`, the `params EntityQueryDesc[]` overload; the `params ComponentType[]` one is at `:439`, same modifier. **Corrected in place (review gate, 2026-08-26): this bullet "corrected" the cite to `:459`, which is past the end of a 458-line file, and inverted which overload sits where.** The original `:449` was right, so the correction broke a working citation — the shape to watch when a pass re-cites a line it did not open. Step 5's `RequireForUpdate` **appends and never replaces** (`SystemState.cs:639-645`), so the target ends up requiring both queries; that composes only because step 3 guarantees the new desc narrows the old, and both targets the technique was derived against already require the query being replaced (`src/Game/Game.Net/LaneSystem.cs:9200`, `src/Game/Game.Serialization/SubBlockSystem.cs:73`). A reader adapting the recipe to *widen* a query would gate the system on both and get a system that stops running — carried by the added `Absent` clause and the existing `ecs-in-this-game` pointer between them.
- **The hash guard: no change, and the mechanism recorded so it is not re-derived.** `EntityQuery.GetHashCode()` is `return (int)__impl;` (`src/Unity.Entities/Unity.Entities/EntityQuery.cs:94-96`) — the `EntityQueryImpl*` truncated to 32 bits, so on a 64-bit build the shipped test is "the low 32 bits of the pointer are zero" rather than "the query is default". The effect the shipped sentence states is right. Unconfirmed: whether `RequireForUpdate` on a *destroyed* target throws or corrupts — `CheckedState()`'s checking half is compiled out here, and settling it needs an `eval` against a running game with a deliberately destroyed system.
- **Source-list feedback.** Entry 13 held on every URL rule and search-first earned its keep twice. Three amendments went in: **`@1.8` for Burst**, which the entry did not name; that a branch URL serves that branch's *newest patch* — `@1.8` served 1.8.29 and 1.8.30 within one session, `@1.3` served 1.3.15 — so the gap is moving and the page header is where the served version is read; and a softening of "never authoritative for API shape" that keeps the precedence rule while allowing the API reference as a **lead**, since two of this pass's strongest findings started on one. **The larger gap was entry 15's rather than 13's**: the DOTS source generators and Burst's IL post-processor ship as source there, and for anything the generators *emit* the generator is the definition where the decompile is only a sample — three of this pass's findings were settled that way and none of them could have been settled from the checkout.

## Review gate 2026-08-26: the Burst attribute check pointed the wrong way

The sweep added "Check the attribute before you conclude that, because not every job struct in `src/Game` carries `[BurstCompile]` and a patch on one that does not runs like any other." The check is sound in the absence direction and unsound in the presence direction, which is the direction a reader uses it in.

`[BurstCompile]` is a request rather than a record. Burst compiles no generic job whose concrete type is closed only at runtime, and the escape hatch that would register such specializations exists nowhere in this game — `RegisterGenericJobType` returns nothing across `src/Game` and `src/Colossal.Core`, its only hits being the attribute's own declaration and two Unity call sites. `DeserializeComponentDataJob<TReader>` carries the attribute (`src/Colossal.Core/Colossal.Serialization.Entities/ComponentDataSerializer.cs:82-83`) and is nested in `ComponentDataSerializer<TComponentData>`, every instance of which is built by reflection (`ComponentSerializerLibrary.cs:82/96`). A Harmony patch on that `Execute` runs.

The file-scoped hedge does not save the sentence, because `src/Game` declares generic `[BurstCompile]` job structs of its own: `Game.Net/AirwaySystem.cs:24-25` and `:43-44`, `Game.Simulation/CellMapSystem.cs:13-14`, `Game.Simulation/GroundHeightSystem.cs:35-36`, `Game.Simulation/BuildingPollutionAddSystem.cs:34-35`, `Game.Tools/ApplyBrushesSystem.cs:82-83`.

**The inconsistency was inside one hunk of this sweep.** The method paragraph gained the presence-direction qualification — "or when that method has no compiled native body" — and the job paragraph four lines below gained only the absence-direction check. One edit, opposite treatment of the same fact.

The correction states the request-vs-record rule and names the runtime-closed generic as the case it bites, and extends the launch-switch sentence with the positive test: a patch that fires with Burst still on is proof that job was never compiled. No marker: both facts are durable architecture rather than names that rot.

**Second and third passes: the query-rewrite step's `Absent` clause was wrong twice more.** Step 3 of the rewrite recipe told a reader to use `Absent` "if your marker is enableable", full stop — the same blanket the delta round retired from `ecs-in-this-game`, left standing here because the first pass only rewrote its *rationale*. The replacement rationale was then wrong in its own way: it claimed the consumers "are vanilla jobs that loop `chunk.Count` and ignore the mask", and nine vanilla systems in `src/Game` consume the mask through `ChunkEntityEnumerator` — `CrimeEffectSystem`, `UnlockSystem`, `CountHouseholdDataSystem`, `ProcessingRequirementSystem`, `TransportRequirementSystem`, `ZoneBuiltRequirementSystem`, `StrictObjectBuiltRequirementSystem`, `InitializeObsoleteSystem` and `SecondaryPrefabReferencesSystem` — and they are exactly the ones whose queries already name an enableable component. So the mechanism claim went, and the step now carries the condition the choice actually turns on: enableable **and** permanent, because an entity `Absent` rejects is never re-admitted by flipping the bit back. `ecs-in-this-game` owns the category distinction and the recipe already points at it.
