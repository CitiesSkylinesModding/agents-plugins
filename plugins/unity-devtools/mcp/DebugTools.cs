using System.ComponentModel;
using System.Globalization;
using JetBrains.Annotations;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using UnityDevtools.Sdb;
using UnityDevtools.Sdb.Eval;

namespace UnityDevtools.Mcp;

/// <summary>
/// The breakpoint/pause debugging toolset over the shared <see cref="UnitySession"/>'s
/// <see cref="DebugController"/>: arm breakpoints and exception breaks, wait for hits, inspect and
/// evaluate in frames, step, and advance a held suspend window.
/// Blocking tools (wait, step, advance) deliberately bypass <see cref="UnitySession.Run{T}"/>: its
/// suspend window would keep the VM frozen and the awaited run could never happen.
/// Result payloads reuse the sdb model types verbatim (a single source of truth; they are plain
/// serializable records of formatted values).
/// </summary>
[McpServerToolType]
[UsedImplicitly]
public sealed class DebugTools(UnitySession session, EvalState state) {
  private const double StepTimeoutSeconds = 10;

  [McpServerTool(Name = "debug_set_breakpoint")]
  [Description(
    """
    Set a breakpoint by type + method name (method-centric: SDB has no file:line addressing).
    Burst caveat: Burst-compiled jobs are native code, invisible to the Mono debugger; no breakpoint
    can hit them (debuggable code = non-Burst mods, or the game run with Burst disabled).
    Managed code hits fine on ANY thread, including job workers.
    Every matching overload gets its own breakpoint id (all returned with signatures); narrow with
    the signature substring.
    Without line/ilOffset the breakpoint sits at method entry, the only mode that works without
    debug info; a line resolves through the method's sequence points (see debug_locations for the
    valid lines).
    condition is a C# expression (eval grammar) evaluated in the hit frame's scope on each hit;
    false auto-resumes the game, so hot-path breakpoints stay cheap.
    hitCount N skips until the Nth hit (agent-side, free).
    The game keeps running; catch hits with debug_wait.
    Breakpoints die with the connection (game restart), listed by debug_status.
    """
  )]
  [UsedImplicitly]
  public SetBreakpointResult SetBreakpoint(
    [Description("Fully-qualified type name declaring the method.")] string type,
    [Description("Method name; every overload matches unless narrowed.")] string method,
    [Description("Source line to anchor to (needs debug info); omit for method entry.")] int? line =
      null,
    [Description("Explicit IL offset escape hatch; wins over line.")] int? ilOffset = null,
    [Description("Case-insensitive substring to narrow overloads by full signature.")]
    string? signature = null,
    [Description(
      "C# condition evaluated in the hit frame (locals, parameters, this, typed roots); " +
      "false auto-resumes."
    )]
    string? condition = null,
    [Description("Break from the Nth hit onward; 0 = every hit.")] int hitCount = 0
  ) {
    return ToolGuard.Run(() => {
        try {
          return session.Run(ctx => new SetBreakpointResult {
              Breakpoints = ctx.Debug.AddBreakpoints(
                new BreakpointSpec {
                  TypeName = type,
                  MethodName = method,
                  Line = line,
                  IlOffset = ilOffset,
                  SignatureContains = signature,
                  Condition = condition,
                  HitCount = hitCount
                }
              )
            }
          );
        }
        catch (EvalParseException ex) {
          throw new McpException(
            $"condition parse error at offset {ex.Position.ToString(CultureInfo.InvariantCulture)}: " +
            ex.Message
          );
        }
      }
    );
  }

  [McpServerTool(Name = "debug_break_on_exception")]
  [Description(
    """
    Break when a matching exception is THROWN (caught or not; a Unity player catches nearly
    everything in its own loop, so an "uncaught only" mode would never fire).
    Omit exceptionType to break on every exception; expect noise, Unity code throws routinely.
    Shares the breakpoint ID space: listed by debug_status, removed by debug_remove_breakpoint.
    Catch hits with debug_wait; the pause reports the exception type, message, and throw site.
    """
  )]
  [UsedImplicitly]
  public SetBreakpointResult BreakOnException(
    [Description("Fully-qualified exception type; omit for all exceptions.")]
    string? exceptionType = null,
    [Description("Also match subclasses of exceptionType.")] bool includeSubclasses = true
  ) {
    return ToolGuard.Run(() => session.Run(ctx => new SetBreakpointResult {
          Breakpoints = [ctx.Debug.AddExceptionBreak(exceptionType, includeSubclasses)]
        }
      )
    );
  }

