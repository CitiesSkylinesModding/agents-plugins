# Navigating the decompile

**Baseline.** Decompiled game 1.6.0f1 (build 6216.19404, changelist 419.d6c6), the checkout at `C:\Users\Morgan\Documents\Projets\DecompiledCitiesSkylines2`, read at commit `ec7c3720` and since moved past, produced by `ilspycmd` over the install's own managed DLLs on 2026-06-24 and re-derived for this file on 2026-08-05.
Install read at the same version, `C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II`.
Mod corpus read 2026-08-05 at the 22-repository checkout under `C:\Users\Morgan\Documents\Projets\cs2-third-party-mods`.
The reformatted UI bundle is `DecompiledCitiesSkylines2/src-ui/source.js`, **135,021 lines**, prettier at its defaults over `Cities2_Data/Content/Game/UI/index.js`; a copy whose line count differs will not resolve the citations below. The same directory also holds `src-ui/source.css`, 24,902 lines, from `index.css`.
The wiki was not fetched: nothing on it bears on this topic (see `## Dead ends`).

**This file is a verification pass, not a discovery pass.**
Its seed is `survey-decompile-moddable-surface.md` §1, §2 and §5, produced 2026-07-31 against the same checkout, and every claim below says whether the survey's number held or moved.
Where a claim is marked **held**, it was re-run and reproduced exactly; where it **moved**, both numbers are given.
The register matters to the authoring agent: a count that has already drifted once in five weeks is a count that carries a volatility marker, and one that reproduced exactly against a re-derivation is architecture.

---

## Findings

### The corpus, measured

`src/` holds **163 assembly directories and 22,984 `.cs` files**, totalling **3,624,127 lines**.
**Withdrawn as a description of a reader's tree**: this checkout is hand-pruned, and a provisioned one holds 173 directories and 25,486 files. The correction under "The decompile is complete for anything a mod can touch" states what that invalidates and what survives.
`src/Game` alone is **4,388 files and 833,247 lines**.

The survey's "163 assemblies" **held**, and there is a counting trap under it worth stating because it is the first command anybody runs.
Counting directories under `src/` answers **164**: the extra one is `src/.idea/`, a JetBrains IDE settings folder (`src/.idea/.idea.Solution/.idea/workspace.xml` and eight siblings) that is not an assembly and holds no C#.
It is also the **only** thing in the whole tree that nests deeper than two levels — every other directory under `src/` is at depth 2 (`src/<Assembly>/<Namespace>/`) and nothing else at all is at depth 3, 4 or 5.

**Ruled (2026-08-05, maintainer).** Nothing about `src/.idea/` ships: it exists only in this checkout, a provisioned decompile has none, and the maintainer's own files are not the reference's subject. The directory-count exclusion this paragraph recommended was cut, and the reference states no directory count at all — see the correction below, which is why counting them was the wrong instruction twice over.

Rots: every count in this section — re-run against `src/` after any regeneration.

### The decompile is complete for anything a mod can touch, and that is checkable

`Cities2_Data/Managed/` ships **175 `.dll` files**. 163 of them have a matching directory under `src/`.
The twelve with none are `Accessibility`, `BacktraceCrashpadWindows`, `crashpad_handler`, `Mono.Posix`, `Mono.WebBrowser`, `System.Configuration.Install`, `System.Runtime.Serialization.Formatters.Soap`, `System.ServiceProcess`, `System.Windows.Forms`, `Unity.Microsoft.GDK`, `Unity.Microsoft.GDK.Tools`, `XblPCSandbox`.

**Corrected (review gate, 2026-08-06): this checkout was pruned by hand, and the twelve are not what this section assumed.**
The `Unconfirmed:` experiment below was run: `ilspycmd` over each of the twelve, with the setup skill's own command and arguments.
**Ten decompile cleanly**, producing 2,502 `.cs` files and 337,832 lines; only `BacktraceCrashpadWindows` and `crashpad_handler` fail, both with `PE file does not contain any managed metadata`, because both are native payloads carrying a `.dll` extension.
So a checkout provisioned by the documented command holds **173 assembly directories and 25,486 `.cs` files**, and this one is missing ten that a reader will have.
`src/Game` itself is untouched — a fresh `ilspycmd` over `Game.dll` yields exactly 4,388 files, matching the checkout — so every figure scoped to `src/Game` stands.
That is the only assembly re-run, so it says nothing about whether the other nine in the reading universe were trimmed inside.

**Any tree-wide _count_ in this file was measured against this pruned tree, and the ten missing assemblies move it.**
Re-derive one against a provisioned checkout before shipping it, or prefer the reading-universe form of the same fact, which does not move.
A tree-wide _zero_ is unaffected, since none of the ten can contain what those searches look for — the frontend-file and `game-ui/` absences stand as written.

The surviving fact is the one the reference ships, and it is narrower than what this section claimed: the provisioning command decompiles every managed assembly, so in a tree provisioned that way an absence is a fact about the game. In a hand-trimmed tree it is a fact about the trimming.
The twelve are emphatically not "none of them game code" — that was inferred from their absence, which is circular, and the absence was a human deletion.

### Assembly triage: the reading universe is ten assemblies and about a fifth of the tree

Re-derived file counts, all **held** against the survey except where noted.

**Tier A — the reading universe.**

| Assembly | `.cs` files | Survey | What it owns |
| --- | ---: | :---: | --- |
| `Game` | 4,388 | held | Simulation, prefabs, tools, UI, the modding API, `SystemUpdatePhase`, `GameSystemBase`, `SystemOrder`. |
| `Colossal.Core` | 303 | held | `COSystemBase`, `Colossal.Entities` (3 files), `Colossal.Serialization.Entities` (47), `Colossal.Json`, `Colossal.Randomization`, `Colossal.Reflection`, `Colossal.Versioning`. |
| `Colossal.IO.AssetDatabase` | 165 | held | Mod discovery and loading (`ExecutableAsset`), `AssetDatabase.global/game/user`, `LocaleAsset`, `PrefabAsset`, `UIModuleAsset`. |
| `Colossal.UI.Binding` | 69 | held | The whole C#↔JS binding vocabulary. |
| `Colossal.Collections` | 54 | held | `NativeQuadTree`, `NativeHeapAllocator`, `NativeAccumulator`. **Corrected (review gate, 2026-08-06): `NativeHeap` does not exist; the types are `NativeHeapAllocator` and `NativeHeapBlock`.** |
| `Colossal.UI` | 43 | held | `UIManager`, `UIView`, `DefaultResourceHandler`, `UISystem`. |
| `Colossal.IO` | 33 | held | `IOUtils`, `MultiPartFileStream`, `ZipUtilities`, the large `BinaryReaderExtensions`/`BinaryWriterExtensions`. |
| `Colossal.Mathematics` | 27 | held | `Bezier4x3`, `Bounds3`, `Line3`. |
| `Colossal.Logging` | 20 | held | `LogManager.GetLogger`. |
| `Colossal.Localization` | 18 | held | `LocalizationManager.AddSource`, `MemorySource`, `CSVFileSource`. |

The survey listed `Colossal.IO` under Tier B and gave the Tier A total as "~5,100 files".
Counting the nine it names gives **5,087**; adding `Colossal.IO` gives **5,120**, which is **22.3% of the tree**.
`Colossal.IO` belongs in Tier A rather than Tier B: it is where the `.cok` package format is read (`ZipUtilities.cs`) and where the 479-line `BinaryReaderExtensions` lives, both of which a save- or asset-touching mod reads.

**Tier B — read when your mod goes there.** `PDX.SDK` 909, `Unity.Entities` 654, `Colossal.Mono.Cecil` 580, `Unity.InputSystem` 334, `PDX.ModsUI` 240, `Colossal.OdinSerializer` 214, `Unity.Collections` 195, `Cohtml.RenderingBackend` 193, `cohtml.Net` 160, `Colossal.AssetPipeline` 132, `Backtrace.Unity` 101, `Colossal.PSI.Common` 89, `Colossal.ATL` 89, `Unity.Mathematics` 79, `Cohtml.Runtime` 69, `Game.ArtPipeline` 48, `Unity.Burst` 38. All **held**.

`Unity.Entities` earns its place for one concrete reason and the survey states it correctly: `TypeManager.InitializeAdditionalTypes(assembly)` is called on a mod's own assembly the moment it loads (`src/Game/Game.Modding/ModManager.cs:148`, inside `AfterLoadAssembly` at `:146`), which is why a mod's `IComponentData` types get type indices at all.

**Tier C — noise.**

- BCL and Mono: `mscorlib` 2,312, `System` 1,626, `System.Xml` 1,080, `System.Data` 704, `System.Core` 579, `System.Runtime.Serialization` 300, `System.Drawing` 258, `System.Security` 236, and the rest. Together with `netstandard`, `Mono.*` and `Microsoft.*` this is **7,993 files** — the survey's "~7,500" **moved up**.
- `UnityEngine.*Module`: **69 directories, 2,975 files**. The survey's "~90 directories, ~2,700 files" **moved both ways** — fewer directories, more files. Largest are `UnityEngine.CoreModule` 920 and `UnityEngine.UIElementsModule` 768, both **held**.
- Unity render pipelines: `Unity.RenderPipelines.HighDefinition.Runtime` 595 and `Unity.RenderPipelines.Core.Runtime` 310, both **held**; **916 files** across all `Unity.RenderPipelines*`.
- Third party: `com.rlabrecque.steamworks.net` 460, `Newtonsoft.Json` 267, `ICSharpCode.SharpZipLib` 116, `Unity.TextMeshPro` 114, `Cinemachine` 106, `Unity.Timeline` 85, `Unity.VectorGraphics` 82, `DiscordSDK` 64. All **held** except TextMeshPro and Timeline, which the survey did not number.
- Test and tooling: `Game.TestScenarios` 30, `Colossal.Core.TestScenarios` 24, `DryDock.Runtime` 15, `Colossal.TestFramework` 14, `AssetDatabase.TestScenarios` 3.

The survey's caveat on `Unity.InputSystem` **holds and is worth carrying**: it is third-party but reachable, because `Game.Input` wraps it with `ProxyAction`/`ProxyBinding` and `ModSetting`'s keybinding properties are `ProxyBinding` values (`src/Game/Game.Modding/ModSetting.cs:32-34`). Read `Game.Input` (61 files), never `Unity.InputSystem` (334).

Rots: every count in this table — re-run per assembly directory.

### The namespace map of `src/Game`: 75 directories, not 70

The survey's header says "4,388 files, 70 directories". The file count **held**; the directory count **moved to 75**.
Part of the gap is a directory the survey's table names nowhere — `Game.PSI.PdxSdk` (3 files) — and the rest is rows that group several directories into one cell, so the table understates its own coverage.

Every per-namespace file count in the survey's table reproduced exactly. The full current ranking, all 75, for the authoring agent to use directly:

| Directory | Files |  | Directory | Files |
| --- | ---: | --- | --- | ---: |
| `Game.Prefabs` | 1274 |  | `Game.City` | 34 |
| `Game.Simulation` | 479 |  | `Game.Companies` | 30 |
| `Game.UI.InGame` | 224 |  | `Game` (root ns) | 27 |
| `Game.Rendering` | 155 |  | `Game.Zones` | 27 |
| `Game.Net` | 148 |  | `Game.SceneFlow` | 22 |
| `Game.UI.Widgets` | 145 |  | `Game.UI.Menu` | 21 |
| `Game.Buildings` | 145 |  | `Game.Triggers` | 20 |
| `Game.UI.Editor` | 111 |  | `Game.Prefabs.Climate` | 19 |
| `Game.Tools` | 111 |  | `Game.Modding.Toolchain.Dependencies` | 19 |
| `Game.Vehicles` | 92 |  | `Game.Effects` | 18 |
| `Game.Prefabs.Modes` | 86 |  | `Game.Notifications` | 17 |
| `Game.Tutorials` | 85 |  | `Game.Simulation.Flow` | 16 |
| `Game.Objects` | 85 |  | `Game.Reflection` | 15 |
| `Game.Settings` | 80 |  | `Game.Serialization.DataMigration` | 12 |
| `Game.Pathfind` | 80 |  | `Game.UI.Localization` | 11 |
| `Game.Serialization` | 74 |  | `Game.Rendering.Utilities` | 11 |
| `Game.Routes` | 70 |  | `Game.Prefabs.Effects` | 11 |
| `Game.Debug` | 69 |  | `Game.UI.Debug` | 10 |
| `Game.Citizens` | 64 |  | `Game.Modding.Toolchain` | 10 |
| `Game.Events` | 63 |  | `Game.Policies` | 8 |
| `Game.Input` | 61 |  | `Game.Achievements` | 8 |
| `Game.Areas` | 51 |  | `Colossal.Atmosphere` | 8 |
| `Game.Common` | 49 |  | `Game.PSI` / `Game.Economy` / `Game.Assets` / `Game.Agents` | 7 each |
| `Game.UI` | 41 |  | `Game.Rendering.Debug` / `Game.Prefabs.Terrain` | 6 each |
| `Game.UI.Tooltip` | 39 |  | `Game.Glossary` / `Game.Audio` | 5 each |
| `Game.Creatures` | 37 |  | `Game.UI.Editor.Widgets`, `Game.Rendering.CinematicCamera`, `Game.PSI.PdxSdk`, `Game.Modding`, `Game.Dlc`, `Game.AssetPipeline` | 3 each |

