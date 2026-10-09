# Bulldoze abandoned buildings unless another mod keeps them

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod that bulldozes abandoned buildings the moment they're abandoned, instead of leaving the ruins standing around. A lot of my players also run Landmark Keeper, a mod that lets you flag buildings you want to keep: it ships as LandmarkKeeper.dll and puts a `LandmarkKeeper.Protected` component on each flagged building. When a player has Landmark Keeper in their playset, my mod should leave the flagged buildings alone; when they don't, every abandoned building goes.

## Checklist

### The other mod is reached without a compile-time reference

- Falls in: the code names the other mod's type at compile time: a `using LandmarkKeeper` directive, `ComponentType.ReadOnly<Protected>()`, `ComponentType.Exclude<Protected>()`, `HasComponent<Protected>()` or a `<Reference>`/`<ProjectReference>` to its assembly in a project file.
- Avoids: the component type is resolved at runtime by name, through `Type.GetType("LandmarkKeeper.Protected, LandmarkKeeper")`, a scan of `AppDomain.CurrentDomain.GetAssemblies()` followed by `assembly.GetType(...)`, or the other mod's `ExecutableAsset.assembly`, and turned into a `ComponentType` through `TypeManager.GetTypeIndex`, `ComponentType.ReadOnly(Type)` or `new ComponentType(Type)`.
- Reference: cs2-modding/references/technique/mod-compatibility/mod-compatibility.md
- Source: src/Game/Game.Modding/ModManager.cs:110

### Presence asked in `OnLoad` is asked of the mod manager

- Falls in: the `OnLoad` body decides whether the other mod is present from `GameManager.instance.modManager.ListModsEnabled()`, a scan of `AppDomain.CurrentDomain.GetAssemblies()`, or a `Type.GetType` probe.
- Avoids: the `OnLoad` body enumerates `GameManager.instance.modManager` itself (`foreach (var modInfo in GameManager.instance.modManager)` or a LINQ query over it) and matches `modInfo.asset.name`, or asks no presence question in `OnLoad` at all and leaves every probe to a deferred callback.
- Reference: cs2-modding/references/technique/mod-compatibility/mod-compatibility.md
- Source: src/Game/Game.Modding/ModManager.cs:225

### The system whose query names the foreign component is registered after every mod has loaded

- Falls in: `updateSystem.UpdateAt<...>` (or `UpdateBefore`/`UpdateAfter`) for the system that queries the foreign component is called from the `OnLoad` body.
- Avoids: that registration runs inside a callback deferred past `OnLoad`, such as `MainThreadDispatcher.RegisterUpdater(...)`, reaching the update system through `World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<UpdateSystem>()`, with a once-guard so a second entry into the callback does not register the system again.
- Reference: cs2-modding/references/technique/mod-compatibility/mod-compatibility.md
- Source: src/Game/Game.Modding/ModManager.cs:451

### The other mod's assembly name is matched whole

- Falls in: the name test is a prefix or substring match, such as `name.StartsWith("LandmarkKeeper")`, `name.Contains("LandmarkKeeper")`, `IndexOf`, or an asset-database `SearchFilter` built from a bare string.
- Avoids: the test is an exact equality on the simple assembly name (`modInfo.asset.name == "LandmarkKeeper"`, `assembly.GetName().Name == "LandmarkKeeper"`, `string.Equals(..., StringComparison.Ordinal)`), or the type is reached by an assembly-qualified `Type.GetType` with no name search at all.
- Reference: cs2-modding/references/technique/mod-compatibility/mod-compatibility.md
- Source: src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/SearchFilter.cs:100

### A cached detection answer is refreshed when the mod set changes

- Falls in: the presence answer or the resolved type is stored once (a `static bool`, a `bool?`, a field set in `OnLoad` or in the deferred callback) and nothing subscribes to `ParadoxModsDataSource.onAfterActivePlaysetOrModStatusChanged`.
- Avoids: the code subscribes to `onAfterActivePlaysetOrModStatusChanged` on `AssetDatabase<ParadoxMods>.instance.dataSource` cast to `ParadoxModsDataSource`, and in that handler clears the cached answer and re-runs the probe and whatever it gated, or never caches and re-runs the probe on each ask.
- Reference: cs2-modding/references/technique/mod-compatibility/mod-compatibility.md
- Source: src/Game/Game.SceneFlow/GameManager.cs:1643

## Roots

- decompile
