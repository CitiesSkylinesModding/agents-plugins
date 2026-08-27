# The solve cycle

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Both flow systems register plainly into `SystemUpdatePhase.GameSimulation` with no `GetUpdateInterval` override; they run every frame and gate themselves on `SimulationSystem.frameIndex % 128`.
The cycle constants are `public const` on the two systems (`src/Game/Game.Simulation/ElectricityFlowSystem.cs`, `WaterPipeFlowSystem.cs`):

| | electricity | water |
| --- | --- | --- |
| `kUpdateInterval` | 128 | 128 |
| `kUpdateOffset` | — | 64 |
| `kUpdatesPerDay` | 2048 | 2048 |
| `kUpdatesPerHour` | 85 | — |
| `kAdjustFrame` | 0 | 64 |
| `kPrepareFrame` | 1 | 65 |
| `kFlowFrames` | 124 | 124 |
| `kFlowCompletionFrame` | 125 | 61 |
| `kApplyFrame` | 126 | 62 |
| `kStatusFrame` | 127 | 63 |
| `kMaxEdgeCapacity` | 1073741823 | 1073741823 |

The water solver runs exactly half a cycle out of phase, which keeps each solver's four bookkeeping frames clear of the other's; the budgeted solve frames themselves overlap for most of the cycle, each job spending its own budget.
The order inside one cycle is the mechanism: adjust writes capacities, prepare snapshots the graph into solver arrays, 124 frames solve, apply writes flows back onto the edge components, then dispatch turns flows into per-building fulfilment, trade bills the outside flows, and status and statistics read the result.
Everything else in the topic hangs off the same interval with an offset landing it in the right slot:

| System | interval | offset | source (`src/Game/Game.Simulation/`) |
| --- | --- | --- | --- |
| `AdjustElectricityConsumptionSystem`, `PowerPlantAISystem`, `BatteryAISystem` | 128 | 0 | each system's `GetUpdateInterval`/`GetUpdateOffset` |
| `DispatchElectricitySystem`, `ElectricityTradeSystem` | 128 | 126 | same |
| `ElectricityStatusSystem`, `ElectricityStatisticsSystem` | 128 | 127 | same |
| `AdjustWaterConsumptionSystem`, `WaterPumpingStationAISystem`, `SewageOutletAISystem`, `GroundWaterSystem`, `GroundWaterPollutionSystem` | 128 | 64 | same |
| `DispatchWaterSystem`, `WaterTradeSystem` | 128 | 62 | same |
| `WaterStatisticsSystem` | 128 | 63 | same |
| `WaterPipePollutionSystem` | 64 | — | same |

Consumption is not recomputed for every building every cycle: the adjust systems filter on the shared `UpdateFrame` component against `SimulationUtils.GetUpdateFrame(frameIndex, 128, 16)` — sixteen buckets, one per cycle, so each building is recomputed `kFullUpdatesPerDay = 128` times per in-game day of `TimeSystem.kTicksPerDay = 262144` frames.
Both adjust systems assert `GetUpdateInterval >= 128` at creation — a first-party statement that lowering the interval is unsupported (`src/Game/Game.Simulation/AdjustElectricityConsumptionSystem.cs`, `AdjustWaterConsumptionSystem.cs`).

## The phases

Sources: `src/Game/Game.Simulation/ElectricityFlowJob.cs`, `src/Game/Game.Simulation/WaterPipeFlowJob.cs`, `src/Game/Game.Simulation.Flow/MaxFlowSolver.cs`, `src/Game/Game.Simulation.Flow/FluidFlowSolver.cs`.

