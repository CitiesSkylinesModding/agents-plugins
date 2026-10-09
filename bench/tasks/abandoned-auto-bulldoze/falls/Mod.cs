using System.Linq;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace AbandonedAutoBulldoze
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(AbandonedAutoBulldoze));

        // Whether Landmark Keeper is part of this playset, decided once when the mod loads.
        public static bool LandmarkKeeperPresent { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            LandmarkKeeperPresent = GameManager.instance.modManager
                .ListModsEnabled()
                .Any(name => name.StartsWith("LandmarkKeeper"));

            Log.Info(LandmarkKeeperPresent
                ? "Landmark Keeper found, flagged buildings will be kept."
                : "Landmark Keeper not found, every abandoned building goes.");

            updateSystem.UpdateAt<AutoBulldozeSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
