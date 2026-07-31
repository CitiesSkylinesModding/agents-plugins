using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>
/// ECS operations over SDB invokes: world selection, EntityManager access, entity queries, and
/// component read/write.
/// Component access goes through the generic EntityManager.Get/SetComponentData&lt;T&gt;
/// instantiated live (protocol 2.24+).
/// Instances are only valid while the VM stays suspended and connected; build one per operation.
/// Staying per-operation is load-bearing rather than incidental: the presence memo below is safe
/// only because an archetype cannot change under an instance that lives inside one suspend window.
/// Whatever an ECS operation may remember for LONGER than that lives on the
/// <see cref="EcsCatalog" /> this is a view over.
/// </summary>
public sealed class Ecs {
  private readonly Invoker inv;

  private readonly EcsCatalog catalog;

  public Ecs(Invoker inv, EcsCatalog catalog, string worldName = null) {
    this.inv = inv;
    this.catalog = catalog;

    var world = catalog.WorldFor(worldName);

    this.World = world.World;
    this.WorldName = world.Name;
    this.EntityManager = world.EntityManager;
    this.EntityManagerType = world.EntityManagerType;
  }

  public Value World { get; }

  public string WorldName { get; }

  public Value EntityManager { get; }

  public TypeMirror EntityManagerType { get; }

  /// <summary>
  /// The game's own ComponentType for a component type mirror, which is how a type is named to
  /// every Entities API that takes one and how its type index is learned.
  /// </summary>
  private StructMirror ComponentTypeOf(TypeMirror type) {
    var ctType = this.inv.ResolveType("Unity.Entities.ComponentType");

    return (StructMirror) this.inv.InvokeStatic(
      ctType,
      this.inv.FindMethod(ctType, "ReadWrite", 1, paramTypes: ["Type"]),
      this.inv.TypeObject(type)
    );
  }

  /// <summary>Builds an EntityQuery requiring all the given component types (ReadWrite).</summary>
  public Value CreateQuery(TypeMirror[] componentTypes) {
    var ctType = this.inv.ResolveType("Unity.Entities.ComponentType");

    var cts = componentTypes.Select(Value (t) => this.ComponentTypeOf(t)).ToArray();

    // ComponentType[] built debuggee-side via Array.CreateInstance + SetValues.
    var arrayType = this.inv.ResolveType("System.Array");

    var arr = (ArrayMirror) this.inv.InvokeStatic(
      arrayType,
      this.inv.FindMethod(arrayType, "CreateInstance", 2, paramTypes: ["Type", "Int32"]),
      this.inv.TypeObject(ctType),
      this.inv.Prim(componentTypes.Length)
    );

    arr.SetValues(0, cts);

    return this.inv.Invoke(
      this.EntityManager,
      this.inv.FindMethod(
        this.EntityManagerType,
        "CreateEntityQuery",
        1,
        paramTypes: ["ComponentType[]"]
      ),
      arr
    );
  }

  public int Count(Value query) =>
    (int) ((PrimitiveValue) this.inv.Invoke(query, "CalculateEntityCount")).Value;

  /// <summary>
  /// Materializes the query's entities as a managed Entity[] in the debuggee (ToEntityArray with
  /// the Temp allocator, then NativeArray.ToArray) and returns its mirror.
  /// </summary>
  public ArrayMirror EntityArray(Value query) {
    var handleType = this.inv.ResolveType("Unity.Collections.AllocatorManager+AllocatorHandle");

    var handle = this.inv.InvokeStatic(
      handleType,
      this.inv.FindMethod(handleType, "op_Implicit", 1, paramTypes: ["Allocator"]),
      this.TempAllocator()
    );

    var native = this.inv.Invoke(query, "ToEntityArray", handle);

    return (ArrayMirror) this.inv.Invoke(native, "ToArray");
  }

  /// <summary>
  /// The <c>Allocator.Temp</c> enum value (2), which every collection this class asks the game to
  /// allocate uses: Temp rewinds when the game's frame ends, so none of them needs a Dispose.
  /// A held suspend window ends no frame, so allocations made under one accumulate until the game
  /// runs again -- a reason to keep a window short, not to switch allocators.
  /// </summary>
  private EnumMirror TempAllocator() =>
    this.inv.Vm.CreateEnumMirror(
      this.inv.ResolveType("Unity.Collections.Allocator"),
      this.inv.Prim(2)
    );

