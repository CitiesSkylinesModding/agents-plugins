using System.Collections.Generic;
using Colossal.UI.Binding;
using Game.Buildings;
using Game.UI;
using Game.UI.InGame;
using Unity.Entities;

namespace BuildingWatchlist
{
    public partial class WatchlistUISystem : UISystemBase
    {
        private NameSystem m_NameSystem;
        private SelectedInfoUISystem m_SelectedInfoUISystem;
        private readonly List<WatchEntry> m_Entries = new List<WatchEntry>();
        private ValueBinding<List<WatchEntry>> m_EntriesBinding;
        private ValueBinding<string> m_MessageBinding;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_SelectedInfoUISystem = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();

            AddBinding(m_EntriesBinding =
                new ValueBinding<List<WatchEntry>>(Mod.Id, "entries", m_Entries));

            // Power is live, so a getter reads it fresh each time the panel asks.
            AddBinding(new GetterValueBinding<List<bool>>(Mod.Id, "power", GetPowerStates));

            AddBinding(m_MessageBinding =
                new ValueBinding<string>(Mod.Id, "message", string.Empty));
            AddBinding(new TriggerBinding(Mod.Id, "watchSelected", WatchSelected));
            AddBinding(new TriggerBinding<Entity>(Mod.Id, "unwatch", Unwatch));
        }

        private void WatchSelected()
        {
            var selected = m_SelectedInfoUISystem.selectedEntity;

            if (selected == Entity.Null || !EntityManager.HasComponent<Building>(selected))
            {
                m_MessageBinding.Update("The selected object is not a building.");
                return;
            }

            if (m_Entries.Exists(entry => entry.Entity == selected))
            {
                m_MessageBinding.Update("That building is already on the watchlist.");
                return;
            }

            m_Entries.Add(new WatchEntry
            {
                Entity = selected,
                Name = m_NameSystem.GetRenderedLabelName(selected),
            });

            m_EntriesBinding.Update(m_Entries);
            m_MessageBinding.Update(string.Empty);
        }

        private void Unwatch(Entity entity)
        {
            m_Entries.RemoveAll(entry => entry.Entity == entity);
            m_EntriesBinding.Update(m_Entries);
        }

        private List<bool> GetPowerStates()
        {
            var states = new List<bool>(m_Entries.Count);

            foreach (var entry in m_Entries)
            {
                states.Add(
                    EntityManager.TryGetComponent(entry.Entity, out ElectricityConsumer consumer)
                    && consumer.electricityConnected);
            }

            return states;
        }
    }
}
