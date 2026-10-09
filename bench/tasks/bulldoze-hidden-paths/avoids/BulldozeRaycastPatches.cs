using System;
using Game.Common;
using Game.Tools;
using HarmonyLib;
using Unity.Entities;

namespace BulldozeHiddenPaths
{
    // The raycast system asks only the active tool to configure the raycast, and the bulldozer
    // admits marker geometry only in the editor, so widening it in a game takes a patch.
    [HarmonyPatch]
    internal static class BulldozeRaycastPatches
    {
        // Whether this mod, rather than the game or another mod, turned the markers flag on this
        // frame. Only the hits this mod's widening caused are filtered below.
        private static bool s_WeSetMarkers;

        [HarmonyPatch(typeof(BulldozeToolSystem), nameof(BulldozeToolSystem.InitializeRaycast))]
        [HarmonyPostfix]
        private static void WidenRaycast(ToolRaycastSystem ___m_ToolRaycastSystem)
        {
            s_WeSetMarkers = false;

            if ((___m_ToolRaycastSystem.raycastFlags & RaycastFlags.Markers) != 0)
            {
                return;
            }

            ___m_ToolRaycastSystem.raycastFlags |= RaycastFlags.Markers;
            s_WeSetMarkers = true;
        }

        // GetRaycastResult is declared on ToolBaseSystem and inherited by the bulldozer, and the
        // name carries four signatures there, so the attribute names the base type and pins the
        // overload.
        [HarmonyPatch(
            typeof(ToolBaseSystem),
            "GetRaycastResult",
            new Type[] { typeof(Entity), typeof(RaycastHit) },
            new ArgumentType[] { ArgumentType.Out, ArgumentType.Out })]
        [HarmonyPostfix]
        private static void RejectMarkerObjects(
            ToolBaseSystem __instance,
            ref bool __result,
            ref Entity entity,
            ref RaycastHit hit)
        {
            if (!__result || !s_WeSetMarkers || !(__instance is BulldozeToolSystem))
            {
                return;
            }

            var entityManager = __instance.EntityManager;

            if (!entityManager.HasComponent<Game.Objects.Marker>(entity)
                && !entityManager.HasComponent<Game.Objects.Marker>(hit.m_HitEntity))
            {
                return;
            }

            __result = false;
            entity = Entity.Null;
            hit = default;
        }
    }
}
