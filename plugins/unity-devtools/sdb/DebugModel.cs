using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>Step granularity exposed by the debug toolset (resume is not a step).</summary>
public enum DebugStepKind {
  Over,

  Into,

  Out
}

/// <summary>
/// A method-centric breakpoint request: type and method name, optionally narrowed to one overload
/// by signature substring, anchored to a source line or an IL offset (neither = method entry).
/// </summary>
public sealed class BreakpointSpec {
  public string TypeName { get; init; }

  public string MethodName { get; init; }

  /// <summary>Source line to anchor to, resolved through the method's sequence points.</summary>
  public int? Line { get; init; }

  /// <summary>Explicit IL offset escape hatch (wins over <see cref="Line"/>).</summary>
  public int? IlOffset { get; init; }

  /// <summary>Case-insensitive substring narrowing overloads by their full signature.</summary>
  public string SignatureContains { get; init; }

  /// <summary>
  /// C# expression evaluated client-side in the hit frame's scope; false auto-resumes.
  /// </summary>
  public string Condition { get; init; }

  /// <summary>Break only on the Nth hit onward (agent-side, free); 0 = every hit.</summary>
  public int HitCount { get; init; }
}

/// <summary>An armed breakpoint, as reported back to the caller that set it.</summary>
public sealed class BreakpointBinding {
  public int Id { get; init; }

  public string Method { get; init; }

  public string Location { get; init; }

  /// <summary>A caveat worth surfacing (e.g., no debug info), null when clean.</summary>
  public string Warning { get; init; }
}

/// <summary>One registered debug request (breakpoint or exception break), for status.</summary>
public sealed class BreakpointDescription {
  public int Id { get; init; }

  /// <summary>"breakpoint" or "exception".</summary>
  public string Kind { get; init; }

  /// <summary>The method signature, or the exception type filter.</summary>
  public string Target { get; init; }

  public string Location { get; init; }

  public string Condition { get; init; }

  public int HitCount { get; init; }
}

/// <summary>
/// A live pause recorded by the event pump; carries mirrors, so it is only meaningful against the
/// attach that produced it.
/// </summary>
public sealed class PauseSnapshot {
  public long Seq { get; init; }

  public ThreadMirror Thread { get; init; }

  public string ThreadName { get; init; }

  /// <summary>"breakpoint", "step", "exception", or "user-break".</summary>
  public string Reason { get; init; }

  public IReadOnlyList<int> BreakpointIds { get; init; }

  /// <summary>The thrown exception object, when <see cref="Reason"/> is "exception".</summary>
  public ObjectMirror Exception { get; init; }

  /// <summary>
  /// Why a breakpoint condition could not be evaluated; the pump pauses instead of guessing, so the
  /// agent can repair the condition.
  /// </summary>
  public string ConditionError { get; init; }
}

/// <summary>One call-stack frame, formatted for reporting.</summary>
public sealed class FrameLine {
  public int Index { get; init; }

  public string Method { get; init; }

  public string SourceFile { get; init; }

  public int Line { get; init; }

  public int IlOffset { get; init; }
}

/// <summary>One named value (local, parameter), formatted like eval output.</summary>
public sealed class VariableView {
  public string Name { get; init; }

  public string Value { get; init; }
}

/// <summary>One thread's identity and names-only stack, for the all-threads view.</summary>
public sealed class ThreadView {
  public string Name { get; init; }

  public long Id { get; init; }

  public string State { get; init; }

  public IReadOnlyList<string> FrameMethods { get; init; }
}

/// <summary>
/// A fully formatted pause report: the paused thread's capped call stack, plus locals, parameters,
/// and <c>this</c> for one selected frame.
/// </summary>
public sealed class PauseDetails {
  /// <summary>"breakpoint", "step", "exception", "user-break", or "suspend-hold".</summary>
  public string Reason { get; init; }

  /// <summary>The pause sequence number; 0 for a suspend-hold view (no pump event).</summary>
  public long Seq { get; init; }

  public IReadOnlyList<int> BreakpointIds { get; init; }

  public string ExceptionType { get; init; }

  public string ExceptionMessage { get; init; }

  public string ConditionError { get; init; }

  public string ThreadName { get; init; }

  public long ThreadId { get; init; }

  public IReadOnlyList<FrameLine> Frames { get; init; }

  public bool FramesTruncated { get; init; }

  /// <summary>The frame whose variables are listed below.</summary>
  public int FrameIndex { get; init; }

  public string This { get; init; }

  public IReadOnlyList<VariableView> Parameters { get; init; }

  public IReadOnlyList<VariableView> Locals { get; init; }

  /// <summary>Why locals are missing (typically no debug info), null when they listed.</summary>
  public string LocalsNote { get; init; }

  /// <summary>Every thread's identity and names-only stack; null unless requested.</summary>
  public IReadOnlyList<ThreadView> Threads { get; init; }
}

/// <summary>One sequence point of a method's line table.</summary>
public sealed class SequencePointInfo {
  public int IlOffset { get; init; }

  public int Line { get; init; }

  public int EndLine { get; init; }
}

/// <summary>
/// A method's source mapping (SDB carries no source text; this is the substitute).
/// </summary>
public sealed class MethodLocationInfo {
  public string Signature { get; init; }

  public string SourceFile { get; init; }

  public bool HasDebugInfo { get; init; }

  public int? StartLine { get; init; }

  public int? EndLine { get; init; }

  /// <summary>The full line table; null in the methods-overview mode (no method given).</summary>
  public IReadOnlyList<SequencePointInfo> SequencePoints { get; init; }
}

/// <summary>The debug surface's non-blocking status: registry plus pause/pump summary.</summary>
public sealed class DebugStatusInfo {
  public bool PumpRunning { get; init; }

  public bool Paused { get; init; }

  public string PauseReason { get; init; }

  public string PauseThreadName { get; init; }

  public IReadOnlyList<int> PauseBreakpointIds { get; init; }

  public long PauseSeq { get; init; }

  /// <summary>
  /// Pause-worthy hits the pump dropped because a pause was already active (one pause at a time);
  /// non-zero means a concurrent hit was lost, not that a breakpoint never fired.
  /// </summary>
  public int DroppedPauses { get; init; }

  public IReadOnlyList<BreakpointDescription> Breakpoints { get; init; }
}
