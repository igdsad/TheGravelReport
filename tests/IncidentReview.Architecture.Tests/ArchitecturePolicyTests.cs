using System.Xml.Linq;

namespace IncidentReview.Architecture.Tests;

[TestClass]
public sealed class ArchitecturePolicyTests
{
    private static readonly RepositoryLayout Layout = RepositoryLayout.Find();
    private static readonly ArchitecturePolicy Policy = ArchitecturePolicy.Load(Layout.PolicyPath);

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-001")]
    [TestProperty("Requirement", "QR-ARC-002")]
    [TestProperty("Requirement", "QR-ARC-003")]
    public void PolicyIsSelfConsistent()
    {
        var violations = new List<string>();
        var knownKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "Production",
            "DeferredProduction",
            "Test",
            "Tool",
        };

        if (string.IsNullOrWhiteSpace(Policy.Version))
        {
            violations.Add("ArchitecturePolicyVersion must be present and nonempty.");
        }

        AddDuplicateViolations(
            violations,
            Policy.Projects.Select(project => project.Name),
            "project identity");
        AddDuplicateViolations(
            violations,
            Policy.Projects.Select(project => project.RootNamespace),
            "root namespace");
        AddDuplicateViolations(
            violations,
            Policy.AllowedProjectReferences.Select(edge => $"{edge.From}->{edge.To}"),
            "project-reference edge");
        AddDuplicateViolations(
            violations,
            Policy.PackageOwners.Select(owner => $"{owner.Package}->{owner.Project}"),
            "package-owner pair");
        AddDuplicateViolations(
            violations,
            Policy.AllowedPublicNamespaces.Select(
                rule => $"{rule.Project}->{rule.Namespace}"),
            "public-namespace rule");

        foreach (var project in Policy.Projects)
        {
            if (!knownKinds.Contains(project.Kind))
            {
                violations.Add(
                    $"Project '{project.Name}' has unknown kind '{project.Kind}'.");
            }

            if (!project.Name.StartsWith("IncidentReview.", StringComparison.Ordinal))
            {
                violations.Add(
                    $"Project '{project.Name}' is outside the IncidentReview identity root.");
            }

            if (project.IsProduction)
            {
                var hasNamespaceRule = Policy.AllowedPublicNamespaces.Any(
                    rule => string.Equals(
                        rule.Project,
                        project.Name,
                        StringComparison.Ordinal));
                if (project.NoPublicApi == hasNamespaceRule)
                {
                    violations.Add(
                        $"Production project '{project.Name}' must choose exactly one of " +
                        "NoPublicApi=true or one-or-more AllowedPublicNamespace rules.");
                }
            }
        }

        foreach (var edge in Policy.AllowedProjectReferences)
        {
            ValidateKnownProject(violations, edge.From, $"edge source '{edge.From}->{edge.To}'");
            ValidateKnownProject(violations, edge.To, $"edge target '{edge.From}->{edge.To}'");
            if (string.Equals(edge.From, edge.To, StringComparison.Ordinal))
            {
                violations.Add($"Self-reference edge '{edge.From}->{edge.To}' is forbidden.");
            }

            var source = Policy.FindProject(edge.From);
            var target = Policy.FindProject(edge.To);
            if (source?.IsProduction == true && target?.IsProduction == false)
            {
                violations.Add(
                    $"Production project '{edge.From}' may not reference non-production " +
                    $"project '{edge.To}'.");
            }
        }

        foreach (var owner in Policy.PackageOwners)
        {
            ValidateKnownProject(
                violations,
                owner.Project,
                $"owner of package '{owner.Package}'");
        }

        foreach (var rule in Policy.AllowedPublicNamespaces)
        {
            ValidateKnownProject(
                violations,
                rule.Project,
                $"owner of public namespace '{rule.Namespace}'");
            var project = Policy.FindProject(rule.Project);
            if (project?.IsProduction != true)
            {
                violations.Add(
                    $"Public-namespace rule '{rule.Project}->{rule.Namespace}' is not " +
                    "owned by a production project.");
            }
        }

