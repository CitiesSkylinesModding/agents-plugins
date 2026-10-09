# Plant a random mix of tree species with the tree tool

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod. When I plant trees with the game's tree tool, I want every tree to come out as a randomly picked species instead of all of them being the one I selected in the toolbar; take the pick from all the trees the game has. It should work the same whether I'm placing trees one at a time or with the brush, and the preview should already show the mix while I'm placing.

## Checklist

### The rewriter is registered ahead of the consumer rather than with `UpdateAt`

- Falls in: the system is registered with `UpdateAt<...>(SystemUpdatePhase.Modification1)`, which lands after `GenerateObjectsSystem` has consumed every definition, or in a phase where the frame's definitions are not played back yet or already built, such as `ToolUpdate` or `GameSimulation`.
- Avoids: `IMod.OnLoad` registers it with `UpdateBefore<TreeMixSystem>(SystemUpdatePhase.Modification1)` or `UpdateBefore<TreeMixSystem, GenerateObjectsSystem>(SystemUpdatePhase.Modification1)`.
- Reference: cs2-modding/references/technique/placement-definitions/placement-definitions.md
- Source: src/Game/Game/UpdateSystem.cs:143

### The rewritten definition is written synchronously

- Falls in: the modified `CreationDefinition` is recorded into a command buffer the system does not play back itself, such as `ModificationBarrier1.CreateCommandBuffer()` followed by `SetComponent`, or any other `*Barrier` or `EntityCommandBufferSystem`.
- Avoids: the write is `EntityManager.SetComponentData(entity, definition)`, or goes through an `EntityCommandBuffer(Allocator.Temp)` the system creates, fills and plays back with `Playback(EntityManager)` inside the same `OnUpdate`.
- Reference: cs2-modding/references/technique/placement-definitions/placement-definitions.md
- Source: src/Game/Game.Common/SystemOrder.cs:86

### The definition query excludes `Deleted`

- Falls in: the query is `{CreationDefinition, ObjectDefinition, Updated}` with no exclusion of `Deleted`, so the definitions the simulation spawns for a growing building match it too.
- Avoids: the query excludes `Deleted`, through `ComponentType.Exclude<Deleted>()`, `None = { Deleted }` in an `EntityQueryDesc`, or `WithNone<Deleted>()` on a builder.
- Reference: cs2-modding/references/technique/placement-definitions/placement-definitions.md
- Source: src/Game/Game.Simulation/ZoneSpawnSystem.cs:1090

### The species is drawn from the definition's own seed, forced non-zero

- Falls in: the pick comes from a generator the definition does not seed (`UnityEngine.Random`, `RandomSeed.Next()`, a `Random` held in a field of the system, the frame index), so the preview reshuffles every frame; or from `new Unity.Mathematics.Random((uint)definition.m_RandomSeed)` with the value handed through unguarded.
- Avoids: the generator is `new Unity.Mathematics.Random(seed)` where `seed` is `(uint)definition.m_RandomSeed` forced non-zero first, through `math.select(seed, 1u, seed == 0)`, `seed | 1u`, `math.max(1u, seed)` or an explicit `== 0` test.
- Reference: cs2-modding/references/technique/placement-definitions/placement-definitions.md
- Source: src/Unity.Mathematics/Unity.Mathematics/Random.cs:425

## Roots

- decompile
