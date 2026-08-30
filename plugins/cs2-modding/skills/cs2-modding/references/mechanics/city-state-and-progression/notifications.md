# Notifications

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`NotificationIconPrefab` (`src/Game/Game.Prefabs/NotificationIconPrefab.cs`) adds `NotificationIconData { m_Archetype }` and `NotificationIconDisplayData` to the prefab and `Icon` to the archetype; `RefreshArchetype` builds the icon-entity archetype in `LateInitialize`, which is what makes an icon a real entity, and `Initialize` disables `NotificationIconDisplayData` when `m_EnabledByDefault` is false — the component is `IEnableableComponent, ISerializeAsEnabled`, and the latter selects the serializer that drops the enabled bit, so the bit is session state the save neither carries nor restores; `InfoviewsUISystem` flips it per active infoview.
An icon exists in two places: as an entity carrying `Owner + Icon + PrefabRef` (`src/Game/Game.Notifications/Icon.cs`; `m_ClusterIndex` is runtime-only) and as an `IconElement { m_Icon }` entry in the owner's buffer (`src/Game/Game.Notifications/IconElement.cs`).
`IconPriority`, `IconFlags`, `IconClusterLayer` with its parallel `IconLayerMask`, and `AnimationType` are the declared sets under `src/Game/Game.Notifications/`.

## The command buffer

Source: `src/Game/Game.Notifications/IconCommandBuffer.cs`, `src/Game/Game.Notifications/IconCommandSystem.cs` (`ModificationEnd`).

```
handshake: buffer = IconCommandSystem.CreateCommandBuffer()      // a fresh NativeQueue per call, m_BufferIndex++
           ... schedule the job that writes it ...
           IconCommandSystem.AddCommandBufferWriter(jobHandle)
           the queues are cleared every update and in OnStopRunning: obtain, fill and register within one frame

Add(owner, prefab, priority = Info, clusterLayer = Default, flags = 0, target = default, isTemp = false, isHidden = false, disallowCluster = false, delay = 0)     // strips IconFlags.CustomLocation
Add(owner, prefab, float3 location, priority = Info, clusterLayer = Default, flags = IgnoreTarget, ...)                                                // adds IconFlags.CustomLocation
Remove(owner, prefab, target = default, flags = 0)
Remove(owner, IconPriority priority)                 // CommandFlags.Remove | All: every icon on the owner at that priority
Update(owner)                                        // re-evaluates positions

playback, one non-parallel IJob over every queue concatenated and sorted by Command.CompareTo:
    owner index, then commands carrying a prefab after those without, then buffer index
```

The order in which two systems obtained their buffers decides which wins on the same owner in the same frame.

## Three families wearing one prefab type

The icon-prefab family is the complete set of map-marker icons; three subfamilies are told apart by what marks them — simulation failures by an authoring component, not an ECS type — and a prefab carrying none is still an icon:

| Family | Marked by | Raised by |
| --- | --- | --- |
| Tool errors | `ToolErrorData { m_Error, m_Flags }` beside `NotificationIconData`, keyed by `Game.Tools.ErrorType` (`src/Game/Game.Prefabs/ToolError.cs`, `src/Game/Game.Tools/ErrorType.cs`) | `Game.Tools.ValidationSystem`, never the simulation |
| Simulation warnings | the `SimulationWarning` authoring component, which adds no ECS type and ORs `1 << (int)category` per `IconCategory` into `NotificationIconDisplayData.m_CategoryMask` (`src/Game/Game.Prefabs/SimulationWarning.cs`, `src/Game/Game.Prefabs/IconCategory.cs`) | the simulation systems, each through a `NotificationIconPrefab` field on its configuration prefab |
| Markers | `TransportStopMarkerData`, `BuildingMarkerData`, `VehicleMarkerData`, `InfoviewMarkerData`, `MarkerMarkerData` on the owning prefab | `MarkerCreateSystem`, at `IconPriority.Info` on `IconClusterLayer.Marker` with `IconFlags.Unique` (`src/Game/Game.Notifications/MarkerCreateSystem.cs`) |

