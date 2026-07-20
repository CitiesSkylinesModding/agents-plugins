using System.Globalization;
using Microsoft.CSharp.RuntimeBinder;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb.Eval;

/// <summary>
/// Walks a parsed program against the live VM, interpreting each construct as mirror primitives
/// over <see cref="Invoker" /> (the way IDE debuggers evaluate over SDB, which has no
/// expression-evaluation command).
/// Values in flight are either SDB mirrors or client-side CLR primitives/strings; operators run
/// client-side, member reads/writes, and calls run through mirrors.
/// Instances are per-evaluation: build one inside a suspend window and discard it.
/// </summary>
public sealed class EvalInterpreter(Invoker inv, IReadOnlyList<IEvalScope> scopes) {
  /// <summary>Locals in declaration order (failure reports list them as declared).</summary>
  private readonly OrderedDictionary<string, object> locals = [];

  /// <summary>Enum constants resolved this evaluation (each costs several wire round-trips).</summary>
  private readonly Dictionary<string, EnumMirror> enumConstants = [];

  /// <summary>Receiver stack for `?.` chains (the tested value binds the member hole).</summary>
  private readonly Stack<object> receivers = new();

  public EvalOutcome Run(EvalProgram program, EvalState state) {
    object last = null;

    for (var i = 0; i < program.Statements.Count; i++) {
      var statement = program.Statements[i];

      try {
        last = statement switch {
          VarStatement declaration => this.ExecuteVar(declaration),
          ExprStatement expr => this.Evaluate(expr.Expression),
          _ => throw new EvalRuntimeException($"unknown statement {statement.GetType().Name}")
        };
      }
      catch (Exception ex) {
        throw this.Fail(program, i, ex);
      }
    }

    // Formatting can itself hit the wire (e.g., rendering a collected `_` mirror), so it stays
    // inside the failure dressing, and `_` only updates once the result proved renderable.
    EvalOutcome outcome;

    try {
      outcome = new EvalOutcome {
        Formatted = this.FormatValue(last, 3),
        TypeName = EvalInterpreter.TypeNameOf(last)
      };
    }
    catch (Exception ex) {
      throw this.Fail(program, program.Statements.Count - 1, ex);
    }

    state.Store(last);

    return outcome;
  }

  private object ExecuteVar(VarStatement declaration) {
    if (this.locals.ContainsKey(declaration.Name)) {
      throw new EvalRuntimeException(
        $"local '{declaration.Name}' is already declared",
        declaration.Position
      );
    }

    var value = this.Evaluate(declaration.Value);

    this.locals[declaration.Name] = value;

    return value;
  }

  // ---- Expression dispatch ----

  private object Evaluate(EvalExpr expr) {
    var result = this.EvaluateAllowingType(expr);

    if (result is TypeRef typeRef) {
      throw new EvalRuntimeException(
        $"'{typeRef.Type.FullName}' is a type, not a value (use typeof(...) for the Type object)",
        expr.Position
      );
    }

    return result;
  }

  /// <summary>
  /// Evaluates an expression, allowing a name chain to resolve to a bare type (for call targets
  /// and static member access); everything else yields a value.
  /// </summary>
  private object EvaluateAllowingType(EvalExpr expr) {
    switch (expr) {
      case LiteralExpr literal: return literal.Value;

      case NameExpr or MemberExpr: return this.EvaluateChain(expr);

      case ImplicitReceiverExpr: return this.receivers.Peek();

      case CallExpr call: return this.EvaluateCall(call);

      case IndexExpr index: return this.EvaluateIndex(index);

      case NewExpr creation: return this.EvaluateNew(creation);

      case CastExpr cast: return this.EvaluateCast(cast);

      case TypeofExpr typeOf:
        return this.ResolveType(typeOf.TypeName, typeOf.Position).GetTypeObject();

      case UnaryExpr unary: {
        var operand = this.Evaluate(unary.Operand);

        var raw = PrimitiveOps.Unary(unary.Op, UnwrapForOps(operand));

        // `~` is the one unary operator C# defines on (flags) enums; it keeps the enum type.
        return unary.Op == "~" && operand is EnumMirror e ? this.MakeEnum(e.Type, raw) : raw;
      }

      case BinaryExpr binary: return this.EvaluateBinary(binary);

      case ConditionalExpr conditional:
        return this.EvaluateBool(conditional.Condition)
          ? this.Evaluate(conditional.WhenTrue)
          : this.Evaluate(conditional.WhenFalse);

      case ConditionalAccessExpr access: {
        var target = this.Evaluate(access.Target);

        if (EvalInterpreter.IsNull(target)) {
          return null;
        }

        this.receivers.Push(target);

        try {
          return this.Evaluate(access.WhenNotNull);
        }
        finally {
          this.receivers.Pop();
        }
      }

      case InterpolatedStringExpr interpolated:
        return string.Concat(interpolated.Parts.Select(p => this.Stringify(this.Evaluate(p))));

      case AssignExpr assignment: return this.EvaluateAssign(assignment);

      default: throw new EvalRuntimeException($"unknown node {expr.GetType().Name}", expr.Position);
    }
  }

  private object EvaluateBinary(BinaryExpr binary) {
    switch (binary.Op) {
      // Lazy operators: laziness is evaluation order, so it lives here, not in PrimitiveOps.
      case "&&": return this.EvaluateBool(binary.Left) && this.EvaluateBool(binary.Right);

      case "||": return this.EvaluateBool(binary.Left) || this.EvaluateBool(binary.Right);

      case "??": {
        var left = this.Evaluate(binary.Left);

        return EvalInterpreter.IsNull(left) ? this.Evaluate(binary.Right) : left;
      }

      default: {
        var left = this.Evaluate(binary.Left);
        var right = this.Evaluate(binary.Right);

        if (left is EnumMirror || right is EnumMirror) {
          return this.EvaluateEnumBinary(binary, left, right);
        }

        // String concatenation with a struct/object operand renders the mirror the way
        // interpolation does; the dynamic binder would concatenate the client wrapper's ToString()
        // instead.
        if (binary.Op is "+" &&
          (EvalInterpreter.IsCompoundMirror(left) || EvalInterpreter.IsCompoundMirror(right)) &&
          (UnwrapForOps(left) is string || UnwrapForOps(right) is string)) {
          return this.Stringify(left) + this.Stringify(right);
        }

        if (binary.Op is "==" or "!=" &&
          (EvalInterpreter.IsCompoundMirror(left) || EvalInterpreter.IsCompoundMirror(right))) {
          return this.EvaluateMirrorEquality(binary, left, right);
        }

        return PrimitiveOps.Binary(binary.Op, UnwrapForOps(left), UnwrapForOps(right));
      }
    }
  }

  /// <summary>Mirror kinds whose operators cannot be computed from an unwrapped value.</summary>
  private static bool IsCompoundMirror(object value) =>
    value is StructMirror and not EnumMirror or ObjectMirror and not StringMirror;

  /// <summary>
  /// Operators over enum operands: same-enum comparisons compare underlying values, bitwise ops
  /// keep the enum type (flags math), and cross-enum operands are rejected.
  /// A numeric operand joins on the underlying value, looser than C# (which only admits the
  /// literal zero) but REPL-friendly.
  /// </summary>
  private object EvaluateEnumBinary(BinaryExpr binary, object left, object right) {
    // String concatenation renders the member name (as interpolation does), not the number.
    if (binary.Op is "+" && (UnwrapForOps(left) is string || UnwrapForOps(right) is string)) {
      return this.Stringify(left) + this.Stringify(right);
    }

