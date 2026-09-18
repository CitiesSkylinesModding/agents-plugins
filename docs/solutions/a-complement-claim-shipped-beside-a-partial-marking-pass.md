---
date: 2026-09-18
area: plugins/coherent-gameface/skills (VOLATILE markers)
symptoms:
  - 'two independent review finders flagged "Unmarked prose holds whatever the engine version." as false'
tags: [markers, volatile, prose, quantifier, review]
---

# A complement claim shipped beside a partial marking pass

## Problem

The gameface skills gained `VOLATILE:` markers on the claims one ticket enumerated, then a sentence defining the marker's complement: "Unmarked prose holds whatever the engine version." The sentence was false the day it was written, and would have let a version sweep move the baseline line over claims nobody re-probed.

## What didn't work

Marking the unmarked claims as reviewers found them. Each round surfaced more — the never-existed lists, the V8 line, most of `gameface-driving`'s CDP matrix — because the ticket's list of claim kinds was a sample of the version-bound prose, never a census of it.

## Root cause

A marker labels one claim; its complement is a promise about every line without one. `plugins/cs2-modding` can state "unmarked prose is architecture and holds" because its authoring pipeline marks exhaustively, per reference. The gameface skills borrowed the token without that pass, so the borrowed complement quantified over lines nobody had judged.

## Fix

The sentence was withdrawn from both skills. There a marker means _re-check this claim_, and an unmarked line promises nothing about the engine version.

## Prevention

State a marker convention's complement only after a pass that judged every line of the tree it covers, and name that pass. Until then define the marker alone. The sibling trap, for a quantified sentence generally: [a file-scoped claim defended against the world](a-file-scoped-claim-defended-against-the-world.md).
