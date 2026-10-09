using System.Collections.Generic;
using Game;
using Game.Buildings;
using Game.Debug;
using Unity.Entities;
using UnityEngine.Rendering;

namespace AbandonedWatch
{
    // The system is both the counter the Watchers tab graphs and the container of the mod's own
    // page: the menu resolves a tab method on a system through the world, so one instance serves.
    // [DebugWatchOnly] hands Enabled to the Watchers tab, which turns the system on only while its
    // foldout is open or one of its values is watched.
    [DebugWatchOnly]
    [DebugContainer]
    public partial class AbandonedCounterSystem : GameSystemBase
    {
        // The sampler masks the frame index with (interval - 1), so the interval has to be a power
        // of two: 16384 is 16 samples a day, the closest to hourly that is one.
        public const int kSampleInterval = 16384;

        [DebugWatchValue(updateInterval = kSampleInterval, historyLength = 256)]
        private int m_AbandonedCount;

        private EntityQuery m_AbandonedQuery;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => kSampleInterval;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_AbandonedQuery = GetEntityQuery(ComponentType.ReadOnly<Abandoned>());

            // Off until the Watchers tab asks for it, as the game's own watch-only systems do.
            Enabled = false;
        }

        protected override void OnUpdate()
        {
            Recount();
        }

        private void Recount()
        {
            m_AbandonedCount = m_AbandonedQuery.CalculateEntityCount();

            if (Mod.Verbose)
            {
                Mod.Log.Info($"{m_AbandonedCount} abandoned buildings.");
            }
        }

        [DebugTab("Abandoned Buildings", 100)]
        private List<DebugUI.Widget> BuildAbandonedDebugUI()
        {
            return new List<DebugUI.Widget>
            {
                // A read-only Value is the kind the menu renders as a text row; MessageBox is not
                // in the translation and would vanish.
                new DebugUI.Value
                {
                    displayName = "Buildings currently flagged abandoned. "
                        + "Watch 'Abandoned Count' on the Watchers tab for the graph.",
                    getter = () => "",
                },
                // The getter runs on a timer while the page is open, so it only reads the field;
                // the query runs in OnUpdate at the sample interval or on the button below.
                new DebugUI.Value
                {
                    displayName = "Abandoned buildings",
                    getter = () => m_AbandonedCount,
                },
                new DebugUI.Button
                {
                    displayName = "Recount now",
                    action = Recount,
                },
                new DebugUI.BoolField
                {
                    displayName = "Verbose logging",
                    getter = () => Mod.Verbose,
                    setter = value => Mod.Verbose = value,
                },
            };
        }
    }
}