  [McpServerTool(Name = "debug_remove_breakpoint")]
  [Description(
    """
    Remove a breakpoint or exception break by ID, or all of them with "all".
    "all" also cancels a step left armed by a timed-out debug_step (the only way to disarm one
    while the game runs).
    Removing does not resume a pause already holding the game; debug_step action=resume does.
    """
  )]
  [UsedImplicitly]
  public RemoveBreakpointResult RemoveBreakpoint(
    [Description("A breakpoint id from debug_set_breakpoint/debug_status, or \"all\".")] string id
  ) {
    return ToolGuard.Run(() => {
        var debug = session.DebugOrNull;

        if (debug is null) {
          return new RemoveBreakpointResult {
            Removed = 0
          };
        }

        if (string.Equals(id, "all", StringComparison.OrdinalIgnoreCase)) {
          return new RemoveBreakpointResult {
            Removed = debug.RemoveAll()
          };
        }

        if (!int.TryParse(id, out var parsed)) {
          throw new McpException($"id must be a breakpoint id or \"all\", got '{id}'");
        }

        return new RemoveBreakpointResult {
          Removed = debug.Remove(parsed) ? 1 : 0
        };
      }
    );
  }

  [McpServerTool(Name = "debug_status")]
  [Description(
    """
    Non-blocking debug overview: armed breakpoints/exception breaks, the current pause (if any),
    held suspensions, and whether the event pump runs.
    Never attaches and never waits.
    """
  )]
  [UsedImplicitly]
  public DebugStatusResult Status() {
    return ToolGuard.Run(() => {
        var snapshot = session.Snapshot();
        var debug = session.DebugOrNull?.Status();

        return new DebugStatusResult {
          Attached = snapshot.Attached,
          HeldSuspends = snapshot.HeldSuspends,
          PumpRunning = debug?.PumpRunning ?? false,
          Paused = debug?.Paused ?? false,
          PauseReason = debug?.PauseReason,
          PauseThreadName = debug?.PauseThreadName,
          PauseBreakpointIds = debug?.PauseBreakpointIds,
          DroppedPauses = debug?.DroppedPauses ?? 0,
          Breakpoints = debug?.Breakpoints ?? []
        };
      }
    );
  }

  [McpServerTool(Name = "debug_pause_state")]
  [Description(
    """
    Inspect the current pause: the paused thread's call stack (capped) plus fully formatted locals,
    parameters, and `this` for one frame (top by default; frameIndex selects).
    Works for any pause: a breakpoint/step/exception hit uses the event thread, a plain held suspend
    (suspend tool) uses the main thread.
    allThreads appends every thread's name/state and a names-only stack (the ECS job-worker view);
    Burst-compiled job code shows no managed frames.
    """
  )]
  [UsedImplicitly]
  public PauseDetails PauseState(
    [Description("Which frame's variables to list; 0 = top.")] int frameIndex = 0,
    [Description("Append every thread's name/state and names-only stack.")] bool allThreads = false
  ) {
    return ToolGuard.Run(() => session.Run(ctx => {
          if (ctx.Debug.CurrentPause is {} pause) {
            return ctx.Debug.Describe(pause, frameIndex, allThreads);
          }

          if (session.HeldSuspendCount > 0) {
            return ctx.Debug.DescribeSuspended(ctx.Invoker.MainThread, frameIndex, allThreads);
          }

          throw new McpException(
            "not paused: no breakpoint/step/exception pause and no held suspend (set a " +
            "breakpoint and debug_wait, or open a window with suspend)"
          );
        }
      )
    );
  }

