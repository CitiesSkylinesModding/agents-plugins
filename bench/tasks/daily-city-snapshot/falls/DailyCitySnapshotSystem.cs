using System.IO;
using System.Runtime.CompilerServices;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Internal;
using UnityEngine;

namespace DailyCitySnapshot
{
    [UpdateAfter(typeof(CityStatisticsSystem))]
    public partial class DailyCitySnapshotSystem : GameSystemBase
    {
        private struct TypeHandle
        {
            public ComponentLookup<ElectricityConsumer>
                __Game_Buildings_ElectricityConsumer_RO_ComponentLookup;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void __AssignHandles(ref SystemState state)
            {
                __Game_Buildings_ElectricityConsumer_RO_ComponentLookup =
                    state.GetComponentLookup<ElectricityConsumer>(isReadOnly: true);
            }
        }

        private const string kFileName = "DailyCitySnapshot.csv";

        private CityStatisticsSystem m_CityStatisticsSystem;
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_TimeDataQuery;
        private EntityQuery m_StudentQuery;
        private EntityQuery m_RenterQuery;
        private TypeHandle __TypeHandle;
        private int m_LastWrittenDay = -1;

        // The statistics system samples every 8192 frames; matching it keeps the snapshot close
        // behind each refresh.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 8192;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CityStatisticsSystem = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());

            // Students are tracked on the school side.
            m_StudentQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Buildings.Student>());

            m_RenterQuery = GetEntityQuery(
                ComponentType.ReadOnly<Household>(),
                ComponentType.ReadOnly<PropertyRenter>());

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
                var gameVersion = typeof(GameSystemBase).Assembly.GetName().Version;

                File.WriteAllText(
                    path,
                    $"# {nameof(DailyCitySnapshot)}, game {gameVersion}\n"
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
            var consumers = InternalCompilerInterface.GetComponentLookup(
                ref __TypeHandle.__Game_Buildings_ElectricityConsumer_RO_ComponentLookup,
                ref CheckedStateRef);

            var renters = m_RenterQuery.ToComponentDataArray<PropertyRenter>(Allocator.Temp);
            var dark = 0;

            foreach (var renter in renters)
            {
                if (consumers.TryGetComponent(renter.m_Property, out var consumer)
                    && (consumer.m_Flags & ElectricityConsumerFlags.Connected) == 0)
                {
                    dark++;
                }
            }

            renters.Dispose();

            return dark;
        }

        private void __AssignQueries(ref SystemState state)
        {
            new EntityQueryBuilder(Allocator.Temp).Dispose();
        }

        protected override void OnCreateForCompiler()
        {
            base.OnCreateForCompiler();
            __AssignQueries(ref CheckedStateRef);
            __TypeHandle.__AssignHandles(ref CheckedStateRef);
        }
    }
}
