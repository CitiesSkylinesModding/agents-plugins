using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;

namespace UnityDevtools.Mcp;

/// <summary>
/// Shuts the server down when the client's end of the stdin pipe closes, independently of the MCP
/// transport.
/// The transport does honor stdin EOF, but only between requests: a tool call blocked on an SDB
/// wire operation (a wedged debuggee) keeps the server's run loop from ever completing, so the
/// SDK-driven shutdown never starts and the stranded server would outlive its client forever.
/// Watching the pipe with <c>PeekNamedPipe</c> observes closure without consuming bytes, so it
/// never races the transport's reads.
/// Windows-only (like <see cref="ParentWatchdog"/>); elsewhere the transport's own EOF handling
/// is the only stdin tie.
/// </summary>
internal sealed class StdinWatchdog(IHostApplicationLifetime lifetime) : BackgroundService {
  private const int StdInputHandle = -10;

  private const int FileTypePipe = 3;

  private const int ErrorBrokenPipe = 109;

  private const int ErrorPipeNotConnected = 233;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    var stdin = StdinWatchdog.GetStdHandle(StdinWatchdog.StdInputHandle);

    // Only a pipe can be watched this way; a console or file stdin (interactive/manual runs) gets
    // no watchdog rather than false shutdowns.
    if (stdin is 0 or -1 || StdinWatchdog.GetFileType(stdin) is not StdinWatchdog.FileTypePipe) {
      return;
    }

    while (true) {
      if (!StdinWatchdog.PeekNamedPipe(stdin, 0, 0, 0, out _, 0)) {
        var error = Marshal.GetLastWin32Error();

        if (error is StdinWatchdog.ErrorBrokenPipe or StdinWatchdog.ErrorPipeNotConnected) {
          break;
        }

        // Any other failure means the pipe cannot be judged; degrade to no watchdog rather than
        // ever shutting a healthy server down.
        await Console.Error.WriteLineAsync($"stdin watchdog disabled: Win32 error {error}");

        return;
      }

      try {
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
      }
      catch (OperationCanceledException) {
        // The host is stopping on its own; nothing left to watch.
        return;
      }
    }

    // Arm first, stop second, log last: graceful shutdown can stall in SDB disposal (with a
    // wedged in-flight tool call it may not even start), and a stderr write can block forever
    // when the client stops draining the pipe, so nothing blockable may precede the failsafe or
    // the stop request.
    HardExit.Arm();

    lifetime.StopApplication();

    // stderr only, best-effort: stdout carries the MCP protocol.
    await Console.Error.WriteLineAsync("stdin closed by the client; shutting down");
  }

  [DllImport("kernel32.dll")]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  private static extern nint GetStdHandle(int handle);

  [DllImport("kernel32.dll")]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  private static extern int GetFileType(nint handle);

  [DllImport("kernel32.dll", SetLastError = true)]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  private static extern bool PeekNamedPipe(
    nint handle,
    nint buffer,
    int bufferSize,
    nint bytesRead,
    out int totalBytesAvailable,
    nint bytesLeftThisMessage
  );
}
