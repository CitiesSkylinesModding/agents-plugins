using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
  /// The ports that group is served on. Unity's own IDE integration binds every one of them, with
  /// nothing in it distinguishing a port by the kind of target that uses it.
  /// Only 54997 is attested by observed player traffic; 34997 is the one Unity's native side calls
  /// the alternative port, and the last two are attested nowhere. All are bound regardless, because
  /// a player on an unbound port is never discovered at all, and that looks exactly like filtered
  /// multicast: the reader is sent after a firewall they do not have.
  /// </summary>
  public static readonly IReadOnlyList<int> MulticastPorts = [54997, 34997, 57997, 58997];

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

  private readonly (Socket Socket, int Port)[] sockets;

  /// <summary>
  /// Guards the wait: producers pulse it after publishing, waiters sleep on it.
  /// A monitor rather than an event because every waiter must see every pulse, and resetting a
  /// shared event per waiter lets one clear a signal another was about to read.
  /// </summary>
  private readonly object signal = new();

  /// <summary>
  /// The sighting is replaced wholesale rather than field by field, so a reader always sees a
  /// beacon and its arrival agreeing.
  /// </summary>
  private volatile Sighting latest;

  private volatile bool closed;

  /// <summary>
  /// What went wrong on each port that is not being listened on, keyed by port. The ports fail
  /// independently, so each names itself: a reader acts on which one is gone.
  /// </summary>
  private readonly ConcurrentDictionary<int, string> faults = new();

  private readonly TimeSpan freshness;

  /// <param name="freshness">
  /// How long a sighting keeps describing the present, <see cref="Freshness"/> by default.
  /// A test shortens it to watch a sighting expire without waiting out the shipped window.
  /// </param>
  public BeaconListener(TimeSpan? freshness = null) {
    this.freshness = freshness ?? BeaconListener.Freshness;

    // Enumerated once and shared, so every socket joins the same set: an interface flapping
    // mid-loop would otherwise leave the sockets with memberships that disagree.
    var (indexes, enumerationFailure) = BeaconListener.MulticastInterfaceIndexes();

    // A machine that will not describe its interfaces refuses every port for that one cause, and
    // the API's own message is the only thing naming it: the generic wording below would send the
    // reader auditing adapters that are fine.
    var noRoute = enumerationFailure is null
      ? "no network interface on this machine would join the group"
      : $"this machine would not list its network interfaces: {enumerationFailure}";

    var listening = new List<(Socket Socket, int Port)>();

    foreach (var port in BeaconListener.MulticastPorts) {
      var socket = BeaconListener.TryListen(port, indexes, noRoute, out var reason);

      if (socket is null) {
        this.faults[port] = $"could not be listened on: {reason}";
      }
      else {
        listening.Add((socket, port));
      }
    }

    this.sockets = [.. listening];

    foreach (var (socket, port) in this.sockets) {
      try {
        new Thread(() => this.ReceiveLoop(socket, port)) {
          Name = $"PlayerConnection beacon {port}",
          IsBackground = true
        }.Start();
      }
      catch (Exception ex) {
        // Nothing escapes this constructor, for the reason TryListen gives. The socket is released
        // here rather than left bound and joined with nobody receiving on it, the loop that would
        // otherwise have owned it never having started.
        this.faults[port] = $"could not be listened on: {ex.Message}";

        socket.Dispose();
      }
    }
  }

  /// <summary>
  /// Every port of the group whose listen was lost and why, or null while none has been.
  /// A disposal is not a loss and adds nothing here.
  /// Discovery reports it rather than failing over it, so a machine whose network refuses the group
  /// still answers the question and still takes an explicit port.
  /// A fault is not the end of the listen, which is what <see cref="Listening"/> answers: a game
  /// can still be discovered while this is set, and equally the port lost can have been the only
  /// one that ever mattered, a player advertising on exactly one.
  /// An entry is never cleared, no path that records one recovering the port it names.
  /// </summary>
  public string Fault {
    get {
      if (this.faults.IsEmpty) {
        return null;
      }

      // Walked in the group's own order, which the dictionary does not keep, and ports sharing a
      // reason are named together: the common failure is one the whole machine has, so listing it
      // per port would say the same thing as many times as there are ports.
      var lost = BeaconListener.MulticastPorts
        .Where(this.faults.ContainsKey)
        .GroupBy(port => this.faults[port])
        .Select(ports =>
          $"port{(ports.Count() > 1 ? "s" : "")} {string.Join(", ", ports)} {ports.Key}"
        );

      return string.Join("; ", lost);
    }
  }

  /// <summary>
  /// Whether any port can still deliver a beacon. False means a missing beacon says nothing
  /// whatever about whether a game is running, and it is what ends a <see cref="Wait"/>; a
  /// construction that bound nothing, and a disposal, both start there.
  /// Derived from the faults rather than counted alongside them, so the two cannot disagree and
  /// no caller has to read them in a particular order: every port that loses its listen records
  /// why before this turns false. A disposal is the exception, ending the listen without a fault.
  /// </summary>
  public bool Listening =>
    !this.closed && this.faults.Count < BeaconListener.MulticastPorts.Count;

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

    // Held across the whole wait, which Monitor.Wait releases while it sleeps: a producer that
    // publishes between the read and the sleep therefore blocks until the sleep begins, so its
    // pulse can never land in the gap.
    lock (this.signal) {
      while (true) {
        // Through Latest rather than the field, so every caller inherits one definition of what is
        // currently advertised.
        var beacon = this.Latest;

        if (beacon is not null) {
          return beacon;
        }

        // Read AFTER the sighting: a listen that ended part-way through the process leaves what it
        // already heard readable for the rest of its freshness, and that beacon is still the
        // answer. Past it there is nothing left to wait for, since no thread will pulse again.
        if (!this.Listening) {
          return null;
        }

        var remaining = limit - clock.Elapsed;

        if (remaining <= TimeSpan.Zero) {
          return null;
        }

        Monitor.Wait(this.signal, remaining);
      }
    }
  }

  public void Dispose() {
    // Set first: the receive loop reads it to tell a disposal from a real failure, and closing the
    // socket is what wakes it out of its blocking receive.
    this.closed = true;

    // `closed` above is what makes Listening false, so a waiter is released here rather than left
    // to sleep out its whole budget against a listener that is gone.
    this.Pulse();

    foreach (var (socket, _) in this.sockets) {
      socket.Dispose();
    }
  }

  private void ReceiveLoop(Socket socket, int port) {
    var buffer = new byte[4096];

    // Released whichever way this loop ends, or a port reported as lost would go on holding its
    // binding and its group memberships with nobody receiving on it.
    using var owned = socket;

    while (!this.closed) {
      int length;

      try {
        length = owned.Receive(buffer);
      }
      catch (Exception ex) {
        if (this.closed) {
          return;
        }

        // This thread must never throw: an unhandled exception on it would take the server down
        // over a UDP hiccup. Windows reports WSAECONNRESET on a datagram socket after an ICMP
        // unreachable, which the next receive recovers from; anything else ends this port's listen,
        // leaving the last beacon readable rather than spinning on a socket that will not answer.
        // A SocketException that keeps coming back rather than clearing is the case this does not
        // cover, and docs/ROADMAP.md carries it.
        if (ex is not SocketException) {
          this.EndLoop(port, ex.Message);

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

      this.Pulse();
    }
  }

  private void EndLoop(int port, string reason) {
    // Recorded before the pulse, so a waiter this releases reads a Listening and a Fault that
    // already agree.
    this.faults[port] = $"stopped receiving: {reason}";

    this.Pulse();
  }

  /// <summary>Releases every waiter to re-read what the caller has just published.</summary>
  private void Pulse() {
    lock (this.signal) {
      Monitor.PulseAll(this.signal);
    }
  }

  /// <summary>
  /// Binds one of the group's ports and joins the group on the given interfaces, or returns null
  /// and the reason nothing on this machine will carry a beacon on that port.
  /// Null rather than a throw because a refused port is an expected outcome here, not a fault: a
  /// host that cannot receive at all must still get a working server, discovery then answering
  /// "nothing is advertised" while the explicit attach endpoint stays reachable, which is the
  /// documented recovery for exactly that host.
  /// Hence the reach of the catch: the constructor calling this has no net of its own, so anything
  /// escaping would take that recovery down with the discovery it exists to bypass.
  /// </summary>
  private static Socket TryListen(
    int port,
    IReadOnlyCollection<int> indexes,
    string noRoute,
    out string reason
  ) {
    Socket socket = null;

    try {
      var group = IPAddress.Parse(BeaconListener.MulticastGroup);

      socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

      // A beacon is a broadcast, so several listeners on one machine (a second server process, an
      // IDE's Unity integration) is the normal case rather than a conflict.
      socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
      socket.Bind(new IPEndPoint(IPAddress.Any, port));

      var joined = 0;

      foreach (var index in indexes) {
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
          // route); one that does simply carries no beacon. Anything else falls to the boundary
          // below, whose message names the real fault rather than blaming the interfaces.
        }
      }

      if (joined > 0) {
        reason = null;

        return socket;
      }

      reason = noRoute;
    }
    catch (Exception ex) {
      reason = ex.Message;
    }

    socket?.Dispose();

    return null;
  }

  /// <summary>
  /// The interfaces worth joining the group on, or an empty set and what the machine said when
  /// asked to list them.
  /// Materialised rather than lazy: every socket walks it, and the enumeration is the expensive
  /// half of coming up.
  /// Nothing escapes here either, for the reason <see cref="TryListen"/> gives.
  /// </summary>
  private static (IReadOnlyCollection<int> Indexes, string Failure) MulticastInterfaceIndexes() {
    var indexes = new List<int>();

    NetworkInterface[] nics;

    try {
      nics = NetworkInterface.GetAllNetworkInterfaces();
    }
    catch (Exception ex) {
      return (indexes, ex.Message);
    }

    foreach (var nic in nics) {
      try {
        if (nic.OperationalStatus is not OperationalStatus.Up || !nic.SupportsMulticast) {
          continue;
        }

        var ipv4 = nic.GetIPProperties().GetIPv4Properties();

        if (ipv4 is not null) {
          indexes.Add(ipv4.Index);
        }
      }
      catch (Exception) {
        // One adapter that will not describe itself carries no beacon; the rest still can.
      }
    }

    return (indexes, null);
  }

  /// <param name="ArrivedAt">
  /// The arrival read from the monotonic clock, which is what freshness is measured against: a
  /// wall-clock correction would otherwise expire a live sighting or revive a dead one.
  /// </param>
  private sealed record Sighting(PlayerConnectionBeacon Beacon, long ArrivedAt);
}
