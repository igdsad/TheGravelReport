using Microsoft.Extensions.Logging;

namespace IncidentReview.EventSync.Sqlite;

internal static class SqliteEventInboxLog
{
    private static readonly Action<ILogger, Exception?> LogInitializationFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(5001, nameof(InitializationFailed)),
            "The SQLite custom-event inbox failed to initialize.");

    private static readonly Action<ILogger, Exception?> LogPersistenceFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(5002, nameof(PersistenceFailed)),
            "A custom-event inbox write failed before commit.");

    private static readonly Action<ILogger, Exception?> LogCommitIndeterminate =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(5003, nameof(CommitIndeterminate)),
            "A custom-event inbox commit ended without a provable outcome.");

    private static readonly Action<ILogger, Exception?> LogCleanupFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(5004, nameof(CleanupFailed)),
            "SQLite custom-event inbox transaction cleanup failed.");

    private static readonly Action<ILogger, Exception?> LogWorkerFailed =
        LoggerMessage.Define(
            LogLevel.Critical,
            new EventId(5005, nameof(WorkerFailed)),
            "The SQLite custom-event inbox worker stopped unexpectedly.");

    public static void InitializationFailed(ILogger logger, Exception exception) =>
        LogInitializationFailed(logger, exception);

    public static void PersistenceFailed(ILogger logger, Exception exception) =>
        LogPersistenceFailed(logger, exception);

    public static void CommitIndeterminate(ILogger logger, Exception exception) =>
        LogCommitIndeterminate(logger, exception);

    public static void CleanupFailed(ILogger logger, Exception exception) =>
        LogCleanupFailed(logger, exception);

    public static void WorkerFailed(ILogger logger, Exception exception) =>
        LogWorkerFailed(logger, exception);
}
