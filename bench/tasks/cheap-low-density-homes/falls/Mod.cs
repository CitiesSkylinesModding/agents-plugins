using Colossal.Logging;
using Game;
using Game.Modding;

namespace CheapHomes
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(CheapHomes));

        public void OnLoad(UpdateSystem updateSystem)
        {
            // The system only listens for the load hook, so it needs no phase.
            updateSystem.World.GetOrCreateSystemManaged<CheapHomesSystem>();
        }

        public void OnDispose()
        {
        }
    }
}
