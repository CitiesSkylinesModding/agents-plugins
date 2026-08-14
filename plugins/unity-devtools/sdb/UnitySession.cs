using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>
/// A persistent debugger session against one running dev-Mono Unity game: lazily attaches on the
/// first operation that needs the VM, resolving the endpoint from the PlayerConnection beacon,
/// transparently reattaches once when the connection drops, and keeps the game running between
/// operations by opening a counted suspend window around each one.
/// <see cref="SuspendHold"/>/<see cref="ResumeHold"/> hold an extra suspension across operations
/// when consistency between several reads/writes matters (the game is fully frozen meanwhile).
/// Thread-safe; the debugger slot is exclusive, so <see cref="Detach"/> frees it for other tools.
/// </summary>
public sealed class UnitySession(BeaconListener beacons) : IDisposable {
  private readonly Lock gate = new();

  /// <summary>
  /// Guards ONLY the published-state fields (session identity, held count, debug controller) so
  /// read-only accessors (<see cref="Snapshot"/>, <see cref="DebugOrNull"/>,
  /// <see cref="HeldSuspendCount"/>) stay live while <see cref="gate"/> is held through a long
  /// operation (an advance window sleeps up to a minute; debug_status must not block on it).
  /// Lock order: <see cref="gate"/> then <see cref="stateGate"/>, never the reverse.
  /// </summary>
  private readonly Lock stateGate = new();

  private SdbSession session;

  private Invoker invoker;

  private DebugController debug;

  private TypeCatalog types;

  private EcsCatalog ecs;

  private string attachedHost;

  private int attachedPort;

  private string attachedVmVersion;

  private string attachedProtocol;

  private int heldSuspends;

  /// <summary>
  /// Runs one operation inside a suspend window, attaching or reattaching as needed.
  /// </summary>
  public T Run<T>(Func<SdbContext, T> operation) {
    lock (this.gate) {
      for (var attempt = 0;; attempt++) {
        var vm = this.EnsureAttached();

        try {
          vm.Suspend();
        }
        catch (Exception e) when (attempt is 0 && UnitySession.IsDisconnect(e)) {
          // Stale connection detected before the operation ran (typically the game has restarted
          // since the last call): discard and retry once against a freshly discovered endpoint.
          // Only this pre-operation window retries: the operation has had no side effects yet.
          this.LoseConnection();

          continue;
        }

        try {
          // The Invoker picks the main thread; build it inside a suspend window where thread
          // listing is guaranteed to be legal. The debug controller is per-attach and idle until
          // its first request (the pump only starts then); so are the two catalogs, which read
          // nothing until an operation asks them something.
          this.invoker ??= new Invoker(vm);
          this.types ??= new TypeCatalog(this.invoker);
          this.ecs ??= new EcsCatalog(this.invoker);

          lock (this.stateGate) {
            this.debug ??= new DebugController(vm, this.invoker);
          }

          return operation(new SdbContext(vm, this.invoker, this.debug, this.types, this.ecs));
        }
        catch (Exception ex) when (UnitySession.IsDisconnect(ex)) {
          // Mid-operation disconnect: the operation may have partially applied in the debuggee,
          // so it is NOT retried; surface the loss instead (the closed socket resumed the game).
          this.LoseConnection();

          throw new InvalidOperationException(
            "the debugger connection dropped mid-operation; the game resumed and the operation " +
            "may have partially applied - verify its effect before redoing it",
            ex
          );
        }
        finally {
          if (this.session is not null) {
            try {
              vm.Resume();
            }
            catch {
              // Connection gone; the closed socket auto-resumes the VM.
            }
          }
        }
      }
    }
  }

  /// <summary>
  /// Holds one extra suspension across operations; returns the held count. The game is fully
  /// frozen until <see cref="ResumeHold"/> releases it (or the session detaches).
  /// </summary>
  public int SuspendHold() {
    return this.Run(ctx => {
        ctx.Vm.Suspend();

        lock (this.stateGate) {
          return ++this.heldSuspends;
        }
      }
    );
  }

  /// <summary>
  /// Throws unless a suspend window is open AND still real, telling the two failures apart: no
  /// window was ever opened, or the connection took the one that was.
  /// A caller checks this before anything a later failure would strand, since the second case reads
  /// as the first from outside -- a dead session reports no held suspension whatever it was
  /// holding.
  /// </summary>
  public void RequireHold() {
    lock (this.gate) {
      this.RequireHoldHeld();
    }
  }

