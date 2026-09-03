using System;
using System.Buffers.Binary;
using System.Globalization;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Debugger.Soft;
using UnityDevtools.Sdb.Eval;

namespace UnityDevtools.Sdb;

/// <summary>
/// The debuggee surface one capture drives, narrow enough that the orchestration runs against
/// scripted answers with no game, no socket and no Unity assembly.
/// </summary>
public interface IScreenshotDebuggee {
  /// <summary>
  /// Whether a breakpoint/step/exception pause holds the game. Nothing renders under one, and no
  /// capture may lift it: releasing someone's pause is their decision, not a screenshot's.
  /// </summary>
  bool HeldByDebugPause { get; }

  /// <summary>Takes one suspension; a capture releases exactly what it took.</summary>
  void SuspendHold();

  /// <summary>Releases one suspension taken by <see cref="SuspendHold"/>.</summary>
  void ResumeHold();

  /// <summary>
  /// Evaluates a statement sequence against the debuggee and returns the final expression's
  /// FORMATTED value, so a string arrives quoted the way every other eval result does.
  /// </summary>
  string Evaluate(string code);

  /// <summary>
  /// Lets the game run the capture window and re-freezes it, so a pending capture gets the
  /// rendered frame it is waiting on. Requires a held suspension.
  /// Returns whether a breakpoint/step/exception pause holds the game after the window.
  /// </summary>
  bool SpendWindow();
}

/// <summary>
/// One screenshot of the composited frame, taken over the shipped expression evaluator: the
/// debuggee writes a PNG to its own temp directory and the bytes come back base64 over the SDB
/// string channel.
/// A capture is a REQUEST that only a rendered frame fulfils, so this owns the whole sequence --
/// request, run the game, confirm, read back -- rather than leaving the frame to the caller, who
/// would have to know that a capture is not an image.
/// </summary>
public sealed class Screenshot(IScreenshotDebuggee debuggee) {
  /// <summary>
  /// One fixed name, overwritten on every call, so captures cannot accumulate on the game
  /// machine under names nothing will ever collect.
  /// </summary>
  private const string FileName = "unity-devtools-screenshot.png";

  /// <summary>
  /// Windows one capture may spend. Two: one more than it takes covers every way a capture is
  /// merely late rather than impossible -- the file not landed, the game too busy encoding to be
  /// suspended, the file still being written -- while a game that will never render must not hang
  /// the call. A cause that needs a third window needs a retry from the caller instead, which is
  /// what the failure message asks for.
  /// Together with <see cref="Window"/> this bounds how long a capture runs the game, a figure
  /// quoted by the screenshot tool's description, the unity-driving skill, and the roadmap's
  /// native-capture entry: retuning either constant makes all three wrong.
  /// </summary>
  private const int WindowBudget = 2;

  /// <summary>
  /// Signature plus the IHDR chunk header and its two dimensions: the least a read has to carry
  /// before anything about it can be judged.
  /// </summary>
  private const int HeaderLength = 24;

  /// <summary>
  /// Said in the capture's own words on every route into it -- a pause already holding the game,
  /// one that hits inside the capture's window, and one that arrives between the two -- since the
  /// advance window's wording is written for a caller who asked for time to pass.
  /// It names a retry because the debug controller reports a suspension while it is still
  /// classifying an event set, so a conditional breakpoint auto-resuming on a false condition
  /// reaches here as a pause that no debug tool will then admit to holding.
  /// </summary>
  private const string PausedMessage =
    "a breakpoint/step/exception pause is holding the game, or one was being classified as this " +
    "call arrived, so it renders nothing to capture; capture again if debug_pause_state reports " +
    "no pause, and otherwise inspect it with the debug tools or release it with debug_step " +
    "action=resume";

  /// <summary>
  /// How long one window lets the game run. This is wall-clock time during which the game renders
  /// whatever it renders, NOT a frame count: at 60 fps it covers several frames, and on a slow
  /// renderer it may not cover one.
  /// </summary>
  private static readonly TimeSpan Window = TimeSpan.FromSeconds(0.1);

  private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

