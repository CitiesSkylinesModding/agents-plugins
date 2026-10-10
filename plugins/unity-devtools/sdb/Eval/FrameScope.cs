using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb.Eval;

/// <summary>
/// A StackFrame-backed binding scope: resolves <c>this</c>, the frame's parameters, and its visible
/// locals, and writes locals/parameters back through
/// <see cref="StackFrame.SetValue(LocalVariable, Value)"/>, giving frame-context evaluation the
/// exact grammar and walker of the frameless eval.
/// Locals need debug info; without it, only parameters and <c>this</c> resolve.
/// A struct's <c>this</c> stores back, whole or after a member write through the regular lvalue
/// machinery (<see cref="StructWriteBack" />), on an agent at protocol 2.44 or later, the first
/// able to store it; a class's takes member writes only, as in C#.
/// Writes follow C# implicit-conversion rules (see <see cref="Coerce" />).
/// Anything else a C# assignment would reject fails loudly, save the read-only slots and enum
/// conveniences the plugin's documented divergences list, with the explicit cast still available
/// in the expression itself (<c>n = (int) 2.9</c> converts client-side before reaching this
/// scope).
/// </summary>
/// <param name="writes">
/// The locals written in this pause, shared across its evaluations (see <see cref="SetLocal" />);
/// a fresh set where the scope lives for one evaluation.
/// </param>
public sealed class FrameScope(
  Invoker inv,
  ThreadMirror thread,
  int frameIndex,
  Dictionary<(long Thread, int Frame, string Name), Value> writes = null
) : IEvalScope {
  private readonly Dictionary<(long Thread, int Frame, string Name), Value> localWrites =
    writes ?? [];

  /// <summary>The thread whose frame this scope reads.</summary>
  public ThreadMirror Thread => thread;

  /// <summary>
  /// The live frame, re-resolved by index on EVERY access: an invoke evaluated mid-expression
  /// (a property getter, a method call) regenerates the thread's frames agent-side, so a cached
  /// StackFrame's ID goes stale and every later slot read/write would fail with
  /// InvalidStackFrameException. GetFrames returns the client cache between invalidations, so
  /// the re-resolution is free when no invoke happened.
  /// </summary>
  private StackFrame Frame {
    get {
      var frames = thread.GetFrames();

      return frameIndex >= 0 && frameIndex < frames.Length
        ? frames[frameIndex]
        : throw new EvalRuntimeException(
          $"frame {frameIndex} is gone (the thread's stack changed mid-evaluation)"
        );
    }
  }

  public IEnumerable<string> Names {
    get {
      var frame = this.Frame;
      var locals = FrameScope.Swallowing(frame.GetVisibleVariables) ?? [];

      return [
        "this",
        .. locals.Where(v => !v.IsArg).Select(v => v.Name),
        .. frame.Method.GetParameters().Select(p => p.Name)
      ];
    }
  }

  public bool TryResolveValue(string name, out object value) {
    if (name is "this") {
      var frame = this.Frame;

      value = FrameScope.Swallowing(frame.GetThis);

      return value is not null;
    }

    if (this.FindLocal(name) is {} local) {
      value = this.localWrites.TryGetValue((thread.Id, frameIndex, name), out var written)
        ? EvalInterpreter.CopyStruct(written)
        : this.Frame.GetValue(local);

      return true;
    }

    if (this.FindParameter(name) is {} parameter) {
      value = this.Frame.GetValue(parameter);

      return true;
    }

    value = null;

    return false;
  }

  public bool TryCall(string name, object[] args, out object result) {
    result = null;

    return false;
  }

  /// <summary>
  /// Writability probe, consulted BEFORE the assignment's right side evaluates, so a bad target
  /// fails before any side effects run (like C#'s compile-time rejection).
  /// </summary>
  public bool CanSetValue(string name) {
    if (name is "this") {
      _ = this.StructThisType();

      return true;
    }

    return this.FindLocal(name) is not null || this.FindParameter(name) is not null;
  }

  public bool TrySetValue(string name, object value) {
    if (name is "this") {
      var type = this.StructThisType();

      this.SetThis(
        value is StructMirror structMirror && structMirror.Type == type
          ? structMirror
          : throw new EvalRuntimeException(
            $"cannot write {FrameScope.Describe(value)} into " +
            $"'this' ({type.FullName})"
          )
      );

      return true;
    }

    if (this.FindLocal(name) is {} local) {
      this.SetLocal(local, this.Coerce(value, local.Type, name));

      return true;
    }

    if (this.FindParameter(name) is {} parameter) {
      this.Frame.SetValue(parameter, this.Coerce(value, parameter.ParameterType, name));

      return true;
    }

    return false;
  }

  /// <summary>
  /// Stores a struct back into a value-typed local or parameter, or into a struct method's
  /// <c>this</c>, the caller's storage; null for a slot typed object or as an interface, which
  /// holds a box (see <see cref="BoxSlotType" />), and for a <c>this</c> an agent below protocol
  /// 2.44 cannot store.
  /// </summary>
  public Action StructWriteBack(string name, StructMirror value) {
    if (name is "this") {
      var method = this.Frame.Method;

      return method.IsStatic ||
        !Invoker.IsValueType(method.DeclaringType) ||
        !inv.Vm.Version.AtLeast(2, 44)
          ? null
          : () => this.SetThis(value);
    }

    if (this.FindLocal(name) is {} local) {
      return Invoker.IsValueType(local.Type) ? () => this.SetLocal(local, value) : null;
    }

    if (this.FindParameter(name) is {} parameter) {
      return Invoker.IsValueType(parameter.ParameterType)
        ? () => this.Frame.SetValue(parameter, value)
        : null;
    }

    return null;
  }

  public TypeMirror BoxSlotType(string name) {
    var type = name is "this"
      ? null
      : this.FindLocal(name)?.Type ?? this.FindParameter(name)?.ParameterType;

    return type is not null && !Invoker.IsValueType(type) ? type : null;
  }

  /// <summary>
  /// The struct whose method the frame runs, for a write of <c>this</c> itself, which fails
  /// loudly where it cannot store: a class's (C# CS1604), or one on an agent below 2.44.
  /// </summary>
  private TypeMirror StructThisType() {
    var method = this.Frame.Method;

    if (method.IsStatic) {
      throw new EvalRuntimeException("a static method has no `this`");
    }

    if (!Invoker.IsValueType(method.DeclaringType)) {
      throw new EvalRuntimeException(
        "cannot assign `this` in a class method; assign its members instead (this.someField = ...)"
      );
    }

    return inv.Vm.Version.AtLeast(2, 44)
      ? method.DeclaringType
      : throw new EvalRuntimeException("this debugger agent cannot store a struct's `this` back");
  }

  /// <summary>
  /// Writes a local and remembers what the agent stored, its nullable wrapper or box included,
  /// which a read answers from then on: the agent serves a register-held local it wrote stale
  /// once an invoke ran, though the resumed code gets the write
  /// (docs/solutions/mono-debuggee-answers-over-sdb.md).
  /// A read straight after the write, with no invoke between, still gets the stored value.
  /// </summary>
  private void SetLocal(LocalVariable local, Value value) {
    var frame = this.Frame;

    frame.SetValue(local, value);
    this.localWrites[(thread.Id, frameIndex, local.Name)] = frame.GetValue(local);
  }

  private void SetThis(StructMirror value) {
    // The client's SetThis refuses a `this` the agent does not flag a value type, a generic
    // struct's among them, though the agent stores it, so the command goes out directly
    // (docs/solutions/sdb-vendored-client-limits.md).
    inv.Vm.conn.StackFrame_SetThis(thread.Id, this.Frame.Id, inv.Vm.EncodeValue(value));
  }

  private LocalVariable FindLocal(string name) {
    // Locals come from the PDB scope table; a method without debug info has none visible, and the
    // frame's parameters (metadata, always present) remain the only named slots.
    var locals = FrameScope.Swallowing(this.Frame.GetVisibleVariables);

    return locals?.FirstOrDefault(v => v.Name == name && !v.IsArg);
  }

  private ParameterInfoMirror FindParameter(string name) =>
    this.Frame.Method.GetParameters().FirstOrDefault(p => p.Name == name);

  /// <summary>
  /// Converts an in-flight value to a mirror for the slot's exact type: mirrors pass through
  /// (the agent's type check rejects a mismatch loudly, save a struct into a reference-typed
  /// slot, checked here), save a primitive, which converts as a client one does unless the slot
  /// is its own type's nullable, which the agent wraps it into.
  /// Client primitives follow C# implicit-conversion rules (exact or widening; never rounding,
  /// never bool/number mixing), box into an object slot, and reach enum slots through the
  /// underlying type when it holds them (the eval grammar's documented numeric-to-enum
  /// convenience).
  /// Null only fits reference-typed, nullable, and pointer slots (a pointer taking it as a zero
  /// pointer).
  /// </summary>
  private Value Coerce(object value, TypeMirror slotType, string name) {
    // A debuggee primitive converts as a client one does, so an int local widens into a long
    // one, save into its own type's nullable, which the agent wraps it into.
    if (value is PrimitiveValue { Value: {} raw } &&
      !(Invoker.IsNullable(slotType) &&
        Invoker.ClrPrimitiveOrNull(slotType.GetGenericArguments()[0].FullName) == raw.GetType())) {
      value = raw;
    }

    switch (value) {
      // The agent stores a struct into any reference-typed slot, whatever its type.
      case StructMirror structMirror when !Invoker.IsValueType(slotType) &&
        !slotType.IsAssignableFrom(structMirror.Type):
        throw new EvalRuntimeException(
          $"cannot write {FrameScope.Describe(value)} into '{name}' ({slotType.FullName}): " +
          (slotType.IsInterface ? "it does not implement the interface" : "no implicit conversion")
        );

      case Value mirror: return mirror;

      case null when !Invoker.TakesNull(slotType):
        throw new EvalRuntimeException(
          $"cannot write null into '{name}' (value type {slotType.FullName})"
        );

      case null: return inv.NullFor(slotType);

      case string s:
        return slotType.IsAssignableFrom(inv.ResolveType("System.String"))
          ? inv.Str(s)
          : throw new EvalRuntimeException(
            $"cannot write a string into '{name}' ({slotType.FullName})"
          );
    }

    if (slotType.FullName is "System.Object") {
      return inv.Box(inv.Prim(value));
    }

    if (slotType.IsEnum) {
      if (!Invoker.IsInteger(value.GetType())) {
        throw new EvalRuntimeException(
          $"cannot write a {value.GetType().Name} into enum variable '{name}' " +
          $"({slotType.FullName}); use an integer or the enum constant"
        );
      }

      try {
        return inv.MakeEnumChecked(slotType, value);
      }
      catch (OverflowException) {
        throw new EvalRuntimeException(
          $"{Convert.ToString(value, CultureInfo.InvariantCulture)} does not fit " +
          $"{slotType.FullName} for '{name}'"
        );
      }
    }

    var source = value.GetType();
    var target = FrameScope.PrimitiveFor(value, slotType.FullName, name);

    if (source == target) {
      return inv.Prim(value);
    }

    if (PrimitiveOps.CanWiden(source, target)) {
      // The cast is the implicit conversion C# applies, rounding into float or double as it does.
      return inv.Prim(Invoker.CastClient(value, target));
    }

    throw new EvalRuntimeException(
      $"no implicit conversion from {source.Name} to {slotType.FullName} for '{name}' " +
      "(cast explicitly in the expression, e.g. `(int) value`)"
    );
  }

  /// <summary>The CLR primitive a slot type maps to; unsupported slot kinds fail loudly.</summary>
  private static Type PrimitiveFor(object value, string fullName, string name) {
    return Invoker.ClrPrimitiveOrNull(fullName) ??
      throw new EvalRuntimeException(
        $"cannot write {FrameScope.Describe(value)} into '{name}' ({fullName}): a frame write " +
        "converts a number or bool, debuggee-side or not, only into a numeric, enum, or object " +
        "slot, or a debuggee one into its own type's nullable; for an interface slot, assign a " +
        "boxed value a debuggee call returns (e.g. `System.Convert.ChangeType(n, typeof(int))`)"
      );
  }

  /// <summary>A value as a refusal names it, by its type.</summary>
  private static string Describe(object value) {
    var type = value switch {
      StructMirror structMirror => structMirror.Type.FullName,
      ObjectMirror objectMirror => objectMirror.Type.FullName,
      PrimitiveValue { Value: {} raw } => raw.GetType().Name,
      null or PrimitiveValue => null,
      _ => value.GetType().Name
    };

    return type is null ? "null" : $"a value of type {type}";
  }

  /// <summary>
  /// Best-effort frame reads: AbsentInformationException (no debug info) and agent errors on
  /// unusual frames (native transitions) mean "nothing resolvable here", not a failure.
  /// </summary>
  private static T Swallowing<T>(Func<T> read) where T : class {
    try {
      return read();
    }
    catch {
      return null;
    }
  }
}
