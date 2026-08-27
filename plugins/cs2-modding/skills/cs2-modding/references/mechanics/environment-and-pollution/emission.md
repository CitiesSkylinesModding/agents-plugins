# Emission

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

Two systems write emission: `BuildingPollutionAddSystem` stamps buildings into the ground, air and noise maps, and `NetPollutionSystem` stamps roads and other nets into air and noise — no net writes the ground map — each sharded 16 ways over `UpdateFrame` so every source contributes once per 2048 frames.
Registration order matters within a frame: the ground fade runs before the building adder, the air update after it, and `NetPollutionSystem` after the noise swap — so a frame's net deposits land in `m_PollutionTemp` and are consumed by the next swap (`src/Game/Game.Common/SystemOrder.cs`).

## Per-building amount

Sources: `src/Game/Game.Simulation/BuildingPollutionAddSystem.cs`, `src/Game/Game.Prefabs/PollutionData.cs`, `src/Game/Game.Prefabs/ZonePollution.cs`, `src/Game/Game.Prefabs/PollutionModifier.cs`, `src/Game/Game.Prefabs/UpgradeUtils.cs`, `src/Game/Game.Buildings/PollutionEmitModifier.cs`.

`GetBuildingPollution` is a public static a mod can call, and the render side does:

```
if destroyed or abandoned:
    ground = air = 0
    noise  = destroyed ? 0 : 5 * lotSize.x * lotSize.y * m_AbandonedNoisePollutionMultiplier
else if efficiency > 0 and the prefab carries PollutionData:
    data = the prefab's PollutionData                       // ground, air, noise
    data += each installed upgrade's PollutionData          // upgrades flagged Inactive are skipped, and each is first scaled by its own PollutionEmitModifier
    if data.m_ScaleWithRenters and not a park and the building has a Renter buffer:
        count, education = summed over every household citizen and employee of every renter
        level  = SpawnableBuildingData.m_Level, or 5 for a non-spawnable
        factor = count > 0 ? 5 * count / (level + 0.5 * floor(education / count)) : 0
                                                            // education / count is C# integer division: the mean is floored
        all three channels *= factor
    if the zone is Industrial without the Office flag:
        apply CityModifierType.IndustrialGroundPollution and .IndustrialAirPollution
                                                            // noise is not modified
    per channel: data *= max(0, 1 + PollutionModifierData summed over installed upgrades)
else:
    data = 0
if (abandoned or a park) and renters exist:
    noise += householdCitizenCount * m_HomelessNoisePollution
afterwards, per instance, PollutionEmitModifier:
    data.m_X += m_XModifier * data.m_X, per channel         // a fraction of the scaled value
```

`PollutionModifier` requires `ServiceUpgrade` and its fields are ranged `[-1, 1]`, so an upgrade can zero a channel and cannot reverse it.
A zoned building gets its `PollutionData` from the zone instead: `ZonePollution` on the zone prefab bakes each channel as its own rate times the lot size, and only when the building prefab does not itself carry `Pollution`.

**`PollutionEmitModifier` is the supported per-instance emission hook.**
It sits on every building whose prefab carries `Pollution`, it is serialized, the game itself writes it at runtime (`BatteryAISystem` sets all three to -1 while an emergency generator idles), and it is applied after every other factor.
Source: `src/Game/Game.Prefabs/Pollution.cs`, `src/Game/Game.Buildings/PollutionEmitModifier.cs`, `src/Game/Game.Simulation/BatteryAISystem.cs`.

## The spatial stamp

Source: `src/Game/Game.Simulation/BuildingPollutionAddSystem.cs`.

```
weight cache: 256 entries of GetWeight(d, m_DistanceExponent) = 1 / max(20, pow(d, exponent)), indexed by 255 * d² / maxRadiusSq, rebuilt only when m_DistanceExponent changes; maxRadiusSq is the square of the largest of the three channel radii
for each cell whose centre lies within the channel's radius of the source:
    weight   = lerp over the cache at that cell's squared distance
    per-cell = pollution * multiplier * weight / (Σ weights * kUpdatesPerDay)
    if per-cell > 0.2:  map[cell].Add(ceil(per-cell))
```

