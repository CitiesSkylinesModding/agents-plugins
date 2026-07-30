using System;
using System.Linq;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// The session-scoped type catalog against a real debuggee: harvesting the domain's type names,
/// searching them client-side, and staying level with a debuggee that loads more.
/// The catalog is shared with the whole suite and only ever GROWS (a sibling test loads an assembly
/// into the one debuggee), so every pattern here anchors on the fixture's own namespace: a bare
/// fragment would sooner or later also match something in mscorlib.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class TypeCatalogTests(MonoDebuggeeFixture fx) {
  /// <summary>Everything in the assembly whose enumeration fails, and nothing else.</summary>
  private const string PartlyLoadable = "^TestFixture[.]Broken[.]";

  [SkippableFact]
  public void SearchingAFragmentFindsTheTypeAndNamesItsAssembly() {
    var found = fx.WithCatalog(c => c.Search("^TestFixture[.]Overloads$", 50));

    var hit = Assert.Single(found.Hits);

    Assert.Equal("TestFixture.Overloads", hit.FullName);
    Assert.Equal("UnityDevtools.TestFixture", hit.Assembly);
    Assert.NotNull(hit.Type);
  }

  [SkippableFact]
  public void SearchingIgnoresCaseByDefault() {
    var found = fx.WithCatalog(c => c.Search("^testfixture[.]overloads$", 50));

    Assert.Equal("TestFixture.Overloads", Assert.Single(found.Hits).FullName);
  }

  [SkippableFact]
  public void AShortNameMatchRanksAboveANamespaceOnlyMatch() {
    var found = fx.WithCatalog(c => c.Search("Catalog", 50));

    // Catalog, Entry and Box all live in TestFixture.Catalog; only one is named after the word.
    // Ranking is global, so compare within the fixture's own hits rather than against Hits[0].
    var ours = found.Hits.Select(h => h.FullName)
      .Where(n => n.StartsWith("TestFixture.", StringComparison.Ordinal))
      .ToList();

    Assert.Equal("TestFixture.Catalog.Catalog", ours[0]);
    Assert.Contains("TestFixture.Catalog.Entry", ours.Skip(1));
  }

  [SkippableFact]
  public void AGenericDefinitionIsReportedUnderTheNameThatResolves() {
    var found = fx.WithCatalog(c => c.Search("^TestFixture[.]Catalog[.]Box", 50));

    var hit = Assert.Single(found.Hits);

    // The harvest renders it "Box`1[T]"; what the caller gets is the full name, which is also what
    // the pattern matched and what every other tool takes.
    Assert.Equal("TestFixture.Catalog.Box`1", hit.FullName);
    Assert.Equal(hit.FullName, hit.Type.FullName);
  }

  [SkippableFact]
  public void AnAnchoredPatternReachesTheEndOfAGenericName() {
    // The type-parameter list the harvest renders would sit between "`1" and the anchor.
    var found = fx.WithCatalog(c => c.Search(@"^TestFixture[.]Catalog[.]Box`1$", 50));

    Assert.Equal("TestFixture.Catalog.Box`1", Assert.Single(found.Hits).FullName);
  }

  [SkippableFact]
  public void ANestedTypeIsFoundUnderItsDeclaringTypeAndASeparator() {
    var found = fx.WithCatalog(c => c.Search(@"^TestFixture[.]Catalog[.]Entry\+Nested$", 50));

    Assert.Equal("TestFixture.Catalog.Entry+Nested", Assert.Single(found.Hits).FullName);
  }

  [SkippableFact]
  public void TheCountCoversEveryMatchWhileTheLimitCapsTheListing() {
    var found = fx.WithCatalog(c => c.Search("^TestFixture[.]Catalog[.]", 2));

    Assert.Equal(4, found.Count);
    Assert.Equal(2, found.Hits.Count);
  }

  [SkippableFact]
  public void AnInvalidPatternReportsTheRuntimesOwnRegexMessage() {
    var ex = Assert.Throws<InvalidOperationException>(() => fx.WithCatalog(c => c.Search("(", 50)));

    Assert.Contains("Not enough", ex.Message, StringComparison.Ordinal);
  }

  [SkippableFact]
  public void ARunawayPatternIsAbandonedOnTheMatchBudget() {
    // Generous enough that an ordinary pattern finishes the whole scan well inside it, so what
    // trips below is the runaway pattern rather than the budget being unreachable.
    var impatient = fx.WithInvoker(inv => new TypeCatalog(inv, TimeSpan.FromMilliseconds(500)));

    Assert.NotEmpty(fx.WithCatalog(impatient, c => c.Search("TestFixture", 1)).Hits);

    // Nested quantifiers over a pattern that can never match (no type name carries whitespace):
    // the engine explores every partition of one name before giving up on it.
    var ex = Assert.Throws<InvalidOperationException>(() =>
      fx.WithCatalog(impatient, c => c.Search(@"(\w+)+\s", 50))
    );

    Assert.Contains("abandoned", ex.Message, StringComparison.Ordinal);
  }

  [SkippableFact]
  public void AnAssemblyLoadedMidSessionBecomesSearchableAndOnlyItIsHarvested() {
    // Its own catalog: loading into the shared debuggee cannot be undone, so the harvest counts
    // this asserts on must not depend on what the suite's shared catalog has already seen.
    var own = fx.WithInvoker(inv => new TypeCatalog(inv));

    _ = fx.WithCatalog(own, c => c.Search("TestFixture", 1));

    var settled = own.AssembliesHarvested;

    _ = fx.WithCatalog(own, c => c.Search("TestFixture", 1));

    Assert.Equal(settled, own.AssembliesHarvested);

    // System.Xml sits beside mscorlib and nothing in the fixture references it, so it is loaded
    // exactly here, at a point every earlier search has already passed.
    _ = fx.Eval(
      """
      var dir = System.IO.Path.GetDirectoryName(typeof(System.String).Assembly.Location);
      System.Reflection.Assembly.LoadFrom(System.IO.Path.Combine(dir, "System.Xml.dll"))
      """
    );

    var found = fx.WithCatalog(own, c => c.Search("System[.]Xml[.]XmlDocument$", 50));

    Assert.Equal("System.Xml.XmlDocument", Assert.Single(found.Hits).FullName);
    Assert.Equal(settled + 1, own.AssembliesHarvested);
  }

  [SkippableFact]
  public void AnAssemblyWhoseEnumerationFailsStillContributesTheTypesThatLoaded() {
    fx.LoadPartlyLoadableAssembly();

    var found = fx.WithCatalog(c => c.Search(TypeCatalogTests.PartlyLoadable, 50));

    // The two that derive from the absent dependency are missing rather than listed under an empty
    // name, and the two that do not are all there is to find.
    Assert.Equal(
      ["TestFixture.Broken.AlsoLoads", "TestFixture.Broken.Loads"],
      found.Hits.Select(h => h.FullName)
    );

    Assert.All(found.Hits, h => Assert.NotNull(h.Type));
  }

  [SkippableFact]
  public void AnAssemblyWhoseEnumerationFailsIsMarkedPartialWithItsReason() {
    fx.LoadPartlyLoadableAssembly();

    var found = fx.WithCatalog(c => c.Search(TypeCatalogTests.PartlyLoadable, 50));

    Assert.All(
      found.Hits,
      h => Assert.Equal(MonoDebuggeeFixture.PartlyLoadableAssembly, h.Assembly)
    );

    var reported = Assert.Single(
      found.Incomplete,
      i => i.Name == MonoDebuggeeFixture.PartlyLoadableAssembly
    );

    Assert.True(reported.IsPartial);
    Assert.Contains("ReflectionTypeLoadException", reported.Reason, StringComparison.Ordinal);
  }

  [SkippableFact]
  public void AnAssemblyWhoseEnumerationFailsIsHarvestedOnce() {
    fx.LoadPartlyLoadableAssembly();

    // Its own catalog, like the mid-session test: the harvest count only means something against a
    // catalog whose first search is this test's.
    var own = fx.WithInvoker(inv => new TypeCatalog(inv));

    _ = fx.WithCatalog(own, c => c.Search(TypeCatalogTests.PartlyLoadable, 50));

    var settled = own.AssembliesHarvested;

    var again = fx.WithCatalog(own, c => c.Search(TypeCatalogTests.PartlyLoadable, 50));

    // The recovery is paid once: the assembly is held with what it gave, not re-enumerated by every
    // later search, and what it gave is still there.
    Assert.Equal(settled, own.AssembliesHarvested);
    Assert.Equal(2, again.Count);
  }

  [SkippableFact]
  public void AnAssemblyThatRecoveredNothingIsNotReportedAsPartlyRead() {
    // A dynamic assembly whose types were never created throws the same partial type-load and
    // carries nothing to recover -- the case that must NOT read as "partially read".
    // TWO of them on purpose: a single gap joins to the empty string, where adjacent gaps join to
    // a run of separators that is non-empty while naming nothing.
    _ = fx.Eval(
      """
      var name = new System.Reflection.AssemblyName("UnfinishedDynamicAssembly");
      var access = System.Reflection.Emit.AssemblyBuilderAccess.Run;
      var built = System.AppDomain.CurrentDomain.DefineDynamicAssembly(name, access);
      var module = built.DefineDynamicModule("m");
      var first = module.DefineType("NeverCreated");
      module.DefineType("NeverCreatedEither")
      """
    );

    // What is searched for does not matter here, only that a search harvests: the assemblies it
    // could not read whole are reported whatever the pattern matched.
    var found = fx.WithCatalog(c => c.Search("^TestFixture[.]Overloads$", 1));

    var reported = Assert.Single(
      found.Incomplete,
      i => i.Name == "UnfinishedDynamicAssembly"
    );

    Assert.False(reported.IsPartial);
  }
}