The 62 rows above, plus five directories at 2 files (`Game.UI.Thumbnails`, `Game.Rendering.Legacy`, `Game.Prefabs.Water`, `Game.Audio.Radio`, `Colossal.Rendering`) and eight at 1 (`Unity.Mathematics`, `Unity.Entities.CodeGeneratedRegistry`, `System.Runtime.CompilerServices`, `Properties`, `Game.Rendering.Climate`, `Game.PSI.Internal`, `Game.CinematicCamera`, `Colossal.Atmosphere.Internal`), make 75.
`src/Game/*.cs` at the assembly root holds **10 files**, four more than the survey names: `__JobReflectionRegistrationOutput__17016606566994089001.cs`, `-BurstDirectCallInitializer.cs`, `UnitySourceGeneratedAssemblyMonoScriptTypes_v1.cs`, `DayNightCycleData.cs`, `DebugCamera.cs`, `DepthFadePass.cs`, `GameModeSettingData.cs`, `ShowIfAttribute.cs`, `UberZOrdererTest.cs`, `VTTestGameManager.cs`.

**The whole public mod API is three files**: `src/Game/Game.Modding/IMod.cs` (8 lines), `ModManager.cs` (577), `ModSetting.cs` (372). **Held**, and it is the single most orienting fact in the table.

Two spot-check families in the survey's table, re-derived so the authoring agent knows which method produced them (this matters — see the family trap below):

| Namespace | Declares `IComponentData` | ~~Anchored pattern finds~~ (withdrawn) | Files containing the token |
| --- | ---: | ---: | ---: |
| `Game.Prefabs` | 409 | ~~390~~ | 413 |
| `Game.Buildings` | 82 | ~~80~~ | 82 |
| `Game.Net` | 63 | ~~58~~ | 64 |
| `Game.Vehicles` | 47 | ~~28~~ | 48 |
| `Game.Citizens` | 35 | ~~29~~ | — |
| `Game.Objects` | 42 | ~~31~~ | — |

**The middle column is the broken pattern's output, kept struck so the correction below has something to point at.** Read the left column as the answer.

~~The survey's four figures reproduce exactly under an anchored pattern (`^public (readonly )?struct … : … IComponentData`) and over-count by 6% to **71%** under a bare token search.~~

**Corrected (review gate, 2026-08-05): the anchored pattern is broken, not stricter.** It requires a bare identifier before the colon and so misses every struct ILSpy rendered with a primary constructor — `public struct Car(CarFlags flags) : IComponentData`. True declaration counts: `Game.Prefabs` **409** (not 390), `Game.Buildings` **82** (80), `Game.Net` **63** (58), `Game.Vehicles` **47** (28), `Game.Citizens` **35** (29), `Game.Objects` **42** (31). So the columns are nearly equal rather than 6–71% apart, and `Game.Vehicles` is 47 against 48 rather than 28 against 48. Reproducing the survey exactly is what made this look verified: the survey and this pass ran the same broken pattern. Primary constructors are one of the two ILSpy tells this very file documents.

Other subclass censuses, all **held**: 280 `: ComponentBase` in `Game.Prefabs`, 112 `: PrefabBase` in `Game.Prefabs`, 301 `: GameSystemBase` in `Game.Simulation` (survey said 300), 29 `: BaseDebugSystem`, 24 `: TooltipSystemBase`, 771 `struct … : IJobChunk` and **zero** `: IJobEntity` across `src/Game`.
Assembly-wide, `: GameSystemBase` appears **745 times in 732 files** — the survey's 726 **moved**, and the two numbers differ because a few files carry the token more than once.

Rots: every count in this section, and the namespace directory set itself.

### The layout is two levels, and the globs that exploit it

`src/<Assembly>/<FullNamespace>/<TypeName>.cs`. Nothing nests further, `src/.idea/` excepted.
**22,803 of 22,984 files** sit at that depth; the remaining **181** sit one level up, directly in an assembly directory with no namespace folder — those are the assembly-root files, and `src/Game` has 10 of them (above), `System.Data` 9, `Cinemachine` 8, `Unity.Entities.Hybrid` 7, `Unity.Entities` 6, `Colossal.Core` 4, and 84 other assemblies between 1 and 5.
A glob written as `src/*/*/<Name>.cs` misses all 181. `src/**/<Name>.cs` catches them.

Verified with the harness's own `Glob` tool against this checkout:

| Goal | Pattern | Result |
| --- | --- | --- |
| A type by name | `src/**/<TypeName>.cs` | `src/**/AgingSystem.cs` → exactly one file, `src/Game/Game.Simulation/AgingSystem.cs` |
| A type whose name collides | same | `src/**/SearchSystem.cs` → six files, all under `src/Game`, in `Game.Zones`, `Game.Routes`, `Game.Net`, `Game.Effects`, `Game.Objects`, `Game.Areas` |
| Every system in a domain | `src/Game/Game.Simulation/*System.cs` | the directory _is_ the namespace |
| Everything in a namespace | `src/Game/<Namespace>/` | verbatim, dots and all |
| Which assembly owns a namespace | `find src -maxdepth 2 -type d -name '<Namespace>'` | the only reliable route, because of the exceptions below |

**How often a name-glob lands on one file.** Over the whole tree there are 21,510 distinct basenames and 20,611 of them occur once — **95.8%**.
Inside the Tier A reading universe (5,120 files) there are 4,898 distinct basenames and 4,726 occur once — **96.5%**.
The survey's "works ~99% of the time" **moved down** to 96.5%, and the gap is not academic: the names that collide are the ones a mod author reaches for. `SearchSystem`, `InitializeSystem`, `RaycastJobs`, `ReferencesSystem`, `ValidationHelpers`, `UpdateCollectSystem`, `Node`, `Edge` are all high-traffic types.

**Grep recipes that still work**, each re-run:

| Question | Pattern | Measured against 1.6.0f1 |
| --- | --- | --- |
| When does system X run | `X>` in `src/Game/Game.Common/SystemOrder.cs` | the only file with registrations at all (below) |
| What runs in phase P | `SystemUpdatePhase.P` in `SystemOrder.cs` | 31 of the 32 phases appear |
| Every subclass of B | `class [A-Za-z0-9_]+ : B\b` | 280 hits for `ComponentBase`, 112 for `PrefabBase` |
| Every binding a system exposes | `AddBinding\|AddUpdateBinding` in that file | 880 call sites across `src/Game` |
| Which archetype carries C | `GetArchetypeComponents` in `src/Game/Game.Prefabs/` | 374 sites |
| The settings vocabulary | `SettingsUI` in `src/Game/Game.Settings/` | 38 `SettingsUI*Attribute.cs` files of 40 `*Attribute.cs` there |

The survey's "all 40+ `SettingsUI*Attribute` types" **moved**: there are exactly **38**, in a directory holding 40 attribute files.

**File-per-type, quantified.** In `src/Game` only **5 files of 4,388** declare more than one top-level type, and they are exactly the five the survey names: `Game.Reflection/DelegateAccessor.cs`, `Game.Settings/QualitySetting.cs`, `Game.UI.Editor/DualPopupValueField.cs`, `Game.UI.Editor/HierarchyMenu.cs`, `Game.UI.Widgets/FloatSliderField.cs`. **Held exactly.**
Across the whole tree it is **149 files of 22,984**, and three of them are in `Colossal.UI.Binding`, four in `Colossal.IO.AssetDatabase` and three in `Colossal.Core` — all Tier A.

That gap is the topic's own worked example of a scoped claim: file-per-type is a property of `src/Game`, not of the decompile.

**Generic arities collapse into one file**, and this is why the Tier A exceptions are where they are. `src/Colossal.UI.Binding/Colossal.UI.Binding/TriggerBinding.cs` holds five types — `TriggerBinding` at `:7`, `TriggerBinding<T>` at `:45`, `<T1,T2>` at `:71`, `<T1,T2,T3>` at `:101`, `<T1,T2,T3,T4>` at `:135`. `CallBinding.cs` holds six — `CallBinding<TResult>` at `:6` through `CallBinding<T1,T2,T3,T4,T5,TResult>` at `:161`. **Held exactly**, line numbers included.
So `Glob CallBinding*.cs` returns one file and that file is six types; a reader who greps for `CallBinding<T1, T2, TResult>` and finds one hit has found the declaration of one arity, not the whole family.

Rots: the glob success rates and the five multi-type files in `src/Game`.

### The namespace-directory rule, and the six places it lies

The rule: the directory name **is** the fully-qualified namespace, verbatim, dots included.
It holds for every one of the 1,150 namespace directories. What does not hold is the inference a reader actually draws from it — that a `Game.*` namespace lives in the `Game` assembly.

**Six places it lies, all re-verified:**

1. **`Game.*` namespaces outside `src/Game`.** Six directories: `src/Colossal.Core/Game.Threading/` (4 files: `CoroutineHost.cs`, `ICoroutineHost.cs`, `TimedScope.cs`, `UnityTask.cs`), `src/Colossal.IO/Game.UI.Editor/` (1 file, `NativeHelpers.cs`), `src/Game.TestScenarios/Game.Debug.Tests/` (27 files), and three inside the `Game.ArtPipeline` assembly (`Game.ArtPipeline`, `Game.ArtPipeline.Impostors`, `Game.ArtPipeline.Preview`).
   The survey named the first two; the other four are new here.
   **`Game.UI.Editor` is therefore split across two assemblies**, `Game` (111 files) and `Colossal.IO` (1).
2. **Non-`Game` namespaces inside `src/Game`.** Seven directories: `Colossal.Atmosphere/` (8), `Colossal.Atmosphere.Internal/` (1), `Colossal.Rendering/` (2), `Properties/` (1), `System.Runtime.CompilerServices/` (1), `Unity.Entities.CodeGeneratedRegistry/` (1), `Unity.Mathematics/` (1). **Held.**
3. **Namespaces split across two assemblies.** `Colossal.Rendering` lives in `Colossal.Core` (46 files, the batch-renderer internals) and in `Game` (2 files, `DebugCustomPass.cs` and `VTTextureRequester.cs`). `Colossal.IO` lives in `Colossal.Core` (3 files) and in `Colossal.IO` (5). Both **held**.
   `Colossal` itself is split four ways: `Colossal.Core`, `Colossal.Logging`, `Colossal.Mathematics` and — the one nobody expects — **`Unity.Entities`**, which carries `src/Unity.Entities/Colossal/CORuntimeApplication.cs` and is the evidence `docs/SOURCES.md` entry 13 rests on for the claim that the shipped Entities assembly is not stock. **Held.**
4. **`Colossal.Core` is a grab-bag.** Its 28 namespace directories include seven `CliWrap*` folders (43 files, a third-party shell-exec library), `Mono.Options` (16 files, the option parser `GameManager` parses the command line with), and `Game.Threading` — alongside the three that matter: `Colossal.Entities` (3), `Colossal.Serialization.Entities` (47) and `Colossal.Json`. **Held.**
5. **A type named `Game` that is not the assembly.** `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/Game.cs` is `public readonly struct Game : IAssetDatabaseDescriptor<Game>` — an asset-database descriptor. **Held.**
6. **`SystemOrder` is in `Game.Common`, not `Game`.** The single most valuable index in the corpus is not in the root namespace. **Held**, and see the next finding.

**And a seventh the survey does not name, which breaks its own remedy.**
The survey's advice for a colliding type name is "always qualify a glob with the namespace directory". That fails on exactly two files, and both are Tier A:
`Colossal.IO/BinaryReaderExtensions.cs` and `Colossal.IO/BinaryWriterExtensions.cs` each exist **twice**, under the same namespace directory, in two assemblies — and they are different classes, not partials.
`src/Colossal.Core/Colossal.IO/BinaryReaderExtensions.cs` is 11 lines and declares one method, `ReadHash`. `src/Colossal.IO/Colossal.IO/BinaryReaderExtensions.cs` is 479 lines and declares `ReadMeshAttribute`, `ReadGuid`, a `kMaxAttributes` field and dozens more. Both are `public static class BinaryReaderExtensions` in `namespace Colossal.IO`.
Across the whole tree, (namespace + filename) pairs appearing in more than one assembly number 111, and **all but these two are BCL, Unity or codegen** — `Unity/ThrowStub.cs` in 15 assemblies, `System.Runtime.CompilerServices/FriendAccessAllowedAttribute.cs` in 9, the `Mono*Attribute` family in 7 each, and the `AssemblyInfo.cs` / `UnitySourceGenerated…` / `AssemblyTypeRegistry.cs` codegen set.

