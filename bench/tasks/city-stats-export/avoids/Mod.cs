using Colossal.Logging;
using Game;
using Game.Modding;

namespace CityStatsExport
{
    public class Mod : IMod
    {
        // Named after the mod so it gets a Logs/CityStatsExport.log of its own; an Error from it
        // would otherwise open the game's modal error dialog and pause the simulation.
        public static readonly ILog Log = LogManager
            .GetLogger(nameof(CityStatsExport))
            .SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("CityStatsExport loaded");

            // The system acts on the loading-complete hook only, so it needs no update phase.
            updateSystem.World.GetOrCreateSystemManaged<StatsExportSystem>();
        }

        public void OnDispose()
        {
        }
    }
}