    var bothEnums = left is EnumMirror && right is EnumMirror;

    if (bothEnums && ((EnumMirror) left).Type.FullName != ((EnumMirror) right).Type.FullName) {
      throw new EvalRuntimeException(
        $"cannot apply '{binary.Op}' to different enum types " +
        $"{((EnumMirror) left).Type.FullName} and {((EnumMirror) right).Type.FullName}",
        binary.Position
      );
    }

    var enumType = (left as EnumMirror ?? (EnumMirror) right).Type;

    var raw = PrimitiveOps.Binary(binary.Op, UnwrapForOps(left), UnwrapForOps(right));

    // Flags math keeps the enum; so do +/- against a numeric offset (enum minus enum is the
    // underlying distance, as in C#); comparisons fall through as bool.
    return binary.Op switch {
      "&" or "|" or "^" => this.MakeEnum(enumType, raw),
      "+" or "-" when !bothEnums => this.MakeEnum(enumType, raw),
      _ => raw
    };
  }

  /// <summary>
  /// C# `==`/`!=` for struct/object operands: the debuggee's own equality decides
  /// (op_Equality/op_Inequality when declared, reference identity for plain objects, Equals for
  /// operator-less structs); client wrapper identity never does.
  /// </summary>
  private object EvaluateMirrorEquality(BinaryExpr binary, object left, object right) {
    var wantEqual = binary.Op is "==";

    // At least one operand is a live mirror, so a null on the other side can never match.
    if (EvalInterpreter.IsNull(left) || EvalInterpreter.IsNull(right)) {
      return !wantEqual;
    }

    var leftType = this.MirrorTypeOf(left, binary.Position);
    var rightType = this.MirrorTypeOf(right, binary.Position);

    var operatorName = wantEqual ? "op_Equality" : "op_Inequality";

    // C# considers operators declared on either operand's type.
    var operators = inv.FindMethods(leftType, operatorName, 2);

    if (rightType != leftType) {
      operators = [.. operators, .. inv.FindMethods(rightType, operatorName, 2)];
    }

    if (operators.Count > 0) {
      (MethodMirror Method, Value[] Values)? bound;

      try {
        bound = this.SelectOverload(operators, [left, right], operatorName, binary.Position);
      }
      catch (EvalRuntimeException) {
        // The declared operator rejects these operands; the defaults below decide.
        bound = null;
      }

      if (bound is {} b) {
        return UnwrapForOps(inv.InvokeStatic(b.Method.DeclaringType, b.Method, b.Values));
      }
    }

    if (left is ObjectMirror leftObject && right is ObjectMirror rightObject) {
      // No declared operator on a reference type: C# compares references.
      var same = leftObject.Address == rightObject.Address;

      return wantEqual ? same : !same;
    }

    // Operator-less structs: C# would reject `==` outright; Equals is the debugger-friendly
    // extension (field-wise via ValueType.Equals unless overridden).
    var (equalsMethod, equalsValues) = this.SelectOverload(
      inv.FindMethods(leftType, "Equals", 1),
      [right],
      $"{leftType.Name}.Equals",
      binary.Position
    );

    var equal = UnwrapForOps(
      inv.Invoke(this.ToMirror(left, binary.Position), equalsMethod, equalsValues)
    ) is true;

    return wantEqual ? equal : !equal;
  }

  private bool EvaluateBool(EvalExpr expr) {
    var value = UnwrapForOps(this.Evaluate(expr));

    return value is bool b
      ? b
      : throw new EvalRuntimeException(
        $"expected a bool, got {EvalInterpreter.TypeNameOf(value)}",
        expr.Position
      );
  }

  // ---- Name and member chains ----

  /// <summary>
  /// Evaluates a NameExpr/MemberExpr chain: locals and scopes bind the root first; otherwise the
  /// longest dotted prefix naming a live type wins, and the remaining segments are member reads.
  /// </summary>
  private object EvaluateChain(EvalExpr expr) {
    if (!EvalInterpreter.TryFlattenChain(expr, out var segments)) {
      // The chain hangs off a non-name target (a call, an indexer, ...): plain member reads.
      var member = (MemberExpr) expr;

      return this.ReadMember(
        this.EvaluateAllowingType(member.Target),
        member.Name,
        member.Position
      );
    }

    var (current, next) = this.ResolveChainRoot(segments);

    for (var i = next; i < segments.Count; i++) {
      current = this.ReadMember(current, segments[i].Name, segments[i].Position);
    }

    return current;
  }

  /// <summary>
  /// Resolves a flattened chain's root: locals and scopes bind the first segment; otherwise the
  /// longest dotted prefix naming a live type wins (nested types spelled with dots resolve
  /// through the '+' fallback).
  /// Returns the root value and the index of the first unconsumed segment.
  /// </summary>
  private (object Root, int Next) ResolveChainRoot(List<(string Name, int Position)> segments) {
    var (rootName, rootPosition) = segments[0];

    if (this.locals.TryGetValue(rootName, out var local)) {
      return (local, 1);
    }

    if (this.TryScopes(rootName, out var scoped)) {
      return (scoped, 1);
    }

    for (var take = segments.Count; take >= 1; take--) {
      var candidate = string.Join(".", segments.Take(take).Select(s => s.Name));

      var type = this.ResolveTypeOrNull(candidate);

      if (type is not null) {
        return (new TypeRef(type), take);
      }
    }

    throw new EvalRuntimeException(
      $"cannot resolve '{rootName}': not a local, a builtin (em, world, entity(), _), or " +
      "the start of a fully-qualified type name",
      rootPosition
    );
  }

  private static bool TryFlattenChain(
    EvalExpr expr,
    out List<(string Name, int Position)> segments
  ) {
    segments = [];

    var reversed = new List<(string, int)>();

    for (var node = expr;; node = ((MemberExpr) node).Target) {
      switch (node) {
        case NameExpr name:
          reversed.Add((name.Name, name.Position));
          reversed.Reverse();
          segments.AddRange(reversed);

          return true;

        case MemberExpr member:
          reversed.Add((member.Name, member.Position));

          continue;

        default: return false;
      }
    }
  }

  private bool TryScopes(string name, out object value) {
    foreach (var scope in scopes) {
      if (scope.TryResolveValue(name, out value)) {
        return true;
      }
    }

    value = null;

    return false;
  }

  // ---- Member reads ----

  private object ReadMember(object target, string name, int position) {
    switch (target) {
      case TypeRef typeRef: return this.ReadStaticMember(typeRef.Type, name, position);

      case null: throw new EvalRuntimeException($"cannot read '{name}' on null", position);

      case ArrayMirror array when name == "Length": return array.Length;

      case StructMirror or ObjectMirror:
        return this.ReadInstanceMember((Value) target, name, position);

      case PrimitiveValue primitive:
        return this.ReadProperty(primitive, this.MirrorTypeOf(primitive, position), name, position);

      case Value other:
        throw new EvalRuntimeException($"cannot read '{name}' on {other.GetType().Name}", position);

      default:
        // Client-side value (string, number, ...): reflect locally, no wire round-trip.
        return EvalInterpreter.ReadClientMember(target, name, position);
    }
  }

