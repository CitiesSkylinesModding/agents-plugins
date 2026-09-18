---
date: 2026-08-03
area: plugins/unity-devtools (driving a running game to settle a claim)
symptoms:
  - "an eval reports 'The vm is not suspended' while a trivial expression still evaluates"
  - 'a live experiment reproduces nothing and the claim it was testing was true'
  - 'suspend returns heldSuspends 1 and state reads still fail'
tags: [unity, sdb, live-verification, experiment-design, false-negative, not-suspended]
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

**Reading the refused reads as a debugger problem.** When state reads began failing, the first guess
was a suspend left held by an earlier call. `debug_status` reported `heldSuspends: 0`, and taking an
explicit suspend returned `heldSuspends: 1` while reads kept failing.

## Root cause

**A paused simulation runs no simulation systems.** Nothing in the tooling says so, and the game
looks identical over a debugger connection either way. `SimulationSystem.frameIndex` is the tell: it
stops advancing.

**`"The vm is not suspended"` on state reads means the main thread has not parked yet**, not that the
session is misconfigured. A suspend that returns has flagged the thread; one flagged inside native
code keeps running it and parks where an invoke can run only on re-entering managed code. Arithmetic
keeps evaluating because it needs no invoke, which is what makes the failure look selective and
therefore look like a tooling bug. The mechanism, the measured park latencies and the wait that now
absorbs them:
[`a-suspend-that-landed-on-an-unparked-thread.md`](a-suspend-that-landed-on-an-unparked-thread.md).

## Fix

Sample `SimulationSystem.frameIndex` twice before trusting any negative result:

```csharp
var ss = world.GetExistingSystemManaged<Game.Simulation.SimulationSystem>();
$"frame={ss.frameIndex} speed={ss.selectedSpeed}"
```

Two identical readings mean the experiment tested nothing. On the not-suspended signature, send the
call again rather than rebuilding the session around it: the park it was waiting on arrives on its
own.

## Prevention

A negative live result is evidence only when the experiment could have produced a positive one, so
record what proves that alongside the result — a rising frame index, an armed exception break that
did not fire, a query that matched. A run without it is not a refutation and must not be used to
withdraw a standing claim.

State a causal attribution as a hypothesis until an experiment separates it from the alternatives.
A clean timeline plus a plausible mechanism is neither.
