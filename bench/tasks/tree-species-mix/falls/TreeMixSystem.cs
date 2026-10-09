using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace TreeMix
{
    public partial class TreeMixSystem : GameSystemBase
    {
        private ModificationBarrier1 m_Barrier;
        private EntityQuery m_DefinitionQuery;
        private EntityQuery m_TreePrefabQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_Barrier = World.GetOrCreateSystemManaged<ModificationBarrier1>();
            m_DefinitionQuery = GetEntityQuery(
                ComponentType.ReadWrite<CreationDefinition>(),
                ComponentType.ReadOnly<ObjectDefinition>(),
                ComponentType.ReadOnly<Updated>());
            m_TreePrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<TreeData>(),
                ComponentType.ReadOnly<PrefabData>());

            RequireForUpdate(m_DefinitionQuery);
        }

        protected override void OnUpdate()
        {
            var trees = m_TreePrefabQuery.ToEntityArray(Allocator.Temp);

            if (trees.Length == 0)
            {
                trees.Dispose();

                return;
            }

            var definitions = m_DefinitionQuery.ToEntityArray(Allocator.Temp);
            var commandBuffer = m_Barrier.CreateCommandBuffer();

            foreach (var entity in definitions)
            {
                var definition = EntityManager.GetComponentData<CreationDefinition>(entity);

                // Only definitions placing a tree are touched; anything else keeps its prefab.
                if (definition.m_Prefab == Entity.Null
                    || !EntityManager.HasComponent<TreeData>(definition.m_Prefab))
                {
                    continue;
                }

                definition.m_Prefab = trees[UnityEngine.Random.Range(0, trees.Length)];
                commandBuffer.SetComponent(entity, definition);
            }

            definitions.Dispose();
            trees.Dispose();
        }
    }
}
