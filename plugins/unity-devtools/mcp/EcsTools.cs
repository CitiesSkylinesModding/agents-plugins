using System.ComponentModel;
using System.Globalization;
using JetBrains.Annotations;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Mono.Debugger.Soft;
using UnityDevtools.Sdb;

namespace UnityDevtools.Mcp;

/// <summary>
/// ECS inspection and mutation tools: entity queries, component read/write, and dynamic-buffer
/// access, all over live SDB invokes.
/// Writes hit the running simulation; hold a suspend window (see the suspend tool) when
/// consistency across several writes matters.
/// </summary>
[McpServerToolType]
[UsedImplicitly]
public sealed class EcsTools(UnitySession session) {
  [McpServerTool(Name = "ecs_query")]
  [Description(
    """
    Count and list the entities having ALL the given component types. With label, each listed
    entity is annotated via a one-Entity-arg method on a managed system (e.g. a name system),
    format "<systemTypeFullName>:<method>".
    Attaches lazily; the game is only briefly suspended unless a suspend hold is active.
    """
  )]
  [UsedImplicitly]
  public EcsQueryResult Query(
    [Description("Fully-qualified component type names; entities must have ALL of them.")]
    string[] components,
    [Description("Max entities to list (the count is always exact).")] int limit = 10,
    [Description("ECS world name; omit for the default world.")] string? world = null,
    [Description("Optional \"<systemTypeFullName>:<method>\" annotation call per entity.")]
    string? label = null
  ) {
    return ToolGuard.Run(() => session.Run(Operation));

    EcsQueryResult Operation(SdbContext ctx) {
      if (components.Length == 0) {
        throw new McpException("components must contain at least one type name");
      }

      var inv = ctx.Invoker;
      var ecs = ctx.Ecs(world);
      var types = components.Select(inv.ResolveType).ToArray();
      var query = ecs.CreateQuery(types);

      try {
        var count = ecs.Count(query);

        Value? labelSystem = null;
        MethodMirror? labelMethod = null;

        if (label != null) {
          var parts = label.Split(':');

          if (parts.Length != 2) {
            throw new McpException("label expects \"<systemTypeFullName>:<method>\"");
          }

          labelSystem = ecs.GetSystem(parts[0]);
          labelMethod = inv.FindMethod(inv.TypeOf(labelSystem), parts[1], 1);
        }

        var entities = new List<EcsEntityInfo>();

        if (count > 0 && limit > 0) {
          var arr = ecs.EntityArray(query);
          var take = Math.Min(limit, arr.Length);

          entities.AddRange(
            arr.GetValues(0, take)
              .Select(e => new EcsEntityInfo {
                  Entity = inv.Format(e),
                  Label = labelSystem != null ? inv.Format(inv.Invoke(labelSystem, labelMethod, e)) : null
                }
              )
          );
        }

        return new EcsQueryResult {
          World = ecs.WorldName,
          Components = components,
          Count = count,
          Entities = entities,
          Omitted = count - entities.Count
        };
      }
      finally {
        _ = inv.Invoke(query, "Dispose");
      }
    }
  }

  [McpServerTool(Name = "ecs_get_component")]
  [Description(
    """
    Read one entity's component and report its field values.
    The entity's existence is verified, so a wrong index/version fails loudly.
    Attaches lazily; the game is only briefly suspended unless a suspend hold is active.
    """
  )]
  [UsedImplicitly]
  public EcsComponentResult GetComponent(
    [Description("Fully-qualified component type name (unmanaged IComponentData).")]
    string component,
    [Description("Entity as \"index[:version]\"; version omitted matches any.")] string entity,
    [Description("ECS world name; omit for the default world.")] string? world = null
  ) {
    return ToolGuard.Run(() => session.Run(Operation));

    EcsComponentResult Operation(SdbContext ctx) {
      var inv = ctx.Invoker;
      var ecs = ctx.Ecs(world);
      var compType = inv.ResolveType(component);
      var e = EcsTools.ResolveEntity(inv, ecs, compType, entity);

      return new EcsComponentResult {
        World = ecs.WorldName,
        Entity = inv.Format(e),
        Component = compType.FullName,
        Value = inv.Format(ecs.GetComponent(e, compType), 3)
      };
    }
  }

