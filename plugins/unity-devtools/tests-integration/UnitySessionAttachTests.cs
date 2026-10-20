using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// Attaching to a port the caller names rather than one the beacon advertises, which is the
/// recovery path for a game the beacon does not describe; an advertised endpoint that refuses; and
/// a held slot read without attaching.
/// A debuggee of its own, because the debugger slot is exclusive and the suite's shared debuggee
/// already has a client.
/// </summary>
[Collection(BeaconGroupCollection.Name)]
public sealed class UnitySessionAttachTests : IDisposable {
  private readonly List<Process> debuggees = [];

  private readonly BeaconListener beacons = new();

  [SkippableFact]
  public void AGivenPortAttachesToThatPortOnThisMachine() {
    Skip.If(MonoDebuggee.SkipReason is not null, MonoDebuggee.SkipReason);

    var (_, port) = this.StartDebuggee();

    using var session = new UnitySession(this.beacons);

    var attached = session.Attach(port);

    Assert.True(attached.Attached);
    Assert.Equal("127.0.0.1", attached.Host);
    Assert.Equal(port, attached.Port);
    Assert.False(string.IsNullOrEmpty(attached.VmVersion));

    Assert.True(session.Detach());
  }

  [SkippableFact]
  public void AGivenPortGovernsItsOwnAttachAndNoLaterOne() {
    Skip.If(MonoDebuggee.SkipReason is not null, MonoDebuggee.SkipReason);

    var (_, port) = this.StartDebuggee();

    // A listener whose sightings are stale the instant they arrive, so the reattach below finds
    // nothing however many games this machine advertises. Skipping on a live beacon instead put
    // the case at the mercy of one: a game that started advertising after the check was attached
    // to and read, which is an effect on someone's running game rather than a test.
    using var deaf = new BeaconListener(TimeSpan.Zero);

    // A listener that never came up answers the reattach in its OWN words, which this case is not
    // about; the multicast suite is what reports a host that cannot receive the beacon at all.
    Skip.If(!deaf.Listening, "this machine cannot listen for the PlayerConnection beacon");

    using var session = new UnitySession(deaf);

    Assert.True(session.Attach(port).Attached);
    Assert.True(session.Detach());

    // The debuggee is still listening, so a session that had stored the port would reattach to it
    // and this operation would succeed. Nothing stored it, so the reattach asks the beacon and
    // finds nothing advertising itself.
    var failure = Assert.ThrowsAny<Exception>(() => session.Run(ctx => ctx.Vm.Version.VMVersion));

    Assert.Contains("advertising itself", failure.Message, StringComparison.Ordinal);
  }

  [SkippableFact]
  public async Task DetachRefusesToSeverAnOperationStillInsideItsOwnTimeouts() {
    Skip.If(MonoDebuggee.SkipReason is not null, MonoDebuggee.SkipReason);

    var (_, port) = this.StartDebuggee();

    using var deaf = new BeaconListener(TimeSpan.Zero);
    using var session = new UnitySession(deaf);

    Assert.True(session.Attach(port).Attached);

    using var holding = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();

    // ReSharper disable AccessToDisposedClosure - awaited before the scope ends.
    var holder = Task.Run(() => session.Exclusive(() => {
          holding.Set();

          return release.Wait(TimeSpan.FromSeconds(10)) ? 1 : 0;
        }
      )
    );

    Assert.True(holding.Wait(TimeSpan.FromSeconds(10)), "the holder never took the gate");

    // Severing costs the debuggee a descriptor it never reclaims, so a young operation is left to
    // finish: it is inside the bounds its own waits carry and will let go on its own.
    var refused = Assert.Throws<InvalidOperationException>(() => session.Detach());

    release.Set();

    _ = await holder;

    // Refused for a reason the caller can act on: it is likely to end by itself, and here is how
    // to insist. The hedge is load-bearing -- one wait it can be inside is not bounded, so a
    // promise here would be a promise the session cannot keep.
    Assert.Contains("end by itself", refused.Message, StringComparison.Ordinal);
    Assert.Contains("detach again", refused.Message, StringComparison.Ordinal);

    // The whole point: the connection the caller nearly lost is still there.
    Assert.True(session.Snapshot().Attached);
    Assert.True(session.Detach());
  }

