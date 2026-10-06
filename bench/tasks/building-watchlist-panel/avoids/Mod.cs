using Colossal.Logging;
using Game;
using Game.Modding;

namespace BuildingWatchlist
{
    public class Mod : IMod
    {
        public const string Id = nameof(BuildingWatchlist);

        public static readonly ILog Log = LogManager.GetLogger(Id);

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<WatchlistUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
        }
    }
}
