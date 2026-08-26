---
date: 2026-08-26
area: docs/research (the cs2-modding pipeline) and any claim settled against the running game
symptoms:
  - 'a claim ships behind UNVERIFIED: while the experiment that would settle it is two calls'
  - 'ecs_query returns hundreds of entities and every buffer read off them comes back length 0'
  - 'a grep for a field of some type returns types carrying no such field'
tags: [research, verification, runtime, unity-devtools, ecs, unverified-marker, experiment]
---

# A live read with no subject to read

## Problem

`mod-compatibility.md` shipped `UNVERIFIED:` on whether `EntityManager.Debug.GetComponentBoxed` refuses to pin a component the runtime treats as non-blittable.
The marker named its own settling experiment — "one live read through the debug proxy" — and survived a seven-round review gate regardless.
Once the game was up, two `eval` calls answered it. The six greps and four dead probes that found something to read took an order of magnitude longer.

## What didn't work

**Waiting to be told the game was running.** No pass checked. `mcp__unity__status` answers in one call, reporting whether the beacon is up and the debugger enabled.

**Grepping the decompile for a field by its type.** `grep "public bool "` returns expression-bodied properties — `public bool waterConnected => (m_Flags & …) != 0` — on types carrying no `bool` field at all. Two candidates were read and discarded before the pattern gained its semicolon: `public bool m_[A-Za-z]*;`.

**Assuming a live entity's buffer holds elements.** `ecs_query` matches on component presence, so it lists an entity whose `DynamicBuffer` is empty exactly like the rest. `Game.Rendering.Emissive` (301 entities), `Game.Net.ArrowPosition` (34) and `Game.Rendering.Skeleton` (156) all read back length 0 before `Game.Net.LabelPosition` came back with 51.

## Root cause

A live read reaches a code path only through data the game already carries.
The decompile names every type that would exercise the path; the running world holds a small and unpredictable subset of them, and nothing connects the two — so "run the experiment" is three steps, and only the last is the read.

## Fix

1. Grep the decompile for a **vanilla** type whose field shape exercises the path, intersecting the interface with the field: `grep -rl IComponentData` against `grep "public bool m_[A-Za-z]*;"`.
2. `ecs_query` for a live entity carrying it. For a buffer, confirm the length with `ecs_get_buffer` before spending the read.
3. Read it through the API under test **and** cross-check against a direct read — `em.Debug.GetComponentBoxed(…)` beside `em.GetComponentData<T>(…)`. Without the control, a call that returns without throwing is indistinguishable from one that misread.

`Game.City.PlayerMoney` (`bool m_Unlimited`) and `Game.Net.LabelPosition` (`bool m_IsUnderground`) are the two subjects that worked, on a loaded city.

## Prevention

Check the beacon before writing `UNVERIFIED:` on anything a live read would settle. Where the game is down, ask the user to launch it and stop there — the root `AGENTS.md` running-game boundary states the form — rather than shipping the marker because asking felt more expensive.

Where a shape has no vanilla carrier at all, that is a finding rather than an open question: no `IComponentData` or `IBufferElementData` under `src/Game/` carries a `char` field, so the `char` half of this question is unanswerable against vanilla and the entry recorded it as such.

Related: [a runtime question answered from first principles](a-runtime-question-answered-from-first-principles.md) is the neighbouring failure, where the cheap experiment went unrun because the question never needed the game at all.
