// ReSharper disable UnusedMember.Global UnusedType.Global UnusedParameter.Global NotAccessedField.Global

using System;
using System.Collections.Generic;
using System.Threading;
using TestFixture.Miss;

namespace TestFixture;

// Shared static roots stay read-only by suite discipline (one debuggee serves every test); tests
// mutate only instances they create inside their own evaluated expressions.
public static class Shapes {
  // ReSharper disable once ConvertToConstant.Global - used as a field in tests
  public static readonly string Greeting = "hello";

  public static readonly string? NullText = null;

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

  // Two generic definitions sharing a name, a parameter count, and a generic arity, the one an
  // int argument binds declared second: a lookup stopping at the first never reaches it.
  public static string Tag<T>(Holder owner, T value) => "holder";

  public static string Tag<T>(int index, T value) => $"int:{value}";

  // The same pair with constraints no single type argument satisfies on both, so one of the two
  // cannot be instantiated whichever the caller means.
  public static string Only<T>(Holder owner, T value) where T : struct => "struct";

  public static string Only<T>(int index, T value) where T : class => "class";
}

public enum Small {
  One = 1,

  Two = 2,

  Three = 3
}

// The shapes generic calls bind against, with and without written type arguments.
public static class Infer {
  // Mutable: a failed eval keeps no locals, so only a static can witness that a call's arguments
  // never ran.
  public static int Calls;

  public static int Count() => ++Infer.Calls;

  public static string Pick(int value) => "int";

  public static string Pick(object value) => "object";

  public static string Pick<T>(T value) => "generic";

  public static string Name<T>(T value) => typeof(T).Name;

  public static string Same<T>(T a, T b) => "same";

  public static T? From<T>(int seed) => default;

  public static object Boxed() => 5;

  public static object BoxedFraction() => 2.5;

  public static string Compare(IComparable value) => "comparable";

  public static string Value(ValueType value) => "value";

  public static string Width(short value) => "short";

  public static string Width(long value) => "long";

  public static string Conv<T>(T value) => "value";

  public static string Conv<T>(int seed) => "seed";

  public static object BoxedPaw() => new Paw();

  public static T? Make<T>() => default;

  public static string Valued<T>(T value) where T : struct => typeof(T).Name;

  public static string Both<T>(T value, out T copy) {
    copy = value;

    return typeof(T).Name;
  }

  public static bool TryMake<T>(out T? made) {
    made = default;

    return false;
  }
}

// A base class's methods and the derived class's same-named ones: C# binds a derived method
// whenever one applies, generic or not.
public class Hiding {
  public string M<T>(T value) => "base";

  public string P(int value) => "base";

  public static string S<T>(T value) => "base";
}

public sealed class Hider : Hiding {
  public string M(object value) => "derived";

  public string P(long value) => "derived";

  public static string S(object value) => "derived";

  public static Hider Make() => new();
}

// An override and a narrowing overload across one inheritance edge: C# ranks an override with the
// method it overrides, and admits a narrowing only for a constant.
public class Overridden {
  public virtual string V(long value) => "base-long";

  public string V(int value) => "base-int";

  public string N(int value) => "base-int";

  public string K(int value) => "base-int";

  public string Q(int value) => "base-int";
}

public sealed class Overriding : Overridden {
  public override string V(long value) => "derived-long";

  public string N(byte value) => "derived-byte";

  public string K(Small value) => "derived-enum";

  public string Q(Small? value) => "derived-enum?";

  public static Overriding Make() => new();
}

// A slot reintroduced with `new virtual`, then overridden: the override belongs to the type that
// reintroduced it.
public class Introducing {
  public virtual string M(int value) => "introducing";
}

public class Reintroducing : Introducing {
  public new virtual string M(int value) => "reintroducing";
}

public sealed class Reoverriding : Reintroducing {
  public override string M(int value) => "reoverriding";

  public static Reoverriding Make() => new();
}

// Overrides of generic methods, their type parameter renamed, bare and nested in a parameter
// type, beside base overloads they must not hide.
public class GenericBase {
  public virtual string G<T>(T value) => "base-generic";

  public string G(int value) => "base-int";

  public virtual string Each<T>(T value, T[] values) => "base-generic";

  public string Each(int value, int[] values) => "base-int";

  public virtual string All<T>(T value, List<T> values) => "base-generic";

  public string All(int value, List<int> values) => "base-int";

  public static int[] Ints() => [1];

  public static List<int> IntList() => [1];
}

