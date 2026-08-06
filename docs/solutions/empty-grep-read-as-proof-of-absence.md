---
date: 2026-08-03
area: docs/research (the cs2-modding discovery pipeline, against any decompile)
symptoms:
  - 'a claim reads "nothing in the game consumes it" and the game consumes it'
  - 'a claim reads "the only system that writes it" and eight systems write it'
  - 'a claim reads "the only repository that does X" and four of them do X'
  - 'a grep of the decompiled source returns no hits for something the game plainly does'
tags: [research, decompile, grep, over-reach, verification, false-absence, corpus]
updated: 2026-08-04
---

# A grep came back empty and the pass concluded absence

## Problem

Four shipped claims across the `cs2-modding` prose asserted that something did not exist, or that a
set was complete, each derived from a search whose pattern could not reach what it was claiming
about. All four were false, and none of them read as doubtful — they were the most confident
sentences in their sections.

A search returning nothing is evidence about the search. Turning it into a claim about the game
needs a separate argument that the search could have found the thing.

## What didn't work

**Re-reading the prose.** A false absence is indistinguishable from a true one by inspection: the
sentence is short, flat and plausible either way. Every one survived authoring, the authoring
agent's own re-read, and the orchestrator's end-to-end pass. Only a reviewer told to re-derive the
claim from the source caught them.

**Trusting the citation.** Each claim carried a correct citation. The lines cited said exactly what
the research file reported; the inference drawn from them was the wrong part.

## Root cause

Four distinct mechanisms, which is why fixing one instance taught nothing about the others.

- **A compile-time `const` is inlined at its call sites.** A name-grep finds the declaration and
  nothing else however many consumers exist. `custom-tools` shipped "nothing in the game consumes
  it, so it exists for the frontend and for mods" about a constant the tool UI system consumes to
  build its snap mask.
- **A scoped grep read as a whole-assembly one.** `placement-definitions` shipped "the only system
  that writes it" from a search of a single namespace; eight sites across four namespaces write it.
  The research file recorded the scope, and the shipped prose dropped it.
- **Whole subsystems are invisible to a C# search.** The frontend and everything shipping as data
  are not in the decompile at all. 72% of this game's localization namespaces never appear in C#,
  so `localization` had a rule resting on the opposite assumption.
- **A pattern that names one member of a family, taken for the family.** A corpus sweep for
  `xunit|from 'bun:test'|testing-library` across the mod repositories returned one hit, and the
  catalog shipped "the only repository here that ships tests". Four ship test projects; the other
  three use NUnit, which the pattern never named. This one does not come back empty, and that is
  what makes it convincing — a search returning exactly one hit looks like a finding rather than
  like a question about the pattern.

## Fix

The `navigating-the-decompile` reference owns what to do about each of these, under "What an empty
grep proves", and it carries the checks a reader runs before writing the word "nothing". Send
anyone who needs the technique there; this file is the incident record and keeps only what the
reference cannot: which claim shipped wrong, and why nothing caught it.

## Prevention

Treat "nothing does X" as a claim needing positive evidence, not as the default when a search is
quiet. `plugins/cs2-modding/AGENTS.md` states the rule for shipped prose — _a grep of `src/` that
comes back empty settles nothing on its own_ — and a reviewer re-deriving claims from primary
sources is what actually enforces it, because the prose gives the reader no signal.

The review gate that produced the reference found two more of these inside the reference itself:
a census whose anchored pattern missed every primary-constructor struct, and a key enumeration
whose character class missed the one key name carrying a digit. Both looked verified because an
earlier pass had run the same pattern and got the same answer.
