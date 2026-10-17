using System;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb.Eval;

/// <summary>
/// How a candidate meets a call's argument count: exactly, by omitting trailing optional
/// parameters as a C# call may, or not at all, with why.
/// The one arity rule eval's method, generic-definition and constructor binding share, so they
/// cannot drift apart.
/// The plugin's own lookups keep exact arity: the <see cref="Invoker.FindMethod" /> probes,
/// operators, and struct `Equals`, the last two through <see cref="Exact" />.
/// A lookup whose shape the plugin knows would read an arity-short match as a successful binding.
/// </summary>
/// <param name="Omitted">
/// What the omitted trailing parameters declare, in position order; empty for an exact fit.
/// </param>
/// <param name="Reason">Why the candidate does not fit; null when it does.</param>
public sealed record ArityFit(MethodMirror Method, DeclaredDefault[] Omitted, string Reason) {
  public bool IsFit => this.Reason is null;

  /// <summary>The candidate as a miss lists it; null when it fits.</summary>
  public Rejection Rejection => this.IsFit ? null : new Rejection(this.Method, this.Reason);

  /// <summary>
  /// Fits a candidate to an argument count,
  /// reading its defaults only when it has more parameters than arguments.
  /// </summary>
  public static ArityFit Of(Invoker inv, MethodMirror candidate, int argc) {
    var parameters = candidate.GetParameters();

    if (parameters.Length <= argc) {
      return ArityFit.Exact(candidate, argc);
    }

    if (inv.Defaults.Of(candidate) is not {} declared) {
      var assembly = Invoker.SimpleAssemblyName(candidate.DeclaringType.Assembly);

      return ArityFit.Unfit(
        candidate,
        $"takes {parameters.Length} argument(s), and the defaults of {assembly} could not be " +
        "read: pass every argument"
      );
    }

    var minimum = Array.FindLastIndex(declared, d => d.Kind is DefaultKind.Required) + 1;

    if (argc < minimum) {
      return ArityFit.Unfit(
        candidate,
        minimum == parameters.Length
          ? $"takes {minimum} argument(s)"
          : $"takes {minimum} to {parameters.Length} argument(s)"
      );
    }

    for (var i = argc; i < parameters.Length; i++) {
      if (ArityFit.Unsupported(parameters[i], declared[i]) is {} unsupported) {
        return ArityFit.Unfit(candidate, unsupported);
      }
    }

    return new ArityFit(candidate, declared[argc..], null);
  }

  /// <summary>
  /// Fits a candidate only at its own parameter count: the rule for a lookup that binds no
  /// defaults but reports its misses alongside one that does.
  /// </summary>
  public static ArityFit Exact(MethodMirror candidate, int argc) {
    var count = candidate.GetParameters().Length;

    return count == argc
      ? new ArityFit(candidate, [], null)
      : ArityFit.Unfit(candidate, $"takes {count} argument(s)");
  }

  private static ArityFit Unfit(MethodMirror candidate, string reason) =>
    new(candidate, null, reason);

  /// <summary>
  /// Why an omitted parameter's default cannot reach the callee, naming it; null when it can.
  /// A constant into a nullable slot cannot: only an empty one is built.
  /// </summary>
  private static string Unsupported(ParameterInfoMirror parameter, DeclaredDefault declared) {
    var slot = Invoker.Bare(parameter.ParameterType);

    // The debugger agent hands the callee another address than the pointer sent, null included.
    if (slot.IsPointer) {
      return $"'{parameter.Name}' is a pointer, which the debugger agent does not deliver";
    }

    var unsupported = declared.Kind switch {
      DefaultKind.DecimalConstant => "a decimal",
      DefaultKind.DateTimeConstant => "a DateTime constant",
      DefaultKind.Constant when declared.Constant is not null && Invoker.IsNullable(slot) =>
        $"{SymbolDisplay.FormatPrimitive(declared.Constant, true, false)} in a nullable",
      _ => null
    };

    return unsupported is null
      ? null
      : $"'{parameter.Name}' defaults to {unsupported}, which eval cannot supply: pass it";
  }
}