  /// <summary>
  /// Resolves an <c>index[:version]</c> spec to a live entity of this world: the rule every tool
  /// that TARGETS an entity shares, so naming one means the same thing whichever tool reads it.
  /// A bare index is resolved to whatever version is live at that index, and an explicit version is
  /// built client-side then verified, so a stale one fails instead of reading its successor.
  /// An entity VALUE being written is a different question, answered literally by
  /// <see cref="CoerceArg" />.
  /// </summary>
  public StructMirror ResolveEntity(string spec) {
    var (index, version) = Ecs.ParseEntitySpec(spec);

    var highest = this.HighestEntityIndex();

    if (highest is {} bound && (index < 0 || index > bound)) {
      throw new InvalidOperationException(
        $"entity index {index} is out of range for world '{this.WorldName}' (valid: 0-{bound})"
      );
    }

    // A named version is verified, not trusted. Where the bound above was unavailable this asks
    // Exists unguarded, a deliberate floor rather than an oversight: refusing instead would leave a
    // target that cannot report its bound with no way to name an entity at all. The bare index
    // below is the path that refuses, because it needs the bound to mean anything.
    if (version is {} v) {
      var named = this.MakeEntity(index, v);

      if (!this.Exists(named)) {
        throw new InvalidOperationException(
          $"entity {index}:{v} does not exist (recycled index or wrong version?)"
        );
      }

      return named;
    }

    // Bare-index resolution needs BOTH members: the lookup itself, and the bound that keeps it
    // inside the store. Without either, refuse rather than resolve unguarded - naming the entity in
    // full still works, and is the one degradation that cannot crash the game.
    // Probing beats inferring the version: Unity.Entities reports assembly version 0.0.0.0.
    var byIndex = this.FindMember("GetEntityByEntityIndex", 1, ["Int32"]);

    if (byIndex is null || highest is null) {
      var absent = byIndex is null ? "GetEntityByEntityIndex" : "HighestEntityIndex";

      throw new InvalidOperationException(
        "this target's Unity Entities version cannot resolve a bare entity index " +
        $"(EntityManager.{absent} is absent); name the entity as \"index:version\""
      );
    }

    var live = (StructMirror) this.inv.Invoke(this.EntityManager, byIndex, this.inv.Prim(index));

    // A free slot (never used, or destroyed) answers Entity.Null, whose version is 0 - a version
    // live entities never carry. The fields are already on the wire, so this costs no invoke.
    if ((int) ((PrimitiveValue) live["Version"]).Value is 0) {
      throw new InvalidOperationException(
        $"no live entity at index {index} in world '{this.WorldName}' (the slot is free)"
      );
    }

    return live;
  }

  private int? storeBound;

  private bool storeBoundRead;

  /// <summary>
  /// The highest index the entity store covers, inclusively; null when the target cannot say.
  /// Every path that indexes the store goes through this FIRST, because the store is indexed
  /// UNCHECKED: out of range, the by-index lookup and Exists alike read foreign memory, and far
  /// enough out both fault the game with an in-game NullReferenceException (verified live on both).
  /// Read once per instance, which asks nothing of the caller: the store cannot grow while the
  /// game is suspended, and one instance lives inside one suspend window.
  /// The flag is what keeps "the target cannot say" distinguishable from "not read yet".
  /// </summary>
  private int? HighestEntityIndex() {
    if (this.storeBoundRead) {
      return this.storeBound;
    }

    var member = this.FindMember("HighestEntityIndex", 0);

    this.storeBound = member is null
      ? null
      : (int) ((PrimitiveValue) this.inv.Invoke(this.EntityManager, member)).Value;

    this.storeBoundRead = true;

    return this.storeBound;
  }

  /// <summary>
  /// Probes a member of the target's Entities API, accepting a property in place of a method: the
  /// same fact is exposed either way across Entities versions, and a getter is reached under its
  /// <c>get_</c> name. <paramref name="type" /> defaults to the EntityManager, which declares most
  /// of what is probed here.
  /// Costs nothing on the wire, since a mirror caches its own member list.
  /// </summary>
  private MethodMirror FindMember(
    string name,
    int argc,
    string[] paramTypes = null,
    TypeMirror type = null
  ) {
    var owner = type ?? this.EntityManagerType;

    return this.inv.FindMethodOrNull(owner, name, argc, paramTypes: paramTypes) ??
      (argc is 0 ? this.inv.FindMethodOrNull(owner, $"get_{name}", 0) : null);
  }

  /// <summary>
  /// An entity's index and version, read off the mirror it already holds, so naming one in a
  /// message or keying on one costs nothing on the wire.
  /// </summary>
  private static (int Index, int Version) Id(StructMirror entity) =>
  (
    (int) ((PrimitiveValue) entity["Index"]).Value,
    (int) ((PrimitiveValue) entity["Version"]).Value
  );

  /// <summary>
  /// Entity-and-type pairs whose presence is settled, keyed by the KIND that settled them: a buffer
  /// element and a component of the same name share a type index, so "carries it as a component" is
  /// not the same fact as "carries it as a buffer", and one must never answer for the other.
  /// </summary>
  private readonly HashSet<(int Index, int Version, TypeMirror Type, string Kind)> carried = [];

  /// <summary>
  /// Refuses a type the entity does not carry, the one gate every accessor below goes through.
  /// The generic accessors derive a chunk offset from the archetype, so where the collections
  /// safety checks are compiled out they read and write memory the entity does not own: verified
  /// live, reading an absent component answered a plausible all-zero value and an absent buffer
  /// answered length 0, and asking for WRITE access on one degraded the entity store until the game
  /// died.
  /// <paramref name="hasMethod" /> is the generic EntityManager predicate for the kind asked about,
  /// and <paramref name="kind" /> names it in the refusal.
  /// A confirmed pair is remembered, because one read-modify-write asks three times and an
  /// archetype cannot change under an instance that lives inside a single suspend window.
  /// </summary>
  private void RequirePresence(
    StructMirror entity,
    TypeMirror type,
    string hasMethod,
    string kind
  ) {
    var id = Ecs.Id(entity);
    var key = (id.Index, id.Version, Type: type, Kind: kind);

    if (this.carried.Contains(key)) {
      return;
    }

    var has = this.Accessor(hasMethod, 1, type);

    if ((bool) ((PrimitiveValue) this.inv.Invoke(this.EntityManager, has, entity)).Value) {
      _ = this.carried.Add(key);

      return;
    }

    throw new InvalidOperationException(
      $"entity {key.Index}:{key.Version} has no {type.FullName} {kind}"
    );
  }

