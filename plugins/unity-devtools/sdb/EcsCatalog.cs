using System;
using System.Collections.Generic;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>
/// What the ECS layer may remember for a whole attach, in one place so that the lifetime and the
/// invalidation of every ECS memo are stated together rather than scattered through the plumbing.
/// Two kinds live here, and they are remembered for different reasons.
/// The flag masks and the component-type descriptions are properties of loaded code, which Mono
/// cannot unload, so they simply cannot go stale before the attach ends.
/// A world handle CAN, and it is a raw pointer into the entity store, so it is revalidated on every
/// operation instead (see <see cref="WorldFor" />).
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

  private readonly Dictionary<string, EcsWorld> worlds = [];

  /// <summary>
  /// Stands in for the default world, which no name selects.
  /// </summary>
  private const string DefaultWorldKey = "\0default";

  /// <summary>
  /// The world a name selects, with everything reaching it needs, rebuilt whenever the cached one
  /// no longer stands.
  /// Revalidating rather than trusting the cache is what makes caching a world safe at all: the
  /// EntityManager is a single raw pointer into the entity store, and on a build with the
  /// collections safety checks compiled out, a stale one is a dangling write rather than an error.
  /// Selection and revalidation live together here because they are one rule: what revalidates a
  /// cached world is whether SELECTION would still land on it, and only the selector knows that
  /// (see <see cref="EcsWorld.Selection" />).
  /// </summary>
  public EcsWorld WorldFor(string name) {
    var key = name ?? EcsCatalog.DefaultWorldKey;

    if (this.worlds.TryGetValue(key, out var cached) && this.StillStands(cached)) {
      return cached;
    }

    var built = this.BuildWorld(name);

    this.worlds[key] = built;

    return built;
  }

  private bool StillStands(EcsWorld cached) {
    try {
      return cached.Selection switch {
        // Two different questions, and the default world needs both. Identity asks whether
        // selection would still land here: a reassigned default answers a different mirror (one
        // mirror per object), while the previous world stays alive and created, so liveness cannot
        // stand in for it. Liveness asks whether what it lands on is still there: a world disposed
        // without the static being cleared keeps answering the same mirror, so identity cannot
        // stand in either. Whichever fails, the handle is rebuilt.
        WorldSelection.DefaultInjection =>
          ReferenceEquals(this.DefaultInjectionWorldOrNull(), cached.World) &&
          this.IsCreated(cached.World),

        WorldSelection.Named => this.IsCreated(cached.World),

        // Selected by scanning because the target named no default world. Whether the scan would
        // still land here depends on a default having appeared since, so there is nothing cheaper
        // to ask than the scan itself: this one is deliberately re-selected every operation.
        _ => false
      };
    }
    catch (Exception ex) when (!UnitySession.IsDisconnect(ex)) {
      // A world that cannot answer whether it still stands is not one to keep using: rebuilding
      // re-runs selection, which either finds a live world or fails with a message about the world
      // rather than about the revalidation.
      return false;
    }
  }

  private EcsWorld BuildWorld(string name) {
    var (world, selectedName, selection) = this.PickWorld(name);
    var entityManager = inv.GetProperty(world, "EntityManager");

    return new EcsWorld {
      World = world,

      // Selection reads a name only where it has to match one, so the default world is still asked
      // for its own.
      Name = selectedName ?? ((StringMirror) inv.GetProperty(world, "Name")).Value,
      EntityManager = entityManager,
      EntityManagerType = inv.TypeOf(entityManager),
      Selection = selection
    };
  }

  /// <summary>
  /// Picks the world a name selects, along with the name it read to select it -- null when it
  /// selected without reading one, which the default world's early exit does.
  /// </summary>
  private (Value World, string Name, WorldSelection Selection) PickWorld(string name) {
    if (name is null && this.DefaultInjectionWorldOrNull() is {} injected) {
      return (injected, null, WorldSelection.DefaultInjection);
    }

    // World.All is a boxing-hostile struct collection; enumerate via Count + indexer.
    var all = inv.GetStaticProperty(this.WorldType(), "All");
    var count = (int) ((PrimitiveValue) inv.GetProperty(all, "Count")).Value;
    var names = new List<string>();

    for (var i = 0; i < count; i++) {
      var world = inv.Invoke(all, "get_Item", inv.Prim(i));
      var worldName = ((StringMirror) inv.GetProperty(world, "Name")).Value;

      if (name is null) {
        return (world, worldName, WorldSelection.DefaultScan);
      }

      if (worldName == name) {
        return (world, worldName, WorldSelection.Named);
      }

      names.Add(worldName);
    }

    throw new InvalidOperationException(
      name is null
        ? "no ECS worlds are live"
        : $"world '{name}' not found; live worlds: {string.Join(", ", names)}"
    );
  }

  /// <summary>
  /// Whether the world is still created, false when the target cannot say -- which rebuilds the
  /// handle rather than trusting one whose liveness nothing established.
  /// </summary>
  private bool IsCreated(Value world) {
    var getter = inv.FindMethodOrNull(inv.TypeOf(world), "get_IsCreated", 0);

    return getter is not null && (bool) ((PrimitiveValue) inv.Invoke(world, getter)).Value;
  }

  /// <summary>The game's main world when it sets one, null when it does not.</summary>
  private ObjectMirror DefaultInjectionWorldOrNull() =>
    inv.GetStaticProperty(this.WorldType(), "DefaultGameObjectInjectionWorld") as ObjectMirror;

  private TypeMirror WorldType() => inv.ResolveType("Unity.Entities.World");
}

/// <summary>How a cached world was selected, which is what decides how it is revalidated.</summary>
public enum WorldSelection {
  /// <summary>The default injection world, revalidated by mirror identity.</summary>
  DefaultInjection,

  /// <summary>Named by the caller and found by scanning, revalidated by liveness.</summary>
  Named,

  /// <summary>
  /// The first live world, taken because the target named no default: not revalidatable short of
  /// re-selecting, so it is never served from cache.
  /// </summary>
  DefaultScan
}

/// <summary>
/// One world and the handles every ECS operation against it needs, cached together because they
/// are read together and revalidated together.
/// </summary>
public sealed record EcsWorld {
  public required Value World { get; init; }

  public required string Name { get; init; }

  public required Value EntityManager { get; init; }

  public required TypeMirror EntityManagerType { get; init; }

  public required WorldSelection Selection { get; init; }
}

/// <summary>
/// What one component type index means: how the type is stored, and what it is called.
/// Both are properties of loaded code, so they hold for the attach.
/// Whether the type is ENABLEABLE is deliberately absent: the answer depends on what this target
/// could be asked at the time, which is not a property of the type, so caching it would let one
/// degraded operation speak for every later one.
/// </summary>
public sealed record EcsComponentType {
  /// <inheritdoc cref="EntityComponentInfo.Kind" />
  public required string Kind { get; init; }

  /// <inheritdoc cref="EntityComponentInfo.Name" />
  public required string Name { get; init; }

  /// <summary>
  /// The index this describes, null when the target holds it in a shape the client cannot read --
  /// which is also what makes the description unmemoizable, since the index is the key.
  /// </summary>
  public required int? TypeIndex { get; init; }
}
