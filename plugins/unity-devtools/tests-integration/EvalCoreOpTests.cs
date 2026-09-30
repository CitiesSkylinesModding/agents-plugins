using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// Core-operation semantics through the full walker against a live Mono debuggee: the historical
/// defect classes (equality, overload binding, coercion, struct-write persistence) lived here,
/// never in syntax breadth.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class EvalCoreOpTests(MonoDebuggeeFixture fx) {
  [SkippableFact]
  public void AddsIntegerLiterals() {
    var outcome = fx.Eval("1 + 2");

    Assert.Equal("3", outcome.Formatted);
    Assert.Equal("System.Int32", outcome.TypeName);
  }

  [SkippableFact]
  public void MirrorStringEqualsClientLiteralByValue() {
    var outcome = fx.Eval("TestFixture.Shapes.Greeting == \"hello\"");

    Assert.Equal("true", outcome.Formatted);
    Assert.Equal("System.Boolean", outcome.TypeName);
  }

  [SkippableFact]
  public void MirrorStringInequalityIsFalse() {
    Assert.Equal("false", fx.Eval("TestFixture.Shapes.Greeting == \"world\"").Formatted);
  }

  [SkippableFact]
  public void MirrorIntEqualsClientLiteral() {
    Assert.Equal("true", fx.Eval("TestFixture.Shapes.Answer == 42").Formatted);
  }

  [SkippableFact]
  public void NullMirrorEqualsNullLiteral() {
    Assert.Equal("true", fx.Eval("TestFixture.Shapes.NullText == null").Formatted);
  }

  [SkippableFact]
  public void CrossTypeNumericEqualityPromotes() {
    Assert.Equal("true", fx.Eval("TestFixture.Shapes.Answer == 42.0").Formatted);
  }

  [SkippableFact]
  public void OverloadBindsExactIntMatch() {
    Assert.Equal("\"int\"", fx.Eval("TestFixture.Overloads.Pick(1)").Formatted);
  }

  [SkippableFact]
  public void OverloadBindsExactLongMatchViaSuffix() {
    Assert.Equal("\"long\"", fx.Eval("TestFixture.Overloads.Pick(1L)").Formatted);
  }

  [SkippableFact]
  public void OverloadBindsFloatToDoubleOverObject() {
    Assert.Equal("\"double\"", fx.Eval("TestFixture.Overloads.Pick(1.5f)").Formatted);
  }

  [SkippableFact]
  public void OverloadBindsByteToCheapestWideningInt() {
    Assert.Equal("\"int\"", fx.Eval("TestFixture.Overloads.Pick((byte) 7)").Formatted);
  }

  [SkippableFact]
  public void GenericOverloadBindsTheDefinitionDeclaredSecond() {
    Assert.Equal("\"int:4\"", fx.Eval("TestFixture.Overloads.Tag<int>(3, 4)").Formatted);
  }

  [SkippableFact]
  public void GenericOverloadStillBindsTheDefinitionDeclaredFirst() {
    Assert.Equal(
      "\"holder\"",
      fx.Eval("TestFixture.Overloads.Tag<int>(new TestFixture.Holder(), 4)").Formatted
    );
  }

  [SkippableFact]
  public void GenericOverloadBindsPastASiblingItsConstraintRules() {
    Assert.Equal("\"class\"", fx.Eval("TestFixture.Overloads.Only<string>(3, \"x\")").Formatted);
  }

  [SkippableFact]
  public void GenericOverloadMissListsEveryDefinitionItTried() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("TestFixture.Overloads.Tag<int>(\"nope\", 4)")
    );

    Assert.Contains("Tag(Holder, Int32)", ex.Message);
    Assert.Contains("Tag(Int32, Int32)", ex.Message);
  }

  [SkippableFact]
  public void GenericOverloadMissNamesTheDefinitionItsConstraintRules() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("TestFixture.Overloads.Only<string>(new TestFixture.Holder(), \"x\")")
    );

    Assert.Contains("Only(Holder, T): cannot be instantiated over <System.String>", ex.Message);
  }

  [SkippableTheory]
  [InlineData("TestFixture.Overloads.Pick(true)", "\"object\"")]
  [InlineData("var o = 7; System.String.Concat(o, \"x\")", "\"7x\"")]
  [InlineData("System.String.Concat(7, 8)", "\"78\"")]
  public void PrimitiveArgumentBoxesForAnObjectParameter(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableTheory]
  [InlineData("\"pin\"")]
  [InlineData("new TestFixture.Point()")]
  [InlineData("7")]
  public void EnumMemberBindsBesideAnObjectParameter(string pinned) {
    var outcome = fx.Eval(
      $"var o = {pinned}; " +
      "var h = System.Runtime.InteropServices.GCHandle.Alloc(" +
      "o, System.Runtime.InteropServices.GCHandleType.Pinned); " +
      "var allocated = h.IsAllocated; h.Free(); allocated"
    );

    Assert.Equal("True", outcome.Formatted);
  }

  [SkippableFact]
  public void IntArgumentWidensToLongParameter() {
    var outcome = fx.Eval("TestFixture.Overloads.TakesLong(42)");

    Assert.Equal("42", outcome.Formatted);
    Assert.Equal("System.Int64", outcome.TypeName);
  }

  [SkippableFact]
  public void StructWriteThroughObjectFieldPersists() {
    // Persistence asserted by a follow-up read, the same way an agent would verify it.
    var outcome = fx.Eval("var h = new TestFixture.Holder(); h.P.X = 5; h.P.X");

    Assert.Equal("5", outcome.Formatted);
    Assert.Equal("System.Int32", outcome.TypeName);
  }
}
