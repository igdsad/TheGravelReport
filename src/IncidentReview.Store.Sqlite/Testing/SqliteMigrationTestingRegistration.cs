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

        var scripts = new List<SqlScript>(migrations.Length + 1)
        {
            new(GateMigrationName, GateMigrationSql),
        };
        scripts.AddRange(migrations.Select(static migration =>
        {
            ArgumentNullException.ThrowIfNull(migration);
            return new SqlScript(migration.Name, migration.Sql);
        }));

        return new SqliteStoreInitializer(
            options,
            scripts,
            new SqliteStoreGate(),
            NullLogger<SqliteStoreInitializer>.Instance,
            connection => connection.CreateFunction<long>(GateFunctionName, gate.EnterAndWait));
    }
}
