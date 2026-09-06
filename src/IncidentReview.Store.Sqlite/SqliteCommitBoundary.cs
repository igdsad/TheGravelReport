using Microsoft.Data.Sqlite;

namespace IncidentReview.Store.Sqlite;

internal interface ISqliteCommitBoundary
{
    public void Commit(SqliteTransaction transaction);
}

internal sealed class SqliteCommitBoundary : ISqliteCommitBoundary
{
    public static SqliteCommitBoundary Instance { get; } = new();

    private SqliteCommitBoundary()
    {
    }

    public void Commit(SqliteTransaction transaction) => transaction.Commit();
}

internal sealed class IndeterminateCommitException : Exception
{
    public IndeterminateCommitException()
        : base("The test commit boundary withheld the native commit outcome.")
    {
    }
}
