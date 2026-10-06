using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace BlackoutLog
{
    public partial class BlackoutLogSystem : GameSystemBase
    {
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_HouseholdQuery;

        // Twenty-four checks a day, one per in-game hour.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 262144 / 24;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_HouseholdQuery = GetEntityQuery(
                ComponentType.ReadOnly<Household>(),
                ComponentType.ReadOnly<PropertyRenter>());
        }

        protected override void OnUpdate()
        {
            if (m_SimulationSystem.frameIndex % 262144 != 0)
            {
                return;
            }

            var renters = m_HouseholdQuery.ToComponentDataArray<PropertyRenter>(Allocator.Temp);
            var dark = 0;

            foreach (var renter in renters)
            {
                if (EntityManager.TryGetComponent(renter.m_Property, out ElectricityConsumer consumer)
                    && (consumer.m_Flags & ElectricityConsumerFlags.Connected) == 0)
                {
                    dark++;
                }
            }

            Mod.Log.Info($"{dark} of {renters.Length} households have no electricity.");
            renters.Dispose();
        }
    }
}
