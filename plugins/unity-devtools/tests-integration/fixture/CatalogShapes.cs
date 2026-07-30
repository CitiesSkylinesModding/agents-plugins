// ReSharper disable UnusedType.Global UnusedMember.Global

namespace TestFixture.Catalog;

// The namespace repeats the word one type is named after, which is what makes short-name ranking
// observable: searching "Catalog" must put Catalog itself above the neighbors that only share the
// namespace.
public sealed class Catalog;

public sealed class Entry {
  // A nested type: its rendered name carries the declaring type ahead of a '+'.
  public sealed class Nested;
}

// A generic definition, whose name renders with its arity and type parameters.
public sealed class Box<T> {
  public T? Value;
}
