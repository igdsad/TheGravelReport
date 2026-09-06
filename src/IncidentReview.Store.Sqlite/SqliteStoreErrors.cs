using IncidentReview.Results;

namespace IncidentReview.Store.Sqlite;

internal static class SqliteStoreErrors
{
    public static Error DirectoryUnavailable { get; } = Error.Create(
        ErrorCode.Define("store.sqlite.directory-unavailable"),
        ErrorKind.Persistence,
        "The SQLite data directory is unavailable.");

    public static Error ProviderUnavailable { get; } = Error.Create(
        ErrorCode.Define("store.sqlite.provider-unavailable"),
        ErrorKind.Integration,
        "The Windows SQLite provider could not be initialized.");

    public static Error MigrationManifestInvalid { get; } = Error.Create(
        ErrorCode.Define("store.sqlite.migration-manifest-invalid"),
        ErrorKind.Persistence,
        "The embedded SQLite migration manifest is invalid.");

    public static Error MigrationFailed { get; } = Error.Create(
        ErrorCode.Define("store.sqlite.migration-failed"),
        ErrorKind.Persistence,
        "The SQLite schema migration failed.");

    public static Error SchemaInvalid { get; } = Error.Create(
        ErrorCode.Define("store.sqlite.schema-invalid"),
        ErrorKind.Persistence,
        "The SQLite schema or required database capabilities are invalid.");
}
