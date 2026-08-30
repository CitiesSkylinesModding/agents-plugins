---
date: 2026-08-30
area: plugins/cs2-modding (trunk summaries of references)
symptoms:
  - 'a trunk paragraph read true and its owning reference contradicted it on the bolded line'
  - 'six one-clause corrections in one review pass, all the same shape'
tags: [summaries, compression, qualifiers, trunk, review-gate]
---

# A summary that dropped the bolded qualifier

## Problem

The trunk skill's five-facts paragraphs each compress one reference into a few sentences.
One certifying pass found six of those sentences wrong the same way: each had dropped exactly the qualifier its reference bolds — the mid-session path that reverses the boot-path facts, the instance scope on field initialisers, the anchored registration form, the phases that actually consult an update interval — leaving a trunk-only reader wrong, not shallower.

## What didn't work

Reviewing the summary for plausibility. Every compressed sentence read true on its own, and none was; the loss only shows against the reference's own lines.

## Root cause

A summary keeps the half of a rule that motivated writing it and sheds the scope that bounds it.
The qualifier is bolded in the reference precisely because its author knew a reader would otherwise assume the universal — and the summarizer is that reader.

## Fix

Re-derive each summary sentence against the owning reference's bolded lines and section openers, never against the summary's own plausibility.

## Prevention

When writing or reviewing a tier-up summary, grep the owning reference for `**` and check each bolded rule against the summary: a bold with no trace in the summary is the candidate wrong-maker.
[a-verdict-that-sheds-its-precondition.md](a-verdict-that-sheds-its-precondition.md) is the excerpting flavour of the same loss.
