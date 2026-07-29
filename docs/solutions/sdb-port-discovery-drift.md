---
date: 2026-07-29
area: plugins/unity-devtools/sdb/SdbDiscovery.cs
symptoms:
  - 'attach refused with N candidates listed, only one of them the game'
  - 'the SDB port differs from the last run'
tags: [sdb, discovery, ports, windows]
---

# The SDB port drifts between runs, and other apps squat the range

## Problem

There is no fixed formula for the Mono soft-debugger port: the agent picks it dynamically, so it
changes every game launch. Discovery must find it from the outside, on Windows, from listen ports.

## What didn't work

Listing every listen port at or above 56000 and treating them as equals. Arbitrary apps (Rider, Steam,
…) hold ephemeral ports up there, so attach refuses with a handful of "candidates" of which exactly one
is the game.

## Root cause

The 56000-56999 range is a convention, not a reservation.

## Fix

Scan the game process's listen ports in **56000-56999**; `PickSdbPort` falls back to the highest listen
port at or above 56000 so a further drift still resolves. `UnitySession` prefers strict in-range
candidates and treats fallback ones as noise whenever an in-range one exists.

## Prevention

`UNITY_MCP_PORT` pins the endpoint and `UNITY_MCP_PROCESS` narrows by process-name prefix when
auto-discovery picks wrong.

Neighbouring ports a Unity game may also hold, both well below the range and both harmless: **9444**
(Gameface CDP — the two channels coexist) and **55000** (PlayerConnection).
