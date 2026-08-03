---
date: 2026-08-03
area: docs/research (the cs2-modding discovery pipeline, against any decompile)
symptoms:
  - 'a claim reads "nothing in the game consumes it" and the game consumes it'
  - 'a claim reads "the only system that writes it" and eight systems write it'
  - 'a grep of the decompiled source returns no hits for something the game plainly does'
tags: [research, decompile, grep, over-reach, verification, false-absence]
---

# A grep came back empty and the pass concluded absence

## Problem

Three shipped claims in the `cs2-modding` references asserted that something did not exist, each
derived from a search that returned nothing. All three were false, and none of them read as
doubtful — they were the most confident sentences in their sections.

A search returning nothing is evidence about the search. Turning it into a claim about the game
needs a separate argument that the search could have found the thing.

## What didn't work

**Re-reading the prose.** A false absence is indistinguishable from a true one by inspection: the
sentence is short, flat and plausible either way. All three survived authoring, the authoring
agent's own re-read, and the orchestrator's end-to-end pass. Only a reviewer told to re-derive the
claim from the source caught them.

**Trusting the citation.** Each claim carried a correct citation. The lines cited said exactly what
the research file reported; the inference drawn from them was the wrong part.

## Root cause

Three distinct mechanisms, which is why fixing one instance taught nothing about the others.

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

## Fix

State the scope in the claim, or widen the search until the claim's scope matches it. Where the
subject can live outside C# — a frontend behaviour, a value in shipped data, an asset flag — the
decompile cannot answer at all and `docs/SOURCES.md` names the source that can.

For a `const` specifically, search for the **value** or for the consuming expression rather than
the name.

## Prevention

Treat "nothing does X" as a claim needing positive evidence, not as the default when a search is
quiet. `plugins/cs2-modding/AGENTS.md` states the rule for shipped prose — _a grep of `src/` that
comes back empty settles nothing on its own_ — and a reviewer re-deriving claims from primary
sources is what actually enforces it, because the prose gives the reader no signal.

The `navigating-the-decompile` reference is scoped to carry all three variants for shipped
readers; until it exists, this file is where they live.
