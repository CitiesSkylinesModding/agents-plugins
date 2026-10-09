using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace CheapHomes
{
    public partial class CheapHomesUpkeepSystem : GameSystemBase
    {
        private const float kUpkeepFactor = 0.5f;

        private EntityQuery m_BuildingPrefabQuery;

        // The upkeep each prefab carried when first seen, so every load halves the game's value
        // and never a value this mod already halved.
        private readonly Dictionary<Entity, int> m_BakedUpkeep = new Dictionary<Entity, int>();

        protected override void OnCreate()
        {
            base.OnCreate();

            m_BuildingPrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<PrefabData>(),
                ComponentType.ReadOnly<SpawnableBuildingData>(),
                ComponentType.ReadWrite<ConsumptionData>());
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            if (mode != GameMode.Game)
            {
                return;
            }

            // Runs on every load, after it: the Deserialize phase just before this hook lets the
            // save's game mode restore and rewrite the consumption data on these prefabs, so a
            // value written once would not survive the next load of the session.
            var prefabs = m_BuildingPrefabQuery.ToEntityArray(Allocator.Temp);
            var changed = 0;

            foreach (var prefab in prefabs)
            {
                var spawnable = EntityManager.GetComponentData<SpawnableBuildingData>(prefab);

                if (!LowDensity.IsLowDensityResidential(EntityManager, spawnable.m_ZonePrefab))
                {
                    continue;
                }

                // The zone's ZoneServiceConsumptionData was only an input: BuildingInitializeSystem
                // baked the upkeep into this component once, and nothing reads the zone's value
                // again, so this is the number the simulation charges.
                var consumption = EntityManager.GetComponentData<ConsumptionData>(prefab);

                if (!m_BakedUpkeep.TryGetValue(prefab, out var baked))
                {
                    baked = consumption.m_Upkeep;
                    m_BakedUpkeep[prefab] = baked;
                }

                consumption.m_Upkeep = Mathf.RoundToInt(baked * kUpkeepFactor);
                EntityManager.SetComponentData(prefab, consumption);
                changed++;
            }

            Mod.Log.Info($"Halved upkeep on {changed} low-density residential prefabs");
            prefabs.Dispose();
        }

        protected override void OnUpdate()
        {
            // Never scheduled: the system is in no phase and works from the load hook alone.
        }
    }
}
