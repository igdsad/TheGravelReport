using DbUp.Engine;
using IncidentReview.Store.Contracts;
using IncidentReview.Store.Sqlite.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace IncidentReview.Store.Sqlite.Testing;

/// <summary>Creates SQLite initializers with deterministic migration coordination.</summary>
public static class SqliteMigrationTestingRegistration
{
    private const string GateFunctionName = "incident_review_test_migration_gate";
    private const string GateMigrationName = "Test_000_MigrationGate.sql";
    private const string GateMigrationSql = "SELECT incident_review_test_migration_gate();";

    /// <summary>Creates an initializer that pauses from within DbUp until the gate is released.</summary>
    public static IStoreInitializer CreateInitializer(
        SqliteStoreOptions options,
        SqliteTestMigrationGate gate,
        params SqliteTestMigration[] migrations)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(migrations);

        return new SqliteStoreInitializer(
            options,
            CreateScripts(migrations),
            new SqliteStoreGate(),
            NullLogger<SqliteStoreInitializer>.Instance,
            connection => connection.CreateFunction<long>(GateFunctionName, gate.EnterAndWait));
    }

    /// <summary>Creates a real gated store paired with a migration-coordinated initializer.</summary>
    public static SqliteTestStoreContext CreateStore(
        SqliteStoreOptions options,
        SqliteTestMigrationGate gate,
        params SqliteTestMigration[] migrations)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(migrations);

        var storeGate = new SqliteStoreGate();
        var store = new SqliteStore(
            options,
            storeGate,
            SqliteCommitBoundary.Instance,
            SqliteTransactionCleanup.Instance,
            NullLogger<SqliteStore>.Instance);
        var initializer = new SqliteStoreInitializer(
            options,
            CreateScripts(migrations),
            storeGate,
            NullLogger<SqliteStoreInitializer>.Instance,
            connection => connection.CreateFunction<long>(GateFunctionName, gate.EnterAndWait));
        return new SqliteTestStoreContext(store, initializer, store);
    }

    private static List<SqlScript> CreateScripts(SqliteTestMigration[] migrations)
    {
        var scripts = new List<SqlScript>(migrations.Length + 1)
        {
            new(GateMigrationName, GateMigrationSql),
        };
        scripts.AddRange(migrations.Select(static migration =>
        {
            ArgumentNullException.ThrowIfNull(migration);
            return new SqlScript(migration.Name, migration.Sql);
        }));

        return scripts;
    }
}
