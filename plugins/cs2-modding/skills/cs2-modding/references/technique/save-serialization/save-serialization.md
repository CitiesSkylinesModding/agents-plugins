# Save serialization

Verified against game version 1.6.0f1.

How to make a mod's data survive a save, and how to keep it readable by every later build of the mod.

A save is a stream of length-prefixed buffers: an uncompressed header buffer, then the component type table, the system type table, one buffer per archetype, and one buffer per system serializer, all ZStd-compressed.
The header carries the writing build's version, the compression format of the later buffers, and the full list of the writing build's format-tag names.

A mod joins that stream by implementing an interface on a component, on a buffer element, or on a system.
**Nothing is registered and there is no opt-in list.**
The serializer library reflects over every type the type manager knows and builds a serializer for each one implementing a serialization interface; after a mod assembly loads, the mod manager registers its types and marks the library dirty, and the serializer system rebuilds both libraries on its next update.
No mod code participates.

Settings are not this: they live in their own files with their own lifetime, and belong to `settings-and-input`.

## Choosing where your data lives

Four places, and the question that picks one:

- **Per entity, one bit of presence** → an empty tag component.
  Free, no format to version.
- **Per entity, real data** → a serializable component or buffer element.
  The most common answer, and the one that owes a version int forever.
- **One record for the whole city** — a mod's own settings for this save, a migration version, a table — → a **system's** save section.
  One copy instead of one per entity, and it gets a defaults hook for saves written before your mod existed.
- **Nothing in the save at all** → collapse your state into the vanilla fields you borrow before the write and restore it after, so a player without your mod can still load the city.
  You pay in precision.

Out-of-band files are not a fifth option but a companion to any of them: the save can carry markers saying which of your files a city needs.

**Whichever you pick, `context.purpose` is how you tell the situations apart**, and every mechanism below reaches for it.
It has seven values — `SaveGame`, `NewGame`, `LoadGame`, `SaveMap`, `NewMap`, `LoadMap`, `Cleanup` — and arrives through `Context.purpose` in every `SetDefaults`, `PreDeserialize`, `PostDeserialize` and `OnGameLoaded`, and through `OnGamePreload(Purpose, GameMode)`.
The game branches on it inside its own serializers, writing camera state differently for a map save and writing the used-mods list only for a game save.
(VOLATILE: the seven purpose names and the hooks carrying the context — the `Purpose` enum and `Context`.)

## The mechanisms, and what the library does with them

The serializer library walks every known type and picks one serializer per type from two interfaces and two type traits, in this order:

| Declares                                      | Serializer            | Cost per entity                     |
| --------------------------------------------- | --------------------- | ----------------------------------- |
| `IEmptySerializable`                          | empty                 | **zero bytes** — presence only      |
| `IEmptySerializable` + `IEnableableComponent` | enableable empty      | **one bit**, packed eight to a byte |
| `ISerializable` on `IComponentData`           | component data        | whatever you write                  |
| the same, plus `IEnableableComponent`         | enableable            | one bit, + your bytes when enabled  |
| `ISerializable` on `IBufferElementData`       | buffer element data   | length prefix + elements            |
| the same, plus `IEnableableComponent`         | enableable            | one bit, + the above when enabled   |
| `ISerializable` on `ISharedComponentData`     | shared component data | once per distinct value             |
| **Neither**                                   | none                  | **not saved, silently**             |

**The enabled bit persists for any enableable type, not only an empty one.**
Never hand-write it into your own `Serialize`: the engine already writes it, and a second copy is a format field you must version forever.
**A disabled entity's payload is not written**, and comes back as the component's default rather than what it held.
So an enableable component is the wrong home for state that has to survive while the component is off.

An empty serializer schedules no job at all: presence is carried entirely by the archetype's serializer-index list.

Two modifiers ride on top:

- **`ISerializeAsEnabled`** means _do not persist the enabled bit_.
  The type serializes as if it were not enableable and comes back enabled.
  The game uses it on `PrefabData` and on the notification-icon display data.
  (VOLATILE: which types declare this modifier — the `ISerializeAsEnabled` implementors across the game assembly.)
- **`IStrideSerializable`** adds `int GetStride(Context)`.
  A non-zero stride declares the fixed byte size of one element and switches the writer into byte-plane column filtering before compression.
  What it buys is compression ratio; returning 0 disables it.
  **It is not write-only.**
  `GetStride` is called again on load, with the _load_ context, and the value de-interleaves the byte planes before a single element is read — so a stride that no longer matches what wrote the save silently yields garbage, with no exception.

