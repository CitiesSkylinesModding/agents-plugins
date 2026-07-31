using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityDevtools.Sdb.Tests;

/// <summary>
/// Decoding a component type's storage kind from its type index, with the masks the target's own
/// type manager declares (the values below are a real target's, read live).
/// </summary>
public sealed class TypeIndexFlagsTests {
  private const int Buffer = 1 << 26;

  private const int Shared = 1 << 27;

  private const int Managed = 1 << 28;

  private const int Chunk = 1 << 29;

  private const int ZeroSize = 1 << 30;

  private const int Enableable = 1 << 24;

  private static readonly IReadOnlyDictionary<string, int> Constants =
    new Dictionary<string, int> {
      ["BufferComponentTypeFlag"] = TypeIndexFlagsTests.Buffer,
      ["SharedComponentTypeFlag"] = TypeIndexFlagsTests.Shared,
      ["ManagedComponentTypeFlag"] = TypeIndexFlagsTests.Managed,
      ["ChunkComponentTypeFlag"] = TypeIndexFlagsTests.Chunk,
      ["ZeroSizeInChunkTypeFlag"] = TypeIndexFlagsTests.ZeroSize,
      ["EnableableComponentFlag"] = TypeIndexFlagsTests.Enableable
    };

  private static TypeIndexFlags Flags() =>
    TypeIndexFlags.FromConstants(TypeIndexFlagsTests.Constants);

  [Theory]
  [InlineData(TypeIndexFlagsTests.Buffer, "buffer")]
  [InlineData(TypeIndexFlagsTests.Shared, "shared")]
  [InlineData(TypeIndexFlagsTests.Chunk, "chunk")]
  [InlineData(TypeIndexFlagsTests.Managed, "managed")]
  [InlineData(TypeIndexFlagsTests.ZeroSize, "tag")]
  public void NamesEachKindFromItsFlagBit(int flag, string kind) {
    Assert.Equal(kind, TypeIndexFlagsTests.Flags().KindOf(flag | 1234));
  }

  [Fact]
  public void ReportsAComponentWhenNoKindFlagIsSet() {
    Assert.Equal("component", TypeIndexFlagsTests.Flags().KindOf(1234));

    // The enabled bit is not a kind, so it must not push the answer off "component".
    Assert.Equal(
      "component",
      TypeIndexFlagsTests.Flags().KindOf(TypeIndexFlagsTests.Enableable | 1234)
    );
  }

  [Theory]
  [InlineData(TypeIndexFlagsTests.Chunk | TypeIndexFlagsTests.ZeroSize, "chunk")]
  [InlineData(TypeIndexFlagsTests.Shared | TypeIndexFlagsTests.ZeroSize, "shared")]
  [InlineData(TypeIndexFlagsTests.Shared | TypeIndexFlagsTests.Managed, "shared")]
  [InlineData(TypeIndexFlagsTests.Buffer | TypeIndexFlagsTests.Managed, "buffer")]
  public void KeepsTheMostSpecificKindWhenSeveralFlagsAreSet(int typeIndex, string kind) {
    Assert.Equal(kind, TypeIndexFlagsTests.Flags().KindOf(typeIndex | 1234));
  }

  [Fact]
  public void ReadsTheEnabledBitIndependentlyOfTheKind() {
    var flags = TypeIndexFlagsTests.Flags();

    Assert.True(flags.IsEnableable(TypeIndexFlagsTests.Enableable | TypeIndexFlagsTests.ZeroSize));
    Assert.False(flags.IsEnableable(TypeIndexFlagsTests.ZeroSize));
  }

  [Fact]
  public void RefusesAnIncompleteMaskSetSoTheCallerCanFallBack() {
    foreach (var missing in TypeIndexFlags.ConstantNames) {
      var partial = TypeIndexFlagsTests.Constants.Where(c => c.Key != missing)
        .ToDictionary(c => c.Key, c => c.Value);

      Assert.Null(TypeIndexFlags.FromConstants(partial));
    }

    Assert.NotNull(TypeIndexFlags.FromConstants(TypeIndexFlagsTests.Constants));
  }
}
