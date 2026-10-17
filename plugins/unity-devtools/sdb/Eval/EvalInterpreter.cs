using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
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
/// <param name="catalog">
/// What a wrong type name is diagnosed against; null for an evaluator that must never harvest
/// (its first use runs invokes) or whose expressions are the plugin's own, and a type miss then
/// reports plainly.
/// </param>
public sealed class EvalInterpreter(
  Invoker inv,
  IReadOnlyList<IEvalScope> scopes,
  TypeCatalog catalog = null
) {
  // ArgumentCost's bands, cheapest first: an exact match, a numeric widening (the target's width
  // rank), an in-range narrowing (the same rank on top of its band), a null into a nullable (the
  // underlying primitive's rank on top of its band, any other underlying type's above every
  // primitive's), any other conversion (an integer zero into an enum included), a conversion to
  // object, and the numeric-to-enum convenience C# would not bind at all.
  private const int ExactCost = 0;

  private const int NarrowingCost = 100;

  private const int NullIntoNullableCost = 440;

  private const int ReferenceCost = 500;

  private const int ObjectCost = 1000;

  private const int EnumConvenienceCost = 1500;

  // The rank of a target no numeric rank orders, above every numeric one.
  private const int OtherRank = 50;

  /// <summary>Locals in declaration order (failure reports list them as declared).</summary>
  private readonly OrderedDictionary<string, object> locals = [];

  /// <summary>Receiver stack for `?.` chains (the tested value binds the member hole).</summary>
  private readonly Stack<Lvalue> receivers = new();

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
        Formatted = this.FormatValue(last, Invoker.ReadDepth),
        TypeName = EvalInterpreter.TypeNameOf(last),
        Value = last
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

    var value = EvalInterpreter.CopyStruct(this.Evaluate(declaration.Value));

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

      case ImplicitReceiverExpr: return this.receivers.Peek().Value;

      case CallExpr call: return this.EvaluateCall(call);

      case IndexExpr index: return this.EvaluateIndex(index);

      case NewExpr creation: return this.EvaluateNew(creation);

      case CastExpr cast: return this.EvaluateCast(cast);

      case TypeofExpr typeOf:
        return inv.TypeObject(this.ResolveType(typeOf.TypeName, typeOf.Position));

      // A type the builder cannot zero client-side is refused there, naming it.
      case DefaultExpr defaultOf:
        return this.DefaultOf(this.ResolveType(defaultOf.TypeName, defaultOf.Position));

      case UnaryExpr unary: {
        var operand = EvalInterpreter.UnwrapNullable(this.Evaluate(unary.Operand));

        var raw = PrimitiveOps.Unary(unary.Op, EvalInterpreter.UnwrapForOps(operand));

        // `~` is the one unary operator C# defines on (flags) enums; it keeps the enum type.
        return unary.Op is "~" && operand is EnumMirror e ? inv.MakeEnum(e.Type, raw) : raw;
      }

      case BinaryExpr binary: return this.EvaluateBinary(binary);

      case ConditionalExpr conditional:
        return this.EvaluateBool(conditional.Condition)
          ? this.Evaluate(conditional.WhenTrue)
          : this.Evaluate(conditional.WhenFalse);

      case ConditionalAccessExpr access: {
        // The receiver keeps its link, so a call through it stores back as through `.`, save a
        // nullable's value, which C# reads as a copy.
        var link = this.EvaluateWritable(access.Target);
        var target = EvalInterpreter.UnwrapNullable(link.Value);

        if (EvalInterpreter.IsNull(target)) {
          return null;
        }

        this.receivers.Push(
          ReferenceEquals(target, link.Value) ? link : new Lvalue(target, null, false)
        );

        try {
          return this.Evaluate(access.WhenNotNull);
        }
        finally {
          this.receivers.Pop();
        }
      }

      case InterpolatedStringExpr interpolated:
        return string.Concat(
          interpolated.Parts.Select(p => this.RenderHole(this.Evaluate(p.Value), p.Format))
        );

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
        var left = EvalInterpreter.UnwrapNullable(this.Evaluate(binary.Left));

        return EvalInterpreter.IsNull(left) ? this.Evaluate(binary.Right) : left;
      }

      default: {
        var left = EvalInterpreter.UnwrapNullable(this.Evaluate(binary.Left));
        var right = EvalInterpreter.UnwrapNullable(this.Evaluate(binary.Right));

        if (left is EnumMirror || right is EnumMirror) {
          return this.EvaluateEnumBinary(binary, left, right);
        }

        // String concatenation with a struct/object operand renders the mirror the way
        // interpolation does; the dynamic binder would concatenate the client wrapper's ToString()
        // instead.
        if (binary.Op is "+" &&
          (EvalInterpreter.IsCompoundMirror(left) || EvalInterpreter.IsCompoundMirror(right)) &&
          (EvalInterpreter.UnwrapForOps(left) is string ||
            EvalInterpreter.UnwrapForOps(right) is string)) {
          return this.Stringify(left) + this.Stringify(right);
        }

        if (binary.Op is "==" or "!=" &&
          (EvalInterpreter.IsCompoundMirror(left) || EvalInterpreter.IsCompoundMirror(right))) {
          return this.EvaluateMirrorEquality(binary, left, right);
        }

        return PrimitiveOps.Binary(
          binary.Op,
          EvalInterpreter.UnwrapForOps(left),
          EvalInterpreter.UnwrapForOps(right)
        );
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
    if (binary.Op is "+" &&
      (EvalInterpreter.UnwrapForOps(left) is string ||
        EvalInterpreter.UnwrapForOps(right) is string)) {
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

    var raw = PrimitiveOps.Binary(
      binary.Op,
      EvalInterpreter.UnwrapForOps(left),
      EvalInterpreter.UnwrapForOps(right)
    );

    // Flags math keeps the enum; so do +/- against a numeric offset (enum minus enum is the
    // underlying distance, as in C#); comparisons fall through as bool.
    return binary.Op switch {
      "&" or "|" or "^" => inv.MakeEnum(enumType, raw),
      "+" or "-" when !bothEnums => inv.MakeEnum(enumType, raw),
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

    // The agent returns a box as its value, so the identity C# compares two objects by is gone.
    if ((left is StructMirror leftBox && EvalInterpreter.IsBoxedPrimitive(leftBox)) ||
      (right is StructMirror rightBox && EvalInterpreter.IsBoxedPrimitive(rightBox))) {
      throw new EvalRuntimeException(
        "cannot compare a boxed value by reference: the debugger reads a box the debuggee " +
        "returns as its value; cast it to its primitive type to compare values",
        binary.Position
      );
    }

    var leftType = this.MirrorTypeOf(left, binary.Position);
    var rightType = this.MirrorTypeOf(right, binary.Position);

    var operatorName = wantEqual ? "op_Equality" : "op_Inequality";

    // C# considers operators declared on either operand's type.
    var operators = Invoker.FindMethods(leftType, operatorName, 2);

    if (rightType != leftType) {
      operators = [.. operators, .. Invoker.FindMethods(rightType, operatorName, 2)];
    }

    // A declared operator rejecting these operands leaves the defaults below to decide.
    if (operators.Count > 0 &&
      this.SelectOverload(
        [[.. operators.Select(o => ArityFit.Exact(o, 2))]],
        [left, right],
        binary.Position,
        []
      ) is {} bound) {
      return EvalInterpreter.UnwrapForOps(
        inv.InvokeStatic(bound.Method.DeclaringType, bound.Method, bound.Values)
      );
    }

    if (left is ObjectMirror leftObject && right is ObjectMirror rightObject) {
      // No declared operator on a reference type: C# compares references.
      var same = leftObject.Address == rightObject.Address;

      return wantEqual ? same : !same;
    }

    // Operator-less structs: C# would reject `==` outright; Equals is the debugger-friendly
    // extension (field-wise via ValueType.Equals unless overridden).
    var failures = new List<Rejection>();

    var (equalsMethod, equalsValues) = this.SelectOverload(
        [[.. Invoker.FindMethods(leftType, "Equals", 1).Select(e => ArityFit.Exact(e, 1))]],
        [right],
        binary.Position,
        failures
      ) ??
      throw new EvalRuntimeException(
        MissDiagnosis.Unbound($"Equals of {leftType.FullName}", 1, 0, failures),
        binary.Position
      );

    var equal = EvalInterpreter.UnwrapForOps(
      inv.Invoke(this.ToMirror(left, binary.Position), equalsMethod, equalsValues)
    ) is true;

    return wantEqual ? equal : !equal;
  }

  private bool EvaluateBool(EvalExpr expr) {
    var value = EvalInterpreter.UnwrapForOps(EvalInterpreter.UnwrapNullable(this.Evaluate(expr)));

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

    if (catalog is null) {
      throw new EvalRuntimeException(
        $"cannot resolve '{rootName}': not a local, a builtin (em, world, entity(), _), or " +
        "the start of a fully-qualified type name",
        rootPosition
      );
    }

    throw new EvalRuntimeException(
      MissDiagnosis.Type(
        catalog,
        segments.Select(s => s.Name).ToList(),
        MissNames.Eval,
        [.. this.locals.Keys, .. scopes.SelectMany(s => s.Names)]
      ),
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

      case ArrayMirror array when name is "Length": return array.Length;

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
    var getter = inv.FindMethodOrNull(type, $"get_{name}", 0) ??
      throw new EvalRuntimeException(
        MissDiagnosis.InstanceMember(type, name, writing: false, fields: false),
        position
      );

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
      MissDiagnosis.InstanceMember(type, name, writing: false),
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
        // Nothing copies or wraps a value stored into a client-held struct, as the agent does for
        // an invoke argument or a game field, so a struct gets its copy, and a nullable field its
        // wrapper, built here.
        var copied = (Value) EvalInterpreter.CopyStruct(coerced);

        structMirror[name] = Invoker.IsNullable(field.FieldType)
          ? inv.WrapNullable(field.FieldType, copied)
          : copied;
      }
      else {
        ((ObjectMirror) mirror).SetValue(field, coerced);
      }

      return;
    }

    if (setter is null) {
      throw new EvalRuntimeException(
        MissDiagnosis.InstanceMember(type, name, writing: true),
        position
      );
    }

    inv.Invoke(
      mirror,
      setter,
      this.CoerceToType(value, setter.GetParameters()[0].ParameterType, position)
    );
  }

  /// <summary>
  /// Refuses a static field of a type whose static constructor has not run: the agent reads its
  /// storage as zeros, and the constructor overwrites a write once it runs.
  /// A <c>const</c> has no storage to wait on, and a type without a static constructor nothing to
  /// run; a constructor the evaluating thread is still running reads partly assigned, as C# shows
  /// it to that thread alone.
  /// </summary>
  private void RequireInitialized(TypeMirror type, FieldInfoMirror field, int position) {
    // The initialized flag is cached once true, where a first scan of the method names costs a
    // round trip per method.
    if (field.IsLiteral ||
      type.IsInitialized ||
      type.GetMethods().All(m => m.Name != ".cctor") ||
      this.IsRunningStaticConstructor(type)) {
      return;
    }

    var name = MissSuggestions.EvalName(type.FullName);

    throw new EvalRuntimeException(
      $"{name}'s static constructor has not run, so '{field.Name}' holds no value yet; run it " +
      "first with `System.Runtime.CompilerServices.RuntimeHelpers" +
      $".RunClassConstructor(typeof({name}).TypeHandle)`, which runs the type's own code",
      position
    );
  }

  /// <summary>
  /// Whether an evaluating thread's stack holds the type's static constructor: the main thread,
  /// which every invoke runs on, or the thread whose frame a scope reads.
  /// </summary>
  private bool IsRunningStaticConstructor(TypeMirror type) {
    ThreadMirror[] threads = [inv.MainThread, .. scopes.OfType<FrameScope>().Select(s => s.Thread)];

    return threads.Any(thread => {
        try {
          return thread.GetFrames()
            .Any(f => f.Method.Name is ".cctor" && f.Method.DeclaringType == type);
        }
        catch (VMNotSuspendedException) {
          // A main thread still in native code has no managed stack to run one on.
          return false;
        }
      }
    );
  }

  private object ReadStaticMember(TypeMirror type, string name, int position) {
    var field = type.GetFields().FirstOrDefault(f => f.IsStatic && f.Name == name);

    // An enum member needs no special path: it is a static field like any other, and the agent
    // answers a `const` literal's value even though it has no storage, as an EnumMirror carrying
    // the member's full width.
    if (field is not null) {
      this.RequireInitialized(type, field, position);

      return type.GetValue(field);
    }

    // An instance getter is a miss too: the type is no receiver for it to run on.
    var getter = inv.FindMethodOrNull(type, $"get_{name}", 0);

    return getter is { IsStatic: true }
      ? inv.InvokeStatic(type, getter)
      : throw new EvalRuntimeException(
        MissDiagnosis.StaticMember(type, name, writing: false),
        position
      );
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
      : throw new EvalRuntimeException(MissDiagnosis.ClientMember(type, name), position);
  }

  // ---- Calls ----

  private object EvaluateCall(CallExpr call) {
    if (call.Target is null) {
      var bareArgs = call.Args.Select(a =>
          a.Mode is ArgMode.Plain
            ? EvalInterpreter.UnwrapForOps(this.Evaluate(a.Value))
            : throw new EvalRuntimeException(
              "builtin functions take no out or ref arguments",
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
    var (target, writeBack, anchored, refusal) = this.EvaluateWritable(call.Target);

    // A temporary may still be the mirror a variable holds, so the call runs on a copy of it; an
    // unanchored struct a refusal names is held somewhere, so the call runs on it.
    if (!anchored && refusal is null) {
      target = EvalInterpreter.CopyStruct(target);
    }

    var isStatic = target is TypeRef;

    var declaringType = target switch {
      TypeRef typeRef => typeRef.Type,
      null => throw new EvalRuntimeException($"cannot call '{call.Name}' on null", call.Position),
      _ => this.MirrorTypeOf(target, call.Position)
    };

    var (method, values, outIndexes) = this.BindCall(declaringType, call, isStatic);

    if (outIndexes.Count is 0) {
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

    // Out and ref values come back positionally in OutArgs; write them into their locals.
    foreach (var index in outIndexes) {
      var argument = call.Args[index];
      var name = ((NameExpr) argument.Value).Name;

      this.locals[name] = index < invokeResult.OutArgs.Length ? invokeResult.OutArgs[index] : null;
    }

    return invokeResult.Result;
  }

  private (MethodMirror Method, Value[] Args, List<int> OutIndexes) BindCall(
    TypeMirror declaringType,
    CallExpr call,
    bool onType
  ) {
    var argc = call.Args.Count;

    // Out targets validate up front; plain arguments only evaluate once the method is known to
    // exist, so a typo'd call has no side effects.
    var outIndexes = new List<int>();

    for (var i = 0; i < argc; i++) {
      var argument = call.Args[i];

      if (argument.Mode is ArgMode.Plain) {
        continue;
      }

      outIndexes.Add(i);

      if (argument.Value is not NameExpr outName) {
        throw new EvalRuntimeException("out arguments must name a local", call.Position);
      }

      if (argument.Mode is ArgMode.Out && !this.locals.ContainsKey(outName.Name)) {
        throw new EvalRuntimeException(
          $"out target '{outName.Name}' is not declared (use `out var name` to declare it)",
          call.Position
        );
      }

      if (argument.Mode is ArgMode.Ref && !this.locals.ContainsKey(outName.Name)) {
        throw new EvalRuntimeException(
          $"ref target '{outName.Name}' is not a local of this program (`ref` takes a local " +
          "declared with `var`)",
          call.Position
        );
      }
    }

    // A static method called through a value still runs, so only the reverse is a miss: an
    // instance method has no receiver to run on when the call is written on the type.
    // Filtered before any instantiation, which costs a command an instance method would waste.
    var named = Invoker.Named(declaringType, call.Name).ToList();
    var reachable = named.Where(c => !onType || c.IsStatic).ToList();

    var fits = reachable.Where(c => Invoker.HasGenericArity(c, call.TypeArgs.Count))
      .Select(c => ArityFit.Of(inv, c, argc))
      .ToList();

    // A call written without type arguments may still bind a generic definition once its
    // arguments fix them; those join the pool AFTER the non-generic candidates, which is what
    // makes `M(int)` win over `M<int>(int)` while `M<T>(T)` still beats `M(object)`, as in C#.
    var definitionFits = call.TypeArgs.Count is 0
      ? reachable.Where(c => c.IsGenericMethodDefinition)
        .Select(c => ArityFit.Of(inv, c, argc))
        .ToList()
      : [];

    var candidates = fits.Where(f => f.IsFit).ToList();
    var inferable = definitionFits.Where(f => f.IsFit).ToList();

    // The candidates whose type arguments could not be had, and those whose arguments failed,
    // for a miss to list.
    var refused = new List<Rejection>();
    var failures = new List<Rejection>();
    Rejection unfixable = null;

    // A type parameter no plain or `ref` argument meets as a parameter of exactly its type can
    // never be inferred, which the call's shape alone tells, so the definition drops out before
    // any argument runs.
    foreach (var definition in inferable.ToList()) {
      if (EvalInterpreter.UnfixableTypeParameter(definition.Method, call) is {} typeParameter) {
        var rejection = new Rejection(
          definition.Method,
          $"{typeParameter.Name} cannot be inferred (no argument fixes it)"
        );

        refused.Add(rejection);
        unfixable ??= rejection;
        inferable.Remove(definition);
      }
    }

    if (candidates.Count is 0 && inferable.Count is 0) {
      throw Missed(unfixable);
    }

    // Written type arguments resolve and instantiate before any argument evaluates.
    if (call.TypeArgs.Count > 0) {
      var typeArgs = call.TypeArgs.Select(name => this.ResolveType(name, call.Position)).ToArray();
      var definitions = candidates;

      candidates = [];

      // Every definition of the name is instantiated, since which one binds is only known once
      // the arguments meet the parameters.
      foreach (var definition in definitions) {
        try {
          candidates.Add(
            definition with {
              Method = inv.Instantiate(definition.Method, typeArgs)
            }
          );
        }
        catch (InvalidOperationException) {
          // The agent refuses type arguments a definition's constraints rule out, and the client
          // surfaces that with no message. A sibling may still bind, so the refusal is reported
          // only if nothing does.
          // The client throws the same exception for every other failure of the command, so
          // this catch cannot tell a constraint from a momentary fault.
          var written = string.Join(", ", call.TypeArgs);

          refused.Add(new Rejection(definition.Method, $"cannot be instantiated over <{written}>"));
        }
      }

      if (candidates.Count is 0) {
        throw Missed(null);
      }
    }

    var evaluated = new object[argc];
    Rejection uninferred = null;

    // The nearest tier's inference conflicts.
    List<InferenceConflict> conflicts = [];

    for (var i = 0; i < argc; i++) {
      evaluated[i] = call.Args[i].Mode switch {
        ArgMode.Plain => this.Evaluate(call.Args[i].Value),

        // A ref slot passes the local's current value in, and the call writes it back.
        ArgMode.Ref => this.locals[((NameExpr) call.Args[i].Value).Name],
        _ => null
      };
    }

    foreach (var fit in inferable) {
      var definition = fit.Method;

      var (typeArgs, reason, hasConflict, fixedTypes) =
        this.InferTypeArguments(definition, call, evaluated);

      if (typeArgs is not null) {
        try {
          candidates.Add(
            fit with {
              Method = inv.Instantiate(definition, typeArgs)
            }
          );

          continue;
        }
        catch (InvalidOperationException) {
          var inferred = string.Join(", ", typeArgs.Select(t => t.FullName));

          reason = $"cannot be instantiated over <{inferred}>";
        }
      }

      if (hasConflict) {
        var depth = EvalInterpreter.TierOf(declaringType, definition);

        if (conflicts.Count > 0 && depth < conflicts[0].Depth) {
          conflicts.Clear();
        }

        if (conflicts.Count is 0 || depth == conflicts[0].Depth) {
          conflicts.Add(new InferenceConflict(reason, depth, definition, fixedTypes));
        }
      }

      var rejection = new Rejection(definition, reason);

      refused.Add(rejection);
      uninferred ??= rejection;
    }

    // Every definition refused and nothing else to try: the miss says why inference did not
    // apply, so the caller knows writing the type argument is the fix rather than a retry.
    if (candidates.Count is 0) {
      throw Missed(uninferred);
    }

    foreach (var fit in candidates.ToList()) {
      if (this.ModeMismatch(fit.Method, call) is {} reason) {
        failures.Add(new Rejection(fit.Method, reason));
        candidates.Remove(fit);
      }
    }

    // The slots taking a placeholder rather than an argument: `out`, not `ref`.
    var outSet = outIndexes.Where(i => call.Args[i].Mode is not ArgMode.Ref).ToHashSet();

    // Within a tier, non-generic candidates still precede inferred ones.
    var (method, values) = this.SelectOverload(
        EvalInterpreter.Tiers(declaringType, candidates),
        evaluated,
        call.Position,
        failures,
        outSet
      ) ??
      throw Missed(null);

    // C# may well have inferred past a conflict and bound the generic, so another binding stands
    // in for it only where C# prefers that one over any instantiation: one from a nearer tier,
    // which hides the generic, or one from its own tier taking every argument as is.
    if (conflicts.Count > 0) {
      var depth = EvalInterpreter.TierOf(declaringType, method);

      var refusing = depth > conflicts[0].Depth
        ? conflicts[0]
        : depth == conflicts[0].Depth
          ? conflicts.FirstOrDefault(c => !this.TakesAsIs(method, evaluated, outSet, c))
          : null;

      if (refusing is not null) {
        throw Missed(new Rejection(refusing.Definition, refusing.Reason));
      }
    }

    // A struct local reaching a `ref` slot through a conversion gets the converted copy written
    // back, not the callee's value, so it binds only at its own type, as C# requires. A reference
    // local converts nothing, and an eval local has no declared type to match.
    var parameters = method.GetParameters();

    foreach (var i in outIndexes.Where(i => call.Args[i].Mode is ArgMode.Ref)) {
      var expected = parameters[i].ParameterType;

      if (Invoker.IsValueType(expected) &&
        !EvalInterpreter.IsNull(evaluated[i]) &&
        this.MirrorTypeOf(evaluated[i], call.Position) is var actual &&
        actual.FullName != expected.FullName) {
        throw new EvalRuntimeException(
          $"ref target '{((NameExpr) call.Args[i].Value).Name}' holds a {actual.FullName}, where " +
          $"'{parameters[i].Name}' takes a {expected.FullName}: `ref` passes a struct local only " +
          "of the parameter's exact type",
          call.Position
        );
      }
    }

    return (method, values, outIndexes);

    // A miss lists the candidates nearest the call first: those its arguments failed, then those
    // whose type arguments could not be had, then those its shape does not fit.
    EvalRuntimeException Missed(Rejection uninferredBy) {
      var receiver = EvalInterpreter.TryFlattenChain(call.Target, out var segments)
        ? string.Join(".", segments.Select(s => s.Name))
        : "value";

      var pooled = fits.Concat(definitionFits).ToList();
      var misfits = pooled.Where(f => !f.IsFit).Select(f => f.Rejection);

      // Every reachable candidate the type-argument count kept out of both pools.
      var typeArgMisfits = reachable.Except(pooled.Select(f => f.Method))
        .Select(c => new Rejection(
            c,
            c.IsGenericMethodDefinition
              ? $"takes {c.GetGenericArguments().Length} type argument(s)"
              : "takes no type arguments"
          )
        );

      var unreachable = named.Except(reachable)
        .Select(c => new Rejection(c, "an instance method: call it on a value"));

      var missed = new MissedCall(
        call.Name,
        argc,
        call.TypeArgs.Count,
        onType,
        receiver,
        uninferredBy,
        [.. failures, .. refused, .. misfits, .. typeArgMisfits, .. unreachable]
      );

      return new EvalRuntimeException(
        MissDiagnosis.Method(inv, catalog, declaringType, missed),
        call.Position
      );
    }
  }

  /// <summary>
  /// Fixes a generic definition's type parameters from the arguments, the way C# does for the one
  /// shape an agent writes by reflex: a parameter whose type IS a type parameter takes the
  /// argument's runtime type.
  /// Any other shape (`T[]`, `List&lt;T&gt;`, a `T` only the return type carries) is not inferred,
  /// nor is one two arguments fix to different types, nor one a null meets: each is reported as
  /// the reason, for the caller to write the type argument instead.
  /// Only a definition every type parameter of which a plain argument can fix reaches here (see
  /// <see cref="UnfixableTypeParameter" />).
  /// </summary>
  /// <returns>
  /// The type arguments in declaration order, or null with the reason, and whether that reason is
  /// a conflict, C# perhaps inferring past it: a disagreement, or a null beside an argument
  /// fixing the type parameter to a type taking it, or one that may be a box, whose declared type
  /// C# would fix instead.
  /// Every type parameter is fixed before a null's reason comes back, with the types fixed (see
  /// <see cref="InferenceConflict" />).
  /// </returns>
  private (TypeMirror[] Types, string Reason, bool HasConflict, TypeMirror[] Fixed)
    InferTypeArguments(
      MethodMirror definition,
      CallExpr call,
      object[] evaluated
    ) {
    var typeParameters = definition.GetGenericArguments();
    var parameters = definition.GetParameters();
    var fixedTypes = new TypeMirror[typeParameters.Length];
    (string Reason, bool HasConflict)? nullMet = null;

    for (var k = 0; k < typeParameters.Length; k++) {
      var fixedBy = -1;
      var nullAt = -1;

      // An omitted parameter fixes nothing, as in C#.
      for (var i = 0; i < call.Args.Count; i++) {
        // A type parameter's mirror is one object per attach, so identity is the test.
        if (parameters[i].ParameterType != typeParameters[k]) {
          continue;
        }

        if (call.Args[i].Mode is not (ArgMode.Plain or ArgMode.Ref)) {
          continue;
        }

        if (EvalInterpreter.IsNull(evaluated[i])) {
          nullAt = nullAt < 0 ? i : nullAt;

          continue;
        }

        var argumentType = this.MirrorTypeOf(evaluated[i], call.Position);

        if (fixedTypes[k] is null) {
          fixedTypes[k] = argumentType;
          fixedBy = i;
        }
        else if (fixedTypes[k] != argumentType) {
          return (
            null,
            $"{typeParameters[k].Name} fixed to {fixedTypes[k].Name} by argument " +
            $"{fixedBy + 1} and {argumentType.Name} by argument {i + 1}",
            true,
            null
          );
        }
      }

      // An instance that cannot take the null is inapplicable, as C# drops it, not a conflict,
      // unless the argument fixing it is a struct, which may be a box of a type taking it.
      if (nullAt >= 0) {
        nullMet ??= (
          $"{typeParameters[k].Name} cannot be inferred (argument {nullAt + 1} is null)",
          fixedTypes[k] is not null &&
          (Invoker.TakesNull(fixedTypes[k]) || evaluated[fixedBy] is StructMirror)
        );
      }
    }

    if (nullMet is var (reason, hasConflict)) {
      return (null, reason, hasConflict, fixedTypes);
    }

    return (fixedTypes, null, false, null);
  }

  /// <summary>
  /// Whether a bound candidate takes every argument as is: each non-null one at its own type, and
  /// a null at a slot no less specific than the conflicting generic's instance takes it at.
  /// </summary>
  private bool TakesAsIs(
    MethodMirror candidate,
    object[] args,
    IReadOnlySet<int> outIndexes,
    InferenceConflict conflict
  ) {
    var parameters = candidate.GetParameters();

    for (var i = 0; i < args.Length; i++) {
      if (outIndexes is not null && outIndexes.Contains(i)) {
        continue;
      }

      var slot = Invoker.Bare(parameters[i].ParameterType);

      var isAsIs = EvalInterpreter.IsNull(args[i])
        ? Invoker.TakesNull(slot) && IsNoLessSpecific(i, slot)
        : this.ArgumentCost(args[i], slot).Cost is EvalInterpreter.ExactCost;

      if (!isAsIs) {
        return false;
      }
    }

    return true;

    // An instance slot taking no null, or a type parameter only nulls met, which C# cannot
    // infer, leaves the generic inapplicable, any slot beating it.
    bool IsNoLessSpecific(int i, TypeMirror slot) {
      if (conflict.Fixed is null) {
        return false;
      }

      var genericSlot = conflict.Definition.GetParameters()[i].ParameterType;
      var k = Array.IndexOf(conflict.Definition.GetGenericArguments(), genericSlot);
      var instanceSlot = k >= 0 ? conflict.Fixed[k] : Invoker.Bare(genericSlot);

      return instanceSlot is null ||
        !Invoker.TakesNull(instanceSlot) ||
        EvalInterpreter.Depth(slot, instanceSlot) is not int.MaxValue;
    }
  }

  /// <summary>
  /// Why an argument's keyword does not match how its parameter is passed, or null when every one
  /// does: as in C#, `ref` and `out` each bind only their own kind and a plain argument neither.
  /// The agent reports a by-reference parameter's type by value, so only the assembly's metadata
  /// tells; where it cannot be read, plain and `out` arguments bind as the agent reports them.
  /// </summary>
  private string ModeMismatch(MethodMirror method, CallExpr call) {
    // Reading the modes fetches the assembly's image the first time, which a call with no
    // argument has nothing to check against.
    if (call.Args.Count is 0) {
      return null;
    }

    var modes = inv.Defaults.Modes(method);

    if (modes is null) {
      return call.Args.Any(static a => a.Mode is ArgMode.Ref)
        ? $"the metadata of {Invoker.SimpleAssemblyName(method.DeclaringType.Assembly)} could " +
        "not be read, so its ref parameters are unknown"
        : null;
    }

    var parameters = method.GetParameters();

    for (var i = 0; i < call.Args.Count; i++) {
      var name = parameters[i].Name;

      var reason = (call.Args[i].Mode, modes[i]) switch {
        (ArgMode.Plain, ParameterMode.Plain or ParameterMode.In) or
          (ArgMode.Ref, ParameterMode.Ref) or
          (ArgMode.Out or ArgMode.OutVar, ParameterMode.Out) => null,
        (_, ParameterMode.Ref) => $"'{name}' is a ref parameter: write `ref`",
        (_, ParameterMode.Out) => $"'{name}' is an out parameter: write `out`",
        (ArgMode.Ref, _) => $"'{name}' is not a ref parameter: drop `ref`",
        _ => $"'{name}' is not an out parameter: drop `out`"
      };

      if (reason is not null) {
        return reason;
      }
    }

    return null;
  }

  /// <summary>
  /// The first type parameter of a generic definition that no plain or `ref` argument meets as a
  /// parameter of exactly its type, or null when the arguments can fix them all.
  /// An out argument fixes nothing: it has no value yet, and the agent reports an `out T`
  /// parameter's type as plain `T`, so only the argument's mode tells it apart.
  /// An omitted parameter fixes nothing either, as in C#, even where it declares a default.
  /// </summary>
  private static TypeMirror UnfixableTypeParameter(MethodMirror definition, CallExpr call) {
    var parameters = definition.GetParameters();

    return definition.GetGenericArguments()
      .FirstOrDefault(typeParameter => !parameters
        .Take(call.Args.Count)
        .Where((p, i) =>
          p.ParameterType == typeParameter && call.Args[i].Mode is ArgMode.Plain or ArgMode.Ref
        )
        .Any()
      );
  }

  /// <summary>
  /// Whether a struct is a primitive the debuggee held boxed: the agent sends a box's contents
  /// rather than the box, a boxed primitive as a struct of its type where an unboxed one arrives
  /// as a <see cref="PrimitiveValue" />, so a struct of a primitive type can only have been boxed.
  /// A boxed struct leaves no such trace: it arrives exactly as an unboxed one.
  /// </summary>
  private static bool IsBoxedPrimitive(StructMirror value) {
    return Invoker.ClrPrimitiveOrNull(value.Type.FullName) is not null;
  }

  /// <summary>
  /// A boxed primitive as its value where the slot takes an enum, or a primitive the box's own
  /// type converts to exactly or by widening, so it binds and ranks by its runtime type as every
  /// argument does; anything else, a box headed for an object or interface slot or a narrower
  /// primitive included, as is.
  /// </summary>
  private static object UnboxFor(object value, TypeMirror slot) {
    if (EvalInterpreter.Unboxed(value) is not {} box) {
      return value;
    }

    var own = Invoker.ClrPrimitive(box.Type.FullName);

    return slot.IsEnum ||
      (Invoker.ClrPrimitiveOrNull(slot.FullName) is {} clr &&
        (clr == own || PrimitiveOps.CanWiden(own, clr)))
        ? box.Value
        : value;
  }

  /// <summary>
  /// A boxed primitive's type and value, whichever shape it arrives in: the agent returns a box as
  /// a struct of the primitive type, while one built client-side stays an object mirror, whose
  /// identity a reference comparison still reads; null for anything else.
  /// </summary>
  private static (TypeMirror Type, Value Value)? Unboxed(object value) {
    return value switch {
      StructMirror boxed when EvalInterpreter.IsBoxedPrimitive(boxed) =>
        (boxed.Type, boxed.Fields[0]),
      ObjectMirror boxed when Invoker.ClrPrimitiveOrNull(boxed.Type.FullName) is not null =>
        (boxed.Type, boxed.GetValue(Invoker.InstanceFields(boxed.Type)[0])),
      _ => null
    };
  }

  /// <summary>
  /// Candidates as C# member lookup ranks them: it binds the most derived type that declares an
  /// applicable member, so each level of the hierarchy is a tier of its own, nearest first.
  /// </summary>
  private static IReadOnlyList<IReadOnlyList<ArityFit>> Tiers(
    TypeMirror type,
    IEnumerable<ArityFit> candidates
  ) {
    return [
      .. candidates
        .GroupBy(c => EvalInterpreter.TierOf(type, c.Method))
        .OrderBy(level => level.Key)
        .Select(level => (IReadOnlyList<ArityFit>) level.ToList())
    ];
  }

  /// <summary>
  /// A candidate's tier below the type a call is written on, an override counting as declared
  /// where its method was introduced.
  /// </summary>
  private static int TierOf(TypeMirror type, MethodMirror method) =>
    EvalInterpreter.Depth(type, EvalInterpreter.SlotOwner(method));

  /// <summary>
  /// The type whose method a candidate is, for member lookup: its declaring type, except for an
  /// override, which belongs to the ancestor that introduced the slot it overrides.
  /// </summary>
  private static TypeMirror SlotOwner(MethodMirror method) {
    if (!EvalInterpreter.IsOverride(method)) {
      return method.DeclaringType;
    }

    var signature = EvalInterpreter.SlotSignature(method);

    for (var type = method.DeclaringType.BaseType; type is not null; type = type.BaseType) {
      var introduces = type.GetMethods()
        .Any(m => m.Name == method.Name &&
          m.IsVirtual &&
          !EvalInterpreter.IsOverride(m) &&
          EvalInterpreter.SlotSignature(m).SequenceEqual(signature)
        );

      if (introduces) {
        return type;
      }
    }

    return method.DeclaringType;
  }

  /// <summary>Whether a method overrides an inherited slot rather than declaring its own.</summary>
  private static bool IsOverride(MethodMirror method) {
    return method.IsVirtual &&
      (method.Attributes & MethodAttributes.VtableLayoutMask) is MethodAttributes.ReuseSlot;
  }

  /// <summary>
  /// What an override shares with the method it overrides: its generic arity and its parameter
  /// types, read off the generic definition so an inferred instantiation compares with an open
  /// one.
  /// </summary>
  private static string[] SlotSignature(MethodMirror method) {
    var definition = method.IsGenericMethod && !method.IsGenericMethodDefinition
      ? method.GetGenericMethodDefinition()
      : method;

    var typeParameters = definition.IsGenericMethodDefinition
      ? definition.GetGenericArguments()
      : [];

    return [
      $"{typeParameters.Length}",
      .. definition.GetParameters()
        .Select(p => EvalInterpreter.SlotKey(p.ParameterType, typeParameters))
    ];
  }

  /// <summary>
  /// A parameter type as <see cref="SlotSignature" /> compares it: a method type parameter, bare
  /// or nested in an array, a by-ref, a pointer, or a generic instance, keyed by its position,
  /// since an override may rename it.
  /// </summary>
  private static string SlotKey(TypeMirror type, TypeMirror[] typeParameters) {
    if (Array.IndexOf(typeParameters, type) is var position and >= 0) {
      return $"!!{position}";
    }

    if (type.IsArray) {
      var rank = new string(',', type.GetArrayRank() - 1);

      return $"{EvalInterpreter.SlotKey(type.GetElementType(), typeParameters)}[{rank}]";
    }

    if (type.IsByRef || type.IsPointer) {
      var suffix = type.IsByRef ? "&" : "*";

      return $"{EvalInterpreter.SlotKey(type.GetElementType(), typeParameters)}{suffix}";
    }

    if (type.IsGenericType && !type.IsGenericTypeDefinition) {
      var arguments = type.GetGenericArguments()
        .Select(argument => EvalInterpreter.SlotKey(argument, typeParameters));

      return $"{type.GetGenericTypeDefinition().FullName}[{string.Join(",", arguments)}]";
    }

    return type.FullName;
  }

  /// <summary>
  /// How many base-type steps separate a type from one of its ancestors; a type outside the chain
  /// sorts last.
  /// </summary>
  private static int Depth(TypeMirror type, TypeMirror ancestor) {
    var depth = 0;

    for (var current = type; current is not null; current = current.BaseType) {
      if (current == ancestor) {
        return depth;
      }

      depth++;
    }

    return int.MaxValue;
  }

  /// <summary>
  /// Picks the overload whose parameters accept the evaluated arguments, coercing each argument:
  /// an exact-type pass runs before a widening pass, so `M(int)` beats `M(long)` for an int
  /// argument regardless of enumeration order.
  /// Candidates come in tiers, each running both passes before the next is tried.
  /// Within a pass, a candidate needing fewer defaults for omitted parameters goes first, which
  /// is C#'s tie-breaker, and the omitted parameters take those defaults once the arguments bind.
  /// Out-argument slots take client-built default placeholders and never coerce.
  /// </summary>
  /// <param name="failures">Receives each candidate tried, with the conversion that failed.</param>
  /// <returns>
  /// The binding, or null when no candidate takes the arguments; a binding that would send a
  /// generic struct or nullable by reference throws instead.
  /// </returns>
  private (MethodMirror Method, Value[] Values)? SelectOverload(
    IReadOnlyList<IReadOnlyList<ArityFit>> tiers,
    object[] args,
    int position,
    List<Rejection> failures,
    IReadOnlySet<int> outIndexes = null
  ) {
    (MethodMirror Method, Value[] Values)? fallback = null;

    foreach (var (candidates, allowWidening) in tiers.SelectMany(t =>
        new[] { (t, false), (t, true) }
      )) {
      // The widening pass tries candidates in ascending conversion cost, approximating C#'s
      // better-conversion rule, so `M(long)` beats `M(double)` for an int argument regardless
      // of declaration order; the more specific reference targets break a tie, then fewer
      // omitted parameters, and declaration order any left (OrderBy is stable).
      var ordered = allowWidening
        ? candidates
          .Select(c => (Fit: c, Cost: this.ConversionCost(c.Method, args, outIndexes)))
          .OrderBy(c => c.Cost.Cost)
          .ThenByDescending(c => c.Cost.Specificity)
          .ThenBy(c => c.Fit.Omitted.Length)
          .Select(c => c.Fit)
        : candidates.OrderBy(c => c.Omitted.Length);

      foreach (var fit in ordered) {
        var candidate = fit.Method;
        var parameters = candidate.GetParameters();
        var values = new Value[parameters.Length];

        try {
          for (var i = 0; i < args.Length; i++) {
            var parameterType = parameters[i].ParameterType;
            var bareType = Invoker.Bare(parameterType);

            if (outIndexes is not null && outIndexes.Contains(i)) {
              values[i] = inv.DefaultMirrorFor(bareType);

              continue;
            }

            // The debugger agent hands the callee another address than the pointer sent, null
            // included, so no raw pointer argument can be passed faithfully. An IntPtr, or a
            // pointer held in a struct's field, arrives intact.
            if (bareType.IsPointer) {
              throw new EvalRuntimeException(
                $"cannot pass a pointer argument as {bareType.FullName}: the debugger agent " +
                "does not deliver its value; pass an IntPtr where the method takes one",
                position
              );
            }

            values[i] = this.CoerceToType(args[i], parameterType, position, allowWidening);
          }

          for (var i = args.Length; i < parameters.Length; i++) {
            values[i] = this.OmittedValue(
              fit.Omitted[i - args.Length],
              parameters[i].ParameterType,
              position
            );
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
            failures.Add(new Rejection(candidate, ex.Message));
          }

          continue;
        }

        // The numeric-to-enum convenience binds a call C# would not, so a binding only through
        // it yields to any other, a costlier sibling's or an ancestor's, and binds when none
        // applies.
        if (this.NeedsEnumConvenience(candidate, args, outIndexes)) {
          fallback ??= (candidate, values);

          continue;
        }

        return this.SendableByReference(candidate, values, position);
      }
    }

    return fallback is {} chosen
      ? this.SendableByReference(chosen.Method, chosen.Values, position)
      : null;
  }

  /// <summary>
  /// The binding, refused when it would send a generic struct or nullable by reference,
  /// through `ref` or into an `in` parameter, which disconnects the debugger agent.
  /// Refused once bound rather than dropped from the candidates,
  /// so no sibling C# would not pick binds in its place.
  /// The agent reports a by-reference parameter's type by value,
  /// so the slot's type says whether the value is at risk,
  /// and only then is the assembly's metadata read for how it is passed.
  /// </summary>
  private (MethodMirror Method, Value[] Values) SendableByReference(
    MethodMirror method,
    Value[] values,
    int position
  ) {
    var parameters = method.GetParameters();
    ParameterMode[] modes = null;

    for (var i = 0; i < values.Length; i++) {
      var type = parameters[i].ParameterType;

      if (values[i] is not StructMirror || !Invoker.BreaksByReference(type)) {
        continue;
      }

      modes ??= inv.Defaults.Modes(method);

      if (modes?[i] is ParameterMode.Ref or ParameterMode.In) {
        throw new EvalRuntimeException(
          $"'{parameters[i].Name}' takes its {type.FullName} by reference, which eval cannot send",
          position
        );
      }
    }

    return (method, values);
  }

  /// <summary>
  /// The value C# passes for an omitted parameter, built client-side with no invoke: its
  /// constant, a null constant reading as the slot's default (a zeroed struct for `= default`),
  /// and an optional parameter declaring no constant taking `default(T)`, or `Type.Missing` in an
  /// object slot, as the compiler emits.
  /// <see cref="ArityFit" /> has already refused a default or a slot this cannot build, save a
  /// zeroed generic struct, which <see cref="Invoker.DefaultMirrorFor" /> refuses here: the mirror
  /// reports an `in` parameter's type by value, so a by-ref slot, where one disconnects the agent,
  /// cannot be told apart by its type.
  /// </summary>
  private Value OmittedValue(DeclaredDefault declared, TypeMirror slot, int position) {
    var bare = Invoker.Bare(slot);

    return declared.Kind switch {
      DefaultKind.Optional when bare.FullName is "System.Object" =>
        (Value) this.ReadStaticMember(inv.ResolveType("System.Type"), "Missing", position),
      DefaultKind.Constant when declared.Constant is not null =>
        this.CoerceToType(declared.Constant, slot, position),
      DefaultKind.Constant or DefaultKind.Optional => this.DefaultOf(bare),
      _ => throw new UnreachableException($"eval cannot supply a {declared.Kind} default")
    };
  }

  /// <summary>C#'s <c>default(T)</c>, built client-side: null where the type takes one.</summary>
  private Value DefaultOf(TypeMirror type) =>
    Invoker.TakesNull(type) ? inv.NullFor(type) : inv.DefaultMirrorFor(type);

  /// <summary>
  /// Whether a bound candidate takes some argument only through the numeric-to-enum convenience:
  /// a non-zero integer into an enum slot.
  /// </summary>
  private bool NeedsEnumConvenience(
    MethodMirror candidate,
    object[] args,
    IReadOnlySet<int> outIndexes
  ) {
    var parameters = candidate.GetParameters();

    for (var i = 0; i < args.Length; i++) {
      var slot = Invoker.Bare(parameters[i].ParameterType);

      slot = Invoker.IsNullable(slot) ? slot.GetGenericArguments()[0] : slot;

      var arg = EvalInterpreter.UnwrapNullable(args[i]);

      if ((outIndexes is null || !outIndexes.Contains(i)) &&
        slot.IsEnum &&
        arg is not EnumMirror &&
        !EvalInterpreter.IsNull(arg) &&
        !EvalInterpreter.IsIntegerZero(EvalInterpreter.UnboxFor(arg, slot))) {
        return true;
      }
    }

    return false;
  }

  private (int Cost, int Specificity) ConversionCost(
    MethodMirror candidate,
    object[] args,
    IReadOnlySet<int> outIndexes
  ) {
    var parameters = candidate.GetParameters();
    var (total, specificity) = (0, 0);

    for (var i = 0; i < args.Length; i++) {
      if (outIndexes is null || !outIndexes.Contains(i)) {
        var cost = this.ArgumentCost(args[i], parameters[i].ParameterType);

        total += cost.Cost;
        specificity += cost.Specificity;
      }
    }

    return (total, specificity);
  }

  /// <summary>
  /// The widening pass's conversion-cost heuristic, approximating C#'s better-conversion rule:
  /// the cheaper of two candidates' conversions is the one C# prefers (see the cost bands), and
  /// of two at equal cost, the more specific.
  /// </summary>
  private (int Cost, int Specificity) ArgumentCost(object arg, TypeMirror parameterType) {
    var bare = Invoker.Bare(parameterType);

    arg = EvalInterpreter.UnboxFor(arg, bare);

    if (Invoker.ClrPrimitiveOrNull(bare.FullName) is {} clr) {
      var argType = EvalInterpreter.UnwrapForOps(arg)?.GetType();

      return argType == clr
        ? (EvalInterpreter.ExactCost, 0)
        : argType is not null && PrimitiveOps.CanWiden(argType, clr)
          ? (EvalInterpreter.NumericRank(clr), 0)
          : (EvalInterpreter.NarrowingCost + EvalInterpreter.NumericRank(clr), 0);
    }

    if ((EvalInterpreter.MirrorTypeOrNull(arg) is {} mirrorType && mirrorType == bare) ||
      (arg is string && bare.FullName is "System.String")) {
      return (EvalInterpreter.ExactCost, 0);
    }

    // A value lifted into a nullable slot costs its plain conversion and a little more, so
    // `M(long?)` beats `M(double?)` for an int, and `M(int)` beats `M(int?)`.
    if (Invoker.IsNullable(bare) && !EvalInterpreter.IsNull(arg)) {
      var (cost, specificity) = this.ArgumentCost(arg, bare.GetGenericArguments()[0]);

      return (cost + 1, specificity);
    }

    if (bare.FullName is "System.Object") {
      return (EvalInterpreter.ObjectCost, 0);
    }

    // A nullable is a better target for a null than any reference type it converts to, and a
    // primitive's narrowest one the best, as C# picks `M(int?)` over `M(long?)` and over
    // `M(ValueType)`.
    if (EvalInterpreter.IsNull(arg) && Invoker.IsNullable(bare)) {
      var underlying = Invoker.ClrPrimitiveOrNull(bare.GetGenericArguments()[0].FullName);

      return (
        EvalInterpreter.NullIntoNullableCost +
        (underlying is null ? EvalInterpreter.OtherRank : EvalInterpreter.NumericRank(underlying)),
        0
      );
    }

    // The numeric-to-enum convenience binds past boxing, as C# would not bind it at all, save for
    // a zero, which C# converts to any enum.
    if (bare.IsEnum) {
      return EvalInterpreter.IsIntegerZero(arg)
        ? (EvalInterpreter.ReferenceCost, 0)
        : (EvalInterpreter.EnumConvenienceCost, 0);
    }

    // Any other conversion reaches a class or an interface; the most
    // specific target, the deepest and the one with the most interfaces, wins a tie, as C# picks
    // `M(Derived)` over `M(Base)` and `M(IList<T>)` over `M(IEnumerable<T>)`.
    var depth = 0;

    for (var t = bare.BaseType; t is not null; t = t.BaseType) {
      depth++;
    }

    return (EvalInterpreter.ReferenceCost, depth + bare.GetInterfaces().Length);
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
      _ => EvalInterpreter.OtherRank
    };
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

    var accessors = Invoker.Named(type, "get_Item").ToList();

    if (accessors.Count is 0) {
      throw new EvalRuntimeException($"{type.FullName} has no indexer", position);
    }

    var (method, values) = this.SelectIndexer(type, accessors, args, position);

    return inv.Invoke(mirror, method, values);
  }

  /// <summary>
  /// Binds an indexer accessor at its own parameter count: a setter takes its value after any
  /// optional index, so omitting one is no trailing omission, and the getter matches it.
  /// </summary>
  private (MethodMirror Method, Value[] Values) SelectIndexer(
    TypeMirror type,
    IEnumerable<MethodMirror> accessors,
    object[] args,
    int position
  ) {
    return this.Bind(
      $"{type.FullName} indexer",
      type,
      [.. accessors.Select(a => ArityFit.Exact(a, args.Length))],
      args,
      position
    );
  }

  /// <summary>
  /// Binds the fitting candidates of a member of the type, or throws the miss listing every
  /// candidate with why it refused.
  /// </summary>
  /// <param name="subject">
  /// What was called, as <see cref="MissDiagnosis.Unbound" /> names it.
  /// </param>
  private (MethodMirror Method, Value[] Values) Bind(
    string subject,
    TypeMirror type,
    IReadOnlyList<ArityFit> fits,
    object[] args,
    int position
  ) {
    var failures = new List<Rejection>();

    return this.SelectOverload(
        EvalInterpreter.Tiers(type, fits.Where(f => f.IsFit)),
        args,
        position,
        failures
      ) ??
      throw new EvalRuntimeException(
        MissDiagnosis.Unbound(
          subject,
          args.Length,
          0,
          [.. failures, .. fits.Where(f => !f.IsFit).Select(f => f.Rejection)]
        ),
        position
      );
  }

  /// <summary>
  /// Validates a rank-1 single-index array access and returns the index; rank &gt; 1 is rejected
  /// (the low-level indexer is linear and would silently misread it), and any integral index
  /// type is accepted, as in C#.
  /// </summary>
  private int ArrayIndexOf(ArrayMirror array, object[] args, int position) {
    if (array.Rank is not 1) {
      throw new EvalRuntimeException(
        $"rank-{array.Rank} array indexing is not supported (rank 1 only)",
        position
      );
    }

    long? index = args.Length is 1
      ? EvalInterpreter.UnwrapForOps(args[0]) switch {
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
          : throw new EvalRuntimeException(
            "constructors take no out or ref arguments",
            creation.Position
          )
      )
      .ToArray();

    var constructors = type.GetMethods().Where(m => m.Name is ".ctor").ToList();

    Value instance;

    if (Invoker.IsValueType(type) &&
      args.Length is 0 &&
      constructors.All(c => c.GetParameters().Length is not 0)) {
      // Zeroed client-side default: no debuggee allocation, fields overwritten locally and
      // serialized on first send. A struct with a declared parameterless constructor must run it
      // instead; one whose parameters are all optional does not, as `new S()` in C# is the
      // default there too.
      instance = inv.DefaultMirrorFor(type);
    }
    else {
      var (ctor, values) = this.Bind(
        $"{type.FullName} constructor",
        type,
        [.. constructors.Select(c => ArityFit.Of(inv, c, args.Length))],
        args,
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
        var value = EvalInterpreter.CopyStruct(this.Evaluate(assignment.Value));

        this.locals[name.Name] = value;

        return value;
      }

      case NameExpr name when this.TryScopes(name.Name, out _): {
        // Writability is probed BEFORE the right side evaluates, so assigning a read-only root
        // (a builtin) rejects without running RHS side effects, like C#'s compile-time error;
        // writable scopes (frame locals/parameters) then take the evaluated value.
        var writable = scopes.FirstOrDefault(s => s.CanSetValue(name.Name));

        if (writable is null) {
          throw new EvalRuntimeException($"cannot assign builtin '{name.Name}'", name.Position);
        }

        var value = this.Evaluate(assignment.Value);

        return !writable.TrySetValue(name.Name, value)
          ? throw new EvalRuntimeException($"cannot assign '{name.Name}'", name.Position)
          : value;
      }

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
  /// A struct as a fresh copy, nested structs included, so a local bound to it is its own value as
  /// in C#: a member write mutates a client-side struct in place; anything else as is.
  /// </summary>
  internal static object CopyStruct(object value) {
    return value is StructMirror structMirror and not EnumMirror
      ? new StructMirror(
        structMirror.VirtualMachine,
        structMirror.Type,
        [.. structMirror.Fields.Select(f => (Value) EvalInterpreter.CopyStruct(f))]
      )
      : value;
  }

  /// <summary>C#'s CS1612 analog: a write into a struct temporary would be silently lost.</summary>
  private static void RequireAnchored(Lvalue target, int position) {
    if (target is { Value: StructMirror, Anchored: false }) {
      throw new EvalRuntimeException(
        target.Refusal ??
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
        var rootName = segments[0].Name;

        // Locals, scope values, and types anchor a chain (their mirrors are the stored
        // instances client-side), save a struct a scope's slot holds, which decodes as a copy:
        // a member write stores the whole struct back.
        var writeBack = root is StructMirror rootStruct &&
          next is 1 &&
          !this.locals.ContainsKey(rootName)
            ? scopes
              .Select(s => s.StructWriteBack(rootName, rootStruct))
              .FirstOrDefault(w => w is not null)
            : null;

        // A box a scope's slot typed object or as an interface holds stores back through it.
        var (boxScope, boxSlotType) = writeBack is null &&
          root is StructMirror &&
          next is 1 &&
          !this.locals.ContainsKey(rootName)
            ? scopes
              .Select(s => (Scope: s, Type: s.BoxSlotType(rootName)))
              .FirstOrDefault(s => s.Type is not null)
            : (null, null);

        var current = boxScope is not null
          ? EvalInterpreter.SlotLink(
            (Value) root,
            boxSlotType,
            rootName,
            () => boxScope.TrySetValue(rootName, root)
          )
          // A struct `this` no scope stores back takes no member write, which would change only
          // a copy; a call on it still runs.
          : writeBack is null && rootName is "this" && root is StructMirror
            ? new Lvalue(
              root,
              null,
              false,
              "this debugger agent cannot store a struct's `this` back, so a member assignment " +
              "through it would change only a copy"
            )
            : new Lvalue(root, writeBack, true);

        for (var i = next; i < segments.Count; i++) {
          current = this.ReadLink(current, segments[i].Name, segments[i].Position);
        }

        return current;
      }

      case MemberExpr member: {
        var container = this.EvaluateWritable(member.Target);

        return this.ReadLink(container, member.Name, member.Position);
      }

      case ImplicitReceiverExpr: return this.receivers.Peek();

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

  /// <summary>
  /// The link of a value a debuggee slot of <paramref name="slotType" /> holds (a field, an array
  /// element, a frame slot), a struct read from it storing back through <paramref name="store" />.
  /// A box, in a slot typed object or as an interface, is stored back only once changed, as a
  /// read-only call keeps the box, and the store replaces it with a new box; one typed object
  /// takes no member write, C# admitting none there.
  /// </summary>
  private static Lvalue SlotLink(Value value, TypeMirror slotType, string name, Action store) {
    if (value is not StructMirror structMirror) {
      return new Lvalue(value, null, true);
    }

    if (Invoker.IsValueType(slotType)) {
      return new Lvalue(value, store, true);
    }

    var changed = EvalInterpreter.ChangedOnly(structMirror, store);

    return slotType.IsInterface
      ? new Lvalue(value, changed, true)
      : new Lvalue(value, changed, false, EvalInterpreter.BoxRefusal(name));
  }

  /// <summary>
  /// A write-back firing only when the struct differs from what it was as this link read it.
  /// </summary>
  private static Action ChangedOnly(StructMirror value, Action writeBack) {
    if (writeBack is null) {
      return null;
    }

    var before = (Value) EvalInterpreter.CopyStruct(value);

    return () => {
      if (!EvalInterpreter.IsSameValue(before, value)) {
        writeBack();
      }
    };
  }

  /// <summary>
  /// Whether two decoded values hold the same data: the vendored PrimitiveValue.Equals compares
  /// boxed payloads by reference, so primitives compare by value here.
  /// </summary>
  private static bool IsSameValue(Value a, Value b) {
    return (a, b) switch {
      (StructMirror x, StructMirror y) => x.Type == y.Type &&
        x.Fields.Length == y.Fields.Length &&
        x.Fields.Zip(y.Fields).All(p => EvalInterpreter.IsSameValue(p.First, p.Second)),
      // A float's sign and payload count, which equality ignores for 0 and -0.
      (PrimitiveValue { Value: float f }, PrimitiveValue { Value: float g }) =>
        BitConverter.SingleToInt32Bits(f) == BitConverter.SingleToInt32Bits(g),
      (PrimitiveValue { Value: double f }, PrimitiveValue { Value: double g }) =>
        BitConverter.DoubleToInt64Bits(f) == BitConverter.DoubleToInt64Bits(g),
      (PrimitiveValue x, PrimitiveValue y) => Equals(x.Value, y.Value),
      (ObjectMirror x, ObjectMirror y) => x.Id == y.Id,
      _ => Equals(a, b)
    };
  }

  private static string BoxRefusal(string name) =>
    $"'{name}' holds a boxed struct, so a member assignment would change only a copy";

  /// <summary>One tracked member link of an lvalue chain (see <see cref="Lvalue" />).</summary>
  private Lvalue ReadLink(Lvalue container, string name, int position) {
    switch (container.Value) {
      case TypeRef typeRef when !typeRef.Type.IsEnum: {
        var field = typeRef.Type.GetFields().FirstOrDefault(f => f.IsStatic && f.Name == name);

        if (field is not null) {
          this.RequireInitialized(typeRef.Type, field, position);

          var value = typeRef.Type.GetValue(field);

          // Static struct fields decode as fresh copies: store the mutated copy back.
          return EvalInterpreter.SlotLink(
            value,
            field.FieldType,
            field.Name,
            () => typeRef.Type.SetValue(field, value)
          );
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
          // client-side, so persistence rides the parent's own write-back, a box's only once
          // changed (see SlotLink). Any other field kind is written directly (heap identity or
          // leaf), needing no replay.
          return value is not StructMirror nested
            ? new Lvalue(value, null, true)
            : Invoker.IsValueType(field.FieldType)
              ? container with {
                Value = value
              }
              : new Lvalue(
                value,
                EvalInterpreter.ChangedOnly(nested, container.WriteBack),
                container.Anchored && field.FieldType.IsInterface,
                field.FieldType.IsInterface ? container.Refusal : EvalInterpreter.BoxRefusal(name)
              );
        }

        if (getter is not null) {
          var value = inv.Invoke(structContainer, getter);

          return new Lvalue(value, null, value is not StructMirror);
        }

        throw new EvalRuntimeException(
          MissDiagnosis.InstanceMember(structContainer.Type, name, writing: false),
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
          return EvalInterpreter.SlotLink(
            value,
            field.FieldType,
            field.Name,
            () => objectContainer.SetValue(field, value)
          );
        }

        if (getter is not null) {
          var value = inv.Invoke(objectContainer, getter);

          return new Lvalue(value, null, value is not StructMirror);
        }

        throw new EvalRuntimeException(
          MissDiagnosis.InstanceMember(objectContainer.Type, name, writing: false),
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
      return EvalInterpreter.SlotLink(
        value,
        array.Type.GetElementType(),
        $"element {at}",
        () => array.SetValues(at, [value])
      );
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
          this.RequireInitialized(typeRef.Type, field, position);
          typeRef.Type.SetValue(field, this.CoerceToType(value, field.FieldType, position));

          return;
        }

        var setter = inv.FindMethodOrNull(typeRef.Type, $"set_{name}", 1);

        if (setter is not { IsStatic: true }) {
          throw new EvalRuntimeException(
            MissDiagnosis.StaticMember(typeRef.Type, name, writing: true),
            position
          );
        }

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

    var accessors = Invoker.Named(type, "set_Item").ToList();

    if (accessors.Count is 0) {
      throw new EvalRuntimeException($"{type.FullName} has no writable indexer", position);
    }

    var (method, values) = this.SelectIndexer(type, accessors, [.. args, value], position);

    inv.Invoke(mirror, method, values);
  }

  // ---- Casts ----

  private object EvaluateCast(CastExpr cast) {
    var type = this.ResolveType(cast.TypeName, cast.Position);
    var value = EvalInterpreter.UnwrapNullable(this.Evaluate(cast.Operand));
    var clr = Invoker.ClrPrimitiveOrNull(type.FullName);

    // An unboxing cast takes the box's own type only, as an enum over it, as C#'s throws
    // InvalidCastException for any other, even a wider one.
    if ((type.IsEnum
        ? type.EnumUnderlyingType
        : clr is not null
          ? type
          : null) is {} unboxTo &&
      EvalInterpreter.Unboxed(value) is {} box) {
      value = box.Type == unboxTo
        ? box.Value
        : throw new EvalRuntimeException(
          $"cannot unbox a {box.Type.FullName} as {type.FullName}",
          cast.Position
        );
    }

    if (type.IsEnum) {
      var numeric = EvalInterpreter.UnwrapForOps(value);

      // Strings are IConvertible but not castable in C#.
      return numeric is IConvertible and not string
        ? inv.MakeEnum(type, numeric)
        : throw new EvalRuntimeException(
          $"cannot cast {EvalInterpreter.TypeNameOf(value)} to enum {type.FullName}",
          cast.Position
        );
    }

    if (clr is not null) {
      var unwrapped = EvalInterpreter.UnwrapForOps(value);

      return unwrapped is IConvertible and not string
        ? Invoker.CastClient(unwrapped, clr)
        : throw new EvalRuntimeException(
          $"cannot cast {EvalInterpreter.TypeNameOf(value)} to {type.FullName}",
          cast.Position
        );
    }

    // Null casts to any target taking it; C# throws for any other.
    if (EvalInterpreter.IsNull(value)) {
      return Invoker.TakesNull(type)
        ? value
        : throw new EvalRuntimeException($"cannot cast null to {type.FullName}", cast.Position);
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
    return type.FullName is "System.Object" || value.GetType().FullName == type.FullName
      ? value
      : throw new EvalRuntimeException(
        $"cannot cast {EvalInterpreter.TypeNameOf(value)} to {type.FullName}",
        cast.Position
      );
  }

  // ---- Types ----

  private TypeMirror ResolveType(string dotted, int position) {
    return this.ResolveTypeOrNull(dotted) ??
      throw new EvalRuntimeException(
        catalog is null
          ? $"type '{dotted}' not found (names must be fully qualified)"
          : MissDiagnosis.Type(catalog, dotted.Split('.'), MissNames.Eval),
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

  /// <summary>
  /// A nullable as an operator reads it, as C# lifts one: its value, or null when it has none.
  /// Member access keeps the wrapper, which is what `.HasValue` and `.Value` are read from.
  /// </summary>
  private static object UnwrapNullable(object value) {
    if (value is not StructMirror nullable || !Invoker.IsNullable(nullable.Type)) {
      return value;
    }

    return nullable["hasValue"] is PrimitiveValue { Value: true } ? nullable["value"] : null;
  }

  /// <summary>Converts any in-flight value to a mirror for use as an invoke target.</summary>
  private Value ToMirror(object value, int position) {
    return value switch {
      Value mirror => mirror,
      null => inv.Vm.CreateValue(null),
      string s => inv.Str(s),
      not null when Invoker.ClrPrimitiveOrNull(value.GetType().FullName) is not null =>
        inv.Prim(value),
      _ => throw new EvalRuntimeException(
        $"cannot send a {value.GetType().FullName} to the debuggee",
        position
      )
    };
  }

  /// <summary>
  /// Coerces an in-flight value to a target (parameter or field) type, client-side: exact CLR
  /// match, plus, when <paramref name="allowWidening" /> permits, every conversion beyond it
  /// (overload binding's strict pass disables them all).
  /// </summary>
  private Value CoerceToType(
    object value,
    TypeMirror targetType,
    int position,
    bool allowWidening = true
  ) {
    var bareTarget = Invoker.Bare(targetType);
    var typeName = bareTarget.FullName;

    if (EvalInterpreter.IsNull(value)) {
      // A null fails here like any other mismatch, so overload matching moves on; it binds only in
      // the widening pass, whose cost ranking prefers the most specific slot.
      return !allowWidening || !Invoker.TakesNull(bareTarget)
        ? throw new EvalRuntimeException($"cannot pass null as {typeName}", position)
        : inv.NullFor(bareTarget);
    }

    // A nullable slot takes its underlying value as is, the agent wrapping it; a lifting
    // conversion, so not in the exact pass.
    if (allowWidening &&
      Invoker.IsNullable(bareTarget) &&
      EvalInterpreter.MirrorTypeOrNull(value) != bareTarget) {
      return this.CoerceToType(
        value,
        bareTarget.GetGenericArguments()[0],
        position,
        allowWidening
      );
    }

    value = EvalInterpreter.UnboxFor(value, bareTarget);

    if (value is TypeRef typeRef) {
      // A bare type where a System.Type is expected: the common typo for typeof(...).
      return typeName is "System.Type"
        ? inv.TypeObject(typeRef.Type)
        : throw new EvalRuntimeException(
          $"'{typeRef.Type.FullName}' is a type, not a value",
          position
        );
    }

    if (value is not Value mirror) {
      return this.CoerceClient(value, targetType, position, allowWidening);
    }

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

      case StringMirror when typeName is "System.String": return mirror;

      case StringMirror when allowWidening && typeName is "System.Object": return mirror;

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
          (typeName is "System.Object" || bareTarget.IsAssignableFrom(mirrorType))) {
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
    var bareTarget = Invoker.Bare(targetType);
    var typeName = bareTarget.FullName;

    if (bareTarget.IsEnum) {
      // Numeric-to-enum is admitted as a REPL convenience (C# itself would want a cast), for an
      // integer the enum's underlying type holds: any other would truncate or wrap into a
      // different member.
      if (!allowWidening || !Invoker.IsInteger(value.GetType())) {
        throw new EvalRuntimeException(
          $"cannot pass {value.GetType().FullName} as enum {typeName}",
          position
        );
      }

      try {
        return inv.MakeEnumChecked(bareTarget, value);
      }
      catch (OverflowException) {
        throw new EvalRuntimeException(
          $"{Convert.ToString(value, CultureInfo.InvariantCulture)} does not fit {typeName}",
          position
        );
      }
    }

    switch (typeName) {
      case "System.String":
        return value is string s
          ? inv.Str(s)
          : throw new EvalRuntimeException(
            $"cannot pass {value.GetType().FullName} as string",
            position
          );

      case "System.Object" when allowWidening: {
        var sent = this.ToMirror(value, position);

        // The agent boxes a struct sent for an object parameter and rejects a primitive, so a
        // primitive goes out already boxed.
        return sent is PrimitiveValue primitive ? inv.Box(primitive) : sent;
      }
    }

    var clr = Invoker.ClrPrimitiveOrNull(typeName);

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
      return inv.Prim(Invoker.CastClient(value, clr));
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

  /// <summary>An integer zero, the one number C# converts to any enum.</summary>
  private static bool IsIntegerZero(object value) {
    return EvalInterpreter.UnwrapForOps(value) is {} number &&
      Invoker.IsInteger(number.GetType()) &&
      Convert.ToDecimal(number, CultureInfo.InvariantCulture) is 0;
  }

  private static bool IsIntegral(Type type) => type == typeof(char) || Invoker.IsInteger(type);

  // ---- Formatting and reporting ----

  private string Stringify(object value, string format = null) {
    return EvalInterpreter.UnwrapNullable(value) switch {
      null => "",
      string s => s,
      IFormattable f => f.ToString(format, CultureInfo.InvariantCulture),
      StringMirror s => s.Value,
      PrimitiveValue p => this.Stringify(p.Value, format),
      EnumMirror e => inv.EnumName(e),
      Value v => inv.Format(v),
      _ => value.ToString()
    };
  }

  /// <summary>
  /// Renders an interpolation hole, a format clause reaching the value's
  /// <c>IFormattable.ToString</c>.
  /// An object or struct runs it in the debuggee, under the debuggee's culture.
  /// A client value or an unboxed debuggee primitive runs it client-side, under the invariant one.
  /// A value that is not formattable ignores the clause, rendering as an unformatted hole does.
  /// </summary>
  private string RenderHole(object value, string format) {
    var unwrapped = EvalInterpreter.UnwrapNullable(value);

    if (format is null || EvalInterpreter.IsNull(unwrapped)) {
      return this.Stringify(value);
    }

    if (unwrapped is not (ObjectMirror or StructMirror)) {
      return this.Stringify(unwrapped, format);
    }

    var mirror = (Value) unwrapped;

    if (EvalInterpreter.FormattableToString(EvalInterpreter.MirrorTypeOrNull(mirror)) is not {
      } method) {
      return this.Stringify(mirror);
    }

    var rendered = inv.Invoke(mirror, method, inv.Str(format), inv.Prim(null));

    return (rendered as StringMirror)?.Value ?? "";
  }

  /// <summary>
  /// The type's <c>IFormattable.ToString(string, IFormatProvider)</c>, public or explicitly
  /// implemented, along its base chain; null when the type is not formattable.
  /// </summary>
  private static MethodMirror FormattableToString(TypeMirror type) {
    var isFormattable = false;

    for (var t = type; t is not null && !isFormattable; t = t.BaseType) {
      isFormattable = t.GetInterfaces().Any(static i => i.FullName is "System.IFormattable");
    }

    if (!isFormattable) {
      return null;
    }

    return Invoker.Named(type, "ToString")
      .Concat(Invoker.Named(type, "System.IFormattable.ToString"))
      .FirstOrDefault(static m => m.GetParameters() is [
          { ParameterType.FullName: "System.String" },
          { ParameterType.FullName: "System.IFormatProvider" }
        ]
      );
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

    // A game throw already renders itself, so the report speaks with its words: whoever reads the
    // message alone (a breakpoint condition error) gets the game's exception too.
    // The cause travels along, leaving this report to add only the statement context and locals the
    // evaluator knows.
    var message = cause switch {
      ObjectCollectedException =>
        "a previous result was garbage-collected after the game resumed; " +
        "re-evaluate it instead of using `_`",
      GameException game => game.Message,
      _ => cause.Message
    };

    return new EvalFailedException(message, cause) {
      StatementIndex = statementIndex,
      StatementSource = source,
      Position = position,
      Locals = this.locals.Select(local =>
          new KeyValuePair<string, string>(local.Key, this.FormatLocalSafely(local.Value))
        )
        .ToArray()
    };
  }

  private string FormatLocalSafely(object value) {
    try {
      return this.FormatValue(value, 1);
    }
    catch {
      return "<unreadable>";
    }
  }

  /// <summary>
  /// An inference conflict a generic definition met, for another binding to stand in past.
  /// </summary>
  /// <param name="Depth">The definition's tier, as <see cref="TierOf" /> counts it.</param>
  /// <param name="Fixed">
  /// The type arguments fixed before a null met the conflict, by type-parameter position; null
  /// for a disagreement, whose two types make no instance.
  /// </param>
  private sealed record InferenceConflict(
    string Reason,
    int Depth,
    MethodMirror Definition,
    TypeMirror[] Fixed
  );

  /// <summary>
  /// An assignment target's container: the value written into, whether mutations to it persist
  /// (<see cref="Anchored" />: a local, a live object, or a chain of tracked links rooted in
  /// one), and the write-back replaying by-copy struct links outward. Object fields, array
  /// elements, and static fields decode structs as fresh client copies, so C# lvalue semantics
  /// require storing the mutated copy back after the leaf write.
  /// </summary>
  /// <param name="Refusal">
  /// Why an unanchored struct takes no write, where the generic copy explanation would mislead.
  /// An unanchored struct carrying one is held somewhere rather than a temporary, so a call runs
  /// on it rather than on a copy.
  /// </param>
  private readonly record struct Lvalue(
    object Value,
    Action WriteBack,
    bool Anchored,
    string Refusal = null
  );

  /// <summary>A name chain resolved to a type instead of a value (static context).</summary>
  private sealed record TypeRef(TypeMirror Type);
}

/// <summary>A successful evaluation: the final value, formatted, with its type name.</summary>
public sealed record EvalOutcome {
  public required string Formatted { get; init; }

  public required string TypeName { get; init; }

  /// <summary>
  /// The raw in-flight result (a mirror or a client-side CLR value), for callers that branch on it
  /// rather than render it (breakpoint conditions).
  /// </summary>
  public object Value { get; init; }
}
