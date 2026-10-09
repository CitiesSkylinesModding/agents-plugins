using Game;
using Game.Buildings;
using Game.Common;
using Game.Tools;
using LandmarkKeeper;
using Unity.Entities;

namespace AbandonedAutoBulldoze
{
    public partial class AutoBulldozeSystem : GameSystemBase
    {
        private EntityQuery m_AbandonedQuery;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 64;

        protected override void OnCreate()
        {
            base.OnCreate();

            // With Landmark Keeper around, a building it flagged is left out of the query.
            m_AbandonedQuery = Mod.LandmarkKeeperPresent
                ? GetEntityQuery(
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Abandoned>(),
                    ComponentType.Exclude<Protected>(),
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>())
                : GetEntityQuery(
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Abandoned>(),
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>());

            RequireForUpdate(m_AbandonedQuery);
        }

        protected override void OnUpdate()
        {
            // Marking the building deleted is what the bulldozer does; the game's own cleanup
            // systems take it from there.
            EntityManager.AddComponent<Deleted>(m_AbandonedQuery);
        }
    }
}
