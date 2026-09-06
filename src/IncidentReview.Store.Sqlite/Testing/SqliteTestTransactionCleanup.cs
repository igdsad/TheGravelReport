using Microsoft.Data.Sqlite;

namespace IncidentReview.Store.Sqlite.Testing;

internal sealed class SqliteTestTransactionCleanup : ISqliteTransactionCleanup
{
    private readonly SqliteTestCleanupMode _mode;

    public SqliteTestTransactionCleanup(SqliteTestCleanupMode mode)
    {
        _mode = mode;
    }

    public void Rollback(SqliteTransaction transaction)
    {
        transaction.Rollback();
        ThrowIfConfigured();
    }

    public void Dispose(SqliteTransaction transaction)
    {
        transaction.Dispose();
        ThrowIfConfigured();
    }

    private void ThrowIfConfigured()
    {
        if (_mode == SqliteTestCleanupMode.FailAfterCleanup)
        {
            throw new InvalidOperationException("A test transaction cleanup failure was injected.");
        }

        if (_mode != SqliteTestCleanupMode.Normal)
        {
            throw new InvalidOperationException("The configured test cleanup mode is invalid.");
        }
    }
}
