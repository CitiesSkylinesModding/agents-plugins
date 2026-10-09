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

            // The tooltip UI system clears its lists, drives UITooltip, then reads them back, so a
            // tooltip added from any other phase is wiped before it is ever read.
            updateSystem.UpdateAt<BuildingRescueTooltipSystem>(SystemUpdatePhase.UITooltip);
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