  [SkippableFact]
  public async Task BreakingAGateHeldOverADeadPeerReportsTheNothingItFreed() {
    Skip.If(MonoDebuggee.SkipReason is not null, MonoDebuggee.SkipReason);

    var (debuggee, port) = this.StartDebuggee();

    using var deaf = new BeaconListener(TimeSpan.Zero);
    using var session = new UnitySession(deaf);

    Assert.True(session.Attach(port).Attached);

    using var holding = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();

    // ReSharper disable AccessToDisposedClosure - awaited before the scope ends.
    var holder = Task.Run(() => session.Exclusive(() => {
          holding.Set();

          return release.Wait(TimeSpan.FromSeconds(10)) ? 1 : 0;
        }
      )
    );

    Assert.True(holding.Wait(TimeSpan.FromSeconds(10)), "the holder never took the gate");

    // The gate is held and the peer is gone, which is the one shape that reaches past the refusal
    // without waiting a threshold out: a connection with nothing left to spend is not rationed.
    // What this pins is the ANSWER, not the sever -- a dead peer's transport is already gone, so
    // closing it again changes nothing observable, and this case cannot hold that line. Only a
    // LIVE holder older than the sever threshold can, which no test waits out; the roadmap's
    // wire-progress entry carries that gap.
    MonoDebuggee.Kill(debuggee);

    for (var deadline = DateTime.UtcNow.AddSeconds(10);
      session.Snapshot().Attached &&
      DateTime.UtcNow < deadline;) {
      Thread.Sleep(25);
    }

    // False, not true: the peer was already dead, so this freed the nothing it had become -- the
    // same answer the gate-free path gives for the same state.
    Assert.False(session.Detach());

    release.Set();

    _ = await holder;
  }

  [SkippableFact]
  public void APeerThatDiesWhileIdleStopsBeingReportedAsAttached() {
    Skip.If(MonoDebuggee.SkipReason is not null, MonoDebuggee.SkipReason);

    var (debuggee, port) = this.StartDebuggee();

    using var session = new UnitySession(this.beacons);

    Assert.True(session.Attach(port).Attached);

    MonoDebuggee.Kill(debuggee);

    // Nothing from here on drives the game: no suspend window, no command on the wire. The status
    // surfaces have to notice the death by themselves, which is the whole point of them -- an agent
    // asks for status precisely when it does not want to touch the game.
    for (
      var deadline = DateTime.UtcNow.AddSeconds(10);
      session.Snapshot().Attached && DateTime.UtcNow < deadline;) {
      Thread.Sleep(25);
    }

    var snapshot = session.Snapshot();

    Assert.False(snapshot.Attached);
    Assert.Null(snapshot.Host);
    Assert.Null(snapshot.Port);
    Assert.Null(snapshot.VmVersion);
    Assert.Null(snapshot.Protocol);
    Assert.Equal(0, snapshot.HeldSuspends);
    Assert.Null(session.DebugOrNull);
    Assert.Equal(0, session.HeldSuspendCount);

    // The dead attach is still on record, so detaching clears it -- and reports it as the nothing
    // it was rather than as a session it just freed.
    Assert.False(session.Detach());
  }

  [SkippableFact]
  public void AHoldASequenceTakesForItselfDoesNotReportAsTheCallersWindow() {
    Skip.If(MonoDebuggee.SkipReason is not null, MonoDebuggee.SkipReason);

    var (_, port) = this.StartDebuggee();

    using var session = new UnitySession(this.beacons);

    Assert.True(session.Attach(port).Attached);

    // The shape a capture makes on its own: the game really frozen, so every guard asking whether
    // it is held says yes and the window it advances is real, and nothing reported to an agent who
    // opened no window and would resume one they never took.
    Assert.Equal(0, session.SuspendHold(reported: false));
    Assert.Equal(1, session.HeldSuspendCount);
    Assert.Equal(0, session.Snapshot().HeldSuspends);
    session.RequireHold();
    Assert.False(session.AdvanceHold(TimeSpan.FromSeconds(0.1)));

    // Stacked under a window the caller does hold, only theirs is reported -- and every surface
    // that answers them reports the same number.
    Assert.Equal(1, session.SuspendHold());
    Assert.Equal(2, session.HeldSuspendCount);
    Assert.Equal(1, session.Snapshot().HeldSuspends);

    Assert.Equal(1, session.ResumeHold(reported: false));
    Assert.Equal(1, session.HeldSuspendCount);
    Assert.Equal(1, session.Snapshot().HeldSuspends);

    Assert.Equal(0, session.ResumeHold());
    Assert.Equal(0, session.HeldSuspendCount);

    Assert.True(session.Detach());
  }

