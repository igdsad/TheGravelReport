using System.Collections.Immutable;
using IncidentReview.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Analyzers.Tests;

[TestClass]
public sealed class RepositorySafetyAnalyzerTests
{
    private const string DapperStub = """
        #nullable enable
        using System.Data;

        namespace Dapper
        {
            public readonly struct CommandDefinition
            {
                public CommandDefinition(
                    string commandText,
                    object? parameters = null,
                    IDbTransaction? transaction = null)
                {
                }
            }

            public static class SqlMapper
            {
                public static int Execute(
                    this IDbConnection connection,
                    string sql,
                    object? param = null,
                    IDbTransaction? transaction = null) => 0;

                public static object Query(
                    this IDbConnection connection,
                    string sql,
                    object? param = null,
                    IDbTransaction? transaction = null) => new object();

                public static int Execute(
                    this IDbConnection connection,
                    CommandDefinition command) => 0;

                public static object Query(
                    this IDbConnection connection,
                    CommandDefinition command) => new object();
            }
        }
        """;

    private const string DependencyInjectionStub = """
        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }
            public interface IServiceProvider { }

            public static class ServiceCollectionContainerBuilderExtensions
            {
                public static IServiceProvider BuildServiceProvider(this IServiceCollection services) => null!;
            }
        }
        """;

    [TestMethod]
    [TestProperty("Requirement", "QR-SQL-001")]
    public async Task InterpolatedDapperSqlReportsIR0001()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static object Run(IDbConnection connection, int incidentId)
                    {
                        return connection.Query($"SELECT * FROM Incident WHERE Id = {incidentId}");
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "Example");

        AssertHasOnly(diagnostics, RepositorySafetyAnalyzer.UnsafeSqlConstructionId);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-SQL-001")]
    public async Task RuntimeConcatenatedDapperSqlReportsIR0001()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static object Run(IDbConnection connection, string tableName)
                    {
                        return connection.Query("SELECT * FROM " + tableName);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "Example");

        AssertHasOnly(diagnostics, RepositorySafetyAnalyzer.UnsafeSqlConstructionId);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-SQL-001")]
    public async Task IndirectRuntimeSqlReportsIR0001()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static object Run(IDbConnection connection, string tableName)
                    {
                        var sql = "SELECT * FROM " + tableName;
                        return connection.Query(sql);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "Example");

        AssertHasOnly(diagnostics, RepositorySafetyAnalyzer.UnsafeSqlConstructionId);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-SQL-001")]
    public async Task ReassignedLocalSqlReportsIR0001()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static object Run(IDbConnection connection, string suffix)
                    {
                        var sql = "SELECT * FROM Incident";
                        sql += suffix;
                        return connection.Query(sql);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "Example");

