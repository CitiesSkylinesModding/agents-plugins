---
date: 2026-07-29
area: plugins/unity-devtools/sdb/Ecs.cs
symptoms:
  - 'World.All throws when enumerated'
  - 'ToEntityArray overload not found for Allocator'
  - 'Get/SetComponentData reports the type argument violates its constraint'
tags: [unity-entities, ecs, sdb, invoke]
---

# Reaching Unity Entities through debugger mirrors

## Problem

The ECS API is built for compiled call sites: generic constraints, allocator structs, `void*`
internals. Over SDB every call is a mirror-level invoke with none of that resolved for you.

## What didn't work

- Enumerating `World.All` as `IEnumerable` — it throws. Use `Count` plus the indexer.
- The non-generic `*Raw` component accessors — `internal`, and they traffic in `void*`, useless
  over the wire.
- Managed `IComponentData` — `Get/SetComponentData<T>` require `T : unmanaged`, so managed components
  are out of reach entirely.
- A debuggee-side reflection helper — never needed; live mirrors and invokes cover the whole path.

## Root cause

Mirrors expose the runtime's view, not the compiler's: overload choice, generic instantiation and
struct construction all have to happen client-side.

## Fix

The generic query chain, all through boxed-struct invokes:

`World.All` → `EntityManager` → `ComponentType.ReadWrite(Type)` → `CreateEntityQuery(ComponentType[])`
→ `CalculateEntityCount` / `ToEntityArray(AllocatorHandle)` → `NativeArray.ToArray()` → managed
`Entity[]` mirror.

`ToEntityArray` takes an `AllocatorManager.AllocatorHandle`: build it via `op_Implicit(Allocator)`;
`Temp` (2) needs no `Dispose`. Read and write a component with `EntityManager.Get/SetComponentData<T>`
instantiated live via `MethodMirror.MakeGenericMethod` (SDB protocol 2.24+; Unity 2022.3 answers 2.58).
Writes land in the running simulation.

Further invoke capabilities, all verified live:

- `out` parameters work via `InvokeOptions.ReturnOutArgs`; `EndInvokeMethodWithResult(...).OutArgs`
  returns every argument post-call with `out` values updated.
- `DynamicBuffer<T>` mutation works through boxed-struct invokes — `get_Item` / `Add` / `RemoveAt` hit
  the live chunk data, not a copy.
- An `Entity` value can be built client-side by cloning the `Entity.Null` `StructMirror` and
  overwriting `Index` / `Version`; no debuggee allocation needed.
- Managed systems are reachable via `World.GetExistingSystemManaged(Type)`.

Resolving an entity from a bare index, all measured live and all three needed together:

- `EntityManager.GetEntityByEntityIndex(int)` answers in ONE invoke, so no query has to be
  materialized and scanned to learn an index's live version.
- It indexes the entity store UNCHECKED. Well past the end it faults, surfacing as an in-game
  `NullReferenceException`; a negative index quietly returns a garbage entity. `HighestEntityIndex()`
  gives the inclusive upper bound, and the range belongs client-side, before the call.
- A free slot — never used, or destroyed — answers `Entity.Null`, so a version of 0 in the returned
  struct is the exact "nothing lives here" signal. It reads off the mirror already on the wire, which
  is why no `Exists` round trip is needed to tell a live answer from an empty one.

That the lookup validates nothing is not particular to it: on a build with the collections checks
compiled out, no `EntityManager` accessor does. See
[`entities-api-has-no-safety-net-on-player-builds.md`](entities-api-has-no-safety-net-on-player-builds.md).

## Prevention

Hold a `suspend` window across reads and writes that must see one consistent state; freezing the whole
game is the only real consistency primitive here.

Identifying the Entities version from the outside: assembly version metadata is all zeros, so the
embedded `com.unity.entities@<version>` string is the authoritative marker (the reference target ships
1.3.10, the modern API).
