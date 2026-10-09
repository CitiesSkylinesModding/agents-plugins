# A ledger of repeat building fires

## Kind

bug report

## Prompt

I'm working on a Cities: Skylines II mod that keeps a ledger of building fires: each time a building catches fire, the mod counts one more fire against that building, so the ones that keep burning stand out, and it writes a line to the game's log saying how many buildings have burned more than once and how many fires the worst one has had. The code is in this workspace.

Two problems. With the mod enabled the game has a small but regular stutter while a city is running, which goes away when I disable the mod. And when I switch between saves in one session without restarting the game, memory use climbs with every save I load and never comes back down.

## Checklist

### The ledger is allocated once and emptied at each city load

- Falls in: the `Allocator.Persistent` container is allocated in a load hook (`OnGameLoadingComplete`, `OnGamePreload` or `OnGameLoaded`) and only `OnDestroy` disposes it, so every city load allocates a new one over the previous.
- Avoids: the container is allocated once in `OnCreate` and emptied with `Clear()` from a load hook (or, at the least, the previous instance is disposed before a new one is allocated), with its `Dispose()` in `OnDestroy`.
- Reference: cs2-modding/references/technique/performance-and-memory/performance-and-memory.md
- Source: src/Game/Game.SceneFlow/GameManager.cs:1033

### The burning buildings are gathered asynchronously

- Falls in: `OnUpdate` calls `ToEntityArray` or `ToComponentDataArray` on the query every update and hands the array to the job.
- Avoids: `ToEntityListAsync` or `ToComponentDataListAsync`, with its `out JobHandle` combined into the job's dependency and the list allocated from `World.UpdateAllocator.ToAllocator` or `Allocator.TempJob`; or the job walks the query itself as an `IJobChunk` or `IJobEntity`, with no gather at all.
- Reference: cs2-modding/references/technique/performance-and-memory/performance-and-memory.md
- Source: src/Unity.Entities/Unity.Entities/EntityQueryImpl.cs:468

### The job's handle is published rather than completed every update

- Falls in: `OnUpdate` calls `Complete()` on the handle it has just scheduled, on every update, whether to dispose the gathered array or to read the ledger back.
- Avoids: the scheduled handle is assigned to `base.Dependency`; the gathered container is disposed with `Dispose(jobHandle)` or comes from `World.UpdateAllocator`; and any `Complete()` sits behind the gate that reads the ledger back, never on the unconditional path.
- Reference: cs2-modding/references/technique/performance-and-memory/performance-and-memory.md
- Source: src/Unity.Entities/Unity.Entities/SystemState.cs:416

### The log line is written when the ledger changes, not on every update

- Falls in: a log call (`Mod.Log.Info` or any other level the logger has enabled) runs unconditionally on every `OnUpdate`.
- Avoids: the log call is gated, written only when the reported numbers differ from the last line written, or at most once per in-game day.
- Reference: cs2-modding/references/technique/performance-and-memory/performance-and-memory.md
- Source: src/Colossal.Logging/Colossal.Logging/UnityLogger.cs:342

## Roots

- decompile
