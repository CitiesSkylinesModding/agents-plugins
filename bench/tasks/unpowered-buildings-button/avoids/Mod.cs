using Colossal.Logging;
using Game;
using Game.Modding;

namespace UnpoweredCounter
{
    public class Mod : IMod
    {
        public const string Id = nameof(UnpoweredCounter);

        public static readonly ILog Log = LogManager.GetLogger(Id);

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<UnpoweredCounterUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
        }
    }
}
