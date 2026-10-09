using System;
using System.Linq;
using Game.SceneFlow;
using Unity.Entities;

namespace AbandonedAutoBulldoze
{
    /// <summary>
    /// Everything that touches Landmark Keeper, by name only.
    /// A reference to its assembly would stop this mod from loading whenever that one is absent.
    /// </summary>
    public static class LandmarkKeeperBridge
    {
        private const string kAssemblyName = "LandmarkKeeper";
        private const string kProtectedTypeName = "LandmarkKeeper.Protected";

        private static bool s_Resolved;
        private static ComponentType? s_ProtectedType;

        /// <summary>
        /// Answerable from OnLoad: the manager lists every mod that will load before any has.
        /// </summary>
        public static bool IsRegistered()
        {
            foreach (var modInfo in GameManager.instance.modManager)
            {
                if (modInfo.isValid
                    && modInfo.asset.canBeLoaded
                    && modInfo.asset.name == kAssemblyName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Only once every mod has loaded. Cached until <see cref="Reset"/>.
        /// </summary>
        public static ComponentType? ResolveProtectedType()
        {
            if (s_Resolved)
            {
                return s_ProtectedType;
            }

            s_Resolved = true;
            s_ProtectedType = null;

            // A mod assembly is loaded from bytes, so the assembly-qualified lookup can miss it
            // and the scan over the loaded assemblies is the route that finds it.
            var type = Type.GetType($"{kProtectedTypeName}, {kAssemblyName}");

            if (type == null)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == kAssemblyName);

                if (assembly == null)
                {
                    Mod.Log.Info("Landmark Keeper is not loaded; every abandoned building goes.");
                    return null;
                }

                type = assembly.GetType(kProtectedTypeName);

                if (type == null)
                {
                    Mod.Log.Warn(
                        $"Landmark Keeper is loaded but declares no {kProtectedTypeName}; "
                        + "its flags cannot be honoured.");
                    return null;
                }
            }

            s_ProtectedType = ComponentType.ReadOnly(type);
            return s_ProtectedType;
        }

        public static void Reset()
        {
            s_Resolved = false;
        }
    }
}
