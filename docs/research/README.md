# The research stage

The first stage of the `cs2-modding` pipeline, and the only one that keeps its citations.
A discovery agent owns one topic across all three sources — the decompiled game, the wiki, the open-source mod corpus — and lands one research file here.
An authoring agent then reads that one file, without the sources, and writes the shipped reference from it.

What lives here:

- `conflicts.md` — every disagreement no agent may settle alone, waiting for the maintainer's ruling.
  Read its open entries before starting a topic they touch, append there rather than deciding, and follow the entry shape it documents.
- `survey-decompile-moddable-surface.md`, `survey-mods-techniques.md`, `survey-wiki-inventory.md` — one orientation survey per source, produced during the interview that became the spec.
  They predate these conventions and carry no per-claim verdicts.
  Read the ones your topic touches before you start: they exist so no topic agent begins cold.
- `<topic-slug>.md` — one file per topic in the approved reference structure, named for the topic its reference will cover.
  Your ticket names the topic, its boundary, and the sources that feed it.

## Findings keep their names

Write findings with their mod names, repository paths and line numbers intact — this stage is where they belong.
A finding is worthless without the citation that proves it, and that citation can never ship: this directory sits outside `plugins/`, so a marketplace install copies none of it.
Taking attribution off is the authoring stage's job, and the only place it happens.

## What a research file carries

One file per topic: the title, the baseline line under it, then `## Findings`, `## Bridge` and `## Dead ends` in that order.

- **A citation on every claim.** `src/<assembly>/<namespace>/<Type>.cs:<line>` for decompiled source, `<checkout>/<path>:<line>` for anything else inside a decompile checkout, `<repo>/<path>:<line>` for a mod, a path from this repository's root for a file in it, `<file>:<line>` for a sibling file here, the full URL for a wiki page.
  Cite a range as `:<first>-<last>` and scattered lines as `:<a>/<b>/<c>`; a claim about a whole file cites the path alone.
  Once a path is cited in full, later mentions may shorten to `<Type>.cs:<line>`, and to a bare `:<line>` only where the full path is the nearest one cited.
  A claim nobody can re-check in a year is a claim the next pass has to rediscover.
- **A baseline**, stated once under the title: always the game version its claims were established against, plus the date the corpus was read and the date the wiki was fetched where those sources were used.
  Every reference derived from your file has to state a version of its own.
- **A verdict wherever the sources disagreed**, opening with `Verdict:` on the claim it settles.
  Name the claim, the source it came from, what the decompile shows, and which wins.
  The decompile is ground truth and wins by default; a verdict going the other way carries the evidence that overturned it.
  Record it even when it is unsurprising — a claim marked verified reads differently from one nobody checked.
  A verdict settles a fact, and only a fact: what no source can settle, the question whose answer changes the product, and the judgement about what ships that can remain even once the facts are in all go to `conflicts.md` instead.
- **What rots**, opening with `Rots:` on the claim it qualifies.
  Flag each claim you judge version-volatile — component and system names, field names, save-format versions, UI module paths, raycast masks — naming what to re-check and the path to re-check it against.
  The author of the reference turns these into its shipped volatility markers, and cannot otherwise tell which claim is architecture and which is a field name that moved.
  Write `Rots:` rather than the shipped marker's own spelling: that marker is greppable as the next version's maintenance checklist precisely because it appears in shipped prose and nowhere else, and a staging file wearing it would put unshipped claims on the list.
- **The bridge.** For a technique topic, the mechanics topics that exercise it; for a mechanics topic, the techniques a change there needs, cited like anything else.
  A file returning only technique or only mechanics has done half the job, and that connection is the thing no other source provides.
- **The dead ends.** What was searched and came back empty, what was ruled out and how.
  A dead end left unwritten is walked again by the next agent at the same cost.

**The corpus reaches a technique topic and a mechanics topic differently.**
`survey-mods-techniques.md` is organised technique-first and carries no mechanics section, and the setup skill's catalog certifies what a mod's source _demonstrates_ — techniques, not mechanics.
So a mechanics topic reads a catalogued mod for the one thing no other source gives: which vanilla systems and components that mod had to disable, fork or query to change the behaviour.
Match on an entry's `Demonstrates` half rather than its `Does` half, as that catalog itself directs, and where no entry's `Demonstrates` names systems in your area, sweep it anyway and record the dead end rather than reaching for a mod whose subject merely sounds close.

Done when each of the three sources has been used, or recorded in `## Dead ends` as checked and empty, or — for the wiki, whose bot challenge often wins — cited through `survey-wiki-inventory.md`'s snapshot with that substitution stated; when every claim the reference will need carries its citation; when `## Bridge` names the other family's material rather than standing empty; and when every disagreement has become a verdict or an entry in `conflicts.md`.
A file that read one source reads exactly like one that swept all three, and the agent writing the reference cannot see what was never looked at.

A survey's or an agent's own recommendation is not a decision: the plugin's `AGENTS.md` governs what ships.
State it as the opinion it is, and leave it out of the verdicts.