  private Value ReadProperty(Value target, TypeMirror type, string name, int position) {
    MethodMirror getter;

    try {
      getter = inv.FindMethod(type, $"get_{name}", 0);
    }
    catch (InvalidOperationException) {
      throw new EvalRuntimeException(
        $"'{name}' is not a field or readable property of {type.FullName}; fields: " +
        Invoker.InstanceFieldNames(type),
        position
      );
    }

    return inv.Invoke(target, getter);
  }

  /// <summary>
  /// Finds a field or accessor method for an instance member, most-derived first: each level of
  /// the type chain checks its own field then accessor before its base, so a `new` member
  /// shadows a base one, as in C#. At most one of the pair is non-null.
  /// </summary>
  private static (FieldInfoMirror Field, MethodMirror Accessor) FindInstanceMemberSlot(
    TypeMirror type,
    string name,
    string accessorName,
    int accessorArity
  ) {
    for (var t = type; t is not null; t = t.BaseType) {
      var field = t.GetFields().FirstOrDefault(f => !f.IsStatic && f.Name == name);

      if (field is not null) {
        return (field, null);
      }

      var accessor = t.GetMethods()
        .FirstOrDefault(m =>
          !m.IsStatic && m.Name == accessorName && m.GetParameters().Length == accessorArity
        );

      if (accessor is not null) {
        return (null, accessor);
      }
    }

    return (null, null);
  }

  /// <summary>Reads a field or property on an instance mirror, most-derived first.</summary>
  private Value ReadInstanceMember(Value mirror, string name, int position) {
    var type = inv.TypeOf(mirror);

    var (field, getter) = EvalInterpreter.FindInstanceMemberSlot(type, name, $"get_{name}", 0);

    if (field is not null) {
      return mirror is StructMirror structMirror
        ? structMirror[name]
        : ((ObjectMirror) mirror).GetValue(field);
    }

    if (getter is not null) {
      return inv.Invoke(mirror, getter);
    }

    throw new EvalRuntimeException(
      $"'{name}' is not a field or readable property of {type.FullName}; fields: " +
      Invoker.InstanceFieldNames(type),
      position
    );
  }

  /// <summary>Writes a field or property on an instance mirror, most-derived first.</summary>
  private void WriteInstanceMember(Value mirror, string name, object value, int position) {
    var type = inv.TypeOf(mirror);

    var (field, setter) = EvalInterpreter.FindInstanceMemberSlot(type, name, $"set_{name}", 1);

    if (field is not null) {
      var coerced = this.CoerceToType(value, field.FieldType, position);

      if (mirror is StructMirror structMirror) {
        structMirror[name] = coerced;
      }
      else {
        ((ObjectMirror) mirror).SetValue(field, coerced);
      }

      return;
    }

    if (setter is null) {
      throw new EvalRuntimeException(
        $"'{name}' is not a writable field or property of {type.FullName}",
        position
      );
    }

    inv.Invoke(
      mirror,
      setter,
      this.CoerceToType(value, setter.GetParameters()[0].ParameterType, position)
    );
  }

  private object ReadStaticMember(TypeMirror type, string name, int position) {
    var field = type.GetFields().FirstOrDefault(f => f.IsStatic && f.Name == name);

    if (field is not null) {
      return type.IsEnum ? this.EnumMemberValue(type, name) : type.GetValue(field);
    }

    MethodMirror getter;

    try {
      getter = inv.FindMethod(type, $"get_{name}", 0);
    }
    catch (InvalidOperationException) {
      throw new EvalRuntimeException(
        $"'{name}' is not a static field or property of {type.FullName}",
        position
      );
    }

    // Outside the catch: a getter that throws is a real invoke failure, not a resolution miss.
    return inv.InvokeStatic(type, getter);
  }

  private static object ReadClientMember(object target, string name, int position) {
    var type = target.GetType();

    var property = type.GetProperty(name);

    if (property is not null) {
      return property.GetValue(target);
    }

    var field = type.GetField(name);

    return field is not null
      ? field.GetValue(target)
      : throw new EvalRuntimeException($"'{name}' is not a member of {type.FullName}", position);
  }

  /// <summary>
  /// Materializes an enum constant as an EnumMirror. Enum constants are literal fields with no
  /// static storage, so the value is obtained via a debuggee-side Enum.Parse + Convert instead of
  /// a static-field read.
  /// </summary>
  private EnumMirror EnumMemberValue(TypeMirror enumType, string name) {
    var key = $"{enumType.FullName}.{name}";

    if (this.enumConstants.TryGetValue(key, out var memoized)) {
      return memoized;
    }

    var enumClass = this.ResolveType("System.Enum", -1);

    var boxed = inv.InvokeStatic(
      enumClass,
      inv.FindMethod(enumClass, "Parse", 2, paramTypes: ["Type", "String"]),
      enumType.GetTypeObject(),
      inv.Str(name)
    );

    // Convert through the enum's actual underlying type, so unsigned 64-bit constants survive
    // (a signed pivot would overflow above long.MaxValue).
    var underlying = EvalInterpreter.ClrPrimitive(enumType.EnumUnderlyingType.FullName);

    var convert = this.ResolveType("System.Convert", -1);

    var numeric = (PrimitiveValue) inv.InvokeStatic(
      convert,
      inv.FindMethod(convert, $"To{underlying.Name}", 1, paramTypes: ["Object"]),
      boxed
    );

    var constant = this.MakeEnum(enumType, numeric.Value);

    this.enumConstants[key] = constant;

    return constant;
  }

  private EnumMirror MakeEnum(TypeMirror enumType, object numeric) {
    // The wire value must match the enum's underlying primitive type exactly; the conversion is
    // the unchecked, truncating cast (C# enum casts wrap rather than range-check, and `~` on a
    // sub-int flags enum yields a negative int that must truncate back).
    var underlying = EvalInterpreter.ClrPrimitive(enumType.EnumUnderlyingType.FullName);

    return inv.Vm.CreateEnumMirror(
      enumType,
      inv.Prim(EvalInterpreter.CastClient(numeric, underlying))
    );
  }

  // ---- Calls ----

  private object EvaluateCall(CallExpr call) {
    if (call.Target is null) {
      var bareArgs = call.Args.Select(a =>
          a.Mode == ArgMode.Plain
            ? UnwrapForOps(this.Evaluate(a.Value))
            : throw new EvalRuntimeException(
              "builtin functions take no out arguments",
              call.Position
            )
        )
        .ToArray();

      foreach (var scope in scopes) {
        if (scope.TryCall(call.Name, bareArgs, out var result)) {
          return result;
        }
      }

      throw new EvalRuntimeException(
        $"unknown function '{call.Name}' (builtins: entity(index, version)); methods need a " +
        "target: a value or a fully-qualified type",
        call.Position
      );
    }

    // The receiver resolves with lvalue tracking: a struct read through a by-copy link (object
    // field, array element, static field) gets its post-call fields replayed back, so mutating
    // methods behave like C# on a variable, not on a discarded temporary.
    var (target, writeBack, _) = this.EvaluateWritable(call.Target);

    var isStatic = target is TypeRef;

    var declaringType = target switch {
      TypeRef typeRef => typeRef.Type,
      null => throw new EvalRuntimeException($"cannot call '{call.Name}' on null", call.Position),
      _ => this.MirrorTypeOf(target, call.Position)
    };