  [McpServerTool(Name = "debug_evaluate")]
  [Description(
    """
    Evaluate a C# statement sequence in a paused frame: the eval grammar plus the frame's locals,
    parameters, and `this` as roots, readable AND assignable.
    Requires an active pause (breakpoint/step/exception hit, or a held suspend, which evaluates
    on the main thread); errors otherwise: use eval for frameless evaluation.
    frameIndex picks the frame (0 = top).
    Frame slots are direct reads/writes; method and property calls run on the game's main thread
    like eval (ECS thread-safety), even when the paused frame belongs to a worker thread.
    Shares the `_` last-result slot with eval.
    """
  )]
  [UsedImplicitly]
  public DebugEvaluateResult Evaluate(
    [Description("C# statement sequence; the final expression's value is the result.")] string code,
    [Description("Frame whose scope to evaluate in; 0 = top.")] int frameIndex = 0,
    [Description("ECS world name for the em/world builtins; omit for the default world.")]
    string? world = null
  ) {
    return ToolGuard.Run(() => {
        var program = DebugTools.Parse(code);

        return session.Run(ctx => {
            var thread = ctx.Debug.CurrentPause?.Thread ??
              (session.HeldSuspendCount > 0
                ? ctx.Invoker.MainThread
                : throw new McpException(
                  "not paused; debug_evaluate needs a pause (breakpoint hit or held suspend): " +
                  "use eval for frameless evaluation"
                ));

            var ecs = new Lazy<Ecs>(() => ctx.Ecs(world));

            try {
              var outcome = ctx.Debug.EvaluateInFrame(
                program,
                thread,
                frameIndex,
                state,
                [new BuiltinScope(ctx.Invoker, () => ecs.Value, state)]
              );

              return new DebugEvaluateResult {
                FrameIndex = frameIndex,
                Result = outcome.Formatted,
                Type = outcome.TypeName
              };
            }
            catch (EvalFailedException ex) {
              throw new McpException(EvalTools.FailureReport(ex));
            }
          }
        );
      }
    );
  }

  [McpServerTool(Name = "debug_step")]
  [Description(
    """
    Resume or step the paused thread. action: resume (release the pause, return immediately),
    over/into/out (step and block until the re-pause, near-instant in normal code).
    Steps are line-sized when debug info exists, single-IL otherwise; DebuggerHidden code and static
    constructors are stepped through.
    If the step does not re-pause in time (the stepped code blocks), the game keeps running with
    the step armed and debug_wait catches its completion.
    There is no pause action: the suspend tool freezes the game.
    """
  )]
  [UsedImplicitly]
  public DebugStepResult Step([Description("resume | over | into | out.")] string action) {
    return ToolGuard.Run(() => {
        var debug = session.DebugOrNull ??
          throw new McpException("no active debug session (nothing armed, nothing paused)");

        if (string.Equals(action, "resume", StringComparison.OrdinalIgnoreCase)) {
          debug.ResumeFromPause();

          return new DebugStepResult {
            Action = "resume",
            Completed = true,
            Message = "resumed; the game runs (catch the next hit with debug_wait)",
            Pause = null
          };
        }

        var kind = action.ToLowerInvariant() switch {
          "over" => DebugStepKind.Over,
          "into" => DebugStepKind.Into,
          "out" => DebugStepKind.Out,
          _ => throw new McpException($"unknown action '{action}' (resume | over | into | out)")
        };

        if (session.HeldSuspendCount > 0) {
          throw new McpException(
            "a held suspension would prevent the step from ever completing; release it with " +
            "resume first"
          );
        }

        var snapshot = debug.Step(kind, TimeSpan.FromSeconds(DebugTools.StepTimeoutSeconds));

        return snapshot is null
          ? new DebugStepResult {
            Action = action,
            Completed = false,
            Message =
              "the step did not re-pause in time (the stepped code is likely blocked); the game " +
              "is running with the step armed, debug_wait catches its completion",
            Pause = null
          }
          : new DebugStepResult {
            Action = action,
            Completed = true,
            Message = null,
            Pause = session.Run(ctx => ctx.Debug.Describe(snapshot, 0, false))
          };
      }
    );
  }

