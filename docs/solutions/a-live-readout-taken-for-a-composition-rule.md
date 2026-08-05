---
date: 2026-08-05
area: docs/research (the cs2-modding discovery pipeline, against any live-queried runtime set)
symptoms:
  - 'a live query readout is correct and the rule written from it is not'
  - 'a set read from the running game matches the code literals exactly, and both are incomplete'
  - 'a claim established against the running game contradicts a shipped sibling that read the code'
tags: [research, live-verification, over-reach, ecs, serialization, verification]
---

# A live readout says what a set contains, never how it is composed

## Problem

The save query in this game was read live and reported eighteen `Any` component types, all vanilla.
The reference shipped the rule that "the save selects entities on a fixed list of vanilla
components", and built a residue-entity guarantee on top of it. The readout was accurate. The rule
was wrong, and it inverted the guidance an agent acts on.

## What didn't work

**Reading the set once and generalising.** Eighteen types came back, and eighteen literals sit in
the code that seeds the query, so the two agreed and the reading looked doubly confirmed.

**Probing with types that could never have shown the gap.** The follow-up test added
`Game.Objects.Transform`, `Game.Prefabs.PrefabRef` and `Unity.Entities.CleanupEntity` and watched
the query match or not. All three are Game- or Unity-assembly types, and the composition step that
was missed admits only types from _other_ assemblies — so the probe was structurally incapable of
observing it.

**Assuming a loaded mod would have revealed it.** Nine code mods were loaded and the query still
read eighteen, which looks like evidence against a union. None of the nine declared a serializable
component, so the union was real and empty.

## Root cause

`SerializerSystem.CreateQuery` takes a parameter and unions it into `Any`: it seeds the eighteen
literals, then `foreach (ComponentType serializableComponent in serializableComponents) {
hashSet.Add(...) }` (`src/Game/Game.Serialization/SerializerSystem.cs:171-209`). The list comes from
`ComponentSerializerLibrary.Initialize`, which admits every `TypeManager` type implementing
`ISerializable` or `IEmptySerializable` whose `type.Assembly != assembly`
(`src/Colossal.Core/Colossal.Serialization.Entities/ComponentSerializerLibrary.cs:42/50-56/73-79`),
and `ModManager.AfterLoadAssembly` calls `SerializerSystem.SetDirty()` precisely so a mod's types
enter it (`src/Game/Game.Modding/ModManager.cs:146-150`).

So the eighteen was the union's empty case. A set assembled at runtime reads identically to a fixed
one whenever the variable half happens to be empty, and nothing in the readout marks which it is.

## Fix

Read the code that _builds_ the set, not only the set. Where that is not available, vary the input
and read twice: enabling one mod that declares serializable components moved the query from eighteen
`Any` to twenty-one, the three additions resolving through `TypeManager.GetType` to that mod's own
components in its own assembly, while the clear query stayed at nineteen throughout.

## Prevention

A live readout of a collection is evidence about its contents at that moment and about nothing else.
Before writing "fixed", "always" or "only" from one, name what would have to be true for the set to
be assembled rather than declared, and say which you checked.

Two-reading beats one-reading whenever the composition is the claim: a probe that cannot change the
answer cannot confirm the rule either. Choose the second reading so that it _would_ differ if the
rule were wrong — a probe drawn from the same assembly as the literals never can.
