# The research stage

The first stage of the `cs2-modding` pipeline, and the only one that keeps its citations.
A discovery agent owns one topic across the sources in [`../SOURCES.md`](../SOURCES.md) and lands one research file here.
An authoring agent then reads that one file, without the sources, and writes the shipped reference from it.

**Read `../SOURCES.md` before you start.**
It names every source, what each is authoritative for and how to reach it, and it carries the precedence rule your verdicts turn on.

**Before recording a claim as unanswerable, name the artifact that would answer it and say whether you opened it.**
That sentence is the whole of what this stage has learned the hard way: a claim shipped as unprovable was one grep away in a file nobody had opened.
Where the artifact is the running game, ask the user to start it — a background agent can ask mid-topic, get the answer back, and carry on with its context intact.
Where the answer arrives after the agent has finished, resume it by name rather than starting a fresh pass; a resumed agent still holds everything it read.

What lives here:

- `conflicts.md` — every disagreement no agent may settle alone, waiting for the maintainer's ruling.
  Read the entries naming your topic before you start, ruled as well as open, append there rather than deciding, and follow the entry shape it documents.
- `survey-decompile-moddable-surface.md`, `survey-mods-techniques.md`, `survey-wiki-inventory.md` — one orientation survey per source, produced during the interview that became the spec.
  They predate these conventions and carry no per-claim verdicts.
  Read the ones your topic touches before you start: they exist so no topic agent begins cold.
- `<topic-slug>.md` — one file per topic in the approved reference structure, named for the topic its reference will cover.
  Your ticket names the topic, its boundary, and the sources that feed it.
- `method-<slug>.md` — how a source was obtained, where obtaining it was itself an investigation.
  No topic owns one and no authoring agent is pointed at one: they exist so a derivation that lives nowhere else survives the scratch folder it was done in.
  A topic file cites its method file like any other sibling here.
  One exists: [`method-decoding-shipped-locale-data.md`](method-decoding-shipped-locale-data.md), which gets the vanilla strings out of the game's compiled `.loc` assets. Read it before decoding one yourself — it carries two traps, including the locale that ships loose beside the package and is silently missed by reading the package alone.

## Findings keep their names

Write findings with their mod names, repository paths and line numbers intact — this stage is where they belong.
A finding is worthless without the citation that proves it, and that citation can never ship: this directory sits outside `plugins/`, so a marketplace install copies none of it.
Taking attribution off is the authoring stage's job, and the only place it happens.

## What a research file carries

One file per topic: the title, the baseline line under it, then `## Findings`, `## Bridge` and `## Dead ends` in that order.

- **A citation on every claim.** `src/<assembly>/<namespace>/<Type>.cs:<line>` for decompiled source, `<checkout>/<path>:<line>` for anything else inside a decompile checkout, `<repo>/<path>:<line>` for a mod, an install-relative path for a file in the installed game, `<package>/<path>:<line>` for a file in an installed toolchain package, a path from this repository's root for a file in it, `<file>:<line>` for a sibling file here, the full URL for a wiki page.
  Cite a range as `:<first>-<last>` and scattered lines as `:<a>/<b>/<c>`; a claim about a whole file cites the path alone.
  Where a source has to be transformed before it can be read at all, cite the transformed copy for its line numbers, and state in the baseline both what produced it and the line count it came to — that count is the only way a later reader can tell whether their own copy and your citations still agree.
  Once a path is cited in full, later mentions may shorten to `<Type>.cs:<line>`, and to a bare `:<line>` only where the full path is the nearest one cited.
  A claim nobody can re-check in a year is a claim the next pass has to rediscover.
- **A baseline**, stated once under the title: always the game version its claims were established against, plus the date the corpus was read and the date the wiki was fetched where those sources were used.
  Every reference derived from your file has to state a version of its own.
