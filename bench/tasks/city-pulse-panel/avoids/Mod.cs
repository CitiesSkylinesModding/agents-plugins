using Colossal.Logging;
using Game;
using Game.Modding;

namespace CityPulse
{
    public class Mod : IMod
    {
        public const string Id = nameof(CityPulse);

        public static readonly ILog Log = LogManager.GetLogger(Id);

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<CityPulseUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
        }
    }
}
