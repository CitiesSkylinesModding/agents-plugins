using Colossal.Logging;
using Game;
using Game.Modding;
using HarmonyLib;

namespace BulldozeHiddenPaths
{
    public class Mod : IMod
    {
        public const string HarmonyId = "BulldozeHiddenPaths";

        public static readonly ILog Log = LogManager.GetLogger(nameof(BulldozeHiddenPaths));

        private readonly Harmony m_Harmony = new Harmony(HarmonyId);

        public void OnLoad(UpdateSystem updateSystem)
        {
            m_Harmony.PatchAll(typeof(Mod).Assembly);

            Log.Info("Bulldozer patches applied.");
        }

        public void OnDispose()
        {
            m_Harmony.UnpatchAll(HarmonyId);
        }
    }
}
