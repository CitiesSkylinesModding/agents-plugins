using Game;
using Game.Citizens;
using Game.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Scholarship
{
    public partial class ScholarshipSystem : GameSystemBase
    {
        // Sixteen passes a day; each pass rolls a sixteenth of the daily odds.
        public const int kUpdatesPerDay = 16;
        public const int kDailyOddsOneIn = 2000;
        public const int kGrant = 5000;

        private EntityQuery m_CandidateQuery;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 262144 / kUpdatesPerDay;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Teens and adults only: children are too young and the elderly are retired.
            m_CandidateQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<Citizen>(),
                    ComponentType.ReadOnly<HouseholdMember>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Teen>(),
                    ComponentType.ReadOnly<Adult>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });

            RequireForUpdate(m_CandidateQuery);
        }

        protected override void OnUpdate()
        {
            var entities = m_CandidateQuery.ToEntityArray(Allocator.Temp);

            foreach (var entity in entities)
            {
                var citizen = EntityManager.GetComponentData<Citizen>(entity);

                // Education is the three bits above the age and gender bits of the state.
                var education = ((int)citizen.m_State >> 4) & 0x7;
                if (education >= 3)
                {
                    continue;
                }

                // Roll from the citizen's own seed, then advance it so tomorrow's draw differs.
                var random = new Random((uint)citizen.m_PseudoRandom + 1);
                var won = random.NextInt(kDailyOddsOneIn * kUpdatesPerDay) == 0;
                citizen.m_PseudoRandom = (ushort)random.NextUInt();
                EntityManager.SetComponentData(entity, citizen);

                if (!won)
                {
                    continue;
                }

                var household = EntityManager.GetComponentData<HouseholdMember>(entity).m_Household;
                var data = EntityManager.GetComponentData<Household>(household);
                data.m_Resources += kGrant;
                EntityManager.SetComponentData(household, data);

                Mod.Log.Info(
                    $"Scholarship of {kGrant} awarded to citizen {entity.Index}, " +
                    $"household {household.Index}.");
            }

            entities.Dispose();
        }
    }
}
