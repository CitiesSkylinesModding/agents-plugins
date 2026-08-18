---
date: 2026-08-03
area: docs/research (the cs2-modding discovery pipeline, against any decompile or corpus)
symptoms:
  - 'a claim reads "nothing in the game consumes it" and the game consumes it'
  - 'a claim reads "the only system that writes it" and eight systems write it'
  - 'a claim reads "the only repository that does X" and four of them do X'
  - 'a claim reads "every call is an extend or an append" and the most common call is neither'
  - 'a grep of the decompiled source returns no hits for something the game plainly does'
  - 'a claim reads "written once and nothing ever writes it back" and a whole directory writes it'
  - 'a claim reads "has no consumer" and the only consumer is elsewhere in the declaring file'
tags: [research, decompile, grep, over-reach, verification, false-absence, corpus, census]
updated: 2026-08-18
---

# A grep came back empty and the pass concluded absence

## Problem

Shipped claims across the `cs2-modding` prose asserted that something did not exist, or that a
set was complete, each derived from a search whose pattern could not reach what it was claiming
about. All of them were false, and none read as doubtful — they were the most confident
sentences in their sections.

A search's result is evidence about the search. Turning it into a claim about the game, or
about the corpus, needs a separate argument that the pattern could have found the thing.

## What didn't work

**Re-reading the prose.** A false absence is indistinguishable from a true one by inspection:
the sentence is short, flat and plausible either way. Every one survived authoring, the
authoring agent's own re-read, and the orchestrator's end-to-end pass. Only a reviewer told to
re-derive the claim from the source caught them.

**Trusting the citation.** Each claim carried a correct citation. The lines cited said exactly
what the research file reported; the inference drawn from them was the wrong part.

**Re-running the recorded search.** The recipe a research file records is the recipe that
produced the error, so a later sweep reproduces the same blind spot and comes back agreeing.

## Root cause

Each bullet below is a distinct mechanism, which is why fixing one instance taught nothing about the
others.

- **A compile-time `const` is inlined at its call sites.** A name-grep finds the declaration and
  nothing else however many consumers exist. `custom-tools` shipped "nothing in the game consumes
  it, so it exists for the frontend and for mods" about a constant the tool UI system consumes to
  build its snap mask. The same mechanism ran the other way in `environment-and-pollution`: the
  unread-constant ruling's check — grep the name, declaration-only means unread — cleared
  `ClimatePrefab.kYearDuration` as dead while its inlined `12`/`12f` literals run the curve axes;
  the private `kOneOverYearDuration` beside it, equally declaration-only yet visibly live as
  `(1f / 12f)`, is the control that exposed it. The check is sound for `static readonly`, which is
  never inlined; establishing a `const` unread takes hunting the value as a literal too.
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
- **A pattern pinned to one spelling of the receiver.** A sweep for `moduleRegistry.<op>(` across
  the corpus returned zero for the UI module registry's `get` and `find`, and `mod-compatibility`
  shipped "every registry call across the repositories read is an `extend` or an `append`". Reads
  are in fact the registry's most-used operation: the bundle aliases the pair and exports it to
  mods under two different names, and mods reach the backing map through four more receiver names
  again. The call site names the _operation_; the receiver is whatever the caller happened to bind
  it to, so pinning the receiver measures naming conventions rather than behaviour.

- **A pattern requiring the type name and the write verb together.** A sweep for
  `SetComponentData|SetSingleton|AddComponentData` **near each of eleven component type names**
  returned one write each, and `mod-lifecycle-and-ordering` shipped "written exactly once, and
  nothing ever writes it back" with a placement rule resting on it. Eight of the eleven are rebuilt
  on load by `Game.Prefabs.Modes`, where the write reads
  `entityManager.SetComponentData(singletonEntity, componentData)` — the component's type appears on
  the `GetComponentData<T>` line above it and never on the write itself, so the two halves of the
  pattern are true in the same file and never on the same line. A grep for the bare type name over
  that directory finds all of them. This got past the verify stage as well as the finders, because
  the verifier was handed the finder's pattern rather than the finder's question.

- **Every hit landing in the declaring file, read as no hit at all.** A sweep for
  `disallowModifiers` and `ignoreModifiers` across `src/` returned both declarations and one more
  line, and `settings-and-input` shipped "have no C# consumer in `src/Game/`". That third line is
  the consumer: `ProxyBinding.PathEquals` at `ProxyBinding.cs:824` reads `disallowModifiers` to pick
  a path-only comparer, five hundred lines below the declaration at `:314` but in the same file, and
  it decides whether two bindings on one key conflict. The grep was never empty — a hit list whose
  every entry sits in the file that declares the symbol reads as the declaration and its neighbours,
  so the eye stops before opening the second one. A member used only by its own type is the normal
  case for a private helper, which is what makes this shape invisible: absence of *external*
  consumers was measured and absence of *any* consumer was written down.

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

What predicts recurrence is a pattern whose _reach_ was never written down beside its result. Two
instances have already survived a review gate on that basis alone: a census whose anchored pattern
missed every primary-constructor struct, and a key enumeration whose character class missed the one
key name carrying a digit. Both looked verified because an earlier pass had run the same pattern and
got the same answer.

Count where the hits landed, too, not just how many there were: a list confined to the declaring
file is the one shape that looks like proof of absence while holding the disproof.

So record the reach beside the result, and re-sweep by varying the pattern rather than repeating it.
Where a search names a symbol the caller chooses — a receiver, an alias, an import name — it can
only ever bound the claim to that spelling, and the honest sentence says so.

A conjunction is the shape to distrust most: a pattern joining a type name to an operation assumes
they meet on one line, and a read-modify-write puts them three lines apart. Search for each half
separately and intersect the files, rather than the lines. The verify stage is no guardrail against
this one, for the reason the plugin's `AGENTS.md` already gives.
