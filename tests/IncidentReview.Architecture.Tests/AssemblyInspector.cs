using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace IncidentReview.Architecture.Tests;

internal static class AssemblyInspector
{
    private const string InternalsVisibleToAttributeName =
        "System.Runtime.CompilerServices.InternalsVisibleToAttribute";

    public static AssemblyInspection Inspect(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            throw new InvalidDataException($"'{assemblyPath}' is not a managed assembly.");
        }

        var reader = peReader.GetMetadataReader();
        if (!reader.IsAssembly)
        {
            throw new InvalidDataException($"'{assemblyPath}' is a managed module, not an assembly.");
        }

        var assemblyName = reader.GetString(reader.GetAssemblyDefinition().Name);
        var provider = new DependencySignatureProvider(reader, assemblyName);

        var assemblyReferences = reader.AssemblyReferences
            .Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name))
            .ToHashSet(StringComparer.Ordinal);

        var publicTypes = reader.TypeDefinitions
            .Where(handle => IsExternallyVisible(reader, handle))
            .Select(handle => InspectPublicType(reader, provider, handle))
            .ToArray();

        var internalsVisibleTo = reader.GetAssemblyDefinition()
            .GetCustomAttributes()
            .Select(handle => GetAttributeType(reader, provider, handle))
            .Any(type => string.Equals(
                type?.FullName,
                InternalsVisibleToAttributeName,
                StringComparison.Ordinal));

        return new AssemblyInspection(
            assemblyName,
            assemblyReferences,
            publicTypes,
            internalsVisibleTo);
    }

    private static PublicTypeInspection InspectPublicType(
        MetadataReader reader,
        DependencySignatureProvider provider,
        TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var @namespace = GetEffectiveNamespace(reader, handle);
        var name = reader.GetString(definition.Name);
        var dependencies = ImmutableHashSet<ReferencedType>.Empty;

        dependencies = dependencies.Union(GetEntityDependencies(provider, definition.BaseType));
        dependencies = dependencies.Union(GetAttributeDependencies(
            reader,
            provider,
            definition.GetCustomAttributes()));
        dependencies = dependencies.Union(GetGenericParameterDependencies(
            reader,
            provider,
            definition.GetGenericParameters()));

        foreach (var interfaceHandle in definition.GetInterfaceImplementations())
        {
            var implementation = reader.GetInterfaceImplementation(interfaceHandle);
            dependencies = dependencies.Union(
                GetEntityDependencies(provider, implementation.Interface));
            dependencies = dependencies.Union(GetAttributeDependencies(
                reader,
                provider,
                implementation.GetCustomAttributes()));
        }

        foreach (var fieldHandle in definition.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if (!IsExternallyVisible(field.Attributes))
            {
                continue;
            }

            dependencies = dependencies.Union(field.DecodeSignature(provider, genericContext: null));
            dependencies = dependencies.Union(GetAttributeDependencies(
                reader,
                provider,
                field.GetCustomAttributes()));
        }

        foreach (var methodHandle in definition.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (!IsExternallyVisible(method.Attributes))
            {
                continue;
            }

            var signature = method.DecodeSignature(provider, genericContext: null);
            dependencies = dependencies.Union(signature.ReturnType);
            foreach (var parameterType in signature.ParameterTypes)
            {
                dependencies = dependencies.Union(parameterType);
            }

            dependencies = dependencies.Union(GetAttributeDependencies(
                reader,
                provider,
                method.GetCustomAttributes()));
            dependencies = dependencies.Union(GetGenericParameterDependencies(
                reader,
                provider,
                method.GetGenericParameters()));

            foreach (var parameterHandle in method.GetParameters())
            {
                dependencies = dependencies.Union(GetAttributeDependencies(
                    reader,
                    provider,
                    reader.GetParameter(parameterHandle).GetCustomAttributes()));
            }
        }

        foreach (var propertyHandle in definition.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            if (!HasExternallyVisibleAccessor(reader, property.GetAccessors()))
            {
                continue;
            }

            var signature = property.DecodeSignature(provider, genericContext: null);
            dependencies = dependencies.Union(signature.ReturnType);
            foreach (var parameterType in signature.ParameterTypes)
            {
                dependencies = dependencies.Union(parameterType);
            }

            dependencies = dependencies.Union(GetAttributeDependencies(
                reader,
                provider,
                property.GetCustomAttributes()));
        }

        foreach (var eventHandle in definition.GetEvents())
        {
            var @event = reader.GetEventDefinition(eventHandle);
            if (!HasExternallyVisibleAccessor(reader, @event.GetAccessors()))
            {
                continue;
            }

            dependencies = dependencies.Union(
                GetEntityDependencies(provider, @event.Type));
            dependencies = dependencies.Union(GetAttributeDependencies(
                reader,
                provider,
                @event.GetCustomAttributes()));
        }

        return new PublicTypeInspection(@namespace, name, dependencies);
    }

    private static ImmutableHashSet<ReferencedType> GetGenericParameterDependencies(
        MetadataReader reader,
        DependencySignatureProvider provider,
        GenericParameterHandleCollection parameters)
    {
        var dependencies = ImmutableHashSet<ReferencedType>.Empty;

        foreach (var parameterHandle in parameters)
        {
            var parameter = reader.GetGenericParameter(parameterHandle);
            dependencies = dependencies.Union(GetAttributeDependencies(
                reader,
                provider,
                parameter.GetCustomAttributes()));

            foreach (var constraintHandle in parameter.GetConstraints())
            {
                var constraint = reader.GetGenericParameterConstraint(constraintHandle);
                dependencies = dependencies.Union(
                    GetEntityDependencies(provider, constraint.Type));
                dependencies = dependencies.Union(GetAttributeDependencies(
                    reader,
                    provider,
                    constraint.GetCustomAttributes()));
            }
        }

        return dependencies;
    }

    private static ImmutableHashSet<ReferencedType> GetAttributeDependencies(
        MetadataReader reader,
        DependencySignatureProvider provider,
        CustomAttributeHandleCollection attributes)
    {
        var dependencies = ImmutableHashSet<ReferencedType>.Empty;

        foreach (var attributeHandle in attributes)
        {
            var attributeType = GetAttributeType(reader, provider, attributeHandle);
            if (attributeType is not null)
            {
                dependencies = dependencies.Add(attributeType);
            }
        }

        return dependencies;
    }

    private static ReferencedType? GetAttributeType(
        MetadataReader reader,
        DependencySignatureProvider provider,
        CustomAttributeHandle attributeHandle)
    {
        var constructor = reader.GetCustomAttribute(attributeHandle).Constructor;
        var declaringType = constructor.Kind switch
        {
            HandleKind.MethodDefinition => reader
                .GetMethodDefinition((MethodDefinitionHandle)constructor)
                .GetDeclaringType(),
            HandleKind.MemberReference => reader
                .GetMemberReference((MemberReferenceHandle)constructor)
                .Parent,
            _ => default(EntityHandle),
        };

        return GetEntityDependencies(provider, declaringType).FirstOrDefault();
    }

    private static ImmutableHashSet<ReferencedType> GetEntityDependencies(
        DependencySignatureProvider provider,
        EntityHandle handle)
    {
        if (handle.IsNil)
        {
            return ImmutableHashSet<ReferencedType>.Empty;
        }

        return handle.Kind switch
        {
            HandleKind.TypeDefinition => provider.GetTypeFromDefinition(
                provider.Reader,
                (TypeDefinitionHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeReference => provider.GetTypeFromReference(
                provider.Reader,
                (TypeReferenceHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeSpecification => provider.GetTypeFromSpecification(
                provider.Reader,
                genericContext: null,
                (TypeSpecificationHandle)handle,
                rawTypeKind: 0),
            _ => ImmutableHashSet<ReferencedType>.Empty,
        };
    }

    private static bool IsExternallyVisible(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var visibility = definition.Attributes & TypeAttributes.VisibilityMask;

        if (visibility == TypeAttributes.Public)
        {
            return true;
        }

        if (visibility is not (TypeAttributes.NestedPublic or
            TypeAttributes.NestedFamily or
            TypeAttributes.NestedFamORAssem))
        {
            return false;
        }

        var declaringType = definition.GetDeclaringType();
        return !declaringType.IsNil && IsExternallyVisible(reader, declaringType);
    }

    private static bool IsExternallyVisible(FieldAttributes attributes)
    {
        var access = attributes & FieldAttributes.FieldAccessMask;
        return access is FieldAttributes.Public or
            FieldAttributes.Family or
            FieldAttributes.FamORAssem;
    }

    private static bool IsExternallyVisible(MethodAttributes attributes)
    {
        var access = attributes & MethodAttributes.MemberAccessMask;
        return access is MethodAttributes.Public or
            MethodAttributes.Family or
            MethodAttributes.FamORAssem;
    }

    private static bool HasExternallyVisibleAccessor(
        MetadataReader reader,
        PropertyAccessors accessors)
    {
        return IsExternallyVisible(reader, accessors.Getter) ||
               IsExternallyVisible(reader, accessors.Setter) ||
               accessors.Others.Any(handle => IsExternallyVisible(reader, handle));
    }

    private static bool HasExternallyVisibleAccessor(
        MetadataReader reader,
        EventAccessors accessors)
    {
        return IsExternallyVisible(reader, accessors.Adder) ||
               IsExternallyVisible(reader, accessors.Remover) ||
               IsExternallyVisible(reader, accessors.Raiser) ||
               accessors.Others.Any(handle => IsExternallyVisible(reader, handle));
    }

    private static bool IsExternallyVisible(
        MetadataReader reader,
        MethodDefinitionHandle handle)
    {
        return !handle.IsNil &&
               IsExternallyVisible(reader.GetMethodDefinition(handle).Attributes);
    }

    private static string GetEffectiveNamespace(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var @namespace = reader.GetString(definition.Namespace);
        if (!string.IsNullOrEmpty(@namespace))
        {
            return @namespace;
        }

        var declaringType = definition.GetDeclaringType();
        return declaringType.IsNil
            ? string.Empty
            : GetEffectiveNamespace(reader, declaringType);
    }
}

internal sealed record AssemblyInspection(
    string AssemblyName,
    IReadOnlySet<string> AssemblyReferences,
    IReadOnlyList<PublicTypeInspection> PublicTypes,
    bool HasInternalsVisibleTo);

internal sealed record PublicTypeInspection(
    string Namespace,
    string Name,
    IReadOnlySet<ReferencedType> Dependencies)
{
    public string FullName => string.IsNullOrEmpty(Namespace)
        ? Name
        : $"{Namespace}.{Name}";
}

internal sealed record ReferencedType(
    string Assembly,
    string Namespace,
    string Name)
{
    public string FullName => string.IsNullOrEmpty(Namespace)
        ? Name
        : $"{Namespace}.{Name}";
}

internal sealed class DependencySignatureProvider
    : ISignatureTypeProvider<ImmutableHashSet<ReferencedType>, object?>
{
    private readonly string _currentAssembly;

    public DependencySignatureProvider(MetadataReader reader, string currentAssembly)
    {
        Reader = reader;
        _currentAssembly = currentAssembly;
    }

    public MetadataReader Reader { get; }

    public ImmutableHashSet<ReferencedType> GetArrayType(
        ImmutableHashSet<ReferencedType> elementType,
        ArrayShape shape) => elementType;

    public ImmutableHashSet<ReferencedType> GetByReferenceType(
        ImmutableHashSet<ReferencedType> elementType) => elementType;

    public ImmutableHashSet<ReferencedType> GetFunctionPointerType(
        MethodSignature<ImmutableHashSet<ReferencedType>> signature)
    {
        var dependencies = signature.ReturnType;
        foreach (var parameterType in signature.ParameterTypes)
        {
            dependencies = dependencies.Union(parameterType);
        }

        return dependencies;
    }

    public ImmutableHashSet<ReferencedType> GetGenericInstantiation(
        ImmutableHashSet<ReferencedType> genericType,
        ImmutableArray<ImmutableHashSet<ReferencedType>> typeArguments)
    {
        var dependencies = genericType;
        foreach (var typeArgument in typeArguments)
        {
            dependencies = dependencies.Union(typeArgument);
        }

        return dependencies;
    }

    public ImmutableHashSet<ReferencedType> GetGenericMethodParameter(
        object? genericContext,
        int index) => ImmutableHashSet<ReferencedType>.Empty;

    public ImmutableHashSet<ReferencedType> GetGenericTypeParameter(
        object? genericContext,
        int index) => ImmutableHashSet<ReferencedType>.Empty;

    public ImmutableHashSet<ReferencedType> GetModifiedType(
        ImmutableHashSet<ReferencedType> modifier,
        ImmutableHashSet<ReferencedType> unmodifiedType,
        bool isRequired) => modifier.Union(unmodifiedType);

    public ImmutableHashSet<ReferencedType> GetPinnedType(
        ImmutableHashSet<ReferencedType> elementType) => elementType;

    public ImmutableHashSet<ReferencedType> GetPointerType(
        ImmutableHashSet<ReferencedType> elementType) => elementType;

    public ImmutableHashSet<ReferencedType> GetPrimitiveType(
        PrimitiveTypeCode typeCode) => ImmutableHashSet<ReferencedType>.Empty;

    public ImmutableHashSet<ReferencedType> GetSZArrayType(
        ImmutableHashSet<ReferencedType> elementType) => elementType;

    public ImmutableHashSet<ReferencedType> GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
    {
        var definition = reader.GetTypeDefinition(handle);
        return One(new ReferencedType(
            _currentAssembly,
            GetEffectiveNamespace(reader, handle),
            reader.GetString(definition.Name)));
    }

    public ImmutableHashSet<ReferencedType> GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
    {
        var reference = reader.GetTypeReference(handle);
        var @namespace = reader.GetString(reference.Namespace);
        if (string.IsNullOrEmpty(@namespace) &&
            reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            @namespace = GetReferenceNamespace(
                reader,
                (TypeReferenceHandle)reference.ResolutionScope);
        }

        return One(new ReferencedType(
            GetReferenceAssembly(reader, reference.ResolutionScope),
            @namespace,
            reader.GetString(reference.Name)));
    }

    public ImmutableHashSet<ReferencedType> GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind)
    {
        return reader.GetTypeSpecification(handle)
            .DecodeSignature(this, genericContext);
    }

    private string GetReferenceAssembly(MetadataReader reader, EntityHandle scope)
    {
        return scope.Kind switch
        {
            HandleKind.AssemblyReference => reader.GetString(
                reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name),
            HandleKind.TypeReference => GetReferenceAssembly(
                reader,
                reader.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope),
            HandleKind.ModuleDefinition or HandleKind.ModuleReference => _currentAssembly,
            _ => _currentAssembly,
        };
    }

    private static string GetReferenceNamespace(
        MetadataReader reader,
        TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        var @namespace = reader.GetString(reference.Namespace);
        return !string.IsNullOrEmpty(@namespace)
            ? @namespace
            : reference.ResolutionScope.Kind == HandleKind.TypeReference
                ? GetReferenceNamespace(
                    reader,
                    (TypeReferenceHandle)reference.ResolutionScope)
                : string.Empty;
    }

    private static ImmutableHashSet<ReferencedType> One(ReferencedType type) =>
        ImmutableHashSet.Create(type);

    private static string GetEffectiveNamespace(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var @namespace = reader.GetString(definition.Namespace);
        if (!string.IsNullOrEmpty(@namespace))
        {
            return @namespace;
        }

        var declaringType = definition.GetDeclaringType();
        return declaringType.IsNil
            ? string.Empty
            : GetEffectiveNamespace(reader, declaringType);
    }
}
