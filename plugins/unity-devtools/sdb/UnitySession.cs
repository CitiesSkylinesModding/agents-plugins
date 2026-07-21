using System.Net.Sockets;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>
/// A persistent debugger session against one running dev-Mono Unity game: lazily attaches on the
/// first operation that needs the VM (discovering the endpoint via <see cref="SdbDiscovery"/> when
/// no port is configured), transparently reattaches once when the connection drops, and keeps the
/// game running between operations by opening a counted suspend window around each one.
/// <see cref="SuspendHold"/>/<see cref="ResumeHold"/> hold an extra suspension across operations
/// when consistency between several reads/writes matters (the game is fully frozen meanwhile).
/// Thread-safe; the debugger slot is exclusive, so <see cref="Detach"/> frees it for other tools.
/// </summary>
public sealed class UnitySession(UnitySessionConfig config) : IDisposable {
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

  private string attachedHost;

  private int attachedPort;

  private string attachedVmVersion;

  private string attachedProtocol;

  private int heldSuspends;

  public UnitySessionConfig Config { get; } = config ?? new UnitySessionConfig();

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
          // its first request (the pump only starts then).
          this.invoker ??= new Invoker(vm);

          lock (this.stateGate) {
            this.debug ??= new DebugController(vm, this.invoker);
          }

