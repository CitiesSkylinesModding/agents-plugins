using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// Launching the net472 fixture program under a Mono runtime with the SDB agent listening, shared
/// by the suite's collection fixture and by the tests that need a debuggee of their own.
/// </summary>
internal static class MonoDebuggee {
  /// <summary>
  /// The Mono runtime to launch, or null when none resolves (tests skip).
  /// </summary>
  internal static string? Runtime { get; } = MonoDebuggee.ResolveMono();

  /// <summary>
  /// Why tests must skip; null when a runtime resolved.
  /// </summary>
  internal static string? SkipReason =>
    MonoDebuggee.Runtime is null
      ? "no Mono runtime found (set UNITY_DEVTOOLS_MONO, put mono on PATH, or install a " +
      "Windows Unity Editor)"
      : null;

  /// <summary>
  /// Starts the fixture exe with the agent bound to <paramref name="port"/>, both output streams
  /// redirected so the caller can read them (and must drain them).
  /// <paramref name="suspend"/> makes the program wait for a debugger before running its Main,
  /// which removes the race between "the agent is listening" and "the program is ready".
  /// </summary>
  internal static Process Start(int port, bool suspend) {
    var suspendFlag = suspend ? "y" : "n";

    return Process.Start(
        new ProcessStartInfo {
          FileName = MonoDebuggee.Runtime!,

          // --debug loads the fixture's portable PDB (line tables and local names); without it,
          // the agent reports AbsentInformation for everything.
          Arguments =
            "--debug " +
            $"--debugger-agent=transport=dt_socket,address=127.0.0.1:{port},server=y," +
            $"suspend={suspendFlag} " +
            $"\"{MonoDebuggee.Output("fixture", "UnityDevtools.TestFixture.exe")}\"",
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true
        }
      ) ??
      throw new InvalidOperationException($"failed to start '{MonoDebuggee.Runtime}'");
  }

  /// <summary>
  /// Ends a debuggee, whether it is still running or not.
  /// </summary>
  internal static void Kill(Process debuggee) {
    try {
      debuggee.Kill(true);
      debuggee.WaitForExit(TimeSpan.FromSeconds(5));
    }
    catch {
      // Already gone.
    }
  }

  internal static int PickFreePort() {
    var listener = new TcpListener(IPAddress.Loopback, 0);

    listener.Start();

    var port = ((IPEndPoint) listener.LocalEndpoint).Port;

    listener.Stop();

    return port;
  }

  /// <summary>
  /// A file in a sibling debuggee project's output: own output is
  /// tests-integration/bin/&lt;Config&gt;/net10.0/, and every net472 project beside it (all built
  /// for any test run by the ReferenceOutputAssembly=false project references) builds under its
  /// own bin/ with the same configuration.
  /// </summary>
  internal static string Output(string project, string file) {
    var output = new DirectoryInfo(
      AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
    );

    var configuration = output.Parent!.Name;
    var projectDir = output.Parent!.Parent!.Parent!.FullName;

    var path = Path.Combine(projectDir, project, "bin", configuration, "net472", file);

    return File.Exists(path)
      ? path
      : throw new FileNotFoundException($"'{file}' not found at '{path}'; build the solution");
  }

  /// <summary>
  /// Resolution order: UNITY_DEVTOOLS_MONO (path to a mono executable) → mono on PATH → well-known
  /// Windows Unity Editor locations.
  /// Null when nothing resolves (tests skip).
  /// </summary>
  private static string? ResolveMono() {
    var configured = Environment.GetEnvironmentVariable("UNITY_DEVTOOLS_MONO");

    if (!string.IsNullOrEmpty(configured)) {
      return configured;
    }

    var exeName = OperatingSystem.IsWindows() ? "mono.exe" : "mono";

    var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "")
      .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
      .Select(dir => Path.Combine(dir.Trim(), exeName))
      .FirstOrDefault(File.Exists);

    if (onPath is not null || !OperatingSystem.IsWindows()) {
      return onPath;
    }

    var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    var suffix = Path.Combine("Editor", "Data", "MonoBleedingEdge", "bin", "mono.exe");

    // Direct installs (C:\Program Files\Unity <version>\...) and Unity Hub installs.
    IEnumerable<string> editorRoots = [
      .. MonoDebuggee.Subdirectories(programFiles, "Unity*"),
      .. MonoDebuggee.Subdirectories(Path.Combine(programFiles, "Unity", "Hub", "Editor"))
    ];

    return editorRoots.Select(root => Path.Combine(root, suffix)).FirstOrDefault(File.Exists);
  }

  private static IEnumerable<string> Subdirectories(string parent, string pattern = "*") =>
    Directory.Exists(parent) ? Directory.EnumerateDirectories(parent, pattern) : [];
}
