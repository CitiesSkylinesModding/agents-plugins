using Colossal.Logging;
using Game;
using Game.Modding;

namespace Scholarship
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(Scholarship));

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<ScholarshipSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
