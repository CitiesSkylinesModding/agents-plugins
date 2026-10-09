using Colossal.Logging;
using Game;
using Game.Modding;

namespace DailyCitySnapshot
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(DailyCitySnapshot));

        public void OnLoad(UpdateSystem updateSystem)
        {
            // The system's [UpdateAfter] attribute places it behind the statistics system.
            updateSystem.UpdateAt<DailyCitySnapshotSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