public sealed class GenericLeaf : GenericBase {
  public override string G<U>(U value) => "leaf-generic";

  public override string Each<U>(U value, U[] values) => "leaf-generic";

  public override string All<U>(U value, List<U> values) => "leaf-generic";

  public static GenericLeaf Make() => new();
}

// Overloads of one type that the in-range narrowing, the numeric-to-enum convenience, and a
// box's conversions compete in, and enum slots the convenience fills.
public static class Narrowed {
  public static string M(byte value) => "byte";

  public static string M(object value) => "object";

  public static string E(byte value) => "byte";

  public static string E(Small value) => "enum";

  public static string Pick(Small value) => "enum";

  public static string Pick(object value) => "object";

  public static string Kind(Small value) => $"enum:{(int) value}";

  public static string Fits(Narrow value) => $"narrow:{(byte) value}";

  public static string Code(int value) => "int";

  public static string Code(Small value) => "enum";

  public static string En(Small? value) => "Small?";

  public static string En(object value) => "object";

  public static string Two(Small value, int other) => "enum";

  public static string Two(object value, object other) => "object";
}

// Generic methods beside non-generic siblings: object siblings (Same, Tri, Cov) that bind where
// the generic one is refused without a conflict, a conflict failing loudly instead; siblings
// (Pair, Exact, Sub, Named) C# prefers over the generic one wherever both apply; siblings
// (ValueNull, Multi) that bind where a null leaves the generic one inapplicable; and ones
// (Loose, Two, Cmp) C# does not prefer, a null slot less specific than a generic one's or a
// boxed argument hiding the type C# would fix.
public static class Sibling {
  public static string Same<T>(T a, T b) => "generic";

  public static string Same(object a, object b) => "object";

  public static string Pair<T>(T a, T b) => "generic";

  public static string Pair(string a, string b) => "nongeneric";

  public static string Exact<T>(T a, T b) => "generic";

  public static string Exact(int a, long b) => "exact";

  public static string Tri<T>(T a, T b, T c) => "generic";

  public static string Tri(object a, object b, object c) => "object";

  public static string Cov<T>(T a, T b) => "generic";

  public static string Cov(object a, object b) => "object";

  public static string Sub<T>(T a, T b) => "generic";

  public static string Sub(RefBase a, RefMid b) => "sibling";

  public static string Loose<T>(T a, T b) => "generic";

  public static string Loose(string a, object b) => "sibling";

  public static string ValueNull<T>(T a, T b) => "generic";

  public static string ValueNull(int a, object b) => "sibling";

  public static string Named<T>(T a, T b, string c) => "generic";

  public static string Named(string a, string b, string c) => "sibling";

  public static string Two<T>(T a, T b, RefBase c) => "first";

  public static string Two<T>(RefBase a, T b, T c) => "second";

  public static string Two(RefBase a, RefBase b, RefMid c) => "sibling";

  public static string Multi<T, TOther>(T a, T b, TOther c) => "generic";

  public static string Multi(string a, string b, object c) => "sibling";

  public static string Cmp<T>(T a, T b) => "generic";

  public static string Cmp(IComparable a, object b) => "sibling";

  public static object BoxedInt() => 5;

  public static string[] Strings() => ["a"];

  public static object[] Objects() => ["a"];
}

// A generic method and a non-generic sibling across an inheritance edge, either way round: the
// derived one, applicable, hides the base one.
public class TierBase {
  public string M(int a, long b) => "base";
}

public sealed class TierDerived : TierBase {
  public string M<T>(T a, T b) => "derived";
}

public class WideBase {
  public string M<T>(T a, T b) => "base-generic";
}

public sealed class WideDerived : WideBase {
  public string M(long a, long b) => "derived-wide";
}

// Indexers across an inheritance edge: the derived one is applicable to an int, so it hides the
// base one.
public class IndexedBase {
  // Mutable: records which setter ran.
  public string Written = "";

  public string this[int index] { get => "base-int"; set => this.Written = "base-int"; }
}

public sealed class IndexedDerived : IndexedBase {
  public string this[long index] { get => "derived-long"; set => this.Written = "derived-long"; }

  public static IndexedDerived Make() => new();
}

// Nullable values a getter returns, empty and set.
public static class NullableReads {
  public static int? None => null;

  public static int? Five => 5;

  public static Small? SomeSmall => Small.Two;
}

// A struct holding a nullable struct field, for cast and struct-copy tests.
public struct PointHolder {
  public Point? Maybe;
}