    var (method, values, outIndexes) = this.BindCall(declaringType, call);

    if (outIndexes.Count == 0) {
      var result = isStatic
        ? inv.InvokeStatic(declaringType, method, values)
        : inv.Invoke(this.ToMirror(target, call.Position), method, values);

      if (!isStatic && target is StructMirror) {
        // ReturnOutThis already updated the receiver mirror with the post-call fields.
        writeBack?.Invoke();
      }

      return result;
    }

    var invokeResult = isStatic
      ? inv.InvokeStaticWithOutArgs(declaringType, method, values)
      : inv.InvokeWithOutArgs(this.ToMirror(target, call.Position), method, values);

    if (!isStatic && target is StructMirror) {
      writeBack?.Invoke();
    }

    // An agent below protocol 2.35 silently ignores the return-out-args request; failing loudly
    // beats writing nulls into the out locals.
    if (invokeResult.OutArgs is null) {
      throw new EvalRuntimeException(
        "this game's debugger agent does not return out arguments " +
        "(Mono SDB protocol 2.35+ required)",
        call.Position
      );
    }

    // Out values come back positionally in OutArgs; write them into their locals.
    foreach (var index in outIndexes) {
      var argument = call.Args[index];
      var name = ((NameExpr) argument.Value).Name;

      this.locals[name] = index < invokeResult.OutArgs.Length ? invokeResult.OutArgs[index] : null;
    }

    return invokeResult.Result;
  }

  private (MethodMirror Method, Value[] Args, List<int> OutIndexes) BindCall(
    TypeMirror declaringType,
    CallExpr call
  ) {
    var argc = call.Args.Count;

    // Out targets validate up front; plain arguments only evaluate once the method is known to
    // exist, so a typo'd call has no side effects.
    var outIndexes = new List<int>();

    for (var i = 0; i < argc; i++) {
      var argument = call.Args[i];

      if (argument.Mode == ArgMode.Plain) {
        continue;
      }

      outIndexes.Add(i);

      if (argument.Value is not NameExpr outName) {
        throw new EvalRuntimeException("out arguments must name a local", call.Position);
      }

      if (argument.Mode == ArgMode.Out && !this.locals.ContainsKey(outName.Name)) {
        throw new EvalRuntimeException(
          $"out target '{outName.Name}' is not declared (use `out var name` to declare it)",
          call.Position
        );
      }
    }

    List<MethodMirror> candidates;

    if (call.TypeArgs.Count > 0) {
      var typeArgs = call.TypeArgs.Select(name => this.ResolveType(name, call.Position)).ToArray();

      var definition = inv.FindMethod(declaringType, call.Name, argc, call.TypeArgs.Count);

      candidates = [definition.MakeGenericMethod(typeArgs)];
    }
    else {
      candidates = inv.FindMethods(declaringType, call.Name, argc);

      if (candidates.Count == 0) {
        throw new EvalRuntimeException(
          $"method {declaringType.Name}.{call.Name}/{argc} not found",
          call.Position
        );
      }
    }

    var evaluated = new object[argc];

    for (var i = 0; i < argc; i++) {
      if (call.Args[i].Mode == ArgMode.Plain) {
        evaluated[i] = this.Evaluate(call.Args[i].Value);
      }
    }

    var (method, values) = this.SelectOverload(
      candidates,
      evaluated,
      $"{declaringType.Name}.{call.Name}",
      call.Position,
      outIndexes.Count > 0 ? outIndexes.ToHashSet() : null
    );

    return (method, values, outIndexes);
  }

  /// <summary>
  /// Picks the overload whose parameters accept the evaluated arguments, coercing each argument:
  /// an exact-type pass runs before a widening pass, so `M(int)` beats `M(long)` for an int
  /// argument regardless of enumeration order.
  /// Out-argument slots take client-built default placeholders and never coerce.
  /// </summary>
  private (MethodMirror Method, Value[] Values) SelectOverload(
    List<MethodMirror> candidates,
    object[] args,
    string context,
    int position,
    IReadOnlySet<int> outIndexes = null
  ) {
    var failures = new List<string>();

    foreach (var allowWidening in new[] { false, true }) {
      // The widening pass tries candidates in ascending conversion cost, approximating C#'s
      // better-conversion rule, so `M(long)` beats `M(double)` for an int argument regardless
      // of declaration order (OrderBy is stable: ties keep declaration order).
      var ordered = allowWidening
        ? candidates.OrderBy(c => this.ConversionCost(c, args, outIndexes)).ToList()
        : candidates;

      foreach (var candidate in ordered) {
        var parameters = candidate.GetParameters();
        var values = new Value[args.Length];

        try {
          for (var i = 0; i < args.Length; i++) {
            var parameterType = parameters[i].ParameterType;

            values[i] = outIndexes is not null && outIndexes.Contains(i)
              ? this.DefaultMirrorFor(
                parameterType.IsByRef ? parameterType.GetElementType() : parameterType
              )
              : this.CoerceToType(args[i], parameterType, position, allowWidening);
          }
        }
        catch (Exception ex) when (ex is EvalRuntimeException or
          InvalidOperationException or
          InvalidCastException or
          OverflowException or
          RuntimeBinderException) {
          // Any conversion failure rejects the candidate; only the widening pass reports (its
          // failures subsume the strict pass's).
          if (allowWidening) {
            failures.Add($"{EvalInterpreter.Signature(candidate)}: {ex.Message}");
          }

          continue;
        }

        return (candidate, values);
      }
    }

    throw new EvalRuntimeException(
      $"no overload of {context} accepts these arguments; tried: {string.Join("; ", failures)}",
      position
    );
  }

  private int ConversionCost(MethodMirror candidate, object[] args, IReadOnlySet<int> outIndexes) {
    var parameters = candidate.GetParameters();
    var cost = 0;

    for (var i = 0; i < args.Length; i++) {
      if (outIndexes is null || !outIndexes.Contains(i)) {
        cost += this.ArgumentCost(args[i], parameters[i].ParameterType);
      }
    }

    return cost;
  }

  /// <summary>
  /// The widening pass's conversion-cost heuristic: exact parameters cost nothing, numeric
  /// widening costs the target's width rank (narrower wins, as in C#), narrowing and boxing
  /// cost most.
  /// </summary>
  private int ArgumentCost(object arg, TypeMirror parameterType) {
    var bare = parameterType.IsByRef ? parameterType.GetElementType() : parameterType;

    if (EvalInterpreter.ClrPrimitiveOrNull(bare.FullName) is {} clr) {
      var argType = UnwrapForOps(arg)?.GetType();

      return argType == clr
        ? 0
        : argType is not null && PrimitiveOps.CanWiden(argType, clr)
          ? EvalInterpreter.NumericRank(clr)
          : 100 + EvalInterpreter.NumericRank(clr);
    }

    if (EvalInterpreter.MirrorTypeOrNull(arg) is {} mirrorType && mirrorType == bare) {
      return 0;
    }

    return bare.FullName == "System.Object" ? 1000 : 500;
  }

  private static int NumericRank(Type clr) {
    return Type.GetTypeCode(clr) switch {
      TypeCode.SByte => 1,
      TypeCode.Byte => 2,
      TypeCode.Int16 => 3,
      TypeCode.UInt16 => 4,
      TypeCode.Char => 4,
      TypeCode.Int32 => 5,
      TypeCode.UInt32 => 6,
      TypeCode.Int64 => 7,
      TypeCode.UInt64 => 8,
      TypeCode.Single => 9,
      TypeCode.Double => 10,
      _ => 50
    };
  }