- **A verdict wherever the sources disagreed**, opening with `Verdict:` on the claim it settles.
  Name the claim, the source it came from, what the authoritative source shows, and which wins.
  The source `../SOURCES.md` names as authoritative for the claim wins by default; a verdict going the other way carries the evidence that overturned it.
  Record it even when it is unsurprising — a claim marked verified reads differently from one nobody checked.
  A verdict settles a fact, and only a fact: what no source can settle, the question whose answer changes the product, and the judgement about what ships that can remain even once the facts are in all go to `conflicts.md` instead.
- **A ruling, wherever one came back**, opening with `**Ruled (<date>, <where it was made>; conflicts.md).**` on the finding it governs.
  The maintainer writes it, not you: your part is the evidence they rule on.
  It restates the decision and what the reference owes because of it, rather than pointing back at the entry, since the authoring agent reads this file and never `conflicts.md`.
  It sits at the finding rather than in a section of its own, so the passage it governs cannot be authored without it.
- **What rots**, opening with `Rots:` on the claim it qualifies.
  Flag each claim you judge version-volatile — component and system names, field names, save-format versions, UI module paths, raycast masks — naming what to re-check and the path to re-check it against.
  The author of the reference turns these into its shipped volatility markers, and cannot otherwise tell which claim is architecture and which is a field name that moved.
  Write `Rots:` rather than the shipped marker's own spelling: that marker is greppable as the next version's maintenance checklist precisely because it appears in shipped prose and nowhere else, and a staging file wearing it would put unshipped claims on the list.
- **What was reached and not confirmed**, opening with `Unconfirmed:` on the claim it qualifies.
  Name what would settle it — the artifact to open, or the experiment to run against the running game — so the reference's author can turn it into a shipped `UNVERIFIED:` marker rather than inventing a hedge.
  The same spelling rule applies for the same reason: the shipped token greps into the list of claims a maintainer can still close, and a staging file wearing it would put unshipped claims on that list.
  This is not the same as a dead end. A dead end is a road that was walked and went nowhere; this is a claim the reference will make, carrying the limit of its evidence.
- **The bridge.** For a technique topic, the mechanics topics that exercise it; for a mechanics topic, the techniques a change there needs, cited like anything else.
  A file returning only technique or only mechanics has done half the job, and that connection is the thing no other source provides.
- **The dead ends.** What was searched and came back empty, what was ruled out and how.
  A dead end left unwritten is walked again by the next agent at the same cost.

**The corpus reaches a technique topic and a mechanics topic differently.**
`survey-mods-techniques.md` is organised technique-first and carries no mechanics section, and the setup skill's catalog certifies what a mod's source _demonstrates_ — techniques, not mechanics.
So a mechanics topic reads a catalogued mod for the one thing no other source gives: which vanilla systems and components that mod had to disable, fork or query to change the behaviour.
Match on an entry's `Demonstrates` half rather than its `Does` half, as that catalog itself directs, and where no entry's `Demonstrates` names systems in your area, sweep it anyway and record the dead end rather than reaching for a mod whose subject merely sounds close.

Done when each source bearing on the topic has been used, or recorded in `## Dead ends` as checked and empty, or — for the wiki, whose bot challenge often wins — cited through `survey-wiki-inventory.md`'s snapshot with that substitution stated; when every claim the reference will need carries its citation; when `## Bridge` names the other family's material rather than standing empty; and when every disagreement has become a verdict or an entry in `conflicts.md`.
A file that read one source reads exactly like one that swept them all, and the agent writing the reference cannot see what was never looked at.

A survey's or an agent's own recommendation is not a decision: the plugin's `AGENTS.md` governs what ships.
State it as the opinion it is, and leave it out of the verdicts.

## The boundary travels with the file

The authoring agent reads one research file and no source, which is what keeps a mod name out of shipped prose.
That leaves it without the one thing a research file never states: where the topic stops.

So hand it the boundary and the questions alongside the file, and judge the shipped reference against them rather than against the file alone.
A file records what was found, so an agent holding it by itself picks its own scope — it writes into a neighbouring reference's territory, or drops a question the file answered in passing.
Nothing downstream catches either: the content lint checks mod names, the version baseline, the marker spelling and that links resolve, and none of those is coverage.
