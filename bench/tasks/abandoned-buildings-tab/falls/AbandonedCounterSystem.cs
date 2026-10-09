using Game;
using Game.Buildings;
using Unity.Entities;

namespace AbandonedWatch
{
    public partial class AbandonedCounterSystem : GameSystemBase
    {
        private EntityQuery m_AbandonedQuery;

        // Recount once per in-game hour, which is what the graph samples.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 262144 / 24;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_AbandonedQuery = GetEntityQuery(ComponentType.ReadOnly<Abandoned>());
        }

        protected override void OnUpdate()
        {
            DebugStats.AbandonedCount = m_AbandonedQuery.CalculateEntityCount();

            if (Mod.Verbose)
            {
                Mod.Log.Info($"{DebugStats.AbandonedCount} abandoned buildings.");
            }
        }
    }
}
