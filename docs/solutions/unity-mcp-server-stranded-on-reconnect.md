---
date: 2026-07-29
area: plugins/unity-devtools/mcp
symptoms:
  - 'attach fails because another debugger client holds the SDB slot'
  - 'MSB3027: could not copy, the file is locked by process (pid)'
  - 'a `unity-devtools-mcp` process survives after /mcp reconnect'
tags: [mcp, process-lifetime, shutdown, sdb]
---

# A stranded server keeps the exclusive SDB slot after reconnect

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

Expect one leaked wrapper+server pair per such reconnect; kill the old `dotnet run` wrapper by hand
and the server dies with it (the dotnet CLI puts the child in a kill-on-close job object). Watch
anthropics/claude-code#79740; once fixed, the manual kill goes away.
