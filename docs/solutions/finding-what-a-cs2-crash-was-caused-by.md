---
date: 2026-08-03
area: Cities Skylines II logs (diagnosing a crash produced by a live experiment)
symptoms:
  - 'the game crashed to desktop and the cause is not in the game logs'
  - 'Logs/SceneFlow.log stops mid-transition with no error'
  - 'a crash left no log at all'
tags: [cs2, crash, logs, live-verification, diagnosis, evidence]
---

# A crash to desktop, and the evidence for it expires

## Problem

A live experiment took the game down. What caused it is one line in a log that is about to be
overwritten, in a different file from the one the game's own subsystem logs suggest, and for some
crashes it was never written at all.

## What didn't work

**`Logs/SceneFlow.log`.** It ends mid-transition — the last entries are the world teardown starting,
with no error and no stack. It reads like the process simply stopped, and it is **rewritten on the
next launch**, so restarting to "have another look" destroys it.

**Restarting first and investigating after.** The relaunch is what costs the evidence.

## Root cause

The two log sets answer different questions and age differently.

- `<userdata>/Logs/*.log` are the game's own subsystem logs. Useful for the _sequence_ — which mode
  was loading, whether a save completed — and overwritten per launch.
- `<userdata>/Player.log` is the Unity player log and holds whatever was printed last, including
  messages the game logs through `UnityEngine.Debug`. **It holds the crashed run for as long as the
  game has not been restarted**, and moves to `Player-prev.log` on the next launch.

`<userdata>` is `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II`.

**Some crashes to desktop write nothing usable to either.** A hard native fault can take the process
before anything is flushed, so an absent log is not evidence that nothing was logged.

## Fix

Read `Player.log` before restarting anything; after a restart, the same content is in
`Player-prev.log`. Copy both, plus `Logs/`, before relaunching.

Then **count the occurrences of the candidate line**, which is what separates a cause from the last
thing printed:

```bash
grep -c "Owner has no SubObject" "$USERPROFILE/AppData/LocalLow/Colossal Order/Cities Skylines II/Player-prev.log"
```

One hit naming the entity the experiment touched, and zero in the healthy session that follows, is a
cause. Hundreds of hits mean the message is routine and the experiment's entity was merely last.

## Prevention

Snapshot the logs as the first action after a crash, before the reflex to relaunch. Grep the
decompiled source for the message to find its emitting site — it gives a `file:line` and the query
around it, which turns "it crashed" into a named mechanism.

Note that a `Debug.Log` line is a marker of the failure and not the failure itself: the process died
after it, and what killed it is a separate question the log usually cannot answer.
