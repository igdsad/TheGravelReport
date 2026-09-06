namespace IncidentReview.Store.Sqlite.Testing;

/// <summary>Controls the otherwise opaque native commit boundary in reliability tests.</summary>
public enum SqliteTestCommitMode
{
    /// <summary>Invokes the real native commit and returns its outcome.</summary>
    Normal,

    /// <summary>Reports an indeterminate outcome without invoking native commit.</summary>
    IndeterminateBeforeCommit,

    /// <summary>Commits natively and then withholds the successful outcome.</summary>
    IndeterminateAfterCommit,
}
