---
date: 2026-08-06
area: docs/research and plugins/cs2-modding/skills (any reference that prescribes a procedure)
symptoms:
  - 'every claim in a passage re-derives clean and the passage still cannot be followed'
  - 'a reader-perspective review returns no findings twice, then four the moment it writes code'
  - 'an instruction to cache and an instruction to invalidate that cannot both be obeyed'
  - 'a corrected count breaks again at the next review round'
tags: [review, verification, prose, agent-facing, review-gate, composition, census]
updated: 2026-08-11
---

# Every claim verified, and the instructions still did not compose

## Problem

The `mod-compatibility` reference went through six review rounds. Correctness finders re-derived
every claim from the decompile. A reader-perspective finder read all three files end to end against
four concrete tasks and returned no findings, twice.

Then the same reader was asked to **write the code** the file prescribes. It found four
contradictions in a single pass, one of which made the file's central caching advice impossible to
follow.

## What didn't work

**Re-deriving each claim.** It checks sentences one at a time, and every sentence was true. The
defect lived in the relationship between three of them.

**Reading the documents as their user.** This is a strong lens — an earlier pass of it found a
recipe that could not work and a missing guard that would have deleted player data. But reading
still lets an agent nod along: each instruction is clear, so nothing snags.

**Treating an empty return as convergence.** Two clean reader passes read as "done". They were
evidence that the lens had stopped finding things, not that the prose worked.

## Root cause

A claim can be individually true and jointly unusable, and only executing the instructions surfaces
it. The passage said three true things:

- cache the detection answer in a `static bool?` filled on first ask, so a negative is not re-probed
  on every call;
- clear it on the mod-set-change event;
- mid-session, a negative from some routes means "not yet" rather than "absent".

Write that and it collapses: `_present ??= Probe()` latches the premature negative, and the only
signal that would clear it has already fired. The reader cannot obey instruction one and instruction
three at once. No single-claim check sees this, because there is no wrong claim to find.

The same pass turned up three more of the same kind — a pointer whose own instruction was what its
target section forbade, a recipe step whose prescribed log line asserted what a later passage
existed to refute, and an imperative (`read it`) naming a value the file never located.

What they share is **distance**: each pair of sentences was right on its own and too far apart to be
read together. A later split of the same file tested that directly. Of the five defects the split's
rounds produced, three came from the new file boundary — and two were sentences three sections apart
that had survived twelve rounds inside one file, and would have survived the merge too.

## Fix

Give one finder a task that spans the passage's constraints and make it produce the artifact. The
brief has four parts, and the first two are what make it bite.

**1. A task built to straddle.** Pick it so that following the prose forces the reader across a
distinction the prose itself draws. The one that worked:

> My mod behaves differently when another named mod is installed. It must be right at boot, right
> when the player enables that mod mid-session, and right when the player disables it mid-session.
> It reaches the other mod two ways: by assembly name, and by asking for a tool the other mod
> registers into `ToolSystem.tools`.

Those two probes sit on opposite sides of the file's own split between routes whose negative is
trustworthy and routes whose negative may mean "not yet". A task inside one section exercises
nothing; the earlier, gentler version — "behave differently when another mod is installed" — passed
twice.

**2. Name the artifacts, so nothing can be waved past.**

> Write the `OnLoad`, the deferred callback, the cache fields, the event subscription, and both
> probes.

An unenumerated "write the code" gets pseudocode with the hard part elided — which is exactly the
part where instructions collide.

**3. Questions that separate a gap from a contradiction.**

> - Did every line come from the file, or did you have to guess?
> - Do the instructions compose, or does one contradict another?
> - Was the distinction stated clearly enough that you knew which class each probe was in, without
>   inferring?
> - Does the file tell you what to do on the disable, given that it says the routes disagree there?

The last one is the shape to copy: take something the prose asserts, and ask what the reader is
supposed to _do_ about it. "The routes disagree" was true, shipped, and actionable by nobody.

**4. Calibration, or the pass reports prose it would have worded differently.**

> Report only where you had to guess, got it wrong, or wasted a pass re-reading. `[]` is the
> expected answer if the code writes itself cleanly.

## Prevention

Where a reference prescribes a procedure, one finder writes the artifact rather than reviewing the
prose. Reading finds gaps; writing finds contradictions, and they are different defects.

It repeats. Run on the fixes from the first pass, a second code-writing pass found two more — one of
them a rule stated in the section that owns deferral and contradicted 111 lines later, which cost
that finder a discarded probe and a full re-read before it saw the conflict. That cost is the
finding: a reader who does not re-read ships the probe.

Read an empty return from a read-only pass as weak evidence. Before concluding a lens has converged,
change what it is asked to produce — if a harder ask immediately finds things, the lens was
under-tested rather than finished.

**Choose the task by what it forces a reader to hold at once, not by where the prose sits.** A
structural change announces that it needs testing and gets it: the split above drew four rounds of
scrutiny in a day. Two sections of one file that contradict each other announce nothing, and the
tasks that caught those cost no more to write than the ones that crossed the boundary — they were
simply aimed at the file's own distances instead of its edges.

**A count corrected by recounting breaks again.** One review corrected the same census three times
(nine → sixteen → nineteen mode classes), each recount matching only the shapes the corrector
already held — this doc's defect at the scale of a number. What held: strip the count of its
authority — ship the set's shape with the re-check attached (the grep or query that re-derives
membership), and have the volatility marker direct the sweep to run it.
