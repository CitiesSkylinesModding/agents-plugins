using System;
using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// What a wrong type name reports: which part of the name failed and why, and the full names the
/// caller could have meant.
/// The catalog is the suite's shared one and only grows, so every expectation anchors on the
/// fixture's own namespaces.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class EvalTypeMissTests(MonoDebuggeeFixture fx) {
  private EvalFailedException Miss(string code, bool catalog = true) =>
    Assert.Throws<EvalFailedException>(() => fx.Eval(code, catalog: catalog));

  [SkippableFact]
  public void ATypeUnderTheWrongNamespaceIsToldTheNamespaceExists() {
    var message = this.Miss("typeof(TestFixture.Twin)").Message;

    Assert.Contains(
      "type 'TestFixture.Twin' not found: namespace 'TestFixture' exists but holds no type or " +
      "namespace named 'Twin'",
      message
    );

    // Both namesakes, ordinal by full name, ahead of the type that only contains the name.
    Assert.Contains("did you mean: TestFixture.Miss.Far.Twin, TestFixture.Miss.Twin", message);

    Assert.True(
      message.IndexOf("TestFixture.Miss.TwinTower", StringComparison.Ordinal) >
      message.IndexOf("TestFixture.Miss.Twin,", StringComparison.Ordinal)
    );
  }

  [SkippableFact]
  public void AChainNamesTheSegmentThatFailedRatherThanItsRoot() {
    var ex = this.Miss("TestFixture.Mis.Twin.Foo");

    Assert.Contains(
      "cannot resolve 'TestFixture.Mis': namespace 'TestFixture' exists but holds no type or " +
      "namespace named 'Mis'",
      ex.Message
    );

    // A later segment naming a type exactly is still offered: the middle one was a namespace typo.
    Assert.Contains("TestFixture.Miss.Far.Twin, TestFixture.Miss.Twin", ex.Message);
  }

  [SkippableFact]
  public void ANamespaceReadAsAValueIsCalledANamespace() {
    Assert.Contains(
      "'TestFixture.Miss' is a namespace, not a type or a value",
      this.Miss("TestFixture.Miss").Message
    );
  }

  [SkippableFact]
  public void ARootThatIsNothingIsToldSo() {
    Assert.Contains(
      "cannot resolve 'Nope': 'Nope' is not a namespace, a type or a local",
      this.Miss("Nope.Twin.Foo").Message
    );
  }

  [SkippableFact]
  public void ABareTypeNameIsToldToQualifyAndShownTheFullName() {
    var inTypePosition = this.Miss("typeof(Dog)").Message;

    Assert.Contains("type 'Dog' not found: type names must be fully qualified", inTypePosition);
    Assert.Contains("did you mean: TestFixture.Miss.Dog", inTypePosition);

    var inAChain = this.Miss("Dog.Population").Message;

    Assert.Contains("cannot resolve 'Dog': type names must be fully qualified", inAChain);
    Assert.Contains("did you mean: TestFixture.Miss.Dog", inAChain);
  }

  [SkippableFact]
  public void AQualifiedMissDoesNotBlameQualification() {
    Assert.DoesNotContain("fully qualified", this.Miss("typeof(TestFixture.Twin)").Message);
  }

  [SkippableFact]
  public void ABareNameMatchingNothingListsWhatIsInScope() {
    var message = this.Miss("var zzyzxPup = 1; zzyzxPupp").Message;

    Assert.Contains("cannot resolve 'zzyzxPupp': not a local, a builtin or a type", message);
    Assert.Contains("in scope: zzyzxPup, em, world, entity(), _", message);

    // A local is usually followed by a member, so the longer chain is told the same.
    Assert.Contains(
      "'zzyzxPupp' is not a namespace, a type or a local; in scope: zzyzxPup, em, world",
      this.Miss("var zzyzxPup = 1; zzyzxPupp.Foo").Message
    );
  }

  [SkippableFact]
  public void ANameDifferingFromATypeByCaseAloneIsNotBlamedOnQualification() {
    var message = this.Miss("dog").Message;

    // A stale local far more often than a type: the type is still suggested.
    Assert.Contains("cannot resolve 'dog': not a local, a builtin or a type; in scope:", message);
    Assert.Contains("did you mean: TestFixture.Miss.Dog", message);

    // Where only a type can be written, no local was meant and qualifying is the fix.
    Assert.Contains(
      "type 'dog' not found: type names must be fully qualified",
      this.Miss("typeof(dog)").Message
    );
  }

  [SkippableFact]
  public void ACaseSlipIsSuggestedRatherThanAccepted() {
    Assert.Contains(
      "did you mean: TestFixture.Miss.Dog",
      this.Miss("typeof(testfixture.miss.dog)").Message
    );
  }

  [SkippableFact]
  public void ANestedTypeIsSuggestedUnderTheSpellingTheEvaluatorResolves() {
    var message = this.Miss("typeof(TestFixture.Cage)").Message;

    Assert.Contains("did you mean: TestFixture.Miss.Kennel.Cage", message);

    // The suggestion is only worth printing if pasting it works.
    Assert.Equal(
      "TestFixture.Miss.Kennel+Cage",
      fx.Eval("typeof(TestFixture.Miss.Kennel.Cage).FullName").Formatted.Trim('"')
    );
  }

  [SkippableFact]
  public void AGuessMatchingNothingSaysSoAndPointsAtTheSearch() {
    var message = this.Miss("typeof(TestFixture.Zzyzx)").Message;

    Assert.Contains("no loaded type is named 'Zzyzx' or contains it", message);
    Assert.Contains("find_types with search", message);
  }

  [SkippableFact]
  public void WithoutACatalogATypeMissKeepsItsPlainWording() {
    Assert.Equal(
      "type 'TestFixture.Twin' not found (names must be fully qualified)",
      this.Miss("typeof(TestFixture.Twin)", catalog: false).Message
    );

    Assert.StartsWith(
      "cannot resolve 'Nope': not a local, a builtin (em, world, entity(), _), or",
      this.Miss("Nope.Twin.Foo", catalog: false).Message
    );
  }

  [SkippableFact]
  public void ABreakpointConditionKeepsThePlainWording() {
    try {
      _ = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.Ticker",
          MethodName = "Tick",
          Condition = "noSuchLocal == 1"
        }
      );

      // A condition runs on the event pump, where the catalog's harvest and its invokes must
      // never be reached.
      Assert.Contains(
        "a builtin (em, world, entity(), _)",
        fx.Debug.WaitForPause(TimeSpan.FromSeconds(10))?.ConditionError
      );

      // Evaluating in the paused frame has the catalog, and lists the frame's own names.
      Assert.Contains(
        "label, n",
        Assert.Throws<EvalFailedException>(() => fx.DebugEval("labl")).Message
      );
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableFact]
  public void ATypeNamedToAToolIsDiagnosedUnderTheSpellingToolsTake() {
    var ex = Assert.Throws<InvalidOperationException>(() =>
      fx.WithCatalog(c => c.ResolveNamed("TestFixture.Cage"))
    );

    Assert.Contains(
      "type 'TestFixture.Cage' not found: namespace 'TestFixture' exists but holds no type or " +
      "namespace named 'Cage'; did you mean: TestFixture.Miss.Kennel+Cage",
      ex.Message
    );
  }

  [SkippableFact]
  public void TheDiagnosingResolveAnswersARightNameLikeThePlainOne() {
    var type = fx.WithCatalog(c =>
      c.ResolveNamed("testfixture.miss.dog")
    );

    Assert.Equal("TestFixture.Miss.Dog", type.FullName);
  }
}
