# Cell maps

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`Game.Simulation.CellMapSystem<T>` (`src/Game/Game.Simulation/CellMapSystem.cs`) is the whole of the game's cell-map machinery, and every layer in this topic is a subclass of it.
The world extent is shared — `public static readonly int kMapSize = 14336`, metres on one axis, the map centred on the world origin so cell 0 sits at `-kMapSize / 2` — while resolution is per layer: each subclass calls `CreateTextures(kTextureSize)` in `OnCreate` with its own `public static readonly int kTextureSize`.
Other topics own further subclasses; the layer roster re-derives with one grep for `: CellMapSystem<` over `src/`.
This topic's layers:

| Layer | System (`src/Game/Game.Simulation/`) | `kTextureSize` |
| --- | --- | --- |
| Ground pollution | `GroundPollutionSystem` | 256 |
| Air pollution | `AirPollutionSystem` | 256 |
| Noise pollution | `NoisePollutionSystem` | 256 |
| Groundwater | `GroundWaterSystem` | 256 |
| Natural resources | `NaturalResourceSystem` | 256 |
| Soil water | `SoilWaterSystem` | 128 |
| Terrain attractiveness | `TerrainAttractivenessSystem` | 128 |
| Wind | `WindSystem` | 64 |

## The reader/writer protocol

- `GetMap(bool readOnly, out JobHandle dependencies)` returns the raw `NativeArray<T>` plus the handle to respect: `readOnly: true` hands you the write chain alone, `readOnly: false` hands you writes and reads combined.
- `GetData(bool readOnly, out JobHandle dependencies)` returns the same array wrapped in `CellMapData<T> { m_Buffer, m_CellSize, m_TextureSize }` — what a Burst job wants, since `m_CellSize` is computed as `kMapSize / textureSize` and needs no constant of its own.
- `AddReader(JobHandle)` registers a read; `AddWriter(JobHandle)` registers a write.
- Coordinate conversion is static and public: `GetCellCoords(position, mapSize, textureSize)` returns `(0.5 + position.xz / mapSize) * textureSize`, `GetCell` floors it, `GetCellCenter` and `GetCellBounds` invert it.

The read, from a managed system:

```
var owner = World.GetOrCreateSystemManaged<GroundPollutionSystem>();
NativeArray<GroundPollution> map = owner.GetMap(readOnly: true, out JobHandle deps);
// schedule against deps combined with base.Dependency and every other source read, then:
owner.AddReader(myJobHandle);
```

A write is the same call with `readOnly: false` and `AddWriter` afterward.
`Game.Rendering.OverlayInfomodeSystem` is a vanilla worked example of the read — one small job per info-view layer, each taking `CellMapData<T>` from the owning system and registering `AddReader` after the schedule — and the editor's `Game.Tools.ApplyBrushesSystem` is the worked example of the write: its brush appliers are generic over `CellMapSystem<TCell>` and end in `AddWriter`.
The generic handle discipline — combine every provider's handle into the schedule, register back with each — is [`performance-and-memory`](../../technique/performance-and-memory/performance-and-memory.md)'s.

**`AddWriter` replaces the write chain, while `AddReader` combines into it.**
Two systems writing one map in the same frame without explicit ordering lose one of the handles, so a mod calling `AddWriter` after the owning system already did drops the owner's write.
Source: `src/Game/Game.Simulation/CellMapSystem.cs`.

**`GetMap` hands the array over whether or not its handle has completed.**
Scheduling a job without chaining `dependencies`, or touching the array from the main thread without completing the handle first, is a race the shipped build will not diagnose.
Source: `src/Game/Game.Simulation/CellMapSystem.cs`.

## Samplers

Each pollution and resource layer publishes its own static bilinear sampler, and they do not agree with each other:

| Sampler | Anchoring | Out of range |
| --- | --- | --- |
| `GroundPollutionSystem.GetPollution` | cell corner | returns zero, and the +1 neighbours read zero at the far edge |
| `AirPollutionSystem.GetPollution`, `NoisePollutionSystem.GetPollution` | cell centre | clamps the cell index to `[0, kTextureSize - 2]`; the lerp fraction is unclamped |
| `GroundWaterSystem.GetGroundWater` | cell centre, unclamped | an off-map neighbour reads as an all-zero cell, like ground pollution |
| `NaturalResourceSystem.GetFertilityAmount` / `GetOilAmount` / `GetOreAmount` / `GetFishAmount` | cell centre, index clamped | the private `GetResource` under them takes a `Func<NaturalResourceCell, NaturalResourceAmount>` delegate, so they are managed-only — a Burst job indexes the buffer itself, as the game's own jobs do |

**The index clamp does not clamp the sample: the air, noise and resource samplers extrapolate at the edge.**
The bilinear fraction comes from the raw position while only the cell index is clamped, so a sample off the map — or in the outermost half-cell ring inside it — extrapolates from the two edge cells: negative for air and noise, and wrapped through the resource samplers' unchecked `ushort` cast; a ground-pollution or groundwater sample outside the map is 0.
Source: `src/Game/Game.Simulation/AirPollutionSystem.cs`, `src/Game/Game.Simulation/NoisePollutionSystem.cs`, `src/Game/Game.Simulation/NaturalResourceSystem.cs`, `src/Game/Game.Simulation/GroundPollutionSystem.cs`, `src/Game/Game.Simulation/GroundWaterSystem.cs`.

**Three resolutions are asserted equal at runtime.**
`NaturalResourceSystem.OnUpdate` asserts its `kTextureSize` equals ground pollution's and noise's, because its regeneration job indexes all three buffers with one running index; a mod changing one of those constants breaks the other two systems.
Source: `src/Game/Game.Simulation/NaturalResourceSystem.cs`.

## Serialization

Every cell map serializes through the base class — `Serialize`, `Deserialize` and `SetDefaults`, each scheduling a Burst job against the dependency chain — so a mod that writes a map is writing the save.
`SetDefaults` zeroes the map unless the subclass overrides it.
The structs declare `IStrideSerializable`, and two carry save-version branches worth knowing: `GroundPollution.Deserialize` skips a retired delta field for a range of older versions, and `NaturalResourceCell.Deserialize` reads the fish amount only when the save format has `FormatTags.FishResource`, with `NaturalResourceSystem.PostDeserialize` forcing a full recompute for saves without it.

(VOLATILE: every system, component, field, method, constant and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Rendering`, `Game.Tools` and the root `Game` namespace, at the files the table rows and traps cite.)