A flag set travelling beside each type name in the save records which of these applies: `Empty | ComponentData | BufferElementData | SharedComponentData | Enableable | Filtered`.

**A component implementing none of the interfaces is dropped without a word.**
The serializer only visits archetype members it has a serializer index for, so such a component simply does not exist after a reload.

(VOLATILE: the interface names, the serializer type names and the flag set above — the `Colossal.Serialization.Entities` namespace, the component serializer library.)

### The cheapest durable per-entity flag costs nothing

```csharp
public struct MyLevelLocked : IComponentData, IQueryTypeParameter, IEmptySerializable { }
```

That is the whole declaration.
No `Serialize`, no `Deserialize`, no version to maintain, and no bytes in the save beyond the one type-table entry shared by every entity carrying it.
It is the right answer whenever the durable state is one bit and that bit is _presence_.

Add `IEnableableComponent` and the bit becomes a persisted true/false at one bit per entity — the right shape when the archetype should not change as the flag flips.

## What decides whether an entity is in the save at all

The serializer system builds one query.
Its `Any` list is a fixed set of eighteen vanilla anchor types — `PrefabRef`, `LoadedIndex`, `ElectricityFlowNode`, `WaterPipeNode`, `ServiceRequest`, `Game.City.City`, `CityStatistic`, `TimeData` among them — **plus every serializable component type declared outside the game assembly**.
That second half is the load-bearing one: the library collects exactly the types whose assembly differs from the serializer system's own and hands them back to the query.
Its `None` list is `NetCompositionData`, `EffectInstance`, `LivePath`, `Temp`, `Deleted`.

Two consequences:

1.  **A mod component is enough to get its entity saved**, even on an entity carrying no vanilla anchor.
2.  **`Temp` and `Deleted` entities are never saved.**
    Both are excluded because they are already on their way out: `Deleted` is the game's destroy request, not an exclusion flag.
    A cleanup pass queries it every frame with no further filter and destroys every match, so adding `Deleted` to a live entity to keep it out of one save destroys that entity instead.
    To exclude an entity you intend to keep, give it no serializable component and no vanilla anchor.

**The clear query does not know your types.**
The world outlives a load — `mod-lifecycle-and-ordering` has why — and the system that empties it beforehand destroys entities matching a query whose `Any` list is nineteen fixed vanilla types and nothing else.
It does not gain the mod types the save query gains.
So an entity carrying only mod components and no vanilla anchor is written to the save, is not destroyed before the next load, and is recreated from the save on top of the copy that survived.
Both halves have been observed on a running game: the save query matches such an entity and the clear query does not, and one created by hand survived a load into an unrelated city intact.
Delete your own marker entities yourself, from a main-loop system that runs after the one which wrote them, rather than relying on the clear.

(VOLATILE: the eighteen save anchors and the nineteen clear types — the serializer system's query construction and the clear system's own.)

## The read and write contract

`ISerializable` is two symmetric generic methods:

```csharp
void Serialize<TWriter>(TWriter writer) where TWriter : IWriter;
void Deserialize<TReader>(TReader reader) where TReader : IReader;
```

The reader and writer are wide but closed: primitives, the `Unity.Mathematics` vector types, `quaternion`, `Color`/`Color32`, `Hash128`, `Bezier4x3`, `string`, `Entity`, bulk `NativeArray`/`NativeList` overloads, and a generic overload that recurses into a nested `ISerializable` of your own.
The reader adds `Skip(int)`.

Two gaps in that surface bite at the call site:

- **There is no enum overload on either side.**
  An enum field is cast to its underlying type to write, and read into that type and cast home.
  Nothing warns you; the code simply does not compile until you do it.
- **The nested-serializable overloads are not symmetric.**
  The writer takes any `ISerializable`.
  The reader splits: a struct comes back through `Read(out T)`, while a **class is passed in already allocated** and filled in place.
  So a persisted class graph is constructed before it is read, not returned by the read.

Five rules the format enforces or exposes:

- **One length-prefixed block per component per archetype.**
  `Begin` reads a four-byte size, `End` compares the position against it and resyncs on a mismatch; the caller turns that into a data-size-mismatch exception.
  **A byte-count mismatch is therefore detected only at the end of the whole block**, after every entity of that archetype has already been read past the end of its own record.
