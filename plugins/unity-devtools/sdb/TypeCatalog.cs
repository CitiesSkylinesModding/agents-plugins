using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>
/// Every type name the debuggee has loaded, harvested once and searched client-side afterward, so
/// iterating on a pattern spends no round trips at all.
/// It still spends the caller's suspend window: the scan runs with the game frozen, which is what
/// the match budget ultimately caps.
/// Harvesting on top of that runs as invokes on the game's main thread; paying those once is what
/// makes the catalog worth keeping for the whole attach.
/// The catalog belongs to one attach: type names are correlated with the assembly mirrors they came
/// from, and those die with the connection.
/// </summary>
public sealed class TypeCatalog {
  /// <summary>
  /// Joins the harvest. It must stay a line break: the scan splits the held text back apart with
  /// <c>EnumerateLines</c>, which knows nothing about this constant.
  /// </summary>
  private const string Separator = "\n";

  private readonly Invoker inv;

  private readonly TimeSpan matchBudget;

  /// <summary>
  /// Per assembly: its display name, its type names still joined as the harvest returned them, and
  /// why it could not be read (null when it was).
  /// Holding the joined text rather than a split array is what keeps a search from allocating
  /// anything until it matches something.
  /// </summary>
  private readonly Dictionary<AssemblyMirror, HeldAssembly> held = new();

  /// <param name="matchBudget">
  /// How long one search may spend matching before it is reported as a runaway pattern.
  /// </param>
  public TypeCatalog(Invoker inv, TimeSpan? matchBudget = null) {
    this.inv = inv;
    this.matchBudget = matchBudget ?? TimeSpan.FromSeconds(2);
  }

  /// <summary>
  /// Assemblies harvested since this catalog was built. It grows only when the debuggee loads an
  /// assembly the catalog has not seen, which is what separates a search that re-harvests nothing
  /// from one that re-harvests everything. The held count cannot tell those apart, since a
  /// re-harvest overwrites in place.
  /// </summary>
  public int AssembliesHarvested { get; private set; }

  /// <summary>
  /// Matches an unanchored regex against every harvested full name, case-insensitively (a plain
  /// fragment therefore behaves as a substring search).
  /// Names whose short name matches rank above names that matched only in the namespace, so
  /// searching a concept puts the type named after it first.
  /// The count is exact; <paramref name="limit"/> caps the listing alone.
  /// </summary>
  public TypeCatalogSearch Search(string pattern, int limit) {
    // Compiled ahead of the harvest, so a typo'd pattern costs an error instead of the freeze.
    var regex = this.Compile(pattern);

    this.Refresh();

    var clock = Stopwatch.StartNew();

    // Every MATCH is materialized and ranked, not only the listed ones, because the count must be
    // exact and the ranking global. A pattern matching a large share of the catalog therefore
    // allocates in proportion to its matches, inside the caller's suspend window; the budget above
    // is what bounds how long that can run.
    var matched = new List<(string FullName, AssemblyMirror Mirror, bool ShortNameMatches)>();

    foreach (var (mirror, assembly) in this.held) {
      foreach (var line in assembly.Types.AsSpan().EnumerateLines()) {
        // A pattern can backtrack catastrophically on ONE name or merely crawl over all of them;
        // the elapsed check catches the second, the regex's own timeout the first.
        if (clock.Elapsed > this.matchBudget) {
          throw Runaway();
        }

        var name = TypeCatalog.FullName(line);

        if (name.IsEmpty || !Matches(name)) {
          continue;
        }

        matched.Add((name.ToString(), mirror, Matches(TypeCatalog.ShortName(name))));
      }
    }

    var ranked = matched.OrderByDescending(m => m.ShortNameMatches)
      .ThenBy(m => m.FullName, StringComparer.Ordinal);

    return new TypeCatalogSearch {
      Count = matched.Count,

      // Only the listed hits are resolved back to live types: the count covers every match, and
      // resolving all of them would spend a lookup per match to describe names nobody sees.
      Hits = ranked.Take(limit)
        .Select(m => new TypeCatalogHit {
            FullName = m.FullName,
            Assembly = this.held[m.Mirror].Name,

            // Resolved through the assembly the name came from, so a full name two assemblies
            // both declare cannot answer with the other one's type.
            Type = m.Mirror.GetType(m.FullName)
          }
        )
        .ToList(),

      Unreadable = this.held.Values.Where(a => a.Error is not null)
        .Select(a => $"{a.Name}: {a.Error}")
        .ToList()
    };

    bool Matches(ReadOnlySpan<char> name) {
      try {
        return regex.IsMatch(name);
      }
      catch (RegexMatchTimeoutException) {
        throw Runaway();
      }
    }

    // The budget is spent two ways: one name backtracking catastrophically, or an ordinary pattern
    // costing too much across every name. Narrowing is the remedy either way -- a pattern carrying
    // a literal prefilters far more names than it matches -- so the message gives the remedy
    // without asserting which cause applied.
    InvalidOperationException Runaway() =>
      new(
        $"the search pattern '{pattern}' was abandoned after " +
        $"{this.matchBudget.TotalSeconds:0.#}s of matching; give it a literal fragment to " +
        "narrow on, and avoid nested quantifiers (e.g. (\\w+)+)"
      );
  }

  /// <summary>
  /// The type's full name, as every other tool spells it.
  /// The harvest renders a generic definition with its type parameters ("Ns.Box`1[T]") where the
  /// full name stops at the arity, so cutting there is what makes the name matched, the name
  /// reported, and the name that resolves all one string.
  /// </summary>
  private static ReadOnlySpan<char> FullName(ReadOnlySpan<char> rendered) {
    var bracket = rendered.IndexOf('[');

    return bracket < 0 ? rendered : rendered[..bracket];
  }

