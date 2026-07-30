using System;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>
/// An invoke whose target threw inside the game, naming the exception the game itself raised.
/// The SDB wrapper (<see cref="InvocationException" />) carries no message of its own, so without
/// this a tool would report "Exception of type 'Mono.Debugger.Soft.InvocationException' was
/// thrown." and an agent could not tell a bad call of its own from a bug in the plugin.
/// <see cref="ThrownMessage" /> is best-effort: it is null when the thrown object's Message could
/// not be read, and <see cref="TypeName" /> alone is still actionable.
/// </summary>
public sealed class GameException(string typeName, string thrownMessage, Exception cause)
  : Exception(GameException.Describe(typeName, thrownMessage), cause) {
  /// <summary>
  /// The in-game exception's type full name, null when even that could not be read.
  /// </summary>
  public string TypeName { get; } = typeName;

  /// <summary>The in-game exception's Message, null when it could not be read.</summary>
  public string ThrownMessage { get; } = thrownMessage;

  /// <summary>
  /// The exception object itself, for a handler that needs more off it than its type and message --
  /// the partial type list a failed enumeration carries, say.
  /// INTERNAL because reading it costs round trips and is only legal inside the suspend window the
  /// failing call held: a GameException outlives that window (it reaches the tool boundary as an
  /// error), so this is for the handler on the spot, and the compiler keeps it from travelling
  /// further than the library that knows the difference.
  /// Null when the exception was built without one.
  /// </summary>
  internal ObjectMirror Thrown { get; init; }

  /// <summary>
  /// The game throw behind a failure, itself or anywhere down its inner-exception chain; null when
  /// the failure is client-side.
  /// This is how a caller that dresses up its own report (the evaluator, with statement source and
  /// locals) reads the game's type and message instead of unwrapping again.
  /// </summary>
  public static GameException FindIn(Exception failure) =>
    GameException.FirstInChain<GameException>(failure);

  /// <summary>The SDB wrapper behind a failure, for the unwrap itself.</summary>
  internal static InvocationException InvocationIn(Exception failure) =>
    GameException.FirstInChain<InvocationException>(failure);

  private static T FirstInChain<T>(Exception failure) where T : Exception {
    for (var ex = failure; ex is not null; ex = ex.InnerException) {
      if (ex is T match) {
        return match;
      }
    }

    return null;
  }

  private static string Describe(string typeName, string thrownMessage) {
    var type = typeName ?? "<unreadable type>";

    return string.IsNullOrEmpty(thrownMessage)
      ? $"in-game exception: {type}"
      : $"in-game exception: {type}: {thrownMessage}";
  }
}
