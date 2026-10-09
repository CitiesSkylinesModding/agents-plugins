using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.Simulation;
using Game.UI;
using Game.UI.Localization;
using Game.UI.Menu;
using Unity.Entities;

namespace MilestoneNotifier
{
    public partial class MilestoneSystem : GameSystemBase
    {
        private static readonly int[] kMilestones =
        {
            1000, 5000, 10000, 50000, 100000, 500000, 1000000,
        };

        private CitySystem m_CitySystem;
        private NotificationUISystem m_Notifications;

        // Index into kMilestones of the next one to announce; -1 until the loaded city is read.
        private int m_NextIndex = -1;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 262144 / 32;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_Notifications = World.GetOrCreateSystemManaged<NotificationUISystem>();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_NextIndex = -1;
        }

        protected override void OnUpdate()
        {
            if (m_CitySystem.City == Entity.Null)
            {
                return;
            }

            var count = EntityManager.GetComponentData<Population>(m_CitySystem.City).m_Population;

            if (m_NextIndex < 0)
            {
                // Arm above the loaded city's population so an old city does not replay its past.
                m_NextIndex = 0;

                while (m_NextIndex < kMilestones.Length && kMilestones[m_NextIndex] <= count)
                {
                    m_NextIndex++;
                }

                return;
            }

            if (m_NextIndex >= kMilestones.Length || count < kMilestones[m_NextIndex])
            {
                return;
            }

            var reached = kMilestones[m_NextIndex];
            m_NextIndex++;

            Announce(reached);
        }

        private void Announce(int reached)
        {
            // The UI resolves the keys in the player's language and formats the number itself; the
            // translation files carry {POPULATION} where it goes.
            m_Notifications.AddOrUpdateNotification(
                $"MilestoneNotifier.{reached}",
                title: LocalizedString.Id("MilestoneNotifier.TITLE"),
                text: LocalizedString.Id(
                    "MilestoneNotifier.TEXT",
                    ("POPULATION", new LocalizedNumber<int>(reached, Unit.kInteger))));
        }
    }
}
