---
date: 2026-09-18
area: docs/research (conflicts.md above all), docs/solutions — any version sweep or correction pass
symptoms:
  - 'a quotation of another file no longer matches the sentence it quotes'
  - 'a dated gate is credited with a measurement it never took'
  - 'a retraction cites a line the retracted reading was never derived from'
  - "a live-read record names a game version, engine or mod playset that session could not have seen"
  - 'a verbatim log excerpt reports a version no install ever printed'
tags: [prose, sweep, records, provenance, review-gate, cs2-modding]
---

# A sweep that edited the record

## Problem

A game-update sweep re-points every cite and bumps every version stamp it finds. Some of the prose
it finds is not a claim about the game — it is a record of what a past session said, measured or
observed. Editing one forges that record, and the edit reads as diligence. The 1.6.2f1 sweep did it
at least eight times, four of them in `conflicts.md` alone.

## Root cause

The cite step and the stamp step both match on shape — a `file.cs:123`, a `1.6.0f1` — and neither
has any notion of the enclosing block. Every carrier below matched:

- **A verbatim quotation of another file's sentence.** `SystemOrder.cs:315-335` was re-pointed to
  `:316-336` inside quote marks, and a quoted sentence was given an "at 1.6.2f1" clause its source
  never carried.
- **A dated `**Retracted**` paragraph** preserving the retracted text after "As written it claimed:".
  Its cite was moved to where the construct sits today, so the retraction pointed at code the
  retracted reading was never derived from.
- **A dated `**Ruling**`'s rationale**, rewritten from "this pipeline cannot run the game" to a
  reason nobody held on that date — while the sweep's own Addendum twelve lines below said the
  ruling stood exactly as decided.
- **A dated `**Corrected**` record's own arithmetic**: `1,493 + 497 = 1,990` restated to
  `1,511 + 500 = 2,011`, while the struck claim above it still read 1,493.
- **A live-read stamp's session conditions** — the engine version, the loaded mod playset, "with no
  mods loaded", the game mode. A 2026-08-23 record was moved to Cohtml 2.2.1.3, which shipped three
  weeks later, and its playset rewritten from three mods to two.
- **A quoted `SceneFlow.log` excerpt**, restamped to a version and engine that install never printed,
  in a file that twice records the read as 2026-08-05 at 1.6.0f1.

## Fix

Restore what the record said. Where the update genuinely overturned it, append an **Addendum** —
`conflicts.md:28-29` already prescribes exactly that, and the sweep followed it elsewhere in the same
files. Where a frozen cite no longer resolves, annotate outside the quotation rather than moving it
(`AssemblyTypeRegistry.cs:10237ff` at 1.6.0f1, `:10291ff` at 1.6.2f1).

Two shapes are not this defect: a `Rots:` line carrying a deliberately current figure, and a cite
that was simply wrong when written, in a file byte-identical across both versions — that one is a
repair, not a re-point.

## Prevention

Before editing any line, read the block it sits in. A record announces itself: a dated bold lead
(`**Ruled**`, `**Established.**`, `**Verdict**`, `**Retracted**`, `**Corrected**`, `**Withdrawn**`,
`**Sources.**`), a `## Dead ends` heading, quote marks around another file's sentence, a fenced log
or JSON capture, or a session stamp naming a date, a city, a playset or a mode.

`git diff -U25` is the cheap check: it shows the enclosing block for every changed line, and every
one of the eight was visible in it.
