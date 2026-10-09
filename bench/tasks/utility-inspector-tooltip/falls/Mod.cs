using Colossal.Logging;
using Game;
using Game.Modding;

namespace UtilityInspector
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(UtilityInspector));

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<UtilityInspectorTooltipSystem>(SystemUpdatePhase.UITooltip);
        }

        public void OnDispose()
        {
        }
    }
}
