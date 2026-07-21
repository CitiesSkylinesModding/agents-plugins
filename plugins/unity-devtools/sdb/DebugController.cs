using Mono.Debugger.Soft;
using UnityDevtools.Sdb.Eval;

namespace UnityDevtools.Sdb;

/// <summary>
/// The breakpoint/pause surface of one attach: a registry of debug requests (breakpoints and
/// exception breaks in one ID space), a background event pump, the resulting pause state, and the
/// operations over it (wait, step, frame reporting, frame-context evaluation).
/// The pump starts with the first debug request and evaluates breakpoint conditions in frame
/// scope, auto-resuming false hits so the game never stays frozen waiting for the agent.
/// "Paused" here means an event-caused suspension; a plain held suspend is reported through
/// <see cref="DescribeSuspended"/> by the session layer (unified pause semantics).
/// Lives and dies with its attach: <see cref="UnitySession"/> discards it on disconnect, and
/// <see cref="Dispose"/> clears every request so a detach never leaves the game re-freezing.
/// </summary>
public sealed class DebugController(VirtualMachine vm, Invoker invoker) : IDisposable {
  private const int MaxFrames = 30;

  /// <summary>Single monitor: registry + pause fields, and the pause signal for waiters.</summary>
  private readonly object gate = new();

  private readonly Dictionary<int, RequestRecord> requests = [];

  private int nextId;

  private Thread pump;

  private bool pumpStopped;

  private bool disposed;

  private PauseSnapshot pause;

  private long pauseSeq;

  private StepEventRequest activeStep;

  /// <summary>
  /// A suspending event set is being classified (conditions may be mid-evaluation): the VM is
  /// already suspended even though no pause has been published yet.
  /// Lets the session's advance guard reject the window instead of silently advancing nothing.
  /// </summary>
  private bool pendingEventSuspension;

  /// <summary>
  /// Pause-worthy hits dropped because a pause was already active (see the pump).
  /// </summary>
  private int droppedPauses;

  private sealed class RequestRecord {
    public int Id { get; init; }

    public string Kind { get; init; }

    public EventRequest Request { get; init; }

    public string Target { get; init; }

    public string Location { get; init; }

    public string ConditionSource { get; init; }

    public EvalProgram Condition { get; init; }

    /// <summary>Zeroed once the Nth hit fired and the request re-armed count-free.</summary>
    public int HitCount { get; set; }
  }

  public bool IsPaused {
    get {
      lock (this.gate) {
        return this.pause is not null;
      }
    }
  }

  /// <summary>
  /// An event-caused suspension is active OR imminent (the pump is still classifying a suspending
  /// event set); the truthful guard for operations that need the VM free to run.
  /// </summary>
  public bool HoldsSuspension {
    get {
      lock (this.gate) {
        return this.pause is not null || this.pendingEventSuspension;
      }
    }
  }

  public PauseSnapshot CurrentPause {
    get {
      lock (this.gate) {
        return this.pause;
      }
    }
  }

  public long PauseSeq {
    get {
      lock (this.gate) {
        return this.pauseSeq;
      }
    }
  }

  // ---- Registry ----

