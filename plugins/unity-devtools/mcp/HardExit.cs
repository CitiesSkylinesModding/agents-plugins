using System.Diagnostics;

namespace UnityDevtools.Mcp;

/// <summary>
/// Last-resort termination for shutdown paths that can stall forever: disposing the SDB session
/// performs synchronous wire round-trips with no timeout, which block indefinitely when the
/// debuggee stops replying while its socket stays open (e.g. a crash handler froze the game
/// process with every thread suspended).
/// A stalled survivor holds the exclusive SDB slot and, in dev, file locks on its own build
/// output, so it must die once its client is gone.
/// <see cref="Environment.Exit(int)"/> cannot serve here: it runs ProcessExit handlers, and the
/// console lifetime's handler waits on the very shutdown that is stalled.
/// Killing our own process bypasses all managed teardown; that is safe for the game, because a
/// dying socket auto-resumes the VM.
/// </summary>
internal static class HardExit {
  /// <summary>
  /// A healthy shutdown (resume + detach against a responsive game, host teardown) completes in
  /// well under a second; anything longer than this grace counts as a stall that will never
  /// finish.
  /// A legitimately long operation still in flight (e.g. an advance window sleeping with the
  /// session gate held) dies with the process too, deliberately: every arming signal means the
  /// client is gone, and the dying socket releases the game unharmed.
  /// </summary>
  private static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

  private static int armed;

  /// <summary>
  /// Arms the kill timer once: the process terminates unless it exits by itself within the grace
  /// period.
  /// Later calls no-op, so overlapping shutdown signals share one timer, and a clean exit never
  /// waits on the timer (a background thread).
  /// The timer thread performs no I/O: a stderr write can block forever when the client stops
  /// draining the pipe, and nothing may stand between the timer and the kill; callers own the
  /// diagnostics, AFTER arming.
  /// </summary>
  public static void Arm() {
    if (Interlocked.Exchange(ref HardExit.armed, 1) is not 0) {
      return;
    }

    var killer = new Thread(() => {
        Thread.Sleep(HardExit.Grace);

        Process.GetCurrentProcess().Kill();
      }
    ) {
      IsBackground = true,
      Name = "shutdown-failsafe"
    };

    killer.Start();
  }
}