  /// <summary>
  /// Records a type as carried without asking the game, for the one caller that ENUMERATED the
  /// entity's archetype in this very suspend window: the gate's invariant is then satisfied by
  /// construction rather than by an invoke that re-establishes what was just read.
  /// </summary>
  private void MarkCarried(StructMirror entity, TypeMirror type, string kind) {
    var id = Ecs.Id(entity);

    _ = this.carried.Add((id.Index, id.Version, type, kind));
  }

  /// <summary>
  /// One of the EntityManager's generic accessors, instantiated over the component type; the
  /// instantiation is memoized for the attach, so a loop over an archetype pays it once per type.
  /// </summary>
  private MethodMirror Accessor(string name, int argc, TypeMirror componentType) =>
    this.inv.Instantiate(
      this.inv.FindMethod(this.EntityManagerType, name, argc, 1, ["Entity"]),
      [componentType]
    );

  public Value GetComponent(StructMirror entity, TypeMirror componentType) {
    this.RequirePresence(entity, componentType, "HasComponent", "component");

    return this.inv.Invoke(
      this.EntityManager,
      this.Accessor("GetComponentData", 1, componentType),
      entity
    );
  }

  public void SetComponent(StructMirror entity, TypeMirror componentType, StructMirror value) {
    this.RequirePresence(entity, componentType, "HasComponent", "component");

    this.inv.Invoke(
      this.EntityManager,
      this.Accessor("SetComponentData", 2, componentType),
      entity,
      value
    );
  }

  /// <summary>
  /// Reports the entity's whole archetype: every component type it carries, the kind of storage
  /// each uses, and, for the enableable ones, whether they are currently enabled.
  /// The kind is what tells a caller which accessor can read a type, so a kind no tool can read is
  /// still listed: state the caller cannot reach is a different answer from state that is absent.
  /// With <paramref name="values" />, every entry also carries what is IN the component (see
  /// <see cref="ValueOf" />), which is one read per component on top of the listing.
  /// What each type index means is remembered for the attach, so listing a second entity spends
  /// nothing on the types the first one already described.
  /// </summary>
  public EntityComponents ListComponents(StructMirror entity, bool values = false) {
    // The allocator parameter is the bare enum here, where ToEntityArray above takes a handle, so
    // the shape is pinned rather than assumed; probe it, since a version that moved it would
    // otherwise fail on a member-not-found no caller can act on.
    var getTypes = this.FindMember("GetComponentTypes", 2, ["Entity", "Allocator"]);

    if (getTypes is null) {
      throw new InvalidOperationException(
        "this target's Unity Entities version cannot list an entity's components " +
        "(EntityManager.GetComponentTypes(Entity, Allocator) is absent)"
      );
    }

    var types = this.inv.Invoke(this.EntityManager, getTypes, entity, this.TempAllocator());

    // Materialized as a managed array so every ComponentType crosses the wire in ONE read; the
    // per-component reads below are where this call's cost actually lives.
    var arr = (ArrayMirror) this.inv.Invoke(types, "ToArray");

    var componentTypes = arr.GetValues(0, arr.Length);

    var flags = this.catalog.Flags;

    // Enabled state costs a member the target may not have, and its absence turns the whole column
    // off: a target that cannot answer must say so, because an unreported state reads as "enabled".
    var isEnabled = this.FindMember("IsComponentEnabled", 2, ["Entity", "ComponentType"]);

    // Whether a type is enableable is one of the bits the index encodes; where it cannot be
    // decoded it is a property invoke per type instead, and THAT member's absence turns the column
    // off just the same.
    // Probing costs nothing on the wire, so it happens whether the masks are expected to answer:
    // the decode still falls through per type on a target whose index this cannot read.
    var isEnableable = this.FindMember(
      "IsEnableable",
      0,
      type: this.inv.ResolveType("Unity.Entities.ComponentType")
    );

    var absent = isEnabled is null
      ? "EntityManager.IsComponentEnabled"
      : flags is null && isEnableable is null
        ? "ComponentType.IsEnableable"
        : null;

    // One absent member turns the column off, which the loop reads off enabledGetter being null.
    var enabledGetter = absent is null ? isEnabled : null;
    var enableableProbe = absent is null ? isEnableable : null;

    var components = new List<EntityComponentInfo>(componentTypes.Count);

    // Whether a type is enableable can go unanswered per TYPE, not just per target: the decode
    // needs an index this target may hold in an unreadable shape, and the invoke needs a member it
    // may not expose. An entry nobody could classify is silent, and silence must be explained.
    var unclassified = false;

    foreach (var ct in componentTypes) {
      var described = this.Describe(ct, flags);

      // With the column off, nothing is asked and nothing is decoded: whatever it would have
      // learned, no entry could report.
      var enableable = enabledGetter is null
        ? null
        : this.EnableableOrNull(ct, described, flags, enableableProbe);

      unclassified |= absent is null && enableable is null;

      var (value, valueError) = values ? this.ValueOf(entity, described) : default;

      components.Add(
        new EntityComponentInfo {
          Name = described.Name,
          Kind = described.Kind,
          Enabled = enableable is true
            ? this.EnabledOrNull(entity, ct, described.Kind, enabledGetter)
            : null,
          Value = value,
          ValueError = valueError
        }
      );
    }

    return new EntityComponents {
      Components = components,
      EnabledStateNote = absent is not null
        ? $"this target's Unity Entities version cannot report enabled state ({absent} is " +
        "absent), so no entry carries one; that is not the same as none being disabled"
        : unclassified
          ? "this target's Unity Entities version could not tell whether every component type " +
          "here is enableable, so an entry carrying no enabled state may be one it could not " +
          "classify rather than one that is not enableable"
          : null
    };
  }

