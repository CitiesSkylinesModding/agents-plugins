# Consumption and dispatch

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

The adjust systems turn prefab consumption into `m_WantedConsumption` and consumer-edge capacity once per cycle for that cycle's `UpdateFrame` bucket — each building every sixteenth cycle; after the solve, the dispatch systems turn edge flow into per-building fulfilment, warnings and efficiency, and the trade systems bill the outside flows.

## Wanted consumption

Sources: `src/Game/Game.Simulation/AdjustElectricityConsumptionSystem.cs`, `src/Game/Game.Simulation/AdjustWaterConsumptionSystem.cs`, `src/Game/Game.Simulation/FlowUtils.cs`.

```
AdjustElectricityConsumptionSystem, per building in this cycle's UpdateFrame bucket:
  c  = ConsumptionData.m_ElectricityConsumption      -- combined over InstalledUpgrade
  c *= ElectricityParameterData.m_TemperatureConsumptionMultiplier
         .Evaluate(ClimateSystem.temperature)        -- 1 when the singleton is absent
  c *= ServiceFeeParameterData.m_ElectricityFeeConsumptionMultiplier
         .Evaluate(fee / m_ElectricityFee.m_Default)
       -- a chunk carrying CityServiceUpkeep skips the fee: multiplier 1, fee efficiency 1
  c  = AreaUtils.ApplyModifier(c, DistrictModifierType.EnergyConsumptionAwareness)
       -- when the building sits in a district with modifiers
  unless the chunk has Park or StorageProperty, and a Renter buffer exists:
    c *= FlowUtils.GetRenterConsumptionMultiplier(...)
  either way: if c was > 0 and is now < 1, c = 1     -- a small positive demand floors
    to 1 rather than rounding away
  wanted = c > 0 ? MathUtils.RoundToIntRandom(random, c) : 0
    -- the guard is reachable: a district modifier can push c negative, past the floor
  wanted /= 10 when BuildingOption.Inactive
  on change: write ElectricityConsumer.m_WantedConsumption, push onto the consumer edge's
    m_Capacity, and enqueue the road edge for the aggregate re-sum
  EfficiencyFactor.ElectricityFee = BuildingEfficiencyParameterData.m_ElectricityFeeFactor
    .Evaluate(relativeFee)

FlowUtils.GetRenterConsumptionMultiplier:
  n   = citizens across the Renter buffer -- household members via HouseholdCitizen,
        or workers via Employee
  edu = their summed Citizen.GetEducationLevel()
  level = SpawnableBuildingData.m_Level, or 5 where the prefab has none
  n == 0: return 0 -- which the caller's floor above then turns into wanted = 1:
    the "was > 0" test runs before this multiplier, so even an empty building wants 1
  return 5 * n / (level + 0.5 * (edu / n))
```

Sampled live at 1.6.0f1, the temperature curve is a U — flat at 1 in a comfort band, rising to a cap on both sides, clamped beyond; evaluating the singleton's curve is the re-check.

`AdjustWaterConsumptionSystem` is the same shape with four differences: no temperature term, no district modifier, `Inactive` applying first as `m_WaterConsumption * 0.1f` where electricity divides the rounded integer by 10 after, and the fee terms swapped to the water fee's — `m_WaterFeeConsumptionMultiplier`, `m_WaterFeeFactor`, `EfficiencyFactor.WaterFee`.
The load-bearing fifth: the single `wanted` is written to both `m_FreshCapacity` and `m_SewageCapacity` on the consumer edge — a building's sewage demand is identical to its fresh demand, always.

## Dispatch

Sources: `src/Game/Game.Simulation/DispatchElectricitySystem.cs`, `src/Game/Game.Simulation/DispatchWaterSystem.cs`.

