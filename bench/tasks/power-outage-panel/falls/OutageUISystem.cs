using System.Collections.Generic;
using Colossal.UI.Binding;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Tools;
using Game.UI;
using Unity.Collections;
using Unity.Entities;

namespace PowerOutages
{
    public partial class OutageUISystem : UISystemBase
    {
        private NameSystem m_NameSystem;
        private EntityQuery m_ConsumerQuery;
        private readonly HashSet<Entity> m_Unpowered = new HashSet<Entity>();
        private ValueBinding<List<OutageEntry>> m_ListBinding;
        private ValueBinding<OutageEntry> m_LatestBinding;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_ConsumerQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<ElectricityConsumer>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            AddBinding(m_ListBinding =
                new ValueBinding<List<OutageEntry>>(Mod.Id, "unpowered", new List<OutageEntry>()));
            AddBinding(m_LatestBinding = new ValueBinding<OutageEntry>(
                Mod.Id, "latest", new OutageEntry(Entity.Null, string.Empty)));
            AddUpdateBinding(new GetterValueBinding<int>(Mod.Id, "count", () => m_Unpowered.Count));
        }

        // Scanning every building each frame is wasteful; about once a second is plenty.
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 64;
        }

        protected override void OnUpdate()
        {
            var entities = m_ConsumerQuery.ToEntityArray(Allocator.Temp);
            var consumers =
                m_ConsumerQuery.ToComponentDataArray<ElectricityConsumer>(Allocator.Temp);
            var current = new HashSet<Entity>();
            var list = new List<OutageEntry>();

            for (var i = 0; i < entities.Length; i++)
            {
                if (consumers[i].electricityConnected)
                {
                    continue;
                }

                var entity = entities[i];
                var entry = new OutageEntry(entity, m_NameSystem.GetRenderedLabelName(entity));

                current.Add(entity);
                list.Add(entry);

                if (!m_Unpowered.Contains(entity))
                {
                    m_LatestBinding.Update(entry);
                }
            }

            entities.Dispose();
            consumers.Dispose();

            m_Unpowered.Clear();
            m_Unpowered.UnionWith(current);
            m_ListBinding.Update(list);
        }
    }
}
