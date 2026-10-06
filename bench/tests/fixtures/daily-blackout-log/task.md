# Log blackouts once per in-game day

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod. I want the game to log, once per in-game day, how many households currently have no electricity, so I can track blackouts over a long session.

## Checklist

### The update interval is a power of two

- Falls in: `GetUpdateInterval` returns a value that is not a power of two, such as `262144 / 24` or a round number like `10000`.
- Avoids: `GetUpdateInterval` returns a power of two, such as `262144 / kUpdatesPerDay` with a power-of-two `kUpdatesPerDay`, or the system declares no interval and gates itself.
- Reference: cs2-modding/references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md
- Source: src/Game/Game/UpdateSystem.cs:288

### The system runs in a phase that honours its interval

- Falls in: a system that relies on its `GetUpdateInterval` override for its cadence is registered with `UpdateAt` in a phase other than `GameSimulation`, `EditorSimulation` or `LoadSimulation`, such as `UIUpdate` or a modification phase.
- Avoids: the system relying on its interval is registered in `GameSimulation`, or a system in another phase gates its own cadence without an interval override.
- Reference: cs2-modding/references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md
- Source: src/Game/Game.Simulation/SimulationSystem.cs:286

### The day change is detected without assuming the system runs on offset zero

- Falls in: the system decides a day has passed by testing `frameIndex % 262144 == 0`, `frameIndex % interval == 0` or an equivalent mask against zero, a frame it may never run on.
- Avoids: the system compares the current day, from `TimeSystem.GetDay` or `frameIndex / 262144`, against the last day it logged.
- Reference: cs2-modding/references/mechanics/simulation-time-and-units/cadence.md
- Source: src/Game/Game/UpdateSystem.cs:224

## Roots

- decompile
