namespace IncidentReview.EventSync.Sqlite;

internal interface ISqliteEventInboxExecutionCheckpoint
{
    public void BeforePersist();
}

internal sealed class SqliteEventInboxExecutionCheckpoint : ISqliteEventInboxExecutionCheckpoint
{
    public static SqliteEventInboxExecutionCheckpoint Instance { get; } = new();

    private SqliteEventInboxExecutionCheckpoint()
    {
    }

    public void BeforePersist()
    {
    }
}
