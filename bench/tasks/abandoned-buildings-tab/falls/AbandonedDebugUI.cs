using System.Collections.Generic;
using Game.Buildings;
using Game.Debug;
using Unity.Entities;
using UnityEngine.Rendering;

namespace AbandonedWatch
{
    [DebugContainer]
    internal sealed class AbandonedDebugUI
    {
        private readonly EntityQuery m_AbandonedQuery;

        public AbandonedDebugUI()
        {
            m_AbandonedQuery = World.DefaultGameObjectInjectionWorld.EntityManager
                .CreateEntityQuery(ComponentType.ReadOnly<Abandoned>());
        }

        [DebugTab("Abandoned Buildings", 100)]
        private List<DebugUI.Widget> BuildAbandonedDebugUI()
        {
            return new List<DebugUI.Widget>
            {
                new DebugUI.MessageBox
                {
                    displayName = "Buildings currently flagged abandoned. "
                        + "The graph of this number is on the Watchers tab.",
                    style = DebugUI.MessageBox.Style.Info,
                },
                new DebugUI.Value
                {
                    displayName = "Abandoned buildings",
                    // Live, so the page never shows a stale number.
                    getter = () => m_AbandonedQuery.CalculateEntityCount(),
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
