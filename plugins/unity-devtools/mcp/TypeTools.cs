using System.ComponentModel;
using JetBrains.Annotations;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Mono.Debugger.Soft;
using UnityDevtools.Sdb;

namespace UnityDevtools.Mcp;

/// <summary>
/// Type reflection and method invocation tools: resolve live types by name and call static or
/// managed-system methods in the debuggee.
/// </summary>
[McpServerToolType]
[UsedImplicitly]
public sealed class TypeTools(UnitySession session) {
  [McpServerTool(Name = "find_types")]
  [Description(
    """
    Resolve a type in the running game by fully-qualified name (case-insensitive) and report where
    it lives; with members=true, also list its fields, properties, and method signatures.
    Attaches lazily; the game is only briefly suspended.
    """
  )]
  [UsedImplicitly]
  public FindTypesResult FindTypes(
    [Description("Fully-qualified type name, e.g. Unity.Entities.Entity.")] string fullName,
    [Description("Also list fields, properties, and methods.")] bool members = false
  ) {
    return ToolGuard.Run(() => session.Run(Operation));

    FindTypesResult Operation(SdbContext ctx) {
      var types = ctx.Vm.GetTypes(fullName, true);

      if (types.Count == 0) {
        throw new McpException(
          $"type '{fullName}' not found (the name must be fully qualified; discover names " +
          "offline from the game's Managed assemblies, e.g. with a decompiler)"
        );
      }

      return new FindTypesResult {
        Query = fullName,
        Types = types.Select(t => new TypeDescription {
              FullName = t.FullName,
              Assembly = t.Assembly.GetName().Name ?? "<unknown>",
              Kind = t.IsValueType
                ? "struct"
                : t.IsInterface
                  ? "interface"
                  : "class",
              Fields = members
                ? t.GetFields()
                  .Where(f => !f.IsStatic)
                  .Select(f => $"{f.Name}: {f.FieldType.FullName}")
                  .ToArray()
                : null,
              Properties =
                members
                  ? t.GetProperties().Select(p => $"{p.Name}: {p.PropertyType.FullName}").ToArray()
                  : null,
              Methods = members
                ? t.GetMethods()
                  .Select(m => {
                      var pars = string.Join(
                        ", ",
                        m.GetParameters().Select(x => $"{x.ParameterType.Name} {x.Name}")
                      );

                      return $"{m.ReturnType.Name} {m.Name}({pars})";
                    }
                  )
                  .ToArray()
                : null
            }
          )
          .ToArray()
      };
    }
  }

  [McpServerTool(Name = "invoke")]
  [Description(
    """
    Call a method in the running game on the main thread and report the result.
    Two targets: "static" calls a static method on a type (out-parameter values are reported after
    the call; pass out-int / out-entity placeholders for them), "system" calls an instance method on
    a managed ECS system fetched from the world.
    Attaches lazily; the game is only briefly suspended unless a suspend hold is active.
    """
  )]
  [UsedImplicitly]
  public InvokeToolResult Invoke(
    [Description("\"static\" (static method on a type) or \"system\" (managed ECS system method).")]
    string target,
    [Description("Fully-qualified type name: the declaring type, or the managed system type.")]
    string type,
    [Description("Method name, matched together with the argument count.")] string method,
    [Description(
      """
      Arguments as text tokens, coerced to the resolved method's parameter types:
      numbers/bools/enums (numeric) per the signature, "index[:version]" for an Entity parameter,
      "em" for an EntityManager parameter, "out-int"/"out-entity" placeholders for out
      parameters, and raw text for string parameters.
      Same-arity overloads are tried until one signature accepts the arguments.
      """
    )]
    string[]? args = null,
    [Description("ECS world name; omit for the default world.")] string? world = null
  ) {
    return ToolGuard.Run(() => session.Run(Operation));

    InvokeToolResult Operation(SdbContext ctx) {
      var inv = ctx.Invoker;

      // The ECS world is resolved lazily: a static call on a game without live ECS worlds
      // must work, so only the "system" target and the "em" token pay for world resolution.
      var ecs = new Lazy<Ecs>(() => ctx.Ecs(world));

      Value? system = null;

      var declaringType = target switch {
        "static" => inv.ResolveType(type),
        "system" => inv.TypeOf(system = ecs.Value.GetSystem(type)),
        _ => throw new McpException("target must be \"static\" or \"system\"")
      };

      var (m, parsed) = TypeTools.BindMethod(inv, declaringType, method, args ?? [], () => ecs.Value.EntityManager);

      if (system is null) {
        var result = inv.InvokeStaticWithOutArgs(declaringType, m, parsed);

        return new InvokeToolResult {
          World = ecs.IsValueCreated ? ecs.Value.WorldName : null,
          Result = inv.Format(result.Result, 3),
          ArgsAfterCall = result.OutArgs?.Select(a => inv.Format(a, 3)).ToArray()
        };
      }

      return new InvokeToolResult {
        World = ecs.Value.WorldName,
        Result = inv.Format(inv.Invoke(system, m, parsed), 3),
        ArgsAfterCall = null
      };
    }
  }

