using IncidentReview.Results;

namespace IncidentReview.EventSync.Sqlite.Options;

/// <summary>Validated startup configuration for the server-side custom-event inbox.</summary>
public sealed record SqliteEventInboxOptions
{
    private const int MaximumExecutorCapacity = 4096;

    private SqliteEventInboxOptions(
        string databasePath,
        int busyTimeoutSeconds,
        int executorCapacity)
    {
        DatabasePath = databasePath;
        BusyTimeoutSeconds = busyTimeoutSeconds;
        ExecutorCapacity = executorCapacity;
    }

    /// <summary>Gets the absolute path of the durable inbox database.</summary>
    public string DatabasePath { get; }

    /// <summary>Gets the finite SQLite lock wait in seconds.</summary>
    public int BusyTimeoutSeconds { get; }

    /// <summary>Gets the maximum number of queued inbox writes.</summary>
    public int ExecutorCapacity { get; }

    /// <summary>Validates and normalizes inbox configuration.</summary>
    public static Result<SqliteEventInboxOptions> TryCreate(
        string? databasePath,
        int busyTimeoutSeconds = 5,
        int executorCapacity = 256)
    {
        if (string.IsNullOrWhiteSpace(databasePath)
            || busyTimeoutSeconds is < 1 or > 60
            || executorCapacity is < 1 or > MaximumExecutorCapacity
            || !Path.IsPathFullyQualified(databasePath))
        {
            return Result<SqliteEventInboxOptions>.Failure(SqliteEventInboxErrors.InvalidOptions);
        }

        try
        {
            var fullPath = Path.GetFullPath(databasePath);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root)
                || string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                || Path.EndsInDirectorySeparator(fullPath))
            {
                return Result<SqliteEventInboxOptions>.Failure(SqliteEventInboxErrors.InvalidOptions);
            }

            return Result<SqliteEventInboxOptions>.Success(
                new SqliteEventInboxOptions(fullPath, busyTimeoutSeconds, executorCapacity));
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return Result<SqliteEventInboxOptions>.Failure(SqliteEventInboxErrors.InvalidOptions);
        }
    }
}