- The `> 0.2` threshold discards a cell's whole contribution and the ceil rounds every survivor up: a weak source spread over many cells deposits nothing, one just above the threshold deposits a whole unit per cell.
- The weights are normalised, so `pollution * multiplier / kUpdatesPerDay` is the budget before the cut — what actually lands still moves with radius, since widening it pushes more cells under the threshold.
- The scratch buffer is `stackalloc float[n * n]` with `n = 3 + ceil(2 * radius * textureSize / mapSize)`: raising a radius parameter raises a stack allocation inside a Burst job.

## Vehicles into the road

Sources: `src/Game/Game.Simulation/CarNavigationSystem.cs` (the train system shares the shape in full; aircraft and watercraft drop the wear normalisation and never apply the wear channel at all), `src/Game/Game.Prefabs/VehicleSideEffects.cs`, `src/Game/Game.Prefabs/VehicleSideEffectData.cs`, `src/Game/Game.Net/Pollution.cs`.

```
s          = saturate(((distance / duration) / prefabMaxSpeed)²)   // duration 0 → the lane's max drive speed
sideEffect = lerp(m_Min, m_Max, s) * (min(1, distance / max(1, curveLength)), duration, duration)
             // channels are (wear, noise, air)
ApplyLaneEffectsJob: the lane owner's Game.Net.Pollution.m_Pollution += sideEffect.yz
```

Wear scales with distance travelled, noise and air with time spent on the lane, and the squared speed fraction is the lerp's `t` rather than a multiplier: a stopped vehicle still emits `m_Min` per second — and its lane time keeps multiplying it — while a vehicle at top speed emits `m_Max`.

## The road into the maps

Sources: `src/Game/Game.Simulation/NetPollutionSystem.cs`, `src/Game/Game.Prefabs/NetPollution.cs`, `src/Game/Game.Prefabs/NetPollutionData.cs`.

```
m_Accumulation = lerp(m_Accumulation, m_Pollution, 4 / kUpdatesPerDay); m_Pollution = 0
    // an exponential moving average: a road's emission ramps in and out over many updates
emitted = m_Accumulation * NetPollutionData.m_Factors        // (noise, air)
noise fans out to a (left, centre, right) triple the upgrades multiply:
    sound barrier both sides (0, 0.5, 0); left only (0, 0.5, 1.5); right only (1.5, 0.5, 0)
    primary and secondary beautification, each applied independently:
        both sides (0.5, 0.5, 0.5); left only (0.5, 0.75, 1); right only (1, 0.75, 0.5)
    each middle beautification (0.875, 0.5, 0.875)
radius = max(m_NetNoiseRadius, composition width / 2); "wide" below = radius above m_NetNoiseRadius
an edge below ground at both ends whose composition carries Tunnel is skipped outright; a node is skipped only when its own elevation is below ground on both axes and every connected edge with a composition is such a tunnel
node stamp: upgrades run per connected edge and the triples average, the side channels folding into one as (left + right) / 2; air lands on the node's own cell; noise is divided by 8, then the side amount lands on four cardinal points at ±radius and the centre amount on four inner points at ±radius/3 when wide, else 4× on the node cell
curve stamp: each map subdivides the curve into ceil(2 * length / its cellSize) samples; air splits evenly, one cell per sample; noise is divided by 4 * samples — the centre channel doubled before the upgrades, then halved again when wide — and lands per sample along the curve normal: the centre amount on two points at ±radius/3 when wide, else on the sample point, plus left and right at ±radius
every noise deposit splits bilinearly over four cells; air is written with no interpolation
```

**A sound barrier on one side is not half a barrier.**
The one-sided multipliers zero the near side and raise the far side to 1.5×, so adding a barrier can raise the noise stamped on the unprotected side.
Source: `src/Game/Game.Simulation/NetPollutionSystem.cs`.

(VOLATILE: every system, component, field, enum, method and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Prefabs`, `Game.Buildings`, `Game.Net`, `Game.Common`, `Game.City` and `Game.Zones`, at the files the sections cite.)
