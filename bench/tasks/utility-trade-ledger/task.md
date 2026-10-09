# A utility trade ledger per outside connection

## Kind

bug report

## Prompt

I'm working on a Cities: Skylines II mod that writes a utility ledger to the log once in a while: for each outside connection, how much electricity, fresh water and sewage goes through it and what that earns or costs per day at the current rate, plus the city's own electricity generation and consumption on a line of its own so I can check that the numbers add up. The code is in this workspace.

Two things are off and I can't see why. The sewage column reads zero for every connection, on a map where the budget panel has a sewage line every day. And the electricity side never balances: generation plus imports comes out well above consumption plus exports, every time, on a city with no batteries. The water export income also looks higher than what the budget panel credits me, though I'm less sure of that one.

## Checklist

### Generated electricity is summed from last production, not capacity

- Falls in: the city's generated figure is `ElectricityStatisticsSystem.production` (or the `IElectricityStatisticsSystem` property), or a sum of `ElectricityProducer.m_Capacity` over the producers.
- Avoids: the generated figure is a sum of `ElectricityProducer.m_LastProduction` over the producers, or of `m_Flow` on each `ElectricityBuildingConnection.m_ProducerEdge`.
- Reference: cs2-modding/references/mechanics/utilities-and-flow-networks/utilities-and-flow-networks.md
- Source: src/Game/Game.Simulation/ElectricityStatisticsSystem.cs:34

### Sewage export is read off the source-side trade edge

- Falls in: a connection's sewage figure sums `WaterPipeEdge.m_SewageFlow` only on edges whose `m_End` is `WaterPipeFlowSystem.sinkNode`, the edges fresh export is read from, and reads no sewage from the edge starting at the source.
- Avoids: the sewage figure is `m_SewageFlow` on the edge whose `m_Start` is `WaterPipeFlowSystem.sourceNode`, the same edge fresh import is read from.
- Reference: cs2-modding/references/mechanics/utilities-and-flow-networks/consumption-and-dispatch.md
- Source: src/Game/Game.Simulation/WaterTradeSystem.cs:67

### Polluted export water earns nothing

- Falls in: fresh export income is the exported `m_FreshFlow` times `OutsideTradeParameterData.m_WaterExportPrice`, with no term reading the edge's `m_FreshPollution`.
- Avoids: the share `min(round(m_FreshPollution / m_WaterExportPollutionTolerance * m_FreshFlow), m_FreshFlow)` is subtracted from the exported flow before it is priced.
- Reference: cs2-modding/references/mechanics/utilities-and-flow-networks/consumption-and-dispatch.md
- Source: src/Game/Game.Simulation/WaterTradeSystem.cs:103

### Sewage export is billed as a cost

- Falls in: the sewage figure times `OutsideTradeParameterData.m_SewageExportPrice` is added to the connection's income, or logged as what the connection earns.
- Avoids: that product is recorded as an expense, beside the fresh-water import cost.
- Reference: cs2-modding/references/mechanics/utilities-and-flow-networks/consumption-and-dispatch.md
- Source: src/Game/Game.Simulation/ServiceFeeSystem.cs:202

## Roots

- decompile
