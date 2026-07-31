using System;
using System.Collections.Generic;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>
/// What the ECS layer may remember for a whole attach, in one place so that the lifetime of every
/// ECS memo is stated together rather than scattered through the plumbing.
/// Everything held here is a property of loaded code or of a debuggee object's own identity --
/// neither of which Mono can take back before the attach ends -- so nothing here can go stale.
/// A world's HANDLES are the deliberate exception and are not kept: <see cref="WorldFor" /> selects
/// them afresh per operation, since an EntityManager held across operations is a raw pointer into
/// the entity store.
/// One catalog belongs to one attach and dies with it; nothing here needs a lock, because the ECS
/// tools reach it only through a session operation and those run one at a time.
/// </summary>
public sealed class EcsCatalog(Invoker inv) {
  private bool flagsRead;

  /// <summary>
  /// The masks that decode a component type's storage kind, null when this target does not expose
  /// them all -- the caller then asks the game per property instead.
  /// Read once per attach, in ONE batched static-field read.
  /// </summary>
  public TypeIndexFlags Flags {
    get {
      if (this.flagsRead) {
        return field;
      }

      // A read that THREW describes the moment, so it is answered but not latched: the next
      // operation asks again rather than inheriting a fallback nothing about the target justified.
      // A target that simply does not declare the constants IS latched, since loaded code cannot
      // lose them.
      try {
        field = this.ReadFlags();
      }
      catch (Exception ex) when (!UnitySession.IsDisconnect(ex)) {
        return null;
      }

      this.flagsRead = true;

      return field;
    }
  }

  private TypeIndexFlags ReadFlags() {
    // A type that does not resolve YET is a moment, not a target property -- the debuggee loads
    // assemblies over time -- so it is refused the same way a throw is, without latching.
    var typeManager = inv.FindTypeOrNull("Unity.Entities.TypeManager") ??
      throw new InvalidOperationException("Unity.Entities.TypeManager does not resolve yet");

    var read = inv.StaticFieldValues(typeManager, TypeIndexFlags.ConstantNames);
    var constants = new Dictionary<string, int>();

    foreach (var (name, value) in read) {
      if (value is PrimitiveValue { Value: int mask }) {
        constants[name] = mask;
      }
    }

    return TypeIndexFlags.FromConstants(constants);
  }

  private readonly Dictionary<int, EcsComponentType> componentTypes = [];

  /// <summary>
  /// What a component type index already meant to this attach, so a second entity listing spends
  /// nothing naming the types the first one already named.
  /// The key is the RAW index, flags and all: a chunk component's index differs from its plain
  /// form's by exactly those bits, and that is the distinction this memo must preserve.
  /// </summary>
  public bool TryDescribe(int typeIndex, out EcsComponentType known) =>
    this.componentTypes.TryGetValue(typeIndex, out known);

  /// <summary>
  /// Keeps a description for the rest of the attach, under the index it describes.
  /// </summary>
  public void Remember(int typeIndex, EcsComponentType described) =>
    this.componentTypes[typeIndex] = described;

  private readonly Dictionary<int, TypeMirror> provenTypes = [];

  /// <summary>
  /// The type mirror already proven to be the one a component type index names.
  /// Only proofs live here. A refusal is not a property of the index -- the debuggee loads
  /// assemblies over time, so a name that answers nothing now may answer later -- and is left for
  /// the next call to re-ask.
  /// </summary>
  public bool TryProvenType(int typeIndex, out TypeMirror proven) =>
    this.provenTypes.TryGetValue(typeIndex, out proven);

  /// <summary>Settles identity for an index for the rest of the attach.</summary>
  public void SettleIdentity(int typeIndex, TypeMirror proven) =>
    this.provenTypes[typeIndex] = proven;

  private readonly Dictionary<TypeMirror, StructMirror> componentTypeStructs = [];

  /// <summary>
  /// The game's own ComponentType already built for a type mirror, which every Entities API taking
  /// one is named through.
  /// It is a property of loaded code, and one struct mirror serves any number of invokes: its
  /// fields are re-serialized on each send, so reusing it cannot leak state between calls.
  /// </summary>
  public bool TryComponentType(TypeMirror type, out StructMirror built) =>
    this.componentTypeStructs.TryGetValue(type, out built);

  /// <summary>Keeps a built ComponentType for the rest of the attach.</summary>
  public void RememberComponentType(TypeMirror type, StructMirror built) =>
    this.componentTypeStructs[type] = built;

