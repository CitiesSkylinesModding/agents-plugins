using Colossal.Logging;
using Game;
using Game.Modding;

namespace InstantTrees
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(InstantTrees));

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<InstantTreesSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