  /// <summary>
  /// Whether the entity carries an enabled bit for this type, null when nothing could say.
  /// Deliberately NOT memoized with the rest of the description: the answer depends on what this
  /// target could be asked at the time, so caching it would let one degraded operation speak for
  /// every later one.
  /// The decode is free where it applies, and the probe costs the invoke the fallback always cost.
  /// </summary>
  private bool? EnableableOrNull(
    Value componentType,
    EcsComponentType described,
    TypeIndexFlags flags,
    MethodMirror isEnableable
  ) {
    // A shared or chunk component keeps its data, and its enabled bit, outside the entity, so the
    // question does not arise -- and asking it anyway would spend an invoke per such entry on the
    // fallback path, for an answer no entry can report.
    if (described.Kind is "shared" or "chunk") {
      return false;
    }

    if (flags is not null && described.TypeIndex is {} index) {
      return flags.IsEnableable(index);
    }

    return isEnableable is not null ? this.Flag(componentType, isEnableable) : null;
  }

  /// <summary>
  /// Works out what one listed ComponentType means, through the attach's memo when the type index
  /// is readable and from scratch when it is not.
  /// Everything held here is a property of loaded code, which is what makes remembering it safe.
  /// </summary>
  private EcsComponentType Describe(Value componentType, TypeIndexFlags flags) {
    var typeIndex = Ecs.TypeIndexOf(componentType);

    if (typeIndex is {} known && this.catalog.TryDescribe(known, out var remembered)) {
      return remembered;
    }

    // The kind is bits of the index, so the masks serve only where BOTH are in hand; otherwise the
    // game answers it itself, one property at a time.
    var described = new EcsComponentType {
      Name = this.ManagedTypeName(componentType),
      Kind = typeIndex is {} index && flags is not null
        ? flags.KindOf(index)
        : this.InvokedKindOf(componentType),
      TypeIndex = typeIndex
    };

    if (typeIndex is {} key) {
      this.catalog.Remember(key, described);
    }

    return described;
  }

  /// <summary>
  /// What the component holds, rendered at <see cref="Invoker.ReadDepth" />.
  /// Only a plain unmanaged component has fields an accessor here can reach; every other kind
  /// answers its own kind where a value would go, so a caller scanning the column reads "reachable
  /// another way" rather than an empty cell it could mistake for "carries nothing".
  /// A read that fails is recorded on the entry alone: one component the game refuses to hand over
  /// must cost its own value, never the listing the caller asked for.
  /// </summary>
  private (string Value, string Error) ValueOf(StructMirror entity, EcsComponentType described) {
    if (described.Kind is not "component") {
      return (described.Kind, null);
    }

    if (described.Name is null) {
      return (null, "the target cannot name this component type, so its value cannot be read");
    }

    try {
      TypeMirror type;

      if (described.TypeIndex is {} index) {
        type = this.ProvenType(described.Name, index) ??
          throw new InvalidOperationException(
            $"no loaded type answers to the type index this entity's {described.Name} carries, " +
            "so a value read off that name would not be this entity's"
          );

        // The archetype was enumerated in this very suspend window and the type is proven to be
        // the one the entry named, so the read's own gate has nothing left to establish.
        this.MarkCarried(entity, type, "component");
      }
      else {
        // A target whose type index this cannot read cannot prove identity either; the read falls
        // back to the name and keeps the presence gate, which is the weaker check and the only one
        // left.
        type = this.inv.FindTypeOrNull(described.Name) ??
          throw new InvalidOperationException(
            $"type '{described.Name}' no longer resolves on this target"
          );
      }

      return (this.inv.Format(this.GetComponent(entity, type), Invoker.ReadDepth), null);
    }
    catch (Exception ex) when (!UnitySession.IsDisconnect(ex)) {
      return (null, ex.Message);
    }
  }

  /// <summary>
  /// Indexes this operation already failed to prove, so one listing does not re-walk the same
  /// refusal per entity it touches. It dies with the instance, which is what keeps a refusal from
  /// outliving the moment that produced it.
  /// </summary>
  private readonly HashSet<int> refused = [];

