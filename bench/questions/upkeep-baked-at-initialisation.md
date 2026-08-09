# Why retuning the upkeep exponent changes nothing

## Prompt

In a running Cities: Skylines II city, a mod raises the residential upkeep level exponent on `EconomyParameterData` and finds that no residential building's upkeep changes. Name the system and method that actually compute a growable building's upkeep, state at what point in the game's lifecycle that computation runs, name the component and field the result is stored in, and explain why the mod's change has no effect.

## Verified answer

**`PropertyRenterSystem.GetUpkeep` computes it**, at `src/Game/Game.Simulation/PropertyRenterSystem.cs:226`:

`	public static int GetUpkeep(int level, float baseUpkeep, int lotSize, AreaType areaType, ref EconomyParameterData economyParameterData, bool isStorage = false)`

It is a pure function of its arguments, reading exactly three economy parameters — `m_ResidentialUpkeepLevelExponent` (`:232`), `m_IndustrialUpkeepLevelExponent` (`:237`) and `m_CommercialUpkeepLevelExponent` (`:240`) — and returning `round(pow(level, exponent) * baseUpkeep * lotSize)`, halved for storage buildings (`:244`).

**It runs once, at prefab initialisation.** It has exactly one call site in the whole decompile, `src/Game/Game.Prefabs/BuildingInitializeSystem.cs:1076`:

`						reference.m_Upkeep = PropertyRenterSystem.GetUpkeep(level, zoneServiceConsumptionData.m_Upkeep, lotSize, value4.m_AreaType, ref economyParameterData, isStorage);`

That system's query is `Created` + `PrefabData` (`:757`), so this is per prefab entity at initialisation — not per tick, and not per building instance. The economy parameters are sampled right there, at `:1075`. Three guards skip the bake, at `:1066`: the chunk must carry `ConsumptionData`, the prefab must not author its own `ServiceConsumption`, and the zone prefab must have `ZoneServiceConsumptionData`. A prefab with no zone prefab at all is skipped earlier, at `:1016`.

**The result is stored in the building prefab's `ConsumptionData.m_Upkeep`** — `reference` at `:1069` is an element of the chunk's `ConsumptionData` array.

**Why the mod's change does nothing:** the value was baked from the economy parameters as they stood at prefab initialisation, and nothing recomputes it afterwards. The per-tick `BuildingUpkeepSystem` never calls `GetUpkeep`; it holds `ConsumptionData` `[ReadOnly]` (`Game.Simulation/BuildingUpkeepSystem.cs:127-128`) and only slices the baked figure:

- `:182` — `				ConsumptionData consumptionData = m_ConsumptionDatas[prefab];`
- `:189` — `				int num = consumptionData.m_Upkeep / kUpdatesPerDay;`

where `kUpdatesPerDay` is a named `public static readonly int` of 16 on that system (`:1014`) — `m_Upkeep` is a daily figure and 16 is the update count per day. The slice is then split at `:190-191` into `num2 = num / kMaterialUpkeep` and `num3 = num - num2`, but `num2` is never used again: only the 3/4 money share drives the renter charge (`:221`) and the condition decay (`:214`).

An exhaustive search of the decompile for writes to `ConsumptionData.m_Upkeep` finds only prefab-time initialisers (`Game.Prefabs/ZoneServiceConsumption.cs:67`, `ServiceConsumption.cs:52`) and the mode-prefab multipliers (`Game.Prefabs.Modes/ZoneServiceConsumptionGlobalMode.cs:36`), which apply on mode apply or restore rather than per tick. Nothing in the simulation writes the field at runtime.

## Rubric

- 4: Names `PropertyRenterSystem.GetUpkeep` as what computes a growable's upkeep.
- 3: Says it runs once at prefab initialisation — called from `BuildingInitializeSystem` — rather than per tick or per building instance.
- 3: Says the result is baked into the building prefab's `ConsumptionData.m_Upkeep` and never recomputed, which is why retuning the economy parameters afterwards changes nothing already written.

## Roots

- decompile
