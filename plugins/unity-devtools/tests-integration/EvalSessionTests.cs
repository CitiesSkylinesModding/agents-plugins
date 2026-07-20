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

    Assert.Contains("garbage-collected", ex.Message);
    Assert.Contains("re-evaluate", ex.Message);
  }

  [SkippableFact]
  public void FailureReportCarriesStatementContextAndInGameException() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("var x = 1; TestFixture.Thrower.Boom()")
    );

    Assert.Equal(1, ex.StatementIndex);
    Assert.Equal("TestFixture.Thrower.Boom()", ex.StatementSource);
    Assert.Equal("System.InvalidOperationException", ex.GameExceptionType);
    Assert.Equal("kaboom", ex.GameExceptionMessage);
    Assert.Contains(ex.Locals, local => local is { Key: "x", Value: "1" });
  }
}
