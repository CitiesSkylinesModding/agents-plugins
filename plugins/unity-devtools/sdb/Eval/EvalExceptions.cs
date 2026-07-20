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
public sealed class EvalFailedException(string message) : Exception(message) {
  public required int StatementIndex { get; init; }

  public required string StatementSource { get; init; }

  public required int Position { get; init; }

  /// <summary>The in-game exception's type full name, when the debuggee threw.</summary>
  public string GameExceptionType { get; init; }

  /// <summary>
  /// The in-game exception's Message, when the debuggee threw and it was readable.
  /// </summary>
  public string GameExceptionMessage { get; init; }

  /// <summary>
  /// Locals evaluated before the failure, formatted shallow, in declaration order.
  /// </summary>
  public required IReadOnlyList<KeyValuePair<string, string>> Locals { get; init; }
}
