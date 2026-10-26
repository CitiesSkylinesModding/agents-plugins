using System;
using System.Linq;
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

      var details = fx.Debug.Describe(pause, 0, true);

      Assert.Contains("Ticker.Tick", details.Frames[0].Method);

      // The pause names threads by their own name, "(unnamed)" for the fixture's idle worker, and
      // "(main)" only for an unnamed main thread, which this fixture never has.
      Assert.Equal("FixtureMain", details.ThreadName);
      Assert.Contains(details.Threads, t => t.Name == "(unnamed)");
      Assert.DoesNotContain(details.Threads, t => t.Name == "(main)");

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
      this.BreakIn("TestFixture.Ticker", "Tick");

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
      this.BreakIn("TestFixture.TickBox", "Bump");

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
  public void ANullWrittenIntoAPointerParameterIsAZeroPointer() {
    try {
      this.BreakIn("TestFixture.Pointers", "At");

      Assert.Equal(
        "PointerValue<(System.Int32*) 0x0>",
        fx.DebugEval("pointer = null; pointer").Formatted
      );
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  // A struct parameter decodes as a copy, so a member write stores the whole struct back.
  [SkippableTheory]
  [InlineData("pt.X = 42; pt.X", "42")]
  [InlineData("holds.Maybe = 5; holds.Maybe ?? 0", "5")]
  public void AMemberWriteOnAFrameStructPersists(string code, string expected) {
    try {
      this.BreakInFrames();

      Assert.Equal(expected, fx.DebugEval(code).Formatted);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  // A frame write takes what C# assigns implicitly: a debuggee int widens into a long, and a
  // client value boxes into an object.
  [SkippableTheory]
  [InlineData("wide = n; wide", "4")]
  [InlineData("boxed = 5; boxed", "5")]
  [InlineData("maybe = n; maybe ?? 0", "4")]
  [InlineData("maybe = n; maybe.HasValue", "True")]
  [InlineData("maybe = null; maybe.HasValue", "False")]
  [InlineData("ratio = 'a'; ratio", "97")]
  public void AFrameWriteWidensAndBoxes(string code, string expected) {
    try {
      this.BreakInFrames();

      Assert.Equal(expected, fx.DebugEval(code).Formatted);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  // An interface slot takes a string, as C# assigns it, and stores a struct a call or member
  // write through it changed back as a new box.
  [SkippableTheory]
  [InlineData("cmp = \"a\"; cmp", "\"a\"")]
  [InlineData("bump.Bump(); bump.P", "1")]
  [InlineData("bump.P = 7; bump.P", "7")]
  public void AnInterfaceSlotTakesAStringAndStoresAChangedBox(string code, string expected) {
    try {
      this.BreakInFrames();

      Assert.Equal(expected, fx.DebugEval(code).Formatted);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  // The agent serves a register-held local it wrote stale once an invoke ran, so a write reads
  // back from what this pause wrote, within one evaluation and across several.
  [SkippableTheory]
  [InlineData("wide = 77; n.GetHashCode(); wide", "77")]
  [InlineData("wide = 77; n.GetHashCode(); wide = wide + 1; wide", "78")]
  public void AFrameLocalWriteReadsBackAfterACall(string code, string expected) {
    try {
      this.BreakInFrames();

      Assert.Equal(expected, fx.DebugEval(code).Formatted);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableFact]
  public void AFrameLocalWriteReadsBackAfterACallInAnotherEvaluation() {
    try {
      this.BreakInFrames();

      _ = fx.DebugEval("wide = 77");
      _ = fx.DebugEval("n.GetHashCode()");

      Assert.Equal("77", fx.DebugEval("wide").Formatted);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableFact]
  public void APauseStateListsALocalThisPauseWrote() {
    try {
      this.BreakInFrames();

      _ = fx.DebugEval("wide = 77; n.GetHashCode()");

      var details = fx.Debug.Describe(fx.Debug.CurrentPause!, 0, false);

      Assert.Contains(details.Locals, l => l.Name is "wide" && l.Value is "77");
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableTheory]
  [InlineData("narrow = 300", "300 does not fit TestFixture.Narrow")]
  [InlineData("narrow = -1", "-1 does not fit TestFixture.Narrow")]
  [InlineData("n = wide", "no implicit conversion from Int64 to System.Int32 for 'n'")]
  [InlineData("narrow = 'a'", "cannot write a Char into enum variable 'narrow'")]
  [InlineData("wide = 77; p = wide", "cannot write a value of type Int64 into 'p'")]
  [InlineData(
    "cmp = TestFixture.Frames.InTarget",
    "cannot write a value of type TestFixture.Point into 'cmp' (System.IComparable)"
  )]
  [InlineData("shape.X = 5", "'shape' holds a boxed struct")]
  [InlineData(
    "text = pt",
    "cannot write a value of type TestFixture.Point into 'text' (System.String)"
  )]
  [InlineData("TestFixture.Frames.ShapeAlias.X = 5", "'ShapeAlias' holds a boxed struct")]
  [InlineData("boxed = 5; boxed == 5", "cannot compare a boxed value by reference")]
  public void AFrameWriteRefusesWhatCSharpWouldNotAssign(string code, string expected) {
    try {
      this.BreakInFrames();

      var ex = Assert.Throws<EvalFailedException>(() => fx.DebugEval(code));

      Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  // The documented divergence: C# boxes a number into an interface slot, where a frame write
  // takes only a box a debuggee call returned.
  [SkippableFact]
  public void ANumberIntoAnInterfaceSlotIsRefusedWhereCSharpBoxesIt() {
    try {
      this.BreakInFrames();

      var ex = Assert.Throws<EvalFailedException>(() => fx.DebugEval("cmp = n"));

      Assert.Contains(
        "cannot write a value of type Int32 into 'cmp' (System.IComparable): a frame write",
        ex.Message,
        StringComparison.Ordinal
      );
      Assert.Equal(
        "4",
        fx.DebugEval("cmp = System.Convert.ChangeType(n, typeof(int)); cmp").Formatted
      );
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

  /// <summary>
  /// The condition error carries the game's own exception: this path reports through the failure's
  /// message alone, so it is where a report that hid the throw behind a summary would show.
  /// </summary>
  [SkippableFact]
  public void AConditionThrowingInGameRecordsTheGamesException() {
    try {
      _ = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.Ticker",
          MethodName = "Tick",
          Condition = "TestFixture.Thrower.BoomBool()"
        }
      );

      var pause = fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout);

      Assert.NotNull(pause);

      Assert.Contains(
        "in-game exception: System.InvalidOperationException: kaboom",
        pause.ConditionError,
        StringComparison.Ordinal
      );
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableFact]
  public void StepOverRePausesAsAStep() {
    try {
      this.BreakIn("TestFixture.Ticker", "Tick");

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

  // A call on a struct a slot typed object holds reads a copy of it, and writes nothing back:
  // the slot keeps the box its alias holds.
  [SkippableTheory]
  [InlineData("shape.GetHashCode()")]
  [InlineData("TestFixture.Frames.ShapeAlias.GetHashCode()")]
  public void ACallOnABoxedStructKeepsTheBox(string code) {
    try {
      this.BreakInFrames();

      _ = fx.DebugEval(code);
      _ = fx.Debug.RemoveAll();
      _ = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.Frames",
          MethodName = "After"
        }
      );

      fx.Debug.ResumeFromPause();

      Assert.NotNull(fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout));
      Assert.Equal("True", fx.DebugEval("same").Formatted);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  // A struct method's `this` is the caller's storage, so a member write or a mutating call
  // through it lands there.
  [SkippableTheory]
  [InlineData("this.X = 9; TestFixture.Frames.Held.X", "9")]
  [InlineData("this.X = 1; this.Bump(); TestFixture.Frames.Held.X", "2")]
  [InlineData("this = new TestFixture.Mutable { X = 5 }; TestFixture.Frames.Held.X", "5")]
  public void AWriteThroughAStructThisReachesTheCaller(string code, string expected) {
    try {
      this.BreakIn("TestFixture.Mutable", "Touch");

      Assert.Equal(expected, fx.DebugEval(code).Formatted);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  [SkippableTheory]
  [InlineData("this.X = 9; TestFixture.Frames.GenericHeld.X", "9")]
  [InlineData("this.GetHashCode() == this.GetHashCode()", "true")]
  public void AGenericStructsThisReadsAndWritesLikeAnyOther(string code, string expected) {
    try {
      this.BreakIn("TestFixture.Generic`1", "Touch");

      Assert.Equal(expected, fx.DebugEval(code).Formatted);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  // A `ref` parameter is the caller's storage, as in C#; an `in` one is too, where C# refuses the
  // write (the documented divergence).
  [SkippableTheory]
  [InlineData("r.X = 5; TestFixture.Frames.RefTarget.X", "5")]
  [InlineData("q.X = 6; TestFixture.Frames.InTarget.X", "6")]
  public void AWriteThroughAByRefParameterReachesTheCaller(string code, string expected) {
    try {
      this.BreakIn("TestFixture.Frames", "ByRefs");

      Assert.Equal(expected, fx.DebugEval(code).Formatted);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  // A pause inside a running static constructor reads the statics it already assigned, as C#
  // does, though the agent reports the type not yet initialized.
  [SkippableFact]
  public void AStaticReadsInsideItsRunningStaticConstructor() {
    try {
      _ = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.Starting",
          MethodName = "Ready"
        }
      );
      _ = fx.Eval("TestFixture.Starter.Requested = true");

      Assert.NotNull(fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout));
      Assert.Equal("5", fx.DebugEval("TestFixture.Starting.First").Formatted);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  // Only the thread running a static constructor sees its statics partly assigned; any other
  // thread would wait for it, so an evaluation elsewhere refuses them.
  [SkippableFact]
  public void AStaticConstructorRunningOnAnotherThreadIsRefused() {
    try {
      _ = fx.Debug.AddBreakpoints(
        new BreakpointSpec {
          TypeName = "TestFixture.WorkerStarting",
          MethodName = "Ready"
        }
      );
      _ = fx.Eval("TestFixture.Starter.StartOnWorker()");

      Assert.NotNull(fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout));

      var ex = Assert.Throws<EvalFailedException>(() => fx.Eval("TestFixture.WorkerStarting.First")
      );

      Assert.Contains("static constructor has not run", ex.Message);
      Assert.Equal("5", fx.DebugEval("TestFixture.WorkerStarting.First").Formatted);
    }
    finally {
      fx.ReleaseDebugger();
    }
  }

  private void BreakInFrames() => this.BreakIn("TestFixture.Frames", "At");

  private void BreakIn(string typeName, string methodName) {
    _ = fx.Debug.AddBreakpoints(
      new BreakpointSpec {
        TypeName = typeName,
        MethodName = methodName
      }
    );

    Assert.NotNull(fx.Debug.WaitForPause(DebugToolsetTests.HitTimeout));
  }
}
