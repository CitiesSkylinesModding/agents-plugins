using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace BuildingChronicle
{
    /// <summary>
    /// The city-wide half of the chronicle: the day the city was founded and how many buildings
    /// have gone up since. Saved once per city rather than once per entity.
    /// </summary>
    public partial class ChronicleSystem : GameSystemBase, ISerializable
    {
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_TimeDataQuery;
        private EntityQuery m_UnrecordedQuery;

        private int m_FoundingDay = BuildingRecord.kUnknownDay;
        private int m_TotalConstructed;

        public int foundingDay => m_FoundingDay;

        public int totalConstructed => m_TotalConstructed;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());
            m_UnrecordedQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.Exclude<BuildingRecord>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());
        }

        protected override void OnUpdate()
        {
            // No founding day yet means the chronicle has never seen this city: found it today.
            if (m_FoundingDay == BuildingRecord.kUnknownDay)
            {
                m_FoundingDay = TimeSystem.GetDay(
                    m_SimulationSystem.frameIndex,
                    m_TimeDataQuery.GetSingleton<TimeData>());

                Mod.Log.Info($"Chronicle started on day {m_FoundingDay}.");
            }
        }

        public void RecordConstructions(int count)
        {
            m_TotalConstructed += count;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(m_FoundingDay);
            writer.Write(m_TotalConstructed);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_FoundingDay);
            reader.Read(out m_TotalConstructed);

            // Buildings that stood before the mod was installed have no record. Give them one
            // marked unknown, so the stamping system does not date them to the day the save loaded.
            if (!m_UnrecordedQuery.IsEmptyIgnoreFilter)
            {
                var buildings = m_UnrecordedQuery.ToEntityArray(Allocator.Temp);

                foreach (var building in buildings)
                {
                    EntityManager.AddComponentData(building, new BuildingRecord
                    {
                        m_BuiltDay = BuildingRecord.kUnknownDay,
                        m_BuiltPopulation = 0,
                    });
                }

                buildings.Dispose();
            }
        }
    }
}
