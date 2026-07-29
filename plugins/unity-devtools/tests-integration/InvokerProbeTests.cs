using System;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// <see cref="Invoker" />'s member lookup against a real debuggee, throwing and non-throwing.
/// The non-throwing variant is how the tools decide what the target's API supports before calling
/// it, so "absent" must come back as a value, never as an exception.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class InvokerProbeTests(MonoDebuggeeFixture fx) {
  [SkippableFact]
  public void ProbingAPresentMemberResolvesIt() {
    var inv = fx.Invoker;

    var method = inv.FindMethodOrNull(inv.ResolveType("TestFixture.Overloads"), "TakesLong", 1);

    Assert.NotNull(method);
    Assert.Equal("TakesLong", method.Name);
  }

  [SkippableFact]
  public void ProbingAnAbsentMemberReturnsNullInsteadOfThrowing() {
    var inv = fx.Invoker;

    Assert.Null(
      inv.FindMethodOrNull(inv.ResolveType("TestFixture.Overloads"), "NoSuchMember", 1)
    );
  }

  [SkippableFact]
  public void ProbingAPresentMemberAtTheWrongArityReportsItAbsent() {
    var inv = fx.Invoker;

    Assert.Null(inv.FindMethodOrNull(inv.ResolveType("TestFixture.Overloads"), "TakesLong", 3));
  }

  [SkippableFact]
  public void ProbingPicksTheOverloadNamedByItsParameterTypes() {
    var inv = fx.Invoker;

    var method = inv.FindMethodOrNull(
      inv.ResolveType("TestFixture.Overloads"),
      "Pick",
      1,
      paramTypes: ["Double"]
    );

    Assert.NotNull(method);
    Assert.Equal("Double", method.GetParameters()[0].ParameterType.Name);
  }

  [SkippableFact]
  public void ProbingWalksTheBaseTypeChain() {
    var inv = fx.Invoker;

    // GetHashCode is declared on Object alone, two hops up from DerivedThing.
    Assert.NotNull(
      inv.FindMethodOrNull(inv.ResolveType("TestFixture.DerivedThing"), "GetHashCode", 0)
    );
  }

  [SkippableFact]
  public void TheThrowingLookupStillNamesTheMissingMember() {
    var inv = fx.Invoker;

    var ex = Assert.Throws<InvalidOperationException>(() =>
      inv.FindMethod(inv.ResolveType("TestFixture.Overloads"), "NoSuchMember", 1)
    );

    Assert.Contains("NoSuchMember", ex.Message, StringComparison.Ordinal);
  }
}