  /// <summary>
  /// The capture request. It resolves its own path debuggee-side, since the server knows nothing
  /// of the debuggee's filesystem, and clears any stale file in the same sequence so a capture
  /// that never renders cannot return the previous one.
  /// That delete is also the whole cleanup story: one after the read-back would be a fourth
  /// evaluation whose only failure mode is swallowing a dropped connection that has taken the
  /// caller's suspend window with it.
  /// </summary>
  private static readonly string RequestProgram =
    $"""
     var p = System.IO.Path.GetTempPath() + "{Screenshot.FileName}";
     System.IO.File.Delete(p);
     UnityEngine.ScreenCapture.CaptureScreenshot(p);
     p
     """;

  /// <summary>
  /// Takes one capture over a live session, as a single exclusive sequence: a capture is several
  /// operations that depend on each other, and the session only serializes one at a time.
  /// </summary>
  public static ScreenshotCapture From(UnitySession session) =>
    session.Exclusive(() => new Screenshot(new SessionDebuggee(session)).Capture());

  /// <summary>
  /// Captures the composited frame, letting the game run in windows until it renders one.
  /// The caller's held suspend count is what it was on every path out.
  /// </summary>
  public ScreenshotCapture Capture() {
    // Refused before anything is asked of the debuggee, so a capture that cannot work leaves no
    // request queued to fire whenever the caller resumes.
    if (debuggee.HeldByDebugPause) {
      throw new InvalidOperationException(Screenshot.PausedMessage);
    }

    // Taken unconditionally rather than only when the caller holds nothing: MCP tool calls are not
    // serialized against each other, so reading the held count and then relying on it leaves a
    // window in which another call releases the very hold this capture decided it could use.
    debuggee.SuspendHold();

    ScreenshotCapture capture;

    try {
      capture = this.Take();
    }
    catch (Exception ex) {
      // The same fault the success path reports as HoldFault, and it means as much here: the
      // capture failing does not tell the caller their own window survived it. Carried on the
      // message rather than beside it, since a throw has nowhere else to put it.
      if (Screenshot.Release(debuggee) is {} fault) {
        throw new InvalidOperationException(
          $"{ex.Message}; and the capture could not give its suspension back ({fault})",
          ex
        );
      }

      throw;
    }

    capture.HoldFault = Screenshot.Release(debuggee);

    return capture;
  }

  /// <summary>
  /// Gives the capture's hold back, reporting rather than raising what went wrong, since an image
  /// already in hand should not be lost to it.
  /// Two ways it fails, and the caller needs both: the count is already gone, because a disconnect
  /// inside the capture zeroed it and took their own window with it; or the resume itself failed,
  /// which leaves our extra suspension on the game.
  /// </summary>
  private static string Release(IScreenshotDebuggee debuggee) {
    try {
      debuggee.ResumeHold();

      return null;
    }
    catch (Exception ex) {
      return ex.Message;
    }
  }

  /// <summary>The capture proper, inside the suspension <see cref="Capture"/> holds.</summary>
  private ScreenshotCapture Take() {
    string path;

    try {
      path = this.Answer(Screenshot.RequestProgram);
    }
    catch (VMNotSuspendedException) {
      // The same busy main thread the loop below absorbs, one statement earlier: a previous
      // capture still encoding is what makes back-to-back captures meet it here. No window can be
      // spent against it, since nothing has been asked of the game yet and the request is what
      // would have asked -- so the caller gets the retry the loop's own failure would have named.
      throw new InvalidOperationException(
        "the game's main thread was too busy to be stopped for the capture request, which a " +
        "previous capture still encoding will do; capture again"
      );
    }

    byte[] png = null;

    var windows = 0;
    var paused = false;
    var partial = false;

    // The budget absorbs every way a capture arrives late, since the caller cannot tell them apart
    // and none is a failure worth surfacing: no file yet, a game too busy encoding for the next
    // read's suspend to land, and a file the engine is still writing.
    // A pause ends the loop instead, because a stopped game will not finish either.
    while (png is null && !paused && windows < Screenshot.WindowBudget) {
      paused = debuggee.SpendWindow();

      windows++;

      byte[] candidate;

      try {
        if (!this.Exists(path)) {
          continue;
        }

        var read = $"System.IO.File.ReadAllBytes({Screenshot.Literal(path)})";

        candidate = Screenshot.Decode(this.Answer($"System.Convert.ToBase64String({read})"));
      }
      catch (VMNotSuspendedException) {
        // The game's own main thread is what encodes the PNG, and a suspend cannot land while it
        // is busy doing so: the wire answers NOT_SUSPENDED past the invoker's retries, which is
        // reachable on a capture large enough to encode slowly. It says what a missing file says
        // -- the capture is not ready -- so the budget absorbs it rather than handing the caller
        // a wire word for the one cause the failure message already tells them to retry.
        continue;
      }

      // Only bytes long enough to judge can be called garbage, and garbage is not lateness, so
      // it fails now rather than spending the rest of the budget. A shorter read is a file the
      // engine has only just created, which is the same lateness as no file at all.
      if (candidate.Length >= Screenshot.HeaderLength) {
        Screenshot.EnsurePng(candidate);
      }

      if (Screenshot.IsWhole(candidate)) {
        png = candidate;
      }
      else {
        partial = true;
      }
    }

    if (png is null) {
      throw new InvalidOperationException(Screenshot.NoImageMessage(windows, paused, partial));
    }

    var (width, height) = Screenshot.Dimensions(png);

    return new ScreenshotCapture {
      Png = png,
      Width = width,
      Height = height,
      Ran = Screenshot.Window * windows,
      Paused = paused
    };
  }

