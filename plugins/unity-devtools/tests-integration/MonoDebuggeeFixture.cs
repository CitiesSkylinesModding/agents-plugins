using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using JetBrains.Annotations;
using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// ONE headless Mono debuggee for the whole suite: resolves a Mono runtime, launches the fixture
/// exe with the SDB agent (<c>suspend=y</c> removes the readiness race), attaches through the real
/// <see cref="SdbSession"/>, and evaluates raw C# strings through the production scope chain.
/// When no Mono runtime resolves, <see cref="SkipReason"/> is set and tests skip instead of fail.
/// Discipline for sharing the debuggee: tests own what they mutate (per-test instances created
/// inside evaluated expressions); shared static fixture roots stay read-only.
/// </summary>
[UsedImplicitly]
public sealed class MonoDebuggeeFixture : IDisposable {
  private readonly Process? debuggee;

  private readonly StringBuilder stderr = new();

  private readonly SdbSession? session;

  private DebugController? debug;

  public MonoDebuggeeFixture() {
    var mono = MonoDebuggeeFixture.ResolveMono();

    if (mono is null) {
      this.SkipReason =
        "no Mono runtime found (set UNITY_DEVTOOLS_MONO, put mono on PATH, or install a " +
        "Windows Unity Editor)";

      return;
    }

    var port = MonoDebuggeeFixture.PickFreePort();

    this.debuggee = Process.Start(
        new ProcessStartInfo {
          FileName = mono,

          // --debug loads the fixture's portable PDB (line tables and local names); without it,
          // the agent reports AbsentInformation for everything.
          Arguments =
            "--debug " +
            $"--debugger-agent=transport=dt_socket,address=127.0.0.1:{port},server=y,suspend=y " +
            $"\"{MonoDebuggeeFixture.DebuggeeOutput("fixture", "UnityDevtools.TestFixture.exe")}\"",
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true
        }
      ) ??
      throw new InvalidOperationException($"failed to start '{mono}'");

    // READY (printed by the fixture's Main after warming up the shared static roots) gates the
    // first eval: mirror reads never trigger class constructors, so evaluating before Main ran
    // would see default values.
    using var ready = new ManualResetEventSlim();

    this.debuggee.OutputDataReceived += (_, e) => {
      if (e.Data is "READY") {
        // ReSharper disable once AccessToDisposedClosure
        ready.Set();
      }
    };

    this.debuggee.ErrorDataReceived += (_, e) => this.stderr.AppendLine(e.Data);
    this.debuggee.BeginOutputReadLine();
    this.debuggee.BeginErrorReadLine();

    try {
      // Attaching resumes the suspend=y debuggee, which then runs Main up to the READY print.
      this.session = this.ConnectWithRetry(port);

      if (!ready.Wait(TimeSpan.FromSeconds(15))) {
        throw new InvalidOperationException(
          $"the Mono debuggee never printed READY; stderr so far:\n{this.stderr}"
        );
      }
    }
    catch {
      this.KillDebuggee();

      throw;
    }
  }

  /// <summary>Why tests must skip; null when the debuggee is up.</summary>
  public string? SkipReason { get; }

  /// <summary>
  /// Evaluates a C# statement sequence against the debuggee and returns the outcome agents would
  /// see, using the production scope chain (with an ECS resolver that always throws: the fixture
  /// has no ECS).
  /// Each call gets a fresh <see cref="EvalState"/> unless the test passes one to assert
  /// <c>_</c> persistence across evals.
  /// </summary>
  public EvalOutcome Eval(string code, EvalState? state = null) {
    var evalState = state ?? new EvalState();

    // Parsing happens inside the window so that the skip check WithInvoker runs first stays the
    // first thing to fire: a parse error escaping ahead of it would fail a suite that must skip.
    return this.WithInvoker(inv => {
        var program = EvalParser.Parse(code);

        var interpreter = new EvalInterpreter(
          inv,
          [
            new BuiltinScope(
              inv,
              () => throw new InvalidOperationException("no ECS in the fixture debuggee"),
              evalState
            )
          ]
        );

        return interpreter.Run(program, evalState);
      }
    );
  }

  /// <summary>
  /// The debuggee's invoke surface, ONE per suite like the session, for tests driving
  /// <see cref="Invoker"/>'s own contract (type and member lookup) rather than the evaluator's.
  /// </summary>
  public Invoker Invoker {
    get {
      Skip.If(this.SkipReason is not null, this.SkipReason);

      if (field is not null) {
        return field;
      }

      var vm = this.session!.Vm;

      // Same discipline as Eval: the Invoker lists threads, which needs a suspend window.
      vm.Suspend();

      try {
        return field = new Invoker(vm);
      }
      finally {
        vm.Resume();
      }
    }
  }

  /// <summary>
  /// Drives the <see cref="Invoker" /> inside the production suspend window, the way
  /// <c>UnitySession.Run</c> does: suspend, act, resume.
  /// </summary>
  public T WithInvoker<T>(Func<Invoker, T> operation) {
    var inv = this.Invoker;
    var vm = this.session!.Vm;

    vm.Suspend();

    try {
      return operation(inv);
    }
    finally {
      vm.Resume();
    }
  }

  /// <summary>
  /// The debuggee's type catalog, ONE per suite like the session, so the harvest is paid once and
  /// later searches exercise the cached path agents actually hit.
  /// A test needing a catalog of its own (a different match budget, a fresh harvest count) builds
  /// one over <see cref="Invoker"/>.
  /// </summary>
  public TypeCatalog Types {
    get {
      // Read through the Invoker accessor first: it owns the skip check and the suspend window the
      // invoker's construction needs.
      var inv = this.Invoker;

      return field ??= new TypeCatalog(inv);
    }
  }

