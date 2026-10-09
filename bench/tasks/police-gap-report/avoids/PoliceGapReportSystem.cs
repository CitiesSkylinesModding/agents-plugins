using Colossal.Entities;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Net;
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

        // Coverage below this value is reported. Tune to taste.
        private const float kPoorCoverage = 1f;

        private SimulationSystem m_SimulationSystem;
        private CityServiceBudgetSystem m_BudgetSystem;
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_HomeQuery;
        private EntityQuery m_StationQuery;
        private EntityQuery m_ServiceQuery;
        private EntityQuery m_TimeDataQuery;
        private int m_LastLoggedDay = -1;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 262144 / kUpdatesPerDay;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_BudgetSystem = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
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
            // A slider nobody has moved has no budget entry; the getter reads that as 100.
            var budget = m_BudgetSystem.GetServiceBudget(policeService);
            var coverages = GetBufferLookup<Game.Net.ServiceCoverage>(true);
            var serviceDistricts = GetBufferLookup<ServiceDistrict>(true);
            var stations = m_StationQuery.ToEntityArray(Allocator.Temp);
            var homes = m_HomeQuery.ToEntityArray(Allocator.Temp);

            foreach (var home in homes)
            {
                var building = EntityManager.GetComponentData<Building>(home);
                var coverage = 0f;

                // Coverage is stored on the road edge the home fronts, sampled at the home's
                // position along it. A home with no road edge has no coverage at all.
                if (coverages.TryGetBuffer(building.m_RoadEdge, out var edgeCoverage))
                {
                    coverage = NetUtils.GetServiceCoverage(
                        edgeCoverage, CoverageService.Police, building.m_CurvePosition);
                }

                if (coverage >= kPoorCoverage)
                {
                    continue;
                }

                var homeName = m_PrefabSystem.GetPrefabName(home);
                var nearest = FindNearestEligibleStation(
                    home, stations, serviceDistricts, out var meters);

                if (nearest == Entity.Null)
                {
                    Mod.Log.Info($"Day {day}: {homeName} has police coverage {coverage:F2} and no "
                        + $"station serves its district. Police budget: {budget}%.");
                    continue;
                }

                var efficiencies = EntityManager.GetBuffer<Efficiency>(nearest, true);
                var efficiency = BuildingUtils.GetEfficiency(efficiencies);
                // A factor at exactly 1 is not stored; the helper reads an absent one as 1.
                var staffing = BuildingUtils.GetEfficiencyFactor(
                    efficiencies, EfficiencyFactor.NotEnoughEmployees);
                var budgetFactor = BuildingUtils.GetEfficiencyFactor(
                    efficiencies, EfficiencyFactor.ServiceBudget);
                var nearestPrefab = EntityManager.GetComponentData<PrefabRef>(nearest).m_Prefab;
                var stationData = EntityManager.GetComponentData<PoliceStationData>(nearestPrefab);
                // The game's own fleet rule: a station that works at all fields at least one car.
                var cars = BuildingUtils.GetVehicleCapacity(
                    efficiency, stationData.m_PatrolCarCapacity);

                Mod.Log.Info($"Day {day}: {homeName} has police coverage {coverage:F2}. Nearest "
                    + $"station {m_PrefabSystem.GetPrefabName(nearest)} at {meters:F0} m: "
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

        private Entity FindNearestEligibleStation(
            Entity home,
            NativeArray<Entity> stations,
            BufferLookup<ServiceDistrict> serviceDistricts,
            out float nearestDistance)
        {
            var district = EntityManager.TryGetComponent(home, out CurrentDistrict currentDistrict)
                ? currentDistrict.m_District
                : Entity.Null;
            var homePosition = EntityManager.GetComponentData<Transform>(home).m_Position;
            var nearest = Entity.Null;
            nearestDistance = float.MaxValue;

            foreach (var station in stations)
            {
                // The game's rule: a station with no district assigned serves the whole city,
                // and one restricted to districts never serves a home outside any district.
                if (!AreaUtils.CheckServiceDistrict(district, station, serviceDistricts))
                {
                    continue;
                }

                var stationPosition = EntityManager.GetComponentData<Transform>(station).m_Position;
                var distance = math.distance(homePosition, stationPosition);

                if (distance < nearestDistance)
                {
                    nearest = station;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }
    }
}
