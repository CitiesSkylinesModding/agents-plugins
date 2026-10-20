using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// What a read, a call, or an indexer on a null receiver reports: the operation, and the receiver
/// as written when it is a named chain.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class EvalNullReceiverTests(MonoDebuggeeFixture fx) {
  [SkippableTheory]
  [InlineData(
    "TestFixture.Shapes.NullText.Length",
    "cannot read 'Length' on null (TestFixture.Shapes.NullText)"
  )]
  [InlineData(
    "TestFixture.Shapes.NullText.Trim()",
    "cannot call 'Trim' on null (TestFixture.Shapes.NullText)"
  )]
  [InlineData("TestFixture.Shapes.NullText[0]", "cannot index null (TestFixture.Shapes.NullText)")]
  [InlineData(
    "var hd = TestFixture.Shapes.NullText; hd.Length",
    "cannot read 'Length' on null (hd)"
  )]
  [InlineData(
    "var hd = TestFixture.Shapes.NullText; hd.Trim()",
    "cannot call 'Trim' on null (hd)"
  )]
  [InlineData("var hd = TestFixture.Shapes.NullText; hd[0]", "cannot index null (hd)")]
  [InlineData(
    "var hd = TestFixture.Shapes.NullText; hd.Length.X = 1",
    "cannot read 'Length' on null (hd)"
  )]
  [InlineData("var hd = TestFixture.Shapes.NullText; hd[0] = 'a'", "cannot index null (hd)")]
  [InlineData("var hd = TestFixture.Shapes.NullText; hd[0].X = 1", "cannot index null (hd)")]
  public void ANamedNullReceiverIsNamed(string code, string expected) {
    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval(code));

    Assert.Equal(expected, ex.Message);
  }

  [SkippableTheory]
  [InlineData(".Length", "cannot read 'Length' on null")]
  [InlineData(".Trim()", "cannot call 'Trim' on null")]
  [InlineData("[0]", "cannot index null")]
  public void AnUnnamedNullReceiverIsNotNamed(string access, string expected) {
    // A call result: an unset variable's value comes back null.
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval($"System.Environment.GetEnvironmentVariable(\"UNSET_NULL_PROBE\"){access}")
    );

    Assert.Equal(expected, ex.Message);
  }
}
