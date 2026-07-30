---
date: 2026-07-29
area: plugins/unity-devtools/sdb/Ecs.cs
symptoms:
  - 'a component read returns all-zero fields for a component the entity does not have'
  - 'ecs_get_buffer reports length 0 for a buffer the entity does not have'
  - 'the game dies a few calls after a buffer tool ran against a wrong element type'
  - 'HighestEntityIndex() throws NullReferenceException while Exists still answers on the same EntityManager'
  - 'a component read answers plausible fields for a type the entity carries as a BUFFER, and the presence check allowed it'
tags: [unity-entities, ecs, sdb, invoke, memory-safety]
updated: 2026-07-30
---

# Entities answers for state that is not there, on a build with the checks compiled out

## Problem

`EntityManager`'s generic accessors validate their arguments under
`ENABLE_UNITY_COLLECTIONS_CHECKS`, which player builds compile out. Call one for a component the
entity does not carry and nothing throws: the call derives a chunk offset from an archetype that has
no such type and reads whatever memory is at that offset. The answer looks entirely plausible, and
where the call takes write access it can take the process down.

Both halves were measured live, on an entity confirmed by `HasComponent` to lack the type:
`GetComponentData<Transform>` returned `Transform { m_Position=float3 { x=0, y=0, z=0 }, ... }`, and
`GetBuffer<T>` reported `Length 0`. An agent reads that as "present and empty" and stops looking.

## What didn't work

Blaming the obvious suspect. The first crash followed a series of deliberately out-of-range
`GetEntityByEntityIndex` probes, so those looked causal. They are not: replayed one at a time on a
fresh process, three consecutive access violations (`int.MaxValue`, `1500000000`, repeated) left the
store fully healthy. That cost a whole investigation pass.

Reading the crash backwards from `HighestEntityIndex()`. It began throwing `NullReferenceException`
two calls before the process died, which reads like a torn-down world -- but `Exists` and
`GetEntityByEntityIndex` kept answering correctly on the same `EntityManager` throughout. It is a
symptom of a store already going bad, not a cause, and not a member to distrust: on a healthy
process it is reliable.

## Root cause

The access mode decides severity, and it is the one variable the harmless and fatal calls differ on.
More than a dozen read-only probes of absent types (`em.GetBuffer<T>(e, true)`) were inert. The two
calls that preceded a crash both went through a tool, and `Ecs.GetBuffer` requests WRITE access:

```csharp
return this.inv.Invoke(this.EntityManager, m, entity, this.inv.Prim(false)); // isReadOnly: false
```

Taking write access on a component the entity does not own is what degrades the store. Reading the
same phantom is merely wrong.

The unchecked by-index entity lookup recorded in
[`unity-entities-over-sdb.md`](unity-entities-over-sdb.md) is the same absent safety net seen from
another angle.

## Fix

Establish presence client-side before invoking any accessor, and let the refusal name what is
missing (`Ecs.RequirePresence`), with the predicate matching the kind asked about -- `HasComponent`
for a component, `HasBuffer` for a buffer:

```csharp
var has = this.inv.FindMethod(this.EntityManagerType, hasMethod, 1, 1, ["Entity"])
  .MakeGenericMethod([type]);
```

One invoke, and worth it: it converts fabricated data and a dead game into
`entity 50397:5 has no Game.Citizens.Citizen component`. Cache the confirmed pair for the operation
-- a read-modify-write asks three times, and an archetype cannot change inside one suspend window.

Pair it with the access mode: the buffer accessor takes `isReadOnly` from the caller, so only the
editing tool asks for write. Presence refuses the mistake, read-only bounds what a missed one costs.

### The gate is not enough when the CALLER names the type

`HasComponent<T>` resolves `T` to a `TypeManager` type index, and a buffer element shares that index
space with a component of the same name. Ask it about a type the entity carries as a BUFFER and it
answers yes, so `GetComponentData<T>` proceeds, reads the chunk at component layout, and reinterprets
the buffer header (pointer, capacity, length) as the struct's fields. The same fabricated value as
above, reached straight through the gate meant to prevent it -- and reachable from a name the
archetype listing itself prints.

So a path taking a type name from outside classifies the storage kind FIRST, off the type's marker
interfaces, and reads only if that says component (`Ecs.Unfollowable`). The interfaces arrive as the
transitive closure in one round trip, so a marker reached through a derived interface still counts.

A chunk component bounds the rule: it IS a plain component type, so no interface check separates it,
but the archetype holds it as a distinct `ComponentType` and `HasComponent<T>` answers no on the
entity carrying it -- `HasChunkComponent<T>` is the yes. There, the presence gate suffices.

## Prevention

Treat every `EntityManager` accessor as unvalidated. Before adding one: check presence first, and
pass `isReadOnly: true` on any path that only reads, so a mistake costs a wrong answer instead of
the user's session.

Where the type comes from the caller rather than from your own code, add the storage-kind check ahead
of the presence one. The presence predicate answers about an index, not a layout, so it cannot tell a
buffer element from a component -- and a tool that lists an entity's types hands callers exactly the
names that expose the difference.

When something does go wrong, the probe that separates the two failure modes is access mode, not
index: repeat the suspect call with `isReadOnly: true` and see whether it is still fatal.
