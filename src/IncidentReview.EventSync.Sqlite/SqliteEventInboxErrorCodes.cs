using IncidentReview.Results;

namespace IncidentReview.EventSync.Sqlite;

/// <summary>Stable error codes owned by the SQLite custom-event inbox adapter.</summary>
public static class SqliteEventInboxErrorCodes
{
    /// <summary>The inbox configuration is invalid.</summary>
    public static ErrorCode InvalidOptions { get; } =
        ErrorCode.Define("event-sync.sqlite.options-invalid");

    /// <summary>The inbox has not completed startup or is stopping.</summary>
    public static ErrorCode NotReady { get; } =
        ErrorCode.Define("event-sync.sqlite.not-ready");

    /// <summary>The bounded inbox executor cannot admit another write.</summary>
    public static ErrorCode CapacityExceeded { get; } =
        ErrorCode.Define("event-sync.sqlite.capacity-exceeded");

    /// <summary>The inbox database could not be initialized.</summary>
    public static ErrorCode InitializationFailed { get; } =
        ErrorCode.Define("event-sync.sqlite.initialization-failed");

    /// <summary>A write failed before a commit was attempted.</summary>
    public static ErrorCode PersistenceUnavailable { get; } =
        ErrorCode.Define("event-sync.sqlite.persistence-unavailable");

    /// <summary>A commit failed without proving whether it took effect.</summary>
    public static ErrorCode CommitIndeterminate { get; } =
        ErrorCode.Define("event-sync.sqlite.commit-indeterminate");
}
