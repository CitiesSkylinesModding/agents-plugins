# Districts and themes

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

A district is an `AreaPrefab` subclass adding two name colors to the four area colors its base authors; its archetype adds `District`, `Geometry`, `LabelExtents`, `LabelVertex`, `DistrictModifier` and `Policy` on top of the area base's `Area`, `Node`, `Triangle`, `PrefabRef`, `Created` and `Updated` (`src/Game/Game.Prefabs/DistrictPrefab.cs`, `AreaPrefab.cs`).

## What a district imposes, and on whom

Sources: `src/Game/Game.Areas/AreaUtils.cs`, `src/Game/Game.Areas/District.cs`, `src/Game/Game.Areas/DistrictOption.cs`, `src/Game/Game.Areas/DistrictModifier.cs`, `src/Game/Game.Areas/DistrictModifierType.cs`, `src/Game/Game.Areas/CurrentDistrictSystem.cs`, `src/Game/Game.Areas/ServiceDistrictSystem.cs`, `src/Game/Game.Simulation/ServiceCoverageSystem.cs`.

```
AreaUtils.CheckOption:   (District.m_OptionMask & (1 << (int)option)) != 0
    // DistrictOption: PaidParking, ForbidCombustionEngines, ForbidTransitTraffic, ForbidHeavyTraffic, ForbidBicycles

AreaUtils.ApplyModifier: if (modifiers.Length > (int)type) { value += delta.x; value += value * delta.y; }
    // DynamicBuffer<DistrictModifier> of float2 m_Delta, indexed positionally by DistrictModifierType: an additive half and a multiplicative half in one entry

AreaUtils.CheckServiceDistrict, the one- and two-district overloads:
  the service has no ServiceDistrict buffer -> true    (serves everywhere)
  the buffer is empty                       -> true    (serves everywhere)
  the target's district(s) all null         -> false   (a target in no district is unreachable)
  otherwise                                 -> membership test; the two-district overload is the road form and passes on either BorderDistrict side
the third overload (building + buffer + CurrentDistrict lookup) folds the same tests into one conjunction with a true fallback: a target with NO CurrentDistrict component at all passes, and one carrying CurrentDistrict with a null district fails the membership test

CurrentDistrictSystem (Modification5): recomputes CurrentDistrict by testing the entity's position against the area search tree; a road gets BorderDistrict { m_Left, m_Right } instead
ServiceDistrictSystem (Modification5): a deleted district is stripped from every ServiceDistrict buffer in the world
```

Whether a given service is scopable is a read of its own prefab class: each service's `ComponentBase` under `Game.Prefabs` adds the `ServiceDistrict` buffer to its archetype under its own condition.
The buffer is written by the district selection UI, and `ServiceDistrictSystem` strips deleted districts from it; a consumer is any system holding `BufferLookup<ServiceDistrict>` — find them by that type — some routing the test through `AreaUtils.CheckServiceDistrict` above, others testing membership inline.
`ServiceCoverageSystem` applies the scope per road edge rather than per building: an edge with neither `BorderDistrict` side in the service's set is skipped outright, and one straddling an assigned and an unassigned district takes a 0.5 density factor.

**A short `DistrictModifier` buffer silently means no modifier.**
`ApplyModifier` guards on `modifiers.Length > (int)type` and does nothing past the buffer's end, and the positional indexing makes `DistrictModifierType`'s member order a save-compatibility surface.
Source: `src/Game/Game.Areas/AreaUtils.cs`, `src/Game/Game.Areas/DistrictModifierType.cs`.

**A building inside no district is invisible to every district-scoped service.**
The same `CheckServiceDistrict` that lets an unscoped service serve everywhere fails a null district's membership test, so scoping a service excludes the un-districted city, not just the other districts.
Source: `src/Game/Game.Areas/AreaUtils.cs`.

**A road takes a district option only when every bordering district that exists has it.**
`CheckOption(BorderDistrict, ...)` requires a side with the option set and no side with it unset — the opposite grain from service scoping, where a road passes on either side.
Source: `src/Game/Game.Areas/AreaUtils.cs`.

