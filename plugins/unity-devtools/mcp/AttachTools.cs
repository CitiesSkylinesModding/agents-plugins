using System.ComponentModel;
using JetBrains.Annotations;
using ModelContextProtocol.Server;
using UnityDevtools.Sdb;

namespace UnityDevtools.Mcp;

/// <summary>
/// Attach-level MCP tools: enough to prove a C# MCP server can reach a running dev-Mono Unity game
/// over the Mono Soft Debugger protocol, and nothing more.
/// No inspection, ECS, or write commands live here on purpose (this is a walking skeleton).
/// </summary>
[McpServerToolType]
[UsedImplicitly]
public static class AttachTools {
  [McpServerTool(Name = "status")]
  [Description(
    """
    Find running dev-Mono Unity game processes and their Mono Soft Debugger (SDB) port, without
    attaching. With no processName, auto-discovers every process exposing an SDB port. Returns
    each matching process with its listen ports and the SDB port to pass to attach_check.
    """
  )]
  [UsedImplicitly]
  public static StatusResult Status(
    [Description(
      """
      Optional process name prefix to match, case-insensitive.
      Omit to auto-discover every running dev-Mono Unity game by its SDB port signature.
      """
    )]
    string? processName = null
  ) {
    var processes = SdbDiscovery.Locate(processName)
      .Select(process => new GameProcessInfo {
          Name = process.Name,
          Pid = process.Pid,
          ListeningPorts = process.ListeningPorts,
          SdbPort = process.SdbPort
        }
      )
      .ToArray();

    return new StatusResult {
      ProcessQuery = processName,
      Processes = processes
    };
  }

  [McpServerTool(Name = "attach_check")]
  [Description(
    """
    Attach to the game's SDB port, report VM/runtime info, then resume and detach.
    The game is briefly suspended during the attach and always resumed afterward, so it keeps
    running.
    """
  )]
  [UsedImplicitly]
  public static AttachCheckResult AttachCheck(
    [Description("SDB port to attach to (discover it with the status tool).")] int port,
    [Description("Debugger host (default 127.0.0.1).")] string host = "127.0.0.1"
  ) {
    // Disposing the session resumes every counted suspension and detaches, even on failure, so the
    // game is never left frozen.
    using var session = SdbSession.Connect(host, port);

    var vm = session.Vm;
    var version = vm.Version;
    var threads = vm.GetThreads();

    return new AttachCheckResult {
      Host = host,
      Port = port,
      VmVersion = version.VMVersion,
      Protocol = $"{version.MajorVersion}.{version.MinorVersion}",
      ThreadCount = threads.Count,
      Threads = threads.Take(8).Select(t => $"[{t.Id}] {t.Name}").ToArray()
    };
  }
}

/// <summary>Result of the <c>status</c> tool: the query and every matching game process.</summary>
public sealed record StatusResult {
  /// <summary>The name-prefix filter applied, or null when discovery ran unfiltered.</summary>
  public required string? ProcessQuery { [UsedImplicitly] get; init; }

  public required IReadOnlyList<GameProcessInfo> Processes { [UsedImplicitly] get; init; }
}

/// <summary>One located game process, its listen ports, and its SDB port when in range.</summary>
public sealed record GameProcessInfo {
  public required string Name { [UsedImplicitly] get; init; }

  public required int Pid { [UsedImplicitly] get; init; }

  public required IReadOnlyList<int> ListeningPorts { [UsedImplicitly] get; init; }

  /// <summary>The SDB port to attach to, or null when none is in the dev-Mono range.</summary>
  public int? SdbPort { [UsedImplicitly] get; init; }
}

/// <summary>
/// Result of the <c>attach_check</c> tool: VM/runtime info from a completed attach.
/// </summary>
public sealed record AttachCheckResult {
  public required string Host { [UsedImplicitly] get; init; }

  public required int Port { [UsedImplicitly] get; init; }

  public required string VmVersion { [UsedImplicitly] get; init; }

  public required string Protocol { [UsedImplicitly] get; init; }

  public required int ThreadCount { [UsedImplicitly] get; init; }

  /// <summary>The first threads (id and name), capped for brevity.</summary>
  public required IReadOnlyList<string> Threads { [UsedImplicitly] get; init; }
}