        AssertHasOnly(diagnostics, RepositorySafetyAnalyzer.UnsafeSqlConstructionId);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-SQL-001")]
    public async Task ParameterizedDapperSqlDoesNotReportIR0001()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static object Run(IDbConnection connection, int incidentId)
                    {
                        return connection.Query(
                            "SELECT * FROM Incident WHERE Id = @IncidentId",
                            new { IncidentId = incidentId });
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "Example");

        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-SQL-001")]
    public async Task CompileTimeConstantSqlConcatenationDoesNotReportIR0001()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static object Run(IDbConnection connection)
                    {
                        return connection.Query("SELECT * " + "FROM Incident");
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "Example");

        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-SQL-001")]
    public async Task NonDapperQueryDoesNotReportIR0001()
    {
        const string source = """
            namespace Example
            {
                public sealed class Connection
                {
                    public object Query(string sql) => new object();
                }

                public static class Subject
                {
                    public static object Run(Connection connection, int incidentId)
                    {
                        return connection.Query($"SELECT {incidentId}");
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "Example");

        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-002")]
    public async Task OmittedDapperMutationTransactionReportsIR0002InSqliteStore()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static int Run(IDbConnection connection)
                    {
                        return connection.Execute("DELETE FROM Incident");
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "IncidentReview.Store.Sqlite");

        AssertHasOnly(diagnostics, RepositorySafetyAnalyzer.MissingDapperTransactionId);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-002")]
    public async Task NullDapperMutationTransactionReportsIR0002InSqliteStore()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static int Run(IDbConnection connection)
                    {
                        return connection.Execute("DELETE FROM Incident", transaction: null);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "IncidentReview.Store.Sqlite");

        AssertHasOnly(diagnostics, RepositorySafetyAnalyzer.MissingDapperTransactionId);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-002")]
    public async Task DefaultDapperMutationTransactionReportsIR0002InSqliteStore()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static int Run(IDbConnection connection)
                    {
                        return connection.Execute("DELETE FROM Incident", transaction: default);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "IncidentReview.Store.Sqlite");

        AssertHasOnly(diagnostics, RepositorySafetyAnalyzer.MissingDapperTransactionId);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-002")]
    public async Task ExplicitDapperMutationTransactionDoesNotReportIR0002()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static int Run(IDbConnection connection, IDbTransaction transaction)
                    {
                        return connection.Execute("DELETE FROM Incident", transaction: transaction);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "IncidentReview.Store.Sqlite");

        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-002")]
    public async Task CommandDefinitionWithoutTransactionReportsIR0002()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static int Run(IDbConnection connection)
                    {
                        return connection.Execute(new CommandDefinition("DELETE FROM Incident"));
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
            source,
            "IncidentReview.Store.Sqlite");

        AssertHasOnly(diagnostics, RepositorySafetyAnalyzer.MissingDapperTransactionId);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-SQL-001")]
    [TestProperty("Requirement", "IR-STR-002")]
    public async Task TransactionBoundCommandFactoryWithConstantSqlIsAccepted()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static int Run(
                        IDbConnection connection,
                        IDbTransaction transaction)
                    {
                        return connection.Execute(CreateDapperCommand(
                            "DELETE FROM Incident",
                            new object(),
                            transaction));
                    }

                    private static CommandDefinition CreateDapperCommand(
                        string sql,
                        object? parameters,
                        IDbTransaction transaction) => new(sql, parameters, transaction);
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
            source,
            "IncidentReview.Store.Sqlite");

        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-SQL-001")]
    [TestProperty("Requirement", "IR-STR-002")]
    public async Task CommandFactoryRejectsIndirectRuntimeSql()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static int Run(
                        IDbConnection connection,
                        IDbTransaction transaction,
                        string tableName)
                    {
                        var sql = "DELETE FROM " + tableName;
                        return connection.Execute(CreateDapperCommand(
                            sql,
                            new object(),
                            transaction));
                    }

                    private static CommandDefinition CreateDapperCommand(
                        string sql,
                        object? parameters,
                        IDbTransaction transaction) => new(sql, parameters, transaction);
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
            source,
            "IncidentReview.Store.Sqlite");

        AssertHasOnly(diagnostics, RepositorySafetyAnalyzer.UnsafeSqlConstructionId);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-002")]
    public async Task CommandFactoryThatDropsTransactionReportsIR0002()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static int Run(
                        IDbConnection connection,
                        IDbTransaction transaction)
                    {
                        return connection.Execute(CreateDapperCommand(
                            "DELETE FROM Incident",
                            new object(),
                            transaction));
                    }

                    private static CommandDefinition CreateDapperCommand(
                        string sql,
                        object? parameters,
                        IDbTransaction transaction) => new(sql, parameters);
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
            source,
            "IncidentReview.Store.Sqlite");

        AssertHasOnly(diagnostics, RepositorySafetyAnalyzer.MissingDapperTransactionId);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-STR-002")]
    public async Task DapperMutationOutsideSqliteStoreDoesNotReportIR0002()
    {
        var source = DapperStub + """
            namespace Example
            {
                using Dapper;
                using System.Data;

                public static class Subject
                {
                    public static int Run(IDbConnection connection)
                    {
                        return connection.Execute("DELETE FROM Incident");
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "Example");

        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-004")]
    public async Task BuildServiceProviderOutsideHostReportsIR0003()
    {
        var source = DependencyInjectionStub + """
            namespace Example
            {
                using Microsoft.Extensions.DependencyInjection;

                public static class Registration
                {
                    public static void AddServices(IServiceCollection services)
                    {
                        _ = services.BuildServiceProvider();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "IncidentReview.Application");

        AssertHasOnly(diagnostics, RepositorySafetyAnalyzer.NestedServiceProviderId);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-004")]
    public async Task BuildServiceProviderInHostDoesNotReportIR0003()
    {
        var source = DependencyInjectionStub + """
            namespace Example
            {
                using Microsoft.Extensions.DependencyInjection;

                public static class CompositionRoot
                {
                    public static void Build(IServiceCollection services)
                    {
                        _ = services.BuildServiceProvider();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "IncidentReview.Host.Wpf");

        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-004")]
    public async Task UnrelatedBuildServiceProviderMethodDoesNotReportIR0003()
    {
        const string source = """
            namespace Example
            {
                public sealed class Services
                {
                    public object BuildServiceProvider() => new object();
                }

                public static class Subject
                {
                    public static void Run(Services services)
                    {
                        _ = services.BuildServiceProvider();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, "IncidentReview.Application");

        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-004")]
    public void DiagnosticsHaveStableErrorSeverity()
    {
        var descriptors = new RepositorySafetyAnalyzer().SupportedDiagnostics;

        CollectionAssert.AreEquivalent(
            new[]
            {
                RepositorySafetyAnalyzer.UnsafeSqlConstructionId,
                RepositorySafetyAnalyzer.MissingDapperTransactionId,
                RepositorySafetyAnalyzer.NestedServiceProviderId,
            },
            descriptors.Select(static descriptor => descriptor.Id).ToArray());
        Assert.IsTrue(descriptors.All(static descriptor => descriptor.DefaultSeverity == DiagnosticSeverity.Error));
        Assert.IsTrue(descriptors.All(static descriptor => descriptor.IsEnabledByDefault));
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-001")]
    public void InitializeRejectsNullAnalysisContext()
    {
        var analyzer = new RepositorySafetyAnalyzer();

        Assert.ThrowsExactly<ArgumentNullException>(() => analyzer.Initialize(null!));
    }

    private static void AssertHasOnly(ImmutableArray<Diagnostic> diagnostics, string expectedId)
    {
        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual(expectedId, diagnostics[0].Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostics[0].Severity);
    }
}
