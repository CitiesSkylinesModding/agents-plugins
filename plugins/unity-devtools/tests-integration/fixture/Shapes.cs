// ReSharper disable UnusedMember.Global UnusedType.Global UnusedParameter.Global NotAccessedField.Global

using System;

namespace TestFixture;

// Shared static roots stay read-only by suite discipline (one debuggee serves every test); tests
// mutate only instances they create inside their own evaluated expressions.
public static class Shapes {
  // ReSharper disable once ConvertToConstant.Global - used as a field in tests
  public static readonly string Greeting = "hello";

  public static readonly string NullText = null;

  public static readonly int Answer = 42;

  public static readonly int[,] Grid = { { 1, 2 }, { 3, 4 } };
}

public static class Overloads {
  public static string Pick(int value) => "int";

  public static string Pick(long value) => "long";

  public static string Pick(double value) => "double";

  public static string Pick(object value) => "object";

  public static long TakesLong(long value) => value;

  public static byte TakesByte(byte value) => value;

  public static string TakesSmall(Small value) => value.ToString();
}

public enum Small {
  One = 1,

  Two = 2,

  Three = 3
}

public struct Point {
  public int X;

  public int Y;
}

public sealed class Holder {
  public Point P;
}

// A declared parameterless struct constructor (C# 10): `new Counted()` must run it, not zero the
// value client-side.
public struct Counted() {
  public int N = 7;
}

public enum Big : ulong {
  Huge = ulong.MaxValue
}

public class BaseThing {
  public string Name => "base";
}

public sealed class DerivedThing : BaseThing {
  public new string Name => "derived";
}

public static class Thrower {
  public static void Boom() => throw new InvalidOperationException("kaboom");
}

// The debug toolset's moving target: Main calls Tick every loop iteration, so an armed breakpoint
// hits within milliseconds, with a parameter and a local in frame.
public static class Ticker {
  public static int LastTick;

  public static string LastLabel;

  public static void Tick(int n) {
    var label = "tick:" + n;

    Ticker.LastTick = n;
    Ticker.LastLabel = label;

    TickBox.Instance.Bump(n);
  }

  // Thrown AND caught, so exception-break tests always have a throw to catch within a second while
  // the program itself never dies. FormatException is otherwise unused in the fixture.
  public static void MaybeThrow(int n) {
    if (n % 100 is not 99) {
      return;
    }

    try {
      throw new FormatException("tick " + n);
    }
    catch (FormatException) {
      // Swallowed on purpose; the debugger breaks on throw, not on catch.
    }
  }
}

// An instance method in the tick path, so `this`-rooted frame evaluation has a live receiver.
public sealed class TickBox {
  public static readonly TickBox Instance = new();

  public int Value;

  public void Bump(int n) => this.Value = n;
}

public struct Accum {
  public int Total;

  public bool AddChecked(int amount, out int before) {
    before = this.Total;
    this.Total += amount;

    return true;
  }
}
