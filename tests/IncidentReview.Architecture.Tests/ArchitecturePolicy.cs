using System.Xml.Linq;

namespace IncidentReview.Architecture.Tests;

internal sealed class ArchitecturePolicy
{
    private ArchitecturePolicy(
        string version,
        IReadOnlyList<ProjectRule> projects,
        IReadOnlyList<ProjectEdge> allowedProjectReferences,
        IReadOnlyList<PackageOwnerRule> packageOwners,
        IReadOnlyList<PublicNamespaceRule> allowedPublicNamespaces,
        IReadOnlyList<TestingFacadeRule> testingFacades,
        IReadOnlyList<string> forbiddenProductionNamespaces,
        IReadOnlyList<ExternalPublicAssemblyRule> allowedExternalPublicAssemblies,
        IReadOnlySet<string> platformPublicAssemblies,
        IReadOnlyList<string> platformPublicAssemblyPrefixes)
    {
        Version = version;
        Projects = projects;
        AllowedProjectReferences = allowedProjectReferences;
        PackageOwners = packageOwners;
        AllowedPublicNamespaces = allowedPublicNamespaces;
        TestingFacades = testingFacades;
        ForbiddenProductionNamespaces = forbiddenProductionNamespaces;
        AllowedExternalPublicAssemblies = allowedExternalPublicAssemblies;
        PlatformPublicAssemblies = platformPublicAssemblies;
        PlatformPublicAssemblyPrefixes = platformPublicAssemblyPrefixes;
    }

    public string Version { get; }

    public IReadOnlyList<ProjectRule> Projects { get; }

    public IReadOnlyList<ProjectEdge> AllowedProjectReferences { get; }

    public IReadOnlyList<PackageOwnerRule> PackageOwners { get; }

    public IReadOnlyList<PublicNamespaceRule> AllowedPublicNamespaces { get; }

    public IReadOnlyList<TestingFacadeRule> TestingFacades { get; }

    public IReadOnlyList<string> ForbiddenProductionNamespaces { get; }

    public IReadOnlyList<ExternalPublicAssemblyRule> AllowedExternalPublicAssemblies { get; }

    public IReadOnlySet<string> PlatformPublicAssemblies { get; }

    public IReadOnlyList<string> PlatformPublicAssemblyPrefixes { get; }

    public static ArchitecturePolicy Load(string policyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyPath);

        var document = XDocument.Load(policyPath, LoadOptions.SetLineInfo);
        var root = document.Root
            ?? throw new InvalidDataException($"Architecture policy '{policyPath}' has no root element.");

        var version = root
            .Descendants("ArchitecturePolicyVersion")
            .Select(element => element.Value.Trim())
            .SingleOrDefault()
            ?? string.Empty;

        var projects = root.Descendants("ArchitectureProject")
            .Select(element => new ProjectRule(
                RequiredAttribute(element, "Include"),
                RequiredElement(element, "Kind"),
                RequiredElement(element, "RootNamespace"),
                OptionalBoolean(element, "NoPublicApi")))
            .ToArray();

        var edges = root.Descendants("AllowedProjectReference")
            .Select(element => new ProjectEdge(
                RequiredAttribute(element, "Include"),
                RequiredElement(element, "To")))
            .ToArray();

        var packageOwners = root.Descendants("PackageOwner")
            .Select(element => new PackageOwnerRule(
                RequiredAttribute(element, "Include"),
                RequiredElement(element, "Project")))
            .ToArray();

        var publicNamespaces = root.Descendants("AllowedPublicNamespace")
            .Select(element => new PublicNamespaceRule(
                RequiredAttribute(element, "Include"),
                RequiredElement(element, "Namespace"),
                OptionalBoolean(element, "AllowDescendants")))
            .ToArray();

        var testingFacades = root.Descendants("TestingFacadeOwner")
            .Select(element => new TestingFacadeRule(
                RequiredAttribute(element, "Include"),
                RequiredElement(element, "Project"),
                RequiredElement(element, "BridgeFile")))
            .ToArray();

        var forbiddenNamespaces = root.Descendants("ForbiddenProductionNamespace")
            .Select(element => RequiredAttribute(element, "Include"))
            .ToArray();

        var externalAssemblies = root.Descendants("AllowedExternalPublicAssembly")
            .Select(element => new ExternalPublicAssemblyRule(
                RequiredElement(element, "Project"),
                RequiredAttribute(element, "Include")))
            .ToArray();

        var platformAssemblies = root.Descendants("PlatformPublicAssembly")
            .Select(element => RequiredAttribute(element, "Include"))
            .ToHashSet(StringComparer.Ordinal);

        var platformPrefixes = root.Descendants("PlatformPublicAssemblyPrefix")
            .Select(element => RequiredAttribute(element, "Include"))
            .ToArray();

        return new ArchitecturePolicy(
            version,
            projects,
            edges,
            packageOwners,
            publicNamespaces,
            testingFacades,
            forbiddenNamespaces,
            externalAssemblies,
            platformAssemblies,
            platformPrefixes);
    }

    public ProjectRule? FindProject(string name) =>
        Projects.SingleOrDefault(project => string.Equals(project.Name, name, StringComparison.Ordinal));

    public bool AllowsProjectReference(string from, string to) =>
        AllowedProjectReferences.Contains(new ProjectEdge(from, to));

    public bool AllowsPublicNamespace(string project, string @namespace)
    {
        return AllowedPublicNamespaces
            .Where(rule => string.Equals(rule.Project, project, StringComparison.Ordinal))
            .Any(rule => rule.Allows(@namespace));
    }

    public bool AllowsExternalPublicAssembly(string project, string assemblyName)
    {
        if (PlatformPublicAssemblies.Contains(assemblyName) ||
            PlatformPublicAssemblyPrefixes.Any(
                prefix => assemblyName.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return true;
        }

        return AllowedExternalPublicAssemblies.Any(
            rule => string.Equals(rule.Project, project, StringComparison.Ordinal) &&
                    string.Equals(rule.Assembly, assemblyName, StringComparison.Ordinal));
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value.Trim();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException(
                $"Architecture policy element '{element.Name}' requires nonempty attribute '{name}'.");
    }

    private static string RequiredElement(XElement element, string name)
    {
        var value = element.Element(name)?.Value.Trim();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException(
                $"Architecture policy element '{element.Name}' requires nonempty child '{name}'.");
    }

    private static bool OptionalBoolean(XElement element, string name)
    {
        var value = element.Element(name)?.Value.Trim();
        return string.IsNullOrEmpty(value)
            ? false
            : bool.Parse(value);
    }
}

internal sealed record ProjectRule(
    string Name,
    string Kind,
    string RootNamespace,
    bool NoPublicApi)
{
    public bool IsProduction =>
        string.Equals(Kind, "Production", StringComparison.Ordinal) ||
        string.Equals(Kind, "DeferredProduction", StringComparison.Ordinal);
}

internal sealed record ProjectEdge(string From, string To);

internal sealed record PackageOwnerRule(string Package, string Project);

internal sealed record PublicNamespaceRule(
    string Project,
    string Namespace,
    bool AllowDescendants)
{
    public bool Allows(string candidate) =>
        string.Equals(Namespace, candidate, StringComparison.Ordinal) ||
        (AllowDescendants && candidate.StartsWith($"{Namespace}.", StringComparison.Ordinal));
}

internal sealed record TestingFacadeRule(
    string Namespace,
    string Project,
    string BridgeFile);

internal sealed record ExternalPublicAssemblyRule(
    string Project,
    string Assembly);
