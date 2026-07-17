using System.Net;
using System.Net.Sockets;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>
/// One attach-act-detach session against the game's Mono Soft Debugger agent.
/// The VM is suspended while the session is open (invokes need a suspended thread);
/// <see cref="Dispose"/> always resumes and detaches, even on failure, so the game never stays
/// frozen.
/// </summary>
public sealed class SdbSession : IDisposable {
  private SdbSession(VirtualMachine vm) {
    this.Vm = vm;
  }

  public VirtualMachine Vm { get; }

  public void Dispose() {
    // The agent counts suspensions; resume until it reports "not suspended" so the game is
    // guaranteed to run again whatever nesting the session built up.
    for (var i = 0; i < 16; i++) {
      try {
        this.Vm.Resume();
      }
      catch {
        // Not suspended anymore, or connection gone (socket close auto-resumes).
        break;
      }
    }

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

    // The agent queues a VM_START composite event at attach time; pump it so the
    // event queue is clean, then suspend explicitly so invokes are legal.
    vm.GetNextEventSet();
    vm.Suspend();

    return new SdbSession(vm);
  }
}
