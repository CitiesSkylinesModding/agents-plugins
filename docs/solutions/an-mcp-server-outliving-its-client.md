---
date: 2026-10-30
area: a Node MCP server over stdio
symptoms:
  - 'the server keeps running after its client went'
  - 'a running call never cleans up when the session ends'
  - 'process.exit() returns nothing and the process stays'
tags: [mcp, stdio, shutdown, signals, node, cancellation]
retire_when: "the SDK's StdioServerTransport handles the end of stdin and Claude Code's stdio cleanup closes stdin first; check the SDK's source and the installed binary's strings"
---

# An MCP server outliving its client

## Problem

A tool call that leaves something behind for its own cleanup depends on the server hearing that its
client went. `cs2-dev`'s `restart` writes a one-shot options file for the game, and a session ended
mid-launch left it for the next launch to read. Other exits left a server running with no client.

## What didn't work

**Relying on the transport.** `StdioServerTransport` ignores the end of its input and any error on
it, so a client that closes stdin leaves the transport open and every call running.

**Listening for stdin end alone.** The SDK's own client closes stdin first. Claude Code does not:
its stdio cleanup sends SIGINT, SIGTERM at 100 ms and SIGKILL at 500 ms, with no stdin close (read
from the 2.1.201 binary; 2.1.314 sends the same SIGINT first; Codex unchecked). An unhandled signal
ends the process with the cleanup undone.

**Ending with `process.exit()`.** It hangs on a step stuck in the thread pool: with a FIFO as the
log being read, the server stayed past 5 s. A signal ends the process regardless.

**Re-raising a signal under `process.once`.** A handler stays armed until its signal first arrives.
A shutdown that began on stdin end and then sends itself SIGTERM runs that handler, not the exit.

## Root cause

The SDK aborts running calls only when the transport closes, and nothing closes it on the ways a
client actually leaves. Each exit has its own trigger, and the harness allows 500 ms in total.

## Fix

`shutDown` in `plugins/cs2-modding/mcp/src/server.ts`. Stdin end, an error on stdin or stdout,
SIGINT, SIGTERM and SIGHUP all close the transport, which aborts the calls. After a 200 ms grace,
inside the harness's window, the server removes its listeners and raises the signal at itself. With
no signal to pass on the timer is unref'd, so an idle server exits 0 on its own and only a stuck one
takes the SIGTERM.

## Prevention

These paths need a process: spawn the bundle under Node with stubs on `PATH`, start a call, send the
signal or close stdin, then assert the exit and the cleanup's effect. No in-process test reaches
them, since a test harness never ends the way a client does.
