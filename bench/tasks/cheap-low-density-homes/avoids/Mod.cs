using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Simulation;

namespace CheapHomes
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(CheapHomes));

        public void OnLoad(UpdateSystem updateSystem)
        {
            // The upkeep system only listens for the load hook, so it needs no phase.
            updateSystem.World.GetOrCreateSystemManaged<CheapHomesUpkeepSystem>();

            // The game's rent pass rewrites every rent it touches from its formula, so the cut is
            // applied right behind it, on the same frames, by a system that inherits its offset.
            updateSystem.UpdateAfter<CheapHomesRentSystem, RentAdjustSystem>(
                SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
