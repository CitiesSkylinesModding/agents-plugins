using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// Which thread <see cref="Invoker" /> routes every invoke to,
/// against a debuggee shaped like a game that names its main thread and runs an unnamed worker:
/// the pick goes by the managed id the runtime gave its first thread, not by the name.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class InvokerMainThreadTests(MonoDebuggeeFixture fx) {
  // The label is built from the picked thread's own managed id and name, so a pick landing on the
  // unnamed worker reads "managed id N, unnamed" and fails here.
  [SkippableFact]
  public void TheMainThreadIsTheOneTheRuntimeAttachedFirstWhateverItsName() {
    Assert.Equal("managed id 1, named \"FixtureMain\"", fx.Invoker.MainThreadLabel);
  }
}