Verdict: the survey's remedy is incomplete and the checkout overturns it. `survey-decompile-moddable-surface.md:508` says "**Always qualify a glob with the namespace directory**", which resolves every one of the 149 duplicated basenames inside `src/Game` and cannot resolve the two `Colossal.IO` twins, because their namespace directory is identical and their contents are not.
So the disambiguation rule is: **qualify by namespace directory, and where that still returns two files, qualify by assembly.** Inside the modding universe that second step is needed exactly twice.

**Duplicate type names inside `src/Game`.** 149 basenames occur more than once; 11 of them occur three or more times.
The worst: `SearchSystem.cs` ×6, `RaycastJobs.cs` ×6, `InitializeSystem.cs` ×6, `ValidationHelpers.cs` ×5, `ReferencesSystem.cs` ×5, `UpdateCollectSystem.cs` ×4, then `OutsideConnection.cs`, `Node.cs`, `IntInputField.cs`, `GeometryFlags.cs` and `Edge.cs` at 3 each.
That leaves **138 two-way collisions**; the survey's "~200" **moved down**. `IntInputField.cs` at ×3 is new relative to the survey's list.

**The game's own source tells you when a name is ambiguous.** Where the C# has to write a name fully qualified, it is because two are in scope. `src/Game/Game.Simulation/AgingSystem.cs:43` declares `ComponentLookup<Game.Citizens.Student> m_Students` and `:62` calls `RemoveComponent<Game.Citizens.Student>`; `src/Game/Game.Common/SystemOrder.cs:110` registers `UpdateAt<Game.Events.InitializeSystem>`. **Both held at the exact lines the survey cited.**
`SystemOrder.cs` carries **33 distinct fully-qualified `Game.*` registrations** — `Game.Areas.SearchSystem`, `Game.Net.SearchSystem`, `Game.Objects.SearchSystem`, `Game.Buildings.InitializeSystem`, `Game.Creatures.InitializeSystem`, `Game.Events.InitializeSystem`, `Game.Net.InitializeSystem`, and so on. Reading that list is the cheapest census of ambiguous names in the game.

Rots: the six-place list and the two `Colossal.IO` twins.

### `SystemOrder.cs` answers "when does this run", and it answers it exhaustively

`src/Game/Game.Common/SystemOrder.cs` is **1,060 lines** and holds **1,012** `UpdateAt<>` / `UpdateBefore<>` / `UpdateAfter<>` registrations.

The stronger claim, which the survey does not make and which is what makes the file worth teaching: **it is the only file in `src/Game` that registers anything.**
A grep for `UpdateAt<|UpdateBefore<|UpdateAfter<` across all 4,388 files returns exactly two files — `SystemOrder.cs` with 1,012 hits, and `src/Game/Game/UpdateSystem.cs` with 5, which are the method declarations themselves.
So a system absent from `SystemOrder.cs` is a system the game never registers, full stop. That is a negative result a reader can act on, and it is the one place in this topic where an empty grep does prove something — because the search space is one file and the search is over a closed vocabulary.

**31 of the 32 phases appear there.** `src/Game/Game/SystemUpdatePhase.cs` is 38 lines declaring `Invalid = -1` plus 32 phases (`:5-37`). Every one is named in `SystemOrder.cs` except **`PreSimulation`**, which has zero registrations and is nevertheless pumped: `src/Game/Game.Simulation/SimulationSystem.cs:168` and `:272` both call `m_UpdateSystem.Update(SystemUpdatePhase.PreSimulation)`.
An empty phase that is still driven is exactly the shape a reader of `SystemOrder.cs` will misread as "this phase does not exist".

**The stock ECS ordering attributes are inert here**, re-verified: zero `[UpdateAfter]`, `[UpdateBefore]` and `[UpdateInGroup]` across all of `src/Game`. **Held.** `mod-lifecycle-and-ordering` owns what to do about that; this topic owns the fact that grepping for them is how a reader discovers it.

Rots: the registration count, the phase set, and `PreSimulation`'s emptiness.

### The mangled type-handle names are more greppable than the type

The DOTS source generator rewrites every system's component access into a nested `TypeHandle` struct whose fields carry the namespace, the type and the access mode in the field name itself.

The canonical example, re-verified line by line: `src/Game/Game.Simulation/AgingSystem.cs:147` opens `private struct TypeHandle`, and its fields are

```
public SharedComponentTypeHandle<UpdateFrame> __Game_Simulation_UpdateFrame_SharedComponentTypeHandle;
public BufferTypeHandle<HouseholdCitizen>     __Game_Citizens_HouseholdCitizen_RO_BufferTypeHandle;
public ComponentLookup<TravelPurpose>         __Game_Citizens_TravelPurpose_RO_ComponentLookup;
public ComponentLookup<Game.Citizens.Student> __Game_Citizens_Student_RO_ComponentLookup;
public ComponentLookup<Citizen>               __Game_Citizens_Citizen_RW_ComponentLookup;
```

with `__AssignHandles` at `:163` filling them (`:168` is `state.GetComponentLookup<Game.Citizens.Student>(isReadOnly: true)`), the field `private TypeHandle __TypeHandle` at `:200`, `__AssignQueries` at `:284` whose whole body is `new EntityQueryBuilder(Allocator.Temp).Dispose();` at `:286`, and `OnCreateForCompiler` at `:289-294` calling both.
The survey cited `:147, 200, 283, 289`; `:283` **moved to** `:284` and the no-op body **moved from** `:285` **to** `:286`. Everything else **held**.

**The pattern, corrected.** The survey states it as `__<Namespace_With_Underscores>_<Type>_<RO|RW>_<ComponentLookup|ComponentTypeHandle|BufferTypeHandle|SharedComponentTypeHandle>`.
That is wrong on one member, and the way it is wrong is this topic's own fourth trap in miniature: **`SharedComponentTypeHandle` fields carry no `_RO_`/`_RW_` segment at all.** They are `__<Namespace>_<Type>_SharedComponentTypeHandle` — `__Game_Simulation_UpdateFrame_SharedComponentTypeHandle` (`AgingSystem.cs:149`, and in `src/Game/Game.Rendering/PreCullingSystem.cs`, `ObjectInterpolateSystem.cs`, `RelativeObjectSystem.cs`, `RouteBufferSystem.cs`, `UtilityLodUpdateSystem.cs`, `src/Game/Game.Serialization/ResetUpdateGroupSizesSystem.cs`), `__Game_Net_CoverageServiceType_SharedComponentTypeHandle` (`src/Game/Game.Notifications/MarkerCreateSystem.cs`, `src/Game/Game.Rendering/ObjectColorSystem.cs`), `__Game_Net_ArrowMaterial_SharedComponentTypeHandle` and `__Game_Net_LabelMaterial_SharedComponentTypeHandle` (`src/Game/Game.Rendering/AggregateMeshSystem.cs`).
Two more shapes escape it too: there is **no** `__…EntityTypeHandle` variant with a namespace segment (entity handles are declared as `__Unity_Entities_Entity_TypeHandle`, seen at `src/Game/Game.Tools/ToolBaseSystem.cs:90`), and `__EntityStorageInfoLookup` carries no namespace or type segment at all — 135 occurrences across `src/Game`.

Verdict: the survey's stated pattern is wrong and the decompile overturns it. `survey-decompile-moddable-surface.md:515` puts `SharedComponentTypeHandle` inside the `<RO|RW>`-bearing alternation; every one of the shared-component fields in `src/Game` carries no access segment, `AgingSystem.cs:149` included. The corrected pattern is two shapes, not one: `__<Namespace>_<Type>_<RO|RW>_<ComponentLookup|ComponentTypeHandle|BufferLookup|BufferTypeHandle>` for the eight measured above, and `__<Namespace>_<Type>_SharedComponentTypeHandle` with no access segment. Note also that `BufferLookup` is in the real family and absent from the survey's list.

**The eight `RO`/`RW` shapes and their populations**, measured across `src/Game` (33,330 occurrences total):

| Suffix | Occurrences |
| --- | ---: |
| `_RO_ComponentLookup` | 16,016 |
| `_RO_ComponentTypeHandle` | 7,032 |
| `_RO_BufferLookup` | 4,224 |
| `_RW_ComponentTypeHandle` | 1,689 |
| `_RW_ComponentLookup` | 1,365 |
| `_RO_BufferTypeHandle` | 1,274 |
| `_RW_BufferLookup` | 904 |
| `_RW_BufferTypeHandle` | 826 |

**What this buys, measured.** For `Game.Citizens.Citizen`:

- a word-boundary grep for the bare name `Citizen` across `src/Game` hits **118 files**, most of which merely mention it;
- `ComponentLookup<Citizen>|ComponentTypeHandle<Citizen>` hits **81 files**, and tells you nothing about read versus write without opening each one;
- `__Game_Citizens_Citizen_` hits **73 files**, split by the field name alone and in one pass into **60 carrying an `_RO_` field** and **19 carrying an `_RW_` one** (a few carry both).

Narrowing to the one shape that matters most, the eleven files holding `__Game_Citizens_Citizen_RW_ComponentLookup` are `Game.Citizens/CitizenInitializeSystem.cs`, `Game.Debug/DebugSystem.cs`, `Game.Serialization.DataMigration/UpdateCitizenFlagsFromHouseholdsSystem.cs`, and in `Game.Simulation`: `AgingSystem.cs`, `CountHouseholdDataSystem.cs`, `FindSchoolSystem.cs`, `LookForPartnerSystem.cs`, `LeisureSystem.cs`, `PartnerSystem.cs`, `PayWageSystem.cs`, `ResidentAISystem.cs`.

**The limit, and it is load-bearing.** The mangled family covers only handle-based access the generator rewrote. It does **not** cover a write made through an `EntityCommandBuffer` with an inferred type, and that is the dominant form in tool code — see the family trap below. So `_RW_` enumerates the systems that write a component through a lookup or a chunk handle, which is not the same set as "everything that writes it".

Rots: the eight suffix shapes and their populations.

### Fifteen decompilation artifacts, headed by the one that makes a reader skip a real file

The decompiler is ILSpy in C# 12 mode. Two tells, both **held at the survey's exact lines**: file-scoped namespaces (`src/Game/Game/UpdateSystem.cs:11` is `namespace Game;`) and primary constructors on structs (`:15` is `private struct SystemData(SystemUpdatePhase phase, int interval, int offset, int addIndex, ComponentSystemBase system) : IComparable<SystemData>`, and `:40` the same for `IntervalData`).

Every count below was re-run over `src/Game` and **every one held exactly**. That is worth stating in the reference: this is the section of the survey that drifted least, because these are properties of the decompiler rather than of the game.

