using System.Diagnostics;

namespace UnityDevtools.Sdb;

/// <summary>
/// How a component type is stored, which is what decides the accessor that can read it.
/// The distinction is a safety boundary rather than a label: a buffer element shares its type index
/// with a component of the same name, so a kind that stops matching sends a buffer's header into
/// GetComponentData, which on a player build fabricates a value instead of failing.
/// </summary>
public enum EcsKind {
  /// <summary>A plain unmanaged component: the only kind an accessor here can read.</summary>
  Component,

  /// <summary>A zero-sized component, where carrying it IS the state.</summary>
  Tag,

  /// <summary>A dynamic buffer of elements, reached through the buffer accessors.</summary>
  Buffer,

  /// <summary>A shared component, whose data lives outside the entity.</summary>
  Shared,

  /// <summary>A chunk component, whose data lives on the chunk, not the entity.</summary>
  Chunk,

  /// <summary>A managed component, which the entity stores as a reference.</summary>
  Managed
}

public static class EcsKinds {
  extension(EcsKind kind) {
    /// <summary>
    /// The word this kind is reported by, which every caller routes on.
    /// Spelled out here rather than derived from the member names: the words are the tool's
    /// contract, so neither a rename nor a serializer's naming policy may reach them.
    /// </summary>
    public string Wire =>
      kind switch {
        EcsKind.Component => "component",
        EcsKind.Tag => "tag",
        EcsKind.Buffer => "buffer",
        EcsKind.Shared => "shared",
        EcsKind.Chunk => "chunk",
        EcsKind.Managed => "managed",
        _ => throw new UnreachableException($"no wire word for storage kind {kind}")
      };
  }
}
