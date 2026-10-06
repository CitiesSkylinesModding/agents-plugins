using Game;
using Game.Objects;
using Unity.Collections;
using Unity.Entities;

namespace InstantTrees
{
    public partial class InstantTreesSystem : GameSystemBase
    {
        // The stage bits of a tree's state; the rest of the byte is flags that stay as they are.
        private const TreeState kStageMask =
            TreeState.Teen | TreeState.Adult | TreeState.Elderly | TreeState.Dead | TreeState.Stump;

        private EndSimulationEntityCommandBufferSystem m_Barrier;
        private EntityQuery m_TreeQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_Barrier = World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
            m_TreeQuery = GetEntityQuery(ComponentType.ReadWrite<Tree>());
            RequireForUpdate(m_TreeQuery);
        }

        protected override void OnUpdate()
        {
            var entities = m_TreeQuery.ToEntityArray(Allocator.Temp);
            var trees = m_TreeQuery.ToComponentDataArray<Tree>(Allocator.Temp);
            var commandBuffer = m_Barrier.CreateCommandBuffer();

            for (var i = 0; i < entities.Length; i++)
            {
                var tree = trees[i];
                var stage = tree.m_State & kStageMask;

                if ((stage & TreeState.Dead) != 0)
                {
                    commandBuffer.DestroyEntity(entities[i]);
                    continue;
                }

                // No stage bit is a seedling; a teen is still growing. Both become adults now.
                if (stage == 0 || stage == TreeState.Teen)
                {
                    tree.m_State = (tree.m_State & ~kStageMask) | TreeState.Adult;
                    tree.m_Growth = 0;
                    EntityManager.SetComponentData(entities[i], tree);
                }
            }

            entities.Dispose();
            trees.Dispose();
        }
    }
}
