using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityDevtools.Sdb.Tests;

/// <summary>
/// What a query's <c>select</c> means before and after the game is asked anything: how the specs
/// parse and group, and how per-row read outcomes become rows and counts.
/// Asserted on plain values, because the read between the two halves needs a live Entities world.
/// </summary>
public sealed class EcsSelectionTests {
  [Fact]
  public void ASpecNamesAComponentAndOptionallyOneField() {
    var parsed = EcsSelection.Parse(["MyGame.Movement.Speed", "MyGame.Health:current"]);

    Assert.Equal(["MyGame.Movement.Speed", "MyGame.Health"], parsed.Select(c => c.Component));
    Assert.Null(parsed[0].Specs.Single().Field);
    Assert.Equal("current", parsed[1].Specs.Single().Field);
  }

  [Fact]
  public void SpecsOnOneComponentGroupIntoOneRead() {
    var parsed = EcsSelection.Parse(
      ["MyGame.Health:current", "MyGame.Movement.Speed", "MyGame.Health:max", "MyGame.Health"]
    );

    Assert.Equal(["MyGame.Health", "MyGame.Movement.Speed"], parsed.Select(c => c.Component));
    Assert.Equal(["current", "max", null], parsed[0].Specs.Select(s => s.Field));
  }

  [Fact]
  public void ASpecWrittenTwiceCollapsesToOneKey() {
    var parsed = EcsSelection.Parse(["MyGame.Health:max", "MyGame.Health:max"]);

    Assert.Equal(["MyGame.Health:max"], parsed.Single().Specs.Select(s => s.Key));
  }

  [Fact]
  public void AKeyEchoesTheSpecAsWritten() {
    var parsed = EcsSelection.Parse([" MyGame.Health : max ", "MyGame.Health:"]);

    Assert.Equal("MyGame.Health", parsed.Single().Component);

    // A trailing colon names no field, so it selects the whole component under its own spelling.
    Assert.Equal(
      [(" MyGame.Health : max ", "max"), ("MyGame.Health:", null)],
      parsed.Single().Specs.Select(s => (s.Key, (string?) s.Field))
    );
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData(":max")]
  [InlineData("MyGame.Health:max:extra")]
  [InlineData(null)]
  public void AMalformedSpecIsRefusedWithTheShapeExpected(string? spec) {
    var ex = Assert.Throws<InvalidOperationException>(() => EcsSelection.Parse([spec!]));

    Assert.Equal(
      $"select expects \"<componentTypeFullName>[:<field>]\", got '{spec}'",
      ex.Message
    );
  }

  private const string Whole = "MyGame.Health";

  private const string Max = "MyGame.Health:max";

  private const string Frozen = "MyGame.Frozen";

  [Fact]
  public void ARowCarriesNoAdditionWhenNothingWasSelectedOrLabelled() {
    var row = EcsSelection.Row("Entity(7:1)", null, null);

    Assert.Equal("Entity(7:1)", row.Entity);
    Assert.Null(row.Label);
    Assert.Null(row.Values);
    Assert.Null(row.Errors);
    Assert.Null(row.Disabled);
  }

  [Fact]
  public void AnAbsentSpecAppearsInNeitherMap() {
    var row = EcsSelection.Row(
      "Entity(7:1)",
      null,
      [SpecRead.Of(EcsSelectionTests.Max, "100"), SpecRead.Absent(EcsSelectionTests.Frozen)]
    );

    Assert.Equal(EcsSelectionTests.Max, Assert.Single(row.Values).Key);
    Assert.Equal("100", row.Values[EcsSelectionTests.Max]);
    Assert.Null(row.Errors);
    Assert.Null(row.Disabled);
  }

  [Fact]
  public void ARowWhoseEverySpecIsAbsentStillCarriesAnEmptyValueMap() {
    var row = EcsSelection.Row("Entity(7:1)", null, [SpecRead.Absent(EcsSelectionTests.Max)]);

    Assert.Empty(row.Values);
  }

  [Fact]
  public void AFailedSpecAppearsInTheErrorsMapOnly() {
    var row = EcsSelection.Row(
      "Entity(7:1)",
      null,
      [SpecRead.Failed(EcsSelectionTests.Max, "boom"), SpecRead.Of(EcsSelectionTests.Whole, "v")]
    );

    Assert.Equal(EcsSelectionTests.Whole, Assert.Single(row.Values).Key);
    Assert.Equal("boom", Assert.Single(row.Errors).Value);
    Assert.Equal(EcsSelectionTests.Max, Assert.Single(row.Errors).Key);
  }