  /// <summary>
  /// The type mirror PROVEN to be the one the listed entry names, null when none is, settled once
  /// per type per attach.
  /// The listing hands back a NAME, and a name is a label rather than a handle: resolving one
  /// answers the first match across the debuggee, so two assemblies declaring the same full name
  /// would otherwise let a plausible value be read off the wrong type.
  /// Building a candidate's own ComponentType costs one invoke and settles it, since the indexes
  /// then compare client-side.
  /// The candidates come from the case-SENSITIVE lookup, because the name came off a live type and
  /// matching C# name semantics narrows the field before the proof even runs.
  /// </summary>
  private TypeMirror ProvenType(string name, int typeIndex) {
    if (this.catalog.TryProvenType(typeIndex, out var settled)) {
      return settled;
    }

    if (this.refused.Contains(typeIndex)) {
      return null;
    }

    var proven = this.ProveIdentity(name, typeIndex);

    // Only a PROVEN identity settles for the attach. A refusal describes the moment rather than the
    // type -- the name may not resolve yet, and the debuggee loads assemblies over time -- so it is
    // remembered only for as long as this operation, and the next one asks again.
    if (proven is null) {
      _ = this.refused.Add(typeIndex);
    }
    else {
      this.catalog.SettleIdentity(typeIndex, proven);
    }

    return proven;
  }

  /// <summary>
  /// Tests the types the name resolves to until one answers to the index.
  /// The cached first match is tried on its own first, because a name with no namesake is the whole
  /// of the common case; only a mismatch pays for the full candidate list, which is exactly the
  /// collision this proof exists to settle.
  /// </summary>
  private TypeMirror ProveIdentity(string name, int typeIndex) {
    var first = this.inv.FindTypeOrNull(name);

    if (first is null) {
      return null;
    }

    if (this.CarriesIndex(first, typeIndex)) {
      return first;
    }

    return this.inv.FindTypes(name)
      .FirstOrDefault(candidate => candidate != first && this.CarriesIndex(candidate, typeIndex));
  }

  /// <summary>
  /// Whether the type's own ComponentType carries the given index.
  /// A type the target's type manager never registered throws instead of answering, and that
  /// refuses the one candidate rather than the listing.
  /// </summary>
  private bool CarriesIndex(TypeMirror type, int typeIndex) {
    try {
      return Ecs.TypeIndexOf(this.ComponentTypeOf(type)) == typeIndex;
    }
    catch (Exception ex) when (!UnitySession.IsDisconnect(ex)) {
      return false;
    }
  }

  /// <summary>
  /// Whether the component is enabled on the entity, null when the question does not apply: the
  /// type is not enableable, the target cannot answer (<paramref name="isEnabled" /> null), or the
  /// kind stores its data outside the entity, where the enabled bit the accessor indexes is not the
  /// entity's to read.
  /// The probed member is INVOKED rather than re-reached by name, so a target exposing the fact as
  /// a method instead of a property is answered rather than thrown at.
  /// </summary>
  private bool? EnabledOrNull(
    StructMirror entity,
    Value componentType,
    string kind,
    MethodMirror isEnabled
  ) {
    if (isEnabled is null || kind is "shared" or "chunk") {
      return null;
    }

    var state = this.inv.Invoke(this.EntityManager, isEnabled, entity, componentType);

    return (bool) ((PrimitiveValue) state).Value;
  }

  private MethodMirror fullNameGetter;

  /// <summary>
  /// The component's fully-qualified name, read off the managed type it wraps, null when the target
  /// cannot name it -- one component that answers no name costs its own entry's name, never the
  /// whole listing.
  /// The debug-name paths are unusable on a build that strips the TypeManager name table:
  /// ComponentType.ToString() answers null there, and EntityManager.Debug.GetEntityInfo answers
  /// ComponentTypeInArchetype placeholders.
  /// </summary>
  private string ManagedTypeName(Value componentType) {
    // A debuggee null arrives as a PrimitiveValue, not as a mirror, so the type test IS the guard.
    if (this.inv.Invoke(componentType, "GetManagedType") is not ObjectMirror managed) {
      return null;
    }

    // Each component answers its OWN Type mirror, and reading a property off a fresh mirror spends
    // a round trip learning its type first. They all share one concrete runtime class, so the
    // getter resolved off the first serves every later one for free.
    this.fullNameGetter ??= this.inv.FindMethod(managed.Type, "get_FullName", 0);

    return (this.inv.Invoke(managed, this.fullNameGetter) as StringMirror)?.Value;
  }

  /// <summary>
  /// Names how the component is stored by asking the game one property at a time, the fallback for
  /// a target that does not expose the masks <see cref="TypeIndexFlags" /> decodes.
  /// Correct and slower: it walks the same ladder, in the same order, over the same facts.
  /// </summary>
  private string InvokedKindOf(Value componentType) =>
    this.Flag(componentType, "IsBuffer")
      ? "buffer"
      : this.Flag(componentType, "IsSharedComponent")
        ? "shared"
        : this.Flag(componentType, "IsChunkComponent")
          ? "chunk"
          : this.Flag(componentType, "IsManagedComponent")
            ? "managed"
            : this.Flag(componentType, "IsZeroSized")
              ? "tag"
              : "component";

  private bool Flag(Value componentType, string name) =>
    (bool) ((PrimitiveValue) this.inv.GetProperty(componentType, name)).Value;

  private bool Flag(Value componentType, MethodMirror getter) =>
    (bool) ((PrimitiveValue) this.inv.Invoke(componentType, getter)).Value;