  /// <summary>
  /// Picks the overload whose signature accepts the raw tokens (coercion is signature-driven, so
  /// tokens carry no type syntax) and returns it with the coerced argument values.
  /// </summary>
  private static (MethodMirror Method, Value[] Args) BindMethod(
    Invoker inv,
    TypeMirror type,
    string name,
    string[] rawArgs,
    Func<Value> entityManager
  ) {
    var candidates = inv.FindMethods(type, name, rawArgs.Length);

    if (candidates.Count == 0) {
      throw new McpException($"method {type.Name}.{name}/{rawArgs.Length} not found");
    }

    var failures = new List<string>();

    foreach (var candidate in candidates) {
      var parameters = candidate.GetParameters();
      var parsed = new Value[rawArgs.Length];

      try {
        for (var i = 0; i < rawArgs.Length; i++) {
          parsed[i] = Ecs.CoerceArg(inv, parameters[i].ParameterType, rawArgs[i], entityManager);
        }
      }
      catch (Exception e) {
        failures.Add($"{TypeTools.Signature(candidate)}: {e.Message}");

        continue;
      }

      return (candidate, parsed);
    }

    throw new McpException(
      $"no overload of {type.Name}.{name} accepts these arguments; tried: " +
      string.Join("; ", failures)
    );
  }

  private static string Signature(MethodMirror m) =>
    $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})";
}

/// <summary>Result of the <c>find_types</c> tool.</summary>
public sealed record FindTypesResult {
  public required string Query { [UsedImplicitly] get; init; }

  public required IReadOnlyList<TypeDescription> Types { [UsedImplicitly] get; init; }
}

/// <summary>One resolved type; member lists are present only when requested.</summary>
public sealed record TypeDescription {
  public required string FullName { [UsedImplicitly] get; init; }

  public required string Assembly { [UsedImplicitly] get; init; }

  public required string Kind { [UsedImplicitly] get; init; }

  public IReadOnlyList<string>? Fields { [UsedImplicitly] get; init; }

  public IReadOnlyList<string>? Properties { [UsedImplicitly] get; init; }

  public IReadOnlyList<string>? Methods { [UsedImplicitly] get; init; }
}

/// <summary>Result of the <c>invoke</c> tool; values are rendered as text.</summary>
public sealed record InvokeToolResult {
  /// <summary>
  /// The ECS world involved, when the call needed one (system target or em token).
  /// </summary>
  public required string? World { [UsedImplicitly] get; init; }

  public required string Result { [UsedImplicitly] get; init; }

  /// <summary>
  /// Every argument's value after a static call ("out" values updated), when reported.
  /// </summary>
  public required IReadOnlyList<string>? ArgsAfterCall { [UsedImplicitly] get; init; }
}
