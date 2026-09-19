---
date: 2026-09-19
area: plugins/coherent-gameface/mcp/src/selectors.ts (any capability whitelist read off a live probe)
symptoms:
  - 'a probe returns ok and the feature is broken anyway'
  - 'a selector matches far more elements than it should, with no error'
  - "a query for a node that exists returns zero and nothing is raised"
  - 'a whitelist grew after a re-probe and the new members do not work'
tags: [probe, whitelist, capability-detection, silent-wrong-answer, selectors, gameface]
---

# A support set built from what did not throw

## Problem

A version sweep re-probed the Cohtml selector whitelist by asking each construct "does
`document.querySelector` reject it?", and added `::slotted()` and `::part()` to the shipped set
because neither threw. Both are broken: `::slotted(x)` matches every `x` in the document rather than
the slotted ones, and `::part(x)` matches nothing at all, even against a real `part="x"` node.
The set is what the MCP server advertises to a client with no skill loaded, so
`game_click('::slotted(button)')` would have clicked an arbitrary button in a live game UI.

## What didn't work

**Acceptance as the membership test.** `(() => { try { document.querySelector(S); return 'ok' } catch
(e) { return String(e) } })()` is the right probe for "does this throw", and the whole catalogue was
built on it. It cannot see a construct that answers, which is the only kind that fails silently.

**Moving the broken pair to the rejected side.** The diagnosis in `selectors.ts` is
whitelist-complement: any token outside `SUPPORTED_PSEUDOS` is named as the suspect behind a
rejection. Dropping the pair from the whitelist made them suspects, so `div::part(x):not(.a)` blamed
`::part(x)` — the one construct that had not thrown — and sent the caller to delete it.

## Root cause

Three outcomes exist where the probe assumed two: rejected, answered correctly, and **answered
wrongly**. The third belongs to neither set, and an engine that answers wrongly is worse than one
that rejects, because nothing reaches the caller to correct.

Argument shape is part of the answer, not a detail: `::part(x)` is answered while the bare `::part`
and the empty `::part()` are rejected, so a token cleared on its name alone mis-classifies both
spellings.

## Fix

Make membership "answers CORRECTLY", and probe it by comparing against the set the construct should
have matched — `::slotted(i)`'s count against bare `i`'s, `::part(x)`'s against `[part="x"]`'s,
`:host` against a plain element and a real shadow host.

Give the third outcome its own carrier rather than either set
(`selectors.ts` `ANSWERED_NOT_REJECTED`, with `ARGUMENTLESS_ANSWERED` for the members taking no
argument), cleared in the diagnosis so it is never named as a suspect, and absent from the summary
so it is never advertised. Both halves need a test: the exhaustive whitelist tests iterate the
supported set and reach neither.

## Prevention

A capability probe states what it would have returned on an implementation WITHOUT the capability,
before its answer is read as support. Where both answers look alike, the probe settles nothing —
see [`a-live-experiment-that-proved-nothing.md`](a-live-experiment-that-proved-nothing.md).

The rule generalises past selectors: for any whitelist read off a live system, "did not error" is
not evidence of working, and the set that ships to callers is the one that must carry the stronger
claim.
