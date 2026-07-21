using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb.Eval;

/// <summary>
/// One link of the evaluator's binding-scope chain: resolves root identifiers and bare function
/// calls before type-prefix resolution kicks in.
/// The chain is pluggable, so a future frame-context evaluation (SDB breakpoint tools) can prepend
/// a StackFrame-backed scope without touching the grammar or the walker.
/// </summary>
public interface IEvalScope {
  bool TryResolveValue(string name, out object value);

  bool TryCall(string name, object[] args, out object result);

  /// <summary>
  /// Whether <see cref="TrySetValue"/> would accept this name; probed BEFORE the assignment's right
  /// side evaluates, so a read-only target rejects before any side effects run.
  /// </summary>
  bool CanSetValue(string name) => false;

  /// <summary>
  /// Writes a root identifier the scope owns (e.g., a frame local); false when the name is not
  /// writable through this scope, so assignment falls through the chain.
  /// </summary>
  bool TrySetValue(string name, object value) => false;
}

/// <summary>
/// Per-server-session evaluator state: the <c>_</c> slot holding the last successful result.
/// Heap mirrors stored here may be garbage-collected once the game resumes; using a collected
/// mirror fails the evaluation with a "re-evaluate" hint.
/// </summary>
public sealed class EvalState {
  public object LastResult { get; private set; }

  public bool HasLastResult { get; private set; }

  public void Store(object result) {
    this.LastResult = result;
    this.HasLastResult = true;
  }
}

/// <summary>
/// The frameless builtin scope: <c>em</c> (selected world's EntityManager), <c>world</c> (the
/// selected World), <c>entity(index, version)</c> (client-side Entity literal), and <c>_</c>
/// (last successful result).
/// ECS access is lazily bound: a non-ECS game evaluates type-rooted expressions fine, and only
/// touching an ECS builtin fails.
/// </summary>
public sealed class BuiltinScope(Invoker inv, Func<Ecs> ecs, EvalState state) : IEvalScope {
  public bool TryResolveValue(string name, out object value) {
    switch (name) {
      case "em":
        value = ecs().EntityManager;

        return true;

      case "world":
        value = ecs().World;

        return true;

      case "_":
        if (!state.HasLastResult) {
          throw new EvalRuntimeException(
            "`_` is empty: no previous successful eval in this session"
          );
        }

        // A mirror from a previous attach is useless against the current VM (the session reattaches
        // transparently when the game restarts); fail with the re-evaluate hint instead of an
        // opaque VM-mismatch error. Client-side values stay valid.
        if (state.LastResult is Value mirror && mirror.VirtualMachine != inv.Vm) {
          throw new EvalRuntimeException(
            "`_` was captured in a previous debugger session (the game restarted or the " +
            "connection was re-established); re-evaluate it"
          );
        }

        value = state.LastResult;

        return true;

      default:
        value = null;

        return false;
    }
  }

  public bool TryCall(string name, object[] args, out object result) {
    if (name is not "entity") {
      result = null;

      return false;
    }

    // Version defaults to 1, mirroring the ecs_* tools' `index[:version]` spec.
    if (args.Length is not (1 or 2) ||
      args[0] is not int index ||
      (args.Length is 2 && args[1] is not int)) {
      throw new EvalRuntimeException("entity() expects (int index[, int version = 1])");
    }

    var version = args.Length is 2 ? (int) args[1] : 1;

    result = Ecs.MakeEntity(inv, index, version);

    return true;
  }
}
