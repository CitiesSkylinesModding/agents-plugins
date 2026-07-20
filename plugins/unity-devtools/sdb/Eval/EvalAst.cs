namespace UnityDevtools.Sdb.Eval;

/// <summary>
/// The evaluator's own AST: the parser translates the supported subset of C# syntax into these
/// nodes, and the interpreter walks them against live mirrors.
/// Owning the AST keeps the supported grammar explicit (anything the parser cannot translate is
/// rejected up front) and keeps the interpreter free of Roslyn types.
/// Positions are character offsets into the evaluated source, for error reporting.
/// </summary>
public abstract record EvalNode {
  public required int Position { get; init; }
}

public abstract record EvalStatement : EvalNode {
  /// <summary>Length of the statement's source text, for quoting it in error reports.</summary>
  public required int Length { get; init; }
}

/// <summary>A <c>var name = value;</c> declaration.</summary>
public sealed record VarStatement(string Name, EvalExpr Value) : EvalStatement;

/// <summary>A bare expression statement; the last one's value is the program result.</summary>
public sealed record ExprStatement(EvalExpr Expression) : EvalStatement;

public abstract record EvalExpr : EvalNode;

/// <summary>A typed constant; null represents the <c>null</c> literal.</summary>
public sealed record LiteralExpr(object Value) : EvalExpr;

/// <summary>A bare identifier, resolved against the binding-scope chain.</summary>
public sealed record NameExpr(string Name) : EvalExpr;

/// <summary>
/// A dotted member access; the interpreter resolves the longest dotted prefix naming a live type
/// before falling back to instance/static member reads.
/// </summary>
public sealed record MemberExpr(EvalExpr Target, string Name) : EvalExpr;

/// <summary>
/// A method call; a null <paramref name="Target" /> is a bare call resolved against the scope
/// chain (e.g., the <c>entity(index, version)</c> builtin).
/// Type arguments are fully qualified type names, instantiated live via MakeGenericMethod.
/// </summary>
public sealed record CallExpr(
  EvalExpr Target,
  string Name,
  IReadOnlyList<string> TypeArgs,
  IReadOnlyList<ArgExpr> Args
) : EvalExpr;

/// <summary>One call argument; out arguments name a local (declared by <c>out var</c>).</summary>
public sealed record ArgExpr(EvalExpr Value, ArgMode Mode = ArgMode.Plain);

public enum ArgMode {
  Plain,

  /// <summary>An <c>out x</c> argument writing an existing local.</summary>
  Out,

  /// <summary>An <c>out var x</c> argument declaring the local it writes.</summary>
  OutVar
}

/// <summary>An indexer access, readable and assignable.</summary>
public sealed record IndexExpr(EvalExpr Target, IReadOnlyList<EvalExpr> Args) : EvalExpr;

/// <summary>
/// A <c>new T(args) { Field = value, ... }</c> construction with optional object initializers.
/// </summary>
public sealed record NewExpr(
  string TypeName,
  IReadOnlyList<ArgExpr> Args,
  IReadOnlyList<FieldInit> Initializers
) : EvalExpr;

/// <summary>One <c>Field = value</c> entry of an object initializer.</summary>
public sealed record FieldInit(string Name, EvalExpr Value);

public sealed record CastExpr(string TypeName, EvalExpr Operand) : EvalExpr;

/// <summary>A unary operation; <paramref name="Op" /> is the operator token text.</summary>
public sealed record UnaryExpr(string Op, EvalExpr Operand) : EvalExpr;

/// <summary>
/// A binary operation; <paramref name="Op" /> is the operator token text.
/// <c>&amp;&amp;</c>, <c>||</c>, and <c>??</c> evaluate their right side lazily.
/// </summary>
public sealed record BinaryExpr(string Op, EvalExpr Left, EvalExpr Right) : EvalExpr;

public sealed record ConditionalExpr(EvalExpr Condition, EvalExpr WhenTrue, EvalExpr WhenFalse)
  : EvalExpr;

/// <summary>
/// A <c>target?.member...</c> chain: when the target is null the whole expression is null,
/// otherwise <paramref name="WhenNotNull" /> runs with <see cref="ImplicitReceiverExpr" /> bound
/// to the target value.
/// </summary>
public sealed record ConditionalAccessExpr(EvalExpr Target, EvalExpr WhenNotNull) : EvalExpr;

/// <summary>The hole inside a conditional access, bound to the tested target value.</summary>
public sealed record ImplicitReceiverExpr : EvalExpr;

/// <summary>A <c>typeof(T)</c> expression yielding the live System.Type object.</summary>
public sealed record TypeofExpr(string TypeName) : EvalExpr;

/// <summary>
/// An interpolated string: parts are string literals and embedded expressions, concatenated after
/// formatting each value client-side.
/// </summary>
public sealed record InterpolatedStringExpr(IReadOnlyList<EvalExpr> Parts) : EvalExpr;

/// <summary>
/// A simple assignment (<c>=</c>) to a local, field, property, or indexer; evaluates to the
/// assigned value.
/// </summary>
public sealed record AssignExpr(EvalExpr Target, EvalExpr Value) : EvalExpr;

/// <summary>A parsed statement sequence, with the source kept for error reporting.</summary>
public sealed record EvalProgram(string Source, IReadOnlyList<EvalStatement> Statements);
