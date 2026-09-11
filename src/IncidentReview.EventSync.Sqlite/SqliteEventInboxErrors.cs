using IncidentReview.Results;

namespace IncidentReview.EventSync.Sqlite;

internal static class SqliteEventInboxErrors
{
    public static Error InvalidOptions { get; } = Error.Create(
        SqliteEventInboxErrorCodes.InvalidOptions,
        ErrorKind.Validation,
        "The SQLite event-inbox configuration is invalid.");

    public static Error NotReady { get; } = Error.Create(
        SqliteEventInboxErrorCodes.NotReady,
        ErrorKind.Unavailable,
        "The SQLite event inbox is not ready to accept submissions.");

    public static Error CapacityExceeded { get; } = Error.Create(
        SqliteEventInboxErrorCodes.CapacityExceeded,
        ErrorKind.Unavailable,
        "The SQLite event inbox is temporarily at capacity.");

    public static Error InitializationFailed { get; } = Error.Create(
        SqliteEventInboxErrorCodes.InitializationFailed,
        ErrorKind.Persistence,
        "The SQLite event inbox could not be initialized.");

    public static Error PersistenceUnavailable { get; } = Error.Create(
        SqliteEventInboxErrorCodes.PersistenceUnavailable,
        ErrorKind.Persistence,
        "The custom event could not be written to the SQLite inbox.");

    public static Error CommitIndeterminate { get; } = Error.Create(
        SqliteEventInboxErrorCodes.CommitIndeterminate,
        ErrorKind.Indeterminate,
        "The SQLite inbox commit outcome is indeterminate.");
}