  /// <summary>
  /// The world a name selects, with everything reaching it needs, selected afresh every operation.
  /// Re-selecting is what keeps the EntityManager out of this catalog: it is a single raw pointer
  /// into the entity store, and on a build with the collections safety checks compiled out, one
  /// held past the moment it was fetched is a dangling write rather than an error.
  /// It subsumes the checks a cache needed, because those only ever decided whether to REBUILD, and
  /// the rebuild re-ran this same selection: what selection lands on is what the caller gets.
  /// What selection cannot answer is a world the target has DISPOSED while its default static still
  /// points at it -- that handle is returned unchecked, and nothing here can tell. A named world
  /// cannot hit that, having left <c>World.All</c>: the name fails with the live-world list.
  /// </summary>
  public EcsWorld WorldFor(string name) {
    var (world, selectedName) = this.PickWorld(name);
    var entityManager = inv.GetProperty(world, "EntityManager");

    return new EcsWorld {
      World = world,
      Name = selectedName,
      EntityManager = entityManager,
      EntityManagerType = inv.TypeOf(entityManager)
    };
  }

  /// <summary>Picks the world a name selects, along with its own name.</summary>
  private (Value World, string Name) PickWorld(string name) {
    if (name is null && this.DefaultInjectionWorldOrNull() is {} injected) {
      return (injected, this.NameOf(injected));
    }

    // World.All is a boxing-hostile struct collection; enumerate via Count + indexer.
    var all = inv.GetStaticProperty(this.WorldType(), "All");
    var count = (int) ((PrimitiveValue) inv.GetProperty(all, "Count")).Value;
    var names = new List<string>();

    for (var i = 0; i < count; i++) {
      var world = inv.Invoke(all, "get_Item", inv.Prim(i));
      var worldName = this.NameOf(world);

      if (name is null || worldName == name) {
        return (world, worldName);
      }

      names.Add(worldName);
    }

    throw new InvalidOperationException(
      name is null
        ? "no ECS worlds are live"
        : $"world '{name}' not found; live worlds: {string.Join(", ", names)}"
    );
  }

  private readonly Dictionary<Value, string> worldNames = [];

  /// <summary>
  /// A world's own name, remembered for the attach under the mirror that answers it: a world is
  /// named at construction and keeps that name, and one debuggee object answers one mirror, so the
  /// key is an identity the client compares without touching the wire.
  /// Every site wanting a world's name comes through here, which is what makes the scan above pay
  /// for a name once per world rather than once per operation.
  /// </summary>
  private string NameOf(Value world) {
    if (this.worldNames.TryGetValue(world, out var known)) {
      return known;
    }

    var name = ((StringMirror) inv.GetProperty(world, "Name")).Value;

    this.worldNames[world] = name;

    return name;
  }

  /// <summary>The game's main world when it sets one, null when it does not.</summary>
  private ObjectMirror DefaultInjectionWorldOrNull() =>
    inv.GetStaticProperty(this.WorldType(), "DefaultGameObjectInjectionWorld") as ObjectMirror;

  private TypeMirror WorldType() => inv.ResolveType("Unity.Entities.World");
}

/// <summary>
/// One world and the handles every ECS operation against it needs, read together and living exactly
/// as long as the operation that selected them.
/// </summary>
public sealed record EcsWorld {
  public required Value World { get; init; }

  public required string Name { get; init; }

  public required Value EntityManager { get; init; }

  public required TypeMirror EntityManagerType { get; init; }
}

/// <summary>
/// What one component type index means: how the type is stored, and what it is called.
/// Both are properties of loaded code, so they hold for the attach.
/// Whether the type is ENABLEABLE is deliberately absent: the answer depends on what this target
/// could be asked at the time, which is not a property of the type, so caching it would let one
/// degraded operation speak for every later one.
/// </summary>
public sealed record EcsComponentType {
  /// <summary>How the type is stored, which is what decides how its value can be reached.</summary>
  public required EcsKind Kind { get; init; }

  /// <inheritdoc cref="EntityComponentInfo.Name" />
  public required string Name { get; init; }

  /// <summary>
  /// The index this describes, null when the target holds it in a shape the client cannot read --
  /// which is also what makes the description unmemoizable, since the index is the key.
  /// </summary>
  public required int? TypeIndex { get; init; }
}
