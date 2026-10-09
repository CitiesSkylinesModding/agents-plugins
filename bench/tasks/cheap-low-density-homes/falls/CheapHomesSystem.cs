using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Prefabs;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace CheapHomes
{
    public partial class CheapHomesSystem : GameSystemBase
    {
        private const float kUpkeepFactor = 0.5f;
        private const float kRentFactor = 0.75f;

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_ZonePrefabQuery;
        private EntityQuery m_BuildingQuery;
        private bool m_Applied;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ZonePrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<ZoneData>(),
                ComponentType.ReadWrite<ZoneServiceConsumptionData>());
            m_BuildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Renter>());
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // Halve once: the zone data is shared by every load of this session, so running again
            // would compound the discount.
            if (mode != GameMode.Game || m_Applied)
            {
                return;
            }

            m_Applied = true;

            var lowDensityZones = new NativeHashSet<Entity>(16, Allocator.Temp);

            HalveUpkeep(lowDensityZones);
            CutRent(lowDensityZones);

            lowDensityZones.Dispose();
        }

        protected override void OnUpdate()
        {
            // Never scheduled: the system is in no phase and works from the load hook alone.
        }

        private void HalveUpkeep(NativeHashSet<Entity> lowDensityZones)
        {
            var zones = m_ZonePrefabQuery.ToEntityArray(Allocator.Temp);

            foreach (var zone in zones)
            {
                var prefab = m_PrefabSystem.GetPrefab<ZonePrefab>(zone);

                // Every theme names its low-density residential zone "<theme> Residential Low".
                if (prefab.m_AreaType != AreaType.Residential || !prefab.name.Contains("Low"))
                {
                    continue;
                }

                lowDensityZones.Add(zone);

                // The zone's consumption data is what the buildings in it inherit.
                var consumption = EntityManager.GetComponentData<ZoneServiceConsumptionData>(zone);
                consumption.m_Upkeep *= kUpkeepFactor;
                EntityManager.SetComponentData(zone, consumption);

                Mod.Log.Info($"Halved upkeep for {prefab.name}");
            }

            zones.Dispose();
        }

        private void CutRent(NativeHashSet<Entity> lowDensityZones)
        {
            var buildings = m_BuildingQuery.ToEntityArray(Allocator.Temp);
            var cut = 0;

            foreach (var building in buildings)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(building).m_Prefab;

                if (!EntityManager.TryGetComponent(prefab, out SpawnableBuildingData spawnable)
                    || !lowDensityZones.Contains(spawnable.m_ZonePrefab))
                {
                    continue;
                }

                var renters = EntityManager.GetBuffer<Renter>(building, isReadOnly: true);

                for (var i = 0; i < renters.Length; i++)
                {
                    var renter = renters[i].m_Renter;

                    if (!EntityManager.TryGetComponent(renter, out PropertyRenter propertyRenter))
                    {
                        continue;
                    }

                    propertyRenter.m_Rent = Mathf.RoundToInt(propertyRenter.m_Rent * kRentFactor);
                    EntityManager.SetComponentData(renter, propertyRenter);
                    cut++;
                }
            }

            Mod.Log.Info($"Cut rent for {cut} households");
            buildings.Dispose();
        }
    }
}
