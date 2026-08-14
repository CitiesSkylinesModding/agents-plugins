using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UnityDevtools.Sdb;

/// <summary>
/// One PlayerConnection target-info beacon as a Unity player multicasts it, plus the Mono Soft
/// Debugger endpoint it implies.
/// The payload is a flat list of <c>[Key] value</c> pairs, and the same text appears verbatim in
/// the player's log, so a logged line parses exactly like a received packet.
/// The debugger port is NOT advertised: <c>[Port]</c> is the PlayerConnection/profiler port, and
/// <c>[Id]</c> carries no <c>:port</c> suffix on a player. It is derived from <c>[Guid]</c>
/// instead, through the formula Unity's IDE integration uses.
/// </summary>
public sealed class PlayerConnectionBeacon {
  /// <summary>
  /// The base of the debugger-port formula; the connection GUID picks the offset.
  /// </summary>
  public const int SdbPortBase = 56000;

  private const int SdbPortSpan = 1000;

  private static readonly Regex FieldPattern = new(
    @"\[(?<key>[A-Za-z]+)\]\s*(?<value>[^\[]*)",
    RegexOptions.CultureInvariant
  );

  /// <summary>
  /// What the last field's value picks up from its carrier: a NUL terminating the datagram, or the
  /// quote closing the log line that wraps the same payload.
  /// </summary>
  private static readonly char[] Padding = ['\0', ' ', '\t', '\r', '\n', '"'];

  /// <summary>
  /// The advertised address, empty when the beacon carries no <c>[IP]</c>.
  /// </summary>
  public string Ip { get; init; }

  /// <summary>
  /// The advertised <c>[Port]</c>: the PlayerConnection/profiler port, never the debugger's.
  /// </summary>
  public int? PlayerConnectionPort { get; init; }

  /// <summary>
  /// The connection GUID the debugger port derives from.
  /// </summary>
  public uint ConnectionGuid { get; init; }

  /// <summary>
  /// The player identity string, e.g. <c>WindowsPlayer(2,SOMEHOST)</c>.
  /// </summary>
  public string Id { get; init; }

  /// <summary>
  /// Whether the player advertises the managed debugger as enabled (<c>[Debug] 1</c>).
  /// </summary>
  public bool DebuggerEnabled { get; init; }

  public string PackageName { get; init; }

  public string ProjectName { get; init; }

  /// <summary>
  /// Where to reach the player: its advertised address, which is correct for a local and a LAN game
  /// alike since the agent binds every interface. Loopback when the beacon omits it.
  /// </summary>
  public string Host => string.IsNullOrEmpty(this.Ip) ? "127.0.0.1" : this.Ip;

  /// <summary>
  /// The SDB port the player's agent binds, derived from the connection GUID.
  /// </summary>
  public int SdbPort =>
    PlayerConnectionBeacon.SdbPortBase +
    (int) (this.ConnectionGuid % PlayerConnectionBeacon.SdbPortSpan);

  /// <summary>
  /// Whether this beacon describes a player worth attaching to.
  /// </summary>
  public bool Attachable => this.DebuggerEnabled;

  /// <summary>
  /// Where an attach would go, or null when this beacon names no target. It is one rule so that
  /// what <c>status</c> reports and what an attach then dials cannot drift apart.
  /// </summary>
  public (string Host, int Port)? Endpoint =>
    this.Attachable ? (this.Host, this.SdbPort) : null;

  /// <summary>
  /// Parses a beacon payload, or returns null when the line is not one.
  /// </summary>
  public static PlayerConnectionBeacon Parse(string line) {
    if (string.IsNullOrWhiteSpace(line)) {
      return null;
    }

    var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (Match match in PlayerConnectionBeacon.FieldPattern.Matches(line)) {
      fields[match.Groups["key"].Value] =
        match.Groups["value"].Value.Trim(PlayerConnectionBeacon.Padding);
    }

    // [Guid] is what makes a line a beacon here: it is the only field the debugger endpoint can be
    // derived from, so a line missing it is rejected whole rather than half-parsed into a target
    // with no port.
    var guid = PlayerConnectionBeacon.Number(fields, "Guid");

    // Read as a long so both ends of the uint range are rejected rather than wrapped: a signed
    // parse accepts a leading minus, and the cast below would turn one into an arbitrary valid
    // GUID, hence an attachable player at a port nothing is listening on.
    if (guid is null or < 0 or > uint.MaxValue) {
      return null;
    }

    return new PlayerConnectionBeacon {
      Ip = PlayerConnectionBeacon.Text(fields, "IP"),
      PlayerConnectionPort = (int?) PlayerConnectionBeacon.Number(fields, "Port"),
      ConnectionGuid = (uint) guid.Value,
      Id = PlayerConnectionBeacon.Text(fields, "Id"),
      DebuggerEnabled = PlayerConnectionBeacon.Text(fields, "Debug") is "1",
      PackageName = PlayerConnectionBeacon.Text(fields, "PackageName"),
      ProjectName = PlayerConnectionBeacon.Text(fields, "ProjectName")
    };
  }

  private static string Text(Dictionary<string, string> fields, string key) =>
    fields.TryGetValue(key, out var value) ? value : null;

  private static long? Number(Dictionary<string, string> fields, string key) {
    var raw = PlayerConnectionBeacon.Text(fields, key);

    return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
      ? value
      : null;
  }
}
