using IncidentReview.Store.Sqlite.Options;
using Microsoft.Data.Sqlite;

namespace IncidentReview.Store.Sqlite;

internal sealed class SqliteConnectionFactory
{
    private readonly string _initializationConnectionString;
    private readonly string _readOnlyConnectionString;
    private readonly string _runtimeConnectionString;

    public SqliteConnectionFactory(SqliteStoreOptions options)
    {
        _initializationConnectionString = CreateConnectionString(
            options,
            SqliteOpenMode.ReadWriteCreate,
            foreignKeys: true);
        _readOnlyConnectionString = CreateConnectionString(
            options,
            SqliteOpenMode.ReadOnly,
            foreignKeys: null);
        _runtimeConnectionString = CreateConnectionString(
            options,
            SqliteOpenMode.ReadWrite,
            foreignKeys: true);
    }

    public SqliteConnection CreateOpen() => CreateOpen(_runtimeConnectionString, applyPragmas: true);

    public SqliteConnection CreateOpenForInitialization() =>
        CreateOpen(_initializationConnectionString, applyPragmas: true);

    public SqliteConnection CreateOpenReadOnly() =>
        CreateOpen(_readOnlyConnectionString, applyPragmas: false);

    private static string CreateConnectionString(
        SqliteStoreOptions options,
        SqliteOpenMode mode,
        bool? foreignKeys)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = options.BusyTimeoutSeconds,
        };
        if (foreignKeys.HasValue)
        {
            builder.ForeignKeys = foreignKeys.Value;
        }

        return builder.ToString();
    }

    private static SqliteConnection CreateOpen(string connectionString, bool applyPragmas)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            if (applyPragmas)
            {
                ExecutePragma(connection, "PRAGMA foreign_keys = ON;");
                ExecutePragma(connection, "PRAGMA journal_mode = DELETE;");
                ExecutePragma(connection, "PRAGMA synchronous = FULL;");
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void ExecutePragma(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteScalar();
    }
}
