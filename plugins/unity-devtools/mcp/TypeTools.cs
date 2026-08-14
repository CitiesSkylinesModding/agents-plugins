using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using JetBrains.Annotations;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Mono.Debugger.Soft;
using UnityDevtools.Sdb;

namespace UnityDevtools.Mcp;

/// <summary>
/// Type reflection tools: resolve live types by name or by pattern and inspect their members
/// (invocation lives in the eval tool).
/// </summary>
[McpServerToolType]
[UsedImplicitly]
public sealed class TypeTools(UnitySession session) {
  /// <summary>
  /// How many matches still earn a member listing: a search that names a handful of candidates is
  /// answering "which of these", where a broad one would bury the answer in signatures.
  /// </summary>
  private const int MembersMatchLimit = 5;

  /// <summary>
  /// The most types one search will list. Resolving a listed hit costs a round trip, and they are
  /// all spent inside the suspend window the call holds, so an unbounded listing would freeze the
  /// game for as long as the pattern happened to match.
  /// </summary>
  private const int MaxListing = 500;

  [McpServerTool(Name = "find_types")]
  [Description(
    """
    Find types in the running game and report where they live: fullName resolves one exact name,
    search matches a regular expression against every type name loaded in the process.
    """
  )]
  [UsedImplicitly]
  public FindTypesResult FindTypes(
    [Description(
      """
      Exact fully-qualified name, case-insensitive (e.g. MyGame.Citizens.Citizen): one cheap lookup,
      for a name you already hold rather than one you are composing.
      A name a search returned is already in this form and pastes straight in.
      """
    )]
    string? fullName = null,
    [Description(
      """
      .NET regular expression matched unanchored and case-insensitively against every loaded type's
      full name, so a plain fragment behaves as a substring search: how you find a type you can only
      describe (a concept like "pathfinding"), rather than name.
      Write the pattern yourself; a name you are pasting belongs in fullName, because type names
      carry metacharacters of their own (a nested type renders as Outer+Inner, and '+' means
      something else in a regex).
      Matching runs against the same full names the results report, so an anchored pattern lands
      where you expect: a generic definition is MyGame.Caching.Cache`1, arity and no more.
      The first search of a session harvests every loaded assembly and freezes the game for a second
      or two; later ones match a cached list and freeze it only briefly.
      """
    )]
    string? search = null,
    [Description(
      """
      Also list fields, properties, and methods; with search, served only while few types match, so
      a loose pattern cannot return thousands of signatures.
      """
    )]
    bool members = false,
    [Description(
      """
      Caps the search listing; count is always exact and omitted says how many were left out.
      Capped server-side at 500 (at 5 when members is set), so past that, narrow the pattern rather
      than raising this.
      """
    )]
    int limit = 50
  ) {
    var exact = !string.IsNullOrEmpty(fullName);
    var searching = !string.IsNullOrEmpty(search);

    if (exact == searching) {
      throw new McpException(
        "give exactly one of fullName (one exact name, cheap) or search (a regex over every " +
        "loaded type name)"
      );
    }

    return ToolGuard.Run(() => session.Run(Operation));

    FindTypesResult Operation(SdbContext ctx) =>
      exact
        ? TypeTools.Exact(ctx, fullName!, members)
        : TypeTools.Matching(ctx, search!, members, limit);
  }

  private static FindTypesResult Exact(SdbContext ctx, string fullName, bool members) {
    var types = ctx.Vm.GetTypes(fullName, true);

    if (types.Count is 0) {
      throw new McpException(
        $"type '{fullName}' not found (fullName takes the exact fully-qualified name; when all " +
        "you have is a fragment or a concept, pass it to search instead)"
      );
    }

    return new FindTypesResult {
      Query = fullName,
      Count = types.Count,
      Omitted = 0,
      Types = types.Select(t =>
          TypeTools.Describe(t, t.FullName, Invoker.SimpleAssemblyName(t.Assembly), members)
        )
        .ToArray()
    };
  }

