using Microsoft.Extensions.Logging;

namespace IncidentReview.Store.Sqlite;

internal static class SqliteLog
{
    private static readonly Action<ILogger, Exception?> LogMigrationManifestInvalid =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1001, nameof(MigrationManifestInvalid)),
            "The SQLite migration manifest failed validation.");

    private static readonly Action<ILogger, Exception?> LogMigrationFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1002, nameof(MigrationFailed)),
            "SQLite schema migration failed.");

    private static readonly Action<ILogger, string, Exception?> LogSchemaInvalid =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1003, nameof(SchemaInvalid)),
            "SQLite schema validation failed for {SchemaCheck}.");

    private static readonly Action<ILogger, Exception?> LogInitializationFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1004, nameof(InitializationFailed)),
            "SQLite initialization failed.");

    private static readonly Action<ILogger, Exception?> LogDirectoryUnavailable =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1005, nameof(DirectoryUnavailable)),
            "The SQLite data directory could not be prepared.");

    private static readonly Action<ILogger, Exception?> LogProviderUnavailable =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1006, nameof(ProviderUnavailable)),
            "The Windows SQLite provider could not be initialized.");

    private static readonly Action<ILogger, Exception?> LogStoreOperationFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1007, nameof(StoreOperationFailed)),
            "A SQLite store operation failed before commit.");

    private static readonly Action<ILogger, Exception?> LogCommitIndeterminate =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1008, nameof(CommitIndeterminate)),
            "A SQLite commit outcome is indeterminate.");

    private static readonly Action<ILogger, Exception?> LogStoreWorkerFailed =
        LoggerMessage.Define(
            LogLevel.Critical,
            new EventId(1009, nameof(StoreWorkerFailed)),
            "The SQLite store worker rejected an unexpected operation failure.");

    private static readonly Action<ILogger, Exception?> LogTransactionCleanupFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1010, nameof(TransactionCleanupFailed)),
            "SQLite transaction cleanup failed after an operation outcome was selected.");

    public static void MigrationManifestInvalid(ILogger logger, Exception exception) =>
        LogMigrationManifestInvalid(logger, exception);

    public static void MigrationFailed(ILogger logger, Exception? exception) =>
        LogMigrationFailed(logger, exception);

    public static void SchemaInvalid(ILogger logger, string check, Exception? exception) =>
        LogSchemaInvalid(logger, check, exception);

    public static void InitializationFailed(ILogger logger, Exception exception) =>
        LogInitializationFailed(logger, exception);

    public static void DirectoryUnavailable(ILogger logger, Exception exception) =>
        LogDirectoryUnavailable(logger, exception);

    public static void ProviderUnavailable(ILogger logger, Exception exception) =>
        LogProviderUnavailable(logger, exception);

    public static void StoreOperationFailed(ILogger logger, Exception exception) =>
        LogStoreOperationFailed(logger, exception);

    public static void CommitIndeterminate(ILogger logger, Exception exception) =>
        LogCommitIndeterminate(logger, exception);

    public static void StoreWorkerFailed(ILogger logger, Exception exception) =>
        LogStoreWorkerFailed(logger, exception);

    public static void TransactionCleanupFailed(ILogger logger, Exception exception) =>
        LogTransactionCleanupFailed(logger, exception);
}
