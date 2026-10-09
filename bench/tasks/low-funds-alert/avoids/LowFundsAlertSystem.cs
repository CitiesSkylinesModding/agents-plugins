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

        // A registered action is inert until the mod enables it; the system's running state is
        // the switch, so the hotkey is live exactly while the system is.
        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            m_ToggleAction.shouldBeEnabled = true;
        }

        protected override void OnStopRunning()
        {
            base.OnStopRunning();

            m_ToggleAction.shouldBeEnabled = false;
        }

        protected override void OnUpdate()
        {
            var settings = Mod.Settings;

            if (m_ToggleAction.WasPerformedThisFrame())
            {
                settings.AlertEnabled = !settings.AlertEnabled;
                // The options-page toggle writes through ApplyAndSave from its own setter; a change
                // made from code has to make the same call to be applied and written.
                settings.ApplyAndSave();
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
