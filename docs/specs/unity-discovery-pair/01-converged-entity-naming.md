# 01 — Converged entity naming

**What to build:** every ECS tool interprets an entity the same way. An agent hands any of them a
bare `index` and gets the live entity, or hands them an explicit `index:version` and gets it
verified. The component tools stop scanning a query to guess a version, the buffer tools stop
silently defaulting it to `1`, and the driving skill describes one rule instead of documenting the
disagreement between two.

An index outside the world's range reports a real error naming the valid bound, rather than
surfacing the in-game `NullReferenceException` that `GetEntityByEntityIndex` throws there.

This ticket also introduces the capability-probing plumbing the rest of the feature depends on,
because its first real consumer is here: a target whose Entities version lacks
`GetEntityByEntityIndex` must refuse bare indices with an actionable message instead of failing on a
missing-member exception. The Entities version is not inferable from assembly metadata
(`Unity.Entities` reports `Version=0.0.0.0`), so support is decided by probing the exact member the
code calls, cached for the session.

**Blocked by:** None — can start immediately.

- [x] A bare `index` resolves through `EntityManager.GetEntityByEntityIndex` in a single invoke, on
      every ECS tool that accepts an entity.
- [x] An explicit `index:version` is still built client-side and verified with `Exists`, unchanged.
- [x] The index is range-checked client-side against `EntityManager.HighestEntityIndex()` before the
      call, and an out-of-range index produces an error naming the valid range.
- [x] The component tools' query-scan resolution branch is removed, along with the ECS module's
      entity-search helper that only it used.
- [x] The buffer tools no longer default a missing version to `1`.
- [x] `Invoker` gains non-throwing counterparts to its existing throwing member lookups, and probe
      results are cached for the session.
- [x] When `GetEntityByEntityIndex` is absent on the target, bare indices are refused with an error
      asking for an explicit version; explicit versions keep working.
- [x] The driving skill's entity passage states one rule and no longer documents the old
      inconsistency.
- [ ] The behavior change to the shipped component and buffer tools is called out explicitly in the
      commit message.
- [x] Verified live against the reference target: bare index, explicit version, stale version,
      out-of-range index, on both a component tool and a buffer tool.

Two behaviors the reference target settled, beyond what the ticket asked:

- The lookup is unchecked at BOTH ends — a negative index returns a garbage entity rather than
  faulting — so the client-side range covers the lower bound too.
- A free slot (never used, or destroyed) answers `Entity.Null`, so a version of 0 on the returned
  mirror rejects a dead index without spending an `Exists` round trip.
