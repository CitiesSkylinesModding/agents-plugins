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
    public partial class ChronicleSystem : GameSystemBase, IDefaultSerializable
    {
        // The section's layout version. Zero is never written: it is what SetDefaults leaves
        // behind when the save carried no section, and the post-load repair reads it as such.
        public const int kVersion = 1;

        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_TimeDataQuery;
        private EntityQuery m_UnrecordedQuery;

        private int m_LoadedVersion;
        private int m_FoundingDay = BuildingRecord.kUnknownDay;
        private int m_TotalConstructed;

        // Set by OnGameLoaded, consumed by the next OnUpdate: the load's purpose and version are
        // in hand there, and the world is open for structural changes again.
        private bool m_LoadPending;
        private Context m_LoadContext;

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

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);

            m_LoadContext = serializationContext;
            m_LoadPending = true;
        }

        protected override void OnUpdate()
        {
            if (!m_LoadPending)
            {
                return;
            }

            m_LoadPending = false;

            // A version of zero means the save had no section, which is true of a save written
            // before the mod existed and of a brand-new city alike. Only the purpose tells them
            // apart, and the two cases want opposite answers.
            switch (m_LoadContext.purpose)
            {
                case Purpose.NewGame:
                    m_FoundingDay = TimeSystem.GetDay(
                        m_SimulationSystem.frameIndex,
                        m_TimeDataQuery.GetSingleton<TimeData>());
                    m_TotalConstructed = 0;

                    Mod.Log.Info($"Chronicle started on day {m_FoundingDay}.");
                    break;

                case Purpose.LoadGame when m_LoadedVersion == 0:
                    m_FoundingDay = BuildingRecord.kUnknownDay;
                    MarkStandingBuildingsUnknown();

                    Mod.Log.Info("Chronicle joined a city founded before it was installed.");
                    break;
            }
        }

        public void RecordConstructions(int count)
        {
            m_TotalConstructed += count;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(kVersion);
            writer.Write(m_FoundingDay);
            writer.Write(m_TotalConstructed);
        }

        // Deserialize and SetDefaults only move bytes into fields: both run while the loader
        // holds an exclusive entity transaction on the world, so no query or EntityManager call
        // belongs in either. The repair they set up runs from OnUpdate.
        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_LoadedVersion);
            reader.Read(out m_FoundingDay);
            reader.Read(out m_TotalConstructed);
        }

        public void SetDefaults(Context context)
        {
            m_LoadedVersion = 0;
            m_FoundingDay = BuildingRecord.kUnknownDay;
            m_TotalConstructed = 0;
        }

        // Buildings that stood before the mod was installed get a record marked unknown, so the
        // stamping system does not date them to the day the save loaded.
        private void MarkStandingBuildingsUnknown()
        {
            if (m_UnrecordedQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

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
