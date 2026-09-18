using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>
/// A persistent debugger session against one running dev-Mono Unity game: lazily attaches on the
/// first operation that needs the VM, resolving the endpoint from the PlayerConnection beacon, and
/// keeps the game running between operations by opening a counted suspend window around each one.
/// A drop found as an operation OPENS its window is retried once against a freshly resolved
/// endpoint, since nothing has been asked of the game yet; one found mid-operation is not, because
/// the operation may already have applied -- it is surfaced instead, and the call after it
/// reattaches.
/// <see cref="SuspendHold"/>/<see cref="ResumeHold"/> hold an extra suspension across operations
/// when consistency between several reads/writes matters (the game is fully frozen meanwhile).
/// Thread-safe; the debugger slot is exclusive, so <see cref="Detach"/> frees it for other tools.
/// </summary>
public sealed class UnitySession(BeaconListener beacons, TimeSpan? gateWait = null) : IDisposable {
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
  /// How many of <see cref="heldSuspends"/> a sequence took for its own use rather than for the
  /// caller. <see cref="Snapshot"/> stays live through a gate-held sequence by design, so a hold
  /// taken inside one would otherwise report as a window the caller opened, and an agent unwinding
  /// what it believes it took resumes the window it really holds.
  /// It subtracts from what is REPORTED and from nothing else: a hold freezes the VM whoever took
  /// it, so every guard asking whether the game is held reads <see cref="HeldSuspendCount"/>.
  /// </summary>
  private int unreportedHolds;

  /// <summary>
  /// The held count as the CALLER sees it, for a caller already holding <see cref="stateGate"/>.
  /// Every surface that answers an agent goes through this one, so `suspend`, `resume` and
  /// `status` cannot answer a question with three different numbers.
  /// Floored, since a release that fails without disconnecting strands the pair above zero and the
  /// caller's own resume then unwinds the reported side past it.
  /// </summary>
  private int ReportedHolds => Math.Max(this.heldSuspends - this.unreportedHolds, 0);

  /// <summary>
  /// How long a caller waits for the operation in flight before being told what holds the debugger
  /// instead of queueing behind it.
  /// An unbounded wait is what turns ONE stalled wire command into a dead server: every later call
  /// queues on <see cref="gate"/>, <c>attach</c> included, so the session cannot be recovered from
  /// inside and only restarting the server clears it. <see cref="Detach"/> is the one call that
  /// can RECOVER without queueing, which is what keeps that door open; the reporting surfaces
  /// answer throughout for a different reason, being off <see cref="gate"/> entirely.
  /// No budget here can separate slow from stuck: an operation is any number of bounded waits, not
  /// one, so a legitimate sequence (an <c>advance</c> window plus its snippets) can outlast any
  /// figure worth making a caller wait. What the refusal buys is an ANSWER instead of a queue, and
  /// it names the holder's age so the caller can tell the two apart themselves.
  /// </summary>
  private static readonly TimeSpan DefaultGateWait = TimeSpan.FromSeconds(90);

  /// <summary>
  /// <see cref="DefaultGateWait"/>, or what the caller chose. A test drives this down: the refusal
  /// is the behavior under test, and waiting out the shipped budget to see it would be a test
  /// nobody runs.
  /// </summary>
  private readonly TimeSpan gateWait = gateWait ?? UnitySession.DefaultGateWait;

  /// <summary>
  /// What <see cref="Detach"/> waits, before breaking the wedge instead of joining the queue.
  /// Short because it is the escape hatch: a caller reaching for it has usually been told the
  /// debugger is held, and its whole value is not waiting for the thing it exists to break.
  /// </summary>
  private static readonly TimeSpan DefaultDetachWait = TimeSpan.FromSeconds(2);

  /// <summary>
  /// <see cref="DefaultDetachWait"/>, capped at <see cref="gateWait"/>: the hatch never waits
  /// longer than the queue it exists to skip.
  /// </summary>
  private readonly TimeSpan detachWait =
    gateWait is {} wait && wait < UnitySession.DefaultDetachWait
      ? wait
      : UnitySession.DefaultDetachWait;

