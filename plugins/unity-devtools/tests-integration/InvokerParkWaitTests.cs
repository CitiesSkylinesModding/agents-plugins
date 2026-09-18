using System;
using System.Diagnostics;
using System.Threading;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// What an invoke does while the main thread is out of reach.
/// A suspend window can open over a thread the agent only TREATS as suspended, because it was
/// inside native code when the suspend landed; invokes stay illegal there until it re-enters
/// managed code, which is a wait rather than a fault.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class InvokerParkWaitTests(MonoDebuggeeFixture fx) {
  /// <summary>
  /// Long enough that the wait is unmistakably longer than a transient, and than any budget sized
  /// for one; short enough to keep the suite's runtime honest.
  /// </summary>
  private static readonly TimeSpan Stall = TimeSpan.FromSeconds(3);

  /// <summary>
  /// How long the debuggee's loop is given to pick the stall up (it ticks every 10 ms).
  /// </summary>
  private static readonly TimeSpan EnterWait = TimeSpan.FromSeconds(5);

  [SkippableFact]
  public void AnInvokeWaitsOutAMainThreadStuckInANativeWait() {
    // Both of these are plain field accesses over the wire, so they keep working on the very
    // thread they are about -- which is the whole reason the stall can be observed from here.
    _ = fx.Eval($"TestFixture.Ticker.StallMs = {InvokerParkWaitTests.Stall.TotalMilliseconds}");

    var entering = Stopwatch.StartNew();

    // "True", not "true": a bool read back from the debuggee is rendered by the formatter every
    // tool answers through, where a client-side comparison result is not.
    while (fx.Eval("TestFixture.Ticker.Stalling").Formatted is not "True") {
      Assert.True(
        entering.Elapsed < InvokerParkWaitTests.EnterWait,
        "the debuggee never entered the stall; is the fixture exe current?"
      );

      // Coarser than the debuggee's own 10 ms tick: each probe parses an expression and opens a
      // suspend window, which freezes the loop this is waiting for.
      Thread.Sleep(50);
    }

    var invoking = Stopwatch.StartNew();

    // An invoke, unlike everything above: it needs the main thread parked at a managed safe point,
    // and the thread will not reach one until its native sleep ends.
    var outcome = fx.Eval("TestFixture.Shapes.Greeting.ToUpperInvariant()");

    invoking.Stop();

    Assert.Equal("\"HELLO\"", outcome.Formatted);

    // Half the stall: proof the call really met it, so the assertion above cannot pass on an
    // invoke that slipped in before the debuggee ever stalled.
    Assert.True(
      invoking.Elapsed > InvokerParkWaitTests.Stall / 2,
      $"the invoke returned in {invoking.Elapsed.TotalSeconds:0.#}s, so it never met the stall"
    );
  }
}
