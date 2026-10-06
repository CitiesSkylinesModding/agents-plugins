# Grow planted trees instantly and clear dead ones

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod for players who don't want to wait on trees. Two things: any tree that gets planted should be fully grown right away instead of starting small, and when a tree dies it should be removed from the map instead of being left standing there.

## Checklist

### The tree query excludes the tool preview

- Falls in: the query over `Tree` carries no exclusion for `Temp`, such as `GetEntityQuery(ComponentType.ReadWrite<Tree>())` or an `EntityQueryDesc` whose `None` omits it, so the brush's hover preview is grown or removed as a real tree.
- Avoids: the query excludes `Temp`, and `Deleted` beside it, through `ComponentType.Exclude<Temp>()`, `None = { Deleted, Temp }` in an `EntityQueryDesc`, or `WithNone<Deleted, Temp>()` on a builder.
- Reference: cs2-modding/references/technique/ecs-in-this-game/ecs-in-this-game.md
- Source: src/Game/Game.Tools/GenerateObjectsSystem.cs:1280

### A grown tree is tagged for the renderer

- Falls in: the code changes a tree's `m_State` and adds neither `BatchesUpdated` nor `Updated` to that entity, so the renderer keeps drawing the old stage.
- Avoids: every tree whose state changed receives `BatchesUpdated`, through `AddComponent<BatchesUpdated>(entity)` or `AddComponent(entity, default(BatchesUpdated))`, directly or on a command buffer.
- Reference: cs2-modding/references/technique/ecs-in-this-game/ecs-in-this-game.md
- Source: src/Game/Game.Rendering/BatchInstanceSystem.cs:342

### A dead tree is removed by tag

- Falls in: the tree entity is destroyed outright with `DestroyEntity`, on `EntityManager` or a command buffer.
- Avoids: the tree entity receives `Deleted` and nothing in the mod destroys it; the game's cleanup does.
- Reference: cs2-modding/references/technique/ecs-in-this-game/ecs-in-this-game.md
- Source: src/Game/Game.Objects/SearchSystem.cs:90

### Structural changes go through a barrier the game runs

- Falls in: the system records into a stock Unity command-buffer system, `EndSimulationEntityCommandBufferSystem` or any other `*EntityCommandBufferSystem` from `Unity.Entities`, reached through `GetOrCreateSystemManaged` or its `Singleton`.
- Avoids: the command buffer comes from one of the game's barriers, `EndFrameBarrier` or a `ModificationBarrier*`, resolved in `OnCreate` and held in a field of that concrete type; or the structural changes go through `EntityManager` directly, or through a buffer the system constructs and plays back itself.
- Reference: cs2-modding/references/technique/ecs-in-this-game/ecs-in-this-game.md
- Source: src/Game/Game.SceneFlow/GameManager.cs:2383

## Roots

- decompile
