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
/// instantiated live via MakeGenericMethod (protocol 2.24+).
/// Instances are only valid while the VM stays suspended and connected; build one per operation.
/// </summary>
public sealed class Ecs {
  private readonly Invoker inv;

  public Ecs(Invoker inv, string worldName = null) {
    this.inv = inv;
    this.World = this.PickWorld(worldName);
    this.WorldName = ((StringMirror) inv.GetProperty(this.World, "Name")).Value;
    this.EntityManager = inv.GetProperty(this.World, "EntityManager");
    this.EntityManagerType = inv.TypeOf(this.EntityManager);
  }

  public Value World { get; }

  public string WorldName { get; }

  public Value EntityManager { get; }

  public TypeMirror EntityManagerType { get; }

  private Value PickWorld(string name) {
    var worldType = this.inv.ResolveType("Unity.Entities.World");

    if (name is null) {
      // The default injection world is the game's main world when set.
      var def = this.inv.GetStaticProperty(worldType, "DefaultGameObjectInjectionWorld");

      if (def is ObjectMirror) {
        return def;
      }
    }

    // World.All is a boxing-hostile struct collection; enumerate via Count + indexer.
    var all = this.inv.GetStaticProperty(worldType, "All");
    var count = (int) ((PrimitiveValue) this.inv.GetProperty(all, "Count")).Value;
    var names = new List<string>();

    for (var i = 0; i < count; i++) {
      var world = this.inv.Invoke(all, "get_Item", this.inv.Prim(i));
      var worldName = ((StringMirror) this.inv.GetProperty(world, "Name")).Value;

      if (name is null || worldName == name) {
        return world;
      }

      names.Add(worldName);
    }

    throw new InvalidOperationException(
      name is null
        ? "no ECS worlds are live"
        : $"world '{name}' not found; live worlds: {string.Join(", ", names)}"
    );
  }

