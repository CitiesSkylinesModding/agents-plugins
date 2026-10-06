# Add a large variant of every school building

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod. I want it to add a large version of every school building in the game (elementary schools, high schools, colleges and universities, including any that come with DLC or asset packs the player owns) as a new building the player can place from the same toolbar menu as the original. Each large version should have double the student capacity of the building it's based on and be identical otherwise.

## Checklist

### The vanilla schools are looked up after mod loading has finished

- Falls in: the sweep that finds the vanilla schools (the prefab query or the `TryGetPrefab` calls) runs from inside `IMod.OnLoad`, directly or through a method `OnLoad` calls.
- Avoids: the sweep runs from a hook that fires later: a system's `OnGamePreload` or `OnGameLoadingComplete` override, a subscription to `GameManager.instance.onGameLoadingComplete`, a system registered into `SystemUpdatePhase.PrefabUpdate`, or an `IPreDeserialize` hook.
- Reference: cs2-modding/references/technique/prefabs-and-assets/prefabs-and-assets.md
- Source: src/Game/Game.SceneFlow/GameManager.cs:624

### A typed prefab lookup is null-tested rather than trusted on its return value

- Falls in: the out value of `TryGetPrefab<T>(entity, out T prefab)`, or the result of `GetPrefab<T>`, with `T` narrower than `PrefabBase`, is used on the true branch with no null test, as in `if (prefabSystem.TryGetPrefab(entity, out BuildingPrefab original)) { original.Clone(...); }`.
- Avoids: the out value is tested against null with `!= null` or `== null` before it is used, or the lookup is typed `PrefabBase` and narrowed with a tested `is` or `as`.
- Reference: cs2-modding/references/technique/prefabs-and-assets/prefabs-and-assets.md
- Source: src/Game/Game.Prefabs/PrefabSystem.cs:662

### The clone sheds the original's obsolete identifiers before registration

- Falls in: the result of `Clone(...)` is handed to `AddPrefab` with no `Remove<ObsoleteIdentifiers>()` between the two calls.
- Avoids: `clone.Remove<ObsoleteIdentifiers>()` runs before `AddPrefab`, or `PrefabSystem.DuplicatePrefab(template, name)` replaces the hand-rolled pair.
- Reference: cs2-modding/references/technique/prefabs-and-assets/prefabs-and-assets.md
- Source: src/Game/Game.Prefabs/PrefabSystem.cs:171

### The result of registering the clone is checked

- Falls in: `AddPrefab(clone)` stands as a bare statement with its `bool` discarded.
- Avoids: the return value is tested, with the failure logged and the clone not used further, as in `if (!prefabSystem.AddPrefab(clone)) { ...; continue; }`; on the `DuplicatePrefab` route, which discards that `bool` itself, the clone's registration is confirmed afterwards with `TryGetEntity` or `TryGetPrefab` before the clone is used.
- Reference: cs2-modding/references/technique/prefabs-and-assets/prefabs-and-assets.md
- Source: src/Game/Game.Prefabs/PrefabSystem.cs:188

### The routine skips a variant it already registered

- Falls in: every run clones and registers with no check for an existing variant: no lookup of the id about to be minted and no already-ran guard of any kind.
- Avoids: before cloning, a `TryGetPrefab(PrefabID)` lookup of the id about to be minted, built from the original's own type name and the variant name, skips an original whose variant already exists; or the routine returns early once it has run.
- Reference: cs2-modding/references/technique/prefabs-and-assets/prefabs-and-assets.md
- Source: src/Game/Game.Prefabs/PrefabSystem.cs:154

### The variant's name carries a token of the mod's own

- Falls in: the clone's name is the original's name plus a plain descriptive word, or a bare descriptive string, with nothing identifying this mod: `original.name + " Large"` or `"Large " + original.name`.
- Avoids: the name opens with or embeds a token unique to the mod, such as `"BigSchools " + original.name`.
- Reference: cs2-modding/references/technique/prefabs-and-assets/prefabs-and-assets.md
- Source: src/Game/Game.Prefabs/PrefabID.cs:47

## Roots

- decompile
