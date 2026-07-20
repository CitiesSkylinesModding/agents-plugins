namespace UnityDevtools.Sdb.Eval;

/// <summary>
/// Rejection of a program before any evaluation: a syntax error or an unsupported construct.
/// <see cref="Position" /> is a character offset into the evaluated source.
/// </summary>
public sealed class EvalParseException(string message, int position) : Exception(message) {
  public int Position { get; } = position;
}
