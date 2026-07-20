using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.Tests;

/// <summary>
/// Program shape and literal coverage: one statement sequence in, our AST out, with the final
/// expression allowed omitting its semicolon.
/// </summary>
public sealed class EvalParserLiteralTests {
  private static EvalExpr ParseSingle(string code) {
    var program = EvalParser.Parse(code);

    var statement = Assert.Single(program.Statements);

    return Assert.IsType<ExprStatement>(statement).Expression;
  }

  [Fact]
  public void ParsesAnIntegerLiteral() {
    var literal = Assert.IsType<LiteralExpr>(EvalParserLiteralTests.ParseSingle("42"));

    Assert.Equal(42, literal.Value);
  }

  [Fact]
  public void FinalExpressionNeedsNoSemicolon() {
    var program = EvalParser.Parse("1;\n2");

    Assert.Equal(2, program.Statements.Count);
  }

  [Theory]
  [InlineData("\"hi\"", "hi")]
  [InlineData("\"a\\nb\"", "a\nb")]
  [InlineData("'x'", 'x')]
  [InlineData("true", true)]
  [InlineData("false", false)]
  [InlineData("3.5", 3.5)]
  [InlineData("3.5f", 3.5f)]
  [InlineData("7L", 7L)]
  [InlineData("7u", 7u)]
  [InlineData("7ul", 7ul)]
  [InlineData("0x10", 16)]
  public void ParsesTypedLiterals(string code, object expected) {
    var literal = Assert.IsType<LiteralExpr>(EvalParserLiteralTests.ParseSingle(code));

    Assert.Equal(expected, literal.Value);
    Assert.Equal(expected.GetType(), literal.Value.GetType());
  }

  [Fact]
  public void ParsesTheNullLiteral() {
    var literal = Assert.IsType<LiteralExpr>(EvalParserLiteralTests.ParseSingle("null"));

    Assert.Null(literal.Value);
  }

  [Fact]
  public void RejectsDecimalLiterals() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("3m"));

    Assert.Contains("unsupported", error.Message);
  }

  [Fact]
  public void ReportsSyntaxErrorsWithTheirPosition() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("1 +"));

    Assert.True(error.Position >= 2);
  }

  [Fact]
  public void ClampsSyntaxErrorPositionsToTheTypedInput() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("1 +"));

    Assert.InRange(error.Position, 0, 3);
  }

  [Fact]
  public void RejectsAnEmptyProgram() {
    Assert.Throws<EvalParseException>(() => EvalParser.Parse("  "));
  }

  [Fact]
  public void RejectsACommentOnlyProgram() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("// just a note"));

    Assert.Contains("comments", error.Message);
  }

  [Fact]
  public void FinalExpressionMayCarryATrailingLineComment() {
    var program = EvalParser.Parse("1 + 2 // result");

    Assert.Single(program.Statements);
  }

  [Fact]
  public void SemicolonInsideATrailingCommentDoesNotCountAsTerminator() {
    var program = EvalParser.Parse("1 + 2 // done;");

    Assert.Single(program.Statements);
  }

  [Fact]
  public void TerminatedStatementMayCarryATrailingLineComment() {
    var program = EvalParser.Parse("1 + 2; // check this");

    Assert.Single(program.Statements);
  }

  [Fact]
  public void NodesCarryTheirSourcePosition() {
    var program = EvalParser.Parse("1;\n42");

    Assert.Equal(3, program.Statements[1].Position);
  }
}