  /// <summary>
  /// How long an operation must hold the debugger before <see cref="Detach"/> will sever it.
  /// The MAGNITUDE is not derived from the bounds below it, and no figure could be: an operation is
  /// any number of bounded waits, so no finite age tells a healthy long call from a stuck one. What
  /// it buys is that severing takes a deliberate second ask -- an agent retrying <c>detach</c> on
  /// reflex cannot spend a descriptor -- while a caller who waits it out gets their session back
  /// either way.
  /// The ORDER is derived, and has to be: a caller sent here by <see cref="gateWait"/>'s refusal
  /// arrives holding an age already past it, so anything at or below that severs on the first ask
  /// and the second one is never asked for. It is therefore measured from the gate wait this
  /// session actually carries rather than from the default, which a constructed wait can exceed.
  /// </summary>
  private readonly TimeSpan wedgeAfter =
    (gateWait ?? UnitySession.DefaultGateWait) + TimeSpan.FromSeconds(30);

  /// <summary>
  /// When the operation in flight took <see cref="gate"/> (<see cref="Stopwatch"/> ticks), or 0
  /// when none is. Read under <see cref="stateGate"/> like every other reporting field, so a
  /// caller blocked behind a long operation -- or a <c>status</c> call answering during one -- can
  /// say how long it has been running.
  /// </summary>
  private long busySince;

  /// <summary>
  /// Gate depth, since a sequence's own operations re-enter it on the same thread: only the
  /// outermost entry stamps <see cref="busySince"/>, so a nested <see cref="Run{T}"/> cannot reset
  /// the age of the call the caller is actually waiting on.
  /// </summary>
  private int busyDepth;

  /// <summary>
  /// How long the operation in flight has held the debugger, or null when none does, for a caller
  /// already holding <see cref="stateGate"/>.
  /// </summary>
  private TimeSpan? BusyForHeld =>
    this.busySince is 0 ? null : Stopwatch.GetElapsedTime(this.busySince);

  /// <summary>
  /// <see cref="BusyForHeld"/>, taking the lock itself.
  /// </summary>
  private TimeSpan? BusyFor {
    get {
      lock (this.stateGate) {
        return this.BusyForHeld;
      }
    }
  }

  /// <summary>
  /// Runs <paramref name="body"/> holding <see cref="gate"/>, refusing rather than queueing
  /// forever when another operation will not let go (<see cref="gateWait"/>).
  /// </summary>
  private T Holding<T>(Func<T> body) {
    if (!this.gate.TryEnter(this.gateWait)) {
      // Named with its age: "still running" and "wedged" are the same wait from out here, and the
      // age is the only thing that separates them.
      var held = this.BusyFor ?? this.gateWait;

      throw new InvalidOperationException(
        $"another operation has held the debugger for {held.TotalSeconds:0}s and this call " +
        $"waited {this.gateWait.TotalSeconds:0}s for it; the game is frozen or busy for as long " +
        "as that lasts - detach is the way out and does not queue behind this, though it refuses " +
        "the first ask and names the age to come back at, since severing a live session costs " +
        "the game a resource it only reclaims on restart"
      );
    }

    return this.Held(body);
  }

  /// <summary>
  /// Runs <paramref name="body"/> on a <see cref="gate"/> the caller has just taken, stamping the
  /// age every reporting surface answers from and releasing the gate on the way out.
  /// Every entry goes through here, <see cref="Detach"/>'s included: an age the gate's holder never
  /// stamped reads as no holder at all, which tells <see cref="Break"/> nothing is worth rationing
  /// and tells a refused caller the holder is as old as their own wait.
  /// </summary>
  private T Held<T>(Func<T> body) {
    lock (this.stateGate) {
      if (this.busyDepth++ is 0) {
        this.busySince = Stopwatch.GetTimestamp();
      }
    }

    try {
      return body();
    }
    finally {
      lock (this.stateGate) {
        if (--this.busyDepth is 0) {
          this.busySince = 0;
        }
      }

      this.gate.Exit();
    }
  }

  /// <summary>
  /// <see cref="Holding{T}"/> for a body that answers nothing.
  /// </summary>
  private void Holding(Action body) {
    _ = this.Holding(() => {
        body();

        return 0;
      }
    );
  }