  [Fact]
  public void AFailedAttachSaysThePortWasGivenRatherThanDiscovered() {
    // Nothing listens here, which is what a mistyped port usually is and what fails fastest. Only
    // the wording is under test; SdbConnectBoundTests owns the peers that fail slowly.
    var port = MonoDebuggee.PickFreePort();

    using var session = new UnitySession(this.beacons);

    var failure = Assert.ThrowsAny<Exception>(() => session.Attach(port));

    Assert.Contains($"port {port}", failure.Message, StringComparison.Ordinal);
    Assert.Contains("attach tool", failure.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void AFailedAttachToTheAdvertisedEndpointSaysTheBeaconAdvertisedIt() {
    // Listening on a port of the group no player broadcasts on: the session attaches to whatever
    // the listener holds, so on a shared port a game running here would replace the synthetic
    // beacon and be attached to in its place.
    var beaconPort = MonoDebuggee.PickFreePort();

    using var listener = new BeaconListener(ports: [beaconPort]);

    // Nothing listens on this port, which is what a beacon a crashed game left fresh names.
    var port = MonoDebuggee.PickFreePort();
    var (guid, payload) = BeaconListenerTests.SyntheticBeacon(port);

    var sent = BeaconListenerTests.BroadcastUntil(
      payload,
      beaconPort,
      // ReSharper disable once AccessToDisposedClosure
      () => listener.Latest?.ConnectionGuid == guid
    );

    Assert.True(
      sent,
      "no beacon reached the listener; see " +
      nameof(BeaconListenerTests.ReceivesABeaconSentToAnyPortOfTheGroupAndDerivesItsEndpoint)
    );

    using var session = new UnitySession(listener);

    var failure = Assert.ThrowsAny<Exception>(() => session.Run(ctx => ctx.Vm.Version.VMVersion));

    Assert.Contains($"127.0.0.1:{port}", failure.Message, StringComparison.Ordinal);
    Assert.Contains("advertised", failure.Message, StringComparison.Ordinal);
    Assert.Contains("status", failure.Message, StringComparison.Ordinal);
    Assert.Contains("stale", failure.Message, StringComparison.Ordinal);

    // Kept underneath, so whatever classifies the failure as a lost connection still does.
    _ = Assert.IsType<SocketException>(failure.InnerException);
  }

  [SkippableFact]
  public void AConnectionHeldToTheDebuggerPortReadsAsAHeldSlotUntilItCloses() {
    Skip.If(MonoDebuggee.SkipReason is not null, MonoDebuggee.SkipReason);

    var (_, port) = this.StartDebuggee();

    Assert.False(SdbSession.IsSlotHeld("127.0.0.1", port));

    // A bare connection rather than an attach: the detection reads the connection table, so any
    // client established on the port is a holder, and nothing here speaks to the agent.
    using (var holder = new TcpClient()) {
      holder.Connect(IPAddress.Loopback, port);

      Assert.True(SdbSession.IsSlotHeld("127.0.0.1", port));
    }

    Assert.False(SdbSession.IsSlotHeld("127.0.0.1", port));
  }

  [SkippableFact]
  public void AHolderOnLoopbackHoldsTheSlotAtEveryAddressOfThisMachineOnly() {
    // A beacon advertises the interface address the player picked, while a client on the same
    // machine usually dials loopback: both name one slot.
    var lan = NetworkInterface.GetAllNetworkInterfaces()
      .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
      .Select(unicast => unicast.Address)
      .FirstOrDefault(address =>
        address.AddressFamily is AddressFamily.InterNetwork && !IPAddress.IsLoopback(address)
      );

    Skip.If(lan is null, "this machine has no address other than loopback");

    // A plain listener rather than a debuggee: the detection reads the connection table alone.
    var port = MonoDebuggee.PickFreePort();
    var listener = new TcpListener(IPAddress.Loopback, port);

    listener.Start();

    try {
      using var holder = new TcpClient();

      holder.Connect(IPAddress.Loopback, port);

      Assert.True(SdbSession.IsSlotHeld(lan.ToString(), port));

      // An address of another machine (TEST-NET-1) is not held by a connection to this one.
      Assert.False(SdbSession.IsSlotHeld("192.0.2.1", port));
    }
    finally {
      listener.Stop();
    }
  }

  [Fact]
  public void APortOutsideTheTcpRangeIsRefusedBeforeAnythingIsDialled() {
    using var session = new UnitySession(this.beacons);

    var failure = Assert.ThrowsAny<Exception>(() => session.Attach(0));

    Assert.Contains("1-65535", failure.Message, StringComparison.Ordinal);
    Assert.False(session.Snapshot().Attached);
  }

  public void Dispose() {
    this.beacons.Dispose();

    foreach (var debuggee in this.debuggees) {
      MonoDebuggee.Kill(debuggee);
      debuggee.Dispose();
    }
  }

  /// <summary>
  /// Starts a debuggee and returns it once its agent listens. suspend=y opens that socket before
  /// any managed code runs, and the wait watches the OS listener table rather than probing with a
  /// connect: Mono's agent greets first and treats an abandoned handshake as fatal, so a probe
  /// would kill the debuggee it was checking on.
  /// </summary>
  private (Process Process, int Port) StartDebuggee() {
    var port = MonoDebuggee.PickFreePort();
    var debuggee = MonoDebuggee.Start(port, suspend: true);

    this.debuggees.Add(debuggee);

    // The streams stay redirected so the agent's own diagnostics cannot reach the runner's console;
    // drain them so a full pipe buffer can never stall the debuggee.
    debuggee.BeginOutputReadLine();
    debuggee.BeginErrorReadLine();

    for (var deadline = DateTime.UtcNow.AddSeconds(15); DateTime.UtcNow < deadline;) {
      if (debuggee.HasExited) {
        throw new InvalidOperationException(
          $"the Mono debuggee exited with code {debuggee.ExitCode} before it listened on {port}"
        );
      }

      if (UnitySessionAttachTests.IsListening(port)) {
        return (debuggee, port);
      }

      Thread.Sleep(50);
    }

    throw new InvalidOperationException($"the Mono debuggee never listened on 127.0.0.1:{port}");
  }

  private static bool IsListening(int port) =>
    IPGlobalProperties.GetIPGlobalProperties()
      .GetActiveTcpListeners()
      .Any(endpoint => endpoint.Port == port);
}
