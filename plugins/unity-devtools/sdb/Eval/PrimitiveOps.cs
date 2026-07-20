using Microsoft.CSharp.RuntimeBinder;

namespace UnityDevtools.Sdb.Eval;

/// <summary>
/// Operator semantics for values the evaluator computes client-side (unwrapped primitives, strings,
/// and null).
/// Delegates to the C# runtime binder via <c>dynamic</c>, so numeric promotion, string
/// concatenation, and operand-type errors are exactly the language's own, not a reimplementation.
/// The lazy operators (<c>&amp;&amp;</c>, <c>||</c>, <c>??</c>) live in the interpreter: laziness
/// is an evaluation-order concern, not a value concern.
/// </summary>
public static class PrimitiveOps {
  public static object Binary(string op, object left, object right) {
    // C# admits mixing an ulong with a non-negative signed integer constant by converting the
    // constant; the runtime binder sees no constants, so the promotion happens here (a negative
    // operand keeps C#'s no-common-type error via the binder).
    // Shifts are exempt: their right operand must stay an int.
    if (op is not ("<<" or ">>")) {
      (left, right) = PrimitiveOps.ReconcileUlong(left, right);
    }

    try {
      return op switch {
        "+" => (dynamic) left + (dynamic) right,
        "-" => (dynamic) left - (dynamic) right,
        "*" => (dynamic) left * (dynamic) right,
        "/" => (dynamic) left / (dynamic) right,
        "%" => (dynamic) left % (dynamic) right,
        "<" => (dynamic) left < (dynamic) right,
        "<=" => (dynamic) left <= (dynamic) right,
        ">" => (dynamic) left > (dynamic) right,
        ">=" => (dynamic) left >= (dynamic) right,
        "==" => (dynamic) left == (dynamic) right,
        "!=" => (dynamic) left != (dynamic) right,
        "&" => (dynamic) left & (dynamic) right,
        "|" => (dynamic) left | (dynamic) right,
        "^" => (dynamic) left ^ (dynamic) right,
        "<<" => (dynamic) left << (dynamic) right,
        ">>" => (dynamic) left >> (dynamic) right,
        _ => throw new InvalidOperationException($"unsupported operator '{op}'")
      };
    }
    catch (RuntimeBinderException e) {
      throw new InvalidOperationException($"operator '{op}' cannot be applied here: {e.Message}");
    }
  }

  public static object Unary(string op, object operand) {
    try {
      return op switch {
        "-" => -(dynamic) operand,
        "+" => +(dynamic) operand,
        "!" => !(dynamic) operand,
        "~" => ~(dynamic) operand,
        _ => throw new InvalidOperationException($"unsupported operator '{op}'")
      };
    }
    catch (RuntimeBinderException e) {
      throw new InvalidOperationException($"operator '{op}' cannot be applied here: {e.Message}");
    }
  }

  private static (object Left, object Right) ReconcileUlong(object left, object right) {
    if (left is ulong && PrimitiveOps.NonNegativeAsUlong(right) is {} promotedRight) {
      return (left, promotedRight);
    }

    if (right is ulong && PrimitiveOps.NonNegativeAsUlong(left) is {} promotedLeft) {
      return (promotedLeft, right);
    }

    return (left, right);
  }

  private static ulong? NonNegativeAsUlong(object value) {
    var signed = value switch {
      sbyte v => (long?) v,
      short v => v,
      int v => v,
      long v => v,
      _ => null
    };

    return signed is >= 0 ? (ulong) signed.Value : null;
  }

  /// <summary>
  /// The C# implicit numeric conversions (widening only: narrowing needs a cast, and identity is
  /// the caller's cheaper first check).
  /// </summary>
  public static bool CanWiden(Type from, Type to) =>
    PrimitiveOps.Widenings.TryGetValue(from, out var targets) && targets.Contains(to);

  private static readonly Dictionary<Type, Type[]> Widenings = new() {
    [typeof(sbyte)] = [typeof(short), typeof(int), typeof(long), typeof(float), typeof(double)],
    [typeof(byte)] = [
      typeof(short),
      typeof(ushort),
      typeof(int),
      typeof(uint),
      typeof(long),
      typeof(ulong),
      typeof(float),
      typeof(double)
    ],
    [typeof(short)] = [typeof(int), typeof(long), typeof(float), typeof(double)],
    [typeof(ushort)] = [
      typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double)
    ],
    [typeof(int)] = [typeof(long), typeof(float), typeof(double)],
    [typeof(uint)] = [typeof(long), typeof(ulong), typeof(float), typeof(double)],
    [typeof(long)] = [typeof(float), typeof(double)],
    [typeof(ulong)] = [typeof(float), typeof(double)],
    [typeof(char)] = [
      typeof(ushort),
      typeof(int),
      typeof(uint),
      typeof(long),
      typeof(ulong),
      typeof(float),
      typeof(double)
    ],
    [typeof(float)] = [typeof(double)]
  };
}
