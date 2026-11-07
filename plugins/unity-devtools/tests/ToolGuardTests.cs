using System;
using ModelContextProtocol;
using UnityDevtools.Mcp;
using Xunit;

namespace UnityDevtools.Sdb.Tests;

/// <summary>
/// The guard every tool body runs under, which decides the error text a client reads.
/// </summary>
public sealed class ToolGuardTests {
  [Theory]
  [InlineData("")]
  [InlineData("  ")]
  public void AnExceptionWithNoMessageSurfacesAsItsTypeName(string message) {
    var ex = Assert.Throws<McpException>(() =>
      ToolGuard.Run<int>(() => throw new InvalidOperationException(message))
    );

    Assert.Equal(nameof(InvalidOperationException), ex.Message);
  }

  [Fact]
  public void AnExceptionWithAMessageSurfacesAsThatMessage() {
    var ex = Assert.Throws<McpException>(() =>
      ToolGuard.Run<int>(() => throw new InvalidOperationException("the slot is held"))
    );

    Assert.Equal("the slot is held", ex.Message);
  }

  [Fact]
  public void AnMcpExceptionAToolThrewPassesThroughUnchanged() {
    var thrown = new McpException("give exactly one of fullName or search");

    var ex = Assert.Throws<McpException>(() => ToolGuard.Run<int>(() => throw thrown));

    Assert.Same(thrown, ex);
  }
}