- **Read and write must be exactly symmetric per entity.**
  There is no per-entity framing.
  A read indexes the buffer directly and advances, so in a Burst release build a read past your own record silently returns the next entity's bytes.
- **A buffer writes its length first, then its elements**, and the deserializer resizes the `DynamicBuffer` to the stored length before reading.
  Your element's `Serialize` is called per element, never per buffer.
- **A `Serialize` that writes nothing throws.**
  The plain component, shared-component and system serializers fire on any zero-byte block; the buffer and enableable ones fire only once some entity actually had something to write, so a test save taken while every buffer is empty or every instance disabled passes and the throw arrives later on a player's city.
  A system section that conditionally writes nothing is the easiest way to meet this, and its message names the system rather than offering the `IEmptySerializable` advice a component gets.
- **A stride and a version-branching `Deserialize` interact.**
  The game's answer is a version-aware stride: its ground-pollution cell returns 4 for saves older than a named constant and 2 after, matching a `Deserialize` that skips a retired field on old saves.
  A mod using both must do the same, or return 0.
  Do not lean on the write-side check to catch a wrong stride: on a buffer element it is an exact-size test, but on a component it only asks whether the payload divides evenly by the entity count times the stride, so a stride declared at an exact fraction of the true element size passes silently.

**Entity references are remapped, and a reference to an unsaved entity becomes `Entity.Null`.**
Writing an `Entity` writes its index in the writer's entity table, or `-1` when it is absent or its version does not match; reading maps the index back or yields `Entity.Null`.
There is no dangling-reference failure mode, and no way to persist a reference to an entity the save did not include.

## Whole-system state

The third option in the chooser is a save section owned by a system rather than an entity.
The system serializer library walks the world's systems and wraps each one implementing `IJobSerializable`, `IDefaultSerializable` or bare `ISerializable`, in that priority.

- **Implement `IDefaultSerializable`.**
  It extends `ISerializable` with `void SetDefaults(Context)`, which **is called for every registered system serializer whose type was absent from the save** — exactly the case of a save written before your mod existed.
- A system implementing plain `ISerializable` works but logs an error at library build time telling you to use one of the other two.
- `IJobSerializable` is the job-scheduling variant, for state large enough to want off-thread work.

**Phase registration is irrelevant here, but existing early is not.**
The library scans the world's systems once and rebuilds only when marked dirty, and the game marks it dirty when a mod assembly loads — before `OnLoad` runs.
So a system created from `OnLoad` gets its save section whether or not it is given a phase, while one created lazily later — from a load callback, a UI action, or first use — has none: `Serialize` is never called on it, `SetDefaults` is never called, and its state silently never reaches a save.
Create it during `OnLoad`, or mark the library dirty yourself afterwards through the serializer system's own public call.

## Writing a version int first

The game needs no per-component version int, because every reader already carries one: `reader.context.version` is the version of the build that wrote the save.
The game's own idiom is a comparison against a named constant:

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

**A mod cannot use that.**
The game's version says nothing about which revision of _your_ component wrote the bytes, and a save can be written by any pairing of game build and mod build.
So write your own int first and branch on it — on a component, or once in a system's save section, with a named constant per format change.

**Read it into a named local, not a discard**, and branch additively rather than in two directions:

```csharp
public void Deserialize<TReader>(TReader reader) where TReader : IReader
{
    reader.Read(out int version);

    reader.Read(out m_Position);
    if (version >= 2)
    {
        reader.Read(out m_Elevation);
    }
    if (version >= 3)
    {
        reader.Read(out m_Flags);
    }
}
```

One `if (version >= N)` block per revision scales past the first format change, where a two-way branch does not.
Give a field the old layout never held an explicit sentinel rather than a default, so the code downstream can tell "absent" from "zero".

**Do not bail out early on a version above your own.**
It is the obvious defence against a player downgrading your mod, and on a component it is the one thing you must not do: the block is framed once for the whole archetype and never per entity, so returning without consuming your whole record leaves the reader mid-record and every entity after it reads its predecessor's tail.
A component simply has no forward compatibility to offer — an old build cannot know how long a new record is.
If you need it, write your own size ahead of your payload and `Skip` the remainder you do not understand.
**A system's save section is the exception**, because its block is framed per section: bailing there costs only that section.

**Where the int rides matters.**
On a buffer element it costs four bytes per _element_, not per entity, so a version on a long buffer is worth hoisting to the owning component or to a system's save section.
A component's version describes its own byte layout; a system section's version describes the repairs a format change implies across the city.
A mod that changes both keeps two numbers.

