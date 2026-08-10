using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using JetBrains.Annotations;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Mono.Debugger.Soft;
using UnityDevtools.Sdb;

namespace UnityDevtools.Mcp;

/// <summary>
/// ECS inspection and mutation tools: entity queries, component read/write, and dynamic-buffer
/// access, all over live SDB invokes.
/// Writes hit the running simulation; hold a suspend window (see the suspend tool) when consistency
/// across several writes matters.
/// </summary>
[McpServerToolType]
[UsedImplicitly]
public sealed class EcsTools(UnitySession session) {
  /// <summary>
  /// The one entity-naming rule, worded identically on every tool that takes an entity so agents
  /// never have to remember which tool interprets a bare index how.
  /// </summary>
  private const string EntityParam =
    """
    Entity as "index[:version]": a bare index resolves to the live entity at that index, an
    explicit version is verified and fails when stale.
    """;

  [McpServerTool(Name = "ecs_query")]
  [Description(
    """
    Count and list the entities having ALL the given component types. With label, each listed
    entity is annotated via a one-Entity-arg method on a managed system (e.g. a name system),
    format "<systemTypeFullName>:<method>".
    Entities tagged Disabled or Prefab are excluded from the match, and so is an entity whose
    queried enableable component is currently disabled, so a count of 0 means "none the query
    can see" rather than "none exist"; reach one of those by index with ecs_list_components,
    which ignores the exclusion.
    Attaches lazily; the game is only briefly suspended unless a suspend hold is active.
    """
  )]
  [UsedImplicitly]
  public EcsQueryResult Query(
    [Description("Fully-qualified component type names; entities must have ALL of them.")]
    string[] components,
    [Description("Max entities to list (the count is always exact).")]
    int limit = 10,
    [Description("ECS world name; omit for the default world.")]
    string? world = null,
    [Description("Optional \"<systemTypeFullName>:<method>\" annotation call per entity.")]
    string? label = null
  ) {
    return ToolGuard.Run(() => session.Run(Operation));

    EcsQueryResult Operation(SdbContext ctx) {
      if (components.Length is 0) {
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

        if (label is not null) {
          var parts = label.Split(':');

          if (parts.Length is not 2) {
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
                  Label = labelSystem is not null
                    ? inv.Format(inv.Invoke(labelSystem, labelMethod, e))
                    : null
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

  [McpServerTool(Name = "ecs_list_components")]
  [Description(
    """
    List every component type an entity carries: the orient step on an unknown entity, answering
    what state it holds without guessing type names one at a time.
    Each entry reports the kind, which says what can read it: "component" -> ecs_get_component,
    "buffer" -> ecs_get_buffer, "tag" -> no fields to read (presence, and "enabled" when it carries
    one, IS the state), "shared" and "chunk" -> eval only, "managed" -> out of reach over the
    debugger.
    Enableable components also report whether they are currently ENABLED: a disabled component is
    still carried and still passes a presence check, while the simulation ignores it.
    With values, each "component" entry also inlines its field values; every other kind reports
    its kind where a value would go.
    With follow, the entity a named component REFERENCES is listed alongside this one: how you
    reach state that lives on a prefab or an owner rather than on the entity you hold.
    Attaches lazily; the game is only briefly suspended unless a suspend hold is active.
    """
  )]
  [UsedImplicitly]
  public EcsListComponentsResult ListComponents(
    [Description(EcsTools.EntityParam)] string entity,
    [Description(
      "Also read each component's field values: one read per component on top of the listing."
    )]
    bool values = false,
    [Description(
      """
      Chase an Entity-typed field to the entity it names and list it too, under the same values
      setting, as "<componentTypeFullName>[:<field>]"; the field is optional when the component
      carries exactly one Entity field.
      Exactly one level is followed: a longer chain is one call per hop.
      """
    )]
    string? follow = null,
    [Description("ECS world name; omit for the default world.")]
    string? world = null
  ) {
    return ToolGuard.Run(() => session.Run(Operation));

    EcsListComponentsResult Operation(SdbContext ctx) {
      var ecs = ctx.Ecs(world);
      var e = ecs.ResolveEntity(entity);

      // The reference is chased BEFORE the listing it accompanies, because every way a follow can
      // fail depends on the entity and the spec alone: failing first costs the caller a handful of
      // reads instead of a whole listing the error then throws away.
      var chased = follow is null ? null : ecs.Follow(e, follow);

      var listed = ecs.ListComponents(e, values);

      EcsFollowedResult? followed = null;

      if (chased is not null) {
        // The chased entity is listed under the caller's own values setting, and is never itself
        // followed: one level is what keeps the response bounded and a reference cycle unreachable.
        var target = ecs.ListComponents(chased.Target, values);

        followed = new EcsFollowedResult {
          Component = chased.Component,
          Field = chased.Field,
          Entity = ctx.Invoker.Format(chased.Target),
          Count = target.Components.Count,
          Components = target.Components
        };
      }

      return new EcsListComponentsResult {
        World = ecs.WorldName,
        Entity = ctx.Invoker.Format(e),
        Count = listed.Components.Count,
        Components = listed.Components,
        EnabledStateNote = listed.EnabledStateNote,
        Followed = followed
      };
    }
  }

  [McpServerTool(Name = "ecs_get_component")]
  [Description(
    """
    Read one entity's component and report its field values.
    Attaches lazily; the game is only briefly suspended unless a suspend hold is active.
    """
  )]
  [UsedImplicitly]
  public EcsComponentResult GetComponent(
    [Description("Fully-qualified component type name (unmanaged IComponentData).")]
    string component,
    [Description(EcsTools.EntityParam)] string entity,
    [Description("ECS world name; omit for the default world.")]
    string? world = null
  ) {
    return ToolGuard.Run(() => session.Run(Operation));

    EcsComponentResult Operation(SdbContext ctx) {
      var inv = ctx.Invoker;
      var ecs = ctx.Ecs(world);
      var compType = inv.ResolveType(component);
      var e = ecs.ResolveEntity(entity);

      return new EcsComponentResult {
        World = ecs.WorldName,
        Entity = inv.Format(e),
        Component = compType.FullName,
        Value = inv.Format(ecs.GetComponent(e, compType), Invoker.ReadDepth)
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
    [Description(EcsTools.EntityParam)] string entity,
    [Description("Field name on the component, case-insensitive.")]
    string field,
    [Description("New value: primitive/enum as text, or \"index:version\" for an Entity field.")]
    string value,
    [Description("ECS world name; omit for the default world.")]
    string? world = null
  ) {
    return ToolGuard.Run(() => session.Run(Operation));

    EcsSetComponentResult Operation(SdbContext ctx) {
      var inv = ctx.Invoker;
      var ecs = ctx.Ecs(world);
      var compType = inv.ResolveType(component);
      var e = ecs.ResolveEntity(entity);
      var fieldInfo = Ecs.RequireField(compType, field);
      var current = (StructMirror) ecs.GetComponent(e, compType);
      var before = inv.Format(current, Invoker.ReadDepth);

      current[fieldInfo.Name] = ecs.ParseFieldValue(fieldInfo.FieldType, value);
      ecs.SetComponent(e, compType, current);

      return new EcsSetComponentResult {
        World = ecs.WorldName,
        Entity = inv.Format(e),
        Component = compType.FullName,
        Before = before,
        After = inv.Format(ecs.GetComponent(e, compType), Invoker.ReadDepth)
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
    [Description(EcsTools.EntityParam)] string entity,
    [Description("ECS world name; omit for the default world.")]
    string? world = null
  ) {
    return ToolGuard.Run(() => session.Run(Operation));

    EcsBufferResult Operation(SdbContext ctx) {
      var inv = ctx.Invoker;
      var ecs = ctx.Ecs(world);
      var e = ecs.ResolveEntity(entity);
      var buf = ecs.GetBuffer(e, inv.ResolveType(elementType), isReadOnly: true);
      var length = ecs.BufferLength(buf);

      var elements = new List<string>(length);

      for (var i = 0; i < length; i++) {
        elements.Add(inv.Format(inv.Invoke(buf, "get_Item", inv.Prim(i)), Invoker.ReadDepth));
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
    [Description(EcsTools.EntityParam)] string entity,
    [Description("For add: \"<field>=<value>\" override applied to the cloned element.")]
    string? set = null,
    [Description("For remove_at: element index to remove.")]
    int? index = null,
    [Description("ECS world name; omit for the default world.")]
    string? world = null
  ) {
    return ToolGuard.Run(() => session.Run(Operation));

    EcsBufferEditResult Operation(SdbContext ctx) {
      var inv = ctx.Invoker;
      var ecs = ctx.Ecs(world);

      var elemType = inv.ResolveType(elementType);
      var e = ecs.ResolveEntity(entity);
      var buf = ecs.GetBuffer(e, elemType, isReadOnly: false);
      var length = ecs.BufferLength(buf);

      switch (op) {
        case "add": {
          if (set is null) {
            throw new McpException("op \"add\" requires set=\"<field>=<value>\"");
          }

          if (length is 0) {
            throw new McpException(
              "buffer is empty; add clones element 0 as the template for new elements"
            );
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
            Element = inv.Format(element, Invoker.ReadDepth),
            NewLength = ecs.BufferLength(buf)
          };
        }

        case "remove_at": {
          if (index is not {} at) {
            throw new McpException("op \"remove_at\" requires index");
          }

          if (at < 0 || at >= length) {
            throw new McpException(
              $"index {at.ToString(CultureInfo.InvariantCulture)} out of range " +
              $"(buffer length {length.ToString(CultureInfo.InvariantCulture)})"
            );
          }

          var removed = inv.Format(inv.Invoke(buf, "get_Item", inv.Prim(at)), Invoker.ReadDepth);

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

/// <summary>Result of the <c>ecs_list_components</c> tool: the entity's whole archetype.</summary>
public sealed record EcsListComponentsResult {
  public required string World { [UsedImplicitly] get; init; }

  public required string Entity { [UsedImplicitly] get; init; }

  public required int Count { [UsedImplicitly] get; init; }

  public required IReadOnlyList<EntityComponentInfo> Components { [UsedImplicitly] get; init; }

  /// <inheritdoc cref="EntityComponents.EnabledStateNote" />
  public required string? EnabledStateNote { [UsedImplicitly] get; init; }

  /// <summary>
  /// The referenced entity's archetype, null unless the call asked to follow one.
  /// </summary>
  public required EcsFollowedResult? Followed { [UsedImplicitly] get; init; }
}

/// <summary>
/// The entity a follow landed on, and the reference that led there.
/// It carries no enabled-state note of its own: that note reports what the TARGET's Entities
/// version can answer, so the one at the top level covers this listing too.
/// </summary>
public sealed record EcsFollowedResult {
  /// <inheritdoc cref="FollowedReference.Component" />
  public required string Component { [UsedImplicitly] get; init; }

  /// <inheritdoc cref="FollowedReference.Field" />
  public required string Field { [UsedImplicitly] get; init; }

  public required string Entity { [UsedImplicitly] get; init; }

  public required int Count { [UsedImplicitly] get; init; }

  public required IReadOnlyList<EntityComponentInfo> Components { [UsedImplicitly] get; init; }
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
