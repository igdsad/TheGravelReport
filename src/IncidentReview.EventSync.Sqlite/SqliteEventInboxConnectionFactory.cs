using IncidentReview.EventSync.Sqlite.Options;
using Microsoft.Data.Sqlite;

namespace IncidentReview.EventSync.Sqlite;

internal sealed class SqliteEventInboxConnectionFactory
{
    private readonly string _initializationConnectionString;
    private readonly string _runtimeConnectionString;

    public SqliteEventInboxConnectionFactory(SqliteEventInboxOptions options)
    {
        _initializationConnectionString = CreateConnectionString(
            options,
            SqliteOpenMode.ReadWriteCreate);
        _runtimeConnectionString = CreateConnectionString(
            options,
            SqliteOpenMode.ReadWrite);
    }

    public SqliteConnection CreateForInitialization() =>
        new(_initializationConnectionString);

    public SqliteConnection CreateForWrite() => new(_runtimeConnectionString);

    private static string CreateConnectionString(
        SqliteEventInboxOptions options,
        SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = options.BusyTimeoutSeconds,
            ForeignKeys = true,
        }.ToString();
}
