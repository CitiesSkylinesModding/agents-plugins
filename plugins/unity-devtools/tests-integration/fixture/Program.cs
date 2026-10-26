using System;
using System.Threading;

namespace TestFixture;

public static class Program {
  /// <summary>The main thread's name, set before the debugger can see it.</summary>
  public const string MainThreadName = "FixtureMain";

  public static void Main() {
    // Name the main thread and start an unnamed worker before READY: some games name their main
    // thread at startup, and a pick going by the name takes the unnamed worker instead, so the
    // suite's every invoke runs against that shape.
    Thread.CurrentThread.Name = Program.MainThreadName;

    new Thread(Program.Idle) {
      IsBackground = true
    }.Start();

    // Mirror reads of static fields do not trigger class constructors, so touch every type holding
    // shared static roots; READY tells the test harness the roots are initialized.
    GC.KeepAlive(Shapes.Greeting);

    Console.WriteLine("READY");

    // Park in a managed loop rather than one infinite sleep: invokes need the main thread at a
    // managed safe point, and a thread blocked forever inside a native wait never reaches one.
    // Each iteration ticks (so armed breakpoints hit within milliseconds), periodically
    // throws-and-catches (so exception breaks have something to catch), honours a stall a test
    // asked for (so the main thread can be put out of reach of invokes on demand), visits the
    // Pointers and Frames shapes (so breakpoint tests have frames to read and write through), and
    // runs a static constructor once a test asks for it.
    var n = 0;

    while (true) {
      Ticker.Tick(n);
      Ticker.MaybeThrow(n);
      Ticker.MaybeStall();
      Pointers.Visit(99);
      Frames.Visit();
      Starter.Visit();

      n++;

      Thread.Sleep(10);
    }

    // ReSharper disable once FunctionNeverReturns
  }

  // Blocked in one native wait for good, like a worker idling on a queue: a thread that re-enters
  // managed code now and then parks for an invoke eventually, and would hide a wrong pick.
  private static void Idle() => new ManualResetEvent(false).WaitOne();
}
