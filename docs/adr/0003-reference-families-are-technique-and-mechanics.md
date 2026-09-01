---
date: 2026-08-02
status: accepted
area: plugins/cs2-modding
---

# Name the two reference families technique and mechanics, one directory each

## Context

The `cs2-modding` plugin nests its references under the trunk skill in two families, because its three sources decompose along two orthogonal axes and both are real: one family teaches mechanism reusable across subject matter, the other teaches what the game simulates and which components carry it.

The spec named the second family "domain", and the word turned out to be load-bearing in more places than a planning document: shipped prose an agent loads and acts on, the plugin description that sits in context every turn, the research-stage conventions every discovery agent satisfies, and the running conflicts file. Renaming it is cheap once and expensive later, so the structure gate was the moment to settle it.

Four alternatives were live.

- **"domain"**, as the spec had it. Accurate but jargon: it says nothing to a reader who does not already hold the split, and the plugin's whole purpose is readers who do not.
- **"simulation"**, the wiki's own framing, since the family axis was adapted from that wiki's simulation-dimension taxonomy. Two costs: it makes the family's own definition a tautology — _simulation references teach what the game simulates_ — and `references/simulation/` collides with `Game.Simulation`, a namespace an agent greps daily, and with `simulation-time-and-units`, one of the family's own references.
- **"gameplay"**, raised during review on the grounds that it leaves "mechanism" free.
- **A flat `references/` directory** with the family carried in filenames, rather than a directory per family.

One constraint cut across all of them: the technique family's own definition uses "mechanism" as its term of art, so any name near that word buys ambiguity.

## Decision

The families are **technique** and **mechanics**, in `skills/cs2-modding/references/technique/` and `skills/cs2-modding/references/mechanics/`, and only the trunk skill splits this way — every other skill keeps a flat `references/`.

"Mechanics" is the word a modder already brings with them, which is exactly what "domain" was not, and it carries both halves of the family's job: what the game models, and the code implementing it. Its near-collision with "mechanism" is bounded rather than absent — each family bullet is labelled by its own bolded name, and the boundary test that decides where a fact goes ("_how do I do this at all_ is technique, _what does the game model here and where does it live_ is mechanics") uses neither word ambiguously. Where the collision did bite, in a research-stage completion criterion that had contrasted "only mechanism or only mechanics", the sentence was reworded rather than the name changed.

"gameplay" would also have worked and was not taken; the choice between the two is taste, and "mechanics" reads better against "technique".

A directory per family beat the flat alternative because the family becomes visible in every path, and because the content lint already treats any file below a `references/` directory as a reference at any depth, so the extra level costs nothing.

## Consequences

The boundary test reads plainly to someone meeting the plugin for the first time, and the two directories give the reference fan-out an index it did not have.

The plugin's technique family defines itself with a word two letters from the other family's name. That is a permanent low-grade ambiguity, accepted because every site that states it also labels it.

The word is load-bearing in four places that have to move together, and a future rename pays the same bill: the plugin's `AGENTS.md`, the research-stage conventions in `docs/research/README.md`, `docs/research/conflicts.md`, and the four manifest descriptions — both `plugin.json` files and both marketplace files, which `mise check:plugin-sync` holds identical. The manifests are the easiest of the four to miss and the only one a user ever reads.

## Addendum (2026-09-01): how a boundary earns a reference, and the merges declined

The structure gate that named the families also fixed the reference count; the working document carrying that decision was deleted with the feature's scratch folder, so the record moves here.

A boundary earns a reference when the three seed surveys agree on it: the decompile's namespace map fixes where the code lives, the wiki's resolved hubs fix what the game models, and the corpus's technique map fixes which mechanisms mods actually reach for. Where only one source sees a split, the split is not real enough to spend a reference on.

The count was reviewed and kept whole; no merge was taken. The three candidates were the cheapest reversals available, recorded so a later session hitting the fan-out cost knows they were considered and declined rather than missed:

1. `placement-definitions` into `custom-tools` — one large tool reference, at the cost of making an agent that only wants to change placement read the tool contract first.
2. `utilities-and-flow-networks` into `city-services-and-coverage` — restores the wiki's own concentration point, at the cost of one reference carrying 12 of the 33 info views.
3. `mod-compatibility` dissolved into `patching`, `save-serialization` and `custom-tools` — cheapest to reverse later, most likely to leave the material scattered.
