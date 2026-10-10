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
  public void AGenericMethodNoArgumentCanFixIsToldToWriteTheTypeArgument() {
    var message = this.Miss("TestFixture.Infer.Make()");

    Assert.Contains(
      "Make is generic and T cannot be inferred (no argument fixes it): write it, as in " +
      "static Make<T>()",
      message
    );
  }

  // A literal null and one read from the game arrive as different values.
  [SkippableTheory]
  [InlineData("null")]
  [InlineData("TestFixture.Shapes.NullText")]
  public void AGenericMethodGivenNullForItsTypedArgumentIsToldToWriteTheTypeArgument(string arg) {
    var message = this.Miss($"TestFixture.Infer.Name({arg})");

    Assert.Contains(
      "Name is generic and T cannot be inferred (argument 1 is null): write it, as in " +
      "static Name<T>(T)",
      message
    );
  }

  [SkippableFact]
  public void AGenericMethodOnlyAnOutArgumentCarriesIsToldToWriteTheTypeArgument() {
    var message = this.Miss("TestFixture.Infer.TryMake(out var made)");

    Assert.Contains(
      "TryMake is generic and T cannot be inferred (no argument fixes it): write it, as in " +
      "static TryMake<T>(T)",
      message
    );
  }

  // An unknown type argument, then one every definition's constraints refuse.
  [SkippableTheory]
  [InlineData("TestFixture.Infer.Name<No.Such.Type>(TestFixture.Infer.Count())")]
  [InlineData("TestFixture.Infer.Valued<string>(TestFixture.Infer.Count())")]
  public void AWrittenTypeArgumentThatCannotBindFailsBeforeTheArgumentsRun(string code) {
    var before = fx.Eval("TestFixture.Infer.Calls").Formatted;

    _ = this.Miss(code);

    Assert.Equal(before, fx.Eval("TestFixture.Infer.Calls").Formatted);
  }

  // A type parameter no argument can fix, alone and inside an array.
  [SkippableTheory]
  [InlineData("TestFixture.Infer.From(TestFixture.Infer.Count())")]
  [InlineData("System.Array.AsReadOnly(TestFixture.Infer.Count())")]
  public void AnUninferableCallFailsBeforeTheArgumentsRun(string code) {
    var before = fx.Eval("TestFixture.Infer.Calls").Formatted;

    Assert.Contains("(no argument fixes it)", this.Miss(code));
    Assert.Equal(before, fx.Eval("TestFixture.Infer.Calls").Formatted);
  }

  [SkippableFact]
  public void ANullArgumentReasonOutranksAnUnfixableSibling() {
    Assert.Contains(
      "T cannot be inferred (argument 1 is null)",
      this.Miss("TestFixture.Infer.Conv(null)")
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