  /// <summary>
  /// <see cref="RequireHold"/>, for a caller already holding <see cref="gate"/>.
  /// </summary>
  private void RequireHoldHeld() {
    if (this.heldSuspends is 0) {
      throw new InvalidOperationException(
        "no suspension is held; a window is opened with the suspend tool"
      );
    }

    // The hold is on record but the connection is not. LoseConnection says so, where carrying on
    // would leave the caller to guess that its consistency window went with the connection.
    if (!this.IsLive) {
      this.LoseConnection();
    }
  }

  /// <summary>
  /// Releases one held suspension; returns the count still held.
  /// </summary>
  public int ResumeHold() {
    lock (this.gate) {
      this.RequireHoldHeld();

      try {
        this.session.Vm.Resume();
      }
      catch (Exception e) when (UnitySession.IsDisconnect(e)) {
        this.LoseConnection();
      }

      lock (this.stateGate) {
        return --this.heldSuspends;
      }
    }
  }

  /// <summary>
  /// Attaches to a local debugger port of the caller's choosing, dropping any live attach first;
  /// with no port given, attaches to what the beacon advertises. Returns what it attached to.
  /// The port is stored nowhere, so every later attach resolves from the beacon again: one that
  /// outlived the game it named would point at a dead endpoint the moment a restart moved the
  /// beacon.
  /// </summary>
  public UnitySessionSnapshot Attach(int? port) {
    (string Host, int Port)? chosen = null;

    if (port is {} given) {
      chosen = given is >= 1 and <= 65535
        ? ("127.0.0.1", given)
        : throw new InvalidOperationException($"{given} is not a TCP port (1-65535)");
    }

    lock (this.gate) {
      this.Discard();

      try {
        this.EnsureAttached(chosen);
      }
      catch (Exception ex) when (chosen is {} endpoint) {
        // Restates the failure as what it is: the caller chose that port, so the fix is a different
        // port rather than a look at the game or its beacon.
        throw new InvalidOperationException(
          $"could not attach to port {endpoint.Port}, the port given to the attach tool " +
          $"(attach with no port to use the beacon): {ex.Message}",
          ex
        );
      }

      return this.Snapshot();
    }
  }

  /// <summary>
  /// Resumes everything and detaches, freeing the exclusive debugger slot (e.g., for an IDE).
  /// Returns false when there was no live attach; a session whose peer already died is cleared the
  /// same way and reported as the nothing it was.
  /// </summary>
  public bool Detach() {
    lock (this.gate) {
      if (this.session is null) {
        return false;
      }

      var wasAlive = this.session.IsAlive;

      this.Discard();

      return wasAlive;
    }
  }

  /// <summary>
  /// The current attach's debug surface, or null when not attached (or not yet used); for
  /// operations that must run WITHOUT a suspend window (waiting, stepping: they need the VM free
  /// to run, which <see cref="Run{T}"/>'s window would prevent).
  /// </summary>
  public DebugController DebugOrNull {
    get {
      lock (this.stateGate) {
        return this.IsLive ? this.debug : null;
      }
    }
  }

  /// <summary>
  /// Suspensions held via <see cref="SuspendHold"/> (the unified-pause fallback).
  /// </summary>
  public int HeldSuspendCount {
    get {
      lock (this.stateGate) {
        return this.IsLive ? this.heldSuspends : 0;
      }
    }
  }

  /// <summary>
  /// Whether the attach on record is still connected: the effective attached state every reporting
  /// surface answers from.
  /// A peer that dies while idle leaves its session in the field until the next operation clears
  /// it, because that cleanup talks to the wire and belongs under <see cref="gate"/>; reading the
  /// field alone would report a dead attach as a live one for as long as nobody drove the game.
  /// Call under either lock: the reporting surfaces read it under <see cref="stateGate"/>, and the
  /// operations that act on the answer already hold <see cref="gate"/>.
  /// </summary>
  private bool IsLive => this.session is not null && this.session.IsAlive;

