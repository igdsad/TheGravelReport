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

        return new SqliteStoreInitializer(
            options,
            CreateScripts(migrations),
            new SqliteStoreGate(),
            NullLogger<SqliteStoreInitializer>.Instance);
    }

    /// <summary>Creates a real store, its initializer, and an owned worker lifetime.</summary>
    public static SqliteTestStoreContext CreateStore(
        SqliteStoreOptions options,
        SqliteTestCommitMode commitMode = SqliteTestCommitMode.Normal,
        SqliteTestCleanupMode cleanupMode = SqliteTestCleanupMode.Normal,
        params SqliteTestMigration[] migrations)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(migrations);

        return CreateStoreCore(
            options,
            commitMode,
            cleanupMode,
            SqliteStoreExecutionCheckpoint.Instance,
            migrations);
    }

    /// <summary>Creates a store that cancels the supplied source immediately after operation lookup.</summary>
    public static SqliteTestStoreContext CreateStoreCancellingAfterOperationLookup(
        SqliteStoreOptions options,
        CancellationTokenSource cancellationSource)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cancellationSource);

        return CreateStoreCore(
            options,
            SqliteTestCommitMode.Normal,
            SqliteTestCleanupMode.Normal,
            new CancellationExecutionCheckpoint(cancellationSource),
            Array.Empty<SqliteTestMigration>());
    }

    private static SqlScript[] CreateScripts(SqliteTestMigration[] migrations)
    {
        var scripts = migrations.Select(static migration =>
        {
            ArgumentNullException.ThrowIfNull(migration);
            return new SqlScript(migration.Name, migration.Sql);
        }).ToArray();

        return scripts;
    }

    private static SqliteTestStoreContext CreateStoreCore(
        SqliteStoreOptions options,
        SqliteTestCommitMode commitMode,
        SqliteTestCleanupMode cleanupMode,
        ISqliteStoreExecutionCheckpoint executionCheckpoint,
        SqliteTestMigration[] migrations)
    {
        var gate = new SqliteStoreGate();
        var store = new SqliteStore(
            options,
            gate,
            new SqliteTestCommitBoundary(commitMode),
            new SqliteTestTransactionCleanup(cleanupMode),
            executionCheckpoint,
            NullLogger<SqliteStore>.Instance);
        var initializer = new SqliteStoreInitializer(
            options,
            CreateScripts(migrations),
            gate,
            NullLogger<SqliteStoreInitializer>.Instance);
        return new SqliteTestStoreContext(store, initializer, store);
    }

    private sealed class CancellationExecutionCheckpoint : ISqliteStoreExecutionCheckpoint
    {
        private readonly CancellationTokenSource _cancellationSource;

        public CancellationExecutionCheckpoint(CancellationTokenSource cancellationSource)
        {
            _cancellationSource = cancellationSource;
        }

        public void AfterOperationLookup() => _cancellationSource.Cancel();
    }
}
