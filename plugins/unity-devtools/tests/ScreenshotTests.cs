using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Debugger.Soft;
using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.Tests;

/// <summary>
/// The screenshot orchestration, driven over a scripted debuggee: no game, no socket, and no
/// UnityEngine assembly, which is why the capture's sequencing lives in the library rather than in
/// the MCP tool.
/// The fake answers in the evaluator's rendering shape by hand -- a string quoted, a bool as Mono
/// capitalizes it -- rather than through the real formatter, which needs a live VM. So these hold
/// the orchestration's own reading of those answers, and a change to Invoker.Format would pass
/// here and fail against a game.
/// </summary>
public sealed class ScreenshotTests {
  /// <summary>
  /// A real 3x2 PNG, so the header parse is exercised on bytes a decoder accepts.
  /// </summary>
  private const string ThreeByTwoPng =
    "iVBORw0KGgoAAAANSUhEUgAAAAMAAAACCAIAAAASFvFNAAAAHElEQVR42mNgUPXKn7LzHrMGA7dBaNX8Iy/5jAE+" +
    "uwbMSNUk2AAAAABJRU5ErkJggg==";

  /// <summary>A real 1x1 PNG, whose dimensions no square-image mix-up could produce.</summary>
  private const string OneByOnePng =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mNgUPUCAACYAHBuFEAqAAAAAElFTkSuQmCC";

  /// <summary>One capture window's worth of game time, as the orchestration spends it.</summary>
  private static readonly TimeSpan OneWindow = TimeSpan.FromSeconds(0.1);

  [Fact]
  public void CaptureRequestsRunsOneWindowAndReturnsTheDecodedBytes() {
    var debuggee = new ScriptedDebuggee();

    var capture = new Screenshot(debuggee).Capture();

    Assert.Equal(Convert.FromBase64String(ScreenshotTests.ThreeByTwoPng), capture.Png);
    Assert.Equal(ScreenshotTests.OneWindow, capture.Ran);
    Assert.Equal(1, debuggee.WindowsSpent);

    // Three evaluations, and the only delete is the one the request program runs before writing.
    Assert.Collection(
      debuggee.Programs,
      program => Assert.Contains("UnityEngine.ScreenCapture.CaptureScreenshot(p)", program),
      program => Assert.Contains("System.IO.File.Exists(", program),
      program => Assert.Contains("System.Convert.ToBase64String(", program)
    );

    Assert.Contains("System.IO.File.Delete(p)", debuggee.Programs[0], StringComparison.Ordinal);
  }

  [Fact]
  public void CaptureReportsThePixelDimensionsReadFromThePngHeader() {
    var wide = new Screenshot(new ScriptedDebuggee()).Capture();

    Assert.Equal(3, wide.Width);
    Assert.Equal(2, wide.Height);

    var single = new Screenshot(
      new ScriptedDebuggee {
        Png = ScreenshotTests.OneByOnePng
      }
    ).Capture();

    Assert.Equal(1, single.Width);
    Assert.Equal(1, single.Height);
  }

  [Fact]
  public void CaptureRunsOneMoreWindowWhenTheFileIsLate() {
    var debuggee = new ScriptedDebuggee {
      LandsAfterWindows = 2
    };

    var capture = new Screenshot(debuggee).Capture();

    Assert.Equal(2, debuggee.WindowsSpent);
    Assert.Equal(Convert.FromBase64String(ScreenshotTests.ThreeByTwoPng), capture.Png);

    // Reported as the game time it actually cost, since one window is many frames or none.
    Assert.Equal(ScreenshotTests.OneWindow * 2, capture.Ran);
  }

  [Fact]
  public void CaptureAbsorbsAGameTooBusyEncodingToBeSuspended() {
    // Reproduced live: a capture large enough to encode slowly keeps the game's main thread busy,
    // and the suspend the next read needs answers NOT_SUSPENDED past the invoker's retries. It is
    // the same lateness a missing file is, so the budget covers it rather than the caller seeing
    // a wire word for a capture that only needed another window.
    var debuggee = new ScriptedDebuggee {
      BusyForWindows = 1
    };

    var capture = new Screenshot(debuggee).Capture();

    Assert.Equal(2, debuggee.WindowsSpent);
    Assert.Equal(Convert.FromBase64String(ScreenshotTests.ThreeByTwoPng), capture.Png);
  }

