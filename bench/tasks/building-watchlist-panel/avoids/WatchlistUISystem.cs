using System.Collections.Generic;
using Colossal.UI.Binding;
using Game;
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

        // The panel exists in a loaded city only; the default keeps the system running in the
        // main menu and the editor too, against a world with no city in it.
        public override GameMode gameMode => GameMode.Game;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_SelectedInfoUISystem = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();

            AddBinding(m_EntriesBinding =
                new ValueBinding<List<WatchEntry>>(Mod.Id, "entries", m_Entries));

            // The answer to an add is a one-shot question the frontend cannot compute, so it comes
            // back through the call's own promise rather than a value pushed beside the trigger.
            AddBinding(new CallBinding<string>(Mod.Id, "watchSelected", WatchSelected));
            AddBinding(new TriggerBinding<Entity>(Mod.Id, "unwatch", Unwatch));
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            var changed = false;

            for (var i = 0; i < m_Entries.Count; i++)
            {
                var entry = m_Entries[i];
                var powered = IsPowered(entry.entity);

                if (entry.powered != powered)
                {
                    entry.powered = powered;
                    m_Entries[i] = entry;
                    changed = true;
                }
            }

            if (changed)
            {
                m_EntriesBinding.TriggerUpdate();
            }
        }

        // Returns the reason the selection was not added, or an empty string once it was.
        private string WatchSelected()
        {
            var selected = m_SelectedInfoUISystem.selectedEntity;

            if (selected == Entity.Null || !EntityManager.HasComponent<Building>(selected))
            {
                return "The selected object is not a building.";
            }

            if (m_Entries.Exists(entry => entry.entity == selected))
            {
                return "That building is already on the watchlist.";
            }

            m_Entries.Add(new WatchEntry
            {
                entity = selected,
                name = m_NameSystem.GetRenderedLabelName(selected),
                powered = IsPowered(selected),
            });

            // The list is the instance the binding already holds, so Update(m_Entries) would
            // compare equal and push nothing; TriggerUpdate pushes it as it is now.
            m_EntriesBinding.TriggerUpdate();

            return string.Empty;
        }

        private void Unwatch(Entity entity)
        {
            if (m_Entries.RemoveAll(entry => entry.entity == entity) > 0)
            {
                m_EntriesBinding.TriggerUpdate();
            }
        }

        private bool IsPowered(Entity entity)
        {
            return EntityManager.TryGetComponent(entity, out ElectricityConsumer consumer)
                && consumer.electricityConnected;
        }
    }
}