  /// <summary>
  /// Runs one operation inside a suspend window, attaching or reattaching as needed.
  /// </summary>
  public T Run<T>(Func<SdbContext, T> operation) {
    return this.Holding(() => {
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
          catch (Exception ex) when (UnitySession.CauseOf<InvokeNeverAnsweredException>(ex)
            is {} timeout) {
            // Dropped on purpose rather than dropped on us, which the generic message below would
            // tell the caller backwards; its own says what really happened and that the game may
            // still be running their call.
            try {
              this.LoseConnection();
            }
            catch (InvalidOperationException) {
              // It raises the same lost window this failure already names, and ends on advice --
              // redo the whole window -- that is the wrong move here: the call behind the window
              // may still be running in the game, so re-issuing it is what must NOT happen. Its
              // state clearing is what was wanted; the throw below carries the message.
            }

            throw new InvalidOperationException(timeout.Message, ex);
          }
          catch (Exception ex) when (UnitySession.IsDisconnect(ex)) {
            // Mid-operation disconnect: the operation may have partially applied in the debuggee,
            // so it is NOT retried; surface the loss instead (the closed socket resumed the game).
            try {
              this.LoseConnection();
            }
            catch (InvalidOperationException) {
              // Same reason as the clause above: its advice is to redo the whole window, which is
              // the one thing a half-applied operation must not have done to it. Its state
              // clearing is what was wanted; the throw below says what the caller has to act on.
            }

            throw new InvalidOperationException(
              "the debugger connection dropped mid-operation; the game resumed and any suspend " +
              "window went with it, and the operation may have partially applied - verify its " +
              "effect before redoing it",
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
    );
  }

  /// <summary>
  /// Runs a SEQUENCE of operations as one, so nothing else touches the session between its steps.
  /// <see cref="Run{T}"/> serializes a single operation, which is enough for a tool that is one;
  /// a tool built from several is not covered by it, because the MCP host dispatches tool calls
  /// concurrently and every gap between two of its operations is a gap another call can take.
  /// The gaps that bite: <see cref="AdvanceHold"/> releases EVERY held suspension, so one
  /// sequence's window unfreezes the game inside another's, and <see cref="ResumeHold"/> from any
  /// caller drops a count a sequence in flight is standing on.
  /// Reporting stays live throughout (<see cref="Snapshot"/>, <see cref="DebugOrNull"/> and
  /// <see cref="HeldSuspendCount"/> read under <see cref="stateGate"/>), and the sequence's own
  /// operations re-enter the gate on this thread, which a <see cref="Lock"/> permits.
  /// </summary>
  public T Exclusive<T>(Func<T> sequence) => this.Holding(sequence);

  /// <summary>
  /// Holds one extra suspension across operations; returns the held count. The game is fully
  /// frozen until <see cref="ResumeHold"/> releases it (or the session detaches).
  /// </summary>
  public int SuspendHold() => this.SuspendHold(reported: true);

  /// <summary>
  /// <see cref="SuspendHold()"/>, taken unreported when a sequence needs a hold of its own: the
  /// game freezes the same way, and the count the status tools publish stays the caller's.
  /// </summary>
  public int SuspendHold(bool reported) {
    return this.Run(ctx => {
        ctx.Vm.Suspend();

        lock (this.stateGate) {
          if (!reported) {
            this.unreportedHolds++;
          }

          this.heldSuspends++;

          return this.ReportedHolds;
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
  public void RequireHold() => this.Holding(this.RequireHoldHeld);

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
  public int ResumeHold() => this.ResumeHold(reported: true);

  /// <summary>
  /// <see cref="ResumeHold()"/>, giving back a hold taken through
  /// <see cref="SuspendHold(bool)"/> unreported.
  /// </summary>
  public int ResumeHold(bool reported) {
    return this.Holding(() => {
        this.RequireHoldHeld();

        try {
          this.session.Vm.Resume();
        }
        catch (Exception e) when (UnitySession.IsDisconnect(e)) {
          this.LoseConnection();
        }

        lock (this.stateGate) {
          if (!reported) {
            this.unreportedHolds--;
          }

          this.heldSuspends--;

          return this.ReportedHolds;
        }
      }
    );
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

    return this.Holding(() => {
        this.Discard();

        try {
          this.EnsureAttached(chosen);
        }
        catch (Exception ex) when (chosen is {} endpoint) {
          // Restates the failure as what it is: the caller chose that port, so the fix is a
          // different port rather than a look at the game or its beacon.
          throw new InvalidOperationException(
            $"could not attach to port {endpoint.Port}, the port given to the attach tool " +
            $"(attach with no port to use the beacon): {ex.Message}",
            ex
          );
        }

        return this.Snapshot();
      }
    );
  }

  /// <summary>
  /// Resumes everything and detaches, freeing the exclusive debugger slot (e.g., for an IDE).
  /// Returns false when there was no live attach; a session whose peer already died is cleared the
  /// same way and reported as the nothing it was.
  /// </summary>
  public bool Detach() {
    // An operation that will not let go is BROKEN rather than waited out. This is the escape
    // hatch, and one that queues behind the wedge it exists to clear is no hatch at all: a stalled
    // wire command would otherwise keep the game frozen and the debugger slot taken until the
    // server itself is restarted.
    if (!this.gate.TryEnter(this.detachWait)) {
      return this.Break();
    }

    return this.Held(() => {
        if (this.session is null) {
          return false;
        }

        var wasAlive = this.session.IsAlive;

        this.Discard();

        return wasAlive;
      }
    );
  }

  /// <summary>
  /// Frees the debugger WITHOUT the gate, by closing the transport under the operation holding it:
  /// the game resumes on the closed socket, and that operation's wire waits fail as the dropped
  /// connection they now are.
  /// It does not necessarily let go at once. A plain command waiting on a reply fails the moment
  /// the socket closes, but an INVOKE the debuggee has already been handed completes from its reply
  /// and from nothing else, so one in flight keeps the gate until its own
  /// <c>Invoker.InvokeWait</c> expires. What the break guarantees is an END to the wait, not an
  /// immediate one.
  /// Nothing here clears the session's fields; the operation that owns them does, on its way out.
  /// That is what keeps <see cref="gate"/> sufficient for the callers that read those fields
  /// holding it alone -- a write from out here would race every one of them, and the first thing
  /// lost would be the warning owed to a caller whose suspend window this just ended.
  /// It refuses an operation still inside its own bounds, because breaking one is NOT free: the
  /// close carries no protocol goodbye, and a debuggee that does not notice a client leaving keeps
  /// the socket -- a cost it never reclaims and that eventually stops it accepting any debugger at
  /// all. Most wire waits are bounded now, so a session worth breaking usually announces itself by
  /// outliving those bounds.
  /// Not all of them are, which is why this exists rather than being a formality: the vendored
  /// client's ASYNC reply path is untimed and unguarded, so a frame refresh -- which follows every
  /// invoke -- can wait on a reply it will never parse. That wait watches the disconnected event,
  /// so closing the transport is exactly what ends it, and severing is the ONLY thing that does.
  /// </summary>
  private bool Break() {
    SdbSession held;
    TimeSpan? busy;

    lock (this.stateGate) {
      held = this.session;
      busy = this.BusyForHeld;
    }

    if (held is null) {
      return false;
    }

    var wasAlive = held.IsAlive;

    // A dead connection has nothing left to spend, so it is always freed.
    // An age of null is a holder that has not stamped one yet, which is the youngest an operation
    // gets rather than the oldest: the nullable comparison alone would read it as past every
    // threshold and sever on the first ask.
    if (wasAlive && (busy ?? TimeSpan.Zero) < this.wedgeAfter) {
      throw new InvalidOperationException(
        $"an operation has held the debugger for {busy?.TotalSeconds ?? 0:0}s and nearly every " +
        "wait it can be inside is bounded, so it will most likely end by itself; severing it " +
        "costs the game a resource it only reclaims on restart, so detach again once it has held " +
        $"for {this.wedgeAfter.TotalSeconds:0}s to sever it anyway"
      );
    }

    held.Abort();

    // Reported like the orderly path's: a session whose peer was already gone is the nothing it
    // was, however it got freed.
    return wasAlive;
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
  /// Every suspension held across operations (the unified-pause fallback), whoever took it: the
  /// physical question of whether the game is frozen, which is what a guard deciding whether a
  /// resume can move it or a frame can be read has to ask.
  /// <see cref="Snapshot"/> answers the other one, what the CALLER holds.
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
    return this.Holding(() => {
        this.RequireHoldHeld();

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
    );
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
        HeldSuspends = alive ? this.ReportedHolds : 0,

        // Answered even while that operation holds the gate, which is the whole point: it is what
        // separates a call that is still working from one that will never come back.
        BusyFor = this.BusyForHeld
      };
    }
  }

  /// <summary>
  /// Shutdown's detach, which cannot be refused: the ration exists so an agent asks twice, and
  /// there is nobody to ask here. A session left attached is freed by the process exiting -- the
  /// OS closes the socket exactly as <see cref="Break"/> would have -- so the refusal is dropped
  /// rather than raised out of container disposal, where it would strand every singleton behind
  /// this one.
  /// </summary>
  public void Dispose() {
    try {
      _ = this.Detach();
    }
    catch (InvalidOperationException) {
      // The operation holding the gate is younger than the sever threshold; see above.
    }
  }

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

    // The group alone: the listener binds every port it is served on, so naming one would send the
    // reader checking a port no more answerable than the rest.
    const string group = BeaconListener.MulticastGroup;

    var listening = beacons.Listening;
    var fault = beacons.Fault;
    var why = fault is null ? "" : $" ({fault})";

    string reason;

    // Three failures the caller acts on differently: a beacon without [Debug] 1 means the game IS
    // running and only the launch option is missing; nothing left listening means nothing about
    // any game is knowable here, so blaming launch options would send the caller nowhere; and a
    // listen still up with no beacon on it is the launch question.
    if (beacon is not null) {
      reason = $"the Unity game advertising itself on {group} ({beacon.Id}) reports no managed " +
        "debugger; relaunch it with 'player-connection-debug=1'";
    }
    else if (!listening) {
      reason = "nothing on this machine is listening for the PlayerConnection beacon" +
        $"{why}, so no game can be discovered; give the game's debugger port to the attach tool";
    }
    else {
      // A listener that lost only part of itself can still discover a game, so its fault annotates
      // the launch question rather than replacing it: that question is the answer in the common
      // case, and gating on the fault would let one dead idle port bury it for the process's life.
      var lost = fault is null
        ? ""
        : $" Part of the listen is lost{why}, so a running game can also go undiscovered.";

      reason = $"no Unity game is advertising itself on the PlayerConnection beacon ({group}); " +
        "is the game running as a development Mono build launched with " +
        $"'player-connection-debug=1'?{lost} If you know its debugger port, give it to the " +
        "attach tool";
    }

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
        this.unreportedHolds = 0;
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

    // Only a window the caller opened is one they can redo, and a sequence's own hold is not:
    // sending them to re-suspend a window they never took is the same falsehood Snapshot avoids.
    var wasTheirs = this.heldSuspends > this.unreportedHolds;

    this.Discard();

    if (hadHold) {
      throw new InvalidOperationException(
        "the debugger connection dropped while a suspension was held; the game resumed and the " +
        (wasTheirs
          ? "hold was lost - re-suspend and redo the whole window"
          : "tool's own hold " +
          "went with it, so nothing you were holding was lost")
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
  /// <remarks>
  /// <see cref="ObjectDisposedException"/> is in the list because <see cref="Break"/> closes the
  /// transport under a live operation, and the vendored client's SEND path checks nothing before
  /// writing (its own FIXMEs say so): the holder's next command reaches a disposed socket and says
  /// so in those words rather than the wire's. Left out, a severed operation reports "cannot access
  /// a disposed object" and keeps its attach on record, which is the wedge the sever exists to end.
  /// </remarks>
  internal static bool IsDisconnect(Exception e) =>
    e is VMDisconnectedException or IOException or SocketException or ObjectDisposedException ||
    (e.InnerException is not null && UnitySession.IsDisconnect(e.InnerException));

  /// <summary>
  /// The <typeparamref name="T"/> a failure was caused by, or null when none of them was.
  /// Walked rather than caught by type, because an operation body is where tools dress a failure in
  /// their own exception: by the time one leaves that body the thrown type is the dressing, and a
  /// clause naming the type that matters would never run.
  /// </summary>
  internal static T CauseOf<T>(Exception e)
    where T : Exception =>
    e as T ?? (e.InnerException is null ? null : UnitySession.CauseOf<T>(e.InnerException));
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

  /// <summary>
  /// How long the operation in flight has held the debugger, null when none does. A call blocked
  /// on this session reads it to tell a slow operation from a wedged one.
  /// </summary>
  public TimeSpan? BusyFor { get; init; }
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
