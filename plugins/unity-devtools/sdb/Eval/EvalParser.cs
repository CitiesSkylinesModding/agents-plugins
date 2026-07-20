using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnityDevtools.Sdb.Eval;

/// <summary>
/// Parses a C# statement sequence into the evaluator's AST using Roslyn parse-only (no semantic
/// model, no compilation): the code is parsed as top-level statements, then translated node by
/// node, rejecting anything outside the supported grammar with an "unsupported: ..." error.
/// </summary>
public static class EvalParser {
  public static EvalProgram Parse(string code) {
    // The final expression may omit its trailing semicolon (REPL ergonomics), and a trailing line
    // comment would swallow one appended on the same line, so the terminator goes on its own line
    // unconditionally (a redundant `;` parses as an empty statement, dropped by translation).
    // Positions are unaffected: nothing before the end moves.
    var trimmed = (code ?? "").TrimEnd();

    var source = trimmed + "\n;";

    var tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default);

    var error = tree.GetDiagnostics().FirstOrDefault(d => d.Severity is DiagnosticSeverity.Error);

    if (error is not null) {
      // Clamp: the error can sit on the appended terminator, past the typed input.
      throw new EvalParseException(
        $"syntax error: {error.GetMessage()}",
        Math.Min(error.Location.SourceSpan.Start, trimmed.Length)
      );
    }

    var root = (CompilationUnitSyntax) tree.GetRoot();

    if (root.Usings.Count > 0) {
      throw new EvalParseException(
        "unsupported: using directive (write fully-qualified type names instead)",
        root.Usings[0].SpanStart
      );
    }

    var statements = new List<EvalStatement>();

    foreach (var member in root.Members) {
      if (member is not GlobalStatementSyntax global) {
        throw EvalParser.Unsupported(member);
      }

      var statement = EvalParser.TranslateStatement(global.Statement, trimmed);

      if (statement is not null) {
        statements.Add(statement);
      }
    }

    if (statements.Count is 0) {
      throw new EvalParseException("nothing to evaluate: the program is empty or only comments", 0);
    }

