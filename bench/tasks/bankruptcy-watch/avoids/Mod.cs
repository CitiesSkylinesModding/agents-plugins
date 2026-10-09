using Colossal.Logging;
using Game;
using Game.Modding;

namespace BankruptcyWatch
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(BankruptcyWatch));

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<BankruptcyWatchSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