  private static string Signature(MethodMirror method) {
    var parameters = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));

    return $"{method.Name}({parameters})";
  }

  // ---- Indexers ----

  private Value EvaluateIndex(IndexExpr index) {
    var target = this.Evaluate(index.Target);
    var args = index.Args.Select(this.Evaluate).ToArray();

    if (target is ArrayMirror array) {
      return array[this.ArrayIndexOf(array, args, index.Position)];
    }

    return this.InvokeIndexerGet(target, args, index.Position);
  }

  private Value InvokeIndexerGet(object target, object[] args, int position) {
    var mirror = this.ToMirror(target, position);
    var type = this.MirrorTypeOf(target, position);

    var candidates = inv.FindMethods(type, "get_Item", args.Length);

    if (candidates.Count == 0) {
      throw new EvalRuntimeException($"{type.FullName} has no indexer", position);
    }

    var (method, values) = this.SelectOverload(candidates, args, $"{type.Name} indexer", position);

    return inv.Invoke(mirror, method, values);
  }

  /// <summary>
  /// Validates a rank-1 single-index array access and returns the index; rank &gt; 1 is rejected
  /// (the low-level indexer is linear and would silently misread it), and any integral index
  /// type is accepted, as in C#.
  /// </summary>
  private int ArrayIndexOf(ArrayMirror array, object[] args, int position) {
    if (array.Rank != 1) {
      throw new EvalRuntimeException(
        $"rank-{array.Rank} array indexing is not supported (rank 1 only)",
        position
      );
    }

    long? index = args.Length == 1
      ? global::UnityDevtools.Sdb.Eval.EvalInterpreter.UnwrapForOps(args[0]) switch {
        int v => v,
        uint v => v,
        long v => v,
        ulong v and <= int.MaxValue => (long) v,
        short v => v,
        ushort v => v,
        byte v => v,
        sbyte v => v,
        char v => v,
        _ => null
      }
      : null;

    if (index is not (>= 0 and <= int.MaxValue)) {
      throw new EvalRuntimeException(
        "array indexing expects one non-negative integer index",
        position
      );
    }

    return (int) index.Value;
  }

  // ---- Construction ----

  private Value EvaluateNew(NewExpr creation) {
    var type = this.ResolveType(creation.TypeName, creation.Position);

    var args = creation.Args.Select(a =>
        a.Mode == ArgMode.Plain
          ? this.Evaluate(a.Value)
          : throw new EvalRuntimeException("constructors take no out arguments", creation.Position)
      )
      .ToArray();

    var candidates = type.GetMethods()
      .Where(m => m.Name == ".ctor" && m.GetParameters().Length == args.Length)
      .ToList();

    Value instance;

    if (type.IsValueType && args.Length == 0 && candidates.Count == 0) {
      // Zeroed client-side default: no debuggee allocation, fields overwritten locally and
      // serialized on first send (the MakeEntity pattern, generalized). A struct with a declared
      // parameterless constructor must run it instead.
      instance = this.DefaultMirrorFor(type);
    }
    else {
      if (candidates.Count == 0) {
        throw new EvalRuntimeException(
          $"no {type.Name} constructor takes {args.Length} argument(s)",
          creation.Position
        );
      }

      var (ctor, values) = this.SelectOverload(
        candidates,
        args,
        $"{type.Name} constructor",
        creation.Position
      );

      instance = inv.NewInstance(type, ctor, values);
    }

    foreach (var initializer in creation.Initializers) {
      this.WriteMember(instance, initializer.Name, initializer.Value, creation.Position);
    }

    return instance;
  }

  // ---- Assignment ----

  private object EvaluateAssign(AssignExpr assignment) {
    switch (assignment.Target) {
      case NameExpr name when this.locals.ContainsKey(name.Name): {
        var value = this.Evaluate(assignment.Value);

        this.locals[name.Name] = value;

        return value;
      }

      case NameExpr name when this.TryScopes(name.Name, out _):
        throw new EvalRuntimeException($"cannot assign builtin '{name.Name}'", name.Position);

      case NameExpr name:
        throw new EvalRuntimeException(
          $"'{name.Name}' is not declared (declare it with `var {name.Name} = ...`)",
          name.Position
        );

      case MemberExpr member: {
        // C# evaluation order: the target reference resolves before the assigned value.
        var target = this.EvaluateWritable(member.Target);

        EvalInterpreter.RequireAnchored(target, member.Position);

        var value = this.Evaluate(assignment.Value);

        this.WriteMemberValue(target.Value, member.Name, value, member.Position);

        target.WriteBack?.Invoke();

        return value;
      }

      case IndexExpr index: {
        // Same order: target, then indexes, then the assigned value.
        var target = this.EvaluateWritable(index.Target);

        EvalInterpreter.RequireAnchored(target, index.Position);

        var args = index.Args.Select(this.Evaluate).ToArray();
        var value = this.Evaluate(assignment.Value);

        this.WriteIndex(target.Value, args, value, index.Position);

        target.WriteBack?.Invoke();

        return value;
      }

      default:
        throw new EvalRuntimeException(
          "assignment target must be a local, field, property, or indexer",
          assignment.Position
        );
    }
  }

  /// <summary>
  /// An assignment target's container: the value written into, whether mutations to it persist
  /// (<see cref="Anchored" />: a local, a live object, or a chain of tracked links rooted in
  /// one), and the write-back replaying by-copy struct links outward. Object fields, array
  /// elements, and static fields decode structs as fresh client copies, so C# lvalue semantics
  /// require storing the mutated copy back after the leaf write.
  /// </summary>
  private readonly record struct Lvalue(object Value, Action WriteBack, bool Anchored);

  /// <summary>C#'s CS1612 analog: a write into a struct temporary would be silently lost.</summary>
  private static void RequireAnchored(Lvalue target, int position) {
    if (target is { Value: StructMirror, Anchored: false }) {
      throw new EvalRuntimeException(
        "the target is a struct copy returned by a method, property, or indexer; store it in a " +
        "local, mutate that, then assign it back whole",
        position
      );
    }
  }

  /// <summary>
  /// Evaluates an assignment target's container expression with lvalue tracking, so a chained
  /// write like `obj.structField.f = v` or `arr[i].f = v` persists through the by-copy links.
  /// </summary>
  private Lvalue EvaluateWritable(EvalExpr expr) {
    switch (expr) {
      case NameExpr or MemberExpr when EvalInterpreter.TryFlattenChain(expr, out var segments): {
        var (root, next) = this.ResolveChainRoot(segments);

        // Locals, scope values, and types anchor a chain (their mirrors are the stored
        // instances client-side).
        var current = new Lvalue(root, null, true);

        for (var i = next; i < segments.Count; i++) {
          current = this.ReadLink(current, segments[i].Name, segments[i].Position);
        }

        return current;
      }

      case MemberExpr member: {
        var container = this.EvaluateWritable(member.Target);

        return this.ReadLink(container, member.Name, member.Position);
      }

      case IndexExpr index: {
        var container = this.EvaluateWritable(index.Target);
        var args = index.Args.Select(this.Evaluate).ToArray();

        return this.ReadIndexLink(container, args, index.Position);
      }

      default:
        // Any other expression (a call, a cast, ...) yields a value with no home; a struct
        // coming out of it is a temporary no write can anchor to.
        return new Lvalue(this.EvaluateAllowingType(expr), null, false);
    }
  }

  /// <summary>One tracked member link of an lvalue chain (see <see cref="Lvalue" />).</summary>
  private Lvalue ReadLink(Lvalue container, string name, int position) {
    switch (container.Value) {
      case TypeRef typeRef when !typeRef.Type.IsEnum: {
        var field = typeRef.Type.GetFields().FirstOrDefault(f => f.IsStatic && f.Name == name);

        if (field is not null) {
          var value = typeRef.Type.GetValue(field);

          // Static struct fields decode as fresh copies: store the mutated copy back.
          return value is StructMirror
            ? new Lvalue(value, () => typeRef.Type.SetValue(field, value), true)
            : new Lvalue(value, null, true);
        }

        // A struct from a static property getter is a temporary, as in C#.
        var read = this.ReadStaticMember(typeRef.Type, name, position);

        return new Lvalue(read, null, read is not StructMirror);
      }

      case StructMirror structContainer: {
        var (field, getter) = EvalInterpreter.FindInstanceMemberSlot(
          structContainer.Type,
          name,
          $"get_{name}",
          0
        );

        if (field is not null) {
          var value = structContainer[name];

          // A nested struct is the STORED mirror: mutations land in the parent's fields
          // client-side, so persistence rides the parent's own write-back. Any other field kind
          // is written directly (heap identity or leaf), needing no replay.
          return value is StructMirror
            ? container with {
              Value = value
            }
            : new Lvalue(value, null, true);
        }

        if (getter is not null) {
          var value = inv.Invoke(structContainer, getter);

          return new Lvalue(value, null, value is not StructMirror);
        }

        throw new EvalRuntimeException(
          $"'{name}' is not a field or readable property of {structContainer.Type.FullName}; " +
          $"fields: {Invoker.InstanceFieldNames(structContainer.Type)}",
          position
        );
      }

      case ObjectMirror objectContainer: {
        var (field, getter) = EvalInterpreter.FindInstanceMemberSlot(
          objectContainer.Type,
          name,
          $"get_{name}",
          0
        );

        if (field is not null) {
          var value = objectContainer.GetValue(field);

          // Object fields decode structs as fresh copies: store the copy back after the write.
          return value is StructMirror
            ? new Lvalue(value, () => objectContainer.SetValue(field, value), true)
            : new Lvalue(value, null, true);
        }

        if (getter is not null) {
          var value = inv.Invoke(objectContainer, getter);

          return new Lvalue(value, null, value is not StructMirror);
        }

        throw new EvalRuntimeException(
          $"'{name}' is not a field or readable property of {objectContainer.Type.FullName}; " +
          $"fields: {Invoker.InstanceFieldNames(objectContainer.Type)}",
          position
        );
      }

      default: {
        // Enum type refs, primitives, client values: read normally; nothing struct-writable
        // comes back this way.
        var value = this.ReadMember(container.Value, name, position);

        return new Lvalue(value, null, value is not StructMirror);
      }
    }
  }

  /// <summary>One tracked index link of an lvalue chain (see <see cref="Lvalue" />).</summary>
  private Lvalue ReadIndexLink(Lvalue container, object[] args, int position) {
    if (container.Value is ArrayMirror array) {
      var at = this.ArrayIndexOf(array, args, position);

      var value = array[at];

      // Array elements decode as fresh copies; unlike C#'s true by-reference elements, the
      // mutated copy must be stored back after the leaf write.
      return value is StructMirror
        ? new Lvalue(value, () => array.SetValues(at, [value]), true)
        : new Lvalue(value, null, true);
    }

    // get_Item returns a temporary; C# itself forbids writing through it when it is a struct.
    var element = this.InvokeIndexerGet(container.Value, args, position);

    return new Lvalue(element, null, element is not StructMirror);
  }

  private void WriteMember(Value target, string name, EvalExpr valueExpr, int position) =>
    this.WriteMemberValue(target, name, this.Evaluate(valueExpr), position);

  private void WriteMemberValue(object target, string name, object value, int position) {
    switch (target) {
      case TypeRef typeRef: {
        var field = typeRef.Type.GetFields().FirstOrDefault(f => f.IsStatic && f.Name == name);

        if (field is not null) {
          typeRef.Type.SetValue(field, this.CoerceToType(value, field.FieldType, position));

          return;
        }

        var setter = inv.FindMethod(typeRef.Type, $"set_{name}", 1);

        inv.InvokeStatic(
          typeRef.Type,
          setter,
          this.CoerceToType(value, setter.GetParameters()[0].ParameterType, position)
        );

        return;
      }

      case StructMirror or ObjectMirror:
        this.WriteInstanceMember((Value) target, name, value, position);

        return;

      case null: throw new EvalRuntimeException($"cannot write '{name}' on null", position);

      default:
        throw new EvalRuntimeException(
          $"cannot write '{name}' on {EvalInterpreter.TypeNameOf(target)}",
          position
        );
    }
  }

  private void WriteIndex(object target, object[] args, object value, int position) {
    if (target is ArrayMirror array) {
      var at = this.ArrayIndexOf(array, args, position);

      var element = this.CoerceToType(value, array.Type.GetElementType(), position);

      array.SetValues(at, [element]);

      return;
    }

    var mirror = this.ToMirror(target, position);
    var type = this.MirrorTypeOf(target, position);

    var candidates = inv.FindMethods(type, "set_Item", args.Length + 1);

    if (candidates.Count == 0) {
      throw new EvalRuntimeException($"{type.FullName} has no writable indexer", position);
    }

    var (method, values) = this.SelectOverload(
      candidates,
      [.. args, value],
      $"{type.Name} indexer",
      position
    );

    inv.Invoke(mirror, method, values);
  }

  // ---- Casts ----

  private object EvaluateCast(CastExpr cast) {
    var type = this.ResolveType(cast.TypeName, cast.Position);
    var value = this.Evaluate(cast.Operand);

    if (type.IsEnum) {
      var numeric = UnwrapForOps(value);

      // Strings are IConvertible but not castable in C#.
      return numeric is IConvertible and not string
        ? this.MakeEnum(type, numeric)
        : throw new EvalRuntimeException(
          $"cannot cast {EvalInterpreter.TypeNameOf(value)} to enum {type.FullName}",
          cast.Position
        );
    }

    var clr = EvalInterpreter.ClrPrimitiveOrNull(type.FullName);

    if (clr is not null) {
      var unwrapped = UnwrapForOps(value);

      return unwrapped is IConvertible and not string
        ? EvalInterpreter.CastClient(unwrapped, clr)
        : throw new EvalRuntimeException(
          $"cannot cast {EvalInterpreter.TypeNameOf(value)} to {type.FullName}",
          cast.Position
        );
    }

    // Null casts to any reference target.
    if (EvalInterpreter.IsNull(value)) {
      return value;
    }

    // Mirrors carry no representation change over a cast, but a mirror's Type is the value's
    // runtime type, so it must actually be the target (C#'s InvalidCastException otherwise).
    var mirrorType = EvalInterpreter.MirrorTypeOrNull(value);

    if (mirrorType is not null) {
      return mirrorType == type || type.IsAssignableFrom(mirrorType)
        ? value
        : throw new EvalRuntimeException(
          $"cannot cast {mirrorType.FullName} to {type.FullName}",
          cast.Position
        );
    }

    // Client-side values: identity and boxing to object are the only meaningful reference casts.
    return type.FullName == "System.Object" || value.GetType().FullName == type.FullName
      ? value
      : throw new EvalRuntimeException(
        $"cannot cast {EvalInterpreter.TypeNameOf(value)} to {type.FullName}",
        cast.Position
      );
  }

  /// <summary>C# cast semantics (truncation, not rounding) via the runtime binder.</summary>
  private static object CastClient(object value, Type target) {
    // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
    return Type.GetTypeCode(target) switch {
      TypeCode.Int32 => (int) (dynamic) value,
      TypeCode.UInt32 => (uint) (dynamic) value,
      TypeCode.Int64 => (long) (dynamic) value,
      TypeCode.UInt64 => (ulong) (dynamic) value,
      TypeCode.Int16 => (short) (dynamic) value,
      TypeCode.UInt16 => (ushort) (dynamic) value,
      TypeCode.Byte => (byte) (dynamic) value,
      TypeCode.SByte => (sbyte) (dynamic) value,
      TypeCode.Single => (float) (dynamic) value,
      TypeCode.Double => (double) (dynamic) value,
      TypeCode.Char => (char) (dynamic) value,
      TypeCode.Boolean => (bool) (dynamic) value,
      _ => throw new InvalidOperationException($"cannot cast to {target.FullName}")
    };
  }

  // ---- Types ----

  private TypeMirror ResolveType(string dotted, int position) {
    return this.ResolveTypeOrNull(dotted) ??
      throw new EvalRuntimeException(
        $"type '{dotted}' not found (names must be fully qualified)",
        position
      );
  }

  /// <summary>
  /// Resolves a dotted name against the live VM (see <see cref="Invoker.FindTypeOrNull" />; the
  /// cache lives on the Invoker so it spans every eval of one attach).
  /// </summary>
  private TypeMirror ResolveTypeOrNull(string dotted) => inv.FindTypeOrNull(dotted);

  // ---- Value plumbing ----

  /// <summary>The mirror's own type, when the value is a struct or object mirror.</summary>
  private static TypeMirror MirrorTypeOrNull(object value) {
    return value switch {
      ObjectMirror o => o.Type,
      StructMirror s => s.Type,
      _ => null
    };
  }

  private TypeMirror MirrorTypeOf(object value, int position) {
    if (EvalInterpreter.MirrorTypeOrNull(value) is {} mirrorType) {
      return mirrorType;
    }

    return value switch {
      PrimitiveValue { Value: not null } p => this.ResolveType(
        p.Value.GetType().FullName,
        position
      ),
      Value v => throw new EvalRuntimeException($"no type for {v.GetType().Name}", position),
      null => throw new EvalRuntimeException("no type for null", position),
      _ => this.ResolveType(value.GetType().FullName, position)
    };
  }

  /// <summary>Client CLR values and primitive-ish mirrors, normalized for PrimitiveOps.</summary>
  private static object UnwrapForOps(object value) {
    return value switch {
      PrimitiveValue p => p.Value,
      StringMirror s => s.Value,
      EnumMirror e => e.Value,
      _ => value
    };
  }

  private static bool IsNull(object value) => value is null or PrimitiveValue { Value: null };

  /// <summary>Converts any in-flight value to a mirror for use as an invoke target.</summary>
  private Value ToMirror(object value, int position) {
    return value switch {
      Value mirror => mirror,
      null => inv.Vm.CreateValue(null),
      string s => inv.Str(s),
      not null when EvalInterpreter.ClrPrimitiveOrNull(value.GetType().FullName) is not null =>
        inv.Prim(value),
      _ => throw new EvalRuntimeException(
        $"cannot send a {value.GetType().FullName} to the debuggee",
        position
      )
    };
  }

  /// <summary>
  /// Coerces an in-flight value to a target (parameter or field) type, client-side: exact CLR
  /// match, plus implicit numeric widening and reference-conversion assignability when
  /// <paramref name="allowWidening" /> permits (overload binding's strict pass disables both).
  /// </summary>
  private Value CoerceToType(
    object value,
    TypeMirror targetType,
    int position,
    bool allowWidening = true
  ) {
    var typeName = targetType.FullName.TrimEnd('&');

    if (EvalInterpreter.IsNull(value)) {
      return inv.Vm.CreateValue(null);
    }

    if (value is TypeRef typeRef) {
      // A bare type where a System.Type is expected: the common typo for typeof(...).
      return typeName == "System.Type"
        ? typeRef.Type.GetTypeObject()
        : throw new EvalRuntimeException(
          $"'{typeRef.Type.FullName}' is a type, not a value",
          position
        );
    }

    if (value is not Value mirror) {
      return this.CoerceClient(value, targetType, position, allowWidening);
    }

    var bareTarget = targetType.IsByRef ? targetType.GetElementType() : targetType;

    switch (mirror) {
      case EnumMirror e when bareTarget.IsEnum:
        return e.Type.FullName == bareTarget.FullName
          ? mirror
          : throw new EvalRuntimeException(
            $"cannot pass {e.Type.FullName} as {typeName}",
            position
          );

      case EnumMirror e:
        // Boxing and implemented-interface targets take the enum as-is; numeric targets need an
        // explicit cast, as in C#.
        return allowWidening &&
          (typeName is "System.Object" or "System.Enum" or "System.ValueType" ||
            bareTarget.IsAssignableFrom(e.Type))
            ? mirror
            : throw new EvalRuntimeException(
              $"cannot pass {e.Type.FullName} as {typeName} (cast explicitly)",
              position
            );

      case PrimitiveValue p: return this.CoerceClient(p.Value, targetType, position, allowWidening);

      case StringMirror when typeName == "System.String": return mirror;

      case StringMirror when allowWidening && typeName == "System.Object": return mirror;

      default: {
        var mirrorType = mirror switch {
          ObjectMirror o => o.Type,
          StructMirror s => s.Type,
          _ => null
        };

        // Mirror kinds without a type mirror pass through untouched.
        if (mirrorType is null) {
          return mirror;
        }

        // ReSharper disable once DuplicatedSequentialIfBodies
        if (mirrorType == bareTarget) {
          return mirror;
        }

        if (allowWidening &&
          (typeName == "System.Object" || bareTarget.IsAssignableFrom(mirrorType))) {
          return mirror;
        }

        throw new EvalRuntimeException(
          $"{mirrorType.FullName} is not assignable to {typeName}",
          position
        );
      }
    }
  }

  private Value CoerceClient(
    object value,
    TypeMirror targetType,
    int position,
    bool allowWidening
  ) {
    if (value is null) {
      return inv.Vm.CreateValue(null);
    }

    var bareTarget = targetType.IsByRef ? targetType.GetElementType() : targetType;
    var typeName = bareTarget.FullName;

    if (bareTarget.IsEnum) {
      // Numeric-to-enum is admitted as a REPL convenience (C# itself would want a cast);
      // strings and bools stay out (no numeric identity to convert).
      return allowWidening && value is IConvertible and not string and not bool
        ? this.MakeEnum(bareTarget, value)
        : throw new EvalRuntimeException(
          $"cannot pass {value.GetType().FullName} as enum {typeName}",
          position
        );
    }

    switch (typeName) {
      case "System.String":
        return value is string s
          ? inv.Str(s)
          : throw new EvalRuntimeException(
            $"cannot pass {value.GetType().FullName} as string",
            position
          );

      case "System.Object" when allowWidening:
        // Boxing happens debuggee-side on send for primitives.
        return this.ToMirror(value, position);
    }

    var clr = EvalInterpreter.ClrPrimitiveOrNull(typeName);

    if (clr is null) {
      throw new EvalRuntimeException(
        $"cannot pass a client-side {value.GetType().FullName} as {typeName}",
        position
      );
    }

    if (value.GetType() == clr) {
      return inv.Prim(value);
    }

    if (!allowWidening) {
      throw new EvalRuntimeException(
        $"no implicit conversion from {value.GetType().Name} to {clr.Name} (cast explicitly)",
        position
      );
    }

    if (PrimitiveOps.CanWiden(value.GetType(), clr)) {
      // The cast, not Convert.ChangeType: Char's IConvertible lacks ToSingle/ToDouble, and the
      // cast is what C#'s implicit conversion compiles to anyway.
      return inv.Prim(EvalInterpreter.CastClient(value, clr));
    }

    if (EvalInterpreter.IsIntegral(value.GetType()) && EvalInterpreter.IsIntegral(clr)) {
      // In-range integral narrowing: C# accepts it for constants, the evaluator cannot tell a
      // literal from a variable, and byte/short component fields are the tool's bread and
      // butter. The checked conversion turns out-of-range into a clear error.
      try {
        return inv.Prim(Convert.ChangeType(value, clr, CultureInfo.InvariantCulture));
      }
      catch (OverflowException) {
        throw new EvalRuntimeException($"{value} does not fit {clr.Name}", position);
      }
    }

    throw new EvalRuntimeException(
      $"no implicit conversion from {value.GetType().Name} to {clr.Name} (cast explicitly)",
      position
    );
  }

  private static bool IsIntegral(Type type) {
    return Type.GetTypeCode(type) is TypeCode.SByte or
      TypeCode.Byte or
      TypeCode.Int16 or
      TypeCode.UInt16 or
      TypeCode.Int32 or
      TypeCode.UInt32 or
      TypeCode.Int64 or
      TypeCode.UInt64 or
      TypeCode.Char;
  }

  private static Type ClrPrimitive(string fullName) =>
    EvalInterpreter.ClrPrimitiveOrNull(fullName) ??
    throw new InvalidOperationException($"{fullName} is not a primitive type");

  private static Type ClrPrimitiveOrNull(string fullName) {
    return fullName switch {
      "System.Int32" => typeof(int),
      "System.UInt32" => typeof(uint),
      "System.Int64" => typeof(long),
      "System.UInt64" => typeof(ulong),
      "System.Int16" => typeof(short),
      "System.UInt16" => typeof(ushort),
      "System.Byte" => typeof(byte),
      "System.SByte" => typeof(sbyte),
      "System.Single" => typeof(float),
      "System.Double" => typeof(double),
      "System.Boolean" => typeof(bool),
      "System.Char" => typeof(char),
      _ => null
    };
  }

  /// <summary>Default value for a mirrored type, built entirely client-side.</summary>
  private Value DefaultMirrorFor(TypeMirror type) {
    if (type.IsEnum) {
      return this.MakeEnum(type, 0);
    }

    var clr = EvalInterpreter.ClrPrimitiveOrNull(type.FullName);

    if (clr is not null) {
      return inv.Prim(
        clr == typeof(char) ? '\0' : Convert.ChangeType(0, clr, CultureInfo.InvariantCulture)
      );
    }

    if (!type.IsValueType) {
      return inv.Vm.CreateValue(null);
    }

    // Client-side default struct: the vendored StructMirror ctor is internal to this assembly.
    var fields = Invoker.InstanceFields(type);

    return new StructMirror(
      inv.Vm,
      type,
      fields.Select(f => this.DefaultMirrorFor(f.FieldType)).ToArray()
    );
  }

  // ---- Formatting and reporting ----

  private string Stringify(object value) {
    return value switch {
      null => "",
      string s => s,
      IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
      StringMirror s => s.Value,
      PrimitiveValue p => this.Stringify(p.Value),
      EnumMirror e => e.StringValue,
      Value v => inv.Format(v),
      _ => value.ToString()
    };
  }

  private string FormatValue(object value, int depth) {
    return value switch {
      null => "null",
      Value mirror => inv.Format(mirror, depth),
      string s => $"\"{s}\"",
      bool b => b ? "true" : "false",
      IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
      _ => value.ToString()
    };
  }

  private static string TypeNameOf(object value) {
    return value switch {
      null => "null",
      StringMirror => "System.String",
      EnumMirror e => e.Type.FullName,
      ArrayMirror a => a.Type.FullName,
      PrimitiveValue p => p.Value?.GetType().FullName ?? "null",
      StructMirror s => s.Type.FullName,
      ObjectMirror o => o.Type.FullName,
      Value v => v.GetType().Name,
      _ => value.GetType().FullName
    };
  }

  private EvalFailedException Fail(EvalProgram program, int statementIndex, Exception cause) {
    var statement = program.Statements[statementIndex];

    var source = program.Source.Substring(
      statement.Position,
      Math.Min(statement.Length, program.Source.Length - statement.Position)
    );

    var position = cause is EvalRuntimeException { Position: >= 0 } runtime
      ? runtime.Position
      : statement.Position;

    string gameExceptionType = null;
    string gameExceptionMessage = null;

    if (EvalInterpreter.FindInvocationException(cause) is {} invocation) {
      var thrown = invocation.Exception;

      gameExceptionType = thrown.Type.FullName;

      try {
        gameExceptionMessage = (inv.GetProperty(thrown, "Message") as StringMirror)?.Value;
      }
      catch {
        // The message is best-effort; the type alone is still actionable.
      }
    }

    var message = cause switch {
      ObjectCollectedException =>
        "a previous result was garbage-collected after the game resumed; " +
        "re-evaluate it instead of using `_`",
      InvocationException => "the invoked code threw an exception in the game",
      _ => cause.Message
    };

    return new EvalFailedException(message) {
      StatementIndex = statementIndex,
      StatementSource = source,
      Position = position,
      GameExceptionType = gameExceptionType,
      GameExceptionMessage = gameExceptionMessage,
      Locals = this.locals.Select(local =>
          new KeyValuePair<string, string>(local.Key, this.FormatLocalSafely(local.Value))
        )
        .ToArray()
    };
  }

  private static InvocationException FindInvocationException(Exception cause) {
    for (var ex = cause; ex is not null; ex = ex.InnerException) {
      if (ex is InvocationException invocation) {
        return invocation;
      }
    }

    return null;
  }

  private string FormatLocalSafely(object value) {
    try {
      return this.FormatValue(value, 1);
    }
    catch {
      return "<unreadable>";
    }
  }

  /// <summary>A name chain resolved to a type instead of a value (static context).</summary>
  private sealed record TypeRef(TypeMirror Type);
}

/// <summary>A successful evaluation: the final value, formatted, with its type name.</summary>
public sealed record EvalOutcome {
  public required string Formatted { get; init; }

  public required string TypeName { get; init; }
}
