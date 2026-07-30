using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using Mono.Debugger.Soft;
using SdbThreadMirror = Mono.Debugger.Soft.ThreadMirror;

namespace UnityDevtools.Sdb;

/// <summary>
/// Mirror-level plumbing shared by the ECS commands: type/method resolution, method invocation on
/// any mirror kind, value construction, and value formatting.
/// All invokes run on the game's main thread (thread-safety: ECS writes from another thread could
/// trip the Entities safety system mid-frame); this holds during breakpoint pauses too, because a
/// suspended main thread still parks at a managed safe point where invokes work (frame-context
/// evaluation reads/writes frame slots via plain wire commands, which need no invoke thread).
/// </summary>
public sealed class Invoker(VirtualMachine vm) {
  public VirtualMachine Vm { get; } = vm;

  public SdbThreadMirror MainThread { get; } = Invoker.PickMainThread(vm);

  private static SdbThreadMirror PickMainThread(VirtualMachine vm) {
    var threads = vm.GetThreads();

    // Unity's main thread is the first attached thread; its name is empty in player builds ("Main
    // Thread" in some editor builds).
    return threads.FirstOrDefault(t => t.Name is "Main Thread") ??
      threads.Where(t => string.IsNullOrEmpty(t.Name)).OrderBy(t => t.Id).FirstOrDefault() ??
      threads.OrderBy(t => t.Id).First();
  }

  /// <summary>
  /// Resolves a type by full name across the whole debuggee, case-insensitively.
  /// The name is NOT a unique handle: two assemblies can declare the same full name, and matching
  /// loosely on case widens that further, so this answers the FIRST of the matches.
  /// A caller that derived the name from a type it already held has therefore not proven it got
  /// that type back, and must keep whatever check guards what it does next.
  /// </summary>
  public TypeMirror ResolveType(string fullName) {
    var types = this.Vm.GetTypes(fullName, true);

    if (types.Count is 0) {
      throw new InvalidOperationException(
        $"type '{fullName}' not found (use a fully-qualified name; see find-types)"
      );
    }

    return types[0];
  }

  /// <summary>
  /// Concurrent because one Invoker serves BOTH the session's tool operations and the debug pump
  /// thread, which evaluates breakpoint conditions outside the session's lock (see
  /// <see cref="DebugController" />). Same for <see cref="methodCache" />.
  /// </summary>
  private readonly ConcurrentDictionary<string, TypeMirror> typeCache = new();

  /// <summary>
  /// Case-sensitive type lookup (unlike <see cref="ResolveType" />, matching C# name semantics)
  /// with the evaluator's nested-type fallback: dotted names retry with '+' separators from the
  /// right (C# syntax has no '+', runtime names do).
  /// Hits are cached for the lifetime of this attach; misses are NOT, because the debuggee loads
  /// assemblies over time and a type can become resolvable later in the same session.
  /// Matching on case narrows the name collision <see cref="ResolveType" /> describes without
  /// closing it, so the same caveat applies: the answer is the first match, not a proven identity.
  /// </summary>
  public TypeMirror FindTypeOrNull(string dotted) {
    if (this.typeCache.TryGetValue(dotted, out var cached)) {
      return cached;
    }

    var candidate = dotted;

    while (true) {
      var types = this.Vm.GetTypes(candidate, false);

      if (types.Count > 0) {
        this.typeCache[dotted] = types[0];

        return types[0];
      }

      var lastDot = candidate.LastIndexOf('.');

      if (lastDot < 0) {
        return null;
      }

      candidate = $"{candidate[..lastDot]}+{candidate[(lastDot + 1)..]}";
    }
  }

  /// <summary>
  /// Finds a method by name and arity, walking the base-type chain.
  /// With <paramref name="genericArity" /> &gt; 0, only generic method definitions with that many
  /// type parameters match.
  /// <paramref name="paramTypes" /> disambiguates overloads by parameter type name, position by
  /// position (e.g. ["Entity"], ["Type", "Int32"]); a null entry matches any.
  /// </summary>
  public MethodMirror FindMethod(
    TypeMirror type,
    string name,
    int argc,
    int genericArity = 0,
    string[] paramTypes = null
  ) {
    return this.FindMethodOrNull(type, name, argc, genericArity, paramTypes) ??
      throw new InvalidOperationException(
        $"method {type.Name}.{name}/{argc}{(genericArity > 0 ? $"<{genericArity}>" : "")} not found"
      );
  }

