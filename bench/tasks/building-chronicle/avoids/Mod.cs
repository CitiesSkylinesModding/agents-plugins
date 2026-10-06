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
            // Both systems are created here, during OnLoad, so each has its save section before
            // the first save. Order within a phase is registration order: the chronicle's
            // post-load repair runs before the stamping system sees the same frame.
            updateSystem.UpdateAt<ChronicleSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<BuildingRecordSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
