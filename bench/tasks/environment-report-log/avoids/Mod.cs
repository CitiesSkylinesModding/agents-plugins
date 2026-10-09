using Colossal.Logging;
using Game;
using Game.Modding;

namespace EnvironmentReport
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(EnvironmentReport));

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<EnvironmentReportSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
