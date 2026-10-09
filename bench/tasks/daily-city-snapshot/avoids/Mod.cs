using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Simulation;

namespace DailyCitySnapshot
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(DailyCitySnapshot));

        public void OnLoad(UpdateSystem updateSystem)
        {
            // Ordering is imperative in this game: the snapshot is spliced right behind the
            // statistics system, in the phase the game registers that system in.
            updateSystem.UpdateAfter<DailyCitySnapshotSystem, CityStatisticsSystem>(
                SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
