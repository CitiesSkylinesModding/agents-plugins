using Game.Areas;
using Game.Common;
using Game.Net;
using Game.Tools;
using HarmonyLib;
using Unity.Entities;

namespace BulldozeHiddenPaths
{
    [HarmonyPatch]
    internal static class BulldozeRaycastPatches
    {
        // The editor-only check sits inside the vanilla method, so the setup is replaced wholesale
        // with the in-game configuration plus the markers.
        [HarmonyPatch(typeof(BulldozeToolSystem), nameof(BulldozeToolSystem.InitializeRaycast))]
        [HarmonyPrefix]
        private static bool ReplaceRaycastSetup(
            BulldozeToolSystem __instance,
            ToolRaycastSystem ___m_ToolRaycastSystem)
        {
            var raycast = ___m_ToolRaycastSystem;

            raycast.raycastFlags = RaycastFlags.Markers
                | RaycastFlags.BuildingLots
                | RaycastFlags.SubBuildings
                | RaycastFlags.Placeholders
                | RaycastFlags.Decals;
            raycast.typeMask = TypeMask.StaticObjects | TypeMask.Net;
            raycast.netLayerMask = Layer.All;
            raycast.areaTypeMask = AreaTypeMask.None;
            raycast.rayOffset = default;
            raycast.owner = Entity.Null;

            if (__instance.underground)
            {
                raycast.collisionMask = CollisionMask.Underground;
            }
            else
            {
                raycast.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;
                raycast.typeMask |= TypeMask.Areas;
                raycast.areaTypeMask = AreaTypeMask.Lots | AreaTypeMask.Surfaces;
            }

            switch (__instance.actualMode)
            {
                case BulldozeToolSystem.Mode.SubElements:
                    raycast.raycastFlags |= RaycastFlags.SubElements | RaycastFlags.NoMainElements;
                    break;
                case BulldozeToolSystem.Mode.Everything:
                    raycast.raycastFlags |= RaycastFlags.SubElements;
                    break;
            }

            if (__instance.allowManipulation)
            {
                raycast.typeMask |= TypeMask.MovingObjects;
            }

            return false;
        }

        // Marker objects become hittable along with the paths, so they are filtered back out.
        [HarmonyPatch(typeof(BulldozeToolSystem), "GetRaycastResult")]
        [HarmonyPostfix]
        private static void RejectMarkerObjects(
            BulldozeToolSystem __instance,
            ref bool __result,
            ref Entity entity,
            ref RaycastHit hit)
        {
            if (!__result)
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
