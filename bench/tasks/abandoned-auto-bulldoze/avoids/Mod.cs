using System;
using Colossal.Core;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Unity.Entities;

namespace AbandonedAutoBulldoze
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(AbandonedAutoBulldoze));

        private bool m_Registered;
        private ParadoxModsDataSource m_Mods;

        public void OnLoad(UpdateSystem updateSystem)
        {
            // The mod manager is filled before any mod's OnLoad runs, so it is the one question
            // answerable here; it says whether Landmark Keeper will load, not that it has.
            Log.Info(LandmarkKeeperBridge.IsRegistered()
                ? "Landmark Keeper is in the playset, flagged buildings will be kept."
                : "Landmark Keeper is not in the playset, every abandoned building goes.");

            // Everything that touches Landmark Keeper's code waits a frame: by then every mod's
            // OnLoad has returned, and the component type exists if it is going to.
            MainThreadDispatcher.RegisterUpdater(ApplyModSet);

            // A mod enabled mid-session runs its OnLoad there and then, so the answer is asked
            // again whenever the player changes the mod set.
            m_Mods = AssetDatabase<ParadoxMods>.instance.dataSource as ParadoxModsDataSource;

            if (m_Mods != null)
            {
                m_Mods.onAfterActivePlaysetOrModStatusChanged += OnModSetChanged;
            }
        }

        public void OnDispose()
        {
            if (m_Mods != null)
            {
                m_Mods.onAfterActivePlaysetOrModStatusChanged -= OnModSetChanged;
            }
        }

        private void OnModSetChanged()
        {
            LandmarkKeeperBridge.Reset();
            ApplyModSet();
        }

        // Runs from the dispatcher tick and from the mod-set event, neither of which guards
        // against a throw, so nothing may escape it.
        private void ApplyModSet()
        {
            try
            {
                var world = World.DefaultGameObjectInjectionWorld;

                if (!m_Registered)
                {
                    world.GetExistingSystemManaged<UpdateSystem>()
                        .UpdateAt<AutoBulldozeSystem>(SystemUpdatePhase.GameSimulation);
                    m_Registered = true;
                }

                world.GetExistingSystemManaged<AutoBulldozeSystem>()
                    .SetProtectedType(LandmarkKeeperBridge.ResolveProtectedType());
            }
            catch (Exception e)
            {
                Log.Error(e, "Could not apply the mod set; abandoned buildings are left alone.");
            }
        }
    }
}
