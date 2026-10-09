using Colossal.Logging;
using Game;
using Game.Modding;

namespace AbandonedWatch
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(AbandonedWatch));

        // Flipped from the developer menu page.
        public static bool Verbose;

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<AbandonedCounterSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
