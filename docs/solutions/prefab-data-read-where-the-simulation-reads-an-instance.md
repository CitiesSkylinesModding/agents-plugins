---
date: 2026-08-06
area: docs/research and plugins/cs2-modding/skills (any mechanics topic whose numbers sit on buildings)
symptoms:
  - 'a value read from a prefab is right and the simulation uses a different one'
  - 'a mod figure matches the game panel for a fresh building and drifts for an upgraded or damaged one'
  - 'a reference names `SchoolData` and the system reading it is `ComponentLookup<Game.Buildings.School>`'
tags: [cs2, prefabs, ecs, mechanics, balance, upgrades, efficiency, over-reach]
---

# A prefab value read where the simulation reads an instance

## Problem

The `citizens-and-households` reference told a reader to reach a school's, a prison's and a
hospital's contribution through the building's `PrefabRef` and read `SchoolData`, `PrisonData` or
`HospitalData`. The simulation reads none of those. Every figure a mod built that way is the
design value, and the citizens receive something else — the same number scaled by the building's
efficiency, with its upgrades folded in.

Nothing about the read fails. It compiles, it returns a plausible number, and it agrees with the
game for a building at full efficiency and no extensions, which is what a first test uses.

## What didn't work

**Naming the component the prose already named.** The research file, the shipped reference and a
verify pass all said `SchoolData`, because the type is the one a search for the concept finds and
the instance component has the same name minus the suffix.

**Reasoning from `ICombineData`.** The interface is on `SchoolData`, `PrisonData`, `HospitalData`
and thirty-odd others, which reads as the platform combining upgrades for you. It does not:
`ICombineData<T>` only declares `Combine(T)`, and a verify pass inferred the rest.

## Root cause

Two component families with near-identical names.

`Game.Prefabs.SchoolData` sits on the **prefab** and holds the unscaled design figure, written once
at authoring (`Game.Prefabs/School.cs:56-64`). `Game.Buildings.School` sits on the **building
entity** and is what the simulation consumes — `CitizenHappinessSystem.cs:175` declares
`ComponentLookup<Game.Buildings.School>`, and `:324-332` reads it for the `Buildings` happiness
factor. `SchoolAISystem.cs:145-146` writes the instance each pass as
`clamp(round(efficiency * combinedPrefabValue), -100, 100)`. `PrisonAISystem.cs:333-334` and
`HospitalAISystem.cs:249` are the same shape; the hospital adds a resource-shortage penalty and
narrows to a byte, so a negative combined bonus cannot survive into the instance at all.

Upgrades are never summed for you. A consumer that wants them walks the building's own
`InstalledUpgrade` buffer into a stack local (`UpgradeUtils.cs:18-31`, taking `ref T data`) and
discards it — the combined figure lives on the stack, and the helper writes it nowhere. So a plain
`PrefabRef` read silently under-reports every building the player has extended.

Not every vanilla read combines, either — `Game.City/CityUtils.cs:73` takes `m_StudentCapacity`
straight off the prefab in the same method that hand-combines `WorkplaceData` two lines above. So
reproducing a vanilla calculation means checking whether _that_ caller combines, rather than
assuming either way.

## Fix

Read the instance for what a citizen receives, the prefab only for what a fully efficient building
would give. Where the effective prefab-level figure is what you want — a capacity, a graduation
modifier — use `UpgradeUtils.TryGetCombinedComponent`, or walk `InstalledUpgrade` and call
`UpgradeUtils.CombineStats` yourself. `CombineStats` skips upgrades flagged `BuildingOption.Inactive`,
so a disabled extension correctly contributes nothing.

## Prevention

The name is the trap, so the check is mechanical: **before citing a `*Data` component for a value
the simulation consumes, look for a same-named component in the `Game.Buildings` namespace, and
read what the consuming system's `ComponentLookup` is actually typed to.** Where both exist, the
instance is the one that moves.

**The twin is not always a component, and not always on a building.** `ServiceFeeParameterData`'s
fee defaults seed the city entity's `ServiceFee` buffer once, when the city is created
(`Game.Simulation/CitySystem.cs:97-102`), and every charge afterwards reads the buffer
(`ServiceFeeSystem.cs:126`). The names share no suffix to warn you — and the same component's other
fields are read live off the singleton every pass, `m_ElectricityFee.m_Default` as the divisor behind
each building's consumption (`AdjustElectricityConsumptionSystem.cs:132`), so this is not even a
property of the component. It is a property of those entries.

So the two cases share a diagnosis and not a mechanism: the building twins are rewritten every pass,
the fee buffer is seeded once. What generalises is the question — **ask what the consuming system
reads, rather than what the value is called** — asked per field rather than per component.

Reading a value off the prefab entity each pass stays the common case by a wide margin, which
`prefabs-and-assets` says in its own words. That is why these need finding rather than assuming: every
remaining mechanics topic inherits the question, `city-services-and-coverage` and
`zoning-buildings-and-land-value` first.
