using Colossal.Logging;
using Game;
using Game.Modding;

namespace BuildingNotes
{
    public class Mod : IMod
    {
        public const string Id = nameof(BuildingNotes);

        public static readonly ILog Log = LogManager.GetLogger(Id);

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<BuildingNotesUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
        }
    }
}
