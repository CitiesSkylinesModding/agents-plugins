using System;
using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// What a wrong member name reports: the members the receiver does have, in the order a reader
/// scans them, and which side the name lives on when it exists on the other one.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class EvalMemberMissTests(MonoDebuggeeFixture fx) {
  private string Miss(string code) =>
    Assert.Throws<EvalFailedException>(() => fx.Eval(code)).Message;

  [SkippableFact]
  public void AReadMissListsPropertiesAheadOfFieldsAcrossTheBaseChain() {
    var message = this.Miss("new TestFixture.Miss.Dog().Bred");

    Assert.Contains("'Bred' is not a field or readable property of TestFixture.Miss.Dog", message);

    // Most-derived first within each group; the private field stays, as the way around a property
    // that cannot be read.
    Assert.Contains(
      "properties: Breed, Species, Nickname; fields: Tricks, Legs, secret",
      message
    );
  }

  [SkippableFact]
  public void NamesContainingTheMissedOneLeadTheirGroup() {
    var message = this.Miss("new TestFixture.Miss.Dog().nick");

    Assert.Contains("properties: Nickname, Breed, Species; fields:", message);

    // An auto-property's backing field has a name no expression can spell, and an indexer's
    // accessor is no property read.
    Assert.DoesNotContain("BackingField", message);
    Assert.DoesNotContain("Item", message);
  }

  [SkippableFact]
  public void AStaticMemberReadThroughAnInstanceSaysWhichTypeReadsIt() {
    var message = this.Miss("new TestFixture.Miss.Dog().Population");

    // Named off the declaring type: a static field is looked up on the type written, not on its
    // bases.
    Assert.Contains("'Population' is a static member", message);
    Assert.Contains("TestFixture.Miss.Animal.Population", message);
  }

  [SkippableFact]
  public void AStaticFieldReadThroughADerivedTypeNamesTheTypeDeclaringIt() {
    Assert.Contains(
      "'Population' is a static field of TestFixture.Miss.Animal, and a derived type's name " +
      "does not reach it: read it as TestFixture.Miss.Animal.Population",
      this.Miss("TestFixture.Miss.Dog.Population")
    );
  }

  [SkippableFact]
  public void AnInstanceMemberReadThroughATypeSaysItNeedsAValue() {
    var message = this.Miss("TestFixture.Miss.Dog.Tricks");

    Assert.Contains("'Tricks' is an instance member of TestFixture.Miss.Dog", message);

    // A property too: its getter exists on the type, with no receiver to run on.
    Assert.Contains(
      "'Breed' is an instance member of TestFixture.Miss.Dog",
      this.Miss("TestFixture.Miss.Dog.Breed")
    );

    Assert.Contains(
      "'Nickname' is an instance member of TestFixture.Miss.Dog",
      this.Miss("TestFixture.Miss.Dog.Nickname = \"rex\"")
    );
  }

  [SkippableFact]
  public void AStaticReadMissListsTheStaticMembers() {
    var message = this.Miss("TestFixture.Miss.Animal.Populaton");

    Assert.Contains(
      "'Populaton' is not a static field or readable property of TestFixture.Miss.Animal",
      message
    );

    Assert.Contains("static properties: Kingdom; static fields: Population", message);
  }

  [SkippableFact]
  public void AWriteMissListsTheWritableMembers() {
    var message = this.Miss("var dog = new TestFixture.Miss.Dog(); dog.Bred = \"x\"");

    Assert.Contains("'Bred' is not a writable field or property of TestFixture.Miss.Dog", message);

    // Breed and Species have no setter, so they are not offered to a write.
    Assert.Contains("writable properties: Nickname; fields: Tricks, Legs, secret", message);
  }

  [SkippableFact]
  public void AStaticWriteMissReportsTheMemberRatherThanItsSetter() {
    var message = this.Miss("TestFixture.Miss.Animal.Populaton = 4");

    Assert.DoesNotContain("set_Populaton", message);

    Assert.Contains(
      "'Populaton' is not a writable static field or property of TestFixture.Miss.Animal",
      message
    );

    Assert.Contains("static fields: Population", message);
  }

  [SkippableFact]
  public void AMissOnAClientSideValueListsItsMembers() {
    var message = this.Miss("\"abc\".Lenght");

    Assert.Contains("'Lenght' is not a member of System.String; properties: Length", message);

    // The string indexer is a property to reflection and no member read to the evaluator.
    Assert.DoesNotContain("Chars", message);
  }

  [SkippableFact]
  public void AMissingComponentFieldListsFieldsAlone() {
    var ex = Assert.Throws<InvalidOperationException>(() =>
      fx.WithInvoker(inv => Ecs.RequireField(inv.ResolveType("TestFixture.Miss.Paw"), "Toes"))
    );

    Assert.Equal(
      "'Toes' is not a field of TestFixture.Miss.Paw; fields: Claws, IsLeft",
      ex.Message
    );
  }
}