```
ElectricityFlowJob.Phase: Initial -> Producer -> PostProducer -> Battery -> PostBattery -> Trade -> PostTrade -> Complete
  budget per frame = max(100, m_LastTotalSteps / 124); the final frame ignores it and runs to completion
  Producer:     a full max-flow with every battery and trade edge FlowDirection.None
  PostProducer: label the shortage sub-graphs backwards from the sink; enable discharge edges whose node sits inside a shortage sub-graph, charge edges whose node does not -- a battery decides per node from the previous solve's min cut, not from a global figure
  Battery:      solve again with those edges on
  PostBattery:  disable every battery edge, re-label, then enable a TradeNode's import edge (source -> node) at a shortage node and its export edge (node -> sink) elsewhere
  Trade:        solve again
  PostTrade:    import on, export off; label connectivity and bottlenecks for the apply pass
WaterPipeFlowJob.Phase: Initial -> Producer -> PostProducer -> Trade -> PostTrade -> FluidFlow -> Complete
  scheduled twice per cycle over one topology: the fresh instance with (import, export) = (1073741823, 1073741823), the sewage instance with (1073741823, 0) -- unlimited handling-capacity import, no export, which is the sewage inversion
  no battery phases; PostProducer labels shortages and enables trade edges directly
  FluidFlow: MaxFlowSolver returns an arbitrary member of the equal-value maximum flows, so FluidFlowSolver re-runs the assignment along short paths (two NativeMinHeap passes, label then push) and the apply pass reads its m_FinalFlow; skipped entirely when WaterPipeFlowSystem.fluidFlowEnabled is false
```

`fluidFlowEnabled` is a public settable property defaulting to true, and the developer menu ships it as its "Water Pipe Fluid Flow" toggle (`src/Game/Game.Simulation/WaterPipeFlowSystem.cs`, `src/Game/Game.Debug/DebugSystem.cs`).
The electricity apply pass maps the solver's labels onto `ElectricityFlowEdgeFlags` — `Bottleneck`, `BeyondBottleneck`, `None`, anything else `Disconnected`; the water pass maps each layer's labels onto its own pair, `WaterShortage`/`WaterDisconnected` for fresh and `SewageBackup`/`SewageDisconnected` for sewage.
Errors ride a poison flag: `m_Error` is set at the top of `Execute` and cleared at the bottom, so a throw leaves it set, later frames short-circuit, and the final frame logs `"Electricity solver error in phase: {phase}"` or `"Water pipe solver error in phase: {phase}"` and resets — the one diagnostic the solve jobs print; `WaterPipeFlowSystem.PostDeserialize` adds its own legacy-pipe and null-node warnings, and the surrounding systems log their own missing-edge errors and load-time and runtime warnings.
`m_LastTotalSteps` — the step count the last complete solve took, feeding the budget; water keeps one per layer — is all either system serializes besides its source and sink entities, so a save carries the solver's own workload estimate; a fresh electricity state starts from `State(20000)` and each water layer from `State(200000)`.

**The two jobs' label constants collide in value and differ in meaning.**
Electricity's `kConnectedNodeLabel = -1` and `kShortageNodeLabel = -2` are water's `kShortageNodeLabel = -1` and `kConnectedNodeLabel = -2`, so reading one job with the other's constants in mind inverts every diagnosis.
Source: `src/Game/Game.Simulation/ElectricityFlowJob.cs`, `src/Game/Game.Simulation/WaterPipeFlowJob.cs`.

**`Game.Simulation.Flow.Node` is a union, and half its fields are stale at any moment.**
`[StructLayout(LayoutKind.Explicit)]` overlays `m_CutElementId`/`m_Retreat` with `m_Distance`/`m_Predecessor`/`m_Enqueued` because the two solvers reuse one array, so `m_Distance` read after a max-flow pass is a cut-element id.
Source: `src/Game/Game.Simulation.Flow/Node.cs`.

## `ready`, and what a mod reads too early

`ready` turns true only in the apply phase and false only in `Reset()`, which `PostDeserialize` calls unconditionally; both dispatch systems do nothing until their solver's is true (`src/Game/Game.Simulation/WaterPipeFlowSystem.cs`, `DispatchWaterSystem.cs`, `DispatchElectricitySystem.cs`).

**After a load, and for as long as the game stays paused, every fulfilment field is the previous session's answer.**
`ElectricityConsumer.m_FulfilledConsumption` and `WaterConsumer.m_FulfilledFresh`/`m_FulfilledSewage` hold what the save carried until a full cycle reaches its apply and dispatch frames, so a mod reading fulfilment in `OnGameLoadingComplete` reads stale data with no marker on it.
Source: `src/Game/Game.Simulation/ElectricityFlowSystem.cs`, `src/Game/Game.Simulation/DispatchWaterSystem.cs`.

(VOLATILE: every system, job, phase enum, constant, flag, property, method, quoted log message, dev-menu label and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Simulation.Flow`, `Game.Buildings`, `Game.Debug` and the root `Game` namespace, plus `NativeMinHeap` in the `Colossal.Collections` assembly, at the files the sections cite.)
