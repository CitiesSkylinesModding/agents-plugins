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
  /// why its listing has a hole (null when it has none).
  /// Holding the joined text rather than a split array is what keeps a search from allocating
  /// anything until it matches something.
  /// </summary>
  private readonly Dictionary<AssemblyMirror, HeldAssembly> held = new();

  /// <summary>
  /// The debuggee's <c>string.Join</c>, resolved on the first assembly that actually needs
  /// harvesting and kept for the attach, like every mirror here: a settled catalog harvests nothing
  /// and so resolves nothing.
  /// </summary>
  private MethodMirror join;

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

        // An assembly holding no types at all still enumerates as one empty line.
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

      Incomplete = this.held.Values.Where(a => a.Reason is not null)
        .Select(a => new IncompleteAssembly {
            Name = a.Name,

            // Claimed on what came back, not on which failure it was: an assembly whose types ALL
            // failed to load throws the same partial type-load and recovers nothing, and calling
            // that "partially read" would tell a caller to stop looking here.
            IsPartial = a.Types.Length > 0,
            Reason = a.Reason
          }
        )
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

    foreach (var assembly in domain.GetAssemblies()) {
      if (this.held.ContainsKey(assembly)) {
        continue;
      }

      this.join ??= this.ResolveJoin();

      this.held[assembly] = this.Harvest(assembly);
      this.AssembliesHarvested++;
    }
  }

  /// <summary>
  /// Resolves the debuggee's <c>string.Join(string, object[])</c>.
  /// Both parameter types are pinned: Join's char-separator overload takes the same arity and would
  /// swallow the separator as a char.
  /// The type comes straight from corlib, NOT through ResolveType: this is the catalog's own
  /// invariant, not a name a caller supplied, and ResolveType's failure tells the caller to fix
  /// their name with a search -- which is the call that would be failing.
  /// A miss answers null there rather than throwing, so it is named here before it reaches a lookup
  /// that would report it as an unrelated null.
  /// </summary>
  private MethodMirror ResolveJoin() {
    var stringType = this.inv.Vm.RootDomain.Corlib.GetType("System.String") ??
      throw new InvalidOperationException(
        "the debuggee's corlib does not expose System.String, so the type catalog cannot " +
        "harvest; resolve types by exact name instead"
      );

    return this.inv.FindMethod(stringType, "Join", 2, paramTypes: ["String", "Object[]"]);
  }

  /// <summary>
  /// Reads one assembly's type names, as far as the game will give them.
  /// An assembly whose OWN enumeration throws inside the game is held with its reason -- and with
  /// whatever names the throw still carries -- rather than left out: that is a property of the
  /// assembly, so re-attempting it on every later search would only re-freeze the game, and letting
  /// it escape would cost the whole catalog -- and every assembly behind it in the domain's order
  /// -- instead of only its own names.
  /// The reason travels to the caller, so a hole in the answer is visible rather than silent.
  /// Every other failure propagates UNCACHED, because it describes the moment rather than the
  /// assembly: a main thread still in native code answers NOT_SUSPENDED for whatever is being
  /// harvested when it happens, and holding that would blank the whole catalog for the attach over
  /// a condition the next search would not have hit.
  /// </summary>
  private HeldAssembly Harvest(AssemblyMirror assembly) {
    // Named before anything that can fail, so an unreadable assembly can still say which one it is.
    var name = "<unnamed>";

    try {
      name = Invoker.SimpleAssemblyName(assembly);

      return new HeldAssembly {
        Name = name,
        Types = this.Join(this.inv.Invoke(assembly.GetAssemblyObject(), "GetTypes"))
      };
    }

    // Everything that is not a passing condition, not only an in-game throw: the agent answers a
    // per-assembly error code for an assembly whose id no longer decodes, and those describe the
    // assembly every bit as much as a throw does.
    // ONE catch, with the recovery inside it rather than in a filtered clause of its own: an
    // exception raised in a catch clause does not reach that clause's siblings, so a recovery that
    // failed in turn would escape with the whole catalog behind it.
    catch (Exception ex) when (!TypeCatalog.DescribesTheMoment(ex)) {
      return new HeldAssembly {
        Name = name,
        Types = ex is GameException game ? this.RecoverPartialLoad(game) : "",
        Reason = ex.Message
      };
    }
  }

  /// <summary>
  /// The names a partial type-load left readable, empty when the failure carries none.
  /// A partial type-load is the failure a mod author is likeliest to hit and likeliest to be
  /// exploring: their assembly loaded, and only the types reaching for something this build of the
  /// game no longer has failed. Two further invokes recover the rest.
  /// Best-effort by construction: the assembly is already held with the reason it failed, so a
  /// recovery that fails in turn costs the names it was adding and nothing more. A dropped
  /// connection is the one exception -- it invalidates the whole session and must stay recognizable
  /// to <see cref="UnitySession" />.
  /// </summary>
  private string RecoverPartialLoad(GameException failure) {
    if (failure.Thrown is not {} thrown) {
      return "";
    }

    try {
      // Named off the MIRROR, not off the failure's best-effort TypeName: that copy is null
      // whenever reading it failed, which would strand exactly the assembly this exists for, and a
      // read that failed once can succeed here.
      // Matched exactly rather than probed for a Types member: any number of exceptions could
      // carry something called Types, and rendering one of those would file whatever it holds
      // under this assembly's name as searchable types.
      if (thrown.Type.FullName is not "System.Reflection.ReflectionTypeLoadException") {
        return "";
      }

      // Nothing guarantees the array is there either: the exception carries whatever it was
      // constructed with, and Join would hand a null straight back to the game to throw on.
      // A debuggee-side null arrives MIRRORED, not as a C# null, so testing for one is not enough.
      var types = this.inv.GetProperty(thrown, "Types");

      var joined = types is null or PrimitiveValue { Value: null } ? "" : this.Join(types);

      // The gaps go here, once, rather than at every later match: it makes the held text exactly
      // the names that loaded, so "did anything load at all" is just "is it empty" -- which a run
      // of separators, all that is left of an array where everything failed, would answer wrong.
      return string.Join(
        TypeCatalog.Separator,
        joined.Split(TypeCatalog.Separator, StringSplitOptions.RemoveEmptyEntries)
      );
    }
    catch (Exception ex) when (!TypeCatalog.DescribesTheMoment(ex)) {
      return "";
    }
  }

  /// <summary>
  /// Whether a failure describes the MOMENT rather than the thing being read: a main thread still
  /// in native code, a mirror collected under us, the client out of memory, a dropped connection.
  /// The recovery swallows everything else -- it is an optional extra on top of an assembly already
  /// held with its reason, so it may not cost more than the names it was adding -- but holding one
  /// of these would blank the assembly for the whole attach over a condition the next search would
  /// not have hit.
  /// </summary>
  private static bool DescribesTheMoment(Exception failure) =>
    failure is VMNotSuspendedException or ObjectCollectedException or OutOfMemoryException ||
    UnitySession.IsDisconnect(failure);

  /// <summary>
  /// Renders an array of types as one separated string: without the join, naming each of the
  /// thousands of types such an array holds would cost a round trip per type.
  /// A slot the debuggee failed to load renders as an empty entry, wherever in the array it sits.
  /// </summary>
  private string Join(Value types) {
    var joined = this.inv.InvokeStatic(
      this.join.DeclaringType,
      this.join,
      this.inv.Str(TypeCatalog.Separator),
      types
    ) as StringMirror;

    return joined?.Value ?? "";
  }

  private sealed class HeldAssembly {
    public string Name { get; init; }

    public string Types { get; init; }

    /// <summary>
    /// What the game said when this assembly's types were enumerated; null when they were, whole.
    /// Whether ANY of them came back is <see cref="Types" /> being non-empty -- the recovery drops
    /// the gaps a partial listing carries, so nothing else has to be stored to tell the two apart.
    /// </summary>
    public string Reason { get; init; }
  }
}

/// <summary>What one catalog search found: the exact count, and the capped listing.</summary>
public sealed class TypeCatalogSearch {
  /// <summary>Every match, whatever the listing's limit.</summary>
  public int Count { get; init; }

  public IReadOnlyList<TypeCatalogHit> Hits { get; init; }

  /// <summary>
  /// Assemblies whose type listing has a hole. A search sees less than everything in these, so an
  /// empty answer means "not found in what could be read" while any of them are listed.
  /// </summary>
  public IReadOnlyList<IncompleteAssembly> Incomplete { get; init; }
}

/// <summary>An assembly the catalog could not read whole, and how much of it is missing.</summary>
public sealed class IncompleteAssembly {
  public string Name { get; init; }

  /// <summary>
  /// Whether some of its types did load and were therefore searched: a recovered partial type-load
  /// contributes what it has, where every other failure leaves the assembly with nothing.
  /// It says what was SEARCHABLE, not what matched.
  /// </summary>
  public bool IsPartial { get; init; }

  /// <summary>
  /// Why the listing is incomplete: the game's own words when it threw enumerating this assembly,
  /// or this client's when the enumeration could not be made at all.
  /// </summary>
  public string Reason { get; init; }
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
