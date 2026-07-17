// Shim for mono's build/common/Locale.cs (global namespace, like upstream), which the
// vendored Mono.Debugger.Soft sources reference but which lives outside the vendored dir.

using System.Diagnostics.CodeAnalysis;

internal static class Locale {
  public static string GetText(string msg) => msg;

  // CA1305: mirrors mono's Locale.GetText verbatim (gettext-style, current-culture); this
  // localization path in the vendored sources is never invoked.
  [SuppressMessage("Globalization", "CA1305", Justification = "Mirrors mono Locale.GetText")]
  public static string GetText(string fmt, params object[] args) => string.Format(fmt, args);
}
