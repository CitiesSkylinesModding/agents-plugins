using Colossal.Logging;
using Game;
using Game.Modding;

namespace LineStatusLog
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(LineStatusLog));

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<LineStatusLogSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