  /// <summary>
  /// Arms a breakpoint on every overload matching the spec (one ID per overload) and starts the
  /// pump.
  /// The condition is parsed here so a bad expression fails at set time, not on the first hit.
  /// </summary>
  public IReadOnlyList<BreakpointBinding> AddBreakpoints(BreakpointSpec spec) {
    var type = this.ResolveTypeByName(spec.TypeName);

    var methods = type.GetMethods().Where(m => m.Name == spec.MethodName).ToList();

    if (methods.Count is 0) {
      throw new InvalidOperationException(
        $"no method '{spec.MethodName}' declared on {type.FullName} (methods are looked up on " +
        "the declaring type only; check the exact name with find_types members=true)"
      );
    }

    if (!string.IsNullOrEmpty(spec.SignatureContains)) {
      methods = methods.Where(m =>
          DebugController.FormatSignature(m)
            .Contains(spec.SignatureContains, StringComparison.OrdinalIgnoreCase)
        )
        .ToList();

      if (methods.Count is 0) {
        throw new InvalidOperationException(
          $"no overload of {type.FullName}.{spec.MethodName} matches signature substring " +
          $"'{spec.SignatureContains}'"
        );
      }
    }

    var condition = string.IsNullOrEmpty(spec.Condition) ? null : EvalParser.Parse(spec.Condition);

    // Every offset resolves BEFORE anything arms: a line missing from one overload's table must
    // reject the whole call with nothing enabled, never leave earlier overloads live behind an
    // error (they would freeze the game with no returned id to remove them by).
    var resolved = methods.Select(m => (Method: m, Offset: DebugController.ResolveOffset(m, spec)))
      .ToList();

    var bindings = new List<BreakpointBinding>();
    var armed = new List<RequestRecord>();

    try {
      foreach (var (method, (offset, where, warning)) in resolved) {
        var request = vm.CreateBreakpointRequest(method, offset);

        if (spec.HitCount > 0) {
          request.Count = spec.HitCount;
        }

        var record = new RequestRecord {
          Id = this.NextRequestId(),
          Kind = "breakpoint",
          Request = request,
          Target = DebugController.FormatSignature(method),
          Location = where,
          ConditionSource = spec.Condition,
          Condition = condition,
          HitCount = spec.HitCount
        };

        // Registered BEFORE Enable, so a hit delivered the instant the request arms (possible on
        // the direct-library path, where the VM keeps running) always finds its record instead of
        // being dropped as unrequested.
        this.Register(record);

        armed.Add(record);

        request.Enable();

        bindings.Add(
          new BreakpointBinding {
            Id = record.Id,
            Method = record.Target,
            Location = where,
            Warning = warning
          }
        );
      }
    }
    catch {
      // All-or-nothing: disarm and unregister whatever this call already armed, then rethrow.
      foreach (var record in armed) {
        _ = this.Remove(record.Id);
      }

      throw;
    }

    return bindings;
  }

  /// <summary>
  /// Arms an exception break (same ID space as breakpoints): fires when a matching exception is
  /// THROWN, caught or not; a Unity player catches nearly everything in its own loop, so an
  /// "uncaught only" mode would never fire.
  /// </summary>
  public BreakpointBinding AddExceptionBreak(string exceptionType, bool includeSubclasses) {
    var type = exceptionType is null ? null : this.ResolveTypeByName(exceptionType);

    var request = vm.CreateExceptionRequest(type, true, true);

    request.IncludeSubclasses = includeSubclasses;
    request.Enable();

    var target = type is null
      ? "any exception"
      : $"{type.FullName}{(includeSubclasses ? " (and subclasses)" : "")}";

    var record = new RequestRecord {
      Id = this.NextRequestId(),
      Kind = "exception",
      Request = request,
      Target = target,
      Location = "on throw"
    };

    this.Register(record);

    return new BreakpointBinding {
      Id = record.Id,
      Method = target,
      Location = record.Location
    };
  }

  public bool Remove(int id) {
    RequestRecord record;

    lock (this.gate) {
      if (!this.requests.Remove(id, out record)) {
        return false;
      }
    }

    DebugController.DisableQuietly(record.Request);

    return true;
  }

  public int RemoveAll() {
    List<RequestRecord> removed;

    lock (this.gate) {
      removed = [.. this.requests.Values];

      this.requests.Clear();

      // "Remove all" also disarms a step request left armed by a timed-out debug_step; it is the
      // only way to cancel one while unpaused (ResumeFromPause needs a pause to release).
      if (this.activeStep is not null) {
        DebugController.DisableQuietly(this.activeStep);

        this.activeStep = null;
      }
    }

    foreach (var record in removed) {
      DebugController.DisableQuietly(record.Request);
    }

    return removed.Count;
  }