  /// <summary>Builds an EntityQuery requiring all the given component types (ReadWrite).</summary>
  public Value CreateQuery(TypeMirror[] componentTypes) {
    var ctType = this.inv.ResolveType("Unity.Entities.ComponentType");

    var cts = componentTypes.Select(t => this.inv.InvokeStatic(
          ctType,
          this.inv.FindMethod(ctType, "ReadWrite", 1, paramTypes: ["Type"]),
          t.GetTypeObject()
        )
      )
      .ToArray();

    // ComponentType[] built debuggee-side via Array.CreateInstance + SetValues.
    var arrayType = this.inv.ResolveType("System.Array");

    var arr = (ArrayMirror) this.inv.InvokeStatic(
      arrayType,
      this.inv.FindMethod(arrayType, "CreateInstance", 2, paramTypes: ["Type", "Int32"]),
      ctType.GetTypeObject(),
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
  /// Temp allocations are frame-scoped; no Dispose needed.
  /// </summary>
  public ArrayMirror EntityArray(Value query) {
    var allocatorType = this.inv.ResolveType("Unity.Collections.Allocator");

    var temp = this.inv.Vm.CreateEnumMirror(allocatorType, this.inv.Prim(2));

    var handleType = this.inv.ResolveType("Unity.Collections.AllocatorManager+AllocatorHandle");

    var handle = this.inv.InvokeStatic(
      handleType,
      this.inv.FindMethod(handleType, "op_Implicit", 1, paramTypes: ["Allocator"]),
      temp
    );

    var native = this.inv.Invoke(query, "ToEntityArray", handle);

    return (ArrayMirror) this.inv.Invoke(native, "ToArray");
  }

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

    // The entity store is indexed UNCHECKED on both paths below: out of range, the by-index lookup
    // and Exists alike read foreign memory, and far enough out both fault the game with an in-game
    // NullReferenceException (verified live on both). HighestEntityIndex bounds them, inclusively.
    var highestIndex = this.FindMember("HighestEntityIndex", 0);

    if (highestIndex is not null) {
      var highest =
        (int) ((PrimitiveValue) this.inv.Invoke(this.EntityManager, highestIndex)).Value;

      if (index < 0 || index > highest) {
        throw new InvalidOperationException(
          $"entity index {index} is out of range for world '{this.WorldName}' (valid: 0-{highest})"
        );
      }
    }

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

    if (byIndex is null || highestIndex is null) {
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

  /// <summary>
  /// Probes an EntityManager member, accepting a property in place of a method: the same fact is
  /// exposed either way across Entities versions, and a getter is reached under its <c>get_</c>
  /// name. Costs nothing on the wire, since a mirror caches its own member list.
  /// </summary>
  private MethodMirror FindMember(string name, int argc, string[] paramTypes = null) =>
    this.inv.FindMethodOrNull(this.EntityManagerType, name, argc, paramTypes: paramTypes) ??
    (argc is 0 ? this.inv.FindMethodOrNull(this.EntityManagerType, $"get_{name}", 0) : null);

  private readonly HashSet<(int Index, int Version, TypeMirror Type)> carried = [];

  /// <summary>
  /// Refuses a component the entity does not carry. The generic accessors derive a chunk offset
  /// from the archetype, so where the collections safety checks are compiled out they read and
  /// write memory the entity does not own: verified live, reading an absent component answered a
  /// plausible all-zero value instead of failing.
  /// A confirmed pair is remembered, because one read-modify-write asks three times and an
  /// archetype cannot change under an instance that lives inside a single suspend window.
  /// </summary>
  private void RequireComponent(StructMirror entity, TypeMirror componentType) {
    var key = (
      Index: (int) ((PrimitiveValue) entity["Index"]).Value,
      Version: (int) ((PrimitiveValue) entity["Version"]).Value,
      Type: componentType
    );

    if (this.carried.Contains(key)) {
      return;
    }

    var has = this.inv.FindMethod(this.EntityManagerType, "HasComponent", 1, 1, ["Entity"])
      .MakeGenericMethod([componentType]);

    if ((bool) ((PrimitiveValue) this.inv.Invoke(this.EntityManager, has, entity)).Value) {
      _ = this.carried.Add(key);

      return;
    }

    throw new InvalidOperationException(
      $"entity {key.Index}:{key.Version} has no {componentType.FullName} component"
    );
  }

  public Value GetComponent(StructMirror entity, TypeMirror componentType) {
    this.RequireComponent(entity, componentType);

    var method = this.inv.FindMethod(this.EntityManagerType, "GetComponentData", 1, 1, ["Entity"])
      .MakeGenericMethod([componentType]);

    return this.inv.Invoke(this.EntityManager, method, entity);
  }

  public void SetComponent(StructMirror entity, TypeMirror componentType, StructMirror value) {
    this.RequireComponent(entity, componentType);

    var method = this.inv.FindMethod(this.EntityManagerType, "SetComponentData", 2, 1, ["Entity"])
      .MakeGenericMethod([componentType]);

    this.inv.Invoke(this.EntityManager, method, entity, value);
  }

  /// <summary>
  /// Builds an Entity value client-side: clones the Entity.Null template StructMirror and
  /// overwrites Index/Version (values are serialized from the client copy on send).
  /// Static because it needs no world, only the invoker.
  /// </summary>
  public static StructMirror MakeEntity(Invoker inv, int index, int version) {
    var entityType = inv.ResolveType("Unity.Entities.Entity");

    var entity = (StructMirror) inv.GetStaticProperty(entityType, "Null");

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
      sysType.GetTypeObject()
    );
  }

  /// <summary>Fetches an entity's DynamicBuffer&lt;T&gt; mirror (read-write).</summary>
  public Value GetBuffer(StructMirror entity, TypeMirror elementType) {
    var m = this.inv.FindMethod(this.EntityManagerType, "GetBuffer", 2, 1, ["Entity"])
      .MakeGenericMethod([elementType]);

    return this.inv.Invoke(this.EntityManager, m, entity, this.inv.Prim(false));
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
    var typeName = targetType.FullName.TrimEnd('&');

    switch (raw) {
      case "em" when typeName is "Unity.Entities.EntityManager": return entityManager();

      case "out-entity" when typeName is "Unity.Entities.Entity": return Ecs.MakeEntity(inv, 0, 0);

      case "out-int" when typeName is "System.Int32": return inv.Prim(0);
    }

    switch (typeName) {
      case "Unity.Entities.Entity":
        var (index, version) = Ecs.ParseEntitySpec(raw);

        return Ecs.MakeEntity(inv, index, version ?? 1);

      case "System.String": return inv.Str(raw);
    }

    if (targetType.IsEnum) {
      return inv.Vm.CreateEnumMirror(
        targetType,
        inv.Prim(int.Parse(raw, CultureInfo.InvariantCulture))
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
