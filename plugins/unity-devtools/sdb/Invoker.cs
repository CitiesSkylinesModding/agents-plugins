using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Mono.Debugger.Soft;
using SdbThreadMirror = Mono.Debugger.Soft.ThreadMirror;

namespace UnityDevtools.Sdb;

/// <summary>
/// Mirror-level plumbing shared by the ECS commands: type/method resolution, method invocation on
/// any mirror kind, value construction, and value formatting.
/// All invokes run on the game's main thread (thread-safety: ECS writes from another thread could
/// trip the Entities safety system mid-frame).
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

  public TypeMirror ResolveType(string fullName) {
    var types = this.Vm.GetTypes(fullName, true);

    if (types.Count == 0) {
      throw new InvalidOperationException(
        $"type '{fullName}' not found (use a fully-qualified name; see find-types)"
      );
    }

    return types[0];
  }

  private readonly Dictionary<string, TypeMirror> typeCache = [];

  /// <summary>
  /// Case-sensitive type lookup (unlike <see cref="ResolveType" />, matching C# name semantics)
  /// with the evaluator's nested-type fallback: dotted names retry with '+' separators from the
  /// right (C# syntax has no '+', runtime names do).
  /// Hits are cached for the lifetime of this attach; misses are NOT, because the debuggee loads
  /// assemblies over time and a type can become resolvable later in the same session.
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

  // CA1822 (mark static): kept an instance member on purpose. It is part of Invoker's cohesive
  // invoke abstraction and is called as `this.inv.FindMethod(...)` across files; making it static
  // would churn every call site for no real gain.
  [SuppressMessage("Performance", "CA1822", Justification = "Cohesive instance API")]
  public MethodMirror FindMethod(
    TypeMirror type,
    string name,
    int argc,
    int genericArity = 0,
    string[] paramTypes = null
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

    throw new InvalidOperationException(
      $"method {type.Name}.{name}/{argc}{(genericArity > 0 ? $"<{genericArity}>" : "")} not found"
    );
  }

  /// <summary>
  /// Finds every non-generic method matching name and arity, derived-first, so a caller can pick
  /// the overload whose signature accepts its arguments.
  /// </summary>

  // CA1822: instance member by design (see FindMethod); part of the invoke plumbing.
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
  /// Runs an <c>invoke</c>, retrying while the agent reports NOT_SUSPENDED: right after attach the
  /// main thread can still be in native engine code, and it only parks at a suspendable safe point
  /// once it re-enters managed code during the frame.
  /// </summary>

  // CA1822: instance member by design (see FindMethod); part of the invoke plumbing.
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
  /// Static invoke that also returns out-parameter values
  /// (<see cref="InvokeOptions.ReturnOutArgs"/>).
  /// Pass placeholder values (defaults) for the out parameters.
  /// </summary>
  public InvokeResult InvokeStaticWithOutArgs(
    TypeMirror type,
    MethodMirror method,
    params Value[] args
  ) {
    return this.Retrying(() => type.EndInvokeMethodWithResult(
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
    return this.Retrying(() => target switch {
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
    this.Retrying(() => type.NewInstance(this.MainThread, ctor, args));

  /// <summary>
  /// Invokes an instance method on whatever mirror kind the target is.
  /// Struct receivers request <see cref="InvokeOptions.ReturnOutThis"/>: the vendored
  /// EndInvokeMethodWithResult writes the post-call fields back into the receiver mirror, so
  /// mutating struct methods and property setters behave like C# on the caller's variable.
  /// </summary>
  public Value Invoke(Value target, MethodMirror method, params Value[] args) {
    return this.Retrying(() => target switch {
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
    this.Retrying(() => type.InvokeMethod(this.MainThread, method, args));

  /// <summary>Reads a property through its getter (works on all mirror kinds).</summary>
  public Value GetProperty(Value target, string name) =>
    this.Invoke(target, this.FindMethod(this.TypeOf(target), $"get_{name}", 0));

  public Value GetStaticProperty(TypeMirror type, string name) =>
    this.InvokeStatic(type, this.FindMethod(type, $"get_{name}", 0));

  // CA1822: instance member by design (see FindMethod); called via this.inv.TypeOf cross-file.
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