  public DebugStatusInfo Status() {
    lock (this.gate) {
      return new DebugStatusInfo {
        PumpRunning = this.pump is not null && !this.pumpStopped,
        Paused = this.pause is not null,
        PauseReason = this.pause?.Reason,
        PauseThreadName = this.pause?.ThreadName,
        PauseBreakpointIds = this.pause?.BreakpointIds,
        PauseSeq = this.pause?.Seq ?? 0,
        DroppedPauses = this.droppedPauses,
        Breakpoints = this.requests.Values.OrderBy(r => r.Id)
          .Select(r => new BreakpointDescription {
              Id = r.Id,
              Kind = r.Kind,
              Target = r.Target,
              Location = r.Location,
              Condition = r.ConditionSource,
              HitCount = r.HitCount
            }
          )
          .ToArray()
      };
    }
  }

  // ---- Pause operations ----

  /// <summary>
  /// Returns the current pause immediately (a hit between arming and waiting is not missed), or
  /// blocks until the next pause; null on timeout (still running).
  /// </summary>
  public PauseSnapshot WaitForPause(TimeSpan timeout) {
    lock (this.gate) {
      return this.pause ?? this.WaitForPauseAfterLocked(this.pauseSeq, timeout);
    }
  }

  /// <summary>Releases the event-caused suspension; the game runs again.</summary>
  public void ResumeFromPause() {
    lock (this.gate) {
      if (this.pause is null) {
        throw new InvalidOperationException("not paused (no breakpoint/step/exception pause)");
      }

      this.pause = null;

      vm.Resume();
    }
  }

  /// <summary>Best-effort resume, for cleanup paths; true when a pause was released.</summary>
  public bool TryResumeFromPause() {
    lock (this.gate) {
      if (this.pause is null) {
        return false;
      }

      this.pause = null;

      try {
        vm.Resume();
      }
      catch {
        // Connection gone; the closed socket auto-resumes.
      }

      return true;
    }
  }

  /// <summary>
  /// Steps the paused thread and blocks until the re-pause (near-instant in normal code).
  /// Null on timeout: the step request stays armed, so a later <see cref="WaitForPause"/> can still
  /// catch its completion (the stepped code was blocking, e.g., in a wait).
  /// </summary>
  public PauseSnapshot Step(DebugStepKind kind, TimeSpan timeout) {
    lock (this.gate) {
      if (this.pause is null) {
        throw new InvalidOperationException(
          "not paused at an event; stepping needs a breakpoint/step/exception pause"
        );
      }

      // A step request left armed by a timed-out step would fire concurrently with this one.
      if (this.activeStep is not null) {
        DebugController.DisableQuietly(this.activeStep);

        this.activeStep = null;
      }

      var request = vm.CreateStepRequest(this.pause.Thread);

      request.Depth = kind switch {
        DebugStepKind.Over => StepDepth.Over,
        DebugStepKind.Into => StepDepth.Into,
        _ => StepDepth.Out
      };

      // Line-sized steps need a line table; without debug info, fall back to single IL steps.
      request.Size = DebugController.TopFrameHasLineInfo(this.pause.Thread)
        ? StepSize.Line
        : StepSize.Min;

      request.Filter = StepFilter.DebuggerHidden | StepFilter.StaticCtor;
      request.Enable();

      this.activeStep = request;
      this.EnsurePumpLocked();

      var seen = this.pauseSeq;
      this.pause = null;

      vm.Resume();

      return this.WaitForPauseAfterLocked(seen, timeout);
    }
  }

  // ---- Reporting ----

  /// <summary>Formats an event-caused pause (frames + one frame's variables).</summary>
  public PauseDetails Describe(PauseSnapshot snapshot, int frameIndex, bool allThreads) {
    string exceptionType = null;
    string exceptionMessage = null;

    if (snapshot.Exception is {} thrown) {
      exceptionType = thrown.Type.FullName;

      try {
        exceptionMessage = (invoker.GetProperty(thrown, "Message") as StringMirror)?.Value;
      }
      catch {
        // Best-effort; the type alone is still actionable.
      }
    }

    return this.BuildDetails(
      snapshot.Thread,
      frameIndex,
      allThreads,
      new PauseDetails {
        Reason = snapshot.Reason,
        Seq = snapshot.Seq,
        BreakpointIds = snapshot.BreakpointIds,
        ExceptionType = exceptionType,
        ExceptionMessage = exceptionMessage,
        ConditionError = snapshot.ConditionError
      }
    );
  }

