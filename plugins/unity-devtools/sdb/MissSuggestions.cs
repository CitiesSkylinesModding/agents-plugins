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
  /// <summary>Type suggestions are full names, so a handful already fills a line.</summary>
  public const int TypeCap = 5;

  /// <summary>Member and method names are short, so the cap only guards the huge type.</summary>
  public const int MemberCap = 30;

  /// <summary>
  /// How many of the type slots names matching another segment alone may hold against a full
  /// list of matches on the contained one.
  /// </summary>
  private const int OtherSegmentSlots = 2;

  /// <summary>
  /// The types a missed name could have meant, matched on the simple name and ignoring case:
  /// those named exactly <paramref name="contained" /> first, then those whose simple name
  /// contains it, then those named exactly another of <paramref name="exact" />; within each, the
  /// shortest simple name first, then ordinal by full name.
  /// The last group keeps <see cref="OtherSegmentSlots" /> of a full list.
  /// No edit distance: a caller guesses a plausible wrong name far more often than it mistypes a
  /// right one, and a listing finds the first where a distance threshold does not.
  /// </summary>
  /// <param name="contained">Null when only exact matches are wanted.</param>
  public static Ranked RankTypes(
    IEnumerable<string> fullNames,
    IReadOnlyCollection<string> exact,
    string contained
  ) {
    var ranked = fullNames.Distinct(StringComparer.Ordinal)
      .Select(fullName => (FullName: fullName, Simple: MissSuggestions.SimpleName(fullName)))
      .Select(n => (n.FullName, n.Simple, Rank: MissSuggestions.RankOf(n.Simple, exact, contained)))
      .Where(n => n.Rank >= 0)
      .OrderBy(n => n.Rank)
      .ThenBy(n => n.Simple.Length)
      .ThenBy(n => n.FullName, StringComparer.Ordinal)
      .ToList();

    var reserved = Math.Min(MissSuggestions.OtherSegmentSlots, ranked.Count(n => n.Rank is 2));

    var listed = ranked.Where(n => n.Rank < 2)
      .Take(MissSuggestions.TypeCap - reserved)
      .Concat(ranked.Where(n => n.Rank is 2))
      .Take(MissSuggestions.TypeCap)
      .Select(n => n.FullName)
      .ToList();

    return new Ranked(listed, ranked.Count);
  }

  /// <summary>
  /// Whether a simple name is one <see cref="RankTypes" /> would keep, so a holder of many names
  /// can filter before it allocates them.
  /// </summary>
  public static bool Matches(
    ReadOnlySpan<char> simpleName,
    IReadOnlyCollection<string> exact,
    string contained
  ) =>
    MissSuggestions.RankOf(simpleName, exact, contained) >= 0;

  /// <summary>
  /// A type's own name as a caller writes it: past the namespace and any declaring type, and
  /// short of the generic arity suffix.
  /// </summary>
  public static string SimpleName(string fullName) =>
    MissSuggestions.SimpleName(fullName.AsSpan()).ToString();

  public static ReadOnlySpan<char> SimpleName(ReadOnlySpan<char> fullName) {
    var cut = fullName.LastIndexOfAny('.', '+');
    var name = cut < 0 ? fullName : fullName[(cut + 1)..];
    var arity = name.IndexOf('`');

    return arity < 0 ? name : name[..arity];
  }

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

  /// <summary>
  /// 0 for the contained name itself, 1 for a name containing it, 2 for another exact name,
  /// negative for none.
  /// </summary>
  private static int RankOf(
    ReadOnlySpan<char> simple,
    IReadOnlyCollection<string> exact,
    string contained
  ) {
    if (contained is not null) {
      if (simple.Equals(contained, StringComparison.OrdinalIgnoreCase)) {
        return 0;
      }

      if (simple.Contains(contained, StringComparison.OrdinalIgnoreCase)) {
        return 1;
      }
    }

    foreach (var name in exact) {
      if (simple.Equals(name, StringComparison.OrdinalIgnoreCase)) {
        return 2;
      }
    }

    return -1;
  }
}

/// <summary>A capped suggestion list, with how many there were before the cut.</summary>
public sealed record Ranked(IReadOnlyList<string> Listed, int Total);
