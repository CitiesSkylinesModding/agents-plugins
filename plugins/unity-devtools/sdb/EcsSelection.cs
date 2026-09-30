using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityDevtools.Sdb;

/// <summary>
/// The parts of a query's <c>select</c> that ask the game nothing: what the specs mean, and how the
/// outcomes of the per-row reads become rows and counts.
/// Kept free of mirrors so both halves hold without a debuggee.
/// </summary>
public static class EcsSelection {
  /// <summary>
  /// Parses <c>componentTypeFullName[:field]</c> specs, grouped by the component they name in the
  /// order each was first written, so a row reads a component once however many specs share it.
  /// </summary>
  public static IReadOnlyList<SelectedComponent> Parse(IReadOnlyList<string> specs) {
    // A spec written twice is one column, so the second spelling adds nothing to read or report.
    return specs.Distinct(StringComparer.Ordinal)
      .Select(static spec => {
          var (component, field) = EcsSelection.SplitSpec(spec, "select");

          return (Component: component, Spec: new SelectedSpec(spec, field));
        }
      )
      .GroupBy(parsed => parsed.Component, parsed => parsed.Spec, StringComparer.Ordinal)
      .Select(group => new SelectedComponent(group.Key, group.ToArray()))
      .ToArray();
  }

  /// <summary>
  /// Splits one <c>componentTypeFullName[:field]</c> spec, the shape every parameter naming a
  /// component and optionally one of its fields takes; the field is null when none is named.
  /// <paramref name="parameter" /> is the parameter the refusal is worded for.
  /// </summary>
  public static (string Component, string Field) SplitSpec(string spec, string parameter) {
    // A caller writes this spec by hand, so space around either half is a typo rather than a
    // name, and a component half that ends up empty named nothing at all.
    var parts = spec?.Split(':').Select(p => p.Trim()).ToArray() ?? [];

    if (parts.Length is 0 or > 2 || parts[0].Length is 0) {
      throw new InvalidOperationException(
        $"{parameter} expects \"<componentTypeFullName>[:<field>]\", got '{spec}'"
      );
    }

    // A trailing colon names no field.
    return (parts[0], parts.Length is 2 && parts[1].Length > 0 ? parts[1] : null);
  }

  /// <summary>The key a row's errors, and the failed counts, report a label under.</summary>
  public const string LabelKey = "label";

  /// <summary>
  /// Assembles one listed entity from what its reads came to.
  /// <paramref name="label" /> is null when no label was asked, and <paramref name="reads" /> when
  /// nothing was selected.
  /// Absence, failure and disabled state are told apart by WHERE a spec appears, never by a marker
  /// string in a value: an absent spec is in neither map, a failed one in the errors alone.
  /// </summary>
  public static EcsQueryRow Row(
    string entity,
    (string Value, string Error)? label,
    IReadOnlyList<SpecRead> reads
  ) {
    var errors = new Dictionary<string, string>(StringComparer.Ordinal);

    if (label?.Error is {} labelError) {
      errors[EcsSelection.LabelKey] = labelError;
    }

    foreach (var read in reads ?? []) {
      if (read.Error is not null) {
        errors[read.Key] = read.Error;
      }
    }

    var disabled = reads?.Where(r => r.IsDisabled).Select(r => r.Key).ToArray();

    return new EcsQueryRow {
      Entity = entity,
      Label = label?.Value,
      Values = reads?.Where(r => r.Value is not null)
        .ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal),
      Errors = errors.Count > 0 ? errors : null,
      Disabled = disabled is { Length: > 0 } ? disabled : null
    };
  }

  /// <summary>
  /// Counts, per spec, the rows that lack it, failed to read it, or carry it disabled; null when
  /// the call neither selected nor labelled.
  /// Every spec reports all three, zeroes included, so a count that is missing is never the way a
  /// caller learns that nothing went wrong.
  /// The counts are over <paramref name="rows" /> alone, which is the listing and not the match.
  /// <paramref name="keys" /> is null when nothing was selected.
  /// </summary>
  public static EcsSelectSummary Summarize(
    IReadOnlyList<string> keys,
    bool labelled,
    IReadOnlyList<EcsQueryRow> rows
  ) {
    if (keys is null && !labelled) {
      return null;
    }

    string[] failable = [.. keys ?? [], .. labelled ? [EcsSelection.LabelKey] : (string[]) []];

    return new EcsSelectSummary {
      Absent = Count(
        keys,
        static (row, key) => !row.Values.ContainsKey(key) && !Failed(row, key)
      ),
      Failed = Count(failable, Failed),
      Disabled = Count(keys, static (row, key) => row.Disabled?.Contains(key) is true)
    };

    Dictionary<string, int> Count(
      IReadOnlyList<string> counted,
      Func<EcsQueryRow, string, bool> hit
    ) =>
      counted?.ToDictionary(key => key, key => rows.Count(row => hit(row, key)));

    static bool Failed(EcsQueryRow row, string key) => row.Errors?.ContainsKey(key) is true;
  }
}

/// <summary>
/// How many of the LISTED rows each spec was absent from, failed on, or was disabled on.
/// </summary>
public sealed class EcsSelectSummary {
  /// <summary>Rows not carrying the spec's component; null when nothing was selected.</summary>
  public IReadOnlyDictionary<string, int> Absent { get; init; }

  /// <summary>Rows whose read threw, the label's under the key "label".</summary>
  public IReadOnlyDictionary<string, int> Failed { get; init; }

  /// <summary>
  /// Rows carrying the spec's component disabled; null when nothing was selected.
  /// </summary>
  public IReadOnlyDictionary<string, int> Disabled { get; init; }
}

/// <summary>
/// What reading one spec on one entity came to: a value, a failure, or neither when the entity
/// does not carry the component.
/// </summary>
public sealed record SpecRead {
  private SpecRead() {
  }

  public string Key { get; private init; }

  public string Value { get; private init; }

  public string Error { get; private init; }

  /// <summary>Whether the component was read off an entity that carries it disabled.</summary>
  public bool IsDisabled { get; private init; }

  public static SpecRead Of(string key, string value, bool disabled = false) =>
    new() {
      Key = key,
      Value = value,
      IsDisabled = disabled
    };

  public static SpecRead Absent(string key) =>
    new() {
      Key = key
    };

  public static SpecRead Failed(string key, string error) =>
    new() {
      Key = key,
      Error = error
    };
}

/// <summary>One selected component, and every spec that reads from it.</summary>
public sealed record SelectedComponent(string Component, IReadOnlyList<SelectedSpec> Specs);

/// <summary>
/// One spec: the key it is reported under, which is the spec exactly as the caller wrote it, and
/// the field it selects, null for the whole component.
/// </summary>
public sealed record SelectedSpec(string Key, string Field);
