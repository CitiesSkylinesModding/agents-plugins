# Environment and pollution

**Baseline.** Game version 1.6.0f1, established against the decompiled C# under `DecompiledCitiesSkylines2/src/` (decompiled 2026-06-24) and against the user's running city — a debug-patched development build, read live over the sibling `unity-devtools` plugin — on 2026-08-18. Wiki pages fetched 2026-08-18. The mod corpus at `cs2-third-party-mods` was read on 2026-08-18. Every live value below was read from that one install, whose DLC set is not a fact about the base game. **The live reads were taken with the simulation paused** (`SimulationSystem.selectedSpeed == 0`), which matters for one finding below and for nothing else: a cell map's contents and a parameter singleton read the same paused or running, while a managed system's cached properties do not.

**Ruled (2026-08-06, the citizens-and-households pass; conflicts.md), governing every finding in this file.** A shipped reference states **no prefab value**. It names the component and the field, with the access shape, and that is the whole of what it says about the magnitude. Three things are untouched and are the spine of the topic: **C# constants ship, as numbers** — a `const` or `static readonly` the consuming code compiled in, offline-checkable and citable to a line; **formulas ship whole** — the expression a system evaluates, its baseline, its step functions and the shape of its randomisation are invariant structure rather than balance; and **the component-to-number-family map ships whole**, because an agent cannot perform the read without it, and it carries the access shape beside each component since a reader cannot write the call from a field name. Two consequences bind this topic: **a derived ratio is a magnitude wearing a mechanism's clothes** (where the ratio is the point, the reference states the direction and names the field), and **an adverb carrying the same magnitude counts** — "far more", "roughly twice", "an order of magnitude" are the ratio in prose. A non-numeric prefab value is still a prefab value. This file records the live numbers freely; the reference will not.
The bite here is concentrated but total: **every emission magnitude, radius, fade rate, advection speed and notification threshold in the topic lives on one singleton, `PollutionParameterData`**, and none of the twenty-six fields ships as a number. Same for `SoilWaterParameterData`, `ExtractorParameterData`, `AttractivenessParameterData`, the groundwater half of `WaterPipeParameterData`, and every per-prefab `PollutionData`, `NetPollutionData`, `ZonePollutionData`, `WeatherPhenomenonData` and `WaterSourceData`. What ships is the map from mechanism to component, plus the formulas, plus the genuine C# constants — of which this topic has an unusually large number, and they are listed as such below.

**Ruled (2026-08-08, the zoning-buildings-and-land-value pass; conflicts.md), governing this file's parameter-prefab classes.** A field initializer on a prefab class is a Unity-serialized default the shipped asset overrides. It ships as **the field, never as the figure**, and a reference whose map or traps send a reader into a file carrying them states once, as a trap, that these are Unity-serialized defaults the shipped asset overrides, with nothing in the C# marking which survived. The test is what consumes the value: a `const` or `static readonly` the code compiled in ships as a number; a public field on a `ScriptableObject`-derived class does not, whatever file declares it.
`PollutionPrefab` (`src/Game/Game.Prefabs/PollutionPrefab.cs:11-65`) initializes every field with `[Tooltip]`s in the authors' own words, and `LateInitialize` copies them straight into `PollutionParameterData` (`:73-106`). Read live at 1.6.0f1, **seventeen of the twenty-three numeric fields differ from their initializer and two of them by more than two orders of magnitude**: `m_GroundRadius` 500 against an initialized 150, `m_AirRadius` 100 against 75, `m_NoiseRadius` 600 against 200, `m_NetNoiseRadius` 3 against 50, `m_WindAdvectionSpeed` 30 against 8, `m_AirFade` **5000** against 5, `m_GroundFade` **4000** against 10, `m_GroundMultiplier` 20 against 25, `m_AirMultiplier` 40 against 25, `m_NoiseMultiplier` 250 against 100, `m_NetAirMultiplier` 1 against 25, `m_NetNoiseMultiplier` 2 against 100, `m_DistanceExponent` 1.5 against 2, the three notification limits −7 against −5, and `m_HomelessNoisePollution` 50 against 100. Only `m_PlantAirMultiplier`, `m_PlantGroundMultiplier`, `m_PlantFade`, `m_FertilityGroundMultiplier`, `m_AbandonedNoisePollutionMultiplier` and `m_GroundPollutionLandValueDivisor` survive (the remaining three fields are `NotificationIconPrefab` references rather than numbers). A reader citing `PollutionPrefab.cs` gets a wrong number more often than a right one, and the two fade rates are wrong by factors of 1000 and 400.

**Ruled (2026-08-06, the citizens-and-households pass; conflicts.md), governing this file's use of the wiki.** No mechanics reference borrows a wiki stat table's numbers **at all** — first-party or nothing. The bound tables here are `Service buildings`' per-building pollution columns and the `Maps` page's per-map resource endowments; both are barred. What the wiki is used for below is relationships and the agenda, and where it is wrong the verdict says so.

---

## Findings

### One mechanism carries every map layer in this topic, and it is `CellMapSystem<T>`

`Game.Simulation.CellMapSystem<T> : GameSystemBase where T : struct, ISerializable` (`src/Game/Game.Simulation/CellMapSystem.cs:11`) is the whole of the game's cell-map machinery. Everything a mod needs is on it:

- **`public static readonly int kMapSize = 14336`** (`:100`) — the playable map's extent in metres, one axis. A C# constant; it ships. `WaterSystem.kMapSize` is a separate declaration of the same 14336 (`src/Game/Game.Simulation/WaterSystem.cs:354`).
- **`CreateTextures(int textureSize)`** (`:213-217`) allocates `textureSize * textureSize` cells as a `NativeArray<T>` in `Allocator.Persistent` and sets `m_TextureSize`. Every subclass calls it in `OnCreate` with its own `kTextureSize`, so **cell resolution is per layer and the world extent is shared**.
- **`GetMap(bool readOnly, out JobHandle dependencies)`** (`:154-158`) returns the raw array plus the handle you must respect: `readOnly` gives you the write dependency alone, `readOnly: false` gives you writes *and* reads combined.
- **`GetData(bool readOnly, out JobHandle dependencies)`** (`:160-169`) returns a `CellMapData<T> { m_Buffer, m_CellSize, m_TextureSize }` (`src/Game/Game.Simulation/CellMapData.cs:7-14`) — the same array with its geometry attached, which is what a Burst job wants because `m_CellSize` is computed as `(float2)kMapSize / (float2)m_TextureSize` and needs no constant of its own.
- **`AddReader(JobHandle)`** (`:171-174`) combines into the read chain; **`AddWriter(JobHandle)`** (`:176-179`) *replaces* the write chain. That asymmetry is load-bearing: two systems writing the same map in the same frame without ordering will lose one of them, and the game never does it.
- **Coordinate conversion is static and public**: `GetCellCoords(float3 position, int mapSize, int textureSize)` returns `(0.5f + position.xz / mapSize) * textureSize` (`:203-206`), `GetCell` floors it (`:208-211`), `GetCellCenter(int index, int textureSize)` and `GetCellBounds` invert it (`:181-201`). The map is centred on the world origin: cell 0 is at `-kMapSize/2`.
- **Serialization is the base class's**, through `Serialize<TWriter>`/`Deserialize<TReader>`/`SetDefaults` (`:110-152`), each scheduling a Burst job against the dependency chain. Every subclass declares `IJobSerializable` and gets save/load for nothing. `SetDefaults` zeroes the map unless the subclass overrides it.

**Fifteen classes derive from it**, and that set is a sweep result the reader can re-derive with one grep for `: CellMapSystem<` over `src/`:
`AirPollutionSystem`, `GroundPollutionSystem`, `NoisePollutionSystem`, `GroundWaterSystem`, `SoilWaterSystem`, `NaturalResourceSystem`, `WindSystem`, `TerrainAttractivenessSystem`, `LandValueSystem`, `PopulationToGridSystem`, `TelecomCoverageSystem`, `TrafficAmbienceSystem`, `ZoneAmbienceSystem`, `AvailabilityInfoToGridSystem` (all in `src/Game/Game.Simulation/`), plus `Game.Tools.TelecomPreviewSystem` (`src/Game/Game.Tools/TelecomPreviewSystem.cs:20`), which is the tool-preview shadow of the telecom one and the only non-simulation member.

Resolutions, each a `public static readonly int kTextureSize` on its own system and therefore a shipping C# constant:

| Layer | System | `kTextureSize` | Cells | Metres/cell |
|---|---|---|---|---|
| Ground pollution | `GroundPollutionSystem.cs:45` | 256 | 65,536 | 56 |
| Air pollution | `AirPollutionSystem.cs:72` | 256 | 65,536 | 56 |
| Noise pollution | `NoisePollutionSystem.cs:50` | 256 | 65,536 | 56 |
| Groundwater | `GroundWaterSystem.cs:123` | 256 | 65,536 | 56 |
| Natural resources | `NaturalResourceSystem.cs:302` | 256 | 65,536 | 56 |
| Soil water | `SoilWaterSystem.cs:220` | 128 | 16,384 | 112 |
| Terrain attractiveness | `TerrainAttractivenessSystem.cs:83` | 128 | 16,384 | 112 |
| Land value | `LandValueSystem.cs:258` | 128 | 16,384 | 112 |
| Availability info | `AvailabilityInfoToGridSystem.cs:138` | 128 | 16,384 | 112 |
| Telecom coverage | `TelecomCoverageSystem.cs` (literal `128` throughout, e.g. `:211-212`) | 128 | 16,384 | 112 |
| Wind | `WindSystem.cs:43` | 64 | 4,096 | 224 |
| Population | `PopulationToGridSystem.cs:90` | 64 | 4,096 | 224 |
| Traffic ambience | `TrafficAmbienceSystem.cs:28` | 64 | 4,096 | 224 |
| Zone ambience | `ZoneAmbienceSystem.cs:28` | 64 | 4,096 | 224 |

All six of the following lengths were confirmed live at 1.6.0f1 by reading `GetMap(true, out var deps).Length`: ground pollution 65536, natural resources 65536, groundwater 65536, soil water 16384, terrain attractiveness 16384, wind 4096.

**Three of these resolutions are asserted equal at runtime.** `NaturalResourceSystem.OnUpdate` opens with `Assert.AreEqual(GroundPollutionSystem.kTextureSize, kTextureSize, "Ground pollution and Natural resources need to have the same resolution")` and the same for noise (`NaturalResourceSystem.cs:416-418`), because its regeneration job indexes all three buffers with one running index rather than converting coordinates (`:132-134`). A mod that changes one of those constants breaks the other two.

`Rots:` every component, system and field name in this file, and every `kTextureSize`. Re-check against `src/Game/Game.Simulation/`, `src/Game/Game.Prefabs/`, `src/Game/Game.Prefabs.Climate/`, `src/Game/Game.Events/`, `src/Game/Game.Areas/`, `src/Game/Game.Objects/` and `src/Game/Game.Common/SystemOrder.cs`, at the files cited beside each claim.

### The five pollution kinds are not five cell maps, and that is the first thing a reader gets wrong

