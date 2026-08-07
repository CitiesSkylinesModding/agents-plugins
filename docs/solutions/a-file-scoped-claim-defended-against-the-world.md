---
date: 2026-08-08
area: plugins/cs2-modding/skills (shipped-prose review)
symptoms:
  - 'a warning-block middle line survived two independent review verdicts and was reversed on the third'
tags: [review, prose, quantifier, warning-block]
---

# A file-scoped claim defended against the world

## Problem

`debug-patch-signals.md`'s warning line read "every game symbol named below is checkable only there" — false, because the file names no game C# at all. The line survived two review rounds before a third reversed both verdicts.

## What didn't work

Both defenses checked the world instead of the sentence: the decompile does contain the code producing the file's signals (`GameManager.GetVersionsInfo` at `GameManager.cs:2090` builds the first log line), so "the decompile is relevant here" felt like confirmation. Relevance of the source is not what the sentence asserts.

## Root cause

The sentence quantifies over _symbols the file names_, and that set was empty. A universally quantified claim over an empty domain reads as harmlessly true to a reviewer holding the wider context, so each pass re-confirmed it from the source's contents rather than from the file's.

## Fix

The line was cut back to what the file actually loses without the source: "Every signal below is a string in a log, a config file or a shipped binary."

## Prevention

Before judging a quantified sentence, enumerate the members it actually ranges over — in the file, not in the world. An empty or mismatched domain fails the sentence regardless of how true the surrounding facts are.
