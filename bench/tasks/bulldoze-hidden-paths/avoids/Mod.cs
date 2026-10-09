using System.Linq;
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

        private Harmony m_Harmony;

        public void OnLoad(UpdateSystem updateSystem)
        {
            // The mod object is created without running a constructor, so the Harmony instance is
            // built here rather than in a field initializer.
            m_Harmony = new Harmony(HarmonyId);
            m_Harmony.PatchAll(typeof(Mod).Assembly);

            Log.Info($"Applied {m_Harmony.GetPatchedMethods().Count()} patches.");
        }

        public void OnDispose()
        {
            m_Harmony?.UnpatchAll(HarmonyId);
        }
    }
}