The wiki opens with "There are five types of pollution in *Cities: Skylines II*: ground, air, water, groundwater and noise pollution" (https://cs2.paradoxwikis.com/index.php?title=Pollution&action=raw). The five exist; they are stored four different ways.

- **Ground, air and noise are cell maps**, one `short` each. `GroundPollution { m_Pollution }` (`src/Game/Game.Simulation/GroundPollution.cs:6-8`), `AirPollution { m_Pollution }` (`src/Game/Game.Simulation/AirPollution.cs:6-8`), `NoisePollution { m_Pollution, m_PollutionTemp }` (`src/Game/Game.Simulation/NoisePollution.cs:6-10`). All three implement `IPollution`, whose whole surface is `void Add(short amount)` (`src/Game/Game.Simulation/IPollution.cs:3-6`), and all three saturate at 32767 on add.
- **Groundwater pollution is a field on the groundwater cell**, not a map of its own: `GroundWater { m_Amount, m_Polluted, m_Max }`, all `short` (`src/Game/Game.Simulation/GroundWater.cs:6-12`). `m_Polluted` is an absolute quantity bounded by `m_Amount`, so the *concentration* is `m_Polluted / m_Amount` and must be derived.
- **Surface-water pollution is not on the CPU at all.** It is the `w` channel of the GPU water texture, surfaced to the CPU as `SurfaceWater { m_Depth, m_Polluted, m_Velocity }`, a struct constructed from a `float4` read back from a render texture (`src/Game/Game.Simulation/SurfaceWater.cs:5-12`). See the surface-water finding below.
- **Piped-water pollution is per graph element**, a `float` fraction on `WaterPipeEdge.m_FreshPollution` and `WaterPipeNode.m_FreshPollution`. That half belongs to `utilities-and-flow-networks`; what this topic owns is where the number enters the pipe graph, which is the pumping station's intake.

So a mod asking "how polluted is this spot" writes four different reads. Each cell-map layer publishes its own **static bilinear sampler**, and they do not agree with each other:

- `GroundPollutionSystem.GetPollution(float3, NativeArray<GroundPollution>)` (`GroundPollutionSystem.cs:65-80`) samples at the *cell corner*: it takes `GetCell(position, …)` directly, treats out-of-range as zero rather than clamping, and treats the +1 neighbours as zero at the far edge.
- `AirPollutionSystem.GetPollution` (`AirPollutionSystem.cs:94-107`) and `NoisePollutionSystem.GetPollution` (`NoisePollutionSystem.cs:66-79`) sample at the *cell centre*: they offset the position by half a cell before flooring and then **clamp only the cell index to `[0, kTextureSize - 2]`** while the bilinear fraction stays raw (`float5.x - (float)cell.x` after the clamp), so a sample outside the map — or in the outermost half-cell ring inside it — extrapolates from the two edge cells and can come back negative or overshooting, not the edge value. (Corrected 2026-08-18 by the gate; this file first wrote "returns the edge value".)
- `GroundWaterSystem.GetGroundWater` (`GroundWaterSystem.cs:157-176`) is centre-sampled but **unclamped**: an off-map neighbour reads as an all-zero cell through the private `GetGroundWater(map, cell)` guard (`:226-233`), like ground pollution and unlike air. The interpolation cannot make `m_Polluted` exceed `m_Amount`: each cell is clamped to `m_Polluted <= m_Amount` (`:112`) and the same bilinear weights apply to both fields.
- `NaturalResourceSystem.GetResource` (`NaturalResourceSystem.cs:529-548`) is centre-sampled with the same index-only clamp — the fraction extrapolates like air's, and the result goes through an unchecked `(ushort)` cast (`:546`), so a negative extrapolation wraps huge — and is exposed as four wrappers, `GetFertilityAmount`, `GetOilAmount`, `GetOreAmount`, `GetFishAmount` (`:509-527`). **They take a `Func<NaturalResourceCell, NaturalResourceAmount>` delegate**, so they are managed-only: a Burst job cannot call them and must index the buffer itself, which is what the game's own jobs do.

**The trap is that "out of bounds" means two different things by layer.** A ground-pollution sample off the map is 0; an air or noise sample off the map is the nearest edge cell's value.

### Emission: one system writes ground, air and noise, and the formula is a distance-weighted stamp

`BuildingPollutionAddSystem` (`src/Game/Game.Simulation/BuildingPollutionAddSystem.cs`, registered at `SystemUpdatePhase.GameSimulation`, `src/Game/Game.Common/SystemOrder.cs:426`) is the only writer of building emission into all three maps. It runs at `262144 / (16 * kUpdatesPerDay)` = 128 frames with `kUpdatesPerDay = 128` (`:363`, `:391-394`), sharded 16 ways over `UpdateFrame`, so each building contributes once per 2048 frames.

**Per-building amount** — `GetBuildingPollution` (`:524-584`), a public static a mod can call and the render side does call (`src/Game/Game.Rendering/ObjectColorSystem.cs:1109`):

1. Base is the prefab's `PollutionData { m_GroundPollution, m_AirPollution, m_NoisePollution, m_ScaleWithRenters }` (`src/Game/Game.Prefabs/PollutionData.cs:7-15`), and it is skipped entirely when `efficiency <= 0` (`:529`).
2. Installed upgrades add their own `PollutionData` through `UpgradeUtils.CombinePollutionStats` (`:531-534`); `PollutionData.Combine` is a plain sum of the three fields (`PollutionData.cs:32-37`).
3. **Renter scaling, when `m_ScaleWithRenters` and the building is not a park** (`:536-544`): `count` and `education` are summed over every household citizen and every employee of every renter (`CountRenters`, `:586-619`), then
   `factor = count > 0 ? 5 * count / (level + 0.5 * floor(education / count)) : 0` — `education / count` is C# integer division, truncated before the float cast (`:540`)
   where `level` is `SpawnableBuildingData.m_Level` or `5` for a non-spawnable. All three magnitudes are multiplied by it. So **emission rises with occupancy and falls with building level and with the occupants' education**, and an empty building emits nothing at all.
4. **City modifiers, for industrial non-office zones only** (`:545-553`): `CityModifierType.IndustrialGroundPollution` and `IndustrialAirPollution` are applied when the prefab's zone is `AreaType.Industrial` without `ZoneFlags.Office`. Noise is not modified.
5. **Upgrade multipliers** (`:554-561`): `PollutionModifierData` summed over the installed upgrades, applied as `max(0, 1 + m)` per channel. `PollutionModifier` is `[ComponentRequirement(typeof(ServiceUpgrade))]` and its three fields are `[Range(-1f, 1f)]` (`src/Game/Game.Prefabs/PollutionModifier.cs:14-27`), so an upgrade can zero a channel and cannot reverse it. The class logs an error if it is attached to anything that is not a service upgrade (`:40-43`).
6. **Destroyed or abandoned short-circuits everything above** (`:568-577`): ground and air become 0, and noise becomes `5 * lotSize.x * lotSize.y * m_AbandonedNoisePollutionMultiplier` for an abandoned building and 0 for a destroyed one.
7. **Abandoned or park buildings add homeless noise** (`:578-582`): `count * m_HomelessNoisePollution`, counting household citizens only (`ignoreEmployees: true`).
8. **Finally, per-instance `PollutionEmitModifier`** (`:235-238`): `pollutionData.m_X += m_XModifier * pollutionData.m_X` for each channel (`src/Game/Game.Buildings/PollutionEmitModifier.cs:15-20`). This component is added to **every building prefab that carries `Pollution`** (`src/Game/Game.Prefabs/Pollution.cs:39-42`), it is `ISerializable`, and the game writes it at runtime — `BatteryAISystem` sets all three to `-1` (which zeroes the emission) while an emergency generator is idle and to `0` while it runs (`src/Game/Game.Simulation/BatteryAISystem.cs:139-146`). **It is the supported per-instance emission hook and the shortest route for a mod that wants to change one building's pollution without touching its prefab.** It is applied after everything else, including the renter scaling, so it is a fraction of the already-scaled value.

**Spatial stamp** — `ApplyBuildingPollutionJob<T>.AddSingle` (`:69-116`), one instantiation per channel:

```
radiusSq = radius²
for each cell whose centre is within `radius` of the source:
    d²      = |cellCentre - sourcePos|²
    weight  = lerp over a 256-entry cache indexed by 255 * d² / maxRadiusSq
total       = Σ weight
per-cell    = pollution * multiplier * weight / (total * kUpdatesPerDay)
if per-cell > 0.2:  map[cell].Add((short)ceil(per-cell))
```

The weight cache is built in `OnUpdate` and rebuilt only when `m_DistanceExponent` changes (`:429-442`), from
**`GetWeight(distance, exponent) = 1 / max(20, pow(distance, exponent))`** (`:519-522`) — a public static and a genuine C# formula. The `max(20, …)` is what flattens the kernel near the source rather than letting it diverge. `maxRadiusSq` is the square of the largest of the three radii (`:429-430`), so **all three channels share one cache and one distance quantisation, and a channel with a small radius uses only the first few entries of a 256-entry table** — the near-field resolution of a short-radius channel is therefore coarse by construction.

Three details a mod will hit:
- **The `> 0.2` threshold discards a cell's whole contribution**, and `Mathf.CeilToInt` rounds every surviving cell up. A weak source spread over many cells deposits nothing; one just above the threshold deposits a whole unit per cell.
- **The per-cell weights are normalised to sum to one**, so `pollution * multiplier / kUpdatesPerDay` is the budget before the threshold and the ceil — what actually lands still moves with radius, since widening it pushes more cells under the `> 0.2` cut while the ceil rounds every survivor up.
- The job's temporary weight buffer is `stackalloc float[n*n]` with `n = 3 + ceil(2 * radius * textureSize / mapSize)` (`:120-121`). Raising a radius parameter raises a stack allocation inside a Burst job.

**Zoned buildings get their emission from the zone, scaled by lot size.** `ZonePollution` is a `ZonePrefab` component whose `GetBuildingPollutionData` returns `m_XPollution * buildingPrefab.lotSize` per channel, and `InitializeBuilding` applies it **only when the building prefab does not itself carry `Pollution`** (`src/Game/Game.Prefabs/ZonePollution.cs:50-67`). So a zoned building's `PollutionData` is authored once per zone type and per lot size, not per building.

### Air pollution: advected by wind, diffused by a shift, faded by a constant

`AirPollutionSystem` (`src/Game/Game.Simulation/AirPollutionSystem.cs:17`, `SystemOrder.cs:428`) runs every `262144 / 128` = 2048 frames (`:84-87`) as a single non-parallel `IJob` over all 65,536 cells (`:20-67`). The whole formula, in order:

**Advection** (`:37-46`). For each cell, sample the wind at the cell centre, then read the pollution from `cellCentre - m_WindAdvectionSpeed * float3(wind.x, 0, wind.y)` — the *upwind* point — with the bilinear sampler, and write that into a scratch buffer. So the field is transported by semi-Lagrangian backtracking, one full advection step per update, and `m_WindAdvectionSpeed` is in metres per update rather than a velocity.

**Diffusion and fade** (`:48-65`), over the scratch buffer into the live map:

```
kSpread = 3                                        // AirPollutionSystem.cs:70, private static readonly
p  = scratch[i]
p += scratch[left]  >> kSpread                     // edge cells contribute 0
p += scratch[right] >> kSpread
p += scratch[down]  >> kSpread
p += scratch[up]    >> kSpread
p -= scratch[i] >> (kSpread - 2)                   // == scratch[i] >> 1, i.e. half of itself
p -= RoundToIntRandom(random, m_AirFade / kUpdatesPerDay)
map[i] = clamp(p, 0, 32767)
```

Two things follow that a reader will not guess. **The diffusion is not conservative**: each cell gives away `p >> 1` and receives `4 * (p >> 3)` from its neighbours, which balances only on a uniform field; on a peak it loses mass and at a boundary it loses mass outright, because an off-map neighbour contributes zero while the centre still pays its half. **The fade is a flat subtraction, not a decay**, and it is stochastically rounded — `MathUtils.RoundToIntRandom` turns the fractional part into a per-cell coin flip, so the field does not quantise to a staircase. `m_AirFade` is a `short` on the parameter singleton (live 5000, `/128` per update ≈ 39 per update).

**There is no rain term, no temperature term and no climate input of any kind.** `AirPollutionSystem` holds a `WindSystem` and a `SimulationSystem` and nothing else (`:76-78`).

**`Verdict:` the wiki's "Rain will reduce air pollution" is false at 1.6.0f1.** The claim is at https://cs2.paradoxwikis.com/index.php?title=Pollution&action=raw ("Air pollution travels with wind direction and expands and dilutes as it mixed with unpolluted air. Rain will reduce air pollution"). The advection and dilution halves are exactly right and are the formula above. The rain half has no implementation: the writers of the air map are `BuildingPollutionAddSystem` (`:491`, `:501`), `NetPollutionSystem` (`src/Game/Game.Simulation/NetPollutionSystem.cs:472`, `:479`), `AirPollutionSystem` itself, and the debug menu's `Game.Debug/DebugSystem.cs:4437-4454` `ResetPollution`, which zeroes it — a census taken by grepping for `GetMap(readOnly: false`, `GetData(readOnly: false` and `AddWriter` over `src/`, and none of them reads `ClimateSystem`. The decompile is authoritative for C# and nothing overturns it. The wiki page carries `{{Version|1.0}}` and sits in `Category:Potentially outdated`, so this may have been true before launch.

### Ground pollution: fade and nothing else

`GroundPollutionSystem` (`src/Game/Game.Simulation/GroundPollutionSystem.cs:17`, `SystemOrder.cs:425`) runs on the same 2048-frame interval (`:55-58`) and its entire update is one job (`:20-43`):

```
if map[i].m_Pollution > 0:
    map[i].m_Pollution = max(0, map[i].m_Pollution - RoundToIntRandom(random, m_GroundFade / kUpdatesPerDay))
```

**No diffusion, no advection, no interaction with water, no interaction with terrain.** Ground pollution sits exactly where it was stamped and decays linearly. The system holds no reference to any other system except `SimulationSystem` (`:49`).

**`Verdict:` the wiki's "A water flow will remove ground pollution" is false at 1.6.0f1.** The claim is at https://cs2.paradoxwikis.com/index.php?title=Pollution&action=raw. The ground map's writers — `BuildingPollutionAddSystem` (`:477`, `:487`), this system's fade, and the same debug reset — come from the same census as above, and none consults water. Nothing in `WaterSystem`, `SoilWaterSystem` or `WaterSimulation` touches the ground-pollution map.

**Ordering note.** The fade is registered at `SystemOrder.cs:425` and the adder at `:426`, so within one frame ground pollution fades before the new deposits land. The same holds for air (`:428`, after the adder, so air's advect-and-fade sees the frame's deposits).

### Noise pollution: rebuilt from scratch every update, with a normalised 3×3 blur

`NoisePollutionSystem` (`src/Game/Game.Simulation/NoisePollutionSystem.cs:11`, `SystemOrder.cs:427`) is the odd one out: **noise does not accumulate or decay, it is recomputed.** Emitters write `m_PollutionTemp` (that is what `NoisePollution.Add` sets, `NoisePollution.cs:12-15`); the system then runs two parallel jobs (`:89-101`):

`NoisePollutionSwapJob` (`:14-34`) writes each cell's `m_Pollution` from the *temp* field of its 3×3 neighbourhood:

```
m_Pollution = temp[centre]/4 + (temp[N]+temp[S]+temp[E]+temp[W])/8 + (temp[4 corners])/16
```

The weights sum to `1/4 + 4/8 + 4/16 = 1`, so the kernel is normalised — a uniform field is preserved and a point source is spread without gain. Off-map neighbours read as 0.

`NoisePollutionClearJob` (`:38-48`) then zeroes every `m_PollutionTemp`.

Consequences: **the noise map is a snapshot of the current emitters, blurred once.** Remove a noisy building and its noise is gone at the next update, with no fade. `m_PollutionTemp` is nevertheless serialized alongside `m_Pollution` (`NoisePollution.cs:17-31`, stride 4), because a save can land mid-accumulation. And the accumulation window is the swap's own interval: the two contributors (`BuildingPollutionAddSystem` at 128 frames × 16 shards, `NetPollutionSystem` at 128 frames × 16 shards) each complete one full pass per 2048 frames, exactly one swap period, so every emitter contributes exactly once per snapshot.

**A mod writing noise must write `m_PollutionTemp`, not `m_Pollution`** — anything written to `m_Pollution` is overwritten at the next swap, and anything written to `m_PollutionTemp` is consumed and cleared.

### Net pollution: the vehicle is the source, the edge is the accumulator, and the road's shape is the filter

Roads and other nets carry `Game.Net.Pollution { float2 m_Pollution, float2 m_Accumulation }` (`src/Game/Game.Net/Pollution.cs:7-11`), where `.x` is noise and `.y` is air. `NetPollution` is the prefab component that requests it, and it also carries `NetPollutionData { float2 m_Factors }` = `(noiseFactor, airFactor)` per net prefab (`src/Game/Game.Prefabs/NetPollution.cs:10-35`, `src/Game/Game.Prefabs/NetPollutionData.cs:6-9`). The runtime component is added only to nodes and edges (`NetPollution.cs:21-27`).

**Vehicles deposit into it.** Each of the four navigation systems computes a per-lane side-effect triple and enqueues it; an `ApplyLaneEffectsJob` drains the queue and does `pollutionOnLaneOwner.m_Pollution += item.m_SideEffects.yz` (`src/Game/Game.Simulation/CarNavigationSystem.cs:2711-2715`, and the same line in `AircraftNavigationSystem.cs:1289`, `TrainNavigationSystem.cs:1256`, `WatercraftNavigationSystem.cs:1436`). The triple comes from the vehicle prefab's `VehicleSideEffectData { float3 m_Min, float3 m_Max }` (`src/Game/Game.Prefabs/VehicleSideEffectData.cs:7-11`), whose channels are `(wear, noise, air)` — the order `VehicleSideEffects.Initialize` fixes at `src/Game/Game.Prefabs/VehicleSideEffects.cs:30-31` — interpolated by **normalised speed squared** and scaled by time and distance (`CarNavigationSystem.cs:2159-2185`):

```
s          = clamp01((distance/duration) / prefabMaxSpeed)²      // duration 0 → use the lane's max drive speed
sideEffect = lerp(m_Min, m_Max, s) * float3(min(1, distance/curveLength), duration, duration)
```

So **wear scales with distance travelled and noise and air scale with time spent on the lane**, and the squared speed fraction is the lerp's `t` rather than a multiplier — a stopped vehicle still emits `m_Min` per second, its lane time multiplying it, while a vehicle at top speed emits `m_Max`. `CalculateNoise` (`:2145-2157`) is the same expression for the `.z` channel alone — which under the corrected order is the *air* channel, the game's own audio path reading the wrong member.

**`Verdict:` the wiki's "the heavier the traffic, the heavier the noise" and "Each type of vehicle has a noise pollution rating" are both correct**, and the second names the right artifact — the rating is `VehicleSideEffectData` on the vehicle prefab. What the wiki does not say is the speed term, which is the half a modder needs: the rating is a range, not a number.

**`NetPollutionSystem`** (`src/Game/Game.Simulation/NetPollutionSystem.cs:21`, `SystemOrder.cs:462`, interval `262144 / (128*16)` = 128 frames, 16-way sharded, `:416`, `:430-432`, `:451-453`) turns the accumulator into map deposits:

1. `m_Accumulation = lerp(m_Accumulation, m_Pollution, 4 / kUpdatesPerDay)`, then `m_Pollution = 0` (`:96-97`, `:154-155`). **The accumulator is an exponential moving average with a fixed 4/128 coefficient**, so a road's noise ramps in and out over roughly thirty updates rather than tracking the last window. `Pollution.Deserialize` seeds `m_Accumulation = m_Pollution * 2` for saves older than `Version.netPollutionAccumulation` (`Pollution.cs:25-33`).
2. `float2 emitted = m_Accumulation * NetPollutionData.m_Factors` (`:104`, `:171`).
3. **Underground nets emit nothing.** For an edge: if `Elevation` is negative on both ends and the composition carries `CompositionFlags.General.Tunnel`, the edge is skipped outright (`:165-168`). For a node the same test runs across every connected edge and the node emits only if at least one connection is not a tunnel (`:108`, `:124-127`, `:135`).
4. **Radius is the road's own width**: `radius = max(m_NetNoiseRadius, netCompositionData.m_Width * 0.5f)` (`:123`, `:164`).
5. **Upgrades multiply a `float3` of (left, centre, right) noise**, `CheckUpgrades` (`:182-228`) — a sound barrier on both sides gives `(0, 0.5, 0)`, left only `(0, 0.5, 1.5)`, right only `(1.5, 0.5, 0)`; primary and secondary beautification are **independent families that stack** (both applying gives `0.5 * 0.5` on an edge): each is `(0.5, 0.5, 0.5)` both sides, `(0.5, 0.75, 1)` left only, `(1, 0.75, 0.5)` right only; each middle beautification is its own `(0.875, 0.5, 0.875)` (`:220-227`). **A sound barrier on one side raises the far side by 1.5×** while zeroing the near side: a barrier on one side is not half a barrier.
6. **The node and curve stamps differ.** For a node (`:230-256`): the per-edge triples are averaged and the side channels folded into one as `(left + right) / 2` (`:139-140`); air is added at the node position; noise is divided by 8 (`:239`), the folded side amount landing on the four cardinal points at `±radius` and the centre amount on four inner points at `±radius/3` when the radius exceeded `m_NetNoiseRadius`, else at `4×` on the node cell. For a curve (`:258-306`): each map subdivides the curve into `ceil(2 * length / itsCellSize)` samples; air is divided evenly across its samples; noise is divided by `4 * samples` with the centre channel halved again when the radius exceeded `m_NetNoiseRadius` (`:277-281`), and lands per sample **along the curve normal only** — the centre amount on two points at `±radius/3` when wide, else on the sample point, plus left at `+radius` and right at `−radius`, each gated on non-zero (`:288-304`). `AddNoise` (`:320-334`) then splits each deposit bilinearly across four cells, while `AddAirPollution` (`:308-318`) writes one cell with no interpolation.
7. **Noise for a curve is asymmetric by construction**: the centre channel is doubled before the upgrade multipliers (`:173`, `noisePollution2.y *= 2f`), and left and right are stamped only when non-zero (`:297-304`).

Net pollution writes both the air map and the noise map directly, and registers itself as a writer of both (`:479-480`). Registered at `SystemOrder.cs:462`, i.e. **after** the noise swap at `:427`, so a frame's net deposits land in `m_PollutionTemp` and are consumed by the *next* swap.

### Cross-contamination, as the code has it

Every link that exists in C#, with its citation, against the wiki's claimed set:

| From | To | Mechanism | Where |
|---|---|---|---|
| Building / zone | ground, air, noise maps | distance-weighted stamp | `BuildingPollutionAddSystem.cs:69-116`, `:524-584` |
| Vehicle | road `Pollution` → air, noise maps | side effect → EMA → star stamp | `CarNavigationSystem.cs:2159-2185`, `NetPollutionSystem.cs:96-306` |
| Wind | air map | semi-Lagrangian advection each update | `AirPollutionSystem.cs:39-41` |
| Air map | itself | shift-based diffusion + flat fade | `AirPollutionSystem.cs:48-65` |
| Ground map | groundwater `m_Polluted` | `+= sampledGroundPollution / 200`, clamped to `m_Amount` | `GroundWaterPollutionSystem.cs:24-28` |
| Ground map | fertile land `m_Used` | `+= RoundToIntRandom(groundPollution * m_FertilityGroundMultiplier / 32)` | `NaturalResourceSystem.cs:136`, `:429` |
| Ground + air maps | tree/plant health `Plant.m_Pollution` | saturating accumulator against a fade | `ObjectPolluteSystem.cs:44-52` |
| Groundwater `m_Polluted` | piped fresh water | pump intake averages the concentration into `WaterPipeEdge.m_FreshPollution` | `WaterPumpingStationAISystem.cs:136-149`, `:178-182` |
| Sewage outlet | surface-water `m_Polluted` | sets its sub-object water source's pollution to the unpurified fraction | `SewageOutletAISystem.cs:97-121` |
| Surface water `m_Polluted` | piped fresh water | pump intake samples `WaterUtils.SamplePolluted` at its own sub-object | `WaterPumpingStationAISystem.cs:150-170` |
| Surface water depth + `m_Polluted` + noise map | fish `m_Base` and `m_Used` | see the natural-resources finding | `NaturalResourceSystem.cs:140-172` |
| Groundwater within a cell's neighbours | groundwater | pollution equalises with flow, and purifies by a flat rate | `GroundWaterSystem.cs:27-77`, `:108-114` |
| Piped fresh water pollution | citizen health, building efficiency | `WaterConsumer.m_Pollution` | `CitizenHappinessSystem.cs:1146-1158`, `DispatchWaterSystem.cs:142-176` |
| Ground / air maps | citizen health | happiness bonus | `CitizenHappinessSystem.cs:1216-1240` |
| Noise map | citizen wellbeing | happiness bonus | `CitizenHappinessSystem.cs:1242-1251` |
| All three maps | land value | weighted penalty | `LandValueSystem.cs:123-140` |
| All three maps | zone spawning, rent, property choice | see the readers census | below |

**Links the wiki claims that do not exist**: rain reducing air pollution; water flow removing ground pollution; ground pollution "if near water" causing water pollution (there is no ground-map → surface-water path at all — the only pollution inlet into the water texture is a source with `m_Polluted > 0` via the `Add` kernel at `WaterSimulation.cs:531-537`, and the simulation's own writer of one is the sewage outlet's sub-object; the authoring `WaterSource` component and the editor's water tool can author one too). All three at https://cs2.paradoxwikis.com/index.php?title=Pollution&action=raw.

**A link the wiki does not claim and the code has**: the **noise map raises fish pollution**. `NaturalResourceSystem.cs:154` folds `noisePollution.m_Pollution * 6.25e-05f` into the water-pollution term that suppresses fish. Noise touches nothing else outside citizen wellbeing.

**A link the wiki claims in the wrong direction**: "When ground pollution comes in contact with groundwater it will quickly pollute the **entire deposit**". Groundwater is per-cell, not per-deposit; contamination is per-cell at `pollution/200` per update and spreads to neighbours only through `HandlePollution`'s concentration equalisation, which moves at most a quarter of the difference per update (`GroundWaterSystem.cs:35`).

### Groundwater: a three-field cell map with flow, pollution equalisation, replenishment and purification

`GroundWaterSystem` (`src/Game/Game.Simulation/GroundWaterSystem.cs:18`, `SystemOrder.cs:405`) runs every 128 frames at offset 64 (`:127-135`) as one serial `IJob` over the 256² map, in three passes (`:79-116`).

**Pass 1, pollution equalisation** (`HandlePollution`, `:27-42`), between each cell and its east and south neighbours:

```
targetPollutedHere = amountHere * (pollutedHere + pollutedThere) / (amountHere + amountThere)
delta = clamp((targetPollutedHere - pollutedHere) / 4,
              -(amountThere - pollutedThere) / 4,
               (amountHere  - pollutedHere ) / 4)
```

So pollution moves toward equal *concentration*, a quarter of the way per pair per update, bounded by the clean water available on each side.

**Pass 2, flow** (`HandleFlow`, `:44-77`), same neighbour pairs:

```
headHere  = amountHere  - maxHere
headThere = amountThere - maxThere
flow = clamp((headThere - headHere) / 4, -amountHere / 4, amountThere / 4)
```

Water flows from the cell furthest above its own `m_Max` toward the one furthest below, a quarter of the difference per update, and **carries pollution at the source cell's concentration** (`:58-68`). Note the sign: `m_Amount` can exceed `m_Max` transiently and the head is measured relative to each cell's own capacity, so a high-capacity cell pulls from a low-capacity one at equal absolute fill.

**Pass 3, replenish and purify** (`:108-114`):

```
m_Amount   = min(m_Amount   + flow + ceil(m_GroundwaterReplenish * m_Max), m_Max)
m_Polluted = clamp(m_Polluted + pollutionDelta - m_GroundwaterPurification, 0, m_Amount)
```

Both rates live on `WaterPipeParameterData` (`m_GroundwaterReplenish`, `m_GroundwaterPurification`; live 0.05 and 1). **Replenishment is a fraction of the cell's `m_Max`, purification is a flat absolute subtraction.** The `ceil` means any positive replenish rate refills a non-empty cell by at least one unit per update.

**`Verdict:` the wiki's "The rate at which the deposit is decontaminated is the same as its water replenishment rate" is false at 1.6.0f1.** The claim is at https://cs2.paradoxwikis.com/index.php?title=Natural_resources&action=raw. They are two independent fields on one component, one proportional and one absolute; nothing in the code ties them.

**Two named constants sit here and neither is read.** `GroundWaterSystem.kMaxGroundWater = 10000` and `kMinGroundWaterThreshold = 500`, both `public const int` (`:119-121`); a grep for either name over `src/` returns only the declaration. They read as the intended per-cell ceiling and a low-water threshold, and the enforced bound is each cell's own `m_Max`. **Ruled (2026-08-18, this pass; conflicts.md): an unread constant ships as no number** — the full ruling sits at the natural-resources finding below, and this pair is two of its eleven instances.

**Consumption is a public static with an unusual contract.** `ConsumeGroundWater(float3 position, NativeArray<GroundWater>, int amount)` (`:178-224`) bilinearly splits the draw across the four cells around the position in proportion to what each holds, calls `GroundWater.Consume` on each, and **logs a `Debug.LogWarning` when the caller asks for more than is available** (`:198-201`) rather than failing. `GroundWater.Consume` preserves the cell's pollution concentration while reducing both figures (`GroundWater.cs:14-22`) — pumping does not clean the aquifer.

**The map's own generator is dead code for a current save.** `SetDefaults` fills the map from `10000 * saturate((PerlinNoise(32u, 32v) - 0.6) / 0.4)` **only when `context.purpose == Purpose.NewGame && context.version < Version.timoSerializationFlow`** (`:269-288`); at the current save version it falls through to the base class, which zeroes the map. Groundwater on a modern map therefore comes from the map asset — painted or imported in the editor — and the wiki's `Map Creation: Resources` page confirms the editor writes it as one of four paintable resource layers (https://cs2.paradoxwikis.com/index.php?title=Map_Creation:_Resources&action=raw). `NaturalResourceSystem.SetDefaults` has the same shape *without* the version gate (`NaturalResourceSystem.cs:361-399`), so a new game still gets Perlin-generated fertility, ore and oil unless the map overrides them.

Settled (2026-08-18, live): a shipped map's groundwater arrives through `Deserialize`. A fresh new game on a base map, before any tool input, read a sparse nonzero aquifer through `GroundWaterSystem.GetMap` (sampled cells 24/24, 174/174, 6/6, 4/4 — each sampled non-empty cell at `m_Amount == m_Max`), and `ApplyBrushesSystem` registers only into `SystemUpdatePhase.ApplyTool` (`SystemOrder.cs:718`), which fires on tool input and never during a load — so the serialization path is the only living candidate, and the editor brushes are how the author painted what the map asset serialized.

### Surface water is a GPU simulation, and that is the hardest constraint in this topic

`WaterSystem` (`src/Game/Game.Simulation/WaterSystem.cs:34`) is an `IGPUSystem`, registered at `SystemUpdatePhase.PostSimulation` (`SystemOrder.cs:630`) and as a GPU system (`SystemOrder.cs:46`). The simulation itself is `Game.Simulation.WaterSimulation` (`src/Game/Game.Simulation/WaterSimulation.cs:22`), a plain class holding **one compute shader and thirty-one kernels** — `VelocityUpdate`, `DepthUpdate`, `Add`, `AddConstant`, `Evaporate`, `CSDownsample`, blur passes, sea-propagation passes and the rest (`:275-330`). The CPU never runs the fluid step.

What the CPU gets is an **asynchronous readback**. `WaterSystem.GetSurfaceData(out JobHandle deps)` returns a `WaterSurfaceData<SurfaceWater>` built by a `SurfaceDataReader` over the `R32G32B32A32_SFloat` water texture (`:754-756`, `:917`), and `SurfaceWater` unpacks that `float4` as `m_Depth = max(data.x, 0)`, `m_Velocity = (data.y, data.z)`, `m_Polluted = data.w` (`src/Game/Game.Simulation/SurfaceWater.cs:5-12`). There are three readers: depths, velocities and max height (`:917-919`), plus a backdrop pair (`:1051`).

**So a mod can read surface water and cannot write it.** The write side is the *source* list: `WaterSimulation.SourceJob` copies every entity carrying `Game.Simulation.WaterSourceData` and a `Transform` into a `NativeList<WaterSourceCache>` (`:25-81`), and the next frame's `SourceStep` dispatches one kernel per cached source (`:520-561`). Editing `WaterSourceData` on an entity is the whole supported surface, and it is what the corpus's only water mod does.

**`WaterSourceData` is a mode switch, and `m_Polluted` is the switch** (`:531-559`):

```
if (m_Polluted > 0)   Add kernel:         amount = 0.3165952 * SimulationCycleSteps * m_Height,  polluted = m_Polluted
else                  AddConstant kernel: target level = m_Position.y + m_Height   (or a lerp toward -1 when m_Height < 0)
```

So a clean source **holds a water level** and a polluted source **injects a volume per step**, with `m_Height` reinterpreted as the output rate. **`Verdict:` the wiki's `Map Creation: Water` page states exactly this** — "Raising the pollution level turns the water source into a sewer source. The Height value stops acting as a level for the water source to target, instead acting as an output rate for pollution" (https://cs2.paradoxwikis.com/index.php?title=Map_Creation:_Water&action=raw) — and the decompile confirms it as a kernel selection. That page is `{{ParadoxVerifiedAmbox|version=1.6.0 f1}}` and is not in `Category:Potentially outdated`; it is the only wiki material in this topic that is both current and mechanically precise.

**A source whose cached radius is 0, or which lies outside the terrain bounds, is skipped entirely** (`:523`), and the cached radius is already the product `m_Radius * m_Modifier` (`:59`) — which is what makes a zero `m_Modifier` an off switch. Both facts are load-bearing for mods: the corpus's `Water Features` manufactures a zero-radius sea-level anchor precisely because the simulation ignores it (`Water_Features/Systems/TidesAndWavesSystem.cs:194-238`), and `SewageOutletAISystem` uses `m_Modifier` as an on/off gate rather than deleting the source (`src/Game/Game.Simulation/SewageOutletAISystem.cs:109-118`).

The runtime component is `Game.Simulation.WaterSourceData { m_ConstantDepth, m_Radius, m_Height, m_Multiplier, m_Polluted, m_Id, m_Modifier }` (`src/Game/Game.Simulation/WaterSourceData.cs:6-20`); `m_Modifier` is **not serialized** and is reset to 1 on deserialize (`:72`). The authoring side is `Game.Prefabs.WaterSource { m_Radius, m_Height, m_Polluted }` → `Game.Prefabs.WaterSourceData { m_Radius, m_height, m_InitialPolluted }` (`src/Game/Game.Prefabs/WaterSource.cs:9-36`, `src/Game/Game.Prefabs/WaterSourceData.cs:5-12`), copied into the instance by `WaterSourceInitializeSystem` (`src/Game/Game.Simulation/WaterSourceInitializeSystem.cs:43`). Note the two component types share the name `WaterSourceData` across two namespaces and are different structs; `Water_Features` disambiguates it in every file.

**The sewage → intake chain in full.** `SewageOutletAISystem` sets, on each of its sub-object water sources: `m_Height = min(2.5, m_SurfaceWaterUsageMultiplier * total)` and `m_Polluted = unpurified / total` where `total = (processed - purified) + (purified - usedPurified)`, with `m_Modifier` set to 0 when nothing is flowing (`src/Game/Game.Simulation/SewageOutletAISystem.cs:97-121`). The GPU advects it. `WaterPumpingStationAISystem` then reads at its own sub-object: `WaterUtils.SamplePolluted(ref surfaceData, position)` weighted by the drawn amount, plus the groundwater half at `m_Polluted / max(1, m_Amount)`, and publishes `m_Pollution = (1 - m_Purification) * weightedPollution / capacity` onto the producer edge as `m_FreshPollution` (`src/Game/Game.Simulation/WaterPumpingStationAISystem.cs:136-182`). **`m_MaxToleratedPollution` on `WaterPipeParameterData` (live 0.05) is the notification threshold** (`:195`), and in `DispatchWaterSystem` it gates only the consumer's dirty-water notification (`src/Game/Game.Simulation/DispatchWaterSystem.cs:142`, `:160`); the efficiency penalty is ungated and proportional, `1 - BuildingEfficiencyParameterData.m_WaterPollutionPenalty * round(pollution * 100) / 100` (`:176-177`).

### Natural resources: four amounts per cell, and only two of them mean "extracted"

`NaturalResourceCell { m_Fertility, m_Ore, m_Oil, m_Fish }`, each a `NaturalResourceAmount { ushort m_Base, ushort m_Used }` (`src/Game/Game.Simulation/NaturalResourceCell.cs:6-14`, `src/Game/Game.Simulation/NaturalResourceAmount.cs:5-9`). **Available = `m_Base - m_Used`** everywhere it is read (`AreaLotSimulationSystem.cs:762-779`, `Game.Areas/AreaResourceSystem.cs:527-547`, `Game.Rendering/OverlayInfomodeSystem.cs:222-341`, `Game.UI.Tooltip/TempExtractorTooltipSystem.cs:255-275`). `MAX_BASE_RESOURCES = 10000` is a `public const int` on the system (`NaturalResourceSystem.cs:292`) with **no reader in `src/`** — the jobs use the literal `10000f` (`:155-156`, `:379`) — but unlike the other unread constants here it agrees with the literal, and with the data: read live, one sampled cell carries `m_Ore.m_Base == 10000` and another `m_Fish.m_Base == 10000`.

**`MapFeature` is the enum that names the layers**, and it is wider than the cell struct: `None = -1, Area, BuildableLand, FertileLand, Forest, Oil, Ore, SurfaceWater, GroundWater, Fish, Count` (`src/Game/Game.Areas/MapFeature.cs:3-16`). Four of those are `NaturalResourceCell` fields; `Forest` is derived from placed trees, `GroundWater` from `GroundWaterSystem`, `SurfaceWater` from the GPU depths, and `Area`/`BuildableLand` from geometry. An area entity carries the results as a `MapFeatureElement { m_Amount, m_RenewalRate }` buffer with `[InternalBufferCapacity(9)]` — one slot per enum member (`src/Game/Game.Areas/MapFeatureElement.cs:22-27`), which is where the info view's per-district figures come from.

**The four channels behave differently, and "used" means something different in each.**

- **Fertility's `m_Used` is a pollution scar, not extraction alone.** `NaturalResourceSystem`'s regeneration job (`:118-176`, one row per `IJobParallelFor` index) does
  `m_Fertility.m_Used = min(m_Base, max(0, m_Used - fertilityRegenerationRate + RoundToIntRandom(groundPollution * m_PollutionRate)))`
  with `m_PollutionRate = m_FertilityGroundMultiplier / 32` (`:429`). So ground pollution *adds* to used fertility and a flat rate removes it every update. **`Verdict:` the wiki's "Fertile Land … resources are depleted by ground pollution" (https://cs2.paradoxwikis.com/index.php?title=Natural_resources&action=raw) understates it in one direction and overstates it in another** — the effect is on `m_Used`, is bounded by `m_Base`, and reverses at a constant rate once the pollution goes; the base endowment is never touched. `Map Creation: Resources` states the correct version, "regeneration can be reduced by ground pollution".
- **Fish is fully derived and is not extracted from the cell in the usual sense.** Per update, per cell (`:137-172`): over the water-simulation sub-cells covering this resource cell, let `d = max(0, depth - 2)`, then sum `d` into a water term and `d * m_Polluted` into a pollution term, scale both by `m_WaterCellFactor = 300 / (waterCellsPerResourceCell)` (`:430`), then add `waterVolume * noisePollution * 6.25e-05`. The computed base is `min(10000, waterVolume)`, a value of 1..19 snapping to 0 and a current base of 1..19 counting as 20 in the comparison (`:155-157`); **the 20-unit deadband gates the `m_Base` write itself**, not only the area-tree notification — both happen only when `|new - current| >= 20` (`:158-164`). `m_Fish.m_Used` chases `clamp(pollutionTerm * 50, 0, 10000)` upward at `RoundToIntRandom(pollutionTerm * 3.125)` per update and downward at the flat regeneration rate (`:165-172`). **So water depth sets the fish stock and water pollution plus traffic noise sets the standing loss.**
- **Ore and oil are the only genuinely extracted channels**, and they deplete asymptotically. `AreaLotSimulationSystem.ExtractNaturalResources` (`src/Game/Game.Simulation/AreaLotSimulationSystem.cs:726-851`) picks the best cell under the extractor's triangles, and for ore and oil sets `num3 = extractedAmount` unconditionally rather than capping it at what the cell holds — `flag = mapFeature == Ore || mapFeature == Oil` at `:738`, used at `:808`. The cell's `m_Used` then rises by
  `GetUnlimitedUsage(originalConcentration, currentConcentration, mu, random, extracted)` (`:720-724`):
  ```
  num = log(original) - log(current)
  return RoundToIntRandom(random, mu * original * exp(-num) * extracted * 10000)
  ```
  **`exp(-(log(o) - log(c)))` is `c/o`, so the whole expression reduces to `mu * current * extracted * 10000`** — with `original = m_Base * 1e-4` and `current = (m_Base - m_Used) * 1e-4`, that is `mu * (m_Base - m_Used) * extracted`. `mu` is `1 / m_OreConsumption` or `1 / m_OilConsumption` (`:825`, `:833`). The consequence is the mechanism: **the fuller the deposit, the faster a unit of extraction depletes it, and as it empties each extracted unit costs less of it** — an exponential approach to empty that never reaches it. The cell is skipped once `m_Base - m_Used` hits 0 (`:785-788`), which is the only floor.
- Fertility and fish take `m_Used += min(round(what the chosen cell scored), extracted)` (`:808`, where `flag` restricts the raw amount to ore and oil; `:819`, `:838`), capped at 65535; every available read clamps at 0 (`:784`).

**The declared regeneration constants are not the ones the job uses.** `NaturalResourceSystem` declares `public const int FERTILITY_REGENERATION_RATE = 800` and `FISH_REGENERATION_RATE = 800` (`:294-296`), and `OnUpdate` assigns `m_FertilityRegenerationRate = 25` and `m_FishRegenerationRate = 25` (`:427-428`). The constants have no other reader in `src/`. A reference that ships the C# constant here would ship a number 32× the operative one. This is the sharpest of the eleven unread constants and is what the `conflicts.md` entry was raised on.
**Ruled (2026-08-18, this pass, made by the orchestrating session under the maintainer's delegated authority for that pass; conflicts.md).** What consumes the value decides, not where it is written. The operative literal the consuming code compiled in ships as a number, cited to the consuming line — `m_FertilityRegenerationRate = 25` at `NaturalResourceSystem.cs:427` is compiled in and no data can replace it, so it ships exactly as a read `const` does. A declared constant nothing reads ships as no number and is cited as no value's source, the one that agrees with its literal (`MAX_BASE_RESOURCES`) included, since agreement is exactly what a reader cannot establish from the declaration. The reference states the unread-constant hazard once, as a trap — a named constant in these files is not necessarily what the system runs on; check for a reader before citing one — with this 32× pair as the worked example.

**`AreaResourceSystem` is the per-district recompute.** `UpdateAreaResourcesJob` rebuilds an updated area's 9-slot `MapFeatureElement` buffer — surface area, buildable land, the four cell resources with their renewal rates, groundwater, and `Forest` as the summed live wood with the summed growth rate as its renewal (`src/Game/Game.Areas/AreaResourceSystem.cs:407-441`) — and recomputes `Extractor.m_ResourceAmount`/`m_MaxConcentration` by the extractor's `ExtractorAreaData.m_MapFeature` (`:382-405`): `Forest` walks the area's cached `WoodResource` tree list and sums `ObjectUtils.CalculateWoodAmount` per tree (`:481-498`), a growth-stage lerp times the prefab's `TreeData.m_WoodAmount` — sapling `lerp(0, 0.2, m_Growth/256)`, teen `lerp(0.2, 0.7, …)`, adult `lerp(0.7, 1, …)`, elderly full, dead or stump zero — then scaled by `(1 − plant.m_Pollution) * (1 − damage)` (`src/Game/Game.Objects/ObjectUtils.cs:376-402`) — with `m_MaxConcentration = max(wood / m_WoodAmount)` capped at 1 (`:494-498`). So a forestry area's concentration is tree state and forest depletes through the trees, never through a cell write; this closes the seam `economy-and-companies.md:139` and `extraction-and-depletion.md:79` delegate here. (Added 2026-08-18 by the gate: the original pass cited `AreaResourceSystem` only at its registration lines and its `GetUsedResources` bug, never its recompute.)

**`GetUnlimitedTotalAmount` is public, broken and uncalled.** `AreaLotSimulationSystem.cs:1240-1243` reads
`Mathf.RoundToInt(math.log(originalAmount / 10000) - math.log((originalAmount - used) / 10000) / mu)`
with `originalAmount` and `used` both `int`, so both divisions are **integer** division and both operands of `log` are 0 or 1 for any value in the 0..10000 range the map uses — `log(0)` is negative infinity. The `/ mu` also binds to the second `log` alone rather than to the difference. A grep for the name over `src/` finds no caller. It is the inverse of `GetUnlimitedUsage` and a reader will find it while reading that; it must not be used.

**Two other declared-and-unused constants** sit on the same system: `UPDATES_PER_DAY = 32` and `EDITOR_ROWS_PER_TICK = 4` (`:298-300`). The real update interval is `8192` in `GameSimulation` and `1` in any other phase (`:334-341`), and the editor's row count is the literal `4` at `:482`.

**A game mode can rewrite the whole layer.** `GameModeNaturalResourcesAdjustSystem` (`src/Game/Game.Simulation/GameModeNaturalResourcesAdjustSystem.cs`, `SystemOrder.cs:491`) carries a one-shot boost job multiplying every cell's `m_Base` for fertility, ore and oil and every groundwater cell's three fields by a mode multiplier (`:15-47`), and a per-update refill job subtracting `m_Base * percentPerDay / 100 / kUpdatesPerDay` from `m_Used` for oil, ore and fertility (`:50-64`). **So "ore and oil are non-renewable" is a property of the default mode, not of the mechanism** — `ModeSettingData.m_PercentOilRefillAmountPerDay` and its two siblings turn them renewable.

**The editor writes these maps through the same base-class protocol a mod would use.** `Game.Tools.ApplyBrushesSystem` declares `ICellModifier<T>` implementations for natural resources and groundwater and applies them through a generic `ApplyCellMapBrush<TCell, TModifier>(CellMapSystem<TCell> cellMapSystem, …)` that ends in `cellMapSystem.AddWriter(jobHandle)` (`src/Game/Game.Tools/ApplyBrushesSystem.cs:30-70`, `:267-276`, `:311-335`). It is the game's own worked example of writing a cell map, it is generic over the base class, and a mod can copy it verbatim.

### Terrain attractiveness: the fourth thing the pollution neighbourhood produces

`TerrainAttractivenessSystem` (`src/Game/Game.Simulation/TerrainAttractivenessSystem.cs:15`, `SystemOrder.cs:414`, interval `262144/16`) writes `TerrainAttractiveness { m_ShoreBonus, m_ForestBonus }` (`src/Game/Game.Simulation/TerrainAttractiveness.cs:5-9`) at 128². It runs two jobs: a prepare pass caching `(waterDepth, terrainHeight, forestAmbience)` per cell (`:18-39`), then a neighbourhood max over a radius of `ceil(max(m_ForestDistance, m_ShoreDistance) / cellSize)`:

```
forestBonus = max over neighbours of  saturate(1 - dist/m_ForestDistance) * forestAmbienceThere
shoreBonus  = max over neighbours of  saturate(1 - dist/m_ShoreDistance)  * (waterDepthThere > 2 ? 1 : 0)
```

(`:54-79`). **The shore test is a hard 2-metre depth threshold** — a C# literal, and the only place in the topic where "is this water" is decided by a number rather than by a component. The consumer is `EvaluateAttractiveness` (`:109-128`), a public static feeding `AttractionSystem.AttractivenessFactor.Forest`, `.Beach` and `.Height`, with the height term `min(m_HeightBonus.z, max(0, terrainHeight - m_HeightBonus.x) * m_HeightBonus.y)`. The forest input is `ZoneAmbienceSystem.GetZoneAmbience(GroupAmbienceType.Forest, …)`, so trees reach attractiveness through the zone-ambience map rather than through this one. Read live at 1.6.0f1, one sampled cell carries `m_ShoreBonus = 0.192`, `m_ForestBonus = 0.983`.

### Climate is a managed system with overridable properties, not an ECS layer

`ClimateSystem` (`src/Game/Game.Simulation/ClimateSystem.cs:26`, `SystemOrder.cs:359` for `GameSimulation` and `:603` for `EditorSimulation`) is a `GameSystemBase` whose whole state is C# properties. There is no per-cell climate, no climate component on any entity, and the two `IComponentData` types in `Game.Prefabs.Climate` — `ClimateData` and `SeasonData` — are **zero-size tag structs** (`src/Game/Game.Prefabs.Climate/ClimateData.cs:6-9`, `SeasonData.cs:6-9`).

**Five values are `OverridableProperty<float>` and that is the mod hook.** `temperature`, `precipitation`, `cloudiness`, `aurora`, `fog` (`:195-203`), plus `currentDate` backed by `m_Date` (`:145`, `:193`). `Game.OverridableProperty<T>` (`src/Game/Game/OverridableProperty.cs:7-79`) holds a value, an override value, an override flag, and an optional `Func<T>` synchroniser. Setting `.overrideValue` **also sets `.overrideState`** (`:31-42`), and the implicit `operator T` returns the override when set, otherwise the synchroniser's result, otherwise the plain value (`:68-79`). **So a mod pins the weather with one assignment and releases it by setting `.overrideState = false`.** `SampleClimate(float t)` honours the overrides on every channel (`:625-654`), so systems reading through the pull path see them too.

**`.value` is not the value, and `currentDate` proves it.** `m_Date` is constructed with a synchroniser (`() => m_TimeSystem.normalizedDate`, `:453`), so `currentDate.value` is the never-written backing field. Read live at 1.6.0f1: `currentDate.value == 0` while `TimeSystem.normalizedDate == 0.684`. The corpus hit this and worked around it the long way: `Water_Features/Systems/SeasonalStreamsSystem.cs:314` carries the author's own note, `float.Parse(climateDate.ToString(), …); // This is a dumb solution but climateDate.value is coming up as 0.` — `ToString()` goes through the implicit conversion and is correct, which is why it works. The right read is the implicit conversion (`float t = climateSystem.currentDate;`) or `ToString()`, never `.value`.

**The other four are cached by `OnUpdate` and stale while the simulation is paused.** `OnUpdate` assigns `temperature.value = SampleClimate(prefab, m_Date).temperature` and the same for the other four (`:465-478`), and the system is registered only in `GameSimulation` and `EditorSimulation`. Read live with `SimulationSystem.selectedSpeed == 0` on a loaded city, all five `.value`s were 0 while `SampleClimate(0.68441457f)` returned `temperature = 16.24, precipitation = 0.151, cloudiness = 0.307, aurora = 0.455, fog = 0`. **`SampleClimate` is the read that always works; the properties are the read that works while the game is running.**

**`Rots:` this whole finding is properties on a managed class rather than components, so nothing about it is greppable as ECS. Re-check `ClimateSystem.cs:131-274` for the property set.**

**What a climate is, as data.** `ClimatePrefab : PrefabBase` (`src/Game/Game.Prefabs.Climate/ClimatePrefab.cs:19`) carries `m_Latitude`, `m_Longitude`, `m_MaxSunElevationAngle`, `m_SunElevationClampStart`, `m_FreezingTemperature`, five `AnimationCurve`s (`m_Temperature`, `m_Precipitation`, `m_Cloudiness`, `m_Aurora`, `m_Fog`), `m_DefaultWeather`, `m_DefaultWeathers[]`, `m_Seasons[]` and `m_RandomSeed` (`:611-648`). **`public const int kYearDuration = 12`** (`:650`) fixes the generated curves' 0..12 time axis, inlined as literals through the adapters; `SampleClimate` evaluates at `t * daysPerYear` (`:607-623`), which traces to `TimeSettingsPrefab.m_DaysPerYear` (`TimeSystem.cs:42-55`, `TimeSettingsPrefab.cs:10`) — also 12, by coincidence rather than through this constant, so changing the year length desyncs the sample point from the curve axis. (Corrected 2026-08-18 by the gate; this file first wrote the constant as the reason for the multiplier.) The five curves are `[HideInEditor]` and are *generated* from the seasons rather than authored: each `ClimateSystem.SeasonInfo` (`ClimateSystem.cs:29-84`) holds `m_StartTime`, `m_TempNightDay`, `m_TempDeviationNightDay`, `m_CloudChance/Amount/AmountDeviation`, `m_PrecipitationChance/Amount/AmountDeviation`, `m_Turbulence`, `m_AuroraAmount`, `m_AuroraChance`, and the prefab's `TemperatureData`/`PrecipitationData`/… adapters build the curves by keying each season at its mid-time and looping (`:83-116` and the parallel blocks).

**`FindSeasonByTime(normalizedDate)` is the season lookup** and returns `(SeasonInfo, startRange, endRange)`; `UpdateSeason` calls it every update and recomputes `seasonTemperature`, `seasonPrecipitation`, `seasonCloudiness` as means over the season's range only when the season changed (`ClimateSystem.cs:656-668`). `currentSeason` returns the season prefab's entity, `currentSeasonName` its name, and the name is what three corpus mods match on. Read live at 1.6.0f1: `currentSeasonName = "SeasonSpring"`, `currentSeason = Entity(41625:1)`, `averageTemperature = 17.67`, `seasonTemperature = 13.92`, `freezingTemperature = 0`, `classification = Scattered`, `daysPerYear = 12`.

**`Verdict:` the `Climate` wiki article's "all maps will introduce different Climates with four distinct seasons" is wrong, and the map-editor page is right.** `m_Seasons` is a plain array with no length constraint, `FindSeasonByTime` walks it, and `Map Creation: Climate` states "A climate may contain one season, two seasons, four seasons, six seasons, or any other number" (https://cs2.paradoxwikis.com/index.php?title=Map_Creation:_Climate&action=raw). The `Climate` article carries `{{Version|1.0}}` and sits in `Category:Potentially outdated`; the map-creation page carries `{{ParadoxVerifiedAmbox|version=1.6.0 f1}}`.

**Average temperature is computed two ways and the switch is `daysPerYear == 12`.** `CalculateTemperatureAverage` (`:399-409`) picks `CalculateMeanTemperatureEkholmModen` when the year is twelve days and `CalculateMeanTemperatureStandard` otherwise. The Ekholm–Modén method is a weighted monthly mean using a hard-coded `int[12,5] kLut` and `int[3] kSampleTimes = { 7, 13, 19 }` (`:157-173`) — a table of real meteorological weights, `Assert.AreEqual(12, m_TimeSystem.daysPerYear)` at `:320`. Both the table and the sample hours are `private static readonly` C# data and are invariant structure. **A mod that changes the year length silently switches the game to the other estimator**, which is what `Time2Work` runs into.

**Weather is a prefab-selection layer above the curves.** `UpdateWeather` (`:804-830`) picks the *next* `WeatherPrefab` by nearest `m_CloudinessRange` to the live cloudiness and *current* as its predecessor in `m_DefaultWeathers` (`SelectWeatherPlaceholder`, `:686-709`, the pair at `:702-703`), then expands each through its `PlaceholderObjectElement` buffer filtered by `ObjectRequirementElement` against the current season, assigning priorities by `WeatherPrefab.RandomizationLayer` — `Aurora` 500, `Season` 300, `Cloudiness` 250, the placeholder itself −1000, the default weather −1001 (`SelectRandomWeather`, `:711-771`). The sorted result is handed to `ClimateRenderSystem.ScheduleFrom`/`ScheduleTo` (`ApplyWeatherEffects`, `:832-853`), and the last non-`Irrelevant` `m_Classification` assigned in `ApplyWeatherEffects`' two plain loops becomes `ClimateSystem.classification` (`:835-852` — an assignment per prefab, no ordering by severity) — one of `Irrelevant, Clear, Few, Scattered, Broken, Overcast, Stormy` (`:86-95`). **Weather is therefore rendering plus a classification enum; the only gameplay values are the five curve samples and `classification`.**

`isRaining`, `isSnowing`, `isPrecipitating` are derived: `precipitation > 0` split by `temperature` against `freezingTemperature` (`:234-258`). `hail` is a plain settable property nothing in the game writes and `HandleTriggers` reads first (`:181`, `:489` — above 0.001 it forces `WeatherStormy` and returns, and `ClimateRenderSystem` gates on it), so it is a drivable gameplay input; `rainbow` (`:183`) has no reader or writer, and `thunder` is a seventh `OverridableProperty<float>` (`:131`) whose only reference in `src/Game/` is its declaration — both dead like `wind`.

**Climate raises triggers rather than driving systems directly**, through `TriggerSystem`: `TriggerType.Temperature` every update carrying the temperature, then at most one of `WeatherStormy` (hail or a stormy classification), `WeatherRainy`, `WeatherSnowy`, `WeatherSunny` (day, clear, `temperature > 15`), `WeatherClear` (day, clear, colder), `WeatherCloudy` (day, cloudy), `AuroraBorealis` (night, clear, aurora) (`:485-519`) — a cloudy, dry night satisfies no branch and fires none. The `15f` and the `0.5f` cloudiness cutoff are C# literals in that method.

### What climate actually drives, in full

A census taken by grepping `src/Game/` for `ClimateSystem`, then reading each simulation-side hit:

| Consumer | Input | Effect |
|---|---|---|
| `AdjustElectricityConsumptionSystem.cs:461-467`, `:443`, `:145` | `temperature` | `electricityConsumption *= m_TemperatureConsumptionMultiplier.Evaluate(temperature)` — an `AnimationCurve` on the prefab |
| `BuildingUpkeepSystem.cs:1069-1071`, `:1184` | `temperature` | `GetHeatingMultiplier(t) = max(0, 15 - t)`, a public static and a shipping formula with a real constant |
| `PowerPlantAISystem.cs:497` | `cloudiness` | solar output `*= lerp(1, 1 - m_CloudinessSolarPenalty, cloudiness)` |
| `FireHazardSystem.cs:318-329` | `isRaining`, `temperature` | `noRainDays` resets to 0 on rain and accrues `1/64` per update otherwise; `FireHazardData.Update` multiplies `m_TemperatureForestFireHazard.Evaluate(temperature)` by `m_NoRainForestFireHazard.Evaluate(noRainDays)` (`EventHelpers.cs:29-33`) |
| `FireSimulationSystem.cs:716` | `temperature`, `FireHazardSystem.noRainDays` | same hazard factor, applied to spreading fires |
| `WeatherHazardSystem.cs:186-199` | `temperature`, `precipitation`, `cloudiness` | disaster spawn probability, formula below |
| `LeisureSystem.cs:960`, `:1007-1010`, `:498-508` | `precipitation` (read as `.value`), `temperature` | leisure appeal per type: beach `0.05 + 4 * saturate(0.35 - weather) * saturate((t - 20)/30)` (the switch's default arm), park `2 * (1 - 0.95 * weather)`, `CityIndoors` flat `1`, and `Travel` `0.5 + saturate((30 - t)/50)` — shipping formulas |
| `TourismSystem.cs:199-231`, `TouristSpawnSystem.cs:203-207` | `classification`, `temperature`, `precipitation`, `isRaining`, `isSnowing` | `GetWeatherEffect` scales the tourist spawn probability, against `AttractivenessParameterData`'s `m_AttractiveTemperature`, `m_ExtremeTemperature`, `m_TemperatureAffect`, `m_SnowEffectRange`, `m_RainEffectRange`, `m_SnowRainExtremeAffect` |
| `WetnessSystem.cs:171-191` | `precipitation`, `temperature` | wetness and snow targets and rates on every `Surface` |
| `SnowSystem.cs:465-466`, `:473`, `:487` | `temperature`, `precipitation` | parameters into the snow accumulation shader, plus `constantWind` |
| `IndustrialFindPropertySystem.cs:279` | `averageTemperature` | property scoring input |
| `WindSimulationSystem.cs:302` | — | **holds a `ClimateSystem` reference and never reads it** |
| `EffectFlagSystem`, `ClimateRenderSystem`, `MeshColorSystem`, `ObjectColorSystem`, `AudioGroupingSystem`, `PhotoModeRenderSystem` | various | presentation only |
| `IndustrialDemandSystem.cs:863`, `TrafficSpawnerAISystem.cs:670` | — | **hold a `ClimateSystem` reference and never read it** |

Three dead references (`WindSimulationSystem`, `IndustrialDemandSystem`, `TrafficSpawnerAISystem`) are worth stating because their presence makes a reader assume a link that is not there — most importantly, **climate does not drive the wind**.

### Wind: a 3D pressure simulation with no gameplay input and one editor-set parameter

Two systems. `WindSimulationSystem` (`src/Game/Game.Simulation/WindSimulationSystem.cs:13`, `SystemOrder.cs:361`) holds a `NativeArray<WindCell>` of `kResolution = int3(64, 64, 16)` (`:130`) — `WindCell { float m_Pressure, float3 m_Velocities }` (`:15-38`). It alternates two jobs on odd and even updates (`:330-370`): a velocity pass reading the terrain heights and the water surface, and a pressure pass that pushes the boundary cells toward a target derived from the prevailing wind:

```
alignment = dot(normalize(cellXY - centre), normalize(m_Wind))
altitude  = pow((1 + z) / (1 + kResolution.z), 1/7)          // the 1/7 power-law wind profile
target    = (40 - 20*(1 + alignment)) * length(m_Wind) * altitude
pressure  = move toward target by at most 0.1 * (2 - alignment)
```

(`:118-124`). `kChangeFactor = 0.02`, `kTerrainSlowdown = 0.99`, `kAirSlowdown = 0.995`, `kVerticalSlowdown = 0.9` and `kUpdateInterval = 512` are all `public static readonly` and ship (`:128-138`). `m_Wind` is `constantWind / 10` (`:358`).

`WindSystem` (`src/Game/Game.Simulation/WindSystem.cs:13`, `SystemOrder.cs:362`) flattens that volume into the 64² `Wind { float2 m_Wind }` cell map every 512 frames (`:43-62`): for each cell it samples the terrain height plus 25 metres, converts that to a fractional layer index, and lerps the horizontal velocity between the two bracketing layers (`:26-40`).

**The prevailing wind is `WindSimulationSystem.constantWind`, a public `float2` auto-property with no gameplay writer.** Its default is `(0.275, 0.275)` (`:303`), it is serialized with the wind cells (`:212`, `:230-238`), and the only setter in `src/` is `SetWind(float2 direction, float pressure)` (`:260-266`) whose only caller is the map editor's climate panel (`src/Game/Game.UI.Editor/ClimatePanelSystem.cs:140-165`, which passes a unit vector from an angle and a pressure of `40f`). **So wind direction is a map property, authored in the editor, saved, and never changed by the simulation.** A mod that wants to change it calls `SetWind`, which resets every cell.

**`ClimateSystem.wind` is a different, dead property.** It is a `float2` auto-property with a private setter initialised to `(0.0275, 0.0275)` (`ClimateSystem.cs:179`), and a grep for `.wind` over `src/` finds no reader and no writer. Read live at 1.6.0f1 it still holds `(0.0275, 0.0275)` while the operative `constantWind` is a different value. **A reader who finds `ClimateSystem.wind` first gets a number that means nothing.**

**Three consumers.** `AirPollutionSystem` advects the air map along it (`AirPollutionSystem.cs:40-41`), `WeatherPhenomenonSystem` moves a live disaster along it at `Wind.SampleWind(m_WindData, position) * 20f` (`WeatherPhenomenonSystem.cs:481`, `:484`), and `PowerPlantAISystem` reads it for wind-turbine output through `WindSystem.GetWind` (`PowerPlantAISystem.cs:222`). `Wind.SampleWind(CellMapData<Wind>, float3)` is a static bilinear sampler on the struct itself (`src/Game/Game.Simulation/Wind.cs:26-38`) and is the one that takes a `CellMapData` rather than a raw array — `WindSystem.GetWind(float3, NativeArray<Wind>)` (`WindSystem.cs:69-80`) is the other, clamping rather than offsetting. Read live at 1.6.0f1, wind cell 2080 holds `(0.190, 0.187)`.

### Snow and wetness: a GPU depth field, a per-object byte, and a snowplough that does not clear snow

`SnowSystem` (`src/Game/Game.Simulation/SnowSystem.cs:21`, `SystemOrder.cs:360`, `:604`) is a second compute-shader simulation — kernels `Reset`, `Add`, `Transfer`, `LoadR16G16_UNorm`, backdrop passes (`:234-241`) — dispatched at 64×64 groups over a `SnowDepth` render texture (`:272-285`). It reads `WindSimulationSystem.constantWind` and `TimeSystem.normalizedTime` as shader parameters (`:467-469`, `:496`). Like water, it is not readable or writable from a job.

The **gameplay** side of snow is `Game.Objects.Surface { byte m_Wetness, m_SnowAmount, m_AccumulatedWetness, m_AccumulatedSnow, m_Dirtyness }` (`src/Game/Game.Objects/Surface.cs:6-16`), driven by `WetnessSystem` (`src/Game/Game.Simulation/WetnessSystem.cs`, `SystemOrder.cs:470`). `OnUpdate` builds three `float4`s of targets and rates from precipitation and temperature, branching on `temperature > 0` (`:168-191`), and the job moves each byte toward its target with a per-object random jitter (`:74-81`):

```
delta = clamp(target - current/255, drySpeed, wetSpeed) * random(0.8, 1) * 255
current = clamp(current + RoundToIntRandom(delta), 0, 255)
```

**`m_AccumulatedSnow >= 15` is the one threshold, and it is a C# literal used in three places**: `WetnessSystem.cs:70` and `:83` (raising `ObjectRequirementFlags.Snow`, which triggers a `SubObjectsUpdated` event so the object swaps to its snow variant), `Game.Objects/SubObjectSystem.cs:3210` (the same selection), and `Game.Buildings/BuildingUtils.cs:701` (the maintenance branch).

**`BuildingUtils.GetMaintenanceType`** (`src/Game/Game.Buildings/BuildingUtils.cs:689-712`) returns `MaintenanceType.Snow` instead of `MaintenanceType.Road` when a net entity's `Surface.m_AccumulatedSnow >= 15`, averaging the two node values when the edge itself carries no `Surface` (`:697-700`). That is the only difference snow makes to maintenance, and **the action it selects is identical**: `MaintenanceVehicleAISystem`'s maintain branch multiplies every sub-lane's `LaneCondition.m_Wear` by a factor and writes `NetCondition.m_Wear` (`src/Game/Game.Simulation/MaintenanceVehicleAISystem.cs:1355-1390`, `MaintainLanes` at `:1504-1523`). **Nothing anywhere in `src/` writes `Surface.m_AccumulatedSnow` except `WetnessSystem`** — a census by grepping the field name. A snowplough repairs road wear and does not remove snow; snow leaves only when `WetnessSystem`'s temperature-driven dry rate takes it.

**`Verdict:` the wiki's "When there is snow, the Road Maintenance building's snowplows take care of excess snow on the roads, keeping up the road condition to reduce the risk of accidents" is wrong on the mechanism.** The claim is at https://cs2.paradoxwikis.com/index.php?title=Climate&action=raw. Road wear has exactly two sources — traffic, via `LaneCondition.m_Wear += sideEffect.x * m_TrafficFactor` (`CarNavigationSystem.cs:2693`, `TrainNavigationSystem.cs:1245`), and time, via `NetDeteriorationSystem.cs:46` — and snow is neither. Accident probability reads `netCondition.m_Wear` (`RoadSafetySystem.cs:106`) and not snow. What is true is the second-order chain: snow re-labels a maintenance request, so a city with only a plain road-maintenance depot may or may not serve it, and unserved wear does raise accidents.

**Live enumeration (2026-08-18, base content set).** The five `MaintenanceDepotData` carriers: the road depot at `Road | Snow | Vehicle` (capacity 10, efficiency 1), a park depot at `Park`, and three `None` sub-entries. No depot carries `Road` without `Snow`, so on the base set both labels land on the same depot — `m_MaintenanceType` is the per-prefab routing mask, and the mask set is an install's content, re-enumerated live per install.

`Unconfirmed:` whether `MaintenanceType.Snow` genuinely restricts which depots respond, or is only a vehicle-selection label. `MaintenanceDepot.m_MaintenanceType` is a mask tested against it (`src/Game/Game.Prefabs/MaintenanceDepot.cs:36`, `MaintenanceVehicleDispatchSystem.cs:412`, `:457`, `:507`) and the road-maintenance depot's mask is `Road | Snow | Vehicle` (`src/Game/Game.Prefabs/InfoviewInitializeSystem.cs:923`), so on the base game every road depot serves both. Settling whether any prefab carries `Road` without `Snow` means enumerating `MaintenanceDepotData` live; not done.

### The day–night cycle: two constants and a small consumer set

`EffectFlagSystem.kNightBegin = 0.75f` and `kDayBegin = 0.25f`, both `public static readonly float` (`src/Game/Game.Effects/EffectFlagSystem.cs:27-29`), and `m_IsNightTime = normalizedTime >= kNightBegin || normalizedTime < kDayBegin` (`:155`). **These are the game's definition of day and night and they ship as numbers.** `ClimateSystem.HandleTriggers` uses the same pair inline (`ClimateSystem.cs:491`).

`TimeSystem.normalizedTime` is the input everywhere. Simulation-side consumers, from a grep over `src/Game/Game.Simulation/`: `CitizenBehaviorSystem.IsSleepTime` (`:1076-1090`, against the citizen's own sleep window) and `:1184`; `CitizenTravelPurposeSystem.cs:667`; `DeathCheckSystem.cs:455`; `StudentSystem.cs:388`, `:408`; `ResidentAISystem.cs:4698`; `PersonalCarAISystem.cs:1203`; `TaxiAISystem.cs:1700`; `ResourceBuyerSystem.cs:1303`; `LeisureSystem.cs:1007`; `TouristLeaveSystem.cs:152`; `TransportLineSystem.cs:823`; `FireSimulationSystem.cs:748`; plus two that quantise it into quarters, `CalendarEventLaunchSystem.cs:114` (`1 << floor(t*4)` into `CalendarEventTimes`) and `RoadSafetySystem.cs:275` / `TrafficFlowSystem.cs:315` (`t * 4`). **The day–night cycle's own definition and length belong to `simulation-time-and-units`; what this topic owns is that the two thresholds are here and that the effect on the world is `EffectFlagSystem` plus `PlanetarySystem`'s sun position from the climate's latitude, longitude and sun-elevation clamp** (`ClimateSystem.cs:588-590`).

### A disaster is an ordinary event entity, spawned by a probability roll against the weather

**The event archetype comes from the prefab.** An `EventPrefab` carrying `Game.Prefabs.WeatherPhenomenon` (`src/Game/Game.Prefabs/WeatherPhenomenon.cs:11`) declares `WeatherPhenomenonData` as its prefab component and, as its *archetype* components, `Game.Events.WeatherPhenomenon`, `HotspotFrame`, `Duration`, `DangerLevel`, `TargetElement` and `InterpolatedTransform` (`:29-40`). `EventData.m_Archetype` is what a spawner instantiates.

**Spawn** — `WeatherHazardSystem` (`src/Game/Game.Simulation/WeatherHazardSystem.cs:17`, `SystemOrder.cs:433`, interval 2048 with `m_TimeDelta = 34.133335f` seconds, `:167-170`, `:189`). Its query is every prefab entity with `EventData + WeatherPhenomenonData`, excluding `Locked` (`:179`), and its job is one formula (`:54-116`):

```
skip if the prefab's Locked component is enabled                          // progression gate
skip if m_DamageSeverity != 0 and natural disasters are switched off      // the city setting
tCentre = centre(m_OccurenceTemperature); tExtent = max(0.5, extents(...))
tFactor = max(0, 1 - ((temperature - tCentre)/tExtent)²)                  // an inverted parabola
rFactor = 1, or a saturating ramp over m_OccurenceRain,       depending on which end is open
cFactor = 1, or a saturating ramp over m_OccurenceCloudiness, depending on which end is open
p = m_OccurenceProbability * tFactor * rFactor * cFactor * m_TimeDelta
while (random.NextFloat(100) < p) { create the event; p -= 100 }          // p > 100 spawns several
```

The two range tests are asymmetric on purpose (`:77-107`): a range of exactly `(0,0)` means "no constraint"; a range whose `max > 0.999` **and** `min >= 0.001` ramps *upward* from `min`; a range whose `max <= 0.999` and `min < 0.001` ramps *downward* to `max`; a range open at both ends — `(0,1)`, the authoring default at `WeatherPhenomenon.cs:18-20` — or closed at both ends falls through and leaves the factor at 1. **A modder authoring a phenomenon must know that only a range open at exactly one end constrains the roll.**

Note `private const int UPDATES_PER_DAY = 128` at `:155` is unread; the interval is the literal 2048.

**Initialisation** — `Game.Events.InitializeSystem.InitializeWeatherEvent` (`src/Game/Game.Events/InitializeSystem.cs:495-573`, `SystemOrder.cs:110`, `Modification2`):

- `m_PhenomenonRadius` and `m_HotspotRadius` are rolled from the prefab's `Bounds1` ranges, the hotspot as a *fraction* of the phenomenon radius (`:504-511`).
- **`m_StartFrame` is delayed by the city's `CityModifierType.DisasterWarningTime`** and only for a phenomenon with non-zero `m_DangerFlags` (`:512-521`). That delay *is* the early-warning window: the entity exists and the disaster has not begun.
- `m_EndFrame = m_StartFrame + random(m_Duration) * 60`, so `m_Duration` is in seconds (`:522-526`).
- Position comes from the first `TargetElement` with a `Transform`, else from **`FindRandomLocation`, which is `random.NextFloat2(-6000f, 6000f)` on xz** (`:832-838`) — a hard C# constant, and notably smaller than the map's own `kMapSize/2 = 7168`.
- The hotspot is placed at a random direction and distance up to `m_PhenomenonRadius - m_HotspotRadius` from the phenomenon centre (`:545-549`).
- The lightning timer is seeded at `5 + max(0, min(m_LightningInterval.min, durationSeconds) - 10)` (`:550-555`).
- The `HotspotFrame` buffer is sized to exactly 4 (`:556-560`) — a four-frame ring the renderer interpolates.
- Every building carrying `Game.Buildings.EarlyDisasterWarningSystem` gets an `EarlyDisasterWarningDuration` stamped with the event's end frame, but **only when the event prefab carries `EarlyDisasterWarningEventData`** (`:561-570`).

**Tick** — `WeatherPhenomenonSystem` (`src/Game/Game.Simulation/WeatherPhenomenonSystem.cs`, `SystemOrder.cs:542`). With `num = 4/15` seconds per update and `t = pow(0.9, num)` (`:455-456`):

- Intensity ramps `±0.2 * num` per update, up between `m_StartFrame` and `m_EndFrame` and down after (`:472-480`), clamped to `[0,1]` — but the expiry branch adds `Deleted` on the first update past `m_EndFrame` (`:548-552`, decrement `<=` vs expiry `<`, interval 16 at `:826-829`), so at most two decrements ever land and the post-end fade is unobservable; the off-transition is always carried by `Deleted`. The `DangerLevel` write, `FindEndangeredObjects` and the expiry all sit OUTSIDE the intensity gate (`:528-556`), which is what lets endangerment run during the early-warning window while intensity is still 0.
- **The phenomenon is advected by the wind map at 20× the wind vector** (`:481`, `:484`), and re-grounded to the water-or-terrain height each step (`:485`) — with movement, hotspot, damage, lightning and traffic accidents all inside an `if (m_Intensity != 0)` gate (`:482`), so nothing moves or damages during the early-warning window.
- The hotspot chases the phenomenon centre with an instability term: its velocity lerps toward `wind + (offset + random(±(phenomenonRadius - hotspotRadius))) * m_HotspotInstability`, then gains a radial correction that keeps it inside the phenomenon disc (`:486-497`).
- Damage runs only when `m_DamageSeverity != 0` (`:499-502`); lightning fires on the timer (`:503-517`); traffic accidents run when the prefab also carries `TrafficAccidentData`, with `dividedProbability = sqrt(m_OccurenceProbability * 0.01)` (`:659`).
- `DangerLevel` is set to the prefab's `m_DangerLevel` while the event is between its frames and 0 otherwise (`:546`).
- On expiry the entity gets `Deleted` and every early-warning building gets `EffectsUpdated` (`:548-556`).

**Severity and damage.** `EventUtils.GetSeverity(position, phenomenon, data)` is a public static (`src/Game/Game.Events/EventUtils.cs:12-17`):

```
d        = distance(position.xz, hotspot.xz) / m_HotspotRadius
severity = m_Intensity * m_DamageSeverity * (1 - d)      // 0 when below 0.001
```

`WeatherDamageSystem` (`src/Game/Game.Simulation/WeatherDamageSystem.cs`, `SystemOrder.cs:574`) applies it to anything carrying `FacingWeather` (`:87-140`):

```
damageRate = severity / GetStructuralIntegrity(prefab)
if structuralIntegrity >= 1e8  →  immune, severity forced to 0
buildings: damageRate scaled by CityModifierType.DisasterDamageRate
damageRate = min(0.5, damageRate * 1.0666667)                       // the literal at :79
with a Damaged component: m_Damage.x += damageRate, capped at 1;
    a Destroy event is created when ObjectUtils.GetTotalDamage(damaged) == 1 (:128)
without one: a Damage event carrying the rate is created instead (:139-143)
```

**`structuralIntegrity >= 1e8` is the game's own "indestructible" sentinel**, and it is what makes the wiki's "shelters cannot be destroyed by disasters" true.

**Endangerment and evacuation.** `FindEndangeredObjects` (`WeatherPhenomenonSystem.cs:608-635`) builds a line segment from the phenomenon centre along the wind for `CityModifierType.DisasterWarningTime` seconds **minus the time still remaining before the start frame** (`:613-617`) — zero as the warning opens, the full window once the disaster begins — and iterates the static-object quadtree for anything within `m_PhenomenonRadius` of it, raising `Endanger` events. **So the early-warning building's effect is literally geometric: it extends a lookahead segment downwind.** `DangerFlags` is `StayIndoors = 1, Evacuate = 2, UseTransport = 4, WaitingCitizens = 8` (`src/Game/Game.Events/DangerFlags.cs`), and the prefab authoring surface exposes only two booleans, `m_Evacuate` and `m_StayIndoors`, **assigned rather than or-ed** (`src/Game/Game.Prefabs/WeatherPhenomenon.cs`, the `Initialize` block: `if (m_Evacuate) flags = Evacuate; if (m_StayIndoors) flags = StayIndoors;`) — so ticking both yields `StayIndoors` only.
`EventUtils.MIN_IN_DANGER_TIME = 64u` and `FLOOD_DEPTH_TOLERANCE = 0.5f` are `public const` (`EventUtils.cs:8-10`) and are two more with **no reader in `src/`**, so under the unread-constant ruling (stated in full at the natural-resources finding) neither ships as a number. `InDanger { m_Event, m_EvacuationRequest, m_Flags, m_EndFrame }` (`src/Game/Game.Events/InDanger.cs`) is the per-object marker.

**The live disaster roster.** Reading `ecs_query` on `Game.Prefabs.EventData` at 1.6.0f1 returns 18 event prefabs, of which the environmental ones are: **Lightning Strike, Tornado, Hail Storm** (the three `WeatherPhenomenonData` carriers), **Flood, Tsunami** (the two `WaterLevelChangeData` carriers), **Forest Fire, Building Fire** and **Building Collapse**. The rest are health, crime, traffic, tourism and calendar events. The six checked live — Flood, Tsunami, Tornado, Hail Storm, Lightning Strike and Forest Fire — each carry a `Locked` component whose enabled bit was `false` in the user's city, i.e. all unlocked. `Locked` is enableable, so `HasComponent` returns true either way and only `IsComponentEnabled` answers the question. Their live `WeatherPhenomenonData` (prefab values, recorded here and not shippable): Tornado `m_OccurenceProbability 0.3, m_DamageSeverity 2000, m_HotspotInstability 0.5, radius 150-300, duration 60-360 s, temperature 10-30, cloudiness 0.12-1, Evacuate`; Hail Storm `0.1, 10, 0.3, 300-500, 15-90 s, temperature 0-15, StayIndoors`; Lightning Strike `5, 0 damage, 0.1, 500-1000, 30-60 s, temperature 10-50, rain 0.3-1, lightning interval 60-60, StayIndoors`. **Lightning's zero `m_DamageSeverity` is why it spawns even with natural disasters switched off** — the gate at `WeatherHazardSystem.cs:68` tests `m_DamageSeverity != 0`.

**Fire is the other environmental event family and it sits on the same rails.** `FireHazardSystem` (`SystemOrder.cs:431`, interval 4096) queries anything with `Building` or `Tree` and without `FireStation`, `Placeholder`, `OnFire`, `Deleted`, `Overridden` or `Temp` (`src/Game/Game.Simulation/FireHazardSystem.cs:291-307`) and rolls the hazard factor built from temperature and `noRainDays`. `OnFire { m_Event, m_RescueRequest, m_Intensity, m_RequestFrame }` (`src/Game/Game.Events/OnFire.cs`) is the marker; the dispatch half is `city-services-and-coverage`'s.

### The rain-driven Flood does not fire at 1.6.0f1, because its driver is never registered

`SoilWaterSystem` (`src/Game/Game.Simulation/SoilWaterSystem.cs:21`) is a full `CellMapSystem<SoilWater>` at 128² with `kUpdatesPerDay = 1024` and `kLoadDistribution = 8` (`:220-224`). Its job (`:24-199`) does three things: diffuses soil moisture between neighbours weighted by terrain height (`HandleInterface`, `:61-76`), adds rain as `m_RainMultiplier * pow(2 * max(0, precipitation - 0.5), 2)` and exchanges moisture with the surface-water depths (`:119-197`), and **runs the flood counter**:

```
rain    = max(0, pow(2 * max(0, precipitation - 0.5), 2))
counter = max(0, 0.98 * counter + 2 * rain - 0.1)
if counter > 20 and no flood exists:   create a Flood event entity from the flood prefab
else if a flood exists:
    if counter == 0:  delete it
    else:             WaterLevelChange.m_Intensity = max(0, (counter - 20) / 80)
```

(`:78-141`). That is the whole rain-flood mechanism, and it is a complete, coherent formula.

**`SoilWaterSystem` is registered into no update phase.** A grep for the name over `src/` returns: `SystemOrder.cs:880` (`UpdateAfter<PostDeserialize<SoilWaterSystem>>(SystemUpdatePhase.Deserialize)`), `WaterSystem.cs:398`/`:835` (a field and a `GetOrCreateSystemManaged`), `Game.Debug/SoilWaterDebugSystem.cs` (a gizmo view), and the generated registries. There is no `UpdateAt`, `UpdateBefore` or `UpdateAfter` for the system itself in any phase. **`OnUpdate` therefore never runs.** Confirmed live at 1.6.0f1: the system exists, `Enabled == True` (the ECS flag, which says nothing about membership in an update group), its map is 16384 cells and every sampled cell reads `m_Amount = 0, m_Max = 0, m_Surface = 0` — a map that has never been ticked. The `FloodCounterData` singleton exists (one entity; `SoilWaterSystem.OnCreate` creates it at `:276`) and nothing else in `src/` writes it: the only other references are `Game.Serialization/ClearSystem.cs:39` and `SerializerSystem.cs:188`.

**The second half of the same absence is in `WaterLevelChangeSystem`.** That system (`src/Game/Game.Simulation/WaterLevelChangeSystem.cs`, `SystemOrder.cs:518`, interval 4) drives `WaterLevelChange.m_Intensity` for `WaterLevelChangeType.Sine` with a full two-phase sine schedule (`:58-74`) and, for every other change type, executes an **empty switch branch** (`:75-79`, which the decompiler renders as `_ = waterLevelChangeData.m_ChangeType; _ = 2;`). The Flood prefab is `m_ChangeType = RainControlled` and the Tsunami prefab is `m_ChangeType = Sine` (read live at 1.6.0f1, with Flood `m_TargetType = River, m_EscalationDelay = 0, DangerFlags 0` and Tsunami `m_TargetType = Sea, m_EscalationDelay = 1, Evacuate`). So `RainControlled` has no driver on either side: nothing creates the event and nothing would animate it if something did.

**Live experiment (2026-08-18, throwaway mod, throwaway save).** Registering `SoilWaterSystem` into `GameSimulation` ran without error, but the loaded map read all zeros — 16384/16384 cells `m_Amount=0, m_Max=0` — and stayed dead: the diffusion's `0/0` NaN truncates to no-ops, while `m_Surface` was written one `kLoadDistribution` slice per tick, proving the tick ran. Re-seeding every cell to `OnCreate`'s `{m_Amount=1024, m_Max=8192}` brought the soil half up (amounts redistributed sanely, no NaN, no crash), and setting `FloodCounterData.m_FloodCounter` above 20 created `Flood` events from the flood prefab, which the counter's decay to 0 then deleted through `StopFlood`. Two caveats a reviving mod inherits: the system ships no `GetUpdateInterval`, so a bare `UpdateAt` runs it every tick — 256× the `kUpdatesPerDay = 1024` design cadence — and at that rate `StartFlood` over-created (8 events in one playback window), since its guard reads an entity list captured before the command buffer plays back. The rain input went unexercised: `precipitation.value` stayed 0 and the counter was driven directly, so the counter arithmetic itself stands on the decompile read above. The probe log is preserved in the test ground's evidence folder.

**`Verdict:` at 1.6.0f1 the Flood disaster is unreachable through its own mechanism.** The Tsunami is fully driven, and its schedule reserves `TsunamiEndDelay = round(WaterSystem.kMapSize / WaterSystem.WaveSpeed)` frames at the end of the event for the wave to cross the map — a public static property, computed rather than authored (`WaterLevelChangeSystem.cs:127`, used at `:60`). This is not a claim about intent and is re-checkable offline at the four cited sites: the missing registration, the empty branch, the single writer of `FloodCounterData`, and the prefab's change type. A live `ecs_query` on `Game.Events.WaterLevelChange` returned 0 entities in the user's city.

`Unconfirmed:` whether a mod registering `SoilWaterSystem` into `GameSimulation` restores the mechanism intact. The job reads `m_WaterSurfaceData`, `m_TerrainHeightData` and a `SoilWaterParameterData` singleton (live: `m_RainMultiplier 64, m_HeightEffect 0.1, m_MaxDiffusion 0.05, m_WaterPerUnit 0.001, m_MoistureUnderWater 0.5, m_MaximumWaterDepth 2, m_OverflowRate 1`) — all of which exist. `OnCreate` seeds every cell `m_Amount = 1024, m_Max = 8192` (`SoilWaterSystem.cs:303-308`), but the load path zeroes the map again (the system overrides neither `SetDefaults` nor `Deserialize`, so the base class's zeroing wins) and the live map reads all zeros including `m_Max`, so the diffusion term divides by zero on the first tick unless something re-seeds the capacities. Settling it means registering the system in a throwaway mod and watching one update.

### Who reads the pollution maps, and what changes when they do

A census by grepping `src/Game/` for each of the three system names and reading every simulation-side hit:

| Reader | Reads | Effect |
|---|---|---|
| `CitizenHappinessSystem.cs:1216-1251` | all three | `GetGroundPollutionBonuses` and `GetAirPollutionBonuses` → **health**, `-min(m_MaxAirAndGroundPollutionBonus, pollution / m_PollutionBonusDivisor)` scaled by `CityModifierType.PollutionHealthAffect`; `GetNoiseBonuses` → **wellbeing**, `-min(m_MaxNoisePollutionBonus, noise / m_PollutionBonusDivisor)` with no modifier. All three are public statics. |
| `LandValueSystem.cs:123-140` | all three | `penalty = ground * m_GroundPollutionPenaltyMultiplier + air * m_AirPollutionPenaltyMultiplier + noise * m_NoisePollutionPenaltyMultiplier` |
| `RentAdjustSystem.cs:363-375` | all three | reuses the three happiness statics, and raises the `BuildingNotification.GroundPollution` / `AirPollution` / `NoisePollution` icons when the bonus sum falls below `2 * m_XPollutionNotificationLimit` |
| `ZoneSpawnSystem.cs:267-272` | all three | passes `float3(ground, noise, air)` into `SelectBuilding`, so pollution gates which building spawns on a lot |
| `HouseholdFindPropertySystem.cs:121-127`, `:193`, `:533`, `:602` | all three | `PropertyUtils.GetPropertyScore` / `GetGenericApartmentQuality` — where a household chooses to live |
| `CitizenPathfindSetup.cs:546-552`, `:663` | all three | the same property score, inside the pathfinder's setup |
| `Game.Tools/ZoningInfoSystem.cs:108-119` | all three | the zoning tool's own preview sums all three unweighted |
| `NaturalResourceSystem.cs:133-134` | ground, noise | fertility scarring and fish suppression |
| `GroundWaterPollutionSystem.cs:24` | ground | aquifer contamination |
| `ObjectPolluteSystem.cs:47-50` | ground, air | plant health |
| `PollutionTriggerSystem.cs:51`, `:76` | air | averages the air-pollution happiness bonus over every non-commuter household and raises `TriggerType.AverageAirPollution` |
| `Game.Rendering/OverlayInfomodeSystem.cs`, `NetColorSystem`, `Game.UI.InGame/PollutionInfoviewUISystem.cs`, `Game.UI.Tooltip/PollutionTooltipSystem.cs`, `Game.Debug/PollutionDebugSystem.cs` | all three | presentation |

`ObjectPolluteSystem` (`src/Game/Game.Simulation/ObjectPolluteSystem.cs`, `SystemOrder.cs:436`, interval `262144 / (32*16)` with `kUpdatesPerDay = 32`, `:80`, `:94-97`) is the plant-damage half and its whole formula is one line (`:50`):

```
Plant.m_Pollution = saturate(m_Pollution + (m_PlantGroundMultiplier * ground
                                          + m_PlantAirMultiplier   * air
                                          - m_PlantFade) / kUpdatesPerDay)
```

`Plant.m_Pollution` then scales the tree's growth and its wood yield: `ObjectUtils` returns `amount * (1 - plant.m_Pollution) * (1 - totalDamage)` and `amount * (1 - plant.m_Pollution)` at `src/Game/Game.Objects/ObjectUtils.cs:401` and `:423`. **`Verdict:` the wiki's "If near trees it kills them" is an overstatement**; pollution saturates a `[0,1]` sickness value that scales yield to zero and never deletes the tree. The claim is at https://cs2.paradoxwikis.com/index.php?title=Pollution&action=raw.

### How a mod reads and writes one of these maps, verified live

**Read.** Get the system, ask for the map read-only, respect the handle, register as a reader:

```csharp
var pollutionSystem = World.GetOrCreateSystemManaged<GroundPollutionSystem>();
NativeArray<GroundPollution> map = pollutionSystem.GetMap(readOnly: true, out JobHandle deps);
// schedule against deps, then:
pollutionSystem.AddReader(myJobHandle);
```

Confirmed live at 1.6.0f1 through `eval`: `GroundPollutionSystem.GetMap(true, out var deps)` returned a 65,536-element array and indexing it returned a value. The corpus's one worked example is `Time2Work/NightShift/Systems/Time2WorkAttractionSystem.cs:106` (`m_TerrainAttractivenessSystem.GetData(true, out dependencies)`) with the paired `AddReader` at `:119` and the terrain height reader at `:118`, and it calls the owning system's own static evaluator inside the job (`:240`) rather than reimplementing it. `Tree_Controller/Tree_Controller/Systems/LumberAndPauseTreeGrowthSystem.cs:189` registers `m_NaturalResourceSystem.AddReader(jobHandle4)` the same way.

**Write.** Same call with `readOnly: false`, and `AddWriter` afterward. Confirmed live: writing `map[10000] = new GroundPollution { m_Pollution = 12345 }` through `eval` and reading it back returned 12345 (reverted immediately afterward). **One trap the live probe surfaced:**

- `GetMap(readOnly: false, out deps)` returned a handle whose `IsCompleted` was `false`. The array is handed over regardless. Touching it from the main thread without completing the handle, or scheduling a job without chaining `deps`, is a race the shipped build will not catch — the collections safety checks are compiled out of the shipped assemblies (`docs/SOURCES.md` entry 15 owns why). The probe above got away with it only because the simulation was paused.

**The write is persistent and versioned.** Every cell map serializes through the base class, so a mod that writes one is writing the save. `T : IStrideSerializable` lets the base class write a strided block for compression: `AirPollution` and `GroundPollution` return 2, `NoisePollution` and `NaturalResourceAmount` return 4, `GroundWater` 6, `SoilWater` 8, `Wind` `sizeof(float2)` (each struct's `GetStride`). Two of them carry save-version branches worth knowing: `GroundPollution.Deserialize` skips a retired delta field between `Version.groundPollutionDelta` and `Version.removeGroundPollutionDelta` and reports stride 4 for older contexts (`GroundPollution.cs:20-37`), and `NaturalResourceCell.Deserialize` reads the fish amount **only when `context.format.Has(FormatTags.FishResource)`** (`NaturalResourceCell.cs:36-40`) — a save from before *Bridges & Ports* has no fish, and `NaturalResourceSystem.PostDeserialize` forces a full recompute in that case (`:401-408`).

**Two arithmetic slips in `NaturalResourceCell` that a reader will trip over.** `GetUsedResources()` returns `float4(fertility.m_Used, ore.m_Used, oil.m_Used, oil.m_Used)` — the `w` component repeats oil instead of reading fish (`NaturalResourceCell.cs:53-56`), while the parallel `GetBaseResources()` reads all four correctly (`:48-51`). And `GetStride` sums fertility, ore and oil only, returning 12 where the struct writes 16 bytes when the fish tag is present (`:43-46`). Both are single lines and re-checkable offline, and `GetUsedResources` has one caller outside the serialization path: `Game.Areas/AreaResourceSystem.cs:594` subtracts its result from the base amounts at `:601`, so a district's fish figure is discounted by oil's usage rather than fish's.

### The map layers reach the UI as a heatmap enum and four indicator bindings

`Game.Rendering.HeatmapData` is the closed enum of every layer the game can render onto the terrain (`src/Game/Game.Rendering/HeatmapData.cs:3-25`): `None, GroundWater, GroundPollution, AirPollution, Wind, WaterFlow, TelecomCoverage, Fertility, Ore, Oil, LandValue, Attraction, Customers, Workplaces, Services, Noise, WaterPollution, Population, GroundWaterPollution, Fish` — twenty members, and it is the closest thing the game has to a roster of "which map layers exist as a visible thing". `OverlayInfomodeSystem` switches on it and runs one small job per layer, each taking `CellMapData<T>` from the owning system and writing a `half4` texture, registering `AddReader` afterward (`src/Game/Game.Rendering/OverlayInfomodeSystem.cs:38-552`, dispatch from `:737`). **Seventeen of the nineteen cases read a `CellMapSystem`; only two do not** — `WaterFlow` (`:830`) and `WaterPollution` (`:1026`) set a mask on `WaterRenderSystem`'s own GPU textures and schedule no job. `Attraction`, `Customers`, `Workplaces` and `Services` all read `AvailabilityInfoToGridSystem` (`:951-1011`), and `Fish` reads `NaturalResourceSystem` (`:906`).

The UI half is `PollutionInfoviewUISystem` (`src/Game/Game.UI.InGame/PollutionInfoviewUISystem.cs:23`), which publishes four `ValueBinding<IndicatorValue>` in the binding group **`pollutionInfo`**: `averageGroundPollution`, `averageWaterPollution`, `averageAirPollution`, `averageNoisePollution` (`:139`, `:189-192`), each an `IndicatorValue(min, max, value)` where the max is the happiness parameter's own cap and the value is the negated average bonus (`:238-241`). That is the route the sibling `coherent-gameface` plugin reads without touching the game (`docs/SOURCES.md` entry 9).

### Catalog gap: four entries in the mod catalog under-describe what they demonstrate here

The catalog is `plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md`; all 22 corpus directories map to an entry, so nothing is missing from it outright.

**`### Water Features` (yenyang/Water_Features)** — the five existing **Demonstrates** lines are accurate. Five additions, each with its evidence:

- *Retuning a running vanilla simulation by writing its public tuning fields every update rather than forking it — evaporation, fluidness, damping and the simulation-speed gate on the water system — with the pre-mod damping captured once at system creation as the only record of it, and a companion that swaps back to that value while the terrain tool is active and counts a cooloff down before restoring the mod's.* Source: `Water_Features/Systems/ChangeWaterSystemValues.cs:109`, `:115`, `:131-187`; `Water_Features/Systems/TidesAndWavesSystem.cs:155-191`.
- *Two vanilla simulation systems switched off from a setting, paired with a one-shot cleanup that reverses what they already did — the event component, the damage component, the entry in each affected entity's notification-icon buffer, and the standalone icon entities — because disabling a system does not undo its output.* Source: `Water_Features/Systems/ChangeWaterSystemValues.cs:112-113`, `:126-127`; `Water_Features/Settings/WaterFeaturesSettings.cs:582-594`; `Water_Features/Systems/RemoveFloodedSystem.cs:57-128`.
- *Reflection into the climate system's private state for facts it publishes no accessor for — the current climate entity, the normalized date, and the mean-precipitation calculation — with the date read back through its string form because the property's value reads zero; the corpus's other climate readers use the public members instead, so read this for the reflection helper and not as the way to ask what season it is.* Source: `Water_Features/Systems/SeasonalStreamsSystem.cs:262-264`, `:288-290`, `:310-316`; `Water_Features/Utils/ReflectionExtensions.cs`; contrast `Tree_Controller/Tree_Controller/Systems/DeciduousSystem.cs:100-122`.
- *A mod-created simulation entity standing in for a global the game does not expose: a bare two-component water source at the world origin with a zero radius, holding the sea floor while every real sea source oscillates around it, identified by that zero radius in every job that iterates sources, and destroyed from five separate call sites including the before-serialize one so it never reaches the save.* Source: `Water_Features/Systems/TidesAndWavesSystem.cs:56-63`, `:194-238`, `:274-277`; `Water_Features/Systems/BeforeSerializeSystem.cs:143`; `Water_Features/Tools/CustomWaterToolSystem.cs:1099`; `Water_Features/Systems/DisableWavesAndTidesSystem.cs:63`; `Water_Features/Settings/WaterFeaturesSettings.cs:445`. (The game's own side of this is `WaterSimulation.cs:523`, which skips any source whose radius is 0.)
- *Calling the game's own validity calculation in a retry loop, growing the input until it stops returning the failure value and reporting the adjustment to the player, because the vanilla call reports an unusable result as a plain number with no error.* Source: `Water_Features/Tools/CustomWaterToolSystem.cs:1061-1089`.

One correction offered rather than asserted: the entry credits the before-serialize pattern with making the save load correctly for someone who removes the mod, which is right, but the whole system self-gates on `WaterSystem.UseLegacyWaterSources` (`Water_Features/Systems/BeforeSerializeSystem.cs:137`), so the guarantee is scoped to that half of the game's water model.

**`### Realistic Trips` (ruzbeh0/Time2Work)** — two additions:

- *Replacing the game's weather sampler wholesale from a prefix that returns false, rebuilding the sample struct by evaluating the climate prefab's own temperature, precipitation, cloudiness and aurora curves at a rescaled time, which is how a mod that changes the length of the year keeps the seasons landing where the map's climate says they should.* Source: `Time2Work/NightShift/Patches/Time2WorkPatches.cs:171-191`.
- *Consuming a vanilla map layer from a forked system: the terrain-attractiveness cell map taken through its owning system's data accessor, carried into a Burst job beside the terrain heights and the parameter singleton, scored by the game's own static evaluator rather than a reimplementation, with both the cell-map reader and the terrain height reader registered back after the schedule.* Source: `Time2Work/NightShift/Mod.cs:102`, `:142`; `Time2Work/NightShift/Systems/Time2WorkAttractionSystem.cs:106-108`, `:117-119`, `:179`, `:240`.

**`### Tree Controller` (yenyang/Tree_Controller)** — the resource-area line already there is confirmed. One addition:

- *Reading the current season the way the game defines it — the climate system's current climate entity resolved to its prefab, then that prefab asked which season the current date falls in — and handing the result into a Burst job as a plain enum, on an update-frame slice so only a fraction of the entities are touched per update.* Source: `Tree_Controller/Tree_Controller/Systems/DeciduousSystem.cs:100-127`; `Tree_Controller/Tree_Controller/Systems/ReloadFoliageColorDataSystem.cs:304-335`.

**`### Recolor` (yenyang/Recolor)** — one addition:

- *The same season read used as a filter over prefab data: the live season matched against each colour variation's group identifier, with the climate prefab resolved lazily and cached, and both resolution failures logged and answered with a false rather than a throw.* Source: `Recolor/Recolor/Systems/SelectedInfoPanel/SIPColorFieldsSystem.OtherPrivateMethods.cs:47-60`; `.../SIPColorFieldsSystem.Main.cs:253-256`, `:498`; `.../SIPColorFieldsSystem.PropertiesAndPublicMethods.cs:168-198`.

Three more are offered as maintainer calls rather than proposed: **Road Builder** attaches `NetPollution` unconfigured to every generated non-pathway road (`RoadBuilder/Utilities/NetworkPrefabGenerationUtil.cs:534`, `:588-591`) — the entry's utility-carriage line already covers the shape; **Move It**'s reflected write to `TerrainSystem`'s private `m_UpdateArea` (`CS2-MoveIt/Code/MoveIt/Tool/MoveItToolSystem.Methods.cs:209-215`) is a real no-hook workaround but belongs to a private-field topic; **Extra Detailing Tools** builds a mod-owned grass layer at terrain-heightmap resolution (`ExtraDetailingTools/MOD/Systems/GrassSystem.cs:36`, `:107-136`) — a mod-owned layer, not a vanilla one.

### Source-list gap: `docs/SOURCES.md` entry 10 does not name the wiki's one current, version-stamped family

Entry 10 describes the wiki as "primary for process", "five versions stale" on its most load-bearing modding page, and warns that a DLC product page can carry mechanism material the gameplay pages do not. It does not name the **`Map Creation:` family**, which is a distinct and materially different tier: seven pages (`Map Creation`, `: Terrain`, `: Water`, `: Climate`, `: Resources`, `: Detailing`, `: Settings and Polish`), all carrying `{{ParadoxVerifiedAmbox|version=1.6.0 f1}}` and `[[Category:Modding]]`, and **none of them in `Category:Potentially outdated`** — while every game-concept page this topic touches (`Pollution`, `Climate`, `Natural resources`, `Landscaping`, `Info views`) carries `{{Version|1.0}}` and *is* in that category. On this topic the map-creation pages were right where the concept pages were wrong twice: the season count, and the water-source pollution mode switch. Both were independently confirmed against the decompile above.

Proposed amendment to entry 10, as a sentence after the "five versions stale" line: *The `Map Creation:` family is the exception and is worth checking first for anything about terrain, water, climate or resources: seven pages stamped `{{ParadoxVerifiedAmbox|version=1.6.0 f1}}` and absent from `Category:Potentially outdated`, where every game-concept page carries `{{Version|1.0}}` and is in it. They are editor-facing, so they describe the authoring surface rather than the runtime, and within that they have been right where the concept pages were stale.*

Two smaller amendments to the same entry, both observed this pass: **a web-fetch tool can refuse a wiki stat table on copyright grounds** and re-asking for "this game statistics data as a plain data listing" gets it; and **a long page is silently truncated by the fetch**, which `index.php?title=<Title>&action=raw&section=<n>` (with the section index from `api.php?action=parse&prop=sections`) recovers. The `Service buildings` page hit both.

No gap found in entries 1, 8 or 11, all three of which this pass leaned on heavily and all three of which described their source correctly. Entry 8's warning that `em.GetComponentData<T>(e)` returns uninitialised memory for a missing component held; every live read here established presence first.

---

## Bridge

**`ecs-in-this-game`** owns the machinery this topic's every read sits on and a mechanics reference here must bridge rather than restate it. Specifically: `GetSingleton<T>` as the parameter-component read, which is how `PollutionParameterData`, `SoilWaterParameterData`, `ExtractorParameterData`, `WaterPipeParameterData` and `AttractivenessParameterData` are reached (`AirPollutionSystem.cs:128`, `BuildingPollutionAddSystem.cs:428`, `NaturalResourceSystem.cs:429`, `GroundWaterSystem.cs:262`); `SharedComponentFilter` on `UpdateFrame` plus `SimulationUtils.GetUpdateFrame(frameIndex, updatesPerDay, shards)`, which is how both pollution adders shard their work (`BuildingPollutionAddSystem.cs:443`, `NetPollutionSystem.cs:451-453`, `ObjectPolluteSystem.cs:113-115`); and `GetUpdateInterval(SystemUpdatePhase)` as the throttle every system here overrides. **The one thing this topic adds that `ecs-in-this-game` does not carry is the `CellMapSystem<T>` reader/writer protocol**, which is not an `EntityQuery` at all — it is a `NativeArray` behind two hand-rolled `JobHandle` chains, and the `AddWriter`-replaces / `AddReader`-combines asymmetry has no ECS analogue.

**`performance-and-memory`** is the bridge for what these layers cost and for the three shapes a mod will get wrong. First, **three of the four systems that walk a 65,536-cell map do it in a single serial `IJob`** — `AirPollutionSystem` (`:20`, `IJob`), `GroundPollutionSystem` (`:20`, `IJob`), `GroundWaterSystem` (`:21`, `IJob`, three full passes per update) — while `NoisePollutionSystem` uses `IJobParallelFor` with batch sizes 4 and 64 (`:99`) and `NaturalResourceSystem` uses `IJobParallelFor` at one row per index (`:486`). A mod adding a fifth full-map pass should copy the parallel shape, not the serial one. Second, **the GPU layers are unreachable from a job at all**: water and snow are compute-shader simulations whose only CPU face is an async readback (`WaterSimulation.cs:275-330`, `SnowSystem.cs:234-241`, `WaterSystem.cs:917-919`), so "sample the water depth" is a readback-latency question rather than a memory question. Third, `BuildingPollutionAddSystem`'s per-source `stackalloc float[n*n]` scales with the square of the radius parameter inside a Burst job (`:120-121`).

**`utilities-and-flow-networks`** is the other half of every water sentence here, and the seam is the pump. This topic owns the aquifer (`GroundWaterSystem`), the surface-water body and its pollution (`WaterSourceData`, the GPU sim, `SewageOutletAISystem`'s source write); that topic owns everything downstream of `WaterPipeEdge.m_FreshPollution`, including `WaterPipePollutionSystem`'s node-and-edge propagation (`src/Game/Game.Simulation/WaterPipePollutionSystem.cs:20-109`), `DispatchWaterSystem`'s efficiency penalty (`:142-176`) and `WaterTradeSystem`. The crossing point is `WaterPumpingStationAISystem.cs:178-182`, where an intake concentration becomes a graph value, and `SewageOutletAISystem.cs:97-121`, where a graph value becomes an environmental one. That topic's own ruled finding — that water and sewage pipes carry the solver's unconstrained sentinel as their capacity — has no counterpart here: every layer in this topic is genuinely bounded — 32767 for the pollution shorts, each cell's `m_Max` for groundwater, 10000 for a map-generated or fish natural-resource base and 65535 under the game-mode boost.

**`economy-and-companies`** owns what a deposit is worth; this topic owns where it sits and how it empties. The seam is `Extractor { m_ResourceAmount, m_MaxConcentration, m_ExtractedAmount, m_WorkAmount, m_HarvestedAmount, m_TotalExtracted, m_WorkType }` (`src/Game/Game.Areas/Extractor.cs:54-68`): that topic decides `m_ExtractedAmount` and this one turns it into `m_Used` through `AreaLotSimulationSystem.ExtractNaturalResources` (`:726-851`). Two facts cross the line in the other direction and that reference will want them: **the specialised-industry gate is `MapFeatureElement.m_Amount` on the area**, rebuilt by `AreaResourceSystem`/`NaturalResourceSystem` (`SystemOrder.cs:259`, `:412`), and **`ProcessEstimate` on a zone prefab is the join from a resource to a recipe** that `economy-and-companies` already ships. Pollution reaches that topic too: `CityModifierType.IndustrialGroundPollution` and `IndustrialAirPollution` scale industrial emission (`BuildingPollutionAddSystem.cs:550-551`), and building efficiency gates emission entirely at `efficiency > 0` (`:529`).

**`citizens-and-households`** is where the three pollution maps actually change the game, and the three public statics are the seam: `CitizenHappinessSystem.GetGroundPollutionBonuses`, `GetAirPollutionBonuses` and `GetNoiseBonuses` (`:1216-1251`). That reference owns the happiness budget those bonuses feed; this one owns what produces the number they read. The same three statics are reused by `RentAdjustSystem` (`:363-365`) and the property-choice path (`HouseholdFindPropertySystem.cs:193`, `CitizenPathfindSetup.cs:663`), so **a mod that changes pollution changes where households move**, which is the least obvious downstream effect in the topic. The other crossings are `PollutionTriggerSystem`'s `TriggerType.AverageAirPollution` (`:76`) and the evacuation path — `InDanger`, `DangerFlags`, and citizens leaving a shelter against `DisasterConfigurationData.m_EmergencyShelterDangerLevelExitProbability` (`src/Game/Game.Prefabs/DisasterConfigurationData.cs`).

Two more topics are touched: `simulation-time-and-units` owns what `ClimatePrefab.kYearDuration = 12` and `TimeSystem.daysPerYear` mean in real time, and `city-services-and-coverage` owns fire, rescue, shelters and the maintenance depot that a snow request routes to. (Both bridged from the entry file as of 2026-08-18 — the ticket's bridge list is a floor every shipped topic exceeds, not a ceiling; this file first recorded them as outside the approved set.)

---

## Dead ends

- **The mod corpus contributes nothing to seven of this topic's nine sub-areas.** The independent grep sweep over all 22 repositories returned **zero files** for each of `GroundPollutionSystem`, `AirPollutionSystem`, `NoisePollutionSystem`, `WaterPipePollutionSystem`, `GroundWaterSystem`, `SoilWaterSystem`, `WindSystem`, `SnowSystem`, `CellMapSystem`, `PollutionParameterData`, `WeatherPhenomenon`, `NaturalResourceCell` and `DisasterConfigurationData`. `GroundWater` appears only inside the identifier `ErrorType.NoGroundWater` (`Anarchy/Systems/Common/AnarchyUISystem.cs:51`). So there is no worked example and no gotcha in the wild for ground, air, noise or piped-water pollution, for groundwater, soil water, wind or snow, or for weather phenomena and disasters. What the corpus does reach is water sources (12 files, all `Water Features`), the water simulation's tuning fields, terrain height sampling, the season read (5 mods) and exactly one cell-map read (`Time2Work`'s attractiveness fork).
- **Nine catalog entries were checked against their **Demonstrates** halves and rejected.** *Area Bucket* (its "cell map" is a mod-owned spatial hash for point merging, `AreaBucket/Systems/AreaBucketToolJobs/MergePoints.cs:45`, and its `WaterSystem` lines are commented out); *Advanced Line Tool*, *Network Tools*, *Platter* (all three sample `TerrainHeightData` and `WaterSurfaceData<SurfaceWater>` for placement height and register their readers correctly — no map layer written); *Move It* (a reflected write to `TerrainSystem.m_UpdateArea` to force a render refresh, not a data change); *Extra Detailing Tools* (a mod-owned grass layer at terrain resolution, not a vanilla one); *Anarchy* (`ErrorType.NoGroundWater` is one member of a placement-error roster and touches no groundwater state); *Info Loom* (its "ForestFireHazard" is the name of a local-effect entry on a `LocalEffectProvider`, for panel colouring); *Road Builder* (attaches `NetPollution` with defaults and demonstrates nothing about the layer). *Scene Explorer*, the corpus's own inspection tool, returned zero hits for every type in the sweep list.
- **The wiki answers none of the five questions this topic exists for, and its own pages disagree with each other.** No page names a system, a component or a cell map for pollution; the word "cell" appears three times in the whole corpus and only `Map Creation: Resources` uses it structurally ("Resources are stored internally on a 256 × 256 resource grid" — which the decompile confirms as `NaturalResourceSystem.kTextureSize`, and which is the only cell-map dimension the wiki states). `Common ECS Components` documents `Game.Common` only and lists `Terrain` with an empty description; `Systems and Components catalog` contains no environmental system at all. `Day-night cycle` reports `missing` from the API and the only day-night documentation anywhere is one paragraph in `Climate`. `Landscaping` has no water section — its `Piers and quays` and `Bicycle paths and quays` headings are empty. The `Citizens` page assigns air and ground pollution to wellbeing in one section and the `Pollution` page assigns them to health; the decompile settles it — health for ground and air, wellbeing for noise (`CitizenHappinessSystem.cs:1216-1251`).
- **`Category:Potentially outdated` covers every game-concept page this topic touches**, verified through `api.php?action=query&list=categorymembers&cmtitle=Category:Potentially%20outdated&cmlimit=500`: `Pollution`, `Climate`, `Natural resources`, `Landscaping`, `Info views`, plus `Citizens`, `Services`, `Policies`, `Zoning`, `Service buildings` and `Progression`. Thirty entries, no continuation token.
- **Every plausible standalone wiki title in this area is a redirect with no content of its own.** `Weather`, `Seasons`, `Disasters`, `Tornado`, `Hail storm`, `Forest fire`, `Rain` → `Climate`; `Ground pollution`, `Air pollution`, `Noise pollution`, `Groundwater pollution`, `Industrial Ground Pollution` → `Pollution`; `Groundwater`, `Fertile land`, `Forest`, `Ore`, `Oil` → `Natural resources`; `Terraforming`, `Vegetation`, `Trees` → `Landscaping`. `Seasons` redirects to `Climate#Seasons`, a **broken anchor** — the heading is `Months and seasons`. No page exists for water pollution as a title, for fish, for lightning, for drought, for earthquake or for flooding. `[[Event Journal]]`, linked from `Climate#Natural disasters`, is a redlink.
- **`Game.Prefabs.EarlyDisasterWarningSystem` is not a system.** It is a two-method `ComponentBase` adding the `Game.Buildings.EarlyDisasterWarningSystem` tag (`src/Game/Game.Prefabs/EarlyDisasterWarningSystem.cs:13-21`). The mechanism lives in `Game.Events.InitializeSystem` and `WeatherPhenomenonSystem`, as recorded above. The name will send a reader to the wrong file.
- **`ClimateSystem.wind`, `WindSimulationSystem.m_ClimateSystem`, `IndustrialDemandSystem.m_ClimateSystem` and `TrafficSpawnerAISystem.m_ClimateSystem` are all assigned and never read.** Four separate greps. Each looks like a link that does not exist.
- **Eleven declared constants in this topic have no reader anywhere in `src/`**: `GroundWaterSystem.kMaxGroundWater` and `kMinGroundWaterThreshold`; `NaturalResourceSystem.MAX_BASE_RESOURCES`, `FERTILITY_REGENERATION_RATE`, `FISH_REGENERATION_RATE`, `UPDATES_PER_DAY` and `EDITOR_ROWS_PER_TICK`; `WeatherHazardSystem.UPDATES_PER_DAY`; `FireHazardSystem.UPDATES_PER_DAY`; `EventUtils.MIN_IN_DANGER_TIME` and `FLOOD_DEPTH_TOLERANCE`. Each verified by grepping its name over the decompile and finding only the declaration. Two of them (the regeneration pair) are 32× the literal the job actually uses and one (`MAX_BASE_RESOURCES`) agrees with its literal exactly, so the class is not uniformly wrong — it is uniformly unverifiable from the name alone. **A reference that ships C# constants because they are offline-checkable would ship several wrong numbers here.** Ruled (2026-08-18; conflicts.md, restated in full at the natural-resources finding): none of the eleven ships as a number, the operative literals ship cited to their consuming lines, and the hazard ships once as a trap.
- **`AreaLotSimulationSystem.GetUnlimitedTotalAmount` is public, uncalled and arithmetically broken** (`:1240-1243`, integer division into `math.log`, and a misplaced `/ mu`). Recorded as a trap rather than a mechanism.
- **`Assert.IsTrue` calls survive in the shipped `Game.dll`.** `GroundWaterSystem`, `NaturalResourceSystem` and `AreaLotSimulationSystem` all open with `#define UNITY_ASSERTIONS` in the decompiled output and carry live `Unity.Assertions.Assert` / `UnityEngine.Assertions.Assert` calls in job bodies (`GroundWaterSystem.cs:38-41`, `:69-76`, `:206-207`; `NaturalResourceSystem.cs:416-418`). Two of `ConsumeGroundWater`'s look unsatisfiable as written (`Assert.IsTrue(Mathf.Approximately(totalAvailable, 0f))` on a quantity that has just been decremented by the consumption, `:206-207`). Whether they actually fire is a runtime question no static read settles, and pursuing it was dropped: the game has been running for hours in the user's city without a groundwater pump complaint, which is weak evidence and not a verdict.
- **Reading a live cell map's maximum was not attempted.** `eval` supports no loops, and no established route builds an `EntityQuery` or a job inside it (`docs/SOURCES.md` entry 8). Individual indices read fine and several were sampled; a distribution would need a throwaway mod.
- **The base game's prefab values for `PollutionPrefab`, `SoilWaterPrefab` and the climate prefabs were not read out of `resources.assets`.** All of them are base-game prefabs behind the Unity serialized-file parser `docs/SOURCES.md` entry 5 describes; the running game answered every one of them in a single component read, which is the route that entry names as the shorter road. The `Prefabs*.cok` zip route was not checked for a DLC climate or weather prefab, so whether a content pack adds a climate, a season or a weather phenomenon is unestablished — the live roster of three `WeatherPhenomenonData` carriers and two `WaterLevelChangeData` carriers is one install's DLC set, not a fact about the base game.