  /// <summary>
  /// Formats a suspend-hold view (no pump event): same frame reporting, against the given thread
  /// (the session passes the main thread).
  /// </summary>
  public PauseDetails DescribeSuspended(ThreadMirror thread, int frameIndex, bool allThreads) {
    return this.BuildDetails(
      thread,
      frameIndex,
      allThreads,
      new PauseDetails {
        Reason = "suspend-hold"
      }
    );
  }

  /// <summary>
  /// Evaluates a parsed program in one frame of the given thread: a <see cref="FrameScope"/> is
  /// prepended to the caller's scopes.
  /// Frame slots are plain wire reads/writes (thread-agnostic); every INVOKE stays on the main
  /// thread, preserving the ECS thread-safety invariant even when the paused frame belongs to a
  /// worker thread.
  /// </summary>
  public EvalOutcome EvaluateInFrame(
    EvalProgram program,
    ThreadMirror thread,
    int frameIndex,
    EvalState state,
    IReadOnlyList<IEvalScope> tailScopes
  ) {
    // Validates the index up front with the frame-count error, before any statement runs.
    _ = DebugController.FrameAt(thread, frameIndex);

    var scopes = new List<IEvalScope> {
      new FrameScope(invoker, thread, frameIndex)
    };

    scopes.AddRange(tailScopes);

    var interpreter = new EvalInterpreter(invoker, scopes);

    return interpreter.Run(program, state);
  }

  /// <summary>
  /// The source mapping SDB can offer (it has no source text on the wire): per-method sequence
  /// points with a method, the type's method map without.
  /// </summary>
  public IReadOnlyList<MethodLocationInfo> Locations(
    string typeName,
    string methodName,
    string signatureContains
  ) {
    var type = this.ResolveTypeByName(typeName);

    var methods = type.GetMethods().AsEnumerable();

    if (methodName is not null) {
      methods = methods.Where(m => m.Name == methodName);
    }

    if (!string.IsNullOrEmpty(signatureContains)) {
      methods = methods.Where(m =>
        DebugController.FormatSignature(m)
          .Contains(signatureContains, StringComparison.OrdinalIgnoreCase)
      );
    }

    var detailed = methodName is not null;

    var result = methods.Select(m => DebugController.DescribeMethod(m, detailed)).ToArray();

    if (result.Length is 0) {
      throw new InvalidOperationException(
        methodName is null
          ? $"{type.FullName} declares no methods"
          : $"no method '{methodName}' declared on {type.FullName}"
      );
    }

    return result;
  }

  public void Dispose() {
    lock (this.gate) {
      if (this.disposed) {
        return;
      }

      this.disposed = true;

      foreach (var record in this.requests.Values) {
        DebugController.DisableQuietly(record.Request);
      }

      this.requests.Clear();

      if (this.activeStep is not null) {
        DebugController.DisableQuietly(this.activeStep);

        this.activeStep = null;
      }

      this.pause = null;

      Monitor.PulseAll(this.gate);
    }
  }

  // ---- Event pump ----

  private void EnsurePumpLocked() {
    if (this.pump is not null) {
      return;
    }

    this.pump = new Thread(this.PumpLoop) {
      IsBackground = true,
      Name = "sdb-debug-pump"
    };

    this.pump.Start();
  }

