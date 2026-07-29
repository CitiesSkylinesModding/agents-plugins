# 06 — Partial type-load recovery

**What to build:** a mod author whose assembly was compiled against a different game version can
still search its types. When an assembly's `GetTypes()` throws `ReflectionTypeLoadException` — the
assembly loaded, but resolving some of its types failed — the catalog keeps the types that did load
instead of dropping the assembly from search entirely.

This matters precisely because the failing assembly is usually the user's own: a mod referencing a
member the game no longer has is both the likeliest thing to throw and the thing its author most
wants to explore. Skipping it would blind search to exactly the wrong assembly.

The recovery is cheap. The thrown object is reachable as a mirror on the invocation failure, so one
further invoke joins its partial `Types` array; the slots that failed to load render as empty
entries and are dropped client-side. The assembly is marked partial so the gap is visible rather
than silent.

The reference target never exercises this path — all 165 of its assemblies enumerate cleanly — which
is why the fixture has to manufacture the failure: an assembly whose dependency is deliberately
absent at run time.

**Blocked by:** 05 — Type catalog and `find_types` search.

- [ ] An assembly whose type enumeration throws contributes the types that did load, rather than
      being skipped.
- [ ] The recovery costs one additional invoke, and only on assemblies that actually failed.
- [ ] Slots that failed to load are dropped rather than surfacing as empty or null entries.
- [ ] The assembly is marked partial in the response, so a caller can tell an incomplete listing
      from a complete one.
- [ ] A failure that is not a partial type-load is not swallowed by this path.
- [ ] The integration fixture gains an assembly whose dependency is absent at run time, so the
      failure is reproducible in the suite.
- [ ] Integration tests assert the recovered partial list, the partial marking, and that a search
      finds a type from the broken assembly.
