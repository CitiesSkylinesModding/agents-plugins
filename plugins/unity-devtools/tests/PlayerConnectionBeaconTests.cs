using Xunit;

namespace UnityDevtools.Sdb.Tests;

/// <summary>
/// Parsing the PlayerConnection beacon and deriving the SDB endpoint from it.
/// The two known-answer cases are recorded runs of the reference game: each pairs the beacon its
/// player logged with the port its agent then reported binding, which is the only evidence the
/// <c>56000 + (guid % 1000)</c> formula has. A change to the derivation must fail here rather than
/// in a live session.
/// </summary>
public sealed class PlayerConnectionBeaconTests {
  private const string RecordedPayload =
    "[IP] 192.168.1.21 [Port] 55000 [Flags] 2 [Guid] 2314420099 [EditorId] 0 [Version] 1048832 " +
    "[Id] WindowsPlayer(2,Anthracite) [Debug] 1 [PackageName] WindowsPlayer " +
    "[ProjectName] <no name>";

  [Fact]
  public void DerivesTheDebuggerPortFromTheFirstRecordedRun() {
    var beacon = PlayerConnectionBeacon.Parse(PlayerConnectionBeaconTests.RecordedPayload);

    Assert.NotNull(beacon);
    Assert.Equal(56099, beacon.SdbPort);
  }

  [Fact]
  public void DerivesTheDebuggerPortFromTheSecondRecordedRun() {
    var beacon = PlayerConnectionBeacon.Parse("[Guid] 4190902252 [Debug] 1");

    Assert.NotNull(beacon);
    Assert.Equal(56252, beacon.SdbPort);
  }

  [Fact]
  public void ReadsEveryReportedField() {
    var beacon = PlayerConnectionBeacon.Parse(PlayerConnectionBeaconTests.RecordedPayload);

    Assert.NotNull(beacon);
    Assert.Equal("192.168.1.21", beacon.Ip);
    Assert.Equal(2314420099u, beacon.ConnectionGuid);
    Assert.Equal("WindowsPlayer(2,Anthracite)", beacon.Id);
    Assert.Equal("WindowsPlayer", beacon.PackageName);
    Assert.Equal("<no name>", beacon.ProjectName);
  }

  [Fact]
  public void KeepsThePlayerConnectionPortApartFromTheDebuggerPort() {
    var beacon = PlayerConnectionBeacon.Parse(PlayerConnectionBeaconTests.RecordedPayload);

    Assert.NotNull(beacon);
    Assert.Equal(55000, beacon.PlayerConnectionPort);
    Assert.Equal(56099, beacon.SdbPort);
  }

  [Fact]
  public void ParsesThePayloadOutOfTheLoggedLineThatWrapsIt() {
    var beacon = PlayerConnectionBeacon.Parse(
      $"Player connection [23716]  * \"{PlayerConnectionBeaconTests.RecordedPayload}\""
    );

    Assert.NotNull(beacon);
    Assert.Equal(56099, beacon.SdbPort);

    // The wrapping quote belongs to the log line, not to the last field's value.
    Assert.Equal("<no name>", beacon.ProjectName);
  }

  [Fact]
  public void DropsTheNulThatTerminatesTheDatagram() {
    var beacon = PlayerConnectionBeacon.Parse($"{PlayerConnectionBeaconTests.RecordedPayload}\0");

    Assert.NotNull(beacon);
    Assert.Equal("<no name>", beacon.ProjectName);
  }

  [Fact]
  public void TakesTheHostFromTheAdvertisedAddress() {
    var beacon = PlayerConnectionBeacon.Parse(PlayerConnectionBeaconTests.RecordedPayload);

    Assert.NotNull(beacon);
    Assert.Equal("192.168.1.21", beacon.Host);
  }

  [Fact]
  public void FallsBackToLoopbackWhenNoAddressIsAdvertised() {
    var beacon = PlayerConnectionBeacon.Parse("[Port] 55000 [Guid] 2314420099 [Debug] 1");

    Assert.NotNull(beacon);
    Assert.Equal("127.0.0.1", beacon.Host);
  }

  [Fact]
  public void TreatsADebugFlagOfOneAsAttachable() {
    var beacon = PlayerConnectionBeacon.Parse(PlayerConnectionBeaconTests.RecordedPayload);

    Assert.NotNull(beacon);
    Assert.True(beacon.Attachable);
  }

  [Fact]
  public void ReportsAPlayerWithoutTheDebuggerAsNotAttachable() {
    var beacon = PlayerConnectionBeacon.Parse(
      PlayerConnectionBeaconTests.RecordedPayload.Replace("[Debug] 1", "[Debug] 0")
    );

    Assert.NotNull(beacon);
    Assert.False(beacon.Attachable);
    Assert.Equal("WindowsPlayer(2,Anthracite)", beacon.Id);
  }

  [Fact]
  public void ReportsAPlayerWithNoDebugFieldAsNotAttachable() {
    var beacon = PlayerConnectionBeacon.Parse("[IP] 192.168.1.21 [Port] 55000 [Guid] 2314420099");

    Assert.NotNull(beacon);
    Assert.False(beacon.Attachable);
  }

  [Theory]
  [InlineData("Player connection [23716] Started UDP target info broadcast (1) on [225.0.0.222].")]
  [InlineData("[IP] 192.168.1.21 [Port] 55000 [Debug] 1")]
  [InlineData("[Guid] not-a-number [Debug] 1")]
  [InlineData("")]
  [InlineData("   ")]
  public void RejectsALineItCannotDeriveAnEndpointFrom(string line) {
    // Rejected whole: half-parsing one into a target with no port would send a caller at an
    // address the beacon never named.
    Assert.Null(PlayerConnectionBeacon.Parse(line));
  }

  [Theory]
  [InlineData("[Guid] 99999999999 [Debug] 1")]
  [InlineData("[Guid] -1 [Debug] 1")]
  public void RejectsAConnectionGuidOutsideTheRangeOfOne(string line) {
    // Both ends: the derivation takes the GUID modulo 1000, so either one wrapping would name a
    // port in range and pass for a player that does not exist.
    Assert.Null(PlayerConnectionBeacon.Parse(line));
  }
}
