---
date: 2026-09-17
area: plugins/unity-devtools/sdb
symptoms:
  - 'accepted the connection but sent no Mono debugger greeting'
  - 'every attach fails while the game is running and still listening'
  - 'reattaching worked all session and then stopped, with nothing attached'
  - 'the game port shows a listening socket whose accept queue never drains'
tags: [sdb, attach, wine, proton, file-descriptors, liveness]
---

# A debugger agent that stopped accepting, and the descriptors behind it

## Problem

Attaching fails against a game that is running, rendering, and still listening on its debugger port.
Nothing holds the slot -- no established connection at all -- and the failure repeats for the life of
the game process. Restarting the game is the only thing that clears it.

## What didn't work

Reading it as a slot held by another client. That is the ordinary cause and the message says so, but
here `ss` showed zero established connections on the port.

Reading it as the network. The connection never leaves the machine, however foreign the address a
game under Proton advertises looks -- [a wedged session that swallowed its own escape
hatch](a-wedged-session-that-swallowed-its-own-escape-hatch.md) has the route.

Reattaching, detaching, restarting the MCP server. None of them reaches the debuggee side, which is
where the state lives.

## Root cause

**The agent stops calling `accept()`, and the kernel keeps completing handshakes without it.** That is
what makes the failure so confusing from the client: `connect()` succeeds, so the port looks healthy,
and then nothing is ever said. The tell is the listening socket's receive queue, which counts
connections the kernel has completed and the application has not taken:

```
LISTEN  Recv-Q 3  Send-Q 16   0.0.0.0:56965
```

Three, not draining. A healthy agent sits at zero.

Behind it, a leak. Every debugger connection the agent has ever served is still in CLOSE-WAIT, which
means the peer's FIN arrived and the agent never closed its side -- it never noticed that client
leaving. Under Wine each of those costs **two** descriptors, because every socket is duplicated into
`wineserver` for handle bookkeeping, and both holders show up:

```
CLOSE-WAIT  <game>:56965  <host>:47878
  users:(("<game>.exe",pid=...,fd=727),("wineserver",pid=...,fd=970))
```

The app-side descriptor still being open is what says the agent never closed it. That second holder
does not exist on native Windows or native Linux.

Measured when the agent gave up: 27 sockets in CLOSE-WAIT, the game at 975 descriptors with its
highest numbered 1013, `wineserver` at 1200.

**What that count does NOT mean.** 1013 sits one below `FD_SETSIZE`, and code still built on
`select()`/`fd_set` goes silently blind to a descriptor at or above 1024 -- no error, no log, exactly
this failure's signature. Tempting, and wrong: a freshly restarted game with the same city loaded
opens at **983 descriptors, highest 1149**, and its agent accepts perfectly well from up there. The
number near the cliff was a coincidence, and a healthy agent lives past it.

So the leak is established and the ceiling is not. What exhausts, and after how many connections,
remains unknown from outside the debuggee -- as does why the agent misses a disconnect under Wine
when it does not on Windows.

## Fix

None on the client side; the state lives in the game. Restart it.

What the client owes is not spending the budget. Every attach leaves a socket behind for the life of
the game process, and nothing reports how many are left, so the only safe assumption is that they are
finite and unreplenished. One long-lived session costs one; a session that reattaches per failure
costs one per failure.

## Prevention

Treat attach churn as consumable rather than free. Keep one server per game, and reach for `detach`
to hand the slot to another debugger, never as a way to clear a failure -- a client-side fault is not
improved by a fresh connection, and the attempt is charged to the game either way.

A workaround that reattaches is the expensive kind. Agents independently invented detach-then-eval to
get around [an unparked main thread](a-suspend-that-landed-on-an-unparked-thread.md), and paid for it
here, in a currency nothing reported.

Watch the budget without a debugger:

```bash
ss -tln | grep <port>                     # Recv-Q above 0 and not draining: it stopped accepting
ss -tn state close-wait | grep -c <port>  # one per attach, never reclaimed
```

The listening socket's queue is the diagnosis; the descriptor counts are context, not a threshold.