  private readonly ConcurrentDictionary<(TypeMirror, string, int, int, string), MethodMirror>
    methodCache = new();

  /// <summary>
  /// Non-throwing counterpart of <see cref="FindMethod" /> (same matching rules), returning null
  /// when nothing matches: the way to probe whether the target's API carries a member before
  /// calling it, since the versions this plugin drives are not inferable from assembly metadata.
  /// Hits AND misses are memoized for the lifetime of this attach, since a loaded type never grows
  /// or loses a method. That saves the repeated scan, not round trips: the mirror caches its own
  /// method list, so only the first lookup on a type reaches the debuggee either way.
  /// </summary>
  public MethodMirror FindMethodOrNull(
    TypeMirror type,
    string name,
    int argc,
    int genericArity = 0,
    string[] paramTypes = null
  ) {
    var key = (type, name, argc, genericArity, string.Join("|", paramTypes ?? []));

    if (this.methodCache.TryGetValue(key, out var cached)) {
      return cached;
    }

    var found = Invoker.SearchMethod(type, name, argc, genericArity, paramTypes);

    this.methodCache[key] = found;

    return found;
  }

  private static MethodMirror SearchMethod(
    TypeMirror type,
    string name,
    int argc,
    int genericArity,
    string[] paramTypes
  ) {
    for (var t = type; t is not null; t = t.BaseType) {
      foreach (var m in t.GetMethods()) {
        if (m.Name != name || m.GetParameters().Length != argc) {
          continue;
        }

        switch (genericArity) {
          case 0 when m.IsGenericMethodDefinition:
          case > 0 when !m.IsGenericMethodDefinition ||
            m.GetGenericArguments().Length != genericArity:
            continue;
        }

        if (paramTypes is not null &&
          paramTypes.Where((p, i) => p is not null && m.GetParameters()[i].ParameterType.Name != p)
            .Any()) {
          continue;
        }

        return m;
      }
    }

    return null;
  }

  /// <summary>
  /// Finds every non-generic method matching name and arity, derived-first, so a caller can pick
  /// the overload whose signature accepts its arguments.
  /// </summary>

  // CA1822 (mark static): kept an instance member on purpose. It is part of Invoker's cohesive
  // invoke abstraction and is called as `this.inv.FindMethods(...)` across files; making it static
  // would churn every call site for no real gain.
  [SuppressMessage("Performance", "CA1822", Justification = "Cohesive instance API")]
  public List<MethodMirror> FindMethods(TypeMirror type, string name, int argc) {
    var matches = new List<MethodMirror>();

    for (var t = type; t is not null; t = t.BaseType) {
      matches.AddRange(
        t.GetMethods()
          .Where(m =>
            m.Name == name && m.GetParameters().Length == argc && !m.IsGenericMethodDefinition
          )
      );
    }

    return matches;
  }

  /// <summary>
  /// Runs an <c>invoke</c>, unwrapping a debuggee-side throw into a <see cref="GameException" />
  /// that names the exception the game itself raised.
  /// Every invoke made on a tool's behalf lands here, which is what makes that naming reach every
  /// tool alike; only the plugin's own reads of a thrown object go through <see cref="Retrying" />
  /// bare, so that describing a throw cannot recurse into describing another.
  /// </summary>
  private T Invoking<T>(Func<T> invoke) {
    try {
      return this.Retrying(invoke);
    }
    catch (Exception ex) when (GameException.InvocationIn(ex) is {} invocation) {
      throw this.DescribeThrow(invocation.Exception, ex);
    }
  }

  /// <summary>
  /// Retries while the agent reports NOT_SUSPENDED: right after attach the main thread can still be
  /// in native engine code, and it only parks at a suspendable safe point once it re-enters managed
  /// code during the frame.
  /// </summary>

  // CA1822: instance member by design (see FindMethods); part of the invoke plumbing.
  [SuppressMessage("Performance", "CA1822", Justification = "Cohesive instance API")]
  private T Retrying<T>(Func<T> invoke) {
    for (var attempt = 0;; attempt++) {
      try {
        return invoke();
      }
      catch (VMNotSuspendedException) when (attempt < 20) {
        Thread.Sleep(50);
      }
    }
  }

