using UnityDevtools.Mcp;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// The <c>find_types</c> member listing over the fixture's live types, shaped by the tool itself.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class TypeToolsTests(MonoDebuggeeFixture fx) {
  [SkippableFact]
  public void MembersListStaticsMarkedStaticBesideUnmarkedInstanceMembers() {
    var animal = fx.WithInvoker(inv =>
      TypeTools.Describe(
        inv.ResolveType("TestFixture.Miss.Animal"),
        "TestFixture.Miss.Animal",
        "UnityDevtools.TestFixture",
        members: true
      )
    );

    Assert.NotNull(animal.Fields);
    Assert.NotNull(animal.Properties);
    Assert.NotNull(animal.Methods);

    Assert.Contains("static Population: System.Int32", animal.Fields);
    Assert.Contains("Legs: System.Int32", animal.Fields);

    Assert.Contains("static Kingdom: System.String", animal.Properties);
    Assert.Contains("Species: System.String", animal.Properties);

    Assert.Contains("static String Classify(Int32 legs)", animal.Methods);
    Assert.Contains("String Speak()", animal.Methods);
  }
}
