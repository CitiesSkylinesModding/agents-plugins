using System;
using System.IO;

namespace UnityDevtools.Sdb;

/// <summary>
/// An invoke the debuggee took and never answered.
/// It is an <see cref="IOException" /> so <see cref="UnitySession.IsDisconnect" /> reads it as the
/// lost connection it effectively is: the session is discarded and the next call reattaches.
/// <see cref="UnitySession.Run{T}" /> catches it by name, because what happened here is the
/// opposite of what its generic disconnect message says -- the connection was fine and this call is
/// why it is being dropped -- and because the game may still be running the method.
/// </summary>
public sealed class InvokeNeverAnsweredException(TimeSpan waited)
  : IOException(
    $"the game took a call and did not answer it within {waited.TotalSeconds:0}s, so the " +
    "debugger session is being dropped rather than waited on: a call that never comes back holds " +
    "every other one behind it. Any suspend window went with the session, and the game may still " +
    "be running that call, so verify its effect before redoing it rather than repeating the window"
  );
