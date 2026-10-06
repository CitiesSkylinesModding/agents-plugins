# A building chronicle that survives a save

## Kind

bug report

## Prompt

I'm working on a Cities: Skylines II mod that keeps a chronicle of the city: every building remembers the day it was built, and the city as a whole remembers the day it was founded and how many buildings have gone up since. The code is in this workspace.

Two things are going wrong. I recently added a second value to what each building remembers (the city's population on the day it was built), and now saves made before that change load with nonsense built-days on every building. And the city-wide part misbehaves when I switch cities in one session: after playing one city, loading another save or starting a new city shows the previous city's founding day and building count, and buildings that were already standing before I installed the mod get dated to the day the save was loaded instead of being marked unknown.

The mod isn't published yet, so the saves from before the change don't have to keep working, but I'll keep adding values to the chronicle as the mod grows.

## Checklist

### The component's layout is versioned by an int of the mod's own

- Falls in: the component's `Deserialize` gates a field on `reader.context.version` compared against a `Game.Version` constant, or reads its fields with no version written by the mod ahead of them.
- Avoids: `Serialize` writes a mod-owned version constant before any field, and `Deserialize` reads it first into a named local (`reader.Read(out int version)`), gating any field added after the first layout on `version >= N`.
- Reference: cs2-modding/references/technique/save-serialization/save-serialization.md
- Source: src/Colossal.Core/Colossal.Serialization.Entities/EntityDeserializer.cs:257

### The component's `Deserialize` has no `try`/`catch`

- Falls in: the component's `Deserialize` wraps its `reader.Read` calls in a `try`/`catch` that assigns fallback values on failure.
- Avoids: `Deserialize` reads unconditionally, with no `try`/`catch`; which fields are present is decided by the version int alone.
- Reference: cs2-modding/references/technique/save-serialization/save-serialization.md
- Source: src/Colossal.Core/Colossal.Serialization.Entities/ComponentDataSerializer.cs:185

### The system's save section is `IDefaultSerializable`

- Falls in: the system declares plain `ISerializable` and has no `SetDefaults(Context)`, so a save without its section leaves the previous city's values in its fields.
- Avoids: the system declares `IDefaultSerializable`, and its `SetDefaults(Context)` resets every persisted field, the version field to zero included.
- Reference: cs2-modding/references/technique/save-serialization/save-serialization.md
- Source: src/Colossal.Core/Colossal.Serialization.Entities/ComponentSystemSerializer.cs:72

### `Deserialize` and `SetDefaults` only read the stream

- Falls in: the system's `Deserialize` or `SetDefaults` queries or changes the world after reading its fields, through `EntityManager` calls, `ToEntityArray`, `IsEmptyIgnoreFilter` or any other query use.
- Avoids: `Deserialize` and `SetDefaults` contain only `reader.Read` calls and field assignments, and the work on entities runs from `OnUpdate` behind a flag set in `OnGameLoaded(Context)`, or from `OnGameLoaded` itself.
- Reference: cs2-modding/references/technique/save-serialization/save-serialization.md
- Source: src/Colossal.Core/Colossal.Serialization.Entities/EntityDeserializer.cs:184

### A new city is told from a save predating the mod by `context.purpose`

- Falls in: the code decides it is looking at a city the chronicle has never seen from its own state alone, a sentinel founding day or a version of zero, with no test of `context.purpose`.
- Avoids: the post-load branch tests the `Context` for `Purpose.NewGame` against `Purpose.LoadGame` before treating a version of zero as a save written before the mod existed.
- Reference: cs2-modding/references/technique/save-serialization/save-serialization.md
- Source: src/Colossal.Core/Colossal.Serialization.Entities/EntityDeserializer.cs:608

## Roots

- decompile
