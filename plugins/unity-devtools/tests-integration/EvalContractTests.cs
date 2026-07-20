using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// The evaluator's semantic contract: the documented deliberate divergences from C# behave as
/// documented, and edges fail loudly with actionable messages, never silently wrong.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class EvalContractTests(MonoDebuggeeFixture fx) {
  [SkippableFact]
  public void NumericArgumentConvertsToEnumParameter() {
    // Documented divergence: numeric-to-enum is a REPL convenience (C# itself wants a cast).
    Assert.Equal("\"Two\"", fx.Eval("TestFixture.Overloads.TakesSmall(2)").Formatted);
  }

  [SkippableFact]
  public void NumericCastsToEnumMember() {
    var outcome = fx.Eval("(TestFixture.Small) 2");

    Assert.Equal("Small.Two", outcome.Formatted);
    Assert.Equal("TestFixture.Small", outcome.TypeName);
  }

  [SkippableFact]
  public void InRangeIntegralNarrowingIsAccepted() {
    // Documented divergence: the evaluator cannot tell a literal from a variable, so in-range
    // narrowing is accepted where C# accepts it only for constants.
    var outcome = fx.Eval("TestFixture.Overloads.TakesByte(200)");

    Assert.Equal("200", outcome.Formatted);
    Assert.Equal("System.Byte", outcome.TypeName);
  }

  [SkippableFact]
  public void OutOfRangeNarrowingFailsLoudly() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("TestFixture.Overloads.TakesByte(300)")
    );

    Assert.Contains("300 does not fit Byte", ex.Message);
  }

  [SkippableFact]
  public void EnumPlusNumericKeepsTheEnumType() {
    // Documented divergence: a numeric operand joins on the underlying value (C# only admits the
    // literal zero); +/- against a numeric offset keeps the enum type.
    var outcome = fx.Eval("TestFixture.Small.Two + 1");

    Assert.Equal("Small.Three", outcome.Formatted);
    Assert.Equal("TestFixture.Small", outcome.TypeName);
  }

  [SkippableFact]
  public void EnumComparesToNumericOnUnderlyingValue() {
    Assert.Equal("true", fx.Eval("TestFixture.Small.Two == 2").Formatted);
  }

  [SkippableFact]
  public void CrossEnumOperandsAreRejectedLoudly() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("TestFixture.Small.Two == TestFixture.Big.Huge")
    );

    Assert.Contains("different enum types", ex.Message);
  }
}
