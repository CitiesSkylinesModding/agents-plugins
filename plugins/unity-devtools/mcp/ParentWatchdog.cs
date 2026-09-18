using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace UnityDevtools.Mcp;

/// <summary>
/// Shuts the server down when its launching wrapper dies.
/// Harnesses run this server through a wrapper (<c>dotnet dnx</c> for installs, <c>dotnet run</c>
/// for local dev); not every wrapper shape guarantees the OS takes the server down with it, so
/// watching the parent process is one of the independent lifetime ties (with
/// <see cref="StdinWatchdog"/>) that keep an MCP reconnect from stranding the previous instance,
/// still holding the exclusive SDB debugger slot (and, in dev, file locks on its build output).
/// It never touches another process, so concurrent servers (two games, two harnesses) stay
/// unaffected.
/// Each platform answers "is the wrapper still there" its own way -- Windows waits on the process,
/// Unix watches for the reparenting that follows its death -- because a tie that exists on one of
/// them is no tie at all on the other, where killing the wrapper leaves stdin held open by the
/// client and the server with nothing to notice.
/// </summary>
internal sealed class ParentWatchdog(IHostApplicationLifetime lifetime)
  : LifetimeWatchdog(lifetime) {
  protected override string Severed => "launching wrapper exited";

  protected override Task<bool> Watch(CancellationToken stoppingToken) =>
    OperatingSystem.IsWindows()
      ? ParentWatchdog.WatchOnWindows(stoppingToken)
      : ParentWatchdog.WatchOnUnix(stoppingToken);

  /// <summary>
  /// Waits on the parent process itself, which Windows lets a child do directly.
  /// </summary>
  private static async Task<bool> WatchOnWindows(CancellationToken stoppingToken) {
    using var self = Process.GetCurrentProcess();

    // A failed pid query must NOT shut a healthy server down; the stdio transport's own shutdown
    // is the only tie then.
    if (ParentWatchdog.ParentPid(self) is not {} parentPid) {
      return false;
    }

    try {
      using var parent = Process.GetProcessById(parentPid);

      // A live parent always predates its child; a younger process means the pid was recycled after
      // the wrapper died (fall through to shut down).
      if (parent.StartTime <= self.StartTime) {
        await parent.WaitForExitAsync(stoppingToken);
      }
    }
    catch (OperationCanceledException) {
      // The host is stopping on its own; nothing left to watch.
      return false;
    }
    catch (ArgumentException) {
      // The parent already exited: this server is stale right from startup; shut down below.
    }
    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) {
      // The parent cannot be read or watched (access denied, exit race): degrade to no watchdog
      // rather than ever shutting a healthy server down. Letting the exception escape would stop
      // the whole host (BackgroundServiceExceptionBehavior.StopHost).
      await Console.Error.WriteLineAsync($"parent watchdog disabled: {ex.Message}");

      return false;
    }

    return true;
  }

  /// <summary>
  /// Watches the parent pid instead of the parent process: a Unix child is REPARENTED when its
  /// parent dies, so <c>getppid</c> changing is the event, and it needs no permission, no handle
  /// and no /proc.
  /// Only a change counts. A server that starts out reparented already cannot tell an orphan from
  /// a launcher that happens to be the init process, and a watchdog that guessed there would shut
  /// healthy servers down inside containers.
  /// </summary>
  private static async Task<bool> WatchOnUnix(CancellationToken stoppingToken) {
    var launcher = ParentWatchdog.GetParentPid();

    while (await LifetimeWatchdog.Naps(stoppingToken)) {
      if (ParentWatchdog.GetParentPid() != launcher) {
        return true;
      }
    }

    return false;
  }

  [DllImport("libc", EntryPoint = "getppid")]
  private static extern int GetParentPid();

  /// <summary>
  /// The parent pid Windows records on the process, or null when the query fails.
  /// Windows-only: the Unix watch reads <c>getppid</c>, whose whole point is that it answers the
  /// question again later.
  /// </summary>
  private static int? ParentPid(Process self) {
    var info = default(ProcessBasicInformation);

    var status = ParentWatchdog.NtQueryInformationProcess(
      self.Handle,
      0,
      ref info,
      Marshal.SizeOf<ProcessBasicInformation>(),
      out _
    );

    return status is 0 ? checked((int) info.InheritedFromUniqueProcessId) : null;
  }

  [DllImport("ntdll.dll")]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  private static extern int NtQueryInformationProcess(
    IntPtr processHandle,
    int processInformationClass,
    ref ProcessBasicInformation processInformation,
    int processInformationLength,
    out int returnLength
  );

  /// <summary>PROCESS_BASIC_INFORMATION; only the parent pid field is consumed.</summary>
  [StructLayout(LayoutKind.Sequential)]
  private struct ProcessBasicInformation {
    public IntPtr Reserved1;

    public IntPtr PebBaseAddress;

    public IntPtr Reserved2First;

    public IntPtr Reserved2Second;

    public IntPtr UniqueProcessId;

    public IntPtr InheritedFromUniqueProcessId;
  }
}