  private void PumpLoop() {
    while (true) {
      lock (this.gate) {
        if (this.disposed) {
          break;
        }
      }

      EventSet set;

      try {
        // The timeout is a liveness poll: it lets the pump notice disposal; events themselves
        // arrive through the connection's receiver thread and wake this immediately.
        set = vm.GetNextEventSet(500);
      }
      catch {
        // VMDisconnect or socket teardown: the pump dies with the attach.
        break;
      }

      if (set is null) {
        continue;
      }

      try {
        if (!this.HandleEventSet(set)) {
          break;
        }
      }
      catch {
        // A disconnect surfacing through classification (e.g. mid-condition invoke): the pump
        // dies with the attach rather than publishing a pause against a dead VM.
        break;
      }
    }

    lock (this.gate) {
      this.pumpStopped = true;

      Monitor.PulseAll(this.gate);
    }
  }

  /// <summary>False stops the pump (VM death/disconnect).</summary>
  private bool HandleEventSet(EventSet set) {
    if (set.Events.Any(e => e is VMDeathEvent or VMDisconnectEvent)) {
      return false;
    }

    // From here until this set is either resumed or published as a pause, the VM sits suspended
    // with no pause visible yet; the flag keeps HoldsSuspension truthful through that window
    // (conditions can take a while).
    if (set.SuspendPolicy is not SuspendPolicy.None) {
      lock (this.gate) {
        this.pendingEventSuspension = true;
      }
    }

    try {
      return this.ClassifyEventSet(set);
    }
    finally {
      lock (this.gate) {
        this.pendingEventSuspension = false;
      }
    }
  }

  private bool ClassifyEventSet(EventSet set) {
    var hits = new List<RequestRecord>();
    var stepHit = false;
    var userBreak = false;
    ObjectMirror exception = null;

    lock (this.gate) {
      foreach (var evt in set.Events) {
        switch (evt) {
          case BreakpointEvent or ExceptionEvent: {
            var record = this.requests.Values.FirstOrDefault(r =>
              ReferenceEquals(r.Request, evt.Request)
            );

            if (record is not null) {
              hits.Add(record);

              if (evt is ExceptionEvent thrown) {
                exception = thrown.Exception;
              }
            }

            break;
          }

          case StepEvent step when ReferenceEquals(step.Request, this.activeStep): {
            // Step requests persist until cleared; one debug_step means one re-pause.
            DebugController.DisableQuietly(this.activeStep);

            this.activeStep = null;
            stepHit = true;

            break;
          }

          case UserBreakEvent:
            // Debugger.Break() in game code: an explicit request to pause, honored unrequested.
            userBreak = true;

            break;
        }
      }
    }

    lock (this.gate) {
      if (this.pause is not null) {
        // A second set racing the current pause would stack a suspension the single ResumeFromPause
        // can never release; drop it, but COUNT a pause-worthy one (surfaced by debug_status), so a
        // lost concurrent hit is observable, not indistinguishable from "never hit".
        if (hits.Count > 0 || stepHit || userBreak) {
          this.droppedPauses++;
        }

        if (set.SuspendPolicy is not SuspendPolicy.None) {
          DebugController.ResumeQuietly(vm);
        }

        return true;
      }
    }

    if (hits.Count is 0 && !stepHit && !userBreak) {
      // Nothing we asked for (a removed request racing its last event, a user log, ...): never
      // leave the game frozen for it.
      if (set.SuspendPolicy is not SuspendPolicy.None) {
        DebugController.ResumeQuietly(vm);
      }

      return true;
    }

    // The agent's count modifier is a ONE-SHOT (verified empirically: it fires exactly at the
    // Nth occurrence and never again), while the tool contract promises "Nth hit onward";
    // re-arming count-free right after the Nth hit fired makes the contract true.
    // This runs BEFORE conditions, so a false-condition auto-resume cannot strand an exhausted
    // request.
    foreach (var hit in hits.Where(hit => hit.HitCount > 0)) {
      try {
        hit.Request.Disable();
        hit.Request.Count = 0;
        hit.Request.Enable();
      }
      catch {
        // Connection gone; the pump notices on its next dequeue.
      }

      lock (this.gate) {
        hit.HitCount = 0;
      }
    }

    // Conditions run OUTSIDE the gate (they do wire invokes); the event suspends the VM, so the
    // state they read is stable.
    string conditionError = null;

    var conditioned = hits.Where(h => h.Condition is not null).ToList();

    if (hits.Count > 0 &&
      !stepHit &&
      !userBreak &&
      exception is null &&
      conditioned.Count == hits.Count) {
      var anyTrue = false;

      foreach (var hit in conditioned) {
        if (this.EvaluateCondition(hit, set.Events[0].Thread, ref conditionError)) {
          anyTrue = true;

          break;
        }
      }

      if (!anyTrue && conditionError is null) {
        DebugController.ResumeQuietly(vm);

        return true;
      }
    }

    var thread = set.Events[0].Thread;

    string threadName;

    try {
      // Unity's main thread reports an empty name in player builds.
      threadName = string.IsNullOrEmpty(thread.Name) ? "(main)" : thread.Name;
    }
    catch {
      threadName = null;
    }

    var reason = exception is not null
      ? "exception"
      : hits.Count > 0
        ? "breakpoint"
        : stepHit
          ? "step"
          : "user-break";

    lock (this.gate) {
      this.pause = new PauseSnapshot {
        Seq = ++this.pauseSeq,
        Thread = thread,
        ThreadName = threadName,
        Reason = reason,
        BreakpointIds = hits.Select(h => h.Id).OrderBy(id => id).ToArray(),
        Exception = exception,
        ConditionError = conditionError
      };

      Monitor.PulseAll(this.gate);
    }

    return true;
  }

