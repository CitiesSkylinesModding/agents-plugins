using Colossal.Entities;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PoliceGapReport
{
    public partial class PoliceGapReportSystem : GameSystemBase
    {
        // A power of two, as the update system requires: 64 checks a day.
        public static readonly int kUpdatesPerDay = 64;

        private SimulationSystem m_SimulationSystem;
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_HomeQuery;
        private EntityQuery m_StationQuery;
        private EntityQuery m_ServiceQuery;
        private EntityQuery m_BudgetQuery;
        private EntityQuery m_TimeDataQuery;
        private int m_LastLoggedDay = -1;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 262144 / kUpdatesPerDay;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_HomeQuery = GetEntityQuery(
                ComponentType.ReadOnly<ResidentialProperty>(),
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());
            m_StationQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.PoliceStation>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.ReadOnly<Efficiency>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());
            m_ServiceQuery = GetEntityQuery(ComponentType.ReadOnly<ServiceData>());
            m_BudgetQuery = GetEntityQuery(ComponentType.ReadOnly<ServiceBudgetData>());
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());
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

            var policeService = FindServicePrefab(CityService.PoliceAndAdministration);
            var budget = ReadPoliceBudget(policeService);
            var stations = m_StationQuery.ToEntityArray(Allocator.Temp);
            var homes = m_HomeQuery.ToEntityArray(Allocator.Temp);

            foreach (var home in homes)
            {
                var homePosition = EntityManager.GetComponentData<Transform>(home).m_Position;
                var district = Entity.Null;

                if (EntityManager.TryGetComponent(home, out CurrentDistrict currentDistrict))
                {
                    district = currentDistrict.m_District;
                }

                var covered = false;
                var nearest = Entity.Null;
                var nearestDistance = float.MaxValue;

                foreach (var station in stations)
                {
                    var stationPrefab = EntityManager.GetComponentData<PrefabRef>(station).m_Prefab;
                    var stationTransform = EntityManager.GetComponentData<Transform>(station);
                    var distance = math.distance(homePosition, stationTransform.m_Position);

                    // The home is covered when it sits inside a station's coverage range.
                    var coverageData = EntityManager.GetComponentData<CoverageData>(stationPrefab);

                    if (distance <= coverageData.m_Range)
                    {
                        covered = true;
                    }

                    // Only a station assigned to the home's district may respond there.
                    var districts = EntityManager.GetBuffer<ServiceDistrict>(station, true);

                    if (!ServesDistrict(districts, district))
                    {
                        continue;
                    }

                    if (distance < nearestDistance)
                    {
                        nearest = station;
                        nearestDistance = distance;
                    }
                }

                if (covered)
                {
                    continue;
                }

                var homeName = m_PrefabSystem.GetPrefabName(home);

                if (nearest == Entity.Null)
                {
                    Mod.Log.Info($"Day {day}: {homeName} is outside police coverage and no station "
                        + $"serves its district. Police budget: {budget}%.");
                    continue;
                }

                var efficiencies = EntityManager.GetBuffer<Efficiency>(nearest, true);
                var efficiency = Product(efficiencies);
                var staffing = FindFactor(efficiencies, EfficiencyFactor.NotEnoughEmployees);
                var budgetFactor = FindFactor(efficiencies, EfficiencyFactor.ServiceBudget);
                var nearestPrefab = EntityManager.GetComponentData<PrefabRef>(nearest).m_Prefab;
                var stationData = EntityManager.GetComponentData<PoliceStationData>(nearestPrefab);
                var cars = (int)(efficiency * stationData.m_PatrolCarCapacity);

                Mod.Log.Info($"Day {day}: {homeName} is outside police coverage. Nearest station "
                    + $"{m_PrefabSystem.GetPrefabName(nearest)} at {nearestDistance:F0} m: "
                    + $"efficiency "
                    + $"{efficiency:P0} (staffing {staffing:P0}, budget {budgetFactor:P0}), "
                    + $"{cars}/{stationData.m_PatrolCarCapacity} patrol cars. "
                    + $"Police budget: {budget}%.");
            }

            homes.Dispose();
            stations.Dispose();
        }

        private Entity FindServicePrefab(CityService service)
        {
            var entities = m_ServiceQuery.ToEntityArray(Allocator.Temp);
            var datas = m_ServiceQuery.ToComponentDataArray<ServiceData>(Allocator.Temp);
            var result = Entity.Null;

            for (var i = 0; i < datas.Length; i++)
            {
                if (datas[i].m_Service == service)
                {
                    result = entities[i];
                    break;
                }
            }

            entities.Dispose();
            datas.Dispose();
            return result;
        }

        private int ReadPoliceBudget(Entity policeService)
        {
            var budgets = EntityManager.GetBuffer<ServiceBudgetData>(
                m_BudgetQuery.GetSingletonEntity(), true);

            foreach (var entry in budgets)
            {
                if (entry.m_Service == policeService)
                {
                    return entry.m_Budget;
                }
            }

            // No budget line exists for the police service yet.
            return 0;
        }

        private static bool ServesDistrict(
            DynamicBuffer<ServiceDistrict> districts,
            Entity district)
        {
            foreach (var entry in districts)
            {
                if (entry.m_District == district)
                {
                    return true;
                }
            }

            return false;
        }

        private static float Product(DynamicBuffer<Efficiency> efficiencies)
        {
            var product = 1f;

            foreach (var entry in efficiencies)
            {
                product *= entry.m_Efficiency;
            }

            return math.saturate(product);
        }

        private static float FindFactor(
            DynamicBuffer<Efficiency> efficiencies,
            EfficiencyFactor factor)
        {
            foreach (var entry in efficiencies)
            {
                if (entry.m_Factor == factor)
                {
                    return entry.m_Efficiency;
                }
            }

            // The game has not reported this factor for the building.
            return 0f;
        }
    }
}