  /// <summary>
  /// Drives the suite's shared catalog inside the production suspend window, like
  /// <see cref="WithInvoker{T}"/>: every harvest is an invoke, so a search outside a window would
  /// be illegal.
  /// </summary>
  public T WithCatalog<T>(Func<TypeCatalog, T> operation) =>
    this.WithCatalog(this.Types, operation);

  /// <summary>Same window, for a test driving a catalog of its own.</summary>
  public T WithCatalog<T>(TypeCatalog catalog, Func<TypeCatalog, T> operation) =>
    this.WithInvoker(_ => operation(catalog));

  /// <summary>The assembly whose types only partly load, as the catalog reports it.</summary>
  public const string PartlyLoadableAssembly = "UnityDevtools.TestFixture.Broken";

  /// <summary>
  /// Puts that assembly in the debuggee: it loads from its own output directory, where the
  /// dependency half its types derive from is deliberately absent, so loading it succeeds and
  /// enumerating its types does not.
  /// One-way for the shared debuggee, like every assembly a test loads, and idempotent: LoadFrom
  /// answers the one already loaded.
  /// </summary>
  public void LoadPartlyLoadableAssembly() {
    // Forward slashes: the path is spliced into a C# literal the debuggee evaluates, where a
    // Windows separator would read as an escape.
    var path = MonoDebuggeeFixture
      .DebuggeeOutput("broken", $"{MonoDebuggeeFixture.PartlyLoadableAssembly}.dll")
      .Replace('\\', '/');

    _ = this.Eval($"System.Reflection.Assembly.LoadFrom(\"{path}\")");
  }

  /// <summary>
  /// The debuggee's breakpoint/pause surface, ONE per suite like the session; tests must remove
  /// their requests and release their pauses (see <see cref="ReleaseDebugger"/>), or every later
  /// test evaluates against a frozen debuggee.
  /// </summary>
  public DebugController Debug {
    get {
      // Read through the Invoker accessor first: it owns the skip check and the suspend window
      // the invoker's construction needs.
      var inv = this.Invoker;

      return this.debug ??= new DebugController(this.session!.Vm, inv);
    }
  }

  /// <summary>
  /// Evaluates in a frame of the CURRENT pause's thread, through the production frame-scope chain
  /// (the contract debug_evaluate serves to agents).
  /// </summary>
  public EvalOutcome DebugEval(string code, int frameIndex = 0) {
    var pause = this.Debug.CurrentPause ??
      throw new InvalidOperationException("not paused; arm and hit a breakpoint first");

    return this.Debug.EvaluateInFrame(
      EvalParser.Parse(code),
      pause.Thread,
      frameIndex,
      new EvalState(),
      []
    );
  }

  /// <summary>
  /// Per-test cleanup: removes every debug request, then drains pauses until none appears for a
  /// settle window.
  /// The dwell matters: a set the pump matched BEFORE RemoveAll can publish its pause moments AFTER
  /// a single check, and a leaked pause freezes the shared debuggee for every later test (events
  /// arriving after RemoveAll match nothing and auto-resume, so a quiet window means quiescence).
  /// </summary>
  public void ReleaseDebugger() {
    if (this.debug is null) {
      return;
    }

    _ = this.debug.RemoveAll();

    for (var quiet = 0; quiet < 4; quiet++) {
      if (this.debug.TryResumeFromPause()) {
        quiet = -1;
      }

      Thread.Sleep(50);
    }
  }

  public void Dispose() {
    this.debug?.Dispose();
    this.session?.Dispose();
    this.KillDebuggee();
  }

  private SdbSession ConnectWithRetry(int port) {
    // The agent needs a moment to open its listen socket; suspend=y guarantees the program itself
    // waits for us, so retrying the TCP connect is the only readiness handling needed.
    for (var deadline = DateTime.UtcNow.AddSeconds(15);;) {
      if (this.debuggee!.HasExited) {
        throw new InvalidOperationException(
          $"the Mono debuggee exited with code {this.debuggee.ExitCode} before attach"
        );
      }

      try {
        return SdbSession.Connect("127.0.0.1", port);
      }
      catch (SocketException) when (DateTime.UtcNow < deadline) {
        Thread.Sleep(100);
      }
    }
  }

  private void KillDebuggee() {
    if (this.debuggee is null) {
      return;
    }

    try {
      this.debuggee.Kill(true);
    }
    catch {
      // Already gone.
    }

    this.debuggee.Dispose();
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
      .. MonoDebuggeeFixture.Subdirectories(programFiles, "Unity*"),
      .. MonoDebuggeeFixture.Subdirectories(Path.Combine(programFiles, "Unity", "Hub", "Editor"))
    ];

    return editorRoots.Select(root => Path.Combine(root, suffix)).FirstOrDefault(File.Exists);
  }

  private static IEnumerable<string> Subdirectories(string parent, string pattern = "*") =>
    Directory.Exists(parent) ? Directory.EnumerateDirectories(parent, pattern) : [];

  private static int PickFreePort() {
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
  private static string DebuggeeOutput(string project, string file) {
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
}

[CollectionDefinition(MonoDebuggeeCollection.Name)]
public sealed class MonoDebuggeeCollection : ICollectionFixture<MonoDebuggeeFixture> {
  public const string Name = "mono debuggee";
}
