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

- [x] Values are off by default; the structural listing is unchanged when they are not requested.
- [x] With values requested, unmanaged components inline their field values at the same formatting
      depth the existing component read tool uses.
- [x] Kinds that no existing tool can read report their kind in place of a value.
- [x] A component whose value read throws records the error on its own entry, and the rest of the
      listing is returned normally.
- [x] The whole listing still runs inside a single suspend window.
- [x] The tool's schema states that values cost substantially more than the structural listing.
- [x] The driving skill notes that Entity-typed fields surfaced by inlined values are the handle for
      a follow-up call on the referenced entity.
- [x] Verified live against the reference target on an entity carrying an unmanaged component, a tag,
      and at least one kind that cannot be read.

`buffer` answers its kind alongside those, though `ecs_get_buffer` can read one: a buffer's length is
unbounded, so inlining elements would let a single entity flood the response. The kind column already
routes a caller to the tool that reads it on purpose.

Live verification, against the reference target:

- A tree instance listed 19 types: eight `component` entries inlined, nested structs and enums
  included (`CullingInfo { m_Bounds=Bounds3 { min=float3 { … } }, m_Mask=BoundsMask.3, … }`), beside
  `buffer`, `tag`, and `shared` entries each answering their own kind.
- `chunk` was proven on a scratch entity (`CreateEntity` + `AddChunkComponentData`, destroyed after),
  since no live archetype carried one. The same type came back as `component` on one entity and
  `chunk` on the other, which is the case the kind gate exists for.
- Following `PrefabRef.m_Prefab` back into the tool dumped the prefab carrying the tree's growth and
  wood data: the state lives on the prefab, not the instance, which is the case the spec opens with.
- The value read keeps the per-type presence gate rather than trusting the listing that just
  enumerated the type, so a duplicate full name across assemblies costs its entry an error instead of
  formatting memory the entity does not own. Re-verified live that the gate rejects nothing the
  listing reports.
- The per-entry error path is code-verified only: no component in the target could be made to fail
  its read, so nothing live exercised the catch.
