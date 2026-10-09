using System.Globalization;
using Game;
using Game.Common;
using Game.Simulation;
using Unity.Entities;

namespace CityDiary
{
    public partial class CityDiarySystem : GameSystemBase
    {
        private SimulationSystem m_SimulationSystem;
        private TimeSystem m_TimeSystem;
        private ClimateSystem m_ClimateSystem;
        private EntityQuery m_TimeDataQuery;
        private int m_LastDay = -1;

        // A few hundred checks a day is plenty to catch the turn of the month.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 1024;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_ClimateSystem = World.GetOrCreateSystemManaged<ClimateSystem>();
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());

            RequireForUpdate(m_TimeDataQuery);
        }

        protected override void OnUpdate()
        {
            TimeData timeData = m_TimeDataQuery.GetSingleton<TimeData>();

            // A month of the game's calendar is one day of 262144 frames, so the turn of the month
            // is the turn of the day, counted from the founding frame rather than from frame zero.
            int day = TimeSystem.GetDay(m_SimulationSystem.frameIndex, timeData);

            if (day == m_LastDay)
            {
                return;
            }

            m_LastDay = day;

            // GetCurrentDateTime packs the game's month into the DateTime's day-of-year slot, so
            // its Month is always January. The index is zero-based, as the game's own date code
            // reads it.
            int monthIndex = m_TimeSystem.GetCurrentDateTime().DayOfYear - 1;
            string month = m_TimeSystem.daysPerYear == 12
                ? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(monthIndex + 1)
                : $"month {monthIndex + 1}";
            string season = m_ClimateSystem.currentSeasonName ?? "an unnamed season";

            Mod.Log.Info(
                $"{month} {m_TimeSystem.year}: founded in {timeData.m_StartingYear}, "
                + $"the city is {day} days old, and it is {season}.");
        }
    }
}