1. **`[CompilerGenerated]` on hand-written classes — 877 files.** `src/Game/Game.Simulation/AgingSystem.cs:18` carries it on `public class AgingSystem : GameSystemBase`, which is a hand-written simulation system that the DOTS source generator rewrote. **Do not skip a file because it says `[CompilerGenerated]`.** This is the single most misleading artifact in the corpus, and it sits on a fifth of `src/Game`.
2. **`__TypeHandle` / `__AssignHandles` / `__AssignQueries` / `OnCreateForCompiler`.** In nearly every system. `__AssignQueries` is frequently a no-op (`AgingSystem.cs:286`). Ignore the machinery — but the **field names inside** the `TypeHandle` struct are the best index of what a system reads and writes (previous finding).
3. **`InternalCompilerInterface.Get*` wrappers.** The codegen form of `SystemAPI.GetComponentLookup<X>()`. A mod writes the ordinary form.
4. **Field-like events lowered to `Delegate.Combine`/`Delegate.Remove` — 80 occurrences across 30 files.** `src/Game/Game/GameSystemBase.cs:25` reads `loadGameSystem.onOnSaveGameLoaded = (LoadGameSystem.EventGameLoaded)Delegate.Combine(loadGameSystem.onOnSaveGameLoaded, new LoadGameSystem.EventGameLoaded(GameLoaded));` — source-level `+=`. **Held.** The tell the survey misses is two lines below: `:27-29` are ordinary `GameManager.instance.onWorldReady += WorldReady;`. The same method shows both forms, because `onOnSaveGameLoaded` is a delegate **field** and `onWorldReady` is an `event`. So the lowering marks a field, not an event, and a reader can tell which they are looking at by which form appears.
5. **Meaningless local names.** `num`, `num2`, `flag`, `text2`, `list`, `array` — and worse, locals named after their type with a numeric suffix: `int2 int5 = m_UpdateRanges[(int)phase];` at `UpdateSystem.cs:180` and again at `:220`. **Held at both lines.** Never infer meaning from a local identifier. This one has a second-order cost the survey does not draw out: it is why type-inferred call sites are unsearchable (family trap, below).
6. **Named arguments partly reconstructed — 754 files carry `isReadOnly: true`.** **Held.** ILSpy restored these from a boolean-literal heuristic; most other call sites show bare positional literals. An absent argument name means nothing.
7. **Deconstruction noise.** `var (_, modInfo2) = (KeyValuePair<Identifier, ModInfo>)(ref modsInfo);` at `src/Game/Game.Modding/ModManager.cs:266` — a `foreach` over a dictionary rendered oddly. **Held at the exact line.**
8. **`[Preserve]` — 1,020 files.** IL2CPP link preservation, on `OnCreate`/`OnUpdate`/`OnDestroy`/constructors. Semantically irrelevant. **Held.**
9. **Explicit no-arg constructors appended to every system.** `AgingSystem.cs:297`, `UpdateSystem.cs:503`. Not in the original source. (Survey said `:296`/`:503`; the AgingSystem line **moved by one**.)
10. **`goto` and label residue — 41 files.** **Held.**
11. **`unsafe` and raw pointers — 71 files.** Mostly native interop and `Colossal.Collections`. **Held.**
12. **Codegen-only files.** Across the whole tree: `UnitySourceGeneratedAssemblyMonoScriptTypes_v1.cs` ×67, `__JobReflectionRegistrationOutput__*.cs` ×14, `-BurstDirectCallInitializer.cs` ×12, `Unity.Entities.CodeGeneratedRegistry/AssemblyTypeRegistry.cs` ×10. **All held.** 103 files, **165,834 lines**. The two largest files in `src/Game` are both on this list — `UnitySourceGeneratedAssemblyMonoScriptTypes_v1.cs` at 44,121 lines and `AssemblyTypeRegistry.cs` at 20,709 — against `Game.Net/LaneSystem.cs` at 9,326, the largest real file.
13. **Compiler-generated closure and iterator classes are essentially absent.** **Zero** occurrences of `<>c__DisplayClass`, `_003C`, `_003E` or `<>c` in `src/Game`; only 24 files retain a visible `MoveNext()`. **Held exactly.** Lambdas and LINQ read as ordinary C# — `ModManager.cs:112-114` is a genuine `from r in asset.references where r.Value == null select r.Key`, **held**. This is better than a typical decompile and worth saying positively, because it is why a reader can trust what they read.
14. **Generic type arguments are preserved**, closed generics included. No evidence of loss.
15. **`[assembly: AssemblyVersion("0.0.0.0")]` is a decoy.** `src/Game/Properties/AssemblyInfo.cs:20`. **131 of the 163 assemblies** carry that same string. The real version is the line above it — see the next finding.

**The Burst mangled names.** `src/Game/Properties/AssemblyInfo.cs:14-18` carries five `BurstCompiler.StaticTypeReinit` attributes naming types like `Game_002ERendering_002EDequeueAndSort_00004B5A_0024BurstDirectCall`. **Held at the exact lines.** `_002E` is `.`, `_0024` is `$`, and the hex block is the RID of the method's metadata token. **Corrected in place (review gate, 2026-08-26): this line read "the four-hex block is a compiler-assigned id" and both halves were wrong — the block is eight hex digits, and it is the method's row id.** The two dated notes below left it standing, which is the shape the repo `CLAUDE.md` warns about: appending a note is not editing the original. **Corrected (review gate, 2026-08-05): these are not dead ends, and the decode is.** The mangled name appears in the declaring source file as well as in the generated tables — `DequeueAndSort_00004B5A_0024BurstDirectCall` resolves to `src/Game/Game.Rendering/WaterRenderSystem.cs:81`, so grepping it lands a reader on the method. The decoded form is what matches nothing: the encoding names the namespace and the method and omits the declaring type, so no `Game.Rendering.DequeueAndSort` exists (it is `WaterRenderSystem.DequeueAndSort`), and the two `CopyWaterValuesInternal` entries decode to one identical name for two different types. **Corrected (review gate, 2026-08-06): the encoding appears in neither file as described.** `__JobReflectionRegistrationOutput__*.cs` carries no `_002E` at all, and `-BurstDirectCallInitializer.cs` carries the `_0024BurstDirectCall` suffix but writes the namespace as ordinary dotted C# — `WaterRenderSystem.DequeueAndSort_00004B5A_0024BurstDirectCall.Initialize();` — which makes it the more useful of the two, since it names the declaring type the encoded form omits.

Rots: the artifact counts, and the ILSpy version tells.

### The decompile states its own version, in one line, next to a line that says it does not

`docs/SOURCES.md:9` says "The install is the one that carries a version the agent can state."
That is narrower than the checkout allows. **`src/Game/Properties/AssemblyInfo.cs:19` is `[assembly: VersionInternal("1.6.0f1 (419.d6c6) [6216.19404]")]`** — game version, changelist and build, from the decompile alone.

Only **four** assemblies carry a `VersionInternal` attribute, and only one of them is the game's:

- `src/Game/Properties/AssemblyInfo.cs:19` — `1.6.0f1 (419.d6c6) [6216.19404]`
- `src/Colossal.UI/Properties/AssemblyInfo.cs:7` — `1.0.0f1 (419.d6c6) [6216.19385]`
- `src/Colossal.Localization/Properties/AssemblyInfo.cs:6` — `1.0.0a1 (419.d6c6) [6216.19385]`
- `src/Colossal.Core/Properties/AssemblyInfo.cs:8` — `1.0.0f1`, no build stamp

The shared changelist `419.d6c6` is what corroborates that the four came off one build.

The trap is the adjacency: `AssemblyVersion("0.0.0.0")` sits one line under the real answer in the same file, and 131 assemblies repeat it. A reader who greps `AssemblyVersion` concludes the decompile is version-blind.
Corroboration from the corpus, first-hand: the one mod file in twenty-two repositories that ships pasted decompiler output carries `// Assembly: Game, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null` as its header (`Time2Work/NightShift/Systems/Time2WorkResourceBuyerSystem.cs:1-3`) — the decoy propagated into a shipped mod.

**Save-format history** is a second version surface: `src/Game/Game/Version.cs` is 826 lines of `[VersionConstant("<game version> [<build>]")]` fields. The survey's "200+ … through `1.5.7f1 [6157.21012]` at `:821`, `current` at `:825`" is consistent with the file's length: re-derived, the file carries **273** `VersionConstant` attributes.
**Ruled (2026-08-05, orchestrator).** The inference this pass first drew from that — _the last named constant being `1.5.7f1` means 1.6.0f1 introduced no save-format break_ — does not follow and was cut from the shipped file. `current` at `:825` carries a bare `[VersionConstant]` with no version string and the value `315255277153176524`, past `garbageFeeReset`'s `310874822824251924` at `:821`, so the named milestones are not a reading of what the running build writes. What the gap implies for a save is `save-serialization`'s, and that reference currently says nothing about `Version.cs` at all. `save-serialization` owns the consequence; this topic owns that `Version.cs` is where the answer is and that it is in the root `Game` namespace directory rather than in `Game.Serialization`.

Rots: the four `VersionInternal` values, and `Version.cs`'s last named constant.

### What an empty grep proves: four traps, each re-derived here

`docs/solutions/empty-grep-read-as-proof-of-absence.md` is the record of three shipped falsehoods and a fourth failure of the same family. Each is re-derived below against this checkout so the reference can cite its own evidence.

The governing rule, from that file and from `plugins/cs2-modding/AGENTS.md`: **a search returning nothing is evidence about the search.** Turning it into a claim about the game needs a separate argument that the search could have found the thing.

#### Trap 1: a compile-time `const` is inlined, so the name has no consumers

**The measurement.** ~~`src/Game` holds **715** `public const k*` declarations under **287 distinct names**. **248 of those names — 86% — occur exactly once in the whole assembly: their own declaration.**~~

**Corrected (review gate, 2026-08-05): the two halves of that sentence measure different populations and it was cut from the shipped file.** `public const … k*` gives **338** declarations; any-access `const … k*` gives **711**, so the 715 is the all-access figure wearing a `public` label. The distinct-name half is worse: independent re-derivations returned 287, 554, 286 and 124, because the set's edge (generic and multi-word type names, `static readonly`, nested types) was never defined. The durable claim, and the one that shipped, is that **most `k*` constants occur exactly once in the assembly, at their own declaration** — which the three worked shapes below establish without a census.

**The case that shipped wrong.** `public const Snap kSnapAllIgnoredMask = Snap.AutoParent | Snap.PrefabType | Snap.ContourLines;` at `src/Game/Game.Tools/ToolBaseSystem.cs:98` is its only occurrence anywhere in `src/`.
`custom-tools` shipped "nothing in the game consumes it, so it exists for the frontend and for mods".
The consumer is `src/Game/Game.UI.InGame/ToolUISystem.cs:142`, inside the `"tool"`/`"allSnapMask"` binding: `return (uint)(onMask & offMask) & 0xFFF8FFFFu;`.
The arithmetic closes it: `src/Game/Game.Tools/Snap.cs:24-26` gives `AutoParent = 0x10000`, `PrefabType = 0x20000`, `ContourLines = 0x40000`; their union is `0x70000`; `~0x70000` as a `uint` is `0xFFF8FFFF`. The literal **is** the constant, inverted.

**A cross-file case, which is the shape a reader will actually hit.** `public const int kTicksPerDay = 262144;` at `src/Game/Game.Simulation/TimeSystem.cs:18` is its only occurrence in `src/Game` by name. The value `262144` appears **135 times in 88 files** — `src/Game/Game.Events/InitializeSystem.cs:720-721`, `src/Game/Game.Objects/ObjectUtils.cs:361/365/369`, `src/Game/Game.Net/NetUtils.cs:656/660/664`, and `src/Game/Game.Simulation/AgingSystem.cs:203` (`return 262144 / (kUpdatesPerDay * 16);`), among many.

**A same-file case, which is what makes the trap feel impossible.** `public const float kDefaultSeaLevel = 511.7f;` at `src/Game/Game.Simulation/WaterSystem.cs:364` has two consumers **in that same file** — `:1165` (`m_SeaLevel = 511.7f;`) and `:1172` (`Shader.SetGlobalVector("colossal_WaterParams", new Vector4(511.7f, 1f, 0f, 0f))`) — and the name appears at neither.

**What does find the consumers: search for the value, or for the consuming expression.**
And state the cost honestly, because the reference must: a value search over-returns. Of the 135 hits on `262144`, several are unrelated — `Plastics = 262144uL` in `src/Game/Game.Economy/Resource.cs:24` is a flags enum member, and `(CarLaneFlags)(isRight ? 524288 : 262144)` at `src/Game/Game.Net/LaneSystem.cs:7048` is a bit position. A value search converts a false-absence problem into a false-presence one, which is the better trade because a reader can rule out a false hit by reading it and cannot rule out a hit that never appeared.

Verdict: the survey does not carry this trap at all, and `custom-tools.md`'s verdict on `kSnapAllIgnoredMask` (2026-08-03 re-sweep) reproduces exactly here — the grep was accurate and the inference from it was not. First-party evidence, re-derived, agrees with the correction.

Rots: the `0xFFF8FFFF` literal at `ToolUISystem.cs:142`.

#### Trap 2: a scoped grep read as a whole-assembly one

The failure is not the search. `placement-definitions` shipped "the only system that writes it" from a search of a single namespace; the research file recorded the scope and the shipped prose dropped it.

**The shape, re-derived on a fresh case.** `CreationDefinition` — the component every placement goes through — is referenced by **34 files across eight namespace directories** in `src/Game`:

- `Game.Tools` — 25 files (`ObjectToolBaseSystem.cs`, `NetToolSystem.cs`, `ZoneToolSystem.cs`, `AreaToolSystem.cs`, `WaterToolSystem.cs`, `TerrainToolSystem.cs`, `RouteToolSystem.cs`, `SelectionToolSystem.cs`, `BulldozeToolSystem.cs`, `DefaultToolSystem.cs`, `ToolBaseSystem.cs`, `UpgradeDeletedSystem.cs`, `CourseSplitSystem.cs`, `CreationDefinition.cs`, and the eleven `Generate*System.cs`)
- `Game.Simulation` — 3 (`ZoneSpawnSystem.cs`, `AreaSpawnSystem.cs`, `BuildingConstructionSystem.cs`)
- `Game.Serialization` — 1 (`ClearSystem.cs`)
- `Game.Rendering` — 1 (`GuideLinesSystem.cs`)
- `Game.Prefabs` — 1 (`ReplacePrefabSystem.cs`)
- `Game.Objects` — 1 (`PlaceholderSystem.cs`)
- `Game.UI.Tooltip` — 1 (`NetCourseTooltipSystem.cs`)
- `Unity.Entities.CodeGeneratedRegistry` — 1 (`AssemblyTypeRegistry.cs`, codegen)

A grep scoped to `src/Game/Game.Tools/` returns 25 of the 34 and reads like a census. Of the nine it misses, one is codegen and the other eight are exactly the ones that make a claim about who produces definitions wrong: the two simulation spawners build definitions from a `CreateArchetype` rather than through a tool, and `ClearSystem`, `PlaceholderSystem` and `ReplacePrefabSystem` are outside the tool pipeline entirely.

