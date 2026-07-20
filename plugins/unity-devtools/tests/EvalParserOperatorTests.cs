using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.Tests;

/// <summary>
/// Operators, precedence, the generic-vs.-less-than ambiguity, and assignments.
/// Precedence itself is Roslyn's; these tests pin that the translated AST reflects it.
/// </summary>
public sealed class EvalParserOperatorTests {
  [Theory]
  [InlineData("1 + 2", "+")]
  [InlineData("1 - 2", "-")]
  [InlineData("1 * 2", "*")]
  [InlineData("1 / 2", "/")]
  [InlineData("1 % 2", "%")]
  [InlineData("1 < 2", "<")]
  [InlineData("1 <= 2", "<=")]
  [InlineData("1 > 2", ">")]
  [InlineData("1 >= 2", ">=")]
  [InlineData("1 == 2", "==")]
  [InlineData("1 != 2", "!=")]
  [InlineData("a && b", "&&")]
  [InlineData("a || b", "||")]
  [InlineData("1 & 2", "&")]
  [InlineData("1 | 2", "|")]
  [InlineData("1 ^ 2", "^")]
  [InlineData("1 << 2", "<<")]
  [InlineData("1 >> 2", ">>")]
  [InlineData("a ?? b", "??")]
  public void ParsesBinaryOperators(string code, string op) {
    var binary = Assert.IsType<BinaryExpr>(EvalParserMemberCallTests.ParseSingle(code));

    Assert.Equal(op, binary.Op);
  }

  [Theory]
  [InlineData("-x", "-")]
  [InlineData("+x", "+")]
  [InlineData("!x", "!")]
  [InlineData("~x", "~")]
  public void ParsesUnaryOperators(string code, string op) {
    var unary = Assert.IsType<UnaryExpr>(EvalParserMemberCallTests.ParseSingle(code));

    Assert.Equal(op, unary.Op);
  }

  [Fact]
  public void MultiplicationBindsTighterThanAddition() {
    var sum = Assert.IsType<BinaryExpr>(EvalParserMemberCallTests.ParseSingle("1 + 2 * 3"));

    Assert.Equal("+", sum.Op);
    Assert.Equal("*", Assert.IsType<BinaryExpr>(sum.Right).Op);
  }

  [Fact]
  public void ParenthesesOverridePrecedence() {
    var product = Assert.IsType<BinaryExpr>(EvalParserMemberCallTests.ParseSingle("(1 + 2) * 3"));

    Assert.Equal("*", product.Op);
    Assert.Equal("+", Assert.IsType<BinaryExpr>(product.Left).Op);
  }

  [Fact]
  public void GenericCallWinsOverComparisonWhenFollowedByParens() {
    // The classic ambiguity: with the trailing (d), C# commits to a generic invocation.
    var call = Assert.IsType<CallExpr>(EvalParserMemberCallTests.ParseSingle("f(a<b, c>(d))"));

    var inner = Assert.IsType<CallExpr>(Assert.Single(call.Args).Value);

    Assert.Equal("a", inner.Name);
    Assert.Equal(["b", "c"], inner.TypeArgs);
  }

  [Fact]
  public void ComparisonStaysAComparisonWithoutTheTrailingParens() {
    var less = Assert.IsType<BinaryExpr>(EvalParserMemberCallTests.ParseSingle("a < b"));

    Assert.Equal("<", less.Op);
  }

  [Fact]
  public void ParsesATernary() {
    var conditional = Assert.IsType<ConditionalExpr>(
      EvalParserMemberCallTests.ParseSingle("a ? 1 : 2")
    );

    Assert.Equal(1, Assert.IsType<LiteralExpr>(conditional.WhenTrue).Value);
    Assert.Equal(2, Assert.IsType<LiteralExpr>(conditional.WhenFalse).Value);
  }

  [Fact]
  public void ParsesAnAssignmentStatement() {
    var assign =
      Assert.IsType<AssignExpr>(EvalParserMemberCallTests.ParseSingle("copy.m_Flags = 3"));

    var target = Assert.IsType<MemberExpr>(assign.Target);

    Assert.Equal("m_Flags", target.Name);
    Assert.Equal(3, Assert.IsType<LiteralExpr>(assign.Value).Value);
  }

  [Fact]
  public void ParsesAnIndexerAssignment() {
    var assign = Assert.IsType<AssignExpr>(EvalParserMemberCallTests.ParseSingle("buf[0] = x"));

    Assert.IsType<IndexExpr>(assign.Target);
  }

  [Fact]
  public void RejectsCompoundAssignment() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("x += 1"));

    Assert.Contains("unsupported", error.Message);
  }

  [Fact]
  public void RejectsIncrement() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("x++"));

    Assert.Contains("unsupported", error.Message);
  }
}
