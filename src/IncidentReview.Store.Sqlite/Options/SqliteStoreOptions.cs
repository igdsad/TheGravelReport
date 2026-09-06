using IncidentReview.Results;

namespace IncidentReview.Store.Sqlite.Options;

/// <summary>Validated startup configuration for the local SQLite store.</summary>
public sealed class SqliteStoreOptions
{
    private static readonly Error InvalidOptionsError = Error.Create(
        ErrorCode.Define("store.sqlite.options-invalid"),
        ErrorKind.Validation,
        "The SQLite store configuration is invalid.");

    private SqliteStoreOptions(string databasePath, int busyTimeoutSeconds)
    {
        DatabasePath = databasePath;
        BusyTimeoutSeconds = busyTimeoutSeconds;
    }

    /// <summary>Gets the absolute local database file path.</summary>
    public string DatabasePath { get; }

    /// <summary>Gets the finite SQLite busy timeout in seconds.</summary>
    public int BusyTimeoutSeconds { get; }

    /// <summary>Validates and normalizes SQLite startup configuration.</summary>
    public static Result<SqliteStoreOptions> TryCreate(
        string? databasePath,
        int busyTimeoutSeconds = 5)
    {
        if (string.IsNullOrWhiteSpace(databasePath)
            || busyTimeoutSeconds is < 1 or > 60
            || !Path.IsPathFullyQualified(databasePath))
        {
            return Result<SqliteStoreOptions>.Failure(InvalidOptionsError);
        }

        try
        {
            var fullPath = Path.GetFullPath(databasePath);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root)
                || string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                || Path.EndsInDirectorySeparator(fullPath)
                || new Uri(fullPath).IsUnc)
            {
                return Result<SqliteStoreOptions>.Failure(InvalidOptionsError);
            }

            return Result<SqliteStoreOptions>.Success(
                new SqliteStoreOptions(fullPath, busyTimeoutSeconds));
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or UriFormatException)
        {
            return Result<SqliteStoreOptions>.Failure(InvalidOptionsError);
        }
    }
}
