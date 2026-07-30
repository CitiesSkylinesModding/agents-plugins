namespace TestFixture.Missing;

/// <summary>
/// A base type compiled against and never shipped: whatever derives from it fails to load in a
/// debuggee that cannot find this assembly.
/// </summary>
public abstract class Anchor;
