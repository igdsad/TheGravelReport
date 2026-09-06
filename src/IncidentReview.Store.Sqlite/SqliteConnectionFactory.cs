using IncidentReview.Store.Sqlite.Options;
using Microsoft.Data.Sqlite;

namespace IncidentReview.Store.Sqlite;

internal sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(SqliteStoreOptions options)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = options.BusyTimeoutSeconds,
        };

        _connectionString = builder.ToString();
    }

    public SqliteConnection CreateOpen()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            ExecutePragma(connection, "PRAGMA foreign_keys = ON;");
            ExecutePragma(connection, "PRAGMA journal_mode = DELETE;");
            ExecutePragma(connection, "PRAGMA synchronous = FULL;");
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
