using System.Globalization;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb.Eval;

/// <summary>
/// A StackFrame-backed binding scope: resolves <c>this</c>, the frame's parameters, and its visible
/// locals, and writes locals/parameters back through
/// <see cref="StackFrame.SetValue(LocalVariable, Value)"/>, giving frame-context evaluation the
/// exact grammar and walker of the frameless eval.
/// Locals need debug info; without it, only parameters and <c>this</c> resolve.
/// Rebinding <c>this</c> itself stays unsupported (member writes through it work: they go through
/// the regular lvalue machinery, not this scope).
/// Writes follow C# implicit-conversion rules (exact match or numeric widening only); anything a
/// C# assignment would reject fails loudly, with the explicit cast still available in the
/// expression itself (<c>n = (int) 2.9</c> converts client-side before reaching this scope).
/// </summary>
public sealed class FrameScope(Invoker inv, ThreadMirror thread, int frameIndex) : IEvalScope {
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

  public bool TryResolveValue(string name, out object value) {
    if (name is "this") {
      var frame = this.Frame;

      value = FrameScope.Swallowing(frame.GetThis);

      return value is not null;
    }

    if (this.FindLocal(name) is {} local) {
      value = this.Frame.GetValue(local);

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
      throw new EvalRuntimeException(
        "cannot rebind `this`; assign its members instead (this.someField = ...)"
      );
    }

    return this.FindLocal(name) is not null || this.FindParameter(name) is not null;
  }

