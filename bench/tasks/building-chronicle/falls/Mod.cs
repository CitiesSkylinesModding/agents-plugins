using Colossal.Logging;
using Game;
using Game.Modding;

namespace BuildingChronicle
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(BuildingChronicle));

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<ChronicleSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<BuildingRecordSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