**The failure surface is not enumerable from the icon set.**
The second family carries no ECS marker, and the category mask is not a sound proxy: `NotificationIconPrefabSystem` writes bit 31 into `m_CategoryMask` for any prefab whose mask is zero, and read live an icon the simulation raises can carry that sentinel despite a fitting `IconCategory` member existing — so the list of simulation failure states lives in the authored icon assets, not in any query — reach it off the managed prefab, `PrefabSystem.GetPrefab<PrefabBase>(prefabEntity).TryGet<SimulationWarning>(out var warning)` for `warning.m_Categories` — `GetPrefab<T>` throws on a prefab the index no longer holds; `TryGetPrefab` with a null check is the guarded form ([`prefabs-and-assets`](../../technique/prefabs-and-assets/prefabs-and-assets.md)).
Source: `src/Game/Game.Prefabs/NotificationIconPrefabSystem.cs`, `src/Game/Game.Prefabs/SimulationWarning.cs`.

**A query naming `NotificationIconDisplayData` under-counts by the disabled prefabs, silently.**
The component is enableable and a prefab shipping `m_EnabledByDefault = false` is dropped by default query filtering; `ecs_query` on `NotificationIconData` alone is the full roster.
Source: `src/Game/Game.Prefabs/NotificationIconDisplayData.cs`, `src/Game/Game.Prefabs/NotificationIconPrefab.cs`.

**The mechanic-to-icon map is the `NotificationIconPrefab` fields on the configuration and parameter prefab classes.**
`BuildingConfigurationData.m_CondemnedNotification` is the shape: query the configuration singleton and read the field, so a mod raises the same icon the vanilla check did without a name lookup; each such field is a `public NotificationIconPrefab` member of a prefab or authoring class under `src/Game/Game.Prefabs/` — reached as a configuration singleton, through an instance's `PrefabRef`, or off the managed prefab.
Source: `src/Game/Game.Prefabs/BuildingConfigurationPrefab.cs`, `src/Game/Game.Prefabs/BuildingConfigurationData.cs`.

**Removing a notification means removing it in two places, and disabling the system that raised it clears nothing.**
The owner's `IconElement` entry and the `Owner + Icon + PrefabRef` entity are separate; a mod retiring a vanilla icon rebuilds the owner's buffer minus the matching entries and deletes every icon entity whose `PrefabRef.m_Prefab` is that prefab, and an uninstall sweeps `[Icon, Owner]` for entities whose `Owner.m_Owner` is `Entity.Null`.
Source: `src/Game/Game.Notifications/IconElement.cs`, `src/Game/Game.Notifications/Icon.cs`.

**The infoview dims out-of-category icons rather than filtering them.**
`NotificationIconBufferSystem` writes `(0.5, 0)` instead of `(1, 1)` into the last two icon params of an icon whose `m_CategoryMask` misses the active `InfoviewPrefab.m_WarningCategories`, so it is still in the buffer; the shader decides what those two numbers look like.
Source: `src/Game/Game.Rendering/NotificationIconBufferSystem.cs`, `src/Game/Game.Prefabs/InfoviewPrefab.cs`.

`NotificationIconDisplayData.m_IconIndex` is assigned per session by walking chunks from 1, so it is an ordering and never an id to persist.
`NotificationsSection` (`src/Game/Game.UI.InGame/NotificationsSection.cs`) is a selected-info section built from the target's `IconElement` buffer, skipping every icon on the `Marker` cluster layer, plus the icons of its employees, its renters' citizens and employees, its route waypoints and the citizens whose `CurrentBuilding` it is.

(VOLATILE: every system, component, field, property, enum, method and `Source:` path this file names — their declarations under `src/Game/` in `Game.Notifications`, `Game.Prefabs`, `Game.Tools`, `Game.Common`, `Game.Citizens`, `Game.Rendering` and `Game.UI.InGame`, at the files the sections cite; plus the live-read sentinel fact the failure-surface trap states, re-derived over the `ecs_query` on `Game.Prefabs.NotificationIconData` roster, reading each prefab's `NotificationIconDisplayData.m_CategoryMask`.)
