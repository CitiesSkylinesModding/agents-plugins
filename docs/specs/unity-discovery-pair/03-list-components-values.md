# 03 — `ecs_list_components`: inline values

**What to build:** an agent that has seen what an entity carries asks the same tool to show what is
_in_ it. With values requested, every unmanaged component inlines its field values at the formatting
depth the other ECS read tools already use, so one call answers both the structural and the content
question. Without it, the structural question stays cheap.

Reading values is materially more expensive than listing types — each component is a generic
instantiation plus an invoke, and formatting walks nested struct fields — so it is opt-in, mirroring
the `members` flag on `find_types`.

Not every kind can be read. Shared, chunk, and managed components report their kind where a value
would go, rather than pretending the state does not exist. A read that throws is recorded on its own
entry: one unreadable component must never fail the listing.

**Blocked by:** 02 — `ecs_list_components`: types, kinds, and enabled state.

- [ ] Values are off by default; the structural listing is unchanged when they are not requested.
- [ ] With values requested, unmanaged components inline their field values at the same formatting
      depth the existing component read tool uses.
- [ ] Kinds that no existing tool can read report their kind in place of a value.
- [ ] A component whose value read throws records the error on its own entry, and the rest of the
      listing is returned normally.
- [ ] The whole listing still runs inside a single suspend window.
- [ ] The tool's schema states that values cost substantially more than the structural listing.
- [ ] The driving skill notes that Entity-typed fields surfaced by inlined values are the handle for
      a follow-up call on the referenced entity.
- [ ] Verified live against the reference target on an entity carrying an unmanaged component, a tag,
      and at least one kind that cannot be read.
