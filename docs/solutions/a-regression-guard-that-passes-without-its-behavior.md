---
date: 2026-08-18
area: any test written as a regression guard
symptoms:
  - 'a test named for a behavior passes with that behavior deleted'
  - 'a review round keeps finding holes in the guard the previous round added'
tags: [testing, mutation-testing, review, regression-guard]
---

# A regression guard that passes without its behavior

## Problem

A review found a defect, a fix landed, and a test was written to hold it. The test passed. It also
passed with the fix removed, so it held nothing. Four separate guards did this in one session, each
written immediately after the defect it was meant to pin, and each found by a later review round
rather than by the suite.

## What didn't work

**Naming the test after the behavior.** `matches a construct whatever its case` asserted only that
the construct was quoted back in the message, and that string came from a slice of the raw input,
independent of the case folding the name refers to. Deleting `.toLowerCase()` left the suite green.

**Choosing an input that exercises the code path.** `names no construct when a frame carries a tail
shaped like the engine's` fed a selector holding no pseudo-class at all, so it took the generic
branch under the rule and without it alike. The path ran; the rule was never load-bearing for the
assertion.

**Asserting the absence of something.** Guards of the form `expect(...).not.toContain(x)` pass when
the mechanism silently drops the input before judging it. One extractor filtered claims with
`startsWith(':')`, so an element-qualified claim was dropped rather than checked, and the assertion
it should have failed never saw it.

**Reasoning about it.** Every one of these looked correct on reading, and each was written by the
same author who had just diagnosed the defect and held it clearly in mind.

## Root cause

A guard is written from the author's model of the defect, and the model is what makes the test look
sufficient. Whether the assertion is actually *coupled* to the behavior is a different question,
and reading cannot answer it — the failing input and the passing input have to differ by that
behavior alone, which is hard to verify by inspection precisely when the mechanism is subtle enough
to have needed a guard.

## Fix

Mutate the behavior and re-run before believing the guard. Back the fix out, or invert its
condition, and confirm the suite goes red — and that the failure names the test you just wrote,
not an unrelated one:

```bash
cp src/module.ts /tmp/module.bak
# remove the fix, e.g. .toLowerCase(), a bounds check, an early return
bun test path/to/suite            # expect exactly the new test to fail
cp /tmp/module.bak src/module.ts
bun test path/to/suite            # expect green again
```

Scripting a loop over several mutations is worth it where one guard covers several shapes: it
distinguishes "the guard bites" from "the guard bites on the one case I had in mind". The same
session found a guard that failed on three overclaim shapes and silently cleared a fourth.

## Prevention

Treat a mutation run as part of writing the guard, not as an audit of it. A guard that has not been
seen to fail is a guard whose coupling is unverified, whatever its name says.

Where a review keeps returning findings against the same guard round after round, that is the
signal to stop hardening and reconsider what the guard is worth: the value it protects is bounded,
and a guard binding two hand-maintained copies of a fact is bound approximately at best.