  /// <summary>
  /// Names the exception the game threw.
  /// Reading the thrown object costs wire reads made while already reporting a failure, so they are
  /// best-effort: letting a second failure escape would leave the caller knowing nothing at all
  /// about the throw.
  /// A dropped connection is the one exception -- it invalidates the whole session and must stay
  /// recognizable to <see cref="UnitySession" />.
  /// </summary>
  private GameException DescribeThrow(ObjectMirror thrown, Exception cause) {
    string typeName = null;

    try {
      typeName = thrown.Type.FullName;
    }
    catch (Exception ex) when (!UnitySession.IsDisconnect(ex)) {
      // Best-effort, like the message: an unnamed throw is still reported as one.
    }

    return new GameException(typeName, this.ReadMessageOrNull(thrown), cause);
  }

  /// <summary>
  /// Reads an exception mirror's Message, null when it cannot be read.
  /// The exception's type alone is already actionable, so a failure here is swallowed -- except a
  /// dropped connection, which the session must still see.
  /// </summary>
  public string ReadMessageOrNull(ObjectMirror thrown) {
    try {
      var getter = this.FindMethodOrNull(thrown.Type, "get_Message", 0);

      return this.Retrying(() =>
        (thrown.InvokeMethod(this.MainThread, getter, []) as StringMirror)?.Value
      );
    }
    catch (Exception ex) when (!UnitySession.IsDisconnect(ex)) {
      return null;
    }
  }

  /// <summary>
  /// Static invoke that also returns out-parameter values
  /// (<see cref="InvokeOptions.ReturnOutArgs"/>).
  /// Pass placeholder values (defaults) for the out parameters.
  /// </summary>
  public InvokeResult InvokeStaticWithOutArgs(
    TypeMirror type,
    MethodMirror method,
    params Value[] args
  ) {
    return this.Invoking(() => type.EndInvokeMethodWithResult(
        type.BeginInvokeMethod(
          this.MainThread,
          method,
          args,
          InvokeOptions.ReturnOutArgs,
          null,
          null
        )
      )
    );
  }

  /// <summary>
  /// Instance invoke that also returns out-parameter values (see
  /// <see cref="InvokeStaticWithOutArgs"/>); pass placeholder values for the out parameters.
  /// Struct receivers additionally request <see cref="InvokeOptions.ReturnOutThis"/>: the
  /// vendored EndInvokeMethodWithResult writes the post-call fields back into the receiver
  /// mirror, so a mutating struct method behaves like C# on the caller's variable.
  /// </summary>
  public InvokeResult InvokeWithOutArgs(Value target, MethodMirror method, params Value[] args) {
    return this.Invoking(() => target switch {
        ObjectMirror o => o.EndInvokeMethodWithResult(
          o.BeginInvokeMethod(
            this.MainThread,
            method,
            args,
            InvokeOptions.ReturnOutArgs,
            null,
            null
          )
        ),
        StructMirror s => s.EndInvokeMethodWithResult(
          s.BeginInvokeMethod(
            this.MainThread,
            method,
            args,
            InvokeOptions.ReturnOutArgs | InvokeOptions.ReturnOutThis,
            null,
            null
          )
        ),
        _ => throw new InvalidOperationException(
          $"cannot invoke with out args on {target.GetType().Name}"
        )
      }
    );
  }

  /// <summary>Constructs a debuggee-side instance through the given constructor.</summary>
  public Value NewInstance(TypeMirror type, MethodMirror ctor, params Value[] args) =>
    this.Invoking(() => type.NewInstance(this.MainThread, ctor, args));

