---
date: 2026-07-29
area: plugins/unity-devtools/mcp
symptoms:
  - 'attach fails because another debugger client holds the SDB slot'
  - 'MSB3027: could not copy, the file is locked by process (pid)'
  - 'a `unity-devtools-mcp` process survives after /mcp reconnect'
  - 'Failed to reconnect to unity: -32000'
  - 'reconnect fails while nothing is connected to the game SDB port'
tags: [mcp, process-lifetime, shutdown, sdb]
updated: 2026-08-02
---

# A stranded server survives a reconnect and blocks the next one

## Problem

Unity allows ONE soft-debugger client at a time. A `/mcp` reconnect that leaves the previous server
alive makes every later attach fail, and its build-output locks break the next `dotnet run`.

## What didn't work

- **Relying on the MCP SDK's stdin-EOF shutdown.** It only completes between requests, so a tool call
  wedged on a wire op blocks shutdown from even starting.
- **Relying on graceful SDB teardown.** Shutdown does synchronous wire round-trips, and each one sits
  out its full reply bound when the debuggee stops replying while its socket stays open — exactly the
  state a crash-handler/WER freeze produces, since it suspends every thread. Those waits are bounded
  now, so the teardown ends by itself; it just ends far later than a client waiting to reconnect will
  wait. (They were unbounded when this was first written, which is why the failsafe exists at all.)
- **`Environment.Exit` as the failsafe.** It deadlocks: ConsoleLifetime's ProcessExit handler waits on
  the very shutdown that stalled.

## Root cause

The server's only lifetime signal was the client's cooperation, funnelled through a path a hung wire
operation blocks for as long as its waits allow — which was forever then, and is merely far longer
than anyone will wait now.

## Fix

Three independent signals, all in `mcp/`, none of which ever touches another process (so concurrent
servers for two games or two harnesses stay safe):

- `ParentWatchdog` — the launching wrapper is gone: Windows waits on the process, Unix watches for
  the reparenting that follows its death (`getppid` changing).
- `StdinWatchdog` — client end of the stdin pipe closed, observed without consuming bytes:
  `PeekNamedPipe` on Windows, `poll`'s POLLHUP on Unix.
- `HardExit` — armed on every host stop; `Process.Kill` on self after a 5 s grace.

Each signal needs its own implementation per platform, and for a while had none on Unix: both
watchdogs opened with a `IsWindows()` early return, so on Linux the SDK's stdin-EOF path — the one
a wedged wire op blocks — was the only tie left, and killing the wrapper left the server running,
reparented and still holding the slot.

The dev server also builds into its own `bin/mcp-run/` (`--property:BaseOutputPath` in the root
`.mcp.json`) so builds and tests never collide with a running server. A reconnect inside the ~5 s
dying window can still hit MSB3027 naming the old pid; reconnect again.

## Prevention

One stray shape remains, and it is the harness's: a reconnect **after the MCP config changed** (edited
root `.mcp.json`, or a release bumping the dnx version pin) orphans the old process tree instead of
closing its stdin (anthropics/claude-code#79740) — pipes stay open, no signal ever arrives, and the
server survives until the harness session itself exits.

Expect one leaked wrapper+server pair per such reconnect; it has also been seen after a reconnect that
changed only sources.

A stranded wrapper is therefore the FIRST thing to check when a reconnect fails — the wrapper, since
its server child can die and leave it holding the `bin/mcp-run/` locks alone:

```powershell
Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
  Where-Object { $_.CommandLine -match 'UnityDevtools\.Mcp' }
```

A survivor older than the failed reconnect is the culprit; `Stop-Process -Force` takes the server with
it (kill-on-close job object) and is safe for the game, whose VM auto-resumes on the closed socket.

On Linux the server is a separate `unity-devtools-mcp` child of the wrapper with no job object tying
the two, but killing the wrapper is enough: the parent watchdog sees the reparenting within a second
and takes the child with it. List both anyway, since a child that died on its own leaves the wrapper
holding the locks:

```bash
ps -eo pid,ppid,lstart,args | grep -E '[U]nityDevtools\.Mcp|[u]nity-devtools-mcp'
```

That settles the usual case, where those build locks are the whole story — but not a slot genuinely
held by another client, which fails identically from the harness. The port `status` reports tells them
apart:

```powershell
Get-NetTCPConnection | Where-Object { $_.RemotePort -eq <sdbPort> }
```

```bash
ss -tnp | grep ':<sdbPort>'
```

Empty means the slot was free all along. An established connection from an IDE debugger is the case no
wrapper-killing fixes; free the slot there first.
