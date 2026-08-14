using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace UnityDevtools.Sdb;

/// <summary>
/// Receives the PlayerConnection target-info beacon Unity players multicast, and holds the latest
/// one seen -- for as long as it still describes the present, see <see cref="Freshness"/> -- so
/// endpoint resolution can read an address instead of inferring one.
/// It listens from construction to disposal rather than on demand: a game already running when the
/// server starts broadcasts on its own schedule, so an on-demand listen would gamble on catching
/// the next one.
/// The group is joined on EVERY multicast-capable interface, because on a machine carrying a VPN, a
/// hypervisor switch, or WSL, the packets arrive on whichever the OS chose, and joining only the
/// default one receives nothing at all.
/// </summary>
public sealed class BeaconListener : IDisposable {
  /// <summary>The multicast group Unity players broadcast their target info on.</summary>
  public const string MulticastGroup = "225.0.0.222";

  /// <summary>
  /// The port that group is served on.
  /// Unity also documents 34997, 57997 and 58997; players use this one.
  /// </summary>
  public const int MulticastPort = 54997;

  /// <summary>
  /// How long <see cref="Wait"/> gives a first beacon before concluding no game is advertising
  /// itself. The listener runs from server start and players rebroadcast about once a second, so
  /// this covers the first moments of a server's life rather than the steady state.
  /// </summary>
  public static readonly TimeSpan DiscoveryWait = TimeSpan.FromSeconds(3);

  /// <summary>
  /// How long a sighting keeps describing the present. Players rebroadcast about once a second and
  /// go on doing so while a debugger is attached, so this is roughly ten missed broadcasts: far
  /// enough out that a scheduling or network stall cannot expire a game that is still running, and
  /// close enough in that a game which exited stops being offered as somewhere to attach.
  /// Without it the last sighting outlives its game indefinitely, and a restart moves the port
  /// anyway, so what it names is not merely old but wrong.
  /// A player SUSPENDED by this debugger stops broadcasting entirely, measured on a live game, and
  /// resumes within a second of being released. No window can cover that, a hold being unbounded,
  /// so it is the reporting surfaces that read a held suspension beside a missing beacon and say
  /// which one explains the other.
  /// </summary>
  public static readonly TimeSpan Freshness = TimeSpan.FromSeconds(10);

  private static readonly TimeSpan ReceiveErrorBackoff = TimeSpan.FromMilliseconds(200);

  private readonly Socket socket;

  private readonly ManualResetEventSlim arrived = new();

  /// <summary>
  /// The sighting is replaced wholesale rather than field by field, so a reader always sees a
  /// beacon and its arrival agreeing.
  /// </summary>
  private volatile Sighting latest;

  private volatile bool closed;

  private volatile string unavailable;

  private readonly TimeSpan freshness;

  /// <param name="freshness">
  /// How long a sighting keeps describing the present, <see cref="Freshness"/> by default.
  /// A test shortens it to watch a sighting expire without waiting out the shipped window.
  /// </param>
  public BeaconListener(TimeSpan? freshness = null) {
    this.freshness = freshness ?? BeaconListener.Freshness;

    try {
      this.socket = BeaconListener.Listen();
    }
    catch (Exception ex) when (ex is
      SocketException or
      NetworkInformationException or
      PlatformNotSupportedException or
      InvalidOperationException) {
      // A host that cannot receive the beacon must still get a working server: discovery then
      // answers "nothing is advertised" and the explicit attach endpoint stays reachable, which is
      // the documented recovery for exactly this host. Every way the network stack can refuse is
      // caught, since one that escaped here would take the server down at startup.
      this.unavailable = ex.Message;

      return;
    }

    new Thread(this.ReceiveLoop) {
      Name = "PlayerConnection beacon",
      IsBackground = true
    }.Start();
  }

  /// <summary>
  /// Why nothing more can be received, or null while the listener is up. Discovery reports it
  /// rather than failing over it, so a machine whose network refuses the group still answers the
  /// question and still takes an explicit port.
  /// Set once and never cleared: every path that reaches it has ended the listen for good, and
  /// silence with no reason attached would send the reader after the game's launch options when
  /// the fault is here.
  /// Volatile because the receive thread is one of the two writers and every reader is a tool
  /// thread, which would otherwise be free to go on seeing the null.
  /// </summary>
  public string Unavailable => this.unavailable;

  /// <summary>
  /// The most recent beacon received, attachable or not, or null when none has arrived within
  /// <see cref="Freshness"/> -- which is what "no game is advertising itself" means, a game having
  /// no way to announce that it stopped.
  /// A non-attachable one is kept deliberately: it is what tells "the game is running without the
  /// debugger" apart from "no game is running".
  /// </summary>
  public PlayerConnectionBeacon Latest {
    get {
      var sighting = this.latest;

      return sighting is not null && Stopwatch.GetElapsedTime(sighting.ArrivedAt) < this.freshness
        ? sighting.Beacon
        : null;
    }
  }

