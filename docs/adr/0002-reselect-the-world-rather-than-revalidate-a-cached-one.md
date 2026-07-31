---
date: 2026-07-31
status: accepted
area: plugins/unity-devtools/sdb
---

# Re-select the world every operation rather than revalidate a cached one

## Context

The ECS layer remembers what it can for a whole attach, and one of the things it remembered was the
selected world: the `World` mirror, its name, and its `EntityManager`. An `EntityManager` is a single
raw pointer into the entity store, and the targets this plugin drives are player builds with the
collections safety checks compiled out, where a stale one is a dangling write rather than an error.
Keeping that pointer honest across operations is what an entire revalidation apparatus existed for —
three selection modes, each with its own rule for when the cached handle still stood, and the most
intricate reasoning in the layer.

An earlier pass had introduced that cache to save round trips. Measuring the wire afterwards
unsettled the premise it rested on: an invoke wakes the game's suspended main thread and costs ~1 ms,
while every other wire command is answered by the debugger agent for ~50 µs. Revalidation is not
free — it is itself invokes — so the cache had to be re-judged against what it actually saved, which
nobody had measured.

Three alternatives were live, and all three were measured against the freeze time of a real call:

- **Keep the cache**, paying its revalidation and holding the pointer.
- **Re-select every operation**, memoizing only the world's name.
- **A hybrid**, caching named worlds only, revalidated by liveness.

## Decision

**Re-select the world on every operation, and memoize nothing about it but its name**, keyed by the
identity of the mirror that answered it.

Two findings settled this, and the second is the one that generalizes.

The cache saved nothing on the path that matters. Serving a cached default world spent two invokes
(the default-injection lookup, then the liveness check) where re-selecting spends two (the same
lookup, then the `EntityManager`). Measured three times, the two paths are identical at 6 invokes and
14 wire commands. The cache was paying for itself in the same currency it claimed to save.

And the revalidation was never the guard it appeared to be. **A check that only decides whether to
REBUILD is not a guard on what the caller receives, when the rebuild re-runs the same selection.**
When the liveness check failed, the rebuild re-read the same static, found the same world, and
handed back the same handle without checking anything. So the check gated a cache entry, never the
pointer the caller got. Re-selection loses no safety on that path because there was none there to
lose — a fact worth stating plainly, because the code read as though there were, and two independent
reviewers later read it that way too.

The cost is not zero everywhere: naming a world explicitly regresses by 3 invokes per operation
(+19% to +30%), because re-selection walks `World.All` where the cache revalidated with one call.
That is accepted deliberately. One world is the norm for DOTS player builds, multi-world is
characteristic of Unity NetCode, and the shipped skill never instructs an agent to name one.

## Consequences

Two categories of reasoning leave the codebase: revalidating by mirror identity, and revalidating by
liveness. With them go the selection-mode enum that existed only to choose between them. There is no
longer a rule about when a cached world is still good, because there is no cached world.

The sharpest failure mode in the layer — a dangling write through an `EntityManager` held past the
moment it was fetched — stops being possible by construction rather than by discipline.

The general rule this establishes: **a memo must pay against a whole call, not against a round
trip.** Counting round trips saved is not evidence a memo earns its place; the invoke count of the
smallest real call that exercises it is. A memo whose upkeep is itself invokes can cost more than it
saves while appearing to save a great deal.

Its corollary, for reading code rather than writing it: before crediting a guard with safety, trace
what happens after it fails. A guard whose failure path arrives at the same result guards nothing.

One limit is unchanged by this decision and is not detectable by selection: a world the target has
disposed while its default static still points at it is returned unchecked. Naming a world cannot
hit that case, since a disposed world has left `World.All` and the name fails with the live-world
list instead.

The hybrid remains ~25 lines to add back, and the numbers to weigh it against are recorded in
[`docs/solutions/sdb-round-trips-are-not-equal-cost.md`](../solutions/sdb-round-trips-are-not-equal-cost.md),
which also owns the cost model itself. This record owns the choice and the rule beneath it.
