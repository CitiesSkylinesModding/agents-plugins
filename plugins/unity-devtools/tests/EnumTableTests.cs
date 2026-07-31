using System;
using System.Collections.Generic;
using Xunit;

namespace UnityDevtools.Sdb.Tests;

/// <summary>
/// Naming an enum value from the member table read off the target: an exact member when there is
/// one, a flags decomposition when the enum declares itself as flags, and the bare number
/// otherwise.
/// </summary>
public sealed class EnumTableTests {
  private static EnumTable Table(
    IReadOnlyDictionary<ulong, string> members,
    bool isFlags = true,
    Type? underlying = null
  ) {
    return new EnumTable {
      Members = members,
      IsFlags = isFlags,
      Underlying = underlying ?? typeof(int)
    };
  }

  private static readonly IReadOnlyDictionary<ulong, string> Powers =
    new Dictionary<ulong, string> {
      [0] = "None",
      [1] = "A",
      [2] = "B",
      [4] = "C"
    };

  [Fact]
  public void NamesAnExactMember() {
    Assert.Equal("B", EnumTableTests.Table(EnumTableTests.Powers).Render(2));
  }

  [Fact]
  public void NamesTheZeroMember() {
    Assert.Equal("None", EnumTableTests.Table(EnumTableTests.Powers).Render(0));
  }

  [Fact]
  public void PrefersAnExactMemberOverAnyDecomposition() {
    var table = EnumTableTests.Table(
      new Dictionary<ulong, string> {
        [1] = "A",
        [2] = "B",
        [3] = "Both"
      }
    );

    Assert.Equal("Both", table.Render(3));
  }

  [Fact]
  public void NamesACompositeMemberRatherThanItsParts() {
    var table = EnumTableTests.Table(
      new Dictionary<ulong, string> {
        [1] = "A",
        [2] = "B",
        [4] = "C",
        [7] = "All"
      }
    );

    // Greedy largest-first: All covers every bit of 15, leaving only the undeclared one.
    Assert.Equal("All | 8", table.Render(15));
  }

  [Fact]
  public void JoinsTheMembersAValueCarries() {
    Assert.Equal("A | C", EnumTableTests.Table(EnumTableTests.Powers).Render(5));
  }

  [Fact]
  public void AppendsLeftoverBitsAsANumber() {
    Assert.Equal("A | B | 8", EnumTableTests.Table(EnumTableTests.Powers).Render(11));
  }

  [Fact]
  public void RendersAnUnmatchedValueAsTheRawNumberWithoutTheFlagsAttribute() {
    var table = EnumTableTests.Table(EnumTableTests.Powers, isFlags: false);

    Assert.Equal("5", table.Render(5));
  }

  [Fact]
  public void RendersAValueMatchingNoMemberAtAllAsTheRawNumber() {
    Assert.Equal("8", EnumTableTests.Table(EnumTableTests.Powers).Render(8));
  }

  [Fact]
  public void DecomposesWithinTheUnderlyingTypeWidthWhenItIsSigned() {
    var table = EnumTableTests.Table(
      new Dictionary<ulong, string> {
        [1] = "A",
        [2] = "B"
      },
      underlying: typeof(sbyte)
    );

    // -1 sets every bit the type has and no more: sign extension would leave 56 phantom bits, and
    // the leftover is written back in the type's own signedness.
    Assert.Equal("A | B | -4", table.Render((sbyte) -1));
  }

  [Fact]
  public void MasksAndRendersACharBackedEnumAtItsOwnWidth() {
    var table = EnumTableTests.Table(
      new Dictionary<ulong, string> {
        [1] = "A"
      },
      underlying: typeof(char)
    );

    // 16 bits wide like the other two-byte types: without the mask the leftover would carry the
    // whole 64-bit pattern.
    Assert.Equal("A | 65534", table.Render(unchecked((char) -1)));

    var plain = EnumTableTests.Table(
      new Dictionary<ulong, string> {
        [1] = "A"
      },
      isFlags: false,
      underlying: typeof(char)
    );

    Assert.Equal("65", plain.Render('A'));
  }

  [Fact]
  public void NamesAMemberDeclaredAtTheTopBitOfAnUnsignedType() {
    var table = EnumTableTests.Table(
      new Dictionary<ulong, string> {
        [1] = "A",
        [ulong.MaxValue] = "All"
      },
      underlying: typeof(ulong)
    );

    Assert.Equal("All", table.Render(ulong.MaxValue));
  }
}
