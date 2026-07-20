using ModelContextProtocol;

namespace UnityDevtools.Mcp;

/// <summary>
/// Wraps tool bodies so failures surface with their real message: the SDK reports unhandled
/// exceptions as an opaque "an error occurred", while an <see cref="McpException"/> message
/// reaches the client verbatim.
/// </summary>
internal static class ToolGuard {
  public static T Run<T>(Func<T> operation) {
    try {
      return operation();
    }
    catch (McpException) {
      throw;
    }
    catch (Exception ex) {
      throw new McpException(ex.Message);
    }
  }
}