// A frame whose parameters are a struct, a struct holding a nullable, a byte-backed enum, an int,
// a boxed struct its alias also holds, and a struct boxed as an interface, and whose locals are a
// long, an object, an int?, an interface, a string, and a pointer, for breakpoint tests writing
// through them; `After` reports whether the box kept its identity, and `ByRefs` takes an `in` and
// a `ref` struct.
public static class Frames {
  // Mutable: the struct whose method a breakpoint test writes `this` through.
  public static Mutable Held;

  // Mutable: the generic struct whose method a breakpoint test writes `this` through.
  public static Generic<int> GenericHeld;

  // Mutable: struct statics, written through `ByRefs`' by-reference parameters and through
  // static-field links.
  public static Point InTarget;

  public static Point RefTarget;

  // Mutable: the box `At` receives, for its identity check.
  public static object? ShapeAlias;

  public static void Visit() {
    object shape = new Point {
      X = 1
    };

    Frames.ShapeAlias = shape;
    Frames.At(
      new Point {
        X = 1
      },
      new Holds(),
      Narrow.Full,
      4,
      shape,
      new BumpZ()
    );
    Frames.Held.Touch();
    Frames.GenericHeld.Touch();
    Frames.ByRefs(in Frames.InTarget, ref Frames.RefTarget);
  }

  private static unsafe void At(
    Point pt,
    Holds holds,
    Narrow narrow,
    int n,
    object shape,
    IBump bump
  ) {
    long wide = 0;
    object? boxed = null;
    int? maybe = null;
    IComparable? cmp = null;
    string? text = null;
    float ratio = 0;
    int* p = null;

    GC.KeepAlive(pt);
    GC.KeepAlive(holds);
    GC.KeepAlive(narrow);
    GC.KeepAlive(wide);
    GC.KeepAlive(boxed);
    GC.KeepAlive(maybe);
    GC.KeepAlive(cmp);
    GC.KeepAlive(text);
    GC.KeepAlive(ratio);
    GC.KeepAlive((long) p);
    Frames.After(ReferenceEquals(shape, Frames.ShapeAlias));
  }

  private static void After(bool same) => GC.KeepAlive(same);

  private static void ByRefs(in Point q, ref Point r) {
    GC.KeepAlive(q);
    GC.KeepAlive(r);
  }
}

// A generic struct, whose `this` the agent does not flag a value type: a breakpoint test reads and
// writes through it.
public struct Generic<T> {
  // Mutable: written through `this` from a breakpoint.
  public int X;

  public void Touch() => GC.KeepAlive(this.X);
}

public struct Mutable {
  // Mutable: written through `this` from a breakpoint.
  public int X;

  public void Touch() => GC.KeepAlive(this.X);

  public void Bump() => this.X++;
}

// A three-level hierarchy, its overloads declared base first: C# binds the closest base.
public class RefBase;

public class RefMid : RefBase;

public sealed class RefLeaf : RefMid;

public static class Refs {
  public static string M(RefBase value) => "base";

  public static string M(RefMid value) => "mid";

  public static string Ifc(IEnumerable<int> value) => "ienum";

  public static string Ifc(IList<int> value) => "ilist";

  public static List<int> Ints() => [1];

  public static string Wide(Deep4 a, Deep4 b, long c) => "class-long";

  public static string Wide(IDeep a, IDeep b, int c) => "iface-int";
}

// A struct and an interface it implements, the interface carrying three bases so the
// most-interfaces tie-break favours it: a null still binds the struct's nullable, as C# prefers a
// nullable over any reference type its struct converts to.
public interface IOne;

public interface ITwo;

public interface IThree : IOne, ITwo, IDisposable;

public struct Triple : IThree {
  public void Dispose() {
  }
}

// A deep chain whose leaf alone implements an interface, so the class and interface slots are
// unrelated and only the third argument decides between them.
public interface IDeep;

public class Deep1;

public class Deep2 : Deep1;

public class Deep3 : Deep2;

public class Deep4 : Deep3;

public sealed class DeepLeaf : Deep4, IDeep;

// The overloads a null argument picks among.
public static class Nulls {
  public static string Pair(KeyValuePair<int, int> pair) => "pair";

  public static string Pair(object value) => "object";

  public static string Text(object value) => "object";

  public static string Text(string value) => "string";

  public static string Maybe(int? value) => value?.ToString() ?? "null";

  public static int? Three() => 3;

  public static string Order(int? value) => "int?";

  public static string Order(int value) => "int";

  public static string Wide(double? value) => "double?";

