using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
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
        private Entity m_City;
        private int m_LastPopulation = -1;
        private bool m_WasActive;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_MilestoneQuery = GetEntityQuery(ComponentType.ReadOnly<MilestoneData>());

            // There is one city, so its entity is looked up once.
            m_City = m_CitySystem.City;
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            RegisterPolicy();
        }

        protected override void OnUpdate()
        {
            if (m_City == Entity.Null || !TryGetPolicyEntity(out var policy))
            {
                return;
            }

            var policies = EntityManager.GetBuffer<Policy>(m_City);
            var active = IsActive(policies, policy);

            if (active != m_WasActive)
            {
                m_WasActive = active;
                LogEntertainment(active);
            }

            var population = EntityManager.GetComponentData<Population>(m_City).m_Population;
            var crossed = m_LastPopulation >= 0
                && m_LastPopulation < kPopulationThreshold
                && population >= kPopulationThreshold;

            m_LastPopulation = population;

            // Unlocked prefabs have had their lock removed.
            if (crossed && !active && !EntityManager.HasComponent<Locked>(policy))
            {
                policies.Add(new Policy(policy, PolicyFlags.Active, 0f));
            }
        }

        private void RegisterPolicy()
        {
            var id = new PrefabID(nameof(PolicyTogglePrefab), kPolicyName);

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

            // Each range runs from no effect while the policy is off to the full bonus while on.
            var modifiers = policy.AddComponent<CityModifiers>();

            modifiers.m_Modifiers = new[]
            {
                new CityModifierInfo
                {
                    m_Type = CityModifierType.Attractiveness,
                    m_Mode = ModifierValueMode.Relative,
                    m_Range = new Bounds1(0f, 0.2f),
                },
                new CityModifierInfo
                {
                    m_Type = CityModifierType.Entertainment,
                    m_Mode = ModifierValueMode.Relative,
                    m_Range = new Bounds1(0f, 0.1f),
                },
            };

            if (!m_PrefabSystem.AddPrefab(policy))
            {
                Mod.Log.Warn($"Could not register {kPolicyName}; the prefab system logged why.");
                return;
            }

            // Reaching milestone 4 unlocks the policy.
            m_PrefabSystem.AddUnlockRequirement(milestone, policy);
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

        private void LogEntertainment(bool active)
        {
            var modifiers = EntityManager.GetBuffer<CityModifier>(m_City, isReadOnly: true);
            var delta = modifiers[(int)CityModifierType.Entertainment].m_Delta;

            Mod.Log.Info(
                $"City Festival {(active ? "on" : "off")}: entertainment modifier is "
                + $"{delta.y:P0} relative, {delta.x} absolute.");
        }
    }
}
