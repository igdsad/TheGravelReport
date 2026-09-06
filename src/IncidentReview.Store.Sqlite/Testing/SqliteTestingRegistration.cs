using DbUp.Engine;
using IncidentReview.Store.Contracts;
using IncidentReview.Store.Sqlite.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace IncidentReview.Store.Sqlite.Testing;

/// <summary>Creates the explicit SQLite initialization seam used by integration tests.</summary>
public static class SqliteTestingRegistration
{
    /// <summary>Creates an initializer with optional test-only migrations.</summary>
    public static IStoreInitializer CreateInitializer(
        SqliteStoreOptions options,
        params SqliteTestMigration[] migrations)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(migrations);

        var scripts = migrations.Select(static migration =>
        {
            ArgumentNullException.ThrowIfNull(migration);
            return new SqlScript(migration.Name, migration.Sql);
        }).ToArray();

        return new SqliteStoreInitializer(
            options,
            scripts,
            NullLogger<SqliteStoreInitializer>.Instance);
    }
}
