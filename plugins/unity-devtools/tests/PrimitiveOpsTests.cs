using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.Tests;

/// <summary>
/// Client-side operator semantics on unwrapped primitive values; expected values and result types
/// follow the C# language rules (numeric promotion, string concatenation, bool logic).
/// </summary>
public sealed class PrimitiveOpsTests {
  [Theory]
  [InlineData("+", 2, 3, 5)]
  [InlineData("-", 2, 3, -1)]
  [InlineData("*", 4, 3, 12)]
  [InlineData("/", 7, 2, 3)]
  [InlineData("%", 7, 2, 1)]
  [InlineData("+", 2, 3.5, 5.5)]
  [InlineData("+", 2.5f, 1.5f, 4f)]
  [InlineData("<", 2, 3, true)]
  [InlineData(">=", 3, 3, true)]
  [InlineData("==", 3, 3, true)]
  [InlineData("!=", 3, 3, false)]
  [InlineData("&", 6, 3, 2)]
  [InlineData("|", 6, 3, 7)]
  [InlineData("^", 6, 3, 5)]
  [InlineData("<<", 1, 4, 16)]
  [InlineData(">>", 16, 4, 1)]
  [InlineData("&", true, false, false)]
  [InlineData("|", true, false, true)]
  [InlineData("+", "a", "b", "ab")]
  [InlineData("+", "n=", 3, "n=3")]
  [InlineData("==", "a", "a", true)]
  [InlineData("==", "a", "b", false)]
  public void AppliesBinaryOperatorsWithCSharpSemantics(
    string op,
    object left,
    object right,
    object expected
  ) {
    var result = PrimitiveOps.Binary(op, left, right);

    Assert.Equal(expected, result);
    Assert.Equal(expected.GetType(), result.GetType());
  }

  [Fact]
  public void PromotesSmallIntegralsToInt() {
    var result = PrimitiveOps.Binary("+", (byte) 1, (byte) 2);

    Assert.IsType<int>(result);
    Assert.Equal(3, result);
  }

  [Fact]
  public void PromotesIntAndLongToLong() {
    var result = PrimitiveOps.Binary("+", 1, 2L);

    Assert.IsType<long>(result);
  }

  [Theory]
  [InlineData("-", 3, -3)]
  [InlineData("+", 3, 3)]
  [InlineData("!", true, false)]
  [InlineData("~", 0, -1)]
  public void AppliesUnaryOperators(string op, object operand, object expected) {
    Assert.Equal(expected, PrimitiveOps.Unary(op, operand));
  }

  [Fact]
  public void ComparesNullWithCSharpEqualitySemantics() {
    Assert.Equal(true, PrimitiveOps.Binary("==", null, null));
    Assert.Equal(false, PrimitiveOps.Binary("==", "a", null));
    Assert.Equal(true, PrimitiveOps.Binary("!=", "a", null));
  }

  [Fact]
  public void ConcatenatesNullAsEmptyInStringAddition() {
    Assert.Equal("a", PrimitiveOps.Binary("+", "a", null));
  }

  [Fact]
  public void RejectsAMeaninglessOperandCombination() {
    var error = Assert.Throws<InvalidOperationException>(() => PrimitiveOps.Binary("-", "a", true));

    Assert.Contains("-", error.Message);
  }

  // C# accepts a non-negative signed integer constant against a ulong by converting it; the
  // runtime binder alone (no constants) would throw "no common type".
  [Fact]
  public void ComparesUlongAgainstNonNegativeSignedOperands() {
    Assert.Equal(false, PrimitiveOps.Binary("==", (ulong) 5, 0));
    Assert.Equal(true, PrimitiveOps.Binary("!=", (ulong) 5, 0));
    Assert.Equal(true, PrimitiveOps.Binary(">", (ulong) 5, 3));
    Assert.Equal(true, PrimitiveOps.Binary("<", 3, (ulong) 5));
  }

  [Fact]
  public void CombinesUlongFlagsWithSignedMasks() {
    Assert.Equal((ulong) 4, PrimitiveOps.Binary("&", (ulong) 6, 4));
    Assert.Equal((ulong) 7, PrimitiveOps.Binary("|", 3, (ulong) 5));
  }

  [Fact]
  public void ShiftCountsStayInt() {
    Assert.Equal((ulong) 16, PrimitiveOps.Binary("<<", (ulong) 1, 4));
  }

  [Fact]
  public void RejectsUlongAgainstANegativeOperand() {
    Assert.Throws<InvalidOperationException>(() => PrimitiveOps.Binary("==", (ulong) 5, -1));
  }

  // The widening table backs overload binding's second pass; an exact match is the strict first
  // pass, so identity deliberately does not count as widening.
  [Theory]
  [InlineData(typeof(byte), typeof(int), true)]
  [InlineData(typeof(sbyte), typeof(long), true)]
  [InlineData(typeof(short), typeof(double), true)]
  [InlineData(typeof(char), typeof(ushort), true)]
  [InlineData(typeof(int), typeof(long), true)]
  [InlineData(typeof(uint), typeof(ulong), true)]
  [InlineData(typeof(ulong), typeof(float), true)]
  [InlineData(typeof(float), typeof(double), true)]
  [InlineData(typeof(long), typeof(int), false)]
  [InlineData(typeof(double), typeof(float), false)]
  [InlineData(typeof(ulong), typeof(long), false)]
  [InlineData(typeof(int), typeof(uint), false)]
  [InlineData(typeof(int), typeof(int), false)]
  [InlineData(typeof(bool), typeof(int), false)]
  [InlineData(typeof(ushort), typeof(char), false)]
  public void WidensOnlyAlongCSharpImplicitNumericConversions(Type from, Type to, bool expected) {
    Assert.Equal(expected, PrimitiveOps.CanWiden(from, to));
  }
}
