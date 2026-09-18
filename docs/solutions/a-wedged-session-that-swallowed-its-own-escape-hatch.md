---
date: 2026-09-17
area: plugins/unity-devtools/sdb
symptoms:
  - 'suspend, resume or detach never returns and has to be cancelled'
  - 'status answers instantly and healthily while every other tool hangs'
  - 'only reconnecting the MCP server clears it; attach and detach cannot'
  - 'the game stays frozen after the server is killed'
tags: [sdb, invoke, timeout, concurrency, wedge, liveness]
---

# A wedged session that swallowed its own escape hatch

## Problem

One tool call stops returning. Every later call stops returning too -- including `detach` and
`attach`, the two that exist to recover a session. `status` keeps answering, instantly, reporting a
healthy attached session throughout. Cancelling the calls does nothing; only reconnecting the MCP
server clears it, and the game can be left frozen even after the server is killed.

Rare and clustered: across a few thousand unity tool calls in this machine's transcripts, around
one in a hundred hung, and all of them on the days a game crashed and was relaunched mid-session.
The longest were `suspend` calls still outstanding minutes later.

## What didn't work

Reading it as a network problem. The debugger connection never leaves the machine: the game
advertises a docker bridge address the host owns, and `ip route get` resolves it `dev lo`. No NAT,
no connection tracking, nothing to reap -- so keepalive, idle timeouts and MTU are all out.

Reading it as two sessions fighting over the single debugger slot. That IS a real defect -- a second
client is accepted and then never greeted, which `SdbSession.Connect` used to report as "nothing
there is speaking the Soft Debugger protocol" -- but it is not this one: of 184 failures, **none**
was the first unity call of a session, and the hangs all followed successful calls seconds earlier.

Reading `status` as a liveness check. It answers from `stateGate` and touches no wire, so it stays
cheerful through every one of these failures. A tool that cannot fail is not evidence.

## Root cause

**The vendored client completes an invoke from its REPLY and from nothing else.** When the
connection drops, the disconnect path sets the flag, pulses `reply_packets_monitor` -- waking every
plain command waiting on a reply -- and never touches the invokes in flight, which sit on
`AsyncWaitHandle.WaitOne()` with no timeout. A game that crashes while an invoke is out therefore
leaves that call waiting forever.

That wait holds `UnitySession.gate`, and the gate had no timeout either, so every later call queued
behind it with nothing to look at. `detach` queued too, which is what made the session
unrecoverable: the escape hatch was waiting on the thing it exists to break.

`SendReceive`'s reply wait was unbounded in the same way, for every plain command.

## Fix

Three bounds and one hatch, each at the layer that owns the wait:

- `Invoker.Awaited` bounds an invoke (60 s, generous because an invoke runs the game's own code) and
  raises an `IOException`, which `UnitySession` already reads as a lost connection: the attach is
  discarded and the next call reattaches.
- A vendored patch bounds `SendReceive`'s reply wait (30 s) the same way. The deadline spans the
  whole loop, not one `Monitor.Wait`: any other reply pulses the monitor and would restart it.
- `UnitySession.Holding` refuses the gate after 90 s and names what holds the debugger and for how
  long, instead of queueing. `status` reports that age as `busySeconds`.
- `Detach` gives the gate two seconds and then closes the transport under the operation holding it
  (`Connection.ForceDisconnect`, which speaks no wire command). The debuggee resumes the game on the
  closed socket, and the operation's wait ends -- though not always at once: the same reply-only
  completion above means an invoke already handed over is freed by its own 60 s bound rather than by
  the close, so the break guarantees an end to the wait and not an immediate one.

That last one is rationed. A close with no protocol goodbye is how a debuggee ends up not noticing a
client left, and a socket it never reclaims is a cost it carries to the end of its life -- see
[a debugger agent that stopped accepting](a-debugger-agent-that-stopped-accepting.md). So a
connection already dead is severed freely, and a live one only on a second ask, once the operation
has held for a stated age; the first is refused with that age and the price.

The age is a deliberate speed bump, not a derivation, and it is worth being clear about why none is
available: an operation is any number of bounded waits, not one, so no finite age separates a
healthy long call from a stuck one. What the bump buys is that an agent retrying `detach` on reflex
cannot spend a descriptor, while a caller who means it still gets their session back.

Its ORDER against the gate refusal is derived, and has to be. A caller sent to `detach` by that
refusal has already waited the gate out, so it arrives holding an age past it: a sever threshold at
or below the gate's wait fires on the first ask, and the second ask the ration is made of is never
asked for. The same holds for an age nobody stamped — `null` loses every comparison it is in, so a
gate holder that has not recorded when it took the gate reads as older than any threshold rather
than younger. Both are ways the ration silently stops existing while its prose still promises it.

## Still open: the async reply path

The three bounds cover the SYNCHRONOUS paths. The vendored client has another, and it is neither
bounded nor guarded:

`Connection.Send (..., cb, count)` registers a reply callback and dispatches it on a thread-pool task
without checking the reply's error code, so a callback that assumes a success payload reads past the
end of an error packet and throws inside a task nobody observes. `Thread_GetFrameInfo` is exactly
that shape, and `ThreadMirror.FetchFrames` latches `fetching` and resets `fetchingEvent` BEFORE the
send while clearing them only after the parse — so the throw strands both, and `GetFrames` then
waits on `WaitAny (DisconnectedEvent, fetchingEvent)` with no timeout.

That matters here because `ObjectMirror.EndInvokeMethodInternalWithResult` calls
`r.Thread.GetFrames ()` after EVERY successful invoke, to refresh the frame cache. So an error reply
to one frame query leaves the gate holder waiting past its own invoke bound, which has already
elapsed by then, and every later invoke on that attach waits the same way.

What saves it is the first handle in that `WaitAny`: the wait ends on the disconnected event, which
is precisely what `Detach`'s sever sets. So the hatch still recovers the session — it is just the
only thing that does, and the refusal's "it will most likely end by itself" is the part that does
not hold for this one. Bounding it properly means another anchored patch on the vendored client --
an error-code check in the callback wrapper is the smaller of the two shapes -- weighed with the
rest in the roadmap's wire-progress entry, since every anchor added is one more that can fail to
re-apply on the next re-vendor.

## Prevention

Every wait on a peer gets a bound, and the bound belongs where the wait is. Three of these were in
three different layers, and bounding any one of them alone still leaves a session that hangs.

Count the paths, not the call sites. Three bounds looked total because every wait anyone had gone
looking for was synchronous; the async one had no bound because nothing had asked whether it was a
different path, and the prose that said "every wait is bounded" was written from the same blind spot.

An escape hatch must not use the mechanism it recovers from. `detach` taking the same gate as the
call it is meant to cancel is the whole failure in one line.

A reporting surface that cannot fail cannot be a health check. `status` answering happily is not
evidence the session is alive -- it is evidence that `status` reads no wire.
