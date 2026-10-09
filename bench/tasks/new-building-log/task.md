# A construction log that never writes a line

## Kind

bug report

## Prompt

I'm making a Cities: Skylines II mod that keeps a construction log for my city. Every time a building gets placed, I want a line in my log with the building's prefab name and where it stands, written as soon as the building is placed. Each line is numbered by how many buildings have been placed since the city was loaded. The code is in this workspace. It builds, and the mod shows as enabled in my playset, but nothing ever shows up in the log and the game reports no error.

## Checklist

### Mod-level state is static or assigned inside `OnLoad`, never left to a field initialiser or constructor

- Falls in: the `IMod` class declares an instance field or property with an initialiser, such as `private readonly ILog m_Log = LogManager.GetLogger(...)`, or a constructor assigning one, and `OnLoad`, `OnDispose` or a system then reads it as if it were set.
- Avoids: everything the mod class holds is a `static` field, or is assigned inside `OnLoad` before anything reads it; no instance initialiser and no constructor carry state.
- Reference: cs2-modding/references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md
- Source: src/Game/Game.Modding/ModManager.cs:122

### Ordering is requested through the update system, not through ECS ordering attributes

- Falls in: the relation to the game's building initialiser is carried by `[UpdateAfter(typeof(...))]`, `[UpdateBefore(...)]` or `[UpdateInGroup(...)]` on the system class, and the registration in `OnLoad` is a single-type call such as `UpdateAt<MySystem>(phase)`, or there is no registration at all.
- Avoids: the relation is carried by the two-type `updateSystem.UpdateAfter<MySystem, Game.Buildings.InitializeSystem>(phase)` or `UpdateBefore<,>` call, or the system is registered with a single-type call and relies on no ordering attribute; any such attribute left on the class is not what the registration depends on.
- Reference: cs2-modding/references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md
- Source: src/Game/Game/UpdateSystem.cs:31

### The system is registered into `Modification2`, the phase the building initialiser runs in

- Falls in: the phase passed to the registration call, anchored or not, is anything other than `SystemUpdatePhase.Modification2`, such as `GameSimulation`, `MainLoop` or `PrefabUpdate`.
- Avoids: the registration call passes `SystemUpdatePhase.Modification2`.
- Reference: cs2-modding/references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md
- Source: src/Game/Game.Common/SystemOrder.cs:118

### The per-city count is reset on a per-load hook

- Falls in: the field counting placed buildings is initialised once and never reset, or is reset only in `OnCreate`, `OnStartRunning` or `OnWorldReady`.
- Avoids: the count is reset in an override of `OnGamePreload`, `OnGameLoaded` or `OnGameLoadingComplete`, or by a system the mod registers into the `Deserialize` phase, including through a `PreDeserialize<T>` wrapper.
- Reference: cs2-modding/references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md
- Source: src/Game/Game.SceneFlow/GameManager.cs:1033

### The system's `OnCreate` calls `base.OnCreate()`

- Falls in: the system overrides `OnCreate` and the body never calls `base.OnCreate()`.
- Avoids: the `OnCreate` override calls `base.OnCreate()`, or the system does not override `OnCreate`.
- Reference: cs2-modding/references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md
- Source: src/Game/Game/GameSystemBase.cs:25

## Roots

- decompile
