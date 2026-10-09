using Colossal.Logging;
using Game;
using Game.Modding;

namespace SmallStreetStopSigns
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(SmallStreetStopSigns));

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<SmallStreetStopSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
