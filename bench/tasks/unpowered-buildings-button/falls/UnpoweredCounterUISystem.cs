using Colossal.UI.Binding;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Tools;
using Game.UI;
using Game.UI.InGame;
using Unity.Collections;
using Unity.Entities;

namespace UnpoweredCounter
{
    public partial class UnpoweredCounterUISystem : UISystemBase
    {
        private EntityQuery m_ConsumerQuery;
        private SelectedInfoUISystem m_SelectedInfoUISystem;
        private ValueBinding<int> m_CountBinding;
        private Entity m_LastFocused;

        // The button lives in the in-game screen only.
        public override GameMode gameMode => GameMode.Game;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SelectedInfoUISystem = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();

            m_ConsumerQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<ElectricityConsumer>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            AddBinding(m_CountBinding = new ValueBinding<int>(Mod.Id, "unpoweredCount", 0));
            AddBinding(new TriggerBinding(Mod.Id, "focusNext", FocusNext));
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            // Update compares against the last pushed value, so an unchanged count sends nothing.
            m_CountBinding.Update(CountUnpowered());
        }

        private int CountUnpowered()
        {
            var consumers = m_ConsumerQuery.ToComponentDataArray<ElectricityConsumer>(Allocator.Temp);
            var count = 0;

            for (var i = 0; i < consumers.Length; i++)
            {
                if (!consumers[i].electricityConnected)
                {
                    count++;
                }
            }

            consumers.Dispose();

            return count;
        }

        // Selects and follows the first unpowered building after the one focused last time,
        // wrapping around so repeated clicks cycle through all of them.
        private void FocusNext()
        {
            var entities = m_ConsumerQuery.ToEntityArray(Allocator.Temp);
            var consumers = m_ConsumerQuery.ToComponentDataArray<ElectricityConsumer>(Allocator.Temp);
            var start = 0;

            for (var i = 0; i < entities.Length; i++)
            {
                if (entities[i] == m_LastFocused)
                {
                    start = i + 1;
                    break;
                }
            }

            for (var step = 0; step < entities.Length; step++)
            {
                var i = (start + step) % entities.Length;

                if (consumers[i].electricityConnected)
                {
                    continue;
                }

                m_LastFocused = entities[i];
                m_SelectedInfoUISystem.SetSelection(m_LastFocused);
                m_SelectedInfoUISystem.Focus(m_LastFocused);
                break;
            }

            entities.Dispose();
            consumers.Dispose();
        }
    }
}