          return operation(new SdbContext(vm, this.invoker, this.debug));
        }
        catch (Exception e) when (UnitySession.IsDisconnect(e)) {
          // Mid-operation disconnect: the operation may have partially applied in the debuggee,
          // so it is NOT retried; surface the loss instead (the closed socket resumed the game).
          this.LoseConnection();

          throw new InvalidOperationException(
            "the debugger connection dropped mid-operation; the game resumed and the operation " +
            "may have partially applied - verify its effect before redoing it",
            e
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

  /// <summary>Releases one held suspension; returns the count still held.</summary>
  public int ResumeHold() {
    lock (this.gate) {
      if (this.heldSuspends is 0) {
        throw new InvalidOperationException("no suspension is held (nothing to resume)");
      }

      if (this.session is null) {
        // The connection has died since the hold; the closed socket already resumed the VM.
        lock (this.stateGate) {
          this.heldSuspends = 0;
        }

        return 0;
      }

      try {
        this.session.Vm.Resume();
      }
      catch (Exception e) when (UnitySession.IsDisconnect(e)) {
        this.Discard();

        return 0;
      }

      lock (this.stateGate) {
        return --this.heldSuspends;
      }
    }
  }

  /// <summary>
  /// Resumes everything and detaches, freeing the exclusive debugger slot (e.g., for an IDE).
  /// Returns false when there was no live attach.
  /// </summary>
  public bool Detach() {
    lock (this.gate) {
      if (this.session is null) {
        return false;
      }

      this.Discard();

      return true;
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
        return this.debug;
      }
    }
  }

  /// <summary>Suspensions held via <see cref="SuspendHold"/> (the unified-pause fallback).</summary>
  public int HeldSuspendCount {
    get {
      lock (this.stateGate) {
        return this.heldSuspends;
      }
    }
  }

  /// <summary>
  /// Releases EVERY held suspension for the given duration, then re-takes them all: the
  /// deterministic "let the simulation react" window (a single resume would leave the VM frozen
  /// whenever more than one hold is stacked). A breakpoint hit during the window pauses the game
  /// normally (the re-taken holds then stack on top of the event pause).
  /// VM operations block for the whole window by design (a suspend window opened mid-advance
  /// would freeze the very frames the caller is trying to let run); status reads stay live
  /// through <see cref="stateGate"/>.
  /// Returns whether an event-caused suspension is active (or imminent) after the window.
  /// </summary>
  public bool AdvanceHold(TimeSpan duration) {
    lock (this.gate) {
      if (this.heldSuspends is 0) {
        throw new InvalidOperationException(
          "no suspension is held; advance releases a held suspend window (use the suspend tool " +
          "first)"
        );
      }

      // An event-caused suspension (active pause, or a suspending event set the pump is still
      // classifying) would keep the VM frozen through the whole window, silently advancing
      // nothing (surfaced live: a hot breakpoint re-hit right after resume).
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
      return new UnitySessionSnapshot {
        Attached = this.session is not null,
        Host = this.attachedHost,
        Port = this.session is not null ? this.attachedPort : null,
        VmVersion = this.attachedVmVersion,
        Protocol = this.attachedProtocol,
        HeldSuspends = this.heldSuspends
      };
    }
  }

  public void Dispose() => this.Detach();

  private VirtualMachine EnsureAttached() {
    if (this.session is not null) {
      return this.session.Vm;
    }

    var (host, port) = this.ResolveEndpoint();

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
    var host = this.Config.Host ?? "127.0.0.1";

    if (this.Config.Port is {} configured) {
      return (host, configured);
    }

    var prefix = this.Config.ProcessNamePrefix;

    var candidates = SdbDiscovery.Locate(prefix).Where(p => p.SdbPort is not null).ToList();

    // The above-range fallback exists for agent drift, but arbitrary apps also happen to listen
    // on ephemeral ports at or above the range start; a SINGLE strict in-range candidate outranks
    // them as the game (verified live: only noise sat above the range).
    // Tradeoff: if the game's agent ever drifts above the range while a noise process sits
    // in-range, this picks the noise port; an explicit port (UNITY_MCP_PORT) is the escape hatch.
    // Ambiguity errors list every candidate, above-range ones included, so nothing is silently
    // dropped.
    var inRange = candidates.Where(p =>
        p.SdbPort is >= SdbDiscovery.PortRangeStart and <= SdbDiscovery.PortRangeEnd
      )
      .ToList();

    if (inRange.Count is 1) {
      // ReSharper disable once PossibleInvalidOperationException
      return (host, inRange[0].SdbPort.Value);
    }

    var scope = string.IsNullOrEmpty(prefix) ? "process" : $"process matching '{prefix}*'";

    return candidates.Count switch {
      // ReSharper disable once PossibleInvalidOperationException
      1 => (host, candidates[0].SdbPort.Value),
      0 => throw new InvalidOperationException(
        $"no dev-Mono Unity game found (no {scope} exposes an SDB port); " +
        "is the game running as a development Mono build?"
      ),
      _ => throw new InvalidOperationException(
        "several dev-Mono Unity candidates found: " +
        string.Join(
          ", ",
          candidates.Select(c =>
            $"{c.Name} (pid {c.Pid}, sdb port {c.SdbPort}" +
            $"{(inRange.Contains(c) ? "" : ", above range")})"
          )
        ) +
        "; restrict discovery with a process-name prefix or an explicit port"
      )
    };
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

  internal static bool IsDisconnect(Exception e) =>
    e is VMDisconnectedException or IOException or SocketException ||
    (e.InnerException is not null && UnitySession.IsDisconnect(e.InnerException));
}

/// <summary>How a <see cref="UnitySession"/> finds its game; every field is optional.</summary>
public sealed class UnitySessionConfig {
  /// <summary>Debugger host; defaults to 127.0.0.1 (discovery is local-only anyway).</summary>
  public string Host { get; init; }

  /// <summary>Explicit SDB port; when null, the port is discovered on each (re)attach.</summary>
  public int? Port { get; init; }

  /// <summary>
  /// Process-name prefix narrowing discovery; null auto-discovers by port signature.
  /// </summary>
  public string ProcessNamePrefix { get; init; }
}

/// <summary>What a <see cref="UnitySession"/> currently holds, for status reporting.</summary>
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
public sealed class SdbContext(VirtualMachine vm, Invoker invoker, DebugController debug) {
  public VirtualMachine Vm { get; } = vm;

  public Invoker Invoker { get; } = invoker;

  /// <summary>The attach's breakpoint/pause surface (idle until its first request).</summary>
  public DebugController Debug { get; } = debug;

  /// <summary>Builds the ECS surface for one operation (world resolved fresh each time).</summary>
  public Ecs Ecs(string worldName = null) => new(this.Invoker, worldName);
}
