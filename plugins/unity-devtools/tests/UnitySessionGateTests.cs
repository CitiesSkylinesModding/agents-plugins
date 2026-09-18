using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace UnityDevtools.Sdb.Tests;

/// <summary>
/// What happens to a caller while another operation holds the debugger.
/// Every tool call serializes on one gate, so an operation whose wire stops answering used to take
/// the whole server with it: later calls queued forever, <c>detach</c> and <c>attach</c> included,
/// and only restarting the server cleared it.
/// No debuggee is involved -- these drive the gate alone, which is why they live in the offline
/// suite.
/// </summary>
public sealed class UnitySessionGateTests {
  private static readonly TimeSpan GateWait = TimeSpan.FromMilliseconds(200);

  /// <summary>
  /// Longer than anything under test waits, so the holder is still there at the assert.
  /// </summary>
  private static readonly TimeSpan Hold = TimeSpan.FromSeconds(5);

  [Fact]
  public async Task ACallerRefusedByALongOperationIsToldWhatHoldsTheDebugger() {
    using var session = new UnitySession(null, UnitySessionGateTests.GateWait);
    using var holder = new GateHolder(session);

    var refused = Assert.Throws<InvalidOperationException>(() => session.Exclusive(() => 0));

    await holder.LetGo();

    Assert.Contains("held the debugger", refused.Message, StringComparison.Ordinal);

    // Being told is only half of it: the message has to name the way out, which is the one call
    // that does not queue behind the operation being complained about.
    Assert.Contains("detach", refused.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task DetachDoesNotQueueBehindTheOperationItExistsToBreak() {
    using var session = new UnitySession(null, UnitySessionGateTests.GateWait);
    using var holder = new GateHolder(session);

    var detaching = Stopwatch.StartNew();
    var wasAttached = session.Detach();

    detaching.Stop();

    await holder.LetGo();

    Assert.False(wasAttached);

    // Well inside the holder's five seconds: the point is that it gave up on the gate rather than
    // waiting for it. Tight, because the hatch never waits longer than the gate wait it skips,
    // which this session drives down to milliseconds.
    Assert.True(
      detaching.Elapsed < TimeSpan.FromSeconds(1),
      $"detach waited {detaching.Elapsed.TotalSeconds:0.#}s for a gate it should have given up on"
    );
  }

  [Fact]
  public async Task StatusAnswersWithTheHeldTimeWhileAnOperationRuns() {
    using var session = new UnitySession(null, UnitySessionGateTests.GateWait);
    using var holder = new GateHolder(session);

    // Reporting reads its own lock, so it stays live through an operation that holds the gate --
    // which is what lets a blocked caller find out why.
    var during = session.Snapshot();

    await holder.LetGo();

    Assert.NotNull(during.BusyFor);
    Assert.Null(session.Snapshot().BusyFor);
  }

  /// <summary>
  /// One operation sitting on the session's gate, the way a stalled wire command does, until the
  /// test lets it go.
  /// </summary>
  private sealed class GateHolder : IDisposable {
    private readonly ManualResetEventSlim release = new();

    private readonly Task<int> held;

    public GateHolder(UnitySession session) {
      using var taken = new ManualResetEventSlim();

      // ReSharper disable once AccessToDisposedClosure - awaited before the scope ends.
      this.held = Task.Run(() => session.Exclusive(() => {
            taken.Set();

            return this.release.Wait(UnitySessionGateTests.Hold) ? 1 : 0;
          }
        )
      );

      Assert.True(taken.Wait(UnitySessionGateTests.Hold), "the holder never took the gate");
    }

    public async Task LetGo() {
      this.release.Set();

      _ = await this.held;
    }

    public void Dispose() => this.release.Dispose();
  }
}