  /// <summary>
  /// True when the breakpoint should pause. An unevaluable condition pauses WITH the error
  /// recorded: auto-resuming would spin the pump hot on a hot path, and silently skipping would
  /// hide real hits; pausing once lets the agent repair the condition.
  /// </summary>
  private bool EvaluateCondition(RequestRecord record, ThreadMirror thread, ref string error) {
    try {
      var frames = thread.GetFrames();

      if (frames.Length is 0) {
        return true;
      }

      var interpreter = new EvalInterpreter(invoker, [new FrameScope(invoker, thread, 0)]);

      var outcome = interpreter.Run(record.Condition, new EvalState());

      return outcome.Value switch {
        bool b => b,
        PrimitiveValue { Value: bool b } => b,
        _ => throw new InvalidOperationException(
          $"condition evaluated to {outcome.TypeName}, expected bool"
        )
      };
    }
    catch (Exception ex) when (UnitySession.IsDisconnect(ex)) {
      // The debuggee died mid-evaluation: stop the pump (via the caller) instead of dressing the
      // disconnect up as a condition error and publishing a pause against a dead VM.
      throw;
    }
    catch (Exception ex) {
      error = $"breakpoint {record.Id} condition `{record.ConditionSource}` failed: {ex.Message}";

      return true;
    }
  }

  // ---- Internals ----

  /// <summary>
  /// Caller must hold the gate; waits for a pause newer than <paramref name="seen"/>.
  /// </summary>
  private PauseSnapshot WaitForPauseAfterLocked(long seen, TimeSpan timeout) {
    var deadline = DateTime.UtcNow + timeout;

    while (this.pause is null || this.pause.Seq <= seen) {
      if (this.disposed || this.pumpStopped) {
        throw new InvalidOperationException(
          "the debug session ended while waiting (detached or connection lost)"
        );
      }

      var remaining = deadline - DateTime.UtcNow;

      if (remaining <= TimeSpan.Zero || !Monitor.Wait(this.gate, remaining)) {
        return null;
      }
    }

    return this.pause;
  }

  private int NextRequestId() {
    lock (this.gate) {
      return ++this.nextId;
    }
  }

  private void Register(RequestRecord record) {
    lock (this.gate) {
      this.requests[record.Id] = record;

      this.EnsurePumpLocked();
    }
  }

  private TypeMirror ResolveTypeByName(string name) {
    var types = vm.GetTypes(name, true);

    return types.Count > 0
      ? types[0]
      : throw new InvalidOperationException(
        $"type '{name}' not found (use a fully-qualified name; see find_types)"
      );
  }

