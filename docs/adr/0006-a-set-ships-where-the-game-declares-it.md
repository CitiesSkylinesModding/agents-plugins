---
date: 2026-08-07
status: accepted
area: plugins/cs2-modding
---

# A set ships where the game declares it

## Context

One passage of `ecs-in-this-game` was corrected four times and verified wrong four times. Sorting its claims by outcome draws a clean line. Mechanisms survived every round: that bucket assignment is load-balanced rather than authored, the order of the branches assigning it, which write call each path uses, the offset gate's arithmetic. Closed lists and absence claims broke every round: which families take which bucket count, which prefab classes carry a field, "nothing calls these constants", "there is no other source for the frame index".

The fix that deleted those lists introduced a fresh one in the same edit — _the index is assigned elsewhere, `UpdateGroupSystem` for the simulation families and `ServiceRequestSystem` for service requests_ — which a verifier refuted by naming two more writers. An author who has just been burned by enumeration reaches for enumeration on the next sentence, so the failure is a reflex rather than a knowledge gap, and a rule phrased as advice does not survive contact with it.

The same pass then found the shape in three sentences nothing had questioned across eighteen rounds. `ecs-in-this-game` claimed `UpdateFrame` was the game's only real shared component while its own `VOLATILE:` marker, in the same file, said five. `mod-lifecycle-and-ordering` claimed the vanilla systems spanning phases all branch their interval override, where the branchers are the smallest of three groups and most of the rest override nothing at all.

## Decision

**A set ships where the game declares it, quoted with the pointer to that declaration.** The command-buffer barriers are one class's registrations; the frame-scoped tags are one system's query. Each is a quotation the reader opens rather than a roster this plugin vouches for, and each held through every round.

**A set the author assembled — by sweeping the assembly for everything matching a shape — is a search result.** It ships as its shape, as its complement, or as a derivation on the site the reader is already holding, never as the list. Every casualty above was assembled; every survivor is provable from one site.

The test is already enforced twice elsewhere in the plugin, so this adds no new instrument: a trap carries an openable `Source:` line, and a marker names a location rather than an errand. `the vanilla system-order class` is a location and that claim held. `the implementors across the game assembly` is an errand, and that marker already confesses its set has grown once. **A set whose marker can only name an errand was assembled.**

Three candidate rules were rejected.

_Ship what grep cannot produce_ is the intuitive one and it protects the wrongest category. Grep cannot produce an absence claim either — [a search taken for a census](../solutions/empty-grep-read-as-proof-of-absence.md) is that argument in full — so the rule licenses exactly the sentences that broke. It also misstates why the reader wins: not that their grep beats the author's sweep, but that they hold one element and need only membership, which is a bounded read.

_Menu versus census_ — a list the reader picks from against one they check membership against — classifies by reader posture, which is not a property of the list. The barrier table is a menu to a reader choosing one and a membership check to a forker arriving with `ModificationBarrier4B` from decompiled source. Two authors imagining different readers classify the same list differently, which is the appeal the rule exists to remove.

Shipping the sweep as a recipe was rejected for the same reason as the first: it re-ships the blind spot as an instruction, and a shipped search passes untouched through a pass that re-derives claims.

## Consequences

This folds into the existing count bullet as its set-shaped half rather than standing as its own rule. A count and a set are one claim in two forms, and four overlapping instruments would leave an author reconciling them per sentence.

The ground is the asymmetry between confirming a member and establishing a boundary — bounded either way for the first, unbounded for both parties on the second — rather than rot rate. Rot legibility falls out of it: a declared set's rot has an address the next sweep opens, while an assembled set gains its member at a site no marker names.

A shape claim survives by its complement. "Zero game types implement `ICleanupComponentData`" is an absence over a whole assembly, but the load-bearing half is "the game's cleanup idiom is `Deleted` plus a frame of grace", which has a declaration site — and rounding buys what precision costs, since five becoming six does not falsify _a handful_.

**This does not fix the mechanics prose budget**, and the hypothesis that it would was wrong. ADR 0005's measurement found names, field names, tables and formulas came through that review nearly untouched; what consumed the budget was mechanism spanning three or more source files, which the shape already discloses into pseudo-code listings, and listings sit in fences the prose-line count excludes. Two correct rules aimed at two different failures.

Detection was never the bottleneck: these sets were caught every round, and authoring could not fix them. What worked is the rule this plugin already had — a claim wrong twice is deleted rather than rephrased. The verification lever added beside this record earns its place differently: asked to construct the member outside a closed list, a verifier returns the mechanism that put one there, which is content worth shipping in the list's place.