**A second case this file produced by accident**, which is the honest one to teach from because nobody set it up: the survey's own §5.1 claim that "only 5 files in all of `src/Game/` declare more than one top-level type". It is exactly right, and I reproduced it exactly. Corpus-wide the number is **149**, and three of the extras are in `Colossal.UI.Binding` — Tier A, and the assembly a UI mod reads first. A reader who drops "in `src/Game`" concludes file-per-type is a property of the decompile and then trusts `Glob CallBinding*.cs` to have found one type.

**What stating the scope looks like.** Two forms, and both are in the shipped tree already:

- Scope in the sentence: `conflicts.md:177` — "A grep of `src/Game/` **outside `Game.Input` and `Game.Settings`** returns no other consumer of `Usages` at 1.6.0f1."
- Scope in the claim's own terms: `placement-definitions.md:14` — "`EntityManager.CreateArchetype` is used for a definition in exactly two places **in `src/Game/`**, both in the simulation rather than in a tool".

The rule for the reference: **a negative claim carries the span it was run over, in the sentence, not in the citation.** A citation names where the evidence is; a scope names where the search was not.

#### Trap 3: whole subsystems are invisible to a C# search

**The frontend, quantified.** `src/` contains **zero** `.js`, `.css`, `.html`, `.tsx` and `.jsx` files.
The shipped frontend is `Cities2_Data/Content/Game/UI/index.js`, 2,219,232 bytes on one line, reformatting to **135,021 lines** at `DecompiledCitiesSkylines2/src-ui/source.js`, beside `index.css` at 462,876 bytes / 24,902 lines.
The bundle names **1,386 distinct `game-ui/…` module paths** (`src-ui/source.js`, e.g. `"game-ui/common/tooltip/description-tooltip/description-tooltip.tsx"`, `"game-ui/overlay/logo-screen/loading/loading-progress.tsx"`). A grep for `game-ui/` across all 22,984 C# files returns **nothing**.
So the entire module registry — the surface a UI mod extends — is unreachable from the decompile by any search, and the decompile does not hint that it exists.

**Anything shipping as data, quantified.** `Cities2_Data/resources.assets` is 221,648,712 bytes and holds the prefabs the base game shipped with. **Corrected (review gate, 2026-08-06): not "every base-game prefab".** Content added by free updates and packs lives in the `Content/Game/Prefabs_*.cok` packages beside it — `ChirperPark01` returns zero from `resources.assets` and eight from `Prefabs_FreeUpdate02.cok`, while `ElementarySchool01` and `CoalPowerPlant01` are in `resources.assets` as this section claims. It carries type names and no field names, confirmed first-hand by byte-grep: `ServiceConsumption` 432 matches, `BuildingPrefab` 265, `ObjectSubObjects` 4,183, `Citizen` 76 — while `m_Upkeep` and `m_ElectricityConsumption` return **zero**.
**Corrected (review gate, 2026-08-06): those four were `grep -a -c` line counts** (203, 257, 3,772 and 35), which is the miscount this same file documents at the byte-grep finding; the figures above are `grep -a -o … | wc -l`. The zero for the field names holds under either method, so the split itself was never in doubt. `docs/SOURCES.md` entry 5's split reproduces exactly.

**The 72% figure re-derived, and it holds.** `docs/solutions/empty-grep-read-as-proof-of-absence.md:45-47` states "72% of this game's localization namespaces never appear in C#". Re-derived against the 75 groups in `plugins/cs2-modding/skills/cs2-modding/references/technique/localization/vanilla-namespaces.md`:

- Searching `src/Game` for each group name **used as a namespace prefix** — the literal `"<Group>.` — returns exactly **21**: `Assets`, `Common`, `DefaultTool`, `Editor`, `GameListScreen`, `Infoviews`, `Loading`, `Maps`, `Menu`, `Notifications`, `Options`, `Paradox`, `PhotoMode`, `Policy`, `Properties`, `Radio`, `SelectedInfoPanel`, `Services`, `StatisticsPanel`, `SubServices`, `Tools`.
  That is the same 21 `localization.md` and `conflicts.md:133` name, reproduced independently. **54 of 75 groups — 72.0% — are invisible.** **Held.**
- Loosening the search to the bare literal `"<Group>"` adds eight — `Budget`, `Chirper`, `Climate`, `Content`, `Main`, `Overlay`, `Toolbar`, `Tutorials` — for 29 found and **46 of 75 (61.3%) invisible**. Several of the eight are coincidences rather than localization uses.

So the shipped figure holds under the strict reading and 61% is the floor under the loosest one. The reference should state 72% with the counting rule attached, because a reader who counts differently gets a different number and will think the claim moved.

**The verdict this trap produces.** For the frontend and for shipped data, `docs/SOURCES.md` names the source that can answer (entries 3, 4, 5, 9) and the decompile cannot answer at all. An empty C# grep on those subjects is not weak evidence — it is **no** evidence.

Rots: the 1,386 module paths and the 135,021-line count.

#### Trap 4: a pattern naming one member of a family, taken for the family

The variant that does not come back empty, which is what makes it convincing: a search returning exactly one hit reads as a finding rather than as a question about the pattern.

**The record's own case.** A corpus sweep for `xunit|from 'bun:test'|testing-library` returned one hit and the catalog shipped "the only repository here that ships tests"; four ship test projects and three use NUnit, which the pattern never named.

**Re-derived here, three ways, all first-hand.**

**(a) `AddComponent<CreationDefinition>` returns zero — and every tool in the game adds that component.**
The call is `m_CommandBuffer.AddComponent(e, component);` at `src/Game/Game.Tools/ObjectToolBaseSystem.cs:1270`, followed by `:1271` and `:1272` for the kind component and `default(Updated)`. The type is inferred from the local, and the local is named `component` — artifact 5 above. So the type name never appears at the call site.
The scale: ~~`src/Game` holds **1,493** `AddComponent(` call sites, of which only **497 (33%)** use the explicit generic form.~~

**Corrected (review gate, 2026-08-05).** `AddComponent(` and `AddComponent<` are **disjoint** patterns — a generic call is written `AddComponent<X>(` and never contains `AddComponent(` — so 497 is not a subset of 1,493. Measured: 1,493 + 497 = **1,990** exactly, so the generic form is **25%** of the adds, not 33%, and the 1,493 are precisely the sites a generic-form grep cannot see. This finding is itself an instance of the trap it documents.
Cross-check: a grep for `AddComponent<CreationDefinition>|SetComponentData(…CreationDefinition|__Game_Tools_CreationDefinition_RW_` over `src/Game` returns **one file** (`GenerateEdgesSystem.cs`), against the 34 that reference the type at all. One hit, and it is not the answer.

**(b) The `SharedComponentTypeHandle` gap in the mangled-name pattern** (previous finding): the family has eight `RO`/`RW` shapes and one that carries neither, and the survey's stated pattern names only the eight. A reader enumerating writers of a shared component with `_RW_` finds nothing and concludes nothing writes it.

**(c) The `IComponentData` census, both ways** (namespace-map finding). ~~390 versus 413 in `Game.Prefabs`, 28 versus 48 in `Game.Vehicles`, a 71% spread in the worst case.~~ **Withdrawn (review gate, 2026-08-05): those are the broken pattern's numbers, retracted at the correction above — the true declaration counts are 409 and 47, so there is no spread to illustrate.** The trap this case was meant to show is real and survives without any figure: an anchored declaration pattern cannot see a primary constructor, so it answers a narrower question than the one asked. That is the form the reference ships.

**The rule for the reference.** Before turning a search into a claim about a family, name the family's other members and check the pattern reaches them. Where the family is a C# construct — a generic method with an inferred type argument, a nested type, an interface with several implementing shapes — the check is cheap and the pattern almost always misses one.

Rots: the 1,493/497 `AddComponent` split.

### The counting variant: a census stops where the pattern becomes clear

The same stopping failure produces a wrong **count**, and a count reads better than a claim does.

`docs/solutions/decompile-read-stopped-at-the-confirming-line.md:61-77` records it: `mod-lifecycle-and-ordering` shipped "nine `PreDeserialize<T>`" because the registration block opens with a contiguous run that explains the pattern, and the read ended when the pattern was clear.

Re-derived: `SystemOrder.cs` holds **57** `PreDeserialize<` registrations, at `:738-794`. The nine that made the survey's reader stop are the leading block — six spatial search systems at `:738-743` (`Game.Objects`, `Game.Net`, `Game.Zones`, `Game.Areas`, `Game.Routes`, `Game.Effects`), then `InstanceCountSystem` at `:744` and the pathfinding pair at `:745-746`. The run continues to `PreDeserialize<TutorialTriggerSystem>` at `:794`.

**A count is a claim about a whole span, so derive it from the span.** `grep -c` over the file beats reading until the pattern is clear, and the count and the illustration are two separate claims: give the illustration a narrow citation and the count a wide one.
The over-correction to expect is characterising the rest of the span from the tail you happened to read — the same reference then shipped "the remaining 48 are UI, infoview and rendering systems", and audio, tool, pathfinding, buffer and tutorial systems are in there too.

The reading discipline behind this — read past the line that confirms you, into the rest of the method, the next line, and the caller — is that solutions file's own subject and belongs to whoever is reading rather than to this topic. The counting half is a search decision and belongs here.

### The surfaces that resist search, and what to do instead

**String-literal-driven lookups.** Binding names and localization keys have no type-level trace: `src/Game` holds **880** `AddBinding`/`AddUpdateBinding` call sites, and each names its group and its key as two bare string literals. `src/Game/Game.UI.InGame/ToolUISystem.cs:135` is `new GetterValueBinding<uint>("tool", "allSnapMask", …)`; `src/Game/Game.UI.InGame/ChirperUISystem.cs` has `"chirper", "chirpAdded"`; `src/Game/Game.UI.InGame/GamePanelUISystem.cs` has `"Tool", "GamePanelUISystem"`. Grepping the literal is the only route.

**The literal is also the only bridge to the frontend, and it works — sometimes.** `"allSnapMask"` appears in the C# at `ToolUISystem.cs:135-143` and in the bundle at `src-ui/source.js:46089` (plus `:13091`, `:46211-46214`). Grepping the same string on both sides is what joins the two halves of a binding.
But `"ModLoadingStatus"` and `"ModsLoading"` — the survey's own examples, at `src/Game/Game.Modding/ModManager.cs:240/262/350/357` — return **zero** hits in the bundle. Their absence there means nothing about the frontend, because they are not binding names: they are notification ids consumed in C#.

**And the worst case is a key that exists in no file as a contiguous string.** `src/Game/Game.UI.Menu/NotificationUISystem.cs:166` returns `"Menu.NOTIFICATION_TITLE[" + titleId + "]"`. So the runtime key for the mod-loading notification is `Menu.NOTIFICATION_TITLE[ModsLoading]`, and grepping that string finds it in **neither** the C# nor the bundle. It exists only in the shipped locale data — where a plain byte-grep does find it, twelve of them (next finding).
**A key the game constructs is a key no source-level search can find.** That is the rule, and `Assets` at 30 ids against 12,028 entries (`vanilla-namespaces.md`) is how big the constructed half of the key space is.

**Reflection-driven behaviour, and the good news that it is enumerable.** There is no call graph from `[SettingsUISlider]` to the slider widget. But only **37 files in `src/Game` import `System.Reflection`**, and the list is short enough to hand a reader whole:

`Game.AssetPipeline/AssetImportPipeline.cs`, `Game.Debug/DebugSystem.cs`, `Game.Debug/DebugWatchSystem.cs`, `Game.Input/ProxyBinding.cs`, `Game.Modding/ModManager.cs`, `Game.Modding/ModSetting.cs`, `Game.Prefabs/PrefabBase.cs`, `Game.Prefabs/PrefabSystem.cs`, `Game.Prefabs/ReferenceCollector.cs`, `Game.Prefabs.Climate/OverrideablePropertiesComponent.cs`, `Game.PSI/Telemetry.cs`, five in `Game.Reflection` (`FieldAccessor`, `GetterWithDepsAccessor`, `ObjectWithDepsAccessor`, `PropertyAccessor`, `ValueAccessorUtils`), `Game.Rendering.CinematicCamera/PhotoModeUtils.cs`, `Game.SceneFlow/GameManager.cs`, `Game.Settings/QualitySetting.cs`, `Game.Settings/Setting.cs`, ten in `Game.UI.Editor`, `Game.UI.InGame/PrefabUISystem.cs`, `Game.UI.Menu/AutomaticSettings.cs`, four in `Game.UI.Widgets` (`EditorGenerator`, `EnumFieldBuilders`, `ListAdapterBase`, `WidgetReflectionUtils`), and `Properties/AssemblyInfo.cs`.

The three that matter to a mod author, all **held** against the survey:

