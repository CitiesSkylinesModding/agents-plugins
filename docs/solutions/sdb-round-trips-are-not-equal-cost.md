---
date: 2026-07-31
area: plugins/unity-devtools/sdb
symptoms:
  - 'a round-trip reduction delivered far less speedup than its count predicted'
  - 'a memoized failure survived the moment that caused it'
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

| operation                          | mean   | median |
| ---------------------------------- | ------ | ------ |
| instance invoke (`World.get_Name`) | 927 µs | 910 µs |
| `VM_GetTypes`                      | 93 µs  | 88 µs  |
| batched static read, 6 fields      | 54 µs  | 52 µs  |
| `Method_MakeGenericMethod`         | 41 µs  | 37 µs  |
| single static field read           | 37 µs  | 35 µs  |
| `Type_GetObject`                   | 36 µs  | 34 µs  |

An invoke costs about **26x** a bare wire command, with a hard floor near 790 µs and no long tail.

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

The figures above are a floor: measured over loopback, under a held suspend, on a trivial
cached-string getter. A real invoke adds its own debuggee-side work on top, and the first touch of any
type (forcing its method or field list) costs far more than any steady-state number here.

To re-measure: a throwaway console project referencing `UnityDevtools.Sdb`, attaching through
`SdbSession.Connect`, one `vm.Suspend()`, then N timed repetitions per operation kind with warm-ups
discarded, and `vm.Resume()` plus dispose in a `finally`. Keep it read-only and keep the window short;
a left-suspended game is a frozen game.

Which vendored accessors reach the wire at all is a separate question, answered in
[`sdb-vendored-client-limits.md`](sdb-vendored-client-limits.md).