Policies supply both halves: `DistrictModifiers` and `DistrictOptions` on a `PolicyPrefab` produce a `DistrictModifierData` buffer and a `DistrictOptionData` mask, which is `city-state-and-progression`'s machinery landing in this topic's components (`src/Game/Game.Prefabs/DistrictModifiers.cs`, `DistrictOptions.cs`).

## Themes

Sources: `src/Game/Game.Prefabs/ThemePrefab.cs`, `src/Game/Game.Prefabs/ThemeObject.cs`, `src/Game/Game.Prefabs/ThemeData.cs`, `src/Game/Game.Prefabs/ObjectRequirementType.cs`, `src/Game/Game.City/CityConfigurationSystem.cs`, `src/Game/Game.UI.InGame/ToolbarUISystem.cs`.

A theme is a `ThemePrefab` — an `assetPrefix` string plus the zero-size `ThemeData` marker — and the whole binding mechanism is one buffer element: `ThemeObject` on a prefab adds a single `ObjectRequirementElement(theme, group, ObjectRequirementType.IgnoreExplicit)` to that prefab's requirement buffer, in a group of its own.
Its `[ComponentMenu]` admits it on `ZonePrefab`, `ObjectPrefab`, `NetPrefab`, `AreaPrefab`, `RoutePrefab` and `NetLanePrefab`.
The city's chosen theme is `CityConfigurationSystem.defaultTheme`, resolved on load: null falls back to the first `ThemeData` entity that is not `Locked`, and an `overrideThemeName` string can name one by prefab name.
A zone prefab's requirement element has one simulation consumer — the theme branch of `ZoneBuiltRequirementSystem` below — while the UI layer reads it wherever themes surface, the toolbar's zone list first; the variation draw that themes object prefabs is `placement-definitions`' machinery.

**Themed and unthemed are not two systems.**
A zone prefab is themed iff it carries a `ThemeObject`; there is no flag and no enum, so the presence of that requirement element is the whole difference, and which shipped zone types are themed is asset data — read it off the prefabs, not off any code.
Source: `src/Game/Game.Prefabs/ThemeObject.cs`, `src/Game/Game.Prefabs/ThemePrefab.cs`.

**`ThemeRequirementData` is declared and never consumed.**
A sweep of `src/` finds no producer and no consumer; the live mechanism is `ObjectRequirementElement`, so do not send a query at it.
Source: `src/Game/Game.Prefabs/ThemeRequirementData.cs`, `src/Game/Game.Prefabs/ThemeObject.cs`.

## Zone-built unlock requirements

Sources: `src/Game/Game.Prefabs/ZoneBuiltRequirementSystem.cs`, `src/Game/Game.Prefabs/ZoneBuiltRequirementData.cs`.

```
ZoneBuiltRequirementData { m_RequiredTheme, m_RequiredZone, m_MinimumSquares, m_MinimumCount, m_RequiredType, m_MinimumLevel }
ZoneBuiltRequirementSystem (Modification5), three mutually exclusive branches:
  m_RequiredZone set  -> tally that zone's squares and count at or above m_MinimumLevel
  m_RequiredTheme set -> walk each built zone's ObjectRequirementElement buffer for a ThemeData requirement equal to the required theme
  else                -> tally by AreaType at or above m_MinimumLevel
  a requirement passes only when BOTH m_MinimumSquares and m_MinimumCount are met
  tallies: a map of ZoneBuiltDataKey { m_Zone, m_Level } -> ZoneBuiltDataValue { m_Squares, m_Count }, fed primarily by a building create-and-delete chunk pass, with the ZoneBuiltLevelUpdate records level-up emits (level-up-loop.md) covering only level transitions -- so bulldozing moves the tally too
```

The unlock it drives belongs to `city-state-and-progression`.

(VOLATILE: every component, field, enum, system and `Source:` path this file names, the truth table and the modifier arithmetic included — their declarations in `Game.Areas`, `Game.Zones`, `Game.Prefabs`, `Game.Simulation`, `Game.UI.InGame`, `Game.Common`, `Game.Policies` and `Game.City` under `src/Game/`, at the files the section sources cite.)