  /// <summary>
  /// A component type's index, read off the mirror the wire already carried, null when this target
  /// holds it in a shape this cannot read.
  /// Entities versions disagree on that shape -- a bare int on one, a single-field struct wrapping
  /// one on another -- so both are accepted and anything else falls back to asking the game.
  /// </summary>
  private static int? TypeIndexOf(Value componentType) {
    return Ecs.FieldOrNull(componentType, "TypeIndex") switch {
      PrimitiveValue { Value: int bare } => bare,
      StructMirror wrapper => Ecs.FieldOrNull(wrapper, "Value") is PrimitiveValue { Value: int v }
        ? v
        : null,
      _ => null
    };
  }

  /// <summary>
  /// An instance field off a struct mirror, null when the type declares no such field: the mirror's
  /// own indexer throws instead, and a shape this does not recognize is a fallback rather than a
  /// failure.
  /// </summary>
  private static Value FieldOrNull(Value mirror, string name) {
    return mirror is StructMirror s && s.Type.GetField(name) is { IsStatic: false }
      ? s[name]
      : null;
  }

  /// <summary>
  /// Chases an Entity-typed field of one of the entity's components to the entity it names, which
  /// is how state that lives on a REFERENCED entity is reached from the one a caller holds.
  /// <paramref name="spec" /> is <c>componentTypeFullName[:field]</c>, the shape
  /// <c>system:method</c> already takes elsewhere; the field is optional exactly when the component
  /// carries a single Entity field, so the common case needs no ceremony.
  /// The caller names the component, which is what keeps every game's own reference types out of
  /// this class.
  /// One level is followed and no more: that bounds the response and makes a reference cycle
  /// unreachable rather than guarded against.
  /// </summary>
  public FollowedReference Follow(StructMirror entity, string spec) {
    // A caller writes this spec by hand, so space around either half is a typo rather than a name,
    // and a half that ends up empty named nothing at all.
    var parts = spec.Split(':').Select(p => p.Trim()).ToArray();

    if (parts.Length > 2 || parts[0].Length is 0) {
      throw new InvalidOperationException(
        $"follow expects \"<componentTypeFullName>[:<field>]\", got '{spec}'"
      );
    }

    var type = this.inv.ResolveType(parts[0]);

    if (Ecs.Unfollowable(type) is {} storage) {
      throw new InvalidOperationException(
        $"follow reads a plain unmanaged component, and {type.FullName} is {storage}; chase an " +
        "Entity field on a type the listing reports under kind \"component\""
      );
    }

    // A trailing colon names no field, so it takes the single-field path below rather than
    // searching for a field named "".
    var named = parts.Length is 2 && parts[1].Length > 0 ? parts[1] : null;
    var field = Ecs.EntityField(type, named);

    // The read's own presence gate is what reports an entity that does not carry the component.
    var component = (StructMirror) this.GetComponent(entity, type);
    var target = (StructMirror) component[field.Name];

    // A field can legitimately hold Entity.Null, a reference the game has since destroyed, or an
    // index this world never covered, and whether it is live is asked in that order: Exists indexes
    // the store unchecked, so the bound comes first (see HighestEntityIndex).
    var (index, version) = Ecs.Id(target);
    var bound = this.HighestEntityIndex();
    var inStore = bound is not {} highest || (index >= 0 && index <= highest);

    if (!inStore || !this.Exists(target)) {
      var from = Ecs.Id(entity);

      throw new InvalidOperationException(
        $"{type.FullName}.{field.Name} on entity {from.Index}:{from.Version} names entity " +
        $"{index}:{version}, which is not live, so there is nothing to follow"
      );
    }

    return new FollowedReference {
      Component = type.FullName,
      Field = field.Name,
      Target = target
    };
  }

  /// <summary>
  /// How a component type is stored when the field read cannot reach it, null when it can.
  /// The presence gate cannot stand in for this check, and it must run BEFORE the read: the gate
  /// asks about a type INDEX, which a buffer element shares with a component of the same name, so
  /// it answers yes for a buffer the entity carries and the read then reinterprets the buffer's
  /// header as the component's fields -- a fabricated value rather than a failure, on a build with
  /// the collections checks compiled out (see
  /// docs/solutions/entities-api-has-no-safety-net-on-player-builds.md).
  /// A chunk component needs no case here: it IS a plain component type, and the entity carrying it
  /// as a chunk component answers the presence gate honestly with no (verified live:
  /// HasChunkComponent true, HasComponent false, on the one entity carrying it).
  /// The interfaces answer this client-side off a mirror that caches its own list, and they arrive
  /// as the TRANSITIVE closure: the runtime collects each interface's own interfaces before
  /// replying, like Type.GetInterfaces(). A marker reached only through a derived interface is
  /// therefore still seen here, in that one round trip, and the ladder holds whether the target's
  /// Entities version chains these three together.
  /// </summary>
  private static string Unfollowable(TypeMirror type) {
    var interfaces = type.GetInterfaces().Select(i => i.FullName).ToArray();

    // Storage first, in the order the kind ladder reports it, so a shared component is named
    // shared whether the game declared it a struct or a class. Being a component at all comes
    // before being a managed one: a type carrying no marker interface is the caller's own mistake,
    // not a kind.
    return interfaces.Contains("Unity.Entities.IBufferElementData")
      ? "a buffer element"
      : interfaces.Contains("Unity.Entities.ISharedComponentData")
        ? "shared"
        : !interfaces.Contains("Unity.Entities.IComponentData")
          ? "not a component type"
          : type.IsValueType
            ? null
            : "managed";
  }

