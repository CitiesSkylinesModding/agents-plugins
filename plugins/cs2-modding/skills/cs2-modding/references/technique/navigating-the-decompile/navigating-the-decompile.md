# Navigating the decompile

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Every route below is a search over that tree, so without one there is nothing here to run.
`cs2-modding-setup` provisions it.

How to find a type, a system, a component, a binding or a string in a decompiled tree of tens of thousands of C# files — and what a search that comes back empty is actually worth.

This is the re-check path: every claim in every other reference here is a claim about the game, and confirming one against the installed version means running one of the routes below.

Two lookups sit beside this file: [assembly-and-namespace-map.md](assembly-and-namespace-map.md) for which namespace owns what, and [decompiler-artifacts.md](decompiler-artifacts.md) for the full artifact catalogue.

## The reading universe is ten assemblies, and everything else is noise

`src/` holds one directory per assembly, and the provisioning command decompiles every `.dll` in the install's `Cities2_Data/Managed/` — so the tree covers the whole game rather than a chosen subset of it.

**In a tree provisioned that way, absence from `src/` is a fact about the game, not about the decompiler.**
Nothing managed is skipped, so a name nowhere in `src/` is nowhere in the game's own code, with no unread assembly left to blame.
That guarantee belongs to the provisioning and not to the decompiler: in a tree somebody trimmed by hand afterwards, an absence is a fact about the trimming instead, and `cs2-modding-setup` is where a reader settles which tree they have.

**Ten assemblies are the reading universe:**

| Assembly | What it owns |
| --- | --- |
| `Game` | Simulation, prefabs, tools, UI, the modding API, `SystemUpdatePhase`, `GameSystemBase`, `SystemOrder`. |
| `Colossal.Core` | `COSystemBase`, `Colossal.Entities`, `Colossal.Serialization.Entities`, `Colossal.Json`, `Colossal.Reflection`. |
| `Colossal.IO.AssetDatabase` | Mod discovery and loading, `ExecutableAsset`, the asset databases, `LocaleAsset`, `PrefabAsset`. |
| `Colossal.UI.Binding` | The whole C#↔JS binding vocabulary. |
| `Colossal.Collections` | `NativeQuadTree`, `NativeHeapAllocator`, `NativeAccumulator`. |
| `Colossal.UI` | `UIManager`, `UIView`, `DefaultResourceHandler`, `UISystem`. |
| `Colossal.IO` | `IOUtils`, `ZipUtilities` (the `.cok` package format), the large binary reader and writer extensions. |
| `Colossal.Mathematics` | `Bezier4x3`, `Bounds3`, `Line3`. |
| `Colossal.Logging` | `LogManager.GetLogger`. |
| `Colossal.Localization` | `LocalizationManager.AddSource`, `MemorySource`, `CSVFileSource`. |

`Game` dwarfs the other nine and carries the bulk of everything a mod touches.
**`src/Game/Game.Modding/` is three files** — `IMod.cs`, `ModManager.cs` and `ModSetting.cs` — and that is the whole entry-point and settings-base surface.
The vocabulary a setting is declared _with_ is not there: the `SettingsUI*Attribute` types live in `src/Game/Game.Settings/`, and without them a `ModSetting` cannot declare a slider, a dropdown or a keybinding.
Source: `src/Game/Game.Modding/`, `src/Game/Game.Settings/`.

More assemblies outside that ten are worth opening when your mod goes there, and [assembly-and-namespace-map.md](assembly-and-namespace-map.md) lists them.
One is worth naming here, because it misleads.
`Unity.Entities` is not stock — the namespace exceptions below show why — so upstream Entities source and documentation are no substitute for reading the shipped copy in `src/`.

What is left after those is noise: the base class library and Mono, the `UnityEngine.*Module` set, the render pipelines, and the vendored third-party, test and tooling assemblies.
The exclusion list below turns that into something a search actually uses.

## The layout is two levels, and the globs that exploit it

`src/<Assembly>/<FullNamespace>/<TypeName>.cs`, and nothing nests further.
The namespace directory is the **fully-qualified namespace, verbatim, dots and all** — `src/Game/Game.UI.InGame/`, not a nested `Game/UI/InGame/`.

