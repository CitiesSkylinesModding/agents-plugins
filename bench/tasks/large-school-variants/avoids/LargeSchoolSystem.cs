using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace BigSchools
{
    public partial class LargeSchoolSystem : GameSystemBase
    {
        // A prefab's identity is its class's short name plus its name string, so the token keeps
        // the variants clear of anything another mod mints.
        private const string kNamePrefix = "BigSchools ";

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_SchoolPrefabQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_SchoolPrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<SchoolData>(),
                ComponentType.ReadOnly<PrefabData>(),
                ComponentType.Exclude<Deleted>());
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            RegisterLargeSchools();
        }

        protected override void OnUpdate()
        {
            // Never scheduled: the system is in no phase and works from the preload hook alone.
        }

        private void RegisterLargeSchools()
        {
            var entities = m_SchoolPrefabQuery.ToEntityArray(Allocator.Temp);

            foreach (var entity in entities)
            {
                // The typed lookup returns true for any live prefab and leaves the out value null
                // when that prefab is not a BuildingPrefab, which a school extension is not.
                if (!m_PrefabSystem.TryGetPrefab(entity, out BuildingPrefab original)
                    || original == null
                    || original.name.StartsWith(kNamePrefix))
                {
                    continue;
                }

                var name = kNamePrefix + original.name;

                // The hook fires on every load, and the variant made on an earlier one is still
                // registered. Clone keeps the original's type, so the id is built from it.
                var variantId = new PrefabID(original.GetType().Name, name);

                if (m_PrefabSystem.TryGetPrefab(variantId, out _))
                {
                    continue;
                }

                var clone = original.Clone(name);

                // Clone copies the original's obsolete identifiers along with everything else,
                // and the original already holds them, so registering them again only logs
                // a duplicate-id warning for each.
                clone.Remove<ObsoleteIdentifiers>();

                if (clone.TryGet(out School school))
                {
                    school.m_StudentCapacity *= 2;
                }

                if (!m_PrefabSystem.AddPrefab(clone))
                {
                    Mod.Log.Warn($"Could not register {name}; the prefab system logged why.");
                    continue;
                }

                Mod.Log.Info($"Registered {name}");
            }

            entities.Dispose();
        }
    }
}
