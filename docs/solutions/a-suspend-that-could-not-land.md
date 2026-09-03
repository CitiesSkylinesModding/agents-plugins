---
date: 2026-09-03
area: plugins/unity-devtools/sdb
symptoms:
  - 'The vm is not suspended'
  - 'a tool failed with a wire error immediately after an advance window'
  - 'an eval that works on its own fails when it follows a heavy debuggee operation'
tags: [sdb, suspend, invoke, retry, advance, not-suspended]
---

# A suspend that could not land on a busy main thread

## Problem

`Invoker.Retrying` exists because NOT_SUSPENDED is a normal transient right after attach, so a tool
built on the invoker reads the state as handled. It is not handled when the debuggee's main thread
stays busy longer than the retry budget: the suspend never lands, and `VMNotSuspendedException`
reaches the caller as `The vm is not suspended` — a wire word from a tool that has its own words for
everything else that can go wrong.

## What didn't work

Reading the retry as a guarantee. It is bounded (`sdb/Invoker.cs:316` retries while `attempt < 20`),
and a budget sized for the moment after an attach does not cover a stall measured in seconds.

Sampling faster to catch the busy state as it passes. `advance` clamps its window to 0.1 s, so the
observation costs at least that much and the stall is not always that long.

## Root cause

The wire refuses a suspend for as long as the runtime will not stop, and a debuggee doing real work
on its main thread will not stop. `UnityEngine.ScreenCapture.CaptureScreenshot` at four times the
pixel count held the thread for seconds; every operation attempted in that span failed the same way.
Reproduced three times against the reference target, on 0.1 s and 3.5 s windows alike.

Two shapes reach a caller. `advance` runs its `after` snippet outside the try that wraps the window,
so the failure arrives with no diagnosis. And any tool that evaluates right after `AdvanceHold` meets
it directly — which is every tool built from a sequence rather than one operation.

The same probe settled what the engine does with the file, which is what makes the stall legible: it
is absent throughout the encode and whole when it appears, never partial. The busy thread IS the
encode, and a missing file is not evidence that nothing is happening.

## Fix

Decide what NOT_SUSPENDED means for the operation rather than letting it out. `Screenshot.Take`
treats it as the lateness it is and spends another window against it; the capture request, which runs
before any window exists to spend, converts it into a message naming a previous capture still
encoding and asking for a retry.

## Prevention

A tool that evaluates after letting the game run owns this state. The invoker's retry covers a
transient; a stall is the caller's to interpret, and the interpretation is nearly always "not ready
yet" rather than "broken".
