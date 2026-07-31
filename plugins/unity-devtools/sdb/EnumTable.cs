using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UnityDevtools.Sdb;

/// <summary>
/// One enum type's members, keyed by the bits they set, plus what rendering a value of it needs:
/// the whole table is read off the target once and every later value is named client-side.
/// Member values are held as raw bits masked to the underlying type's width, so a signed member
/// never brings bits the type does not have into a decomposition.
/// </summary>
public sealed record EnumTable {
  /// <summary>Member bits to member name; a value appearing twice keeps one name.</summary>
  public required IReadOnlyDictionary<ulong, string> Members { get; init; }

  /// <summary>
  /// Whether the enum carries <c>[Flags]</c>, which is what licenses a decomposition.
  /// </summary>
  public required bool IsFlags { get; init; }

  /// <summary>
  /// The enum's underlying integral type, which decides its width and its signedness.
  /// </summary>
  public required Type Underlying { get; init; }

  /// <summary>
  /// Names a value: its own member when one matches exactly, otherwise, on a flags enum, the
  /// members whose bits it carries joined with " | " and any leftover bits appended as a number.
  /// A plain enum's unmatched value stays the bare number it renders as today: decomposing bits
  /// the type never declared as flags would invent a reading the game does not have.
  /// A number is always written the way the underlying type writes it, so one value reads the same
  /// whether it fell back whole or only in part.
  /// </summary>
  public string Render(object value) {
    var bits = EnumTable.Bits(value, this.Underlying);

    if (this.Members.TryGetValue(bits, out var exact)) {
      return exact;
    }

    return this.IsFlags ? this.Decompose(bits) : this.Number(bits);
  }

  /// <summary>
  /// Takes the largest members first, so a composite member (<c>All = A | B | C</c>) is named
  /// rather than spelled out as its parts, then reports what it took in ascending value order, the
  /// way the members read where they are declared.
  /// </summary>
  private string Decompose(ulong bits) {
    var remaining = bits;
    var taken = new List<KeyValuePair<ulong, string>>();

    // The zero member names no bit, so it can only ever match exactly; taking it here would prefix
    // every decomposition with "None".
    foreach (var member in this.Members.Where(m => m.Key is not 0).OrderByDescending(m => m.Key)) {
      if ((remaining & member.Key) == member.Key) {
        taken.Add(member);

        remaining &= ~member.Key;
      }
    }

    if (taken.Count is 0) {
      return this.Number(bits);
    }

    var parts = taken.OrderBy(m => m.Key).Select(m => m.Value).ToList();

    if (remaining is not 0) {
      parts.Add(this.Number(remaining));
    }

    return string.Join(" | ", parts);
  }

  /// <summary>
  /// A member's or a value's bits, sign-extended to 64 bits then masked back to the underlying
  /// type's width: without the mask, a negative sbyte member would carry 56 bits the enum cannot
  /// hold and would swallow every other member in a decomposition.
  /// Keying <see cref="Members" /> through this is what makes a member and a value comparable.
  /// </summary>
  public static ulong Bits(object value, Type underlying) {
    var raw = value is ulong unsigned
      ? unsigned
      : unchecked((ulong) Convert.ToInt64(value, CultureInfo.InvariantCulture));

    return raw & EnumTable.WidthMask(underlying);
  }

  private static ulong WidthMask(Type underlying) {
    return Type.GetTypeCode(underlying) switch {
      TypeCode.SByte or TypeCode.Byte => byte.MaxValue,
      TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Char => ushort.MaxValue,
      TypeCode.Int32 or TypeCode.UInt32 => uint.MaxValue,
      _ => ulong.MaxValue
    };
  }

  private string Number(ulong bits) {
    IFormattable typed;

    unchecked {
      typed = Type.GetTypeCode(this.Underlying) switch {
        TypeCode.SByte => (sbyte) bits,
        TypeCode.Byte => (byte) bits,
        TypeCode.Int16 => (short) bits,
        TypeCode.UInt16 or TypeCode.Char => (ushort) bits,
        TypeCode.Int32 => (int) bits,
        TypeCode.UInt32 => (uint) bits,
        TypeCode.Int64 => (long) bits,
        _ => bits
      };
    }

    return typed.ToString(null, CultureInfo.InvariantCulture);
  }
}
