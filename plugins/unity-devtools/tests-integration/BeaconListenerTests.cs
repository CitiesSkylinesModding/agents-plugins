using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// Serializes the classes that care what is on the beacon group, which is a real shared resource:
/// this machine's multicast traffic.
/// One of them broadcasts synthetic beacons to the real group, and another skips itself whenever a
/// game appears to be advertising. Run in parallel, the first makes the second skip on its own
/// traffic -- silently, and in CI as readily as locally, so the test simply stops running.
/// </summary>
[CollectionDefinition(BeaconGroupCollection.Name)]
public sealed class BeaconGroupCollection {
  public const string Name = "PlayerConnection beacon group";
}

/// <summary>
/// The multicast receive path, against a synthetic beacon sent to the real group.
/// This is the one thing the offline parser suite cannot cover and the single point of failure of
/// beacon discovery: joining the group, binding an interface that actually receives, and reading
/// the datagram. It therefore FAILS rather than skips on a host that filters multicast: a skip
/// would hide exactly the condition the test exists to detect.
/// The one carve-out is a port this machine reserves for something else, which the listener reports
/// for itself and which is therefore not the silent condition being hunted. Never for the attested
/// port, whose loss is the whole of discovery.
/// </summary>
[Collection(BeaconGroupCollection.Name)]
public sealed class BeaconListenerTests {
  /// <summary>The one port of the group observed player traffic uses.</summary>
  private const int AttestedPort = 54997;

  private static readonly TimeSpan DeliveryWait = TimeSpan.FromSeconds(10);

  private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(200);

  /// <summary>
  /// One case per port the listener binds. Each bind is independent and can fail on its own, so a
  /// single case leaves the rest asserted by nothing, and a port that stops being bound is
  /// invisible from inside the process and indistinguishable from filtered multicast from outside.
  /// </summary>
  public static TheoryData<int> EveryMulticastPort => [.. BeaconListener.MulticastPorts];

  [SkippableTheory]
  [MemberData(nameof(BeaconListenerTests.EveryMulticastPort))]
  public void ReceivesABeaconSentToAnyPortOfTheGroupAndDerivesItsEndpoint(int port) {
    // The cases come from the list under test, so dropping a port from it would shrink the theory
    // rather than fail it. This pins the one port observed player traffic uses, which is the whole
    // of discovery in practice.
    Assert.Contains(BeaconListenerTests.AttestedPort, BeaconListener.MulticastPorts);

    var (guid, payload) = BeaconListenerTests.SyntheticBeacon();

    using var listener = new BeaconListener();

    // A port this machine reserves elsewhere (Hyper-V, WSL and Docker each exclude blocks of the
    // dynamic range, where three of the four sit) refuses the bind whatever the listener does.
    // That is a condition of the host rather than the silent unbinding this test exists to catch,
    // so it is not a failure here -- except on the attested port, whose loss is the whole of
    // discovery and must red the build.
    // Asked of the OS rather than read out of Fault, whose wording is not this test's business.
    Skip.If(
      port != BeaconListenerTests.AttestedPort && !BeaconListenerTests.Bindable(port),
      $"this machine will not let anything bind {port}"
    );

    // Captured as the predicate sees it rather than re-read below: a real game advertising on this
    // machine can replace the listener's sighting in between, and the endpoint assertions would
    // then fail against its beacon while blaming multicast delivery.
    PlayerConnectionBeacon? ours = null;

    var sent = BeaconListenerTests.BroadcastUntil(
      payload,
      port,
      // ReSharper disable once AccessToDisposedClosure
      () => (ours = listener.Latest)?.ConnectionGuid == guid
    );

    Assert.True(
      sent,
      $"no beacon reached the listener in {BeaconListenerTests.DeliveryWait.TotalSeconds:0}s, " +
      $"though datagrams were sent to {BeaconListener.MulticastGroup}:{port} throughout. Either " +
      "this host filters IPv4 multicast (a firewall, a VPN interface, or a container with no " +
      "multicast route), which is the one condition beacon discovery cannot work through, or the " +
      "listener no longer binds that port. The listener " +
      $"reports: {listener.Fault ?? "it came up, so the packets were dropped in transit"}."
    );

    Assert.Equal("127.0.0.1", ours!.Host);
    Assert.Equal(56000 + (int) (guid % 1000), ours.SdbPort);
    Assert.True(ours.Attachable);
  }