  [McpServerTool(Name = "ecs_set_component")]
  [Description(
    """
    Write one field of one entity's component (read-modify-write of the whole component), then
    read it back. Mutates live game state.
    Field values: primitives and enums as text, Entity fields as "index:version".
    Attaches lazily; hold a suspend window across several writes when consistency matters.
    """
  )]
  [UsedImplicitly]
  public EcsSetComponentResult SetComponent(
    [Description("Fully-qualified component type name (unmanaged IComponentData).")]
    string component,
    [Description("Entity as \"index[:version]\"; version omitted matches any.")] string entity,
    [Description("Field name on the component, case-insensitive.")] string field,
    [Description("New value: primitive/enum as text, or \"index:version\" for an Entity field.")]
    string value,
    [Description("ECS world name; omit for the default world.")] string? world = null
  ) {
    return ToolGuard.Run(() => session.Run(Operation));

    EcsSetComponentResult Operation(SdbContext ctx) {
      var inv = ctx.Invoker;
      var ecs = ctx.Ecs(world);
      var compType = inv.ResolveType(component);
      var e = EcsTools.ResolveEntity(inv, ecs, compType, entity);
      var fieldInfo = Ecs.RequireField(compType, field);
      var current = (StructMirror) ecs.GetComponent(e, compType);
      var before = inv.Format(current, 3);

      current[fieldInfo.Name] = ecs.ParseFieldValue(fieldInfo.FieldType, value);
      ecs.SetComponent(e, compType, current);

      return new EcsSetComponentResult {
        World = ecs.WorldName,
        Entity = inv.Format(e),
        Component = compType.FullName,
        Before = before,
        After = inv.Format(ecs.GetComponent(e, compType), 3)
      };
    }
  }

  [McpServerTool(Name = "ecs_get_buffer")]
  [Description(
    """
    Read one entity's DynamicBuffer and report its elements.
    Attaches lazily; the game is only briefly suspended unless a suspend hold is active.
    """
  )]
  [UsedImplicitly]
  public EcsBufferResult GetBuffer(
    [Description("Fully-qualified buffer element type name (IBufferElementData).")]
    string elementType,
    [Description("Entity as \"index[:version]\"; version defaults to 1.")] string entity,
    [Description("ECS world name; omit for the default world.")] string? world = null
  ) {
    return ToolGuard.Run(() => session.Run(Operation));

    EcsBufferResult Operation(SdbContext ctx) {
      var inv = ctx.Invoker;
      var ecs = ctx.Ecs(world);
      var (index, version) = Ecs.ParseEntitySpec(entity);
      var e = EcsTools.RequireEntity(ecs, index, version ?? 1);
      var buf = ecs.GetBuffer(e, inv.ResolveType(elementType));
      var length = ecs.BufferLength(buf);

      var elements = new List<string>(length);

      for (var i = 0; i < length; i++) {
        elements.Add(inv.Format(inv.Invoke(buf, "get_Item", inv.Prim(i)), 3));
      }

      return new EcsBufferResult {
        World = ecs.WorldName,
        Entity = inv.Format(e),
        ElementType = elementType,
        Length = length,
        Elements = elements
      };
    }
  }

  [McpServerTool(Name = "ecs_buffer_edit")]
  [Description(
    """
    Edit one entity's DynamicBuffer in place; mutates live game state.
    op "add" appends an element cloned from element 0 with one field overridden via set (the buffer
    must be non-empty);
    op "remove_at" removes the element at index.
    Hold a suspend window across several edits when consistency matters.
    """
  )]
  [UsedImplicitly]
  public EcsBufferEditResult BufferEdit(
    [Description("\"add\" (append, cloned from element 0 + set) or \"remove_at\".")] string op,
    [Description("Fully-qualified buffer element type name (IBufferElementData).")]
    string elementType,
    [Description("Entity as \"index[:version]\"; version defaults to 1.")] string entity,
    [Description("For add: \"<field>=<value>\" override applied to the cloned element.")]
    string? set = null,
    [Description("For remove_at: element index to remove.")] int? index = null,
    [Description("ECS world name; omit for the default world.")] string? world = null
  ) {
    return ToolGuard.Run(() => session.Run(Operation));

    EcsBufferEditResult Operation(SdbContext ctx) {
        var inv = ctx.Invoker;
        var ecs = ctx.Ecs(world);
        var elemType = inv.ResolveType(elementType);
        var (entityIndex, entityVersion) = Ecs.ParseEntitySpec(entity);
        var e = EcsTools.RequireEntity(ecs, entityIndex, entityVersion ?? 1);
        var buf = ecs.GetBuffer(e, elemType);
        var length = ecs.BufferLength(buf);

        switch (op) {
          case "add": {
            if (set == null) {
              throw new McpException("op \"add\" requires set=\"<field>=<value>\"");
            }

            if (length == 0) {
              throw new McpException("buffer is empty; add clones element 0 as the template for new elements");
            }

            var eq = set.IndexOf('=', StringComparison.Ordinal);

            if (eq <= 0) {
              throw new McpException("set expects \"<field>=<value>\"");
            }

            var fieldInfo = Ecs.RequireField(elemType, set[..eq]);
            var element = (StructMirror) inv.Invoke(buf, "get_Item", inv.Prim(0));

            element[fieldInfo.Name] = ecs.ParseFieldValue(fieldInfo.FieldType, set[(eq + 1)..]);
            _ = inv.Invoke(buf, "Add", element);

            return new EcsBufferEditResult {
              World = ecs.WorldName,
              Entity = inv.Format(e),
              Element = inv.Format(element, 3),
              NewLength = ecs.BufferLength(buf)
            };
          }

          case "remove_at": {
            if (index is not {} at) {
              throw new McpException("op \"remove_at\" requires index");
            }

            if (at < 0 || at >= length) {
              throw new McpException($"index {at.ToString(CultureInfo.InvariantCulture)} out of range " + $"(buffer length {length.ToString(CultureInfo.InvariantCulture)})");
            }

            var removed = inv.Format(inv.Invoke(buf, "get_Item", inv.Prim(at)), 3);

            _ = inv.Invoke(buf, "RemoveAt", inv.Prim(at));

            return new EcsBufferEditResult {
              World = ecs.WorldName,
              Entity = inv.Format(e),
              Element = removed,
              NewLength = ecs.BufferLength(buf)
            };
          }

          default: throw new McpException("op must be \"add\" or \"remove_at\"");
        }
      }
  }

