using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.Common;
using Game.Policies;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace CityFestival
{
    public partial class CityFestivalSystem : GameSystemBase
    {
        public const string kPolicyName = "CityFestival";

        private const int kMilestoneIndex = 4;
        private const int kPopulationThreshold = 10000;

        private PrefabSystem m_PrefabSystem;
        private CitySystem m_CitySystem;
        private EntityQuery m_MilestoneQuery;
        private EntityArchetype m_ModifyArchetype;
        private int m_LastPopulation = -1;
        private bool m_WasActive;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_MilestoneQuery = GetEntityQuery(ComponentType.ReadOnly<MilestoneData>());
            m_ModifyArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<Event>(),
                ComponentType.ReadWrite<Modify>());
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            RegisterPolicy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // Each load is another city: the crossing and toggle trackers start over.
            m_LastPopulation = -1;
            m_WasActive = false;
        }

        protected override void OnUpdate()
        {
            if (!TryGetPolicyEntity(out var policy))
            {
                return;
            }

            // The city entity is serialized with the save and differs between the cities loaded in
            // one session, so it is read off the system each time rather than kept in a field.
            var city = m_CitySystem.City;

            if (city == Entity.Null)
            {
                return;
            }

            var active = IsActive(EntityManager.GetBuffer<Policy>(city, isReadOnly: true), policy);

            if (active != m_WasActive)
            {
                m_WasActive = active;
                LogEntertainment(city, active);
            }

            var population = EntityManager.GetComponentData<Population>(city).m_Population;
            var crossed = m_LastPopulation >= 0
                && m_LastPopulation < kPopulationThreshold
                && population >= kPopulationThreshold;

            m_LastPopulation = population;

            // Locked stays on the prefab after it unlocks; only its enabled bit changes.
            if (crossed && !active && !EntityManager.HasEnabledComponent<Locked>(policy))
            {
                // The policy system applies the change, refreshes the city's effects and marks the
                // city updated for the panel; writing the Policy buffer would skip all of that.
                var modify = EntityManager.CreateEntity(m_ModifyArchetype);

                EntityManager.SetComponentData(modify, new Modify(city, policy, true, 0f));
            }
        }

        private void RegisterPolicy()
        {
            var id = new PrefabID(nameof(PolicyTogglePrefab), kPolicyName);

            // The hook fires on every load and the prefab made on an earlier one is still there.
            if (m_PrefabSystem.TryGetPrefab(id, out _))
            {
                return;
            }

            if (!TryGetMilestone(kMilestoneIndex, out var milestone))
            {
                Mod.Log.Warn($"No milestone prefab has index {kMilestoneIndex}; policy not added.");
                return;
            }

            var policy = ScriptableObject.CreateInstance<PolicyTogglePrefab>();

            policy.name = kPolicyName;
            policy.m_Category = PolicyCategory.Culture;

            // A toggle policy contributes the range's min; max is read for sliders only.
            var modifiers = policy.AddComponent<CityModifiers>();

            modifiers.m_Modifiers = new[]
            {
                new CityModifierInfo
                {
                    m_Type = CityModifierType.Attractiveness,
                    m_Mode = ModifierValueMode.Relative,
                    m_Range = new Bounds1(0.2f, 0.2f),
                },
                new CityModifierInfo
                {
                    m_Type = CityModifierType.Entertainment,
                    m_Mode = ModifierValueMode.Relative,
                    m_Range = new Bounds1(0.1f, 0.1f),
                },
            };

            // Whether a prefab can be locked at all is decided at registration from the components
            // it carries then, so the requirement goes on before AddPrefab.
            var unlockable = policy.AddComponent<Unlockable>();

            unlockable.m_RequireAll = new PrefabBase[] { milestone };

            if (!m_PrefabSystem.AddPrefab(policy))
            {
                Mod.Log.Warn($"Could not register {kPolicyName}; the prefab system logged why.");
            }
        }

        private bool TryGetMilestone(int index, out MilestonePrefab milestone)
        {
            var entities = m_MilestoneQuery.ToEntityArray(Allocator.Temp);
            var data = m_MilestoneQuery.ToComponentDataArray<MilestoneData>(Allocator.Temp);

            milestone = null;

            for (var i = 0; i < data.Length; i++)
            {
                if (data[i].m_Index == index
                    && m_PrefabSystem.TryGetPrefab(entities[i], out milestone))
                {
                    break;
                }
            }

            entities.Dispose();
            data.Dispose();

            return milestone != null;
        }

        private bool TryGetPolicyEntity(out Entity policy)
        {
            policy = Entity.Null;

            var id = new PrefabID(nameof(PolicyTogglePrefab), kPolicyName);

            return m_PrefabSystem.TryGetPrefab(id, out var prefab)
                && m_PrefabSystem.TryGetEntity(prefab, out policy);
        }

        private static bool IsActive(DynamicBuffer<Policy> policies, Entity policy)
        {
            foreach (var entry in policies)
            {
                if (entry.m_Policy == policy)
                {
                    return (entry.m_Flags & PolicyFlags.Active) != 0;
                }
            }

            return false;
        }

        private void LogEntertainment(Entity city, bool active)
        {
            var modifiers = EntityManager.GetBuffer<CityModifier>(city, isReadOnly: true);

            // The buffer is grown only up to the highest modifier type in use, so a type nothing
            // has touched yet lies past its end; the accessor returns a default for that case.
            var delta = CityUtils.GetModifier(modifiers, CityModifierType.Entertainment);

            Mod.Log.Info(
                $"City Festival {(active ? "on" : "off")}: entertainment modifier is "
                + $"{delta.y:P0} relative, {delta.x} absolute.");
        }
    }
}
