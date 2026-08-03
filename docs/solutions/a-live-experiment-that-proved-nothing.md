---
date: 2026-08-03
area: plugins/unity-devtools (driving a running game to settle a claim)
symptoms:
  - "an eval reports 'The vm is not suspended' while a trivial expression still evaluates"
  - 'a live experiment reproduces nothing and the claim it was testing was true'
  - 'suspend returns heldSuspends 1 and state reads still fail'
tags: [unity, sdb, live-verification, experiment-design, false-negative, wedged-main-thread]
---

# A live experiment came back negative and the experiment was the broken part

## Problem

Two ways a run against the live game produces an answer that is not about the game. Both cost more
than the thing being tested: one retracted a correct finding, the other misdiagnosed the tooling.

## What didn't work

**Concluding from a quiet 20 seconds.** A mutation was applied, nothing happened, and the earlier
attribution built on it was withdrawn. The simulation was **paused**, so the systems that would have
faulted never ran. The withdrawal was worse than the original error — it retracted a claim that
turned out to be right, on evidence incapable of testing it. The maintainer caught it, not the
experiment.

**Reading a failed suspend as a debugger problem.** When state reads began failing, the first guess
was a suspend left held by an earlier call. `debug_status` reported `heldSuspends: 0`, and taking an
explicit suspend returned `heldSuspends: 1` while reads kept failing.

## Root cause

**A paused simulation runs no simulation systems.** Nothing in the tooling says so, and the game
looks identical over a debugger connection either way. `SimulationSystem.frameIndex` is the tell: it
stops advancing.

**`"The vm is not suspended"` on state reads means the game's main thread is wedged**, not that the
session is misconfigured. Mono can only suspend at a safepoint; a thread spinning in native or Burst
code never reaches one, so the suspend request is recorded and never serviced. Arithmetic keeps
evaluating because it touches no VM state, which is what makes the failure look selective and
therefore look like a tooling bug.

## Fix

Sample `SimulationSystem.frameIndex` twice before trusting any negative result:

```csharp
var ss = world.GetExistingSystemManaged<Game.Simulation.SimulationSystem>();
$"frame={ss.frameIndex} speed={ss.selectedSpeed}"
```

Two identical readings mean the experiment tested nothing. On the wedge signature, release any
suspend taken and stop — the process is not recoverable from the debugger, and the remaining
evidence is in the log rather than in the VM.

## Prevention

A negative live result is evidence only when the experiment could have produced a positive one, so
record what proves that alongside the result — a rising frame index, an armed exception break that
did not fire, a query that matched. A run without it is not a refutation and must not be used to
withdraw a standing claim.

State a causal attribution as a hypothesis until an experiment separates it from the alternatives.
A clean timeline plus a plausible mechanism is neither.
