using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.Tests;

/// <summary>
/// Object construction, casts, typeof, string interpolation, conditional access, and the deliberate
/// exclusions (control flow, LINQ).
/// </summary>
public sealed class EvalParserConstructTests {
  [Fact]
  public void ParsesNewWithArgsAndInitializers() {
    var expr = Assert.IsType<NewExpr>(
      EvalParserMemberCallTests.ParseSingle("new MyGame.Movement.Speed { m_Value = v }")
    );

    Assert.Equal("MyGame.Movement.Speed", expr.TypeName);
    Assert.Empty(expr.Args);

    var init = Assert.Single(expr.Initializers);

    Assert.Equal("m_Value", init.Name);
    Assert.Equal("v", Assert.IsType<NameExpr>(init.Value).Name);
  }

  [Fact]
  public void ParsesNewWithConstructorArguments() {
    var expr = Assert.IsType<NewExpr>(
      EvalParserMemberCallTests.ParseSingle("new System.Text.StringBuilder(16)")
    );

    Assert.Equal("System.Text.StringBuilder", expr.TypeName);
    Assert.Equal(16, Assert.IsType<LiteralExpr>(Assert.Single(expr.Args).Value).Value);
    Assert.Empty(expr.Initializers);
  }

  [Fact]
  public void ParsesACast() {
    var cast = Assert.IsType<CastExpr>(
      EvalParserMemberCallTests.ParseSingle("(MyGame.Weather.Season) 2")
    );

    Assert.Equal("MyGame.Weather.Season", cast.TypeName);
    Assert.Equal(2, Assert.IsType<LiteralExpr>(cast.Operand).Value);
  }

  [Fact]
  public void ParsesAPredefinedCast() {
    var cast = Assert.IsType<CastExpr>(EvalParserMemberCallTests.ParseSingle("(float) x"));

    Assert.Equal("System.Single", cast.TypeName);
  }

  [Fact]
  public void ParsesTypeof() {
    var expr = Assert.IsType<TypeofExpr>(
      EvalParserMemberCallTests.ParseSingle("typeof(MyGame.Units.Unit)")
    );

    Assert.Equal("MyGame.Units.Unit", expr.TypeName);
  }

  [Fact]
  public void ParsesStringInterpolation() {
    var expr = Assert.IsType<InterpolatedStringExpr>(
      EvalParserMemberCallTests.ParseSingle("$\"a={a} | b={b}\"")
    );

    Assert.Equal(4, expr.Parts.Count);
    Assert.Equal("a=", Assert.IsType<LiteralExpr>(expr.Parts[0].Value).Value);
    Assert.Equal("a", Assert.IsType<NameExpr>(expr.Parts[1].Value).Name);
    Assert.Equal(" | b=", Assert.IsType<LiteralExpr>(expr.Parts[2].Value).Value);
    Assert.All(expr.Parts, static p => Assert.Null(p.Format));
  }

  [Fact]
  public void AFormatClauseLandsOnItsInterpolationPart() {
    var expr = Assert.IsType<InterpolatedStringExpr>(
      EvalParserMemberCallTests.ParseSingle("$\"x={x:F1}\"")
    );

    Assert.Null(expr.Parts[0].Format);
    Assert.Equal("x", Assert.IsType<NameExpr>(expr.Parts[1].Value).Name);
    Assert.Equal("F1", expr.Parts[1].Format);
  }

  [Theory]
  [InlineData("$\"{x,8}\"")]
  [InlineData("$\"{x,8:F1}\"")]
  public void RejectsInterpolationAlignmentNamingAlignmentAlone(string code) {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse(code));

    Assert.Contains("unsupported: interpolation alignment", error.Message);
    Assert.DoesNotContain("format", error.Message);
  }

  [Fact]
  public void AnEscapedQuoteInAHoleSaysTheHoleTakesPlainQuotes() {
    var error = Assert.Throws<EvalParseException>(() =>
      EvalParser.Parse("$\"{d[\\\"k\\\"]}\"")
    );

    Assert.Contains("hole takes plain quotes", error.Message);
    Assert.Equal(5, error.Position);
  }

  [Fact]
  public void ParsesADefaultOfAType() {
    var expr = Assert.IsType<DefaultExpr>(
      EvalParserMemberCallTests.ParseSingle("default(MyGame.Movement.Speed)")
    );

    Assert.Equal("MyGame.Movement.Speed", expr.TypeName);
  }

  [Fact]
  public void ParsesADefaultOfAPredefinedType() {
    var expr = Assert.IsType<DefaultExpr>(EvalParserMemberCallTests.ParseSingle("default(int)"));

    Assert.Equal("System.Int32", expr.TypeName);
  }

  [Fact]
  public void TheBareDefaultLiteralPointsAtDefaultOfT() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("var a = default;"));

    Assert.Contains("unsupported: default literal (write `default(T)`)", error.Message);
  }

  [Fact]
  public void ParsesConditionalAccess() {
    var access = Assert.IsType<ConditionalAccessExpr>(
      EvalParserMemberCallTests.ParseSingle("a?.Name")
    );

    Assert.Equal("a", Assert.IsType<NameExpr>(access.Target).Name);

    var member = Assert.IsType<MemberExpr>(access.WhenNotNull);

    Assert.Equal("Name", member.Name);
    Assert.IsType<ImplicitReceiverExpr>(member.Target);
  }

  [Fact]
  public void ParsesConditionalInvocation() {
    var access = Assert.IsType<ConditionalAccessExpr>(
      EvalParserMemberCallTests.ParseSingle("a?.ToString()")
    );

    var call = Assert.IsType<CallExpr>(access.WhenNotNull);

    Assert.Equal("ToString", call.Name);
    Assert.IsType<ImplicitReceiverExpr>(call.Target);
  }

  [Theory]
  [InlineData("if (a) { b(); }")]
  [InlineData("for (;;) { }")]
  [InlineData("while (true) { }")]
  public void RejectsControlFlow(string code) {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse(code));

    Assert.Contains("unsupported", error.Message);
  }

  [Fact]
  public void AReturnSaysTheFinalExpressionIsTheResult() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("var a = 1; return a;"));

    Assert.Contains("unsupported: return statement", error.Message);
    Assert.Contains("the final expression's value is the result", error.Message);
  }

  [Fact]
  public void RejectsQueryExpressions() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("from x in xs select x"));

    Assert.Contains("unsupported", error.Message);
  }

  [Fact]
  public void UnsupportedErrorsNameTheConstruct() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("await foo"));

    Assert.Contains("unsupported: await", error.Message);
  }

  [Fact]
  public void AnInArgumentIsRejectedNamingItsKeyword() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("Foo.Bar(in a)"));

    Assert.Contains("unsupported: in argument", error.Message);
  }
}
