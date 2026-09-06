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
}
