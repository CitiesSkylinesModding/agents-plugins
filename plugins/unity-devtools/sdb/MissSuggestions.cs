using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityDevtools.Sdb;

/// <summary>
/// What a miss offers in place of the name that was not found: which candidates, in which order,
/// and how many before the list is cut.
/// Pure by design, names in and text out, so the rules are testable without a debuggee: what
/// overflows a cap against a live one depends on which runtime happens to be loaded.
/// </summary>
public static class MissSuggestions {
  /// <summary>Member and method names are short, so the cap only guards the huge type.</summary>
  public const int MemberCap = 30;

  /// <summary>
  /// The name as the evaluator resolves it: a nested type is spelled with dots there, where the
  /// runtime and every other tool join it to its declaring type with a '+'.
  /// </summary>
  public static string EvalName(string fullName) => fullName.Replace('+', '.');

  /// <summary>
  /// Names containing <paramref name="contained" /> ahead of the rest, each group keeping the
  /// order it came in.
  /// </summary>
  public static IReadOnlyList<string> ContainingFirst(
    IEnumerable<string> names,
    string contained
  ) {
    var distinct = names.Distinct(StringComparer.Ordinal).ToList();

    return distinct.Where(Contains).Concat(distinct.Where(n => !Contains(n))).ToList();

    bool Contains(string name) => name.Contains(contained, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Renders labeled groups of names under ONE cap spanning them all, earlier groups first, so
  /// what a cut drops is the tail of the last group.
  /// A cut counts what it dropped and says where the whole list is.
  /// </summary>
  public static string Listing(
    IReadOnlyList<(string Label, IReadOnlyList<string> Names)> groups,
    int cap,
    string whereTheRestIs
  ) {
    var total = groups.Sum(g => g.Names.Count);

    if (total is 0) {
      return "none";
    }

    var room = cap;
    var parts = new List<string>();

    foreach (var (label, names) in groups) {
      var taken = names.Take(room).ToList();

      room -= taken.Count;

      if (taken.Count > 0) {
        parts.Add($"{label}: {string.Join(", ", taken)}");
      }
    }

    var listing = string.Join("; ", parts);

    return total > cap ? $"{listing} (+{total - cap} more: {whereTheRestIs})" : listing;
  }
}