  /// <summary>Whether the pending capture has landed on the game machine's disk.</summary>
  private bool Exists(string path) {
    var answer = this.Answer($"System.IO.File.Exists({Screenshot.Literal(path)})");

    return bool.TryParse(answer, out var exists)
      ? exists
      : throw new InvalidOperationException(
        $"the game answered `{answer}` when asked whether the screenshot file exists"
      );
  }

  /// <summary>
  /// One program's result as a value. The evaluator renders a string quoted, the way it renders
  /// one to an agent; a capture reads what it asked for instead.
  /// </summary>
  private string Answer(string code) => Screenshot.Unquote(debuggee.Evaluate(code));

  private static string Unquote(string formatted) =>
    formatted.Length >= 2 && formatted[0] is '"' && formatted[^1] is '"'
      ? formatted[1..^1]
      : formatted;

  /// <summary>
  /// Renders a debuggee-side path as a C# string literal the evaluator can parse.
  /// Roslyn's own formatter rather than a quote-and-backslash escape, since it is the one that
  /// agrees with the Roslyn lexer the evaluator parses with: a path holding a control character
  /// escapes to a literal that parses, where the hand-rolled form emits a raw newline the lexer
  /// rejects.
  /// </summary>
  private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

  /// <summary>
  /// The image's own dimensions, read off its IHDR header: ground truth about what was delivered,
  /// where asking the game for its screen size would be a second source able to disagree with it.
  /// PNG puts IHDR first by spec -- 8-byte signature, 4-byte chunk length, the tag, then the two
  /// big-endian dimensions.
  /// </summary>
  private static (int Width, int Height) Dimensions(byte[] png) =>
  (
    BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
    BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4))
  );

  /// <summary>The read-back as bytes, saying so when what came back was never base64.</summary>
  private static byte[] Decode(string encoded) {
    try {
      return Convert.FromBase64String(encoded);
    }
    catch (FormatException ex) {
      throw new InvalidOperationException(
        $"the game's screenshot did not come back as base64: {ex.Message}",
        ex
      );
    }
  }

  /// <summary>
  /// Rejects what no further window could turn into an image, leaving lateness to the budget.
  /// </summary>
  private static void EnsurePng(byte[] png) {
    if (!png.AsSpan(0, Screenshot.PngSignature.Length).SequenceEqual(Screenshot.PngSignature)) {
      throw new InvalidOperationException(
        $"the game returned {png.Length.ToString(CultureInfo.InvariantCulture)} bytes that are " +
        "not a PNG"
      );
    }
  }

  /// <summary>
  /// Whether the file is finished. Only the tail can say: a capture read while the engine is
  /// still writing it carries a whole signature and IHDR, so its dimensions look right and only
  /// the pixels are missing.
  /// A live probe never caught that state -- the engine starts writing only once it has encoded,
  /// so every sample was absent or whole -- which makes this a guard against an engine that
  /// flushes as it goes rather than one covering the reference target's observed behaviour.
  /// </summary>
  private static bool IsWhole(byte[] png) =>
    png.Length >= Screenshot.HeaderLength &&
    png.AsSpan(png.Length - 8, 4).SequenceEqual("IEND"u8);

  /// <summary>
  /// Why the budget ran out, in the terms the caller acts on: each cause has a different next
  /// move, and "no image" on its own is the dead end this tool exists to remove.
  /// </summary>
  private static string NoImageMessage(int windows, bool paused, bool partial) {
    if (paused) {
      return Screenshot.PausedMessage;
    }

    var ran = (Screenshot.Window * windows).TotalSeconds
      .ToString("0.0#", CultureInfo.InvariantCulture);

    return partial
      ? $"the game was still writing its screenshot after running {ran} s, so only part of the " +
      "file came back; capture again, and expect it to cost more on a game this slow to encode"
      : $"the game wrote no screenshot after running {ran} s: the engine starts writing only " +
      "once it has encoded the frame, so capture again first -- a large screen, or one drawing " +
      "too slowly to finish a frame, may need longer. If that fails too, its window may be " +
      "minimized on a build that does not run in the background, or the build may have no screen " +
      "at all";
  }

  /// <summary>
  /// The live wiring: eval and the advance window over the shared session.
  /// </summary>
  private sealed class SessionDebuggee(UnitySession session) : IScreenshotDebuggee {
    /// <summary>
    /// Private to the capture, so the base64 payload never lands in the `_` slot an agent reads.
    /// </summary>
    private readonly EvalState state = new();

    public bool HeldByDebugPause => session.DebugOrNull?.HoldsSuspension ?? false;

    // Unreported, since a capture's hold is not a window the caller opened: status would
    // otherwise show them one to give back. It still freezes the game, so the guards that ask
    // whether the game is held do see it, and a step dispatched alongside a capture is refused.
    public void SuspendHold() => _ = session.SuspendHold(reported: false);

    public void ResumeHold() => _ = session.ResumeHold(reported: false);

    public string Evaluate(string code) {
      var program = EvalParser.Parse(code);

      return session.Run(ctx => {
          // No scopes: every expression a capture runs is rooted in a fully-qualified type, so
          // nothing here touches the em/world builtins, whose world selection would cost invokes
          // on behalf of a caller who never named a world.
          var interpreter = new EvalInterpreter(ctx.Invoker, []);

          // The failure travels as its own message alone, never through the evaluator's locals
          // report: a statement failing after binding the base64 string would dump the whole
          // image back as text.
          return interpreter.Run(program, this.state).Formatted;
        }
      );
    }

    public bool SpendWindow() {
      try {
        return session.AdvanceHold(Screenshot.Window);
      }
      catch (InvalidOperationException ex)
        when (ex is not VMNotSuspendedException && session.HeldSuspendCount > 0) {
        // A pause that arrived after Capture's pre-flight, which reads while the game is still
        // running. AdvanceHold refuses it in wording written for a caller who asked for time to
        // pass; reporting it as a pause instead lets the capture say it in PausedMessage, whose
        // retry advice is the whole point for a hit still being classified.
        // The filter reads the hold rather than the pause, since a hit auto-resuming on a false
        // condition is gone again by the time this runs, and it is the case that advice is for.
        // Its other two raises both leave the hold gone, so the count separates them; the wire's
        // own NOT_SUSPENDED does not, and is a fault rather than a pause.
        return true;
      }
    }
  }
}

/// <summary>What one capture delivered.</summary>
public sealed class ScreenshotCapture {
  /// <summary>The PNG bytes as the game encoded them.</summary>
  public required byte[] Png { get; init; }

  public required int Width { get; init; }

  public required int Height { get; init; }

  /// <summary>
  /// How long the game was let run to produce the image, which is what the capture cost a caller
  /// holding a suspend window. Wall-clock time, not a frame count: the renderer draws what it can
  /// in it.
  /// </summary>
  public required TimeSpan Ran { get; init; }

  /// <summary>
  /// Whether a breakpoint/step/exception pause holds the game now. A hit during the capture's own
  /// window leaves the game stopped, which the caller has to hear about.
  /// </summary>
  public required bool Paused { get; init; }

  /// <summary>
  /// Why giving the capture's suspension back failed, or null when it did not. The message says
  /// which of the two it was: the connection went, taking any window the caller held with it; or
  /// the resume failed, leaving this capture's suspension on a game nothing else will release.
  /// Either way the caller has to look before their next write.
  /// </summary>
  public string HoldFault { get; internal set; }
}
