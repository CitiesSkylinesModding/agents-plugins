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
        // Streets at or under this speed limit count as small.
        private const float kMaxSpeedLimit = 30f;

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
                ComponentType.ReadOnly<SubLane>(),
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
                if (!IsSmallStreetJunction(node))
                {
                    continue;
                }

                // Stop signs on every approach: flag each of the junction's car lanes.
                var lanes = EntityManager.GetBuffer<SubLane>(node, isReadOnly: true)
                    .ToNativeArray(Allocator.Temp);
                var changed = false;

                foreach (var lane in lanes)
                {
                    if (!EntityManager.HasComponent<CarLane>(lane.m_SubLane))
                    {
                        continue;
                    }

                    var carLane = EntityManager.GetComponentData<CarLane>(lane.m_SubLane);

                    if ((carLane.m_Flags & CarLaneFlags.Stop) != 0)
                    {
                        continue;
                    }

                    carLane.m_Flags |= CarLaneFlags.Stop;
                    EntityManager.SetComponentData(lane.m_SubLane, carLane);
                    changed = true;
                }

                if (changed)
                {
                    // Tell the game the junction changed.
                    EntityManager.AddComponent<Updated>(node);
                    Mod.Log.Info($"Added an all-way stop at {node}.");
                }

                lanes.Dispose();
            }

            nodes.Dispose();
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
