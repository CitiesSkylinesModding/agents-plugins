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

- [ ] `follow` accepts a fully-qualified component type name, optionally suffixed with a field name.
- [ ] With no suffix and exactly one Entity-typed field on the component, that field is chased.
- [ ] With no suffix and several Entity-typed fields, the call fails listing the candidates, in the
      style of the existing missing-field error.
- [ ] A named field that does not exist, or is not Entity-typed, fails with the same style of error.
- [ ] An entity that does not carry the named component fails with a message saying so, naming the
      component.
- [ ] The followed block reports the target entity, the component and field that led there, and the
      target's own component listing.
- [ ] The followed listing honours the same values setting as the primary entity.
- [ ] Exactly one level is followed; the followed entity's own references are not chased.
- [ ] The driving skill documents the pattern with the prefab case as a labelled reference-target
      example.
- [ ] Verified live against the reference target by following a prefab reference from a placed
      entity and finding state on the target that is absent from the source.