  public bool TrySetValue(string name, object value) {
    if (name is "this") {
      throw new EvalRuntimeException(
        "cannot rebind `this`; assign its members instead (this.someField = ...)"
      );
    }

    if (this.FindLocal(name) is {} local) {
      this.Frame.SetValue(local, this.Coerce(value, local.Type, name));

      return true;
    }

    if (this.FindParameter(name) is {} parameter) {
      this.Frame.SetValue(parameter, this.Coerce(value, parameter.ParameterType, name));

      return true;
    }

    return false;
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
  /// (the client-side SetValue type check rejects mismatches loudly), client primitives follow
  /// C# implicit-conversion rules (exact or widening; never rounding, never bool/number mixing),
  /// integral values reach enum slots through the underlying type (the eval grammar's documented
  /// numeric-to-enum convenience), and null only fits reference-typed slots.
  /// </summary>
  private Value Coerce(object value, TypeMirror slotType, string name) {
    switch (value) {
      case Value mirror: return mirror;

      case null:
        return slotType.IsValueType
          ? throw new EvalRuntimeException(
            $"cannot write null into '{name}' (value type {slotType.FullName})"
          )
          : inv.Vm.CreateValue(null);

      case string s:
        return slotType.FullName is "System.String" or "System.Object"
          ? inv.Str(s)
          : throw new EvalRuntimeException(
            $"cannot write a string into '{name}' ({slotType.FullName})"
          );
    }

    if (slotType.IsEnum) {
      if (!FrameScope.IsIntegral(value)) {
        throw new EvalRuntimeException(
          $"cannot write a {value.GetType().Name} into enum variable '{name}' " +
          $"({slotType.FullName}); use an integral value or the enum constant"
        );
      }

      // The wire value must carry the enum's exact underlying primitive; the unchecked truncating
      // conversion matches C# enum-cast semantics (and the interpreter's MakeEnum).
      var underlying = FrameScope.PrimitiveFor(slotType.EnumUnderlyingType.FullName, name);

      return inv.Vm.CreateEnumMirror(
        slotType,
        inv.Prim(FrameScope.ConvertUnchecked(value, underlying))
      );
    }

    var target = FrameScope.PrimitiveFor(slotType.FullName, name);
    var source = value.GetType();

    if (source == target) {
      return inv.Prim(value);
    }

    if (FrameScope.WidensTo(source, target)) {
      // Widening never rounds or truncates, so ChangeType is exact here.
      return inv.Prim(Convert.ChangeType(value, target, CultureInfo.InvariantCulture));
    }

    throw new EvalRuntimeException(
      $"no implicit conversion from {source.Name} to {slotType.FullName} for '{name}' " +
      "(cast explicitly in the expression, e.g. `(int) value`)"
    );
  }

  private static bool IsIntegral(object value) =>
    value is sbyte or byte or short or ushort or int or uint or long or ulong or char;

  /// <summary>The CLR primitive a slot type maps to; unsupported slot kinds fail loudly.</summary>
  private static Type PrimitiveFor(string fullName, string name) {
    return fullName switch {
      "System.Boolean" => typeof(bool),
      "System.Char" => typeof(char),
      "System.SByte" => typeof(sbyte),
      "System.Byte" => typeof(byte),
      "System.Int16" => typeof(short),
      "System.UInt16" => typeof(ushort),
      "System.Int32" => typeof(int),
      "System.UInt32" => typeof(uint),
      "System.Int64" => typeof(long),
      "System.UInt64" => typeof(ulong),
      "System.Single" => typeof(float),
      "System.Double" => typeof(double),
      _ => throw new EvalRuntimeException(
        $"cannot write a client-side value into '{name}' ({fullName}); build the value " +
        "debuggee-side (e.g. with `new`) and assign that"
      )
    };
  }

  /// <summary>C#'s implicit numeric widening table (bool and char-as-target excluded).</summary>
  private static bool WidensTo(Type source, Type target) {
    return
      (source == typeof(sbyte) &&
        (target == typeof(short) ||
          target == typeof(int) ||
          target == typeof(long) ||
          target == typeof(float) ||
          target == typeof(double))) ||
      (source == typeof(byte) &&
        (target == typeof(short) ||
          target == typeof(ushort) ||
          target == typeof(int) ||
          target == typeof(uint) ||
          target == typeof(long) ||
          target == typeof(ulong) ||
          target == typeof(float) ||
          target == typeof(double))) ||
      (source == typeof(short) &&
        (target == typeof(int) ||
          target == typeof(long) ||
          target == typeof(float) ||
          target == typeof(double))) ||
      (source == typeof(ushort) &&
        (target == typeof(int) ||
          target == typeof(uint) ||
          target == typeof(long) ||
          target == typeof(ulong) ||
          target == typeof(float) ||
          target == typeof(double))) ||
      (source == typeof(int) &&
        (target == typeof(long) || target == typeof(float) || target == typeof(double))) ||
      (source == typeof(uint) &&
        (target == typeof(long) ||
          target == typeof(ulong) ||
          target == typeof(float) ||
          target == typeof(double))) ||
      (source == typeof(long) && (target == typeof(float) || target == typeof(double))) ||
      (source == typeof(ulong) && (target == typeof(float) || target == typeof(double))) ||
      (source == typeof(char) &&
        (target == typeof(ushort) ||
          target == typeof(int) ||
          target == typeof(uint) ||
          target == typeof(long) ||
          target == typeof(ulong) ||
          target == typeof(float) ||
          target == typeof(double))) ||
      (source == typeof(float) && target == typeof(double));
  }

  /// <summary>Unchecked truncating conversion to an enum's underlying primitive.</summary>
  private static object ConvertUnchecked(object value, Type underlying) {
    var wide = value switch {
      ulong u => unchecked((long) u),
      char c => c,
      _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
    };

    return underlying switch {
      _ when underlying == typeof(sbyte) => unchecked((sbyte) wide),
      _ when underlying == typeof(byte) => unchecked((byte) wide),
      _ when underlying == typeof(short) => unchecked((short) wide),
      _ when underlying == typeof(ushort) => unchecked((ushort) wide),
      _ when underlying == typeof(int) => unchecked((int) wide),
      _ when underlying == typeof(uint) => unchecked((uint) wide),
      _ when underlying == typeof(long) => wide,
      _ => unchecked((ulong) wide)
    };
  }

  /// <summary>
  /// Best-effort frame reads: AbsentInformationException (no debug info) and agent errors on
  /// unusual frames (native transitions) mean "nothing resolvable here", not a failure.
  /// </summary>
  private static T Swallowing<T>(Func<T> read)
    where T : class {
    try {
      return read();
    }
    catch {
      return null;
    }
  }
}
