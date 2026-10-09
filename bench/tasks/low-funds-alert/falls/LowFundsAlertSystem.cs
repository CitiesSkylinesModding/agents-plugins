using Game;
using Game.City;
using Game.Input;
using Game.Simulation;
using Unity.Entities;

namespace LowFundsAlert
{
    public partial class LowFundsAlertSystem : GameSystemBase
    {
        private CitySystem m_CitySystem;
        private ProxyAction m_ToggleAction;
        private bool m_WasBelow;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_ToggleAction = Mod.Settings.GetAction(LowFundsAlertSettings.kToggleActionName);
        }

        protected override void OnUpdate()
        {
            var settings = Mod.Settings;

            if (m_ToggleAction.WasPerformedThisFrame())
            {
                settings.AlertEnabled = !settings.AlertEnabled;
                var state = settings.AlertEnabled ? "enabled" : "disabled";
                Mod.Log.Info($"Low funds alert {state}.");
            }

            var city = m_CitySystem.City;

            if (!settings.AlertEnabled || !EntityManager.HasComponent<PlayerMoney>(city))
            {
                m_WasBelow = false;
                return;
            }

            var money = EntityManager.GetComponentData<PlayerMoney>(city).money;
            var below = money < settings.Threshold;

            // Warn once per crossing rather than every frame spent below the line.
            if (below && !m_WasBelow)
            {
                Mod.Log.Warn(
                    $"City funds dropped to {money:N0}, "
                    + $"below the {settings.Threshold:N0} threshold.");
            }

            m_WasBelow = below;
        }
    }
}
