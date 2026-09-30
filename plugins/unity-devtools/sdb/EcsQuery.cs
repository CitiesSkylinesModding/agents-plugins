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
  /// <paramref name="limit" /> of them, each with what <paramref name="select" /> reads off it.
  /// <paramref name="label" /> is <c>systemTypeFullName:method</c>, a one-Entity-arg method on a
  /// managed system that every listed entity is annotated through.
  /// <paramref name="select" /> is a list of <c>componentTypeFullName[:field]</c> specs and picks
  /// columns only: the match is built from <paramref name="components" /> alone, so a listed
  /// entity lacking a selected component is a row with that spec absent.
  /// A spec that is wrong for every row is refused before the query exists; a read that throws on
  /// one row marks that row and the rest return.
  /// The query is the game's own object and is disposed on every way out, since nothing else would
  /// ever give it back.
  /// </summary>
  public EcsQueryListing Query(
    IReadOnlyList<string> components,
    int limit,
    string label = null,
    IReadOnlyList<string> select = null
  ) {
    if (components.Count is 0) {
      throw new InvalidOperationException("components must contain at least one type name");
    }

    var filter = components.Select(this.types.ResolveNamed).ToArray();
    var selection = select is { Count: > 0 } ? this.ResolveSelection(select, filter) : null;
    var query = this.CreateQuery(filter);

    try {
      var count = this.Count(query);

      var labeller = label is null ? null : this.ResolveLabel(label);
      var rows = new List<EcsQueryRow>();

      if (count > 0 && limit > 0) {
        var entities = this.EntityArray(query);

        foreach (var listed in entities.GetValues(0, Math.Min(limit, entities.Length))) {
          var entity = (StructMirror) listed;

          rows.Add(
            EcsSelection.Row(
              this.inv.Format(entity),
              labeller is var (system, method) ? this.LabelOf(system, method, entity) : null,
              selection?.Columns.SelectMany(column => this.ReadColumn(entity, column)).ToArray()
            )
          );
        }
      }

      return new EcsQueryListing {
        Count = count,
        Rows = rows,
        Summary = EcsSelection.Summarize(selection?.Keys, labeller is not null, rows),
        EnabledStateNote = selection?.EnabledStateNote
      };
    }
    finally {
      _ = this.inv.Invoke(query, "Dispose");
    }
  }

  /// <summary>
  /// Finds the system and the method a <c>systemTypeFullName:method</c> label names.
  /// </summary>
  private (Value System, MethodMirror Method)? ResolveLabel(string label) {
    var parts = label.Split(':');

    if (parts.Length is not 2) {
      throw new InvalidOperationException("label expects \"<systemTypeFullName>:<method>\"");
    }

    var system = this.GetSystem(parts[0]);

    return (system, this.inv.FindMethod(this.inv.TypeOf(system), parts[1], 1));
  }

  /// <summary>
  /// One entity's label, or why it has none.
  /// A throw in the game costs this row its label and nothing else, the rule every per-row read
  /// of a listing follows; a lost connection is let through, since nothing after it can be read.
  /// </summary>
  private (string Value, string Error) LabelOf(
    Value system,
    MethodMirror method,
    StructMirror entity
  ) {
    try {
      return (this.inv.Format(this.inv.Invoke(system, method, entity)), null);
    }
    catch (Exception ex) when (!UnitySession.IsDisconnect(ex)) {
      return (null, ex.Message);
    }
  }

  /// <summary>
  /// Settles everything about a selection that is true of every row: which types the specs name,
  /// that each is stored in a way the component accessors serve, which fields exist, and what each
  /// row will have to ask.
  /// Storage is classified in two steps and in this order: the marker interfaces establish that
  /// the type is a component at all, and only then does the type index say whether it is a tag,
  /// which no marker separates from a component with fields.
  /// </summary>
  private ResolvedSelection ResolveSelection(IReadOnlyList<string> select, TypeMirror[] filter) {
    var flags = this.catalog.Flags;
    var (isEnabled, isEnableable, absent) = this.ProbeEnabledState(flags);

    // Two spellings can resolve to one type, and a row reads a type once.
    var byType = EcsSelection.Parse(select)
      .Select(c => (Type: this.types.ResolveNamed(c.Component), c.Specs))
      .GroupBy(c => c.Type, c => c.Specs);

    var columns = new List<SelectedColumn>();
    var unclassified = false;

    foreach (var group in byType) {
      var type = group.Key;

      Ecs.RequireComponentStorage(type);

      var componentType = this.ComponentTypeOf(type);
      var described = this.Describe(componentType, flags);

      if (described.Kind is not (EcsKind.Component or EcsKind.Tag)) {
        throw new InvalidOperationException(
          $"{type.FullName} is stored as kind \"{described.Kind.Wire}\", which select cannot read"
        );
      }

      var isTag = described.Kind is EcsKind.Tag;

      var specs = group.SelectMany(g => g)
        .Select(spec => (spec.Key, Field: Ecs.SelectedField(type, spec, isTag)))
        .ToArray();

      // The query already excludes an entity whose FILTERED enableable component is off, and
      // carries every filtered component by construction, so a column in the filter has neither
      // question left to ask.
      var inFilter = Array.IndexOf(filter, type) >= 0;

      var enableable = inFilter || isEnabled is null
        ? null
        : this.EnableableOrNull(componentType, described, flags, isEnableable);

      unclassified |= !inFilter && absent is null && enableable is null;

      columns.Add(
        new SelectedColumn {
          Type = type,
          Specs = specs,
          IsTag = isTag,
          InFilter = inFilter,
          ComponentType = componentType,
          EnabledGetter = enableable is true ? isEnabled : null
        }
      );
    }

    var outsideFilter = columns.Exists(c => !c.InFilter);

    return new ResolvedSelection {
      Columns = columns,
      EnabledStateNote = absent is not null && outsideFilter
        ? $"this target's Unity Entities version cannot report enabled state ({absent} is " +
        "absent), so no row lists a disabled spec; that is not the same as none being disabled"
        : unclassified
          ? "this target's Unity Entities version could not tell whether every selected " +
          "component type is enableable, so a spec no row lists as disabled may be one it could " +
          "not classify rather than one that is never disabled"
          : null
    };
  }

  /// <summary>
  /// The field a spec selects, null for the whole component.
  /// A tag carries no field to name, and refusing the spec says so once rather than reporting the
  /// same miss on every row.
  /// </summary>
  private static FieldInfoMirror SelectedField(TypeMirror type, SelectedSpec spec, bool isTag) {
    if (spec.Field is null) {
      return null;
    }

    return isTag
      ? throw new InvalidOperationException(
        $"{type.FullName} is a tag and carries no fields, so '{spec.Key}' selects nothing; " +
        $"select \"{type.FullName}\" to report its presence"
      )
      : Ecs.RequireField(type, spec.Field);
  }

  /// <summary>
  /// Reads one selected component off one entity and renders every spec on it, which is one value
  /// read however many specs share the component.
  /// Absence is the presence predicate answering no, never a caught refusal: deciding it by
  /// catching would file a read that failed for another reason under absent.
  /// The presence check is what keeps a build with the collections checks compiled out from
  /// answering plausible zeros for a component the entity does not carry, so it is skipped only
  /// where the query itself guarantees the answer.
  /// A column therefore costs a row one invoke for the value, plus one for presence and, on an
  /// enableable type, one for the enabled state when the query does not filter on it: the guards
  /// are the price of a read that can be believed, not waste to trim.
  /// A throw in the game marks this column on this row; a lost connection is let through, since a
  /// catch that absorbed one would keep invoking on a window that is no longer sound.
  /// </summary>
  private SpecRead[] ReadColumn(StructMirror entity, SelectedColumn column) {
    try {
      if (
        !column.InFilter &&
        !this.Carries(entity, column.Type, "HasComponent", EcsKind.Component)
      ) {
        return column.Specs.Select(spec => SpecRead.Absent(spec.Key)).ToArray();
      }

      var disabled = this.EnabledOrNull(
        entity,
        column.ComponentType,
        EcsKind.Component,
        column.EnabledGetter
      ) is false;

      // Carrying a tag IS its state, so there is nothing to read and the kind stands in for the
      // value, as it does in a listing.
      if (column.IsTag) {
        return column.Specs.Select(spec => SpecRead.Of(spec.Key, EcsKind.Tag.Wire, disabled))
          .ToArray();
      }

      if (column.InFilter) {
        this.MarkCarried(entity, column.Type, EcsKind.Component);
      }

      var value = (StructMirror) this.GetComponent(entity, column.Type);

      return column.Specs
        .Select(spec => SpecRead.Of(
            spec.Key,
            this.inv.Format(spec.Field is null ? value : value[spec.Field.Name], Invoker.ReadDepth),
            disabled
          )
        )
        .ToArray();
    }
    catch (Exception ex) when (!UnitySession.IsDisconnect(ex)) {
      return column.Specs.Select(spec => SpecRead.Failed(spec.Key, ex.Message)).ToArray();
    }
  }

  /// <summary>A selection with everything that holds for every row already settled.</summary>
  private sealed class ResolvedSelection {
    public IReadOnlyList<SelectedColumn> Columns { get; init; }

    /// <summary>Every spec as the caller wrote it, in the order rows report them.</summary>
    public IReadOnlyList<string> Keys =>
      this.Columns.SelectMany(c => c.Specs).Select(s => s.Key).ToArray();

    /// <inheritdoc cref="EcsQueryListing.EnabledStateNote" />
    public string EnabledStateNote { get; init; }
  }

  /// <summary>One selected component type, and what reading it off a row takes.</summary>
  private sealed class SelectedColumn {
    public TypeMirror Type { get; init; }

    /// <summary>The specs on this type, each with the field it selects or null.</summary>
    public IReadOnlyList<(string Key, FieldInfoMirror Field)> Specs { get; init; }

    public bool IsTag { get; init; }

    /// <summary>Whether the query itself requires this type, compared as resolved types.</summary>
    public bool InFilter { get; init; }

    public StructMirror ComponentType { get; init; }

    /// <summary>
    /// What answers whether a row carries this type disabled, null when no row needs asking: the
    /// type is not enableable, the query filters on it, or the target cannot say.
    /// </summary>
    public MethodMirror EnabledGetter { get; init; }
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

  /// <summary>
  /// Per-spec counts over <see cref="Rows" />; null when the call neither selected nor labelled.
  /// </summary>
  public EcsSelectSummary Summary { get; init; }

  /// <summary>
  /// Why a disabled selected component may have gone unreported, null when the target reports
  /// enabled state for every selected type that needed asking.
  /// </summary>
  public string EnabledStateNote { get; init; }
}

/// <summary>
/// One listed entity, with what the label call and the selected specs read off it.
/// </summary>
public sealed class EcsQueryRow {
  public string Entity { get; init; }

  /// <summary>
  /// What the label call answered; null when no label was asked, or when it failed on this entity
  /// (see <see cref="Errors" />).
  /// </summary>
  public string Label { get; init; }

  /// <summary>
  /// The selected values, keyed by the spec exactly as the caller wrote it; null when nothing was
  /// selected.
  /// A spec missing from both this and <see cref="Errors" /> names a component the entity does
  /// not carry.
  /// A tag carries its kind name where a value would go, the one entry no read produced.
  /// </summary>
  public IReadOnlyDictionary<string, string> Values { get; init; }

  /// <summary>
  /// Why a spec, or the label under the key "label", carries no value on this entity; null when
  /// every read answered.
  /// </summary>
  public IReadOnlyDictionary<string, string> Errors { get; init; }

  /// <summary>
  /// The specs whose component this entity carries DISABLED, beside the stored value each still
  /// reports; null when there is none.
  /// </summary>
  public IReadOnlyList<string> Disabled { get; init; }
}
