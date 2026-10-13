using System;
using UnityDevtools.Sdb.Eval;
using Xunit;

namespace UnityDevtools.Sdb.IntegrationTests;

/// <summary>
/// Core-operation semantics through the full walker against a live Mono debuggee: the historical
/// defect classes (equality, overload binding, coercion, struct-write persistence) lived here,
/// never in syntax breadth.
/// </summary>
[Collection(MonoDebuggeeCollection.Name)]
public sealed class EvalCoreOpTests(MonoDebuggeeFixture fx) {
  [SkippableFact]
  public void AddsIntegerLiterals() {
    var outcome = fx.Eval("1 + 2");

    Assert.Equal("3", outcome.Formatted);
    Assert.Equal("System.Int32", outcome.TypeName);
  }

  [SkippableFact]
  public void MirrorStringEqualsClientLiteralByValue() {
    var outcome = fx.Eval("TestFixture.Shapes.Greeting == \"hello\"");

    Assert.Equal("true", outcome.Formatted);
    Assert.Equal("System.Boolean", outcome.TypeName);
  }

  [SkippableFact]
  public void MirrorStringInequalityIsFalse() {
    Assert.Equal("false", fx.Eval("TestFixture.Shapes.Greeting == \"world\"").Formatted);
  }

  [SkippableFact]
  public void MirrorIntEqualsClientLiteral() {
    Assert.Equal("true", fx.Eval("TestFixture.Shapes.Answer == 42").Formatted);
  }

  [SkippableFact]
  public void NullMirrorEqualsNullLiteral() {
    Assert.Equal("true", fx.Eval("TestFixture.Shapes.NullText == null").Formatted);
  }

  [SkippableFact]
  public void CrossTypeNumericEqualityPromotes() {
    Assert.Equal("true", fx.Eval("TestFixture.Shapes.Answer == 42.0").Formatted);
  }

  [SkippableFact]
  public void OverloadBindsExactIntMatch() {
    Assert.Equal("\"int\"", fx.Eval("TestFixture.Overloads.Pick(1)").Formatted);
  }

  [SkippableFact]
  public void OverloadBindsExactLongMatchViaSuffix() {
    Assert.Equal("\"long\"", fx.Eval("TestFixture.Overloads.Pick(1L)").Formatted);
  }

  [SkippableFact]
  public void OverloadBindsFloatToDoubleOverObject() {
    Assert.Equal("\"double\"", fx.Eval("TestFixture.Overloads.Pick(1.5f)").Formatted);
  }

  [SkippableFact]
  public void OverloadBindsByteToCheapestWideningInt() {
    Assert.Equal("\"int\"", fx.Eval("TestFixture.Overloads.Pick((byte) 7)").Formatted);
  }

  [SkippableFact]
  public void GenericOverloadBindsTheDefinitionDeclaredSecond() {
    Assert.Equal("\"int:4\"", fx.Eval("TestFixture.Overloads.Tag<int>(3, 4)").Formatted);
  }

  [SkippableFact]
  public void GenericOverloadStillBindsTheDefinitionDeclaredFirst() {
    Assert.Equal(
      "\"holder\"",
      fx.Eval("TestFixture.Overloads.Tag<int>(new TestFixture.Holder(), 4)").Formatted
    );
  }

  [SkippableFact]
  public void GenericOverloadBindsPastASiblingItsConstraintRules() {
    Assert.Equal("\"class\"", fx.Eval("TestFixture.Overloads.Only<string>(3, \"x\")").Formatted);
  }

  // The agent reads an uninitialized type's statics as zeros, so a field access refuses until its
  // static constructor runs; a `const` has no storage to wait on.
  [SkippableFact]
  public void AStaticOfATypeNotYetInitializedIsRefused() {
    foreach (var code in new[] {
        "TestFixture.Unstarted.Value", "TestFixture.Unstarted.Value = 5"
      }) {
      var ex = Assert.Throws<EvalFailedException>(() => fx.Eval(code));

      Assert.Contains("TestFixture.Unstarted's static constructor has not run", ex.Message);
    }

    // The suggested call names a nested type as the parser reads it.
    var nested =
      Assert.Throws<EvalFailedException>(() => fx.Eval("TestFixture.Unstarted.Inner.Value"));

    Assert.Contains("typeof(TestFixture.Unstarted.Inner).TypeHandle", nested.Message);
    Assert.Equal("3", fx.Eval("TestFixture.Unstarted.Literal").Formatted);
    Assert.Equal(
      "7",
      fx.Eval(
          "System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(" +
          "typeof(TestFixture.Unstarted).TypeHandle); TestFixture.Unstarted.Value"
        )
        .Formatted
    );
  }

