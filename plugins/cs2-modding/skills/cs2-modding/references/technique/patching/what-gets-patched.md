# What the corpus was found patching

Verified against game version 1.6.0f1.

The vanilla surfaces mods were found patching, grouped by the kind of seam that is missing.
`patching` owns the discipline; this file is the lookup.

Every target below was read at 1.6.0f1 and carries the signature its patch claims.

**This is a sample rather than a complete list.**
It was built mostly from patch declarations, with some targets recovered from mods that apply patches through a wrapper of their own instead.
That second route is not swept exhaustively, so a target reached by reflection or through such a wrapper can be patched in the wild and absent here.
So the useful reading runs one way only.
**Your target is here** means somebody has already patched it, and the group tells you what shape their patch took.
**Your target is absent** means nothing at all, and least of all that the game leaves a seam there.

(VOLATILE: every method name and signature on this page — the tools, UI, simulation and rendering namespaces of the decompiled game.)

## A tool's per-frame raycast and snap configuration

`custom-tools` owns the enums and contracts behind this group, and `patching` says why the game leaves no seam here.

| Target                                                                                                      | Type                                                       |
| ----------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------- |
| `InitializeRaycast()`                                                                                       | `BulldozeToolSystem`, `NetToolSystem`, `DefaultToolSystem` |
| `GetRaycastResult(...)`, both overload families                                                             | `ToolBaseSystem`, `BulldozeToolSystem`                     |
| `GetActualSnap(Snap, Snap, Snap)` — the public static three-parameter form                                  | `ToolBaseSystem`                                           |
| `GetAvailableSnapMask(...)` — the private static overload, not the public override                          | `AreaToolSystem`, `ObjectToolSystem`                       |
| `GetAllowRotation()`                                                                                        | `ObjectToolSystem`                                         |
| `SnapControlPoint(JobHandle)` — private, called from seven places across the cancel, apply and update paths | `ObjectToolSystem`                                         |

## A value the game publishes to its own UI

Most producers here are private and reached only through a delegate the system captured in its own `OnCreate`, and for those there is no seam by construction: the binding is already registered, the system is registered as a concrete type, and the producer is not virtual.
Not all of them are, so check yours before assuming a patch was the only route: two of the time producers are public, and one of the actions-section pair overrides an abstract method its base class calls, which a derived section reaches without patching.
The other half of that pair is a private callback bound as a trigger, which nothing overrides.

| Target                                                         | Type               |
| -------------------------------------------------------------- | ------------------ |
| `GetElevationRange()`, `AllowBrush()`, `SetBrushStrength(...)` | `ToolUISystem`     |
| `Apply(...)`, `BindAssets(...)`                                | `ToolbarUISystem`  |
| `GetDay(...)`, `GetTicks(...)`                                 | `TimeUISystem`     |
| `WriteDemandFactors(...)` — private, called from six writers   | `CityInfoUISystem` |
| `OnProcess(...)`, `OnDelete(...)`                              | `ActionsSection`   |

## A value the game asks for and then acts on

A value the game consumes immediately, rewritten through `ref __result`.
Most are booleans forced the other way; `GetObjectPrefab` is not, and returns a prefab.

| Target                                                               | Type                        |
| -------------------------------------------------------------------- | --------------------------- |
| `IsPlacedUniqueAsset(...)`                                           | `UniqueAssetTrackingSystem` |
| `IsEditor(...)` — an extension method on an enum                     | `GameModeExtensions`        |
| `TrySetPrefab(PrefabBase)` — returns `bool`                          | `ObjectToolSystem`          |
| `GetObjectPrefab()` — private, returns `ObjectPrefab`, no parameters | `ObjectToolSystem`          |

The outlier that belongs here is **neutralising a system from inside its own constructor**: a postfix on `UniqueAssetTrackingSystem.OnCreate` setting `Enabled = false` on it.
The ordinary route is `World.GetOrCreateSystemManaged<T>().Enabled = false` from `OnLoad` (`mod-lifecycle-and-ordering`), and it is what to reach for.

## A simulation value, or the managed method that schedules a job

| Target                                                                                                                                                            | Type                         |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------- |
| `OnUpdate`, both `GetYear` overloads, `get_normalizedDate`, `GetDay`, `GetCurrentDateTime`, `GetStartingDate`, `GetElapsedYears`, `GetTimeOfYear`, `GetTimeOfDay` | `TimeSystem`                 |
| `SampleClimate(ClimatePrefab, float)` — the two-argument overload                                                                                                 | `ClimateSystem`              |
| `CalculateUpkeep(...)` — public static                                                                                                                            | `CityServiceUpkeepSystem`    |
| `OnUpdate`                                                                                                                                                        | `CityServiceBudgetSystem`    |
| `SetGlobalProperties(CommandBuffer, WindVolumeComponent)` — private                                                                                               | `Game.Rendering.WindControl` |
| `UpdatePrefabs(...)`                                                                                                                                              | `PrefabSystem`               |
| `GetTextureReferenceCount(...)`                                                                                                                                   | `AssetImportPipeline`        |
| `FindTargets(SetupTargetType, in SetupData)` — private                                                                                                            | `PathfindSetupSystem`        |

The last one and `ObjectToolSystem.SnapControlPoint` are the two job-substitution patches: a prefix that schedules its own job and returns the handle through `ref __result`.
`patching` states that shape in full.

`TimeSystem` is where the overload hazard `patching` describes bites hardest: `GetTimeOfDay` and `GetTimeOfYear` each split a public overload from a protected one on a single `double renderingFrame` parameter, and `GetYear`'s two overloads split the same way while both being public.
All of them need an explicit `Type[]`.
