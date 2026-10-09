using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TreeMix
{
    public partial class TreeMixSystem : GameSystemBase
    {
        private ToolSystem m_ToolSystem;
        private ObjectToolSystem m_ObjectToolSystem;
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_DefinitionQuery;
        private EntityQuery m_TreePrefabQuery;
        private NativeArray<Entity> m_TreePrefabs;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_ObjectToolSystem = World.GetOrCreateSystemManaged<ObjectToolSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            // The simulation's own placements (a building growing on a lot) are born carrying
            // Deleted, so excluding it confines the rewrite to what a tool is drawing.
            m_DefinitionQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<CreationDefinition>(),
                    ComponentType.ReadOnly<ObjectDefinition>(),
                    ComponentType.ReadOnly<Updated>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            m_TreePrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<TreeData>(),
                ComponentType.ReadOnly<PrefabData>());

            RequireForUpdate(m_DefinitionQuery);
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            if (m_TreePrefabs.IsCreated)
            {
                m_TreePrefabs.Dispose();
            }

            m_TreePrefabs = m_TreePrefabQuery.ToEntityArray(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (m_TreePrefabs.IsCreated)
            {
                m_TreePrefabs.Dispose();
            }

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            // The query matches every object definition in the frame, a tree being moved or
            // selected included, so only the object tool placing a tree gets rewritten.
            if (!m_TreePrefabs.IsCreated || m_ToolSystem.activeTool != m_ObjectToolSystem)
            {
                return;
            }

            var toolMode = m_ObjectToolSystem.actualMode;

            if (toolMode == ObjectToolSystem.Mode.Move || toolMode == ObjectToolSystem.Mode.Upgrade)
            {
                return;
            }

            var selected = m_ObjectToolSystem.prefab;

            if (selected == null || !m_PrefabSystem.HasComponent<TreeData>(selected))
            {
                return;
            }

            var definitions = m_DefinitionQuery.ToEntityArray(Allocator.Temp);

            foreach (var entity in definitions)
            {
                var definition = EntityManager.GetComponentData<CreationDefinition>(entity);

                if (definition.m_Prefab == Entity.Null
                    || !EntityManager.HasComponent<TreeData>(definition.m_Prefab))
                {
                    continue;
                }

                // The tool keeps each definition's seed across the frames a preview is rebuilt, so
                // drawing from it holds the pick until the player clicks. A zero seed leaves this
                // generator stuck on one value, and nothing in this build raises on it.
                var seed = (uint)definition.m_RandomSeed;
                var random = new Unity.Mathematics.Random(math.select(seed, 1u, seed == 0));

                definition.m_Prefab = m_TreePrefabs[random.NextInt(m_TreePrefabs.Length)];

                // ModificationBarrier1 plays back at the end of the phase, after the consumer has
                // read the definitions, so the write has to land now.
                EntityManager.SetComponentData(entity, definition);
            }

            definitions.Dispose();
        }
    }
}
