using System;
using System.Net.Sockets;
using System.Threading;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>
/// One live attach to the game's Mono Soft Debugger agent.
/// The VM is left RUNNING between operations; callers open their own suspend windows (invokes need
/// a suspended thread).
/// <see cref="Dispose"/> always resumes and detaches, even on failure, so the game never stays
/// frozen.
/// </summary>
public sealed class SdbSession : IDisposable {
  private static readonly TimeSpan DefaultGreetingWait = TimeSpan.FromSeconds(5);

  private SdbSession(VirtualMachine vm) {
    this.Vm = vm;
  }

  public VirtualMachine Vm { get; }

  /// <summary>
  /// Whether the connection is still up, answered without touching the wire.
  /// It reads the receiver thread's own disconnect flag, which that thread sets on EOF, on a VM
  /// crash and on any receive failure, before it closes the transport.
  /// Polling the socket instead loses to that thread; see
  /// docs/solutions/sdb-vendored-client-limits.md.
  /// </summary>
  public bool IsAlive => !this.Vm.conn.DisconnectedEvent.WaitOne(0);

  public void Dispose() {
    try {
      // Any armed breakpoint would re-suspend the game after we resume and detach; clear every
      // agent-side breakpoint first, so letting go really lets the game run.
      this.Vm.ClearAllBreakpoints();
    }
    catch {
      // Connection already dead; the agent clears its requests on disconnect anyway.
    }

    SdbSession.DrainSuspends(this.Vm);

    try {
      this.Vm.Detach();
    }
    catch {
      // Connection already dead; nothing left to detach.
    }
  }

  /// <summary>
  /// Attaches to a Mono Soft Debugger agent, allowing it <paramref name="greetingWait"/> (five
  /// seconds by default) for each step it could stall on: answering the connect, speaking first,
  /// and finishing the handshake.
  /// </summary>
  public static SdbSession Connect(string host, int port, TimeSpan? greetingWait = null) {
    // Synchronous connect through the internal TcpConnection (same assembly, so accessible):
    // VirtualMachineManager's Begin/EndConnect rely on delegate BeginInvoke, which modern .NET
    // removed at runtime.
    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    var wait = greetingWait ?? SdbSession.DefaultGreetingWait;

    try {
      SdbSession.Dial(socket, host, port, wait);

      // The agent greets first and the vendored handshake read is untimed (upstream marks the gap
      // with a FIXME), so a peer that accepts and stays silent would block the caller forever, with
      // the session gate held against every other tool.
      // This waits rather than sets a receive timeout: a timeout stays on the socket once the
      // vendored receiver thread takes over, and that thread would then call a healthy connection
      // dead on its first idle period.
      // It also reads nothing, leaving the greeting for the handshake: Mono's agent treats a failed
      // handshake as fatal and exits, so a half-read one would kill the game.
      if (!socket.Poll(wait, SelectMode.SelectRead)) {
        throw new InvalidOperationException(
          $"{host}:{port} accepted the connection but sent no Mono debugger greeting within " +
          $"{wait.TotalSeconds:0.#}s; nothing there is speaking the Soft Debugger protocol"
        );
      }

      var vm = SdbSession.Handshake(socket, host, port, wait);

      // The agent queues a VM_START composite event at attach time; pump it so the event queue is
      // clean, then normalize to "running" whatever suspend state the composite left behind, so the
      // game keeps playing until an operation opens its own suspend window.
      vm.GetNextEventSet();
      SdbSession.DrainSuspends(vm);

      return new SdbSession(vm);
    }
    catch {
      // Everything up to the returned session closes the socket on the way out: the agent takes ONE
      // client, so a connected socket left behind would hold the game's exclusive debugger slot
      // against the corrected retry, which would then fail for a reason naming nothing.
      socket.Close();

      throw;
    }
  }

  /// <summary>
  /// Runs the vendored handshake under a deadline. That read is fixed-length and untimed, so a peer
  /// writing one byte and then stalling satisfies the greeting poll and blocks here for good, with
  /// the session gate held against every other tool -- and the derived port sits in the ephemeral
  /// range, where an unrelated program owning it is ordinary.
  /// Closing the socket is what wakes that read: a receive timeout cannot, since it would stay on
  /// the socket for the vendored receiver thread, which calls an idle connection dead.
  /// </summary>
  private static VirtualMachine Handshake(Socket socket, string host, int port, TimeSpan wait) {
    using var deadline = new CancellationTokenSource(wait);
    using var abort = deadline.Token.Register(socket.Close);

    try {
      var vm = VirtualMachineManager.Connect(new TcpConnection(socket), null, null);

      // Disarmed the moment the handshake lands: a deadline left running to the end of this scope
      // would close the socket of a connection that just succeeded, and nothing threw, so the arm
      // below would not even name what happened.
      deadline.CancelAfter(Timeout.InfiniteTimeSpan);

      return vm;
    }
    catch when (deadline.IsCancellationRequested) {
      throw new InvalidOperationException(
        $"{host}:{port} began the Mono debugger handshake but did not finish it within " +
        $"{wait.TotalSeconds:0.#}s; nothing there is speaking the Soft Debugger protocol"
      );
    }
  }

  /// <summary>
  /// Connects within <paramref name="wait"/>. The beacon advertises whichever interface address the
  /// player picked, which on a machine carrying a VPN or a hypervisor switch can be one nothing
  /// routes to: an untimed connect would then sit out the OS SYN retry schedule, tens of seconds
  /// with the session gate held against every other tool.
  /// </summary>
  private static void Dial(Socket socket, string host, int port, TimeSpan wait) {
    using var giveUp = new CancellationTokenSource(wait);

    try {
      socket.ConnectAsync(host, port, giveUp.Token).AsTask().GetAwaiter().GetResult();
    }
    catch (OperationCanceledException) {
      throw new InvalidOperationException(
        $"{host}:{port} did not answer within {wait.TotalSeconds:0.#}s; nothing is listening " +
        "there, or the address is not reachable from this machine"
      );
    }
  }

  /// <summary>
  /// Resumes until the agent reports "not suspended": suspensions are counted, so a single resume
  /// is not enough to guarantee the game runs again.
  /// </summary>
  private static void DrainSuspends(VirtualMachine vm) {
    for (var i = 0; i < 16; i++) {
      try {
        vm.Resume();
      }
      catch {
        // Not suspended anymore, or connection gone (socket close auto-resumes).
        break;
      }
    }
  }
}
