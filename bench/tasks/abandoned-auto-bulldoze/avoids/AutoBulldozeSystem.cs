using System.Collections.Generic;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Tools;
using Unity.Entities;

namespace AbandonedAutoBulldoze
{
    public partial class AutoBulldozeSystem : GameSystemBase
    {
        private EntityQuery m_AbandonedQuery;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 64;

        protected override void OnCreate()
        {
            base.OnCreate();

            SetProtectedType(null);
        }

        /// <summary>
        /// Rebuilds the query around the component Landmark Keeper flags kept buildings with,
        /// or around none when that mod is absent.
        /// </summary>
        public void SetProtectedType(ComponentType? protectedType)
        {
            var none = new List<ComponentType>
            {
                ComponentType.ReadOnly<Deleted>(),
                ComponentType.ReadOnly<Temp>(),
            };

            if (protectedType.HasValue)
            {
                none.Add(protectedType.Value);
            }

            m_AbandonedQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Abandoned>(),
                },
                None = none.ToArray(),
            });
        }

        protected override void OnUpdate()
        {
            if (m_AbandonedQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            // Marking the building deleted is what the bulldozer does; the game's own cleanup
            // systems take it from there.
            EntityManager.AddComponent<Deleted>(m_AbandonedQuery);
        }
    }
}
