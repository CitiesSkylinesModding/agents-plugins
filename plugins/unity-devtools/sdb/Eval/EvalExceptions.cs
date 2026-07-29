using System;
using System.Collections.Generic;

namespace UnityDevtools.Sdb.Eval;

/// <summary>
/// An evaluation error raised while walking the AST, before it is dressed with statement context.
/// <see cref="Position" /> is a character offset into the source, -1 when unknown.
/// </summary>
public sealed class EvalRuntimeException(string message, int position = -1) : Exception(message) {
  public int Position { get; } = position;
}

/// <summary>
/// A failed evaluation with its full report: which statement failed and where, the in-game
/// exception when the failure happened debuggee-side, and the locals evaluated so far (shallow),
/// so the caller can see how far the program got.
/// </summary>
public sealed class EvalFailedException(string message, Exception cause)
  : Exception(message, cause) {
  public required int StatementIndex { get; init; }

  public required string StatementSource { get; init; }

  public required int Position { get; init; }

  /// <summary>
  /// The game's own exception, when the failure happened debuggee-side; null when it is
  /// client-side.
  /// It is read off the cause chain, so it stays whatever the unwrap in
  /// <see cref="UnityDevtools.Sdb.Invoker" /> produced and this report only adds what the evaluator
  /// knows.
  /// </summary>
  public GameException Game => GameException.FindIn(this);

  /// <summary>
  /// Locals evaluated before the failure, formatted shallow, in declaration order.
  /// </summary>
  public required IReadOnlyList<KeyValuePair<string, string>> Locals { get; init; }
}
