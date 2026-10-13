using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// Calls omitting trailing optional parameters: the default each kind binds, the overload C#
/// picks among defaulted candidates, what a miss lists, and the lookups that keep exact arity.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class EvalDefaultArgumentTests(MonoDebuggeeFixture fx) {
  private const string Defaulted = "TestFixture.Defaulted";

  private string Miss(string code) =>
    Assert.Throws<EvalFailedException>(() => fx.Eval(code)).Message;

  private string Call(string call) =>
    fx.Eval($"{EvalDefaultArgumentTests.Defaulted}.{call}").Formatted;

  // Each method reports the default it received.
  [SkippableTheory]
  [InlineData("Flag()", "True")]
  [InlineData("Whole()", "-3/40000000000")]
  [InlineData("Real()", "1.5/-2.25")]
  [InlineData("Letter()", "q")]
  [InlineData("Text()", "hi")]
  [InlineData("Member()", "Two/Full")]
  [InlineData("Nothing()", "null")]
  [InlineData("Zeroed()", "0,0")]
  [InlineData("Empty()", "empty")]
  [InlineData("Attributed()", "0/null")]
  [InlineData("Unspecified()", "Missing")]
  [InlineData("Several(1)", "1,2,three,True")]
  [InlineData("Several(1, 5)", "1,5,three,True")]
  public void AnOmittedParameterTakesItsDeclaredDefault(string call, string received) {
    Assert.Equal($"\"{received}\"", this.Call(call));
  }

  // Exact needs no default, Wider converts its int better, and Fewer omits fewer parameters.
  [SkippableTheory]
  [InlineData("Exact(1)", "exact")]
  [InlineData("Wider(1)", "defaulted")]
  [InlineData("Fewer(1)", "one")]
  public void ADefaultedCandidateRanksAsCSharpRanksIt(string call, string bound) {
    Assert.Equal($"\"{bound}\"", this.Call(call));
  }

  [SkippableFact]
  public void AConstructorOmitsItsOptionalParameter() {
    Assert.Equal("\"Zone/x/0\"", fx.Eval("new TestFixture.Built(\"Zone\", \"x\").Seen").Formatted);
  }

  // C# zero-initializes rather than running a constructor whose parameters are all optional.
  [SkippableFact]
  public void AStructWhoseConstructorIsAllOptionalBuildsZeroed() {
    Assert.Equal("0", fx.Eval("new TestFixture.Paced().Step").Formatted);
    Assert.Equal("2", fx.Eval("new TestFixture.Paced(2).Step").Formatted);
  }

  [SkippableFact]
  public void AnInferredGenericMethodOmitsADefault() {
    Assert.Equal("\"Int32/False\"", this.Call("Typed(5)"));
  }

  [SkippableTheory]
  [InlineData("Constrained<int>(1)")]
  [InlineData("Matched(5)")]
  public void AStructConstrainedTypeParameterTakesItsZeroedDefault(string call) {
    Assert.Equal("\"Int32/0\"", this.Call(call));
  }

  [SkippableFact]
  public void AWrittenTypeArgumentCallOmitsADefault() {
    Assert.Equal("\"Int32:3:False\"", fx.Eval("new TestFixture.Store().Read<int>(3)").Formatted);
  }

  [SkippableFact]
  public void ATypeArgumentOnlyAnOmittedParameterFixesIsNotInferred() {
    var message = this.Miss($"{EvalDefaultArgumentTests.Defaulted}.Seeded(1)");

    Assert.Contains(
      "Seeded is generic and T cannot be inferred (no argument fixes it): write it, as in " +
      "static Seeded<T>(Int32, T)",
      message
    );
  }

  // Each would bind default(T) were its default read as a plain [Optional].
  [SkippableTheory]
  [InlineData("Money", "Decimal", "'amount' defaults to a decimal")]
  [InlineData("When", "DateTime", "'when' defaults to a DateTime constant")]
  [InlineData("Maybe", "Nullable`1", "'maybe' defaults to 5 in a nullable")]
  public void ADefaultEvalCannotBuildIsRefusedByName(string name, string type, string reason) {
    var message = this.Miss($"{EvalDefaultArgumentTests.Defaulted}.{name}()");

    Assert.Contains(
      $"no {name} of TestFixture.Defaulted accepts these 0 argument(s); it has: " +
      $"static {name}({type}): {reason}, which eval cannot supply: pass it",
      message
    );
  }

  [SkippableTheory]
  [InlineData("Paired")]
  [InlineData("InPaired")]
  public void AZeroedGenericStructDefaultFailsWithoutDisconnecting(string name) {
    var message = this.Miss($"{EvalDefaultArgumentTests.Defaulted}.{name}(1)");

    Assert.Contains($"static {name}(Int32, KeyValuePair`2): eval cannot build a ", message);
  }

  [SkippableFact]
  public void AnOmittedPointerIsRefused() {
    Assert.Contains(
      "'pointer' is a pointer, which the debugger agent does not deliver",
      this.Miss($"{EvalDefaultArgumentTests.Defaulted}.Pointed(1)")
    );
  }

  [SkippableFact]
  public void AConstructorMissListsEveryConstructorWithItsReason() {
    var message = this.Miss("new TestFixture.Built(1)");

    Assert.Contains(
      "no TestFixture.Built constructor accepts these 1 argument(s); it has: " +
      "Built(String, String, Point): takes 2 to 3 argument(s); " +
      "Built(Holder, Point): takes 2 argument(s)",
      message
    );
  }

  [SkippableFact]
  public void AMethodMissNamesTheRangeADefaultedCandidateTakes() {
    var message = this.Miss($"{EvalDefaultArgumentTests.Defaulted}.Several()");

    Assert.Contains(
      "no Several of TestFixture.Defaulted accepts these 0 argument(s); it has: " +
      "static Several(Int32, Int32, String, Boolean): takes 1 to 4 argument(s)",
      message
    );
  }

  // A dynamic assembly serves no metadata, so its defaults stay unread and every argument binds.
  [SkippableFact]
  public void AMethodWhoseDefaultsCannotBeReadTakesEveryArgument() {
    fx.Eval("TestFixture.Emitted.Build()");

    var message = this.Miss("TestFixture.Emitted.Defaulted.Add(1)");

    Assert.Contains(
      "static Add(Int32, Int32): takes 2 argument(s), and the defaults of " +
      "UnityDevtools.TestFixture.Emitted could not be read: pass every argument",
      message
    );

    Assert.Equal("3", fx.Eval("TestFixture.Emitted.Defaulted.Add(1, 2)").Formatted);
  }

  // A property read finds no getter in a method needing an argument C# would never omit there.
  [SkippableFact]
  public void APropertyReadKeepsExactArity() {
    Assert.Equal("\"method\"", fx.Eval("TestFixture.Probed.get_Label()").Formatted);
    Assert.Contains(
      "'Label' is not a static field or readable",
      this.Miss("TestFixture.Probed.Label")
    );
  }

  // Neither declares an operator C# would call, so references compare.
  [SkippableFact]
  public void AnOperatorLookupKeepsExactArity() {
    Assert.Equal(
      "false",
      fx.Eval("TestFixture.Probed.Make() == TestFixture.Probed.Make()").Formatted
    );
  }

  [SkippableFact]
  public void AStructEqualityKeepsExactArity() {
    Assert.Equal(
      "false",
      fx.Eval("new TestFixture.ProbedValue { X = 1 } == new TestFixture.ProbedValue()").Formatted
    );
  }
}
