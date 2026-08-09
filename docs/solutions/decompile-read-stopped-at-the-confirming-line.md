---
date: 2026-08-04
updated: 2026-08-09
area: docs/research (the cs2-modding discovery pipeline, against any decompile)
symptoms:
  - 'a correction lands, and the corrected sentence is wrong again for the same reason'
  - 'a citation range ends one line before the code that overturns it'
  - 'a citation range contains the code that overturns the claim it supports'
  - 'a claim about what a method does, derived from the half of it that was read'
tags: [research, decompile, verification, over-reach, correction, confirmation]
---

# A derivation stopped at the line that agreed with it

## Problem

One claim about the game's `coui://` resource hosts was corrected three times, and the first two
corrections were as wrong as what they replaced. Each pass read the decompile until it found a line
confirming what it already believed and stopped there. Each time the deciding code was a few lines
further on, in the same method or the next call up.

Nothing about the resulting prose read as doubtful. A claim derived from real code, carrying a real
citation, reads exactly like one derived from all of it.

## What didn't work

**Correcting the sentence.** Round one replaced a false claim with a differently false one. Round
two narrowed that and was still wrong. The sentence was never the problem, so editing it never fixed
anything.

**Dismissing the contrary report.** A mod in the corpus carried a source comment describing the true
behaviour, and round one recorded it as a mod author's mistaken belief on the strength of a partial
read. The corpus settles nothing about the game, which is why the comment could not be the warrant —
but it was a lead, and a lead that contradicts your derivation is the one to chase rather than
explain away.

## Root cause

Three stops, each on a line that confirmed the belief.

- **The rest of the method.** `UILiveReload.Update`'s media branch calls
  `uiSystem.ClearCachedUnusedImages()`, which reads as "only unused images are dropped". Four lines
  earlier the same branch does `UpdateURL(m_Target.liveReloadUrl)` — navigating the view to
  `assetdb://gameui/Static/reload.html` — and after the clear it navigates back
  (`src/Colossal.UI/Colossal.UI/UILiveReload.cs:337`, `:341-343`, `:367`). The document is gone
  before the clear runs, so "unused" spares nothing and the whole UI reloads.
- **The next line.** `UIManager` sets `liveReload = developerMode`
  (`src/Colossal.UI/Colossal.UI/UIManager.cs:189`), which settles the question if you stop reading
  at `:189`. `:191` is `AssetDatabase.global.LoadSettings("UIManager Settings", …)`, which writes
  over it from a stored asset. The claim shipped citing `:177-189`.
- **The caller.** `RequestResourceAsync` walks the host's paths and caches nothing, so "every
  request reads the file off disk again" follows — from the wrong entry point.
  `TryPreloadedResourceRequestAsync` runs first and resolves a raster image against
  `UI/SharedImages` before any host is consulted
  (`src/Colossal.UI/Colossal.UI/DefaultResourceHandler.cs:428`, `:650-690`).

## Fix

Read past the confirmation: the rest of the method, the caller that reaches it, and the lines
immediately after the one you cited. A range that ends exactly where the claim is satisfied is the
shape to distrust in your own citations.

## The counting variant

The same stop produces a wrong **census**, and it reads even better than a wrong claim does.

A registration list, an enum or a family of systems usually opens with a contiguous block that
explains the pattern — the six spatial search systems and the pathfinding pair that open the
`PreDeserialize` registrations, say. That block answers _what is this for_, so the read ends there
and the count ends with it. The `mod-lifecycle-and-ordering` reference shipped "nine
`PreDeserialize<T>`" off exactly this; there are 57, and the nine are the leading block.

**A count is a claim about a whole span, so derive it from the span.** `grep -c` over the file
beats reading until the pattern is clear, and the count and the illustration are two separate
claims: give the illustration its own narrow range and the count its own wide one.

The over-correction to expect here is characterising the rest of the span from the tail you
happened to read. The same reference then shipped "the remaining 48 are UI, infoview and rendering
systems" — audio, tool, pathfinding, buffer and tutorial systems are in there too.

## The verifier variant

A verifier asked to confirm a claim performs the same stop on your behalf: it finds the supporting
line and returns CONFIRMED. A review pass verified "`Criminal` is active only while
`m_Event != Entity.Null`" that way; the claim was wrong for the whole sentenced-to-released stretch
(`CriminalSystem.cs:262` nulls `m_Event` at sentencing), caught only when a later verifier was asked
to trace the full state machine and report each field's value at every stage. Brief a verifier on
the mechanism's trace, never on the claim's confirmation.

Four moves make that briefing hold. **Bar the verifier from the prose under test**, so it cannot
cite the thing it is checking. **Order the work**: derive from the source first, compare with the
claim second. **Say that a disagreement is a useful result**, or the agent reads its own contrary
derivation as its own failure. **Demand the negatives by name** — every call site, every writer,
every consumer — because proving one forces the enumeration that a confirming read never performs.
A fan-out briefed this way over eight shipped reference claims came back disagreeing on four, three
of them a right conclusion resting on a wrong mechanism: the shape a confirmation pass returns
CONFIRMED on every time.

## The mid-range variant

The stop can happen inside the cited range itself, which defeats the wide-range habit below. The
`debug-menu` pass derived "a same-named `[DebugTab]` appends to the vanilla tab" from
`DebugManager.GetPanel` returning the existing panel, and cited `DebugSystem.AddPanel` at
`:1085-1095` — a range containing the three lines that overturn the claim:
`m_Panels.TryGetValue(name, out var value)` … `panel?.children.Remove(value)`
(`src/Game/Game.Debug/DebugSystem.cs:1088-1093`) remove the previously registered widget list, so
an exact-name collision replaces and only a case-varied name appends. The route shipped as
sanctioned would have wiped the vanilla Gizmos tab. A wide citation reads as diligence and proves
nothing about the reading: the range is evidence only when every line inside it was traced.

## Prevention

Two habits, both cheap.

**Cite a range that starts before the claim and ends after it.** The reviewer who catches this
catches it by reading around your citation, so give them the surrounding lines rather than the
excerpt that proves you right.

**Chase the contrary lead first.** A mod, a wiki page or a comment saying something your derivation
denies is the cheapest signal you have that the derivation is short. Chase it before writing,
whatever the source's standing. `docs/SOURCES.md` decides what may _settle_ a claim, not what may
_prompt_ one — its entry 11 now says so.

A correction is new prose and earns another `/review-gate` on the same terms as anything else. Three
rounds here each found real defects in the passage the round before had just rewritten.
