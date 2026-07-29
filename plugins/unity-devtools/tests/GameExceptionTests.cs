using System;
using Xunit;

namespace UnityDevtools.Sdb.Tests;

/// <summary>
/// How a game throw is found and rendered once <see cref="Invoker" /> has unwrapped it.
/// The unwrap itself needs a debuggee and lives in the integration suite.
/// </summary>
public sealed class GameExceptionTests {
  [Fact]
  public void ItRendersTheTypeAndMessageAgentsReadInEveryToolError() {
    var ex = new GameException("System.NullReferenceException", "object reference not set", null);

    Assert.Equal(
      "in-game exception: System.NullReferenceException: object reference not set",
      ex.Message
    );
  }

  [Fact]
  public void AnUnreadableMessageLeavesTheTypeAlone() {
    var ex = new GameException("System.NullReferenceException", null, null);

    Assert.Equal("in-game exception: System.NullReferenceException", ex.Message);
  }

  [Fact]
  public void AnEmptyMessageReadsLikeNoMessageRatherThanADanglingColon() {
    var ex = new GameException("System.NullReferenceException", "", null);

    Assert.Equal("in-game exception: System.NullReferenceException", ex.Message);
  }

  [Fact]
  public void AThrowWhoseTypeCouldNotBeReadStillReportsAsOne() {
    var ex = new GameException(null, "something went wrong", null);

    Assert.Equal("in-game exception: <unreadable type>: something went wrong", ex.Message);
  }

  [Fact]
  public void ItIsFoundThroughAnInnerExceptionChain() {
    var game = new GameException("System.InvalidOperationException", "kaboom", null);

    var dressedUp = new InvalidOperationException(
      "outer",
      new InvalidOperationException("middle", game)
    );

    Assert.Same(game, GameException.FindIn(dressedUp));
  }

  [Fact]
  public void AClientSideFailureCarriesNoGameThrow() {
    Assert.Null(GameException.FindIn(new InvalidOperationException("no debuggee involved")));
  }
}
