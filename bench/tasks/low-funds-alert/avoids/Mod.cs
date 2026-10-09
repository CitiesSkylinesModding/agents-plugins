using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace LowFundsAlert
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(LowFundsAlert));

        public static LowFundsAlertSettings Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Settings = new LowFundsAlertSettings(this);
            Settings.RegisterInOptionsUI();
            Settings.RegisterKeyBindings();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));

            // The third argument is the reference the save path diffs against: only the values the
            // player changed are written. Without it every property is written on every save.
            AssetDatabase.global.LoadSettings(
                nameof(LowFundsAlert),
                Settings,
                new LowFundsAlertSettings(this));

            updateSystem.UpdateAt<LowFundsAlertSystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
