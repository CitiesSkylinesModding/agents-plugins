using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace SmallStreetStopSigns
{
    public partial class SmallStreetStopSystem : GameSystemBase
    {
        // Streets at or under this speed limit count as small. Road prefabs are authored in km/h,
        // but the baked RoadData holds m/s, so the threshold is converted once here.
        private const float kMaxSpeedLimitKmh = 30f;
        private const float kMaxSpeedLimit = kMaxSpeedLimitKmh / 3.6f;

        private EntityQuery m_NodeQuery;

        // A sweep every few seconds of game time picks up junctions built since the last one.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 256;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Temp keeps the road tool's preview out; Deleted keeps out nodes on their way out.
            m_NodeQuery = GetEntityQuery(
                ComponentType.ReadOnly<Node>(),
                ComponentType.ReadOnly<ConnectedEdge>(),
                ComponentType.Exclude<Roundabout>(),
                ComponentType.Exclude<TrafficLights>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            RequireForUpdate(m_NodeQuery);
        }

        protected override void OnUpdate()
        {
            var nodes = m_NodeQuery.ToEntityArray(Allocator.Temp);

            foreach (var node in nodes)
            {
                if (HasAllWayStop(node) || !IsSmallStreetJunction(node))
                {
                    continue;
                }

                // The stop signs are not written onto lanes: lanes are regenerated from the node's
                // composition on every relane, so the all-way stop goes in as a composition input,
                // the same upgrade flag the road tool writes.
                var upgraded = EntityManager.HasComponent<Upgraded>(node)
                    ? EntityManager.GetComponentData<Upgraded>(node)
                    : default;
                upgraded.m_Flags.m_General |= CompositionFlags.General.AllWayStop;

                if (EntityManager.HasComponent<Upgraded>(node))
                {
                    EntityManager.SetComponentData(node, upgraded);
                }
                else
                {
                    EntityManager.AddComponentData(node, upgraded);
                }

                // The node's composition is re-selected while the game walks its edges, and each
                // far node's lanes are generated from those edges' compositions, so the marker goes
                // on the node, every connected edge and every far node.
                var edges = EntityManager.GetBuffer<ConnectedEdge>(node, isReadOnly: true)
                    .ToNativeArray(Allocator.Temp);

                EntityManager.AddComponent<Updated>(node);

                foreach (var connected in edges)
                {
                    var edge = EntityManager.GetComponentData<Edge>(connected.m_Edge);
                    var farNode = edge.m_Start == node ? edge.m_End : edge.m_Start;

                    EntityManager.AddComponent<Updated>(connected.m_Edge);
                    EntityManager.AddComponent<Updated>(farNode);
                }

                edges.Dispose();
                Mod.Log.Info($"Added an all-way stop at {node}.");
            }

            nodes.Dispose();
        }

        private bool HasAllWayStop(Entity node)
        {
            return EntityManager.HasComponent<Upgraded>(node)
                && (EntityManager.GetComponentData<Upgraded>(node).m_Flags.m_General
                    & CompositionFlags.General.AllWayStop) != 0;
        }

        // Three or more roads, every one of them a small street.
        private bool IsSmallStreetJunction(Entity node)
        {
            var edges = EntityManager.GetBuffer<ConnectedEdge>(node, isReadOnly: true);

            if (edges.Length < 3)
            {
                return false;
            }

            foreach (var edge in edges)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(edge.m_Edge).m_Prefab;

                // A track or a footpath has no road data; a junction with one is not all streets.
                if (!EntityManager.HasComponent<RoadData>(prefab))
                {
                    return false;
                }

                if (EntityManager.GetComponentData<RoadData>(prefab).m_SpeedLimit > kMaxSpeedLimit)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