  private static (int Offset, string Where, string Warning) ResolveOffset(
    MethodMirror method,
    BreakpointSpec spec
  ) {
    if (spec.IlOffset is {} explicitOffset) {
      return (explicitOffset, $"IL {explicitOffset}", null);
    }

    if (spec.Line is {} line) {
      IList<Location> table;

      try {
        table = method.Locations;
      }
      catch (AbsentInformationException) {
        throw new InvalidOperationException(
          $"{method.DeclaringType.FullName}.{method.Name} has no debug info, so a line cannot " +
          "resolve; omit the line to break at method entry"
        );
      }

      var location = table.FirstOrDefault(l => l.LineNumber == line);

      if (location is null) {
        var lines = table.Select(l => l.LineNumber).Where(l => l > 0).ToList();

        throw new InvalidOperationException(
          lines.Count > 0
            ? $"line {line} has no sequence point in {method.Name} " +
            $"(covered lines {lines.Min()}-{lines.Max()}; see debug_locations)"
            : $"{method.Name} has no line table; omit the line to break at method entry"
        );
      }

      return (location.ILOffset, $"{location.SourceFile}:{line} (IL {location.ILOffset})", null);
    }

    // Method entry, the only mode that works without debug info.
    var warning = DebugController.HasDebugInfo(method)
      ? null
      : "no debug info for this method: entry breakpoints work, lines and locals do not";

    return (0, "method entry (IL 0)", warning);
  }

  private static bool HasDebugInfo(MethodMirror method) {
    try {
      return !string.IsNullOrEmpty(method.SourceFile);
    }
    catch {
      return false;
    }
  }

  private static bool TopFrameHasLineInfo(ThreadMirror thread) {
    try {
      var frames = thread.GetFrames();

      return frames.Length > 0 && frames[0].Location.LineNumber > 0;
    }
    catch {
      return false;
    }
  }

  private static StackFrame FrameAt(ThreadMirror thread, int frameIndex) {
    var frames = thread.GetFrames();

    if (frames.Length is 0) {
      throw new InvalidOperationException("the paused thread has no managed frames");
    }

    return frameIndex >= 0 && frameIndex < frames.Length
      ? frames[frameIndex]
      : throw new InvalidOperationException(
        $"frameIndex {frameIndex} out of range (0-{frames.Length - 1})"
      );
  }

  private PauseDetails BuildDetails(
    ThreadMirror thread,
    int frameIndex,
    bool allThreads,
    PauseDetails header
  ) {
    var frames = thread.GetFrames();

    var lines = frames.Take(DebugController.MaxFrames)
      .Select((f, i) => new FrameLine {
          Index = i,
          Method = DebugController.FormatSignature(f.Method),
          SourceFile = DebugController.SourceFileOf(f),
          Line = DebugController.LineOf(f),
          IlOffset = f.ILOffset
        }
      )
      .ToArray();

    var selected = DebugController.FrameAt(thread, frameIndex);

    var (locals, localsNote) = this.ReadLocals(selected);

    string threadName;

    try {
      threadName = thread.Name;
    }
    catch {
      threadName = null;
    }

    return new PauseDetails {
      Reason = header.Reason,
      Seq = header.Seq,
      BreakpointIds = header.BreakpointIds,
      ExceptionType = header.ExceptionType,
      ExceptionMessage = header.ExceptionMessage,
      ConditionError = header.ConditionError,
      ThreadName = string.IsNullOrEmpty(threadName) ? "(main)" : threadName,
      ThreadId = thread.Id,
      Frames = lines,
      FramesTruncated = frames.Length > DebugController.MaxFrames,
      FrameIndex = frameIndex,
      This = this.ReadThis(selected),
      Parameters = this.ReadParameters(selected),
      Locals = locals,
      LocalsNote = localsNote,
      Threads = allThreads ? this.ReadAllThreads() : null
    };
  }

  private string ReadThis(StackFrame frame) {
    try {
      var self = frame.GetThis();

      return self is null ? null : invoker.Format(self);
    }
    catch {
      return null;
    }
  }

