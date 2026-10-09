using Colossal.Logging;
using Game;
using Game.Modding;

namespace UtilityLedger
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(UtilityLedger));

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<UtilityLedgerSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
