---
date: 2026-09-17
area: plugins/unity-devtools/sdb
symptoms:
  - 'The vm is not suspended'
  - 'an eval fails instantly, and the same eval wrapped in suspend/resume works'
  - 'an eval that works on its own fails when it follows a heavy debuggee operation'
  - 'a tool failed with a wire error immediately after an advance window'
tags: [sdb, suspend, invoke, park, safe-point, not-suspended]
---

# A suspend that landed on a thread that had not parked

## Problem

`eval` answers `The vm is not suspended` from inside the suspend window it opened for that very
call, while the session reports itself attached and holding nothing. Wrapping the same expression in
`suspend` / `resume` makes it work, which reads as a session bug and is not one.

## What didn't work

Reading it as session state. `detach` and a re-attaching eval fail identically; `status` reports
`attached` with `heldSuspends: 0` and a beacon whose host differs from the attached one. All true,
none of it the cause.

Reading it as a WEDGED main thread, as this repo's own prose did until this was measured. Under that
model the process is unrecoverable and the advice is to stop; it recovers on its own, every time,
and the recovery is what the retry was already racing.

Resuming and re-suspending to re-arm the interrupt, on the theory that the suspension was blocking
what the main thread was waiting for. Measured over 14 fresh attaches alternating the two
strategies: 2.1-3.8 s to park either way. Letting the game run between attempts buys nothing, and
costs the window's whole point.

Growing the attempt count. The budget is the thing to fix, but an attempt count sized in units of
one sleep says nothing about the state being waited on.

## Root cause

**A suspend that returns has flagged every thread, not parked them.** A thread flagged inside native
code is *treated* as suspended: the agent records it and lets that native code run on, and the
thread parks at a managed safe point -- where an invoke can run -- only when it next re-enters
managed code. So the window is open, the game is stopped, and the main thread is still out of reach.

Only an INVOKE needs that park. Type lookups, static and instance field reads, frame slot reads are
plain wire commands and answer throughout, which is what makes the failure look selective enough to
read as a tooling bug: `Game.Version.current` answers while `Game.Version.current.shortVersion`, one
property call further, does not.

Measured against the reference target, a city running at ~13 fps under Proton, over 450 windows:
median park 25 ms, p90 ~55 ms, and a tail to 9.4 s whenever the engine dragged a frame out. No
window ever failed to park within 20 s. The old budget -- 20 attempts at 50 ms, ~1 s -- therefore
covered the median by a wide margin and lost to any bad frame, and an explicit `suspend` call
"fixed" the same eval only because the MCP round trip behind it outlasts the park.

The 2026-09-03 screenshot episode is this mechanism with a longer stretch of native code:
`UnityEngine.ScreenCapture.CaptureScreenshot` at four times the pixel count kept the main thread out
of managed code for seconds, and every operation attempted in that span failed the same way.

## Fix

`Invoker.Retrying` waits on a deadline (`ParkWait`) rather than an attempt count, and names the
state when that expires (`MainThreadNotParkedException`) instead of handing up the wire's words for
a state the session is not in. The wait is paid by the first invoke of a window alone: a thread that
has parked stays parked until the window closes.

A tool with a better answer than the wire's interprets NOT_SUSPENDED itself rather than shortening
the budget for everyone -- the wait is still paid in full first: once it expires, `Screenshot.Take`
converts it into a message naming a previous capture still encoding, and its read loop absorbs it as
the lateness it is. Such a tool matches on the CAUSE chain rather than on the thrown type, because
the evaluator it reaches the debuggee through dresses every failure in an `EvalFailedException`, so
a clause naming the wire's own type never runs.

## Prevention

Ask what the operation needed before reading NOT_SUSPENDED as a fault. It is never about the
session, and it is about the main thread only for as long as that thread stays in native code.

A budget for a wait belongs in units of the thing waited on. This one is sized against measured park
latency and its tail; an attempt count was sized against nothing.
