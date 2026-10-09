using Colossal.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;

namespace BankruptcyWatch
{
    public partial class BankruptcyWatchSystem : GameSystemBase
    {
        // A power of two, as the update system requires: 64 checks a day.
        public static readonly int kUpdatesPerDay = 64;

        // A company below the limit is given this many frames to recover before it leaves.
        public const uint kInsolvencyWindow = 65536;

        private SimulationSystem m_SimulationSystem;
        private ResourceSystem m_ResourceSystem;
        private EntityQuery m_CompanyQuery;
        private EntityQuery m_EconomyParameterQuery;
        private EntityQuery m_TimeDataQuery;
        private ComponentLookup<ResourceData> m_ResourceDatas;
        private ComponentLookup<Game.Vehicles.DeliveryTruck> m_DeliveryTrucks;
        private BufferLookup<LayoutElement> m_LayoutElements;
        private int m_LastLoggedDay = -1;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 262144 / kUpdatesPerDay;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();

            // Extractors are never judged for bankruptcy: a depleted one idles instead of leaving,
            // so listing it would warn about a departure that cannot happen.
            m_CompanyQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(),
                ComponentType.ReadOnly<PropertyRenter>(),
                ComponentType.ReadOnly<Resources>(),
                ComponentType.ReadOnly<CompanyStatisticData>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Game.Companies.ExtractorCompany>(),
                ComponentType.Exclude<Deleted>());
            m_EconomyParameterQuery = GetEntityQuery(
                ComponentType.ReadOnly<EconomyParameterData>());
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());
            m_ResourceDatas = GetComponentLookup<ResourceData>(true);
            m_DeliveryTrucks = GetComponentLookup<Game.Vehicles.DeliveryTruck>(true);
            m_LayoutElements = GetBufferLookup<LayoutElement>(true);

            RequireForUpdate(m_CompanyQuery);
            RequireForUpdate(m_EconomyParameterQuery);
        }

        protected override void OnUpdate()
        {
            var frame = m_SimulationSystem.frameIndex;
            var day = TimeSystem.GetDay(frame, m_TimeDataQuery.GetSingleton<TimeData>());

            if (day == m_LastLoggedDay)
            {
                return;
            }

            m_LastLoggedDay = day;
            m_ResourceDatas.Update(this);
            m_DeliveryTrucks.Update(this);
            m_LayoutElements.Update(this);

            // Read live each pass: the loaded game mode rewrites the parameters after the prefab
            // initialised them, so neither the prefab's initializer nor a cached copy is the value.
            var limit = m_EconomyParameterQuery.GetSingleton<EconomyParameterData>()
                .m_CompanyBankruptcyLimit;

            // Start warning once a company has burned through this share of its headroom.
            var warningHeadroom = -limit / 10;

            var prefabs = m_ResourceSystem.GetPrefabs();
            var companies = m_CompanyQuery.ToEntityArray(Allocator.Temp);

            foreach (var company in companies)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(company).m_Prefab;
                var process = EntityManager.GetComponentData<IndustrialProcessData>(prefab);
                var resources = EntityManager.GetBuffer<Resources>(company, true);
                var isIndustrial = !EntityManager.HasComponent<ServiceAvailable>(company);

                // The game compares the whole balance sheet, stock and loaded trucks included, not
                // the cash row alone.
                var worth = EntityManager.TryGetBuffer(
                    company, true, out DynamicBuffer<OwnedVehicle> vehicles)
                    ? EconomyUtils.GetCompanyTotalWorth(
                        isIndustrial, process, resources, vehicles,
                        ref m_LayoutElements, ref m_DeliveryTrucks, prefabs, ref m_ResourceDatas)
                    : EconomyUtils.GetCompanyTotalWorth(
                        isIndustrial, process, resources, prefabs, ref m_ResourceDatas);

                var headroom = worth - limit;
                if (headroom > warningHeadroom)
                {
                    continue;
                }

                var statistics = EntityManager.GetComponentData<CompanyStatisticData>(company);

                // Below the limit, the statistic stamp holds the last frame the company was solvent
                // on, and it leaves once that is more than a quarter day ago.
                var insolventFrames = frame - statistics.m_LastFrameLowIncome;
                var status = headroom <= 0
                    ? $"bankrupt for {insolventFrames} of {kInsolvencyWindow} frames"
                    : $"{headroom} above the limit";

                Mod.Log.Info(
                    $"Day {day}: company {company.Index} ({process.m_Output.m_Resource}) " +
                    $"{status}, worth {worth}, profit {statistics.m_Profit}/day, limit {limit}.");
            }

            companies.Dispose();
        }
    }
}
