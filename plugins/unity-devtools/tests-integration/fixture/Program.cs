using System;
using System.Threading;

namespace TestFixture;

public static class Program {
  public static void Main() {
    // Mirror reads of static fields do not trigger class constructors, so touch every type holding
    // shared static roots; READY tells the test harness the roots are initialized.
    GC.KeepAlive(Shapes.Greeting);

    Console.WriteLine("READY");

    // Park in a managed loop rather than one infinite sleep: invokes need the main thread at a
    // managed safe point, and a thread blocked forever inside a native wait never reaches one.
    // Each iteration ticks (so armed breakpoints hit within milliseconds) and periodically
    // throws-and-catches (so exception breaks have something to catch).
    var n = 0;

    while (true) {
      Ticker.Tick(n);
      Ticker.MaybeThrow(n);

      n++;

      Thread.Sleep(10);
    }

    // ReSharper disable once FunctionNeverReturns
  }
}
