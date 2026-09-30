using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityDevtools.Sdb.Tests;

/// <summary>
/// The rules deciding what a miss suggests: which names, in which order, and where the list is
/// cut.
/// They are asserted here, on names alone, because a live debuggee's catalog carries its runtime's
/// own types, and what overflows a cap there depends on which runtime is installed.
/// </summary>
public sealed class MissSuggestionsTests {
  [Fact]
  public void ExactSimpleNamesRankAheadOfContainingOnes() {
    var ranked = MissSuggestions.RankTypes(
      ["A.SpeedLimit", "Z.Far.Speed", "B.Speed", "C.Unrelated"],
      ["Speed"],
      "Speed"
    );

    Assert.Equal(["B.Speed", "Z.Far.Speed", "A.SpeedLimit"], ranked.Listed);
    Assert.Equal(3, ranked.Total);
  }

  [Fact]
  public void ContainingNamesRankShortestSimpleNameFirstThenOrdinalByFullName() {
    var ranked = MissSuggestions.RankTypes(
      ["A.SpeedLimiter", "B.SpeedLimit", "C.MaxSpeed", "A.SpeedLimit"],
      ["Speed"],
      "Speed"
    );

    Assert.Equal(["C.MaxSpeed", "A.SpeedLimit", "B.SpeedLimit", "A.SpeedLimiter"], ranked.Listed);
  }

  [Fact]
  public void MatchingIgnoresCaseAndTheNamespace() {
    var ranked = MissSuggestions.RankTypes(["Speed.Other", "A.SPEED", "A.speedy"], ["speed"], null);

    // "Speed.Other" carries the word in its namespace alone, which is not what was typed.
    Assert.Equal(["A.SPEED"], ranked.Listed);
  }

  [Fact]
  public void AnyOfSeveralExactNamesMatches() {
    var ranked = MissSuggestions.RankTypes(
      ["A.Movement", "A.Speed", "A.Other"],
      ["Movment", "Speed"],
      "Movment"
    );

    Assert.Equal(["A.Speed"], ranked.Listed);
  }

  [Fact]
  public void LaterSegmentsNamingTypesExactlyDoNotCrowdOutTheFailedOne() {
    var ranked = MissSuggestions.RankTypes(
      ["A.Time", "B.Time", "C.Time", "D.Time", "E.Time", "Z.SystemBase"],
      ["System", "World", "Time"],
      "System"
    );

    // A chain's later segments are usually members, and common member names are type names too.
    Assert.Equal(["Z.SystemBase", "A.Time", "B.Time", "C.Time", "D.Time"], ranked.Listed);
    Assert.Equal(6, ranked.Total);
  }

  [Fact]
  public void ALaterSegmentNamingATypeExactlyKeepsItsPlaceInACrowdedList() {
    var ranked = MissSuggestions.RankTypes(
      ["A.Mist", "B.Mist", "C.Mist", "D.Mist", "E.Mist", "F.Mist", "X.Twin", "Y.Twin"],
      ["Mis", "Twin", "Foo"],
      "Mis"
    );

    // A mistyped namespace leaves the type's own name intact further right.
    Assert.Equal(["A.Mist", "B.Mist", "C.Mist", "X.Twin", "Y.Twin"], ranked.Listed);
  }

  [Fact]
  public void TheCapCutsTheListingAndTheTotalStillCountsEveryMatch() {
    var names = Enumerable.Range(0, 12).Select(i => $"N{i:00}.Speed").ToList();

    var ranked = MissSuggestions.RankTypes(names, ["Speed"], "Speed");

    Assert.Equal(MissSuggestions.TypeCap, ranked.Listed.Count);
    Assert.Equal("N00.Speed", ranked.Listed[0]);
    Assert.Equal(12, ranked.Total);
  }

  [Fact]
  public void ANameTwoAssembliesDeclareIsSuggestedOnce() {
    var ranked = MissSuggestions.RankTypes(["A.Speed", "A.Speed"], ["Speed"], null);

    Assert.Equal(1, ranked.Total);
  }

  [Theory]
  [InlineData("A.B.Speed", "Speed")]
  [InlineData("A.Box`1", "Box")]
  [InlineData("A.Outer+Inner", "Inner")]
  [InlineData("A.Outer`2+Inner`1", "Inner")]
  [InlineData("Global", "Global")]
  public void TheSimpleNameDropsNamespaceDeclaringTypeAndArity(string fullName, string simple) {
    Assert.Equal(simple, MissSuggestions.SimpleName(fullName));
  }

  [Fact]
  public void AGenericTypeMatchesOnItsNameWithoutTheArity() {
    Assert.Equal(["A.Box`1"], MissSuggestions.RankTypes(["A.Box`1"], ["Box"], null).Listed);
  }

  [Fact]
  public void ANestedTypeIsSpelledWithDotsForTheEvaluator() {
    Assert.Equal("A.Outer.Inner", MissSuggestions.EvalName("A.Outer+Inner"));
  }

  [Fact]
  public void ContainingNamesLeadAndBothGroupsKeepTheirOrder() {
    Assert.Equal(
      ["GetSpeed", "SpeedUp", "Zeta", "Alpha"],
      MissSuggestions.ContainingFirst(["Zeta", "GetSpeed", "Alpha", "SpeedUp", "Zeta"], "speed")
    );
  }

  [Fact]
  public void AListingUnderTheCapNamesEveryGroupInOrder() {
    Assert.Equal(
      "properties: A, B; fields: c",
      MissSuggestions.Listing(MissSuggestionsTests.Groups(["A", "B"], ["c"]), 30, "elsewhere")
    );
  }

  [Fact]
  public void OneCapSpansTheGroupsAndTheCutCountsWhatItDropped() {
    Assert.Equal(
      "properties: A, B; fields: c (+2 more: elsewhere)",
      MissSuggestions.Listing(
        MissSuggestionsTests.Groups(["A", "B"], ["c", "d", "e"]),
        3,
        "elsewhere"
      )
    );
  }

  [Fact]
  public void AGroupTheCapNeverReachesIsNotLabeled() {
    Assert.Equal(
      "properties: A, B (+3 more: elsewhere)",
      MissSuggestions.Listing(
        MissSuggestionsTests.Groups(["A", "B", "C"], ["d", "e"]),
        2,
        "elsewhere"
      )
    );
  }

  [Fact]
  public void AnEmptyGroupIsLeftOutAndNoNamesAtAllSaysNone() {
    Assert.Equal(
      "fields: c",
      MissSuggestions.Listing(MissSuggestionsTests.Groups([], ["c"]), 30, "elsewhere")
    );

    Assert.Equal(
      "none",
      MissSuggestions.Listing(MissSuggestionsTests.Groups([], []), 30, "elsewhere")
    );
  }

  private static IReadOnlyList<(string, IReadOnlyList<string>)> Groups(
    IReadOnlyList<string> properties,
    IReadOnlyList<string> fields
  ) => [("properties", properties), ("fields", fields)];
}
