// ReSharper disable UnusedType.Global UnusedMember.Global

using TestFixture.Missing;

namespace TestFixture.Broken;

// The types that cannot load, declared FIRST and LAST on purpose: their slots in the partial array
// the recovery reads land at both ends of it, so the gaps a recovered listing has to survive are
// exercised where they are easiest to lose rather than only in the middle.
public sealed class MissesItsBase : Anchor {
}

public sealed class Loads {
  public int Value;
}

public sealed class AlsoLoads {
}

public sealed class MissesItsBaseToo : Anchor {
}
