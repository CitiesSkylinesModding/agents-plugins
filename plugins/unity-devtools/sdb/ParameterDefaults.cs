using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Mono.Debugger.Soft;

namespace UnityDevtools.Sdb;

/// <summary>
/// What a method's parameters declare, read off its assembly's own metadata: their defaults, and
/// how each is passed.
/// The agent's parameter info carries no flags and no constant, and reports a by-reference
/// parameter's type by value, so the assembly image is fetched whole, once per assembly and
/// attach, and decoded client-side, costing the game no invoke.
/// Metadata is immutable while the assembly is loaded, so the image is held for the attach.
/// An assembly whose image cannot be fetched or parsed, a dynamic one included, is held as
/// unreadable for the attach too: what the agent refuses to serve is a property of the assembly.
/// A dead connection or an unanswered command describes the moment and propagates instead.
/// </summary>
public sealed class ParameterDefaults {
  private const string DecimalConstant = "System.Runtime.CompilerServices.DecimalConstantAttribute";

  private const string DateTimeConstant =
    "System.Runtime.CompilerServices.DateTimeConstantAttribute";

  // A null value marks an assembly whose image could not be read.
  private readonly ConcurrentDictionary<AssemblyMirror, PEReader> images = new();

  /// <summary>
  /// What each parameter of a method declares, in position order, or null when its assembly's
  /// metadata cannot be read.
  /// </summary>
  public DeclaredDefault[] Of(MethodMirror method) {
    if (this.DefinitionOrNull(method) is not var (reader, definition)) {
      return null;
    }

    var declared = new DeclaredDefault[method.GetParameters().Length];

    // A parameter with no Param row declares nothing, which leaves it required.
    foreach (var parameterHandle in definition.GetParameters()) {
      var parameter = reader.GetParameter(parameterHandle);
      var position = parameter.SequenceNumber - 1;

      // Sequence number 0 is the return value.
      if (position < 0 || position >= declared.Length) {
        continue;
      }

      declared[position] = ParameterDefaults.Declared(reader, parameter);
    }

    return declared;
  }

  /// <summary>
  /// How C# declares each of a method's parameters passed, in position order.
  /// Null when its assembly's metadata cannot be read.
  /// </summary>
  public ParameterMode[] Modes(MethodMirror method) {
    if (this.DefinitionOrNull(method) is not var (reader, definition)) {
      return null;
    }

    var modes = definition.DecodeSignature(ByReferenceProvider.Instance, null)
      .ParameterTypes
      .Select(static isByReference => isByReference ? ParameterMode.Ref : ParameterMode.Plain)
      .ToArray();

    // A by-reference parameter flagged Out alone is `out`, flagged In alone `in`; both flags, as
    // interop writes `[In, Out] ref`, leave it `ref`.
    foreach (var parameterHandle in definition.GetParameters()) {
      var parameter = reader.GetParameter(parameterHandle);
      var position = parameter.SequenceNumber - 1;

      if (position < 0 || position >= modes.Length || modes[position] is not ParameterMode.Ref) {
        continue;
      }

      modes[position] = (parameter.Attributes & (ParameterAttributes.Out | ParameterAttributes.In))
        switch {
          ParameterAttributes.Out => ParameterMode.Out,
          ParameterAttributes.In => ParameterMode.In,
          _ => ParameterMode.Ref
        };
    }

    return modes;
  }

  /// <summary>
  /// The method's definition row and its reader, or null when its assembly's metadata cannot be
  /// read.
  /// A generic instantiation answers its definition's, whose token it carries.
  /// </summary>
  private (MetadataReader Reader, MethodDefinition Definition)? DefinitionOrNull(
    MethodMirror method
  ) {
    var reader = this.ReaderOrNull(method.DeclaringType.Assembly);

    var handle = MetadataTokens.EntityHandle(method.MetadataToken);

    if (reader is null || handle.Kind is not HandleKind.MethodDefinition) {
      return null;
    }

    return (reader, reader.GetMethodDefinition((MethodDefinitionHandle) handle));
  }

  /// <summary>
  /// A parameter as C# reads it: optional only through the Optional flag, whatever else it
  /// carries, its default in the Constant table, or, for the two kinds no constant can encode, in
  /// an attribute.
  /// </summary>
  private static DeclaredDefault Declared(MetadataReader reader, Parameter parameter) {
    if ((parameter.Attributes & ParameterAttributes.Optional) is 0) {
      return new DeclaredDefault(DefaultKind.Required, null);
    }

    if (parameter.GetDefaultValue() is { IsNil: false } constantHandle) {
      var constant = reader.GetConstant(constantHandle);
      var value = reader.GetBlobReader(constant.Value).ReadConstant(constant.TypeCode);

      return new DeclaredDefault(DefaultKind.Constant, value);
    }

    foreach (var attributeHandle in parameter.GetCustomAttributes()) {
      var attributeType = ParameterDefaults.AttributeTypeName(
        reader,
        reader.GetCustomAttribute(attributeHandle)
      );

      switch (attributeType) {
        case ParameterDefaults.DecimalConstant:
          return new DeclaredDefault(DefaultKind.DecimalConstant, null);

        case ParameterDefaults.DateTimeConstant:
          return new DeclaredDefault(DefaultKind.DateTimeConstant, null);
      }
    }

    return new DeclaredDefault(DefaultKind.Optional, null);
  }

