# 08 — Refuse a buffer the entity does not have

**What to build:** the buffer tools establish that the entity actually carries the buffer before
asking the game for it. Today neither checks, and the consequences run from a wrong answer to a dead
process.

`EntityManager.GetBuffer<T>` is only safe on an entity that has `T`. On a target built with the
collections safety checks disabled — the reference target is one — it does not throw for a missing
`T`; it hands back a `DynamicBuffer<T>` over memory the entity does not own. Two symptoms follow
from that one missing precondition:

- `ecs_get_buffer` reports `length 0`, which reads as "the buffer is there and is empty". An agent
  looking for state concludes it does not exist and stops looking. This is the silent wrongness the
  plugin's contract exists to prevent.
- The entity store degrades and the game dies shortly after. Established live, twice: the only two
  calls made through a buffer tool against an absent element type each preceded a crash, the first
  with `EntityManager.HighestEntityIndex()` beginning to throw two calls later while `Exists` and
  `GetEntityByEntityIndex` still answered on the same `EntityManager`.

What separates the harmless case from the fatal one is the access mode. `Ecs.GetBuffer` passes
`isReadOnly: false`, so every buffer tool asks for WRITE access to a component the entity may not
carry. Read-only probes of absent element types, run more than a dozen times during the
investigation, had no effect at all.

`ecs_buffer_edit`'s "buffer is empty" guard is not a substitute: it fires after `GetBuffer` has
already been invoked, so it rejects the call having already done the damage.

Refusing the call is also the more useful answer. "This entity has no buffer of that type" is what
the agent needed to learn, and it is the same shape as the errors the ECS tools already give for an
unknown type or a missing field.

**Blocked by:** None — can start immediately.

- [ ] A buffer read or edit on an entity that does not carry the element type fails with an error
      naming the entity and the type, and no `GetBuffer` invoke is made.
- [ ] `ecs_get_buffer` requests READ-ONLY access, since it never writes.
- [ ] `ecs_buffer_edit` keeps write access, and its empty-buffer guard no longer stands in for a
      presence check.
- [ ] The presence check costs one invoke and runs on both buffer tools through one shared path, so
      a later buffer tool cannot skip it.
- [ ] Verified live against the reference target: reading and editing an absent buffer each fail
      cleanly, and the game is still healthy afterwards.
- [ ] Verified live that a buffer the entity does carry reads and edits exactly as before.
- [ ] The checks-disabled behavior of `GetBuffer` on a missing component is recorded in the ECS
      solutions note, since nothing in the API's signature warns about it.
- [ ] The refusal is called out as a behavior change in the commit message: a call that used to
      answer `length 0` now fails.
