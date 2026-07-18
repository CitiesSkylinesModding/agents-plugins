using System.Diagnostics;
using System.Globalization;

namespace UnityDevtools.Sdb;

/// <summary>
/// Locates running dev-Mono Unity game processes and their Mono Soft Debugger listen port, so a
/// caller can discover where to attach without touching the port first.
/// Windows-only: it maps listen ports to a PID by parsing <c>netstat</c> (the lightest route with
/// no P/Invoke). The SDB agent binds a dynamic port; there is no fixed formula, only the range.
/// </summary>
public static class SdbDiscovery {
  // The dev-Mono agent picks its debugger port dynamically from this range; scan it to find the
  // live one. The range is deliberately generous because the port drifts between runs, and
  // PickSdbPort falls back past PortRangeEnd if the agent ever binds higher.
  public const int PortRangeStart = 56000;

  public const int PortRangeEnd = 56999;

  /// <summary>
  /// Locates running dev-Mono Unity game processes and, for each, its listen ports and best guess
  /// at the SDB port.
  /// With a <paramref name="processNamePrefix" />, returns every process whose name starts with it
  /// (case-insensitive), whether it exposes a port or not, so a caller can tell "not running" from
  /// "running but not a dev/Mono build".
  /// With no prefix, returns every process that exposes a plausible SDB port: generic
  /// auto-discovery by port signature, with no assumption about the game's name.
  /// </summary>
  public static IReadOnlyList<GameProcess> Locate(string processNamePrefix = null) {
    var hasFilter = !string.IsNullOrEmpty(processNamePrefix);

    // One netstat pass maps every listening port to its owning PID, so unfiltered discovery does
    // not spawn a netstat per process on the machine.
    var portsByPid = SdbDiscovery.ListenPortsByPid();

    var found = new List<GameProcess>();

    foreach (var process in Process.GetProcesses()) {
      if (hasFilter &&
        !process.ProcessName.StartsWith(processNamePrefix, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      var ports = portsByPid.TryGetValue(process.Id, out var owned) ? owned : [];
      var sdbPort = SdbDiscovery.PickSdbPort(ports);

      // Without a name filter, discovery is purely by port signature: skip anything with no SDB
      // port, so the result is real dev-Mono candidates, not every process on the machine.
      if (!hasFilter && sdbPort is null) {
        continue;
      }

      found.Add(
        new GameProcess {
          Name = process.ProcessName,
          Pid = process.Id,
          ListeningPorts = ports.OrderBy(x => x).ToArray(),
          SdbPort = sdbPort
        }
      );
    }

    return found;
  }

  /// <summary>
  /// Picks the SDB port from a process's listen ports: the lowest port inside the nominal range,
  /// or, if none lands there, the highest port at or above <see cref="PortRangeStart" /> as a
  /// drift fallback (the fixed PlayerConnection and Gameface-CDP ports sit well below it, so this
  /// keeps discovery working even when the agent binds past <see cref="PortRangeEnd" />).
  /// Null when the process exposes no plausible SDB port.
  /// </summary>
  private static int? PickSdbPort(List<int> ports) {
    var inRange = ports
      .Where(port => port is >= SdbDiscovery.PortRangeStart and <= SdbDiscovery.PortRangeEnd)
      .OrderBy(port => port)
      .ToList();

    if (inRange.Count > 0) {
      return inRange[0];
    }

    var aboveStart = ports.Where(port => port >= SdbDiscovery.PortRangeStart).ToList();

    return aboveStart.Count > 0 ? aboveStart.Max() : null;
  }

  private static Dictionary<int, List<int>> ListenPortsByPid() {
    var psi = new ProcessStartInfo("netstat", "-ano -p tcp") {
      RedirectStandardOutput = true,
      UseShellExecute = false
    };

    using var netstat = Process.Start(psi);

    var output = netstat!.StandardOutput.ReadToEnd();

    netstat.WaitForExit();

    var byPid = new Dictionary<int, List<int>>();

    foreach (var line in output.Split('\n')) {
      var cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

      // TCP <local> <remote> LISTENING <pid>
      if (cols.Length < 5 || cols[0] != "TCP" || cols[3] != "LISTENING") {
        continue;
      }

      var pidParsed = int.TryParse(
        cols[4].Trim(),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var pid
      );

      if (!pidParsed) {
        continue;
      }

      var local = cols[1];
      var idx = local.LastIndexOf(':');

      if (idx <= 0 || !int.TryParse(local[(idx + 1)..], out var port)) {
        continue;
      }

      if (!byPid.TryGetValue(pid, out var ports)) {
        ports = [];
        byPid[pid] = ports;
      }

      if (!ports.Contains(port)) {
        ports.Add(port);
      }
    }

    return byPid;
  }
}

/// <summary>
/// A located game process, its listen ports, and its SDB port when one is in range.
/// </summary>
public sealed class GameProcess {
  public string Name { get; init; }

  public int Pid { get; init; }

  public IReadOnlyList<int> ListeningPorts { get; init; }

  /// <summary>
  /// The best guess at the SDB port to attach to (see <see cref="SdbDiscovery.Locate" />), or null
  /// when the process exposes no plausible SDB port (not a dev/Mono build?).
  /// </summary>
  public int? SdbPort { get; init; }
}
