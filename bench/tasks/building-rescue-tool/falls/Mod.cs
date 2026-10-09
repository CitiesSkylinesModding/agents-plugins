using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;

namespace BuildingRescue
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(BuildingRescue));

        public static BuildingRescueSettings Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Settings = new BuildingRescueSettings(this);
            Settings.RegisterInOptionsUI();
            Settings.RegisterKeyBindings();
            AssetDatabase.global.LoadSettings(
                nameof(BuildingRescue), Settings, new BuildingRescueSettings(this));

            updateSystem.UpdateAt<BuildingRescueToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<BuildingRescueTooltipSystem>(SystemUpdatePhase.UIUpdate);
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
