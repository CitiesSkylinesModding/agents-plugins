---
date: 2026-08-11
area: any experiment run to settle a claim before writing it down
symptoms:
  - 'a fix works and the write-up credits the wrong half of it'
  - 'a reviewer refutes a claim using output the author had already collected'
  - 'a corrected passage is wrong again next round, on a neighbouring gap'
  - 'a test skips for the ambient reason that fits, not the one that holds'
tags: [experiment-design, verification, over-reach, confounding, correction]
---

# A result attributed to the wrong change

## Problem

Four claims shipped broader than their evidence in one session. Each was caught by a different
reviewer, none by re-reading evidence already in hand.

## Root cause

Two mechanisms, one habit — taking the first explanation consistent with the evidence without
separating it from the alternatives.

**Two variables moved, one got the credit.** An `.editorconfig` guard started firing after
`EnableNETAnalyzers` was added *and* a blocking error was removed in the same step. The write-up
credited the property. `EnforceCodeStyleInBuild` alone does it, and the shipped version would have
switched on the whole CA rule set.

**A cause that fitted was never separated from the one that held.** A test skipped on "a game is
advertising itself" while the user's game was running, so that was reported as the reason — twice.
The real cause was a sibling test class broadcasting to the same multicast group in parallel.
Running the class on its own took eleven seconds and settled it, and was available from the first
skip.

**Disconfirming evidence sat unread in output already collected.** PolySharp printed
`System.Range.g.cs` in a file listing that had been read hours before the prose claimed list
patterns need only `System.Index`.

## Fix

Move one variable per run. Where a fix needs two changes, land one, observe, then the other — the
second build costs a minute and is the only thing separating the two explanations.

Before writing a claim down, re-read the raw output the session already produced, looking for what
contradicts it rather than what supports it. It is cheaper than any reviewer and finds a different
class of error.

## Prevention

A confounded run is not evidence, so record which single thing changed alongside the result.

Correcting one gap opens the next: the same passage was wrong on three consecutive rounds, each
fix correct and each opening a neighbouring hole. When a claim comes back wrong twice, narrow it to
what was actually measured and state the boundary, rather than extending the list again.

[A derivation stopped at the line that agreed with it](decompile-read-stopped-at-the-confirming-line.md)
is the reading-shaped version of the same habit.
