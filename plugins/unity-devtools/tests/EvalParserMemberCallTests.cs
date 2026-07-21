using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.Tests;

/// <summary>
/// Identifiers, member chains, method calls (including explicit generic type arguments), "out"
/// arguments, and indexers.
/// </summary>
public sealed class EvalParserMemberCallTests {
  internal static EvalExpr ParseSingle(string code) {
    var program = EvalParser.Parse(code);

    var statement = Assert.Single(program.Statements);

    return Assert.IsType<ExprStatement>(statement).Expression;
  }

  [Fact]
  public void ParsesABareIdentifier() {
    var name = Assert.IsType<NameExpr>(EvalParserMemberCallTests.ParseSingle("em"));

    Assert.Equal("em", name.Name);
  }

  [Fact]
  public void ParsesThisAsANameRoot() {
    var name = Assert.IsType<NameExpr>(EvalParserMemberCallTests.ParseSingle("this"));

    Assert.Equal("this", name.Name);
  }

  [Fact]
  public void ParsesAMemberChainRootedAtThis() {
    var chain = Assert.IsType<MemberExpr>(EvalParserMemberCallTests.ParseSingle("this.gameMode"));

    Assert.Equal("gameMode", chain.Name);

    var root = Assert.IsType<NameExpr>(chain.Target);

    Assert.Equal("this", root.Name);
  }

  [Fact]
  public void ParsesAnAssignmentThroughThis() {
    var assignment = Assert.IsType<AssignExpr>(
      EvalParserMemberCallTests.ParseSingle("this.m_State = 1")
    );

    var target = Assert.IsType<MemberExpr>(assignment.Target);

    Assert.Equal("m_State", target.Name);
    Assert.Equal("this", Assert.IsType<NameExpr>(target.Target).Name);
  }

  [Fact]
  public void ParsesADottedMemberChain() {
    var chain = Assert.IsType<MemberExpr>(
      EvalParserMemberCallTests.ParseSingle("Game.SceneFlow.GameManager.instance")
    );

    Assert.Equal("instance", chain.Name);

    var gameManager = Assert.IsType<MemberExpr>(chain.Target);

    Assert.Equal("GameManager", gameManager.Name);

    var sceneFlow = Assert.IsType<MemberExpr>(gameManager.Target);

    Assert.Equal("SceneFlow", sceneFlow.Name);
    Assert.Equal("Game", Assert.IsType<NameExpr>(sceneFlow.Target).Name);
  }

  [Fact]
  public void ParsesAGenericMethodCallWithAQualifiedTypeArgument() {
    var call = Assert.IsType<CallExpr>(
      EvalParserMemberCallTests.ParseSingle("em.GetComponentData<Game.Citizens.HouseholdMember>(e)")
    );

    Assert.Equal("GetComponentData", call.Name);
    Assert.Equal("em", Assert.IsType<NameExpr>(call.Target).Name);
    Assert.Equal(["Game.Citizens.HouseholdMember"], call.TypeArgs);

    var arg = Assert.Single(call.Args);

    Assert.Equal(ArgMode.Plain, arg.Mode);
    Assert.Equal("e", Assert.IsType<NameExpr>(arg.Value).Name);
  }

  [Fact]
  public void MapsPredefinedTypeArgumentsToTheirSystemNames() {
    var call = Assert.IsType<CallExpr>(EvalParserMemberCallTests.ParseSingle("x.Foo<int>()"));

    Assert.Equal(["System.Int32"], call.TypeArgs);
  }

  [Fact]
  public void ParsesABareCallAsTargetless() {
    var call = Assert.IsType<CallExpr>(EvalParserMemberCallTests.ParseSingle("entity(123, 1)"));

    Assert.Null(call.Target);
    Assert.Equal("entity", call.Name);
    Assert.Equal(2, call.Args.Count);
  }

  [Fact]
  public void ParsesACallOnACallResult() {
    var outer = Assert.IsType<CallExpr>(
      EvalParserMemberCallTests.ParseSingle("world.GetExistingSystemManaged(t).GetName()")
    );

    Assert.Equal("GetName", outer.Name);
    Assert.IsType<CallExpr>(outer.Target);
  }

  [Fact]
  public void ParsesOutVarAndOutIdentifierArguments() {
    var call = Assert.IsType<CallExpr>(
      EvalParserMemberCallTests.ParseSingle("Foo.Bar(out var a, out b)")
    );

    Assert.Equal(ArgMode.OutVar, call.Args[0].Mode);
    Assert.Equal("a", Assert.IsType<NameExpr>(call.Args[0].Value).Name);
    Assert.Equal(ArgMode.Out, call.Args[1].Mode);
    Assert.Equal("b", Assert.IsType<NameExpr>(call.Args[1].Value).Name);
  }

  [Fact]
  public void ParsesAnIndexerAccess() {
    var index = Assert.IsType<IndexExpr>(EvalParserMemberCallTests.ParseSingle("arr[0]"));

    Assert.Equal("arr", Assert.IsType<NameExpr>(index.Target).Name);
    Assert.Equal(0, Assert.IsType<LiteralExpr>(Assert.Single(index.Args)).Value);
  }

  [Fact]
  public void RejectsRefArguments() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("Foo.Bar(ref x)"));

    Assert.Contains("unsupported", error.Message);
  }

  [Fact]
  public void RejectsNamedArguments() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("Foo.Bar(x: 1)"));

    Assert.Contains("unsupported", error.Message);
  }

  [Fact]
  public void RejectsLambdas() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("items.Where(x => x)"));

    Assert.Contains("unsupported", error.Message);
    Assert.Contains("lambda", error.Message);
  }

  [Fact]
  public void RejectsNameof() {
    var error = Assert.Throws<EvalParseException>(() => EvalParser.Parse("nameof(x)"));

    Assert.Contains("nameof", error.Message);
  }
}
