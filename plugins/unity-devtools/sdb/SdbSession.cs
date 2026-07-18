using System.Net;
using System.Net.Sockets;
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
  private SdbSession(VirtualMachine vm) {
    this.Vm = vm;
  }

  public VirtualMachine Vm { get; }

  public void Dispose() {
    SdbSession.DrainSuspends(this.Vm);

    try {
      this.Vm.Detach();
    }
    catch {
      // Connection already dead; nothing left to detach.
    }
  }

  public static SdbSession Connect(string host, int port) {
    // Synchronous connect through the internal TcpConnection (same assembly, so accessible):
    // VirtualMachineManager's Begin/EndConnect rely on delegate BeginInvoke, which modern .NET
    // removed at runtime.
    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

    socket.Connect(new IPEndPoint(IPAddress.Parse(host), port));

    var vm = VirtualMachineManager.Connect(new TcpConnection(socket), null, null);

    // The agent queues a VM_START composite event at attach time; pump it so the event queue is
    // clean, then normalize to "running" whatever suspend state the composite left behind, so the
    // game keeps playing until an operation opens its own suspend window.
    vm.GetNextEventSet();
    SdbSession.DrainSuspends(vm);

    return new SdbSession(vm);
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