**What it buys is the ability to add a field later.**
Without it, the only way to change a component's layout is to break every existing save.

**What happens when you skip it and later add a field** is quieter than a failed load, which is the reason the rule is absolute:

1.  The old save's block is shorter than what the new `Deserialize` reads.
    There is no per-entity bound check, so entity 0 consumes entity 1's bytes and so on down the archetype — every value is garbage, not merely absent.
2.  At the end of the block the size check fails and the deserialize job throws.
3.  That job is one of many, Burst-compiled and scheduled off the main thread, and the block-end check resyncs the reader before throwing.

**So nothing visibly breaks.**
The city loads fully populated, every other component reads correctly, and the damage is confined to your own component's values being garbage on every entity that carries it.
Do not expect the phase driver's `System update error during Deserialize->...` line: that catch is for a system's own `Update`, and this throw does not travel through it.
(UNVERIFIED: which message the job's exception reaches the log under, or whether it reaches it at all — settling it needs a mod built to read its own component wrongly, since the throw cannot be provoked from outside.)

Note the asymmetry the load does give you: a **system** section is deserialized on the main thread inside a per-section catch, so one bad system section is logged and the others still load.

**A `try`/`catch` inside `Deserialize` is false comfort.**
It cannot fire on a size mismatch, because the mismatch is detected by the caller after `Deserialize` has already returned.

## The game's own version constants, and what a format break looks like

`Game.Version` is nothing but named build stamps — **273 of them** in 1.6.0f1, ending in `current`.
Each packs its fields so that `>=` is a chronological test, which is what the comparisons below rely on.

**Beside them sits a coarser mechanism the game uses for two thirds of its own migrations: format tags.**
`Game.FormatTags` is a flat enum — **42 members** in 1.6.0f1 — each naming one format change.
On save, every name in the writing build's enum is written as a string.
On load, each name is looked up in the loading build's enum and the matching bit set in `context.format`.

That gives the two directions their shapes:

- **Older save, newer game.**
  The new build's tags are absent from the save, so `context.format.Has(tag)` is false and the migrations gated on it run.
- **Newer save, older game.**
  The save carries a tag name the old build's enum lacks.
  The loader logs an unknown-format-tag line, marks the format unsupported, stops requesting buffers, and deserialization returns false.
  The serializer system then rewrites the context purpose — `LoadGame` becomes `NewGame`, `LoadMap` becomes `NewMap` — with a fresh current-version context.
  **A save from a newer build does not error out; it comes up as a new game.**
  That is what a save-format break looks like from inside.

`context.format.Has` is generic over the tag enum, and C# infers that argument from the value you pass, so source reads `Has(FormatTags.X)`.
Anywhere inference is unavailable — evaluating an expression against a running game, for one — spell it `Has<FormatTags>(FormatTags.X)`, because there is no non-generic overload to fall back on.

**A mod cannot add a format tag.**
The serializer system closes the generic over the game's own enum at both call sites, so the tag table is the game's alone.
Your equivalent is your own version int.
Reading a _game_ tag from inside your own `Deserialize` is legitimate — the tag says something true about the save — but it couples your format to a vanilla one.

(VOLATILE: the 273 constant names, the 42 tag names, and the count of both — `Game.Version` and `Game.FormatTags`.)

## The two phases, and where a mod migration goes

`SystemUpdatePhase.Serialize` and `SystemUpdatePhase.Deserialize` each fire exactly once per save and per load; `mod-lifecycle-and-ordering` owns their position in the phase tree.

The deserialize phase is where the game does its own migrations, and they gate on one of two things: a format tag, or `context.version` against a named constant.
Of those two gates, only the second is available to a mod.
[deserialize-phase-census.md](deserialize-phase-census.md) names the twelve shipped migrations, gives the split between the two shapes, and maps the phase's three bands — including which barrier is already closed by the time each one runs.

**A thirteenth system uses neither gate, and it is the one a mod should usually copy.**
The required-component system is a long list of "has X, lacks Y → add Y" queries built in `OnCreate` and applied unconditionally in `OnUpdate`, each behind an emptiness check:

```csharp
if (!m_BuildingEfficiencyQuery.IsEmptyIgnoreFilter)
{
    base.EntityManager.AddComponent<Efficiency>(m_BuildingEfficiencyQuery);
}
```

It needs no version and no tag, because the query itself is the test.
That is how the game backfills a component added to an existing archetype, and it is exactly a mod's problem when its component must appear on entities from a save written before the mod existed.

**Why a mod migration sometimes cannot run in the deserialize phase.**
That phase's middle band runs while the world is only half rebuilt: net compositions, lane geometry and most derived state are produced _after_ it, in the modification phases of the first simulation frames.
A migration that reads derived state therefore cannot sit in the phase built for migrations, and moves to a modification phase instead.

The split that makes that work is three parts, and all three are needed:

1.  **The system is `IDefaultSerializable`**, so it gets a save section wherever it is registered.
    `Deserialize` reads one int into a version field; `SetDefaults` sets it to zero for saves that predate the mod.
2.  **`OnGameLoaded(Context)` sets a loaded flag.**
    The callback fires after the whole deserialize phase has run and carries the `Context`, so the version and the format tags are in hand, and no phase registration is needed to receive it — `mod-lifecycle-and-ordering` owns the subscription and the two conditions that silently withhold it, of which **overriding `OnCreate` without calling `base.OnCreate()`** is the one that costs a migration everything.
3.  **`OnUpdate` runs on the chosen phase**, sees the flag, checks `context.purpose`, branches on the version, runs the repair, and clears the flag.

**The purpose check in step 3 is not optional.**
The deserialize phase runs on a brand-new city too, and with no save to read, `SetDefaults` sets your version field to zero and `OnGameLoaded` still fires — so a migration written for legacy data runs against a freshly generated map unless it tests for `Purpose.LoadGame` first.
Version zero means _this save predates the mod_, and only the purpose distinguishes that from _there was no save at all_.

## Type identity, renames and uninstalls

Both type tables store the **assembly-qualified name**.
On load, resolution is three stages:

1.  `Type.GetType(storedName)`.
    **The runtime binds this by simple assembly name**, ignoring the version and public-key-token fields the stored name carries, so bumping your assembly version does not by itself strand a save.
2.  Failing that, the stored name is looked up in a table built from `[FormerlySerializedAs]` attributes, **progressively trimming from the last comma** — `Ns.Type, Asm, Version=…, Culture=…, PublicKeyToken=…` is retried as `Ns.Type, Asm, Version=…`, then `Ns.Type, Asm`, then `Ns.Type`.
    The trimming only widens what matches an attribute, so the attribute may name the old type at whatever precision is convenient; it does nothing for a type that carries no attribute.
3.  Failing both — or if the resolved type implements neither serialization interface — the loader logs a not-serializable line and treats the entry as obsolete.

**An obsolete type is skipped, not fatal.**
Its serializer reads the block's size prefix and jumps to the end, and each archetype is rebuilt from only the types that resolved.
So **uninstalling a mod loses that mod's components and keeps the city**, with one log line per lost type.

One further guard: where the resolved type's serializer kind disagrees with the kind recorded in the save — a component that became a shared component, say — the loader logs a type mismatch and falls back to the obsolete serializer, discarding the data rather than misreading it.
Two crossings escape it deliberately.
A type recorded as a component and now declared as a buffer is handed to the buffer serializer, which recognises the saved kind and reads each old record into a one-element buffer — so promoting a component to a buffer element migrates every existing save cleanly, with no rename and no version gate.
And the check is skipped whenever either side is the empty kind, so an `IEmptySerializable` tag that gains a payload, or loses one, is resolved by the save's own recorded kind instead — which is why neither of those directions misreads.

**Rename the type that owns a save section and you owe it a `[FormerlySerializedAs]`.**
This is the serialization library's own attribute, not the Unity attribute of the same name.

**Prefabs are a separate identity space with the same story.**
The prefab system writes an ordered list of prefab ids and entities reference prefabs by index into it.
An id that no longer resolves becomes an obsolete prefab entity: it gets a negative prefab index, its `PrefabData` is disabled, the missing id is registered and logged, and a further system fills in placeholder data for the object, net, lane and area families.
So a mod that adds prefabs leaves standing placeholders in the city when it is removed, not a failed load.

(VOLATILE: `[FormerlySerializedAs]`'s namespace — `Colossal.Serialization.Entities`, distinct from Unity's own attribute of the same name.)

## Out-of-band files, and what tells a load it needs them

A saved city is **not one file**.
Saving builds a transient asset database, fills it, and packages the whole of it as one asset under the user's saves path.
Inside it:

- **The entity stream**, the save-game data asset.
- **A metadata record** holding a JSON info blob — the city's name, its headline statistics and its options, plus three lists that matter here.
- **A preview texture.**
- **Prefab assets cloned into the package.**
  The prefab system copies the current climate prefab and the water and terrain render settings into the save database and remaps each prefab entity's guid to the packaged asset's, so the stored prefab id carries the packaged guid instead of the runtime one.
  **That is the game's own mechanism for shipping an asset inside a save and having the load find it.**

The three lists:

- **Content prerequisites** — the DLC and content packs, checked before the load starts.
- **Mods enabled** — loaded from the save and then **unioned** with the currently enabled mods before being written back.
  It is cumulative: every mod the city has _ever_ been saved with, not the set it currently needs.
- **Prefab references** — the packaged prefab assets, kept so a later save knows which ones it already carried.

**A mod's own files have no such index.**
A file written under the user data path is per-installation, not per-city, and nothing in the save points at it.
If a city needs to know which of your files it depends on, put the answer inside the save:

Write one bare marker entity per item from a system registered **`UpdateBefore` in `Serialize`**, each carrying a component holding that item's id, and have that component's `Serialize` write the item's whole configuration into the stream as well — so the save is self-sufficient even when the on-disk file is gone.
The band is the whole of it: the serializer and the writer both sit in `UpdateAt`, and a mod's `UpdateAt` sorts after them, so markers created there are built after the save has been written and never reach it.
Then delete the markers from a system on the main loop, which runs later in the same frame and is also the answer to the clear-query trap above.

## Persisting nothing at all

The fourth option is to write no mod component into the save, and instead fold your custom state back into the vanilla fields you borrow: one system registered `UpdateBefore` in `Serialize` collapses it, and systems registered `UpdateAfter` restore it once the write has run.
The result is a save a player without the mod can load correctly, at the cost of your own precision.

This is the option that leans hardest on `context.purpose`: the collapse must not run when there is nothing to collapse, and the restore must not run on a new map.

## What this reference hands to others

`ecs-in-this-game` owns the component kinds these mechanisms attach to and the archetype model the loader reconstructs.
`mod-lifecycle-and-ordering` owns the phase tree, the once-per-load firing of `Serialize` and `Deserialize`, the pre- and post-deserialize wrappers, and the per-system exception catching this file's failure mode rests on.
`prefabs-and-assets` owns prefab identity and the asset database; this file owns only what the save does with them.
`diagnostics` owns the log; the strings a save problem produces are serializer-not-found (the type resolved but has no serializer), not-serializable-type (the type did not resolve — what an uninstalled mod produces), serializer-type-mismatch, unknown-format-tag and unknown-prefab-id.
Two informational lines bracket a load, and the second is itself diagnostic: the serialized-version line is emitted always, while the format-tags line is emitted only where deserialization succeeded — so its absence is the signature of a save from a newer build, not evidence the phase never ran.
`settings-and-input` owns the settings files, the other durable store, which share none of this machinery.
`mod-compatibility` takes from here that a save records used mods cumulatively, that a foreign mod's save section is skipped rather than fatal, and that a foreign mod's components stay readable and removable by anyone who can resolve their type by name — which is how one mod migrates another's data.

The mechanics references whose components a mod would persist against:

- `roads-and-traffic` — the densest case, and the home of the stride-serialized net types and the version-branching area node.
- `zoning-buildings-and-land-value` — building-level tags are the archetypal empty-serializable case.
- `citizens-and-households` — per-citizen persisted state, and three of the twelve shipped migrations repair household and citizen data on load.
- `environment-and-pollution` — the cell maps are the stride-serialized types, and the ground-pollution cell carries the version-aware stride.
- `utilities-and-flow-networks` — four of its node and edge types are save-query anchors, and their graphs are rebuilt on load rather than saved.
- `city-state-and-progression` — the city, statistic, budget and time components are anchors, and the city configuration system is the system-level save section carrying name, theme, required content, options and the used-mods list.
- `city-services-and-coverage` — service requests are an anchor, and coverage is rebuilt on load.
- `transportation-and-vehicles` — route and vehicle membership is deliberately _not_ saved and is rebuilt in the deserialize middle band.
- `economy-and-companies` — two of the twelve shipped migrations live here, and the collapse-into-vanilla-fields pattern is the model for anything borrowing an economic field.
- `simulation-time-and-units` — time data is an anchor and the time system runs in the back band, so anything time-derived is restored after the stream is read.
