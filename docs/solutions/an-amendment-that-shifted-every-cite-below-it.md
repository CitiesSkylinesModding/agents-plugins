---
date: 2026-08-24
area: docs/research (the cs2-modding pipeline's cited stage, against docs/SOURCES.md)
symptoms:
  - 'a research cite names a SOURCES.md line that is blank or carries an unrelated sentence'
  - 'a quote attributed to a line sits one or two lines below it'
  - 'a dated correction re-points a cite past the end of the file'
tags: [citations, line-numbers, sources, amendment, correction]
updated: 2026-08-26
---

# An amendment that shifted every cite below it

## Problem

Research files cite `docs/SOURCES.md` by line number, and SOURCES.md is amended in place whenever a pass finds an entry wrong or incomplete.
An amendment that adds a line silently moves every cite below the insertion: one +1-line amendment to entry 6 broke three cites across two research files, one of them to a line that became whitespace.

## What didn't work

Fixing only the cites the finder happened to name: the same shift had broken cites in a file nobody was reviewing, found only by sweeping every `SOURCES.md:<n>` cite in `docs/research/` against the current file.

## Root cause

The citation convention (`docs/research/README.md`, "A citation on every claim") anchors claims to line numbers in a living sibling file, and nothing re-checks those anchors when the sibling changes.
A reader who opens a stale cite finds unrelated text and either treats a supported claim as unsupported or re-opens a settled amendment — the verified-claims-that-did-not-compose shape, arriving through the file that was supposed to prevent it.

## Fix

After any edit to `docs/SOURCES.md` that changes its line count, sweep every `SOURCES.md:<n>` and `../SOURCES.md:<n>` cite under `docs/research/` and re-point the ones at or below the insertion.
Where the amendment can extend an existing line instead of adding one, do that — the entry-7 amendment landed this way and moved nothing.

## A cite can also be broken by correcting it

Added 2026-08-26. The other way a cite stops resolving is a pass that re-cites a line without opening it.
A review round "corrected" `ComponentSystemBase.cs:449` to `:459` — past the end of a 458-line file — and inverted which overload sits at which line. The original cite was right.

This is worse than drift, because the correction arrives dated and therefore looks newer and better-founded than what it replaced: the next pass trusts it over the working cite and propagates it.

Open the line before re-citing it, exactly as the original derivation had to. A citation is a claim like any other, and a correction to one earns the same opening.

## Prevention

Prefer same-line amendments to SOURCES.md; when a new line is unavoidable, the sweep above is part of the amendment, not a follow-up.
Re-cite only from an opened line, whether the cite is new, drifted, or being corrected.
