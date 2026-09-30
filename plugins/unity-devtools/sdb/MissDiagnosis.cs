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
  /// How many candidate classes the extension lookup resolves and reads: each costs wire commands
  /// for its method list, inside a call that has already failed.
  /// </summary>
  private const int ExtensionClassCap = 10;

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

  /// <summary>
  /// The report for a full name that resolved to nothing, under the spelling the tools take
  /// (a nested type joined to its declaring type with a '+').
  /// </summary>
  public static string TypeNotFound(TypeCatalog catalog, string fullName) {
    return MissDiagnosis.Type(catalog, fullName.Split('.'), MissNames.Tool);
  }

  /// <summary>
  /// Why a dotted name resolved to no type, and what it could have meant.
  /// The longest leading run of segments that is a real namespace decides the cause: past it, the
  /// next segment is the one that failed; with none, the root itself is what nothing knows.
  /// Exact simple-name matches are looked up for every segment from the failed one on, since a
  /// mistyped namespace leaves the type's own name intact further right.
  /// Containing matches are looked up for ONE segment, the one most likely to be the type: the
  /// last of a name written where a type is expected, the failed one of a chain, whose later
  /// segments are members.
  /// May run the catalog's first harvest.
  /// </summary>
  /// <param name="chainScope">
  /// Non-null when the name is an expression chain, which can also start on a local and continue
  /// into members: the locals and builtins its root could have been.
  /// Null for a name written where only a type is expected.
  /// </param>
  public static string Type(
    TypeCatalog catalog,
    IReadOnlyList<string> segments,
    MissNames names,
    IReadOnlyList<string> chainScope = null
  ) {
    var isChain = chainScope is not null;
    var known = catalog.NamespaceDepth(segments);
    var whole = string.Join(".", segments);

    if (known == segments.Count) {
      return isChain
        ? $"'{whole}' is a namespace, not a type or a value"
        : $"type '{whole}' not found: it is a namespace";
    }

    var failed = segments[known];
    var exact = segments.Skip(known).ToList();
    var contained = isChain ? failed : segments[^1];

    var candidates = catalog.Names(n =>
      MissSuggestions.Matches(MissSuggestions.SimpleName(n), exact, contained)
    );

    var found = MissSuggestions.RankTypes(candidates, exact, contained);

    var head = isChain
      ? $"cannot resolve '{string.Join(".", segments.Take(known + 1))}'"
      : $"type '{whole}' not found";

    // Read off the names already fetched: the failed segment is one of the exact ones asked for.
    // Case counts in a chain, where it does not for a suggestion: a root differing from a type by
    // case alone is far more often a local that is gone than a type left unqualified.
    var casing = isChain ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    var isBareTypeName = candidates.Any(n =>
      MissSuggestions.SimpleName(n.AsSpan()).Equals(failed, casing)
    );

    var inScope = chainScope is { Count: > 0 }
      ? $"; in scope: {string.Join(", ", chainScope)}"
      : "";

    string cause;

    if (known > 0) {
      cause = $"namespace '{string.Join(".", segments.Take(known))}' exists but holds no type " +
        $"or namespace named '{failed}'";
    }
    else if (isBareTypeName) {
      cause = "type names must be fully qualified";
    }
    else if (segments.Count > 1) {
      cause = $"'{failed}' is not a namespace{(isChain ? ", a type or a local" : " or a type")}" +
        inScope;
    }
    else if (isChain) {
      cause = $"not a local, a builtin or a type{inScope}";
    }
    else {
      cause = "no type has that name";
    }

    string suggestions;

    if (found.Total is 0) {
      suggestions = $"no loaded type is named '{contained}' or contains it in its name " +
        "(find_types with search matches a regex against every full name)";
    }
    else {
      var listed = names is MissNames.Eval
        ? found.Listed.Select(MissSuggestions.EvalName)
        : found.Listed;

      suggestions = $"did you mean: {string.Join(", ", listed)}";

      if (found.Total > found.Listed.Count) {
        suggestions += $" ({found.Listed.Count} of {found.Total} shown: find_types with search " +
          "lists them all)";
      }
    }

    return $"{head}: {cause}; {suggestions}";
  }

  /// <summary>
  /// A call no method of the receiver's type answers, diagnosed by what the type does have: the
  /// name on the wrong side, the name wanting a type argument, the name at other arities, or no
  /// such name at all.
  /// Only that last case can be an extension method, so only it looks for one, and may run the
  /// catalog's first harvest doing so.
  /// </summary>
  /// <param name="catalog">
  /// Null skips the extension lookup, leaving its static form stated as a shape.
  /// </param>
  public static string Method(Invoker inv, TypeCatalog catalog, TypeMirror type, MissedCall call) {
    var named = MissDiagnosis.Chain(type)
      .SelectMany(t => t.GetMethods())
      .Where(m => m.Name == call.Name)
      .ToList();

    if (named.Count is 0) {
      return MissDiagnosis.UnknownMethod(inv, catalog, type, call);
    }

    if (call.OnType && named.All(m => !m.IsStatic)) {
      return $"{call.Name} is an instance method of {type.FullName}: call it on a value of that " +
        "type, not on the type itself";
    }

    var signatures = MissSuggestions.Listing(
      [("it has", named.Select(MissDiagnosis.Signature).Distinct().ToList())],
      MissSuggestions.SignatureCap,
      MissDiagnosis.MemberPointer
    );

    if (call.TypeArgCount > 0) {
      return $"no {call.Name} of {type.FullName} takes {call.TypeArgCount} type argument(s) " +
        $"with {call.ArgCount} argument(s); {signatures}";
    }

    var generic = named.FirstOrDefault(m =>
      m.IsGenericMethodDefinition && m.GetParameters().Length == call.ArgCount
    );

    return generic is not null
      ? $"{call.Name} is generic and its type argument is not inferred: write it, as in " +
      MissDiagnosis.Signature(generic)
      : $"no {call.Name} of {type.FullName} takes {call.ArgCount} argument(s); {signatures}";
  }

  private static string UnknownMethod(
    Invoker inv,
    TypeCatalog catalog,
    TypeMirror type,
    MissedCall call
  ) {
    var rest = call.ArgCount > 0 ? ", ..." : "";

    if (!call.OnType &&
      catalog is not null &&
      MissDiagnosis.FindExtension(inv, catalog, type, call.Name) is {} extension) {
      var more = extension.GetParameters().Length > 1 ? ", ..." : "";
      var declaring = MissSuggestions.EvalName(extension.DeclaringType.FullName);

      return $"{type.FullName} has no method '{call.Name}', but {call.Name} is an extension " +
        $"method: call it as {declaring}.{call.Name}({call.Receiver}{more})";
    }

    var side = call.OnType ? "static " : "";

    // Most-derived first, so what every object inherits comes last and is what a cut drops.
    var names = MissDiagnosis.Chain(type)
      .SelectMany(t => t.GetMethods())
      .Where(m => m.IsStatic == call.OnType && m.IsPublic && !m.IsSpecialName)
      .Select(m => m.Name)
      .ToList();

    var listing = MissDiagnosis.MemberListing(call.Name, ($"{side}methods", names));

    var message = $"{type.FullName} has no {side}method '{call.Name}'; {listing}";

    return call.OnType
      ? message
      : $"{message}; if {call.Name} is an extension method, call its static form, " +
      $"DeclaringClass.{call.Name}({call.Receiver}{rest}) (find_types with search finds the class)";
  }

  /// <summary>
  /// Looks for the static method behind an extension call, without an index of methods to look in:
  /// a bounded guess at where such a method is conventionally declared, a class named
  /// "...Extensions" or "...Utils" that either carries the receiver's name or shares its namespace.
  /// Classes naming the receiver are read first, so the cap drops the looser guesses.
  /// </summary>
  private static MethodMirror FindExtension(
    Invoker inv,
    TypeCatalog catalog,
    TypeMirror receiver,
    string name
  ) {
    var receiverName = MissSuggestions.SimpleName(receiver.FullName);
    var inNamespace = $"{receiver.Namespace}.";

    var classes = catalog.Names(fullName => {
          var simple = MissSuggestions.SimpleName(fullName);

          if (!simple.EndsWith("Extensions", StringComparison.Ordinal) &&
            !simple.EndsWith("Utils", StringComparison.Ordinal)) {
            return false;
          }

          return simple.Contains(receiverName, StringComparison.Ordinal) ||
            (fullName.StartsWith(inNamespace, StringComparison.Ordinal) &&
              fullName.Length == inNamespace.Length + simple.Length);
        }
      )
      .OrderByDescending(n =>
        MissSuggestions.SimpleName(n.AsSpan()).Contains(receiverName, StringComparison.Ordinal)
      )
      .ThenBy(n => n, StringComparer.Ordinal)
      .Take(MissDiagnosis.ExtensionClassCap);

    foreach (var fullName in classes) {
      var found = inv.FindTypeOrNull(fullName)
        ?.GetMethods()
        .FirstOrDefault(m =>
          m.Name == name &&
          m.IsStatic &&
          m.GetParameters() is { Length: > 0 } parameters &&
          parameters[0].ParameterType.IsAssignableFrom(receiver)
        );

      if (found is not null) {
        return found;
      }
    }

    return null;
  }

  private static string Signature(MethodMirror method) {
    var typeParameters = method.IsGenericMethodDefinition
      ? $"<{string.Join(", ", method.GetGenericArguments().Select(t => t.Name))}>"
      : "";

    var parameters = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));

    return $"{(method.IsStatic ? "static " : "")}{method.Name}{typeParameters}({parameters})";
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

/// <summary>A call that bound to no method, as the caller wrote it.</summary>
/// <param name="OnType">The receiver is a type, so only a static method can answer.</param>
/// <param name="Receiver">
/// The receiver's own text where it is a plain name chain, a placeholder otherwise: what the
/// static form of an extension call takes as its first argument.
/// </param>
public sealed record MissedCall(
  string Name,
  int ArgCount,
  int TypeArgCount,
  bool OnType,
  string Receiver
);

/// <summary>Which spelling a suggested type name is printed under.</summary>
public enum MissNames {
  /// <summary>The evaluator's: a nested type follows its declaring type after a dot.</summary>
  Eval,

  /// <summary>Every other tool's: the runtime full name, a nested type after a '+'.</summary>
  Tool
}
