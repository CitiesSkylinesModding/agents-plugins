using Game;
using Game.Citizens;
using Game.Common;
using Game.Economy;
using Unity.Collections;
using Unity.Entities;

namespace Scholarship
{
    public partial class ScholarshipSystem : GameSystemBase
    {
        // Sixteen passes a day; each pass rolls a sixteenth of the daily odds.
        public const int kUpdatesPerDay = 16;
        public const int kDailyOddsOneIn = 2000;
        public const int kGrant = 5000;

        private EntityQuery m_CitizenQuery;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 262144 / kUpdatesPerDay;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Age is packed into Citizen.m_State, so every citizen is a candidate at query time
            // and the age test happens per entity below.
            m_CitizenQuery = GetEntityQuery(
                ComponentType.ReadOnly<Citizen>(),
                ComponentType.ReadOnly<HouseholdMember>(),
                ComponentType.Exclude<Deleted>());

            RequireForUpdate(m_CitizenQuery);
        }

        protected override void OnUpdate()
        {
            // A fresh seed per pass. Citizen.m_PseudoRandom is the citizen's fixed personality
            // seed and is never written here.
            var random = RandomSeed.Next().GetRandom(0);
            var entities = m_CitizenQuery.ToEntityArray(Allocator.Temp);
            var citizens = m_CitizenQuery.ToComponentDataArray<Citizen>(Allocator.Temp);

            for (var i = 0; i < entities.Length; i++)
            {
                var citizen = citizens[i];

                // Tourists and commuters are citizens with households of their own.
                var visitor = CitizenFlags.Tourist | CitizenFlags.Commuter;
                if ((citizen.m_State & visitor) != CitizenFlags.None)
                {
                    continue;
                }

                var age = citizen.GetAge();
                if (age != CitizenAge.Teen && age != CitizenAge.Adult)
                {
                    continue;
                }

                // Levels 0-3 and level 4 are packed differently; only the accessor decodes them.
                if (citizen.GetEducationLevel() >= 3)
                {
                    continue;
                }

                if (random.NextInt(kDailyOddsOneIn * kUpdatesPerDay) != 0)
                {
                    continue;
                }

                var entity = entities[i];
                var household = EntityManager.GetComponentData<HouseholdMember>(entity).m_Household;

                // Household.m_Resources is the stock of shopped goods; money is the Money entry of
                // the household's Resources buffer.
                var resources = EntityManager.GetBuffer<Resources>(household);
                EconomyUtils.AddResources(Resource.Money, kGrant, resources);

                Mod.Log.Info(
                    $"Scholarship of {kGrant} awarded to citizen {entity.Index}, " +
                    $"household {household.Index}.");
            }

            citizens.Dispose();
            entities.Dispose();
        }
    }
}