  /// <summary>
  /// The type's own name: what follows the last namespace dot, or the last '+' when it is nested
  /// inside another type.
  /// </summary>
  private static ReadOnlySpan<char> ShortName(ReadOnlySpan<char> fullName) {
    var cut = fullName.LastIndexOfAny('.', '+');

    return cut < 0 ? fullName : fullName[(cut + 1)..];
  }

  private Regex Compile(string pattern) {
    try {
      return new Regex(
        pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        this.matchBudget
      );
    }
    catch (ArgumentException ex) {
      // The runtime's own message names the offending construct and its position, which is more
      // than any rephrasing of ours could say.
      throw new InvalidOperationException($"invalid search pattern: {ex.Message}", ex);
    }
  }

  /// <summary>
  /// Brings the catalog level with the debuggee: one round trip for the live assembly list, then
  /// a harvest of the assemblies alone that are new since last time, so an assembly loaded
  /// mid-session becomes searchable without anyone asking for a refresh.
  /// </summary>
  private void Refresh() {
    var domain = this.inv.Vm.RootDomain;

    // The domain mirror caches its assembly list and drops the cache only on an AssemblyLoad event,
    // which this client never requests; invalidating by hand is what keeps the list live.
    this.inv.Vm.InvalidateAssemblyCaches();

    // Resolved on the first assembly that actually needs harvesting, and shared by the rest of this
    // pass: a settled catalog harvests nothing and so resolves nothing.
    TypeMirror stringType = null;
    MethodMirror join = null;

    foreach (var assembly in domain.GetAssemblies()) {
      if (this.held.ContainsKey(assembly)) {
        continue;
      }

      if (stringType is null) {
        // Straight from corlib, NOT through ResolveType: this is the catalog's own invariant, not
        // a name a caller supplied, and ResolveType's failure tells the caller to fix their name
        // with a search -- which is the call that would be failing.
        // A miss answers null here rather than throwing, so it is named before it reaches a lookup
        // that would report it as an unrelated null.
        stringType = this.inv.Vm.RootDomain.Corlib.GetType("System.String") ??
          throw new InvalidOperationException(
            "the debuggee's corlib does not expose System.String, so the type catalog cannot " +
            "harvest; resolve types by exact name instead"
          );

        // Both parameter types are pinned: Join's char-separator overload takes the same arity and
        // would swallow the separator as a char.
        join = this.inv.FindMethod(stringType, "Join", 2, paramTypes: ["String", "Object[]"]);
      }

      this.held[assembly] = this.Harvest(assembly, stringType, join);
      this.AssembliesHarvested++;
    }
  }

  /// <summary>
  /// Reads one assembly's type names as a single joined string: without the join, naming each of
  /// the thousands of types the returned array holds would cost a round trip per type.
  /// An assembly whose OWN enumeration throws inside the game is held with its reason rather than
  /// left out: that is a property of the assembly, so re-attempting it on every later search would
  /// only re-freeze the game, and letting it escape would cost the whole catalog -- and every
  /// assembly behind it in the domain's order -- instead of only its own names.
  /// The reason travels to the caller, so a hole in the answer is visible rather than silent.
  /// Every other failure propagates UNCACHED, because it describes the moment rather than the
  /// assembly: a main thread still in native code answers NOT_SUSPENDED for whatever is being
  /// harvested when it happens, and holding that would blank the whole catalog for the attach over
  /// a condition the next search would not have hit.
  /// </summary>
  private HeldAssembly Harvest(AssemblyMirror assembly, TypeMirror stringType, MethodMirror join) {
    // Named before anything that can fail, so an unreadable assembly can still say which one it is.
    var name = "<unnamed>";

    try {
      name = assembly.GetName().Name ?? name;

      var types = this.inv.Invoke(assembly.GetAssemblyObject(), "GetTypes");

      var joined = this.inv.InvokeStatic(
        stringType,
        join,
        this.inv.Str(TypeCatalog.Separator),
        types
      ) as StringMirror;

      return new HeldAssembly {
        Name = name,
        Types = joined?.Value ?? ""
      };
    }
    catch (GameException ex) {
      return new HeldAssembly {
        Name = name,
        Types = "",
        Error = ex.Message
      };
    }
  }

  private sealed class HeldAssembly {
    public string Name { get; init; }

    public string Types { get; init; }

    /// <summary>Why this assembly's types could not be read; null when they were.</summary>
    public string Error { get; init; }
  }
}

/// <summary>What one catalog search found: the exact count, and the capped listing.</summary>
public sealed class TypeCatalogSearch {
  /// <summary>Every match, whatever the listing's limit.</summary>
  public int Count { get; init; }

  public IReadOnlyList<TypeCatalogHit> Hits { get; init; }

  /// <summary>
  /// Assemblies whose types could not be read, each with its reason. A search cannot see into
  /// these, so an empty answer means "not found HERE" while any of them are listed.
  /// </summary>
  public IReadOnlyList<string> Unreadable { get; init; }
}

/// <summary>One matched type name, with the live type behind it.</summary>
public sealed class TypeCatalogHit {
  public string FullName { get; init; }

  public string Assembly { get; init; }

  /// <summary>
  /// The live type, for describing kind and members; null when the harvested name no longer
  /// resolves in its assembly.
  /// </summary>
  public TypeMirror Type { get; init; }
}
