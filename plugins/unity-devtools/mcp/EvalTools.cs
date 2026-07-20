using System.ComponentModel;
using System.Globalization;
using System.Text;
using JetBrains.Annotations;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using UnityDevtools.Sdb;
using UnityDevtools.Sdb.Eval;

namespace UnityDevtools.Mcp;

/// <summary>
/// The C# expression evaluator tool: parses a statement sequence client-side and interprets it
/// against the live game as mirror primitives (SDB has no expression-evaluation command).
/// </summary>
[McpServerToolType]
[UsedImplicitly]
public sealed class EvalTools(UnitySession session, EvalState state) {
  [McpServerTool(Name = "eval")]
  [Description(
    """
    Evaluate a C# statement sequence against the running game, like an IDE debugger: `var`
    declarations, expression statements, and assignments; the final expression's value is the
    result.
    Calls run on the game's main thread; writes mutate live game state.
    Supported: literals, member access, method calls (including explicit generic type arguments,
    e.g. em.GetComponentData<MyComponent>(entity(123, 1))), indexers, `new` with initializers,
    casts, operators, ternary/`?.`/`??`, typeof(T), string interpolation, and `out var x` / `out x`
    arguments.
    Not supported: lambdas, LINQ, loops, control flow.
    Roots: fully-qualified type names, plus builtins `em` (the world's EntityManager), `world` (the
    ECS World), `entity(index, version)` (an Entity value), and `_` (the previous successful eval's
    result; heap results may be collected once the game resumes).
    Struct writes follow C# lvalue semantics: chained writes through object fields and array
    elements persist; a struct temporary returned by a method or property is rejected; a component
    fetched into a `var` local stays a client-side copy, so persist it with
    em.SetComponentData(entity(...), copy).
    The whole sequence runs in one suspend window; combine with the suspend tool for consistency
    across several eval calls. Attaches lazily.
    """
  )]
  [UsedImplicitly]
  public EvalToolResult Eval(
    [Description("C# statement sequence; the final expression's value is the result.")] string code,
    [Description("ECS world name for the em/world builtins; omit for the default world.")]
    string? world = null
  ) {
    return ToolGuard.Run(() => {
        EvalProgram program;

        try {
          program = EvalParser.Parse(code);
        }
        catch (EvalParseException e) {
          throw new McpException(
            $"parse error at offset {e.Position.ToString(CultureInfo.InvariantCulture)}: " +
            e.Message
          );
        }

        return session.Run(ctx => Operation(ctx, program));
      }
    );

    EvalToolResult Operation(SdbContext ctx, EvalProgram program) {
      var inv = ctx.Invoker;

      // ECS is resolved lazily so type-rooted expressions work on games without live worlds;
      // only touching em/world/entity pays for (and can fail on) world resolution.
      var ecs = new Lazy<Ecs>(() => ctx.Ecs(world));

      var interpreter = new EvalInterpreter(inv, [new BuiltinScope(inv, () => ecs.Value, state)]);

      try {
        var outcome = interpreter.Run(program, state);

        return new EvalToolResult {
          World = ecs.IsValueCreated ? ecs.Value.WorldName : null,
          Result = outcome.Formatted,
          Type = outcome.TypeName
        };
      }
      catch (EvalFailedException e) {
        throw new McpException(EvalTools.FailureReport(e));
      }
    }
  }

  private static string FailureReport(EvalFailedException e) {
    var report = new StringBuilder();

    _ = report.Append(
      CultureInfo.InvariantCulture,
      $"statement {e.StatementIndex + 1} (`{e.StatementSource}`) failed at offset " +
      $"{e.Position}: {e.Message}"
    );

    if (e.GameExceptionType is not null) {
      _ = report.Append(
        CultureInfo.InvariantCulture,
        $"\nin-game exception: {e.GameExceptionType}"
      );

      if (e.GameExceptionMessage is not null) {
        _ = report.Append(CultureInfo.InvariantCulture, $": {e.GameExceptionMessage}");
      }
    }

    if (e.Locals.Count <= 0) {
      return report.ToString();
    }

    _ = report.Append("\nlocals so far: ");
    _ = report.Append(string.Join(", ", e.Locals.Select(l => $"{l.Key} = {l.Value}")));

    return report.ToString();
  }
}

/// <summary>Result of the <c>eval</c> tool; the value is rendered as text.</summary>
public sealed record EvalToolResult {
  /// <summary>The ECS world involved, when the program touched an ECS builtin.</summary>
  public required string? World { [UsedImplicitly] get; init; }

  public required string Result { [UsedImplicitly] get; init; }

  /// <summary>The result value's type full name.</summary>
  public required string Type { [UsedImplicitly] get; init; }
}
