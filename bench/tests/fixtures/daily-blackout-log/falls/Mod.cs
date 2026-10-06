using Colossal.Logging;
using Game;
using Game.Modding;

namespace BlackoutLog
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(BlackoutLog));

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<BlackoutLogSystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
        }
    }
}
