# 02 — `ecs_list_components`: types, kinds, and enabled state

**What to build:** an agent that lands on an unknown entity asks one question and learns everything
the entity carries. The new tool reports every component type on the entity, each annotated with its
kind — component, tag, buffer, shared, chunk, or managed — so the agent knows which existing tool
can read it, including when the honest answer is that none can. Enableable components additionally
report whether they are currently enabled, because a present-but-disabled component is invisible to
the simulation while `HasComponent` still answers yes.

This replaces playing twenty questions with `HasComponent<T>` through `eval`.

The tool stays narrow by design: it reports the entity's shape and composes with the existing read
tools for anything deeper.

Two acquisition facts were established live and should not be re-derived. The component array comes
from `EntityManager.GetComponentTypes(entity, Temp)` in one invoke. Names must come from
`ComponentType.GetManagedType().FullName` — the debug-name paths are unusable on a shipped build,
where `ComponentType.ToString()` returns null and `EntityManager.Debug.GetEntityInfo` returns
`ComponentTypeInArchetype` placeholders, because the `TypeManager` debug-name table is stripped.

**Blocked by:** 01 — Converged entity naming.

- [x] The tool accepts an entity under the converged naming rule and lists every component type on
      it.
- [x] Each entry carries the component's fully-qualified name and its kind, classified from the
      `ComponentType` flags (`IsBuffer`, `IsSharedComponent`, `IsChunkComponent`, `IsZeroSized`,
      `IsManagedComponent`).
- [x] Entries whose kind no existing tool can read are listed and marked, never omitted.
- [x] Components reporting `IsEnableable` carry their current enabled state, read through the
      non-generic `EntityManager.IsComponentEnabled(entity, componentType)` overload.
- [x] When the enabled-state capability is absent on the target, the state is omitted and the
      response says the capability is unavailable, so absence is never read as "nothing is disabled".
- [x] No game-specific type name appears anywhere in the implementation.
- [x] The driving skill documents the tool as the orient step for an unknown entity, and states what
      each kind implies about which tool can read it.
- [x] Verified live against the reference target on an entity carrying both enableable and
      non-enableable components, and on one carrying a buffer.

Live verification, against the reference target:

- A prefab entity listed 11 types across `component`, `buffer`, and `tag`, two of them enableable and
  both enabled; a city object listed a `buffer` reporting `enabled: false` beside enabled ones, which
  is the case the state exists for.
- `shared` and `chunk` were absent from every archetype the target had live, so they were proven on a
  scratch entity built through `eval` (`CreateEntity` + `AddSharedComponent` +
  `AddChunkComponentData`, destroyed after). `managed` stays unobserved: the target carries none.
- A chunk component reports the wrapped type's name under kind `chunk`, which is the readable answer.
