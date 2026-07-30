# 04 — `ecs_list_components`: follow

**What to build:** state an agent is hunting often lives not on the entity it holds but on an entity
that one references — the case that motivated this work was a building's attractiveness living on
its prefab. `follow` chases one Entity-typed field to that second entity and dumps its archetype in
the same response.

The caller names the component to chase, so no game-specific type name enters the implementation.
When that component has exactly one Entity-typed field, naming the component alone is enough; when
it has several, the call fails with the candidates listed and an optional field suffix disambiguates.
The suffix shape mirrors the `<systemTypeFullName>:<method>` form the entity query tool's label
parameter already uses.

Exactly one level is followed. That bounds the response and makes cycles impossible by construction;
deeper chains are another call.

**Blocked by:** 02 — `ecs_list_components`: types, kinds, and enabled state.

- [x] `follow` accepts a fully-qualified component type name, optionally suffixed with a field name.
- [x] With no suffix and exactly one Entity-typed field on the component, that field is chased.
- [x] With no suffix and several Entity-typed fields, the call fails listing the candidates, in the
      style of the existing missing-field error.
- [x] A named field that does not exist, or is not Entity-typed, fails with the same style of error.
- [x] An entity that does not carry the named component fails with a message saying so, naming the
      component.
- [x] The followed block reports the target entity, the component and field that led there, and the
      target's own component listing.
- [x] The followed listing honours the same values setting as the primary entity.
- [x] Exactly one level is followed; the followed entity's own references are not chased.
- [ ] The driving skill documents the pattern with the prefab case as a labelled reference-target
      example.
- [x] Verified live against the reference target by following a prefab reference from a placed
      entity and finding state on the target that is absent from the source.

The skill documents the pattern WITHOUT the reference-target label the criterion asked for: the user
asked for the label and the game type name dropped, so the example reads generically ("a placed
instance whose data sits on its prefab costs one call, not two") and the concrete case lives below
instead.

The skill's older labelled examples went with it, converging the whole file on the root genericity
boundary: every illustrative type name now reads `MyGame.…`, and the framing line says so instead of
defining a reference-target label. The verification badges those examples carried are gone too,
following the same pass over the gameface skills.

Live verification, against the reference target:

- A tree instance followed through `Game.Prefabs.PrefabRef` (single Entity field, no suffix) with
  `values=true`: the instance's `Tree { m_State=TreeState.0, m_Growth=0 }` sits beside the prefab's
  `TreeData { m_WoodAmount=2400 }` and `GrowthScaleData { m_ChildSize=… }`, neither of which the
  instance carries. That is the case the spec opens with, answered in one call.
- The followed block's own Entity fields (`ServiceObjectData.m_Service`, `UIObjectData.m_Group`)
  came back as values, not as further listings, which is the one-level bound.
- `Game.Net.Edge` (`m_Start`, `m_End`) drove the ambiguity error and then the `:m_End` suffix, which
  landed on the node the edge ends at.
- The field shape is resolved BEFORE the entity is read, so a component no entity could be followed
  through reports its shape rather than a presence failure; a component that IS followable and
  absent reports the presence failure naming it.
- A field holding `Entity.Null` was refused by the liveness check
  (`SpawnableObjectData.m_RandomizationGroup` on the prefab above), which is what keeps the follow
  off an unchecked entity-store read.

An iterated review then changed five things, each re-verified live afterwards:

- **The storage kind is refused before anything reads memory.** A `buffer` name — which the listing
  itself prints — passed the presence gate, because `HasComponent<T>` resolves T to a type index a
  buffer element shares with a component; the read then reinterpreted the buffer header as the
  component's fields and answered a fabricated entity. `Unfollowable` now classifies the type off
  its (transitive) marker interfaces, in the order `KindOf` reports storage. Live: a buffer, a
  shared component, and a non-component class each came back named as what they are, the last two
  agreeing with the listing's own kind column.
- **A chunk component is covered twice over**, which is why the kind ladder needs no case for it.
  On a scratch entity carrying `Game.Common.Owner` as a chunk component, `HasChunkComponent` is true
  while `HasComponent` is false, so the ladder admits the type (it IS a plain component type) and the
  presence gate refuses the entity. Verified live, since no reasoning from the code could settle it.
- **The chased index is bounded before `Exists` is asked about it.** `Exists` indexes the store
  unchecked, so the bound `ResolveEntity` insists on now guards this path too, through a shared
  `HighestEntityIndex` helper. Where that member is absent the follow proceeds to `Exists` anyway,
  matching what `ResolveEntity` already does for an `index:version` pair rather than being stricter
  than the shared rule.
- **The chase runs before the primary listing**, so a failing follow no longer discards a listing
  the caller never sees. Nothing about a successful response changed.
- **The spec is trimmed, and an empty type half is refused** by the format error rather than by a
  type lookup that talks about something else. Live: `" "` is refused by name, and
  `PrefabRef: m_Prefab` follows rather than hunting a field called `" m_Prefab"`.
- A trailing colon named a field called `""` instead of taking the single-field path; `PrefabRef:`
  now follows.

Left as it stands, deliberately: with `values=true` the followed component is read twice, one round
trip out of roughly eighty, since deduplicating it would need a per-operation mirror cache. And an
`Entity` carries no world tag, so a reference into another world cannot be detected — `Exists` can
only answer for the world the call named. That is inherent to the ECS type and already true of every
entity parameter in the toolset.
