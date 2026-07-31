using System.Collections.Generic;
using System.Linq;

namespace UnityDevtools.Sdb;

/// <summary>
/// The bit masks a component type's storage kind is encoded in, as the target's own type manager
/// declares them.
/// The positions move between Entities versions, so they are READ off the target rather than
/// hardcoded: the target states its own layout and this follows it, which is what lets the kind be
/// decoded client-side from a type index the wire already carried (see
/// docs/adr/0001-decode-target-constants-client-side.md).
/// </summary>
public sealed record TypeIndexFlags {
  private const string BufferConstant = "BufferComponentTypeFlag";

  private const string SharedConstant = "SharedComponentTypeFlag";

  private const string ManagedConstant = "ManagedComponentTypeFlag";

  private const string ChunkConstant = "ChunkComponentTypeFlag";

  private const string ZeroSizeConstant = "ZeroSizeInChunkTypeFlag";

  private const string EnableableConstant = "EnableableComponentFlag";

  /// <summary>
  /// The constants this decode is built from, under the names the target declares them by.
  /// Every one of them is required: a partial set decodes some kinds and silently mis-decodes the
  /// rest, so <see cref="FromConstants" /> refuses it wholesale.
  /// </summary>
  public static IReadOnlyList<string> ConstantNames { get; } = [
    TypeIndexFlags.BufferConstant,
    TypeIndexFlags.SharedConstant,
    TypeIndexFlags.ManagedConstant,
    TypeIndexFlags.ChunkConstant,
    TypeIndexFlags.ZeroSizeConstant,
    TypeIndexFlags.EnableableConstant
  ];

  public required int Buffer { get; init; }

  public required int Shared { get; init; }

  public required int Managed { get; init; }

  public required int Chunk { get; init; }

  public required int ZeroSize { get; init; }

  public required int Enableable { get; init; }

  /// <summary>
  /// Builds the mask set from the constants read off the target, null when any is missing.
  /// A null answer is the caller's signal to ask the target per property instead: an unfamiliar
  /// version is slower rather than wrong.
  /// </summary>
  public static TypeIndexFlags FromConstants(IReadOnlyDictionary<string, int> constants) {
    if (!TypeIndexFlags.ConstantNames.All(constants.ContainsKey)) {
      return null;
    }

    return new TypeIndexFlags {
      Buffer = constants[TypeIndexFlags.BufferConstant],
      Shared = constants[TypeIndexFlags.SharedConstant],
      Managed = constants[TypeIndexFlags.ManagedConstant],
      Chunk = constants[TypeIndexFlags.ChunkConstant],
      ZeroSize = constants[TypeIndexFlags.ZeroSizeConstant],
      Enableable = constants[TypeIndexFlags.EnableableConstant]
    };
  }

  /// <summary>
  /// Names how a component type is stored, which is what decides the accessor that can read it.
  /// The flags are not mutually exclusive -- a shared component is also zero-sized, a chunk
  /// component is zero-sized on the entity that carries it -- so this ladder IS the
  /// classification, most specific first.
  /// </summary>
  public EcsKind KindOf(int typeIndex) {
    return TypeIndexFlags.IsSet(typeIndex, this.Buffer)
      ? EcsKind.Buffer
      : TypeIndexFlags.IsSet(typeIndex, this.Shared)
        ? EcsKind.Shared
        : TypeIndexFlags.IsSet(typeIndex, this.Chunk)
          ? EcsKind.Chunk
          : TypeIndexFlags.IsSet(typeIndex, this.Managed)
            ? EcsKind.Managed
            : TypeIndexFlags.IsSet(typeIndex, this.ZeroSize)
              ? EcsKind.Tag
              : EcsKind.Component;
  }

  /// <summary>Whether the type carries an enabled bit on the entities storing it.</summary>
  public bool IsEnableable(int typeIndex) => TypeIndexFlags.IsSet(typeIndex, this.Enableable);

  private static bool IsSet(int typeIndex, int mask) => (typeIndex & mask) is not 0;
}
