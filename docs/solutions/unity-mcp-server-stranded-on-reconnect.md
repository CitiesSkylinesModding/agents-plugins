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
- **Relying on graceful SDB teardown.** Shutdown does untimed synchronous wire round-trips, which stall
  forever when the debuggee stops replying while its socket stays open — exactly the state a
  crash-handler/WER freeze produces, since it suspends every thread.
- **`Environment.Exit` as the failsafe.** It deadlocks: ConsoleLifetime's ProcessExit handler waits on
  the very shutdown that stalled.

## Root cause

The server's only lifetime signal was the client's cooperation, funnelled through a path that a hung
wire operation can block indefinitely.

## Fix

Three independent signals, all in `mcp/`, none of which ever touches another process (so concurrent
servers for two games or two harnesses stay safe):

- `ParentWatchdog` — parent pid death.
- `StdinWatchdog` — client end of the stdin pipe closed, observed with non-consuming `PeekNamedPipe`.
- `HardExit` — armed on every host stop; `Process.Kill` on self after a 5 s grace.

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
  Where-Object { $_.CommandLine -match 'UnityDevtools\.Mcp\.csproj' }
```

A survivor older than the failed reconnect is the culprit; `Stop-Process -Force` takes the server with
it (kill-on-close job object) and is safe for the game, whose VM auto-resumes on the closed socket.

That settles the usual case, where those build locks are the whole story — but not a slot genuinely
held by another client, which fails identically from the harness. The port `status` reports tells them
apart:

```powershell
Get-NetTCPConnection | Where-Object { $_.RemotePort -eq <sdbPort> }
```

Empty means the slot was free all along. An established connection from an IDE debugger is the case no
wrapper-killing fixes; free the slot there first.
