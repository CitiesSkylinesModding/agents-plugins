using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>
/// The entity query half of <see cref="Ecs" />: building one, listing what it matches, and giving
/// it back.
/// </summary>
public sealed partial class Ecs {
  /// <summary>
  /// Counts the entities carrying ALL the named component types and lists the first
  /// <paramref name="limit" /> of them.
  /// <paramref name="label" /> is <c>systemTypeFullName:method</c>, a one-Entity-arg method on a
  /// managed system that every listed entity is annotated through.
  /// The query is the game's own object and is disposed on every way out, since nothing else would
  /// ever give it back.
  /// </summary>
  public EcsQueryListing Query(IReadOnlyList<string> components, int limit, string label = null) {
    if (components.Count is 0) {
      throw new InvalidOperationException("components must contain at least one type name");
    }

    var query = this.CreateQuery(components.Select(this.types.ResolveNamed).ToArray());

    try {
      var count = this.Count(query);

      Value labelSystem = null;
      MethodMirror labelMethod = null;

      if (label is not null) {
        var parts = label.Split(':');

        if (parts.Length is not 2) {
          throw new InvalidOperationException("label expects \"<systemTypeFullName>:<method>\"");
        }

        labelSystem = this.GetSystem(parts[0]);
        labelMethod = this.inv.FindMethod(this.inv.TypeOf(labelSystem), parts[1], 1);
      }

      var rows = new List<EcsQueryRow>();

      if (count > 0 && limit > 0) {
        var entities = this.EntityArray(query);

        foreach (var entity in entities.GetValues(0, Math.Min(limit, entities.Length))) {
          rows.Add(
            new EcsQueryRow {
              Entity = this.inv.Format(entity),
              Label = labelSystem is not null
                ? this.inv.Format(this.inv.Invoke(labelSystem, labelMethod, entity))
                : null
            }
          );
        }
      }

      return new EcsQueryListing {
        Count = count,
        Rows = rows
      };
    }
    finally {
      _ = this.inv.Invoke(query, "Dispose");
    }
  }

  /// <summary>Builds an EntityQuery requiring all the given component types (ReadWrite).</summary>
  private Value CreateQuery(TypeMirror[] componentTypes) {
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

  private int Count(Value query) =>
    (int) ((PrimitiveValue) this.inv.Invoke(query, "CalculateEntityCount")).Value;

  /// <summary>
  /// Materializes the query's entities as a managed Entity[] in the debuggee (ToEntityArray with
  /// the Temp allocator, then NativeArray.ToArray) and returns its mirror.
  /// </summary>
  private ArrayMirror EntityArray(Value query) {
    var handleType = this.inv.ResolveType("Unity.Collections.AllocatorManager+AllocatorHandle");

    var handle = this.inv.InvokeStatic(
      handleType,
      this.inv.FindMethod(handleType, "op_Implicit", 1, paramTypes: ["Allocator"]),
      this.TempAllocator()
    );

    var native = this.inv.Invoke(query, "ToEntityArray", handle);

    return (ArrayMirror) this.inv.Invoke(native, "ToArray");
  }
}

/// <summary>
/// What <see cref="Ecs.Query" /> matched: the exact count, and the rows it listed.
/// </summary>
public sealed class EcsQueryListing {
  /// <summary>Exact match count (independent of the listing limit).</summary>
  public int Count { get; init; }

  public IReadOnlyList<EcsQueryRow> Rows { get; init; }
}

/// <summary>One listed entity, optionally annotated via the label system call.</summary>
public sealed class EcsQueryRow {
  public string Entity { get; init; }

  public string Label { get; init; }
}