**Some files sit one level up**, directly in an assembly directory with no namespace folder — `src/Game` has ten.
A glob written `src/*/*/<Name>.cs` misses every one; `src/**/<Name>.cs` catches them.
Source: `src/Game/`.

| Goal | Pattern |
| --- | --- |
| A type by name | `src/**/<TypeName>.cs` |
| Every system in one namespace | `src/Game/<Namespace>/*System.cs` |
| Everything in a namespace | `src/Game/<Namespace>/` |
| Which assembly owns a namespace | `find src -maxdepth 2 -type d -name '<Namespace>'` |

**A name-glob usually lands on exactly one file**, and where it does not, the collision is not academic: the names that repeat are the ones a mod author reaches for.
`SearchSystem`, `InitializeSystem`, `RaycastJobs`, `ReferencesSystem`, `UpdateCollectSystem`, `Node` and `Edge` are all high-traffic and all duplicated inside `src/Game` alone.

**One file is not one type.**
File-per-type is close to a rule, and only a handful of files across the reading universe break it — but some of those are in `Colossal.UI.Binding`, the assembly a UI mod opens first.
Generic arities collapse: `src/Colossal.UI.Binding/Colossal.UI.Binding/TriggerBinding.cs` holds the non-generic `TriggerBinding` through `TriggerBinding<T1,T2,T3,T4>`, and `CallBinding.cs` does the same.
So a glob for `CallBinding*.cs` returns one file, and a grep for one arity has found one declaration rather than the family.
Source: `src/Colossal.UI.Binding/Colossal.UI.Binding/TriggerBinding.cs`, `src/Colossal.UI.Binding/Colossal.UI.Binding/CallBinding.cs`.