  /// <summary>
  /// Picks the Entity-typed field to chase: the one named, or the component's single one when the
  /// caller named none.
  /// Every refusal ends with what could have been named instead, in the style
  /// <see cref="RequireField" /> sets, because the component's shape is exactly what a caller
  /// naming it from the outside cannot see.
  /// </summary>
  private static FieldInfoMirror EntityField(TypeMirror type, string name) {
    var candidates = Invoker.InstanceFields(type)
      .Where(f => f.FieldType.FullName is "Unity.Entities.Entity")
      .ToArray();

    var choices = candidates.Length is 0
      ? $"the component carries no Entity-typed field (fields: {Invoker.InstanceFieldNames(type)})"
      : $"Entity fields: {string.Join(", ", candidates.Select(f => f.Name))}";

    if (name is not null) {
      var named = Ecs.RequireField(type, name);

      return Array.Exists(candidates, f => f.Name == named.Name)
        ? named
        : throw new InvalidOperationException(
          $"field '{named.Name}' on {type.FullName} is a {named.FieldType.FullName}, not an " +
          $"Entity; {choices}"
        );
    }

    return candidates.Length switch {
      1 => candidates[0],
      0 => throw new InvalidOperationException($"{type.FullName} cannot be followed: {choices}"),
      _ => throw new InvalidOperationException(
        $"{type.FullName} carries several Entity-typed fields, so follow must name one as " +
        $"\"{type.FullName}:<field>\"; {choices}"
      )
    };
  }

  /// <summary>
  /// Builds an Entity value entirely client-side: a zeroed mirror of the type with its two fields
  /// written, serialized from the client copy on send, so naming an entity asks the game nothing.
  /// The fields are written BY NAME. A positional build would bake in the declaration order the
  /// mirror's value array happens to have, and a future reordering would then swap index and
  /// version silently -- a read of the wrong entity rather than an error.
  /// Static because it needs no world, only the invoker.
  /// </summary>
  public static StructMirror MakeEntity(Invoker inv, int index, int version) {
    var entityType = inv.ResolveType("Unity.Entities.Entity");

    var entity = (StructMirror) inv.DefaultMirrorFor(entityType);

    entity["Index"] = inv.Prim(index);
    entity["Version"] = inv.Prim(version);

    return entity;
  }

  public StructMirror MakeEntity(int index, int version) =>
    Ecs.MakeEntity(this.inv, index, version);

  /// <summary>Whether the entity (index AND version) is live in this world.</summary>
  public bool Exists(StructMirror entity) =>
    (bool) ((PrimitiveValue) this.inv.Invoke(this.EntityManager, "Exists", entity)).Value;

  /// <summary>Fetches a managed system instance from the world by type name.</summary>
  public Value GetSystem(string systemTypeFullName) {
    var sysType = this.inv.ResolveType(systemTypeFullName);

    var worldType = this.inv.TypeOf(this.World);

    return this.inv.Invoke(
      this.World,
      this.inv.FindMethod(worldType, "GetExistingSystemManaged", 1, paramTypes: ["Type"]),
      this.inv.TypeObject(sysType)
    );
  }

  /// <summary>
  /// Fetches an entity's DynamicBuffer&lt;T&gt; mirror, at the narrowest access the caller needs:
  /// write access is what turns an accessor mistake fatal, so only a path that mutates asks for it.
  /// </summary>
  public Value GetBuffer(StructMirror entity, TypeMirror elementType, bool isReadOnly) {
    this.RequirePresence(entity, elementType, "HasBuffer", "buffer");

    return this.inv.Invoke(
      this.EntityManager,
      this.Accessor("GetBuffer", 2, elementType),
      entity,
      this.inv.Prim(isReadOnly)
    );
  }

  public int BufferLength(Value buffer) =>
    (int) ((PrimitiveValue) this.inv.GetProperty(buffer, "Length")).Value;

