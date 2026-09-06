using IncidentReview.Store.Sqlite.Options;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IncidentReview.Store.Sqlite.Tests;

internal sealed class TemporarySqliteDatabase : IDisposable
{
    private readonly string _directoryPath = Path.Combine(
        Path.GetTempPath(),
        "IncidentReview.Tests",
        Guid.NewGuid().ToString("N"));

    public TemporarySqliteDatabase(int executorCapacity = 64)
    {
        DatabasePath = Path.Combine(_directoryPath, "incident-review.db");
        var options = SqliteStoreOptions.TryCreate(
            DatabasePath,
            executorCapacity: executorCapacity);
        Assert.IsTrue(options.IsSuccess);
        Options = options.Value;
    }

    public string DatabasePath { get; }

    public SqliteStoreOptions Options { get; }

    public SqliteConnection CreateConnection()
    {
        _ = Directory.CreateDirectory(_directoryPath);
        return OpenConnection(SqliteOpenMode.ReadWriteCreate);
    }

    public SqliteConnection OpenConnection() => OpenConnection(SqliteOpenMode.ReadWrite);

    private SqliteConnection OpenConnection(SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }
}
