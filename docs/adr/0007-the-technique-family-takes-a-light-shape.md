---
date: 2026-08-09
status: accepted
area: plugins/cs2-modding
---

# The technique family takes a light shape of its own

## Context

[ADR 0005](0005-every-reference-is-read-beside-the-decompile.md) retired self-sufficiency for every reference and gave the mechanics family a fixed form, leaving the technique family with the decompile warning and no shape of its own — no sections, no budget, no trap format. The roadmap held that question open until one mechanics topic had shipped under the new form, and prescribed measuring the family before assuming the mechanics diagnosis transferred.

The measurement (2026-08-09, all 31 shipped technique files, using the lint's own prose-line definition) came back against the transfer. Decompile-restating narration is roughly a sixth of the family's prose, and it almost never stands free: it appears as a premise welded to a consequence the reader acts on. The bulk is procedure, traps and API contract that no game file states — the order to try alternatives in, the version-int discipline, the escape routes from a raycast mask that cannot see mod-owned entities. Entry files run up to 291 prose lines against the mechanics family's 100-line ceiling, and every sibling sits under 60. The mechanics diagnosis — transcription of code the reader holds — describes a family whose topics all do the same job over game code; technique topics do different jobs over material that exists nowhere else, so importing that form would delete the product.

Two findings did indict the family. Not one `Source:` line exists across its 31 files, against the mechanics average of five per file — so exactly the fraction of its prose that is a claim about game code, including its highest-consequence traps, has nothing a fact-checking pass can open. And its entry files carry self-contained censuses and engine walks that the folder-per-topic disclosure mechanism was built to hold as siblings.

## Decision

**The technique family keeps its per-topic sections and gains four rules**, stated in [the technique reference shape](../authoring/technique-reference-shape.md): every trap resting on game behaviour carries an openable `Source:` line, with `UNVERIFIED:` standing in where only a running game proves the claim; a self-contained account a reader consults rather than reads through discloses into a sibling; a prose budget of three hundred warn and four hundred fail, enforced by the content lint; and the mechanical pass — someone other than the author opens every `Source:` line before the prose review. The shape fixes no section list; beyond the header block every reference already carries, its one fixed piece is the `## What this reference hands to others` closer on entry files.

Declined, with the measurement as the reason: fixed sections, because the family's topics are heterogeneous jobs and their strength is the premise-consequence interleaving a section list would break apart; a budget tight enough to force cuts, because it would indict content the measurement cleared; and consolidating the family's per-claim `VOLATILE:` markers to the mechanics per-file form, because a technique file names symbols from many places that rot at different rates.

A trap about mod-to-mod behaviour anchors to the mediating game surface — the shared host, the delegate field both mods write — which is game code even when the collision is between mods. A claim about patching anchors to Harmony's own source, the one nameable library.

## Consequences

At this ruling the shipped family did not conform: no `Source:` lines anywhere; four measured disclosure candidates sitting in entry files (the settings reflection-engine walk and page-shape sections, the diagnostics loader-state and failure-notification pair, the prefab-data initialisation account, the placement consumer-build account); and three entry files — `mod-compatibility`, `patching`, `performance-and-memory` — closing without the `## What this reference hands to others` section.

Riding the conforming on the standing resweep passes was considered and dropped — together they open barely half the family's topics — so a dedicated conforming pass runs after them, consuming the research files' citations for its anchors, and edits those passes make in the meantime are written under this shape from the start.

## Amended after the conforming pass

That pass settled the docket: three of the four candidates disclosed, and **the placement consumer-build account was measured and deliberately kept inline** — short, and welded to the window argument the rest of that file turns on, which is the premise-consequence interleaving this record defends. It is not outstanding work. The pass also disclosed siblings the docket did not name. Two of those folders had crossed the warn line under their new anchors alone; `performance-and-memory` had crossed it on prose alone, in the resweeps that ran between this ruling and the conforming pass, so its two siblings answer that growth rather than anything the anchoring added.

It also exposed one thing this budget got wrong. The thresholds above were measured over a technique family with no `Source:` lines, and the shape then mandated one anchor per trap — so counting anchors as prose measured anchor density against a baseline containing none, and a file could cross the warn line having grown by anchors alone. **For the technique family only, the lint now excludes `Source:` lines from the count**, for the reason it already excluded headings and table rows: the budget guards what an author over-produces, and an anchor is mandated rather than chosen. The mechanics thresholds are untouched, and deliberately: they were asserted rather than measured — fixed before that family had a single shipped file — so there is no measurement to correct there, and loosening a number nobody derived would only hide growth. The technique numbers themselves are unchanged, so the measurement behind them still holds.

The UI skill's references, unwritten at this ruling, take neither family's shape by default: their frontend half anchors to a shipped JavaScript bundle rather than to C#, a different verification economy, and they get their own cheap ruling when the first of them is authored.

The settlement's home outlives the run that produced it. The two shape docs and the reference-ticket protocol now live in `docs/authoring/`, tracked, so the update era — re-verifying references against new game versions — runs from a standing contract rather than from a second spec.
