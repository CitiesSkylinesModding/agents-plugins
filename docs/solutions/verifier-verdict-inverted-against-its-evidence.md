---
date: 2026-08-16
area: review-pipeline/verifiers
symptoms:
  - 'verifier returned REFUTED while its quoted evidence proved the candidate'
  - 'verifier returned CONFIRMED while its quoted evidence disproved the candidate'
tags: [review-gate, verifiers, subagents, verdicts]
---

# Verifier verdict labels inverted against their own evidence

## Problem

In one review run, three verifier sub-agents returned verdict labels that contradicted the evidence quoted beside them — `REFUTED` on candidates their own trace proved, `CONFIRMED` on one it disproved.
An orchestrator keying on the label alone would have dropped real defects and applied a non-fix.

## Root cause

A verification brief carries two possible verdict targets: the candidate ("the doc is wrong here") and the doc itself.
When the brief does not pin one, each verifier binds `CONFIRMED`/`REFUTED` to whichever target it happened to frame, so the label's polarity is unreliable even while the evidence is sound.

## Fix

Normalize every verdict from its quoted evidence, never from its label; a verdict whose evidence argues the other way is read by the evidence.

## Prevention

Pin the target in the brief — "verdict is on whether the EDIT is correct: CONFIRMED = edit correct; REFUTED = edit wrong, with the proving quote" — and require the evidence to quote the proving line, so a flipped label is detectable.
