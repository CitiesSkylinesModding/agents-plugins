using Colossal.Logging;
using Game;
using Game.Modding;

namespace NewBuildingLog
{
    public class Mod : IMod
    {
        public static Mod Instance { get; private set; }

        private readonly ILog m_Log = LogManager.GetLogger(nameof(NewBuildingLog))
            .SetShowsErrorsInUI(false);

        public ILog Log => m_Log;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Instance = this;
            m_Log.Info("Loading");

            // The simulation phase is where the city's building work happens; the attribute on
            // the system orders it after the game's own building initialisation.
            updateSystem.UpdateAt<NewBuildingLogSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
