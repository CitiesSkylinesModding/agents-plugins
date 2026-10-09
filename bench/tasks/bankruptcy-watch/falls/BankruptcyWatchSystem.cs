using Colossal.Entities;
using Game;
using Game.Agents;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace BankruptcyWatch
{
    public partial class BankruptcyWatchSystem : GameSystemBase
    {
        // A power of two, as the update system requires: 64 checks a day.
        public static readonly int kUpdatesPerDay = 64;

        // The game's bankruptcy limit, from the economy prefab.
        public const int kBankruptcyLimit = -5000000;

        // Start warning once a company has burned through this share of its headroom.
        public const int kWarningHeadroom = -kBankruptcyLimit / 10;

        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_CompanyQuery;
        private EntityQuery m_TimeDataQuery;
        private int m_LastLoggedDay = -1;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 262144 / kUpdatesPerDay;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_CompanyQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(),
                ComponentType.ReadOnly<PropertyRenter>(),
                ComponentType.ReadOnly<Resources>(),
                ComponentType.ReadOnly<Profitability>(),
                ComponentType.Exclude<Deleted>());
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());

            RequireForUpdate(m_CompanyQuery);
        }

        protected override void OnUpdate()
        {
            var day = TimeSystem.GetDay(
                m_SimulationSystem.frameIndex,
                m_TimeDataQuery.GetSingleton<TimeData>());

            if (day == m_LastLoggedDay)
            {
                return;
            }

            m_LastLoggedDay = day;

            var companies = m_CompanyQuery.ToEntityArray(Allocator.Temp);

            foreach (var company in companies)
            {
                var resources = EntityManager.GetBuffer<Resources>(company, true);
                var money = EconomyUtils.GetResources(Resource.Money, resources);
                var headroom = money - kBankruptcyLimit;

                // The game tags a company it has given up on, so that is the surest signal.
                var leaving = EntityManager.HasComponent<MovingAway>(company);

                if (!leaving && headroom > kWarningHeadroom)
                {
                    continue;
                }

                // The company's daily profit figure, as the infoview shows it.
                var dailyProfit = EntityManager
                    .GetComponentData<Profitability>(company).m_Profitability;

                var output = Resource.NoResource;
                if (EntityManager.TryGetComponent(company, out IndustrialProcessData process))
                {
                    output = process.m_Output.m_Resource;
                }

                var status = leaving
                    ? "leaving"
                    : headroom <= 0 ? "bankrupt" : $"{headroom} above the limit";

                Mod.Log.Info(
                    $"Day {day}: company {company.Index} ({output}) {status}, " +
                    $"money {money}, profit {dailyProfit}/day, limit {kBankruptcyLimit}.");
            }

            companies.Dispose();
        }
    }
}
