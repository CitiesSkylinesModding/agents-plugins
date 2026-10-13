// ReSharper disable UnusedMember.Global UnusedParameter.Global

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TestFixture;

// Calls omitting trailing optional parameters. Each method reports what it received, so a test
// sees the default the callee got, not merely that the call bound.
public static class Defaulted {
  public static string Flag(bool flag = true) => $"{flag}";

  public static string Whole(int count = -3, long big = 40000000000) =>
    FormattableString.Invariant($"{count}/{big}");

  public static string Real(float single = 1.5f, double wide = -2.25) =>
    FormattableString.Invariant($"{single}/{wide}");

  public static string Letter(char letter = 'q') => $"{letter}";

  public static string Text(string text = "hi") => text;

  public static string Member(Small member = Small.Two, Narrow narrow = Narrow.Full) =>
    $"{member}/{narrow}";

  public static string Nothing(Holder? holder = null) => holder is null ? "null" : "holder";

  public static string Zeroed(Point point = default) => $"{point.X},{point.Y}";

  public static string Empty(int? maybe = null) => maybe?.ToString() ?? "empty";

  // [Optional] with no constant: C# passes default(T).
  public static string Attributed([Optional] int count, [Optional] string? text) =>
    $"{count}/{text ?? "null"}";

  // [Optional] object with no constant: C# passes Type.Missing rather than null.
  public static string Unspecified([Optional] object value) => value?.GetType().Name ?? "null";

  public static string Several(
    int first,
    int second = 2,
    string third = "three",
    bool fourth = true
  ) =>
    $"{first},{second},{third},{fourth}";

  // C# binds the candidate needing no default over the one needing some, declared first here so
  // declaration order alone would pick the wrong one.
  public static string Exact(int value, bool flag = false) => "defaulted";

  public static string Exact(int value) => "exact";

  // C# binds the defaulted candidate, whose int argument converts better than to long.
  public static string Wider(long value) => "long";

  public static string Wider(int value, bool flag = false) => "defaulted";

  // C# would call these ambiguous; the one needing fewer defaults binds, declared second.
  public static string Fewer(int value, int first = 0, int second = 0) => "two";

  public static string Fewer(int value, int first = 0) => "one";

  public static string Typed<T>(T value, bool flag = false) => $"{typeof(T).Name}/{flag}";

  // A struct-constrained type parameter takes its zeroed default, inferred or written.
  public static string Constrained<T>(int value, T seed = default) where T : struct =>
    $"{typeof(T).Name}/{seed}";

  public static string Matched<T>(T value, T other = default) where T : struct =>
    $"{typeof(T).Name}/{other}";

  // T is fixed only by the parameter a call omits, which C# never infers from.
  public static string Seeded<T>(int seed, T value = default!) => typeof(T).Name;

  // No value of these defaults is built client-side, so a call omitting them is refused.
  public static string Money(decimal amount = 1.5m) => $"{amount}";

  public static string When([Optional, DateTimeConstant(630822816000000000)] DateTime when) =>
    $"{when.Ticks}";

  public static string Maybe(int? maybe = 5) => $"{maybe}";

  // A zeroed generic struct, which disconnects the agent in a by-ref slot such as `in`.
  public static string Paired(int value, KeyValuePair<int, int> pair = default) => $"{pair.Key}";

  public static string InPaired(int value, in KeyValuePair<int, int> pair = default) =>
    $"{pair.Key}";

  // The debugger agent hands the callee another address than the pointer sent, null included.
  public static unsafe string Pointed(int value, byte* pointer = null) =>
    pointer == null ? "null" : "set";
}

// A struct whose one constructor takes only optional parameters: `new Paced()` is still the
// zeroed default in C#, and runs no constructor.
public struct Paced {
  public int Step;

  public Paced(int step = 5) {
    this.Step = step;
  }
}

// Members the plugin's own lookups probe at a fixed count, each declaring one optional parameter
// more: a lookup reading an arity-short match as a fit would bind what C# never calls.
public sealed class Probed {
  public static Probed Make() => new();

  public static string get_Label(int width = 0) => "method";

  public static bool op_Equality(Probed left, Probed right, bool loose = true) => true;

  public static bool op_Inequality(Probed left, Probed right, bool loose = true) => false;
}

public struct ProbedValue {
  public int X;

  public bool Equals(ProbedValue other, bool loose = true) => true;
}

// Shaped like an engine store whose generic accessor takes an optional flag.
public sealed class Store {
  public string Read<T>(int entity, bool isReadOnly = false) =>
    $"{typeof(T).Name}:{entity}:{isReadOnly}";
}

// Shaped like an identifier built from a type and a name, with an optional hash.
public sealed class Built {
  public Built(string kind, string name, Point hash = default) {
    this.Seen = $"{kind}/{name}/{hash.X}";
  }

  public Built(Holder owner, Point hash) {
    this.Seen = "owner";
  }

  public string Seen { get; }
}

// A type with an optional-parameter method, built in a Reflection.Emit dynamic assembly: the
// shape whose defaults the debugger agent serves no metadata for. Built on a test's request and
// once, since the eval grammar cannot create the Type[] a parameterised method is defined with.
public static class Emitted {
  public const string TypeName = "TestFixture.Emitted.Defaulted";

  private static Type? built;

  public static Type Build() {
    if (Emitted.built is not null) {
      return Emitted.built;
    }

    var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
      new System.Reflection.AssemblyName("UnityDevtools.TestFixture.Emitted"),
      System.Reflection.Emit.AssemblyBuilderAccess.Run
    );

    var type = assembly.DefineDynamicModule("m")
      .DefineType(
        Emitted.TypeName,
        System.Reflection.TypeAttributes.Public |
        System.Reflection.TypeAttributes.Abstract |
        System.Reflection.TypeAttributes.Sealed
      );

    // static int Add(int a, int b = 7) => a + b;
    var add = type.DefineMethod(
      "Add",
      System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
      typeof(int),
      [typeof(int), typeof(int)]
    );

    add.DefineParameter(1, System.Reflection.ParameterAttributes.None, "a");

    add.DefineParameter(
        2,
        System.Reflection.ParameterAttributes.Optional |
        System.Reflection.ParameterAttributes.HasDefault,
        "b"
      )
      .SetConstant(7);

    var il = add.GetILGenerator();

    il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
    il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
    il.Emit(System.Reflection.Emit.OpCodes.Add);
    il.Emit(System.Reflection.Emit.OpCodes.Ret);

    return Emitted.built = type.CreateType();
  }
}
