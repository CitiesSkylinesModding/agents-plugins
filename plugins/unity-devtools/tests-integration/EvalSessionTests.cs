using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// Behaviors spanning several evals in one server session: the <c>_</c> slot, its collected-mirror
/// failure mode, and the failure report agents receive.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class EvalSessionTests(MonoDebuggeeFixture fx) {
  [SkippableFact]
  public void UnderscoreChainsAcrossEvals() {
    var state = new EvalState();

    Assert.Equal("41", fx.Eval("40 + 1", state).Formatted);
    Assert.Equal("42", fx.Eval("_ + 1", state).Formatted);
  }

  [SkippableFact]
  public void CollectedUnderscoreMirrorFailsWithReEvaluateHint() {
    var state = new EvalState();

    // A heap result with no debuggee-side root; the VM resumes between evals, so a forced GC leaves
    // `_` holding a collected mirror.
    _ = fx.Eval("new System.Text.StringBuilder(\"gone\")", state);

    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval(
        "System.GC.Collect(); _.ToString()",
        state
      )
    );

    Assert.Contains("garbage-collected the object held by `_`", ex.Message);
    Assert.Contains("re-evaluate", ex.Message);
  }

  [SkippableFact]
  public void CollectedLocalMirrorIsNamed() {
    var state = new EvalState();

    // The debuggee keeps alive an object the debugger touched in the current window, so only a
    // local bound off `_` without a round trip can hold a mirror the collection takes.
    _ = fx.Eval("new System.Text.StringBuilder(\"gone\")", state);

    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval(
        "var sb = _; System.GC.Collect(); sb.ToString()",
        state
      )
    );

    Assert.Contains("garbage-collected the objects held by `sb`, `_`", ex.Message);
    Assert.Contains("may invalidate", ex.Message);
  }

  [SkippableFact]
  public void CollectedMirrorNoLocalHoldsLeavesUnderscoreUnnamed() {
    var state = new EvalState();

    // `_` is a struct, whose own mirror cannot be collected, holding an object that can.
    _ = fx.Eval(
      "new System.Collections.DictionaryEntry(\"k\", new System.Text.StringBuilder(\"gone\"))",
      state
    );

    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval(
        "System.GC.Collect(); _.Value.ToString()",
        state
      )
    );

    Assert.Contains("the statement used an object the game garbage-collected", ex.Message);
    Assert.DoesNotContain("`_`", ex.Message);
  }

  [SkippableFact]
  public void FailureReportClipsALongLocalAndNamesWhatItCut() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("var big = \"x\".PadRight(5000, 'y'); TestFixture.Thrower.Boom()")
    );

    var big = Assert.Single(ex.Locals, local => local.Key is "big").Value;

    // The rendering quotes the string, so it runs two characters past the value's own length.
    var elided = 5002 - EvalInterpreter.LocalBudget;

    Assert.StartsWith("\"xyyy", big);
    Assert.EndsWith($"(+{elided} more characters)", big);
    Assert.True(big.Length < EvalInterpreter.LocalBudget + 40, big);
  }

  [SkippableFact]
  public void FailureReportCarriesStatementContextAndInGameException() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("var x = 1; TestFixture.Thrower.Boom()")
    );

    Assert.Equal(1, ex.StatementIndex);
    Assert.Equal("TestFixture.Thrower.Boom()", ex.StatementSource);
    Assert.Equal("System.InvalidOperationException", ex.Game.TypeName);
    Assert.Equal("kaboom", ex.Game.ThrownMessage);

    // The failure's own message names the throw too, for the consumers that read only that.
    Assert.Equal("in-game exception: System.InvalidOperationException: kaboom", ex.Message);
    Assert.Contains(ex.Locals, local => local is { Key: "x", Value: "1" });
  }
}
