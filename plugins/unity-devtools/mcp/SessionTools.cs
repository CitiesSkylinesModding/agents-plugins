using System.ComponentModel;
using JetBrains.Annotations;
using ModelContextProtocol.Server;
using UnityDevtools.Sdb;

namespace UnityDevtools.Mcp;

/// <summary>
/// Session lifecycle tools over the shared <see cref="UnitySession"/>: discovery/state reporting,
/// attaching to a port the beacon does not give, held suspend windows, and freeing the exclusive
/// debugger slot.
/// Attaching is otherwise lazy: any tool that needs the VM attaches (and reattaches) on demand.
/// </summary>
[McpServerToolType]
[UsedImplicitly]
public sealed class SessionTools(UnitySession session, BeaconListener beacons) {
  [McpServerTool(Name = "status")]
  [Description(
    """
    Report what the running Unity game advertises on the PlayerConnection beacon, the Mono Soft
    Debugger endpoint that would be attached to, and the current session state. Never attaches, so
    it is safe while an IDE holds the debugger slot.
    A beacon with debuggerEnabled false means the game IS running but was launched without
    'player-connection-debug=1'; no beacon at all means no game is advertising itself, unless
    beaconFault or beaconListening says the listen itself is impaired.
    No beacon while session.heldSuspends is above zero means neither: a suspended game stops
    broadcasting, so this session froze the very thing it is reporting on. Resume and ask again.
    Other tools attach lazily on first use, so a session needs no explicit attach step.
    """
  )]
  [UsedImplicitly]
  public StatusResult Status() {
    return ToolGuard.Run(Operation);

    StatusResult Operation() {
      var beacon = beacons.Wait();

      return new StatusResult {
        Beacon = beacon is null
          ? null
          : new BeaconInfo {
            Host = beacon.Host,
            SdbPort = beacon.SdbPort,
            DebuggerEnabled = beacon.DebuggerEnabled,
            ConnectionGuid = beacon.ConnectionGuid,
            PlayerConnectionPort = beacon.PlayerConnectionPort,
            Id = beacon.Id,
            PackageName = beacon.PackageName,
            ProjectName = beacon.ProjectName
          },
        Endpoint = beacon?.Endpoint is {} endpoint ? $"{endpoint.Host}:{endpoint.Port}" : null,
        BeaconListening = beacons.Listening,
        BeaconFault = beacons.Fault,
        Session = SessionTools.Describe(session.Snapshot())
      };
    }
  }

  [McpServerTool(Name = "attach")]
  [Description(
    """
    Attach to a Mono Soft Debugger port on this machine, replacing any live session and connecting
    straight away.
    Reach for it when status reports no beacon while the game is running (multicast filtered by a
    firewall or a VPN interface, or a beaconFault), or when an external loader started the debug
    server on a port of its own.
    The port applies to this attach alone: a later reattach, the one after a dropped connection
    included, goes back to the beacon, so give the port again if the beacon still cannot see the
    game.
    """
  )]
  [UsedImplicitly]
  public SessionInfo Attach(
    [Description(
      "The game's Mono Soft Debugger port on this machine; omit it to attach to what the beacon " +
      "advertises."
    )]
    int? port = null
  ) {
    return ToolGuard.Run(() => SessionTools.Describe(session.Attach(port)));
  }

  [McpServerTool(Name = "detach")]
  [Description(
    """
    Resume the game fully and detach the debugger session, freeing the single SDB debugger slot
    (e.g. so an IDE can attach).
    The next tool that needs the VM reattaches automatically.
    """
  )]
  [UsedImplicitly]
  public DetachResult Detach() {
    return ToolGuard.Run(() => new DetachResult {
        WasAttached = session.Detach()
      }
    );
  }

  [McpServerTool(Name = "suspend")]
  [Description(
    """
    Hold the game fully frozen (simulation AND rendering) across subsequent tool calls, opening a
    consistency window for multi-step reads/writes. Without a hold, each operation opens and closes
    a brief window of its own, so the game runs on between calls.
    Suspensions are counted; call resume once per suspend.
    Detaching or a dropped connection always resumes the game.
    """
  )]
  [UsedImplicitly]
  public SuspendResult Suspend() {
    return ToolGuard.Run(() => new SuspendResult {
        HeldSuspends = session.SuspendHold()
      }
    );
  }

