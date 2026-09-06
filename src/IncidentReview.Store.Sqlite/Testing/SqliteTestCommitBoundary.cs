using Microsoft.Data.Sqlite;

namespace IncidentReview.Store.Sqlite.Testing;

internal sealed class SqliteTestCommitBoundary : ISqliteCommitBoundary
{
    private readonly SqliteTestCommitMode _mode;

    public SqliteTestCommitBoundary(SqliteTestCommitMode mode)
    {
        _mode = mode;
    }

    public void Commit(SqliteTransaction transaction)
    {
        switch (_mode)
        {
            case SqliteTestCommitMode.Normal:
                transaction.Commit();
                return;
            case SqliteTestCommitMode.IndeterminateBeforeCommit:
                throw new IndeterminateCommitException();
            case SqliteTestCommitMode.IndeterminateAfterCommit:
                transaction.Commit();
                throw new IndeterminateCommitException();
            default:
                throw new InvalidOperationException("The configured test commit mode is invalid.");
        }
    }
}