  [McpServerTool(Name = "debug_wait")]
  [Description(
    """
    Block until the next pause (breakpoint/exception/step hit) and return the full pause state,
    or report "still running" on timeout.
    Returns immediately when already paused, so a hit that landed before the call is never missed.
    This is how to catch a hit triggered by in-game action: arm, trigger, debug_wait.
    """
  )]
  [UsedImplicitly]
  public DebugWaitResult Wait(
    [Description("How long to wait; clamped to 1-300.")] double timeoutSeconds = 30
  ) {
    return ToolGuard.Run(() => {
        var debug = session.DebugOrNull ??
          throw new McpException(
            "no active debug session (arm a breakpoint or exception break first)"
          );

        var status = debug.Status();

        if (status is { Paused: false, PumpRunning: false }) {
          throw new McpException(
            "nothing is armed and nothing is paused; arm a breakpoint or exception break, then " +
            "wait"
          );
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 300));
        var snapshot = debug.WaitForPause(timeout);

        return snapshot is null
          ? new DebugWaitResult {
            Paused = false,
            Message = $"still running (no pause within {timeout.TotalSeconds:0}s)",
            Pause = null
          }
          : new DebugWaitResult {
            Paused = true,
            Message = null,
            Pause = session.Run(ctx => ctx.Debug.Describe(snapshot, 0, false))
          };
      }
    );
  }

  [McpServerTool(Name = "debug_locations")]
  [Description(
    """
    The substitute for source text (SDB carries none): with a method, the full sequence-point table
    (source line <-> IL offset) per matching overload; without, the type's method map with source
    file and line range per method.
    Methods without debug info report so ("breakpoints resolve to method entry"), not an error.
    """
  )]
  [UsedImplicitly]
  public DebugLocationsResult Locations(
    [Description("Fully-qualified type name.")] string type,
    [Description("Method name for the detailed line table; omit for the type's method map.")]
    string? method = null,
    [Description("Case-insensitive substring to narrow methods by full signature.")]
    string? signature = null
  ) {
    return ToolGuard.Run(() => session.Run(ctx => {
          var methods = ctx.Debug.Locations(type, method, signature);

          return new DebugLocationsResult {
            Methods = methods,
            Note = methods.All(m => !m.HasDebugInfo)
              ? "no debug info for these methods; breakpoints resolve to method entry"
              : null
          };
        }
      )
    );
  }

  [McpServerTool(Name = "advance")]
  [Description(
    """
    Release the held debugger suspension (suspend tool) for N seconds of real time, then re-take
    it: the "let the simulation react, then verify" primitive.
    Requires a held suspension.
    A game's OWN pause (e.g. a simulation-speed setting) is game logic no SDB operation can lift:
    pass before/after eval snippets to flip it (the per-game recipe belongs to the caller, e.g.
    before unpauses the simulation, after re-pauses it). Snippets use the eval grammar and the
    `_` slot.
    Other tools block for the whole window by design.
    If a breakpoint hits during the window, the pause holds after advance returns
    (pausedDuringAdvance=true); inspect it before resuming.
    """
  )]
  [UsedImplicitly]
  public AdvanceResult Advance(
    [Description("Real-time seconds to let the game run; clamped to 0.1-60.")] double seconds,
    [Description("Eval snippet run before releasing the hold (e.g. unpause the simulation).")]
    string? before = null,
    [Description("Eval snippet run after re-taking the hold (e.g. re-pause the simulation).")]
    string? after = null,
    [Description("ECS world name for the snippets' em/world builtins.")] string? world = null
  ) {
    return ToolGuard.Run(() => {
        if (session.HeldSuspendCount is 0) {
          throw new McpException(
            "advance releases a held suspension and none is held; open the window with the " +
            "suspend tool first"
          );
        }

        var duration = TimeSpan.FromSeconds(Math.Clamp(seconds, 0.1, 60));

        var beforeResult = before is null ? null : this.RunSnippet(before, world);

        bool pausedDuring;

        try {
          pausedDuring = session.AdvanceHold(duration);
        }
        catch (Exception ex) when (before is not null || after is not null) {
          // The before snippet may have flipped game state (e.g., unpaused the simulation); a
          // failed window must not strand that flip, so the compensating after snippet still
          // runs whenever the connection survived.
          if (after is null) {
            throw new McpException(
              $"{ex.Message}; note: the before snippet already ran and its change is still applied"
            );
          }

          if (!session.Snapshot().Attached) {
            throw new McpException(
              $"{ex.Message}; the connection is gone, so the after snippet could NOT run: " +
              "the before snippet's change is still applied"
            );
          }

          string compensation;

          try {
            _ = this.RunSnippet(after, world);

            compensation = "the after snippet was still run to compensate";
          }
          catch (Exception afterEx) {
            compensation = $"the compensating after snippet ALSO failed ({afterEx.Message}); " +
              "the before snippet's change is still applied";
          }

          throw new McpException($"{ex.Message}; {compensation}");
        }

        var afterResult = after is null ? null : this.RunSnippet(after, world);

        return new AdvanceResult {
          SecondsAdvanced = duration.TotalSeconds,
          Before = beforeResult,
          After = afterResult,
          PausedDuringAdvance = pausedDuring
        };
      }
    );
  }

  /// <summary>One before/after advance snippet, through the regular frameless eval path.</summary>
  private string RunSnippet(string code, string? world) {
    var program = DebugTools.Parse(code);

    return session.Run(ctx => {
        var ecs = new Lazy<Ecs>(() => ctx.Ecs(world));

        var interpreter = new EvalInterpreter(
          ctx.Invoker,
          [new BuiltinScope(ctx.Invoker, () => ecs.Value, state)]
        );

        try {
          return interpreter.Run(program, state).Formatted;
        }
        catch (EvalFailedException ex) {
          throw new McpException(EvalTools.FailureReport(ex));
        }
      }
    );
  }

  private static EvalProgram Parse(string code) {
    try {
      return EvalParser.Parse(code);
    }
    catch (EvalParseException ex) {
      throw new McpException(
        $"parse error at offset {ex.Position.ToString(CultureInfo.InvariantCulture)}: {ex.Message}"
      );
    }
  }
}

