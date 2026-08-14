using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// Attaching to a port the caller names rather than one the beacon advertises: the recovery path
/// for a game the beacon does not describe.
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

    // The assertion is the beacon failure a reattach produces, so a game advertising itself here
    // would be attached to instead: an effect on someone's running game rather than a test. A
    // listener that never came up fails the same assertion on its own wording, and the multicast
    // suite is what reports that host anyway.
    // On Listening rather than on Fault, which a lost idle port sets on a listener that still
    // discovers games perfectly well: skipping on that would park this case for good, and a skip
    // reads exactly like a pass.
    Skip.If(
      !this.beacons.Listening || this.beacons.Wait() is not null,
      "this machine advertises a Unity game, or receives no beacon at all"
    );

    var (_, port) = this.StartDebuggee();

    using var session = new UnitySession(this.beacons);

    Assert.True(session.Attach(port).Attached);
    Assert.True(session.Detach());

    // The debuggee is still listening, so a session that had stored the port would reattach to it
    // and this operation would succeed. Nothing stored it, so the reattach asks the beacon and
    // finds nothing advertising itself.
    var failure = Assert.ThrowsAny<Exception>(() => session.Run(ctx => ctx.Vm.Version.VMVersion));

    Assert.Contains("advertising itself", failure.Message, StringComparison.Ordinal);
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