    return new EvalProgram(trimmed, statements);
  }

  private static EvalStatement TranslateStatement(StatementSyntax statement, string original) {
    // The appended semicolon can push the last statement's span one char past the original code;
    // clamp so error reports never quote text that was not typed.
    var length = Math.Min(statement.Span.Length, original.Length - statement.SpanStart);

    switch (statement) {
      case EmptyStatementSyntax: return null;

      case ExpressionStatementSyntax expr:
        return new ExprStatement(EvalParser.TranslateExpr(expr.Expression)) {
          Position = statement.SpanStart,
          Length = length
        };

      case LocalDeclarationStatementSyntax local: {
        if (local.Modifiers.Count > 0 || !local.Declaration.Type.IsVar) {
          throw new EvalParseException(
            "unsupported: explicitly typed or modified declaration (use `var name = ...`)",
            statement.SpanStart
          );
        }

        if (local.Declaration.Variables.Count != 1) {
          throw new EvalParseException(
            "unsupported: multiple declarators in one statement (declare one var per statement)",
            statement.SpanStart
          );
        }

        var declarator = local.Declaration.Variables[0];

        if (declarator.Initializer is null) {
          throw new EvalParseException(
            "unsupported: declaration without an initializer",
            statement.SpanStart
          );
        }

        return new VarStatement(
          declarator.Identifier.Text,
          EvalParser.TranslateExpr(declarator.Initializer.Value)
        ) {
          Position = statement.SpanStart,
          Length = length
        };
      }

      default: throw EvalParser.Unsupported(statement);
    }
  }

  private static EvalExpr TranslateExpr(ExpressionSyntax expression) {
    while (true) {
      switch (expression) {
        case ParenthesizedExpressionSyntax paren:
          expression = paren.Expression;

          continue;

        case LiteralExpressionSyntax literal: return EvalParser.TranslateLiteral(literal);

        case IdentifierNameSyntax identifier:
          return new NameExpr(identifier.Identifier.Text) {
            Position = identifier.SpanStart
          };

        case MemberAccessExpressionSyntax member
          when member.IsKind(SyntaxKind.SimpleMemberAccessExpression) &&
          member.Name is IdentifierNameSyntax name:
          return new MemberExpr(EvalParser.TranslateExpr(member.Expression), name.Identifier.Text) {
            Position = member.SpanStart
          };

        case InvocationExpressionSyntax invocation:
          return EvalParser.TranslateInvocation(invocation);

        case ElementAccessExpressionSyntax element:
          return new IndexExpr(
            EvalParser.TranslateExpr(element.Expression),
            element.ArgumentList.Arguments.Select(a =>
                a.RefKindKeyword.IsKind(SyntaxKind.None) && a.NameColon is null
                  ? EvalParser.TranslateExpr(a.Expression)
                  : throw EvalParser.Unsupported(a)
              )
              .ToArray()
          ) {
            Position = element.SpanStart
          };

        case BinaryExpressionSyntax binary when EvalParser.IsSupportedBinary(binary):
          return new BinaryExpr(
            binary.OperatorToken.Text,
            EvalParser.TranslateExpr(binary.Left),
            EvalParser.TranslateExpr(binary.Right)
          ) {
            Position = binary.SpanStart
          };

        case PrefixUnaryExpressionSyntax unary when EvalParser.IsSupportedUnary(unary):
          return new UnaryExpr(unary.OperatorToken.Text, EvalParser.TranslateExpr(unary.Operand)) {
            Position = unary.SpanStart
          };

        case ConditionalExpressionSyntax conditional:
          return new ConditionalExpr(
            EvalParser.TranslateExpr(conditional.Condition),
            EvalParser.TranslateExpr(conditional.WhenTrue),
            EvalParser.TranslateExpr(conditional.WhenFalse)
          ) {
            Position = conditional.SpanStart
          };

        case AssignmentExpressionSyntax assignment
          when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
          return new AssignExpr(
            EvalParser.TranslateExpr(assignment.Left),
            EvalParser.TranslateExpr(assignment.Right)
          ) {
            Position = assignment.SpanStart
          };

        case AssignmentExpressionSyntax compound:
          throw new EvalParseException(
            "unsupported: compound assignment (write `x = x op y` instead)",
            compound.SpanStart
          );

        case ObjectCreationExpressionSyntax creation: return EvalParser.TranslateCreation(creation);

        case CastExpressionSyntax cast:
          return new CastExpr(
            EvalParser.TranslateTypeName(cast.Type),
            EvalParser.TranslateExpr(cast.Expression)
          ) {
            Position = cast.SpanStart
          };

        case TypeOfExpressionSyntax typeOf:
          return new TypeofExpr(EvalParser.TranslateTypeName(typeOf.Type)) {
            Position = typeOf.SpanStart
          };

        case InterpolatedStringExpressionSyntax interpolated:
          return EvalParser.TranslateInterpolatedString(interpolated);

        case ConditionalAccessExpressionSyntax conditionalAccess:
          return new ConditionalAccessExpr(
            EvalParser.TranslateExpr(conditionalAccess.Expression),
            EvalParser.TranslateExpr(conditionalAccess.WhenNotNull)
          ) {
            Position = conditionalAccess.SpanStart
          };

        // `.Name` inside conditional access; the receiver is the tested target's value.
        case MemberBindingExpressionSyntax { Name: IdentifierNameSyntax bindingName } binding:
          return new MemberExpr(
            new ImplicitReceiverExpr {
              Position = binding.SpanStart
            },
            bindingName.Identifier.Text
          ) {
            Position = binding.SpanStart
          };

        default: throw EvalParser.Unsupported(expression);
      }
    }
  }

  private static NewExpr TranslateCreation(ObjectCreationExpressionSyntax creation) {
    var initializers = new List<FieldInit>();

    if (creation.Initializer is not null) {
      if (!creation.Initializer.IsKind(SyntaxKind.ObjectInitializerExpression)) {
        throw EvalParser.Unsupported(creation.Initializer);
      }

      foreach (var entry in creation.Initializer.Expressions) {
        if (entry is not AssignmentExpressionSyntax {
            Left: IdentifierNameSyntax field
          } assignment ||
          !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)) {
          throw new EvalParseException(
            "unsupported: object-initializer entry (use `Field = value`)",
            entry.SpanStart
          );
        }

        initializers.Add(
          new FieldInit(field.Identifier.Text, EvalParser.TranslateExpr(assignment.Right))
        );
      }
    }

    var args = creation.ArgumentList is null
      ? []
      : creation.ArgumentList.Arguments.Select(EvalParser.TranslateArgument).ToArray();

    return new NewExpr(EvalParser.TranslateTypeName(creation.Type), args, initializers) {
      Position = creation.SpanStart
    };
  }

  private static InterpolatedStringExpr TranslateInterpolatedString(
    InterpolatedStringExpressionSyntax interpolated
  ) {
    var parts = new List<EvalExpr>();

    foreach (var content in interpolated.Contents) {
      switch (content) {
        case InterpolatedStringTextSyntax text:
          parts.Add(
            new LiteralExpr(text.TextToken.ValueText) {
              Position = text.SpanStart
            }
          );

          break;

        case InterpolationSyntax interpolation:
          if (interpolation.AlignmentClause is not null || interpolation.FormatClause is not null) {
            throw new EvalParseException(
              "unsupported: interpolation alignment/format clause (format the value yourself)",
              interpolation.SpanStart
            );
          }

          parts.Add(EvalParser.TranslateExpr(interpolation.Expression));

          break;

        default: throw EvalParser.Unsupported(content);
      }
    }

    return new InterpolatedStringExpr(parts) {
      Position = interpolated.SpanStart
    };
  }

  private static bool IsSupportedBinary(BinaryExpressionSyntax binary) {
    // `is`/`as` need type-pattern semantics the walker does not implement; everything else the
    // C# grammar calls a binary expression is computed client-side (or lazily for &&/||/??).
    return !binary.IsKind(SyntaxKind.IsExpression) && !binary.IsKind(SyntaxKind.AsExpression);
  }

  private static bool IsSupportedUnary(PrefixUnaryExpressionSyntax unary) {
    return unary.Kind() is SyntaxKind.UnaryMinusExpression or
      SyntaxKind.UnaryPlusExpression or
      SyntaxKind.LogicalNotExpression or
      SyntaxKind.BitwiseNotExpression;
  }

  private static CallExpr TranslateInvocation(InvocationExpressionSyntax invocation) {
    var args = invocation.ArgumentList.Arguments.Select(EvalParser.TranslateArgument).ToArray();

    var (target, nameSyntax) = invocation.Expression switch {
      MemberAccessExpressionSyntax member when member.IsKind(
        SyntaxKind.SimpleMemberAccessExpression
      ) => (EvalParser.TranslateExpr(member.Expression), member.Name),
      SimpleNameSyntax bare => (null, bare),

      // `?.Method(...)`: the receiver is the conditional access's tested value.
      MemberBindingExpressionSyntax binding => (new ImplicitReceiverExpr {
        Position = binding.SpanStart
      }, binding.Name),
      _ => throw EvalParser.Unsupported(invocation.Expression)
    };

    var (name, typeArgs) = nameSyntax switch {
      GenericNameSyntax generic => (generic.Identifier.Text,
        generic.TypeArgumentList.Arguments.Select(EvalParser.TranslateTypeName).ToArray()),
      IdentifierNameSyntax plain => (plain.Identifier.Text, []),
      _ => throw EvalParser.Unsupported(nameSyntax)
    };

    // Roslyn parses `nameof(x)` as a plain invocation; without this it would surface as a
    // confusing "unknown function 'nameof'" at evaluation time.
    if (target is null && name == "nameof") {
      throw new EvalParseException(
        "unsupported: nameof (write the string literal)",
        invocation.SpanStart
      );
    }

    return new CallExpr(target, name, typeArgs, args) {
      Position = invocation.SpanStart
    };
  }

  private static ArgExpr TranslateArgument(ArgumentSyntax argument) {
    if (argument.NameColon is not null) {
      throw new EvalParseException(
        "unsupported: named argument (pass arguments positionally)",
        argument.SpanStart
      );
    }

    if (argument.RefKindKeyword.IsKind(SyntaxKind.None)) {
      return new ArgExpr(EvalParser.TranslateExpr(argument.Expression));
    }

    if (!argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) {
      throw EvalParser.Unsupported(argument);
    }

    switch (argument.Expression) {
      // `out var x` declares the local the call writes.
      case DeclarationExpressionSyntax {
        Type.IsVar: true, Designation: SingleVariableDesignationSyntax variable
      } declaration:
        return new ArgExpr(
          new NameExpr(variable.Identifier.Text) {
            Position = declaration.SpanStart
          },
          ArgMode.OutVar
        );

      case IdentifierNameSyntax identifier:
        return new ArgExpr(
          new NameExpr(identifier.Identifier.Text) {
            Position = identifier.SpanStart
          },
          ArgMode.Out
        );

      default:
        throw new EvalParseException(
          "unsupported: out argument target (use `out var x` or `out x`)",
          argument.SpanStart
        );
    }
  }

  /// <summary>
  /// Renders a type reference as the dotted fully qualified name the live type lookup expects;
  /// predefined keywords map to their System names.
  /// </summary>
  private static string TranslateTypeName(TypeSyntax type) {
    switch (type) {
      case IdentifierNameSyntax identifier: return identifier.Identifier.Text;

      case QualifiedNameSyntax { Right: IdentifierNameSyntax right } qualified:
        return $"{EvalParser.TranslateTypeName(qualified.Left)}.{right.Identifier.Text}";

      case PredefinedTypeSyntax predefined: {
        var mapped = predefined.Keyword.Kind() switch {
          SyntaxKind.IntKeyword => "System.Int32",
          SyntaxKind.UIntKeyword => "System.UInt32",
          SyntaxKind.LongKeyword => "System.Int64",
          SyntaxKind.ULongKeyword => "System.UInt64",
          SyntaxKind.ShortKeyword => "System.Int16",
          SyntaxKind.UShortKeyword => "System.UInt16",
          SyntaxKind.ByteKeyword => "System.Byte",
          SyntaxKind.SByteKeyword => "System.SByte",
          SyntaxKind.FloatKeyword => "System.Single",
          SyntaxKind.DoubleKeyword => "System.Double",
          SyntaxKind.BoolKeyword => "System.Boolean",
          SyntaxKind.CharKeyword => "System.Char",
          SyntaxKind.StringKeyword => "System.String",
          SyntaxKind.ObjectKeyword => "System.Object",
          _ => null
        };

        return mapped ?? throw EvalParser.Unsupported(type);
      }

      default: throw EvalParser.Unsupported(type);
    }
  }

  private static LiteralExpr TranslateLiteral(LiteralExpressionSyntax literal) {
    if (literal.IsKind(SyntaxKind.DefaultLiteralExpression)) {
      throw EvalParser.Unsupported(literal);
    }

    var value = literal.IsKind(SyntaxKind.NullLiteralExpression) ? null : literal!.Token.Value;

    if (value is decimal) {
      throw new EvalParseException(
        "unsupported: decimal literal (decimal is not expressible over SDB primitives)",
        literal.SpanStart
      );
    }

    return new LiteralExpr(value) {
      Position = literal.SpanStart
    };
  }

  private static EvalParseException Unsupported(SyntaxNode node) {
    return new EvalParseException(
      $"unsupported: {EvalParser.Describe(node.Kind())}",
      node.SpanStart
    );
  }

  /// <summary>
  /// Humanizes a syntax kind ("SimpleLambdaExpression" becomes "simple lambda expression").
  /// </summary>
  private static string Describe(SyntaxKind kind) {
    var name = kind.ToString();

    #pragma warning disable SYSLIB1045
    var words = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ").ToLowerInvariant();
    #pragma warning restore SYSLIB1045

    return words;
  }
}
