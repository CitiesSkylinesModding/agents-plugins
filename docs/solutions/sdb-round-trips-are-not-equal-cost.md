---
date: 2026-07-31
area: plugins/unity-devtools/sdb
symptoms:
  - 'a round-trip reduction delivered far less speedup than its count predicted'
  - 'a memoized failure survived the moment that caused it'
  - 'two timings of the same code disagreed while the wire counters matched'
tags: [sdb, invoke, performance, caching, measurement]
---

# Round trips over SDB do not all cost the same

## Problem

Every operation this plugin performs is a round trip to a game frozen mid-frame, so cost work gets
planned by counting round trips. Counting them as equal mis-predicts the result. A change that took a
component read from 13 round trips to 6 made it roughly 28% faster, not twice as fast, because most of
what it removed was the cheap kind.

## What didn't work

- **Weighting every round trip the same when estimating.** The estimate and the measurement disagreed
  by an order of magnitude on some paths, in both directions.
- **Wrapping a plain wire command in `Invoker.Retrying` to make its remaining failures durable enough
  to memoize.** `Retrying` catches only `VMNotSuspendedException`, and NOT_SUSPENDED is an
  invoke-path error: a wire command's real failure modes (a refused field id, an unmapped error code,
  a collected object) pass straight through. The wrap changes nothing, so a memo justified by it
  caches a moment.

## Root cause

There are two tiers, and only one of them is expensive.

An **invoke** is not a message. It is a request handed to the game's suspended main thread, which has
to resume, run the method, and signal completion. Every **other** command is answered by the debugger
agent directly, without waking anything.

Measured against the reference target, N=200 per operation over 3 runs, inside one held suspend,
5 warm-ups discarded, run-to-run drift under 4%:

| operation | mean | median |
| --- | --- | --- |
| instance invoke (`World.get_Name`) | 927 µs | 910 µs |
| `VM_GetTypes` | 93 µs | 88 µs |
| batched static read, 6 fields | 54 µs | 52 µs |
| `Method_MakeGenericMethod` | 41 µs | 37 µs |
| single static field read | 37 µs | 35 µs |
| `Type_GetObject` | 36 µs | 34 µs |

An invoke costs about **26x** a bare wire command, with a hard floor near 790 µs and no long tail.

## What a whole call costs

The per-command figures say what to trade. They do not say whether a trade is worth making, because a
memo pays against a **tool call**, not against a round trip. The yardstick is the freeze time of the
smallest real call that exercises the memo, and ~10% of it is the threshold worth a line of code.

Measured against the reference target, arms interleaved across 3 rounds × 20 repetitions, each
repetition a full operation cycle, pooled medians:

| call | invokes | wire | freeze |
| --- | ---: | ---: | ---: |
| read one component off an entity | 6 | 14 | 16 ms |
| list a 17-component archetype | 9 | 23 | 20 ms |
| the same listing, with values | 18 | 41 | 36 ms |
| list a 36-component archetype, with values | 25 | 55 | 45 ms |
| query on one component type | 9 | 26 | 15 ms |
| evaluate an expression naming an enum member | 0 | 7 | 1 ms |

A call that opens its own suspend window pays ~0.5 ms for it, and **a running simulation costs no more
than a paused one** (15.6 ms against 16.7 for the same component read): the main thread parks at a safe
point either way.

One invoke is therefore roughly 5% of a component read, so a memo has to save two or three of them
before it clears the threshold at all. That is the scale on which the ECS memos were settled: the
per-attach component-type descriptions save ~40 ms on a 17-component listing (44 invokes against 9),
while re-selecting the world every operation costs nothing on the DEFAULT path — measured three
times at 6 invokes / 14 wire either way — because the cached path was itself spending an invoke on
the revalidation the re-selection subsumes.

That parity is specific to the default world. Naming one explicitly costs **+3 invokes per
operation, +19% to +30%**: a cached named world revalidated with a single `IsCreated`, where
re-selection walks `World.All` — `All`, `Count`, one `get_Item` per world walked past, then
`EntityManager`. The regression scales with how deep the named world sits in that list and is at its
floor where only one world is live, which is the norm for DOTS player builds; multi-world is
characteristic of Unity NetCode. Reinstating a cache for named worlds only, revalidated by
`IsCreated`, is ~25 lines and deliberately unbuilt — these are the numbers to weigh it against.

## Fix

Count **invokes**, not round trips.

- Trading one invoke for ~20 wire commands is break-even; for five it is a clear win.
- Removing a `Type_GetObject` or a `MakeGenericMethod` saves ~4% of an invoke: worth taking when it
  is free, never worth shaping a design around.
- Batching static-field reads is nearly free (six fields cost ~1.35x one field) and, for the same
  reason, a small prize. It is worth doing because the alternative is usually an invoke, not because
  the batching itself saves much.
- Memoize a failure only when it is a property of the TARGET. A failure describing the moment must be
  left for the next call to re-ask. Catching cannot tell the two apart, so separate them at their
  causes rather than at the `catch`.

## Prevention

The per-command figures are a floor: measured over loopback, under a held suspend, on a trivial
cached-string getter. A real invoke adds its own debuggee-side work on top, and the first touch of any
type (forcing its method or field list) costs far more than any steady-state number here.

Two disciplines bound how any of these numbers may be read, both learned by trusting a clock that was
lying:

- **Absolute times drift ~50% between sessions**, with nothing changed — the same call measured
  10.80 ms in one session and 16.11 ms an hour later, the game paused throughout. Only arms
  interleaved inside one run are comparable, so a before captured in one session and an after captured
  in the next measures the sessions, not the change.
- **Within a run the noise floor is ±12% on heavy calls and ~5% on light ones**, established from arms
  the counters proved to be doing identical work. Invoke and wire counts are exact. Where the clock and
  the counters disagree, the counters decide — that is what caught a memo whose three arms did
  byte-identical wire work while their times spread 15%.

To re-measure: a throwaway console project referencing `UnityDevtools.Sdb`, attaching through
`SdbSession.Connect`, with a counter patched into `Invoker.Invoking` and another into the vendored
`Connection`'s send path, then arms interleaved across rounds rather than run back to back. Time whole
operation cycles for a call-level answer, and single commands under one held suspend for a
command-level one. Keep it read-only and keep the window short; a left-suspended game is a frozen game,
and `Allocator.Temp` allocations accumulate under a held suspend, so release the hold between blocks.

Which vendored accessors reach the wire at all is a separate question, answered in
[`sdb-vendored-client-limits.md`](sdb-vendored-client-limits.md).
