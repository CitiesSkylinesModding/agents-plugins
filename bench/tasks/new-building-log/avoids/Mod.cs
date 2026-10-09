using Colossal.Logging;
using Game;
using Game.Modding;

namespace NewBuildingLog
{
    public class Mod : IMod
    {
        // Static on purpose: the loader allocates the mod object without running a constructor,
        // so an instance field initialiser on this class would never run.
        public static readonly ILog Log = LogManager.GetLogger(nameof(NewBuildingLog))
            .SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("Loading");

            // Splice the system directly behind the game's building initialiser. The phase has
            // to be the one that system is registered in, or the splice is silently dropped.
            updateSystem.UpdateAfter<NewBuildingLogSystem, Game.Buildings.InitializeSystem>(
                SystemUpdatePhase.Modification2);
        }

        public void OnDispose()
        {
        }
    }
}