  [Fact]
  public void APresentTagCarriesItsKindName() {
    var row = EcsSelection.Row(
      "Entity(7:1)",
      null,
      [SpecRead.Of(EcsSelectionTests.Frozen, EcsKind.Tag.Wire)]
    );

    Assert.Equal("tag", row.Values[EcsSelectionTests.Frozen]);
  }

  [Fact]
  public void ADisabledComponentKeepsItsValueAndIsListedAsDisabled() {
    var row = EcsSelection.Row(
      "Entity(7:1)",
      null,
      [
        SpecRead.Of(EcsSelectionTests.Max, "100", disabled: true),
        SpecRead.Of(EcsSelectionTests.Frozen, "tag")
      ]
    );

    Assert.Equal("100", row.Values[EcsSelectionTests.Max]);
    Assert.Equal([EcsSelectionTests.Max], row.Disabled);
  }

  [Fact]
  public void ALabelFailureIsKeyedAsLabel() {
    var row = EcsSelection.Row("Entity(7:1)", (null, "name system threw"), null);

    Assert.Null(row.Label);
    Assert.Null(row.Values);
    Assert.Equal("label", Assert.Single(row.Errors).Key);
    Assert.Equal("name system threw", row.Errors["label"]);
  }

  [Fact]
  public void ALabelThatAnsweredIsCarriedBesideTheValues() {
    var row = EcsSelection.Row(
      "Entity(7:1)",
      ("\"Depot\"", null),
      [SpecRead.Of(EcsSelectionTests.Max, "100")]
    );

    Assert.Equal("\"Depot\"", row.Label);
    Assert.Null(row.Errors);
  }

  [Fact]
  public void TheSummaryCountsAbsentFailedAndDisabledRowsPerSpec() {
    string[] keys = [EcsSelectionTests.Max, EcsSelectionTests.Frozen];

    EcsQueryRow[] rows = [
      EcsSelection.Row(
        "Entity(1:1)",
        null,
        [SpecRead.Of(EcsSelectionTests.Max, "100"), SpecRead.Absent(EcsSelectionTests.Frozen)]
      ),
      EcsSelection.Row(
        "Entity(2:1)",
        null,
        [
          SpecRead.Failed(EcsSelectionTests.Max, "boom"),
          SpecRead.Of(EcsSelectionTests.Frozen, "tag", disabled: true)
        ]
      ),
      EcsSelection.Row(
        "Entity(3:1)",
        null,
        [SpecRead.Absent(EcsSelectionTests.Max), SpecRead.Absent(EcsSelectionTests.Frozen)]
      )
    ];

    var summary = EcsSelection.Summarize(keys, labelled: false, rows);

    Assert.Equal(
      new Dictionary<string, int> {
        [EcsSelectionTests.Max] = 1,
        [EcsSelectionTests.Frozen] = 2
      },
      summary.Absent
    );

    Assert.Equal(
      new Dictionary<string, int> {
        [EcsSelectionTests.Max] = 1,
        [EcsSelectionTests.Frozen] = 0
      },
      summary.Failed
    );

    Assert.Equal(
      new Dictionary<string, int> {
        [EcsSelectionTests.Max] = 0,
        [EcsSelectionTests.Frozen] = 1
      },
      summary.Disabled
    );
  }

  [Fact]
  public void ALabelFailureCountsUnderTheLabelKey() {
    EcsQueryRow[] rows = [
      EcsSelection.Row("Entity(1:1)", ("\"Depot\"", null), null),
      EcsSelection.Row("Entity(2:1)", (null, "name system threw"), null)
    ];

    var summary = EcsSelection.Summarize(null, labelled: true, rows);

    Assert.Equal(1, Assert.Single(summary.Failed).Value);
    Assert.Equal("label", Assert.Single(summary.Failed).Key);

    // Nothing was selected, so there is no spec an absent or a disabled count could be about.
    Assert.Null(summary.Absent);
    Assert.Null(summary.Disabled);
  }

  [Fact]
  public void ThereIsNoSummaryWhenNothingWasSelectedOrLabelled() {
    Assert.Null(
      EcsSelection.Summarize(null, labelled: false, [EcsSelection.Row("Entity(1:1)", null, null)])
    );
  }

  [Fact]
  public void ASpecNoRowMissedStillReportsItsZeroes() {
    var summary = EcsSelection.Summarize([EcsSelectionTests.Max], labelled: true, []);

    Assert.Equal(0, summary.Absent[EcsSelectionTests.Max]);
    Assert.Equal(0, summary.Failed[EcsSelectionTests.Max]);
    Assert.Equal(0, summary.Failed["label"]);
  }
}