  private static string AttributeTypeName(MetadataReader reader, CustomAttribute attribute) {
    var type = attribute.Constructor.Kind switch {
      HandleKind.MemberReference =>
        reader.GetMemberReference((MemberReferenceHandle) attribute.Constructor).Parent,
      HandleKind.MethodDefinition =>
        reader.GetMethodDefinition((MethodDefinitionHandle) attribute.Constructor)
          .GetDeclaringType(),
      _ => default
    };

    return type.Kind switch {
      HandleKind.TypeReference => ParameterDefaults.FullName(
        reader,
        reader.GetTypeReference((TypeReferenceHandle) type).Namespace,
        reader.GetTypeReference((TypeReferenceHandle) type).Name
      ),
      HandleKind.TypeDefinition => ParameterDefaults.FullName(
        reader,
        reader.GetTypeDefinition((TypeDefinitionHandle) type).Namespace,
        reader.GetTypeDefinition((TypeDefinitionHandle) type).Name
      ),
      _ => null
    };
  }

  private static string FullName(
    MetadataReader reader,
    StringHandle typeNamespace,
    StringHandle name
  ) =>
    $"{reader.GetString(typeNamespace)}.{reader.GetString(name)}";

  private MetadataReader ReaderOrNull(AssemblyMirror assembly) {
    // The reader points into the image, which the dictionary keeps alive.
    return this.images.GetOrAdd(assembly, ParameterDefaults.Load)?.GetMetadataReader();
  }

  /// <summary>
  /// The assembly's image, or null when it cannot be fetched or parsed.
  /// The agent answers with the whole PE image, not the metadata section alone.
  /// </summary>
  private static PEReader Load(AssemblyMirror assembly) {
    try {
      // The mirror already holds the bytes for the attach, so the reader shares them rather than
      // keeping a copy of an image that can run to megabytes.
      var image = new PEReader(
        ImmutableCollectionsMarshal.AsImmutableArray(assembly.GetMetadataBlob())
      );

      // Parsed here rather than on first use, so a malformed image is told apart now.
      image.GetMetadataReader();

      return image;
    }
    catch (Exception ex) when (ex is NotSupportedException or
      CommandException or
      AbsentInformationException or
      BadImageFormatException or
      InvalidOperationException) {
      return null;
    }
  }
}

/// <summary>
/// Decodes a signature down to whether each type is passed by reference, ignoring the rest; a
/// modifier, such as the one marking an `in` parameter, keeps the type it modifies.
/// </summary>
internal sealed class ByReferenceProvider : ISignatureTypeProvider<bool, object> {
  public static readonly ByReferenceProvider Instance = new();

  public bool GetByReferenceType(bool elementType) => true;

  public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) =>
    unmodifiedType;

  public bool GetArrayType(bool elementType, ArrayShape shape) => false;

  public bool GetFunctionPointerType(MethodSignature<bool> signature) => false;

  public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments) =>
    false;

  public bool GetGenericMethodParameter(object genericContext, int index) => false;

  public bool GetGenericTypeParameter(object genericContext, int index) => false;

  public bool GetPinnedType(bool elementType) => false;

  public bool GetPointerType(bool elementType) => false;

  public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => false;

  public bool GetSZArrayType(bool elementType) => false;

  public bool GetTypeFromDefinition(
    MetadataReader reader,
    TypeDefinitionHandle handle,
    byte rawTypeKind
  ) =>
    false;

  public bool GetTypeFromReference(
    MetadataReader reader,
    TypeReferenceHandle handle,
    byte rawTypeKind
  ) =>
    false;

  public bool GetTypeFromSpecification(
    MetadataReader reader,
    object genericContext,
    TypeSpecificationHandle handle,
    byte rawTypeKind
  ) =>
    false;
}

/// <summary>How a parameter is passed, as C# declares it.</summary>
public enum ParameterMode {
  Plain,

  Ref,

  Out,

  In
}

/// <summary>What a parameter declares about being omitted.</summary>
/// <param name="Constant">
/// The Constant-table value for <see cref="DefaultKind.Constant" />: a primitive, a string, or
/// null, an enum's arriving as its underlying number.
/// </param>
public readonly record struct DeclaredDefault(DefaultKind Kind, object Constant);

public enum DefaultKind {
  /// <summary>A call must pass it.</summary>
  Required,

  /// <summary>Omitted, it takes the Constant-table value.</summary>
  Constant,

  /// <summary>
  /// Optional with no constant and no attribute naming one: C# passes `default(T)`, or
  /// `Type.Missing` for an object.
  /// </summary>
  Optional,

  /// <summary>A `decimal` default, carried by a <c>DecimalConstantAttribute</c>.</summary>
  DecimalConstant,

  /// <summary>A `DateTime` default carried by a <c>DateTimeConstantAttribute</c>.</summary>
  DateTimeConstant
}