- `src/Game/Game.UI.Menu/AutomaticSettings.cs`, 1,897 lines, with 20 reflection call sites — `ReflectionUtils.GetAttribute<SettingsUIAdvancedAttribute>(…)` at `:411`, `GetAttributes<SettingsUISectionAttribute>` at `:774`, `setting.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)` at `:1023`. This is the whole options UI, built from attributes at runtime.
- `src/Game/Game.Modding/ModSetting.cs:32-34` — `keyBindingProperties` is `GetType().GetProperties(…).Where(p => p.CanRead && p.CanWrite && p.PropertyType == typeof(ProxyBinding))`, consumed by `InitializeKeyBindings()` at `:56` and again at `:147`, `:274` and `:283`. The keybinding surface is discovered by property type, so nothing references a mod's binding property by name.
- `src/Game/Game.Modding/ModManager.cs:148` — `TypeManager.InitializeAdditionalTypes(assembly)`, the third registry built by reflection at load.

`Colossal.Reflection` is a single file (`src/Colossal.Core/Colossal.Reflection/ReflectionUtils.cs`); `Game.Reflection` is 15 files of accessor plumbing for the widget and settings machinery.

**Where a static read runs out: the registries the game builds at startup.** `docs/SOURCES.md` entry 8 names the running game as the source for "the contents of any registry the game builds by reflection at startup", and that is exactly the class the three sites above produce. The reference should route a reader there rather than to a wider grep.

Rots: the 37-file reflection list, `AutomaticSettings.cs`'s length, and the `ModSetting.cs` line numbers.

### Searching what the decompile cannot see, without a decoder

Two techniques that answer presence and absence on the non-C# surfaces at almost no cost, and neither is written down anywhere in this repository.

**The `.cok` packages are stored zips, so a raw byte-grep works.**
`grep -a -o "Menu.NOTIFICATION_TITLE\[[A-Za-z0-9]*\]" "Cities2_Data/Content/Game/Locale.cok" | sort -u` returns **thirteen** distinct notification title keys. **Corrected (review gate, 2026-08-05):** this pass first ran the class as `[A-Za-z]*` and reported twelve, missing `Menu.NOTIFICATION_TITLE[CS1TreasureHunt]` on the digit alone — this topic's own family trap, landing on its own enumeration. The twelve are — `ActionRequired`, `ActionResolved`, `DLCContent`, `EnabledModsChanged`, `KeyBindingConflict`, `ModsLoading`, `PDXAccount`, `PDXDataSyncConflict`, `SavingGame`, `ScreenshotTaken`, `Toolchain`, `VTBackgroundLoading` — with no zip reader, no `BinaryReader`, and no decode.
`docs/SOURCES.md` entry 4 documents the decoder and `method-decoding-shipped-locale-data.md` carries the recipe; neither says that a plain grep settles existence. For "does this key exist" and "what keys are in this family", it does, and the whole cost is one command.
The same works on `resources.assets` for type names (previous finding): `ServiceConsumption` and `BuildingPrefab` both hit, while field names return zero, which is the negative that entry 5 predicts. The counts live with that finding, under its correction.

**The reformatted UI bundle is a grep target like any other**, and `docs/SOURCES.md:40-42` already states the one thing that misleads: `grep -c` over the minified `index.js` answers 1 or 0 whatever the truth, because it counts matching lines and there is one line. `grep -o … | wc -l` gives the real count. The reformatted copy is for citing line numbers, not for establishing presence.

### The exclusion list, measured

The survey's standing exclusions are `src/mscorlib`, `src/System*`, `src/UnityEngine*`, `src/Unity.RenderPipelines*`, `src/Newtonsoft.Json`, `src/Colossal.Mono.Cecil`, `src/PDX.SDK`, plus four filename patterns.
That set covers **13,527 of 22,984 files — 58.9%**.

Extending it with the rest of Tier C — `src/netstandard`, `src/Mono.*`, `src/Microsoft.*`, `src/PDX.ModsUI`, `src/com.rlabrecque.steamworks.net`, `src/ICSharpCode.SharpZipLib`, `src/Cinemachine`, `src/DiscordSDK`, `src/Unity.TextMeshPro`, `src/Unity.VectorGraphics`, `src/Unity.Timeline`, `src/Colossal.OdinSerializer`, `src/*TestScenarios`, `src/Colossal.TestFramework`, `src/DryDock.Runtime` — brings it to **15,207 files, 66.2%**, leaving 7,777.

**The four filename patterns are worth more than their file count suggests.** `**/UnitySourceGeneratedAssemblyMonoScriptTypes_v1.cs`, `**/-BurstDirectCallInitializer.cs`, `**/__JobReflectionRegistrationOutput__*.cs` and `**/AssemblyTypeRegistry.cs` are 103 files and **165,834 lines** — and they include the two largest files in `src/Game`, at 44,121 and 20,709 lines, against 9,326 for the largest real one (`Game.Net/LaneSystem.cs`). A grep for a common identifier without them spends most of its output on generated tables.

`**/AssemblyInfo.cs` should **not** be excluded despite being 163 near-identical files: it is where the version lives.

Rots: both coverage percentages.

### A search order

Re-derived from the recipes above, in the order that costs least first. This is the survey's §5.5 with the measurements attached and two steps added.

1. **A type name known** → `Glob src/**/<Name>.cs`. One hit 96.5% of the time in Tier A. More than one → qualify by namespace directory; still more than one → qualify by assembly (needed exactly twice: the `Colossal.IO` twins).
2. **"When does this run" / "what runs in phase P"** → `Grep "<Name>>"` or `Grep "SystemUpdatePhase.<P>"` in `src/Game/Game.Common/SystemOrder.cs`. It holds all 1,012 registrations in the game, so a miss there is a real absence — with the caveat that `PreSimulation` is pumped and empty.
3. **"What data does this system touch"** → read the nested `TypeHandle` struct and the `OnCreate` `EntityQueryDesc`. **"Who reads / writes component C"** → `Grep "__<Namespace_With_Underscores>_<C>_RO_"` and `_RW_` across `src/Game`, remembering that shared-component handles carry neither segment and that command-buffer writes carry no type name at all.
4. **"What components does prefab type P produce"** → `Grep "GetArchetypeComponents" src/Game/Game.Prefabs/P*.cs` (374 sites in that directory).
5. **"How do I expose Y to the UI"** → find a comparable `UISystemBase` in `Game.UI.InGame` and read its `AddBinding` calls; the binding types are all in `Colossal.UI.Binding`.
6. **The search came back empty** → before concluding absence, run the four checks: is it a `const` (search the value); was the search scoped (state the span or widen it); does the subject live outside C# (frontend, shipped data, a constructed string — go to the install); does the pattern name the whole family (a generic call site with an inferred type argument names nothing).
7. **The search came back with one hit** → treat that as a question about the pattern rather than as a finding.
8. **A count is wanted** → derive it from the whole span with `grep -c` or `rg --count-matches`, and cite the illustration separately from the census.

### Feedback: the mod catalog

The sweep this obligation asks for came back **almost** empty, as expected — this topic has no corpus half. Three items surfaced; two are worth the maintainer's eye and neither is a technique the catalog is missing, and the third is recorded only so the next pass does not chase it.

**1. `ruzbeh0/Time2Work` — the corpus's only pasted decompiler output.**
`Time2Work/NightShift/Systems/Time2WorkResourceBuyerSystem.cs:1-3` opens

```
// Decompiled with JetBrains decompiler
// Type: Game.Simulation.Time2WorkResourceBuyerSystem
// Assembly: Game, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
```

It is the **only** file in twenty-two repositories carrying a decompiler header, established by `rg --no-ignore -g '*.cs' '// Decompiled with'` over the whole corpus root.
Candidate sentence for its **Demonstrates** entry: _"Its largest fork ships with the decompiler's own header comment intact, which is the plainest evidence in the corpus that the fork technique starts as a literal paste of decompiled output."_
Against adding it: the entry already covers forking at length, including the type-handle point, and this adds provenance rather than a technique.
**Ruled (2026-08-05, orchestrator).** Not added: provenance rather than a technique, on the entry's own reading.

**2. `toverux/HallOfFame` — the frontend reconstructed as checked-in declarations.**
`HallOfFame/HallOfFame/UI/src/vanilla-modules/` mirrors the game's own module paths as local TypeScript files — `game-ui/common/hooks/use-scroll-controller.ts`, `game-ui/common/input/toggle/checkbox/checkbox.tsx`, `game-ui/common/tooltip/description-tooltip/description-tooltip.ts`, `game-ui/overlay/logo-screen/loading/loading-progress.ts` — imported by path from the mod's components (`HallOfFame/HallOfFame/UI/src/area-game/screenshot-upload-panel/panel-image.tsx:6`, `panel-info-form.tsx:15/19`, `upload-progress.tsx:8`).
Beside them, `HallOfFame/AGENTS.md:59` tells an agent where to look for both halves of the game: the C# at `../DecompiledCitiesSkylines2`, the UI at `HallOfFame/UI/vanilla-modules.source.js`. That second file is tracked and present, 119,304 lines against my reformatted 135,021 — a vendored bundle copy like `CS2-Platter`'s, and stale in the same way. (The two `ignorePatterns` entries naming it, `HallOfFame/oxfmt.config.ts:6` and `HallOfFame/oxlint.config.ts:11`, are formatter and linter exclusions rather than gitignore; `git ls-files` resolves the path.)
Candidate sentence: _"Reconstructs the vanilla module tree it extends as checked-in declaration files under the game's own `game-ui/…` paths, so a module the bundle exposes and no shipped declaration names becomes a typed import."_
Against adding it: the entry's existing line about "extending vanilla React components the module registry exposes and no shipped declaration file names" already implies the mechanism. This names the artifact.
**Ruled (2026-08-05, orchestrator).** Added, re-derived first-hand and rewritten, because the existing line states the _problem_ and not what to do about it. Each local file is a guarded runtime accessor rather than a declaration — `getModuleExport('game-ui/common/hooks/use-scroll-controller.tsx', 'useScrollController', <type guard>, undefined)` at `HallOfFame/HallOfFame/UI/src/vanilla-modules/game-ui/common/hooks/use-scroll-controller.ts:4-10` — and mirroring the vanilla path as the local path is what makes the call site an ordinary import (`panel-info-form.tsx:15/19`, `panel-image.tsx:6`, `upload-progress.tsx:8`). Shipped sentence: _"One local module per vanilla module it imports, filed under that module's own `game-ui/…` path and resolving the export through the registry behind a type guard and a fallback, so a module no declaration file names still reads as an ordinary typed import at every call site."_

**3. `algernon-A/CS2-Platter` — recorded, not proposed.** `CS2-Platter/Platter/UI/tools/source.js` is 123,754 lines, a beautified copy of the game's UI bundle vendored with no version stamp. My own reformatted copy of the 1.6.0f1 bundle is **135,021** lines — an 11,267-line gap, which is what a stale copy looks like. `conflicts.md`'s key-namespace entry already records this and `docs/SOURCES.md`'s "what looks like a source and is not" already bars it. Nothing to add.

### Feedback: `docs/SOURCES.md`

Four amendments, in the order they cost the reader.

**1. Entry 1 should say the decompile states its own version.**
`SOURCES.md:9` reads "The install is the one that carries a version the agent can state", and entry 1 says nothing about a version at all. The decompile carries it exactly, at `src/Game/Properties/AssemblyInfo.cs:19` — `[assembly: VersionInternal("1.6.0f1 (419.d6c6) [6216.19404]")]` — with three corroborating assemblies sharing the changelist. The line below it, `AssemblyVersion("0.0.0.0")`, is repeated by 131 assemblies and is what makes the checkout look version-blind.
Proposed addition to entry 1: _"It states its own version: `src/Game/Properties/AssemblyInfo.cs` carries a `VersionInternal` attribute with the game version, changelist and build. The `AssemblyVersion` attribute on the line below it reads `0.0.0.0` in 131 of the 163 assemblies and settles nothing."_
The precedence sentence at `:9` should narrow to what it is actually about — the install carries a version for the **frontend and the shipped data**, which the decompile does not cover.

**2. Entry 4 should say a byte-grep answers existence without decoding.**
Entry 4 documents the `.loc` format and points at the decoder. A plain `grep -a` over `Locale.cok` returns whole keys — `Menu.NOTIFICATION_TITLE[ModsLoading]` and its eleven siblings — because the package is a stored zip and the payload is uncompressed. That answers "does this key exist" and "what is in this key family" at zero cost, and a pass that reaches for the decoder to answer either has overpaid.
Proposed addition: _"Because the package is stored rather than deflated and the payload uncompressed, a raw byte-grep over the `.cok` finds a key by name without any decoding. Use it for existence and for enumerating a key family; the decoder is for counts and for the whole table."_

**3. Entry 1 should name `src/.idea/`.**
Entry 1 says "Reach it under the checkout's `src/`, one directory per assembly." In this checkout `src/` also holds `src/.idea/`, an IDE settings folder, which is the difference between a directory count answering 164 and the true 163, and the only thing in the tree nesting past two levels. One clause.