  [SkippableFact]
  public void GenericOverloadMissListsEveryDefinitionItTried() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("TestFixture.Overloads.Tag<int>(\"nope\", 4)")
    );

    Assert.Contains("Tag(Holder, Int32)", ex.Message);
    Assert.Contains("Tag(Int32, Int32)", ex.Message);
  }

  [SkippableFact]
  public void GenericOverloadMissNamesTheDefinitionItsConstraintRules() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("TestFixture.Overloads.Only<string>(new TestFixture.Holder(), \"x\")")
    );

    Assert.Contains("Only<T>(Holder, T): cannot be instantiated over <System.String>", ex.Message);
  }

  [SkippableFact]
  public void ATypeArgumentIsInferredFromAPrimitiveAStringAndAStructArgument() {
    Assert.Equal("\"Int32\"", fx.Eval("TestFixture.Infer.Name(1)").Formatted);
    Assert.Equal("\"String\"", fx.Eval("TestFixture.Infer.Name(\"x\")").Formatted);
    Assert.Equal(
      "\"Paw\"",
      fx.Eval("TestFixture.Infer.Name(new TestFixture.Miss.Paw())").Formatted
    );
  }

  [SkippableFact]
  public void ANonGenericExactOverloadBeatsTheInferredGenericOne() {
    Assert.Equal("\"int\"", fx.Eval("TestFixture.Infer.Pick(1)").Formatted);
  }

  [SkippableFact]
  public void TheInferredGenericOverloadBeatsAnObjectOne() {
    Assert.Equal(
      "\"generic\"",
      fx.Eval("TestFixture.Infer.Pick(new TestFixture.Holder())").Formatted
    );
  }

  // A literal null and one read from the game arrive as different values.
  [SkippableTheory]
  [InlineData("null")]
  [InlineData("TestFixture.Shapes.NullText")]
  public void ANullArgumentSkipsTheValueTypeOverloadForTheObjectOne(string arg) {
    Assert.Equal("\"object\"", fx.Eval($"TestFixture.Infer.Pick({arg})").Formatted);
  }

  [SkippableFact]
  public void ANullSkipsAGenericStructOverloadForTheObjectOne() {
    Assert.Equal("\"object\"", fx.Eval("TestFixture.Nulls.Pair(null)").Formatted);
  }

  [SkippableTheory]
  [InlineData("var s = new TestFixture.PairSlots(); s.Pair = null")]
  [InlineData("var s = new TestFixture.PairSlots(); s.Pairs[0] = null")]
  public void ANullWrittenIntoAGenericStructSlotIsRefused(string code) {
    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval(code));

    Assert.Contains("cannot pass null as System.Collections.Generic.KeyValuePair", ex.Message);
  }

  [SkippableFact]
  public void ANullPrefersTheStringOverloadToTheObjectOne() {
    Assert.Equal("\"string\"", fx.Eval("TestFixture.Nulls.Text(null)").Formatted);
  }

  [SkippableTheory]
  [InlineData("5", "\"5\"")]
  [InlineData("null", "\"null\"")]
  [InlineData("TestFixture.Nulls.Three()", "\"3\"")]
  public void ANullableParameterTakesItsUnderlyingValueAndNull(string arg, string expected) {
    Assert.Equal(expected, fx.Eval($"TestFixture.Nulls.Maybe({arg})").Formatted);
  }

  [SkippableTheory]
  [InlineData("TestFixture.Nulls.Order(5)", "\"int\"")]
  [InlineData("TestFixture.Nulls.Wide(5)", "\"long?\"")]
  [InlineData("TestFixture.Nulls.Most(null)", "\"derived\"")]
  public void OverloadsRankNullableAndNullArgumentsAsCSharpDoes(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  // The agent cannot read such an out value back, and a client-built placeholder for one
  // disconnects it: the call is refused, and the debuggee lives on.
  [SkippableTheory]
  [InlineData("TestFixture.Nulls.OutMaybe(out var value)", "System.Nullable")]
  [InlineData("TestFixture.Nulls.OutPair(out var pair)", "System.Collections.Generic.KeyValuePair")]
  public void AnOutSlotTheAgentCannotFillIsRefused(string code, string type) {
    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval(code));

    Assert.Contains($"cannot build a {type}", ex.Message);
    Assert.Equal("2", fx.Eval("1 + 1").Formatted);
  }

  [SkippableTheory]
  [InlineData("TestFixture.Nulls.Take(new TestFixture.Holds { X = 5 })", "5")]
  [InlineData(
    "TestFixture.Nulls.OutHolds(out var holds); holds.Maybe",
    "Nullable`1 { hasValue=True, value=3 }"
  )]
  public void
    AStructHoldingANullableAndAGenericStructBuildsClientSide(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableTheory]
  [InlineData("TestFixture.Nulls.OutPointer(out var pointer); pointer.ToInt64()", "42")]
  [InlineData("TestFixture.Nulls.TakePointer(new TestFixture.HoldsPointer { X = 3 })", "3")]
  public void APointerSlotBuildsClientSideAsAZeroPointer(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableTheory]
  [InlineData("TestFixture.Pointers.IsNull(null)")]
  [InlineData("TestFixture.Pointers.Address(new System.IntPtr(7).ToPointer())")]
  public void ARawPointerArgumentIsRefused(string code) {
    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval(code));

    Assert.Contains("cannot pass a pointer argument", ex.Message, StringComparison.Ordinal);
    Assert.Equal("2", fx.Eval("1 + 1").Formatted);
  }

  [SkippableTheory]
  [InlineData(
    "TestFixture.Pointers.Slot = TestFixture.Pointers.Make(7); TestFixture.Pointers.ReadSlot()",
    "7"
  )]
  [InlineData(
    "TestFixture.Pointers.Slots[0] = TestFixture.Pointers.Make(7); " +
    "TestFixture.Pointers.ReadSlots()",
    "7"
  )]
  [InlineData(
    "TestFixture.Pointers.Reset(); TestFixture.Pointers.Slot = null; " +
    "TestFixture.Pointers.ReadSlot()",
    "0"
  )]
  [InlineData(
    "TestFixture.Pointers.Reset(); TestFixture.Pointers.Slots[0] = null; " +
    "TestFixture.Pointers.ReadSlots()",
    "0"
  )]
  public void APointerWriteStoresItsValueAndANullAsZero(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableTheory]
  [InlineData("TestFixture.Hider.Make().M(5)")]
  [InlineData("TestFixture.Hider.Make().P(5)")]
  [InlineData("TestFixture.Hider.S(5)")]
  public void AnApplicableMethodOfTheDerivedTypeHidesTheBaseOnes(string code) {
    Assert.Equal("\"derived\"", fx.Eval(code).Formatted);
  }

  [SkippableFact]
  public void AnUnboxingCastTakesTheBoxedTypeOnly() {
    Assert.Equal("6", fx.Eval("(int) TestFixture.Infer.Boxed() + 1").Formatted);

    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval("(long) TestFixture.Infer.Boxed()"));

    Assert.Contains(
      "cannot unbox a System.Int32 as System.Int64",
      ex.Message,
      StringComparison.Ordinal
    );
  }

  // A box binds by its runtime type, as any argument does: C# would bind Pick(object) for both
  // and fix T to object, a documented divergence.
  [SkippableTheory]
  [InlineData("TestFixture.Infer.Name(TestFixture.Infer.Boxed())", "\"Int32\"")]
  [InlineData("TestFixture.Infer.Pick(TestFixture.Infer.Boxed())", "\"int\"")]
  [InlineData("TestFixture.Infer.Pick(TestFixture.Infer.BoxedPaw())", "\"generic\"")]
  public void ABoxedArgumentBindsByItsRuntimeType(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableTheory]
  [InlineData(
    "var h = new TestFixture.Holds { X = 2 }; h.Maybe = 5; h.Maybe",
    "Nullable`1 { hasValue=True, value=5 }"
  )]
  [InlineData(
    "var h = new TestFixture.Holds { X = 2 }; h.Maybe = 5; TestFixture.Nulls.Take(h)",
    "2"
  )]
  [InlineData(
    "var h = new TestFixture.Holds { X = 2 }; h.Maybe = null; TestFixture.Nulls.Take(h)",
    "2"
  )]
  [InlineData("TestFixture.Nulls.Take(new TestFixture.Holds { Maybe = 5, X = 2 })", "2")]
  public void ANullableFieldOfAClientBuiltStructTakesItsValue(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  // An override ranks with the method it overrides, yet still dispatches to the override; a
  // narrowing never decides which type's methods bind.
  [SkippableTheory]
  [InlineData("var w = new System.IO.StringWriter(); w.Write(65); w.ToString()", "\"65\"")]
  [InlineData("TestFixture.Overriding.Make().V(5)", "\"base-int\"")]
  [InlineData("TestFixture.Overriding.Make().V(5L)", "\"derived-long\"")]
  [InlineData("TestFixture.Overriding.Make().N(5)", "\"derived-byte\"")]
  // The documented narrowing divergence: a variable binds as a constant would, where C# binds the
  // base N(int).
  [InlineData("var x = 5; TestFixture.Overriding.Make().N(x)", "\"derived-byte\"")]
  [InlineData(
    "TestFixture.GenericLeaf.Make().Each(5, TestFixture.GenericBase.Ints())",
    "\"base-int\""
  )]
  [InlineData(
    "TestFixture.GenericLeaf.Make().All(5, TestFixture.GenericBase.IntList())",
    "\"base-int\""
  )]
  [InlineData("TestFixture.Reoverriding.Make().M(5)", "\"reoverriding\"")]
  [InlineData("TestFixture.GenericLeaf.Make().G(5)", "\"base-int\"")]
  [InlineData("TestFixture.Overriding.Make().K(5)", "\"base-int\"")]
  [InlineData("TestFixture.Overriding.Make().K(0)", "\"derived-enum\"")]
  [InlineData("TestFixture.IndexedDerived.Make()[5]", "\"derived-long\"")]
  [InlineData(
    "var d = TestFixture.IndexedDerived.Make(); d[5] = \"x\"; d.Written",
    "\"derived-long\""
  )]
  public void OverloadsAcrossAnInheritanceEdgeBindAsCSharpDoes(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableTheory]
  [InlineData("TestFixture.Infer.Compare(TestFixture.Infer.Boxed())", "\"comparable\"")]
  [InlineData("TestFixture.Infer.Value(TestFixture.Infer.Boxed())", "\"value\"")]
  [InlineData("TestFixture.Infer.Width(TestFixture.Infer.Boxed())", "\"long\"")]
  [InlineData("TestFixture.Narrowed.Kind(TestFixture.Infer.Boxed())", "\"enum:5\"")]
  public void ABoxedPrimitiveBindsLikeItsValue(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  // The evaluator admits an in-range narrowing for any integral value, as C# does for a constant,
  // and prices it below boxing and the numeric-to-enum convenience; the convenience binds last,
  // save for a zero, which C# converts to any enum.
  [SkippableTheory]
  [InlineData("TestFixture.Narrowed.M(5)", "\"byte\"")]
  [InlineData("TestFixture.Narrowed.E(5)", "\"byte\"")]
  [InlineData("TestFixture.Narrowed.Pick(5)", "\"object\"")]
  [InlineData("TestFixture.Narrowed.Pick(0)", "\"enum\"")]
  public void AnInRangeNarrowingOutranksBoxingAndTheEnumConvenience(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  // A box converts as its own type does: exactly or by widening, never by a narrowing past the
  // object slot it fits.
  [SkippableTheory]
  [InlineData("TestFixture.Narrowed.M(TestFixture.Infer.Boxed())", "\"object\"")]
  [InlineData("TestFixture.Narrowed.Pick(TestFixture.Infer.Boxed())", "\"object\"")]
  public void ABoxedPrimitiveKeepsItsOwnTypesConversions(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableTheory]
  [InlineData(
    "TestFixture.Narrowed.Kind(2.5)",
    "cannot pass System.Double as enum TestFixture.Small"
  )]
  [InlineData(
    "TestFixture.Narrowed.Kind(TestFixture.Infer.BoxedFraction())",
    "cannot pass System.Double as enum TestFixture.Small"
  )]
  [InlineData("TestFixture.Narrowed.Fits(300)", "300 does not fit TestFixture.Narrow")]
  [InlineData("TestFixture.Narrowed.Fits(-1)", "-1 does not fit TestFixture.Narrow")]
  [InlineData(
    "TestFixture.Narrowed.Kind('a')",
    "cannot pass System.Char as enum TestFixture.Small"
  )]
  public void TheEnumConvenienceRefusesWhatItWouldTruncate(string code, string expected) {
    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval(code));

    Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
  }

  // An operator reads a nullable as its value or null, as C# lifts it; member access still sees
  // the wrapper, so `.HasValue` and `.Value` work.
  [SkippableTheory]
  [InlineData("new TestFixture.Holds().Maybe == null", "true")]
  [InlineData("var h = new TestFixture.Holds { X = 2 }; h.Maybe ?? 7", "7")]
  [InlineData("var h = new TestFixture.Holds { Maybe = 5 }; h.Maybe + 1", "6")]
  [InlineData("var h = new TestFixture.Holds { Maybe = 5 }; h.Maybe = null; h.Maybe ?? 9", "9")]
  [InlineData("var h = new TestFixture.Holds { Maybe = 5 }; -h.Maybe", "-5")]
  [InlineData("var h = new TestFixture.Holds { Maybe = 5 }; h.Maybe.HasValue", "True")]
  [InlineData("TestFixture.NullableReads.None == null", "true")]
  [InlineData("TestFixture.NullableReads.Five > 3", "true")]
  [InlineData("\"v=\" + TestFixture.NullableReads.Five", "\"v=5\"")]
  public void AnOperatorReadsANullableAsItsValueOrNull(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  // A conflicting inference refuses loudly rather than fall through to a binding C# may not make:
  // a sibling taking an argument by conversion, or a farther tier's overload the generic hides.
  [SkippableTheory]
  [InlineData(
    "TestFixture.Sibling.Same(1, 2L)",
    "T fixed to Int32 by argument 1 and Int64 by argument 2"
  )]
  [InlineData(
    "new TestFixture.TierDerived().M(1, 2L)",
    "T fixed to Int32 by argument 1 and Int64 by argument 2"
  )]
  [InlineData("TestFixture.Sibling.Same(\"a\", null)", "T cannot be inferred (argument 2 is null)")]
  [InlineData(
    "TestFixture.Sibling.Loose(\"x\", null)",
    "T cannot be inferred (argument 2 is null)"
  )]
  [InlineData(
    "TestFixture.Sibling.Two(new TestFixture.RefBase(), null, new TestFixture.RefMid())",
    "T cannot be inferred (argument 2 is null)"
  )]
  [InlineData(
    "TestFixture.Sibling.Cmp(TestFixture.Sibling.BoxedInt(), null)",
    "T cannot be inferred (argument 2 is null)"
  )]
  [InlineData(
    "TestFixture.Sibling.Same(1, \"a\")",
    "T fixed to Int32 by argument 1 and String by argument 2"
  )]
  [InlineData(
    "TestFixture.Sibling.Tri(1, 2u, 3L)",
    "T fixed to Int32 by argument 1 and UInt32 by argument 2"
  )]
  [InlineData(
    "TestFixture.Sibling.Cov(TestFixture.Sibling.Strings(), TestFixture.Sibling.Objects())",
    "T fixed to String[] by argument 1 and Object[] by argument 2"
  )]
  public void AConflictingInferenceFailsLoudly(string code, string expected) {
    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval(code));

    Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
  }

  [SkippableTheory]
  [InlineData("TestFixture.Sibling.Same(1, 2)", "\"generic\"")]
  [InlineData("TestFixture.Sibling.Same(null, null)", "\"object\"")]
  [InlineData("new TestFixture.Miss.Dog().Echo(1)", "1")]
  public void AnInferableCallBindsTheGenericAndAnUninferableOneItsSibling(
    string code,
    string expected
  ) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableTheory]
  [InlineData("TestFixture.Refs.M(new TestFixture.RefLeaf())", "\"mid\"")]
  [InlineData("TestFixture.Nulls.Narrowest(null)", "\"int?\"")]
  [InlineData("TestFixture.Nulls.Wide(null)", "\"long?\"")]
  [InlineData("TestFixture.Nulls.Boxy(null)", "\"int?\"")]
  [InlineData("TestFixture.Nulls.Cmp(null)", "\"bool?\"")]
  [InlineData("TestFixture.Nulls.Three(null)", "\"Triple?\"")]
  [InlineData("TestFixture.Narrowed.En(TestFixture.NullableReads.SomeSmall)", "\"Small?\"")]
  public void AnArgumentBindsTheMostSpecificSlotWhateverTheDeclarationOrder(
    string code,
    string expected
  ) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  // A box the client built reads like one the debuggee returned, and unboxes to its own type, an
  // enum over it included.
  [SkippableTheory]
  [InlineData("TestFixture.Infer.Pick(new TestFixture.Boxed { Value = 5 }.Value)", "\"int\"")]
  [InlineData("(int) new TestFixture.Boxed { Value = 5 }.Value", "5")]
  [InlineData("(int) (TestFixture.Small) TestFixture.Infer.Boxed()", "5")]
  public void ABoxUnboxesToItsOwnType(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableTheory]
  [InlineData(
    "(long) new TestFixture.Boxed { Value = 5 }.Value",
    "cannot unbox a System.Int32 as System.Int64"
  )]
  [InlineData(
    "(TestFixture.Small) TestFixture.Infer.BoxedFraction()",
    "cannot unbox a System.Double as TestFixture.Small"
  )]
  public void AnUnboxingCastToAnotherTypeIsRefused(string code, string expected) {
    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval(code));

    Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
  }

  // A char has no conversion to an enum, so only the slots C# admits it into bind.
  [SkippableTheory]
  [InlineData("TestFixture.Narrowed.Pick('a')", "\"object\"")]
  [InlineData("TestFixture.Narrowed.Code('a')", "\"int\"")]
  public void ACharTakesNoEnumConvenience(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  // The numeric-to-enum convenience yields to any other binding, a costlier sibling's in the
  // same type and an ancestor's alike.
  [SkippableTheory]
  [InlineData("TestFixture.Narrowed.Two(5, 3)", "\"object\"")]
  [InlineData("TestFixture.Overriding.Make().Q(5)", "\"base-int\"")]
  public void TheEnumConvenienceYieldsToAnyOtherBinding(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableTheory]
  [InlineData("TestFixture.Refs.Ifc(TestFixture.Refs.Ints())", "\"ilist\"")]
  [InlineData(
    "TestFixture.Refs.Wide(new TestFixture.DeepLeaf(), new TestFixture.DeepLeaf(), 5)",
    "\"iface-int\""
  )]
  public void AReferenceSlotsSpecificityOnlyBreaksTies(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  // A sibling C# prefers over any instantiation binds over a refused generic: one in a nearer
  // tier, which hides it, or one in its own tier taking every argument as is, a null at a slot
  // no less specific than the generic one's.
  [SkippableTheory]
  [InlineData("TestFixture.Sibling.Pair(\"a\", null)", "\"nongeneric\"")]
  [InlineData("TestFixture.Sibling.Exact(1, 2L)", "\"exact\"")]
  [InlineData("TestFixture.Sibling.Sub(new TestFixture.RefBase(), null)", "\"sibling\"")]
  [InlineData("new TestFixture.WideDerived().M(1, 2L)", "\"derived-wide\"")]
  [InlineData("TestFixture.Sibling.ValueNull(1, null)", "\"sibling\"")]
  [InlineData("TestFixture.Sibling.Named(\"x\", null, null)", "\"sibling\"")]
  [InlineData("TestFixture.Sibling.Same(1, null)", "\"object\"")]
  [InlineData("TestFixture.Sibling.Multi(\"x\", null, null)", "\"sibling\"")]
  public void ASiblingCSharpPrefersBindsOverARefusedGeneric(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableTheory]
  [InlineData("var h = new TestFixture.Holds { Maybe = 5 }; (int) h.Maybe", "5")]
  [InlineData("var h = new TestFixture.Holds { Maybe = 5 }; $\"[{h.Maybe}]\"", "\"[5]\"")]
  [InlineData("var h = new TestFixture.Holds(); $\"[{h.Maybe}]\"", "\"[]\"")]
  [InlineData(
    "var h = new TestFixture.Holds(); h.Maybe?.ToString() ?? \"was-null\"",
    "\"was-null\""
  )]
  public void ACastInterpolationOrConditionalAccessReadsANullableAsItsValue(
    string code,
    string expected
  ) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableTheory]
  [InlineData(
    "var a = new TestFixture.Boxed { Value = 5 }; var b = new TestFixture.Boxed { Value = 5 }; a.Value == b.Value",
    "false"
  )]
  [InlineData("var a = new TestFixture.Boxed { Value = 5 }; a.Value == a.Value", "true")]
  public void BoxesCompareByReference(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableFact]
  public void ABoxTheDebuggeeReturnedRefusesAReferenceComparison() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("TestFixture.Infer.Boxed() == TestFixture.Infer.Boxed()")
    );

    Assert.Contains(
      "cannot compare a boxed value by reference",
      ex.Message,
      StringComparison.Ordinal
    );
  }

  // A struct stored anywhere is a copy, as C# copies a struct on assignment.
  [SkippableTheory]
  [InlineData("var p = new TestFixture.Point(); var q = p; q.X = 5; p.X", "0")]
  [InlineData(
    "var p = new TestFixture.Point(); var q = new TestFixture.Point(); q = p; q.X = 5; p.X",
    "0"
  )]
  [InlineData(
    "var p = new TestFixture.Point(); var h = new TestFixture.TwoPoints(); h.A = p; p.X = 5; h.A.X",
    "0"
  )]
  [InlineData(
    "var p = new TestFixture.Point(); var h = new TestFixture.TwoPoints(); h.A = p; h.B = p; " +
    "h.A.X = 7; h.B.X",
    "0"
  )]
  [InlineData(
    "var p = new TestFixture.Point(); var h = new TestFixture.PointHolder(); h.Maybe = p; " +
    "p.X = 5; h.Maybe.Value.X",
    "0"
  )]
  [InlineData(
    "var p = new TestFixture.Point { X = 3 }; var h = new TestFixture.TwoPoints(); h.A = p; " +
    "h.B = p; TestFixture.TwoPoints.Sum(h)",
    "6"
  )]
  public void AStructStoredAnywhereIsACopy(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  // A call on a struct temporary runs on a copy, as in C#; a parenthesised variable is no
  // temporary.
  [SkippableTheory]
  [InlineData(
    "var p = new TestFixture.Mutable(); var q = new TestFixture.Mutable(); (q = p).Bump(); q.X",
    "0"
  )]
  [InlineData("var p = new TestFixture.Mutable(); (true ? p : p).Bump(); p.X", "0")]
  [InlineData("var p = new TestFixture.Mutable(); ((TestFixture.Mutable) p).Bump(); p.X", "0")]
  [InlineData("var p = new TestFixture.Mutable(); (p).Bump(); p.X", "1")]
  public void ACallOnAStructTemporaryRunsOnACopy(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableFact]
  public void CastingAnEmptyNullableToItsStructIsRefused() {
    var ex = Assert.Throws<EvalFailedException>(() =>
      fx.Eval("(TestFixture.Point) new TestFixture.PointHolder().Maybe")
    );

    Assert.Contains("cannot cast null to TestFixture.Point", ex.Message, StringComparison.Ordinal);
  }

  [SkippableFact]
  public void AnIntPtrArgumentArrivesIntact() {
    Assert.Equal("7", fx.Eval("TestFixture.Nulls.ReadPointer(new System.IntPtr(7))").Formatted);
  }

  [SkippableFact]
  public void AnOutEnumNestedInAGenericTypeReadsBackItsValue() {
    Assert.Equal("Kind.B", fx.Eval("TestFixture.Nulls.OutKind(out var kind); kind").Formatted);
  }

  [SkippableFact]
  public void APrimitiveConstructedWithoutArgumentsIsItsDefault() {
    Assert.Equal("0", fx.Eval("new System.Int32()").Formatted);
  }

  [SkippableFact]
  public void AClientBoxedPrimitiveFormatsAsItsValue() {
    Assert.Equal("5", fx.Eval("new TestFixture.Boxed { Value = 5 }.Value").Formatted);
  }

  [SkippableFact]
  public void AnOutArgumentLeavesTheTypeArgumentToThePlainOne() {
    Assert.Equal("\"Int32\"", fx.Eval("TestFixture.Infer.Both(1, out var copy)").Formatted);
  }

  [SkippableFact]
  public void ATypeArgumentTwoArgumentsDisagreeOnIsRefusedNamingBoth() {
    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval("TestFixture.Infer.Same(1, 2L)"));

    Assert.Contains(
      "Same is generic and T fixed to Int32 by argument 1 and Int64 by argument 2: write it, " +
      "as in static Same<T>(T, T)",
      ex.Message
    );
  }

  [SkippableTheory]
  [InlineData("TestFixture.Overloads.Pick(true)", "\"object\"")]
  [InlineData("var o = 7; System.String.Concat(o, \"x\")", "\"7x\"")]
  [InlineData("System.String.Concat(7, 8)", "\"78\"")]
  public void PrimitiveArgumentBoxesForAnObjectParameter(string code, string expected) {
    Assert.Equal(expected, fx.Eval(code).Formatted);
  }

  [SkippableTheory]
  [InlineData("\"pin\"")]
  [InlineData("new TestFixture.Point()")]
  [InlineData("7")]
  public void EnumMemberBindsBesideAnObjectParameter(string pinned) {
    var outcome = fx.Eval(
      $"var o = {pinned}; " +
      "var h = System.Runtime.InteropServices.GCHandle.Alloc(" +
      "o, System.Runtime.InteropServices.GCHandleType.Pinned); " +
      "var allocated = h.IsAllocated; h.Free(); allocated"
    );

    Assert.Equal("True", outcome.Formatted);
  }

  [SkippableFact]
  public void IntArgumentWidensToLongParameter() {
    var outcome = fx.Eval("TestFixture.Overloads.TakesLong(42)");

    Assert.Equal("42", outcome.Formatted);
    Assert.Equal("System.Int64", outcome.TypeName);
  }

  [SkippableFact]
  public void StructWriteThroughObjectFieldPersists() {
    // Persistence asserted by a follow-up read, the same way an agent would verify it.
    var outcome = fx.Eval("var h = new TestFixture.Holder(); h.P.X = 5; h.P.X");

    Assert.Equal("5", outcome.Formatted);
    Assert.Equal("System.Int32", outcome.TypeName);
  }

  // A call through a holder typed object or as an interface, or a member write through one typed
  // as an interface, stores the changed struct back as a new box; a read-only call keeps the box.
  [SkippableTheory]
  [InlineData("TestFixture.Boxes.Ifc.Bump(); ((TestFixture.BumpZ) TestFixture.Boxes.Ifc).X", "1")]
  [InlineData("TestFixture.Boxes.Ifc.P = 7; TestFixture.Boxes.Ifc.P", "7")]
  [InlineData("TestFixture.Boxes.Ifc?.Bump(); TestFixture.Boxes.Ifc.P", "1")]
  [InlineData(
    "TestFixture.Boxes.Ifc.Negate(); 1 / ((TestFixture.BumpZ) TestFixture.Boxes.Ifc).F < 0",
    "true"
  )]
  [InlineData(
    "TestFixture.Boxes.Obj.ToString(); ((TestFixture.Sneaky) TestFixture.Boxes.Obj).X",
    "1"
  )]
  [InlineData("TestFixture.Boxes.Ifc.GetHashCode(); TestFixture.Boxes.SameIfc()", "True")]
  [InlineData("TestFixture.Boxes.Arr[0].GetHashCode(); TestFixture.Boxes.SameArr()", "True")]
  public void ACallThroughABoxedHolderStoresOnlyAChange(string code, string expected) {
    Assert.Equal(expected, fx.Eval($"TestFixture.Boxes.Reset(); {code}").Formatted);
  }

  [SkippableTheory]
  [InlineData("TestFixture.Boxes.Arr[0].X = 5", "'element 0' holds a boxed struct")]
  [InlineData("TestFixture.Boxes.Held.O.X = 5", "'O' holds a boxed struct")]
  public void AMemberWriteIntoABoxTypedObjectIsRefused(string code, string expected) {
    var ex = Assert.Throws<EvalFailedException>(() => fx.Eval($"TestFixture.Boxes.Reset(); {code}")
    );

    Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
  }

  [SkippableFact]
  public void StructWriteThroughStaticFieldPersists() {
    var outcome = fx.Eval(
      "TestFixture.Frames.InTarget.X = 7; var x = TestFixture.Frames.InTarget.X; " +
      "TestFixture.Frames.InTarget.X = 0; x"
    );

    Assert.Equal("7", outcome.Formatted);
  }
}
