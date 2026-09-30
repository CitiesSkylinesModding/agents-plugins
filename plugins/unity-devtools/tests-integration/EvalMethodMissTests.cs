using System;
using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// What a wrong method call reports: what the receiver's type does have under that name, which
/// side it lives on, whether it wants a type argument, and the static form of an extension method.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class EvalMethodMissTests(MonoDebuggeeFixture fx) {
  private const string Dog = "new TestFixture.Miss.Dog()";

  private string Miss(string code, bool catalog = true) =>
    Assert.Throws<EvalFailedException>(() => fx.Eval(code, catalog: catalog)).Message;

  [SkippableFact]
  public void AWrongArgumentCountListsTheSignaturesThatNameHas() {
    var message = this.Miss($"{EvalMethodMissTests.Dog}.Speak(1, 2)");

    Assert.Contains(
      "no Speak of TestFixture.Miss.Dog takes 2 argument(s); it has: Speak(), Speak(Int32)",
      message
    );
  }

  [SkippableFact]
  public void AGenericMethodCalledBareIsToldItWantsATypeArgument() {
    var message = this.Miss($"{EvalMethodMissTests.Dog}.Echo(1)");

    Assert.Contains(
      "Echo is generic and its type argument is not inferred: write it, as in Echo<T>(T)",
      message
    );
  }

  [SkippableFact]
  public void AGenericCallOnANonGenericMethodListsItsSignatures() {
    var message = this.Miss($"{EvalMethodMissTests.Dog}.Bark<System.Int32>()");

    Assert.Contains("no Bark of TestFixture.Miss.Dog takes 1 type argument(s)", message);
    Assert.Contains("it has: Bark()", message);
  }

  [SkippableFact]
  public void AnInstanceMethodCalledOnATypeSaysItNeedsAValue() {
    var message = this.Miss("TestFixture.Miss.Dog.Bark()");

    Assert.Contains(
      "Bark is an instance method of TestFixture.Miss.Dog: call it on a value of that type",
      message
    );
  }

  [SkippableFact]
  public void AnUnknownNameListsContainingNamesThenTheTypesOwnAheadOfInheritedOnes() {
    var message = this.Miss($"{EvalMethodMissTests.Dog}.Spea()");

    Assert.Contains("TestFixture.Miss.Dog has no method 'Spea'", message);

    // The containing name leads; then the most-derived type's own, then its base's, and what
    // every object inherits last.
    Assert.Contains("methods: Speak, Bark, Reveal, Echo, ", message);

    Assert.True(
      message.IndexOf("ToString", StringComparison.Ordinal) >
      message.IndexOf("Echo", StringComparison.Ordinal)
    );
  }

  [SkippableFact]
  public void AnUnknownStaticNameListsTheStaticMethods() {
    var message = this.Miss("TestFixture.Miss.Animal.Clasify(4)");

    Assert.Contains("TestFixture.Miss.Animal has no static method 'Clasify'", message);
    Assert.Contains("static methods: Classify", message);
    Assert.DoesNotContain("Speak", message);

    // No value to pass as a first argument, so no extension form to offer.
    Assert.DoesNotContain("extension", message);
  }

  [SkippableFact]
  public void AnExtensionMethodCalledThroughAnInstanceIsShownItsStaticCall() {
    var message = this.Miss("var rex = new TestFixture.Miss.Dog(); rex.Fetch(2)");

    Assert.Contains(
      "Fetch is an extension method: call it as TestFixture.Miss.DogExtensions.Fetch(rex, ...)",
      message
    );
  }

  [SkippableFact]
  public void AnExtensionMethodTheLookupCannotFindIsToldTheStaticShape() {
    var message = this.Miss($"{EvalMethodMissTests.Dog}.Roll()");

    Assert.Contains("TestFixture.Miss.Dog has no method 'Roll'", message);

    Assert.Contains(
      "if Roll is an extension method, call its static form, DeclaringClass.Roll(value)",
      message
    );
  }

  [SkippableFact]
  public void WithoutACatalogAnExtensionMethodIsToldTheStaticShape() {
    var message = this.Miss("var rex = new TestFixture.Miss.Dog(); rex.Fetch(2)", catalog: false);

    Assert.DoesNotContain("DogExtensions", message);
    Assert.Contains("call its static form, DeclaringClass.Fetch(rex, ...)", message);
  }
}
