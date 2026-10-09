using Colossal.Logging;
using Game;
using Game.Modding;

namespace CityStatsExport
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.Default;

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