  /// <summary>
  /// Finds an instance field by name (case-insensitive); throws with the field list when absent.
  /// </summary>
  public static FieldInfoMirror RequireField(TypeMirror type, string name) {
    return Invoker.InstanceFields(type)
        .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) ??
      throw new InvalidOperationException(
        $"field '{name}' not found on {type.FullName}; " +
        $"fields: {Invoker.InstanceFieldNames(type)}"
      );
  }

  /// <summary>
  /// Parses a user-supplied string into a mirrored value for the given field type.
  /// </summary>
  public Value ParseFieldValue(TypeMirror fieldType, string raw) =>
    Ecs.CoerceArg(this.inv, fieldType, raw, () => this.EntityManager);

  /// <summary>
  /// Coerces a raw text token to a target (parameter or field) type, so callers never guess a
  /// token's type: the resolved signature is the truth.
  /// <c>em</c> materializes an EntityManager parameter through <paramref name="entityManager" />;
  /// <c>index[:version]</c> builds an Entity (version defaults to 1); <c>out-int</c> /
  /// <c>out-entity</c> are out-param placeholders; enums parse by numeric value; string parameters
  /// take the raw text; out/ref parameters coerce to their element type.
  /// Throws when the token does not parse as, or the type is not expressible over, SDB mirrors.
  /// </summary>
  public static Value CoerceArg(
    Invoker inv,
    TypeMirror targetType,
    string raw,
    Func<Value> entityManager
  ) {
    // Out/ref parameter types surface as "<element>&"; the value sent is the element's.
    var target = targetType.IsByRef ? targetType.GetElementType() : targetType;
    var typeName = target.FullName;

    switch (raw) {
      case "em" when typeName is "Unity.Entities.EntityManager": return entityManager();

      // An out slot only has to be a well-shaped value of the right type for the call to write
      // over, which is exactly what the client-side default is.
      case "out-entity" when typeName is "Unity.Entities.Entity":
      case "out-int" when typeName is "System.Int32":
        return inv.DefaultMirrorFor(target);
    }

    switch (typeName) {
      case "Unity.Entities.Entity":
        var (index, version) = Ecs.ParseEntitySpec(raw);

        return Ecs.MakeEntity(inv, index, version ?? 1);

      case "System.String": return inv.Str(raw);
    }

    if (target.IsEnum) {
      // Parsed AS the enum's underlying type, which does both jobs at once: the wire value matches
      // its width, since a byte-backed enum sent as an Int32 is refused outright, and a token that
      // does not fit is rejected rather than truncated into a different member of the same enum.
      return inv.MakeEnum(
        target,
        Convert.ChangeType(
          raw,
          Invoker.ClrPrimitive(target.EnumUnderlyingType.FullName),
          CultureInfo.InvariantCulture
        )
      );
    }

    object parsed = typeName switch {
      "System.Int32" => int.Parse(raw, CultureInfo.InvariantCulture),
      "System.UInt32" => uint.Parse(raw, CultureInfo.InvariantCulture),
      "System.Int64" => long.Parse(raw, CultureInfo.InvariantCulture),
      "System.UInt64" => ulong.Parse(raw, CultureInfo.InvariantCulture),
      "System.Int16" => short.Parse(raw, CultureInfo.InvariantCulture),
      "System.UInt16" => ushort.Parse(raw, CultureInfo.InvariantCulture),
      "System.Byte" => byte.Parse(raw, CultureInfo.InvariantCulture),
      "System.SByte" => sbyte.Parse(raw, CultureInfo.InvariantCulture),
      "System.Single" => float.Parse(raw, CultureInfo.InvariantCulture),
      "System.Double" => double.Parse(raw, CultureInfo.InvariantCulture),
      "System.Boolean" => bool.Parse(raw),
      _ => throw new InvalidOperationException(
        $"unsupported target type {typeName} (primitives, enums, strings, Entity, and the " +
        "EntityManager only)"
      )
    };

    return inv.Prim(parsed);
  }

  /// <summary>Parses an <c>index[:version]</c> entity spec.</summary>
  public static (int Index, int? Version) ParseEntitySpec(string spec) {
    var parts = spec.Split(':');

    return (int.Parse(parts[0], CultureInfo.InvariantCulture),
      parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : null);
  }
}

/// <summary>
/// One component type an entity carries, as listed by <see cref="Ecs.ListComponents"/>.
/// </summary>
public sealed class EntityComponentInfo {
  /// <summary>
  /// The component's fully-qualified managed type name, null when the target cannot name it.
  /// </summary>
  public string Name { get; init; }

  /// <summary>
  /// How the component is stored, which is what decides how its value can be reached: one of
  /// "component", "tag", "buffer", "shared", "chunk", "managed".
  /// </summary>
  public string Kind { get; init; }

  /// <summary>
  /// Whether the component is currently enabled; null when the type is not enableable, when its
  /// kind stores the data outside the entity, or when the target cannot report the state at all
  /// (see <see cref="EntityComponents.EnabledStateNote"/>).
  /// A disabled component is still carried by the entity, and still answers a presence check,
  /// while the simulation ignores it.
  /// </summary>
  public bool? Enabled { get; init; }

  /// <summary>
  /// The component's field values, null when the caller did not ask for values or when the read
  /// failed (see <see cref="ValueError" />).
  /// A kind whose data no accessor here can reach carries its kind instead of a value.
  /// </summary>
  public string Value { get; init; }

  /// <summary>Why this entry carries no value, null when it carries one.</summary>
  public string ValueError { get; init; }
}

/// <summary>
/// Where <see cref="Ecs.Follow" /> landed, and the reference that led there: the provenance is part
/// of the answer, since a second archetype means nothing without what pointed at it.
/// </summary>
public sealed class FollowedReference {
  /// <summary>The entity the chased field named, live as of the follow.</summary>
  public StructMirror Target { get; init; }

  /// <summary>The resolved full name of the component the chased field belongs to.</summary>
  public string Component { get; init; }

  /// <summary>The chased field's own name, as the component declares it.</summary>
  public string Field { get; init; }
}

/// <summary>An entity's whole archetype, as reported by <see cref="Ecs.ListComponents"/>.</summary>
public sealed class EntityComponents {
  public IReadOnlyList<EntityComponentInfo> Components { get; init; }

  /// <summary>
  /// Why no entry carries an enabled state, null when the target reports it.
  /// Absence of the state must never be read as "nothing is disabled", so it is said out loud.
  /// </summary>
  public string EnabledStateNote { get; init; }
}
