using Colossal.Logging;
using Game;
using Game.Modding;

namespace PowerOutages
{
    public class Mod : IMod
    {
        public const string Id = nameof(PowerOutages);

        public static readonly ILog Log = LogManager.GetLogger(Id);

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<OutageUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
        }
    }
}