  /// <summary>
  /// Resolves the target entity of a component tool: with a version, builds it client-side and
  /// verifies existence (no query needed); without one, scans a query on the component type to
  /// find the live version.
  /// </summary>
  private static StructMirror ResolveEntity(
    Invoker inv,
    Ecs ecs,
    TypeMirror compType,
    string spec
  ) {
    var (index, version) = Ecs.ParseEntitySpec(spec);

    if (version is {} v) {
      return EcsTools.RequireEntity(ecs, index, v);
    }

    var query = ecs.CreateQuery([compType]);

    try {
      return ecs.FindEntity(query, index, null);
    }
    finally {
      _ = inv.Invoke(query, "Dispose");
    }
  }

  private static StructMirror RequireEntity(Ecs ecs, int index, int version) {
    var entity = ecs.MakeEntity(index, version);

    return !ecs.Exists(entity)
      ? throw new McpException(
        $"entity {index}:{version} does not exist (recycled index or wrong version?)"
      )
      : entity;
  }
}

/// <summary>Result of the <c>ecs_query</c> tool.</summary>
public sealed record EcsQueryResult {
  public required string World { [UsedImplicitly] get; init; }

  public required IReadOnlyList<string> Components { [UsedImplicitly] get; init; }

  /// <summary>Exact match count (independent of the listing limit).</summary>
  public required int Count { [UsedImplicitly] get; init; }

  public required IReadOnlyList<EcsEntityInfo> Entities { [UsedImplicitly] get; init; }

  /// <summary>Matches not listed; raise the limit to see them.</summary>
  public required int Omitted { [UsedImplicitly] get; init; }
}

/// <summary>One listed entity, optionally annotated via the label system call.</summary>
public sealed record EcsEntityInfo {
  public required string Entity { [UsedImplicitly] get; init; }

  public required string? Label { [UsedImplicitly] get; init; }
}

/// <summary>Result of the <c>ecs_get_component</c> tool.</summary>
public sealed record EcsComponentResult {
  public required string World { [UsedImplicitly] get; init; }

  public required string Entity { [UsedImplicitly] get; init; }

  public required string Component { [UsedImplicitly] get; init; }

  public required string Value { [UsedImplicitly] get; init; }
}

/// <summary>Result of the <c>ecs_set_component</c> tool: the component before and after.</summary>
public sealed record EcsSetComponentResult {
  public required string World { [UsedImplicitly] get; init; }

  public required string Entity { [UsedImplicitly] get; init; }

  public required string Component { [UsedImplicitly] get; init; }

  public required string Before { [UsedImplicitly] get; init; }

  /// <summary>Read back from the debuggee after the write.</summary>
  public required string After { [UsedImplicitly] get; init; }
}

/// <summary>Result of the <c>ecs_get_buffer</c> tool.</summary>
public sealed record EcsBufferResult {
  public required string World { [UsedImplicitly] get; init; }

  public required string Entity { [UsedImplicitly] get; init; }

  public required string ElementType { [UsedImplicitly] get; init; }

  public required int Length { [UsedImplicitly] get; init; }

  public required IReadOnlyList<string> Elements { [UsedImplicitly] get; init; }
}

/// <summary>Result of the <c>ecs_buffer_edit</c> tool.</summary>
public sealed record EcsBufferEditResult {
  public required string World { [UsedImplicitly] get; init; }

  public required string Entity { [UsedImplicitly] get; init; }

  /// <summary>The element added or removed.</summary>
  public required string Element { [UsedImplicitly] get; init; }

  public required int NewLength { [UsedImplicitly] get; init; }
}
