using Game;
using Game.Buildings;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace NewBuildingLog
{
    // Run after the game has initialised the building, so its setup is complete when we log it.
    [UpdateAfter(typeof(Game.Buildings.InitializeSystem))]
    public partial class NewBuildingLogSystem : GameSystemBase
    {
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_NewBuildingQuery;

        // Buildings placed since the city was loaded.
        private int m_PlacedThisCity;

        protected override void OnCreate()
        {
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_NewBuildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Created>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Temp>());
            RequireForUpdate(m_NewBuildingQuery);
        }

        protected override void OnUpdate()
        {
            var prefabRefs = m_NewBuildingQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp);
            var transforms = m_NewBuildingQuery.ToComponentDataArray<Transform>(Allocator.Temp);

            for (var i = 0; i < prefabRefs.Length; i++)
            {
                m_PlacedThisCity++;

                var name = m_PrefabSystem.GetPrefabName(prefabRefs[i].m_Prefab);
                Mod.Instance.Log.Info(
                    $"#{m_PlacedThisCity} {name} placed at {transforms[i].m_Position}");
            }

            prefabRefs.Dispose();
            transforms.Dispose();
        }
    }
}
