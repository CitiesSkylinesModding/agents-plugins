// Compile-level shim for System.Runtime.Remoting.Messaging.AsyncResult, which modern .NET removed
// but the vendored VirtualMachineManager Begin/End methods reference.
// Those code paths are never called: SdbSession connects synchronously via
// `VirtualMachineManager.Connect(Connection, ...)` instead (delegate `BeginInvoke` would throw
// `PlatformNotSupportedException` on .NET Core anyway).

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace System.Runtime.Remoting.Messaging;

// CA1852: kept unsealed to mirror the shape of the BCL AsyncResult this stands in for.
[SuppressMessage("Performance", "CA1852", Justification = "Mirrors the BCL AsyncResult shape")]
[UsedImplicitly]
internal class AsyncResult : IAsyncResult {
  // CA1822: instance member on purpose; the vendored Begin/End paths read `.AsyncDelegate` off an
  // AsyncResult instance, so making it static would not compile against those references.
  [SuppressMessage("Performance", "CA1822", Justification = "Read on an instance by vendored code")]
  public object AsyncDelegate =>
    throw new PlatformNotSupportedException("legacy Remoting AsyncResult shim");

  public object AsyncState =>
    throw new PlatformNotSupportedException("legacy Remoting AsyncResult shim");

  public WaitHandle AsyncWaitHandle =>
    throw new PlatformNotSupportedException("legacy Remoting AsyncResult shim");

  public bool CompletedSynchronously => false;

  public bool IsCompleted => false;
}
