namespace IncidentReview.Store.Sqlite.Testing;

/// <summary>Controls transaction-cleanup reporting in reliability tests.</summary>
public enum SqliteTestCleanupMode
{
    /// <summary>Reports the real rollback and disposal outcomes.</summary>
    Normal,

    /// <summary>Performs cleanup and then simulates failure reporting from each cleanup step.</summary>
    FailAfterCleanup,
}
