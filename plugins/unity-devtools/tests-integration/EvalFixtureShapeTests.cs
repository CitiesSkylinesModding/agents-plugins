using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// Shapes the reference game (compiled as C# 9) cannot provide, so they were never verifiable live:
/// C# 10+ constructs and layouts the walker must still evaluate correctly.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class EvalFixtureShapeTests(MonoDebuggeeFixture fx) {
  [SkippableFact]
  public void DeclaredParameterlessStructCtorRuns() {
    var outcome = fx.Eval("new TestFixture.Counted().N");

    Assert.Equal("7", outcome.Formatted);
    Assert.Equal("System.Int32", outcome.TypeName);
  }

  [SkippableFact]
  public void UlongBackedEnumConstantAboveLongMaxResolves() {
    var outcome = fx.Eval("TestFixture.Big.Huge");

    Assert.Equal("Big.Huge", outcome.Formatted);
    Assert.Equal("TestFixture.Big", outcome.TypeName);
  }

  [SkippableFact]
  public void UlongBackedEnumCastsToItsFullNumericValue() {
    var outcome = fx.Eval("(ulong) TestFixture.Big.Huge");

    Assert.Equal("18446744073709551615", outcome.Formatted);
    Assert.Equal("System.UInt64", outcome.TypeName);
  }

  [SkippableFact]
  public void NewMemberShadowingPicksTheMostDerivedOne() {
    Assert.Equal("\"derived\"", fx.Eval("new TestFixture.DerivedThing().Name").Formatted);
  }

  [SkippableFact]
  public void StructMethodMutatesItsReceiver() {
    var outcome = fx.Eval("var a = new TestFixture.Accum(); a.AddChecked(5, out var b); a.Total");

    Assert.Equal("5", outcome.Formatted);
  }

  [SkippableFact]
  public void StructMethodOutParameterLandsInItsLocal() {
    var outcome = fx.Eval(
      "var a = new TestFixture.Accum(); a.AddChecked(5, out var b); a.AddChecked(2, out b); b"
    );

    Assert.Equal("5", outcome.Formatted);
    Assert.Equal("System.Int32", outcome.TypeName);
  }

  [SkippableFact]
  public void RankTwoArrayIndexingFailsLoudly() {
    // Deliberate walker limitation: the low-level indexer is linear, so a rank > 1 access would
    // silently misread; the contract demands a loud, actionable rejection instead.
    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval("TestFixture.Shapes.Grid[1, 0]"));

    Assert.Contains("rank-2 array indexing is not supported", ex.Message);
  }
}
