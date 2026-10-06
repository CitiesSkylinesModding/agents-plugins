using Game;
using Game.Common;
using Game.Objects;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace InstantTrees
{
    public partial class InstantTreesSystem : GameSystemBase
    {
        // The stage bits of a tree's state; the rest of the byte is flags that stay as they are.
        private const TreeState kStageMask =
            TreeState.Teen | TreeState.Adult | TreeState.Elderly | TreeState.Dead | TreeState.Stump;

        private EndFrameBarrier m_EndFrameBarrier;
        private EntityQuery m_TreeQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();

            // The tool's hover preview is a real entity carrying Tree, so Temp keeps it out, and
            // Deleted keeps out trees already on their way out this frame.
            m_TreeQuery = GetEntityQuery(
                ComponentType.ReadWrite<Tree>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            RequireForUpdate(m_TreeQuery);
        }

        protected override void OnUpdate()
        {
            var entities = m_TreeQuery.ToEntityArray(Allocator.Temp);
            var trees = m_TreeQuery.ToComponentDataArray<Tree>(Allocator.Temp);
            var commandBuffer = m_EndFrameBarrier.CreateCommandBuffer();

            for (var i = 0; i < entities.Length; i++)
            {
                var tree = trees[i];
                var stage = tree.m_State & kStageMask;

                if ((stage & TreeState.Dead) != 0)
                {
                    // Deleted gives every system holding this tree the rest of the frame to
                    // unhook; the game's cleanup destroys the entity after that.
                    commandBuffer.AddComponent<Deleted>(entities[i]);
                    continue;
                }

                // No stage bit is a seedling; a teen is still growing. Both become adults now.
                if (stage == 0 || stage == TreeState.Teen)
                {
                    tree.m_State = (tree.m_State & ~kStageMask) | TreeState.Adult;
                    tree.m_Growth = 0;
                    EntityManager.SetComponentData(entities[i], tree);

                    // The mesh follows the stage, so the renderer has to rebuild this batch.
                    commandBuffer.AddComponent<BatchesUpdated>(entities[i]);
                }
            }

            entities.Dispose();
            trees.Dispose();
        }
    }
}
