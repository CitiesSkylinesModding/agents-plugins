using Colossal.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace CheapHomes
{
    public partial class CheapHomesRentSystem : GameSystemBase
    {
        private const float kRentFactor = 0.75f;

        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_BuildingQuery;

        // The rent pass's own interval. Registered after that system with no offset of its own,
        // this one inherits its offset and so runs on the same frames, right behind it.
        public override int GetUpdateInterval(SystemUpdatePhase phase) =>
            262144 / (RentAdjustSystem.kUpdatesPerDay * 16);

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_BuildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<ResidentialProperty>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Renter>(),
                ComponentType.ReadOnly<UpdateFrame>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());

            RequireForUpdate(m_BuildingQuery);
        }

        protected override void OnUpdate()
        {
            // The rent pass rewrites PropertyRenter.m_Rent from its formula for one UpdateFrame
            // bucket per run, so the cut is applied to that bucket only, every run, on top of the
            // value it just wrote. Touching the other buckets would compound the cut on rents the
            // pass has not reset yet.
            var updateFrame = SimulationUtils.GetUpdateFrame(
                m_SimulationSystem.frameIndex,
                RentAdjustSystem.kUpdatesPerDay,
                16);

            m_BuildingQuery.ResetFilter();
            m_BuildingQuery.SetSharedComponentFilter(new UpdateFrame(updateFrame));

            var buildings = m_BuildingQuery.ToEntityArray(Allocator.Temp);

            foreach (var building in buildings)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(building).m_Prefab;

                if (!EntityManager.TryGetComponent(prefab, out SpawnableBuildingData spawnable)
                    || !LowDensity.IsLowDensityResidential(EntityManager, spawnable.m_ZonePrefab))
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
                }
            }

            buildings.Dispose();
        }
    }
}
