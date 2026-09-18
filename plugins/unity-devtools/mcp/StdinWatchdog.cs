using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace UnityDevtools.Mcp;

/// <summary>
/// Shuts the server down when the client's end of the stdin pipe closes, independently of the MCP
/// transport.
/// The transport does honor stdin EOF, but only between requests: a tool call blocked on an SDB
/// wire operation (a wedged debuggee) keeps the server's run loop from ever completing, so the
/// SDK-driven shutdown never starts and the stranded server would outlive its client forever.
/// Both platforms observe closure WITHOUT consuming bytes -- <c>PeekNamedPipe</c> on Windows,
/// <c>poll</c>'s POLLHUP on Unix -- so neither ever races the transport's reads.
/// </summary>
internal sealed class StdinWatchdog(IHostApplicationLifetime lifetime)
  : LifetimeWatchdog(lifetime) {
  private const int StdInputHandle = -10;

  private const int FileTypePipe = 3;

  private const int ErrorBrokenPipe = 109;

  private const int ErrorPipeNotConnected = 233;

  /// <summary>Every writer has closed: the Unix name for a broken pipe.</summary>
  private const short PollHup = 0x010;

  /// <summary>The descriptor is not open at all, which is the same news for this watch.</summary>
  private const short PollNval = 0x020;

  protected override string Severed => "stdin closed by the client";

  protected override Task<bool> Watch(CancellationToken stoppingToken) =>
    OperatingSystem.IsWindows()
      ? StdinWatchdog.WatchOnWindows(stoppingToken)
      : StdinWatchdog.WatchOnUnix(stoppingToken);

  /// <summary>
  /// Watches the pipe without consuming from it, which <c>PeekNamedPipe</c> reports as a broken
  /// pipe once the client's end closes.
  /// </summary>
  private static async Task<bool> WatchOnWindows(CancellationToken stoppingToken) {
    var stdin = StdinWatchdog.GetStdHandle(StdinWatchdog.StdInputHandle);

    // Only a pipe can be watched this way; a console or file stdin (interactive/manual runs) gets
    // no watchdog rather than false shutdowns.
    if (stdin is 0 or -1 || StdinWatchdog.GetFileType(stdin) is not StdinWatchdog.FileTypePipe) {
      return false;
    }

    while (true) {
      if (!StdinWatchdog.PeekNamedPipe(stdin, 0, 0, 0, out _, 0)) {
        var error = Marshal.GetLastWin32Error();

        if (error is StdinWatchdog.ErrorBrokenPipe or StdinWatchdog.ErrorPipeNotConnected) {
          return true;
        }

        // Any other failure means the pipe cannot be judged; degrade to no watchdog rather than
        // ever shutting a healthy server down.
        await Console.Error.WriteLineAsync($"stdin watchdog disabled: Win32 error {error}");

        return false;
      }

      if (!await LifetimeWatchdog.Naps(stoppingToken)) {
        return false;
      }
    }
  }

  /// <summary>
  /// <see cref="WatchOnWindows"/> over <c>poll</c>, which reports POLLHUP once every writer has
  /// closed whether or not bytes are still buffered -- so a closure is seen even while the
  /// transport still has reading to do.
  /// A terminal or file stdin never raises it, which is the same "no watchdog rather than false
  /// shutdowns" the Windows side gets from its pipe check.
  /// </summary>
  private static async Task<bool> WatchOnUnix(CancellationToken stoppingToken) {
    while (true) {
      // All zero, and each zero deliberate: stdin is descriptor 0, nothing is being waited FOR
      // (POLLHUP and POLLNVAL are reported regardless of what was requested), and a zero timeout
      // keeps the poll from parking a thread pool thread.
      var watched = default(PollDescriptor);

      if (StdinWatchdog.Poll(ref watched, 1, 0) < 0) {
        await Console.Error.WriteLineAsync(
          $"stdin watchdog disabled: poll errno {Marshal.GetLastWin32Error()}"
        );

        return false;
      }

      if ((watched.Reported & (StdinWatchdog.PollHup | StdinWatchdog.PollNval)) is not 0) {
        return true;
      }

      if (!await LifetimeWatchdog.Naps(stoppingToken)) {
        return false;
      }
    }
  }

  [DllImport("kernel32.dll")]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  private static extern nint GetStdHandle(int handle);

  [DllImport("kernel32.dll")]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  private static extern int GetFileType(nint handle);

  /// <summary>
  /// One <c>struct pollfd</c>; only stdin is ever watched, so one is all there is.
  /// </summary>
  [StructLayout(LayoutKind.Sequential)]
  private struct PollDescriptor {
    public int Descriptor;

    public short Events;

    public short Reported;
  }

  [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
  private static extern int Poll(ref PollDescriptor descriptors, nuint count, int timeoutMs);

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
