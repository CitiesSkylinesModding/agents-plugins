// Compile-level shim for System.Runtime.Remoting.Messaging.AsyncResult, which modern .NET removed
// but the vendored VirtualMachineManager Begin/End methods reference.
// Those code paths are never called: SdbSession connects synchronously via
// `VirtualMachineManager.Connect(Connection, ...)` instead (delegate `BeginInvoke` would throw
// `PlatformNotSupportedException` on .NET Core anyway).

using JetBrains.Annotations;

namespace System.Runtime.Remoting.Messaging;

[UsedImplicitly]
internal class AsyncResult : IAsyncResult {
  public object AsyncDelegate =>
    throw new PlatformNotSupportedException("legacy Remoting AsyncResult shim");

  public object AsyncState =>
    throw new PlatformNotSupportedException("legacy Remoting AsyncResult shim");

  public WaitHandle AsyncWaitHandle =>
    throw new PlatformNotSupportedException("legacy Remoting AsyncResult shim");

  public bool CompletedSynchronously => false;

  public bool IsCompleted => false;
}
