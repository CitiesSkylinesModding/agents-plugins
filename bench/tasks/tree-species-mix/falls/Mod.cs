using Colossal.Logging;
using Game;
using Game.Modding;

namespace TreeMix
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(TreeMix));

        public void OnLoad(UpdateSystem updateSystem)
        {
            updateSystem.UpdateAt<TreeMixSystem>(SystemUpdatePhase.Modification1);
        }

        public void OnDispose()
        {
        }
    }
}
