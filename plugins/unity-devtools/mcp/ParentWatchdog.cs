using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;

namespace UnityDevtools.Mcp;

/// <summary>
/// Shuts the server down when its launching wrapper dies.
/// Harnesses run this server through a wrapper (<c>dotnet dnx</c> for installs, <c>dotnet run</c>
/// for local dev), and killing the wrapper does not reliably kill the server: an MCP reconnect can
/// strand the previous instance, still holding the exclusive SDB debugger slot (and, in dev, file
/// locks on its build output).
/// Watching the parent process ties this server's lifetime to its own wrapper without ever touching
/// another process, so concurrent servers (two games, two harnesses) stay unaffected.
/// </summary>
internal sealed class ParentWatchdog(IHostApplicationLifetime lifetime) : BackgroundService {
  protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
    using var self = Process.GetCurrentProcess();

    // An unknown parent (non-Windows, or a failed pid query) must NOT shut a healthy server down;
    // the stdio transport's own shutdown is the only tie then.
    if (ParentWatchdog.ParentPid(self) is not {} parentPid) {
      return;
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
      return;
    }
    catch (ArgumentException) {
      // The parent already exited: this server is stale right from startup; shut down below.
    }
    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) {
      // The parent cannot be read or watched (access denied, exit race): degrade to no watchdog
      // rather than ever shutting a healthy server down. Letting the exception escape would stop
      // the whole host (BackgroundServiceExceptionBehavior.StopHost).
      await Console.Error.WriteLineAsync($"parent watchdog disabled: {ex.Message}");

      return;
    }

    // stderr only: stdout carries the MCP protocol.
    await Console.Error.WriteLineAsync("launching wrapper exited; shutting down");

    lifetime.StopApplication();

    // Failsafe: if graceful shutdown stalls, the survivor would keep the exclusive SDB slot, the
    // very situation this watchdog exists to prevent. A hard exit is safe for the game: a dying
    // socket auto-resumes the VM (verified safety net).
    _ = Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None)
      .ContinueWith(
        _ => Environment.Exit(1),
        CancellationToken.None,
        TaskContinuationOptions.None,
        TaskScheduler.Default
      );
  }

  private static int? ParentPid(Process self) {
    if (!OperatingSystem.IsWindows()) {
      return null;
    }

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
