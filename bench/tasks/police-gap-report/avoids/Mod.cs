using Colossal.Logging;
using Game;
using Game.Modding;

namespace PoliceGapReport
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(PoliceGapReport));

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<PoliceGapReportSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
