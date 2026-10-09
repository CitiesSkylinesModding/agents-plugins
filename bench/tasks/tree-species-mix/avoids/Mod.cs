using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Tools;

namespace TreeMix
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(TreeMix));

        public void OnLoad(UpdateSystem updateSystem)
        {
            // Spliced ahead of the consumer: a plain UpdateAt in this phase would land after it.
            updateSystem.UpdateBefore<TreeMixSystem, GenerateObjectsSystem>(
                SystemUpdatePhase.Modification1);
        }

        public void OnDispose()
        {
        }
    }
}