  [McpServerTool(Name = "resume")]
  [Description("Release one held suspension (see the suspend tool); the game runs again at zero.")]
  [UsedImplicitly]
  public SuspendResult Resume() {
    return ToolGuard.Run(() => new SuspendResult {
        HeldSuspends = session.ResumeHold()
      }
    );
  }

  private static SessionInfo Describe(UnitySessionSnapshot snapshot) =>
    new() {
      Attached = snapshot.Attached,
      Host = snapshot.Host,
      Port = snapshot.Port,
      VmVersion = snapshot.VmVersion,
      Protocol = snapshot.Protocol,
      HeldSuspends = snapshot.HeldSuspends
    };
}

/// <summary>Result of the <c>status</c> tool: what was discovered plus the session state.</summary>
public sealed record StatusResult {
  /// <summary>
  /// What is being advertised right now, or null when nothing is. A game broadcasts about once a
  /// second and cannot announce that it stopped, so its beacon expires shortly after it exits
  /// rather than lingering as a description of a game that is gone.
  /// </summary>
  public required BeaconInfo? Beacon { [UsedImplicitly] get; init; }

  /// <summary>
  /// The <c>host:port</c> the beacon points at, or null when nothing attachable is advertising
  /// itself.
  /// </summary>
  public required string? Endpoint { [UsedImplicitly] get; init; }

  /// <summary>
  /// Which part of the beacon listen was lost and why, or null while all of it is up. Set, a
  /// running game can go undiscovered however it was launched, so read a missing beacon as
  /// inconclusive rather than as "no game", and reach for the attach tool's explicit port.
  /// </summary>
  public required string? BeaconFault { [UsedImplicitly] get; init; }

  /// <summary>
  /// Whether any part of the listen is still up. False and a missing beacon says nothing at all
  /// about whether a game is running: only an explicit port reaches one from here.
  /// </summary>
  public required bool BeaconListening { [UsedImplicitly] get; init; }

  public required SessionInfo Session { [UsedImplicitly] get; init; }
}

/// <summary>What a Unity player advertised about itself on the PlayerConnection beacon.</summary>
public sealed record BeaconInfo {
  public required string Host { [UsedImplicitly] get; init; }

  /// <summary>The debugger port this player is reachable on.</summary>
  public required int SdbPort { [UsedImplicitly] get; init; }

  /// <summary>
  /// Whether the player reports the managed debugger as enabled; false means the game runs without
  /// 'player-connection-debug=1' and cannot be attached to.
  /// </summary>
  public required bool DebuggerEnabled { [UsedImplicitly] get; init; }

  public required uint ConnectionGuid { [UsedImplicitly] get; init; }

  /// <summary>The PlayerConnection/profiler port, which is NOT the debugger port.</summary>
  public required int? PlayerConnectionPort { [UsedImplicitly] get; init; }

  public required string? Id { [UsedImplicitly] get; init; }

  public required string? PackageName { [UsedImplicitly] get; init; }

  public required string? ProjectName { [UsedImplicitly] get; init; }
}

/// <summary>The persistent session's current state.</summary>
public sealed record SessionInfo {
  /// <summary>
  /// Whether the debugger connection is up, read from the connection rather than remembered from
  /// the attach: a game that exited while idle reports false here.
  /// </summary>
  public required bool Attached { [UsedImplicitly] get; init; }

  public required string? Host { [UsedImplicitly] get; init; }

  public required int? Port { [UsedImplicitly] get; init; }

  /// <summary>The debuggee's Mono VM version, when attached.</summary>
  public required string? VmVersion { [UsedImplicitly] get; init; }

  /// <summary>The negotiated SDB protocol, when attached (generic invokes need 2.24+).</summary>
  public required string? Protocol { [UsedImplicitly] get; init; }

  /// <summary>Suspensions currently held via the suspend tool (game frozen while &gt; 0).</summary>
  public required int HeldSuspends { [UsedImplicitly] get; init; }
}

/// <summary>Result of the <c>detach</c> tool.</summary>
public sealed record DetachResult {
  public required bool WasAttached { [UsedImplicitly] get; init; }
}

/// <summary>
/// Result of the <c>suspend</c>/<c>resume</c> tools: the held count after the call.
/// </summary>
public sealed record SuspendResult {
  public required int HeldSuspends { [UsedImplicitly] get; init; }
}