(VOLATILE: the reading-universe membership and the colliding names — the checkout's own `src/` tree.)

## Namespace equals directory, and the six places the inference breaks

The rule holds for every namespace directory in the tree.
What does not hold is the inference a reader draws from it — that a `Game.*` namespace lives in the `Game` assembly, and that a namespace lives in one assembly at all.

1. **`Game.*` namespaces outside `src/Game`.** `src/Colossal.Core/Game.Threading/`, `src/Colossal.IO/Game.UI.Editor/`, `src/Game.TestScenarios/Game.Debug.Tests/`, and several under the art-pipeline assembly. `Game.UI.Editor` is therefore split across two assemblies.
   Source: `src/Colossal.Core/Game.Threading/`, `src/Colossal.IO/Game.UI.Editor/`, `src/Game.TestScenarios/Game.Debug.Tests/`, `src/Game.ArtPipeline/`.
2. **Non-`Game` namespaces inside `src/Game`.** `Colossal.Atmosphere/`, `Colossal.Rendering/`, `Unity.Mathematics/` and `System.Runtime.CompilerServices/` among them.
   Source: `src/Game/Colossal.Atmosphere/`, `src/Game/Colossal.Rendering/`, `src/Game/Unity.Mathematics/`, `src/Game/System.Runtime.CompilerServices/`.
3. **Namespaces split across two assemblies.** `Colossal.Rendering` lives in both `Colossal.Core` and `Game`; `Colossal.IO` in both `Colossal.Core` and `Colossal.IO`. `Colossal` itself is split four ways, and the fourth is `Unity.Entities`, which carries `src/Unity.Entities/Colossal/CORuntimeApplication.cs` — the plainest evidence that the shipped Entities assembly is not the stock package.
   Source: `src/Colossal.Core/Colossal.Rendering/`, `src/Game/Colossal.Rendering/`, `src/Colossal.Core/Colossal.IO/`, `src/Colossal.IO/Colossal.IO/`, `src/Unity.Entities/Colossal/CORuntimeApplication.cs`.
4. **A type named `Game` that is not the assembly.** `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/Game.cs` declares `public readonly struct Game : IAssetDatabaseDescriptor<Game>`.
   Source: `src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/Game.cs`.
5. **Two tree-wide indexes are not where a reader looks for them.** `SystemOrder` is in `Game.Common` rather than in the root `Game` namespace, and `Version.cs`, the save-format milestone list, is in the root `Game` directory rather than in `Game.Serialization`. What its constants do and do not tell you is `save-serialization`'s, and [decompiler-artifacts.md](decompiler-artifacts.md) states the trap.
   Source: `src/Game/Game.Common/SystemOrder.cs`, `src/Game/Game/Version.cs`.
6. **Two files exist twice under the same namespace directory, in two assemblies, and are different classes.** `Colossal.IO/BinaryReaderExtensions.cs` and `Colossal.IO/BinaryWriterExtensions.cs` each appear in `src/Colossal.Core` and in `src/Colossal.IO`. The `Colossal.Core` copy declares a single method and the `Colossal.IO` one declares dozens, yet both are `public static class BinaryReaderExtensions` in `namespace Colossal.IO`, and neither is a partial of the other.
   Source: `src/Colossal.Core/Colossal.IO/BinaryReaderExtensions.cs`, `src/Colossal.IO/Colossal.IO/BinaryReaderExtensions.cs`.

**So the disambiguation rule is two steps, not one: qualify a colliding name by its namespace directory, and where that still returns two files, qualify by assembly.**
Among hand-written game code the pair above is where that second step earns its keep.
Everywhere else, a (namespace + filename) pair appearing in two assemblies is boilerplate that repeats by design — `Properties/AssemblyInfo.cs` in all ten of the reading universe's assemblies, the codegen registries, and the base-class-library and Unity duplicates across the wider tree.

**A dotted name in the game's own source means one of two things, and only one of them is ambiguity.**
`src/Game/Game.Simulation/AgingSystem.cs` writes `ComponentLookup<Game.Citizens.Student>` because two `Student` types are in scope — that is the namespace case, and `SystemOrder.cs`'s `UpdateAt<Game.Events.InitializeSystem>` registrations collect a ready-made list of the ambiguous _system_ names. Only system names: a colliding component or job name like `Node` or `RaycastJobs` never appears there, so the list is a head start and not a census.

The other case is a nested type, which carries its declaring type whether or not anything collides.
`SystemOrder.cs` registers `ValidationSystem.Components`, and `src/Game/Game.Tools/ValidationSystem.cs` declares `public class Components : GameSystemBase` in its body — there is no `Components.cs`.
**So read the left of the dot before globbing for the right: a namespace names a directory, a type name means the file you want is already named on the left.**
Source: `src/Game/Game.Simulation/AgingSystem.cs`, `src/Game/Game.Common/SystemOrder.cs`, `src/Game/Game.Tools/ValidationSystem.cs`.

(VOLATILE: the six exception families above — the checkout's own `src/` tree, at directory level.)

## `SystemOrder.cs` answers "when does this run", exhaustively

`src/Game/Game.Common/SystemOrder.cs` holds every `UpdateAt<>` / `UpdateBefore<>` / `UpdateAfter<>` registration in the game.

**It is the only file in `src/Game` that registers anything.**
A grep for those three forms across the assembly returns this file and `src/Game/Game/UpdateSystem.cs`, where the hits are the method declarations themselves.
So a system absent from `SystemOrder.cs` is a system the game never registers: the search space is a single file over a closed vocabulary, which is the condition under which an empty grep proves anything at all.
Source: `src/Game/Game.Common/SystemOrder.cs`, `src/Game/Game/UpdateSystem.cs`.

Two routes into it: `Grep "<SystemName>"` for one system, `Grep "SystemUpdatePhase.<Phase>"` for a whole phase.

**Grep the bare system name, with no trailing `>`.**
Registrations come in three shapes — `UpdateAt<A>`, the two-argument `UpdateBefore<A, B>`, and a nested generic like `UpdateAt<AllowBarrier<A>>` — and a pattern anchored on `>` reaches only the first, silently missing the other two.
`UpdateBefore<CitizenTravelPurposeSystem, TripNeededSystem>` is a registration that `CitizenTravelPurposeSystem>` does not find, under a guarantee that says a miss is a real absence.
Source: `src/Game/Game.Common/SystemOrder.cs`.

**Every phase but one appears there.**
`src/Game/Game/SystemUpdatePhase.cs` declares the phase set; all of them are named in `SystemOrder.cs` except `PreSimulation`, which has zero registrations and is pumped anyway — `src/Game/Game.Simulation/SimulationSystem.cs` drives it once a frame, from either of two mutually exclusive branches.
An empty phase that is still driven is exactly the shape a reader of that file misreads as "this phase does not exist".
Source: `src/Game/Game/SystemUpdatePhase.cs`, `src/Game/Game.Simulation/SimulationSystem.cs`.

**The stock ECS ordering attributes are inert here**: zero `[UpdateAfter]`, `[UpdateBefore]` and `[UpdateInGroup]` across all of `src/Game`.
Grepping for them is how a reader discovers that, and `mod-lifecycle-and-ordering` owns what to do instead.

(VOLATILE: the phase set, and which phase carries no registration — `src/Game/Game.Common/SystemOrder.cs` and `src/Game/Game/SystemUpdatePhase.cs`.)

## The mangled handle names are more greppable than the type they belong to

The DOTS source generator rewrites every system's component access into a nested `TypeHandle` struct whose **field names carry the namespace, the type and the access mode**.

```csharp
public SharedComponentTypeHandle<UpdateFrame> __Game_Simulation_UpdateFrame_SharedComponentTypeHandle;
public BufferTypeHandle<HouseholdCitizen>     __Game_Citizens_HouseholdCitizen_RO_BufferTypeHandle;
public ComponentLookup<TravelPurpose>         __Game_Citizens_TravelPurpose_RO_ComponentLookup;
public ComponentLookup<Citizen>               __Game_Citizens_Citizen_RW_ComponentLookup;
```

**The family is two shapes, not one.**

- `__<Namespace_With_Underscores>_<Type>_<RO|RW>_<ComponentLookup|ComponentTypeHandle|BufferLookup|BufferTypeHandle>` — the eight access-bearing shapes **this build uses**. The generator emits more that `src/Game` never shows: `AspectTypeHandle` and `AspectLookup` in both access modes, plus a `JobEntityTypeHandle` carrying `WithDefaultQuery` or `WithoutDefaultQuery` where the access segment sits.
- `__<Namespace>_<Type>_SharedComponentTypeHandle` — **no access segment at all**, on every shared-component handle in the game.

Three more escape both: an entity handle is `__Unity_Entities_Entity_TypeHandle`, with no game namespace in it, `__EntityStorageInfoLookup` carries no namespace or type segment whatsoever, and a `SystemAPI.Query` foreach's container handle is `__IFE_<id>_<n>_TypeHandle`, which names no component type at all.
Source: `src/Game/Game.Simulation/AgingSystem.cs`, `src/Game/Game.Tools/ToolBaseSystem.cs`, `src/Game/Game.Rendering/EditorGizmoSystem.cs` and `src/Unity.Scenes/Unity.Scenes/WeakAssetReferenceLoadingSystem.cs` (the shapes this build uses), the field-description types under `Unity.Entities/SourceGenerators/Source~/SystemGenerator.Common/INonQueryFieldDescriptions/` in the Entities package (the scheme's full list).

**What this buys**, on `Game.Citizens.Citizen`: a word-boundary grep for the bare name returns every file that so much as mentions it, and `ComponentLookup<Citizen>` narrows that but says nothing about read versus write.
`__Game_Citizens_Citizen_` returns only the systems that actually take a handle on it, and sorts them into readers and writers on the field name alone, in one pass.
Read-only dominates, so an `_RW_` sweep comes back small enough to read whole.

**The limits, and every one is load-bearing.**
A `_RW_` sweep for a _shared_ component finds nothing, because those fields carry no access segment.
And the family only covers handle-based access the generator rewrote: a write made through an `EntityCommandBuffer` with an inferred type argument carries no type name at any call site, which is the dominant form in tool code.
So `_RW_` enumerates the systems that took **write access** through a lookup or a chunk handle — not everything that writes it, and not only what writes it: the access mode is the call form the generator saw, so a system that only reads still carries an `_RW_` field wherever it used a read-write default.
Source: `src/Game/Game.Tools/ObjectToolBaseSystem.cs` (the command-buffer add with an inferred type argument), `src/Game/Game.Rendering/EditorGizmoSystem.cs` (a read-write handle the system never writes through).

The names are also strictly per-system: nothing outside the declaring system ever consumes a mangled field, so the field list is a reliable index of one system and never a cross-system one.

(VOLATILE: which shapes this build uses — the generated `TypeHandle` structs across the decompile, `src/Game` for the vanilla ones; and the scheme's full shape list — the field-description types under the Entities package's `SourceGenerators/…/INonQueryFieldDescriptions/`, in the modding toolchain's Unity project package cache.)

## Three decompiler artifacts that make a reader wrong

Most of what the decompiler leaves behind is harmless noise, catalogued in [decompiler-artifacts.md](decompiler-artifacts.md) along with the tells that identify it.
These three are not.

1. **`[CompilerGenerated]` sits on hand-written classes**, on a large fraction of `src/Game`. `AgingSystem` carries it and is an ordinary simulation system that the DOTS generator rewrote. **Never skip a file because it says `[CompilerGenerated]`.**
   Source: `src/Game/Game.Simulation/AgingSystem.cs`.
2. **Local names carry no meaning.** `num`, `num2`, `flag`, and worse, locals named after their own type with a numeric suffix — `int2 int5 = m_UpdateRanges[(int)phase];`. Never infer intent from a local identifier, and note the second-order cost: this is exactly why a call site with an inferred generic type argument is unsearchable by type name.
   Source: `src/Game/Game/UpdateSystem.cs`.
3. **`[assembly: AssemblyVersion("0.0.0.0")]` is a decoy**, repeated by most assemblies in the tree. **The decompile does state its own version**, one line above it: `src/Game/Properties/AssemblyInfo.cs` carries `[assembly: VersionInternal("1.6.0f1 (419.d6c6) [6216.19404]")]` — game version, changelist and build.
   Source: `src/Game/Properties/AssemblyInfo.cs`.

Against that: closure and iterator classes are essentially absent from `src/Game`, generic type arguments survive intact, and lambdas and LINQ read as ordinary C# — which is why a reader can trust what they read.

## The surfaces that resist search

**String-literal-driven lookups have no type-level trace.**
A binding names its group and its key as strings — `new GetterValueBinding<uint>("tool", "allSnapMask", …)` in `src/Game/Game.UI.InGame/ToolUISystem.cs` — and grepping the literal is the only route to one.
That literal is also the **only** bridge between the C# and the frontend: `"allSnapMask"` appears in both halves, and grepping the same string on each side is what joins them.
An absence on the frontend side proves nothing on its own, though — plenty of C# string literals are notification ids or internal keys that were never binding names.
Source: `src/Game/Game.UI.InGame/ToolUISystem.cs`, `Cities2_Data/Content/Game/UI/index.js`.

**The key is always a literal; the group often is not.**
The info-panel sections write `new TriggerBinding(group, "toggle", …)` against the abstract `group` property declared in `src/Game/Game.UI.InGame/InfoSectionBase.cs`, so grepping a group name finds the systems that spell it and misses every one that inherits it.
Search the key, and reach the group through the system that declares it.
Source: `src/Game/Game.UI.InGame/InfoSectionBase.cs`, `src/Game/Game.UI.InGame/ActionsSection.cs`.

**A key the game constructs is a key no source-level search can find.**
`src/Game/Game.UI.Menu/NotificationUISystem.cs` returns `"Menu.NOTIFICATION_TITLE[" + titleId + "]"`, so the runtime key exists in neither the C# nor the UI bundle as a contiguous string.
It does exist in the shipped locale data, and a byte-grep finds it there (below).
Source: `src/Game/Game.UI.Menu/NotificationUISystem.cs`, `Cities2_Data/Content/Game/Locale.cok`.

**The frontend is not in the corpus at all.**
`src/` contains zero `.js`, `.css`, `.html`, `.tsx` and `.jsx` files.
The shipped bundle names over a thousand distinct `game-ui/…` module paths, and a grep for `game-ui/` across the whole tree returns nothing.
The entire module registry — the surface a UI mod extends — is unreachable from the decompile by any search, and the decompile does not hint that it exists.
`frontend-and-injection` and `ui-build-and-devloop` own that half.
Source: `Cities2_Data/Content/Game/UI/index.js` (the module registry the decompile has no counterpart for).

**Anything shipping as data is half-visible.**
`Cities2_Data/resources.assets` carries **type names but no field names**: a byte-grep finds `BuildingPrefab` and `ServiceConsumption` and returns nothing for `m_Upkeep` or `m_ElectricityConsumption`.
So a prefab's _values_ are a question for the running game or for the asset data, never for a C# grep.
Source: `Cities2_Data/resources.assets`.

**And `resources.assets` is not the whole prefab set.** It holds what the base game shipped with; content added by later updates and packs lives in the `Prefabs_*.cok` packages beside it, which is why `ChirperPark01` is absent from `resources.assets` and present in `Content/Game/Prefabs_FreeUpdate02.cok`.
Searching only `resources.assets` and finding nothing is the scoped-grep failure below, run against the install instead of against `src/`.
Source: `Cities2_Data/resources.assets`, `Cities2_Data/Content/Game/Prefabs_FreeUpdate02.cok`.

**Reflection-driven behaviour has no call graph, but it is enumerable.**
Nothing statically connects `[SettingsUISlider]` to the slider widget.
What makes it tractable is that only a few dozen files in `src/Game` import `System.Reflection` at all, and three of them carry everything a mod author needs: `Game.UI.Menu/AutomaticSettings.cs` builds the whole options UI from attributes at runtime, `Game.Modding/ModSetting.cs` discovers keybindings by _property type_ rather than by name, and `Game.Modding/ModManager.cs` registers a mod's component types at load.
Where a question is really "what is in a registry the game built at startup", widening the grep does not help and the running game is the source.
Source: `src/Game/Game.UI.Menu/AutomaticSettings.cs`, `src/Game/Game.Modding/ModSetting.cs`, `src/Game/Game.Modding/ModManager.cs`.

**Two cheap tricks answer presence on the non-C# surfaces.**
The `.cok` packages are stored zips with uncompressed payloads, so a plain `grep -a -o` over `Cities2_Data/Content/Game/Locale.cok` returns whole localization keys with no decoding at all — one command enumerates a whole key family.
Write the character class wide enough to cover the family, though: a key set that looks complete under `[A-Za-z]*` gains a member under `[A-Za-z0-9]*`, because one of the game's own key names carries a digit.
And the shipped UI bundle is one enormous line, so `grep -c` over it answers 1 or 0 whatever the truth; count with `grep -o … | wc -l`, and use the reformatted copy for citing line numbers rather than for establishing presence.
Source: `Cities2_Data/Content/Game/Locale.cok`, `Cities2_Data/Content/Game/UI/index.js`.

(VOLATILE: the three reflection files a mod author needs — `src/Game/Game.UI.Menu` and `src/Game/Game.Modding`.)

## What an empty grep proves

**A search returning nothing is evidence about the search.**
Turning it into a claim about the game needs a separate argument that the search _could_ have found the thing.
Every variant below has put a false statement into this plugin's own shipped prose, so run the four checks below before writing the word "nothing".

### The name was compiled away

A compile-time constant is inlined at every use, so its name has no consumers to find.
**Most `k*` constants in `src/Game` occur exactly once in the whole assembly: at their own declaration.**

Three shapes, in ascending order of how convincing the false absence looks:

- **Inverted into a literal.** `public const Snap kSnapAllIgnoredMask = …` in `Game.Tools/ToolBaseSystem.cs` is its only occurrence anywhere. Its consumer in `Game.UI.InGame/ToolUISystem.cs` writes the mask's bitwise _complement_ as a hex literal, so even a search for the constant's own value misses it. `custom-tools` works the arithmetic.
- **Cross-file.** `public const int kTicksPerDay = 262144;` in `Game.Simulation/TimeSystem.cs` is its only occurrence by name, while the literal `262144` is written all over the assembly.
- **Same-file**, which is what makes the trap feel impossible. `public const float kDefaultSeaLevel = 511.7f;` in `Game.Simulation/WaterSystem.cs` has two consumers in that same file, and the name appears at neither — both write `511.7f`.

Source: `src/Game/Game.Tools/ToolBaseSystem.cs` and `src/Game/Game.UI.InGame/ToolUISystem.cs`, `src/Game/Game.Simulation/TimeSystem.cs`, `src/Game/Game.Simulation/WaterSystem.cs`.

**What finds the consumers is the value, or the consuming expression.**
State the cost honestly: a value search over-returns, and some of those `262144` hits are an unrelated flags-enum member and a bit position.
That is the better trade, because a reader can rule out a false hit by opening it and can never rule out a hit that was never printed.
Source: `src/Game/Game.Economy/Resource.cs`, `src/Game/Game.Net/LaneSystem.cs`.

### The search was scoped and the claim was not

Most references to `CreationDefinition` — the component every placement goes through — sit in `Game.Tools`, so a grep scoped there returns a big set that reads exactly like a census.
The files it misses are the ones that make a claim about who produces definitions wrong: two simulation spawners build definitions from an archetype rather than through a tool, and the rest sit outside the tool pipeline entirely.
Source: `src/Game/Game.Simulation/ZoneSpawnSystem.cs`, `src/Game/Game.Simulation/AreaSpawnSystem.cs`.

**So a negative result carries the span it was run over.**
Know which directories your search covered, and state the span whenever you report the absence — "no consumer in `src/Game/` outside `Game.Input` and `Game.Settings`" is a usable answer where "no consumer" is not.

### The subject was never in C# to begin with

For the frontend, for shipped prefab values, and for a key the game constructs at runtime, an empty C# grep is not weak evidence — it is **no** evidence.
The measured case: **most of the groups the game's own localization keys occupy are named nowhere in `src/Game`** as a key-prefix literal.
When the subject is one of those surfaces, the install answers and the decompile cannot; `localization` and `binding-layer` say which artifact.

### The pattern named one member of a family

This is the variant that does not come back empty, which is what makes it convincing: **a pattern reaching one member of a family returns a partial answer that reads as a complete one.**

`AddComponent<CreationDefinition>` returns zero across `src/Game`, and every tool in the game adds that component.
The call is `m_CommandBuffer.AddComponent(e, component);` — the type is inferred from a local, and the local is named `component`.
The scale of the blind spot: the explicit generic form is a minority of the `AddComponent` calls in `src/Game`, so a generic-form grep misses most of the adds in the game and reads as complete.
The two spellings are disjoint — `AddComponent(` never matches a generic one — so the two greps add up rather than overlap.
Source: `src/Game/Game.Tools/ObjectToolBaseSystem.cs`.

Two more of the same shape.
A `_RW_` sweep silently excludes every shared component, for the reason given above.
And an anchored declaration pattern — `class <Name> : <Base>`, or `^public struct <Name> : … IComponentData` — assumes a bare identifier sits before the colon. Two shapes put something else there: a primary constructor puts a parameter list (`public struct Car(CarFlags flags) : IComponentData`, which this decompiler emits by design) and a generic declaration puts type parameters. Allow for both, or drop the anchor and accept the over-return.
Source: `src/Game/Game.Vehicles/Car.cs`.

**Before turning a search into a claim about a family, name the family's other members and check the pattern reaches them.**
Where the family is a C# construct — a generic method with an inferred type argument, a nested type, an interface with several implementing shapes — the check is cheap and the pattern almost always misses one.

### And the same failure produces a wrong count

A count reads more authoritative than a claim does, and a census that stops where the pattern becomes clear undercounts badly: the deserialize-phase registrations open with a contiguous run that explains the idiom completely, and then go on for several times as long.
Source: `src/Game/Game.Common/SystemOrder.cs`.

**A count is a claim about a whole span, so derive it from the span** — `grep -c`, or `rg --count-matches` — rather than by reading until the pattern is obvious.

## The standing exclusion list

Excluding these costs a search nothing and removes well over half the tree:

```
src/mscorlib  src/System*  src/UnityEngine*  src/Unity.RenderPipelines*
src/Newtonsoft.Json  src/Colossal.Mono.Cecil
**/UnitySourceGeneratedAssemblyMonoScriptTypes_v1.cs
**/-BurstDirectCallInitializer.cs
**/__JobReflectionRegistrationOutput__*.cs
**/AssemblyTypeRegistry.cs
```

**The `src/…` patterns are relative to the decompile root, so run the search from there.**
From anywhere else they match no path, and the list applies without error.

Extending it with the rest of the noise — `src/netstandard`, `src/Mono.*`, `src/Unity.Microsoft.GDK*`, the vendored third-party assemblies and the `*TestScenarios` set — takes it to about two thirds.

`src/PDX.SDK` and `src/PDX.ModsUI` are the judgement call in that set, which is why neither is listed above: exclude them for any question about the game's own simulation or UI, and read them when the question is Paradox Mods, accounts or cloud saves.

**The four filename patterns are worth far more than their file count suggests.**
They match a hundred-odd files carrying enormous generated tables, and the two largest files in `src/Game` are both on that list.
A grep for a common identifier without them spends most of its output on generated registries.
Source: `src/Game/UnitySourceGeneratedAssemblyMonoScriptTypes_v1.cs`, `src/Game/Unity.Entities.CodeGeneratedRegistry/AssemblyTypeRegistry.cs`.

`**/AssemblyInfo.cs` stays in scope despite being one near-identical file per assembly: one of them is where the game's version lives, and three others carry a `VersionInternal` of their own for a Colossal component rather than for the game.

(VOLATILE: the exclusion list's own entries — the checkout's own `src/` tree, at directory level.)

## A search order

Cheapest first.

1. **A type name is known** → `Glob src/**/<Name>.cs`. Usually one hit in the reading universe. More than one → qualify by namespace directory; still more than one → qualify by assembly.
2. **"When does this run" / "what runs in phase P"** → `Grep "<Name>"` or `Grep "SystemUpdatePhase.<P>"` in `src/Game/Game.Common/SystemOrder.cs`. It holds every registration in the game, so a miss there is a real absence — remembering that `PreSimulation` is empty and pumped anyway.
3. **"What data does this system touch"** → read its nested `TypeHandle` struct and its `OnCreate` query. **"Who reads or writes component C"** → `Grep "__<Namespace_With_Underscores>_<C>_RO_"` and `_RW_` across `src/Game`, remembering the limits above.
4. **"What components does prefab type P produce"** → open `P.cs`, read its `GetArchetypeComponents` override, then follow each `base.GetArchetypeComponents(…)` up the chain, since every level adds its own. One file never answers this: the prefab's attached `ComponentBase` objects contribute more at load, so the C# gives you the fixed part of the archetype and never the whole of it.
5. **"How do I expose Y to the UI"** → find a comparable `UISystemBase` in `Game.UI.InGame` and read its `AddBinding` calls; the binding types are all in `Colossal.UI.Binding`.
6. **The search came back empty** → run the four checks before concluding absence. Is it a `const` (search the value)? Was the search scoped (state the span, or widen it)? Does the subject live outside C# (go to the install)? Does the pattern reach the whole family (an inferred generic type argument names nothing)?
7. **The search came back with a clean-looking partial answer** → treat that as a question about the pattern rather than as a finding.
8. **A count is wanted** → derive it from the whole span, and cite the illustration separately from the census.

## What this reference hands to others

`diagnostics` is the partner, and the traffic runs one way into this file: a reader arriving from a log line holding a type name, a system name or a message string finds it by the name-glob, by `SystemOrder.cs`, or — when the string was assembled at runtime — by the byte-grep.

`localization`, `binding-layer`, `frontend-and-injection` and `ui-build-and-devloop` own the far side of the one hard boundary here: the decompile ends where the string tables and the JavaScript bundle begin, and nothing past it is reachable by a C# search.

Everything else this file teaches is a technique another reference consumes in place — the mangled handle names in `ecs-in-this-game`, `SystemOrder.cs` in `mod-lifecycle-and-ordering`, the disambiguation rule in `patching`, the worked `CreationDefinition` and snap-mask cases in `placement-definitions` and `custom-tools`, the reflection list in `settings-and-input`, `Version.cs`'s home in `save-serialization`, and the Burst mangled names in `performance-and-memory`.
Each of those owns its material; this file owns finding it, and every mechanics reference reaches its own through the namespace map and confirms it through the `_RW_` sweep.
