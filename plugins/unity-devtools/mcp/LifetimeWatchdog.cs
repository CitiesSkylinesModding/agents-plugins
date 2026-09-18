using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace UnityDevtools.Mcp;

/// <summary>
/// A lifetime tie: one thing this server is tied to, watched until it is gone, and a shutdown when
/// it is.
/// The ties are independent by design -- each covers a way the other can be blind -- so a subclass
/// answers "is mine still there" alone and inherits the shutdown, whose ordering is the part that
/// must not drift between them.
/// </summary>
internal abstract class LifetimeWatchdog(IHostApplicationLifetime lifetime) : BackgroundService {
  /// <summary>
  /// How often a watch samples. The cost of a miss is a server holding the exclusive debugger
  /// slot, and the cost of the sample is one syscall.
  /// </summary>
  protected static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

  /// <summary>What the shutdown announces, in the voice of the tie that broke.</summary>
  protected abstract string Severed { get; }

  /// <summary>
  /// Whether the tie is gone. False means the watchdog stood down without judging anything --
  /// either the host is stopping on its own, or this platform cannot observe the tie, in which
  /// case no watchdog is the right answer rather than a false shutdown.
  /// </summary>
  protected abstract Task<bool> Watch(CancellationToken stoppingToken);

  protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
    bool severed;

    try {
      severed = await this.Watch(stoppingToken);
    }
    catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) {
      // The platform call the watch is built on is not there to make (a libc this runtime cannot
      // resolve under that name). No watchdog is the right answer, the same one every other
      // unobservable tie gets: letting it escape would stop the whole host
      // (BackgroundServiceExceptionBehavior.StopHost) over a server that is otherwise healthy.
      await Console.Error.WriteLineAsync($"{this.GetType().Name} disabled: {ex.Message}");

      return;
    }

    if (!severed) {
      return;
    }

    // Arm first, stop second, log last: graceful shutdown can stall in SDB disposal (a survivor
    // would keep the exclusive SDB slot, the very situation these watchdogs exist to prevent, and
    // with a wedged in-flight tool call it may not even start), and a stderr write can block
    // forever when the client stops draining the pipe, so nothing blockable may precede the
    // failsafe or the stop request.
    HardExit.Arm();

    lifetime.StopApplication();

    // stderr only, best-effort: stdout carries the MCP protocol.
    await Console.Error.WriteLineAsync($"{this.Severed}; shutting down");
  }

  /// <summary>
  /// Waits out one <see cref="PollInterval"/>, answering false when the host is stopping instead
  /// -- the shape every polling watch's loop ends on.
  /// </summary>
  protected static async Task<bool> Naps(CancellationToken stoppingToken) {
    try {
      await Task.Delay(LifetimeWatchdog.PollInterval, stoppingToken);

      return true;
    }
    catch (OperationCanceledException) {
      return false;
    }
  }
}
