using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace BlackoutLog
{
    public partial class BlackoutLogSystem : GameSystemBase
    {
        // A power of two, as the update system requires: 64 checks a day.
        public static readonly int kUpdatesPerDay = 64;

        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_HouseholdQuery;
        private EntityQuery m_TimeDataQuery;
        private int m_LastLoggedDay = -1;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 262144 / kUpdatesPerDay;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_HouseholdQuery = GetEntityQuery(
                ComponentType.ReadOnly<Household>(),
                ComponentType.ReadOnly<PropertyRenter>());
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());
        }

        protected override void OnUpdate()
        {
            // The system runs on the frames its assigned offset picks, never necessarily on a day's
            // first frame, so the day number is compared rather than the frame index.
            var day = TimeSystem.GetDay(
                m_SimulationSystem.frameIndex,
                m_TimeDataQuery.GetSingleton<TimeData>());

            if (day == m_LastLoggedDay)
            {
                return;
            }

            m_LastLoggedDay = day;

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

            Mod.Log.Info(
                $"Day {day}: {dark} of {renters.Length} households have no electricity.");
            renters.Dispose();
        }
    }
}
