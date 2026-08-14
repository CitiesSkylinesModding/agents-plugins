using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// The attach against a peer that accepts the connection and then says nothing.
/// The SDB handshake has the debuggee greet first and the vendored client's read of that greeting
/// is untimed, so this is the case that would otherwise block the caller forever while it holds the
/// session gate against every other tool.
/// </summary>
public sealed class SdbConnectBoundTests {
  private static readonly TimeSpan GreetingWait = TimeSpan.FromMilliseconds(500);

  [Fact]
  public void GivesUpOnAPeerThatAcceptsAndNeverSpeaks() {
    // Accepts and holds the connection open without writing a byte, which is what an arbitrary TCP
    // listener on the derived port looks like.
    var mute = new TcpListener(IPAddress.Loopback, 0);

    mute.Start();

    try {
      var port = ((IPEndPoint) mute.LocalEndpoint).Port;
      var clock = Stopwatch.StartNew();

      var failure = Assert.ThrowsAny<Exception>(() =>
        SdbSession.Connect("127.0.0.1", port, SdbConnectBoundTests.GreetingWait)
      );

      Assert.Contains("greeting", failure.Message, StringComparison.OrdinalIgnoreCase);

      // Generous against a loaded CI machine, and still orders of magnitude under "forever".
      Assert.True(
        clock.Elapsed < TimeSpan.FromSeconds(10),
        $"the connect took {clock.Elapsed.TotalSeconds:0.#}s to give up"
      );
    }
    finally {
      mute.Stop();
    }
  }
}
