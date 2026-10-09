using Colossal.Logging;
using Game;
using Game.Modding;

namespace CityDiary
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(CityDiary));

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<CityDiarySystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }
}
