// ReSharper disable UnusedMember.Global

using System;
using System.Collections.Generic;
using System.Globalization;

namespace TestFixture;

// Calls taking `ref`: each reports what it received, so a test sees the value passed in as well as
// the one written back.
public static class ByRef {
  public static int Add(ref int value, int by) {
    var received = value;

    value += by;

    return received;
  }

  public static void Shift(ref Point point, int by) {
    point.X += by;
    point.Y += by;
  }

  public static int Plain(int value) => value;

  public static void Take(out int value) => value = 1;

  public static void Swap<T>(ref T a, ref T b) => (a, b) = (b, a);

  public static KeyValuePair<int, int> MakePair() => new(1, 2);

  public static void FlipPair(ref KeyValuePair<int, int> pair) =>
    pair = new KeyValuePair<int, int>(pair.Value, pair.Key);

  public static int? MakeMaybe() => 3;

  public static void BumpMaybe(ref int? value) => value += 1;

  public static int Read(in int value) => value;

  public static int ReadPair(in KeyValuePair<int, int> pair) => pair.Key;

  public static string Over(in KeyValuePair<int, int> pair) => "in";

  public static string Over(object value) => "object";

  public static string ReadBoxed(in object value) => $"boxed:{value}";
}

// A constructor and an indexer taking a generic struct by `in`, which bind outside method calls.
public sealed class PairReader {
  public PairReader() {
  }

  public PairReader(in KeyValuePair<int, int> pair) {
    this.Key = pair.Key;
  }

  public int Key { get; }

  public int this[in KeyValuePair<int, int> pair] => pair.Value;
}

// Values formatting themselves in a shape no client-side formatting produces, so a test sees the
// debuggee ran the format: a struct implementing the interface publicly, and a class explicitly.
public struct Gauge : IFormattable {
  public double Level;

  public readonly string ToString(string? format, IFormatProvider? provider) =>
    $"gauge:{this.Level.ToString(format, CultureInfo.InvariantCulture)}";
}

public sealed class Dial : IFormattable {
  string IFormattable.ToString(string? format, IFormatProvider? provider) => $"dial:{format}";
}
