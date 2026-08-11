# Save serialization: making mod data survive a save

**Baseline.** Decompiled game version 1.6.0f1, read at the checkout `C:\Users\Morgan\Documents\Projets\DecompiledCitiesSkylines2`. Mod corpus (22 repositories under `C:\Users\Morgan\Documents\Projets\cs2-third-party-mods\`) read 2026-08-04. Wiki not fetched: it has no page on this subject (see `## Dead ends`).
A fourth source appears in three findings: **the running game**, a 1.6.0f1 development build driven over the Mono soft debugger on 2026-08-04, with a city loaded that was written by an older build. Its results are cited as `live 1.6.0f1` and carry no path.

## Findings

### The whole mechanism in one paragraph

A save is a stream of length-prefixed buffers written by `EntitySerializer` (`src/Colossal.Core/Colossal.Serialization.Entities/EntitySerializer.cs`) and read back by `EntityDeserializer` (`EntityDeserializer.cs`).
The first buffer is uncompressed and holds the writing build's version, the compression format of every later buffer, and the full list of the writing build's format-tag names (`EntitySerializer.cs:660-673`).
Then come the component type table, the system type table, one buffer per archetype, and one buffer per system serializer (`EntitySerializer.Serialize<TWriter, TFormatTags>` at `EntitySerializer.cs:410`, its dispatch at `:594-648`; the read side is `EntityDeserializer.Deserialize` at `EntityDeserializer.cs:165-203`).
Everything but the first buffer is ZStd-compressed: `SerializerSystem` passes `BufferFormat.CompressedZStd` (`src/Game/Game.Serialization/SerializerSystem.cs:102`), and `ReadSystem` decompresses each buffer as it is pulled (`src/Game/Game.Serialization/ReadSystem.cs:56-79`).

A mod participates by implementing one of a small set of interfaces on a component, a buffer element, or a system.
Nothing is registered, and there is no opt-in list: the serializer library reflects over every type the `TypeManager` knows and builds a serializer for each one that implements a serialization interface (`ComponentSerializerLibrary.cs:26-102`).

Rots: nearly every type name in this file. The stable spine is `Colossal.Serialization.Entities` and `Game.Serialization`; re-read `ComponentSerializerLibrary.cs`, `SerializerSystem.cs` and `src/Game/Game/Version.cs` first on a new version.

### The five component-level mechanisms, and what chooses between them

`ComponentSerializerLibrary.Initialize` walks `TypeManager.GetTypeCount()` and picks a serializer per type from two interfaces and two type traits (`ComponentSerializerLibrary.cs:45-99`).
The decision tree, in the order the code tests it:

1. **`IEmptySerializable`** (`IEmptySerializable.cs` — a marker with no members). Not enableable, or enableable but also `ISerializeAsEnabled` → `EmptyComponentSerializer` (`:57-63`). **Writes zero bytes of payload** (`EmptyComponentSerializer.cs:64-68` schedules no job at all, only accounting overhead). Presence is carried entirely by the archetype's serializer-index list.
2. **`IEmptySerializable` + `IEnableableComponent`** → `EnableableEmptyComponentSerializer<T>` for a component, `EnableableEmptyBufferElementSerializer<T>` for a buffer (`:64-71`). Cost: **one bit per entity**, packed eight to a byte (`EnableableEmptyComponentSerializer.cs:29-54`).
3. **`ISerializable` on an `IComponentData`** → `ComponentDataSerializer<T>`, or `EnableableComponentDataSerializer<T>` when the type is enableable and not `ISerializeAsEnabled` (`:80-83`).
4. **`ISerializable` on an `IBufferElementData`** → `BufferElementDataSerializer<T>` / `EnableableBufferElementDataSerializer<T>` (`:84-87`).
5. **`ISerializable` on an `ISharedComponentData`** → `SharedComponentDataSerializer<T>` (`:88-91`).

Two modifiers ride on top:

- **`ISerializeAsEnabled`** (`ISerializeAsEnabled.cs`) is a marker meaning _do not persist the enabled bit_: the type is serialized as if it were not enableable, and comes back enabled. The game uses it twice, on `PrefabData` and `NotificationIconDisplayData` (`src/Game/Game.Prefabs/PrefabData.cs:7`, `src/Game/Game.Prefabs/NotificationIconDisplayData.cs:7`). No mod in the corpus uses it.
- **`IStrideSerializable`** (`IStrideSerializable.cs`) adds `int GetStride(Context)`. A non-zero stride declares the fixed byte size of one element, which switches the writer into a **column-filtered** mode: the payload is written through `NativeCompression` byte-plane filtering before compression (`ComponentDataSerializer.cs:34-56`, `BinaryWriter` / `BinaryReader.Read(NativeArray<byte>, int stride)` at `BinaryReader.cs:100-104`). It buys compression ratio. Returning 0 disables it. **The stride is also read on load**: `ComponentDataSerializer.Update` calls `GetStride(context)` with the load context (`:218`) and the deserialize job passes it to `reader.Read(nativeArray, m_Stride)` (`:164`), which de-interleaves the byte planes before any element is read (`BinaryReader.cs:105`). A stride that no longer matches what wrote the save yields garbage silently: the only guard is the byte-count check at `:171`, which a wrong-but-same-length stride passes. Verdict: corrected at the review gate of 2026-08-04; the original claim generalised from the write path.

`ComponentSerializerType` is the flag set that travels in the save beside each type name: `Empty | ComponentData | BufferElementData | SharedComponentData | Enableable | Filtered` (`ComponentSerializerType.cs:5-13`).

**A component that implements none of these is not saved.** It is silently dropped: the serializer only visits archetype members for which `TryGetSerializerIndex` succeeds (`EntitySerializer.cs`, the `SerializeArchetypeJob` fan-out over `archetype.GetComponentTypes()`), so a mod component with no interface simply does not exist after a reload.

Rots: the interface names and the `ComponentSerializerType` flag values.

### The cheapest durable per-entity flag is an empty tag, and it costs nothing

```csharp
public struct LevelLocked : IComponentData, IQueryTypeParameter, IEmptySerializable { }
```

That is the whole declaration (`PlopTheGrowables/Code/Systems/LevelLocked.cs:15`; the same shape at `PlopTheGrowables/Code/Systems/PloppedBuilding.cs:15`, `Anarchy/Anarchy/Components/PreventOverride.cs:13`, `Anarchy/Anarchy/Components/DoNotForceUpdate.cs`, `BetterBulldozer/BetterBulldozer/Components/PermanentlyRemovedSubElementPrefab.cs`, `CS2-Platter/Platter/Components/Initialized.cs`).
No `Serialize`, no `Deserialize`, no version to maintain, and no bytes in the save beyond the type-table entry shared by every entity that carries it.
It is the correct answer whenever the durable state is one bit and the bit is _presence_.

Make it `IEnableableComponent` as well and the bit becomes a persisted true/false at one bit per entity, which is the right shape when the archetype should not change as the flag flips.

The catalog already certifies this for Plop the Growables ("Empty tag components using the engine's own serialization"), which is accurate.

### The read and write contract, and where it is enforced

`ISerializable` is two symmetric generic methods (`ISerializable.cs`):

```csharp
void Serialize<TWriter>(TWriter writer) where TWriter : IWriter;
void Deserialize<TReader>(TReader reader) where TReader : IReader;
```

`IWriter`/`IReader` are wide but closed: primitives, the `Unity.Mathematics` vector types, `quaternion`, `Color`/`Color32`, `Hash128`, `Bezier4x3`, `string`, `Entity`, `NativeArray`/`NativeList` bulk overloads, and a generic `Write<T>(T) where T : ISerializable` that recurses into a nested serializable type (`IWriter.cs:9-104`, `IReader.cs:9-106`). `IReader` additionally has `Skip(int)` (`IReader.cs:105`).

**The two sides are not symmetric on nested serializables, and neither has an enum overload.**
`IWriter` carries one: `Write<TSerializable>(TSerializable) where TSerializable : ISerializable`, with no `struct` constraint (`IWriter.cs:47`), so a reference type rides through it.
`IReader` splits into `Read<TSerializable>(out TSerializable) where TSerializable : struct, ISerializable` and `Read<TSerializable>(TSerializable) where TSerializable : class, ISerializable` (`IReader.cs:45/47`).
The reference-type overload takes an **already-allocated instance** and simply calls `Deserialize` on it (`BinaryReader.cs:165-168`), so a mod persisting a class graph has to construct every node before reading it, and nothing on either side writes or checks a null marker.
`CS2-WriteEverywhere` is the corpus's only user of that overload, through a `WriteNullCheck`/`ReadNullCheck` pair of its own whose read half restores the `out` shape and therefore has to do the allocation and the null handling itself (called at `CS2-WriteEverywhere/BelzontWE/Templates/WETemplateManager.cs:80/165`; defined in the closed-source `Belzont.Serialization`, which is not in the corpus, so what it puts on the wire for a null is not readable here).
Neither side has an overload for an enum of any underlying type, so an enum field is written cast to its underlying type and read back into that type and cast home (`CS2-Platter/Platter/Components/Parcel.cs:66/76-77`).

Five contract rules the code enforces or exposes, each worth teaching:

- **Every component's data for one archetype is one length-prefixed block.** `writer.Begin()` / `writer.End(block, out size)` and `reader.Begin(out size)` / `reader.End(block)`; `Begin` reads a four-byte size and `End` compares the position against it, resyncing and returning false on a mismatch (`BinaryReader.cs:30-48`). The caller turns false into `ComponentSerializerException("Data size mismatch when deserializing component ...")` (`ComponentDataSerializer.cs:181-184`). **So writing and reading different byte counts is detected, but only at the end of the whole block** — after every entity of that archetype has been read past the end of its own record.
- **Read and write must be exactly symmetric per entity.** There is no per-entity framing. `Read(out uint)` indexes the buffer directly and advances (`BinaryReader.cs:341-349`), and in a Burst release build a read past your own record silently returns the next entity's bytes.
- **A buffer writes its length first, then its elements.** `writer.Write(buffer.Length); writer.Write(buffer.AsNativeArray());` per entity, and the deserializer resizes the `DynamicBuffer` to the stored length before reading elements (`BufferElementDataSerializer.cs:79-92`). The mod's `Serialize` is called per element, never per buffer.
- **A component whose `Serialize` writes nothing throws.** `"Nothing serialized for component {0}. Use IEmptySerializable instead"` (`ComponentDataSerializer.cs:72-75`, and for buffers only when some buffer was non-empty, `BufferElementDataSerializer.cs:97-100`).
- **`IStrideSerializable` and a version-branching `Deserialize` interact.** The write-side guard differs by kind and is weaker than it looks on a component: `ComponentDataSerializer.cs:51` tests `num != 0 && buffer.Length % (num * m_Stride) != 0`, a **divisibility** test, so a stride declared at an exact fraction of the true element size passes silently; `BufferElementDataSerializer.cs:71` uses exact equality (`num2 * m_Stride != buffer.Length`). Verdict: corrected at the review gate of 2026-08-04. The game's own answer is a version-aware stride: `GroundPollution.GetStride` returns 4 for saves older than `Version.removeGroundPollutionDelta` and 2 after (`src/Game/Game.Simulation/GroundPollution.cs:30-37`), matching a `Deserialize` that skips a retired field on old saves (`:20-28`). A mod using both must do the same or return 0.

**Entity references are remapped, and a reference to an unsaved entity becomes `Entity.Null`.** `Write(Entity)` looks the entity up in the writer's entity table and writes its table index, or `-1` if it is absent or the version does not match; `Read(out Entity)` maps the index back or yields `Entity.Null` (`BinaryWriter.cs:128-141`, `BinaryReader.cs:146-157`). There is no dangling-reference failure mode and no way to persist a reference to an entity the save did not include.

### A correct `ISerializable` component with nothing else in it

The corpus declares 47 types combining `ISerializable` with `IComponentData` or `IBufferElementData`, across 11 of the 22 repositories (swept 2026-08-04 for both orderings of the two interfaces on one declaration line). `CS2-Platter`'s `Parcel` is five fields and two short methods with nothing else in them (`CS2-Platter/Platter/Components/Parcel.cs:33/60-78`), which is the baseline shape the rest add to:

```csharp
public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
{
    writer.Write(m_PreZoneType);
    writer.Write(m_RoadEdge);
    writer.Write(m_CurvePosition);
    writer.Write(m_Building);
    writer.Write((byte)m_State);
}
```

Two details in it are the surface rules above made concrete.
`m_PreZoneType` is `Game.Zones.ZoneType`, which is itself `ISerializable` (`src/Game/Game.Zones/ZoneType.cs:6`), so it binds the nested-serializable overload and needs no unpacking; that it is also `IStrideSerializable` contributes nothing here, because a nested type is reached only through `Serialize`/`Deserialize` and the stride is asked of the component type alone (`ComponentDataSerializer.cs:218`).
`m_State` is a `[Flags] byte` enum (`Parcel.cs:19-28`) and takes the explicit cast in both directions.

What it lacks is a version int, which is what the version-int finding below is about: a sixth field on `Parcel` breaks every save that carries a parcel.
It is not alone in lacking one. Twelve of the 47 contain no occurrence of the string "version" anywhere in the file — six of `CS2-Platter`'s seven, `Anarchy/Anarchy/Components/TransformRecord.cs`, both of `ExtraDetailingTools`', `Recolor/Recolor/Domain/CustomMeshColor.cs` and `Palette/AssignedPalette.cs`, and `RoadBuilder-CSII/RoadBuilder/Domain/Components/NetworkConfigComponent.cs`, whose versioning sits on its json instead.
Unconfirmed: that each of those twelve genuinely writes no version, rather than writing a bare literal under another name. Settling it needs each `Serialize` read; the string sweep is what stands behind the count.

### What decides whether an entity is in the save at all

`SerializerSystem.CreateQuery` builds one query (`SerializerSystem.cs:171-210`):

- **`Any`** — a fixed list of eighteen vanilla anchor types (`PrefabRef`, `LoadedIndex`, `ElectricityFlowNode`, `WaterPipeNode`, `ServiceRequest`, `Game.City.City`, `CityStatistic`, `TimeData`, `MeshColorPalette` and the rest, `:173-193`) **plus every serializable component type declared outside the `Game` assembly** (`:194-197`). That second half is the load-bearing one for mods: `ComponentSerializerLibrary.Initialize` collects exactly the types whose assembly differs from the serializer system's own (`ComponentSerializerLibrary.cs:53-55/76-78`, out-parameter declared at `:25`) and hands them back as `serializableComponents`.
- **`None`** — `NetCompositionData`, `EffectInstance`, `LivePath`, `Temp`, `Deleted` (`:201-208`).

Two consequences a mod author needs:

1. **A mod component is enough to get its entity saved**, even on an entity that carries no vanilla anchor.
2. **`Temp` and `Deleted` entities are never saved**, because both are already on their way out. `Deleted` is the destroy request, not an exclusion flag: `PrepareCleanUpSystem` queries `Any = { Deleted, Event }` with no further filter (`src/Game/Game.Common/PrepareCleanUpSystem.cs:21-28`) and `CleanUpSystem.OnUpdate` calls `EntityManager.DestroyEntity` on every match (`CleanUpSystem.cs:52`), both every frame (`SystemOrder.cs:50/54`). Verdict: corrected at the review gate of 2026-08-04 - the original sentence would have told a mod to destroy an entity it meant to keep.

**A mod's types only reach the library because the mod manager forces a rebuild.** `ModManager.AfterLoadAssembly` calls `TypeManager.InitializeAdditionalTypes(assembly)` and then `SerializerSystem.SetDirty()` (`src/Game/Game.Modding/ModManager.cs:147-150`), and `SerializerSystem.OnUpdate` rebuilds both libraries whenever they are dirty, re-deriving the query as it goes (`SerializerSystem.cs:76-92`). No mod code is involved.

Live 1.6.0f1, with no mods loaded: 745 component serializers and 72 system serializers in the library.

### Whole-system state: `IDefaultSerializable` on a system

The second mechanism is a save section owned by a system rather than an entity.
`SystemSerializerLibrary.Initialize` walks `world.Systems` and wraps each one implementing `IJobSerializable`, `IDefaultSerializable` or bare `ISerializable`, in that priority (`SystemSerializerLibrary.cs:32-57`).

- `IDefaultSerializable : ISerializable` adds `void SetDefaults(Context)` (`IDefaultSerializable.cs`). **`SetDefaults` is called for every registered system serializer whose type was absent from the save** (`EntityDeserializer.cs:421-429/614-618`) — which is exactly the case of a save written before the mod existed.
- A system implementing plain `ISerializable` still works but logs an error at library build time: `"<Name> implements ISerializable. All systems should use IDefaultSerializable/IJobSerializable instead!"` (`SystemSerializerLibrary.cs:55`). Implement `IDefaultSerializable`.
- `IJobSerializable` (`IJobSerializable.cs`) is the job-scheduling variant, for state large enough to want off-thread work.

**Registration into an update phase is irrelevant here; existing when the library is built is not.** `SystemSerializerLibrary.Initialize` enumerates `world.Systems` once (`:32`) and ends `isDirty = false` (`:63`); the rebuild is gated on `isDirty` in `SerializerSystem.OnUpdate` (`SerializerSystem.cs:89-92`), and the sole vanilla trigger is `ModManager.AfterLoadAssembly` (`ModManager.cs:149`), fired before `IMod.OnLoad` (`:123`). So a system created during `OnLoad` gets its save section with or without a phase, while one created lazily later gets none — neither `Serialize` nor `SetDefaults` is ever called on it — unless the mod calls the public `SerializerSystem.SetDirty()` or `SystemSerializerLibrary.SetDirty()` (`SerializerSystem.cs:43/66`) itself. Verdict: scoped at the review gate of 2026-08-04.

Two exemplars:
`Traffic/Code/Systems/Serialization/TrafficDataMigrationSystem.cs:406-421` writes one int (its own schema version), reads it back into a field, and `SetDefaults` sets that field to 0.
`CS2-WriteEverywhere/BelzontWE/Mesh/WECustomMeshLibrary.cs:22/290-345` writes an entire mesh library — ids, vertices, normals, UVs and triangles — into the save through the same interface, behind a `CURRENT_VERSION` constant.

### Writing a version int first: what it buys, and what happens when a mod skips it

The game does not need a per-component version int, because every reader already has one: `reader.context.version` is the `Colossal.Version` of the build that wrote the save (`Context.cs:14`, populated from the metadata buffer at `EntityDeserializer.cs:253-257`).
The game's own idiom is therefore a comparison against a named constant:

```csharp
public void Deserialize<TReader>(TReader reader) where TReader : IReader
{
    reader.Read(out m_Position);
    if (reader.context.version >= Version.laneElevation)
    {
        reader.Read(out m_Elevation);
    }
}
```

(`src/Game/Game.Areas/Node.cs:23-32`; the same pattern at `GroundPollution.cs:20-28`, `src/Game/Game.Prefabs/PrefabSystem.cs:868-874`, `src/Game/Game.City/CityConfigurationSystem.cs:413-421`.)

A mod cannot use that: the game's version tells it nothing about which revision of _its own_ component wrote the bytes, and a save can be written by any pairing of game build and mod build.
So a mod writes its own int first and branches on it. `NodeController/NodeController/Domain/Components/NC_NodeData.cs:12/25/35` writes `KVersion = 1` and reads it back into a discard; `Time2Work/NightShift/Components/CitizenSchedule.cs:13/45/61` carries the version as a real field.

**The exemplar to copy is `Traffic`'s, and it is a buffer element rather than a component.**
`ModifiedLaneConnections` writes a named constant first and reads it into a local it then branches on, supplying defaults for the two fields the old layout never held (`Traffic/Code/Components/LaneConnections/ModifiedLaneConnections.cs:40-72`):

```csharp
reader.Read(out int v);
if (v < DataMigrationVersion.LaneConnectionDataUpgradeV1)
{
    // DO NOT CHANGE ORDER
    reader.Read(out laneIndex);
    reader.Read(out edgeEntity);
    reader.Read(out modifiedConnections);
    carriagewayAndGroup = TrafficDataMigrationSystem.InvalidCarriagewayAndGroup;
    lanePosition = float3.zero;
}
else { /* the five current fields, in write order */ }
```

Three things make it the better teaching case than the two above.
The version goes into a named local, so the branch can use it — a discard read proves nothing was gained by writing the int at all.
The constants live in one central registry, each carrying a comment naming the game or mod version that broke the format: `LaneConnectionDataUpgradeV1 = 2`, `LaneConnectionDataUpgradeV2 = 3`, `PriorityManagementDataV1 = 4` (`Traffic/Code/DataMigrationVersion.cs:5-10`). Their values do not match the ordinals in their names, so the registry has to be read rather than guessed at.
And the missing fields are filled with an explicit sentinel rather than left at `default`, which is what lets the repair downstream find them: `FindIncompleteV1DataJob` takes `InvalidCarriagewayAndGroup` as a job field and flags any element equal to it (`Traffic/Code/Systems/Serialization/TrafficDataMigrationSystem.cs:30/275`, `TrafficDataMigrationSystem.FindIncompleteV1DataJob.cs:34/54`).

Being a buffer element rather than a component also costs what a mod author should know it costs: the version int is written **per element**, not once per entity or per block, since `Serialize` is called per element (`BufferElementDataSerializer.cs:79-92`).

**One registry, two consumers, and they are at different numbers.** `Serialize` still writes `LaneConnectionDataUpgradeV1` (2) even though the registry has reached 4 (`:43`), as does the mod's other serializable component, `Traffic/Code/Components/LaneConnections/GeneratedConnection.cs:34/48`. The two later constants belong to the system section instead: `TrafficDataMigrationSystem.Serialize` writes `PriorityManagementDataV1` (4) as its own version (`Traffic/Code/Systems/Serialization/TrafficDataMigrationSystem.cs:408`) and `OnUpdate` branches `_version < LaneConnectionDataUpgradeV1` then `_version < LaneConnectionDataUpgradeV2` (`:57/62`).
That is the division worth teaching: a component's version int tracks that component's byte layout and nothing else, while the repairs a format change implies are versioned once, in a system's section, against the same shared registry.

**For more than one format change, the two-way branch does not scale, and `CS2-WriteEverywhere` shows the shape that does.**
`WETextDataXml.Deserialize` reads the version into a local, bails with a warning when it exceeds `CURRENT_VERSION`, reads the fields common to every revision, then appends one `if (version >= N)` block per revision that added a field (`CS2-WriteEverywhere/BelzontWE/IO/WETextDataXml.cs:40/77-92/94-121`, the four appended blocks at `:110/114/118/122`).
Its `WETemplateManager` system section does the same at `WETemplateManager.cs:34/66-73`, appended blocks at `:114/121/126`.
Each new field costs one appended block and no edit to any existing one, and the `version > CURRENT_VERSION` bail is the mod's own answer to a save written by a newer build of itself — the game's equivalent is the unknown-format-tag path, which a mod cannot reach.

**What a version int buys is the ability to add a field later.** Without it, the only way to change a component's layout is to break every existing save.

**What happens when a mod skips it and then adds a field** is worse than a failed load, and this is the part that has to ship:

1. The old save's block is shorter than what the new `Deserialize` reads. There is no per-entity bound check, so entity 0 consumes entity 1's bytes, and so on down the archetype — every value is garbage, not merely absent.
2. At the end of the block, `reader.End(block)` returns false and the job throws `ComponentSerializerException` (`ComponentDataSerializer.cs:181-184`).
3. The throw is raised inside `DeserializeComponentDataJob<TReader>` (`ComponentDataSerializer.cs:186-189`), a `[BurstCompile] IJob` scheduled off the main thread (`:252-260`), so `UpdateSystem`'s per-system try/catch (`src/Game/Game/UpdateSystem.cs:184-197`) is not what handles it — that catch wraps a system's own `Update()`. Verdict: corrected at the review gate of 2026-08-04; the original chain was inferred from the diagnostic rather than derived.

So the load does not abort, and it does not stop early either. Verdict, established at the review gate of 2026-08-04: `EntityDeserializer.Deserialize` schedules every archetype's component job in a loop that completes before `m_ComponentDeps.Complete()` (`:176-183`), and all entities were created at `:175`, so every buffer is pulled and every other component block still reads correctly — `End` resyncs to the block end before throwing (`BinaryReader.cs:48-49`).
The real outcome is quieter than the one this file first recorded: the city loads fully populated and the damage is confined to the offending component's values being garbage on that archetype's entities. Only the system sections, deserialized after that `Complete`, could be skipped at all.
What remains open is whether the job's exception reaches the log, and under which message; the game was not running when the gate re-derived this, so it stands as `Unconfirmed:` and the reference carries an `UNVERIFIED:` marker for it.

Note the asymmetry: a **system** section is deserialized on the main thread inside a per-section catch, so one bad system section is logged and the others still load (`EntityDeserializer.cs:575-578/597-604`). A component's throw is raised inside a scheduled job and does not travel that path at all.

`Time2Work` illustrates a false comfort worth naming: `CitizenSchedule.Deserialize` wraps its reads in `try { ... } catch { version = 2; work_type = 0; }` (`Time2Work/NightShift/Components/CitizenSchedule.cs:57-79`). The catch cannot fire on a size mismatch, because the mismatch is detected by the _caller_ after `Deserialize` has returned.

### The game's own version constants, and what a save-format break looks like

`src/Game/Game/Version.cs` is 826 lines of nothing but named build stamps: **273 `[VersionConstant]` fields**, from `MS9_0` at 0.9.0a1 through `garbageFeeReset` at 1.5.7f1, ending with `current` (`Version.cs:825`).
Each is a `Colossal.Version`, and comparison is a single packed `long` ordered major, minor, build, release type, incremental, build date, build time (`src/Colossal.Core/Colossal/Version.cs:268-271/336-354`, layout at `:383+`), so `>=` is a chronological test. On the wire a version is 13 bytes (`:356-364`).

Live 1.6.0f1 reports `current` as `1.6.0f1 (419.d6c6) [6216.19404]`.

**Beside the version constants there is a second, coarser mechanism the game actually prefers for migrations: format tags.**
`Game.FormatTags` is a flat enum of 42 members in 1.6.0f1 (`src/Game/Game/FormatTags.cs`, count confirmed live), each naming a format change: `ShortLaneOptimization`, `HomelessAndWorkerFix`, `CargoPortCleanup`, `HouseholdPetLimit`, `TripPriority` and so on.
On save, **every name in the writing build's enum is written as a string** (`EntitySerializer.cs:662-671`).
On load, each name is looked up in the loading build's enum and the matching bit set in `context.format` (`EntityDeserializer.cs:265-285`).

That gives the two directions their shapes:

- **Older save, newer game** — the new build's tags are simply absent from the save, so `context.format.Has(tag)` is false and the migration systems gated on it run. Confirmed live: the loaded city was written by `1.3.5f1 (1335.8e75) [5962.14277]`, is loading under 1.6.0f1 with `purpose = LoadGame`, and `context.format.Has<FormatTags>(FormatTags.TripPriority)` is `false`.
- **Newer save, older game** — the save carries a tag name the old build's enum does not have. The loader logs `"Unknown format tag: {0}"`, sets `m_UnsupportedFormat`, stops requesting buffers, and `Deserialize` returns false (`EntityDeserializer.cs:274-284/202`). `SerializerSystem` then rewrites the context purpose `LoadGame → NewGame` and `LoadMap → NewMap` with a fresh `Version.current` context (`SerializerSystem.cs:133-147`). **A save from a newer build does not error out; it comes up as a new game.** That is what a save-format break looks like from inside.

`context.format.Has` is generic over the tag enum — `Has<FormatTags>(FormatTags.X)` — and a non-generic call does not compile or bind (confirmed live: `ContextFormat.Has/1 not found`).

**A mod cannot add a format tag.** `SerializerSystem` closes the generic over `Game.FormatTags` at both call sites (`SerializerSystem.cs:102/117`), so the tag table is the game's alone. A mod's equivalent is its own version int, on a component or on a system section.

One mod reads the game's tag table from inside a **system's** save section: `Time2Work/NightShift/Systems/Time2WorkDeathCheckSystem.cs:169-176` branches its own `Deserialize` on `reader.context.format.Has<FormatTags>(FormatTags.EasyModeDeathRateFix)`. That is legitimate — the tag says something true about the save — but it couples the mod's format to a vanilla one.

Rots: the 273 constant names, the 42 tag names, and the count of both. Re-read `src/Game/Game/Version.cs` and `src/Game/Game/FormatTags.cs`.

### The two phases, and what actually occupies them

`SystemUpdatePhase.Serialize` and `SystemUpdatePhase.Deserialize` (`src/Game/Game/SystemUpdatePhase.cs:26-27`) each fire exactly once, driven by `SaveGameSystem.OnUpdate` and `LoadGameSystem.OnUpdate` respectively (`SaveGameSystem.cs:137-155`, `LoadGameSystem.cs:50-57`). `mod-lifecycle-and-ordering.md` owns their position in the tree and their once-per-load firing; this file owns their contents.

**Serialize**, seven registrations (`src/Game/Game.Common/SystemOrder.cs:730-736`):
`UpdateBefore` band — `TrimPathsSystem`, `PreSerialize<ClimateSystem>`, `PreSerialize<AudioManager>`.
`UpdateAt` — `BeginPrefabSerializationSystem`, `SerializerSystem`, `EndPrefabSerializationSystem`, `WriteSystem`.
Vanilla registers nothing `UpdateAfter`.

**Deserialize**, three bands used deliberately (`SystemOrder.cs:737-897`):

- Front band (`UpdateBefore`): `AllowBarrier<DeserializationBarrier>` (`:737`), then 57 `PreDeserialize<T>` wrappers, then `ClearSystem` and `GameModeSystem` (`:795-796`).
- Middle band (`UpdateAt`, `:798-852`): `SerializerSystem`, `ReadSystem`, then the rebuilders — `FilterLoadedSystem`, `ResolvePrefabsSystem`, `RequiredComponentSystem`, and some thirty systems that reconstruct derived state that is deliberately not saved (`SubLaneSystem`, `SubObjectSystem`, `HouseholdCitizenSystem`, `OwnedVehicleSystem`, `ConnectedRouteSystem`, `ElectricityGraphSystem` …), with the shipped migration systems last (`:842-852`).
- Back band (`UpdateAfter`, `:853-897`): the resets, then 32 `PostDeserialize<T>` wrappers.

Counted as `UpdateBefore<PreDeserialize<` and `UpdateAfter<PostDeserialize<` occurrences in `SystemOrder.cs`; outside their own declaration files, the two wrappers are named nowhere else in `src/Game`.

**`ClearSystem` is why loading a second save in one session works, and it is a trap for mods.**
The world is created once at boot and destroyed only at shutdown (`src/Game/Game.SceneFlow/GameManager.cs:591/755`), so every load runs against the world the previous city left behind. `ClearSystem.OnUpdate` is a single `EntityManager.DestroyEntity(m_ClearQuery)` (`src/Game/Game.Serialization/ClearSystem.cs:55-58`) over a query whose `Any` list is **nineteen fixed vanilla types and nothing else** (`:22-51`) — it does not gain the mod types the save query gains.
So an entity carrying only mod components and no vanilla anchor is written to the save, is not destroyed before the next load, and is created again from the save: it accumulates across loads within one session.
`RoadBuilder` hits exactly this shape and mitigates it, deleting its own marker entities the frame after they are written (below).

Verdict, settled live on 2026-08-04 with `PlopTheGrowables` loaded (the pass that opened this question had no mod declaring a serializable component, which is why it could not close it):
the component serializer library went from 745 entries to 748, one per component the mod declares, with no registration of any kind;
an entity created carrying only `PlopTheGrowables.LevelLocked` (plus Unity's automatic `Simulate`) was matched by `SerializerSystem.m_Query` (26 query types) and **not** matched by `ClearSystem.m_ClearQuery` (21 query types);
and that entity survived a load of an unrelated city — written by `1.2.3f1`, 168,267 entities against the previous city's 384,844 — with its index, version and both components intact.
The asymmetry and the survival are therefore observed rather than derived; the recreation-from-save half follows from the save query matching.

### The twelve migration systems the game ships, and the shape they share

`src/Game/Game.Serialization.DataMigration/` holds twelve systems. Eleven are registered `UpdateAt` in `Deserialize` (`SystemOrder.cs:842-852`); `ResidentPseudoRandomSystem` alone sits in the back band (`:859`):
`BicyclePathfindFixSystem`, `CargoPortCleanupSystem`, `CompanyAndCargoFixSystem`, `HomelessAndWorkerFixSystem`, `HouseholdPetLimitSystem`, `LaneDirectionNetObjectSystem`, `PlaceholderCleanupSystem`, `QuantityObjectMissingSystem`, `ResidentPseudoRandomSystem`, `ShortLaneRemoveSystem`, `TradeCostFixSystem`, `UpdateCitizenFlagsFromHouseholdsSystem`.

The shape is uniform, and eight of the twelve gate on a format tag while four compare `context.version` against a named constant — `LaneDirectionNetObjectSystem.cs:95` (`Version.laneDirectionNetObject`), `PlaceholderCleanupSystem.cs:105`, `QuantityObjectMissingSystem.cs:95`, `ResidentPseudoRandomSystem.cs:88` — and those four repair through Burst jobs writing to a command buffer (`LaneDirectionNetObjectSystem.cs:101`) rather than main-thread `EntityManager` calls. Verdict: the uniform-shape claim was corrected at the review gate of 2026-08-04, having been generalised from one system. `HouseholdPetLimitSystem` is the whole of the tag-gated shape in 40 lines (`src/Game/Game.Serialization.DataMigration/HouseholdPetLimitSystem.cs:26-59`):

```csharp
if (m_LoadGameSystem.context.format.Has(FormatTags.HouseholdPetLimit) || m_HouseholdQuery.IsEmptyIgnoreFilter)
{
    return;
}
// trim every HouseholdAnimal buffer to 2, Deleted-tag the surplus pets,
// remove the buffer from households left with none
```

Gate on the format tag, bail on an empty query, then repair with plain main-thread `EntityManager` calls.

**There is a thirteenth pattern that is not version-gated at all, and it is the one a mod should usually copy.**
`RequiredComponentSystem` (1,840 lines, `src/Game/Game.Serialization/RequiredComponentSystem.cs`) is a long list of "has X, lacks Y → add Y" queries built in `OnCreate` (`:329-749`) and applied unconditionally in `OnUpdate` (`:753-…`), each behind an `IsEmptyIgnoreFilter` check:

```csharp
if (!m_BuildingEfficiencyQuery.IsEmptyIgnoreFilter)
{
    base.EntityManager.AddComponent<Efficiency>(m_BuildingEfficiencyQuery);
}
```

It needs no version and no tag, because the query itself is the test. This is how the game backfills a component added to an existing archetype, and it is precisely a mod's problem when its component must appear on entities from a save written before the mod existed.
`PlopTheGrowables` is the corpus's example of the same idea (catalogued as "a deserialize-phase system that backfills entities from saves written before the mod existed").

### Why a mod migration sometimes cannot run in the deserialize phase, and where it goes instead

The deserialize phase's middle band runs while the world is only half rebuilt: net compositions, lane geometry and most derived state are produced _after_ it, in the modification phases of the first simulation frames.
A migration that has to read that derived state therefore cannot sit in the phase built for migrations.

`Traffic` states this in a comment beside the registration and moves its migration to `Modification4`, anchored before its own sync system (`Traffic/Code/Mod.cs:81-82`):

> `/*data migration - requires NetCompositions to work correctly - not possible to run in SystemUpdatePhase.Deserialize */`
> `updateSystem.UpdateBefore<TrafficDataMigrationSystem, SyncCustomLaneConnectionsSystem>(SystemUpdatePhase.Modification4);`

The split that makes this work is worth teaching as a unit, because it is three parts:

1. The system is `IDefaultSerializable`, so it gets a save section wherever it is registered. `Deserialize` reads one int into `_version`; `SetDefaults` sets `_version = 0` for saves that predate the mod (`Traffic/Code/Systems/Serialization/TrafficDataMigrationSystem.cs:406-421`).
   **`SetDefaults` also runs on a new game**, since `GameManager.Load` permits an invalid descriptor for `Purpose.NewGame` and still runs the phase (`GameManager.cs:1046/1098`), leaving every serializer unmarked so `DeserializeSystems` calls `SetDefaults` on all of them (`EntityDeserializer.cs:614-617`). So version zero means _a save predating the mod_ only once `context.purpose` has been checked, and a migration without that check repairs a freshly generated map. Established at the review gate of 2026-08-04.
2. `OnGameLoaded(Context)` sets a `_loaded` flag (`:400-404`). **`GameSystemBase.OnCreate` subscribes the system to that callback** — `Delegate.Combine` onto `LoadGameSystem.onOnSaveGameLoaded` (`src/Game/Game/GameSystemBase.cs:21-25`), inside `if (base.World == World.DefaultGameObjectInjectionWorld)` and reached only when `base.OnCreate()` runs, so a subclass overriding `OnCreate` without chaining never subscribes and its migration silently never fires (`GameLoaded` is private, `OnGameLoaded` an empty virtual at `:119-121`), which fires after the entire Deserialize phase has run (`LoadGameSystem.cs:53-55`). No phase registration is needed to observe a load, and the callback carries the `Context`, so the version and the format tags are in hand.
3. `OnUpdate` runs on the mod's chosen phase, sees `_loaded`, branches on `_version` against the named constants, runs the repair jobs and clears the flag (`:41-60`).

The same file also shows what a mod owes when it renames the type that owns a save section: `[FormerlySerializedAs("Traffic.Systems.DataMigration.TrafficDataMigrationSystem")]` on the class (`:26`).

### Reading another mod's persisted components, and doing it without a save section

The corpus has one cross-mod migration, and it needs none of the machinery above.
`Traffic`'s `TLEDataMigrationSystem` imports the lane directions a different mod persisted, and it never implements a serialization interface at all: the foreign mod's own component is already in the world by the time the system runs, because its assembly was loaded and the serializer library reflected over its types like any other (`Traffic/Code/Systems/ModCompatibility/TLEDataMigrationSystem.cs:25`).

The four parts:

1. **Resolve the foreign type by name out of the asset database**, since a mod cannot reference another mod's assembly. `OnCreate` fetches the `ExecutableAsset` whose name matches, pulls the type off `asset.assembly`, and turns it into a `ComponentType` through `TypeManager.GetTypeIndex` (`:34-44`). Missing type, or any exception, sets `Enabled = false` and logs (`:36-41/52-57`).
2. **Query on that runtime `ComponentType`**, built with an `EntityQueryDesc` array rather than a generic `GetEntityQuery<T>` — the only form available when the type is not known at compile time (`:45-50`).
3. **Run in the Deserialize back band.** The registration is `UpdateAfter<TLEDataMigrationSystem>(SystemUpdatePhase.Deserialize)`, and it is issued lazily from a `MainThreadDispatcher` updater rather than from `OnLoad`, once the other mod has been detected as enabled (`Traffic/Code/Mod.cs:111-112/137/167`). Lazily is safe here precisely because the system owns no save section: the finding above says a system created after the library was built silently gets none, and this one wants none.
4. **Remove the foreign component after migrating**, with the comment `// delete TLE data components to prevent data corruption` (`TLEDataMigrationSystem.cs:89-91`), and disable the other mod's system that would otherwise act on it (`Mod.cs:166`).

The migration itself is an `IJobChunk` writing through a command buffer (`TLEDataMigrationSystem.cs:65-83`, job at `TLEDataMigrationSystem.MigrateCustomLaneDirectionsJob.cs:26-40`), and it ends by putting a dialog in front of the player naming how many intersections it converted (`:94-98`).

Note what this makes possible in the other direction: **a foreign mod's component is readable, and removable, by anyone who can name its type.** The uninstall path below is not the only way a mod's persisted data disappears.

Rots: the asset name `C2VM.CommonLibraries.LaneSystem` and the type name it reaches for; that mod is not in the 22-repository checkout, so only `Traffic`'s side of this is verifiable here.

### Type identity in the save, and what a rename or an uninstall does

Both type tables store the **assembly-qualified name** of the type (`ComponentSerializer.SerializeType`, `:33-41`). On load, resolution is three-stage (`ComponentSerializer.DeserializeType`, `:43-74`):

1. `Type.GetType(storedName)`.
2. Failing that, the stored name is looked up in a **type table** built from `[FormerlySerializedAs]` attributes (`ComponentSerializerLibrary.cs:106-115`, `SystemSerializerLibrary.cs:71-79`, attribute at `FormerlySerializedAsAttribute.cs`), **progressively trimming from the last comma** — so `Ns.Type, Asm, Version=…, Culture=…, PublicKeyToken=…` is retried as `Ns.Type, Asm, Version=…`, then `Ns.Type, Asm`, then `Ns.Type`. An assembly version bump therefore never breaks a save on its own, and a `[FormerlySerializedAs]` may name the old type at whatever precision is convenient.
3. Failing both, or if the resolved type implements neither serialization interface, the loader logs `"Not serializable type: {0}"` and treats the entry as obsolete.

**An obsolete type is skipped, not fatal.** `ObsoleteComponentSerializer` reads the block's size prefix and jumps straight to its end (`ObsoleteComponentSerializer.cs:231-241`), and `CreateEntities` builds each archetype from only the types that resolved (`EntityDeserializer.cs:485-503`).
So **uninstalling a mod loses that mod's components and keeps the city.** The log carries `"Not serializable type: {0}"` per lost type (`ComponentSerializer.cs:71-73`), which is the stage-3 branch above: the assembly is gone, so `Type.GetType` and the attribute table both miss. Verdict: corrected at the review gate of 2026-08-04. `"Serializer not found ({0}): {1}"` (`EntityDeserializer.cs:350`) is a different case — the type resolved but `TryGetSerializerIndex` missed (`:326-328`).

There is one further guard: if the resolved type's serializer kind disagrees with the kind recorded in the save — a component that became a buffer, say — the loader logs `"Serializer type mismatch ({0}): {1} != {2}"` and falls back to the obsolete serializer, discarding the data rather than misreading it (`EntityDeserializer.cs:333-342`). The one tolerated crossing is buffer-in-code against component-in-save, which is read through the buffer serializer (`:335-339`).

**Prefabs are a separate identity space with the same story.** `PrefabSystem` writes an ordered list of `PrefabID` (type name + prefab name) and entities reference prefabs by index into it (`PrefabSystem.cs:807-864`; `LoadedIndex` at `src/Game/Game.Prefabs/LoadedIndex.cs`). An id that no longer resolves becomes an **obsolete prefab entity**: `ResolvePrefabsSystem` gives it a negative `PrefabData.m_Index`, disables `PrefabData`, and registers the missing id, logging `"Unknown prefab ID: {0}"` (`src/Game/Game.Serialization/ResolvePrefabsSystem.cs:525-537`, `PrefabSystem.cs:958-966`). `InitializeObsoleteSystem` then fills in placeholder data for anything with `ObjectData`, `NetData`, `AggregateNetData`, `NetLaneArchetypeData` or `AreaData` and a disabled `PrefabData` (`src/Game/Game.Serialization/InitializeObsoleteSystem.cs:307-320`).
So a mod that adds prefabs leaves standing placeholders in the city when it is removed, not a failed load.

Rots: `[FormerlySerializedAs]`'s namespace (`Colossal.Serialization.Entities`, distinct from Unity's own attribute of the same name), and the three log strings above, which `diagnostics` will want verbatim.

### Out-of-band files: what the game keeps beside the entity stream, and what tells a load it needs them

A saved city is **not one file**. `GameManager.Save` builds a transient asset database, fills it, and packages the whole of it as one `PackageAsset` under `Saves/<user path>` (`src/Game/Game.SceneFlow/GameManager.cs:940-1002`, path from `src/Game/Game.Assets/SaveGameMetadata.cs:14`). Inside it:

- **The entity stream**, a `SaveGameData` asset — extensions `.SaveGameData` and `.cds` (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/SaveGameData.cs:5-7`), written to `saveGameData.GetWriteStream()` (`GameManager.cs:955`).
- **A metadata record**, `.SaveGameMetadata`, holding a JSON `SaveInfo`: city name, population, money, XP, simulation date, game mode, options, `mapName`, `preview`, and three lists that matter here — `contentPrerequisites`, `modsEnabled`, `prefabReferences` (`src/Game/Game.Assets/SaveInfo.cs:11-69/71-115`).
- **A preview texture**, captured and compressed into the same package (`GameManager.cs:957-964`).
- **Prefab assets cloned into the package.** `PrefabSystem.SavePrefabAssets` copies the current climate prefab, the water render settings and the terrain render settings into the save database, and calls `context.RemapEntityGuid(prefabEntity, asset.id)` for each (`PrefabSystem.cs:378-437`, `SavePrefab` at `:363-376`). The remap is then read back when the prefab id list is written, so the stored `PrefabID` carries the packaged asset's guid instead of the runtime one (`PrefabSystem.cs:839-841`, `Context.RemapEntityGuid`/`TryGetRemapEntityGuid` at `Context.cs:38-54`). **That is the game's own mechanism for shipping an asset inside a save and having the load find it.**

What tells a load which out-of-band things it needs:

- **`contentPrerequisites`** — the DLC and content packs. Filled from `SaveGameSystem.referencedContent`, mapped to prefab names (`GameManager.cs:924-932`), and checked before load by `ArePrerequisitesMet` (`GameManager.cs:1831-1850`).
- **`modsEnabled`** — from `CityConfigurationSystem.usedMods`, a `HashSet<string>` that is **loaded from the save and then unioned with the currently enabled mods** (`src/Game/Game.City/CityConfigurationSystem.cs:147/255-262/412-421`), written into the entity stream behind `Version.saveGameUsedMods` and into the metadata JSON (`:326-337`, `src/Game/Game.UI.Menu/MenuUISystem.cs:881`). So it is cumulative: every mod the city has _ever_ been saved with, not the set it currently needs.
- **`prefabReferences`** — the packaged prefab assets, kept so a later save knows which ones it already carried (`WasPreviouslySaved`, `PrefabSystem.cs:399-424`).

**The corpus's two out-of-band shapes:**

1. **Files in the user data directory, keyed by nothing.** `Recolor` writes palette and subcategory prefabs and saved colour sets under `EnvPath.kUserDataPath/ModsData/<ModId>/…` (`Recolor/Recolor/Systems/Palettes/PalettesUISystem.Main.cs:257/265`, `Recolor/Recolor/Systems/SelectedInfoPanel/SIPColorFieldsSystem.Initialization.cs:358`); `CS2-WriteEverywhere` writes layouts and templates as XML (`CS2-WriteEverywhere/BelzontWE/Controllers/WELayoutController.cs:84/179`, `BelzontWE/Templates/WETemplateManager.ModulesIntegration.cs:276`). These are per-installation, not per-city, and nothing in the save points at them.
   `CS2-WriteEverywhere` does, though, avoid keeping two formats: **the same class is the XML DTO and the save payload.** `WETextDataXmlTree` and `WETextDataXml` carry `[XmlRoot]`/`[XmlElement]` attributes and an `ISerializable` implementation side by side (`CS2-WriteEverywhere/BelzontWE/IO/WETextDataXmlTree.cs:14-15`, `WETextDataXml.cs:38/43-53`), and the system section writes the very objects the XML export serialises, `WETextDataXmlTree.FromEntity(...)` feeding `writer.WriteNullCheck` at `WETemplateManager.cs:164-165/174-175` and `ToXML()` at `WETextDataXmlTree.cs:68`. That is what makes the reference-type reader overload above load-bearing for this mod.
2. **A manifest of marker entities written into the save.** `RoadBuilder` is the exemplar and it is worth teaching whole. `RoadBuilderSerializeSystem` is registered `UpdateBefore` in the `Serialize` phase (`RoadBuilder-CSII/RoadBuilder/Mod.cs:67`). Its `OnUpdate` creates one bare entity per placed custom road, carrying `NetworkConfigComponent { NetworkId }`, and writes each config to `<content folder>/<id>.json` (`RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderSerializeSystem.cs:48-72`, `RoadBuilder-CSII/RoadBuilder/Utilities/LocalSaveUtil.cs:15-23`). The component's `Serialize` delegates to a static that writes the config itself into the save stream, so the save is self-sufficient even if the json is gone (`RoadBuilder-CSII/RoadBuilder/Domain/Components/NetworkConfigComponent.cs:10-23`). `RoadBuilderConfigCleanupSystem`, registered `UpdateAt(MainLoop)`, `Deleted`-tags every such entity later in the **same** frame (`RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderConfigCleanupSystem.cs:18-26`, `Mod.cs:74`): `SaveGameSystem` is itself `UpdateAt(MainLoop)` (`SystemOrder.cs:69`) and drives the write inline (`SaveGameSystem.cs:82`), while a mod's `UpdateAt` sorts after every vanilla one in the phase, so the cleanup and the vanilla `Cleanup` destroy both land in the frame that wrote the markers. Verdict: corrected at the review gate of 2026-08-04 — which is both the tidy-up and the answer to the `ClearSystem` accumulation trap above.

Its version discipline sits on the json rather than in the component: `CURRENT_VERSION = 5` with named history constants (`RoadBuilderSerializeSystem.cs:26-32`).

### The third option: persist nothing, and collapse into vanilla fields instead

`Water_Features` writes no mod component into the save at all. A system registered `UpdateBefore` in `Serialize` folds the mod's custom water state back into the vanilla fields it borrows, and five systems registered `UpdateAfter` restore it once `WriteSystem` has run (`Water_Features/Water_Features/WaterFeaturesMod.cs:142-147`).
The result is a save that a player without the mod can load correctly, at the cost of the mod's own precision.
Its systems also branch on `context.purpose` — `Purpose.NewMap`/`NewGame` versus a load (`Water_Features/Water_Features/Systems/SeasonalStreamsSystem.cs:70/84`, `TidesAndWavesSystem.cs:68/258`).

`Purpose` has seven values: `SaveGame`, `NewGame`, `LoadGame`, `SaveMap`, `NewMap`, `LoadMap`, `Cleanup` (`Purpose.cs:5-13`). It reaches a mod through `Context.purpose` in every `SetDefaults`, `PreDeserialize`, `PostDeserialize` and `OnGameLoaded`, and through `OnGamePreload(Purpose, GameMode)`.
The game itself branches on it inside a serializer: `CityConfigurationSystem.Serialize` writes camera state differently for `SaveMap` and writes `usedMods` only for `SaveGame` (`CityConfigurationSystem.cs:277-337`).

## Bridge

**Technique siblings.**
`ecs-in-this-game` owns the component kinds this file's mechanisms attach to — `IComponentData`, `IBufferElementData`, `ISharedComponentData`, `IEnableableComponent` — and the archetype and chunk model that `CreateEntities` reconstructs on load (`EntityDeserializer.cs:469-528`). It already carries the `ISerializeAsEnabled`-adjacent enableable-bit story and the `UpdateFrame` shared component, whose serializer is the only `SharedComponentDataSerializer` the game exercises.
`mod-lifecycle-and-ordering` owns the phase tree, the once-per-load firing of `Serialize` and `Deserialize`, the `PreDeserialize<T>`/`PostDeserialize<T>`/`PreSerialize<T>` wrappers, and the fact that `UpdateSystem.Update` catches per-system exceptions — this file's failure-mode finding rests on that last one (`src/Game/Game/UpdateSystem.cs:182-195`).
`prefabs-and-assets` owns `PrefabID`, `PrefabData`, `PrefabSystem.AddPrefab` and the asset database; this file owns only what the save does with them — the id list, `LoadedIndex`, the obsolete-prefab path, and `SavePrefabAssets` packaging a prefab into the save.
`diagnostics` owns the log; take from here the six strings a save problem produces: `"System update error during Deserialize->SerializerSystem:"`, `"Serializer not found ({0}): {1}"`, `"Serializer type mismatch ({0}): {1} != {2}"`, `"Not serializable type: {0}"`, `"Unknown format tag: {0}"`, `"Unknown prefab ID: {0}"`, plus two informational lines: `"Serialized version: {0}"` always (`SerializerSystem.cs:118`), and `"Format tags: {0}"` only where `Deserialize` returned true (`:131`, inside `if (num)`), so it is absent on the unsupported-format path — its absence is the signature of a save from a newer build. Verdict: corrected at the review gate of 2026-08-04.
`settings-and-input` owns the `.coc` settings files, which are the other durable store and share none of this machinery.
`mod-compatibility` owns two-mod interaction; this file supplies the fact that a save records `usedMods` cumulatively, that a foreign mod's save section is skipped rather than fatal, and that a foreign mod's _components_ are readable and removable by any mod that can resolve their type by name — the cross-mod-migration finding above is the whole procedure, and the compatibility reference owns the question of when doing it is legitimate.

**Mechanics references whose components a mod would persist against**, named individually because the authoring agent cannot derive the list:

- `roads-and-traffic` — the densest case. `Traffic` persists against `Game.Net.Node` and `Edge`, `NodeController` against per-node geometry, `RoadBuilder` against `Edge`/`RoadBuilderNetwork`. `Game.Net.Node`, `Lane` and `Curve` are all `IStrideSerializable`, and `Game.Areas.Node` is the version-branching exemplar (`src/Game/Game.Areas/Node.cs:23-32`).
- `zoning-buildings-and-land-value` — `PlopTheGrowables`' `LevelLocked`, `PloppedBuilding` and `SpawnedBuilding` tags all ride on building entities.
- `citizens-and-households` — `Time2Work`'s `CitizenSchedule` is per-citizen persisted state, and three of the game's own migrations (`HouseholdPetLimitSystem`, `UpdateCitizenFlagsFromHouseholdsSystem`, `HomelessAndWorkerFixSystem`) repair household and citizen data on load.
- `environment-and-pollution` — the cell maps are the stride-serialized types (`GroundPollution`, `AirPollution`, `NoisePollution`, `GroundWater`, `SoilWater`, `NaturalResourceCell`), and `GroundPollution` carries the version-aware `GetStride`.
- `utilities-and-flow-networks` — `ElectricityFlowNode`, `ElectricityFlowEdge`, `WaterPipeNode`, `WaterPipeEdge` are four of the eighteen fixed anchors in the save query and four of the nineteen in `ClearSystem`, and on load `ElectricityGraphSystem` re-applies the electricity edges' capacities and directions from prefab data — reversing and negating a backwards-stored edge, falling back to a C# literal where the prefab lost the component — while `ConnectedFlowEdgeSystem` rebuilds only the adjacency buffers; the edges' own capacities and flows serialize. (Corrected 2026-08-11 by the utilities-and-flow-networks pass: previously read as the graphs being rebuilt rather than saved.)
- `city-state-and-progression` — `Game.City.City`, `CityStatistic`, `ServiceBudgetData` and `TimeData` are save-query anchors, and `CityConfigurationSystem` is the system-level save section that carries city name, theme, required content, the game options and `usedMods`.
- `city-services-and-coverage` — `ServiceRequest` is an anchor, and `ServiceCoverageSystem` and `AvailabilitySystem` rebuild coverage on load rather than saving it.
- `transportation-and-vehicles` — route and vehicle membership is deliberately _not_ saved: `RouteWaypointSystem`, `RouteSegmentSystem`, `RouteVehicleSystem`, `OwnedVehicleSystem`, `GuestVehicleSystem` and `PassengerSystem` all rebuild it in the deserialize middle band.
- `economy-and-companies` — `CompanyAndCargoFixSystem` and `TradeCostFixSystem` are two of the twelve shipped migrations, and `Water_Features`' collapse pattern is the model for anything that borrows a vanilla economic field.
- `simulation-time-and-units` — `TimeData` is an anchor and `PostDeserialize<TimeSystem>` runs in the back band, so anything time-derived is restored after the stream is read.

## Dead ends

- **`survey-mods-techniques.md` §7.5, worked claim by claim (2026-08-04).** Every file it cites still exists and still says what it says; nothing in it failed verification. Its three mechanisms and its migration paragraph were already in this file, but four of its exemplars were not, and three are better than what stood here: `Parcel` as the plain `ISerializable` component, `ModifiedLaneConnections` as the versioned one, and `TLEDataMigrationSystem` as a cross-mod migration, all now findings above; the fourth, `PlopTheGrowables/Code/Systems/PloppedBuilding.cs:15`, is a second instance of a shape already carried. One of its line ranges drifts: `RoadBuilder`'s named version constants are at `RoadBuilderSerializeSystem.cs:29-32`, not `:26-31`, which is `CURRENT_VERSION` through the first two of them. It was right and this file was wrong about `Traffic/Code/Mod.cs`, whose migration comment sits at `:81-82`; that citation is corrected above. Its own weakness is that it names the mechanisms without the surface rules that make them work: nothing in §7.5 mentions the enum cast, the split reader overloads, or the per-element cost of a version int on a buffer.
- **The wiki.** `survey-wiki-inventory.md` records one page touching persistence, `Settings` (`survey-wiki-inventory.md:54`), and it is about the `.coc` settings format — `settings-and-input`'s territory, not this one. A grep of the inventory for "serial" and "save" returns nothing else. The wiki was not fetched live for this topic: there is no page to fetch. This is the ticket's expectation confirmed rather than a surprise.
- **`Colossal.Serialization.Entities` for a mod-extensible tag mechanism.** Searched for any generic parameterisation of the format-tag enum reachable from outside `Game`: `SerializerSystem` closes it over `Game.FormatTags` at both call sites and nothing else in `src/Game` passes a different enum. There is no mod-visible seam.
- **A per-save sidecar file in the corpus.** Grepped all 22 repositories for `File.WriteAllText`, `StreamWriter` and `EnvPath` and inspected every hit in `Recolor`, `CS2-WriteEverywhere`, `RoadBuilder` and `HallOfFame`. Every mod file store is per-installation under `ModsData/<ModId>/`; none is keyed to a save's guid, name or `sessionGuid`, even though `SaveInfo.sessionGuid` exists (`SaveInfo.cs:64`). The one mod that ties disk files to a city does it the other way round, with marker entities inside the save (`RoadBuilder`).
- **`ICleanupComponentData` as a persistence mechanism.** It is not one: the serializer library never tests for it, and the game declares none. `ecs-in-this-game` covers the one mod that uses it, for handle ownership rather than persistence.
- **`ISharedComponentData` in the corpus.** `SharedComponentDataSerializer<T>` exists and works, and zero of the 22 repositories declare a shared component. Nothing to teach from the corpus here.
- **A vanilla site that validates a mod's component layout before reading it.** Searched `Colossal.Serialization.Entities` for any schema, checksum or field-count check beyond the per-block size prefix. There is none: the block size is the only integrity mechanism in the format.

## Catalog gaps

Five entries in `plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md` should gain a sentence; one is a correction of emphasis rather than an addition, and one replaces a sentence already there.

**Traffic** (`mod-catalog.md:135`). Its `Demonstrates` block calls it "the reference example of save-data migration" and names the version constant and repair jobs, but not the two mechanics that make it work. Add:

> Its migration version lives in a save section owned by a system rather than on any entity, with a defaults hook that supplies version zero for saves written before the mod existed, and the class is annotated with the formerly-serialized-as attribute so an earlier namespace still resolves.

Source lines: `Traffic/Code/Systems/Serialization/TrafficDataMigrationSystem.cs:26` (the attribute), `:27` (`IDefaultSerializable`), `:406-421` (`Serialize`/`Deserialize`/`SetDefaults`), `:400-404` (`OnGameLoaded` deferring the work).

**Road Builder** (`mod-catalog.md:184`, with the player-facing half at `:181`). Its entry says custom roads "are saved inside the city and travel with it" and that it demonstrates "versioned custom save serialization with migration constants". It does not name the mechanism, which is the transferable part. Add:

> Marker entities written into the save during the serialize phase and deleted the following frame, each carrying one configuration, so a load knows which of the mod's on-disk files that city needs and can rebuild them from the save when the files are gone.

Source lines: `RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderSerializeSystem.cs:48-72`, `RoadBuilder-CSII/RoadBuilder/Domain/Components/NetworkConfigComponent.cs:10-23`, `RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderConfigCleanupSystem.cs:18-26`, registrations at `RoadBuilder-CSII/RoadBuilder/Mod.cs:67/74`.

**Realistic Trips** (`mod-catalog.md:170`). Its `Demonstrates` names "a versioned serializable component … with a fallback path that reads saves written by older versions". Two amendments: the fallback is a `try`/`catch` inside `Deserialize` that cannot fire on the failure it is written for, and the mod separately reads one of the _game's_ format tags — from a **system's** save section, not a component (`Time2WorkDeathCheckSystem.cs:29` declares `GameSystemBase, IDefaultSerializable, ISerializable`). Verdict on the retired half, established at the review gate of 2026-08-04: `CitizenSchedule.Serialize`/`Deserialize` read and write all ten fields unconditionally (`:43-78`), so the catalog's "fallback path that reads saves written by older versions" was false of that component. Add:

> Reading the game's own save-format tag table from inside a system's save section, to tell which vanilla revision wrote the save it is loading.

Source lines: `Time2Work/NightShift/Systems/Time2WorkDeathCheckSystem.cs:169-176`; the ineffective catch at `Time2Work/NightShift/Components/CitizenSchedule.cs:57-79`.

The three above have since landed in the catalog (`mod-catalog.md:136-137`, `:188`, `:172-173`). The three below came out of §7.5 and have not.

**Platter** (`mod-catalog.md:120-127`). Its `Demonstrates` block runs to eight sentences and none of them mentions serialization, though the mod persists seven serializable components of its own. Add:

> A serializable component in its minimal form — five fields written and read in the same order, with a nested game struct passed straight through and a flags enum cast to its underlying type in both directions, since the reader and writer have no enum overload.

Source lines: `CS2-Platter/Platter/Components/Parcel.cs:33/60-78`, the enum at `:19-28/66/76-77`.

**Write Everywhere** (`mod-catalog.md:288`). The existing sentence, "Versioned serialization with an explicit migration scheme", names neither of the two things its scheme actually shows, and both are transferable where the phrase is not. Replace it with:

> A version int read into a local that bails when the save was written by a newer build of the mod, followed by one appended conditional block per revision that added a field, so a new field costs no edit to any existing branch.
> The same classes are both the mod's XML file format and its save payload, which works because the reader's overload for a reference type deserializes into an instance the caller has already allocated.

Source lines: `CS2-WriteEverywhere/BelzontWE/IO/WETextDataXml.cs:40/94-121` (blocks at `:110/114/118/122`), `BelzontWE/Templates/WETemplateManager.cs:34/66-73/114/121/126`, the dual-purpose classes at `BelzontWE/IO/WETextDataXmlTree.cs:14-15/68` and `WETextDataXml.cs:38/43-53`, the reader overload at `src/Colossal.Core/Colossal.Serialization.Entities/IReader.cs:47`.

**Traffic** (`mod-catalog.md:135-138`) has a third serialization technique its block does not name, distinct from the two that landed there. Add:

> Importing the persisted components of a _different_ mod: the foreign type is resolved by name out of the asset database, queried through a runtime component type, migrated by a chunk job in the deserialize phase's back band, and then removed from every entity that carried it.

Source lines: `Traffic/Code/Systems/ModCompatibility/TLEDataMigrationSystem.cs:34-50/60-91`, registration at `Traffic/Code/Mod.cs:167`.

## Source-list gaps

One correction to `docs/SOURCES.md`, and it is small.

Entry 1, the decompiled game, says the decompile is ground truth for "systems, components, prefabs, the modding API, serialization, the binding layer's C# half" — which held throughout. Nothing to amend there.

Entry 8, the running game, is understated for this topic in a way worth fixing. It lists "live component values, real ECS query results, actual execution order, whether a patch took". It settled three things here that are none of those: the live build's version string, the counts inside a reflection-built registry that exists only at runtime (745 component and 72 system serializers), and **the deserialization context of the currently loaded city** — the version that wrote it and which format tags it carries. Suggested amendment to entry 8's second line:

> Settles what no static read can: live component values, real ECS query results, actual execution order, whether a patch took, the contents of any registry the game builds by reflection at startup, and the load-time context of the city currently open — including the build that wrote its save.

No path, format or scope in `SOURCES.md` turned out wrong, and no artifact this topic needed is missing from it.
