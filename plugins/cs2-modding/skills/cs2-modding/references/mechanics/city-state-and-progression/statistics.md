# Statistics

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

A statistic is an entity: a `StatisticsPrefab` declares `StatisticsData` as its prefab component and `CityStatistic` as its archetype (`src/Game/Game.Prefabs/StatisticsPrefab.cs`), and `CityStatisticsSystem.InitializeLookup` calls its `CreateInstance` once per parameter value.
`CityStatisticsSystem` (`src/Game/Game.Simulation/CityStatisticsSystem.cs`, `GameSimulation`) samples every `8192` frames, a literal in `GetUpdateInterval`, so one sample is a thirty-second of a day.
The address is `StatisticsKey(StatisticType, int parameter)`, and `GetLookup()` returns the `NativeParallelHashMap<StatisticsKey, Entity>` that is the only way from a key to its buffer.
Each `ParametricStatistic` subclass names in `GetParameterName` the enum its parameter indexes — `ResourceStatistic` → `Resource`, through `EconomyUtils.GetResourceIndex` rather than the flag value, is the shape, and `LevelStatistic` indexes a raw int; the subclasses are the classes deriving from it under `src/Game/Game.Prefabs/`.
`StatisticType`, `StatisticCollectionType` and `StatisticUnitType` are the declared sets under `src/Game/Game.City/`.

## Collection modes

Source: `src/Game/Game.Simulation/CityStatisticsSystem.cs` (`OnUpdate`, `CityStatisticsJob`, `ProcessStatisticsJob`, `ResetEntityJob`).

```
every 8192 frames, three jobs chained in this order:
CityStatisticsJob: enqueue the system's own events (Money from CitySystem.moneyAmount, lodging from Tourism, the rest from the household counts)
ProcessStatisticsJob, draining the queue:
    skip when m_Statistic == StatisticType.Count, or the key is not in the lookup
    if the buffer is empty: append a zero element
    if buffer.Length == 1 and type == Money: buffer[^1].m_TotalValue = m_Change
    buffer[^1].m_Value += m_Change
ResetEntityJob, for every key in the lookup:
    if the buffer is empty: append { m_TotalValue = 0, m_Value = (type == Money ? money : 0) }
    last = buffer[^1]
    switch StatisticsData.m_CollectionType:
        Cumulative: append { m_TotalValue = last.m_TotalValue + last.m_Value, m_Value = 0 }
        Point:      append { m_TotalValue = last.m_Value, m_Value = 0 }
        Daily:      old = buffer.Length >= 32 ? buffer[^32].m_Value : 0
                    append { m_TotalValue = last.m_TotalValue + last.m_Value - old, m_Value = 0 }
```

The drain runs before the roll-over, so an event enqueued during an update lands on the current sample and is folded into `m_TotalValue` by that same update.
Money is the one series that starts from an absolute rather than from zero.

## Writing

The handshake is `GetStatisticsEventQueue(out JobHandle deps)` before scheduling and `AddWriter(JobHandle)` after, enqueueing `StatisticsEvent { m_Statistic, m_Change, m_Parameter }`; `SafeStatisticQueue` is the wrapper the game passes into jobs, and it drops every enqueue while `m_StatisticsEnabled` is false.
The system's own `CityStatisticsJob` pushes the statistics derived from the city entity and the household counts itself; every other producer is a caller of `GetStatisticsEventQueue` or of `GetSafeStatisticsQueue`, the accessor returning the wrapper.

**An event for a `StatisticType` no `StatisticsPrefab` declares is dropped without a log.**
The key is not in the lookup, so `ProcessStatisticsJob` skips it; a mod adding a statistic ships the prefab, not only the enum value.
Source: `src/Game/Game.Simulation/CityStatisticsSystem.cs`.

## Reading

`GetStatisticValue` / `GetStatisticValueLong` have static forms taking `(lookup, BufferLookup<CityStatistic>, type, parameter)` that are Burst-callable, `(BufferLookup<CityStatistic>, type, parameter)` instance forms that only delegate to them, and `(type, parameter)` instance forms that complete the writer chain first; all return `Math.Round(buffer[^1].m_TotalValue, MidpointRounding.AwayFromZero)` saturated to the return type's range, and 0 for an unknown key or an empty buffer.
`ICityStatisticsSystem` (`src/Game/Game.Simulation/ICityStatisticsSystem.cs`) is the full surface — `GetStatisticDataArray` for a whole series, `sampleCount`, `GetSampleFrameIndex(index)` as `m_LastSampleFrameIndex - (sampleCount - index - 1) * 8192`, and the `eventStatisticsUpdated` action.

**The sample cadence is four literals, and `kUpdatesPerDay` is none of them.**
`8192` in `GetUpdateInterval` and again in `GetSampleFrameIndex`, `32` in `ResetEntityJob`'s `Daily` window and again in the `statistics` binding's `updatesPerDay`; a mod changing the cadence patches all four or the chart's frame-to-date conversion and the `Daily` series drift.
Source: `src/Game/Game.UI.InGame/StatisticsUISystem.cs`, `src/Game/Game.Simulation/CityStatisticsSystem.cs`.

## The trigger

Sources: `src/Game/Game.Simulation/StatisticTriggerSystem.cs`, `src/Game/Game.Prefabs/StatisticTriggerPrefab.cs`, `src/Game/Game.Prefabs/StatisticTriggerData.cs`.

```
authoring (LateInitialize): if the statistic or its normaliser is Daily, m_MinSamples = max(m_MinSamples, 32 + max(0, m_TimeFrame - 1))
per trigger, n = max(1, m_TimeFrame), a = the statistic's int series, b = the normaliser's:
    skip when either series is shorter than n or m_MinSamples, or the statistic entity (or the normaliser) has Locked enabled
    normalised, by m_Type:
        TotalValue:     Σ_{j=1..n} (float)a[^j] / (float)b[^j]               // only when every b[^j] != 0
        AverageValue:   that sum / n
        AbsoluteChange: (float)a[^1] / (float)b[^1] - (float)a[^n] / (float)b[^n]   // only when b[^n] != 0 and b[^1] != 0
        RelativeChange: r0 = a[^n] / b[^n]; r1 = a[^1] / b[^1]              // same two guards; int / int, no cast: truncated
                        value = (r1 - r0) / r0, only when r0 != 0
    unnormalised: the same four over a alone
enqueue TriggerAction { TriggerType.StatisticsValue, prefab, value }
```

(VOLATILE: every system, component, field, property, enum, method, constant, binding name and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Prefabs`, `Game.City`, `Game.Triggers`, `Game.Economy` and `Game.UI.InGame`, at the files the sections cite.)
