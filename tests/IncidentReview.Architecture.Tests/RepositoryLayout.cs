namespace IncidentReview.Architecture.Tests;

internal sealed class RepositoryLayout
{
    private static readonly string[] ProjectRootNames = ["src", "tests", "tools"];
    private static readonly char[] DirectorySeparators =
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private RepositoryLayout(string rootPath)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public string PolicyPath => Path.Combine(RootPath, "eng", "ArchitecturePolicy.props");

    public static RepositoryLayout Find()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(
            "INCIDENT_REVIEW_REPOSITORY_ROOT");

        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return FromCandidate(configuredRoot);
        }

        for (var candidate = new DirectoryInfo(AppContext.BaseDirectory);
             candidate is not null;
             candidate = candidate.Parent)
        {
            if (File.Exists(Path.Combine(candidate.FullName, "eng", "ArchitecturePolicy.props")))
            {
                return new RepositoryLayout(candidate.FullName);
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate eng/ArchitecturePolicy.props above the test output. " +
            "Set INCIDENT_REVIEW_REPOSITORY_ROOT when running the architecture tests " +
            "outside the repository build tree.");
    }

    public IReadOnlyList<string> FindProjectFiles()
    {
        var roots = ProjectRootNames
            .Select(name => Path.Combine(RootPath, name))
            .Where(Directory.Exists);

        return roots
            .SelectMany(root => Directory.EnumerateFiles(
                root,
                "*.csproj",
                SearchOption.AllDirectories))
            .Where(path => !ContainsGeneratedDirectory(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static string? FindBuiltAssembly(string projectFile)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)
            ?? throw new InvalidDataException($"Project path '{projectFile}' has no directory.");
        var projectName = Path.GetFileNameWithoutExtension(projectFile);
        var binDirectory = Path.Combine(projectDirectory, "bin");

        if (!Directory.Exists(binDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(
                binDirectory,
                $"{projectName}.dll",
                SearchOption.AllDirectories)
            .Where(path => !HasPathSegment(path, "ref") &&
                           !HasPathSegment(path, "refint") &&
                           !HasPathSegment(path, "publish"))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private static RepositoryLayout FromCandidate(string candidate)
    {
        var fullPath = Path.GetFullPath(candidate);
        var policyPath = Path.Combine(fullPath, "eng", "ArchitecturePolicy.props");
        return File.Exists(policyPath)
            ? new RepositoryLayout(fullPath)
            : throw new DirectoryNotFoundException(
                $"INCIDENT_REVIEW_REPOSITORY_ROOT '{fullPath}' does not contain " +
                "eng/ArchitecturePolicy.props.");
    }

    private static bool ContainsGeneratedDirectory(string path) =>
        HasPathSegment(path, "bin") || HasPathSegment(path, "obj");

    private static bool HasPathSegment(string path, string segment)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var segments = path.Split(DirectorySeparators, StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(part => string.Equals(part, segment, comparison));
    }
}