  private static FindTypesResult Matching(SdbContext ctx, string search, bool members, int limit) {
    // A member listing is refused past MembersMatchLimit anyway, so asking for more than that many
    // would only resolve types the refusal below is about to throw away.
    var listing = Math.Clamp(
      limit,
      0,
      members ? TypeTools.MembersMatchLimit : TypeTools.MaxListing
    );

    var found = ctx.Types.Search(search, listing);

    if (members && found.Count > TypeTools.MembersMatchLimit) {
      throw new McpException(
        $"the pattern matches {found.Count} types and members is served up to " +
        $"{TypeTools.MembersMatchLimit}; tighten the pattern, or name the one type you want with " +
        "fullName"
      );
    }

    return new FindTypesResult {
      Query = search,
      Count = found.Count,
      Omitted = found.Count - found.Hits.Count,

      Types = found.Hits
        .Select(h => TypeTools.Describe(h.Type, h.FullName, h.Assembly, members))
        .ToArray(),

      // Surfaced only when there is a hole: an empty result means "no such type" when nothing is
      // listed here, and "not in what could be read" when something is.
      IncompleteAssemblies = found.Incomplete.Count is 0
        ? null
        : found.Incomplete
          .Select(a => new IncompleteAssemblyDescription {
              Assembly = a.Name,
              Partial = a.IsPartial,
              Reason = a.Reason
            }
          )
          .ToArray()
    };
  }

  private static TypeDescription Describe(
    TypeMirror? type,
    string fullName,
    string assembly,
    bool members
  ) {
    if (type is null) {
      // A searched name whose type no longer resolves is still reported: that the type exists is
      // most of what the search was for, and everything else about it needs the live mirror.
      return new TypeDescription {
        FullName = fullName,
        Assembly = assembly,
        Kind = "unknown"
      };
    }

    return new TypeDescription {
      FullName = fullName,
      Assembly = assembly,

      Kind = type.IsValueType
        ? "struct"
        : type.IsInterface
          ? "interface"
          : "class",

      Fields = members
        ? type.GetFields()
          .Where(f => !f.IsStatic)
          .Select(f => $"{f.Name}: {f.FieldType.FullName}")
          .ToArray()
        : null,

      Properties = members
        ? type.GetProperties().Select(p => $"{p.Name}: {p.PropertyType.FullName}").ToArray()
        : null,

      Methods = members
        ? type.GetMethods()
          .Select(m => {
              var pars = string.Join(
                ", ",
                m.GetParameters().Select(x => $"{x.ParameterType.Name} {x.Name}")
              );

              return $"{m.ReturnType.Name} {m.Name}({pars})";
            }
          )
          .ToArray()
        : null
    };
  }
}

/// <summary>Result of the <c>find_types</c> tool.</summary>
public sealed record FindTypesResult {
  public required string Query { [UsedImplicitly] get; init; }

  /// <summary>Every match, whatever the listing's limit.</summary>
  public required int Count { [UsedImplicitly] get; init; }

  /// <summary>Matches the limit left out of <see cref="Types"/>.</summary>
  public required int Omitted { [UsedImplicitly] get; init; }

  public required IReadOnlyList<TypeDescription> Types { [UsedImplicitly] get; init; }

  /// <summary>
  /// Assemblies the search saw incompletely; absent when every assembly was read whole.
  /// "No match" only means "not in what could be read" while any are listed.
  /// </summary>
  public IReadOnlyList<IncompleteAssemblyDescription>? IncompleteAssemblies {
    [UsedImplicitly] get;
    init;
  }
}

/// <summary>An assembly the search could not read whole, and how much of it is missing.</summary>
public sealed record IncompleteAssemblyDescription {
  public required string Assembly { [UsedImplicitly] get; init; }

  /// <summary>
  /// True when some of its types were still read and were searched, false when none of them could
  /// be read at all. It says what was searchable, NOT what matched: a true here with no hit from
  /// this assembly means your pattern missed the names that were read, where a false means those
  /// names were never available to match.
  /// </summary>
  public required bool Partial { [UsedImplicitly] get; init; }

  /// <summary>
  /// Why the listing is incomplete: the game's own words when it threw enumerating this assembly,
  /// or the debugger's when the enumeration could not be made at all.
  /// </summary>
  public required string Reason { [UsedImplicitly] get; init; }
}

/// <summary>One resolved type; member lists are present only when requested.</summary>
public sealed record TypeDescription {
  public required string FullName { [UsedImplicitly] get; init; }

  public required string Assembly { [UsedImplicitly] get; init; }

  /// <summary>
  /// "struct", "interface", "class", or "unknown" when the searched name no longer resolves.
  /// </summary>
  public required string Kind { [UsedImplicitly] get; init; }

  public IReadOnlyList<string>? Fields { [UsedImplicitly] get; init; }

  public IReadOnlyList<string>? Properties { [UsedImplicitly] get; init; }

  public IReadOnlyList<string>? Methods { [UsedImplicitly] get; init; }
}