**4. Entry 3 should name the CSS copy.**
Entry 3 covers `index.js` and its reformatted copy in detail and names `index.css` only in the file list. The same checkout carries `src-ui/source.css`, 24,902 lines, reformatted the same way, and no entry says it exists or what its line count is. A topic needing a computed style or a class name has a first-party artifact on disk that the list does not point at.

`docs/SOURCES.md` was otherwise accurate everywhere this pass touched it: entry 5's split between content-pack prefabs and `resources.assets` reproduced exactly (type names present, field names absent), and entry 3's `grep -c` warning is correct for the reason it gives.

**Ruled (2026-08-05, orchestrator).** Two applied, two rejected. Applied: the `VersionInternal` line with the 131-assembly `0.0.0.0` decoy, which entry 1 now owns along with the version clause the precedence sentence at `:9` gave up; and the byte-grep-without-decoding fact on entry 4.
**Proposals 3 and 4 above are wrong and must not be re-applied.** `src/.idea/`, `src/Solution.sln` and `src-ui/source.css` exist only in the maintainer's own checkout — the first two because it was opened in a JetBrains IDE, the third because the CSS was reformatted by hand. `cs2-modding-setup/SKILL.md` provisions `src/` with `ilspycmd` per DLL and reformats `index.js` alone, so a provisioned decompile has none of the three. This pass confirmed each "on disk" and mistook one machine's state for the decompile's, which is the same error the topic's own fourth trap describes.

### The checkout's own orientation prose has been deleted since the ruling that named it

`conflicts.md`'s ruled entry "An orientation document in one decompile checkout teaches an ordering mechanism the game does not use" (ruled 2026-08-02, the mod-lifecycle-and-ordering pass) cites `DecompiledCitiesSkylines2/AGENTS.md:56` and `DecompiledCitiesSkylines2/docs/game.md:9`.

Neither exists in the working tree now. `docs/cohtml.md`, `docs/colossal.md` and `docs/game.md` were deleted and `AGENTS.md` cut from **64** lines to **14**, committed in `565e22b7` and `190766c4`. The current `AGENTS.md` is a two-section orientation note with no modding guidance in it at all.

Both cited lines are verifiable at commit `ec7c3720`, which `HEAD` has since moved three commits past — cite the SHA, not `HEAD`. `git show ec7c3720:AGENTS.md` line 56 is "**Simulation Hooks**: Most simulation logic is in `Game.Simulation`. Use `[UpdateAfter]` or `[UpdateBefore]` to inject custom systems.", and `git show ec7c3720:docs/game.md` line 9 offers `Initialization`, `Simulation` and `Rendering` as `SystemUpdatePhase` examples, of which only `Rendering` exists (`src/Game/Game/SystemUpdatePhase.cs:20`).

The ruling is unaffected — it was already ruled that shipped prose states the trap as a plain negative fact about the game and names no document. What has changed is the `**Established.**` section's exposure claim. An `**Addendum**` has been appended to that entry (see `## Dead ends`, last item).

This pass proposed a durable consequence — _hand-written prose sitting in a decompile checkout is not part of the decompile_ — and **the review gate cut it from the shipped file** (orchestrator, 2026-08-05). The reasoning that justified it is what refutes it: `cs2-modding-setup/SKILL.md:70-93` provisions `src/` alone, so a provisioned decompile contains no `.md` for a reader to hit, and the only checkout where the hazard was ever real is the maintainer's own — whose orientation notes were themselves deleted in `565e22b7`. A warning about a situation the provisioning path cannot produce costs every reader context and protects nobody.

---

## Bridge

This topic bridges to **every** reference in the plugin, because it is the re-check path any claim goes through. A reader arriving at any reference with a type name, a message string or a field name and wanting to confirm it against 1.6.0f1 comes here for the route. That is the general bridge; the specific ones below are where a mechanics or technique topic needs a named artifact from this file rather than the general habit.

**`diagnostics`** — the named partner, and the traffic runs one way into this topic. A reader arrives holding a type name or a message string out of a log line and needs to find it. Three routes this file establishes serve exactly that:
the name-glob (`src/**/<Name>.cs`, one hit 96.5% of the time in Tier A);
`SystemOrder.cs` for whether the system that logged it runs at all and in which phase;
and the constructed-string rule for a message that is assembled rather than written — `src/Game/Game.UI.Menu/NotificationUISystem.cs:166` builds `"Menu.NOTIFICATION_TITLE[" + titleId + "]"`, so a log line quoting the whole key is unfindable by grep and the `.cok` byte-grep is what finds it.
`diagnostics.md:584` is the only research file that currently names this topic.

**`ecs-in-this-game`** — the mangled `TypeHandle` field names are how a reader answers "who reads and who writes this component" without a call graph, and the eight `RO`/`RW` shapes with their populations are the vocabulary. The limits belong there too: shared-component handles carry no access segment, a command-buffer write carries no type name at all (`ObjectToolBaseSystem.cs:1270`), and `_RW_` records the access the generator saw rather than an actual write, so a system that only reads still carries one wherever it took a read-write default. The `IJobChunk`/`IJobEntity` census (771 / 0) is that topic's and was re-verified here.

**`mod-lifecycle-and-ordering`** — `SystemOrder.cs` is that reference's spine, and this file establishes the property that makes it trustworthy: it holds **all** 1,012 registrations in `src/Game`, so absence from it is a real absence. Two facts feed straight in: `PreSimulation` is registered by nobody and pumped anyway (`SimulationSystem.cs:168/272`), and the 57 `PreDeserialize<T>` at `:738-794` is that reference's own corrected count.

**`placement-definitions`** — the scoped-grep trap's worked case is `CreationDefinition` (34 files across eight namespace directories, 25 of them in `Game.Tools`), and the family trap's worked case is that `AddComponent<CreationDefinition>` returns zero while every tool adds it. Both are that reference's material seen from the search side.

**`custom-tools`** — `kSnapAllIgnoredMask` at `ToolBaseSystem.cs:98` and its inlined consumer at `ToolUISystem.cs:142` is the const trap's headline case and is that reference's own corrected claim. The reference should not restate the correction; this topic teaches the mechanism that produced it.

