using System.Globalization;
using Mono.Debugger.Soft;
using UnityDevtools.Sdb;

namespace UnityDevtools.Poc;

/// <summary>
/// ECS operations over SDB invokes: world selection, EntityManager access, entity queries, and
/// component read/write.
/// Component access goes through the generic EntityManager.Get/SetComponentData&lt;T&gt;
/// instantiated live via MakeGenericMethod (protocol 2.24+).
/// </summary>
internal sealed class Ecs {
  private readonly Invoker inv;

  public Ecs(Invoker inv, string worldName) {
    this.inv = inv;
    this.World = this.PickWorld(worldName);

    Console.WriteLine($"world: {inv.Format(inv.GetProperty(this.World, "Name"))}");

    this.EntityManager = inv.GetProperty(this.World, "EntityManager");
    this.EntityManagerType = inv.TypeOf(this.EntityManager);
  }

  public Value World { get; }

  public Value EntityManager { get; }

  public TypeMirror EntityManagerType { get; }

  private Value PickWorld(string name) {
    var worldType = this.inv.ResolveType("Unity.Entities.World");

    if (name == null) {
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
      name == null
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
  ///   Materializes the query's entities as a managed Entity[] in the debuggee
  ///   (ToEntityArray with the Temp allocator, then NativeArray.ToArray) and returns
  ///   its mirror. Temp allocations are frame-scoped; no Dispose needed.
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
  ///   Finds one entity by Index (and Version when given) among the query's matches.
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
  /// </summary>
  public StructMirror MakeEntity(int index, int version) {
    var entityType = this.inv.ResolveType("Unity.Entities.Entity");

    var entity = (StructMirror) this.inv.GetStaticProperty(entityType, "Null");

    entity["Index"] = this.inv.Prim(index);
    entity["Version"] = this.inv.Prim(version);

    return entity;
  }

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

  /// <summary>Parses a CLI string into a mirrored value for the given field type.</summary>
  public Value ParseFieldValue(TypeMirror fieldType, string raw) {
    if (fieldType.FullName == "Unity.Entities.Entity") {
      var parts = raw.Split(':');

      return this.MakeEntity(
        int.Parse(parts[0], CultureInfo.InvariantCulture),
        parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 1
      );
    }

    if (fieldType.IsEnum) {
      return this.inv.Vm.CreateEnumMirror(
        fieldType,
        this.inv.Prim(int.Parse(raw, CultureInfo.InvariantCulture))
      );
    }

    object parsed = fieldType.FullName switch {
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
        $"unsupported field type {fieldType.FullName} (primitives and enums only)"
      )
    };

    return this.inv.Prim(parsed);
  }
}