```
DispatchElectricitySystem, per consumer, on the cycle's dispatch frame:
  own ElectricityBuildingConnection:
    flow = consumerEdge.m_Flow; beyondBottleneck and disconnected from its flags
  road-edge aggregate (no building connection):
    edge = roadEdgeNode -> sink
    edge saturated (m_Capacity == m_Flow): flow = m_WantedConsumption
    else: flow = floor(wanted * m_Flow / m_Capacity)
      -- pro-rata and floored, so every building on an undersupplied road is equally short
  m_FulfilledConsumption = min(flow, m_WantedConsumption)
  cooldown (skipped entirely while the electricity asset-menu prefab is Locked, the
            efficiency factors below still computing from the frozen counters;
            DispatchWaterSystem gates on its own asset-menu prefab the same way,
            its dirty-water icon included):
    short: m_CooldownCounter = min(counter + 1, 10000)
      at kAlertCooldown (public static readonly short = 2) raise the warning flag --
      the bottleneck icon when beyondBottleneck, the plain no-electricity icon otherwise
    fulfilled: counter = 0
  Connected = wanting ? fulfilled >= wanted : !disconnected
    -- the second arm is why an empty lot still shows a power symbol
  EfficiencyFactor.ElectricitySupply =
    1 - m_ElectricityPenalty * saturate(counter / m_ElectricityPenaltyDelay)

DispatchWaterSystem: fresh and sewage each ride the same two arms, with these differences --
  no clamp: an own connection's m_FulfilledFresh/m_FulfilledSewage take the edge's raw
    flow where electricity takes min(flow, wanted) -- a shape difference: the consumer
    edge's capacity is the same wanted figure, so the raw flow stays within it anyway;
    the road aggregate is pro-rata per layer
  the cooldowns are bytes capped at byte.MaxValue, not 10000; kAlertCooldown is its own
    identical declaration of 2
  WaterConsumerFlags is None/WaterConnected/SewageConnected, rebuilt every pass --
    connectivity only, no shortage bit for a mod to check
  a non-wanting building's connectivity comes from the edge's shortage/disconnection
    flag pair instead of fulfilment, decided after the cooldown pass
  m_Pollution = freshFlow > 0 ? edge.m_FreshPollution : 0
  the dirty-water icon toggles as m_Pollution crosses WaterPipeParameterData.m_MaxToleratedPollution
  EfficiencyFactor.WaterSupply    = 1 - m_WaterPenalty * saturate(freshCooldown / m_WaterPenaltyDelay)
  EfficiencyFactor.DirtyWater     = 1 - m_WaterPollutionPenalty * round(pollution * 100) / 100
  EfficiencyFactor.SewageHandling = 1 - m_SewagePenalty * saturate(sewageCooldown / m_SewagePenaltyDelay)
```

Every efficiency write goes through `BuildingUtils.SetEfficiencyFactor`, which removes the buffer entry when the value is within 0.001 of 1 — a mod scanning the `Efficiency` buffer reads absence as 1 (`src/Game/Game.Buildings/BuildingUtils.cs`).

`DispatchWaterSystem.freshConsumptionDisabled` and `sewageConsumptionDisabled` short-circuit every building's fulfilment to its demand and mask the shortage flags out of the connectivity test — but they are not a mod's private switch: `WaterPipeFlowSystem.PostDeserialize` resets both on every load (and sets them loading a pre-`waterPipeFlowSim` save), and the developer menu ships them as its "Disable Water consumption" and "Disable Sewage generation" toggles, so a mod's write holds only until the next load (`src/Game/Game.Simulation/DispatchWaterSystem.cs`, `src/Game/Game.Simulation/WaterPipeFlowSystem.cs`, `src/Game/Game.Debug/DebugSystem.cs`).

**`FlowUtils.ConsumeFromTotal` is not the fulfilment rule.**
Its three callers are the two raycast tooltips and `NetColorSystem` — presentation splitting a road edge's aggregate for display — and no simulation system calls it, so a reader who finds it first has the wrong answer.
Source: `src/Game/Game.Simulation/FlowUtils.cs`, `src/Game/Game.UI.Tooltip/RaycastElectricityTooltipSystem.cs`, `src/Game/Game.UI.Tooltip/RaycastWaterTooltipSystem.cs`, `src/Game/Game.Rendering/NetColorSystem.cs`.

## Trade billing

Sources: `src/Game/Game.Simulation/ElectricityTradeSystem.cs`, `src/Game/Game.Simulation/WaterTradeSystem.cs`.

```
ElectricityTradeSystem: over every TradeNode's ConnectedFlowEdge buffer,
  export += flow on edges ending at the sink; import += flow on edges starting at the source
  exportRevenue = export / 2048 * OutsideTradeParameterData.m_ElectricityExportPrice
  importCost    = import / 2048 * m_ElectricityImportPrice
  both queued as ServiceFeeSystem.FeeEvent { m_Outside = true }, the import amount negated
  -- 2048 matches kUpdatesPerDay, converting per-tick flow to a per-day amount;
     both trade systems write the bare literal, so grep for 2048f, not the symbol

WaterTradeSystem: four sums instead of two --
  sink-side edges:   freshExport += m_FreshFlow
                     pollutedExport += min(round(m_FreshPollution
                       / m_WaterExportPollutionTolerance * m_FreshFlow), m_FreshFlow)
                     (asserts m_SewageFlow == 0 there -- the sewage inversion: sewage rides
                       source to sink as handling capacity, so its export sums on importing edges)
  source-side edges: freshImport += m_FreshFlow; sewageExport += m_SewageFlow
  freshExport = max(min(availableWater, freshExport), 0)  -- capped at the city's spare water
  exportRevenue = (freshExport - pollutedExport) / 2048 * m_WaterExportPrice
    -- dirty water sold abroad earns nothing
  freshImport and sewageExport are both billed as costs (negated amounts)
```

(VOLATILE: every system, component, field, curve, property, method, constant, dev-menu label and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Prefabs`, `Game.Buildings`, `Game.Areas`, `Game.City`, `Game.Citizens`, `Game.Companies`, `Game.Debug`, `Game.UI.Tooltip` and `Game.Rendering`, plus `MathUtils` in the `Colossal.Mathematics` assembly, at the files the sections cite; plus the live-sampled temperature-curve shape, re-checked by evaluating the singleton's curve.)
