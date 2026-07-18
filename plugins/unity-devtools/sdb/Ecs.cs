using System.Globalization;
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

      if (name == null || worldName == name) {
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
  /// Finds one entity by Index (and Version when given) among the query's matches.
  /// </summary>
  public StructMirror FindEntity(Value query, int index, int? version) {
    var arr = this.EntityArray(query);

    const int chunk = 4096;

    for (var offset = 0; offset < arr.Length; offset += chunk) {
      var slice = arr.GetValues(offset, Math.Min(chunk, arr.Length - offset));

      foreach (var v in slice) {
        var e = (StructMirror) v;

        if ((int) ((PrimitiveValue) e["Index"]).Value == index &&
          (version == null || (int) ((PrimitiveValue) e["Version"]).Value == version)) {
          return e;
        }
      }
    }

    throw new InvalidOperationException(
      $"entity {index}{(version != null ? $":{version}" : "")} not found among " +
      $"{arr.Length} entities matching the query"
    );
  }

  public Value GetComponent(StructMirror entity, TypeMirror componentType) {
    var method = this.inv.FindMethod(this.EntityManagerType, "GetComponentData", 1, 1, ["Entity"])
      .MakeGenericMethod([componentType]);

    return this.inv.Invoke(this.EntityManager, method, entity);
  }

  public void SetComponent(StructMirror entity, TypeMirror componentType, StructMirror value) {
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
        $"fields: {string.Join(", ", Invoker.InstanceFields(type).Select(f => f.Name))}"
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
      case "em" when typeName == "Unity.Entities.EntityManager":
        return entityManager();
      case "out-entity" when typeName == "Unity.Entities.Entity":
        return Ecs.MakeEntity(inv, 0, 0);
      case "out-int" when typeName == "System.Int32":
        return inv.Prim(0);
    }

    switch (typeName) {
      case "Unity.Entities.Entity":
        var (index, version) = Ecs.ParseEntitySpec(raw);

        return Ecs.MakeEntity(inv, index, version ?? 1);
      case "System.String":
        return inv.Str(raw);
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
