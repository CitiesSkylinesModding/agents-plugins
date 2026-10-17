using System;
using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// The grammar forms admitted under the admission rule, each evaluating as C# would: predefined
/// aliases as roots, `ref` arguments, interpolation format clauses, and `default(T)`.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class EvalGrammarTests(MonoDebuggeeFixture fx) {
  [SkippableTheory]
  [InlineData("int.MaxValue", "2147483647")]
  [InlineData("string.Concat(\"a\", \"b\")", "\"ab\"")]
  public void APredefinedAliasRootsAMemberOrACall(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableFact]
  public void RefPassesTheLocalInAndWritesTheCalleesValueBack() {
    var outcome = fx.Eval(
      "var n = 5; var received = TestFixture.ByRef.Add(ref n, 3); $\"{received}|{n}\""
    );

    Assert.Equal("\"5|8\"", outcome.Formatted);
  }

  [SkippableFact]
  public void RefWritesBackAStructLocal() {
    var outcome = fx.Eval(
      "var p = new TestFixture.Point { X = 1, Y = 2 }; TestFixture.ByRef.Shift(ref p, 10); " +
      "$\"{p.X},{p.Y}\""
    );

    Assert.Equal("\"11,12\"", outcome.Formatted);
  }

  [SkippableFact]
  public void RefAgainstAParameterNotDeclaredRefIsAMissNamingTheMismatch() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("var n = 5; TestFixture.ByRef.Plain(ref n)")
    );

    Assert.Contains("'value' is not a ref parameter", ex.Message, StringComparison.Ordinal);
  }

  [SkippableFact]
  public void APlainArgumentAgainstARefParameterIsAMissSayingWriteRef() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("var n = 5; TestFixture.ByRef.Add(n, 3)")
    );

    Assert.Contains(
      "'value' is a ref parameter: write `ref`",
      ex.Message,
      StringComparison.Ordinal
    );
  }

  [SkippableFact]
  public void RefAgainstAnOutParameterIsAMissSayingWriteOut() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("var n = 5; TestFixture.ByRef.Take(ref n)")
    );

    Assert.Contains(
      "'value' is an out parameter: write `out`",
      ex.Message,
      StringComparison.Ordinal
    );
  }

  [SkippableFact]
  public void RefArgumentsFixATypeParameter() {
    var outcome = fx.Eval(
      "var a = 1; var b = 2; TestFixture.ByRef.Swap(ref a, ref b); $\"{a}|{b}\""
    );

    Assert.Equal("\"2|1\"", outcome.Formatted);
  }

  [SkippableFact]
  public void APlainArgumentBindsAnInParameter() {
    Assert.Equal("4", fx.Eval("var q = 4; TestFixture.ByRef.Read(q)").Formatted);
  }

  // Sent by reference, through `ref` or into an `in` parameter, a client-built generic struct or
  // nullable disconnects the VM, so the refusal must come before any invoke.
  [SkippableTheory]
  [InlineData("var kv = TestFixture.ByRef.MakePair(); TestFixture.ByRef.FlipPair(ref kv)")]
  [InlineData("var m = TestFixture.ByRef.MakeMaybe(); TestFixture.ByRef.BumpMaybe(ref m)")]
  [InlineData("var kv = TestFixture.ByRef.MakePair(); TestFixture.ByRef.ReadPair(kv)")]
  [InlineData("var kv = TestFixture.ByRef.MakePair(); TestFixture.ByRef.Over(kv)")]
  [InlineData("var kv = TestFixture.ByRef.MakePair(); new TestFixture.PairReader(kv).Key")]
  [InlineData("var kv = TestFixture.ByRef.MakePair(); new TestFixture.PairReader()[kv]")]
  public void AGenericStructOrNullableSentByReferenceIsRefused(string code) {
    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval(code));

    Assert.Contains("by reference, which eval cannot send", ex.Message, StringComparison.Ordinal);
  }

  [SkippableFact]
  public void AGenericStructIntoAnInObjectParameterIsSent() {
    var outcome = fx.Eval("var kv = TestFixture.ByRef.MakePair(); TestFixture.ByRef.ReadBoxed(kv)");

    Assert.Equal("\"boxed:[1, 2]\"", outcome.Formatted);
  }

  [SkippableFact]
  public void RefOnAGenericStructAgainstAByValueParameterSaysDropRef() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("var kv = TestFixture.ByRef.MakePair(); TestFixture.ByRef.Plain(ref kv)")
    );

    Assert.Contains(
      "'value' is not a ref parameter: drop `ref`",
      ex.Message,
      StringComparison.Ordinal
    );
  }

  [SkippableFact]
  public void RefOnALocalOfAnotherValueTypeIsAMissNamingBothTypes() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("var x = 3; TestFixture.ByRef.BumpMaybe(ref x)")
    );

    Assert.Contains("of the parameter's exact type", ex.Message, StringComparison.Ordinal);
  }

  [SkippableFact]
  public void RefOnANameThatIsNoLocalSaysRefTakesALocal() {
    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval("TestFixture.ByRef.Add(ref n, 1)"));

    Assert.Contains("`ref` takes a local", ex.Message, StringComparison.Ordinal);
  }

  [SkippableFact]
  public void AFormatClauseOnAMirrorEqualsTheDebuggeesToString() {
    var outcome = fx.Eval(
      "var g = new TestFixture.Gauge { Level = 2.46 }; $\"{g:F1}|{g.ToString(\"F1\", null)}\""
    );

    Assert.Equal("\"gauge:2.5|gauge:2.5\"", outcome.Formatted);
  }

  [SkippableFact]
  public void AFormatClauseReachesAnExplicitImplementation() {
    Assert.Equal("\"dial:X2\"", fx.Eval("var d = new TestFixture.Dial(); $\"{d:X2}\"").Formatted);
  }

  [SkippableFact]
  public void AFormatClauseOnAClientValueFormatsUnderTheInvariantCulture() {
    var outcome = fx.Eval("$\"{1.25 * 2:F2}|{TestFixture.Shapes.Answer:D4}\"");

    Assert.Equal("\"2.50|0042\"", outcome.Formatted);
  }

  [SkippableFact]
  public void AFormatClauseOnAnEnumMirrorRunsTheEnumsFormat() {
    Assert.Equal("\"00000002\"", fx.Eval("$\"{TestFixture.Small.Two:X}\"").Formatted);
  }

  [SkippableFact]
  public void AFormatClauseOnAValueThatIsNotFormattableIsIgnored() {
    Assert.Equal("\"hello\"", fx.Eval("$\"{TestFixture.Shapes.Greeting:F1}\"").Formatted);
  }

  [SkippableTheory]
  [InlineData("default(TestFixture.Point)", "Point { X=0, Y=0 }")]
  [InlineData("default(int)", "0")]
  [InlineData("default(TestFixture.Small)", "Small.0")]
  public void DefaultOfAStructIsItsZeroValue(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableTheory]
  [InlineData("default(TestFixture.Holder)")]
  [InlineData("default(string)")]
  public void DefaultOfAReferenceTypeIsNull(string code) {
    Assert.Equal("null", fx.Eval(code).Formatted);
  }
}
