using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>
/// What a name the debuggee does not have is answered with: the actual cause, and the names the
/// caller could have meant, so the retry is the next call.
/// Every wording, list and cap a miss reports lives here, shared by the evaluator and the tools
/// taking a name from their caller.
/// Nothing here substitutes a near match: a miss still fails, and the caller picks the name.
/// </summary>
public static class MissDiagnosis {
  private const string MemberPointer = "find_types with fullName and members lists them all";

  /// <summary>
  /// A member name an instance does not have, reading or writing.
  /// The list covers what the lookup itself walks, the whole base chain, so it never omits a name
  /// that would have resolved.
  /// </summary>
  /// <param name="fields">
  /// False for a receiver whose fields the caller cannot reach (a primitive read through its
  /// getters alone).
  /// </param>
  public static string InstanceMember(
    TypeMirror type,
    string name,
    bool writing,
    bool fields = true
  ) {
    if (MissDiagnosis.FindStatic(type, name) is {} declaring) {
      var spelled = $"{MissSuggestions.EvalName(declaring.FullName)}.{name}";

      return $"'{name}' is a static member, not one of a {type.FullName} value: " +
        $"{(writing ? "write" : "read")} it as {spelled}";
    }

    var (properties, fieldNames) = MissDiagnosis.Members(type, isStatic: false, writing);

    var what = (writing, fields) switch {
      (true, _) => "a writable field or property",
      (false, true) => "a field or readable property",
      (false, false) => "a readable property"
    };

    var listing = MissDiagnosis.MemberListing(
      name,
      (writing ? "writable properties" : "properties", properties),
      ("fields", fields ? fieldNames : [])
    );

    return $"'{name}' is not {what} of {type.FullName}; {listing}";
  }

  /// <summary>A member name a type does not have on its static side.</summary>
  public static string StaticMember(TypeMirror type, string name, bool writing) {
    if (MissDiagnosis.HasInstance(type, name, writing)) {
      return $"'{name}' is an instance member of {type.FullName}: " +
        $"{(writing ? "write" : "read")} it on a value of that type, not on the type itself";
    }

    // A static field is looked up on the type written alone, where a static property's accessor
    // is found along the base chain.
    var declaring = MissDiagnosis.Chain(type.BaseType)
      .FirstOrDefault(t => t.GetFields().Any(f => f.Name == name && f.IsStatic));

    if (declaring is not null) {
      var spelled = $"{MissSuggestions.EvalName(declaring.FullName)}.{name}";

      return $"'{name}' is a static field of {declaring.FullName}, and a derived type's name " +
        $"does not reach it: {(writing ? "write" : "read")} it as {spelled}";
    }

    var (properties, fields) = MissDiagnosis.Members(type, isStatic: true, writing);

    var what = writing
      ? "a writable static field or property"
      : "a static field or readable property";

    var listing = MissDiagnosis.MemberListing(
      name,
      (writing ? "writable static properties" : "static properties", properties),
      ("static fields", fields)
    );

    return $"'{name}' is not {what} of {type.FullName}; {listing}";
  }

  /// <summary>
  /// A member name a value held client-side (a string, a number) does not have.
  /// Public members alone: those are what the client-side read reflects over.
  /// </summary>
  public static string ClientMember(Type type, string name) {
    const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

    var listing = MissDiagnosis.MemberListing(
      name,
      (
        "properties",
        type.GetProperties(flags)
          .Where(p => p.GetIndexParameters().Length is 0)
          .Select(p => p.Name)
          .ToList()
      ),
      ("fields", type.GetFields(flags).Select(f => f.Name).ToList())
    );

    return $"'{name}' is not a member of {type.FullName}; {listing}";
  }

