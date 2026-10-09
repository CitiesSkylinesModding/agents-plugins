using Colossal.UI.Binding;
using Game;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Game.UI.InGame;
using Unity.Collections;
using Unity.Entities;

namespace CityPulse
{
    public partial class CityPulseUISystem : UISystemBase
    {
        private CitySystem m_CitySystem;
        private EntityQuery m_ElectricityQuery;
        private EntityQuery m_WaterQuery;

        // The panel exists in a loaded city only.
        public override GameMode gameMode => GameMode.Game;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();

            m_ElectricityQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<ElectricityConsumer>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_WaterQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<WaterConsumer>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            // Registering the panel with the game's panel system is what lets the frontend open it
            // by its type name through game.togglePanel, and what closes it when another panel
            // opens, since that system holds the single active panel.
            World.GetOrCreateSystemManaged<GamePanelUISystem>().SetDefaultArgs(new PulsePanel());

            AddUpdateBinding(new GetterValueBinding<int>(Mod.Id, "population", GetPopulation));
            AddUpdateBinding(new GetterValueBinding<int>(Mod.Id, "unpowered", CountUnpowered));
            AddUpdateBinding(new GetterValueBinding<int>(Mod.Id, "unwatered", CountUnwatered));
        }

        private int GetPopulation()
        {
            return EntityManager.TryGetComponent(m_CitySystem.City, out Population population)
                ? population.m_Population
                : 0;
        }

        private int CountUnpowered()
        {
            using var consumers =
                m_ElectricityQuery.ToComponentDataArray<ElectricityConsumer>(Allocator.Temp);

            var count = 0;

            foreach (var consumer in consumers)
            {
                if (!consumer.electricityConnected)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountUnwatered()
        {
            using var consumers = m_WaterQuery.ToComponentDataArray<WaterConsumer>(Allocator.Temp);

            var count = 0;

            foreach (var consumer in consumers)
            {
                if (!consumer.waterConnected)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
