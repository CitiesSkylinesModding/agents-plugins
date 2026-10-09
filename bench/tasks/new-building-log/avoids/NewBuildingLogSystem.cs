using Colossal.Serialization.Entities;
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
    public partial class NewBuildingLogSystem : GameSystemBase
    {
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_NewBuildingQuery;

        // Buildings placed since the city was loaded. The world and its systems live for the
        // whole process, so this survives into the next city unless a load hook resets it.
        private int m_PlacedThisCity;

        protected override void OnCreate()
        {
            // The base subscribes the load hooks below; skipping it leaves them never called.
            base.OnCreate();

            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_NewBuildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Created>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Temp>());
            RequireForUpdate(m_NewBuildingQuery);
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_PlacedThisCity = 0;
        }

        protected override void OnUpdate()
        {
            var prefabRefs = m_NewBuildingQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp);
            var transforms = m_NewBuildingQuery.ToComponentDataArray<Transform>(Allocator.Temp);

            for (var i = 0; i < prefabRefs.Length; i++)
            {
                m_PlacedThisCity++;

                var name = m_PrefabSystem.GetPrefabName(prefabRefs[i].m_Prefab);
                Mod.Log.Info($"#{m_PlacedThisCity} {name} placed at {transforms[i].m_Position}");
            }

            prefabRefs.Dispose();
            transforms.Dispose();
        }
    }
}
