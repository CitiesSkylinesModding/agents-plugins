using System.ComponentModel;
using JetBrains.Annotations;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using UnityDevtools.Sdb;

namespace UnityDevtools.Mcp;

/// <summary>
/// Type reflection tools: resolve live types by name and inspect their members (invocation lives
/// in the eval tool).
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

      if (types.Count is 0) {
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
