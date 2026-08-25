---
date: 2026-08-25
area: docs/research (verdict prose in the cs2-modding pipeline)
symptoms:
  - 'one reviewer refuted a contradiction that a later pass confirmed from the same lines'
  - 'a shipped rule derived from a Verdict sentence dropped the caveat that sat beside it'
tags: [research, verdicts, corrections, excerpting, review-gate]
---

# A verdict that sheds its precondition

## Problem

A research verdict read "`Schedule().Complete()` is strictly worse than `Run` — same blocking, same
chain drain" with its precondition — the caller completes conflicting work first — in the paragraph
above it. Read whole, the section was right; excerpted, the verdict licensed rewriting
`Schedule(dep).Complete()` as a bare `Run`, which waits on nothing — a silent race on a build whose
safety system is compiled out.

## Root cause

The Verdict line is the sentence later passes copy, and qualifiers in neighbouring paragraphs do not
travel with it. One reviewer judged the pair coherent and refuted the bug report; a second, reading
the verdict as the excerpt it becomes, confirmed it. Both read the same lines correctly.

## Fix

Inline the condition into the verdict itself: "strictly worse than `Run` *preceded by the same
conflicting-work completions*."

## Prevention

Write every Verdict — and any bolded rule — to survive excerpting: whatever conditions its truth
lives in the same sentence. Two reviewers disagreeing over the same lines is the tell that sentence
and context diverge; repair the sentence, not the readings.
