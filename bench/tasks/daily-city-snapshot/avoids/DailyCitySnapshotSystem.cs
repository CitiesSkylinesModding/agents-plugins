using System.IO;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace DailyCitySnapshot
{
    public partial class DailyCitySnapshotSystem : GameSystemBase
    {
        private const string kFileName = "DailyCitySnapshot.csv";

        private CityStatisticsSystem m_CityStatisticsSystem;
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_TimeDataQuery;
        private EntityQuery m_StudentQuery;
        private EntityQuery m_RenterQuery;
        private ComponentLookup<ElectricityConsumer> m_ElectricityConsumers;
        private int m_LastWrittenDay = -1;

        // The statistics system samples every 8192 frames. Running at the same interval, spliced
        // right after it, each snapshot lands behind a refresh.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 8192;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CityStatisticsSystem = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());

            // Two Student types are in scope: Game.Buildings.Student is the roster buffer on a
            // school, Game.Citizens.Student the component on each enrolled citizen.
            m_StudentQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Citizens.Student>());

            m_RenterQuery = GetEntityQuery(
                ComponentType.ReadOnly<Household>(),
                ComponentType.ReadOnly<PropertyRenter>());
            m_ElectricityConsumers = GetComponentLookup<ElectricityConsumer>(isReadOnly: true);

            RequireForUpdate(m_TimeDataQuery);
        }

        protected override void OnUpdate()
        {
            var day = TimeSystem.GetDay(
                m_SimulationSystem.frameIndex,
                m_TimeDataQuery.GetSingleton<TimeData>());

            if (day == m_LastWrittenDay)
            {
                return;
            }

            m_LastWrittenDay = day;

            var path = Path.Combine(Application.persistentDataPath, kFileName);

            if (!File.Exists(path))
            {
                // Game.Version.current is the running build. The game assemblies all carry an
                // AssemblyVersion of 0.0.0.0, so reflection on them says nothing.
                File.WriteAllText(
                    path,
                    $"# {nameof(DailyCitySnapshot)}, game {Game.Version.current.fullVersion}\n"
                    + "day,population,households,workers,unemployed,students,"
                    + "households_without_electricity\n");
            }

            var population = m_CityStatisticsSystem.GetStatisticValue(StatisticType.Population);
            var households = m_CityStatisticsSystem.GetStatisticValue(StatisticType.HouseholdCount);
            var workers = m_CityStatisticsSystem.GetStatisticValue(StatisticType.WorkerCount);
            var unemployed = m_CityStatisticsSystem.GetStatisticValue(StatisticType.Unemployed);
            var students = m_StudentQuery.CalculateEntityCount();
            var dark = CountHouseholdsWithoutElectricity();

            File.AppendAllText(
                path,
                $"{day},{population},{households},{workers},{unemployed},{students},{dark}\n");
        }

        private int CountHouseholdsWithoutElectricity()
        {
            m_ElectricityConsumers.Update(this);

            var renters = m_RenterQuery.ToComponentDataArray<PropertyRenter>(Allocator.Temp);
            var dark = 0;

            foreach (var renter in renters)
            {
                if (m_ElectricityConsumers.TryGetComponent(renter.m_Property, out var consumer)
                    && (consumer.m_Flags & ElectricityConsumerFlags.Connected) == 0)
                {
                    dark++;
                }
            }

            renters.Dispose();

            return dark;
        }
    }
}
