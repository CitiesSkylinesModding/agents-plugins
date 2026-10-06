using Colossal.Logging;
using Game;
using Game.Modding;

namespace BigSchools
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(BigSchools));

        public void OnLoad(UpdateSystem updateSystem)
        {
            // The vanilla prefabs are not registered yet when OnLoad runs, so the work waits for
            // a load hook. The system gets no phase: it only listens for that hook.
            updateSystem.World.GetOrCreateSystemManaged<LargeSchoolSystem>();
        }

        public void OnDispose()
        {
        }
    }
}
