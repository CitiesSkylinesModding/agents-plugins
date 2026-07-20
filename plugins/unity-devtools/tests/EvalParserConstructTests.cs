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
      EvalParserMemberCallTests.ParseSingle("new Game.Citizens.HouseholdMember { m_Household = h }")
    );

    Assert.Equal("Game.Citizens.HouseholdMember", expr.TypeName);
    Assert.Empty(expr.Args);

    var init = Assert.Single(expr.Initializers);

    Assert.Equal("m_Household", init.Name);
    Assert.Equal("h", Assert.IsType<NameExpr>(init.Value).Name);
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
      EvalParserMemberCallTests.ParseSingle("(Game.Simulation.Season) 2")
    );

    Assert.Equal("Game.Simulation.Season", cast.TypeName);
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
      EvalParserMemberCallTests.ParseSingle("typeof(Game.Citizens.Citizen)")
    );

    Assert.Equal("Game.Citizens.Citizen", expr.TypeName);
  }

  [Fact]
  public void ParsesStringInterpolation() {
    var expr = Assert.IsType<InterpolatedStringExpr>(
      EvalParserMemberCallTests.ParseSingle("$\"a={a} | b={b}\"")
    );

    Assert.Equal(4, expr.Parts.Count);
    Assert.Equal("a=", Assert.IsType<LiteralExpr>(expr.Parts[0]).Value);
    Assert.Equal("a", Assert.IsType<NameExpr>(expr.Parts[1]).Name);
    Assert.Equal(" | b=", Assert.IsType<LiteralExpr>(expr.Parts[2]).Value);
  }

  [Fact]
  public void RejectsInterpolationFormatSpecifiers() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("$\"{x:F2}\""));

    Assert.Contains("unsupported", error.Message);
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
  [InlineData("return 1;")]
  public void RejectsControlFlow(string code) {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse(code));

    Assert.Contains("unsupported", error.Message);
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
}
