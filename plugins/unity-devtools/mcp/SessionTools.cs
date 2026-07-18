using System.ComponentModel;
using JetBrains.Annotations;
using ModelContextProtocol.Server;
using UnityDevtools.Sdb;

namespace UnityDevtools.Mcp;

/// <summary>
/// Session lifecycle tools over the shared <see cref="UnitySession"/>: discovery/state reporting,
/// held suspend windows, and freeing the exclusive debugger slot.
/// Attach itself is lazy: any tool that needs the VM attaches (and reattaches) on demand.
/// </summary>
[McpServerToolType]
[UsedImplicitly]
public sealed class SessionTools(UnitySession session) {
  [McpServerTool(Name = "status")]
  [Description(
    """
    Find running dev-Mono Unity game processes and their Mono Soft Debugger (SDB) port, without
    attaching, and report the current session state.
    With no processName, auto-discovers every process exposing an SDB port.
    Other tools attach lazily on first use, so no explicit attach step exists.
    """
  )]
  [UsedImplicitly]
  public StatusResult Status(
    [Description(
      """
      Optional process name prefix to match, case-insensitive.
      Omit to use the UNITY_MCP_PROCESS env filter if set, else auto-discover every running dev-Mono
      Unity game by its SDB port signature.
      """
    )]
    string? processName = null
  ) {
    return ToolGuard.Run(Operation);

    StatusResult Operation() {
      var query = processName ?? session.Config.ProcessNamePrefix;

      var processes = SdbDiscovery.Locate(query)
        .Select(process => new GameProcessInfo {
            Name = process.Name,
            Pid = process.Pid,
            ListeningPorts = process.ListeningPorts,
            SdbPort = process.SdbPort
          }
        )
        .ToArray();

      var snapshot = session.Snapshot();

      return new StatusResult {
        ProcessQuery = query,
        Processes = processes,
        Session = new SessionInfo {
          Attached = snapshot.Attached,
          Host = snapshot.Host,
          Port = snapshot.Port,
          VmVersion = snapshot.VmVersion,
          Protocol = snapshot.Protocol,
          HeldSuspends = snapshot.HeldSuspends
        }
      };
    }
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
    consistency window for multi-step reads/writes.
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
}

/// <summary>Result of the <c>status</c> tool: discovery matches plus the session state.</summary>
public sealed record StatusResult {
  /// <summary>The name-prefix filter applied, or null when discovery ran unfiltered.</summary>
  public required string? ProcessQuery { [UsedImplicitly] get; init; }

  public required IReadOnlyList<GameProcessInfo> Processes { [UsedImplicitly] get; init; }

  public required SessionInfo Session { [UsedImplicitly] get; init; }
}

/// <summary>One located game process, its listen ports, and its SDB port when in range.</summary>
public sealed record GameProcessInfo {
  public required string Name { [UsedImplicitly] get; init; }

  public required int Pid { [UsedImplicitly] get; init; }

  public required IReadOnlyList<int> ListeningPorts { [UsedImplicitly] get; init; }

  /// <summary>The SDB port to attach to, or null when none is in the dev-Mono range.</summary>
  public int? SdbPort { [UsedImplicitly] get; init; }
}

/// <summary>The persistent session's current state.</summary>
public sealed record SessionInfo {
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