  public static string Wide(long? value) => "long?";

  public static string Narrowest(long? value) => "long?";

  public static string Narrowest(int? value) => "int?";

  public static string Boxy(ValueType value) => "ValueType";

  public static string Boxy(int? value) => "int?";

  public static string Cmp(IComparable value) => "IComparable";

  public static string Cmp(bool? value) => "bool?";

  public static string Three(IThree value) => "IThree";

  public static string Three(Triple? value) => "Triple?";

  public static string Most(Animal animal) => "base";

  public static string Most(Dog dog) => "derived";

  public static void OutMaybe(out int? value) => value = 7;

  public static void OutPair(out KeyValuePair<int, string> pair) =>
    pair = new KeyValuePair<int, string>(1, "one");

  public static int Take(Holds holds) => holds.X;

  public static void OutKind(out Outer<int>.Kind kind) => kind = Outer<int>.Kind.B;

  public static void OutPointer(out IntPtr pointer) => pointer = new IntPtr(42);

  public static int TakePointer(HoldsPointer holds) => holds.X;

  public static long ReadPointer(IntPtr pointer) => pointer.ToInt64();

  public static void OutHolds(out Holds holds) =>
    holds = new Holds {
      Maybe = 3,
      X = 4
    };
}

// An enum the agent reports as a generic instance, being nested in a generic type.
public sealed class Outer<T> {
  public enum Kind {
    A,

    B
  }
}

// Raw pointer parameters, which the debugger agent delivers to the callee at another address, and
// the pointer slots it writes faithfully.
public static unsafe class Pointers {
  // Mutable: the pointer slots the eval writes into.
  public static int* Slot;

  public static readonly int*[] Slots = new int*[1];

  public static string IsNull(int* pointer) => pointer == null ? "null" : "nonnull";

  public static long Address(void* pointer) => (long) pointer;

  public static int* Make(long address) => (int*) address;

  public static long ReadSlot() => (long) Pointers.Slot;

  public static long ReadSlots() => (long) Pointers.Slots[0];

  public static void Reset() {
    Pointers.Slot = (int*) 99;
    Pointers.Slots[0] = (int*) 99;
  }

  // Called every tick, so a breakpoint on At pauses in a frame holding a pointer parameter.
  public static void Visit(long address) => Pointers.At((int*) address);

  private static void At(int* pointer) {
  }
}

// A plain struct whose IntPtr field is a pointer underneath.
public struct HoldsPointer {
  // Mutable: set through an object initializer.
  public IntPtr Pointer;

  public int X;
}

// A plain struct whose fields are a nullable and a generic struct, the shapes the agent decodes
// only as fields.
public struct Holds {
  // Mutable: set through an object initializer.
  public int? Maybe;

  public KeyValuePair<int, int> Pair;

  public int X;
}

// Generic-struct slots a test writes null into, on an instance it creates.
public sealed class PairSlots {
  // Mutable: the write under test is the point.
  public KeyValuePair<int, int> Pair;

  public readonly KeyValuePair<int, int>[] Pairs = new KeyValuePair<int, int>[1];
}

// A struct built client-side whose object field holds a value the client boxed.
public struct Boxed {
  // Mutable: set through an object initializer.
  public object? Value;
}

public struct Point {
  public int X;

  public int Y;
}

public sealed class Holder {
  public Point P;
}

// Structs boxed in holders typed object or as an interface, the interface-typed and array ones
// with an alias, for the box identity and write-back tests.
public interface IBump {
  int P { get; set; }

  void Bump();
}

public struct BumpZ : IBump {
  // Mutable: bumped through the box an interface-typed holder keeps.
  public int X;

  // Mutable: negated through the box, its sign the only change.
  public float F;

  public int P { get => this.X; set => this.X = value; }

  public void Bump() => this.X++;

  public void Negate() => this.F = -this.F;
}

// A struct whose ToString changes it, so a call through a holder typed object changes its box.
public struct Sneaky {
  // Mutable: counts ToString calls.
  public int X;

  public override string ToString() => $"{++this.X}";
}

public struct ObjectHolder {
  // Mutable: holds a boxed Point.
  public object? O;
}

public static class Boxes {
  // Mutable: rebuilt by Reset, so each test starts from fresh boxes.
  public static IBump? Ifc;

  public static object? IfcAlias;

  public static object? Obj;

  public static object?[] Arr = [];

  public static object? ArrAlias;

  public static ObjectHolder Held;