**`localization`** — the 72% figure is re-derived here (21 of 75 groups named as C# string literals) and is this topic's proof that a C# grep cannot survey the key space. The constructed-key rule (`Assets` at 30 ids and 12,028 entries) is why. The `.cok` byte-grep is the cheapest check a reader can run against that reference's table.

**`binding-layer`** and **`frontend-and-injection`** — the 880 `AddBinding` sites, the string-literal shape (**corrected, review gate 2026-08-06: the _key_ is always a literal, the _group_ often is not** — the info-panel sections write `new TriggerBinding(group, "toggle", …)` against the abstract `group` property in `Game.UI.InGame/InfoSectionBase.cs:38`, so a group-name grep misses every section that inherits it), and the fact that the literal is the **only** link between the C# and the bundle: `"allSnapMask"` in both (`ToolUISystem.cs:135`, `src-ui/source.js:46089`), `"ModLoadingStatus"` in one (`ModManager.cs:240`, absent from the bundle). Also the hard boundary: 1,386 `game-ui/…` module paths in the bundle, zero occurrences of `game-ui/` in 22,984 C# files, so nothing about the module registry is derivable from the decompile.

**`ui-build-and-devloop`** — the same boundary decides where that reference's reader looks. The install's `Cities2_Data/Content/Game/UI/` is the whole first-party frontend (`index.js` 2,219,232 bytes, `index.css` 462,876, plus `index.html`, `gameui.uiHost`, `Fonts/`, `Media/`, `Static/`), the reformatted copies at `DecompiledCitiesSkylines2/src-ui/source.js` (135,021 lines) and `source.css` (24,902) are what make a citation possible, and `grep -c` over the shipped one-line bundle is the counting trap (`docs/SOURCES.md:40-42`). Two corpus mods carry their own copies and both are stale — `CS2-Platter/Platter/UI/tools/source.js` at 123,754 lines and `HallOfFame/UI/vanilla-modules.source.js` at 119,304, against the install's 135,021 — which is why the install rather than a repository is the source.

**`settings-and-input`** — `AutomaticSettings.cs` (1,897 lines, 20 reflection sites) and `ModSetting.cs:32-34`'s property-type scan are the two places where there is no call graph to follow, and the 38 `SettingsUI*Attribute` files are the vocabulary a reader enumerates instead. The 37-file reflection list is where that reference's "no static route" claims stop.

**`prefabs-and-assets`** — `Game.Prefabs` is 1,274 files and declares 409 `IComponentData` types (not 390 — see the correction above), 280 `ComponentBase` subclasses and 112 `PrefabBase` subclasses; `GetArchetypeComponents` is declared 373 times there. `PrefabSystem.cs`, `PrefabBase.cs` and `ReferenceCollector.cs` are three of the 37 reflection files, which is why a prefab's component set is a runtime question — and an archetype is assembled up a base chain plus the attached `ComponentBase` list, so no single file answers what a prefab type produces. Base-game prefab values are in `resources.assets` with type names and no field names; update and pack content is in `Prefabs_*.cok`.

**`save-serialization`** — `src/Game/Game/Version.cs` (826 lines of `VersionConstant`) is in the root `Game` namespace directory rather than in `Game.Serialization`, which is the layout lie most likely to cost that reference's reader a search.

**`patching`** — a patch target has to be named exactly, so the disambiguation rule is that reference's precondition: qualify a colliding type by namespace directory, then by assembly. `src/Game` holds 149 duplicated basenames and 33 registrations the game itself has to write fully qualified.

**`performance-and-memory`** — the Burst mangled names (`Game_002ERendering_002EDequeueAndSort_00004B5A_0024BurstDirectCall`, `AssemblyInfo.cs:14-18`) are what a reader chasing a Burst-compiled method greps for, from the method name onward. **Corrected in place (review gate, 2026-08-26): this line read that the names "are a dead end" and that the decode "gets them back to `Game.Rendering.DequeueAndSort`".** Both were retired by the 2026-08-05 correction above and survived here: the mangled name is what resolves, and the decoded form is what matches nothing, since the encoding omits the declaring type.

**`debug-menu`** — `Game.Debug` is 69 files with 29 `: BaseDebugSystem` subclasses, and `Game.Debug/DebugSystem.cs` (7,776 lines, the third-largest real file in `src/Game`) is both a reflection user and a `Citizen` writer, which is why it turns up in searches that have nothing to do with debugging.

**Every mechanics topic** — `citizens-and-households`, `zoning-buildings-and-land-value`, `economy-and-companies`, `utilities-and-flow-networks`, `city-services-and-coverage`, `roads-and-traffic`, `transportation-and-vehicles`, `environment-and-pollution`, `city-state-and-progression`, `simulation-time-and-units`, `units-and-formatting`, `mod-compatibility` — reaches its own material through the namespace map above and confirms it through the `_RW_` sweep. `simulation-time-and-units` gets one specific gift: `kTicksPerDay = 262144` at `TimeSystem.cs:18` has no name-level consumers and 135 value-level ones, so every time-derived constant in that reference has to be found by value.

---

## Dead ends

**`conflicts.md` names this topic nowhere, ruled or open.** Confirmed by grep for the slug across the whole repository before this pass wrote anything: three files carried it — `docs/solutions/empty-grep-read-as-proof-of-absence.md:71` (which scopes this reference to carry the three variants), `docs/research/diagnostics.md:584` (a bridge line) and the shipped `plugins/cs2-modding/skills/cs2-modding/references/technique/diagnostics/diagnostics.md` (the same bridge, shipped). `conflicts.md` was not among them. There was no entry to read and none to honour; the one occurrence it carries now is the addendum this pass appended.

**The wiki.** Not fetched, and nothing on it bears on this topic. `survey-wiki-inventory.md` records no page about reading decompiled game code, and the wiki's own subject is process — toolchain, debugging, publishing, key bindings, options. A page telling a modder how to navigate a decompile would be a page about a tool the wiki does not distribute. Recorded as checked-and-empty rather than skipped.

**The mod corpus has no half of this topic, by construction.** Swept anyway, three ways, and the result is the near-empty return the ticket predicted. `rg --no-ignore -g '*.cs' '// Decompiled with'` over all 22 repositories returns **one** file. A case-insensitive sweep for `ilspy|decompil` returns six paths. Three are noise (`ExtraDetailingTools/UI/node_modules/sass/sass.dart.js`, `ExtraDetailingTools/UI/node_modules/ts-loader/dist/after-compile.js`, `NodeController/NodeController.sln.DotSettings.user`). Two are the items recorded above (`Time2Work`, `HallOfFame/AGENTS.md:52-60`). The sixth is `LineTool-CS2/Code/Systems/CreateDefinitions.cs:32-36`, a StyleCop suppression justified "Decompiled game code." on a Burst-compiled port of the game's own definition pipeline — already covered by that mod's existing catalog entry, so nothing to add.
No repository documents how to search a decompile, and the catalog's `Demonstrates` half certifies modding techniques rather than reading ones, so there is no entry to match on. **Do not walk this again.**

**Whether the game's own source ever names a mangled `TypeHandle` field outside a system.** Searched: the mangled family appears only inside `TypeHandle` structs and their `__AssignHandles` bodies. Nothing consumes a mangled name from outside the declaring system, which is why the field name is a reliable per-system index and not a cross-system one.

**Whether a `SharedComponentTypeHandle` field ever carries an `_RO_`/`_RW_` segment.** Searched across `src/Game` with `__[A-Za-z0-9_]*SharedComponentTypeHandle`; every hit is `__<Namespace>_<Type>_SharedComponentTypeHandle`. Not a gap in the search — a property of the generator.

**Whether `__…EntityTypeHandle` exists as a namespaced mangled name.** It does not. Entity handles are `__Unity_Entities_Entity_TypeHandle` (`src/Game/Game.Tools/ToolBaseSystem.cs:90`) and `__EntityStorageInfoLookup` carries no segments at all.

**Whether the twelve un-decompiled DLLs hold anything.** Not opened. Named as `Unconfirmed:` above with the artifact that would answer it (`ilspycmd` over one of them). None is reachable from a mod, so the answer changes nothing.

**Whether the checkout's deleted `docs/*.md` were ever read by anything in this plugin.** Checked: `plugins/cs2-modding/skills/cs2-modding-setup/SKILL.md:70-93` provisions `src/` alone. No shipped path delivers that prose, which is what the mod-lifecycle-and-ordering pass's ruling already established and what makes the deletion a change to the evidence rather than to the product.

**The running game was not used and did not need to be.** Every question this topic asks is about what is on disk. The one place a live source would help — enumerating a reflection-built registry's contents — belongs to the topics that own those registries, and `docs/SOURCES.md` entry 8 already routes there. No live question was recorded as unanswerable.

**Appended to `conflicts.md`.** One addendum, on the ruled entry "An orientation document in one decompile checkout teaches an ordering mechanism the game does not use": the two cited files are deleted and `AGENTS.md` is cut from 64 lines to 14, so the entry's `**Established.**` exposure claim no longer describes the checkout. Both lines remain verifiable at commit `ec7c3720` and the ruling is untouched, since it was already ruled to state the trap as a plain negative fact naming no document. Nothing new was opened as an entry: this pass produced no question a source could not settle.

## Re-sweep 2026-08-26: Unity's documentation (ticket 38)

**This file was named in neither of ticket 38's lists**, and was swept under the ticket's own rule that a file scoped out which turns out to assert engine behaviour is a finding. The assembly map, the grep recipes and the decompiler's own artifacts stayed out of scope — those are facts about ILSpy's output and about this checkout, and no Unity page speaks to them. What was swept is everything the two files claim about **what the DOTS generators and Burst's IL post-processor emit**, since those ship as source in the package cache and are therefore checkable by construction rather than by sampling.

Unity docs fetched live 2026-08-26 at the version-pinned URLs `SOURCES.md` entry 13 fixes, Burst at `@1.8`; decompile read the same day at 1.6.0f1; package sources (entry 15) at `com.unity.burst@1.8.23` and `com.unity.entities@1.3.10`. No live game was used.

- **The Burst mangled block is eight hex digits, not four, and the shipped grep recipe was built on the wrong width.** The post-processor builds it as `$"_{burstCompileMethod.MetadataToken.RID:X8}"` (`com.unity.burst@1.8.23/Unity.Burst.CodeGen/ILPostProcessing.cs:670`), appended before `"$BurstDirectCall"` (`:727`, the constant at `:41`). `X8` is hex zero-padded to eight, and the value is the method's row id in the assembly's `MethodDef` table. Every instance in this build is eight digits — five in `src/Game/Properties/AssemblyInfo.cs:14-18` (`DequeueAndSort_00004B5A_…`, `CopyWaterValuesInternal_000065EB_…`, `_000065F2_…`, `SimplexNoise_00009354_…`, `PerlinNoise_00009355_…`) and three more in `src/Unity.Burst/Properties/AssemblyInfo.cs:14-16`. Eight for eight. **This was not a docs-versus-decompile question at all** — the docs name the post-processor and never the mangling — it was a shipped sentence that miscounted, and the count is load-bearing because the next line tells the reader to grep the mangled name: a pattern built on four hex matches none of the eight names in this build. The shipped sentence now also says what the block *is*, so a reader knows it moves when the method table shifts rather than treating it as a stable id.
- **The missing declaring type has a cause, and the cause confirms the shipped rule rather than moving it.** The post-processor creates the generated class with the *declaring type's namespace as its own* `Namespace` and only the method in its `Name`, then nests it (`ILPostProcessing.cs:727-739`, the nesting at `:739`) — which is exactly why an ILSpy-rendered `typeof(...)` yields namespace-plus-method and no declaring type. `src/Game/-BurstDirectCallInitializer.cs:11-15` names the declaring type, and the class is declared nested at `src/Game/Game.Rendering/WaterRenderSystem.cs:81`. No change; recorded because a claim verified reads differently from one nobody checked, and because this mechanism is what stops a later pass "correcting" it.
- **`_RW_` is write *access*, not writes, and the shipped complement was drawn on one side only.** The access segment is decided by one boolean at the call the generator saw, never by what the code does with the handle: `$"__{Type}_{(IsReadOnly ? "RO" : "RW")}_…"` in `ComponentLookupFieldDescription.cs:32`, `BufferLookupFieldDescription.cs:31`, and `TypeHandleFieldDescription.cs:97/107`. The counterexample is in the game's own code: `src/Game/Game.Rendering/EditorGizmoSystem.cs` declares **both** handles on `SubObject` (`:315` read-only, `:317` read-write), uses the read-write one once to copy the buffer into a `NativeArray` a read-only gizmo job walks (`:457`, `:70-72`), and writes `SubObject` nowhere. So the system appears in an `_RW_` sweep and is not a writer. Reach, reported, on whether a third limit belongs beside the two the file already gives: `src/Game` has **zero** direct `GetComponentTypeHandle<…>(false)` and zero direct `GetComponentLookup<…>(false)` outside `InternalCompilerInterface`, so every read-write handle in the game is generator-emitted and the two stated limits are the right two on that side.
- **The shipped set of eight was cleared by construction rather than confirmed, which is the discipline this plugin's sets have failed before.** The brief was to build a ninth from the generator source, not to check the eight given. The generators ship under `com.unity.entities@1.3.10/Unity.Entities/SourceGenerators/Source~/`, and every `TypeHandle` field name is one string interpolation in a field-description type, so reading them enumerates the family by construction — which no grep of `src/Game` can do. **The answer is four more access-bearing shapes** — `AspectTypeHandle` and `AspectLookup`, each in `RO` and `RW` (`TypeHandleFieldDescription.cs:92/118`, `AspectLookupFieldDescription.cs:32`) — **plus two kinds carrying no access segment**: `__<Type>_<WithDefaultQuery|WithoutDefaultQuery>_JobEntityTypeHandle` (`JobEntityQueryAndHandleDescription.cs:29`), which is the one that would defeat a `_RW_` sweep by putting `WithDefaultQuery` or `WithoutDefaultQuery` in the slot a reader expects `RO` or `RW` in, and the IFE container handle (`IfeTypeHandleFieldDescription.cs:24`). **And not one of them appears in this build**: `_AspectTypeHandle`, `_AspectLookup` and `_JobEntityTypeHandle` return zero across the whole of `src/`, and a terminal-segment census over the distinct `__…` identifiers in `src/Game` returns exactly the four access-bearing families, the shared-component shape with no access variant, the two escapees the file already lists, and the `__query_<hash>_<n>` fields. So the shipped eight are **complete for this checkout and are not the family**, and the shipped sentence now says which of the two it is — the honest fix, given that the file's subject is navigating *this* decompile. That a build using aspects or the per-entity job interface would carry the other four is what makes the zero result a measurement rather than an assumption.
- **`__AssignQueries` is almost always the no-op, and the query fields it does assign had no entry.** The exact one-line body dominates by a wide margin across `src/Game`. **The census figures did not ship** — "frequently" became "almost always", which is what a reader acts on, and a proportion swept across an assembly is a search result. What did ship is the gap: the catalogue named `__AssignQueries` and never the `__query_<hash>_<n>` fields it assigns (for example `__query_350738572_0`, declared at `src/Game/Game.Rendering/WaterRenderSystem.cs:195`, assigned at `:642`), which a reader grepping `__` inside a system meets beside the handle fields with nothing explaining them.
- **One clause judged and not shipped, for the maintainer.** `InternalCompilerInterface` carries a second, smaller family the `Get*` glob does not name — `HasComponentAfterCompletingDependency`, `GetBufferAfterCompletingDependency`, `HasBufferAfterCompletingDependency`, `GetComponentAfterCompletingDependency`, `DoesEntityExist` — the codegen form of the single-entity `SystemAPI` reads. By the file's own standard of telling machinery from meaning, item 2 is complete without them. The argument for a clause is that "Ignore the machinery" read across to item 2 tells a reader to skip a line that drains the system's dependency chain, which is `performance-and-memory`'s subject since ticket 37. The argument against is that this file has kept runtime costs out entirely, and adding one pushes a sibling's material into a catalogue that does not want it. Left out; the maintainer owns it.
- **Source-list feedback.** Entry 13 held on every URL rule. Three amendments went in, shared with the `patching` sweep: `@1.8` for Burst, that a branch URL serves that branch's newest patch so the page header is where the served version is read, and a softening of "never authoritative for API shape" that keeps the precedence rule while allowing the API reference as a lead. **The larger gap was entry 15's**, and this file is the clearest case for it: the DOTS generators and Burst's IL post-processor ship as source there, and for anything they *emit* the generator is the definition where the decompile is only a sample. Three of this pass's findings — the eight-digit width, the access-mode mechanism, and the completeness of the shape set — were settled that way, and none could have been settled from the checkout.

## Review gate 2026-08-26: the set was wrong at its edge, and its marker named a location that could not re-derive it

Two corrections and a marker split, all from the generator sources rather than from `src/Game`.

- **"The method's own metadata token" is the RID, not the token.** Burst builds the suffix as `$"_{burstCompileMethod.MetadataToken.RID:X8}"` (`com.unity.burst@1.8.23/Unity.Burst.CodeGen/ILPostProcessing.cs:670`), spliced at `:727` before the `"$BurstDirectCall"` constant at `:41`; the generator's own comment at `:726` reads `// private static class (Name_RID.$Postfix)`. Every instance in this build has a zero high byte, which a MethodDef token — high byte `0x06` — cannot. A reader holding a token from a decompiler would compose `DequeueAndSort_06004B5A_0024BurstDirectCall` and grep for a name that does not exist, and the next line of that paragraph is the instruction to grep.
- **The shipped set omitted the IFE container handle.** Constructed rather than confirmed, as `plugins/cs2-modding/AGENTS.md` requires of a set: every field of a generated `TypeHandle` struct is written from `TypeHandleStructNestedFields` (`SourceGenerators/Source~/SystemGenerator.Common/QueriesAndHandles.cs:14`), emitted by its only two writers (`:103-129`, `:165-198`), so the set is closed by the implementers of `IMemberDescription` — all of them under `INonQueryFieldDescriptions/`, and each one's `GeneratedFieldName` interpolation is one name shape. The one outside the shipped list is `__{ContainerTypeFullName}_TypeHandle` (`IfeTypeHandleFieldDescription.cs:24`), built for a `SystemAPI.Query` foreach from `IFE_{UniqueId}_{n}` (`SystemGenerator.SystemAPI.Query/IfeDescription.cs:345`). It carries no namespace, type or access segment, so neither shipped bullet caught it — and it is in this checkout, at `src/Unity.Scenes/Unity.Scenes/WeakAssetReferenceLoadingSystem.cs:274/276` and `src/Unity.Transforms.Hybrid/Unity.Entities/CompanionGameObjectUpdateTransformSystem.cs:189`.
- **The marker named `src/Game`, where none of the beyond-this-build shapes appears.** `_AspectTypeHandle`, `_AspectLookup` and `JobEntityTypeHandle` return zero files across the whole decompile, and `_IFE_` returns only the two files above — nothing under `src/Game`. A sweep opening only what the marker named could never confirm, extend or retire the clause, which is how the clause came to omit a shape. The marker is now split per claim: `src/Game` for which shapes this build uses, and the package's field-description directory for the scheme's full list. It also read "the two shapes it takes" while closing a passage stating two shapes plus four exceptions.

**Corrected (review gate, 2026-08-26): the four-hex claim in the Burst mangled-names bullet above.** That bullet still read "the four-hex block is a compiler-assigned id" under two dated corrections that left the width and the identity untouched — the shape repo `CLAUDE.md` warns about, where appending a note is not editing the original. Both halves are retired: the block is eight hex digits, and it is the method's RID.
