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
