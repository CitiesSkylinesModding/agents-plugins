using Game;
using Game.Buildings;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace UtilityLedger
{
    public partial class UtilityLedgerSystem : GameSystemBase
    {
        private ElectricityFlowSystem m_ElectricityFlowSystem;
        private WaterPipeFlowSystem m_WaterPipeFlowSystem;
        private ElectricityStatisticsSystem m_ElectricityStatisticsSystem;
        private EntityQuery m_ElectricityTradeQuery;
        private EntityQuery m_WaterTradeQuery;
        private EntityQuery m_ProducerQuery;
        private EntityQuery m_TradeParameterQuery;

        // Both utility solvers run a 128-frame cycle; its last frame is where the game's own
        // statistics read the result, so the ledger reads there too.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 128;

        public override int GetUpdateOffset(SystemUpdatePhase phase) => 127;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ElectricityFlowSystem = World.GetOrCreateSystemManaged<ElectricityFlowSystem>();
            m_WaterPipeFlowSystem = World.GetOrCreateSystemManaged<WaterPipeFlowSystem>();
            m_ElectricityStatisticsSystem =
                World.GetOrCreateSystemManaged<ElectricityStatisticsSystem>();
            m_ElectricityTradeQuery = GetEntityQuery(
                ComponentType.ReadOnly<TradeNode>(),
                ComponentType.ReadOnly<ElectricityFlowNode>(),
                ComponentType.ReadOnly<ConnectedFlowEdge>());
            m_WaterTradeQuery = GetEntityQuery(
                ComponentType.ReadOnly<TradeNode>(),
                ComponentType.ReadOnly<WaterPipeNode>(),
                ComponentType.ReadOnly<ConnectedFlowEdge>());
            m_ProducerQuery = GetEntityQuery(ComponentType.ReadOnly<ElectricityProducer>());
            m_TradeParameterQuery = GetEntityQuery(
                ComponentType.ReadOnly<OutsideTradeParameterData>());

            RequireForUpdate(m_TradeParameterQuery);
        }

        protected override void OnUpdate()
        {
            var prices = m_TradeParameterQuery.GetSingleton<OutsideTradeParameterData>();

            LogElectricity(prices);
            LogWater(prices);
        }

        private void LogElectricity(OutsideTradeParameterData prices)
        {
            var source = m_ElectricityFlowSystem.sourceNode;
            var sink = m_ElectricityFlowSystem.sinkNode;
            var nodes = m_ElectricityTradeQuery.ToEntityArray(Allocator.Temp);

            foreach (var node in nodes)
            {
                var import = 0;
                var export = 0;

                foreach (var connected in EntityManager.GetBuffer<ConnectedFlowEdge>(node, true))
                {
                    var edge =
                        EntityManager.GetComponentData<ElectricityFlowEdge>(connected.m_Edge);

                    if (edge.m_End == sink)
                    {
                        export += edge.m_Flow;
                    }
                    else if (edge.m_Start == source)
                    {
                        import += edge.m_Flow;
                    }
                }

                // Flows are daily rates, so a day at this rate is the flow times the price.
                var earned = export * prices.m_ElectricityExportPrice;
                var paid = import * prices.m_ElectricityImportPrice;

                Mod.Log.Info(
                    $"Electricity #{node.Index}: in {import}, out {export}, "
                    + $"earns {earned:F0}/day, costs {paid:F0}/day");
            }

            nodes.Dispose();

            // The statistics system's "production" sums each plant's capacity, the most it could
            // put out this cycle; what it did put out is the flow the plant read back off its
            // producer edge, kept as its last production.
            var producers =
                m_ProducerQuery.ToComponentDataArray<ElectricityProducer>(Allocator.Temp);
            var generated = 0;

            foreach (var producer in producers)
            {
                generated += producer.m_LastProduction;
            }

            producers.Dispose();

            Mod.Log.Info(
                $"Electricity city: generated {generated}, "
                + $"consumed {m_ElectricityStatisticsSystem.fulfilledConsumption}");
        }

        private void LogWater(OutsideTradeParameterData prices)
        {
            var source = m_WaterPipeFlowSystem.sourceNode;
            var sink = m_WaterPipeFlowSystem.sinkNode;
            var nodes = m_WaterTradeQuery.ToEntityArray(Allocator.Temp);

            foreach (var node in nodes)
            {
                var freshIn = 0;
                var freshOut = 0;
                var pollutedOut = 0;
                var sewageOut = 0;

                foreach (var connected in EntityManager.GetBuffer<ConnectedFlowEdge>(node, true))
                {
                    var edge = EntityManager.GetComponentData<WaterPipeEdge>(connected.m_Edge);

                    if (edge.m_End == sink)
                    {
                        freshOut += edge.m_FreshFlow;

                        // The share of the exported water too dirty to be paid for; the rest of
                        // the fresh flow is sold at full price.
                        var dirtyShare =
                            edge.m_FreshPollution / prices.m_WaterExportPollutionTolerance;
                        pollutedOut += math.min(
                            (int)math.round(dirtyShare * edge.m_FreshFlow),
                            edge.m_FreshFlow);
                    }
                    else if (edge.m_Start == source)
                    {
                        freshIn += edge.m_FreshFlow;

                        // The sewage layer carries capacity to accept sewage, so what the city
                        // sends out arrives on the same source-side edge its fresh imports do;
                        // the sink-side edge never carries sewage flow.
                        sewageOut += edge.m_SewageFlow;
                    }
                }

                var earned = (freshOut - pollutedOut) * prices.m_WaterExportPrice;

                // Sending sewage out is paid for, like importing water.
                var paid = freshIn * prices.m_WaterImportPrice
                    + sewageOut * prices.m_SewageExportPrice;

                Mod.Log.Info(
                    $"Water #{node.Index}: fresh in {freshIn}, fresh out {freshOut}, "
                    + $"sewage out {sewageOut}, earns {earned:F0}/day, costs {paid:F0}/day");
            }

            nodes.Dispose();
        }
    }
}
