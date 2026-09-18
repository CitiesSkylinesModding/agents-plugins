using System;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>
/// The main thread never parked at a managed safe point inside a suspend window, so nothing could
/// be invoked on it.
/// It stays a <see cref="VMNotSuspendedException" /> so every site that reads NOT_SUSPENDED as
/// lateness rather than as a fault keeps its meaning; what changes is the words, since the wire's
/// own ("The vm is not suspended") describe a state the session is not in and name no way out.
/// </summary>
public sealed class MainThreadNotParkedException(TimeSpan waited) : VMNotSuspendedException {
  public override string Message =>
    $"the game's main thread stayed inside engine or native code for {waited.TotalSeconds:0.#}s, " +
    "so no call could be run on it; the session is still attached and nothing was left " +
    "half-done, and the same call works again once the game is advancing frames - check that it is";
}