  /// <summary>
  /// <see cref="Latest"/>, waiting up to <paramref name="timeout" /> (<see cref="DiscoveryWait"/>
  /// by default) when nothing is currently advertised. Null when nothing arrives in time.
  /// Even a caller that only reports needs the wait, and needs it on every call rather than while
  /// the listener warms up: a game launched or restarted just now has not broadcast yet either, and
  /// answering "no game is running" in that gap fails the reattach a restart exists to survive.
  /// </summary>
  public PlayerConnectionBeacon Wait(TimeSpan? timeout = null) {
    // On the monotonic clock, like freshness: a wall-clock correction mid-wait would otherwise
    // stretch the wait by the size of the step, hanging a tool call for as long as the step lasted.
    var clock = Stopwatch.StartNew();
    var limit = timeout ?? BeaconListener.DiscoveryWait;

    while (true) {
      // Reset BEFORE reading, so a beacon stored between the read and the wait re-signals rather
      // than being slept through.
      this.arrived.Reset();

      // Through Latest rather than the field, so every caller inherits one definition of what is
      // currently advertised.
      var beacon = this.Latest;

      if (beacon is not null) {
        return beacon;
      }

      // Read AFTER the sighting: a listen that ended part-way through the process leaves what it
      // already heard readable for the rest of its freshness, and that beacon is still the answer.
      // Past it there is nothing left to wait for, since no thread will ever signal again.
      if (this.Unavailable is not null) {
        return null;
      }

      var remaining = limit - clock.Elapsed;

      if (remaining <= TimeSpan.Zero) {
        return null;
      }

      this.arrived.Wait(remaining);
    }
  }

  public void Dispose() {
    // Set first: the receive loop reads it to tell a disposal from a real failure, and closing the
    // socket is what wakes it out of its blocking receive.
    this.closed = true;

    // The signal is deliberately left undisposed: nothing here ever touches its WaitHandle, so it
    // holds no unmanaged resource, and disposing it under a receive thread still between a datagram
    // and its Set would throw on a thread that must never throw.
    this.socket?.Dispose();
  }

  private void ReceiveLoop() {
    var buffer = new byte[4096];

    while (!this.closed) {
      int length;

      try {
        length = this.socket.Receive(buffer);
      }
      catch (Exception ex) {
        if (this.closed) {
          return;
        }

        // This thread must never throw: an unhandled exception on it would take the server down
        // over a UDP hiccup. Windows reports WSAECONNRESET on a datagram socket after an ICMP
        // unreachable, which the next receive recovers from; anything else ends the listen, leaving
        // the last beacon readable rather than spinning on a socket that will not answer.
        if (ex is not SocketException) {
          this.unavailable = ex.Message;

          return;
        }

        Thread.Sleep(BeaconListener.ReceiveErrorBackoff);

        continue;
      }

      var beacon = PlayerConnectionBeacon.Parse(Encoding.UTF8.GetString(buffer, 0, length));

      if (beacon is null) {
        continue;
      }

      this.latest = new Sighting(beacon, Stopwatch.GetTimestamp());

      this.arrived.Set();
    }
  }

  /// <summary>
  /// Binds the group's port and joins the group on every multicast-capable interface, or throws
  /// when nothing on this machine will carry a beacon.
  /// </summary>
  private static Socket Listen() {
    var group = IPAddress.Parse(BeaconListener.MulticastGroup);
    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    try {
      // A beacon is a broadcast, so several listeners on one machine (a second server process, an
      // IDE's Unity integration) is the normal case rather than a conflict.
      socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
      socket.Bind(new IPEndPoint(IPAddress.Any, BeaconListener.MulticastPort));

      var joined = 0;

      foreach (var index in BeaconListener.MulticastInterfaceIndexes()) {
        try {
          socket.SetSocketOption(
            SocketOptionLevel.IP,
            SocketOptionName.AddMembership,
            new MulticastOption(group, index)
          );

          joined++;
        }
        catch (SocketException) {
          // An interface can refuse the join (it went down since the enumeration, or it has no IPv4
          // route); one that does simply carries no beacon.
        }
      }

      return joined > 0
        ? socket
        : throw new InvalidOperationException(
          "no network interface on this machine would join the PlayerConnection beacon group " +
          $"{BeaconListener.MulticastGroup}, so no Unity game can be discovered"
        );
    }
    catch {
      socket.Dispose();

      throw;
    }
  }

  private static IEnumerable<int> MulticastInterfaceIndexes() {
    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
      if (nic.OperationalStatus is not OperationalStatus.Up || !nic.SupportsMulticast) {
        continue;
      }

      IPv4InterfaceProperties ipv4;

      try {
        ipv4 = nic.GetIPProperties().GetIPv4Properties();
      }
      catch (NetworkInformationException) {
        continue;
      }

      if (ipv4 is not null) {
        yield return ipv4.Index;
      }
    }
  }

  /// <param name="ArrivedAt">
  /// The arrival read from the monotonic clock, which is what freshness is measured against: a
  /// wall-clock correction would otherwise expire a live sighting or revive a dead one.
  /// </param>
  private sealed record Sighting(PlayerConnectionBeacon Beacon, long ArrivedAt);
}
