# Decompiler artifacts

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Every artifact below is a property of that tree, so without one there is nothing here to tell apart.
`cs2-modding-setup` provisions it.

Everything the decompiler left behind that is not in the game's original source, so a reader can tell machinery from meaning.
The three that make a reader _wrong_ are stated in [`navigating-the-decompile`](navigating-the-decompile.md) itself and not repeated here; this page is everything else, plus the version decoy worked out in full, since that is the one of the three with a real answer sitting next to it.

## The tool tells

ILSpy in C# 12 mode.
Two signatures: **file-scoped namespaces** (`namespace Game;`) and **primary constructors on structs** (`private struct SystemData(SystemUpdatePhase phase, int interval, …) : IComparable<SystemData>`).
Neither is in the original source; both are the decompiler's output style, and the second is what defeats an anchored declaration pattern.
Source: `src/Game/Game/UpdateSystem.cs`.

## The catalogue

1. **`__TypeHandle`, `__AssignHandles`, `__AssignQueries`, `OnCreateForCompiler`.** In nearly every system. `__AssignQueries` is almost always a no-op whose whole body is `new EntityQueryBuilder(Allocator.Temp).Dispose();`, and the query fields it does assign are named `__query_<hash>_<n>`. Ignore the machinery — but the field names _inside_ the `TypeHandle` struct are the best index of what a system reads and writes.
   Source: `src/Game/Game.Simulation/AgingSystem.cs` (the no-op body), `src/Game/Game.Rendering/WaterRenderSystem.cs` (an assigned query field).
2. **`InternalCompilerInterface.Get*` wrappers.** The codegen form of `SystemAPI.GetComponentLookup<X>()`. A mod writes the ordinary form.
   Source: `src/Unity.Entities/Unity.Entities.Internal/InternalCompilerInterface.cs`, `src/Game/Game.Achievements/AchievementTriggerSystem.cs`.
3. **Field-like events lowered to `Delegate.Combine`/`Delegate.Remove`.** `loadGameSystem.onOnSaveGameLoaded = (LoadGameSystem.EventGameLoaded)Delegate.Combine(…)` is a source-level `+=`. The lowering marks a delegate **field**; a real `event` keeps the ordinary `+=`, and `GameSystemBase` shows both forms two lines apart, so the form tells you which you are looking at.
   Source: `src/Game/Game/GameSystemBase.cs`.
4. **Named arguments partly reconstructed.** `isReadOnly: true` is restored from a boolean-literal heuristic; most other call sites show bare positional literals, so an absent argument name means nothing.
   Source: `src/Game/Game.Simulation/AgingSystem.cs`.
5. **Deconstruction noise.** `var (_, modInfo2) = (KeyValuePair<Identifier, ModInfo>)(ref modsInfo);` is a `foreach` over a dictionary rendered oddly.
   Source: `src/Game/Game.Modding/ModManager.cs`.
6. **`[Preserve]`.** Link preservation on `OnCreate`/`OnUpdate`/`OnDestroy` and constructors. Semantically irrelevant.
   Source: `src/Game/Game.Simulation/AgingSystem.cs`.
7. **Explicit no-arg constructors appended to every system.** Not in the original source.
   Source: `src/Game/Game.Simulation/AgingSystem.cs`.
8. **`goto` and label residue.**
   Source: `src/Game/Game.AssetPipeline/AssetImportPipeline.cs`.
9. **`unsafe` and raw pointers**, mostly native interop and `Colossal.Collections`.
   Source: `src/Colossal.Collections/Colossal.Collections/NativeAccumulator.cs`.
10. **Codegen-only files.** `UnitySourceGeneratedAssemblyMonoScriptTypes_v1.cs`, `__JobReflectionRegistrationOutput__*.cs`, `-BurstDirectCallInitializer.cs` and `Unity.Entities.CodeGeneratedRegistry/AssemblyTypeRegistry.cs`, at most one of each in an assembly and in most assemblies none. They are a small share of the files and a large share of the lines, which is why [`navigating-the-decompile`](navigating-the-decompile.md) excludes them by name.
    Source: `src/Game/UnitySourceGeneratedAssemblyMonoScriptTypes_v1.cs`, `src/Game/Unity.Entities.CodeGeneratedRegistry/AssemblyTypeRegistry.cs`.
