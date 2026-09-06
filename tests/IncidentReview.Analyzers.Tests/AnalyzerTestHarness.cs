using System.Collections.Immutable;
using IncidentReview.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Analyzers.Tests;

internal static class AnalyzerTestHarness
{
    private static readonly ImmutableArray<MetadataReference> PlatformReferences = CreatePlatformReferences();

    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string source,
        string assemblyName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            PlatformReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var compilationErrors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        if (compilationErrors.Length > 0)
        {
            Assert.Fail(
                "The analyzer test source did not compile:" + Environment.NewLine
                + string.Join(Environment.NewLine, compilationErrors.Select(static error => error.ToString())));
        }

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new RepositorySafetyAnalyzer());
        var diagnostics = await compilation
            .WithAnalyzers(analyzers)
            .GetAnalyzerDiagnosticsAsync();

        return diagnostics
            .OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToImmutableArray();
    }

    private static ImmutableArray<MetadataReference> CreatePlatformReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            throw new InvalidOperationException("The runtime did not provide its trusted platform assembly list.");
        }

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }
}