  public static void Reset() {
    Boxes.Ifc = new BumpZ();
    Boxes.IfcAlias = Boxes.Ifc;
    Boxes.Obj = new Sneaky();
    Boxes.ArrAlias = new Point();
    Boxes.Arr = [Boxes.ArrAlias];
    Boxes.Held = new ObjectHolder {
      O = new Point()
    };
  }

  public static bool SameIfc() => ReferenceEquals(Boxes.Ifc, Boxes.IfcAlias);

  public static bool SameArr() => ReferenceEquals(Boxes.Arr[0], Boxes.ArrAlias);
}

// A struct holding two structs, which a call encodes apart however they were assigned.
public struct TwoPoints {
  public Point A;

  public Point B;

  public static int Sum(TwoPoints h) => h.A.X + h.B.X;
}

// A declared parameterless struct constructor (C# 10): `new Counted()` must run it, not zero the
// value client-side.
public struct Counted() {
  public int N = 7;
}

public enum Big : ulong {
  Huge = ulong.MaxValue
}

// The narrow end of the underlying-type range: a member whose value fills its byte, so a constant
// read at the wrong width shows up as a wrong value rather than as a wrong type.
public enum Narrow : byte {
  Full = byte.MaxValue
}

public class BaseThing {
  public string Name => "base";
}

public sealed class DerivedThing : BaseThing {
  public new string Name => "derived";
}

public static class Thrower {
  public static void Boom() => throw new InvalidOperationException("kaboom");

  public static void BoomUnreadable() => throw new SpitefulException();

  // Same throw behind a bool, for the places that need one (a breakpoint condition).
  public static bool BoomBool() => throw new InvalidOperationException("kaboom");
}

// A thrown object whose Message cannot be read: reporting it must fall back to the type alone.
public sealed class SpitefulException : Exception {
  public override string Message => throw new InvalidOperationException("no message for you");
}

// The debug toolset's moving target: Main calls Tick every loop iteration, so an armed breakpoint
// hits within milliseconds, with a parameter and a local in frame.
public static class Ticker {
  public static int LastTick;

  public static string? LastLabel;

  public static void Tick(int n) {
    var label = "tick:" + n;

    Ticker.LastTick = n;
    Ticker.LastLabel = label;

    TickBox.Instance.Bump(n);
  }

  // A main thread no invoke can reach, on demand: a debugger writes StallMs (a field write is a
  // plain wire command, so it lands on the very thread it is about), and the loop then spends that
  // long inside ONE native wait, which holds no managed safe point for an invoke to run at.
  // Both fields are volatile because the writer is outside the process entirely.
  public static volatile int StallMs;

  // Whether the loop is inside such a wait right now, so a test times its call against the stall
  // rather than against the race to enter it.
  public static volatile bool Stalling;

  public static void MaybeStall() {
    var ms = Ticker.StallMs;

    if (ms <= 0) {
      return;
    }

    // Cleared first: the stall is one-shot, and the thread cannot clear it while it sleeps.
    Ticker.StallMs = 0;
    Ticker.Stalling = true;

    try {
      Thread.Sleep(ms);
    }
    finally {
      Ticker.Stalling = false;
    }
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

// A type nothing in the fixture touches, so its static constructor runs only once a test runs it.
public static class Unstarted {
  public const int Literal = 3;

  public static readonly int Value = 7;

  static Unstarted() {
  }

  public static class Inner {
    public static readonly int Value = 9;

    static Inner() {
    }
  }
}

// A type whose static constructor runs once a test asks `Starter` to touch it, for reads paused
// inside a running static constructor.
public static class Starting {
  // Mutable: assigned by the static constructor, before the call a test breaks in.
  public static int First;

  static Starting() {
    Starting.First = 5;
    Starting.Ready();
  }

  public static void Ready() {
  }
}

// `Starting` for a static constructor a test runs on a worker thread through `Starter`.
public static class WorkerStarting {
  // Mutable: assigned by the static constructor, before the call a test breaks in.
  public static int First;

  static WorkerStarting() {
    WorkerStarting.First = 5;
    WorkerStarting.Ready();
  }

  public static void Ready() {
  }
}

public static class Starter {
  // Mutable: set by a test to run `Starting`'s static constructor on the main loop.
  public static bool Requested;

  public static void StartOnWorker() {
    new Thread(() => GC.KeepAlive(WorkerStarting.First)).Start();
  }

  public static void Visit() {
    if (Starter.Requested) {
      GC.KeepAlive(Starting.First);
    }
  }
}