/// <summary>Result of <c>debug_set_breakpoint</c>/<c>debug_break_on_exception</c>.</summary>
public sealed record SetBreakpointResult {
  public required IReadOnlyList<BreakpointBinding> Breakpoints { [UsedImplicitly] get; init; }
}

/// <summary>Result of <c>debug_remove_breakpoint</c>.</summary>
public sealed record RemoveBreakpointResult {
  public required int Removed { [UsedImplicitly] get; init; }
}

/// <summary>Result of <c>debug_status</c>: registry + pause/pump/hold summary.</summary>
public sealed record DebugStatusResult {
  public required bool Attached { [UsedImplicitly] get; init; }

  /// <summary>Suspensions held via the suspend tool (frame tools work under a hold too).</summary>
  public required int HeldSuspends { [UsedImplicitly] get; init; }

  public required bool PumpRunning { [UsedImplicitly] get; init; }

  /// <summary>An event-caused pause (breakpoint/step/exception/user-break) is active.</summary>
  public required bool Paused { [UsedImplicitly] get; init; }

  public required string? PauseReason { [UsedImplicitly] get; init; }

  public required string? PauseThreadName { [UsedImplicitly] get; init; }

  public required IReadOnlyList<int>? PauseBreakpointIds { [UsedImplicitly] get; init; }

  /// <summary>
  /// Pause-worthy hits dropped because a pause was already active; non-zero means a concurrent
  /// hit was lost, not that a breakpoint never fired.
  /// </summary>
  public required int DroppedPauses { [UsedImplicitly] get; init; }

  public required IReadOnlyList<BreakpointDescription> Breakpoints { [UsedImplicitly] get; init; }
}

/// <summary>Result of <c>debug_evaluate</c>.</summary>
public sealed record DebugEvaluateResult {
  public required int FrameIndex { [UsedImplicitly] get; init; }

  public required string Result { [UsedImplicitly] get; init; }

  /// <summary>The result value's type full name.</summary>
  public required string Type { [UsedImplicitly] get; init; }
}

/// <summary>Result of <c>debug_step</c>.</summary>
public sealed record DebugStepResult {
  public required string Action { [UsedImplicitly] get; init; }

  /// <summary>False when the step armed but no re-pause happened in time.</summary>
  public required bool Completed { [UsedImplicitly] get; init; }

  public required string? Message { [UsedImplicitly] get; init; }

  public required PauseDetails? Pause { [UsedImplicitly] get; init; }
}

/// <summary>Result of <c>debug_wait</c>.</summary>
public sealed record DebugWaitResult {
  public required bool Paused { [UsedImplicitly] get; init; }

  public required string? Message { [UsedImplicitly] get; init; }

  public required PauseDetails? Pause { [UsedImplicitly] get; init; }
}

/// <summary>Result of <c>debug_locations</c>.</summary>
public sealed record DebugLocationsResult {
  public required IReadOnlyList<MethodLocationInfo> Methods { [UsedImplicitly] get; init; }

  public required string? Note { [UsedImplicitly] get; init; }
}

/// <summary>Result of <c>advance</c>.</summary>
public sealed record AdvanceResult {
  public required double SecondsAdvanced { [UsedImplicitly] get; init; }

  /// <summary>The before snippet's result, when one ran.</summary>
  public required string? Before { [UsedImplicitly] get; init; }

  /// <summary>The after snippet's result, when one ran.</summary>
  public required string? After { [UsedImplicitly] get; init; }

  /// <summary>A breakpoint/exception pause landed during the window and still holds.</summary>
  public required bool PausedDuringAdvance { [UsedImplicitly] get; init; }
}