11. **No closure or iterator residue.** No `<>c__DisplayClass`, `_003C`, `_003E` or `<>c` anywhere in `src/Game`, and only a small minority of files retain a visible `MoveNext()` — down to a genuine `from r in asset.references where r.Value == null select r.Key` surviving as LINQ. [`navigating-the-decompile`](navigating-the-decompile.md) states what that buys a reader.
    Source: `src/Game/Game.Modding/ModManager.cs` (the surviving LINQ).

## The version, which is one line above the `AssemblyVersion` decoy

`src/Game/Properties/AssemblyInfo.cs` carries `[assembly: VersionInternal("1.6.0f1 (419.d6c6) [6216.19404]")]` — game version, changelist and build, from the decompile alone.

Only four assemblies carry a `VersionInternal` attribute at all, and only one of them is the game's:

| Assembly | `VersionInternal` |
| --- | --- |
| `Game` | `1.6.0f1 (419.d6c6) [6216.19404]` |
| `Colossal.UI` | `1.0.0f1 (419.d6c6) [6216.19385]` |
| `Colossal.Localization` | `1.0.0a1 (419.d6c6) [6216.19385]` |
| `Colossal.Core` | `1.0.0f1`, no build stamp |

Three of the four share the changelist `419.d6c6`, which is what corroborates that they came off one build; `Colossal.Core` carries a bare version and settles nothing.
The trap is pure adjacency: `AssemblyVersion("0.0.0.0")` sits one line under the real answer in the same file, so a reader who greps `AssemblyVersion` concludes the checkout is version-blind.
Source: `src/Game/Properties/AssemblyInfo.cs`, `src/Colossal.UI/Properties/AssemblyInfo.cs`, `src/Colossal.Localization/Properties/AssemblyInfo.cs`, `src/Colossal.Core/Properties/AssemblyInfo.cs`.

**Save-format history is the other version surface.**
`src/Game/Game/Version.cs` is a long list of `[VersionConstant("<game version> [<build>]")]` fields, one per format milestone.
The last one carrying a version string names `1.5.7f1`; the `current` field below it carries a bare `[VersionConstant]` with no string and a value past that milestone, so the named list is not a reading of what the running build writes.
[`save-serialization`](../save-serialization/save-serialization.md) owns what any of it implies for a save.
Source: `src/Game/Game/Version.cs`.

(VOLATILE: the four `VersionInternal` strings and `Version.cs`'s last named constant — `src/Game/Properties/AssemblyInfo.cs` and `src/Game/Game/Version.cs`; and the mangled names themselves, whose eight-hex block is a row id that moves with the method table — the `BurstCompiler.StaticTypeReinit` attributes in that same `AssemblyInfo.cs`.)

## Burst mangled names read as noise and are the better search key

`src/Game/Properties/AssemblyInfo.cs` carries `BurstCompiler.StaticTypeReinit` attributes naming types like `Game_002ERendering_002EDequeueAndSort_00004B5A_0024BurstDirectCall`.

`_002E` is `.`, `_0024` is `$`, and the eight-hex block between the method name and `_0024BurstDirectCall` is the RID of the method's metadata token — its row in the method table, zero-padded, without the token's own `06` table byte — so it changes whenever that table shifts.
**Grep the mangled name rather than the decoded one, and grep it from the method name onward.** The full attribute string, namespace segments and all, appears only in `AssemblyInfo.cs`; drop the `<Namespace>_002E…` prefix and the remainder resolves to the declaring source file as well as to the generated tables — `DequeueAndSort_00004B5A_0024BurstDirectCall` lands on `src/Game/Game.Rendering/WaterRenderSystem.cs`, where it is the generated class's own declaration.
The decode is what dead-ends: the encoded segments name the namespace and the method and omit the declaring type, so `Game.Rendering.DequeueAndSort` matches nothing and two different types can encode to one identical decoded name.
`-BurstDirectCallInitializer.cs` is where that pays off: it writes `WaterRenderSystem.DequeueAndSort_00004B5A_0024BurstDirectCall.Initialize();`, naming the declaring type the encoded form leaves out.
Grep it for the method name and the `_0024BurstDirectCall` suffix — it carries no `_002E` segment to match.
Source: `src/Game/Properties/AssemblyInfo.cs`, `src/Game/-BurstDirectCallInitializer.cs`, `src/Game/Game.Rendering/WaterRenderSystem.cs`, and `Unity.Burst.CodeGen/ILPostProcessing.cs` in the Burst package (the mangling itself — the width, the row id, and the namespace-without-declaring-type).
[`performance-and-memory`](../performance-and-memory/performance-and-memory.md) owns what Burst compilation costs a reader chasing one of these at runtime.
