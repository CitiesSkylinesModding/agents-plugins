using System;
using Mono.Debugger.Soft;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// What an agent reads when the code it asked for throws inside the game.
/// Every tool invokes through <see cref="Invoker" />, so the unwrap living there is what makes the
/// game's own exception visible everywhere and not only in the evaluator's richer report.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class InvokerGameThrowTests(MonoDebuggeeFixture fx) {
  [SkippableFact]
  public void AnInvokeThatThrowsInGameReportsTheGamesTypeAndMessage() {
    var ex = InvokerGameThrowTests.Throwing(fx, "Boom");

    Assert.Equal("System.InvalidOperationException", ex.TypeName);
    Assert.Equal("kaboom", ex.ThrownMessage);

    // The message is what ToolGuard forwards verbatim to every non-evaluator tool.
    Assert.Contains("System.InvalidOperationException", ex.Message, StringComparison.Ordinal);
    Assert.Contains("kaboom", ex.Message, StringComparison.Ordinal);

    Assert.IsType<InvocationException>(ex.InnerException);
  }

  [SkippableFact]
  public void AnUnreadableMessageStillReportsTheThrownType() {
    var ex = InvokerGameThrowTests.Throwing(fx, "BoomUnreadable");

    Assert.Equal("TestFixture.SpitefulException", ex.TypeName);
    Assert.Null(ex.ThrownMessage);
    Assert.Contains("TestFixture.SpitefulException", ex.Message, StringComparison.Ordinal);
  }

  private static GameException Throwing(MonoDebuggeeFixture fx, string method) {
    // Reading the Invoker up front runs the fixture's skip check outside Assert.Throws, which
    // would otherwise report a skipped suite as the wrong exception type.
    _ = fx.Invoker;

    return Assert.Throws<GameException>(() => fx.WithInvoker(inv =>
        inv.InvokeStatic(inv.ResolveType("TestFixture.Thrower"), method)
      )
    );
  }
}
