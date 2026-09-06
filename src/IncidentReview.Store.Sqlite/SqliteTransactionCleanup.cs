using Microsoft.Data.Sqlite;

namespace IncidentReview.Store.Sqlite;

internal interface ISqliteTransactionCleanup
{
    public void Rollback(SqliteTransaction transaction);

    public void Dispose(SqliteTransaction transaction);
}

internal sealed class SqliteTransactionCleanup : ISqliteTransactionCleanup
{
    public static SqliteTransactionCleanup Instance { get; } = new();

    private SqliteTransactionCleanup()
    {
    }

    public void Rollback(SqliteTransaction transaction) => transaction.Rollback();

    public void Dispose(SqliteTransaction transaction) => transaction.Dispose();
}
