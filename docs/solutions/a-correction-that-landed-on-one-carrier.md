---
date: 2026-08-26
area: plugins/*/skills, docs/research and shipped plugin metadata (any correction sweep)
symptoms:
  - 'a section heading asserts what its own body denies three lines below'
  - 'a claim is corrected and the next round finds the same claim standing elsewhere'
  - 'a dated correction note itself states what the correction retired'
  - 'a feature ships and the marketplace or package description still sells the product without it'
  - 'a claim is corrected and the replacement turns out to be false in its own right'
tags: [prose, correction, sweep, carriers, review-gate]
updated: 2026-09-19
---

# A correction that landed on one carrier

## Problem

Across a seven-round review loop, the dominant defect was never a wrong fix — it was a right fix applied to one statement of a claim while another statement of the same claim stood untouched.
Four of the seven rounds were mostly this, and the certifying pass still found more.

## What didn't work

Sweeping by subject rather than by the retired phrasing, which the root `CLAUDE.md` already requires.
It is necessary and it was being done. It still missed carriers, because a subject sweep greps prose and these carriers are not prose.

## Root cause

A claim lives in more places than its sentence, and the other places do not read like the sentence:

- **The section heading** whose body you just corrected. Fixed twice in this loop, missed twice.
- **A forward reference earlier in the same file** — "the sync that completes every job in the world, whose section below owns that cost."
- **A table cell** stating the claim in five words.
- **A hand-incremented count** elsewhere in the section: "Two more escape both" after a third was added.
- **The bridge paragraph in the matching `docs/research/` file**, which is what the next authoring pass runs on.
- **A supersede note that itself asserts what the correction overturned** — appending a note is not editing the original, and the note is new prose that can be wrong in its own right.
- **A pointer's promise**: "`performance-and-memory` owns that trade in full" is a claim about another file's contents.
- **The site a convention file names as the authority for the mechanism.** `AGENTS.md` says the bound lives where the wait is and points at `Invoker`; a later loop corrected the session's docblock, its refusal message and that `AGENTS.md` line, and left `Invoker`'s own docblock still claiming the invoke path was bounded. You correct OUTWARD from the authority site, because it is the one you trust, so it is the one you never re-read.
- **An ADR's present-tense aside.** The record reads frozen, so a sweep skips it, but the sentence around the decision is live: "the server-lifetime watchdogs still are" outlived the change that ported them.
- **A badge.** `platform-Windows` is a claim inside a URL, and no prose grep reaches it.
- **The checklist that enumerates the carriers.** A sweep skill's step, or an `AGENTS.md` invariant, listing the sites a re-probe must edit is itself a site the correction lands on. It fails in both directions: a round that adds a carrier and not the checklist leaves the next sweep blind to it, and a round that REMOVES a carrier leaves the checklist pointing at a site that no longer says anything.
- **A capability sentence in shipped metadata**, where adding a tool restates it ten times: both harness `plugin.json` files, both marketplace files, the package project's `<Description>` AND its own header comment, the plugin README's headline and tool table, the ROOT README's row for that plugin, and the roadmap's section intro for it. `check:plugin-sync` compares two of the ten, so a sweep that stops at prose leaves every install-decision surface selling the plugin without the thing it just gained.

## Fix

After a correction lands, before the round closes, re-read the *whole section* — not the hunk — and check the heading, any table, any count, and every pointer into or out of it. Then open the matching research file and correct the derivation, not just the prose it produced.

## Prevention

Treat a correction as landing on a *set* of carriers and enumerate the set before editing any of it.
The check that catches the most for the least: read the corrected passage top to bottom as a stranger, because a heading contradicting its body is invisible in a diff and obvious in a read.

Where a correction adds or removes a carrier, edit the checklist that enumerates them in the same pass, and record why a site is deliberately NOT a carrier — a bare absence reads as an oversight, and the next sweep re-adds it on the same reasoning that put it there the first time.

Where a claim spans two references, correct both in the pass that found it — deferring drops it, and the two then disagree in the tree until someone notices.

Verify the REPLACEMENT, not just the claim being retired. A comment reading "NOT_SUSPENDED is a post-attach transient" was corrected to "retried like every other wire operation", which is also false, and `sdb-round-trips-are-not-equal-cost.md` had already settled that subject under its own what-didn't-work. The replacement is new material: search the store for the doc that owns the subject before writing it, and hold it to the evidence the original would have needed.
