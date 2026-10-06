using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace BigSchools
{
    public static class LargeSchoolRegistrar
    {
        public static void Register(World world)
        {
            var prefabSystem = world.GetOrCreateSystemManaged<PrefabSystem>();
            var schoolPrefabs = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SchoolData>(),
                ComponentType.ReadOnly<PrefabData>());
            var entities = schoolPrefabs.ToEntityArray(Allocator.Temp);

            foreach (var entity in entities)
            {
                if (!prefabSystem.TryGetPrefab(entity, out BuildingPrefab original))
                {
                    continue;
                }

                var clone = original.Clone(original.name + " Large");

                if (clone.TryGet(out School school))
                {
                    school.m_StudentCapacity *= 2;
                }

                prefabSystem.AddPrefab(clone);
                Mod.Log.Info($"Registered {clone.name}");
            }

            entities.Dispose();
        }
    }
}
