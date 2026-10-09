using System;
using System.Globalization;
using Game;
using Game.Simulation;

namespace CityDiary
{
    public partial class CityDiarySystem : GameSystemBase
    {
        private SimulationSystem m_SimulationSystem;
        private TimeSystem m_TimeSystem;
        private int m_LastMonth = -1;

        // A few hundred checks a day is plenty to catch the turn of the month.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 1024;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
        }

        protected override void OnUpdate()
        {
            DateTime now = m_TimeSystem.GetCurrentDateTime();

            if (now.Month == m_LastMonth)
            {
                return;
            }

            m_LastMonth = now.Month;

            string date = now.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            int ageInDays = (int)(m_SimulationSystem.frameIndex / TimeSystem.kTicksPerDay);

            Mod.Log.Info(
                $"{date}: founded in {m_TimeSystem.startingYear}, "
                + $"the city is {ageInDays} days old, and it is {SeasonOf(now.Month)}.");
        }

        private static string SeasonOf(int month)
        {
            switch (month)
            {
                case 12:
                case 1:
                case 2:
                    return "winter";
                case 3:
                case 4:
                case 5:
                    return "spring";
                case 6:
                case 7:
                case 8:
                    return "summer";
                default:
                    return "autumn";
            }
        }
    }
}
