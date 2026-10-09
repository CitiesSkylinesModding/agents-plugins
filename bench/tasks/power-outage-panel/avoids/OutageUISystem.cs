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
        // Simulation frames between two scans; frameIndex stops while paused, as power does.
        private const int kScanInterval = 64;

        private NameSystem m_NameSystem;
        private EntityQuery m_ConsumerQuery;
        private UIUpdateState m_ScanState;
        private readonly HashSet<Entity> m_Unpowered = new HashSet<Entity>();
        private ValueBinding<List<OutageEntry>> m_ListBinding;
        private EventBinding<OutageEntry> m_OutageEvent;

        // The panel exists in a loaded city only; the default keeps the system running in the
        // main menu and the editor too, against a world with no city in it.
        public override GameMode gameMode => GameMode.Game;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_ConsumerQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<ElectricityConsumer>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            // GetUpdateInterval is read but never honoured on the UIUpdate phase, so the throttle
            // is the game's own UIUpdateState, advanced from OnUpdate.
            m_ScanState = UIUpdateState.Create(World, kScanInterval);

            AddBinding(m_ListBinding =
                new ValueBinding<List<OutageEntry>>(Mod.Id, "unpowered", new List<OutageEntry>()));

            // A value binding drops an update equal to the one it holds, so the same building going
            // dark twice in a row would never reach the panel; an event carries every occurrence.
            AddBinding(m_OutageEvent = new EventBinding<OutageEntry>(Mod.Id, "outage"));
            AddUpdateBinding(new GetterValueBinding<int>(Mod.Id, "count", () => m_Unpowered.Count));
        }

        protected override void OnUpdate()
        {
            // The base pumps the bindings registered through AddUpdateBinding; without this call
            // the count getter runs on the first subscribe and never again.
            base.OnUpdate();

            // Nothing holds the list while the panel is closed, so the scan has no reader; the
            // forced update makes the first frame after it opens scan at once.
            if (!m_ListBinding.active)
            {
                m_ScanState.ForceUpdate();
                return;
            }

            if (!m_ScanState.Advance())
            {
                return;
            }

            Scan();
        }

        private void Scan()
        {
            var entities = m_ConsumerQuery.ToEntityArray(Allocator.Temp);
            var consumers =
                m_ConsumerQuery.ToComponentDataArray<ElectricityConsumer>(Allocator.Temp);
            var current = new HashSet<Entity>();
            var list = new List<OutageEntry>();
            var changed = false;

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
                    changed = true;
                    m_OutageEvent.Trigger(entry);
                }
            }

            entities.Dispose();
            consumers.Dispose();

            if (changed || current.Count != m_Unpowered.Count)
            {
                m_Unpowered.Clear();
                m_Unpowered.UnionWith(current);
                m_ListBinding.Update(list);
            }
        }
    }
}