  [Fact]
  public void ACaptureRequestTheGameIsTooBusyToTakeSaysSoRatherThanQuotingTheWire() {
    // The request runs before any window, so there is none to spend against a busy main thread:
    // the caller retries instead, and must be told that rather than handed NOT_SUSPENDED.
    var debuggee = new ScriptedDebuggee {
      BusyForRequest = true
    };

    var failure =
      Assert.Throws<InvalidOperationException>(() => new Screenshot(debuggee).Capture());

    Assert.Contains("too busy", failure.Message, StringComparison.Ordinal);
    Assert.Contains("capture again", failure.Message, StringComparison.Ordinal);

    // Nothing was asked of the game beyond the request, and the hold went back.
    Assert.Equal(0, debuggee.WindowsSpent);
    Assert.Equal(0, debuggee.Held);
  }

  [Fact]
  public void CaptureThatFailsStillReportsAHoldItCouldNotGiveBack() {
    // A capture that failed says nothing about whether the caller's own window survived it, so
    // the release fault rides out on the message the way HoldFault rides out on a success.
    var debuggee = new ScriptedDebuggee {
      LandsAfterWindows = int.MaxValue,
      ResumeThrows = true
    };

    var failure =
      Assert.Throws<InvalidOperationException>(() => new Screenshot(debuggee).Capture());

    Assert.Contains("wrote no screenshot", failure.Message, StringComparison.Ordinal);
    Assert.Contains(
      "could not give its suspension back",
      failure.Message,
      StringComparison.Ordinal
    );
    Assert.Contains("no suspension is held", failure.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void CaptureFailsAfterTheRetryNamingTheLikelyCauses() {
    var debuggee = new ScriptedDebuggee {
      LandsAfterWindows = int.MaxValue
    };

    var failure =
      Assert.Throws<InvalidOperationException>(() => new Screenshot(debuggee).Capture());

    // An encode that has not landed yet is the cause the caller can act on, and the only one they
    // act on by retrying, so it leads the two they cannot mistake it for.
    Assert.Contains("capture again", failure.Message, StringComparison.Ordinal);
    Assert.Contains("minimized", failure.Message, StringComparison.Ordinal);
    Assert.Contains("background", failure.Message, StringComparison.Ordinal);
    Assert.Contains("no screen at all", failure.Message, StringComparison.Ordinal);

    // Bounded: a game that will never render must not hold the call open window after window.
    Assert.Equal(2, debuggee.WindowsSpent);
  }

  [Fact]
  public void CaptureReportsAPauseThatHitDuringItsOwnWindow() {
    var debuggee = new ScriptedDebuggee {
      PauseAfterWindow = true
    };

    var capture = new Screenshot(debuggee).Capture();

    // The game is stopped at a breakpoint the caller never armed a wait for; a successful-looking
    // image that hides it leaves them driving a frozen game.
    Assert.True(capture.Paused);
    Assert.False(new Screenshot(new ScriptedDebuggee()).Capture().Paused);
  }

  [Fact]
  public void CaptureStopsAtAPauseInsteadOfSpendingAWindowThatCannotRender() {
    var debuggee = new ScriptedDebuggee {
      PauseAfterWindow = true,
      LandsAfterWindows = 2
    };

    var failure =
      Assert.Throws<InvalidOperationException>(() => new Screenshot(debuggee).Capture());

    // The realistic shape: a hit stops the game, so the file never lands. Spending the second
    // window would throw on the pause with the advance window's wording instead of this one.
    Assert.Contains("breakpoint", failure.Message, StringComparison.Ordinal);
    Assert.Contains("debug_step action=resume", failure.Message, StringComparison.Ordinal);
    Assert.Equal(1, debuggee.WindowsSpent);
  }

  [Fact]
  public void CaptureRefusesAPauseAlreadyHoldingTheGameWithoutTouchingIt() {
    var debuggee = new ScriptedDebuggee {
      HeldByDebugPause = true
    };

    var failure =
      Assert.Throws<InvalidOperationException>(() => new Screenshot(debuggee).Capture());

    Assert.Contains("debug_step action=resume", failure.Message, StringComparison.Ordinal);

    // A capture that cannot work must leave nothing behind on its way to saying so: no queued
    // capture request to fire whenever the caller resumes, and no hold to strand.
    Assert.Empty(debuggee.Programs);
    Assert.Equal(0, debuggee.SuspendsTaken);
  }

  [Fact]
  public void CaptureKeepsTheImageWhenReleasingItsHoldFails() {
    var debuggee = new ScriptedDebuggee {
      ResumeThrows = true
    };

    var capture = new Screenshot(debuggee).Capture();

    // Whatever the release failed on, it must not cost the caller an image already in hand.
    Assert.Equal(Convert.FromBase64String(ScreenshotTests.ThreeByTwoPng), capture.Png);

    // Reported rather than discarded: whichever way the release failed, the caller's next write
    // lands on a game that is not where they left it.
    Assert.Equal("no suspension is held", capture.HoldFault);
    Assert.Null(new Screenshot(new ScriptedDebuggee()).Capture().HoldFault);
  }

  [Fact]
  public void CaptureTakesTheTempPathFromTheDebuggeesOwnAnswer() {
    var debuggee = new ScriptedDebuggee {
      TempDirectory = "/answered/by//the/game/"
    };

    _ = new Screenshot(debuggee).Capture();

    // The request resolves the path on the game machine; every later program then quotes back what
    // it answered, rather than anything this process could know about that filesystem.
    Assert.Contains("System.IO.Path.GetTempPath()", debuggee.Programs[0]);

    Assert.All(
      debuggee.Programs.Skip(1),
      program => Assert.Contains(
        "\"/answered/by//the/game/unity-devtools-screenshot.png\"",
        program,
        StringComparison.Ordinal
      )
    );
  }

  [Fact]
  public void CaptureQuotesAWindowsPathBackAsAParsableLiteral() {
    var debuggee = new ScriptedDebuggee {
      TempDirectory = @"C:\Users\Someone\AppData\Local\Temp\"
    };

    _ = new Screenshot(debuggee).Capture();

    Assert.All(
      debuggee.Programs.Skip(1),
      program => Assert.Contains(
        @"""C:\\Users\\Someone\\AppData\\Local\\Temp\\unity-devtools-screenshot.png""",
        program,
        StringComparison.Ordinal
      )
    );
  }

  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  public void CaptureLeavesTheCallersHeldSuspendCountUnchanged(int heldOnEntry) {
    var succeeding = new ScriptedDebuggee {
      Held = heldOnEntry
    };

    _ = new Screenshot(succeeding).Capture();

    Assert.Equal(heldOnEntry, succeeding.Held);

    var failing = new ScriptedDebuggee {
      Held = heldOnEntry,
      LandsAfterWindows = int.MaxValue
    };

    _ = Assert.Throws<InvalidOperationException>(() => new Screenshot(failing).Capture());

    Assert.Equal(heldOnEntry, failing.Held);

    // Always its own hold, never a borrowed one: reading the caller's count and then relying on it
    // would let a concurrent tool call release the hold this capture is standing on.
    Assert.Equal(1, succeeding.SuspendsTaken);
    Assert.Equal(1, failing.SuspendsTaken);
  }

  [Theory]
  [InlineData("/tmp/")]
  [InlineData(@"C:\Users\Someone\AppData\Local\Temp\")]
  [InlineData("/tmp/with a space/")]
  [InlineData("/tmp/with\ttab/")]
  [InlineData("/tmp/with\nnewline/")]
  [InlineData("/tmp/with\"quote/")]
  public void EveryProgramACaptureGeneratesParsesAsTheRealGrammar(string tempDirectory) {
    var debuggee = new ScriptedDebuggee {
      TempDirectory = tempDirectory
    };

    _ = new Screenshot(debuggee).Capture();

    Assert.NotEmpty(debuggee.Programs);

    // The fake accepts any string, so only the real parser can say whether the path a game
    // answered survives being quoted back into a program the game will be asked to run.
    foreach (var program in debuggee.Programs) {
      _ = EvalParser.Parse(program);
    }
  }

  [Theory]
  [InlineData(0)]
  [InlineData(8)]
  [InlineData(32)]
  public void CaptureAbsorbsAFileStillBeingWrittenWhateverHasLanded(int bytesSoFar) {
    var debuggee = new ScriptedDebuggee {
      WholeAfterWindows = 2,
      PartialBytes = bytesSoFar
    };

    var capture = new Screenshot(debuggee).Capture();

    // A read too short to judge is the same lateness as one with a header and no tail: the engine
    // creates the file before it writes it, so both are a capture arriving mid-write.
    Assert.Equal(Convert.FromBase64String(ScreenshotTests.ThreeByTwoPng), capture.Png);
    Assert.Equal(2, debuggee.WindowsSpent);
  }

  [Fact]
  public void CaptureFailsWhenTheFileIsStillPartialAfterTheBudget() {
    var debuggee = new ScriptedDebuggee {
      WholeAfterWindows = int.MaxValue
    };

    var failure =
      Assert.Throws<InvalidOperationException>(() => new Screenshot(debuggee).Capture());

    // Named apart from "no file at all": the next move is another capture, not a look at the
    // game's window.
    Assert.Contains("still writing", failure.Message, StringComparison.Ordinal);
    Assert.Equal(2, debuggee.WindowsSpent);
  }

  [Fact]
  public void CaptureFailsWhenWhatCameBackIsNotAPng() {
    var debuggee = new ScriptedDebuggee {
      // Long enough to judge, so it is garbage rather than a file mid-write.
      Png = Convert.ToBase64String("not an image, and long enough to say so"u8.ToArray())
    };

    var failure =
      Assert.Throws<InvalidOperationException>(() => new Screenshot(debuggee).Capture());

    Assert.Contains("not a PNG", failure.Message, StringComparison.Ordinal);
  }

  /// <summary>
  /// A debuggee that answers the capture's three programs from a script, and refuses to advance a
  /// game nothing is holding, the way the live advance window does.
  /// </summary>
  private sealed class ScriptedDebuggee : IScreenshotDebuggee {
    /// <summary>Suspensions held right now, the caller's own plus the capture's.</summary>
    public int Held { get; set; }

    /// <summary>Suspensions the capture took for itself.</summary>
    public int SuspendsTaken { get; private set; }

    public int WindowsSpent { get; private set; }

    /// <summary>Every program the capture evaluated, in order.</summary>
    public List<string> Programs { get; } = [];

    /// <summary>What the game answers for its own temp directory.</summary>
    public string TempDirectory { get; init; } = "/tmp/";

    /// <summary>The base64 the game hands back for the file.</summary>
    public string Png { get; init; } = ScreenshotTests.ThreeByTwoPng;

    /// <summary>How many windows the pending capture takes to land.</summary>
    public int LandsAfterWindows { get; init; } = 1;

    /// <summary>How many windows before the file the game hands back is finished.</summary>
    public int WholeAfterWindows { get; init; } = 1;

    /// <summary>How much of the file has landed before then.</summary>
    public int PartialBytes { get; init; } = 32;

    /// <summary>
    /// How many windows the game spends too busy encoding for a suspend to land, answering
    /// NOT_SUSPENDED the way the wire does when the main thread will not stop.
    /// </summary>
    public int BusyForWindows { get; init; }

    /// <summary>
    /// Whether the game is too busy to be stopped for the capture REQUEST, which is what a
    /// previous capture still encoding does to the one after it.
    /// </summary>
    public bool BusyForRequest { get; init; }

    /// <summary>Whether a debug pause holds the game once a window closes.</summary>
    public bool PauseAfterWindow { get; init; }

    /// <summary>Whether a debug pause already holds the game when the capture starts.</summary>
    public bool HeldByDebugPause { get; init; }

    /// <summary>
    /// The live release fails when a disconnect inside the capture has already zeroed the count,
    /// or when the resume itself fails and leaves the capture's suspension on the game.
    /// </summary>
    public bool ResumeThrows { get; init; }

    public void SuspendHold() {
      this.Held++;
      this.SuspendsTaken++;
    }

    public void ResumeHold() {
      if (this.ResumeThrows) {
        throw new InvalidOperationException("no suspension is held");
      }

      if (this.Held is 0) {
        throw new InvalidOperationException("released a suspension nothing was holding");
      }

      this.Held--;
    }

    public bool SpendWindow() {
      if (this.Held is 0) {
        throw new InvalidOperationException("the advance window needs a held suspension");
      }

      this.WindowsSpent++;

      return this.PauseAfterWindow;
    }

    public string Evaluate(string code) {
      this.Programs.Add(code);

      if (code.Contains("CaptureScreenshot")) {
        if (this.BusyForRequest) {
          throw new VMNotSuspendedException();
        }

        return $"\"{this.TempDirectory}unity-devtools-screenshot.png\"";
      }

      if (code.Contains("File.Exists")) {
        if (this.WindowsSpent <= this.BusyForWindows) {
          throw new VMNotSuspendedException();
        }

        // Mono renders a bool the way .NET's ToString does, capitalized.
        return this.WindowsSpent >= this.LandsAfterWindows ? "True" : "False";
      }

      if (code.Contains("ToBase64String")) {
        if (this.WindowsSpent >= this.WholeAfterWindows) {
          return $"\"{this.Png}\"";
        }

        // Still being written: whatever the engine has flushed so far, which early on is less
        // than a PNG header.
        var head = Convert.FromBase64String(this.Png).AsSpan(0, this.PartialBytes).ToArray();

        return $"\"{Convert.ToBase64String(head)}\"";
      }

      throw new InvalidOperationException($"the capture evaluated an unscripted program: {code}");
    }
  }
}