  /// <summary>
  /// A field name a type does not have, for the tools that write fields and nothing else: a
  /// property in the list would name something they cannot set.
  /// The type's own fields alone, as their lookup reads no base.
  /// </summary>
  public static string Field(TypeMirror type, string name) {
    var listing = MissDiagnosis.MemberListing(
      name,
      (
        "fields",
        Invoker.InstanceFields(type)
          .Select(f => f.Name)
          .Where(MissDiagnosis.IsSpellable)
          .ToList()
      )
    );

    return $"'{name}' is not a field of {type.FullName}; {listing}";
  }

  /// <summary>
  /// The capped member list of a miss: within each group, names containing the missed one lead,
  /// so a near guess on a type with many members is answered ahead of the cut.
  /// </summary>
  private static string MemberListing(
    string missed,
    params (string Label, IReadOnlyList<string> Names)[] groups
  ) {
    return MissSuggestions.Listing(
      groups.Select(g => (g.Label, MissSuggestions.ContainingFirst(g.Names, missed))).ToList(),
      MissSuggestions.MemberCap,
      MissDiagnosis.MemberPointer
    );
  }

  private static IEnumerable<TypeMirror> Chain(TypeMirror type) {
    for (var t = type; t is not null; t = t.BaseType) {
      yield return t;
    }
  }

  /// <summary>
  /// The members a lookup on this side could have found, most-derived first.
  /// Properties are read off the accessor methods the lookup already fetched rather than off a
  /// property enumeration of their own; only the accessors' parameters are asked for on top, which
  /// is what tells a property from an indexer.
  /// A name no expression can spell (a compiler-generated backing field, an explicit interface
  /// implementation) is left out: it could not be the one the caller meant.
  /// Static fields stop at the type itself, as the static lookup does.
  /// </summary>
  private static (IReadOnlyList<string> Properties, IReadOnlyList<string> Fields) Members(
    TypeMirror type,
    bool isStatic,
    bool writing
  ) {
    var prefix = writing ? "set_" : "get_";
    var arity = writing ? 1 : 0;

    var properties = new List<string>();
    var fields = new List<string>();

    // Names are compared first throughout: they are already fetched, where a method's staticness
    // and parameters each cost a command the first time they are asked for.
    foreach (var t in MissDiagnosis.Chain(type)) {
      if (!isStatic || t == type) {
        fields.AddRange(
          t.GetFields()
            .Where(f => f.IsStatic == isStatic && MissDiagnosis.IsSpellable(f.Name))
            .Select(f => f.Name)
        );
      }

      properties.AddRange(
        t.GetMethods()
          .Where(m =>
            m.Name.StartsWith(prefix, StringComparison.Ordinal) &&
            MissDiagnosis.IsSpellable(m.Name) &&
            m.IsStatic == isStatic &&
            m.GetParameters().Length == arity
          )
          .Select(m => m.Name[prefix.Length..])
      );
    }

    return (properties, fields);
  }

  /// <summary>The type declaring a static member of that name, along the base chain.</summary>
  private static TypeMirror FindStatic(TypeMirror type, string name) {
    return MissDiagnosis.Chain(type)
      .FirstOrDefault(t =>
        t.GetFields().Any(f => f.Name == name && f.IsStatic) ||
        t.GetMethods()
          .Any(m => (m.Name == $"get_{name}" || m.Name == $"set_{name}") && m.IsStatic)
      );
  }

  /// <summary>
  /// Whether the instance lookup would have found that name: a field, or a property's accessor
  /// (an indexer's takes more parameters), anywhere along the base chain.
  /// </summary>
  private static bool HasInstance(TypeMirror type, string name, bool writing) {
    var accessor = $"{(writing ? "set_" : "get_")}{name}";
    var arity = writing ? 1 : 0;

    return MissDiagnosis.Chain(type)
      .Any(t =>
        t.GetFields().Any(f => f.Name == name && !f.IsStatic) ||
        t.GetMethods()
          .Any(m => m.Name == accessor && !m.IsStatic && m.GetParameters().Length == arity)
      );
  }

  private static bool IsSpellable(string name) => !name.Contains('<') && !name.Contains('.');
}
