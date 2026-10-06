using Game;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace BuildingChronicle
{
    /// <summary>
    /// Stamps every building the chronicle has not seen yet with the current day and population.
    /// </summary>
    public partial class BuildingRecordSystem : GameSystemBase
    {
        private SimulationSystem m_SimulationSystem;
        private ChronicleSystem m_ChronicleSystem;
        private EntityQuery m_UnrecordedQuery;
        private EntityQuery m_TimeDataQuery;
        private EntityQuery m_PopulationQuery;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 64;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_ChronicleSystem = World.GetOrCreateSystemManaged<ChronicleSystem>();
            m_UnrecordedQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.Exclude<BuildingRecord>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());
            m_PopulationQuery = GetEntityQuery(ComponentType.ReadOnly<Population>());

            RequireForUpdate(m_UnrecordedQuery);
        }

        protected override void OnUpdate()
        {
            var day = TimeSystem.GetDay(
                m_SimulationSystem.frameIndex,
                m_TimeDataQuery.GetSingleton<TimeData>());
            var population = m_PopulationQuery.GetSingleton<Population>().m_Population;
            var buildings = m_UnrecordedQuery.ToEntityArray(Allocator.Temp);

            foreach (var building in buildings)
            {
                EntityManager.AddComponentData(building, new BuildingRecord
                {
                    m_BuiltDay = day,
                    m_BuiltPopulation = population,
                });
            }

            m_ChronicleSystem.RecordConstructions(buildings.Length);
            buildings.Dispose();
        }
    }
}