  /// <summary>
  /// Invokes an instance method on whatever mirror kind the target is.
  /// Struct receivers request <see cref="InvokeOptions.ReturnOutThis"/>: the vendored
  /// EndInvokeMethodWithResult writes the post-call fields back into the receiver mirror, so
  /// mutating struct methods and property setters behave like C# on the caller's variable.
  /// </summary>
  public Value Invoke(Value target, MethodMirror method, params Value[] args) {
    return this.Invoking(() => target switch {
        ObjectMirror o => o.InvokeMethod(this.MainThread, method, args),
        StructMirror s => s.EndInvokeMethodWithResult(
            s.BeginInvokeMethod(
              this.MainThread,
              method,
              args,
              InvokeOptions.ReturnOutThis,
              null,
              null
            )
          )
          .Result,
        PrimitiveValue p => p.InvokeMethod(this.MainThread, method, args),
        _ => throw new InvalidOperationException($"cannot invoke on {target.GetType().Name}")
      }
    );
  }

  public Value Invoke(Value target, string method, params Value[] args) =>
    this.Invoke(target, this.FindMethod(this.TypeOf(target), method, args.Length), args);

  public Value InvokeStatic(TypeMirror type, string method, params Value[] args) =>
    this.InvokeStatic(type, this.FindMethod(type, method, args.Length), args);

  public Value InvokeStatic(TypeMirror type, MethodMirror method, params Value[] args) =>
    this.Invoking(() => type.InvokeMethod(this.MainThread, method, args));

  /// <summary>Reads a property through its getter (works on all mirror kinds).</summary>
  public Value GetProperty(Value target, string name) =>
    this.Invoke(target, this.FindMethod(this.TypeOf(target), $"get_{name}", 0));

  public Value GetStaticProperty(TypeMirror type, string name) =>
    this.InvokeStatic(type, this.FindMethod(type, $"get_{name}", 0));

  // CA1822: instance member by design (see FindMethods); called via this.inv.TypeOf cross-file.
  [SuppressMessage("Performance", "CA1822", Justification = "Cohesive instance API")]
  public TypeMirror TypeOf(Value v) {
    return v switch {
      ObjectMirror o => o.Type,
      StructMirror s => s.Type,
      _ => throw new InvalidOperationException($"no type mirror for {v.GetType().Name}")
    };
  }

  public PrimitiveValue Prim(object value) => this.Vm.CreateValue(value);

  public StringMirror Str(string s) => this.Vm.RootDomain.CreateString(s);

  /// <summary>
  /// The nesting depth every tool that REPORTS a value to the caller renders at, so a component
  /// read one way is legible exactly as deep as the same component read another way.
  /// Deep enough for the nested vector and bounds structs game components are built from; anything
  /// past it is a pointer chase the caller should ask for on purpose.
  /// </summary>
  public const int ReadDepth = 3;

  /// <summary>Renders a mirrored value as text; structs and boxed structs list fields.</summary>
  public string Format(Value v, int depth = 2) {
    switch (v) {
      case null: return "null";

      case PrimitiveValue p:
        return p.Value is IFormattable f
          ? f.ToString(null, CultureInfo.InvariantCulture)
          : p.Value?.ToString() ?? "null";

      case StringMirror s: return $"\"{s.Value}\"";

      case EnumMirror e: return $"{e.Type.Name}.{e.StringValue}";

      case ArrayMirror a: return $"{a.Type.FullName}[{a.Length}]";

      case StructMirror st: return this.FormatFields(st.Type, st.Fields, depth);

      case ObjectMirror o when o.Type.IsValueType: {
        // Boxed struct: read its instance fields off the heap object.
        var fields = Invoker.InstanceFields(o.Type);

        return this.FormatFields(o.Type, o.GetValues(fields), depth);
      }

      case ObjectMirror o: return $"{o.Type.FullName}#{o.Address}";

      default: return v.ToString();
    }
  }

  private string FormatFields(TypeMirror type, Value[] values, int depth) {
    if (depth <= 0) {
      return $"{type.Name} {{...}}";
    }

    var fields = Invoker.InstanceFields(type);

    var parts = fields.Select((f, i) =>
      $"{f.Name}={this.Format(i < values.Length ? values[i] : null, depth - 1)}"
    );

    return $"{type.Name} {{ {string.Join(", ", parts)} }}";
  }

  public static FieldInfoMirror[] InstanceFields(TypeMirror type) =>
    type.GetFields().Where(f => !f.IsStatic).ToArray();

  /// <summary>Comma-joined instance field names, for member-not-found error messages.</summary>
  public static string InstanceFieldNames(TypeMirror type) =>
    string.Join(", ", Invoker.InstanceFields(type).Select(f => f.Name));
}
