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
            LargeSchoolRegistrar.Register(updateSystem.World);
        }

        public void OnDispose()
        {
        }
    }
}