  [SkippableFact]
  public void ASightingStopsBeingReportedOnceItsGameGoesQuiet() {
    var (guid, payload) = BeaconListenerTests.SyntheticBeacon();
    var freshness = TimeSpan.FromMilliseconds(400);

    using var listener = new BeaconListener(freshness);

    // The assertion is that NOTHING is reported once the synthetic sender stops, so a real game
    // advertising itself would keep the listener supplied and the test would pass with the expiry
    // ripped out. Skipping is the only honest answer: there is no way to unsee another
    // broadcaster on a shared multicast group.
    Skip.If(
      listener.Wait(BeaconListener.DiscoveryWait) is not null,
      "a Unity game is advertising itself on this machine"
    );

    var sent = BeaconListenerTests.BroadcastUntil(
      payload,
      // This test is about expiry, not about coverage, so one port carries it.
      BeaconListenerTests.AttestedPort,
      // ReSharper disable once AccessToDisposedClosure
      () => listener.Latest?.ConnectionGuid == guid
    );

    Assert.True(
      sent,
      "no beacon reached the listener; see " +
      nameof(BeaconListenerTests.ReceivesABeaconSentToAnyPortOfTheGroupAndDerivesItsEndpoint)
    );

    // Nothing is sent from here on, which is what a game exiting looks like from the outside: a
    // player cannot announce that it stopped, it simply stops broadcasting.
    Thread.Sleep(freshness + freshness);

    Assert.Null(listener.Latest);

    // The wait path answers from the same view, so waiting does not resurrect what expired -- and
    // a real wait still returns, rather than blocking on a sighting that can never refresh.
    Assert.Null(listener.Wait(TimeSpan.Zero));
    Assert.Null(listener.Wait(freshness));
  }

  /// <summary>
  /// A beacon payload with a GUID unique to this run: a real game advertising itself on this
  /// machine puts its own beacons on the same group, and an assertion has to be about ours.
  /// </summary>
  private static (uint Guid, string Payload) SyntheticBeacon() {
    var guid = (uint) Random.Shared.Next(1, 1000000);

    return (
      guid,
      $"[IP] 127.0.0.1 [Port] 55000 [Flags] 2 [Guid] {guid} [EditorId] 0 [Version] 1048832 " +
      "[Id] SyntheticPlayer(1,TEST) [Debug] 1 [PackageName] SyntheticPlayer " +
      "[ProjectName] beacon listener test"
    );
  }

  /// <summary>
  /// Whether anything on this machine may bind the port at all, which an OS-level exclusion range
  /// denies outright. Uses the listener's own options, so a port it could take reads as bindable.
  /// </summary>
  private static bool Bindable(int port) {
    using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    try {
      probe.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
      probe.Bind(new IPEndPoint(IPAddress.Any, port));

      return true;
    }
    catch (SocketException) {
      return false;
    }
  }

  /// <summary>
  /// Sends the payload to one of the beacon group's ports until <paramref name="received"/> holds,
  /// and reports whether it ever did.
  /// </summary>
  private static bool BroadcastUntil(string payload, int port, Func<bool> received) {
    var group = new IPEndPoint(IPAddress.Parse(BeaconListener.MulticastGroup), port);

    using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    // Explicit rather than inherited: the default is on, but a host that turned it off would fail
    // this test for a reason that has nothing to do with the listener.
    sender.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);

    var datagram = Encoding.UTF8.GetBytes(payload);
    var clock = Stopwatch.StartNew();

    while (clock.Elapsed < BeaconListenerTests.DeliveryWait) {
      sender.SendTo(datagram, group);

      if (received()) {
        return true;
      }

      Thread.Sleep(BeaconListenerTests.SendInterval);
    }

    return received();
  }
}