  private VariableView[] ReadParameters(StackFrame frame) {
    return frame.Method.GetParameters()
      .Select(p => new VariableView {
          Name = p.Name,
          Value = this.FormatSlot(() => frame.GetValue(p))
        }
      )
      .ToArray();
  }

  private (IReadOnlyList<VariableView> Locals, string Note) ReadLocals(StackFrame frame) {
    IList<LocalVariable> visible;

    try {
      visible = frame.GetVisibleVariables();
    }
    catch (AbsentInformationException) {
      return ([], "no debug info for this method: local variable names are unavailable");
    }
    catch (Exception ex) {
      return ([], $"locals unavailable: {ex.Message}");
    }

    var locals = visible.Where(v => !v.IsArg)
      .Select(v => new VariableView {
          Name = v.Name,
          Value = this.FormatSlot(() => frame.GetValue(v))
        }
      )
      .ToArray();

    return (locals, null);
  }

  private string FormatSlot(Func<Value> read) {
    try {
      return invoker.Format(read());
    }
    catch (Exception ex) {
      return $"<unreadable: {ex.Message}>";
    }
  }

  private ThreadView[] ReadAllThreads() {
    var threads = vm.GetThreads();

    // One bulk round-trip for every stack instead of one per thread.
    try {
      ThreadMirror.FetchFrames(threads);
    }
    catch {
      // Per-thread GetFrames below still works, just slower.
    }

    return threads.Select(t => {
          string[] methods;

          try {
            methods = t.GetFrames()
              .Take(DebugController.MaxFrames)
              .Select(f => DebugController.FormatSignature(f.Method))
              .ToArray();
          }
          catch {
            methods = [];
          }

          string name;

          try {
            name = t.Name;
          }
          catch {
            name = null;
          }

          return new ThreadView {
            Name = string.IsNullOrEmpty(name) ? "(unnamed)" : name,
            Id = t.Id,
            State = DebugController.ThreadStateOf(t),
            FrameMethods = methods
          };
        }
      )
      .ToArray();
  }

  private static string ThreadStateOf(ThreadMirror thread) {
    try {
      return thread.ThreadState.ToString();
    }
    catch {
      return "unknown";
    }
  }

  private static MethodLocationInfo DescribeMethod(MethodMirror method, bool detailed) {
    IList<Location> table;

    try {
      table = method.Locations;
    }
    catch (AbsentInformationException) {
      table = null;
    }

    var lines = table?.Select(l => l.LineNumber).Where(l => l > 0).ToList();
    var hasInfo = lines is { Count: > 0 };

    return new MethodLocationInfo {
      Signature = DebugController.FormatSignature(method),
      SourceFile = hasInfo ? table[0].SourceFile : null,
      HasDebugInfo = hasInfo,
      StartLine = hasInfo ? lines.Min() : null,
      EndLine = hasInfo ? lines.Max() : null,
      SequencePoints = detailed && hasInfo
        ? table.Select(l => new SequencePointInfo {
              IlOffset = l.ILOffset,
              Line = l.LineNumber,
              EndLine = l.EndLineNumber
            }
          )
          .ToArray()
        : null
    };
  }

  private static string SourceFileOf(StackFrame frame) {
    try {
      return frame.Location.SourceFile;
    }
    catch {
      return null;
    }
  }

  private static int LineOf(StackFrame frame) {
    try {
      return frame.Location.LineNumber;
    }
    catch {
      return 0;
    }
  }

  private static string FormatSignature(MethodMirror method) {
    var parameters = string.Join(
      ", ",
      method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}")
    );

    return $"{method.ReturnType.Name} {method.DeclaringType.FullName}.{method.Name}({parameters})";
  }

  private static void DisableQuietly(EventRequest request) {
    try {
      request.Disable();
    }
    catch {
      // Connection gone or request already cleared agent-side.
    }
  }

  private static void ResumeQuietly(VirtualMachine target) {
    try {
      target.Resume();
    }
    catch {
      // Connection gone; the closed socket auto-resumes.
    }
  }
}
