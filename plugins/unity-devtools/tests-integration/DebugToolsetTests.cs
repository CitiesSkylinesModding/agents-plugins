using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// The breakpoint/pause toolset against the live debuggee: the fixture's main loop calls
/// <c>Ticker.Tick(n)</c> every ~10ms, so an armed breakpoint hits within milliseconds and a
/// periodic caught FormatException feeds the exception-break test.
/// Every test releases what it armed (see <see cref="MonoDebuggeeFixture.ReleaseDebugger"/>);
/// a leaked pause would freeze the shared debuggee for every later test.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class DebugToolsetTests(MonoDebuggeeFixture fx) {
  private static readonly TimeSpan HitTimeout = TimeSpan.FromSeconds(15);

  [SkippableFact]
  public void EntryBreakpointPausesAndReportsTheFrame() {
    try {
      var bindings = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.Ticker",
          MethodName = "Tick"
        }
      );

      var binding = Assert.Single(bindings);

      Assert.Contains("Ticker.Tick", binding.Method);

      var pause = fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout);

      Assert.NotNull(pause);
      Assert.Equal("breakpoint", pause.Reason);
      Assert.Contains(binding.Id, pause.BreakpointIds);

      var details = fx.Debug.Describe(pause, 0, false);

      Assert.Contains("Ticker.Tick", details.Frames[0].Method);

      var parameter = Assert.Single(details.Parameters);

      Assert.Equal("n", parameter.Name);
      Assert.True(int.TryParse(parameter.Value, out _));
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableFact]
  public void FrameEvaluationReadsAndWritesTheParameter() {
    try {
      _ = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.Ticker",
          MethodName = "Tick"
        }
      );

      Assert.NotNull(fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout));

      // Reads compose with the eval grammar (operators run client-side on the frame value).
      var n = int.Parse(fx.DebugEval("n").Formatted);

      Assert.Equal((n + 1).ToString(), fx.DebugEval("n + 1").Formatted);

      // Writes go through StackFrame.SetValue; a FRESH evaluation re-reading the live slot (not an
      // interpreter-local copy) proves the frame took the value.
      Assert.Equal("424242", fx.DebugEval("n = 424242; n").Formatted);
      Assert.Equal("424242", fx.DebugEval("n").Formatted);

      // Frame writes follow C# implicit-conversion rules: no silent double->int rounding; the
      // explicit cast converts client-side before the scope sees the value.
      var ex = Assert.Throws<EvalFailedException>(() => fx.DebugEval("n = 2.9"));

      Assert.Contains("no implicit conversion", ex.Message);
      Assert.Equal("2", fx.DebugEval("n = (int) 2.9; n").Formatted);

      // A mid-expression INVOKE regenerates the thread's frames agent-side; the frame slot read
      // after it must still resolve (the scope re-resolves the frame per access).
      Assert.Equal("2", fx.DebugEval("n.ToString(); n").Formatted);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableFact]
  public void LineBreakpointSeesTheLocal() {
    try {
      var tick = Assert.Single(fx.Debug.Locations("TestFixture.Ticker", "Tick", null));

      Assert.True(tick.HasDebugInfo, "the fixture should carry a portable PDB mono can read");
      Assert.NotNull(tick.SequencePoints);
      Assert.True(tick.StartLine < tick.EndLine);

      // The method's last sequence point (the closing brace) sits after every statement, so the
      // local is assigned and still in scope there.
      _ = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.Ticker",
          MethodName = "Tick",
          Line = tick.EndLine.Value
        }
      );

      Assert.NotNull(fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout));

      Assert.StartsWith("\"tick:", fx.DebugEval("label").Formatted);

      var pause = fx.Debug.CurrentPause!;
      var details = fx.Debug.Describe(pause, 0, false);

      Assert.Contains(details.Locals, l => l.Name is "label" && l.Value.StartsWith("\"tick:"));
      Assert.Null(details.LocalsNote);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableFact]
  public void ThisResolvesAndItsMembersAssign() {
    try {
      _ = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.TickBox",
          MethodName = "Bump"
        }
      );

      Assert.NotNull(fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout));

      // `this` parses as a name root, and the frame scope binds it to the live receiver.
      Assert.StartsWith("TestFixture.TickBox#", fx.DebugEval("this").Formatted);

      // Member writes through `this` go through the regular lvalue machinery.
      Assert.Equal("424242", fx.DebugEval("this.Value = 424242; this.Value").Formatted);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableFact]
  public void ConditionAutoResumesFalseHits() {
    try {
      _ = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.Ticker",
          MethodName = "Tick",
          Condition = "n % 10 == 7"
        }
      );

      var pause = fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout);

      Assert.NotNull(pause);
      Assert.Null(pause.ConditionError);

      // Nine of ten hits were auto-resumed; the one that paused satisfies the condition.
      Assert.Equal("7", fx.DebugEval("n % 10").Formatted);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableFact]
  public void UnevaluableConditionPausesWithTheErrorRecorded() {
    try {
      _ = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.Ticker",
          MethodName = "Tick",
          Condition = "noSuchLocal == 1"
        }
      );

      var pause = fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout);

      Assert.NotNull(pause);
      Assert.Contains("noSuchLocal", pause.ConditionError);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableFact]
  public void StepOverRePausesAsAStep() {
    try {
      _ = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.Ticker",
          MethodName = "Tick"
        }
      );

      Assert.NotNull(fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout));

      // Removed first, so the re-pause can only be the step event, not the next Tick hit.
      _ = fx.Debug.RemoveAll();

      var pause = fx.Debug.Step(DebugStepKind.Over, DebugToolsetTests.HitTimeout);

      Assert.NotNull(pause);
      Assert.Equal("step", pause.Reason);

      var details = fx.Debug.Describe(pause, 0, false);

      Assert.Contains("Ticker.Tick", details.Frames[0].Method);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableFact]
  public void HitCountBreaksFromTheNthHitOnward() {
    try {
      _ = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.Ticker",
          MethodName = "Tick",
          HitCount = 3
        }
      );

      // The agent's raw count modifier is a ONE-SHOT (verified empirically); the controller re-arms
      // the request count-free after the Nth hit, so the documented "Nth hit onward" contract
      // holds: a second pause must arrive after the first resume.
      Assert.NotNull(fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout));

      fx.Debug.ResumeFromPause();

      Assert.NotNull(fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout));
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableFact]
  public void ExceptionBreakPausesOnCaughtThrow() {
    try {
      var binding = fx.Debug.AddExceptionBreak("System.FormatException", true);

      Assert.Contains("FormatException", binding.Method);

      var pause = fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout);

      Assert.NotNull(pause);
      Assert.Equal("exception", pause.Reason);

      var details = fx.Debug.Describe(pause, 0, false);

      Assert.Equal("System.FormatException", details.ExceptionType);
      Assert.StartsWith("tick ", details.ExceptionMessage);
      Assert.Contains("MaybeThrow", details.Frames[0].Method);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableFact]
  public void StatusListsAndRemoveClearsTheSharedIdSpace() {
    try {
      var breakpoint = Assert.Single(
        fx.Debug.AddBreakpoints(
          new BreakpointSpec {
            TypeName = "TestFixture.Ticker",
            MethodName = "MaybeThrow",
            Condition = "false",
            HitCount = 5
          }
        )
      );

      var exceptionBreak = fx.Debug.AddExceptionBreak(null, true);

      Assert.NotEqual(breakpoint.Id, exceptionBreak.Id);

      var status = fx.Debug.Status();

      Assert.True(status.PumpRunning);
      Assert.Equal(2, status.Breakpoints.Count);
      Assert.Contains(status.Breakpoints, b => b.Kind is "breakpoint" && b.HitCount is 5);
      Assert.Contains(status.Breakpoints, b => b.Kind is "exception" && b.Id == exceptionBreak.Id);

      Assert.True(fx.Debug.Remove(exceptionBreak.Id));
      Assert.False(fx.Debug.Remove(exceptionBreak.Id));
      Assert.Equal(1, fx.Debug.RemoveAll());
      Assert.Empty(fx.Debug.Status().Breakpoints);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableFact]
  public void OverloadsBindOneBreakpointEachAndSignatureNarrows() {
    try {
      var all = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.Overloads",
          MethodName = "Pick"
        }
      );

      Assert.Equal(4, all.Count);
      Assert.Equal(4, all.Select(b => b.Id).Distinct().Count());

      _ = fx.Debug.RemoveAll();

      var narrowed = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.Overloads",
          MethodName = "Pick",
          SignatureContains = "(Double"
        }
      );

      var single = Assert.Single(narrowed);

      Assert.Contains("Double", single.Method);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }
}