  /// <summary>
  /// Releases EVERY held suspension for the given duration, then re-takes them all: the
  /// deterministic "let the simulation react" window (a single resume would leave the VM frozen
  /// whenever more than one hold is stacked). A breakpoint hit during the window pauses the game
  /// normally (the re-taken holds then stack on top of the event pause).
  /// VM operations block for the whole window by design (a suspend window opened mid-advance would
  /// freeze the very frames the caller is trying to let run); status reads stay live through
  /// <see cref="stateGate"/>.
  /// Returns whether an event-caused suspension is active (or imminent) after the window.
  /// </summary>
  public bool AdvanceHold(TimeSpan duration) {
    lock (this.gate) {
      this.RequireHoldHeld();

      // An event-caused suspension (active pause, or a suspending event set the pump is still
      // classifying) would keep the VM frozen through the whole window, silently advancing nothing
      // (surfaced live: a hot breakpoint re-hit right after resume).
      if (this.debug?.HoldsSuspension is true) {
        throw new InvalidOperationException(
          "a breakpoint/step/exception pause is holding the game, so the window could not " +
          "advance anything; release it first (debug_step action=resume)"
        );
      }

      var vm = this.session.Vm;
      var holds = this.heldSuspends;

      try {
        for (var i = 0; i < holds; i++) {
          vm.Resume();
        }

        Thread.Sleep(duration);

        for (var i = 0; i < holds; i++) {
          vm.Suspend();
        }
      }
      catch (Exception ex) when (UnitySession.IsDisconnect(ex)) {
        // The hold is gone with the connection; LoseConnection reports it loudly.
        this.LoseConnection();

        throw;
      }

      return this.debug?.HoldsSuspension ?? false;
    }
  }

  public UnitySessionSnapshot Snapshot() {
    lock (this.stateGate) {
      var alive = this.IsLive;

      return new UnitySessionSnapshot {
        Attached = alive,
        Host = alive ? this.attachedHost : null,
        Port = alive ? this.attachedPort : null,
        VmVersion = alive ? this.attachedVmVersion : null,
        Protocol = alive ? this.attachedProtocol : null,

        // A dropped connection resumed the game, so a hold reported against a dead attach would be
        // a second falsehood on top of the first.
        HeldSuspends = alive ? this.heldSuspends : 0
      };
    }
  }

  public void Dispose() => this.Detach();

  private VirtualMachine EnsureAttached((string Host, int Port)? endpoint = null) {
    if (this.session is not null) {
      if (this.session.IsAlive) {
        return this.session.Vm;
      }

      // The peer died while nothing was driving it, so no operation has taken the disconnect path
      // yet. Clearing it here spares the caller a doomed wire call, and LoseConnection still fails
      // loudly when a suspension was held: the closed socket resumed the game, so that consistency
      // window is gone whether the reattach below succeeds.
      this.LoseConnection();
    }

    var (host, port) = endpoint ?? this.ResolveEndpoint();

    var connected = SdbSession.Connect(host, port);

    // The version info is cached from the attach handshake (no extra round-trip); it lets status
    // report the negotiated SDB protocol (generic invokes need 2.24+).
    var version = connected.Vm.Version;

    lock (this.stateGate) {
      this.session = connected;
      this.attachedHost = host;
      this.attachedPort = port;
      this.attachedVmVersion = version.VMVersion;
      this.attachedProtocol = $"{version.MajorVersion}.{version.MinorVersion}";
    }

    return connected.Vm;
  }

  private (string Host, int Port) ResolveEndpoint() {
    var beacon = beacons.Wait();

    if (beacon?.Endpoint is {} endpoint) {
      return endpoint;
    }

    var group = $"{BeaconListener.MulticastGroup}:{BeaconListener.MulticastPort}";

    // Three failures the caller acts on differently: a beacon without [Debug] 1 means the game IS
    // running and only the launch option is missing; a listener that never came up means nothing
    // about any game is knowable here, so blaming launch options would send the caller nowhere.
    // The order is what keeps them apart, an unavailable listener never yielding a beacon.
    var reason = beacon is not null
      ? $"the Unity game advertising itself on {group} ({beacon.Id}) reports no managed " +
      "debugger; relaunch it with 'player-connection-debug=1'"
      : beacons.Unavailable is not null
        ? $"this machine cannot receive the PlayerConnection beacon ({beacons.Unavailable}), so " +
        "no game can be discovered; give the game's debugger port to the attach tool"
        : $"no Unity game is advertising itself on the PlayerConnection beacon ({group}); is the " +
        "game running as a development Mono build launched with 'player-connection-debug=1'? " +
        "If you know its debugger port, give it to the attach tool";

    throw new InvalidOperationException(reason);
  }

