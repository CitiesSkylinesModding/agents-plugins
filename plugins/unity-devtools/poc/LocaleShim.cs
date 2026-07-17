// Shim for mono's build/common/Locale.cs (global namespace, like upstream), which the
// vendored Mono.Debugger.Soft sources reference but which lives outside the vendored dir.

internal static class Locale {
  public static string GetText(string msg) => msg;

  public static string GetText(string fmt, params object[] args) => string.Format(fmt, args);
}