        foreach (var facade in Policy.TestingFacades)
        {
            ValidateKnownProject(
                violations,
                facade.Project,
                $"owner of testing facade '{facade.Namespace}'");
            if (Policy.FindProject(facade.Project)?.IsProduction != true)
            {
                violations.Add(
                    $"Testing facade '{facade.Namespace}' must belong to a production project.");
            }

            if (!facade.Namespace.Contains(".Testing", StringComparison.Ordinal))
            {
                violations.Add(
                    $"Testing facade '{facade.Namespace}' must be in a .Testing namespace.");
            }

            if (!Policy.AllowsPublicNamespace(facade.Project, facade.Namespace))
            {
                violations.Add(
                    $"Testing facade '{facade.Namespace}' is not an allowed public namespace " +
                    $"of '{facade.Project}'.");
            }

            if (!string.Equals(
                    Path.GetExtension(facade.BridgeFile),
                    ".cs",
                    StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(facade.BridgeFile) != facade.BridgeFile)
            {
                violations.Add(
                    $"Testing facade '{facade.Namespace}' must name one bridge C# filename, " +
                    $"not '{facade.BridgeFile}'.");
            }
        }

        foreach (var rule in Policy.AllowedExternalPublicAssemblies)
        {
            ValidateKnownProject(
                violations,
                rule.Project,
                $"consumer of external public assembly '{rule.Assembly}'");
            if (Policy.FindProject(rule.Project)?.IsProduction != true)
            {
                violations.Add(
                    $"External public-assembly rule '{rule.Project}->{rule.Assembly}' is not " +
                    "owned by a production project.");
            }
        }

        if (Policy.ForbiddenProductionNamespaces.Count == 0)
        {
            violations.Add("At least one ForbiddenProductionNamespace rule is required.");
        }

        violations.AddRange(FindCycles(Policy.Projects, Policy.AllowedProjectReferences));
        AssertNoViolations(violations);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-001")]
    public void RepositoryProjectsAreDeclaredInThePolicy()
    {
        var violations = new List<string>();

        foreach (var projectFile in Layout.FindProjectFiles())
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            var rule = Policy.FindProject(projectName);
            if (rule is null)
            {
                violations.Add(
                    $"Project '{Relative(projectFile)}' has no ArchitectureProject declaration.");
                continue;
            }

            var relative = Relative(projectFile).Replace('\\', '/');
            var expectedKind = relative.StartsWith("src/", StringComparison.Ordinal)
                ? rule.Kind is "Production" or "DeferredProduction"
                : relative.StartsWith("tests/", StringComparison.Ordinal)
                    ? string.Equals(rule.Kind, "Test", StringComparison.Ordinal)
                    : relative.StartsWith("tools/", StringComparison.Ordinal) &&
                      string.Equals(rule.Kind, "Tool", StringComparison.Ordinal);

            if (!expectedKind)
            {
                violations.Add(
                    $"Project '{relative}' is classified as '{rule.Kind}', which conflicts " +
                    "with its repository location.");
            }
        }

        AssertNoViolations(violations);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-001")]
    public void ProjectReferencesFollowTheCanonicalAllowlist()
    {
        var violations = new List<string>();

        foreach (var projectFile in Layout.FindProjectFiles())
        {
            var source = Path.GetFileNameWithoutExtension(projectFile);
            var document = XDocument.Load(projectFile, LoadOptions.SetLineInfo);
            var references = document.Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference");

            foreach (var reference in references)
            {
                var include = reference.Attribute("Include")?.Value.Trim();
                if (string.IsNullOrEmpty(include))
                {
                    violations.Add(
                        $"Project '{source}' contains a ProjectReference without Include.");
                    continue;
                }

                if (include.Contains("$(", StringComparison.Ordinal))
                {
                    violations.Add(
                        $"Project '{source}' uses unresolved ProjectReference '{include}'. " +
                        "Architecture edges must have stable paths.");
                    continue;
                }

                var normalizedInclude = include
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                var targetPath = Path.GetFullPath(
                    normalizedInclude,
                    Path.GetDirectoryName(projectFile)!);
                var target = Path.GetFileNameWithoutExtension(targetPath);

                if (!File.Exists(targetPath))
                {
                    violations.Add(
                        $"Project '{source}' references missing project '{Relative(targetPath)}'.");
                    continue;
                }

                if (Policy.FindProject(target) is null)
                {
                    violations.Add(
                        $"Project '{source}' references undeclared project '{target}'.");
                    continue;
                }

                if (!Policy.AllowsProjectReference(source, target))
                {
                    violations.Add(
                        $"Forbidden project reference '{source}->{target}' in " +
                        $"'{Relative(projectFile)}'.");
                }
            }
        }

        AssertNoViolations(violations);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-001")]
    [TestProperty("Requirement", "QR-ARC-003")]
    public void PackageReferencesUseCentralVersionsAndRespectOwnership()
    {
        var violations = new List<string>();

        foreach (var projectFile in Layout.FindProjectFiles())
        {
            var project = Path.GetFileNameWithoutExtension(projectFile);
            var document = XDocument.Load(projectFile, LoadOptions.SetLineInfo);
            var references = document.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference");

            foreach (var reference in references)
            {
                var package = reference.Attribute("Include")?.Value.Trim() ??
                              reference.Attribute("Update")?.Value.Trim();
                if (string.IsNullOrEmpty(package))
                {
                    violations.Add(
                        $"Project '{project}' contains a PackageReference without Include/Update.");
                    continue;
                }

                if (reference.Attribute("Version") is not null ||
                    reference.Elements().Any(
                        element => element.Name.LocalName is "Version" or "VersionOverride"))
                {
                    violations.Add(
                        $"Package '{package}' in '{project}' declares a project-level version.");
                }

                var packageRules = Policy.PackageOwners
                    .Where(owner => string.Equals(
                        owner.Package,
                        package,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (packageRules.Length > 0 && !packageRules.Any(
                        owner => string.Equals(
                            owner.Project,
                            project,
                            StringComparison.Ordinal)))
                {
                    violations.Add(
                        $"Project '{project}' consumes owned package '{package}' without a " +
                        "matching PackageOwner declaration.");
                }
            }
        }

        AssertNoViolations(violations);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void ProductionSourcesDoNotDeclareFriendAssemblies()
    {
        var violations = new List<string>();

        foreach (var projectFile in FindExistingProductionProjects())
        {
            var project = Path.GetFileNameWithoutExtension(projectFile);
            var document = XDocument.Load(projectFile, LoadOptions.SetLineInfo);
            if (document.Descendants().Any(
                    element => element.Name.LocalName == "InternalsVisibleTo"))
            {
                violations.Add(
                    $"Production project '{project}' declares InternalsVisibleTo in its project file.");
            }

            var projectDirectory = Path.GetDirectoryName(projectFile)!;
            foreach (var sourceFile in Directory.EnumerateFiles(
                         projectDirectory,
                         "*.cs",
                         SearchOption.AllDirectories)
                     .Where(path => !HasGeneratedSegment(path)))
            {
                var source = File.ReadAllText(sourceFile);
                if (source.Contains("InternalsVisibleTo(", StringComparison.Ordinal) ||
                    source.Contains("InternalsVisibleToAttribute(", StringComparison.Ordinal))
                {
                    violations.Add(
                        $"Production source '{Relative(sourceFile)}' declares a friend assembly.");
                }
            }
        }

        AssertNoViolations(violations);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-001")]
    [TestProperty("Requirement", "QR-ARC-002")]
    [TestProperty("Requirement", "QR-ARC-003")]
    public void BuiltProductionAssembliesFollowReferenceAndPublicApiRules()
    {
        var violations = new List<string>();

        foreach (var projectFile in FindExistingProductionProjects())
        {
            var project = Path.GetFileNameWithoutExtension(projectFile);
            var assemblyPath = RepositoryLayout.FindBuiltAssembly(projectFile);
            if (assemblyPath is null)
            {
                // Standalone architecture-test runs do not build unrelated projects. The
                // repository verification pipeline builds the solution first; any assembly
                // present there is inspected here. Project-file rules above always run.
                continue;
            }

            var inspection = AssemblyInspector.Inspect(assemblyPath);
            if (!string.Equals(inspection.AssemblyName, project, StringComparison.Ordinal))
            {
                violations.Add(
                    $"Project '{project}' emitted assembly '{inspection.AssemblyName}'. " +
                    "Stable project/assembly identities are required by the policy.");
                continue;
            }

            if (inspection.HasInternalsVisibleTo)
            {
                violations.Add(
                    $"Production assembly '{project}' contains InternalsVisibleToAttribute.");
            }

            foreach (var reference in inspection.AssemblyReferences.Where(
                         name => name.StartsWith("IncidentReview.", StringComparison.Ordinal)))
            {
                if (Policy.FindProject(reference) is null)
                {
                    violations.Add(
                        $"Assembly '{project}' references undeclared repository assembly " +
                        $"'{reference}'.");
                }
                else if (!Policy.AllowsProjectReference(project, reference))
                {
                    violations.Add(
                        $"Assembly '{project}' has forbidden compiled reference '{reference}'.");
                }
            }

            ValidatePublicApi(violations, project, inspection);
        }

        AssertNoViolations(violations);
    }

    private static void ValidatePublicApi(
        List<string> violations,
        string project,
        AssemblyInspection inspection)
    {
        var projectRule = Policy.FindProject(project)!;
        if (projectRule.NoPublicApi && inspection.PublicTypes.Count > 0)
        {
            violations.Add(
                $"Assembly '{project}' declares NoPublicApi=true but exports: " +
                string.Join(", ", inspection.PublicTypes.Select(type => type.FullName)));
            return;
        }

        foreach (var type in inspection.PublicTypes)
        {
            if (!Policy.AllowsPublicNamespace(project, type.Namespace))
            {
                violations.Add(
                    $"Public type '{type.FullName}' is outside the public namespaces owned " +
                    $"by '{project}'.");
            }

            var declaringTestingFacade = Policy.TestingFacades.SingleOrDefault(
                facade => string.Equals(facade.Project, project, StringComparison.Ordinal) &&
                          IsNamespaceOrDescendant(type.Namespace, facade.Namespace));

            foreach (var dependency in type.Dependencies)
            {
                if (IsTestingNamespace(dependency.Namespace))
                {
                    var approvedFacade = declaringTestingFacade is not null &&
                        string.Equals(dependency.Assembly, project, StringComparison.Ordinal) &&
                        IsNamespaceOrDescendant(
                            dependency.Namespace,
                            declaringTestingFacade.Namespace);
                    if (!approvedFacade)
                    {
                        violations.Add(
                            $"Public API '{type.FullName}' exposes testing type " +
                            $"'{dependency.FullName}' from '{dependency.Assembly}'.");
                    }
                }

                if (string.Equals(dependency.Assembly, project, StringComparison.Ordinal))
                {
                    continue;
                }

                if (dependency.Assembly.StartsWith("IncidentReview.", StringComparison.Ordinal))
                {
                    if (Policy.FindProject(dependency.Assembly) is null)
                    {
                        violations.Add(
                            $"Public API '{type.FullName}' exposes type '{dependency.FullName}' " +
                            $"from unknown assembly '{dependency.Assembly}'.");
                    }
                    else if (!Policy.AllowsProjectReference(project, dependency.Assembly))
                    {
                        violations.Add(
                            $"Public API '{type.FullName}' crosses forbidden boundary into " +
                            $"'{dependency.Assembly}' via '{dependency.FullName}'.");
                    }

                    continue;
                }

                if (!Policy.AllowsExternalPublicAssembly(project, dependency.Assembly))
                {
                    violations.Add(
                        $"Public API '{type.FullName}' exposes '{dependency.FullName}' from " +
                        $"unapproved external assembly '{dependency.Assembly}'.");
                }
            }
        }
    }

    private static IEnumerable<string> FindExistingProductionProjects()
    {
        return Layout.FindProjectFiles().Where(projectFile =>
        {
            var name = Path.GetFileNameWithoutExtension(projectFile);
            return Policy.FindProject(name)?.IsProduction == true;
        });
    }

    private static HashSet<string> FindCycles(
        IReadOnlyList<ProjectRule> projects,
        IReadOnlyList<ProjectEdge> edges)
    {
        var adjacency = projects.ToDictionary(
            project => project.Name,
            _ => new List<string>(),
            StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (adjacency.TryGetValue(edge.From, out var targets) &&
                adjacency.ContainsKey(edge.To))
            {
                targets.Add(edge.To);
            }
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var path = new Stack<string>();
        var violations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in adjacency.Keys)
        {
            Visit(project);
        }

        return violations;

        void Visit(string project)
        {
            if (active.Contains(project))
            {
                var cycle = path.Reverse().SkipWhile(item => item != project).Append(project);
                violations.Add($"Project-reference policy contains cycle: {string.Join(" -> ", cycle)}.");
                return;
            }

            if (!visited.Add(project))
            {
                return;
            }

            active.Add(project);
            path.Push(project);
            foreach (var target in adjacency[project])
            {
                Visit(target);
            }

            path.Pop();
            active.Remove(project);
        }
    }

    private static void ValidateKnownProject(
        List<string> violations,
        string project,
        string context)
    {
        if (Policy.FindProject(project) is null)
        {
            violations.Add($"Unknown project '{project}' used as {context}.");
        }
    }

    private static void AddDuplicateViolations(
        List<string> violations,
        IEnumerable<string> values,
        string description)
    {
        foreach (var duplicate in values
                     .GroupBy(value => value, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            violations.Add($"Duplicate {description} '{duplicate}'.");
        }
    }

    private static bool IsTestingNamespace(string @namespace) =>
        @namespace.EndsWith(".Testing", StringComparison.Ordinal) ||
        @namespace.Contains(".Testing.", StringComparison.Ordinal);

    private static bool IsNamespaceOrDescendant(string candidate, string parent) =>
        string.Equals(candidate, parent, StringComparison.Ordinal) ||
        candidate.StartsWith($"{parent}.", StringComparison.Ordinal);

    private static bool HasGeneratedSegment(string path)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return path.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment =>
                string.Equals(segment, "bin", comparison) ||
                string.Equals(segment, "obj", comparison));
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(Layout.RootPath, path);

    private static void AssertNoViolations(List<string> violations)
    {
        if (violations.Count == 0)
        {
            return;
        }

        Assert.Fail(
            $"Architecture policy violations ({violations.Count}):{Environment.NewLine}" +
            string.Join(
                Environment.NewLine,
                violations.Order(StringComparer.Ordinal).Select(item => $" - {item}")));
    }
}