  /// <summary>
  /// Drops the current attach; SdbSession.Dispose resumes and detaches best-effort.
  /// </summary>
  private void Discard() {
    try {
      // Clears every debug request BEFORE the session resumes and detaches, so an armed
      // breakpoint can never re-freeze the game after we let go.
      this.debug?.Dispose();
    }
    catch {
      // Best-effort; SdbSession.Dispose clears agent-side requests as the safety net.
    }

    try {
      this.session?.Dispose();
    }
    finally {
      lock (this.stateGate) {
        this.session = null;
        this.invoker = null;
        this.debug = null;

        // Every catalog holds mirrors correlated with this attach, so a reattach rebuilds rather
        // than answering from handles the new connection cannot resolve.
        this.types = null;
        this.ecs = null;
        this.attachedHost = null;
        this.attachedVmVersion = null;
        this.attachedProtocol = null;
        this.heldSuspends = 0;
      }
    }
  }

  /// <summary>
  /// Discards a dropped connection; when a suspension was held, fails loudly instead of letting
  /// the caller carry on, because the closed socket resumed the game and the consistency window
  /// is gone.
  /// </summary>
  private void LoseConnection() {
    var hadHold = this.heldSuspends > 0;

    this.Discard();

    if (hadHold) {
      throw new InvalidOperationException(
        "the debugger connection dropped while a suspension was held; the game resumed and the " +
        "hold was lost - re-suspend and redo the whole window"
      );
    }
  }

  /// <summary>
  /// Whether a failure means the connection is gone.
  /// It matches on ANY <see cref="IOException" />, which is wider than the socket: a client-side
  /// parse can raise one too (<c>AssemblyName</c> rejects a malformed display name with a
  /// FileLoadException), and answering true for one of those discards a live attach and warns the
  /// user their game state may be half-written, over something that never touched the wire.
  /// A caller reading debuggee metadata client-side therefore handles its own parse failures rather
  /// than letting them reach here.
  /// </summary>
  internal static bool IsDisconnect(Exception e) =>
    e is VMDisconnectedException or IOException or SocketException ||
    (e.InnerException is not null && UnitySession.IsDisconnect(e.InnerException));
}

/// <summary>
/// What a <see cref="UnitySession"/> currently holds, for status reporting.
/// </summary>
public sealed class UnitySessionSnapshot {
  public bool Attached { get; init; }

  public string Host { get; init; }

  public int? Port { get; init; }

  /// <summary>The debuggee's Mono VM version string, when attached.</summary>
  public string VmVersion { get; init; }

  /// <summary>The negotiated SDB wire-protocol version, when attached.</summary>
  public string Protocol { get; init; }

  public int HeldSuspends { get; init; }
}

/// <summary>
/// The live-VM surface handed to one <see cref="UnitySession.Run{T}"/> operation.
/// </summary>
public sealed class SdbContext(
  VirtualMachine vm,
  Invoker invoker,
  DebugController debug,
  TypeCatalog types,
  EcsCatalog ecs
) {
  public VirtualMachine Vm { get; } = vm;

  public Invoker Invoker { get; } = invoker;

  /// <summary>
  /// The attach's breakpoint/pause surface (idle until its first request).
  /// </summary>
  public DebugController Debug { get; } = debug;

  /// <summary>
  /// The attach's type catalog (idle until its first search).
  /// </summary>
  public TypeCatalog Types { get; } = types;

  /// <summary>
  /// The attach's ECS catalog (idle until an ECS operation asks it something).
  /// </summary>
  public EcsCatalog EcsCatalog { get; } = ecs;

  /// <summary>
  /// Builds the ECS surface for one operation, as a view over the attach's catalog: the world it
  /// selects is revalidated rather than resolved from scratch.
  /// </summary>
  public Ecs Ecs(string worldName = null) => new(this.Invoker, this.EcsCatalog, worldName);
}
