using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Store.ContractTests;

[TestClass]
public sealed class PublicApiBoundaryTests
{
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "Dapper",
        "DbUp",
        "Microsoft.Data.Sqlite",
        "System.Data",
        "System.Linq.Expressions",
        "System.Net.Http",
    ];

    private static readonly string[] ForbiddenVocabulary =
    [
        "Connection",
        "Dapper",
        "DbUp",
        "Expression",
        "Http",
        "IQueryable",
        "Migration",
        "Sql",
        "Transaction",
    ];

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-003")]
    [TestProperty("Requirement", "QR-SQL-001")]
    public void ContractAssemblyDoesNotReferenceProviderOrTransportAssemblies()
    {
        var references = typeof(IStore).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        foreach (var reference in references)
        {
            Assert.IsFalse(
                ForbiddenAssemblyPrefixes.Any(prefix =>
                    reference.StartsWith(prefix, StringComparison.Ordinal)),
                $"Forbidden assembly reference: {reference}");
        }
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-003")]
    [TestProperty("Requirement", "QR-SQL-002")]
    public void PublicApiDoesNotExposeProviderSqlOrTransportVocabulary()
    {
        var exportedTypes = typeof(IStore).Assembly.GetExportedTypes();
        var surface = exportedTypes
            .SelectMany(GetPublicSurface)
            .ToArray();

        foreach (var item in surface)
        {
            foreach (var forbiddenWord in ForbiddenVocabulary)
            {
                Assert.IsFalse(
                    item.Contains(forbiddenWord, StringComparison.OrdinalIgnoreCase),
                    $"Forbidden public API vocabulary '{forbiddenWord}' in {item}");
            }
        }
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-003")]
    public void PublicApiTypeSignaturesDoNotExposeProviderOrTransportTypes()
    {
        var exposedTypes = typeof(IStore).Assembly.GetExportedTypes()
            .SelectMany(GetSignatureTypes)
            .Distinct()
            .ToArray();

        foreach (var exposedType in exposedTypes)
        {
            var assemblyName = exposedType.Assembly.GetName().Name ?? string.Empty;
            var typeName = exposedType.FullName ?? exposedType.Name;

            Assert.IsFalse(
                ForbiddenAssemblyPrefixes.Any(prefix =>
                    assemblyName.StartsWith(prefix, StringComparison.Ordinal)),
                $"Forbidden public API type: {typeName} from {assemblyName}");
            Assert.AreNotEqual(typeof(IQueryable), exposedType);
            Assert.IsFalse(
                exposedType.IsGenericType &&
                exposedType.GetGenericTypeDefinition() == typeof(IQueryable<>),
                $"Forbidden queryable public API type: {typeName}");
        }
    }

    private static IEnumerable<string> GetPublicSurface(Type type)
    {
        yield return type.FullName ?? type.Name;

        foreach (var member in type.GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.DeclaredOnly))
        {
            yield return $"{type.FullName}.{member.Name}";
        }
    }

    private static IEnumerable<Type> GetSignatureTypes(Type type)
    {
        yield return type;

        foreach (var interfaceType in type.GetInterfaces())
        {
            foreach (var component in Flatten(interfaceType))
            {
                yield return component;
            }
        }

        foreach (var method in type.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.DeclaredOnly))
        {
            foreach (var component in Flatten(method.ReturnType))
            {
                yield return component;
            }

            foreach (var parameter in method.GetParameters())
            {
                foreach (var component in Flatten(parameter.ParameterType))
                {
                    yield return component;
                }
            }
        }

        foreach (var property in type.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.DeclaredOnly))
        {
            foreach (var component in Flatten(property.PropertyType))
            {
                yield return component;
            }
        }
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.HasElementType)
        {
            foreach (var component in Flatten(type.GetElementType()!))
            {
                yield return component;
            }
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var component in Flatten(argument))
            {
                yield return component;
            }
        }
    }
}
