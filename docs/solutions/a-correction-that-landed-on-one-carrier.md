---
date: 2026-08-26
area: plugins/cs2-modding/skills and docs/research (any prose correction sweep)
symptoms:
  - 'a section heading asserts what its own body denies three lines below'
  - 'a claim is corrected and the next round finds the same claim standing elsewhere'
  - 'a dated correction note itself states what the correction retired'
tags: [prose, correction, sweep, carriers, review-gate]
---

# A correction that landed on one carrier

## Problem

Across a seven-round review loop, the dominant defect was never a wrong fix — it was a right fix applied to one statement of a claim while another statement of the same claim stood untouched.
Four of the seven rounds were mostly this, and the certifying pass still found more.

## What didn't work

Sweeping by subject rather than by the retired phrasing, which the root `CLAUDE.md` already requires.
It is necessary and it was being done. It still missed carriers, because a subject sweep greps prose and these carriers are not prose.

## Root cause

A claim lives in more places than its sentence, and the other places do not read like the sentence:

- **The section heading** whose body you just corrected. Fixed twice in this loop, missed twice.
- **A forward reference earlier in the same file** — "the sync that completes every job in the world, whose section below owns that cost."
- **A table cell** stating the claim in five words.
- **A hand-incremented count** elsewhere in the section: "Two more escape both" after a third was added.
- **The bridge paragraph in the matching `docs/research/` file**, which is what the next authoring pass runs on.
- **A supersede note that itself asserts what the correction overturned** — appending a note is not editing the original, and the note is new prose that can be wrong in its own right.
- **A pointer's promise**: "`performance-and-memory` owns that trade in full" is a claim about another file's contents.

## Fix

After a correction lands, before the round closes, re-read the *whole section* — not the hunk — and check the heading, any table, any count, and every pointer into or out of it. Then open the matching research file and correct the derivation, not just the prose it produced.

## Prevention

Treat a correction as landing on a *set* of carriers and enumerate the set before editing any of it.
The check that catches the most for the least: read the corrected passage top to bottom as a stranger, because a heading contradicting its body is invisible in a diff and obvious in a read.

Where a claim spans two references, correct both in the pass that found it — deferring drops it, and the two then disagree in the tree until someone notices.
