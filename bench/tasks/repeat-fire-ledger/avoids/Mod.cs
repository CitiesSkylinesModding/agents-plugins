using Colossal.Logging;
using Game;
using Game.Modding;

namespace FireLedger
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(FireLedger));

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<FireLedgerSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
