---
date: 2026-08-07
status: accepted
area: plugins/cs2-modding
---

# Every reference is read beside the decompile

## Context

Every reference this plugin ships was held to one rule: an agent with no local decompile, no running game and no network still gets correct answers. It was written for the technique family, where it costs nothing — how to register a system or patch a method is in no game file, so a technique reference is the only place that answer exists.

The first mechanics topic is where it came due. What a mechanics reference teaches is what the game does, and the game's code says that better than prose about it. Held to self-sufficiency the reference has to restate the behaviour, and ADR 0004 had already removed the numbers, which were the other half of what it could carry. The ticket governing it said the opposite — leave the branches to the agent that reads the decompile — and lost, because the rule it contradicted outranked it.

What shipped was a 491-line entry file that eighteen `/review-gate` rounds did not converge. Almost every finding hit one kind of sentence: a summary of control flow across its branches, which had dropped a branch. Names, field names, tables and formulas came through nearly untouched.

A measurement then ruled out the obvious cure. Classifying all 799 lines of that topic's five files found 42 lines of explaining prose — the content was not transcription, and deleting a category of sentence would have recovered five percent. What the entry file actually held was ten mechanisms each spanning three or more source files, and no ceiling on its own length.

Carving only the mechanics family out of the rule was tried first and does not hold. `ecs-in-this-game` and `mod-lifecycle-and-ordering` both gained decompile-resident mechanism written out longhand in the same pass — how `UpdateFrame`'s bucket index is computed, which group counts the dispatch systems use, what the update system does with an explicit offset. Neither is a technique. `navigating-the-decompile` sharpens it: a technique reference whose entire subject is a source tree the rule made it assume the reader lacks.

## Decision

**Every reference in this plugin is read beside the decompile.** The self-sufficiency rule is retired rather than re-cut, and the line stating it leaves `plugins/cs2-modding/AGENTS.md`.

Restating it as a condition — _a reference may assume the decompile where the answer is in the decompile_ — would have been truer than a family seam and wrong in the same way, since it makes each sentence a judgement about where its answer lives, and that judgement had already gone wrong twice. What replaces the rule is not a licence to write less but a licence to point instead of transcribe.

**A mechanics reference goes further and takes a fixed form**, which [the mechanics reference shape](../authoring/mechanics-reference-shape.md) carries: a sketch, a map of concept to component and field, formulas, traps with an openable `Source:` line, pseudo-code listings disclosed into siblings, bridges, and a prose-line budget the content lint enforces. That form is the mechanics family's alone. A technique reference may now route a reader to the code rather than restating it; it does not thereby become a map.

The reference warns rather than gates. A block under the version baseline tells a reader without the source what that file in particular loses, and where to get it; the file still answers, and it is the only place several of these facts exist. The decompile is that source for almost every reference, but not for all of them — a file whose claims rest on the game's shipped data or on its install rather than on C# opens on what it actually needs, since sending that reader to provision a tree answering none of their questions costs them minutes and teaches them nothing.

## Consequences

The budget is the part no review had, since nothing in those eighteen rounds ever asked whether the file was too long.

The authoring stage changes with it. An agent writing pointers into code it cannot open compresses a research file that already compressed the code — a second cause of the dropped branches, independent of the first. A mechanics authoring agent reads the decompiled game and not the mod corpus, so the gate that keeps mod names out of shipped prose costs nothing and the second compression goes.

Every trunk reference carries the warning, technique and mechanics alike, and they differ in the line between its fixed first and last. That line is written per file rather than per family: what a reader loses by arriving without the source is a property of the file, and a sentence borrowed from a neighbour ends up contradicting its own body. The plugin's `AGENTS.md` states the cases, and the content lint holds the fixed lines around them as matched opener-and-closer pairs, so the accepted wordings live in one place a author can open.

The first mechanics topic's five files were deleted rather than stripped: none of their traps carried an openable source, and one was false. It is re-authored from its research file, which stands.

A record that replaces a rule amends every site still instructing under the old one, and those sites are never only the tickets. This one's sweep reached the mechanics tickets' depth paragraphs and their balance-data criteria, the approved reference structure, both stages of the shared authoring prompt, and a research file whose own reasoning rested on the retired rule. Scoping that sweep to the _unstarted_ tickets was the mistake worth recording: the ticket about to run is neither unstarted nor finished, and it is the only one whose amendment could not wait.

The honest cost: a reader with no decompile gets less than the old rule promised. That promise was being kept by a file nothing could verify.
